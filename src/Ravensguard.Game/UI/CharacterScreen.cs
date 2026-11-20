using System;
using System.Linq;
using Godot;
using Ravensguard.Content;
using Ravensguard.Game.Inputs;
using Ravensguard.Game.UI.NewGame;
using Ravensguard.Sim.Characters;
using Ravensguard.Sim.Crafting;
using Ravensguard.Sim.People;
using Ravensguard.Sim.Trade;

namespace Ravensguard.Game.UI;

/// <summary>
/// The character sheet (C, plan 2.8, trade-inventory-character spec 4.4): the overview (origin, ambition and its current step, conditions, warmth and clothing from the clothing book, the load bar),
/// the five attributes with their slow growth (milli-points over the base, the base-50 reference, a trend arrow), the skills grouped Combat, Craft, Trade and Social, Wild, each with its level, a
/// thin bar to the next whole level, an arrow when it grew in the last three game days and the uses a level takes at this point; locked skills greyed with how to learn them; the body (wounds arrive
/// in Phase 4) and the party. In a running world everything comes from the barrier's <see cref="HeroView"/> (skills are the world's skills by use); without one, from the <see cref="HeroRecord"/>.
/// </summary>
public partial class CharacterScreen : Control
{
    readonly ScreenRouter _router;
    readonly HeroRecord _hero;
    readonly ContentDatabase _db;
    readonly Func<HeroView?>? _view;
    readonly Func<string>? _body;
    int _shotWait;
    /// <summary>Dev: when set, the screen saves its own picture to this path and quits.</summary>
    public string? ShotPath { get; init; }
    /// <summary>P5-A2: opens the hero's deeds and claims (the Property list); null hides the button.</summary>
    public Action? OpenProperty { get; init; }
    /// <summary>P6-A3: opens the Standing tab (what groups think of the hero, renown, marks); null hides the button.</summary>
    public Action? OpenStanding { get; init; }
    /// <summary>P6-B1: opens the treatment screen on the hero himself (the Treat button); null hides it.</summary>
    public Action? OpenTreat { get; init; }
    /// <summary>P6-B3: opens the Family panel (spouse, children, lessons, what age has taken); null hides the button.</summary>
    public Action? OpenFamily { get; init; }

    public CharacterScreen(ScreenRouter router, HeroRecord hero, ContentDatabase db, Func<HeroView?>? view = null, Func<string>? bodyLine = null)
    { _router = router; _hero = hero; _db = db; _view = view; _body = bodyLine; }

    static readonly (string Key, Skill[] Skills)[] Groups =
    [
        ("SKILLS_COMBAT", [Skill.Melee, Skill.Archery, Skill.Tactics, Skill.Athletics, Skill.Stealth, Skill.Riding]),
        ("SKILLS_CRAFT", [Skill.Smithing, Skill.Carpentry, Skill.Masonry, Skill.Tailoring, Skill.Weaving, Skill.Spinning, Skill.Tanning, Skill.Cobbling, Skill.Baking, Skill.Brewing, Skill.Milling, Skill.Butchery, Skill.Cooking, Skill.Housekeeping, Skill.Surgery, Skill.Lockpicking]),
        ("SKILLS_SOCIAL", [Skill.Trading, Skill.Speech, Skill.Leadership, Skill.Literacy, Skill.Law, Skill.Theology]),
        ("SKILLS_WILD", [Skill.Farming, Skill.Husbandry, Skill.Forestry, Skill.Fishing, Skill.Mining]),
    ];

    public override void _Ready()
    {
        var v = _view?.Invoke();
        var margin = UiKit.Margin(this, 14);
        var root = new VBoxContainer(); root.AddThemeConstantOverride("separation", 6); margin.AddChild(root);
        root.AddChild(UiKit.Title(_hero.Name));
        string origin = _db.TryGet<OriginDef>(_hero.Origin, out var o) ? o.Name : UiKit.Pretty(_hero.Origin);
        root.AddChild(UiKit.Hint($"{L.T(_hero.Female ? "QS_SEX_FEMALE" : "QS_SEX_MALE")}  -  {origin}" + (v != null ? $"  -  {L.F("CHAR_PURSE", Quotes.Money(v.CoinMites))}" : "")));

        var cols = new HBoxContainer { SizeFlagsVertical = SizeFlags.ExpandFill }; cols.AddThemeConstantOverride("separation", 8);
        root.AddChild(UiKit.Scroll(cols));      // the body scrolls; the Back bar below stays on screen

        // ---- left: overview, attributes, body
        var left = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill }; left.AddThemeConstantOverride("separation", 4);
        var lp = UiKit.Panel(left, 300); lp.SizeFlagsHorizontal = SizeFlags.ExpandFill; lp.SizeFlagsStretchRatio = 4; lp.SizeFlagsVertical = SizeFlags.ShrinkBegin; cols.AddChild(lp);
        left.AddChild(UiKit.Hint(L.T("CHAR_OVERVIEW")));
        if (_db.TryGet<AmbitionDef>(_hero.AmbitionId, out var a))
        {
            left.AddChild(new Label { Text = a.Name, ThemeTypeVariation = "GoldLabel" });
            var step = _hero.CurrentStep(a);
            left.AddChild(UiKit.Body(step != null ? L.F("CHAR_STEP", _hero.Milestone + 1, a.Steps.Count, step.Name) : L.T("CHAR_AMBITION_DONE"), false));
        }
        else left.AddChild(UiKit.Body(L.T("CHAR_NO_AMBITION")));
        if (_body?.Invoke() is { Length: > 0 } body) left.AddChild(UiKit.Body(L.F("CHAR_CONDITIONS", body), true));
        if (v != null) left.AddChild(InventoryScreen.LoadBar(v, 160));

        left.AddChild(UiKit.Hint(L.T("CHAR_ATTRIBUTES")));
        for (int i = 0; i < HeroRecord.AttrIds.Length; i++)
        {
            int baseV = _hero.Attributes[i], milli = v?.AttrMilli[i] ?? 0;
            double shown = baseV + milli / 1000.0;
            var row = new HBoxContainer(); row.AddThemeConstantOverride("separation", 8);
            row.AddChild(new Label { Text = L.T("ATTR_" + HeroRecord.AttrIds[i].ToUpperInvariant()), CustomMinimumSize = new Vector2(96, 0) });
            row.AddChild(new Label { Text = milli == 0 ? baseV.ToString() : shown.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture), CustomMinimumSize = new Vector2(52, 0), HorizontalAlignment = HorizontalAlignment.Right });
            row.AddChild(new Label { Text = milli > 0 ? "^" : milli < 0 ? "v" : " ", CustomMinimumSize = new Vector2(12, 0) });
            var bar = new ProgressBar { MaxValue = 100, Value = shown, ShowPercentage = false, CustomMinimumSize = new Vector2(70, 12), SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ShrinkCenter };
            row.AddChild(bar);
            row.AddChild(UiKit.Hint(baseV > HeroRecord.BaselineAttribute ? $"+{baseV - HeroRecord.BaselineAttribute}" : baseV < HeroRecord.BaselineAttribute ? $"{baseV - HeroRecord.BaselineAttribute}" : "="));
            left.AddChild(row);
        }
        left.AddChild(UiKit.Hint(L.T("CHAR_ATTR_NOTE"), true));
        left.AddChild(UiKit.Hint(L.T("CHAR_BODY")));
        left.AddChild(UiKit.Body(L.T("CHAR_WOUNDS_NONE"), true));
        if (v != null)
        {
            var worn = v.Items.Where(x => x.Slot != Ravensguard.Sim.Inventory.EquipSlot.None).Select(x => x.Name.ToLowerInvariant()).ToList();
            left.AddChild(UiKit.Body(L.F("CHAR_WEARING", worn.Count > 0 ? string.Join(", ", worn) : L.T("CHAR_NOTHING")), true));
            left.AddChild(UiKit.Hint(L.F("CHAR_SQUAD", v.Squad.Count(m => m.Active), v.SquadCap, InputBootstrap.Hint("world.panel_squad"))));
            // P5-A4 (inheritance 6): the hero's family, as the person table has them
            left.AddChild(UiKit.Hint(L.T("in_family")));
            if (v.Family.Count == 0) left.AddChild(UiKit.Body(L.T("in_family_none"), true));
            foreach (var k in v.Family)
            {
                string rel = L.T("in_kin_" + k.Relation.ToString().ToLowerInvariant());
                left.AddChild(UiKit.Body(k.Alive ? L.F("in_kin_line", k.Name, rel, k.Age, k.Place) : L.F("in_kin_dead", k.Name, rel, k.Age), true));
            }
        }

        // ---- right: skills by use
        var right = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill }; right.AddThemeConstantOverride("separation", 2);
        var rp = UiKit.Panel(right, 340); rp.SizeFlagsHorizontal = SizeFlags.ExpandFill; rp.SizeFlagsStretchRatio = 6; rp.SizeFlagsVertical = SizeFlags.ShrinkBegin; cols.AddChild(rp);
        long now = (v?.HourDone ?? 0) * 3600;
        foreach (var (key, skills) in Groups)
        {
            right.AddChild(UiKit.Hint(L.T(key)));
            var grid = new GridContainer { Columns = 4 }; grid.AddThemeConstantOverride("h_separation", 8); grid.AddThemeConstantOverride("v_separation", 1);
            foreach (var s in skills.OrderByDescending(s => Micro(v, s)).ThenBy(s => s.ToString(), StringComparer.Ordinal))
            {
                long micro = Micro(v, s); int lvl = (int)(micro / CraftMath.MicroPerLevel);
                long last = v?.Skills.FirstOrDefault(r => r.Skill == s).LastGainSec ?? long.MinValue;
                bool rising = last != long.MinValue && now - last <= 3 * 86400;
                var name = new Label { Text = UiKit.Pretty(HeroRecord.SkillId(s)) + (rising ? " ^" : ""), CustomMinimumSize = new Vector2(110, 0), ClipText = true };
                var val = new Label { Text = lvl.ToString(), CustomMinimumSize = new Vector2(30, 0), HorizontalAlignment = HorizontalAlignment.Right };
                var bar = new ProgressBar { MaxValue = 1000, Value = micro % CraftMath.MicroPerLevel / 1000, ShowPercentage = false, CustomMinimumSize = new Vector2(80, 8), SizeFlagsVertical = SizeFlags.ShrinkCenter };
                // uses for the next level at an ordinary task (d = 100): what the bar means
                long gain = Progress.GainMicro(micro, 100);
                long need = CraftMath.MicroPerLevel - micro % CraftMath.MicroPerLevel;
                var hint = UiKit.Hint(lvl >= 100 ? "-" : lvl == 0 ? L.T("CHAR_SKILL_LOCKED") : L.F("CHAR_USES_NEXT", gain > 0 ? (need + gain - 1) / gain : 0));
                hint.ClipText = true; hint.CustomMinimumSize = new Vector2(70, 0); hint.SizeFlagsHorizontal = SizeFlags.ExpandFill; hint.TooltipText = hint.Text; hint.MouseFilter = MouseFilterEnum.Stop;
                if (lvl == 0) { name.AddThemeColorOverride("font_color", PixelTheme.Dim); val.AddThemeColorOverride("font_color", PixelTheme.Dim); }
                if (rising) name.AddThemeColorOverride("font_color", PixelTheme.Gold);
                name.TooltipText = L.F("CHAR_SKILL_TIP", gain / 1000.0, last == long.MinValue ? L.T("CHAR_NEVER") : L.F("CHAR_DAYS_AGO", Math.Max(0, (now - last) / 86400)));
                grid.AddChild(name); grid.AddChild(val); grid.AddChild(bar); grid.AddChild(hint);
            }
            right.AddChild(grid);
        }
        right.AddChild(UiKit.Hint(L.T("CHAR_SKILLS_NOTE"), true));

        var bottom = new HBoxContainer(); bottom.AddThemeConstantOverride("separation", 8);
        var back = UiKit.Key(L.T("UI_BACK"), "menu.back", 200); back.Pressed += _router.Pop; bottom.AddChild(back);
        if (OpenProperty is { } prop) { var pb = new Button { Text = L.T("ld_char_property"), CustomMinimumSize = new Vector2(150, 36) }; pb.Pressed += () => { _router.Pop(); prop(); }; bottom.AddChild(pb); }   // P5-A2
        if (OpenTreat is { } treat) { var tb = new Button { Text = L.T("tr_char_btn"), CustomMinimumSize = new Vector2(90, 36) }; tb.Pressed += () => { _router.Pop(); treat(); }; bottom.AddChild(tb); }   // P6-B1
        if (OpenStanding is { } stand) { var sb = new Button { Text = L.T("st_open"), CustomMinimumSize = new Vector2(120, 36) }; sb.Pressed += () => { _router.Pop(); stand(); }; bottom.AddChild(sb); }   // P6-A3
        if (OpenFamily is { } fam) { var fb = new Button { Text = L.T("fam_open"), CustomMinimumSize = new Vector2(120, 36) }; fb.Pressed += () => { _router.Pop(); fam(); }; bottom.AddChild(fb); }   // P6-B3
        bottom.AddChild(UiKit.Hint(L.F("CHAR_CLOSE_HINT", InputBootstrap.Hint("world.panel_character"))));
        root.AddChild(bottom);
    }

    long Micro(HeroView? v, Skill s)
    {
        if (v != null) { foreach (var r in v.Skills) if (r.Skill == s) return r.Micro; }
        return _hero.Skill(HeroRecord.SkillId(s)) * CraftMath.MicroPerLevel;
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
