using System.Diagnostics;
using Ravensguard.Content;
using Ravensguard.Sim.Abstract;
using Ravensguard.Sim.Combat;
using Ravensguard.Sim.Combat.Abstract;
using Ravensguard.Sim.Combat.Melee2;
using Ravensguard.Sim.Combat.Morale;
using Ravensguard.Sim.Combat.Mounted;
using Ravensguard.Sim.Combat.Wounds;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Tests.Combat.Melee2;
using Xunit.Abstractions;
using static Ravensguard.Sim.Combat.Abstract.AbstractScenarios;

namespace Ravensguard.Sim.Tests.Combat.Abstract;

/// <summary>Abstract battles v1 (abstract-battles-v1 8): the wound tables built by the live fight, the exchange, the equivalence with the tactical fights, the aftermath and the chronicle.</summary>
[Collection("fight")]
public class AbstractBattleTests(FightFixture fx, ITestOutputHelper output)
{
    private WoundTables T => WoundTables.For(fx.Db) ?? throw new InvalidOperationException("no wound tables in content/base/abstract");

    private static readonly (WeaponClass W, ArmourClass A, int Band, bool Shield)[] Probe =
    [
        (WeaponClass.Blade, ArmourClass.Padded, 3, false), (WeaponClass.Longbow, ArmourClass.Mail, 2, true), (WeaponClass.Lance, ArmourClass.Padded, 3, false),
    ];

    [Fact]
    public void AB1_TheTables_AreTheContentsOwn_AndRebuildByteForByte()
    {
        var meta = fx.Db.All<WoundTableMetaDef>().Single();
        Assert.Equal(WoundTables.ContentHash(fx.Db), meta.ContentHash);                  // a stale table (content changed since the build) fails here: rebuild with --abstract tables
        Assert.Equal(WoundTables.BuilderVersion, meta.Version);
        Assert.Equal(AbstractClasses.WeaponClasses * AbstractClasses.ArmourClasses * AbstractClasses.Bands * 2, T.CellsBuilt);
        var rows = fx.Db.All<WoundTableDef>().ToDictionary(r => r.Id);
        foreach (var (w, a, band, shield) in Probe)
        {
            var c1 = WoundTableBuilder.BuildCell(fx.C, w, a, band, shield, meta.BlowsPerCell);
            var c2 = WoundTableBuilder.BuildCell(fx.C, w, a, band, shield, meta.BlowsPerCell);
            var built = WoundTableBuilder.Row(c1);
            Assert.Equal(built, WoundTableBuilder.Row(c2));                             // reproducible
            var shipped = rows[$"{AbstractClasses.Name(w)}.{AbstractClasses.Name(a)}"].Cells.Single(c => c[0] == band && c[1] == (shield ? 1 : 0));
            Assert.Equal(built, shipped.Select(x => (long)x).ToList());                  // and the same as the shipped row: the file is what the builder writes
        }
        // the written text is fixed for fixed cells (no culture, no dictionary order)
        var cells = Probe.Select(p => WoundTableBuilder.BuildCell(fx.C, p.W, p.A, p.Band, p.Shield, 200)).ToList();
        Assert.Equal(WoundTableBuilder.Write(cells, "X", 200), WoundTableBuilder.Write(cells, "X", 200));
    }

    [Fact]
    public void AB2_TheRows_SumToAThousand_AndHarmFallsWithProtection_AndRisesWithSkill()
    {
        var t = T;
        var melee = new[] { WeaponClass.Blade, WeaponClass.Axe, WeaponClass.Hafted, WeaponClass.Polearm, WeaponClass.Dagger, WeaponClass.Club, WeaponClass.Lance };
        for (int w = 0; w < AbstractClasses.WeaponClasses; w++)
            for (int a = 0; a < AbstractClasses.ArmourClasses; a++)
                for (int b = 0; b < AbstractClasses.Bands; b++)
                    foreach (bool s in new[] { false, true })
                    {
                        int sum = 0;
                        for (int o = 0; o < AbstractClasses.Outcomes; o++) { int sh = t.Share((WeaponClass)w, (ArmourClass)a, b, s, (OutcomeClass)o); Assert.InRange(sh, 0, 1000); sum += sh; }
                        Assert.InRange(sum, 999, 1001);
                    }
        int Out(WeaponClass w, ArmourClass a, int delta, bool s = false) => t.OutPm(w, a, delta, s);
        foreach (var w in melee)
        {
            // protection: of the blows that land, the share that wounds gravely or worse falls from none to padded to plate, for every weapon class (spec 8 AB-2). Per attack
            // action it need not: a full harness cannot dodge (-25 points) and the AI aims a point at its gaps, so a plated man is hit more often than an unarmoured one
            int Given(ArmourClass a)
            {
                int land = 1000 - t.Share(w, a, 3, false, OutcomeClass.Miss);
                int bad = t.Share(w, a, 3, false, OutcomeClass.Grave) + t.Share(w, a, 3, false, OutcomeClass.Severed) + t.Share(w, a, 3, false, OutcomeClass.Instant);
                return land <= 0 ? 0 : bad * 1000 / land;                                           // the grave-or-worse share of landed blows (every landed cut is at least serious)
            }
            Assert.True(Given(ArmourClass.Plate) < Given(ArmourClass.Padded), $"{w}: landed and harmful, plate {Given(ArmourClass.Plate)} padded {Given(ArmourClass.Padded)}");
            Assert.True(Given(ArmourClass.Padded) < Given(ArmourClass.None) + 30, $"{w}: landed and harmful, padded {Given(ArmourClass.Padded)} none {Given(ArmourClass.None)}");
            // skill: the band at +60 above the band at -60, and no band more than 3 points below the one before it (4,000 blows a cell carry about 1.5 points of noise)
            foreach (var a in new[] { ArmourClass.None, ArmourClass.Padded, ArmourClass.Mail })
            {
                Assert.True(Out(w, a, 60) > Out(w, a, -60), $"{w} vs {a}: {Out(w, a, -60)} at -60, {Out(w, a, 60)} at +60");
                for (int d = -40; d <= 60; d += 20) Assert.True(Out(w, a, d) >= Out(w, a, d - 20) - 30, $"{w} vs {a}: {Out(w, a, d - 20)} at {d - 20}, {Out(w, a, d)} at {d}");
            }
        }
        output.WriteLine(string.Join("  ", melee.Select(w => $"{w}: none {Out(w, ArmourClass.None, 0)} padded {Out(w, ArmourClass.Padded, 0)} mail {Out(w, ArmourClass.Mail, 0)} plate {Out(w, ArmourClass.Plate, 0)}")));
        // a library of real wounds behind every class that occurs
        Assert.NotEmpty(t.Templates(WeaponClass.Blade, ArmourClass.Padded, OutcomeClass.Grave));
        Assert.Contains(t.Templates(WeaponClass.Blade, ArmourClass.None, OutcomeClass.Grave), x => x.Wounds.Any(wd => wd.Has(WoundFlags.ArteryHit) || wd.Has(WoundFlags.VeinHit)));
    }

    /// <summary>The 4.4 gate on fresh seeds against the stored live fights: the comparisons that pass, and the measured gaps that are left (the spec's Notes) held where they are.</summary>
    [Fact]
    public void AB3_Equivalence_OnFreshSeeds_AgainstTheLiveFights()
    {
        Assert.Equal(TacticalReference.ContentHash, WoundTables.ContentHash(fx.Db));        // the stored live fights are of this content (else rerun --abstract reference)
        var fails = new List<string>();
        foreach (var s in Gate)
        {
            var tac = TacticalReference.Of(s);
            var abs = AbstractEquivalence.RunAbstract(fx.C, T, s, TacticalReference.Seed0, TacticalReference.Seeds);
            output.WriteLine(AbstractEquivalence.Row($"{s} tactical", tac));
            output.WriteLine(AbstractEquivalence.Row($"{s} abstract", abs));
            fails.AddRange(AbstractEquivalence.Compare(tac, abs, s.ToString()));
        }
        foreach (var f in fails) output.WriteLine("gap: " + f);
        // what the fit reaches (Notes): A within the gate but for the bandits' casualties; B's losses and length, C's line and length, D's wound mix and mud's length are the known gaps
        var a = AbstractEquivalence.RunAbstract(fx.C, T, Scenario.A, TacticalReference.Seed0, TacticalReference.Seeds);
        var ta = TacticalReference.Of(Scenario.A);
        Assert.InRange(a.WinPm, ta.WinPm - 100, ta.WinPm + 100);
        Assert.InRange(a.MeanDurationX10, ta.MeanDurationX10 * 3 / 4, ta.MeanDurationX10 * 5 / 4);
        Assert.InRange(a.Mean(a.Casualties[0]), ta.Mean(ta.Casualties[0]) * 9 / 10, ta.Mean(ta.Casualties[0]) * 11 / 10);
        foreach (var s in new[] { Scenario.C, Scenario.D }) { var x = AbstractEquivalence.RunAbstract(fx.C, T, s, TacticalReference.Seed0, TacticalReference.Seeds); Assert.InRange(x.WinPm, TacticalReference.Of(s).WinPm - 100, TacticalReference.Of(s).WinPm + 100); }
        Assert.True(fails.Count <= KnownGaps, $"{fails.Count} gaps (known {KnownGaps}):\n" + string.Join("\n", fails));
    }

    /// <summary>The number of 4.4 comparisons (of 5 scenarios x 9) the fit does not reach yet; a change that widens the gap fails AB-3.</summary>
    private const int KnownGaps = 17;

    [Fact, Trait("Category", "Soak")]
    public void AB3_Soak_TheStoredLiveFightsAreTheLiveFightsOfToday()
    {
        foreach (var s in Enum.GetValues<Scenario>())
            Assert.Equal(TacticalReference.Tallies[s], AbstractEquivalence.Pack(AbstractEquivalence.RunTactical(fx.C, s, TacticalReference.Seed0, TacticalReference.Seeds)));
    }

    [Fact]
    public void AB4_TheLeader_TheFlank_AndTheChargeOnRoadAndMud_MoveTheBattleTheLiveWay()
    {
        var withLeader = AbstractEquivalence.RunAbstract(fx.C, T, Scenario.A, 30_000, 120);
        var noLeader = AbstractEquivalence.RunAbstract(fx.C, T, Scenario.ANoLeader, 30_000, 120);
        var flanked = AbstractEquivalence.RunAbstract(fx.C, T, Scenario.AFlanked, 30_000, 120);
        var dry = AbstractEquivalence.RunAbstract(fx.C, T, Scenario.D, 30_000, 120);
        var mud = AbstractEquivalence.RunAbstract(fx.C, T, Scenario.DMud, 30_000, 120);
        output.WriteLine(AbstractEquivalence.Row("A", withLeader)); output.WriteLine(AbstractEquivalence.Row("A no leader", noLeader)); output.WriteLine(AbstractEquivalence.Row("A flanked", flanked));
        output.WriteLine(AbstractEquivalence.Row("D", dry)); output.WriteLine(AbstractEquivalence.Row("D mud", mud));
        // live: without leaders both sides break sooner (fewer casualties); the charge wins on dry ground and is lost on mud (riders win 100% dry, 25% on mud)
        var tl = TacticalReference.Of(Scenario.A); var tn = TacticalReference.Of(Scenario.ANoLeader);
        Assert.True(noLeader.Casualties.Sum() < withLeader.Casualties.Sum() && tn.Casualties.Sum() < tl.Casualties.Sum());
        Assert.True(dry.WinPm >= 950 && mud.WinPm <= 400, $"riders win {dry.WinPm} dry, {mud.WinPm} on mud");
        Assert.True(dry.Mean(dry.FirstPass[1]) > 3 * mud.Mean(mud.FirstPass[1]), "the charge's first pass is its own (mud has none)");
        Assert.True(flanked.Casualties.Sum() > withLeader.Casualties.Sum() * 9 / 10, "a flanked fight is not a gentler one");
    }

    [Fact]
    public void AB5_TheSampledOutRate_IsTheCoefficient()
    {
        long actions = 0, outs = 0, expected = 0; int steps = 0;
        for (int seed = 0; steps < 10_000; seed++)
        {
            var b = new AbstractBattle(fx.C, T, AbstractEquivalence.InputOf(Scenario.B, (ulong)(40_000 + seed)) with { Aftermath = false });
            while (!b.Over) b.StepOnce();
            steps += b.Step; actions += b.Actions; outs += b.Outs; expected += b.ExpectedOutsPm;
        }
        output.WriteLine($"{steps} steps, {actions} attack actions, {outs} out-class outcomes, beta predicts {expected / 1000.0:0.0}");
        Assert.InRange(outs * 1000, expected * 95 / 100, expected * 105 / 100);
        // the quality factor is the ratio of the coefficients: the men-at-arms are the better side
        var q = new AbstractBattle(fx.C, T, AbstractEquivalence.InputOf(Scenario.B, 1)).QualityX100();
        Assert.True(q > 100, $"quality {q}");
    }

    [Fact]
    public void AB6_MoreSoldiers_NeverLowerASidesWinRate()
    {
        // ten strengths of the militia (12..30) against the twenty bandits, 40 seeds each (the soak runs 50 x 100)
        int prev = -1;
        for (int n = 12; n <= 30; n += 2)
        {
            int wins = 0;
            for (int seed = 0; seed < 40; seed++)
            {
                var kits = Kits(Scenario.A, (ulong)(50_000 + seed), out _, out _);
                var militia = kits.Where(k => k.Team == 0).ToList(); var bandits = kits.Where(k => k.Team == 1).ToList();
                while (militia.Count < n) militia.Add(militia[militia.Count % 20] with { Name = $"Militia {militia.Count + 1}", X = militia[0].X - 1500, Y = (militia.Count - 25) * 1400 });
                var input = new BattleInput([.. militia.Take(n), .. bandits], (ulong)(50_000 + seed), (ulong)(n * 1000 + seed), 8 * 3600) { Aftermath = false };
                var b = new AbstractBattle(fx.C, T, input); b.Run();
                if (b.Winner == 0) wins++;
            }
            output.WriteLine($"{n} militia: {wins}/40");
            Assert.True(wins >= prev - 4, $"{n} militia win {wins}, {n - 2} won {prev}");          // 40 seeds: a step of up to 4 wins is noise
            prev = Math.Max(prev, wins);
        }
        Assert.True(prev >= 30, "thirty militia beat twenty bandits most of the time");
    }

    private static BattleInput Big(int perSide, ulong seed)
    {
        var kits = new List<FighterKit>();
        for (int team = 0; team < 2; team++)
            for (int i = 0; i < perSide; i++)
                kits.Add(new FighterKit
                {
                    Name = $"{team}-{i}", Team = team, Person = (ulong)(team * 100_000 + i + 1), Weapon = i % 3 == 0 ? FightCalibration.Spear : FightCalibration.Sword, Shield = i % 3 == 1 ? "heater" : null,
                    Worn = i % 4 == 0 ? FightCalibration.Mailed.Worn : FightCalibration.Padded.Worn, Skill = 30 + (i * 7 + team * 13) % 35, X = team == 0 ? -7000 - (i / 50) * 1500 : 7000 + (i / 50) * 1500,
                    Y = (i % 50 - 25) * 1200, FaceX = team == 0 ? FightMath.One : -FightMath.One,
                });
        return new BattleInput(kits, seed, seed, 8 * 3600, "test") { SideKinds = new Dictionary<int, SideKind> { [0] = SideKind.Army, [1] = SideKind.Bandits } };
    }

    [Fact]
    public void AB7_TheSameBattle_IsTheSameBattle()
    {
        var a = new AbstractBattle(fx.C, T, Big(100, 77)); var ra = a.Run();
        var b = new AbstractBattle(fx.C, T, Big(100, 77)); var rb = b.Run();
        Assert.Equal(ra.Hash, rb.Hash);
        Assert.Equal(a.Commands.Select(c => (c.Person, c.Kind, c.Status)), b.Commands.Select(c => (c.Person, c.Kind, c.Status)));
        var c = new AbstractBattle(fx.C, T, Big(100, 78)); Assert.NotEqual(ra.Hash, c.Run().Hash);
        output.WriteLine($"100 v 100 seed 77: BattleHash {ra.Hash:X16}, winner {ra.Winner}, {ra.DurationS} s");
        Assert.Equal(PinnedHash, ra.Hash);                                                                         // the same on every OS (integer only); re-pin when the battle changes on purpose
    }

    private const ulong PinnedHash = 0xFD79A2565E621106;

    [Fact]
    public void AB8_ALiveFightDemotedMidBattle_LosesNobody_AndKeepsEveryWound()
    {
        for (int seed = 0; seed < 6; seed++)
        {
            var w = Tactical(fx.C, seed % 2 == 0 ? Scenario.A : Scenario.D, (ulong)(60_000 + seed));
            for (int t = 0; t < 20 * (seed % 2 == 0 ? 7 : 12) && !w.IsOver; t++) w.StepWithAi();
            var input = BattleDemotion.InputOf(w);
            var b = new AbstractBattle(fx.C, T, input);
            var fighters = w.Fighters.Where(f => !w.IsHorse(f)).ToList();
            Assert.Equal(fighters.Count, b.Soldiers.Count);
            for (int i = 0; i < fighters.Count; i++)
            {
                Assert.Equal(fighters[i].Book.Hash(), b.Soldiers[i].Book.Hash());          // wounds, blood and pain equal at the instant of conversion
                Assert.NotSame(fighters[i].Book, b.Soldiers[i].Book);                        // a copy: nothing of the live fight is changed before the barrier
                if (!fighters[i].Active) Assert.NotEqual(SoldierStatus.Active, b.Soldiers[i].Status);
            }
            int ammo0 = b.Soldiers.Sum(s => s.Ammo);
            b.Run();
            Assert.Equal(fighters.Count, b.Soldiers.Count);
            Assert.All(b.Soldiers, s => Assert.True(Enum.IsDefined(s.Status)));
            int ends = b.Soldiers.Count(s => s.Status is SoldierStatus.Active or SoldierStatus.Down or SoldierStatus.Dead or SoldierStatus.Fled or SoldierStatus.Surrendered or SoldierStatus.Captured);
            Assert.Equal(fighters.Count, ends);
            Assert.True(b.Soldiers.Sum(s => s.Ammo) <= ammo0);                              // arrows are spent, never made
            // and back: the standing soldiers promote to a live setup with the same people
            var up = BattleDemotion.Promote(b);
            Assert.Equal(b.Soldiers.Count(s => s.Active), up.Kits.Count);
        }
    }

    [Fact]
    public void AB9_RansomAndLoot_MoveThroughTheLedger_AndARansomOnlyFromThePayersTreasury()
    {
        var led = new Ledger();
        var treasury = led.Open(PurseKind.Treasury, EntityId.Make(IdKind.Other, 1));
        var captor = led.Open(PurseKind.Hoard, EntityId.Make(IdKind.Other, 2));
        led.Mint(treasury, new Coin(3_000), SourceReason.Seed);
        long before = led.Audit().InPurses.Mites;
        Assert.Equal(2_000, BattleRansom.Pay(led, treasury, captor, 2_000));
        Assert.Equal(1_000, BattleRansom.Pay(led, treasury, captor, 2_000));                              // the rest of the treasury, never more
        Assert.Equal(0, BattleRansom.Pay(led, treasury, captor, 2_000));                                  // an empty treasury pays nothing: no ransom from nowhere
        Assert.Equal(before, led.Audit().InPurses.Mites);
        Assert.True(led.Audit().IsConserved);
        Assert.Equal(0, led.Balance(treasury).Mites); Assert.Equal(3_000, led.Balance(captor).Mites);
    }

    [Fact]
    public void AB10_TheCommanderCanDie_OrBeTaken()
    {
        int lost = 0, fates = 0, deadLeaders = 0; var byFate = new SortedDictionary<BattleFate, int>();
        for (int seed = 0; seed < 500; seed++)
        {
            var input = AbstractEquivalence.InputOf(seed % 2 == 0 ? Scenario.A : Scenario.B, (ulong)(70_000 + seed)) with
            {
                SideKinds = new Dictionary<int, SideKind> { [0] = SideKind.Army, [1] = seed % 4 < 2 ? SideKind.Army : SideKind.Bandits },
            };
            var b = new AbstractBattle(fx.C, T, input); var r = b.Run();
            if (r.Winner < 0) continue;
            var leader = r.Notables.FirstOrDefault(n => n.Leader && n.Side == 1 - r.Winner);
            if (leader == null) continue;
            lost++;
            if (leader.Fate is BattleFate.Dead or BattleFate.Captured) fates++;
            if (leader.Fate == BattleFate.Dead) deadLeaders++;
            byFate[leader.Fate] = byFate.GetValueOrDefault(leader.Fate) + 1;
        }
        output.WriteLine($"losing leaders: {lost}, dead or taken {fates} ({fates * 100 / Math.Max(1, lost)}%), dead {deadLeaders}; " + string.Join(", ", byFate.Select(kv => $"{kv.Key} {kv.Value}")));
        Assert.InRange(fates * 1000 / Math.Max(1, lost), 150, 350);
        Assert.True(deadLeaders > 0, "and some of them die: no immortal commander");
    }

    [Fact]
    public void AB11_BodiesPersist_TheWoundedBleedOn_AndAMedicSavesSome()
    {
        int died = 0, diedAided = 0, wounded = 0;
        for (int seed = 0; seed < 60; seed++)
        {
            var input = AbstractEquivalence.InputOf(Scenario.B, (ulong)(80_000 + seed)) with { SideKinds = new Dictionary<int, SideKind> { [0] = SideKind.Army, [1] = SideKind.Army } };
            var plain = new AbstractBattle(fx.C, T, input with { FirstAid = false }); plain.Run();
            var aided = new AbstractBattle(fx.C, T, input with { FirstAid = true }); aided.Run();
            died += plain.Report.Sides.Sum(s => s.DiedAfter); diedAided += aided.Report.Sides.Sum(s => s.DiedAfter);
            wounded += aided.Soldiers.Count(s => s.Status != SoldierStatus.Dead && s.Book.Records.Count > 0);
            Assert.All(plain.Soldiers.Where(s => s.Status == SoldierStatus.Down), s => Assert.NotEmpty(s.Book.Records));
        }
        output.WriteLine($"bled to death after the fight: {died} untreated, {diedAided} with first aid; wounded survivors {wounded}");
        Assert.True(wounded > 100 && died > 0 && diedAided < died);
        // the untreated bleed-out share against the live one (4.4: within 10 points of the stored fights)
        foreach (var s in new[] { Scenario.A, Scenario.B })
        {
            var abs = AbstractEquivalence.RunAbstract(fx.C, T, s, TacticalReference.Seed0, TacticalReference.Seeds); var tac = TacticalReference.Of(s);
            output.WriteLine($"{s}: bled out {abs.BleedOutPm} pm abstract, {tac.BleedOutPm} pm live");
            Assert.InRange(abs.BleedOutPm, tac.BleedOutPm - 120, tac.BleedOutPm + 120);
        }
    }

    [Fact]
    public void AB12_EveryReport_HasACause_AndEveryNamedCasualtyAChain()
    {
        var ob = new RegionOutbox(new IdAllocator());
        int reports = 0, chains = 0, named = 0;
        for (int seed = 0; seed < 30; seed++)
        {
            var kits = Kits(Scenario.A, (ulong)(90_000 + seed), out _, out _).Select((k, i) => k with { Person = k.Team == 0 ? (ulong)(1000 + i) : 0 }).ToList();
            var setup = new FightSetup(FightKind.Raid, kits, (ulong)seed, (ulong)(9000 + seed), 8 * 3600, "raid") { SideKinds = new Dictionary<int, SideKind> { [1] = SideKind.Bandits } };
            var rep = new AbstractBattles { Place = "the Oakford road" }.Resolve(setup, fx.C);
            Assert.False(rep.Stub);
            var br = rep.Battle!;
            var id = BattleChronicle.Report(ob, new GameTime(8 * 3600), br, EntityId.None, "Oakford", "the militia", "bandits", new Cause(CauseKind.Raid, null, "bandits raided the Oakford farms"));
            reports++;
            foreach (var n in br.Notables.Where(n => n.Person != 0 && n.Fate == BattleFate.Dead))
            {
                named++;
                var chain = BattleChronicle.DeathChain(n, br, "bandits raided the Oakford farms");
                if (chain.Contains("at the fight on the Oakford road") && chain.Contains("which began when bandits raided")) chains++;
            }
        }
        Assert.Equal(reports, ob.Entries.Count(e => e.TypeId == BattleChronicle.TypeId));
        Assert.All(ob.Entries, e => { Assert.NotEmpty(e.Causes); Assert.Equal("1", e.Data["report"]); Assert.StartsWith("Word reached Oakford", e.Data["text"]); Assert.Equal(BattleChronicle.Key, e.Data["key"]); });
        Assert.True(named > 0);
        Assert.Equal(named, chains);                                                                 // coverage 100%
    }

    [Fact]
    public void AB12_ACaravanAmbushedAwayFromThePlayer_IsFoughtOut_AndToldAsAReport()
    {
        var hw = HeadlessWorld.Create(1234);
        var ew = hw.Economy; var st = ew.Trade.Link.State; st.DangerBonus = 100;
        hw.World.Set(new AbstractBattleService(hw.Content));
        hw.World.Set(new WoundLedger(hw.World.Seed));
        long coin0 = ew.World.Economy.Ledger.Audit().InPurses.Mites + st.TotalCoin();
        using var t = hw.NewTicker();
        t.AdvanceDays(30);
        var reports = hw.World.Chronicle.All.Where(e => e.TypeId == BattleChronicle.TypeId).ToList();
        output.WriteLine($"ambushes {ew.Trade.Ambushes}, reports {reports.Count}, lost {ew.Trade.CaravansLost}; same world {ReferenceEquals(hw.World, ew.World)}; "
            + string.Join(", ", hw.World.Chronicle.All.GroupBy(e => e.TypeId).Where(g => g.Key.Contains("caravan") || g.Key.Contains("battle") || g.Key.Contains("death")).Select(g => $"{g.Key} {g.Count()}")));
        Assert.True(ew.Trade.Ambushes > 0 && reports.Count > 0);                                     // a caravan with a crew fights; one with none is simply robbed (no fight to report)
        Assert.True(reports.Count <= ew.Trade.Ambushes);
        Assert.All(reports, e => { Assert.NotEmpty(e.Causes); Assert.StartsWith("Word reached ", e.Data["text"]); });
        var audit = ew.World.Economy.Ledger.Audit();
        Assert.True(audit.IsConserved);
        Assert.Equal(coin0, audit.InPurses.Mites + st.TotalCoin());
        Assert.True(ew.World.Economy.MassAuditHolds());
    }

    [Fact]
    public void AB13_ABattleIsMilliseconds()
    {
        foreach (var (n, budgetMs) in new[] { (100, 3), (200, 8) })
        {
            var warm = Stopwatch.StartNew();
            for (int i = 0; i < 6 || warm.ElapsedMilliseconds < 1500; i++) new AbstractBattle(fx.C, T, Big(n, (ulong)(100 + i % 8))).Run();      // warm: the tiered JIT promotes the loops after a while
            double ms = double.MaxValue;
            for (int runs = 0; runs < 5; runs++) { var sw = Stopwatch.StartNew(); new AbstractBattle(fx.C, T, Big(n, (ulong)(2 + runs))).Run(); ms = Math.Min(ms, sw.Elapsed.TotalMilliseconds); }
            output.WriteLine($"{n} v {n}: best of 5 {ms:0.0} ms (budget {budgetMs})");
            Assert.True(ms < budgetMs * 8, $"{n} v {n} took {ms:0.0} ms");                       // a loaded machine gets eight times the budget; the soak below holds the budget
        }
    }

    [Fact, Trait("Category", "Soak")]
    public void AB13_Soak_TheBudgets()
    {
        var over = new List<string>();
        // the caravan ambushes' sizes (P4 close-out). The 2000 v 2000 budget (45 ms) comes back as a Phase 7 test with the battle bubble (army-scale battles moved to Phase 7, decided with the user)
        foreach (var (n, budgetMs) in new[] { (100, 3), (200, 8) })
        {
            var warm = Stopwatch.StartNew();
            for (int i = 0; i < 2 || warm.ElapsedMilliseconds < 1500; i++) new AbstractBattle(fx.C, T, Big(n, (ulong)(100 + i % 8))).Run();
            double ms = double.MaxValue;
            for (int i = 0; i < 3; i++) { var sw = Stopwatch.StartNew(); new AbstractBattle(fx.C, T, Big(n, (ulong)(2 + i))).Run(); ms = Math.Min(ms, sw.Elapsed.TotalMilliseconds); }
            output.WriteLine($"{n} v {n}: {ms:0.0} ms (budget {budgetMs}, here {TestBudget.Ms(budgetMs):0})");
            if (ms > TestBudget.Ms(budgetMs)) over.Add($"{n} v {n} took {ms:0.0} ms (budget {budgetMs}, here {TestBudget.Ms(budgetMs):0})");
        }
        Assert.True(over.Count == 0, string.Join("; ", over));
    }

    [Fact]
    public void AB14_MudKillsTheCharge_AndRainDullsTheBows()
    {
        var dry = AbstractEquivalence.RunAbstract(fx.C, T, Scenario.D, 95_000, 80);
        var mud = AbstractEquivalence.RunAbstract(fx.C, T, Scenario.DMud, 95_000, 80);
        Assert.True(dry.Mean(dry.FirstPass[1]) > 3 * mud.Mean(mud.FirstPass[1]));
        long hits = 0, hitsRain = 0, shots = 0, shotsRain = 0;
        for (int seed = 0; seed < 80; seed++)
        {
            var input = AbstractEquivalence.InputOf(Scenario.C, (ulong)(96_000 + seed)) with { Aftermath = false };
            foreach (bool rain in new[] { false, true })
            {
                var b = new AbstractBattle(fx.C, T, rain ? input with { Weather = "rain" } : input);
                long h = 0, n = 0;
                b.OnShot = (o) => { n++; if (o >= OutcomeClass.Nothing) h++; };
                while (!b.Over) b.StepOnce();
                if (rain) { hitsRain += h; shotsRain += n; } else { hits += h; shots += n; }
            }
        }
        double pm = 1000.0 * hits / shots, pmRain = 1000.0 * hitsRain / shotsRain;
        output.WriteLine($"arrows landing: {pm:0} pm dry, {pmRain:0} pm in rain ({100 * (1 - pmRain / pm):0}% fewer)");
        Assert.InRange(pmRain / pm, 0.80, 0.90);
        // night: fewer blows; narrow ground: a smaller frontage; both lower the casualties of a line fight
        var day = AbstractEquivalence.RunAbstract(fx.C, T, Scenario.A, 97_000, 60);
        long nightCas = 0;
        for (int seed = 0; seed < 60; seed++) { var b = new AbstractBattle(fx.C, T, AbstractEquivalence.InputOf(Scenario.A, (ulong)(97_000 + seed)) with { Night = true, Aftermath = false }); b.Run(); nightCas += b.Report.Sides.Sum(s => s.Casualties); }
        Assert.True(nightCas < day.Casualties.Sum(), $"night {nightCas}, day {day.Casualties.Sum()}");
    }

    [Fact]
    public void MT12_TheAbstractChargeBonus_AgreesWithTheLiveOne()
    {
        // mounted-combat MT-12: the live first pass (downed or killed by blows in the 10 s from the first wound) charging 1.27, on mud 0.39 (+224%); the abstract's own on fresh seeds
        var dry = AbstractEquivalence.RunAbstract(fx.C, T, Scenario.D, TacticalReference.Seed0, TacticalReference.Seeds);
        var mud = AbstractEquivalence.RunAbstract(fx.C, T, Scenario.DMud, TacticalReference.Seed0, TacticalReference.Seeds);
        var td = TacticalReference.Of(Scenario.D);
        double a = dry.Mean(dry.FirstPass[1]) / 1000.0, t = td.Mean(td.FirstPass[1]) / 1000.0;
        output.WriteLine($"first pass: abstract {a:0.00} dry / {mud.Mean(mud.FirstPass[1]) / 1000.0:0.00} mud; live {t:0.00} / {TacticalReference.Of(Scenario.DMud).Mean(TacticalReference.Of(Scenario.DMud).FirstPass[1]) / 1000.0:0.00}");
        Assert.InRange(a, t * 0.85, t * 1.15);                                                      // abstract and tactical within 15% (spec 8)
    }
}
