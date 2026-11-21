using System;
using System.Linq;
using Godot;
using Ravensguard.Game.Inputs;
using Ravensguard.Game.UI.NewGame;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Land;
using Ravensguard.Sim.Trade;

namespace Ravensguard.Game.UI;

/// <summary>What the land screen can ask the street view to send to the hero's desk (each becomes one recorded command).</summary>
public sealed class LandActions
{
    public Action<Listing>? Buy;
    public Action<int>? LeaseField;
    public Action<int, bool>? RentStall;
    public Action<EntityId, bool>? Renew;
    public Action<EntityId, long>? Offer;
    public Action<int>? Pull;
}

/// <summary>
/// The thin deed and lot screen (P5-A2, land spec 6): <b>Lots and leases</b> is the lord's officer's table in a place (plots for sale with size, price, dues and tenure; fields the lord would let;
/// market stalls by the season or the fair day); <b>Deeds</b> is the hero's property (every deed with its terms, dues, arrears and lease end; his claims with their deadline; his charters with the
/// households settled). A view over the barrier's <see cref="LandView"/>; every button sends one command through the hero's desk, and a buy asks first (a modal: the street keys stay off).
/// </summary>
public partial class LandScreen : Control
{
    readonly ScreenRouter _router;
    readonly Func<LandView?> _data;
    readonly LandActions _act;
    VBoxContainer _body = null!;
    Label _status = null!;
    bool _deeds;
    double _refresh;
    int _shotWait;
    LandView? _shown;
    /// <summary>Dev: when set, the screen saves its own picture to this path and quits.</summary>
    public string? ShotPath { get; init; }

    public LandScreen(ScreenRouter router, Func<LandView?> data, LandActions act, bool deeds) { _router = router; _data = data; _act = act; _deeds = deeds; }

    public override void _Ready()
    {
        var margin = UiKit.Margin(this, 14);
        var root = new VBoxContainer(); root.AddThemeConstantOverride("separation", 6); margin.AddChild(root);
        var head = new HBoxContainer(); head.AddThemeConstantOverride("separation", 8);
        var title = UiKit.Title(L.T(_deeds ? "ld_title_deeds" : "ld_title_market")); title.SizeFlagsHorizontal = SizeFlags.ExpandFill; head.AddChild(title);
        var other = new Button { Text = L.T(_deeds ? "ld_tab_market" : "ld_tab_deeds"), CustomMinimumSize = new Vector2(150, 30) };
        other.Pressed += () => { _deeds = !_deeds; title.Text = L.T(_deeds ? "ld_title_deeds" : "ld_title_market"); other.Text = L.T(_deeds ? "ld_tab_market" : "ld_tab_deeds"); _shown = null; Build(); };
        head.AddChild(other); root.AddChild(head);
        _status = UiKit.Hint(""); root.AddChild(_status);
        _body = new VBoxContainer(); _body.AddThemeConstantOverride("separation", 4);
        var p = UiKit.Panel(UiKit.Scroll(_body), 0, 0); p.SizeFlagsVertical = SizeFlags.ExpandFill; root.AddChild(p);       // the lists scroll; Back stays on screen
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
        GD.Print($"[land] screenshot {ShotPath}: {img.SavePng(ShotPath)}");
        GetTree().Quit();
    }

    static string M(long mites) => UiKit.Money(mites);
    static string Tenure(Tenure t) => L.T("ld_tenure_" + t.ToString().ToLowerInvariant());
    static string Day(int day) => L.F("ld_day", day % Ravensguard.Sim.Core.TimeConst.DaysPerSeason + 1, UiKit.SeasonName(GameTime.FromGameDays(day).Season), GameTime.FromGameDays(day).Year);

    Label Row(string text, bool dim = false)
    {
        var l = new Label { Text = text, SizeFlagsHorizontal = SizeFlags.ExpandFill, ClipText = true, TooltipText = text, MouseFilter = MouseFilterEnum.Stop };
        if (dim) l.AddThemeColorOverride("font_color", PixelTheme.Dim);
        return l;
    }

    HBoxContainer Line(string text, string? button, Action? press, bool enabled = true, int width = 110)
    {
        var row = new HBoxContainer(); row.AddThemeConstantOverride("separation", 8);
        row.AddChild(Row(text));
        if (button != null) { var b = new Button { Text = button, Disabled = !enabled || press == null, CustomMinimumSize = new Vector2(width, 26) }; b.Pressed += () => press?.Invoke(); row.AddChild(b); }
        return row;
    }

    void Build()
    {
        foreach (var c in _body.GetChildren()) { _body.RemoveChild(c); c.QueueFree(); }
        var v = _data(); _shown = v;
        if (v == null) { _status.Text = L.T("ld_waiting"); return; }
        _status.Text = L.F("ld_status", v.Place.Length > 0 ? v.Place : L.T("ld_no_place"), M(v.CoinMites));
        if (_deeds) BuildDeeds(v); else BuildMarket(v);
    }

    void BuildMarket(LandView v)
    {
        _body.AddChild(UiKit.Hint(L.T("ld_head_lots")));
        if (v.Listings.Count == 0) _body.AddChild(Row(L.T("ld_none_for_sale"), true));
        foreach (var l in v.Listings)
        {
            string what = l.Lot >= 0 ? L.F("ld_lot_house", l.Rect.W, l.Rect.H) : L.F("ld_lot_plot", l.Rect.W, l.Rect.H);
            var ll = l;
            _body.AddChild(Line(L.F("ld_lot_row", what, M(l.PriceMites), Tenure(l.Tenure), M(l.DuesMites)), L.T("ld_btn_buy"), () => ConfirmBuy(ll), v.CoinMites >= l.PriceMites));
        }
        _body.AddChild(UiKit.Hint(L.T("ld_head_fields")));
        if (v.Fields.Count == 0) _body.AddChild(Row(L.T("ld_no_fields"), true));
        foreach (var (p, fields, rent) in v.Fields)
        {
            int pp = p;
            _body.AddChild(Line(L.F("ld_field_row", fields, M(rent)), L.T("ld_btn_lease"), () => _act.LeaseField?.Invoke(pp)));
        }
        _body.AddChild(UiKit.Hint(L.T("ld_head_stalls")));
        if (v.FreeStalls.Count == 0) _body.AddChild(Row(L.T("ld_no_stalls"), true));
        else
        {
            int k = v.FreeStalls[0];
            _body.AddChild(Line(L.F("ld_stall_row", v.FreeStalls.Count, M(v.StallSeasonMites)), L.T("ld_btn_season"), () => _act.RentStall?.Invoke(k, false), v.CoinMites >= v.StallSeasonMites));
            _body.AddChild(Line(L.F("ld_stall_day_row", M(v.StallDayMites)), L.T("ld_btn_day"), () => _act.RentStall?.Invoke(k, true), v.CoinMites >= v.StallDayMites));
        }
        _body.AddChild(UiKit.Hint(L.T("ld_market_note"), true));
    }

    void ConfirmBuy(Listing l)
    {
        string what = l.Lot >= 0 ? L.F("ld_lot_house", l.Rect.W, l.Rect.H) : L.F("ld_lot_plot", l.Rect.W, l.Rect.H);
        _router.ShowModal(L.T("ld_buy_title"), L.F("ld_buy_body", what, M(l.PriceMites), Tenure(l.Tenure), M(l.DuesMites)),
            (L.T("ld_btn_buy"), () => _act.Buy?.Invoke(l)), (L.T("UI_CANCEL"), null));
    }

    void BuildDeeds(LandView v)
    {
        _body.AddChild(UiKit.Hint(L.T("ld_head_deeds")));
        if (v.Deeds.Count == 0) _body.AddChild(Row(L.T("ld_no_deeds"), true));
        foreach (var d in v.Deeds)
        {
            string kind = L.T("ld_kind_" + d.Kind.ToString().ToLowerInvariant());
            string terms = L.F("ld_deed_row", kind, d.Where, Tenure(d.Tenure), d.DuesMites > 0 ? M(d.DuesMites) : "-");
            if (d.LeaseEndDay >= 0) terms += "  " + L.F("ld_lease_end", Day(d.LeaseEndDay));
            if (d.ArrearsSeasons > 0) terms += "  " + L.F(d.InDebt ? "ld_in_debt" : "ld_arrears", d.ArrearsSeasons, M(d.OwedMites));
            if (d.NoticeDay >= 0) terms += "  " + L.F("ld_notice", Day(d.NoticeDay));
            if (d.AskMites > 0) terms += "  " + L.F("ld_offered_at", M(d.AskMites));
            var id = d.Id;
            if (d.Kind is HoldingKind.Parcel or HoldingKind.Building) _body.AddChild(Line(terms, L.T("ld_btn_give_up"), () => _act.Renew?.Invoke(id, false)));
            else if (d.Kind == HoldingKind.Lot && d.ValueMites > 0) { long ask = d.ValueMites * 9 / 10; _body.AddChild(Line(terms, d.AskMites > 0 ? L.T("ld_btn_withdraw") : L.F("ld_btn_offer", M(ask)), () => _act.Offer?.Invoke(id, d.AskMites > 0 ? 0 : ask), true, 150)); }
            else _body.AddChild(Line(terms, null, null));
        }
        _body.AddChild(UiKit.Hint(L.T("ld_head_claims")));
        if (v.Claims.Count == 0) _body.AddChild(Row(L.T("ld_no_claims"), true));
        foreach (var c in v.Claims)
        {
            string land = c.Fief.Length > 0 ? L.F(c.Squatting ? "ld_claim_squat" : "ld_claim_lic", c.Fief) : L.T("ld_claim_wild");
            string state = c.State == ClaimState.Deeded ? L.T("ld_claim_deeded") : L.F("ld_claim_by", Day(c.DeadlineDay), c.Improvements);
            int serial = c.Serial;
            _body.AddChild(Line(L.F("ld_claim_row", c.Tiles, land, state), c.State == ClaimState.Open ? L.T("ld_btn_pull") : null, () => _act.Pull?.Invoke(serial)));
        }
        if (v.Charters.Count > 0)
        {
            _body.AddChild(UiKit.Hint(L.T("ld_head_charters")));
            foreach (var c in v.Charters)
                _body.AddChild(Row(L.F("ld_charter_row", c.Grantor, c.Settled, c.Needed, Day(c.SettleByDay), L.T("ld_cstate_" + c.State.ToString().ToLowerInvariant()), M(c.DuesMitesPerYear), c.ServiceMen, c.ServiceDays)));
        }
        _body.AddChild(UiKit.Hint(L.T("ld_deeds_note"), true));
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (!Visible || _router.HasModal) return;
        if (Actions.Pressed(e, "world.context_menu")) { _router.Pop(); GetViewport().SetInputAsHandled(); }
    }
}
