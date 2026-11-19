using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Ravensguard.Game.Inputs;
using Ravensguard.Game.UI.NewGame;
using Ravensguard.Game.World.Combat;
using Ravensguard.Sim.Combat.Melee2;
using Ravensguard.Sim.Combat.Watch;
using Ravensguard.Sim.Travel;

namespace Ravensguard.Game.UI;

/// <summary>
/// The watch-mode HUD on the combat view (watch-mode 7): the Watch button (F6) with the stance chip (Shift+F6 cycles; a click opens the stance panel: the presets, the toggles of
/// 3.1, the flee thresholds and whom to protect), the odds with the counts of allies and enemies, the hero's blood and pain bars with the collapse countdown, the banner
/// "WATCH - press F6 or any combat input to take control", the "why" line, and the "Take control?" prompt of a danger pause. A thin view over the fight's <see cref="WatchState"/>:
/// every change goes back through the view's recorded inputs. The stance panel and the prompt are a modal input guard (Esc and F8 pass).
/// </summary>
public partial class WatchHud : Control
{
    readonly FightView _view;
    Button _watch = null!, _chip = null!;
    Label _odds = null!, _banner = null!, _why = null!, _toast = null!, _bars = null!;
    PanelContainer _top = null!, _stancePanel = null!, _prompt = null!;
    VBoxContainer _stanceBody = null!;
    Label _promptText = null!;
    Button _promptTake = null!, _promptKeep = null!;
    double _toastLeft;
    string _built = "";

    public WatchHud(FightView view) { _view = view; Name = "WatchHud"; }

    /// <summary>The mouse is over the HUD's box (its buttons take the click, not the sword arm).</summary>
    public bool Covers(Vector2 p) => IsInsideTree() && (_top.GetGlobalRect().HasPoint(p) || ModalOpen);

    /// <summary>The stance panel or the danger prompt is up: the fight's keys and clicks wait.</summary>
    public bool ModalOpen => _stancePanel.Visible || _prompt.Visible;

    public override void _Ready()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;
        // the top-right box: Watch, the stance chip, the odds, the bars
        _top = new PanelContainer { MouseFilter = MouseFilterEnum.Stop };
        _top.SetAnchorsAndOffsetsPreset(LayoutPreset.BottomRight);              // bottom right: the help lines run along the top, the log feed along the bottom left
        _top.OffsetLeft = -328; _top.OffsetTop = -132; _top.OffsetRight = -8; _top.OffsetBottom = -8;
        _top.GrowHorizontal = GrowDirection.Begin; _top.GrowVertical = GrowDirection.Begin;
        _top.AddThemeStyleboxOverride("panel", Box(0.72f, false));
        AddChild(_top);
        var v = new VBoxContainer(); v.AddThemeConstantOverride("separation", 4); _top.AddChild(v);
        var row = new HBoxContainer(); row.AddThemeConstantOverride("separation", 6); v.AddChild(row);
        _watch = new Button { CustomMinimumSize = new Vector2(132, 30), FocusMode = FocusModeEnum.None, ToggleMode = true };
        _watch.Pressed += () => _view.ToggleWatch();
        row.AddChild(_watch);
        _chip = new Button { CustomMinimumSize = new Vector2(170, 30), FocusMode = FocusModeEnum.None, SizeFlagsHorizontal = SizeFlags.ExpandFill, ClipText = true };
        _chip.Pressed += OpenStance;
        row.AddChild(_chip);
        _odds = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(308, 0) }; _odds.AddThemeFontSizeOverride("font_size", 14); v.AddChild(_odds);
        _bars = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(308, 0) }; _bars.AddThemeFontSizeOverride("font_size", 14); v.AddChild(_bars);
        // the banner and the why line, top centre
        var bannerBox = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        bannerBox.SetAnchorsAndOffsetsPreset(LayoutPreset.CenterTop);
        bannerBox.OffsetLeft = -250; bannerBox.OffsetRight = 250; bannerBox.OffsetTop = 84;
        AddChild(bannerBox);
        _banner = new Label { HorizontalAlignment = HorizontalAlignment.Center, AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(500, 0) };
        _banner.AddThemeColorOverride("font_color", new Color(1f, 0.85f, 0.35f)); _banner.AddThemeFontSizeOverride("font_size", 16);
        _banner.AddThemeConstantOverride("outline_size", 4); _banner.AddThemeColorOverride("font_outline_color", Colors.Black);
        bannerBox.AddChild(_banner);
        _why = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(308, 0) };
        _why.AddThemeFontSizeOverride("font_size", 15); _why.AddThemeColorOverride("font_color", new Color(1f, 0.85f, 0.35f));
        v.AddChild(_why);                                                       // the "why" line: what the brain is doing for you
        _toast = new Label { HorizontalAlignment = HorizontalAlignment.Center, CustomMinimumSize = new Vector2(500, 0), AutowrapMode = TextServer.AutowrapMode.WordSmart };
        _toast.AddThemeColorOverride("font_color", new Color(1f, 0.6f, 0.5f)); _toast.AddThemeFontSizeOverride("font_size", 15);
        _toast.AddThemeConstantOverride("outline_size", 4); _toast.AddThemeColorOverride("font_outline_color", Colors.Black);
        bannerBox.AddChild(_toast);
        BuildStancePanel();
        BuildPrompt();
        Refresh();
    }

    // ------------------------------------------------------------------ the frame

    public override void _Process(double delta)
    {
        if (_toastLeft > 0) { _toastLeft -= delta; if (_toastLeft <= 0) _toast.Text = ""; }
        _dim.Visible = ModalOpen;
        Refresh();
    }

    static string Pct(int pm) => $"{(pm + 5) / 10}%";

    void Refresh()
    {
        var w = _view.W; var h = _view.HeroF; var ws = w.Watch;
        bool on = _view.Watching;
        _watch.ButtonPressed = on;
        _watch.Text = L.F("wm_button", InputBootstrap.Hint("combatstance.watch_toggle"));
        var st = ws?.Stance ?? (h != null ? WatchStance.Of(w.Content, WatchStance.PresetOf(h.Stance.Id)) : default);
        _chip.Text = L.F("wm_chip", L.T("wm_stance_" + st.Id) + (st.Custom ? "*" : ""), InputBootstrap.Hint("combatstance.stance_next"));
        _chip.TooltipText = L.F("wm_chip_tip", Pct(st.FleeBloodPm), st.FleeMoraleBelow);
        int odds = ws?.OddsPm ?? RiskEstimate.OddsOf(w, h?.Team ?? 0);
        _odds.Text = L.F("wm_odds", Pct(odds), L.T(RiskEstimate.Word(odds)), ws?.Allies ?? w.Fighters.Count(f => f.Team == (h?.Team ?? 0) && f.Active), ws?.Enemies ?? w.Fighters.Count(f => f.Team != (h?.Team ?? 0) && f.Active));
        if (h != null)
        {
            int pain = (int)Math.Min(100, h.Book.PainX100 * 100L / Math.Max(1, h.Book.KnockoutPainX100));
            int collapse = h.Book.SecondsToCollapse();
            _bars.Text = L.F("wm_bars", Pct(h.Book.LossPm), pain) + (collapse >= 0 && collapse < 120 ? "  " + L.F("wm_collapse", collapse) : "");
            _bars.AddThemeColorOverride("font_color", h.Book.LossPm >= 200 || pain >= 70 ? new Color(1f, 0.45f, 0.4f) : Colors.White);
        }
        _banner.Text = on ? (ws!.TakeBackOnInput ? L.F("wm_banner_input", InputBootstrap.Hint("combatstance.watch_toggle")) : L.F("wm_banner", InputBootstrap.Hint("combatstance.watch_toggle"))) : "";
        // the why line: while watching, and for 3 s after a take-back
        bool showWhy = ws != null && (on || w.Tick - ws.DecisionTick < 60) && ws.LastDecision != WatchReason.None;
        _why.Text = showWhy ? WhyText(ws!) : "";
        string key = $"{st.PackA()}|{ws?.ProtectFighter}";
        if (_stancePanel.Visible && key != _built) FillStance();
    }

    string WhyText(WatchState ws)
    {
        var w = _view.W;
        string arg = ws.LastDecision switch
        {
            WatchReason.Protecting or WatchReason.Treating => ws.LastDecisionArg >= 0 && ws.LastDecisionArg < w.Fighters.Count ? w.Fighters[ws.LastDecisionArg].Name : "",
            _ => ws.LastDecisionArg.ToString(),
        };
        return L.F(WatchWhy.Key(ws.LastDecision), arg);
    }

    /// <summary>A watch notice (WatchLosing, WatchAmmoOut) for a few seconds under the why line.</summary>
    public void Toast(string text, double seconds = 4) { _toast.Text = text; _toastLeft = seconds; }

    // ------------------------------------------------------------------ the danger prompt (4.5)

    /// <summary>The HUD's panel look (the fight's canvas layer has no theme of its own).</summary>
    static StyleBoxFlat Box(float alpha, bool rim)
    {
        var b = new StyleBoxFlat { BgColor = new Color(PixelTheme.Fill, alpha), BorderColor = PixelTheme.Rim, ContentMarginLeft = 10, ContentMarginRight = 10, ContentMarginTop = 6, ContentMarginBottom = 6 };
        b.SetBorderWidthAll(rim ? 2 : 1);
        return b;
    }

    ColorRect _dim = null!;

    void BuildPrompt()
    {
        _prompt = new PanelContainer { Visible = false, MouseFilter = MouseFilterEnum.Stop };
        _prompt.AddThemeStyleboxOverride("panel", Box(1f, true));
        _prompt.SetAnchorsAndOffsetsPreset(LayoutPreset.Center);
        _prompt.OffsetLeft = -220; _prompt.OffsetRight = 220; _prompt.OffsetTop = -70; _prompt.OffsetBottom = 70;
        AddChild(_prompt);
        var v = new VBoxContainer(); v.AddThemeConstantOverride("separation", 8); _prompt.AddChild(v);
        v.AddChild(UiKit.Title(L.T("wm_prompt_title")));
        _promptText = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(420, 0) }; v.AddChild(_promptText);
        var row = new HBoxContainer(); row.AddThemeConstantOverride("separation", 8); v.AddChild(row);
        _promptTake = new Button { Text = L.F("wm_prompt_take", InputBootstrap.Hint("combatstance.watch_toggle")), CustomMinimumSize = new Vector2(200, 32) };
        _promptTake.Pressed += () => { _prompt.Visible = false; _view.AnswerDanger(take: true); };
        row.AddChild(_promptTake);
        _promptKeep = new Button { Text = L.T("wm_prompt_keep"), CustomMinimumSize = new Vector2(200, 32) };
        _promptKeep.Pressed += () => { _prompt.Visible = false; _view.AnswerDanger(take: false); };
        row.AddChild(_promptKeep);
    }

    /// <summary>A pausing watch interrupt: "Take control?" (WatchDanger) or "The fight is over" (WatchOver, only Continue).</summary>
    public void ShowPrompt(WatchSignal s)
    {
        string reason = s.Kind == InterruptKind.WatchOver ? L.T(s.Value == 1 ? "wm_over_won" : "wm_over_lost") : L.T(s.Value switch { 1 => "wm_danger_blood", 2 => "wm_danger_pain", 3 => "wm_danger_morale", _ => "wm_danger_artery" });
        _promptText.Text = reason;
        _promptTake.Visible = s.Kind == InterruptKind.WatchDanger;
        _promptKeep.Text = L.T(s.Kind == InterruptKind.WatchDanger ? "wm_prompt_keep" : "wm_prompt_continue");
        _prompt.Visible = true;
        (s.Kind == InterruptKind.WatchDanger ? _promptTake : _promptKeep).GrabFocus();
    }

    // ------------------------------------------------------------------ the stance panel (3.1)

    void BuildStancePanel()
    {
        _dim = new ColorRect { Color = new Color(0, 0, 0, 0.55f), MouseFilter = MouseFilterEnum.Stop, Visible = false };
        _dim.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(_dim);
        _stancePanel = new PanelContainer { Visible = false, MouseFilter = MouseFilterEnum.Stop };
        _stancePanel.AddThemeStyleboxOverride("panel", Box(1f, true));
        _stancePanel.SetAnchorsAndOffsetsPreset(LayoutPreset.Center);
        _stancePanel.OffsetLeft = -300; _stancePanel.OffsetRight = 300; _stancePanel.OffsetTop = -215; _stancePanel.OffsetBottom = 215;
        AddChild(_stancePanel);
        var v = new VBoxContainer(); v.AddThemeConstantOverride("separation", 6); _stancePanel.AddChild(v);
        var head = new HBoxContainer(); v.AddChild(head);
        var title = UiKit.Title(L.T("wm_stance_title")); title.SizeFlagsHorizontal = SizeFlags.ExpandFill; head.AddChild(title);
        var close = new Button { Text = L.T("wm_close"), CustomMinimumSize = new Vector2(90, 28) }; close.Pressed += () => _stancePanel.Visible = false; head.AddChild(close);
        _stanceBody = new VBoxContainer(); _stanceBody.AddThemeConstantOverride("separation", 4);
        var scroll = UiKit.Scroll(_stanceBody); scroll.CustomMinimumSize = new Vector2(580, 360);
        v.AddChild(scroll);
    }

    void OpenStance() { _stancePanel.Visible = true; FillStance(); }
    /// <summary>The demo's screenshot opens the panel as a click would.</summary>
    public void OpenStanceForShot() => OpenStance();
    public void CloseStance() => _stancePanel.Visible = false;

    WatchStance Current() => _view.W.Watch?.Stance ?? (_view.HeroF is { } h ? WatchStance.Of(_view.W.Content, WatchStance.PresetOf(h.Stance.Id)) : WatchStance.Of(_view.W.Content, StancePreset.Balanced));
    int ProtectNow() => _view.W.Watch?.ProtectFighter ?? -1;

    void FillStance()
    {
        foreach (var c in _stanceBody.GetChildren()) c.QueueFree();
        var st = Current(); var c0 = _view.W.Content;
        _built = $"{st.PackA()}|{ProtectNow()}";
        var presets = new HFlowContainer(); presets.AddThemeConstantOverride("h_separation", 4); presets.AddThemeConstantOverride("v_separation", 4);
        _stanceBody.AddChild(presets);
        foreach (StancePreset p in Enum.GetValues<StancePreset>())
        {
            var b = new Button { Text = L.T("wm_stance_" + WatchStance.Ids[(int)p]), ToggleMode = true, ButtonPressed = st.Preset == p && !st.Custom, CustomMinimumSize = new Vector2(120, 28) };
            var pp = p; b.Pressed += () => _view.SetStance(WatchStance.Of(c0, pp) with { Protect = st.Protect }, ProtectNow());
            presets.AddChild(b);
        }
        _stanceBody.AddChild(new Label { Text = L.T("wm_stance_desc_" + st.Id), AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(560, 0), ThemeTypeVariation = "HintLabel" });
        void Toggle(StanceFlags f, string key)
        {
            var cb = new CheckBox { Text = L.T(key), ButtonPressed = (st.Flags & f) != 0 };
            cb.Toggled += on => _view.SetStance(Current().WithFlag(f, on), ProtectNow());
            _stanceBody.AddChild(cb);
        }
        Toggle(StanceFlags.NoLethal, "wm_opt_nolethal");
        Toggle(StanceFlags.CalledShots, "wm_opt_called");
        Toggle(StanceFlags.UseRanged, "wm_opt_ranged");
        Toggle(StanceFlags.TreatAllies, "wm_opt_treat");
        void Stepper(string key, string value, Func<WatchStance, int, WatchStance> change)
        {
            var row = new HBoxContainer(); row.AddThemeConstantOverride("separation", 6);
            row.AddChild(new Label { Text = L.F(key, value), CustomMinimumSize = new Vector2(380, 0) });
            var minus = new Button { Text = "-", CustomMinimumSize = new Vector2(36, 26) }; minus.Pressed += () => _view.SetStance(change(Current(), -1) with { Flags = Current().Flags | StanceFlags.Custom }, ProtectNow());
            var plus = new Button { Text = "+", CustomMinimumSize = new Vector2(36, 26) }; plus.Pressed += () => _view.SetStance(change(Current(), 1) with { Flags = Current().Flags | StanceFlags.Custom }, ProtectNow());
            row.AddChild(minus); row.AddChild(plus); _stanceBody.AddChild(row);
        }
        Stepper("wm_opt_flee_blood", Pct(st.FleeBloodPm), (s, d) => s with { FleeBloodPm = (ushort)Math.Clamp(s.FleeBloodPm + 50 * d, 50, 450) });
        Stepper("wm_opt_flee_morale", st.FleeMoraleBelow.ToString(), (s, d) => s with { FleeMoraleBelow = (byte)Math.Clamp(s.FleeMoraleBelow + 5 * d, 0, 60) });
        Stepper("wm_opt_flee_odds", st.FleeOddsBelow10 == 0 ? L.T("wm_never") : $"{st.FleeOddsBelow10}%", (s, d) => s with { FleeOddsBelow10 = (byte)Math.Clamp(s.FleeOddsBelow10 + 5 * d, 0, 50) });
        // whom to protect: the party and the allies in the fight
        var h = _view.HeroF;
        if (h != null)
        {
            _stanceBody.AddChild(new Label { Text = L.T("wm_protect_who") });
            var flow = new HFlowContainer(); flow.AddThemeConstantOverride("h_separation", 4); _stanceBody.AddChild(flow);
            var none = new Button { Text = L.T("wm_protect_none"), ToggleMode = true, ButtonPressed = ProtectNow() < 0 }; none.Pressed += () => _view.SetStance(Current(), -1); flow.AddChild(none);
            foreach (var a in _view.W.Fighters.Where(f => f.Team == h.Team && f.Id != h.Id && f.Act is not (FAct.Dead or FAct.Fled) && !_view.W.IsHorse(f)))
            {
                int id = a.Id;
                var b = new Button { Text = a.Name, ToggleMode = true, ButtonPressed = ProtectNow() == id };
                b.Pressed += () => _view.SetStance(Current().Preset == StancePreset.Protect ? Current() : WatchStance.Of(_view.W.Content, StancePreset.Protect), id);
                flow.AddChild(b);
            }
        }
        _stanceBody.AddChild(new Label { Text = L.T("wm_stance_note"), ThemeTypeVariation = "HintLabel", AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(560, 0) });
    }

    /// <summary>The modal input guard of the stance panel and the prompt: keys stop here (Esc closes the panel; F8 goes on to the bug report).</summary>
    public override void _Input(InputEvent e)
    {
        if (!ModalOpen || e is not InputEventKey { Pressed: true }) return;
        if (Actions.PressedAlways(e, "world.report_bug")) return;
        if (_stancePanel.Visible && (Actions.Pressed(e, "combatstance.back") || Actions.Pressed(e, "menu.back") || Actions.Pressed(e, "world.back"))) { _stancePanel.Visible = false; GetViewport().SetInputAsHandled(); return; }
        if (GetViewport().GuiGetFocusOwner() is Button b && IsAncestorOf(b) && (e.IsActionPressed("ui_accept") || e.IsActionPressed("ui_left") || e.IsActionPressed("ui_right"))) return;
        GetViewport().SetInputAsHandled();
    }
}
