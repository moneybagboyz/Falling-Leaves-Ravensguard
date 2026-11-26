using Ravensguard.Content;
using Ravensguard.Sim.Abstract;
using Ravensguard.Sim.Contracts;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Rule;
using Ravensguard.Sim.Save;
using Ravensguard.Sim.Society;
using Ravensguard.Sim.Worlds;

namespace Ravensguard.Sim.Politics;

/// <summary>
/// Politics' place in the abstract tick (diplomacy-and-ai-lords 4.6): the opinions' modifiers fade at hour 0 of a season's first day (H5); at the season's first day (H6, the slice <c>S10Politics</c> after the continent's summary)
/// the realms' diplomacy and the manors' rents run at hour 2 and each fief's lord thinks at his own hour (one fief an hour). A no-op in a world without a <see cref="PoliticsBook"/> and a <see cref="RuleBook"/>.
/// </summary>
[AbstractSystem]
public sealed class PoliticsSystem : AbstractSystem
{
    public PoliticsSystem() : base("politics", TickPhase.H5DailyGlobals | TickPhase.H6Seasonal, SystemOrder.S10Politics) { }

    public override void Step(TickContext ctx)
    {
        if (!ctx.IsSeasonStartDay) return;
        var pb = ctx.World.Get<PoliticsBook>(); var rb = ctx.World.Get<RuleBook>(); if (pb == null || rb == null) return;
        var c = PoliticsEngine.Context(ctx.World, pb, rb, RuleChron.Of(ctx.Outbox), (int)ctx.Day);
        if (ctx.Phase == TickPhase.H5DailyGlobals) { if (ctx.HourOfDay == 0) PoliticsEngine.Decay(c); }
        else PoliticsEngine.SeasonHour(c, ctx.HourOfDay);
    }
}

/// <summary>The politics' passes, callable by a test with a hand-built world and by <see cref="PoliticsSystem"/> on the tick.</summary>
public static class PoliticsEngine
{
    public static PoliticsCtx Context(SimWorld w, PoliticsBook pb, RuleBook rb, RuleChron chron, int day)
    {
        var links = w.Get<PoliticsLinks>() ?? new PoliticsLinks();
        var rc = RuleWiring.Context(w, rb, chron);
        var aw = w.Get<Armies.ArmyWorld>();
        return new PoliticsCtx { B = pb, R = rc, H = links.History, Links = links, Day = day, OnMap = aw == null ? null : aw.ScoreFor, OnScored = aw == null ? null : aw.ScoredWar };
    }

    /// <summary>The season's fading: each modifier moves toward zero by its kind; a war's grudge holds while its war lasts. Once for a day number.</summary>
    public static void Decay(PoliticsCtx c)
    {
        if (c.B.LastDecayDay == c.Day) return;
        Opinions.Decay(c.B, c.RB, m => m.Kind == "war" && AtWarKeys(c, m.A, m.B));
        c.B.LastDecayDay = c.Day;
    }

    /// <summary>Whether the two parties of a war grudge are (the realms of) belligerents still at war.</summary>
    public static bool AtWarKeys(PoliticsCtx c, ulong a, ulong b)
    {
        int ra = Diplomacy.RealmOfKey(a), rb = Diplomacy.RealmOfKey(b);
        if (ra >= 0 && rb >= 0) return c.B.WarOf(ra, rb) != null;
        // a lord's grudge against the captor's realm lasts while his own realm fights it
        if (rb >= 0) foreach (var f in c.RB.Fiefs.Values) if (f.Holder == (long)a && f.Realm >= 0 && c.B.WarOf(f.Realm, rb) != null) return true;
        return false;
    }

    /// <summary>One hour of a season's first day: the realms at 2, the lords at their own hours.</summary>
    public static void SeasonHour(PoliticsCtx c, int hour)
    {
        if (hour == 2) { Diplomacy.Season(c); Road.ManorRents(c); }
        LordBrain.Slice(c, hour);
    }

    /// <summary>The whole season at once (a test's, a catch-up's): decay, the realms, every lord in fief order.</summary>
    public static void WholeSeason(PoliticsCtx c)
    {
        Decay(c); Diplomacy.Season(c); Road.ManorRents(c);
        foreach (var f in c.RB.Fiefs.Values.ToList()) LordBrain.Think(c, f);
    }

    /// <summary>What the rule's councils ask: whether (and why) a realm is at war: "defence" for the one attacked, "casus_belli" for the one that declared.</summary>
    public static string WarCause(PoliticsBook pb, int realm) => pb.WarOf(realm) is { } w ? (w.Defender == realm ? "defence" : "casus_belli") : "";
}

/// <summary>Installs the politics on the running world (the P6-D2 hook in <see cref="WorldRuntime"/>): the book of the world state, its numbers bound from content, the history the realms come from and the hero's side of the links.</summary>
public static class PoliticsWiring
{
    public static PoliticsBook Bind(PoliticsBook b, ContentDatabase? db, ulong seed)
    {
        b.Seed = seed; if (db == null) return b;
        var s = db.All<PoliticsSettingsDef>().OrderBy(x => x.Id, StringComparer.Ordinal).FirstOrDefault() ?? b.Settings;
        if (db.All<CaptivityRulesDef>().OrderBy(x => x.Id, StringComparer.Ordinal).FirstOrDefault() is { } cap) s = s with { LordRansomMites = cap.RankBaron };
        b.Settings = s;
        b.OpinionKinds = db.All<OpinionKindDef>().ToDictionary(k => k.Id, StringComparer.Ordinal);
        b.Actions = [.. db.All<LordActionDef>().OrderBy(a => a.Id, StringComparer.Ordinal)];
        return b;
    }

    public static PoliticsBook? Attach(RegionWorld home, WorldState state, ContentDatabase content)
    {
        if (state.Rule == null) return null;                                                // no fiefs, no politics
        var book = state.Politics ??= new PoliticsBook();
        Bind(book, content, state.Seed);
        var sim = home.Sim; sim.Set(book);
        var links = sim.Get<PoliticsLinks>() ?? new PoliticsLinks(); sim.Set(links);
        links.History = state.History;
        links.IsHero = id => state.Party != null && state.Party.Hero.Value == id;
        links.HeroName = () => state.Hero?.Name ?? "";
        links.StandingWithFief = (p, fief) => Standing.Of(state.Standing, p, GroupRef.Fief(fief));
        links.RenownPm = p => state.Standing != null && state.Standing.Renowns.TryGetValue(p.Value, out var r) ? r.Pm : 0;
        links.ContractsDone = (p, fief) => ContractsDoneIn(state, p, fief);
        if (sim.Get<RuleLinks>() is { } rl) rl.WarCause = realm => PoliticsEngine.WarCause(book, realm);
        return book;
    }

    /// <summary>Contracts a person completed in the settlements of a fief (the work that earns a lord's trust).</summary>
    public static int ContractsDoneIn(WorldState st, EntityId person, int fief)
    {
        var cb = st.Contracts; if (cb == null) return 0;
        var region = st.Population.Region; int n = 0;
        foreach (var k in cb.Contracts.Values)
            if (k.State == WorkState.Done && k.Taker == person && k.Settlement >= 0 && k.Settlement < region.Settlements.Count && region.Settlements[k.Settlement].FiefIndex == fief) n++;
        return n;
    }
}
