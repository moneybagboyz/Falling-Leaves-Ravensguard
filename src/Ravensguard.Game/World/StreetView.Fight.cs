using System.Collections.Generic;
using System.Linq;
using Godot;
using Ravensguard.Content;
using Ravensguard.Game.World.Combat;
using Ravensguard.Sim.Combat;
using Ravensguard.Sim.Combat.Melee2;
using Ravensguard.Sim.Combat.Watch;
using Ravensguard.Sim.Combat.Wounds;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Live;
using Ravensguard.Sim.Trade;
using Ravensguard.Sim.Travel;

namespace Ravensguard.Game.World;

/// <summary>
/// The road fight (P4-C, overview 3): choosing Fight on a road encounter builds a <see cref="FightSetup"/> from the encounter's cast and the hero's kit and fights it for real in the
/// live bubble (the live tick steps it at one game second a step, the wounds go into the world's ledger); the combat stance view draws it and records every input. When it is over
/// the encounter is settled (<see cref="EncounterOutcomes.AfterFight"/>: held the field, ran, or knocked down and robbed until the defeat table of morale-capture-succession).
/// P4-F: the hero's saved stance and "keep watching" go into the setup; the watch HUD's stance changes are kept for the hero through a hero command; a watched fight runs at the
/// chosen speed (capped, 4x by default) by stepping the bubble more often; the practice yard's bout is the same fight with the rack's blunted weapons.
/// </summary>
public partial class StreetView
{
    static (ContentDatabase Db, FightContent C)? _fightContent;
    CanvasLayer? _fightLayer;
    FightView? _fightView;
    (EncounterEvent Ev, long Coin, int EndedTick)? _roadFight;
    /// <summary>A practice bout in the yard (or a tournament bout) in progress instead of a road fight: what to do when it ends.</summary>
    (FightKind Kind, int EndedTick, long Purse, int Settlement)? _boutFight;
    double _lastFightSliceMs;
    int _speedDropShown;

    FightContent? FightContentOf()
    {
        var db = _source?.Content;
        if (db == null) return null;
        if (_fightContent is { } c && ReferenceEquals(c.Db, db)) return c.C;
        var fc = new FightContent(new CombatTables(db), db);
        _fightContent = (db, fc);
        return fc;
    }

    /// <summary>The hero's kit for a fight: the record's weapons and skills, and what is worn (a shirt and tunic when the clothing view has nothing). The fighter is named as the hero is
    /// (the log said "You charges" when the kit was called "You").</summary>
    FighterKit HeroKit(FightContent fc)
    {
        var worn = new List<WornItem> { new("shirt", "linen"), new("tunic", "wool") };
        var rec = Runtime?.State.Hero ?? _hero;
        string name = _hero?.Name ?? rec?.Name ?? L.T("wm_hero_unnamed");
        if (rec == null)
            return new FighterKit { Name = name, Person = EconomyState.HeroOwner.Value, Weapon = new WeaponInstance("dagger", "iron"), Worn = worn, Skill = 30 };
        return FightSetup.HeroKit(fc, rec, worn, EconomyState.HeroOwner.Value, name);
    }

    /// <summary>The hero's watch settings (the world's book; its changes go back through <see cref="KeepStance"/>).</summary>
    WatchBook WatchBookOf() => Runtime?.State.Watch ?? _localWatch;
    readonly WatchBook _localWatch = new();

    /// <summary>The stance the hero chose in the HUD, kept for the next fight: a hero command applied at the barrier (recorded and saved).</summary>
    void KeepStance(WatchStance st)
    {
        if (_heroLink == null) { _localWatch.Set(EconomyState.HeroOwner, st); return; }
        _heroLink.Issue(new HeroCommand { Kind = HeroCommandKind.Watch, IssuedSec = _world.Clock.Now.Seconds, Tag = WatchDesk.StanceTag(EconomyState.HeroOwner, st), Origin = CommandOrigin.Human });
    }

    /// <summary>The setup of the encounter's fight (the panel rates it for its odds before anyone chooses).</summary>
    FightSetup? EncounterSetup(EncounterDef def, EncounterEvent ev)
    {
        var fc = FightContentOf(); if (fc == null) return null;
        var book = WatchBookOf();
        var setup = FightSetup.Encounter(fc, def, ev, HeroKit(fc), [], Runtime?.State.Seed ?? 1);
        return setup with { HeroStance = book.StanceOf(fc, EconomyState.HeroOwner, Runtime?.State.Autopilot), StartWatched = book.KeepWatching };
    }

    /// <summary>The encounter's options with the odds on the Fight button (P4-F: "probably lose"; a fight is offered, never forced).</summary>
    IReadOnlyList<EncounterOption> EncounterOptionsFor(EncounterDef def, EncounterEvent ev, long coin)
    {
        var fc = FightContentOf();
        if (fc == null || !def.Outcomes.ContainsKey("fight")) return EncounterOutcomes.Options(def, coin);
        var setup = EncounterSetup(def, ev);
        return setup == null ? EncounterOutcomes.Options(def, coin) : EncounterOutcomes.Options(def, coin, RiskEstimate.OddsOf(setup, fc));
    }

    /// <summary>Starts the real fight of an encounter (instead of the Phase 3 stub).</summary>
    bool StartRoadFight(EncounterDef def, EncounterEvent ev, long coin)
    {
        var fc = FightContentOf();
        if (fc == null || _fightView != null) return false;
        var rt = Runtime;
        var ledger = rt?.State.Wounds ?? _world.Wounds;
        if (ledger == null) { ledger = new WoundLedger(rt?.State.Seed ?? 1, _world.Clock.Now.Seconds); _world.AttachWounds(ledger); }
        var setup = EncounterSetup(def, ev)!;
        var fight = setup.Start(fc, ledger);
        if (_ride is { IsMounted: true } ride) Ravensguard.Sim.Combat.Mounted.MountedRide.MountHero(fight, setup.PlayerIndex, ride);   // [mounted-combat] a rider fights from the saddle
        _rec.StartFight(fight, setup.PlayerIndex);
        _rec.SetSpeed(SimSpeed.X1);
        _roadFight = (ev, coin, -1);
        OpenFightView(fight, setup.PlayerIndex);
        BeginFightLog(def, ev);                                    // P4-D: what began it, and the log for the save
        if (rt != null) rt.State.FightSource = () => _world.Fight is { } f ? Ravensguard.Sim.Save.FightRecord.Capture(f, setup.KindName, ev.Id) : null;
        return true;
    }

    void OpenFightView(FightWorld fight, int hero)
    {
        var book = WatchBookOf();
        _fightLayer = new CanvasLayer { Layer = 6, Name = "FightLayer" };
        AddChild(_fightLayer);
        _fightView = new FightView(fight, hero, i => _rec.FightInput(i), _rig)
        {
            SetPaused = p => _rec.SetSpeed(p ? SimSpeed.Paused : SimSpeed.X1), Name = "FightView", StanceChanged = KeepStance,
            TakeBackOnInput = book.TakeBackOnInput, Bus = _travel?.Bus ?? new InterruptBus(),
        };
        if (Runtime?.State.Autopilot is { } ap && ap.States.TryGetValue(EconomyState.HeroOwner, out var row) && row.Enabled) { _fightView.Autopilot = row; _fightView.Router = ap.Router; }
        _fightLayer.AddChild(_fightView);
    }

    /// <summary>How many live steps a slice takes: the speed while a watched fight runs (capped by the setting, and one level lower after a slice over the 25 ms budget), else one.</summary>
    int FightStepsPerSlice()
    {
        if (_fightView == null || _world.Fight == null || !_fightView.Watching || _world.Clock.Speed == SimSpeed.Paused) return 1;
        int want = WatchSpeed.Capped((int)_world.Clock.Speed, WatchBookOf().SpeedCap);
        int n = WatchSpeed.StepsPerFrame(want, _lastFightSliceMs);
        if (n < want && _frames - _speedDropShown > 120) { _speedDropShown = _frames; _router?.ShowToast(L.F("wm_speed_drop", n), 2f); }
        return n;
    }

    /// <summary>Each frame of a road fight: when it has been over for two seconds, settle the encounter and close the stance view.</summary>
    void RoadFightFrame()
    {
        if (_boutFight != null) { BoutFrame(); return; }
        if (_fightView == null || _roadFight is not { } rf) return;
        var f = _world.Fight;
        if (f == null) { CloseRoadFight(); return; }
        if (!f.IsOver) return;
        if (rf.EndedTick < 0) { _roadFight = rf with { EndedTick = f.Tick }; return; }
        if (f.Tick - rf.EndedTick < 40) return;
        var hero = f.Fighters[_fightView.Hero];
        bool won = f.DecidedWinner == hero.Team && hero.Act != FAct.Surrendered, fled = hero.Act == FAct.Fled;   // P4-D: the winner of a walk-away too (morale 4.3)
        var t = _travel;
        if (!won && !fled && DefeatHook(rf.Ev, f, _fightView.Hero)) { CloseRoadFight(); return; }   // P4-D hook: the defeat table, robbery, capture (StreetView.Captivity.cs)
        var o = EncounterOutcomes.AfterFight(rf.Ev, won, fled, rf.Coin, id => t == null || !t.Travellers.Any(x => x.Id == id));
        long now = _world.Clock.Now.Seconds;
        if (o.PayMites > 0 && _heroLink != null)
            _heroLink.Issue(new HeroCommand { Kind = HeroCommandKind.EncounterPay, IssuedSec = now, CoinGive = o.PayMites, Person = o.Payee, Tag = rf.Ev.Id, Origin = CommandOrigin.Human });
        _router?.ShowToast(o.PayMites > 0 ? L.F(o.Text, Quotes.Money(o.PayMites)) : L.T(o.Text), 5f);
        FightEnded(f);                                             // P4-D: close the log, chronicle the named dead, open the report
        CloseRoadFight();
        if (t != null) { t.ScreenPending = false; FinishEncounter(o, now, false); }
    }

    void CloseRoadFight()
    {
        if (_world.Fight != null) _rec.EndFight();
        _fightView?.QueueFree(); _fightLayer?.QueueFree();
        _fightView = null; _fightLayer = null; _roadFight = null; _boutFight = null;
        if (Runtime is { } rt) rt.State.FightSource = null;
    }

    // ------------------------------------------------------------------ the practice yard and tournaments on the real fight (overview 3)

    /// <summary>The yard's trainer as a fighter: a guard with a spear or sword, skill 45.</summary>
    FighterKit TrainerKit(FightContent fc, string name)
    {
        var row = FightSetup.KitDef(fc, "guard", "guard");
        return FightSetup.Roll(fc, row, Runtime?.State.Seed ?? 1, 0x7A2D, name, 1, 2000, 0, -FightMath.One, 0) with { Skill = 45, Person = 0 };
    }

    /// <summary>A sparring bout to three clean hits in the yard (onboarding 3.4 on the real fight: blunted practice weapons, the same combat stance view). False when it cannot start.</summary>
    bool StartPracticeBout(string trainerName)
    {
        var fc = FightContentOf();
        if (fc == null || _fightView != null || _yard == null) return false;
        var setup = FightSetup.Practice(fc, HeroKit(fc), TrainerKit(fc, trainerName), Runtime?.State.Seed ?? 1, _world.Clock.Now.Seconds, (ulong)(_yard.Settlement + 1));
        return StartBout(fc, setup, FightKind.Practice, 0, _yard.Settlement);
    }

    /// <summary>A tournament bout at a fair (plan 4A.3): blunted, yielding at the pain knock-out; the purse is paid by the town's treasury through a hero command.</summary>
    bool StartTournamentBout(long purseMites)
    {
        var fc = FightContentOf();
        if (fc == null || _fightView != null || _yard == null) return false;
        var row = FightSetup.KitDef(fc, "soldier", "soldier");
        var entrant = FightSetup.Roll(fc, row, Runtime?.State.Seed ?? 1, (ulong)(0x70E0 + _world.Clock.Now.Seconds / 86400), L.T("wm_entrant"), 1, 3000, 0, -FightMath.One, 0) with { Person = 0 };
        var setup = FightSetup.Tournament(fc, HeroKit(fc), entrant, Runtime?.State.Seed ?? 1, _world.Clock.Now.Seconds, (ulong)(_yard.Settlement + 1), (int)(_world.Clock.Now.Seconds / 600));
        return StartBout(fc, setup, FightKind.Tournament, purseMites, _yard.Settlement);
    }

    bool StartBout(FightContent fc, FightSetup setup, FightKind kind, long purse, int settlement)
    {
        var rt = Runtime;
        var ledger = rt?.State.Wounds ?? _world.Wounds;
        if (ledger == null) { ledger = new WoundLedger(rt?.State.Seed ?? 1, _world.Clock.Now.Seconds); _world.AttachWounds(ledger); }
        var fight = setup.Start(fc, ledger);
        _rec.StartFight(fight, setup.PlayerIndex);
        _rec.SetSpeed(SimSpeed.X1);
        _boutFight = (kind, -1, purse, settlement);
        OpenFightView(fight, setup.PlayerIndex);
        if (_fightView != null) _fightView.Log.Info(setup.PlayerIndex).Name = _hero?.Name ?? setup.Kits[0].Name;
        if (rt != null) rt.State.FightSource = () => _world.Fight is { } f ? Ravensguard.Sim.Save.FightRecord.Capture(f, setup.KindName, "") : null;
        _router?.ShowToast(L.T(kind == FightKind.Practice ? "wm_bout_begin" : "wm_tourney_begin"), 3f);
        return true;
    }

    /// <summary>A bout is over: the skill use of the bout (the same cap as before), the purse to a winning hero, and the view closes.</summary>
    void BoutFrame()
    {
        if (_fightView == null || _boutFight is not { } b) return;
        var f = _world.Fight;
        if (f == null) { CloseRoadFight(); return; }
        if (!f.IsOver) return;
        if (b.EndedTick < 0) { _boutFight = b with { EndedTick = f.Tick }; return; }
        if (f.Tick - b.EndedTick < 40) return;
        var hero = f.Fighters[_fightView.Hero];
        bool won = f.DecidedWinner == hero.Team && hero.Act is not (FAct.Surrendered or FAct.Fled);
        long now = _world.Clock.Now.Seconds;
        if (b.Kind == FightKind.Practice && Runtime is { } rt && _heroLink != null)
        {
            _yardLastBout = now;
            _ = Bar(rt, () => Ravensguard.Sim.Onboarding.PracticeYard.FinishBout(_heroLink.Context().Craft.State.Book, EconomyState.HeroOwner, now));
            _router?.ShowToast(L.T(won ? "YARD_SPAR_WON" : "YARD_SPAR_LOST"), 4f);
        }
        else if (b.Kind == FightKind.Tournament)
        {
            if (won && b.Purse > 0 && _heroLink != null)
                _heroLink.Issue(new HeroCommand { Kind = HeroCommandKind.Watch, IssuedSec = now, Tag = WatchDesk.PrizeTag(b.Settlement, b.Purse), Origin = CommandOrigin.Human });
            _router?.ShowToast(won ? L.F("wm_tourney_won", Quotes.Money(b.Purse)) : L.T("wm_tourney_lost"), 5f);
        }
        _fightView.FinishLog();
        CloseRoadFight();
    }
}
