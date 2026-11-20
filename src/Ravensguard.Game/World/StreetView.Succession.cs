using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Ravensguard.Game.World.Combat;
using Ravensguard.Sim.Live;
using Ravensguard.Sim.Travel;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Succession;
using Ravensguard.Sim.Trade;

namespace Ravensguard.Game.World;

/// <summary>
/// [succession] The hero's death in play (morale-capture-succession 4.8, 7; P4 close-out): the succession screen shows who continues (Ironman: the first of the kinship order, nobody to pick;
/// otherwise the whole order, and loading the last save); the choice goes to the barrier (<see cref="SuccessionDesk"/>: the estate is settled, the heir becomes the hero), and once it is applied
/// the hero is put at the heir's home and play goes on as the heir. With nobody left the run ends (the world persists in the library). No state here but which screen is up.
/// </summary>
public partial class StreetView
{
    SuccessionView? _successionView;
    Task<(List<SuccessionCandidate> Heirs, EstateSummary Estate)>? _heirsTask;
    long _successionSeq = -1;
    (long X, long Y) _deathPlace;
    long _deathSec;
    string _deathCause = "";
    bool _runEndedPending;

    /// <summary>The hero is dead (a road fight, below ground): the kinship order is read at the barrier and the succession screen follows.</summary>
    void HeroDied(string causeKey)
    {
        if (Runtime is not { } rt || rt.State.Party is not { } party || _successionView != null || _heirsTask != null) { _router?.ReturnToMainMenu(); return; }
        _deathPlace = HeroRegionMm(); _deathSec = _world.Clock.Now.Seconds; _deathCause = causeKey;
        int day = (int)(_deathSec / 86400);
        _rec.SetSpeed(SimSpeed.Paused);
        var content = rt.State.Content;
        _heirsTask = rt.AtBarrier(() =>
        {
            var h = SuccessionDesk.Candidates(rt.State, party, day);
            // P5-H: the estate as it would settle for the first heir (the screen's "You inherit" and "Goes to the lord")
            var est = content != null ? SuccessionDesk.Estate(rt.State, party, content, h.FirstOrDefault(), day) : EstateSummary.Empty;
            return (h, est);
        });
    }

    /// <summary>Each frame: the order coming back, the heir's command applied.</summary>
    void SuccessionFrame()
    {
        // the hero bled out or died of a wound outside a fight or a crawl (on the road, in captivity): the wounds go on in the ledger after the fight now (review finding 3)
        if (_fightView == null && _crawl == null && _successionView == null && _heirsTask == null && _successionSeq < 0
            && Runtime?.State.Wounds?.Find(Ravensguard.Sim.Economy.EconomyState.HeroOwner.Value) is { Dead: true })
            HeroDied(Runtime.State.Justice is { HeroExecuted: not 0 } ? "su_cause_executed" : "su_cause_wounds");      // P6-C1: the court's rope has its own words
        // P6-B3 (family 4.6): old age, or the childbed, has taken the hero; the heir he raised takes over
        if (_fightView == null && _crawl == null && _successionView == null && _heirsTask == null && _successionSeq < 0 && _familyLink is { DeathPending: true } fl)
            HeroDied(fl.CauseKey);
        if (_heirsTask is { IsCompleted: true } t)
        {
            _heirsTask = null;
            var heirs = t.IsCompletedSuccessfully ? t.Result.Heirs : [];
            var estate = t.IsCompletedSuccessfully ? t.Result.Estate : null;
            Layer();
            bool ironman = Run is { Ironman: true };
            _successionView = new SuccessionView(_hero?.Name ?? "", L.T(_deathCause), ironman ? heirs.Take(1).ToList() : heirs, heirs.Count, ironman, PickHeir,
                !ironman && Run?.ManualEntry() != null ? QuickLoad : null, EndLine, estate) { Name = "SuccessionView" };
            _mcLayer!.AddChild(_successionView);
        }
        if (_successionSeq < 0 || _heroLink?.View is not { } v) return;
        var r = v.Results.FirstOrDefault(x => x.Seq == _successionSeq);
        if (r == null) return;
        _successionSeq = -1;
        _successionView?.QueueFree(); _successionView = null;
        if (_runEndedPending || !r.Ok) { _runEndedPending = false; _router?.ShowToast(r.Text, 6f); _leaving = true; _router?.ReturnToMainMenu(); return; }
        TakeUpTheHeir(r);
    }


    void PickHeir(SuccessionCandidate c)
    {
        if (_heroLink == null) return;
        _successionSeq = _heroLink.Issue(SuccessionDesk.HeirCommand(c.Person, _deathPlace.X, _deathPlace.Y, _deathSec)).Seq;
        _heirRow = c.PersonIndex;
        _rec.SetSpeed(SimSpeed.X8);                                                  // to the barrier of this hour
    }

    void EndLine()
    {
        if (_heroLink == null) { _router?.ReturnToMainMenu(); return; }
        _runEndedPending = true;
        _successionSeq = _heroLink.Issue(SuccessionDesk.EndCommand(_deathPlace.X, _deathPlace.Y, _deathSec)).Seq;
        _rec.SetSpeed(SimSpeed.X8);
    }

    int _heirRow = -1;

    /// <summary>The heir is the hero now: their record, the clothes they stand in, at their home (the estate lies where the old hero fell).</summary>
    void TakeUpTheHeir(HeroResult r)
    {
        if (Runtime is not { } rt) return;
        _hero = rt.State.Hero;
        if (_hero == null) return;
        long now = _world.Clock.Now.Seconds;
        // the old hero's road is over: the encounter that killed him is settled and the journey given up (recorded, as the cancel key does)
        if (_aftermathEv != null && _travel is { } t0)
        {
            _aftermathEv = null; t0.ScreenPending = false;
            FinishEncounter(new EncounterOutcome(EncounterChoice.Fight, "aftermath", 0, EntityId.None, EntityId.None, EncounterOutcomes.FleeDelaySec, ""), now, false);
        }
        _aftermathEv = null;
        if (_travel is { Journey: not null } t1) { t1.Cancel(now); _rec.Action(Ravensguard.Sim.Diagnostics.HeroAction.TravelCancel); t1.ClearCurrent(); }
        if (SuccessionDesk.HomeOf(rt.State, _heirRow) is { } home)
        {
            var o = rt.State.HomeRegion;
            _rec.TravelPlace(home.X + o.OriginXM * 1000L, home.Y + o.OriginYM * 1000L, Facing.South);
        }
        _heirRow = -1;
        _clothes?.EnsureHero(_hero, _world.Clock.Now.Seconds, fromInventory: true);   // dressed from the heir's own clothes (the desk made them)
        _rec.SetWantedSpeed(SimSpeed.X1); _rec.SetSpeed(SimSpeed.X1);
        _router?.ShowToast(r.Text, 6f);
    }

    /// <summary>Smoke and shots: the succession screen over the street view with the given order.</summary>
    public Control DebugShowSuccession(List<SuccessionCandidate> heirs, bool ironman, EstateSummary? estate = null)
    {
        Layer();
        _successionView = new SuccessionView(_hero?.Name ?? "Aldric", L.T("su_cause_fight"), ironman ? heirs.Take(1).ToList() : heirs, heirs.Count, ironman, _ => DebugCloseSuccession(), ironman ? null : () => { }, DebugCloseSuccession, estate) { Name = "SuccessionView" };
        _mcLayer!.AddChild(_successionView);
        return _successionView;
    }

    public void DebugCloseSuccession() { _successionView?.QueueFree(); _successionView = null; }
    public Control? DebugSuccessionView => _successionView;

    bool SuccessionInput(InputEvent e) => (_successionView != null || _heirsTask != null) && e is InputEventKey && !Ravensguard.Game.Inputs.Actions.Pressed(e, "world.back");
}
