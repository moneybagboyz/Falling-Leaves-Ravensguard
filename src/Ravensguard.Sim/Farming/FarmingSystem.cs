using Ravensguard.Content;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Weather;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Goods;
using Ravensguard.Sim.People;
using Ravensguard.Sim.WorldGen.Region;
using RegionData = Ravensguard.Sim.WorldGen.Region.Region;

namespace Ravensguard.Sim.Farming;

/// <summary>Counters for tests and tooling.</summary>
public sealed class FarmingDiagnostics { public long SowRefusedSeed, SowRefusedFertility; public long[] RefusedByClass = new long[3]; public long[] SownByClass = new long[3]; }

/// <summary>What the labour planner did on the last day: worker-hours wanted, available and used (farming spec 3.2).</summary>
public sealed record LabourReport(long DemandHours, long SupplyHours, long OwnUsedHours, long HiredUsedHours, long UnmetHours, long WagesMites);

/// <summary>
/// Farming (farming spec): per-field state, three-field rotation, the seasonal calendar, labour, weather effects, the harvest split and the herds that
/// couple to it. A plain class with step methods, in the tick order of the spec: <see cref="StepSeasonal"/> at hour 0 of doy 1, 16, 31 and 46 (S1-S4), then
/// <see cref="StepDaily"/> at hour 0 of every day (H5 weather and field events, then the day's labour credit). <see cref="StepDay"/> does both in order.
/// Spoilage of the stores farming owns is the caller's daily spoilage pass (<see cref="EconomyState.ApplyDailySpoilage"/>); farming never spoils anything itself.
/// </summary>
public sealed partial class FarmingSystem
{
    public RegionData Region { get; }
    public Population Pop { get; }
    public EconomyState Econ { get; }
    public FarmingSettings Settings { get; }
    public IFarmWeather Weather { get; }
    public Chronicle Chronicle { get; }
    public IdAllocator Ids { get; }
    public ulong Seed { get; }
    public FarmingState State { get; }
    public LabourReport LastLabour { get; private set; } = new(0, 0, 0, 0, 0, 0);
    /// <summary>Counters of why fields went unsown this run (not saved): refused for want of seed, and refused because the soil was below every crop's minimum.</summary>
    public FarmingDiagnostics Diagnostics { get; } = new();

    private FieldTable F => State.Fields;
    private ParcelWork P => State.Parcels;
    private readonly FarmingSettingsDef _d;
    private readonly int _wh;
    private int[] _cellOfField = [];
    private int[] _cellStart = [], _cellFields = [];
    private int[] _usedCells = [];
    private byte[] _cultureOfParcel = [];
    private int[] _settlementOfParcel = [], _parcelCell = [];
    private int[] _pendingThresh = [];
    private bool[] _isHolder;
    private int[] _hhHours;
    private int[][] _householdsOf;
    private int[] _popHolder;
    private readonly long[] _rainToday = new long[CellCropClimate.Cells];
    private readonly int[] _tempToday = new int[CellCropClimate.Cells];
    private readonly bool[] _stormToday = new bool[CellCropClimate.Cells];
    private readonly LongCondition[] _long = new LongCondition[256];
    private readonly GoodCatalog _cat;
    private readonly ushort _hay, _straw;
    private int _lastDoy = -1;

    public FarmingSystem(RegionData region, Population pop, EconomyState econ, FarmingSettings settings, IFarmWeather weather, Chronicle chronicle, IdAllocator ids, ulong seed, FarmingState? state = null)
    {
        Region = region; Pop = pop; Econ = econ; Settings = settings; Weather = weather; Chronicle = chronicle; Ids = ids; Seed = seed;
        _d = settings.Def; _wh = _d.WorkdayHours; _cat = econ.Goods;
        settings.BindGoods(_cat);
        _hay = _cat.IdOf("hay"); _straw = _cat.IdOf("straw");
        State = state ?? CreateState(region, pop, settings, seed, pop.NowYear);
        if (State.Fields.Count != region.Fields.Length || State.Parcels.Count != region.Parcels.Count) throw new ArgumentException("farming state does not match the region");

        BuildFieldIndexes();

        int np = P.Count, ns = region.Settlements.Count;
        int nh = pop.Households.Count;
        _isHolder = new bool[nh];
        for (int p = 0; p < np; p++) if (P.Holder[p] >= 0) _isHolder[P.Holder[p]] = true;
        _hhHours = new int[nh];
        var lists = new List<int>[ns]; for (int s = 0; s < ns; s++) lists[s] = [];
        for (int h = 0; h < nh; h++) { int s = pop.Households.Settlement[h]; if ((uint)s < (uint)ns) lists[s].Add(h); }
        _householdsOf = lists.Select(l => l.ToArray()).ToArray();
        _popHolder = (int[])P.Holder.Clone();

        // stores farming owns (idempotent: a restored economy already has them)
        for (int h = 0; h < nh; h++) if (_isHolder[h]) econ.OpenStore(StoreKind.Barn, FarmIds.Barn(h));
        for (int s = 0; s < ns; s++) { econ.OpenStore(StoreKind.Granary, FarmIds.ParishGranary(s)); econ.OpenStore(StoreKind.Barn, FarmIds.Fodder(s)); }
        Array.Fill(_tempToday, int.MinValue);
        RecomputeHours(pop.NowDay);
        RefreshLong(State.Year);
    }

    /// <summary>
    /// The indexes derived from the fields and parcels of the region: each field's weather cell, the fields of each cell, the cells in use, each parcel's culture,
    /// settlement and cell and the sheaves waiting to be threshed. Built by the constructor, and again when background growth appends parcels.
    /// </summary>
    private void BuildFieldIndexes()
    {
        var region = Region;
        int nf = F.Count, np = P.Count, ns = region.Settlements.Count;
        _cellOfField = new int[nf];
        var perCell = new int[CellCropClimate.Cells + 1];
        for (int i = 0; i < nf; i++)
        {
            int t = F.Tile[i]; int lx = Math.Min(15, t % RegionData.Size * RegionData.TileM / 2000), ly = Math.Min(15, t / RegionData.Size * RegionData.TileM / 2000);
            _cellOfField[i] = ly * 16 + lx; perCell[_cellOfField[i] + 1]++;
        }
        for (int c = 0; c < CellCropClimate.Cells; c++) perCell[c + 1] += perCell[c];
        _cellStart = perCell; _cellFields = new int[nf];
        var cursor = (int[])perCell.Clone();
        for (int i = 0; i < nf; i++) _cellFields[cursor[_cellOfField[i]]++] = i;
        _usedCells = Enumerable.Range(0, CellCropClimate.Cells).Where(c => _cellStart[c + 1] > _cellStart[c]).ToArray();

        _cultureOfParcel = new byte[np]; _settlementOfParcel = new int[np]; _parcelCell = new int[np]; _pendingThresh = new int[np];
        for (int s = 0; s < ns; s++) foreach (int p in region.Settlements[s].Parcels) { _settlementOfParcel[p] = s; _cultureOfParcel[p] = (byte)region.Settlements[s].Culture; }
        for (int p = 0; p < np; p++)
        {
            var parcel = region.Parcels[p];
            _parcelCell[p] = parcel.FieldCount > 0 ? _cellOfField[parcel.FieldStart] : 0;
            for (int k = 0; k < parcel.FieldCount; k++) if (F.Has(parcel.FieldStart + k, FieldFlags.Bound)) _pendingThresh[p]++;
        }
    }

    /// <summary>
    /// Background growth (assarting): the region has just had <paramref name="parcels"/> appended (each with its fields, already in <c>Region.Fields</c> with their fertility
    /// scaled as the generated fields' were). Extends the field and parcel tables, starts the new fields fallow (cleared ground rests its first year), gives each parcel to
    /// <paramref name="holder"/> (the lord, who lets it to a landless household at the next year's re-let), and rebuilds the derived indexes.
    /// </summary>
    public void AddParcels(IReadOnlyList<int> parcels, int holder)
    {
        int nf = Region.Fields.Length, np = Region.Parcels.Count;
        State.GrowTo(nf, np);
        foreach (int p in parcels)
        {
            var parcel = Region.Parcels[p];
            for (int k = 0; k < parcel.FieldCount; k++)
            {
                int i = parcel.FieldStart + k; var src = Region.Fields[i];
                F.Id[i] = src.Id; F.Tile[i] = src.Tile; F.Parcel[i] = src.Parcel; F.Fertility[i] = src.Fertility; F.Slot[i] = src.Slot; F.Region[i] = 0; F.SownFertility[i] = src.Fertility;
                F.Crop[i] = Crop.Fallow; F.Stage[i] = FieldStage.Stubble;
            }
            P.Holder[p] = holder; P.Landlord[p] = -1; P.OwnerKind[p] = parcel.Owner.Kind; P.Lease[p] = LeaseKind.None; P.Slot[p] = parcel.Slot;
            if (holder >= 0) Pop.Households.ParcelCount[holder]++;
        }
        var ph = Pop.ParcelHolder;
        if (ph.Length < np) { Array.Resize(ref ph, np); Pop.ParcelHolder = ph; }
        foreach (int p in parcels) { ph[p] = holder; State.Livestock.MeadowAcres[Region.Parcels[p].Settlement] += Region.Parcels[p].FieldCount * _d.MeadowPctOfFieldAcres / 100; }
        _popHolder = (int[])P.Holder.Clone();
        BuildFieldIndexes();
        _holderStale = true;
    }

    /// <summary>The fresh field, parcel and herd state at world start (day 1 of <paramref name="year"/>): winter grain standing on slot 0, spring fields stubble, fallow resting.</summary>
    public static FarmingState CreateState(RegionData region, Population pop, FarmingSettings settings, ulong seed, int year)
    {
        var d = settings.Def;
        var st = new FarmingState(region.Fields.Length, region.Parcels.Count, region.Settlements.Count, pop.Households.Count) { Year = year };
        var f = st.Fields;
        int scale = FertilityScalePermille(region, TargetFertility(region, pop, d));
        long yearHour = (long)year * TimeConst.DaysPerYear * 24;
        for (int i = 0; i < f.Count; i++)
        {
            var src = region.Fields[i];
            f.Id[i] = src.Id; f.Tile[i] = src.Tile; f.Parcel[i] = src.Parcel; f.Fertility[i] = ScaleFertility(src.Fertility, scale); f.Slot[i] = src.Slot; f.Region[i] = 0;
            f.SownFertility[i] = f.Fertility[i];
            if (src.Slot == 0)
            {
                var crop = PickWinter(settings, seed, yearHour, src.Id, f.Fertility[i], 0);
                f.Crop[i] = crop; f.Stage[i] = crop == Crop.Fallow ? FieldStage.Stubble : FieldStage.Growing;
                if (crop != Crop.Fallow) { f.SeedG[i] = (int)settings.Info(crop).SeedG(d.GramsPerMeasure); f.SownDay[i] = 40; }
            }
            else { f.Crop[i] = Crop.Fallow; f.Stage[i] = FieldStage.Stubble; }
        }
        var w = st.Parcels;
        for (int p = 0; p < w.Count; p++)
        {
            var parcel = region.Parcels[p];
            w.Holder[p] = p < pop.ParcelHolder.Length ? pop.ParcelHolder[p] : -1; w.Landlord[p] = -1; w.OwnerKind[p] = parcel.Owner.Kind; w.Slot[p] = parcel.Slot;
            if (parcel.Owner.Kind == OwnerKind.Tenant && parcel.Owner.FiefIndex >= 0 && parcel.Owner.FiefIndex < pop.FiefLordHousehold.Length && pop.FiefLordHousehold[parcel.Owner.FiefIndex] >= 0)
            {
                w.Landlord[p] = pop.FiefLordHousehold[parcel.Owner.FiefIndex];
                bool coin = DetRng.Range(seed, 0, parcel.Id.Value, RngStreams.FarmEvents, 7, 0, 100) < d.CoinLeasePct;
                w.Lease[p] = coin ? LeaseKind.Coin : LeaseKind.Share; w.ShareBp[p] = (short)d.RentShareBpDefault; w.CoinMitesPerFieldYear[p] = d.RentCoinMitesPerFieldYear;
            }
        }
        // herds: oxen 3 per 100 field acres, sheep a little over the grazing threshold, meadows a share of the field acres
        var lv = st.Livestock;
        for (int s = 0; s < region.Settlements.Count; s++)
        {
            int acres = 0, fallow = 0;
            foreach (int p in region.Settlements[s].Parcels) { acres += region.Parcels[p].FieldCount; if (region.Parcels[p].Slot == 2) fallow += region.Parcels[p].FieldCount; }
            lv.Oxen[s] = (acres * d.OxenPer100ArableAcres + 50) / 100;
            lv.Sheep[s] = (fallow * d.SheepStartPerFallowAcreX100 + 50) / 100;
            lv.Cows[s] = (acres * d.CowsPer100FieldAcres + 50) / 100;
            lv.MeadowAcres[s] = acres * d.MeadowPctOfFieldAcres / 100;
        }
        return st;
    }

    /// <summary>The Phase 1 to farming fertility scale in permille: the smallest (found by bisection, integer) that gives mean capped yield fertility at least <paramref name="targetMean"/>; 1000 when the target is 0.</summary>
    public static int FertilityScalePermille(RegionData region, int targetMean)
    {
        if (targetMean <= 0 || region.Fields.Length == 0) return 1000;
        long n = region.Fields.Length;
        long MeanX1000(int s) { long t = 0; foreach (var f in region.Fields) t += Math.Min(YieldMath.FertilityCap, Math.Min(100, f.Fertility * s / 1000)); return t * 1000 / n; }
        int lo = 1000, hi = 8000;
        if (MeanX1000(lo) >= targetMean * 1000L) return 1000;
        while (hi - lo > 1) { int mid = (lo + hi) / 2; if (MeanX1000(mid) >= targetMean * 1000L) hi = mid; else lo = mid; }
        return hi;
    }

    /// <summary>The mean yield fertility the region is calibrated to: the plan's 70, raised in proportion when the region has fewer fields per person than the plan's 62,400 for 25,000 (clamped 60..88).</summary>
    public static int TargetFertility(RegionData region, Population pop, FarmingSettingsDef d)
    {
        if (d.TargetMeanFertility <= 0) return 0;
        if (d.ReferenceFieldsPerKiloPerson <= 0 || pop.People.Count == 0) return d.TargetMeanFertility;
        long perKilo = region.Fields.Length * 1000L / pop.People.Count;                 // fields per 1,000 people
        return (int)Math.Clamp(d.TargetMeanFertility * (long)d.ReferenceFieldsPerKiloPerson / Math.Max(1, perKilo), 60, 88);
    }

    public static byte ScaleFertility(int fertility, int scalePermille) => (byte)Math.Clamp(fertility * scalePermille / 1000, 0, 100);

    // ---------- crop choice (farming spec 2.1: stable per field and year) ----------

    private static int Pct(FarmingSettings s, ulong seed, long yearHour, EntityId field, int year)
        => DetRng.Range(seed, yearHour, field.Value, RngStreams.YieldVariance, (uint)year, 0, 100);

    /// <summary>The winter crop for a field: wheat for 60% by hash when fertility is 55 or more, otherwise rye; fallow when even rye fails its minimum fertility.</summary>
    public static Crop PickWinter(FarmingSettings s, ulong seed, long yearHour, EntityId field, int fertility, int year)
    {
        bool wheat = fertility >= s.Def.WheatMinFertilityForWinterPick && fertility >= s.Info(Crop.Wheat).MinFertility && Pct(s, seed, yearHour, field, year) < s.Def.WinterWheatPct;
        if (wheat) return Crop.Wheat;
        return fertility >= s.Info(Crop.Rye).MinFertility ? Crop.Rye : Crop.Fallow;
    }

    /// <summary>Spring crop preference by the 40/27/20/13 mix; legumes only below the forced-legume fertility.</summary>
    public static Crop PickSpring(FarmingSettings s, ulong seed, long yearHour, EntityId field, int fertility, int year)
    {
        int r = Pct(s, seed, yearHour, field, year);
        var mix = s.Def.SpringMixPct;
        Crop pick = Crop.Beans; int acc = 0;
        for (int i = 0; i < 4; i++) { acc += mix[i]; if (r < acc) { pick = FarmingSettings.SpringCrops[i]; break; } }
        if (fertility < s.Def.LegumeForcedBelowFertility && pick is Crop.Barley or Crop.Oats) pick = r % 2 == 0 ? Crop.Peas : Crop.Beans;
        return pick;
    }

    /// <summary>
    /// Keeps the weather's snow-depth chain of the farmed cells warm, a few cells an hour (integration decision I40): the chain restarts at midsummer, and the first
    /// winter query of each cell then walked 60 to 120 days of weather, all at hour 0 of one day (a 160 ms hour). A cache only: the values are pure functions of
    /// the cell and day, so the results never change.
    /// </summary>
    public void PrewarmWeather(GameTime now)
    {
        int h = now.Hour; if (h == 0) return;
        long day = now.TotalDays;
        for (int i = h - 1; i < _usedCells.Length; i += 23) Weather.SnowDepthCm(CellContinent(_usedCells[i]), day);
    }

    private int CellContinent(int local) => (Region.CellY + local / 16) * WorldGen.Continent.Size + Region.CellX + local % 16;
    private long YearHour(int year) => (long)year * TimeConst.DaysPerYear * 24;

    private void RefreshLong(int year)
    {
        for (int c = 0; c < 256; c++) _long[c] = default;
        foreach (var s in Region.Settlements) { int c = s.Culture & 255; if (_long[c].Kind == Ravensguard.Sim.Weather.LongKind.None) _long[c] = Weather.Long(c, year); }
    }

    /// <summary>
    /// Persons the farm labour planner must not count because another employer (a business) already has them; set by the integration layer. Null: nobody is employed.
    /// </summary>
    public Func<int, bool>? IsEmployedElsewhere { get; set; }

    /// <summary>The household of the lord a person serves as a manor retainer, or -1; set by the integration layer.</summary>
    public Func<int, int>? ManorLordOf { get; set; }

    /// <summary>
    /// Brings the farming tables in step with a population that grows and changes hands (births, weddings, inheritance, escheat, emigration): the household
    /// arrays grow, parcels whose holder changed in the population table pass to the new holder (with the barn stock), barns open for new holders and the
    /// per-settlement household lists are rebuilt. A no-op for a static population.
    /// </summary>
    public void SyncPopulation()
    {
        int nh = Pop.Households.Count, np = P.Count;
        if (_hhHours.Length < nh)
        {
            Array.Resize(ref _hhHours, nh); Array.Resize(ref _isHolder, nh);
            if (State.SeedDebtG.Length < nh) { var sd = State.SeedDebtG; Array.Resize(ref sd, nh); State.SeedDebtG = sd; }
        }
        bool changed = false;
        var src = Pop.ParcelHolder;
        for (int p = 0; p < np && p < src.Length; p++)
        {
            if (src[p] == _popHolder[p]) continue;
            int old = P.Holder[p], now = src[p]; _popHolder[p] = now; changed = true;
            P.Holder[p] = now;
            if (now >= 0 && old >= 0 && old != now && old < nh && Econ.OtherStores.TryGetValue(FarmIds.Barn(old), out var oldBarn))
            {
                var nb = Econ.OpenStore(StoreKind.Barn, FarmIds.Barn(now));
                foreach (var (g, q) in oldBarn.Rows()) Store.Move(oldBarn, nb, g, q);
            }
        }
        if (changed || _holderStale)
        {
            Array.Clear(_isHolder);
            for (int p = 0; p < np; p++) if (P.Holder[p] >= 0 && P.Holder[p] < nh) _isHolder[P.Holder[p]] = true;
            for (int h = 0; h < nh; h++) if (_isHolder[h]) Econ.OpenStore(StoreKind.Barn, FarmIds.Barn(h));
            _holderStale = false;
        }
        int ns = Region.Settlements.Count;
        var cnt = new int[ns];
        for (int h = 0; h < nh; h++) { int s = Pop.Households.Settlement[h]; if ((uint)s < (uint)ns && Pop.Households.MemberCount[h] > 0) cnt[s]++; }
        for (int s = 0; s < ns; s++)
        {
            if (_householdsOf[s].Length != cnt[s]) _householdsOf[s] = new int[cnt[s]];
            int k = 0; var arr = _householdsOf[s];
            if (cnt[s] == 0) continue;
            for (int h = 0; h < nh; h++) if (Pop.Households.Settlement[h] == s && Pop.Households.MemberCount[h] > 0) arr[k++] = h;
        }
    }
    private bool _holderStale = true;

    /// <summary>
    /// Worker-hours per day of each household: members from 12 up (5 tenths of a day to 15 and over 60, a full day between) who farm or whose household holds land,
    /// plus the idle of any peasant or burgher household (no occupation, no employer: the seasonal hands the harvest hires). Business staff are not counted.
    /// </summary>
    private void RecomputeHours(int day)
    {
        var people = Pop.People; var hh = Pop.Households; int n = Math.Min(hh.Count, _hhHours.Length);
        var away = IsEmployedElsewhere; var lordOf = ManorLordOf;
        var demesneHands = new int[n];
        for (int h = 0; h < n; h++)
        {
            int hours = 0;
            if (hh.MemberCount[h] > 0 && (hh.Class[h] != SocialClass.Gentry || _isHolder[h]))
                foreach (int m in hh.Members(h))
                {
                    int age = people.AgeYears(m, day);
                    if (age < 12) continue;
                    if (people.Status[m] != PersonStatus.Alive) continue;
                    var occ = people.Occupation[m];
                    bool hand = occ is Occupation.Farmer or Occupation.Ploughman or Occupation.Labourer or Occupation.Shepherd
                                || (occ == Occupation.None && (hh.Class[h] == SocialClass.Peasant || (_idleHands && hh.Class[h] != SocialClass.Gentry)));
                    if (!hand) continue;
                    if (away != null && away(m)) continue;
                    int hrs = age < 16 || age >= 60 ? _wh / 2 : _wh;
                    // a retainer of a manor works the lord's demesne as part of his service (integration decision I31): his hours are the lord's own, not hired at a wage the lord may not have the coin for
                    int lord = lordOf?.Invoke(m) ?? -1;
                    if (lord >= 0 && lord < n && _isHolder[lord]) { demesneHands[lord] += hrs; continue; }
                    hours += hrs;
                }
            _hhHours[h] = hours;
        }
        for (int h = 0; h < n; h++) _hhHours[h] += demesneHands[h];
        for (int h = n; h < _hhHours.Length; h++) _hhHours[h] = 0;
    }

    /// <summary>When true (the integrated economy), unoccupied members of landless households are seasonal hands too; the standalone farming tests keep the original rule.</summary>
    public bool CountIdleHands { get => _idleHands; set => _idleHands = value; }
    private bool _idleHands;

    /// <summary>Runs the seasonal steps due at hour 0 of this day (doy 1, 16, 31, 46) and then the daily step.</summary>
    public void StepDay(GameTime now)
    {
        int doy = now.DayOfYear;
        if (doy is 1 or 16 or 31 or 46) StepSeasonal(now);
        StepDaily(now);
    }

    // ---------- helpers ----------

    private EntityId NewEvent() => Ids.Allocate(IdKind.Event);
    private Store HouseStore(int hh) => Econ.HouseholdStore[hh];
    private Store Barn(int hh) => Econ.OtherStores[FarmIds.Barn(hh)];
    private Store Parish(int settlement) => Econ.OtherStores[FarmIds.ParishGranary(settlement)];
    private Store Fodder(int settlement) => Econ.OtherStores[FarmIds.Fodder(settlement)];

    /// <summary>Number of fields in a settlement whose parcels are in the given slot.</summary>
    public int SlotAcres(int settlement, int slot)
    {
        int n = 0;
        foreach (int p in Region.Settlements[settlement].Parcels) if (P.Slot[p] == slot) n += Region.Parcels[p].FieldCount;
        return n;
    }

    /// <summary>Count of grams of one good over every store farming or households hold (for audits in tests).</summary>
    public long TotalGrams(ushort good)
    {
        long s = 0;
        for (int h = 0; h < Econ.HouseholdCount; h++) s += Econ.HouseholdStore[h].Qty(good);
        foreach (var st in Econ.OtherStores.Values) s += st.Qty(good);
        return s;
    }
}
