using Ravensguard.Sim.Abstract;
using Ravensguard.Sim.Caravans;
using Ravensguard.Sim.Continental;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Economy.Markets;
using Ravensguard.Sim.People.Vital;
using Ravensguard.Sim.Save;
using Ravensguard.Sim.Tests.RegionsB;
using Ravensguard.Sim.Tests.Seamless;
using Ravensguard.Sim.WorldGen.Region;
using Ravensguard.Sim.Worlds;
using Xunit;
using Xunit.Abstractions;

namespace Ravensguard.Sim.Tests.CaravanTests;

/// <summary>Continent tick v1 and caravans (continent-tick and caravans-and-logistics specs).</summary>
public class ContinentAndCaravanTests(ITestOutputHelper output)
{
    private static ContinentState Fresh(int home = 120)
    {
        var w = SeamWorld.Get(1234);
        return ContinentState.Create(1234, w.History, home, 25_000, 25_000L * 5400);
    }

    private static GameTime Spring(int year, int season) => GameTime.FromCalendar(year, (Season)season, 2);

    private static void Run(ContinentState st, int seasons, IReadOnlyList<ResidentRegion>? res = null)
    {
        for (int i = 0; i < seasons; i++) ContinentTick.Run(st, res ?? [], Spring(100 + i / 4, i % 4));
    }

    [Fact]
    public void SummaryOnly_Ticks_ConserveCoin_AndAreDeterministic()
    {
        var a = Fresh(); var b = Fresh();
        Run(a, 1); Run(b, 1);
        long coin0 = a.CoinInSummaries();
        Run(a, 40); Run(b, 40);
        output.WriteLine($"coin {coin0:N0} -> {a.CoinInSummaries():N0}; pop {a.Summaries.Sum(s => (long)s.Pop):N0}; edges {a.Edges.Length}");
        Assert.Equal(coin0, a.CoinInSummaries());                         // trade, migration and tolls move coin between regions, never make or lose it
        Assert.Equal(a.ComputeHash(), b.ComputeHash());
        Assert.All(a.Summaries, s => { Assert.True(s.CoinTotal >= 0); Assert.True(s.StockG.All(x => x >= 0)); });
        Assert.All(a.Summaries.Where(s => s.Pop > 0), s => Assert.InRange(s.PriceIdx[0], ContinentRules.PriceMinPermille, ContinentRules.PriceMaxPermille));
    }

    [Fact]
    public void ResidentSet_DoesNotChangeTheSummariesOfTheOthers_UntilActivation()
    {
        // G-C9: a region that nobody visits evolves the same whatever else is resident; here the empty and the paged world agree on a far region
        var a = Fresh(); var b = Fresh();
        Run(a, 12); Run(b, 12);
        int far = Enumerable.Range(0, 256).First(i => a.Summaries[i].Pop > 0 && i != 120 && !a.EdgesOf[120].Select(e => a.Edges[e].Other(120)).Contains(i) && a.EdgesOf[i].Length == 0);
        Assert.Equal(a.Summaries[far].PopRemainder, b.Summaries[far].PopRemainder);
        Assert.Equal(a.Summaries[far].StockG, b.Summaries[far].StockG);
    }

    [Fact]
    public void NewsOfAFamine_ReachesNeighbours_AsATypedEntry()
    {
        var rig = new RegionRig(); var home = rig.Set.HomeWorld!;
        var st = rig.Sv.AbstractContinent!;
        int nb = st.EdgesOf[rig.Home.Id].Select(e => st.Edges[e].Other(rig.Home.Id)).First(n => st.Summaries[n].Pop > 0);
        st.Summaries[nb].FamineDays = 45; st.Summaries[nb].StockG[0] = 0;
        ContinentTick.Run(st, [new ResidentRegion(rig.Home.Id, home.Economy)], GameTime.FromGameHours(home.HourDone + 24 * 20));
        var e = home.Chronicle.ByType("word_reached").FirstOrDefault();
        Assert.NotNull(e);
        output.WriteLine(e!.Data["text"]);
        Assert.Contains("famine", e.Data["about"]);
    }

    [Fact]
    public void Migrants_AndTheirCoin_CrossThroughTheLink_WithoutLoss()
    {
        var rig = new RegionRig(); var home = rig.Set.HomeWorld!; var st = rig.Sv.AbstractContinent!; var sim = home.Sim;
        long ledger0 = sim.Economy.Ledger.Audit().InPurses.Mites, cont0 = st.TotalCoin();
        int hh = Enumerable.Range(0, sim.Households.Count).First(h => sim.Households.MemberCount[h] > 0 && sim.Economy.Ledger.Balance(sim.Economy.HouseholdPurse[h]).Mites > 0);
        long alive0 = sim.Vital.Alive;
        Demography.DepartHousehold(sim, hh, GameTime.FromGameHours(home.HourDone), new RegionOutbox(sim.Ids));
        var rl = st.Link(rig.Home.Id);
        Assert.True(rl.EmigrantPersons > 0 && sim.Vital.Alive < alive0);
        Assert.True(sim.Economy.Ledger.Audit().IsConserved);
        long total = sim.Economy.Ledger.Audit().InPurses.Mites + st.TotalCoin();
        Assert.Equal(ledger0 + cont0, total);                                   // the household's coin left the ledger and is on the continent's books
        ContinentTick.Run(st, [new ResidentRegion(rig.Home.Id, home.Economy)], GameTime.FromGameHours(home.HourDone + 24 * 20));
        Assert.Equal(0, rl.EmigrantPersons);
        Assert.Equal(ledger0 + cont0, sim.Economy.Ledger.Audit().InPurses.Mites + st.TotalCoin());   // settled into a neighbour's summary
    }

    [Fact]
    public void TheRegionTransfer_MovesCoinAndMass_WithoutLoss()
    {
        var rig = new RegionRig(); var a = rig.Set.HomeWorld!;
        var b = rig.FirstPopulated(rig.Home.Neighbours());
        var tot0 = a.Sim.Economy.Ledger.Audit().InPurses.Mites + b.Sim.Economy.Ledger.Audit().InPurses.Mites;
        int hh = Enumerable.Range(0, a.Sim.Households.Count).First(h => a.Sim.Households.MemberCount[h] > 0);
        var t = GameTime.FromGameHours(Math.Max(a.HourDone, b.HourDone));
        Worlds_Move(a, b, hh, t);
        Assert.True(a.Sim.Economy.Ledger.Audit().IsConserved && b.Sim.Economy.Ledger.Audit().IsConserved);
        Assert.Equal(tot0, a.Sim.Economy.Ledger.Audit().InPurses.Mites + b.Sim.Economy.Ledger.Audit().InPurses.Mites);   // a transfer between regions is not a sink
    }

    private static void Worlds_Move(RegionWorld a, RegionWorld b, int hh, GameTime t) => RegionTransfer.MoveHousehold(a, b, hh, -1, t);

    // ------------------------------------------------------------------ caravans

    private static Caravan Synthetic(long dist, long anchor, long prev, int vehicles = 2)
    {
        var path = CaravanPath.FromPoints([(1000, 1000, RoadClass.Road), (4000, 1000, RoadClass.Road), (7000, 1000, RoadClass.Road), (7000, 3000, RoadClass.Road)],
            [new TollMark(3_000_000, TollKind.Bridge)]);
        return new Caravan
        {
            Id = EntityId.Make(IdKind.Other, 0x400_0001), Home = 1, Region = 1, Path = path, State = CaravanState.Outbound, Vehicles = Enumerable.Repeat(VehicleKind.Cart, vehicles).ToArray(),
            Crew = [1, 2], Guards = 1, Master = 3, Dist = dist, PrevDist = prev, AnchorSec = anchor,
        };
    }

    [Fact]
    public void CartLegOf8Km_TakesAbout18Hours_AndThePositionFunctionIsContinuous()
    {
        Assert.Equal(125, Vehicles.SpeedMmPerGs(VehicleKind.Cart));
        long hours = 8000L * 1000 / (125 * 3600);
        Assert.InRange(hours, 17, 18);                                           // 17.8 game hours
        var c = Synthetic(2_700_000, 3600 * 6, 2_250_000);                       // G-L11: 2,700 m along a 3-edge route at +6 h
        var p = CaravanMotion.PositionOf(c, 3600 * 6);
        Assert.Equal(1000L * 3700, p.XMm);                                       // still on the first 3 km edge, 2,700 m past the start (x from 1,000 m)
        var mid = CaravanMotion.PositionOf(c, 3600 * 6 - 1800);
        Assert.Equal(2_250_000 + 225_000, CaravanMotion.DistAt(c, 3600 * 6 - 1800));
        Assert.True(mid.XMm < p.XMm);
    }

    [Fact]
    public void Promotion_PlacesTheLeadWhereTheRecordSays_AndDemotionWaitsFiveSeconds()
    {
        var c = Synthetic(2_700_000, 3600 * 6, 2_250_000);
        var bridge = new CaravanBridge();
        long now = 3600 * 6 - 600;
        var lead = CaravanMotion.PositionOf(c, now);
        var live = bridge.Update([c], lead.XMm + 100_000, lead.YMm, now);      // 100 m away: promoted
        Assert.Single(live); Assert.Equal(1, bridge.Promotions);
        Assert.Equal(lead.XMm, live[0].LeadXMm); Assert.Equal(lead.YMm, live[0].LeadYMm);
        var carts = live[0].Parts.Where(p => p.Kind == LivePartKind.Cart).ToList();
        Assert.Equal(2, carts.Count);
        Assert.Equal(lead.XMm, carts[0].XMm);                                   // the lead cart is the record's position, to the millimetre
        Assert.True(live[0].Parts.Any(p => p.Kind == LivePartKind.Draught) && live[0].Parts.Any(p => p.Kind == LivePartKind.Master));
        // 170 m: still live (hysteresis); 250 m for 5 game seconds: demoted
        Assert.Single(bridge.Update([c], lead.XMm + 170_000, lead.YMm, now + 1));
        bridge.Update([c], lead.XMm + 250_000, lead.YMm, now + 2);
        Assert.Single(bridge.Update([c], lead.XMm + 250_000, lead.YMm, now + 50));
        Assert.Empty(bridge.Update([c], lead.XMm + 250_000, lead.YMm, now + 2 + CaravanBridge.DemoteAfterGameSeconds));
        Assert.Equal(1, bridge.Demotions);
    }

    [Fact]
    public void ContinentSection_SavesLoadsAndSavesAgain_ByteForByte_WithACaravanInFlight()
    {
        var rig = new RegionRig(); var st = rig.Sv.AbstractContinent!;
        var c = Synthetic(2_700_000, 3600 * 6, 2_250_000); c.Id = EntityId.Make(IdKind.Other, 0x400_0000 + EntityId.RegionBase(rig.Home.Id) + st.NextCaravanSerial(rig.Home.Id));
        c.Home = c.Region = rig.Home.Id; c.Neighbour = st.EdgesOf[rig.Home.Id].Select(e => st.Edges[e].Other(rig.Home.Id)).First(); c.Used = 1; c.Cargo.Add(new CargoLine { Good = 3, Units = 500, Dest = 2 });
        st.Caravans.Add(c);
        st.Summaries[5].News.Add(new NewsItem(NewsKind.Famine, 5, 100, 12, 1));
        var bytes = SaveCodec.SerializeCompressed(ContinentSave.Capture(st));
        var back = SaveCodec.DeserializeCompressed<ContinentSave>(bytes).Restore(1234, SeamWorld.Get(1234).History);
        Assert.Equal(bytes, SaveCodec.SerializeCompressed(ContinentSave.Capture(back)));
        Assert.Equal(st.ComputeHash(), back.ComputeHash());
    }

    private static readonly Lazy<HeadlessWorld> Hw = new(() => HeadlessWorld.Create(1234), true);

    [Fact]
    public void ATrip_RunsEndToEnd_PaysTolls_IsAmbushedWhenTheRoadIsDangerous_AndCoinAndMassStayExact()
    {
        var hw = HeadlessWorld.Create(1234);
        var ew = hw.Economy; var st = ew.Trade.Link.State; st.DangerBonus = 100;
        long coin0 = ew.World.Economy.Ledger.Audit().InPurses.Mites + st.TotalCoin();
        using var t = hw.NewTicker();
        t.AdvanceDays(45);
        var tr = ew.Trade;
        output.WriteLine($"founded {tr.CaravansFounded}, trips {tr.TripsDone}, started {tr.TripsStarted}, ambushes {tr.Ambushes}, lost {tr.CaravansLost}, tolls {tr.TollsMites}, wages {tr.WagesMites}, loot {tr.LootMites}");
        Assert.True(tr.CaravansFounded > 0 && tr.TripsDone > 0);
        Assert.True(tr.Ambushes > 0, "a road at danger 100 must produce an ambush in 45 days");
        Assert.True(tr.TollsMites > 0);
        Assert.True(tr.WagesMites > 0);
        Assert.True(st.Caravans.ActiveCount() <= CaravanTable.MaxActive);
        var audit = ew.World.Economy.Ledger.Audit();
        Assert.True(audit.IsConserved);
        Assert.True(ew.World.Economy.MassAuditHolds());
        // the world's coin: ledger plus the continent's books (summaries, settlements owed, immigrants on the road) is what it was
        Assert.Equal(coin0, audit.InPurses.Mites + st.TotalCoin());
    }
}
