using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Godot;
using Ravensguard.Content;
using Ravensguard.Game.World;
using Ravensguard.Sim.Building;

namespace Ravensguard.Game.Tools.BlueprintEditor;

/// <summary>Where the editor reads and writes content: content/base plus an optional user mod folder, and (re)loading the building library from them.</summary>
public static class EditorLibrary
{
    public const string UserModId = "user_blueprints";

    /// <summary>Raised after a save has re-read the content ("hot reload"); screens that show blueprints can refresh.</summary>
    public static event Action<BuildingLibrary>? ContentReloaded;

    public static string BaseDir => StreetView.ContentDir();
    public static string UserModDir => Path.GetFullPath(ProjectSettings.GlobalizePath("user://mods/" + UserModId));

    /// <summary>Creates the user mod folder (with mod.json) on first use and returns it.</summary>
    public static string EnsureUserMod()
    {
        string dir = UserModDir;
        Directory.CreateDirectory(Path.Combine(dir, "blueprints"));
        string manifest = Path.Combine(dir, "mod.json");
        if (!File.Exists(manifest))
            File.WriteAllText(manifest, "{ \"id\": \"" + UserModId + "\", \"name\": \"User blueprints\", \"version\": \"0.1.0\", \"dependencies\": [\"base\"] }\n");
        return dir;
    }

    public static List<string> ModFolders(IEnumerable<string>? extra = null)
    {
        var l = new List<string> { Path.GetFullPath(BaseDir) };
        if (File.Exists(Path.Combine(UserModDir, "mod.json"))) l.Add(UserModDir);
        if (extra != null) foreach (var e in extra) { var f = Path.GetFullPath(e); if (!l.Contains(f)) l.Add(f); }
        return l;
    }

    /// <summary>Folder of the mod that holds <paramref name="file"/> (the nearest parent with a mod.json), or null.</summary>
    public static string? ModRootOf(string file)
    {
        var d = Path.GetDirectoryName(Path.GetFullPath(file));
        while (!string.IsNullOrEmpty(d))
        {
            if (File.Exists(Path.Combine(d, "mod.json"))) return d;
            d = Path.GetDirectoryName(d);
        }
        return null;
    }

    public static BuildingLibrary Load(IEnumerable<string> folders, out string? error)
    {
        var r = new ModLoader(ContentTypeRegistry.Default).Load(folders);
        error = r.Success ? null : r.Report.ToString();
        if (!r.Success) throw new InvalidOperationException("content failed to load: " + error);
        return BuildingLibrary.FromDatabase(r.Database);
    }

    public static void RaiseReloaded(BuildingLibrary lib) => ContentReloaded?.Invoke(lib);

    /// <summary>All blueprint files the editor can write into: those in content/base/blueprints and in the user mod folder.</summary>
    public static List<string> BlueprintFiles()
    {
        var l = new List<string>();
        foreach (var dir in new[] { Path.Combine(BaseDir, "blueprints"), Path.Combine(UserModDir, "blueprints") })
            if (Directory.Exists(dir)) l.AddRange(Directory.GetFiles(dir, "*.jsonc").OrderBy(f => f, StringComparer.Ordinal));
        return l;
    }

    public static string Describe(string file)
    {
        var full = Path.GetFullPath(file);
        var user = UserModDir + Path.DirectorySeparatorChar;
        if (full.StartsWith(user, StringComparison.Ordinal)) return "user/" + Path.GetFileName(full);
        return "base/" + Path.GetFileName(full);
    }
}
