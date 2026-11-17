using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Ravensguard.Content;
using Ravensguard.Sim.Combat;
using Ravensguard.Sim.Combat.Melee;

namespace Ravensguard.Game.Spikes;

/// <summary>
/// Phase 0 spike 2: real-time melee feel. A 1v1 duel (you vs AI) and a 10v10 skirmish on the pure-C# model in Ravensguard.Sim.Combat.Melee.
/// Rendering is a thin shell: people atlas sprites, a wind-up bar (red tail = parry window), floating wound callouts, a combat log.
/// Controls: WASD move, LMB hold/release attack, RMB block (tap just before impact = parry; during your wind-up = feint), Ctrl+WASD dodge,
/// T called shot (pauses; arrows + F), K skirmish, R restart, N next opponent, Esc cancel. Not production code.
/// </summary>
public partial class MeleeSpike : Node2D
{
    const double Dt = MeleeRules.Dt;
    static readonly string[] OpponentNames = ["sword + padded", "axe + mail", "spear + tunic"];

    // --- sim ---
    static CombatTables? _tablesCache;
    CombatTables _tables = null!;
    MeleeWorld _w = null!;
    Fighter? _player;
    ulong _seed = 7;
    int _opponent;
    bool _skirmish, _auto, _movie;
    double _acc; int _evCursor;
    bool _releaseAfterStep;

    // --- called shot ---
    bool _calling; int _callPart, _callAtk; string? _calledPart; string? _calledAtk;

    // --- render ---
    Texture2D _atlas = null!;
    readonly Dictionary<string, int> _spriteX = new();
    Font _font = null!;
    Vector2 _cam = Vector2.Zero;
    double _ppm = 72;
    readonly List<Callout> _callouts = new();
    readonly List<string> _log = new();
    readonly Dictionary<int, int> _flashUntil = new();
    Label _hud = null!, _logLabel = null!;
    string _banner = "";

    sealed class Callout { public Vector2 Pos; public string Text = ""; public Color Col; public double Age; public double Life = 1.6; }

    public override void _Ready()
    {
        var args = new List<string>(OS.GetCmdlineUserArgs());
        var all = new List<string>(OS.GetCmdlineArgs());
        _movie = all.Contains("--write-movie") || !string.IsNullOrEmpty(Engine.GetWriteMoviePath()); // movie capture runs the AI-vs-AI demo
        _auto = args.Contains("--auto") || _movie;
        _skirmish = args.Contains("--skirmish");
        foreach (var a in args) if (a.StartsWith("--seed=")) _seed = ulong.Parse(a[7..]);
        foreach (var a in args) if (a.StartsWith("--opponent=")) _opponent = int.Parse(a[11..]) % OpponentNames.Length;

        LoadAssets();
        _tables = _tablesCache ??= LoadTables();

        var cl = new CanvasLayer(); AddChild(cl);
        _hud = new Label { Position = new Vector2(10, 8) };
        _hud.AddThemeColorOverride("font_color", Colors.White);
        _hud.AddThemeColorOverride("font_outline_color", Colors.Black);
        _hud.AddThemeConstantOverride("outline_size", 4);
        _hud.AddThemeFontSizeOverride("font_size", 14);
        cl.AddChild(_hud);
        _logLabel = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(760, 0), GrowVertical = Control.GrowDirection.Begin };
        _logLabel.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.BottomLeft);
        _logLabel.OffsetLeft = 10; _logLabel.OffsetBottom = -10; _logLabel.OffsetRight = 770;
        _logLabel.AddThemeColorOverride("font_color", new Color(0.92f, 0.9f, 0.8f));
        _logLabel.AddThemeColorOverride("font_outline_color", Colors.Black);
        _logLabel.AddThemeConstantOverride("outline_size", 4);
        _logLabel.AddThemeFontSizeOverride("font_size", 13);
        cl.AddChild(_logLabel);

        StartBattle();
        GD.Print($"MeleeSpike ready: {(_skirmish ? "skirmish 10v10" : "duel")} auto={_auto} seed={_seed}");
    }

    void LoadAssets()
    {
        var img = Image.LoadFromFile(ProjectSettings.GlobalizePath("res://assets/spike/people_32x64.png"));
        img.Convert(Image.Format.Rgba8);
        _atlas = ImageTexture.CreateFromImage(img);
        var json = Json.ParseString(Godot.FileAccess.GetFileAsString("res://assets/spike/index.json")).AsGodotDictionary();
        var sprites = json["sprites"].AsGodotDictionary();
        foreach (var k in sprites.Keys)
        {
            var key = k.AsString();
            if (!key.StartsWith("person.")) continue;
            _spriteX[key] = (int)sprites[k].AsGodotDictionary()["x"].AsDouble();
        }
        _font = GD.Load<Font>("res://assets/fonts/PixelifySans.ttf") ?? ThemeDB.FallbackFont;
    }

    static CombatTables LoadTables()
    {
        var root = System.IO.Path.GetFullPath(ProjectSettings.GlobalizePath("res://") + "../../content/base");
        var r = new ModLoader(ContentTypeRegistry.Default).Load(new[] { root });
        if (!r.Success) throw new InvalidOperationException(r.Report.ToString());
        return new CombatTables(r.Database);
    }

    // ------------------------------------------------------------------ battle setup

    void StartBattle()
    {
        _log.Clear(); _callouts.Clear(); _flashUntil.Clear(); _evCursor = 0; _acc = 0; _banner = "";
        _calling = false; _calledPart = null; _calledAtk = null;
        if (_skirmish)
        {
            _w = MeleeScenarios.Skirmish(_tables, _seed, 10, playerInTeam0: !_auto);
            _ppm = 27;
        }
        else
        {
            var m = MeleeScenarios.BenchMatchups[_opponent];
            _w = new MeleeWorld(_tables, _seed);
            var me = new FighterSpec("You", 0, MeleeScenarios.Sword, MeleeScenarios.Padded, new Vec2(-2.2, 0), 55, ShieldCover: _auto ? 0 : MeleeRules.HeaterCover, Sprite: "person.hero")
            { FaceDir = new Vec2(1, 0) };
            if (_auto) me = me with { Name = "Aldric", Sprite = "person.soldier_blue" };
            _w.Add(me, isPlayer: !_auto);
            var foe = m.B(_opponent + (int)_seed);
            _w.Add(foe with { Name = "Bram", Start = new Vec2(_movie ? 0.9 : 3.5, 0), Skill = 45 });
            _ppm = 72;
        }
        _player = _w.Fighters.FirstOrDefault(f => f.IsPlayer);
        _w.EmitText = true;
        Say(_skirmish ? "Skirmish: Wolves (blue) vs Crows (red), 10 a side." : $"Duel vs {OpponentNames[_opponent]}. Seed {_seed}.");
    }

    // ------------------------------------------------------------------ input

    public override void _UnhandledInput(InputEvent ev)
    {
        if (ev is InputEventKey { Pressed: true, Echo: false } k)
        {
            if (_calling) { CallingKey(k); GetViewport().SetInputAsHandled(); return; }
            switch (k.Keycode)
            {
                case Key.R: _seed++; StartBattle(); return;
                case Key.K: _skirmish = !_skirmish; StartBattle(); return;
                case Key.N: _opponent = (_opponent + 1) % OpponentNames.Length; _skirmish = false; StartBattle(); return;
                case Key.T when _player != null: BeginCalling(); return;
            }
            if (_player != null && k.CtrlPressed)
            {
                Vec2? dir = k.Keycode switch { Key.W => new Vec2(0, -1), Key.S => new Vec2(0, 1), Key.A => new Vec2(-1, 0), Key.D => new Vec2(1, 0), _ => null };
                if (dir != null) _player.Cmd.Dodge = dir;
            }
        }
        if (_player == null || _calling) return;
        if (ev is InputEventMouseButton mb)
        {
            if (mb.ButtonIndex == MouseButton.Left)
            {
                if (mb.Pressed)
                {
                    var atk = _calledAtk ?? _player.WeaponStats.Shape.Attacks[0].Id;
                    _player.Cmd.Start = new AttackRequest { PartId = _calledPart ?? "upper_body", AttackId = atk, TargetId = -1, AutoRelease = false };
                    _player.Cmd.Hold = true; _releaseAfterStep = false;
                    _calledPart = null; _calledAtk = null;
                }
                else if (_player.Cmd.Start != null) _releaseAfterStep = true;
                else _player.Cmd.Hold = false;
            }
            else if (mb.ButtonIndex == MouseButton.Right)
            {
                if (mb.Pressed)
                {
                    if (_player.State == Act.Windup && _player.WindupElapsed * 2 < _player.WindupNeeded) _player.Cmd.Feint = true;
                    else _player.Cmd.Block = true;
                }
                else _player.Cmd.Block = false;
            }
        }
    }

    void PollPlayer()
    {
        if (_player == null) return;
        var c = _player.Cmd;
        Vector2 m = Vector2.Zero;
        if (Input.IsKeyPressed(Key.W)) m.Y -= 1;
        if (Input.IsKeyPressed(Key.S)) m.Y += 1;
        if (Input.IsKeyPressed(Key.A)) m.X -= 1;
        if (Input.IsKeyPressed(Key.D)) m.X += 1;
        if (Input.IsKeyPressed(Key.Ctrl)) m = Vector2.Zero; // Ctrl+dir is a dodge, not a walk
        m = m.Normalized();
        c.Move = new Vec2(m.X, m.Y);
        c.Sprint = Input.IsKeyPressed(Key.Shift);
        var mouseWorld = (GetGlobalMousePosition() - ScreenOffset()) / (float)_ppm;
        var d = new Vec2(mouseWorld.X - _player.Pos.X, mouseWorld.Y - _player.Pos.Y).Normalized();
        c.Face = d;
    }

    // ------------------------------------------------------------------ called shot (T)

    string[] CallParts => (_w.Fighters.FirstOrDefault(f => f.Team != _player!.Team) ?? _w.Fighters[0]).PartIds;
    AttackDef[] CallAtks => _player!.WeaponStats.Shape.Attacks.ToArray();

    void BeginCalling() { _calling = true; _callPart = Math.Max(0, Array.IndexOf(CallParts, "upper_body")); _callAtk = 0; }

    void CallingKey(InputEventKey k)
    {
        var parts = CallParts; var atks = CallAtks;
        switch (k.Keycode)
        {
            case Key.Up: _callPart = (_callPart + parts.Length - 1) % parts.Length; break;
            case Key.Down: _callPart = (_callPart + 1) % parts.Length; break;
            case Key.Left: _callAtk = (_callAtk + atks.Length - 1) % atks.Length; break;
            case Key.Right: _callAtk = (_callAtk + 1) % atks.Length; break;
            case Key.F: _calledPart = parts[_callPart]; _calledAtk = atks[_callAtk].Id; _calling = false; Say($"Called shot: {_calledAtk} to {_calledPart}. Next attack."); break;
            case Key.Escape or Key.T: _calling = false; break;
        }
    }

    double CallHitChance()
    {
        var foe = _w.Fighters.First(f => f.Team != _player!.Team);
        var parts = CallParts; var atks = CallAtks;
        var def = new Defender(foe.Plan, foe.Plan.SizeCm3, foe.Spec.Worn, foe.Spec.Skill);
        var input = new StrikeInput(new Attacker(_player!.Spec.Strength, _player.Spec.Skill), _player.Spec.Weapon, atks[_callAtk].Id, parts[_callPart], def);
        return _w.Resolver.HitChance(input);
    }

    // ------------------------------------------------------------------ frame

    public override void _Process(double delta)
    {
        PollPlayer();
        if (!_calling)
        {
            _acc += delta;
            int guard = 0;
            while (_acc >= Dt && guard++ < 6)
            {
                _acc -= Dt;
                foreach (var f in _w.Fighters) if (!f.IsPlayer) MeleeAi.Think(_w, f);
                _w.Step();
                if (_releaseAfterStep && _player != null) { _player.Cmd.Hold = false; _releaseAfterStep = false; }
                Consume();
            }
            if (guard >= 6) _acc = 0;
        }
        foreach (var c in _callouts) c.Age += delta;
        _callouts.RemoveAll(c => c.Age > c.Life);
        UpdateCamera(delta);
        UpdateBanner();
        UpdateHud();
        QueueRedraw();
    }

    void UpdateCamera(double delta)
    {
        Vector2 target;
        if (_skirmish) target = _player != null ? P(_player.Pos) : Centroid();
        else target = (P(_w.Fighters[0].Pos) + P(_w.Fighters[1].Pos)) * 0.5f;
        _cam = _cam.Lerp(target, (float)Math.Min(1.0, delta * 4));
        if (_w.Tick <= 1) _cam = target;
    }

    Vector2 Centroid()
    {
        var act = _w.Fighters.Where(f => f.Active).ToList();
        if (act.Count == 0) return Vector2.Zero;
        return new Vector2((float)act.Average(f => f.Pos.X), (float)act.Average(f => f.Pos.Y));
    }

    static Vector2 P(Vec2 v) => new((float)v.X, (float)v.Y);
    Vector2 ScreenOffset() => GetViewportRect().Size * 0.5f - _cam * (float)_ppm;
    Vector2 S(Vec2 v) => P(v) * (float)_ppm + ScreenOffset();

    void UpdateBanner()
    {
        if (!_w.IsOver) { _banner = ""; return; }
        var live = _w.Fighters.Where(f => f.Active).ToList();
        if (live.Count == 0) { _banner = "Everyone is down. R to restart."; return; }
        int team = live[0].Team;
        _banner = _skirmish ? $"{(team == 0 ? "Wolves" : "Crows")} hold the field ({_w.TimeS:0}s). R to restart, K duel."
                            : $"{live[0].Name} wins in {_w.TimeS:0.0}s -- the other is {OutcomeText(_w.Fighters.First(f => f.Team != team))}. R restarts, N changes foe, K skirmish.";
    }

    static string OutcomeText(Fighter f) => f.State switch
    {
        Act.Dead => "dead", Act.Down => f.Down == DownReason.Pain ? "knocked out" : "down", Act.Fled => "fled", Act.Surrendered => "yielding",
        _ => f.Routing ? "routing" : "standing",
    };

    // ------------------------------------------------------------------ events -> log, callouts

    void Say(string t) { _log.Add(t); if (_log.Count > 9) _log.RemoveAt(0); }

    void Consume()
    {
        for (; _evCursor < _w.Events.Count; _evCursor++)
        {
            var e = _w.Events[_evCursor];
            var tgt = e.Target >= 0 && e.Kind is EventKind.Wound or EventKind.Whiff ? _w.Fighters[e.Target] : _w.Fighters[Math.Max(0, e.Actor)];
            bool logIt = e.Kind != EventKind.Windup && e.Kind != EventKind.Riposte && (!_skirmish || e.Severe || e.Kind is EventKind.Rout or EventKind.Parried);
            if (logIt) Say($"[{e.Tick * Dt:0.0}] {e.Text}");
            var anchor = S(tgt.Pos) + new Vector2(0, -(float)(_ppm * 1.9) - 34);
            switch (e.Kind)
            {
                case EventKind.Wound:
                    var txt = e.Text; int i = txt.IndexOf(": ", StringComparison.Ordinal);
                    var detail = i >= 0 ? txt[(i + 2)..] : txt;
                    var part = e.PartId != null && tgt.Plan.ById.TryGetValue(e.PartId, out var bp) ? bp.Name : "";
                    AddCallout(anchor, Trim($"{part}: {Short(detail)}", 34), e.Severe ? new Color(1, 0.35f, 0.3f) : new Color(1, 0.8f, 0.4f));
                    _flashUntil[tgt.Id] = _w.Tick + 4;
                    break;
                case EventKind.Blocked: AddCallout(anchor, "BLOCK", new Color(0.7f, 0.85f, 1f)); break;
                case EventKind.Parried: AddCallout(anchor, "PARRY!", new Color(0.4f, 1f, 0.5f)); break;
                case EventKind.Dodged: AddCallout(anchor, "DODGE", new Color(0.7f, 1f, 1f)); break;
                case EventKind.Whiff: AddCallout(anchor, "miss", new Color(0.8f, 0.8f, 0.8f)); break;
                case EventKind.Feint: AddCallout(anchor, "feint", new Color(0.9f, 0.9f, 0.5f)); break;
                case EventKind.Down: AddCallout(anchor, "DOWN", new Color(1, 0.5f, 0.2f)); break;
                case EventKind.Death: AddCallout(anchor, "DEAD", new Color(1, 0.2f, 0.2f)); break;
                case EventKind.Rout: AddCallout(anchor, "ROUT!", new Color(1, 1, 0.3f)); break;
                case EventKind.Surrender: AddCallout(anchor, "YIELDS", new Color(1, 1, 1)); break;
            }
        }
    }

    static string Trim(string t, int n) => t.Length <= n ? t : t[..(n - 1)] + "~";

    static string Short(string detail)
    {
        var bits = detail.Split(", ");
        // keep the most telling bits: severed / artery / vein / bone / organ, else the deepest tissue
        var key = bits.Where(b => b.Contains("SEVERED") || b.Contains("heavily") || b.Contains("bleeding") || b.Contains("bone broken") || b.Contains("pierced") || b.Contains("severed")).ToList();
        return key.Count > 0 ? string.Join(", ", key.Take(2)) : bits[^1];
    }

    void AddCallout(Vector2 pos, string text, Color col)
    {
        int n = _callouts.Count(c => c.Age < 0.3 && (c.Pos - pos).Length() < 40);
        _callouts.Add(new Callout { Pos = pos + new Vector2(0, -16 * n), Text = text, Col = col });
    }

    // ------------------------------------------------------------------ HUD

    void UpdateHud()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("WASD move  Shift sprint  |  LMB hold=wind up, release=strike  |  RMB hold=block, tap just before impact=parry, during wind-up=feint");
        sb.AppendLine("Ctrl+WASD dodge  |  T called shot (arrows, F)  |  K duel/skirmish  N next foe  R restart  |  red tail on a foe's bar = parry NOW");
        if (_player != null)
        {
            var p = _player;
            sb.AppendLine($"YOU {p.State}  blood {100 * (1 - p.BloodLossFrac):0}%  stamina {p.Stamina:0}  pain {p.Pain:0}/{p.KnockoutPain:0}  morale {p.Morale:0}  bleed {p.BleedRate:0.0} ml/s  t={_w.TimeS:0.0}s");
            if (_calledPart != null) sb.AppendLine($"Called shot armed: {_calledAtk} to {_calledPart}");
        }
        else sb.AppendLine($"AUTO  t={_w.TimeS:0.0}s  seed {_seed}  active {_w.Fighters.Count(f => f.Active)}/{_w.Fighters.Count}");
        _hud.Text = sb.ToString();
        _logLabel.Text = string.Join("\n", _log);
    }

    // ------------------------------------------------------------------ drawing

    public override void _Draw()
    {
        var sz = GetViewportRect().Size;
        DrawRect(new Rect2(Vector2.Zero, sz), new Color(0.2f, 0.27f, 0.17f));
        // 1 m grid so movement and distances read
        float ppm = (float)_ppm; var off = ScreenOffset();
        var gcol = new Color(0, 0, 0, 0.10f);
        float x0 = off.X % ppm, y0 = off.Y % ppm;
        for (float x = x0; x < sz.X; x += ppm) DrawLine(new Vector2(x, 0), new Vector2(x, sz.Y), gcol);
        for (float y = y0; y < sz.Y; y += ppm) DrawLine(new Vector2(0, y), new Vector2(sz.X, y), gcol);

        foreach (var f in _w.Fighters.OrderBy(f => (f.State is Act.Down or Act.Dead ? -100 : 0) + f.Pos.Y)) DrawFighter(f);
        foreach (var c in _callouts)
        {
            float a = 1f - (float)Math.Max(0, (c.Age - c.Life * 0.6) / (c.Life * 0.4));
            var p = c.Pos + new Vector2(0, -(float)(c.Age * 28));
            var q = p + new Vector2(-130, 0);
            DrawString(_font, q + new Vector2(1, 1), c.Text, HorizontalAlignment.Center, 260, 16, new Color(0, 0, 0, a));
            DrawString(_font, q, c.Text, HorizontalAlignment.Center, 260, 16, new Color(c.Col, a));
        }
        if (_calling) DrawCalling(sz);
        if (_banner.Length > 0)
        {
            DrawRect(new Rect2(0, sz.Y * 0.4f, sz.X, 44), new Color(0, 0, 0, 0.6f));
            DrawString(_font, new Vector2(0, sz.Y * 0.4f + 30), _banner, HorizontalAlignment.Center, sz.X, 20, Colors.White);
        }
    }

    void DrawFighter(Fighter f)
    {
        if (f.State is Act.Fled) return;
        float ppm = (float)_ppm;
        var pos = S(f.Pos);
        float h = ppm * 1.8f, w = h * 0.5f;
        bool flip = f.Face.X < 0;
        bool flat = f.State is Act.Down or Act.Dead;
        var tint = Colors.White;
        if (f.Routing) tint = new Color(1f, 1f, 0.6f);
        if (f.Weakened) tint = tint.Darkened(0.2f);
        if (_flashUntil.TryGetValue(f.Id, out var until) && _w.Tick < until) tint = new Color(1f, 0.45f, 0.45f);
        if (f.State == Act.Dead) tint = tint.Darkened(0.4f);
        if (f.State == Act.Surrendered) tint = new Color(1, 1, 1, 0.6f);

        // shadow
        DrawSetTransform(pos, 0, new Vector2(1, 0.35f));
        DrawCircle(Vector2.Zero, w * 0.55f, new Color(0, 0, 0, 0.3f));
        DrawSetTransform(Vector2.Zero, 0, Vector2.One);

        // pose offset: lean back while winding up, lunge on the strike
        float lean = 0;
        if (f.State == Act.Windup) lean = -0.22f * h * 0.25f * Math.Min(1f, f.WindupElapsed / (float)Math.Max(1, f.WindupNeeded));
        else if (f.State == Act.Strike) lean = 0.35f * h * 0.25f;
        else if (f.State == Act.Dodge) lean = -0.1f * h;
        var fwd = new Vector2((float)f.Face.X, (float)f.Face.Y);
        var body = pos + fwd * lean;
        if (f.State == Act.Dodge) body += new Vector2(0, -6);

        int sx = _spriteX.TryGetValue(f.Spec.Sprite, out var x) ? x : 0;
        var src = new Rect2(sx, 0, 32, 64);
        if (flat)
        {
            DrawSetTransform(pos + new Vector2(0, -h * 0.12f), flip ? -Mathf.Pi / 2 : Mathf.Pi / 2, Vector2.One);
            DrawTextureRectRegion(_atlas, new Rect2(-w / 2, -h, w, h), src, tint);
            DrawSetTransform(Vector2.Zero, 0, Vector2.One);
        }
        else
        {
            var dest = new Rect2(body.X - (flip ? -w / 2 : w / 2) - (flip ? w : 0) * 0 , body.Y - h, flip ? -w : w, h);
            dest = new Rect2(body.X + (flip ? w / 2 : -w / 2), body.Y - h, flip ? -w : w, h);
            DrawTextureRectRegion(_atlas, dest, src, tint);
            DrawWeapon(f, body, h);
            if (f.ShieldCover > 0 && f.State != Act.Dead) DrawShield(f, body, h);
        }
        DrawBars(f, pos, h);
    }

    void DrawWeapon(Fighter f, Vector2 body, float h)
    {
        var fwd = new Vector2((float)f.Face.X, (float)f.Face.Y);
        float side = fwd.X >= 0 ? 1 : -1;
        var hand = body + new Vector2(side * h * 0.16f, -h * 0.42f);
        float len = (float)(f.Reach * _ppm * 0.9f + h * 0.18f);
        // wind-up: blade raised high and back; strike: swung through, pointing forward; recover: dropping back
        float t = 0; float ang;  // radians from "forward" (positive = up)
        switch (f.State)
        {
            case Act.Windup:
                t = Math.Min(1f, f.WindupElapsed / (float)Math.Max(1, f.WindupNeeded));
                ang = Mathf.Lerp(0.3f, 2.3f, t); break;
            case Act.Strike: ang = -0.2f; break;
            case Act.Recover: ang = -0.9f; break;
            case Act.Block: ang = 1.1f; break;
            default: ang = 0.5f; break;
        }
        var dir = new Vector2(side * Mathf.Cos(ang), -Mathf.Sin(ang));
        var tip = hand + dir * len;
        var col = f.State == Act.Strike ? new Color(1, 1, 0.8f) : f.State == Act.Windup ? new Color(1f, 0.85f, 0.6f) : new Color(0.85f, 0.88f, 0.95f, 0.45f);
        DrawLine(hand, tip, new Color(0, 0, 0, 0.7f), 5);
        DrawLine(hand, tip, col, 3);
        if (f.State == Act.Strike) DrawArc(hand, len * 0.9f, side > 0 ? -1.2f : Mathf.Pi - 0.0f, side > 0 ? 0.3f : Mathf.Pi + 1.2f, 10, new Color(1, 1, 1, 0.35f), 4);
    }

    void DrawShield(Fighter f, Vector2 body, float h)
    {
        float side = f.Face.X >= 0 ? 1 : -1;
        bool up = f.State == Act.Block;
        var c = body + new Vector2(side * (up ? h * 0.30f : h * 0.14f), -h * (up ? 0.48f : 0.38f));
        var r = new Rect2(c.X - h * 0.09f, c.Y - h * 0.15f, h * 0.18f, h * 0.30f);
        DrawRect(r, new Color(0.45f, 0.3f, 0.15f)); DrawRect(r, new Color(0.15f, 0.1f, 0.05f), false, 2);
    }

    void DrawBars(Fighter f, Vector2 pos, float h)
    {
        if (f.State is Act.Dead or Act.Surrendered) return;
        bool detail = !_skirmish || f.IsPlayer;
        float bw = detail ? 54 : 30, x = pos.X - bw / 2, y = pos.Y - h - 14;
        // blood (red) always; stamina (green) + pain (violet) in duels
        float blood = (float)Math.Clamp(1 - f.BloodLossFrac, 0, 1);
        DrawRect(new Rect2(x - 1, y - 1, bw + 2, detail ? 18 : 6), new Color(0, 0, 0, 0.6f));
        DrawRect(new Rect2(x, y, bw * blood, 4), new Color(0.8f, 0.1f, 0.1f));
        if (detail)
        {
            DrawRect(new Rect2(x, y + 5, bw * (float)(f.Stamina / 100), 3), new Color(0.35f, 0.8f, 0.3f));
            DrawRect(new Rect2(x, y + 9, bw * (float)Math.Min(1, f.Pain / f.KnockoutPain), 3), new Color(0.7f, 0.35f, 0.9f));
            DrawRect(new Rect2(x, y + 13, bw * (float)(f.Morale / 100), 2), new Color(0.95f, 0.85f, 0.3f));
        }
        // wind-up bar: fills as the blow charges; the red tail is the 0.2 s parry window
        if (f.State == Act.Windup)
        {
            float wy = y - 12, ww = 54;
            float frac = Math.Min(1f, f.WindupElapsed / (float)Math.Max(1, f.WindupNeeded));
            float tail = MeleeRules.ParryWindowTicks / (float)Math.Max(MeleeRules.ParryWindowTicks, f.WindupNeeded);
            float wx = pos.X - ww / 2;
            DrawRect(new Rect2(wx - 1, wy - 1, ww + 2, 9), Colors.Black);
            DrawRect(new Rect2(wx, wy, ww, 7), new Color(0.2f, 0.2f, 0.2f));
            DrawRect(new Rect2(wx + ww * (1 - tail), wy, ww * tail, 7), new Color(0.45f, 0.1f, 0.1f));
            DrawRect(new Rect2(wx, wy, ww * frac, 7), frac >= 1 - tail ? new Color(1f, 0.25f, 0.2f) : new Color(1f, 0.7f, 0.2f));
            if (f.Heavy) DrawString(_font, new Vector2(pos.X - 50, wy - 3), "HEAVY", HorizontalAlignment.Center, 100, 12, Colors.OrangeRed);
        }
        else if (f.State == Act.Strike) DrawString(_font, new Vector2(pos.X - 50, y - 6), "!", HorizontalAlignment.Center, 100, 22, Colors.Yellow);
        else if (f.State == Act.Stagger) DrawString(_font, new Vector2(pos.X - 50, y - 6), "stagger", HorizontalAlignment.Center, 100, 12, Colors.LightGray);
        if (!_skirmish || f.IsPlayer) DrawString(_font, new Vector2(pos.X - 50, y - (f.State == Act.Windup ? 24 : 6)), f.Name, HorizontalAlignment.Center, 100, 13, f.IsPlayer ? Colors.Gold : Colors.White);
    }

    void DrawCalling(Vector2 sz)
    {
        var parts = CallParts; var atks = CallAtks;
        var plan = _w.Fighters[0].Plan;
        float w = 360, rowH = 20, h = 70 + parts.Length * rowH;
        var o = new Vector2(sz.X - w - 20, 40);
        DrawRect(new Rect2(o, new Vector2(w, h)), new Color(0, 0, 0, 0.82f));
        DrawString(_font, o + new Vector2(12, 24), "CALLED SHOT (paused): arrows pick, F strike, Esc cancel", HorizontalAlignment.Left, w - 20, 14, Colors.Gold);
        DrawString(_font, o + new Vector2(12, 46), $"attack: < {atks[_callAtk].Name} >    P(hit) = {100 * CallHitChance():0}%", HorizontalAlignment.Left, w - 20, 15, Colors.White);
        for (int i = 0; i < parts.Length; i++)
        {
            bool sel = i == _callPart;
            if (sel) DrawRect(new Rect2(o + new Vector2(6, 58 + i * rowH), new Vector2(w - 12, rowH)), new Color(0.3f, 0.3f, 0.1f));
            DrawString(_font, o + new Vector2(14, 74 + i * rowH), (sel ? "> " : "  ") + plan.ById[parts[i]].Name, HorizontalAlignment.Left, w - 20, 14, sel ? Colors.Yellow : Colors.LightGray);
        }
    }
}
