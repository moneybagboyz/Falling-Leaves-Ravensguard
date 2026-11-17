using Ravensguard.Sim.Core;
using Ravensguard.Sim.Live;
using Ravensguard.Sim.Save;
using Ravensguard.Sim.WorldGen.Region;
using RegionData = Ravensguard.Sim.WorldGen.Region.Region;

namespace Ravensguard.Sim.WorldGen.Chunks;

public sealed partial class ChunkGenerator
{
    private const int PFixed = 0, PSite = 1, PYard = 2, PHedge = 3, PTree = 4, PBush = 5;

    private void CollectProps(Buf b, List<PendingProp> props)
    {
        int cx0 = b.X0 + Pad, cy0 = b.Y0 + Pad;
        int chunkX = cx0 >> 5, chunkY = cy0 >> 5;
        bool In(int x, int y) => (uint)(x - cx0) < Size && (uint)(y - cy0) < Size;

        // fixed fixtures of the settlements nearby (wells, market stalls, braziers)
        if (_settlementsAt.TryGetValue(Key(chunkX, chunkY), out var near))
            foreach (int si in near)
                foreach (var f in FixedOf(si))
                    if (In(f.X, f.Y)) props.Add(new PendingProp(f.X, f.Y, f.Kind, f.Variant, PFixed));

        // site entrances
        if (_sitesAt.TryGetValue(Key(chunkX, chunkY), out var sites))
            foreach (int i in sites)
            {
                var s = Region.Sites[i];
                var k = s.Kind switch
                {
                    SiteKind.Barrow => PropKind.SiteBarrow, SiteKind.Cave => PropKind.SiteCave, SiteKind.Mine => PropKind.SiteMine,
                    SiteKind.Ruin => PropKind.SiteRuin, SiteKind.Battlefield => PropKind.SiteBattlefield, _ => PropKind.SiteHoard,
                };
                if (In(s.X, s.Y)) props.Add(new PendingProp(s.X, s.Y, k, 0, PSite, (ushort)i));
                int ring = s.Kind switch { SiteKind.Ruin => 9, SiteKind.Cave => 4, SiteKind.Barrow => 4, SiteKind.Mine => 3, SiteKind.Battlefield => 6, _ => 0 };
                for (int n = 0; n < ring; n++)
                {
                    ulong h = H(StSite, s.X * 131L + n, s.Y);
                    int rad = s.Kind == SiteKind.Battlefield ? 14 : s.Kind == SiteKind.Ruin ? 7 : 3;
                    int px = s.X + (int)(h % (ulong)(2 * rad + 1)) - rad, py = s.Y + (int)(h >> 16) % (2 * rad + 1) - rad;
                    if (!In(px, py) || (px == s.X && py == s.Y)) continue;
                    var dk = s.Kind switch { SiteKind.Ruin => PropKind.SiteRuin, SiteKind.Battlefield => PropKind.SiteBattlefield, SiteKind.Cave or SiteKind.Barrow => PropKind.Boulder, _ => PropKind.Rocks };
                    props.Add(new PendingProp(px, py, dk, (byte)(h >> 40 & 3), PSite, ushort.MaxValue));
                }
            }

        // yards, fences and farm goods of the lots on this chunk
        foreach (var st in b.Stamps)
            foreach (var (x, y, k, v) in st.Props)
                if (In(x, y)) props.Add(new PendingProp(x, y, k, (byte)v, PYard));

        // hedges where a field meets open ground
        for (int wy = cy0; wy < cy0 + Size; wy++)
            for (int wx = cx0; wx < cx0 + Size; wx++)
            {
                if (Outside(wx, wy)) continue;
                bool vx = (wx & 63) == 0, vy = (wy & 63) == 0;
                if (!vx && !vy) continue;
                int t = (wy >> 6) * N + (wx >> 6);
                bool boundary = false;
                if (vx && wx > 0) { int o = Region.ParcelMap[t - 1]; boundary |= (Region.ParcelMap[t] != 0) != (o != 0); }
                if (vy && wy > 0) { int o = Region.ParcelMap[t - N]; boundary |= (Region.ParcelMap[t] != 0) != (o != 0); }
                if (!boundary) continue;
                ulong h = H(StHedge, wx >> 3 << 3 | (vx ? 0 : 1), wy >> 3 << 3 | (vx ? 1 : 0));
                if ((h & 7) == 0) continue; // a gateway every so often
                props.Add(new PendingProp(wx, wy, PropKind.Hedge, (byte)(H(StHedge, wx, wy) & 3), PHedge));
            }

        // trees on a jittered 3 m lattice: density follows the forest layer
        for (int by = FloorDiv(cy0, 3); by <= FloorDiv(cy0 + Size - 1, 3); by++)
            for (int bx = FloorDiv(cx0, 3); bx <= FloorDiv(cx0 + Size - 1, 3); bx++)
            {
                ulong h = H(StTree, bx, by);
                int px = bx * 3 + (int)(h % 3), py = by * 3 + (int)(h >> 8) % 3;
                if (!In(px, py) || Outside(px, py)) continue;
                int dens = Bilinear(Region.Forest, px, py) / 64;
                int p = dens < 6 ? 3 : Math.Min(205, 8 + (dens - 6) * 2);
                if ((int)(h >> 16 & 255) < p) props.Add(new PendingProp(px, py, PropKind.Tree, (byte)(h >> 28 & 3), PTree));
            }

        // bushes on a 2 m lattice, rocks on a 4 m one
        for (int by = FloorDiv(cy0, 2); by <= FloorDiv(cy0 + Size - 1, 2); by++)
            for (int bx = FloorDiv(cx0, 2); bx <= FloorDiv(cx0 + Size - 1, 2); bx++)
            {
                ulong h = H(StBush, bx, by);
                int px = bx * 2 + (int)(h & 1), py = by * 2 + (int)(h >> 4 & 1);
                if (!In(px, py) || Outside(px, py)) continue;
                int dens = Bilinear(Region.Forest, px, py) / 64;
                if ((int)(h >> 16 & 255) < 2 + dens / 7) props.Add(new PendingProp(px, py, PropKind.Bush, 0, PBush));
            }
        for (int by = FloorDiv(cy0, 4); by <= FloorDiv(cy0 + Size - 1, 4); by++)
            for (int bx = FloorDiv(cx0, 4); bx <= FloorDiv(cx0 + Size - 1, 4); bx++)
            {
                ulong h = H(StRock, bx, by);
                int px = bx * 4 + (int)(h & 3), py = by * 4 + (int)(h >> 4 & 3);
                if (!In(px, py) || Outside(px, py)) continue;
                int t = (py >> 6) * N + (px >> 6);
                int slope = Region.SlopePermille[t];
                bool mountain = Region.Biomes[t] == Biome.Mountain;
                int p = 3 + slope / 8 + (mountain ? 22 : 0);
                if ((int)(h >> 16 & 255) < p) props.Add(new PendingProp(px, py, (mountain || slope > 140) && (h >> 40 & 3) == 0 ? PropKind.Boulder : PropKind.Rocks, 0, PBush));
            }
    }

    private List<FixedProp> FixedOf(int si) => _fixed.GetOrAdd(si, _ =>
    {
        var s = Region.Settlements[si];
        var list = new List<FixedProp>();
        var q = s.Plan.Square;
        if (q.W < 6 || q.H < 6) return list;
        list.Add(new FixedProp(q.CenterX, q.CenterY, PropKind.Well, 0));
        if (s.Class == SettlementClass.Town)
        {
            list.Add(new FixedProp(q.X + 1, q.Y + 1, PropKind.Brazier, 0)); list.Add(new FixedProp(q.Right - 2, q.Y + 1, PropKind.Brazier, 0));
            list.Add(new FixedProp(q.X + 1, q.Bottom - 2, PropKind.Brazier, 0)); list.Add(new FixedProp(q.Right - 2, q.Bottom - 2, PropKind.Brazier, 0));
            int n = Math.Clamp(q.W * q.H / 260, 2, 8), placed = 0;
            foreach (int ry in new[] { q.Y + 4, q.Bottom - 5 })
                for (int x = q.X + 6; x + 3 < q.Right - 4 && placed < n; x += 7)
                {
                    if (Math.Abs(x - q.CenterX) < 5 && Math.Abs(ry - q.CenterY) < 5) continue;
                    list.Add(new FixedProp(x, ry, PropKind.Stall, (byte)(H(StFixed, x, ry) % 3)));
                    placed++;
                }
        }
        else
        {
            list.Add(new FixedProp(q.X + 2, q.Y + 2, PropKind.Barrel, 0));
            list.Add(new FixedProp(q.Right - 3, q.Bottom - 3, PropKind.Haystack, 0));
        }
        return list;
    });

    private static bool BlocksTrees(byte f) => (f & (Buf.Road | Buf.Water | Buf.Build | Buf.Yard | Buf.Field | Buf.Paved)) != 0;

    private void EmitLevels(Buf b, List<PendingProp> props, GeneratedChunk res)
    {
        int cx0 = b.X0 + Pad, cy0 = b.Y0 + Pad;
        // resolve props in priority order (position order within a priority keeps it stable)
        props.Sort((p, q) => p.Priority != q.Priority ? p.Priority.CompareTo(q.Priority) : p.Y != q.Y ? p.Y.CompareTo(q.Y) : p.X.CompareTo(q.X));
        var accepted = new List<PendingProp>();
        foreach (var p in props)
        {
            if (!b.InChunk(p.X, p.Y)) continue;
            int idx = b.I(p.X, p.Y);
            var info = PropTable.Describe(p.Kind, p.Variant);
            if (!PropFits(b, p, idx, info)) continue;
            b.Flags[idx] |= Buf.Prop;
            if (info.Wide) b.Flags[b.I(p.X + 1, p.Y)] |= Buf.Prop;
            accepted.Add(p);
        }

        var ground = new ChunkContent();
        for (int ly = 0; ly < Size; ly++)
            for (int lx = 0; lx < Size; lx++)
            {
                int idx = b.I(cx0 + lx, cy0 + ly);
                var g = b.Ground[idx];
                int code = TileCode.Make(g, b.Variant[idx], b.Structure[idx], b.Fixture[idx]);
                if (code != 0) ground.Tiles[ly * Size + lx] = code;
            }
        foreach (var p in accepted)
        {
            var rec = new PropRecord(p.Kind, p.Variant, (byte)(p.X - cx0), (byte)(p.Y - cy0), p.Extra);
            ground.Objects[rec.Id(cx0 >> 5, cy0 >> 5, 0)] = new ObjectRecord { Id = rec.Id(cx0 >> 5, cy0 >> 5, 0), Data = rec.Encode() };
        }
        res.Levels[0] = ground;
        foreach (var (z, (sa, fa)) in b.Upper)
        {
            var c = new ChunkContent();
            for (int i = 0; i < sa.Length; i++)
                if (sa[i] != 0 || fa[i] != 0) c.Tiles[i] = TileCode.Make(GroundKind.None, 0, sa[i], fa[i]);
            if (!c.IsEmpty) res.Levels[z] = c;
        }
    }

    private bool PropFits(Buf b, PendingProp p, int idx, PropInfo info)
    {
        byte f = b.Flags[idx];
        if ((f & (Buf.Prop | Buf.Build | Buf.Deep)) != 0) return false;
        var g = b.Ground[idx];
        if (g is GroundKind.Water or GroundKind.DeepWater or GroundKind.Ford or GroundKind.Bridge) return false;
        if (info.Wide)
        {
            if (!b.InChunk(p.X + 1, p.Y)) return false;
            byte f2 = b.Flags[b.I(p.X + 1, p.Y)];
            if ((f2 & (Buf.Prop | Buf.Build | Buf.Deep | Buf.Water)) != 0) return false;
        }
        switch (p.Priority)
        {
            case PFixed: case PSite: return true;
            case PYard: return (f & (Buf.Road | Buf.Paved | Buf.Water)) == 0;
            case PHedge: return (f & (Buf.Road | Buf.Paved | Buf.Yard | Buf.Water)) == 0;
            default:
                if (BlocksTrees(f)) return false;
                if (p.Kind == PropKind.Tree)
                    for (int dy = -1; dy <= 1; dy++)
                        for (int dx = -1; dx <= 1; dx++)
                            if ((b.Flags[b.I(p.X + dx, p.Y + dy)] & (Buf.Road | Buf.Water | Buf.Build | Buf.Paved)) != 0) return false;
                return true;
        }
    }
}
