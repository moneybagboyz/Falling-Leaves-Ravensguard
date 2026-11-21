using Ravensguard.Sim.Carts;
using Ravensguard.Sim.Core;

namespace Ravensguard.Sim.Naval;

/// <summary>Integer geometry of a moving frame: headings are turns in 2^24ths (Q24; the overview's heading Q10 is <c>&gt;&gt; 14</c>), cosine and sine are Q30 from <see cref="IntMath.CosSinQ30"/>, so every platform gets the same bits.</summary>
public static class FrameMath
{
    public const int Turn = 1 << 24;
    public const int Mask = Turn - 1;
    private const long Half = 1L << 29;

    public static (long Cos, long Sin) CosSin(int headingQ24) => IntMath.CosSinQ30(headingQ24 & Mask, Turn);

    /// <summary>Whole degrees to Q24 turns.</summary>
    public static int FromDegrees(int deg) => (int)((long)deg * Turn / 360) & Mask;

    private static int Round30(long v) => (int)((v + Half) >> 30);

    /// <summary>
    /// The world point of local (lx, ly) of a frame at (px, py) heading h (cos, sin): local y runs along the heading (to the bow) and local x across it to the right (the heading turned a quarter turn clockwise with y down),
    /// <c>w = p + ly * f + lx * s</c> with <c>f = (cos, sin)</c>, <c>s = (-sin, cos)</c>.
    /// </summary>
    public static (int X, int Y) ToWorld(int px, int py, long cos, long sin, int lx, int ly) => (px + Round30((long)ly * cos - (long)lx * sin), py + Round30((long)ly * sin + (long)lx * cos));

    /// <summary>The local point of a world point: the same turn undone (it can differ from a point that was set in local millimetres by one millimetre of rounding, which is why a rider's local position is the one the frame keeps).</summary>
    public static (int Lx, int Ly) ToLocal(int px, int py, long cos, long sin, int wx, int wy)
    {
        long dx = wx - px, dy = wy - py;
        return (Round30(-dx * sin + dy * cos), Round30(dx * cos + dy * sin));
    }
}

/// <summary>What stands or lies on a deck and rides it (ships-and-naval-war 4.6: "things on board move with the vehicle": people, items and lights).</summary>
public enum RiderKind : byte { Person, Item, Light, Animal }

/// <summary>A rider's place in the frame it rides: local millimetres and the deck.</summary>
public sealed class Rider
{
    public RiderKind Kind;
    public ulong Id;
    public int Lx, Ly;
    public byte Deck;
}

/// <summary>A ship frame as the save keeps it (the later <c>naval</c> section is a list of these): the placement, the motion, the sub-millimetre remainders and the riders.</summary>
public sealed record ShipFrameState(ulong Id, int X, int Y, int Heading, int Speed, int TurnPerTick, long AccX, long AccY, byte[] RiderKinds, ulong[] RiderIds, int[] RiderLx, int[] RiderLy, byte[] RiderDecks);

/// <summary>
/// P7-SH-A, <c>ShipFrame</c> (ships-and-naval-war 4.6): a ship's part grid as a local frame with a transform (position in world millimetres, heading) that moves each 20 Hz tick, its <see cref="DeckMap"/> of decks, and what rides it. People, items and
/// lights stand in local coordinates; the world sees them through the transform (<see cref="ToWorld"/>). A rider that does not move keeps its local millimetres exactly while the ship turns, and its world point is always the transform of them, so
/// carrying a crowd through a turn never drifts a man by a millimetre (SH-13). Motion is whole numbers: a speed in mm/s along the heading and a turn per tick, with the sub-millimetre remainder kept (<see cref="ShipFrameState"/>), so a saved
/// frame continues exactly. No float, no draw.
/// </summary>
public sealed class ShipFrame
{
    public EntityId Id { get; }
    public DeckMap Deck { get; }
    public int XMm { get; set; }
    public int YMm { get; set; }
    /// <summary>The bow's direction, Q24 turns (0 along +x, a quarter turn along +y).</summary>
    public int Heading { get; private set; }
    /// <summary>Speed along the heading, mm/s.</summary>
    public int SpeedMmps { get; set; }
    /// <summary>The turn each 20 Hz tick, Q24 turns (a ship turning 3 degrees a second: <see cref="TurnOf"/>(3)).</summary>
    public int TurnPerTick { get; set; }
    public int HeadingQ10 => Heading >> 14;
    private long _accX, _accY;
    private long _cos, _sin; private bool _trig;
    private readonly List<Rider> _riders = [];
    public IReadOnlyList<Rider> Riders => _riders;

    public ShipFrame(EntityId id, DeckMap deck, int xMm, int yMm, int headingQ24 = 0) { Id = id; Deck = deck; XMm = xMm; YMm = yMm; Heading = headingQ24 & FrameMath.Mask; }

    /// <summary>A vehicle's frame from its part rows: the decks of <paramref name="rows"/> in a grid <paramref name="width"/> by <paramref name="length"/>.</summary>
    public static ShipFrame Of(EntityId id, IReadOnlyList<PartRow> rows, VehicleCatalog cat, int width, int length, int xMm, int yMm, int headingQ24 = 0) => new(id, DeckMap.Build(rows, cat, width, length), xMm, yMm, headingQ24);

    /// <summary>A blueprint's frame, its parts as built.</summary>
    public static ShipFrame Of(EntityId id, VehicleBlueprint bp, VehicleCatalog cat, int xMm, int yMm, int headingQ24 = 0) => Of(id, bp.Parts, cat, bp.Stats.WidthCells, bp.Stats.LengthCells, xMm, yMm, headingQ24);

    /// <summary>Q24 turns a tick for a turn of <paramref name="degPerS"/> degrees a second (a whole number of degrees; negative to port).</summary>
    public static int TurnOf(int degPerS) => (int)((long)degPerS * FrameMath.Turn / 360 / 20);

    public (long Cos, long Sin) CosSin { get { Trig(); return (_cos, _sin); } }
    private void Trig() { if (_trig) return; (_cos, _sin) = FrameMath.CosSin(Heading); _trig = true; }

    public void SetHeading(int q24) { Heading = q24 & FrameMath.Mask; _trig = false; }

    public (int X, int Y) ToWorld(int lx, int ly) { Trig(); return FrameMath.ToWorld(XMm, YMm, _cos, _sin, lx, ly); }
    public (int Lx, int Ly) ToLocal(int wx, int wy) { Trig(); return FrameMath.ToLocal(XMm, YMm, _cos, _sin, wx, wy); }
    public (int X, int Y) WorldOf(Rider r) => ToWorld(r.Lx, r.Ly);
    /// <summary>Whether a world point lies over the ship's footprint (its grid's rectangle).</summary>
    public bool Contains(int wx, int wy) { var (lx, ly) = ToLocal(wx, wy); return Deck.Inside(lx, ly); }

    // ------------------------------------------------------------------ motion

    /// <summary>One 20 Hz tick: the turn, then the advance along the new heading; the sub-millimetre remainder is carried.</summary>
    public void Step()
    {
        if (TurnPerTick != 0) SetHeading(Heading + TurnPerTick);
        if (SpeedMmps == 0) return;
        Trig();
        _accX += (long)SpeedMmps * _cos; _accY += (long)SpeedMmps * _sin;
        const long Per = 20L * (1L << 30);
        long dx = _accX / Per, dy = _accY / Per;
        _accX -= dx * Per; _accY -= dy * Per;
        XMm += (int)dx; YMm += (int)dy;
    }

    // ------------------------------------------------------------------ riders

    private int Find(RiderKind k, ulong id)
    {
        int lo = 0, hi = _riders.Count - 1;
        while (lo <= hi)
        {
            int mid = (lo + hi) >> 1; var r = _riders[mid];
            int c = r.Kind != k ? ((byte)r.Kind).CompareTo((byte)k) : r.Id.CompareTo(id);
            if (c == 0) return mid;
            if (c < 0) lo = mid + 1; else hi = mid - 1;
        }
        return ~lo;
    }

    public Rider? RiderOf(RiderKind k, ulong id) { int i = Find(k, id); return i >= 0 ? _riders[i] : null; }

    /// <summary>Puts something aboard at local (lx, ly) on a deck (riders stay in (kind, id) order); one already aboard is moved.</summary>
    public Rider Board(RiderKind kind, ulong id, int lx, int ly, int deck)
    {
        int i = Find(kind, id);
        if (i >= 0) { var r = _riders[i]; r.Lx = lx; r.Ly = ly; r.Deck = (byte)deck; return r; }
        var n = new Rider { Kind = kind, Id = id, Lx = lx, Ly = ly, Deck = (byte)deck };
        _riders.Insert(~i, n); return n;
    }

    /// <summary>Puts something aboard on a cell's centre.</summary>
    public Rider BoardAt(RiderKind kind, ulong id, int cx, int cy, int deck) => Board(kind, id, Deck.CentreX(cx), Deck.CentreY(cy), deck);

    public bool Leave(RiderKind kind, ulong id) { int i = Find(kind, id); if (i < 0) return false; _riders.RemoveAt(i); return true; }

    // ------------------------------------------------------------------ planks and ladders

    /// <summary>The world point at the end of a boarding plank run out from plank cell <paramref name="idx"/> (<see cref="DeckMap.Planks"/>): 3 m beyond the cell's centre along its outward step.</summary>
    public (int X, int Y) PlankEnd(int idx)
    {
        var p = Deck.Planks[idx];
        return ToWorld(Deck.CentreX(p.X) + p.Dx * Gangways.PlankMm, Deck.CentreY(p.Y) + p.Dy * Gangways.PlankMm);
    }

    // ------------------------------------------------------------------ the save

    public ShipFrameState Export()
    {
        int n = _riders.Count; var k = new byte[n]; var id = new ulong[n]; var lx = new int[n]; var ly = new int[n]; var dk = new byte[n];
        for (int i = 0; i < n; i++) { var r = _riders[i]; k[i] = (byte)r.Kind; id[i] = r.Id; lx[i] = r.Lx; ly[i] = r.Ly; dk[i] = r.Deck; }
        return new ShipFrameState(Id.Value, XMm, YMm, Heading, SpeedMmps, TurnPerTick, _accX, _accY, k, id, lx, ly, dk);
    }

    /// <summary>A frame as it was saved, over its decks (rebuilt from the vehicle's parts).</summary>
    public static ShipFrame Restore(ShipFrameState s, DeckMap deck)
    {
        var f = new ShipFrame(new EntityId(s.Id), deck, s.X, s.Y, s.Heading) { SpeedMmps = s.Speed, TurnPerTick = s.TurnPerTick, _accX = s.AccX, _accY = s.AccY };
        for (int i = 0; i < s.RiderIds.Length; i++) f._riders.Add(new Rider { Kind = (RiderKind)s.RiderKinds[i], Id = s.RiderIds[i], Lx = s.RiderLx[i], Ly = s.RiderLy[i], Deck = s.RiderDecks[i] });
        return f;
    }

    /// <summary>FNV-1a over the placement, motion and riders (a part of the <c>NavalHash</c>).</summary>
    public ulong Hash(ulong h)
    {
        h = Mix(h, (long)Id.Value); h = Mix(h, XMm); h = Mix(h, YMm); h = Mix(h, Heading); h = Mix(h, SpeedMmps); h = Mix(h, TurnPerTick); h = Mix(h, _accX); h = Mix(h, _accY);
        foreach (var r in _riders) { h = Mix(h, (long)r.Kind | (long)r.Deck << 8); h = Mix(h, (long)r.Id); h = Mix(h, r.Lx); h = Mix(h, r.Ly); }
        return h;
    }

    internal static ulong Mix(ulong h, long v) { unchecked { for (int i = 0; i < 8; i++) { h ^= (byte)(v >> (8 * i)); h *= 1099511628211UL; } } return h; }
}
