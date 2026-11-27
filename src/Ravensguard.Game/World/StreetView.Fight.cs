using Ravensguard.Game.UI.NewGame;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Ravensguard.Content;
using Ravensguard.Game.World.Combat;
using Ravensguard.Sim.Bondage;
using Ravensguard.Sim.Battle;
using Ravensguard.Sim.Combat;
using Ravensguard.Sim.Combat.Aftermath;
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
        // P4 close-out: what the hero wears is the inventory's worn garments and armour (item rows, in their order), so the fight's wear can go back to them; a shirt and tunic when none
        var worn = HeroWornRows(fc);
        if (worn.Count == 0) { worn.Add(new("shirt", "linen")); worn.Add(new("tunic", "wool")); }
        var rec = Runtime?.State.Hero ?? _hero;
        string name = _hero?.Name ?? rec?.Name ?? L.T("wm_hero_unnamed");
        if (rec == null)
            return new FighterKit { Name = name, Person = EconomyState.HeroOwner.Value, Weapon = new WeaponInstance("dagger", "iron"), Worn = worn, Skill = 30 };
        // P5-A4: the weapon is chosen from what the pack really holds (loot included, with its quality), not the record's starting kit
        var carried = _heroLink?.View is { } v ? Ravensguard.Sim.Inventory.HeroPack.Things(v.Items, v.Goods, id => fc.Ammo.ContainsKey(id) || id == "javelin") : null;
        return FightSetup.HeroKit(fc, rec, worn, EconomyState.HeroOwner.Value, name, carried);
    }

    /// <summary>The hero's worn item rows in the order of the kit's worn pieces (empty for the stand-in shirt and tunic), with their condition before the fight.</summary>
    readonly List<(EntityId Item, int ConditionPm)> _heroWorn = [];

    /// <summary>The inventory's worn garments and armour as fight pieces, in row order, and <see cref="_heroWorn"/> with them.</summary>
    List<WornItem> HeroWornRows(FightContent fc)
    {
        _heroWorn.Clear();
        var worn = new List<WornItem>();
        foreach (var it in _heroLink?.View?.Items ?? [])
        {
            if (it.Slot == Ravensguard.Sim.Inventory.EquipSlot.None || !fc.Db.TryGet<ArmourShapeDef>(it.Shape, out var a)) continue;
            string mat = it.Material.Length > 0 && fc.T.HasMaterial(it.Material) ? it.Material : a.DefaultMaterial ?? "";
            if (mat.Length == 0 || !fc.T.HasMaterial(mat)) continue;
            worn.Add(new WornItem(it.Shape, mat, it.Quality)); _heroWorn.Add((it.Id, it.ConditionPermille));    // P5-A1: the row's tier is the piece's Qa
        }
        return worn;
    }

    /// <summary>After a road fight (P6-E3): a slave of the party who broke and ran in it is a runaway (unless chained); the rest are steadied by a held field or shaken by a lost one.</summary>
    void SlavesAfterFight(FightWorld f, bool won)
    {
        var st = Runtime?.State; if (_heroLink == null || st?.Bonds == null || st.Party == null || !st.Party.Squad.Members.Any(m => m.Origin == Ravensguard.Sim.Trade.FollowerOrigin.Slave)) return;
        long now = _world.Clock.Now.Seconds;
        foreach (var fi in f.Fighters)
            if (fi.Act == FAct.Fled && fi.Person != 0 && st.Party.Squad.Find(new EntityId(fi.Person)) is { Origin: Ravensguard.Sim.Trade.FollowerOrigin.Slave })
                _heroLink.Issue(BondDesk.Flee(new EntityId(fi.Person), true, now));
        _heroLink.Issue(BondDesk.Fought(won, now));
    }

    /// <summary>The pieces start the fight at their item rows' condition (the kit knows only shape and material); set before the fight is recorded, so a replay starts the same.</summary>
    void WornCondition(FightWorld fight, int hero)
    {
        if (hero < 0 || hero >= fight.Fighters.Count) return;
        var w = fight.Fighters[hero].Worn;
        for (int i = 0; i < w.Length && i < _heroWorn.Count; i++) w[i].ConditionPm = Math.Clamp(_heroWorn[i].ConditionPm, 0, 1000);
    }

    /// <summary>After a road fight (P4 close-out): the wear on the hero's worn pieces goes back to their item rows at the barrier (condition; burst rings and straps stay the fight's).</summary>
    void WriteBackWear(FightWorld fight, int hero)
    {
        if (_heroLink == null || hero < 0 || hero >= fight.Fighters.Count) return;
        var w = fight.Fighters[hero].Worn;
        var wear = new List<(EntityId, int)>();
        for (int i = 0; i < w.Length && i < _heroWorn.Count; i++) if (w[i].ConditionPm < _heroWorn[i].ConditionPm) wear.Add((_heroWorn[i].Item, w[i].ConditionPm));
        if (wear.Count > 0) _heroLink.Issue(AftermathDesk.WearCommand(wear, _world.Clock.Now.Seconds));
        var blades = fight.WeaponKeepPm().ToList();                                  // P6-D3 (heists 4.4): the blades' wear, and a brittle blade that broke leaving the pack
        if (blades.Count > 0) _heroLink.Issue(AftermathDesk.WeaponWearCommand(blades.Select(b => (new EntityId(b.Item), b.KeepPm)), _world.Clock.Now.Seconds));
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
        var setup = FightSetup.Encounter(fc, def, ev, HeroKit(fc), SlaveParty.Kits(SlaveStands(fc)), Runtime?.State.Seed ?? 1);       // P6-E3: the armed slaves of the party stand with him
        return setup with { HeroStance = book.StanceOf(fc, EconomyState.HeroOwner, Runtime?.State.Autopilot), StartWatched = book.KeepWatching, Weather = _wx.State.ToString() };   // P4 close-out: the live weather's row
    }

    /// <summary>The hero's slaves in the party as a fight sees them (P6-E3, overview 10a): armed ones fight, unarmed ones stand back, unchained ones under morale 40 bolt.</summary>
    List<SlaveStand> SlaveStands(FightContent fc)
    {
        var st = Runtime?.State;
        if (st?.Party == null || st.Economy == null || st.Bonds == null) return [];
        return SlaveParty.Muster(st.Bonds, st.Party.Squad, st.Economy, fc.Db, BondRules.From(fc.Db));
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
        if (_heroLink != null)
            foreach (var sl in SlaveStands(fc).Where(x => x.Role == SlaveRole.Flees))                  // P6-E3: a slave under the flee morale and not chained bolts before the first blow: a runaway by the daily path
                _heroLink.Issue(BondDesk.Flee(sl.Person, false, _world.Clock.Now.Seconds));
        var fight = setup.Start(fc, ledger);
        WornCondition(fight, setup.PlayerIndex);                    // P4 close-out: the pieces as worn as their rows
        if (_ride is { IsMounted: true } ride) Ravensguard.Sim.Combat.Mounted.MountedRide.MountHero(fight, setup.PlayerIndex, ride);   // [mounted-combat] a rider fights from the saddle
        _rec.StartFight(fight, setup.PlayerIndex);
        _rec.SetSpeed(SimSpeed.X1);
        _roadFight = (ev, coin, -1);
        OpenFightView(fight, setup.PlayerIndex);
        BeginFightLog(def, ev);                                    // P4-D: what began it, and the log for the save
        if (rt != null) rt.State.FightSource = () => _world.Fight is { } f ? Ravensguard.Sim.Save.FightRecord.Capture(f, setup.KindName, ev.Id) : null;
        return true;
    }

    void OpenFightView(FightWorld fight, int hero, BattleBubble? bubble = null)
    {
        var book = WatchBookOf();
        _fightLayer = new CanvasLayer { Layer = 6, Name = "FightLayer" };
        AddChild(_fightLayer);
        _fightView = new FightView(fight, hero, i => _rec.FightInput(i), _rig)
        {
            SetPaused = p => _rec.SetSpeed(p ? SimSpeed.Paused : SimSpeed.X1), Name = "FightView", StanceChanged = KeepStance,
            TakeBackOnInput = book.TakeBackOnInput, Bus = _travel?.Bus ?? new InterruptBus(), Bubble = bubble,
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
        if (!_fightResumeChecked) { _fightResumeChecked = true; ResumeFight(); }   // P4 close-out: a save made mid-fight
        AssaultFrame();                                              // P8-LW-1: a live assault the sim has opened becomes a battle
        if (_battleOpen) { BattleFrame(); return; }                 // P7-BB-C: a battle in the bubble
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
        SlavesAfterFight(f, won);
        WriteBackWear(f, _fightView.Hero);                         // P4 close-out: the dents and tears onto the hero's clothing rows (before any robbery)
        if (!won && !fled && DefeatHook(rf.Ev, f, _fightView.Hero)) { CloseRoadFight(); return; }   // P4-D hook: the defeat table, robbery, capture (StreetView.Captivity.cs)
        var o = EncounterOutcomes.AfterFight(rf.Ev, won, fled, rf.Coin, id => t == null || !t.Travellers.Any(x => x.Id == id));
        long now = _world.Clock.Now.Seconds;
        if (o.PayMites > 0 && _heroLink != null)
            _heroLink.Issue(new HeroCommand { Kind = HeroCommandKind.EncounterPay, IssuedSec = now, CoinGive = o.PayMites, Person = o.Payee, Tag = rf.Ev.Id, Origin = CommandOrigin.Human });
        _router?.ShowToast(o.PayMites > 0 ? L.F(o.Text, UiKit.Money(o.PayMites)) : L.T(o.Text), 5f);
        FightEnded(f);                                             // P4-D: close the log, chronicle the named dead, open the report
        CloseRoadFight();
        if (t != null) { t.ScreenPending = false; FinishEncounter(o, now, false); }
    }

    bool _fightResumeChecked;

    /// <summary>
    /// A save made mid-fight (P4 close-out): the fight record of the "fight" section is rebuilt where it stood (same FightTick, books, morale, watch and weather) and attached to the live world
    /// through the recorder (so a replay starts it too), the stance view opens on it paused, and the road fight or the bout is settled as before when it ends. A site's fight comes back with
    /// its crawl (StreetView.Crawl.cs), not here.
    /// </summary>
    void ResumeFight()
    {
        if (Runtime is not { } rt || rt.State.Fight is not { } rec || _fightView != null || _world.Fight != null) return;
        rt.State.Fight = null;                                          // taken up: the next save captures the live fight, never this record again
        if (rt.State.Battle != null && ResumeBattle(rt, rec)) return;   // P7-BB-C: a save made inside a battle
        var kind = FightSetup.KindOf(rec.Kind);
        if (kind == FightKind.SiteInhabitants || FightContentOf() is not { } fc) return;
        var ledger = rt.State.Wounds ?? _world.Wounds;
        if (ledger == null) { ledger = new WoundLedger(rt.State.Seed, _world.Clock.Now.Seconds); _world.AttachWounds(ledger); }
        var fight = rec.Restore(fc, ledger);
        int hero = fight.Fighters.FindIndex(f => f.Player);
        if (hero < 0) return;
        // the worn rows the fight's wear goes back to (review, game finding 5: not kept across a load, so a resumed fight wrote no wear back); the rows are as they were when it began,
        // since wear is written only when it ends. When they no longer match the pieces in the fight, nothing is written back.
        var rows = HeroWornRows(fc);
        var pieces = fight.Fighters[hero].Kit.Worn;
        if (rows.Count != pieces.Count || rows.Where((w, i) => w.ShapeId != pieces[i].ShapeId || w.MaterialId != pieces[i].MaterialId).Any()) _heroWorn.Clear();
        _rec.StartFight(fight, hero);
        _rec.SetSpeed(SimSpeed.Paused);
        if (kind is FightKind.Practice or FightKind.Tournament) _boutFight = (kind.Value, -1, BoutPurseOf(rec.Cause), BoutSettlementOf(rec.Cause, _yard?.Settlement ?? -1));   // the purse and the town come back with the record (review, game finding 2)
        else
        {
            var t = _travel;
            var ev = t?.Current is { } cur && cur.Id == rec.Cause ? cur : new EncounterEvent(rec.GameSec0, rec.Cause, "Ambush", 0, 0, 0, 0, 0, [], "");
            _roadFight = (ev, _heroLink?.View?.CoinMites ?? 0, -1);
            if (t != null) t.ScreenPending = true;
        }
        OpenFightView(fight, hero);
        if (_fightView != null && _hero != null) { var me = _fightView.Log.Info(hero); me.Name = _hero.Name; me.Female = _hero.Female; }
        string kindName = rec.Kind, cause = rec.Cause;
        rt.State.FightSource = () => _world.Fight is { } f ? Ravensguard.Sim.Save.FightRecord.Capture(f, kindName, cause) : null;
        _router?.ShowToast(L.T("fr_resumed"), 4f);
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
        string cause = BoutCause(purse, settlement);                 // a mid-bout save keeps the purse and the town (review, game finding 2)
        if (rt != null) rt.State.FightSource = () => _world.Fight is { } f ? Ravensguard.Sim.Save.FightRecord.Capture(f, setup.KindName, cause) : null;
        _router?.ShowToast(L.T(kind == FightKind.Practice ? "wm_bout_begin" : "wm_tourney_begin"), 3f);
        return true;
    }

    /// <summary>The fight record's cause of a bout: "bout|purse|settlement" (invariant digits), so a load mid-bout still pays a tournament's purse to the right town.</summary>
    static string BoutCause(long purse, int settlement) => string.Create(System.Globalization.CultureInfo.InvariantCulture, $"bout|{purse}|{settlement}");
    static long BoutPurseOf(string cause) => BoutPart(cause, 1, out long v) ? Math.Max(0, v) : 0;
    static int BoutSettlementOf(string cause, int fallback) => BoutPart(cause, 2, out long v) && v >= int.MinValue && v <= int.MaxValue ? (int)v : fallback;
    static bool BoutPart(string cause, int i, out long v)
    {
        v = 0;
        var p = cause.Split('|');
        return p.Length == 3 && p[0] == "bout" && long.TryParse(p[i], System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out v);
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
            _router?.ShowToast(won ? L.F("wm_tourney_won", UiKit.Money(b.Purse)) : L.T("wm_tourney_lost"), 5f);
        }
        _fightView.FinishLog();
        CloseRoadFight();
    }
}
