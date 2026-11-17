using Godot;

namespace Ravensguard.Game.UI;

/// <summary>Builds the pixel UI theme in code: 9-slice pixel panels, no rounded corners, nearest filtering.</summary>
public static class PixelTheme
{
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

        t.SetColor("font_color", "Label", Body);
        t.SetColor("default_color", "RichTextLabel", Body);

        t.AddType("TitleLabel");
        t.SetTypeVariation("TitleLabel", "Label");
        t.SetColor("font_color", "TitleLabel", Gold);
        t.SetFontSize("font_size", "TitleLabel", 32);
        t.AddType("WarnLabel");
        t.SetTypeVariation("WarnLabel", "Label");
        t.SetColor("font_color", "WarnLabel", Warn);
        t.AddType("HintLabel");
        t.SetTypeVariation("HintLabel", "Label");
        t.SetColor("font_color", "HintLabel", Dim);
        return t;
    }
}
