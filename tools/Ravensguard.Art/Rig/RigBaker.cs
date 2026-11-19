using System.Text.Json;

namespace Ravensguard.Art.Rig;

/// <summary>An outfit is a stack of layer ids drawn in <see cref="RigLayers.Order"/>.</summary>
public sealed record Outfit(string Id, Stance Stance, string[] Layers);

/// <summary>Poses the rig, draws every layer per view/action/frame, cleans up, and composites outfits with an outline pass.</summary>
public sealed class RigBake
{
    public static readonly string[] ViewNames = { "front", "back", "side" };
    public const string AtlasName = "rig_32x64";

    public static readonly Outfit[] Outfits =
    {
        new("hero", Stance.Blade, new[] { "body", "hose", "linen", "gambeson", "sword" }),
        new("soldier", Stance.Blade, new[] { "body", "hose", "linen", "gambeson", "mail", "surcoat", "helm", "shield", "sword" }),
        new("spearman", Stance.Pole, new[] { "body", "hose", "linen", "gambeson", "mail", "surcoat", "helm", "shield", "spear" }),
        new("knight", Stance.Blade, new[] { "body", "hose", "linen", "gambeson", "mail", "plate", "surcoat", "cloak", "helm", "shield", "sword" }),
        new("peasant", Stance.Blade, new[] { "body", "hose", "linen", "tunic" }),
        new("traveller", Stance.Blade, new[] { "body", "hose", "linen", "tunic", "cloak", "hood" }),
    };

    public readonly record struct Key(string Layer, View View, Stance Stance, string Action, int Frame, int Plane);

    public readonly Palette Pal;
    public readonly List<RigLayer> Layers;
    public readonly Dictionary<Key, PixelBuffer> Sprites = new();
    readonly uint _out;

    public RigBake(Palette pal)
    {
        Pal = pal; _out = pal.P["out"];
        Layers = RigLayers.Build(pal);
        foreach (var st in new[] { Stance.Blade, Stance.Pole })
            foreach (var act in RigPoses.Actions)
            {
                var poses = RigPoses.Frames(act, st);
                for (int fi = 0; fi < poses.Length; fi++)
                    foreach (var v in new[] { View.Front, View.Back, View.Side })
                    {
                        var sk = new Skeleton(poses[fi], v);
                        foreach (var layer in Layers)
                        {
                            if (!layer.Has(st)) continue;
                            var ctx = new Ctx(sk);
                            layer.Render(ctx); ctx.Flush();
                            for (int p = 0; p < 3; p++)
                            {
                                Cleanup(ctx.Planes[p]);
                                if (ctx.Planes[p].Data.Any(c => c != 0)) Sprites[new Key(layer.Id, v, st, act, fi, p)] = ctx.Planes[p];
                            }
                            // P4-C: the limb-split copy (planes 3..17 = 3 + depth + 3 x group), only for layers that draw on a body bone
                            for (int p = 0; p < 15; p++)
                            {
                                var sp = ctx.Split[p];
                                if (!sp.Data.Any(c => c != 0)) continue;
                                Cleanup(sp);
                                if (sp.Data.Any(c => c != 0)) Sprites[new Key(layer.Id, v, st, act, fi, 3 + p)] = sp;
                            }
                        }
                    }
            }
    }

    public static int FrameCount(string action) => RigPoses.Frames(action, Stance.Blade).Length;

    /// <summary>Removes orphan pixels: opaque pixels with no opaque 8-neighbour.</summary>
    public static void Cleanup(PixelBuffer b)
    {
        var kill = new List<(int, int)>();
        for (int y = 0; y < b.H; y++)
            for (int x = 0; x < b.W; x++)
            {
                if (!b.Opaque(x, y)) continue;
                bool any = false;
                for (int dy = -1; dy <= 1 && !any; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                        if ((dx != 0 || dy != 0) && b.Opaque(x + dx, y + dy)) { any = true; break; }
                if (!any) kill.Add((x, y));
            }
        foreach (var (x, y) in kill) b.Clear(x, y);
    }

    /// <summary>Layers stacked in draw order (plane, then layer order), no outline.</summary>
    public PixelBuffer Stack(Outfit o, View v, string action, int frame, IReadOnlyList<(string From, string To)>? recolor = null)
    {
        var b = new PixelBuffer(32, 64);
        for (int p = 0; p < 3; p++)
            foreach (var id in RigLayers.Order)
                if (o.Layers.Contains(id) && Sprites.TryGetValue(new Key(id, v, o.Stance, action, frame, p), out var s)) b.Blit(s, 0, 0);
        if (recolor != null)
            foreach (var (from, to) in recolor) b = b.Recolor(PixelBuffer.RampSwap(Pal.R[from], Pal.R[to]));
        return b;
    }

    /// <summary>The outline ring (only the pixels outside the stacked silhouette).</summary>
    public PixelBuffer Ring(PixelBuffer stacked)
    {
        var r = new PixelBuffer(32, 64);
        for (int y = 0; y < 64; y++)
            for (int x = 0; x < 32; x++)
                if (!stacked.Opaque(x, y) && (stacked.Opaque(x - 1, y) || stacked.Opaque(x + 1, y) || stacked.Opaque(x, y - 1) || stacked.Opaque(x, y + 1)))
                    r.Data[y * 32 + x] = _out;
        return r;
    }

    /// <summary>Outfit composite with outline, ready to show.</summary>
    public PixelBuffer Composite(Outfit o, View v, string action, int frame, IReadOnlyList<(string, string)>? recolor = null)
    {
        var s = Stack(o, v, action, frame, recolor);
        var r = Ring(s);
        r.Blit(s, 0, 0);
        return r;
    }

    // ------------------------------------------------------------------ validation

    public readonly List<string> Warnings = new();
    static readonly string[] StrictActions = { "idle", "walk", "jog" };

    /// <summary>Errors for palette/outline problems and for clipping in idle/walk/jog. Clipping in combat and fall frames is a warning:
    /// reaches with a sword do not always fit the 32 px frame.</summary>
    public List<string> Validate()
    {
        var errs = new List<string>(); Warnings.Clear();
        foreach (var (k, b) in Sprites)
        {
            int bad = b.Data.Count(c => c != 0 && !Pal.Contains(c));
            if (bad > 0) errs.Add($"rig {k.Layer}/{ViewNames[(int)k.View]}/{k.Stance}/{k.Action}/{k.Frame}/p{k.Plane}: {bad} non-palette pixel(s)");
        }
        foreach (var o in Outfits)
            foreach (var act in RigPoses.Actions)
                for (int fi = 0; fi < FrameCount(act); fi++)
                    foreach (var v in new[] { View.Front, View.Back, View.Side })
                    {
                        string id = $"rig {o.Id}/{ViewNames[(int)v]}/{act}/{fi}";
                        var c = Composite(o, v, act, fi);
                        int open = 0, border = 0, bad = 0, ring = 0, opaque = 0, bx0 = 99, bx1 = -1, by0 = 99;
                        for (int y = 0; y < 64; y++)
                            for (int x = 0; x < 32; x++)
                            {
                                uint px = c.Data[y * 32 + x];
                                if (px == 0) continue;
                                opaque++;
                                if (!Pal.Contains(px)) { bad++; continue; }
                                if (px == _out) { ring++; continue; }
                                bx0 = Math.Min(bx0, x); bx1 = Math.Max(bx1, x); by0 = Math.Min(by0, y);
                                if (x == 0 || y == 0 || x == 31 || y == 63) border++;
                                if (!c.Opaque(x - 1, y) || !c.Opaque(x + 1, y) || !c.Opaque(x, y - 1) || !c.Opaque(x, y + 1)) open++;
                            }
                        if (opaque == 0) errs.Add(id + ": empty");
                        if (bad > 0) errs.Add($"{id}: {bad} non-palette pixel(s)");
                        if (ring == 0) errs.Add(id + ": no outline");
                        if (open > 0 && StrictActions.Contains(act)) errs.Add($"{id}: {open} edge pixel(s) not outlined");
                        if (border > 0 && StrictActions.Contains(act)) errs.Add($"{id}: {border} pixel(s) touch the frame border (clipped; x {bx0}..{bx1}, top y {by0})");
                        else if (border > 0 || open > 0) Warnings.Add($"{id}: touches frame border (x {bx0}..{bx1}, top y {by0})");
                    }
        return errs;
    }

    // ------------------------------------------------------------------ atlas

    sealed record Packed(string Layer, View View, Stance Stance, string Action, int Frame, int Plane, PixelBuffer Img, int Ox, int Oy);

    static (PixelBuffer, int, int)? Trim(PixelBuffer b)
    {
        int x0 = 99, y0 = 99, x1 = -1, y1 = -1;
        for (int y = 0; y < b.H; y++)
            for (int x = 0; x < b.W; x++)
                if (b.Data[y * b.W + x] != 0) { x0 = Math.Min(x0, x); y0 = Math.Min(y0, y); x1 = Math.Max(x1, x); y1 = Math.Max(y1, y); }
        if (x1 < 0) return null;
        var t = new PixelBuffer(x1 - x0 + 1, y1 - y0 + 1);
        for (int y = 0; y < t.H; y++) for (int x = 0; x < t.W; x++) t.Data[y * t.W + x] = b.Data[(y + y0) * b.W + x + x0];
        return (t, x0, y0);
    }

    /// <summary>Writes rig_32x64.png and rig_index.json (layer, view, stance, action, frame, plane to rect + trim offset).</summary>
    public void Write(string outDir)
    {
        Directory.CreateDirectory(outDir);
        var items = new List<Packed>();
        foreach (var (k, b) in Sprites)
            if (Trim(b) is { } t) items.Add(new Packed(k.Layer, k.View, k.Stance, k.Action, k.Frame, k.Plane, t.Item1, t.Item2, t.Item3));
        foreach (var o in Outfits)
            foreach (var act in RigPoses.Actions)
                for (int fi = 0; fi < FrameCount(act); fi++)
                    foreach (var v in new[] { View.Front, View.Back, View.Side })
                        if (Trim(Ring(Stack(o, v, act, fi))) is { } t)
                            items.Add(new Packed("outline." + o.Id, v, o.Stance, act, fi, 0, t.Item1, t.Item2, t.Item3));

        const int width = 1024;
        var order = items.OrderByDescending(i => i.Img.H).ThenBy(i => i.Layer, StringComparer.Ordinal).ToList();
        int x = 0, y = 0, rowH = 0;
        var pos = new Dictionary<Packed, (int X, int Y)>();
        foreach (var it in order)
        {
            if (x + it.Img.W + 1 > width) { x = 0; y += rowH + 1; rowH = 0; }
            pos[it] = (x, y); x += it.Img.W + 1; rowH = Math.Max(rowH, it.Img.H);
        }
        int height = y + rowH;
        var atlas = new PixelBuffer(width, height);
        foreach (var it in order) { var (px, py) = pos[it]; for (int j = 0; j < it.Img.H; j++) for (int i = 0; i < it.Img.W; i++) atlas.Data[(py + j) * width + px + i] = it.Img.Data[j * it.Img.W + i]; }
        Png.Write(Path.Combine(outDir, AtlasName + ".png"), atlas);

        // layer -> view -> stance -> action -> frame -> [plane, x, y, w, h, ox, oy]
        var tree = new SortedDictionary<string, object>(StringComparer.Ordinal);
        foreach (var g in items.GroupBy(i => i.Layer))
        {
            var views = new SortedDictionary<string, object>();
            foreach (var gv in g.GroupBy(i => i.View))
            {
                var stances = new SortedDictionary<string, object>();
                foreach (var gs in gv.GroupBy(i => i.Stance))
                {
                    var acts = new SortedDictionary<string, object>();
                    foreach (var ga in gs.GroupBy(i => i.Action))
                    {
                        int n = FrameCount(ga.Key);
                        var frames = new List<int[][]>();
                        for (int fi = 0; fi < n; fi++)
                            frames.Add(ga.Where(i => i.Frame == fi).OrderBy(i => i.Plane)
                                .Select(i => new[] { i.Plane, pos[i].X, pos[i].Y, i.Img.W, i.Img.H, i.Ox, i.Oy }).ToArray());
                        acts[ga.Key] = frames;
                    }
                    stances[gs.Key.ToString().ToLowerInvariant()] = acts;
                }
                views[ViewNames[(int)gv.Key]] = stances;
            }
            tree[g.Key] = views;
        }
        var doc = new
        {
            atlas = AtlasName + ".png", width, height, frame = new[] { 32, 64 }, anchorX = new { front = 16, back = 16, side = (int)Skeleton.SideBase },
            note = "west = side view mirrored. Draw planes 0..2, and within a plane the layers in 'order' (outline.* first). Rects are trimmed; place at (ox,oy) inside the 32x64 frame.",
            views = ViewNames, stances = new[] { "blade", "pole" }, planes = 3, order = RigLayers.Order, overlays = RigLayers.Overlays,
            split = "planes 3..17: the same layer split by limb group, plane = 3 + depth + 3 x group (0 rest, 1 left forearm+hand, 2 right forearm+hand, 3 left shin+foot, 4 right shin+foot); draw them instead of 0..2 to leave a lost limb out",
            actions = RigPoses.Actions.ToDictionary(a => a, a => FrameCount(a)), todo = RigPoses.Todo,
            outfits = Outfits.ToDictionary(o => o.Id, o => new { stance = o.Stance.ToString().ToLowerInvariant(), layers = o.Layers }),
            sprites = tree,
        };
        File.WriteAllText(Path.Combine(outDir, "rig_index.json"), JsonSerializer.Serialize(doc) + "\n");
    }

    // ------------------------------------------------------------------ contact sheet

    /// <summary>Rows: one per outfit and direction (S, W, E, N); columns: idle, walk x4, wind-up x2, strike x2.</summary>
    public PixelBuffer Sheet(string[] outfitIds, int scale, IReadOnlyDictionary<string, (string, string)[]>? recolor = null, string? colSpec = null)
    {
        var cols = new List<(string Act, int F)>();
        if (colSpec != null) foreach (var t in colSpec.Split(',')) { var q = t.Split(':'); cols.Add((q[0], int.Parse(q[1]))); }
        else
        {
            cols.Add(("idle", 0));
            for (int i = 0; i < 4; i++) cols.Add(("walk", i));
            for (int i = 0; i < 2; i++) cols.Add(("windup", i));
            for (int i = 0; i < 2; i++) cols.Add(("strike", i));
        }
        var dirs = new (View V, bool Mirror)[] { (View.Front, false), (View.Side, true), (View.Side, false), (View.Back, false) };
        int cw = 32 * scale + 6, chh = 64 * scale + 6, m = 8;
        int rows = outfitIds.Length * dirs.Length;
        var sheet = new PixelBuffer(m * 2 + cols.Count * cw, m * 2 + rows * chh + (outfitIds.Length - 1) * 10);
        Array.Fill(sheet.Data, 0xFF1D181Cu);
        int row = 0;
        foreach (var id in outfitIds)
        {
            var o = Outfits.First(x => x.Id == id);
            var rc = recolor != null && recolor.TryGetValue(id, out var r) ? r : null;
            foreach (var (v, mir) in dirs)
            {
                for (int c = 0; c < cols.Count; c++)
                {
                    var img = Composite(o, v, cols[c].Act, cols[c].F, rc);
                    if (mir) img = Mirrored(img);
                    int x = m + c * cw, y = m + row * chh + (row / dirs.Length) * 10;
                    sheet.Rect(x, y, cw - 2, chh - 2, 0xFF2B2420);
                    sheet.Blit(img.Scaled(scale), x + 2, y + 2);
                }
                row++;
            }
        }
        return sheet;
    }

    public static PixelBuffer Mirrored(PixelBuffer b)
    {
        var r = new PixelBuffer(b.W, b.H);
        for (int y = 0; y < b.H; y++) for (int x = 0; x < b.W; x++) r.Data[y * b.W + x] = b.Data[y * b.W + (b.W - 1 - x)];
        return r;
    }
}
