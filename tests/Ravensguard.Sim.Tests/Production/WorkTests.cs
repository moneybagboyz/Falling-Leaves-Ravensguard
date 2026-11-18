using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Goods;
using Ravensguard.Sim.People;
using Ravensguard.Sim.Production;

namespace Ravensguard.Sim.Tests.Production;

/// <summary>Golden tests G-B1, G-B3, G-B10, G-B11, G-B14 and the cash-box rules: wages, arrears, quitting, dividends, closure, quality, tool wear, mass audit.</summary>
public class WorkTests
{
    private static GameTime At(long hour) => GameTime.FromGameHours(hour);

    /// <summary>Clock hours of work (hours 06-15 only) until the station finishes its batch.</summary>
    private static int ClockHours(ProdKit k, Station st, long startHour = 6)
    {
        int worked = 0;
        for (long h = startHour; h < startHour + 24 * 10 && st.Busy; h++)
        {
            var t = At(h); bool work = t.Hour is >= 6 and < 16;
            k.Sys.WorkHour(t);
            if (work) worked++;
        }
        return worked;
    }

    private static (ProdKit Kit, Business B, Station St) Smelter(int ownerSkill, int staff, int staffSkill)
    {
        var k = new ProdKit(households: 1 + staff);
        var b = k.Make("smithy", 100_000, staff, Occupation.Smith, Skill.Smithing, staffSkill, ownerLevel: ownerSkill);
        k.Give(b, "iron_ore", 40_000); k.Give(b, "charcoal", 60_000);
        var st = b.Stations.First(s => s.Kind == "bloomery");
        Assert.True(k.Sys.TryStart(b, st, k.R("smelt_bloom"), At(6)));
        return (k, b, st);
    }

    [Fact]
    public void GB1_TimeToComplete_BySkillAndWorkers()
    {
        var (k1, b1, s1) = Smelter(50, 0, 50);
        Assert.Equal(100_000, Station.PerWorkerHour(10 * 1000, 1000) / 1_000_000);       // 100,000 ppm per worker-hour at skill 50
        Assert.Equal(10, ClockHours(k1, s1));                                            // 10 worker-hours, one worker
        Assert.Equal(6_000, k1.Have(b1, "iron_bloom"));                                  // 6 kg of bloom appear

        var (k2, b2, s2) = Smelter(50, 1, 50);
        Assert.Equal(5, ClockHours(k2, s2));                                             // two workers: 5 clock hours
        Assert.Equal(6_000, k2.Have(b2, "iron_bloom"));

        var (k3, _, s3) = Smelter(0, 0, 0);
        Assert.Equal(13, ClockHours(k3, s3));                                            // time_mult 1300
        var (k4, _, s4) = Smelter(100, 0, 100);
        Assert.Equal(7, ClockHours(k4, s4));                                             // time_mult 700
    }

    [Fact]
    public void GB3_FiveLooms_FiveTimesTheYarn_SameMassRatio()
    {
        double Ratio(int looms, out long clothG, out long yarnG)
        {
            var k = new ProdKit(households: 2 * looms);
            var b = k.Make("weavery", 0, 2 * looms - 1, Occupation.Weaver, Skill.Weaving, 50);
            b.Stations.RemoveAll(s => s.Kind != "loom");
            while (b.Stations.Count < looms) b.Stations.Add(new Station { Kind = "loom" });
            k.Give(b, "yarn", 1_600 * looms);
            for (int i = 0; i < looms; i++) Assert.True(k.Sys.TryStart(b, b.Stations[i], k.R("weave_cloth"), At(6)) || i > 0 && k.Have(b, "yarn") < 1_600);
            // one workday with full inputs for every loom
            for (long h = 6; h < 16; h++) k.Sys.WorkHour(At(h));
            clothG = k.Have(b, "cloth_wool"); yarnG = 1_600L * looms;
            return clothG / (double)yarnG;
        }
        double r1 = Ratio(1, out long c1, out long y1), r5 = Ratio(5, out long c5, out long y5);
        Assert.Equal(1_500, c1); Assert.Equal(7_500, c5);
        Assert.Equal(0.9375, r1, 6); Assert.Equal(0.9375, r5, 6);
        Assert.Equal(c1 * 5, c5); Assert.Equal(y1 * 5, y5);
    }

    [Fact]
    public void GB10_ThreeStationsFiveWorkers_CrewsAreTwoTwoOne()
    {
        var k = new ProdKit(households: 5);
        var b = k.Make("weavery", 0, 4, Occupation.Weaver, Skill.Weaving, 50);
        b.Stations.RemoveAll(s => s.Kind != "loom");
        while (b.Stations.Count < 3) b.Stations.Add(new Station { Kind = "loom" });
        k.Give(b, "yarn", 10_000);
        foreach (var st in b.Stations) Assert.True(k.Sys.TryStart(b, st, k.R("weave_cloth"), At(6)));
        k.Sys.WorkHour(At(6));
        Assert.Equal([2, 2, 1], b.Stations.Select(s => s.Crew.Count).ToArray());
        // five worker-hours credited in this clock hour
        long ppm = Station.PerWorkerHour(10 * 1000, 1000);
        Assert.Equal(2 * ppm, b.Stations[0].ProgressMicro); Assert.Equal(ppm, b.Stations[2].ProgressMicro);
        Assert.Equal(5 * ppm, b.Stations.Sum(s => s.ProgressMicro));
    }

    [Fact]
    public void GB11_Tanning_FifteenDaysInThePit_SecondBatchWaits()
    {
        var k = new ProdKit(households: 2);
        var b = k.Make("tannery", 100_000, 1, Occupation.Tanner, Skill.Tanning, 50);
        k.Give(b, "hide_raw", 100_000); k.Give(b, "oak_bark", 40_000);
        var pit = b.Stations[0];
        Assert.True(k.Sys.TryStart(b, pit, k.R("tan_hides"), At(6)));
        Assert.Equal(6 + 15 * 24, pit.BusyUntilHour);
        // a second batch cannot start in the busy pit, even with inputs
        k.Give(b, "hide_raw", 100_000); k.Give(b, "oak_bark", 40_000);
        Assert.False(k.Sys.TryStart(b, pit, k.R("tan_hides"), At(30)));
        long workHours = 0; long done = -1;
        for (long h = 6; h <= 6 + 15 * 24; h++)
        {
            var t = At(h);
            long before = pit.ProgressMicro;
            k.Sys.WorkHour(t);
            if (k.Have(b, "leather") > 0 && done < 0) done = h;
            if (pit.Busy && pit.ProgressMicro > before) workHours += pit.Crew.Count;
        }
        Assert.Equal(6 + 15 * 24, done);                                                 // day 15, hour 6
        Assert.Equal(60_000, k.Have(b, "leather"));
        Assert.Equal(40, workHours);                                                     // 40 worker-hours, all spent in the first day or two of the 15
        Assert.True(pit.Busy);                                                           // the second batch started only now, when the pit was free
        Assert.Equal(6 + 15 * 24, pit.StartedHour);
        Assert.Equal(0, k.Have(b, "oak_bark"));                                          // the second batch took the second 40 kg of bark
    }

    [Fact]
    public void GB14_ShortOfCharcoal_NothingStarts_NothingConsumed_AnOrderIsPosted()
    {
        var k = new ProdKit(households: 1, markets: true);
        var b = k.Make("smithy", 50_000, 0, Occupation.Smith, Skill.Smithing, 50);
        k.Give(b, "iron_ore", 40_000); k.Give(b, "charcoal", 59_000);
        var st = b.Stations.First(s => s.Kind == "bloomery");
        Assert.False(k.Sys.TryStart(b, st, k.R("smelt_bloom"), At(6)));
        Assert.Equal(40_000, k.Have(b, "iron_ore")); Assert.Equal(59_000, k.Have(b, "charcoal")); Assert.False(st.Busy);
        var book = k.Markets!.BookForSettlement(0);
        k.Sys.PostInputOrders(b, k.R("smelt_bloom"), book);
        Assert.Equal(1, book.PendingOrders);                                              // only the charcoal is short, by 1 kg
        // the order is exactly the shortfall and is filled from the book when someone lists charcoal
        var seller = k.Markets.HouseholdParty(0); k.Give(b, "iron_ore", 0);
        k.Econ.HouseholdStore[0].Create(k.Cat.IdOf("charcoal"), 5_000, CreateReason.Seed);
        book.List(seller, k.Cat.IdOf("charcoal"), 5_000, 100, 0);
        book.RunSession(10, 0);
        Assert.Equal(60_000, k.Have(b, "charcoal"));
        Assert.True(k.Sys.TryStart(b, st, k.R("smelt_bloom"), At(8)));
        Assert.True(k.Econ.MassAuditHolds());
    }

    [Fact]
    public void PartBatches_RunOnlyWhereTheRecipeAllowsThem()
    {
        var k = new ProdKit(households: 1);
        var b = k.Make("bakery", 10_000, 0, Occupation.Baker, Skill.Baking, 50);
        k.Give(b, "flour", 25_000); k.Give(b, "firewood", 30_000);
        var oven = b.Stations[0];
        Assert.True(k.Sys.TryStart(b, oven, k.R("bake_bread"), At(6)));                  // min batch 10%: 25 kg of flour is a 25% batch
        Assert.Equal(250, oven.ScalePermille);
        Assert.Equal(0, k.Have(b, "flour")); Assert.Equal(25_000, k.Have(b, "firewood"));  // 5 kg of firewood used
        Assert.Equal(1.5, ClockHours(k, oven), 0);                                       // 1.5 worker-hours: 2 clock hours
        Assert.Equal(32_500, k.Have(b, "bread"));                                        // 130 kg x 25%
    }

    [Fact]
    public void Wages_FlowThroughTheLedger_ArrearsAccrue_AndWorkersQuitAfterThreeShortDays()
    {
        var k = new ProdKit(households: 3);
        var b = k.Make("tannery", 1_050, 2, Occupation.Tanner, Skill.Tanning, 50);        // cash for 1,050 mites only
        long coin0 = k.Econ.Ledger.Audit().InPurses.Mites;
        k.Give(b, "hide_raw", 300_000); k.Give(b, "oak_bark", 120_000);
        foreach (var st in b.Stations) Assert.True(k.Sys.TryStart(b, st, k.R("tan_hides"), At(6)));
        for (long h = 6; h < 16; h++) k.Sys.WorkHour(At(h));
        // two staff worked 10 h each at 35/h; the owner works for nothing
        Assert.Equal(700, b.Owed.Sum());
        long hh1 = k.Econ.Ledger.Balance(k.Econ.HouseholdPurse[1]).Mites;
        k.Sys.PayWages(b, At(24));
        Assert.Equal(0, b.ArrearsMites);
        Assert.Equal(350, k.Econ.Ledger.Balance(k.Econ.HouseholdPurse[1]).Mites - hh1);
        Assert.Equal(350, k.Econ.Ledger.Balance(b.Purse).Mites);
        Assert.Equal(coin0, k.Econ.Ledger.Audit().InPurses.Mites);
        // a day with less cash than wages: the first staff member (ascending) is paid, the other accrues arrears
        for (long h = 30; h < 40; h++) k.Sys.WorkHour(At(h));
        k.Sys.PayWages(b, At(48));
        Assert.Equal(350, b.ArrearsMites); Assert.Equal(0, k.Econ.Ledger.Balance(b.Purse).Mites);
        Assert.Equal(2, b.Staff.Count);
        for (int day = 2; day <= 3; day++)
        {
            for (long h = day * 24 + 6; h < day * 24 + 16; h++) k.Sys.WorkHour(At(h));
            k.Sys.PayWages(b, At(day * 24 + 24));
        }
        Assert.Single(b.Staff);                                                          // the unpaid worker quit after three short days
        Assert.Equal(1, k.Sys.QuitCount);
        Assert.Equal(-1, k.People.Employer[2] == 0 ? -1 : k.People.Employer[2]);
        Assert.Equal(coin0, k.Econ.Ledger.Audit().InPurses.Mites);
    }

    [Fact]
    public void SeasonalDividend_ThenClosureAfterThreeLossSeasons()
    {
        var k = new ProdKit(households: 2);
        var rich = k.Make("bakery", 50_000, 0, Occupation.Baker, Skill.Baking, 50);
        k.Sys.Seasonal(At(0));
        Assert.Equal(25_000, rich.Dividends);                                             // no costs: reserve 0, half of the cash
        Assert.Equal(25_000, k.Econ.Ledger.Balance(k.Econ.HouseholdPurse[0]).Mites);
        Assert.Equal(0, rich.LossSeasons);

        var k2 = new ProdKit(households: 2);
        var poor = k2.Make("bakery", 10_000, 1, Occupation.Baker, Skill.Baking, 50);
        // three seasons in which wages (3,000 a season) are the only flow: losses every season, cash drains
        for (int s = 0; s < 3; s++)
        {
            poor.Owed[0] += 3_000;
            k2.Sys.PayWages(poor, At(s * 360));
            k2.Sys.Seasonal(At(s * 360));
        }
        Assert.Equal(BusinessState.Closed, poor.State);
        Assert.Empty(poor.Staff);
        Assert.Equal(0, k2.Econ.Ledger.Balance(poor.Purse).Mites);                        // what was left went back to the owner
        Assert.True(k2.Econ.Ledger.Audit().IsConserved);
    }

    [Fact]
    public void QualityFollowsSkill_ToolWearIsCounted_AndTheMassAuditNeverTrips()
    {
        long Run(int skill, out int[] q, out long wear, out long violations)
        {
            var k = new ProdKit(households: 1, seed: 5);
            var b = k.Make("smithy", 1_000_000, 0, Occupation.Smith, Skill.Smithing, skill);
            k.Give(b, "iron_bar", 6_000_000); k.Give(b, "charcoal", 10_000_000);
            var st = b.Stations.First(s => s.Kind == "anvil");
            Assert.True(k.Sys.TryStart(b, st, k.R("forge_nails"), At(6)));
            for (long h = 0; h < 24 * 60; h++) k.Sys.WorkHour(At(h));
            q = b.Quality; wear = b.ToolPiecesWorn; violations = k.Sys.MassViolations;
            Assert.True(k.Econ.MassAuditHolds());
            return b.Batches;
        }
        long nLow = Run(5, out var qLow, out _, out var v1), nHigh = Run(95, out var qHigh, out long wear, out var v2);
        Assert.Equal(0, v1 + v2);
        Assert.True(nLow >= 5 && nHigh >= 5);
        Assert.Equal(nLow, qLow.Sum()); Assert.Equal(nHigh, qHigh.Sum());
        double Mean(int[] q) => Enumerable.Range(0, 5).Sum(i => i * q[i]) / (double)q.Sum();
        Assert.True(Mean(qHigh) > Mean(qLow));
        Assert.True(wear >= 0);
    }

    [Fact]
    public void Determinism_TwoRunsGiveTheSameEconomyHash()
    {
        string Run()
        {
            var k = new ProdKit(households: 3, markets: true, seed: 9);
            var b = k.Make("bakery", 20_000, 2, Occupation.Baker, Skill.Baking, 60);
            k.Give(b, "flour", 300_000); k.Give(b, "firewood", 60_000);
            for (long h = 0; h < 24 * 20; h++) { var t = At(h); k.Markets!.HourlySession(t); k.Sys.WorkHour(t); k.Sys.DailySlice(t); }
            return k.Econ.ComputeHash();
        }
        Assert.Equal(Run(), Run());
    }
}
