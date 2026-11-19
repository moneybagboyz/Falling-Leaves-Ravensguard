using System;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Ravensguard.Content;
using Ravensguard.Game.Inputs;
using Ravensguard.Sim.Combat;
using Ravensguard.Sim.Combat.Melee2;
using Ravensguard.Sim.Combat.Wounds;

namespace Ravensguard.Game.World.Combat;

/// <summary>
/// The combat stance on its own (<c>-- --fight-demo [duel|skirmish] [--seed n]</c>): you (sword and heater, padded and mailed) against a bandit, or ten a side, on the shipped
/// <see cref="FightWorld"/> and <see cref="CombatBrain"/>. R restarts, K switches duel and skirmish. Screenshots: <c>--shot-out file.png --shot-what windup|calledshot|wounded|feed|log|logall|report|hit</c>
/// (the last four are the P4-D log panel, the same with every filter off, the after-fight report and a landed blow with its callout and flash).
/// </summary>
public partial class FightDemo : Node2D
{
    static FightContent? _content;
    FightView _view = null!;
    FightWorld _w = null!;
    bool _skirmish; ulong _seed = 7;
    string? _shotOut, _shotWhat;

    public static FightContent Content()
    {
        if (_content != null) return _content;
        var root = System.IO.Path.GetFullPath(ProjectSettings.GlobalizePath("res://") + "../../content/base");
        var r = new ModLoader(ContentTypeRegistry.Default).Load(new[] { root });
        if (!r.Success) throw new InvalidOperationException(r.Report.ToString());
        return _content = new FightContent(new CombatTables(r.Database), r.Database);
    }

    public override void _Ready()
    {
        var args = OS.GetCmdlineUserArgs();
        string Arg(string n, string d) { int i = Array.IndexOf(args, n); return i >= 0 && i + 1 < args.Length ? args[i + 1] : d; }
        int k = Array.IndexOf(args, "--fight-demo");
        _skirmish = k >= 0 && k + 1 < args.Length && args[k + 1] == "skirmish";
        _mounted = k >= 0 && k + 1 < args.Length && args[k + 1] == "mounted";             // [mounted-combat] FightDemo.Mounted.cs
        _seed = ulong.TryParse(Arg("--seed", "7"), out var s) ? s : 7;
        _shotOut = Arg("--shot-out", ""); if (_shotOut.Length == 0) _shotOut = null;
        _shotWhat = Arg("--shot-what", "windup");
        Start();
        if (_shotOut != null) _ = Shot();
        GD.Print($"FightDemo ready: {(_mounted ? "mounted" : _skirmish ? "skirmish" : "duel")} seed {_seed}");
    }

    FightWorld Build()
    {
        var c = Content();
        if (_mounted) return BuildMounted(c);
        if (_skirmish)
        {
            var sk = FightCalibration.Skirmish(c, _seed, 10);
            sk.Fighters[1].Player = true;
            sk.LodCentre = (sk.Fighters[1].X, sk.Fighters[1].Y);
            return sk;
        }
        var w = new FightWorld(c, _seed, 1, 8 * 3600);
        w.Add(new FighterKit
        {
            Name = "Aldric", Team = 0, Weapon = new WeaponInstance("arming_sword", "steel"), Shield = "heater", Skill = 55, X = -1800, FaceX = 1024,
            Worn = [new("shirt", "linen"), new("gambeson", "linen"), new("mail_hauberk", "iron"), new("helm", "iron")],
        }, player: true);
        w.Add(new FighterKit
        {
            Name = "Bandit", Team = 1, Weapon = new WeaponInstance("axe", "iron"), Skill = 40, X = 1800, FaceX = -1024, Stance = "aggressive",
            Worn = [new("shirt", "linen"), new("tunic", "wool"), new("jack", "linen"), new("cap", "leather")],
        });
        return w;
    }

    void Start()
    {
        _w = Build();
        int hero = _w.Fighters.FindIndex(f => f.Player);
        if (_view == null)
        {
            _view = new FightView(_w, hero, i => _w.Input(hero, i)) { OwnsStepping = true, Name = "FightView" };
            AddChild(_view);
        }
        else _view.Replace(_w);
        if (!_skirmish && hero >= 0) { _view.Log.Info(hero).Name = "Aldric"; _view.Log.Info(1 - hero).Name = "Bram"; }       // the log names people
        _view.Banner = _mounted ? L.T("mt_demo_mounted") : L.T(_skirmish ? "FIGHT_DEMO_SKIRMISH" : "FIGHT_DEMO_DUEL");
        GetTree().CreateTimer(3.0).Timeout += () => { if (IsInstanceValid(_view)) _view.Banner = ""; };
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (e is not InputEventKey { Pressed: true, Echo: false } k || _view.Calling) return;
        if (k.Keycode == Key.R) { _seed++; Start(); }
        else if (k.Keycode == Key.K) { if (_mounted) _mounted = false; else if (_skirmish) { _skirmish = false; _mounted = true; } else _skirmish = true; Start(); }
        else if (Actions.Pressed(e, "combatstance.back")) GetTree().Quit();
    }

    // ------------------------------------------------------------------ screenshots (p4c-*.png)

    async Task Shot()
    {
        async Task Frames(int n) { for (int i = 0; i < n; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }
        await Frames(2);
        _view.SetProcess(false);
        _view.Banner = "";
        var hero = _w.Fighters.First(f => f.Player);
        hero.Player = false;                                          // the brain drives both while the scene is staged
        switch (_shotWhat)
        {
            case "feed": case "log": case "logall":
            {
                // fifteen seconds of the duel, then the combat log: the small feed along the bottom, or the open panel (F7), with the default filter or with everything
                _view.Advance(20 * 14);
                hero.Player = true;
                _view.Zoom = 3f;
                if (_shotWhat == "logall") { _view.LogPanel.Filter.Flags = Ravensguard.Sim.Combat.Flavour.LogFilterFlags.Misses | Ravensguard.Sim.Combat.Flavour.LogFilterFlags.Treatment; }
                _view.LogPanel.SetExpanded(_shotWhat != "feed");
                _view.ClearCallouts();
                break;
            }
            case "hit":
            {
                // the blow that has just landed on someone: the callout, the white flash and the blood on the ground
                for (int i = 0; i < 20 * 60; i++)
                {
                    _view.Advance(1);
                    if (_view.Log.Lines.Count > 0 && _view.Log.Lines[^1] is { Kind: FightEventKind.Hit, Severity: >= 2 }) break;
                }
                _view.Feedback.Update(0.02);
                hero.Player = true;
                _view.Zoom = 3.5f;
                break;
            }
            case "report":
            {
                for (int i = 0; i < 20 * 200 && !_w.IsOver; i++) _view.Advance(1);
                _view.Advance(30);
                _view.FinishLog();
                hero.Player = true;
                _view.ClearCallouts();
                _view.OpenReport();
                break;
            }
            case "watch" or "stance" or "danger" or "protect":
            {
                // P4-F: the hero handed to the brain (F6): the banner, the why line, the odds; the stance panel open; the "Take control?" prompt of a danger pause
                if (_shotWhat == "protect" && _w.Fighters.Count(f => f.Team == hero.Team) > 1)
                    _view.SetStance(Ravensguard.Sim.Combat.Watch.WatchStance.Of(_w.Content, Ravensguard.Sim.Combat.Watch.StancePreset.Protect), _w.Fighters.First(f => f.Team == hero.Team && f.Id != hero.Id).Id);
                hero.Player = true;
                _view.ToggleWatch();
                for (int i = 0; i < 20 * 9; i++) { _view.Advance(1); if (_w.IsOver) break; }
                if (_shotWhat == "stance") _view.Hud.OpenStanceForShot();
                if (_shotWhat == "danger") _view.Hud.ShowPrompt(new Ravensguard.Sim.Combat.Watch.WatchSignal(_w.Tick, Ravensguard.Sim.Travel.InterruptKind.WatchDanger, hero.Id, 1));
                _view.Zoom = 3f;
                _view.ClearCallouts();
                _view.SetProcess(true); await Frames(2); _view.SetProcess(false);
                break;
            }
            case "charge" or "impact" or "brace" or "unhorsed":
                StageMounted(_shotWhat!, hero);                     // [mounted-combat]
                break;
            case "calledshot":
                _view.Advance(30);
                hero.Player = true;
                _view.ShowCalling("armpit", hero.Weapon.AttackIndex("stab"));
                break;
            case "wounded":
            {
                // fight until someone has taken two wounding blows and still stands, then the other dresses the worst one: a bandage over the clothes, blood where it still runs
                Fighter? hurt = null;
                for (int i = 0; i < 20 * 40 && hurt == null; i++)
                {
                    _view.Advance(1);
                    hurt = _w.Fighters.FirstOrDefault(f => f.Active && f.Book.Records.Select(r => r.Part).Distinct().Count() >= 3 && f.Book.Records.Any(r => r.BleedX10 > 0));
                }
                hurt ??= _w.Fighters.OrderByDescending(f => f.Book.Records.Count).First();
                var items = new ItemBag().Add("bandage", 2).Add("splint", 1);
                // the dressing goes on the wound most in view (an arm or the trunk before a foot behind a boot)
                int Rank(WoundRecord r) { var b = hurt.Plan.Parts[r.Part].BaseId; return b is "lower_arm" or "upper_arm" or "hand" ? 0 : b is "upper_body" or "lower_body" or "head" ? 1 : 2; }
                var bleeding = hurt.Book.Records.Where(r => r.BleedX10 > 0 && r.Dressing == Dressing.None).OrderBy(Rank).ThenByDescending(r => r.BleedX10).ToList();
                if (bleeding.Count > 0) Treatment.Perform(hurt.Book, hurt.Plan, new TreatRequest(TreatKind.Bandage, bleeding[0].Part, _w.GameSec, 30, 1), items);
                else if (TreatAi.Next(hurt.Book, hurt.Plan, items, _w.GameSec, 30, 1) is { } rq) Treatment.Perform(hurt.Book, hurt.Plan, rq, items);
                foreach (var f in _w.Fighters) { f.Player = true; f.Cmd.ClearAll(); if (f.Active) f.Act = FAct.Idle; }
                _view.Advance(10);                                      // the flash of the last hit fades; both stand
                _view.Zoom = 4.2f;
                break;
            }
            default:
            {
                // a duel mid-wind-up: step until a blow is in its red parry tail
                for (int i = 0; i < 20 * 40; i++)
                {
                    _view.Advance(1);
                    var f = _w.Fighters.FirstOrDefault(x => x.Act == FAct.Windup && x.Req.Kind == BlowKind.Melee && x.WindNeeded - x.WindElapsed is >= 1 and <= 3 && _w.Tick > 60);
                    if (f != null) break;
                }
                hero.Player = true;
                _view.Zoom = 3.5f;
                break;
            }
        }
        if (_shotWhat == "wounded") _view.ClearCallouts();
        _view.QueueRedraw();
        await Frames(6);
        var img = GetViewport().GetTexture().GetImage();
        System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(_shotOut!) ?? ".");
        img.SavePng(_shotOut!);
        GD.Print("shot: " + _shotOut);
        await Frames(2);
        GetTree().Quit(0);
    }
}
