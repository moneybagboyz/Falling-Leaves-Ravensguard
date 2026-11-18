using System.Security.Cryptography;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.WorldGen.Region;

namespace Ravensguard.Sim.Caravans;

/// <summary>Kinds of vehicle a caravan is made of (caravans spec 2.1).</summary>
public enum VehicleKind : byte { PackHorse = 0, Cart = 1, OxWagon = 2 }

/// <summary>What a vehicle carries and how fast it goes. Speeds are the plan's real metres per second; a game second is 1/20 of a real one, so <c>mm per game second = real m/s x 1000 / 20</c>.</summary>
public static class Vehicles
{
    public static int CapKg(VehicleKind k) => k switch { VehicleKind.PackHorse => 100, VehicleKind.Cart => 500, _ => 1000 };
    public static int CapLitres(VehicleKind k) => k switch { VehicleKind.PackHorse => 250, VehicleKind.Cart => 1000, _ => 2000 };
    /// <summary>Millimetres per game second: cart 2.5 m/s real = 125, ox wagon 1.8 = 90, pack horse 3.0 = 150 (a cart goes 450 m per game hour, 8 km in 17.8 h).</summary>
    public static int SpeedMmPerGs(VehicleKind k) => k switch { VehicleKind.PackHorse => 150, VehicleKind.Cart => 125, _ => 90 };
    /// <summary>Price of a vehicle with its animals, mites: the base price of a cart plus a draught animal's keep (an ox wagon two oxen).</summary>
    public static long PriceMites(VehicleKind k) => k switch { VehicleKind.PackHorse => 12_000, VehicleKind.Cart => 19_800, _ => 36_000 };
    public static int Draught(VehicleKind k) => k switch { VehicleKind.PackHorse => 1, VehicleKind.Cart => 1, _ => 2 };
    /// <summary>Road-class speed factor, permille (caravans spec 2.1).</summary>
    public static int ClassPermille(RoadClass c) => c switch { RoadClass.Highway => 1000, RoadClass.Road => 1000, RoadClass.Track => 800, _ => 700 };
}

public enum CaravanState : byte { Idle, Loading, Outbound, AtGate, Inbound, Lost }

/// <summary>What a toll was for.</summary>
public enum TollKind : byte { Bridge = 0, RealmGate = 1 }

/// <summary>One toll point on the road: where along the path (millimetres from the start), what it is, and which way it is paid.</summary>
public readonly record struct TollMark(long AtMm, TollKind Kind);

/// <summary>
/// The road a caravan walks, from its market to the gate where it trades: a polyline in continent metres with the road class of each segment and the tolls on it
/// (bridges, and a realm gate when the neighbour belongs to another realm). Positions along it are millimetres; <see cref="Round"/> is there and back. Immutable once built.
/// </summary>
public sealed class CaravanPath
{
    public int[] X = [], Y = [];
    public byte[] Class = [];                 // road class of the segment i -> i+1
    public long[] CumMm = [0];
    public TollMark[] Tolls = [];
    public long LengthMm => CumMm[^1];
    public static long Isqrt(long n) { if (n <= 0) return 0; long x = (long)Math.Sqrt(n); while (x * x > n) x--; while ((x + 1) * (x + 1) <= n) x++; return x; }
    /// <summary>Millimetres of a full round trip.</summary>
    public long RoundMm => LengthMm * 2;

    public static CaravanPath FromPoints(IReadOnlyList<(int X, int Y, RoadClass Class)> pts, IEnumerable<TollMark>? tolls = null)
    {
        var p = new CaravanPath { X = new int[pts.Count], Y = new int[pts.Count], Class = new byte[Math.Max(0, pts.Count - 1)], CumMm = new long[pts.Count] };
        for (int i = 0; i < pts.Count; i++)
        {
            p.X[i] = pts[i].X; p.Y[i] = pts[i].Y;
            if (i > 0)
            {
                p.Class[i - 1] = (byte)pts[i].Class;
                long dx = p.X[i] - p.X[i - 1], dy = p.Y[i] - p.Y[i - 1];
                p.CumMm[i] = p.CumMm[i - 1] + Isqrt((dx * dx + dy * dy) * 1_000_000);                // millimetres, integer square root
            }
        }
        p.Tolls = tolls == null ? [] : [.. tolls.OrderBy(t => t.AtMm)];
        return p;
    }

    /// <summary>Metres position and the unit heading (x1024) at <paramref name="mm"/> along the way out (0..Length); beyond it the way back.</summary>
    public (long XMm, long YMm, int HeadX, int HeadY) At(long mm)
    {
        long len = LengthMm; bool back = mm > len;
        long d = back ? Math.Max(0, 2 * len - mm) : Math.Max(0, mm);
        if (X.Length == 0) return (0, 0, 1024, 0);
        if (X.Length == 1 || d <= 0) { var (hx0, hy0) = Heading(0, back); return ((long)X[0] * 1000, (long)Y[0] * 1000, hx0, hy0); }
        int i = Array.BinarySearch(CumMm, d); if (i < 0) i = ~i - 1;
        i = Math.Clamp(i, 0, X.Length - 2);
        long seg = CumMm[i + 1] - CumMm[i];
        long t = seg == 0 ? 0 : d - CumMm[i];
        long x = (long)X[i] * 1000 + ((long)X[i + 1] - X[i]) * 1000 * t / Math.Max(1, seg);
        long y = (long)Y[i] * 1000 + ((long)Y[i + 1] - Y[i]) * 1000 * t / Math.Max(1, seg);
        var (hx, hy) = Heading(i, back);
        return (x, y, hx, hy);
    }

    private (int, int) Heading(int seg, bool back)
    {
        if (X.Length < 2) return (1024, 0);
        seg = Math.Clamp(seg, 0, X.Length - 2);
        long dx = X[seg + 1] - X[seg], dy = Y[seg + 1] - Y[seg];
        long n = Math.Max(1, Isqrt(dx * dx + dy * dy));
        int hx = (int)(dx * 1024 / n), hy = (int)(dy * 1024 / n);
        return back ? (-hx, -hy) : (hx, hy);
    }

    /// <summary>Speed factor (permille) of the road under the position <paramref name="mm"/>.</summary>
    public int SpeedPermille(long mm)
    {
        if (Class.Length == 0) return 1000;
        long d = mm > LengthMm ? Math.Max(0, 2 * LengthMm - mm) : Math.Max(0, mm);
        int i = Array.BinarySearch(CumMm, d); if (i < 0) i = ~i - 1;
        i = Math.Clamp(i, 0, Class.Length - 1);
        return Vehicles.ClassPermille((RoadClass)Class[i]);
    }

    public string ComputeHash()
    {
        using var h = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var b = new byte[8];
        void L(long v) { System.Buffers.Binary.BinaryPrimitives.WriteInt64LittleEndian(b, v); h.AppendData(b); }
        for (int i = 0; i < X.Length; i++) { L(X[i]); L(Y[i]); L(CumMm[i]); }
        foreach (var c in Class) L(c);
        foreach (var t in Tolls) { L(t.AtMm); L((long)t.Kind); }
        return Convert.ToHexString(h.GetHashAndReset());
    }
}

/// <summary>One line of a caravan's cargo: a good, units (grams or pieces) and the mites it cost to buy.</summary>
public sealed class CargoLine { public ushort Good; public long Units; public long CostMites; public int Dest = -1; }

/// <summary>
/// A caravan (caravans spec 2.1): a merchant-owned train of carts with its crew and cargo, as a record. Its purse (<see cref="Account"/>) and cargo store live in the home region's
/// ledger and stores; this row holds what is needed to move it, to draw it and to settle it. Position is a pure function of (<see cref="Dist"/>, <see cref="AnchorSec"/>,
/// <see cref="PrevDist"/>): see <see cref="CaravanMotion.PositionOf"/>.
/// </summary>
public sealed class Caravan
{
    public EntityId Id;
    /// <summary>The region whose records own the caravan's purse, store and crew (E2: the id's region bits).</summary>
    public int Home;
    /// <summary>The region the caravan is in now: changes when it crosses into another region's gate (the record then belongs to that region's record set).</summary>
    public int Region;
    public int Settlement;                 // home market (index in the home region)
    public int OwnerBusiness = -1;         // index of the merchant business that owns the caravan
    public int Master = -1;                // person row in the home region
    public int OwnerHousehold = -1;
    public PurseId Account = PurseId.None;
    public VehicleKind[] Vehicles = [];
    /// <summary>How many of the vehicles go on the current trip (the planner takes as many as the cargo fills); 0 = all.</summary>
    public int Used;
    public int Fielded => Used > 0 ? Math.Min(Used, Vehicles.Length) : Vehicles.Length;
    public int[] Crew = [];                // person rows in the home region: drivers first, then guards
    public int Guards;
    public CaravanState State;
    public CaravanPath Path = new();
    public int Neighbour = -1;             // region the gate leads to
    public int GateIndex;                  // gate node used
    /// <summary>Millimetres along the round trip (0 = home, <see cref="CaravanPath.LengthMm"/> = the gate, twice that = home again) at <see cref="AnchorSec"/>.</summary>
    public long Dist;
    public long AnchorSec;
    /// <summary>Millimetres along the round trip one hour before <see cref="AnchorSec"/> (equal to <see cref="Dist"/> when it rested that hour).</summary>
    public long PrevDist;
    /// <summary>Game second before which the caravan does not move (loading, trading at the gate).</summary>
    public long WaitUntilSec;
    public long DepartSec, FoundedSec;
    public long ArrearsMites;
    public long ExpectedProfitMites, TripCostMites, FloatMites, PurchaseMites;
    public List<CargoLine> Cargo = [];
    public int Trips, LostDay = -1;
    public long LastTollMm = -1;
    public bool TradedAtGate;
    public long ProfitTotalMites;
    public int DangerX = 0;
    /// <summary>Coin the owner borrowed from his lord's treasury to buy the wagons, and whose treasury it is (repaid from the first trips' takings).</summary>
    public long DebtMites;
    public ulong DebtTo;

    public int CapKg { get { int c = 0; for (int i = 0; i < Fielded; i++) c += Ravensguard.Sim.Caravans.Vehicles.CapKg(Vehicles[i]); return c; } }
    public int SlowestMmPerGs { get { int s = int.MaxValue; for (int i = 0; i < Fielded; i++) s = Math.Min(s, Ravensguard.Sim.Caravans.Vehicles.SpeedMmPerGs(Vehicles[i])); return s == int.MaxValue ? 0 : s; } }
    public bool Active => State != CaravanState.Idle && State != CaravanState.Lost;
    public EntityId StoreOwner => Id;

    public Caravan Clone()
    {
        var c = (Caravan)MemberwiseClone();
        c.Cargo = Cargo.Select(l => new CargoLine { Good = l.Good, Units = l.Units, CostMites = l.CostMites, Dest = l.Dest }).ToList();
        c.Vehicles = (VehicleKind[])Vehicles.Clone(); c.Crew = (int[])Crew.Clone();
        return c;
    }
}

/// <summary>Where a caravan is, from the record alone: the same function the abstract hour step and the live bubble evaluate, so they cannot disagree (spec 3.1, G-L11).</summary>
public static class CaravanMotion
{
    /// <summary>Millimetres along the round trip at game second <paramref name="nowSec"/>.</summary>
    public static long DistAt(Caravan c, long nowSec)
    {
        if (c.PrevDist == c.Dist || nowSec >= c.AnchorSec) return c.Dist;
        if (nowSec <= c.AnchorSec - 3600) return c.PrevDist;                            // only the hour just stepped is interpolated
        return c.PrevDist + (c.Dist - c.PrevDist) * (nowSec - (c.AnchorSec - 3600)) / 3600;
    }

    /// <summary>The continent position (millimetres) and heading of the lead vehicle.</summary>
    public static (long XMm, long YMm, int HeadX, int HeadY) PositionOf(Caravan c, long nowSec) => c.Path.At(DistAt(c, nowSec));
}

/// <summary>The caravans of the world, ascending id (stable order everywhere).</summary>
public sealed class CaravanTable
{
    public const int MaxActive = 60;
    private readonly SortedDictionary<EntityId, Caravan> _byId = [];
    public IEnumerable<Caravan> All => _byId.Values;
    public int Count => _byId.Count;
    public Caravan? Get(EntityId id) => _byId.GetValueOrDefault(id);
    public void Add(Caravan c) => _byId.Add(c.Id, c);
    public bool Remove(EntityId id) => _byId.Remove(id);
    public int ActiveCount() { int n = 0; foreach (var c in _byId.Values) if (c.State != CaravanState.Lost) n++; return n; }
    /// <summary>Caravans whose record is in <paramref name="region"/> now (the region's record set).</summary>
    public IEnumerable<Caravan> InRegion(int region) { foreach (var c in _byId.Values) if (c.Region == region) yield return c; }

    public string ComputeHash()
    {
        using var h = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var b = new byte[8];
        void L(long v) { System.Buffers.Binary.BinaryPrimitives.WriteInt64LittleEndian(b, v); h.AppendData(b); }
        foreach (var c in _byId.Values)
        {
            L((long)c.Id.Value); L(c.Home); L(c.Region); L(c.Settlement); L(c.OwnerBusiness); L(c.Master); L(c.Account.Index); L((long)c.State); L(c.Neighbour); L(c.Dist); L(c.AnchorSec);
            L(c.PrevDist); L(c.WaitUntilSec); L(c.DepartSec); L(c.ArrearsMites); L(c.ExpectedProfitMites); L(c.FloatMites); L(c.PurchaseMites); L(c.Trips); L(c.LostDay); L(c.Guards); L(c.Used);
            L(c.TradedAtGate ? 1 : 0); L(c.ProfitTotalMites); L(c.DebtMites); L((long)c.DebtTo);
            foreach (var v in c.Vehicles) L((long)v);
            foreach (int m in c.Crew) L(m);
            foreach (var l in c.Cargo) { L(l.Good); L(l.Units); L(l.CostMites); L(l.Dest); }
            h.AppendData(System.Text.Encoding.ASCII.GetBytes(c.Path.ComputeHash()));
        }
        return Convert.ToHexString(h.GetHashAndReset());
    }
}
