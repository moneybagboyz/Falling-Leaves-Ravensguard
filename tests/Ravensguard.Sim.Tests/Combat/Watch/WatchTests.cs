using Ravensguard.Content;
using Ravensguard.Sim.Autopilot;
using Ravensguard.Sim.Combat;
using Ravensguard.Sim.Combat.Aftermath;
using Ravensguard.Sim.Combat.Melee2;
using Ravensguard.Sim.Combat.Resolver2;
using Ravensguard.Sim.Combat.Watch;
using Ravensguard.Sim.Combat.Wounds;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Save;
using Ravensguard.Sim.Tests.Combat.Melee2;
using Ravensguard.Sim.Travel;
using Xunit.Abstractions;

namespace Ravensguard.Sim.Tests.Combat.Watch;

/// <summary>Watch mode (docs/specs/phase4/watch-mode.md, P4-F): gates WM-1 to WM-13.</summary>
[Collection("fight")]
public class WatchTests(FightFixture fx, ITestOutputHelper output)
{
    private static readonly WeaponInstance Sword = FightCalibration.Sword;

    private FighterKit Kit(string name, int team, int x, int skill = 45, WeaponInstance? weapon = null, string? shield = null, IReadOnlyList<WornItem>? worn = null, string stance = "balanced", int y = 0) =>
        new()
        {
            Name = name, Team = team, X = x, Y = y, FaceX = team == 0 ? FightMath.One : -FightMath.One, Weapon = weapon ?? Sword, Skill = skill, Shield = shield,
            Worn = worn ?? [new WornItem("tunic", "linen")], Stance = stance,
        };

    private WatchStance S(StancePreset p) => WatchStance.Of(fx.C, p);

    // ------------------------------------------------------------------ WM-1: the same AI

    [Fact]
    public void WM1_AWatchedHeroAndThePlainNpcWithTheSameStance_FightTheSameFight()
    {
        var presets = new[] { StancePreset.Balanced, StancePreset.Aggressive, StancePreset.Defensive, StancePreset.FleeHurt, StancePreset.Subdue };
        for (int seed = 1; seed <= 20; seed++)
        {
            var st = S(presets[seed % presets.Length]);
            var hero = FightCalibration.Skirmish(fx.C, (ulong)seed, 10);
            var npc = FightCalibration.Skirmish(fx.C, (ulong)seed, 10);
            hero.LodFighter = npc.LodFighter = 0;
            hero.Fighters[0].Player = true;
            hero.SetStance(0, st); hero.SetWatch(0, true);          // the player's hero, handed to the AI at tick 0
            npc.SetStance(0, st);                                   // the same person as a plain NPC
            Assert.NotNull(hero.Watch); Assert.Null(npc.Watch);
            for (int t = 0; t < 20 * 60 && !(hero.IsOver && npc.IsOver); t++)
            {
                hero.StepWithAi(); npc.StepWithAi();
                if (t % 50 == 0) Assert.Equal(npc.Hash(), hero.Hash());
            }
            Assert.Equal(npc.Hash(), hero.Hash());
            Assert.Equal(npc.Tick, hero.Tick);
        }
    }

    // ------------------------------------------------------------------ WM-2: the hand-off is seamless

    [Fact]
    public void WM2_HandingOffMidWindup_CompletesTheSwing_AndFromThenOnEqualsTheAllAiRun()
    {
        int checkedRuns = 0;
        for (int seed = 3; seed < 40 && checkedRuns < 5; seed++)
        {
            // the all-AI run, to find a tick at or after 100 where the hero (fighter 0) is winding up
            var probe = FightCalibration.Skirmish(fx.C, (ulong)seed, 4); probe.LodFighter = 0;
            int handAt = -1;
            for (int t = 0; t < 400; t++) { if (t >= 100 && probe.Fighters[0].Act == FAct.Windup && probe.Fighters[0].WindElapsed >= 1) { handAt = t; break; } probe.StepWithAi(); }
            if (handAt < 0) continue;
            var ai = FightCalibration.Skirmish(fx.C, (ulong)seed, 4); ai.LodFighter = 0;
            var pl = FightCalibration.Skirmish(fx.C, (ulong)seed, 4); pl.LodFighter = 0;
            pl.Fighters[0].Player = true;
            for (int t = 0; t < handAt; t++)
            {
                CombatBrain.Think(pl, pl.Fighters[0]);           // the player's hands do exactly what the brain would (a puppet), so the two runs can be compared
                pl.StepWithAi(); ai.StepWithAi();
            }
            Assert.Equal(FAct.Windup, pl.Fighters[0].Act);
            uint serial = pl.Fighters[0].Serial;
            pl.SetWatch(0, true);                                   // tick barrier: the AI takes the swing over
            for (int t = 0; t < 300; t++)
            {
                pl.StepWithAi(); ai.StepWithAi();
                Assert.Equal(ai.Hash(), pl.Hash());
            }
            // the swing in progress landed (or was met) exactly once
            int outcomes = pl.Events.Count(e => e.Tick > handAt && e.Tick <= handAt + 40 && e.Kind is FightEventKind.Hit or FightEventKind.Stopped or FightEventKind.Missed or FightEventKind.Whiff
                && e.Actor == 0 || e.Tick > handAt && e.Tick <= handAt + 40 && e.Kind is FightEventKind.Blocked or FightEventKind.Parried or FightEventKind.Dodged && e.Target == 0);
            Assert.True(pl.Fighters[0].Serial >= serial);
            Assert.True(outcomes >= 1, $"seed {seed}: the swing at the hand-off was lost");
            checkedRuns++;
        }
        Assert.True(checkedRuns >= 3);

        // a real player's held swing (released by nobody) also completes once under the AI: no dropped or doubled blow
        var w = fx.Duel(seed: 7, aPlayer: true).W;
        w.LodFighter = 0;
        w.Input(0, new FightInput(FightInputKind.AttackDown, -1, -1, -1));
        w.StepWithAi(); w.StepWithAi();
        Assert.Equal(FAct.Windup, w.Fighters[0].Act); Assert.False(w.Fighters[0].Req.Auto);
        uint s0 = w.Fighters[0].Serial;
        w.SetWatch(0, true);
        int start = w.Tick;
        for (int t = 0; t < 30; t++) w.StepWithAi();
        int next = w.Events.Where(e => e.Tick > start && e.Actor == 0 && e.Kind == FightEventKind.Windup).Select(e => e.Tick).DefaultIfEmpty(int.MaxValue).Min();
        var mine = w.Events.Where(e => e.Tick > start && e.Tick < next && (e.Actor == 0 && e.Kind is FightEventKind.Hit or FightEventKind.Stopped or FightEventKind.Missed or FightEventKind.Whiff
            || e.Target == 0 && e.Kind is FightEventKind.Blocked or FightEventKind.Parried or FightEventKind.Dodged)).ToList();
        Assert.True(mine.Count == 1, $"the held swing resolved {mine.Count} times before the next");
        Assert.True(w.Fighters[0].Serial >= s0);
    }

    // ------------------------------------------------------------------ WM-3: take-back

    [Fact]
    public void WM3_TakingBackControl_AppliesTheInputAtThatTick_AndTheBrainDrawsNothingForTheHeroAfter()
    {
        var w = FightCalibration.Skirmish(fx.C, 11, 3); w.LodFighter = 0;
        w.Fighters[0].Player = true; w.SetWatch(0, true);
        var draws = new List<(int Tick, int Fighter, uint Stream)>();
        w.AiDrawLog = (f, s) => draws.Add((w.Tick, f, s));
        for (int t = 0; t < 120; t++) w.StepWithAi();
        Assert.Contains(draws, d => d.Fighter == 0);              // the brain was thinking for the hero
        // wait until the hero is free to swing, then take back with a swing (the input of this barrier)
        for (int t = 0; t < 200 && w.Fighters[0].Act is not (FAct.Idle or FAct.Block); t++) w.StepWithAi();
        int T = w.Tick;
        uint serial = w.Fighters[0].Serial;
        w.Input(0, new FightInput(FightInputKind.Watch, 0, 1));
        w.Input(0, new FightInput(FightInputKind.AttackDown, -1, -1, -1));
        w.StepWithAi();
        Assert.True(w.Fighters[0].Player);
        Assert.Equal(FAct.Windup, w.Fighters[0].Act);              // the new command applied at the tick it was given
        Assert.Equal(serial + 1, w.Fighters[0].Serial); Assert.False(w.Fighters[0].Req.Auto);
        for (int t = 0; t < 200; t++) w.StepWithAi();
        Assert.DoesNotContain(draws, d => d.Fighter == 0 && d.Tick >= T);
        Assert.Contains(draws, d => d.Fighter != 0 && d.Tick >= T);   // the others still think
        Assert.False(w.Watch!.On);
    }

    // ------------------------------------------------------------------ WM-4: subdue

    [Fact]
    public void WM4_Subdue_LandsNoEdgeOrPoint_NoBlowAtHeadOrNeck_KnocksOutMost_KillsAlmostNone()
    {
        int n = 200, ko = 0, deaths = 0, edged = 0, headNeck = 0, landed = 0;
        for (int i = 0; i < n; i++)
        {
            var w = new FightWorld(fx.C, (ulong)(5000 + i), (ulong)(i + 1), 8 * 3600) { RecordEvents = false };
            var a = w.Add(Kit("Hero", 0, -2000, skill: 60, stance: "subdue"));
            var b = w.Add(Kit("Thug", 1, 2000, skill: 35, weapon: new WeaponInstance("club", "oak")));
            w.OnBlow = (att, t, r, s) =>
            {
                if (att.Id != a.Id || !r.Hit || r.WoundCount == 0) return;
                landed++;
                if (s.Weapon.Attacks[s.Attack].Type != AttackType.Blunt) edged++;
                if (t.Plan.Parts[r.Part] is var p && (p.BaseId is "head" or "neck" || p.IsHosted && t.Plan.Parts[p.Host].BaseId is "head" or "neck")) headNeck++;
            };
            while (!w.IsOver && w.Tick < 20 * 240) w.StepWithAi();
            for (int k = 0; k < 20 * 60 && !b.Book.Dead; k++) w.StepWithAi();    // a minute after: a knock-out who bleeds to death counts as a death
            if (b.Act == FAct.Dead || b.Book.Dead) deaths++;
            else if (b.Act == FAct.Down) ko++;
        }
        output.WriteLine($"subdue: landed {landed}, ko {ko}/{n}, deaths {deaths}, edged {edged}, head/neck {headNeck}");
        Assert.Equal(0, edged); Assert.Equal(0, headNeck);
        Assert.True(landed > n, "the subdue stance did fight");
        Assert.True(ko * 1000 >= 600 * n, $"knock-outs {ko}/{n}");
        Assert.True(deaths * 1000 <= 50 * n, $"deaths {deaths}/{n}");
    }

    // ------------------------------------------------------------------ WM-5: flee when hurt

    [Fact]
    public void WM5_FleeHurt_FleesAtFifteenPercentBloodLoss_ThenTreatsItself()
    {
        // a hardened hero (toughness 40, courage 100) fights on with a cut in the shield arm that bleeds (a vein, 10-20 ml/s: blood is what the stance has to watch, not pain), against a
        // careful shield bearer; of the duels where the blood crossed 13% while the hero still fought, 95% broke off at 15% +-2
        int n = 200, reached = 0, onTime = 0, fled = 0, treatedOnTime = 0;
        var at = new List<int>();
        for (int i = 0; i < n; i++)
        {
            var w = new FightWorld(fx.C, (ulong)(7000 + i), (ulong)(i + 1), 8 * 3600) { RecordEvents = true, Rules = FightRules.For(FightKind.Encounter) };
            var a = w.Add(Kit("Hero", 0, -2000, skill: 45, stance: "flee_hurt") with { Courage = 100, Toughness = 40 });
            w.Add(Kit("Shield", 1, 2000, skill: 55, shield: "heater", stance: "defensive", worn: FightCalibration.Mailed.Worn));
            int leg = a.Plan.IndexOf("upper_arm_l"); var pl = a.Plan.Parts[leg];
            int vi = Array.FindIndex(pl.Structures, x => x.Kind == StructureKind.Vein);
            int rate = 100 + 5 * (i % 21);
            var cut = vi >= 0
                ? new WoundV2 { Part = (short)leg, Tissue = (short)pl.Structures[vi].Tissue.Index, Structure = (short)vi, Organ = -1, Kind = WoundKind.Cut, DepthPm = 500, Flags = WoundFlags.VeinHit, BleedX10 = rate }
                : new WoundV2 { Part = (short)leg, Tissue = -1, Structure = -1, Organ = -1, Kind = WoundKind.Cut, DepthPm = 500, BleedX10 = rate };
            a.Book.Apply(a.Plan, [cut], w.GameSec, w.Tick, 0, 0, false);
            int routLoss = -1, firstClear = -1, treatAt = -1;
            bool reachedHere = false;
            for (int t = 0; t < 20 * 300 && treatAt < 0; t++)
            {
                w.StepWithAi();
                if (a.Book.LossPm >= 150 && a.Active && !a.Routing) reachedHere = true;      // the blood reached the mark while the hero still fought (an earlier flee by pain is the stance's pain trigger)
                if (routLoss < 0 && a.Routing) routLoss = a.Book.LossPm;
                if (routLoss >= 0 && firstClear < 0 && !w.Fighters.Any(e => e.Team != a.Team && e.Active && w.DistMm(a, e) <= 20_000)) firstClear = w.Tick;
                foreach (var e in w.Events.TakeLast(3)) if (treatAt < 0 && e.Kind == FightEventKind.Treated && e.Actor == a.Id && e.Tick == w.Tick) treatAt = e.Tick;
                if (w.IsOver && firstClear < 0) break;
            }
            if (reachedHere || routLoss >= 130) { reached++; if (routLoss is >= 130 and <= 170) onTime++; }
            if (routLoss >= 0) at.Add(routLoss);
            if (firstClear >= 0 && Treatment.WorstBleeder(a.Book, -1, w.GameSec) is var _ && routLoss >= 0) { fled++; if (treatAt >= 0 && treatAt - firstClear <= 8 * 20) treatedOnTime++; }
        }
        output.WriteLine($"flee_hurt: reached {reached}, on time {onTime}, clear of enemies {fled}, treated in 8 s {treatedOnTime}; rout losses {string.Join(",", at.Take(60))}");
        Assert.True(reached >= 75, $"only {reached} duels reached 15% blood loss while fighting");
        Assert.True(onTime * 1000 >= 950 * reached, $"fled at 15% +-2 in {onTime} of {reached}");
        Assert.True(fled >= 20, $"got 20 m clear {fled}");       // the others bled to the collapse (30%) from what the fight opened before they got clear
        Assert.True(treatedOnTime * 1000 >= 950 * fled, $"treated itself within 8 s of reaching 20 m in {treatedOnTime} of {fled}");
    }

    // ------------------------------------------------------------------ WM-6: protect

    [Fact]
    public void WM6_Protect_StaysWithinThreeMetresOfTheCharge()
    {
        var def = fx.Db.Get<EncounterDef>("bandit_ambush_small");
        long near = 0, ticks = 0;
        for (int i = 0; i < 40; i++)
        {
            var hero = Kit("Hero", 0, 0, skill: 50, shield: "heater", worn: [new("tunic", "linen"), new("gambeson", "linen")]) with { Person = 7 };
            var charge = Kit("Edda", 0, -800, skill: 30, y: 1000, stance: "defensive") with { Person = 8 };      // a companion who holds her ground (she does not chase routers)
            var ev = new EncounterEvent(500_000 + i * 3600, def.Id, def.Kind, 0, 0, 0, 0, 1, [101, 102, 103], def.Bark);
            var setup = FightSetup.Encounter(fx.C, def, ev, hero, [charge], (ulong)(40 + i));
            var w = setup.Start(fx.C, null);
            w.SetStance(0, S(StancePreset.Protect), 1); w.SetWatch(0, true);
            var h = w.Fighters[0]; var x = w.Fighters[1];
            for (int t = 0; t < 20 * 90 && !w.IsOver; t++)
            {
                w.StepWithAi();
                if (!h.Active || x.Act is FAct.Dead or FAct.Fled || x.Act == FAct.Surrendered) continue;
                ticks++; if (w.DistMm(h, x) <= 3000) near++;
            }
        }
        output.WriteLine($"protect: within 3 m in {near} of {ticks} ticks");
        Assert.True(ticks > 2000);
        // over 240 seeds the protector is within 3 m 887 permille of the ticks before the Phase 4 close-out weapon changes and 889 after; these 40 read 905 before and 862 after
        Assert.True(near * 1000 >= 850 * ticks, $"within 3 m in {near * 1000 / Math.Max(1, ticks)} permille of ticks");
    }

    // ------------------------------------------------------------------ WM-7: interrupts

    [Fact]
    public void WM7_WatchDangerPausesAtThirtyPercent_WatchLosingAtBadOdds_WatchOverAtTheEnd_TwentySevenKinds()
    {
        Assert.Equal(27, Enum.GetValues<InterruptKind>().Length);
        // danger: the watched hero's blood loss reaches 30%
        var w = fx.Duel(seed: 2, gap: 9000).W; w.LodFighter = 0;
        w.Add(Kit("Ally", 0, -6000));                                // the fight goes on when the hero collapses
        w.Fighters[0].Player = true; w.SetWatch(0, true);
        for (int t = 0; t < 8; t++) w.StepWithAi();
        Assert.DoesNotContain(w.Watch!.Signals, s => s.Kind == InterruptKind.WatchDanger);
        var book = w.Fighters[0].Book;
        book.BloodUl = book.BloodMaxUl * 69 / 100;                 // 31% lost
        for (int t = 0; t < 8; t++) w.StepWithAi();
        var sig = w.Watch.TakeSignals();
        Assert.Contains(sig, s => s.Kind == InterruptKind.WatchDanger && s.Value == 1);
        var bus = new InterruptBus();
        var routed = WatchInterrupts.Route(sig, bus, 1000);
        Assert.Contains(routed, r => r.Signal.Kind == InterruptKind.WatchDanger && r.Policy == InterruptPolicy.Pause);
        Assert.True(bus.Paused);
        // declined: no new danger pause for 5 s
        w.Input(0, new FightInput(FightInputKind.DeclineDanger));
        w.Watch.DangerActive = false;
        for (int t = 0; t < 40; t++) w.StepWithAi();
        Assert.DoesNotContain(w.Watch.TakeSignals(), s => s.Kind == InterruptKind.WatchDanger);

        // losing: alone against five armoured men, the odds stay under 25% for 3 s
        var l = new FightWorld(fx.C, 9, 9, 8 * 3600);
        l.Add(Kit("Hero", 0, 0, skill: 30) with { Courage = 100 }, player: true);
        l.Add(Kit("Lad", 0, -1500, skill: 20, y: 1000) with { Courage = 100 });
        for (int i = 0; i < 5; i++) l.Add(Kit($"Sergeant {i}", 1, 14_000, skill: 60, shield: "heater", y: (i - 2) * 1200, worn: FightCalibration.Mailed.Worn));
        l.SetWatch(0, true);
        for (int t = 0; t < 70; t++) l.StepWithAi();
        Assert.True(l.Watch!.OddsPm < 250, $"odds {l.Watch.OddsPm}");
        var ls = l.Watch.TakeSignals();
        Assert.Single(ls, s => s.Kind == InterruptKind.WatchLosing);
        var bus2 = new InterruptBus();
        Assert.Contains(WatchInterrupts.Route(ls, bus2, 2000), r => r.Signal.Kind == InterruptKind.WatchLosing && r.Policy == InterruptPolicy.Notify);
        for (int t = 0; t < 200; t++) l.StepWithAi();
        Assert.DoesNotContain(l.Watch.TakeSignals(), s => s.Kind == InterruptKind.WatchLosing);   // once a fight

        // over: the fight ends while watched
        var o = fx.Duel(seed: 5).W; o.Fighters[0].Player = true; o.SetWatch(0, true);
        var all = new List<WatchSignal>();
        for (int t = 0; t < 20 * 240 && !o.IsOver; t++) { o.StepWithAi(); all.AddRange(o.Watch!.TakeSignals()); }
        o.StepWithAi(); all.AddRange(o.Watch!.TakeSignals());
        Assert.True(o.IsOver);
        Assert.Single(all, s => s.Kind == InterruptKind.WatchOver);
        Assert.Equal(InterruptPolicy.Pause, InterruptBus.DefaultPolicy(InterruptKind.WatchOver));
        // through autopilot's router (a person on autopilot): logged once, with the AI's answer when set to Ai
        var ap = new AutopilotState { Enabled = true };
        ap.Policy[(int)InterruptKind.WatchOver] = InterruptPolicy.Ai;
        var router = new InterruptRouter(new AutopilotLog());
        var r3 = WatchInterrupts.Route([new WatchSignal(1, InterruptKind.WatchOver, 0, 1)], new InterruptBus { Autopilot = true }, 3000, ap, router);
        Assert.Equal(InterruptPolicy.Ai, r3[0].Policy);
        Assert.Equal(AiResponse.TreatAfterFight, InterruptRouter.AiAnswer(InterruptKind.WatchOver));
    }

    // ------------------------------------------------------------------ WM-8: Ironman

    [Fact]
    public void WM8_WatchModeIsAllowedInIronman_AndAWatchedHerosDeathGoesToSuccession()
    {
        var w = new FightWorld(fx.C, 13, 13, 8 * 3600);
        w.Add(Kit("Hero", 0, 0, skill: 20, worn: [new("tunic", "linen")]) with { Stance = "aggressive" }, player: true);
        for (int i = 0; i < 4; i++) w.Add(Kit($"Reaver {i}", 1, 3000, skill: 60, weapon: new WeaponInstance("great_axe", "steel"), y: (i - 2) * 900, stance: "aggressive"));
        Assert.True(WatchRules.Allowed(ironman: true));
        w.SetWatch(0, true);
        var h = w.Fighters[0];
        for (int t = 0; t < 20 * 400 && h.Act != FAct.Dead; t++) w.StepWithAi();
        if (h.Act != FAct.Dead) { h.Book.BloodUl = 0; w.StepWithAi(); }          // a hero who lies bleeding dies of it in the end
        Assert.Equal(FAct.Dead, h.Act);
        var req = DefeatRequest.Of(w, 0, WinnerKind.Bandits);
        Assert.Equal(LoserState.Dead, req.State);
        var rules = AftermathRules.For(fx.Db);
        var outcome = DefeatTable.Decide(rules, 1, w.FightId, WinnerKind.Bandits, null, new LoserFacts(new EntityId(7), LoserRole.Hero, req.State, false, false, true, 0, 0));
        Assert.Equal(DefeatKind.Killed, outcome.Kind);
        Assert.True(WatchRules.GoesToSuccession(outcome, ironman: true));
    }

    // ------------------------------------------------------------------ WM-9: speed

    [Fact]
    public void WM9_TheSameFightAtOneTwoFourAndEightTimes_GivesTheSameFightHash()
    {
        ulong? first = null;
        foreach (int speed in new[] { 1, 2, 4, 8 })
        {
            var w = FightCalibration.Skirmish(fx.C, 17, 6); w.LodFighter = 0;
            w.Fighters[0].Player = true; w.SetWatch(0, true);
            int steps = 0;
            while (steps < 1600)
            {
                int n = WatchSpeed.StepsPerFrame(speed, frameMs: 3, budgetMs: 25);       // a frame steps the bubble `speed` times, never a longer step
                for (int k = 0; k < n && steps < 1600; k++, steps++) w.StepWithAi();
            }
            Assert.Equal(1600, w.Tick);
            first ??= w.Hash();
            Assert.Equal(first.Value, w.Hash());
        }
        // over budget the speed drops a level (and the toast says so); never a longer step
        Assert.Equal(4, WatchSpeed.StepsPerFrame(8, frameMs: 30, budgetMs: 25));
        Assert.Equal(1, WatchSpeed.StepsPerFrame(1, frameMs: 30, budgetMs: 25));
        Assert.Equal(4, WatchSpeed.Capped(8, 4));
    }

    // ------------------------------------------------------------------ WM-10: one no-lethal flag

    [Fact]
    public void WM10_TheWatchNoLethalAndAutopilotsNoLethalForce_AreOneFlag()
    {
        var hero = new EntityId(EntityId.Make(IdKind.Person, 1).Value);
        var ap = new AutopilotBook();
        ap.States[hero] = new AutopilotState { Person = hero, Enabled = true };
        ap.States[hero].Limits.NoLethalForce = false;
        var book = new WatchBook();
        Assert.False(book.NoLethal(fx.C, hero, ap));
        // set from the stance panel: the limit shows it
        book.SetNoLethal(fx.C, hero, true, ap);
        Assert.True(ap.States[hero].Limits.NoLethalForce);
        Assert.True(book.StanceOf(fx.C, hero, ap).NoLethal);
        // set from the autopilot panel: the stance shows it
        ap.States[hero].Limits.NoLethalForce = false;
        Assert.False(book.StanceOf(fx.C, hero, ap).NoLethal);
        Assert.False(book.NoLethal(fx.C, hero, ap));
        // and the stance the fight reads is the subdue filter (blunt only, no head)
        book.SetNoLethal(fx.C, hero, true, ap);
        var def = book.StanceOf(fx.C, hero, ap).ToDef(fx.C);
        Assert.True(def.NoLethal);
        // the desk applies the same through a hero command
        var b2 = new WatchBook();
        Assert.True(WatchDesk.Apply(b2, WatchDesk.NoLethalTag(hero, true), fx.C, ap));
        Assert.True(ap.States[hero].Limits.NoLethalForce);
    }

    // ------------------------------------------------------------------ WM-11: the odds

    [Fact]
    public void WM11_TheOddsAgreeWithSampledOutcomes_WithinFifteenPoints()
    {
        int n = 500;
        var preds = new int[n]; var wins = new bool[n];
        string[] weapons = ["arming_sword", "axe", "mace", "spear", "club", "dagger"];
        var kits = new[] { FightCalibration.Unarmoured, FightCalibration.Padded, FightCalibration.Mailed };
        Parallel.For(0, n, i =>
        {
            ulong seed = (ulong)(90_000 + i);
            int R(uint k, int m) => (int)(DetRng.NextUInt(seed, 0, 1, 77, k) % (uint)m);
            var w = new FightWorld(fx.C, seed, seed, 8 * 3600) { RecordEvents = false };
            int na = 1 + R(0, 4), ne = 1 + R(1, 4);
            for (int s = 0; s < 2; s++)
                for (int k = 0; k < (s == 0 ? na : ne); k++)
                {
                    uint d = (uint)(10 + s * 40 + k * 4);
                    string wp = weapons[R(d, weapons.Length)];
                    string? sh = wp is "spear" ? null : R(d + 1, 3) == 0 ? "round_shield" : null;
                    w.Add(Kit($"{s}{k}", s, s == 0 ? -4000 : 4000, skill: 20 + R(d + 2, 45), weapon: new WeaponInstance(wp, wp is "club" ? "oak" : "iron"), shield: sh,
                        worn: kits[R(d + 3, kits.Length)].Worn, y: (k * 2 - 3) * 800));
                }
            preds[i] = RiskEstimate.OddsOf(w, 0);
            while (!w.IsOver && w.Tick < 20 * 180) w.StepWithAi();
            wins[i] = w.DecidedWinner == 0;
        });
        var lines = new List<string>();
        int worst = 0;
        for (int lo = 0; lo < 1000; lo += 200)
        {
            var bin = Enumerable.Range(0, n).Where(i => preds[i] >= lo && preds[i] < lo + 200).ToList();
            if (bin.Count == 0) continue;
            int predicted = (int)bin.Average(i => preds[i]), actual = bin.Count(i => wins[i]) * 1000 / bin.Count;
            lines.Add($"[{lo},{lo + 200}) n {bin.Count} predicted {predicted} actual {actual}");
            if (bin.Count >= 30) worst = Math.Max(worst, Math.Abs(predicted - actual));
        }
        int meanP = (int)preds.Average(), meanA = wins.Count(x => x) * 1000 / n;
        output.WriteLine(string.Join("\n", lines) + $"\nmean predicted {meanP} actual {meanA}");
        Assert.True(Math.Abs(meanP - meanA) <= 150, $"mean predicted {meanP}, actual {meanA}");
        Assert.True(worst <= 150, string.Join("; ", lines));
    }

    // ------------------------------------------------------------------ WM-12: squad formation

    [Fact]
    public void WM12_TheLeadersTacticsPicksTheFormation_AndAnFKeyOverridesIt()
    {
        Assert.Equal(Formation.ShieldWall, SquadTactics.Choose(40, enemyRanged: true, enemyCavalry: false, shieldBearers: 3));
        Assert.Equal(Formation.Line, SquadTactics.Choose(40, enemyRanged: false, enemyCavalry: false, shieldBearers: 3));
        Assert.Equal(Formation.Loose, SquadTactics.Choose(10, enemyRanged: true, enemyCavalry: false, shieldBearers: 3));
        // in a fight: the hero (Tactics 40) leads three shield bearers against archers
        FightWorld Squad(int tactics)
        {
            var w = new FightWorld(fx.C, 21, 21, 8 * 3600);
            w.Add(Kit("Hero", 0, 0, shield: "heater") with { Tactics = tactics }, player: true);
            for (int i = 0; i < 3; i++) w.Add(Kit($"Spear {i}", 0, 0, shield: "heater", y: (i + 1) * 900));
            for (int i = 0; i < 3; i++) w.Add(new FighterKit { Name = $"Bow {i}", Team = 1, Launcher = "longbow", Ammo = "bodkin_arrow", AmmoCount = 12, Skill = 40, X = 30_000, Y = i * 1500, FaceX = -FightMath.One, Worn = [new("tunic", "linen")] });
            w.CaptainFormation(0, 0, tactics);
            return w;
        }
        var wall = Squad(40);
        Assert.Equal(Formation.ShieldWall, wall.OrdersOf(0).Formation);
        Assert.Equal(Formation.Loose, Squad(10).OrdersOf(0).Formation);
        // the wall stands and braces while the archers are far
        for (int t = 0; t < 80; t++) wall.StepWithAi();
        Assert.True(wall.Fighters.Skip(1).Take(3).Count(f => wall.Braced(f)) >= 2);
        // the F-key wins over the captain, and stays when the captain looks again
        wall.Input(0, new FightInput(FightInputKind.Formation, (int)Formation.Loose));
        Assert.Equal(Formation.Loose, wall.OrdersOf(0).Formation);
        wall.CaptainFormation(0, 0, 40);
        Assert.Equal(Formation.Loose, wall.OrdersOf(0).Formation);
        Assert.Equal(Formation.ShieldWall, SquadTactics.OfKey(2));
    }

    // ------------------------------------------------------------------ WM-13: zero-player

    [Fact]
    public void WM13_ZeroPlayer_AutopilotWatchAndAllInterruptsOnAi_TwentyAmbushesEndWithAnOutcome()
    {
        var def = fx.Db.Get<EncounterDef>("bandit_ambush_small");
        var outcomes = new Dictionary<string, int>();
        for (int i = 0; i < 20; i++)
        {
            var ap = new AutopilotState { Enabled = true };
            for (int k = 0; k < ap.Policy.Length; k++) ap.Policy[k] = InterruptPolicy.Ai;
            var router = new InterruptRouter(new AutopilotLog());
            var bus = new InterruptBus { Autopilot = true };
            var hero = Kit("Hero", 0, 0, skill: 50 + i % 10, shield: "heater", worn: FightCalibration.Mailed.Worn) with { Person = 7 };
            var mate = Kit("Brand", 0, 0, skill: 40, weapon: FightCalibration.Spear, worn: FightCalibration.Padded.Worn) with { Person = 8 };
            var ev = new EncounterEvent(700_000 + i * 7200, def.Id, def.Kind, 0, 0, 0, 0, 1, [101, 102, 103], def.Bark);
            var setup = FightSetup.Encounter(fx.C, def, ev, hero, i % 4 == 3 ? [] : [mate], (ulong)(300 + i)) with { HeroStance = S(StancePreset.Balanced), StartWatched = true };
            // autopilot's choice when Attacked is on Ai: fight at odds >= 1000 - MaxRiskPm, else run
            int odds = RiskEstimate.OddsOf(setup, fx.C);
            if (!RiskEstimate.AutopilotFights(odds, ap.Limits.MaxRiskPm)) { outcomes["ran"] = outcomes.GetValueOrDefault("ran") + 1; continue; }
            var w = setup.Start(fx.C, new WoundLedger(1, ev.Sec));
            Assert.True(w.Watch!.On);
            bool paused = false;
            for (int t = 0; t < 20 * 300 && !w.IsOver; t++)
            {
                w.StepWithAi();
                foreach (var r in WatchInterrupts.Route(w.Watch.TakeSignals(), bus, ev.Sec + t, ap, router)) paused |= r.Policy == InterruptPolicy.Pause;
            }
            Assert.True(w.IsOver, $"ambush {i} did not end");
            Assert.False(paused, "nothing paused a zero-player run");
            var h = w.Fighters[0];
            string o = w.DecidedWinner == 0 ? "won" : h.Act switch { FAct.Fled => "fled", FAct.Dead => "killed", FAct.Surrendered => "yielded", FAct.Down => "down", _ => "lost" };
            outcomes[o] = outcomes.GetValueOrDefault(o) + 1;
        }
        output.WriteLine(string.Join(", ", outcomes.Select(kv => $"{kv.Key} {kv.Value}")));
        Assert.Equal(20, outcomes.Values.Sum());
        Assert.True(outcomes.GetValueOrDefault("ran") < 15, "most ambushes were fought");
    }

    // ------------------------------------------------------------------ determinism, saves, the reserved stream

    [Fact]
    public void TheReservedStream0x490_IsNeverDrawn()
    {
        Assert.Equal(0x490u, RngStreams.WatchReserved);
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../.."));
        foreach (var f in Directory.GetFiles(Path.Combine(root, "src", "Ravensguard.Sim"), "*.cs", SearchOption.AllDirectories))
        {
            if (f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") || f.EndsWith("RngStreams.cs", StringComparison.Ordinal)) continue;
            var src = File.ReadAllText(f);
            Assert.DoesNotContain("WatchReserved", src, StringComparison.Ordinal);
            Assert.DoesNotContain("0x490", src, StringComparison.Ordinal);
        }
        // the watch folder draws nothing at all
        foreach (var f in Directory.GetFiles(Path.Combine(root, "src", "Ravensguard.Sim", "Combat", "Watch"), "*.cs")) Assert.DoesNotContain("DetRng", File.ReadAllText(f), StringComparison.Ordinal);
    }

    [Fact]
    public void AWatchedFight_SavesAndLoadsByteIdentically_AndGoesOnTheSame()
    {
        var w = FightCalibration.Skirmish(fx.C, 23, 4); w.LodFighter = 0; w.Rules = FightRules.For(FightKind.Encounter);
        w.Fighters[0].Player = true; w.SetStance(0, S(StancePreset.Protect), 1); w.SetWatch(0, true);
        w.SetFormation(0, Formation.Loose); w.Order(0, SquadOrderKind.Focus, 5);
        for (int t = 0; t < 140; t++) w.StepWithAi();
        var bytes = FightRecord.Capture(w, "encounter").ToBytes();
        var loaded = FightRecord.FromBytes(bytes).Restore(fx.C);
        Assert.Equal(bytes, FightRecord.Capture(loaded, "encounter").ToBytes());
        Assert.Equal(w.Hash(), loaded.Hash());
        Assert.Equal(w.Watch!.Hash(), loaded.Watch!.Hash());
        for (int t = 0; t < 600; t++) { w.StepWithAi(); loaded.StepWithAi(); }
        Assert.Equal(w.Hash(), loaded.Hash());
        Assert.Equal(w.Watch.Hash(), loaded.Watch.Hash());
        // the run's watch book in the optional section
        var book = new WatchBook { KeepWatching = true };
        book.Set(new EntityId(7), S(StancePreset.Defensive).WithFlag(StanceFlags.NoLethal, true));
        var b1 = SaveCodec.SerializeCompressed(WatchSave.Capture(book));
        var back = SaveCodec.DeserializeCompressed<WatchSave>(b1).Restore();
        Assert.Equal(book.Hash(), back.Hash());
        Assert.Equal(b1, SaveCodec.SerializeCompressed(WatchSave.Capture(back)));
    }

    [Fact]
    public void AWatchedRoadFight_IsRecordedAndReplaysToTheSameHashes()
    {
        static Ravensguard.Sim.Live.LiveWorld Field()
        {
            var g = new Ravensguard.Sim.World.LevelGrid(40, 20, 0, 1);
            g.FillRect(0, 0, 39, 19, 0, true);
            return new Ravensguard.Sim.Live.LiveWorld(g, new Ravensguard.Sim.Lighting.LightGrid(g), new Ravensguard.Sim.World.Cell3(5, 5, 0), GameTime.FromCalendar(1, Season.Spring, 8, 12));
        }
        var def = fx.Db.Get<EncounterDef>("bandit_ambush_small");
        var ev = new EncounterEvent(500_000, def.Id, def.Kind, 0, 0, 0, 0, 1, [101, 102, 103], def.Bark);
        var setup = FightSetup.Encounter(fx.C, def, ev, Kit("Aldwin", 0, 0, skill: 50, shield: "heater") with { Person = 7 }, [Kit("Brand", 0, 0, skill: 40) with { Person = 8 }], 5);
        var live = Field();
        var rec = new Ravensguard.Sim.Diagnostics.SessionRecorder(live);
        rec.StartFight(setup.Start(fx.C, null), 0);
        for (int t = 0; t < 700; t++)
        {
            if (t == 20) rec.FightInput(new FightInput(FightInputKind.Watch, 1, 1));
            if (t == 60) rec.FightInput(S(StancePreset.Protect).AsInput(1));
            if (t == 300) rec.FightInput(new FightInput(FightInputKind.Watch, 0, 1));
            if (t == 301) rec.FightInput(new FightInput(FightInputKind.AttackDown, -1, -1, -1));
            if (t == 312) rec.FightInput(new FightInput(FightInputKind.AttackUp));
            if (t == 400) rec.FightInput(new FightInput(FightInputKind.Watch, 1, 1));
            rec.Step();
        }
        Assert.True(live.Fight!.Watch!.EverOn);
        Assert.Equal(FightKind.Ambush, live.Fight.SetupKind);
        var (trace, csv) = rec.Export();
        var again = Field();
        again.FightFactory = id => FightRecord.FromBytes(rec.FightStarts[id]).Restore(fx.C);
        var res = Ravensguard.Sim.Diagnostics.ReplayRunner.Run(again, Ravensguard.Sim.Diagnostics.CommandLog.FromBytes(trace), Ravensguard.Sim.Diagnostics.StateHashTrail.FromCsv(csv));
        Assert.True(res.Matched, res.Describe());
        Assert.Equal(live.Fight.Hash(), again.Fight!.Hash());
        Assert.Equal(live.Fight.Watch.Hash(), again.Fight.Watch!.Hash());
        Assert.True(again.Fight.Rules.TreatInFight);
    }

    [Fact]
    public void StancesAreTheContentRows_AndShiftF6CyclesThem()
    {
        var b = S(StancePreset.Balanced);
        Assert.Same(fx.C.Stance("balanced"), b.ToDef(fx.C));                       // nothing overridden: the NPC row itself
        var s = b;
        var seen = new List<string>();
        for (int i = 0; i < 6; i++) { seen.Add(s.Id); s = s.Next(fx.C); }
        Assert.Equal(["balanced", "defensive", "protect", "subdue", "flee_hurt", "aggressive"], seen);
        Assert.Equal(b, WatchStance.Unpack(b.PackA(), 0));
        Assert.Equal(b, WatchStance.FromInput(b.AsInput(-1)));
        Assert.True(fx.C.Stance("flee_hurt").FleeChecks); Assert.False(fx.C.Stance("balanced").FleeChecks);
        Assert.Equal(20, fx.C.Stance("aggressive").PursueM);
    }
}
