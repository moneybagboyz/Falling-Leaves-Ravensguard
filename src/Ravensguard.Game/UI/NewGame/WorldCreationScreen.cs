using System;
using System.Collections.Generic;
using Godot;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Save;
using Ravensguard.Sim.WorldGen;
using Ravensguard.Sim.Worlds;

namespace Ravensguard.Game.UI.NewGame;

/// <summary>
/// World creation (or opening a library world): history runs on a worker thread and unfolds here as a scrolling chronicle with a progress bar,
/// while the continent map fills in and the realm borders move (plan §1A Stage 3).
/// </summary>
public partial class WorldCreationScreen : Control
{
    const int Lines = 12;
    readonly ScreenRouter _router;
    readonly NewGameDraft? _draft;
    readonly WorldInfo? _open;
    readonly WorldPurpose _purpose;
    WorldJob _job = null!;
    TextureRect _map = null!;
    ProgressBar _bar = null!;
    Label _status = null!, _stats = null!, _error = null!;
    readonly Label[] _lines = new Label[Lines];
    readonly List<string> _log = new();
    Continent? _continent;
    byte[]? _owner;
    int _entries, _year, _years = 1;
    bool _mapDirty, _done;
    double _sinceMap;

    public WorldCreationScreen(ScreenRouter router, NewGameDraft draft, WorldPurpose purpose) { _router = router; _draft = draft; _purpose = purpose; }
    public WorldCreationScreen(ScreenRouter router, WorldInfo open, WorldPurpose purpose) { _router = router; _open = open; _purpose = purpose; }

    public override void _Ready()
    {
        var margin = UiKit.Margin(this, 8);
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 10);
        margin.AddChild(row);
        _map = new TextureRect { CustomMinimumSize = new Vector2(ContinentMap.Px, ContinentMap.Px), StretchMode = TextureRect.StretchModeEnum.Keep, TextureFilter = TextureFilterEnum.Nearest };
        var black = new byte[ContinentMap.Px * ContinentMap.Px * 4];
        for (int i = 3; i < black.Length; i += 4) black[i] = 255;
        _map.Texture = UiKit.Tex(black, ContinentMap.Px, ContinentMap.Px);
        var frame = UiKit.Panel(_map);
        frame.AddThemeStyleboxOverride("panel", PixelTheme.PanelBox(new Color("141118"), PixelTheme.Rim, 2));
        row.AddChild(frame);

        var v = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        v.AddThemeConstantOverride("separation", 8);
        row.AddChild(v);
        string name = _draft != null ? (_draft.LibraryWorldId == null ? _draft.WorldName : _draft.LibraryWorldId) : _open!.Name;
        v.AddChild(UiKit.Title(L.T(_draft is { LibraryWorldId: null } ? "WC_TITLE_NEW" : "WC_TITLE_OPEN")));
        v.AddChild(new Label { Text = _draft is { LibraryWorldId: null } ? _draft.WorldName : _open?.Name ?? _draft!.LibraryWorldId, ThemeTypeVariation = "GoldLabel" });
        _status = new Label { Text = L.T("WC_STAGE_LAYOUT") };
        v.AddChild(_status);
        _bar = new ProgressBar { MaxValue = 100, ShowPercentage = false, CustomMinimumSize = new Vector2(0, 18) };
        v.AddChild(_bar);
        _stats = UiKit.Hint("");
        v.AddChild(_stats);
        v.AddChild(UiKit.Hint(L.T("WC_CHRONICLE")));
        var lines = new VBoxContainer { SizeFlagsVertical = SizeFlags.ExpandFill, ClipContents = true };
        lines.AddThemeConstantOverride("separation", 1);
        for (int i = 0; i < Lines; i++)
        {
            _lines[i] = new Label { ClipText = true, TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis, CustomMinimumSize = new Vector2(0, 18) };
            lines.AddChild(_lines[i]);
        }
        var lp = UiKit.Panel(lines);
        lp.SizeFlagsVertical = SizeFlags.ExpandFill;
        v.AddChild(lp);
        _error = new Label { ThemeTypeVariation = "WarnLabel", AutowrapMode = TextServer.AutowrapMode.WordSmart };
        v.AddChild(_error);
        var cancel = UiKit.Key(L.T("UI_CANCEL"), "menu.back", 220);
        cancel.Pressed += _router.Pop;
        v.AddChild(cancel);

        var content = NewGameServices.Content();
        if (_draft is { LibraryWorldId: null } && NewGameServices.CheckLibraryWritable() is { } bad)
        {
            // checked before the (long) generation starts, so it never ends in a world that cannot be saved
            _done = true; _error.Text = L.F("WC_LIB_UNWRITABLE", NewGameServices.Library.Root, bad);
            return;
        }
        if (_draft is { LibraryWorldId: null }) _job = WorldJob.Create(_draft.WorldName, _draft.Continent, _draft.History, content.Hash, NewGameServices.NowText());
        else
        {
            string id = _open?.Id ?? _draft!.LibraryWorldId!;
            var report = NewGameServices.Library.TryRead(id) is { } info ? LoadReport.ForWorld(info.File, content.Hash) : null;
            if (report is { NeedsConfirmation: true })
            {
                // a world made with other content: told first (plan 2.11); its land and history still have to regenerate as they were, and it is refused with the cause if they do not
                _done = true;                                                                  // nothing runs until the player continues
                LoadReportDialog.Ask(_router, report, true,
                    () => { _done = false; _job = WorldJob.Open(id, content.Hash); },
                    () => { _router.Pop(); });
            }
            else _job = WorldJob.Open(id, content.Hash);
        }
    }

    public override void _ExitTree() => _job?.Cancel();

    public override void _Process(double delta)
    {
        if (_job == null || _done) return;
        int n = 0;
        while (n++ < 400 && _job.Progress.TryDequeue(out var p)) Take(p);
        _sinceMap += delta;
        if (_mapDirty && _continent != null && _sinceMap > 0.12) { RenderMap(); _sinceMap = 0; }
        for (int i = 0; i < Lines; i++) _lines[i].Text = i < _log.Count ? _log[_log.Count - Math.Min(_log.Count, Lines) + i] : "";
        _stats.Text = L.F("WC_STATS", _year, _years, _entries);
        if (!_job.Finished || !_job.Progress.IsEmpty) return;
        _done = true;
        if (_job.Error is OperationCanceledException) return;
        if (_job.Error != null) { _error.Text = L.F("WC_FAILED", _job.Error.Message); GD.PushWarning("world creation failed: " + _job.Error); return; }
        _bar.Value = 100;
        if (_continent != null) { _owner = _job.Result!.History.FinalOwner; RenderMap(); }
        var w = _job.Result!;
        NewGameServices.Remember(w);
        if (_purpose == WorldPurpose.Legends) _router.Replace(new LegendsScreen(_router, w));
        else NewGameFlow.AfterWorld(_router, _draft!, w, replace: true);
    }

    void Take(WorldGenProgress p)
    {
        _bar.Value = p.Fraction * 100;
        _years = Math.Max(1, p.Years);
        if (p.Phase == WorldGenPhase.Continent) _status.Text = L.T("WC_STAGE_" + p.Stage.ToUpperInvariant());
        else if (p.Phase == WorldGenPhase.History)
        {
            _status.Text = L.T("WC_STAGE_HISTORY");
            _year = p.Year;
        }
        if (p.Continent != null) { _continent = p.Continent; _mapDirty = true; }
        if (p.Owner != null) { _owner = p.Owner; _mapDirty = true; }
        if (p.Entry is { } e)
        {
            _entries++;
            _log.Add($"{UiKit.SeasonName(e.When.Season)} {e.When.Year}: {e.Data.GetValueOrDefault("title", e.TypeId)} - {e.Data.GetValueOrDefault("text", "").TrimEnd('.')}");
            if (_log.Count > 4 * Lines) _log.RemoveRange(0, _log.Count - Lines);
        }
    }

    void RenderMap()
    {
        _mapDirty = false;
        var bytes = ContinentMap.Render(_continent!, null, new MapOptions(MapLayer.Realm, Rivers: true, Towns: false, Legends: false, OwnerOverride: _owner));
        _map.Texture = UiKit.Tex(bytes, ContinentMap.Px, ContinentMap.Px);
    }
}
