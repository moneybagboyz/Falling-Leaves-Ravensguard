using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Ravensguard.Game.Inputs;
using Ravensguard.Game.UI.NewGame;
using Ravensguard.Sim.Combat.Flavour;

namespace Ravensguard.Game.UI;

/// <summary>
/// The after-fight report (feedback-flavour-log 4.3, Shift+F7): the result and its numbers, what you did, your wounds with what to do first, the fallen with the line that finished them and the
/// chain of what led to it, the spoils, and what to do next. A modal over whatever is behind it: it swallows every key and click but its own (Esc, Enter or Shift+F7 close it), its body scrolls
/// so it reads at 960x540, and nothing is lost by closing (the report is rebuilt from the log on reopening). Pure view over <see cref="AfterFightReport"/>.
/// </summary>
public partial class AfterFightPanel : Control
{
    readonly AfterFightReport _report;
    readonly int _spoils, _arrows;
    public Action? Closed { get; set; }
    /// <summary>P6-B1 (medicine 4.2): opens the treatment screen on the hero; with <see cref="Bleeding"/> above 0 the report says how many wounds still bleed and offers it.</summary>
    public Action? Treat { get; init; }
    public int Bleeding { get; init; }

    public AfterFightPanel(AfterFightReport report, int groundItems = 0, int arrows = 0) { _report = report; _spoils = groundItems; _arrows = arrows; Name = "AfterFightPanel"; }

    public override void _Ready()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Stop;
        var dim = new ColorRect { Color = new Color(0, 0, 0, 0.62f), MouseFilter = MouseFilterEnum.Stop }; dim.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect); AddChild(dim);
        var margin = new MarginContainer(); margin.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        foreach (var s in new[] { "left", "right", "top", "bottom" }) margin.AddThemeConstantOverride("margin_" + s, 24);
        AddChild(margin);
        var panel = new PanelContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ExpandFill }; margin.AddChild(panel);
        var solid = new StyleBoxFlat { BgColor = PixelTheme.Fill, BorderColor = PixelTheme.Rim, ContentMarginLeft = 12, ContentMarginRight = 12, ContentMarginTop = 8, ContentMarginBottom = 8 };
        solid.SetBorderWidthAll(2);
        panel.AddThemeStyleboxOverride("panel", solid);
        var root = new VBoxContainer(); root.AddThemeConstantOverride("separation", 6); panel.AddChild(root);
        root.AddChild(UiKit.Title(L.T("fl_ui_report_title") + ": " + L.T("fl_ui_rep_res_" + _report.Result)));
        var body = new VBoxContainer(); body.AddThemeConstantOverride("separation", 4);
        root.AddChild(UiKit.Scroll(body));
        var r = _report;

        Section(body, "fl_ui_rep_result");
        Line(body, r.ResultLine, true);
        Line(body, L.F("fl_ui_rep_duration", FightLog.Clock(r.DurationSec * 20), string.Join(" / ", r.SideSizes)));
        Line(body, L.F("fl_ui_rep_casualties", r.Dead, r.Down, r.Yielded, r.Routed));

        Section(body, "fl_ui_rep_you");
        Line(body, L.F("fl_ui_rep_blows", r.Thrown, r.Landed, r.Blocked, r.Parried, r.Dodged, r.Missed));
        Line(body, L.F("fl_ui_rep_damage", r.Edge, r.Point, r.Blunt, r.Missile));
        if (r.BestLine.Length > 0) Line(body, r.BestLine, true);
        Line(body, L.F("fl_ui_rep_low", r.StaminaLowPct, r.LossHighPm / 10));
        Line(body, L.T("fl_ui_rep_skills"), false, true);

        Section(body, "fl_ui_rep_wounds");
        if (Bleeding > 0) { var cta = Line(body, L.F("tr_after_bleeding", Bleeding), true); cta.AddThemeColorOverride("font_color", PixelTheme.Warn); }
        if (r.Wounds.Count == 0 && r.MinorWounds == 0) Line(body, L.T("fl_ui_rep_no_wounds"), false, true);
        if (r.MinorWounds > 0) Line(body, L.F("fl_ui_rep_minor", r.MinorWounds), false, true);
        foreach (var w in r.Wounds)
        {
            Line(body, "- " + w.Text);
            string adv = w.Advice.Length == 0 ? "" : w.Seconds > 0 ? L.F(w.Advice, w.Seconds) : L.T(w.Advice);
            if (adv.Length > 0) { var l = Line(body, "    " + adv, false, true); l.AddThemeColorOverride("font_color", w.Advice is "fl_ui_adv_artery" or "fl_ui_adv_organ" or "fl_ui_adv_sever" or "fl_ui_adv_spine" ? PixelTheme.Warn : PixelTheme.Gold); }
        }

        Section(body, "fl_ui_rep_fallen");
        if (r.Fallen.Count == 0) Line(body, L.T("fl_ui_rep_no_fallen"), false, true);
        foreach (var f in r.Fallen)
        {
            Line(body, "- " + f.FinishingLine);
            if (f.Chain.Text.Length > 0) Line(body, "    " + L.F("fl_ui_rep_chain", f.Chain.Text), false, true);
        }

        Section(body, "fl_ui_rep_spoils");
        Line(body, _spoils + _arrows == 0 ? L.T("fl_ui_rep_no_spoils") : L.F("fl_ui_rep_spoils_line", _spoils, _arrows), false, true);
        Section(body, "fl_ui_rep_next");
        Line(body, L.T("fl_ui_rep_next_hint"), false, true);

        var bottom = new HBoxContainer(); bottom.AddThemeConstantOverride("separation", 8); root.AddChild(bottom);
        var close = new Button { Text = L.T("fl_ui_close"), CustomMinimumSize = new Vector2(160, 32) }; close.Pressed += Close; bottom.AddChild(close);
        if (Treat != null && Bleeding > 0) { var tb = new Button { Text = L.T("tr_after_btn"), CustomMinimumSize = new Vector2(180, 32) }; tb.Pressed += () => { Close(); Treat(); }; bottom.AddChild(tb); }
        bottom.AddChild(UiKit.Hint(L.F("fl_ui_close_hint", InputBootstrap.Hint("world.last_report"))));
        close.GrabFocus();
    }

    static void Section(VBoxContainer v, string key)
    {
        var l = new Label { Text = L.T(key) };
        l.AddThemeColorOverride("font_color", PixelTheme.Gold);
        v.AddChild(l);
    }

    static Label Line(VBoxContainer v, string text, bool strong = false, bool dim = false)
    {
        var l = new Label { Text = text, AutowrapMode = TextServer.AutowrapMode.WordSmart, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        if (dim) l.ThemeTypeVariation = "HintLabel";
        if (strong) l.AddThemeColorOverride("font_color", PixelTheme.Body);
        v.AddChild(l);
        return l;
    }

    public void Close() { Closed?.Invoke(); }

    /// <summary>The input guard of a modal: Esc, Enter and Shift+F7 close it, every other key is swallowed so the fight view behind it never sees it (the fight view also checks <see cref="Open"/> for the mouse).</summary>
    public bool Open => Visible && IsInsideTree();

    public override void _Input(InputEvent e)
    {
        if (!Visible) return;
        if (e is InputEventKey { Pressed: true, Echo: false } k)
        {
            if (Actions.Pressed(e, "menu.back") || Actions.Pressed(e, "combatstance.last_report") || Actions.Pressed(e, "world.last_report") || k.Keycode is Key.Enter or Key.KpEnter) Close();
            GetViewport().SetInputAsHandled();
        }
    }
}
