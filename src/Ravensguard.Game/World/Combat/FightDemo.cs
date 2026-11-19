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
/// <see cref="FightWorld"/> and <see cref="CombatBrain"/>. R restarts, K switches duel and skirmish. Screenshots: <c>--shot-out file.png --shot-what windup|calledshot|wounded</c>.
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
            Name = "You", Team = 0, Weapon = new WeaponInstance("arming_sword", "steel"), Shield = "heater", Skill = 55, X = -1800, FaceX = 1024,
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
