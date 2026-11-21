namespace Ravensguard.Sim.Naval;

/// <summary>
/// P7-SH-A: gangplank portals (ships-and-naval-war 4.6): a boarding plank is a 3 m portal from a plank cell of one ship to the plank cell of another, or to the quay (a world point). It joins them when their ends lie within the plank's length
/// of each other; someone on the plank cell crosses by it and arrives on the other side in the other frame's local coordinates (or at the world point). Ladders inside a ship are the deck grid's own portals (<see cref="DeckMap.Grid"/>).
/// </summary>
public static class Gangways
{
    /// <summary>The length of a boarding plank (mm).</summary>
    public const int PlankMm = 3000;
    /// <summary>How far a plank's end may be from where it lands (mm): the plank, plus the half metre of the cell it lands on.</summary>
    public const int ReachMm = PlankMm + 500;

    /// <summary>Whether the plank at <paramref name="idxA"/> of <paramref name="a"/> reaches the plank at <paramref name="idxB"/> of <paramref name="b"/> (their cell centres within the reach).</summary>
    public static bool Reaches(ShipFrame a, int idxA, ShipFrame b, int idxB)
    {
        var pa = a.Deck.Planks[idxA]; var pb = b.Deck.Planks[idxB];
        var wa = a.ToWorld(a.Deck.CentreX(pa.X), a.Deck.CentreY(pa.Y)); var wb = b.ToWorld(b.Deck.CentreX(pb.X), b.Deck.CentreY(pb.Y));
        return Within(wa, wb, ReachMm);
    }

    /// <summary>Whether the plank at <paramref name="idx"/> of <paramref name="a"/> reaches a quay point (world mm).</summary>
    public static bool Reaches(ShipFrame a, int idx, int wx, int wy)
    {
        var p = a.Deck.Planks[idx];
        return Within(a.ToWorld(a.Deck.CentreX(p.X), a.Deck.CentreY(p.Y)), (wx, wy), ReachMm);
    }

    private static bool Within((int X, int Y) a, (int X, int Y) b, int mm) { long dx = a.X - b.X, dy = a.Y - b.Y; return dx * dx + dy * dy <= (long)mm * mm; }

    /// <summary>Which plank cell (index into <see cref="DeckMap.Planks"/>) a rider stands on, or -1.</summary>
    public static int PlankUnder(ShipFrame f, Rider r)
    {
        if (r.Deck != 0) return -1;
        int cx = f.Deck.CellX(r.Lx), cy = f.Deck.CellY(r.Ly);
        for (int i = 0; i < f.Deck.Planks.Count; i++) if (f.Deck.Planks[i].X == cx && f.Deck.Planks[i].Y == cy) return i;
        return -1;
    }

    /// <summary>
    /// A person on plank cell <paramref name="idxA"/> of <paramref name="a"/> walks the plank to plank cell <paramref name="idxB"/> of <paramref name="b"/>: false when he is not on a plank cell or the planks do not meet; else he is aboard
    /// <paramref name="b"/> on the cell's centre and no longer aboard <paramref name="a"/>.
    /// </summary>
    public static bool Cross(ShipFrame a, int idxA, ShipFrame b, int idxB, RiderKind kind, ulong id)
    {
        var r = a.RiderOf(kind, id); if (r == null || PlankUnder(a, r) != idxA || !Reaches(a, idxA, b, idxB)) return false;
        var pb = b.Deck.Planks[idxB];
        a.Leave(kind, id); b.BoardAt(kind, id, pb.X, pb.Y, 0);
        return true;
    }

    /// <summary>A person on a plank cell walks the plank onto the quay: he leaves the frame and his world point is returned; null when he is not on the plank cell or the quay point is out of reach.</summary>
    public static (int X, int Y)? CrossToQuay(ShipFrame a, int idx, int quayX, int quayY, RiderKind kind, ulong id)
    {
        var r = a.RiderOf(kind, id); if (r == null || PlankUnder(a, r) != idx || !Reaches(a, idx, quayX, quayY)) return null;
        a.Leave(kind, id);
        return (quayX, quayY);
    }

    /// <summary>Someone on the quay at a world point walks the plank onto plank cell <paramref name="idx"/> of a ship: false when the plank does not reach him.</summary>
    public static bool CrossFromQuay(ShipFrame a, int idx, int wx, int wy, RiderKind kind, ulong id)
    {
        if (!Reaches(a, idx, wx, wy)) return false;
        var p = a.Deck.Planks[idx];
        a.BoardAt(kind, id, p.X, p.Y, 0);
        return true;
    }
}
