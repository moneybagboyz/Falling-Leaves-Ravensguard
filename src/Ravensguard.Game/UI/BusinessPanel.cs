using System;
using System.Linq;
using Godot;
using Ravensguard.Game.Inputs;
using Ravensguard.Game.UI.NewGame;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Production;
using Ravensguard.Sim.Trade;

namespace Ravensguard.Game.UI;

/// <summary>What the business panel can ask the street view to send to the hero's desk (each becomes one recorded command).</summary>
public sealed class BusinessActions
{
    public Action<EntityId, int, int>? Hire;
    public Action<EntityId, int>? Fire;
    public Action<EntityId, int>? Steward;
    public Action<EntityId>? Unsteward;
    public Action<EntityId, string>? Orders;
    public Action<EntityId, long>? Cash;
    public Action<EntityId>? SupplyAll;
    public Action<EntityId>? TakeOutputs;
    public Action<EntityId>? OpenStall;
    public Action<long>? MindHour;
    public Action<EntityId>? Pump;
    public Action<EntityId>? SellShare;
    /// <summary>P7-ES1: appoint a staff member foreman, dismiss one, petition the town's lord for a manufactory licence.</summary>
    public Action<EntityId, int>? Foreman;
    public Action<EntityId, int>? Unforeman;
    public Action<EntityId>? Manufactory;
    /// <summary>P7-ES2: end a supply contract the hero is a party to.</summary>
    public Action<int>? EndContract;
}

/// <summary>
/// The business panel (P5-F, businesses spec 6 detail): staff (hire from the place's best, let go), stations and their batches, the store, the recipes with their margin an hour, the directives (the
/// recipe, send profit every n days, keep in the box, hire up to), the steward (hire, dismiss; his price lag and reserve), the season history four seasons back, and "where is the money?" (the box, the
/// hero's purse, his chest, what was sent where). A view over the barrier's <see cref="BusinessView"/>; every button is one command through the hero's desk; a dismissal asks first (a modal).
/// </summary>
public partial class BusinessPanel : Control
{
    readonly ScreenRouter _router;
    readonly Func<BusinessView?> _data;
    readonly BusinessActions _act;
    VBoxContainer _body = null!;
    Label _title = null!, _status = null!;
    BusinessView? _shown;
    double _refresh;
    int _shotWait;
    public string? ShotPath { get; init; }

    public BusinessPanel(ScreenRouter router, Func<BusinessView?> data, BusinessActions act) { _router = router; _data = data; _act = act; }

    public override void _Ready()
    {
        var margin = UiKit.Margin(this, 12);
        var root = new VBoxContainer(); root.AddThemeConstantOverride("separation", 4); margin.AddChild(root);
        _title = UiKit.Title(L.T("bz_ui_title")); root.AddChild(_title);
        _status = UiKit.Hint(""); root.AddChild(_status);
        _body = new VBoxContainer(); _body.AddThemeConstantOverride("separation", 3);
        var p = UiKit.Panel(UiKit.Scroll(_body), 0, 0); p.SizeFlagsVertical = SizeFlags.ExpandFill; root.AddChild(p);
        var back = UiKit.Key(L.T("UI_BACK"), "menu.back", 200); back.Pressed += _router.Pop; root.AddChild(back);
        Build();
    }

    public override void _Process(double delta)
    {
        _refresh += delta;
        if (_refresh > 0.5) { _refresh = 0; if (!ReferenceEquals(_data(), _shown)) Build(); }
        if (ShotPath == null || !Visible || _shown == null || ++_shotWait != 12) return;
        var img = GetViewport().GetTexture().GetImage();
        System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(ShotPath) ?? ".");
        GD.Print($"[business] screenshot {ShotPath}: {img.SavePng(ShotPath)}");
        GetTree().Quit();
    }

    static string M(long m) => Quotes.Money(m);
    /// <summary>Micro-mites a gram as mites a kilogram with one decimal.</summary>
    static string Micro(long microPerGram) => $"{microPerGram / 1000}.{microPerGram % 1000 / 100}";

    Label Row(string text, bool dim = false)
    {
        var l = new Label { Text = text, SizeFlagsHorizontal = SizeFlags.ExpandFill, ClipText = true, TooltipText = text, MouseFilter = MouseFilterEnum.Stop };
        if (dim) l.AddThemeColorOverride("font_color", PixelTheme.Dim);
        return l;
    }

    HBoxContainer Line(string text, params (string Label, Action? Press, bool Enabled)[] buttons)
    {
        var row = new HBoxContainer(); row.AddThemeConstantOverride("separation", 6);
        row.AddChild(Row(text));
        foreach (var (label, press, en) in buttons) { var b = new Button { Text = label, Disabled = !en || press == null, CustomMinimumSize = new Vector2(96, 24) }; b.Pressed += () => press?.Invoke(); row.AddChild(b); }
        return row;
    }

    void Build()
    {
        foreach (var c in _body.GetChildren()) { _body.RemoveChild(c); c.QueueFree(); }
        var v = _data(); _shown = v;
        if (v == null) { _status.Text = L.T("lg_waiting"); return; }
        _title.Text = v.Name;
        var id = v.Id;
        _status.Text = L.F("bz_ui_status", M(v.Cash), M(v.Value), v.Staff.Count, v.Slots, M(v.ProjectedSeasonProfit), v.Owned ? L.T("bz_ui_owner") : L.F("bz_ui_shareholder", v.HeroPm / 10)) + (v.Alerts != OwnerAlert.None ? "  " + string.Join(" ", Enum.GetValues<OwnerAlert>().Where(a => a != OwnerAlert.None && (v.Alerts & a) != 0).Select(a => "!" + L.T("bz_ui_alert_" + a.ToString().ToLowerInvariant()))) : "");
        if (!v.Owned)
        {
            _body.AddChild(UiKit.Hint(L.F("bz_ui_share_note", v.HeroPm / 10, v.OwnerPm / 10), true));
            _body.AddChild(Line(L.T("bz_ui_sell_share"), (L.T("bz_ui_btn_sell"), () => _act.SellShare?.Invoke(id), v.HeroPm > 0)));
        }
        // where is the money
        _body.AddChild(UiKit.Hint(L.T("bz_ui_money")));
        _body.AddChild(UiKit.Body(L.F("bz_ui_money_row", M(v.Cash), M(v.HeroPurse), M(v.Chest), M(v.SentToPurse), M(v.SentToChest), M(v.DailyCost))));
        if (v.Owned) _body.AddChild(Line(L.T("bz_ui_cash"), (L.F("bz_ui_btn_in", M(5000)), () => _act.Cash?.Invoke(id, 5000), v.HeroPurse >= 5000), (L.F("bz_ui_btn_out", M(5000)), () => _act.Cash?.Invoke(id, -5000), v.Cash >= 5000)));
        // staff
        _body.AddChild(UiKit.Hint(L.F("bz_ui_staff", v.Staff.Count, v.Slots)));
        foreach (var s in v.Staff)
        {
            var who = s;
            _body.AddChild(Line(L.F("bz_ui_staff_row", s.Name, s.Occupation, s.Skill, M(s.WagePerDay), s.OwedMites > 0 ? L.F("bz_ui_owed", M(s.OwedMites)) : ""), (L.T("bz_ui_btn_fire"), v.Owned ? () => _router.ShowModal(L.T("bz_ui_fire_title"), L.F("bz_ui_fire_body", who.Name), (L.T("bz_ui_btn_fire"), () => _act.Fire?.Invoke(id, who.Person)), (L.T("UI_CANCEL"), null)) : null, v.Owned)));
        }
        if (v.Owned && v.Staff.Count < v.Slots)
            foreach (var c in v.Hire.Take(3))
            {
                var cand = c; int offer = Math.Max(c.CurrentWage + 50, c.Skill >= 25 ? 350 : 150);
                _body.AddChild(Line(L.F("bz_ui_cand_row", c.Name, c.Occupation, c.Skill, c.CurrentWage > 0 ? M(c.CurrentWage) : L.T("bz_ui_idle")), (L.F("bz_ui_btn_hire", M(offer)), () => _act.Hire?.Invoke(id, cand.Person, offer), true)));
            }
        // steward
        _body.AddChild(UiKit.Hint(L.T("bz_ui_steward")));
        if (v.Steward >= 0)
            _body.AddChild(Line(L.F("bz_ui_steward_row", v.StewardName, v.StewardTrading, v.StewardLeadership, v.StewardLag, M(v.StewardReserve)), (L.T("bz_ui_btn_dismiss"), v.Owned ? () => _act.Unsteward?.Invoke(id) : null, v.Owned)));
        else if (v.Owned)
        {
            _body.AddChild(Row(L.T("bz_ui_hands_on"), true));
            foreach (var c in v.Stewards.Take(2)) { var cand = c; _body.AddChild(Line(L.F("bz_ui_steward_cand", c.Name, c.Trading, c.Leadership), (L.T("bz_ui_btn_steward"), () => _act.Steward?.Invoke(id, cand.Person), true))); }
            if (v.Stewards.Count == 0) _body.AddChild(Row(L.T("bz_ui_no_steward_here"), true));
        }
        // directives
        if (v.Owned)
        {
            var o = v.Orders;
            _body.AddChild(UiKit.Hint(L.T("bz_ui_orders")));
            string recipe = v.OrderedRecipe.Length > 0 ? v.OrderedRecipe : L.T("bz_ui_by_margin");
            _body.AddChild(Line(L.F("bz_ui_order_recipe", recipe), (L.T("bz_ui_btn_next"), () => _act.Orders?.Invoke(id, "recipe=" + NextRecipe(v, v.OrderedRecipe)), v.Recipes.Count > 0)));
            _body.AddChild(Line(L.F("bz_ui_order_send", o.SendEveryDays, M(o.KeepMites)), ("-", () => _act.Orders?.Invoke(id, $"send_days={Math.Max(1, o.SendEveryDays - 1)}"), true), ("+", () => _act.Orders?.Invoke(id, $"send_days={o.SendEveryDays + 1}"), true)));
            _body.AddChild(Line(L.F("bz_ui_order_hire", o.HireUpTo < 0 ? L.T("bz_ui_slots") : o.HireUpTo.ToString()), ("-", () => _act.Orders?.Invoke(id, $"hire={Math.Max(0, (o.HireUpTo < 0 ? v.Slots : o.HireUpTo) - 1)}"), true), ("+", () => _act.Orders?.Invoke(id, $"hire={(o.HireUpTo < 0 ? v.Slots : o.HireUpTo) + 1}"), true)));
            _body.AddChild(Line(L.F("bz_ui_order_close", L.T(o.CloseAfterTwoLosses ? "bz_ui_yes" : "bz_ui_no")), (L.T("bz_ui_btn_toggle"), () => _act.Orders?.Invoke(id, "close=" + (o.CloseAfterTwoLosses ? "0" : "1")), true)));
        }
        // crew (P7-ES1, economies-of-scale 9): who stands at which station by role, the hours of the batch's grades, crowding, the span of control, foremen, the guild's verdict
        if (v.Crew is { } cw && (cw.Stations.Count > 0 || cw.Foremen.Count > 0 || cw.Guild.Length > 0))
        {
            _body.AddChild(UiKit.Hint(L.T("bz_ui_crew")));
            foreach (var cs in cw.Stations)
                _body.AddChild(Row(L.F("bz_ui_crew_row", cs.Kind, cs.Recipe, cs.Masters, cs.Journeymen, cs.Hands, Hours(cs.MinutesM), Hours(cs.MinutesJ), Hours(cs.MinutesA), Hours(cs.ElapsedMinutes), cs.Crowd == 0 ? "" : L.F(cs.Crowd == 1 ? "bz_ui_crowd_amber" : "bz_ui_crowd_red", cs.CrewMax)), cs.Crowd == 2));
            if (cw.Workers > 0) _body.AddChild(Row(L.F("bz_ui_span_row", cw.SpanSum, cw.Workers, cw.Excess, cw.SpeedPm / 10), cw.Excess > 0));
            foreach (var fm in cw.Foremen)
            {
                var f = fm;
                _body.AddChild(Line(L.F("bz_ui_foreman_row", f.Name, f.Leadership, f.Appointed ? L.T("bz_ui_foreman_on") : ""),
                    f.Appointed ? (L.T("bz_ui_btn_unappoint"), v.Owned ? () => _act.Unforeman?.Invoke(id, f.Person) : null, v.Owned) : (L.T("bz_ui_btn_appoint"), v.Owned ? () => _act.Foreman?.Invoke(id, f.Person) : null, v.Owned)));
            }
            if (cw.Guild.Length > 0)
                _body.AddChild(Line(L.F("bz_ui_guild_row", cw.Guild, cw.OverCaps, cw.Fines, cw.Licensed ? L.T("bz_ui_licensed") : ""), (L.T("bz_ui_btn_licence"), v.Owned && !cw.Licensed ? () => _act.Manufactory?.Invoke(id) : null, v.Owned && !cw.Licensed)));
        }
        // costs (P7-ES2, economies-of-scale 9): the variable cost of a unit, the unit cost at today's output and at the cap the markets in reach set, the machines and the supply contracts
        if (v.Costs is { } co && (co.Recipes.Count > 0 || co.Machines.Count > 0 || co.Contracts.Count > 0))
        {
            _body.AddChild(UiKit.Hint(L.T("bz_ui_costs")));
            foreach (var cr in co.Recipes.Take(6))
                _body.AddChild(Row(L.F("bz_ui_cost_row", cr.Recipe, Micro(cr.InputsMicroPerUnit + cr.WagesMicroPerUnit), Micro(cr.UnitCostMicroToday), Micro(cr.UnitCostMicroAtCap), cr.CapPerDay > 0 ? Quotes.Weight(cr.CapPerDay) : "-", cr.Stops ? L.T("bz_ui_cost_stops") : ""), cr.Stops));
            foreach (var mr in co.Machines)
                _body.AddChild(Row(mr.Cause.Length == 0 ? L.F("bz_ui_machine_on", mr.Kind, mr.LabourPm / 10) : L.F("bz_ui_machine_off", mr.Kind, L.T(mr.Cause)), mr.Cause.Length > 0));
            foreach (var ct in co.Contracts)
            {
                var c = ct;
                _body.AddChild(Line(L.F("bz_ui_contract_row", c.Id, c.Seller ? L.T("bz_ui_contract_to") : L.T("bz_ui_contract_from"), c.Other, c.Good, Quotes.Weight(c.QtyPerDelivery), c.EveryDays, c.Delivered, c.Missed, c.State),
                    (L.T("bz_ui_btn_end_contract"), v.Owned && c.State == "Open" ? () => _act.EndContract?.Invoke(c.Id) : null, v.Owned && c.State == "Open")));
            }
        }
        // stations, store, recipes
        _body.AddChild(UiKit.Hint(L.T("bz_ui_stations")));
        foreach (var s in v.Stations) _body.AddChild(Row(L.F("bz_ui_station_row", s.Kind, s.Recipe.Length > 0 ? s.Recipe : L.T("bz_ui_idle"), s.ProgressPm / 10), s.Recipe.Length == 0));
        _body.AddChild(Line(L.F("bz_ui_stock", v.Stock.Count == 0 ? L.T("bz_ui_empty") : string.Join(", ", v.Stock.Take(6).Select(x => (x.Piece ? x.Qty.ToString() : Quotes.Weight(x.Qty)) + " " + x.Name.ToLowerInvariant()))),
            (L.T("bz_ui_btn_supply"), v.Owned ? () => _act.SupplyAll?.Invoke(id) : null, v.Owned), (L.T("bz_ui_btn_take"), v.Owned ? () => _act.TakeOutputs?.Invoke(id) : null, v.Owned)));
        foreach (var r in v.Recipes.Take(5)) _body.AddChild(Row(L.F("bz_ui_recipe_row", r.Id, r.Station, M(r.MarginMites), r.WorkerHours, M(r.MarginPerHourMites)), r.MarginMites <= 0));
        if (v.MineInitialG > 0)
        {
            _body.AddChild(Line(L.F("bz_ui_mine", Quotes.Weight(v.MineRemainingG), Quotes.Weight(v.MineInitialG), v.MineState, M(v.RoyaltyPaid), v.Unlicensed ? L.T("bz_ui_unlicensed") : ""), (L.T("bz_ui_btn_pump"), v.MineState == "Flooded" && v.Owned ? () => _act.Pump?.Invoke(id) : null, v.MineState == "Flooded")));
        }
        // the stall
        if (v.Owned && v.Kind is not ("mine" or "manor"))
            _body.AddChild(v.StallSubject >= 0 ? Line(L.T("bz_ui_stall_on"), (L.T("bz_ui_btn_mind"), () => _act.MindHour?.Invoke(v.StallSubject), true)) : Line(L.T("bz_ui_stall_off"), (L.T("bz_ui_btn_stall"), () => _act.OpenStall?.Invoke(id), true)));
        // the season history
        _body.AddChild(UiKit.Hint(L.T("bz_ui_history")));
        if (v.History.Count == 0) _body.AddChild(Row(L.T("bz_ui_no_history"), true));
        foreach (var h in v.History) _body.AddChild(Row(L.F("bz_ui_history_row", UiKit.SeasonName((Season)(h.Season % 4)), h.Season / 4 + 1, M(h.Revenue), M(h.Wages), M(h.Inputs), M(h.Profit), M(h.Paid)), h.Profit < 0));
        _body.AddChild(UiKit.Hint(L.F("bz_ui_projection", M(v.ProjectedSeasonProfit), M(350L * 15)), true));
    }

    static string Hours(long minutes) => $"{minutes / 60}.{minutes % 60 * 10 / 60}";

    static string NextRecipe(BusinessView v, string cur)
    {
        var ids = v.Recipes.Select(r => r.Id).ToList();
        int i = ids.IndexOf(cur);
        return i + 1 < ids.Count ? ids[i + 1] : "-";
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (!Visible || _router.HasModal) return;
        if (Actions.Pressed(e, "world.context_menu")) { _router.Pop(); GetViewport().SetInputAsHandled(); }
    }
}
