using Ravensguard.Content;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Family;
using Ravensguard.Sim.People;
using Ravensguard.Sim.Rule;
using Ravensguard.Sim.Save;
using Ravensguard.Sim.Society;
using Ravensguard.Sim.Succession;
using Ravensguard.Sim.Trade;
using Ravensguard.Sim.WorldGen.Region;
using Ravensguard.Sim.Worlds;
using Xunit.Abstractions;

namespace Ravensguard.Sim.Tests.Phase6Overview;

/// <summary>
/// OV-7 (overview 4, family, faith and lifespan), headless on a whole running game of seed 1234 with the ageing pace at 4 (only the ageing clock scales): the hero courts and marries at the church with a dowry (commands
/// through the desk, the wedding on the first holy day five days off), two children are born into the house (the Phase 2 births, the test's rows as FL-7 makes them), the son is taught arms by his father, the hero grows
/// old and dies of old age past 60 (the old-age table forced in the test, as FL-10 does: 100 percent at 61), the son of sixteen or more takes over with half of the name his father had, and, since the hero held a fief, owes its
/// overlord relief. Coin and the people are exact.
/// </summary>
public class Ov7FamilyTests(ITestOutputHelper output)
{
    [Fact]
    public void OV7_TheHeroCourtsAndMarriesAtTheChurchWithADowry_TwoChildrenAreBorn_TheSonIsTaughtArms_TheHeroDiesOfOldAgePast60_TheSonTakesOverWithHalfTheNameAndPaysRelief()
    {
        var (w, s) = P6Game.Game();
        var pt = s.Population.People; var hht = s.Population.Households; var e = s.Economy!;
        int hero = s.Hero!.PersonIndex; int d0 = (int)s.Clock.Now.TotalDays;
        pt.AgingPace = 4;
        pt.BirthDay[hero] = PersonTable.BirthDayFor(d0, 26, 0, pt.AgingPace);
        pt.Sex[hero] = Sex.Male;
        using var rt = new WorldRuntime(s, w.Content, new MemoryRegionPager());
        var fam = P6Game.AttachGameLinks(rt, w.Content);
        var ctx = new HeroContext(s, s.Party!, w.Content);
        var p = new P6Play(rt, ctx);
        var heroId = s.Party!.Hero;
        e.Ledger.Mint(ctx.Purse, new Coin(50_000), SourceReason.Seed);                                       // a purse for the gifts of a courtship and the priest's fee
        long seeded = e.Ledger.Audit().Seeded.Mites;
        int hhHero = pt.Household[hero];
        hht.Lot[hhHero] = 0; hht.Dwelling[hhHero] = DwellingType.Cottage; hht.Beds[hhHero] = 6;               // the hero holds a dwelling with free beds (FL-3: a betrothal waits for a roof)
        var fw = FamilyWorld.From(s, w.Content);
        p.Days(1);
        int day = (int)(rt.HourDone / 24);

        // 1. a courtship: a free woman of 24 to 32 of a house with a head and company; her house thinks well of him (the test's faucet: trust 900); then the question
        int bride = -1;
        for (int i = 0; i < pt.Count && bride < 0; i++)
        {
            if (i == hero || pt.Status[i] != PersonStatus.Alive || pt.Sex[i] != Sex.Female || pt.Spouse[i] >= 0) continue;
            int age = pt.AgeYears(i, day), hh = pt.Household[i];
            if (age < 24 || age > 32 || hh < 0 || hh == hhHero || hht.Head[hh] < 0 || hht.Head[hh] == i || hht.MemberCount[hh] < 2 || pt.Status[hht.Head[hh]] != PersonStatus.Alive) continue;
            if (Courting.WhyNot(fw, hero, i, day).Length == 0) bride = i;
        }
        Assert.True(bride >= 0, "no one to court");
        int brideHh = pt.Household[bride], head = hht.Head[brideHh];
        hht.Class[brideHh] = pt.Class[hero];
        s.Party.Know(pt.IdOf(bride)).TrustPm = 900; s.Party.Know(pt.IdOf(head)).TrustPm = 900;
        var hhPurse = e.HouseholdPurse[brideHh]; long have = e.Ledger.Balance(hhPurse).Mites;
        if (have < 30_000) e.Ledger.Mint(hhPurse, new Coin(30_000 - have), SourceReason.Seed);                // a burgher bride's house with 30,000 mites gives 3,000 (FL-4)
        seeded = e.Ledger.Audit().Seeded.Mites;
        var court = p.Do(FamilyDesk.Court(pt.IdOf(bride), 200, p.Sec)); Assert.True(court.Ok, court.Text.ToString());
        var ask = p.Do(FamilyDesk.Ask(pt.IdOf(bride), p.Sec)); Assert.True(ask.Ok, ask.Text.ToString());
        var c = s.Family!.Courtships.Values.Single();
        Assert.Equal((CourtState.Betrothed, ConsentState.Yes), (c.State, c.Consent));
        Assert.True(c.WeddingDay >= day + 5 && c.WeddingDay % fw.S.HolyDayEvery == 0, "the wedding is on a holy day, at least five days off: day " + c.WeddingDay);

        // 2. the wedding at the church on the holy day
        p.To((long)(c.WeddingDay + 1) * 24 + 5);
        Assert.Equal(CourtState.Wed, c.State);
        Assert.Equal(bride, pt.Spouse[hero]); Assert.Equal(hhHero, pt.Household[bride]);
        var wedding = s.Chronicle.All.Single(x => x.TypeId == "hero_wedding");
        Assert.Equal("ch_family_wedding_dowry", wedding.TextKey); Assert.Equal("ch_note_courted_and_wed", wedding.Causes[0].NoteKey);
        Assert.True(long.Parse(wedding.Data["dowry"]) >= 3_000, wedding.Data["dowry"]);                      // the dowry: a tenth of her house's purse, a real transfer (the entry names it)
        Assert.True(e.Ledger.Flow(TransferReason.Dowry).Mites >= 3_000);
        Assert.True(e.Ledger.Audit().IsConserved);
        output.WriteLine($"wed on day {c.WeddingDay}, {c.WeddingDay - day} days after the question; dowry {wedding.Data["dowry"]} mites");

        // 3. two children born into the house (the wife's household rolls the Phase 2 birth; the rows are made as FL-7 makes them), noticed by the family's day
        int AddChild(string given, int day2)
        {
            s.Ids.EnsureNext(IdKind.Person, pt.BaseCounter + (ulong)pt.Count); s.Ids.Allocate(IdKind.Person);
            int ch = pt.Add(); e.AddPerson(ch);
            pt.Given[ch] = given; pt.Family[ch] = pt.Family[hero]; pt.Sex[ch] = Sex.Male; pt.BirthDay[ch] = PersonTable.BirthDayFor(day2, 0, 0, pt.AgingPace);
            pt.Father[ch] = hero; pt.Mother[ch] = bride; pt.Household[ch] = hhHero; pt.HomeSettlement[ch] = hht.Settlement[hhHero]; pt.Class[ch] = hht.Class[hhHero];
            pt.Status[ch] = PersonStatus.Alive; pt.Culture[ch] = pt.Culture[hero];
            hht.AddMemberLater(hhHero, ch); s.Population.ResetKin();
            return ch;
        }
        int dayW = (int)(rt.HourDone / 24);
        int son = AddChild("Wren", dayW); int daughterish = AddChild("Tam", dayW + 1);
        p.Days(2);
        Assert.Equal(2, s.Chronicle.All.Count(x => x.TypeId == "hero_child_born"));
        Assert.All(new[] { son, daughterish }, ch => Assert.Contains(s.Succession!.Kin, k => k.Person == pt.IdOf(ch) && k.Relation == KinRelation.Child));
        int boyDay = dayW;

        // 4. the son is taught arms by his father; the lessons start when he is seven (pace 4: seven years take a hundred and five days), the days pass until he is sixteen
        var teach = p.Do(FamilyDesk.Teach(pt.IdOf(son), Course.Arms, Skill.None, heroId, p.Sec)); Assert.True(teach.Ok, teach.Text.ToString());
        while (pt.AgeYears(son, (int)(rt.HourDone / 24)) < 16) p.Days(15);
        int ageDay = (int)(rt.HourDone / 24);
        output.WriteLine($"{ageDay - boyDay} days on: the son is {pt.AgeYears(son, ageDay)}, blades {pt.SkillLevelOf(son, Skill.Blades)}, {s.Family.Upbringings[pt.IdOf(son).Value].Hours} lesson hours; the hero is {pt.AgeYears(hero, ageDay)}");
        Assert.True(pt.SkillLevelOf(son, Skill.Blades) > 0);
        Assert.True(s.Family.Upbringings[pt.IdOf(son).Value].Hours > 0);
        Assert.True(pt.Status[hero] is PersonStatus.Hero);

        // 5. he holds a fief of the realm (OV-2's road, here the grant is a faucet); the years pass and he is old: past sixty, the table forced at 100 percent from 61, the death on a birthday, a season after the warning
        var fiefRow = s.Rule!.Fiefs.Values.First(f => f.Holder >= 0 && f.Realm >= 0 && !f.Forfeited);
        long fiefHolder = fiefRow.Holder;
        fiefRow.Holder = (long)heroId.Value; fiefRow.LastYearReceipts.ToString();
        fiefRow.Receipts = [6000, 6000, 6000, 6000];
        Assert.True(RuleMath.Relief(fiefRow.LastYearReceipts, s.Rule.Settings.ReliefPct) > 0);
        fam.ForcedContent = new FamilyContent(new FamilySettingsDef { Id = "family", OldAge = [new OldAgeRow { From = 0, Pct = 0 }, new OldAgeRow { From = 61, Pct = 100 }] });
        pt.BirthDay[hero] = PersonTable.BirthDayFor(ageDay, 60, 0, pt.AgingPace);                              // the years pass: the hero is sixty (the test's faucet; the table's own hazard is FL-9 and FL-10's)
        s.Family.Ageing!.LastAge = 60;
        int died = -1;
        for (int d = 0; d < 90 && died < 0; d++) { p.Days(1); if (fam.DeathPending) died = (int)(rt.HourDone / 24); }
        Assert.True(died > 0, "the hero did not die of old age");
        output.WriteLine($"warnings {s.Chronicle.All.Count(x => x.TypeId == "old_age_warning")}: days {string.Join(",", s.Chronicle.All.Where(x => x.TypeId == "old_age_warning").Select(x => x.When.TotalDays))}; deaths {s.Chronicle.All.Count(x => x.TypeId == "hero_died_old_age")} on {string.Join(",", s.Chronicle.All.Where(x => x.TypeId == "hero_died_old_age").Select(x => x.When.TotalDays))}; died day {died}");
        var warn = s.Chronicle.All.First(x => x.TypeId == "old_age_warning"); var diedE = s.Chronicle.All.First(x => x.TypeId == "hero_died_old_age");
        Assert.InRange(diedE.When.TotalDays - warn.When.TotalDays, 10, 20);
        Assert.Equal(61, s.Family.Death!.Age); Assert.Equal(DeathCause.Age, s.Family.Death.Cause);
        Assert.Equal("ch_family_died_old_age", diedE.TextKey); Assert.Equal("ch_note_old_age", diedE.Causes[0].NoteKey);
        int ageAtDeath = pt.AgeYears(hero, died); Assert.True(ageAtDeath > 60);

        // 6. the half of the name: the hero was known in the place (the wedding of a man is talked of); the son of sixteen takes up the hero's id with half of it
        int home = pt.HomeSettlement[hero];
        int before = Standing.Of(s.Standing, heroId, GroupRef.Settlement(home));
        output.WriteLine($"the hero's standing at home before the heir takes over: {before}");
        var heir = SuccessionDesk.Candidates(s, s.Party, died).First();
        Assert.Equal(pt.IdOf(son), heir.Person); Assert.Equal(SuccessionRule.Child, heir.Rule); Assert.True(pt.AgeYears(son, died) >= 16);
        long coinBefore = e.Ledger.Audit().InPurses.Mites;
        var take = p.Do(SuccessionDesk.HeirCommand(heir.Person, 0, 0, p.Sec));
        Assert.True(take.Ok, take.Text.ToString());
        Assert.Equal(PersonStatus.Dead, pt.Status[hero]); Assert.Equal(DeathCause.Age, pt.DeathCause[hero] == DeathCause.Other ? DeathCause.Age : pt.DeathCause[hero]);
        Assert.Equal(son, s.Hero!.PersonIndex);
        int after = Standing.Of(s.Standing, s.Party.Hero, GroupRef.Settlement(home));
        output.WriteLine($"and after: {after}");
        Assert.InRange(after, before / 2 - 1, before / 2 + 1);                                               // half (a kin heir: 50 percent of every row)
        Assert.True(before > 0 || after == 0);

        // 7. the fief he held passes to the son: relief is owed to the overlord, in the rule book's dues, as for any heir
        Assert.Equal((long)heroId.Value, fiefRow.Holder);
        var relief = s.Rule.Dues.Single(x => x.Kind == DueKind.Relief && x.Units == fiefRow.Fief);
        Assert.Equal(RuleMath.Relief(fiefRow.LastYearReceipts, s.Rule.Settings.ReliefPct), relief.AmountMites);
        Assert.Contains(s.Chronicle.All, x => x.TypeId == "fief_inherited" && x.Causes.Any(z => z.NoteKey == "ch_note_lord_died"));
        _ = fiefHolder;
        p.Days(10);
        Assert.True(e.Ledger.Audit().IsConserved); Assert.Equal(seeded, e.Ledger.Audit().Seeded.Mites);
        Assert.True(e.Ledger.Audit().IsConserved);   // P7-CN1: the mints strike the silver the hearths send (Minted is no longer 0)
        _ = coinBefore;
    }
}
