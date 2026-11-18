using System;
using System.Linq;
using Godot;
using Ravensguard.Game.Inputs;
using Ravensguard.Game.UI.NewGame;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Trade;

namespace Ravensguard.Game.UI;

/// <summary>
/// The squad panel (P, trade-inventory-character spec 4.6, 6): the party with loyalty bars, wages and days unpaid, how they joined, and their orders (Follow, Hold, Work through the job board, Rest)
/// and Dismiss; below, the person beside you, with what it takes to hire them (the asking wage of the spec's formula and the offer they would take) and whether they would join you for good, and
/// Talk, Hire and Invite to join. Every button is a hero command applied at the next barrier; the panel reads the barrier's <see cref="HeroView"/>.
/// </summary>
public partial class SquadScreen : Control
{
    readonly ScreenRouter _router;
    readonly HeroWorld _hw;
    readonly Func<long> _now;
    readonly Func<(EntityId Id, string Name, long X, long Y)?> _near;
    VBoxContainer _body = null!;
    Label _status = null!;
    HeroView? _view;
    EntityId _shown;
    double _refresh;
    long _waitSeq;
    int _shotWait;
    public string? ShotPath { get; init; }

    public SquadScreen(ScreenRouter router, HeroWorld hw, Func<long> now, Func<(EntityId Id, string Name, long X, long Y)?> near) { _router = router; _hw = hw; _now = now; _near = near; }

    public override void _Ready()
    {
        var margin = UiKit.Margin(this, 14);
        var root = new VBoxContainer(); root.AddThemeConstantOverride("separation", 6); margin.AddChild(root);
        root.AddChild(UiKit.Title(L.T("SQUAD_TITLE")));
        _body = new VBoxContainer { SizeFlagsVertical = SizeFlags.ExpandFill }; _body.AddThemeConstantOverride("separation", 4);
        var p = UiKit.Panel(_body, 0, 0); p.SizeFlagsVertical = SizeFlags.ExpandFill; root.AddChild(p);
        _status = UiKit.Body("", true); root.AddChild(_status);
        var bottom = new HBoxContainer(); bottom.AddThemeConstantOverride("separation", 8); root.AddChild(bottom);
        var back = UiKit.Key(L.T("UI_BACK"), "menu.back", 200); back.Pressed += _router.Pop; bottom.AddChild(back);
        bottom.AddChild(UiKit.Hint(L.F("SQUAD_CLOSE_HINT", InputBootstrap.Hint("world.panel_squad"))));
        if (_near() is { } n) { _shown = n.Id; _hw.Issue(new HeroCommand { Kind = HeroCommandKind.ViewPerson, Person = n.Id, IssuedSec = _now() }); }
        Build();
    }

    void Build()
    {
        _view = _hw.View;
        foreach (var c in _body.GetChildren()) { _body.RemoveChild(c); c.QueueFree(); }
        var v = _view; if (v == null) { _body.AddChild(UiKit.Body(L.T("TRADE_SETTLING"))); return; }
        var members = v.Squad.Where(m => m.Active).ToList();
        _body.AddChild(UiKit.Hint(L.F("SQUAD_MEMBERS", members.Count, v.SquadCap, Quotes.Money(members.Sum(m => m.WageMitesPerDay)))));
        if (members.Count == 0) _body.AddChild(UiKit.Body(L.T("SQUAD_EMPTY"), true));
        foreach (var m in members)
        {
            var row = new HBoxContainer(); row.AddThemeConstantOverride("separation", 8);
            row.AddChild(new Label { Text = m.Name, CustomMinimumSize = new Vector2(200, 0), ClipText = true, ThemeTypeVariation = "GoldLabel" });
            row.AddChild(new Label { Text = L.T(m.Origin == FollowerOrigin.Recruited ? "SQUAD_RECRUITED" : "SQUAD_HIRED") + (m.Origin == FollowerOrigin.Hired && m.EndDay > 0 ? $" ({L.F("SQUAD_UNTIL", m.EndDay - v.Day)})" : ""), CustomMinimumSize = new Vector2(150, 0) });
            row.AddChild(new Label { Text = L.T("SQUAD_LOYALTY"), ThemeTypeVariation = "HintLabel" });
            row.AddChild(new ProgressBar { MaxValue = 1000, Value = m.Loyalty, ShowPercentage = false, CustomMinimumSize = new Vector2(100, 12), SizeFlagsVertical = SizeFlags.ShrinkCenter });
            row.AddChild(new Label { Text = L.F("SQUAD_WAGE", Quotes.Money(m.WageMitesPerDay)) + (m.UnpaidDays > 0 ? "  " + L.F("SQUAD_UNPAID", m.UnpaidDays) : ""), CustomMinimumSize = new Vector2(150, 0) });
            row.AddChild(new Label { Text = L.T("ORDER_" + m.Order.ToString().ToUpperInvariant()), CustomMinimumSize = new Vector2(70, 0), ThemeTypeVariation = "HintLabel" });
            foreach (var o in new[] { SquadOrder.Follow, SquadOrder.Hold, SquadOrder.Work, SquadOrder.Rest })
            {
                var oo = o;
                Btn(row, L.T("ORDER_" + o.ToString().ToUpperInvariant()), () => new HeroCommand { Kind = HeroCommandKind.Order, Person = m.Person, Order = oo, X = _near()?.X ?? 0, Y = _near()?.Y ?? 0 }, m.Order == o);
            }
            Btn(row, L.T("SQUAD_DISMISS"), () => new HeroCommand { Kind = HeroCommandKind.Dismiss, Person = m.Person });
            _body.AddChild(row);
            _body.AddChild(UiKit.Hint("      " + L.F("SQUAD_TRADE", UiKit.Pretty(m.Trade.ToLowerInvariant()), m.MaxSkill)));
        }
        // the person beside you (Q > Hire, Q > Invite to join)
        _body.AddChild(UiKit.Hint(L.T("SQUAD_NEAR")));
        if (v.Person is { } p && p.Id == _shown)
        {
            _body.AddChild(new Label { Text = $"{p.Name}, {p.Age}, {UiKit.Pretty(p.Trade.ToLowerInvariant())} ({L.F("SQUAD_SKILL", p.MaxSkill)})", ThemeTypeVariation = "GoldLabel" });
            _body.AddChild(UiKit.Body(L.F("SQUAD_KNOW", p.Familiarity, p.TrustPm / 10), true));
            _body.AddChild(UiKit.Body(L.F("SQUAD_ASKS", Quotes.Money(p.ReservationWage), Quotes.Money(p.FairOffer)), true));
            _body.AddChild(UiKit.Body(p.InSquad ? L.T("SQUAD_ALREADY") : p.RecruitCheck == RecruitAnswer.Accept ? L.F("SQUAD_WOULD_JOIN", Quotes.Money(p.FairOffer * 5)) : L.T("SQUAD_JOIN_" + p.RecruitCheck.ToString().ToUpperInvariant()), true));
            var acts = new HBoxContainer(); acts.AddThemeConstantOverride("separation", 8);
            Btn(acts, L.T("SQUAD_TALK"), () => new HeroCommand { Kind = HeroCommandKind.Talk, Person = p.Id });
            Btn(acts, L.F("SQUAD_HIRE", Quotes.Money(p.FairOffer)), () => new HeroCommand { Kind = HeroCommandKind.Hire, Person = p.Id, Wage = p.FairOffer, Days = 7 });
            Btn(acts, L.T("SQUAD_RECRUIT"), () => new HeroCommand { Kind = HeroCommandKind.Recruit, Person = p.Id, Wage = p.FairOffer });
            _body.AddChild(acts);
        }
        else _body.AddChild(UiKit.Body(L.T("SQUAD_NOBODY"), true));
    }

    void Btn(HBoxContainer row, string text, Func<HeroCommand> make, bool on = false)
    {
        var b = new Button { Text = text, CustomMinimumSize = new Vector2(0, 26), Disabled = _waitSeq > 0, ToggleMode = on, ButtonPressed = on };
        b.Pressed += () => { _waitSeq = _hw.Issue(make() with { IssuedSec = _now() }).Seq; Build(); };
        row.AddChild(b);
    }

    public override void _Process(double delta)
    {
        _refresh += delta;
        if (_refresh > 0.2)
        {
            _refresh = 0;
            var v = _hw.View;
            if (!ReferenceEquals(v, _view))
            {
                if (_waitSeq > 0 && v?.Results.FirstOrDefault(r => r.Seq == _waitSeq) is { } res) { _status.Text = res.Text; _status.AddThemeColorOverride("font_color", res.Ok ? PixelTheme.Body : PixelTheme.Warn); _waitSeq = 0; }
                Build();
            }
        }
        if (ShotPath == null || !Visible || _view == null || ++_shotWait != 12) return;
        var img = GetViewport().GetTexture().GetImage();
        System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(ShotPath) ?? ".");
        GD.Print($"[squad] screenshot {ShotPath}: {img.SavePng(ShotPath)}");
        GetTree().Quit();
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (!Visible) return;
        if (Actions.Pressed(e, "world.panel_squad")) { _router.Pop(); GetViewport().SetInputAsHandled(); }
    }
}
