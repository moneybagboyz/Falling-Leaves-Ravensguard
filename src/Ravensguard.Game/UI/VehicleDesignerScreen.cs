using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Ravensguard.Game.Inputs;
using Ravensguard.Game.World;
using Ravensguard.Sim.Animals;
using Ravensguard.Sim.Carts;
using Ravensguard.Sim.Trade;

namespace Ravensguard.Game.UI;

/// <summary>
/// The vehicle designer (vehicle-designer 4.1, 6; context <c>VehicleDesigner</c>, standalone like the blueprint editor): a top-down grid of 1 m cells at 32 px a cell with a deck switcher, the parts in
/// categories at the right top, the stats pane at the right bottom (mass, cargo, crew, the pull needed and the team that gives it, speeds per surface, turning radius, axle load, a boat's draft, freeboard,
/// GM and speeds) with the warnings in red, and a preview in the chosen facing (eight, the diagonals baked). Every number is the sim's <see cref="VehicleDesigner"/>; saving and building go through the
/// hero's desk (recorded for replay). Laid out for 960 x 540.
/// </summary>
public sealed partial class VehicleDesignerScreen : Control, IHandlesBack
{
    const string Ctx = "vehicledesigner.";
    readonly ScreenRouter _router;
    readonly ArtAtlas _art;
    public VehicleDesigner Model { get; }
    readonly Func<HeroCommand, bool>? _issue;
    readonly Func<(long X, long Y, long Sec)>? _here;
    readonly Dictionary<string, Action> _keys = new();
    int _cat, _part;
    float _zoom = 1f; Vector2 _pan;
    (int X, int Y)? _hover;
    bool _statsOpen = true, _built;
    int _shownRev = -1;
    Label _status = null!, _deck = null!, _category = null!;
    LineEdit _name = null!;
    ItemList _parts = null!;
    RichTextLabel _stats = null!;
    Control _grid = null!, _previewBox = null!;
    VehicleComposite _preview = null!;

    /// <summary>
    /// <paramref name="issue"/> sends a desk command (null outside a running game: Save writes to disk only and Build is refused), <paramref name="here"/> is the hero's position (region mm) and the game second.
    /// </summary>
    public VehicleDesignerScreen(ScreenRouter router, VehicleCatalog cat, AnimalCatalog? breeds, Func<HeroCommand, bool>? issue = null, Func<(long X, long Y, long Sec)>? here = null, string? open = null, int localDepthMm = 0)
    {
        _router = router; _art = ArtAtlas.Shared; _issue = issue; _here = here;
        Model = new VehicleDesigner(cat, breeds) { LocalDepthMm = localDepthMm };
        if (open != null && cat.Find(open) is { } bp) { Model.Load(bp.Def); if (!bp.Player) { Model.Id = VehicleDesigner.IdFor(bp.Def.Name + " copy"); Model.Name = bp.Def.Name + " copy"; } }
        else Model.New(2, 5);
        Name = "VehicleDesigner";
    }

    public override void _Ready() { Build(); ActiveContexts.Enter(InputContext.VehicleDesigner); Refresh(); }
    public override void _ExitTree() => ActiveContexts.Exit(InputContext.VehicleDesigner);

    // ------------------------------------------------------------------ layout

    Button Btn(string text, string action, Action press)
    {
        string id = Ctx + action;
        var b = new Button { Text = text + "  " + InputBootstrap.Hint(id), FocusMode = FocusModeEnum.None, TooltipText = text + " " + InputBootstrap.Hint(id) };
        b.Pressed += () => { press(); Refresh(); };
        _keys[id] = press;
        return b;
    }

    void Key(string action, Action press) => _keys[Ctx + action] = press;

    void Build()
    {
        if (_built) return; _built = true;
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        var bg = new ColorRect { Color = PixelTheme.Backdrop, MouseFilter = MouseFilterEnum.Ignore }; bg.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect); AddChild(bg);
        var root = new VBoxContainer(); root.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect, LayoutPresetMode.Minsize, 6); root.AddThemeConstantOverride("separation", 4); AddChild(root);
        var top = new HFlowContainer(); top.AddThemeConstantOverride("h_separation", 4); root.AddChild(top);
        top.AddChild(new Label { Text = L.T("vd_title"), ThemeTypeVariation = "GoldLabel" });
        _name = new LineEdit { Text = Model.Name, CustomMinimumSize = new Vector2(150, 0), PlaceholderText = L.T("vd_name") };
        _name.TextChanged += t => { Model.Name = t; Model.Id = VehicleDesigner.IdFor(t); };
        top.AddChild(_name);
        top.AddChild(Btn(L.T("vd_new"), "new", () => { Model.New(2, 5); _name.Text = Model.Name; }));
        top.AddChild(Btn(L.T("vd_open"), "open", OpenList));
        top.AddChild(Btn(L.T("vd_save"), "save", Save));
        top.AddChild(Btn(L.T("vd_build"), "build", BuildThis));
        top.AddChild(Btn(L.T("vd_close"), "close", Close));

        var body = new HBoxContainer { SizeFlagsVertical = SizeFlags.ExpandFill }; body.AddThemeConstantOverride("separation", 6); root.AddChild(body);
        var left = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill }; body.AddChild(left);
        var tools = new HFlowContainer(); tools.AddThemeConstantOverride("h_separation", 4); left.AddChild(tools);
        _deck = new Label { ThemeTypeVariation = "HintLabel" }; tools.AddChild(_deck);
        tools.AddChild(Btn(L.T("vd_deck_up"), "deck_up", () => Model.DeckUp()));
        tools.AddChild(Btn(L.T("vd_deck_down"), "deck_down", () => Model.DeckDown()));
        tools.AddChild(Btn(L.T("vd_rotate"), "rotate", TurnEighth));
        tools.AddChild(Btn(L.T("vd_mirror"), "mirror", () => Model.Mirror()));
        tools.AddChild(Btn(L.T("vd_undo"), "undo", () => Model.Undo()));
        tools.AddChild(Btn(L.T("vd_redo"), "redo", () => Model.Redo()));
        var size = new HBoxContainer(); tools.AddChild(size);
        foreach (var (lbl, dw, dl) in new[] { ("W-", -1, 0), ("W+", 1, 0), ("L-", 0, -1), ("L+", 0, 1) })
        {
            var b = new Button { Text = lbl, FocusMode = FocusModeEnum.None, TooltipText = L.T("vd_resize") };
            b.Pressed += () => { Model.Resize(Model.Width + dw, Model.Length + dl); Refresh(); };
            size.AddChild(b);
        }
        _grid = new GridView(this) { SizeFlagsVertical = SizeFlags.ExpandFill, SizeFlagsHorizontal = SizeFlags.ExpandFill, ClipContents = true, FocusMode = FocusModeEnum.Click };
        left.AddChild(_grid);

        var right = new VBoxContainer { CustomMinimumSize = new Vector2(330, 0) }; right.AddThemeConstantOverride("separation", 3); body.AddChild(right);
        var catRow = new HBoxContainer(); right.AddChild(catRow);
        catRow.AddChild(Btn("<", "category_prev", () => Cat(-1)));
        _category = new Label { ThemeTypeVariation = "GoldLabel", SizeFlagsHorizontal = SizeFlags.ExpandFill, HorizontalAlignment = HorizontalAlignment.Center }; catRow.AddChild(_category);
        catRow.AddChild(Btn(">", "category_next", () => Cat(1)));
        _parts = new ItemList { CustomMinimumSize = new Vector2(0, 130), FocusMode = FocusModeEnum.None, SelectMode = ItemList.SelectModeEnum.Single };
        _parts.ItemSelected += i => { _part = (int)i; Refresh(); };
        right.AddChild(_parts);
        var mid = new HBoxContainer(); right.AddChild(mid);
        _previewBox = new Control { CustomMinimumSize = new Vector2(120, 96), ClipContents = true }; mid.AddChild(_previewBox);
        _preview = new VehicleComposite(_art) { Scale = new Vector2(0.4f, 0.4f) }; _previewBox.AddChild(_preview);
        mid.AddChild(new Label { Text = L.F("vd_preview_hint", InputBootstrap.Hint(Ctx + "rotate")), ThemeTypeVariation = "HintLabel", AutowrapMode = TextServer.AutowrapMode.WordSmart, SizeFlagsHorizontal = SizeFlags.ExpandFill });
        right.AddChild(Btn(L.T("vd_stats_pane"), "stats_pane", () => { _statsOpen = !_statsOpen; }));
        _stats = new RichTextLabel { BbcodeEnabled = true, SizeFlagsVertical = SizeFlags.ExpandFill, ScrollActive = true, FitContent = false };
        _stats.AddThemeFontSizeOverride("normal_font_size", 13);
        right.AddChild(_stats);
        _status = new Label { ThemeTypeVariation = "HintLabel", ClipText = true }; root.AddChild(_status);
        Key("part_prev", () => Part(-1)); Key("part_next", () => Part(1));
        Key("zoom_in", () => _zoom = Math.Min(3f, _zoom + 0.25f)); Key("zoom_out", () => _zoom = Math.Max(0.5f, _zoom - 0.25f));
        Key("pan_up", () => _pan.Y += 32); Key("pan_down", () => _pan.Y -= 32); Key("pan_left", () => _pan.X += 32); Key("pan_right", () => _pan.X -= 32);
    }

    string CatId => VehicleDesigner.Categories[_cat];
    IReadOnlyList<Ravensguard.Content.VehiclePartDef> CatParts => Model.PartsOf(CatId);
    public string? SelectedPart => CatParts.Count == 0 ? null : CatParts[Math.Clamp(_part, 0, CatParts.Count - 1)].Id;
    void Cat(int d) { _cat = (_cat + d + VehicleDesigner.Categories.Length) % VehicleDesigner.Categories.Length; _part = 0; }
    void Part(int d) { int n = Math.Max(1, CatParts.Count); _part = (_part + d + n) % n; }

    /// <summary>Selects a part by id (its category too): the eyedropper, tests and shots.</summary>
    public void Select(string partId)
    {
        for (int c = 0; c < VehicleDesigner.Categories.Length; c++)
        {
            var ps = Model.PartsOf(VehicleDesigner.Categories[c]);
            for (int i = 0; i < ps.Count; i++) if (ps[i].Id == partId) { _cat = c; _part = i; Refresh(); return; }
        }
    }

    // ------------------------------------------------------------------ refresh

    public void Refresh()
    {
        if (!_built) return;
        _category.Text = L.T("vd_cat_" + CatId);
        _parts.Clear();
        foreach (var p in CatParts)
        {
            int i = _parts.AddItem($"{p.Name}  {p.MassKg} kg" + (p.CargoKg > 0 ? $", {p.CargoKg} kg" : ""));
            string id = $"vpart.{p.Id}.e";
            if (_art.Has(id)) _parts.SetItemIcon(i, _art.Texture(id));
        }
        if (_parts.ItemCount > 0) _parts.Select(Math.Clamp(_part, 0, _parts.ItemCount - 1));
        _deck.Text = L.F("vd_deck", Model.Deck, Model.Decks, Model.Width, Model.Length);
        var st = Model.Stats();
        _preview.Set(Model.Catalog, PartGrid.Expand(Model.ToDef(), Model.Catalog), Model.Width, Model.Length, Model.Length / 2f, Model.Revision);
        _preview.Dir8 = Model.Facing * 2 + (_diag ? 1 : 0);
        _preview.Position = _previewBox.Size / 2 + new Vector2(0, 0);
        _stats.Text = _statsOpen ? StatsText(st) : L.T("vd_stats_hidden"); _shownRev = Model.Revision;
        _status.Text = L.F("vd_status", SelectedPart is { } sp && Model.Catalog.Parts.TryGetValue(sp, out var pd) ? pd.Name : "", InputBootstrap.Hint(Ctx + "place"), InputBootstrap.Hint(Ctx + "remove"));
        _grid.QueueRedraw();
    }

    bool _diag;
    /// <summary>The preview's facing also steps through the diagonals: T turns it an eighth (four drawn views, four baked).</summary>
    public void TurnEighth() { if (_diag) { _diag = false; Model.Rotate(); } else _diag = true; Refresh(); }

    string StatsText(DesignerStats d)
    {
        var s = d.Stats; var sb = new System.Text.StringBuilder();
        sb.Append(L.F("vd_st_mass", s.MassKg, s.CargoKg, s.CargoL)).Append('\n');
        sb.Append(L.F("vd_st_crew", s.Driver, s.Passengers, s.Seats, s.Rowers)).Append('\n');
        if (!s.Boat)
        {
            sb.Append(L.F("vd_st_pull", d.PullRoadN, d.AnimalsRoad, d.PullMudN, d.AnimalsMud, Beast(d.Beast, Math.Max(d.AnimalsRoad, d.AnimalsMud)))).Append('\n');
            sb.Append(L.F("vd_st_radius", (s.TurningRadiusDm / 10.0).ToString("0.0"), s.AxleLimitKg, d.AxleLoadPm / 10)).Append('\n');
            if (d.Speeds.Count > 0) sb.Append(L.F("vd_st_speeds", d.TeamAnimals, Beast(d.Beast, d.TeamAnimals), string.Join(", ", d.Speeds.Select(x => $"{L.T("SURFACE_" + x.Surface.ToString().ToUpperInvariant())} {x.SpeedCmps / 100.0:0.0}")))).Append('\n');
        }
        else
        {
            sb.Append(L.F("vd_st_draft", d.Empty.DraftMm, d.Full.DraftMm, d.Full.FreeboardMm, d.Full.GmMm, L.T(d.Full.GmMm <= 0 ? "vd_gm_capsize" : d.Full.GmMm < Model.Catalog.Water.TenderGmMm ? "vd_gm_tender" : "vd_gm_steady"))).Append('\n');
            sb.Append(L.F("vd_st_boat_speeds", d.RowMmps / 1000.0, d.PoleMmps / 1000.0, d.TowMmps / 1000.0, d.DownstreamMmps / 1000.0)).Append('\n');
            sb.Append(d.DeepestRiverWidthClass >= 0 ? L.F("vd_st_river", L.T("vd_river_" + Model.Catalog.Water.Classes[d.DeepestRiverWidthClass].Id)) : L.T("vd_st_river_none")).Append('\n');
        }
        foreach (var i in d.Issues)
            sb.Append(i.IsError ? "[color=#e8735a]" : "[color=#e0b050]").Append(L.F("vd_issue_" + i.Code, i.Arg, i.X, i.Y)).Append(i.X >= 0 ? $" ({i.X},{i.Y})" : "").Append("[/color]\n");
        if (d.CanBuild) sb.Append("[color=#8fcf7a]").Append(L.T("vd_st_ok")).Append("[/color]");
        return sb.ToString();
    }

    static string Beast(string id, int n) => L.T(n == 1 ? "vd_beast_" + (id.Length == 0 ? "none" : id) : "vd_beasts_" + (id.Length == 0 ? "none" : id));

    // ------------------------------------------------------------------ actions

    void OpenList()
    {
        var verbs = new List<QMenu.Verb>();
        foreach (var b in Model.Catalog.All)
        {
            var bp = b;
            verbs.Add(new(bp.Player ? L.F("vd_open_player", bp.Def.Name) : bp.Def.Name, () => { Model.Load(bp.Def); if (!bp.Player) { Model.Name = bp.Def.Name + " copy"; Model.Id = VehicleDesigner.IdFor(Model.Name); } _name.Text = Model.Name; Refresh(); }));
        }
        _router.Push(new QMenu(_router, L.T("vd_open"), verbs));
    }

    /// <summary>Ctrl+S: the design into the save's designs through the desk (then it can be built) and onto disk in the player's mod (<c>user://blueprints/player/vehicle.&lt;id&gt;.jsonc</c>).</summary>
    public void Save()
    {
        if (Model.Name.Trim().Length == 0) { _router.ShowToast(L.T("vd_need_name")); return; }
        Model.Id = VehicleDesigner.IdFor(Model.Name);
        string text = Model.Text();
        try { PlayerDesigns.Write("vehicle." + Model.Id, text); } catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException) { GD.PushWarning("[vd] " + ex.Message); }
        bool sent = _here != null && _issue != null && _issue(VehicleDesk.SaveDesign(Model.Id, text, _here().Sec));
        _router.ShowToast(L.F(sent ? "vd_saved" : "vd_saved_disk", Model.Name));
    }

    /// <summary>Enter: "build this" at the hero's yard or slipway (refused while an error stands; the design is saved first).</summary>
    public void BuildThis()
    {
        var st = Model.Stats();
        if (!st.CanBuild) { _router.ShowToast(L.T("vd_cannot_build"), 3f); return; }
        if (_issue == null || _here == null) { _router.ShowToast(L.T("vd_no_world")); return; }
        Save();
        var (x, y, sec) = _here();
        long ahead = (Model.Length + 2) * 1000L;
        _issue(VehicleDesk.Build(Model.Id, x + ahead, y, sec));
        _router.ShowToast(L.F("vd_build_ordered", Model.Name), 3f);
    }

    void Close() => _router.Pop();

    // ------------------------------------------------------------------ input

    public override void _UnhandledInput(InputEvent e)
    {
        if (!Visible || e is InputEventMouseMotion || _router.HasModal || _router.Top != this) return;      // modal guard: a dialog over the designer takes the keys
        if (Actions.Pressed(e, Ctx + "rotate")) { TurnEighth(); GetViewport().SetInputAsHandled(); return; }
        foreach (var (id, act) in _keys)
            if (e is InputEventKey && Actions.Pressed(e, id)) { act(); Refresh(); GetViewport().SetInputAsHandled(); return; }
    }

    /// <summary>Runs an action by id (smoke and shots).</summary>
    public bool Invoke(string action) { if (action == "rotate") { TurnEighth(); return true; } if (!_keys.TryGetValue(Ctx + action, out var a)) return false; a(); Refresh(); return true; }

    // ------------------------------------------------------------------ the grid

    /// <summary>A cell's size on screen: the whole grid fits the box at zoom 1 (at most 48 px a cell), the wheel and = / - zoom from there.</summary>
    float CellPx => Math.Max(8f, Math.Min(48f, Math.Min((_grid.Size.Y - 28) / Math.Max(1, Model.Length), (_grid.Size.X - 16) / Math.Max(1, Model.Width)))) * _zoom;
    Vector2 Origin(Vector2 size) => new Vector2((size.X - Model.Width * CellPx) / 2f, (size.Y - Model.Length * CellPx) / 2f) + _pan;

    /// <summary>The grid: rows from the rear at the bottom to the front at the top (the vehicle faces up the screen), each cell's stack drawn ground up, the lower deck faint under an upper one.</summary>
    sealed partial class GridView : Control
    {
        readonly VehicleDesignerScreen _s;
        public GridView(VehicleDesignerScreen s) { _s = s; MouseFilter = MouseFilterEnum.Stop; }

        (int X, int Y) CellAt(Vector2 p)
        {
            var o = _s.Origin(Size); float c = _s.CellPx;
            int x = (int)Math.Floor((p.X - o.X) / c), row = (int)Math.Floor((p.Y - o.Y) / c);
            return (x, _s.Model.Length - 1 - row);
        }

        public override void _GuiInput(InputEvent e)
        {
            if (_s._router.HasModal) return;
            if (e is InputEventMouseMotion mm) { var c = CellAt(mm.Position); _s._hover = c; QueueRedraw(); return; }
            if (e is not InputEventMouseButton mb || !mb.Pressed) return;
            if (Actions.Pressed(e, Ctx + "zoom_in")) { _s._zoom = Math.Min(3f, _s._zoom + 0.25f); QueueRedraw(); AcceptEvent(); return; }
            if (Actions.Pressed(e, Ctx + "zoom_out")) { _s._zoom = Math.Max(0.5f, _s._zoom - 0.25f); QueueRedraw(); AcceptEvent(); return; }
            var cell = CellAt(mb.Position);
            if (Actions.Pressed(e, Ctx + "place") && _s.SelectedPart is { } part)
            {
                var r = _s.Model.Place(cell.X, cell.Y, part);
                if (r != PlaceResult.Ok) _s._router.ShowToast(L.T("vd_place_" + r.ToString().ToLowerInvariant()), 1.5f);
                _s.Refresh(); AcceptEvent();
            }
            else if (Actions.Pressed(e, Ctx + "remove")) { _s.Model.RemoveTop(cell.X, cell.Y); _s.Refresh(); AcceptEvent(); }
        }

        public override void _Draw()
        {
            var m = _s.Model; var o = _s.Origin(Size); float c = _s.CellPx; var art = _s._art;
            DrawRect(new Rect2(Vector2.Zero, Size), new Color(0.09f, 0.08f, 0.07f));
            for (int y = 0; y < m.Length; y++)
                for (int x = 0; x < m.Width; x++)
                {
                    var r = new Rect2(o + new Vector2(x * c, (m.Length - 1 - y) * c), new Vector2(c, c));
                    DrawRect(r, (x + y) % 2 == 0 ? new Color(0.2f, 0.18f, 0.15f) : new Color(0.17f, 0.15f, 0.13f));
                    void Stack(int deck, Color mod)
                    {
                        foreach (var id in m.Stack(x, y, deck))
                        {
                            string sid = $"vpart.{id}.n";
                            if (!art.Has(sid)) continue;
                            var g = art.Get(sid);
                            bool flip = m.Width > 1 && x >= m.Width / 2 && m.Catalog.Parts.TryGetValue(id, out var p) && (p.Tags.Contains("wheel") || p.Tags.Contains("tyre") || p.Tags.Contains("brake"));
                            if (flip) DrawSetTransform(r.Position + new Vector2(c, 0), 0, new Vector2(-1, 1));          // the right-hand wheel: mirrored across
                            DrawTextureRectRegion(art.Atlas(g.Atlas), flip ? new Rect2(Vector2.Zero, r.Size) : r, new Rect2(g.X, g.Y, g.W, g.H), mod);
                            if (flip) DrawSetTransform(Vector2.Zero, 0, Vector2.One);
                        }
                    }
                    if (m.Deck > 0) Stack(m.Deck - 1, new Color(1, 1, 1, 0.35f));
                    Stack(m.Deck, Colors.White);
                    DrawRect(r, new Color(0.35f, 0.3f, 0.25f), false, 1f);
                }
            // the front, and the hovered cell with the part that would go on it
            DrawString(ThemeDB.FallbackFont, o + new Vector2(0, -6), L.T("vd_front"), HorizontalAlignment.Left, -1, 12, new Color(0.85f, 0.75f, 0.5f));
            foreach (var i in m.Stats().Issues.Where(i => i.X >= 0 && i.Y >= 0 && i.Deck == m.Deck))
                DrawRect(new Rect2(o + new Vector2(i.X * c, (m.Length - 1 - i.Y) * c), new Vector2(c, c)), i.IsError ? new Color(0.9f, 0.3f, 0.25f) : new Color(0.9f, 0.7f, 0.3f), false, 2f);
            if (_s._hover is { } h && h.X >= 0 && h.Y >= 0 && h.X < m.Width && h.Y < m.Length)
                DrawRect(new Rect2(o + new Vector2(h.X * c, (m.Length - 1 - h.Y) * c), new Vector2(c, c)), new Color(1f, 0.9f, 0.5f), false, 2f);
        }
    }

    // ------------------------------------------------------------------ dev

    /// <summary>Places a whole stack at a cell (shots and smoke).</summary>
    public void DebugStack(int x, int y, params string[] parts) { Model.PlaceStack(x, y, parts); Refresh(); }
    public void DebugLoad(string id) { if (Model.Catalog.Find(id) is { } bp) { Model.Load(bp.Def); _name.Text = Model.Name; Refresh(); } }
    public void DebugName(string name) { Model.Name = name; Model.Id = VehicleDesigner.IdFor(name); _name.Text = name; Refresh(); }
}
