using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Ravensguard.Game.UI;
using Ravensguard.Sim.Autopilot;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Live;
using Ravensguard.Sim.Travel;
using Ravensguard.Sim.World;

namespace Ravensguard.Game.World;

/// <summary>What one barrier of the autopilot gave back: the person's view and where the AI wants the hero to walk.</summary>
public sealed record ApResult(AutopilotView View);

/// <summary>
/// The street view's side of the autopilot (autopilot-v1 spec 6): the Y panel, the HUD chip, and the hero's walk. The sim's <see cref="AutopilotWorld"/> runs on the tick worker at a barrier about once a real second; the
/// view reads the copy it hands back, walks the hero to the point the AI wants (the same <c>CommandMoveTo</c> a click makes, so it is in the command log), and takes the hero back at once on any movement key, a click on the
/// ground, or opening the trade or talk screen. Nothing here decides anything.
/// </summary>
public partial class StreetView
{
    AutopilotWorld? _ap;
    Task<AutopilotWorld?>? _apInit;
    Task<ApResult>? _apTask;
    AutopilotView _apView = AutopilotView.None;
    AutopilotSetup? _apSetup;
    long _apTick;
    Button? _apChip; Label? _apBanner; PanelContainer? _apAlert; Label? _apAlertText;
    double _apMoveCool; int _apMoveFail;
    bool _apLocalPaused;
    long _apShotAt = -1;
    readonly object _apPosLock = new();
    (long X, long Y) _apPos;

    /// <summary>Builds the autopilot on the tick worker once the hero's desk is there (after <see cref="AttachHero"/>).</summary>
    void AttachAutopilot()
    {
        if (Runtime is not { } rt || _heroLink == null || _source?.Content == null) return;
        var link = _heroLink; var content = _source.Content;
        _apInit = rt.AtBarrier<AutopilotWorld?>(() =>
        {
            var book = rt.State.Autopilot ??= new AutopilotBook();
            var ap = new AutopilotWorld(rt.State, link.Context(), link.Desk, book, AutopilotContent.From(content))
            {
                TravelOf = () => null,                                  // the main thread owns the journey; the autopilot walks the plain way here
                PositionHook = id => { lock (_apPosLock) return id == HeroId ? _apPos : null; },
            };
            return ap;
        });
    }

    void BuildAutopilotHud(VBoxContainer v)
    {
        _apChip = new Button { Visible = false, CustomMinimumSize = new Vector2(0, 26), Alignment = HorizontalAlignment.Left, FocusMode = FocusModeEnum.None, ClipText = true };
        _apChip.Pressed += ApChipClicked;
        v.AddChild(_apChip);
        _apBanner = new Label { Visible = false, Modulate = new Color(1f, 0.82f, 0.45f) };
        v.AddChild(_apBanner);
    }

    void ApChipClicked()
    {
        if (_ap == null || Runtime is not { } rt) return;
        long now = _world.Clock.Now.Seconds;
        if (_apView.AlertPaused) { ApContinue(); return; }
        if (_apView.Paused || _apLocalPaused) { _apLocalPaused = false; _ = Bar(rt, () => { _ap.Resume(HeroId, now); return 0; }); _router?.ShowToast(L.T("AP_RESUMED")); }
        else ApTakeControl(null);
    }

    void ApContinue()
    {
        if (_ap == null || Runtime is not { } rt) return;
        _ = Bar(rt, () => { _ap.BusOf(_ap.Book.Get(HeroId)!).Resume(); return 0; });
        if (_world.Clock.Speed == SimSpeed.Paused) _rec.SetSpeed(SimSpeed.X1);
        if (_apAlert != null) _apAlert.Visible = false;
    }

    bool ApActive => _ap != null && _apView.Enabled && !_apView.Paused && !_apLocalPaused;

    /// <summary>Takes the hero back at once (a movement key, a click, the trade or talk screen). The directive and its progress are kept; Y opens the panel to change it or resume.</summary>
    void ApTakeControl(string? toast)
    {
        if (!ApActive || Runtime is not { } rt || _ap == null) return;
        _apLocalPaused = true; _apMoveCool = 0;
        long now = _world.Clock.Now.Seconds;
        _rec.Action(Ravensguard.Sim.Diagnostics.HeroAction.TakeControl, 0, now);
        _ = Bar(rt, () => { _ap.TakeControl(HeroId, now); return 0; });
        if (toast != null) _router?.ShowToast(toast);
        ApHud();
    }

    // ------------------------------------------------------------------ per frame

    void AutopilotTick(double delta)
    {
        if (_apInit is { IsCompleted: true } init)
        {
            _ap = init.IsCompletedSuccessfully ? init.Result : null; _apInit = null;
            if (_ap != null) { var st = _ap.Book.Get(HeroId); if (st != null) _apView = AutopilotView.None with { Exists = true, Enabled = st.Enabled, Paused = st.Paused }; }
        }
        if (_ap == null || Runtime is not { } rt) return;
        lock (_apPosLock) _apPos = HeroRegionMm();
        long now = _world.Clock.Now.Seconds;
        if (_apTask is { IsCompleted: true } t)
        {
            _apTask = null;
            if (t.IsCompletedSuccessfully) { _apView = t.Result.View; if (_apView.Paused) _apLocalPaused = true; else if (!_apView.Enabled) _apLocalPaused = false; }
        }
        // one barrier about a real second apart (never while the game is paused: no game time passes, none is made up)
        if (_apTask == null && _world.Clock.Speed != SimSpeed.Paused && (_world.Tick - _apTick >= 20 || _apView.Wanted == null && _world.Tick - _apTick >= 10))
        {
            _apTick = _world.Tick;
            var ap = _ap; bool run = !_apLocalPaused;
            _apTask = rt.AtBarrier(() => { if (run) ap.Advance(now); return new ApResult(ap.View(HeroId, now)); });
        }
        // the hero walks where the AI wants (at most one new path every little while: a click plans one path, not one per frame)
        _apMoveCool -= delta;
        if (ApActive && _apView.Wanted is { } w && _apMoveCool <= 0 && !_apAlertOpen) { ApWalk(w.X, w.Y); _apMoveCool = 0.5; }
        // an alert that paused the game, a notify that slows it down
        if (_apView.AlertPaused && _world.Clock.Speed != SimSpeed.Paused) { _rec.SetSpeed(SimSpeed.Paused); ShowApAlert(); }
        else if (ApActive && _apView.SlowUntilSec > now && _world.Clock.Speed > SimSpeed.X4) _rec.SetSpeed(SimSpeed.X4);
        ApHud();
    }

    bool _apAlertOpen => _apAlert is { Visible: true };

    void ApWalk(long rx, long ry)
    {
        var (hx, hy) = HeroRegionMm();
        long dx = rx - hx, dy = ry - hy;
        if (Math.Abs(dx) < 1500 && Math.Abs(dy) < 1500) return;
        var h = _world.Snapshot.Hero;
        var cell = new Cell3(h.CellX + (int)Math.Round(dx / 1000.0), h.CellY + (int)Math.Round(dy / 1000.0), h.Z);
        bool ok = _world.Grid.InBounds(cell.X, cell.Y, cell.Z) && _rec.CommandMoveTo(cell);
        if (ok) { _apMoveFail = 0; return; }
        if (++_apMoveFail >= 3)                       // no path in the loaded ground: the abstract way, a step along the line (what the journey does)
        {
            long step = 4000, len = Math.Max(1, PathPlan.Dist(dx, dy)), nx = hx + dx * Math.Min(step, len) / len, ny = hy + dy * Math.Min(step, len) / len;
            _rec.TravelPlace(nx, ny, Math.Abs(dx) >= Math.Abs(dy) ? (dx < 0 ? Facing.West : Facing.East) : (dy < 0 ? Facing.North : Facing.South));
        }
    }

    void ApHud()
    {
        if (_apChip == null) return;
        bool on = _apView.Enabled;
        _apChip.Visible = on;
        if (on)
        {
            var v = _apView with { Paused = _apView.Paused || _apLocalPaused };
            _apChip.Text = "[" + v.Chip(L.T("AP_CHIP_OFF")) + "]";
            _apChip.Modulate = v.AlertPaused ? new Color(1f, 0.55f, 0.45f) : v.Paused ? new Color(1f, 0.82f, 0.45f) : new Color(0.75f, 0.95f, 0.7f);
        }
        if (_apBanner != null) { _apBanner.Visible = on && (_apView.Paused || _apLocalPaused); _apBanner.Text = L.F("AP_BANNER", H("world.autopilot")); }
    }

    void ShowApAlert()
    {
        if (_apAlert == null)
        {
            _apAlert = new PanelContainer { Visible = false, Position = new Vector2(380, 200), CustomMinimumSize = new Vector2(520, 0) };
            var v = new VBoxContainer(); v.AddThemeConstantOverride("separation", 8); _apAlert.AddChild(v);
            v.AddChild(new Label { Text = L.T("AP_ALERT"), ThemeTypeVariation = "TitleLabel" });
            _apAlertText = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart }; v.AddChild(_apAlertText);
            var row = new HBoxContainer(); row.AddThemeConstantOverride("separation", 8); v.AddChild(row);
            var take = new Button { Text = L.T("AP_ALERT_TAKE") }; take.Pressed += () => { ApContinue(); ApTakeControl(null); }; row.AddChild(take);
            var go = new Button { Text = L.T("AP_ALERT_GO") }; go.Pressed += ApContinue; row.AddChild(go);
            var jr = new Button { Text = L.T("AP_ALERT_JOURNAL") }; jr.Pressed += () => { ApContinue(); OpenJournal(); }; row.AddChild(jr);
            GetNode("Hud").AddChild(_apAlert);
        }
        _apAlertText!.Text = _apView.AlertText;
        _apAlert.Visible = true;
    }

    // ------------------------------------------------------------------ the panel

    AutopilotPanelHost ApHost()
    {
        var rt = Runtime!; var ap = _ap!; var cfg = ap.Cfg.Settings;
        _apSetup ??= NewApSetup(ap);
        var region = rt.State.Population.Region;
        var places = region.Settlements.Select((s, i) => (s.Name, s.CenterX * 1000L, s.CenterY * 1000L, i)).ToList();
        return new AutopilotPanelHost
        {
            View = () => _apView with { Paused = _apView.Paused || _apLocalPaused },
            Setup = () => _apSetup!,
            Places = () => places,
            GoodId = id => rt.State.Economy!.Goods.IdOf(id),
            Skills = () => ap.Cfg.Activities.Skills,
            Start = s =>
            {
                _apSetup = s;
                var (hx, hy) = HeroRegionMm();
                if (s.Limits.HomeByNightfall && s.Limits.HomeX == 0 && s.Limits.HomeY == 0) { s.Limits.HomeX = hx; s.Limits.HomeY = hy; }
                var copy = new AutopilotSetup { Queue = [.. s.Queue.Select(d => d.Clone())], Limits = s.Limits.Clone(), Policy = (InterruptPolicy[])s.Policy.Clone(), Quiet = (bool[])s.Quiet.Clone() };
                long now = _world.Clock.Now.Seconds; _apLocalPaused = false;
                _rec.Action(Ravensguard.Sim.Diagnostics.HeroAction.Directive, copy.Queue.Count, now);
                _ = Bar(rt, () => { ap.Apply(HeroId, now, copy); return 0; });
            },
            Stop = () => { long now = _world.Clock.Now.Seconds; _apLocalPaused = false; _rec.Action(Ravensguard.Sim.Diagnostics.HeroAction.DirectiveStop, 0, now); _ = Bar(rt, () => { ap.Disable(HeroId, now); return 0; }); },
            PauseResume = () => ApChipClicked(),
            Preset = (name, s) => s.Preset(name, AutopilotSettingsDefLike.Of(cfg)),
            MaxRiskLow = () => cfg.RiskLowPm, MaxRiskMedium = () => cfg.RiskMediumPm, MaxRiskHigh = () => cfg.RiskHighPm,
        };
    }

    static AutopilotSetup NewApSetup(AutopilotWorld ap)
    {
        var s = new AutopilotSetup();
        s.Limits.SpendCapMitesPerDay = ap.Cfg.Settings.DefaultSpendCapMitesPerDay; s.Limits.MaxRiskPm = ap.Cfg.Settings.DefaultMaxRiskPm; s.Limits.PurseReserveMites = ap.Cfg.Settings.DefaultPurseReserveMites;
        s.Queue.Add(Directive.Work(0, L.T("AP_D_WORK")));
        return s;
    }

    void OpenAutopilot()
    {
        if (_router == null || _ap == null || Runtime == null) { _router?.ShowToast(L.T("MAP_NONE")); return; }
        _router.Push(new AutopilotPanel(_router, ApHost()) { ShotPath = _opt.Panel == "autopilot" ? _opt.ShotPath : null });
    }

    // ------------------------------------------------------------------ dev shots (--shot-panel autopilot | autopilot-work)

    void ApShots()
    {
        if (_opt.Panel is not ("autopilot" or "autopilot-work")) return;
        if (_frames % 60 == 0) GD.Print($"[autopilot] frame {_frames}: ap {(_ap != null ? "ready" : "none")}, view enabled {_apView.Enabled} paused {_apView.Paused} active '{_apView.Active}', wanted {_apView.Wanted}, job {(_jobSnap?.Live.Any(j => j.Worker == HeroId) == true)}");
        if (_ap == null) return;
        if (_opt.Panel == "autopilot" && _frames == 150) OpenAutopilot();
        if (_opt.Panel != "autopilot-work") return;
        if (_frames == 140)
        {
            var host = ApHost(); var s = host.Setup(); s.Queue.Clear(); s.Queue.Add(Directive.Work(0, L.T("AP_D_WORK")));
            host.Preset("worker", s); host.Start(s);
            _rec.SetWantedSpeed(SimSpeed.X8); _rec.SetSpeed(SimSpeed.X8);
        }
        // the picture a little after the hero holds a job (or, failing that, late)
        bool has = _jobSnap?.Live.Any(j => j.Worker == HeroId) == true;
        if (has && _apShotAt < 0) { _apShotAt = _frames + 45; _rec.SetSpeed(SimSpeed.X2); }
        if (!_quitting && ((_apShotAt >= 0 && _frames >= _apShotAt) || _frames >= 1500)) { ApHud(); GD.Print($"[autopilot] work shot at frame {_frames}"); Shoot(); }
    }
}
