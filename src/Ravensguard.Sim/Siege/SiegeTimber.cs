using Ravensguard.Sim.Core;
using Ravensguard.Sim.Goods;
using Ravensguard.Sim.Production;
using Ravensguard.Sim.Save;
using Ravensguard.Sim.WorldGen.Chunks;
using RegionData = Ravensguard.Sim.WorldGen.Region.Region;

namespace Ravensguard.Sim.Siege;

/// <summary>
/// P8-LW-1, felling timber for the engines (3.1): a host that has no timber in its camp fells real trees within 2 km of the camp. Each tree comes out of its chunk through a <c>RemoveObject</c> of the chunk's delta (a place the hero has not seen is first recorded as generated, so the
/// tree is gone for ever and never felled twice) and the wood it held comes off the tile's wood row, into the camp store as logs (<see cref="CreateReason.Cut"/>): the mass audit finds created equal to what the row lost, to the gram.
/// The nearest trees fall first; a tree whose tile has no wood left is not felled.
/// </summary>
public static class SiegeTimber
{
    public const int RadiusM = 2_000, MaxChunks = 200;
    /// <summary>The most wood one tree gives (300 kg of logs); a tile holding less gives what it has.</summary>
    public const long TreeG = 300_000;
    /// <summary>The timber of one ladder (40 kg of logs).</summary>
    public const long LadderG = 40_000;

    public readonly record struct Felled(int Trees, long LogsG);

    /// <summary>Fells trees within <see cref="RadiusM"/> of (<paramref name="cxM"/>, <paramref name="cyM"/>) (region metres) until <paramref name="wantG"/> of logs are in <paramref name="yard"/> or no tree is left in reach.</summary>
    public static Felled Fell(ChunkStore chunks, ChunkGenerator gen, ResourceLayer res, Store yard, ushort logs, long cxM, long cyM, long wantG, int day)
    {
        int size = ChunkContent.Size, per = ChunkGenerator.ChunksPerSide;
        int x0 = (int)Math.Max(0, (cxM - RadiusM) / size), x1 = (int)Math.Min(per - 1, (cxM + RadiusM) / size), y0 = (int)Math.Max(0, (cyM - RadiusM) / size), y1 = (int)Math.Min(per - 1, (cyM + RadiusM) / size);
        var order = new List<(long D2, int X, int Y)>();
        for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
            {
                long dx = Math.Max(0, Math.Max(x * (long)size - cxM, cxM - (x + 1) * (long)size)), dy = Math.Max(0, Math.Max(y * (long)size - cyM, cyM - (y + 1) * (long)size));
                if (dx * dx + dy * dy <= (long)RadiusM * RadiusM) order.Add((dx * dx + dy * dy, x, y));
            }
        order.Sort((a, b) => a.D2 != b.D2 ? a.D2.CompareTo(b.D2) : a.Y != b.Y ? a.Y.CompareTo(b.Y) : a.X.CompareTo(b.X));
        int trees = 0; long got = 0;
        for (int i = 0; i < order.Count && i < MaxChunks && got < wantG; i++)
        {
            var (_, cx, cy) = order[i]; var key = new ChunkKey(cx, cy, 0);
            if (chunks.GetSnapshot(key) == null)
            {
                var gc = gen.GenerateLevels(cx, cy);
                foreach (var (z, content) in gc.Levels) chunks.RecordFirstVisit(new ChunkKey(cx, cy, z), ChunkGenerator.Version, gc.ContentHash, content);
            }
            var now = chunks.Rebuild(key); if (now == null) continue;
            var found = new List<(long D2, ulong Id, int Tile)>();
            foreach (var (id, o) in now.Objects)
            {
                if (o.Data.Length < PropRecord.Bytes) continue;
                var p = PropRecord.Decode(o.Data); if (p.Kind != PropKind.Tree) continue;
                long wx = cx * (long)size + p.X, wy = cy * (long)size + p.Y, dx = wx - cxM, dy = wy - cyM, d2 = dx * dx + dy * dy;
                if (d2 > (long)RadiusM * RadiusM) continue;
                found.Add((d2, id, RegionData.Index((int)(wx / RegionData.TileM), (int)(wy / RegionData.TileM))));
            }
            found.Sort((a, b) => a.D2 != b.D2 ? a.D2.CompareTo(b.D2) : a.Id.CompareTo(b.Id));
            foreach (var (_, id, tile) in found)
            {
                if (got >= wantG) break;
                int row = res.Find(tile, ResourceKind.Wood); if (row < 0) continue;
                long take = Math.Min(TreeG, res.StockG(row)); if (take <= 0) continue;
                chunks.AppendDelta(key, [ChunkOp.RemoveObject(new EntityId(id))]);
                long g = res.Take(row, take, day);
                yard.Create(logs, g, CreateReason.Cut);
                got += g; trees++;
            }
        }
        return new Felled(trees, got);
    }
}
