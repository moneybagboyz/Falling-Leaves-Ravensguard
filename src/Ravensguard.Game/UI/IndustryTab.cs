using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Ravensguard.Game.UI.NewGame;
using Ravensguard.Sim.Production.Industry;

namespace Ravensguard.Game.UI;

/// <summary>What the Industry tab can ask of the world; each is one <c>in:</c> command through the hero desk (the desk checks and answers; the tab keeps no state of the works).</summary>
public sealed class IndustryActions
{
    public Action<ulong, int>? Crew, Wage;
    public Action<ulong, string>? Route;
    public Action<ulong>? Send, Upgrade;
}

/// <summary>
/// The Industry tab of the Ledger (industries 6, P7-IN3): the chain as a row of stage boxes (tier, crew, throughput, stock, the link's hauling), the bottleneck outlined in amber with its reason and a stopped stage in red, the selected stage's detail (crew, wage, inputs, stop,
/// the next upgrade, the gate against the works' own caravan), and the balance sheet by day and by 6-day week in silver with one decimal. A view over the barrier's <see cref="IndustryView"/>: only the cursor lives here; every change is a command.
/// Up and Down pick a stage (through every works), Left and Right the crew target, Enter the one-click upgrade plan, S the route, A the wage offer, / sends the caravan.
/// </summary>
public sealed class IndustryTab
{
    const int FontSize = 13;
    static readonly Color Amber = new("f0a020"), Red = new("e05a4a"), Dim = new("9a9a8a");
    readonly IndustryActions _act;
    int _cursor;
    IndustryView? _view;

    public IndustryTab(IndustryActions act) { _act = act; }

    /// <summary>The stages of every works in order: the cursor's rows.</summary>
    IEnumerable<(WorksView W, StageView S, int Index)> Rows() { if (_view != null) foreach (var w in _view.Works) for (int i = 0; i < w.Stages.Count; i++) yield return (w, w.Stages[i], i); }
    (WorksView W, StageView S, int Index)? Selected() { var rows = Rows().ToList(); return rows.Count == 0 ? null : rows[Math.Clamp(_cursor, 0, rows.Count - 1)]; }
    int Count => Rows().Count();

    public void Move(int d) { int n = Count; if (n > 0) _cursor = ((Math.Clamp(_cursor, 0, n - 1) + d) % n + n) % n; }
    public void Crew(int d) { if (Selected() is { } r) _act.Crew?.Invoke(r.S.Business, Math.Clamp(r.S.CrewTarget + d, r.S.CrewMin, r.S.CrewMax)); }
    public void Upgrade() { if (Selected() is { } r) _act.Upgrade?.Invoke(r.S.Business); }
    public void Send() { if (Selected() is { } r) _act.Send?.Invoke(r.S.Business); }

    /// <summary>The route that follows the stage's: on to the next stage, the gate, the store, then its own caravan once an order stands.</summary>
    public void CycleRoute()
    {
        if (Selected() is not { } r) return;
        var order = new List<Routing> { Routing.Next, Routing.Gate, Routing.Store }; if (r.S.HasOrder) order.Add(Routing.Caravan);
        int at = order.IndexOf(r.S.Route);
        _act.Route?.Invoke(r.S.Business, order[(at + 1) % order.Count].ToString());
    }

    /// <summary>The wage offer steps up by 10 percent of the class wage and wraps from 150 back to 70 (the desk clamps it to 50-200).</summary>
    public void CycleWage() { if (Selected() is { } r) _act.Wage?.Invoke(r.S.Business, r.S.WageOfferPm >= 1500 ? 700 : r.S.WageOfferPm + 100); }

    /// <summary>The bottleneck line for the status bar (amber), or empty.</summary>
    public string Status(IndustryView? v)
    {
        if (v == null || v.IsEmpty) return "";
        var w = Selected()?.W ?? v.Works[0];
        return Bottleneck(w);
    }

    static string StageName(StageView s) => L.T("ind_stage_" + s.KindId);
    static string Kg(long grams, bool piece) => piece ? grams.ToString("N0") : (grams / 1000).ToString("N0") + " kg";
    static string Reason(StageView s) => L.T("ind_stop_" + s.Stop.ToString().ToLowerInvariant()) + (s.StopGood.Length > 0 ? " (" + s.StopGood + ")" : "");

    static string Bottleneck(WorksView w) => w.Bottleneck switch
    {
        BottleneckKind.Stopped => L.F("ui_ind_bn_stopped", StageName(w.Stages[w.BottleneckStage]), Reason(w.Stages[w.BottleneckStage]), w.Stages[w.BottleneckStage].StopDays),
        BottleneckKind.Hauling => L.F("ui_ind_bn_hauling", StageName(w.Stages[w.Links[w.BottleneckLink].From]), StageName(w.Stages[w.BottleneckStage]), Kg(w.Links[w.BottleneckLink].MovedG, false), Kg(w.Stages[w.Links[w.BottleneckLink].From].MadeG, false), Kg(w.Links[w.BottleneckLink].CapG, false)),
        BottleneckKind.Slowest => L.F("ui_ind_bn_slowest", StageName(w.Stages[w.BottleneckStage]), Kg(w.Stages[w.BottleneckStage].MadeG, w.Stages[w.BottleneckStage].Piece), Kg(w.Stages[w.BottleneckStage].PotentialG, w.Stages[w.BottleneckStage].Piece)),
        _ => L.T("ui_ind_bn_none"),
    };

    // ------------------------------------------------------------------ drawing

    static Label Small(string text, Color? color = null, bool wrap = false)
    {
        var l = new Label { Text = text, ClipText = !wrap, AutowrapMode = wrap ? TextServer.AutowrapMode.WordSmart : TextServer.AutowrapMode.Off, MouseFilter = Control.MouseFilterEnum.Pass, TooltipText = text, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        l.AddThemeFontSizeOverride("font_size", FontSize);
        if (color != null) l.AddThemeColorOverride("font_color", color.Value);
        return l;
    }

    static Button Btn(string text, Action act)
    {
        var b = new Button { Text = text, FocusMode = Control.FocusModeEnum.None, CustomMinimumSize = new Vector2(0, 20) };
        b.AddThemeFontSizeOverride("font_size", FontSize);
        b.Pressed += act;
        return b;
    }

    /// <summary>Fills <paramref name="host"/> (an empty container) with the tab for this view.</summary>
    public void Build(Control host, IndustryView? v)
    {
        _view = v;
        var root = new VBoxContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; root.AddThemeConstantOverride("separation", 3);
        host.AddChild(root);
        if (v == null || v.IsEmpty) { root.AddChild(UiKit.Hint(L.T(v == null ? "lg_waiting" : "ui_ind_empty"), true)); return; }
        _cursor = Math.Clamp(_cursor, 0, Count - 1);
        var sel = Selected()!.Value; var w = sel.W;
        root.AddChild(Head(w));
        root.AddChild(Flow(w, sel.Index));
        var low = new HBoxContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; low.AddThemeConstantOverride("separation", 6);
        low.AddChild(Pane(Detail(w, sel.S, sel.Index), 3));
        low.AddChild(Pane(Balance(w, sel.Index), 2));
        root.AddChild(low);
    }

    static Control Pane(Control content, int ratio)
    {
        var sc = UiKit.Scroll(content); sc.SizeFlagsStretchRatio = ratio; sc.CustomMinimumSize = new Vector2(0, 0);
        return sc;
    }

    Control Head(WorksView w)
    {
        string payback = w.PaidPm >= 1000 || w.PaybackDays == 0 ? L.T("ui_ind_paid_back") : w.PaybackDays > 0 ? L.F("ui_ind_payback", w.PaybackDays) : L.T("ui_ind_payback_none");
        string place = L.F("ui_ind_works", L.T("ind_chain_" + w.Chain), w.Place, w.Warning ? "!" : "");
        string line = place + "   " + L.F("ui_ind_net", IndustryViews.Silver(w.Day.Net), IndustryViews.Silver(w.Week.Net)) + "   " + L.F("ui_ind_buildings", IndustryViews.Silver(w.BuildingsCostMites), w.PaidPm / 10) + "   " + payback;
        return Small(line, w.Warning ? Amber : PixelTheme.Gold);
    }

    Control Flow(WorksView w, int sel)
    {
        var row = new HBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; row.AddThemeConstantOverride("separation", 4);
        for (int i = 0; i < w.Stages.Count; i++)
        {
            var s = w.Stages[i];
            bool bn = w.Bottleneck != BottleneckKind.None && w.BottleneckStage == i, stopped = s.Stop is not (StageStop.None or StageStop.Full);
            var box = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; box.AddThemeConstantOverride("separation", 0);
            box.AddChild(Small(StageName(s) + "  " + L.F("ui_ind_tier", s.Tier) + (s.ShortHanded ? "*" : ""), PixelTheme.Gold));
            box.AddChild(Small(L.F("ui_ind_crew_box", s.Have, s.CrewMin, s.CrewMax)));
            box.AddChild(Small(L.F("ui_ind_made", Kg(s.MadeG, s.Piece))));
            var link = w.Links.FirstOrDefault(l => l.To == i);
            box.AddChild(link != null && link.CapG >= 0 ? Small(L.F("ui_ind_haul", Kg(link.MovedG, false), Kg(link.CapG, false)), link.Short ? Amber : Dim) : Small(s.Stock.Count > 0 ? s.Stock[0].Good + " " + Kg(s.Stock[0].Qty, s.Stock[0].Piece) : L.T("ui_ind_no_stock"), Dim));
            box.AddChild(Small(stopped ? Reason(s) : s.Stop == StageStop.Full ? L.T("ind_stop_full") : L.T("ui_ind_working"), stopped ? Red : Dim, true));
            var panel = new PanelContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(0, 0), TooltipText = StageName(s) };
            var sb = new StyleBoxFlat { BgColor = new Color(0.1f, 0.09f, 0.08f, 0.9f), ContentMarginLeft = 4, ContentMarginRight = 4, ContentMarginTop = 2, ContentMarginBottom = 2 };
            int bw = i == sel ? 2 : bn || stopped ? 2 : 1;
            sb.SetBorderWidthAll(bw);
            sb.BorderColor = bn ? Amber : stopped ? Red : i == sel ? PixelTheme.Gold : new Color(0.3f, 0.28f, 0.22f);
            panel.AddThemeStyleboxOverride("panel", sb);
            panel.AddChild(box);
            int idx = i;
            panel.GuiInput += e => { if (e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left }) { int at = 0; foreach (var x in _view!.Works) { if (x == w) break; at += x.Stages.Count; } _cursor = at + idx; Refresh?.Invoke(); } };
            row.AddChild(panel);
        }
        return row;
    }

    /// <summary>Set by the screen: redraws after the cursor moved with the mouse.</summary>
    public Action? Refresh;

    Control Detail(WorksView w, StageView s, int index)
    {
        var box = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; box.AddThemeConstantOverride("separation", 1);
        box.AddChild(Small(StageName(s) + "  " + L.F("ui_ind_tier_of", s.Tier, s.TierCount) + "  " + L.F("ui_ind_route", L.T("ui_ind_route_" + s.Route.ToString().ToLowerInvariant())), PixelTheme.Gold));
        // the crew and the wage, with the buttons that move them
        var crew = new HBoxContainer(); crew.AddThemeConstantOverride("separation", 4);
        crew.AddChild(Btn("-", () => Crew(-1))); crew.AddChild(Btn("+", () => Crew(1)));
        crew.AddChild(Small(L.F("ui_ind_crew", s.CrewTarget, s.CrewMin, s.CrewMax, s.Have) + "  " + L.F("ui_ind_wage", s.WageOfferPm / 10, IndustryViews.Silver(s.WagePerDayMites))));
        crew.AddChild(Btn(L.T("ui_ind_btn_wage"), CycleWage)); crew.AddChild(Btn(L.T("ui_ind_btn_route"), CycleRoute));
        box.AddChild(crew);
        box.AddChild(Small(s.Stop is StageStop.None ? L.T("ui_ind_working") : L.F("ui_ind_stopped", Reason(s), s.StopDays), s.Stop is StageStop.None or StageStop.Full ? Dim : Red, true));
        foreach (var i in s.Inputs) box.AddChild(Small(L.F("ui_ind_input", i.Good, Kg(i.StockG, false), Kg(i.PerDayG, false), (i.CoverDaysTenths / 10) + "." + (i.CoverDaysTenths % 10), L.T(i.FromChain ? "ui_ind_src_chain" : "ui_ind_src_market")), i.CoverDaysTenths < 10 && i.PerDayG > 0 ? Amber : null, true));
        // the next upgrade and its one-click plan
        if (s.Upgrade is { } u)
        {
            var up = new HBoxContainer(); up.AddThemeConstantOverride("separation", 4);
            up.AddChild(Btn(L.F("ui_ind_btn_upgrade", u.Tier), Upgrade));
            up.AddChild(Small(u.InHand ? L.T("ui_ind_upgrade_hand") : L.F("ui_ind_upgrade", u.Tier, IndustryViews.Silver(u.BillMites), u.CrewMin, u.CrewMax), PixelTheme.Gold));
            box.AddChild(up);
            var extra = new List<string>();
            if (u.Missing.Count > 0) extra.Add(L.F("ui_ind_missing", string.Join(", ", u.Missing)));
            if (u.HireMore > 0) extra.Add(L.F("ui_ind_hire_more", u.HireMore));
            if (u.ExtraGPerDay > 0) extra.Add(L.F("ui_ind_extra", Kg(u.ExtraGPerDay, s.Piece)));
            extra.Add(u.PaybackDays > 0 ? L.F("ui_ind_upgrade_payback", u.PaybackDays, IndustryViews.Silver(u.ExtraNetPerDayMites)) : L.T("ui_ind_upgrade_payback_none"));
            box.AddChild(Small(string.Join("; ", extra), null, true));
        }
        else box.AddChild(Small(L.T("ui_ind_top_tier"), Dim));
        // selling: the gate's price and what the books take, then the works' own caravan beside it
        foreach (var g in s.Sell)
            box.AddChild(Small(L.F("ui_ind_gate", g.Good, Kg(g.StockG, false), (g.NetPerKgMilli / 1000.0).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture), g.GateTown, Kg(g.AbsorbPerDay, false), g.DaysToClear > 9999 ? "-" : g.DaysToClear.ToString()), g.Over ? Amber : null, true));
        if (s.Caravan is { } c)
        {
            var line = new HBoxContainer(); line.AddThemeConstantOverride("separation", 4);
            line.AddChild(Btn(L.T("ui_ind_btn_send"), Send));
            line.AddChild(Small(c.Ok ? L.F("ui_ind_caravan", c.Dest, c.Wagons, c.Guards, c.Days) : L.F("ui_ind_caravan_no", c.Dest, L.T(c.Why)), c.Ok ? null : Dim, true));
            box.AddChild(line);
            if (c.Ok)
            {
                box.AddChild(Small(L.F("ui_ind_quote", IndustryViews.Silver(c.GrossMites), IndustryViews.Silver(c.CostMites), IndustryViews.Silver(c.RiskMites), IndustryViews.Silver(c.MarginMites), IndustryViews.Silver(c.GateNetMites)), c.MarginMites > c.GateNetMites ? null : Amber, true));
                box.AddChild(Small(string.Join(", ", c.Parts.Where(p => p.Mites != 0).Select(p => L.T(p.Key) + " " + IndustryViews.Silver(p.Mites))) + (c.PriceAgeDays > 0 ? "; " + L.F("ui_ind_price_age", c.PriceAgeDays) : ""), Dim, true));
            }
            if (c.Away) box.AddChild(Small(L.T("in_caravan_away"), Dim));
        }
        if (w.CaravanTrips + w.CaravansLost > 0) box.AddChild(Small(L.F("ui_ind_tally", w.CaravanTrips, w.CaravansLost, IndustryViews.Silver(w.CaravanNetMites)), Dim));
        return box;
    }

    Control Balance(WorksView w, int index)
    {
        var g = new GridContainer { Columns = 5, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; g.AddThemeConstantOverride("h_separation", 6); g.AddThemeConstantOverride("v_separation", 0);
        var sd = w.StageDay[index]; var sw = w.StageWeek[index];
        void Row(string key, Func<BalanceRow, long> f, Color? c = null)
        {
            g.AddChild(Small(L.T(key), c)); foreach (var r in new[] { w.Day, w.Week, sd, sw }) { var l = Small(IndustryViews.Silver(f(r)), c); l.HorizontalAlignment = HorizontalAlignment.Right; g.AddChild(l); }
        }
        g.AddChild(Small(""));
        foreach (var k in new[] { "ui_ind_col_day", "ui_ind_col_week", "ui_ind_col_stage_day", "ui_ind_col_stage_week" }) { var l = Small(L.T(k), Dim); l.HorizontalAlignment = HorizontalAlignment.Right; g.AddChild(l); }
        Row("ui_ind_row_income", r => r.Income); Row("ui_ind_row_wages", r => -r.Wages); Row("ui_ind_row_inputs", r => -r.Inputs); Row("ui_ind_row_upkeep", r => -r.Upkeep); Row("ui_ind_row_royalties", r => -r.Royalties);
        Row("ui_ind_row_net", r => r.Net, PixelTheme.Gold);
        var box = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; box.AddThemeConstantOverride("separation", 2);
        box.AddChild(Small(L.F("ui_ind_sheet", w.LastDay < 0 ? "-" : (w.LastDay + 1).ToString(), WorksView.WeekDays), Dim));
        box.AddChild(g);
        return box;
    }
}
