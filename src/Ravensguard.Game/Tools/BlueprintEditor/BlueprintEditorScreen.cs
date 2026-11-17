using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Godot;
using Ravensguard.Content;
using Ravensguard.Game.Inputs;
using Ravensguard.Game.UI;
using Ravensguard.Sim.Building;
using Ravensguard.Sim.World;

namespace Ravensguard.Game.Tools.BlueprintEditor;

/// <summary>
/// The Phase 1 dev blueprint editor (plan §2.11): palette with the real sprites, level switcher, brush tools, room purposes, fixtures, transforms,
/// undo/redo, live validation with clickable problems, wealth/style preview, Save through <see cref="Ravensguard.Sim.Building.BlueprintWriter"/> with hot reload.
/// Debug builds only: reached from the dev console (<c>bpedit [id]</c>) and the main menu's Dev entry.
/// </summary>
public partial class BlueprintEditorScreen : Control
{
    const string Ctx = "blueprinteditor.";
    static string T(string key) => L.T(key);

    readonly ScreenRouter? _router;
    public EditorModel Model { get; }
    public EditorCanvas Canvas { get; private set; } = null!;

    readonly Dictionary<string, Action> _keys = new();
    readonly Dictionary<string, Button> _pieceButtons = new();
    readonly List<(Button B, Func<bool> On)> _stateButtons = new();

    Label _status = null!, _hoverInfo = null!, _title = null!, _levelLabel = null!, _summary = null!;
    LineEdit _id = null!, _name = null!;
    OptionButton _wealth = null!, _style = null!, _kind = null!, _dwelling = null!, _purpose = null!;
    ItemList _roomList = null!, _problemList = null!, _roomInfoList = null!;
    Button _previewWealthBtn = null!, _previewStyleBtn = null!, _gridBtn = null!, _regionsBtn = null!;
    Control? _dialog;
    bool _syncing, _uiDirty = true;
    int _validatedRevision = -1;
    double _sinceChange;
    int _previewWealthStep, _previewStyleStep;
    bool _built;

    public BlueprintEditorScreen(ScreenRouter? router, EditorModel model)
    {
        _router = router;
        Model = model;
    }

    // ---- launching ----

    /// <summary>Opens the editor (optionally on a blueprint id). Debug builds only. Returns a message for the console.</summary>
    public static string Launch(ScreenRouter? router, string? id)
    {
        if (!OS.IsDebugBuild()) return "the blueprint editor is a debug-build tool";
        if (router == null) return "no screen router";
        if (router.Top is BlueprintEditorScreen) return "the blueprint editor is already open";
        EditorModel model;
        try { model = new EditorModel(EditorLibrary.Load(EditorLibrary.ModFolders(), out _)); }
        catch (Exception ex) { return "could not load content: " + ex.Message; }
        if (!string.IsNullOrEmpty(id) && !model.Open(id, out var err)) return "bpedit: " + err;
        router.Push(new BlueprintEditorScreen(router, model));
        return string.IsNullOrEmpty(id) ? "blueprint editor opened" : $"editing {id}";
    }

    // ---- building the UI ----

    public override void _Ready()
    {
        Build();
        Model.Changed += OnChanged;
        Model.Validate();
        Refresh();
        Callable.From(() => Canvas.Fit()).CallDeferred();
    }

    public override void _ExitTree()
    {
        Model.Changed -= OnChanged;
    }

    void OnChanged() { _uiDirty = true; _sinceChange = 0; }

    Button HintBtn(string text, string action, Action press, bool toggle = false)
    {
        string id = Ctx + action;
        var b = ScreenRouter.HintButton(text, id);
        int hintLen = InputBootstrap.Hint(id).Length;
        b.CustomMinimumSize = new Vector2(Math.Max(56, (int)(text.Length * 8.5f + hintLen * 8.5f) + 30), 28);
        b.TooltipText = text + " " + InputBootstrap.Hint(id);
        b.FocusMode = FocusModeEnum.None;
        b.Pressed += () => press();
        b.ToggleMode = toggle;
        _keys[id] = press;
        return b;
    }

    static Label Hint(string text) => new() { Text = text, ThemeTypeVariation = "HintLabel" };

    void Build()
    {
        if (_built) return;
        _built = true;
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        var root = new VBoxContainer();
        root.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect, LayoutPresetMode.Minsize, 8);
        root.AddThemeConstantOverride("separation", 6);
        AddChild(root);

        // top bar
        var top = new HFlowContainer();
        top.AddThemeConstantOverride("h_separation", 6);
        root.AddChild(top);
        _title = new Label { Text = T("BPEDIT_TITLE"), ThemeTypeVariation = "GoldLabel", CustomMinimumSize = new Vector2(190, 0) };
        top.AddChild(_title);
        top.AddChild(HintBtn(T("BPEDIT_NEW"), "new", AskNew));
        top.AddChild(HintBtn(T("BPEDIT_OPEN"), "open", AskOpen));
        top.AddChild(HintBtn(T("BPEDIT_SAVE"), "save", Save));
        top.AddChild(HintBtn(T("BPEDIT_SAVE_AS"), "save_as", ShowSaveAs));
        top.AddChild(HintBtn(T("BPEDIT_UNDO"), "undo", Model.Undo));
        top.AddChild(HintBtn(T("BPEDIT_REDO"), "redo", Model.Redo));
        top.AddChild(HintBtn(T("BPEDIT_CLOSE"), "close", RequestClose));

        var body = new HBoxContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        body.AddThemeConstantOverride("separation", 8);
        root.AddChild(body);
        body.AddChild(BuildLeft());
        body.AddChild(BuildCenter());
        body.AddChild(BuildRight());

        _status = new Label { Text = "", ThemeTypeVariation = "HintLabel", ClipText = true, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _hoverInfo = new Label { Text = "", ThemeTypeVariation = "HintLabel", CustomMinimumSize = new Vector2(320, 0), HorizontalAlignment = HorizontalAlignment.Right };
        var bar = new HBoxContainer();
        bar.AddChild(_status);
        bar.AddChild(_hoverInfo);
        root.AddChild(bar);
    }

    Control BuildLeft()
    {
        var panel = new PanelContainer { CustomMinimumSize = new Vector2(250, 0) };
        var v = new VBoxContainer();
        v.AddThemeConstantOverride("separation", 4);
        panel.AddChild(v);

        var grid = new GridContainer { Columns = 2 };
        grid.AddThemeConstantOverride("h_separation", 6);
        grid.AddThemeConstantOverride("v_separation", 3);
        v.AddChild(grid);
        _id = new LineEdit { SizeFlagsHorizontal = SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(150, 0) };
        _id.TextSubmitted += _ => { CommitHeader(); _id.ReleaseFocus(); };
        _id.FocusExited += CommitHeader;
        _name = new LineEdit { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _name.TextSubmitted += _ => { CommitHeader(); _name.ReleaseFocus(); };
        _name.FocusExited += CommitHeader;
        _wealth = Options(["poor", "middling", "rich"]);
        _wealth.ItemSelected += _ => RestyleFromHeader();
        _style = new OptionButton { SizeFlagsHorizontal = SizeFlags.ExpandFill, FocusMode = FocusModeEnum.None };
        _style.ItemSelected += _ => RestyleFromHeader();
        _kind = Options(["building", "room_template"]);
        _kind.ItemSelected += i => { if (!_syncing) Model.SetMeta(m => m with { Kind = i == 1 ? BlueprintKind.RoomTemplate : BlueprintKind.Building }); };
        _dwelling = Options(["-", "cottage", "longhouse", "burgage"]);
        _dwelling.ItemSelected += i => { if (!_syncing) Model.SetMeta(m => m with { Dwelling = i == 0 ? null : _dwelling.GetItemText((int)i) }); };
        void Row(string key, Control c) { grid.AddChild(new Label { Text = T(key) }); grid.AddChild(c); }
        Row("BPEDIT_F_ID", _id); Row("BPEDIT_F_NAME", _name); Row("BPEDIT_F_WEALTH", _wealth); Row("BPEDIT_F_STYLE", _style);
        Row("BPEDIT_F_KIND", _kind); Row("BPEDIT_F_DWELLING", _dwelling);

        v.AddChild(new HSeparator());
        v.AddChild(Hint(T("BPEDIT_PALETTE")));
        var cyc = new HBoxContainer();
        cyc.AddThemeConstantOverride("separation", 4);
        cyc.AddChild(HintBtn(T("BPEDIT_PIECE_PREV"), "piece_prev", () => Model.CyclePiece(-1)));
        cyc.AddChild(HintBtn(T("BPEDIT_PIECE_NEXT"), "piece_next", () => Model.CyclePiece(+1)));
        v.AddChild(cyc);
        var scroll = new ScrollContainer { SizeFlagsVertical = SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        v.AddChild(scroll);
        var list = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        list.AddThemeConstantOverride("separation", 3);
        scroll.AddChild(list);
        for (int s = 0; s < EditorModel.SectionKinds.Length; s++)
        {
            var pieces = Model.Section(s).ToList();
            if (pieces.Count == 0) continue;
            list.AddChild(new Label { Text = T(EditorModel.SectionKeys[s]), ThemeTypeVariation = "GoldLabel" });
            var flow = new HFlowContainer();
            flow.AddThemeConstantOverride("h_separation", 3);
            flow.AddThemeConstantOverride("v_separation", 3);
            list.AddChild(flow);
            foreach (var p in pieces) flow.AddChild(PieceButton(p));
        }
        return panel;
    }

    OptionButton Options(string[] items)
    {
        var o = new OptionButton { SizeFlagsHorizontal = SizeFlags.ExpandFill, FocusMode = FocusModeEnum.None };
        foreach (var i in items) o.AddItem(i);
        return o;
    }

    Button PieceButton(BuildPieceDef p)
    {
        var b = new Button { ToggleMode = true, CustomMinimumSize = new Vector2(44, 44), FocusMode = FocusModeEnum.None, TooltipText = $"{p.Name}\n{p.Id} ({p.Kind})" };
        var (terrain, obj) = EditorArt.Icon(Model.Pieces, p.Id);
        foreach (var sprite in new[] { terrain, obj })
        {
            if (EditorArt.Texture(sprite) is not { } tex) continue;
            var tr = new TextureRect { Texture = tex, ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered, MouseFilter = MouseFilterEnum.Ignore, TextureFilter = TextureFilterEnum.Nearest };
            tr.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect, LayoutPresetMode.Minsize, 5);
            b.AddChild(tr);
        }
        if (terrain == null && obj == null) b.Text = p.Glyph;
        string id = p.Id;
        b.Pressed += () => { Model.SetPiece(id); };
        _pieceButtons[id] = b;
        return b;
    }

    Control BuildCenter()
    {
        var v = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        v.AddThemeConstantOverride("separation", 4);

        var tools = new HFlowContainer();
        tools.AddThemeConstantOverride("h_separation", 4);
        tools.AddThemeConstantOverride("v_separation", 4);
        v.AddChild(tools);
        void ToolBtn(string text, string action, EditTool tool) => AddToggle(tools, HintBtn(text, action, () => { Model.Tool = tool; OnChanged(); }, true), () => Model.Tool == tool);
        ToolBtn(T("BPEDIT_TOOL_PAINT"), "tool_paint", EditTool.Paint);
        ToolBtn(T("BPEDIT_TOOL_RECT"), "tool_rect", EditTool.Rect);
        ToolBtn(T("BPEDIT_TOOL_LINE"), "tool_line", EditTool.Line);
        ToolBtn(T("BPEDIT_TOOL_FLOOD"), "tool_flood", EditTool.Flood);
        ToolBtn(T("BPEDIT_TOOL_ERASE"), "tool_erase", EditTool.Erase);
        ToolBtn(T("BPEDIT_TOOL_PICK"), "tool_eyedropper", EditTool.Eyedropper);

        var level = new HFlowContainer();
        level.AddThemeConstantOverride("h_separation", 4);
        level.AddThemeConstantOverride("v_separation", 4);
        v.AddChild(level);
        _levelLabel = new Label { CustomMinimumSize = new Vector2(150, 0), ThemeTypeVariation = "GoldLabel" };
        level.AddChild(_levelLabel);
        level.AddChild(HintBtn(T("BPEDIT_LEVEL_UP"), "level_up", () => Model.SetZ(Model.Z + 1)));
        level.AddChild(HintBtn(T("BPEDIT_LEVEL_DOWN"), "level_down", () => Model.SetZ(Model.Z - 1)));
        level.AddChild(HintBtn(T("BPEDIT_COPY_BELOW"), "copy_below", Model.CopyFromBelow));
        level.AddChild(HintBtn(T("BPEDIT_CLEAR_LEVEL"), "clear_level", Model.ClearLevel));
        var xform = new HFlowContainer();
        xform.AddThemeConstantOverride("h_separation", 4);
        xform.AddThemeConstantOverride("v_separation", 4);
        v.AddChild(xform);
        xform.AddChild(HintBtn(T("BPEDIT_ROTATE"), "rotate", Model.Rotate));
        xform.AddChild(HintBtn(T("BPEDIT_MIRROR_X"), "mirror_x", Model.MirrorX));
        xform.AddChild(HintBtn(T("BPEDIT_MIRROR_Y"), "mirror_y", Model.MirrorY));
        xform.AddChild(HintBtn(T("BPEDIT_RESIZE"), "resize", ShowResize));
        xform.AddChild(HintBtn(T("BPEDIT_FIT"), "fit", () => Canvas.Fit()));

        var view = new HFlowContainer();
        view.AddThemeConstantOverride("h_separation", 4);
        view.AddThemeConstantOverride("v_separation", 4);
        v.AddChild(view);
        _previewWealthBtn = HintBtn(T("BPEDIT_PREVIEW_WEALTH"), "preview_wealth", CyclePreviewWealth);
        _previewStyleBtn = HintBtn(T("BPEDIT_PREVIEW_STYLE"), "preview_style", CyclePreviewStyle);
        view.AddChild(_previewWealthBtn);
        view.AddChild(_previewStyleBtn);
        _regionsBtn = HintBtn(T("BPEDIT_REGIONS"), "toggle_regions", () => { Canvas.ShowRegions = !Canvas.ShowRegions; OnChanged(); Canvas.QueueRedraw(); }, true);
        _gridBtn = HintBtn(T("BPEDIT_GRID"), "toggle_grid", () => { Canvas.ShowGrid = !Canvas.ShowGrid; OnChanged(); Canvas.QueueRedraw(); }, true);
        view.AddChild(_regionsBtn);
        view.AddChild(_gridBtn);

        Canvas = new EditorCanvas(Model);
        Canvas.HoverChanged += OnHover;
        var frame = new PanelContainer { SizeFlagsVertical = SizeFlags.ExpandFill, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        frame.AddChild(Canvas);
        v.AddChild(frame);
        var help = Hint(T("BPEDIT_HELP"));
        help.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        v.AddChild(help);
        return v;
    }

    void AddToggle(Control parent, Button b, Func<bool> on)
    {
        parent.AddChild(b);
        _stateButtons.Add((b, on));
    }

    Control BuildRight()
    {
        var panel = new PanelContainer { CustomMinimumSize = new Vector2(340, 0) };
        var v = new VBoxContainer();
        v.AddThemeConstantOverride("separation", 4);
        panel.AddChild(v);

        var layers = new HFlowContainer();
        layers.AddThemeConstantOverride("h_separation", 4);
        v.AddChild(new Label { Text = T("BPEDIT_LAYER"), ThemeTypeVariation = "GoldLabel" });
        v.AddChild(layers);
                void LayerBtn(string text, string action, EditLayer layer) => AddToggle(layers, HintBtn(text, action, () => { Model.Layer = layer; OnChanged(); }, true), () => Model.Layer == layer);
        LayerBtn(T("BPEDIT_LAYER_STRUCTURE"), "layer_structure", EditLayer.Structure);
        LayerBtn(T("BPEDIT_LAYER_FIXTURES"), "layer_fixtures", EditLayer.Fixtures);
        LayerBtn(T("BPEDIT_LAYER_ROOMS"), "layer_rooms", EditLayer.Rooms);

        v.AddChild(new Label { Text = T("BPEDIT_ROOMS"), ThemeTypeVariation = "GoldLabel" });
        _roomList = new ItemList { CustomMinimumSize = new Vector2(0, 70), SizeFlagsHorizontal = SizeFlags.ExpandFill, FocusMode = FocusModeEnum.None };
        _roomList.ItemSelected += i => { if (!_syncing) { Model.Letter = _roomList.GetItemText((int)i)[0]; Model.Layer = EditLayer.Rooms; OnChanged(); } };
        v.AddChild(_roomList);
        _purpose = new OptionButton { SizeFlagsHorizontal = SizeFlags.ExpandFill, FocusMode = FocusModeEnum.None };
        foreach (var p in Model.Purposes()) _purpose.AddItem(p);
        _purpose.ItemSelected += i => { if (!_syncing && Model.Letter != '\0') Model.SetPurpose(Model.Letter, _purpose.GetItemText((int)i)); };
        v.AddChild(_purpose);
        var rb = new HFlowContainer();
        rb.AddThemeConstantOverride("h_separation", 4);
        rb.AddChild(HintBtn(T("BPEDIT_ROOM_ADD"), "add_room", () => Model.AddRoom(_purpose.GetItemText(Math.Max(0, _purpose.Selected)))));
        rb.AddChild(HintBtn(T("BPEDIT_ROOM_DELETE"), "delete_room", () => Model.DeleteRoom(Model.Letter)));
        v.AddChild(rb);
        v.AddChild(new HSeparator());

        _summary = new Label { Text = "", ThemeTypeVariation = "GoldLabel" };
        v.AddChild(_summary);
        _problemList = new ItemList { SizeFlagsVertical = SizeFlags.ExpandFill, SizeFlagsHorizontal = SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(0, 90), AllowReselect = true, FocusMode = FocusModeEnum.None };
        _problemList.ItemSelected += i => ClickProblem((int)i);
        v.AddChild(_problemList);
        v.AddChild(new Label { Text = T("BPEDIT_DETECTED"), ThemeTypeVariation = "GoldLabel" });
        _roomInfoList = new ItemList { SizeFlagsVertical = SizeFlags.ExpandFill, SizeFlagsHorizontal = SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(0, 70), AllowReselect = true, FocusMode = FocusModeEnum.None };
        _roomInfoList.ItemSelected += i => ClickRoom((int)i);
        v.AddChild(_roomInfoList);
        return panel;
    }

    // ---- refresh ----

    public override void _Process(double delta)
    {
        if (!Visible) return;
        _sinceChange += delta;
        if (_uiDirty && (_sinceChange > 0.12 || _validatedRevision < 0)) { Refresh(); }
        float pan = 700f * (float)delta;
        var d = Vector2.Zero;
        if (Actions.Held(Ctx + "pan_left")) d.X += pan;
        if (Actions.Held(Ctx + "pan_right")) d.X -= pan;
        if (Actions.Held(Ctx + "pan_up")) d.Y += pan;
        if (Actions.Held(Ctx + "pan_down")) d.Y -= pan;
        if (d != Vector2.Zero) Canvas.Pan(d);
    }

    /// <summary>Re-validates (when the drawing changed) and brings every widget in line with the model.</summary>
    public void Refresh()
    {
        _uiDirty = false;
        _syncing = true;
        if (_validatedRevision != Model.Revision) { Model.Validate(); _validatedRevision = Model.Revision; FillLists(); }
        var m = Model.Meta;
        _title.Text = T("BPEDIT_TITLE") + (Model.Dirty ? " *" : "");
        if (!_id.HasFocus() && _id.Text != m.Id) _id.Text = m.Id;
        if (!_name.HasFocus() && _name.Text != m.Name) _name.Text = m.Name;
        _wealth.Selected = (int)m.Wealth;
        _kind.Selected = m.Kind == BlueprintKind.RoomTemplate ? 1 : 0;
        int di = m.Dwelling == null ? 0 : Math.Max(0, Enumerable.Range(0, _dwelling.ItemCount).FirstOrDefault(i => _dwelling.GetItemText(i) == m.Dwelling));
        _dwelling.Selected = di;
        FillStyles();
        _levelLabel.Text = $"z = {(Model.Z > 0 ? "+" : "")}{Model.Z}" + (Model.Z == 0 ? " " + T("BPEDIT_GROUND") : "") + (Model.Level(Model.Z) == null ? " " + T("BPEDIT_EMPTY") : "");
        _status.Text = Model.Status;
        foreach (var (b, on) in _stateButtons) b.SetPressedNoSignal(on());
        _regionsBtn.SetPressedNoSignal(Canvas.ShowRegions);
        _gridBtn.SetPressedNoSignal(Canvas.ShowGrid);
        foreach (var (id, b) in _pieceButtons) b.SetPressedNoSignal(id == Model.Piece);
        _previewWealthBtn.Text = T("BPEDIT_PREVIEW_WEALTH") + (Model.PreviewWealth is { } w ? ": " + w : ": " + T("BPEDIT_OFF"));
        _previewStyleBtn.Text = T("BPEDIT_PREVIEW_STYLE") + (Model.PreviewStyle is { } s ? ": " + s : ": " + T("BPEDIT_OFF"));
        _previewWealthBtn.CustomMinimumSize = new Vector2(_previewWealthBtn.Text.Length * 8.5f + 80, 28);
        _previewStyleBtn.CustomMinimumSize = new Vector2(_previewStyleBtn.Text.Length * 8.5f + 80, 28);
        // room list selection
        for (int i = 0; i < _roomList.ItemCount; i++) if (_roomList.GetItemText(i)[0] == Model.Letter) { _roomList.Select(i); break; }
        if (Model.Letter != '\0' && Model.Rooms.TryGetValue(Model.Letter, out var pur))
            for (int i = 0; i < _purpose.ItemCount; i++) if (_purpose.GetItemText(i) == pur) { _purpose.Selected = i; break; }
        _syncing = false;
    }

    void FillStyles()
    {
        var want = new[] { "(default)" }.Concat(Model.Lib.Styles.Keys.OrderBy(k => k, StringComparer.Ordinal)).ToList();
        if (_style.ItemCount != want.Count || Enumerable.Range(0, want.Count).Any(i => _style.GetItemText(i) != want[i]))
        {
            _style.Clear();
            foreach (var s in want) _style.AddItem(s);
        }
        int at = Model.Meta.Style == null ? 0 : want.IndexOf(Model.Meta.Style);
        _style.Selected = Math.Max(0, at);
    }

    List<Problem> _listedProblems = new();
    void FillLists()
    {
        _roomList.Clear();
        foreach (var (k, v) in Model.Rooms.OrderBy(p => p.Key)) _roomList.AddItem($"{k}  {v}");
        _problemList.Clear();
        _listedProblems = Model.Problems.ToList();
        foreach (var p in _listedProblems)
        {
            int i = _problemList.AddItem((p.IsError ? "! " : "i ") + p.Message);
            _problemList.SetItemTooltip(i, p.Code + (p.Cells.Count > 0 ? $"  ({p.Cells.Count} cells)" : ""));
            _problemList.SetItemCustomFgColor(i, p.IsError ? PixelTheme.Warn : PixelTheme.Dim);
        }
        int errors = _listedProblems.Count(p => p.IsError);
        _summary.Text = errors == 0 ? T("BPEDIT_VALID") : L.F("BPEDIT_PROBLEMS", errors);
        _summary.ThemeTypeVariation = errors == 0 ? "GoodLabel" : "WarnLabel";
        _roomInfoList.Clear();
        foreach (var r in Model.RoomInfos)
        {
            int i = _roomInfoList.AddItem($"#{r.Index} z={r.Z} {r.Area} cells: {r.Type} ({r.Declared})");
            _roomInfoList.SetItemCustomFgColor(i, r.Ok ? PixelTheme.Body : PixelTheme.Warn);
        }
    }

    void OnHover((int X, int Y)? h)
    {
        if (h is not { } c) { _hoverInfo.Text = ""; return; }
        var lv = Model.Level(Model.Z);
        string s = "", f = "", r = "";
        if (lv != null)
        {
            int i = lv.Idx(c.X, c.Y);
            s = lv.Structure[i] ?? "-"; f = lv.Fixture[i] ?? ""; r = lv.Letter[i] != '\0' ? $" room {lv.Letter[i]}" : "";
        }
        _hoverInfo.Text = $"({c.X},{c.Y}) {s}{(f.Length > 0 ? " + " + f : "")}{r}";
    }

    public void DebugClickProblem(int i) => ClickProblem(i);

    void ClickProblem(int i)
    {
        if (i < 0 || i >= _listedProblems.Count) return;
        Highlight(_listedProblems[i].Cells, _listedProblems[i].Message);
    }

    void ClickRoom(int i)
    {
        if (i < 0 || i >= Model.RoomInfos.Count) return;
        var r = Model.RoomInfos[i];
        Highlight(r.Cells, $"room #{r.Index}: {r.Type}");
    }

    void Highlight(IReadOnlyList<Cell3> cells, string what)
    {
        if (cells.Count == 0) { Model.SetStatus(what); _uiDirty = true; return; }
        int z = cells[0].Z;
        Model.SetZ(z);
        Canvas.Highlight(cells.Where(c => c.Z == z).Select(c => (c.X, c.Y)));
        Model.SetStatus(what);
        _uiDirty = true;
    }

    // ---- preview ----

    void CyclePreviewWealth()
    {
        _previewWealthStep = (_previewWealthStep + 1) % 4;
        Model.SetPreview(_previewWealthStep == 0 ? null : (Wealth)(_previewWealthStep - 1), Model.PreviewStyle);
    }

    void CyclePreviewStyle()
    {
        var styles = Model.Lib.Styles.Keys.OrderBy(k => k, StringComparer.Ordinal).ToList();
        _previewStyleStep = (_previewStyleStep + 1) % (styles.Count + 1);
        Model.SetPreview(Model.PreviewWealth, _previewStyleStep == 0 ? null : styles[_previewStyleStep - 1]);
    }

    // ---- header ----

    void CommitHeader()
    {
        if (_syncing) return;
        string id = _id.Text.Trim().ToLowerInvariant().Replace(' ', '_');
        string name = _name.Text.Trim();
        if (id == Model.Meta.Id && name == Model.Meta.Name) return;
        Model.SetMeta(m => m with { Id = id.Length > 0 ? id : m.Id, Name = name });
    }

    void RestyleFromHeader()
    {
        if (_syncing) return;
        string? style = _style.Selected <= 0 ? null : _style.GetItemText(_style.Selected);
        Model.Restyle((Wealth)Math.Max(0, _wealth.Selected), style);
    }

    // ---- commands ----

    void Save()
    {
        CommitHeader();
        if (Model.FilePath == null) { ShowSaveAs(); return; }
        Model.Save(out var msg);
        Toast(msg);
    }

    void Toast(string msg) { if (_router != null) _router.ShowToast(msg); else GD.Print(msg); }

    void RequestClose()
    {
        if (_dialog != null) { CloseDialog(); return; }
        if (Model.Dirty && _router != null)
            ShowDialog(T("BPEDIT_UNSAVED_TITLE"), new Label { Text = T("BPEDIT_UNSAVED"), AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(380, 0) },
                (T("BPEDIT_DISCARD"), () => _router.Pop()), (T("BPEDIT_KEEP_EDITING"), null));
        else _router?.Pop();
    }

    void WithUnsavedCheck(Action next)
    {
        if (!Model.Dirty) { next(); return; }
        ShowDialog(T("BPEDIT_UNSAVED_TITLE"), new Label { Text = T("BPEDIT_UNSAVED"), AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(380, 0) },
            (T("BPEDIT_DISCARD"), next), (T("BPEDIT_KEEP_EDITING"), null));
    }

    void AskNew() => WithUnsavedCheck(ShowNew);

    void AskOpen() => WithUnsavedCheck(ShowOpen);

    // ---- dialogs (own overlay so Esc is ours to handle) ----

    void ShowDialog(string title, Control content, params (string Label, Action? Press)[] buttons)
    {
        CloseDialog();
        var dim = new ColorRect { Color = new Color(0, 0, 0, 0.6f), MouseFilter = MouseFilterEnum.Stop };
        dim.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        var center = new CenterContainer();
        center.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        dim.AddChild(center);
        var panel = new PanelContainer();
        center.AddChild(panel);
        var v = new VBoxContainer();
        v.AddThemeConstantOverride("separation", 10);
        panel.AddChild(v);
        v.AddChild(new Label { Text = title, ThemeTypeVariation = "GoldLabel" });
        v.AddChild(content);
        var row = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.End };
        row.AddThemeConstantOverride("separation", 8);
        v.AddChild(row);
        foreach (var (label, press) in buttons)
        {
            var b = new Button { Text = label, FocusMode = FocusModeEnum.None };
            var p = press;
            b.Pressed += () => { CloseDialog(); p?.Invoke(); };
            row.AddChild(b);
        }
        var cancel = new Button { Text = T("BPEDIT_CANCEL") + " " + InputBootstrap.Hint("menu.back"), FocusMode = FocusModeEnum.None };
        cancel.Pressed += CloseDialog;
        if (buttons.All(x => x.Press != null)) row.AddChild(cancel);
        AddChild(dim);
        _dialog = dim;
        Actions.UiCaptured = false;
    }

    void CloseDialog()
    {
        _dialog?.QueueFree();
        _dialog = null;
    }

    void ShowNew()
    {
        var box = new GridContainer { Columns = 2 };
        var id = new LineEdit { Text = "new_blueprint", CustomMinimumSize = new Vector2(220, 0) };
        var w = new SpinBox { MinValue = 1, MaxValue = EditorModel.MaxSize, Value = 8 };
        var h = new SpinBox { MinValue = 1, MaxValue = EditorModel.MaxSize, Value = 6 };
        box.AddChild(new Label { Text = T("BPEDIT_F_ID") }); box.AddChild(id);
        box.AddChild(new Label { Text = T("BPEDIT_WIDTH") }); box.AddChild(w);
        box.AddChild(new Label { Text = T("BPEDIT_HEIGHT") }); box.AddChild(h);
        ShowDialog(T("BPEDIT_NEW"), box, (T("BPEDIT_CREATE"), () =>
        {
            string nid = id.Text.Trim().ToLowerInvariant().Replace(' ', '_');
            Model.New(nid.Length > 0 ? nid : "new_blueprint", (int)w.Value, (int)h.Value);
            Canvas.Fit();
        }));
        id.GrabFocus(); id.SelectAll();
    }

    void ShowOpen()
    {
        var entries = Model.Lib.Blueprints.OrderBy(b => b.Def.Id, StringComparer.Ordinal).ToList();
        var filter = new LineEdit { PlaceholderText = T("BPEDIT_FILTER"), CustomMinimumSize = new Vector2(420, 0) };
        var list = new ItemList { CustomMinimumSize = new Vector2(420, 360) };
        var shown = new List<BlueprintEntry>();
        void Fill()
        {
            list.Clear(); shown.Clear();
            foreach (var e in entries)
            {
                if (filter.Text.Length > 0 && !(e.Def.Id + " " + e.Def.Name).Contains(filter.Text, StringComparison.OrdinalIgnoreCase)) continue;
                string file = e.Location is { } l ? EditorLibrary.Describe(l.File) : "?";
                list.AddItem($"{e.Def.Id}   {e.Def.Name}   [{file}]");
                shown.Add(e);
            }
            if (shown.Count > 0) list.Select(0);
        }
        void OpenSel(int i)
        {
            if (i < 0 || i >= shown.Count) return;
            CloseDialog();
            if (!Model.Open(shown[i], out var err)) { Model.SetStatus("Open failed: " + err); Toast("Open failed: " + err); }
            Canvas.Fit();
        }
        filter.TextChanged += _ => Fill();
        filter.TextSubmitted += _ => { var s = list.GetSelectedItems(); if (s.Length > 0) OpenSel(s[0]); };
        list.ItemActivated += i => OpenSel((int)i);
        Fill();
        var v = new VBoxContainer();
        v.AddChild(filter); v.AddChild(list);
        ShowDialog(T("BPEDIT_OPEN"), v, (T("BPEDIT_OPEN"), () => { var sel = list.GetSelectedItems(); if (sel.Length > 0) OpenSel(sel[0]); }));
        filter.GrabFocus();
    }

    void ShowSaveAs()
    {
        CommitHeader();
        var files = EditorLibrary.BlueprintFiles();
        var pick = new OptionButton { CustomMinimumSize = new Vector2(360, 0) };
        pick.AddItem(T("BPEDIT_NEW_USER_FILE"));
        foreach (var f in files) pick.AddItem(EditorLibrary.Describe(f));
        var name = new LineEdit { Text = Model.Meta.Id + ".jsonc", CustomMinimumSize = new Vector2(360, 0) };
        int cur = Model.FilePath == null ? -1 : files.FindIndex(f => string.Equals(Path.GetFullPath(f), Path.GetFullPath(Model.FilePath), StringComparison.Ordinal));
        pick.Selected = cur >= 0 ? cur + 1 : 0;
        var v = new VBoxContainer();
        v.AddThemeConstantOverride("separation", 6);
        v.AddChild(new Label { Text = T("BPEDIT_SAVE_WHERE") });
        v.AddChild(pick);
        v.AddChild(new Label { Text = T("BPEDIT_NEW_FILE_NAME") });
        v.AddChild(name);
        v.AddChild(Hint(T("BPEDIT_SAVE_HINT")));
        ShowDialog(T("BPEDIT_SAVE_AS"), v, (T("BPEDIT_SAVE"), () =>
        {
            string path;
            if (pick.Selected > 0) path = files[pick.Selected - 1];
            else
            {
                string fn = name.Text.Trim();
                if (fn.Length == 0) fn = Model.Meta.Id;
                if (!fn.EndsWith(".jsonc", StringComparison.OrdinalIgnoreCase)) fn += ".jsonc";
                path = Path.Combine(EditorLibrary.EnsureUserMod(), "blueprints", Path.GetFileName(fn));
            }
            Model.SaveTo(path, out var msg);
            Toast(msg);
        }));
    }

    void ShowResize()
    {
        var w = new SpinBox { MinValue = 1, MaxValue = EditorModel.MaxSize, Value = Model.Width };
        var h = new SpinBox { MinValue = 1, MaxValue = EditorModel.MaxSize, Value = Model.Height };
        var anchor = new OptionButton();
        foreach (var a in new[] { "BPEDIT_ANCHOR_TL", "BPEDIT_ANCHOR_TR", "BPEDIT_ANCHOR_BL", "BPEDIT_ANCHOR_BR", "BPEDIT_ANCHOR_C" }) anchor.AddItem(T(a));
        var box = new GridContainer { Columns = 2 };
        box.AddChild(new Label { Text = T("BPEDIT_WIDTH") }); box.AddChild(w);
        box.AddChild(new Label { Text = T("BPEDIT_HEIGHT") }); box.AddChild(h);
        box.AddChild(new Label { Text = T("BPEDIT_ANCHOR") }); box.AddChild(anchor);
        ShowDialog(T("BPEDIT_RESIZE"), box, (T("BPEDIT_APPLY"), () =>
        {
            int nw = (int)w.Value, nh = (int)h.Value, dx = nw - Model.Width, dy = nh - Model.Height;
            // the anchor is the corner that stays put: growing at the other side shifts the content
            (int ox, int oy) = anchor.Selected switch { 1 => (dx, 0), 2 => (0, dy), 3 => (dx, dy), 4 => (dx / 2, dy / 2), _ => (0, 0) };
            Model.Resize(nw, nh, ox, oy);
            Canvas.Fit();
        }));
    }

    // ---- input ----

    public override void _UnhandledInput(InputEvent e)
    {
        if (!Visible || e is InputEventMouseMotion) return;
        if (Actions.Pressed(e, "menu.back"))
        {
            if (_dialog != null) CloseDialog(); else RequestClose();
            GetViewport().SetInputAsHandled();
            return;
        }
        if (_dialog != null) return;
        foreach (var (id, act) in _keys)
            if (Actions.Pressed(e, id)) { Invoke(id); GetViewport().SetInputAsHandled(); return; }
    }

    /// <summary>Runs the handler of an action id such as "blueprinteditor.tool_rect" (also what the keys call).</summary>
    public bool Invoke(string actionId)
    {
        if (!_keys.TryGetValue(actionId, out var act)) return false;
        act();
        OnChanged();
        return true;
    }
}
