using System.Text;
using Ravensguard.Sim.Caravans;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Goods;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.People;
using Ravensguard.Sim.Production;
using Ravensguard.Sim.Production.Industry;
using Ravensguard.Sim.WorldGen.Region;

namespace Ravensguard.Sim.Diagnostics;

/// <summary>How the scripted L3 iron works (P7-EA3b) sells its bar: at the gate (the best book in reach), or on its own caravans to a neighbouring region over the border gate (industries 10, the "ship far" rule).</summary>
public enum L3Sale { Gate, Caravan }

/// <summary>
/// The second audit run's scripted L3 iron works (economy-audit 5.2, "an L3 iron works near the route"; overview 10b: a big works keeps its output and ships far). A harness fixture, not a game rule: a works of the iron chain is founded at a town on the
/// march route by a rich household of that town (its owner is an ordinary person), every stage is set to tier 3 with the crew at the tier's top and the powered machines assumed built and running (the stages carry no site, so recognition leaves the tier
/// alone), the hiring hall fills the empty posts from other businesses' labourers (a fully employed town has no idle hands to give), and the works runs on the region's real fuel and ore. The building bills are not paid (nobody builds): <see cref="BillMites"/> is
/// the sheet of the three tiers at the base prices, put on the works as its <c>BuildingsCostMites</c> so the sim's own payback day is the measure. The working capital is seed money created once (<see cref="SeedMites"/>, subtracted from the money supply in the
/// table); the founders' tending leaves the works alone because its start day lies in the future. <see cref="FuelFaucet"/> keeps the book's charcoal up (the IN-20 rig's faucet) for the run that asks what the works does when fuel is no limit.
/// </summary>
public sealed class L3IronWorks(WarLoadRun run, L3Sale sale, bool fuelFaucet = false, long capitalMites = 3_000_000)
{
    public Works? Works { get; private set; }
    public int Settlement { get; private set; } = -1;
    public int StartDay { get; private set; } = -1;
    public long SeedMites { get; private set; }
    public long BillMites { get; private set; }
    public long L1BillMites { get; private set; }
    public bool FuelFaucet => fuelFaucet;
    public L3Sale Sale => sale;
    public int Neighbour { get; private set; } = -1;
    public string Note { get; private set; } = "";
    /// <summary>Grams of charcoal created by the faucet (a harness source of goods, reported).</summary>
    public long SeedGoodsG { get; private set; }
    /// <summary>Labourers pulled from other businesses to crew the works (the hall), counted.</summary>
    public int HallHires { get; private set; }
    private readonly List<int> _pool = [];

    private IndustryWorld Iw => run.State.HomeRecords!.Sim.Get<IndustryWorld>()!;

    /// <summary>Founds the works on <paramref name="day"/> at a town of the route (a town within 8 km of a stop, one that already works iron preferred). Returns false and sets <see cref="Note"/> when none can be placed.</summary>
    public bool Place(int day, IEnumerable<int> route)
    {
        var region = run.State.Population.Region; var iw = Iw; var pt = run.State.Population.People;
        var stops = route.Distinct().ToArray();
        int pick = -1; bool pickSite = false; long pickDist = long.MaxValue;
        for (int s = 0; s < region.Settlements.Count; s++)
        {
            if (region.Settlements[s].Class != SettlementClass.Town) continue;
            if (run.Economy.Markets.BookOf[s] != s) continue;
            long best = long.MaxValue;
            foreach (int r in stops) { long dx = region.Settlements[s].CenterX - region.Settlements[r].CenterX, dy = region.Settlements[s].CenterY - region.Settlements[r].CenterY; best = Math.Min(best, dx * dx + dy * dy); }
            if (best > 8000L * 8000L) continue;
            bool site = iw.SiteOk("iron", s);
            if (pick < 0 || (site && !pickSite) || (site == pickSite && best < pickDist)) { pick = s; pickSite = site; pickDist = best; }
        }
        if (pick < 0) { Note = "no town within 8 km of the route"; return false; }
        // the owner: the richest head of a household of the town aged 26 to 40 (a works closes when its owner dies, IndustryWorld.cs: an old owner would end the measure early; finding EA_F20)
        int owner = -1, ownerHh = -1; long ownerBal = -1; var hht = run.State.Population.Households; var econ = run.Econ;
        for (int h = 0; h < hht.Count; h++)
        {
            if (hht.MemberCount[h] == 0) continue; int head = hht.Head[h];
            if (head < 0 || head >= pt.Count || pt.Status[head] != PersonStatus.Alive || pt.HomeSettlement[head] != pick || pt.AgeYears(head, day) is < 26 or > 40) continue;
            long bal = econ.Ledger.Balance(econ.HouseholdPurse[h]).Mites;
            if (bal > ownerBal) { ownerBal = bal; owner = head; ownerHh = h; }
        }
        if (owner < 0) { Note = "no household head in the town"; return false; }
        var payer = econ.HouseholdPurse[ownerHh];
        econ.Ledger.Mint(payer, new Coin(capitalMites), SourceReason.Seed); SeedMites = capitalMites;
        var res = iw.Found("iron", owner, pick, payer, capitalMites, day);
        if (!res.Ok) { Note = "found refused: " + res.Key; return false; }
        var w = iw.Book.Works[^1]; Works = w; Settlement = pick; StartDay = day;
        w.StartedDay = 1_000_000;                                     // in the future: the founders' tending (a loss closes a works, a dividend is drawn) leaves it alone
        iw.PowerPm = (works, _) => works == w ? 1000 : 0;             // the L3 machines are assumed built and powered; no other works has a powered stage that matters at this tag
        L1BillMites = iw.L1BillMites("iron", pick);
        long bill = 0;
        var ck = run.Economy.Production.Content.Industries.Chain("iron")!;
        foreach (var sk in ck.Stages) for (int u = 1; u <= 3 && u <= sk.TierCount; u++) bill += iw.PlanBill(sk.Tier(u).Plan);
        BillMites = bill;
        w.BuildingsCostMites = bill;
        foreach (var st in w.Stages)
        {
            iw.SetTier(st.Business, 3);
            var kind = iw.KindOf(st); iw.SetCrew(st.Business, kind.Tier(st.Tier).CrewMax);
        }
        if (sale == L3Sale.Caravan) SetUpCaravans();
        Hall();
        Note = $"town {pick} (iron site {(pickSite ? "yes" : "no")}), owner {owner}" + (sale == L3Sale.Caravan ? $", caravans to region {Neighbour}" : ", gate sale");
        return true;
    }

    private void SetUpCaravans()
    {
        var w = Works!; var st = run.State; var iw = Iw; var cont = st.ContinentState; if (cont == null) { Note = "no continent"; return; }
        var rl = cont.Link(st.HomeRegion.Id);
        int best = -1;
        foreach (var e in rl.Edges.OrderBy(e => e.Neighbour))
        {
            if (cont.Summaries[e.Neighbour].Detailed || cont.Summaries[e.Neighbour].Pop <= 0 || cont.Edges[e.Edge].Closed) continue;
            if (iw.Caravans.PathOf(w.Settlement, new CaravanOrder { Kind = DestKind.Neighbour, Dest = e.Neighbour }, out _) == null) continue;
            best = e.Neighbour; break;
        }
        if (best < 0) { Note = "no reachable neighbour"; return; }
        Neighbour = best;
        var last = w.Stages[^1];
        iw.SetCaravanOrder(last.Business, DestKind.Neighbour, best, 2, 4, 2, true);
        iw.SetRoute(last.Business, Routing.Caravan);
        // a free trader of the town (Trading 60) to lead the caravans: the works hires the best free trader
        var pt = run.State.Population.People; int day = (int)(run.HourDone / 24);
        for (int p = 0; p < pt.Count; p++)
            if (pt.IsAlive(p) && pt.Status[p] == PersonStatus.Alive && pt.HomeSettlement[p] == w.Settlement && pt.Sex[p] == Sex.Male && pt.AgeYears(p, day) is >= 25 and < 50 && pt.Occupation[p] == Occupation.None && pt.Household[p] >= 0)
            { pt.SetSkill(p, 0, Skill.Trading, 60); break; }
    }

    /// <summary>Called at the start of each day (before the host slot): the hiring hall for the works, and the fuel faucet when asked.</summary>
    public void Daily(int day)
    {
        if (Works == null || Works.Closed) return;
        Tally(); TopUp();
        if (fuelFaucet) Stock("charcoal", Math.Max(12_000_000, 26 * run.Economy.Markets.BookForSettlement(Settlement).Goods[run.Econ.Goods.IdOf("charcoal")].Demand), day);
        Hall();
    }

    /// <summary>The owner keeps the works' purse above a floor from seed money (counted in <see cref="SeedMites"/> and <see cref="TopUps"/>): the audit asks what the works earns, and the capital it needs is reported, not hidden.</summary>
    private void TopUp()
    {
        var head = run.Economy.Production.ById(new EntityId(Works!.Id)); if (head == null) return;
        if (run.Econ.Ledger.Balance(head.Purse).Mites >= TopUpFloorMites) return;
        var payer = run.Econ.HouseholdPurse[run.State.Population.People.Household[Works.Owner]];
        run.Econ.Ledger.Mint(payer, new Coin(TopUpMites), SourceReason.Seed);
        if (Iw.Cash(Works.Id, payer, TopUpMites).Ok) { SeedMites += TopUpMites; TopUps++; }
    }
    public const long TopUpFloorMites = 600_000, TopUpMites = 1_000_000;
    /// <summary>How many times the owner topped the works' purse up.</summary>
    public int TopUps { get; private set; }

    private void Stock(string good, long minG, int day)
    {
        var book = run.Economy.Markets.BookForSettlement(Settlement); var id = run.Econ.Goods.IdOf(good); var gb = book.Goods[id];
        if (gb.StockUnits >= minG || book.Merchants.Count == 0) return;
        var m = book.Merchants[0]; long add = minG - gb.StockUnits; SeedGoodsG += add;
        m.Store.Create(id, add, CreateReason.Seed); book.List(m, id, add, 0, day, true);
    }

    private void FreeHands(int n)
    {
        var pt = run.State.Population.People; var prod = run.Economy.Production; int freed = 0; int day = (int)(run.HourDone / 24);
        foreach (var b in prod.Businesses.ToList())
        {
            if (prod.KindOf(b).Stage != null) continue;
            for (int i = b.Staff.Count - 1; i >= 0 && freed < n; i--)
            {
                int p = b.Staff[i];
                if (pt.Occupation[p] is Occupation.Spinner or Occupation.Labourer && pt.AgeYears(p, day) >= 18 && pt.Status[p] == PersonStatus.Alive) { _pool.Add(p); prod.DropStaff(b, i, true); freed++; }
            }
        }
    }

    private void Hall()
    {
        var w = Works!; var iw = Iw; var prod = run.Economy.Production; var pt = run.State.Population.People;
        foreach (var st in w.Stages)
        {
            var bz = prod.ById(new EntityId(st.Business)); if (bz == null || !iw.SlotsFor(bz, out var slots)) continue;
            var kd = iw.KindOf(st);
            foreach (var (occ, cnt, _) in slots)
            {
                int have = bz.Staff.Count(p => pt.Occupation[p] == occ);
                for (; have < cnt; have++)
                {
                    if (_pool.Count == 0) FreeHands(60);
                    if (_pool.Count == 0) return;
                    int p = _pool[^1]; _pool.RemoveAt(_pool.Count - 1); HallHires++;
                    pt.Occupation[p] = occ; pt.SetSkill(p, 0, kd.Skill == Skill.None ? Skill.Farming : kd.Skill, LabourMarket.StartSkill); prod.Employ(bz, p, occ);
                }
            }
        }
    }

    private readonly Dictionary<ulong, long> _year = [];
    private string StoreKg(WorkStage s)
    {
        var b = run.Economy.Production.ById(new EntityId(s.Business)); if (b == null) return "0";
        var top = b.Store.Rows().OrderByDescending(r => run.Econ.Goods.MassG(r.Good, r.Qty)).Take(3).Select(r => $"{run.Econ.Goods.StringId(r.Good)} {run.Econ.Goods.MassG(r.Good, r.Qty) / 1000:N0}");
        return (b.Store.TotalMassG() / 1000).ToString("N0") + " (" + string.Join(", ", top) + ")";
    }
    /// <summary>Adds each stage's last day to the year's tally (the day's status is read at the next day's start).</summary>
    private void Tally() { foreach (var st in Works!.Stages) if (Iw.Status(st.Business) is { } x) _year[st.Business] = _year.GetValueOrDefault(st.Business) + x.MadeG; }
    /// <summary>Clears the year's tallies (the soak calls it after the row).</summary>
    public void NextYear() => _year.Clear();

    /// <summary>The works' state at a day: the last 60 days' books and where each stage stands.</summary>
    public string Status(int day)
    {
        var w = Works; if (w == null) return "no works";
        var iw = Iw; var sb = new StringBuilder();
        long net60 = w.Stages.Sum(s => s.Net(day, WorkStage.RingDays)), income60 = w.Stages.Sum(s => s.Sum(WorkStage.Income, day)), wages60 = w.Stages.Sum(s => s.Sum(WorkStage.Wages, day)), inputs60 = w.Stages.Sum(s => s.Sum(WorkStage.Inputs, day)), up60 = w.Stages.Sum(s => s.Sum(WorkStage.Upkeep, day));
        sb.Append($"net60 {net60:N0} (income {income60:N0} wages {wages60:N0} inputs {inputs60:N0} upkeep {up60:N0}); cum {w.CumulativeNetMites:N0} of bill {BillMites:N0}; paid back day {w.PaidBackDay}; top-ups {TopUps} (seed {SeedMites:N0}); trips {w.CaravanTrips} lost {w.CaravansLost} sold {w.CaravanSoldMites:N0} net {w.CaravanNetMites:N0}; stages:");
        foreach (var s in w.Stages)
            if (iw.Status(s.Business) is { } st) sb.Append($" [{iw.KindOf(s).Id}: {st.Stop}{(st.Stop != StageStop.None && st.StopGood != ushort.MaxValue ? " " + run.Econ.Goods.StringId(st.StopGood) : "")}, crew {st.Have}/{st.Need}, today made {st.MadeG / 1000} moved {st.MovedG / 1000} of potential {st.PotentialG / 1000} kg, store {StoreKg(s)} kg, year made {_year.GetValueOrDefault(s.Business) / 1000:N0} kg]");
        return sb.ToString();
    }
}
