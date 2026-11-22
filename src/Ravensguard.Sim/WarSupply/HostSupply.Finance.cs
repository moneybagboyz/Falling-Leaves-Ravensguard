using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Economy.Markets;
using Ravensguard.Sim.People;
using Ravensguard.Sim.Rule;
using Ravensguard.Sim.WorldGen.Region;

namespace Ravensguard.Sim.WarSupply;

/// <summary>
/// What a campaign costs a day and what funds it (army-economy 4.5): <see cref="Pay"/>, the staple at the region's grain mid, the fodder at the books' mid and the carting; the funds are the realm's treasury, its hosts' pay chests, the
/// scutage called and not yet paid, the subsidies granted and not yet collected and the loan room the lenders offer. <see cref="DaysFundedMilli"/> is those over the daily cost (thousandths of a day).
/// </summary>
public sealed record CampaignBudget(long DailyCostMites, long PayMites, long StapleMites, long FodderMites, long CartingMites, long TreasuryMites, long ChestMites, long ScutageDueMites, long SubsidiesMites, long LoanRoomMites)
{
    public long FundsMites => TreasuryMites + ChestMites + ScutageDueMites + SubsidiesMites + LoanRoomMites;
    public long DaysFundedMilli => DailyCostMites > 0 ? FundsMites * 1000 / DailyCostMites : 0;
    /// <summary>Whole days funded (the campaign panel's figure, read by the war utility: start at 30 or more, below 10 seek a loan, a subsidy or peace).</summary>
    public int DaysFunded => (int)(DaysFundedMilli / 1000);
    public bool MayStart => DaysFunded >= SupplyConfig.CampaignStartDaysFunded;
    public bool SeekFunds => DaysFunded < SupplyConfig.CampaignSeekFundsDays;
}

/// <summary>What <see cref="HostSupply.Raise"/> and <see cref="HostSupply.Fund"/> raised, by source, in mites.</summary>
public sealed record FundResult(long Treasury, long Aid, long Loans, long Tallage, long ToChest);

public sealed partial class HostSupply
{
    private long MeanMidMicro(ushort g)
    {
        long sum = 0; int n = 0;
        foreach (var b in Economy.Markets.AllBooks()) { long m = b.MidMicro(g); if (m > 0) { sum += m; n++; } }
        return n == 0 ? 0 : sum / n;
    }

    private static long MitesOf(long grams, long microPerGram) => (long)(((Int128)grams * microPerGram + PriceCurve.Micro - 1) / PriceCurve.Micro);

    /// <summary>The cheapest staple's mean mid over the region's books, micro-mites a gram.</summary>
    private long StapleMidMicro() { long best = 0; foreach (var g in _stapleGoods) { long m = MeanMidMicro(g); if (m > 0 && (best == 0 || m < best)) best = m; } return best; }

    /// <summary>The daily cost of a roster with its animals over a plan of <paramref name="days"/> days: its pay (the levies' first days unpaid, so a short plan costs less of it), the staple and fodder at the books' mid and the carting (a twentieth of the food).</summary>
    private (long Pay, long Staple, long Fodder, long Carting) DailyCost(Roster men, int followers, AnimalCounts animals, int days)
    {
        int d = Math.Max(1, days); long pay = 0;
        for (int r = 0; r < Roster.Roles; r++)
        {
            var role = (TroopRole)r; long n = men.Counts[r]; if (n == 0) continue;
            pay += role == TroopRole.Levy ? n * SupplyConfig.Wage(role) * Math.Max(0, d - SupplyConfig.LevyFreeDays) / d : n * SupplyConfig.Wage(role);
        }
        long staple = MitesOf((long)men.Total * SupplyConfig.RationG + (long)followers * SupplyConfig.FollowerRationG, StapleMidMicro());
        var (hay, oats) = SupplyConfig.Fodder(animals);
        long fodder = (_hay == ushort.MaxValue ? 0 : MitesOf(hay, MeanMidMicro(_hay))) + (_oats == ushort.MaxValue ? 0 : MitesOf(oats, MeanMidMicro(_oats)));
        return (pay, staple, fodder, (staple + fodder) / 20);
    }

    /// <summary>
    /// The campaign's budget for a realm (army-economy 3.4, 4.5): the daily cost of the planned roster (with the animals and followers of the realm's live hosts) and the funds. The loan room is what the richest lenders of the realm would
    /// lend (an eighth of those that may lend at interest, half of what each holds); the subsidies are the granted levies not yet collected; the scutage due is what the realm's vassals owe and have not paid.
    /// </summary>
    public CampaignBudget Budget(int realm, Roster plan, int days)
    {
        int followers = 0; var animals = new AnimalCounts(0, 0, 0, 0); long chests = 0;
        foreach (var a in Book.Hosts.Values)
        {
            if (a.Realm != realm) continue;
            followers += a.Followers; animals = new AnimalCounts(animals.Riding + a.Riding, animals.Pack + a.Pack, animals.Draught + a.Draught, animals.Oxen + a.Oxen);
            chests += _econ.Ledger.Balance(Resolve(a).Chest).Mites;
        }
        var (pay, staple, fodder, carting) = DailyCost(plan, followers, animals, days);
        long treasury = RealmPurse(realm) is { IsNone: false } rp ? _econ.Ledger.Balance(rp).Mites : 0;
        long scutage = 0, subsidies = 0, loanRoom = 0; var rb = Rules;
        if (rb != null)
        {
            foreach (var d in rb.Dues) if (d.Kind == DueKind.Scutage && d.Payee == Party.OfRealm(realm) && d.State is DueState.Due or DueState.Arrears) scutage += d.Owing;
            var c = RuleWiring.Context(Economy.World, rb, RuleChron.Silent);
            subsidies = PendingSubsidies(c, rb, realm);
            loanRoom = LoanRoom(c, realm);
        }
        return new CampaignBudget(pay + staple + fodder + carting, pay, staple, fodder, carting, treasury, chests, scutage, subsidies, loanRoom);
    }

    private long PendingSubsidies(RuleCtx c, RuleBook rb, int realm)
    {
        long sum = 0; var hht = c.Households;
        foreach (var lv in rb.Levies)
        {
            if (lv.Realm != realm) continue;
            for (int h = 0; h < hht.Count; h++)
            {
                if (hht.MemberCount[h] == 0) continue;
                int s = hht.Settlement[h]; if (RealmOf(s) != realm) continue;
                int permille = c.Region.Settlements[s].Class == SettlementClass.Town ? lv.TownPermille : lv.CountryPermille;
                sum += Math.Min(RuleMath.OfMovables(RuleCouncil.Movables(c, h), permille), c.Led.Balance(_econ.HouseholdPurse[h]).Mites);
            }
        }
        return sum;
    }

    private long LoanRoom(RuleCtx c, int realm)
    {
        var hht = c.Households; var cands = new List<long>(); var borrower = Party.OfRealm(realm);
        if (RuleLoans.CreditClosed(c.B, borrower, Day)) return 0;
        for (int h = 0; h < hht.Count; h++)
        {
            if (hht.MemberCount[h] == 0 || hht.Class[h] == SocialClass.Peasant || RealmOf(hht.Settlement[h]) != realm) continue;
            if (!RuleLoans.MayLend(c, Party.OfHousehold(hht.IdOf(h)))) continue;
            cands.Add(c.Led.Balance(_econ.HouseholdPurse[h]).Mites);
        }
        cands.Sort((x, y) => y.CompareTo(x));
        long room = 0; foreach (long b in cands.Take(8)) room += b / Math.Max(2, c.S.LoanLenderMinMultiple);
        return room;
    }

    /// <summary>The roster, followers and animals of the realm's live hosts, priced by <see cref="Budget"/> for thirty days: the figure the war utility reads.</summary>
    public CampaignBudget BudgetOfHosts(int realm, int days = SupplyConfig.CampaignStartDaysFunded)
    {
        var plan = new Roster(); foreach (var a in Book.Hosts.Values) if (a.Realm == realm) for (int r = 0; r < Roster.Roles; r++) plan.Counts[r] += a.Men[r];
        return Budget(realm, plan, days);
    }

    /// <summary>Thousandths of a day the realm's live hosts are funded (the treasury, their chests, scutage, subsidies and the loan room over their daily cost).</summary>
    public long DaysFundedMilli(int realm) => BudgetOfHosts(realm).DaysFundedMilli;

    /// <summary>Whole days the realm's live hosts are funded (army-economy 3.4: the war utility starts a campaign at 30 and seeks funds under 10).</summary>
    public int DaysFunded(int realm) => (int)(DaysFundedMilli(realm) / 1000);

    /// <summary>
    /// The realm raises coin into its treasury until it holds <paramref name="target"/> (army-economy 4.5), in the rule's order: the fiefs' aid (half of each treasury of the realm, <c>Aid</c>), loans (existing coin from the realm's richest lenders,
    /// <c>RuleLoans</c>, a quarter of the want each, up to four), then tallage on the towns (<c>RuleCouncil.Tallage</c>: a tenth of a town's movables, the households' grievances, the mayor's opinion). Every step is a ledger transfer; what it
    /// cannot raise the host goes short of. Returns what each source gave.
    /// </summary>
    public (long Aid, long Loans, long Tallage) Raise(int realm, long target, int day)
    {
        var led = _econ.Ledger; var purse = RealmPurse(realm); long aid = 0, loans = 0, tallage = 0;
        var rb = Rules; if (purse.IsNone || rb == null) return (0, 0, 0);
        long Short() => target - led.Balance(purse).Mites;
        foreach (var t in Economy.Taxes.Treasuries.All)
        {
            if (Short() <= 0) break;
            if (t.Kind != Ravensguard.Sim.Taxes.TreasuryKind.Fief) continue;
            if (rb.FiefOf(t.Fief) is { } f && f.Realm != realm) continue;
            long give = Math.Min(Short(), led.Balance(t.Purse).Mites * 500 / 1000);
            if (give > 0 && led.Transfer(t.Purse, purse, new Coin(give), TransferReason.Aid)) aid += give;
        }
        var c = RuleWiring.Context(Economy.World, rb, Chron);
        var borrower = Party.OfRealm(realm);
        for (int k = 0; k < 4 && Short() > 0; k++)
        {
            long principal = Math.Max(1, Math.Min(Short(), Math.Max(1, (target - led.Balance(purse).Mites + loans) / 4)));
            var lender = RuleLoans.FindLender(c, borrower, principal, day); if (lender == null) break;
            var (loan, _) = RuleLoans.Lend(c, lender.Value, borrower, principal, 4, LoanSecurity.Customs, 0, day);
            if (loan == null) break;
            loans += principal;
        }
        Book.Stats[15] += loans;
        if (Short() > 0)
        {
            var towns = new List<(int Hh, int S)>(); var region = _pop.Region;
            for (int s = 0; s < region.Settlements.Count; s++)
                if (region.Settlements[s].Class == SettlementClass.Town && RealmOf(s) == realm) towns.Add((c.HouseholdsInSettlement(s), s));
            towns.Sort((x, y) => x.Hh != y.Hh ? y.Hh.CompareTo(x.Hh) : x.S.CompareTo(y.S));
            foreach (var (_, s) in towns) { if (Short() <= 0) break; tallage += RuleCouncil.Tallage(c, s, borrower, day); }
        }
        return (aid, loans, tallage);
    }

    /// <summary>
    /// The realm funds a host's chest for <paramref name="days"/> days of its pay and food (army-economy 4.5): what the treasury holds, after <see cref="Raise"/> has tried the rule's other sources, moves to the pay chest (a ledger transfer;
    /// the chest is physical). <c>campaign_funded</c> is written with the days funded and the sources.
    /// </summary>
    public FundResult Fund(HostId h, int days, int day)
    {
        var a = Acc(h); var rt = Resolve(a); var led = _econ.Ledger;
        var (pay, staple, fodder, carting) = DailyCost(a.Roster, a.Followers, a.Animals, days);
        long cost = (pay + staple + fodder + carting) * days, need = cost - led.Balance(rt.Chest).Mites;
        var purse = RealmPurse(a.Realm); if (purse.IsNone) purse = rt.Treasury;
        if (need <= 0 || purse.IsNone) return new FundResult(0, 0, 0, 0, 0);
        long treasury = Math.Min(need, led.Balance(purse).Mites), aid = 0, loans = 0, tallage = 0;
        if (treasury < need) (aid, loans, tallage) = Raise(a.Realm, need, day);
        long move = Math.Min(need, led.Balance(purse).Mites);
        if (move > 0 && purse != rt.Chest) led.Transfer(purse, rt.Chest, new Coin(move), TransferReason.Hoard);
        var res = new FundResult(treasury, aid, loans, tallage, move);
        int funded = (int)((led.Balance(rt.Chest).Mites) * days / Math.Max(1, cost));
        Chron.Chron(WriteTime(day), "campaign_funded", "ch_war_funded", [ChronParam.Count("days", funded), ChronParam.Mites("treasury", treasury), ChronParam.Mites("aid", aid), ChronParam.Mites("loans", loans), ChronParam.Mites("tallage", tallage)],
            a.Lord != 0 ? [new EntityId(a.Lord)] : [], [HostCause(a, CauseKind.Supply, "ch_note_war_funded")], ("host", a.Id.ToString()));
        return res;
    }

    /// <summary>
    /// The host's pay chest is taken (army-economy 4.5, AE-20): the whole chest moves to the captor's purse as <c>Plunder</c>, its gold pieces with it (the coin book moves the pieces a payment of that size carries); the host's account goes on
    /// with an empty chest. Returns the mites taken and writes <c>pay_chest_captured</c>.
    /// </summary>
    public long CapturePayChest(HostId h, PurseId captor, EntityId by, int day)
    {
        var a = Acc(h); var rt = Resolve(a); var led = _econ.Ledger;
        long sum = led.Balance(rt.Chest).Mites; if (sum <= 0 || captor == rt.Chest) return 0;
        if (!led.Transfer(rt.Chest, captor, new Coin(sum), TransferReason.Plunder)) return 0;
        Book.Stats[14] += sum;
        Chron.Chron(WriteTime(day), "pay_chest_captured", "ch_war_chest_taken", [ChronParam.Mites("amount", sum)], by.Value != 0 ? [by] : [], [HostCause(a, CauseKind.War, "ch_note_war_chest")], ("host", a.Id.ToString()));
        return sum;
    }
}
