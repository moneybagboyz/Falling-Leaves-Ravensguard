using System;
using System.Collections.Generic;
using Godot;
using Ravensguard.Content;
using Ravensguard.Sim.Building;
using Ravensguard.Sim.Live;
using Ravensguard.Sim.WorldGen.Chunks;

namespace Ravensguard.Game.World;

/// <summary>One prop of a chunk as the far bands see it: its sprite id (the generator's name for it), local cell and whether it spans two cells.</summary>
public readonly record struct FarProp(string Sprite, int X, int Y, bool Wide);

/// <summary>
/// A chunk's content reduced to what the far bands draw: ground kind per cell, which cells hold building pieces (roofs go over those), wall material and props.
/// Plain arrays, so a baker thread can read it without touching the sim. Built on the main thread from a loaded chunk, or on a worker from generator output.
/// </summary>
public sealed class FarInput
{
    public const int N = 32;
    public int CX, CY;
    public readonly GroundKind[] Ground = new GroundKind[N * N];
    public readonly byte[] Variant = new byte[N * N];
    /// <summary>0 none, 1 building floor/door/window (roofed), 2 wall or post (roofed when a building floor is next to it, else a wall top).</summary>
    public readonly byte[] Piece = new byte[N * N];
    /// <summary>0 timber, 1 wattle, 2 stone.</summary>
    public readonly byte[] WallMat = new byte[N * N];
    public readonly List<FarProp> Props = new();

    static byte Mat(string id) => id.EndsWith("stone") || id.EndsWith("brick") || id.EndsWith("crenellated") ? (byte)2 : id.EndsWith("wattle") ? (byte)1 : (byte)0;

    static void Classify(FarInput f, int i, PieceKind? kind, string? id)
    {
        if (kind == null || id == null) return;
        switch (kind.Value)
        {
            case PieceKind.Ground: break;
            case PieceKind.Wall: case PieceKind.Post: f.Piece[i] = 2; f.WallMat[i] = Mat(id); break;
            default: f.Piece[i] = 1; break;
        }
    }

    /// <summary>From a chunk of the streamed region (level 0 plus the props of every level that sit on it).</summary>
    public static FarInput FromChunk(LoadedChunk c, ChunkGenerator gen)
    {
        var f = new FarInput { CX = c.X, CY = c.Y };
        if (c.Levels.TryGetValue(0, out var lv))
        {
            for (int i = 0; i < N * N; i++)
            {
                int code = lv.Tiles[i];
                var g = TileCode.Ground(code);
                f.Ground[i] = g == GroundKind.None ? GroundKind.Grass : g;
                f.Variant[i] = (byte)TileCode.Variant(code);
                var id = gen.Palette.Id(TileCode.Structure(code));
                Classify(f, i, gen.Library.Pieces.KindOf(id), id);
            }
            foreach (var p in lv.Props.Values) { var info = p.Info; f.Props.Add(new FarProp(info.Sprite, p.X, p.Y, info.Wide)); }
        }
        else Array.Fill(f.Ground, GroundKind.Grass);
        return f;
    }

    /// <summary>From a 32 x 32 block of the test hamlet.</summary>
    public static FarInput FromMap(HamletMap m, int bx, int by)
    {
        var f = new FarInput { CX = bx, CY = by };
        for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                int i = y * N + x, wx = bx * N + x, wy = by * N + y;
                var g = m.GroundAt(wx, wy, 0);
                f.Ground[i] = g == GroundKind.None ? GroundKind.Grass : g;
                f.Variant[i] = m.Grid.InBounds(wx, wy, 0) ? m.GroundVariant[m.Idx(wx, wy, 0)] : (byte)0;
                var id = m.StructureAt(wx, wy, 0);
                Classify(f, i, m.Pieces.KindOf(id), id);
            }
        foreach (var p in m.Props)
            if (p.Z == 0 && p.X >= bx * N && p.X < bx * N + N && p.Y >= by * N && p.Y < by * N + N) f.Props.Add(new FarProp(p.Sprite, p.X - bx * N, p.Y - by * N, p.Wide));
        return f;
    }
}

/// <summary>An RGBA8 image in managed memory (the baker's working type, so worker threads never touch engine objects).</summary>
public sealed class Rgba
{
    public readonly int W, H;
    public readonly byte[] Data;
    public Rgba(int w, int h) { W = w; H = h; Data = new byte[w * h * 4]; }

    public void Blit(Rgba src, int dx, int dy, bool transposed = false)
    {
        for (int y = 0; y < src.H; y++)
            for (int x = 0; x < src.W; x++)
            {
                int sx = transposed ? y : x, sy = transposed ? x : y;
                int s = (sy * src.W + sx) * 4;
                if (src.Data[s + 3] == 0) continue;
                int px = dx + x, py = dy + y;
                if ((uint)px >= (uint)W || (uint)py >= (uint)H) continue;
                int d = (py * W + px) * 4;
                Data[d] = src.Data[s]; Data[d + 1] = src.Data[s + 1]; Data[d + 2] = src.Data[s + 2]; Data[d + 3] = 255;
            }
    }

    public void Set(int x, int y, (byte R, byte G, byte B) c)
    {
        if ((uint)x >= (uint)W || (uint)y >= (uint)H) return;
        int d = (y * W + x) * 4;
        Data[d] = c.R; Data[d + 1] = c.G; Data[d + 2] = c.B; Data[d + 3] = 255;
    }
}

/// <summary>The baked Field / Command sprites read into managed memory once: 8x8 cells (field_8) and transparent minis (mini_16), plus each cell's most common colour.</summary>
public sealed class FarArt
{
    readonly Dictionary<string, Rgba> _spr = new();
    readonly Dictionary<string, (byte R, byte G, byte B)[]> _tri = new();
    public Rgba Get(string id) => _spr[id];
    public bool Has(string id) => _spr.ContainsKey(id);
    /// <summary>(dark, mid, light) of a cell: its most common colour and the nearest darker and lighter colours near it.</summary>
    public (byte R, byte G, byte B)[] Triple(string id) => _tri[id];
    public (byte R, byte G, byte B) Mid(string id) => _tri[id][1];

    public static FarArt Load(ArtAtlas art)
    {
        var f = new FarArt();
        var imgs = new Dictionary<string, byte[]>();
        foreach (var (id, r) in art.All)
        {
            if (r.Atlas != "field_8" && r.Atlas != "mini_16") continue;
            if (!imgs.TryGetValue(r.Atlas, out var data)) { var im = art.Atlas(r.Atlas).GetImage(); im.Convert(Image.Format.Rgba8); imgs[r.Atlas] = data = im.GetData(); }
            int aw = art.Atlas(r.Atlas).GetWidth();
            var s = new Rgba(r.W, r.H);
            for (int y = 0; y < r.H; y++) Buffer.BlockCopy(data, ((r.Y + y) * aw + r.X) * 4, s.Data, y * r.W * 4, r.W * 4);
            f._spr[id] = s;
            if (r.Atlas == "field_8") f._tri[id] = Tri(s);
        }
        return f;
    }

    static (byte, byte, byte)[] Tri(Rgba s)
    {
        var count = new Dictionary<int, int>();
        for (int i = 0; i < s.W * s.H; i++) { int k = s.Data[i * 4] << 16 | s.Data[i * 4 + 1] << 8 | s.Data[i * 4 + 2]; count[k] = count.GetValueOrDefault(k) + 1; }
        int mid = 0, best = -1;
        foreach (var (k, n) in count) if (n > best || (n == best && k < mid)) { best = n; mid = k; }
        static double Lum(int c) => 0.30 * ((c >> 16) & 255) + 0.59 * ((c >> 8) & 255) + 0.11 * (c & 255);
        int lighter = mid, darker = mid;
        double lm = Lum(mid), bl = double.MaxValue, bd = double.MaxValue;
        foreach (var k in count.Keys)
        {
            double dl = Lum(k) - lm;
            if (dl > 0 && dl < bl) { bl = dl; lighter = k; }
            if (dl < 0 && -dl < bd) { bd = -dl; darker = k; }
        }
        static (byte, byte, byte) C(int k) => ((byte)(k >> 16), (byte)(k >> 8), (byte)k);
        return new[] { C(darker), C(mid), C(lighter) };
    }
}

/// <summary>Composes the Field (8 px/m) and Command (2 px/m) images of one chunk from the baked minis. Pure and thread-safe: reads only the input and the art.</summary>
public static class FarBake
{
    public const int Field = 8, Pad = 12, FieldPx = 32 * Field, TopPx = FieldPx + 2 * Pad, CommandPx = 64;
    static readonly (byte R, byte G, byte B) Outline = (0x1a, 0x14, 0x18);

    static double Hash(int x, int y, int s)
    {
        unchecked
        {
            uint h = (uint)x * 374761393u + (uint)y * 668265263u + (uint)s * 1442695041u;
            h = (h ^ (h >> 13)) * 1274126177u; h ^= h >> 16;
            return h / 4294967296.0;
        }
    }

    public static string GroundTile(GroundKind g, int v) => g switch
    {
        GroundKind.Dirt => $"field.dirt.{v & 1}",
        GroundKind.Cobble => $"field.cobble.{v & 1}",
        GroundKind.Meadow => "field.meadow",
        GroundKind.ForestFloor => "field.forest_floor",
        GroundKind.LeafLitter => "field.leaf_litter",
        GroundKind.Sand => "field.sand",
        GroundKind.Mud => "field.mud",
        GroundKind.Marsh => "field.marsh",
        GroundKind.Gravel => "field.gravel",
        GroundKind.Rock => "field.rock",
        GroundKind.Scree => "field.scree",
        GroundKind.Peat => "field.peat",
        GroundKind.Water => "field.water_shallow",
        GroundKind.DeepWater => "field.water_deep",
        GroundKind.Ford => "field.ford",
        GroundKind.Bridge => "field.bridge",
        GroundKind.Furrow => "field.farm.ploughed",
        GroundKind.Sown => "field.farm.sown",
        GroundKind.Crop => v is 3 or 4 ? "field.farm.barley_ripe" : "field.farm.wheat_ripe",
        GroundKind.Stubble => "field.farm.stubble",
        GroundKind.Fallow => "field.farm.fallow",
        _ => $"field.grass.{v & 3}",
    };

    /// <summary>Roof cells of a chunk: building floors, and walls that stand next to a floor. Returns the mask and the roof material and part per cell.</summary>
    static void Roofs(FarInput f, bool[] roofed, string[] part, string[] mat)
    {
        const int N = FarInput.N;
        for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                int i = y * N + x;
                if (f.Piece[i] == 1) roofed[i] = true;
                else if (f.Piece[i] == 2)
                    for (int dy = -1; dy <= 1 && !roofed[i]; dy++)
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            int nx = x + dx, ny = y + dy;
                            if ((uint)nx < N && (uint)ny < N && f.Piece[ny * N + nx] == 1) { roofed[i] = true; break; }
                        }
            }
        var seen = new bool[N * N];
        var stack = new Stack<int>();
        var cells = new List<int>();
        for (int s = 0; s < N * N; s++)
        {
            if (!roofed[s] || seen[s]) continue;
            cells.Clear(); stack.Push(s); seen[s] = true;
            int x0 = N, x1 = -1, y0 = N, y1 = -1, stone = 0, other = 0;
            while (stack.Count > 0)
            {
                int c = stack.Pop(); cells.Add(c);
                int cx = c % N, cy = c / N;
                x0 = Math.Min(x0, cx); x1 = Math.Max(x1, cx); y0 = Math.Min(y0, cy); y1 = Math.Max(y1, cy);
                if (f.Piece[c] == 2) { if (f.WallMat[c] == 2) stone++; else other++; }
                for (int k = 0; k < 4; k++)
                {
                    int nx = cx + (k == 0 ? 1 : k == 1 ? -1 : 0), ny = cy + (k == 2 ? 1 : k == 3 ? -1 : 0);
                    if ((uint)nx >= N || (uint)ny >= N) continue;
                    int ni = ny * N + nx;
                    if (roofed[ni] && !seen[ni]) { seen[ni] = true; stack.Push(ni); }
                }
            }
            double h = Hash(x0 + f.CX * N, y0 + f.CY * N, 77);
            string m = stone > other ? (h < .5 ? "slate" : "tile") : (h < .6 ? "thatch" : "shingle");
            bool horizontal = (x1 - x0) >= (y1 - y0);
            double cx2 = (x0 + x1) / 2.0, cy2 = (y0 + y1) / 2.0;
            int cross = horizontal ? y1 - y0 + 1 : x1 - x0 + 1;
            foreach (int c in cells)
            {
                int cx = c % N, cy = c / N;
                double d = horizontal ? cy - cy2 : cx - cx2;
                string p = cross <= 2 || Math.Abs(d) <= 0.5 ? (horizontal ? "ridge" : "ridge_v") : d < 0 ? "hi" : "lo";
                part[c] = p; mat[c] = m;
            }
        }
    }

    static string TreeSprite(FarProp p, FarInput f)
    {
        int n = 0;
        int dot = p.Sprite.LastIndexOf('.');
        if (dot > 0) int.TryParse(p.Sprite.AsSpan(dot + 1), out n);
        string sp = Hash(p.X + f.CX * 32, p.Y + f.CY * 32, 91) < .22 ? "pine" : (n % 3) switch { 0 => "oak", 1 => "ash", _ => "birch" };
        return $"mini.tree.{sp}.summer";
    }

    /// <summary>The Field image: ground (opaque, 256 x 256, roofs drawn over buildings) and the top layer (trees, bushes, rocks and the like, transparent, 12 px of padding so a crown can overhang the next chunk).</summary>
    public static (Rgba Ground, Rgba Top) BakeField(FarInput f, FarArt art)
    {
        const int N = FarInput.N;
        var g = new Rgba(FieldPx, FieldPx);
        var roofed = new bool[N * N]; var part = new string[N * N]; var mat = new string[N * N];
        Roofs(f, roofed, part, mat);
        for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                int i = y * N + x;
                string id;
                if (roofed[i]) id = $"field.roof.{mat[i]}.{(part[i] == "ridge_v" ? "ridge" : part[i])}";
                else if (f.Piece[i] == 2) id = f.WallMat[i] == 2 ? "field.wall.stone" : f.WallMat[i] == 1 ? "field.wall.wattle" : "field.wall.timber";
                else id = GroundTile(f.Ground[i], f.Variant[i]);
                if (!art.Has(id)) id = "field.grass.0";
                g.Blit(art.Get(id), x * Field, y * Field, part[i] == "ridge_v");
            }
        // building outline: a dark line along every roof edge that borders open ground
        for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                if (!roofed[y * N + x]) continue;
                bool Open(int nx, int ny) => (uint)nx < N && (uint)ny < N && !roofed[ny * N + nx];
                for (int k = 0; k < Field; k++)
                {
                    if (Open(x, y + 1)) g.Set(x * Field + k, y * Field + Field - 1, Outline);
                    if (Open(x + 1, y)) g.Set(x * Field + Field - 1, y * Field + k, Outline);
                    if (Open(x, y - 1)) g.Set(x * Field + k, y * Field, Outline);
                    if (Open(x - 1, y)) g.Set(x * Field, y * Field + k, Outline);
                }
            }

        var top = new Rgba(TopPx, TopPx);
        var props = new List<FarProp>(f.Props);
        props.Sort((a, b) => a.Y != b.Y ? a.Y.CompareTo(b.Y) : a.X.CompareTo(b.X));
        foreach (var p in props)
        {
            string s = p.Sprite;
            int px = p.X * Field + Pad, py = p.Y * Field + Pad;
            if (s.StartsWith("object.tree")) { var spr = art.Get(TreeSprite(p, f)); top.Blit(spr, px + 4 - spr.W / 2, py + Field - 18); }
            else if (s.StartsWith("object.bush") || s.StartsWith("object.hedge")) top.Blit(art.Get("mini.bush"), px, py);
            else if (s is "object.boulder" or "object.rocks" || s.StartsWith("object.rock")) top.Blit(art.Get("mini.rock"), px, py);
            else if (s.StartsWith("object.stump") || s.StartsWith("object.rubble") || s.StartsWith("object.grave")) top.Blit(art.Get("mini.rock"), px, py);
            else
            {
                top.Blit(art.Get("mini.block"), px, py);
                if (p.Wide) top.Blit(art.Get("mini.block"), px + Field, py);
            }
        }
        return (g, top);
    }

    /// <summary>The Command image: one flat 2 x 2 block per cell in the cell's most common colour (a pixel in four goes lighter or darker), trees as dark canopy blocks.</summary>
    public static Rgba BakeCommand(FarInput f, FarArt art)
    {
        const int N = FarInput.N;
        var img = new Rgba(CommandPx, CommandPx);
        var roofed = new bool[N * N]; var part = new string[N * N]; var mat = new string[N * N];
        Roofs(f, roofed, part, mat);
        for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                int i = y * N + x;
                string id;
                if (roofed[i]) id = $"field.roof.{mat[i]}.{(part[i] == "ridge_v" ? "ridge" : part[i])}";
                else if (f.Piece[i] == 2) id = f.WallMat[i] == 2 ? "field.wall.stone" : f.WallMat[i] == 1 ? "field.wall.wattle" : "field.wall.timber";
                else id = GroundTile(f.Ground[i], f.Variant[i]);
                if (!art.Has(id)) id = "field.grass.0";
                var t = art.Triple(id);
                for (int k = 0; k < 4; k++)
                {
                    double h = Hash(x + f.CX * N, y + f.CY * N, 13 + k);
                    img.Set(x * 2 + (k & 1), y * 2 + (k >> 1), h < .16 ? t[2] : h > .88 ? t[0] : t[1]);
                }
            }
        foreach (var p in f.Props)
        {
            if (!p.Sprite.StartsWith("object.tree")) continue;
            var spr = art.Get(TreeSprite(p, f));
            // the canopy colours of the tree sprite: its two main colours, as a 2 x 2 block
            var mid = Sample(spr, 6, 5); var dark = Sample(spr, 9, 8);
            img.Set(p.X * 2, p.Y * 2, mid); img.Set(p.X * 2 + 1, p.Y * 2, mid); img.Set(p.X * 2, p.Y * 2 + 1, dark); img.Set(p.X * 2 + 1, p.Y * 2 + 1, dark);
        }
        return img;
    }

    static (byte, byte, byte) Sample(Rgba s, int x, int y) { int i = (y * s.W + x) * 4; return (s.Data[i], s.Data[i + 1], s.Data[i + 2]); }
}
