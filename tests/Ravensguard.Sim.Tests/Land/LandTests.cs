using Ravensguard.Content;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Goods;
using Ravensguard.Sim.Growth;
using Ravensguard.Sim.Land;
using Ravensguard.Sim.People;
using Ravensguard.Sim.Tests.Economy;
using Ravensguard.Sim.WorldGen.Region;
using Xunit.Abstractions;
using RegionData = Ravensguard.Sim.WorldGen.Region.Region;

namespace Ravensguard.Sim.Tests.Land;

/// <summary>The golden tests of docs/specs/phase5/land-and-deeds.md (LD-1 to LD-14; LD-2 is in <see cref="LandSaveTests"/>, the charters' LD-C1 to LD-C4 in <see cref="CharterTests"/>).</summary>
public class LandTests(ITestOutputHelper output)
{
    private static readonly LandRulesDef Rules = LandRules.From(EconFixture.Content);

    // ------------------------------------------------------------------ LD-1

    [Fact]
    public void LD1_TheRegistryFromGeneration_GivesEveryDwellingHouseholdOneDwellingDeed_AndDerivesTheDeedColumn()
    {
        var pop = EconFixture.Pop; var hht = pop.Households;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var reg = LandRegistry.Build(pop.Region, pop, new LandBook(), Rules);
        sw.Stop();
        int dwelling = 0, none = 0, bad = 0;
        // dwelling deeds: lot deeds whose holder is the household that lives on the lot (a lord also holds the empty lots of his place)
        var perHolder = reg.Deeds.Where(d => d.Kind == HoldingKind.Lot && d.HolderPurse == PurseKind.Household && LandIds.SplitLot(d.Subject) is var (s, l) && pop.LotHousehold[s][l] == hht.IndexOf(d.Holder))
            .GroupBy(d => d.Holder).ToDictionary(g => g.Key, g => g.Count());
        for (int h = 0; h < hht.Count; h++)
        {
            if (hht.Deed[h] != reg.DerivedDeedKind(h))
            {
                int ss = hht.Settlement[h], ll = hht.Lot[h];
                output.WriteLine($"hh {h} deed {hht.Deed[h]} derived {reg.DerivedDeedKind(h)} class {hht.Class[h]} s {ss} lot {ll} members {hht.MemberCount[h]} lh {(ll >= 0 ? pop.LotHousehold[ss][ll] : -9)} dw {(ll >= 0 ? pop.Region.Settlements[ss].Plan.Lots[ll].Dwelling.ToString() : "")} holder {(ll >= 0 ? reg.LotDeed(ss, ll)?.Holder.ToString() : "")} id {hht.IdOf(h)}");
                if (++bad > 10) break;
            }
            bool lives = hht.Lot[h] >= 0 && pop.LotHousehold[hht.Settlement[h]][hht.Lot[h]] == h && hht.MemberCount[h] > 0;
            if (!lives) { none++; continue; }
            dwelling++;
            Assert.NotNull(reg.DwellingOf(h));
            Assert.Equal(1, perHolder.GetValueOrDefault(hht.IdOf(h)));                     // exactly one dwelling deed
        }
        Assert.Equal(0, bad);
        output.WriteLine($"{hht.Count} households: {dwelling} with a dwelling deed, {none} without a dwelling; {reg.Deeds.Count} deeds built in {sw.ElapsedMilliseconds} ms");
        Assert.True(hht.Count >= 4900 && dwelling >= 4000);
        Assert.Equal(pop.Region.Parcels.Count, reg.Deeds.Count(d => d.Kind == HoldingKind.Parcel));
        // the same records give the same registry
        Assert.Equal(reg.ComputeHash(), LandRegistry.Build(pop.Region, pop, new LandBook(), Rules).ComputeHash());
    }

    // ------------------------------------------------------------------ LD-3

    [Fact]
    public void LD3_PlotPrices_AreTheSpecsNumbers()
    {
        Assert.Equal(46_200, LandRules.PlotPrice(Rules, 11 * 28, SettlementClass.Town, LandRules.DemandPm(Rules, 0)));
        Assert.Equal(48_510, LandRules.PlotPrice(Rules, 11 * 28, SettlementClass.Town, LandRules.DemandPm(Rules, 10)));
        Assert.Equal(24_320, LandRules.PlotPrice(Rules, 16 * 38, SettlementClass.Village, LandRules.DemandPm(Rules, 0)));
        Assert.Equal(1500, LandRules.DemandPm(Rules, 1000));                                // clamped
        Assert.Equal(300, LandRules.PlotDues(Rules, SettlementClass.Town, 308));
        Assert.Equal(Tenure.Burgage, LandRules.PlotTenure(SettlementClass.Town));
        Assert.Equal(Tenure.Freehold, LandRules.PlotTenure(SettlementClass.Village));
    }

    // ------------------------------------------------------------------ LD-4, LD-5

    [Fact]
    public void LD4_BuyingAListedPlot_PaysTheLordTheFullPrice_MakesABurgageDeed_AndConservesCoin()
    {
        using var rig = LandRig.Make(crowns: 500);
        int town = rig.Town(); var st = rig.Region.Settlements[town];
        var l = rig.L.Listings(town).First(x => x.Lot < 0);
        var (lordId, lordKind) = LandRegistry.LordOf(rig.W.Pop, town);
        int crowded = LandRules.CrowdedPct(Rules, rig.W.Pop.Households, town);
        long hero0 = rig.Coin, lord0 = rig.Bal(lordId, lordKind), trea0 = rig.Bal(rig.Region.Fiefs[st.FiefIndex].Id, PurseKind.Treasury);
        var audit0 = rig.Led.Audit();
        var r = rig.L.Buy(rig.Hero, PurseKind.Person, town, l.Lot, l.Rect, 0, rig.Day, rig.C);
        output.WriteLine($"{st.Name}: plot {l.Rect.W} x {l.Rect.H} m at {l.PriceMites} mites ({crowded}% crowded), dues {l.DuesMites}; {r.Key}");
        Assert.True(r.Ok);
        Assert.Equal(LandRules.PlotPrice(Rules, l.Rect.W * l.Rect.H, SettlementClass.Town, LandRules.DemandPm(Rules, crowded)), l.PriceMites);
        if (crowded == 0 && l.Rect.W * l.Rect.H == 308) Assert.Equal(46_200, l.PriceMites);
        Assert.Equal(hero0 - l.PriceMites, rig.Coin);
        Assert.Equal(lord0 + l.PriceMites, rig.Bal(lordId, lordKind));
        Assert.Equal(trea0, rig.Bal(rig.Region.Fiefs[st.FiefIndex].Id, PurseKind.Treasury));            // the treasury tops up nothing
        var d = rig.L.Book.Deeds[r.Deed];
        Assert.Equal((Tenure.Burgage, 300L, rig.Hero), (d.Tenure, d.DuesMitesPerSeason, d.Holder));
        Assert.True(rig.Led.Audit().IsConserved); Assert.Equal(audit0.InPurses, rig.Led.Audit().InPurses);
        // the plot is a lot of the plan now, in the growth log, and never offered again
        var (s, lot) = LandIds.SplitLot(d.Subject);
        Assert.Equal(l.Rect, rig.Region.Settlements[s].Plan.Lots[lot].Rect);
        Assert.Contains(rig.W.Economy.Growth.State.Lots, x => x.Settlement == s && x.Lot == lot && x.Dwelling == DwellingType.None);
        Assert.DoesNotContain(rig.L.Listings(town), x => x.Rect == l.Rect);
        Assert.Single(rig.Chron, e => e.TypeId == "land_bought");
        Assert.Equal(rig.Hero, rig.L.Registry().LotDeed(s, lot)!.Holder);
    }

    [Fact]
    public void LD5_TwoBuyersOfOnePlot_TheEarlierCommandWins_AndTheOtherGetsTheNext_Deterministically()
    {
        (string, string) Run()
        {
            using var rig = LandRig.Make(crowns: 2000);
            int v = rig.Village(); var list = rig.L.Listings(v); var first = list.First(x => x.Lot < 0);
            // a growth cottage start took the first plot (its project reserves it), then the hero asks for it: he gets the next
            rig.W.Economy.Growth.State.Projects.Add(new BuildProject { Serial = 999, Kind = ProjectKind.Cottage, Settlement = v, Lot = -1, Rect = first.Rect, Payer = 0 });
            var r1 = rig.L.Buy(rig.Hero, PurseKind.Person, v, -1, first.Rect, 0, rig.Day, rig.C);
            Assert.True(r1.Ok);
            var got1 = rig.L.Book.Deeds[r1.Deed].Rect;
            Assert.NotEqual(first.Rect, got1);
            // two buyers in one barrier: the hero, then a household, ask for the same plot: the household (later) gets another
            int town = rig.Town(); var tl = rig.L.Listings(town).First(x => x.Lot < 0);
            int hh = Enumerable.Range(0, rig.W.Pop.Households.Count).First(h => rig.W.Pop.Households.Settlement[h] == town && rig.W.Pop.Households.Class[h] == SocialClass.Burgher && rig.W.Pop.Households.MemberCount[h] > 0);
            rig.Led.Transfer(rig.HeroPurse, rig.W.Econ.HouseholdPurse[hh], new Coin(tl.PriceMites * 2), TransferReason.Gift);     // the household can pay
            var a = rig.L.Buy(rig.Hero, PurseKind.Person, town, -1, tl.Rect, 0, rig.Day, rig.C);
            var b = rig.L.Buy(rig.W.Pop.Households.IdOf(hh), PurseKind.Household, town, -1, tl.Rect, 0, rig.Day, rig.C);
            Assert.True(a.Ok && b.Ok);
            Assert.Equal(tl.Rect, rig.L.Book.Deeds[a.Deed].Rect);
            Assert.NotEqual(tl.Rect, rig.L.Book.Deeds[b.Deed].Rect);
            Assert.False(rig.L.Book.Deeds[a.Deed].Rect.Overlaps(rig.L.Book.Deeds[b.Deed].Rect));
            // a growth build after the sales never takes a sold plot
            foreach (var d in rig.L.Book.Deeds.Values) Assert.True(RegionGrowth.FindPlot(rig.Region, d.Settlement, 11, 28, 30, [], out var free, out _) && !free.Overlaps(d.Rect));
            Assert.True(rig.Led.Audit().IsConserved);
            return (rig.L.Book.ComputeHash(), rig.W.Econ.ComputeHash());
        }
        Assert.Equal(Run(), Run());
    }

    // ------------------------------------------------------------------ LD-6

    [Fact]
    public void LD6_May_BuildOnHisLot_FarmStructuresOnALeasedParcel_RefusedOnANeighboursLotWithTheirName()
    {
        using var rig = LandRig.Make(crowns: 2000);
        int v = rig.Village(); var pick = rig.L.Listings(v).First(x => x.Lot < 0);
        var bought = rig.L.Buy(rig.Hero, PurseKind.Person, v, -1, pick.Rect, 0, rig.Day, rig.C);
        int p = LeasableParcel(rig);
        var leased = rig.L.LeaseParcel(rig.Hero, PurseKind.Person, p, rig.Day, rig.C);
        Assert.True(bought.Ok && leased.Ok, leased.Key);
        var reg = rig.L.Registry();
        var q = pick.Rect;
        Assert.True(reg.May(rig.Hero, -1, LandAction.Build, q.CenterX, q.CenterY).Allowed);
        Assert.True(reg.May(rig.Hero, -1, LandAction.Demolish, q.CenterX, q.CenterY).Allowed);
        var field = rig.Region.Fields[rig.Region.Parcels[p].FieldStart]; int fx = field.Tile % RegionData.Size * RegionData.TileM + 32, fy = field.Tile / RegionData.Size * RegionData.TileM + 32;
        Assert.True(reg.May(rig.Hero, -1, LandAction.BuildFarm, fx, fy).Allowed);
        Assert.True(reg.May(rig.Hero, -1, LandAction.Farm, fx, fy).Allowed);
        var house = reg.May(rig.Hero, -1, LandAction.Build, fx, fy);
        Assert.False(house.Allowed); Assert.Equal("ld_may_farm_only", house.Why);
        // a neighbour's house: refused, naming the head of the household that lives there
        var hht = rig.W.Pop.Households;
        int nb = Enumerable.Range(0, hht.Count).First(h => hht.Settlement[h] == v && hht.Lot[h] >= 0 && hht.Head[h] >= 0 && rig.W.Pop.LotHousehold[v][hht.Lot[h]] == h && hht.Deed[h] == DeedKind.Owned);
        var nq = rig.Region.Settlements[v].Plan.Lots[hht.Lot[nb]].Rect;
        var no = reg.May(rig.Hero, -1, LandAction.Build, nq.CenterX, nq.CenterY);
        var pt = rig.W.Pop.People; string head = (pt.Given[hht.Head[nb]] + " " + pt.Family[hht.Head[nb]]).Trim();
        output.WriteLine($"refused on {head}'s lot: {no.Why} ({no.HolderName}); on the leased field: {house.Why} ({house.HolderName})");
        Assert.False(no.Allowed); Assert.Equal(head, no.HolderName); Assert.Equal("ld_may_theirs", no.Why);
        // and the neighbour may build on his own
        Assert.True(reg.May(pt.IdOf(hht.Head[nb]), nb, LandAction.Build, nq.CenterX, nq.CenterY).Allowed);
        // wild land with no claim: nobody's yet
        var (wx, wy) = rig.ClaimPoint(wild: true, room: 8);
        Assert.Equal("ld_may_wild", reg.May(rig.Hero, -1, LandAction.Build, wx, wy).Why);
        // May is cheap (spec 9: 2 microseconds a call with the cell index)
        var sw = System.Diagnostics.Stopwatch.StartNew();
        for (int i = 0; i < 20_000; i++) reg.May(rig.Hero, -1, LandAction.Build, q.CenterX, q.CenterY);
        output.WriteLine($"May: {sw.Elapsed.TotalMilliseconds * 1000 / 20_000:0.00} us a call");
    }

    /// <summary>A parcel the lord's re-let would let: one that fell back to him, else (none in the fresh world) the first tenant holding of a village, given back to him as the re-let finds it.</summary>
    internal static int LeasableParcel(LandRig rig)
    {
        for (int p = 0; p < rig.Region.Parcels.Count; p++) if (rig.L.Leasable(p)) return p;
        var pop = rig.W.Pop;
        for (int p = 0; p < rig.Region.Parcels.Count; p++)
        {
            var pc = rig.Region.Parcels[p];
            if (pc.Owner.Kind != OwnerKind.Tenant || rig.Region.Settlements[pc.Settlement].Class != SettlementClass.Village) continue;
            int lord = LandRegistry.LordHousehold(pop, pc.Settlement); if (lord < 0) continue;
            int old = pop.ParcelHolder[p]; if (old >= 0) pop.Households.ParcelCount[old]--;
            pop.ParcelHolder[p] = lord; pop.Households.ParcelCount[lord]++;
            Assert.True(rig.L.Leasable(p));
            return p;
        }
        throw new InvalidOperationException("no parcel");
    }

    // ------------------------------------------------------------------ LD-7

    [Fact]
    public void LD7_AFieldLease_Pays80ASeasonAField_ArrearsBecomeADebt_ThenNotice_And15DaysLaterTheLeaseEnds()
    {
        using var rig = LandRig.Make(crowns: 100);
        int p = LeasableParcel(rig); var pc = rig.Region.Parcels[p];
        int day = rig.Day;
        var r = rig.L.LeaseParcel(rig.Hero, PurseKind.Person, p, day, rig.C);
        Assert.True(r.Ok);
        var d = rig.L.Book.Deeds[r.Deed];
        Assert.Equal(80L * pc.FieldCount, d.DuesMitesPerSeason);
        Assert.Equal(day + 60, d.LeaseEndDay);
        var (lordId, lordKind) = (d.Lessor, d.LessorPurse);
        long hero0 = rig.Coin, lord0 = rig.Bal(lordId, lordKind);
        int season = LandWorld.NextSeasonStart(day);
        rig.L.Seasonal(season, rig.C);                                               // S5: the season's rent through the dues step
        Assert.Equal(hero0 - 80L * pc.FieldCount, rig.Coin);
        Assert.Equal(lord0 + 80L * pc.FieldCount, rig.Bal(lordId, lordKind));
        Assert.Equal(0, d.ArrearsSeasons);
        // the farming re-let leaves the leased parcel alone
        Assert.True(rig.L.IsLeased(p));
        // no coin: three seasons in arrears is a debt, six is the lessor's notice; the lease is held over past its term while it is owed
        rig.Drain();
        for (int k = 1; k <= 6; k++)
        {
            int sd = season + k * TimeConst.DaysPerSeason;
            rig.L.Seasonal(sd, rig.C);
            rig.L.Daily(sd, rig.C);
            Assert.Equal(k, d.ArrearsSeasons);
            Assert.Equal(k >= 3, d.InDebt);
            Assert.Equal(k >= 6 ? sd + 15 : -1, d.NoticeDay);
            Assert.True(rig.L.Book.Deeds.ContainsKey(d.Id), $"the lease ended early at season {k}");
        }
        Assert.Equal(6 * 80L * pc.FieldCount, d.OwedMites);
        int notice = d.NoticeDay;
        rig.L.Daily(notice - 1, rig.C);
        Assert.True(rig.L.Book.Deeds.ContainsKey(d.Id));
        rig.L.Daily(notice, rig.C);
        Assert.False(rig.L.Book.Deeds.ContainsKey(d.Id));
        Assert.False(rig.L.IsLeased(p));
        Assert.Contains(rig.Chron, e => e.TypeId == "land_debt"); Assert.Contains(rig.Chron, e => e.TypeId == "land_notice"); Assert.Contains(rig.Chron, e => e.TypeId == "land_evicted");
        Assert.True(rig.Led.Audit().IsConserved);
    }

    // ------------------------------------------------------------------ LD-8

    [Fact]
    public void LD8_AStallForASeasonAndForAFairDay_PayRentToTheTownsTreasury()
    {
        using var rig = LandRig.Make(crowns: 100);
        int town = rig.Town(); var (tid, tk) = LandRegistry.TownOf(rig.Region, town);
        long t0 = rig.Bal(tid, tk), h0 = rig.Coin, rent0 = rig.Led.Flow(TransferReason.Rent).Mites;
        int day = rig.Day;
        var a = rig.L.LeaseStall(rig.Hero, PurseKind.Person, town, 0, false, day, rig.C);
        var b = rig.L.LeaseStall(rig.Hero, PurseKind.Person, town, 1, true, day, rig.C);
        Assert.True(a.Ok && b.Ok);
        Assert.Equal(t0 + 1500 + 150, rig.Bal(tid, tk));
        Assert.Equal(h0 - 1650, rig.Coin);
        Assert.Equal(rent0 + 1650, rig.Led.Flow(TransferReason.Rent).Mites);
        Assert.False(rig.L.LeaseStall(rig.Hero, PurseKind.Person, town, 0, true, day, rig.C).Ok);           // taken for the season
        Assert.Equal(LandWorld.NextSeasonStart(day), rig.L.Book.Deeds[a.Deed].LeaseEndDay);
        rig.L.Daily(day + 1, rig.C);                                                              // the fair day is over
        Assert.False(rig.L.Book.Deeds.ContainsKey(b.Deed)); Assert.True(rig.L.Book.Deeds.ContainsKey(a.Deed));
        rig.L.Daily(LandWorld.NextSeasonStart(day), rig.C);
        Assert.False(rig.L.Book.Deeds.ContainsKey(a.Deed));
        Assert.True(rig.Led.Audit().IsConserved);
    }

    // ------------------------------------------------------------------ LD-9, LD-10, LD-11

    [Fact]
    public void LD9_AClaimOutsideEveryFief_Covers8Tiles_AndLapsesOnDay60AtHour6_OnTheRealTick()
    {
        using var rig = LandRig.Make(crowns: 10, ticker: true);
        var (x, y) = rig.ClaimPoint(wild: true, room: 8);
        int day0 = rig.Day;
        var r = rig.L.Stake(rig.Hero, PurseKind.Person, x, y, licence: false, day0, rig.C);
        Assert.True(r.Ok, r.Key);
        Assert.Equal("ld_claim_free", r.Key);
        var c = rig.L.Book.Claims.Single();
        Assert.Equal(8, c.Tiles.Length); Assert.True(LandRig.Connected(c.Tiles));
        Assert.False(c.Licensed || c.Squatting); Assert.Equal(-1, c.Fief);
        long hourStart = rig.W.Sim.State.HourDone;
        long target = (long)(day0 + 60) * 24 + LandSystem.DailyHour;                                // the hour step of day 60, hour 6
        rig.W.Ticker.AdvanceHours((int)(target - hourStart - 1));
        Assert.Equal(ClaimState.Open, c.State);
        rig.W.Ticker.AdvanceHours(1);
        Assert.Equal(ClaimState.Lapsed, c.State);
        Assert.Equal(day0 + 60, c.EndDay);
        Assert.Contains(rig.W.Sim.Chronicle.All, e => e.TypeId == "claim_lapsed");
        output.WriteLine($"staked day {day0}, lapsed day {c.EndDay} at hour {LandSystem.DailyHour}");
    }

    [Fact]
    public void LD10_AnImprovedClaim_GrowsTo16Then24ConnectedTiles_AndADwellingMakesItAFreeholdDeed()
    {
        using var rig = LandRig.Make(crowns: 10);
        var (x, y) = rig.ClaimPoint(wild: true, room: 24);
        int day0 = rig.Day;
        rig.L.Stake(rig.Hero, PurseKind.Person, x, y, false, day0, rig.C);
        var c = rig.L.Book.Claims.Single();
        var before = c.Tiles;
        rig.L.Improve(c.Serial, ImprovementKind.Dwelling, day0 + 40, rig.C);                // a cottage recognised on day 40
        Assert.Equal(16, c.Tiles.Length); Assert.True(LandRig.Connected(c.Tiles)); Assert.True(before.All(t => c.Tiles.Contains(t)));
        Assert.Equal(ClaimState.Deeded, c.State);
        var d = rig.L.Book.Deeds[c.DeedId];
        Assert.Equal((Tenure.Freehold, 0L, HoldingKind.Claim), (d.Tenure, d.DuesMitesPerSeason, d.Kind));
        rig.L.Improve(c.Serial, ImprovementKind.SownAcre, day0 + 50, rig.C);                 // an acre sown on day 50
        Assert.Equal(24, c.Tiles.Length); Assert.True(LandRig.Connected(c.Tiles));
        rig.L.Daily(day0 + 60, rig.C);
        Assert.Equal(ClaimState.Deeded, c.State);                                              // improved: no lapse
        Assert.False(rig.L.Pull(rig.Hero, c.Serial, day0 + 61, rig.C).Ok);                     // the deed stands without the banner
        // the hero may build anywhere on it; the deed is in the registry
        var reg = rig.L.Registry();
        int t = c.Tiles[^1]; Assert.True(reg.May(rig.Hero, -1, LandAction.Build, t % RegionData.Size * 64 + 32, t / RegionData.Size * 64 + 32).Allowed);
        Assert.Equal(rig.Hero, reg.Get(c.DeedId)!.Holder);
    }

    [Fact]
    public void LD11_ALicenceOnAFiefsWaste_Pays8000ForEightAcresToTheLord_GivesACustomaryDeedAt200_WithoutItSquatting()
    {
        using var rig = LandRig.Make(crowns: 200);
        var (x, y) = rig.ClaimPoint(wild: false, room: 24);
        int fief = LandRules.LandFief(rig.Region, Rules, LandRules.TileAt(x, y));
        var (lordId, lordKind) = LandRegistry.LordOf(rig.W.Pop, rig.Region.Fiefs[fief].SeatSettlement);
        long lord0 = rig.Bal(lordId, lordKind), hero0 = rig.Coin;
        var r = rig.L.Stake(rig.Hero, PurseKind.Person, x, y, licence: true, rig.Day, rig.C);
        Assert.True(r.Ok, r.Key);
        var c = rig.L.Book.Claims.Single();
        Assert.Equal(8, c.Tiles.Length);
        Assert.Equal(8_000, r.Mites);
        Assert.Equal(lord0 + 8_000, rig.Bal(lordId, lordKind)); Assert.Equal(hero0 - 8_000, rig.Coin);
        Assert.True(c.Licensed); Assert.False(c.Squatting);
        rig.L.Improve(c.Serial, ImprovementKind.Dwelling, rig.Day + 10, rig.C);
        var d = rig.L.Book.Deeds[c.DeedId];
        Assert.Equal((Tenure.Customary, 200L), (d.Tenure, d.DuesMitesPerSeason));
        // the quit-rent goes to the lord at the season's dues
        long l1 = rig.Bal(lordId, lordKind);
        rig.L.Seasonal(LandWorld.NextSeasonStart(rig.Day + 10), rig.C);
        Assert.Equal(l1 + 200, rig.Bal(lordId, lordKind));
        // without a licence: allowed, marked squatting, nothing paid, never a deed
        var other = OtherWastePoint(rig, c);
        long lord2 = rig.Bal(lordId, lordKind);
        var s = rig.L.Stake(rig.Hero, PurseKind.Person, other.X, other.Y, licence: false, rig.Day, rig.C);
        Assert.True(s.Ok, s.Key); Assert.Equal("ld_claim_squatting", s.Key);
        var c2 = rig.L.Book.Claims.Last();
        Assert.True(c2.Squatting); Assert.False(c2.Licensed); Assert.Equal(0, s.Mites); Assert.Equal(lord2, rig.Bal(lordId, lordKind));
        rig.L.Improve(c2.Serial, ImprovementKind.Dwelling, rig.Day + 5, rig.C);
        Assert.Equal(ClaimState.Open, c2.State);                                              // a squatter's cottage makes no deed
        Assert.Contains(rig.Chron, e => e.TypeId == "claim_staked" && e.Data["squatting"] == "1");
        Assert.True(rig.Led.Audit().IsConserved);
    }

    private static (int X, int Y) OtherWastePoint(LandRig rig, Claim not)
    {
        int N = RegionData.Size;
        for (int t = not.Tiles[^1] + 200; t < RegionData.Tiles; t += 11)
        {
            if (!LandRules.OpenGround(rig.Region, t) || rig.L.Taken(t)) continue;
            int f = LandRules.LandFief(rig.Region, Rules, t); if (f < 0) continue;
            int x = t % N * 64 + 32, y = t / N * 64 + 32;
            if (rig.L.Grow(t, [], 16, x, y, u => rig.L.ClaimTileOk(u, f, null)).Length >= 16) return (x, y);
        }
        throw new InvalidOperationException("no waste");
    }

    // ------------------------------------------------------------------ LD-12

    [Fact]
    public void LD12_ABarnsStorageRoom_Of48SquareMetres_Holds48000LitresAnd96Tonnes()
    {
        var (vol, cap) = LandRules.StoreFor(Rules, 6 * 8, upperFloor: false);
        Assert.Equal(48_000, vol);
        Assert.Equal(96_000_000, cap);
        Assert.Equal(48 * 400 * 1000L, LandRules.StoreFor(Rules, 48, upperFloor: true).MassCapG);
        // a store with those caps refuses what does not fit
        var econ = EconFixture.NewEconomy(goods: false);
        var store = econ.OpenStore(StoreKind.Barn, EntityId.Make(IdKind.Other, 4242), cap, vol * 1000, enforce: true);
        Assert.Equal(96_000_000, store.CapacityMassG); Assert.Equal(48_000_000, store.CapacityVolumeMl);
    }

    // ------------------------------------------------------------------ LD-13

    [Fact]
    public void LD13_EvictionWithGoodsInside_TheGoodsGoToTheLessorAsDistraintAtBid_MassAndCoinExact()
    {
        using var rig = LandRig.Make(crowns: 20);
        var (s, lot) = VacantHouse(rig);
        int day = rig.Day;
        var r = rig.L.LeaseHouse(rig.Hero, PurseKind.Person, s, lot, day, rig.C);
        Assert.True(r.Ok, r.Key);
        var d = rig.L.Book.Deeds[r.Deed];
        var store = rig.W.Econ.OtherStores[d.Id];
        // the hero brings 40 kg of wheat in (moved from a household's store, so mass is only moved)
        ushort wheat = rig.W.Econ.Goods.IdOf("wheat");
        int giver = Enumerable.Range(0, rig.W.Pop.Households.Count).First(h => rig.W.Econ.HouseholdStore[h].Qty(wheat) > 50_000);
        Assert.True(Store.Move(rig.W.Econ.HouseholdStore[giver], store, wheat, 40_000));
        Assert.True(rig.L.HoldsLot(s, lot));
        var (lessor, lk) = (d.Lessor, d.LessorPurse);
        int lordHh = rig.W.Pop.Households.IndexOf(lessor);
        long lordWheat0 = rig.W.Econ.HouseholdStore[lordHh].Qty(wheat);
        var mass0 = rig.W.Econ.MassAuditHolds(); var coin0 = rig.Led.Audit().InPurses;
        rig.Drain();
        int sd = LandWorld.NextSeasonStart(day);
        for (int k = 0; k < 6; k++) { rig.L.Seasonal(sd + k * 15, rig.C); rig.L.Daily(sd + k * 15, rig.C); }
        Assert.True(d.NoticeDay > 0);
        long owed = d.OwedMites;
        rig.L.Daily(d.NoticeDay, rig.C);
        Assert.False(rig.L.Book.Deeds.ContainsKey(d.Id));
        Assert.Equal(0, store.Qty(wheat));
        Assert.Equal(lordWheat0 + 40_000, rig.W.Econ.HouseholdStore[lordHh].Qty(wheat));
        long bid = (long)((Int128)rig.W.Markets.BookForSettlement(s).BidMicro(wheat) * 40_000 / Ravensguard.Sim.Economy.Markets.PriceCurve.Micro);
        output.WriteLine($"owed {owed} mites, distraint {rig.L.Book.DistrainedMites} (40 kg wheat at bid {bid}), still owed {d.OwedMites}");
        Assert.Equal(bid, rig.L.Book.DistrainedMites);
        Assert.Equal(Math.Max(0, owed - bid), d.OwedMites);
        Assert.True(mass0 && rig.W.Econ.MassAuditHolds());
        Assert.True(rig.Led.Audit().IsConserved); Assert.Equal(coin0, rig.Led.Audit().InPurses);
        Assert.False(rig.L.HoldsLot(s, lot));
    }

    internal static (int S, int Lot) VacantHouse(LandRig rig)
    {
        for (int s = 0; s < rig.Region.Settlements.Count; s++)
        {
            var lots = rig.Region.Settlements[s].Plan.Lots;
            for (int l = 0; l < lots.Count; l++)
                if (lots[l].Vacant && lots[l].Dwelling != DwellingType.None && LandRegistry.LordHousehold(rig.W.Pop, s) >= 0 && !rig.L.HoldsLot(s, l)) return (s, l);
        }
        throw new InvalidOperationException("no vacant house");
    }

    // ------------------------------------------------------------------ LD-14

    [Fact]
    public void LD14_LandDrawsNothing_Stream0x500IsDrawnOnlyByTheNpcMarketsTies_AndTheSameScriptGivesTheSameBook()
    {
        Assert.Equal(0x500u, RngStreams.LandMarketTies);
        var root = FindRepo();
        foreach (var f in Directory.GetFiles(Path.Combine(root, "src", "Ravensguard.Sim"), "*.cs", SearchOption.AllDirectories))
        {
            if (f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") || f.EndsWith("RngStreams.cs", StringComparison.Ordinal)) continue;
            var src = File.ReadAllText(f);
            // P6-D4: stream 0x500 is the NPC land market's exact ties (holdings 3.3) and nobody else's
            if (!f.EndsWith("LandMarketNpc.cs", StringComparison.Ordinal)) Assert.DoesNotContain("LandMarketTies", src, StringComparison.Ordinal);
            Assert.DoesNotContain("0x500,", src, StringComparison.Ordinal);
        }
        foreach (var f in Directory.GetFiles(Path.Combine(root, "src", "Ravensguard.Sim", "Land"), "*.cs"))
        {
            if (f.EndsWith("LandMarketNpc.cs", StringComparison.Ordinal)) continue;       // its ties are the one draw (stream 0x500)
            var src = File.ReadAllText(f);
            Assert.DoesNotContain("DetRng", src, StringComparison.Ordinal); Assert.DoesNotContain("RngStream", src, StringComparison.Ordinal); Assert.DoesNotContain("Random", src, StringComparison.Ordinal);
        }
        // LD-4 to LD-11 as one script, twice: the same book, ledger and stores
        string Run()
        {
            using var rig = LandRig.Make(crowns: 2000);
            int town = rig.Town(); int day = rig.Day;
            var l = rig.L.Listings(town)[0]; rig.L.Buy(rig.Hero, PurseKind.Person, town, l.Lot, l.Rect, 0, day, rig.C);
            rig.L.LeaseParcel(rig.Hero, PurseKind.Person, LeasableParcel(rig), day, rig.C);
            rig.L.LeaseStall(rig.Hero, PurseKind.Person, town, 2, false, day, rig.C);
            var (wx, wy) = rig.ClaimPoint(true); rig.L.Stake(rig.Hero, PurseKind.Person, wx, wy, false, day, rig.C);
            var (fx, fy) = rig.ClaimPoint(false); rig.L.Stake(rig.Hero, PurseKind.Person, fx, fy, true, day, rig.C);
            rig.L.Improve(rig.L.Book.Claims[0].Serial, ImprovementKind.Dwelling, day + 3, rig.C);
            for (int k = 0; k < 3; k++) { int sd = LandWorld.NextSeasonStart(day) + k * 15; rig.L.Seasonal(sd, rig.C); rig.L.Daily(sd, rig.C); }
            return rig.L.Book.ComputeHash() + rig.W.Econ.ComputeHash() + rig.L.Registry().ComputeHash() + string.Join(",", rig.Chron.Select(e => e.TypeId));
        }
        Assert.Equal(Run(), Run());
    }

    internal static string FindRepo()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d != null && !File.Exists(Path.Combine(d.FullName, "Ravensguard.slnx"))) d = d.Parent;
        return d?.FullName ?? throw new DirectoryNotFoundException("repo root");
    }
}
