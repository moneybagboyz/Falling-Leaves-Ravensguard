using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Ravensguard.Content;
using Ravensguard.Game.Inputs;
using Ravensguard.Game.UI;
using Ravensguard.Sim.Combat;
using Ravensguard.Sim.Combat.Flavour;
using Ravensguard.Sim.Combat.Melee2;
using Ravensguard.Sim.Combat.Resolver2;
using Ravensguard.Sim.Combat.Wounds;

namespace Ravensguard.Game.World.Combat;

/// <summary>
/// The combat stance (plan 2.8, melee-and-ranged 7): a thin view over a <see cref="FightWorld"/>. Fighters are the baked three-view rig (idle, walk, wind-up, strike, recover,
/// block, parry, hit-react, stagger, fall, crawl) with wound overlays from their wound books (blood, bandage, splint, a lost limb drawn from the limb-split planes with a stump);
/// readable wind-ups (an orange bar whose last 0.2 s is the red parry tail), stamina with the 25 mark, the 60 degree attack cone and 100 degree guard arc on the ground, an aim
/// fan and a reload ring. The mouse is the weapon arm: LMB hold winds up and release strikes; RMB blocks (a tap just before impact parries, during one's own wind-up feints);
/// Ctrl + WASD dodges; T pauses for the called-shot paper doll; 5 reloads; 6 cycles the attack type; G throws. Every input goes through <see cref="Send"/>, which the street view
/// records (replay). No game state lives here.
/// </summary>
public partial class FightView : Node2D
{
    public FightWorld W { get; private set; }
    public int Hero { get; }
    /// <summary>Sends one player input: recorded and applied (the street view's session recorder), or applied straight to the fight (the demo).</summary>
    public Action<FightInput> Send { get; }
    /// <summary>True when this view steps the fight itself (the demo and the screenshots); false when the live world steps it on its 20 Hz tick.</summary>
    public bool OwnsStepping { get; init; }
    /// <summary>Pauses or resumes the fight (the called-shot menu pauses).</summary>
    public Action<bool>? SetPaused { get; init; }
    public float Zoom { get; set; } = 3f;
    public string Banner { get; set; } = "";

    readonly RigAtlas _rig;
    Font _font = null!;
    double _acc;
    bool _calling; int _callSel, _callAtk = -1;
    int _attack = -1;                           // -1 = Auto, else the weapon's attack index (6 cycles)
    int _lastMoveDir = 64, _lastFaceDir = -1; bool _lastSprint;
    int _evCursor;
    readonly List<(string Text, Color Col, int Tick)> _log = new();
    readonly Dictionary<int, int> _hitAt = new();
    readonly List<(Vector2 Pos, string Text, Color Col, double Age)> _callouts = new();
    Vector2 _cam;

    // ---- P4-D feedback and flavour (feedback-flavour-log): a recorder writes the log, the feedback draws the effects, the panel shows the lines ----
    public FlavourRecorder Recorder { get; private set; }
    public FlavourBook Book { get; private set; }
    public FightFeedback Feedback { get; private set; }
    public FightLogPanel LogPanel { get; private set; } = null!;
    public AfterFightPanel? Report { get; private set; }
    public FightLog Log => Recorder.Log;
    /// <summary>A report or the log panel has the keyboard and the mouse: the combat inputs wait.</summary>
    public bool ModalOpen => Report != null && IsInstanceValid(Report);
    bool _ended; int _endedTick = -1;
    static (ContentDatabase Db, FlavourBook Book, IReadOnlyList<AudioEventDef> Audio)? _flavourCache;

    public FightView(FightWorld w, int hero, Action<FightInput> send, RigAtlas? rig = null)
    {
        W = w; Hero = hero; Send = send; _rig = rig ?? RigAtlas.Shared;
        Book = null!; Recorder = null!; Feedback = null!;
        StartFlavour(null);
    }

    /// <summary>Starts recording the fight (a new log, or the one restored from a save to carry on) and its feedback.</summary>
    void StartFlavour(FightLog? log)
    {
        var db = W.Content.Db;
        if (_flavourCache is not { } c || !ReferenceEquals(c.Db, db)) _flavourCache = c = (db, new FlavourBook(db), db.All<AudioEventDef>().OrderBy(a => a.Id, StringComparer.Ordinal).ToList());
        Book = c.Book;
        Recorder = FlavourRecorder.Attach(W, log, Book);
        if (Hero >= 0 && Hero < W.Fighters.Count) { Recorder.Log.Hero = Hero; Recorder.Log.HeroTeam = W.Fighters[Hero].Team; }
        Feedback = new FightFeedback(Recorder.Log, W, c.Audio);
        _ended = false; _endedTick = -1;
    }

    /// <summary>The fight is over: the log is closed (the report's duration and result) once.</summary>
    public void FinishLog() { if (_ended) return; _ended = true; Recorder.Finish(); }

    /// <summary>Opens the after-fight report over the view (Shift+F7); the report is rebuilt from the log each time.</summary>
    public void OpenReport()
    {
        if (ModalOpen) return;
        var rep = AfterFightReport.Build(Log, Book, GameText.Instance);
        var layer = new CanvasLayer { Layer = 20, Name = "ReportLayer" };
        var panel = new AfterFightPanel(rep, W.Ground.Count, W.SpentAmmo.Values.Sum() / 2);
        panel.Closed = () => { layer.QueueFree(); Report = null; };
        layer.AddChild(panel); AddChild(layer); Report = panel;
    }

    public Fighter? HeroF => Hero >= 0 && Hero < W.Fighters.Count ? W.Fighters[Hero] : null;
    public bool Calling => _calling;

    public override void _Ready()
    {
        _font = GD.Load<Font>("res://assets/fonts/PixelifySans.ttf") ?? ThemeDB.FallbackFont;
        ActiveContexts.Enter(InputContext.CombatStance);
        _cam = Centre();
        var layer = new CanvasLayer { Layer = 8, Name = "LogLayer" };
        LogPanel = new FightLogPanel(() => Recorder.Log, Book);
        layer.AddChild(LogPanel); AddChild(layer);
        LogPanel.Layout();
    }

    public override void _ExitTree() { ActiveContexts.Exit(InputContext.CombatStance); }

    // ------------------------------------------------------------------ the frame

    public override void _Process(double delta)
    {
        PollHero();
        if (OwnsStepping && !_calling)
        {
            _acc += delta * Feedback.TimeScale;                       // slow motion and hit-stop change the steps per real second, never a step (FL-9)
            int guard = 0;
            while (_acc >= 0.05 && guard++ < 6) { _acc -= 0.05; W.StepWithAi(); }
            if (guard >= 6) _acc = 0;
        }
        Consume();
        Feedback.Poll(); Feedback.Update(delta);
        CheckEnd();
        for (int i = _callouts.Count - 1; i >= 0; i--) { var c = _callouts[i]; c.Age += delta; if (c.Age > 1.4) _callouts.RemoveAt(i); else _callouts[i] = c; }
        _cam = _cam.Lerp(Centre(), (float)Math.Min(1, delta * 4));
        QueueRedraw();
    }

    /// <summary>Steps the fight <paramref name="ticks"/> times without drawing (screenshots, tests of the scene).</summary>
    public void Advance(int ticks) { for (int i = 0; i < ticks; i++) W.StepWithAi(); Consume(); Feedback.Poll(); _cam = Centre(); }

    /// <summary>When a fight the view steps itself is over for two seconds, the log is closed and the report offered (F3: opened for a wound on the hero or three casualties, else a banner).</summary>
    void CheckEnd()
    {
        if (!OwnsStepping || _ended || !W.IsOver) return;
        if (_endedTick < 0) { _endedTick = W.Tick; return; }
        if (W.Tick - _endedTick < 40) return;
        FinishLog();
        var rep = AfterFightReport.Build(Log, Book, GameText.Instance);
        bool hurt = HeroF != null && HeroF.Book.Records.Count > 0;
        if (hurt || rep.Casualties >= 3) OpenReport(); else Banner = L.T("fl_ui_toast_report");
    }

    /// <summary>Drops the floating callouts (a staged screenshot: they only age while the view runs).</summary>
    public void ClearCallouts() { _callouts.Clear(); Feedback.Clear(); }

    public void Replace(FightWorld w)
    {
        Recorder?.Detach();
        W = w; _evCursor = 0; _log.Clear(); _hitAt.Clear(); _cam = Centre();
        StartFlavour(null);
        if (Report != null && IsInstanceValid(Report)) Report.Close();
        LogPanel?.SetExpanded(false);
    }

    /// <summary>The camera looks at the hero (or the middle of the fight) a metre above the feet, so the figures stand in the middle of the screen.</summary>
    Vector2 Centre()
    {
        var up = new Vector2(0, -0.9f);
        if (HeroF is { } h && h.Act != FAct.Fled) return new Vector2(h.X, h.Y) / 1000f + up;
        var act = W.Fighters.Where(f => f.Act != FAct.Fled).ToList();
        if (act.Count == 0) return up;
        return new Vector2((float)act.Average(f => f.X), (float)act.Average(f => f.Y)) / 1000f + up;
    }

    float Ppm => 48f / 1.8f * Zoom;               // a 1.8 m figure is about 48 px of the 64 px frame (the rest is headroom for weapons)
    Vector2 Screen(int xMm, int yMm) => GetViewportRect().Size * 0.5f + (new Vector2(xMm, yMm) / 1000f - _cam) * Ppm + Feedback.Shake;

    // ------------------------------------------------------------------ input (recorded)

    void PollHero()
    {
        var h = HeroF;
        if (h == null || _calling || !h.Player) return;
        int dx = 0, dy = 0;
        if (Actions.HeldLoose("combatstance.move_up")) dy -= 1;
        if (Actions.HeldLoose("combatstance.move_down")) dy += 1;
        if (Actions.HeldLoose("combatstance.move_left")) dx -= 1;
        if (Actions.HeldLoose("combatstance.move_right")) dx += 1;
        int md = FightMath.DirIndex(dx * 1024, dy * 1024);
        bool sprint = Actions.HeldLoose("combatstance.sprint");
        if (md != _lastMoveDir || sprint != _lastSprint) { _lastMoveDir = md; _lastSprint = sprint; Send(new FightInput(FightInputKind.Move, md, sprint ? 1 : 0)); }
        var m = GetGlobalMousePosition() - Screen(h.X, h.Y);
        if (m.LengthSquared() > 64)
        {
            int fd = FightMath.DirIndex((int)(m.X * 100), (int)(m.Y * 100));
            if (fd != _lastFaceDir) { _lastFaceDir = fd; Send(new FightInput(FightInputKind.Face, fd)); }
        }
    }

    public override void _Input(InputEvent e)
    {
        if (ModalOpen) return;                                                           // the report is up: it has the keys (and the mouse)
        if (e is InputEventKey { Pressed: true, Echo: false })
        {
            if (Actions.Pressed(e, "combatstance.last_report")) { OpenReport(); GetViewport().SetInputAsHandled(); return; }
            if (Actions.Pressed(e, "combatstance.combat_log")) { LogPanel.Toggle(); GetViewport().SetInputAsHandled(); return; }
        }
        if (e is InputEventMouse mouse && LogPanel.Covers(mouse.Position)) return;       // clicks on the open log panel are the panel's: no swings through it
        var h = HeroF;
        if (h == null || !h.Player) return;
        if (_calling) { if (e is InputEventKey { Pressed: true, Echo: false }) { CallingKey(e); GetViewport().SetInputAsHandled(); } return; }
        bool handled = true;
        if (e is InputEventMouseButton mb)
        {
            if (mb.ButtonIndex == MouseButton.Left) Send(mb.Pressed ? new FightInput(FightInputKind.AttackDown, _attack, -1, -1) : new FightInput(FightInputKind.AttackUp));
            else if (mb.ButtonIndex == MouseButton.Right) Send(new FightInput(mb.Pressed ? FightInputKind.BlockDown : FightInputKind.BlockUp));
            else handled = false;
        }
        else if (e is InputEventKey { Pressed: true, Echo: false })
        {
            int dodge = Actions.Pressed(e, "combatstance.dodge_up") ? 48 : Actions.Pressed(e, "combatstance.dodge_down") ? 16
                : Actions.Pressed(e, "combatstance.dodge_left") ? 32 : Actions.Pressed(e, "combatstance.dodge_right") ? 0 : -1;
            if (dodge >= 0) Send(new FightInput(FightInputKind.Dodge, dodge));
            else if (Actions.Pressed(e, "combatstance.called_shot")) BeginCalling();
            else if (Actions.Pressed(e, "combatstance.reload")) Send(new FightInput(FightInputKind.Reload));
            else if (Actions.Pressed(e, "combatstance.attack_next")) _attack = _attack + 1 >= h.Weapon.Attacks.Length ? -1 : _attack + 1;
            else if (Actions.Pressed(e, "combatstance.throw"))
            {
                var p = (GetGlobalMousePosition() - GetViewportRect().Size * 0.5f) / Ppm + _cam;
                Send(new FightInput(FightInputKind.Throw, (int)(p.X * 1000), (int)(p.Y * 1000)));
            }
            else handled = false;
        }
        else handled = false;
        if (handled) GetViewport().SetInputAsHandled();
    }

    // ------------------------------------------------------------------ called shot (T: paused paper doll)

    /// <summary>The doll's choices: every non-hosted part of the target, then the named gaps its armour leaves.</summary>
    public List<(int Part, int Gap, string Name)> CallChoices()
    {
        var t = Target(); var list = new List<(int, int, string)>();
        if (t == null) return list;
        foreach (var p in t.Plan.Parts) if (!p.IsHosted) list.Add((p.Index, -1, p.Name));
        ulong gaps = 0;
        foreach (var wv in t.Worn) gaps |= wv.Stats.GapMaskByPlan[t.Plan.Index];
        foreach (var g in t.Plan.Gaps)
            if ((gaps & (1UL << g.Index)) != 0)
            {
                int part = -1, best = -1;
                foreach (var p in t.Plan.Parts) if (!p.IsHosted && g.OnBase(p.BaseIndex) && p.RelPpm > best) { best = p.RelPpm; part = p.Index; }
                if (part >= 0) list.Add((part, g.Index, L.F("FIGHT_GAP", g.Name.Replace('_', ' '))));
            }
        MountedChoices(list, t);                                                // [mounted-combat] the mount's parts after the rider's
        return list;
    }

    Fighter? Target()
    {
        var h = HeroF; if (h == null) return null;
        if (h.Brain.Target >= 0 && h.Brain.Target < W.Fighters.Count && W.Fighters[h.Brain.Target].Active) return W.Fighters[h.Brain.Target];
        return W.NearestEnemy(h);
    }

    public void BeginCalling()
    {
        var t = Target(); if (t == null) return;
        _calling = true; _callAtk = _attack < 0 ? 0 : _attack;
        var ch = CallChoices(); _callSel = Math.Max(0, ch.FindIndex(c => c.Gap < 0 && t.Plan.Parts[c.Part].BaseId == "upper_body"));
        Send(new FightInput(FightInputKind.Target, t.Id));
        SetPaused?.Invoke(true);
    }

    void CallingKey(InputEvent e)
    {
        var ch = CallChoices(); var h = HeroF!;
        if (ch.Count == 0) { EndCalling(); return; }
        if (Actions.Pressed(e, "calledshot.part_prev")) _callSel = (_callSel + ch.Count - 1) % ch.Count;
        else if (Actions.Pressed(e, "calledshot.part_next")) _callSel = (_callSel + 1) % ch.Count;
        else if (Actions.Pressed(e, "calledshot.type_prev")) _callAtk = (_callAtk + h.Weapon.Attacks.Length - 1) % h.Weapon.Attacks.Length;
        else if (Actions.Pressed(e, "calledshot.type_next")) _callAtk = (_callAtk + 1) % h.Weapon.Attacks.Length;
        else if (Actions.Pressed(e, "calledshot.strike")) { var c = ch[_callSel]; EndCalling(); if (!MountedCalledShot(c.Part, c.Gap)) Send(new FightInput(FightInputKind.CalledShot, _callAtk, c.Part, c.Gap)); }
        else if (Actions.Pressed(e, "calledshot.cancel") || Actions.Pressed(e, "combatstance.called_shot")) EndCalling();
    }

    void EndCalling() { _calling = false; SetPaused?.Invoke(false); }

    /// <summary>Opens the doll on a given choice (screenshots).</summary>
    public void ShowCalling(string baseOrGap, int attack)
    {
        BeginCalling();
        var ch = CallChoices();
        int i = ch.FindIndex(c => c.Name.Contains(baseOrGap.Replace('_', ' '), StringComparison.OrdinalIgnoreCase));
        if (i >= 0) _callSel = i;
        _callAtk = attack;
    }

    /// <summary>The hit chance of the selected called shot (resolver-v2 4.1), percent.</summary>
    public double CallHitPct(int part, int gap, int attack)
    {
        var h = HeroF; var t = Target(); if (h == null || t == null) return 0;
        if (MountedHitPct(part, attack) is var mp && mp >= 0) return mp;           // [mounted-combat] a shot at the mount
        var s = StrikeV2.Create(h.Weapon, Math.Clamp(attack, 0, h.Weapon.Attacks.Length - 1), t.Plan, part, h.Skill);
        s.DefenderSkill = t.Skill; s.TargetGap = gap; s.Worn = t.Worn; s.WornCount = t.Worn.Length; s.Facing = W.FacingOf(t, h.X, h.Y);
        return W.Resolver.HitChanceBp(s) / 100.0;
    }

    // ------------------------------------------------------------------ events -> log and callouts

    void Consume()
    {
        for (; _evCursor < W.Events.Count; _evCursor++)
        {
            var e = W.Events[_evCursor];
            string actor = e.Actor >= 0 && e.Actor < W.Fighters.Count ? W.Fighters[e.Actor].Name : "";
            string target = e.Target >= 0 && e.Target < W.Fighters.Count ? W.Fighters[e.Target].Name : "";
            var tf = e.Target >= 0 && e.Target < W.Fighters.Count ? W.Fighters[e.Target] : null;
            string part = tf != null && e.Part >= 0 && e.Part < tf.Plan.Parts.Length ? tf.Plan.Parts[e.Part].Name.ToLowerInvariant() : "";
            var af = e.Actor >= 0 && e.Actor < W.Fighters.Count ? W.Fighters[e.Actor] : null;
            (string? t, Color c, Fighter? at, string call) = e.Kind switch
            {
                FightEventKind.Hit => (L.F("FIGHT_EV_HIT", actor, target, part), new Color(1, 0.45f, 0.35f), tf, part),
                FightEventKind.Blocked => (L.F("FIGHT_EV_BLOCKED", actor, target), new Color(0.7f, 0.85f, 1f), af, L.T("FIGHT_CALL_BLOCK")),
                FightEventKind.Parried => (L.F("FIGHT_EV_PARRIED", actor, target), new Color(0.45f, 1f, 0.5f), af, L.T("FIGHT_CALL_PARRY")),
                FightEventKind.Dodged => (L.F("FIGHT_EV_DODGED", actor, target), new Color(0.7f, 1f, 1f), af, L.T("FIGHT_CALL_DODGE")),
                FightEventKind.Missed => (L.F("FIGHT_EV_MISSED", actor, target), new Color(0.8f, 0.8f, 0.8f), tf, L.T("FIGHT_CALL_MISS")),
                FightEventKind.Stopped => (L.F("FIGHT_EV_STOPPED", actor, target), new Color(0.8f, 0.8f, 0.7f), tf, L.T("FIGHT_CALL_ARMOUR")),
                FightEventKind.Feint => (L.F("FIGHT_EV_FEINT", actor), new Color(0.9f, 0.9f, 0.5f), af, L.T("FIGHT_CALL_FEINT")),
                FightEventKind.Down => (L.F("FIGHT_EV_DOWN", actor), new Color(1, 0.55f, 0.2f), af, L.T("FIGHT_CALL_DOWN")),
                FightEventKind.Death => (L.F("FIGHT_EV_DEATH", actor), new Color(1, 0.2f, 0.2f), af, L.T("FIGHT_CALL_DEAD")),
                FightEventKind.Rout => (L.F("FIGHT_EV_ROUT", actor), new Color(1, 1, 0.3f), af, ""),
                FightEventKind.Surrender => (L.F("FIGHT_EV_SURRENDER", actor), Colors.White, af, ""),
                FightEventKind.Shot => (L.F("FIGHT_EV_SHOT", actor), new Color(0.85f, 0.8f, 0.6f), null, ""),
                FightEventKind.ProjectileHit => (L.F("FIGHT_EV_ARROW", target, part), new Color(1, 0.5f, 0.35f), tf, part),
                FightEventKind.Broke => (L.F("FIGHT_EV_BROKE", actor), new Color(0.9f, 0.7f, 0.5f), af, ""),
                _ => ((string?)null, Colors.White, (Fighter?)null, ""),
            };
            if (e.Kind == FightEventKind.Hit || e.Kind == FightEventKind.ProjectileHit) if (tf != null) _hitAt[tf.Id] = e.Tick;
            if (t == null && MountedEvent(e) is { } me) (t, c, at, call) = me;           // [mounted-combat] the mounted events
            if (t == null) continue;
            _log.Add((t, c, e.Tick)); if (_log.Count > 7) _log.RemoveAt(0);
            if (Feedback == null && at != null && call.Length > 0)      // the feedback layer draws the callouts now (severity colours, the facade)
            {
                int stack = _callouts.Count(x => x.Age < 0.5 && (x.Pos - new Vector2(at.X, at.Y)).Length() < 300);
                _callouts.Add((new Vector2(at.X, at.Y - stack * 380), call, c, 0));
            }
        }
    }

    // ------------------------------------------------------------------ drawing

    public override void _Draw()
    {
        var sz = GetViewportRect().Size;
        DrawRect(new Rect2(Vector2.Zero, sz), new Color(0.24f, 0.3f, 0.19f));
        float ppm = Ppm;
        var o = Screen(0, 0);
        var grid = new Color(0, 0, 0, 0.08f);
        for (float x = o.X % ppm; x < sz.X; x += ppm) DrawLine(new Vector2(x, 0), new Vector2(x, sz.Y), grid);
        for (float y = o.Y % ppm; y < sz.Y; y += ppm) DrawLine(new Vector2(0, y), new Vector2(sz.X, y), grid);
        foreach (var g in W.Ground) DrawCircle(Screen(g.X, g.Y), 3, new Color(0.5f, 0.4f, 0.3f));
        Feedback.DrawDecals(this, Screen, Ppm);
        if (HeroF is { Active: true } hero) DrawArcs(hero);
        foreach (var f in W.Fighters.OrderBy(f => (f.Act is FAct.Down or FAct.Dead ? -100000 : 0) + f.Y)) DrawFighter(f);
        foreach (var p in W.Projectiles) if (!p.Done) DrawProjectile(p);
        foreach (var f in W.Fighters) if (f.Act is not (FAct.Dead or FAct.Fled)) DrawBars(f);
        foreach (var c in _callouts)
        {
            var p = Screen((int)c.Pos.X, (int)c.Pos.Y) + new Vector2(0, -64 * Zoom - (float)c.Age * 30);
            float a = (float)Math.Clamp(1.4 - c.Age, 0, 1);
            DrawString(_font, p + new Vector2(-100 + 1, 1), c.Text, HorizontalAlignment.Center, 200, 16, new Color(0, 0, 0, a));
            DrawString(_font, p + new Vector2(-100, 0), c.Text, HorizontalAlignment.Center, 200, 16, new Color(c.Col, a));
        }
        Feedback.DrawOverlay(this, sz, _font, Screen, Zoom, HeroF?.Book.LossPm ?? 0);
        DrawHud(sz);
        if (_calling) DrawDoll(sz);
        if (Banner.Length > 0)
        {
            DrawRect(new Rect2(0, sz.Y * 0.42f, sz.X, 40), new Color(0, 0, 0, 0.6f));
            DrawString(_font, new Vector2(0, sz.Y * 0.42f + 27), Banner, HorizontalAlignment.Center, sz.X, 20, Colors.White);
        }
    }

    void DrawArcs(Fighter h)
    {
        var c = Screen(h.X, h.Y);
        float face = Mathf.Atan2(h.FY, h.FX);
        float reach = (h.ReachMm + W.K.ReachSlackMm) / 1000f * Ppm;
        var cone = new Color(1f, 0.85f, 0.4f, 0.18f);
        DrawSetTransform(c, 0, new Vector2(1, 0.55f));
        DrawArc(Vector2.Zero, reach, face - Mathf.Pi / 3, face + Mathf.Pi / 3, 24, new Color(1f, 0.85f, 0.4f, 0.5f), 2);
        DrawLine(Vector2.Zero, new Vector2(Mathf.Cos(face - Mathf.Pi / 3), Mathf.Sin(face - Mathf.Pi / 3)) * reach, cone, 2);
        DrawLine(Vector2.Zero, new Vector2(Mathf.Cos(face + Mathf.Pi / 3), Mathf.Sin(face + Mathf.Pi / 3)) * reach, cone, 2);
        if (h.Act == FAct.Block)
            DrawArc(Vector2.Zero, reach * 0.55f, face - Mathf.DegToRad(50), face + Mathf.DegToRad(50), 20, new Color(0.6f, 0.8f, 1f, 0.8f), 4);
        if (h.Launcher != null && h.Act is FAct.Aiming or FAct.Drawing)
        {
            float sig = Mathf.DegToRad(W.AimSigmaMdeg(h.ArcherySkill, h.Act == FAct.Drawing, h.Act == FAct.Aiming ? W.Tick - h.AimSince : 0, false, h.OneEye) / 1000f) * 2;
            float len = 30 * Ppm;
            DrawLine(Vector2.Zero, new Vector2(Mathf.Cos(face - sig), Mathf.Sin(face - sig)) * len, new Color(1, 1, 0.6f, 0.5f), 1);
            DrawLine(Vector2.Zero, new Vector2(Mathf.Cos(face + sig), Mathf.Sin(face + sig)) * len, new Color(1, 1, 0.6f, 0.5f), 1);
        }
        DrawSetTransform(Vector2.Zero, 0, Vector2.One);
    }

    void DrawProjectile(Projectile p)
    {
        var (x, y) = p.At(p.Flown);
        var a = Screen(x, y) + new Vector2(0, -p.Z(p.Flown) / 1000f * Ppm * 0.55f);
        var d = new Vector2(p.DirX, p.DirY) / 1024f * 0.6f * Ppm;
        DrawLine(a - d, a, new Color(0.95f, 0.9f, 0.75f), 2);
    }

    // ---- the figure

    /// <summary>The rig layers a kit shows (clothing view of plan 2.7: what is worn), the stance it holds its weapon in, and the outfit whose outline ring fits best.</summary>
    public static (List<string> Layers, string Stance, string Outfit) LookOf(Fighter f)
    {
        var l = new List<string> { "body", "hose", "linen" };
        foreach (var w in f.Kit.Worn)
        {
            string layer = w.ShapeId switch
            {
                "tunic" or "doublet" => "tunic", "gambeson" or "jack" or "padded_coif" => "gambeson", "mail_hauberk" or "mail_coif" or "mail_chausses" or "scale_hauberk" or "lamellar" => "mail",
                "breastplate" or "backplate" or "brigandine" or "cuir_bouilli" => "plate", "surcoat" or "tabard" => "surcoat", "cloak" => "cloak", "hood" => "hood",
                "helm" or "kettle_hat" or "nasal_helm" or "spangenhelm" or "great_helm" or "cap" => "helm", _ => "",
            };
            if (layer.Length > 0 && !l.Contains(layer)) l.Add(layer);
        }
        if (f.Shield != null) l.Add("shield");
        bool pole = f.Family == "polearms";
        if (f.Family is "blades" or "axes" or "hafted" or "improvised") l.Add("sword");
        if (pole) l.Add("spear");
        string outfit = pole ? "spearman" : l.Contains("plate") ? "knight" : l.Contains("mail") ? "soldier" : l.Contains("gambeson") ? "hero" : "peasant";
        return (l, pole ? "pole" : "blade", outfit);
    }

    /// <summary>The overlays a fighter's wound book calls for, and the limb groups lost (1 left forearm, 2 right forearm, 3 left shin, 4 right shin).</summary>
    public static (List<string> Overlays, int MissingGroups) WoundLook(Fighter f)
    {
        var o = new List<string>(); int missing = 0;
        foreach (var w in f.Book.Records)
        {
            if (w.Is(Aid.Resolved) || w.State == WoundState.Scar) continue;
            var p = f.Plan.Parts[w.Part];
            string b = p.IsHosted && p.Host >= 0 ? f.Plan.Parts[p.Host].BaseId : p.BaseId;
            string side = p.Side == 1 ? "_l" : "_r";
            string region = b switch
            {
                "head" or "neck" or "eye" or "ear" or "nose" or "jaw" or "teeth" or "skull" => "head",
                "upper_body" or "lower_body" or "ribs" or "collarbone" or "pelvis" => "torso",
                "upper_arm" or "lower_arm" or "hand" or "thumb" or "fingers" => "arm" + side,
                "upper_leg" or "lower_leg" or "foot" or "kneecap" => "leg" + side,
                _ => "torso",
            };
            if (w.Has(WoundFlags.Severed) && b is "lower_arm" or "hand" or "lower_leg" or "foot")
            {
                missing |= 1 << (region == "arm_l" ? 1 : region == "arm_r" ? 2 : region == "leg_l" ? 3 : 4);
                Add("stump_" + region);
                continue;
            }
            if (w.Is(Aid.Splinted) && region.StartsWith("arm", StringComparison.Ordinal) | region.StartsWith("leg", StringComparison.Ordinal)) Add("splint_" + region);
            if (w.Dressing is Dressing.Bandage or Dressing.Pressure or Dressing.Tourniquet) Add("bandage_" + region);
            else if (w.BleedX10 > 0 && w.Cat is WoundCat.Skin or WoundCat.Muscle or WoundCat.Vessel or WoundCat.Severed) Add("blood_" + region);
        }
        void Add(string s) { if (!o.Contains(s)) o.Add(s); }
        return (o, missing);
    }

    (string Action, int Frame) Pose(Fighter f)
    {
        int t = W.Tick;
        switch (f.Act)
        {
            case FAct.Dead: return ("lie", 0);
            case FAct.Down: return f.Crawling ? ("crawl", (t / 8) % 2) : ("lie", 0);
            case FAct.Windup: return ("windup", f.WindElapsed * 2 >= f.WindNeeded ? 1 : 0);
            case FAct.Strike: return ("strike", f.ActTicks >= f.StrikeTicks ? 0 : 1);
            case FAct.Recover: return ("recover", 0);
            case FAct.Stagger: return ("stagger", 0);
            case FAct.Block: return t - f.BlockSince < 4 ? ("parry", 0) : ("block", 0);
            case FAct.Dodge: return ("jog", (t / 2) % 4);
        }
        if (_hitAt.TryGetValue(f.Id, out var h) && t - h < 5) return ("hitreact", 0);
        if (_riderLift != Vector2.Zero) return ("idle", 0);                       // [mounted-combat] the horse walks, not the rider
        if (f.LastMoveMm > 0) return (f.LastMoveMm > 180 ? "jog" : "walk", (t / 4) % 4);
        return ("idle", (t / 16) % 2);
    }

    static (int View, bool Mirror) ViewOf(int fx, int fy)
    {
        if (Math.Abs(fx) >= Math.Abs(fy)) return (2, fx < 0);
        return fy > 0 ? (0, false) : (1, false);
    }

    void DrawFighter(Fighter f)
    {
        if (_riderLift == Vector2.Zero && MountedFigure(f)) return;             // [mounted-combat] horses and riders (FightView.Mounted.cs)
        if (f.Act == FAct.Fled) return;
        var pos = Screen(f.X, f.Y) + _riderLift;
        float z = Zoom;
        DrawSetTransform(pos, 0, new Vector2(1, 0.32f));
        DrawCircle(Vector2.Zero, 11 * z, new Color(0, 0, 0, 0.28f));
        DrawSetTransform(Vector2.Zero, 0, Vector2.One);
        if (f.Plan.Id != "human")
        {
            var col = f.Plan.Id == "quadruped" ? new Color(0.45f, 0.42f, 0.38f) : new Color(0.32f, 0.24f, 0.18f);
            float r = f.Plan.Id == "quadruped" ? 9 * z : 16 * z;
            DrawSetTransform(pos + new Vector2(0, -r * 0.6f), 0, new Vector2(1.6f, 0.8f));
            DrawCircle(Vector2.Zero, r, f.Active ? col : col.Darkened(0.4f));
            DrawSetTransform(Vector2.Zero, 0, Vector2.One);
            return;
        }
        var (layers, stance, outfit) = LookOf(f);
        var (overlays, missing) = WoundLook(f);
        if (!f.WeaponArm) { layers.Remove("sword"); layers.Remove("spear"); }
        if ((missing & 4) != 0) { layers.Remove("sword"); layers.Remove("spear"); }
        if ((missing & 2) != 0) layers.Remove("shield");
        var (action, frame) = Pose(f);
        var (view, mirror) = ViewOf(f.FX, f.FY);
        int ax = _rig.AnchorX[view];
        var tint = Colors.White;
        if (_hitAt.TryGetValue(f.Id, out var h) && W.Tick - h < 3) tint = new Color(1f, 0.55f, 0.55f);
        if (f.Act == FAct.Dead) tint = new Color(0.55f, 0.5f, 0.5f);
        if (f.Routing) tint = tint * new Color(1, 1, 0.75f);
        tint = Feedback.Tint(f.Id, tint);                                  // the white flash and the wound's red tint (through the facade)
        var all = new List<string>(layers); all.AddRange(overlays);
        // west is the side view mirrored: the whole figure is drawn through a flipped transform about the feet (a negative rect size is not a flip in every renderer)
        DrawSetTransform(pos, 0, new Vector2(mirror ? -1 : 1, 1));
        void Piece(RigAtlas.Piece p) =>
            DrawTextureRectRegion(_rig.Atlas, new Rect2((p.Ox - ax) * z, (p.Oy - 64) * z, p.W * z, p.H * z), new Rect2(p.X, p.Y, p.W, p.H), tint);
        if (missing == 0)
            foreach (var p in _rig.Get("outline." + outfit, view, stance, action, frame)) if (p.Plane == 0) Piece(p);
        string[] order = RigAtlasOrder();
        for (int depth = 0; depth < 3; depth++)
            foreach (var layer in order)
            {
                if (!all.Contains(layer)) continue;
                var pcs = _rig.Get(layer, view, stance, action, frame);
                bool split = missing != 0 && pcs.Any(p => p.Plane >= 3);
                foreach (var p in pcs)
                {
                    if (!split) { if (p.Plane == depth) Piece(p); continue; }
                    if (p.Plane < 3) continue;
                    int q = p.Plane - 3, d = q % 3, g = q / 3;
                    if (d == depth && (missing & (1 << g)) == 0) Piece(p);
                }
            }
        DrawSetTransform(Vector2.Zero, 0, Vector2.One);
    }

    string[]? _order;
    string[] RigAtlasOrder() => _order ??= _rig.Order;

    void DrawBars(Fighter f)
    {
        if (MountedBars(f)) return;                                             // [mounted-combat] a horse's panic and stamina
        var pos = Screen(f.X, f.Y) + BarLift(f);
        float z = Zoom, bw = 16 * z, x = pos.X - bw / 2, y = pos.Y - 60 * z;
        if (f.Act == FAct.Down) y = pos.Y - 24 * z;
        float blood = Math.Clamp(1 - f.Book.LossPm / 500f, 0, 1);
        float stam = f.Stamina / 10000f;
        DrawRect(new Rect2(x - 1, y - 1, bw + 2, 9), new Color(0, 0, 0, 0.65f));
        DrawRect(new Rect2(x, y, bw * blood, 3), new Color(0.8f, 0.12f, 0.12f));
        DrawRect(new Rect2(x, y + 4, bw * stam, 3), new Color(0.35f, 0.8f, 0.3f));
        DrawLine(new Vector2(x + bw * 0.25f, y + 3), new Vector2(x + bw * 0.25f, y + 8), Colors.White, 1);     // the 25 mark
        DrawString(_font, new Vector2(x - 40, y - 4), f.Name, HorizontalAlignment.Center, bw + 80, 13, f.Player ? Colors.Gold : Colors.White);
        if (f.Act == FAct.Windup && f.Req.Kind == BlowKind.Melee)
        {
            // the wind-up: orange fill, its last 0.2 s (the parry window) a red tail; the bar turns red inside the window
            float wy = y - 30, ww = 20 * z;
            float wx = pos.X - ww / 2;
            int need = Math.Max(1, f.WindNeeded);
            float frac = Math.Min(1f, f.WindElapsed / (float)need);
            float tail = Math.Min(1f, W.K.ParryWindowTicks / (float)need);
            DrawRect(new Rect2(wx - 1, wy - 1, ww + 2, 10), Colors.Black);
            DrawRect(new Rect2(wx, wy, ww, 8), new Color(0.2f, 0.2f, 0.2f));
            DrawRect(new Rect2(wx + ww * (1 - tail), wy, ww * tail, 8), new Color(0.5f, 0.1f, 0.1f));
            bool inWindow = frac >= 1 - tail;
            DrawRect(new Rect2(wx, wy, ww * frac, 8), inWindow ? new Color(1f, 0.22f, 0.18f) : new Color(1f, 0.68f, 0.2f));
            if (f.Heavy) DrawString(_font, new Vector2(wx - 30, wy - 4), L.T("FIGHT_HEAVY"), HorizontalAlignment.Center, ww + 60, 12, Colors.OrangeRed);
        }
        else if (f.Act == FAct.Stagger) DrawString(_font, new Vector2(x - 40, y - 18), L.T("FIGHT_STAGGER"), HorizontalAlignment.Center, bw + 80, 12, Colors.LightGray);
        if (f.Act == FAct.Reloading && f.Launcher != null)
        {
            float done = 1 - f.ActTicks / (float)Math.Max(1, f.Launcher.ReloadTicks);
            DrawArc(pos + new Vector2(0, -58 * z), 7, -Mathf.Pi / 2, -Mathf.Pi / 2 + Mathf.Tau * done, 16, new Color(0.9f, 0.9f, 0.6f), 3);
        }
    }

    // ---- HUD and the called-shot doll

    void DrawHud(Vector2 sz)
    {
        var lines = new List<string> { L.T("FIGHT_HELP_1"), L.T("FIGHT_HELP_2") };
        if (HeroF is { } h)
        {
            string atk = _attack < 0 ? L.T("FIGHT_AUTO") : h.Weapon.Attacks[_attack].Def.Name;
            lines.Add(L.F("FIGHT_HUD", h.Act.ToString(), 100 - h.Book.LossPm / 10, h.Stamina / 100, h.Book.PainX100 / 100, h.Book.KnockoutPainX100 / 100, atk, h.Book.RateMlPerS));
            if (h.Launcher != null) lines.Add(L.F("FIGHT_HUD_AMMO", h.Ammo, h.Loaded ? L.T("FIGHT_LOADED") : L.T("FIGHT_EMPTY")));
        }
        lines.Add(L.F("FIGHT_TIME", W.Tick / 20.0));
        DrawMountedHud(sz);                                                     // [mounted-combat] the charge meter, the horse
        for (int i = 0; i < lines.Count; i++)
        {
            DrawString(_font, new Vector2(11, 23 + i * 18), lines[i], HorizontalAlignment.Left, sz.X - 20, 14, Colors.Black);
            DrawString(_font, new Vector2(10, 22 + i * 18), lines[i], HorizontalAlignment.Left, sz.X - 20, 14, Colors.White);
        }
        for (int i = 0; i < _log.Count && LogPanel == null; i++)      // the combat log panel's feed replaces this plain list
        {
            var (t, c, _) = _log[i];
            float yy = sz.Y - 14 - (_log.Count - 1 - i) * 18;
            DrawString(_font, new Vector2(11, yy + 1), t, HorizontalAlignment.Left, sz.X * 0.6f, 14, Colors.Black);
            DrawString(_font, new Vector2(10, yy), t, HorizontalAlignment.Left, sz.X * 0.6f, 14, c);
        }
    }

    static readonly (string Base, string Side, Rect2 R)[] Doll =
    {
        ("head", "", new Rect2(-14, 0, 28, 30)), ("neck", "", new Rect2(-7, 30, 14, 10)), ("upper_body", "", new Rect2(-26, 40, 52, 56)), ("lower_body", "", new Rect2(-24, 96, 48, 30)),
        ("upper_arm", "_r", new Rect2(-44, 42, 16, 44)), ("upper_arm", "_l", new Rect2(28, 42, 16, 44)), ("lower_arm", "_r", new Rect2(-48, 88, 16, 40)), ("lower_arm", "_l", new Rect2(32, 88, 16, 40)),
        ("hand", "_r", new Rect2(-50, 130, 18, 18)), ("hand", "_l", new Rect2(32, 130, 18, 18)), ("upper_leg", "_r", new Rect2(-22, 128, 20, 60)), ("upper_leg", "_l", new Rect2(2, 128, 20, 60)),
        ("lower_leg", "_r", new Rect2(-21, 190, 18, 56)), ("lower_leg", "_l", new Rect2(3, 190, 18, 56)), ("foot", "_r", new Rect2(-26, 248, 22, 12)), ("foot", "_l", new Rect2(4, 248, 22, 12)),
    };

    void DrawDoll(Vector2 sz)
    {
        var t = Target(); var h = HeroF; if (t == null || h == null) return;
        var ch = CallChoices(); if (ch.Count == 0) return;
        _callSel = Math.Clamp(_callSel, 0, ch.Count - 1);
        var sel = ch[_callSel];
        float pw = 560, ph = 470;
        var o = new Vector2(sz.X - pw - 20, 30);
        DrawRect(new Rect2(o, new Vector2(pw, ph)), new Color(0.06f, 0.05f, 0.05f, 0.9f));
        DrawRect(new Rect2(o, new Vector2(pw, ph)), new Color(0.75f, 0.6f, 0.3f), false, 2);
        DrawString(_font, o + new Vector2(14, 26), L.F("FIGHT_DOLL_TITLE", t.Name), HorizontalAlignment.Left, pw - 20, 16, new Color(0.95f, 0.8f, 0.4f));
        DrawString(_font, o + new Vector2(14, 46), L.T("FIGHT_DOLL_KEYS"), HorizontalAlignment.Left, pw - 20, 12, Colors.LightGray);
        // the paper doll: each part shaded by how well the armour covers it, the choice outlined in gold
        var dollO = o + new Vector2(120, 70);
        foreach (var (b, side, r) in Doll)
        {
            int pi = t.Plan.IndexOf(b + side); if (pi < 0) pi = t.Plan.IndexOf(b);
            if (pi < 0) continue;
            var part = t.Plan.Parts[pi];
            int cov = 0;
            foreach (var wv in t.Worn) if (wv.Stats.Layer == Ravensguard.Content.ArmourLayer.Armour) cov = Math.Max(cov, wv.Stats.CoverageByPlan[t.Plan.Index][part.BaseIndex]);
            var col = new Color(0.78f, 0.62f, 0.5f).Lerp(new Color(0.55f, 0.6f, 0.68f), cov / 1000f);
            if ((t.MissingMask & (1UL << pi)) != 0) col = new Color(0.2f, 0.2f, 0.2f);
            bool on = sel.Part == pi;
            var rr = new Rect2(dollO + r.Position, r.Size);
            DrawRect(rr, on ? col.Lightened(0.25f) : col);
            DrawRect(rr, on ? new Color(1f, 0.82f, 0.2f) : new Color(0.15f, 0.12f, 0.1f), false, on ? 3 : 1);
            foreach (var w in t.Book.Records) if (w.Part == pi && w.BleedX10 > 0) { DrawCircle(rr.GetCenter(), 4, new Color(0.7f, 0.05f, 0.05f)); break; }
        }
        if (sel.Gap >= 0)
        {
            var gname = t.Plan.Gaps[sel.Gap].Name;
            var gp = dollO + gname switch { "armpit" => new Vector2(-30, 48), "groin" => new Vector2(0, 122), "face" => new Vector2(0, 16), "neck" => new Vector2(0, 35), "back_of_knee" => new Vector2(-12, 190), "inner_elbow" => new Vector2(-40, 88), _ => new Vector2(0, 60) };
            DrawCircle(gp, 7, new Color(1f, 0.82f, 0.2f));
            DrawCircle(gp, 4, new Color(0.1f, 0.08f, 0.05f));
        }
        // the list: parts and gaps with their hit chance
        var lo = o + new Vector2(300, 70);
        int first = Math.Clamp(_callSel - 8, 0, Math.Max(0, ch.Count - 17));
        for (int i = first; i < Math.Min(ch.Count, first + 17); i++)
        {
            var c = ch[i]; bool on = i == _callSel;
            float yy = lo.Y + (i - first) * 20;
            if (on) DrawRect(new Rect2(lo.X - 6, yy - 15, 250, 19), new Color(0.35f, 0.28f, 0.1f));
            DrawString(_font, new Vector2(lo.X, yy), $"{(on ? "> " : "  ")}{c.Name}", HorizontalAlignment.Left, 180, 13, on ? Colors.Yellow : c.Gap >= 0 ? new Color(0.9f, 0.8f, 0.55f) : Colors.LightGray);
            DrawString(_font, new Vector2(lo.X + 180, yy), $"{CallHitPct(c.Part, c.Gap, _callAtk):0}%", HorizontalAlignment.Right, 60, 13, on ? Colors.Yellow : Colors.LightGray);
        }
        var atk = h.Weapon.Attacks[Math.Clamp(_callAtk, 0, h.Weapon.Attacks.Length - 1)];
        DrawString(_font, o + new Vector2(14, ph - 44), L.F("FIGHT_DOLL_ATTACK", atk.Def.Name, atk.Type.ToString().ToLowerInvariant()), HorizontalAlignment.Left, pw - 20, 15, Colors.White);
        DrawString(_font, o + new Vector2(14, ph - 20), L.F("FIGHT_DOLL_CHANCE", ch[_callSel].Name, CallHitPct(sel.Part, sel.Gap, _callAtk)), HorizontalAlignment.Left, pw - 20, 15, new Color(1f, 0.85f, 0.4f));
    }
}
