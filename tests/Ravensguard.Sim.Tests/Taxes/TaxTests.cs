using Ravensguard.Sim.Abstract;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.People;
using Ravensguard.Sim.People.Vital;
using Ravensguard.Sim.Save;
using Ravensguard.Sim.Taxes;
using Ravensguard.Sim.Tests.Production;
using Ravensguard.Sim.WorldGen.Region;
using Xunit;
using Xunit.Abstractions;

namespace Ravensguard.Sim.Tests.Taxes;

/// <summary>Taxes and treasuries (taxes spec, golden tests G-T1 to G-T15 and decisions T1 to T12) on the seed-1234 world: the real ledger, the real tick, no stand-ins.</summary>
public class TaxTests(ITestOutputHelper output)
{
    private static GameTime Now(SimWorld sim, int seasonsLater = 0) => GameTime.FromGameSeconds(GameTime.FromGameHours(sim.State.HourDone + 1).Seconds + seasonsLater * 15L * TimeConst.SecondsPerDay);

    /// <summary>Moves coin between a household and the richest other household so the purse holds exactly <paramref name="mites"/> (a transfer: conservation is untouched).</summary>
    private static void SetCoin(SimWorld w, int hh, long mites)
    {
        var e = w.Economy; var led = e.Ledger; long cur = led.Balance(e.HouseholdPurse[hh]).Mites;
        int bank = -1; long best = -1;
        for (int h = 0; h < w.Households.Count; h++) { if (h == hh || w.Households.MemberCount[h] == 0) continue; long b = led.Balance(e.HouseholdPurse[h]).Mites; if (b > best) { best = b; bank = h; } }
        if (cur > mites) Assert.True(led.Transfer(e.HouseholdPurse[hh], e.HouseholdPurse[bank], new Coin(cur - mites), TransferReason.Gift));
        else if (cur < mites) Assert.True(led.Transfer(e.HouseholdPurse[bank], e.HouseholdPurse[hh], new Coin(mites - cur), TransferReason.Gift));
    }

    /// <summary>Normalises a settlement for a collection test: every household owns its dwelling, holds the given coin, has no grievance and no arrears.</summary>
    private static List<int> Prepare(SoakWorld w, int settlement, long coin)
    {
        var hht = w.Sim.Households; var list = new List<int>();
        for (int h = 0; h < hht.Count; h++)
        {
            if (hht.MemberCount[h] == 0 || hht.Settlement[h] != settlement) continue;
            hht.Deed[h] = DeedKind.Owned; w.Econ.Grievances[h] = Grievance.None; w.Sim.State.Taxes.Arrears.Remove(h);
            SetCoin(w.Sim, h, coin); list.Add(h);
        }
        return list;
    }

    private static int BiggestSettlement(SimWorld sim)
    {
        var hht = sim.Households; var n = new int[sim.Population.Region.Settlements.Count];
        for (int h = 0; h < hht.Count; h++) if (hht.MemberCount[h] > 0) n[hht.Settlement[h]]++;
        int best = 0; for (int s = 1; s < n.Length; s++) if (n[s] > n[best]) best = s;
        return best;
    }

    // ------------------------------------------------------------------ G-T1: the four levels and the crown's share

    [Fact]
    public void G_T1_TheHearthTaxAtTheFourLevels_AndTheFiefsAndRealmsShares()
    {
        using var w = SoakWorld.Create(withTicker: false); var tax = w.Economy.Taxes;
        // the golden region: 5,200 households
        Assert.Equal(1_040_000, 5200L * tax.HearthMites(TaxLevel.Normal));
        Assert.Equal(520_000, 5200L * tax.HearthMites(TaxLevel.Low));
        Assert.Equal(1_820_000, 5200L * tax.HearthMites(TaxLevel.High));
        Assert.Equal(3_120_000, 5200L * tax.HearthMites(TaxLevel.Crushing));
        long all = 5200L * 200, fief = all * 70 / 100;
        Assert.Equal(728_000, fief); Assert.Equal(312_000, all - fief);

        // the real collection in the biggest settlement: every household pays in full, the fief keeps the floor of 70 percent, the realm the rest
        int s = BiggestSettlement(w.Sim); var hh = Prepare(w, s, 100_000);
        var T = tax.Treasuries; int payee = T.PayeeSlot[s], realm = T.RealmSlotOfSettlement[s];
        Assert.True(payee >= 0 && realm >= 0, "the settlement has a fief or town treasury and a realm");
        foreach (var level in new[] { TaxLevel.Low, TaxLevel.Normal, TaxLevel.High, TaxLevel.Crushing })
        {
            tax.SetAllLevels(level);
            long p0 = T.IncomeOf(payee, TransferReason.Hearth, false), r0 = T.IncomeOf(realm, TransferReason.Hearth, false), c0 = w.Econ.Ledger.Audit().InPurses.Mites;
            tax.CollectSettlement(s, Now(w.Sim), new RegionOutbox(w.Sim.Ids));
            long total = (long)hh.Count * tax.HearthMites(level), fiefKeeps = total * 70 / 100;
            Assert.Equal(total, T.IncomeOf(payee, TransferReason.Hearth, false) - p0);
            Assert.Equal(total - fiefKeeps, T.IncomeOf(realm, TransferReason.Hearth, false) - r0);
            Assert.Equal(c0, w.Econ.Ledger.Audit().InPurses.Mites);
            hh.ForEach(h => SetCoin(w.Sim, h, 100_000));
        }
    }

    // ------------------------------------------------------------------ G-T2, G-T3: arrears, debt, eviction

    private static int PickPlainHousehold(SoakWorld w, int settlement)
    {
        var hht = w.Sim.Households; var prod = w.Economy.Production;
        for (int h = 0; h < hht.Count; h++)
            if (hht.MemberCount[h] > 1 && hht.Settlement[h] == settlement && !prod.Businesses.Any(b => b.OwnerHousehold == h) && w.Sim.Population.FiefLordHousehold.All(l => l != h) && w.Sim.Population.PriestHousehold.All(p => p != h)) return h;
        throw new InvalidOperationException("no plain household");
    }

    [Fact]
    public void G_T2_AHouseholdThatCannotPayInFull_PaysWhatItHolds_AndOwesTheRestNextSeason()
    {
        using var w = SoakWorld.Create(withTicker: false); var tax = w.Economy.Taxes; tax.SetAllLevels(TaxLevel.Normal);
        int s = BiggestSettlement(w.Sim); Prepare(w, s, 100_000); int h = PickPlainHousehold(w, s);
        var T = tax.Treasuries; int payee = T.PayeeSlot[s]; long in0 = T.IncomeOf(payee, TransferReason.Hearth, false);
        SetCoin(w.Sim, h, 150);
        tax.CollectSettlement(s, Now(w.Sim, 0), new RegionOutbox(w.Sim.Ids));
        var row = w.Sim.State.Taxes.Row(h)!;
        Assert.Equal(50, row.ArrHearth); Assert.Equal(0, w.Econ.HouseholdCoin(h).Mites); Assert.Equal(1, row.Seasons);
        // next season: due 200 + 50 = 250, it pays all of it
        SetCoin(w.Sim, h, 1000); tax.CollectSettlement(s, Now(w.Sim, 1), new RegionOutbox(w.Sim.Ids));
        Assert.Null(w.Sim.State.Taxes.Row(h));                      // a clear row is dropped
        Assert.Equal(1000 - 250, w.Econ.HouseholdCoin(h).Mites);
        Assert.True(T.IncomeOf(payee, TransferReason.Hearth, false) > in0);
    }

    [Fact]
    public void G_T3_ArrearsBecomeDebtAfterThreeSeasons_AndEvictionComesAtSix_OwnedKeepsADebtRecord()
    {
        using var w = SoakWorld.Create(withTicker: false); var tax = w.Economy.Taxes; tax.SetAllLevels(TaxLevel.Normal);
        tax.Policy = tax.Policy with { WorkOffDaysPerSeason = 0 };                    // no labour: the plain ladder
        int s = BiggestSettlement(w.Sim); Prepare(w, s, 100_000);
        int rented = PickPlainHousehold(w, s); int owned = -1; var hht = w.Sim.Households;
        for (int h = rented + 1; h < hht.Count; h++) if (hht.MemberCount[h] > 0 && hht.Settlement[h] == s && h != rented && !w.Economy.Production.Businesses.Any(b => b.OwnerHousehold == h)) { owned = h; break; }
        hht.Deed[rented] = DeedKind.Tenant; hht.Deed[owned] = DeedKind.Owned;
        var ob = new RegionOutbox(w.Sim.Ids); int members = hht.MemberCount[rented];
        for (int k = 0; k < 6; k++)
        {
            SetCoin(w.Sim, rented, 0); SetCoin(w.Sim, owned, 0);
            tax.CollectSettlement(s, Now(w.Sim, k), ob);
            if (k == 2)
            {
                var r = w.Sim.State.Taxes.Row(owned)!;
                Assert.Equal(0, r.Arrears); Assert.Equal(600, r.Debt); Assert.Equal(3, r.Seasons);   // arrears of 600 became a debt
            }
        }
        var kept = w.Sim.State.Taxes.Row(owned)!;
        Assert.Equal(1200, kept.Total); Assert.Equal(600, kept.Debt); Assert.Equal(600, kept.Arrears); Assert.Equal(6, kept.Seasons);
        Assert.True(hht.MemberCount[owned] > 0, "an owner keeps the house: Phase 2 only keeps the debt record");
        Assert.Equal(0, hht.MemberCount[rented]);                                   // evicted: the household left
        Assert.Null(w.Sim.State.Taxes.Row(rented));
        Assert.Equal(1, w.Sim.State.Taxes.TallyOf(TaxTally.Evictions));
        var entry = ob.Entries.Single(e => e.TypeId == "eviction");
        Assert.Contains(entry.Causes, c => c.Kind == CauseKind.Tax);
        Assert.Equal(members, ob.Counters["emigrants"]);
        Assert.True(w.Econ.Ledger.Audit().IsConserved);
    }

    [Fact]
    public void LabourOnTheDemesne_WorksArrearsOff_SoATenantWithHandsIsNotEvicted()
    {
        using var w = SoakWorld.Create(withTicker: false); var tax = w.Economy.Taxes; tax.SetAllLevels(TaxLevel.High);
        int s = BiggestSettlement(w.Sim); Prepare(w, s, 100_000); int h = PickPlainHousehold(w, s); var hht = w.Sim.Households;
        hht.Deed[h] = DeedKind.Tenant;
        for (int k = 0; k < 8; k++) { SetCoin(w.Sim, h, 0); tax.CollectSettlement(s, Now(w.Sim, k), new RegionOutbox(w.Sim.Ids)); }
        Assert.True(hht.MemberCount[h] > 0, "a household with working adults works its dues off on the demesne");
        Assert.True(w.Sim.State.Taxes.TallyOf(TaxTally.WorkedOff) > 0);
    }

    [Fact]
    public void ACoinParcelRentBehind_IsAGrievanceNotASeasonOfTheLadder()
    {
        using var w = SoakWorld.Create(withTicker: false); var tax = w.Economy.Taxes;
        int s = BiggestSettlement(w.Sim); Prepare(w, s, 100_000); int h = PickPlainHousehold(w, s);
        var P = w.Economy.Farming.State.Parcels; int p = 0; P.Holder[p] = h; P.CoinArrears[p] = 3840;
        var t0 = GameTime.FromCalendar(w.Pop.NowYear, Season.Spring, 1);
        for (int k = 0; k < 8; k++) tax.CollectSettlement(s, GameTime.FromGameSeconds(t0.Seconds + k * 15L * TimeConst.SecondsPerDay), new RegionOutbox(w.Sim.Ids));
        Assert.Null(w.Sim.State.Taxes.Row(h));                                          // two years behind on the lease, no season counted, nobody evicted
        Assert.True(w.Sim.Households.MemberCount[h] > 0);
        tax.Daily(Now(w.Sim), new RegionOutbox(w.Sim.Ids));
        Assert.True((w.Econ.Grievances[h] & Grievance.Tax) != 0);
    }

    [Fact]
    public void AHouseholdWithNoHandsToWork_IsLetStayOnCharityWhenTheLadderEndsInEviction()
    {
        using var w = SoakWorld.Create(withTicker: false); var tax = w.Economy.Taxes; tax.SetAllLevels(TaxLevel.Normal);
        tax.Policy = tax.Policy with { WorkOffDaysPerSeason = 0 };
        int s = BiggestSettlement(w.Sim); Prepare(w, s, 100_000); int h = PickPlainHousehold(w, s); var hht = w.Sim.Households; var p = w.Sim.People;
        hht.Deed[h] = DeedKind.Rented;
        foreach (int m in hht.Members(h)) p.BirthDay[m] = (int)(w.Sim.State.HourDone / 24) - 70 * TimeConst.DaysPerYear;           // everyone over sixty
        for (int k = 0; k < 7; k++) { SetCoin(w.Sim, h, 0); tax.CollectSettlement(s, Now(w.Sim, k), new RegionOutbox(w.Sim.Ids)); }
        Assert.True(hht.MemberCount[h] > 0);
        Assert.Equal(0, w.Sim.State.Taxes.TallyOf(TaxTally.Evictions));
        Assert.True(w.Sim.State.Taxes.TallyOf(TaxTally.WrittenOff) > 0);
    }

    // ------------------------------------------------------------------ G-T5 and the toll API

    [Fact]
    public void G_T5_AMarketToll_IsFivePercentRoundedHalfUp_AndTheCaravanTollsFollowTheSpec()
    {
        Assert.Equal(389, Tolls.MarketToll(7776)); Assert.Equal(7776 - 389, 7387);
        var p = new Ravensguard.Content.TaxPolicyDef { Id = "d" };
        Assert.Equal(424, Tolls.Compute(p, TollKind.Gate, 4, 64_800));      // caravans G-L3: 4 carts x 25 + 0.5 percent of the cargo
        Assert.Equal(30, Tolls.Compute(p, TollKind.Bridge, 3, 99_999));
        Assert.Equal(389, Tolls.Compute(p, TollKind.Market, 0, 7776));
    }

    [Fact]
    public void ATollPaidAtAGate_GoesToTheHoldersTreasury_AndAPayerPaysWhatItHolds()
    {
        using var w = SoakWorld.Create(withTicker: false); var sim = w.Sim; var tax = w.Economy.Taxes; var e = w.Econ;
        int s = BiggestSettlement(sim); var holder = Tolls.HolderPurse(sim, s); Assert.False(holder.IsNone);
        int payerHh = PickPlainHousehold(w, s); var payer = e.HouseholdPurse[payerHh]; SetCoin(sim, payerHh, 1000);
        long h0 = e.Ledger.Balance(holder).Mites, c0 = e.Ledger.Audit().InPurses.Mites;
        Assert.Equal(424, Tolls.ChargeAt(sim, payer, s, TollKind.Gate, 4, 64_800));
        Assert.Equal(h0 + 424, e.Ledger.Balance(holder).Mites);
        Assert.Equal(576, e.Ledger.Balance(payer).Mites);
        Assert.Equal(576, Tolls.ChargeAt(sim, payer, s, TollKind.Gate, 100, 10_000_000));     // owes 2,500 + 50,000, holds 576: pays 576
        Assert.Equal(0, Tolls.ChargeAt(sim, payer, s, TollKind.Gate, 1, 0));                  // nothing left
        Assert.Equal(c0, e.Ledger.Audit().InPurses.Mites);
        int slot = tax.Treasuries.All.First(r => r.Purse == holder).Slot;
        Assert.Equal(424 + 576, tax.Treasuries.IncomeOf(slot, TransferReason.Toll, false));
    }

    // ------------------------------------------------------------------ G-T8, G-T9, G-T11, G-T12

    [Fact]
    public void G_T8_Unrest_IsTheWeightedShareOfHouseholdsWithAGrievance()
    {
        long sum = 0;
        for (int i = 0; i < 30; i++) sum += TaxSystem.GrievanceWeightHalves(Grievance.Hunger);
        for (int i = 0; i < 10; i++) sum += TaxSystem.GrievanceWeightHalves(Grievance.Tax);
        for (int i = 0; i < 5; i++) sum += TaxSystem.GrievanceWeightHalves(Grievance.Hunger | Grievance.Tax);
        Assert.Equal(125, sum);                                                       // 62.5 in whole units
        Assert.Equal(625, TaxSystem.UnrestOf(sum, 100));
        using var w = SoakWorld.Create(withTicker: false); var tax = w.Economy.Taxes;
        Assert.Equal(3, tax.RungFor(625)); Assert.Equal(4, tax.RungFor(750)); Assert.Equal(2, tax.RungFor(350)); Assert.Equal(1, tax.RungFor(200)); Assert.Equal(0, tax.RungFor(199));
        Assert.Equal(1000, TaxSystem.UnrestOf(100_000, 10));
        Assert.Equal(100, TaxSystem.UnrestOf(2, 1));                                  // a lone farmstead is not a crowd (T9)
    }

    [Fact]
    public void G_T9_Evasion_IsSeededAndNearThirtyPercent_AndTheDuesCarryAsArrears()
    {
        (long Evaded, int Evaders, string Hash) Run(bool reverse)
        {
            using var w = SoakWorld.Create(withTicker: false); var tax = w.Economy.Taxes; tax.SetAllLevels(TaxLevel.Normal);
            if (reverse) tax.HouseholdOrder = l => l.Reverse();
            int s = BiggestSettlement(w.Sim); var hh = Prepare(w, s, 100_000);
            foreach (int h in hh) w.Econ.Grievances[h] = Grievance.Tax;
            w.Sim.State.Taxes.Rung[s] = 2;
            tax.CollectSettlement(s, Now(w.Sim), new RegionOutbox(w.Sim.Ids));
            int evaders = w.Sim.State.Taxes.Arrears.Count(kv => kv.Value.ArrHearth == 200);
            output.WriteLine($"{hh.Count} households, {evaders} evaded");
            Assert.True(Math.Abs(evaders - hh.Count * 30 / 100) <= 4 * Math.Sqrt(hh.Count * 0.21), $"evaders {evaders} of {hh.Count}");
            Assert.Equal(evaders * 200L, w.Sim.State.Taxes.TallyOf(TaxTally.HearthEvaded));
            return (w.Sim.State.Taxes.TallyOf(TaxTally.HearthEvaded), evaders, w.Sim.ComputeHash());
        }
        var a = Run(false); var b = Run(false); var c = Run(true);
        Assert.Equal(a, b);                                                           // deterministic
        Assert.Equal(a, c);                                                           // and independent of the order the reeve visits the houses (G-T14)
    }

    [Fact]
    public void EA_S13_WithUnrestAttached_ThePhase2EvasionRollIsSkipped_AndTheHookAloneDecides()
    {
        (int Evaders, int Hooked) Run(bool hookEvadesAll)
        {
            using var w = SoakWorld.Create(withTicker: false); var tax = w.Economy.Taxes; tax.SetAllLevels(TaxLevel.Normal);
            int called = 0;
            tax.EvasionHook = (_, _, _) => { called++; return hookEvadesAll; };       // E1's rung-2 roll stands in: it says nobody evades (or everybody)
            int s = BiggestSettlement(w.Sim); var hh = Prepare(w, s, 100_000);
            foreach (int h in hh) w.Econ.Grievances[h] = Grievance.Tax;
            w.Sim.State.Taxes.Rung[s] = 2;
            tax.CollectSettlement(s, Now(w.Sim), new RegionOutbox(w.Sim.Ids));
            return (w.Sim.State.Taxes.Arrears.Count(kv => kv.Value.ArrHearth == 200), called);
        }
        var none = Run(false); var all = Run(true);
        Assert.Equal(0, none.Evaders);                                                // fails before P7-0B: Phase 2's own roll still evaded about 30 percent
        Assert.True(none.Hooked > 0); Assert.Equal(all.Hooked, all.Evaders);          // every evader is the hook's
    }

    [Fact]
    public void G_T11_TheLevy_IsAtenthOfTheHouseholdsInPeace_AndAFifthAtWar()
    {
        using var w = SoakWorld.Create(withTicker: false); var tax = w.Economy.Taxes;
        Assert.Equal(520, tax.LevyQuota(5200, false)); Assert.Equal(1040, tax.LevyQuota(5200, true));
        Assert.Equal(130_000, 520L * 250); Assert.Equal(1_950_000, 520L * 250 * 15);
        tax.SeasonEnd(Now(w.Sim));
        var reg = w.Sim.Population.Region; var hht = w.Sim.Households; var per = new int[reg.Fiefs.Count];
        for (int h = 0; h < hht.Count; h++) if (hht.MemberCount[h] > 0) { int f = reg.Settlements[hht.Settlement[h]].FiefIndex; if (f >= 0) per[f]++; }
        for (int f = 0; f < per.Length; f++) Assert.Equal(per[f] / 10, w.Sim.State.Taxes.LevyDue[f]);
        Assert.True(per.Sum() > 0);
    }

    [Fact]
    public void G_T12_TheLordKeepsFortyPercentOfWhatTheTreasuryHoldsAboveItsPayrollAndReserve()
    {
        Assert.Equal(248_900, TaxSystem.LordShare(728_000, 35_250, 2, 40));
        Assert.Equal(0, TaxSystem.LordShare(100_000, 35_250, 2, 40));                 // under the reserve: nothing
        using var w = SoakWorld.Create(withTicker: false); var sim = w.Sim; var tax = w.Economy.Taxes; var reg = sim.Population.Region; var e = w.Econ;
        int f = Enumerable.Range(0, reg.Fiefs.Count).First(i => sim.Population.FiefLordHousehold[i] >= 0 && sim.Households.MemberCount[sim.Population.FiefLordHousehold[i]] > 0);
        int slot = tax.Treasuries.FiefSlot[f]; var purse = tax.Treasuries[slot].Purse; int lord = sim.Population.FiefLordHousehold[f];
        long have = e.Ledger.Balance(purse).Mites; if (have > 0) Assert.True(e.Ledger.Transfer(purse, e.HouseholdPurse[BiggestHousehold(sim, lord)], new Coin(have), TransferReason.Gift));
        long payroll0 = tax.PayrollSeason(f), fill = payroll0 * 8 + 500_000; long got = 0;
        foreach (int h in Enumerable.Range(0, sim.Households.Count).Where(h => h != lord && sim.Households.MemberCount[h] > 0).OrderByDescending(h => e.Ledger.Balance(e.HouseholdPurse[h]).Mites).ThenBy(h => h).Take(60))
        {
            if (got >= fill) break;
            long t = Math.Min(fill - got, e.Ledger.Balance(e.HouseholdPurse[h]).Mites / 2);
            if (t > 0 && e.Ledger.Transfer(e.HouseholdPurse[h], purse, new Coin(t), TransferReason.Gift)) got += t;
        }
        fill = got; Assert.True(fill > payroll0 * 3, $"payroll {payroll0}, funded {fill}");
        long payroll = tax.PayrollSeason(f), expect = TaxSystem.LordShare(fill, payroll, 2, 40), lord0 = e.Ledger.Balance(e.HouseholdPurse[lord]).Mites;
        tax.SeasonEnd(Now(sim));
        Assert.True(expect > 0);
        Assert.Equal(lord0 + expect, e.Ledger.Balance(e.HouseholdPurse[lord]).Mites);
        Assert.Equal(fill - expect, e.Ledger.Balance(purse).Mites);
        Assert.Equal(expect, sim.State.Taxes.LordPaid[slot]);
    }
    private static int BiggestHousehold(SimWorld sim, int not) { int b = -1; long best = -1; var e = sim.Economy; for (int h = 0; h < sim.Households.Count; h++) { if (h == not || sim.Households.MemberCount[h] == 0) continue; long v = e.Ledger.Balance(e.HouseholdPurse[h]).Mites; if (v > best) { best = v; b = h; } } return b; }

    // ------------------------------------------------------------------ treasuries: parish payroll and wages owed

    [Fact]
    public void AParishChest_PaysItsPriestADay_AndOwesWhatItCannotPay()
    {
        using var w = SoakWorld.Create(withTicker: false); var sim = w.Sim; var tax = w.Economy.Taxes; var e = w.Econ; var led = e.Ledger;
        var rec = tax.Treasuries.All.First(r => r.Kind == TreasuryKind.Parish); int priest = sim.Population.PriestHousehold[rec.Settlement];
        long have = led.Balance(rec.Purse).Mites; if (have > 0) Assert.True(led.Transfer(rec.Purse, e.HouseholdPurse[priest], new Coin(have), TransferReason.Gift));
        long p0 = led.Balance(e.HouseholdPurse[priest]).Mites;
        for (int d = 0; d < 3; d++) tax.Daily(Now(sim), new RegionOutbox(sim.Ids));
        Assert.Equal(750, sim.State.Taxes.WageOwed[rec.Slot]); Assert.Equal(p0, led.Balance(e.HouseholdPurse[priest]).Mites);
        Assert.True(led.Transfer(e.HouseholdPurse[priest], rec.Purse, new Coin(10_000), TransferReason.Tithe));
        long p1 = led.Balance(e.HouseholdPurse[priest]).Mites;
        tax.Daily(Now(sim), new RegionOutbox(sim.Ids));
        Assert.Equal(0, sim.State.Taxes.WageOwed[rec.Slot]); Assert.Equal(p1 + 1000, led.Balance(e.HouseholdPurse[priest]).Mites);   // 750 owed + the day's 250
        Assert.True(led.Audit().IsConserved);
    }

    // ------------------------------------------------------------------ on the real tick: the crown's share, conservation, save and resume

    [Fact]
    public void OnTheRealTick_TheCrownsShareReachesTheRealm_EveryMiteAccountedFor()
    {
        using var w = SoakWorld.Create(); var sim = w.Sim; var tax = w.Economy.Taxes; var T = tax.Treasuries;
        w.Run(2);                                                                     // the first day of spring: S4 collects, and the realm gets its share
        long hearth = sim.State.Taxes.TallyOf(TaxTally.HearthCollected), fwd = sim.State.Taxes.TallyOf(TaxTally.CrownForwarded);
        Assert.True(hearth > 4_000 * 150, $"collected {hearth}");
        Assert.True(fwd >= hearth * 29 / 100 - 100 && fwd <= hearth * 31 / 100 + 100, $"forward {fwd} of {hearth}");
        var realms = T.All.Where(r => r.Kind == TreasuryKind.Realm).ToList();
        Assert.NotEmpty(realms);
        long atRealms = realms.Sum(r => T.IncomeOf(r.Slot, TransferReason.Hearth, false));
        Assert.Equal(fwd, atRealms);
        long fiefIncome = T.All.Where(r => r.Kind is TreasuryKind.Fief or TreasuryKind.Town).Sum(r => T.IncomeOf(r.Slot, TransferReason.Hearth, false));
        Assert.Equal(hearth, fiefIncome);                                              // what the households paid is what the fiefs and towns received
        Assert.True(w.Econ.Ledger.Audit().IsConserved);
        Assert.True(w.Econ.Ledger.Audit().Imbalance.IsZero);                          // no taxes were printed: coin only moved (the continent's own flows are audited as sources and sinks)
        output.WriteLine(tax.Report());
    }

    [Fact]
    public void AYearOfTaxesRentsAndWages_KeepsCoinAndMassExact_AndNoTreasuryGoesNegative()
    {
        using var w = SoakWorld.Create(); var tax = w.Economy.Taxes;
        int treasuries = tax.Treasuries.Count;
        w.Run(60, (d, _) =>
        {
            if (d % 15 == 14) { Assert.True(w.Econ.Ledger.Audit().IsConserved); foreach (var r in tax.Treasuries.All) Assert.True(w.Econ.Ledger.Balance(r.Purse).Mites >= 0); }
        });
        Assert.True(w.Econ.Ledger.Audit().IsConserved); Assert.True(w.Econ.MassAuditHolds());
        output.WriteLine($"{treasuries} treasuries\n{tax.Report()}\n{tax.Treasuries.LastSeasonReport()}");
        Assert.True(tax.Treasuries.History.Count >= 3);
        Assert.True(tax.Treasuries.IncomeTotal(tax.Treasuries.FiefSlot[0]) > 0);
    }

    [Fact]
    public void AHigherLevel_BreedsGrievances_AndACrushingOneRaisesUnrestThroughTheLadder()
    {
        using var w = SoakWorld.Create(); var tax = w.Economy.Taxes; var st = w.Sim.State.Taxes; tax.SetAllLevels(TaxLevel.Crushing);
        w.Run(20);
        int s = BiggestSettlement(w.Sim);
        output.WriteLine($"unrest {st.Unrest[s]} rung {st.Rung[s]}; riots {st.TallyOf(TaxTally.Riots)}; {tax.Report()}");
        Assert.True(st.Unrest[s] >= 750, "every household's tax grievance counts at the crushing level");
        Assert.True(st.Rung[s] >= 3);
        Assert.True(st.TallyOf(TaxTally.Riots) > 0, "the riot rung storms a granary");
        Assert.True(w.Econ.Ledger.Audit().IsConserved); Assert.True(w.Econ.MassAuditHolds());
    }

    [Fact]
    public void TheTaxState_SavesAndLoadsByteForByte_AndWithTheAbstractSection()
    {
        using var w = SoakWorld.Create(); var sim = w.Sim; var st = sim.State.Taxes;
        w.Run(50);                                                                     // three seasons of collections, a tally, some arrears
        var cap1 = AbstractSave.Capture(sim.State, sim.Population);
        var bytes1 = SaveCodec.SerializeCompressed(cap1);
        var back = SaveCodec.DeserializeCompressed<AbstractSave>(bytes1).Restore(sim.Population);
        var bytes2 = SaveCodec.SerializeCompressed(AbstractSave.Capture(back, sim.Population));
        Assert.Equal(bytes1, bytes2);
        Assert.Equal(st.Tally, back.Taxes.Tally); Assert.Equal(st.Arrears.Count(kv => !kv.Value.IsClear), back.Taxes.Arrears.Count);
        Assert.Equal(st.Unrest, back.Taxes.Unrest); Assert.Equal(st.LastIncome, back.Taxes.LastIncome);
        output.WriteLine($"{bytes1.Length} bytes, {st.Arrears.Count} arrears rows");
    }
}
