using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Ravensguard.Game.World.Combat;
using Ravensguard.Sim.Battle;
using Ravensguard.Sim.Combat.Melee2;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Save;
using Ravensguard.Sim.World;
using Ravensguard.Sim.Worlds;

namespace Ravensguard.Game.World;

/// <summary>
/// The street view's side of a battle in the bubble (P7-BB-C, battle-bubble-and-formations 4.1, 7, 8): a <see cref="BattleBubble"/> becomes the live fight (the live tick steps it, the session recorder keeps its start),
/// the combat view opens on it with the crowd layer, a save inside it writes the fight and the <c>battle</c> section, a load takes it up again where it stood, and when it closes its chronicle entries go to the
/// world at a barrier. Nothing here decides anything: the bubble does. Until the armies of AM exist, the dev console's <c>battle</c> command is what opens one.
/// </summary>
public partial class StreetView
{
    bool _battleOpen; int _battleEnded = -1;

    /// <summary>Opens the battle in the live world: the bubble is attached to the live tier and the recorder, the view opens on it, saves write it. False when a fight is already open.</summary>
    bool StartBattle(BattleBubble bubble, int hero)
    {
        if (_fightView != null || _world.Fight != null) return false;
        bubble.Chronicle = BattleChronicleToWorld();
        _rec.StartBattle(bubble, hero);
        _lod.PausedAt = (x, y) => bubble.IsPausedAt(x, y);                    // the ambient simulation next to the box holds while the battle lasts (4.1)
        _rec.SetSpeed(SimSpeed.X1);
        OpenFightView(bubble.W, hero, bubble);
        _battleOpen = true; _battleEnded = -1;
        HookBattleSaves(bubble.W);
        return true;
    }

    void HookBattleSaves(FightWorld w)
    {
        if (Runtime is not { } rt) return;
        rt.State.FightSource = () => _world.Fight is { } f ? FightRecord.Capture(f, "raid", "battle") : null;
        rt.State.BattleSource = () => _world.Battle is { } b ? BattleRecord.Capture(b) : null;
    }

    /// <summary>A save made inside a battle: the fight record is rebuilt where it stood and the bubble over it (soldiers entering, reserves, waves, the box); the view opens on it paused.</summary>
    bool ResumeBattle(WorldRuntime rt, FightRecord rec)
    {
        if (rt.State.Battle is not { } br || FightContentOf() is not { } fc) return false;
        rt.State.Battle = null;
        var ledger = rt.State.Wounds ?? _world.Wounds;
        if (ledger == null) { ledger = new Ravensguard.Sim.Combat.Wounds.WoundLedger(rt.State.Seed, _world.Clock.Now.Seconds); _world.AttachWounds(ledger); }
        var fight = rec.Restore(fc, ledger);
        var bubble = BattleBubble.Restore(fight, br, rt.State.Mounts);
        ResumeAssault(rt, fight, bubble);                                       // P8-LW-1: a save made in a live assault takes its layer up again
        int hero = bubble.Hero;
        bubble.Chronicle = BattleChronicleToWorld();
        _rec.StartBattle(bubble, hero);
        _lod.PausedAt = (x, y) => bubble.IsPausedAt(x, y);                    // the ambient simulation next to the box holds while the battle lasts (4.1)
        _rec.SetSpeed(SimSpeed.Paused);
        OpenFightView(fight, hero, bubble);
        _battleOpen = true; _battleEnded = -1;
        HookBattleSaves(fight);
        _router?.ShowToast(L.T("fr_resumed"), 4f);
        return true;
    }

    /// <summary>Each frame of a battle: when the bubble has closed, its result is shown for a couple of seconds and the view is let go.</summary>
    void BattleFrame()
    {
        if (_fightView == null) { _battleOpen = false; return; }
        var b = _world.Battle;
        if (b == null) { CloseBattle(); return; }
        if (!b.Closed) return;
        if (_battleEnded < 0) { _battleEnded = _frames; FlushBattleChronicle(); _router?.ShowToast(L.T(b.Result?.OutcomeKey ?? BattleBubble.OutDrawn), 5f); return; }
        if (_frames - _battleEnded < 180) return;
        _fightView.FinishLog();
        CloseBattle();
    }

    void CloseBattle()
    {
        _battleOpen = false; _battleEnded = -1;
        CloseRoadFight();
        if (Runtime is { } rt) { rt.State.BattleSource = null; rt.State.Battle = null; }
        _lod.PausedAt = null;                                                   // the ambient chunks around the box resume
        LiveAssaultClosed();                                                    // P8-LW-1: a live assault ends its siege at the next barrier
        LiveBeachClosed();                                                      // P8-LW-2: a beach fight ends at the next barrier
        LiveRoadClosed();                                                       // P8-LW-4: a road battle ends at the next barrier
    }

    /// <summary>
    /// The bubble's chronicle sink for the live world. Entries are made on the live thread but the chronicle and its ids belong to the tick worker, so they are held here and written at a barrier once the battle
    /// has closed, each given its real id there and the causes that named an earlier one of them re-pointed (the battle entry is the cause of the others).
    /// </summary>
    readonly List<ChronicleEntry> _heldChronicle = [];
    ulong _tempChronicle = 1UL << 60;

    BattleChronicleSink BattleChronicleToWorld() => (when, type, key, ps, subjects, causes, data) =>
    {
        var id = new EntityId(++_tempChronicle);
        _heldChronicle.Add(new ChronicleEntry(id, when, type, [.. subjects], [.. causes], data.ToDictionary(d => d.Key, d => d.Value), key, [.. ps]));
        return id;
    };

    void FlushBattleChronicle()
    {
        if (_heldChronicle.Count == 0 || Runtime is not { } rt) return;
        var batch = _heldChronicle.ToArray(); _heldChronicle.Clear();
        _ = Bar(rt, () =>
        {
            var real = new Dictionary<ulong, EntityId>();
            foreach (var e in batch)
            {
                var rid = rt.State.Ids.Allocate(IdKind.Event);
                real[e.Id.Value] = rid;
                var cs = e.Causes.Select(c => c.EventId is { } ev && real.TryGetValue(ev.Value, out var r) ? c with { EventId = r } : c).ToList();
                rt.State.Chronicle.Add(e with { Id = rid, Causes = cs });
            }
            return batch.Length;
        });
    }

    /// <summary>The dev console's <c>battle [per side] [battle size]</c>: two armies of foot in formations meet beside the hero, the first led by the hero (Leadership 50), the rest in reserve to come as waves.</summary>
    string DevBattle(string[] a)
    {
        var fc = FightContentOf();
        if (fc == null) return "no content";
        if (_fightView != null || _world.Fight != null) return "a fight is open";
        int per = a.Length > 0 && int.TryParse(a[0], out var p) ? Math.Clamp(p, 20, 3000) : 400, cap = a.Length > 1 && int.TryParse(a[1], out var c) ? c : 0;
        var op = BubbleScenario.Opening(per, per >= 600 ? 100 : 60, Runtime?.State.Seed ?? 1, hero: true, cap: cap);
        op.GameSec = _world.Clock.Now.Seconds;
        var (hx, hy) = _world.WorldMm;
        op.OriginMm = (hx, hy);
        var bubble = BattleBubble.Open(fc, op);
        return StartBattle(bubble, bubble.Hero) ? $"a battle of {per} a side opened (battle size {bubble.CapTotal}, box {(bubble.Box.X1 - bubble.Box.X0) / 1000} x {(bubble.Box.Y1 - bubble.Box.Y0) / 1000} m)" : "could not open it";
    }
}
