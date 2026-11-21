using System;
using System.Collections.Generic;
using System.Diagnostics;
using Godot;
using Ravensguard.Sim.Battle;
using Ravensguard.Sim.Combat.Melee2;

namespace Ravensguard.Game.World;

/// <summary>
/// The battle crowd (P7-BB-E; REDESIGN_PLAN 2.7, battle-bubble-and-formations 9; source: the Phase 0 spike, docs/spikes/03-crowds.md). One MultiMesh per layer group (lying bodies, standing soldiers,
/// Command blocks, icons, shape markers), every instance carrying an atlas frame index (into a table of the frames' texel rectangles), a palette row and wound flags, standing soldiers y-sorted every frame
/// by a counting sort on the band's pixel row (ties by fighter id, so a frame is the same however the camera moves). Street and Close draw the 32x64 people sprites, Field the baked 8x16
/// troop minis (front, back, side, west mirrored) at 4 world units a texel, Command one banner block per formation with its role icon and team shape marker.
/// Nothing here is game state: the layer reads <see cref="BattleSnapshot"/>s published once per tick, interpolates between the last two, and never writes back. The world is metres x 32 (a
/// Street pixel); the caller's camera supplies the zoom (Close 2, Street 1, Field 1/4, Command 1/16), so a texel is always a whole number of screen pixels.
/// </summary>
public sealed partial class CrowdLayer : Node2D
{
    public const float WorldPerM = 32f;
    public const int Stride = 12;
    const int MaxBuckets = 1 << 16;

    /// <summary>Screen pixels per metre of a band (Close 64, Street 32, Field 8, Command 2).</summary>
    public static float Ppm(StreetView.Band b) => b switch { StreetView.Band.Close => 64f, StreetView.Band.Street => 32f, StreetView.Band.Field => 8f, _ => 2f };
    /// <summary>World units a texel covers in a band (the camera zoom takes Close's second pixel).</summary>
    public static float TexelWorld(StreetView.Band b) => b switch { StreetView.Band.Field => 4f, StreetView.Band.Command => 16f, _ => 1f };

    /// <summary>The art team of a fight team: red, blue, green, gold, iron, neutral (the rows of <c>mini.ramps</c>).</summary>
    public static readonly int[] RowOfTeam = { 1, 2, 3, 4, 5, 0 };
    public static int RowOf(int team) => RowOfTeam[(uint)team < RowOfTeam.Length ? team : RowOfTeam.Length - 1];

    static readonly string[] Roles = { "levy", "spearman", "archer", "crossbowman", "sergeant", "man_at_arms", "knight", "engineer", "sailor", "marine" };
    static readonly string[] Views = { "front", "back", "side" };
    // the 32x64 people sprites (one view, mirrored for west) that stand for each role at Street and Close
    static readonly string[] StreetSprite =
    {
        "person.man_red", "person.spearman_red", "person.archer", "person.archer", "person.soldier_red", "person.soldier_red", "person.soldier_red", "person.man_red", "person.man_red", "person.coif_axeman",
    };
    static readonly string[] TeamNames = { "neutral", "red", "blue", "green", "gold", "iron" };

    readonly record struct Fr(int Index, int W, int H);

    sealed class Group
    {
        public MultiMesh Mm = null!; public MultiMeshInstance2D Node = null!; public Rid Rid;
        public float[] Buf = Array.Empty<float>(); public int Cap, N;
        public void Reset() => N = 0;
    }

    readonly ArtAtlas _art;
    readonly Group _bodies = new(), _stand = new(), _blocks = new(), _icons = new(), _marks = new();
    readonly ShaderMaterial _spriteMat, _blockMat;
    readonly Texture2D _mini, _people, _miniFrames, _peopleFrames;
    readonly Vector2 _miniSize, _peopleSize;
    readonly Fr[] _street = new Fr[Roles.Length];
    readonly Fr[,] _field = new Fr[Roles.Length, 3];
    readonly Fr[] _icon = new Fr[Roles.Length], _mark = new Fr[6];
    readonly Fr _fallen;

    BattleSnapshot _prev = new(), _cur = new(), _spare = new();
    bool _havePrev;
    StreetView.Band _band = StreetView.Band.Street;
    // per frame scratch
    int[] _idx = Array.Empty<int>(), _key = Array.Empty<int>(), _sorted = Array.Empty<int>(), _count = new int[1024];
    float[] _px = Array.Empty<float>(), _py = Array.Empty<float>();
    readonly Dictionary<long, int> _cluster = new();
    FormationMark[] _clusterMarks = new FormationMark[64];

    /// <summary>Last frame's cost, milliseconds (for the frame run): interpolation and culling, the y-sort, the instance fill, the upload to the renderer.</summary>
    public double CullMs, SortMs, FillMs, UploadMs;
    public int Drawn, Standing, Fallen, Blocks;

    public StreetView.Band Band { get => _band; set { if (_band == value) return; _band = value; ApplyBand(); } }

    public CrowdLayer(ArtAtlas art)
    {
        _art = art;
        Name = "CrowdLayer";
        TextureFilter = TextureFilterEnum.Nearest;
        var ramps = ImageTexture.CreateFromImage(art.Crop("mini.ramps"));
        _mini = art.Atlas("mini_16"); _miniSize = new Vector2(_mini.GetWidth(), _mini.GetHeight());
        _people = art.Atlas("people_32x64"); _peopleSize = new Vector2(_people.GetWidth(), _people.GetHeight());
        _spriteMat = new ShaderMaterial { Shader = GD.Load<Shader>("res://World/CrowdSprite.gdshader") };
        _spriteMat.SetShaderParameter("ramps", ramps); _spriteMat.SetShaderParameter("ramp_rows", (float)TeamNames.Length);
        _blockMat = new ShaderMaterial { Shader = GD.Load<Shader>("res://World/CrowdBlock.gdshader") };
        _blockMat.SetShaderParameter("ramps", ramps); _blockMat.SetShaderParameter("ramp_rows", (float)TeamNames.Length);

        // the frame tables: one RGBA float texel (x, y, w, h) per sprite, in the order they were asked for; an instance carries only the index
        var miniTable = new List<Color>(); var peopleTable = new List<Color>();
        Fr Of(string id, List<Color> table) { var r = art.Get(id); table.Add(new Color(r.X, r.Y, r.W, r.H)); return new Fr(table.Count - 1, r.W, r.H); }
        for (int i = 0; i < Roles.Length; i++)
        {
            _street[i] = Of(StreetSprite[i], peopleTable);
            for (int v = 0; v < 3; v++) _field[i, v] = Of($"mini.troop.{Roles[i]}.{Views[v]}", miniTable);
            _icon[i] = Of($"mini.icon.{Roles[i]}", miniTable);
        }
        for (int t = 0; t < 6; t++) _mark[t] = Of($"mini.mark.{TeamNames[t]}", miniTable);
        _fallen = Of("mini.troop.fallen", miniTable);
        _miniFrames = FrameTable(miniTable); _peopleFrames = FrameTable(peopleTable);

        var mesh = UnitQuad();
        // draw order: bodies under standing soldiers under blocks under icons under markers
        MakeGroup(_bodies, mesh, _spriteMat); MakeGroup(_stand, mesh, _spriteMat); MakeGroup(_blocks, mesh, _blockMat); MakeGroup(_icons, mesh, _spriteMat); MakeGroup(_marks, mesh, _spriteMat);
        ApplyBand();
    }

    static ImageTexture FrameTable(List<Color> frames)
    {
        var img = Image.CreateEmpty(frames.Count, 1, false, Image.Format.Rgbaf);
        for (int i = 0; i < frames.Count; i++) img.SetPixel(i, 0, frames[i]);
        return ImageTexture.CreateFromImage(img);
    }

    static ArrayMesh UnitQuad()
    {
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        Vector2[] p = { new(-0.5f, -1), new(0.5f, -1), new(0.5f, 0), new(-0.5f, 0) };
        Vector2[] uv = { new(0, 0), new(1, 0), new(1, 1), new(0, 1) };
        foreach (int i in new[] { 0, 1, 2, 0, 2, 3 }) { st.SetUV(uv[i]); st.AddVertex(new Vector3(p[i].X, p[i].Y, 0)); }
        return st.Commit();
    }

    void MakeGroup(Group g, Mesh mesh, ShaderMaterial mat)
    {
        g.Mm = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform2D, UseCustomData = true, Mesh = mesh, InstanceCount = 0 };
        g.Node = new MultiMeshInstance2D { Multimesh = g.Mm, Material = mat, TextureFilter = TextureFilterEnum.Nearest };
        AddChild(g.Node);
        g.Rid = g.Mm.GetRid();
    }

    static void Ensure(Group g, int n)
    {
        if (n <= g.Cap) return;
        int cap = Math.Max(64, Math.Max(n, g.Cap * 2));
        g.Mm.InstanceCount = cap; g.Cap = cap; g.Buf = new float[cap * Stride];
    }

    void ApplyBand()
    {
        bool cmd = _band == StreetView.Band.Command, street = _band is StreetView.Band.Street or StreetView.Band.Close;
        _bodies.Node.Visible = !cmd; _stand.Node.Visible = !cmd; _blocks.Node.Visible = cmd; _icons.Node.Visible = cmd; _marks.Node.Visible = cmd;
        // the sprite material is shared by bodies, soldiers, icons and markers: the standing group's atlas is the band's; icons and markers always read the mini atlas, so they get their own material copy
        var tex = street ? _people : _mini; var size = street ? _peopleSize : _miniSize;
        var standMat = (ShaderMaterial)_spriteMat.Duplicate();
        standMat.SetShaderParameter("atlas", tex); standMat.SetShaderParameter("atlas_size", size); standMat.SetShaderParameter("frames", street ? _peopleFrames : _miniFrames);
        _stand.Node.Material = standMat;
        var miniMat = (ShaderMaterial)_spriteMat.Duplicate();
        miniMat.SetShaderParameter("atlas", _mini); miniMat.SetShaderParameter("atlas_size", _miniSize); miniMat.SetShaderParameter("frames", _miniFrames);
        _bodies.Node.Material = miniMat; _icons.Node.Material = miniMat; _marks.Node.Material = miniMat;
    }

    /// <summary>Publishes the world's state once per tick: captures it into the spare snapshot (read-only on the world) and makes it the current one; the previous current is what the next frames interpolate from.</summary>
    public void Publish(FightWorld w, Func<Fighter, byte>? roleOf = null)
    {
        var s = BattleSnapshot.Capture(w, _spare, roleOf);
        _spare = _prev; _prev = _cur; _cur = s; _havePrev = _prev.Count > 0 && _prev.Tick != _cur.Tick;
    }

    /// <summary>The snapshot the layer is showing (for tests and the HUD).</summary>
    public BattleSnapshot Current => _cur;

    /// <summary>
    /// Lays the crowd out for one frame. <paramref name="centreM"/> is the camera centre in metres, <paramref name="viewPx"/> the viewport size, <paramref name="alpha"/> how far the clock is from the
    /// previous tick to the current one (0..1).
    /// </summary>
    public void Refresh(Vector2 centreM, Vector2 viewPx, float alpha)
    {
        long t0 = Stopwatch.GetTimestamp();
        var s = _cur; int n = s.Count;
        _bodies.Reset(); _stand.Reset(); _blocks.Reset(); _icons.Reset(); _marks.Reset();
        Drawn = Standing = Fallen = Blocks = 0;
        if (n == 0) { Flush(); return; }
        float ppm = Ppm(_band), tex = TexelWorld(_band);
        float hx = viewPx.X / 2f / ppm + 4f, hy = viewPx.Y / 2f / ppm + 4f;
        float x0 = centreM.X - hx, x1 = centreM.X + hx, y0 = centreM.Y - hy, y1 = centreM.Y + hy;
        if (_band == StreetView.Band.Command) { RefreshCommand(x0, x1, y0, y1, ppm, tex); return; }

        if (_px.Length < n) { _px = new float[n]; _py = new float[n]; _idx = new int[n]; _key = new int[n]; _sorted = new int[n]; }
        var p = _havePrev ? _prev : null;
        int nv = 0, nbody = 0;
        bool street = _band is StreetView.Band.Street or StreetView.Band.Close;
        for (int i = 0; i < n; i++)
        {
            var fl = (SnapFlags)s.Flags[i];
            if ((fl & SnapFlags.Gone) != 0) continue;
            float cx = s.X[i] * 0.001f, cy = s.Y[i] * 0.001f, xm = cx, ym = cy;
            if (p != null && i < p.Count) { xm = p.X[i] * 0.001f + (cx - p.X[i] * 0.001f) * alpha; ym = p.Y[i] * 0.001f + (cy - p.Y[i] * 0.001f) * alpha; }
            if (xm < x0 || xm > x1 || ym < y0 || ym > y1) continue;
            // snapped to a whole texel of the band, so no sprite ever sits between pixels
            float wx = MathF.Round(xm * WorldPerM / tex) * tex, wy = MathF.Round(ym * WorldPerM / tex) * tex;
            _px[i] = wx; _py[i] = wy;
            if ((fl & (SnapFlags.Dead | SnapFlags.Down)) != 0) { _idx[nv++] = i | int.MinValue; nbody++; continue; }
            _idx[nv++] = i;
        }
        Drawn = nv;
        CullMs = Stopwatch.GetElapsedTime(t0).TotalMilliseconds;

        // counting sort of the standing soldiers by pixel row, ties by fighter id (the order they were gathered in)
        long t1 = Stopwatch.GetTimestamp();
        int ns = 0, minK = int.MaxValue, maxK = int.MinValue;
        for (int k = 0; k < nv; k++)
        {
            int i = _idx[k]; if (i < 0) continue;
            int key = (int)MathF.Round(_py[i] / tex); _key[ns] = key; _sorted[ns++] = i;
            if (key < minK) minK = key; if (key > maxK) maxK = key;
        }
        SortStanding(ns, minK, maxK);
        SortMs = Stopwatch.GetElapsedTime(t1).TotalMilliseconds;

        long t2 = Stopwatch.GetTimestamp();
        Ensure(_bodies, Math.Max(1, nbody)); Ensure(_stand, Math.Max(1, ns));
        float bodyTex = street ? 4f : tex; // a lying body is 8x8 texels of the Field art: four world units a texel at Street too
        for (int k = 0; k < nv; k++)
        {
            int e = _idx[k]; if (e >= 0) continue;
            int i = e & int.MaxValue;
            var fl = (SnapFlags)s.Flags[i];
            int flags = (fl & SnapFlags.Dead) != 0 ? 32 : 0;
            if (s.Fx[i] < 0) flags |= 16;
            Put(_bodies, _px[i], _py[i], _fallen, RowOf(s.Team[i]), flags, i, bodyTex);
        }
        for (int k = 0; k < ns; k++)
        {
            int i = _sorted[k];
            var fl = (SnapFlags)s.Flags[i];
            int role = Math.Min(s.Role[i], Roles.Length - 1);
            int fx = s.Fx[i], fy = s.Fy[i];
            int flags = 0;
            if ((fl & SnapFlags.Blood) != 0) flags |= 1;
            if ((fl & SnapFlags.Hurt) != 0) flags |= 2;
            if ((fl & SnapFlags.LegWound) != 0) flags |= 4;
            if ((fl & SnapFlags.Routing) != 0) flags |= 64;
            if (p != null && i < p.Count && Math.Abs(s.X[i] - p.X[i]) + Math.Abs(s.Y[i] - p.Y[i]) > 20) flags |= 8; // walking: moved more than 2 cm in a tick
            Fr fr;
            if (street) { fr = _street[role]; if (fx < 0 && Math.Abs(fy) <= Math.Abs(fx)) flags |= 16; }
            else
            {
                bool side = Math.Abs(fy) <= Math.Abs(fx);
                fr = _field[role, side ? 2 : fy > 0 ? 0 : 1];
                if (side && fx < 0) flags |= 16;
            }
            Put(_stand, _px[i], _py[i], fr, RowOf(s.Team[i]), flags, i, tex);
        }
        FillMs = Stopwatch.GetElapsedTime(t2).TotalMilliseconds;
        Standing = ns; Fallen = nbody;
        Flush();
    }

    /// <summary>Counting sort of <c>_sorted[0..ns)</c> by <c>_key</c> (stable, so equal rows keep id order); falls back to an index sort when the range is huge.</summary>
    void SortStanding(int ns, int minK, int maxK)
    {
        if (ns < 2) return;
        long range = (long)maxK - minK + 1;
        if (range > MaxBuckets)
        {
            // a freak range (a far-flung outlier): sort keys with their fighter, then break ties by id
            Array.Sort(_key, _sorted, 0, ns);
            int a = 0;
            while (a < ns) { int b = a + 1; while (b < ns && _key[b] == _key[a]) b++; if (b - a > 1) Array.Sort(_sorted, a, b - a); a = b; }
            return;
        }
        int r = (int)range;
        if (_count.Length < r + 1) _count = new int[Math.Max(r + 1, _count.Length * 2)];
        Array.Clear(_count, 0, r + 1);
        for (int k = 0; k < ns; k++) _count[_key[k] - minK + 1]++;
        for (int b = 1; b <= r; b++) _count[b] += _count[b - 1];
        var tmp = _scatter.Length >= ns ? _scatter : (_scatter = new int[Math.Max(ns, _scatter.Length * 2)]);
        for (int k = 0; k < ns; k++) tmp[_count[_key[k] - minK]++] = _sorted[k];
        Array.Copy(tmp, _sorted, ns);
    }
    int[] _scatter = new int[1024];

    void Put(Group g, float x, float y, Fr fr, int row, int flags, int id, float tex)
    {
        int o = g.N++ * Stride; var b = g.Buf;
        b[o] = fr.W * tex; b[o + 1] = 0; b[o + 2] = 0; b[o + 3] = x;
        b[o + 4] = 0; b[o + 5] = fr.H * tex; b[o + 6] = 0; b[o + 7] = y;
        b[o + 8] = fr.Index; b[o + 9] = row; b[o + 10] = flags; b[o + 11] = (id * 0.618034f) % 1f;
    }

    // ---------------------------------------------------------------- Command

    void RefreshCommand(float x0, float x1, float y0, float y1, float ppm, float tex)
    {
        long t0 = Stopwatch.GetTimestamp();
        var s = _cur;
        var marks = s.FormCount > 0 ? s.Forms : null; int m = s.FormCount;
        if (marks == null) { marks = ClusterTeams(s); m = _clusterN; }
        int nv = 0;
        _tmpIdx = _tmpIdx.Length >= m ? _tmpIdx : new int[Math.Max(m, 64)];
        _tmpKey = _tmpKey.Length >= m ? _tmpKey : new int[Math.Max(m, 64)];
        for (int k = 0; k < m; k++)
        {
            float cx = marks[k].CentreX * 0.001f, cy = marks[k].CentreY * 0.001f;
            if (cx < x0 - 30 || cx > x1 + 30 || cy < y0 - 30 || cy > y1 + 30) continue;
            _tmpKey[nv] = (int)MathF.Round(cy * ppm); _tmpIdx[nv++] = k;
        }
        Array.Sort(_tmpKey, _tmpIdx, 0, nv);
        Drawn = nv; Blocks = nv;
        CullMs = Stopwatch.GetElapsedTime(t0).TotalMilliseconds;
        long t1 = Stopwatch.GetTimestamp();
        Ensure(_blocks, Math.Max(1, nv)); Ensure(_icons, Math.Max(1, nv)); Ensure(_marks, Math.Max(1, nv));
        for (int q = 0; q < nv; q++)
        {
            var f = marks[_tmpIdx[q]];
            bool vert = Math.Abs(f.FaceY) > Math.Abs(f.FaceX); // facing north or south: the front runs across the screen
            float wpx = Math.Max(8f, (vert ? f.FrontMm : f.DepthMm) * 0.001f * ppm), hpx = Math.Max(8f, (vert ? f.DepthMm : f.FrontMm) * 0.001f * ppm);
            wpx = MathF.Round(wpx); hpx = MathF.Round(hpx);
            float wx = MathF.Round(f.CentreX * 0.001f * ppm) * tex, wy = MathF.Round(f.CentreY * 0.001f * ppm) * tex;
            int row = RowOf(f.Team);
            // the block: a unit quad, foot-anchored, so its centre is half its height up
            int o = _blocks.N++ * Stride; var b = _blocks.Buf;
            b[o] = wpx * tex; b[o + 1] = 0; b[o + 2] = 0; b[o + 3] = wx;
            b[o + 4] = 0; b[o + 5] = hpx * tex; b[o + 6] = 0; b[o + 7] = wy + hpx * tex * 0.5f;
            b[o + 8] = wpx; b[o + 9] = hpx; b[o + 10] = row + (f.Active > 0 && f.Routing * 2 >= f.Active ? 8 : 0); b[o + 11] = f.Total <= 0 ? 0 : Math.Clamp((float)f.Active / f.Total, 0f, 1f);
            var ic = _icon[Math.Min((int)f.Role, Roles.Length - 1)];
            Put(_icons, wx, wy + ic.H * tex * 0.5f, ic, row, 0, q, tex);
            // the shape marker at the block's top-left corner
            var mk = _mark[Math.Min(row, 5)];
            Put(_marks, wx - wpx * tex * 0.5f + tex + mk.W * tex * 0.5f, wy - hpx * tex * 0.5f + (mk.H + 1) * tex, mk, 0, 0, q, tex);
        }
        FillMs = Stopwatch.GetElapsedTime(t1).TotalMilliseconds;
        Standing = 0; Fallen = 0;
        Flush();
    }
    int[] _tmpIdx = new int[64], _tmpKey = new int[64];
    int _clusterN;

    /// <summary>A fight with no formation book (the Phase 4 skirmishes): one block per team and 32 m cell of men on their feet, so Command still shows the line.</summary>
    FormationMark[] ClusterTeams(BattleSnapshot s)
    {
        _cluster.Clear(); _clusterN = 0;
        var sum = new List<(long X, long Y, int N, int Team)>();
        for (int i = 0; i < s.Count; i++)
        {
            if ((s.Flags[i] & (byte)(SnapFlags.Gone | SnapFlags.Dead | SnapFlags.Down)) != 0) continue;
            long key = ((long)s.Team[i] << 40) | ((long)Math.Floor(s.X[i] / 32000.0) & 0xFFFFF) << 20 | ((long)Math.Floor(s.Y[i] / 32000.0) & 0xFFFFF);
            if (!_cluster.TryGetValue(key, out int c)) { c = sum.Count; _cluster[key] = c; sum.Add((0, 0, 0, s.Team[i])); }
            var e = sum[c]; sum[c] = (e.X + s.X[i], e.Y + s.Y[i], e.N + 1, e.Team);
        }
        if (_clusterMarks.Length < sum.Count) _clusterMarks = new FormationMark[sum.Count * 2];
        foreach (var e in sum)
        {
            int side = Math.Max(4000, (int)(Math.Sqrt(e.N) * 1800)); // about 1.8 m a man
            _clusterMarks[_clusterN++] = new FormationMark(-1, e.Team, FormationKind.Infantry, (int)(e.X / e.N), (int)(e.Y / e.N), e.Team == 0 ? 1024 : -1024, 0, side, side, e.N, e.N, 0, 0);
        }
        return _clusterMarks;
    }

    void Flush()
    {
        long t = Stopwatch.GetTimestamp();
        Upload(_bodies); Upload(_stand); Upload(_blocks); Upload(_icons); Upload(_marks);
        UploadMs = Stopwatch.GetElapsedTime(t).TotalMilliseconds;
    }

    static void Upload(Group g)
    {
        if (g.Cap == 0) return;
        RenderingServer.MultimeshSetBuffer(g.Rid, g.Buf);
        RenderingServer.MultimeshSetVisibleInstances(g.Rid, g.N);
    }
}
