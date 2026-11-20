using System;
using Godot;
using Ravensguard.Game.UI;
using Ravensguard.Game.UI.NewGame;

namespace Ravensguard.Game.World.Build;

/// <summary>
/// P5-C: save as blueprint (Ctrl+S in build mode, a rectangle dragged over built pieces and ghosts; build-mode-and-construction 4.8): the plan's name, then one command through the hero's desk.
/// The sim writes the blueprint (rows, legend, levels, rooms, wealth, cultures ["any"]) into the save's designs, and the street view copies it to the player's mod on disk. A modal over the
/// street (the rectangle stays in view): while it is open build mode takes no key (the street view's guard), the name field holds the keyboard (<see cref="UiKit.CaptureKeys"/>), Enter saves
/// and Esc closes it.
/// </summary>
public partial class SaveBlueprintDialog : Control
{
    readonly int _w, _h;
    readonly Action<string> _save;
    readonly Action _close;
    LineEdit _name = null!;
    bool _done, _closed;

    public SaveBlueprintDialog(int w, int h, Action<string> save, Action close) { _w = w; _h = h; _save = save; _close = close; }

    public override void _Ready()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Stop;
        var dim = new ColorRect { Color = new Color(0, 0, 0, 0.35f), MouseFilter = MouseFilterEnum.Stop };
        dim.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect); AddChild(dim);
        var center = new CenterContainer(); center.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect); AddChild(center);
        var v = new VBoxContainer { CustomMinimumSize = new Vector2(420, 0) }; v.AddThemeConstantOverride("separation", 8);
        center.AddChild(UiKit.Panel(v));
        v.AddChild(UiKit.Title(L.T("BUILD_SAVE_TITLE")));
        var body = UiKit.Body(L.F("BUILD_SAVE_BODY", _w, _h)); body.CustomMinimumSize = new Vector2(400, 0); v.AddChild(body);
        _name = new LineEdit { PlaceholderText = L.T("BUILD_SAVE_PLACEHOLDER"), MaxLength = 40, CustomMinimumSize = new Vector2(400, 30) };
        UiKit.CaptureKeys(_name);
        _name.TextSubmitted += _ => Save();
        v.AddChild(_name);
        var note = UiKit.Hint(L.T("BUILD_SAVE_NOTE"), wrap: true); note.CustomMinimumSize = new Vector2(400, 0); v.AddChild(note);
        var row = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.End }; row.AddThemeConstantOverride("separation", 8); v.AddChild(row);
        var ok = new Button { Text = L.T("BUILD_SAVE_OK") + "  [Enter]", CustomMinimumSize = new Vector2(150, 30) }; ok.Pressed += Save; row.AddChild(ok);
        var no = new Button { Text = L.T("UI_CANCEL") + "  [Esc]", CustomMinimumSize = new Vector2(150, 30) }; no.Pressed += Close; row.AddChild(no);
        _name.CallDeferred(Control.MethodName.GrabFocus);
    }

    /// <summary>Dev and smoke: the name typed.</summary>
    public void DebugName(string n) { if (_name != null) _name.Text = n; }

    void Save()
    {
        if (_done) return;
        string n = _name.Text.Trim();
        if (n.Length == 0) n = L.T("BUILD_SAVE_DEFAULT_NAME");
        _done = true;
        _save(n);
        Close();
    }

    public void Close() { if (_closed) return; _closed = true; _close(); }

    public override void _Input(InputEvent e)
    {
        if (!Visible || e is not InputEventKey { Pressed: true } k) return;
        if (k.Keycode == Key.Escape) { Close(); GetViewport().SetInputAsHandled(); }
        else if (k.Keycode is Key.Enter or Key.KpEnter) { Save(); GetViewport().SetInputAsHandled(); }
    }
}
