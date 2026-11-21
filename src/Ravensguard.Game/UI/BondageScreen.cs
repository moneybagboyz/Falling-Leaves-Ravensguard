using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Ravensguard.Game.UI.NewGame;
using Ravensguard.Sim.Bondage;
using Ravensguard.Sim.Combat.Aftermath;
using Ravensguard.Sim.Trade;

namespace Ravensguard.Game.UI;

/// <summary>What the bondage panel can do: each is a recorded hero command through the desk (<see cref="BondDesk"/>), issued by the street view.</summary>
public sealed class BondageLink
{
    public required Func<BondView?> View { get; init; }
    public required Action<ulong> Buy { get; init; }
    public required Action<ulong> Sell { get; init; }
    public required Action<ulong> Free { get; init; }
    public required Action<ulong, Treatment> Treat { get; init; }
    public required Action<ulong, bool> Chain { get; init; }
    public required Action<ulong, BondWork> Work { get; init; }
    /// <summary>P6-E3: hand a spare weapon to a slave of the party (true) or take it back (false).</summary>
    public Action<ulong, bool> Arm { get; init; } = (_, _) => { };
    public required Action Escape { get; init; }
    public required Action Run { get; init; }
    public required Action SelfBuy { get; init; }
}

/// <summary>
/// The bondage panel (slavery 6): the pen's list (person, age, skill, how he came, the asking price) with Buy; the hero's own slaves (the Ledger's People tab: work, treatment, chains, morale, peculium, term) with Free, Sell,
/// the treatment, the chains and the work; and, when the hero is himself enslaved, his bond: whose he is, what he works at, the chains, the peculium and the ways out the world knows (pick the chains at night, run when
/// unchained, buy himself free). A view over the barrier's <see cref="BondView"/>; every act is a hero command. Back closes it.
/// </summary>
public partial class BondageScreen : Control
{
    readonly ScreenRouter _router;
    readonly BondageLink _link;
    VBoxContainer _list = null!, _detail = null!;
    Label _head = null!;
    HBoxContainer _tabs = null!;
    BondView? _shown;
    int _tab;
    ulong _sel;
    double _refresh;
    int _shotWait;
    /// <summary>Dev: when set, the panel saves its own picture to this path and quits.</summary>
    public string? ShotPath { get; init; }
    /// <summary>Dev: the tab to open on (0 pen, 1 slaves, 2 bond).</summary>
    public int StartTab { get; init; }

    public BondageScreen(ScreenRouter router, BondageLink link) { _router = router; _link = link; }

    public override void _Ready()
    {
        _tab = StartTab;
        var margin = UiKit.Margin(this, 12);
        var root = new VBoxContainer(); root.AddThemeConstantOverride("separation", 4); margin.AddChild(root);
        root.AddChild(UiKit.Title(L.T("sl_title")));
        _tabs = new HBoxContainer(); _tabs.AddThemeConstantOverride("separation", 6); root.AddChild(_tabs);
        _head = UiKit.Hint("", true); root.AddChild(_head);
        var cols = new HBoxContainer { SizeFlagsVertical = SizeFlags.ExpandFill }; cols.AddThemeConstantOverride("separation", 8); root.AddChild(cols);
        _list = new VBoxContainer(); _list.AddThemeConstantOverride("separation", 2);
        var lp = UiKit.Panel(UiKit.Scroll(_list), 0, 0); lp.SizeFlagsHorizontal = SizeFlags.ExpandFill; lp.SizeFlagsVertical = SizeFlags.ExpandFill; cols.AddChild(lp);
        _detail = new VBoxContainer(); _detail.AddThemeConstantOverride("separation", 3);
        var rp = UiKit.Panel(UiKit.Scroll(_detail), 0, 0); rp.SizeFlagsHorizontal = SizeFlags.ExpandFill; rp.SizeFlagsVertical = SizeFlags.ExpandFill; rp.SizeFlagsStretchRatio = 0.9f; cols.AddChild(rp);
        var bottom = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.End }; bottom.AddThemeConstantOverride("separation", 8);
        var back = UiKit.Key(L.T("UI_BACK"), "menu.back", 120, 32); back.Pressed += _router.Pop; bottom.AddChild(back);
        root.AddChild(bottom);
        Build();
    }

    /// <summary>Dev (smoke, shots): a made-up pen of three lots at a Seaboard port, two slaves of the hero's and his own chains.</summary>
    public static BondView Demo(bool enslaved)
    {
        BondRow R(ulong id, string name, int age, int skill, BondKind k, BondOrigin o, Treatment t, Restraint c, BondWork w, int morale, long price, long ask, long pec, int term, int pct, int place, string pn) =>
            new(id, name, age, skill, k, o, t, c, w, morale, price, ask, pec, term, pct, false, place, pn, 0);
        var pen = new List<BondRow>
        {
            R(1, "Ada Marsh", 24, 52, BondKind.Chattel, BondOrigin.Captured, Treatment.Fair, Restraint.Bound, BondWork.Idle, 50, 13_500, 13_500, 0, -1, 0, 3, "Saltmouth"),
            R(2, "Ewan Reed", 13, 5, BondKind.Chattel, BondOrigin.Captured, Treatment.Fair, Restraint.Bound, BondWork.Idle, 50, 6_300, 6_300, 0, -1, 0, 3, "Saltmouth"),
            R(3, "Old Tam", 58, 30, BondKind.Chattel, BondOrigin.Captured, Treatment.Fair, Restraint.Bound, BondWork.Idle, 50, 3_600, 3_600, 0, -1, 0, 3, "Saltmouth"),
        };
        var owned = new List<BondRow>
        {
            R(4, "Wyn Cotter", 31, 44, BondKind.Chattel, BondOrigin.Sold, Treatment.Fair, Restraint.Bound, BondWork.Follow, 52, 13_500, 0, 85, -1, 0, 3, "Saltmouth"),
            R(5, "Bryn Stoke", 19, 12, BondKind.Debt, BondOrigin.Debt, Treatment.Kind, Restraint.None, BondWork.Fields, 66, 4_500, 0, 140, 22, 0, 3, "Saltmouth"),
        };
        HeroBond? hb = enslaved ? new HeroBond(R(9, "Aldric Vane", 30, 20, BondKind.Chattel, BondOrigin.Sold, Treatment.Fair, Restraint.Chained, BondWork.Fields, 44, 9_000, 0, 35, -1, 0, 3, "Saltmouth"), 0, "Hale Brenner", true, 42, 35, 9_000, true) : null;
        return new BondView(enslaved ? [] : owned, [new PenRow(3, "Saltmouth", SlaveLaw.Chattel, pen)], hb, 20, 0);
    }

    public override void _Process(double delta)
    {
        _refresh += delta;
        if (_refresh > 0.4) { _refresh = 0; var v = _link.View(); if (!ReferenceEquals(v, _shown)) Build(); }
        if (ShotPath == null || !Visible || ++_shotWait != 10) return;
        var img = GetViewport().GetTexture().GetImage();
        System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(ShotPath) ?? ".");
        GD.Print($"[bondage] screenshot {ShotPath}: {img.SavePng(ShotPath)}");
        GetTree().Quit();
    }

    static string Money(long mites) => UiKit.Money(mites);

    Button Act(string text, Action act, bool enabled = true, int w = 150)
    {
        var b = new Button { Text = text, Disabled = !enabled, CustomMinimumSize = new Vector2(w, 28), ClipText = true };
        b.Pressed += act;
        return b;
    }

    void Build()
    {
        _shown = _link.View();
        var v = _shown;
        foreach (var c in _tabs.GetChildren()) { _tabs.RemoveChild(c); c.QueueFree(); }
        string[] names = ["sl_tab_pen", "sl_tab_slaves", "sl_tab_bond"];
        for (int i = 0; i < 3; i++)
        {
            int ti = i;
            var b = new Button { Text = L.T(names[i]), ToggleMode = true, ButtonPressed = _tab == i, CustomMinimumSize = new Vector2(140, 28) };
            b.Pressed += () => { _tab = ti; _sel = 0; Build(); };
            _tabs.AddChild(b);
        }
        foreach (var c in _list.GetChildren()) { _list.RemoveChild(c); c.QueueFree(); }
        foreach (var c in _detail.GetChildren()) { _detail.RemoveChild(c); c.QueueFree(); }
        if (v == null) { _head.Text = L.T("sl_none"); return; }
        switch (_tab)
        {
            case 0: BuildPen(v); break;
            case 1: BuildSlaves(v); break;
            default: BuildBond(v); break;
        }
    }

    void Row(BondRow r, string line)
    {
        var b = new Button { Text = line, Alignment = HorizontalAlignment.Left, ClipText = true, ToggleMode = true, ButtonPressed = _sel == r.Person, CustomMinimumSize = new Vector2(0, 26), SizeFlagsHorizontal = SizeFlags.ExpandFill };
        ulong id = r.Person; b.Pressed += () => { _sel = id; Build(); };
        _list.AddChild(b);
    }

    void Detail(BondRow r)
    {
        _detail.AddChild(UiKit.Title(r.Name));
        _detail.AddChild(UiKit.Body(L.F("sl_d_who", r.Age, r.Skill, L.T("sl_kind_" + r.Kind.ToString().ToLowerInvariant()), L.T("sl_origin_" + r.Origin.ToString().ToLowerInvariant()))));
        if (r.AskMites > 0) _detail.AddChild(UiKit.Body(L.F("sl_d_ask", Money(r.AskMites), r.PlaceName)));
        else
        {
            _detail.AddChild(UiKit.Body(L.F("sl_d_kept", L.T("sl_work_" + r.Work.ToString().ToLowerInvariant()), L.T("sl_treat_" + r.Treatment.ToString().ToLowerInvariant()), L.T("sl_chain_" + r.Chain.ToString().ToLowerInvariant()))));
            _detail.AddChild(UiKit.Body(L.F("sl_d_morale", r.Morale, Money(r.PeculiumMites), Money(r.PriceMites))));
            if (r.TermDaysLeft >= 0) _detail.AddChild(UiKit.Hint(L.F("sl_d_term", r.TermDaysLeft)));
            if (r.GrievancePct > 0) _detail.AddChild(UiKit.Hint(L.T("sl_d_grievance")));
            if (r.OnRun) _detail.AddChild(UiKit.Hint(L.T("sl_d_onrun")));
        }
    }

    void BuildPen(BondView v)
    {
        _head.Text = v.Pens.Count == 0 ? L.T("sl_no_pen") : L.T("sl_head_pen");
        var all = new List<BondRow>();
        foreach (var pen in v.Pens)
        {
            _list.AddChild(UiKit.Hint(L.F("sl_pen_of", pen.Name, L.T("sl_law_" + pen.Law.ToString().ToLowerInvariant()))));
            foreach (var r in pen.Lots) { all.Add(r); Row(r, L.F("sl_row_pen", r.Name, r.Age, r.Skill, L.T("sl_origin_" + r.Origin.ToString().ToLowerInvariant()), Money(r.AskMites))); }
        }
        var sel = all.FirstOrDefault(r => r.Person == _sel) ?? all.FirstOrDefault();
        if (sel == null) return;
        _sel = sel.Person; Detail(sel);
        ulong id = sel.Person;
        _detail.AddChild(Act(L.F("sl_btn_buy", Money(sel.AskMites)), () => _link.Buy(id), true, 220));
    }

    void BuildSlaves(BondView v)
    {
        _head.Text = v.Owned.Count == 0 ? L.T("sl_no_slaves") : L.T("sl_head_slaves");
        foreach (var r in v.Owned) Row(r, L.F("sl_row_slave", r.Name, L.T("sl_work_" + r.Work.ToString().ToLowerInvariant()), L.T("sl_treat_" + r.Treatment.ToString().ToLowerInvariant()), r.Morale, Money(r.PeculiumMites)));
        var sel = v.Owned.FirstOrDefault(r => r.Person == _sel) ?? v.Owned.FirstOrDefault();
        if (sel == null) return;
        _sel = sel.Person; Detail(sel);
        ulong id = sel.Person;
        var row = new HFlowContainer(); row.AddThemeConstantOverride("h_separation", 6); row.AddThemeConstantOverride("v_separation", 4); _detail.AddChild(row);
        row.AddChild(Act(L.T("sl_btn_free"), () => _link.Free(id)));
        row.AddChild(Act(L.T("sl_btn_sell"), () => _link.Sell(id)));
        row.AddChild(Act(L.F("sl_btn_treat", L.T("sl_treat_" + ((Treatment)(((int)sel.Treatment + 1) % 3)).ToString().ToLowerInvariant())), () => _link.Treat(id, (Treatment)(((int)sel.Treatment + 1) % 3)), true, 190));
        bool chained = sel.Chain is Restraint.Chained or Restraint.Caged;
        row.AddChild(Act(L.T(chained ? "sl_btn_unchain" : "sl_btn_chain"), () => _link.Chain(id, !chained)));
        var nextWork = sel.Work switch { BondWork.Idle => BondWork.Fields, BondWork.Fields => BondWork.Mine, BondWork.Mine => BondWork.House, BondWork.House => BondWork.Follow, _ => BondWork.Idle };
        row.AddChild(Act(L.F("sl_btn_work", L.T("sl_work_" + nextWork.ToString().ToLowerInvariant())), () => _link.Work(id, nextWork), true, 190));
        if (sel.Work == BondWork.Follow) row.AddChild(Act(L.T(sel.Armed ? "sl_btn_disarm" : "sl_btn_arm"), () => _link.Arm(id, !sel.Armed)));
    }

    void BuildBond(BondView v)
    {
        if (v.Hero is not { } h) { _head.Text = L.T("sl_not_enslaved"); return; }
        _head.Text = L.F("sl_bond_head", h.OwnerName);
        var r = h.Row;
        _list.AddChild(UiKit.Body(L.F("sl_bond_owner", h.OwnerName, r.PlaceName)));
        _list.AddChild(UiKit.Body(L.F("sl_d_kept", L.T("sl_work_" + r.Work.ToString().ToLowerInvariant()), L.T("sl_treat_" + r.Treatment.ToString().ToLowerInvariant()), L.T("sl_chain_" + r.Chain.ToString().ToLowerInvariant()))));
        _list.AddChild(UiKit.Body(L.F("sl_bond_peculium", Money(h.PeculiumMites), Money(h.PriceMites))));
        _list.AddChild(UiKit.Hint(L.F("sl_b_morale", r.Morale)));
        _detail.AddChild(UiKit.Title(L.T("sl_ways")));
        _detail.AddChild(UiKit.Body(L.F("sl_way_escape", h.LockPm, L.T(h.Night ? "sl_night" : "sl_day")), true));
        _detail.AddChild(UiKit.Body(L.T("sl_way_run"), true));
        _detail.AddChild(UiKit.Body(L.F("sl_way_selfbuy", Money(h.PriceMites)), true));
        _detail.AddChild(UiKit.Body(L.T("sl_way_kin"), true));
        _detail.AddChild(UiKit.Body(L.T("sl_way_trust"), true));
        var row = new HFlowContainer(); row.AddThemeConstantOverride("h_separation", 6); row.AddThemeConstantOverride("v_separation", 4); _detail.AddChild(row);
        row.AddChild(Act(L.T("sl_btn_escape"), _link.Escape, r.Chain != Restraint.None));
        row.AddChild(Act(L.T("sl_btn_run"), _link.Run, r.Chain == Restraint.None));
        row.AddChild(Act(L.T("sl_btn_selfbuy"), _link.SelfBuy, h.PeculiumMites >= h.PriceMites && h.PriceMites > 0));
    }
}
