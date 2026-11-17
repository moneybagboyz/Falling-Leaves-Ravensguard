using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Godot;
using Ravensguard.Content;
using Ravensguard.Sim.WorldGen;
using Ravensguard.Sim.WorldGen.History;
using Ravensguard.Sim.WorldGen.Region;
using Ravensguard.Sim.Worlds;

namespace Ravensguard.Game.UI.NewGame;

/// <summary>Loaded base content plus its hash (the world library records the hash a world was made with).</summary>
public sealed record ContentBundle(ContentDatabase Db, string Hash, string Dir);

/// <summary>What the player has chosen so far in the new-game flow. Plain data; screens read and write it, nothing else does.</summary>
public sealed class NewGameDraft
{
    public string Name = "";
    public bool Female;
    public string OriginId = "";
    public bool Ironman;
    /// <summary>Library world folder id, or null for a new world.</summary>
    public string? LibraryWorldId;
    public string WorldName = "";
    public ContinentSettings Continent = new();
    public HistorySettings History = new();
    /// <summary>The chosen embark window; null means the Quick Start auto pick.</summary>
    public EmbarkScore? Embark;
    public bool ChooseRegion;
}

/// <summary>Content, library and background-job plumbing shared by the new-game screens.</summary>
public static class NewGameServices
{
    static string? _libraryOverride;
    static ContentBundle? _content;
    static LoadedWorld? _cached;

    /// <summary>Dev/test hook: <c>--ng-throttle-ms N</c> sleeps N ms per simulated year so the creation screen can be watched or photographed.</summary>
    public static int ThrottleMsPerYear { get; set; }

    public static void Configure(string[] args)
    {
        int i = Array.IndexOf(args, "--ng-library");
        if (i >= 0 && i + 1 < args.Length) _libraryOverride = args[i + 1];
        i = Array.IndexOf(args, "--ng-throttle-ms");
        if (i >= 0 && i + 1 < args.Length && int.TryParse(args[i + 1], out var ms)) ThrottleMsPerYear = ms;
    }

    public static WorldLibrary Library => new(_libraryOverride ?? ProjectSettings.GlobalizePath("user://worlds"));

    public static string NowText() => Time.GetDatetimeStringFromSystem(false, true);

    public static ContentBundle Content()
    {
        if (_content != null) return _content;
        foreach (var dir in new[]
        {
            ProjectSettings.GlobalizePath("res://../../content/base"),
            Path.Combine(Path.GetDirectoryName(OS.GetExecutablePath()) ?? ".", "content/base"),
        })
        {
            var full = Path.GetFullPath(dir);
            if (!Directory.Exists(full)) continue;
            var cache = ProjectSettings.GlobalizePath("user://content_cache");
            var r = new ModLoader(ContentTypeRegistry.Default, BaseVocabulary.Create()).Load([full], cache);
            if (!r.Success) throw new InvalidOperationException("content failed to load: " + r.Report);
            return _content = new ContentBundle(r.Database, r.Hash ?? "", full);
        }
        throw new InvalidOperationException("content/base not found");
    }

    public static NewGameDraft NewDraft()
    {
        var c = Content();
        var d = new NewGameDraft
        {
            Continent = ContinentSettings.LoadDefaults(c.Dir),
            History = HistorySettings.LoadDefaults(c.Dir),
            OriginId = Origins().FirstOrDefault()?.Id ?? "",
        };
        Reseed(d, (ulong)Random.Shared.Next(1, 99_999_999));
        return d;
    }

    public static void Reseed(NewGameDraft d, ulong seed)
    {
        d.Continent.Seed = seed;
        d.History.Seed = seed;
        d.WorldName = WorldLibrary.DefaultName(seed);
    }

    public static System.Collections.Generic.List<OriginDef> Origins() => Content().Db.All<OriginDef>().OrderBy(o => o.Order).ThenBy(o => o.Id, StringComparer.Ordinal).ToList();

    public static LoadedWorld? Cached(string id) => _cached?.Info.Id == id ? _cached : null;
    public static void Remember(LoadedWorld w) => _cached = w;
    public static void Forget(string id) { if (_cached?.Info.Id == id) _cached = null; }
}

/// <summary>A world creation or library load on a worker thread. The UI drains <see cref="Progress"/> every frame.</summary>
public sealed class WorldJob : IDisposable
{
    readonly CancellationTokenSource _cts = new();
    public ConcurrentQueue<WorldGenProgress> Progress { get; } = new();
    public LoadedWorld? Result { get; private set; }
    public Exception? Error { get; private set; }
    public bool Finished { get; private set; }

    void Report(WorldGenProgress p)
    {
        Progress.Enqueue(p);
        if (NewGameServices.ThrottleMsPerYear > 0 && p.Phase == WorldGenPhase.History && p.Entry == null) Thread.Sleep(NewGameServices.ThrottleMsPerYear);
    }

    static WorldJob Run(Func<WorldJob, LoadedWorld> work)
    {
        var job = new WorldJob();
        Task.Run(() =>
        {
            try { job.Result = work(job); }
            catch (OperationCanceledException) { job.Error = new OperationCanceledException(); }
            catch (Exception e) { job.Error = e; }
            finally { job.Finished = true; }
        });
        return job;
    }

    public static WorldJob Create(string name, ContinentSettings cs, HistorySettings hs, string contentHash, string created)
    {
        var lib = NewGameServices.Library;
        return Run(j => lib.Create(name, cs, hs, contentHash, created, j.Report, j._cts.Token));
    }

    public static WorldJob Open(string id, string contentHash)
    {
        var lib = NewGameServices.Library;
        return Run(j => lib.Load(id, contentHash, j.Report, j._cts.Token));
    }

    public void Cancel() => _cts.Cancel();
    public void Dispose() => _cts.Cancel();
}
