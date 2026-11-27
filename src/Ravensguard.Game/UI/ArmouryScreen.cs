using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Ravensguard.Game.Inputs;
using Ravensguard.Game.UI.NewGame;
using Ravensguard.Sim.Soldiers;

namespace Ravensguard.Game.UI;

/// <summary>What the Armoury screen asks of the world; each is one <c>so:</c> command through the hero desk (the desk checks, moves the items and answers; the screen moves nothing).</summary>
public sealed class ArmouryActions
{
    public Action<long>? FillAll, FillGaps;
    /// <summary>P8-WS-2: S hires the steward for the armoury (the key, the kits) or lets him go (when one is hired); C commissions an armourer once (the key, the kits). Each is one <c>so:</c> command.</summary>
    public Action<long, int, bool>? Steward;
    public Action<long, int>? Commission;
    public Action<ulong, long>? FillOne;
    public Action<ulong, string, string>? SetTemplate;
    /// <summary>The template ids a role's men may take (content), in id order.</summary>
    public Func<IReadOnlyList<string>>? Templates;
}

/// <summary>
/// The Armoury screen (P8-WS-1, war screens 3.1; the Armoury context): the hero's soldiers down the left, the template's slots across, a required slot with nothing in it in red, one an armoury fill could close in gold.
/// Up and Down pick a man, Left and Right a slot, F fills all from the armoury (best first), G only the gaps, T takes the next template for the man, Enter fits the one man by hand, Tab the next armoury, Esc closes.
/// A view over <see cref="ArmouryView"/> built at a barrier: it holds the cursor and nothing of the world, and every change is a desk command.
/// </summary>
public partial class ArmouryScreen : Control, IHandlesBack
{
    const int MaxRows = 14, SlotW = 66, NameW = 190, ArmsKits = 10;
    readonly ScreenRouter _router;
    readonly Func<long, ArmouryView?> _data;
    readonly Func<IReadOnlyList<long>> _keys;
    readonly ArmouryActions _act;
    int _row, _col, _first, _armoury;
    ArmouryView? _shown;
    Label _status = null!, _keysHint = null!;
    VBoxContainer _table = null!;
    double _refresh;
    int _shotWait;
    public string? ShotPath { get; init; }

    public ArmouryScreen(ScreenRouter router, Func<long, ArmouryView?> data, Func<IReadOnlyList<long>> keys, ArmouryActions act)
    {
        _router = router; _data = data; _keys = keys; _act = act;
    }

    public override void _Ready()
    {
        ActiveContexts.Enter(InputContext.Armoury);
        var margin = UiKit.Margin(this, 12);
        var root = new VBoxContainer(); root.AddThemeConstantOverride("separation", 4); margin.AddChild(root);
        root.AddChild(UiKit.Title(L.T("ar_title")));
        _status = UiKit.Hint(""); root.AddChild(_status);
        _table = new VBoxContainer(); _table.AddThemeConstantOverride("separation", 1);
        var p = UiKit.Panel(_table, 0, 0); p.SizeFlagsVertical = SizeFlags.ExpandFill; root.AddChild(p);
        _keysHint = UiKit.Hint(L.F("ar_keys", InputBootstrap.Hint("armoury.fill_all"), InputBootstrap.Hint("armoury.fill_gaps"), InputBootstrap.Hint("armoury.template"), InputBootstrap.Hint("armoury.refit"), InputBootstrap.Hint("armoury.next_armoury"), InputBootstrap.Hint("armoury.back"), InputBootstrap.Hint("armoury.steward"), InputBootstrap.Hint("armoury.commission")), true);
        root.AddChild(_keysHint);
        Build();
    }

    public override void _ExitTree() => ActiveContexts.Exit(InputContext.Armoury);

    long Key() { var ks = _keys(); return ks.Count == 0 ? -1 : ks[Math.Clamp(_armoury, 0, ks.Count - 1)]; }

    public override void _Process(double delta)
    {
        _refresh += delta;
        if (_refresh > 0.5) { _refresh = 0; Build(); }
        if (ShotPath == null || !Visible || _shown == null || ++_shotWait != 12) return;
        var img = GetViewport().GetTexture().GetImage();
        System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(ShotPath) ?? ".");
        GD.Print($"[armoury] screenshot {ShotPath}: {img.SavePng(ShotPath)}");
        GetTree().Quit();
    }

    static Label Cell(string text, int w, Color? c = null)
    {
        var l = new Label { Text = text, CustomMinimumSize = new Vector2(w, 0), ClipText = true, TooltipText = text, MouseFilter = MouseFilterEnum.Pass };
        if (c is { } col) l.AddThemeColorOverride("font_color", col);
        return l;
    }

    static readonly Color Red = new(0.95f, 0.35f, 0.3f);

    void Build()
    {
        var v = _data(Key());
        _shown = v;
        foreach (var c in _table.GetChildren()) { _table.RemoveChild(c); c.QueueFree(); }
        if (v == null || Key() < 0) { _status.Text = L.T("ar_none"); return; }
        _row = Math.Clamp(_row, 0, Math.Max(0, v.Rows.Count - 1)); _col = Math.Clamp(_col, 0, Math.Max(0, v.Columns.Count - 1));
        if (_row < _first) _first = _row; if (_row >= _first + MaxRows) _first = _row - MaxRows + 1;
        _status.Text = L.F("ar_status", v.Rows.Count, v.Stock, v.GapCount) + "  " + (v.StewardBudgetMites >= 0 ? L.F("ar_arms_steward", Ravensguard.Sim.Core.MoneyText.Format(v.StewardBudgetMites), v.StewardKits, v.ArmsLive) : L.F("ar_arms_none", v.ArmsLive));
        if (v.Rows.Count == 0) { _table.AddChild(UiKit.Hint(L.T("ar_no_soldiers"), true)); return; }
        var head = new HBoxContainer(); head.AddThemeConstantOverride("separation", 2);
        head.AddChild(Cell(L.T("ar_col_soldier"), NameW, PixelTheme.Dim));
        for (int k = 0; k < v.Columns.Count; k++) head.AddChild(Cell(L.T("ar_slot_" + v.Columns[k]), SlotW, k == _col ? PixelTheme.Gold : PixelTheme.Dim));
        _table.AddChild(head);
        for (int r = _first; r < Math.Min(v.Rows.Count, _first + MaxRows); r++)
        {
            var row = v.Rows[r]; bool sel = r == _row;
            var line = new HBoxContainer { CustomMinimumSize = new Vector2(0, 22) }; line.AddThemeConstantOverride("separation", 2);
            line.AddChild(Cell((row.Name.Length > 0 ? row.Name : "#" + row.Person) + " " + L.T("so_role_" + row.Role), NameW, sel ? PixelTheme.Gold : null));
            foreach (var col in v.Columns)
            {
                var cell = row.Cells.FirstOrDefault(c => c.Slot == col);
                if (cell == null) { line.AddChild(Cell("", SlotW)); continue; }
                string text = cell.Shape.Length > 0 ? cell.Shape.Replace('_', ' ') : cell.Own ? "-" : cell.Gap ? L.T("ar_gap") : cell.Fillable ? L.T("ar_can_fill") : "-";
                Color? tint = cell.Gap && !cell.Fillable ? Red : cell.Fillable ? PixelTheme.Gold : cell.Shape.Length == 0 ? PixelTheme.Dim : null;
                if (sel && col == v.Columns[_col]) tint = Colors.White;
                line.AddChild(Cell(text, SlotW, tint));
            }
            int rr = r;
            line.GuiInput += e => { if (e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left }) { _row = rr; Build(); } };
            _table.AddChild(line);
        }
    }

    ArmourySoldierRow? Current => _shown != null && _row >= 0 && _row < _shown.Rows.Count ? _shown.Rows[_row] : null;

    /// <summary>T: the next template (by id) the man's role may take; the desk sets it and answers.</summary>
    void NextTemplate()
    {
        if (Current is not { } r || _act.Templates == null) return;
        var all = _act.Templates();
        if (all.Count == 0) return;
        int at = all.ToList().IndexOf(r.Template);
        _act.SetTemplate?.Invoke(r.Person, r.Role, all[(at + 1) % all.Count]);
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (!Visible || _router.HasModal) return;
        if (Actions.Pressed(e, "armoury.back")) { _router.Pop(); }
        else if (Actions.Pressed(e, "armoury.up")) { _row = Math.Max(0, _row - 1); Build(); }
        else if (Actions.Pressed(e, "armoury.down")) { _row++; Build(); }
        else if (Actions.Pressed(e, "armoury.left")) { _col = Math.Max(0, _col - 1); Build(); }
        else if (Actions.Pressed(e, "armoury.right")) { _col++; Build(); }
        else if (Actions.Pressed(e, "armoury.next_armoury")) { _armoury = _keys().Count == 0 ? 0 : (_armoury + 1) % _keys().Count; _row = 0; _first = 0; Build(); }
        else if (Actions.Pressed(e, "armoury.fill_all")) { if (Key() >= 0) _act.FillAll?.Invoke(Key()); }
        else if (Actions.Pressed(e, "armoury.fill_gaps")) { if (Key() >= 0) _act.FillGaps?.Invoke(Key()); }
        else if (Actions.Pressed(e, "armoury.template")) NextTemplate();
        else if (Actions.Pressed(e, "armoury.steward")) { if (Key() >= 0) _act.Steward?.Invoke(Key(), ArmsKits, _shown is { StewardBudgetMites: >= 0 }); }
        else if (Actions.Pressed(e, "armoury.commission")) { if (Key() >= 0) _act.Commission?.Invoke(Key(), ArmsKits); }
        else if (Actions.Pressed(e, "armoury.refit")) { if (Current is { } r && Key() >= 0) _act.FillOne?.Invoke(r.Person, Key()); }
        else return;
        GetViewport().SetInputAsHandled();
    }
}
