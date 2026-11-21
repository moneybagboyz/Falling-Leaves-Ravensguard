using Ravensguard.Content;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Economy.Markets;
using Ravensguard.Sim.Goods;
using Ravensguard.Sim.People;
using Ravensguard.Sim.Rule;

namespace Ravensguard.Sim.Production.Industry;

/// <summary>What a founder expects of a chain at a place today (industries 5.12): the first-tier buildings' bill, the net a day of the reference sheet at the place's prices, what a day costs, and the payback in game days (0 when the sheet loses money).</summary>
public readonly record struct NpcSheet(string Chain, int Settlement, long BillMites, long NetPerDayMites, long CostPerDayMites, long PaybackDays)
{
    public bool Pays => NetPerDayMites > 0;
}

/// <summary>
/// NPC founders (P7-IN5, industries 5.12, overview Q6): a household with coin to spare looks at the trade once a working week (the utility is deterministic, so no stream: households and places in id order, the best payback wins, ties the lower id).
/// It founds a works of the chain whose trade the place already works (an iron mine, a clay pit or brickworks, a woodlot or sawpit), when the sheet's payback at the place's prices is within the limit, the idle hands to crew it are there and the book
/// holds the building materials. The first-tier buildings are paid for with real coin and real goods: the materials are bought from the place's book and built in (<see cref="DestroyReason.Materialised"/>), the labour is paid as wages to the place's building
/// trades. The founder then runs it like anyone: the crew the sheet asks for, hired by the labour market, selling at the gate; he draws the coin above a reserve as a dividend and shuts a works that runs at a loss.
/// </summary>
public sealed partial class IndustryWorld
{
    /// <summary>The idle adults a settlement could crew a works from on a day (the labour market's view); null: the founders never look (tests that do not want them) or find as many as they need.</summary>
    public Func<int, int, int>? IdleHands { get; set; }
    /// <summary>A price the founders' sheet and bill read instead of the place's book (tests): settlement and good to milli-mites a kilogram; null or 0: the book's mid.</summary>
    public Func<int, ushort, long>? NpcPrice { get; set; }
    /// <summary>Whether a place works the chain's trade (tests); null: the rule of <see cref="SiteOk"/>.</summary>
    public Func<string, int, bool>? NpcSite { get; set; }

    /// <summary>Switches the founders off (tests and a world rule); the content's <c>npc_founders</c> is the default.</summary>
    public bool NpcDisabled { get; set; }
    public bool NpcEnabled => Set.NpcFounders && !NpcDisabled;

    /// <summary>True for a works whose owner is not the hero.</summary>
    public bool IsNpc(Works w) => w.Owner >= 0 && w.Owner < People.Count && People.Status[w.Owner] != PersonStatus.Hero;

    private long PriceOf(int settlement, ushort good)
    {
        long p = NpcPrice?.Invoke(settlement, good) ?? 0; if (p > 0) return p;
        if (Prod.Markets != null) { long mid = Prod.Markets.BookForSettlement(settlement).MidMicro(good); if (mid > 0) return mid; }
        return Prod.Econ.Goods.BasePriceMilliMites(good);
    }

    private ChainDef? ChainDefOf(string chain) => Content.TryGet<ChainDef>(chain, out var d) ? d : null;

    /// <summary>The first-tier buildings' bill of a chain at a place's prices: the worker-hours at the settings' labour rate and the materials at the book's mid.</summary>
    public long L1BillMites(string chain, int settlement)
    {
        if (ChainDefOf(chain) is not { } d) return 0;
        long mites = (long)d.Build.LabourHours * Set.PlanLabourMites;
        foreach (var m in d.Build.Materials) if (Prod.Econ.Goods.TryIdOf(m.Good, out var g)) mites += m.Kg * PriceOf(settlement, g) / 1000;
        return mites;
    }

    /// <summary>The first-tier buildings' bill at the goods' base prices (the sheets' prices): what the payback of a works is measured against.</summary>
    public long BaseBillMites(string chain)
    {
        if (ChainDefOf(chain) is not { } d) return 0;
        long mites = (long)d.Build.LabourHours * Set.PlanLabourMites;
        foreach (var m in d.Build.Materials) if (Prod.Econ.Goods.TryIdOf(m.Good, out var g)) mites += m.Kg * Prod.Econ.Goods.BasePriceMilliMites(g) / 1000;
        return mites;
    }

    /// <summary>The founders' reference sheet of a chain at a place (5.11): sales at the gate's share of the mid, purchases at the mid, the wages and the rest, over a year of working days and off days.</summary>
    public NpcSheet SheetOf(string chain, int settlement)
    {
        if (ChainDefOf(chain) is not { } d) return new NpcSheet(chain, settlement, 0, 0, 0, 0);
        var cat = Prod.Econ.Goods; long sells = 0, buys = 0;
        foreach (var s in d.Sheet.Sells) if (cat.TryIdOf(s.Good, out var g)) sells += s.Kg * PriceOf(settlement, g) / 1000;
        foreach (var s in d.Sheet.Buys) if (cat.TryIdOf(s.Good, out var g)) buys += s.Kg * PriceOf(settlement, g) / 1000;
        sells = sells * Set.NpcGatePm / 1000;
        long wages = (long)d.Sheet.WagesMites * Prod.Econ.WageMultPm / 1000;
        long cost = buys + wages + d.Sheet.OtherMites;
        long work = sells - cost; int wd = Math.Clamp(d.Sheet.WorkDays, 1, 60);
        long net = (work * wd - (long)(60 - wd) * d.Sheet.OffDayMites) / 60;
        long bill = L1BillMites(chain, settlement);
        return new NpcSheet(chain, settlement, bill, net, cost, net <= 0 ? 0 : (bill + net - 1) / net);
    }

    /// <summary>Whether a place works the chain's trade: an open mine making the ore the chain smelts, or an open business of one of the kinds the chain names; a chain that names neither is open anywhere.</summary>
    public bool SiteOk(string chain, int settlement)
    {
        if (NpcSite != null) return NpcSite(chain, settlement);
        if (ChainDefOf(chain) is not { } d) return false;
        if (d.SiteMineral.Length == 0 && d.SiteKinds.Count == 0) return true;
        foreach (var b in Prod.Businesses)
        {
            if (b.State != BusinessState.Open || b.Settlement != settlement) continue;
            if (d.SiteMineral.Length > 0 && b.Mine >= 0 && b.Mine < Prod.Mines.Count && MineWorks.OreGood(Prod.Mines[b.Mine].Mineral) == d.SiteMineral) return true;
            if (d.SiteKinds.Count > 0 && d.SiteKinds.Contains(Prod.KindOf(b).Id)) return true;
        }
        return false;
    }

    private bool OwnsWorks(int person) { foreach (var w in Book.Works) if (!w.Closed && w.Owner == person) return true; return false; }

    // ------------------------------------------------------------------ the weekly look

    /// <summary>The founders' day (the industry system calls it at a fixed hour): on every <c>npc_check_days</c>th day the NPC works are tended and, below the limit, one household founds a works.</summary>
    public void NpcPass(int day, GameTime now)
    {
        if (!NpcEnabled || Prod.Markets == null || Ind.Chains.Length == 0) return;
        if (day % Math.Max(1, Set.NpcCheckDays) != 0) return;
        Tend(day, now);
        int held = 0; foreach (var w in Book.Works) if (!w.Closed && IsNpc(w)) held++;
        if (held >= Set.NpcMaxWorks) return;
        var hht = Prod.Pop.Households; var econ = Prod.Econ; var led = Led;
        for (int h = 0; h < hht.Count; h++)
        {
            if (hht.MemberCount[h] == 0) continue;
            int head = hht.Head[h];
            if (head < 0 || head >= People.Count || People.Status[head] != PersonStatus.Alive) continue;
            long bal = led.Balance(econ.HouseholdPurse[h]).Mites;
            if (bal < Set.NpcMinCapitalMites || OwnsWorks(head)) continue;
            if (People.AgeYears(head, day) < 18) continue;
            if (TryFound(head, h, bal, day, now)) return;
        }
    }

    private IEnumerable<int> PlacesOf(int owner)
    {
        int home = People.HomeSettlement[owner]; if (home < 0) yield break;
        yield return home;
        var region = Prod.Pop.Region; int fief = region.Settlements[home].FiefIndex;
        if (fief < 0) yield break;
        for (int s = 0; s < region.Settlements.Count; s++) if (s != home && region.Settlements[s].FiefIndex == fief) yield return s;
    }

    private bool TryFound(int owner, int hh, long bal, int day, GameTime now)
    {
        var markets = Prod.Markets!; NpcSheet best = default; string? bestChain = null; ChainDef? bestDef = null; long bestQuote = 0;
        foreach (int place in PlacesOf(owner).Distinct().OrderBy(p => p))
        {
            var book = markets.BookForSettlement(place);
            foreach (var ck in Ind.Chains)
            {
                if (ChainDefOf(ck.Id) is not { } d || d.Crew.Count == 0) continue;
                var sh = SheetOf(ck.Id, place);
                // every works of the chain already at the place sells into the same book: the founder counts it as one more share of the market, so his payback is that much longer
                int crowd = 0; foreach (var o in Book.Works) if (!o.Closed && o.Chain == ck.Id && o.Settlement == place) crowd++;
                sh = sh with { PaybackDays = sh.PaybackDays * (1 + crowd) };
                if (!sh.Pays || sh.PaybackDays > Set.NpcMaxPaybackDays) continue;
                if (bestChain != null && sh.PaybackDays >= best.PaybackDays) continue;
                if (!SiteOk(ck.Id, place)) continue;
                long quote = QuoteBill(d, book, out bool inStock); if (!inStock) continue;
                long reserve = bal * Set.NpcReservePm / 1000;
                if (bal - quote - reserve < sh.CostPerDayMites * Math.Max(1, Set.NpcCapitalDays / 4)) continue;          // after the bill and his reserve he must be able to carry the works for a quarter of the working capital's days
                int hands = 0; foreach (int c in d.Crew) hands += c;
                if (IdleHands != null && IdleHands(place, day) < hands) continue;
                best = sh; bestChain = ck.Id; bestDef = d; bestQuote = quote;
            }
        }
        if (bestChain == null || bestDef == null) return false;
        return Establish(owner, hh, bestChain, bestDef, best.Settlement, bal, bestQuote, day, now);
    }

    /// <summary>What buying the chain's materials from the book and paying its labour would cost today; <paramref name="inStock"/> is false when the book holds less than a bill's materials.</summary>
    private long QuoteBill(ChainDef d, MarketBook book, out bool inStock)
    {
        inStock = true; long mites = (long)d.Build.LabourHours * Set.PlanLabourMites;
        foreach (var m in d.Build.Materials)
        {
            if (!Prod.Econ.Goods.TryIdOf(m.Good, out var g)) { inStock = false; return 0; }
            long grams = m.Kg * 1000L;
            if (book.Goods[g].StockUnits < grams) { inStock = false; return 0; }
            mites += book.QuoteBuy(g, grams);
        }
        return mites;
    }

    private bool Establish(int owner, int hh, string chain, ChainDef d, int place, long bal, long quote, int day, GameTime now)
    {
        var payer = Prod.Econ.HouseholdPurse[hh];
        var sh = SheetOf(chain, place);
        long reserve = bal * Set.NpcReservePm / 1000;
        long paid = PayBuild(owner, hh, place, d, day);
        long left = Led.Balance(payer).Mites;
        long capital = Math.Min(sh.CostPerDayMites * Set.NpcCapitalDays, Math.Max(0, left - reserve));
        var r = Found(chain, owner, place, payer, capital, day);
        if (!r.Ok) return false;
        var w = Book.Works[^1];
        w.BuildingsCostMites = paid;
        for (int i = 0; i < w.Stages.Count && i < d.Crew.Count; i++) SetCrew(w.Stages[i].Business, d.Crew[i]);
        return true;
    }

    // ------------------------------------------------------------------ paying for the buildings (real coin, real goods)

    private static readonly Occupation[] BuildTrades = [Occupation.Carpenter, Occupation.Mason, Occupation.Labourer, Occupation.Woodcutter];
    private const int BuildersPaid = 60;

    /// <summary>
    /// Pays a chain's first-tier buildings: the materials are bought from the settlement's book (the book's price moves, the coin goes to the lots' owners) and built in; the labour is shared out as wages among the first 60 adults of the building trades of the place
    /// (the fief's treasury when there are none). Returns what was paid in all.
    /// </summary>
    private long PayBuild(int owner, int hh, int place, ChainDef d, int day)
    {
        var markets = Prod.Markets!; var led = Led; var econ = Prod.Econ; var book = markets.BookForSettlement(place);
        var party = markets.HouseholdParty(hh); var payer = econ.HouseholdPurse[hh]; long paid = 0;
        foreach (var m in d.Build.Materials)
        {
            if (!econ.Goods.TryIdOf(m.Good, out var g)) continue;
            var res = book.Buy(party, g, m.Kg * 1000L, 5000, day);
            if (res.Qty <= 0) continue;
            if (!party.Store.Destroy(g, res.Qty, DestroyReason.Materialised)) throw new InvalidOperationException("the founder's materials vanished");
            paid += res.PaidMites;
        }
        long labour = Math.Min((long)d.Build.LabourHours * Set.PlanLabourMites, led.Balance(payer).Mites);
        if (labour <= 0) return paid;
        var hands = new List<int>();
        for (int p = 0; p < People.Count && hands.Count < BuildersPaid; p++)
        {
            if (People.Status[p] != PersonStatus.Alive || People.HomeSettlement[p] != place || People.Household[p] < 0 || People.Household[p] == hh) continue;
            if (People.AgeYears(p, day) < 16 || Array.IndexOf(BuildTrades, People.Occupation[p]) < 0) continue;
            hands.Add(p);
        }
        if (hands.Count == 0)
        {
            int fief = Prod.Pop.Region.Settlements[place].FiefIndex;
            if (fief >= 0 && led.TryFind(PurseKind.Treasury, Prod.Pop.Region.Fiefs[fief].Id, out var tr) && led.Transfer(payer, tr, new Coin(labour), TransferReason.Rent)) return paid + labour;
            return paid;
        }
        long each = labour / hands.Count, rem = labour - each * hands.Count;
        for (int i = 0; i < hands.Count; i++)
        {
            long share = each + (i == 0 ? rem : 0);
            if (share > 0 && led.Transfer(payer, econ.HouseholdPurse[People.Household[hands[i]]], new Coin(share), TransferReason.Wage)) paid += share;
        }
        return paid;
    }

    // ------------------------------------------------------------------ running it: the dividend and the loss

    private void Tend(int day, GameTime now)
    {
        for (int i = 0; i < Book.Works.Count; i++)
        {
            var w = Book.Works[i];
            if (w.Closed || !IsNpc(w) || People.Status[w.Owner] != PersonStatus.Alive) continue;
            int age = day - w.StartedDay; if (age < 30) continue;
            var head = Prod.ById(new EntityId(w.Id)); if (head == null) continue;
            int hh = People.Household[w.Owner]; if (hh < 0) continue;
            int span = Math.Min(age, WorkStage.RingDays);
            long net = 0, cost = 0;
            foreach (var s in w.Stages) { net += s.Net(day, span); cost += s.Sum(WorkStage.Wages, day, span) + s.Sum(WorkStage.Inputs, day, span) + s.Sum(WorkStage.Upkeep, day, span); }
            if (age >= Set.NpcLossDays && net < 0 && w.CumulativeNetMites < 0) { CloseByOwner(w, now); continue; }
            long perDay = Math.Max(1, cost / span);
            long keep = perDay * Set.NpcCapitalDays * Set.NpcDividendPm / 1000;
            long take = Led.Balance(head.Purse).Mites - keep;
            if (take > 0) Cash(w.Id, Prod.Econ.HouseholdPurse[hh], -take);
        }
    }

    private void CloseByOwner(Works w, GameTime now)
    {
        CloseWorks(w);
        var who = People.IdOf(w.Owner); var place = Prod.Pop.Region.Settlements[w.Settlement].Id;
        Chron.Chron(now, "works_closed", "ch_ind_closed", [ChronParam.Person("owner", who), ChronParam.TextKey("chain", "ind_chain_" + w.Chain), ChronParam.Place("place", place)],
            [who, new EntityId(w.Id)], [new Cause(CauseKind.Industry, null, NoteKey: "ch_note_works_closed")]);
    }
}
