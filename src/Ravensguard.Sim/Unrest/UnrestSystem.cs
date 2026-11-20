using Ravensguard.Content;
using Ravensguard.Sim.Abstract;
using Ravensguard.Sim.Combat.Aftermath;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Law;
using Ravensguard.Sim.Politics;
using Ravensguard.Sim.Rule;
using Ravensguard.Sim.Save;
using Ravensguard.Sim.Society;
using Ravensguard.Sim.Worlds;

namespace Ravensguard.Sim.Unrest;

/// <summary>
/// Unrest's place in the abstract tick (unrest-and-revolt 4.5): the daily pass at hour 0 of H5 (after the tax's own, which sets the economy's grievance flags), and every hour of H4 the riots that gather or act and the revolts whose day has
/// come. A no-op in a world without an <see cref="UnrestBook"/> and a <see cref="RuleBook"/>; free while nothing is rising.
/// </summary>
[AbstractSystem]
public sealed class UnrestSystem : AbstractSystem
{
    public UnrestSystem() : base("unrest", TickPhase.H4DailySlice | TickPhase.H5DailyGlobals, SystemOrder.DailyGlobalsTaxes + 10) { }

    public override void Step(TickContext ctx)
    {
        var b = ctx.World.Get<UnrestBook>(); var rb = ctx.World.Get<RuleBook>(); if (b == null || rb == null) return;
        if (ctx.Phase == TickPhase.H5DailyGlobals) { if (ctx.HourOfDay != 0) return; UnrestEngine.Daily(UnrestWiring.Context(ctx.World, b, rb, ctx.Outbox, (int)ctx.Day, ctx.Hour)); return; }
        if (b.Riots.Count == 0 && b.Revolts.Count == 0) return;
        if (!b.Riots.Any(r => r.State != RiotState.Ended) && !b.Revolts.Any(v => v.State == RevoltState.Pending)) return;
        UnrestEngine.Hourly(UnrestWiring.Context(ctx.World, b, rb, ctx.Outbox, (int)ctx.Day, ctx.Hour));
    }
}

/// <summary>Installs unrest on the home region (the P6-E1 hook in <see cref="WorldRuntime"/>): the book of the world state with its content bound, the hooks to the law, the standing book and the hearth tax.</summary>
public static class UnrestWiring
{
    public static UnrestBook Bind(UnrestBook b, ContentDatabase? db, ulong seed)
    {
        b.Seed = seed; if (db == null) return b;
        b.Settings = db.All<UnrestSettingsDef>().OrderBy(x => x.Id, StringComparer.Ordinal).FirstOrDefault() ?? b.Settings;
        var kinds = db.All<GrievanceKindDef>().OrderBy(x => x.Id, StringComparer.Ordinal).ToList();
        var order = new[] { "hunger", "tax_high", "tax_crushing", "levy", "crowding", "crime", "kin_punished", "wronged", "standing", "banned", "enclosure", "unpaid_wages", "eviction", "slavery", "purveyance" };
        var sorted = kinds.OrderBy(k => { int i = Array.IndexOf(order, k.Id); return i < 0 ? order.Length : i; }).ThenBy(k => k.Id, StringComparer.Ordinal).ToList();
        b.KindList = sorted; b.Kinds = sorted.ToDictionary(k => k.Id, StringComparer.Ordinal);
        return b;
    }

    /// <summary>The context of a tick or a barrier: the book, the rule's context over the sim world, the links the wiring installed.</summary>
    public static UnrestCtx Context(SimWorld w, UnrestBook b, RuleBook rb, RegionOutbox? ob, int day, long hour = 0)
    {
        var chron = ob != null ? RuleChron.Of(ob) : RuleChron.Silent;
        return new UnrestCtx { B = b, R = RuleWiring.Context(w, rb, chron), Ob = ob, Links = w.Get<UnrestLinks>() ?? new UnrestLinks(), PB = w.Get<PoliticsBook>(), Day = day, Hour = hour };
    }

    /// <summary>The unrest context behind a politics context (the lord's actions at a season's start), or null when the world has no unrest book.</summary>
    public static UnrestCtx? CtxFor(PoliticsCtx pc)
    {
        var b = pc.R.W.Get<UnrestBook>(); if (b == null) return null;
        return new UnrestCtx { B = b, R = pc.R, Ob = null, Links = pc.R.W.Get<UnrestLinks>() ?? new UnrestLinks(), PB = pc.B, Day = pc.Day };
    }

    public static UnrestBook? Attach(RegionWorld home, WorldState state, ContentDatabase content)
    {
        if (state.Rule == null) return null;                                                // no fiefs, no ladder
        var book = state.Unrest ??= new UnrestBook();
        Bind(book, content, state.Seed);
        var sim = home.Sim; sim.Set(book);
        var links = sim.Get<UnrestLinks>() ?? new UnrestLinks(); sim.Set(links);
        links.IsHero = id => state.Party != null && state.Party.Hero.Value == id;
        links.StandingWithSettlement = (p, s) => Standing.Of(state.Standing, p, GroupRef.Settlement(s));
        links.Deed = (doer, deedId, entry, place, day) => { if (state.Content != null) Ravensguard.Sim.Faith.FaithStanding.Record(state, state.Content, doer, deedId, entry, place, 1, -1, false, day); };
        links.Crime = rc =>
        {
            var law = LawWorld.From(state, state.Content);
            var crimes = state.Law ??= new CrimeBook();
            var rows = rc.Witnesses.Select(w => new WitnessRow(w.Value, WitnessRow.KnewIt)).ToList();
            var rec = CrimeSystem.Commit(law, crimes, new ActSpec(rc.Kind, rc.Doer, rc.Victim, rc.Settlement, rc.Sec, rc.Value, 0, rc.NoteKey, [ChronParam.Entry("riot", new EntityId(rc.Riot))]), rows, ActorLook.None);
            if (rc.Report && rc.Witnesses.Count > 0) CrimeSystem.Report(law, rec, rc.Witnesses[0], rc.Sec);
            return new EntityId(rec.Entry);
        };
        links.Pardon = person =>
        {
            if (state.Law == null) return;
            foreach (var c in state.Law.Open(person).ToList()) c.State = CrimeState.Pardoned;
        };
        links.Jailbreak = (s, sec) =>
        {
            var jb = state.Justice; if (jb == null) return [];
            var law = LawWorld.From(state, state.Content);
            var out_ = new List<EntityId>();
            foreach (var row in jb.Gaol.Values.Where(r => r.Settlement == s).OrderBy(r => r.Person).ToList())
            { Gaol.Free(law, jb, new EntityId(row.Person), CaptiveState.Rescued, "", sec, "ch_note_ur_jailbreak", state.Law); out_.Add(new EntityId(row.Person)); }
            return out_;
        };
        if (home.Economy.Taxes is { } tax)
        {
            tax.RiotsElsewhere = true;
            tax.EvasionHook = (s, h, day) => UnrestEngine.Evades(Context(sim, book, state.Rule, null, day), s, h);
            tax.Evicted = (s, hh, entry, day) => UnrestConcede.Evicted(book, sim, s, hh, day, entry.Value);
        }
        return book;
    }

    /// <summary>D1's hook (slavery 4.5): a holding's bonded have risen and been set free; the rising is raised as a revolt of the bonded, to be fought by the guards on the next day. A no-op without a rule book.</summary>
    public static void SlaveRising(WorldState st, int place, ulong leader, IReadOnlyList<ulong> bonded, ulong entry, int day)
    {
        if (st.Rule == null || st.HomeRecords == null) return;
        var book = st.Unrest ??= new UnrestBook(); Bind(book, st.Content, st.Seed);
        var sim = st.HomeRecords.Sim;
        var c = new UnrestCtx { B = book, R = RuleWiring.Context(sim, st.Rule, RuleChron.Of(st.Chronicle, st.Ids)), Ob = null, Links = sim.Get<UnrestLinks>() ?? new UnrestLinks(), PB = st.Politics, Day = day };
        UnrestEngine.BeginSlaveRising(c, place, leader, bonded, entry);
    }
}
