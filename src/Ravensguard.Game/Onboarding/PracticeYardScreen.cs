using System;
using System.Collections.Generic;
using Godot;
using Ravensguard.Game.Inputs;
using Ravensguard.Game.UI;
using Ravensguard.Game.UI.NewGame;
using Ravensguard.Sim.Onboarding;
using Ravensguard.Sim.Trade;

namespace Ravensguard.Game.Onboarding;

/// <summary>
/// The practice yard's training screen (onboarding spec 3.4): a timing bar for the swing (wind-up, then the gold strike window) against a training dummy, with the one-line reason under it ("too early", "good hit"), and a sparring
/// bout to three clean hits against the trainer. A stub until Phase 4 combat: it uses the rules in <see cref="PracticeYard"/> and nothing else. Skill gain is sent to the world at a barrier by <paramref name="gain"/>.
/// </summary>
public partial class PracticeYardScreen : Control
{
    readonly ScreenRouter _router;
    readonly Func<SwingResult, bool, string> _gain;        // (swing, spar bout done) -> the reason line to show, applies the skill use at the barrier
    readonly Func<int> _melee; readonly Func<int> _sparWaitMin; readonly bool _trainerHere;
    Label _reason = null!, _skill = null!, _state = null!; ColorRect _bar = null!, _window = null!, _marker = null!;
    double _t; bool _swinging; double _windup, _win; int _heroHits, _trainerHits; bool _sparring; string _weapon = "sword";
    public string? ShotPath { get; init; }
    int _shotWait;

    public PracticeYardScreen(ScreenRouter router, Func<SwingResult, bool, string> gain, Func<int> meleeLevel, Func<int> sparWaitMinutes, bool trainerHere)
    { _router = router; _gain = gain; _melee = meleeLevel; _sparWaitMin = sparWaitMinutes; _trainerHere = trainerHere; }

    public override void _Ready()
    {
        var margin = UiKit.Margin(this, 24);
        var root = new VBoxContainer(); root.AddThemeConstantOverride("separation", 8); margin.AddChild(root);
        root.AddChild(UiKit.Title(L.T("YARD_TITLE")));
        root.AddChild(UiKit.Hint(L.F("YARD_HINT", InputBootstrap.Hint("world.act"))));
        _skill = new Label(); root.AddChild(_skill);
        var barHolder = new Control { CustomMinimumSize = new Vector2(560, 36) };
        _bar = new ColorRect { Color = new Color(0.16f, 0.14f, 0.12f), Position = Vector2.Zero, Size = new Vector2(560, 36) }; barHolder.AddChild(_bar);
        (_windup, _win) = PracticeYard.Timing(_weapon);
        _window = new ColorRect { Color = PixelTheme.Gold, Position = new Vector2((float)(_windup / 1.2 * 560), 0), Size = new Vector2((float)(_win / 1.2 * 560), 36) }; barHolder.AddChild(_window);
        _marker = new ColorRect { Color = Colors.White, Position = Vector2.Zero, Size = new Vector2(4, 36) }; barHolder.AddChild(_marker);
        root.AddChild(UiKit.Panel(barHolder, 0, 0));
        _reason = new Label { ThemeTypeVariation = "TitleLabel" }; root.AddChild(_reason);
        _state = new Label(); root.AddChild(_state);
        var row = new HBoxContainer(); row.AddThemeConstantOverride("separation", 10); root.AddChild(row);
        var hit = new Button { Text = L.T("YARD_DUMMY"), CustomMinimumSize = new Vector2(220, 36) }; hit.Pressed += () => Swing(false); row.AddChild(hit);
        var spar = new Button { Text = L.T("YARD_SPAR"), CustomMinimumSize = new Vector2(220, 36), Disabled = !_trainerHere }; spar.Pressed += StartSpar; row.AddChild(spar);
        if (!_trainerHere) spar.TooltipText = L.T("YARD_NO_TRAINER");
        var back = UiKit.Key(L.T("UI_BACK"), "menu.back", 200); back.Pressed += _router.Pop; root.AddChild(back);
        Refresh();
    }

    void Refresh() => _skill.Text = L.F("YARD_SKILL", _melee());

    void StartSpar()
    {
        int wait = _sparWaitMin();
        if (wait > 0) { _reason.Text = L.F("YARD_SPAR_WAIT", wait); return; }
        _sparring = true; _heroHits = _trainerHits = 0; _state.Text = L.T("YARD_SPAR"); Swing(true);
    }

    void Swing(bool spar) { _swinging = true; _t = 0; if (!spar && _sparring) _sparring = false; }

    public override void _Input(InputEvent e)
    {
        if (!Visible || !_swinging) return;
        if (Actions.PressedAlways(e, "world.act") || (e is InputEventKey { Pressed: true, Echo: false, Keycode: Key.Space })) { Strike(); GetViewport().SetInputAsHandled(); }
    }

    void Strike()
    {
        var r = PracticeYard.Judge(_t, _windup, _win); _swinging = false;
        bool done = false;
        if (_sparring)
        {
            if (r == SwingResult.Good) _heroHits++; else _trainerHits++;
            _state.Text = $"{_heroHits} : {_trainerHits}";
            if (_heroHits >= 3 || _trainerHits >= 3) { done = true; _sparring = false; _reason.Text = L.T(_heroHits >= 3 ? "YARD_SPAR_WON" : "YARD_SPAR_LOST"); }
            else Callable.From(() => Swing(true)).CallDeferred();
        }
        string line = _gain(r, done);
        if (!done) _reason.Text = line;
        Refresh();
    }

    public override void _Process(double delta)
    {
        if (_swinging) { _t += delta * 0.6; _marker.Position = new Vector2((float)Math.Min(1.2, _t) / 1.2f * 560f, 0); if (_t > 1.2) Strike(); }
        if (ShotPath == null || !Visible || ++_shotWait != 8) return;
        var img = GetViewport().GetTexture().GetImage();
        System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(ShotPath) ?? ".");
        img.SavePng(ShotPath); GD.Print("shot: " + ShotPath);
    }
}
