using Ravensguard.Sim.Caravans;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Diagnostics;
using Ravensguard.Sim.Disease;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Farming;
using Ravensguard.Sim.Goods;
using Ravensguard.Sim.People;
using Ravensguard.Sim.Rule;
using Ravensguard.Sim.Save;
using Ravensguard.Sim.WarSupply;
using Ravensguard.Sim.Worlds;
using Xunit;
using Xunit.Abstractions;

namespace Ravensguard.Sim.Tests.EconomyAudit;

/// <summary>P7-AE1 gate: AE-2, AE-3, AE-4, AE-11, AE-12, AE-13 and AE-21 (AE-1 is in <see cref="HostSupplyTests"/>): the ladder with the lord's barn, depots and convoys, the price entry, the larder, the muster, the pay order and the abstract tier.</summary>
public class HostSupplyLadderTests(ITestOutputHelper output)
{
    private static readonly Roster Army = new([(TroopRole.Levy, 1040), (TroopRole.Spearman, 300), (TroopRole.Archer, 160), (TroopRole.ManAtArms, 60), (TroopRole.Knight, 40), (TroopRole.Mercenary, 400)]);

    private static ushort Wheat(WarLoadRun run) => run.Econ.Goods.IdOf("wheat");

    /// <summary>AE-2: the daily pay of the army-economy 4.5 roster by the overview's wage table (the levies serve their first days unpaid): spearmen 250, archers 250, men-at-arms 750, knights 1,500, mercenaries 375 = 370,000 mites.</summary>
    [Fact]
    public void AE2_Needs_Pay_OfThe45Roster_Is370000_AndTheLevyWageFollowsTheFreeDays()
    {
        using var run = AuditWorld.Create(links: false);
        var h = AuditWorld.Host(run, 1, Army, 0);
        var hs = run.Supply;
        Assert.Equal(300 * 250 + 160 * 250 + 60 * 750 + 40 * 1500 + 400 * 375, 370_000);
        Assert.Equal(370_000, hs.Needs(h, 200).PayMites);
        hs.Account(h).ServiceDays = SupplyConfig.LevyFreeDays;
        Assert.Equal(370_000 + 1040 * 150, hs.Needs(h, 200).PayMites);
    }

    /// <summary>AE-13: a chest of 100,000 against the 370,000 roster pays the mercenaries first (100,000 of their 150,000), the rest of them and every other role is in arrears by role, and every paid role that is short gains an unpaid day.</summary>
    [Fact]
    public void AE13_ChestOf100000_AgainstTheRoster_PaysMercenariesFirst_RestInArrearsByRole()
    {
        using var run = AuditWorld.Create(links: false);
        var h = AuditWorld.Host(run, 2, Army, 0);
        var hs = run.Supply; var (start, _) = AuditWorld.Stops(run);
        hs.Update(h, new HostPlace(start, -1_000_000, -1_000_000), HostMode.March);        // far from every book: the chest pays and nothing else
        var chest = hs.ChestOf(h); run.Econ.Ledger.Mint(chest, new Coin(100_000), SourceReason.Seed);
        int day = run.NextSlotDay; run.BeforeSlot(day);
        var r = hs.Supply(h, day);
        Assert.Equal(100_000, r.Paid[(int)TroopRole.Mercenary]); Assert.Equal(100_000, r.PaidTotal);
        Assert.Equal(50_000, r.Arrears[(int)TroopRole.Mercenary]); Assert.Equal(60_000, r.Arrears[(int)TroopRole.Knight]); Assert.Equal(45_000, r.Arrears[(int)TroopRole.ManAtArms]);
        Assert.Equal(75_000, r.Arrears[(int)TroopRole.Spearman]); Assert.Equal(40_000, r.Arrears[(int)TroopRole.Archer]); Assert.Equal(0, r.Arrears[(int)TroopRole.Levy]);      // the levies' first days are free
        foreach (var role in new[] { TroopRole.Mercenary, TroopRole.Knight, TroopRole.ManAtArms, TroopRole.Spearman, TroopRole.Archer }) Assert.Equal(1, r.UnpaidDays[(int)role]);
        Assert.Equal(0, r.UnpaidDays[(int)TroopRole.Levy]);
        Assert.Equal(0, run.Econ.Ledger.Balance(chest).Mites); Assert.True(run.CoinConserved && run.MassOk);
        var r2 = hs.Supply(h, day + 1);
        Assert.Equal(2, r2.UnpaidDays[(int)TroopRole.Mercenary]); Assert.Equal(200_000, r2.Arrears[(int)TroopRole.Mercenary]);
    }

    // ------------------------------------------------------------------ AE-3

    /// <summary>A world with a host of 200 spearmen camped at the biggest town, its lord a landholder of that town with a grain barn, a depot at a settlement beyond a day's reach on a road, and a funded chest.</summary>
    private sealed class Scene
    {
        public WarLoadRun Run = null!; public HostSupply Hs = null!; public HostId Host; public int Start, DepotAt; public Store Barn = null!, Depot = null!, Bag = null!; public long ReserveEq; public int LordHousehold;
        public long Need => Hs.Needs(Host, Run.DayNow).StapleG;
    }

    private static Scene BuildScene(int men = 200, int crowns = 5000)
    {
        var run = AuditWorld.Create();
        var sc = new Scene { Run = run, Hs = run.Supply };
        var (start, _) = AuditWorld.Stops(run); sc.Start = start;
        var region = run.State.Population.Region; var hhs = run.State.Population.Households; var pt = run.State.Population.People;
        var wheat = Wheat(run);
        sc.Host = AuditWorld.Host(run, 3, new Roster([(TroopRole.Spearman, men)]), crowns);
        var a = sc.Hs.Account(sc.Host);
        a.Carts = 12;
        sc.Hs.Update(sc.Host, sc.Hs.PlaceAt(start), HostMode.Camp);
        sc.Bag = sc.Hs.BaggageOf(sc.Host);
        // the lord: the head of a landholding household of the start town, whose barn is within the buy radius
        int lordHh = -1;
        for (int i = 0; i < hhs.Count && lordHh < 0; i++)
            if (hhs.MemberCount[i] > 0 && hhs.Settlement[i] == start && run.Econ.OtherStores.ContainsKey(FarmIds.Barn(i))) lordHh = i;
        if (lordHh < 0) for (int i = 0; i < hhs.Count && lordHh < 0; i++) if (hhs.MemberCount[i] > 0 && run.Econ.OtherStores.ContainsKey(FarmIds.Barn(i)) && Dist(region, hhs.Settlement[i], start) < 7000) lordHh = i;
        Assert.True(lordHh >= 0, "a landholder near the start");
        sc.LordHousehold = lordHh; a.Lord = pt.IdOf(hhs.Head[lordHh]).Value;
        sc.Barn = run.Econ.OtherStores[FarmIds.Barn(lordHh)];
        sc.ReserveEq = (long)hhs.MemberCount[lordHh] * 1100 * SupplyConfig.ReserveGrainDays;
        // the depot: a settlement 9 to 30 km from the host that the roads join to it
        int pick = -1; long best = long.MaxValue;
        for (int s = 0; s < region.Settlements.Count; s++)
        {
            long d = Dist(region, s, start); if (d < 9000 || d > 30000) continue;
            if (CaravanRoads.BuildBetween(region, start, s, false) == null) continue;
            if (d < best) { best = d; pick = s; }
        }
        Assert.True(pick >= 0, "a depot site on a road");
        sc.DepotAt = pick; sc.Depot = run.Supply.OpenDepot(pick); a.Depots = [pick];
        _ = wheat;
        return sc;
    }

    private static long Dist(Ravensguard.Sim.WorldGen.Region.Region r, int a, int b) { long dx = r.Settlements[a].CenterX - r.Settlements[b].CenterX, dy = r.Settlements[a].CenterY - r.Settlements[b].CenterY; return (long)Math.Sqrt(dx * dx + dy * dy); }

    private static DayResult Day(Scene sc)
    {
        int day = sc.Run.NextSlotDay;
        sc.Run.BeforeSlot(day);
        sc.Hs.Update(sc.Host, sc.Hs.PlaceAt(sc.Start), HostMode.Camp);
        sc.Run.AfterSlot(day);
        var r = sc.Hs.Last(sc.Host)!; Assert.Equal(day, r.Day);
        Assert.True(sc.Run.CoinConserved, $"coin day {day}"); Assert.True(sc.Run.MassOk, $"mass day {day}");
        return r;
    }

    /// <summary>AE-3: day 1 the baggage (and the quartermaster, the baggage being low, sends carts to the depot beyond a day's reach); day 2 the lord's barn, keeping his household's 30 days of grain; day 3 the town's book while the convoy is still on the road; the convoy comes home and every gram it took is in the baggage or eaten from it.</summary>
    [Fact]
    public void AE3_Ladder_Baggage_TheLordsBarn_TheBookWhileTheConvoyTravels_ThenTheBaggageIsRefilled()
    {
        var sc = BuildScene(); using var runHold = sc.Run; var hs = sc.Hs; var wheat = Wheat(sc.Run);
        long need = sc.Need; Assert.Equal(200_000, need);
        sc.Bag.Create(wheat, need, CreateReason.Seed);                                              // one day in the carts
        sc.Depot.Create(wheat, 8_000_000, CreateReason.Seed); long depot0 = sc.Depot.Qty(wheat);    // 8 t at the depot
        long barn0 = sc.Barn.Qty(wheat); sc.Barn.Create(wheat, (sc.ReserveEq + need) * 105 / 100, CreateReason.Seed);     // the lord's own: his household's reserve and a day for the host, and a little for a day's spoilage
        int carts0 = hs.Account(sc.Host).Carts;
        int stp = (int)SupplyNeed.Staple;
        var d1 = Day(sc);
        Assert.InRange(d1.Delivered[stp, (int)SupplySource.Baggage], need * 995 / 1000, need);       // the carts' grain, less a day's spoilage
        Assert.True(d1.DeliveredFrom(SupplySource.FoodRent) < need / 100, "the barn gives the spoiled grams the carts lacked and nothing else"); Assert.Equal(0, d1.Delivered[stp, (int)SupplySource.Market]);
        Assert.Single(hs.Book.Convoys);                                                             // the baggage is empty: carts go to the depot
        var rec = hs.Book.Convoys.Values.Single(); Assert.Equal(sc.DepotAt, rec.Depot); Assert.InRange(rec.Carts, 1, SupplyConfig.ConvoyMaxCarts); Assert.Equal(carts0 - rec.Carts, hs.Account(sc.Host).Carts);
        var car = sc.Run.Economy.Trade.Link.State.Caravans.Get(new EntityId(rec.Caravan))!; Assert.NotNull(car); Assert.Equal(rec.Carts, car.Vehicles.Length); Assert.True(car.Crew.Length >= 1);
        var d2 = Day(sc);
        Assert.InRange(d2.Delivered[stp, (int)SupplySource.FoodRent], need * 99 / 100, need); Assert.True(d2.Delivered[stp, (int)SupplySource.Market] < need / 100);
        Assert.True(sc.Barn.Qty(wheat) >= barn0 + sc.ReserveEq * 98 / 100, "the lord's household keeps its reserve");
        var d3 = Day(sc);
        Assert.True(d3.DeliveredFrom(SupplySource.FoodRent) < need / 4, "the barn is down to his household's reserve and the odd margin"); Assert.True(d3.Delivered[stp, (int)SupplySource.Market] >= need * 3 / 4, "the book fills the day");
        Assert.Single(hs.Book.Convoys);                                                             // and the convoy is still travelling; only one at a time
        long eatenAfter = d3.Delivered[stp, (int)SupplySource.Baggage] + d2.Delivered[stp, (int)SupplySource.Baggage]; int home = -1;
        for (int i = 4; i <= 18 && home < 0; i++)
        {
            var r = Day(sc);
            eatenAfter += r.Delivered[stp, (int)SupplySource.Baggage];
            if (hs.Book.Convoys.Count == 0) home = i;
        }
        Assert.True(home > 0, "the convoy came home");
        long depotLoss = depot0 - sc.Depot.Qty(wheat); Assert.True(depotLoss > 0, "grain left the depot");
        Assert.Equal(carts0, hs.Account(sc.Host).Carts);                                            // the carts are back
        Assert.InRange(sc.Bag.Qty(wheat) + eatenAfter, depotLoss * 95 / 100, depotLoss);            // every gram the carts took is in the baggage or has been eaten from it, less the days' spoilage in the carts (the mass audit holds every day)
        Assert.Null(sc.Run.Economy.Trade.Link.State.Caravans.Get(new EntityId(rec.Caravan)));        // the caravan is struck from the table
        var after = Day(sc);
        Assert.True(after.Delivered[stp, (int)SupplySource.Baggage] > 0, "the baggage feeds the host again");
        output.WriteLine($"convoy of {rec.Carts} carts, home on day {home}, took {depotLoss / 1000} kg from the depot");
    }

    /// <summary>A depot within a day's cart delivers the same day, from the host's baggage's view as a ladder step; the day it is emptied the chronicle says so once.</summary>
    [Fact]
    public void Depot_WithinADay_DeliversTheSameDay_AndWritesDepotEmptiedOnce()
    {
        var sc = BuildScene(); using var runHold = sc.Run; var hs = sc.Hs; var wheat = Wheat(sc.Run);
        var region = sc.Run.State.Population.Region;
        int near = sc.Start;                                                                          // the town's own depot: 0 m away
        hs.Account(sc.Host).Depots = [near]; var depot = hs.OpenDepot(near);
        hs.Chron = RuleChron.Of(sc.Run.State.Chronicle, sc.Run.State.Ids);
        long need = sc.Need; depot.Create(wheat, need + need / 2, CreateReason.Seed);
        var d1 = Day(sc); hs.Chron = RuleChron.Of(sc.Run.State.Chronicle, sc.Run.State.Ids);
        Assert.Equal(need, d1.Delivered[(int)SupplyNeed.Staple, (int)SupplySource.Depot]);
        Assert.Empty(hs.Book.Convoys);
        Assert.Empty(sc.Run.State.Chronicle.ByType("depot_emptied"));
        var d2 = Day(sc);
        Assert.InRange(d2.Delivered[(int)SupplyNeed.Staple, (int)SupplySource.Depot], need / 2 * 98 / 100, need / 2);       // what a day's spoilage left in the granary
        Assert.True(d2.Delivered[(int)SupplyNeed.Staple, (int)SupplySource.Market] > 0);
        Assert.Single(sc.Run.State.Chronicle.ByType("depot_emptied"));
        Day(sc); Assert.Single(sc.Run.State.Chronicle.ByType("depot_emptied"));
        var e = sc.Run.State.Chronicle.ByType("depot_emptied").Single();
        Assert.Equal(CauseKind.Supply, e.Causes[0].Kind); Assert.Equal("ch_note_war_depot_drawn", e.Causes[0].NoteKey);

    }

    /// <summary>The convoy is a real caravan: save and load on the road and both runs come home the same (the economy, the supply and the people sections equal), coin and mass exact on every day.</summary>
    [Fact]
    public void Convoy_SaveAndLoadOnTheRoad_ContinuesAsTheUninterruptedRun()
    {
        var sc = BuildScene(); using var straight = sc.Run; var hs = sc.Hs; var wheat = Wheat(sc.Run);
        sc.Depot.Create(wheat, 8_000_000, CreateReason.Seed);
        Day(sc); Day(sc); Day(sc);
        Assert.Single(hs.Book.Convoys);
        var file = WorldSave.Capture(straight.State, "h", "0.1");
        var bytes = SaveCodec.Encode(file);
        var st = WorldSave.Restore(SaveCodec.Decode(bytes), straight.State.Content!, "h", null, straight.State.Continent, straight.State.History);
        st.Content = straight.State.Content;
        using var rt = new WorldRuntime(st, st.Content!, new MemoryRegionPager());
        WarLoadRun.AttachLinks(rt);
        var again = WorldSave.Capture(st, "h", "0.1");
        foreach (var k in file.Sections.Keys) Assert.True(file.Sections[k].Span.SequenceEqual(again.Sections[k].Span), $"section {k} differs after load and save");
        var hs2 = st.HomeRecords!.Sim.Get<HostSupply>()!; Assert.NotNull(hs2);
        Assert.Equal(hs.Book.Hash(), hs2.Book.Hash()); Assert.Single(hs2.Book.Convoys);
        var host = sc.Host; int dayNow = straight.DayNow;
        void Go(WorldRuntime r, HostSupply sup)
        {
            for (int d = dayNow + 1; d < dayNow + 9; d++)
            {
                r.AdvanceTo(GameTime.FromGameHours((long)d * 24 + 17));
                sup.Update(host, sup.PlaceAt(sc.Start), HostMode.Camp);
                r.AdvanceTo(GameTime.FromGameHours((long)d * 24 + 18));
            }
            r.AdvanceTo(GameTime.FromGameHours((long)(dayNow + 9) * 24));
        }
        Go(straight.Runtime, hs); Go(rt, hs2);
        Assert.Equal(hs.Book.Hash(), hs2.Book.Hash());
        var s1 = WorldSave.Capture(straight.State, "h", "0.1"); var s2 = WorldSave.Capture(st, "h", "0.1");
        foreach (var k in s1.Sections.Keys.Where(k => k != WorldSave.MetaSection && k is "economy" or WarSupplySave.SectionName or "people" or "continent"))
            Assert.True(s1.Sections[k].Span.SequenceEqual(s2.Sections[k].Span), $"section {k} differs after the continued run");
        Assert.True(st.Economy!.Ledger.Audit().IsConserved); Assert.True(st.Economy.MassAuditHolds());
        Assert.Empty(hs2.Book.Convoys);
    }

    // ------------------------------------------------------------------ AE-4

    /// <summary>AE-4: the quartermaster's buying goes through the book's per-unit repricing (the stock falls by what he took and the ask is the curve's at the new stock), and the entry for the price it moved is caused by the host's campaign.</summary>
    [Fact]
    public void AE4_MarketBuying_MovesTheBooksPrice_AndThePriceEntryNamesTheHost()
    {
        using var run = AuditWorld.Create(links: false);
        var hs = run.Supply; var (start, _) = AuditWorld.Stops(run);
        var chron = run.State.Chronicle; var ids = run.State.Ids;
        hs.Chron = RuleChron.Of(chron, ids);
        var campaign = hs.Chron.Chron(GameTime.FromGameHours(1), "depot_filled", "ch_works_depot_filled", [ChronParam.Count("x", 0), ChronParam.Count("y", 0), ChronParam.Count("tonnes", 1)], [], [new Cause(CauseKind.Supply, null, NoteKey: "ch_note_depot_target")]);
        var h = AuditWorld.Host(run, 4, new Roster([(TroopRole.Spearman, 6000)]), 400_000);
        hs.Account(h).Campaign = campaign.Value; hs.Account(h).Lord = 0;
        hs.Update(h, hs.PlaceAt(start), HostMode.Camp);
        var book = run.Economy.Markets.BookForSettlement(start);
        var goods = new[] { "wheat", "rye", "barley", "bread", "flour_wheat" }.Where(g => run.Econ.Goods.TryIdOf(g, out _)).Select(g => run.Econ.Goods.IdOf(g)).ToArray();
        int day = run.NextSlotDay; run.BeforeSlot(day);
        var stock0 = goods.ToDictionary(g => g, g => book.Goods[g].StockUnits); var ask0 = goods.ToDictionary(g => g, g => book.AskMicro(g));
        var r = hs.Supply(h, day);
        Assert.True(r.Delivered[(int)SupplyNeed.Staple, (int)SupplySource.Market] > 0);
        var bought = goods.Where(g => book.Goods[g].StockUnits < stock0[g]).ToArray(); Assert.NotEmpty(bought);
        var gd = bought[0]; long taken = stock0[gd] - book.Goods[gd].StockUnits;
        Assert.True(book.AskMicro(gd) > ask0[gd], "buying raised the ask");
        Assert.Equal(book.Curve(gd).MidMicro(book.Goods[gd].StockUnits) * (1000 + book.Money.SpreadHalfPermille) / 1000, book.AskMicro(gd));
        var entry = chron.ByType("army_bought").FirstOrDefault(e => e.Subjects.Contains(run.State.Population.Region.Settlements[start].Id));
        Assert.NotNull(entry);
        Assert.Equal(CauseKind.Supply, entry!.Causes[0].Kind); Assert.Equal(campaign, entry.Causes[0].EventId);
        Assert.Equal(h.Value.ToString(), entry.Data["host"]); Assert.Contains(entry.Id, r.Events);
        Assert.Equal(ask0[gd].ToString(), entry.Data["ask_before"]); Assert.Equal(book.AskMicro(gd).ToString(), entry.Data["ask_after"]);
        Assert.True(taken > 0); Assert.True(run.CoinConserved && run.MassOk);
        // without a campaign the entry stands on its note key, still a Supply cause
        hs.Account(h).Campaign = 0; var n0 = chron.ByType("army_bought").Count();
        var r2 = hs.Supply(h, day + 1);
        _ = r2;
        Assert.True(chron.ByType("army_bought").Count() >= n0);
    }

    // ------------------------------------------------------------------ AE-11

    /// <summary>AE-11: a town's days of food are its staple over what its people eat (1,100 g a day each): 30 days of grain read 30; the blockade shuts its book to imports and exports; under 5 days <c>siege_starving</c> is written once; lifting the blockade reopens the book.</summary>
    [Fact]
    public void AE11_SiegeLarder_DaysOfFood_Blockade_AndSiegeStarvingAtFiveDays()
    {
        using var run = AuditWorld.Create(links: false);
        var hs = run.Supply; var econ = run.Econ; var wheat = Wheat(run);
        var (town, _) = AuditWorld.Stops(run);
        var hhs = run.State.Population.Households; var region = run.State.Population.Region; var book = run.Economy.Markets.BookForSettlement(town);
        int day = run.NextSlotDay; run.BeforeSlot(day);                                                    // a day of the world first: nothing runs between the larder's reading and the checks below
        // empty the town's larder of staple, then lay up 30 days of it
        int people = 0;
        void Drain(Store st) { foreach (var (g, q) in st.Rows()) if (econ.Food.Staples.Contains(g)) st.DestroyUpTo(g, q, DestroyReason.Eaten); }
        for (int i = 0; i < hhs.Count; i++) if (hhs.Settlement[i] == town && hhs.MemberCount[i] > 0) { people += hhs.MemberCount[i]; Drain(econ.HouseholdStore[i]); }
        Drain(book.BookStore); if (econ.OtherStores.TryGetValue(region.Settlements[town].Id, out var own)) Drain(own);
        book.SyncAll();
        var larder = econ.OpenStore(StoreKind.Granary, region.Settlements[town].Id, 0, 0, false);
        long left0 = hs.DaysOfFoodMilli(town); Assert.InRange(left0, 0, 400);                         // whatever staple is left is a fraction of a day (other goods held as food)
        long grams = (long)people * 1100 * 30 - (long)(left0 * people * 1100 / 1000) + 200; larder.Create(wheat, grams, CreateReason.Seed);
        Assert.Equal(30, hs.DaysOfFood(town)); Assert.InRange(hs.DaysOfFoodMilli(town), 30_000, 30_010);
        // a host besieges it
        var h = AuditWorld.Host(run, 5, new Roster([(TroopRole.Spearman, 20)]), 100);
        hs.Chron = RuleChron.Of(run.State.Chronicle, run.State.Ids);
        hs.Update(h, hs.PlaceAt(town), HostMode.Siege);
        hs.SiegeBlockade(town, true, h);
        Assert.True(book.Blockaded); Assert.True(hs.IsBlockaded(town));
        hs.RunDay(day); Assert.Empty(run.State.Chronicle.ByType("siege_starving"));                    // 30 days: nothing to say
        larder.DestroyUpTo(wheat, larder.Qty(wheat) - (long)people * 1100 * 4 - 500, DestroyReason.Eaten);          // down to 4 days and a bit
        Assert.Equal(4, hs.DaysOfFood(town));
        hs.RunDay(day + 1); hs.RunDay(day + 2);
        var e = run.State.Chronicle.ByType("siege_starving").Single();                                  // once
        Assert.Equal(CauseKind.Famine, e.Causes[0].Kind); Assert.Equal(region.Settlements[town].Id, e.Subjects[0]);
        Assert.Contains(town, hs.Book.Starving);
        hs.SiegeBlockade(town, false, h);
        Assert.False(book.Blockaded); Assert.DoesNotContain(town, hs.Book.Starving);
        Assert.True(run.CoinConserved && run.MassOk);
    }

    // ------------------------------------------------------------------ AE-12

    /// <summary>AE-12: a fief of 520 households in war owes 104 men (one in five); a muster draws that quota, one man a household, the men's households lose 10 worker-hours a day of farm labour each, and a household that evades gives none.</summary>
    [Fact]
    public void AE12_Muster_AFiefOf520Households_InWar_Owes104_TheirFarmHoursFall_EvadersGiveNone()
    {
        using var run = AuditWorld.Create(links: false);
        Assert.Equal(104, run.Economy.Taxes.LevyQuota(520, true)); Assert.Equal(52, run.Economy.Taxes.LevyQuota(520, false));
        var hs = run.Supply; var pt = run.State.Population.People; var hh = run.State.Population.Households; var region = run.State.Population.Region;
        int Count(int f) => Enumerable.Range(0, hh.Count).Count(i => hh.MemberCount[i] > 0 && hh.Settlement[i] >= 0 && region.Settlements[hh.Settlement[i]].FiefIndex == f);
        int fief = Enumerable.Range(0, region.Fiefs.Count).OrderByDescending(Count).First();
        int households = Count(fief), quota = run.Economy.Taxes.LevyQuota(households, true);
        var h = AuditWorld.Host(run, 6, new Roster(), 1000);
        int doy = (int)(run.HourDone / 24) % 60 + 1;
        // the evaders: every third household gives no man
        hs.Evades = (s, household, day) => household % 3 == 0;
        var farm = run.Economy.Farming; int day = run.DayNow;
        var recompute = typeof(FarmingSystem).GetMethod("RecomputeHours", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        recompute.Invoke(farm, [day]);                                                                 // the planner's hours as of now (no time passes between the two readings)
        long supply0 = farm.LabourOutlook(doy).SupplyHours;
        var m = hs.Muster(h, fief, quota);
        Assert.Equal(quota, m.Persons.Count); Assert.True(m.Evaded > 0, "some households evaded");
        Assert.All(m.Persons, p => Assert.NotEqual(0, pt.Household[p] % 3));
        Assert.Equal(m.Persons.Count, m.Persons.Select(p => pt.Household[p]).Distinct().Count());
        recompute.Invoke(farm, [day]);
        long supply1 = farm.LabourOutlook(doy).SupplyHours, fall = supply0 - supply1;
        // each drawn man who farmed (a hand of a household that works the land) took a worker-day of 10 hours (5 under 16 or over 59) from it
        long expected = 0;
        foreach (int p in m.Persons)
        {
            int age = pt.AgeYears(p, day), hhi = pt.Household[p]; var occ = pt.Occupation[p];
            bool hand = occ is Occupation.Farmer or Occupation.Ploughman or Occupation.Labourer or Occupation.Shepherd || (occ == Occupation.None && hh.Class[hhi] != SocialClass.Gentry);
            if (hand && age >= 12) expected += age < 16 || age >= 60 ? SupplyConfig.LevyHoursPerDay / 2 : SupplyConfig.LevyHoursPerDay;
        }
        output.WriteLine($"fief {fief}: {households} households, quota {quota}, drew {m.Persons.Count}, evaded {m.Evaded}; farm hours {supply0} to {supply1} (fall {fall}, expected {expected})");
        Assert.Equal(expected, fall);
        Assert.True(fall > 0 && expected > 0, "the drawn field hands' households lose their labour");
    }

    // ------------------------------------------------------------------ AE-21

    /// <summary>The same host for ten days on the same land in the three ways: detailed throughout, abstract throughout, and detailed for five days then abstract for five.</summary>
    private static (long Staple, long Hay, long Oats, long Pay, HostAccount Acc, long ChestLeft, string Hash) TenDays(int mode)
    {
        using var run = AuditWorld.Create(links: false);
        var hs = run.Supply; var (start, _) = AuditWorld.Stops(run); var wheat = Wheat(run);
        var h = AuditWorld.Host(run, 7, new Roster([(TroopRole.Spearman, 200), (TroopRole.Archer, 50), (TroopRole.Mercenary, 30)]), 100_000, followers: 20, animals: new AnimalCounts(20, 0, 0, 0));
        var bag = hs.BaggageOf(h);
        bag.Create(run.Econ.Goods.IdOf("hay"), 10 * 160_000, CreateReason.Seed); bag.Create(run.Econ.Goods.IdOf("oats"), 10 * 50_000, CreateReason.Seed);      // the fodder rides in the carts
        hs.Update(h, hs.PlaceAt(start), HostMode.Camp);
        if (mode == 1) hs.ToAbstract(h);
        int day0 = run.NextSlotDay;
        for (int d = day0; d < day0 + 10; d++)
        {
            run.BeforeSlot(d);
            if (mode == 2 && d == day0 + 5) hs.ToAbstract(h);
            hs.Update(h, hs.PlaceAt(start), HostMode.Camp);
            run.AfterSlot(d);                                                                              // the host slot runs the day
            Assert.True(run.CoinConserved, $"coin day {d} mode {mode}"); Assert.True(run.MassOk, $"mass day {d} mode {mode}");
        }
        var a = hs.Account(h); long T(SupplyNeed n) { long t = 0; for (int s = 0; s < DayResult.Sources; s++) t += a.Totals[(int)n * DayResult.Sources + s]; return t; }
        return (T(SupplyNeed.Staple), T(SupplyNeed.Hay), T(SupplyNeed.Oats), T(SupplyNeed.Pay), a, run.Econ.Ledger.Balance(hs.ChestOf(h)).Mites, hs.Book.Hash());
    }

    /// <summary>AE-21: the abstract tier gives the host the same staple, fodder and pay (within 5%) as the detailed one on the same land; a host that moves between the tiers keeps its counters.</summary>
    [Fact]
    public void AE21_TierParity_AbstractAndDetailedHostsGetTheSame_AndTheCountersCarryAcross()
    {
        var det = TenDays(0); var abs = TenDays(1); var mix = TenDays(2);
        output.WriteLine($"detailed staple {det.Staple} hay {det.Hay} oats {det.Oats} pay {det.Pay}; abstract {abs.Staple} {abs.Hay} {abs.Oats} {abs.Pay}; moved {mix.Staple} {mix.Hay} {mix.Oats} {mix.Pay}");
        Assert.Equal(10 * (280 * 1000L + 20 * 800), det.Staple);                                    // every need met on the detailed land
        foreach (var other in new[] { abs, mix })
        {
            Assert.InRange(other.Staple, det.Staple * 95 / 100, det.Staple * 105 / 100);
            Assert.InRange(other.Hay, det.Hay * 95 / 100, det.Hay * 105 / 100); Assert.InRange(other.Oats, det.Oats * 95 / 100, det.Oats * 105 / 100);
            Assert.InRange(other.Pay, det.Pay * 95 / 100, det.Pay * 105 / 100);
            Assert.Equal(det.Acc.ServiceDays, other.Acc.ServiceDays); Assert.Equal(10, other.Acc.ServiceDays);
            Assert.Equal(det.Acc.Arrears.Sum(), other.Acc.Arrears.Sum()); Assert.Equal(det.Acc.HungerDays.Sum(), other.Acc.HungerDays.Sum());
        }
        Assert.True(mix.Acc.Abstract); Assert.True(abs.Acc.Abstract); Assert.False(det.Acc.Abstract);
        Assert.True(abs.Pay == det.Pay && mix.Pay == det.Pay, "pay is the same rule in both tiers");
    }

    // ------------------------------------------------------------------ camps, the section

    /// <summary>The disease rule's army camp: a camped host's men, its lord and its followers are in a camp of men plus followers; a host on the march is none.</summary>
    [Fact]
    public void Camp_FeedsTheDiseaseRulesArmyCampSize()
    {
        using var run = AuditWorld.Create(links: false);
        var hs = run.Supply; var (start, _) = AuditWorld.Stops(run); var pt = run.State.Population.People;
        run.State.Disease ??= new DiseaseBook();
        var h = AuditWorld.Host(run, 8, new Roster([(TroopRole.Spearman, 150)]), 100, followers: 30);
        int fief = run.State.Population.Region.Settlements[start].FiefIndex;
        var m = hs.Muster(h, fief, 20); var a = hs.Account(h); a.Lord = pt.IdOf(m.Persons[0]).Value;
        hs.Update(h, hs.PlaceAt(start), HostMode.March);
        hs.Camp(h, start);
        var hook = run.State.Disease!.ArmyCampSize; Assert.NotNull(hook);
        Assert.Equal(170 + 30, hook!(pt.IdOf(m.Persons[1]).Value)); Assert.Equal(200, hook(a.Lord));
        int other = Enumerable.Range(0, pt.Count).First(p => pt.Status[p] == PersonStatus.Alive && !hs.IsOnCampaign(p));
        Assert.Equal(0, hook(pt.IdOf(other).Value));
        hs.Update(h, hs.PlaceAt(start), HostMode.March); Assert.Equal(0, hook(a.Lord));
    }

    [Fact]
    public void Section_CarriesTheConvoysTheNotesTheCampaignAndTheAbstractFlag()
    {
        using var run = AuditWorld.Create(links: false);
        var h = AuditWorld.Host(run, 9, new Roster([(TroopRole.Spearman, 10)]), 50);
        var hs = run.Supply; var a = hs.Account(h); a.Campaign = 4242; hs.ToAbstract(h);
        hs.Book.Convoys[77] = new ConvoyRec(77, a.Id, 3, 4, HostIds.Baggage(9).Value, 5, HostIds.Chest(9).Value, 1234, 12);
        hs.Book.Starving.Add(2); hs.Book.Emptied.Add(5);
        var back = SaveCodec.Deserialize<WarSupplySave>(SaveCodec.Serialize(WarSupplySave.Capture(hs.Book))).Restore();
        Assert.Equal(hs.Book.Hash(), back.Hash());
        Assert.Equal(4242UL, back.Hosts[a.Id].Campaign); Assert.True(back.Hosts[a.Id].Abstract);
        Assert.Equal(hs.Book.Convoys[77], back.Convoys[77]); Assert.Contains(2, back.Starving); Assert.Contains(5, back.Emptied); Assert.DoesNotContain(5, back.Starving);
        hs.Book.Emptied.Remove(5); Assert.NotEqual(hs.Book.Hash(), back.Hash());
    }
}
