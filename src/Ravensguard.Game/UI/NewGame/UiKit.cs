using System;
using System.Collections.Generic;
using Godot;
using Ravensguard.Game.Inputs;
using Ravensguard.Sim.Core;

namespace Ravensguard.Game.UI.NewGame;

/// <summary>Small widget helpers shared by the new-game screens. Widgets only; no game state.</summary>
public static class UiKit
{
    public static MarginContainer Margin(Control parent, int m = 24)
    {
        var margin = new MarginContainer();
        margin.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        foreach (var s in new[] { "left", "right", "top", "bottom" }) margin.AddThemeConstantOverride("margin_" + s, m);
        parent.AddChild(margin);
        return margin;
    }

    public static Label Title(string text) => new() { Text = text, ThemeTypeVariation = "TitleLabel" };
    public static Label Hint(string text, bool wrap = false) => new() { Text = text, ThemeTypeVariation = "HintLabel", AutowrapMode = wrap ? TextServer.AutowrapMode.WordSmart : TextServer.AutowrapMode.Off };
    public static Label Body(string text, bool wrap = true) => new() { Text = text, AutowrapMode = wrap ? TextServer.AutowrapMode.WordSmart : TextServer.AutowrapMode.Off };

    /// <summary>HintButton at a compact width that shows its key.</summary>
    public static Button Key(string text, string action, int width = 0, int height = 36)
    {
        var b = ScreenRouter.HintButton(text, action);
        b.CustomMinimumSize = new Vector2(width, height);
        return b;
    }

    static readonly HashSet<LineEdit> _capturing = new();

    /// <summary>Keeps the keyboard shortcuts quiet while a text field has focus. The flag follows the set of focused fields, so it clears when the field loses focus, is hidden, is disabled or leaves the tree,
    /// and a field gaining focus before the previous one reports losing it cannot leave the shortcuts on or off by accident.</summary>
    public static void CaptureKeys(LineEdit e)
    {
        void Set(bool on) { if (on) _capturing.Add(e); else _capturing.Remove(e); Actions.UiCaptured = _capturing.Count > 0; }
        e.FocusEntered += () => Set(true);
        e.FocusExited += () => Set(false);
        e.TreeExiting += () => Set(false);
        e.VisibilityChanged += () => { if (!e.IsVisibleInTree()) Set(false); };
        e.TextSubmitted += _ => e.ReleaseFocus();
    }

    /// <summary>A vertically scrolling body (no horizontal scroll: the content wraps or clips to the width), so a screen's Back bar can stay pinned under it at any window size.</summary>
    public static ScrollContainer Scroll(Control content)
    {
        var s = new ScrollContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, SizeFlagsVertical = Control.SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        content.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        s.AddChild(content);
        return s;
    }

    public static PanelContainer Panel(Control child, int minW = 0, int minH = 0)
    {
        var p = new PanelContainer { CustomMinimumSize = new Vector2(minW, minH) };
        p.AddChild(child);
        return p;
    }

    /// <summary>"- value +" stepper row with a label. Returns the row and a refresh callback.</summary>
    public static HBoxContainer Stepper(string label, Func<int> get, Action<int> set, int min, int max, int step, Func<int, string>? fmt = null)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 4);
        row.AddChild(new Label { Text = label, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
        var minus = new Button { Text = "-", CustomMinimumSize = new Vector2(28, 28) };
        var val = new Label { CustomMinimumSize = new Vector2(64, 0), HorizontalAlignment = HorizontalAlignment.Center };
        var plus = new Button { Text = "+", CustomMinimumSize = new Vector2(28, 28) };
        void Refresh() { val.Text = (fmt ?? (v => v.ToString()))(get()); }
        minus.Pressed += () => { set(Math.Max(min, get() - step)); Refresh(); };
        plus.Pressed += () => { set(Math.Min(max, get() + step)); Refresh(); };
        Refresh();
        row.AddChild(minus); row.AddChild(val); row.AddChild(plus);
        return row;
    }

    public static string Pretty(string id)
    {
        if (id.Length == 0) return id;
        var s = id.Replace('_', ' ');
        return char.ToUpperInvariant(s[0]) + s[1..];
    }

    public static string SeasonName(Season s) => L.T("SEASON_" + s.ToString().ToUpperInvariant());

    /// <summary>P7-CN2: every money string in the game goes through here (coinage 5.7): mites in, "1 gp 2 sp 3 cp" out, the unit words from the <c>mt_</c> rows.</summary>
    public static string Money(long mites, MoneyStyle style = MoneyStyle.Amount) => MoneyText.Format(mites, style, MoneyWords);
    /// <summary>A bulk price with its pack ("4.5 cp a kg", "2 sp 6 cp per 100 kg"), from the books' micro-mites per gram (a piece good: per piece).</summary>
    public static string PerUnit(long micro, bool piece) => MoneyText.PerUnit(micro, piece, MoneyWords);
    /// <summary>The pieces a purse holds, with their mass ("3 gold, 47 silver, 6 copper (26 g)").</summary>
    public static string Pieces(long gold, long silver, long copper, long massGrams) => MoneyText.Pieces(gold, silver, copper, massGrams, MoneyWords);
    static readonly Func<string, string> MoneyWords = MoneyText.Words(L.T);

    /// <summary>Texture from RGBA bytes (nearest filtering is set by the router theme).</summary>
    public static ImageTexture Tex(byte[] rgba, int w, int h) => ImageTexture.CreateFromImage(Image.CreateFromData(w, h, false, Image.Format.Rgba8, rgba));

    public static string Reason(string simReason) => simReason switch
    {
        "too much sea" => L.T("EMBARK_REASON_SEA"),
        "no settlements" => L.T("EMBARK_REASON_EMPTY"),
        "outside the continent" => L.T("EMBARK_REASON_OUTSIDE"),
        var r when r.StartsWith("active war") => L.T("EMBARK_REASON_WAR"),
        var r => r,
    };
}
