using System;
using System.Linq;
using System.Threading.Tasks;
using Ravensguard.Sim.Battle;
using Ravensguard.Sim.Combat.Melee2;
using Ravensguard.Sim.Save;
using Ravensguard.Sim.Siege;
using Ravensguard.Sim.Worlds;

namespace Ravensguard.Game.World;

/// <summary>
/// The street view's side of the live siege assault (P8-LW-1, live-war 3.1): it turns the live switch on, asks the tick worker for an assault the hero's bubble has opened (at a barrier, never across threads), takes it up as a battle (the battle path:
/// the bubble is stepped by the live tick, the view opens on it with the assault orders on the wheel), writes it into the <c>sieges</c> section while it lasts, takes a saved one up again, and when the bubble has closed ends the siege at a barrier.
/// Nothing here decides anything: the bubble and the assault layer do.
/// </summary>
public partial class StreetView
{
    LiveAssault? _liveAssault; Task<LiveAssault?>? _assaultAsk; bool _assaultsOn; int _assaultAskedFrame = -1000;

    static SiegeAssaults? AssaultsOf(WorldRuntime rt) => rt.State.HomeRecords?.Sim.Get<SiegeWorld>()?.Assaults;

    /// <summary>Each frame, before the battle's own: the switch, the question, and the start of a live assault the sim has opened.</summary>
    void AssaultFrame()
    {
        if (Runtime is not { } rt || AssaultsOf(rt) is not { } a) return;
        if (!_assaultsOn) { _assaultsOn = true; _ = Bar(rt, () => { a.GameDrivesLiveAssaults = true; return 0; }); }
        if (_assaultAsk is { } ask)
        {
            if (!ask.IsCompleted) return;
            _assaultAsk = null;
            if (ask.IsCompletedSuccessfully && ask.Result is { } la && _fightView == null && _world.Fight == null && !_battleOpen) StartLiveAssault(rt, la);
            return;
        }
        if (_battleOpen || _fightView != null || _world.Fight != null || _frames - _assaultAskedFrame < 30) return;
        _assaultAskedFrame = _frames;
        _assaultAsk = Bar(rt, () => a.Live.Values.FirstOrDefault(x => !x.Bubble.Closed && !ReferenceEquals(x, _liveAssault)));
    }

    void StartLiveAssault(WorldRuntime rt, LiveAssault la)
    {
        if (!StartBattle(la.Bubble, la.Bubble.Hero)) return;
        _liveAssault = la;
        rt.State.AssaultSource = () => _liveAssault?.Layer.Capture();
    }

    /// <summary>The battle view is let go: the siege hears how the assault ended at the next barrier.</summary>
    void LiveAssaultClosed()
    {
        if (_liveAssault == null) return;
        _liveAssault = null;
        if (Runtime is not { } rt) return;
        rt.State.AssaultSource = null; rt.State.Assault = null;
        if (AssaultsOf(rt) is { } a) _ = Bar(rt, () => a.CloseClosed());
    }

    /// <summary>A save made inside a live assault: the layer is laid over the restored fight again, on the walls of its siege, and handed back to the siege's assaults. Null when the save had no assault, or its siege or walls are gone.</summary>
    AssaultLayer? ResumeAssault(WorldRuntime rt, FightWorld fight, BattleBubble bubble)
    {
        if (rt.State.Assault is not { } rec || AssaultsOf(rt) is not { } a) return null;
        rt.State.Assault = null;
        var sw = rt.State.HomeRecords?.Sim.Get<SiegeWorld>();
        if (sw?.Peek?.Sieges.TryGetValue(rec.Siege, out var row) != true || sw.WallsOf?.Invoke(row!.Place) is not { } walls) return null;
        a.GameDrivesLiveAssaults = true; _assaultsOn = true;
        var layer = AssaultLayer.Restore(rec, fight, walls.World);
        a.Adopt(rec.Siege, bubble, layer);
        _liveAssault = a.Live[rec.Siege];
        rt.State.AssaultSource = () => _liveAssault?.Layer.Capture();
        return layer;
    }
}
