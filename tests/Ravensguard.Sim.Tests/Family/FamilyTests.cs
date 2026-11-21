using Ravensguard.Content;
using Ravensguard.Sim.Characters;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Family;
using Ravensguard.Sim.People;
using Ravensguard.Sim.People.Vital;
using Ravensguard.Sim.Save;
using Ravensguard.Sim.Succession;
using Ravensguard.Sim.Tests.Seamless;
using Ravensguard.Sim.Trade;
using Ravensguard.Sim.WorldGen.Region;
using Ravensguard.Sim.Worlds;
using Xunit;
using Xunit.Abstractions;

namespace Ravensguard.Sim.Tests.Family;

/// <summary>
/// The family core (docs/specs/phase6/family-and-lifespan.md, P6-B3): FL-1 (the courting score), FL-2 (consent and the wedding on a holy day), FL-3 (no dwelling: the betrothal waits), FL-4 (the dowry,
/// coin conserved), FL-7 (children over ten years), FL-8 (a child taught arms and one untaught), FL-9 (the ageing table), FL-10 (old age kills, the heir takes over), FL-11 (kidnapping) and FL-15 (the save).
/// Each runs the family's day (<see cref="FamilySystem.Daily"/>) over a real generated town, the way the link runs it on the tick worker.
/// </summary>
public class FamilyTests(ITestOutputHelper output)
{
    private sealed class Rig
    {
        public SeamWorld W = null!; public WorldState St = null!; public FamilyWorld Fw = null!; public int Hero; public int D0;
        public PersonTable Pt => St.Population.People;
        public HouseholdTable Hht => St.Population.Households;
        public EntityId HeroId => St.Party!.Hero;
        public int HeroHh => Pt.Household[Hero];

        public Rig(bool female = false, FamilySettingsDef? settings = null, int heroAge = 26, Func<int, bool>? band = null)
        {
            W = SeamWorld.Get(1234);
            var home = W.BlockOrigin.Offset(1, 1);
            var dir = Ravensguard.Sim.WorldGen.ContinentSettings.FindBaseContent()!;
            var cs = Ravensguard.Sim.WorldGen.ContinentSettings.LoadDefaults(dir); cs.Seed = 1234;
            var hs = Ravensguard.Sim.WorldGen.History.HistorySettings.LoadDefaults(dir); hs.Seed = 1234;
            var region = RegionGenerator.Generate(W.Continent, W.History, home, W.Settings);
            LandmarkPlacer.Place(region, W.History, W.Content);
            var pop = Populator.Populate(region, W.History, W.Content);
            var h = HeroRecord.FromOrigin("Aldric Vane", false, W.Content.All<OriginDef>().OrderBy(o => o.Order).First());
            St = NewGameWorld.Create(W.Content, W.Continent, W.History, cs, hs, W.Settings, home.CellX, home.CellY, pop, h, null);
            St.Content = W.Content;
            Hero = St.Hero!.PersonIndex;
            D0 = (int)St.Clock.Now.TotalDays;
            if (female) Pt.Sex[Hero] = Sex.Female;
            Pt.BirthDay[Hero] = PersonTable.BirthDayFor(D0, heroAge, 0, Pt.AgingPace);
            Fw = Make(settings, band);
            St.Economy!.Ledger.Mint(Fw.HeroPurse, new Coin(50_000), SourceReason.Seed);
        }

        public FamilyWorld Make(FamilySettingsDef? settings = null, Func<int, bool>? band = null) => new()
        {
            St = St, Content = W.Content, F = settings == null ? FamilyContent.From(W.Content) : new FamilyContent(settings), HeroId = HeroId, Rates = VitalRates.EconomyDefault, BandNear = band ?? (_ => false),
        };

        /// <summary>A free adult of the other sex in a house with a head and company, 24 to 32 years old, whom the hero may court.</summary>
        public int PickMate(int day, int minAge = 24, int maxAge = 32)
        {
            var want = Pt.Sex[Hero] == Sex.Female ? Sex.Male : Sex.Female;
            for (int i = 0; i < Pt.Count; i++)
            {
                if (i == Hero || Pt.Status[i] != PersonStatus.Alive || Pt.Sex[i] != want || Pt.Spouse[i] >= 0) continue;
                int age = Pt.AgeYears(i, day), hh = Pt.Household[i];
                if (age < minAge || age > maxAge || hh < 0 || hh == HeroHh || Hht.Head[hh] < 0 || Hht.Head[hh] == i || Hht.MemberCount[hh] < 2 || Pt.Status[Hht.Head[hh]] != PersonStatus.Alive) continue;
                if (Courting.WhyNot(Fw, Hero, i, day).Length == 0) return i;
            }
            throw new InvalidOperationException("no one to court in the test town");
        }

        public void GiveRoof() { var hh = HeroHh; Hht.Lot[hh] = 0; Hht.Dwelling[hh] = DwellingType.Cottage; Hht.Beds[hh] = 6; }
        public void Trust(int person, int pm) => St.Party!.Know(Pt.IdOf(person)).TrustPm = pm;
        public long Purse(PurseId p) => St.Economy!.Ledger.Balance(p).Mites;
        public void SetHousehold(int hh, long mites)
        {
            var l = St.Economy!.Ledger; var p = St.Economy.HouseholdPurse[hh]; long have = l.Balance(p).Mites;
            if (have < mites) l.Mint(p, new Coin(mites - have), SourceReason.Seed);
            else if (have > mites) l.Transfer(p, Fw.HeroPurse, new Coin(have - mites), TransferReason.Gift);
        }

        /// <summary>Courts, asks and returns the bride; trust is set high so the answer is yes whatever the jitter.</summary>
        public int Betroth(int day)
        {
            int p = PickMate(day); int head = Hht.Head[Pt.Household[p]];
            Hht.Class[Pt.Household[p]] = Pt.Class[Hero];
            Trust(p, 900); Trust(head, 900);
            var c = Courting.Court(Fw, Pt.IdOf(p).Value, 0, day); Assert.True(c.Ok, c.Key);
            var a = Courting.Ask(Fw, Pt.IdOf(p).Value, day); Assert.True(a.Ok, a.Key);
            return p;
        }

        public int AddChild(int ageYears, int father, int mother, int day)
        {
            St.Ids.EnsureNext(IdKind.Person, Pt.BaseCounter + (ulong)Pt.Count); St.Ids.Allocate(IdKind.Person);
            int c = Pt.Add(); St.Economy!.AddPerson(c);
            Pt.Given[c] = "Wren"; Pt.Family[c] = "Vane"; Pt.Sex[c] = Sex.Male; Pt.BirthDay[c] = PersonTable.BirthDayFor(day, ageYears, 0, Pt.AgingPace);
            Pt.Father[c] = father; Pt.Mother[c] = mother; Pt.Household[c] = HeroHh; Pt.HomeSettlement[c] = Hht.Settlement[HeroHh]; Pt.Class[c] = Hht.Class[HeroHh];
            Pt.Status[c] = PersonStatus.Alive; Pt.Culture[c] = Pt.Culture[Hero];
            Hht.AddMemberLater(HeroHh, c); St.Population.ResetKin();
            return c;
        }
    }

    // ---------------------------------------------------------------- FL-1

    [Fact]
    public void FL1_TheCourtingScore_IsTrustOverTenPlusTwenty_AndOneClassApartIsNoGood()
    {
        var s = new FamilySettingsDef { Id = "family" };
        Assert.Equal(70, FamilyRules.Acceptance(s, 500, 0, 0, 0, 0));
        Assert.True(70 >= s.YesAt);
        Assert.Equal(40, FamilyRules.Acceptance(s, 500, 1, 0, 0, 0)); Assert.True(40 < s.YesAt);
        Assert.Equal(80, FamilyRules.Acceptance(s, 500, 0, 3, 0, 0));          // a richer suitor: +10
        Assert.Equal(60, FamilyRules.Acceptance(s, 500, 0, -1, 0, 0));          // a poorer: -10
        Assert.Equal(75, FamilyRules.Acceptance(s, 500, 0, 0, 100, 0));         // standing 100 pm: +5
        // the jitter is a draw on stream 0x640 keyed on the courtship and the day: the same each time, within the range, and it varies
        var seen = new HashSet<int>();
        for (int d = 0; d < 200; d++) { int j = FamilyRules.Jitter(s, 1234, 5, d, 0); Assert.InRange(j, -10, 10); Assert.Equal(j, FamilyRules.Jitter(s, 1234, 5, d, 0)); seen.Add(j); }
        Assert.True(seen.Count >= 15);
    }

    // ---------------------------------------------------------------- FL-2

    [Fact]
    public void FL2_TheHeadConsents_TheWeddingIsOnTheNextHolyDayAtLeastFiveDaysOff_ThePriestIsPaid_AndTheBrideJoinsTheHousehold()
    {
        var r = new Rig(); r.GiveRoof();
        int day = r.D0 + 3;
        int p = r.Betroth(day);
        var c = r.St.Family!.Courtships.Values.Single();
        Assert.Equal(CourtState.Betrothed, c.State); Assert.Equal(ConsentState.Yes, c.Consent);
        Assert.True(c.WeddingDay >= day + 5 && c.WeddingDay % r.Fw.S.HolyDayEvery == 0, "day " + c.WeddingDay);
        Assert.Equal(c.WeddingDay, FamilyRules.NextHolyDay(r.Fw.S, day));
        int settlement = r.Hht.Settlement[r.HeroHh]; var fee = Courting.FeePurse(r.Fw, settlement);
        long heroBefore = r.Purse(r.Fw.HeroPurse), feeBefore = fee is { } f0 ? r.Purse(f0) : 0, spouseHh = r.Pt.Household[p];
        for (int d = day; d < c.WeddingDay; d++) { FamilySystem.Daily(r.Fw, d); Assert.Equal(CourtState.Betrothed, c.State); Assert.True(r.Pt.Spouse[p] < 0); }
        FamilySystem.Daily(r.Fw, c.WeddingDay);
        Assert.Equal(CourtState.Wed, c.State);
        Assert.Equal(p, r.Pt.Spouse[r.Hero]); Assert.Equal(r.Hero, r.Pt.Spouse[p]); Assert.Equal(r.HeroHh, r.Pt.Household[p]);
        Assert.Contains(r.Hht.Members(r.HeroHh).ToList(), m => m == p); Assert.DoesNotContain(r.Hht.Members((int)spouseHh).ToList(), m => m == p);
        Assert.Contains(r.St.Succession!.Kin, k => k.Person == r.Pt.IdOf(p) && k.Relation == KinRelation.Spouse);
        if (fee is { } f1) { Assert.Equal(100, r.Purse(f1) - feeBefore); Assert.Equal(heroBefore - 100, r.Purse(r.Fw.HeroPurse)); }
        var entry = r.St.Chronicle.All.Single(e => e.TypeId == "hero_wedding");
        Assert.Equal("ch_family_wedding_dowry", entry.TextKey); Assert.Equal(CauseKind.Family, entry.Causes[0].Kind); Assert.Equal("ch_note_courted_and_wed", entry.Causes[0].NoteKey);
        Assert.True(r.St.Economy!.Ledger.Audit().IsConserved);
        output.WriteLine($"wed on day {c.WeddingDay} ({c.WeddingDay - day} days after the betrothal)");
    }

    [Fact]
    public void FL2_APleaIsAnsweredOncePerDay_AHouseThatSaysNoStopsTheMatch_AndNoOneIsCourtedWhoMayNotBe()
    {
        var r = new Rig(); r.GiveRoof(); int day = r.D0 + 2;
        int p = r.PickMate(day); int head = r.Hht.Head[r.Pt.Household[p]]; ulong id = r.Pt.IdOf(p).Value;
        Assert.Equal("fm_not_courted", Courting.Ask(r.Fw, id, day).Key);
        r.Hht.Class[r.Pt.Household[p]] = r.Pt.Class[r.Hero]; r.Trust(p, 900); r.Trust(head, -900);
        Assert.True(Courting.Court(r.Fw, id, 0, day).Ok);
        var no = Courting.Ask(r.Fw, id, day); Assert.False(no.Ok); Assert.Equal("fm_house_no", no.Key);
        Assert.Equal("fm_asked_today", Courting.Ask(r.Fw, id, day).Key);
        Assert.Equal(CourtState.Courting, r.St.Family!.Courtships.Values.Single().State);
        // kin within degree 2 may not wed, and a person of the hero's own sex is not courted
        int mother = r.Pt.Mother[r.Hero], father = r.Pt.Father[r.Hero];
        var kin2 = Courting.Kin2(r.Fw, r.Hero);
        if (mother >= 0) Assert.Contains(mother, kin2);
        if (father >= 0) Assert.Contains(father, kin2);
        Assert.Equal("fm_not_so", Courting.WhyNot(r.Fw, r.Hero, FirstOf(r, r.Pt.Sex[r.Hero], day), day));
    }

    private static int FirstOf(Rig r, Sex sex, int day)
    {
        for (int i = 0; i < r.Pt.Count; i++) if (i != r.Hero && r.Pt.Status[i] == PersonStatus.Alive && r.Pt.Sex[i] == sex && r.Pt.Spouse[i] < 0 && r.Pt.AgeYears(i, day) >= 26 && r.Pt.Household[i] != r.HeroHh) return i;
        return -1;
    }

    // ---------------------------------------------------------------- FL-3

    [Fact]
    public void FL3_ABetrothalWaitsUntilTheHeroHoldsADwellingWithAFreeBed()
    {
        var r = new Rig(); int day = r.D0 + 3;
        Assert.True(r.Hht.Lot[r.HeroHh] < 0);                                    // the hero's household has no dwelling at the start
        int p = r.Betroth(day);
        var c = r.St.Family!.Courtships.Values.Single();
        Assert.Equal(CourtState.Betrothed, c.State); Assert.Equal(-1, c.WeddingDay);
        for (int d = day; d < day + 40; d++) FamilySystem.Daily(r.Fw, d);
        Assert.Equal(CourtState.Betrothed, c.State); Assert.Equal(-1, c.WeddingDay); Assert.True(r.Pt.Spouse[p] < 0);
        r.GiveRoof();
        FamilySystem.Daily(r.Fw, day + 40);
        Assert.Equal(FamilyRules.NextHolyDay(r.Fw.S, day + 40), c.WeddingDay);
        for (int d = day + 41; d <= c.WeddingDay; d++) FamilySystem.Daily(r.Fw, d);
        Assert.Equal(CourtState.Wed, c.State); Assert.Equal(r.Hero, r.Pt.Spouse[p]);
    }

    [Fact]
    public void FL3_AFullDwellingIsNoRoof_AndAHeldHomeBecomesTheHouseholdsAtTheWedding()
    {
        var r = new Rig(); int day = r.D0 + 3;
        r.GiveRoof(); r.Hht.Beds[r.HeroHh] = (short)r.Hht.MemberCount[r.HeroHh];       // every bed taken
        Assert.False(Courting.RoofFor(r.Fw, r.Hero).Ok);
        r.Hht.Lot[r.HeroHh] = -1; r.Hht.Dwelling[r.HeroHh] = DwellingType.None; r.Hht.Beds[r.HeroHh] = 0;
        // a vacant house of his: the first lot of the region with no household and room, as a deed would name it
        var pop = r.St.Population; int hs = -1, hl = -1;
        for (int s = 0; s < pop.LotHousehold.Length && hs < 0; s++)
            for (int l = 0; l < pop.LotHousehold[s].Length; l++)
                if (pop.LotHousehold[s][l] < 0 && pop.Region.Settlements[s].Plan.Lots[l].Dwelling is DwellingType.Cottage or DwellingType.Longhouse or DwellingType.Burgage) { hs = s; hl = l; break; }
        if (hs < 0) { output.WriteLine("no vacant lot in the test town: the held-home path is not covered here"); return; }
        var fw = new FamilyWorld { St = r.St, Content = r.W.Content, F = r.Fw.F, HeroId = r.HeroId, Rates = r.Fw.Rates, HeldHomes = () => [(hs, hl)] };
        var roof = Courting.RoofFor(fw, r.Hero); Assert.True(roof.Ok); Assert.True(roof.MoveIn); Assert.Equal((hs, hl), (roof.Settlement, roof.Lot));
        r.Fw = fw;
        int p = r.Betroth(day); var c = r.St.Family!.Courtships.Values.Single();
        for (int d = day; d <= c.WeddingDay; d++) FamilySystem.Daily(r.Fw, d);
        Assert.Equal(CourtState.Wed, c.State);
        Assert.Equal(hl, r.Hht.Lot[r.HeroHh]); Assert.Equal(hs, r.Hht.Settlement[r.HeroHh]); Assert.Equal(r.HeroHh, pop.LotHousehold[hs][hl]);
        Assert.True(r.Hht.Beds[r.HeroHh] >= 2);
        Assert.Equal(r.Hht.Lot[r.HeroHh], r.Pt.HomeLot[p]);
    }

    // ---------------------------------------------------------------- FL-4

    [Fact]
    public void FL4_ABurgherBridesHouseWithThirtyThousandMitesGivesThreeThousandAtTheWedding_CoinConserved()
    {
        var r = new Rig(); r.GiveRoof(); int day = r.D0 + 3;
        int p = r.PickMate(day); int bh = r.Pt.Household[p]; int head = r.Hht.Head[bh];
        r.Hht.Class[bh] = SocialClass.Burgher; r.Pt.Class[r.Hero] = SocialClass.Burgher; r.SetHousehold(bh, 30_000);
        r.Trust(p, 900); r.Trust(head, 900);
        Assert.True(Courting.Court(r.Fw, r.Pt.IdOf(p).Value, 0, day).Ok);
        Assert.True(Courting.Ask(r.Fw, r.Pt.IdOf(p).Value, day).Ok);
        var c = r.St.Family!.Courtships.Values.Single();
        long hhBefore = r.Purse(r.St.Economy!.HouseholdPurse[r.HeroHh]), total = r.St.Economy.Ledger.Audit().InPurses.Mites;
        for (int d = day; d <= c.WeddingDay; d++) FamilySystem.Daily(r.Fw, d);
        Assert.Equal(CourtState.Wed, c.State);
        Assert.Equal(27_000, r.Purse(r.St.Economy.HouseholdPurse[bh]));
        Assert.Equal(hhBefore + 3_000, r.Purse(r.St.Economy.HouseholdPurse[r.HeroHh]));
        Assert.True(r.St.Economy.Ledger.Audit().IsConserved); Assert.Equal(total, r.St.Economy.Ledger.Audit().InPurses.Mites);
        Assert.Equal(3_000, r.St.Chronicle.All.Single(e => e.TypeId == "hero_wedding").Params!.Single(x => x.Name == "dowry").Value);
        Assert.Equal(10, r.Fw.F.DowryPct(SocialClass.Burgher)); Assert.Equal(5, r.Fw.F.DowryPct(SocialClass.Peasant)); Assert.Equal(20, r.Fw.F.DowryPct(SocialClass.Gentry));
    }

    // ---------------------------------------------------------------- FL-7

    [Fact]
    public void FL7_AMarriedHeroCoupleOverTenYears_HasChildrenAtThePhase2Rate_EveryChildARowWithBothParentsBornIntoTheHousehold()
    {
        var s = new FamilySettingsDef { Id = "family", ChildbirthRiskPct = 0 };
        var r = new Rig(female: true, settings: s, heroAge: 24); r.GiveRoof(); int day = r.D0 + 3;
        int groom = r.Betroth(day); var c = r.St.Family!.Courtships.Values.Single();
        for (int d = day; d <= c.WeddingDay; d++) FamilySystem.Daily(r.Fw, d);
        Assert.Equal(CourtState.Wed, c.State);
        int start = c.WeddingDay + 1, births = 0;
        var kids = new List<int>();
        for (int d = start; d < start + 600; d++)
        {
            FamilySystem.Daily(r.Fw, d);
            // the children grow up with the household (their own deaths are the Phase 2 rolls', not under test here)
        }
        for (int i = 0; i < r.Pt.Count; i++) if (r.Pt.Mother[i] == r.Hero) kids.Add(i);
        births = kids.Count;
        output.WriteLine($"{births} children in ten game years");
        Assert.InRange(births, 1, 7);
        foreach (int k in kids)
        {
            Assert.Equal(r.Hero, r.Pt.Mother[k]); Assert.Equal(groom, r.Pt.Father[k]); Assert.Equal(r.HeroHh, r.Pt.Household[k]); Assert.Equal(PersonStatus.Alive, r.Pt.Status[k]);
            Assert.Contains(r.St.Succession!.Kin, x => x.Person == r.Pt.IdOf(k) && x.Relation == KinRelation.Child);
            Assert.Contains(r.Pt.IdOf(k).Value, r.St.Family!.Born);
            for (int a = 0; a < PersonTable.AttrCount; a++)
            {
                int mean = (r.Pt.Attribute(r.Hero, a) + r.Pt.Attribute(groom, a)) / 2;
                Assert.InRange(r.Pt.Attribute(k, a), Math.Max(1, mean - 2 - 1), mean + 2 + 1);          // the parents' mean plus or minus two (the hero's attributes are mirrored on the row at the barrier)
            }
        }
        Assert.Equal(births, r.St.Chronicle.All.Count(e => e.TypeId == "hero_child_born"));
        Assert.True(r.St.Economy!.Ledger.Audit().IsConserved);
    }

    [Fact]
    public void FL7_AChildBornToTheHerosWifeIsNoticed_AttributesFromTheParentsMean_AndTheKinLinkIsMade()
    {
        var r = new Rig(); r.GiveRoof(); int day = r.D0 + 3;
        int wife = r.Betroth(day); var c = r.St.Family!.Courtships.Values.Single();
        for (int d = day; d <= c.WeddingDay; d++) FamilySystem.Daily(r.Fw, d);
        int d2 = c.WeddingDay + 1;
        int child = r.AddChild(0, r.Hero, wife, d2);                    // the Phase 2 birth the wife's household rolls: a newborn row of the hero's household
        FamilySystem.Daily(r.Fw, d2);
        Assert.Contains(r.Pt.IdOf(child).Value, r.St.Family!.Born);
        Assert.Contains(r.St.Succession!.Kin, x => x.Person == r.Pt.IdOf(child) && x.Relation == KinRelation.Child);
        for (int a = 0; a < PersonTable.AttrCount; a++)
        {
            int mean = (r.Pt.Attribute(r.Hero, a) + r.Pt.Attribute(wife, a)) / 2;
            Assert.InRange(r.Pt.Attribute(child, a), Math.Max(1, mean - 2), mean + 2);
        }
        Assert.Contains(r.St.Chronicle.All, e => e.TypeId == "hero_child_born" && e.Subjects.Contains(r.Pt.IdOf(child)));
        int noted = r.St.Chronicle.All.Count(e => e.TypeId == "hero_child_born"); FamilySystem.Daily(r.Fw, d2 + 1); Assert.Equal(noted, r.St.Chronicle.All.Count(e => e.TypeId == "hero_child_born"));       // noticed once
    }

    // ---------------------------------------------------------------- FL-8

    [Fact]
    public void FL8_AChildTaughtArmsFromSevenToSixteen_HasBladesAtFiftySeven_AnUntaughtChildFromTenHasThirtyOneInTheHouseholdTrade()
    {
        var r = new Rig(heroAge: 30); int day = r.D0;
        r.Pt.SetSkill(r.Hero, 0, Skill.Farming, 40);
        int taught = r.AddChild(7, r.Hero, -1, day), untaught = r.AddChild(10, r.Hero, -1, day);
        // the untaught one is noticed from day 0; the taught one has the hero himself for a tutor
        var t = Ravensguard.Sim.Family.FamilyDesk.Teach(r.Fw, r.Pt.IdOf(taught).Value, Course.Arms, Skill.None, r.HeroId, day); Assert.True(t.Ok, t.Key);
        int strBefore = r.Pt.Attribute(taught, PersonTable.StrengthIdx);
        for (int d = day; d < day + 540; d++)
        {
            FamilySystem.Daily(r.Fw, d);
        }
        int blades = r.Pt.SkillLevelOf(taught, Skill.Blades);
        Assert.InRange(blades, 56, 58);
        // the untaught child's 360 days of half-rate lessons (6 years: ages 10 to 16) fall in the first 360 days
        Assert.InRange(r.Pt.SkillLevelOf(untaught, Skill.Farming), 30, 32);
        Assert.Equal(540 * 4, r.St.Family!.Upbringings[r.Pt.IdOf(taught).Value].Hours);
        Assert.InRange(r.Pt.Attribute(taught, PersonTable.StrengthIdx) - strBefore, 10, 11);        // +0.02 a day for 540 days
        output.WriteLine($"blades {blades}, untaught farming {r.Pt.SkillLevelOf(untaught, Skill.Farming)}");
        // the lesson formula alone: 1 / (1 - s / 100) = 1 + 0.0025 t
        long milli = 0; for (int i = 0; i < 540; i++) milli += FamilyRules.LessonGain(250, milli);
        Assert.InRange(milli, 57_000, 58_000);
    }

    [Fact]
    public void FL8_AHiredTutorIsPaidTheDaysWage_AndLessonsStopWhenTheHeroCannotPay()
    {
        var r = new Rig(heroAge: 30); int day = r.D0;
        int child = r.AddChild(8, r.Hero, -1, day);
        int tutor = FirstOf(r, Sex.Male, day);
        Assert.True(FamilyDesk.Teach(r.Fw, r.Pt.IdOf(child).Value, Course.Letters, Skill.None, r.Pt.IdOf(tutor), day).Ok);
        long before = r.Purse(r.Fw.HeroPurse), tutorHh = r.Purse(r.St.Economy!.HouseholdPurse[r.Pt.Household[tutor]]);
        FamilySystem.Daily(r.Fw, day);
        Assert.Equal(before - 350, r.Purse(r.Fw.HeroPurse)); Assert.Equal(tutorHh + 350, r.Purse(r.St.Economy.HouseholdPurse[r.Pt.Household[tutor]]));
        Assert.Equal(4, r.St.Family!.Upbringings[r.Pt.IdOf(child).Value].Hours);
        // broke: no lesson
        r.St.Economy.Ledger.Transfer(r.Fw.HeroPurse, r.St.Economy.HouseholdPurse[r.Pt.Household[tutor]], new Coin(r.Purse(r.Fw.HeroPurse)), TransferReason.Gift);
        FamilySystem.Daily(r.Fw, day + 1);
        Assert.Equal(4, r.St.Family!.Upbringings[r.Pt.IdOf(child).Value].Hours);
        Assert.True(r.St.Economy.Ledger.Audit().IsConserved);
    }

    // ---------------------------------------------------------------- FL-9

    private static int ExpectedStrengthLoss(int age) { int l = 0; for (int b = 40; b <= age; b++) l += b <= 49 ? (b % 2 == 0 ? 1 : 0) : b <= 59 ? 1 : 2; return l; }
    private static int ExpectedToughnessLoss(int age) { int l = 0; for (int b = 45; b <= age; b++) l += b <= 59 ? 1 : 2; return l; }

    [Fact]
    public void FL9_AHeroOfFiftyWhoTurnsFortyOnDayZero_LosesStrengthAndToughnessOnTheTable()
    {
        var noDeath = new FamilySettingsDef { Id = "family", OldAge = [new OldAgeRow { From = 0, Pct = 0 }] };
        var r = new Rig(settings: noDeath, heroAge: 40);
        var hero = r.St.Hero!; for (int a = 0; a < 5; a++) hero.Attributes[a] = 50;
        r.St.Family = new FamilyBook { Ageing = new HeroAgeing { Hero = r.HeroId.Value, Row = r.Hero, LastAge = 39 } };
        r.Pt.BirthDay[r.Hero] = PersonTable.BirthDayFor(r.D0, 40, 0, r.Pt.AgingPace);
        int last = 39;
        for (int d = r.D0; d < r.D0 + 35 * 60 + 5; d++)
        {
            FamilySystem.Daily(r.Fw, d);
            int age = r.Pt.AgeYears(r.Hero, d);
            if (age == last || age > 75) continue;
            last = age;
            Assert.Equal(50 - ExpectedStrengthLoss(age), hero.Attributes[PersonTable.StrengthIdx]);
            Assert.Equal(50 - ExpectedToughnessLoss(age), hero.Attributes[PersonTable.ToughnessIdx]);
            if (age == 52) Assert.Equal(42, hero.Attributes[0]);            // -5 at 40 to 49, -3 at 50 to 52
            if (age == 64) Assert.Equal(25, hero.Attributes[1]);            // -15 at 45 to 59, -10 at 60 to 64
            if (age == 70) Assert.Equal(13, hero.Attributes[0]);            // -5, -10, -22
            Assert.Equal(hero.Attributes[PersonTable.StrengthIdx], r.Pt.Attribute(r.Hero, PersonTable.StrengthIdx));        // the row mirrors it
        }
        Assert.Equal(75, last);
        Assert.Equal(50 - ExpectedStrengthLoss(75), hero.Attributes[0]);
        Assert.Equal(ExpectedStrengthLoss(75), r.St.Family!.Ageing!.Lost[0]);
    }

    [Fact]
    public void FL9_TheAgingPaceSetting_MovesTheBirthdaysNotTheTable()
    {
        var noDeath = new FamilySettingsDef { Id = "family", OldAge = [new OldAgeRow { From = 0, Pct = 0 }] };
        var r = new Rig(settings: noDeath, heroAge: 40);
        r.Pt.AgingPace = 4;
        r.Pt.BirthDay[r.Hero] = PersonTable.BirthDayFor(r.D0, 40, 0, 4);
        var hero = r.St.Hero!; for (int a = 0; a < 5; a++) hero.Attributes[a] = 50;
        r.St.Family = new FamilyBook { Ageing = new HeroAgeing { Hero = r.HeroId.Value, Row = r.Hero, LastAge = 39 } };
        for (int d = r.D0; d < r.D0 + 12 * 60 / 4 + 1; d++) FamilySystem.Daily(r.Fw, d);        // twelve years of ageing in a quarter of the days
        Assert.Equal(52, r.Pt.AgeYears(r.Hero, r.D0 + 12 * 60 / 4));
        Assert.Equal(42, hero.Attributes[0]);
    }

    // ---------------------------------------------------------------- FL-10

    [Fact]
    public void FL10_OldAgeKills_TenThousandHeroesFromFifty_SurviveToSixtyFiveAtTheProductOfTheHazardTable()
    {
        var c = FamilyContent.Default; int alive = 0, n = 10_000;
        for (ulong h = 1; h <= (ulong)n; h++)
        {
            bool dead = false;
            for (int age = 51; age <= 65 && !dead; age++) dead = FamilyRules.DiesOfOldAge(c, 1234, h * 7919, age, age);
            if (!dead) alive++;
        }
        double want = Math.Pow(0.99, 9) * Math.Pow(0.97, 5) * 0.90, got = alive / (double)n;
        output.WriteLine($"survivors {got:P2} vs {want:P2}");
        Assert.InRange(got, want - 0.015, want + 0.015);
        Assert.Equal(0, c.OldAgePct(49)); Assert.Equal(1, c.OldAgePct(50)); Assert.Equal(3, c.OldAgePct(60)); Assert.Equal(10, c.OldAgePct(65)); Assert.Equal(20, c.OldAgePct(75)); Assert.Equal(20, c.OldAgePct(90));
    }

    [Fact]
    public void FL10_AHeroWhoDiesOfOldAge_IsWarnedASeasonBefore_AndTheHeirHeRaisedTakesOver()
    {
        var surely = new FamilySettingsDef { Id = "family", OldAge = [new OldAgeRow { From = 0, Pct = 0 }, new OldAgeRow { From = 61, Pct = 100 }] };
        var r = new Rig(settings: surely, heroAge: 60);
        r.St.Family = new FamilyBook { Ageing = new HeroAgeing { Hero = r.HeroId.Value, Row = r.Hero, LastAge = 60 } };
        int heir = r.AddChild(17, r.Hero, -1, r.D0);
        r.St.Succession ??= new SuccessionBook(); r.St.Succession.Kin.Add(new HeroKinLink(r.Pt.IdOf(heir), KinRelation.Child));
        r.Pt.BirthDay[r.Hero] = PersonTable.BirthDayFor(r.D0, 60, 0, r.Pt.AgingPace);
        int dead = -1;
        // the warning needs a hazard of at least 3 percent the coming year: the table's 100 percent at 61 is one
        for (int d = r.D0; d < r.D0 + 120 && dead < 0; d++) { FamilySystem.Daily(r.Fw, d); if (r.St.Family?.Death != null) dead = d; }
        Assert.True(dead > 0);
        var warn = r.St.Chronicle.All.Single(e => e.TypeId == "old_age_warning");
        var died = r.St.Chronicle.All.Single(e => e.TypeId == "hero_died_old_age");
        Assert.InRange(died.When.TotalDays - warn.When.TotalDays, 10, 20);                        // a season before
        Assert.Equal(61, r.St.Family!.Death!.Age); Assert.Equal(DeathCause.Age, r.St.Family.Death.Cause);
        Assert.Equal("ch_family_died_old_age", died.TextKey); Assert.Equal("ch_note_old_age", died.Causes[0].NoteKey);
        // the day stops with the hero's death: nothing else ran on the day after
        int n0 = r.St.Chronicle.All.Count; FamilySystem.Daily(r.Fw, dead + 1); Assert.Equal(n0, r.St.Chronicle.All.Count);
        // the succession screen's command makes the heir the hero
        var s = r.St; var ctx = new HeroContext(s, s.Party!, r.W.Content); var desk = new HeroDesk();
        var heirId = r.Pt.IdOf(heir);
        var cand = SuccessionDesk.Candidates(s, s.Party!, dead);
        Assert.Equal(heirId, cand.First().Person); Assert.Equal(SuccessionRule.Child, cand.First().Rule);
        desk.Issue(SuccessionDesk.HeirCommand(heirId, 0, 0, dead * 86400L + 5 * 3600));
        desk.ApplyDue(ctx, dead * 24L + 6);
        Assert.All(desk.View!.Results, x => Assert.True(x.Ok, x.Text));
        Assert.Equal(PersonStatus.Dead, r.Pt.Status[r.Hero]); Assert.Equal(heir, s.Hero!.PersonIndex);
        // the heir has taken up: the pending death clears and the old row keeps its cause
        var after = new FamilyWorld { St = s, Content = r.W.Content, F = r.Fw.F, HeroId = r.HeroId, Rates = r.Fw.Rates };
        FamilySystem.Daily(after, dead + 2);
        Assert.Null(s.Family?.Death); Assert.Equal(DeathCause.Age, r.Pt.DeathCause[r.Hero]);
    }

    // ---------------------------------------------------------------- FL-11

    [Fact]
    public void FL11_RenownFiveHundredAndABandNear_AKidnappingIsFourPercentASeason()
    {
        var s = new FamilySettingsDef { Id = "family" }; int hits = 0, seasons = 10_000;
        for (int season = 0; season < seasons; season++) if (FamilySystem.KidnapDraw(s, 1234, 42, season, 500)) hits++;
        output.WriteLine($"{hits} of {seasons} seasons");
        Assert.InRange(hits / (double)seasons, 0.035, 0.045);
        int none = 0; for (int season = 0; season < seasons; season++) if (FamilySystem.KidnapDraw(s, 1234, 42, season, 0)) none++;
        Assert.Equal(0, none);                                                                       // no renown, no ransom worth the trouble
        Assert.Equal(400, FamilySystem.KidnapChanceX100(s, 500)); Assert.Equal(200, FamilySystem.KidnapChanceX100(s, 250));
    }

    [Fact]
    public void FL11_ABandNearTakesAKinMemberForRansom_AtThePhase4RankPrice_AndTheDeedAndRumourFollow()
    {
        var r = new Rig(band: _ => true); r.GiveRoof(); int day = r.D0 + 3;
        int wife = r.Betroth(day); var c = r.St.Family!.Courtships.Values.Single();
        for (int d = day; d <= c.WeddingDay; d++) FamilySystem.Daily(r.Fw, d);
        r.St.Standing ??= new Ravensguard.Sim.Society.StandingBook();
        r.St.Standing.Renowns[r.HeroId.Value] = new Ravensguard.Sim.Society.RenownRow { SharePm = 500, DecayPm = 1000 };
        Assert.Equal(500, r.Fw.RenownPm);
        int season = -1;
        for (int s = c.WeddingDay / 15 + 1; s < c.WeddingDay / 15 + 4000 && season < 0; s++) if (FamilySystem.KidnapDraw(r.Fw.S, r.Fw.Seed, r.HeroId.Value, s, 500)) season = s;
        Assert.True(season > 0);
        Assert.True(FamilySystem.TryKidnap(r.Fw, season * 15, r.Hero, season));
        var cap = r.St.Captivity!.Captives.Values.Single(x => x.Held);
        Assert.Equal(CaptorKindOf(), cap.Captor); Assert.Equal(r.HeroId.Value, cap.Payers.Single().Owner.Value);
        var rules = r.St.Captivity.Rules; var rank = r.Pt.Class[r.Pt.IndexOf(cap.Person)] switch { SocialClass.Gentry => Ravensguard.Sim.Combat.Aftermath.CaptiveRank.Knight, SocialClass.Burgher => Ravensguard.Sim.Combat.Aftermath.CaptiveRank.ManAtArms, _ => Ravensguard.Sim.Combat.Aftermath.CaptiveRank.Levy };
        Assert.Equal(Ravensguard.Sim.Combat.Aftermath.Ransom.ForRank(rules, rank), cap.RansomMites);
        Assert.Equal(1, r.St.Family!.Kidnaps);
        var e = r.St.Chronicle.All.Single(x => x.TypeId == "kin_kidnapped");
        Assert.Equal("ch_family_kidnapped", e.TextKey); Assert.Contains(cap.Person, e.Subjects); Assert.Equal(cap.RansomMites, e.Params!.Single(x => x.Name == "ransom").Value);
        Assert.Equal("ch_note_kidnappers", e.Causes[0].NoteKey);
        Assert.True(r.St.Economy!.Ledger.Audit().IsConserved);
        // not the same kin twice while held, and nobody left to take returns false
        for (int s = season + 1; s < season + 8000; s++)
            if (FamilySystem.KidnapDraw(r.Fw.S, r.Fw.Seed, r.HeroId.Value, s, 500)) { FamilySystem.TryKidnap(r.Fw, s * 15, r.Hero, s); break; }
        Assert.True(r.St.Captivity.Captives.Values.Count(x => x.Held) <= r.St.Succession!.Kin.Count);
    }

    [Fact]
    public void S3_AKidnapBandIsStockedFromRealCoin_SoTheDailyFeedingKeepsTheCaptiveTheWholeHold()
    {
        var r = new Rig(band: _ => true); r.GiveRoof(); int day = r.D0 + 3;
        r.Betroth(day); var c = r.St.Family!.Courtships.Values.Single();
        for (int d = day; d <= c.WeddingDay; d++) FamilySystem.Daily(r.Fw, d);
        r.St.Standing ??= new Ravensguard.Sim.Society.StandingBook();
        r.St.Standing.Renowns[r.HeroId.Value] = new Ravensguard.Sim.Society.RenownRow { SharePm = 500, DecayPm = 1000 };
        int season = -1;
        for (int s = c.WeddingDay / 15 + 1; s < c.WeddingDay / 15 + 4000 && season < 0; s++) if (FamilySystem.KidnapDraw(r.Fw.S, r.Fw.Seed, r.HeroId.Value, s, 500)) season = s;
        var led = r.St.Economy!.Ledger; var sourced = led.Audit().Sourced;
        Assert.True(FamilySystem.TryKidnap(r.Fw, season * 15, r.Hero, season));
        Assert.Equal(sourced, led.Audit().Sourced);                                               // the stash moved, it was not created
        var cap = r.St.Captivity!.Captives.Values.Single(x => x.Held);
        var law = Ravensguard.Sim.Law.LawWorld.From(r.St, r.W.Content); var cb = new Ravensguard.Sim.Law.CrimeBook(); var jb = new Ravensguard.Sim.Law.JusticeBook();
        for (int d = 1; d <= 8; d++) Ravensguard.Sim.Law.Gaol.Feed(law, cb, jb, season * 15 + d, (season * 15 + d) * 86_400L);
        Assert.True(cap.Held, "the kidnappers kept feeding him; nobody was let go 'unfed'");
        Assert.True(led.Audit().IsConserved);
    }

    private static Ravensguard.Sim.Combat.Aftermath.CaptorKind CaptorKindOf() => Ravensguard.Sim.Combat.Aftermath.CaptorKind.Bandits;

    // ---------------------------------------------------------------- the desk and FL-15

    [Fact]
    public void TheDesk_CourtsAsksAndTeachesThroughTheHeroCommands_RecordedLikeAnyOther()
    {
        var r = new Rig(); r.GiveRoof(); int day = r.D0 + 3;
        int p = r.PickMate(day); int head = r.Hht.Head[r.Pt.Household[p]]; r.Hht.Class[r.Pt.Household[p]] = r.Pt.Class[r.Hero]; r.Trust(p, 900); r.Trust(head, 900);
        var s = r.St; var ctx = new HeroContext(s, s.Party!, r.W.Content); var desk = new HeroDesk();
        long sec = day * 86400L + 9 * 3600;
        Assert.True(desk.Issue(FamilyDesk.Court(r.Pt.IdOf(p), 200, sec)).Seq > 0);
        desk.Issue(FamilyDesk.Ask(r.Pt.IdOf(p), sec + 60));
        desk.ApplyDue(ctx, day * 24L + 10);
        var res = desk.View!.Results.Where(x => x.Kind == HeroCommandKind.Family).ToList();
        Assert.Equal(2, res.Count); Assert.All(res, x => Assert.True(x.Ok, x.Text));
        Assert.Equal(CourtState.Betrothed, s.Family!.Courtships.Values.Single().State); Assert.Equal(200, s.Family.Courtships.Values.Single().Gifts);
        desk.Issue(FamilyDesk.Break(r.Pt.IdOf(p), sec + 120));
        desk.ApplyDue(ctx, day * 24L + 11);
        Assert.Equal(CourtState.Broken, s.Family.Courtships.Values.Single().State);
        Assert.True(s.Economy!.Ledger.Audit().IsConserved);
    }

    [Fact]
    public void FL15_SaveMidCourtshipMidPregnancyMidLesson_LoadsAndSavesByteIdentical_AndTheFamilyHashIsEqual()
    {
        var r = new Rig(female: true, heroAge: 26); r.GiveRoof(); int day = r.D0 + 3;
        int groom = r.Betroth(day); var c = r.St.Family!.Courtships.Values.Single();
        int child = r.AddChild(8, r.Hero, groom, day);
        FamilyDesk.Teach(r.Fw, r.Pt.IdOf(child).Value, Course.Arms, Skill.None, r.HeroId, day);
        for (int d = day; d < day + 14; d++) FamilySystem.Daily(r.Fw, d);                        // mid-courtship: a betrothal with its wedding two days off at most, lessons running
        r.Pt.PregnantUntilDay[r.Hero] = day + 30; r.Pt.FatherOfUnborn[r.Hero] = groom;           // mid-pregnancy
        Assert.True(r.St.Family!.Upbringings[r.Pt.IdOf(child).Value].Milli.Count > 0);           // mid-lesson
        var file = WorldSave.Capture(r.St, "h", "0.1");
        Assert.Contains("family", file.Sections.Keys);
        byte[] a = SaveCodec.Encode(file);
        var st2 = WorldSave.Restore(SaveCodec.Decode(a), r.W.Content, "h", null, r.W.Continent, r.W.History); st2.Content = r.W.Content;
        byte[] b = SaveCodec.Encode(WorldSave.Capture(st2, "h", "0.1"));
        Assert.True(a.AsSpan().SequenceEqual(b), "the world did not save back byte for byte");
        Assert.Equal(r.St.Family!.Hash(), st2.Family!.Hash());
        Assert.Equal(day + 30, st2.Population.People.PregnantUntilDay[r.Hero]);
        // and the section is absent from a world that never had a family
        var plain = new Rig();
        plain.St.Family = null;
        Assert.DoesNotContain("family", WorldSave.Capture(plain.St, "h", "0.1").Sections.Keys);
    }
}
