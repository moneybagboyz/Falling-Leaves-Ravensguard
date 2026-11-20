using System.Linq;
using System.Threading.Tasks;
using Ravensguard.Sim.Live;
using Ravensguard.Sim.Save;
using Ravensguard.Sim.Worlds;

namespace Ravensguard.Game.UI.NewGame;

/// <summary>Which slot a save goes to. An Ironman run ignores this: it only ever writes its one Ironman slot.</summary>
public enum SaveKind { Manual, Autosave }

/// <summary>
/// The run being played: where it saves and what it saves. Created by the region build or by loading a save, handed to the street view. A save is taken in two
/// halves (plan 2.11): <see cref="Snapshot"/> copies the live state on the main thread in a few milliseconds, <see cref="WriteAsync"/> encodes and writes it on a worker.
/// Library, content hash and run name are fixed on the main thread before the work starts, so saving never touches shared state.
/// </summary>
public sealed class RunSession
{
    readonly WorldLibrary _library;
    readonly string _contentHash, _worldId, _runName;
    readonly IReadOnlyList<Ravensguard.Sim.Save.ModEntry> _mods = NewGameServices.InstalledMods();      // fixed at the start of the run (P5-I)
    SaveWriter? _writer; // one per run: its gate keeps two writes (manual, autosave) from touching the directory at once

    public RunSession(WorldState state, string runName, bool ironman, string? lastPath, WorldLibrary library, string contentHash, string worldId, WorldRuntime? runtime = null)
    {
        State = state; _runName = string.IsNullOrEmpty(runName) ? "run" : runName; Ironman = ironman; LastPath = lastPath;
        _library = library; _contentHash = contentHash; _worldId = worldId; Runtime = runtime;
    }

    public RunSession(RunStart start, WorldLibrary library, string contentHash, string worldId)
        : this(start.World, start.RunName, start.Draft.Ironman, start.SavePath, library, contentHash, worldId, start.Runtime) { }

    /// <summary>The running abstract world of this run (its records are copied on its tick worker, between two hours); null for a world without one.</summary>
    public WorldRuntime? Runtime { get; }
    /// <summary>The run's own name: its manual slot, and the prefix of its autosaves and Ironman slot.</summary>
    public string RunName => _runName;
    /// <summary>The run's manual slot as a catalogue entry (what F9 loads), or null when nothing was saved to it yet.</summary>
    public RunEntry? ManualEntry()
    {
        var path = SaveWriter.ManualPath(Directory, _runName);
        return System.IO.File.Exists(path) ? RunCatalog.List(_library, _worldId).FirstOrDefault(e => e.Path == path && e.Readable) : null;
    }
    /// <summary>The save folder of this run's world.</summary>
    public string Directory => _library.RunsDirectory(_worldId);

    /// <summary>The world the run plays in (the chunk store of the street view is State.Chunks).</summary>
    public WorldState State { get; }
    public bool Ironman { get; }
    public string? LastPath { get; private set; }

    /// <summary>
    /// Main thread: takes the hero, clock, torch, explored map and Codex progress as they are now, and has the world's own records (people, economy, regions, changed chunks)
    /// copied on the tick worker between two game hours, so the main thread never waits for it. The task resolves to the capture, which <see cref="WriteAsync"/> encodes.
    /// </summary>
    public Task<LiveCapture> SnapshotAsync(LiveWorld world)
    {
        var resume = world.CaptureResume();
        return Runtime != null ? Runtime.AtBarrier(() => WorldSave.SnapshotLive(State, resume, _contentHash, "0.1", _mods, Ironman))
            : Task.FromResult(WorldSave.SnapshotLive(State, resume, _contentHash, "0.1", _mods, Ironman));
    }

    /// <summary>Worker thread: encodes the snapshot and writes it (Ironman: the Ironman slot with its backups; else the run's manual slot, or the rotating autosaves). Resolves to the file written.</summary>
    public async Task<string> WriteAsync(LiveCapture capture, SaveKind kind)
    {
        var writer = _writer ??= new SaveWriter(_library.RunsDirectory(_worldId), _runName);
        string path = Ironman ? await writer.WriteIronmanAsync(capture).ConfigureAwait(false)
            : kind == SaveKind.Autosave ? await writer.WriteAutosaveAsync(capture).ConfigureAwait(false)
            : await writer.WriteManualAsync(capture, _runName).ConfigureAwait(false);
        if (kind == SaveKind.Manual || Ironman) LastPath = path;
        return path;
    }
}
