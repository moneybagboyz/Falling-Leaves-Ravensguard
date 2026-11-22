using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text;
using Godot;
using Ravensguard.Sim.Battle;
using Ravensguard.Sim.Combat;
using Ravensguard.Sim.Combat.Melee2;
using Ravensguard.Sim.Combat.Watch;

namespace Ravensguard.Game.World.Combat;

/// <summary>The 2,000-soldier fight of the frame run and the camera check: two armies in formations of 100, on the shipped resolver and brains (the same set-up as the BB-B budget test).</summary>
public static class BattleScenario
{
    static readonly WeaponInstance Spear = new("spear", "steel"), Sword = new("arming_sword", "steel");

    /// <summary>With <paramref name="hero"/> the first man of formation 1 of side 0 is the player's fighter and the book's hero (Leadership 50), and side 0's captains wait for his orders (P7-BB-D).</summary>
    public static FightWorld Make(int perSide, int rowSize = 100, ulong seed = 50, bool hero = false)
    {
        var w = new FightWorld(FightDemo.Content(), seed, 1, 8 * 3600) { RecordEvents = false };
        var book = w.EnsureFormations();
        for (int team = 0; team < 2; team++)
        {
            int rows = Math.Max(1, perSide / rowSize);
            for (int k = 0; k < rows; k++)
            {
                var ids = new List<int>();
                for (int i = 0; i < rowSize; i++)
                {
                    int cls = i % 5 == 4 ? 3 : i % 3 == 0 ? 0 : i % 3 == 1 ? 1 : 2, skill = 30 + i * 7 % 40;
                    int face = team == 0 ? FightMath.One : -FightMath.One;
                    var kit = cls switch
                    {
                        0 => new FighterKit { Name = $"s{i}", Team = team, Weapon = Sword, Shield = "heater", Worn = [new WornItem("tunic", "linen")], Skill = skill, ShieldSkill = skill, FaceX = face },
                        1 => new FighterKit { Name = $"p{i}", Team = team, Weapon = Spear, Worn = [new WornItem("tunic", "linen")], Skill = skill, FaceX = face },
                        2 => new FighterKit { Name = $"w{i}", Team = team, Weapon = Sword, Worn = [new WornItem("tunic", "linen")], Skill = skill, FaceX = face },
                        _ => new FighterKit { Name = $"a{i}", Team = team, Launcher = "longbow", Ammo = "bodkin_arrow", AmmoCount = 12, Worn = [new WornItem("tunic", "linen")], Skill = skill, ArcherySkill = skill, FaceX = face },
                    };
                    ids.Add(w.Add(kit with { Tactics = i == 0 ? 30 : 0, Courage = 100 }).Id);
                }
                int ax = team == 0 ? -10_000 : 10_000;
                var r = book.Form(w, team, (byte)(k + 1), FormationKind.Infantry, ids, ax, (k - rows / 2) * 34_000, team == 0 ? 1024 : -1024, 0, Formation.Line, 4, place: true);
                r.CaptainAlways = true;
            }
        }
        if (hero)
        {
            var r1 = book.Find(0, 1)!;
            int h = r1.Members[0];
            w.Fighters[h].Player = true;
            book.SetHero(w, h, 50);
            foreach (var r in book.Rows) if (r.Side == 0) r.CaptainAlways = false;
        }
        w.LodCentre = (-10_000, 0);
        return w;
    }
}

/// <summary>
/// The frame run (P7-BB-E) and the camera-independence check (BB-3). <c>-- --crowd-bench [--seconds=N] [--soldiers=N] [--phase=street|close|field|command] [--shot-out=dir]</c> steps a real fight at 20 Hz,
/// publishes a snapshot per tick into <see cref="CrowdLayer"/> and walks the camera through the zoom bands, printing the frame and layer costs. Run it under a display (Xvfb with the Compatibility
/// renderer works) with vsync off; headless has no renderer and so no frame time.
/// </summary>
public partial class CrowdBench : Node2D
{
    const float TickDt = 0.05f;
    sealed class Stat { public readonly List<double> Frame = new(), Cull = new(), Sort = new(), Fill = new(), Upload = new(), Sim = new(), Capture = new(); public int Drawn, Standing; }

    FightWorld _w = null!;
    CrowdLayer _crowd = null!;
    Camera2D _cam = null!;
    Label _hud = null!;
    float _acc, _phaseSeconds = 4f;
    int _soldiers = 2000;
    string? _shotOut;
    bool _frozen;
    (string Name, StreetView.Band Band, float Zoom, Vector2 CentreM, float Stress)[] _phases = null!;
    Stat[] _stats = null!;
    int _phase, _frames; double _phaseT; ulong _last;
    readonly StringBuilder _report = new();

    public override void _Ready()
    {
        foreach (var a in OS.GetCmdlineUserArgs())
        {
            if (a.StartsWith("--seconds=")) _phaseSeconds = float.Parse(a[10..], CultureInfo.InvariantCulture);
            else if (a.StartsWith("--soldiers=")) _soldiers = int.Parse(a[11..], CultureInfo.InvariantCulture);
            else if (a.StartsWith("--shot-out=")) _shotOut = a[11..];
            else if (a == "--freeze") _frozen = true; // step 300 ticks first (the lines are in contact), then stop the sim: the frames are the render alone
        }
        string only = OS.GetCmdlineUserArgs().Select(a => a.StartsWith("--phase=") ? a[8..] : "").FirstOrDefault(s => s.Length > 0) ?? "";
        DisplayServer.WindowSetVsyncMode(DisplayServer.VSyncMode.Disabled);
        Engine.MaxFps = 0;
        _w = BattleScenario.Make(_soldiers / 2);
        for (int i = 0; i < (_frozen ? 300 : 20); i++) _w.StepWithAi();
        var bg = new ColorRect { Color = new Color("447030"), Position = new Vector2(-700, -700) * CrowdLayer.WorldPerM, Size = new Vector2(1400, 1400) * CrowdLayer.WorldPerM, ZIndex = -10 };
        AddChild(bg);
        _crowd = new CrowdLayer(ArtAtlas.Shared);
        AddChild(_crowd);
        _cam = new Camera2D(); AddChild(_cam);
        var cl = new CanvasLayer(); AddChild(cl);
        _hud = new Label { Position = new Vector2(8, 8) };
        _hud.AddThemeColorOverride("font_color", Colors.White); _hud.AddThemeColorOverride("font_outline_color", Colors.Black); _hud.AddThemeConstantOverride("outline_size", 6);
        cl.AddChild(_hud);
        // contact is at x = 0 from about tick 200; Street and Close look at the line, Field and Command at the whole field
        // the "CPU stress" phases hand the layer a view 5x wider and taller than the window, so every soldier is culled in, sorted, filled and uploaded (the GPU still draws only the window)
        var all = new (string, StreetView.Band, float, Vector2, float)[]
        {
            ("Close (64 px/m) at the line", StreetView.Band.Close, 2f, new Vector2(0, 0), 1f),
            ("Street (32 px/m) at the line", StreetView.Band.Street, 1f, new Vector2(0, 0), 1f),
            ("Field (8 px/m)", StreetView.Band.Field, 0.25f, new Vector2(0, 0), 1f),
            ("Command (2 px/m)", StreetView.Band.Command, 1f / 16f, new Vector2(0, 0), 1f),
            ("Street CPU stress, all drawn", StreetView.Band.Street, 1f, new Vector2(0, 0), 40f),
            ("Field CPU stress, all drawn", StreetView.Band.Field, 0.25f, new Vector2(0, 0), 5f),
        };
        _phases = only.Length == 0 ? all.Take(4).ToArray() : all.Where(p => p.Item1.Contains(only, StringComparison.OrdinalIgnoreCase)).ToArray();
        _stats = _phases.Select(_ => new Stat()).ToArray();
        _crowd.Publish(_w);
        Apply();
        _last = Time.GetTicksUsec();
        GD.Print($"[crowd-bench] {_soldiers} soldiers, {_phases.Length} phases of {_phaseSeconds} s, window {GetViewport().GetVisibleRect().Size}");
    }

    void Apply()
    {
        var p = _phases[_phase];
        _crowd.Band = p.Band; _cam.Zoom = Vector2.One * p.Zoom; _cam.Position = p.CentreM * CrowdLayer.WorldPerM;
        _phaseT = 0; _frames = 0;
    }

    public override void _Process(double delta)
    {
        ulong now = Time.GetTicksUsec(); double frameMs = (now - _last) / 1000.0; _last = now;
        var st = _stats[_phase]; bool warm = _frames < 5;
        _acc += (float)Math.Min(delta, 0.2);
        int ticks = 0;
        while (_acc >= TickDt && ticks < 3 && !_frozen)
        {
            long a = Stopwatch.GetTimestamp(); _w.StepWithAi(); double simMs = Stopwatch.GetElapsedTime(a).TotalMilliseconds;
            long b = Stopwatch.GetTimestamp(); _crowd.Publish(_w); double capMs = Stopwatch.GetElapsedTime(b).TotalMilliseconds;
            if (!warm) { st.Sim.Add(simMs); st.Capture.Add(capMs); }
            _acc -= TickDt; ticks++;
        }
        if (ticks == 3 || _frozen) _acc = 0.025f;
        var view = GetViewport().GetVisibleRect().Size;
        _crowd.Refresh(_cam.Position / CrowdLayer.WorldPerM, view * _phases[_phase].Stress, _acc / TickDt);
        if (!warm)
        {
            st.Frame.Add(frameMs); st.Cull.Add(_crowd.CullMs); st.Sort.Add(_crowd.SortMs); st.Fill.Add(_crowd.FillMs); st.Upload.Add(_crowd.UploadMs);
            st.Drawn = Math.Max(st.Drawn, _crowd.Drawn); st.Standing = Math.Max(st.Standing, _crowd.Standing);
        }
        _frames++; _phaseT += delta;
        if (_frames % 15 == 0) _hud.Text = $"{_phases[_phase].Name}  frame {frameMs:F1} ms  drawn {_crowd.Drawn}  tick {_w.Tick}";
        if (_phaseT >= _phaseSeconds)
        {
            if (_shotOut != null) Shot(_phases[_phase].Band.ToString());
            _phase++;
            if (_phase >= _phases.Length) { Finish(); return; }
            Apply();
        }
    }

    void Shot(string name)
    {
        System.IO.Directory.CreateDirectory(_shotOut!);
        GetViewport().GetTexture().GetImage().SavePng(System.IO.Path.Combine(_shotOut!, $"crowd_{name}.png"));
    }

    static double Avg(List<double> v) => v.Count == 0 ? double.NaN : v.Average();
    static double Pct(List<double> v, double p) { if (v.Count == 0) return double.NaN; var s = v.OrderBy(x => x).ToArray(); return s[Math.Min(s.Length - 1, (int)Math.Ceiling(p * s.Length) - 1)]; }

    void Finish()
    {
        var sb = _report;
        sb.AppendLine("| Phase | frames | frame avg | frame p99 | cull | sort | fill | upload | drawn | standing | sim tick avg | capture avg |");
        sb.AppendLine("|---|---|---|---|---|---|---|---|---|---|---|---|");
        for (int i = 0; i < _phases.Length; i++)
        {
            var s = _stats[i];
            sb.AppendLine($"| {_phases[i].Name} | {s.Frame.Count} | {Avg(s.Frame):F1} ms | {Pct(s.Frame, .99):F1} ms | {Avg(s.Cull):F3} | {Avg(s.Sort):F3} | {Avg(s.Fill):F3} | {Avg(s.Upload):F3} | {s.Drawn} | {s.Standing} | {Avg(s.Sim):F2} ms | {Avg(s.Capture):F3} ms |");
        }
        sb.AppendLine($"renderer: {RenderingServer.GetVideoAdapterName()} / {RenderingServer.GetVideoAdapterType()}; viewport {GetViewport().GetVisibleRect().Size}; fight tick {_w.Tick}");
        GD.Print("[crowd-bench] SUMMARY\n" + sb);
        GetTree().Quit();
    }
}

/// <summary>Headless checks of the crowd layer (part of <c>--selftest</c>): BB-3 camera independence and BB-18 art coverage on the real atlas.</summary>
public static class CrowdSelfTest
{
    public static void Run(Action<bool, string> check)
    {
        try { Core(check); }
        catch (Exception ex) { check(false, "crowd selftest threw: " + ex); }
    }

    static void Core(Action<bool, string> check)
    {
        var art = ArtAtlas.Shared;
        // BB-18 on the atlas the game loads: every troop role has three Field views and a Command icon, with the sizes the validator promised
        string[] roles = { "levy", "spearman", "archer", "crossbowman", "sergeant", "man_at_arms", "knight", "engineer", "sailor", "marine" };
        foreach (var r in roles)
        {
            foreach (var v in new[] { "front", "back", "side" })
            {
                bool ok = art.Has($"mini.troop.{r}.{v}") && art.Get($"mini.troop.{r}.{v}") is { W: 8, H: 16 };
                check(ok, $"mini.troop.{r}.{v} missing or not 8x16");
            }
            check(art.Has($"mini.icon.{r}") && art.Get($"mini.icon.{r}") is { W: 8, H: 8 }, $"mini.icon.{r} missing or not 8x8");
        }
        check(art.Has("mini.ramps") && art.Has("mini.troop.fallen"), "mini.ramps / mini.troop.fallen missing");

        // BB-3: one fight stepped twice with the crowd layer drawing it, the camera parked at opposite corners (and in every band); the fight hash must not move
        ulong Run(Vector2 corner, StreetView.Band band)
        {
            var w = BattleScenario.Make(300, 50, 77);
            var crowd = new CrowdLayer(art) { Band = band };
            var view = new Vector2(1280, 720);
            for (int t = 0; t < 120; t++)
            {
                w.StepWithAi();
                crowd.Publish(w);
                crowd.Refresh(corner, view, (t % 4) / 4f);
            }
            ulong h = w.Hash();
            crowd.Free();
            return h;
        }
        ulong a = Run(new Vector2(-300, -300), StreetView.Band.Field), b = Run(new Vector2(300, 300), StreetView.Band.Command), c = Run(new Vector2(0, 0), StreetView.Band.Street);
        check(a == b && b == c, $"BB-3: the fight hash depends on the camera ({a:X16} {b:X16} {c:X16})");
    }
}
