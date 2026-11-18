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
    SaveWriter? _writer; // one per run: its gate keeps two writes (manual, autosave) from touching the directory at once

    public RunSession(WorldState state, string runName, bool ironman, string? lastPath, WorldLibrary library, string contentHash, string worldId)
    {
        State = state; _runName = string.IsNullOrEmpty(runName) ? "run" : runName; Ironman = ironman; LastPath = lastPath;
        _library = library; _contentHash = contentHash; _worldId = worldId;
    }

    public RunSession(RunStart start, WorldLibrary library, string contentHash, string worldId)
        : this(start.World, start.RunName, start.Draft.Ironman, start.SavePath, library, contentHash, worldId) { }

    /// <summary>The world the run plays in (the chunk store of the street view is State.Chunks).</summary>
    public WorldState State { get; }
    public bool Ironman { get; }
    public string? LastPath { get; private set; }

    /// <summary>Main thread: copies the hero, clock, torch, explored map, Codex progress, people and changed chunks as they are now.</summary>
    public LiveCapture Snapshot(LiveWorld world) => WorldSave.SnapshotLive(State, world, _contentHash, "0.1", null, Ironman);

    /// <summary>Worker thread: encodes the snapshot and writes it (Ironman: the Ironman slot with its backups; else the run's manual slot, or the rotating autosaves). Resolves to the file written.</summary>
    public async Task<string> WriteAsync(LiveCapture capture, SaveKind kind)
    {
        var writer = _writer ??= new SaveWriter(_library.RunsDirectory(_worldId));
        string path = Ironman ? await writer.WriteIronmanAsync(capture).ConfigureAwait(false)
            : kind == SaveKind.Autosave ? await writer.WriteAutosaveAsync(capture).ConfigureAwait(false)
            : await writer.WriteManualAsync(capture, _runName).ConfigureAwait(false);
        if (kind == SaveKind.Manual || Ironman) LastPath = path;
        return path;
    }
}
