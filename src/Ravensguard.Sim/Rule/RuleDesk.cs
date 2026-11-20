using System.Globalization;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Land;
using Ravensguard.Sim.Taxes;
using Ravensguard.Sim.Trade;

namespace Ravensguard.Sim.Rule;

/// <summary>
/// The hero's acts of rule at the barrier (rule-and-income 4, 6; recorded for replay like every hero command, the tag <c>ru:</c>). A lord (the hero holding a fief's deed) sets his tax level, switches his rents between
/// grain and coin once a year, appoints and dismisses officials, tallages a town of his, asks a loan, petitions for a market charter and extends a lapsed charter; a ruler (the person the book names for a realm) calls
/// a council and sets the customs; anyone pays a due or repays a loan. Each answer is a text key the Ledger's Fief tab shows. The hero holds a fief only through the grant or inheritance of the diplomacy step.
/// </summary>
public static class RuleDesk
{
    public const string Prefix = "ru:";
    private static string N(long v) => v.ToString(CultureInfo.InvariantCulture);
    private static HeroCommand Make(string tag, long sec, int settlement = -1) => new() { Kind = HeroCommandKind.Rule, IssuedSec = sec, Origin = CommandOrigin.Human, Settlement = settlement, Tag = Prefix + tag };

    public static HeroCommand Level(int fief, TaxLevel level, long sec) => Make("level|" + N(fief) + "|" + N((int)level), sec);
    public static HeroCommand Rent(int fief, bool coin, long sec) => Make("rent|" + N(fief) + "|" + (coin ? "1" : "0"), sec);
    public static HeroCommand Appoint(OfficeKind kind, int fief, long person, long sec) => Make("appoint|" + N((int)kind) + "|" + N(fief) + "|" + N(person), sec);
    public static HeroCommand Dismiss(int office, long sec) => Make("dismiss|" + N(office), sec);
    public static HeroCommand Council(int realm, string cause, long sec) => Make("council|" + N(realm) + "|" + cause, sec);
    public static HeroCommand Tallage(int settlement, long sec) => Make("tallage|" + N(settlement), sec, settlement);
    public static HeroCommand Customs(int realm, int pct, long sec) => Make("customs|" + N(realm) + "|" + N(pct), sec);
    public static HeroCommand Loan(long mites, int seasons, ulong deed, long sec) => Make("loan|" + N(mites) + "|" + N(seasons) + "|" + N((long)deed), sec);
    public static HeroCommand Repay(int loan, long sec) => Make("repay|" + N(loan), sec);
    public static HeroCommand Market(int settlement, long sec) => Make("market|" + N(settlement), sec, settlement);
    public static HeroCommand Extend(int charter, long sec) => Make("extend|" + N(charter), sec);
    public static HeroCommand Pay(int due, long sec) => Make("pay|" + N(due), sec);
    public static HeroCommand Aid(int realm, string occasion, long sec) => Make("aid|" + N(realm) + "|" + occasion, sec);

    private static HeroResult R(HeroCommand c, HeroResultCode code, string key, params object[] args) => new(c.Seq, c.Kind, code, HeroText.Of(key, args), 0, 0, c.IssuedSec);

    public static HeroResult Apply(HeroContext ctx, HeroCommand c)
    {
        if (!c.Tag.StartsWith(Prefix, StringComparison.Ordinal)) return R(c, HeroResultCode.Invalid, "HERO_UNKNOWN_COMMAND");
        var st = ctx.State; var book = RuleWiring.Bind(st, ctx.Content);
        var sim = st.HomeRecords?.Sim;
        if (book == null || sim == null) return R(c, HeroResultCode.Refused, "ru_no_rule");
        var rc = RuleWiring.Context(sim, book, RuleChron.Of(st.Chronicle, st.Ids));
        var hero = ctx.Party.Hero; long heroId = (long)hero.Value; int day = (int)(c.IssuedSec / 86400);
        var f = c.Tag[Prefix.Length..].Split('|');
        long A(int i) => f.Length > i && long.TryParse(f[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out long v) ? v : -1;
        FiefRow? Held(long fief) { var r = book.FiefOf((int)fief); return r != null && r.Holder == heroId && !r.Forfeited ? r : null; }
        switch (f[0])
        {
            case "level":
            {
                var r = Held(A(1)); long lv = A(2); if (r == null) return R(c, HeroResultCode.Refused, "ru_not_your_fief"); if (lv < 0 || lv > 3) return R(c, HeroResultCode.Invalid, "HERO_MAKES_NO_SENSE");
                rc.Tax?.SetLevel(r.Fief, (TaxLevel)lv);
                return R(c, HeroResultCode.Ok, "ru_level_set", rc.FiefName(r.Fief), "ru_level_" + (TaxLevel)lv);
            }
            case "rent":
            {
                var r = Held(A(1)); if (r == null) return R(c, HeroResultCode.Refused, "ru_not_your_fief");
                int n = RuleDues.Commute(rc, r.Fief, A(2) == 1, day);
                return n < 0 ? R(c, HeroResultCode.Refused, "ru_commute_once_a_year") : R(c, HeroResultCode.Ok, "ru_rents_commuted", n);
            }
            case "appoint":
            {
                var r = Held(A(2)); if (r == null) return R(c, HeroResultCode.Refused, "ru_not_your_fief");
                var o = RuleOfficials.Appoint(rc, (OfficeKind)A(1), r.Fief, r.Realm, A(3), day);
                return o == null ? R(c, HeroResultCode.Refused, "ru_cannot_hold_office") : R(c, HeroResultCode.Ok, "ru_appointed", rc.NameOf(o.Holder), "ru_office_" + o.Kind.ToString().ToLowerInvariant());
            }
            case "dismiss":
            {
                var o = book.OfficeById((int)A(1)); if (o == null || !o.InOffice || Held(o.Fief) == null) return R(c, HeroResultCode.Refused, "ru_not_your_fief");
                RuleOfficials.Dismiss(rc, o, day, "ch_note_lord_dismisses");
                return R(c, HeroResultCode.Ok, "ru_dismissed", rc.NameOf(o.Holder));
            }
            case "council":
            {
                int realm = (int)A(1); if (!book.Rulers.TryGetValue(realm, out long ruler) || ruler != heroId) return R(c, HeroResultCode.Refused, "ru_not_ruler");
                var rec = RuleCouncil.Call(rc, realm, f.Length > 2 ? f[2] : "none", day);
                return new HeroResult(c.Seq, c.Kind, HeroResultCode.Ok, HeroText.Of(rec.Passed ? "ru_council_granted" : "ru_council_refused", rec.Yes, rec.No), 0, rec.Serial, c.IssuedSec);
            }
            case "tallage":
            {
                int s = (int)A(1); if ((uint)s >= (uint)rc.Region.Settlements.Count) return R(c, HeroResultCode.Invalid, "HERO_MAKES_NO_SENSE");
                int fi = rc.Region.Settlements[s].FiefIndex; int realm = RuleCouncil.RealmOfSettlement(rc, s);
                Party to;
                if (fi >= 0 && Held(fi) != null) to = Party.OfFief(fi);
                else if (realm >= 0 && book.Rulers.TryGetValue(realm, out long ruler) && ruler == heroId) to = Party.OfRealm(realm);
                else return R(c, HeroResultCode.Refused, "ru_not_your_town");
                long got = RuleCouncil.Tallage(rc, s, to, day);
                return new HeroResult(c.Seq, c.Kind, HeroResultCode.Ok, HeroText.Of("ru_tallage_taken", Quotes.Money(got)), got, 0, c.IssuedSec);
            }
            case "customs":
            {
                int realm = (int)A(1); int pct = (int)A(2);
                if (!book.Rulers.TryGetValue(realm, out long ruler) || ruler != heroId) return R(c, HeroResultCode.Refused, "ru_not_ruler");
                if (pct < 0 || pct > book.Settings.CustomsMaxPct) return R(c, HeroResultCode.Invalid, "ru_customs_range");
                book.Customs[realm] = pct;
                return R(c, HeroResultCode.Ok, "ru_customs_set", pct);
            }
            case "loan":
            {
                long amount = A(1); int term = (int)A(2); ulong deed = (ulong)Math.Max(0, A(3));
                var me = Party.OfPerson(hero);
                var lender = RuleLoans.FindLender(rc, me, amount, day); if (lender == null) return R(c, HeroResultCode.Refused, "ru_no_lender");
                var (loan, why) = RuleLoans.Lend(rc, lender.Value, me, amount, term, deed != 0 ? LoanSecurity.Deed : LoanSecurity.None, deed, day);
                return loan == null ? R(c, HeroResultCode.Refused, why) : new HeroResult(c.Seq, c.Kind, HeroResultCode.Ok, HeroText.Of("ru_loan_made", Quotes.Money(amount), loan.RatePmPerSeason / 10), amount, loan.Serial, c.IssuedSec);
            }
            case "repay":
            {
                var l = book.LoanById((int)A(1)); if (l == null || l.State != LoanState.Open || l.Borrower != Party.OfPerson(hero)) return R(c, HeroResultCode.Refused, "ru_not_your_loan");
                long owe = l.Owing; if (!rc.Pay(l.Borrower, l.Lender, owe, TransferReason.Loan)) return R(c, HeroResultCode.CannotAfford, "ru_cannot_afford", Quotes.Money(owe));
                l.Repaid += owe; l.State = LoanState.Repaid;
                return R(c, HeroResultCode.Ok, "ru_loan_repaid", Quotes.Money(owe));
            }
            case "market":
            {
                var (ok, key) = RuleLand.PetitionMarket(rc, hero, (int)A(1), day);
                return ok ? R(c, HeroResultCode.Ok, "ru_market_granted", rc.Region.Settlements[(int)A(1)].Name) : R(c, HeroResultCode.Refused, key);
            }
            case "extend":
            {
                var why = RuleLand.Extend(rc, (int)A(1), day);
                return why.Length == 0 ? R(c, HeroResultCode.Ok, "ru_charter_extended") : R(c, HeroResultCode.Refused, why);
            }
            case "pay":
            {
                var d = book.DueById((int)A(1)); var me = Party.OfPerson(hero);
                if (d == null || d.State is DueState.Paid or DueState.Waived || (d.Payer != me && d.Payer != rc.HouseholdOf(heroId))) return R(c, HeroResultCode.Refused, "ru_not_your_due");
                long pay = rc.PayUpTo(d.Payer, d.Payee, d.Owing, d.Kind switch { DueKind.Relief => TransferReason.Relief, DueKind.Aid => TransferReason.Aid, _ => TransferReason.Fee });
                d.PaidMites += pay; if (d.Owing == 0) d.State = DueState.Paid;
                return pay <= 0 ? R(c, HeroResultCode.CannotAfford, "ru_cannot_afford", Quotes.Money(d.Owing)) : R(c, HeroResultCode.Ok, "ru_due_paid", Quotes.Money(pay));
            }
            case "aid":
            {
                int realm = (int)A(1); if (!book.Rulers.TryGetValue(realm, out long ruler) || ruler != heroId) return R(c, HeroResultCode.Refused, "ru_not_ruler");
                int n = RuleDues.CallAid(rc, realm, f.Length > 2 ? "ru_occasion_" + f[2] : "ru_occasion_ransom", day);
                return R(c, HeroResultCode.Ok, "ru_aid_called", n);
            }
        }
        return R(c, HeroResultCode.Invalid, "HERO_MAKES_NO_SENSE");
    }
}
