using System.Globalization;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Rule;
using Ravensguard.Sim.Save;
using Ravensguard.Sim.Society;
using Ravensguard.Sim.Trade;

namespace Ravensguard.Sim.Politics;

/// <summary>
/// The hero's acts of politics at the barrier (diplomacy-and-ai-lords 4.3, 6; recorded for replay like every hero command, the tag <c>dp:</c>): swear fealty to the lord of a fief, ask him for a manor, ask the ruler for a vacant fief,
/// give a gift to a lord or a realm, and renounce his oath. Each answer is a text key the Faction screen shows; a refusal names the term that failed ("he thinks little of you").
/// </summary>
public static class PoliticsDesk
{
    public const string Prefix = "dp:";
    private static string N(long v) => v.ToString(CultureInfo.InvariantCulture);
    private static HeroCommand Make(string tag, long sec) => new() { Kind = HeroCommandKind.Diplomacy, IssuedSec = sec, Origin = CommandOrigin.Human, Tag = Prefix + tag };

    public static HeroCommand Swear(int fief, long sec) => Make("swear|" + N(fief), sec);
    public static HeroCommand Manor(int fief, long sec) => Make("manor|" + N(fief), sec);
    public static HeroCommand AskFief(int fief, long sec) => Make("fief|" + N(fief), sec);
    public static HeroCommand GiftLord(int fief, long mites, long sec) => Make("gift|" + N(fief) + "|" + N(mites), sec);
    public static HeroCommand GiftRealm(int realm, long mites, long sec) => Make("giftr|" + N(realm) + "|" + N(mites), sec);
    public static HeroCommand Renounce(long sec) => Make("renounce", sec);

    private static HeroResult R(HeroCommand c, HeroResultCode code, string key, params object[] args) => new(c.Seq, c.Kind, code, HeroText.Of(key, args), 0, 0, c.IssuedSec);

    public static HeroResult Apply(HeroContext ctx, HeroCommand c)
    {
        if (!c.Tag.StartsWith(Prefix, StringComparison.Ordinal)) return R(c, HeroResultCode.Invalid, "HERO_UNKNOWN_COMMAND");
        var st = ctx.State; var sim = st.HomeRecords?.Sim; var rule = RuleWiring.Bind(st, ctx.Content);
        if (sim == null || rule == null) return R(c, HeroResultCode.Refused, "dp_no_politics");
        var book = st.Politics ??= new PoliticsBook(); PoliticsWiring.Bind(book, ctx.Content, st.Seed);
        int day = (int)(c.IssuedSec / 86400); var hero = ctx.Party.Hero;
        var pc = PoliticsEngine.Context(sim, book, rule, RuleChron.Of(st.Chronicle, st.Ids), day);
        var f = c.Tag[Prefix.Length..].Split('|');
        long A(int i) => f.Length > i && long.TryParse(f[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out long v) ? v : -1;
        switch (f[0])
        {
            case "swear": { var (ok, key) = Road.Swear(pc, hero, (int)A(1)); return ok ? R(c, HeroResultCode.Ok, key) : R(c, HeroResultCode.Refused, "dp_refused_" + key); }
            case "manor":
            {
                var (ok, key, s) = Road.GrantManor(pc, hero, (int)A(1));
                return ok ? R(c, HeroResultCode.Ok, key, pc.R.Region.Settlements[s].Name) : R(c, HeroResultCode.Refused, "dp_refused_" + key);
            }
            case "fief": { var (ok, key) = Road.AskFief(pc, hero, (int)A(1)); return ok ? R(c, HeroResultCode.Ok, key, pc.R.FiefName((int)A(1))) : R(c, HeroResultCode.Refused, "dp_refused_" + key); }
            case "gift":
            {
                var fr = rule.FiefOf((int)A(1)); if (fr == null || fr.Holder < 0) return R(c, HeroResultCode.Refused, "dp_no_lord");
                var (ok, key, pts) = Road.Gift(pc, hero, pc.R.HouseholdOf(fr.Holder), (ulong)fr.Holder, A(2));
                return ok ? new HeroResult(c.Seq, c.Kind, HeroResultCode.Ok, HeroText.Of(key, pts), A(2), 0, c.IssuedSec) : R(c, key.EndsWith("afford") ? HeroResultCode.CannotAfford : HeroResultCode.Refused, key);
            }
            case "giftr":
            {
                int realm = (int)A(1); if (!pc.Alive(realm)) return R(c, HeroResultCode.Refused, "dp_no_lord");
                if (pc.R.PurseOf(Party.OfRealm(realm)).IsNone) return R(c, HeroResultCode.Refused, "dp_no_lord");
                var (ok, key, pts) = Road.Gift(pc, hero, Party.OfRealm(realm), pc.KeyOf(realm), A(2));
                return ok ? new HeroResult(c.Seq, c.Kind, HeroResultCode.Ok, HeroText.Of(key, pts), A(2), 0, c.IssuedSec) : R(c, key.EndsWith("afford") ? HeroResultCode.CannotAfford : HeroResultCode.Refused, key);
            }
            case "renounce":
            {
                int idx = Road.Renounce(pc, hero.Value);
                return idx < 0 ? R(c, HeroResultCode.Refused, "dp_not_a_lord") : R(c, HeroResultCode.Ok, "dp_renounced", pc.NameOf(idx));
            }
        }
        return R(c, HeroResultCode.Invalid, "HERO_MAKES_NO_SENSE");
    }
}

// ====================================================================== the Faction screen's view

public sealed record FiefLine(int Fief, string Name, string Holder, string Realm, string TreasuryKey, bool Vacant, bool Mine);
public sealed record OpinionLine(string Name, ulong Id, int Value, IReadOnlyList<(string KindKey, int Amount, int Day, ulong Cause)> Mods);
public sealed record PactLine(string A, string B, string KindKey, int ExpiryDay, long Tribute);
public sealed record WarLine(string A, string B, string CbKey, int Score, int Seasons, int WearinessA, int WearinessD, int StartDay);
public sealed record ActionLine(string Id, int Target, string TargetName, bool Ok, IReadOnlyList<Term> Terms);
public sealed record LogLine(int Day, string Lord, string ActionKey, string NoteKey, ulong Entry);

/// <summary>
/// The Faction screen's data (diplomacy 6), built at a barrier: the realm (its ruler, the liege chain, vassals, fiefs and the treasury's band), the people who matter and why they think as they do, treaties and wars, the hero's standing
/// (the same lines as the Standing tab) and the actions open to him, each with the terms in words. The screen only sorts and draws.
/// </summary>
public sealed record FactionView(int Day, string RankKey, string RealmName, string RulerName, IReadOnlyList<string> Chain, IReadOnlyList<FiefLine> Fiefs, IReadOnlyList<string> Vassals,
    IReadOnlyList<OpinionLine> Regard, IReadOnlyList<PactLine> Treaties, IReadOnlyList<WarLine> Wars, IReadOnlyList<LogLine> Log, IReadOnlyList<ActionLine> Actions, StandingView Standing)
{
    public static readonly FactionView Empty = new(0, "dp_rank_wanderer", "", "", [], [], [], [], [], [], [], [], StandingView.Empty);

    private static string Band(PoliticsCtx c, FiefRow f)
    {
        long pay = c.R.Tax?.PayrollSeason(f.Fief) ?? 0; long bal = c.R.Balance(Party.OfFief(f.Fief));
        if (pay <= 0) return bal > 0 ? "dp_band_comfortable" : "dp_band_poor";
        long seasons = bal / pay;
        return seasons < 1 ? "dp_band_poor" : seasons < 3 ? "dp_band_modest" : seasons < 6 ? "dp_band_comfortable" : "dp_band_rich";
    }

    private static string RulerNameOf(PoliticsCtx c, int realm)
    {
        ulong r = c.RulerOf(realm); if (r == 0) return "";
        if (c.H?.GetNotable(new EntityId(r)) is { } n) return n.Full;
        return c.R.NameOf((long)r);
    }

    private static int HomeFief(WorldState st)
    {
        var region = st.Population.Region; var pt = st.Population.People;
        int row = Ravensguard.Sim.Succession.HeroPerson.RowOf(pt, st.Hero, st.Party?.Hero ?? EntityId.None);
        int s = row >= 0 ? pt.HomeSettlement[row] : -1;
        int f = s >= 0 && s < region.Settlements.Count ? region.Settlements[s].FiefIndex : -1;
        return f >= 0 && st.Rule!.Fiefs.ContainsKey(f) ? f : st.Rule!.Fiefs.Keys.FirstOrDefault(-1);
    }

    public static FactionView Of(WorldState st, DeedTable deeds, int day)
    {
        if (st.Rule == null || st.HomeRecords == null || st.Party == null) return Empty with { Day = day };
        var sim = st.HomeRecords.Sim; var hero = st.Party.Hero;
        var book = st.Politics ?? new PoliticsBook(); PoliticsWiring.Bind(book, st.Content, st.Seed);
        var pc = PoliticsEngine.Context(sim, book, st.Rule, RuleChron.Silent, day);
        var rb = st.Rule; ulong heroId = hero.Value;
        int home = HomeFief(st); var homeRow = home >= 0 ? rb.FiefOf(home) : null;
        int realm = homeRow?.Realm ?? -1;

        // the hero's rung
        bool holds = rb.Fiefs.Values.Any(f => f.Holder == (long)heroId && !f.Forfeited);
        bool founder = rb.Rulers.Any(kv => kv.Value == (long)heroId && book.RealmIfAny(kv.Key) is { Founded: true });
        bool manor = book.Manors.Any(m => m.Holder == (long)heroId);
        bool sworn = book.OathOf(heroId) != null;
        bool worked = st.Contracts != null && st.Contracts.Contracts.Values.Any(k => k.State == Ravensguard.Sim.Contracts.WorkState.Done && k.Taker == hero);
        string rank = founder ? "dp_rank_founder" : holds ? "dp_rank_lord" : sworn || manor ? "dp_rank_vassal" : worked ? "dp_rank_sellsword" : "dp_rank_wanderer";

        // the realm
        var chain = new List<string>();
        if (homeRow != null) { chain.Add(pc.R.FiefName(home)); if (realm >= 0) chain.Add(pc.NameOf(realm)); }
        foreach (var p in book.Pacts) if (!p.Ended && p.Kind == PactKind.Vassalage && p.B == realm) chain.Add(pc.NameOf(p.A));
        var fiefs = new List<FiefLine>();
        foreach (var f in rb.Fiefs.Values)
        {
            if (realm >= 0 && f.Realm != realm) continue;
            fiefs.Add(new FiefLine(f.Fief, pc.R.FiefName(f.Fief), f.Holder >= 0 && !f.Forfeited ? pc.R.NameOf(f.Holder) : "", f.Realm >= 0 ? pc.NameOf(f.Realm) : "", Band(pc, f), Road.Vacant(f), f.Holder == (long)heroId));
        }
        var vassals = new List<string>();
        foreach (var o in book.Oaths) if (o.Liege == heroId) vassals.Add(pc.R.NameOf((long)o.Vassal));

        // opinions of the people who matter: what they think of the hero
        var ops = new List<OpinionLine>();
        foreach (var f in rb.Fiefs.Values)
        {
            if (f.Holder < 0 || f.Holder == (long)heroId || f.Forfeited) continue;
            ulong id = (ulong)f.Holder;
            ops.Add(new OpinionLine(pc.R.NameOf(f.Holder), id, Opinions.Value(rb, id, heroId), Mods(book, id, heroId)));
        }
        foreach (int r in pc.AliveRealms().Where(r => rb.Fiefs.Values.Any(f => f.Realm == r)))
        {
            ulong key = pc.KeyOf(r);
            ops.Add(new OpinionLine(RulerNameOf(pc, r) + " (" + pc.NameOf(r) + ")", key, Opinions.Value(rb, key, heroId), Mods(book, key, heroId)));
        }
        ops.Sort((a, b) => b.Value != a.Value ? b.Value.CompareTo(a.Value) : a.Id.CompareTo(b.Id));

        // treaties and wars
        var treaties = book.Pacts.Where(p => !p.Ended || (p.TributeFrom >= 0 && p.TributeUntilDay > day)).Select(p => new PactLine(pc.NameOf(p.A), pc.NameOf(p.B), "ch_dp_pact_" + p.Kind.ToString().ToLowerInvariant(), p.ExpiryDay, p.TributeUntilDay > day ? p.TributeMites : 0)).ToList();
        var wars = book.Wars.Where(w => w.Alive).Select(w => new WarLine(pc.NameOf(w.Attacker), pc.NameOf(w.Defender), "ch_dp_cb_" + w.Cb.ToString().ToLowerInvariant(), w.Score, w.Seasons,
            WarUtility.Weariness(book.Settings, w.Seasons, w.LossA / 10, pc.TreasurySeasons(w.Attacker)), WarUtility.Weariness(book.Settings, w.Seasons, w.LossD / 10, pc.TreasurySeasons(w.Defender)), w.StartDay)).ToList();
        var log = book.Log.Skip(Math.Max(0, book.Log.Count - 12)).Reverse().Select(l => new LogLine(l.Day, pc.R.NameOf((long)l.Lord), l.Action, l.Note.Length > 0 ? l.Note : "ch_note_dp_own_judgement", l.Entry)).ToList();

        // the actions open to him, each with its terms
        var acts = new List<ActionLine>();
        if (homeRow != null && homeRow.Holder >= 0 && homeRow.Holder != (long)heroId && !homeRow.Forfeited)
        {
            if (!sworn) { var v = Road.FealtyVerdict(pc, hero, home); acts.Add(new ActionLine("swear", home, pc.R.FiefName(home), v.Ok, v.Terms)); }
            else if (book.OathOf(heroId)!.Liege == (ulong)homeRow.Holder) { var v = Road.ManorVerdict(pc, hero, home); acts.Add(new ActionLine("manor", home, pc.R.FiefName(home), v.Ok, v.Terms)); }
            acts.Add(new ActionLine("gift", home, pc.R.FiefName(home), true, []));
        }
        foreach (var f in rb.Fiefs.Values.Where(f => f.Realm >= 0 && Road.Vacant(f)))
        { var v = Road.FiefVerdict(pc, hero, f.Fief); acts.Add(new ActionLine("fief", f.Fief, pc.R.FiefName(f.Fief), v.Ok, v.Terms)); }
        if (realm >= 0 && !pc.HeroRules(realm) && !pc.R.PurseOf(Party.OfRealm(realm)).IsNone) acts.Add(new ActionLine("giftr", realm, pc.NameOf(realm), true, []));
        if (holds) acts.Add(new ActionLine("renounce", -1, "", true, []));

        var standing = st.Content != null ? StandingView.Of(st, deeds, hero, day) : StandingView.Empty;
        return new FactionView(day, rank, realm >= 0 ? pc.NameOf(realm) : "", realm >= 0 ? RulerNameOf(pc, realm) : "", chain, fiefs, vassals, ops, treaties, wars, log, acts, standing);
    }

    private static IReadOnlyList<(string, int, int, ulong)> Mods(PoliticsBook b, ulong from, ulong of) => [.. Opinions.Top(b, from, of).Select(m => ("dp_op_" + m.Kind, m.Amount, m.Day, m.Cause))];
}
