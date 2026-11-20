using System.Globalization;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.People;
using Ravensguard.Sim.Rule;
using Ravensguard.Sim.Save;
using Ravensguard.Sim.Trade;

namespace Ravensguard.Sim.Unrest;

/// <summary>
/// The hero's part in unrest at the barrier (unrest-and-revolt 4.2, 4.4; recorded for replay like every hero command, the tag <c>ur:</c>): join a riot (the aggrieved's standing up, the lord's down), lead it (if his Leadership and
/// Speech beat the leader's), stand with the guards, or mediate (Speech against the unrest over ten: the lord's concession on the spot when his utility for it is at least 200); and as a lord, give a grain dole or a pardon.
/// Each answer is a text key the screen shows.
/// </summary>
public static class UnrestDesk
{
    public const string Prefix = "ur:";
    private static string N(long v) => v.ToString(CultureInfo.InvariantCulture);
    private static HeroCommand Make(string tag, long sec) => new() { Kind = HeroCommandKind.Unrest, IssuedSec = sec, Origin = CommandOrigin.Human, Tag = Prefix + tag };

    public static HeroCommand Join(int riot, long sec) => Make("join|" + N(riot), sec);
    public static HeroCommand Lead(int riot, long sec) => Make("lead|" + N(riot), sec);
    public static HeroCommand StandWithGuards(int riot, long sec) => Make("guards|" + N(riot), sec);
    public static HeroCommand Mediate(int riot, long sec) => Make("mediate|" + N(riot), sec);
    public static HeroCommand Dole(int fief, long sec) => Make("dole|" + N(fief), sec);
    public static HeroCommand Pardon(int fief, long sec) => Make("pardon|" + N(fief), sec);

    private static HeroResult R(HeroCommand c, HeroResultCode code, string key, params object[] args) => new(c.Seq, c.Kind, code, HeroText.Of(key, args), 0, 0, c.IssuedSec);

    public static HeroResult Apply(HeroContext ctx, HeroCommand c)
    {
        if (!c.Tag.StartsWith(Prefix, StringComparison.Ordinal)) return R(c, HeroResultCode.Invalid, "HERO_UNKNOWN_COMMAND");
        var st = ctx.State; var sim = st.HomeRecords?.Sim; var rule = RuleWiring.Bind(st, ctx.Content);
        if (sim == null || rule == null || st.Unrest == null) return R(c, HeroResultCode.Refused, "ur_no_unrest");
        var book = st.Unrest; UnrestWiring.Bind(book, ctx.Content, st.Seed);
        int day = (int)(c.IssuedSec / 86400); var hero = ctx.Party.Hero; long heroId = (long)hero.Value;
        var uc = UnrestWiring.Context(sim, book, rule, null, day);
        uc = new UnrestCtx { B = uc.B, R = RuleWiring.Context(sim, rule, RuleChron.Of(st.Chronicle, st.Ids)), Ob = null, Links = uc.Links, PB = uc.PB, Day = day, Hour = c.IssuedSec / 3600 };
        var f = c.Tag[Prefix.Length..].Split('|');
        long A(int i) => f.Length > i && long.TryParse(f[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out long v) ? v : -1;
        int HS(string id) => ctx.Hero?.Skill(id) ?? 0;
        switch (f[0])
        {
            case "join": case "lead": case "guards": case "mediate":
            {
                var riot = book.RiotById((int)A(1)); if (riot == null || riot.State == RiotState.Ended) return R(c, HeroResultCode.Refused, "ur_refused_over");
                var place = uc.PlaceId(riot.Settlement);
                void Deed(string id)
                {
                    string tkey = "ch_ur_" + id;
                    var dps = new List<ChronParam> { ChronParam.Person("doer", hero), ChronParam.Place("place", place) };
                    var dwhy = new Cause(CauseKind.Unrest, new EntityId(riot.RiseEntry), "", "ch_note_ur_riot_ran");
                    var e = uc.Chron.Chron(uc.Now, id, tkey, dps, [hero, place], [dwhy]);
                    uc.Links.Deed?.Invoke(hero, id, e, riot.Settlement, day);
                }
                switch (f[0])
                {
                    case "join": riot.HeroSide = 1; Deed("riot_joined"); return R(c, HeroResultCode.Ok, "ur_joined", uc.PlaceName(riot.Settlement));
                    case "guards": riot.HeroSide = 3; Deed("riot_guards"); return R(c, HeroResultCode.Ok, "ur_with_guards", uc.PlaceName(riot.Settlement));
                    case "lead":
                    {
                        var pt = uc.W.People; int lr = pt.IndexOf(new EntityId(riot.Leader));
                        int theirs = lr >= 0 ? pt.SkillLevelOf(lr, Skill.Leadership) + pt.SkillLevelOf(lr, Skill.Speech) : 0;
                        if (HS("leadership") + HS("speech") <= theirs) return R(c, HeroResultCode.Refused, "ur_cannot_lead");
                        riot.HeroSide = 2; Deed("riot_joined"); return R(c, HeroResultCode.Ok, "ur_leads", uc.PlaceName(riot.Settlement));
                    }
                    default:
                    {
                        string key = UnrestConcede.Mediate(uc, riot, HS("speech"));
                        if (key == "ur_mediated") Deed("riot_mediated");
                        return key == "ur_mediated" ? R(c, HeroResultCode.Ok, key) : R(c, HeroResultCode.Refused, key);
                    }
                }
            }
            case "dole": case "pardon":
            {
                int fief = (int)A(1); var fr = rule.FiefOf(fief);
                if (fr == null || fr.Holder != heroId || fr.Forfeited) return R(c, HeroResultCode.Refused, "ur_not_your_fief");
                if (f[0] == "pardon") { int n = UnrestConcede.Pardon(uc, fief, "ch_note_ur_pardon_mercy"); return n > 0 ? R(c, HeroResultCode.Ok, "ur_pardoned", n) : R(c, HeroResultCode.Refused, "ur_nothing_to_pardon"); }
                var from = uc.R.HouseholdOf(heroId);
                int fed = UnrestConcede.Dole(uc, fief, from, "ch_note_dp_unrest_high");
                return fed > 0 ? R(c, HeroResultCode.Ok, "ur_doled", fed) : R(c, HeroResultCode.Refused, "ur_nobody_hungry");
            }
        }
        return R(c, HeroResultCode.Invalid, "HERO_MAKES_NO_SENSE");
    }
}

/// <summary>The Fief tab's unrest rows (unrest-and-revolt 6): per settlement of a shown fief at rung 1 or more, its rung and unrest and its top three grievances with the counts.</summary>
public static class UnrestLedger
{
    public const string Mark = RuleLedger.Mark;

    /// <summary>Rows as cell arrays (what, who, amount, state, ends, alerts) in the Fief tab's columns.</summary>
    public static List<string[]> Lines(WorldState st, IEnumerable<int> fiefs, int day)
    {
        var rows = new List<string[]>();
        var b = st.Unrest; if (b == null || b.Settlements.Count == 0 || st.Rule == null || st.HomeRecords == null) return rows;
        var sim = st.HomeRecords.Sim; var c = UnrestWiring.Context(sim, b, st.Rule, null, day);
        var hht = c.W.Households; var fiefSet = fiefs.ToHashSet();
        foreach (var (s, su) in b.Settlements)
        {
            if (su.Rung < 1 || !fiefSet.Contains(c.FiefOfSettlement(s))) continue;
            string name = c.PlaceName(s);
            var riot = b.ActiveRiot(s);
            rows.Add([Mark + "ur_row_rung", name, su.Unrest.ToString(CultureInfo.InvariantCulture), Mark + "ur_rung_" + su.Rung, riot != null ? Mark + "ur_riot_on" : "", ""]);
            var hs = new List<int>(); for (int h = 0; h < hht.Count; h++) if (hht.MemberCount[h] > 0 && hht.Settlement[h] == s) hs.Add(h);
            var a = UnrestEngine.Assess(c, s, hs);
            foreach (var (k, n) in UnrestEngine.Ranked(c, a).Take(3))
                rows.Add([Mark + b.Kinds[k].TextKey, name, n.ToString(CultureInfo.InvariantCulture), "", "", ""]);
        }
        return rows;
    }
}
