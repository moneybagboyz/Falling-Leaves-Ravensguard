using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Ravensguard.Game.Inputs;
using Ravensguard.Game.UI.NewGame;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Inventory;
using Ravensguard.Sim.Trade;

namespace Ravensguard.Game.UI;

/// <summary>
/// The inventory (I, trade-inventory-character spec 4.3, 6): the weight bar against the carrying capacity <c>15 + 2.5 x strength</c> kg (green to half, amber to full, red over) with what it does to
/// speed and stamina, the volume of the worn containers, and everything carried by where it is: worn and held things by slot, then each container with what is in it (things and stacks of goods),
/// then what is carried loose. Equip, take off, put into a container and inspect are hero commands applied at the next barrier; the screen reads the barrier's <see cref="HeroView"/>.
/// </summary>
public partial class InventoryScreen : Control
{
    readonly ScreenRouter _router;
    readonly HeroWorld _hw;
    readonly Func<long> _now;
    VBoxContainer _body = null!;
    Label _status = null!;
    HeroView? _view;
    double _refresh;
    long _waitSeq;
    int _shotWait;
    public string? ShotPath { get; init; }

    public InventoryScreen(ScreenRouter router, HeroWorld hw, Func<long> now) { _router = router; _hw = hw; _now = now; }

    public override void _Ready()
    {
        var margin = UiKit.Margin(this, 14);
        var root = new VBoxContainer(); root.AddThemeConstantOverride("separation", 6); margin.AddChild(root);
        root.AddChild(UiKit.Title(L.T("INV_TITLE")));
        var scroll = new ScrollContainer { SizeFlagsVertical = SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        _body = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill }; _body.AddThemeConstantOverride("separation", 3);
        scroll.AddChild(_body);
        var p = UiKit.Panel(scroll, 0, 0); p.SizeFlagsVertical = SizeFlags.ExpandFill; root.AddChild(p);
        _status = UiKit.Body("", true); root.AddChild(_status);
        var bottom = new HBoxContainer(); bottom.AddThemeConstantOverride("separation", 8); root.AddChild(bottom);
        var back = UiKit.Key(L.T("UI_BACK"), "menu.back", 200); back.Pressed += _router.Pop; bottom.AddChild(back);
        bottom.AddChild(UiKit.Hint(L.F("INV_CLOSE_HINT", InputBootstrap.Hint("world.panel_inventory"))));
        Build();
    }

    static string Kg(long g) => g < 50 ? "0" : Quotes.Weight(g).Replace(" kg", "");
    static string Litres(long ml) => $"{ml / 1000.0:0.#}";

    public static Control LoadBar(HeroView v, int width = 420)
    {
        var col = new VBoxContainer(); col.AddThemeConstantOverride("separation", 2);
        var row = new HBoxContainer(); row.AddThemeConstantOverride("separation", 8);
        row.AddChild(new Label { Text = L.T("INV_WEIGHT"), CustomMinimumSize = new Vector2(90, 0) });
        var bar = new ProgressBar { MaxValue = 1500, Value = Math.Min(1500, v.RatioPm), ShowPercentage = false, CustomMinimumSize = new Vector2(width, 14), SizeFlagsVertical = SizeFlags.ShrinkCenter };
        var col0 = Encumbrance.Band(v.RatioPm) switch { 0 => new Color("8fd16b"), 1 => new Color("e0a84a"), _ => new Color("d86a5a") };
        bar.AddThemeStyleboxOverride("fill", new StyleBoxFlat { BgColor = col0 });
        row.AddChild(bar);
        row.AddChild(new Label { Text = $"{Kg(v.MassG)} / {Kg(v.CapacityG)} kg" });
        col.AddChild(row);
        var r2 = new HBoxContainer(); r2.AddThemeConstantOverride("separation", 8);
        r2.AddChild(new Label { Text = L.T("INV_VOLUME"), CustomMinimumSize = new Vector2(90, 0) });
        r2.AddChild(new ProgressBar { MaxValue = Math.Max(1, v.WornCapacityMl), Value = Math.Min(v.WornCapacityMl, v.WornVolumeMl), ShowPercentage = false, CustomMinimumSize = new Vector2(width, 14), SizeFlagsVertical = SizeFlags.ShrinkCenter });
        r2.AddChild(new Label { Text = $"{Litres(v.WornVolumeMl)} / {Litres(v.WornCapacityMl)} L" });
        col.AddChild(r2);
        string effect = v.RatioPm <= 500 ? L.T("INV_LOAD_LIGHT") : Encumbrance.CanWalk(v.RatioPm) ? L.F("INV_LOAD_SLOW", v.RatioPm / 1000.0, v.SpeedPm / 1000.0, v.StaminaPm / 1000.0) : L.T("INV_LOAD_CANNOT");
        var eff = UiKit.Hint(effect, true);
        if (v.RatioPm > 1000) eff.AddThemeColorOverride("font_color", PixelTheme.Warn);
        col.AddChild(eff);
        return col;
    }

    void Build()
    {
        _view = _hw.View;
        foreach (var c in _body.GetChildren()) { _body.RemoveChild(c); c.QueueFree(); }
        var v = _view;
        if (v == null) { _body.AddChild(UiKit.Body(L.T("TRADE_SETTLING"))); return; }
        _body.AddChild(LoadBar(v));
        _body.AddChild(new Label { Text = L.F("INV_PURSE", Quotes.Money(v.CoinMites), $"{Encumbrance.CoinMassG(v.CoinMites)} g"), ThemeTypeVariation = "GoldLabel" });
        // worn and held
        _body.AddChild(UiKit.Hint(L.T("INV_WORN")));
        foreach (var it in v.Items.Where(i => i.Slot != EquipSlot.None).OrderBy(i => (int)i.Slot).ThenBy(i => i.Id)) ItemRow(v, it, 0);
        // containers and what is in them
        foreach (var box in v.Items.Where(i => i.IsContainer).OrderBy(i => i.Depth).ThenBy(i => i.Id))
        {
            _body.AddChild(UiKit.Hint($"{box.Name}  {Litres(box.UsedMl)} / {Litres(box.CapacityMl)} L" + (box.Container != EntityId.None ? $"  ({L.F("INV_INSIDE", v.Items.FirstOrDefault(x => x.Id == box.Container)?.Name ?? "?")})" : "")));
            foreach (var it in v.Items.Where(i => i.Container == box.Id && i.Slot == EquipSlot.None)) ItemRow(v, it, 1);
            foreach (var g in v.Goods.Where(g => g.Container == box.Id)) GoodRow(v, g, 1);
            if (!v.Items.Any(i => i.Container == box.Id && i.Slot == EquipSlot.None) && !v.Goods.Any(g => g.Container == box.Id)) _body.AddChild(UiKit.Hint("    " + L.T("INV_EMPTY")));
        }
        var loose = v.Items.Where(i => i.Container == EntityId.None && i.Slot == EquipSlot.None && !i.IsContainer).ToList();
        var looseGoods = v.Goods.Where(g => g.Container == EntityId.None).ToList();
        if (loose.Count + looseGoods.Count > 0)
        {
            _body.AddChild(UiKit.Hint(L.T("INV_LOOSE")));
            foreach (var it in loose) ItemRow(v, it, 0);
            foreach (var g in looseGoods) GoodRow(v, g, 0);
        }
        // the quick bar
        var qb = new HBoxContainer(); qb.AddThemeConstantOverride("separation", 4);
        qb.AddChild(UiKit.Hint(L.T("INV_QUICKBAR")));
        for (int i = 1; i <= 8; i++) qb.AddChild(new Button { Text = i.ToString(), Disabled = true, CustomMinimumSize = new Vector2(30, 26) });
        _body.AddChild(qb);
    }

    void ItemRow(HeroView v, CarriedItem it, int indent)
    {
        var row = new HBoxContainer(); row.AddThemeConstantOverride("separation", 8);
        string slot = it.Slot != EquipSlot.None ? $"[{L.T("SLOT_" + it.Slot.ToString().ToUpperInvariant())}] " : "";
        // P5-A1 (items-quality-and-wear 4.4, 7): the condition bands (none from 700, worn, battered, ruined) and the tier's adjective ("fine arming sword, worn"); a named artefact by its name
        string cond = it.ConditionPermille >= 700 ? "" : it.ConditionPermille >= 400 ? L.T("WEAR_WORN") : it.ConditionPermille >= 150 ? L.T("WEAR_BATTERED") : L.T("WEAR_BROKEN");
        string tier = it.Quality == Ravensguard.Sim.Goods.ItemQuality.Common ? "" : L.T("iq_tier_" + it.Quality.ToString().ToLowerInvariant()) + " ";
        string label = v.Crafting?.Artefacts.Of(it.Id) is { } art ? L.F("iq_named", art.Name, tier + it.Name.ToLowerInvariant()) : tier + it.Name;
        var nameLabel = new Label { Text = new string(' ', indent * 4) + slot + label + (cond.Length > 0 ? $", {cond}" : ""), SizeFlagsHorizontal = SizeFlags.ExpandFill, ClipText = true };
        if ((it.Flags & Ravensguard.Sim.Goods.ItemFlags.Named) != 0) nameLabel.ThemeTypeVariation = "GoldLabel";
        // P5-E (items-quality-and-wear 7): the tooltip says what the tier and the wear do: a weapon's edge against the best its material takes (a masterwork at full condition), an armour's protection
        var tiers = Ravensguard.Sim.Goods.QualityTiers.Default;
        if (_hw.Content.Contains("weapon_shape", it.Shape))
            nameLabel.TooltipText = L.F("cf_ui_tip_edge", Ravensguard.Sim.Goods.QualityTiers.EdgePm(tiers.Combat(it.Quality), it.ConditionPermille) * 100 / tiers.Combat(Ravensguard.Sim.Goods.ItemQuality.Masterwork),
                it.Material.Length > 0 ? it.Material.Replace('_', ' ') : it.Name.ToLowerInvariant()) + ((it.Flags & Ravensguard.Sim.Goods.ItemFlags.Brittle) != 0 ? "\n" + L.T("cf_ui_brittle") : "");
        else if (_hw.Content.Contains("armour_shape", it.Shape))
            nameLabel.TooltipText = L.F("cf_ui_tip_protection", (tiers.Combat(it.Quality) / 1000.0).ToString("0.0#", System.Globalization.CultureInfo.InvariantCulture),
                it.Quality == Ravensguard.Sim.Goods.ItemQuality.Common ? L.T("Q_COMMON").ToLowerInvariant() : L.T("iq_tier_" + it.Quality.ToString().ToLowerInvariant()));
        if (nameLabel.TooltipText.Length > 0) nameLabel.MouseFilter = MouseFilterEnum.Stop;
        row.AddChild(nameLabel);
        row.AddChild(new Label { Text = $"{Kg(it.MassG)} kg", CustomMinimumSize = new Vector2(70, 0), HorizontalAlignment = HorizontalAlignment.Right });
        row.AddChild(new Label { Text = Quotes.Money(it.ValueMites), CustomMinimumSize = new Vector2(90, 0), HorizontalAlignment = HorizontalAlignment.Right, ThemeTypeVariation = "HintLabel" });
        if (it.Slot != EquipSlot.None) Btn(row, L.T("INV_TAKE_OFF"), new HeroCommand { Kind = HeroCommandKind.Unequip, Item = it.Id });
        else Btn(row, L.T("INV_EQUIP"), new HeroCommand { Kind = HeroCommandKind.Equip, Item = it.Id });
        // put into the next container that is not itself (cycles through them)
        var boxes = v.Items.Where(b => b.IsContainer && b.Id != it.Id).ToList();
        if (boxes.Count > 0)
        {
            int at = boxes.FindIndex(b => b.Id == it.Container);
            var next = at + 1 < boxes.Count ? boxes[at + 1] : null;
            Btn(row, next == null ? L.T("INV_TAKE_OUT") : L.F("INV_PUT_IN", next.Name), new HeroCommand { Kind = HeroCommandKind.PutItem, Item = it.Id, Container = next?.Id ?? EntityId.None });
        }
        Btn(row, L.T("INV_INSPECT"), new HeroCommand { Kind = HeroCommandKind.Inspect, Item = it.Id });
        _body.AddChild(row);
    }

    void GoodRow(HeroView v, CarriedGood g, int indent)
    {
        var row = new HBoxContainer(); row.AddThemeConstantOverride("separation", 8);
        row.AddChild(new Label { Text = new string(' ', indent * 4) + g.Name + (g.Piece ? $" x {g.Qty}" : $"  {Quotes.Weight(g.Qty)}"), SizeFlagsHorizontal = SizeFlags.ExpandFill, ClipText = true });
        row.AddChild(new Label { Text = $"{Kg(g.MassG)} kg", CustomMinimumSize = new Vector2(70, 0), HorizontalAlignment = HorizontalAlignment.Right });
        row.AddChild(new Label { Text = $"{Litres(g.VolumeMl)} L", CustomMinimumSize = new Vector2(90, 0), HorizontalAlignment = HorizontalAlignment.Right, ThemeTypeVariation = "HintLabel" });
        var boxes = v.Items.Where(b => b.IsContainer).ToList();
        if (boxes.Count > 0)
        {
            int at = boxes.FindIndex(b => b.Id == g.Container);
            var next = at + 1 < boxes.Count ? boxes[at + 1] : null;
            Btn(row, next == null ? L.T("INV_TAKE_OUT") : L.F("INV_PUT_IN", next.Name), new HeroCommand { Kind = HeroCommandKind.PutGood, Good = g.Good, Container = next?.Id ?? EntityId.None });
        }
        if (g.Piece) Btn(row, L.T("INV_EQUIP"), new HeroCommand { Kind = HeroCommandKind.Equip, Good = g.Good });
        _body.AddChild(row);
    }

    void Btn(HBoxContainer row, string text, HeroCommand c)
    {
        var b = new Button { Text = text, CustomMinimumSize = new Vector2(0, 24), Disabled = _waitSeq > 0 };
        b.Pressed += () => { _waitSeq = _hw.Issue(c with { IssuedSec = _now() }).Seq; Build(); };
        row.AddChild(b);
    }

    double _waitFor;

    public override void _Process(double delta)
    {
        _refresh += delta;
        if (_waitSeq > 0 && (_waitFor += delta) > 8) { _waitSeq = 0; _waitFor = 0; Build(); }       // the answer never came (the worker faulted, the result was trimmed): the buttons must not stay dead
        if (_waitSeq == 0) _waitFor = 0;
        if (_refresh > 0.2)
        {
            _refresh = 0;
            var v = _hw.View;
            if (!ReferenceEquals(v, _view))
            {
                if (_waitSeq > 0 && v?.Results.FirstOrDefault(r => r.Seq == _waitSeq) is { } res) { _status.Text = res.Text; _waitSeq = 0; }
                Build();
            }
        }
        if (ShotPath == null || !Visible || _view == null || ++_shotWait != 10) return;
        var img = GetViewport().GetTexture().GetImage();
        System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(ShotPath) ?? ".");
        GD.Print($"[inventory] screenshot {ShotPath}: {img.SavePng(ShotPath)}");
        GetTree().Quit();
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (!Visible) return;
        if (Actions.Pressed(e, "world.panel_inventory")) { _router.Pop(); GetViewport().SetInputAsHandled(); }
    }
}
