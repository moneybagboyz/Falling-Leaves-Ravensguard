using Godot;

namespace Ravensguard.Game.UI;

/// <summary>Builds the pixel UI theme in code: 9-slice pixel panels, no rounded corners, nearest filtering.</summary>
public static class PixelTheme
{
    public static readonly Color Backdrop = new("141118");
    public static readonly Color Fill = new("2b2420");
    public static readonly Color Border = new("1a1418");
    public static readonly Color Rim = new("6b5a48");
    public static readonly Color Gold = new("e0b450");
    public static readonly Color Body = new("e8dcc0");
    public static readonly Color Dim = new("8c7e68");
    public static readonly Color Warn = new("d96a4a");

    const string FontPath = "res://assets/fonts/PixelifySans.ttf";

    /// <summary>8x8 image: 2px border, 1px inner rim, then fill; used as a 9-slice with 3px margins.</summary>
    static StyleBoxTexture Box(Color fill, Color rim, float margin = 8)
    {
        var img = Image.CreateEmpty(8, 8, false, Image.Format.Rgba8);
        for (int y = 0; y < 8; y++)
            for (int x = 0; x < 8; x++)
            {
                int d = Mathf.Min(Mathf.Min(x, y), Mathf.Min(7 - x, 7 - y));
                img.SetPixel(x, y, d < 2 ? Border : d == 2 ? rim : fill);
            }
        var sb = new StyleBoxTexture { Texture = ImageTexture.CreateFromImage(img) };
        sb.SetTextureMarginAll(3);
        sb.SetContentMarginAll(margin);
        return sb;
    }

    /// <summary>A pixel panel box for cards, selection frames and map insets. Same 9-slice as the theme's panels.</summary>
    public static StyleBox PanelBox(Color fill, Color rim, float margin = 8) => Box(fill, rim, margin);

    static StyleBoxFlat Flat(Color c, float margin = 0)
    {
        var f = new StyleBoxFlat { BgColor = c };
        f.SetContentMarginAll(margin);
        return f;
    }

    public static Theme Build()
    {
        var t = new Theme { DefaultFontSize = 16 };
        if (ResourceLoader.Exists(FontPath) && ResourceLoader.Load<Font>(FontPath) is FontFile ff)
        {
            ff.Antialiasing = TextServer.FontAntialiasing.None;
            ff.Hinting = TextServer.Hinting.None;
            ff.SubpixelPositioning = TextServer.SubpixelPositioning.Disabled;
            ff.Oversampling = 1f;
            t.DefaultFont = ff;
        } // else: engine default font (see README TODO)

        t.SetStylebox("panel", "PanelContainer", Box(Fill, Rim, 10));
        t.SetStylebox("panel", "Panel", Box(Fill, Rim, 10));
        t.SetStylebox("panel", "TabContainer", Box(Fill, Rim, 10));
        t.SetStylebox("tab_selected", "TabContainer", Box(new Color("3a3029"), Rim, 8));
        t.SetStylebox("tab_unselected", "TabContainer", Box(new Color("201a17"), Border, 8));
        t.SetStylebox("tab_hovered", "TabContainer", Box(new Color("33291f"), Rim, 8));
        t.SetColor("font_selected_color", "TabContainer", Gold);
        t.SetColor("font_unselected_color", "TabContainer", Body);
        t.SetColor("font_hovered_color", "TabContainer", Gold);

        t.SetStylebox("normal", "Button", Box(new Color("3a3029"), Rim, 8));
        t.SetStylebox("hover", "Button", Box(new Color("4b3d31"), Gold, 8));
        t.SetStylebox("pressed", "Button", Box(new Color("201a17"), Rim, 8));
        t.SetStylebox("focus", "Button", Box(new Color("3a3029"), Gold, 8));
        t.SetStylebox("disabled", "Button", Box(Fill, Border, 8));
        t.SetColor("font_color", "Button", Body);
        t.SetColor("font_hover_color", "Button", Gold);
        t.SetColor("font_pressed_color", "Button", Gold);
        t.SetColor("font_focus_color", "Button", Gold);
        t.SetColor("font_disabled_color", "Button", Dim);

        t.SetStylebox("normal", "CheckBox", new StyleBoxEmpty());
        t.SetColor("font_color", "CheckBox", Body);
        t.SetColor("font_hover_color", "CheckBox", Gold);

        t.SetStylebox("normal", "LineEdit", Box(new Color("1a1418"), Rim, 6));
        t.SetStylebox("focus", "LineEdit", Box(new Color("1a1418"), Gold, 6));
        t.SetColor("font_color", "LineEdit", Body);
        t.SetColor("caret_color", "LineEdit", Gold);

        t.SetStylebox("background", "ProgressBar", Box(new Color("1a1418"), Rim, 4));
        t.SetStylebox("fill", "ProgressBar", Flat(Gold));
        t.SetColor("font_color", "ProgressBar", Body);

        t.SetStylebox("panel", "ItemList", Box(new Color("1a1418"), Rim, 4));
        t.SetStylebox("focus", "ItemList", new StyleBoxEmpty());
        t.SetStylebox("selected", "ItemList", Flat(new Color("4b3d31"), 2));
        t.SetStylebox("selected_focus", "ItemList", Flat(new Color("5a4936"), 2));
        t.SetStylebox("hovered", "ItemList", Flat(new Color("33291f"), 2));
        t.SetColor("font_color", "ItemList", Body);
        t.SetColor("font_selected_color", "ItemList", Gold);
        t.SetColor("font_hovered_color", "ItemList", Gold);
        t.SetConstant("v_separation", "ItemList", 2);

        foreach (var sb in new[] { "VScrollBar", "HScrollBar" })
        {
            t.SetStylebox("scroll", sb, Flat(new Color("1a1418")));
            t.SetStylebox("grabber", sb, Flat(Rim));
            t.SetStylebox("grabber_highlight", sb, Flat(Gold));
            t.SetStylebox("grabber_pressed", sb, Flat(Gold));
        }
        t.SetStylebox("normal", "RichTextLabel", new StyleBoxEmpty());
        t.SetStylebox("panel", "TooltipPanel", Box(new Color("1a1418"), Gold, 6));
        t.SetColor("font_color", "TooltipLabel", Body);

        t.SetColor("font_color", "Label", Body);
        t.SetColor("default_color", "RichTextLabel", Body);

        t.AddType("TitleLabel");
        t.SetTypeVariation("TitleLabel", "Label");
        t.SetColor("font_color", "TitleLabel", Gold);
        t.SetFontSize("font_size", "TitleLabel", 32);
        t.AddType("GoodLabel");
        t.SetTypeVariation("GoodLabel", "Label");
        t.SetColor("font_color", "GoodLabel", new Color("8fc46a"));
        t.AddType("GoldLabel");
        t.SetTypeVariation("GoldLabel", "Label");
        t.SetColor("font_color", "GoldLabel", Gold);
        t.AddType("WarnLabel");
        t.SetTypeVariation("WarnLabel", "Label");
        t.SetColor("font_color", "WarnLabel", Warn);
        t.AddType("HintLabel");
        t.SetTypeVariation("HintLabel", "Label");
        t.SetColor("font_color", "HintLabel", Dim);
        return t;
    }
}
