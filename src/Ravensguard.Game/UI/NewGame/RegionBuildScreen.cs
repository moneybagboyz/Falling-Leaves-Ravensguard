using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Godot;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.People;
using Ravensguard.Sim.Save;
using Ravensguard.Sim.WorldGen;
using Ravensguard.Sim.WorldGen.Region;
using Ravensguard.Sim.Worlds;
using RegionData = Ravensguard.Sim.WorldGen.Region.Region;

namespace Ravensguard.Game.UI.NewGame;

/// <summary>What the new-game flow hands to the street view: the populated world, the choices, and where the run was saved.</summary>
public sealed record RunStart(WorldState World, NewGameDraft Draft, EmbarkScore Site, string StartSettlement, string? SavePath, Ravensguard.Sim.Codex.CodexBook? Codex = null,
    string RunName = "", string? SaveError = null);

/// <summary>Everything the worker thread needs that lives on the engine or in shared state, read once on the main thread before the task starts.</summary>
sealed record BuildInputs(ContentBundle Content, LoadedWorld World, NewGameDraft Draft, WorldLibrary Library, string RunName, string Now, System.Collections.Generic.List<Ravensguard.Sim.Codex.CodexRule> Rules, Ravensguard.Content.OriginDef? Origin, string ContentHash);

/// <summary>Region build step: terrain, settlements and people for the chosen square on a worker thread with progress, then the first save and the street view.</summary>
public partial class RegionBuildScreen : Control
{
    const int Steps = 4;
    readonly ScreenRouter _router;
    readonly NewGameDraft _d;
    readonly LoadedWorld _w;
    readonly Label[] _step = new Label[Steps];
    ProgressBar _bar = null!;
    Label _site = null!, _error = null!;
    Task<RunStart>? _task;
    readonly CancellationTokenSource _cts = new();
    BuildInputs? _in;
    volatile int _at;
    bool _done;

    public RegionBuildScreen(ScreenRouter router, NewGameDraft d, LoadedWorld w) { _router = router; _d = d; _w = w; }

    /// <summary>Latest run built by this flow (read by the street view hand-off); cleared when that street view exits, or when this screen is left before the build finished.</summary>
    public static RunStart? Latest { get; private set; }
    public static void ClearLatest() => Latest = null;

    public override void _ExitTree()
    {
        _cts.Cancel(); // the worker stops at its next step boundary and writes nothing after this
        if (!_done) Latest = null;
    }

    public override void _Ready()
    {
        var center = new CenterContainer();
        center.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(center);
        var v = new VBoxContainer { CustomMinimumSize = new Vector2(480, 0) };
        v.AddThemeConstantOverride("separation", 10);
        center.AddChild(v);
        v.AddChild(UiKit.Title(L.T("RB_TITLE")));
        _site = new Label { ThemeTypeVariation = "GoldLabel" };
        v.AddChild(_site);
        for (int i = 0; i < Steps; i++) { _step[i] = new Label(); v.AddChild(_step[i]); }
        _bar = new ProgressBar { MaxValue = Steps, ShowPercentage = false, CustomMinimumSize = new Vector2(0, 18) };
        v.AddChild(_bar);
        _error = new Label { ThemeTypeVariation = "WarnLabel", AutowrapMode = TextServer.AutowrapMode.WordSmart };
        v.AddChild(_error);
        var back = UiKit.Key(L.T("UI_BACK"), "menu.back", 220);
        back.Pressed += _router.Pop;
        v.AddChild(back);

        var content = NewGameServices.Content();
        var w = _w; var d = _d;
        _site.Text = d.Embark != null ? L.F("RB_SITE", d.Embark.CellX, d.Embark.CellY) : L.T("RB_SITE_AUTO");
        // everything the worker needs from the engine or from shared state is read here, on the main thread
        var lib = NewGameServices.Library;
        if (NewGameServices.CheckLibraryWritable(lib) is { } bad) { _done = true; _error.Text = L.F("RB_FAILED", bad); return; }
        string runName = WorldLibrary.Slug(d.Name) + "-" + (lib.ListRuns(w.Info.Id).Count + 1);
        content.Db.TryGet<Ravensguard.Content.OriginDef>(d.OriginId, out var origin);
        _in = new BuildInputs(content, w, d, lib, runName, NewGameServices.NowText(), CodexRules.FromInput(), origin, content.Hash);
        var inputs = _in; var ct = _cts.Token;
        _task = Task.Run(() => Build(inputs, ct), ct);
        Refresh();
    }

    RunStart Build(BuildInputs bi, CancellationToken ct)
    {
        var content = bi.Content; var w = bi.World; var d = bi.Draft;
        _at = 0;
        var site = d.Embark ?? EmbarkSite.AutoPick(w.Continent, w.History);
        ct.ThrowIfCancellationRequested();
        _at = 1;
        var rs = RegionSettings.LoadDefaults(content.Dir);
        rs.Seed = w.Info.Seed;
        var region = RegionGenerator.Generate(w.Continent, w.History, site.CellX, site.CellY, rs);
        Ravensguard.Sim.WorldGen.Region.LandmarkPlacer.Place(region, w.History, content.Db); // authored landmarks go into the generated region (plan: landmark phase)
        ct.ThrowIfCancellationRequested();
        _at = 2;
        var pop = Populator.Populate(region, w.History, content.Db);
        ct.ThrowIfCancellationRequested();
        _at = 3;
        var start = ChooseStart(region, bi.Origin);
        // the player character from the Quick Start origin, and the Codex of this world with what the embark region already knows
        var hero = bi.Origin != null ? Ravensguard.Sim.Characters.HeroRecord.FromOrigin(d.Name, d.Female, bi.Origin) : null;
        var codex = Ravensguard.Sim.Codex.CodexBook.Build(w.History, region, bi.Rules);
        var world = new WorldState
        {
            Hero = hero, Codex = codex.ToProgress(),
            ContinentSettings = w.Info.File.Continent, HistorySettings = w.Info.File.History, RegionSettings = rs, EmbarkCellX = site.CellX, EmbarkCellY = site.CellY,
            Continent = w.Continent, History = w.History, Population = pop,
            Clock = new SimClock(GameTime.FromCalendar(pop.NowYear, Season.Spring, 1, 8)),
            Ids = new IdAllocator(), Chunks = new ChunkStore(), Chronicle = new Chronicle(),
        };
        ct.ThrowIfCancellationRequested(); // cancelled: no save or run info is written
        string? path = null, saveError = null;
        try
        {
            var lib = bi.Library;
            var file = WorldSave.Capture(world, bi.ContentHash, "0.1", null, d.Ironman);
            var writer = new SaveWriter(lib.RunsDirectory(w.Info.Id));
            path = (d.Ironman ? writer.WriteIronmanAsync(file) : writer.WriteManualAsync(file, bi.RunName)).GetAwaiter().GetResult();
            lib.WriteRunInfo(w.Info.Id, d.Ironman ? "ironman" : bi.RunName, new WorldLibrary.RunInfo(d.Name, d.Female, d.OriginId, bi.Origin?.Ambition ?? "", d.Ironman, site.CellX, site.CellY, bi.Now));
        }
        catch (Exception e) { saveError = e.Message; GD.PushWarning("could not write the first save: " + e.Message); }
        _at = 4;
        return new RunStart(world, d, site, start, path, codex, bi.RunName, saveError);
    }

    /// <summary>The settlement the character starts in: the kind the origin asks for if it is clear of active wars (10 km) and monster lairs (3 km), else any safe one (plan §3f).</summary>
    internal static string ChooseStart(RegionData region, Ravensguard.Content.OriginDef? o)
    {
        string want = o?.StartLocation ?? "village";
        SettlementClass cls = want == "town" ? SettlementClass.Town : SettlementClass.Village;
        var safe = region.Settlements.Where(s => region.IsSafeStart(s.CenterX, s.CenterY)).ToList();
        var pick = safe.FirstOrDefault(s => s.Class == cls) ?? safe.FirstOrDefault() ?? region.Settlements.FirstOrDefault();
        return pick?.Name ?? "";
    }

    void Refresh()
    {
        string[] keys = ["RB_STEP_PICK", "RB_STEP_TERRAIN", "RB_STEP_PEOPLE", "RB_STEP_SAVE"];
        for (int i = 0; i < Steps; i++) _step[i].Text = (i < _at ? "[x] " : i == _at ? "[>] " : "[ ] ") + L.T(keys[i]);
        _bar.Value = _at;
    }

    public override void _Process(double delta)
    {
        if (_task == null || _done) return;
        Refresh();
        if (!_task.IsCompleted) return;
        _done = true;
        if (_cts.IsCancellationRequested) return;
        if (_task.IsFaulted) { _error.Text = L.F("RB_FAILED", _task.Exception!.GetBaseException().Message); GD.PushWarning("region build failed: " + _task.Exception); return; }
        if (_task.IsCanceled) return;
        Latest = _task.Result;
        var w = Latest.World;
        if (Latest.SaveError != null) // the run is playable but nothing is on disk yet: say so, loudly
            _router.ShowModal(L.T("RB_SAVE_FAILED_TITLE"), L.F("RB_SAVE_FAILED", Latest.SaveError), (L.T("UI_OK"), null));
        var session = new RunSession(Latest, _in!.Library, _in.ContentHash, _w.Info.Id);
        // Walk the generated region, starting in the settlement the origin asked for (plan §3f safe start).
        var opt = new Ravensguard.Game.World.StreetOptions { Poi = "at:" + Latest.StartSettlement };
        var view = Ravensguard.Game.World.StreetView.ForRegion(w.Population.Region, w.Population, _in.Content.Db, _router, opt, w.Chunks,
            hero: w.Hero, codex: Latest.Codex, history: w.History, continent: w.Continent);
        view.Run = session;
        view.OnExit = ClearLatest;
        _router.Replace(view);
    }
}
