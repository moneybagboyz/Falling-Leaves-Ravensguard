using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Ravensguard.Sim.Armies;
using Ravensguard.Sim.Naval;
using Ravensguard.Sim.Worlds;

namespace Ravensguard.Game.World;

/// <summary>
/// The street view's side of the bell and the live beach fight (P8-LW-2, live-war 3.2), the way <c>StreetView.Assault.cs</c> does the live storm: it turns the live switch on, takes the bells the tick worker queued and raises them as the hero's
/// <c>BellRung</c> interrupt, asks for a beach fight the hero's bubble has opened (at a barrier, never across threads) and takes it up as a battle, and when the bubble has closed ends the fight on the sand at a barrier. Nothing here decides
/// anything: <see cref="BeachFights"/> does. A save made inside a beach fight is the battle's own save; the raid then finds no live fight on resume and is fought abstractly (the fallback).
/// </summary>
public partial class StreetView
{
    LiveBeach? _liveBeach; Task<LiveBeach?>? _beachAsk; Task<List<BellRing>>? _bellAsk; bool _beachOn; int _beachAskedFrame = -1000;

    static BeachFights? BeachOf(WorldRuntime rt) => rt.State.HomeRecords?.Sim.Get<ArmyWorld>()?.BeachFights;

    /// <summary>Each frame, before the battle's own: the switch, the bells, the question, and the start of a beach fight the sim has opened.</summary>
    void BeachFrame()
    {
        if (Runtime is not { } rt || BeachOf(rt) is not { } b) return;
        if (!_beachOn) { _beachOn = true; _ = Bar(rt, () => { b.GameDrivesLive = true; return 0; }); }
        if (_bellAsk is { IsCompleted: true } bells)
        {
            _bellAsk = null;
            if (bells.IsCompletedSuccessfully && _travel != null && bells.Result.Count > 0) BeachFights.RaiseBells(_travel.Bus, bells.Result, _ => "");
        }
        if (_beachAsk is { } ask)
        {
            if (!ask.IsCompleted) return;
            _beachAsk = null;
            if (ask.IsCompletedSuccessfully && ask.Result is { } lb && _fightView == null && _world.Fight == null && !_battleOpen) StartLiveBeach(lb);
            return;
        }
        if (_frames - _beachAskedFrame < 30) return;
        _beachAskedFrame = _frames;
        _bellAsk ??= Bar(rt, () => b.TakeBells());
        if (_battleOpen || _fightView != null || _world.Fight != null) return;
        _beachAsk = Bar(rt, () => b.Live.Values.FirstOrDefault(x => !x.Bubble.Closed && !ReferenceEquals(x, _liveBeach)));
    }

    void StartLiveBeach(LiveBeach lb)
    {
        if (!StartBattle(lb.Bubble, lb.Bubble.Hero)) return;
        _liveBeach = lb;
    }

    /// <summary>The battle view is let go: the raid hears how the fight on the sand ended at the next barrier.</summary>
    void LiveBeachClosed()
    {
        if (_liveBeach == null) return;
        _liveBeach = null;
        if (Runtime is not { } rt || rt.State.HomeRecords?.Sim.Get<ArmyWorld>() is not { } aw) return;
        _ = Bar(rt, () => aw.BeachFights.CloseClosed(new ArmyEnv(aw.W, null, rt.HourDone + 1)));
    }
}
