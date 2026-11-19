using Ravensguard.Content;
using Ravensguard.Sim.Combat;
using Ravensguard.Sim.Combat.Melee2;
using Ravensguard.Sim.Combat.Morale;
using Ravensguard.Sim.Combat.Resolver2;
using Ravensguard.Sim.Combat.Watch;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Save;
using Ravensguard.Sim.Tests.Combat.Melee2;
using Ravensguard.Sim.Travel;

namespace Ravensguard.Sim.Tests.Combat.Watch;

/// <summary>The fight setup as the one entry point for every fight (overview 3, P4-F): the kinds, the practice yard and tournaments on the real fight, raids, ambushes and sites, the abstract seam, the odds on the encounter panel.</summary>
[Collection("fight")]
public class FightSetupTests(FightFixture fx)
{
    private static FighterKit Person(string name, int skill = 40, WeaponInstance? w = null, string? shield = null, IReadOnlyList<WornItem>? worn = null) =>
        new() { Name = name, Weapon = w ?? FightCalibration.Sword, Skill = skill, Shield = shield, Worn = worn ?? [new WornItem("tunic", "linen")] };

    [Fact]
    public void ThePracticeYard_IsARealFightWithBluntedPracticeWeapons_ToThreeCleanHits()
    {
        int heroWins = 0, bouts = 0;
        for (int i = 0; i < 30; i++)
        {
            var hero = Person("Hero", 30 + i, shield: "heater", worn: FightCalibration.Mailed.Worn) with { Person = 7 };
            var trainer = Person("Sergeant", 45, new WeaponInstance("spear", "iron")) with { Person = 8 };
            var setup = FightSetup.Practice(fx.C, hero, trainer, 11, 8 * 3600 + i * 600, 3);
            Assert.Equal(FightKind.Practice, setup.Kind); Assert.Equal("practice", setup.KindName);
            Assert.All(setup.Kits, k => Assert.True(k.Blunted)); Assert.All(setup.Kits, k => Assert.StartsWith("practice_", k.Weapon!.ShapeId));
            Assert.Equal("practice_spear", setup.Kits[1].Weapon!.ShapeId); Assert.Null(setup.Kits[0].Shield);
            var w = setup.Start(fx.C, null);
            Assert.Equal(3, w.Rules.CleanHitsToWin);
            var edged = 0;
            w.OnBlow = (_, _, r, s) => { if (r.Hit && s.Weapon.Attacks[s.Attack].Type != AttackType.Blunt) edged++; };
            w.Fighters[0].Player = false;                                       // the hero's bout fought by the brain for the test
            for (int t = 0; t < 20 * 180 && !w.IsOver; t++) w.StepWithAi();
            Assert.True(w.IsOver, $"bout {i} ended");
            Assert.Equal(0, edged);
            Assert.DoesNotContain(w.Fighters, f => f.Act == FAct.Dead);
            int winner = w.HitsWinner();
            if (winner >= 0) { bouts++; Assert.Equal(3, w.Fighters[winner].Landed); if (w.Fighters[winner].Team == 0) heroWins++; Assert.Equal(w.Fighters[winner].Team, w.WinnerTeam); }
        }
        Assert.True(bouts >= 20, $"{bouts} bouts went to three hits");
        Assert.InRange(heroWins, 1, bouts - 1);
        // the record names the kind, so a saved bout keeps its rules
        var bout = FightSetup.Practice(fx.C, Person("Hero"), Person("Sergeant"), 1, 0, 1).Start(fx.C, null);
        var back = FightRecord.FromBytes(FightRecord.Capture(bout, "practice").ToBytes()).Restore(fx.C);
        Assert.Equal(3, back.Rules.CleanHitsToWin);
    }

    [Fact]
    public void BluntedWeapons_HalveTheMomentum_AndEdgesCountAsBlunt()
    {
        var plan = fx.C.T.Plan("human");
        var sword = fx.C.T.Weapon("arming_sword", "steel");
        var s = StrikeV2.Create(sword, 0, plan, plan.IndexOf("upper_arm_l"), 50);
        var b = s; b.MomentumPm = 500; b.Blunted = true;
        var r1 = new BlowResult(); var r2 = new BlowResult();
        new ResolverV2(fx.C.T).ResolveImpact(s, new BlowKey(1, 1, 1, 1), r1);
        new ResolverV2(fx.C.T).ResolveImpact(b, new BlowKey(1, 1, 1, 1), r2);
        Assert.InRange(r2.InitialMilli, r1.InitialMilli / 2 - 2, r1.InitialMilli / 2 + 2);
        Assert.Contains(Enumerable.Range(0, r1.WoundCount), i => r1.Wounds[i].Kind == WoundKind.Cut);
        Assert.DoesNotContain(Enumerable.Range(0, r2.WoundCount), i => r2.Wounds[i].Kind == WoundKind.Cut);
    }

    [Fact]
    public void ATournamentBout_YieldsAtThePainKnockOut_AndThePurseComesFromTheTownTreasury()
    {
        int yielded = 0, deaths = 0;
        for (int i = 0; i < 30; i++)
        {
            var hero = Person("Hero", 50, shield: "heater", worn: FightCalibration.Padded.Worn) with { Person = 7 };
            var entrant = Person("Sir Odo", 50, new WeaponInstance("longsword", "steel"), worn: FightCalibration.Padded.Worn) with { Person = 9 };
            var setup = FightSetup.Tournament(fx.C, hero, entrant, 21, 9 * 3600 + i * 900, 4, i);
            Assert.Equal(FightKind.Tournament, setup.Kind); Assert.All(setup.Kits, k => Assert.True(k.Blunted));
            var w = setup.Start(fx.C, null);
            w.Fighters[0].Player = false;
            for (int t = 0; t < 20 * 240 && !w.IsOver; t++) w.StepWithAi();
            Assert.True(w.IsOver);
            yielded += w.Fighters.Count(f => f.Act == FAct.Surrendered);
            deaths += w.Fighters.Count(f => f.Act == FAct.Dead);
            Assert.DoesNotContain(w.Fighters, f => f.Act == FAct.Down && f.Book.PainX100 >= f.Book.KnockoutPainX100 && f.Book.LossPm < 300);   // a pain knock-out yields instead
        }
        Assert.True(yielded >= 15, $"{yielded} bouts ended in a yield");
        Assert.Equal(0, deaths);
        // the purse: the town treasury pays the winner through the ledger, never more than it holds; coin is conserved
        var l = new Ledger();
        var town = l.Open(PurseKind.Treasury, EntityId.Make(IdKind.Person, 900)); var winner = l.Open(PurseKind.Person, new EntityId(7));
        l.Mint(town, Coin.FromMites(5000), SourceReason.Seed);
        Assert.Equal(1200, TournamentPurse.Pay(l, town, winner, 1200));
        Assert.Equal(3800, l.Balance(town).Mites); Assert.Equal(1200, l.Balance(winner).Mites);
        Assert.Equal(3800, TournamentPurse.Pay(l, town, winner, 9999));     // what is left, not a mite more
        Assert.Equal(0, TournamentPurse.Pay(l, town, winner, 10));
        Assert.True(l.Audit().IsConserved);
        Assert.Equal(5000, l.Balance(winner).Mites);
    }

    [Fact]
    public void RaidsAmbushesAndSites_AreBuiltByTheSameSetup()
    {
        var defenders = new[] { Person("Farmer"), Person("Son") };
        var raiders = new[] { Person("Raider 1", 45, new WeaponInstance("axe", "iron")), Person("Raider 2", 45), Person("Raider 3", 35) };
        var raid = FightSetup.Raid(defenders, raiders, 5, 77, 8 * 3600, "raid");
        Assert.Equal(FightKind.Raid, raid.Kind); Assert.Equal(2, raid.Sides.Count); Assert.Equal(SideKind.Bandits, raid.Sides[1].Kind);
        var rw = raid.Start(fx.C, null);
        Assert.Equal(SideKind.Bandits, rw.SideOf(1));
        Assert.All(rw.Fighters.Where(f => f.Team == 1), f => Assert.True(f.Kit.X >= 15_000));
        for (int t = 0; t < 20 * 240 && !rw.IsOver; t++) rw.StepWithAi();
        Assert.True(rw.IsOver);

        var amb = FightSetup.Ambush(defenders, raiders, 6, 78, 8 * 3600, "ambush_attack");
        Assert.Equal(FightKind.Ambush, amb.Kind);
        Assert.All(amb.Kits.Where(k => k.Team == 1), k => Assert.InRange(FightMath.Len(k.X, k.Y), 5_000, 7_000));

        var draugr = new FighterKit { Name = "Draugr", Weapon = new WeaponInstance("axe", "iron"), Skill = 45, Plan = "human", Worn = [] };
        var site = FightSetup.SiteInhabitants([Person("Hero") with { Person = 7 }], [draugr, draugr with { Name = "Draugr 2", X = 2000 }], SideKind.Draugr, 7, 4401, 2, 9 * 3600, heroIndex: 0);
        Assert.Equal(FightKind.SiteInhabitants, site.Kind); Assert.Equal("site_inhabitants", site.KindName);
        var sw = site.Start(fx.C, null);
        Assert.Equal(SideKind.Draugr, sw.SideOf(1)); Assert.Equal(0, sw.LodFighter);
        Assert.Equal(FightKind.SiteInhabitants, FightSetup.KindOf("site_inhabitants"));
        Assert.Null(FightSetup.KindOf("test"));
    }

    [Fact]
    public void OutsideTheBubble_AFightGoesToTheAbstractSeam_TheAbstractBattle()
    {
        var setup = FightSetup.Raid([Person("Farmer", 20)], [Person("Raider 1", 55, worn: FightCalibration.Mailed.Worn), Person("Raider 2", 50)], 9, 79, 8 * 3600, "raid");
        var live = setup.Run(fx.C, null, inBubble: true);
        Assert.NotNull(live.Live); Assert.Null(live.Report);
        var far = setup.Run(fx.C, null, inBubble: false);
        Assert.Null(far.Live);
        var rep = far.Report!;
        // step 6: the default behind the seam is the abstract battle (abstract-battles-v1); the stub remains for content without wound tables
        Assert.False(rep.Stub); Assert.NotNull(rep.Battle); Assert.Equal(1, rep.WinnerTeam); Assert.True(rep.OddsPm > 500);
        Assert.True(rep.OutByTeam[0] >= 1);
        var again = setup.Run(fx.C, null, inBubble: false).Report!;           // deterministic
        Assert.Equal((rep.WinnerTeam, rep.OddsPm, rep.FightId, rep.Battle!.Hash), (again.WinnerTeam, again.OddsPm, again.FightId, again.Battle!.Hash));
        var stub = setup.Run(fx.C, null, inBubble: false, AbstractFightStub.Instance).Report!;
        Assert.True(stub.Stub); Assert.All(stub.OutByTeam.Values, v => Assert.Equal(0, v));
        // step 6 plugs its battles in behind the interface; callers do not change
        var custom = setup.Run(fx.C, null, inBubble: false, new FixedAbstract());
        Assert.False(custom.Report!.Stub);
    }

    private sealed class FixedAbstract : IAbstractFights
    {
        public AbstractFightReport Resolve(FightSetup setup, FightContent content) => new(setup.FightId, setup.Kind, 0, 0, new Dictionary<int, int>(), false, setup.Cause);
    }

    [Fact]
    public void TheEncounterFight_IsOfferedWithOdds_NeverForced_AndTheHeroIsCalledByName()
    {
        var def = fx.Db.Get<EncounterDef>("bandit_ambush_small");
        var ev = new EncounterEvent(500_000, def.Id, def.Kind, 0, 0, 0, 0, 1, [101, 102, 103], def.Bark);
        var weak = FightSetup.Encounter(fx.C, def, ev, Person("Aldwin", 15) with { Person = 7 }, [], 3);
        var strong = FightSetup.Encounter(fx.C, def, ev, Person("Aldwin", 70, shield: "heater", worn: FightCalibration.Mailed.Worn) with { Person = 7 }, [Person("Brand", 60, worn: FightCalibration.Mailed.Worn)], 3);
        int lo = RiskEstimate.OddsOf(weak, fx.C), hi = RiskEstimate.OddsOf(strong, fx.C);
        Assert.True(lo < 350, $"weak {lo}"); Assert.True(hi > lo);
        Assert.Equal("wm_odds_lose", RiskEstimate.Word(Math.Max(100, lo)));
        var opts = EncounterOutcomes.Options(def, 500, lo);
        var fight = opts.Single(o => o.Choice == EncounterChoice.Fight);
        Assert.Equal(lo, fight.OddsPm);
        Assert.Contains(opts, o => o.Choice is EncounterChoice.Flee or EncounterChoice.Pay);    // never forced: a way out is always beside it
        Assert.Equal(-1, EncounterOutcomes.Options(def, 500).Single(o => o.Choice == EncounterChoice.Fight).OddsPm);
        // the road fight's hero is the record's name, not "You" (the log said "You charges")
        var rec = new Ravensguard.Sim.Characters.HeroRecord { Name = "Aldwin", Origin = "x" };
        Assert.Equal("Aldwin", FightSetup.HeroKit(fx.C, rec, [], 7).Name);
        Assert.Equal(SideKind.Bandits, weak.SideKinds[1]);
    }
}
