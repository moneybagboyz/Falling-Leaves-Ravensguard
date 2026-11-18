using Ravensguard.Sim.Abstract;
using Ravensguard.Sim.Caravans;
using Ravensguard.Sim.Continental;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Goods;
using Ravensguard.Sim.WorldGen.Region;

namespace Ravensguard.Sim.Economy.Markets;

/// <summary>Where a region sits in the continent: the shared state and the region's own id.</summary>
public sealed class ContinentLink(ContinentState state, int region)
{
    public ContinentState State { get; } = state;
    public int Region { get; } = region;
    public RegionLink Mine => State.Link(Region);
}

/// <summary>
/// The continent as a trade partner of the region, carried by caravans (continent-tick spec 3.6, caravans spec 3.3-3.6; replaces the daily stand-in of integration decision
/// I13/I26 that bought from and sold to a purse with a finite float). Merchants of the books plan trips to the border gates of the region from the prices they know
/// (a neighbour's summary, discounted by its age): a trip runs only when its expected margin beats its cost. The cargo is bought at the merchant's own book with his own
/// coin, travels the real road (<see cref="CaravanRoads"/>) at cart speed in the daylight hours, pays bridge and realm tolls, risks encounters (stream 0x230) and
/// is sold at the gate for coin the neighbour's summary pays out (limited by the season's <see cref="EdgeTrade"/> budget), with a back-haul bought at the gate and listed
/// at home. Coin and goods that cross the gate are booked as emigrated/immigrated and exported/imported so every region's audit holds and the continent's total is conserved.
/// <para>This file holds the state and the entry points; the planner is <c>CaravanPlanner.cs</c> and the hourly step <c>CaravanStep.cs</c> (both partial parts of this class).</para>
/// </summary>
public sealed partial class ContinentTrade
{
    /// <summary>Bridge toll per vehicle and the realm-gate toll per vehicle, mites; the goods toll at a realm gate, permille of cargo value (roads.jsonc defaults, caravans spec 2.2).</summary>
    public const int TollBridgeMites = 10, TollGateMites = 25, TollGoodsPermille = 5;
    public const int MinMarginPct = 8, PriceAgeDiscountPermillePerDay = 10, RiskPremiumPct = 2, CapitalCostPermillePerDay = 5;
    public const int DriverWageMites = 150, GuardWageMites = 1_750;
    public const int FoundingCashMites = 20_000;
    public const uint StreamCaravan = 0x230, StreamTrip = 0x231;

    private readonly EconomyWorld _ew;
    private readonly Func<IEnumerable<MarketBook>> _books;
    private readonly EconomyState _econ;
    private readonly bool[] _local, _made;
    private ContinentLink? _link;

    public long ImportedMites, ExportedMites, TollsMites, WagesMites;
    public long[] ImportedUnits, ExportedUnits;
    public int TripsStarted, TripsDone, CaravansFounded, CaravansLost, Ambushes;
    public long LootMites;

    /// <param name="madeLocally">Goods some recipe of the region makes: caravans do not bring them back (they would undercut the region's own workshops); they bring what no workshop here can make, the raw materials and the grain.</param>
    public ContinentTrade(EconomyWorld ew, Func<IEnumerable<MarketBook>> books, EconomyState econ, IEnumerable<ushort>? madeLocally = null)
    {
        _ew = ew; _books = books; _econ = econ;
        _made = new bool[econ.Goods.Count]; if (madeLocally != null) foreach (ushort g in madeLocally) _made[g] = true;
        var c = econ.Goods; int n = c.Count;
        _local = new bool[n];
        foreach (var g in new[] { "milk", "meat_fresh", "fish_fresh", "eggs", "vegetables", "cheese", "sheep", "cow", "pig", "hay", "straw", "firewood", "silver", "arming_sword", "whey", "sawdust", "offal", "bread" })
            if (c.TryIdOf(g, out ushort id)) _local[id] = true;
        ImportedUnits = new long[n]; ExportedUnits = new long[n];
    }

    /// <summary>The region's place in the continent. A region built without one (a bare economy in a test) gets a small isolated continent of its own the first time it is asked.</summary>
    public ContinentLink Link => _link ??= Standalone();
    public bool IsBound => _link != null;

    public void Bind(ContinentLink link) => _link = link;

    private ContinentLink Standalone()
    {
        var w = _ew.World; var region = w.Population.Region;
        int id = RegionCoord.OfCell(region.CellX, region.CellY).Id;
        long coin = _econ.Ledger.Audit().InPurses.Mites;
        var st = ContinentState.Standalone_(w.Seed, id, (int)Math.Max(1, w.Vital.Alive), coin);
        st.HarvestPermille = null;
        var l = new ContinentLink(st, id);
        w.Set(l);
        Ravensguard.Sim.Continental.ContinentTick.BindPrices(_econ.Goods);
        Ravensguard.Sim.Continental.ContinentTick.Run(st, [new ResidentRegion(id, _ew)], GameTime.FromGameHours(w.State.HourDone + 1));
        return l;
    }

    /// <summary>One day of trade (hour 3 of the day, after the market): planning of trips for the merchants whose caravans are home, founding of new caravans.</summary>
    public void Daily(GameTime now) { DeskDaily(now); PlanDay(now); }
}
