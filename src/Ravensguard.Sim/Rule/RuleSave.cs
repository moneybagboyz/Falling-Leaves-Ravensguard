using MessagePack;
using Ravensguard.Sim.Rule;

namespace Ravensguard.Sim.Save;

/// <summary>
/// The optional "fiefs" section (P6-C3, rule-and-income 3, overview 7; no format bump, absent while the rule has nothing to say, so a world that never had a rule book keeps its bytes): fief deeds with their
/// policy, offices and the honesty rolled, dues, loans, councils, levies, grievances, opinions and the small tables. Rows are plain number arrays in ascending order so the bytes are deterministic.
/// </summary>
[MessagePackObject]
public sealed class RuleSave
{
    public const string SectionName = "fiefs";

    /// <summary>Per fief: fief, holder, realm, overlord fief, rights, service men, service days, rent in coin, commute day, market days, ward until, guardian, took day, the four receipts, forfeited, the population's lord.</summary>
    [Key(0)] public long[][] Fiefs { get; set; } = [];
    /// <summary>Per office: serial, kind, holder, fief, realm, wage, honesty, appointed, collected, expected, skimmed, found, dismissed, season skim.</summary>
    [Key(1)] public long[][] Offices { get; set; } = [];
    /// <summary>Per person with honesty rolled: id, honesty.</summary>
    [Key(2)] public long[][] Honesty { get; set; } = [];
    /// <summary>Per due: serial, kind, payer kind, payer id, payee kind, payee id, amount, units, due day, paid, state, entry.</summary>
    [Key(3)] public long[][] Dues { get; set; } = [];
    /// <summary>Per loan: serial, lender kind, lender id, borrower kind, borrower id, principal, rate, start, term, security, deed, repaid, interest paid, seasons paid, late, state, entry.</summary>
    [Key(4)] public long[][] Loans { get; set; } = [];
    /// <summary>Per council: serial, day, realm, yes, no, passed, amount, then (member, yes, score) triples.</summary>
    [Key(5)] public long[][] Councils { get; set; } = [];
    [Key(6)] public string[] CouncilCauses { get; set; } = [];
    /// <summary>Per levy: serial, realm, due day, town permille, country permille.</summary>
    [Key(7)] public long[][] Levies { get; set; } = [];
    /// <summary>Per grievance: household, points, until day, entry.</summary>
    [Key(8)] public long[][] Grievances { get; set; } = [];
    /// <summary>Per opinion: who, of, value.</summary>
    [Key(9)] public long[][] Opinions { get; set; } = [];
    /// <summary>The small tables as (table, a, b[, c]) rows: 0 customs (realm, pct), 1 last subsidy (realm, day), 2 credit closed (kind, id, day), 3 eviction notice (claim, day), 4 eviction order (claim, day),
    /// 5 demesne hours (fief, hours), 6 labour coin (fief, mites), 7 market charter (settlement, day, holder), 8 charter extended (serial), 9 charter revoked (serial), 10 ruler (realm, person).</summary>
    [Key(10)] public long[][] Small { get; set; } = [];
    /// <summary>Counters: next office, due, loan, council, levy, the last daily day, the last seasonal day, the last labour day.</summary>
    [Key(11)] public long[] Counters { get; set; } = [];

    public static RuleSave Capture(RuleBook b)
    {
        var small = new List<long[]>();
        foreach (var (k, v) in b.Customs) small.Add([0, k, v]);
        foreach (var (k, v) in b.LastSubsidyDay) small.Add([1, k, v]);
        foreach (var ((k, i), v) in b.CreditClosedUntil) small.Add([2, k, i, v]);
        foreach (var (k, v) in b.EvictionNotices) small.Add([3, k, v]);
        foreach (var (k, v) in b.EvictionOrders) small.Add([4, k, v]);
        foreach (var (k, v) in b.DemesneHours) small.Add([5, k, v]);
        foreach (var (k, v) in b.LabourCoin) small.Add([6, k, v]);
        foreach (var (k, v) in b.MarketCharters) small.Add([7, k, v.Day, v.Holder]);
        foreach (var (k, v) in b.Rulers) small.Add([10, k, v]);
        foreach (int k in b.Extended) small.Add([8, k, 0]);
        foreach (int k in b.Revoked) small.Add([9, k, 0]);
        return new RuleSave
        {
            Fiefs = [.. b.Fiefs.Values.Select(f => new long[] { f.Fief, f.Holder, f.Realm, f.OverlordFief, (long)f.Rights, f.ServiceMen, f.ServiceDays, f.RentInCoin ? 1 : 0, f.CommuteDay, f.MarketDays, f.WardUntilDay, f.Guardian, f.TookDay, f.Receipts[0], f.Receipts[1], f.Receipts[2], f.Receipts[3], f.Forfeited ? 1 : 0, f.PopLord })],
            Offices = [.. b.Offices.Select(o => new long[] { o.Serial, (long)o.Kind, o.Holder, o.Fief, o.Realm, o.WageMitesPerDay, o.Honesty, o.AppointedDay, o.Collected, o.Expected, o.Skimmed, o.Found, o.DismissedDay, o.SeasonSkim })],
            Honesty = [.. b.Honesty.Select(kv => new long[] { (long)kv.Key, kv.Value })],
            Dues = [.. b.Dues.Select(d => new long[] { d.Serial, (long)d.Kind, (long)d.Payer.Kind, d.Payer.Id, (long)d.Payee.Kind, d.Payee.Id, d.AmountMites, d.Units, d.DueDay, d.PaidMites, (long)d.State, (long)d.Entry })],
            Loans = [.. b.Loans.Select(l => new long[] { l.Serial, (long)l.Lender.Kind, l.Lender.Id, (long)l.Borrower.Kind, l.Borrower.Id, l.PrincipalMites, l.RatePmPerSeason, l.StartDay, l.TermSeasons, (long)l.Security, (long)l.DeedId, l.Repaid, l.InterestPaid, l.SeasonsPaid, l.LateSeasons, (long)l.State, (long)l.Entry })],
            Councils = [.. b.Councils.Select(c => { var r = new List<long> { c.Serial, c.Day, c.Realm, c.Yes, c.No, c.Passed ? 1 : 0, c.AmountMites }; foreach (var (m, y, s) in c.Votes) { r.Add(m); r.Add(y ? 1 : 0); r.Add(s); } return r.ToArray(); })],
            CouncilCauses = [.. b.Councils.Select(c => c.Cause)],
            Levies = [.. b.Levies.Select(l => new long[] { l.Serial, l.Realm, l.DueDay, l.TownPermille, l.CountryPermille })],
            Grievances = [.. b.Grievances.Select(kv => new long[] { (long)kv.Key, kv.Value.Points, kv.Value.UntilDay, (long)kv.Value.Entry })],
            Opinions = [.. b.Opinions.Select(kv => new long[] { (long)kv.Key.Who, (long)kv.Key.Of, kv.Value })],
            Small = [.. small],
            Counters = [b.NextOffice, b.NextDue, b.NextLoan, b.NextCouncil, b.NextLevy, b.LastDay, b.LastSeasonDay, b.LastLabourDay],
        };
    }

    public RuleBook Restore()
    {
        if (Counters.Length < 8 || Councils.Length != CouncilCauses.Length) throw new SaveException("Corrupt 'fiefs' section in save.");
        var b = new RuleBook { NextOffice = (int)Counters[0], NextDue = (int)Counters[1], NextLoan = (int)Counters[2], NextCouncil = (int)Counters[3], NextLevy = (int)Counters[4], LastDay = (int)Counters[5], LastSeasonDay = (int)Counters[6], LastLabourDay = (int)Counters[7] };
        foreach (var v in Fiefs)
        {
            if (v.Length < 19) throw new SaveException("Corrupt fief in save.");
            b.Fiefs[(int)v[0]] = new FiefRow
            {
                Fief = (int)v[0], Holder = v[1], Realm = (int)v[2], OverlordFief = (int)v[3], Rights = (FiefRights)v[4], ServiceMen = (int)v[5], ServiceDays = (int)v[6], RentInCoin = v[7] != 0, CommuteDay = (int)v[8], MarketDays = (byte)v[9],
                WardUntilDay = (int)v[10], Guardian = v[11], TookDay = (int)v[12], Receipts = [v[13], v[14], v[15], v[16]], Forfeited = v[17] != 0, PopLord = v[18],
            };
        }
        foreach (var v in Offices)
        {
            if (v.Length < 14) throw new SaveException("Corrupt office in save.");
            b.Offices.Add(new Office { Serial = (int)v[0], Kind = (OfficeKind)v[1], Holder = v[2], Fief = (int)v[3], Realm = (int)v[4], WageMitesPerDay = (int)v[5], Honesty = (byte)v[6], AppointedDay = (int)v[7], Collected = v[8], Expected = v[9], Skimmed = v[10], Found = v[11], DismissedDay = (int)v[12], SeasonSkim = v[13] });
        }
        foreach (var v in Honesty) { if (v.Length < 2) throw new SaveException("Corrupt honesty in save."); b.Honesty[(ulong)v[0]] = (byte)v[1]; }
        foreach (var v in Dues)
        {
            if (v.Length < 12) throw new SaveException("Corrupt due in save.");
            b.Dues.Add(new DueRow { Serial = (int)v[0], Kind = (DueKind)v[1], Payer = new Party((PartyKind)v[2], v[3]), Payee = new Party((PartyKind)v[4], v[5]), AmountMites = v[6], Units = (int)v[7], DueDay = (int)v[8], PaidMites = v[9], State = (DueState)v[10], Entry = (ulong)v[11] });
        }
        foreach (var v in Loans)
        {
            if (v.Length < 17) throw new SaveException("Corrupt loan in save.");
            b.Loans.Add(new Loan { Serial = (int)v[0], Lender = new Party((PartyKind)v[1], v[2]), Borrower = new Party((PartyKind)v[3], v[4]), PrincipalMites = v[5], RatePmPerSeason = (int)v[6], StartDay = (int)v[7], TermSeasons = (int)v[8], Security = (LoanSecurity)v[9], DeedId = (ulong)v[10], Repaid = v[11], InterestPaid = v[12], SeasonsPaid = (int)v[13], LateSeasons = (int)v[14], State = (LoanState)v[15], Entry = (ulong)v[16] });
        }
        for (int i = 0; i < Councils.Length; i++)
        {
            var v = Councils[i]; if (v.Length < 7 || (v.Length - 7) % 3 != 0) throw new SaveException("Corrupt council in save.");
            var c = new CouncilRecord { Serial = (int)v[0], Day = (int)v[1], Realm = (int)v[2], Yes = (int)v[3], No = (int)v[4], Passed = v[5] != 0, AmountMites = v[6], Cause = CouncilCauses[i] };
            for (int k = 7; k < v.Length; k += 3) c.Votes.Add((v[k], v[k + 1] != 0, (int)v[k + 2]));
            b.Councils.Add(c);
        }
        foreach (var v in Levies) { if (v.Length < 5) throw new SaveException("Corrupt levy in save."); b.Levies.Add(new LevyRow { Serial = (int)v[0], Realm = (int)v[1], DueDay = (int)v[2], TownPermille = (int)v[3], CountryPermille = (int)v[4] }); }
        foreach (var v in Grievances) { if (v.Length < 4) throw new SaveException("Corrupt grievance in save."); b.Grievances[(ulong)v[0]] = new GrievanceRow { Points = (int)v[1], UntilDay = (int)v[2], Entry = (ulong)v[3] }; }
        foreach (var v in Opinions) { if (v.Length < 3) throw new SaveException("Corrupt opinion in save."); b.Opinions[((ulong)v[0], (ulong)v[1])] = (int)v[2]; }
        foreach (var v in Small)
        {
            if (v.Length < 3) throw new SaveException("Corrupt rule table in save.");
            switch (v[0])
            {
                case 0: b.Customs[(int)v[1]] = (int)v[2]; break;
                case 1: b.LastSubsidyDay[(int)v[1]] = (int)v[2]; break;
                case 2: if (v.Length < 4) throw new SaveException("Corrupt rule table in save."); b.CreditClosedUntil[((byte)v[1], v[2])] = (int)v[3]; break;
                case 3: b.EvictionNotices[(int)v[1]] = (int)v[2]; break;
                case 4: b.EvictionOrders[(int)v[1]] = (int)v[2]; break;
                case 5: b.DemesneHours[(int)v[1]] = v[2]; break;
                case 6: b.LabourCoin[(int)v[1]] = v[2]; break;
                case 7: if (v.Length < 4) throw new SaveException("Corrupt rule table in save."); b.MarketCharters[(int)v[1]] = ((int)v[2], v[3]); break;
                case 8: b.Extended.Add((int)v[1]); break;
                case 9: b.Revoked.Add((int)v[1]); break;
                case 10: b.Rulers[(int)v[1]] = v[2]; break;
                default: throw new SaveException("Corrupt rule table in save.");
            }
        }
        return b;
    }
}
