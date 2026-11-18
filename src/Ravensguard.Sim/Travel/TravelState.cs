using Ravensguard.Content;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Goods;

namespace Ravensguard.Sim.Travel;

public enum JourneyState : byte { Idle, Travelling, Paused, Arrived, Cancelled }

/// <summary>A route in region millimetres with the cumulative length, so a position is a pure function of the distance walked.</summary>
public sealed class RoutePath
{
    public long[] X { get; }
    public long[] Y { get; }
    public long[] Cum { get; }
    public long LengthMm => Cum[^1];

    public RoutePath(long[] x, long[] y)
    {
        if (x.Length != y.Length || x.Length == 0) throw new ArgumentException("a route needs matching, non-empty point lists");
        X = x; Y = y; Cum = new long[x.Length];
        for (int i = 1; i < x.Length; i++) Cum[i] = Cum[i - 1] + Dist(x[i] - x[i - 1], y[i] - y[i - 1]);
    }

    public static long Dist(long dx, long dy)
    {
        long n = dx * dx + dy * dy; if (n <= 0) return 0;
        long r = (long)Math.Sqrt(n); while (r * r > n) r--; while ((r + 1) * (r + 1) <= n) r++;
        return r;
    }

    public (long X, long Y, int HeadX, int HeadY) At(long progressMm)
    {
        long p = Math.Clamp(progressMm, 0, LengthMm);
        int i = Array.BinarySearch(Cum, p); if (i < 0) i = ~i - 1;
        i = Math.Clamp(i, 0, Math.Max(0, X.Length - 2));
        if (X.Length == 1) return (X[0], Y[0], 0, 1);
        long seg = Cum[i + 1] - Cum[i];
        long dx = X[i + 1] - X[i], dy = Y[i + 1] - Y[i];
        long t = seg == 0 ? 0 : p - Cum[i];
        long x = X[i] + (seg == 0 ? 0 : dx * t / seg), y = Y[i] + (seg == 0 ? 0 : dy * t / seg);
        return (x, y, Math.Sign(dx), Math.Sign(dy));
    }

    public (long X, long Y) Ahead(long progressMm, long aheadMm) { var (x, y, _, _) = At(progressMm + aheadMm); return (x, y); }
}

/// <summary>The journey in progress (saved): destination, route, how far along, and the log lines that become a <see cref="JourneyLog"/>.</summary>
public sealed class Journey
{
    public string DestName = "";
    public long StartSec, StartXMm, StartYMm, DestXMm, DestYMm;
    public required RoutePath Path;
    public long ProgressMm;
    /// <summary>Game second the journey has been advanced to.</summary>
    public long Sec;
    public JourneyState State;
    public long SlotDone = -1;
    public int SlotSpeedMmPerGs;
    public long BreakUntilSec;
    public int Leg;
    public long CoinSpent, FoodUsedG;
    public List<string> EncounterIds = [];
    public long TotalMm => Path.LengthMm;
}

/// <summary>What the Journal keeps of a finished trip (spec 4.2).</summary>
public sealed record JourneyLog(long StartSec, long EndSec, string Dest, long DistM, long CoinSpent, long FoodUsedG, string[] Encounters, bool Completed);

/// <summary>
/// Everything travel keeps for a run: the encounter director's memory, the journey, the camps, the snares, the carcasses, the interrupts and the people it made up. One object, one save section
/// ("travel"), advanced in game seconds only, so it gives the same results at 1x and 8x.
/// </summary>
public sealed partial class TravelState
{
    public ulong Seed { get; }
    public EntityId HeroId { get; set; } = EntityId.Make(IdKind.Person, 0);
    public TravelSettingsDef Cfg { get; }
    public EncounterTable Table { get; }
    public ContentDatabase Content { get; }
    public InterruptBus Bus { get; } = new();
    public IEncounterCast? CastSource { get; set; }
    public GoodCatalog? Goods { get; }
    public MassLedger? Ledger { get; }
    /// <summary>The hero's pack for meals and the camp's break-up (set by the game from the hero inventory; null in a bare world).</summary>
    public Func<Store?>? PackStore { get; set; }
    /// <summary>Opens a store by owner id (the economy's, so camp stock is in the mass ledger); null: a private store on <see cref="Ledger"/>.</summary>
    public Func<EntityId, Store>? OpenStore { get; set; }
    public bool Hosted { get; set; }
    public int LatitudeTenths { get; set; } = 480;

    public long GameStartSec { get; set; }
    public Journey? Journey { get; set; }
    public readonly List<JourneyLog> Logs = [];
    public readonly List<EncounterEvent> Encounters = [];
    public long LastEncounterSec = long.MinValue;
    public readonly SortedDictionary<string, long> LastById = new(StringComparer.Ordinal);
    public int LegSerial;
    public readonly SortedSet<string> LegFired = new(StringComparer.Ordinal);
    public readonly List<Traveller> Travellers = [];
    public ulong NextTraveller;
    public long LastMealSec;
    /// <summary>Set by the game when an encounter needs the player (the view reads and clears it).</summary>
    public EncounterEvent? PendingEncounter;
    /// <summary>The encounter on the road the hero is dealing with (review finding 6: it was held in the view node, so a save lost it), whether its options are still open, and whether a trade or talk window it opened must settle when it closes. Saved.</summary>
    public EncounterEvent? Current;
    public bool CurrentOpen, ScreenPending;

    /// <summary>Puts the encounter away (the journey is cancelled or begun anew).</summary>
    public void ClearCurrent() { Current = null; CurrentOpen = false; }

    public TravelState(ContentDatabase db, ulong seed, GoodCatalog? goods = null, MassLedger? ledger = null)
    {
        Content = db; Seed = seed; Goods = goods; Ledger = ledger;
        Table = EncounterTable.From(db); Cfg = Table.Cfg;
        _privateStores = [];
    }

    private readonly Dictionary<EntityId, Store> _privateStores;

    /// <summary>The store of an owner: the economy's when hosted, else a private open store on the ledger.</summary>
    public Store StoreOf(EntityId owner)
    {
        if (OpenStore != null) return OpenStore(owner);
        if (Goods == null || Ledger == null) throw new InvalidOperationException("travel needs a goods catalog and a mass ledger for stores");
        if (!_privateStores.TryGetValue(owner, out var s)) _privateStores[owner] = s = new Store(Goods, Ledger, StoreKind.Open, owner, 0, 0, enforce: false);
        return s;
    }

    public bool TryStore(EntityId owner, out Store s) { if (OpenStore != null || _privateStores.ContainsKey(owner)) { s = StoreOf(owner); return true; } s = null!; return false; }
    public IEnumerable<KeyValuePair<EntityId, Store>> PrivateStores => _privateStores;

    public int HoursSinceMeal(long sec) => (int)Math.Max(0, (sec - LastMealSec) / TimeConst.SecondsPerHour);
}

public sealed partial class TravelState
{
    /// <summary>True when nothing was ever recorded: such a state is not saved, so worlds nobody travelled in keep their bytes.</summary>
    public bool IsEmpty => Journey == null && Logs.Count == 0 && Encounters.Count == 0 && Camp == null && OldCamps.Count == 0 && Carcasses.Count == 0 && Snares.Count == 0 && Travellers.Count == 0
        && Companions.Count == 0 && Bus.Log.Count == 0 && LastById.Count == 0 && LegSerial == 0 && NextCamp == 0
        // the counters too (review finding 5): an emptied state with advanced serials is saved, so a load never hands out an id again or forgets the last meal
        && Current == null && !CurrentOpen && !ScreenPending && CampJob == null && NextTraveller == 0 && NextCarcass == 0 && NextSnare == 0 && LastMealSec == 0 && LastEncounterSec == long.MinValue;
}
