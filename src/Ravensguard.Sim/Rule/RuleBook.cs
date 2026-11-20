using Ravensguard.Content;
using Ravensguard.Sim.Core;

namespace Ravensguard.Sim.Rule;

/// <summary>The rights a fief deed carries (rule-and-income 3): to tax, to levy men, to judge, to hold a market.</summary>
[Flags] public enum FiefRights : byte { None = 0, Tax = 1, Levy = 2, Judge = 4, Market = 8, All = 15 }

/// <summary>The offices a lord fills (rule-and-income 3). Append only.</summary>
public enum OfficeKind : byte { Reeve, Bailiff, Steward, Sheriff, CustomsCollector, Gaoler, Treasurer }

/// <summary>What is owed (rule-and-income 3, 4.2, 4.3). Append only.</summary>
public enum DueKind : byte { Service, Aid, Relief, Wardship, MarriageFee, Subsidy, Tallage, Rent, Labour }

/// <summary>Where a due stands: due, paid, in arrears, waived, or a grievance (the payer was left unpaid and it counts against the payee). Append only.</summary>
public enum DueState : byte { Due, Paid, Arrears, Waived, Grievance }

/// <summary>Who pays or is paid: a fief's treasury, a realm's, a household, or a person. The pair (kind, id) names the purse.</summary>
public enum PartyKind : byte { Fief, Realm, Household, Person }

/// <summary>A payer or a payee of a due or a loan: <see cref="Id"/> is the fief index, the history realm index, the household's or the person's id value.</summary>
public readonly record struct Party(PartyKind Kind, long Id) : IComparable<Party>
{
    public static readonly Party None = new(PartyKind.Fief, -1);
    public static Party OfFief(int fief) => new(PartyKind.Fief, fief);
    public static Party OfRealm(int realm) => new(PartyKind.Realm, realm);
    public static Party OfHousehold(EntityId id) => new(PartyKind.Household, (long)id.Value);
    public static Party OfPerson(EntityId id) => new(PartyKind.Person, (long)id.Value);
    public bool IsNone => Id < 0 && Kind == PartyKind.Fief;
    public int CompareTo(Party o) { int c = Kind.CompareTo(o.Kind); return c != 0 ? c : Id.CompareTo(o.Id); }
}

/// <summary>
/// A fief as a deed (rule-and-income 3, <c>HoldingKind.Fief</c>): who holds it, whose overlord, the rights, the service owed, the lord's policy beyond the tax level (that stays in <c>TaxState.FiefLevel</c>), and the
/// bookkeeping the dues read (the last year's receipts, a wardship). Derived from the Phase 1 lords at the seeding pass, then saved.
/// </summary>
public sealed class FiefRow
{
    public int Fief;
    /// <summary>The person id of the lord (the holder), -1 while vacant.</summary>
    public long Holder = -1;
    /// <summary>The overlord's realm (a history index), -1 for none; and for a manor, the fief it is a sub-fief of (-1 for a fief).</summary>
    public int Realm = -1, OverlordFief = -1;
    public FiefRights Rights = FiefRights.All;
    public int ServiceMen, ServiceDays;
    // ---- policy (rule-and-income 3)
    /// <summary>Whether the lord takes his rents in coin (true) or in kind; one switch a year (<see cref="CommuteDay"/>).</summary>
    public bool RentInCoin;
    public int CommuteDay = -1000;
    /// <summary>Market days a week as a bit mask (bit 0 Monday of the seven-day week of <c>day % 7</c>); 0 means the default (every day the market already keeps).</summary>
    public byte MarketDays;
    // ---- books
    /// <summary>Receipts of the last four finished seasons, most recent at index 0 (mites): the year relief and wardship are measured by.</summary>
    public long[] Receipts = new long[4];
    /// <summary>The day a wardship ends (-1 none), and the guardian's person id (-1 none).</summary>
    public int WardUntilDay = -1;
    public long Guardian = -1;
    /// <summary>The day the holder took the fief (for the record), and the days of the unpaid relief.</summary>
    public int TookDay = -1;
    /// <summary>The overlord's court has taken the deed back (an unpaid relief, a treason): the fief has no holder until a new grant, whoever lives in the manor (rule-and-income 4.2).</summary>
    public bool Forfeited;
    /// <summary>The lord the population last named for this fief (a person id, -1 none): a change in it is the line's succession; a holder set by a grant (the hero's) is left alone while it stays.</summary>
    public long PopLord = -1;
    public long LastYearReceipts => Receipts[0] + Receipts[1] + Receipts[2] + Receipts[3];
}

/// <summary>An official (rule-and-income 3, 4.8): a person appointed to a post of a fief (or realm), with his honesty and his record of what he handed over against what was expected.</summary>
public sealed class Office
{
    public int Serial;
    public OfficeKind Kind;
    public long Holder;
    /// <summary>The fief index the office serves, or -1 for a realm office.</summary>
    public int Fief = -1;
    public int Realm = -1;
    public int WageMitesPerDay;
    public byte Honesty;
    public int AppointedDay;
    /// <summary>What he collected, what was expected of him, what he skimmed (the ledger knows) and what audits found, lifetime, in mites.</summary>
    public long Collected, Expected, Skimmed, Found;
    /// <summary>What he skimmed since the last seasonal pass (the pass lets a clerk see it, an audit find it, and then clears it).</summary>
    public long SeasonSkim;
    /// <summary>The day he was dismissed (-1 in office).</summary>
    public int DismissedDay = -1;
    public bool InOffice => DismissedDay < 0;
}

/// <summary>A due (rule-and-income 3): what one party owes another, its day, what has been paid and where it stands.</summary>
public sealed class DueRow
{
    public int Serial;
    public DueKind Kind;
    public Party Payer, Payee;
    /// <summary>Mites owed (goods or labour dues carry their coin value; <see cref="Units"/> says how many men, days or measures).</summary>
    public long AmountMites;
    public int Units;
    public int DueDay;
    public long PaidMites;
    public DueState State;
    /// <summary>The chronicle entry of the event that made it due (0 none).</summary>
    public ulong Entry;
    public long Owing => Math.Max(0, AmountMites - PaidMites);
}

/// <summary>How a loan is secured (rule-and-income 4.6). Append only.</summary>
public enum LoanSecurity : byte { None, Deed, Customs }

/// <summary>The state of a loan. Append only.</summary>
public enum LoanState : byte { Open, Repaid, Default }

/// <summary>
/// A loan (rule-and-income 3, 4.6): coin that exists moves from the lender's purse to the borrower's; the interest is paid each season and the principal at the term. Nothing here makes coin.
/// </summary>
public sealed class Loan
{
    public int Serial;
    public Party Lender, Borrower;
    public long PrincipalMites;
    /// <summary>Interest a season in permille of the principal (40 for 4%).</summary>
    public int RatePmPerSeason;
    public int StartDay, TermSeasons;
    public LoanSecurity Security;
    /// <summary>The deed a mortgage is on (an <see cref="EntityId"/> value), 0 none.</summary>
    public ulong DeedId;
    public long Repaid, InterestPaid;
    /// <summary>Seasons paid so far and consecutive seasons late.</summary>
    public int SeasonsPaid, LateSeasons;
    public LoanState State;
    public ulong Entry;
    public long Owing => Math.Max(0, PrincipalMites - Repaid);
}

/// <summary>A council that was held (rule-and-income 4.3): the cause, who voted how, and whether it passed; the Ledger's council panel reads these.</summary>
public sealed class CouncilRecord
{
    public int Serial, Day, Realm;
    public string Cause = "";
    public int Yes, No;
    public bool Passed;
    public long AmountMites;
    /// <summary>Per member (ascending person id): id, vote (1 yes), the sum that was weighed.</summary>
    public List<(long Member, bool Yes, int Score)> Votes { get; } = [];
}

/// <summary>A levy waiting to be collected (a passed subsidy): taken from the households' movables over the next season.</summary>
public sealed class LevyRow
{
    public int Serial, Realm, DueDay;
    public int TownPermille, CountryPermille;
}

/// <summary>A household's standing grievance against the lord's government (rule-and-income 4.3): points and the day they lapse.</summary>
public sealed class GrievanceRow
{
    public int Points, UntilDay;
    public ulong Entry;
}

/// <summary>
/// The rule and income of the home region (rule-and-income 3, the optional "fiefs" section, no format bump): fief deeds and policy, offices and honesty, dues, loans, councils and levies, grievances, opinions between
/// parties, closed credit. Everything else (the treasuries, the arrears ladder, the leases) lives where Phase 2 and 5 keep it. No random draw lives here; the systems draw with the registered streams and write results in.
/// </summary>
public sealed class RuleBook
{
    public readonly SortedDictionary<int, FiefRow> Fiefs = [];
    public readonly List<Office> Offices = [];
    /// <summary>Honesty (10..100) of persons it has been rolled for, by person id (stream 0x540, drawn once per person the first time it matters).</summary>
    public readonly SortedDictionary<ulong, byte> Honesty = [];
    public readonly List<DueRow> Dues = [];
    public readonly List<Loan> Loans = [];
    public readonly List<CouncilRecord> Councils = [];
    public readonly List<LevyRow> Levies = [];
    /// <summary>Households' grievances by household id value.</summary>
    public readonly SortedDictionary<ulong, GrievanceRow> Grievances = [];
    /// <summary>What one party thinks of another (rule-and-income 4.2, 4.6): added by dues unpaid, loans defaulted, tallage; a later opinion system reads and moves it. Keyed (who thinks, of whom).</summary>
    public readonly SortedDictionary<(ulong Who, ulong Of), int> Opinions = [];
    /// <summary>Realm -> customs rate in percent set by the ruler (absent: the default rate).</summary>
    public readonly SortedDictionary<int, int> Customs = [];
    /// <summary>Realm -> the day of its last subsidy (for the second-within-two-years penalty).</summary>
    public readonly SortedDictionary<int, int> LastSubsidyDay = [];
    /// <summary>Borrower party (kind, id) -> the day its credit reopens.</summary>
    public readonly SortedDictionary<(byte Kind, long Id), int> CreditClosedUntil = [];
    /// <summary>Squatting claims noticed: claim serial -> the day the notice runs out (rule-and-income 4.7).</summary>
    public readonly SortedDictionary<int, int> EvictionNotices = [];
    /// <summary>Claims that were refused the licence and handed to the guards: serial -> the day they were reported to the law (the hook C1's guards read).</summary>
    public readonly SortedDictionary<int, int> EvictionOrders = [];
    /// <summary>Demesne labour credited: fief -> worker-hours in total; and the coin owed for days missed that were collected.</summary>
    public readonly SortedDictionary<int, long> DemesneHours = [];
    public readonly SortedDictionary<int, long> LabourCoin = [];
    /// <summary>Market charters granted (settlement index -> day); a charter's year dues are paid to its holder's realm.</summary>
    public readonly SortedDictionary<int, (int Day, long Holder)> MarketCharters = [];
    /// <summary>Founding charters (by serial) whose deadline has had its one extension, and those revoked (rule-and-income 4.9).</summary>
    public readonly SortedSet<int> Extended = [], Revoked = [];
    /// <summary>Realm -> the person who rules it (set by the diplomacy step when a realm has a ruler of flesh; the hero's ruler powers are checked against it).</summary>
    public readonly SortedDictionary<int, long> Rulers = [];
    public int NextOffice = 1, NextDue = 1, NextLoan = 1, NextCouncil = 1, NextLevy = 1;
    /// <summary>The day of the last daily pass (-1 never) and the season day of the last seasonal pass.</summary>
    public int LastDay = -1, LastSeasonDay = -1;
    /// <summary>The last labour window day settled (spring and autumn), so a window is settled once.</summary>
    public int LastLabourDay = -1;

    // ---- not saved: what the systems read, set when the book is attached
    public RuleSettingsDef Settings { get; set; } = new() { Id = "rule" };
    public IReadOnlyDictionary<string, OfficeDef> OfficeDefs { get; set; } = new Dictionary<string, OfficeDef>();
    public IReadOnlyList<CustomsRateDef> CustomsRates { get; set; } = [];
    public ulong Seed { get; set; }

    public bool IsEmpty => Fiefs.Count == 0 && Offices.Count == 0 && Honesty.Count == 0 && Dues.Count == 0 && Loans.Count == 0 && Councils.Count == 0 && Levies.Count == 0 && Grievances.Count == 0
        && Opinions.Count == 0 && Customs.Count == 0 && LastSubsidyDay.Count == 0 && CreditClosedUntil.Count == 0 && EvictionNotices.Count == 0 && EvictionOrders.Count == 0 && DemesneHours.Count == 0
        && LabourCoin.Count == 0 && MarketCharters.Count == 0 && Extended.Count == 0 && Revoked.Count == 0 && Rulers.Count == 0 && LastDay < 0 && LastSeasonDay < 0 && LastLabourDay < 0;

    public FiefRow? FiefOf(int fief) => Fiefs.GetValueOrDefault(fief);
    public Office? OfficeOf(OfficeKind kind, int fief) { foreach (var o in Offices) if (o.InOffice && o.Kind == kind && o.Fief == fief) return o; return null; }
    public Office? OfficeById(int serial) { foreach (var o in Offices) if (o.Serial == serial) return o; return null; }
    public IEnumerable<Office> OfficesOf(int fief) => Offices.Where(o => o.InOffice && o.Fief == fief);
    public Loan? LoanById(int serial) { foreach (var l in Loans) if (l.Serial == serial) return l; return null; }
    public DueRow? DueById(int serial) { foreach (var d in Dues) if (d.Serial == serial) return d; return null; }

    public int Opinion(ulong who, ulong of) => Opinions.GetValueOrDefault((who, of));
    public void AddOpinion(ulong who, ulong of, int delta) { if (who == 0 || of == 0 || delta == 0) return; int v = Math.Clamp(Opinion(who, of) + delta, -1000, 1000); if (v == 0) Opinions.Remove((who, of)); else Opinions[(who, of)] = v; }

    /// <summary>The customs rate (percent) a realm's ruler has set, or the default.</summary>
    public int CustomsPct(int realm) => Customs.TryGetValue(realm, out int v) ? v : Settings.CustomsDefaultPct;

    /// <summary>Grievance points a household carries on <paramref name="day"/> (a row lapses on its day).</summary>
    public int GrievancePoints(ulong household, int day) => Grievances.TryGetValue(household, out var g) && g.UntilDay > day ? g.Points : 0;

    /// <summary>The <c>FiefHash</c> of the daily trail (rule-and-income 5): fief deeds, policy, offices, dues, loans, councils, levies and the small tables, in ascending order.</summary>
    public ulong Hash()
    {
        ulong h = 0xCBF29CE484222325UL;
        void M(long v) => h = DetRng.Mix(h ^ ((ulong)v + 0x9E3779B97F4A7C15UL));
        foreach (var (k, f) in Fiefs)
        {
            M(k); M(f.Holder); M(f.Realm); M(f.OverlordFief); M((long)f.Rights); M(f.ServiceMen); M(f.ServiceDays); M(f.RentInCoin ? 1 : 0); M(f.CommuteDay); M(f.MarketDays);
            foreach (long r in f.Receipts) M(r);
            M(f.WardUntilDay); M(f.Guardian); M(f.TookDay); M(f.Forfeited ? 1 : 0); M(f.PopLord);
        }
        foreach (var o in Offices) { M(o.Serial); M((long)o.Kind); M(o.Holder); M(o.Fief); M(o.Realm); M(o.WageMitesPerDay); M(o.Honesty); M(o.AppointedDay); M(o.Collected); M(o.Expected); M(o.Skimmed); M(o.Found); M(o.SeasonSkim); M(o.DismissedDay); }
        foreach (var (k, v) in Honesty) { M((long)k); M(v); }
        foreach (var d in Dues) { M(d.Serial); M((long)d.Kind); M((long)d.Payer.Kind); M(d.Payer.Id); M((long)d.Payee.Kind); M(d.Payee.Id); M(d.AmountMites); M(d.Units); M(d.DueDay); M(d.PaidMites); M((long)d.State); M((long)d.Entry); }
        foreach (var l in Loans) { M(l.Serial); M((long)l.Lender.Kind); M(l.Lender.Id); M((long)l.Borrower.Kind); M(l.Borrower.Id); M(l.PrincipalMites); M(l.RatePmPerSeason); M(l.StartDay); M(l.TermSeasons); M((long)l.Security); M((long)l.DeedId); M(l.Repaid); M(l.InterestPaid); M(l.SeasonsPaid); M(l.LateSeasons); M((long)l.State); M((long)l.Entry); }
        foreach (var c in Councils) { M(c.Serial); M(c.Day); M(c.Realm); foreach (char ch in c.Cause) M(ch); M(c.Yes); M(c.No); M(c.Passed ? 1 : 0); M(c.AmountMites); foreach (var (m, y, s) in c.Votes) { M(m); M(y ? 1 : 0); M(s); } }
        foreach (var l in Levies) { M(l.Serial); M(l.Realm); M(l.DueDay); M(l.TownPermille); M(l.CountryPermille); }
        foreach (var (k, g) in Grievances) { M((long)k); M(g.Points); M(g.UntilDay); M((long)g.Entry); }
        foreach (var ((a, b), v) in Opinions) { M((long)a); M((long)b); M(v); }
        foreach (var (k, v) in Customs) { M(k); M(v); }
        foreach (var (k, v) in LastSubsidyDay) { M(k); M(v); }
        foreach (var ((k, i), v) in CreditClosedUntil) { M(k); M(i); M(v); }
        foreach (var (k, v) in EvictionNotices) { M(k); M(v); }
        foreach (var (k, v) in EvictionOrders) { M(k); M(v); }
        foreach (var (k, v) in DemesneHours) { M(k); M(v); }
        foreach (var (k, v) in LabourCoin) { M(k); M(v); }
        foreach (var (k, v) in MarketCharters) { M(k); M(v.Day); M(v.Holder); }
        foreach (var (k, v) in Rulers) { M(k); M(v); }
        foreach (int k in Extended) M(k);
        foreach (int k in Revoked) M(~k);
        M(NextOffice); M(NextDue); M(NextLoan); M(NextCouncil); M(NextLevy); M(LastDay); M(LastSeasonDay); M(LastLabourDay);
        return h;
    }
}
