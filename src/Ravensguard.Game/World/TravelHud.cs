using System.Collections.Generic;
using Godot;
using Ravensguard.Game.UI;

namespace Ravensguard.Game.World;

/// <summary>
/// The travel HUD (travel spec 6): the journey banner at the top of the screen ("Travelling to Oakford (6.1 km, 11 h)" with the clock multiplier and the Stop key), the alert panel an interrupt opens
/// (what happened, what the keys do), the encounter line, and the camp panel (fire, shelter, cook, watch, talk, sleep, break camp). Pure view: StreetView.Travel.cs sets the strings each frame.
/// </summary>
public sealed partial class TravelHud : Control
{
    readonly PanelContainer _banner = new() { MouseFilter = MouseFilterEnum.Ignore, Visible = false };
    readonly Label _bannerTitle = new() { ThemeTypeVariation = "TitleLabel" }, _bannerLine = new(), _bannerWarn = new() { Modulate = new Color(1f, 0.6f, 0.4f) };
    readonly ColorRect _barBack = new() { Color = new Color(0.12f, 0.1f, 0.08f, 0.9f), CustomMinimumSize = new Vector2(360, 8) }, _bar = new() { Color = new Color(0.9f, 0.72f, 0.3f) };
    readonly PanelContainer _alert = new() { MouseFilter = MouseFilterEnum.Ignore, Visible = false };
    readonly Label _alertTitle = new() { ThemeTypeVariation = "TitleLabel" }, _alertBody = new(), _alertKeys = new() { ThemeTypeVariation = "HintLabel" };
    readonly PanelContainer _camp = new() { MouseFilter = MouseFilterEnum.Ignore, Visible = false };
    readonly Label _campTitle = new() { ThemeTypeVariation = "TitleLabel" };
    readonly List<Label> _campLines = [];
    readonly VBoxContainer _campBox = new() { MouseFilter = MouseFilterEnum.Ignore };

    public TravelHud()
    {
        _bannerTitle.AddThemeFontSizeOverride("font_size", 18);
        MouseFilter = MouseFilterEnum.Ignore;
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        _banner.AnchorLeft = 0.5f; _banner.AnchorRight = 0.5f; _banner.OffsetLeft = -140; _banner.OffsetRight = 300; _banner.OffsetTop = 12;
        var bv = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore }; bv.AddThemeConstantOverride("separation", 3);
        _banner.AddChild(bv); bv.AddChild(_bannerTitle); bv.AddChild(_barBack); _barBack.AddChild(_bar); _bar.SetAnchorsAndOffsetsPreset(LayoutPreset.LeftWide); bv.AddChild(_bannerLine); bv.AddChild(_bannerWarn);
        AddChild(_banner);
        _alert.AnchorLeft = 0.5f; _alert.AnchorRight = 0.5f; _alert.AnchorTop = 1f; _alert.AnchorBottom = 1f; _alert.OffsetLeft = -230; _alert.OffsetRight = 230; _alert.OffsetTop = -200; _alert.OffsetBottom = -50; _alert.GrowHorizontal = GrowDirection.Both; _alert.GrowVertical = GrowDirection.Begin;
        var av = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore }; av.AddThemeConstantOverride("separation", 6);
        _alertBody.AutowrapMode = TextServer.AutowrapMode.WordSmart; _alertBody.CustomMinimumSize = new Vector2(430, 0);
        _alert.AddChild(av); av.AddChild(_alertTitle); av.AddChild(_alertBody); av.AddChild(_alertKeys);
        AddChild(_alert);
        _camp.AnchorLeft = 1; _camp.AnchorRight = 1; _camp.OffsetLeft = -330; _camp.OffsetRight = -12; _camp.OffsetTop = 190;
        _campBox.AddThemeConstantOverride("separation", 2); _campBox.AddChild(_campTitle);
        for (int i = 0; i < 12; i++) { var l = new Label(); _campLines.Add(l); _campBox.AddChild(l); }
        _camp.AddChild(_campBox); AddChild(_camp);
    }

    public void Banner(string title, float progress, string line, string warn)
    {
        _banner.Visible = true; _bannerTitle.Text = title; _bannerLine.Text = line; _bannerWarn.Text = warn; _bannerWarn.Visible = warn.Length > 0;
        _bar.AnchorRight = Mathf.Clamp(progress, 0f, 1f);
    }
    public void HideBanner() => _banner.Visible = false;

    public void Alert(string title, string body, string keys, bool danger)
    {
        _alert.Visible = true; _alertTitle.Text = title; _alertBody.Text = body; _alertKeys.Text = keys;
        _alertTitle.Modulate = danger ? new Color(1f, 0.55f, 0.4f) : Colors.White;
    }
    public void HideAlert() => _alert.Visible = false;

    public bool AlertShown => _alert.Visible;

    public void Camp(string title, IReadOnlyList<string> lines)
    {
        _camp.Visible = true; _campTitle.Text = title;
        for (int i = 0; i < _campLines.Count; i++) { _campLines[i].Visible = i < lines.Count; if (i < lines.Count) _campLines[i].Text = lines[i]; }
    }
    public void HideCamp() => _camp.Visible = false;
    public bool CampShown => _camp.Visible;
}
