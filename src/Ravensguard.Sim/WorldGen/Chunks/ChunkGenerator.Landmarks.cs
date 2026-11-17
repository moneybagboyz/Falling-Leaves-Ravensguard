using Ravensguard.Sim.WorldGen.Region;

namespace Ravensguard.Sim.WorldGen.Chunks;

public sealed partial class ChunkGenerator
{
    /// <summary>Priority of landmark props: with site markers, ahead of trees, hedges and yards, so a footprint is never overgrown.</summary>
    private const int PLandmark = 1;

    /// <summary>Tag in <see cref="PendingProp.Extra"/> for a landmark prop: 0x8000 plus the landmark's index in <see cref="RegionData.Landmarks"/>.</summary>
    public const ushort LandmarkExtraFlag = 0x8000;

    /// <summary>Stamps the footprints of the authored landmarks (<see cref="LandmarkPlacer"/>) that fall in this chunk.</summary>
    private void CollectLandmarkProps(Buf b, List<PendingProp> props)
    {
        var marks = Region.Landmarks;
        if (marks == null || marks.Count == 0) return;
        int cx0 = b.X0 + Pad, cy0 = b.Y0 + Pad;
        foreach (var lm in marks)
            foreach (var p in lm.Props)
            {
                int x = lm.X + p.Dx, y = lm.Y + p.Dy;
                if ((uint)(x - cx0) < Size && (uint)(y - cy0) < Size)
                    props.Add(new PendingProp(x, y, p.Kind, p.Variant, PLandmark, (ushort)(LandmarkExtraFlag | lm.Index)));
            }
    }
}
