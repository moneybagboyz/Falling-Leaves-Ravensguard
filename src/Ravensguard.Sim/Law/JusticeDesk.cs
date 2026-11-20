using System.Globalization;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Trade;

namespace Ravensguard.Sim.Law;

/// <summary>
/// The hero's acts in the courts and the cell, at the barrier (crime-and-justice 4.5, 4.7), recorded and replayed like every hero command: the tag "jx:" says which. As the accused: surrender, plead guilty or
/// claim benefit of clergy, make restitution before sentence, offer the gaoler coin. As a judge (a lord's seat): pronounce a case on the docket, adjourn it to the steward, and write the fief's own law for a crime.
/// </summary>
public static class JusticeDesk
{
    public const string Prefix = "jx:";

    private static string N(long v) => v.ToString(CultureInfo.InvariantCulture);
    private static HeroCommand C(string tag, long sec, int settlement = -1) => new() { Kind = HeroCommandKind.Justice, IssuedSec = sec, Origin = CommandOrigin.Human, Settlement = settlement, Tag = Prefix + tag };

    /// <summary>The hero gives himself up to the guards of a settlement.</summary>
    public static HeroCommand Surrender(int settlement, long sec) => C("surrender", sec, settlement);
    /// <summary>The accused pleads guilty to a crime (the court then needs no witness) or claims benefit of clergy.</summary>
    public static HeroCommand Plead(int crime, bool clergy, long sec) => C("plead|" + N(crime) + "|" + (clergy ? "clergy" : "guilty"), sec);
    /// <summary>Full restitution before sentence (mercy: down a step): the value of what was taken, to the victim.</summary>
    public static HeroCommand Restitute(int crime, long sec) => C("restitute|" + N(crime), sec);
    /// <summary>An offer of coin to the gaoler.</summary>
    public static HeroCommand Bribe(long mites, long sec) => C("bribe|" + N(mites), sec);
    /// <summary>The hero as judge pronounces a case on his docket; an empty key is the expected sentence, else any table of the content (<see cref="Sentences.Alternatives"/>).</summary>
    public static HeroCommand Judge(int crime, string key, long sec) => C("judge|" + N(crime) + "|" + key, sec);
    /// <summary>The hero as judge acquits (the case goes through the proof as it would: a case with no proof is acquitted anyway; this one is acquitted whatever the proof).</summary>
    public static HeroCommand Acquit(int crime, long sec) => C("acquit|" + N(crime), sec);
    /// <summary>Adjourns the court: what is left on the docket goes to the steward.</summary>
    public static HeroCommand Adjourn(long sec) => C("adjourn", sec);
    /// <summary>The fief's own law for a crime kind (the Law panel); an empty key goes back to the culture's.</summary>
    public static HeroCommand SetLaw(int fief, string kind, string key, long sec) => C("law|" + N(fief) + "|" + kind + "|" + key, sec);

    private static HeroResult R(HeroCommand c, HeroResultCode code, string key, params object[] args) => new(c.Seq, c.Kind, code, HeroText.Of(key, args), 0, 0, c.IssuedSec);

    public static HeroResult Apply(HeroContext ctx, HeroCommand c)
    {
        if (!c.Tag.StartsWith(Prefix, StringComparison.Ordinal)) return R(c, HeroResultCode.Invalid, "HERO_UNKNOWN_COMMAND");
        var f = c.Tag[Prefix.Length..].Split('|');
        var st = ctx.State; var w = LawWorld.From(st, ctx.Content); var hero = ctx.Party.Hero;
        var cb = st.Law ??= new CrimeBook(); var jb = st.Justice ??= new JusticeBook();
        try
        {
            switch (f[0])
            {
                case "surrender":
                {
                    int town = c.Settlement >= 0 ? c.Settlement : w.HomeOf(hero);
                    var charges = Gaol.Arrest(w, cb, jb, hero, town, c.IssuedSec, inAct: false);
                    return charges.Count == 0 ? R(c, HeroResultCode.Refused, "jx_nothing_charged") : R(c, HeroResultCode.Ok, "jx_surrendered", charges.Count);
                }
                case "plead" when f.Length >= 3 && int.TryParse(f[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int pc):
                {
                    if (cb.Get(pc) is not { State: CrimeState.Held } rec || rec.Perpetrator != (long)hero.Value) return R(c, HeroResultCode.Refused, "jx_no_such_charge");
                    (f[2] == "clergy" ? jb.Clergy : jb.Confessed).Add(pc);
                    return R(c, HeroResultCode.Ok, f[2] == "clergy" ? "jx_claimed_clergy" : "jx_pleaded_guilty");
                }
                case "restitute" when f.Length >= 2 && int.TryParse(f[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int rc):
                {
                    if (cb.Get(rc) is not { State: CrimeState.Held } rec || rec.Perpetrator != (long)hero.Value) return R(c, HeroResultCode.Refused, "jx_no_such_charge");
                    if (jb.Restituted.Contains(rc)) return R(c, HeroResultCode.Refused, "jx_already_restituted");
                    long due = rec.ValueMites;
                    var from = w.PurseOf(hero); var to = w.VictimPurse(rec.Victim);
                    if (from == null || to == null || w.Ledger == null || from == to) return R(c, HeroResultCode.Refused, "jx_nobody_to_pay");
                    if (due > 0 && !w.Ledger.Transfer(from.Value, to.Value, Coin.FromMites(due), TransferReason.Restitution)) return R(c, HeroResultCode.CannotAfford, "jx_cannot_afford", Quotes.Money(due));
                    jb.Restituted.Add(rc);
                    return new HeroResult(c.Seq, c.Kind, HeroResultCode.Ok, HeroText.Of("jx_restituted", Quotes.Money(due)), due, 0, c.IssuedSec);
                }
                case "bribe" when f.Length >= 2 && long.TryParse(f[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out long offer):
                {
                    var o = Gaol.Bribe(w, cb, jb, hero, offer, c.IssuedSec);
                    return o switch
                    {
                        BribeOutcome.Taken => new HeroResult(c.Seq, c.Kind, HeroResultCode.Ok, HeroText.Of("jx_bribe_taken", Quotes.Money(offer)), offer, 0, c.IssuedSec),
                        BribeOutcome.NotHeld => R(c, HeroResultCode.Refused, "jx_not_held"),
                        BribeOutcome.Cannot => R(c, HeroResultCode.CannotAfford, "jx_bribe_cannot"),
                        _ => R(c, HeroResultCode.Refused, "jx_bribe_refused"),
                    };
                }
                case "judge" when f.Length >= 3 && int.TryParse(f[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int jc):
                    return Court.Judge(w, cb, jb, jc, f[2], c.IssuedSec) ? R(c, HeroResultCode.Ok, "jx_judged") : R(c, HeroResultCode.Refused, "jx_no_such_case");
                case "acquit" when f.Length >= 2 && int.TryParse(f[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int ac):
                {
                    if (!jb.Docket.Contains(ac) || cb.Get(ac) is not { State: CrimeState.Held } rec) return R(c, HeroResultCode.Refused, "jx_no_such_case");
                    jb.Docket.Remove(ac);
                    return Court.Acquit(w, cb, jb, rec, true, c.IssuedSec) ? R(c, HeroResultCode.Ok, "jx_acquitted") : R(c, HeroResultCode.Refused, "jx_no_such_case");
                }
                case "adjourn":
                    return R(c, HeroResultCode.Ok, "jx_adjourned", Court.Adjourn(w, cb, jb, c.IssuedSec));
                case "law" when f.Length >= 4 && int.TryParse(f[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int fief):
                {
                    if (!w.HeroLordOf(fief)) return R(c, HeroResultCode.Refused, "jx_not_your_fief");
                    if (f[3].Length == 0) { jb.Laws.Remove((fief, f[2])); return R(c, HeroResultCode.Ok, "jx_law_reset"); }
                    int seat = fief < w.FiefSeat.Length ? w.FiefSeat[fief] : -1;
                    string culture = w.CultureKey(seat, f[2]) ?? w.CultureKey(seat, Sentences.Alias(f[2])) ?? "fine";
                    if (!Sentences.Allowed(w, culture).Contains(f[3])) return R(c, HeroResultCode.Refused, "jx_law_not_allowed");
                    jb.Laws[(fief, f[2])] = f[3];
                    return R(c, HeroResultCode.Ok, "jx_law_set");
                }
            }
        }
        finally { if (st.Justice is { IsEmpty: true }) st.Justice = null; }
        return R(c, HeroResultCode.Invalid, "HERO_MAKES_NO_SENSE");
    }
}
