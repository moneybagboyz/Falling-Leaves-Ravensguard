using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace Ravensguard.Game.UI;

/// <summary>Dev console toggled with backtick (global.console). Commands register via Register().</summary>
public partial class DevConsole : CanvasLayer
{
    public static DevConsole? Instance { get; private set; }
    readonly Dictionary<string, (string help, Action<string[]> run)> _commands = new();
    Control _root = null!;
    RichTextLabel _log = null!;
    LineEdit _input = null!;

    public bool IsOpen => _root.Visible;

    public void Register(string name, string help, Action<string[]> run) => _commands[name] = (help, run);

    public void Print(string line) { _log.AppendText(line + "\n"); }

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

        Register("help", L.T("CONSOLE_HELP_HELP"), _ => { foreach (var kv in _commands.OrderBy(k => k.Key)) Print($"{kv.Key} - {kv.Value.help}"); });
        Register("echo", L.T("CONSOLE_HELP_ECHO"), a => Print(string.Join(" ", a)));
        Register("fps", L.T("CONSOLE_HELP_FPS"), _ => Print($"{Engine.GetFramesPerSecond()} fps"));
        Register("quit", L.T("CONSOLE_HELP_QUIT"), _ => GetTree().Quit());
        Print(L.T("CONSOLE_WELCOME"));
    }

    void Submit(string text)
    {
        _input.Clear();
        var parts = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return;
        Print("> " + text);
        if (_commands.TryGetValue(parts[0], out var c)) c.run(parts[1..]);
        else Print(L.F("CONSOLE_UNKNOWN", parts[0]));
    }

    public override void _Input(InputEvent e)
    {
        if (!e.IsActionPressed("global.console")) return;
        _root.Visible = !_root.Visible;
        if (_root.Visible) _input.GrabFocus(); else _input.ReleaseFocus();
        GetViewport().SetInputAsHandled();
    }
}
