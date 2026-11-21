using System;
using System.Linq;
using Godot;
using Ravensguard.Game.UI.NewGame;
using Ravensguard.Sim.Faith;
using Ravensguard.Sim.Society;

namespace Ravensguard.Game.UI;

/// <summary>What the Faith panel can do (P6-B4): each is a recorded hero command through the desk, issued by the street view.</summary>
public sealed class FaithLink
{
    public required Func<FaithView?> View { get; init; }
    public required Action Refresh { get; init; }
    public required Action Attend { get; init; }
    public required Action<long> Alms { get; init; }
    public required Action<int> Vow { get; init; }
    public required Action Abandon { get; init; }
    // ---- P6-E2: the faith's politics (each a recorded hero command)
    public Action Penance { get; init; } = () => { };
    public Action<ulong> Venerate { get; init; } = _ => { };
    public Action<ulong> SellRelic { get; init; } = _ => { };
    public Action<ulong> BuyRelic { get; init; } = _ => { };
    public Action ShowHead { get; init; } = () => { };
    public Action Charm { get; init; } = () => { };
}

/// <summary>
/// The Faith panel of the character screen (P6-B4, faith-and-church 6): the hero's religion and its god, his piety and its band, his standing with the church, the priest of his parish and the bishop of his see,
/// the holy days ahead, the doctrines, and the pilgrimage (the holy site and the shrines, a vow taken or given up). A view over the barrier's <see cref="FaithView"/>, refreshed while it is open; Back closes it.
/// Readable at 960 x 540: one scrolling column of short rows and a row of buttons that never leaves the screen.
/// </summary>
public partial class FaithScreen : Control
{
    readonly ScreenRouter _router;
    readonly FaithLink _link;
    VBoxContainer _body = null!;
    FaithView? _shown;
    double _acc;
    int _shotWait;
    /// <summary>Dev: when set, the screen saves its own picture to this path and quits.</summary>
    public string? ShotPath { get; init; }

    public FaithScreen(ScreenRouter router, FaithLink link) { _router = router; _link = link; }

    public override void _Ready()
    {
        var margin = UiKit.Margin(this, 14);
        var root = new VBoxContainer(); root.AddThemeConstantOverride("separation", 6); margin.AddChild(root);
        root.AddChild(UiKit.Title(L.T("fa_title")));
        _body = new VBoxContainer(); _body.AddThemeConstantOverride("separation", 4);
        var panel = UiKit.Panel(UiKit.Scroll(_body), 0, 0); panel.SizeFlagsVertical = SizeFlags.ExpandFill; root.AddChild(panel);      // the body scrolls; the buttons stay on screen
        var bottom = new HBoxContainer(); bottom.AddThemeConstantOverride("separation", 8); bottom.Name = "FaithButtons";
        var back = UiKit.Key(L.T("UI_BACK"), "menu.back", 200); back.Pressed += _router.Pop; bottom.AddChild(back);
        bottom.AddChild(Act(L.T("fa_btn_attend"), _link.Attend, "attend"));
        bottom.AddChild(Act(L.T("fa_btn_alms"), () => _link.Alms(100), "alms"));
        root.AddChild(bottom);
        _link.Refresh();
        Build(_link.View());
    }

    Button Act(string text, Action act, string name)
    {
        var b = new Button { Text = text, Name = name, CustomMinimumSize = new Vector2(150, 36) };
        b.Pressed += () => { act(); _link.Refresh(); };
        return b;
    }

    public override void _Process(double delta)
    {
        _acc += delta;
        if (_acc >= 1.0) { _acc = 0; _link.Refresh(); var v = _link.View(); if (!ReferenceEquals(v, _shown)) Build(v); }
        if (ShotPath == null || !Visible || ++_shotWait != 8) return;
        var img = GetViewport().GetTexture().GetImage();
        System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(ShotPath) ?? ".");
        GD.Print($"[faith] screenshot {ShotPath}: {img.SavePng(ShotPath)}");
        GetTree().Quit();
    }

    static Label Dim(string text) { var l = UiKit.Hint(text, true); return l; }

    HBoxContainer Row(string label, string value, Color? colour = null)
    {
        var row = new HBoxContainer(); row.AddThemeConstantOverride("separation", 8);
        row.AddChild(new Label { Text = label, CustomMinimumSize = new Vector2(190, 0) });
        var v = new Label { Text = value, SizeFlagsHorizontal = SizeFlags.ExpandFill, ClipText = true };
        if (colour is { } c) v.AddThemeColorOverride("font_color", c);
        row.AddChild(v);
        return row;
    }

    void Build(FaithView? v)
    {
        _shown = v;
        foreach (var c in _body.GetChildren()) c.QueueFree();
        if (v == null) { _body.AddChild(UiKit.Body(L.T("fa_loading"))); return; }
        if (v.ReligionId.Length == 0) { _body.AddChild(UiKit.Body(L.T("fa_no_faith"))); return; }
        _body.AddChild(UiKit.Body(L.F("fa_religion_line", v.ReligionName, v.Deity), true));
        // piety, and standing with the church
        var pr = new HBoxContainer(); pr.AddThemeConstantOverride("separation", 8);
        pr.AddChild(new Label { Text = L.T("fa_piety"), CustomMinimumSize = new Vector2(190, 0) });
        pr.AddChild(new Label { Text = $"{v.PietyNow}  {L.T(v.PietyBandKey)}", CustomMinimumSize = new Vector2(150, 0) });
        pr.AddChild(new ProgressBar { MinValue = 0, MaxValue = 100, Value = v.PietyNow, ShowPercentage = false, CustomMinimumSize = new Vector2(160, 10), SizeFlagsVertical = SizeFlags.ShrinkCenter });
        _body.AddChild(pr);
        Color? sc = v.StandingPm >= 150 ? PixelTheme.Gold : v.StandingPm <= -150 ? new Color(0.9f, 0.45f, 0.4f) : null;
        _body.AddChild(Row(L.T("fa_standing"), $"{L.T(Standing.BandKey(v.Band))}  ({(v.StandingPm > 0 ? "+" : "")}{v.StandingPm})", sc));
        if (v.Excommunicated) _body.AddChild(Dim(L.T("fa_excommunicated")));
        else if (Standing.ChurchRefuses(v.StandingPm)) _body.AddChild(Dim(L.T("fa_refuses")));
        // two columns: the church and the holy days; what the faith teaches and the pilgrimage
        var cols = new HBoxContainer(); cols.AddThemeConstantOverride("separation", 16); _body.AddChild(cols);
        var left = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill }; left.AddThemeConstantOverride("separation", 3); cols.AddChild(left);
        var right = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill }; right.AddThemeConstantOverride("separation", 3); cols.AddChild(right);
        left.AddChild(UiKit.Hint(L.T("fa_head_church")));
        left.AddChild(Pair(L.F("fa_priest_of", v.ParishName), v.ParishVacant || v.PriestName.Length == 0 ? L.T("fa_vacant") : v.PriestName));
        left.AddChild(Pair(L.F("fa_bishop_of", v.SeeName), v.SeeVacant || v.BishopName.Length == 0 ? L.T("fa_vacant_see") : v.BishopName));
        if (v.LastServiceDay >= 0 && v.ServiceAdults > 0) left.AddChild(Dim(L.F("fa_last_service", v.ServiceAttended, v.ServiceAdults)));
        left.AddChild(UiKit.Hint(L.T("fa_head_days")));
        if (v.ServiceToday) left.AddChild(Pair(L.F("fa_today", v.FeastToday), v.AttendedToday ? L.T("fa_attended_yes") : L.T("fa_attended_no"), PixelTheme.Gold));
        foreach (var (name, inDays, today, saint) in v.HolyDays.Take(5))
            if (!today) left.AddChild(Pair(saint ? L.F("fa_saint_day", name) : name, L.F("fa_in_days", inDays)));
        right.AddChild(UiKit.Hint(L.T("fa_head_doctrine")));
        foreach (var d in v.Doctrines) right.AddChild(Dim(L.T("fa_doc_" + d)));
        right.AddChild(UiKit.Hint(L.T("fa_head_pilgrim")));
        right.AddChild(Pair(L.T("fa_holy_site"), v.HolySiteName.Length > 0 ? v.HolySiteName : L.T("fa_none")));
        if (v.PilgrimagesDone > 0) right.AddChild(Pair(L.T("fa_done"), v.PilgrimagesDone.ToString()));
        if (v.OnPilgrimage)
        {
            right.AddChild(Pair(L.T("fa_on_pilgrimage"), L.F("fa_walk_to", v.PilgrimTarget), PixelTheme.Gold));
            var giveUp = new Button { Text = L.T("fa_btn_giveup"), Name = "giveup", CustomMinimumSize = new Vector2(190, 30), SizeFlagsHorizontal = SizeFlags.ShrinkBegin };
            giveUp.Pressed += () => { _link.Abandon(); _link.Refresh(); };
            right.AddChild(giveUp);
        }
        else
        {
            foreach (var (name, settlement, site) in v.Shrines.Take(3))
            {
                var b = new Button { Text = L.F(site ? "fa_btn_vow_site" : "fa_btn_vow", name), Name = "vow" + settlement, CustomMinimumSize = new Vector2(0, 30), SizeFlagsHorizontal = SizeFlags.ExpandFill, ClipText = true };
                int s2 = settlement; b.Pressed += () => { _link.Vow(s2); _link.Refresh(); };
                right.AddChild(b);
            }
            right.AddChild(Dim(L.T("fa_pilgrim_note")));
        }
        if (v.Politics is { } pv) Politics(v, pv);
    }

    Button Small(string text, string name, Action act)
    {
        var b = new Button { Text = text, Name = name, CustomMinimumSize = new Vector2(0, 28), SizeFlagsHorizontal = SizeFlags.ShrinkBegin };
        b.Pressed += () => { act(); _link.Refresh(); };
        return b;
    }

    /// <summary>The faith's politics (P6-E2): superstition, a heresy and the bishop's answer, a ban and its penance, relics, a pardoner's offer, a monster's head, believed miracles with the natural explanation beside each.</summary>
    void Politics(FaithView v, FaithPoliticsView pv)
    {
        _body.AddChild(UiKit.Hint(L.T("fa_head_politics")));
        _body.AddChild(Row(L.T("fa_fear"), pv.AvoidsNight ? L.F("fa_fear_night", pv.Fear) : pv.Fear.ToString()));
        if (pv.AvoidsNight) _body.AddChild(Small(L.F("fa_btn_charm", pv.CharmMites), "charm", _link.Charm));
        if (pv.CanShowHead) _body.AddChild(Small(L.T("fa_btn_head"), "head", _link.ShowHead));
        if (pv.HeresyChange.Length > 0)
        {
            _body.AddChild(Row(L.T("fa_heresy_here"), L.F("fa_heresy_line", L.T("fa_heresy_" + pv.HeresyChange), pv.HeresyAdherents, pv.HeresyAdults), new Color(0.9f, 0.55f, 0.4f)));
            _body.AddChild(Dim(L.T("fa_heresy_resp_" + pv.HeresyResponse)));
            if (pv.Interdict) _body.AddChild(Dim(L.T("fa_interdict_line")));
        }
        if (pv.Banned)
        {
            _body.AddChild(Row(L.T("fa_ban_head"), L.F("fa_ban_line", UiKit.Money(pv.PenanceMites)), new Color(0.9f, 0.45f, 0.4f)));
            if (pv.PilgrimageSince) _body.AddChild(Small(L.T("fa_btn_penance"), "penance", _link.Penance)); else _body.AddChild(Dim(L.T("fa_penance_needs")));
        }
        foreach (var (key, name, carried, fake, venerable) in pv.Held)
        {
            var row = new HBoxContainer(); row.AddThemeConstantOverride("separation", 8);
            row.AddChild(new Label { Text = (carried ? L.T("fa_relic_carried") : L.T("fa_relic_altar")) + name + (fake ? L.T("fa_relic_fake") : ""), SizeFlagsHorizontal = SizeFlags.ExpandFill, ClipText = true });
            ulong k = key;
            if (carried) row.AddChild(Small(L.T("fa_btn_sell"), "sell" + key, () => _link.SellRelic(k)));
            else if (venerable) row.AddChild(Small(L.T("fa_btn_venerate"), "venerate" + key, () => _link.Venerate(k)));
            _body.AddChild(row);
        }
        foreach (var (key, name) in pv.ForSale)
        {
            var row = new HBoxContainer(); row.AddThemeConstantOverride("separation", 8);
            row.AddChild(new Label { Text = L.F("fa_for_sale", name), SizeFlagsHorizontal = SizeFlags.ExpandFill, ClipText = true });
            ulong k = key; row.AddChild(Small(L.T("fa_btn_buy"), "buy" + key, () => _link.BuyRelic(k)));
            _body.AddChild(row);
        }
        foreach (var (kind, note, disputed, day) in pv.Miracles)
        {
            _body.AddChild(Row(L.T("fa_head_miracle"), L.T(kind) + (disputed ? L.T("fa_miracle_disputed") : ""), PixelTheme.Gold));
            _body.AddChild(Dim(L.F("fa_miracle_natural", L.T(note))));
        }
    }

    /// <summary>A label and its value on one row inside a column (the label takes the left half).</summary>
    static HBoxContainer Pair(string label, string value, Color? colour = null)
    {
        var row = new HBoxContainer(); row.AddThemeConstantOverride("separation", 6);
        row.AddChild(new Label { Text = label, SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsStretchRatio = 1f, ClipText = true });
        var v = new Label { Text = value, SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsStretchRatio = 1f, ClipText = true };
        if (colour is { } c) v.AddThemeColorOverride("font_color", c);
        row.AddChild(v);
        return row;
    }
}
