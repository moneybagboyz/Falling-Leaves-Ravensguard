using System;
using System.IO;
using System.Linq;
using Godot;
using Ravensguard.Sim.Building;

namespace Ravensguard.Game.Tools.BlueprintEditor;

/// <summary>Headless check of the blueprint editor (part of `--selftest`): paints a 6x5 cottage with a door and a hearth, validates, writes it to a temp mod, loads it back and compares.</summary>
public static class EditorSelfTest
{
    public static void Run(Action<bool, string> check)
    {
        string? tmp = null;
        try { tmp = RunCore(check); }
        catch (Exception ex) { check(false, "blueprint editor selftest threw: " + ex); }
        finally { if (tmp != null && Directory.Exists(tmp)) Directory.Delete(tmp, true); }
    }

    static string RunCore(Action<bool, string> check)
    {
        var lib = EditorLibrary.Load([EditorLibrary.BaseDir], out _);
        var m = new EditorModel(lib);
        m.New("selftest_cottage", 6, 5);

        // walls round the edge, planks inside, a door in the south wall, a window, a hearth and a hearth room
        m.Tool = EditTool.Rect;
        m.SetPiece("wall_timber");
        m.BeginEdit(); m.ApplyCells(EditorModel.RectCells(0, 0, 5, 4, true), false); m.EndEdit();
        m.SetPiece("floor_plank");
        m.BeginEdit(); m.ApplyCells(EditorModel.RectCells(1, 1, 4, 3, false), false); m.EndEdit();
        m.SetPiece("door_wood");
        m.BeginEdit(); m.Apply(2, 4, false); m.EndEdit();
        m.SetPiece("window_shuttered");
        m.BeginEdit(); m.Apply(0, 2, false); m.EndEdit();
        m.SetPiece("hearth");
        check(m.Layer == EditLayer.Fixtures, "picking furniture did not switch to the Fixtures layer");
        m.BeginEdit(); m.Apply(1, 1, false); m.EndEdit();
        char room = m.AddRoom("hearth");
        check(room != '\0' && m.Layer == EditLayer.Rooms, "AddRoom did not select the Rooms layer");
        m.BeginEdit(); int filled = m.Flood(2, 2, false); m.EndEdit();
        check(filled == 12, $"room flood filled {filled} cells, expected 12");

        // undo and redo walk back and forth over the strokes
        m.Undo(); check(m.Level(0)!.Letter.All(c => c == '\0'), "undo of the room fill left letters behind");
        m.Redo(); check(m.Level(0)!.Letter.Count(c => c == room) == 12, "redo did not restore the room fill");

        m.Validate();
        check(!m.HasErrors, "cottage does not validate: " + string.Join(" | ", m.Problems.Select(p => p.Code + ": " + p.Message)));
        check(m.RoomInfos.Count == 1 && m.RoomInfos[0].Ok, "room detector does not read the cottage as a hearth room: " + string.Join(", ", m.RoomInfos.Select(r => r.Type)));

        // a deliberate fault is found and mapped to cells: remove the door
        m.SetPiece("wall_timber");
        m.BeginEdit(); m.Apply(2, 4, false); m.EndEdit();
        m.Validate();
        check(m.Problems.Any(p => p.Code == "exterior.no_door" || p.Code.StartsWith("room.")), "removing the door raised no problem");
        m.Undo();
        m.Validate();
        check(!m.HasErrors, "undo of the fault did not clear it");

        // write to a temp mod, reload, compare
        string tmp = Path.Combine(Path.GetTempPath(), "rg_bped_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(tmp, "blueprints"));
        File.WriteAllText(Path.Combine(tmp, "mod.json"), "{ \"id\": \"bped_selftest\", \"name\": \"t\", \"dependencies\": [\"base\"] }");
        string file = Path.Combine(tmp, "blueprints", "selftest_cottage.jsonc");
        var before = m.Grid.Clone();
        check(m.SaveTo(file, out var msg), "save failed: " + msg);
        string text = File.ReadAllText(file);
        check(text.Contains("\"id\": \"selftest_cottage\"") && text.Contains("\"#\": \"$wall\"") && text.Contains("\"hearth\"") && text.StartsWith("//"), "written text lacks header, $wall token or hearth");
        var back = m.Lib.Blueprints.FirstOrDefault(b => b.Def.Id == "selftest_cottage");
        check(back != null, "reloaded content has no selftest_cottage");
        if (back != null)
        {
            var g = m.Lib.Parse(back, new System.Collections.Generic.List<BlueprintIssue>());
            check(g != null && g.ContentEquals(before) && g.Rooms.SequenceEqual(before.Rooms), "reloaded grid differs from the painted one");
            check(back.Def.Rows.Count == 5 && back.Def.Rows[4] == "##+###", "ground rows are not the painted ones: " + string.Join("/", back.Def.Rows));
        }
        check(!File.ReadAllText(Path.Combine(EditorLibrary.BaseDir, "blueprints", "dwellings.jsonc")).Contains("selftest_cottage"), "selftest touched content/base");

        // saving again replaces the entry in place
        check(m.SaveTo(file, out msg) && File.ReadAllText(file) == text, "second save changed the file");

        // errors block the save (file untouched), "Save anyway" writes it, then the fix is saved again
        m.SetPiece("wall_timber");
        m.BeginEdit(); m.Apply(2, 4, false); m.EndEdit();
        string cleanText = File.ReadAllText(file);
        check(!m.SaveTo(file, out msg) && msg.StartsWith("Not saved") && File.ReadAllText(file) == cleanText && !File.Exists(file + ".tmp"), "a blueprint with errors was saved without 'Save anyway': " + msg);
        m.SaveAnyway = true;
        check(m.SaveTo(file, out msg) && File.ReadAllText(file) != cleanText, "'Save anyway' did not write: " + msg);
        m.SaveAnyway = false;
        m.Undo(); m.Validate();
        check(m.SaveTo(file, out msg) && File.ReadAllText(file) == cleanText, "re-save of the fixed drawing differs: " + msg);

        // a save whose content cannot be read back is rolled back: file removed, FilePath and Dirty untouched
        var orphan = new EditorModel(m.Lib);
        orphan.New("selftest_orphan", 4, 4);
        orphan.SaveAnyway = true;
        string orphanFile = Path.Combine(Path.GetTempPath(), "rg_bped_orphan_" + Guid.NewGuid().ToString("N"), "x.jsonc"); // no mod.json above it: its entries are never loaded
        bool dirtyBefore = orphan.Dirty;
        check(!orphan.SaveTo(orphanFile, out msg) && !File.Exists(orphanFile) && orphan.FilePath == null && orphan.Dirty == dirtyBefore, "failed save left the file or changed the model: " + msg);
        try { Directory.Delete(Path.GetDirectoryName(orphanFile)!, true); } catch { }

        // the screen builds (palette with real sprites, panels) and answers key actions
        if (Engine.GetMainLoop() is SceneTree tree)
        {
            Node? host = tree.Root.GetChildren().FirstOrDefault(c => c is App) ?? tree.CurrentScene;
            if (host != null)
            {
                var screen = new BlueprintEditorScreen(null, m);
                host.AddChild(screen);
                screen.Refresh();
                screen.Invoke("blueprinteditor.level_up");
                check(m.Z == 1, "level_up key action did not move to z=+1");
                screen.Invoke("blueprinteditor.level_down");
                screen.Invoke("blueprinteditor.tool_line");
                check(m.Tool == EditTool.Line, "tool_line key action did not select the line tool");
                foreach (var dlg in new[] { "open", "resize", "new", "save_as" })
                {
                    check(screen.Invoke("blueprinteditor." + dlg), "no key action " + dlg);
                    screen.Invoke("blueprinteditor.close"); // closes the dialog
                }
                host.RemoveChild(screen);
                screen.Free();
            }
        }
        GD.Print("selftest: blueprint editor ok (cottage painted, validated, saved, reloaded, screen driven)");
        return tmp;
    }
}
