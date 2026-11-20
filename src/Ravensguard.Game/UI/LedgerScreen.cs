using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Ravensguard.Game.Inputs;
using Ravensguard.Game.UI.NewGame;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Production;
using Ravensguard.Sim.Trade;

namespace Ravensguard.Game.UI;

/// <summary>
/// The Ledger (L; P5-F, businesses spec 6): every property, business, site, field, stall, vehicle, share and steward of the hero, each tab a sortable table. Up and Down pick a row, Left and Right a column,
/// S sorts by it (again: the other way), / searches, A shows only rows with alerts, Enter opens the row (a business opens its panel), Tab the next tab, Esc closes. Six columns show at 960 x 540; the
/// rest scroll sideways with the column cursor. A modal screen (the Ledger context, alone with Global): the street keys are off while it is up. A view over the barrier's <see cref="LedgerView"/>.
/// </summary>
public partial class LedgerScreen : Control, IHandlesBack
{
    const int Visible6 = 6, RowH = 22, MaxRows = 14;
    readonly ScreenRouter _router;
    readonly Func<LedgerView?> _data;
    readonly Action<EntityId>? _openBusiness;
    LedgerTab _tab = LedgerTab.Businesses;
    int _row, _col, _sortCol = 3, _first;
    bool _desc = true, _alerts;
    string _search = "";
    LineEdit? _searchBox;
    Label _status = null!, _money = null!;
    HBoxContainer _tabs = null!;
    VBoxContainer _table = null!;
    LedgerView? _shown;
    List<LedgerRow> _rows = [];
    double _refresh;
    int _shotWait;
    public string? ShotPath { get; init; }
    /// <summary>The tab it opens on.</summary>
    public LedgerTab StartTab { init => _tab = value; }
    /// <summary>The time to build the shown table (BS-17: under 15 ms).</summary>
    public double LastBuildMs { get; private set; }

    public LedgerScreen(ScreenRouter router, Func<LedgerView?> data, Action<EntityId>? openBusiness) { _router = router; _data = data; _openBusiness = openBusiness; }

    public override void _Ready()
    {
        ActiveContexts.Enter(InputContext.Ledger);
        var margin = UiKit.Margin(this, 12);
        var root = new VBoxContainer(); root.AddThemeConstantOverride("separation", 4); margin.AddChild(root);
        var head = new HBoxContainer(); head.AddThemeConstantOverride("separation", 8);
        var title = UiKit.Title(L.T("lg_title")); title.SizeFlagsHorizontal = SizeFlags.ExpandFill; head.AddChild(title);
        _money = UiKit.Hint(""); head.AddChild(_money);
        root.AddChild(head);
        _tabs = new HBoxContainer(); _tabs.AddThemeConstantOverride("separation", 4); root.AddChild(_tabs);
        foreach (var t in Enum.GetValues<LedgerTab>())
        {
            var b = new Button { Text = L.T("lg_tab_" + t.ToString().ToLowerInvariant()), ToggleMode = true, CustomMinimumSize = new Vector2(0, 24), FocusMode = FocusModeEnum.None };
            var tt = t; b.Pressed += () => { _tab = tt; _row = 0; _first = 0; _col = 0; Build(); };
            _tabs.AddChild(b);
        }
        _status = UiKit.Hint(""); root.AddChild(_status);
        _table = new VBoxContainer(); _table.AddThemeConstantOverride("separation", 1);
        var p = UiKit.Panel(_table, 0, 0); p.SizeFlagsVertical = SizeFlags.ExpandFill; root.AddChild(p);
        root.AddChild(UiKit.Hint(L.F("lg_keys", InputBootstrap.Hint("ledger.sort"), InputBootstrap.Hint("ledger.search"), InputBootstrap.Hint("ledger.alerts"), InputBootstrap.Hint("ledger.open"), InputBootstrap.Hint("ledger.next_tab"), InputBootstrap.Hint("ledger.back")), true));
        Build();
    }

    public override void _ExitTree() => ActiveContexts.Exit(InputContext.Ledger);

    public override void _Process(double delta)
    {
        _refresh += delta;
        if (_refresh > 0.5) { _refresh = 0; if (!ReferenceEquals(_data(), _shown)) Build(); }
        if (ShotPath == null || !Visible || _shown == null || ++_shotWait != 12) return;
        var img = GetViewport().GetTexture().GetImage();
        System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(ShotPath) ?? ".");
        GD.Print($"[ledger] screenshot {ShotPath}: {img.SavePng(ShotPath)}");
        GetTree().Quit();
    }

    static string M(long mites) => Quotes.Money(mites);

    void Build()
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        foreach (var c in _table.GetChildren()) { _table.RemoveChild(c); c.QueueFree(); }
        var v = _data(); _shown = v;
        int i = 0; foreach (var c in _tabs.GetChildren()) if (c is Button b) { var t = (LedgerTab)i++; b.ButtonPressed = t == _tab; int n = v?.Tab(t).Count() ?? 0; b.Text = L.T("lg_tab_" + t.ToString().ToLowerInvariant()) + (n > 0 ? $" {n}" : ""); }
        if (v == null) { _status.Text = L.T("lg_waiting"); return; }
        _money.Text = L.F("lg_money", M(v.Purse), M(v.Chest), M(v.Boxes), v.AlertCount);
        var cols = LedgerView.Columns[_tab];
        _col = Math.Clamp(_col, 0, cols.Length - 1);
        _rows = LedgerView.Sort(v.Tab(_tab), Math.Min(_sortCol, cols.Length - 1), _desc, _alerts, _search);
        _row = Math.Clamp(_row, 0, Math.Max(0, _rows.Count - 1));
        if (_row < _first) _first = _row; if (_row >= _first + MaxRows) _first = _row - MaxRows + 1;
        int c0 = Math.Clamp(_col - Visible6 + 1, 0, Math.Max(0, cols.Length - Visible6));
        string estate = v.EstateOf.Length > 0 ? "   " + L.F(v.EstateUnpaid > 0 ? "in_lg_estate_owed" : "in_lg_estate", v.EstateOf, v.EstateLines, M(v.EstateDebts), M(v.EstateUnpaid)) : "";
        _status.Text = L.F("lg_status", _rows.Count, L.T(cols[Math.Min(_sortCol, cols.Length - 1)]), L.T(_desc ? "lg_desc" : "lg_asc"), _alerts ? L.T("lg_alerts_only") : "", _search.Length > 0 ? "/" + _search : "", c0 > 0 || cols.Length > Visible6 ? L.F("lg_cols", c0 + 1, Math.Min(cols.Length, c0 + Visible6), cols.Length) : "") + estate;
        // the head row: the visible columns, the sorted one marked, the cursor's highlighted
        var head = new HBoxContainer(); head.AddThemeConstantOverride("separation", 2);
        for (int k = c0; k < Math.Min(cols.Length, c0 + Visible6); k++)
        {
            var l = Cell(L.T(cols[k]) + (k == _sortCol ? (_desc ? " -" : " +") : ""), k == 0 ? 220 : 110);
            l.AddThemeColorOverride("font_color", k == _col ? PixelTheme.Gold : PixelTheme.Dim);
            head.AddChild(l);
        }
        _table.AddChild(head);
        if (_rows.Count == 0) _table.AddChild(UiKit.Hint(L.T("lg_empty_" + _tab.ToString().ToLowerInvariant()), true));
        for (int r = _first; r < Math.Min(_rows.Count, _first + MaxRows); r++)
        {
            var row = _rows[r];
            var line = new HBoxContainer { CustomMinimumSize = new Vector2(0, RowH) }; line.AddThemeConstantOverride("separation", 2);
            for (int k = c0; k < Math.Min(cols.Length, c0 + Visible6); k++)
            {
                string text = cols[k] == "lg_col_alerts" ? AlertText(row.Alerts) : k < row.Cells.Length ? row.Cells[k] : "";
                if (text.StartsWith(Ravensguard.Sim.Rule.RuleLedger.Mark, StringComparison.Ordinal)) text = L.T(text[1..]);        // P6-C3: the Fief tab's rows carry text keys
                if (k == 0 && row.Inherited) text += " " + L.T("in_lg_inherited");                 // P5-H: what came by inheritance is marked
                var l = Cell(text, k == 0 ? 220 : 110);
                if (r == _row) l.AddThemeColorOverride("font_color", PixelTheme.Gold);
                else if (row.Alerts != OwnerAlert.None && cols[k] == "lg_col_alerts") l.AddThemeColorOverride("font_color", new Color(0.95f, 0.45f, 0.35f));
                line.AddChild(l);
            }
            int rr = r;
            line.GuiInput += e => { if (e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } mb) { _row = rr; if (mb.DoubleClick) Open(); else Build(); } };
            _table.AddChild(line);
        }
        LastBuildMs = sw.Elapsed.TotalMilliseconds;
    }

    static string AlertText(OwnerAlert a) => a == OwnerAlert.None ? "" : string.Join(", ", Enum.GetValues<OwnerAlert>().Where(f => f != OwnerAlert.None && (a & f) != 0).Select(f => L.T("bz_ui_alert_" + f.ToString().ToLowerInvariant())));

    static Label Cell(string text, int w) => new() { Text = text, CustomMinimumSize = new Vector2(w, 0), ClipText = true, TooltipText = text, MouseFilter = MouseFilterEnum.Pass, SizeFlagsHorizontal = w >= 200 ? SizeFlags.ExpandFill : SizeFlags.Fill };

    void Open()
    {
        if (_row < 0 || _row >= _rows.Count) return;
        var r = _rows[_row];
        if (r.Tab is LedgerTab.Businesses or LedgerTab.Shares && _openBusiness != null) { _openBusiness(r.Id); return; }
        _router.ShowModal(L.T("lg_tab_" + r.Tab.ToString().ToLowerInvariant()), string.Join("\n", LedgerView.Columns[r.Tab].Select((c, k) => $"{L.T(c)}: {(k < r.Cells.Length ? r.Cells[k] : "")}")), (L.T("UI_OK"), null));
    }

    void Search()
    {
        if (_searchBox != null) return;
        _searchBox = new LineEdit { PlaceholderText = L.T("lg_search"), Text = _search, CustomMinimumSize = new Vector2(240, 26) };
        UiKit.CaptureKeys(_searchBox);
        _searchBox.TextSubmitted += t => { _search = t.Trim(); _searchBox?.QueueFree(); _searchBox = null; _row = 0; Build(); };
        _status.GetParent().AddChild(_searchBox);
        _searchBox.GrabFocus();
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (!Visible || _router.HasModal || _searchBox != null) return;
        var cols = LedgerView.Columns[_tab];
        if (Actions.Pressed(e, "ledger.back")) { _router.Pop(); }
        else if (Actions.Pressed(e, "ledger.up")) { _row = Math.Max(0, _row - 1); Build(); }
        else if (Actions.Pressed(e, "ledger.down")) { _row++; Build(); }
        else if (Actions.Pressed(e, "ledger.left")) { _col = Math.Max(0, _col - 1); Build(); }
        else if (Actions.Pressed(e, "ledger.right")) { _col = Math.Min(cols.Length - 1, _col + 1); Build(); }
        else if (Actions.Pressed(e, "ledger.next_tab")) { _tab = (LedgerTab)(((int)_tab + 1) % Enum.GetValues<LedgerTab>().Length); _row = 0; _first = 0; _col = 0; Build(); }
        else if (Actions.Pressed(e, "ledger.sort")) { if (_sortCol == _col) _desc = !_desc; else { _sortCol = _col; _desc = true; } Build(); }
        else if (Actions.Pressed(e, "ledger.alerts")) { _alerts = !_alerts; _row = 0; Build(); }
        else if (Actions.Pressed(e, "ledger.search")) Search();
        else if (Actions.Pressed(e, "ledger.open")) Open();
        else return;
        GetViewport().SetInputAsHandled();
    }
}
