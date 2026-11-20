using Ravensguard.Content;
using Ravensguard.Sim.Characters;
using Ravensguard.Sim.Contracts;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Farming;
using Ravensguard.Sim.Goods;
using Ravensguard.Sim.Growth;
using Ravensguard.Sim.Holdings;
using Ravensguard.Sim.Land;
using Ravensguard.Sim.People;
using Ravensguard.Sim.Save;
using Ravensguard.Sim.Tests.Production;
using Ravensguard.Sim.Tests.Seamless;
using Ravensguard.Sim.Trade;
using Ravensguard.Sim.WorldGen;
using Ravensguard.Sim.WorldGen.Region;
using Ravensguard.Sim.Worlds;
using Xunit.Abstractions;

namespace Ravensguard.Sim.Tests.Holdings;

/// <summary>
/// P6-D4 (docs/specs/phase6/holdings-and-works-v2.md 3.5 and 3.8): HW-5 soil improvement as contracts (posted by holders with the pay in escrow, taken by the hero or by the farmhands of the place, marl +15 for twenty
/// game years then fading a point a year, dung +8 for two seasons, the hero's own fields without a contract), HW-14 the world-start stock calibration hook, HW-15 the saves of streets and soil works. Whole
/// <see cref="WorldRuntime"/>s of a new game (seed 1234), every hero step a command through the desk.
/// </summary>
public class SoilWorksTests(ITestOutputHelper output)
{
    private static (SeamWorld W, WorldState State) Game()
    {
        var w = SeamWorld.Get(1234);
        var home = w.BlockOrigin.Offset(1, 1);
        var dir = ContinentSettings.FindBaseContent()!;
        var cs = ContinentSettings.LoadDefaults(dir); cs.Seed = 1234;
        var hs = Ravensguard.Sim.WorldGen.History.HistorySettings.LoadDefaults(dir); hs.Seed = 1234;
        var region = RegionGenerator.Generate(w.Continent, w.History, home, w.Settings);
        LandmarkPlacer.Place(region, w.History, w.Content);
        var pop = Populator.Populate(region, w.History, w.Content);
        var h = HeroRecord.FromOrigin("Aldric Vane", false, w.Content.All<OriginDef>().OrderBy(o => o.Order).First());
        var st = NewGameWorld.Create(w.Content, w.Continent, w.History, cs, hs, w.Settings, home.CellX, home.CellY, pop, h, null);
        st.Content = w.Content;
        return (w, st);
    }

    private sealed class Play(WorldRuntime rt, HeroContext ctx)
    {
        public readonly HeroDesk Desk = new();
        public HeroResult Do(HeroCommand c)
        {
            var cmd = Desk.Issue(c with { IssuedSec = Math.Max(c.IssuedSec, rt.HourDone * 3600) });
            Desk.ApplyDue(ctx, rt.HourDone + 1);
            return Desk.View!.Results.Last(r => r.Seq == cmd.Seq);
        }
        public long Sec => (rt.HourDone + 1) * 3600;
        public int Day => (int)(Sec / 86400);
        public void Days(int days) => rt.AdvanceTo(GameTime.FromGameHours(rt.HourDone + 1 + days * 24L));
        /// <summary>Runs to hour 10 of the next season's first day (the land's and the contracts' hours at 9 have been done).</summary>
        public void ToSeasonDay()
        {
            long day = rt.HourDone / 24 + 1; long next = (day + TimeConst.DaysPerSeason - 1) / TimeConst.DaysPerSeason * TimeConst.DaysPerSeason;
            if (next <= rt.HourDone / 24) next += TimeConst.DaysPerSeason;
            rt.AdvanceTo(GameTime.FromGameHours(next * 24 + 10));
        }
    }

    private static T Barrier<T>(WorldRuntime rt, Func<T> f) => rt.AtBarrier(f).GetAwaiter().GetResult();

    private sealed record Rig(SeamWorld W, WorldState S, WorldRuntime Rt, HeroContext Ctx, Play P, SoilWorld Sw, FarmingSystem Farm, LandWorld Lw);

    private static Rig Start()
    {
        var (w, s) = Game();
        var rt = new WorldRuntime(s, w.Content, new MemoryRegionPager());
        ContractLink.Attach(rt);                                                                          // the game attaches the contracts' hour to the runtime (StreetView.Work)
        var ctx = new HeroContext(s, s.Party!, w.Content);
        var farm = s.HomeRecords!.Economy.Farming;
        return new Rig(w, s, rt, ctx, new Play(rt, ctx), SoilWorld.For(s)!, farm, s.HomeRecords.Sim.Get<LandWorld>()!);
    }

    /// <summary>A parcel an NPC household holds (not the demesne, the glebe or the hero's): its index, its first field, its holder household, its place. The first of those whose place has dung to spare (for the manure test) or any (marl).</summary>
    private static (int Parcel, int Field, int Hh, int Place) NpcField(Rig r, Func<int, bool>? place = null)
    {
        var reg = r.S.Population.Region; var hht = r.S.Population.Households;
        for (int p = 0; p < reg.Parcels.Count && p < r.S.Population.ParcelHolder.Length; p++)
        {
            var pc = reg.Parcels[p]; int hh = r.S.Population.ParcelHolder[p];
            if (pc.Owner.Kind is OwnerKind.Demesne or OwnerKind.Glebe || pc.FieldCount == 0 || hh < 0 || hht.MemberCount[hh] == 0 || hht.Class[hh] == SocialClass.Gentry) continue;
            if (place != null && !place(pc.Settlement)) continue;
            return (p, pc.FieldStart, hh, pc.Settlement);
        }
        throw new InvalidOperationException("no suitable field in seed 1234");
    }

    /// <summary>Every field of the place but one is made fertile, so the season's three jobs a place are that one's (the generated fields are poor in places).</summary>
    private static void OnlyThisPoor(Rig r, int place, int field, int fertility)
    {
        var reg = r.S.Population.Region; var F = r.Farm.State.Fields;
        foreach (int pc in reg.Settlements[place].Parcels) for (int k = 0; k < reg.Parcels[pc].FieldCount; k++) F.Fertility[reg.Parcels[pc].FieldStart + k] = 85;
        F.Fertility[field] = (byte)fertility;
    }

    // ------------------------------------------------------------------ HW-5

    [Fact]
    public void HW5_AHolderWhoseFieldIsPoorPostsTheJobWithItsPayInEscrow_TheHeroTakesAndWorksIt_AndGetsPaidAndTheFieldGainsEightPointsForTwoSeasons()
    {
        var r = Start(); using var _ = r.Rt;
        var (_, s, rt, ctx, p, sw, farm, lw) = (r.W, r.S, r.Rt, r.Ctx, r.P, r.Sw, r.Farm, r.Lw);
        var e = s.Economy!; var F = farm.State.Fields;
        sw.HasMarlPit = _ => false;                                                                       // no marl pit near: it is dung
        var (parcel, field, hh, place) = NpcField(r, pl => sw.DungLeftKg(pl, p.Day) >= lw.Rules.ManureKg);
        var holder = s.Population.Households.IdOf(hh); var purse = e.HouseholdPurse[hh];
        OnlyThisPoor(r, place, field, 40);
        long pay = sw.PayOf(SoilKind.Manure);
        Assert.Equal(20 * RecipeBookWage() * 3200 / 1000, pay);
        e.Ledger.Mint(purse, new Coin(pay * 3), SourceReason.Seed);
        long holder0 = e.Ledger.Balance(purse).Mites;
        p.ToSeasonDay();
        var work = sw.Book.Works.First(x => x.Field == field);
        Assert.Equal(SoilKind.Manure, work.Kind); Assert.Equal(SoilState.Open, work.State);
        var c = s.Contracts!.Get(work.Contract)!;
        Assert.Equal("soil_improvement", c.Kind); Assert.Equal(pay, c.PayMites); Assert.Equal(holder, c.Issuer); Assert.Equal(20, c.Param);
        Assert.Equal(pay, e.Ledger.Balance(new PurseId(c.Escrow)).Mites);                                // the pay is in escrow, nothing is posted that cannot be paid
        Assert.True(e.Ledger.Balance(purse).Mites <= holder0 - pay + 1_000_000);                          // out of the holder's purse (the rest of the day's trade aside)
        Assert.Contains(s.Chronicle.All, en => en.TypeId == "work_need" && en.Data.GetValueOrDefault("need") == "soil");
        // the hero takes it (nobody has in a week) and works it by hand, six hours a day before noon: twenty hours, four days
        long coin0 = e.Ledger.Balance(ctx.Purse).Mites;
        Assert.True(p.Do(ContractDesk.Take(c.Id, p.Sec)).Ok);
        Assert.Equal(WorkState.Taken, c.State);
        int fert0 = F.Fertility[field], days = 0;
        HeroResult res;
        do
        {
            p.Days(1); long ds = (p.Sec / 86400 - 1) * 86400;
            res = p.Do(HoldingsDesk.SoilWork(work.Serial, ds + 6 * 3600, ds + 12 * 3600, p.Sec));
            Assert.True(res.Ok, res.Text.ToString()); days++;
        } while (work.State != SoilState.Done && days < 10);
        Assert.Equal(SoilState.Done, work.State); Assert.InRange(days, 3, 5);
        Assert.Equal(WorkState.Done, c.State);
        Assert.Equal(coin0 + pay, e.Ledger.Balance(ctx.Purse).Mites);                                    // the escrow paid the hero, to the mite
        Assert.Equal(0, e.Ledger.Balance(new PurseId(c.Escrow)).Mites);
        var eff = Assert.Single(sw.Book.Effects, x => x.Field == field);
        Assert.Equal((SoilKind.Manure, 8), (eff.Kind, eff.Amount));
        Assert.True(F.Fertility[field] >= Math.Min(100, fert0 + 8) - 4);                                   // +8 (the harvest's own drift aside)
        Assert.Contains(s.Chronicle.All, en => en.TypeId == "soil_improved" && en.TextKey == "ch_soil_improved" && en.Causes.Any(x => x.NoteKey == "ch_note_soil_poor"));
        // two seasons: the dung is gone from the field again
        int at = F.Fertility[field];
        sw.Age(work.DoneDay + 29); Assert.Equal(at, F.Fertility[field]);
        sw.Age(work.DoneDay + 30); Assert.Equal(at - 8, F.Fertility[field]); Assert.DoesNotContain(sw.Book.Effects, x => x.Field == field);
        Assert.True(e.Ledger.Audit().IsConserved); Assert.True(e.MassAuditHolds());
        output.WriteLine($"manure on field {field} of {s.Population.Region.Settlements[place].Name}: {pay} mites, {days} days");
    }

    private static long RecipeBookWage() => Ravensguard.Sim.Production.RecipeBook.WagePerHour(Ravensguard.Sim.Production.WageClass.Unskilled);

    [Fact]
    public void HW5_AJobNobodyTakesInAWeekIsTakenByTheFarmhandsOfThePlace_WhoFinishItByTheContractRules_AndAreTheOnesPaid()
    {
        var r = Start(); using var _ = r.Rt;
        var (s, rt, p, sw, farm) = (r.S, r.Rt, r.P, r.Sw, r.Farm); var e = s.Economy!; var F = farm.State.Fields;
        sw.HasMarlPit = _ => true;
        // a place with four idle hands to make a crew
        var w0 = ContractWiring.From(s, r.W.Content);
        var crews = w0.Companies().Where(c => c.Kind == "farmhands").Select(c => c.Settlement).ToHashSet();
        Assert.NotEmpty(crews);
        var (parcel, field, hh, place) = NpcField(r, pl => crews.Contains(pl));
        var purse = e.HouseholdPurse[hh]; OnlyThisPoor(r, place, field, 35);
        long pay = sw.PayOf(SoilKind.Marl); Assert.Equal(250 * RecipeBookWage() * 3200 / 1000, pay);
        e.Ledger.Mint(purse, new Coin(pay * 3), SourceReason.Seed);
        p.ToSeasonDay();
        var work = sw.Book.Works.First(x => x.Field == field); var c = s.Contracts!.Get(work.Contract)!;
        Assert.Equal(SoilKind.Marl, work.Kind); Assert.Equal(250, c.Param); Assert.Equal(WorkState.Open, c.State);
        p.Days(8);
        Assert.Equal(WorkState.Taken, c.State); Assert.True(c.ByCompany); Assert.Equal(SoilWorld.CrewId(place), c.Taker);
        var crew = ContractWiring.From(s, r.W.Content).Companies().First(x => x.Id == c.Taker);
        long crew0 = e.Ledger.Balance(new PurseId(crew.Purse)).Mites;
        p.Days(2 + 250 / 10);                                                                              // the crew's twenty-five days
        Assert.Equal(WorkState.Done, c.State);
        Assert.Equal(SoilState.Done, work.State);
        Assert.Contains(sw.Book.Effects, x => x.Field == field && x.Kind == SoilKind.Marl && x.Amount > 0);
        Assert.True(e.Ledger.Balance(new PurseId(crew.Purse)).Mites >= crew0 + pay - 1_000_000_000L && e.Ledger.Balance(new PurseId(c.Escrow)).Mites == 0);
        Assert.True(e.Ledger.Audit().IsConserved); Assert.True(e.MassAuditHolds());
    }

    [Fact]
    public void HW5_MarlAdds15ForTwentyGameYearsThenFadesAPointAYear_TheCapOfOneHundredLimitsIt_AndDungLeavesAfterTwoSeasons()
    {
        var r = Start(); using var _ = r.Rt;
        var (s, sw, farm) = (r.S, r.Sw, r.Farm); var F = farm.State.Fields; var rules = r.Lw.Rules;
        Assert.Equal((15, 250, 40_000, 20, 8, 2, 1_000), (rules.MarlFertility, rules.MarlHours, rules.MarlKg, rules.MarlLastYears, rules.ManureFertility, rules.ManureSeasons, rules.ManureKg));
        var (_, f0, _, place) = NpcField(r);
        F.Fertility[f0] = 40; int day = 5_000;
        var w = new SoilWork { Serial = sw.Book.NextSerial++, Field = f0, Settlement = place, Kind = SoilKind.Marl, State = SoilState.Working, NeedMilli = 250_000, DoneMilli = 250_000, PostedDay = day };
        sw.Book.Works.Add(w); sw.Finish(w, day);
        Assert.Equal(55, F.Fertility[f0]);
        sw.Age(day + 20 * TimeConst.DaysPerYear - 1); Assert.Equal(55, F.Fertility[f0]);                  // twenty game years it holds
        sw.Age(day + 20 * TimeConst.DaysPerYear + TimeConst.DaysPerYear - 1); Assert.Equal(55, F.Fertility[f0]);
        sw.Age(day + 21 * TimeConst.DaysPerYear); Assert.Equal(54, F.Fertility[f0]);                       // then a point a year
        sw.Age(day + 22 * TimeConst.DaysPerYear); Assert.Equal(53, F.Fertility[f0]);
        sw.Age(day + 40 * TimeConst.DaysPerYear); Assert.Equal(40, F.Fertility[f0]);                       // fifteen points faded: gone
        Assert.DoesNotContain(sw.Book.Effects, x => x.Field == f0);
        // the cap: a field at 95 gains 5, and fades 5
        F.Fertility[f0] = 95; var w2 = new SoilWork { Serial = sw.Book.NextSerial++, Field = f0, Settlement = place, Kind = SoilKind.Marl, State = SoilState.Working, NeedMilli = 1, PostedDay = day };
        sw.Book.Works.Add(w2); sw.Finish(w2, day);
        Assert.Equal(100, F.Fertility[f0]); Assert.Equal(5, sw.Book.Effects.Single(x => x.Field == f0).Amount);
        sw.Age(day + 40 * TimeConst.DaysPerYear); Assert.Equal(95, F.Fertility[f0]);
    }

    [Fact]
    public void HW5_TheHeroImprovesAFieldOfHisOwnHouseholdWithoutAContract_AndNotAnotherMansField()
    {
        var r = Start(); using var _ = r.Rt;
        var (s, p, sw, farm, lw, ctx) = (r.S, r.P, r.Sw, r.Farm, r.Lw, r.Ctx); var F = farm.State.Fields; var e = s.Economy!;
        sw.HasMarlPit = _ => false;
        var (parcel, field, hh, place) = NpcField(r, pl => sw.DungLeftKg(pl, p.Day) >= lw.Rules.ManureKg);
        { var no = p.Do(HoldingsDesk.SoilOwn(field, SoilKind.Manure, p.Sec)); Assert.False(no.Ok); Assert.Contains("not your household", no.Text.ToString()); }        // another household's
        lw.ParcelLet!(parcel, ctx.Party.Hero, true);                                                       // a field he leases is his household's
        farm.SyncPopulation();
        var (jobs0, fields) = HoldingsDesk.SoilView(sw, ctx.Party.Hero, ConstructionDeskHousehold(ctx), p.Day);
        Assert.Contains(fields, f => f.Field == field && !f.Busy && f.CanManure && !f.CanMarl); Assert.Empty(jobs0);
        F.Fertility[field] = 42; long coin0 = e.Ledger.Balance(ctx.Purse).Mites;
        var start = p.Do(HoldingsDesk.SoilOwn(field, SoilKind.Manure, p.Sec));
        Assert.True(start.Ok, start.Text.ToString());
        var work = sw.Book.Works.Single(x => x.Field == field); Assert.Equal(0, work.Contract);
        { var no = p.Do(HoldingsDesk.SoilOwn(field, SoilKind.Manure, p.Sec)); Assert.False(no.Ok); Assert.Contains("already stands", no.Text.ToString()); }
        for (int d = 0; d < 8 && work.State != SoilState.Done; d++)
        {
            p.Days(1); long ds = (p.Sec / 86400 - 1) * 86400;
            Assert.True(p.Do(HoldingsDesk.SoilWork(work.Serial, ds + 6 * 3600, ds + 12 * 3600, p.Sec)).Ok);
            if (work.State != SoilState.Done) Assert.Single(HoldingsDesk.SoilView(sw, ctx.Party.Hero, ConstructionDeskHousehold(ctx), p.Day).Jobs);
        }
        Assert.Equal(SoilState.Done, work.State); Assert.Contains(sw.Book.Effects, x => x.Field == field && x.Amount == 8);
        Assert.Equal(coin0, e.Ledger.Balance(ctx.Purse).Mites);                                            // nobody pays him for his own field, and it costs no coin
        Assert.True(e.Ledger.Audit().IsConserved);
    }

    private static int ConstructionDeskHousehold(HeroContext ctx) => Ravensguard.Sim.Construction.ConstructionDesk.HouseholdOf(ctx);

    // ------------------------------------------------------------------ HW-14

    [Fact]
    public void HW14_TheWorldStartStockHookIsOffByDefault_AndWhenAskedTopsEveryMerchantUpToDaysOfStructuralDemand_WithNothingMadeFromNothing()
    {
        Assert.Equal(0, new MoneySettingsDef { Id = "m" }.WorldStartStockDays);
        using var w = SoakWorld.Create(withTicker: false); var m = w.Markets; var e = w.Econ;
        Assert.Equal(0, e.Money.WorldStartStockDays);                                                      // the shipped content leaves the first weeks as they were
        long before = m.AllBooks().Sum(b => b.Goods.Sum(g => g.StockUnits));
        Assert.Equal(0, m.SeedStockDays(w.Pop.NowDay, 8, 0));
        long added = m.SeedStockDays(w.Pop.NowDay, 8, 5);
        long after = m.AllBooks().Sum(b => b.Goods.Sum(g => g.StockUnits));
        Assert.True(added > 0 || before > 0);
        Assert.Equal(before + added, after);
        var demand = m.ComputeStructuralDemand(w.Pop.NowDay, 8);
        foreach (var b in m.AllBooks().Where(x => x.Merchants.Count > 0))
            for (ushort g = 0; g < demand[b.SettlementIndex].Length; g++)
                if (demand[b.SettlementIndex][g] > 0) Assert.True(b.Goods[g].StockUnits >= demand[b.SettlementIndex][g] * 5, $"book {b.SettlementIndex} good {g}");
        Assert.Equal(0, m.SeedStockDays(w.Pop.NowDay, 8, 5));                                              // topped up already: nothing more
        Assert.True(e.MassAuditHolds()); Assert.True(e.Ledger.Audit().IsConserved);
    }

    // ------------------------------------------------------------------ HW-15

    [Fact]
    public void HW15_AStreetAndASoilJobSaveLoadAndSaveTheSameBytes_AndTheLoadedRegionHasTheStreet()
    {
        var r = Start(); using var _ = r.Rt;
        var (w, s, rt, sw, farm, lw) = (r.W, r.S, r.Rt, r.Sw, r.Farm, r.Lw); var reg = s.Population.Region;
        // a street the lord lays (the first place that has room for one), a posted soil job and an improvement standing
        StreetRecord? rec = null; int place = -1;
        for (int i = 0; i < reg.Settlements.Count && rec == null; i++)
            if (reg.Settlements[i].Class != SettlementClass.Farmstead && Zoning.Plan(reg, i, lw.Rules, lw.GrowthTuning, 7) is { } pl) { rec = pl; place = i; }
        Assert.NotNull(rec);
        int streets0 = reg.Settlements[place].Plan.Streets.Count;
        Barrier(rt, () => { Zoning.Lay(lw, rec!, null); return 0; });
        Assert.Equal(streets0 + 1, reg.Settlements[place].Plan.Streets.Count);
        sw.HasMarlPit = _ => false;
        var (parcel, field, hh, pl2) = NpcField(r);
        farm.State.Fields.Fertility[field] = 41;
        var wk = new SoilWork { Serial = sw.Book.NextSerial++, Field = field, Parcel = parcel, Settlement = pl2, Kind = SoilKind.Manure, State = SoilState.Working, NeedMilli = 20_000, DoneMilli = 20_000, PostedDay = 3, Issuer = s.Population.Households.IdOf(hh) };
        sw.Book.Works.Add(wk); sw.Finish(wk, 4);
        sw.Book.Works.Add(new SoilWork { Serial = sw.Book.NextSerial++, Field = field + 1, Parcel = parcel, Settlement = pl2, Kind = SoilKind.Marl, State = SoilState.Open, NeedMilli = 250_000, PostedDay = 4, Contract = 0 });
        var f1 = Barrier(rt, () => WorldSave.Capture(rt.State, "h", "0.1"));
        Assert.Contains(LandWorksSave.SectionName, f1.Sections.Keys);
        var back = WorldSave.Restore(SaveCodec.Decode(SaveCodec.Encode(f1)), w.Content, "h", null, w.Continent, w.History);
        Assert.Equal(sw.Book.ComputeHash(), back.Soil!.ComputeHash());
        Assert.Equal(streets0 + 1, back.Population.Region.Settlements[place].Plan.Streets.Count);          // the street is replayed over the regenerated region
        Assert.Equal(reg.Settlements[place].Plan.Streets, back.Population.Region.Settlements[place].Plan.Streets);
        Assert.Equal(rec, Assert.Single(back.Growth!.Streets));
        var again = WorldSave.Capture(back, "h", "0.1");
        foreach (var name in new[] { LandWorksSave.SectionName, "growth", "farming" })
            Assert.True(f1.Sections[name].Span.SequenceEqual(again.Sections[name].Span), $"section '{name}' saves the same bytes after a load");
        // a world that never laid a street or posted a job writes no such section (older saves keep their bytes)
        var (w2, s2) = Game();
        using var rt2 = new WorldRuntime(s2, w2.Content, new MemoryRegionPager());
        var f2 = Barrier(rt2, () => WorldSave.Capture(rt2.State, "h", "0.1"));
        Assert.DoesNotContain(LandWorksSave.SectionName, f2.Sections.Keys);
    }

    [Fact]
    public void HW15_TheSoilAndStreetWiring_IsDeterministic_TwoRunsOfASeasonGiveTheSameBooks()
    {
        string Run()
        {
            var r = Start(); using var _ = r.Rt;
            r.Sw.HasMarlPit = _ => false;
            var (_, field, hh, pl) = NpcField(r);
            OnlyThisPoor(r, pl, field, 40);
            r.S.Economy!.Ledger.Mint(r.S.Economy.HouseholdPurse[hh], new Coin(100_000), SourceReason.Seed);
            r.P.ToSeasonDay(); r.P.Days(9);
            return r.Sw.Book.ComputeHash() + r.S.Contracts!.Hash() + r.S.Economy!.ComputeHash() + r.S.Growth?.ComputeHash();
        }
        Assert.Equal(Run(), Run());
    }
}
