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
    public const uint StreamCaravan = RngStreams.CaravanEncounter, StreamTrip = RngStreams.CaravanTrip;

    private readonly EconomyWorld _ew;
    private readonly Func<IEnumerable<MarketBook>> _books;
    private readonly EconomyState _econ;
    private readonly bool[] _local, _made;
    private ContinentLink? _link;
    /// <summary>P7-IN4: the works' own caravans, which settle with their works instead of a merchant (null: none).</summary>
    public IOwnCaravans? Own { get; set; }
    /// <summary>What the border desk may still pay in today, mites (the desk's room: the glut it can still carry off at its price).</summary>
    public long DeskRoomMites => _deskRoom;
    /// <summary>The region's simulated world (the rules a caravan's tolls and customs are worked out from).</summary>
    public Ravensguard.Sim.Abstract.SimWorld SimWorld => _ew.World;

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
    public void Daily(GameTime now) { BoundDeskExports(now); DeskDaily(now); PlanDay(now); }

    /// <summary>The region's coin is steered to grow by this much a year, permille of its seed money a head (plan 4A.1: a money supply growing 1 to 2 percent a year).</summary>
    public const int CoinGrowthPermillePerYear = 10;
    /// <summary>Half the width of the band around that path, permille of the base: inside it the desk trades freely both ways.</summary>
    public const int CoinBandPermille = 50;

    /// <summary>
    /// The border desk's budgets (decision I42). The region's coin is steered along a path: the seed money a head (<c>seed_money_crowns_per_person</c>) for the people it has
    /// now, grown <see cref="CoinGrowthPermillePerYear"/> a year (simple), with a band of <see cref="CoinBandPermille"/> either side. The desk buys a glut for the
    /// neighbours' coin only while the region's coin is under the band's top, and its agents buy a short good from the neighbours only while the region's coin is over
    /// four times the band under the path (a short book matters more than the coin); each budget is what is left to that edge, and the desk's thresholds lean with the coin (a region under its path sells a glut from
    /// 1.5 x its cover instead of 2.8 x, one over it tops a book up from half its cover instead of 0.3 x). The desk paid out of an 8 percent float of the region's coin plus what its imports had taken out,
    /// and imported whatever a book lacked: seed 1234 took in about 40,000 crowns a year net (its ledger grew 90 percent in 25 years), seed 1 lost 2.6 percent a year once
    /// the exports were bounded. The rule reads only saved state (the ledger, the people and the year), so a loaded world gives the same budgets (the old float was not
    /// saved and was taken afresh from the loaded coin). Caravans (planned trips) are not bounded by it.
    /// </summary>
    private void BoundDeskExports(GameTime now)
    {
        var w = _ew.World; long people = Math.Max(1, w.Vital.Alive);
        long baseCoin = people * _econ.Money.SeedMoneyCrownsPerPerson * 100L;
        long years = Math.Max(0, now.Year - w.Population.NowYear);                       // whole years since the world began
        long path = baseCoin + (long)((Int128)baseCoin * CoinGrowthPermillePerYear * years / 1000), band = baseCoin * CoinBandPermille / 1000;
        long coin = _econ.Ledger.Audit().InPurses.Mites;
        _deskRoom = Math.Max(0, path + band - coin);
        _deskImportRoom = Math.Max(0, coin - (path - 4 * band));
        // the thresholds lean with the coin's place in the band (d from -1000 at the bottom to +1000 at the top): under the path the desk takes a glut from 1.5 x the
        // cover (2.8 x at the path and above); over it, it tops a short book up from half its cover (0.3 x at the path and below)
        long d = band <= 0 ? 0 : Math.Clamp((coin - path) * 1000 / band, -1000, 1000);
        _glutAbovePermille = d >= 0 ? GlutAbovePermilleOfCover : (int)(GlutAbovePermilleOfCover + (GlutAbovePermilleOfCover - 1500) * d / 1000);
        _shortBelowPermille = d <= 0 ? ShortBelowPermilleOfCover : (int)(ShortBelowPermilleOfCover + (500 - ShortBelowPermilleOfCover) * d / 1000);
    }
    private int _glutAbovePermille = GlutAbovePermilleOfCover, _shortBelowPermille = ShortBelowPermilleOfCover;
    /// <summary>What the desk may still pay in today (exports) and pay out (imports), mites; set by <see cref="BoundDeskExports"/> and drawn down by each trade.</summary>
    private long _deskRoom, _deskImportRoom;
}
