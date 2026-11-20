using Ravensguard.Content;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Family;
using Ravensguard.Sim.People;
using Ravensguard.Sim.Politics;
using Ravensguard.Sim.Rule;
using Ravensguard.Sim.Save;
using Ravensguard.Sim.Succession;
using Ravensguard.Sim.Tests.Politics;
using Ravensguard.Sim.Trade;
using Ravensguard.Sim.WorldGen.Region;
using Xunit;
using Xunit.Abstractions;

namespace Ravensguard.Sim.Tests.Family;

/// <summary>
/// Family politics (P6-E3, docs/specs/phase6/family-and-lifespan.md): FL-5 a lord's daughter married to a neighbouring lord's son is an alliance (opinion +30 both ways, a marriage treaty), FL-6 the overlord's marriage right for a
/// fief's heir (his consent and fee; refused when he will not sell; defied at a fine), FL-12 kin who turn (leave, refuse a match, contest a will), on a new game of seed 1234 with the rule and politics books.
/// </summary>
public class FamilyPoliticsTests(ITestOutputHelper output)
{
    private sealed class Rig : IDisposable
    {
        public PoliticsRig P = new();
        public WorldState St => P.State;
        public PersonTable Pt => St.Population.People;
        public HouseholdTable Hht => St.Population.Households;
        public EntityId HeroId => St.Party!.Hero;
        public int Hero => St.Hero!.PersonIndex;
        public int HeroHh => Pt.Household[Hero];
        public int Day => P.Day;
        public FamilyWorld Fw => FamilyWorld.From(St, P.R.Content);
        public RuleBook Rule => St.Rule!;
        public FiefRow FiefA = null!, FiefB = null!;
        public int HolderB;

        public Rig()
        {
            // the hero holds a fief and a neighbouring lord holds another, of another realm
            FiefA = P.Fief(0);
            FiefB = Rule.Fiefs.Values.First(f => f.Holder >= 0 && f.Realm >= 0 && f.Realm != FiefA.Realm && !f.Forfeited && f.Fief != FiefA.Fief);
            HolderB = Pt.IndexOf(new EntityId((ulong)FiefB.Holder));
            FiefA.Holder = (long)HeroId.Value; FiefA.Receipts[0] = 4_000; FiefB.Receipts[0] = 4_000;
            var led = St.Economy!.Ledger; var hp = led.TryFind(PurseKind.Person, HeroId, out var ho) ? ho : led.Open(PurseKind.Person, HeroId); led.Mint(hp, new Coin(50_000), SourceReason.Seed);
            Hht.Class[HeroHh] = SocialClass.Gentry; Hht.Head[HeroHh] = Hero; Hht.Beds[HeroHh] = 20; Hht.Dwelling[HeroHh] = Hht.Dwelling[HeroHh] == DwellingType.None ? DwellingType.Manor : Hht.Dwelling[HeroHh];
            int hb = Pt.Household[HolderB]; Hht.Class[hb] = SocialClass.Gentry; Hht.Head[hb] = HolderB; Hht.Beds[hb] = 20; if (Hht.Dwelling[hb] == DwellingType.None) Hht.Dwelling[hb] = DwellingType.Manor;
            Fund(HeroHh, 60_000); Fund(hb, 60_000);
        }

        public void Fund(int hh, long mites)
        {
            var l = St.Economy!.Ledger; var p = St.Economy.HouseholdPurse[hh]; long have = l.Balance(p).Mites;
            if (have < mites) l.Mint(p, new Coin(mites - have), SourceReason.Seed);
        }

        public int AddPerson(int household, Sex sex, int age, int father, string given)
        {
            St.Ids.EnsureNext(IdKind.Person, Pt.BaseCounter + (ulong)Pt.Count); St.Ids.Allocate(IdKind.Person);
            int c = Pt.Add(); St.Economy!.AddPerson(c);
            Pt.Given[c] = given; Pt.Family[c] = Pt.Family[Hht.Head[household]]; Pt.Sex[c] = sex; Pt.BirthDay[c] = PersonTable.BirthDayFor(Day, age, 0, Pt.AgingPace);
            Pt.Father[c] = father; Pt.Mother[c] = -1; Pt.Household[c] = household; Pt.HomeSettlement[c] = Hht.Settlement[household]; Pt.Class[c] = Hht.Class[household];
            Pt.Status[c] = PersonStatus.Alive; Pt.Culture[c] = Pt.Culture[Hht.Head[household]];
            Hht.AddMemberLater(household, c); St.Population.ResetKin();
            return c;
        }

        /// <summary>The two lords' houses with a grown heir each and a younger unmarried child each: the hero's daughter (23) and the neighbour's younger son (27).</summary>
        public (int Daughter, int Son) Children()
        {
            int hb = Pt.Household[HolderB];
            AddPerson(HeroHh, Sex.Male, 34, Hero, "Edric");                 // the hero's heir: eldest son
            int daughter = AddPerson(HeroHh, Sex.Female, 23, Hero, "Alys");
            AddPerson(hb, Sex.Male, 36, HolderB, "Osric");                  // the neighbour's heir
            int son = AddPerson(hb, Sex.Male, 27, HolderB, "Wulf");
            return (daughter, son);
        }

        public ulong Key(int row) => Opinions.K(row == Hero ? HeroId.Value : Pt.IdOf(row).Value);
        public void Like(int answerer, int of, int pm) => Rule.AddOpinion(Key(answerer), Key(of), pm);

        public long Coin(int hh) => St.Economy!.Ledger.Balance(St.Economy.HouseholdPurse[hh]).Mites;
        public void Dispose() => P.Dispose();
    }

    // ---------------------------------------------------------------- FL-5

    [Fact]
    public void FL5_ALordMarriesHisDaughterToANeighbouringLordsSon_OpinionPlus30BothWays_AMarriageTreatyInTheBook_AndTheDowryMoves()
    {
        using var r = new Rig(); var pt = r.Pt;
        var (daughter, son) = r.Children();
        // the neighbour thinks well enough of the hero for the alliance: 30 for the alliance and 20 for his opinion reach the 50 the head asks
        r.Like(r.HolderB, r.Hero, 20);
        var fw = r.Fw; int day = r.Day;
        Assert.True(Matches.IsAlliance(fw, r.HeroHh, pt.Household[r.HolderB]));
        var cand = Matches.Candidates(fw, daughter, day).First(c => c.Row == son);
        Assert.Equal(30, cand.Terms.Alliance); Assert.True(cand.Terms.Total >= 50, "utility " + cand.Terms.Total);
        long heroHh0 = r.Coin(r.HeroHh), theirs0 = r.Coin(pt.Household[r.HolderB]);
        long share = Matches.ClassShare(fw, r.HeroHh); Assert.Equal(heroHh0 * 20 / 100, share);
        var res = Matches.Arrange(fw, pt.IdOf(daughter).Value, pt.IdOf(son).Value, -1, false, day);
        Assert.True(res.Ok, res.Key);
        var m = r.St.Family!.Matches.Values.Single();
        Assert.Equal(MatchState.Betrothed, m.State); Assert.True(m.Alliance); Assert.Equal(share, m.DowryMites); Assert.True(m.WeddingDay >= day + 5 && m.WeddingDay % fw.S.HolyDayEvery == 0);
        Assert.Contains(r.St.Chronicle.All, e => e.TypeId == "match_arranged");
        long audit0 = r.St.Economy!.Ledger.Audit().Circulating.Mites;
        for (int d = day; d <= m.WeddingDay; d++) Matches.Daily(r.Fw, d);
        Assert.Equal(MatchState.Wed, m.State);
        Assert.Equal(son, pt.Spouse[daughter]); Assert.Equal(daughter, pt.Spouse[son]);
        Assert.Equal(pt.Household[son], pt.Household[daughter]);                                        // she joins her husband's house
        long fee = Courting.FeePurse(fw, r.Hht.Settlement[pt.Household[son]]) != null ? fw.S.WeddingFeeMites : 0;       // the priest's fee, from the house that takes the couple in
        Assert.Equal(heroHh0 - share, r.Coin(r.HeroHh));
        Assert.Equal(theirs0 + share - fee, r.Coin(pt.Household[son]));
        Assert.Equal(audit0, r.St.Economy.Ledger.Audit().Circulating.Mites);                              // coin conserved
        // the alliance: opinion marriage +30 each way, a treaty between the two realms
        var rb = r.Rule; var hero = r.HeroId.Value; ulong b = pt.IdOf(r.HolderB).Value;
        Assert.True(Opinions.Value(rb, b, hero) >= 50 && Opinions.Standing(r.St.Politics!, b, hero, "marriage") == 30, "b of hero " + Opinions.Value(rb, b, hero));
        Assert.Equal(30, Opinions.Standing(r.St.Politics!, hero, b, "marriage")); Assert.True(Opinions.Value(rb, hero, b) >= 30);
        Assert.Contains(r.St.Politics!.Pacts, p => p.Kind == PactKind.MarriagePact && !p.Ended && p.Between(r.FiefA.Realm, r.FiefB.Realm));
        Assert.Contains(r.St.Chronicle.All, e => e.TypeId == "pact_signed");
        var wed = r.St.Chronicle.All.Single(e => e.TypeId == "match_wed"); Assert.Equal("ch_note_match_alliance", wed.Causes[0].NoteKey);
    }

    [Fact]
    public void FL5_AnArrangedMatchThatTheHeadsDoNotLike_IsRefused_AndAnAgeGapOverTenYearsCostsTwentyAYear()
    {
        using var r = new Rig(); var pt = r.Pt;
        var (daughter, son) = r.Children();
        var fw = r.Fw; int day = r.Day;
        var res = Matches.Arrange(fw, pt.IdOf(daughter).Value, pt.IdOf(son).Value, -1, false, day);       // the alliance alone is 30: under the 50 the head asks
        Assert.False(res.Ok); Assert.Equal("fm_match_no", res.Key);
        Assert.Empty((IEnumerable<Match>?)r.St.Family?.Matches.Values ?? []);
        Assert.Equal(0, Matches.Terms(fw, r.HeroHh, pt.Household[r.HolderB], son, daughter, 0, day).AgeGap);
        int old = r.AddPerson(r.HeroHh, Sex.Female, 40, r.Hero, "Maud");                                   // 13 years older than the groom: 3 years over ten
        Assert.Equal(60, Matches.Terms(fw, r.HeroHh, pt.Household[r.HolderB], son, old, 0, day).AgeGap);
        // a person who is not of the household, a kin of the same house, the same sex: refused with a reason
        Assert.Equal("fm_not_your_member", Matches.Arrange(fw, pt.IdOf(r.HolderB).Value, pt.IdOf(son).Value, -1, false, day).Key);
        Assert.Equal("fm_not_a_fit", Matches.Arrange(fw, pt.IdOf(daughter).Value, pt.IdOf(r.AddPerson(pt.Household[r.HolderB], Sex.Female, 25, r.HolderB, "Nan")).Value, -1, false, day).Key);
    }

    [Fact]
    public void FL5_TheDowryIsNegotiatedWithinHalfTheClassShareEitherWay()
    {
        using var r = new Rig(); var pt = r.Pt;
        var (daughter, son) = r.Children(); r.Like(r.HolderB, r.Hero, 40);
        var fw = r.Fw; long share = Matches.ClassShare(fw, r.HeroHh);
        Assert.Equal("fm_dowry_range", Matches.Arrange(fw, pt.IdOf(daughter).Value, pt.IdOf(son).Value, share * 16 / 10, false, r.Day).Key);
        Assert.Equal("fm_dowry_range", Matches.Arrange(fw, pt.IdOf(daughter).Value, pt.IdOf(son).Value, share * 4 / 10, false, r.Day).Key);
        Assert.True(Matches.Arrange(fw, pt.IdOf(daughter).Value, pt.IdOf(son).Value, share * 12 / 10, false, r.Day).Ok);
        Assert.Equal(share * 12 / 10, r.St.Family!.Matches.Values.Single().DowryMites);
    }

    [Fact]
    public void FL5_LordsArrangeMatchesAmongThemselvesEachSeason_ByTheSameFunction_Deterministically()
    {
        using var r = new Rig(); var pt = r.Pt;
        // two lords with grown unmarried children, neither the hero
        var fiefs = r.Rule.Fiefs.Values.Where(f => f.Holder >= 0 && !f.Forfeited && (ulong)f.Holder != r.HeroId.Value).Take(2).ToList();
        int h1 = pt.IndexOf(new EntityId((ulong)fiefs[0].Holder)), h2 = pt.IndexOf(new EntityId((ulong)fiefs[1].Holder));
        foreach (int h in new[] { h1, h2 }) { int hh = pt.Household[h]; r.Hht.Class[hh] = SocialClass.Gentry; r.Hht.Head[hh] = h; r.Hht.Beds[hh] = 20; r.Fund(hh, 50_000); if (r.Hht.Dwelling[hh] == DwellingType.None) r.Hht.Dwelling[hh] = DwellingType.Manor; }
        int hugh = r.AddPerson(pt.Household[h1], Sex.Male, 28, h1, "Hugh"), edith = r.AddPerson(pt.Household[h2], Sex.Female, 24, h2, "Edith");
        r.Like(h2, h1, 30); r.Like(h1, h2, 30);
        int season = 0; ulong keyA = r.Hht.IdOf(pt.Household[h1]).Value, keyB = r.Hht.IdOf(pt.Household[h2]).Value;
        // the chance is the 0x645 draw: find a season where either lord's draw passes, then the match is made, and the same call again makes nothing new
        int made = 0, day = 0;
        for (; season < 60 && made == 0; season++)
        {
            day = (r.Day / TimeConst.DaysPerSeason + season) * TimeConst.DaysPerSeason;
            if (DetRng.Range(r.St.Seed, day / TimeConst.DaysPerSeason, keyA, RngStreams.LordMatches, 0, 0, 100) >= 30 && DetRng.Range(r.St.Seed, day / TimeConst.DaysPerSeason, keyB, RngStreams.LordMatches, 0, 0, 100) >= 30) continue;
            made = Matches.LordsSeason(r.Fw, day);
        }
        Assert.True(made >= 1, "no season drew a match");
        var m = r.St.Family!.Matches.Values.First(x => !x.ByHero); Assert.True(m.Alliance); Assert.True(m.Score >= 50); Assert.True(m.State is MatchState.Betrothed or MatchState.AwaitingFee);
        int before = r.St.Family.Matches.Count;
        Matches.LordsSeason(r.Fw, day);                                                                    // the same day again: the promised are not matched twice
        var people = r.St.Family.Matches.Values.SelectMany(x => new[] { x.Groom, x.Bride }).ToList();
        Assert.Equal(people.Count, people.Distinct().Count());
        Assert.True(r.St.Family.Matches.Count >= before);
    }

    // ---------------------------------------------------------------- FL-6

    [Fact]
    public void FL6_AFiefHeiressMatch_NeedsTheOverlordsConsentAndFee_RefusedWhenHeWillNotSell_ThePaidFeeAllowsIt_DefyingItCostsAFineAndHisOpinion()
    {
        using var r = new Rig(); var pt = r.Pt;
        // the hero's only child is a daughter: she is the heiress of his fief, and the match needs his overlord
        int hb = pt.Household[r.HolderB];
        int daughter = r.AddPerson(r.HeroHh, Sex.Female, 23, r.Hero, "Alys");
        r.AddPerson(hb, Sex.Male, 36, r.HolderB, "Osric"); int son = r.AddPerson(hb, Sex.Male, 27, r.HolderB, "Wulf");
        r.Like(r.HolderB, r.Hero, 40);
        var fw = r.Fw; int day = r.Day;
        Assert.Equal(r.FiefA.Fief, Matches.HeirOfFief(fw, daughter));
        Assert.Equal(r.FiefA.Fief, Matches.ConsentFief(fw, daughter, son));
        var c = r.P.C().R; var overlord = c.OverlordOf(r.FiefA); Assert.False(overlord.IsNone);
        ulong okey = c.KeyOf(overlord).Value;
        // (a) the overlord thinks ill of the hero: he will not sell his consent, so there is no match
        r.Rule.AddOpinion(okey, Opinions.K(r.HeroId.Value), -60);
        var no = Matches.Arrange(fw, pt.IdOf(daughter).Value, pt.IdOf(son).Value, -1, false, day);
        Assert.False(no.Ok); Assert.Equal("fm_overlord_refuses", no.Key); Assert.Empty((IEnumerable<Match>?)r.St.Family?.Matches.Values ?? []);
        // (b) he will sell: the fee is the due of the hero's house, paid to him; the wedding is allowed
        r.Rule.AddOpinion(okey, Opinions.K(r.HeroId.Value), 60);
        long fee0 = r.Coin(r.HeroHh); long pay0 = c.Balance(overlord);
        var yes = Matches.Arrange(fw, pt.IdOf(daughter).Value, pt.IdOf(son).Value, -1, false, day);
        Assert.True(yes.Ok, yes.Key);
        var m = r.St.Family!.Matches.Values.Single(); Assert.True(m.Consent); Assert.Equal(r.FiefA.Fief, m.Fief);
        var due = r.Rule.Dues.Single(d => d.Serial == m.DueSerial); Assert.Equal(DueKind.MarriageFee, due.Kind);
        if (due.State == DueState.Paid)
        {
            Assert.Equal(MatchState.Betrothed, m.State); Assert.True(c.Balance(overlord) - pay0 >= due.AmountMites - 1);
        }
        Assert.Contains(r.St.Chronicle.All, e => e.TypeId == "marriage_consent_sold");
        _ = fee0;
    }

    [Fact]
    public void FL6_AFeeThatCannotBePaidKeepsTheWeddingWaiting_UntilTheHouseHasTheCoin_AndAWeddingWithoutConsentIsFinedAndLosesTheOverlordsOpinion()
    {
        using var r = new Rig(); var pt = r.Pt;
        int hb = pt.Household[r.HolderB];
        int daughter = r.AddPerson(r.HeroHh, Sex.Female, 23, r.Hero, "Alys");
        r.AddPerson(hb, Sex.Male, 36, r.HolderB, "Osric"); int son = r.AddPerson(hb, Sex.Male, 27, r.HolderB, "Wulf");
        r.Like(r.HolderB, r.Hero, 40);
        var c = r.P.C().R; var overlord = c.OverlordOf(r.FiefA); ulong okey = c.KeyOf(overlord).Value;
        // the hero's house has nothing: the due stays owing and the match waits; the wedding does not happen
        var led = r.St.Economy!.Ledger; var hp = r.St.Economy.HouseholdPurse[r.HeroHh]; long all = led.Balance(hp).Mites;
        var chest = led.Open(PurseKind.Treasury, EntityId.Make(IdKind.Faction, 777_001)); led.Transfer(hp, chest, new Coin(all), TransferReason.Gift);
        var heroOwn = led.TryFind(PurseKind.Person, r.HeroId, out var hop) ? hop : led.Open(PurseKind.Person, r.HeroId); long own = led.Balance(heroOwn).Mites;
        if (own > 0) led.Transfer(heroOwn, chest, new Coin(own), TransferReason.Gift);          // the lord pays his dues from his own purse first, then his house's
        r.Fund(r.HeroHh, 5);
        var fw = r.Fw; int day = r.Day;
        var res = Matches.Arrange(fw, pt.IdOf(daughter).Value, pt.IdOf(son).Value, 0, false, day);
        Assert.True(res.Ok, res.Key);
        var m = r.St.Family!.Matches.Values.Single();
        Assert.Equal(MatchState.AwaitingFee, m.State); Assert.Equal(-1, m.WeddingDay);
        for (int d = day; d < day + 12; d++) { Matches.Daily(r.Fw, d); Assert.True(pt.Spouse[daughter] < 0); }
        // coin arrives: the due is settled and the match is betrothed
        led.Transfer(chest, hp, new Coin(all), TransferReason.Gift);
        Matches.Daily(r.Fw, day + 12);
        Assert.Equal(MatchState.Betrothed, m.State); Assert.True(m.WeddingDay >= day + 12 + 5);
        Assert.Equal(DueState.Paid, r.Rule.Dues.Single(d => d.Serial == m.DueSerial).State);
        // a second match, defying the overlord: the fine is a due and his opinion falls by the rule's 30
        int daughter2 = r.AddPerson(r.HeroHh, Sex.Female, 23, r.Hero, "Beth"); int son2 = r.AddPerson(hb, Sex.Male, 28, r.HolderB, "Wat");
        r.Rule.Dues.Clear();
        int op0 = Opinions.Value(r.Rule, okey, r.HeroId.Value);
        // Beth is not the heir (Alys is eldest), so make her the heiress: the elder daughter marries off first
        pt.Status[daughter] = PersonStatus.Departed; r.St.Population.ResetKin();
        var def = Matches.Arrange(r.Fw, pt.IdOf(daughter2).Value, pt.IdOf(son2).Value, -1, true, day);
        Assert.True(def.Ok, def.Key);
        var m2 = r.St.Family!.Matches.Values.Last(); Assert.True(m2.Defied); Assert.False(m2.Consent);
        Assert.Contains(r.St.Chronicle.All, e => e.TypeId == "marriage_fined");
        Assert.True(Opinions.Value(r.Rule, okey, r.HeroId.Value) <= op0 - 30 + 1 || r.Rule.Dues.Any(d => d.Kind == DueKind.MarriageFee));
        _ = output;
    }

    // ---------------------------------------------------------------- FL-12

    [Fact]
    public void FL12_ASonWithTrustMinus400AndTwoGrievances_IsBelowTheTurnLine_LeavesWithThirtyPercentAYear_AndRefusesAnyMatch()
    {
        using var r = new Rig(); var pt = r.Pt;
        var s = r.Fw.S;
        int son = r.AddPerson(r.HeroHh, Sex.Male, 28, r.Hero, "Wystan");
        var id = pt.IdOf(son); (r.St.Succession ??= new SuccessionBook()).Kin.Add(new HeroKinLink(id, KinRelation.Child));
        r.St.Party!.Know(id).TrustPm = -400;
        r.St.Family = new FamilyBook(); r.St.Family.Grievances[id.Value] = KinGrievance.Neglected | KinGrievance.MarriedOff;
        Assert.Equal(-700, Matches.KinLoyalty(r.Fw, id));
        // the yearly draw: 30 percent of 10,000 years, stream 0x642 keyed on the person and the year
        int left = 0; for (int y = 0; y < 10_000; y++) if (KinTurning.Leaves(s, r.St.Seed, id.Value, y)) left++;
        Assert.InRange(left, 2_850, 3_150); Assert.Equal(KinTurning.Leaves(s, r.St.Seed, id.Value, 7), KinTurning.Leaves(s, r.St.Seed, id.Value, 7));
        // a match is refused outright, whatever the houses think
        var cand = pt.Household[r.HolderB];
        int bride = r.AddPerson(cand, Sex.Female, 24, r.HolderB, "Cecily"); r.Like(r.HolderB, r.Hero, 60);
        Assert.Equal("fm_kin_refuses", Matches.Arrange(r.Fw, id.Value, pt.IdOf(bride).Value, -1, false, r.Day).Key);
        // on his birthday of a year whose draw says leave, he goes to another roof of the place, and the entry names the grievances as causes
        int year = Enumerable.Range(r.Day / TimeConst.DaysPerYear, 200).First(y => KinTurning.Leaves(s, r.St.Seed, id.Value, y));
        // find the actual birthday day inside the year
        int bday = Enumerable.Range(year * TimeConst.DaysPerYear, TimeConst.DaysPerYear + 1).First(d => pt.AgeYears(son, d) > pt.AgeYears(son, d - 1));
        int homeHh = pt.Household[son];
        Assert.Equal(year, bday / TimeConst.DaysPerYear);
        var n = KinTurning.Daily(r.Fw, bday, r.Hero);
        Assert.Equal(1, n);
        Assert.NotEqual(homeHh, pt.Household[son]);
        var e = r.St.Chronicle.All.Single(x => x.TypeId == "kin_left");
        Assert.Contains(e.Causes, c => c.NoteKey == "ch_note_kin_neglected"); Assert.Contains(e.Causes, c => c.NoteKey == "ch_note_kin_married_off");
    }

    [Fact]
    public void FL12_ASonThePassedOverByTheWillContestsIt_TheCourtDecidesOnStream0x561_AndTheShareIsPaidThroughTheLedger()
    {
        using var r = new Rig(); var pt = r.Pt;
        var s = r.Fw.S;
        int heir = r.AddPerson(r.HeroHh, Sex.Male, 22, r.Hero, "Alan"); int passed = r.AddPerson(r.HeroHh, Sex.Male, 30, r.Hero, "Bryce");
        var hid = pt.IdOf(heir); var pid = pt.IdOf(passed);
        r.St.Party!.Know(pid).TrustPm = -400;
        var will = new Will { Testator = r.HeroId, Day = r.Day }; will.Bequests.Add(new Bequest(hid, BequestWhat.All));
        // the pure rules: the law beats the will at 60 percent, standing moves it a tenth of the difference, held to 5..95
        Assert.Equal(600, Disputes.LawPm(s, 0, 0)); Assert.Equal(700, Disputes.LawPm(s, 1000, 0)); Assert.Equal(500, Disputes.LawPm(s, 0, 1000)); Assert.Equal(950, Disputes.LawPm(s, 1000, -9000)); Assert.Equal(50, Disputes.LawPm(s, -9000, 1000));
        int wins = 0; for (int d = 0; d < 10_000; d++) if (Disputes.Overturns(1234, d, pid.Value, 600)) wins++;
        Assert.InRange(wins, 5_850, 6_150);
        // the dead hero's coin has gone to the heir: the contest is brought, decided once, and when the law wins the child is paid his equal share
        var led = r.St.Economy!.Ledger; var heroPurse = led.TryFind(PurseKind.Person, r.HeroId, out var hp) ? hp : led.Open(PurseKind.Person, r.HeroId);
        led.Mint(heroPurse, new Coin(30_000), SourceReason.Seed);
        long total0 = led.Audit().Circulating.Mites, bal0 = led.Balance(heroPurse).Mites, kin0 = led.Balance(r.St.Economy.HouseholdPurse[pt.Household[passed]]).Mites;
        int day = r.Day; while (!Disputes.Overturns(r.St.Seed, day, pid.Value, Disputes.LawPm(s, 0, 0))) day++;          // a day the court sides with the law
        int brought = Disputes.Contest(r.St, r.P.R.Content, r.St.Party, heroPurse, r.Hero, heir, will, day, day * 86_400L);
        Assert.Equal(1, brought);
        var e = r.St.Chronicle.All.Single(x => x.TypeId == "will_contested");
        Assert.Contains(e.Causes, c => c.NoteKey == "ch_note_kin_passed_over"); Assert.Contains(e.Causes, c => c.NoteKey == "ch_note_court_law");
        long paid = bal0 - led.Balance(heroPurse).Mites; Assert.True(paid > 0);
        Assert.Equal(paid, led.Balance(r.St.Economy.HouseholdPurse[pt.Household[passed]]).Mites - kin0);
        Assert.Equal(total0, led.Audit().Circulating.Mites);
        Assert.Contains(pid.Value, r.St.Family!.Contested);
        Assert.Equal(0, Disputes.Contest(r.St, r.P.R.Content, r.St.Party, heroPurse, r.Hero, heir, will, day + 1, 0));   // one dispute each
        // a son the will names, the heir, and a child who still trusts him: no contest
        r.St.Family.Contested.Clear(); r.St.Party.Know(pid).TrustPm = 400;
        Assert.Equal(0, Disputes.Contest(r.St, r.P.R.Content, r.St.Party, heroPurse, r.Hero, heir, will, day, 0));
        Assert.Equal(0, Disputes.Contest(r.St, r.P.R.Content, r.St.Party, heroPurse, r.Hero, heir, null, day, 0));        // no will: the law ran
    }

    [Fact]
    public void FamilyBookWithMatchesAndAContest_SavesAndComesBackTheSame_AndABookWithoutThemKeepsItsHash()
    {
        using var r = new Rig(); var pt = r.Pt;
        var (daughter, son) = r.Children(); r.Like(r.HolderB, r.Hero, 30);
        Assert.True(Matches.Arrange(r.Fw, pt.IdOf(daughter).Value, pt.IdOf(son).Value, -1, false, r.Day).Ok);
        r.St.Family!.Contested.Add(99); r.St.Family.Grievances[77] = KinGrievance.PassedOver;
        ulong hash = r.St.Family.Hash();
        var file = WorldSave.Capture(r.St, "b", "0.1");
        var st2 = WorldSave.Restore(SaveCodec.Decode(SaveCodec.Encode(file)), r.P.R.Content, "b", null, r.P.R.W.Continent, r.P.R.W.History);
        Assert.Equal(hash, st2.Family!.Hash()); Assert.Single(st2.Family.Matches); Assert.Contains(99UL, st2.Family.Contested);
        Assert.Equal(file.Sections[FamilySave.SectionName].ToArray(), WorldSave.Capture(st2, "b", "0.1").Sections[FamilySave.SectionName].ToArray());
        var plain = new FamilyBook { LastDay = 3 };
        Assert.Equal(plain.Hash(), new FamilyBook { LastDay = 3 }.Hash());
        var withNone = new FamilyBook { LastDay = 3 }; withNone.Matches.Clear();
        Assert.Equal(plain.Hash(), withNone.Hash());
    }
}
