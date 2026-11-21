using Ravensguard.Content;
using Ravensguard.Sim.Building;
using Ravensguard.Sim.Combat.Wounds;
using Ravensguard.Sim.Construction;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Goods;
using Ravensguard.Sim.People;
using Ravensguard.Sim.Production;
using Ravensguard.Sim.Production.Industry;
using Ravensguard.Sim.Save;
using Ravensguard.Sim.Tests.Economy;

namespace Ravensguard.Sim.Tests.Production;

/// <summary>P7-IN2 (industries 10): IN-4 to IN-7, IN-12 to IN-15, IN-21, IN-27 and IN-28, with the recognition rules, the machines' share, the stage site's save and the tier plans' content.</summary>
public class IndustryTierTests
{
    private static GameTime At(long day, int hour = 9) => GameTime.FromGameHours(day * 24 + hour);
    private static IndustryContent Ind => ProdKit.Content.Industries;
    private static ContentDatabase Db => EconFixture.Content;

    private const int SummerDay = 20, WinterDay = 45;                      // days 15-29 summer, 45-59 winter

    private sealed class Kit
    {
        public ProdKit K; public IndustryBook Book = new(); public IndustryWorld World; public Works W = null!; public ConstructionWorld Cw;
        public Kit(string chain, int households = 40, long capital = 1_000_000)
        {
            K = new ProdKit(households: households);
            World = new IndustryWorld(Book, K.Sys, Db, 42);
            Cw = new ConstructionWorld(new ConstructionBook(), K.Econ, PieceCosts.Of(Db)) { Pop = K.Pop, Content = Db };
            World.Build = Cw;
            K.Fund(0, capital + 100_000);
            var r = World.Found(chain, 0, 0, K.Econ.HouseholdPurse[0], capital, 0);
            Assert.True(r.Ok, r.Key);
            W = Book.Works[0];
        }
        public Business B(int stage) => K.Sys.ById(new EntityId(W.Stages[stage].Business))!;
        public ulong Id(int stage) => W.Stages[stage].Business;
        public void Crew(int stage, int n, int hands = 0, int skill = 50)
        {
            var k = World.KindOf(W.Stages[stage]); var b = B(stage);
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
        public void Give(int stage, string good, long q) => B(stage).Store.Create(K.Cat.IdOf(good), q, CreateReason.Seed);
        public void Day(long day = 0) => World.Day(W, (int)day, At(day));
        public StageStatus St(int stage) => World.Status(W.Stages[stage].Business)!;
        public void Put(int x, int y, string piece)
        {
            var c = Cw.Costs.Get(piece)!;
            var b = new BuiltCell { X = x, Y = y, Z = 0, Slot = GhostSlot.Fixture, Piece = piece, Hp = PieceCosts.BuiltHp(c, 50), BuiltSkill = 50 };
            Cw.Book.Built[b.SlotKey] = b;
        }
        public void Remove(int x, int y) => Cw.Book.Built.Remove(ConstructionIds.SlotKey(x, y, 0, GhostSlot.Fixture));
        public long All(string good) { ushort g = K.Cat.IdOf(good); long q = 0; foreach (var s in W.Stages) q += K.Sys.ById(new EntityId(s.Business))!.Store.Qty(g); return q; }
    }

    // ---------------------------------------------------------------- IN-4, the dressing (with the yield forced)

    [Fact]
    public void IN4_Dressing_1000KgRough_At600Permille_Gives600KgOre_And400KgWaste_MassExact()
    {
        var t = new Kit("iron");
        t.World.ForceYieldPm = () => 600;
        t.Crew(1, 3); t.Give(1, "iron_ore_rough", 1_000_000);
        t.Day();
        Assert.Equal(600_000, t.All("iron_ore"));
        Assert.Equal(400_000, t.K.Econ.Mass.Destroyed(t.K.Cat.IdOf("iron_ore_rough"), DestroyReason.Waste));
        foreach (var good in new[] { "iron_ore_rough", "iron_ore" }) Assert.Equal(t.K.Econ.Mass.Net(t.K.Cat.IdOf(good)), t.All(good));
    }

    // ---------------------------------------------------------------- IN-5, recognition from the pieces

    [Fact]
    public void IN5_Recognition_RailsAndHunds_MakeTheMineL2TheSameDay_AndRemovingThemDropsItBack()
    {
        var t = new Kit("iron"); var ch = new Chronicle();
        t.World.Chron = Ravensguard.Sim.Rule.RuleChron.Of(ch, new IdAllocator());
        Assert.Equal(OwnResult.Yes("in_site_set").Key, t.World.SetSite(t.Id(0), 0, 0, 30, 30).Key);
        t.Crew(0, 2, 1);
        t.Day(0);
        Assert.Equal(1, t.W.Stages[0].Tier);
        for (int i = 0; i < 8; i++) t.Put(i, 0, "mine_rail");
        t.Put(0, 2, "hund");
        t.Day(1);
        Assert.Equal(1, t.W.Stages[0].Tier);                                    // one hund short
        Assert.Equal(["hund x2"], t.World.Missing(t.W.Stages[0], 2));
        t.Put(1, 2, "hund");
        t.Day(2);
        Assert.Equal(2, t.W.Stages[0].Tier);
        Assert.Single(ch.All, e => e.TypeId == "works_upgraded");
        t.Remove(0, 2);
        t.Day(3);
        Assert.Equal(1, t.W.Stages[0].Tier);                                    // a hund gone: L1 the same day
        t.Put(0, 2, "hund");
        t.Cw.Book.Built[ConstructionIds.SlotKey(3, 0, 0, GhostSlot.Fixture)].Hp = 0;   // a worn-out rail no longer counts
        t.Day(4);
        Assert.Equal(1, t.W.Stages[0].Tier);
    }

    [Fact]
    public void Recognition_NeedsEveryLowerTier_AndAStageWithoutASiteKeepsItsSetTier()
    {
        var t = new Kit("iron");
        Assert.Equal(1, IndustryWorld.RecognisedTier(t.World.KindOf(t.W.Stages[3]), new Dictionary<string, int> { ["great_bellows"] = 3, ["bloomery"] = 3, ["trip_hammer"] = 1 }));   // L3's pieces without L2's finery: L1
        Assert.Equal(3, IndustryWorld.RecognisedTier(t.World.KindOf(t.W.Stages[3]), new Dictionary<string, int> { ["great_bellows"] = 3, ["bloomery"] = 3, ["trip_hammer"] = 1, ["finery_hearth"] = 1 }));
        t.World.SetTier(t.Id(3), 2);
        t.Day();
        Assert.Equal(2, t.W.Stages[3].Tier);                                     // no site: the owner's tier stands (IN1)
        Assert.Equal(["crusher_rolls", "washing_floor"], t.World.Missing(Probe(t, 1), 2));
    }

    private static WorkStage Probe(Kit t, int stage) { var s = t.W.Stages[stage]; s.SiteX0 = 0; s.SiteY0 = 0; s.SiteX1 = 5; s.SiteY1 = 5; return s; }

    // ---------------------------------------------------------------- IN-6, an upgrade is its plan's pieces

    [Fact]
    public void IN6_UpgradePlan_TheTierStaysL1UntilTheLastPieceIsBuilt_ThenTheBillGoesIntoTheWorksCost()
    {
        var t = new Kit("iron"); var ch = new Chronicle();
        t.World.Chron = Ravensguard.Sim.Rule.RuleChron.Of(ch, new IdAllocator());
        t.World.SetSite(t.Id(0), 0, 0, 5, 5);
        var next = t.World.NextUpgrade(t.W.Stages[0])!.Value;
        Assert.Equal((2, "tier_iron_mine_2"), next);
        var g = t.Cw.Plans().Grid(next.Plan)!;
        var cells = PlanLibrary.Cells(g, t.Cw.Costs).Where(c => t.Cw.Costs[c.Piece].Slot == GhostSlot.Fixture).ToList();
        // the builder's materials run out one piece short: the tier does not change
        Assert.True(t.World.BeginUpgrade(t.Id(0), 10, 10, g.Width, g.Height, 7).Ok);
        Assert.Equal(2, t.W.Stages[0].UpgradeTo);
        var hunds = cells.Where(c => c.Piece == "hund").ToList();
        var last = hunds[1];                                                     // the plan has 4 hunds, 2 are asked: the builder is one short
        foreach (var c in cells) if (c.Piece != "hund" || c == hunds[0]) t.Put(10 + c.X, 10 + c.Y, c.Piece);
        t.Day(0);
        Assert.Equal(1, t.W.Stages[0].Tier); Assert.Equal(2, t.W.Stages[0].UpgradeTo);
        t.Put(10 + last.X, 10 + last.Y, last.Piece);
        t.Day(1);
        Assert.Equal(2, t.W.Stages[0].Tier); Assert.Equal(0, t.W.Stages[0].UpgradeTo);
        Assert.InRange(t.W.BuildingsCostMites, 84_000, 126_000);                  // 5.3a: 105,000 within 20%
        Assert.Single(ch.All, e => e.TypeId == "works_upgraded");
    }

    // ---------------------------------------------------------------- IN-7, the machines' power

    [Fact]
    public void IN7_SmelteryL3_WithStoppedMachines_FallsBackTo18Units_AndSaysNoPower()
    {
        long Make(int power, out StageStatus st)
        {
            var t = new Kit("iron");
            t.World.SetTier(t.Id(3), 3); t.World.PowerPm = (w, s) => power;
            t.Crew(3, 18); t.Give(3, "iron_ore", 3_000_000); t.Give(3, "charcoal", 5_000_000);
            t.Day();
            st = t.St(3); return st.MadeG;
        }
        Assert.InRange(Make(1000, out var on), 1_000_000, 1_440_000); Assert.False(on.NoPower);                  // the crew (over its overseers' span) or the 36 units, whichever is less
        Assert.Equal(720_000, Make(0, out var off));                               // 18 units of 40 kg: hands alone, the bellows and hammer at 1,000 permille of the labour
        Assert.True(off.NoPower); Assert.Equal(StageStop.None, off.Stop);
    }

    [Fact]
    public void MachinePm_ReadsThePremisesNet_AGinGives1000WToTwoMachinesThatDraw2000W()
    {
        var t = new Kit("iron");
        var pw = new PowerWorld(t.Cw, PowerCatalog.Of(Db), 1234);
        t.World.Power = pw;
        t.World.SetSite(t.Id(0), 0, 0, 30, 30);
        for (int i = 0; i < 8; i++) t.Put(i, 0, "mine_rail");
        t.Put(0, 2, "hund"); t.Put(1, 2, "hund");
        t.Put(10, 10, "horse_gin"); t.Put(11, 10, "hoist_drum"); t.Put(9, 10, "rag_pump");
        t.Crew(0, 6, 4);
        pw.Refresh(1);
        t.Day(1);
        Assert.Equal(3, t.W.Stages[0].Tier);
        Assert.Equal(500, t.World.MachinePm(t.W, t.W.Stages[0]));                  // 1,000 W shared over 2,000 W of drawn machines
        t.World.SetSite(t.Id(0), 0, 0, 5, 5);
        t.Day(2);
        Assert.Equal(1, t.W.Stages[0].Tier);                                       // the premises moved away from its pieces: back to L1
    }

    // ---------------------------------------------------------------- the winter, the drying days (IN-12, IN-28)

    [Fact]
    public void IN12_Winter_AtAnL1Brickworks_DryingStops_MouldingStopsWhenTheHacksAreFull_PitAndYardWork()
    {
        var t = new Kit("clay");
        t.Crew(0, 3); t.Crew(1, 3); t.Crew(2, 4); t.Crew(3, 1);
        t.Give(3, "bricks_green", 16_800_000);                                     // 6,000 bricks fill the hacks
        t.Give(2, "tempered_clay", 100_000_000);
        for (int d = WinterDay; d < WinterDay + 6; d++) t.Day(d);
        Assert.Equal(0, t.St(3).MovedG);                                           // open hacks dry nothing in winter
        Assert.Equal(16_800_000, t.Have(3, "bricks_green"));
        Assert.Equal(StageStop.Full, t.St(2).Stop);                                // moulding waits for room
        Assert.True(t.St(0).MadeG > 0); Assert.True(t.St(1).MadeG > 0);
    }

    [Fact]
    public void IN28_Drying_L1SixDaysFromSpring_L2TwelveInWinter_L3SixInWinter()
    {
        long FirstMove(int tier, int crew, int start)
        {
            var t = new Kit("clay");
            t.World.SetTier(t.Id(3), tier); t.Crew(3, crew);
            t.Give(3, "bricks_green", 16_800_000);
            for (int d = start; d < start + 40; d++) { t.Day(d); if (t.St(3).MovedG > 0) return d - start; }
            return -1;
        }
        Assert.Equal(6, FirstMove(1, 1, SummerDay));
        Assert.Equal(12, FirstMove(2, 3, WinterDay));
        Assert.Equal(6, FirstMove(3, 5, WinterDay));
        var t1 = new Kit("clay"); t1.Crew(3, 1); t1.Give(3, "bricks_green", 16_800_000);
        for (int d = SummerDay; d < SummerDay + 6; d++) { t1.Day(d); Assert.Equal(0, t1.St(3).MovedG); }
        t1.Day(SummerDay + 6);
        Assert.Equal(2_800_000, t1.St(3).MovedG);                                  // 1,000 bricks a worker a day
    }

    // ---------------------------------------------------------------- IN-13, the kiln

    [Fact]
    public void IN13_Kiln_AFiringOf20000Bricks_With5PercentLost_Gives19000_AndBurns25000KgOfFirewood()
    {
        var t = new Kit("clay");
        t.World.ForceLossPm = () => 50;
        t.Crew(4, 3); t.Give(4, "bricks_green", 56_000_000); t.Give(4, "firewood", 60_000_000);
        long fire0 = t.Have(4, "firewood");
        for (int d = 0; d < 10; d++) t.Day(d);                                      // 5.6 t a day, 10 days
        long bricks = t.All("bricks");
        Assert.InRange(bricks, 47_400_000, 47_600_000);                             // 56 t green -> 50 t fired -> 5% lost
        long burnt = fire0 - t.Have(4, "firewood");
        Assert.InRange(burnt, 24_900_000, 25_100_000);
        long waste = t.K.Econ.Mass.Destroyed(t.K.Cat.IdOf("bricks"), DestroyReason.Waste);
        Assert.InRange(waste * 1000 / (bricks + waste), 49, 50);
        Assert.Equal(t.K.Econ.Mass.Net(t.K.Cat.IdOf("bricks")), bricks);
        Assert.True(t.K.Econ.Ledger.Audit().IsConserved);
    }

    // ---------------------------------------------------------------- IN-14, packing in dry casks

    [Fact]
    public void IN14_Packing_1000KgOfBar_In10Casks_LoadsDenser_And9CasksReturnAtTheBuyer_MassExact()
    {
        var t = new Kit("iron");
        t.Give(4, "iron_bar", 1_000_000); t.Give(4, "dry_cask", 10);
        long packed = t.World.Pack(t.Id(4), t.K.Cat.IdOf("iron_bar"), 1_000_000);
        Assert.Equal(1_000_000, packed);
        Assert.Equal(10, t.W.Stages[4].Packed[0].Containers);
        Assert.Equal(0, t.World.Pack(t.Id(4), t.K.Cat.IdOf("iron_bar"), 1_000_000));          // nothing loose, no free cask
        // 10% denser, half the loading time
        var s = Ind.Settings; var l = new WorkLink { DistanceM = 600, Carts = 1 };
        long loose = StageMath.LinkCapacityG(s, l), tight = StageMath.LinkCapacityG(s, l, goodsPacked: true);
        Assert.Equal(1_000_000, loose);                                             // floor(10 / (2 x 600 / 450 + 1)) = 2 trips of 500 kg
        Assert.Equal(1_650_000, tight);                                             // half an hour's loading: 3 trips, and 10% denser
        // at the buyer: the casks come back, a tenth worn out
        var buyer = t.B(3).Store;
        long back = t.World.Unpack(t.W.Stages[4], t.K.Cat.IdOf("iron_bar"), 1_000_000, buyer);
        Assert.Equal(9, back); Assert.Equal(9, buyer.Qty(t.K.Cat.IdOf("dry_cask"))); Assert.Equal(0, t.B(4).Store.Qty(t.K.Cat.IdOf("dry_cask")));
        Assert.Empty(t.W.Stages[4].Packed);
        ushort cask = t.K.Cat.IdOf("dry_cask");
        Assert.Equal(t.K.Econ.Mass.Net(cask), t.All("dry_cask"));
    }

    // ---------------------------------------------------------------- IN-15, a shortage

    [Fact]
    public void IN15_NoCharcoalFor4Days_StopsTheSmeltery_WithNoFuel_AndWritesWorksStopped()
    {
        var t = new Kit("iron"); var ch = new Chronicle();
        t.World.Chron = Ravensguard.Sim.Rule.RuleChron.Of(ch, new IdAllocator());
        t.Crew(3, 5); t.Give(3, "iron_ore", 400_000);
        for (int d = 0; d < 4; d++) t.Day(d);
        var s = t.St(3);
        Assert.Equal(StageStop.NoFuel, s.Stop); Assert.Equal(t.K.Cat.IdOf("charcoal"), s.StopGood);
        var e = Assert.Single(ch.All, e => e.TypeId == "works_stopped" && e.Params!.Any(p => p.Key == "ind_stage_stage_iron_smeltery"));
        Assert.Contains(e.Params!, p => p.Key == "ind_stop_nofuel");
    }

    // ---------------------------------------------------------------- the finery

    [Fact]
    public void Finery_L2Smeltery_TurnsBloomIntoBar_At800Permille_AndTheScaleIsWaste()
    {
        var t = new Kit("iron");
        t.World.SetTier(t.Id(3), 2); t.Crew(3, 6); t.Give(3, "iron_ore", 400_000); t.Give(3, "charcoal", 600_000);
        t.W.Stages[3].Route = Routing.Store;
        t.Day();
        Assert.True(t.Have(3, "iron_bar") > 0);
        Assert.Equal(36_000, t.Have(3, "iron_bar"));                                // 45 kg of bloom, 80% of it bar
        Assert.Equal(9_000, t.K.Econ.Mass.Destroyed(t.K.Cat.IdOf("iron_bloom"), DestroyReason.Waste));
        foreach (var good in new[] { "iron_bloom", "iron_bar" }) Assert.Equal(t.K.Econ.Mass.Net(t.K.Cat.IdOf(good)), t.All(good));
    }

    // ---------------------------------------------------------------- IN-21, an accident

    [Fact]
    public void IN21_Accident_AtAMineL1_IsAWoundThroughTheWoundBook_AndCountsOnce()
    {
        var t = new Kit("iron");
        var ledger = new WoundLedger(42, 0);
        t.World.Injure = (p, mm, ev, sec) => { FallWounds.Apply(Db, ledger, t.K.People.IdOf(p), mm, ev, sec, 42); return ledger.Find(t.K.People.IdOf(p).Value)?.Dead == true; };
        t.World.ForceAccident = () => true;
        t.Crew(0, 2, 1);
        t.Day();
        Assert.Equal(1, t.World.Accidented);
        Assert.Equal(1, ledger.Count);
        var book = ledger.Books.Values.Single();
        Assert.True(book.Records.Count > 0);
        // without the force the 1,000 ppm draw is a rare thing: 20 quiet days of 3 workers leave the count alone or at most a few
        var t2 = new Kit("iron"); t2.World.Injure = (p, mm, ev, sec) => false; t2.Crew(0, 2, 1);
        for (int d = 0; d < 20; d++) t2.Day(d);
        Assert.InRange(t2.World.Accidented, 0, 6);
    }

    // ---------------------------------------------------------------- the save

    [Fact]
    public void Save_CarriesSitesDryingBatchesAndPackedRows_AndResavesIdentically()
    {
        var w = EconFixture.WorldWith(null);
        w.Industry = new IndustryBook();
        var works = new Works { Owner = 0, Chain = "clay", Settlement = 0, StartedDay = 3 };
        works.Stages.Add(new WorkStage { Business = 5, SiteX0 = 1, SiteY0 = 2, SiteX1 = 9, SiteY1 = 8, UpgradeTo = 2, UpgradeSite = 4 });
        var d = new WorkStage { Business = 6 }; d.Dwell.Add((3000, 1_000_000)); d.Dwell.Add((0, 500)); d.Packed.Add(new PackedRow { Good = 4, Container = 9, Grams = 100_000, Containers = 1 });
        works.Stages.Add(d);
        w.Industry.Works.Add(works);
        var file = WorldSave.Capture(w, EconFixture.ContentHash, "0.1.0");
        var back = WorldSave.Restore(SaveCodec.Decode(SaveCodec.Encode(file)), EconFixture.Content, EconFixture.ContentHash);
        Assert.Equal(w.Industry.Hash(), back.Industry!.Hash());
        var bs = back.Industry.Works[0].Stages;
        Assert.True(bs[0].HasSite); Assert.Equal(2, bs[0].UpgradeTo); Assert.Equal(2, bs[1].Dwell.Count); Assert.Equal(1, bs[1].Packed[0].Containers);
        Assert.Equal(SaveCodec.Encode(file), SaveCodec.Encode(WorldSave.Capture(back, EconFixture.ContentHash, "0.1.0")));
    }

    // ---------------------------------------------------------------- content

    [Fact]
    public void Content_EveryTierAboveTheFirst_HasAPlanThatBuildsWhatItIsRecognisedFrom()
    {
        var t = new Kit("iron");
        var cw = t.Cw; var plans = cw.Plans();
        var pieces = Db.All<BuildPieceDef>().Select(p => p.Id).ToHashSet();
        foreach (var k in Ind.Stages)
        {
            var have = new Dictionary<string, int>();
            for (int tier = 2; tier <= k.TierCount; tier++)
            {
                var tt = k.Tier(tier);
                Assert.NotEmpty(tt.Clauses);
                Assert.True(plans.Get(tt.Plan) != null, $"{k.Id} L{tier}: plan '{tt.Plan}'");
                var g = plans.Grid(tt.Plan)!;
                foreach (var c in PlanLibrary.Cells(g, cw.Costs)) if (cw.Costs[c.Piece].Slot == GhostSlot.Fixture) have[c.Piece] = have.GetValueOrDefault(c.Piece) + 1;
                foreach (var c in tt.Clauses)
                {
                    foreach (var (p, _) in c.Any) Assert.Contains(p, pieces);
                    Assert.True(c.Met(have), $"{k.Id} L{tier}: the plans up to here do not build '{c}'");
                }
            }
        }
        Assert.Equal(33, Ind.Stages.Sum(k => k.TierCount - 1));
    }

    [Theory]
    [InlineData("stage_iron_mine", 2, 105_000)] [InlineData("stage_iron_mine", 3, 140_000)] [InlineData("stage_iron_crusher", 2, 30_000)] [InlineData("stage_iron_crusher", 3, 183_000)]
    [InlineData("stage_iron_yard", 2, 7_500)] [InlineData("stage_iron_yard", 3, 18_000)] [InlineData("stage_iron_smeltery", 2, 60_000)] [InlineData("stage_iron_smeltery", 3, 199_000)]
    [InlineData("stage_iron_packing", 2, 7_500)] [InlineData("stage_iron_packing", 3, 18_000)] [InlineData("stage_clay_pit", 2, 4_500)] [InlineData("stage_clay_pit", 3, 32_000)]
    [InlineData("stage_clay_yard", 2, 22_000)] [InlineData("stage_clay_yard", 3, 30_000)] [InlineData("stage_clay_moulding", 2, 9_000)] [InlineData("stage_clay_moulding", 3, 12_000)]
    [InlineData("stage_clay_drying", 2, 7_500)] [InlineData("stage_clay_drying", 3, 10_000)] [InlineData("stage_clay_kiln", 2, 15_000)] [InlineData("stage_clay_kiln", 3, 20_000)]
    [InlineData("stage_clay_packing", 2, 4_500)] [InlineData("stage_clay_packing", 3, 18_000)] [InlineData("stage_wood_felling", 2, 1_500)] [InlineData("stage_wood_felling", 3, 2_000)]
    [InlineData("stage_wood_hauling", 2, 3_000)] [InlineData("stage_wood_hauling", 3, 4_000)] [InlineData("stage_wood_yard", 2, 15_000)] [InlineData("stage_wood_yard", 3, 20_000)]
    [InlineData("stage_wood_sawmill", 2, 50_000)] [InlineData("stage_wood_sawmill", 3, 131_000)] [InlineData("stage_wood_packing", 2, 1_500)] [InlineData("stage_wood_shipping", 2, 1_500)]
    [InlineData("stage_wood_shipping", 3, 18_000)]
    public void IN27_UpgradeBills_AreWithin20PercentOfTheTableOf5_3a(string stage, int tier, long table)
    {
        var t = new Kit("iron");
        long bill = t.World.PlanBill(Ind.Stage(stage)!.Tier(tier).Plan);
        Assert.InRange(bill, table * 80 / 100, table * 120 / 100);
    }

    [Fact]
    public void Content_TheSpecsLaterTiersCarryTheirOwnRules()
    {
        var smelt = Ind.Stage("stage_iron_smeltery")!;
        Assert.Equal(720_000, smelt.Tier(3).CapacityUnpoweredG);                                         // 18 units of 40 kg
        Assert.Equal(200_000, smelt.Tier(2).CapacityUnpoweredG);                                         // an unpowered tier falls to the one below (L1 200 kg) by default
        Assert.Equal(800, smelt.Tier(2).BarPm);
        Assert.Equal(1000, Ind.Stage("stage_iron_mine")!.Tier(1).AccidentPpm); Assert.Equal(500, Ind.Stage("stage_iron_mine")!.Tier(3).AccidentPpm);
        Assert.Equal(6, Ind.Stage("stage_clay_drying")!.Tier(1).DryDays); Assert.Equal(0, Ind.Stage("stage_clay_drying")!.Tier(1).DryWinterPm); Assert.Equal(500, Ind.Stage("stage_clay_drying")!.Tier(2).DryWinterPm);
        Assert.Equal(("dry_cask", 100_000), (Db.All<StageKindDef>().Single(d => d.Id == "stage_iron_packing").Container, Ind.Stage("stage_iron_packing")!.ContainerG));
        Assert.True(Ind.Stage("stage_iron_mine")!.Tier(1).Floods); Assert.False(Ind.Stage("stage_iron_mine")!.Tier(3).Floods);
    }
}
