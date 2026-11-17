using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Ravensguard.Game.Inputs;
using Ravensguard.Game.Debug;
using Ravensguard.Sim.Debug;

namespace Ravensguard.Game.UI;

/// <summary>Dev console toggled with backtick (global.console). Commands register via Register().</summary>
public partial class DevConsole : CanvasLayer
{
    public static DevConsole? Instance { get; private set; }
    public DevCommandRegistry Registry { get; } = new();
    Control _root = null!;
    RichTextLabel _log = null!;
    LineEdit _input = null!;

    public bool IsOpen => _root.Visible;

    public void Register(string name, string help, Func<string[], string> run) => Registry.Register(name, help, run);

    const int MaxLines = 500;
    readonly Queue<string> _lines = new();

    public void Print(string line)
    {
        foreach (var l in line.Split('\n')) _lines.Enqueue(l);
        if (_lines.Count <= MaxLines) { _log.AppendText(line + "\n"); return; }
        while (_lines.Count > MaxLines) _lines.Dequeue();
        _log.Clear();
        _log.AppendText(string.Join("\n", _lines) + "\n");
    }
    public void Clear() { _lines.Clear(); _log.Clear(); }

    public override void _ExitTree()
    {
        if (Instance == this) Instance = null;
        if (_root.Visible) Actions.UiCaptured = false;
    }

    public override void _Ready()
    {
        Instance = this;
        Layer = 100;
        _root = new PanelContainer { Visible = false, Theme = PixelTheme.Build(), TextureFilter = Control.TextureFilterEnum.Nearest };
        _root.SetAnchorsPreset(Control.LayoutPreset.TopWide);
        _root.CustomMinimumSize = new Vector2(0, 280);
        AddChild(_root);
        var v = new VBoxContainer();
        _root.AddChild(v);
        _log = new RichTextLabel { ScrollFollowing = true, SizeFlagsVertical = Control.SizeFlags.ExpandFill, FitContent = false, CustomMinimumSize = new Vector2(0, 220) };
        v.AddChild(_log);
        _input = new LineEdit { PlaceholderText = L.T("CONSOLE_PROMPT") };
        _input.TextSubmitted += Submit;
        v.AddChild(_input);

        Register("echo", L.T("CONSOLE_HELP_ECHO"), a => string.Join(" ", a));
        Register("fps", L.T("CONSOLE_HELP_FPS"), _ => $"{Engine.GetFramesPerSecond()} fps");
        Register("quit", L.T("CONSOLE_HELP_QUIT"), _ => { GetTree().Quit(); return ""; });
        DevContext.RegisterBuiltins(Registry, Clear);
        Register("bpedit", L.T("CONSOLE_HELP_BPEDIT"), a =>
        {
            if (a.Length > 1) return "usage: bpedit [blueprint_id]";
            return Tools.BlueprintEditor.BlueprintEditorScreen.Launch((GetParent() as App)?.Router, a.Length == 1 ? a[0] : null);
        });
        Print(L.T("CONSOLE_WELCOME"));
    }

    void Submit(string text)
    {
        _input.Clear();
        if (string.IsNullOrWhiteSpace(text)) return;
        Print("> " + text);
        var output = Registry.Execute(text);
        if (output.Length > 0) Print(output);
    }

    public override void _Input(InputEvent e)
    {
        if (!Actions.PressedAlways(e, "global.console")) return;
        _root.Visible = !_root.Visible;
        Actions.UiCaptured = _root.Visible;
        if (_root.Visible) _input.GrabFocus(); else _input.ReleaseFocus();
        GetViewport().SetInputAsHandled();
    }

    /// <summary>While open, keys and clicks the console did not consume must not reach the screens underneath.</summary>
    public override void _UnhandledInput(InputEvent e)
    {
        if (_root.Visible && e is InputEventKey or InputEventMouseButton) GetViewport().SetInputAsHandled();
    }
}
