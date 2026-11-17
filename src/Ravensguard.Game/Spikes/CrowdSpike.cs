using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using Godot;

namespace Ravensguard.Game.Spikes;

/// <summary>
/// Phase 0 spike 3: 2,000 soldiers as MultiMesh crowds over a 512x320 m tile ground.
/// World unit = 1 px at Street zoom (32 px/m). Not production code.
/// </summary>
public partial class CrowdSpike : Node2D
{
    const int PerArmy = 1000, N = PerArmy * 2;
    const int GroundW = 512, GroundH = 320, Chunk = 32; // tiles (= metres)
    const float Ppm = 32f, TickDt = 0.05f;
    const int Stride = 12; // 8 transform + 4 custom floats

    // --- options ---
    bool _bench; float _phaseSeconds = 2.4f; int _layers = 3; int _fixedPhase = -1;

    // --- sim state (struct-of-arrays, metres) ---
    float[] _px = new float[N], _py = new float[N], _cx = new float[N], _cy = new float[N];
    float[] _homeY = new float[N], _stopX = new float[N], _phase = new float[N];
    byte[] _team = new byte[N], _row = new byte[N], _wound = new byte[N], _frame = new byte[N];
    bool[] _moving = new bool[N];
    int[] _gridHead = new int[(GroundW / 2 + 1) * (GroundH / 2 + 1)], _gridNext = new int[N];
    Random _rng = new(1234);
    float _acc;

    // --- render ---
    MultiMesh[] _mms = null!; Rid[] _mmRids = null!;
    float[] _buf = new float[N * Stride]; float[] _keys = new float[N]; int[] _idx = new int[N];
    Camera2D _cam = null!;
    TileMapLayer _tiles = null!; Node2D _chunkNode = null!; Sprite2D[] _chunkSprites = Array.Empty<Sprite2D>();
    Texture2D[] _chunkField = null!, _chunkCmd = null!;
    Label _hud = null!;

    // --- phases ---
    record Phase(string Name, float Ppm, bool Chunks, int ChunkLod);
    static readonly Phase[] Phases =
    {
        new("Street 32px/m, TileMapLayer", 32f, false, 0),
        new("Field 8px/m, TileMapLayer", 8f, false, 0),
        new("Field 8px/m, chunk images (256px)", 8f, true, 256),
        new("Command 2px/m, TileMapLayer", 2f, false, 0),
        new("Command 2px/m, chunk images (64px)", 2f, true, 64),
    };
    class Stat { public List<double> Frame = new(), Sort = new(), Fill = new(), Upload = new(), Tick = new(); }
    Stat[] _stats = null!; int _phaseIdx; double _phaseT; int _phaseFrames;
    ulong _lastUsec; double _bakeMs, _tileMapMs, _setupMs;
    StringBuilder _report = new();

    public override void _Ready()
    {
        var args = new List<string>(OS.GetCmdlineUserArgs());
        _bench = args.Contains("--bench");
        foreach (var a in args)
        {
            if (a.StartsWith("--seconds=")) _phaseSeconds = float.Parse(a[10..]) / Phases.Length;
            if (a.StartsWith("--layers=")) _layers = int.Parse(a[9..]);
            if (a.StartsWith("--phase=")) _fixedPhase = int.Parse(a[8..]);
        }
        DisplayServer.WindowSetVsyncMode(DisplayServer.VSyncMode.Disabled);
        Engine.MaxFps = 0;

        var sw = Stopwatch.StartNew();
        var peopleImg = LoadImg("res://assets/spike/people_32x64.png");
        var terrImg = LoadImg("res://assets/spike/terrain_32.png");
        var peopleTex = ImageTexture.CreateFromImage(peopleImg);
        BuildGround(terrImg);
        BuildCrowd(peopleTex);
        InitSim();

        _cam = new Camera2D { Position = new Vector2(GroundW / 2f, GroundH / 2f) * Ppm };
        AddChild(_cam);
        var cl = new CanvasLayer(); AddChild(cl);
        _hud = new Label { Position = new Vector2(8, 8) };
        _hud.AddThemeColorOverride("font_color", Colors.White);
        _hud.AddThemeColorOverride("font_outline_color", Colors.Black);
        _hud.AddThemeConstantOverride("outline_size", 6);
        cl.AddChild(_hud);
        _setupMs = sw.Elapsed.TotalMilliseconds;

        _stats = new Stat[Phases.Length];
        for (int i = 0; i < _stats.Length; i++) _stats[i] = new Stat();
        _phaseIdx = Math.Max(_fixedPhase, 0);
        ApplyPhase();
        _lastUsec = Time.GetTicksUsec();
        GD.Print($"[crowd] setup {_setupMs:F0} ms (chunk bake {_bakeMs:F0} ms, TileMapLayer fill {_tileMapMs:F0} ms), soldiers={N}, layers={_layers}");
    }

    static Image LoadImg(string res)
    {
        var img = Image.LoadFromFile(ProjectSettings.GlobalizePath(res));
        img.Convert(Image.Format.Rgba8);
        return img;
    }

    // ---------------------------------------------------------------- ground
    void BuildGround(Image terr)
    {
        // terrain choice: grass 0-5, dirt blobs 6-9
        int Tile(int x, int y)
        {
            uint h = (uint)(x * 73856093 ^ y * 19349663);
            h ^= h >> 13; h *= 1274126177u; h ^= h >> 16;
            uint bh = (uint)((x / 6) * 83492791 ^ (y / 6) * 2654435761u); bh ^= bh >> 15; bh *= 2246822519u; bh ^= bh >> 13;
            bool dirt = (bh % 100) < 18 || Math.Abs(y - GroundH / 2) < 2;
            return dirt ? 6 + (int)(h % 4) : (int)(h % 6);
        }
        var tiles = new byte[GroundW * GroundH];
        for (int y = 0; y < GroundH; y++) for (int x = 0; x < GroundW; x++) tiles[y * GroundW + x] = (byte)Tile(x, y);

        // (a) TileMapLayer
        var sw = Stopwatch.StartNew();
        var ts = new TileSet { TileSize = new Vector2I(32, 32) };
        var src = new TileSetAtlasSource { Texture = ImageTexture.CreateFromImage(terr), TextureRegionSize = new Vector2I(32, 32) };
        for (int i = 0; i < 10; i++) src.CreateTile(new Vector2I(i, 0));
        ts.AddSource(src, 0);
        _tiles = new TileMapLayer { TileSet = ts, TextureFilter = CanvasItem.TextureFilterEnum.Nearest };
        for (int y = 0; y < GroundH; y++) for (int x = 0; x < GroundW; x++)
            _tiles.SetCell(new Vector2I(x, y), 0, new Vector2I(tiles[y * GroundW + x], 0));
        AddChild(_tiles);
        _tileMapMs = sw.Elapsed.TotalMilliseconds;

        // (b) chunk images: 32x32 tiles -> 1024px, baked once, then LODs 256 (Field) and 64 (Command)
        sw.Restart();
        int cw = GroundW / Chunk, ch = GroundH / Chunk;
        _chunkField = new Texture2D[cw * ch]; _chunkCmd = new Texture2D[cw * ch];
        _chunkNode = new Node2D(); AddChild(_chunkNode);
        _chunkSprites = new Sprite2D[cw * ch];
        var big = Image.CreateEmpty(Chunk * 32, Chunk * 32, false, Image.Format.Rgba8);
        for (int cy = 0; cy < ch; cy++) for (int cx = 0; cx < cw; cx++)
        {
            for (int ty = 0; ty < Chunk; ty++) for (int tx = 0; tx < Chunk; tx++)
                big.BlitRect(terr, new Rect2I(tiles[(cy * Chunk + ty) * GroundW + cx * Chunk + tx] * 32, 0, 32, 32), new Vector2I(tx * 32, ty * 32));
            var f = (Image)big.Duplicate(); f.Resize(256, 256, Image.Interpolation.Lanczos);
            var c = (Image)big.Duplicate(); c.Resize(64, 64, Image.Interpolation.Lanczos);
            int ci = cy * cw + cx;
            _chunkField[ci] = ImageTexture.CreateFromImage(f); _chunkCmd[ci] = ImageTexture.CreateFromImage(c);
            _chunkSprites[ci] = new Sprite2D { Centered = false, Position = new Vector2(cx, cy) * Chunk * Ppm, TextureFilter = CanvasItem.TextureFilterEnum.Linear };
            _chunkNode.AddChild(_chunkSprites[ci]);
        }
        _bakeMs = sw.Elapsed.TotalMilliseconds;
    }

    void SetChunkLod(int px)
    {
        float s = Chunk * Ppm / px;
        for (int i = 0; i < _chunkSprites.Length; i++)
        {
            _chunkSprites[i].Texture = px == 256 ? _chunkField[i] : _chunkCmd[i];
            _chunkSprites[i].Scale = new Vector2(s, s);
        }
    }

    // ---------------------------------------------------------------- crowd
    void BuildCrowd(Texture2D atlas)
    {
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        Vector2[] p = { new(-16, -64), new(16, -64), new(16, 0), new(-16, 0) };
        Vector2[] uv = { new(0, 0), new(1, 0), new(1, 1), new(0, 1) };
        foreach (int i in new[] { 0, 1, 2, 0, 2, 3 }) { st.SetUV(uv[i]); st.AddVertex(new Vector3(p[i].X, p[i].Y, 0)); }
        var mesh = st.Commit();

        // ramps: 4 levels x 4 rows (red, blue, gold, green)
        var rimg = Image.CreateEmpty(4, 4, false, Image.Format.Rgba8);
        string[][] ramps = {
            new[] { "#5e1a14", "#8a2a1e", "#b83c2a", "#d9604a" }, new[] { "#14284e", "#1f3f7a", "#2f5ea8", "#5088d0" },
            new[] { "#5a4410", "#8a6c1c", "#c09a2c", "#e8c850" }, new[] { "#16401c", "#245c2c", "#38823e", "#5cae60" } };
        for (int r = 0; r < 4; r++) for (int l = 0; l < 4; l++) rimg.SetPixel(l, r, new Color(ramps[r][l]));
        var mat = new ShaderMaterial { Shader = GD.Load<Shader>("res://Spikes/crowd.gdshader") };
        mat.SetShaderParameter("atlas", atlas);
        mat.SetShaderParameter("ramps", ImageTexture.CreateFromImage(rimg));

        _mms = new MultiMesh[_layers]; _mmRids = new Rid[_layers];
        for (int l = 0; l < _layers; l++)
        {
            var mm = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform2D, UseCustomData = true, Mesh = mesh, InstanceCount = N };
            var mmi = new MultiMeshInstance2D { Multimesh = mm, Material = mat, Visible = l == 0 }; // layers>0 are uploaded but hidden (cost test)
            AddChild(mmi);
            _mms[l] = mm; _mmRids[l] = mm.GetRid();
        }
    }

    // ---------------------------------------------------------------- sim
    void InitSim()
    {
        int[] frames = { 17, 17, 17, 20, 21, 11 }; // soldier_red x3, coif_axeman, archer, spearman_red (32 px cell index)
        for (int i = 0; i < N; i++)
        {
            int team = i < PerArmy ? 0 : 1, k = i % PerArmy, col = k % 20, rowi = k / 20;
            float x = team == 0 ? 206 + col * 2.2f : 306 - col * 2.2f;
            float y = 60 + rowi * 4.0f + (float)_rng.NextDouble();
            _px[i] = _cx[i] = x; _py[i] = _cy[i] = y; _homeY[i] = y;
            _stopX[i] = 256 + (team == 0 ? -1 : 1) * (0.8f + (float)_rng.NextDouble() * 5f);
            _team[i] = (byte)team; _row[i] = (byte)(team == 0 ? (_rng.Next(5) == 0 ? 2 : 0) : (_rng.Next(5) == 0 ? 3 : 1));
            _frame[i] = (byte)frames[_rng.Next(frames.Length)]; _phase[i] = (float)_rng.NextDouble();
        }
        // y-bands overlap between armies, so they engage along the line
    }

    void Tick()
    {
        const float speed = 3.0f; // brisk advance so 10 s of benchmark shows movement
        Array.Copy(_cx, _px, N); Array.Copy(_cy, _py, N);
        // spatial hash, 2 m cells
        int gw = GroundW / 2 + 1, gh = GroundH / 2 + 1;
        Array.Fill(_gridHead, -1);
        for (int i = 0; i < N; i++)
        {
            int gx = Math.Clamp((int)(_cx[i] * 0.5f), 0, gw - 1), gy = Math.Clamp((int)(_cy[i] * 0.5f), 0, gh - 1);
            int g = gy * gw + gx; _gridNext[i] = _gridHead[g]; _gridHead[g] = i;
        }
        for (int i = 0; i < N; i++)
        {
            float x = _cx[i], y = _cy[i];
            float dx = _stopX[i] - x;
            float vx = 0, vy = 0;
            bool engaged = MathF.Abs(dx) < 0.6f;
            if (!engaged) vx = MathF.Sign(dx) * speed;
            vy += (_homeY[i] - y) * 0.15f;                                  // hold lane
            vx += ((float)_rng.NextDouble() - 0.5f) * (engaged ? 2.5f : 0.8f); // jitter / milling
            vy += ((float)_rng.NextDouble() - 0.5f) * (engaged ? 2.5f : 0.8f);
            // separation
            int gx = Math.Clamp((int)(x * 0.5f), 0, gw - 1), gy = Math.Clamp((int)(y * 0.5f), 0, gh - 1);
            for (int oy = Math.Max(gy - 1, 0); oy <= Math.Min(gy + 1, gh - 1); oy++)
                for (int ox = Math.Max(gx - 1, 0); ox <= Math.Min(gx + 1, gw - 1); ox++)
                    for (int j = _gridHead[oy * gw + ox]; j >= 0; j = _gridNext[j])
                    {
                        if (j == i) continue;
                        float ddx = x - _cx[j], ddy = y - _cy[j], d2 = ddx * ddx + ddy * ddy;
                        if (d2 < 0.81f && d2 > 1e-6f) { float inv = 1f / MathF.Sqrt(d2); vx += ddx * inv * 2f; vy += ddy * inv * 2f; }
                    }
            _cx[i] = Math.Clamp(x + vx * TickDt, 1, GroundW - 1); _cy[i] = Math.Clamp(y + vy * TickDt, 1, GroundH - 1);
            _moving[i] = !engaged || (vx * vx + vy * vy) > 4f;
            if (engaged && _rng.Next(400) == 0) _wound[i] = (byte)(_wound[i] | (1 << _rng.Next(3))); // wounds accrue in the melee
        }
    }

    // ---------------------------------------------------------------- frame
    public override void _Process(double delta)
    {
        ulong now = Time.GetTicksUsec();
        double frameMs = (now - _lastUsec) / 1000.0; _lastUsec = now;
        var stat = _stats[_phaseIdx];
        bool warm = _phaseFrames < 5;

        // sim at 20 Hz
        _acc += (float)Math.Min(delta, 0.2);
        int ticks = 0;
        while (_acc >= TickDt && ticks < 4)
        {
            long t0 = Stopwatch.GetTimestamp(); Tick();
            if (!warm) stat.Tick.Add(Stopwatch.GetElapsedTime(t0).TotalMilliseconds);
            _acc -= TickDt; ticks++;
        }
        if (ticks == 4) _acc = 0;
        float alpha = _acc / TickDt;

        // interpolate + y-sort + fill + upload
        long a = Stopwatch.GetTimestamp();
        for (int i = 0; i < N; i++) { _keys[i] = _py[i] + (_cy[i] - _py[i]) * alpha; _idx[i] = i; }
        Array.Sort(_keys, _idx);
        long b = Stopwatch.GetTimestamp();
        var buf = _buf;
        for (int k = 0; k < N; k++)
        {
            int i = _idx[k], o = k * Stride;
            float x = (_px[i] + (_cx[i] - _px[i]) * alpha) * Ppm, y = _keys[k] * Ppm;
            buf[o] = 1; buf[o + 1] = 0; buf[o + 2] = 0; buf[o + 3] = x;
            buf[o + 4] = 0; buf[o + 5] = 1; buf[o + 6] = 0; buf[o + 7] = y;
            buf[o + 8] = _frame[i]; buf[o + 9] = _row[i]; buf[o + 10] = _wound[i] + (_moving[i] ? 8 : 0); buf[o + 11] = _phase[i];
        }
        long c = Stopwatch.GetTimestamp();
        for (int l = 0; l < _layers; l++) _mms[l].Buffer = buf;
        long d = Stopwatch.GetTimestamp();

        if (!warm)
        {
            stat.Frame.Add(frameMs);
            stat.Sort.Add(Stopwatch.GetElapsedTime(a, b).TotalMilliseconds);
            stat.Fill.Add(Stopwatch.GetElapsedTime(b, c).TotalMilliseconds);
            stat.Upload.Add(Stopwatch.GetElapsedTime(c, d).TotalMilliseconds);
        }
        _phaseFrames++; _phaseT += delta;
        _hud.Text = $"{Phases[_phaseIdx].Name}\nframe {frameMs:F1} ms   soldiers {N}   layers {_layers}";

        if (_bench && _fixedPhase < 0 && _phaseT >= _phaseSeconds)
        {
            _phaseIdx++;
            if (_phaseIdx >= Phases.Length) { Finish(); return; }
            ApplyPhase();
        }
        else if (_bench && _fixedPhase >= 0 && _phaseT >= _phaseSeconds * Phases.Length) Finish();
    }

    void ApplyPhase()
    {
        var p = Phases[_phaseIdx];
        _cam.Zoom = Vector2.One * (p.Ppm / Ppm);
        _tiles.Visible = !p.Chunks; _chunkNode.Visible = p.Chunks;
        if (p.Chunks) SetChunkLod(p.ChunkLod);
        _phaseT = 0; _phaseFrames = 0;
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (e is InputEventKey { Pressed: true } k && k.Keycode >= Key.Key1 && k.Keycode <= Key.Key5) { _phaseIdx = (int)k.Keycode - (int)Key.Key1; ApplyPhase(); }
    }

    static double Pct(List<double> v, double p) { if (v.Count == 0) return double.NaN; var s = v.ToArray(); Array.Sort(s); return s[Math.Min(s.Length - 1, (int)Math.Ceiling(p * s.Length) - 1)]; }
    static double Avg(List<double> v) { if (v.Count == 0) return double.NaN; double t = 0; foreach (var x in v) t += x; return t / v.Count; }

    void Finish()
    {
        var all = new Stat();
        var sb = _report;
        sb.AppendLine($"| Phase | frames | frame avg | frame p99 | sort | fill | upload ({_layers} layers) |");
        sb.AppendLine("|---|---|---|---|---|---|---|");
        for (int i = 0; i < Phases.Length; i++)
        {
            var s = _stats[i];
            if (s.Frame.Count == 0) continue;
            sb.AppendLine($"| {Phases[i].Name} | {s.Frame.Count} | {Avg(s.Frame):F1} ms | {Pct(s.Frame, .99):F1} ms | {Avg(s.Sort):F3} ms | {Avg(s.Fill):F3} ms | {Avg(s.Upload):F3} ms |");
            all.Frame.AddRange(s.Frame); all.Sort.AddRange(s.Sort); all.Fill.AddRange(s.Fill); all.Upload.AddRange(s.Upload); all.Tick.AddRange(s.Tick);
        }
        sb.AppendLine($"| ALL | {all.Frame.Count} | {Avg(all.Frame):F1} ms | {Pct(all.Frame, .99):F1} ms | {Avg(all.Sort):F3} ms | {Avg(all.Fill):F3} ms | {Avg(all.Upload):F3} ms |");
        sb.AppendLine();
        sb.AppendLine($"sim tick (n={all.Tick.Count}): avg {Avg(all.Tick):F3} ms, p99 {Pct(all.Tick, .99):F3} ms, max {Pct(all.Tick, 1):F3} ms");
        sb.AppendLine($"sort p99 {Pct(all.Sort, .99):F3} ms; fill p99 {Pct(all.Fill, .99):F3} ms; upload p99 {Pct(all.Upload, .99):F3} ms");
        sb.AppendLine($"setup {_setupMs:F0} ms: chunk-image bake {_bakeMs:F0} ms (160 chunks x 3 LODs), TileMapLayer fill (163,840 cells) {_tileMapMs:F0} ms");
        sb.AppendLine($"renderer: {RenderingServer.GetRenderingDevice()?.GetDeviceName() ?? RenderingServer.GetVideoAdapterName()} / {RenderingServer.GetVideoAdapterType()}");
        GD.Print("[crowd] SUMMARY\n" + sb);
        GetTree().Quit();
    }
}
