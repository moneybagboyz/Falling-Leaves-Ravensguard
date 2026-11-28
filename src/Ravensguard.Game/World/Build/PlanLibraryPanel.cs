using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Ravensguard.Content;
using Ravensguard.Game.Inputs;
using Ravensguard.Game.UI;
using Ravensguard.Game.UI.NewGame;
using Ravensguard.Sim.Building;
using Ravensguard.Sim.Construction;

namespace Ravensguard.Game.World.Build;

/// <summary>
/// P5-C: the plan library (P in build mode; build-mode-and-construction 4.1, 4.2): world generation's building plans and the player's own (saved in this game, or on disk in the player's
/// mod), filtered by culture (the local one first), wealth and purpose, and "fits this lot" when the cursor is on one. The preview shows the plan as drawn at that culture and wealth,
/// its size and levels, cost, worker-hours and materials, how it fits the lot (or why not: "14 m wide; the lot is 11 m"), and the rooms it will have (RoomDetector v2). Place hands the plan to
/// build mode, which fits it to the lot under the cursor as ghosts to commit. Pure view: the library is computed from the content and the designs' texts.
/// </summary>
public partial class PlanLibraryPanel : Control
{
    readonly ScreenRouter _router;
    readonly ContentDatabase _db;
    readonly BuildCatalog _cat;
    readonly Func<IReadOnlyList<(string Id, string Text)>> _saved;
    readonly IReadOnlyList<(string Id, string Text)> _disk;
    readonly FitLot? _lot;
    readonly Action<string, string, string?, Wealth?, (string Id, string Text)?> _chosen;
    readonly List<CultureDef> _cultures;
    PlanLibrary _lib = null!;
    string? _culture; Wealth? _wealth; PlanPurpose _purpose; bool _fitsOnly;
    ItemList _list = null!;
    VBoxContainer _detail = null!;
    Label _status = null!;
    PlanPicture _picture = null!;
    Button _place = null!;
    List<PlanEntry> _shown = [];
    PlanEntry? _sel;
    int _savedCount = -1;

    public PlanLibraryPanel(ScreenRouter router, BuildCatalog cat, ContentDatabase db, Func<IReadOnlyList<(string Id, string Text)>> saved, IReadOnlyList<(string Id, string Text)> disk, FitLot? lot, string? culture,
        Action<string, string, string?, Wealth?, (string Id, string Text)?> chosen)
    {
        _router = router; _cat = cat; _db = db; _saved = saved; _disk = disk; _lot = lot; _culture = culture; _chosen = chosen;
        _cultures = [.. db.All<CultureDef>().OrderBy(c => c.Id, StringComparer.Ordinal)];
        _fitsOnly = false;
    }

    static T Small<T>(T c, int size = 13) where T : Control { c.AddThemeFontSizeOverride("font_size", size); return c; }

    public override void _Ready()
    {
        var margin = UiKit.Margin(this, 14);
        var root = new VBoxContainer(); root.AddThemeConstantOverride("separation", 6); margin.AddChild(root);
        root.AddChild(UiKit.Title(L.T("BUILD_LIB_TITLE")));
        _status = Small(UiKit.Hint("")); root.AddChild(_status);

        // filters
        var f = new HBoxContainer(); f.AddThemeConstantOverride("separation", 8); root.AddChild(f);
        var cul = Small(new OptionButton { CustomMinimumSize = new Vector2(170, 26) });
        cul.AddItem(L.T("BUILD_LIB_ANY_CULTURE"));
        foreach (var c in _cultures) cul.AddItem(c.Name);
        cul.Selected = _culture == null ? 0 : _cultures.FindIndex(c => c.Id == _culture) + 1;
        cul.ItemSelected += i => { _culture = i == 0 ? null : _cultures[(int)i - 1].Id; Rebuild(); };
        var wl = Small(new OptionButton { CustomMinimumSize = new Vector2(130, 26) });
        foreach (var k in new[] { "BUILD_LIB_ANY_WEALTH", "BUILD_WEALTH_POOR", "BUILD_WEALTH_MIDDLING", "BUILD_WEALTH_RICH" }) wl.AddItem(L.T(k));
        wl.ItemSelected += i => { _wealth = i == 0 ? null : (Wealth)(i - 1); Rebuild(); };
        var pu = Small(new OptionButton { CustomMinimumSize = new Vector2(150, 26) });
        foreach (var p in Enum.GetValues<PlanPurpose>()) pu.AddItem(L.T("BUILD_PURPOSE_" + p.ToString().ToUpperInvariant()));
        pu.ItemSelected += i => { _purpose = (PlanPurpose)i; Rebuild(); };
        var fits = Small(new CheckBox { Text = L.T("BUILD_LIB_FITS"), Disabled = _lot == null, TooltipText = _lot == null ? L.T("cn_fit_no_lot") : "" });
        fits.Toggled += on => { _fitsOnly = on; Rebuild(); };
        f.AddChild(cul); f.AddChild(wl); f.AddChild(pu); f.AddChild(fits);

        // the list and the preview
        var body = new HBoxContainer { SizeFlagsVertical = SizeFlags.ExpandFill }; body.AddThemeConstantOverride("separation", 10); root.AddChild(body);
        _list = Small(new ItemList { CustomMinimumSize = new Vector2(330, 0), SizeFlagsVertical = SizeFlags.ExpandFill, SelectMode = ItemList.SelectModeEnum.Single });
        _list.ItemSelected += i => Select((int)i);
        _list.ItemActivated += i => { Select((int)i); Place(); };
        body.AddChild(_list);
        var right = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ExpandFill }; right.AddThemeConstantOverride("separation", 4); body.AddChild(right);
        _picture = new PlanPicture { CustomMinimumSize = new Vector2(0, 150), SizeFlagsHorizontal = SizeFlags.ExpandFill };
        right.AddChild(UiKit.Panel(_picture));
        _detail = new VBoxContainer(); _detail.AddThemeConstantOverride("separation", 2);
        var dp = UiKit.Panel(UiKit.Scroll(_detail)); dp.SizeFlagsVertical = SizeFlags.ExpandFill; right.AddChild(dp);

        var bar = new HBoxContainer(); bar.AddThemeConstantOverride("separation", 10); root.AddChild(bar);
        _place = UiKit.Key(L.T("BUILD_LIB_PLACE"), "menu.accept", 260, 32); _place.Pressed += Place; bar.AddChild(_place);
        var back = UiKit.Key(L.T("UI_BACK"), "menu.back", 180, 32); back.Pressed += _router.Pop; bar.AddChild(back);
        Rebuild();
    }

    public override void _Process(double delta)
    {
        if (_saved().Count != _savedCount) Rebuild();             // the save's designs arrived from the barrier
    }

    IEnumerable<(string Id, string Text)> Designs()
    {
        var d = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var (id, t) in _disk) d[id] = t;
        foreach (var (id, t) in _saved()) d[id] = t;                 // the save's copy wins
        return d.Select(kv => (kv.Key, kv.Value));
    }

    void Rebuild()
    {
        _savedCount = _saved().Count;
        _lib = PlanLibrary.For(_db, null, Designs());
        string? keep = _sel?.Id;
        _shown = [.. _lib.Filter(_culture, _wealth, _purpose)];
        if (_fitsOnly && _lot is { } lot) _shown = [.. _shown.Where(p => _lib.Grid(p.Id, _culture, _wealth ?? p.Wealth) is { } g && PlanFitter.Fit(g, lot, _lib.Library.Pieces).Ok)];
        _list.Clear();
        foreach (var p in _shown)
            _list.AddItem(L.F(p.Player ? "BUILD_LIB_ROW_YOURS" : "BUILD_LIB_ROW", p.Name, p.Width, p.Height, p.Levels.Count, L.T("BUILD_WEALTH_" + p.Wealth.ToString().ToUpperInvariant())));
        _status.Text = L.F("BUILD_LIB_STATUS", _shown.Count, _lib.All.Count, _lot is { } l ? L.F("BUILD_LIB_LOT", l.W, l.H) : L.T("BUILD_LIB_NO_LOT"))
            + (_lib.Broken.Count > 0 ? "  " + L.F("BUILD_LIB_BROKEN", _lib.Broken.Count) : "");
        int at = keep == null ? 0 : Math.Max(0, _shown.FindIndex(p => p.Id == keep));
        if (_shown.Count > 0) { _list.Select(at); Select(at); }
        else { _sel = null; Detail(null); }
    }

    void Select(int i)
    {
        _sel = i >= 0 && i < _shown.Count ? _shown[i] : null;
        Detail(_sel);
    }

    void Detail(PlanEntry? p)
    {
        foreach (var c in _detail.GetChildren()) { _detail.RemoveChild(c); c.QueueFree(); }
        _place.Disabled = p == null;
        _picture.Show(null, null);
        if (p == null) { _detail.AddChild(Small(UiKit.Hint(L.T("BUILD_LIB_NONE")))); return; }
        var issues = new List<BlueprintIssue>();
        var g = _lib.Grid(p.Id, _culture, _wealth ?? p.Wealth, issues);
        if (g == null) { _detail.AddChild(Small(UiKit.Body(string.Join("; ", issues.Select(x => x.Code))))); _place.Disabled = true; return; }
        var costs = _cat.Costs;
        var pv = _lib.Preview(p.Id, _lib.GridWithCellar(p.Id, _culture, _wealth ?? p.Wealth) ?? g, costs);       // P8-LM-3: the cost and the rooms include the cellar
        _picture.Show(g, _lib.Library.Pieces);
        Label Line(string t, Color? col = null) { var l = Small(UiKit.Body(t)); if (col is { } c) l.AddThemeColorOverride("font_color", c); _detail.AddChild(l); return l; }
        Line(p.Name + (p.Player ? "  " + L.T("BUILD_LIB_YOURS") : ""), PixelTheme.Gold);
        Line(L.F("BUILD_LIB_SIZE", g.Width, g.Height, string.Join(", ", pv.Levels.Select(z => z == 0 ? L.T("BUILD_LEVEL_GROUND") : "+" + z)), L.T("BUILD_WEALTH_" + (_wealth ?? p.Wealth).ToString().ToUpperInvariant())));
        if (p.Entry.Def.Levels.Any(l => l.Z < 0)) Line(L.T("BUILD_LIB_NO_CELLAR"), PixelTheme.Dim);
        Line(BuildCatalog.Describe(_cat.Cost(PlanLibrary.Cells(g, costs).Select(c => c.Piece)), 6));
        Line(L.F("BUILD_LIB_CREW", BuildCatalog.Hours(pv.MilliHours), Math.Round(pv.MilliHours / 1000.0 / 10.0, 1), Math.Round(pv.MilliHours / 1000.0 / 60.0, 1)), PixelTheme.Dim);
        if (_lot is { } lot)
        {
            var fit = PlanFitter.Fit(g, lot, _lib.Library.Pieces);
            if (fit.Ok) Line(L.F(fit.DoorToStreet ? "BUILD_FIT_STREET" : "BUILD_FIT_OK", lot.W, lot.H), new Color(0.55f, 0.95f, 0.55f));
            else Line(L.F("BUILD_FIT_NO", Ravensguard.Sim.Trade.HeroText.Of(fit.Key, fit.Args).Render()), new Color(1f, 0.45f, 0.38f));
        }
        Line(L.T("BUILD_ROOMS_HEAD"), PixelTheme.Gold);
        if (pv.Rooms.Count == 0) Line(L.T("BUILD_ROOMS_NONE"), PixelTheme.Dim);
        foreach (var r in pv.Rooms) Line(RoomLine(r));
    }

    /// <summary>"Hearth room, 18 m2, indoor, 2 beds, Decent (ground floor)": one room of the room panel.</summary>
    public static string RoomLine(PlannedRoom r)
    {
        string name = r.TypeName.Length > 0 ? r.TypeName : L.T("BUILD_ROOM_PLAIN");
        string where = r.Z == 0 ? L.T("BUILD_LEVEL_GROUND") : "+" + r.Z;
        var bits = new List<string> { L.F("BUILD_ROOM_AREA", r.Area), L.T(r.Indoor ? "BUILD_ROOM_INDOOR" : r.Enclosed ? "BUILD_ROOM_UNROOFED" : "BUILD_ROOM_OPEN") };
        if (r.Beds > 0) bits.Add(L.F("BUILD_ROOM_BEDS", r.Beds));
        if (r.Stations.Count > 0) bits.Add(string.Join(", ", r.Stations.Select(UiKit.Pretty)));
        bits.Add(L.T("BUILD_VALUE_" + r.Value.ToString().ToUpperInvariant()));
        string s = L.F("BUILD_ROOM_LINE", name, string.Join(", ", bits), where);
        if (r.Gaps.Count > 0) s += "  " + L.F("BUILD_ROOM_GAPS", string.Join(", ", r.Gaps));
        return s;
    }

    void Place()
    {
        if (_sel is not { } p) return;
        (string, string)? text = null;
        if (p.Player) { var d = Designs().FirstOrDefault(x => x.Id == p.Id); if (d.Id != null) text = d; }
        _router.Pop();
        _chosen(p.Id, p.Name, _culture, _wealth, text);
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (!Visible || _router.HasModal) return;
        if (Actions.Pressed(e, "menu.accept")) { Place(); GetViewport().SetInputAsHandled(); }
        else if (Actions.Pressed(e, "build.plan_library")) { _router.Pop(); GetViewport().SetInputAsHandled(); }
    }

    /// <summary>The plan's ground floor as a little map: walls dark, floors by material, doors and windows marked, fixtures dotted; the upper levels' outline over it.</summary>
    sealed partial class PlanPicture : Control
    {
        BuildingGrid? _g; PieceCatalog? _p;
        public void Show(BuildingGrid? g, PieceCatalog? p) { _g = g; _p = p; QueueRedraw(); }

        public override void _Draw()
        {
            if (_g?.Level(0) is not { } lv || _p == null) return;
            float cell = Math.Min((Size.X - 8) / _g.Width, (Size.Y - 8) / _g.Height);
            cell = Math.Clamp(MathF.Floor(cell), 2, 14);
            var o = new Vector2((Size.X - cell * _g.Width) / 2, (Size.Y - cell * _g.Height) / 2);
            for (int y = 0; y < lv.Height; y++)
                for (int x = 0; x < lv.Width; x++)
                {
                    var s = lv.Structure[lv.Idx(x, y)];
                    var k = _p.KindOf(s);
                    if (k == null) continue;
                    var col = k switch
                    {
                        PieceKind.Wall => s!.Contains("stone") || s.Contains("brick") ? new Color(0.55f, 0.55f, 0.58f) : s.Contains("wattle") ? new Color(0.62f, 0.5f, 0.32f) : new Color(0.45f, 0.3f, 0.18f),
                        PieceKind.Door => new Color(0.85f, 0.65f, 0.3f), PieceKind.Window => new Color(0.55f, 0.75f, 0.9f),
                        PieceKind.Floor => s!.Contains("plank") ? new Color(0.72f, 0.56f, 0.36f) : s.Contains("flag") ? new Color(0.7f, 0.7f, 0.68f) : new Color(0.6f, 0.5f, 0.38f),
                        PieceKind.StairsUp or PieceKind.StairsDown or PieceKind.Ladder or PieceKind.Trapdoor => new Color(0.95f, 0.9f, 0.5f),
                        _ => new Color(0.5f, 0.5f, 0.5f),
                    };
                    var r = new Rect2(o + new Vector2(x * cell, y * cell), new Vector2(cell, cell));
                    DrawRect(r, col);
                    if (lv.Fixture[lv.Idx(x, y)] != null) DrawCircle(r.GetCenter(), Math.Max(1, cell / 4), new Color(0.2f, 0.15f, 0.1f));
                }
            foreach (var (z, up) in _g.Levels)
            {
                if (z <= 0) continue;
                for (int y = 0; y < up.Height; y++)
                    for (int x = 0; x < up.Width; x++)
                        if (up.Structure[up.Idx(x, y)] != null && _p.KindOf(up.Structure[up.Idx(x, y)]) is PieceKind.Wall)
                            DrawRect(new Rect2(o + new Vector2(x * cell, y * cell), new Vector2(cell, cell)).Grow(-cell / 3), new Color(1, 1, 1, 0.25f));
            }
        }
    }
}
