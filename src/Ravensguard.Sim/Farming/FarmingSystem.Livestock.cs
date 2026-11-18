using Ravensguard.Sim.Core;
using Ravensguard.Sim.Goods;
using Ravensguard.Sim.WorldGen.Region;

namespace Ravensguard.Sim.Farming;

public sealed partial class FarmingSystem
{
    /// <summary>S2 of summer: a settlement grazes its fallow when sheep reach 0.4 per fallow acre; grazed fallow later gains +15 fertility instead of +5.</summary>
    private void SetGrazing()
    {
        var lv = State.Livestock;
        for (int s = 0; s < Region.Settlements.Count; s++)
        {
            int fallow = SlotAcres(s, 2);
            lv.Grazed[s] = (long)lv.Sheep[s] * 100 >= (long)_d.GrazingSheepPerFallowAcreX100 * fallow;
            foreach (int p in Region.Settlements[s].Parcels)
            {
                if (P.Slot[p] != 2) continue;
                var parcel = Region.Parcels[p];
                for (int k = 0; k < parcel.FieldCount; k++) { int f = parcel.FieldStart + k; if (lv.Grazed[s]) F.Set(f, FieldFlags.Grazed); else F.Clear(f, FieldFlags.Grazed); }
            }
        }
    }

    /// <summary>Kilograms times ten of hay the herds eat in one winter day.</summary>
    private long DailyNeedKgX10(int s) => (long)State.Livestock.Sheep[s] * _d.SheepHayKgPerHeadDayX10 + (long)State.Livestock.Oxen[s] * _d.OxHayKgPerHeadDayX10
        + (long)State.Livestock.Cows[s] * _d.CowHayKgPerHeadDayX10;

    /// <summary>True when the herds make products (wool, cheese, meat): any of the product keys is set. Off in the farming unit tests, on in the game content.</summary>
    private bool HerdProducts => _d.WoolGPerSheepYear > 0 || _d.CheeseGPerCowYear > 0 || _d.CowCalvingPermilleYear > 0;

    /// <summary>
    /// Shares out a product of the settlement's herds over the households that hold its parcels, in proportion to the field acres each holds (the herds graze and are
    /// kept on the village's land, so a holder owns its acres' share; largest remainder, ties to the lower household). Reason Harvest, so the mass audit counts it
    /// with the other faucets of the farm.
    /// </summary>
    private void ToHolders(int s, ushort good, long qty)
    {
        if (qty <= 0) return;
        var weight = new SortedDictionary<int, int>(); long total = 0;
        foreach (int p in Region.Settlements[s].Parcels)
        {
            int hh = P.Holder[p]; if (hh < 0) continue;
            int w = Region.Parcels[p].FieldCount; weight[hh] = weight.GetValueOrDefault(hh) + w; total += w;
        }
        if (total == 0) return;
        long given = 0; int top = -1, topW = -1;
        foreach (var (hh, w) in weight)
        {
            long share = qty * w / total; given += share;
            if (share > 0) HouseStore(hh).Create(good, share, CreateReason.Harvest);
            if (w > topW) { topW = w; top = hh; }
        }
        if (qty > given) HouseStore(top).Create(good, qty - given, CreateReason.Harvest);
    }

    /// <summary>
    /// The tithe of animal products (taxes spec 3.3, G-T4): a tenth of the wool and the cheese goes to the parish's granary (reason Harvest, as the grain tithe is); returns what is left for the
    /// holders. The parish sells it with the rest of its tithe goods. Nothing is taken from a settlement whose parish has no granary.
    /// </summary>
    private long TitheOf(int s, ushort good, long qty)
    {
        long tithe = qty * _d.TithePermille / 1000;
        if (tithe <= 0 || !Econ.OtherStores.TryGetValue(FarmIds.ParishGranary(s), out var granary)) return qty;
        granary.Create(good, tithe, CreateReason.Harvest);
        return qty - tithe;
    }

    /// <summary>Spring shearing (resources spec 3.4, decision R2): raw wool from every sheep, to the holders by acres.</summary>
    private void Shear()
    {
        if (_d.WoolGPerSheepYear <= 0) return;
        var wool = _cat.IdOf("wool_raw");
        for (int s = 0; s < Region.Settlements.Count; s++) ToHolders(s, wool, TitheOf(s, wool, (long)State.Livestock.Sheep[s] * _d.WoolGPerSheepYear));
    }

    /// <summary>Start of autumn: the dairy of the year, a game year's cheese per cow, to the holders by acres.</summary>
    private void MakeCheese()
    {
        if (_d.CheeseGPerCowYear <= 0) return;
        var cheese = _cat.IdOf("cheese");
        for (int s = 0; s < Region.Settlements.Count; s++) ToHolders(s, cheese, TitheOf(s, cheese, (long)State.Livestock.Cows[s] * _d.CheeseGPerCowYear));
    }

    /// <summary>Animals slaughtered (culled, or surplus young) leave the herd as live pieces for the holders, who sell them to a butcher (the slaughter recipes' input).</summary>
    private void Slaughtered(int s, string animal, int head) { if (head > 0 && HerdProducts) ToHolders(s, _cat.IdOf(animal), head); }

    /// <summary>Stochastic whole animals from a yearly rate: <c>head x permille / 1000</c> with the remainder decided by stream 0x212 (draw by species).</summary>
    private int Stoch(int head, int permille, int s, long hourKey, uint draw)
    {
        long x = (long)head * permille; int whole = (int)(x / 1000), rem = (int)(x % 1000);
        if (rem > 0 && DetRng.Range(Seed, hourKey, (ulong)s, RngStreams.Herds, draw, 0, 1000) < rem) whole++;
        return whole;
    }

    /// <summary>
    /// The herd year at S1 for settlement <paramref name="s"/> (after the sheep growth of <see cref="RollYear"/>): mortality, calving (bull calves refill the oxen up to
    /// the plough target, the rest and the surplus above the pasture cap go to the butcher), and the oxen's own replacement.
    /// </summary>
    private void HerdYear(int s, long hourKey)
    {
        var lv = State.Livestock;
        if (_d.SheepMortalityPermilleYear > 0) lv.Sheep[s] = Math.Max(0, lv.Sheep[s] - Stoch(lv.Sheep[s], _d.SheepMortalityPermilleYear, s, hourKey, 1));
        if (_d.OxenMortalityPermilleYear > 0) lv.Oxen[s] = Math.Max(0, lv.Oxen[s] - Stoch(lv.Oxen[s], _d.OxenMortalityPermilleYear, s, hourKey, 2));
        if (_d.CowMortalityPermilleYear > 0) lv.Cows[s] = Math.Max(0, lv.Cows[s] - Stoch(lv.Cows[s], _d.CowMortalityPermilleYear, s, hourKey, 3));
        if (_d.CowCalvingPermilleYear <= 0) return;
        int acres = 0; foreach (int p in Region.Settlements[s].Parcels) acres += Region.Parcels[p].FieldCount;
        int oxTarget = (acres * _d.OxenPer100ArableAcres + 50) / 100, cowCap = acres * _d.CowPerFieldAcreCapX100 / 100;       // decision G11: the cap is cows per field acre x100 (11 is 0.11), not per 10,000: it was 0 for a village, no heifer was ever kept and the herd fell 1 percent a year
        int calves = Stoch(lv.Cows[s], _d.CowCalvingPermilleYear, s, hourKey, 4);
        int bulls = calves / 2, heifers = calves - bulls;
        int toOxen = Math.Max(0, Math.Min(bulls, oxTarget - lv.Oxen[s]));
        lv.Oxen[s] += toOxen;
        int room = Math.Max(0, cowCap - lv.Cows[s]), toCows = Math.Min(heifers, room);
        lv.Cows[s] += toCows;
        Slaughtered(s, "cow", (bulls - toOxen) + (heifers - toCows));
    }

    /// <summary>S3 at autumn end (resources spec 3.3): sheep are culled when hay (plus half the straw) cannot carry the winter; oxen are kept.</summary>
    private void CullHerds(GameTime now)
    {
        var lv = State.Livestock;
        for (int s = 0; s < Region.Settlements.Count; s++)
        {
            long needed = DailyNeedKgX10(s) * 100 * _d.WinterFodderDays;                    // grams: kg x10 -> g is x100
            var fod = Fodder(s);
            long have = fod.Qty(_hay) + fod.Qty(_straw) / 2;
            int permille = YieldMath.CullPermille(have, needed, _d.CullMaxPermille);
            if (permille <= 0) continue;
            int cull = lv.Sheep[s] * permille / 1000, cullCows = lv.Cows[s] * permille / 1000;
            if (cull <= 0 && cullCows <= 0) continue;
            lv.Sheep[s] -= cull; lv.Cows[s] -= cullCows;
            Slaughtered(s, "sheep", cull); Slaughtered(s, "cow", cullCows);
            Chronicle.Add(new ChronicleEntry(NewEvent(), now, "farm.cull", [Region.Settlements[s].Id], [new Cause(CauseKind.Weather, null, "short of fodder: sheep slaughtered before winter")],
                new Dictionary<string, string> { ["settlement"] = Region.Settlements[s].Name, ["culled"] = cull.ToString(), ["permille"] = permille.ToString() }));
        }
    }

    /// <summary>Winter feeding (autumn end to year end): hay first, then straw at two grams per gram of hay.</summary>
    private void FeedHerds()
    {
        for (int s = 0; s < Region.Settlements.Count; s++)
        {
            long need = DailyNeedKgX10(s) * 100; if (need <= 0) continue;
            var fod = Fodder(s);
            long ate = fod.DestroyUpTo(_hay, need, DestroyReason.Consumed);
            long rest = need - ate;
            if (rest > 0) fod.DestroyUpTo(_straw, rest * 2, DestroyReason.Consumed);
        }
    }
}
