using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Ravensguard.Content;
using Ravensguard.Game.Inputs;
using Ravensguard.Sim.People;
using Ravensguard.Sim.WorldGen;
using Ravensguard.Sim.Worlds;

namespace Ravensguard.Game.UI.NewGame;

/// <summary>
/// The one Quick Start screen (plan §3 item 1): name, origin cards with their ambition, Ironman, the world (new or from the library)
/// and the Advanced world panel. Start walks straight in; Choose region opens the embark map first.
/// </summary>
public partial class QuickStartScreen : Control
{
    readonly ScreenRouter _router;
    readonly NewGameDraft _d;
    readonly List<OriginDef> _origins;
    readonly List<WorldInfo> _library;
    readonly Dictionary<string, Button> _cards = new();
    int _worldIdx; // 0 = new world, 1.. = library
    LineEdit _name = null!;
    Button _sex = null!, _ironman = null!, _world = null!, _advanced = null!;
    RichTextLabel _detail = null!;
    Label _worldInfo = null!, _warn = null!, _ironHelp = null!;
    VBoxContainer _advBox = null!;
    LineEdit _seed = null!;
    ScrollContainer _advScroll = null!;
    bool _advOpen;
    ulong _nameCounter = 1;

    public QuickStartScreen(ScreenRouter router, string? preselectWorld = null)
    {
        _router = router;
        _d = NewGameServices.NewDraft();
        _origins = NewGameServices.Origins();
        _library = NewGameServices.Library.List();
        if (preselectWorld != null) _worldIdx = _library.FindIndex(w => w.Id == preselectWorld) + 1;
        if (_worldIdx < 0) _worldIdx = 0;
    }

    public override void _Ready()
    {
        var margin = UiKit.Margin(this, 16);
        var root = new VBoxContainer();
        root.AddThemeConstantOverride("separation", 6);
        margin.AddChild(root);
        root.AddChild(UiKit.Title(L.T("QS_TITLE")));

        var cols = new HBoxContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        cols.AddThemeConstantOverride("separation", 12);
        root.AddChild(cols);

        // left: name, origins, origin detail
        var left = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsStretchRatio = 1.7f };
        left.AddThemeConstantOverride("separation", 8);
        cols.AddChild(left);
        var nameRow = new HBoxContainer();
        nameRow.AddThemeConstantOverride("separation", 6);
        nameRow.AddChild(new Label { Text = L.T("QS_NAME") });
        _name = new LineEdit { SizeFlagsHorizontal = SizeFlags.ExpandFill, MaxLength = 28, PlaceholderText = L.T("QS_NAME_PLACEHOLDER"), CustomMinimumSize = new Vector2(0, 32) };
        UiKit.CaptureKeys(_name);
        _name.TextChanged += t => { _d.Name = t; _warn.Text = ""; };
        nameRow.AddChild(_name);
        _sex = UiKit.Key("", "menu.sex", 150, 32);
        _sex.Pressed += ToggleSex;
        nameRow.AddChild(_sex);
        var reroll = UiKit.Key(L.T("QS_REROLL_NAME"), "menu.reroll_name", 150, 32);
        reroll.Pressed += RerollName;
        nameRow.AddChild(reroll);
        left.AddChild(nameRow);

        left.AddChild(UiKit.Hint(L.T("QS_ORIGIN")));
        var grid = new GridContainer { Columns = 2 };
        grid.AddThemeConstantOverride("h_separation", 6);
        grid.AddThemeConstantOverride("v_separation", 6);
        left.AddChild(grid);
        var group = new ButtonGroup();
        foreach (var o in _origins)
        {
            var b = new Button
            {
                ToggleMode = true, ButtonGroup = group, Alignment = HorizontalAlignment.Left, SizeFlagsHorizontal = SizeFlags.ExpandFill,
                CustomMinimumSize = new Vector2(0, 40), Text = o.Name + "\n" + SkillLine(o), ClipText = true, TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
            };
            b.AddThemeStyleboxOverride("pressed", PixelTheme.PanelBox(new Color("4b3d31"), PixelTheme.Gold, 4));
            b.AddThemeStyleboxOverride("normal", PixelTheme.PanelBox(new Color("2f2722"), PixelTheme.Rim, 4));
            b.AddThemeColorOverride("font_pressed_color", PixelTheme.Gold);
            var id = o.Id;
            b.Pressed += () => SelectOrigin(id);
            b.FocusEntered += () => SelectOrigin(id);
            _cards[id] = b;
            grid.AddChild(b);
        }
        _detail = new RichTextLabel { BbcodeEnabled = true, FitContent = false, ScrollActive = false, SizeFlagsVertical = SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(0, 60) };
        var dp = UiKit.Panel(_detail); dp.SizeFlagsVertical = SizeFlags.ExpandFill; left.AddChild(dp);

        // right: world
        var right = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        right.AddThemeConstantOverride("separation", 8);
        cols.AddChild(right);
        right.AddChild(UiKit.Hint(L.T("QS_WORLD")));
        _world = UiKit.Key("", "menu.world_cycle", 0, 36);
        _world.Pressed += CycleWorld;
        right.AddChild(_world);
        _worldInfo = UiKit.Body("");
        right.AddChild(_worldInfo);
        _ironman = UiKit.Key("", "menu.ironman", 0, 36);
        _ironman.Pressed += ToggleIronman;
        right.AddChild(_ironman);
        _ironHelp = UiKit.Hint(L.T("QS_IRONMAN_HELP"), true);
        right.AddChild(_ironHelp);
        _advanced = UiKit.Key("", "menu.advanced", 0, 36);
        _advanced.Pressed += ToggleAdvanced;
        right.AddChild(_advanced);
        _advScroll = new ScrollContainer { SizeFlagsVertical = SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, Visible = false };
        _advBox = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _advBox.AddThemeConstantOverride("separation", 4);
        _advScroll.AddChild(_advBox);
        right.AddChild(_advScroll);
        BuildAdvanced();

        _warn = new Label { ThemeTypeVariation = "WarnLabel" };
        root.AddChild(_warn);
        var bottom = new HBoxContainer();
        bottom.AddThemeConstantOverride("separation", 8);
        var back = UiKit.Key(L.T("UI_BACK"), "menu.back", 180);
        back.Pressed += _router.Pop;
        var choose = UiKit.Key(L.T("QS_CHOOSE_REGION"), "menu.choose_region", 260);
        choose.Pressed += () => Go(true);
        var start = UiKit.Key(L.T("QS_START"), "menu.accept", 260);
        start.Pressed += () => Go(false);
        bottom.AddChild(back);
        bottom.AddChild(new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill });
        bottom.AddChild(choose);
        bottom.AddChild(start);
        root.AddChild(bottom);

        RerollName();
        SelectOrigin(_d.OriginId);
        ApplyWorld();
        RefreshToggles();
        _cards[_d.OriginId].GrabFocus();
    }

    static string SkillLine(OriginDef o) =>
        string.Join("  ", o.Skills.OrderByDescending(kv => kv.Value).Take(2).Select(kv => $"{UiKit.Pretty(kv.Key)} +{kv.Value}"));

    void BuildAdvanced()
    {
        _advBox.AddChild(UiKit.Hint(L.T("ADV_HELP"), true));
        var seedRow = new HBoxContainer();
        seedRow.AddThemeConstantOverride("separation", 4);
        seedRow.AddChild(new Label { Text = L.T("ADV_SEED"), SizeFlagsHorizontal = SizeFlags.ExpandFill });
        _seed = new LineEdit { Text = _d.Continent.Seed.ToString(), CustomMinimumSize = new Vector2(120, 28), MaxLength = 10 };
        UiKit.CaptureKeys(_seed);
        _seed.TextChanged += t => { if (ulong.TryParse(t, out var v) && v > 0) { NewGameServices.Reseed(_d, v); ApplyWorld(); } };
        seedRow.AddChild(_seed);
        var rs = UiKit.Key(L.T("ADV_REROLL_SEED"), "menu.reroll_seed", 140, 28);
        rs.Pressed += RerollSeed;
        seedRow.AddChild(rs);
        _advBox.AddChild(seedRow);

        var c = _d.Continent; var h = _d.History;
        var layout = new HBoxContainer();
        layout.AddChild(new Label { Text = L.T("ADV_LAYOUT"), SizeFlagsHorizontal = SizeFlags.ExpandFill });
        var lb = new Button { CustomMinimumSize = new Vector2(150, 28) };
        void LayoutText() => lb.Text = L.T("ADV_LAYOUT_" + c.Layout.ToString().ToUpperInvariant());
        lb.Pressed += () => { c.Layout = (ContinentLayout)(((int)c.Layout + 1) % 3); LayoutText(); };
        LayoutText();
        layout.AddChild(lb);
        _advBox.AddChild(layout);
        _advBox.AddChild(UiKit.Stepper(L.T("ADV_PLATES"), () => c.PlateCount, v => c.PlateCount = v, 6, 20, 1));
        _advBox.AddChild(UiKit.Stepper(L.T("ADV_CULTURES"), () => c.CultureCount, v => c.CultureCount = v, 3, 8, 1));
        _advBox.AddChild(UiKit.Stepper(L.T("ADV_REALMS"), () => c.RealmCount, v => c.RealmCount = v, 3, 16, 1));
        _advBox.AddChild(UiKit.Stepper(L.T("ADV_YEARS"), () => h.Years, v => h.Years = v, 50, 500, 25, v => L.F("ADV_YEARS_FMT", v)));
        _advBox.AddChild(UiKit.Stepper(L.T("ADV_MINERALS"), () => c.MineralDensityPct, v => c.MineralDensityPct = v, 25, 200, 25, v => v + "%"));
        _advBox.AddChild(UiKit.Stepper(L.T("ADV_SAVAGERY"), () => c.SavageryPct, v => c.SavageryPct = v, 0, 200, 25, v => v + "%"));
        _advBox.AddChild(UiKit.Stepper(L.T("ADV_MOISTURE"), () => c.MoisturePct, v => c.MoisturePct = v, 50, 150, 10, v => v + "%"));
        _advBox.AddChild(UiKit.Stepper(L.T("ADV_TEMPERATURE"), () => c.TemperatureOffsetTenths, v => c.TemperatureOffsetTenths = v, -60, 60, 10, v => (v >= 0 ? "+" : "") + (v / 10.0).ToString("0.0") + " C"));
    }

    void RerollSeed()
    {
        NewGameServices.Reseed(_d, (ulong)Random.Shared.Next(1, 99_999_999));
        _seed.Text = _d.Continent.Seed.ToString();
        ApplyWorld();
    }

    void RerollName()
    {
        var content = NewGameServices.Content();
        var pools = new NamePools(content.Db.All<NamePoolDef>(), (ulong)Random.Shared.Next(1, int.MaxValue));
        var pool = pools.Pools[0];
        var rnd = new Rnd((ulong)Random.Shared.Next(1, int.MaxValue), _nameCounter++, 99);
        _d.Name = pool.Given(_d.Female, rnd);
        _name.Text = _d.Name;
        _warn.Text = "";
    }

    void ToggleSex() { _d.Female = !_d.Female; RefreshToggles(); }
    void ToggleIronman() { _d.Ironman = !_d.Ironman; RefreshToggles(); }

    public void DebugAdvanced() => ToggleAdvanced();

    void ToggleAdvanced()
    {
        if (_worldIdx != 0) return;
        _advOpen = !_advOpen;
        _ironHelp.Visible = !_advOpen; // small windows: the panel gets the room (it scrolls for the rest)
        RefreshToggles();
    }

    void CycleWorld()
    {
        _worldIdx = (_worldIdx + 1) % (_library.Count + 1);
        ApplyWorld();
    }

    void ApplyWorld()
    {
        if (_worldIdx == 0)
        {
            _d.LibraryWorldId = null;
            _world.Text = L.F("QS_WORLD_NEW", _d.WorldName);
            _worldInfo.Text = L.F("QS_WORLD_NEW_INFO", _d.Continent.Seed, _d.History.Years);
        }
        else
        {
            var w = _library[_worldIdx - 1];
            _d.LibraryWorldId = w.Id;
            var s = w.File.Summary;
            _world.Text = L.F("QS_WORLD_LIB", w.Name);
            _worldInfo.Text = L.F("QS_WORLD_LIB_INFO", w.Seed, s.Years, s.RealmsAlive, s.Settlements, s.Ruins, w.Runs.Count);
            _advOpen = false;
        }
        RefreshToggles();
    }

    void RefreshToggles()
    {
        _sex.Text = L.T(_d.Female ? "QS_SEX_FEMALE" : "QS_SEX_MALE");
        _ironman.Text = L.F("QS_IRONMAN", L.T(_d.Ironman ? "UI_ON" : "UI_OFF"));
        bool lib = _worldIdx != 0;
        _advanced.Disabled = lib;
        _advanced.Text = lib ? L.T("QS_ADVANCED_LOCKED") : L.T(_advOpen ? "QS_ADVANCED_OPEN" : "QS_ADVANCED");
        _advScroll.Visible = _advOpen && !lib;
        if (_seed != null && GodotObject.IsInstanceValid(_seed)) _seed.Text = _d.Continent.Seed.ToString();
    }

    void SelectOrigin(string id)
    {
        if (!_cards.TryGetValue(id, out var card)) return;
        _d.OriginId = id;
        card.SetPressedNoSignal(true);
        var o = _origins.First(x => x.Id == id);
        var db = NewGameServices.Content().Db;
        string Item(KitItemDef k)
        {
            string name = db.TryGet<WeaponShapeDef>(k.Item, out var w) ? w.Name : db.TryGet<ArmourShapeDef>(k.Item, out var a) ? a.Name : UiKit.Pretty(k.Item);
            return k.Count > 1 ? $"{name} x{k.Count}" : name;
        }
        var amb = db.TryGet<AmbitionDef>(o.Ambition, out var ad) ? ad : null;
        string gold = PixelTheme.Gold.ToHtml(false), dim = PixelTheme.Dim.ToHtml(false);
        var sb = new System.Text.StringBuilder();
        sb.Append(o.Description).Append("\n\n");
        sb.Append($"[color=#{dim}]{L.T("QS_SKILLS")}[/color] ").Append(string.Join(", ", o.Skills.Select(kv => $"{UiKit.Pretty(kv.Key)} +{kv.Value}"))).Append('\n');
        sb.Append($"[color=#{dim}]{L.T("QS_KIT")}[/color] ").Append(string.Join(", ", o.Kit.Select(Item))).Append(", ").Append(UiKit.Money(o.Coin)).Append('\n');
        sb.Append($"[color=#{dim}]{L.T("QS_STARTS")}[/color] ").Append(L.T("START_" + o.StartLocation.ToUpperInvariant())).Append("   ");
        sb.Append($"[color=#{dim}]{L.T("QS_MENTOR")}[/color] ").Append(UiKit.Pretty(o.Mentor)).Append('\n');
        if (amb != null)
        {
            sb.Append($"[color=#{gold}]{L.T("QS_AMBITION")}: {amb.Name}[/color]  ").Append(amb.Description).Append('\n');
            sb.Append(string.Join("  >  ", amb.Steps.Select(s => s.Name)));
        }
        _detail.Text = sb.ToString();
    }

    void Go(bool chooseRegion)
    {
        if (string.IsNullOrWhiteSpace(_d.Name)) { _warn.Text = L.T("QS_NEED_NAME"); _name.GrabFocus(); return; }
        _d.Name = _d.Name.Trim();
        _d.ChooseRegion = chooseRegion;
        NewGameFlow.Begin(_router, _d);
    }

    // Enter starts unless a button other than an origin card has focus (then Enter presses that button).
    public override void _Input(InputEvent e)
    {
        if (!Visible || !Actions.Pressed(e, "menu.accept")) return;
        var f = GetViewport().GuiGetFocusOwner();
        if (f is LineEdit || (f is Button b && !_cards.ContainsValue(b))) return;
        Go(false);
        GetViewport().SetInputAsHandled();
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (!Visible) return;
        if (Actions.Pressed(e, "menu.sex")) ToggleSex();
        else if (Actions.Pressed(e, "menu.ironman")) ToggleIronman();
        else if (Actions.Pressed(e, "menu.advanced")) ToggleAdvanced();
        else if (Actions.Pressed(e, "menu.world_cycle")) CycleWorld();
        else if (Actions.Pressed(e, "menu.reroll_name")) RerollName();
        else if (Actions.Pressed(e, "menu.reroll_seed") && _worldIdx == 0) RerollSeed();
        else if (Actions.Pressed(e, "menu.choose_region")) Go(true);
        else return;
        GetViewport().SetInputAsHandled();
    }
}
