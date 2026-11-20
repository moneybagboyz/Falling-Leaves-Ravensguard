using Ravensguard.Content;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;

namespace Ravensguard.Sim.Contracts;

/// <summary>
/// NPC companies (mercenaries, guards, builders) take contracts by the same scorer the hero's autopilot uses (spec 4.2): pay per hour of the work, the risk by the odds rule (a hook: watch mode's risk estimate),
/// the distance. A company that takes a contract works it for the days it needs and then succeeds or fails by the odds (stream 0x690), so the hero competes with them for the board: first come by the claim rule.
/// </summary>
public static class Companies
{
    /// <summary>What a company of this kind will take: mercenaries and guards the fighting families, builders the building ones.</summary>
    public static bool Fits(string companyKind, string family) => companyKind switch
    {
        "builder" => family is "build" or "repair",
        "farmhands" => false,                        // P6-D4: a crew of idle hands, handed the soil jobs by the soil works (never takes from the board by the scorer)
        _ => family is "guard" or "escort" or "hunt" or "clear" or "rescue" or "bounty",
    };

    /// <summary>The hours of work a contract is expected to take (service days at ten hours; a fight or a rescue a day's work; a house far longer).</summary>
    public static int HoursOf(WorkContract c, ContractKindDef k) => k.Family switch
    {
        "guard" or "escort" => (int)Math.Max(10, c.Param * 10),
        "build" => 120, "repair" => c.Kind == "soil_improvement" && c.Param > 0 ? (int)c.Param : 40,         // P6-D4: a soil job carries its worker-hours
        "clear" => 16, "rescue" => 12, "hunt" => 10,
        _ => 8,
    };

    /// <summary>
    /// The scorer, in mites an hour: <c>pay / hours x (100 - risk) / 100 - 2 x km</c>. The hero's autopilot weighs a contract the same way (<see cref="Score"/> with his own risk), so a contract is as good to him as to a company of the same strength.
    /// </summary>
    public static long Score(long payMites, int hours, int riskPct, long distanceM) => payMites / Math.Max(1, hours) * (100 - Math.Clamp(riskPct, 0, 100)) / 100 - 2 * (distanceM / 1000);

    /// <summary>The contract a company would take, or null: the best scorer above the company's minimum, ties by contract id (a draw of stream 0x690 keyed on the company and the day breaks equal scores between companies).</summary>
    public static WorkContract? Choose(ContractWorld w, ContractBook book, CompanyInfo co, int day, long minHourly)
    {
        WorkContract? best = null; long bestScore = long.MinValue; uint bestTie = uint.MaxValue;
        foreach (var c in book.Contracts.Values)
        {
            if (!c.Open || w.Content.Kind(c.Kind) is not { Crime: false } k || !Fits(co.Kind, k.Family)) continue;
            long sc = Score(c.PayMites, HoursOf(c, k), w.RiskPct(co, c), w.DistanceM(co.Settlement, c.Settlement));
            if (sc < minHourly) continue;
            uint tie = DetRng.NextUInt(w.Seed, day, co.Id.Value, RngStreams.CompanyTies, (uint)c.Id * 2);
            if (sc > bestScore || sc == bestScore && tie < bestTie) { best = c; bestScore = sc; bestTie = tie; }
        }
        return best;
    }

    /// <summary>One pass at the day's start: every idle company, in id order, takes the contract it likes best. Returns the contracts taken.</summary>
    public static List<WorkContract> TakeAll(ContractWorld w, ContractBook book, int day, long minHourly = -1)
    {
        var taken = new List<WorkContract>();
        if (minHourly < 0) minHourly = w.S.CompanyMinHourlyMites;
        foreach (var co in w.Companies().OrderBy(x => x.Id))
        {
            if (book.Contracts.Values.Any(c => c.State == WorkState.Taken && c.Taker == co.Id)) continue;
            if (Choose(w, book, co, day, minHourly) is not { } pick) continue;
            if (ContractSystem.Take(w, book, pick.Id, co.Id, new PurseId(co.Purse), day, byCompany: true) == TakeResult.Taken) taken.Add(pick);
        }
        return taken;
    }

    /// <summary>
    /// The companies' contracts that have run their days: success or failure by the odds, a draw of stream 0x690 (key the company and the day). Success pays the company's purse; failure charges its bond and sends
    /// the escrow home. The hook <see cref="ContractWorld.CompanyDone"/> lets the world see the effect (the band cleared, the captive free).
    /// </summary>
    public static int Resolve(ContractWorld w, ContractBook book, int day)
    {
        int n = 0;
        foreach (var c in book.Contracts.Values.Where(x => x.State == WorkState.Taken && x.ByCompany).ToList())
        {
            var k = w.Content.Kind(c.Kind); if (k == null) continue;
            var co = w.Companies().FirstOrDefault(x => x.Id == c.Taker);
            int days = Math.Max(1, (HoursOf(c, k) + 9) / 10) + (int)(w.DistanceM(co.Settlement, c.Settlement) / 20_000);
            if (day < c.TakenDay + days) continue;
            int risk = w.RiskPct(co, c);
            bool ok = DetRng.Range(w.Seed, day, c.Taker.Value, RngStreams.CompanyTies, (uint)c.Id * 2 + 1, 0, 100) >= risk;
            if (ok) { w.CompanyDone?.Invoke(c); ContractSystem.Complete(w, book, c, day); } else ContractSystem.Fail(w, book, c, day, "company_failed");
            n++;
        }
        return n;
    }
}

/// <summary>A builder's (or the hero's crew's) bid on a building contract: the materials, the work hours, the wage of an hour of the crew, each hand's own skill.</summary>
public sealed record Bidder(EntityId Id, string Name, int[] HandSkills, long MaterialsMites, long HoursNeeded, long CrewWagePerHourMites, bool IsHero, int Purse);

/// <summary>One bid as the board shows it: the price and the days the crew expects by its own hands.</summary>
public sealed record BidRow(Bidder Who, long TotalMites, int ExpectedDaysX10);

/// <summary>
/// The construction market (spec 4.3, closes Phase 5's carry-over): NPC households and businesses commission builders by contract; builders and the hero bid by one formula
/// (<c>materials + hours x crew wage x margin / 1,000</c>, the margin Phase 5's 1,450 pm); the lowest wins, ties by builder id; the expected days count each hand's own skill (a hand of skill 50 is a standard
/// worker; the pace is <c>500 + 10 x skill</c> permille), not a flat 50 for everyone.
/// </summary>
public static class ConstructionMarket
{
    public const int DayHours = 10;

    public static long BidMites(long materials, long hours, long crewWagePerHour, int marginPm) => materials + hours * crewWagePerHour * marginPm / 1000;

    /// <summary>The permille pace of a hand of this skill: 500 at skill 0, 1000 at 50, 1500 at 100.</summary>
    public static int PacePm(int skill) => 500 + 10 * Math.Clamp(skill, 0, 100);

    /// <summary>Days (tenths) the crew expects: the hours over what its hands do in a ten-hour day each at their own pace.</summary>
    public static int ExpectedDaysX10(long hours, IReadOnlyList<int> handSkills)
    {
        long perDayX1000 = 0; foreach (int s in handSkills) perDayX1000 += (long)PacePm(s) * DayHours;
        return perDayX1000 <= 0 ? 0 : (int)((hours * 1000 * 10 + perDayX1000 - 1) / perDayX1000);
    }

    public static List<BidRow> Bids(ContractSettingsDef s, IEnumerable<Bidder> bidders) =>
        [.. bidders.Select(b => new BidRow(b, BidMites(b.MaterialsMites, b.HoursNeeded, b.CrewWagePerHourMites, s.BidMarginPm), ExpectedDaysX10(b.HoursNeeded, b.HandSkills))).OrderBy(r => r.TotalMites).ThenBy(r => r.Who.Id)];

    /// <summary>
    /// Awards a building contract to the lowest bid within the client's budget: the escrow beyond the bid goes back to the client (the contract is paid what was bid), the winner takes the contract. A hero's winning bid
    /// leaves the contract taken by him. Returns the bids (cheapest first) and the winner, or null when no bid fits.
    /// </summary>
    public static (List<BidRow> Bids, BidRow? Winner) Award(ContractWorld w, ContractBook book, WorkContract c, IEnumerable<Bidder> bidders, int day)
    {
        var rows = Bids(w.S, bidders);
        var win = rows.FirstOrDefault(r => r.TotalMites <= c.PayMites && r.TotalMites > 0);
        if (win == null || !c.Open) return (rows, null);
        long extra = c.PayMites - win.TotalMites;
        if (extra > 0 && c.IssuerPurse >= 0 && w.Ledger.Transfer(new PurseId(c.Escrow), new PurseId(c.IssuerPurse), new Coin(extra), TransferReason.ContractRefund)) c.PayMites = win.TotalMites;
        var res = ContractSystem.Take(w, book, c.Id, win.Who.Id, new PurseId(win.Who.Purse), day, byCompany: !win.Who.IsHero);
        return res == TakeResult.Taken ? (rows, win) : (rows, null);
    }
}

/// <summary>
/// The autopilot's side of a contract offer (AP-8, Phase 3): a contract worth at least five days of the hero's wage is a big choice, so dialogue raises <c>offer_big</c> and the router pauses (or, with the AI
/// on, declines it and records the decline). Smaller contracts are the AI's own business.
/// </summary>
public static class ContractOffers
{
    public static bool IsBig(ContractSettingsDef s, long payMites, long dailyWageMites) => dailyWageMites > 0 && payMites >= s.OfferBigWageDays * dailyWageMites;

    /// <summary>The authored node an offer stands for: always with its decline branch (AP-8's content rule holds for every contract kind).</summary>
    public static Ravensguard.Sim.Autopilot.BigNode NodeFor(ContractWorld w, WorkContract c) => new("offer_" + c.Kind, IsBig(w.S, c.PayMites, w.DailyWageMites), true);

    /// <summary>Offers a contract to the autopilot: raises <c>BigChoice</c> through the router when it is big. Returns the router's result, or null when the offer is small and the AI may take it on its own.</summary>
    public static Ravensguard.Sim.Autopilot.RouteResult? Offer(ContractWorld w, WorkContract c, Ravensguard.Sim.Autopilot.InterruptRouter router, Ravensguard.Sim.Autopilot.AutopilotState st, Ravensguard.Sim.Travel.InterruptBus bus, long sec)
        => IsBig(w.S, c.PayMites, w.DailyWageMites) ? router.Raise(st, bus, Ravensguard.Sim.Travel.InterruptKind.BigChoice, sec, 0, 0, c.Kind) : null;
}
