using Ravensguard.Content;
using Ravensguard.Sim.Building;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.WorldGen.History;
using Ravensguard.Sim.WorldGen.Region;
using RegionData = Ravensguard.Sim.WorldGen.Region.Region;

namespace Ravensguard.Sim.People;

/// <summary>
/// Stage 3 part B (plan §1A, §2.1a, §4A.2): creates every person of the region at world generation, grouped into households that
/// live in the settlement lots, work the parcels and run the workplaces. Pure function of (region, history, content): integer maths
/// and stable-id iteration order only.
/// </summary>
public sealed partial class Populator
{
    /// <summary>Creates the whole population. <paramref name="content"/> must hold the base building content and the name pools.</summary>
    public static Population Populate(RegionData region, WorldHistory history, ContentDatabase content)
        => new Populator(region, history, content).Run();

    private const uint StLord = 1, StSettlement = 2;
    private const int KidPermille = 360, ElderPermille = 160;

    private readonly RegionData _r;
    private readonly WorldHistory _h;
    private readonly BuildingLibrary _lib;
    private readonly NamePools _names;
    private readonly ulong _seed;
    private readonly int _nowDay;
    private readonly int _nowYear;

    private PersonTable _p = null!;
    private HouseholdTable _hh = null!;
    private int[][] _lotHousehold = [], _lotBeds = [], _lotKeeper = [];
    private bool[][] _lotFarm = [], _lotOwnedParcel = [];
    private int[] _fiefLord = [], _fiefLordHh = [], _priestHh = [], _lordPeople = [], _lordLot = [], _settlementPeople = [];
    private readonly HashSet<string> _used = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _bedCache = [];
    private int _tot, _kid, _eld;

    private Populator(RegionData region, WorldHistory history, ContentDatabase content)
    {
        _r = region; _h = history;
        _lib = BuildingLibrary.FromDatabase(content);
        _seed = region.Seed ^ 0x50455031UL;
        _names = new NamePools(content.All<NamePoolDef>(), _seed);
        _nowYear = history.Years;
        _nowDay = _nowYear * TimeConst.DaysPerYear;
    }

    private Population Run()
    {
        int ns = _r.Settlements.Count;
        ulong pBase = 1, hBase = 1;
        foreach (var n in _h.Notables) pBase = Math.Max(pBase, n.Id.Counter + 1);
        foreach (var d in _h.Dynasties) hBase = Math.Max(hBase, d.Id.Counter + 1);
        int cap = Math.Max(1024, _r.TotalPopulation + _r.TotalPopulation / 20);
        _p = new PersonTable(cap, pBase);
        _hh = new HouseholdTable(Math.Max(256, _r.TotalHouseholds + _r.TotalHouseholds / 10), hBase);

        _lotHousehold = new int[ns][]; _lotBeds = new int[ns][]; _lotKeeper = new int[ns][]; _lotFarm = new bool[ns][]; _lotOwnedParcel = new bool[ns][];
        for (int i = 0; i < ns; i++)
        {
            int nl = _r.Settlements[i].Plan.Lots.Count;
            _lotHousehold[i] = new int[nl]; Array.Fill(_lotHousehold[i], -1);
            _lotKeeper[i] = new int[nl]; Array.Fill(_lotKeeper[i], -1);
            _lotBeds[i] = new int[nl]; _lotFarm[i] = new bool[nl]; _lotOwnedParcel[i] = new bool[nl];
            for (int l = 0; l < nl; l++)
            {
                var lot = _r.Settlements[i].Plan.Lots[l];
                if (lot.IsDwelling) _lotBeds[i][l] = BedsOf(lot);
            }
        }
        foreach (var pc in _r.Parcels)
        {
            var o = pc.Owner;
            if (o.Kind is OwnerKind.Tenant or OwnerKind.Freeholder && o.LotIndex >= 0 && o.LotIndex < _lotFarm[pc.Settlement].Length) _lotFarm[pc.Settlement][o.LotIndex] = true;
            if (o.Kind is OwnerKind.Burgher && o.LotIndex >= 0 && o.LotIndex < _lotOwnedParcel[pc.Settlement].Length) _lotOwnedParcel[pc.Settlement][o.LotIndex] = true;
        }
        int nf = _r.Fiefs.Count;
        _fiefLord = new int[nf]; _fiefLordHh = new int[nf]; Array.Fill(_fiefLord, -1); Array.Fill(_fiefLordHh, -1);
        _priestHh = new int[ns]; Array.Fill(_priestHh, -1);
        _lordPeople = new int[ns]; _lordLot = new int[ns]; Array.Fill(_lordLot, -1);
        _settlementPeople = new int[ns];

        BuildLords();
        for (int si = 0; si < ns; si++) BuildSettlement(si);

        var holders = BindParcels();
        for (int h = 0; h < _hh.Count; h++) _settlementPeople[_hh.Settlement[h]] += _hh.MemberCount[h];
        return new Population
        {
            Region = _r, People = _p, Households = _hh, NowDay = _nowDay, LotHousehold = _lotHousehold, LotBeds = _lotBeds, LotKeeper = _lotKeeper,
            FiefLord = _fiefLord, FiefLordHousehold = _fiefLordHh, ParcelHolder = holders, PriestHousehold = _priestHh, SettlementPeople = _settlementPeople,
        };
    }

    // ---------- bed capacity from blueprint sleeping rooms ----------
    private static (int Min, int Max) BedRange(DwellingType t) => t switch
    {
        DwellingType.Cottage => (4, 7), DwellingType.Longhouse => (6, 10), DwellingType.Burgage => (6, 12), DwellingType.Manor => (10, 24), _ => (0, 0),
    };

    private int BedsOf(Lot lot)
    {
        string key = lot.BlueprintId + "|" + (int)lot.Dwelling;
        if (_bedCache.TryGetValue(key, out int v)) return v;
        int beds = 0, sleepArea = 0;
        try
        {
            var grid = _lib.Grid(lot.BlueprintId);
            foreach (var room in _lib.Detector.Detect(grid))
            {
                beds += room.Beds;
                if (room.DeclaredPurposes.Contains("sleeping")) sleepArea += room.Area;
            }
        }
        catch (Exception) { /* no blueprint yet: fall back to the minimum for the dwelling type */ }
        var (min, max) = BedRange(lot.Dwelling);
        return _bedCache[key] = Math.Clamp(Math.Max(beds, sleepArea / 3), min, max);
    }

    private int CultureOf(RegionSettlement s) => s.Realm >= 0 && s.Realm < _h.Realms.Count ? _h.Realms[s.Realm].CultureId : 0;

    // ---------- parcels ----------
    private int[] BindParcels()
    {
        var res = new int[_r.Parcels.Count];
        var firstHh = new int[_r.Settlements.Count]; Array.Fill(firstHh, -1);
        for (int h = _hh.Count - 1; h >= 0; h--) if (_hh.Dwelling[h] != DwellingType.Manor) firstHh[_hh.Settlement[h]] = h;
        for (int i = 0; i < res.Length; i++)
        {
            var pc = _r.Parcels[i];
            var o = pc.Owner;
            int holder = -1;
            switch (o.Kind)
            {
                case OwnerKind.Demesne: holder = o.FiefIndex >= 0 ? _fiefLordHh[o.FiefIndex] : -1; break;
                case OwnerKind.Glebe: holder = _priestHh[pc.Settlement] >= 0 ? _priestHh[pc.Settlement] : o.FiefIndex >= 0 ? _fiefLordHh[o.FiefIndex] : -1; break;
                default:
                    holder = o.LotIndex >= 0 && o.LotIndex < _lotHousehold[pc.Settlement].Length ? _lotHousehold[pc.Settlement][o.LotIndex] : -1;
                    if (holder < 0) holder = firstHh[pc.Settlement];
                    break;
            }
            res[i] = holder;
            if (holder >= 0) _hh.ParcelCount[holder]++;
        }
        return res;
    }
}
