using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Ravensguard.Sim.Armies;
using Ravensguard.Sim.Worlds;

namespace Ravensguard.Game.World;

/// <summary>
/// The street view's side of the live road battle (P8-LW-4), the way <c>StreetView.Beach.cs</c> does the beach fight: it turns the live switch on, asks at a barrier for a road battle the hero's bubble has opened and takes it up as a battle
/// (F1 to F4 and the order wheel command its formations), and when the bubble has closed ends the fight at the next barrier. Nothing here decides anything: <see cref="RoadBattles"/> does. A save made inside a road battle is the battle's own save;
/// the campaign finds no live fight on resume and opens a fresh one at the next hour if the hero is still there (else it is fought abstractly).
/// </summary>
public partial class StreetView
{
    LiveRoad? _liveRoad; Task<LiveRoad?>? _roadAsk; bool _roadOn; int _roadAskedFrame = -1000;

    static RoadBattles? RoadOf(WorldRuntime rt) => rt.State.HomeRecords?.Sim.Get<ArmyWorld>()?.RoadBattles;

    /// <summary>Each frame, before the battle's own: the switch, the question, and the start of a road battle the sim has opened.</summary>
    void RoadBattleFrame()
    {
        if (Runtime is not { } rt || RoadOf(rt) is not { } r) return;
        if (!_roadOn) { _roadOn = true; _ = Bar(rt, () => { r.GameDrivesLive = true; return 0; }); }
        if (_roadAsk is { } ask)
        {
            if (!ask.IsCompleted) return;
            _roadAsk = null;
            if (ask.IsCompletedSuccessfully && ask.Result is { } lr && _fightView == null && _world.Fight == null && !_battleOpen) StartLiveRoad(lr);
            return;
        }
        if (_frames - _roadAskedFrame < 30 || _battleOpen || _fightView != null || _world.Fight != null) return;
        _roadAskedFrame = _frames;
        _roadAsk = Bar(rt, () => r.Live.Values.FirstOrDefault(x => !x.Bubble.Closed && !ReferenceEquals(x, _liveRoad)));
    }

    void StartLiveRoad(LiveRoad lr)
    {
        if (!StartBattle(lr.Bubble, lr.Bubble.Hero)) return;
        _liveRoad = lr;
    }

    /// <summary>The battle view is let go: the hosts hear how the fight ended at the next barrier.</summary>
    void LiveRoadClosed()
    {
        if (_liveRoad == null) return;
        _liveRoad = null;
        if (Runtime is not { } rt || rt.State.HomeRecords?.Sim.Get<ArmyWorld>() is not { } aw) return;
        _ = Bar(rt, () => aw.RoadBattles.CloseClosed(new ArmyEnv(aw.W, null, rt.HourDone + 1)));
    }
}
