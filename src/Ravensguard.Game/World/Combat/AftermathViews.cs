using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Ravensguard.Game.Inputs;
using Ravensguard.Game.UI.NewGame;
using Ravensguard.Sim.Combat.Aftermath;
using Ravensguard.Sim.Succession;
using Ravensguard.Sim.Trade;

namespace Ravensguard.Game.World.Combat;

/// <summary>
/// The shared frame of the aftermath screens (morale-capture-succession 7): a dimmed full-screen guard that swallows the mouse and every key but Esc (the pause menu) and F8 (the bug
/// report), and a centred panel that fits 960 x 540 with its body in a scroll box and the buttons pinned under it. A thin view: it shows what it is given and calls back.
/// </summary>
public abstract partial class AftermathPanel : Control
{
    protected VBoxContainer Body = null!;
    protected HFlowContainer Buttons = null!;
    protected Label Heading = null!;

    public override void _Ready()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Stop;
        var dim = new ColorRect { Color = new Color(0, 0, 0, 0.72f), MouseFilter = MouseFilterEnum.Stop };
        dim.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(dim);
        var margin = UiKit.Margin(this, 20);
        var center = new CenterContainer();
        margin.AddChild(center);
        var panel = new PanelContainer { CustomMinimumSize = new Vector2(720, 0) };
        center.AddChild(panel);
        var v = new VBoxContainer(); v.AddThemeConstantOverride("separation", 8);
        panel.AddChild(v);
        Heading = UiKit.Title("");
        v.AddChild(Heading);
        Body = new VBoxContainer(); Body.AddThemeConstantOverride("separation", 4);
        var scroll = UiKit.Scroll(Body);
        scroll.CustomMinimumSize = new Vector2(720, 318);
        v.AddChild(scroll);
        Buttons = new HFlowContainer { Alignment = FlowContainer.AlignmentMode.End, CustomMinimumSize = new Vector2(720, 0) };
        Buttons.AddThemeConstantOverride("h_separation", 6); Buttons.AddThemeConstantOverride("v_separation", 6);
        v.AddChild(Buttons);
        Build();
    }

    protected abstract void Build();

    /// <summary>The modal input guard: keys stop here (Esc and F8 go on to the street view's pause menu and the bug report).</summary>
    public override void _Input(InputEvent e)
    {
        if (!Visible || e is not InputEventKey) return;
        if (Actions.Pressed(e, "world.back") || Actions.Pressed(e, "menu.back") || Actions.PressedAlways(e, "world.report_bug")) return;
        if (e is InputEventKey { Pressed: true } && GetViewport().GuiGetFocusOwner() is Button b && IsAncestorOf(b) && (e.IsActionPressed("ui_accept") || e.IsActionPressed("ui_left") || e.IsActionPressed("ui_right") || e.IsActionPressed("ui_up") || e.IsActionPressed("ui_down")))
            return;                                                                         // the focused button takes Enter and the arrows
        GetViewport().SetInputAsHandled();
    }

    protected static void Clear(Control c) { foreach (var ch in c.GetChildren()) { c.RemoveChild(ch); ch.QueueFree(); } }
    protected void Section(string title) => Body.AddChild(UiKit.Hint(title));
    protected void Line(string text) => Body.AddChild(UiKit.Body(text));
    protected Button Btn(string text, Action act, bool enabled = true, int w = 150)
    {
        var b = new Button { Text = text, CustomMinimumSize = new Vector2(Math.Min(w, 90), 32), Disabled = !enabled };
        b.Pressed += act;
        Buttons.AddChild(b);
        return b;
    }
    protected static string Money(long m) => UiKit.Money(m);
    protected static string Item(string shape) => shape.Replace('_', ' ');
}

/// <summary>
/// The defeat screen (7): "You wake in the mud. The bandits took 340 mites, your sword and your mare. You still have your shirt." — what happened, what was taken and what is left, the
/// wounds, and what to do first. For a death it shows the succession: who would continue, and that the world goes on.
/// </summary>
public partial class DefeatView : AftermathPanel
{
    readonly DefeatReport _r;
    readonly Action _continue;
    readonly IReadOnlyList<SuccessionCandidate> _heirs;
    readonly bool _ironman;

    public DefeatView(DefeatReport report, Action onContinue, IReadOnlyList<SuccessionCandidate>? heirs = null, bool ironman = false)
    {
        _r = report; _continue = onContinue; _heirs = heirs ?? []; _ironman = ironman;
    }

    protected override void Build()
    {
        string who = L.T("MC_WINNER_" + _r.Winner.ToString().ToUpperInvariant());
        bool arrest = _r.Kind == DefeatKind.Captured && _r.Winner == WinnerKind.Guards;                    // P6-C1: the guards take him to the lord's gaol
        Heading.Text = L.T(arrest ? "MC_DEFEAT_TITLE_ARRESTED" : "MC_DEFEAT_TITLE_" + _r.Kind.ToString().ToUpperInvariant());
        Line(L.F(arrest ? "MC_DEFEAT_BODY_ARRESTED" : "MC_DEFEAT_BODY_" + _r.Kind.ToString().ToUpperInvariant(), who, Money(_r.CoinTaken)));
        if (_r.Kind is DefeatKind.Robbed or DefeatKind.Captured)
        {
            Section(L.T("MC_TAKEN"));
            if (_r.CoinTaken > 0) Line("  " + L.F("MC_TAKEN_COIN", Money(_r.CoinTaken)));
            foreach (var g in _r.ItemsTaken.GroupBy(s => s).OrderBy(g => g.Key, StringComparer.Ordinal)) Line("  " + (g.Count() > 1 ? $"{Item(g.Key)} x{g.Count()}" : Item(g.Key)));
            if (_r.CoinTaken == 0 && _r.ItemsTaken.Count == 0) Line("  " + L.T("MC_TAKEN_NOTHING"));
            Section(L.T("MC_LEFT"));
            if (_r.CoinLeft > 0) Line("  " + L.F("MC_LEFT_COIN", Money(_r.CoinLeft)));
            foreach (var g in _r.ItemsLeft.GroupBy(s => s).OrderBy(g => g.Key, StringComparer.Ordinal)) Line("  " + Item(g.Key));
        }
        if (_r.Fine > 0) Line(L.F("MC_FINED", Money(_r.Fine)));
        Section(L.T("MC_WOUNDS"));
        Line("  " + (_r.Wounds == 0 ? L.T("MC_WOUNDS_NONE") : L.F(_r.Bleeding ? "MC_WOUNDS_BLEEDING" : "MC_WOUNDS_SOME", _r.Wounds)));
        if (_r.Mercy) Line("  " + L.T("MC_MERCY"));
        if (_r.Kind == DefeatKind.Killed)
        {
            Section(L.T("MC_SUCCESSION"));
            Line("  " + L.T("su_next_screen"));                                           // the succession screen follows (who continues, the estate)
            Line("  " + L.T("MC_WORLD_PERSISTS"));
        }
        else
        {
            Section(L.T("MC_FIRST"));
            if (_r.Bleeding || _r.NeedsDoctor) Line("  " + L.T("MC_FIRST_TREAT"));
            if (_r.Kind == DefeatKind.Robbed && _r.Winner == WinnerKind.Bandits) Line("  " + L.T("MC_FIRST_TRACK"));
            if (_r.Kind == DefeatKind.Captured) Line("  " + L.T(_r.Winner == WinnerKind.Guards ? "MC_FIRST_ARRESTED" : "MC_FIRST_CAPTIVE"));
            Line("  " + L.T("MC_FIRST_ROAD"));
        }
        var b = Btn(L.T(_r.Kind == DefeatKind.Captured ? "MC_BTN_CAPTIVITY" : _r.Kind == DefeatKind.Killed ? "su_btn_on" : "MC_BTN_GET_UP"), _continue, w: 220);
        b.GrabFocus();
    }
}

/// <summary>
/// The captivity screen (7, the captive HUD): who holds the hero and how (restraint), the guards (awake by shift, day or night), the ransom (how much, who pays, when it falls due, what the
/// payer has), the time left before the captors decide, the odds, and the ways out: pick the lock, work the bonds, search for a pick, offer a ransom, talk to a guard, wait, slip away on a
/// transfer. Every option is a command to the barrier; the view only reads the published <see cref="CaptiveView"/>. Work off the sentence is Phase 6 (shown, disabled).
/// </summary>
public partial class CaptivityView : AftermathPanel
{
    readonly Func<CaptiveView?> _view;
    readonly Action<CaptiveVerb> _act;
    readonly Func<bool> _waiting;
    CaptiveView? _shown;
    double _poll;
    public Func<string>? Pending;
    /// <summary>The free companions who could come for the hero (P4 close-out, 4.7): the first is offered as "Take control of {name}" (the rescue control switch), with <see cref="TakeControl"/>.</summary>
    public Func<IReadOnlyList<(Ravensguard.Sim.Core.EntityId Id, string Name)>>? Rescuers;
    public Action<Ravensguard.Sim.Core.EntityId>? TakeControl;
    /// <summary>P6-C1 (crime and justice 4.5, 6, 10a): the cell as the court sees it (charges, the expected sentence, the proof, the ways out), and the acts of the accused ("guilty", "clergy", "restitute", "bribe").</summary>
    public Func<Ravensguard.Sim.Law.CellView?>? Cell;
    public Action<string>? Plea;
    Ravensguard.Sim.Law.CellView? _cellShown;

    public CaptivityView(Func<CaptiveView?> view, Action<CaptiveVerb> act, Func<bool> waiting) { _view = view; _act = act; _waiting = waiting; }

    protected override void Build() { _shown = _view(); Rebuild(); }

    public override void _Process(double delta)
    {
        _poll += delta;
        if (_poll < 0.25) return;
        _poll = 0;
        var v = _view();
        var cell = Cell?.Invoke();
        if (!ReferenceEquals(v, _shown) || !ReferenceEquals(cell, _cellShown)) { _shown = v; _cellShown = cell; Rebuild(); }
    }

    void Rebuild()
    {
        Clear(Body); Clear(Buttons);
        var v = _shown;
        if (v == null) { Heading.Text = L.T("MC_CAPTIVE_TITLE_BANDITS"); Line(L.T("MC_CAPTIVE_WAIT")); return; }
        if (v.Captor == CaptorKind.Gaol) { GaolBody(v); return; }
        Heading.Text = L.T(v.Captor == CaptorKind.Army ? "MC_CAPTIVE_TITLE_ARMY" : "MC_CAPTIVE_TITLE_BANDITS");
        Section(L.T("MC_STATUS"));
        Line("  " + L.T("MC_RESTRAINT_" + v.Restraint.ToString().ToUpperInvariant()));
        Line("  " + L.F("MC_GUARDS", v.Guards, v.GuardsAwake, L.T(v.Dark ? "MC_NIGHT" : "MC_DAY")));
        Line("  " + L.F("MC_HELD_DAYS", Math.Max(0, v.HoldUntilDay - v.Day)));
        Section(L.T("MC_RANSOM"));
        Line("  " + L.F("MC_RANSOM_ASK", Money(v.RansomMites), L.T("MC_PAYER_" + v.Payer.ToString().ToUpperInvariant())));
        Line("  " + (v.PayableMites >= v.RansomMites && v.Payer != PayerKind.None ? L.F("MC_RANSOM_DUE", Math.Max(0, v.DueDay - v.Day)) : L.F("MC_RANSOM_SHORT", Money(v.PayableMites))));
        Section(L.T("MC_ODDS"));
        if (v.Restraint == Restraint.Bound) Line("  " + L.F("MC_ODDS_BONDS", v.BondsPm / 10));
        else Line("  " + (v.HasPick ? L.F("MC_ODDS_LOCK", v.LockPm / 10, L.T(v.ImprovisedPick ? "MC_PICK_NAIL" : "MC_PICK_TOOL")) : L.T("MC_ODDS_NO_PICK")));
        Line("  " + L.F("MC_ODDS_NOTICE", v.NoticePm / 10));
        if (v.TransferTonight) Line("  " + L.T("MC_TRANSFER_TONIGHT"));
        if (v.RescueMembers > 0) Line("  " + L.F("MC_RESCUE_FRIENDS", v.RescueMembers));
        if (v.Last is { } last)
        {
            Section(L.T("MC_LAST"));
            Line("  " + L.F(last.Text, Money(last.Mites)));
        }
        if (Pending?.Invoke() is { Length: > 0 } p) Line(p);
        bool held = v.State == CaptiveState.Held;
        bool busy = _waiting();
        Btn(L.T("MC_OPT_PICK"), () => _act(CaptiveVerb.PickLock), held && !busy && v.Restraint is Restraint.Chained or Restraint.Caged && v.HasPick, 120);
        Btn(L.T("MC_OPT_BONDS"), () => _act(CaptiveVerb.WorkBonds), held && !busy && v.Restraint == Restraint.Bound, 120);
        Btn(L.T("MC_OPT_SEARCH"), () => _act(CaptiveVerb.Search), held && !busy && !v.HasPick && v.Restraint != Restraint.Bound, 110);
        Btn(L.T("MC_OPT_RANSOM"), () => _act(CaptiveVerb.OfferRansom), held && !busy && v.Payer != PayerKind.None, 120);
        Btn(L.T("MC_OPT_TALK"), () => _act(CaptiveVerb.TalkToGuard), held && !busy, 90);
        Btn(L.T("MC_OPT_ESCAPE"), () => _act(CaptiveVerb.Escape), held && !busy && v.TransferTonight, 110);
        Btn(L.T("MC_OPT_WAIT"), () => _act(CaptiveVerb.Wait), held, 80).GrabFocus();
        Btn(L.T("MC_OPT_WORK"), () => { }, false, 100).TooltipText = L.T("MC_OPT_WORK_LATER");
        if (Rescuers?.Invoke() is { Count: > 0 } rs && TakeControl != null)                 // the rescue control switch (Tab in the spec's table; a button here, the screen is modal)
            Btn(L.F("rs_btn_control", rs[0].Name), () => TakeControl(rs[0].Id), held && !busy, 200);
    }
}

public partial class CaptivityView
{
    /// <summary>The lord's gaol (P6-C1): who holds him and how, what he is charged with and the sentence to expect (with every reason), the proof, what is being served, the ways out, and what he can do.</summary>
    void GaolBody(CaptiveView v)
    {
        Heading.Text = L.T("MC_CAPTIVE_TITLE_GAOL");
        var c = _cellShown ?? Cell?.Invoke();
        Section(L.T("MC_STATUS"));
        Line("  " + L.T("MC_RESTRAINT_" + v.Restraint.ToString().ToUpperInvariant()));
        Line("  " + L.F("MC_GUARDS", v.Guards, v.GuardsAwake, L.T(v.Dark ? "MC_NIGHT" : "MC_DAY")));
        if (c != null) Line("  " + L.F(c.CourtInDays == 0 ? "jl_cell_court_today" : "jl_cell_court_in", c.CourtInDays) + "  " + L.F("jl_cell_fed", c.FedDays));
        if (c != null && c.Charges.Count > 0)
        {
            Section(L.T("jl_cell_charges"));
            foreach (var ch in c.Charges)
            {
                Line("  " + Ravensguard.Game.UI.JusticeText.Line(ch.Kind, ch.Expected));
                Line("    " + Ravensguard.Game.UI.JusticeText.Proof(ch.Witnesses, ch.Attending, ch.InAct, ch.Confessed));
            }
        }
        if (c != null && c.Serving.Count > 0)
        {
            Section(L.T("jl_cell_serving"));
            foreach (var s in c.Serving) Line("  " + L.F(s.EndDay >= 0 ? "jl_cell_serving_until" : "jl_cell_serving_at", L.T("jl_step_" + s.Kind), s.EndDay >= 0 ? s.EndDay : s.DueDay));
        }
        if (c != null)
        {
            Section(L.T("jl_cell_ways"));
            foreach (var w in c.WaysOut) Line("  " + (w.Available ? "+ " : "- ") + L.F(w.Key, Money(w.Mites)));
        }
        Section(L.T("MC_ODDS"));
        if (v.Restraint == Restraint.Bound) Line("  " + L.F("MC_ODDS_BONDS", v.BondsPm / 10));
        else Line("  " + (v.HasPick ? L.F("MC_ODDS_LOCK", v.LockPm / 10, L.T(v.ImprovisedPick ? "MC_PICK_NAIL" : "MC_PICK_TOOL")) : L.T("MC_ODDS_NO_PICK")));
        Line("  " + L.F("MC_ODDS_NOTICE", v.NoticePm / 10));
        if (v.Last is { } last) { Section(L.T("MC_LAST")); Line("  " + L.F(last.Text, Money(last.Mites))); }
        if (Pending?.Invoke() is { Length: > 0 } p) Line(p);
        bool held = v.State == CaptiveState.Held, busy = _waiting();
        var ways = c?.WaysOut.ToDictionary(w => w.Key) ?? [];
        Btn(L.T("jl_btn_guilty"), () => Plea?.Invoke("guilty"), held && !busy && c is { Charges.Count: > 0 }, 110);
        Btn(L.T("jl_btn_clergy"), () => Plea?.Invoke("clergy"), held && !busy && ways.TryGetValue("jl_way_clergy", out var cl) && cl.Available, 110);
        Btn(L.T("jl_btn_restitute"), () => Plea?.Invoke("restitute"), held && !busy && ways.TryGetValue("jl_way_restitution", out var rs) && rs.Available, 120);
        Btn(L.T("jl_btn_bribe"), () => Plea?.Invoke("bribe"), held && !busy && ways.TryGetValue("jl_way_bribe", out var br) && br.Available, 120);
        Btn(L.T("MC_OPT_PICK"), () => _act(CaptiveVerb.PickLock), held && !busy && v.Restraint is Restraint.Chained or Restraint.Caged && v.HasPick, 100);
        Btn(L.T("MC_OPT_BONDS"), () => _act(CaptiveVerb.WorkBonds), held && !busy && v.Restraint == Restraint.Bound, 100);
        Btn(L.T("MC_OPT_SEARCH"), () => _act(CaptiveVerb.Search), held && !busy && !v.HasPick && v.Restraint != Restraint.Bound, 100);
        Btn(L.T("MC_OPT_TALK"), () => _act(CaptiveVerb.TalkToGuard), held && !busy, 90);
        Btn(L.T("MC_OPT_WAIT"), () => _act(CaptiveVerb.Wait), held, 80).GrabFocus();
        if (Rescuers?.Invoke() is { Count: > 0 } rsc && TakeControl != null)
            Btn(L.F("rs_btn_control", rsc[0].Name), () => TakeControl(rsc[0].Id), held && !busy, 200);
    }
}

/// <summary>
/// The succession screen (7; P4 close-out): "Aldric is dead. Isolde, his sister, continues." — the heir and why they inherit, the others in the order (not in Ironman, where the first
/// continues), what happens to the estate, and the buttons: continue as one of them, load the last save (not in Ironman), or, with nobody left, end the line.
/// </summary>
public partial class SuccessionView : AftermathPanel
{
    readonly string _dead, _cause;
    readonly IReadOnlyList<SuccessionCandidate> _heirs;
    readonly int _all;
    readonly bool _ironman;
    readonly Action<SuccessionCandidate> _pick;
    readonly Action? _load;
    readonly Action _end;
    readonly EstateSummary? _estate;

    public SuccessionView(string dead, string cause, IReadOnlyList<SuccessionCandidate> heirs, int all, bool ironman, Action<SuccessionCandidate> pick, Action? load, Action end, EstateSummary? estate = null)
    {
        _dead = dead; _cause = cause; _heirs = heirs; _all = all; _ironman = ironman; _pick = pick; _load = load; _end = end; _estate = estate;
    }

    static string M(long mites) => UiKit.Money(mites);

    static string Rule(SuccessionCandidate c) => L.T("MC_RULE_" + c.Rule.ToString().ToUpperInvariant());

    protected override void Build()
    {
        Heading.Text = L.F("su_title", _dead);
        Line(_cause);
        Section(L.T("MC_SUCCESSION"));
        if (_heirs.Count == 0) { Line("  " + L.T("MC_SUCCESSION_NONE")); Line("  " + L.T("MC_WORLD_PERSISTS")); }
        else
        {
            Line("  " + L.F("su_heir_line", _heirs[0].Name, Rule(_heirs[0]), _heirs[0].AgeYears));
            foreach (var h in _heirs.Skip(1).Take(6)) Line("    " + L.F("su_other_line", h.Name, Rule(h), h.AgeYears));
            if (_ironman && _all > 1) Line("  " + L.T("su_ironman"));
        }
        Section(L.T("su_estate"));
        if (_estate is { } e && (e.Lines.Count > 0 || e.CoinMites > 0))
        {
            // P5-H (inheritance 4.3, 6): what the heir receives, what goes to other kin and to the lord, the coin after the funeral and the debts
            Line("  " + L.F("in_su_coin", M(e.CoinMites), M(e.FuneralMites), M(e.DebtsMites)) + (e.Will ? "  " + L.T("in_su_by_will") : ""));
            if (e.DebtsMites > e.DebtsFromCoinMites) Line("  " + L.F("in_su_claims", M(e.DebtsMites - e.DebtsFromCoinMites)));
            if (_heirs.Count > 0)
            {
                Line("  " + L.F("in_su_you_inherit", _heirs[0].Name, M(e.CoinToHeirMites)) + "  " + (e.Law == "partible" ? L.T("in_su_law_partible") : L.T("in_su_law_primo")));
                foreach (var l in e.YouInherit.Take(8)) Line("    " + L.F("in_su_line", l.Name, M(l.ValueMites)));
                if (!e.YouInherit.Any()) Line("    " + L.T("in_su_inherit_nothing"));
            }
            if (e.ToOthers.Any()) { Line("  " + L.T("in_su_to_kin")); foreach (var l in e.ToOthers.Take(6)) Line("    " + L.F("in_su_line_to", l.Name, M(l.ValueMites), l.To)); }
            if (e.ToTheLord.Any()) { Line("  " + L.T("in_su_to_lord")); foreach (var l in e.ToTheLord.Take(6)) Line("    " + L.F("in_su_line_to", l.Name, M(l.ValueMites), l.To)); }
            Line("  " + L.T("in_su_body_line"));
        }
        else Line("  " + L.T("su_estate_line"));
        Button? first = null;
        foreach (var h in _heirs.Take(4)) { var c = h; var b = Btn(L.F("su_btn_continue", c.Name), () => _pick(c), w: 220); first ??= b; }
        if (_load != null) Btn(L.T("su_btn_load"), _load, w: 180);
        if (_heirs.Count == 0) first = Btn(L.T("su_btn_end"), _end, w: 200);
        first?.GrabFocus();
    }
}
