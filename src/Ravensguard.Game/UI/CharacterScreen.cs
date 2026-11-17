using System.Linq;
using System.Text;
using Godot;
using Ravensguard.Content;
using Ravensguard.Game.Inputs;
using Ravensguard.Game.UI.NewGame;
using Ravensguard.Sim.Characters;

namespace Ravensguard.Game.UI;

/// <summary>The Character panel (C, plan 2.8, 3 items 1 and 2): attributes, skills, kit, coin and the steps of the ambition, read from the <see cref="HeroRecord"/>.</summary>
public partial class CharacterScreen : Control
{
    readonly ScreenRouter _router;
    readonly HeroRecord _hero;
    readonly ContentDatabase _db;
    int _shotWait;
    /// <summary>Dev: when set, the screen saves its own picture to this path and quits.</summary>
    public string? ShotPath { get; init; }

    public CharacterScreen(ScreenRouter router, HeroRecord hero, ContentDatabase db) { _router = router; _hero = hero; _db = db; }

    public override void _Ready()
    {
        var margin = UiKit.Margin(this, 16);
        var root = new VBoxContainer();
        root.AddThemeConstantOverride("separation", 8);
        margin.AddChild(root);
        root.AddChild(UiKit.Title(_hero.Name));
        string origin = _db.TryGet<OriginDef>(_hero.Origin, out var o) ? o.Name : UiKit.Pretty(_hero.Origin);
        root.AddChild(UiKit.Hint($"{L.T(_hero.Female ? "QS_SEX_FEMALE" : "QS_SEX_MALE")}  -  {origin}"));

        var cols = new HBoxContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        cols.AddThemeConstantOverride("separation", 12);
        root.AddChild(cols);

        // attributes and skills
        var left = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        left.AddThemeConstantOverride("separation", 4);
        cols.AddChild(UiKit.Panel(left));
        left.AddChild(UiKit.Hint(L.T("CHAR_ATTRIBUTES")));
        for (int i = 0; i < HeroRecord.AttrIds.Length; i++) left.AddChild(Bar(L.T("ATTR_" + HeroRecord.AttrIds[i].ToUpperInvariant()), _hero.Attributes[i]));
        left.AddChild(UiKit.Hint(L.T("CHAR_SKILLS")));
        var skills = _hero.Skills.Where(kv => kv.Value > 0).OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key, System.StringComparer.Ordinal).ToList();
        foreach (var (id, lvl) in skills) left.AddChild(Bar(UiKit.Pretty(id), lvl));
        if (skills.Count == 0) left.AddChild(UiKit.Body(L.T("CHAR_NO_SKILLS")));
        left.AddChild(UiKit.Hint(L.F("CHAR_UNTRAINED", _hero.Skills.Count - skills.Count)));

        // kit, coin, ambition
        var right = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        right.AddThemeConstantOverride("separation", 8);
        cols.AddChild(right);
        var kit = new VBoxContainer();
        kit.AddThemeConstantOverride("separation", 2);
        right.AddChild(UiKit.Panel(kit));
        kit.AddChild(UiKit.Hint(L.T("CHAR_KIT")));
        foreach (var it in _hero.Inventory) kit.AddChild(UiKit.Body($"{(it.Count > 1 ? it.Count + " x " : "")}{ItemName(it.Shape)} ({MaterialName(it.Material)})", false));
        if (_hero.Inventory.Count == 0) kit.AddChild(UiKit.Body(L.T("CHAR_NO_KIT")));
        kit.AddChild(new Label { Text = L.F("CHAR_COIN", _hero.Coin), ThemeTypeVariation = "GoldLabel" });

        var amb = new VBoxContainer();
        amb.AddThemeConstantOverride("separation", 3);
        var ap = UiKit.Panel(amb);
        ap.SizeFlagsVertical = SizeFlags.ExpandFill;
        right.AddChild(ap);
        amb.AddChild(UiKit.Hint(L.T("CHAR_AMBITION")));
        if (_db.TryGet<AmbitionDef>(_hero.AmbitionId, out var a))
        {
            amb.AddChild(new Label { Text = a.Name, ThemeTypeVariation = "GoldLabel" });
            amb.AddChild(UiKit.Body(a.Description));
            for (int i = 0; i < a.Steps.Count; i++)
            {
                bool done = i < _hero.Milestone, now = i == _hero.Milestone;
                var l = UiKit.Body((done ? "[x] " : now ? "[>] " : "[ ] ") + a.Steps[i].Name, false);
                l.AddThemeColorOverride("font_color", done ? PixelTheme.Dim : now ? PixelTheme.Gold : PixelTheme.Body);
                amb.AddChild(l);
                if (now && a.Steps[i].Description.Length > 0) { var d = UiKit.Hint("      " + a.Steps[i].Description, true); amb.AddChild(d); }
            }
            if (_hero.Milestone >= a.Steps.Count) amb.AddChild(UiKit.Body(L.T("CHAR_AMBITION_DONE")));
        }
        else amb.AddChild(UiKit.Body(L.T("CHAR_NO_AMBITION")));

        var bottom = new HBoxContainer();
        bottom.AddThemeConstantOverride("separation", 8);
        var back = UiKit.Key(L.T("UI_BACK"), "menu.back", 200);
        back.Pressed += _router.Pop;
        bottom.AddChild(back);
        bottom.AddChild(UiKit.Hint(L.F("CHAR_CLOSE_HINT", InputBootstrap.Hint("world.panel_character"))));
        root.AddChild(bottom);
    }

    string ItemName(string shape) => _db.TryGet<WeaponShapeDef>(shape, out var w) ? w.Name : _db.TryGet<ArmourShapeDef>(shape, out var a) ? a.Name : UiKit.Pretty(shape);
    string MaterialName(string id) => _db.TryGet<MaterialDef>(id, out var m) ? m.Name.ToLowerInvariant() : id;

    static Control Bar(string name, int value)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 8);
        row.AddChild(new Label { Text = name, CustomMinimumSize = new Vector2(150, 0) });
        row.AddChild(new Label { Text = value.ToString(), CustomMinimumSize = new Vector2(34, 0), HorizontalAlignment = HorizontalAlignment.Right });
        row.AddChild(new ProgressBar { MaxValue = 100, Value = value, ShowPercentage = false, CustomMinimumSize = new Vector2(180, 12), SizeFlagsVertical = SizeFlags.ShrinkCenter });
        return row;
    }

    public override void _Process(double delta)
    {
        if (ShotPath == null || !Visible || ++_shotWait != 8) return;
        var img = GetViewport().GetTexture().GetImage();
        System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(ShotPath) ?? ".");
        GD.Print($"[character] screenshot {ShotPath}: {img.SavePng(ShotPath)}");
        GetTree().Quit();
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (!Visible) return;
        if (Actions.Pressed(e, "world.panel_character")) { _router.Pop(); GetViewport().SetInputAsHandled(); }
    }
}
