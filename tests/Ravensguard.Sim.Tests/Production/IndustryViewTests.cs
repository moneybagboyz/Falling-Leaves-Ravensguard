using Ravensguard.Content;
using Ravensguard.Sim.Construction;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Goods;
using Ravensguard.Sim.People;
using Ravensguard.Sim.Production;
using Ravensguard.Sim.Production.Industry;
using Ravensguard.Sim.Tests.Economy;

namespace Ravensguard.Sim.Tests.Production;

/// <summary>P7-IN3 (industries 6, the Industry tab): the view model built at a barrier. IN-9 to IN-11 as views: the bottleneck named with its reason, the balance sheet equal to the ledger's transfers of the day, the upgrade row equal to IN2's plan, the unit.</summary>
public class IndustryViewTests
{
    private static GameTime At(long day, int hour = 9) => GameTime.FromGameHours(day * 24 + hour);
    private static IndustryContent Ind => ProdKit.Content.Industries;
    private static ContentDatabase Db => EconFixture.Content;

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
        public void Give(int stage, string good, long q) => B(stage).Store.Create(K.Cat.IdOf(good), q, CreateReason.Seed);
        public void Day(long day = 0) => World.Day(W, (int)day, At(day));
        public WorksView View(int day = 1) => IndustryViews.Build(World, W.Owner, day).Works.Single();
    }

    [Fact]
    public void Silver_IsOneDecimal_AHundredMitesTheSilverPiece()
    {
        Assert.Equal("67.9 sp", IndustryViews.Silver(6790));
        Assert.Equal("8.5 sp", IndustryViews.Silver(852));
        Assert.Equal("0.0 sp", IndustryViews.Silver(0));
        Assert.Equal("-21.0 sp", IndustryViews.Silver(-2100));
        Assert.Equal("1,234.6 sp", IndustryViews.Silver(123_456));
    }

    [Fact]
    public void Bottleneck_NamesTheStoppedStage_AndItsReason()
    {
        var t = new Kit("iron");
        t.Crew(0, 2, 1); t.Crew(1, 3); t.Crew(2, 1);
        t.Crew(3, 1); t.Give(3, "iron_ore", 200_000); t.Give(3, "charcoal", 300_000);              // one hand at the smeltery: its first tier asks for more
        t.Day(0); t.Day(1);
        var v = t.View();
        Assert.Equal(BottleneckKind.Stopped, v.Bottleneck);
        Assert.Equal(3, v.BottleneckStage);
        Assert.Equal(StageStop.UnderCrewed, v.Stages[3].Stop);
        Assert.Equal(1_000_000, v.Stages[0].MadeG);                                                // the mine made its 1,000 kg
        Assert.True(v.Warning);
    }

    [Fact]
    public void Bottleneck_AShortageNamesTheGood()
    {
        var t = new Kit("iron");
        t.Crew(0, 2, 1); t.Crew(1, 3); t.Crew(2, 1);
        t.Crew(3, 2, 3); t.Give(3, "iron_ore", 200_000);                                            // crewed, ore, but no charcoal and no market to buy it at
        t.Day(0); t.Day(1);
        var v = t.View();
        Assert.Equal(BottleneckKind.Stopped, v.Bottleneck);
        Assert.Equal(3, v.BottleneckStage);
        Assert.Contains(v.Stages[3].Stop, new[] { StageStop.NoFuel, StageStop.NoInput });
        Assert.False(string.IsNullOrEmpty(v.Stages[3].StopGood));
        Assert.Contains(v.Stages[3].Inputs, i => i.Fuel && i.StockG == 0 && i.CoverDaysTenths == 0);
    }

    [Fact]
    public void Bottleneck_ALinkThatCannotCarryTheDay_IsHauling()
    {
        var t = new Kit("iron");
        t.World.SetLink(t.W.Id, 1, 600, 1, 0);
        t.World.SliderPct = () => 200;                                                              // the mine makes 2,000 kg, one cart moves 1,000
        t.Crew(0, 2, 1);
        t.Day(0); t.Day(1);
        var v = t.View();
        Assert.Equal(BottleneckKind.Hauling, v.Bottleneck);
        Assert.Equal(0, v.BottleneckLink);
        Assert.Equal(1, v.BottleneckStage);
        Assert.Equal(1_000_000, v.Links[0].CapG);
        Assert.True(v.Links[0].MovedG <= v.Links[0].CapG);
    }

    [Fact]
    public void BalanceSheet_WagesEqualTheLedgersTransfersOfTheDay_AndTheWeekIsTheSum()
    {
        var t = new Kit("iron");
        t.Crew(0, 2, 1); t.Crew(1, 3);
        var led = t.K.Econ.Ledger;
        t.Day(0);
        long w0 = led.Flow(TransferReason.Wage).Mites;
        t.Day(1);
        long w1 = led.Flow(TransferReason.Wage).Mites - w0;
        var v = t.View();
        Assert.Equal(1, v.LastDay);
        Assert.Equal(w1, v.Day.Wages);
        Assert.True(v.Day.Wages > 0);
        Assert.Equal(w0 + w1, v.Week.Wages);                                                        // two working days inside the 6-day week
        Assert.Equal(v.Day.Income - v.Day.Wages - v.Day.Inputs - v.Day.Upkeep - v.Day.Royalties, v.Day.Net);
        Assert.Equal(v.Stages.Count, v.StageDay.Count);
        Assert.Equal(v.Day.Wages, v.StageDay.Sum(r => r.Wages));
        Assert.Equal(v.Week.Net, v.StageWeek.Sum(r => r.Net));
        Assert.Equal(t.W.Stages.Sum(s => s.Net(1, 1)), v.NetDay);
    }

    [Fact]
    public void UpgradeRow_IsTheNextTiersPlan_ItsBill_ItsMissingPieces_AndItsCrewBand()
    {
        var t = new Kit("iron");
        t.World.SetSite(t.W.Stages[0].Business, 0, 0, 30, 30);
        t.Crew(0, 2, 1);
        t.Day(0);
        var v = t.View(0);
        var st = v.Stages[0]; var next = t.World.NextUpgrade(t.W.Stages[0])!.Value;
        Assert.NotNull(st.Upgrade);
        Assert.Equal(next.Tier, st.Upgrade!.Tier); Assert.Equal(next.Plan, st.Upgrade.Plan);
        Assert.Equal(t.World.PlanBill(next.Plan), st.Upgrade.BillMites);
        Assert.Equal(t.World.Missing(t.W.Stages[0], next.Tier), st.Upgrade.Missing);
        var band = Ind.Stage("stage_iron_mine")!.Tier(next.Tier);
        Assert.Equal(band.CrewMin, st.Upgrade.CrewMin); Assert.Equal(band.CrewMax, st.Upgrade.CrewMax);
        Assert.Equal(Math.Max(0, band.CrewMin - st.Have), st.Upgrade.HireMore);
        Assert.False(st.Upgrade.InHand);
        Assert.Equal(1, st.Tier); Assert.Equal(Ind.Stage("stage_iron_mine")!.TierCount, st.TierCount);
        t.World.SetTier(t.W.Stages[0].Business, st.TierCount);
        Assert.Null(t.View(0).Stages[0].Upgrade);                                                   // the top tier has no next plan
    }

    [Fact]
    public void TheView_OnlyReads_AndShowsOnlyTheOwnersWorks()
    {
        var t = new Kit("iron");
        t.Crew(0, 2, 1); t.Day(0); t.Day(1);
        ulong h0 = t.Book.Hash(); long seq = t.K.Econ.Ledger.Sequence;
        var v = IndustryViews.Build(t.World, t.W.Owner, 1);
        Assert.Single(v.Works);
        Assert.Equal(h0, t.Book.Hash()); Assert.Equal(seq, t.K.Econ.Ledger.Sequence);
        Assert.True(IndustryViews.Build(t.World, t.W.Owner + 1, 1).IsEmpty);
        Assert.Equal(t.W.Stages.Count, v.Works[0].Stages.Count);
        Assert.All(v.Works[0].Stages, s => Assert.True(s.CrewMin <= s.CrewTarget && s.CrewTarget <= s.CrewMax));
    }
}
