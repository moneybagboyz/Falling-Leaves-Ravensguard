using Ravensguard.Sim.Core;
using Ravensguard.Sim.Diagnostics;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Save;
using Ravensguard.Sim.WarSupply;
using Ravensguard.Sim.Worlds;
using Xunit;
using Xunit.Abstractions;

namespace Ravensguard.Sim.Tests.EconomyAudit;

/// <summary>P7-EA1 gate: EA-11 (the load alone, hand-counted) and EA-12 (determinism and the warsupply section's round trip) on a new game of seed 1234.</summary>
public class WarLoadTests(ITestOutputHelper output)
{
    /// <summary>EA-11: the small host (200 men, 30 horses, 6 carts) for 15 game days beside the region's largest town: the needs exact by the rules, every need filled or short to the gram, the pay exact against the hand count (levies unpaid their first 10 days), coin and mass exact every day, nothing from the rest of the ladder.</summary>
    [Fact]
    public void EA11_WarLoadAlone_15Days_NeedsPayCoinAndMassMatchTheHandCount()
    {
        using var run = AuditWorld.Create();
        var (start, gate) = AuditWorld.Stops(run);
        var spec = WarLoadSpec.Small200; Assert.Equal(200, spec.Men);
        var load = new WarLoad(run.Supply, spec, run.Seed, 1);
        int day0 = run.NextSlotDay;
        run.BeforeSlot(day0);
        load.Begin(start, gate);
        var led = run.Econ.Ledger; var chest = run.Supply.ChestOf(load.Host);
        led.Mint(chest, Coin.FromCrowns(100_000), SourceReason.Seed);       // the realm's own chest is small: a faucet of the test so no day goes unpaid
        int levies = load.Mustered; Assert.InRange(levies, 1, 100);
        var hs = run.Supply;
        var acc = hs.Account(load.Host); Assert.Equal(levies, acc.Men[(int)TroopRole.Levy]); Assert.Equal(spec.Men - spec.Levies + levies, acc.Total);
        int men = acc.Total;

        long needStaple = 0, gotStaple = 0, shortStaple = 0, paid = 0;
        for (int d = day0; d < day0 + 15; d++)
        {
            run.BeforeSlot(d);
            load.Before(marching: false);
            run.AfterSlot(d);
            load.After(d);
            var r = load.LastResult!; Assert.Equal(d, r.Day);
            // the needs of the rules: 1 kg of staple, 150 g of relish and 1.5 kg of firewood a man, 8 kg of hay and 2.5 kg of oats a riding horse (spring: no halving, no doubling)
            Assert.Equal(men * 1000L, r.Needed.StapleG); Assert.Equal(men * 150L, r.Needed.RelishG); Assert.Equal(men * 1500L, r.Needed.FirewoodG);
            Assert.Equal(30 * 8000L, r.Needed.FodderHayG); Assert.Equal(30 * 2500L, r.Needed.FodderOatsG);
            for (int n = 0; n < DayResult.Needs - 1; n++)
                Assert.Equal(r.Needed.Of((SupplyNeed)n), r.DeliveredOf((SupplyNeed)n) + r.ShortG[n]);        // filled or short, to the gram
            Assert.Equal(0, r.DeliveredFrom(SupplySource.Forage) + r.DeliveredFrom(SupplySource.Purveyance) + r.DeliveredFrom(SupplySource.Depot) + r.DeliveredFrom(SupplySource.Sutlers) + r.DeliveredFrom(SupplySource.Plunder) + r.DeliveredFrom(SupplySource.FoodRent));
            needStaple += r.Needed.StapleG; gotStaple += r.DeliveredOf(SupplyNeed.Staple); shortStaple += r.ShortG[(int)SupplyNeed.Staple];
            paid += r.PaidTotal;
            Assert.True(run.CoinConserved, $"coin day {d}"); Assert.True(run.MassOk, $"mass day {d}");
        }
        Assert.Equal(15 * men * 1000L, needStaple);
        Assert.Equal(needStaple, gotStaple + shortStaple);
        Assert.Equal(0, shortStaple);                                                                        // the books of the region hold 200 kg a day
        Assert.Equal(0, load.Tally.ManDaysHungry); Assert.Equal(0, load.Tally.Deserted);
        Assert.True(load.Tally.BoughtStapleG > 0 && load.Tally.BoughtMites > 0, "the quartermaster buys at the books' asks");
        // the pay: 40 spearmen and 20 archers at 250, 10 men-at-arms at 750, 30 mercenaries at 375 every day; the levies' 150 from the 11th day
        long daily = 40 * 250 + 20 * 250 + 10 * 750 + 30 * 375, expected = 15 * daily + 5L * levies * 150;
        Assert.Equal(expected, paid); Assert.Equal(expected, load.Tally.PaidMites); Assert.Equal(0, acc.Arrears.Sum());
        // the levy persons' pay went to their own households' purses, the rest to the soldiers' purse
        Assert.Equal(levies, hs.Persons(load.Host).Count);
        long soldiers = led.Balance(hs.SoldiersPurse(load.Host)).Mites; Assert.Equal(15 * daily, soldiers);
        // disband: the soldiers' coin goes home (the mercenaries' share leaves with them), the unspent coin returns, the men are home
        load.End();
        Assert.False(hs.Exists(load.Host) && load.Active); Assert.Empty(hs.Book.OnCampaign); Assert.Empty(hs.Book.Hosts);
        Assert.Equal(0, led.Balance(chest).Mites);
        Assert.True(run.CoinConserved); Assert.True(run.MassOk);
        output.WriteLine($"EA-11: {men} men, {levies} levies, bought {load.Tally.BoughtStapleG / 1000} kg for {load.Tally.BoughtMites} mites, paid {paid}");
    }

    /// <summary>One run of the small host to <paramref name="days"/>, for the determinism check: returns the whole save's hash and the host supply's own.</summary>
    private static (string Save, string Supply) Run(int days)
    {
        using var run = AuditWorld.Create();
        var (start, gate) = AuditWorld.Stops(run);
        var load = new WarLoad(run.Supply, WarLoadSpec.Small200, run.Seed, 1);
        int day0 = run.NextSlotDay;
        run.BeforeSlot(day0); load.Begin(start, gate);
        for (int d = day0; d < day0 + days; d++) { run.BeforeSlot(d); load.Before(marching: d % 3 != 0); run.AfterSlot(d); load.After(d); }
        run.AdvanceToDayEnd(day0 + days - 1);
        return (run.SaveHash(), run.Supply.Book.Hash());
    }

    /// <summary>EA-12 (determinism): two runs of the load give equal save and supply hashes at day 15, and the section is in the save.</summary>
    [Fact]
    public void EA12_WarLoad_TwoRunsAreEqualAtDay15()
    {
        var a = Run(15); var b = Run(15);
        Assert.Equal(a.Save, b.Save); Assert.Equal(a.Supply, b.Supply);
        Assert.NotEqual(new WarSupplyBook().Hash(), a.Supply);
    }

    /// <summary>EA-12 (saves): save, load, save at day 8 is byte-identical in every section (the <c>warsupply</c> one among them), the loaded world is a live one (the host found again through its chest and baggage, the blockade back on the book) and it continues to day 15 to the straight run's hashes.</summary>
    [Fact]
    public void EA12_SaveLoadSave_AtDay8_IsByteIdentical_AndTheLoadedRunContinuesAsTheStraightOne()
    {
        using var straight = AuditWorld.Create();
        var (start, gate) = AuditWorld.Stops(straight);
        var load = new WarLoad(straight.Supply, WarLoadSpec.Small200, straight.Seed, 1);
        int day0 = straight.NextSlotDay;
        straight.BeforeSlot(day0); load.Begin(start, gate);
        straight.Supply.SiegeBlockade(start, true, load.Host);               // a blockade across the save too
        for (int d = day0; d < day0 + 8; d++) { straight.BeforeSlot(d); load.Before(marching: true); straight.AfterSlot(d); load.After(d); }
        straight.AdvanceToDayEnd(day0 + 7);
        var host = load.Host;
        var file = WorldSave.Capture(straight.State, "h", "0.1");
        Assert.Contains(WarSupplySave.SectionName, file.Sections.Keys);
        var bytes = SaveCodec.Encode(file);

        var st = WorldSave.Restore(SaveCodec.Decode(bytes), straight.State.Content!, "h", null, straight.State.Continent, straight.State.History);
        st.Content = straight.State.Content;
        using var rt = new Ravensguard.Sim.Worlds.WorldRuntime(st, st.Content!, new Ravensguard.Sim.Worlds.MemoryRegionPager());
        WarLoadRun.AttachLinks(rt);                                            // the game's links on the loaded world, as the straight run has them
        var again = WorldSave.Capture(st, "h", "0.1");
        Assert.Equal(file.Sections.Keys.Order(StringComparer.Ordinal), again.Sections.Keys.Order(StringComparer.Ordinal));
        foreach (var k in file.Sections.Keys) Assert.True(file.Sections[k].Span.SequenceEqual(again.Sections[k].Span), $"section {k} differs after load and save");
        var hs = st.HomeRecords!.Sim.Get<HostSupply>(); Assert.NotNull(hs);
        Assert.True(hs.Exists(host)); Assert.Equal(straight.Supply.Book.Hash(), hs.Book.Hash());
        Assert.True(hs.IsBlockaded(start)); Assert.True(st.HomeRecords.Economy.Markets.BookForSettlement(start).Blockaded);
        Assert.Equal(straight.Supply.Persons(host), hs.Persons(host));

        // both go on 7 more days with the host standing where it was (the harness's load is the straight run's own object; the continuation is driven through the interface alone)
        void Go(Ravensguard.Sim.Worlds.WorldRuntime r, HostSupply sup)
        {
            for (int d = day0 + 8; d < day0 + 15; d++)
            {
                r.AdvanceTo(GameTime.FromGameHours((long)d * 24 + 17));
                sup.Update(host, sup.PlaceAt(sup.Account(host).PlaceSettlement), HostMode.Camp);
                r.AdvanceTo(GameTime.FromGameHours((long)d * 24 + 18));
            }
            r.AdvanceTo(GameTime.FromGameHours((long)(day0 + 15) * 24));
        }
        Go(straight.Runtime, straight.Supply); Go(rt, hs);
        Assert.Equal(straight.Supply.Book.Hash(), hs.Book.Hash());
        var s1 = WorldSave.Capture(straight.State, "h", "0.1"); var s2 = WorldSave.Capture(st, "h", "0.1");
        foreach (var k in s1.Sections.Keys.Where(k => k != WorldSave.MetaSection && k is "economy" or WarSupplySave.SectionName or "people"))
            Assert.True(s1.Sections[k].Span.SequenceEqual(s2.Sections[k].Span), $"section {k} differs after the continued run");
        Assert.True(st.Economy!.Ledger.Audit().IsConserved); Assert.True(st.Economy.MassAuditHolds());
    }

    /// <summary>A shortened war-load soak (seed 1234, the first campaign and the second, years 1 to 7: about 90 s): the coin and mass audits exact every day, the host fed from the books and paid from the chest, the levies out of their households, nothing left running at the end.</summary>
    [Fact, Trait("Category", "Soak")]
    public void WarLoadSoak_Short_Years1To7_KeepsCoinAndMassExact()
    {
        using var run = AuditWorld.Create();
        var soak = new WarLoadSoak(run, 7);
        soak.Run(r => output.WriteLine(WarLoadSoak.Format(r)));
        Assert.True(soak.Ok, soak.FirstFailure);
        Assert.Equal(7, soak.Rows.Count);
        Assert.All(soak.Rows.Take(5), r => Assert.Equal(0, r.Mustered));        // peace
        Assert.True(soak.Rows[5].Mustered > 0 && soak.Rows[6].Mustered > 0);    // Y6 and Y7 campaigns
        Assert.True(soak.Rows[5].ManDaysFed + soak.Rows[5].ManDaysHungry > 0);
        Assert.Empty(run.Supply.Book.OnCampaign);
    }

    /// <summary>The nightly one: the whole 25-year scenario on seed 1234 (the five seeds and WL-1 to WL-15 are EA3's EA-15). About 5 minutes.</summary>
    [Fact, Trait("Category", "Soak")]
    public void WarLoadSoak_Full25Years_Seed1234_NightlyKeepsCoinAndMassExact()
    {
        if (Environment.GetEnvironmentVariable("FL_NIGHTLY") != "1") return;       // run by the nightly job (FL_NIGHTLY=1); the short soak above is the CI one
        using var run = AuditWorld.Create();
        var soak = new WarLoadSoak(run, 25);
        soak.Run(r => output.WriteLine(WarLoadSoak.Format(r)));
        Assert.True(soak.Ok, soak.FirstFailure);
        Assert.Equal(25, soak.Rows.Count);
    }
}
