using Ravensguard.Sim.WorldGen.Region;

namespace Ravensguard.Sim.WorldGen.Chunks;

public sealed partial class ChunkGenerator
{
    /// <summary>A market stall of a settlement's square: its cell in region metres (the stall is two cells wide, X and X+1) and the side customers stand on (a unit step toward the middle of the square).</summary>
    public readonly record struct StallSpot(int X, int Y, int FaceDx, int FaceDy);

    /// <summary>The stalls the generator puts on a settlement's square (a town's; a village's green has none), in the order they are placed. The live people queue at these.</summary>
    public IReadOnlyList<StallSpot> StallsOf(int settlement)
    {
        var list = new List<StallSpot>();
        if (settlement < 0 || settlement >= Region.Settlements.Count) return list;
        var q = Region.Settlements[settlement].Plan.Square;
        bool horiz = q.W >= q.H;
        foreach (var f in FixedOf(settlement))
        {
            if (f.Kind != PropKind.Stall) continue;
            int fx = 0, fy = 0;
            if (horiz) fy = q.CenterY > f.Y ? 1 : -1; else fx = q.CenterX > f.X ? 1 : -1;
            list.Add(new StallSpot(f.X, f.Y, fx, fy));
        }
        return list;
    }
}
