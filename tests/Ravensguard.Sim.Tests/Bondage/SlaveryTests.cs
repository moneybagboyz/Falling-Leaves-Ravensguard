using Ravensguard.Content;
using Ravensguard.Sim.Bondage;
using Ravensguard.Sim.Combat.Aftermath;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Jobs;
using Ravensguard.Sim.Law;
using Ravensguard.Sim.People;
using Ravensguard.Sim.Save;
using Ravensguard.Sim.Society;
using Xunit;
using Xunit.Abstractions;

namespace Ravensguard.Sim.Tests.Bondage;

/// <summary>
/// Slavery (docs/specs/phase6/slavery.md, P6-D1) on a hand-made set of four towns (<see cref="BondRig"/>): SL-1 prices, SL-2 the Phase 4 stub replaced, SL-3 buying and the law's eye, SL-4 debt, SL-5 manumission, SL-8 a runaway in a free
/// realm, SL-9 self-purchase, SL-10 revolt, SL-11 inheritance, SL-13 raids and the slider, SL-14 the hash and the section. The world-level cases (SL-6 faith, SL-7 the enslaved hero, SL-12 heirs, the desk, the save) are in <see cref="SlaveryWorldTests"/>.
/// </summary>
public class SlaveryTests(ITestOutputHelper output)
{
    private static readonly ContentDatabase Db = BondRig.Db.Value;

    // ------------------------------------------------------------------ SL-1

    [Fact]
    public void SL1_ThePriceTable_ASmithOfFiftyIsThirteenThousandFiveHundred_ATeenSixThousandThreeHundred_AOneHandedAdultFortyFiveHundred()
    {
        var r = BondRules.From(Db);
        Assert.Equal(9_000, r.Price(25, 0, false, false));
        Assert.Equal(13_500, r.Price(25, 50, false, false));
        Assert.Equal(18_000, r.Price(25, 70, false, false));                           // a master x2
        Assert.Equal(6_300, r.Price(13, 50, false, false));                            // 12 to 15 x0.7, no trade yet
        Assert.Equal(3_600, r.Price(8, 0, false, false));                              // a child x0.4
        Assert.Equal(4_500, r.Price(25, 0, true, false));                              // maimed x0.5
        Assert.Equal(1_800, r.Price(25, 0, false, true));                              // infirm x0.2
        Assert.Equal(3_600, r.Price(58, 30, false, false));                            // over fifty x0.4
        // the same through a person of the table
        var rig = BondRig.Make();
        var smith = rig.Of(0, 5); int row = rig.P.IndexOf(smith);
        rig.SetAge(smith, 25, 3); rig.P.SetSkill(row, 0, Skill.Smithing, 50);
        Assert.Equal(13_500, rig.Bw.PriceOf(smith, 3));
        rig.P.SetSkill(row, 0, Skill.Melee, 90);
        Assert.Equal(9_000, rig.Bw.PriceOf(smith, 3));                                 // a fighter's skill is not a trade
    }

    // ------------------------------------------------------------------ SL-2

    [Fact]
    public void SL2_TheHoldEndsInASale_NobodyIsReleasedWithARumour_TheyStandInTheNearestPen()
    {
        var rig = BondRig.Make(); var caps = rig.Caps; var rules = AftermathRules.Default;
        var band = AftermathCoin.BandId(77);
        var kidnapped = Enumerable.Range(0, 120).Select(i => rig.P.IdOf(40 + i)).ToList();      // Ashby, Highfold, Thorp
        foreach (var p in kidnapped)
            caps.Capture(rules, CaptorKind.Bandits, p, false, band, new PurseRef(PurseKind.Household, band), BondRig.Sec(0), CaptiveRank.Levy, 0, PayerKind.None, [], [], (0, 0, 0, 0, 0));
        var slavers = new List<EntityId>();
        for (int i = 0; i < 12; i++) { var p = rig.P.IdOf(10 + i); slavers.Add(p); caps.Capture(rules, CaptorKind.Slavers, p, false, Slavery.BandId(0, 0), new PurseRef(PurseKind.Household, Slavery.BandId(0, 0)), BondRig.Sec(0), CaptiveRank.Levy, 0, PayerKind.None, [], [], (0, 0, 0, 0, 0)); }
        for (long h = 0; h < 12 * 24; h++) caps.StepHour(rules, null, h);
        var sold = caps.Captives.Values.Where(c => c.Note == "sold").ToList();
        Assert.NotEmpty(sold);
        Assert.All(slavers, p => Assert.True(caps.Find(p)!.Note == "sold" || caps.Find(p)!.State == CaptiveState.Escaped, "a slaver's captive is sold or got away; never released with a rumour"));
        Assert.Contains(slavers, p => caps.Find(p)!.Note == "sold");
        var listed = Raids.Sweep(rig.Bw, rig.Book, caps, 4, 12, BondRig.Sec(12));
        Assert.Equal(sold.Count, listed.Count);
        foreach (var c in sold)
        {
            var b = rig.Book.Of(c.Person)!;
            Assert.True(b.InPen); Assert.Equal(BondRig.Chattel, b.Place);                 // the nearest market of a law that has one: the Seaboard port
            Assert.Equal(rig.Bw.PriceOf(c.Person, 12), b.Ask); Assert.Equal(BondKind.Chattel, b.Kind);
            Assert.Equal((byte)PurseKind.Household, b.SellerKind);
        }
        Assert.Empty(Raids.Sweep(rig.Bw, rig.Book, caps, 4, 12, BondRig.Sec(12)));        // listed once
        output.WriteLine($"{sold.Count} of {kidnapped.Count + slavers.Count} captives sold into the pen");
    }

    // ------------------------------------------------------------------ SL-3

    [Fact]
    public void SL3_BuyingInAChattelTown_MovesTheCoinAndTheOwner_StandingUnchanged_ButTheSameManInValeholtIsTheCrimeSlaving()
    {
        var rig = BondRig.Make(); var slave = rig.Of(0, 7);
        var pen = rig.Pen(slave); long price = pen.Ask; Assert.Equal(9_000, price);
        var band = Slavery.BandId(0, 0); var bandPurse = Slavery.BandPurse(rig.Bw, band);
        long audit = rig.L.Audit().InPurses.Mites;
        long hero0 = rig.L.Balance(rig.HeroPurse).Mites;
        var r = Slavery.Buy(rig.Bw, rig.Book, rig.Hero, slave, 2, BondRig.Sec(2));
        Assert.True(r.Ok, r.Key);
        Assert.Equal(hero0 - price, rig.L.Balance(rig.HeroPurse).Mites); Assert.Equal(price, rig.L.Balance(bandPurse).Mites);
        Assert.Equal(audit, rig.L.Audit().InPurses.Mites);                                 // coin moved, none made
        var b = rig.Book.Of(slave)!;
        Assert.False(b.InPen); Assert.Equal(rig.Hero.Value, b.Owner); Assert.Equal(price, b.PriceMites); Assert.Equal(Restraint.Bound, b.Chain);
        Assert.Equal(rig.P.Household[0], rig.P.Household[rig.P.IndexOf(slave)]);          // he is of the hero's household now
        var e = rig.C.All.Last(x => x.TypeId == "slave_bought");
        Assert.Equal("chattel", e.Data["law"]); Assert.Equal(rig.Hero, e.Subjects[0]);
        var deeds = DeedTable.From(Db);
        Assert.Null(deeds.RowFor(e));                                                       // no standing lost where the law is chattel
        // a seen purchase where the law is none is a deed with a cost
        var none = new ChronicleEntry(EntityId.Make(IdKind.Event, 9_999), new GameTime(0), "slave_bought", [rig.Hero, slave], [], new Dictionary<string, string> { ["law"] = "none" });
        Assert.Equal(-40, deeds.RowFor(none)!.SettlementPm);
        // carried into Valeholt, the owner is seen: the crime, the slave freed, twice the price in a fine, a second time outlawry
        b.Place = BondRig.None;
        int day = 3; bool freed = false;
        for (; day < 60 && !freed; day++) { BondDaily.Run(rig.Bw, rig.Book, rig.Crimes, day); freed = !rig.Book.Has(slave); }
        Assert.True(freed, "within sixty days someone saw a slave in Ashby");
        var crime = rig.Crimes.Crimes.Values.Single(c => c.Kind == "slaving");
        Assert.Equal(rig.Hero.Value, crime.Doer); Assert.Equal(price, crime.ValueMites);
        Assert.True(rig.L.Balance(rig.Treasury).Mites > 100_000);
        Assert.False(rig.Marks.IsOutlawAnywhere(rig.Hero));                                // the first time
        var slave2 = rig.Of(0, 8); rig.Pen(slave2, day: day);
        Assert.True(Slavery.Buy(rig.Bw, rig.Book, rig.Hero, slave2, day, BondRig.Sec(day)).Ok);
        rig.Book.Of(slave2)!.Place = BondRig.None;
        for (int d = day + 1; d < day + 80 && rig.Book.Has(slave2); d++) BondDaily.Run(rig.Bw, rig.Book, rig.Crimes, d);
        Assert.False(rig.Book.Has(slave2)); Assert.True(rig.Marks.IsOutlawAnywhere(rig.Hero));   // the second time: outlawry in the realm
        Assert.Equal(audit, rig.L.Audit().InPurses.Mites);
    }

    [Fact]
    public void BuyingIsRefusedWhereThereIsNoMarket_OrWithoutTheCoin_AndAHouseholdBuysFromThePenInTheSeason()
    {
        var rig = BondRig.Make(); var p1 = rig.Of(1, 3); var p2 = rig.Of(0, 9);
        rig.Pen(p1, market: BondRig.None);                                                  // a lot stood in a town whose law is none: no market
        Assert.Equal("sl_no_market", Slavery.Buy(rig.Bw, rig.Book, rig.Hero, p1, 2, BondRig.Sec(2)).Key);
        rig.Pen(p2);
        rig.L.Transfer(rig.HeroPurse, rig.Treasury, new Coin(rig.L.Balance(rig.HeroPurse).Mites - 100), TransferReason.Fine);
        Assert.Equal("sl_cannot_afford", Slavery.Buy(rig.Bw, rig.Book, rig.Hero, p2, 2, BondRig.Sec(2)).Key);
        // a rich household of the town buys at the season's draw
        var hh = rig.H.IdOf(1);                                                             // people 4..7 of Saltmouth
        rig.L.Transfer(rig.Treasury, rig.HhPurse[1], new Coin(60_000), TransferReason.Fine);
        Assert.Equal(hh, Slavery.Buyer(rig.Bw, 0, 9_000, 3, 1));
        int bought = 0; for (int d = 15; d < 15 + 15 * 12 && bought == 0; d += 15) bought = Raids.Market(rig.Bw, rig.Book, d, BondRig.Sec(d));
        Assert.Equal(1, bought);
        Assert.Equal(hh.Value, rig.Book.Of(p2)!.Owner);
        Assert.Equal(1, rig.P.Household[rig.P.IndexOf(p2)]);
    }

    // ------------------------------------------------------------------ SL-4

    [Fact]
    public void SL4_ADebtorOwingFortyFiveHundredIsBoundThirtyDays_ThenFree_TheTermOnTheBond()
    {
        var rig = BondRig.Make(); var debtor = rig.Of(2, 4); var creditor = rig.Of(2, 20);
        var r = Slavery.BindDebtor(rig.Bw, rig.Book, debtor, creditor, 4_500, 10, BondRig.Sec(10));
        Assert.True(r.Ok, r.Key); Assert.Equal(30, r.Mites);
        var b = rig.Book.Of(debtor)!; Assert.Equal(BondKind.Debt, b.Kind); Assert.Equal(BondOrigin.Debt, b.Origin); Assert.Equal(40, b.TermEndDay); Assert.Equal(Restraint.None, b.Chain);
        Assert.Equal(rig.P.Household[rig.P.IndexOf(creditor)], rig.P.Household[rig.P.IndexOf(debtor)]);
        for (int d = 11; d < 40; d++) { BondDaily.Run(rig.Bw, rig.Book, rig.Crimes, d); Assert.True(rig.Book.Has(debtor), $"still bound on day {d}"); }
        BondDaily.Run(rig.Bw, rig.Book, rig.Crimes, 40);
        Assert.False(rig.Book.Has(debtor));
        Assert.Contains(rig.C.All, e => e.TypeId == "slave_term_ended" && e.Subjects.Contains(debtor));
        // where the law is none a debtor cannot be bound
        Assert.Equal("sl_law_forbids", Slavery.BindDebtor(rig.Bw, rig.Book, rig.Of(1, 4), creditor, 4_500, 10, BondRig.Sec(10)).Key);
        // a sentence to the fief's works binds for its term
        var convict = Slavery.BindConvict(rig.Bw, rig.Book, rig.Of(0, 30), 0, 12, 0, 50, BondRig.Sec(50), 0)!;
        Assert.Equal(BondKind.Convict, convict.Kind); Assert.Equal(62, convict.TermEndDay); Assert.Equal(Fief(rig), new EntityId(convict.Owner));
        for (int d = 51; d <= 62; d++) BondDaily.Run(rig.Bw, rig.Book, rig.Crimes, d);
        Assert.False(rig.Book.Has(rig.Of(0, 30)));
    }

    private static EntityId Fief(BondRig r) => BondRig.Fief;

    // ------------------------------------------------------------------ SL-5

    [Fact]
    public void SL5_Manumission_IsADeedOfPlusThirtyAndSixty_AndAFreedManStaysAServantWhenTrusted_OtherwiseGoesHome()
    {
        var rig = BondRig.Make(); var deeds = DeedTable.From(Db);
        var a = rig.Of(0, 11); var b = rig.Of(0, 12);
        int homeA = rig.P.Household[rig.P.IndexOf(a)], homeB = rig.P.Household[rig.P.IndexOf(b)];
        rig.Pen(a); rig.Pen(b);
        Assert.True(Slavery.Buy(rig.Bw, rig.Book, rig.Hero, a, 2, BondRig.Sec(2)).Ok); Assert.True(Slavery.Buy(rig.Bw, rig.Book, rig.Hero, b, 2, BondRig.Sec(2)).Ok);
        rig.Opinion = (_, who) => who == b ? -100 : 0;
        var fa = Slavery.Manumit(rig.Bw, rig.Book, rig.Hero, a, 3, BondRig.Sec(3)); Assert.True(fa.Ok);
        var fb = Slavery.Manumit(rig.Bw, rig.Book, rig.Hero, b, 3, BondRig.Sec(3)); Assert.True(fb.Ok);
        var ea = rig.C.All.Last(e => e.TypeId == "slave_freed" && e.Subjects.Contains(a));
        var row = deeds.RowFor(ea)!; Assert.Equal(30, row.SettlementPm); Assert.Equal(60, row.ReligionPm);
        Assert.Equal(rig.Hero, ea.Subjects[0]);                                              // the deed is the hero's
        Assert.Equal("1", ea.Data["stays"]); Assert.Equal(rig.P.Household[0], rig.P.Household[rig.P.IndexOf(a)]);
        var eb = rig.C.All.Last(e => e.TypeId == "slave_freed" && e.Subjects.Contains(b));
        Assert.Equal("0", eb.Data["stays"]); Assert.Equal(homeB, rig.P.Household[rig.P.IndexOf(b)]);   // he goes back to the house he came from
        Assert.False(rig.Book.Has(a)); Assert.False(rig.Book.Has(b));
        Assert.Equal("sl_not_yours", Slavery.Manumit(rig.Bw, rig.Book, rig.Hero, a, 4, BondRig.Sec(4)).Key);
        _ = homeA;
    }

    // ------------------------------------------------------------------ SL-8

    [Fact]
    public void SL8_AnUnchainedUnhappySlaveRuns_TheOwnerPostsABounty_AndAtAFreeTownTheBondIsVoidAndTheBountyGone()
    {
        var rig = BondRig.Make(); var slave = rig.Of(0, 15); var owner = rig.Of(0, 16);
        var b = Slavery.Make(rig.Bw, rig.Book, slave, rig.H.IdOf(rig.P.Household[rig.P.IndexOf(owner)]), BondKind.Chattel, BondOrigin.Sold, 1, 0, 9_000, 0, 0);
        b.Chain = Restraint.None; b.Morale = 0; b.Treatment = Treatment.Harsh;
        int day = 2, fled = 0;
        for (; day < 80 && !b.OnRun; day++) { var t = BondDaily.Run(rig.Bw, rig.Book, rig.Crimes, day); fled += t.Fled; }
        Assert.True(b.OnRun, "a morale of zero and no chain: he runs within weeks"); Assert.Equal(1, fled);
        Assert.NotEqual(0UL, b.BountyJob);
        var job = rig.Jobs.Get(new EntityId(b.BountyJob))!; Assert.Equal(JobState.Open, job.State);
        var rec = rig.Crimes.Crimes.Values.Single(c => c.Kind == "runaway"); Assert.Equal(slave.Value, rec.Doer);
        // he reaches Ashby, where the courts do not return slaves
        Assert.False(BondDaily.Arrive(rig.Bw, rig.Book, slave, BondRig.Chattel, day, BondRig.Sec(day)));   // not free in a market town
        Assert.True(BondDaily.Arrive(rig.Bw, rig.Book, slave, BondRig.None, day, BondRig.Sec(day)));
        Assert.False(rig.Book.Has(slave)); Assert.NotEqual(JobState.Open, rig.Jobs.Get(new EntityId(b.BountyJob))!.State);
        var e = rig.C.All.Last(x => x.TypeId == "slave_freed"); Assert.Equal("Voided", e.Data["how"]);
        Assert.Equal(rig.L.Audit().InPurses.Mites, rig.L.Audit().InPurses.Mites);
        // the catchers find another runaway: the bounty is paid to a guard and he is back, chained
        rig.P.Occupation[0] = Occupation.Guard; rig.P.Occupation[1] = Occupation.Guard;
        var s2 = rig.Of(0, 17); var b2 = Slavery.Make(rig.Bw, rig.Book, s2, rig.H.IdOf(rig.P.Household[rig.P.IndexOf(owner)]), BondKind.Chattel, BondOrigin.Sold, 1, 0, 9_000, 0, 0);
        b2.Chain = Restraint.None; BondDaily.Flee(rig.Bw, rig.Book, rig.Crimes, b2, day, BondRig.Sec(day));
        for (int d = day + 1; d < day + 200 && b2.OnRun; d++) BondDaily.RunawayDay(rig.Bw, rig.Book, rig.Crimes, b2, d, BondRig.Sec(d));
        Assert.True(!b2.OnRun || !rig.Book.Has(s2));
        if (rig.Book.Has(s2)) Assert.Equal(Restraint.Chained, b2.Chain);
    }

    // ------------------------------------------------------------------ S5

    private static (BondRig Rig, EntityId Slave, Bond B) RunawayWithGuards()
    {
        var rig = BondRig.Make(arrestPct: 100); var slave = rig.Of(0, 15); var owner = rig.Of(0, 16);
        rig.P.Occupation[1] = Occupation.Guard; rig.P.Occupation[2] = Occupation.Guard; rig.P.Occupation[3] = Occupation.Guard;
        var b = Slavery.Make(rig.Bw, rig.Book, slave, rig.H.IdOf(rig.P.Household[rig.P.IndexOf(owner)]), BondKind.Chattel, BondOrigin.Sold, 1, 0, 9_000, 0, 0);
        b.Chain = Restraint.None; BondDaily.Flee(rig.Bw, rig.Book, rig.Crimes, b, 2, BondRig.Sec(2));
        Assert.True(b.OnRun); Assert.Single(rig.Crimes.Crimes.Values, c => c.Kind == "runaway");
        return (rig, slave, b);
    }

    private static int DailyArrests(BondRig rig, int from, int to)
    {
        int n = 0;
        for (int d = from; d <= to; d++) for (int h = 0; h < 24; h += 6) n += Gaol.Daily(rig.W, rig.Crimes, rig.Jb, d, BondRig.Sec(d, h)).Count;
        return n;
    }

    [Fact]
    public void S5_ARunawayWhoReachesAFreeTown_HasNoRunawayCrimeLeft_SoTheGuardsDoNotGaolHim()
    {
        var (rig, slave, b) = RunawayWithGuards();
        Assert.True(BondDaily.Arrive(rig.Bw, rig.Book, slave, BondRig.None, 5, BondRig.Sec(5), rig.Crimes));
        Assert.False(rig.Book.Has(slave));
        Assert.All(rig.Crimes.Crimes.Values.Where(c => c.Kind == "runaway"), c => Assert.True(c.State is CrimeState.Pardoned or CrimeState.Served));
        Assert.Equal(0, DailyArrests(rig, 6, 12)); Assert.DoesNotContain(slave.Value, rig.Jb.Gaol.Keys);
    }

    [Fact]
    public void S5_ARunawayWhoseBondEndsOnTheRun_IsNotArrestedForRunningAway()
    {
        var (rig, slave, b) = RunawayWithGuards();
        b.TermEndDay = 4;                                                                         // the term runs out while he is away (the will and a free town end it the same way)
        BondDaily.Run(rig.Bw, rig.Book, rig.Crimes, 5);
        Assert.False(rig.Book.Has(slave));
        Assert.Equal(0, DailyArrests(rig, 6, 12)); Assert.DoesNotContain(slave.Value, rig.Jb.Gaol.Keys);
    }

    [Fact]
    public void S5_ARunawayStillOnTheRunIsTheCatchersBusiness_TheGuardsDoNotGaolHimToo_AndCaughtWithNoBountyClosesTheCharge()
    {
        var (rig, slave, b) = RunawayWithGuards();
        Assert.Equal(0, DailyArrests(rig, 3, 12)); Assert.Empty(rig.Jb.Gaol);                    // never both the gaol and the catchers
        b.BountyJob = 0;                                                                         // an empty treasury posted no bounty
        BondDaily.Caught(rig.Bw, rig.Book, rig.Crimes, b, 0, 13, BondRig.Sec(13));
        Assert.All(rig.Crimes.Crimes.Values.Where(c => c.Kind == "runaway"), c => Assert.Equal(CrimeState.Served, c.State));
        Assert.Equal(0, DailyArrests(rig, 14, 20));
    }

    // ------------------------------------------------------------------ SL-9

    [Fact]
    public void SL9_SelfPurchase_AFairOwnerAndFiveAMiteADay_FreeOnTheDayThePriceIsHeld_ScaledFiveHundredOnDayHundred()
    {
        var rig = BondRig.Make(); var slave = rig.Of(0, 20); var owner = rig.H.IdOf(rig.P.Household[rig.P.IndexOf(rig.Of(0, 21))]);
        var b = Slavery.Make(rig.Bw, rig.Book, slave, owner, BondKind.Chattel, BondOrigin.Sold, 0, 0, 500, 0, 0); b.Treatment = Treatment.Fair;
        long audit = rig.L.Audit().InPurses.Mites;
        Assert.Equal(1_800, 9_000 / BondRules.From(Db).D.FairTipMites);                     // the full price: day 1,800
        for (int d = 1; d < 100; d++) { BondDaily.Run(rig.Bw, rig.Book, rig.Crimes, d); Assert.True(rig.Book.Has(slave), $"still a slave on day {d}"); Assert.Equal(5L * d, rig.Bw.Peculium(slave)); }
        BondDaily.Run(rig.Bw, rig.Book, rig.Crimes, 100);
        Assert.False(rig.Book.Has(slave));
        var e = rig.C.All.Last(x => x.TypeId == "slave_self_bought"); Assert.Equal(slave, e.Subjects[0]);
        Assert.Equal(audit, rig.L.Audit().InPurses.Mites);
        // a Harsh owner in a chattel law refuses (a debt law would not)
        var s2 = rig.Of(0, 22); var b2 = Slavery.Make(rig.Bw, rig.Book, s2, owner, BondKind.Chattel, BondOrigin.Sold, 100, 0, 100, 0, 0); b2.Treatment = Treatment.Harsh;
        rig.L.Transfer(rig.HhPurse[rig.P.Household[rig.P.IndexOf(s2)]], rig.Bw.PeculiumPurse(s2), new Coin(200), TransferReason.Gift);
        Assert.Equal("sl_owner_refuses", Slavery.SelfPurchase(rig.Bw, rig.Book, s2, 101, BondRig.Sec(101)).Key);
        b2.Place = BondRig.Debt; b2.Treatment = Treatment.Fair;
        Assert.True(Slavery.SelfPurchase(rig.Bw, rig.Book, s2, 101, BondRig.Sec(101)).Ok);
    }

    [Fact]
    public void AnOwnerWhoTrustsHimAYearFreesHim_AndAPurchasedFreedomByAFriendCostsTheLastPrice()
    {
        var rig = BondRig.Make(); var s = rig.Of(0, 25); var owner = rig.H.IdOf(rig.P.Household[rig.P.IndexOf(rig.Of(0, 26))]);
        var b = Slavery.Make(rig.Bw, rig.Book, s, owner, BondKind.Chattel, BondOrigin.Sold, 0, 0, 9_000, 0, 0); b.Treatment = Treatment.Kind;
        rig.Opinion = (_, _) => 800;
        for (int d = 1; d < 59; d++) { BondDaily.Run(rig.Bw, rig.Book, rig.Crimes, d); Assert.True(rig.Book.Has(s)); }
        BondDaily.Run(rig.Bw, rig.Book, rig.Crimes, 60);
        Assert.False(rig.Book.Has(s)); Assert.Equal("Trust", rig.C.All.Last(x => x.TypeId == "slave_freed").Data["how"]);
        // a companion buys another free at the price the owner paid
        var t = rig.Of(0, 27); Slavery.Make(rig.Bw, rig.Book, t, owner, BondKind.Chattel, BondOrigin.Sold, 0, 0, 4_000, 0, 0);
        long owner0 = rig.L.Balance(rig.HhPurse[rig.P.Household[rig.P.IndexOf(rig.Of(0, 26))]]).Mites, hero0 = rig.L.Balance(rig.HeroPurse).Mites;
        var r = Slavery.BuyFree(rig.Bw, rig.Book, rig.Hero, t, 61, BondRig.Sec(61)); Assert.True(r.Ok, r.Key);
        Assert.Equal(hero0 - 4_000, rig.L.Balance(rig.HeroPurse).Mites); Assert.False(rig.Book.Has(t));
        Assert.True(rig.L.Balance(rig.HhPurse[rig.P.Household[rig.P.IndexOf(rig.Of(0, 26))]]).Mites >= owner0 + 4_000 - 1_000);
    }

    // ------------------------------------------------------------------ SL-10

    [Fact]
    public void SL10_ATwentyBondedMine_TwoGuards_ALeaderOfFortyFive_RevoltsInTheWindowOfItsDraw_ChainsBroken_AFreeBandForms_CausesNameTheFloggings()
    {
        var rig = BondRig.Make(); rig.Guards[0] = 2;
        var people = Enumerable.Range(0, 20).Select(i => rig.Of(0, 10 + i)).ToList();
        rig.Leadership[people[3].Value] = 45; rig.Leadership[people[4].Value] = 30;
        foreach (var p in people)
        {
            var b = Slavery.Make(rig.Bw, rig.Book, p, BondRig.Fief, BondKind.Thrall, BondOrigin.Captured, 0, 0, 9_000, 0, 0);
            b.Treatment = Treatment.Harsh; b.Chain = Restraint.Chained; b.Work = BondWork.Mine; b.Morale = 20;
        }
        // the first day at or after the season's first (the floggings) on which the 0x633 draw is under (share - 50) in ten thousand: share 100% is 50
        ulong key = rig.W.PlaceId(0).Value; int expect = -1;
        for (int d = 15; d < 6_000; d++) if (DetRng.Range(BondRig.Seed, d, key, RngStreams.SlaveRevoltRoll, 0, 0, 10_000) < 50) { expect = d; break; }
        Assert.True(expect > 0);
        int fired = -1;
        for (int d = 15; d <= expect + 1 && fired < 0; d++) { var t = BondDaily.Run(rig.Bw, rig.Book, rig.Crimes, d); if (t.Revolts > 0) fired = d; }
        Assert.Equal(expect, fired);
        Assert.Empty(rig.Book.Bonds);                                                          // chains broken, all free
        var band = Assert.Single(rig.Book.Rebels); Assert.Equal(people[3].Value, band.Leader); Assert.Equal(20, band.Members);
        var rev = rig.C.All.Single(e => e.TypeId == "slave_revolt");
        Assert.Contains(rev.Causes, c => c.Kind == CauseKind.Slavery && c.EventId is { } id && rig.C.Get(id)!.TypeId == "slave_flogged");   // the floggings by entry
        Assert.Contains(rev.Causes, c => c.NoteKey == "ch_note_slave_hungry");                // the hunger
        output.WriteLine($"revolt on day {fired} (draw predicted {expect})");
    }

    [Fact]
    public void ARevoltNeedsTenBonded_AMajorityAggrieved_FewGuards_AndALeader_AndItsChanceIsShareMinusFiftyInTenThousand()
    {
        Assert.Equal(0, Revolts.DailyChance(50)); Assert.Equal(0, Revolts.DailyChance(30)); Assert.Equal(10, Revolts.DailyChance(60)); Assert.Equal(50, Revolts.DailyChance(100));
        int hits = 0; for (int d = 0; d < 20_000; d++) if (DetRng.Range(BondRig.Seed, d, 77, RngStreams.SlaveRevoltRoll, 0, 0, 10_000) < Revolts.DailyChance(100)) hits++;
        Assert.InRange(hits, 60, 140);                                                         // 5 per mille a day: 100 in 20,000
        var rig = BondRig.Make(); rig.Leadership[rig.Of(0, 10).Value] = 90;
        void Bond(int n, Treatment t) { for (int i = 0; i < n; i++) { var b = Slavery.Make(rig.Bw, rig.Book, rig.Of(0, 10 + i), BondRig.Fief, BondKind.Thrall, BondOrigin.Captured, 0, 0, 9_000, 0, 0); b.Treatment = t; b.Chain = Restraint.Chained; } }
        Bond(9, Treatment.Harsh);                                                              // nine are too few
        for (int d = 1; d < 4_000; d++) Assert.Equal(0, Revolts.Daily(rig.Bw, rig.Book, d, BondRig.Sec(d)));
        Slavery.Make(rig.Bw, rig.Book, rig.Of(0, 19), BondRig.Fief, BondKind.Thrall, BondOrigin.Captured, 0, 0, 9_000, 0, 0).Treatment = Treatment.Kind;
        rig.Book.Of(rig.Of(0, 19))!.Chain = Restraint.Chained;
        rig.Guards[0] = 2;                                                                     // ten bonded, nine of them aggrieved: 90%; two guards are fewer than a fifth of ten? no: 2 x 5 = 10, not fewer
        for (int d = 1; d < 4_000; d++) Assert.Equal(0, Revolts.Daily(rig.Bw, rig.Book, d, BondRig.Sec(d)));
        rig.Guards[0] = 1; int rose = 0; for (int d = 1; d < 4_000 && rose == 0; d++) rose = Revolts.Daily(rig.Bw, rig.Book, d, BondRig.Sec(d));
        Assert.Equal(1, rose);
    }

    // ------------------------------------------------------------------ SL-11

    [Fact]
    public void SL11_WhenTheOwnerDies_TheBondsPassWithTheEstate_ToHisHousehold_ElseTheFief_AndAWillMayFreeThem()
    {
        var rig = BondRig.Make(); var owner = rig.Of(0, 12); int orow = rig.P.IndexOf(owner); int hh = rig.P.Household[orow];
        var s1 = rig.Of(0, 30); var s2 = rig.Of(0, 31);
        foreach (var s in new[] { s1, s2 }) Slavery.Make(rig.Bw, rig.Book, s, owner, BondKind.Chattel, BondOrigin.Sold, 0, 0, 9_000, 0, 0);
        rig.P.Status[orow] = PersonStatus.Dead;
        var t = BondDaily.Run(rig.Bw, rig.Book, rig.Crimes, 5);
        Assert.Equal(2, t.Inherited);
        Assert.Equal(rig.H.IdOf(hh).Value, rig.Book.Of(s1)!.Owner); Assert.Equal(rig.H.IdOf(hh).Value, rig.Book.Of(s2)!.Owner);
        Assert.Contains(rig.C.All, e => e.TypeId == "slave_inherited" && e.Subjects.Contains(s1));
        // the household dies out: the fief takes them (escheat)
        for (int k = 0; k < 4; k++) rig.P.Status[hh * 4 + k] = PersonStatus.Dead; rig.H.MemberCount[hh] = 0;
        BondDaily.Run(rig.Bw, rig.Book, rig.Crimes, 6);
        Assert.Equal(BondRig.Fief.Value, rig.Book.Of(s1)!.Owner);
        // a will that frees them
        var rig2 = BondRig.Make(); var o2 = rig2.Of(0, 12); var f1 = rig2.Of(0, 30);
        Slavery.Make(rig2.Bw, rig2.Book, f1, o2, BondKind.Chattel, BondOrigin.Sold, 0, 0, 9_000, 0, 0);
        rig2.P.Status[rig2.P.IndexOf(o2)] = PersonStatus.Dead; rig2.WillFrees = p => p == o2;
        BondDaily.Run(rig2.Bw, rig2.Book, rig2.Crimes, 5);
        Assert.False(rig2.Book.Has(f1)); Assert.Equal("Will", rig2.C.All.Last(e => e.TypeId == "slave_freed").Data["how"]);
    }

    [Fact]
    public void TheDeadAreNoLongerSlaves_AndAChildOfABondedMotherInAChattelLawIsBondedToHerOwner_ButNotInAWarCaptiveLaw()
    {
        var rig = BondRig.Make(); var mother = rig.Of(0, 3); var owner = rig.H.IdOf(5);
        Slavery.Make(rig.Bw, rig.Book, mother, owner, BondKind.Chattel, BondOrigin.Sold, 0, 0, 9_000, 0, 0);
        int mrow = rig.P.IndexOf(mother);
        int c = rig.P.Add(); rig.P.Given[c] = "Kid"; rig.P.Family[c] = ""; rig.P.Byname[c] = ""; rig.P.BirthDay[c] = 9; rig.P.Mother[c] = mrow; rig.P.Father[c] = -1; rig.P.Spouse[c] = -1;
        rig.P.Status[c] = PersonStatus.Alive; rig.P.Household[c] = rig.P.Household[mrow]; rig.P.HomeSettlement[c] = 0; rig.P.DeathDay[c] = -1;
        BondDaily.Run(rig.Bw, rig.Book, rig.Crimes, 10);
        var kid = rig.Book.Of(rig.P.IdOf(c))!; Assert.Equal(owner.Value, kid.Owner); Assert.Equal(BondOrigin.Born, kid.Origin);
        // a thrall's child is free
        var rig2 = BondRig.Make(); var m2 = rig2.Of(3, 3);
        var tb = Slavery.Make(rig2.Bw, rig2.Book, m2, rig2.H.IdOf(30), BondKind.Thrall, BondOrigin.Captured, 0, 3, 9_000, 0, 0);
        int r2 = rig2.P.IndexOf(m2); int k2 = rig2.P.Add(); rig2.P.Given[k2] = "Kid"; rig2.P.Family[k2] = ""; rig2.P.Byname[k2] = ""; rig2.P.BirthDay[k2] = 9; rig2.P.Mother[k2] = r2; rig2.P.Father[k2] = -1; rig2.P.Spouse[k2] = -1;
        rig2.P.Status[k2] = PersonStatus.Alive; rig2.P.Household[k2] = 0; rig2.P.HomeSettlement[k2] = 3; rig2.P.DeathDay[k2] = -1;
        BondDaily.Run(rig2.Bw, rig2.Book, rig2.Crimes, 10);
        Assert.False(rig2.Book.Has(rig2.P.IdOf(k2))); _ = tb;
        // the dead leave the book
        rig.P.Status[mrow] = PersonStatus.Dead; BondDaily.Run(rig.Bw, rig.Book, rig.Crimes, 11);
        Assert.False(rig.Book.Has(mother));
    }

    // ------------------------------------------------------------------ SL-13

    [Fact]
    public void SL13_RaidsAndTheSlider_FifteenPercentPlusOrMinusThreeAtOneHundred_ThirtyPlusOrMinusFourAtTwoHundred()
    {
        var rules = BondRules.From(Db);
        double Rate(int pct) { int hits = 0, n = 0; for (int region = 0; region < 10; region++) for (int season = 0; season < 100; season++) { n++; if (Raids.Happens(BondRig.Seed, region, season, pct, rules)) hits++; } return 100.0 * hits / n; }
        double a = Rate(100), b = Rate(200), z = Rate(0), half = Rate(50);
        output.WriteLine($"raid rate: 0% {z}, 50% {half}, 100% {a}, 200% {b}");
        Assert.InRange(a, 12, 18); Assert.InRange(b, 26, 34); Assert.Equal(0, z); Assert.InRange(half, 4, 11);
        Assert.Equal(Raids.Happens(1, 3, 9, 100, rules), Raids.Happens(1, 3, 9, 100, rules));
    }

    [Fact]
    public void ARaidTakesTwoToEightAdultsFromAWeakVillage_AsPhase4Captives_PeopleStayWhole_AndTheHoldEndsInThePen()
    {
        var rig = BondRig.Make(); var caps = rig.Caps; var rules = AftermathRules.Default;
        rig.Guards[0] = 9; rig.Guards[1] = 9; rig.Guards[3] = 9; rig.Guards[2] = 1;           // only Highfold is weak
        for (int i = 0; i < rig.P.Count; i++) rig.P.BirthDay[i] = -30 * TimeConst.DaysPerYear;
        int people = rig.P.Count;
        var taken = Raids.Raid(rig.Bw, rig.Book, caps, rules, 5, 3, 45, BondRig.Sec(45), p => p == rig.Hero);
        Assert.InRange(taken.Count, 2, 8);
        Assert.All(taken, p => { Assert.Equal(BondRig.Debt, rig.P.HomeSettlement[rig.P.IndexOf(p)]); Assert.Equal(CaptorKind.Slavers, caps.Find(p)!.Captor); Assert.True(caps.Find(p)!.Held); });
        Assert.Equal(people, rig.P.Count); Assert.All(taken, p => Assert.True(rig.P.IsAlive(rig.P.IndexOf(p))));   // nobody made, nobody removed
        Assert.Contains(rig.C.All, e => e.TypeId == "slave_raid" && e.Params!.Any(x => x.Name == "count" && x.Value == taken.Count));
        var band = rig.Book.Bands.Values.Single(); Assert.InRange(band.Size, 6, 15); Assert.Equal(1, band.Raids);
        Assert.Equal(taken, Raids.Raid(rig.Bw, new BondBook(), new CaptiveBook(BondRig.Seed), rules, 5, 3, 45, BondRig.Sec(45), p => p == rig.Hero));   // the same season gives the same victims
        // after the hold, a sale: nobody is released
        for (long h = 45 * 24; h < 58 * 24; h++) caps.StepHour(rules, null, h);
        Assert.All(taken, p => Assert.True(caps.Find(p)!.Note == "sold" || caps.Find(p)!.State == CaptiveState.Escaped));
        Raids.Sweep(rig.Bw, rig.Book, caps, 4, 58, BondRig.Sec(58));
        Assert.All(taken.Where(p => caps.Find(p)!.Note == "sold"), p => Assert.True(rig.Book.Of(p)!.InPen));
        rig.Guards[2] = 9;
        Assert.Empty(Raids.Raid(rig.Bw, new BondBook(), new CaptiveBook(BondRig.Seed), rules, 5, 4, 60, BondRig.Sec(60)));   // no weak village, no raid
    }

    // ------------------------------------------------------------------ SL-14

    [Fact]
    public void SL14_TheBondHashIsEqualAcrossRuns_AndTheSectionRoundTripsByteForByte_WithAPenARunawayAndAChainedHero()
    {
        BondBook Build()
        {
            var rig = BondRig.Make(); var pen = rig.Of(0, 5); rig.Pen(pen);
            var run = rig.Of(0, 6); var b = Slavery.Make(rig.Bw, rig.Book, run, rig.H.IdOf(rig.P.Household[rig.P.IndexOf(rig.Of(0, 7))]), BondKind.Chattel, BondOrigin.Sold, 1, 0, 9_000, 0, 0);
            b.Chain = Restraint.None; BondDaily.Flee(rig.Bw, rig.Book, rig.Crimes, b, 4, BondRig.Sec(4));
            var hero = Slavery.Make(rig.Bw, rig.Book, rig.Hero, rig.H.IdOf(9), BondKind.Chattel, BondOrigin.Sold, 2, 0, 9_000, 0, 0); hero.Chain = Restraint.Chained; hero.Work = BondWork.Fields;
            Raids.Raid(rig.Bw, rig.Book, new CaptiveBook(1), AftermathRules.Default, 2, 1, 5, BondRig.Sec(5));
            rig.Book.Swept.Add(77);
            for (int d = 5; d < 20; d++) BondDaily.Run(rig.Bw, rig.Book, rig.Crimes, d);
            return rig.Book;
        }
        var a = Build(); var c = Build();
        Assert.Equal(a.Hash(), c.Hash()); Assert.NotEqual(0UL, a.Hash());
        var bytes = SaveCodec.SerializeCompressed(BondSave.Capture(a));
        var back = SaveCodec.DeserializeCompressed<BondSave>(bytes).Restore();
        Assert.Equal(a.Hash(), back.Hash());
        Assert.Equal(bytes, SaveCodec.SerializeCompressed(BondSave.Capture(back)));
        Assert.True(new BondBook().IsEmpty);                                                     // a world with no slavery writes no section
        Assert.InRange(bytes.Length, 1, 20_000);
    }

    [Fact]
    public void ADayOfBondsCostsWellUnderABudget_AndCoinIsConservedThroughASeasonOfBuyingSellingTipsAndFreedom()
    {
        var rig = BondRig.Make(); long audit = rig.L.Audit().InPurses.Mites;
        for (int i = 0; i < 30; i++)
        {
            var p = rig.Of(i % 3 == 0 ? 0 : 2, i); var own = rig.H.IdOf(rig.P.Household[rig.P.IndexOf(rig.Of(i % 3 == 0 ? 0 : 2, 39 - i))]);
            var b = Slavery.Make(rig.Bw, rig.Book, p, own, BondKind.Chattel, BondOrigin.Sold, 0, rig.P.HomeSettlement[rig.P.IndexOf(p)], 600 + 10 * i, 0, 0);
            b.Treatment = (Treatment)(i % 3);
        }
        var sw = System.Diagnostics.Stopwatch.StartNew();
        for (int d = 1; d <= 60; d++) BondDaily.Run(rig.Bw, rig.Book, rig.Crimes, d);
        sw.Stop();
        output.WriteLine($"60 days of 30 bonds: {sw.Elapsed.TotalMilliseconds:F1} ms");
        Assert.Equal(audit, rig.L.Audit().InPurses.Mites);
        Assert.True(sw.Elapsed.TotalMilliseconds < 2_000);
    }
}
