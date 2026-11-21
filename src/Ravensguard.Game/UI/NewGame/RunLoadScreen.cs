using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Godot;
using Ravensguard.Sim.Codex;
using Ravensguard.Sim.Save;
using Ravensguard.Sim.Worlds;
using Ravensguard.Game.World;

namespace Ravensguard.Game.UI.NewGame;

/// <summary>
/// Loading a saved run: the world's continent and history (from the cache or regenerated and verified), the save, the region and people rebuilt from it
/// (<see cref="WorldSave.Restore"/>), the Codex of the world with the run's progress, then the street view with the hero standing where the save left it.
/// Everything slow runs on a worker; engine and shared state are read on the main thread first.
/// </summary>
public partial class RunLoadScreen : Control
{
    const int Steps = 4;
    sealed record Inputs(RunEntry Entry, ContentBundle Content, WorldLibrary Library, LoadedWorld? Cached, System.Collections.Generic.List<CodexRule> Rules);
    sealed record Loaded(WorldState State, SaveHeader Header, LoadedWorld World, CodexBook Codex, string StartSettlement, WorldRuntime Runtime);

    readonly ScreenRouter _router;
    readonly RunEntry _entry;
    readonly Label[] _step = new Label[Steps];
    ProgressBar _bar = null!;
    Label _error = null!;
    Task<Loaded>? _task;
    Inputs? _in;
    readonly CancellationTokenSource _cts = new();
    volatile int _at;
    bool _done;
    volatile LoadReport? _ask;                                  // set by the worker when the content changed; the main thread shows it, the worker waits for the answer
    bool _asked;
    readonly TaskCompletionSource<bool> _answer = new();

    RunLoadScreen(ScreenRouter router, RunEntry entry) { _router = router; _entry = entry; }

    /// <summary>Opens the loading screen for a save (it replaces itself with the street view when ready).</summary>
    public static void Open(ScreenRouter router, RunEntry entry) => router.Push(new RunLoadScreen(router, entry));

    public override void _ExitTree() => _cts.Cancel();

    public override void _Ready()
    {
        var center = new CenterContainer();
        center.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(center);
        var v = new VBoxContainer { CustomMinimumSize = new Vector2(480, 0) };
        v.AddThemeConstantOverride("separation", 10);
        center.AddChild(v);
        v.AddChild(UiKit.Title(L.T("LOAD_TITLE")));
        v.AddChild(new Label { Text = RunCatalog.Title(_entry), ThemeTypeVariation = "GoldLabel" });
        for (int i = 0; i < Steps; i++) { _step[i] = new Label(); v.AddChild(_step[i]); }
        _bar = new ProgressBar { MaxValue = Steps, ShowPercentage = false, CustomMinimumSize = new Vector2(0, 18) };
        v.AddChild(_bar);
        _error = new Label { ThemeTypeVariation = "WarnLabel", AutowrapMode = TextServer.AutowrapMode.WordSmart };
        v.AddChild(_error);
        var back = UiKit.Key(L.T("UI_BACK"), "menu.back", 220);
        back.Pressed += _router.Pop;
        v.AddChild(back);

        try
        {
            var content = NewGameServices.Content();
            _in = new Inputs(_entry, content, NewGameServices.Library, NewGameServices.Cached(_entry.WorldId), CodexRules.FromInput());
        }
        catch (Exception e) { _done = true; _error.Text = L.F("LOAD_FAILED", e.Message); Refresh(); return; }
        var inputs = _in; var ct = _cts.Token;
        _task = Task.Run(() => Load(inputs, ct), ct);
        Refresh();
    }

    Loaded Load(Inputs bi, CancellationToken ct)
    {
        _at = 0;
        var lib = bi.Library; var content = bi.Content;
        var world = bi.Cached ?? lib.Load(bi.Entry.WorldId, content.Hash, null, ct);
        ct.ThrowIfCancellationRequested();
        _at = 1;
        var file = new SaveReader().Load(bi.Entry.Path);
        ct.ThrowIfCancellationRequested();
        // the pre-load report (plan 2.11): refused with its cause, or shown for Continue / Cancel before anything is loaded
        // a save from before the game recorded its mods holds none: nothing to compare then, so it is not reported as every mod added
        var mods = file.Header.Mods.Count == 0 ? null : content.Installed;
        var report = WorldSave.Inspect(file, content.Db, content.Hash, mods, world.Continent, world.History);
        if (!report.CanLoad) throw new SaveException(report.ToText());
        foreach (var notice in report.Notices) GD.PushWarning(notice);   // P7-CN1: informational, never asks (coinage 2.3)
        if (report.NeedsConfirmation)
        {
            _ask = report;
            if (!_answer.Task.WaitAsync(ct).GetAwaiter().GetResult()) throw new OperationCanceledException(ct);
        }
        ct.ThrowIfCancellationRequested();
        _at = 2;
        var state = WorldSave.Restore(file, content.Db, content.Hash, mods, world.Continent, world.History);
        state.Content = content.Db; // the next save diffs the people against the regenerated ones (note N24)
        foreach (var note in state.LoadNotes) GD.PushWarning(note);   // content ids mapped by name, goods written off (ContentRemap)
        ct.ThrowIfCancellationRequested();
        _at = 3;
        // the abstract world of the run: the home region's records from their sections, the other regions' sections waiting in a folder until the hero comes near
        var runtime = new WorldRuntime(state, content.Db, StreetSource.MakePager());
        try
        {
            if (ct.IsCancellationRequested) ct.ThrowIfCancellationRequested();
            var region = state.Population.Region;
            var codex = CodexBook.Build(world.History, region, bi.Rules);
            if (state.Live == null && state.Codex != null) codex.Apply(state.Codex); // a save made before the live section existed (the build's first save)
            string start = "";
            if (state.Live == null)
            {
                var origin = lib.ReadRunInfo(bi.Entry.WorldId, RunCatalog.RunOf(bi.Entry.Slot) is { Length: > 0 } runOf ? runOf : bi.Entry.Slot) is { } info && content.Db.TryGet<Ravensguard.Content.OriginDef>(info.OriginId, out var o) ? o : null;
                start = RegionBuildScreen.ChooseStart(region, origin);
            }
            return new Loaded(state, file.Header, world, codex, start, runtime);
        }
        catch { runtime.Dispose(); throw; } // a failed or cancelled load leaves no worker and no page folder behind
    }

    void Refresh()
    {
        string[] keys = ["LOAD_STEP_WORLD", "LOAD_STEP_SAVE", "LOAD_STEP_REGION", "LOAD_STEP_VIEW"];
        for (int i = 0; i < Steps; i++) _step[i].Text = (i < _at ? "[x] " : i == _at ? "[>] " : "[ ] ") + L.T(keys[i]);
        _bar.Value = _at;
    }

    public override void _Process(double delta)
    {
        if (_task == null || _done) return;
        if (_ask is { } ask && !_asked)
        {
            _asked = true;
            LoadReportDialog.Ask(_router, ask, false, () => _answer.TrySetResult(true), () => { _answer.TrySetResult(false); _done = true; _router.Pop(); });
        }
        Refresh();
        if (!_task.IsCompleted) return;
        _done = true;
        if (_cts.IsCancellationRequested || _task.IsCanceled) return;
        if (_task.IsFaulted)
        {
            var ex = _task.Exception!.GetBaseException();
            _error.Text = L.F("LOAD_FAILED", ex.Message);
            GD.PushWarning("run load failed: " + ex);
            return;
        }
        var r = _task.Result;
        _at = Steps; Refresh();
        NewGameServices.Remember(r.World);
        var lib = _in!.Library;
        string slot = _entry.Slot;
        string runName = RunCatalog.ManualNameFor(slot, r.State.Hero?.Name, lib.ListRuns(_entry.WorldId).Count);
        var session = new RunSession(r.State, runName, r.Header.Ironman, _entry.Path, lib, _in.Content.Hash, _entry.WorldId, r.Runtime);
        var opt = new StreetOptions { Poi = r.State.Live == null ? "at:" + r.StartSettlement : null };
        var view = StreetView.ForRegion(r.State.Population.Region, r.State.Population, _in.Content.Db, _router, opt, r.State.Chunks,
            hero: r.State.Hero, codex: r.Codex, history: r.World.History, continent: r.World.Continent, resume: r.State.Live, regionSettings: r.State.RegionSettings, runtime: r.Runtime);
        view.Run = session;
        _router.Replace(view);
    }
}
