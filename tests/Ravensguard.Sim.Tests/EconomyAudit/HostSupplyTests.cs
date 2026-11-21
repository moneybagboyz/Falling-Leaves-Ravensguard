using Ravensguard.Sim.Core;
using Ravensguard.Sim.Diagnostics;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.People;
using Ravensguard.Sim.Save;
using Ravensguard.Sim.WarSupply;
using Xunit;

namespace Ravensguard.Sim.Tests.EconomyAudit;

/// <summary>The interface's first cut (army-economy 3): the pure needs, muster and release, the blockade, pay order and arrears, the section's round trip.</summary>
public class HostSupplyTests
{
    private static readonly Roster Army = new([(TroopRole.Levy, 1040), (TroopRole.Spearman, 300), (TroopRole.Archer, 160), (TroopRole.ManAtArms, 60), (TroopRole.Knight, 40), (TroopRole.Mercenary, 400)]);

    /// <summary>AE-1's numbers: 2,000 men, 100 followers, 300 riding horses on summer pasture.</summary>
    [Fact]
    public void Needs_2000Men_100Followers_300Horses_OnSummerPasture_MatchTheRules()
    {
        using var run = AuditWorld.Create(links: false);
        var h = AuditWorld.Host(run, 1, Army, 0, followers: 100, animals: new AnimalCounts(300, 0, 0, 0));
        var hs = run.Supply; var (start, _) = AuditWorld.Stops(run);
        hs.Update(h, hs.PlaceAt(start, pasture: true), HostMode.Camp);
        int summer = 60 * 3 + 20, spring = 60 * 3 + 2, winter = 60 * 3 + 50;     // days of year 4: summer 16-30, spring 1-15, winter 46-60
        var n = hs.Needs(h, summer);
        Assert.Equal(2_080_000, n.StapleG); Assert.Equal(1_200_000, n.FodderHayG); Assert.Equal(750_000, n.FodderOatsG); Assert.Equal(3_150_000, n.FirewoodG);
        Assert.Equal(2_400_000, hs.Needs(h, spring).FodderHayG);                // hay is halved only on summer and autumn pasture
        Assert.Equal(6_300_000, hs.Needs(h, winter).FirewoodG);                  // firewood doubles in winter
        // pay: levies free the first 10 service days; then 150. 300 x 250 + 160 x 250 + 60 x 750 + 40 x 1,500 + 400 x 375
        long rest = 300 * 250 + 160 * 250 + 60 * 750 + 40 * 1500 + 400 * 375;
        Assert.Equal(rest, n.PayMites);
        hs.Account(h).ServiceDays = 10;
        Assert.Equal(rest + 1040 * 150, hs.Needs(h, summer).PayMites);
    }

    [Fact]
    public void Wages_FollowTheOverviewTable_AndMercenariesAreOneAndAHalfTimes()
    {
        Assert.Equal(1.5, SupplyConfig.Wage(TroopRole.Mercenary) / (double)SupplyConfig.Wage(TroopRole.Spearman));
        Assert.Equal([150, 250, 250, 300, 500, 750, 1500, 400, 250, 300, 375, 350, 350], Enum.GetValues<TroopRole>().Select(r => SupplyConfig.Wage(r)).ToArray());
        Assert.Equal(Roster.Roles, SupplyConfig.PayOrder.Distinct().Count());
        Assert.Equal(TroopRole.Mercenary, SupplyConfig.PayOrder[0]);
    }

    /// <summary>A chest that cannot pay all pays in the overview's order, the rest is arrears by role, the unpaid days run, and the arrears are paid when the coin comes.</summary>
    [Fact]
    public void Pay_ShortChest_PaysMercenariesFirst_RestInArrears_ThenArrearsWhenFunded()
    {
        using var run = AuditWorld.Create(links: false);
        var men = new Roster([(TroopRole.Spearman, 100), (TroopRole.Mercenary, 100), (TroopRole.Knight, 10)]);
        var h = AuditWorld.Host(run, 2, men, 0);
        var hs = run.Supply; var (start, _) = AuditWorld.Stops(run); hs.Update(h, hs.PlaceAt(start), HostMode.Camp);
        var led = run.Econ.Ledger; var chest = hs.ChestOf(h);
        hs.Update(h, new HostPlace(start, -1_000_000, -1_000_000), HostMode.March);      // far from every book: the chest goes to pay alone
        long mercs = 100 * 375, knights = 10 * 1500, spears = 100 * 250;
        led.Mint(chest, new Coin(mercs + 5000), SourceReason.Seed);              // the mercenaries and 5,000 of the knights' pay
        int day = run.NextSlotDay;
        run.BeforeSlot(day);
        var r = hs.Supply(h, day);
        Assert.Equal(mercs, r.Paid[(int)TroopRole.Mercenary]); Assert.Equal(5000, r.Paid[(int)TroopRole.Knight]); Assert.Equal(0, r.Paid[(int)TroopRole.Spearman]);
        Assert.Equal(knights - 5000, r.Arrears[(int)TroopRole.Knight]); Assert.Equal(spears, r.Arrears[(int)TroopRole.Spearman]);
        Assert.Equal(1, r.UnpaidDays[(int)TroopRole.Knight]); Assert.Equal(1, r.UnpaidDays[(int)TroopRole.Spearman]); Assert.Equal(0, r.UnpaidDays[(int)TroopRole.Mercenary]);
        Assert.Equal(0, led.Balance(chest).Mites);
        Assert.True(run.CoinConserved);
        led.Mint(chest, new Coin(1_000_000), SourceReason.Seed);
        var r2 = hs.Supply(h, day + 1);                                           // today's wages in full, then the arrears with what is left
        Assert.Equal(0, hs.Account(h).Arrears.Sum()); Assert.All(r2.UnpaidDays, d => Assert.Equal(0, d));
        Assert.Equal(mercs + knights + spears + (knights - 5000) + spears, r2.PaidTotal);
        Assert.True(run.CoinConserved && run.MassOk);
    }

    /// <summary>Muster marks real persons (men 16 to 45, one a household, none on a staff), their household's labour leaves the farm planner's hours, release and desert bring them home; the roster follows.</summary>
    [Fact]
    public void Muster_MarksPersons_TheirFarmLabourLeaves_ReleaseAndDesertSettle()
    {
        using var run = AuditWorld.Create(links: false);
        var h = AuditWorld.Host(run, 3, new Roster(), 1000);
        var hs = run.Supply; var pt = run.State.Population.People; var hh = run.State.Population.Households; var region = run.State.Population.Region;
        int fief = Enumerable.Range(0, region.Fiefs.Count).First(f => Enumerable.Range(0, hh.Count).Count(i => hh.MemberCount[i] > 0 && region.Settlements[hh.Settlement[i]].FiefIndex == f) >= 40);
        var m = hs.Muster(h, fief, 25);
        Assert.Equal(25, m.Persons.Count); Assert.Equal(25, hs.Account(h).Men[(int)TroopRole.Levy]);
        Assert.Equal(25, m.Persons.Select(p => pt.Household[p]).Distinct().Count());                       // one man a household
        int day = (int)(run.HourDone / 24);
        Assert.All(m.Persons, p =>
        {
            Assert.Equal(Sex.Male, pt.Sex[p]); Assert.InRange(pt.AgeYears(p, day), 16, 45); Assert.Equal(PersonStatus.Alive, pt.Status[p]);
            Assert.True(hs.IsOnCampaign(p)); Assert.True(run.Economy.Farming.IsEmployedElsewhere!(p));      // his household has lost his labour
            Assert.Equal(fief, region.Settlements[hh.Settlement[pt.Household[p]]].FiefIndex);
        });
        Assert.True(m.Persons.Select(p => hh.Settlement[pt.Household[p]]).Distinct().Count() < region.Settlements.Count(st => st.FiefIndex == fief));      // P7-EA3d: drawn by village, not spread over every settlement of the fief (LevyByVillageTests has the order)
        var again = hs.Muster(h, fief, 25);                                                                  // nobody is drawn twice
        Assert.Empty(again.Persons.Intersect(m.Persons));
        hs.Release(h, m.Persons.Take(5).ToList());
        Assert.False(hs.IsOnCampaign(m.Persons[0])); Assert.False(run.Economy.Farming.IsEmployedElsewhere!(m.Persons[0]));
        Assert.Equal(45, hs.Account(h).Men[(int)TroopRole.Levy]);
        hs.Account(h).Arrears[(int)TroopRole.Levy] = 4500;
        hs.Desert(h, m.Persons.Skip(5).Take(9).ToList());
        Assert.Equal(36, hs.Account(h).Men[(int)TroopRole.Levy]); Assert.Equal(3600, hs.Account(h).Arrears[(int)TroopRole.Levy]);     // 9 of 45 forgiven their share
        hs.Disband(h);
        Assert.Empty(hs.Book.OnCampaign);
        Assert.False(run.Economy.Farming.IsEmployedElsewhere!(again.Persons[0]));
    }

    [Fact]
    public void Blockade_ClosesTheBook_AndDaysOfFoodAreTheLarder()
    {
        using var run = AuditWorld.Create(links: false);
        var h = AuditWorld.Host(run, 4, new Roster([(TroopRole.Spearman, 10)]), 1000);
        var hs = run.Supply; var (town, _) = AuditWorld.Stops(run);
        int days = hs.DaysOfFood(town); Assert.InRange(days, 5, 400);
        var book = run.Economy.Markets.BookForSettlement(town);
        Assert.False(book.Blockaded);
        hs.SiegeBlockade(town, true, h);
        Assert.True(book.Blockaded); Assert.True(hs.IsBlockaded(town));
        hs.Update(h, hs.PlaceAt(town), HostMode.Siege);
        int day = run.NextSlotDay; run.BeforeSlot(day);
        long stock0 = book.BookStore.Qty("wheat");
        hs.Account(h).BuyRadiusM = 1;                                             // only the besieged town's own book is in reach: it sells nothing to the besieger
        var r = hs.Supply(h, day);
        Assert.Equal(0, r.DeliveredFrom(SupplySource.Market)); Assert.Equal(stock0, book.BookStore.Qty("wheat"));
        Assert.True(run.CoinConserved && run.MassOk);
        hs.SiegeBlockade(town, false, h);
        Assert.False(book.Blockaded);
    }

    [Fact]
    public void Section_RoundTripsThroughTheCodec_AndTheHashSeesEveryField()
    {
        using var run = AuditWorld.Create(links: false);
        var h = AuditWorld.Host(run, 5, new Roster([(TroopRole.Spearman, 10), (TroopRole.Mercenary, 7)]), 50, followers: 3, animals: new AnimalCounts(4, 1, 2, 3), outside: 7);
        var hs = run.Supply; var (town, _) = AuditWorld.Stops(run);
        hs.Muster(h, 0, 3); hs.SiegeBlockade(town, true, h);
        var a = hs.Account(h); a.Arrears[3] = 99; a.HungerDays[2] = 4; a.UnpaidDays[1] = 2; a.Totals[7] = 12345;
        var back = SaveCodec.Deserialize<WarSupplySave>(SaveCodec.Serialize(WarSupplySave.Capture(hs.Book))).Restore();
        Assert.Equal(hs.Book.Hash(), back.Hash());
        a.Arrears[3] = 100;
        Assert.NotEqual(hs.Book.Hash(), back.Hash());
        Assert.True(new WarSupplyBook().IsEmpty); Assert.False(hs.Book.IsEmpty);
    }

    /// <summary>A game that never had a host writes no section and has no service in the bag: its bytes are the Phase 6 ones.</summary>
    [Fact]
    public void AWorldWithoutAHost_HasNoWarSupplySection()
    {
        var b = Tests.Economy.EconFixture.Shared;
        var st = Ravensguard.Sim.Worlds.NewGameWorld.Create(b.Content, b.Continent, b.History, b.Cs, b.Hs, b.Rs, b.CellX, b.CellY, AuditWorld.FreshPop(), null, null);
        Assert.Null(st.WarSupply);
        Assert.DoesNotContain(WarSupplySave.SectionName, WorldSave.Capture(st, "h", "0.1").Sections.Keys);
        Assert.Null(st.HomeRecords!.Sim.Get<HostSupply>());
    }
}
