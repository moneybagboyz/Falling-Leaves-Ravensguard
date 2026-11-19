using Ravensguard.Content;
using Ravensguard.Sim.Behaviour;
using Ravensguard.Sim.Combat;
using Ravensguard.Sim.Combat.Melee2;
using Ravensguard.Sim.Combat.Resolver2;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Diagnostics;
using Ravensguard.Sim.Lighting;
using Ravensguard.Sim.Live;
using Ravensguard.Sim.People;
using Ravensguard.Sim.Save;
using Ravensguard.Sim.World;

namespace Ravensguard.Sim.Tests.Combat.Melee2;

/// <summary>melee-and-ranged 8 ML-14 to ML-16, the done-when checks (a seeded fight is deterministic, near equals far, a called shot through a gap), the fight save, replay,
/// the weapon-family skills and their migration, the encounter fight and the combat keys.</summary>
[Collection("fight")]
public class FightSystemTests(FightFixture fx)
{
    // ---------------------------------------------------------------- ML-14 calibration (resolver-v2 8)

    private static void AssertTargets(IReadOnlyList<FightCalibration.Row> rows)
    {
        var by = rows.ToDictionary(r => r.Name);
        foreach (var r in rows)
        {
            // close-out (docs/reviews/phase-4.md): full plate against full plate runs long (a failed point now skids off plate instead of crushing it, item 2), and the spear at plan
            // timings ends its duels at about 10 s (the 2,000-duel table: 10.3 s); the sample's 160 duels may read a few tenths under
            Assert.True(r.MeanS is >= 10 and <= 20 || r.Name == "sword vs sword (plate)" && r.MeanS <= 25 || r.Name.StartsWith("spear", StringComparison.Ordinal) && r.MeanS >= 9.5, $"{r.Name}: {r.MeanS:0.0} s");
            Assert.True(r.DefendedPct is >= 37 and <= 62, $"{r.Name}: defended {r.DefendedPct:0}%");
            Assert.True(r.SeversPerDuel <= 0.12, $"{r.Name}: {r.SeversPerDuel} severs per duel");
        }
        Assert.InRange(by["sword vs sword (padded)"].WinAPct, 42, 58);
        Assert.True(by["spear vs sword (unarmoured)"].WinAPct >= 55, $"spear {by["spear vs sword (unarmoured)"].WinAPct:0}%");     // resolver-v2 8: a floor, not a band (close-out item 1)
        Assert.True(by["mace vs sword (plate)"].WinAPct > 50, $"mace vs plate {by["mace vs sword (plate)"].WinAPct:0}%");
        Assert.True(by["mace (padded) vs sword (plate)"].WinAPct >= by["sword (padded) vs sword (plate)"].WinAPct - 2, "the mace does at least as well as a sword against plate");
        long ko = rows.Sum(r => r.KoTreatedN), bled = rows.Sum(r => r.KoBledOutTreated);
        Assert.True(bled * 100 <= ko * 35, $"bleed-out with first aid {100.0 * bled / Math.Max(1, ko):0}%");
    }

    [Fact]
    public void ML14_CalibrationTargets_Sample()
    {
        var rows = FightCalibration.MainPairings().Select(p => FightCalibration.Run(fx.C, p, 160)).ToList();
        AssertTargets(rows);
    }

    [Fact, Trait("Category", "Soak")]
    public void ML14_CalibrationTargets_2000DuelsPerPairing()
    {
        var rows = FightCalibration.MainPairings().Select(p => FightCalibration.Run(fx.C, p, 2000)).ToList();
        AssertTargets(rows);
    }

    // ---------------------------------------------------------------- ML-15 determinism

    [Fact]
    public void ML15_SeededSkirmishIsDeterministic_AndTheHeroIsJustAnotherFighter()
    {
        ulong Run(ulong seed)
        {
            var w = FightCalibration.Skirmish(fx.C, seed, 10);
            for (int i = 0; i < 600; i++) w.StepWithAi();
            return w.Hash();
        }
        Assert.Equal(Run(11), Run(11));
        Assert.NotEqual(Run(11), Run(12));
        // a fighter marked as the player but driven by the brain (watch mode's contract) fights exactly as the same NPC (WM-1's precondition)
        var a = FightCalibration.Skirmish(fx.C, 13, 10);
        var b = FightCalibration.Skirmish(fx.C, 13, 10);
        b.LodCentre = (b.Fighters[4].X, b.Fighters[4].Y); a.LodCentre = b.LodCentre;
        for (int i = 0; i < 600; i++)
        {
            a.StepWithAi();
            foreach (var f in b.Fighters) CombatBrain.Think(b, f);          // the hero (fighter 4) thought for by the same brain
            b.Step();
        }
        Assert.Equal(a.Hash(), b.Hash());
    }

    // ---------------------------------------------------------------- ML-16 near equals far

    [Fact]
    public void ML16_NearAndFarAI_GiveTheSameOutcomesWithin5Percent()
    {
        (double Down, double Dead, Dictionary<string, double> Wounds) Run(bool near)
        {
            int downs = 0, deaths = 0, n = 0;
            var wounds = new Dictionary<string, long>();
            var mats = fx.C.T.MaterialList;
            for (ulong s = 0; s < 200; s++)
            {
                var w = FightCalibration.Skirmish(fx.C, 9000 + s, 5, near);
                w.OnBlow = (_, t, r, _) => { foreach (var wd in r.WoundSpan) { var k = Calibration.Category(t.Plan, mats, wd); wounds[k] = wounds.GetValueOrDefault(k) + 1; } };
                for (int i = 0; i < 1200 && !w.IsOver; i++) w.StepWithAi();
                foreach (var f in w.Fighters) { n++; if (f.Act == FAct.Down) downs++; if (f.Act == FAct.Dead) deaths++; }
            }
            long tot = Math.Max(1, wounds.Values.Sum());
            return (100.0 * downs / n, 100.0 * deaths / n, wounds.ToDictionary(kv => kv.Key, kv => 100.0 * kv.Value / tot));
        }
        var near = Run(true); var far = Run(false);
        Assert.InRange(near.Down - far.Down, -5, 5);
        Assert.InRange(near.Dead - far.Dead, -5, 5);
        foreach (var k in near.Wounds.Keys.Union(far.Wounds.Keys))
            Assert.InRange(near.Wounds.GetValueOrDefault(k) - far.Wounds.GetValueOrDefault(k), -5, 5);
    }

    // ---------------------------------------------------------------- done-when: a called shot through an armour gap

    [Fact]
    public void CalledShotThroughAnArmourGap_BypassesTheMail()
    {
        // the hero presses T, picks the armpit on a mail wearer and strikes (overview OV-2): every blow that lands there goes past the hauberk
        int bypassed = 0, landed = 0;
        for (ulong seed = 1; seed <= 200 && landed < 12; seed++)
        {
            var w = fx.World(seed);
            var hero = w.Add(fx.Kit("You", 0, 0, skill: 60), true);
            var foe = w.Add(fx.Kit("Mail", 1, 1500, skill: 20, worn: [new("tunic", "linen"), new("gambeson", "linen"), new("mail_hauberk", "iron")]), true);
            int part = foe.Plan.IndexOf("upper_body"), gap = foe.Plan.GapIndex("armpit");
            Assert.True(gap >= 0);
            var stab = hero.Weapon.AttackIndex("stab");
            w.OnBlow = (_, _, r, _) =>
            {
                if (!r.Hit || r.Blocked || r.Redirected) return;              // a missed called shot that lands on a neighbour is not at the gap
                landed++;
                if (r.StepSpan.ToArray().Any(st => st.IsArmour && st.Outcome == LayerOutcome.Bypassed)) bypassed++;
            };
            w.Input(hero.Id, new FightInput(FightInputKind.AttackDown, stab, part, gap));
            for (int i = 0; i < 12; i++) { w.Step(); if (i == 7) w.Input(hero.Id, new FightInput(FightInputKind.AttackUp)); }
        }
        Assert.True(landed >= 5, $"landed {landed}");
        Assert.Equal(landed, bypassed);
    }

    // ---------------------------------------------------------------- the fight save (overview 8.3, OV-3)

    [Fact]
    public void SaveLoadSave_MidFight_IsByteIdentical_AndResumesExactly()
    {
        var w = FightCalibration.Skirmish(fx.C, 21, 6);
        w.Fighters[2].Player = true;
        for (int i = 0; i < 160; i++) { if (i == 40) w.Input(2, new FightInput(FightInputKind.Move, 0, 1)); w.StepWithAi(); }
        Assert.Contains(w.Fighters, f => f.Book.Records.Count > 0);
        var bytes = FightRecord.Capture(w, "test").ToBytes();
        var loaded = FightRecord.FromBytes(bytes).Restore(fx.C);
        Assert.Equal(bytes, FightRecord.Capture(loaded, "test").ToBytes());
        Assert.Equal(w.Hash(), loaded.Hash());
        // the next 600 ticks are the same fight
        for (int i = 0; i < 600; i++) { w.StepWithAi(); loaded.StepWithAi(); }
        Assert.Equal(w.Hash(), loaded.Hash());
        // and the world save carries it in the "fight" section
        var file = new SaveFile(new SaveHeader(), [new(FightRecord.SectionName, bytes)]);
        Assert.True(file.Sections.ContainsKey("fight"));
    }

    // ---------------------------------------------------------------- replay

    [Fact]
    public void AReplayOfAFight_ReproducesTheHashes()
    {
        static LiveWorld Field()
        {
            var g = new LevelGrid(40, 20, 0, 1);
            g.FillRect(0, 0, 39, 19, 0, true);
            return new LiveWorld(g, new LightGrid(g), new Cell3(5, 5, 0), GameTime.FromCalendar(1, Season.Spring, 8, 12));
        }
        var live = Field();
        var rec = new SessionRecorder(live);
        var start = FightCalibration.Skirmish(fx.C, 31, 3);
        start.Fighters[0].Player = true;
        rec.StartFight(start, 0);
        for (int t = 0; t < 900; t++)
        {
            if (t % 37 == 5) rec.FightInput(new FightInput(FightInputKind.AttackDown, -1, -1, -1));
            if (t % 37 == 14) rec.FightInput(new FightInput(FightInputKind.AttackUp));
            if (t % 53 == 20) rec.FightInput(new FightInput(FightInputKind.BlockDown));
            if (t % 53 == 30) rec.FightInput(new FightInput(FightInputKind.BlockUp));
            if (t % 90 == 0) rec.FightInput(new FightInput(FightInputKind.Move, (t / 90) % 64, 0));
            rec.Step();
        }
        var (trace, csv) = rec.Export();
        Assert.Contains(rec.Commands.Items, c => c.Kind == ReplayKind.Fight);
        // a fresh world, the same start, the recorded inputs: the same hashes
        var again = Field();
        again.FightFactory = id => FightRecord.FromBytes(rec.FightStarts[id]).Restore(fx.C);
        var res = ReplayRunner.Run(again, CommandLog.FromBytes(trace), StateHashTrail.FromCsv(csv));
        Assert.True(res.Matched, res.Describe());
        Assert.NotNull(again.Fight);
        Assert.Equal(live.Fight!.Hash(), again.Fight!.Hash());
    }

    // ---------------------------------------------------------------- skills, actions, the encounter

    [Fact]
    public void WeaponFamilies_AreSixSkills_ReadAs80PercentOfMelee_AndMigrateFromV5()
    {
        Assert.Equal([Skill.Blades, Skill.Axes, Skill.Hafted, Skill.Polearms, Skill.Shields, Skill.Throwing], CombatSkills.Families);
        Assert.Equal(40, CombatSkills.Level(s => s == Skill.Melee ? 50 : 0, Skill.Blades));
        Assert.Equal(70, CombatSkills.Level(s => s == Skill.Melee ? 50 : s == Skill.Blades ? 70 : 0, Skill.Blades));
        Assert.Equal(Skill.Polearms, CombatSkills.Of("polearms"));
        // a v5 hero (no family ids in the save) migrates to Melee x0.8
        var hero = new Ravensguard.Sim.Characters.HeroSave
        {
            Name = "H", Origin = "x", Attributes = [50, 50, 50, 50, 50], SkillIds = ["melee", "archery"], SkillLevels = [45, 20], ItemShapes = [], ItemMaterials = [], ItemCounts = [],
        };
        var file = new SaveFile(new SaveHeader { FormatVersion = 5 }, [new(WorldSave.HeroSection, SaveCodec.Serialize(hero))]);
        var migrated = SaveMigrator.CreateDefault().Migrate(file);
        Assert.Equal(SaveFormat.CurrentVersion, migrated.Header.FormatVersion);   // v7 (P4-D) steps on from v6
        var h = Ravensguard.Sim.Characters.HeroRecord.FromSave(SaveCodec.Deserialize<Ravensguard.Sim.Characters.HeroSave>(migrated.Sections[WorldSave.HeroSection].ToArray()));
        foreach (var f in CombatSkills.Families) Assert.Equal(36, h.Skill(Ravensguard.Sim.Characters.HeroRecord.SkillId(f)));
        Assert.Equal(45, h.Skill("melee"));
        // the two Phase 4 actions are appended and the policy grew to hold them
        Assert.Equal(15, (int)ActionKind.Treat); Assert.Equal(16, (int)ActionKind.Fight);
        Assert.Equal(17, ActionPolicy.Kinds);
    }

    [Fact]
    public void ARoadEncounterFight_IsARealFight()
    {
        var def = fx.Db.Get<EncounterDef>("bandit_ambush_small");
        var ev = new Ravensguard.Sim.Travel.EncounterEvent(500_000, def.Id, def.Kind, 0, 0, 0, 0, 1, [101, 102, 103], def.Bark);
        var hero = fx.Kit("You", 0, 0, skill: 55, shield: "heater", worn: [new("tunic", "linen"), new("gambeson", "linen"), new("mail_hauberk", "iron")]) with { Person = 7 };
        var setup = FightSetup.Encounter(fx.C, def, ev, hero, [], 99);
        Assert.Equal(4, setup.Kits.Count);
        Assert.Equal(0, setup.PlayerIndex);
        var w = setup.Start(fx.C, new Ravensguard.Sim.Combat.Wounds.WoundLedger(99, ev.Sec));
        w.Fighters[0].Player = false;                                               // the hero handed to the AI for the test
        for (int i = 0; i < 20 * 180 && !w.IsOver; i++) w.StepWithAi();
        Assert.True(w.IsOver, "the fight ends");
        Assert.True(w.Ledger!.Books.Count > 0, "the wounds went into the world's ledger");
        Assert.Equal(setup.Kits.Count, w.Fighters.Count);
        Assert.All(w.Fighters.Skip(1), f => Assert.Equal(1, f.Team));
        // the same encounter is the same fight
        var w2 = FightSetup.Encounter(fx.C, def, ev, hero, [], 99).Start(fx.C, new Ravensguard.Sim.Combat.Wounds.WoundLedger(99, ev.Sec));
        w2.Fighters[0].Player = false;
        for (int i = 0; i < w.Tick; i++) w2.StepWithAi();
        Assert.Equal(w.Hash(), w2.Hash());
    }

    [Fact]
    public void CombatKeys_AreBoundOnceAndDoNotClash()
    {
        // the Phase 4 keys (overview 6A): F6, Shift+F6, F7, Shift+F7 in World; 5 reload and 6 attack type in CombatStance
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../.."));
        var lines = File.ReadAllLines(Path.Combine(root, "src", "Ravensguard.Game", "Input", "InputDefs.cs"));
        string Line(string ctx, string name) => lines.Single(l => l.Contains($"A({ctx}, \"{name}\""));
        Assert.Contains("Key.F6)", Line("W", "watch_toggle"));
        Assert.Contains("Key.F6, Mods.Shift", Line("W", "stance_next"));
        Assert.Contains("Key.F7)", Line("W", "combat_log"));
        Assert.Contains("Key.F7, Mods.Shift", Line("W", "last_report"));
        Assert.Contains("Key.Key5", Line("C", "reload"));
        Assert.Contains("Key.Key6", Line("C", "attack_next"));
        // inside CombatStance no key is bound twice
        var combat = lines.Where(l => l.TrimStart().StartsWith("A(C, ", StringComparison.Ordinal)).Select(l => l[(l.IndexOf(", K(", StringComparison.Ordinal) is int i and >= 0 ? i : l.IndexOf(", M(", StringComparison.Ordinal))..]).ToList();
        Assert.Equal(combat.Count, combat.Distinct().Count());
    }
}
