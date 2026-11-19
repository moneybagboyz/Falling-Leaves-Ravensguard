using System;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Ravensguard.Game.Inputs;
using Ravensguard.Game.World.Combat;
using Ravensguard.Sim.Combat;
using Ravensguard.Sim.Combat.Melee2;
using Ravensguard.Sim.Combat.Wounds;
using Ravensguard.Sim.Sites;
using Ravensguard.Sim.WorldGen.Region;

namespace Ravensguard.Game.World.Crawl;

/// <summary>
/// A crawl on its own (<c>-- --crawl-demo [crypt|barrow|mine] [--seed n]</c>, P4-H): the abbey crypt of the done-when (three levels by torchlight, a trap and a grave robber), a barrow
/// with its draugr, or a mine with bad air, on the shipped <see cref="SiteCrawl"/>. Screenshots: <c>--shot-out file.png --shot-what walk|dark|map|trap|air|fight|draugr</c>.
/// </summary>
public partial class CrawlDemo : Node2D
{
    CrawlView _view = null!;
    SiteCrawl _c = null!;
    FightView? _fight;
    CanvasLayer? _fightLayer;
    string _kind = "crypt", _site = "";
    ulong _seed = 59;
    string? _shotOut, _shotWhat;

    public override void _Ready()
    {
        var args = OS.GetCmdlineUserArgs();
        string Arg(string n, string d) { int i = Array.IndexOf(args, n); return i >= 0 && i + 1 < args.Length ? args[i + 1] : d; }
        int k = Array.IndexOf(args, "--crawl-demo");
        if (k >= 0 && k + 1 < args.Length && !args[k + 1].StartsWith("--", StringComparison.Ordinal)) _kind = args[k + 1];
        _seed = ulong.TryParse(Arg("--seed", "59"), out var s) ? s : 59;
        _shotOut = Arg("--shot-out", ""); if (_shotOut.Length == 0) _shotOut = null;
        _shotWhat = Arg("--shot-what", "walk");
        if (_shotWhat is "air") _kind = "mine";
        if (_shotWhat is "draugr") _kind = "barrow";
        Start();
        if (_shotOut != null) _ = Shot();
        GD.Print($"CrawlDemo ready: {_kind} seed {_seed}");
    }

    void Start()
    {
        var fc = FightDemo.Content();
        var sc = SiteWiring.ContentOf(fc.Db);
        SiteHistoryInfo h = _kind switch
        {
            "barrow" => new SiteHistoryInfo { SiteId = 7300, Kind = SiteKind.Barrow, Sub = "barrow", Name = "Ravenfoot's Howe", Levels = 2, MonsterLair = true, LairKind = "draugr", YearsSince = 300, LegendName = "Ketil Ravenfoot" },
            "mine" => new SiteHistoryInfo { SiteId = 8007, Kind = SiteKind.Mine, Sub = "iron", Name = "Iron mine near Coldwell", Levels = 3, Mine = MineState.Abandoned, YearsSince = 40 },
            _ => new SiteHistoryInfo { SiteId = 4401, Kind = SiteKind.Ruin, Sub = "sacked", Name = "Ruins of Aldwick Abbey", Levels = 3, PeakPop = 120, SettlementName = "Aldwick", EventYear = 287, YearsSince = 60 },
        };
        _site = h.Name;
        var map = SiteGen.Generate(sc, h, _seed);
        if (_kind == "crypt") OneGuard(map);
        _c = new SiteCrawl(map, sc, fc, new WoundLedger(_seed, 9 * 3600), 9 * 3600, _seed) { Surgeon = new SurgeonInfo("Aldwick Market", 15_552) };
        var hero = _c.Add(new CrawlMember
        {
            Person = 7, Name = "Aldric", SpareTorches = 3, Perception = _shotWhat == "trap" ? 70 : 40, Bandages = 2,
            Kit = new FighterKit { Name = "Aldric", Person = 7, Weapon = new WeaponInstance("arming_sword", "iron"), Skill = 50,
                Worn = _kind == "barrow" ? [new("shirt", "linen"), new("gambeson", "linen"), new("mail_hauberk", "iron"), new("helm", "iron")] : [new("shirt", "linen"), new("tunic", "wool")] },
        });
        _c.ToggleLight(hero);
        if (_view == null)
        {
            _view = new CrawlView(_c, i => _c.Input(i)) { OwnsStepping = true, Name = "CrawlView", SiteName = _site };
            AddChild(_view);
        }
        else _view.Replace(_c);
        _view.SiteName = _site;
    }

    /// <summary>The crypt's one guard, a grave robber in the far room of the deepest level (EX-12's scenario).</summary>
    static void OneGuard(SiteMap m)
    {
        m.Inhabitants.Clear();
        var crypt = m.Rooms.Where(r => r.Level == m.Levels - 1 && r.Template != "stairs_down").OrderByDescending(r => r.Index).First();
        int cell = Enumerable.Range(0, m.Cells).First(i => m.Room[i] == crypt.Index && m.Tiles[i] == SiteTile.Floor && m.TrapAt(i) == null);
        var (l, x, y) = m.At(cell);
        m.Inhabitants.Add(new Inhabitant { Id = SiteGen.FolkId(m.SiteId, 0), Role = "grave_robber", Kit = "grave_robber", Name = "Osric Pale", Home = cell, Skill = 42, Hostile = true, State = InhabitantState.Home, X = x * 1000 + 500, Y = y * 1000 + 500, Level = l });
    }

    int CryptCell() { var m = _c.Map; var r = m.Rooms.Where(q => q.Level == m.Levels - 1 && q.Template != "stairs_down").OrderByDescending(q => q.Index).First(); return Enumerable.Range(0, m.Cells).First(i => m.Room[i] == r.Index && m.Tiles[i] == SiteTile.Floor); }

    public override void _Process(double delta)
    {
        // a fight the crawl starts is fought in the combat stance view, stepped by it; the crawl watches
        if (_c.Fight != null && _fight == null)
        {
            _c.OwnsFight = false; _view.OwnsStepping = false;
            _fightLayer = new CanvasLayer { Layer = 6 };
            AddChild(_fightLayer);
            _fight = new FightView(_c.Fight, Math.Max(0, _c.HeroFighter), i => _c.Fight?.Input(Math.Max(0, _c.HeroFighter), i)) { OwnsStepping = true, Name = "FightView", Ground = new Color(0.16f, 0.13f, 0.11f) };
            _fightLayer.AddChild(_fight);
        }
        if (_fight != null && _c.Fight != null) _c.Step();
        if (_fight != null && _c.Fight == null) { _fight.QueueFree(); _fightLayer?.QueueFree(); _fight = null; _c.OwnsFight = true; _view.OwnsStepping = true; }
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (e is not InputEventKey { Pressed: true, Echo: false } k || _view.ModalOpen) return;
        if (k.Keycode == Key.R && _fight == null) { _seed++; Start(); }
        else if (Actions.Pressed(e, "world.back") && _fight == null) GetTree().Quit();
    }

    // ------------------------------------------------------------------ screenshots (p4h-*.png)

    /// <summary>Walks the hero towards a cell (the pathfinder's way) until <paramref name="stop"/> holds or the time runs out.</summary>
    void WalkUntil(int goal, Func<CrawlEvent, bool> stop, int maxS = 900, bool fightsToo = false)
    {
        _c.HeroAi = true;
        _c.GoTo(_c.Hero, goal);
        for (int s = 0; s < 20 * maxS; s++)
        {
            _c.Step();
            bool hit = false;
            foreach (var e in _c.Events) if (stop(e)) hit = true;
            if (hit) return;
            _c.Events.Clear();
            if (_c.Fight != null && !fightsToo) return;
            if (_c.Fight == null && _c.Hero.Path == null && _c.Hero.CanAct && _c.Hero.Cell != goal) _c.GoTo(_c.Hero, goal);
            if (_c.Hero.Cell == goal && _c.Fight == null) return;
        }
    }

    async Task Shot()
    {
        async Task Frames(int n) { for (int i = 0; i < n; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }
        await Frames(2);
        _view.SetProcess(false);
        var m = _c.Map;
        switch (_shotWhat)
        {
            case "walk":
                // down the first stairs by torchlight: the stair room behind, the dark ahead
                WalkUntil(m.Arrival[1], e => e.Kind == CrawlEventKind.LevelChanged);
                WalkUntil(CryptCell(), _ => false, maxS: 12);
                _c.Hero.Path = null;
                _view.Cell = 20;
                break;
            case "dark":
                // the torch snuffed: only what was seen remains, drawn dim on the map; the lines say why
                WalkUntil(m.Arrival[1], e => e.Kind == CrawlEventKind.LevelChanged);
                for (int i = 0; i < 20 * 12; i++) { _c.Input(new CrawlInput(CrawlInputKind.Move, 2, 0)); _c.Step(); }
                _c.Input(new CrawlInput(CrawlInputKind.Move, -1, 0));
                _c.Input(new CrawlInput(CrawlInputKind.Torch)); _c.Step(); _c.Step();
                _view.Cell = 20;
                break;
            case "map":
                WalkUntil(CryptCell(), e => e.Kind == CrawlEventKind.FightStarted || e.Kind == CrawlEventKind.TrapTriggered);
                while (_c.Fight != null) _c.Step();
                _view.OpenMap(_c.Hero.Level);
                break;
            case "trap":
                WalkUntil(CryptCell(), e => e.Kind is CrawlEventKind.TrapDetected or CrawlEventKind.TrapTriggered);
                for (int i = 0; i < 10; i++) _c.Step();
                _view.Cell = 22;
                break;
            case "air":
            {
                // a dead mine's foul drift: the candle gutters at the edge of the bad air
                int core = Enumerable.Range(0, m.Cells).Where(i => SiteTiles.Walkable(m.Tiles[i])).OrderBy(i => m.Air[i]).First();
                WalkUntil(core, e => e.Kind == CrawlEventKind.AirWarning);
                for (int i = 0; i < 20 * 3; i++) _c.Step();
                _view.Cell = 20;
                break;
            }
            case "fight" or "draugr":
            {
                if (_kind == "barrow")
                {
                    // the burial chamber: take a grave good from the bier and wait for the dead to stir
                    var dr = m.Inhabitants.First(n => n.Monster == "draugr");
                    var grave = m.Containers.First(x => x.Room == m.Room[dr.Home] && x.Items.Count > 0);
                    int beside = _c.Around(grave.Cell).First(x => SiteTiles.Walkable(m.Tiles[x]) && m.Tiles[x] != SiteTile.Door);
                    WalkUntil(beside, _ => false);
                    _c.Loot(_c.Hero, grave);
                    for (int s = 0; s < 20 * 120 && _c.Fight == null; s++) _c.Step();
                }
                else WalkUntil(CryptCell(), e => e.Kind == CrawlEventKind.FightStarted);
                _c.HeroAi = false;
                if (_c.Fight != null)
                {
                    _c.Fight.Fighters[Math.Max(0, _c.HeroFighter)].Player = false;
                    _Process(0);                                   // opens the combat stance view
                    for (int i = 0; i < 20; i++) _fight!.Advance(1);
                    _fight!.Zoom = 3.4f;
                    _fight.SetProcess(false);
                }
                break;
            }
        }
        _view.QueueRedraw();
        _view.SetProcess(true); await Frames(3); _view.SetProcess(false);
        await Frames(4);
        var img = GetViewport().GetTexture().GetImage();
        System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(_shotOut!) ?? ".");
        img.SavePng(_shotOut!);
        GD.Print("shot: " + _shotOut);
        await Frames(2);
        GetTree().Quit(0);
    }
}
