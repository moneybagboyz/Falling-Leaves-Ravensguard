using Godot;
using Ravensguard.Game.Inputs;
using Ravensguard.Game.UI;
using Ravensguard.Sim.Onboarding;

namespace Ravensguard.Game.Onboarding;

/// <summary>
/// The one-line hint at the bottom centre (onboarding spec 3.1): the words with the keys resolved, and "learn more" with its key (default Enter) or a click, which opens the Codex page. One at a time, six real seconds, a
/// half second fade. It only draws what <see cref="HintEngine"/> hands it; it keeps no game state.
/// </summary>
public partial class HintBar : PanelContainer
{
    readonly Label _text = new() { AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(520, 0), HorizontalAlignment = HorizontalAlignment.Center };
    readonly Label _more = new() { ThemeTypeVariation = "HintLabel", HorizontalAlignment = HorizontalAlignment.Center };
    double _left; HintToastData? _cur;
    public System.Action<string>? OpenCodex { get; set; }

    public sealed record HintToastData(string Id, string Text, string LearnMore);

    public HintBar()
    {
        Name = "HintBar"; MouseFilter = MouseFilterEnum.Stop; Visible = false; ZIndex = 80;
        SetAnchorsAndOffsetsPreset(LayoutPreset.CenterBottom);
        AnchorLeft = 0.5f; AnchorRight = 0.5f; AnchorTop = 1; AnchorBottom = 1; GrowHorizontal = GrowDirection.Both; GrowVertical = GrowDirection.Begin; OffsetTop = -96; OffsetBottom = -60;
        var v = new VBoxContainer(); v.AddThemeConstantOverride("separation", 2); AddChild(v);
        v.AddChild(_text); v.AddChild(_more);
        GuiInput += e => { if (e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } && _cur != null) Learn(); };
    }

    public bool Showing => Visible && _cur != null;
    public string? CurrentId => _cur?.Id;

    public void Show(HintToastData t)
    {
        _cur = t; _text.Text = t.Text;
        _more.Text = t.LearnMore.Length > 0 ? L.F("HINT_LEARN_MORE", InputBootstrap.Hint("world.hint_more")) : "";
        _left = HintEngine.ShowSeconds; Modulate = Colors.White; Visible = true;
    }

    public void Learn() { if (_cur is { LearnMore.Length: > 0 } c) { OpenCodex?.Invoke(c.LearnMore); Dismiss(); } }
    public void Dismiss() { _cur = null; Visible = false; _left = 0; }

    public override void _Process(double delta)
    {
        if (_cur == null) return;
        _left -= delta;
        if (_left <= HintEngine.FadeSeconds) Modulate = new Color(1, 1, 1, (float)Mathf.Clamp(_left / HintEngine.FadeSeconds, 0, 1));
        if (_left <= 0) Dismiss();
    }
}
