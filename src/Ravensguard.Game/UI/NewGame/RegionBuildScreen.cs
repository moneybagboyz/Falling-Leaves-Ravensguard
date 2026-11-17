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
public sealed record RunStart(WorldState World, NewGameDraft Draft, EmbarkScore Site, string StartSettlement, string? SavePath);

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
    volatile int _at;
    bool _done;

    public RegionBuildScreen(ScreenRouter router, NewGameDraft d, LoadedWorld w) { _router = router; _d = d; _w = w; }

    /// <summary>Latest run built by this flow (read by the street view hand-off).</summary>
    public static RunStart? Latest { get; private set; }

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
        _task = Task.Run(() => Build(content, w, d));
        Refresh();
    }

    RunStart Build(ContentBundle content, LoadedWorld w, NewGameDraft d)
    {
        _at = 0;
        var site = d.Embark ?? EmbarkSite.AutoPick(w.Continent, w.History);
        _at = 1;
        var rs = RegionSettings.LoadDefaults(content.Dir);
        rs.Seed = w.Info.Seed;
        var region = RegionGenerator.Generate(w.Continent, w.History, site.CellX, site.CellY, rs);
        _at = 2;
        var pop = Populator.Populate(region, w.History, content.Db);
        _at = 3;
        var start = ChooseStart(region, d);
        var world = new WorldState
        {
            ContinentSettings = w.Info.File.Continent, HistorySettings = w.Info.File.History, RegionSettings = rs, EmbarkCellX = site.CellX, EmbarkCellY = site.CellY,
            Continent = w.Continent, History = w.History, Population = pop,
            Clock = new SimClock(GameTime.FromCalendar(pop.NowYear, Season.Spring, 1, 8)),
            Ids = new IdAllocator(), Chunks = new ChunkStore(), Chronicle = new Chronicle(),
        };
        string? path = null;
        try
        {
            var lib = NewGameServices.Library;
            var runName = WorldLibrary.Slug(d.Name) + "-" + (lib.ListRuns(w.Info.Id).Count + 1);
            var file = WorldSave.Capture(world, content.Hash, "0.1", null, d.Ironman);
            var writer = new SaveWriter(lib.RunsDirectory(w.Info.Id));
            path = (d.Ironman ? writer.WriteIronmanAsync(file) : writer.WriteManualAsync(file, runName)).GetAwaiter().GetResult();
            var origin = content.Db.TryGet<Ravensguard.Content.OriginDef>(d.OriginId, out var o) ? o : null;
            lib.WriteRunInfo(w.Info.Id, d.Ironman ? "ironman" : runName, new WorldLibrary.RunInfo(d.Name, d.Female, d.OriginId, origin?.Ambition ?? "", d.Ironman, site.CellX, site.CellY, NewGameServices.NowText()));
        }
        catch (Exception e) { GD.PushWarning("could not write the first save: " + e.Message); }
        _at = 4;
        return new RunStart(world, d, site, start, path);
    }

    /// <summary>The settlement the character starts in: the kind the origin asks for if it is clear of active wars (10 km) and monster lairs (3 km), else any safe one (plan §3f).</summary>
    static string ChooseStart(RegionData region, NewGameDraft d)
    {
        var db = NewGameServices.Content().Db;
        string want = db.TryGet<Ravensguard.Content.OriginDef>(d.OriginId, out var o) ? o.StartLocation : "village";
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
        if (_task.IsFaulted) { _error.Text = L.F("RB_FAILED", _task.Exception!.GetBaseException().Message); GD.PushWarning("region build failed: " + _task.Exception); return; }
        Latest = _task.Result;
        // Walk the generated region, starting in the settlement the origin asked for (plan §3f safe start).
        var w = Latest.World;
        var opt = new Ravensguard.Game.World.StreetOptions { Poi = "at:" + Latest.StartSettlement };
        _router.Replace(Ravensguard.Game.World.StreetView.ForRegion(w.Population.Region, w.Population, NewGameServices.Content().Db, _router, opt, w.Chunks));
    }
}
