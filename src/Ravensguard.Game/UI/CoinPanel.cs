using System;
using Godot;
using Ravensguard.Game.UI.NewGame;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Trade;

namespace Ravensguard.Game.UI;

/// <summary>Which counter the coin panel is.</summary>
public enum CoinCounter { Mint, Changer }

/// <summary>
/// P7-CN2 (coinage 9): the mint counter (Q at a mint town > Sell metal at the mint: the metal in the pack with the mint's rate after its cut, the moneyers' queue) and the money changer
/// (Q at a town with a changer > Change money: silver and gold side by side, the fee, the gold to be had today). A view over the barrier's <see cref="CoinView"/>; every button sends one
/// <c>cn:</c> command through the hero's desk (<see cref="CoinDesk"/>) and the answer is toasted by the street view. The router's Back closes it.
/// </summary>
public partial class CoinPanel : Control
{
    readonly ScreenRouter _router;
    readonly CoinCounter _kind;
    readonly Func<CoinView?> _data;
    readonly Action<MintMetal, long> _sell;
    readonly Action<bool, long> _change;
    VBoxContainer _body = null!;
    CoinView? _shown;
    double _refresh;

    public CoinPanel(ScreenRouter router, CoinCounter kind, Func<CoinView?> data, Action<MintMetal, long> sell, Action<bool, long> change)
    { _router = router; _kind = kind; _data = data; _sell = sell; _change = change; }

    public override void _Ready()
    {
        var margin = UiKit.Margin(this, 12);
        var root = new VBoxContainer(); root.AddThemeConstantOverride("separation", 4); margin.AddChild(root);
        root.AddChild(UiKit.Title(L.T(_kind == CoinCounter.Mint ? "co_ui_mint_title" : "co_ui_changer_title")));
        _body = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill }; _body.AddThemeConstantOverride("separation", 4);
        var sc = UiKit.Scroll(_body); sc.SizeFlagsVertical = SizeFlags.ExpandFill; root.AddChild(UiKit.Panel(sc, 0, 0));
        var buttons = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.End };
        var back = UiKit.Key(L.T("UI_BACK"), "menu.back", 160, 32); back.Pressed += _router.Pop; buttons.AddChild(back);
        root.AddChild(buttons);
        Build();
    }

    public override void _Process(double delta)
    {
        _refresh += delta;
        if (_refresh > 0.5) { _refresh = 0; if (!ReferenceEquals(_data(), _shown)) Build(); }
    }

    static Button Act(string text, Action go, bool off = false)
    {
        var b = new Button { Text = text, CustomMinimumSize = new Vector2(0, 28), Disabled = off, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        b.Pressed += go; return b;
    }

    void Build()
    {
        foreach (var c in _body.GetChildren()) { _body.RemoveChild(c); c.QueueFree(); }
        _shown = _data();
        if (_shown is not { } v) { _body.AddChild(UiKit.Hint(L.T("co_ui_wait"), true)); return; }
        _body.AddChild(UiKit.Hint(L.F("co_ui_purse", UiKit.Money(v.Mites), v.GoldPieces), true));
        if (_kind == CoinCounter.Mint) BuildMint(v); else BuildChanger(v);
    }

    // ------------------------------------------------------------------ the mint

    void BuildMint(CoinView v)
    {
        string? shut = !v.HasMint ? "co_no_mint" : !v.MintStaffed ? "co_mint_closed" : !v.MintOpenNow ? "co_mint_shut" : null;
        if (shut != null) _body.AddChild(UiKit.Body(L.T(shut), true));
        Metal(v, MintMetal.Gold, "co_ui_gold", v.GoldG, v.GoldKgPaidMites, shut != null);
        Metal(v, MintMetal.Silver, "co_ui_silver", v.SilverG, v.SilverKgPaidMites, shut != null);
        Metal(v, MintMetal.Copper, "co_ui_copper", v.CopperG, 0, shut != null);
        if (v.QueueGoldG + v.QueueSilverG > 0) _body.AddChild(UiKit.Hint(L.F("co_ui_queue", Quotes.Weight(v.QueueGoldG), Quotes.Weight(v.QueueSilverG)), true));
    }

    void Metal(CoinView v, MintMetal metal, string nameKey, long grams, long kgPaid, bool off)
    {
        var box = new VBoxContainer(); box.AddThemeConstantOverride("separation", 2); _body.AddChild(box);
        box.AddChild(UiKit.Body(L.F("co_ui_carry", L.T(nameKey), Quotes.Weight(grams)), true));
        box.AddChild(UiKit.Hint(metal == MintMetal.Copper ? L.T("co_ui_rate_copper") : L.F("co_ui_rate", UiKit.Money(kgPaid, Ravensguard.Sim.Core.MoneyStyle.Exact)), true));
        var row = new HBoxContainer(); row.AddThemeConstantOverride("separation", 4); box.AddChild(row);
        foreach (long g in new long[] { 100, 1000 })
            row.AddChild(Act(L.F("co_ui_sell", Quotes.Weight(g)), () => _sell(metal, g), off || grams < g));
        row.AddChild(Act(L.T("co_ui_sell_all"), () => _sell(metal, grams), off || grams <= 0));
    }

    // ------------------------------------------------------------------ the changer

    void BuildChanger(CoinView v)
    {
        if (!v.HasChanger) { _body.AddChild(UiKit.Body(L.T("co_no_changer"), true)); return; }
        _body.AddChild(UiKit.Hint(L.F("co_ui_chg_fee", v.FeePermille, v.GoldAvailable), true));
        var cols = new HBoxContainer(); cols.AddThemeConstantOverride("separation", 8); _body.AddChild(cols);
        var left = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill }; left.AddThemeConstantOverride("separation", 2); cols.AddChild(left);
        var right = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill }; right.AddThemeConstantOverride("separation", 2); cols.AddChild(right);
        left.AddChild(UiKit.Body(L.F("co_ui_chg_silver_head", UiKit.Money(v.Mites)), true));
        foreach (long gp in new long[] { 1, 5, 10 })
        {
            long mites = Ravensguard.Sim.Economy.Coin.FromGold(gp).Mites;
            left.AddChild(Act(L.F("co_ui_chg_to_gold", UiKit.Money(mites), UiKit.Money(mites * v.FeePermille / 1000, Ravensguard.Sim.Core.MoneyStyle.Exact)), () => _change(true, mites), v.Mites < mites || v.GoldAvailable <= 0));
        }
        right.AddChild(UiKit.Body(L.F("co_ui_chg_gold_head", v.GoldPieces), true));
        foreach (long gp in new long[] { 1, 5, 10 })
            right.AddChild(Act(L.F("co_ui_chg_to_silver", gp, UiKit.Money(Ravensguard.Sim.Economy.Coin.FromGold(gp).Mites * v.FeePermille / 1000, Ravensguard.Sim.Core.MoneyStyle.Exact)), () => _change(false, gp), v.GoldPieces < gp));
        right.AddChild(Act(L.F("co_ui_chg_all_silver", v.GoldPieces), () => _change(false, v.GoldPieces), v.GoldPieces <= 0));
    }
}
