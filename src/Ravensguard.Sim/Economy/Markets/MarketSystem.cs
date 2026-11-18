using Ravensguard.Content;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Goods;
using Ravensguard.Sim.People;
using Ravensguard.Sim.WorldGen.Region;

namespace Ravensguard.Sim.Economy.Markets;

/// <summary>Anything that adds structural demand to the books (businesses: planned recipe inputs, money spec 3.1).</summary>
public interface IStructuralDemandSource
{
    /// <summary>Adds units per day to <c>demand[bookSettlementIndex][good]</c>.</summary>
    void AddStructuralDemand(MarketSystem markets, long[][] demand);
}

/// <summary>
/// The households' consumption mix (decision MK1): how a class's staple and relish measures split over the goods that satisfy them. The shopper orders by it and
/// the structural demand counts by it, so the price curve sees what households actually want.
/// </summary>
public sealed class ConsumptionMix
{
    public (ushort Good, int Permille)[] Staple { get; }
    public (ushort Good, int Permille)[] Relish { get; }
    public ConsumptionMix(GoodCatalog c)
    {
        (ushort, int)[] Make((string, int)[] src) => [.. src.Where(x => c.TryIdOf(x.Item1, out _)).Select(x => (c.IdOf(x.Item1), x.Item2))];
        Staple = Make([("wheat", 250), ("rye", 100), ("barley", 50), ("oats", 50), ("flour", 250), ("bread", 300)]);
        Relish = Make([("peas", 200), ("beans", 100), ("vegetables", 250), ("cheese", 100), ("eggs", 100), ("fish_fresh", 100), ("meat_fresh", 100), ("fish_salted", 25), ("meat_salted", 25)]);
    }
}

/// <summary>
/// The markets of one region (money spec 2.4, 3.4-3.6): a <see cref="MarketBook"/> for every town and village, the road-nearest book for each farmstead, the
/// households' parties, structural demand and the step functions at their tick places (H3 session, H5 daily globals, S5 price drift). Plain class with
/// explicit parameters, shaped for the abstract tick; it does no scheduling of its own.
/// </summary>
public sealed class MarketSystem
{
    public EconomyState Econ { get; }
    public Population Pop { get; }
    public ulong Seed { get; }
    public MarketBook?[] Books { get; }
    /// <summary>Per settlement index: the settlement index of the book it uses.</summary>
    public int[] BookOf { get; }
    public ConsumptionMix Mix { get; }
    public IStructuralDemandSource? Production { get; set; }
    private MarketParty?[] _hh;
    private int _grainPermille;
    /// <summary>The grain goods' share of the staple mix in permille (what a landed household's staple demand is divided by).</summary>
    private int GrainPermille => _grainPermille > 0 ? _grainPermille : _grainPermille = Math.Max(1, Mix.Staple.Where(m => Econ.Food.IsGrain(m.Good)).Sum(m => m.Permille));
    private readonly bool[] _basketGood;
    private readonly ushort[] _durable;

    public MarketSystem(EconomyState econ, Population pop, ulong seed)
    {
        Econ = econ; Pop = pop; Seed = seed; Mix = new ConsumptionMix(econ.Goods);
        var r = pop.Region;
        int n = r.Settlements.Count;
        Books = new MarketBook?[n]; BookOf = new int[n]; _hh = new MarketParty?[pop.Households.Count];
        var counts = new int[n];
        for (int p = 0; p < pop.People.Count; p++) { int s = pop.People.HomeSettlement[p]; if (s >= 0) counts[s]++; }
        var hosts = new List<int>();
        for (int s = 0; s < n; s++)
            if (r.Settlements[s].Class is SettlementClass.Town or SettlementClass.Village) hosts.Add(s);
        if (hosts.Count == 0) throw new InvalidOperationException("region has no town or village for a market");
        for (int s = 0; s < n; s++)
        {
            var st = r.Settlements[s];
            if (st.Class is SettlementClass.Town or SettlementClass.Village) BookOf[s] = s;
            else
            {
                int best = hosts[0]; long bd = long.MaxValue;
                foreach (int h in hosts) { long dx = r.Settlements[h].CenterX - st.CenterX, dy = r.Settlements[h].CenterY - st.CenterY, d = dx * dx + dy * dy; if (d < bd) { bd = d; best = h; } }
                BookOf[s] = best;
            }
        }
        var pops = new int[n];
        for (int s = 0; s < n; s++) pops[BookOf[s]] += counts[s];
        foreach (int s in hosts)
        {
            var st = r.Settlements[s];
            PurseId toll = st.FiefIndex >= 0 ? econ.Ledger.Open(PurseKind.Treasury, r.Fiefs[st.FiefIndex].Id) : econ.Ledger.Open(PurseKind.Settlement, st.Id);
            Books[s] = new MarketBook(econ, s, st.Id, st.Name, toll, pops[s], seed);
        }
        _durable = [econ.Goods.IdOf("shoes"), econ.Goods.IdOf("clothes"), econ.Goods.IdOf("tools")];
        _basketGood = new bool[econ.Goods.Count];
        foreach (var (g, _) in Mix.Staple) _basketGood[g] = true;
        foreach (var (g, _) in Mix.Relish) _basketGood[g] = true;
        _basketGood[econ.Food.Ale] = _basketGood[econ.Food.Salt] = _basketGood[econ.Food.Firewood] = true;
        foreach (var b in Books) if (b != null) SetFloors(b);
    }

    private void SetFloors(MarketBook b)
    {
        var c = Econ.Goods;
        for (ushort g = 0; g < c.Count; g++)
        {
            // 0.02 x population g/day for every listed good, 0.25 x population x its share of the basket for basket goods
            long grams = (long)b.Population * 20 / 1000;
            if (_basketGood[g]) grams = Math.Max(grams, (long)b.Population * 1100 / 4 / Math.Max(1, BasketWidth(g)));
            b.Goods[g].FloorDemand = c.IsPiece(g) ? Math.Max(1, grams / Math.Max(1, c.Def(g).PieceMassG)) : grams;
        }
    }

    private int BasketWidth(ushort g)
    {
        if (g == Econ.Food.Ale || g == Econ.Food.Salt || g == Econ.Food.Firewood) return 1;
        foreach (var (x, _) in Mix.Staple) if (x == g) return Mix.Staple.Length;
        return Mix.Relish.Length * 2;
    }

    public MarketBook BookForSettlement(int settlement) => Books[BookOf[settlement]]!;
    public MarketBook BookForHousehold(int hh) => BookForSettlement(Pop.Households.Settlement[hh]);
    public IEnumerable<MarketBook> AllBooks() { foreach (var b in Books) if (b != null) yield return b; }

    public MarketParty HouseholdParty(int hh)
    {
        if (hh >= _hh.Length) Array.Resize(ref _hh, Math.Max(hh + 1, _hh.Length * 2));
        return _hh[hh] ??= new MarketParty(Pop.Households.IdOf(hh), Econ.HouseholdPurse[hh], Econ.HouseholdStore[hh], PartyKind.Household, hh);
    }

    // ---------- tick steps ----------

    /// <summary>H3: one session in every open book, settlements ascending (money spec 4).</summary>
    public (int Filled, long Mites) HourlySession(GameTime now)
    {
        int filled = 0; long mites = 0;
        foreach (var b in AllBooks())
        {
            if (!b.IsOpen(now.Hour)) continue;
            var (f, m) = b.RunSession(now.TotalHours, (int)now.TotalDays);
            filled += f; mites += m;
        }
        return (filled, mites);
    }

    /// <summary>H5 (hour 0): structural demand, then the demand EMA, expiry and import requests (money spec 4).</summary>
    public void DailyGlobals(GameTime now, int tMeanC = 10)
    {
        int day = (int)now.TotalDays;
        var demand = ComputeStructuralDemand(day, tMeanC);
        foreach (var b in AllBooks())
        {
            var d = demand[b.SettlementIndex];
            for (int g = 0; g < d.Length; g++) b.Goods[g].StructuralDemand = d[g];
            b.Daily(day);
            PostImportRequests(b, day);
        }
    }

    private void PostImportRequests(MarketBook b, int day)
    {
        b.ImportRequests.Clear();
        if (b.Merchants.Count == 0) return;
        for (ushort g = 0; g < b.Goods.Length; g++)
        {
            var gb = b.Goods[g];
            if (gb.StockUnits * 2 >= gb.Demand * gb.TargetCoverDays) continue;
            if (gb.LastTradedDay < 0 && gb.DemandEma == 0) continue;
            long want = gb.Demand * gb.TargetCoverDays / 2 - gb.StockUnits;
            if (want > 0) b.ImportRequests.Add(new ImportRequest(b.SettlementIndex, g, want, day, b.Merchants[0].Id));
        }
    }

    /// <summary>S5: <c>Ref</c> drifts a quarter of the way toward <c>base x clamp(local coin per person / 5,000 mites, 0.5, 2)</c> (money spec 3.5).</summary>
    public void SeasonalDrift(GameTime now)
    {
        var coin = new long[Books.Length];
        var hh = Pop.Households;
        for (int h = 0; h < hh.Count; h++) coin[BookOf[hh.Settlement[h]]] += Econ.Ledger.Balance(Econ.HouseholdPurse[h]).Mites;
        foreach (var (s, mites) in ExtraCoin) coin[BookOf[s]] += mites;
        foreach (var b in AllBooks()) b.DriftRefs(coin[b.SettlementIndex]);
    }

    /// <summary>Coin held by other local accounts (business cash boxes), per settlement; set by the production system before <see cref="SeasonalDrift"/>.</summary>
    public List<(int Settlement, long Mites)> ExtraCoin { get; } = [];

    // ---------- structural demand ----------

    /// <summary>Households' basket demand by good plus the planned inputs of businesses, in units per day, per book (money spec 3.1).</summary>
    public long[][] ComputeStructuralDemand(int day, int tMeanC)
    {
        var res = new long[Books.Length][];
        foreach (var b in AllBooks()) res[b.SettlementIndex] = new long[Econ.Goods.Count];
        var hh = Pop.Households; var c = Econ.Goods;
        for (int h = 0; h < hh.Count; h++)
        {
            if (hh.MemberCount[h] == 0) continue;
            var arr = res[BookOf[hh.Settlement[h]]];
            var need = HouseholdNeeds.Basket(Econ, Pop.People, hh, h, day, tMeanC);
            // a household with land eats its own grain, milled for the toll and baked at home (decision I17, I18): its staple demand is all grain
            bool grainOnly = hh.ParcelCount[h] > 0;
            foreach (var (g, pm) in Mix.Staple)
            {
                if (grainOnly) { if (!Econ.Food.IsGrain(g)) continue; arr[g] += HouseholdNeeds.GramsFor(c, g, (long)need.StapleX100 * pm / GrainPermille); }
                else arr[g] += HouseholdNeeds.GramsFor(c, g, (long)need.StapleX100 * pm / 1000);
            }
            foreach (var (g, pm) in Mix.Relish) arr[g] += HouseholdNeeds.GramsFor(c, g, (long)need.RelishX100 * pm / 1000);
            if (!grainOnly || hh.Class[h] == SocialClass.Burgher) arr[Econ.Food.Ale] += need.AleMl * c.Def(Econ.Food.Ale).LitreMassG / 1000;   // landed households brew their own (decision I23)
            arr[Econ.Food.Salt] += need.SaltG; if (hh.Class[h] == SocialClass.Burgher) arr[Econ.Food.Firewood] += need.FuelG;   // the others gather their own (decision I22)
        }
        Production?.AddStructuralDemand(this, res);
        return res;
    }

    /// <summary>
    /// World start (integration decision I5): the first merchant of every book that has one holds <paramref name="pct"/> percent of the target cover of every good with a
    /// structural demand, as a merchant lot at floor 1.0 (reason Seed): the trade stock a settlement already had when the world was generated.
    /// </summary>
    public long SeedMerchantStock(int day, int tMeanC, int pct)
    {
        var demand = ComputeStructuralDemand(day, tMeanC); long total = 0;
        foreach (var b in AllBooks())
        {
            if (b.Merchants.Count == 0) continue;
            var d = demand[b.SettlementIndex]; var m = b.Merchants[0];
            for (ushort g = 0; g < d.Length; g++)
            {
                long units = d[g] * b.Goods[g].TargetCoverDays * pct / 100;
                if (units <= 0 || !m.Store.Create(g, units, CreateReason.Seed)) continue;
                b.List(m, g, units, 1000, day, merchantLot: true); total += units;
            }
        }
        return total;
    }

    /// <summary>The callback of a household's durable order: the new pair, set or tool replaces the worn one (consumed in service) and the due count falls.</summary>
    public Action<TradeResult> DurableHook(int hh, int k)
    {
        var party = HouseholdParty(hh); ushort g = _durable[k]; int idx = hh * EconomyState.Durables + k;
        return res =>
        {
            if (res.Qty <= 0) return;
            party.Store.Destroy(g, res.Qty, DestroyReason.Worn);
            Econ.DueDurables[idx] = (byte)Math.Max(0, Econ.DueDurables[idx] - (int)res.Qty);
        };
    }

    private MarketParty?[] _stewards = [];

    /// <summary>The steward of a fief (relief purchases): a party over the fief's treasury purse and chest, made once (taxes spec 3.6).</summary>
    public MarketParty Steward(int fief)
    {
        var reg = Pop.Region;
        if (_stewards.Length != reg.Fiefs.Count) _stewards = new MarketParty?[reg.Fiefs.Count];
        return _stewards[fief] ??= new MarketParty(reg.Fiefs[fief].Id, Econ.Ledger.Open(PurseKind.Treasury, reg.Fiefs[fief].Id), Econ.OpenStore(StoreKind.Chest, reg.Fiefs[fief].Id, 0, 0, enforce: false), PartyKind.Household, 1_000_000 + fief);
    }

    /// <summary>The steward party whose fief has this id, or null.</summary>
    public MarketParty? StewardById(EntityId id)
    {
        var fiefs = Pop.Region.Fiefs;
        for (int f = 0; f < fiefs.Count; f++) if (fiefs[f].Id == id) return Steward(f);
        return null;
    }

    // ---------- household shopping (households spec 3.4) ----------

    /// <summary>
    /// The shopping a household does in its daily slice: for each need line below half of its target cover it posts an order up to the target, staples first and
    /// split by the consumption mix over the goods the market holds. Orders are filled by the next session (decision MK2).
    /// </summary>
    public int PostHouseholdOrders(int hh, int day, int tMeanC)
    {
        var hht = Pop.Households;
        if (hht.MemberCount[hh] == 0) return 0;
        var book = BookForHousehold(hh); var party = HouseholdParty(hh); var c = Econ.Goods;
        var need = HouseholdNeeds.Basket(Econ, Pop.People, hht, hh, day, tMeanC);
        var basket = Econ.Basket(hht.Class[hh]);
        int ratio = Econ.Tiers[hh] == HouseholdTier.Survive ? 4000 : 2000;
        int posted = 0;
        long reserveLeft = Econ.SeedReserveG[hh];                                  // seed corn is not food: the shopper counts only what the household may eat
        var held = new Dictionary<ushort, long>();
        foreach (ushort sg in Econ.Food.Staples)
        {
            long q = party.Store.Qty(sg);
            if (Econ.Food.IsGrain(sg)) { long r = Math.Min(q, reserveLeft); reserveLeft -= r; q -= r; }
            held[sg] = q;
        }
        long Held(ushort g) => held.TryGetValue(g, out long q) ? q : party.Store.Qty(g);
        int Line(IReadOnlyList<(ushort Good, int Permille)> mix, long measuresX100, int coverDays, int priority)
        {
            if (measuresX100 <= 0) return 0;
            // flour and bread count as on sale only when the book holds a day of its demand (a handful of loaves is not a market: the order would lapse and the household go hungry, decision I18); grain always
            var avail = mix.Where(m => Held(m.Good) > 0 || (Econ.Food.IsGrain(m.Good) || mix != Mix.Staple ? book.Goods[m.Good].StockUnits > 0 : book.Goods[m.Good].StockUnits >= Math.Max(1, book.Goods[m.Good].Demand))).ToList();
            int tot = avail.Sum(m => m.Permille); int n = 0;
            if (tot == 0) return 0;
            foreach (var (g, pm) in avail)
            {
                long daily = HouseholdNeeds.GramsFor(c, g, measuresX100 * pm / tot);
                long target = daily * coverDays, have = Held(g);
                if (daily > 0 && have < target / 2) { book.Post(new BuyOrder(party, g, target - have, ratio) { Priority = priority }); n++; }
            }
            return n;
        }
        long heldStaple = 0; foreach (ushort sg in Econ.Food.Staples) heldStaple += HouseholdNeeds.MeasuresX100(c, sg, Held(sg));
        // a household that holds the grain for its next days eats it as pottage and home-baked bread: it does not buy flour or bread (integration decision I17)
        if (heldStaple < (long)need.StapleX100 * basket.TargetCoverDays.GetValueOrDefault("staple", 20) / 2)
        posted += Line(Mix.Staple, need.StapleX100, basket.TargetCoverDays.GetValueOrDefault("staple", 20), 0);
        posted += Line(Mix.Relish, need.RelishX100, basket.TargetCoverDays.GetValueOrDefault("relish", 3), 1);
        void Single(ushort g, long daily, string key, int priority)
        {
            long target = daily * basket.TargetCoverDays.GetValueOrDefault(key, 10), have = party.Store.Qty(g);
            if (daily > 0 && have < target / 2) { book.Post(new BuyOrder(party, g, target - have, ratio) { Priority = priority }); posted++; }
        }
        Single(Econ.Food.Firewood, need.FuelG, "firewood", 2);
        if (!(hht.ParcelCount[hh] > 0 && hht.Class[hh] != SocialClass.Burgher && party.Store.Qty(c.IdOf("barley")) > 0)) Single(Econ.Food.Ale, need.AleMl * c.Def(Econ.Food.Ale).LitreMassG / 1000, "ale", 3);
        Single(Econ.Food.Salt, need.SaltG, "salt", 4);
        for (int k = 0; k < EconomyState.Durables; k++)
        {
            int idx = hh * EconomyState.Durables + k; int due = Econ.DueDurables[idx];
            if (due == 0) continue;
            ushort g = _durable[k];
            if (book.Goods[g].StockUnits <= 0) continue;
            book.Post(new BuyOrder(party, g, due, 2000) { Priority = 6, OnFilled = DurableHook(hh, k), HookKind = 2, HookArg = idx });
            posted++;
        }
        return posted;
    }
}
