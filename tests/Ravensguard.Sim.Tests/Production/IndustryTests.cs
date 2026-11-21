using Ravensguard.Content;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Goods;
using Ravensguard.Sim.People;
using Ravensguard.Sim.Production;
using Ravensguard.Sim.Production.Industry;
using Ravensguard.Sim.Save;
using Ravensguard.Sim.Tests.Economy;

namespace Ravensguard.Sim.Tests.Production;

/// <summary>P7-IN1 (industries 10): IN-1 to IN-3, IN-8, IN-16, IN-19, IN-23, IN-26, plus the content's crew bands, the stop reasons, the slider, the labour market's crew target and the save section.</summary>
public class IndustryTests
{
    private static GameTime At(long day, int hour = 9) => GameTime.FromGameHours(day * 24 + hour);
    private static IndustryContent Ind => ProdKit.Content.Industries;

    private sealed class Kit
    {
        public ProdKit K; public IndustryBook Book = new(); public IndustryWorld World; public Works W = null!;
        public Kit(string chain, int households = 40, long capital = 1_000_000)
        {
            K = new ProdKit(households: households);
            World = new IndustryWorld(Book, K.Sys, EconFixture.Content, 42);
            K.Fund(0, capital + 100_000);
            var r = World.Found(chain, 0, 0, K.Econ.HouseholdPurse[0], capital, 0);
            Assert.True(r.Ok, r.Key);
            W = Book.Works[0];
        }
        public Business B(int stage) => K.Sys.ById(new EntityId(W.Stages[stage].Business))!;
        public StageKind Kind(int stage) => World.KindOf(W.Stages[stage]);
        /// <summary>Puts persons (from the next free one) on a stage's books: <paramref name="n"/> of the stage's trade at skill 50, and <paramref name="hands"/> carriers.</summary>
        public void Crew(int stage, int n, int hands = 0, int skill = 50)
        {
            var k = Kind(stage); var b = B(stage);
            for (int i = 0; i < n + hands; i++)
            {
                int p = _next++; bool prim = i < n;
                K.People.Occupation[p] = prim ? k.Primary : k.Support;
                K.People.SetSkill(p, 0, k.Skill == Skill.None ? Skill.Farming : k.Skill, skill);
                K.Sys.Employ(b, p, prim ? k.Primary : k.Support);
            }
        }
        private int _next = 1;
        public long Have(int stage, string good) => B(stage).Store.Qty(K.Cat.IdOf(good));
        public void Give(int stage, string good, long g) => B(stage).Store.Create(K.Cat.IdOf(good), g, CreateReason.Seed);
        public void Day(long day = 0) => World.Day(W, (int)day, At(day));
    }

    // ---------------------------------------------------------------- the stage formula

    [Fact]
    public void IN1_StageDay_MineL1_TwoHewersAndAHand_At50_Is1000Kg()
    {
        var t = new Kit("iron");
        t.Crew(0, 2, hands: 1);
        t.Day();
        var s = t.World.Status(t.W.Stages[0].Business)!;
        Assert.Equal(1_000_000, s.MadeG);
        Assert.Equal(StageStop.None, s.Stop);
        // the day's ore went on to the crusher's store by the stage's link
        Assert.Equal(1_000_000, t.Have(1, "iron_ore_rough") + t.Have(0, "iron_ore_rough"));
    }

    [Fact]
    public void IN1_Formula_Pure_HaulHandsLimitTheHewers_AndNoHandsIsNoLimit()
    {
        var k = Ind.Stage("stage_iron_mine")!;
        StageWorker H() => new(50, 1000, true); StageWorker C() => new(50, 1000, false);
        Assert.Equal(1_000_000, StageMath.Plan(k, 1, [H(), H(), C()], 1000, 1000, 100).PotentialG);
        Assert.Equal(1_000_000, StageMath.Plan(k, 1, [H(), H()], 1000, 1000, 100).PotentialG);               // no carriers at all: hauling does not limit (Phase 2's rule)
        Assert.Equal(1_000_000, StageMath.Plan(k, 1, [H(), H(), H(), C()], 1000, 1000, 100).PotentialG);        // 3 hewers, one carrier: only 2 hewers' worth is served
        Assert.Equal(0, StageMath.Plan(k, 1, [H()], 1000, 1000, 100).PotentialG);                               // under the first tier's minimum of 2
    }

    [Fact]
    public void IN2_UnderCrewed_SmelteryL1_WithOne_MakesNothing()
    {
        var t = new Kit("iron");
        t.Crew(3, 1); t.Give(3, "iron_ore", 200_000); t.Give(3, "charcoal", 300_000);
        t.Day();
        var s = t.World.Status(t.W.Stages[3].Business)!;
        Assert.Equal(StageStop.UnderCrewed, s.Stop); Assert.Equal(0, s.MadeG);
        Assert.Equal(200_000, t.Have(3, "iron_ore")); Assert.Equal(0, t.Have(3, "iron_bloom"));
    }

    [Fact]
    public void IN3_CrewMax_HiringStopsAtTheTiersMaximum()
    {
        var t = new Kit("iron", households: 60);
        Assert.Equal(5, Ind.Stage("stage_iron_smeltery")!.Tier(1).CrewMax);
        var r = t.World.SetCrew(t.W.Stages[3].Business, 9);
        Assert.Equal(5, t.W.Stages[3].CrewTarget); Assert.True(r.Ok);
        var lm = new LabourMarket(t.K.Sys, t.K.Pop, t.K.Econ, 42, _ => []);
        lm.Daily(At(0, 0)); lm.Daily(At(1, 0));
        Assert.Equal(5, t.B(3).Staff.Count);
        Assert.Equal(2, t.B(0).Staff.Count);                                                                      // the mine's target is the first tier's minimum
        Assert.Equal(2, t.B(0).Staff.Count(p => t.K.People.Occupation[p] == Occupation.Miner));
    }

    [Fact]
    public void IN3_CrewTarget_ClampsToTheBand_AndTheMineHiresHewersAndCarriers()
    {
        var t = new Kit("iron", households: 60);
        t.World.SetCrew(t.W.Stages[0].Business, 4);
        var lm = new LabourMarket(t.K.Sys, t.K.Pop, t.K.Econ, 42, _ => []);
        lm.Daily(At(0, 0)); lm.Daily(At(1, 0));
        Assert.Equal(4, t.B(0).Staff.Count);
        Assert.Equal(3, t.B(0).Staff.Count(p => t.K.People.Occupation[p] == Occupation.Miner));                    // 4 posts: 3 hewers and 1 carrier (a carrier for two hewers, rounded up)
        Assert.Equal(2, t.World.SetCrew(t.W.Stages[0].Business, 0) is { Ok: true } ? t.W.Stages[0].CrewTarget : -1);   // the target never falls under the band
    }

    // ---------------------------------------------------------------- hauling and the slider

    [Fact]
    public void IN8_HaulingLink_600m_OneCart_TwoTrips_1000Kg()
    {
        Assert.Equal(2, StageMath.Trips(Ind.Settings, 600, false));
        Assert.Equal(1_000_000, StageMath.LinkCapacityG(Ind.Settings, new WorkLink { DistanceM = 600, Carts = 1 }));
        Assert.Equal(2_000_000, StageMath.LinkCapacityG(Ind.Settings, new WorkLink { DistanceM = 600, Wagons = 1 }));
        Assert.Equal(0, StageMath.LinkCapacityG(Ind.Settings, new WorkLink { DistanceM = 600 }));                  // beyond 60 m with no cart: nothing moves
        Assert.Equal(long.MaxValue, StageMath.LinkCapacityG(Ind.Settings, new WorkLink { DistanceM = 60 }));      // within 60 m the crews' own hands pass it
        Assert.True(StageMath.Trips(Ind.Settings, 600, true) > 2);                                                 // packed goods or a crane load in half the time
    }

    [Fact]
    public void IN8_HaulingLink_IsTheBottleneck_InTheWorld()
    {
        var t = new Kit("iron");
        t.World.SetLink(t.W.Id, 1, 600, 1, 0);
        t.Crew(0, 2, hands: 1);
        t.Give(0, "iron_ore_rough", 0);
        t.Day();
        Assert.Equal(1_000_000, t.World.Status(t.W.Stages[0].Business)!.MadeG);
        Assert.Equal(1_000_000, t.World.Status(t.W.Stages[0].Business)!.MovedG);                                   // the day's whole 1,000 kg fits one cart's two trips
        t.World.SliderPct = () => 200;                                                                             // the mine now makes 2,000 kg a day
        t.Day(1);
        Assert.Equal(2_000_000, t.World.Status(t.W.Stages[0].Business)!.MadeG);
        Assert.Equal(1_000_000, t.World.Status(t.W.Stages[0].Business)!.MovedG);                                   // but only 1,000 kg a day travel
        Assert.True(t.Have(0, "iron_ore_rough") >= 1_000_000);
    }

    [Fact]
    public void IN16_Slider300_MineMakes3000Kg_AndTheSmelterysCharcoalPerUnitIsUnchanged()
    {
        var t = new Kit("iron");
        t.World.SliderPct = () => 300;
        t.Crew(0, 2);
        t.Crew(3, 5); t.Give(3, "iron_ore", 2_000_000); t.Give(3, "charcoal", 5_000_000);
        t.Day();
        Assert.Equal(3_000_000, t.World.Status(t.W.Stages[0].Business)!.MadeG);
        long ore = 2_000_000 - t.Have(3, "iron_ore"), coal = 5_000_000 - t.Have(3, "charcoal");
        Assert.Equal(600_000, ore);                                                                                 // 5 units x 40 kg x 3
        Assert.Equal(ore * 3 / 2, coal);                                                                            // 1.5 kg of charcoal a kg of ore, whatever the slider
        Assert.Equal(ore * 150 / 1000, t.Have(3, "iron_bloom") + t.Have(4, "iron_bloom"));
    }

    [Fact]
    public void Slider_DefaultIsTheDifficultys100_AndIsTheSameWorldRuleForEveryWorks()
    {
        Assert.Equal(100, Difficulty.Default.IndustryOutputPct);
        var t = new Kit("iron"); t.Crew(0, 2, hands: 1); t.Day();
        Assert.Equal(1_000_000, t.World.Status(t.W.Stages[0].Business)!.MadeG);
    }

    // ---------------------------------------------------------------- generated industries

    [Fact]
    public void IN19_CompositeRate_ForAGeneratedMine_Is109KgDressedOreAWorkerDay()
    {
        Assert.Equal(109, Ind.CompositeGPerWorkerDay / 1000);
        var k = new ProdKit(households: 6);
        k.People.Occupation[0] = Occupation.Miner;
        var b = k.Sys.NewBusiness(k.Used.Kind("mine"), 0, 0, 0); b.OwnerWorks = false;
        for (int p = 1; p <= 3; p++) { k.People.Occupation[p] = Occupation.Miner; k.Sys.AddStaff(b, p); }
        k.Sys.AddMine(new MineWorks { SiteIndex = 0, SiteId = EntityId.Make(IdKind.Settlement, 99), Mineral = Ravensguard.Sim.WorldGen.MineralType.Iron, RemainingG = 100_000_000_000, InitialG = 100_000_000_000, Levels = 1, State = MineStateKind.Active });
        b.Mine = 0;
        k.Sys.WorkHour(GameTime.FromGameHours(6));
        Assert.Equal(3 * Ind.CompositeGPerWorkerDay, k.Have(b, "iron_ore"));                                       // three miners, skill-mult 1,000
        Assert.Equal(3 * Ind.CompositeGPerWorkerDay * 1000 / 600, 100_000_000_000 - k.Sys.Mines[0].RemainingG);     // the body gives up the rough ore behind it
    }

    // ---------------------------------------------------------------- shared purse and conservation

    [Fact]
    public void IN23_SharedPurse_WagesOfThreeStages_FromOneBox_RowsSumToTheWorks_CoinAndMassExact()
    {
        var t = new Kit("iron");
        t.Crew(0, 2, hands: 1); t.Crew(1, 1); t.Crew(3, 3);
        var purse = t.B(0).Purse;
        Assert.Equal(purse, t.B(1).Purse); Assert.Equal(purse, t.B(3).Purse); Assert.Equal(purse, t.B(4).Purse);
        t.Give(3, "iron_ore", 400_000); t.Give(3, "charcoal", 900_000);
        long before = t.K.Econ.Ledger.Balance(purse).Mites;
        t.Day();
        long after = t.K.Econ.Ledger.Balance(purse).Mites;
        long rows = t.W.Stages.Sum(s => s.Sum(WorkStage.Wages, 0));
        Assert.Equal(before - after, rows);                                                                         // the box paid exactly what the rows say
        // mine 2 hewers + 1 hand (all unskilled, 150), crusher 1 hand (150), smeltery 3 at skill 50 (journeymen below the master line: skilled class, role by skill 30..59: 350)
        Assert.Equal(3 * 150 + 150 + 3 * 350, rows);
        Assert.Equal(rows, t.W.Stages[0].Sum(WorkStage.Wages, 0) + t.W.Stages[1].Sum(WorkStage.Wages, 0) + t.W.Stages[3].Sum(WorkStage.Wages, 0));
        Assert.Equal(0, t.W.Stages[2].Sum(WorkStage.Wages, 0));
        Assert.True(t.K.Econ.Ledger.Audit().IsConserved);
        foreach (var good in new[] { "iron_ore_rough", "iron_ore", "iron_bloom", "charcoal" })
        {
            ushort g = t.K.Cat.IdOf(good); long held = 0; foreach (var s in t.W.Stages) held += t.K.Sys.ById(new EntityId(s.Business))!.Store.Qty(g);
            Assert.Equal(t.K.Econ.Mass.Net(g), held);                                                               // every gram created is somewhere or destroyed with its reason
        }
    }

    [Fact]
    public void Crusher_YieldIsTheDrawsShare_AndTheRestIsWasteNotConsumed()
    {
        var t = new Kit("iron");
        t.World.ForceYieldPm = () => 600;
        t.Crew(1, 3); t.Give(1, "iron_ore_rough", 1_000_000);
        var mass = t.K.Econ.Mass; ushort rough = t.K.Cat.IdOf("iron_ore_rough");
        t.Day();
        long dressed = 0; for (int i = 0; i < 5; i++) dressed += t.Have(i, "iron_ore");                              // the ore moved on down the works in the same day
        Assert.Equal(600_000, dressed);                                      // 1,000 kg of rough ore gave 600 kg dressed...
        Assert.Equal(400_000, mass.Destroyed(rough, DestroyReason.Waste));                                         // ...and 400 kg of tailings
        Assert.Equal(600_000, mass.Destroyed(rough, DestroyReason.Consumed));
    }

    // ---------------------------------------------------------------- tiers, short hands and spare hours

    [Fact]
    public void IN26_ShortHanded_SmelteryRecognisedL2_WithFiveHands_WorksAtL1()
    {
        var t = new Kit("iron");
        t.World.SetTier(t.W.Stages[3].Business, 2);
        t.Crew(3, 5); t.Give(3, "iron_ore", 200_000); t.Give(3, "charcoal", 300_000);
        t.Day();
        var s = t.World.Status(t.W.Stages[3].Business)!;
        Assert.Equal(1, s.WorkingTier); Assert.Equal(2, s.RecognisedTier); Assert.True(s.ShortHanded);
        Assert.Equal(5, s.Have); Assert.Equal(6, s.Need);                                                          // "L2, short-handed: 5 of 6"
        Assert.Equal(200_000, s.MadeG);                                                                             // 5 units at L1's 10 worker-hours each
        Assert.Equal(30_000, t.Have(3, "iron_bloom") + t.Have(4, "iron_bloom"));
    }

    [Fact]
    public void IN26_SpareHours_AnL2YardWithNoHands_MovesWhatFourLentHoursDo_AndNeverCountsTowardItsMinimum()
    {
        var t = new Kit("iron");
        t.World.SetTier(t.W.Stages[2].Business, 2);
        t.Crew(3, 5); t.Give(3, "iron_ore", 184_000); t.Give(3, "charcoal", 276_000);                               // 4.6 units: the smeltery's five hands idle 4 hours of 50
        t.Give(2, "iron_ore", 2_000_000);
        t.Day();
        var y = t.World.Status(t.W.Stages[2].Business)!;
        Assert.Equal(1, y.WorkingTier); Assert.Equal(4000, y.LentMilliHours);
        Assert.Equal(1_200_000, y.MovedG);                                                                          // 4 h of 10 at L1's 3,000 kg a day
        // with one hand of its own it is still L1 ("short-handed: 1 of 2"), the day's work its own rate
        var t2 = new Kit("iron");
        t2.World.SetTier(t2.W.Stages[2].Business, 2); t2.Crew(2, 1); t2.Give(2, "iron_ore", 5_000_000);
        t2.Day();
        var y2 = t2.World.Status(t2.W.Stages[2].Business)!;
        Assert.True(y2.ShortHanded); Assert.Equal(1, y2.Have); Assert.Equal(2, y2.Need);
        Assert.Equal(3_000_000, y2.MovedG);
    }

    [Fact]
    public void Stops_AreNamed_WithTheGood()
    {
        var t = new Kit("iron");
        t.Crew(3, 5); t.Give(3, "iron_ore", 200_000);
        t.Day();
        var s = t.World.Status(t.W.Stages[3].Business)!;
        Assert.Equal(StageStop.NoFuel, s.Stop); Assert.Equal(t.K.Cat.IdOf("charcoal"), s.StopGood);
        var t2 = new Kit("iron"); t2.Crew(3, 5); t2.Give(3, "charcoal", 500_000); t2.Day();
        Assert.Equal(StageStop.NoInput, t2.World.Status(t2.W.Stages[3].Business)!.Stop);
        var t3 = new Kit("iron"); t3.Crew(0, 2, 1);
        t3.World.ExternalStop = (w, s2) => (StageStop.Flooded, ushort.MaxValue);
        t3.Day();
        Assert.Equal(StageStop.Flooded, t3.World.Status(t3.W.Stages[0].Business)!.Stop);
        Assert.Equal(0, t3.World.Status(t3.W.Stages[0].Business)!.MadeG);
    }

    [Fact]
    public void AStageStoppedMoreThanThreeDays_WritesWorksStoppedOnce()
    {
        var t = new Kit("iron"); var ch = new Chronicle();
        t.World.Chron = Ravensguard.Sim.Rule.RuleChron.Of(ch, new IdAllocator());
        t.Crew(3, 5); t.Give(3, "iron_ore", 200_000);
        for (int d = 0; d < 7; d++) t.Day(d);
        Assert.Single(ch.All, e => e.TypeId == "works_stopped" && e.Params!.Any(p => p.Key == "ind_stage_stage_iron_smeltery"));
        Assert.Equal(7, t.W.Stages[3].StopDays);
    }

    // ---------------------------------------------------------------- content

    [Fact]
    public void Content_17StageKinds_ThreeChains_CrewBandsRiseWithTheTierAndNeverOverlap()
    {
        Assert.Equal(17, Ind.Stages.Length); Assert.Equal(3, Ind.Chains.Length);
        foreach (var k in Ind.Stages)
        {
            for (int t = 2; t <= k.TierCount; t++)
            {
                Assert.True(k.Tier(t).CrewMin == k.Tier(t - 1).CrewMax + 1, $"{k.Id} L{t} starts one above L{t - 1}'s maximum");
                Assert.True(k.Tier(t).CrewMax > k.Tier(t - 1).CrewMax, k.Id);
            }
            if (k.Role == StageRole.Handle && k.TierCount >= 2) { Assert.True(k.Tier(2).CrewMin >= 2, k.Id); if (k.TierCount >= 3) Assert.True(k.Tier(3).CrewMin >= 4, k.Id); }
        }
        (int, int) Total(string chain, int tier) { var c = Ind.Chain(chain)!; return (c.Stages.Sum(s => s.Tier(Math.Min(tier, s.TierCount)).CrewMin), c.Stages.Sum(s => s.Tier(Math.Min(tier, s.TierCount)).CrewMax)); }
        Assert.Equal((5, 14), Total("iron", 1)); Assert.Equal((19, 39), Total("iron", 2)); Assert.Equal((44, 67), Total("iron", 3));
        Assert.Equal((8, 16), Total("clay", 1)); Assert.Equal((22, 39), Total("clay", 2)); Assert.Equal((45, 68), Total("clay", 3));
        Assert.Equal((4, 15), Total("wood", 1)); Assert.Equal((21, 30), Total("wood", 2)); Assert.Equal((33, 43), Total("wood", 3));
    }

    [Fact]
    public void SkillCurve_1000At50_AndSkill20Against60Spans118()
    {
        Assert.Equal(1000, StageMath.SpeedPm(false, 50)); Assert.Equal(1000, StageMath.SpeedPm(true, 50));
        Assert.Equal(880, StageMath.SpeedPm(false, 20)); Assert.Equal(1040, StageMath.SpeedPm(false, 60));        // 1.18x, not the sheets' 2x (industries 5.1a)
    }

    [Fact]
    public void StageKinds_AreBusinessKindsAfterEveryOrdinaryKind()
    {
        var kinds = ProdKit.Content.Kinds; int firstStage = Array.FindIndex(kinds, k => k.Stage != null);
        Assert.True(firstStage > 0); Assert.True(kinds.Skip(firstStage).All(k => k.Stage != null)); Assert.True(kinds.Take(firstStage).All(k => k.Stage == null));
        Assert.All(kinds.Where(k => k.Stage != null), k => Assert.True(k.FoundedOnly));
    }

    // ---------------------------------------------------------------- saves

    [Fact]
    public void Save_SectionRoundTrip_IsByteIdentical_AndEmptyBookIsAbsent()
    {
        Assert.True(new IndustryBook().IsEmpty); Assert.Equal(0UL, new IndustryBook().Hash());
        var t = new Kit("clay");
        t.Crew(0, 2); t.Crew(2, 2); t.World.SetLink(t.W.Id, 1, 120, 2, 1); t.World.SetRoute(t.W.Stages[2].Business, Routing.Store);
        t.Give(0, "clay", 500_000); t.Day(); t.Day(1);
        var back = IndustrySave.Capture(t.Book).Restore();
        Assert.Equal(t.Book.Hash(), back.Hash()); Assert.NotEqual(0UL, t.Book.Hash());
        Assert.Equal(SaveCodec.SerializeCompressed(IndustrySave.Capture(t.Book)), SaveCodec.SerializeCompressed(IndustrySave.Capture(back)));
        Assert.Equal(t.W.Stages[0].Sum(WorkStage.Wages, 1), back.Works[0].Stages[0].Sum(WorkStage.Wages, 1));
    }

    [Fact]
    public void Determinism_TwoRuns_SameHash()
    {
        ulong Run()
        {
            var t = new Kit("wood"); t.Crew(0, 2); t.Crew(1, 2); t.Crew(3, 4);
            for (int d = 0; d < 20; d++) t.Day(d);
            return t.Book.Hash();
        }
        Assert.Equal(Run(), Run());
    }

    [Fact]
    public void ClayWorks_FlowsDownTheChain_AndTheCoinAuditHolds()
    {
        var t = new Kit("clay");
        t.Crew(0, 3); t.Crew(1, 3); t.Crew(2, 4); t.Crew(3, 2); t.Crew(4, 3); t.Give(4, "firewood", 20_000_000);
        for (int d = 0; d < 8; d++) t.Day(d);
        long bricks = 0, green = 0; foreach (var s in t.W.Stages) { var b = t.K.Sys.ById(new EntityId(s.Business))!; bricks += b.Store.Qty(t.K.Cat.IdOf("bricks")); green += b.Store.Qty(t.K.Cat.IdOf("bricks_green")); }
        Assert.True(bricks > 0, string.Join(" | ", t.W.Stages.Select((s, i) => $"{i}: {t.World.Status(s.Business)}")));
        Assert.True(t.K.Econ.Ledger.Audit().IsConserved);
        Assert.True(t.W.Stages.Sum(s => s.Sum(WorkStage.Wages, 7, 8)) > 0);
    }
}
