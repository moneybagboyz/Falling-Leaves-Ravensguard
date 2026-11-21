using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Ravensguard.Game.Inputs;
using Ravensguard.Game.UI.NewGame;
using Ravensguard.Sim.Jobs;
using Ravensguard.Sim.Live;

namespace Ravensguard.Game.UI;

/// <summary>What the work panel shows: your job, the person next to you, and the jobs listed where you stand (live-behaviour spec 6). Plain data, filled by the street view.</summary>
public sealed class JobPanelData
{
    public Job? Mine;
    public long NowSec;
    public string NearName = "", NearWhy = "";
    /// <summary>"Notice board" in a town, "What the reeve and the innkeeper say" in a village.</summary>
    public bool Board;
    public string PlaceName = "";
    public List<Job> Open = [];
    /// <summary>Hero position in region millimetres.</summary>
    public long HeroX, HeroY;
}

/// <summary>
/// The minimal work panel (J, or Q for "what can I do here"): the job you hold with its step, deadline and pay and a way to abandon it; the person beside you and what they are doing and why; and the odd jobs on
/// the board of the town (word of mouth in a village) with pay, distance, deadline and a risk tag. There is no trade UI yet (step 4): this is only the way to take a paid job and see it through.
/// </summary>
public partial class JobPanel : Control
{
    readonly ScreenRouter _router;
    readonly Func<JobPanelData> _data;
    readonly Action<Job> _take;
    readonly Action<Job> _abandon;
    VBoxContainer _body = null!;
    double _refresh;
    public string? ShotPath { get; init; }

    public JobPanel(ScreenRouter router, Func<JobPanelData> data, Action<Job> take, Action<Job> abandon) { _router = router; _data = data; _take = take; _abandon = abandon; }

    public override void _Ready()
    {
        var margin = UiKit.Margin(this, 14);
        var root = new VBoxContainer(); root.AddThemeConstantOverride("separation", 8); margin.AddChild(root);
        root.AddChild(UiKit.Title(L.T("JOBS_TITLE")));
        _body = new VBoxContainer(); _body.AddThemeConstantOverride("separation", 6);
        var p = UiKit.Panel(UiKit.Scroll(_body), 0, 0); p.SizeFlagsVertical = SizeFlags.ExpandFill; root.AddChild(p);      // the board scrolls; Back stays on screen
        var back = UiKit.Key(L.T("UI_BACK"), "menu.back", 200); back.Pressed += _router.Pop; root.AddChild(back);
        Build();
    }

    int _shotWait;

    public override void _Process(double delta)
    {
        _refresh += delta; if (_refresh > 1.0) { _refresh = 0; Build(); }
        if (ShotPath == null || !Visible || ++_shotWait != 8) return;
        var img = GetViewport().GetTexture().GetImage();
        System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(ShotPath) ?? ".");
        GD.Print($"[jobs] screenshot {ShotPath}: {img.SavePng(ShotPath)}");
        GetTree().Quit();
    }

    static string Pay(long mites) => UiKit.Money(mites);
    static string Clock(long sec) { long s = sec % 86400; return $"{s / 3600:00}:{s % 3600 / 60:00}"; }

    static string Left(long sec) => sec <= 0 ? "now" : sec < 2 * 86400 ? $"{(sec + 3599) / 3600} h" : $"{sec / 86400} days";

    static string Risk(Job j) => j.Kind is OddJobKind.Guard or OddJobKind.Escort ? L.T("JOBS_RISK") : "-";

    void Build()
    {
        foreach (var c in _body.GetChildren()) { _body.RemoveChild(c); c.QueueFree(); }
        var d = _data();
        _body.AddChild(UiKit.Hint(L.T("JOBS_MINE")));
        if (d.Mine is { } m)
        {
            var (_, _, phase, _) = JobTable.PositionAt(m, d.NowSec);
            long end = JobTable.EndSec(m);
            _body.AddChild(UiKit.Body(L.F("JOBS_ACTIVE", m.Title.Length > 0 ? m.Title : m.Kind.ToString(), Phase(m, phase), Clock(end), Pay(m.WageMites))));
            var ab = new Button { Text = L.T("JOBS_ABANDON"), CustomMinimumSize = new Vector2(0, 30) }; ab.Pressed += () => { _abandon(m); Build(); }; _body.AddChild(ab);
            _body.AddChild(UiKit.Hint(L.T("JOBS_ABANDON_NOTE"), true));
        }
        else _body.AddChild(UiKit.Body(L.T("JOBS_NONE")));
        if (d.NearName.Length > 0) { _body.AddChild(UiKit.Hint(L.T("JOBS_NEAR"))); _body.AddChild(UiKit.Body($"{d.NearName}: {d.NearWhy}")); }
        _body.AddChild(UiKit.Hint(L.F(d.Board ? "JOBS_BOARD" : "JOBS_WORD", d.PlaceName)));
        if (d.Open.Count == 0) _body.AddChild(UiKit.Body(L.T("JOBS_EMPTY")));
        foreach (var j in d.Open.Take(8))
        {
            long dist = PathPlan.Dist(j.P0 - d.HeroX, j.P1 - d.HeroY) / 1000;
            var row = new HBoxContainer(); row.AddThemeConstantOverride("separation", 8);
            row.AddChild(new Label { Text = L.F("JOBS_ROW", j.Title.Length > 0 ? j.Title : j.Kind.ToString(), Pay(j.WageMites), dist, Left(j.DeadlineSec - d.NowSec), Risk(j)), SizeFlagsHorizontal = SizeFlags.ExpandFill, ClipText = true });
            var take = new Button { Text = L.T("JOBS_TAKE"), Disabled = d.Mine != null, CustomMinimumSize = new Vector2(90, 28) };
            take.Pressed += () => { _take(j); _router.ShowToast(L.T("JOBS_TAKEN")); Build(); };
            row.AddChild(take); _body.AddChild(row);
        }
    }

    static string Phase(Job j, int phase) => JobTable.Phases(j)[Math.Min(phase, JobTable.Phases(j).Length - 1)].Kind switch
    {
        PhaseKind.Go => L.T("JOBS_STEP_GO"), PhaseKind.Load => L.T("JOBS_STEP_LOAD"), PhaseKind.Carry => L.T("JOBS_STEP_CARRY"), PhaseKind.Unload => L.T("JOBS_STEP_UNLOAD"),
        PhaseKind.Work => L.T("JOBS_STEP_WORK"), _ => L.T("JOBS_STEP_DONE"),
    };

    public override void _UnhandledInput(InputEvent e)
    {
        if (!Visible) return;
        if (Actions.Pressed(e, "world.panel_journal") || Actions.Pressed(e, "world.context_menu")) _router.Pop();
        else return;
        GetViewport().SetInputAsHandled();
    }
}
