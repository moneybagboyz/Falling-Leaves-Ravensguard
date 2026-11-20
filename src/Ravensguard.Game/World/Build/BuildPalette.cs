using System;
using System.Collections.Generic;
using Godot;
using Ravensguard.Game.Inputs;
using Ravensguard.Game.UI;
using Ravensguard.Game.UI.NewGame;

namespace Ravensguard.Game.World.Build;

/// <summary>
/// P5-C: build mode's palette (build-mode-and-construction 6), a strip along the bottom of the street at 960 x 540 and up: the category and its pieces with their material (<c>[</c> <c>]</c>,
/// <c>,</c> <c>.</c> or a click), the tool, the level and the turn, the brush's cost, worker-hours and materials, every refusal reason before placing, the trees building would clear, and
/// buttons for the tools with their keys. A view: it shows what the street view hands it and calls back; no state of the game.
/// </summary>
public partial class BuildPalette : PanelContainer
{
    public sealed class Acts
    {
        public Action<int>? Category;
        public Action<int>? Piece;
        public Action? Library, Roof, TakeDown, SaveAs, Site, Undo, Close, Commit;
    }

    const int Font = 12;
    readonly BuildCatalog _cat;
    readonly Acts _act;
    Label _title = null!, _catName = null!, _tool = null!;
    VBoxContainer _pieces = null!, _info = null!;
    Button _commit = null!;
    BuildCategory? _shownCat;
    int _shownPiece = -1;

    public BuildPalette(BuildCatalog cat, Acts act) { _cat = cat; _act = act; }

    static T Small<T>(T c) where T : Control { c.AddThemeFontSizeOverride("font_size", Font); return c; }
    static Label Lbl(string t = "", bool wrap = false) => Small(new Label { Text = t, AutowrapMode = wrap ? TextServer.AutowrapMode.WordSmart : TextServer.AutowrapMode.Off, ClipText = !wrap, MouseFilter = MouseFilterEnum.Ignore });

    Button Btn(string text, string action, Action? press, int w = 150)
    {
        var b = Small(new Button { Text = text + "  " + InputBootstrap.Hint(action), CustomMinimumSize = new Vector2(w, 22), Alignment = HorizontalAlignment.Left, ClipText = true, FocusMode = FocusModeEnum.None });
        b.Pressed += () => press?.Invoke();
        return b;
    }

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Stop;
        AnchorTop = 1; AnchorBottom = 1; AnchorLeft = 0; AnchorRight = 0;
        OffsetLeft = 12; OffsetRight = 12 + 640; OffsetTop = -214; OffsetBottom = -8;
        GrowVertical = GrowDirection.Begin;
        var row = new HBoxContainer(); row.AddThemeConstantOverride("separation", 10); AddChild(row);

        // the category and its pieces
        var a = new VBoxContainer { CustomMinimumSize = new Vector2(210, 0) }; a.AddThemeConstantOverride("separation", 2); row.AddChild(a);
        var cr = new HBoxContainer(); cr.AddThemeConstantOverride("separation", 4); a.AddChild(cr);
        var prev = Small(new Button { Text = "<", CustomMinimumSize = new Vector2(24, 22), FocusMode = FocusModeEnum.None, TooltipText = InputBootstrap.Hint("build.category_prev") }); prev.Pressed += () => _act.Category?.Invoke(-1);
        var next = Small(new Button { Text = ">", CustomMinimumSize = new Vector2(24, 22), FocusMode = FocusModeEnum.None, TooltipText = InputBootstrap.Hint("build.category_next") }); next.Pressed += () => _act.Category?.Invoke(1);
        _catName = Lbl(); _catName.HorizontalAlignment = HorizontalAlignment.Center; _catName.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        cr.AddChild(prev); cr.AddChild(_catName); cr.AddChild(next);
        _pieces = new VBoxContainer(); _pieces.AddThemeConstantOverride("separation", 0);
        var sc = UiKit.Scroll(_pieces); sc.CustomMinimumSize = new Vector2(210, 160); sc.SizeFlagsVertical = SizeFlags.ExpandFill;
        a.AddChild(sc);

        // the tool, its cost and refusals
        var b = new VBoxContainer { CustomMinimumSize = new Vector2(250, 0), SizeFlagsHorizontal = SizeFlags.ExpandFill }; b.AddThemeConstantOverride("separation", 2); row.AddChild(b);
        _title = Lbl(); _title.AddThemeColorOverride("font_color", PixelTheme.Gold); b.AddChild(_title);
        _tool = Lbl(); b.AddChild(_tool);
        _info = new VBoxContainer(); _info.AddThemeConstantOverride("separation", 1);
        var isc = UiKit.Scroll(_info); isc.CustomMinimumSize = new Vector2(250, 128); isc.SizeFlagsVertical = SizeFlags.ExpandFill; b.AddChild(isc);
        string keys = L.F("BUILD_KEYS_HINT", InputBootstrap.Hint("build.rotate_ccw"), InputBootstrap.Hint("build.rotate_cw"), InputBootstrap.Hint("world.level_up"), InputBootstrap.Hint("world.level_down"));
        var kl = Lbl(keys); kl.TooltipText = keys; kl.MouseFilter = MouseFilterEnum.Stop; kl.AddThemeColorOverride("font_color", PixelTheme.Dim); b.AddChild(kl);

        // the tools
        var c = new VBoxContainer { CustomMinimumSize = new Vector2(150, 0) }; c.AddThemeConstantOverride("separation", 2); row.AddChild(c);
        c.AddChild(Btn(L.T("BUILD_BTN_PLANS"), "build.plan_library", () => _act.Library?.Invoke()));
        _commit = Btn(L.T("BUILD_BTN_COMMIT"), "build.commit", () => _act.Commit?.Invoke()); c.AddChild(_commit);
        c.AddChild(Btn(L.T("BUILD_BTN_ROOF"), "build.roof_over", () => _act.Roof?.Invoke()));
        c.AddChild(Btn(L.T("BUILD_BTN_TAKEDOWN"), "build.deconstruct", () => _act.TakeDown?.Invoke()));
        c.AddChild(Btn(L.T("BUILD_BTN_SAVEAS"), "build.save_blueprint", () => _act.SaveAs?.Invoke()));
        c.AddChild(Btn(L.T("BUILD_BTN_SITE"), "build.site_panel", () => _act.Site?.Invoke()));
        c.AddChild(Btn(L.T("BUILD_BTN_UNDO"), "build.undo", () => _act.Undo?.Invoke()));
        c.AddChild(Btn(L.T("BUILD_BTN_CLOSE"), "build.close", () => _act.Close?.Invoke()));
    }

    /// <summary>Shows the palette's state: the category and the piece picked, the tool line, the level and turn, the preview's lines (tone 0 plain, 1 good, 2 refused, 3 a note).</summary>
    public void Show(BuildCategory cat, int piece, string tool, int z, byte rot, IReadOnlyList<(string Text, int Tone)> lines, bool plan, bool handWork)
    {
        if (_title == null) return;
        _title.Text = L.F("BUILD_TITLE", z >= 0 ? "+" + z : z.ToString(), L.T("BUILD_FACING_" + (rot & 3))) + (handWork ? "  " + L.T("BUILD_HAND_TAG") : "");
        _tool.Text = tool; _tool.TooltipText = tool;
        _commit.Disabled = !plan;
        if (_shownCat != cat || _shownPiece != piece)
        {
            _shownCat = cat; _shownPiece = piece;
            _catName.Text = BuildCatalog.CategoryName(cat);
            foreach (var ch in _pieces.GetChildren()) { _pieces.RemoveChild(ch); ch.QueueFree(); }
            var list = _cat.In(cat);
            for (int i = 0; i < list.Count; i++)
            {
                int k = i; var p = list[i];
                var b = Small(new Button { Text = L.F("BUILD_PIECE_ROW", p.Name, p.MaterialName), ToggleMode = true, ButtonPressed = i == piece, Flat = i != piece, Alignment = HorizontalAlignment.Left, ClipText = true,
                    CustomMinimumSize = new Vector2(196, 20), FocusMode = FocusModeEnum.None, TooltipText = BuildCatalog.Describe(_cat.Cost([p.Id])) });
                b.Pressed += () => _act.Piece?.Invoke(k);
                _pieces.AddChild(b);
            }
            if (list.Count == 0) _pieces.AddChild(Lbl(L.T("BUILD_NO_PIECES")));
        }
        foreach (var ch in _info.GetChildren()) { _info.RemoveChild(ch); ch.QueueFree(); }
        foreach (var (text, tone) in lines)
        {
            var l = Lbl(text, wrap: true);
            l.CustomMinimumSize = new Vector2(240, 0);
            if (tone == 1) l.AddThemeColorOverride("font_color", new Color(0.55f, 0.95f, 0.55f));
            else if (tone == 2) l.AddThemeColorOverride("font_color", new Color(1f, 0.45f, 0.38f));
            else if (tone == 3) l.AddThemeColorOverride("font_color", new Color(1f, 0.78f, 0.35f));
            _info.AddChild(l);
        }
    }
}
