using System;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Save;
using Ravensguard.Sim.Worlds;

namespace Ravensguard.Game.UI.NewGame;

/// <summary>
/// The run being played: where it saves and what it saves. Created by the region build, handed to the street view, which calls <see cref="Save"/> from its pause menu
/// (and on quit for an Ironman run). Library, content hash and run name are fixed on the main thread before the build starts, so saving never touches shared state.
/// </summary>
public sealed class RunSession
{
    readonly WorldState _state;
    readonly WorldLibrary _library;
    readonly string _contentHash, _worldId;

    public RunSession(RunStart start, WorldLibrary library, string contentHash, string worldId)
    {
        Start = start; _state = start.World; _library = library; _contentHash = contentHash; _worldId = worldId;
        LastPath = start.SavePath;
    }

    public RunStart Start { get; }
    public bool Ironman => Start.Draft.Ironman;
    public string? LastPath { get; private set; }

    /// <summary>Writes the run as it is now: the live clock, the chunks the street view changed, the Codex the hero has opened. Returns the file written, or the reason it failed.</summary>
    public (bool Ok, string Message) Save(GameTime now, SimSpeed speed, Ravensguard.Sim.Codex.CodexBook? codex)
    {
        try
        {
            var live = new WorldState
            {
                ContinentSettings = _state.ContinentSettings, HistorySettings = _state.HistorySettings, RegionSettings = _state.RegionSettings,
                EmbarkCellX = _state.EmbarkCellX, EmbarkCellY = _state.EmbarkCellY, Continent = _state.Continent, History = _state.History, Population = _state.Population,
                Clock = new SimClock(now) { Speed = speed }, Ids = _state.Ids, Chunks = _state.Chunks, Chronicle = _state.Chronicle,
                Hero = _state.Hero, Codex = codex?.ToProgress() ?? _state.Codex,
            };
            var file = WorldSave.Capture(live, _contentHash, "0.1", null, Ironman);
            var writer = new SaveWriter(_library.RunsDirectory(_worldId));
            string path = Ironman
                ? writer.WriteIronmanAsync(file).GetAwaiter().GetResult()
                : writer.WriteManualAsync(file, string.IsNullOrEmpty(Start.RunName) ? "run" : Start.RunName).GetAwaiter().GetResult();
            LastPath = path;
            return (true, path);
        }
        catch (Exception e) { return (false, e.Message); }
    }
}
