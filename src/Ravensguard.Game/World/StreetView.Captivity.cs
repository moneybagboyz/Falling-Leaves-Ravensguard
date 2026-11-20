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
        RescueFrame();                                                    // P4 close-out: the rescue control switch
        if (_defeatPending is { } p && _defeatView == null && Captives?.LastDefeat is { } rep && rep.AtSec == p.IssuedSec)
        {
            _rec.SetSpeed(SimSpeed.Paused);
            ShowDefeat(rep, p.Ev);
        }
        // a save loaded while the hero is held (review, game finding 3): the sim's book says held, so the captivity screen comes back and the journey waits, as before the save
        bool held = Captives is { HeroHeld: true };
        if (held && _captivityView == null && _defeatView == null && _defeatPending == null && _rescuer == null && _successionView == null && _heirsTask == null
            && Captives?.View is { State: CaptiveState.Held })
        {
            OpenCaptivityView();
            _rec.SetWantedSpeed(SimSpeed.X8); _rec.SetSpeed(SimSpeed.X8);
        }
        if (_aftermathEv != null || held) HoldJourney(now);              // from the fall to the end of captivity the journey waits
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
        _defeatView = new DefeatView(rep, () => AfterDefeat(rep, ev), [], Run is { Ironman: true }) { Name = "DefeatView" };
        _mcLayer!.AddChild(_defeatView);
    }

    void AfterDefeat(DefeatReport rep, EncounterEvent ev)
    {
        _defeatView?.QueueFree(); _defeatView = null;
        _defeatPending = null;
        long now = _world.Clock.Now.Seconds;
        if (rep.Kind == DefeatKind.Killed) { HeroDied("su_cause_fight"); return; }   // the heir takes over (StreetView.Succession.cs); with nobody left the run ends
        if (rep.Kind == DefeatKind.Captured)
        {
            OpenCaptivityView();                                            // adds the view itself (it was added a second time here: review, game finding 4)
            _rec.SetWantedSpeed(SimSpeed.X8); _rec.SetSpeed(SimSpeed.X8);
            return;
        }
        _rec.SetSpeed(SimSpeed.X1);
        Settle(ev, now, rep.Kind == DefeatKind.Robbed ? "MC_ROBBED_TOAST" : "MC_LEFT_TOAST", rep.CoinTaken);
    }

    void OpenCaptivityView()
    {
        Layer();
        _captivityView = new CaptivityView(() => Captives?.View, CaptiveAct, () => _captiveActSec >= 0 || _rescueSeq >= 0)
        {
            Name = "CaptivityView", Pending = () => _captiveActSec >= 0 ? L.T("MC_WORKING") : "",
            Rescuers = () => [.. (_heroLink?.View?.Squad ?? []).Where(m => m.Active).OrderByDescending(m => m.Loyalty).ThenBy(m => m.Person.Value).Select(m => (m.Person, m.Name))],
            TakeControl = AskControl,
            Cell = () => _cell, Plea = PleaAct,                                         // P6-C1: the cell's charges, the sentence to expect, the ways out
        };
        _mcLayer!.AddChild(_captivityView);
    }

    // ------------------------------------------------------------------ the rescue control switch (4.7; P4 close-out)

    (EntityId Id, string Name)? _rescuer;
    (long X, long Y) _rescueCell;
    long _rescueSeq = -1;
    bool _rescueAsk;
    /// <summary>How far from the captors' camp the companion starts (world millimetres south of the cell) and how near it must come before E frees the hero (the rest is the sim's creep).</summary>
    const long RescueStartMm = 60_000, RescueReachMm = 15_000;

    /// <summary>Take control of a free companion: asked at the barrier; once granted the street view is the companion's (the captivity screen closes until the rescue is over).</summary>
    void AskControl(EntityId companion)
    {
        if (_heroLink == null || Captives?.View is not { } v) return;
        var name = (_heroLink.View?.Squad ?? []).FirstOrDefault(m => m.Person == companion)?.Name ?? "";
        _rescuer = (companion, name); _rescueCell = (v.PlaceX, v.PlaceY); _rescueAsk = true;
        _rescueSeq = _heroLink.Issue(AftermathDesk.ControlCommand(companion, _world.Clock.Now.Seconds)).Seq;
        _rec.SetSpeed(SimSpeed.X8);
    }

    /// <summary>E while controlling the companion: near enough to the camp, the rescue is tried from where it stands.</summary>
    bool TryRescueAct()
    {
        if (_rescuer is not { } r || _rescueAsk || _heroLink == null) return false;
        var (x, y) = _world.WorldMm; long dx = x - _rescueCell.X, dy = y - _rescueCell.Y;
        if (dx * dx + dy * dy > RescueReachMm * RescueReachMm) { _router?.ShowToast(L.T("rs_too_far"), 3f); return true; }
        _rescueSeq = _heroLink.Issue(AftermathDesk.RescueCommand(r.Id, dx, dy, _world.Clock.Now.Seconds)).Seq;
        _rec.SetSpeed(SimSpeed.X8);
        return true;
    }

    /// <summary>Each frame: the answers of the control and rescue commands.</summary>
    void RescueFrame()
    {
        if (_rescueSeq < 0 || _heroLink?.View is not { } hv) return;
        var res = hv.Results.FirstOrDefault(x => x.Seq == _rescueSeq);
        if (res == null) return;
        _rescueSeq = -1;
        _router?.ShowToast(res.Text, 5f);
        if (_rescueAsk)
        {
            _rescueAsk = false;
            if (!res.Ok || _rescuer is not { } r) { _rescuer = null; return; }
            // the street view is the companion's: the captivity screen goes, the avatar stands a way off from the camp, time runs at 1x
            _captivityView?.QueueFree(); _captivityView = null;
            _rec.TravelPlace(_rescueCell.X, _rescueCell.Y + RescueStartMm, Ravensguard.Sim.Live.Facing.North);
            _rec.SetWantedSpeed(SimSpeed.X1); _rec.SetSpeed(SimSpeed.X1);
            _router?.ShowToast(L.F("rs_now_you_are", r.Name, _hero?.Name ?? ""), 6f);
            return;
        }
        // the attempt is over: the hero stands at the cell again (freed, or still held and the screen returns)
        _rescuer = null;
        _rec.TravelPlace(_rescueCell.X, _rescueCell.Y, Ravensguard.Sim.Live.Facing.South);
        long now = _world.Clock.Now.Seconds;
        if (Captives?.View is { State: not CaptiveState.Held } v) EndCaptivity(v, now);
        else { OpenCaptivityView(); _rec.SetWantedSpeed(SimSpeed.X8); _rec.SetSpeed(SimSpeed.X8); }
    }

    /// <summary>The HUD line while the player is the companion.</summary>
    string? RescueHud() => _rescuer is { } r && !_rescueAsk ? L.F("rs_hud", r.Name, _hero?.Name ?? "") : null;

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
        var v = new CaptiveView(Ravensguard.Sim.Economy.EconomyState.HeroOwner, which == 2 ? CaptorKind.Gaol : CaptorKind.Army, which == 2 ? Restraint.Bound : Restraint.Caged, CaptiveState.Held, 2, 2, false, 1500, PayerKind.Household, 800, 6, 1, 40, true, true,
            500, 400, 250, false, 0, 1, "", null, 0, 0);
        Ravensguard.Sim.Law.CellView? cell = null;
        if (which == 2)
        {
            Ravensguard.Sim.Law.Expected E(string key, string kind, int step, long mites = 0, int days = 0, int lashes = 0, int sev = 4, int priors = 0, int mercy = 0) => new(key, step, kind, mites, days, lashes, 0, 0, "gaol", sev, priors, mercy, false, false, "stocks_then_hand");
            cell = new Ravensguard.Sim.Law.CellView(0, 0, 1, 3, 2, false,
                [new Ravensguard.Sim.Law.ChargeView(1, "theft", 240, E("stocks_then_hand", "flogging", 1, lashes: 20, priors: 1), 3, 2, false, false, false, false),
                 new Ravensguard.Sim.Law.ChargeView(2, "assault", 0, E("fine", "fine", 0, 100, sev: 1), 1, 0, true, false, false, false)],
                [new Ravensguard.Sim.Law.ServingView(3, "gaol", "gaol", 0, 15, 0, 5, 20, Ravensguard.Sim.Law.SentenceState.Pending)],
                [new Ravensguard.Sim.Law.WayOut("jl_way_pay", true, 580), new Ravensguard.Sim.Law.WayOut("jl_way_restitution", true, 240), new Ravensguard.Sim.Law.WayOut("jl_way_mercy", false),
                 new Ravensguard.Sim.Law.WayOut("jl_way_clergy", false), new Ravensguard.Sim.Law.WayOut("jl_way_bribe", true, 900), new Ravensguard.Sim.Law.WayOut("jl_way_escape", true), new Ravensguard.Sim.Law.WayOut("jl_way_rescue", false)]);
        }
        _captivityView = new CaptivityView(() => v, _ => { }, () => false) { Name = "CaptivityView", Cell = () => cell, Plea = _ => { } };
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
        if (_defeatView != null || _captivityView != null || _rescueAsk) return e is InputEventKey && !Ravensguard.Game.Inputs.Actions.Pressed(e, "world.back");
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
