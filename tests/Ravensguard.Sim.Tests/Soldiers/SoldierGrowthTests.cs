using Ravensguard.Content;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.People;
using Ravensguard.Sim.Production;
using Ravensguard.Sim.Rule;
using Ravensguard.Sim.Save;
using Ravensguard.Sim.Soldiers;
using Ravensguard.Sim.Tests.Economy;
using Xunit.Abstractions;
using SoldierGrowth = Ravensguard.Sim.Soldiers.Growth;

namespace Ravensguard.Sim.Tests.Soldiers;

/// <summary>P7-SC-B golden tests (soldiers-and-companies.md 10): SC-7 to SC-12, with the save round trip of the new keys.</summary>
public class SoldierGrowthTests(ITestOutputHelper output)
{
    private static ContentDatabase Db => EconFixture.Content;

    private sealed class NoFit : IKitFit
    {
        public int FitOf(ulong item) => 70_000;
        public int BodyOf(ulong person) => 70_000;
        public bool SetFit(ulong item, int cm3) => true;
    }

    private static SoldierWorld World(SoldierBook? book = null)
    {
        var econ = EconFixture.NewEconomy();
        var rules = Db.All<LargeBuildingRulesDef>().First();
        return new SoldierWorld(book ?? new SoldierBook(), Db, econ, new WorksBook(), rules, new NoFit(), () => null);
    }

    private static ulong P(int n) => EntityId.Make(IdKind.Person, (ulong)n).Value;
    private static EntityId PE(int n) => new(P(n));
    private static void Set(SoldierWorld sw, ulong p, Skill s, int level) => sw.Book.Skills.Set(new EntityId(p), s, level * 1_000_000L);
    private static int Lvl(SoldierWorld sw, ulong p, Skill s) => sw.SkillOf(p, s);

    // ---------------------------------------------------------------- SC-7

    [Fact]
    public void SC7_FightingGrowth_ARecruitAtTwentyIsAtThirtyEightToFortyTwoAfterSixFights()
    {
        var sw = World(); ulong rec = P(1);
        Set(sw, rec, Skill.Polearms, 20);
        for (int f = 0; f < 6; f++) SoldierGrowth.Fight(sw, rec, Skill.Polearms, 40, 40, (long)f * 86400 * 3 + 9 * 3600);     // more blows than a fight counts: the rules' cap holds
        int lvl = Lvl(sw, rec, Skill.Polearms); output.WriteLine("level after 6 fights: " + lvl + " micro " + sw.Book.Skills.Micro(new EntityId(rec), Skill.Polearms));
        Assert.InRange(lvl, 38, 42);
        // the twelve uses an hour hold: the same fight in one hour gains less than when spread over two
        var a = World(); var b = World(); Set(a, rec, Skill.Polearms, 20); Set(b, rec, Skill.Polearms, 20);
        long g1 = SoldierGrowth.Fight(a, rec, Skill.Polearms, 40, 12, 0), g2 = SoldierGrowth.Fight(b, rec, Skill.Polearms, 40, 24, 0);
        Assert.True(g2 > g1);
        // a fight against a man 31 levels worse teaches nothing
        Set(a, rec, Skill.Blades, 80); Assert.Equal(0, SoldierGrowth.Fight(a, rec, Skill.Blades, 40, 18, 5 * 86400));
    }

    // ---------------------------------------------------------------- SC-8

    [Fact]
    public void SC8_DrillCap_SixtyHoursTakeTwentyToFortyAndNoFurther_SparringWithAFiftyFiveTakesHimToFifty()
    {
        var sw = World(); ulong m = P(2);
        Set(sw, m, Skill.Polearms, 20);
        int hours = 0, day = 0;
        while (hours < 60) { hours += SoldierGrowth.Drill(sw, m, Skill.Polearms, 4, day); day++; }                 // asked for 4 hours, the rules give 2 a day
        Assert.Equal(30, day); Assert.Equal(60, hours);
        Assert.Equal(40, Lvl(sw, m, Skill.Polearms));
        for (int i = 0; i < 20; i++) SoldierGrowth.Drill(sw, m, Skill.Polearms, 2, day++);
        Assert.Equal(40, Lvl(sw, m, Skill.Polearms));                                                      // no further
        // shield bearers stop at 35
        ulong s = P(3); Set(sw, s, Skill.Shields, 20);
        for (int i = 0; i < 40; i++) SoldierGrowth.Drill(sw, s, Skill.Shields, 2, i);
        Assert.Equal(35, Lvl(sw, s, Skill.Shields));
        // sparring with a 55 takes him to 50 (partner - 5), and no further; a partner no better teaches nothing
        for (int i = 0; i < 80; i++) SoldierGrowth.Spar(sw, m, Skill.Polearms, 55, 2, 100 + i);
        Assert.Equal(50, Lvl(sw, m, Skill.Polearms));
        ulong n = P(4); Set(sw, n, Skill.Blades, 30); SoldierGrowth.Spar(sw, n, Skill.Blades, 30, 2, 1); Assert.Equal(30, Lvl(sw, n, Skill.Blades));
        // and the ceiling of 60
        ulong q = P(5); Set(sw, q, Skill.Blades, 20);
        for (int i = 0; i < 200; i++) SoldierGrowth.Spar(sw, q, Skill.Blades, 90, 2, i);
        Assert.Equal(60, Lvl(sw, q, Skill.Blades));
    }

    // ---------------------------------------------------------------- SC-9

    [Fact]
    public void SC9_FormationDrill_TwentyAnHour_FiveADayWithout_ASixHundredUnitClosesRanksInSixteenTicks()
    {
        var sw = World();
        Assert.Equal(20, SoldierGrowth.FormationHours(sw, 7, 1, 1));
        Assert.Equal(100, SoldierGrowth.FormationHours(sw, 7, 4, 2));
        SoldierGrowth.FormationDay(sw, 2); Assert.Equal(100, SoldierGrowth.DrillOf(sw, 7));                                // drilled today: no loss
        SoldierGrowth.FormationDay(sw, 3); Assert.Equal(95, SoldierGrowth.DrillOf(sw, 7));
        for (int d = 4; d < 8; d++) SoldierGrowth.FormationDay(sw, d);
        Assert.Equal(75, SoldierGrowth.DrillOf(sw, 7));
        for (int h = 0; h < 40; h++) SoldierGrowth.FormationHours(sw, 8, 3, 10);
        Assert.Equal(1000, SoldierGrowth.DrillOf(sw, 8));                                                          // clamped
        SoldierGrowth.FormationHours(sw, 9, 30, 10); Assert.Equal(600, SoldierGrowth.DrillOf(sw, 9));
        Assert.Equal(16, SoldierGrowth.CloseRanksTicks(sw.Roles, 600));
        Assert.Equal(40, SoldierGrowth.CloseRanksTicks(sw.Roles, 0)); Assert.Equal(28, SoldierGrowth.CloseRanksTicks(sw.Roles, 300));
        for (int d = 11; d < 11 + 130; d++) SoldierGrowth.FormationDay(sw, d);
        Assert.Equal(0, SoldierGrowth.DrillOf(sw, 9)); Assert.DoesNotContain(9L, sw.Book.Units.Keys);               // lost drill is gone from the book
    }

    // ---------------------------------------------------------------- SC-10

    [Fact]
    public void SC10_Promotion_ASpearmanWithPolearms45AndShields30IsOfferedManAtArmsAtTheSeasonsStart_WithTheNewTemplatesGaps()
    {
        var sw = World(); ulong sp = P(10), raw = P(11);
        sw.Book.Rows[sp] = new SoldierRow { Person = sp, Role = "spearman", Lord = 0 };
        sw.Book.Rows[raw] = new SoldierRow { Person = raw, Role = "spearman", Lord = 0 };
        Set(sw, sp, Skill.Polearms, 45); Set(sw, sp, Skill.Shields, 30); Set(sw, raw, Skill.Polearms, 44); Set(sw, raw, Skill.Shields, 30);
        int start = TimeConst.DaysPerSeason * 5;
        Assert.Empty(Promotions.Offer(sw, 0, start + 1, 4242));                                              // not the season's first day
        var offers = Promotions.Offer(sw, 0, start, 4242);
        var o = Assert.Single(offers);
        Assert.Equal(sp, o.Offer.Person); Assert.Equal("man_at_arms", o.Offer.Role);
        Assert.Contains(o.Gaps, g => g.Slot == "armour" && g.Required);                                      // an empty armoury: the template's required slots show as gaps
        output.WriteLine("gaps: " + string.Join(", ", o.Gaps.Select(g => g.Slot)));
        // nothing moved until he answers; accepting sets the role, the wage and one chronicle entry
        Assert.Equal("spearman", sw.Book.Of(sp)!.Role);
        var chron = new Chronicle(); var ids = new IdAllocator(); var rc = RuleChron.Of(chron, ids);
        Assert.True(Promotions.Accept(sw, sp, start, rc, PE(900)));
        Assert.Equal("man_at_arms", sw.Book.Of(sp)!.Role); Assert.Equal(start, sw.Book.Of(sp)!.PromotedDay);
        Assert.Equal(75 * 10, WageRoll.WageMites(sw, "man_at_arms"));
        Assert.Equal(1, chron.Count); var e = chron.All.Single();
        Assert.Equal("soldier_promoted", e.TypeId); Assert.Contains(e.Causes, c => c.NoteKey == "ch_note_so_skill");
        Assert.False(Promotions.Accept(sw, sp, start, rc, PE(900)));                                         // answered once
        // a refused offer is gone; an AI lord with no room refuses the dearer role
        Promotions.Offer(sw, 0, start + TimeConst.DaysPerSeason);                                            // sp has nothing further: no knight by skill alone
        Assert.Empty(sw.Book.Offers);
        Set(sw, raw, Skill.Polearms, 45); sw.Book.Rows[raw].Role = "spearman";
        Promotions.Offer(sw, 0, start + 2 * TimeConst.DaysPerSeason);
        Assert.Equal(0, Promotions.AcceptWithin(sw, 0, start, rc, PE(900), WageRoll.DailyMites(sw, 0)));      // the roll would pass the budget
        Assert.Empty(sw.Book.Offers); Assert.Equal("spearman", sw.Book.Of(raw)!.Role);
    }

    // ---------------------------------------------------------------- SC-11

    [Fact]
    public void SC11_Dubbing_AManAtArmsWith55AndRiding45AndACourserIsDubbed_Entry_StandingPlus100_Wage150()
    {
        var sw = World(); ulong m = P(20);
        sw.Book.Rows[m] = new SoldierRow { Person = m, Role = "man_at_arms", Lord = 0 };
        Set(sw, m, Skill.Blades, 55); Set(sw, m, Skill.Riding, 45); Set(sw, m, Skill.Shields, 30);
        var chron = new Chronicle(); var rc = RuleChron.Of(chron, new IdAllocator());
        Assert.False(Promotions.Dub(sw, m, 400, rc, PE(900)));                                               // no courser
        sw.HasWarHorse = p => p == m;
        Set(sw, m, Skill.Riding, 44); Assert.False(Promotions.Dub(sw, m, 400, rc, PE(900))); Set(sw, m, Skill.Riding, 45);
        Assert.Equal(0, chron.Count);
        // by skill alone a knight is never offered
        Assert.Null(Promotions.NextFor(sw, sw.Book.Of(m)!));
        Assert.True(Promotions.Dub(sw, m, 400, rc, PE(900)));
        Assert.Equal("knight", sw.Book.Of(m)!.Role); Assert.Equal(150, sw.Roles.WageCp("knight")); Assert.Equal(1500, WageRoll.WageMites(sw, "knight"));
        var e = chron.All.Single(); Assert.Equal("knight_dubbed", e.TypeId); Assert.Equal(PE(20).Value, e.Subjects[0].Value);
        // the standing is the deed table's: the first person of the entry gains 100 permille with the lord's fief
        var deed = Db.All<DeedDef>().Single(d => d.Entry == "knight_dubbed"); Assert.Equal(100, deed.FiefPm);
        // a knight is not dubbed twice
        Assert.False(Promotions.Dub(sw, m, 401, rc, PE(900)));
        // the entry is a legal chronicle entry (a typed root with its note)
        var types = ChronicleTypes.From(Db); Assert.NotNull(types.Get("knight_dubbed")); Assert.NotNull(types.Get("soldier_promoted"));
    }

    // ---------------------------------------------------------------- SC-12

    [Fact]
    public void SC12_RetinueCap_ABaronHeroHoldsFortySoldiersBeyondHisCompanions_TheFortyFirstIsRefusedWithTheReason()
    {
        var sw = World(); sw.HoldingOf = l => l == 0 ? "baron" : "";
        var comp = new HashSet<ulong> { P(100), P(101), P(102) };
        sw.CompanionsOf = l => comp;
        foreach (var c in comp) sw.Book.Rows[c] = new SoldierRow { Person = c, Role = "spearman", Lord = 0 };      // companions take roles too, outside the cap
        for (int i = 0; i < 40; i++)
        {
            ulong p = P(200 + i); Assert.True(Retinues.CanEnlist(sw, 0, p).Ok, "man " + i);
            sw.Book.Rows[p] = new SoldierRow { Person = p, Role = "spearman", Lord = 0 };
        }
        var no = Retinues.CanEnlist(sw, 0, P(300));
        Assert.False(no.Ok); Assert.Equal("baron", no.Holding); Assert.Equal(40, no.Cap); Assert.Equal(40, no.Have);
        Assert.True(Retinues.CanEnlist(sw, 0, P(103) is var c4 && comp.Add(c4) ? c4 : 0).Ok);                  // a new companion still comes: the squad cap rules him
        Assert.Equal(6, Retinues.CapOf(sw.Roles, "manor")); Assert.Equal(12, Retinues.CapOf(sw.Roles, "knights_fee"));
        Assert.Equal(120, Retinues.CapOf(sw.Roles, "count")); Assert.Equal(300, Retinues.CapOf(sw.Roles, "ruler"));
        Assert.Equal(int.MaxValue, Retinues.CapOf(sw.Roles, ""));                                              // no holding known: no cap
    }

    // ---------------------------------------------------------------- wages, saves

    [Fact]
    public void WageRoll_PaysFromThePurseByTransfer_ArrearsWhenItCannot_CoinConserved()
    {
        var econ = EconFixture.NewEconomy(); var led = econ.Ledger;
        var from = PurseId.None;
        var sw = new SoldierWorld(new SoldierBook(), Db, econ, new WorksBook(), Db.All<LargeBuildingRulesDef>().First(), new NoFit(), () => null);
        // the lord has no purse, so each wage is arrears: nothing is minted
        ulong a = P(1); sw.Book.Rows[a] = new SoldierRow { Person = a, Role = "spearman", Lord = 0 };
        sw.PurseHook = p => p == a ? led.Open(PurseKind.Person, new EntityId(a)) : null;          // P7R-8: a man with no purse is owed nothing, so he has one here
        long before = led.Audit().IsConserved ? 1 : 0;
        Assert.Equal(0, WageRoll.PayDay(sw, 0, from, led));
        Assert.Equal(250, sw.Book.Of(a)!.ArrearsMites); Assert.Equal(1, before); Assert.True(led.Audit().IsConserved);
        Assert.Single(WageRoll.Rows(sw, 0)); Assert.Equal(250, WageRoll.DailyMites(sw, 0));
    }

    [Fact]
    public void Save_TheGrowthKeys_RoundTripByteIdentical()
    {
        var w = EconFixture.WorldWith(null);
        w.Soldiers = new SoldierBook();
        var row = new SoldierRow { Person = 5, Role = "spearman", Lord = 0, ArrearsMites = 250 }; row.DrillHours["Polearms"] = 14; w.Soldiers.Rows[5] = row;
        w.Soldiers.Skills.Set(new EntityId(5), Skill.Polearms, 33_500_000); w.Soldiers.Skills.Use(new EntityId(5), Skill.Polearms, 40, 40, 3600);
        w.Soldiers.Units[7] = new UnitDrill { Value = 600, LastDay = 12 };
        w.Soldiers.Offers[5] = new PromotionOffer(5, "man_at_arms", 90);
        var file = WorldSave.Capture(w, EconFixture.ContentHash, "0.1.0");
        var back = WorldSave.Restore(SaveCodec.Decode(SaveCodec.Encode(file)), EconFixture.Content, EconFixture.ContentHash);
        Assert.Equal(w.Soldiers.Hash(), back.Soldiers!.Hash());
        Assert.Equal(250, back.Soldiers.Rows[5].ArrearsMites); Assert.Equal(14, back.Soldiers.Rows[5].DrillHours["Polearms"]);
        Assert.Equal(600, back.Soldiers.Units[7].Value); Assert.Equal("man_at_arms", back.Soldiers.Offers[5].Role);
        Assert.Equal(w.Soldiers.Skills.Micro(new EntityId(5), Skill.Polearms), back.Soldiers.Skills.Micro(new EntityId(5), Skill.Polearms));
        Assert.Equal(SaveCodec.Encode(file), SaveCodec.Encode(WorldSave.Capture(back, EconFixture.ContentHash, "0.1.0")));
    }
}
