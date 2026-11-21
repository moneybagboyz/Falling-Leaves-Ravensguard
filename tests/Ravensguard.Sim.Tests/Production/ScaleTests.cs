using Ravensguard.Content;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Goods;
using Ravensguard.Sim.People;
using Ravensguard.Sim.Production;
using Ravensguard.Sim.Save;
using Ravensguard.Sim.Tests.Economy;
using Ravensguard.Sim.WorldGen.Region;

namespace Ravensguard.Sim.Tests.Production;

/// <summary>P7-ES1 (economies-of-scale 10): ES-1 to ES-3, ES-6, ES-7, ES-12, ES-13, and the hourly integration (wages by role, the unsupervised rule, the grade rows, the save section).</summary>
public class ScaleTests
{
    private static ScaleSettingsDef Ss => ProdKit.Content.Scale;
    private static GameTime At(long hour) => GameTime.FromGameHours(hour);

    // ---------------------------------------------------------------- the closed forms

    [Fact]
    public void ES1_ForgeSword_ThreeCrews()
    {
        // 24 h at an anvil, smithing grades 200/400/400 (4.8 / 9.6 / 9.6 h), in worker-minutes
        long hM = 288, hJ = 576, hA = 576;
        Assert.Equal(24 * 60, Crews.ElapsedMinutes(hM, hJ, hA, 1, 0, 0));
        Assert.Equal(1200, Crews.CostForElapsed(24 * 60, 1, 0, 0));
        Assert.Equal(8 * 60, Crews.ElapsedMinutes(hM, hJ, hA, 1, 1, 1));                    // max(4.8, 7.2, 8.0)
        Assert.Equal(800, Crews.CostForElapsed(8 * 60, 1, 1, 1));
        Assert.Equal(720, Crews.CostByGrade(hM, hJ, hA));                                   // each step by its own grade: 4.8 x 50 + 9.6 x 35 + 9.6 x 15
        Assert.Equal(new Grades(200, 400, 400), ProductionContent.GradesFor(Ss, ProdKit.Recipes["forge_sword"]));
    }

    [Fact]
    public void ES2_NoMaster_JourneymenDoTheMSteps_AtOneAndAHalf()
    {
        // M hours 4.8 x 1.5 = 7.2; T = max(7.2 / 2, (7.2 + 9.6) / 2, 26.4 / 2) = 13.2 h
        Assert.Equal((long)(13.2 * 60), Crews.ElapsedMinutes(288, 576, 576, 0, 2, 0, Ss.UnsupervisedPm));
    }

    [Theory]
    [InlineData(60, 4)]
    [InlineData(0, 2)]
    [InlineData(30, 3)]
    [InlineData(100, 4)]
    public void ES3_MastersReach(int leadership, int stations) => Assert.Equal(stations, Crews.Reach(Ss, leadership));

    [Fact]
    public void ES6_Span_ThirtyWorkers_OverseersEightNineNine()
    {
        int span = Crews.Span(Ss, 20) + Crews.Span(Ss, 30) + Crews.Span(Ss, 30);
        Assert.Equal(8 + 9 + 9, span);
        Assert.Equal(900, Crews.SpeedPm(Ss, 30, span));                                     // excess 4: 1,000 - 4 x 25
        Assert.Equal(1000, Crews.SpeedPm(Ss, 26, span));
        Assert.Equal(500, Crews.SpeedPm(Ss, 200, span));                                    // the floor
        Assert.Equal(16, Crews.Span(Ss, 100));                                              // the cap
    }

    [Fact]
    public void ES7_Crowding_FiveAtAnAnvil()
    {
        Assert.Equal(3, ProdKit.Content.CrewMax("anvil"));
        Assert.Equal(4000, Crews.EffectivePm(5, 3));
        Assert.Equal(3000, Crews.EffectivePm(3, 3));
        Assert.Equal(4500, Crews.EffectivePm(9, 3));                                        // 3 full, 3 half, the rest count for nothing
        Assert.Equal([1000, 1000, 1000, 500, 500, 500, 0], Enumerable.Range(0, 7).Select(i => Crews.SlotPm(i, 3)).ToArray());
    }

    [Fact]
    public void Roles_FromSkillAndAge_AndWagesNeverAboveTheRecipesClass()
    {
        Assert.Equal(CrewRole.Master, Crews.RoleOf(Ss, 60, 30));
        Assert.Equal(CrewRole.Journeyman, Crews.RoleOf(Ss, 59, 30));
        Assert.Equal(CrewRole.Journeyman, Crews.RoleOf(Ss, 30, 30));
        Assert.Equal(CrewRole.Hand, Crews.RoleOf(Ss, 29, 30));
        Assert.Equal(CrewRole.Apprentice, Crews.RoleOf(Ss, 80, 15));
        Assert.Equal(WageClass.Skilled, Crews.WageOf(CrewRole.Master, WageClass.Skilled));
        Assert.Equal(WageClass.Master, Crews.WageOf(CrewRole.Master, WageClass.Master));
        Assert.Equal(WageClass.Unskilled, Crews.WageOf(CrewRole.Hand, WageClass.Master));
    }

    [Fact]
    public void Rate_UndividedRecipe_IsExactlyTheSumOfTheWorkers()
        => Assert.Equal(7_000, Crews.Rate(0, 0, 7_000, Grades.Plain));

    [Fact]
    public void Grades_AreLoadedFromTheScaleContent_RecipeBeforeSkill()
    {
        Assert.Equal(new Grades(0, 300, 700), ProductionContent.GradesFor(Ss, ProdKit.Recipes["saw_planks"]));
        Assert.Equal(new Grades(0, 200, 800), ProductionContent.GradesFor(Ss, ProdKit.Recipes["burn_charcoal"]));
        Assert.Equal(new Grades(200, 400, 400), ProdKit.Recipes["forge_bar"].Grades);
        Assert.Equal(Grades.Plain, ProdKit.Recipes["tan_hides"].Grades);                    // no row: all hand steps
    }

    // ---------------------------------------------------------------- the hourly integration

    [Fact]
    public void Hourly_OwnerMasterAndAHand_WageByRoleNotByClass()
    {
        var k = new ProdKit(households: 2);
        var b = k.Make("smithy", 100_000, 1, Occupation.Smith, Skill.Smithing, 10, ownerLevel: 70);     // the owner a master, the one hand skill 10
        k.Give(b, "iron_bloom", 6_000); k.Give(b, "charcoal", 10_000);
        var st = b.Stations.First(s => s.Kind == "forge");
        Assert.True(k.Sys.TryStart(b, st, k.R("forge_bar"), At(6)));
        k.Sys.WorkHour(At(6));
        Assert.Equal(2, st.Crew.Count);
        Assert.Equal(RecipeBook.WagePerHour(WageClass.Unskilled), b.Owed[0]);               // the hand earns the unskilled 15 an hour, not the recipe's skilled 35
        Assert.True(k.R("forge_bar").Wage >= WageClass.Skilled);
    }

    [Fact]
    public void Hourly_NoMasterAtWork_TheJourneymenDoTheMStepsSlower()
    {
        long OneHour(bool ownerWorks)
        {
            var k = new ProdKit(households: 3);
            var b = k.Make("smithy", 100_000, 2, Occupation.Smith, Skill.Smithing, 45, ownerWorks: ownerWorks, ownerLevel: 45);
            k.Give(b, "iron_bloom", 6_000); k.Give(b, "charcoal", 10_000);
            var st = b.Stations.First(s => s.Kind == "forge");
            Assert.True(k.Sys.TryStart(b, st, k.R("forge_bar"), At(6)));
            k.Sys.WorkHour(At(6));
            Assert.Equal(2, st.Crew.Count);
            return st.ProgressMicro;
        }
        long p = Station.PerWorkerHour(8 * 1000, 1300 - 6 * 45);
        Assert.Equal(2 * p, OneHour(true));                                                    // the owner is the master of his house: Phase 2's two worker-hours
        // two journeymen alone: forge_bar's M steps (200 of 1,000) take half as long again: 1,100 / 1,000 of the work
        Assert.Equal((2 * p * 1000 + 1099) / 1100, OneHour(false));
    }

    [Fact]
    public void Hourly_UndividedRecipe_StaysPhase2()
    {
        var k = new ProdKit(households: 2);
        var b = k.Make("tannery", 100_000, 1, Occupation.Tanner, Skill.Tanning, 50);
        k.Give(b, "hide_raw", 100_000); k.Give(b, "oak_bark", 40_000);
        var pit = b.Stations[0];
        Assert.True(k.Sys.TryStart(b, pit, k.R("tan_hides"), At(6)));
        k.Sys.WorkHour(At(6));
        Assert.Equal(2 * Station.PerWorkerHour(40 * 1000, 1000), pit.ProgressMicro);
    }

    // ---------------------------------------------------------------- the guild (ES-12, ES-13)

    private sealed class Town
    {
        public ProdKit Kit = null!; public ScaleWorld Sw = null!; public Business Big = null!; public PurseId Treasury;
        public List<(ulong Who, ulong Of, int Delta)> Opinions = [];
    }

    private static ProductionContent WithScale(Func<ScaleSettingsDef, ScaleSettingsDef> f)
    {
        var c = ProdKit.Content;
        return new ProductionContent { Recipes = c.Recipes, Kinds = c.Kinds, Extractions = c.Extractions, Settings = c.Settings, Spending = c.Spending, Tax = c.Tax, Growth = c.Growth, Ownership = c.Ownership, Scale = f(c.Scale) };
    }

    /// <summary>A guild town (three smithies, one with five journeymen), its lord's treasury, the guild's world.</summary>
    private static Town MakeTown(int journeymen = 5, Func<ScaleSettingsDef, ScaleSettingsDef>? scale = null)
    {
        var t = new Town();
        var k = t.Kit = new ProdKit(households: 6 + journeymen + 2, content: scale == null ? null : WithScale(scale));
        var region = k.Pop.Region; region.Settlements[0].Class = SettlementClass.Town;
        region.Fiefs.Add(new Fief { Id = EntityId.Make(IdKind.Faction, 77), Name = "Fief", Realm = 0, SeatSettlement = 0 }); region.Fiefs[0].Settlements.Add(0); region.Settlements[0].FiefIndex = 0;
        t.Treasury = k.Econ.Ledger.Open(PurseKind.Treasury, region.Fiefs[0].Id);
        var kind = k.Used.Kind("smithy");
        Business Shop(int owner, int firstStaff, int staff, int skill = 70)
        {
            k.People.Occupation[owner] = Occupation.Smith; k.People.SetSkill(owner, 0, Skill.Smithing, skill);
            var b = k.Sys.NewBusiness(kind, 0, 0, owner);
            foreach (var (sk, n) in kind.Stations) for (int c = 0; c < n; c++) b.Stations.Add(new Station { Kind = sk });
            for (int s = 0; s < staff; s++) { int p = firstStaff + s; k.People.Occupation[p] = Occupation.Smith; k.People.SetSkill(p, 0, Skill.Smithing, 45); k.Sys.AddStaff(b, p); }
            k.Econ.Ledger.Mint(b.Purse, new Coin(50_000), SourceReason.Seed);
            return b;
        }
        t.Big = Shop(0, 3, journeymen);
        Shop(1, 3 + journeymen, 0, 80); Shop(2, 3 + journeymen, 0, 75);          // the guild's master is the best smith: not the big shop's owner
        var book = new ScaleBook();
        t.Sw = new ScaleWorld(book, k.Sys, EconFixture.Content, 99) { AddOpinion = (w, o, d) => t.Opinions.Add((w, o, d)) };
        return t;
    }

    [Fact]
    public void ES12_FiveJourneymen_AreFinedFourThousand_TheMastersOpinionFalls_AndAfterThreeTheShopCloses()
    {
        var t = MakeTown(scale: s => s with { InspectionPct = 100 });
        var k = t.Kit; var shops = Limits.Shops(k.Sys, EconFixture.Content, 0);
        var big = shops.First(s => s.Business == t.Big);
        Assert.Equal(new GuildOver(2, 0, 0), big.Over);
        Assert.Equal(3, shops.Count);

        long before = k.Econ.Ledger.Balance(t.Treasury).Mites, box = k.Econ.Ledger.Balance(t.Big.Purse).Mites;
        Assert.Equal(1, t.Sw.Seasonal(15));
        Assert.Equal(before + 4_000, k.Econ.Ledger.Balance(t.Treasury).Mites);
        Assert.Equal(box - 4_000, k.Econ.Ledger.Balance(t.Big.Purse).Mites);
        var fine = Assert.Single(t.Sw.Book.Fines); Assert.Equal(4_000, fine.Mites); Assert.Equal(4_000, fine.Paid);
        var op = Assert.Single(t.Opinions); Assert.Equal(-20, op.Delta);
        Assert.Equal(BusinessState.Open, t.Big.State);

        t.Sw.Seasonal(30); Assert.Equal(BusinessState.Open, t.Big.State);
        t.Sw.Seasonal(45);                                                                    // the third fine: the lord's court closes the shop
        Assert.Equal(3, t.Sw.Book.Fines.Count);
        Assert.Equal(BusinessState.Closed, t.Big.State);
        Assert.Equal((t.Big.Id.Value, 45), Assert.Single(t.Sw.Book.Closings));
        Assert.Equal(0, k.Econ.Ledger.Balance(t.Big.Purse).Mites);                              // the cash box went home to the owner on closing
    }

    [Fact]
    public void ES12_TheDraw_IsFiftyPercent_ByStream0x721_AndDeterministic()
    {
        int Fines()
        {
            var t = MakeTown();
            int n = 0; for (int season = 1; season <= 8; season++) { n += t.Sw.Seasonal(season * 15); if (t.Big.State == BusinessState.Closed) break; }
            return n;
        }
        int a = Fines(), b = Fines();
        Assert.Equal(a, b);                                                                    // same seed, same draws
        Assert.InRange(a, 1, 7);                                                               // not every season, not none
    }

    [Fact]
    public void ES12_WithinTheCaps_OrOutsideATownWithAGuild_NothingIsFined()
    {
        var t = MakeTown(journeymen: 3, scale: s => s with { InspectionPct = 100 });
        Assert.Equal(0, t.Sw.Seasonal(15));
        var v = MakeTown(scale: s => s with { InspectionPct = 100 });
        v.Kit.Pop.Region.Settlements[0].Class = SettlementClass.Village;                      // rural industry: no liberty
        Assert.Equal(0, v.Sw.Seasonal(15));
        Assert.Empty(v.Sw.Book.Fines);
    }

    [Fact]
    public void ES12_ALicensedWorks_IsNotFined_AndPaysItsRent()
    {
        var t = MakeTown(scale: s => s with { InspectionPct = 100 });
        var k = t.Kit; var hero = k.People.IdOf(0);
        var payer = k.Econ.Ledger.Open(PurseKind.Person, hero); k.Econ.Ledger.Mint(payer, new Coin(20_000), SourceReason.Seed);
        t.Sw.OpinionOf = (_, _) => 50;                                                         // the lord and the guild master think well of him
        var r = t.Sw.Petition(t.Big, hero, payer, 10);
        Assert.True(r.Ok, r.Key);
        Assert.Equal(5_000, k.Econ.Ledger.Balance(t.Treasury).Mites);
        Assert.Equal(0, t.Sw.Seasonal(15 * 1));                                               // licensed: not fined
        Assert.Single(t.Sw.Book.Licences);
        long treasury = k.Econ.Ledger.Balance(t.Treasury).Mites;
        t.Sw.Seasonal(30);
        Assert.Equal(treasury + 500, k.Econ.Ledger.Balance(t.Treasury).Mites);                // the season's rent
        Assert.False(t.Sw.Petition(t.Big, hero, payer, 31).Ok);                                // already licensed
    }

    [Fact]
    public void ES13_Licence_TheRuleSumWithTheGuildMastersTerm()
    {
        var rs = new RuleSettingsDef { Id = "rule" };
        // opinion 20 + standing 200 / 10 + 5 gp of fee x 5 = 65; the guild master's -30 leaves 35, under the threshold 40
        var (score, ok, key) = Limits.LicenceTerms(rs, Ss, 20, 200, 5_000, -30, 0);
        Assert.Equal(35, score); Assert.False(ok); Assert.Equal("ld_licence_refused_guild_master", key);
        Assert.True(Limits.LicenceTerms(rs, Ss, 20, 200, 5_000, 0, 0).Granted);
        // every licence already granted in the trade costs 20
        var third = Limits.LicenceTerms(rs, Ss, 20, 200, 5_000, 0, 2);
        Assert.False(third.Granted); Assert.Equal("ld_licence_refused_licences", third.RefusalKey);
        Assert.Equal("ld_licence_refused_opinion", Limits.LicenceTerms(rs, Ss, -50, 200, 5_000, -10, 0).RefusalKey);
    }

    [Fact]
    public void ES13_Petition_RefusedByTheGuildMaster_TakesNoCoin()
    {
        var t = MakeTown();
        var k = t.Kit; var hero = k.People.IdOf(0);
        var payer = k.Econ.Ledger.Open(PurseKind.Person, hero); k.Econ.Ledger.Mint(payer, new Coin(20_000), SourceReason.Seed);
        var master = k.People.IdOf(Limits.MasterOf(k.Sys, Limits.Guilds(EconFixture.Content).First(g => g.Id == "smiths"), 0)).Value;
        t.Sw.OpinionOf = (who, _) => who == master ? -60 : 20;
        t.Sw.StandingWithRealm = (_, _) => 200;
        var r = t.Sw.Petition(t.Big, hero, payer, 10);
        Assert.False(r.Ok); Assert.Equal("ld_licence_refused_guild_master", r.Key);
        Assert.Equal(20_000, k.Econ.Ledger.Balance(payer).Mites); Assert.Empty(t.Sw.Book.Licences);
    }

    // ---------------------------------------------------------------- foremen and the span

    [Fact]
    public void Foreman_NeedsLeadership_EarnsFiftyADay_AndWidensTheSpan()
    {
        var t = MakeTown(journeymen: 3);
        var k = t.Kit; var big = t.Big; int p = big.Staff[0];
        Assert.Equal("sc_foreman_leadership", t.Sw.Appoint(big, p).Key);
        k.People.SetSkill(p, 1, Skill.Leadership, 30);
        Assert.True(t.Sw.Appoint(big, p).Ok);
        Assert.Equal("sc_already_foreman", t.Sw.Appoint(big, p).Key);
        long owed = big.Owed[0];
        k.Sys.DailySlice(At(big.Index % 24));                                                   // the business's own hour: the day's accruals
        Assert.True(big.Owed[0] >= 0 && owed + 50 >= 0);
        Assert.True(t.Sw.Dismiss(big, p).Ok); Assert.False(t.Sw.Book.IsForeman(big.Id.Value, p));
    }

    [Fact]
    public void CrewView_ShowsTheStationsCrewByRole_AndTheSpan()
    {
        var k = new ProdKit(households: 2);
        var b = k.Make("smithy", 100_000, 1, Occupation.Smith, Skill.Smithing, 10, ownerLevel: 70);
        k.Give(b, "iron_bloom", 6_000); k.Give(b, "charcoal", 10_000);
        var st = b.Stations.First(s => s.Kind == "forge");
        Assert.True(k.Sys.TryStart(b, st, k.R("forge_bar"), At(6)));
        k.Sys.WorkHour(At(6));
        var v = CrewViews.Of(k.Sys, null, EconFixture.Content, b, 0);
        var row = Assert.Single(v.Stations);
        Assert.Equal((1, 0, 1), (row.Masters, row.Journeymen, row.Hands));
        Assert.Equal(480 * 20 / 100, row.MinutesM);                                            // forge_bar: 8 h, 200 permille of it the master's steps
        Assert.Equal(2, v.Workers); Assert.Equal(0, v.Excess); Assert.Equal(1000, v.SpeedPm); Assert.Equal(6, v.SpanSum);
        Assert.Equal(0, row.Crowd);
    }

    // ---------------------------------------------------------------- the save section

    [Fact]
    public void ScaleSection_RoundTrips_AndAnEmptyBookWritesNothing()
    {
        var empty = new ScaleBook(); Assert.True(empty.IsEmpty); Assert.Equal(0UL, empty.Hash());
        var b = new ScaleBook();
        b.Foremen[7] = [3, 5]; b.Foremen[9] = [1];
        b.Licences.Add(new ManufactoryLicence { Business = 7, Fief = 2, Guild = "smiths", GrantedDay = 10, FeeMites = 5000, PaidSeason = 3 });
        b.Fines.Add(new GuildFine { Business = 7, Season = 2, Day = 30, Over = 2, Mites = 4000, Paid = 3500 });
        b.Closings.Add((9, 60));
        var back = ScaleSave.Capture(b).Restore();
        Assert.Equal(b.Hash(), back.Hash()); Assert.NotEqual(0UL, b.Hash());
        Assert.Equal([3, 5], back.Foremen[7].ToArray()); Assert.Equal("smiths", back.Licences[0].Guild); Assert.Equal(3500, back.Fines[0].Paid); Assert.Equal((9UL, 60), back.Closings[0]);
        var bytes = SaveCodec.SerializeCompressed(ScaleSave.Capture(b)); var again = SaveCodec.SerializeCompressed(ScaleSave.Capture(back));
        Assert.Equal(bytes, again);                                                             // load and resave: the same bytes
    }
}
