using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Ravensguard.Content;
using Ravensguard.Game.Audio;
using Ravensguard.Sim.Combat.Flavour;
using Ravensguard.Sim.Combat.Melee2;
using Ravensguard.Sim.Core;

namespace Ravensguard.Game.World.Combat;

/// <summary>The accessibility toggles as the planner reads them: every one goes through the <see cref="Accessibility"/> facade (test FL-12), never the settings.</summary>
public sealed class AccessibilityFeedback : IFeedbackSettings
{
    public static readonly AccessibilityFeedback Instance = new();
    public bool ShakeOn => Accessibility.ShakeScale(1f) > 0f;
    public bool FlashesOn => Accessibility.FlashScale(1f) > 0f;
    public bool SlowMotionOn => Accessibility.TimeScale(0.35f) < 1f;
    public bool CaptionsOn => Accessibility.CaptionsEnabled;
    public bool ReduceBlood => Accessibility.ReduceBloodEnabled;
}

/// <summary>
/// Hit feedback of a fight (feedback-flavour-log 4.4, 4.5, 4.6, 4.9): hit flashes, part callouts, screen shake, persistent blood decals, hit-stop, slow motion, the blood-loss vignette,
/// placeholder sound hooks and their captions. It reads the <see cref="FightLog"/> the recorder writes and never the sim; the planner's numbers pass through <see cref="Accessibility"/>
/// before anything is drawn or slowed. No game state lives here: a fight that is not being looked at needs none of it.
/// </summary>
public sealed class FightFeedback
{
    public const int MaxDecals = 400, MaxCallouts = 120;

    readonly FightLog _log;
    readonly FightWorld _w;
    readonly FeedbackPlanner _planner;
    readonly SlowMotionGovernor _slow = new();
    readonly Dictionary<int, double> _flash = [];       // fighter id -> seconds of white flash left
    readonly Dictionary<int, double> _tint = [];
    readonly Dictionary<int, double> _lastFlash = [];
    readonly List<Callout3> _callouts = [];
    readonly List<Decal> _decals = [];
    readonly List<(string Text, double Age)> _captions = [];
    int _serial = -1;
    double _clock;
    double _shakeAmp, _shakeLeft, _hitStop;
    Vector2 _shake;
    int _jitter;

    public readonly record struct Callout3(int XMm, int YMm, string Text, Color Col, bool Bold, double Age);
    public readonly record struct Decal(int XMm, int YMm, int Size, int Tick, int Shade);

    public FightFeedback(FightLog log, FightWorld w, IReadOnlyList<AudioEventDef> audio)
    {
        _log = log; _w = w;
        _planner = new FeedbackPlanner(log, audio, AccessibilityFeedback.Instance);
    }

    public IReadOnlyList<Callout3> Callouts => _callouts;
    public IReadOnlyList<Decal> Decals => _decals;
    /// <summary>The screen shake offset in pixels (zero with the setting off).</summary>
    public Vector2 Shake => _shake;
    public bool HitStopping => _hitStop > 0;

    /// <summary>Real seconds to fight steps: 1 normally; the slow-motion scale during a moment, 0 during a hit-stop. Fight steps per real second change, never the step (FL-9).</summary>
    public double TimeScale
    {
        get
        {
            if (_hitStop > 0 && Accessibility.SlowMotionEnabled) return 0;
            int pm = _slow.Scale((long)(_clock * 1000));
            return pm >= 1000 ? 1.0 : Accessibility.TimeScale(pm / 1000f);
        }
    }

    public static Color SeverityColour(int sev) => sev switch
    {
        0 => new Color(0.72f, 0.72f, 0.72f), 1 => Colors.White, 2 => new Color(1f, 0.62f, 0.2f), 3 => new Color(1f, 0.28f, 0.22f), _ => new Color(1f, 0.1f, 0.1f),
    };

    /// <summary>Takes in the lines written since the last call: plans and applies each (callouts, flashes, decals, sounds, captions, a slow-motion moment).</summary>
    public void Poll()
    {
        var lines = _log.Lines;
        int i = _serial < 0 ? 0 : Math.Max(0, _log.IndexOfSerial(_serial) + 1);
        if (_serial >= 0 && _log.IndexOfSerial(_serial) < 0) i = 0;
        for (; i < lines.Count; i++)
        {
            var l = lines[i];
            _serial = l.Serial;
            bool near = IsNear(l);
            var f = _planner.Plan(l, near);
            Apply(l, f);
        }
    }

    bool IsNear(in FightLine l)
    {
        if (_log.Hero < 0) return true;
        if (l.Actor == _log.Hero || l.Target == _log.Hero) return true;
        if (_log.Hero >= _w.Fighters.Count) return false;
        var h = _w.Fighters[_log.Hero];
        foreach (int id in new[] { (int)l.Actor, (int)l.Target })
            if (id >= 0 && id < _w.Fighters.Count) { var o = _w.Fighters[id]; if (FightMath.Len(o.X - h.X, o.Y - h.Y) <= 6000) return true; }
        return false;
    }

    (int X, int Y) Where(in FightLine l)
    {
        int id = l.Kind is FightEventKind.Hit or FightEventKind.Stopped or FightEventKind.Missed ? l.Target : l.Actor;
        if (id < 0 || id >= _w.Fighters.Count) id = Math.Max(0, (int)l.Actor);
        if (id >= _w.Fighters.Count) return (0, 0);
        var f = _w.Fighters[id];
        return (f.X, f.Y);
    }

    void Apply(in FightLine l, in HitFeedback f)
    {
        var (x, y) = Where(l);
        int victim = l.Kind is FightEventKind.Hit or FightEventKind.Stopped ? l.Target : l.Actor;
        if (f.FlashMs > 0 && victim >= 0)
        {
            // at most two flashes a second on one sprite; the intensity goes through the facade
            if (!_lastFlash.TryGetValue(victim, out var last) || _clock - last >= 0.5)
            {
                _lastFlash[victim] = _clock;
                if (Accessibility.FlashScale(1f) > 0f) _flash[victim] = f.FlashMs / 1000.0;
            }
            if (f.TintMs > 0 && Accessibility.FlashScale(1f) > 0f) _tint[victim] = f.TintMs / 1000.0;
        }
        if (f.Callout.Key != null && _callouts.Count < MaxCallouts)
        {
            string t = L.T(f.Callout.Key);
            if (t != f.Callout.Key)
            {
                int stack = _callouts.Count(c => c.Age < 0.5 && Math.Abs(c.XMm - x) < 400 && Math.Abs(c.YMm - y) < 400);
                _callouts.Add(new Callout3(x, y - stack * 380, t, SeverityColour(f.Callout.Severity), f.Callout.Severity >= 4, 0));
            }
        }
        if (f.ShakePx > 0)
        {
            float amp = Accessibility.ShakeScale(f.ShakePx);
            if (amp > 0f) { _shakeAmp = amp; _shakeLeft = f.ShakeMs / 1000.0; }
        }
        if (f.HitStopMs > 0) _hitStop = f.HitStopMs / 1000.0;
        if (f.SlowMotion) _slow.Request((long)(_clock * 1000), Accessibility.SlowMotionEnabled);
        if (f.Decal > 0 && !Accessibility.ReduceBloodEnabled && _decals.Count < MaxDecals * 4)
        {
            int jx = DetRng.Range(_log.Seed, l.Tick, (ulong)l.Serial, RngStreams.CosmeticJitter, 1, -250, 251), jy = DetRng.Range(_log.Seed, l.Tick, (ulong)l.Serial, RngStreams.CosmeticJitter, 2, -150, 151);
            _decals.Add(new Decal(x + jx, y + jy, f.Decal, l.Tick, DetRng.Range(_log.Seed, l.Tick, (ulong)l.Serial, RngStreams.CosmeticJitter, 3, 0, 3)));
            if (_decals.Count > MaxDecals) _decals.RemoveAt(0);
        }
        if (f.Sound.Length > 0) AudioBus.Emit(f.Sound, x, y, f.Intensity);
        if (f.Caption.Length > 0 && Accessibility.CaptionsEnabled)
        {
            string text = L.T(f.Caption);
            if (text != f.Caption)
            {
                string dir = "ahead";
                if (_log.Hero >= 0 && _log.Hero < _w.Fighters.Count) { var h = _w.Fighters[_log.Hero]; dir = VoicePool.Bearing(x - h.X, y - h.Y, h.FX, h.FY); }
                _captions.Add((L.F("fl_cap_dir", text, L.T("fl_cap_dir_" + dir)), 0));
                if (_captions.Count > 3) _captions.RemoveAt(0);
            }
        }
    }

    /// <summary>Ages the effects by real seconds (not fight time: a slow-motion moment keeps its real length).</summary>
    public void Update(double dt)
    {
        _clock += dt;
        for (int i = _callouts.Count - 1; i >= 0; i--) { var c = _callouts[i]; if (c.Age + dt > 1.2) _callouts.RemoveAt(i); else _callouts[i] = c with { Age = c.Age + dt }; }
        foreach (var k in _flash.Keys.ToList()) { _flash[k] -= dt; if (_flash[k] <= 0) _flash.Remove(k); }
        foreach (var k in _tint.Keys.ToList()) { _tint[k] -= dt; if (_tint[k] <= 0) _tint.Remove(k); }
        for (int i = _captions.Count - 1; i >= 0; i--) { var c = _captions[i]; if (c.Age + dt > 2.5) _captions.RemoveAt(i); else _captions[i] = (c.Text, c.Age + dt); }
        if (_hitStop > 0) _hitStop = Math.Max(0, _hitStop - dt);
        if (_shakeLeft > 0)
        {
            _shakeLeft -= dt;
            _jitter++;
            int a = DetRng.Range(_log.Seed, _jitter, 7, RngStreams.CosmeticJitter, 10, -100, 101), b = DetRng.Range(_log.Seed, _jitter, 7, RngStreams.CosmeticJitter, 11, -100, 101);
            double k = Math.Max(0, _shakeLeft / 0.15) * _shakeAmp / 100.0;
            _shake = new Vector2((float)(a * k), (float)(b * k));
        }
        else _shake = Vector2.Zero;
    }

    /// <summary>The colour a sprite is drawn in: a white flash, then the wound's red tint, else <paramref name="normal"/>.</summary>
    public Color Tint(int fighter, Color normal)
    {
        if (_flash.ContainsKey(fighter)) return new Color(2.2f, 2.2f, 2.2f);
        if (_tint.ContainsKey(fighter)) return new Color(1f, 0.5f, 0.5f);
        return normal;
    }

    public void Clear() { _callouts.Clear(); _flash.Clear(); _tint.Clear(); _captions.Clear(); _shake = Vector2.Zero; _shakeLeft = 0; _hitStop = 0; }

    // ------------------------------------------------------------------ drawing

    /// <summary>Blood on the ground (4.4): pools sized by the wound, drying to brown as the fight goes on.</summary>
    public void DrawDecals(CanvasItem ci, Func<int, int, Vector2> screen, float ppm)
    {
        foreach (var d in _decals)
        {
            float age = Math.Clamp((_w.Tick - d.Tick) / 600f, 0, 1);               // a fresh red becomes a brown stain
            var col = new Color(0.55f, 0.05f, 0.05f).Lerp(new Color(0.3f, 0.12f, 0.06f), age);
            col.A = 0.6f;
            var p = screen(d.XMm, d.YMm);
            float r = (d.Size * 0.09f + 0.05f + d.Shade * 0.015f) * ppm;
            ci.DrawSetTransform(p, 0, new Vector2(1, 0.45f));
            ci.DrawCircle(Vector2.Zero, r, col);
            ci.DrawSetTransform(Vector2.Zero, 0, Vector2.One);
        }
    }

    /// <summary>Callouts over the wounds, the blood-loss vignette and the captions (all through the facade; the callout words are not effects and always show).</summary>
    public void DrawOverlay(CanvasItem ci, Vector2 size, Font font, Func<int, int, Vector2> screen, float zoom, int heroLossPm)
    {
        foreach (var c in _callouts)
        {
            var p = screen(c.XMm, c.YMm) + new Vector2(0, -64 * zoom - (float)c.Age * 24f);
            float a = (float)Math.Clamp(1.2 - c.Age, 0, 1);
            int fs = c.Bold ? 22 : 16;
            ci.DrawString(font, p + new Vector2(-100 + 1, 1), c.Text, HorizontalAlignment.Center, 200, fs, new Color(0, 0, 0, a));
            ci.DrawString(font, p + new Vector2(-100, 0), c.Text, HorizontalAlignment.Center, 200, fs, new Color(c.Col, a));
            if (c.Bold) ci.DrawString(font, p + new Vector2(-99, 0), c.Text, HorizontalAlignment.Center, 200, fs, new Color(c.Col, a));
        }
        // the vignette tightens as blood runs out (15, 30 and 40 percent), never above 40 percent opacity, and with flashes off it is not drawn
        float vig = heroLossPm >= 400 ? 0.4f : heroLossPm >= 300 ? 0.28f : heroLossPm >= 150 ? 0.14f : 0f;
        vig = Accessibility.FlashScale(vig);
        if (vig > 0)
        {
            var edge = new Color(0.35f, 0, 0, vig);
            float w = size.X * 0.12f, h = size.Y * 0.12f;
            ci.DrawRect(new Rect2(0, 0, size.X, h), edge); ci.DrawRect(new Rect2(0, size.Y - h, size.X, h), edge);
            ci.DrawRect(new Rect2(0, 0, w, size.Y), edge); ci.DrawRect(new Rect2(size.X - w, 0, w, size.Y), edge);
        }
        for (int i = 0; i < _captions.Count; i++)
        {
            var (t, age) = _captions[i];
            float a = (float)Math.Clamp(2.5 - age, 0, 1);
            float y = size.Y - 70 - (_captions.Count - 1 - i) * 22;
            ci.DrawRect(new Rect2(size.X / 2 - 150, y - 16, 300, 22), new Color(0, 0, 0, 0.55f * a));
            ci.DrawString(font, new Vector2(size.X / 2 - 150, y), t, HorizontalAlignment.Center, 300, 15, new Color(1, 1, 1, a));
        }
    }
}
