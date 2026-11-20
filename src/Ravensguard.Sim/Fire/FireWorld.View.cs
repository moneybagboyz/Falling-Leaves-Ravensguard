namespace Ravensguard.Sim.Burning;

/// <summary>One burning cell as the street view draws it: the cell, its intensity (1 to 3), whether it has caught yet and whether it is a roof (flames over a roof show from outside).</summary>
public readonly record struct FireCellView(int X, int Y, int Z, byte Intensity, bool Smouldering, bool Roof);

/// <summary>What a fire left on a cell: char, ash, cracked daub, smouldering turf, scorched stone or charred rubble.</summary>
public readonly record struct ScarView(int X, int Y, int Z, ScarKind Kind);

/// <summary>An abstract building fire (smoke over the building from far off): the centre of the building.</summary>
public readonly record struct BuildingFireView(int Structure, int X, int Y, int DamagePm);

/// <summary>What the main thread sees of fire at the last barrier: an immutable copy (fire 6), so the street view never reads the book while it is stepped.</summary>
public sealed record FireView(long Sec, long Version, IReadOnlyList<FireCellView> Cells, IReadOnlyList<ScarView> Scars, IReadOnlyList<BuildingFireView> Buildings);

public sealed partial class FireWorld
{
    /// <summary>The newest view for the main thread (swapped whole at a barrier by <see cref="Publish"/>).</summary>
    public volatile FireView? View;
    /// <summary>Bumped whenever anything the view shows changed; <see cref="Publish"/> makes a new view only then.</summary>
    public long Version { get; private set; }

    private void Touched() => Version++;

    /// <summary>Builds the view when something changed since the last one (a barrier job: the book is quiet).</summary>
    public FireView Publish(long sec)
    {
        var v = View;
        if (v != null && v.Version == Version) return v;
        var cells = new List<FireCellView>(Book.Cells.Count);
        foreach (var c in Book.Cells.Values)
        {
            var (x, y, z) = Unpack(c.Key);
            cells.Add(new FireCellView(x, y, z, c.Intensity, c.State == CellFireState.Smouldering, Rooms.PieceAt(x, y, z)?.Kind == Ravensguard.Content.PieceKind.Roof));
        }
        var scars = new List<ScarView>(Book.Scars.Count);
        foreach (var (k, s) in Book.Scars) { var (x, y, z) = Unpack(k); scars.Add(new ScarView(x, y, z, s)); }
        var buildings = new List<BuildingFireView>(Book.Buildings.Count);
        foreach (var b in Book.Buildings.Values)
        {
            var p = ProfileOf(b.Structure);
            if (p != null) buildings.Add(new BuildingFireView(b.Structure, p.CentreX, p.CentreY, b.DamagePm));
        }
        return View = new FireView(sec, Version, cells, scars, buildings);
    }
}
