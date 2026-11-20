using System;
using System.Linq;
using Godot;
using Ravensguard.Game.UI.NewGame;
using Ravensguard.Sim.Society;

namespace Ravensguard.Game.UI;

/// <summary>
/// The Standing tab of the character screen (P6-A3, reputation 6): what each group thinks of the hero, as a value, a band (Hated, Distrusted, Unknown, Known, Respected, Honoured) and the three deeds
/// that weigh most (<c>why</c>), then renown and the marks he carries. Realms and fiefs come first (the Faction screen reads the same lines), then the settlements. A view over the barrier's
/// <see cref="StandingView"/>, refreshed while it is open; Back closes it. Readable at 960 x 540: one scrolling column, one row of text per deed.
/// </summary>
public partial class StandingScreen : Control
{
    readonly ScreenRouter _router;
    readonly Func<StandingView?> _data;
    readonly Action _refresh;
    VBoxContainer _body = null!;
    StandingView? _shown;
    double _acc;
    int _shotWait;
    /// <summary>Dev: when set, the screen saves its own picture to this path and quits.</summary>
    public string? ShotPath { get; init; }

    public StandingScreen(ScreenRouter router, Func<StandingView?> data, Action refresh) { _router = router; _data = data; _refresh = refresh; }

    public override void _Ready()
    {
        var margin = UiKit.Margin(this, 14);
        var root = new VBoxContainer(); root.AddThemeConstantOverride("separation", 6); margin.AddChild(root);
        root.AddChild(UiKit.Title(L.T("st_title")));
        root.AddChild(UiKit.Hint(L.T("st_intro"), true));
        _body = new VBoxContainer(); _body.AddThemeConstantOverride("separation", 4);
        root.AddChild(UiKit.Panel(UiKit.Scroll(_body), 0, 0));
        var bottom = new HBoxContainer(); bottom.AddThemeConstantOverride("separation", 8);
        var back = UiKit.Key(L.T("UI_BACK"), "menu.back", 200); back.Pressed += _router.Pop; bottom.AddChild(back);
        root.AddChild(bottom);
        _refresh();
        Build(_data());
    }

    public override void _Process(double delta)
    {
        _acc += delta;
        if (_acc >= 1.0) { _acc = 0; _refresh(); var v = _data(); if (!ReferenceEquals(v, _shown)) Build(v); }
        if (ShotPath == null || !Visible || ++_shotWait != 8) return;
        var img = GetViewport().GetTexture().GetImage();
        System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(ShotPath) ?? ".");
        GD.Print($"[standing] screenshot {ShotPath}: {img.SavePng(ShotPath)}");
        GetTree().Quit();
    }

    static string DeedName(string id) => L.T("st_deed_" + id);

    void Build(StandingView? v)
    {
        _shown = v;
        foreach (var c in _body.GetChildren()) c.QueueFree();
        if (v == null) { _body.AddChild(UiKit.Body(L.T("st_loading"))); return; }
        // renown and marks first: who the hero is to strangers
        _body.AddChild(UiKit.Body(L.F("st_renown", v.RenownPm / 10, v.RenownPm % 10), false));
        _body.AddChild(UiKit.Hint(L.T("st_marks")));
        if (v.Marks.Count == 0) _body.AddChild(UiKit.Body(L.T("st_marks_none"), true));
        foreach (var (mark, where) in v.Marks) _body.AddChild(UiKit.Body(where.Length > 0 ? L.F("st_mark_" + mark + "_at", where) : L.T("st_mark_" + mark), true));
        _body.AddChild(UiKit.Hint(L.T("st_groups")));
        if (v.Lines.Count == 0) _body.AddChild(UiKit.Body(L.T("st_none"), true));
        foreach (var line in v.Lines)
        {
            var row = new HBoxContainer(); row.AddThemeConstantOverride("separation", 8);
            string kind = L.T("st_kind_" + line.Group.Kind.ToString().ToLowerInvariant());
            row.AddChild(new Label { Text = line.Name.Length > 0 ? $"{kind}: {line.Name}" : kind, CustomMinimumSize = new Vector2(250, 0), ClipText = true, SizeFlagsHorizontal = SizeFlags.ExpandFill });
            var band = new Label { Text = L.T(Standing.BandKey(line.Band)), CustomMinimumSize = new Vector2(110, 0) };
            if (line.ValuePm >= 150) band.AddThemeColorOverride("font_color", PixelTheme.Gold); else if (line.ValuePm <= -150) band.AddThemeColorOverride("font_color", new Color(0.9f, 0.45f, 0.4f));
            row.AddChild(band);
            row.AddChild(new Label { Text = (line.ValuePm > 0 ? "+" : "") + line.ValuePm, CustomMinimumSize = new Vector2(52, 0), HorizontalAlignment = HorizontalAlignment.Right });
            var bar = new ProgressBar { MinValue = -1000, MaxValue = 1000, Value = line.ValuePm, ShowPercentage = false, CustomMinimumSize = new Vector2(90, 10), SizeFlagsVertical = SizeFlags.ShrinkCenter };
            row.AddChild(bar);
            _body.AddChild(row);
            foreach (var w in line.Why)
                _body.AddChild(UiKit.Hint(L.F("st_why_line", DeedName(w.DeedId), (w.ValuePm > 0 ? "+" : "") + w.ValuePm, w.Day), false));
            if (line.Why.Count == 0) _body.AddChild(UiKit.Hint(L.T("st_why_none"), false));
        }
    }
}
