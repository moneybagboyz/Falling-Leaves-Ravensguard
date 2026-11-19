using System.Collections.Generic;
using System.Linq;
using Godot;
using Ravensguard.Content;
using Ravensguard.Game.World.Combat;
using Ravensguard.Sim.Combat;
using Ravensguard.Sim.Combat.Melee2;
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
/// </summary>
public partial class StreetView
{
    static (ContentDatabase Db, FightContent C)? _fightContent;
    CanvasLayer? _fightLayer;
    FightView? _fightView;
    (EncounterEvent Ev, long Coin, int EndedTick)? _roadFight;

    FightContent? FightContentOf()
    {
        var db = _source?.Content;
        if (db == null) return null;
        if (_fightContent is { } c && ReferenceEquals(c.Db, db)) return c.C;
        var fc = new FightContent(new CombatTables(db), db);
        _fightContent = (db, fc);
        return fc;
    }

    /// <summary>The hero's kit for a fight: the record's weapons and skills, and what is worn (a shirt and tunic when the clothing view has nothing).</summary>
    FighterKit HeroKit(FightContent fc)
    {
        var worn = new List<WornItem> { new("shirt", "linen"), new("tunic", "wool") };
        var rec = Runtime?.State.Hero;
        if (rec == null)
            return new FighterKit { Name = L.T("TRAVEL_CAMP_YOU"), Person = EconomyState.HeroOwner.Value, Weapon = new WeaponInstance("dagger", "iron"), Worn = worn, Skill = 30 };
        return FightSetup.HeroKit(fc, rec, worn, EconomyState.HeroOwner.Value, L.T("TRAVEL_CAMP_YOU"));
    }

    /// <summary>Starts the real fight of an encounter (instead of the Phase 3 stub).</summary>
    bool StartRoadFight(EncounterDef def, EncounterEvent ev, long coin)
    {
        var fc = FightContentOf();
        if (fc == null || _fightView != null) return false;
        var rt = Runtime;
        var ledger = rt?.State.Wounds ?? _world.Wounds;
        if (ledger == null) { ledger = new WoundLedger(rt?.State.Seed ?? 1, _world.Clock.Now.Seconds); _world.AttachWounds(ledger); }
        var setup = FightSetup.Encounter(fc, def, ev, HeroKit(fc), [], rt?.State.Seed ?? 1);
        var fight = setup.Start(fc, ledger);
        if (_ride is { IsMounted: true } ride) Ravensguard.Sim.Combat.Mounted.MountedRide.MountHero(fight, setup.PlayerIndex, ride);   // [mounted-combat] a rider fights from the saddle
        _rec.StartFight(fight, setup.PlayerIndex);
        _rec.SetSpeed(SimSpeed.X1);
        _roadFight = (ev, coin, -1);
        _fightLayer = new CanvasLayer { Layer = 6, Name = "FightLayer" };
        AddChild(_fightLayer);
        _fightView = new FightView(fight, setup.PlayerIndex, i => _rec.FightInput(i), _rig) { SetPaused = p => _rec.SetSpeed(p ? SimSpeed.Paused : SimSpeed.X1), Name = "FightView" };
        _fightLayer.AddChild(_fightView);
        if (rt != null) rt.State.FightSource = () => _world.Fight is { } f ? Ravensguard.Sim.Save.FightRecord.Capture(f, "encounter", ev.Id) : null;
        return true;
    }

    /// <summary>Each frame of a road fight: when it has been over for two seconds, settle the encounter and close the stance view.</summary>
    void RoadFightFrame()
    {
        if (_fightView == null || _roadFight is not { } rf) return;
        var f = _world.Fight;
        if (f == null) { CloseRoadFight(); return; }
        if (!f.IsOver) return;
        if (rf.EndedTick < 0) { _roadFight = rf with { EndedTick = f.Tick }; return; }
        if (f.Tick - rf.EndedTick < 40) return;
        var hero = f.Fighters[_fightView.Hero];
        bool won = f.WinnerTeam == hero.Team, fled = hero.Act == FAct.Fled;
        var t = _travel;
        var o = EncounterOutcomes.AfterFight(rf.Ev, won, fled, rf.Coin, id => t == null || !t.Travellers.Any(x => x.Id == id));
        long now = _world.Clock.Now.Seconds;
        if (o.PayMites > 0 && _heroLink != null)
            _heroLink.Issue(new HeroCommand { Kind = HeroCommandKind.EncounterPay, IssuedSec = now, CoinGive = o.PayMites, Person = o.Payee, Tag = rf.Ev.Id, Origin = CommandOrigin.Human });
        _router?.ShowToast(o.PayMites > 0 ? L.F(o.Text, Quotes.Money(o.PayMites)) : L.T(o.Text), 5f);
        CloseRoadFight();
        if (t != null) { t.ScreenPending = false; FinishEncounter(o, now, false); }
    }

    void CloseRoadFight()
    {
        if (_world.Fight != null) _rec.EndFight();
        _fightView?.QueueFree(); _fightLayer?.QueueFree();
        _fightView = null; _fightLayer = null; _roadFight = null;
        if (Runtime is { } rt) rt.State.FightSource = null;
    }
}
