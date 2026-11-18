using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Save;
using Ravensguard.Sim.Weather;
using Ravensguard.Sim.Worlds;

namespace Ravensguard.Game.UI.NewGame;

/// <summary>One save file of a run: which world it belongs to, when it was written and how far the run had got.</summary>
public sealed record RunEntry(string WorldId, string WorldName, string Path, string Slot, DateTime Saved, string Character, bool Ironman, bool Autosave, long GameSeconds, bool Readable);

/// <summary>The saved runs of the world library: what the main menu's Continue and Load list. Pure file listing (headers only), safe to call from tests.</summary>
public static class RunCatalog
{
    public const string AutosavePrefix = "autosave_";

    /// <summary>Every save of one world (or of all worlds), newest first.</summary>
    public static List<RunEntry> List(WorldLibrary lib, string? worldId = null)
    {
        var res = new List<RunEntry>();
        var reader = new SaveReader();
        foreach (var w in lib.List())
        {
            if (worldId != null && w.Id != worldId) continue;
            foreach (var file in w.Runs)
            {
                var path = Path.Combine(lib.RunsDirectory(w.Id), file);
                string slot = Path.GetFileNameWithoutExtension(file);
                DateTime saved; try { saved = File.GetLastWriteTimeUtc(path); } catch (IOException) { continue; }
                bool readable = true, ironman = false; long seconds = 0;
                try { var h = reader.ReadHeader(path); ironman = h.Ironman; seconds = h.GameSeconds; }
                catch (Exception e) when (e is SaveException or IOException or UnauthorizedAccessException) { readable = false; }
                string who = lib.ReadRunInfo(w.Id, slot)?.CharacterName ?? "";
                res.Add(new RunEntry(w.Id, w.Name, path, slot, saved, who, ironman, slot.StartsWith(AutosavePrefix, StringComparison.Ordinal), seconds, readable));
            }
        }
        return Newest(res);
    }

    /// <summary>The most recently written readable save, or null.</summary>
    public static RunEntry? Latest(WorldLibrary lib) => List(lib).FirstOrDefault(e => e.Readable);

    /// <summary>Newest first; ties by world, then slot name, so the order never depends on directory order.</summary>
    public static List<RunEntry> Newest(IEnumerable<RunEntry> entries)
        => entries.OrderByDescending(e => e.Saved).ThenBy(e => e.WorldId, StringComparer.Ordinal).ThenBy(e => e.Slot, StringComparer.Ordinal).ToList();

    /// <summary>The name a loaded run saves its manual slot under: the file's own name, except for the autosave and Ironman slots, which a manual save must not overwrite.</summary>
    public static string ManualNameFor(string slot, string? hero, int existingSaves)
    {
        if (slot.Length > 0 && !slot.StartsWith(AutosavePrefix, StringComparison.Ordinal) && slot != "ironman") return slot;
        return WorldLibrary.Slug(string.IsNullOrWhiteSpace(hero) ? "run" : hero) + "-" + (existingSaves + 1);
    }

    /// <summary>"Aldric (autosave 1)  /  17:00, 3rd of Spring ..." style one-line text for a save's game date.</summary>
    public static string GameDate(RunEntry e) => e.Readable ? new Calendar().Describe(new GameTime(e.GameSeconds)) : L.T("RUNS_UNREADABLE");

    public static string Title(RunEntry e)
    {
        string name = e.Autosave ? L.F("RUNS_AUTOSAVE", e.Slot[AutosavePrefix.Length..]) : e.Slot;
        string who = e.Character.Length > 0 ? e.Character + " - " : "";
        return who + name + (e.Ironman ? "  [" + L.T("RUNS_IRONMAN") + "]" : "");
    }
}
