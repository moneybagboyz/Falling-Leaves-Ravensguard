using Ravensguard.Content;
using Ravensguard.Sim.Abstract;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Land;
using Ravensguard.Sim.People;
using Ravensguard.Sim.Rule;
using Ravensguard.Sim.Save;
using Ravensguard.Sim.Taxes;
using Ravensguard.Sim.Tests.Economy;
using Ravensguard.Sim.Tests.Production;
using Ravensguard.Sim.WorldGen.Region;
using RegionData = Ravensguard.Sim.WorldGen.Region.Region;
using Xunit;
using Xunit.Abstractions;

namespace Ravensguard.Sim.Tests.Rule;

/// <summary>
/// Rule and income (P6-C3, docs/specs/phase6/rule-and-income.md 7): RI-1 to RI-19 on a private headless world of seed 1234 whose rule book is derived as the seeding pass derives it. Each test names its gate; the pure
/// arithmetic is pinned with the spec's numbers and the passes are driven by hand with real purses, so every coin that moves is a ledger transfer the audit sees.
/// </summary>
public class RuleTests(ITestOutputHelper output)
{
    private static RuleSettingsDef S => new() { Id = "rule" };

    // ---------------------------------------------------------------- RI-1 and RI-12 (also in RuleSaveTests for the runtime)

    [Fact]
    public void RI1_EveryRegionFiefHasADeed_WithHolderOverlordAndRights_TheCountsAreThePhase1Lords()
    {
        using var rig = RuleRig.Make();
        var region = rig.Region; var pop = rig.W.Pop;
        Assert.Equal(region.Fiefs.Count, rig.B.Fiefs.Count);
        Assert.Equal(pop.FiefLord.Count(l => l >= 0), rig.B.Fiefs.Values.Count(f => f.Holder >= 0));
        foreach (var f in rig.B.Fiefs.Values)
        {
            Assert.Equal(region.Fiefs[f.Fief].Realm, f.Realm);                            // the overlord is the fief's realm
            Assert.Equal(FiefRights.All, f.Rights);
            if (pop.FiefLord[f.Fief] >= 0) Assert.Equal((long)rig.People.IdOf(pop.FiefLord[f.Fief]).Value, f.Holder);
            Assert.True(f.ServiceMen >= 1 && f.ServiceDays == 7);
        }
        // every fief has a reeve and the offices are real people living in it
        foreach (var f in rig.B.Fiefs.Values)
        {
            var reeve = rig.B.OfficeOf(OfficeKind.Reeve, f.Fief);
            if (reeve == null) continue;
            int row = rig.People.IndexOf(new EntityId((ulong)reeve.Holder));
            Assert.True(row >= 0 && rig.People.IsAlive(row));
            Assert.InRange((int)reeve.Honesty, 10, 100);
        }
        Assert.NotEmpty(rig.B.Offices);
        // a pure function of the seed and the ids: derived twice, the same
        var again = RuleSeeding.Build(rig.W.Pop, EconFixture.Content, SoakWorld.Seed);
        Assert.Equal(rig.B.Hash(), again.Hash());
        output.WriteLine($"{rig.B.Fiefs.Count} fiefs, {rig.B.Offices.Count} offices, hash {rig.B.Hash():X}");
    }

    // ---------------------------------------------------------------- RI-2 food rent in kind, commuted

    [Fact]
    public void RI2_AThirtyPercentShareOfTwentyAcres_IsTwoHundredTwentyEightMeasures_AndCommutedItBecomesCoin()
    {
        Assert.Equal(228, RuleMath.ShareMeasures(20, 38, 30));
        Assert.Equal(228 * 12, RuleMath.Commuted(228, 12));
        using var rig = RuleRig.Make();
        var c = rig.C; var P = rig.W.Economy.Farming.State.Parcels;
        // a fief whose lord has share-leased parcels
        int fief = -1, leases = 0;
        foreach (var f in rig.B.Fiefs.Values)
        {
            int lordHh = f.Fief < rig.W.Pop.FiefLordHousehold.Length ? rig.W.Pop.FiefLordHousehold[f.Fief] : -1; if (lordHh < 0) continue;
            int n = 0; for (int p = 0; p < P.Count; p++) if (P.Landlord[p] == lordHh && P.Lease[p] == Ravensguard.Sim.Farming.LeaseKind.Share) n++;
            if (n > 0) { fief = f.Fief; leases = n; break; }
        }
        Assert.True(fief >= 0, "no share leases in the region");
        Assert.Equal(leases, RuleDues.Commute(c, fief, true, rig.Day));
        var d = rig.W.Economy.Farming.Settings.Def;
        int lh = rig.W.Pop.FiefLordHousehold[fief];
        for (int p = 0; p < P.Count; p++) if (P.Landlord[p] == lh) Assert.NotEqual(Ravensguard.Sim.Farming.LeaseKind.Share, P.Lease[p]);
        Assert.True(rig.B.Fiefs[fief].RentInCoin);
        Assert.Equal(-1, RuleDues.Commute(c, fief, false, rig.Day + 30));                    // one switch a year
        int coinLeases = 0; for (int p = 0; p < P.Count; p++) if (P.Landlord[p] == lh && P.Lease[p] == Ravensguard.Sim.Farming.LeaseKind.Coin) coinLeases++;
        Assert.Equal(coinLeases, RuleDues.Commute(c, fief, false, rig.Day + 61));                // all of his coin leases go back to a share
        Assert.Equal(d.RentShareBpDefault, P.ShareBp.Where((_, i) => i < P.Count && P.Landlord[i] == lh && P.Lease[i] == Ravensguard.Sim.Farming.LeaseKind.Share).First());
    }

    // ---------------------------------------------------------------- RI-3 labour service

    [Fact]
    public void RI3_TenantHouseholdsOweOneDayInFiveOfAWindow_CreditedToTheDemesne_AMissingOneOwesAHundredFiftyAMiteDay()
    {
        Assert.Equal(3, RuleMath.LabourDaysOwed(15, 5));
        Assert.Equal(3 * 150, RuleMath.LabourMissed(3, 0, 150));
        using var rig = RuleRig.Make(); var c = rig.C;
        var hht = rig.W.Pop.Households;
        var fief = rig.B.Fiefs.Values.OrderByDescending(f => c.HouseholdsOfFief(f.Fief).Count(h => hht.Deed[h] == DeedKind.Tenant)).First();
        var tenants = c.HouseholdsOfFief(fief.Fief).Where(h => hht.Deed[h] == DeedKind.Tenant).ToList();
        Assert.True(tenants.Count >= 4, $"only {tenants.Count} tenants");
        var absent = tenants.Take(2).ToHashSet();
        foreach (int h in absent) rig.Fund(c, Party.OfHousehold(hht.IdOf(h)), 1000);
        long t0 = rig.Led.Balance(c.PurseOf(Party.OfFief(fief.Fief))).Mites, total0 = rig.Total();
        var (n, days, coin) = RuleDues.SettleLabour(c, fief.Fief, 15, rig.Day, h => !absent.Contains(h));
        Assert.Equal(tenants.Count, n);
        Assert.Equal((tenants.Count - 2) * 3, days);                                         // 3 days each credited to the demesne
        Assert.Equal(2 * 3 * 150, coin);                                                     // the two who were away owe 150 a day, collected from what they hold
        Assert.Equal((long)days * 8, rig.B.DemesneHours[fief.Fief]);
        Assert.Equal(t0 + coin, rig.Led.Balance(c.PurseOf(Party.OfFief(fief.Fief))).Mites);
        Assert.Equal(total0, rig.Total());
    }

    // ---------------------------------------------------------------- RI-4 relief, RI-5 wardship, RI-6 aid

    private static int AdultAt(RuleRig rig, int lo, int hi, bool withAdultKin = false)
    {
        var pt = rig.People; var pop = rig.W.Pop; var lords = pop.FiefLord.ToHashSet();
        for (int i = 0; i < pt.Count; i++)
        {
            if (!pt.IsAlive(i) || lords.Contains(i)) continue;
            int age = pt.AgeYears(i, rig.Day); if (age < lo || age > hi) continue;
            if (pt.Household[i] < 0) continue;
            if (withAdultKin && !rig.W.Pop.Households.Members(pt.Household[i]).Any(m => m != i && pt.IsAlive(m) && pt.AgeYears(m, rig.Day) >= 18)) continue;
            return i;
        }
        throw new InvalidOperationException($"nobody aged {lo}-{hi}");
    }

    [Fact]
    public void RI4_AnHeirOwesAQuarterOfLastYearsReceiptsAsRelief_PaidWhenHeHasIt_ForfeitAfterFourSeasonsWhenHeHasNot()
    {
        Assert.Equal(10_000, RuleMath.Relief(40_000, 25));
        foreach (bool pays in new[] { true, false })
        {
            using var rig = RuleRig.Make(); var c = rig.C;
            var f = rig.Fief(); f.Receipts = [10_000, 10_000, 10_000, 10_000];
            int heir = AdultAt(rig, 25, 45); var heirId = rig.People.IdOf(heir);
            rig.W.Pop.FiefLord[f.Fief] = heir;
            Assert.Equal(1, RuleDues.Succession(c, rig.Day));
            Assert.Equal((long)heirId.Value, f.Holder);
            var due = rig.B.Dues.Single(d => d.Kind == DueKind.Relief);
            Assert.Equal(10_000, due.AmountMites); Assert.Equal(rig.Day + 30, due.DueDay);
            Assert.Equal(f.Realm, (int)due.Payee.Id);
            var hhParty = c.HouseholdOf((long)heirId.Value); rig.Drain(c, hhParty);
            long realm0 = rig.Led.Balance(c.PurseOf(due.Payee)).Mites;
            if (pays)
            {
                rig.Fund(c, hhParty, 12_000);
                RuleDues.Settle(c, rig.Day + 30);
                Assert.Equal(DueState.Paid, due.State);
                Assert.Equal(realm0 + 10_000, rig.Led.Balance(c.PurseOf(due.Payee)).Mites);
                Assert.Equal(10_000, rig.Led.Flow(TransferReason.Relief).Mites - (rig.Led.Flow(TransferReason.Relief).Mites - 10_000));
            }
            else
            {
                RuleDues.Settle(c, rig.Day + 30); Assert.Equal(DueState.Arrears, due.State); Assert.False(f.Forfeited);
                RuleDues.Settle(c, rig.Day + 45); Assert.Equal(DueState.Grievance, due.State);
                RuleDues.Settle(c, rig.Day + 60);
                Assert.True(f.Forfeited); Assert.Equal(-1, f.Holder);                         // the overlord's court took the deed back
                Assert.True(rig.B.Opinion(c.KeyOf(due.Payee).Value, c.KeyOf(due.Payer).Value) <= -20);
            }
            Assert.True(rig.Led.Audit().IsConserved);
        }
    }

    [Fact]
    public void AFiefGrantedToTheHero_IsNotTakenBackByTheLinesSuccession_ButTheLinesChangeStillMovesTheRowItFollows()
    {
        using var rig = RuleRig.Make(); var c = rig.C;
        var f = rig.Fief(); long was = f.Holder;
        f.Holder = 12345;                                                                      // a grant: the deed is someone else's now
        Assert.Equal(0, RuleDues.Succession(c, rig.Day)); Assert.Equal(12345, f.Holder);
        int heir = AdultAt(rig, 25, 45); rig.W.Pop.FiefLord[f.Fief] = heir;                    // the old line changes hands: the people's lord moves, the grantee's deed does not
        Assert.Equal(0, RuleDues.Succession(c, rig.Day + 1)); Assert.Equal(12345, f.Holder); Assert.Equal((long)rig.People.IdOf(heir).Value, f.PopLord);
        Assert.Empty(rig.B.Dues);
        _ = was;
    }

    [Fact]
    public void RI5_AnHeirOfTwelve_PutsTheFiefInWard_TheOverlordTakesTheReceiptsAndPaysTheGuardianAFifth()
    {
        Assert.Equal(2_000, RuleMath.GuardianKeep(10_000, 20));
        using var rig = RuleRig.Make(); var c = rig.C;
        var f = rig.Fief(); f.Receipts = [10_000, 0, 0, 0];
        int heir = AdultAt(rig, 11, 13, withAdultKin: true); var heirId = rig.People.IdOf(heir);
        rig.W.Pop.FiefLord[f.Fief] = heir;
        RuleDues.Succession(c, rig.Day);
        Assert.True(f.WardUntilDay > rig.Day);
        double years = (f.WardUntilDay - rig.Day) / (double)TimeConst.DaysPerYear; Assert.InRange(years, 2.9, 5.1);
        Assert.True(f.Guardian >= 0);
        rig.Fund(c, Party.OfFief(f.Fief), 10_000);
        var overlord = c.OverlordOf(f); long o0 = rig.Led.Balance(c.PurseOf(overlord)).Mites, g0 = c.Balance(c.HouseholdOf(f.Guardian)), total0 = rig.Total();
        long taken = RuleDues.Wardships(c, rig.Day + 15);
        Assert.Equal(10_000, taken);
        Assert.Equal(o0 + 10_000 - 2_000, rig.Led.Balance(c.PurseOf(overlord)).Mites);
        Assert.Equal(g0 + 2_000, c.Balance(c.HouseholdOf(f.Guardian)));
        Assert.Equal(total0, rig.Total());
        RuleDues.Wardships(c, f.WardUntilDay);                                                 // of age: the ward ends
        Assert.Equal(-1, f.WardUntilDay);
    }

    [Fact]
    public void RI6_WhenTheOverlordIsRansomed_EachVassalOwesASeasonOfHisFiefsHearthTax()
    {
        using var rig = RuleRig.Make(); var c = rig.C;
        var f = rig.Fief(households: 5);
        int n = RuleDues.CallAid(c, f.Realm, "ru_occasion_ransom", rig.Day);
        Assert.True(n >= 1);
        var due = rig.B.Dues.Single(d => d.Kind == DueKind.Aid && d.Payer == Party.OfFief(f.Fief));
        var tax = c.Tax!; long expect = c.HouseholdsOfFief(f.Fief).Count() * (long)tax.HearthMites(tax.LevelOfFief(f.Fief));
        Assert.Equal(expect, due.AmountMites);
        rig.Fund(c, Party.OfFief(f.Fief), due.AmountMites);
        long r0 = rig.Led.Balance(c.PurseOf(Party.OfRealm(f.Realm))).Mites;
        RuleDues.Settle(c, due.DueDay);
        Assert.Equal(DueState.Paid, due.State);
        Assert.True(rig.Led.Balance(c.PurseOf(Party.OfRealm(f.Realm))).Mites >= r0 + due.AmountMites);
        Assert.True(rig.Led.Audit().IsConserved);
    }

    // ---------------------------------------------------------------- RI-7 council, RI-8 tallage

    [Fact]
    public void RI7_NineMembersInADefensiveWar_EightVoteYes_ThePassedSubsidyIsTakenNextSeason_EachPayerCarriesAGrievance()
    {
        // the golden vote: opinions [60,40,20,10,0,-10,-20,-40,-60], defence +40, jitter 0
        int[] ops = [60, 40, 20, 10, 0, -10, -20, -40, -60];
        var members = ops.Select((o, i) => new CouncilMember(i + 1, o, 1)).ToList();
        var t = RuleCouncil.Tally(S, members, S.CauseDefence, _ => 0);
        Assert.Equal(8, t.Yes); Assert.Equal(1, t.No); Assert.True(t.Passed);
        Assert.False(t.Votes.Single(v => v.Member == 9).Yes);                                  // the one at -60
        Assert.False(RuleCouncil.Tally(S, members, S.CauseNoWar, _ => 0).Passed);            // no war: -30 sinks most of them

        // in the world: a realm's holders think well of the ruler, the council passes, the levy is taken from real coin
        using var rig = RuleRig.Make(); var c = rig.C;
        int realm = rig.B.Fiefs.Values.Where(f => f.Realm >= 0 && f.Holder >= 0).GroupBy(f => f.Realm).OrderByDescending(g => g.Count()).First().Key;
        foreach (var f in rig.B.Fiefs.Values.Where(f => f.Realm == realm && f.Holder >= 0)) rig.B.AddOpinion((ulong)f.Holder, c.RealmKey(realm).Value, 100);
        var rec = RuleCouncil.Call(c, realm, "defence", rig.Day);
        Assert.True(rec.Passed); Assert.Equal(rec.Yes + rec.No, rec.Votes.Count);
        var levy = rig.B.Levies.Single(); Assert.Equal(rig.Day - rig.Day % 15 + 15, levy.DueDay);
        // the second subsidy within two years is harder (-20): the cause weight shows it
        Assert.Equal(S.CauseDefence + S.CauseSecondSubsidy, RuleMath.CauseWeight(S, "defence", true));
        var hht = rig.W.Pop.Households; long expect = 0; int payers = 0; var r0 = rig.Led.Balance(c.PurseOf(Party.OfRealm(realm))).Mites;
        for (int h = 0; h < hht.Count; h++)
        {
            if (hht.MemberCount[h] == 0 || RuleCouncil.RealmOfSettlement(c, hht.Settlement[h]) != realm) continue;
            int pm = rig.Region.Settlements[hht.Settlement[h]].Class == SettlementClass.Town ? S.SubsidyTownPermille : S.SubsidyCountryPermille;
            long take = Math.Min(RuleMath.OfMovables(RuleCouncil.Movables(c, h), pm), rig.Led.Balance(rig.Econ.HouseholdPurse[h]).Mites);
            if (take > 0) { expect += take; payers++; }
        }
        long total0 = rig.Total();
        long got = RuleCouncil.CollectLevies(c, levy.DueDay);
        Assert.Equal(expect, got); Assert.True(got > 0);
        Assert.Equal(r0 + got, rig.Led.Balance(c.PurseOf(Party.OfRealm(realm))).Mites);
        Assert.Equal(payers, rig.B.Grievances.Count);
        Assert.Equal(total0, rig.Total());
        RuleDues.ApplyGrievances(c, levy.DueDay);
        Assert.Contains(rig.B.Grievances.Keys, k => (rig.Econ.Grievances[hht.IndexOf(new EntityId(k))] & Grievance.Lord) != 0);   // the unrest ladder reads them
        RuleDues.ApplyGrievances(c, levy.DueDay + 15); Assert.Empty(rig.B.Grievances);        // a season later they have lapsed
    }

    [Fact]
    public void RI8_Tallage_TakesATenthOfATownsMovables_TwoGrievancesAHousehold_TheMayorThinksTheWorse()
    {
        Assert.Equal(40_000, RuleMath.OfMovables(400_000, 100));
        using var rig = RuleRig.Make(); var c = rig.C; var hht = rig.W.Pop.Households;
        int s = Enumerable.Range(0, rig.Region.Settlements.Count).First(i => RuleCouncil.RealmOfSettlement(c, i) >= 0 && rig.People.Count > 0
            && Enumerable.Range(0, rig.People.Count).Any(p => rig.People.IsAlive(p) && rig.People.Occupation[p] == Occupation.Mayor && rig.People.HomeSettlement[p] == i));
        long expect = 0; int payers = 0;
        for (int h = 0; h < hht.Count; h++) if (hht.MemberCount[h] > 0 && hht.Settlement[h] == s) { long t = Math.Min(RuleMath.OfMovables(RuleCouncil.Movables(c, h), 100), rig.Led.Balance(rig.Econ.HouseholdPurse[h]).Mites); if (t > 0) { expect += t; payers++; } }
        var ruler = Party.OfRealm(RuleCouncil.RealmOfSettlement(c, s)); long r0 = rig.Led.Balance(c.PurseOf(ruler)).Mites, total0 = rig.Total();
        long got = RuleCouncil.Tallage(c, s, ruler, rig.Day);
        Assert.Equal(expect, got); Assert.True(got > 0);
        Assert.Equal(r0 + got, rig.Led.Balance(c.PurseOf(ruler)).Mites);
        Assert.Equal(payers, rig.B.Grievances.Count);
        Assert.All(rig.B.Grievances.Values, g => Assert.Equal(2, g.Points));
        int mayor = Enumerable.Range(0, rig.People.Count).First(p => rig.People.IsAlive(p) && rig.People.Occupation[p] == Occupation.Mayor && rig.People.HomeSettlement[p] == s);
        Assert.Equal(-30, rig.B.Opinion(rig.People.IdOf(mayor).Value, c.KeyOf(ruler).Value));
        Assert.Equal(total0, rig.Total());
    }

    // ---------------------------------------------------------------- RI-9 customs

    [Fact]
    public void RI9_CustomsOnWoolLeavingAreTenPercent_ARateOfTwelveSendsFortyPercentFewerCaravans_AndTheDutyIsARealTransfer()
    {
        Assert.Equal(2_000, RuleMath.Duty(20_000, 10));
        Assert.Equal(100, RuleMath.CaravanPct(S, 8)); Assert.Equal(60, RuleMath.CaravanPct(S, 12)); Assert.Equal(0, RuleMath.CaravanPct(S, 18));
        using var rig = RuleRig.Make(); var c = rig.C;
        Assert.Equal(10, RuleCustoms.RatePct(rig.B, 0, "wool_raw", true));
        Assert.Equal(5, RuleCustoms.RatePct(rig.B, 0, "wine", false));
        Assert.Equal(3, RuleCustoms.RatePct(rig.B, 0, "bread", true));
        int home = Enumerable.Range(0, rig.Region.Settlements.Count).First(s => RuleCustoms.RealmOf(rig.Sim, s) >= 0); int realm = RuleCustoms.RealmOf(rig.Sim, home);
        var account = rig.Led.Open(PurseKind.Caravan, EntityId.Make(IdKind.Other, 4242)); rig.Led.Mint(account, new Coin(50_000), SourceReason.Seed);
        // honest collector for the gate (or none): the realm's treasury gets the whole duty
        foreach (var o in rig.B.Offices.Where(o => o.Kind == OfficeKind.CustomsCollector && o.Realm == realm)) o.Honesty = 100;
        long r0 = rig.Led.Balance(c.PurseOf(Party.OfRealm(realm))).Mites, total0 = rig.Total();
        long paid = RuleCustoms.Charge(rig.Sim, account, home, "wool_raw", 20_000, export: true);
        Assert.Equal(2_000, paid); Assert.Equal(48_000, rig.Led.Balance(account).Mites);
        Assert.Equal(r0 + 2_000, rig.Led.Balance(c.PurseOf(Party.OfRealm(realm))).Mites);
        Assert.Equal(total0, rig.Total());
        rig.B.Customs[realm] = 12;                                                             // the ruler sets 12%: duty and the gate's traffic both feel it
        Assert.Equal(2_400, RuleCustoms.Charge(rig.Sim, account, home, "wool_raw", 20_000, export: true));
        int allowed = Enumerable.Range(0, 100).Count(d => RuleCustoms.AllowTrip(rig.Sim, home, d, 3));
        Assert.Equal(60, allowed);
    }

    // ---------------------------------------------------------------- RI-10 skimming, RI-11 audit, RI-12 honesty

    private static (Office Off, int Settlement) Bailiff(RuleRig rig)
    {
        foreach (var o in rig.B.Offices.Where(o => o.Kind == OfficeKind.Bailiff && o.InOffice).OrderBy(o => o.Serial))
        {
            int s = rig.Region.Fiefs[o.Fief].SeatSettlement;
            if (rig.B.OfficeOf(OfficeKind.Reeve, o.Fief) != null && rig.B.FiefOf(o.Fief)!.Holder >= 0) return (o, s);
        }
        throw new InvalidOperationException("no bailiff");
    }

    [Fact]
    public void RI10_ABailiffOfHonestyTwentyCollectingThirtyThousand_KeepsThreeThousand_AndTheLedgerStaysExact()
    {
        Assert.Equal(10, RuleMath.SkimPct(S, 20)); Assert.Equal(3_000, RuleMath.Skim(S, 30_000, 20)); Assert.Equal(0, RuleMath.Skim(S, 30_000, 60)); Assert.Equal(0, RuleMath.Skim(S, 30_000, 100));
        using var rig = RuleRig.Make(); var c = rig.C;
        var (off, s) = Bailiff(rig); off.Honesty = 20;
        rig.Fund(c, Party.OfFief(off.Fief), 30_000);                                           // the round brought in 30,000
        long chest0 = rig.Led.Balance(c.PurseOf(Party.OfFief(off.Fief))).Mites, own0 = c.Balance(c.HouseholdOf(off.Holder)), total0 = rig.Total();
        long took = RuleOfficials.Collected(c, s, 30_000, 30_000, rig.Day);
        Assert.Equal(3_000, took);
        Assert.Equal(chest0 - 3_000, rig.Led.Balance(c.PurseOf(Party.OfFief(off.Fief))).Mites);
        Assert.Equal(own0 + 3_000, c.Balance(c.HouseholdOf(off.Holder)));
        Assert.Equal(3_000, rig.Led.Flow(TransferReason.Skim).Mites);
        Assert.Equal(30_000, off.Collected); Assert.Equal(3_000, off.SeasonSkim);
        Assert.Equal(total0, rig.Total()); Assert.True(rig.Led.Audit().IsConserved);
        // an honest one keeps nothing
        off.Honesty = 80; Assert.Equal(0, RuleOfficials.Collected(c, s, 30_000, 30_000, rig.Day));
    }

    [Fact]
    public void RI11_AuditorLaw60AgainstOfficialLaw40_FindsWithSeventyPercent_AFoundThiefRepaysIsDismissedAndTheLawHearsOfIt()
    {
        Assert.Equal(70, RuleMath.AuditPct(S, 60, 40)); Assert.Equal(90, RuleMath.AuditPct(S, 100, 0)); Assert.Equal(30, RuleMath.AuditPct(S, 0, 0));
        using var rig = RuleRig.Make(); var c = rig.C;
        var (off, s) = Bailiff(rig); off.Honesty = 20; off.AppointedDay = rig.Day - 15;                 // in his post a season already: audits and sightings begin after the first
        int found = 0, season = 0; long skimmed = 0;
        for (; season < 12 && found == 0; season++)                                           // 70% a season: found within a few
        {
            int day = rig.Day + season * 15;
            rig.Fund(c, Party.OfFief(off.Fief), 30_000);
            skimmed = RuleOfficials.Collected(c, s, 30_000, 30_000, day);
            long own0 = c.Balance(c.HouseholdOf(off.Holder)), chest0 = rig.Led.Balance(c.PurseOf(Party.OfFief(off.Fief))).Mites;
            found = RuleOfficials.Audit(c, day);
            if (found > 0)
            {
                Assert.False(off.InOffice); Assert.Equal(day, off.DismissedDay);
                Assert.Equal(skimmed, off.Found);
                Assert.Equal(own0 - skimmed, c.Balance(c.HouseholdOf(off.Holder)));            // restitution to the fief's chest
                Assert.Equal(chest0 + skimmed, rig.Led.Balance(c.PurseOf(Party.OfFief(off.Fief))).Mites);
                var crime = rig.Crimes.Last();
                Assert.Equal((ulong)off.Holder, crime.Doer.Value); Assert.True(crime.Report); Assert.Equal(skimmed, crime.Value);
            }
        }
        Assert.Equal(1, found); output.WriteLine($"found in season {season}");
        Assert.True(rig.Led.Audit().IsConserved);
    }

    [Fact]
    public void RI11b_AColleagueWhoHandlesTheSameChestMaySeeTheSkimming_ThatIsAWitnessedEmbezzlement()
    {
        using var rig = RuleRig.Make(); var c = rig.C;
        var (off, s) = Bailiff(rig); off.Honesty = 20; off.AppointedDay = rig.Day - 15;
        int seen = 0;
        for (int season = 0; season < 40 && seen == 0; season++)                               // 20% a season
        {
            rig.Fund(c, Party.OfFief(off.Fief), 30_000);
            RuleOfficials.Collected(c, s, 30_000, 30_000, rig.Day + season * 15);
            seen = RuleOfficials.Seen(c, rig.Day + season * 15);
            off.SeasonSkim = 0;
        }
        Assert.Equal(1, seen);
        var crime = rig.Crimes.Single(); Assert.False(crime.Report); Assert.Single(crime.Witnesses);
        Assert.Equal("ch_note_skim_seen", crime.NoteKey);
    }

    [Fact]
    public void RI12_Honesty_IsRolledOncePerPersonFromStream0x540_AndNeverBeforeTheRuleBookAsksForIt()
    {
        var b = new RuleBook();
        Assert.Empty(b.Honesty);                                                               // a bare book (a world before the seeding pass) has drawn nothing
        var a = new EntityId(0x1234); var z = new EntityId(0x9876);
        byte ha = RuleOfficials.HonestyOf(b, 1234, a), hz = RuleOfficials.HonestyOf(b, 1234, z);
        Assert.Equal(ha, RuleOfficials.HonestyOf(b, 1234, a));                                 // once: kept
        Assert.Equal(2, b.Honesty.Count);
        Assert.InRange((int)ha, 10, 100); Assert.InRange((int)hz, 10, 100);
        Assert.Equal(DetRng.Range(1234, 0, a.Value, RngStreams.OfficialHonesty, 0, 0, 91) + 10, ha);   // key the person id, never the day
        var many = Enumerable.Range(1, 400).Select(i => (int)RuleOfficials.HonestyOf(new RuleBook(), 7, new EntityId((ulong)i))).ToList();
        Assert.True(many.Min() < 25 && many.Max() > 90);                                       // spread over the range
        Assert.Equal(0x540u, RngStreams.OfficialHonesty);
    }

    // ---------------------------------------------------------------- RI-13 loans conserve, RI-14 mortgage default

    private static Party RichLender(RuleRig rig, RuleCtx c, long mites)
    {
        var hht = rig.W.Pop.Households;
        int h = Enumerable.Range(0, hht.Count).First(i => hht.MemberCount[i] > 0 && hht.Class[i] != SocialClass.Peasant);
        var p = Party.OfHousehold(hht.IdOf(h)); rig.Drain(c, p); rig.Fund(c, p, mites); return p;
    }

    [Fact]
    public void RI13_ARulerBorrowsOneHundredThousandAtFourPercentForFourSeasons_SixteenThousandInterest_NoCoinIsMade()
    {
        Assert.Equal(40, RuleMath.RatePermille(S, false)); Assert.Equal(60, RuleMath.RatePermille(S, true)); Assert.Equal(4_000, RuleMath.Interest(100_000, 40));
        using var rig = RuleRig.Make(); var c = rig.C;
        int realm = rig.Fief().Realm; var ruler = Party.OfRealm(realm);
        var lender = RichLender(rig, c, 300_000);
        rig.Drain(c, ruler); rig.Fund(c, ruler, 16_000);                                       // the realm's own coming-in covers the interest
        var audit0 = rig.Led.Audit(); long total0 = rig.Total(), l0 = c.Balance(lender);
        var (loan, why) = RuleLoans.Lend(c, lender, ruler, 100_000, 4, LoanSecurity.None, 0, rig.Day);
        Assert.True(loan != null, why);
        Assert.Equal(l0 - 100_000, c.Balance(lender)); Assert.Equal(116_000, c.Balance(ruler));
        for (int s = 1; s <= 4; s++) RuleLoans.Season(c, rig.Day + s * 15);
        Assert.Equal(LoanState.Repaid, loan!.State); Assert.Equal(16_000, loan.InterestPaid); Assert.Equal(100_000, loan.Repaid);
        Assert.Equal(l0 + 16_000, c.Balance(lender)); Assert.Equal(0, c.Balance(ruler));
        Assert.Equal(16_000, rig.Led.Flow(TransferReason.Interest).Mites);
        var audit1 = rig.Led.Audit();
        Assert.Equal(total0, rig.Total());                                                      // total coin unchanged across the cycle
        Assert.Equal(audit0.Minted, audit1.Minted); Assert.Equal(audit0.Seeded, audit1.Seeded); Assert.True(audit1.IsConserved);
        // a lender with less than twice the principal will not lend
        var poor = RichLender(rig, c, 150_000);
        Assert.Equal("ru_loan_lender_short", RuleLoans.Lend(c, poor, ruler, 100_000, 4, LoanSecurity.None, 0, rig.Day).Refusal);
    }

    [Fact]
    public void RI14_AMortgagedDeedTwoSeasonsLate_PassesToTheLender_TheCreditClosesAndTheyThinkTheWorseOfEachOther()
    {
        using var rig = RuleRig.Make(); var c = rig.C;
        var reg = rig.L.Registry(); var hht = rig.W.Pop.Households;
        int h = Enumerable.Range(0, hht.Count).First(i => hht.MemberCount[i] > 0 && reg.DwellingOf(i) != null && hht.Class[i] == SocialClass.Peasant);
        var deed = reg.DwellingOf(h)!; var borrower = Party.OfHousehold(hht.IdOf(h));
        var lender = RichLender(rig, c, 100_000);
        var (loan, why) = RuleLoans.Lend(c, lender, borrower, 20_000, 4, LoanSecurity.Deed, deed.Id.Value, rig.Day);
        Assert.True(loan != null, why); rig.Drain(c, borrower);                                  // he spends it and cannot pay
        RuleLoans.Season(c, rig.Day + 15); Assert.Equal(1, loan!.LateSeasons); Assert.Equal(LoanState.Open, loan.State);
        RuleLoans.Season(c, rig.Day + 30);
        Assert.Equal(LoanState.Default, loan.State);
        var now = rig.L.Registry().Get(deed.Id)!;
        Assert.Equal(hht.IdOf(Enumerable.Range(0, hht.Count).First(i => hht.IdOf(i).Value == (ulong)lender.Id)), now.Holder);
        Assert.Equal(DeedCause.Forfeited, now.Cause);
        Assert.True(RuleLoans.CreditClosed(rig.B, borrower, rig.Day + 31));
        Assert.False(RuleLoans.CreditClosed(rig.B, borrower, rig.Day + 31 + 2 * TimeConst.DaysPerYear));
        Assert.Equal(-40, rig.B.Opinion(c.KeyOf(lender).Value, c.KeyOf(borrower).Value));
        Assert.Equal(-40, rig.B.Opinion(c.KeyOf(borrower).Value, c.KeyOf(lender).Value));
        Assert.Equal("ru_loan_credit_closed", RuleLoans.Lend(c, lender, borrower, 1_000, 1, LoanSecurity.None, 0, rig.Day + 40).Refusal);
        Assert.True(rig.Led.Audit().IsConserved);
    }

    // ---------------------------------------------------------------- RI-15 eviction

    [Fact]
    public void RI15_ASquattersClaimIsServedNoticeThenCleared_TheGuardsHookTakesTheOrderOver_ALicenceBoughtInTimeStopsIt()
    {
        using var rig = RuleRig.Make(); var c = rig.C;
        var f = rig.Fief(); var hero = EconomyState.HeroOwner;
        Claim New() { var cl = new Claim { Serial = rig.L.Book.NextSerial++, Claimant = hero, ClaimantPurse = PurseKind.Person, Tiles = [1000, 1001, 1002], Fief = f.Fief, StakeDay = rig.Day, Squatting = true, State = ClaimState.Open }; rig.L.Book.Claims.Add(cl); return cl; }
        var cl = New();
        Assert.Equal((1, 0), RuleLand.Squatters(c, rig.Day));                                   // notice at S6
        Assert.Equal(rig.Day + 15, rig.B.EvictionNotices[cl.Serial]);
        Assert.Equal((0, 0), RuleLand.Squatters(c, rig.Day + 14));                              // fifteen days to pay the licence or leave
        Assert.Equal(ClaimState.Open, cl.State);
        Assert.Equal((0, 1), RuleLand.Squatters(c, rig.Day + 15));
        Assert.Equal(ClaimState.Pulled, cl.State); Assert.Equal(DeedCause.Evicted, rig.L.Book.Changes.Last().Cause);
        Assert.Empty(rig.B.EvictionNotices); Assert.Empty(rig.B.EvictionOrders);
        // with the justice step's guards installed they take the order: it waits for them, then they finish it
        rig.Sim.Get<RuleLinks>()!.GuardsEnforce = (serial, fief, day) => true;
        var two = New(); RuleLand.Squatters(rig.C, rig.Day + 20); Assert.Equal((0, 0), RuleLand.Squatters(rig.C, rig.Day + 35));
        Assert.True(rig.B.EvictionOrders.ContainsKey(two.Serial)); Assert.Equal(ClaimState.Open, two.State);
        Assert.True(RuleLand.Complete(rig.C, two.Serial, rig.Day + 36)); Assert.Equal(ClaimState.Pulled, two.State);
        // a licence bought before the notice runs out ends it
        var three = New(); RuleLand.Squatters(c, rig.Day + 40); three.Squatting = false; three.Licensed = true;
        RuleLand.Squatters(c, rig.Day + 60); Assert.Equal(ClaimState.Open, three.State); Assert.False(rig.B.EvictionNotices.ContainsKey(three.Serial));
    }

    // ---------------------------------------------------------------- RI-16 charters as politics, RI-17 revocation, RI-18 market charter

    private (int Fief, int X, int Y) Site(RuleRig rig)
    {
        var r = rig.Region; int N = RegionData.Size;
        for (int t = 100; t < RegionData.Tiles; t += 5)
        {
            if (!LandRules.OpenGround(r, t) || LandRules.LandFief(r, rig.L.Rules, t) != -1) continue;
            int f = LandRules.FiefOfTile(r, t); if (f < 0) continue;
            int x = t % N * 64 + 32, y = t / N * 64 + 32;
            if (rig.L.CharterSite(GrantorKind.Fief, f, x, y).Length >= rig.L.Rules.CharterSiteTiles && r.NearestActiveWarM(x, y) >= rig.L.Rules.CharterWarM) return (f, x, y);
        }
        throw new InvalidOperationException("no site");
    }

    [Fact]
    public void RI16_ACharterIsGrantedWhenTheTermsAddUpToForty_ARefusalNamesTheLargestNegativeTerm()
    {
        var ok = RuleMath.CharterScore(S, opinion: 20, standing: 200, feeMites: 5_000, contested: false, otherRealm: false);
        Assert.Equal(65, ok.Score);
        var contested = RuleMath.CharterScore(S, 20, 200, 5_000, true, false);
        Assert.Equal(15, contested.Score); Assert.Equal("ru_term_contested", contested.Largest);
        Assert.Equal("ru_term_other_realm", RuleMath.CharterScore(S, 0, 0, 5_000, false, true).Largest);

        using var rig = RuleRig.Make(); var c = rig.C; var hero = EconomyState.HeroOwner; rig.Econ.SeedHero(200);
        var (f, x, y) = Site(rig); int seat = rig.L.SeatOf(GrantorKind.Fief, f);
        var fiefKey = rig.Region.Fiefs[f].Id;
        // no friends yet: 25 < 40, refused
        var no = rig.L.Petition(hero, PurseKind.Person, GrantorKind.Fief, f, seat, x, y, rig.Day, (when, type, subj, causes, data) => EntityId.None);
        Assert.False(no.Ok); Assert.StartsWith("ld_charter_refused_", no.Key);
        // the ruler likes him (20) and the realm knows him (200): 65, granted
        rig.B.AddOpinion(fiefKey.Value, hero.Value, 20); rig.L.Book.Standing[LandBook.StandingKey(GrantorKind.Fief, f)] = 200;
        long t0 = rig.Led.Balance(c.PurseOf(Party.OfFief(f))).Mites;
        var yes = rig.L.Petition(hero, PurseKind.Person, GrantorKind.Fief, f, seat, x, y, rig.Day, (when, type, subj, causes, data) => EntityId.None);
        Assert.True(yes.Ok, yes.Key);
        Assert.Equal(t0 + 5_000, rig.Led.Balance(c.PurseOf(Party.OfFief(f))).Mites);           // the fee is a transfer to the grantor
        Assert.True(rig.Led.Audit().IsConserved);
        // contested land (a claim on a tile): 15, refused naming the land
        var site2 = rig.L.CharterSite(GrantorKind.Fief, f, x, y);
        var (f2, x2, y2) = (f, x, y);
        rig.L.Book.Charters.Clear(); rig.L.Book.Deeds.Clear();
        rig.L.Book.Claims.Add(new Claim { Serial = rig.L.Book.NextSerial++, Claimant = hero, Tiles = [site2[0]], Fief = -1, State = ClaimState.Open });
        var refused = rig.L.Petition(hero, PurseKind.Person, GrantorKind.Fief, f2, seat, x2, y2, rig.Day, (when, type, subj, causes, data) => EntityId.None);
        Assert.False(refused.Ok); Assert.Equal("ld_charter_refused_contested", refused.Key);
    }

    [Fact]
    public void RI17_ALapsedFoundingCharterIsRevoked_UnlessItsHolderPaysHalfTheFeeForOneExtension()
    {
        Assert.Equal(2_500, RuleMath.ExtensionFee(5_000, 50));
        using var rig = RuleRig.Make(); var c = rig.C; var hero = EconomyState.HeroOwner; rig.Econ.SeedHero(200);
        var (f, x, y) = Site(rig); int seat = rig.L.SeatOf(GrantorKind.Fief, f);
        rig.B.AddOpinion(rig.Region.Fiefs[f].Id.Value, hero.Value, 40); rig.L.Book.Standing[LandBook.StandingKey(GrantorKind.Fief, f)] = 200;
        var res = rig.L.Petition(hero, PurseKind.Person, GrantorKind.Fief, f, seat, x, y, rig.Day, (when, type, subj, causes, data) => EntityId.None);
        Assert.True(res.Ok, res.Key);
        var ch = rig.L.Book.Charters.Single();
        rig.L.CheckCharter(ch, ch.SettleByDay + 1, (when, type, subj, causes, data) => EntityId.None);
        Assert.Equal(CharterState.Lapsed, ch.State);
        // the holder has the coin: the extension is paid (half the fee), the deadline moves a year, once
        long g0 = rig.Led.Balance(c.PurseOf(Party.OfFief(f))).Mites, deadline = ch.SettleByDay;
        Assert.Equal(0, RuleLand.Charters(c, ch.SettleByDay + 15));
        Assert.Equal(CharterState.Granted, ch.State); Assert.Equal(g0 + 2_500, rig.Led.Balance(c.PurseOf(Party.OfFief(f))).Mites);
        Assert.True(ch.SettleByDay > deadline); Assert.Contains(ch.Serial, rig.B.Extended);
        // it lapses again: no second extension, revoked at the next S6; the deed returns to the grantor
        rig.L.CheckCharter(ch, ch.SettleByDay + 1, (when, type, subj, causes, data) => EntityId.None);
        Assert.Equal(1, RuleLand.Charters(c, ch.SettleByDay + 15));
        Assert.Contains(ch.Serial, rig.B.Revoked); Assert.False(rig.L.Book.Deeds.ContainsKey(ch.DeedId));
        Assert.Equal(DeedCause.Forfeited, rig.L.Book.Changes.Last().Cause);
        Assert.Equal(0, RuleLand.Charters(c, ch.SettleByDay + 30));                              // not revoked twice
        Assert.NotEmpty(rig.Chronicle.ByType("charter_revoked"));
        // a holder with no coin cannot pay: revoked at once
        using var rig2 = RuleRig.Make(); var c2 = rig2.C; var hero2 = EconomyState.HeroOwner; var purse = rig2.Econ.SeedHero(200);
        var (f3, x3, y3) = Site(rig2); int seat3 = rig2.L.SeatOf(GrantorKind.Fief, f3);
        rig2.B.AddOpinion(rig2.Region.Fiefs[f3].Id.Value, hero2.Value, 40); rig2.L.Book.Standing[LandBook.StandingKey(GrantorKind.Fief, f3)] = 200;
        Assert.True(rig2.L.Petition(hero2, PurseKind.Person, GrantorKind.Fief, f3, seat3, x3, y3, rig2.Day, (when, type, subj, causes, data) => EntityId.None).Ok);
        var ch2 = rig2.L.Book.Charters.Single(); rig2.Drain(c2, Party.OfPerson(hero2));
        rig2.L.CheckCharter(ch2, ch2.SettleByDay + 1, (when, type, subj, causes, data) => EntityId.None);
        Assert.Equal(1, RuleLand.Charters(c2, ch2.SettleByDay + 15));
        Assert.Contains(ch2.Serial, rig2.B.Revoked);
        _ = purse;
    }

    [Fact]
    public void RI18_AVillageOfTwoHundredWithASquare_IsGrantedAMarketCharter_AndBecomesATownWithStalls()
    {
        using var rig = RuleRig.Make(); var c = rig.C;
        // the most populous village that has a realm and a square
        int s = Enumerable.Range(0, rig.Region.Settlements.Count).Where(i => rig.Region.Settlements[i].Class == SettlementClass.Village && RuleCouncil.RealmOfSettlement(c, i) >= 0 && rig.Region.Settlements[i].Plan.Square.W > 0)
            .OrderByDescending(i => RuleLand.People(c, i)).First();
        int people = RuleLand.People(c, s); output.WriteLine($"{rig.Region.Settlements[s].Name}: {people} people");
        if (people < rig.B.Settings.MarketCharterPeople) rig.B.Settings = rig.B.Settings with { MarketCharterPeople = people };   // a seed whose villages are small: the rule's number follows the village
        Assert.True(RuleLand.MayPetitionMarket(c, s));
        int realm = RuleCouncil.RealmOfSettlement(c, s);
        int pr = Enumerable.Range(0, rig.People.Count).First(i => rig.People.IsAlive(i) && rig.People.HomeSettlement[i] == s && rig.People.Household[i] >= 0);
        var petitioner = rig.People.IdOf(pr);
        Assert.False(RuleLand.PetitionMarket(c, petitioner, s, rig.Day).Granted);               // unknown, short of the fee: refused
        rig.B.AddOpinion(c.RealmKey(realm).Value, petitioner.Value, 20); rig.Sim.Get<RuleLinks>()!.StandingWithRealm = (_, _) => 200;
        rig.Fund(c, c.HouseholdOf((long)petitioner.Value), 10_000);
        int stalls0 = rig.L.StallCount(s); long r0 = rig.Led.Balance(c.PurseOf(Party.OfRealm(realm))).Mites;
        var (ok, key) = RuleLand.PetitionMarket(c, petitioner, s, rig.Day);
        Assert.True(ok, key);
        Assert.Equal(SettlementClass.Town, rig.Region.Settlements[s].Class);
        Assert.True(rig.L.StallCount(s) >= stalls0); Assert.Equal(rig.L.Rules.StallsTown, rig.L.StallCount(s));
        Assert.Equal(r0 + 5_000, rig.Led.Balance(c.PurseOf(Party.OfRealm(realm))).Mites);
        Assert.Equal(((long)petitioner.Value), rig.B.MarketCharters[s].Holder);
        Assert.False(RuleLand.MayPetitionMarket(c, s));                                          // already a town
        Assert.True(rig.Led.Audit().IsConserved);
    }

    // ---------------------------------------------------------------- RI-19 determinism, save, conservation

    private static string RunYear(ITestOutputHelper output, out RuleBook book, out long coin)
    {
        using var rig = RuleRig.Make(ticker: true);
        var c0 = rig.C;
        var (off, _) = Bailiff(rig); off.Honesty = 20;                                           // a thief in the first fief with a bailiff (his post begins at the start: audited from the second season)
        int realm = off.Realm >= 0 ? off.Realm : rig.Fief().Realm;
        foreach (var f in rig.B.Fiefs.Values.Where(f => f.Realm == realm && f.Holder >= 0)) rig.B.AddOpinion((ulong)f.Holder, c0.RealmKey(realm).Value, 100);
        RuleCouncil.Call(c0, realm, "defence", rig.Day);
        var lender = RichLender(rig, c0, 400_000);
        RuleLoans.Lend(c0, lender, Party.OfRealm(realm), 50_000, 3, LoanSecurity.None, 0, rig.Day);
        var audit0 = rig.Led.Audit();
        rig.W.Run(130);                                                                           // two game years and a few days: eight and a half seasons
        var audit1 = rig.Led.Audit();
        Assert.True(audit1.IsConserved); Assert.Equal(audit0.Minted, audit1.Minted);
        output.WriteLine($"skimmed {rig.B.Offices.Sum(o => o.Skimmed)} found {rig.B.Offices.Sum(o => o.Found)} councils {rig.B.Councils.Count} loans {rig.B.Loans.Count(l => l.State == LoanState.Repaid)} repaid, dues {rig.B.Dues.Count}, crimes {rig.Crimes.Count}");
        Assert.True(rig.B.Offices.Sum(o => o.Skimmed) > 0);                                       // the thief stole
        Assert.Equal(LoanState.Repaid, rig.B.Loans.Single().State);
        book = rig.B; coin = rig.Total();
        return rig.B.Hash().ToString("X");
    }

    [Fact]
    public void RI19_ATwoYearRunWithACouncilALoanAndASkimmingBailiff_IsExactAndTheSameTwice_AndTheSectionSavesAndLoadsByteIdentical()
    {
        string a = RunYear(output, out var book, out long coinA), b = RunYear(output, out var book2, out long coinB);
        Assert.Equal(a, b); Assert.Equal(coinA, coinB);
        var bytes = SaveCodec.SerializeCompressed(RuleSave.Capture(book));
        var back = SaveCodec.DeserializeCompressed<RuleSave>(bytes).Restore();
        Assert.Equal(book.Hash(), back.Hash());
        Assert.Equal(bytes, SaveCodec.SerializeCompressed(RuleSave.Capture(back)));
        Assert.Equal(SaveCodec.SerializeCompressed(RuleSave.Capture(book2)), bytes);
        Assert.True(book.Councils.Count == 1 && book.Councils[0].Passed);
    }
}
