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
    protected static string Money(long m) => Quotes.Money(m);
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
        Heading.Text = L.T("MC_DEFEAT_TITLE_" + _r.Kind.ToString().ToUpperInvariant());
        Line(L.F("MC_DEFEAT_BODY_" + _r.Kind.ToString().ToUpperInvariant(), who, Money(_r.CoinTaken)));
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
            if (_heirs.Count == 0) Line("  " + L.T("MC_SUCCESSION_NONE"));
            else
            {
                Line("  " + L.F("MC_SUCCESSION_HEIR", _heirs[0].Name, L.T("MC_RULE_" + _heirs[0].Rule.ToString().ToUpperInvariant())));
                if (!_ironman) foreach (var h in _heirs.Skip(1).Take(6)) Line("    " + L.F("MC_SUCCESSION_OTHER", h.Name, L.T("MC_RULE_" + h.Rule.ToString().ToUpperInvariant())));
            }
            Line("  " + L.T("MC_WORLD_PERSISTS"));
        }
        else
        {
            Section(L.T("MC_FIRST"));
            if (_r.Bleeding || _r.NeedsDoctor) Line("  " + L.T("MC_FIRST_TREAT"));
            if (_r.Kind == DefeatKind.Robbed && _r.Winner == WinnerKind.Bandits) Line("  " + L.T("MC_FIRST_TRACK"));
            if (_r.Kind == DefeatKind.Captured) Line("  " + L.T("MC_FIRST_CAPTIVE"));
            Line("  " + L.T("MC_FIRST_ROAD"));
        }
        var b = Btn(L.T(_r.Kind == DefeatKind.Captured ? "MC_BTN_CAPTIVITY" : _r.Kind == DefeatKind.Killed ? "MC_BTN_MENU" : "MC_BTN_GET_UP"), _continue, w: 220);
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

    public CaptivityView(Func<CaptiveView?> view, Action<CaptiveVerb> act, Func<bool> waiting) { _view = view; _act = act; _waiting = waiting; }

    protected override void Build() { _shown = _view(); Rebuild(); }

    public override void _Process(double delta)
    {
        _poll += delta;
        if (_poll < 0.25) return;
        _poll = 0;
        var v = _view();
        if (!ReferenceEquals(v, _shown)) { _shown = v; Rebuild(); }
    }

    void Rebuild()
    {
        Clear(Body); Clear(Buttons);
        var v = _shown;
        if (v == null) { Heading.Text = L.T("MC_CAPTIVE_TITLE_BANDITS"); Line(L.T("MC_CAPTIVE_WAIT")); return; }
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
    }
}
