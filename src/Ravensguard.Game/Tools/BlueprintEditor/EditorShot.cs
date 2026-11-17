using System;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Ravensguard.Content;
using Ravensguard.Game.UI;
using Ravensguard.Sim.Building;

namespace Ravensguard.Game.Tools.BlueprintEditor;

/// <summary>`-- --bpedit-shot [id] --shot-out file.png`: opens the editor on a blueprint, makes an edit that breaks it, selects the problem and saves a screenshot (debug builds).</summary>
public static class EditorShot
{
    public static async Task Run(SceneTree tree, ScreenRouter router, string[] args)
    {
        string Arg(string name, string def) { int i = Array.IndexOf(args, name); return i >= 0 && i + 1 < args.Length && !args[i + 1].StartsWith("--") ? args[i + 1] : def; }
        string id = Arg("--bpedit-shot", "cottage_two_room"), outPath = Arg("--shot-out", "/tmp/bpedit.png");
        async Task Frames(int n) { for (int i = 0; i < n; i++) await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame); }
        await Frames(3);
        GD.Print("bpedit-shot: " + BlueprintEditorScreen.Launch(router, id));
        await Frames(4);
        if (router.Top is not BlueprintEditorScreen screen) { tree.Quit(1); return; }
        var m = screen.Model;
        // mid-edit: a bed painted into the hearth room, then the street door walled up so the validator complains
        m.Tool = EditTool.Rect;
        var ground = m.Level(0)!;
        (int X, int Y)? door = null;
        for (int y = 0; y < m.Height && door == null; y++)
            for (int x = 0; x < m.Width; x++)
                if (m.Pieces.KindOf(ground.Structure[ground.Idx(x, y)]) == PieceKind.Door && (x == 0 || y == 0 || x == m.Width - 1 || y == m.Height - 1)) { door = (x, y); break; }
        m.SetPiece("bed");
        m.BeginEdit(); m.ApplyCells(EditorModel.RectCells(1, 1, 2, 1, false), false); m.EndEdit();
        if (door is { } d)
        {
            m.SetPiece("wall_timber");
            m.BeginEdit(); m.Apply(d.X, d.Y, false); m.EndEdit();
        }
        await Frames(4);
        // a real drag with the mouse: tables along the bottom of the hearth room
        m.SetPiece("table");
        m.Tool = EditTool.Paint;
        var a = screen.Canvas.CellCenter(1, 4); var b = screen.Canvas.CellCenter(3, 4);
        void Mouse(InputEvent e) { Input.ParseInputEvent(e); }
        Mouse(new InputEventMouseMotion { Position = a, GlobalPosition = a });
        await Frames(2);
        GD.Print($"bpedit-shot: a={a} hover={screen.Canvas.Hover} layer={m.Layer} piece={m.Piece} tool={m.Tool}");
        Mouse(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = a, GlobalPosition = a });
        await Frames(2);
        Mouse(new InputEventMouseMotion { Position = b, GlobalPosition = b, Relative = b - a });
        await Frames(2);
        Mouse(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = b, GlobalPosition = b });
        await Frames(2);
        GD.Print($"bpedit-shot: mouse drag painted {(m.Level(0)!.Fixture.Count(f => f == "table"))} table cells (expected 3 new), undo steps ok={m.CanUndo}");
        await Frames(6);
        screen.Refresh();
        screen.DebugClickProblem(1);
        if (Array.IndexOf(args, "--bpedit-dialog") >= 0) { screen.Invoke("blueprinteditor." + Arg("--bpedit-dialog", "open")); await Frames(4); }
        await Frames(6);
        var img = tree.Root.GetViewport().GetTexture().GetImage();
        img.SavePng(outPath);
        GD.Print("bpedit-shot: wrote " + outPath + $" ({m.Problems.Count} problems)");
        tree.Quit(0);
    }
}
