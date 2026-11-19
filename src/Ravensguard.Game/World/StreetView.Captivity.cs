using System;
using System.Linq;
using Godot;
using Ravensguard.Game.World.Combat;
using Ravensguard.Sim.Combat.Aftermath;
using Ravensguard.Sim.Combat.Melee2;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Succession;
using Ravensguard.Sim.Trade;
using Ravensguard.Sim.Travel;

namespace Ravensguard.Game.World;

/// <summary>
/// The aftermath of a lost road fight (P4-D, morale-capture-succession 4.3 to 4.8, 7): the fight's end becomes a defeat request sent to the barrier (the defeat table, robbery and capture are
/// the sim's, <see cref="AftermathDesk"/>); the defeat screen shows what it cost; a capture opens the captivity screen, whose options are captive commands; when the hero is free (escaped
/// to the road, ransomed, released, rescued) the encounter is settled and the journey goes on. The Q menu in the combat stance offers to yield. Thin views: no state here but which screen is up.
/// </summary>
public partial class StreetView
{
    CanvasLayer? _mcLayer;
    DefeatView? _defeatView;
    CaptivityView? _captivityView;
    (EncounterEvent Ev, long IssuedSec)? _defeatPending;
    long _captiveActSec = -1;
    EncounterEvent? _aftermathEv;

    CaptiveBook? Captives => Runtime?.State.Captivity;

    /// <summary>The road fight is lost (the hero down or yielded): the defeat goes to the barrier; the screen follows when it has been applied. False when there is no desk to send it to.</summary>
    bool DefeatHook(EncounterEvent ev, FightWorld f, int hero)
    {
        if (_heroLink == null || Runtime == null || _source?.Content == null) return false;
        var def = _travel?.Table.Defs.FirstOrDefault(d => d.Id == ev.Id);
        var rules = AftermathRules.For(_source.Content);
        var winner = def is { Groups.Count: > 0 } ? rules.WinnerOf(def.Groups[0].Archetype) : WinnerKind.Folk;
        var (x, y) = _world.WorldMm;
        long now = _world.Clock.Now.Seconds;
        _heroLink.Issue(AftermathDesk.DefeatCommand(DefeatRequest.Of(f, hero, winner, x, y), now));
        _defeatPending = (ev, now); _aftermathEv = ev;
        HoldJourney(now);
        _rec.SetWantedSpeed(SimSpeed.X8); _rec.SetSpeed(SimSpeed.X8);   // to the barrier of this hour: "you wake..."
        _router?.ShowToast(L.T("MC_DARKNESS"), 4f);
        return true;
    }

    /// <summary>Each frame: show the defeat once the barrier has applied it, keep the hero still while held, close captivity when the hero is free.</summary>
    void AftermathFrame()
    {
        long now = _world.Clock.Now.Seconds;
        if (_defeatPending is { } p && _defeatView == null && Captives?.LastDefeat is { } rep && rep.AtSec == p.IssuedSec)
        {
            _rec.SetSpeed(SimSpeed.Paused);
            ShowDefeat(rep, p.Ev);
        }
        if (_aftermathEv != null) HoldJourney(now);                       // from the fall to the end of captivity the journey waits
        if (_captivityView == null) return;
        var v = Captives?.View;
        if (_captiveActSec >= 0 && v?.Last is { } last && last.EndSec >= _captiveActSec) _captiveActSec = -1;
        if (v != null && v.State != CaptiveState.Held) EndCaptivity(v, now);
    }

    void HoldJourney(long now) { if (_travel?.Journey is { } j) j.BreakUntilSec = Math.Max(j.BreakUntilSec, now + 600); }

    void Layer()
    {
        if (_mcLayer != null) return;
        _mcLayer = new CanvasLayer { Layer = 8, Name = "AftermathLayer" };
        AddChild(_mcLayer);
    }

    void ShowDefeat(DefeatReport rep, EncounterEvent ev)
    {
        Layer();
        var heirs = rep.Kind == DefeatKind.Killed ? HeirsOfTheParty() : [];
        _defeatView = new DefeatView(rep, () => AfterDefeat(rep, ev), heirs) { Name = "DefeatView" };
        _mcLayer!.AddChild(_defeatView);
    }

    /// <summary>Who would continue: the party (the hero is not a row of the person table in Phase 4, so kin comes with Phase 5's estates; the order is <see cref="SuccessionOrder"/>'s).</summary>
    System.Collections.Generic.List<SuccessionCandidate> HeirsOfTheParty()
    {
        var squad = (_heroLink?.View?.Squad ?? []).Where(m => m.Active)
            .Select(m => new SquadMember(m.Person, -1, m.Loyalty, m.JoinedDay, m.Origin == FollowerOrigin.Recruited, m.Name)).ToList();
        return SuccessionOrder.Order(new Ravensguard.Sim.People.PersonTable(1, 1), -1, (int)(_world.Clock.Now.Seconds / 86400), squad);
    }

    void AfterDefeat(DefeatReport rep, EncounterEvent ev)
    {
        _defeatView?.QueueFree(); _defeatView = null;
        _defeatPending = null;
        long now = _world.Clock.Now.Seconds;
        if (rep.Kind == DefeatKind.Killed) { _router?.ReturnToMainMenu(); return; }   // the world persists; a new life starts from the library
        if (rep.Kind == DefeatKind.Captured)
        {
            Layer();
            _captivityView = new CaptivityView(() => Captives?.View, CaptiveAct, () => _captiveActSec >= 0) { Name = "CaptivityView", Pending = () => _captiveActSec >= 0 ? L.T("MC_WORKING") : "" };
            _mcLayer!.AddChild(_captivityView);
            _rec.SetWantedSpeed(SimSpeed.X8); _rec.SetSpeed(SimSpeed.X8);
            return;
        }
        _rec.SetSpeed(SimSpeed.X1);
        Settle(ev, now, rep.Kind == DefeatKind.Robbed ? "MC_ROBBED_TOAST" : "MC_LEFT_TOAST", rep.CoinTaken);
    }

    void CaptiveAct(CaptiveVerb verb)
    {
        if (_heroLink == null) return;
        long now = _world.Clock.Now.Seconds;
        _heroLink.Issue(AftermathDesk.ActCommand(verb, now));
        _captiveActSec = verb == CaptiveVerb.Wait ? -1 : now;
        _rec.SetWantedSpeed(SimSpeed.X8); _rec.SetSpeed(SimSpeed.X8);   // the attempt lands at this hour's barrier; time runs while the hero waits
    }

    void EndCaptivity(CaptiveView v, long now)
    {
        _captivityView?.QueueFree(); _captivityView = null; _captiveActSec = -1;
        _rec.SetWantedSpeed(SimSpeed.X1); _rec.SetSpeed(SimSpeed.X1);
        if (_aftermathEv is { } ev) Settle(ev, now, "MC_FREE_" + v.State.ToString().ToUpperInvariant(), 0);
        else _router?.ShowToast(L.T("MC_FREE_" + v.State.ToString().ToUpperInvariant()), 5f);
    }

    void Settle(EncounterEvent ev, long now, string toastKey, long mites)
    {
        _router?.ShowToast(L.F(toastKey, Quotes.Money(mites)), 5f);
        _aftermathEv = null;
        var t = _travel;
        if (t == null) return;
        t.ScreenPending = false;
        FinishEncounter(new EncounterOutcome(EncounterChoice.Fight, "aftermath", 0, EntityId.None, EntityId.None, EncounterOutcomes.FleeDelaySec, ""), now, false);
    }

    /// <summary>Smoke: stages the defeat screen (0) or the captivity screen (1, an army cage) over the street view; <see cref="DebugCloseAftermath"/> takes it away.</summary>
    public Control DebugShowAftermath(int which)
    {
        Layer();
        long now = _world.Clock.Now.Seconds;
        if (which == 0)
        {
            var rep = new DefeatReport(DefeatKind.Robbed, WinnerKind.Bandits, "robber", 340, 20, ["arming_sword", "mail_hauberk", "javelin"], ["shirt", "boots", "dagger", "bread"], true, 2, true, true, 0, now);
            _defeatView = new DefeatView(rep, DebugCloseAftermath) { Name = "DefeatView" };
            _mcLayer!.AddChild(_defeatView);
            return _defeatView;
        }
        var v = new CaptiveView(Ravensguard.Sim.Economy.EconomyState.HeroOwner, CaptorKind.Army, Restraint.Caged, CaptiveState.Held, 2, 2, false, 1500, PayerKind.Household, 800, 6, 1, 40, true, true,
            500, 400, 250, false, 0, 1, "", null, 0, 0);
        _captivityView = new CaptivityView(() => v, _ => { }, () => false) { Name = "CaptivityView" };
        _mcLayer!.AddChild(_captivityView);
        return _captivityView;
    }

    public void DebugCloseAftermath()
    {
        _defeatView?.QueueFree(); _defeatView = null;
        _captivityView?.QueueFree(); _captivityView = null;
    }

    /// <summary>The input guard while an aftermath screen is up, and the yield verb (Q in the combat stance). True when the event is taken.</summary>
    bool AftermathInput(InputEvent e)
    {
        if (_defeatView != null || _captivityView != null) return e is InputEventKey && !Ravensguard.Game.Inputs.Actions.Pressed(e, "world.back");
        if (_fightView != null && _world.Fight is { } f && _router != null && Ravensguard.Game.Inputs.Actions.Pressed(e, "combatstance.context_menu"))
        {
            var hero = f.Fighters[_fightView.Hero];
            if (!hero.Active) return true;
            _rec.SetSpeed(SimSpeed.Paused);
            _router.ShowModal(L.T("MC_YIELD_TITLE"), L.T("MC_YIELD_BODY"), () => _rec.SetSpeed(SimSpeed.X1),
                (L.T("MC_YIELD"), () => _rec.FightInput(new FightInput(FightInputKind.Yield))), (L.T("UI_CANCEL"), null));
            return true;
        }
        return false;
    }
}
