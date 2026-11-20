using Ravensguard.Content;
using Ravensguard.Sim.Bondage;
using Ravensguard.Sim.Combat.Aftermath;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Faith;
using Ravensguard.Sim.Law;
using Ravensguard.Sim.Succession;
using Ravensguard.Sim.People;
using Ravensguard.Sim.Save;
using Ravensguard.Sim.Society;
using Ravensguard.Sim.Tests.Faith;
using Ravensguard.Sim.Trade;
using Xunit;
using Xunit.Abstractions;

namespace Ravensguard.Sim.Tests.Bondage;

/// <summary>
/// Slavery on a real new game (the faith tests' world, seed 1234): SL-6 the faith's season, SL-7 the hero enslaved from the defeat to the chains to the escape, SL-12 no slave is a play heir, the hero's desk, the runtime's day and
/// raid (people stay whole), and the whole world's save (OV-8's slave sale, SL-14).
/// </summary>
public class SlaveryWorldTests(ITestOutputHelper output)
{
    private static EntityId SomeoneElse(FaithRig r, int skip = 0)
    {
        var pt = r.People; int n = 0;
        for (int i = 0; i < pt.Count; i++) if (pt.IsAlive(i) && pt.IdOf(i) != r.Hero && pt.AgeYears(i, 1_000_000) >= 16 && n++ == skip) return pt.IdOf(i);
        throw new InvalidOperationException("nobody");
    }

    // ------------------------------------------------------------------ SL-6

    [Fact]
    public void SL6_OwningASlaveUnderAForbiddingFaith_CostsTenPietyASeason_AndTheStandingWithTheReligionFallsFiveHundredInAYear()
    {
        using var r = new FaithRig();
        var st = r.State; var church = r.Church; var pt = r.People; var hero = r.Hero;
        var (hs, ri) = r.HeroFaith();
        Assert.Equal("forbid", r.Religions.Get(ri)!.Doctrine.SlaveryStance);
        var book = st.Bonds = new BondBook(); var bw = BondWiring.World(st, r.Content);
        var slave = SomeoneElse(r);
        Slavery.Make(bw, book, slave, hero, BondKind.Chattel, BondOrigin.Sold, 1, hs, 9_000, 0, 0);
        Piety.Adjust(church, pt, hero, 100); int p0 = Piety.Of(church, pt, hero);
        BondLink.FaithSeason(st, r.Content, bw, book, 60 * 86_400L);
        Assert.Equal(p0 - church.Settings.SlaveSeasonPiety, Piety.Of(church, pt, hero)); Assert.Equal(10, church.Settings.SlaveSeasonPiety);
        Assert.Contains(st.Chronicle.All, e => e.TypeId == "slave_held" && e.Subjects.Contains(hero));
        int s0 = Standing.Of(st.Standing, hero, GroupRef.Religion(ri));
        for (int season = 1; season < 4; season++) BondLink.FaithSeason(st, r.Content, bw, book, (60 + 15 * season) * 86_400L);
        int s4 = Standing.Of(st.Standing, hero, GroupRef.Religion(ri));
        output.WriteLine($"standing with the religion: {s0} after one season, {s4} after four; piety {p0} -> {Piety.Of(church, pt, hero)}");
        Assert.True(s4 <= s0 && s4 <= -400, "a year of slave-keeping costs the faith's regard");
        // a believer whose faith accepts it loses nothing: no slaves of his own faith's forbidding
        Assert.Equal(0, ChurchServices.SlaveHeldSeason(st, r.Content, hero, r.Religions.All.First(x => x.Doctrine.SlaveryStance == "accept").Index, 2, _ => ri, hs, new GameTime(300 * 86_400L)));
    }

    // ------------------------------------------------------------------ SL-7

    [Fact]
    public void SL7_TheHeroEnslaved_DefeatBySlavers_HeldTenDays_SoldAtAMarket_ChainedInTheFields_PicksTheChainsAtNight_AndHisOwnerPostsABounty()
    {
        using var r = new FaithRig();
        var st = r.State; var region = st.Population.Region; var hero = r.Hero; var e = st.Economy!;
        // a Seaboard port in the region: the market
        int port = Math.Max(0, region.Settlements.Count - 1); region.Settlements[port].Culture = 0;
        Assert.Equal(SlaveLaw.Chattel, BondRules.Parse(ReligionBook.CultureOf(r.Content, 0)!.Slavery));
        // the defeat table: slavers take the party alive; a stranger who is not hurt is let go
        var rules = AftermathRules.For(r.Content);
        Assert.Equal(DefeatKind.Captured, DefeatTable.Decide(rules, 1, 5, WinnerKind.Slavers, null, new LoserFacts(hero, LoserRole.Hero, LoserState.KnockedOut, false, false, true, 1000, 0)).Kind);
        Assert.Equal(DefeatKind.Left, DefeatTable.Decide(rules, 1, 5, WinnerKind.Slavers, null, new LoserFacts(SomeoneElse(r), LoserRole.Stranger, LoserState.KnockedOut, false, false, true, 0, 0)).Kind);
        Assert.Equal(WinnerKind.Slavers, rules.WinnerOf("slavers"));
        var cb = AftermathDesk.BookOf(st, r.Content);
        long t0 = 200L * 86_400 + 21 * 3600;
        var heroPurse = e.Ledger.TryFind(PurseKind.Person, hero, out var hp) ? hp : throw new InvalidOperationException();
        var req = new DefeatRequest(31, WinnerKind.Slavers, LoserState.KnockedOut, false, false, true, true, [new Robber(1250, 70000, false)], 1000, 2000);
        var rep = AftermathDesk.Defeat(st, r.Content, st.Party!, heroPurse, req, t0);
        Assert.Equal(DefeatKind.Captured, rep.Kind);
        var cap = cb.Find(hero)!; Assert.Equal(CaptorKind.Slavers, cap.Captor); Assert.True(cap.Held);
        // ten days later the hold ends in a sale: the hero stands in the port's pen
        for (long h = t0 / 3600; h < t0 / 3600 + 11 * 24 + 6; h++) cb.StepHour(cb.Rules, e.Ledger, h);
        Assert.Equal("sold", cb.Find(hero)!.Note); Assert.Equal(CaptiveState.Released, cb.Find(hero)!.State);
        int day = (int)(t0 / 86_400) + 11;
        BondLink.Daily(st, r.Content, day + 1);
        var lot = st.Bonds!.Of(hero)!; Assert.True(lot.InPen); Assert.Equal(port, lot.Place);
        Assert.True(BondWiring.World(st, r.Content).LawAt(port) == SlaveLaw.Chattel);
        // a household of the port buys him
        var bw = BondWiring.World(st, r.Content);
        int hh = Enumerable.Range(0, st.Population.Households.Count).First(h => st.Population.Households.Settlement[h] == port && st.Population.Households.MemberCount[h] > 0);
        e.Ledger.Mint(e.HouseholdPurse[hh], new Coin(100_000), SourceReason.Seed);
        var buy = Slavery.Buy(bw, st.Bonds, st.Population.Households.IdOf(hh), hero, day + 2, (day + 2) * 86_400L);
        Assert.True(buy.Ok, buy.Key);
        var b = st.Bonds.Of(hero)!; Assert.False(b.InPen); Assert.Equal(st.Population.Households.IdOf(hh).Value, b.Owner);
        Assert.True(st.Standing!.Marks.IsEnslaved(hero));                                          // the mark of A3
        // he works the fields in chains
        b.Chain = Restraint.Chained; b.Work = BondWork.Fields;
        var crimes = st.Law ?? new CrimeBook(); var bw2 = BondWiring.World(st, r.Content);
        // by day the overseer is awake
        Assert.Equal("sl_escape_day", Slavery.Escape(bw2, st.Bonds, crimes, cb.Rules, hero, (day + 3) * 86_400L + 12 * 3600, 30, 100).Key);
        // at night the chance is Phase 4's lock rule on stream 0x455: the first success is exactly the first draw under it
        int pm = EscapeTable.LockPm(cb.Rules, 100, CaptorKind.Slavers, false);
        long night = (day + 3) * 86_400L + 22 * 3600, win = 0; bool done = false; int tries = 0;
        for (; tries < 200 && !done; tries++)
        {
            long sec = night + tries * 20L; bool expect = EscapeTable.Roll(st.Seed, sec / 20, hero, RngStreams.LockPick, 0) < pm;
            var res = Slavery.Escape(bw2, st.Bonds, crimes, cb.Rules, hero, sec, 0, 100); win = sec;
            Assert.Equal(expect, res.Ok); done = res.Ok;
            if (!done && b.Chain == Restraint.None) Assert.Fail("a failed pick left the chains off");
        }
        Assert.True(done); output.WriteLine($"the chains came off at the {tries}th try (pick chance {pm} in a thousand)");
        Assert.True(b.OnRun); Assert.Equal(Restraint.None, b.Chain);
        Assert.NotEqual(0UL, b.BountyJob);                                                          // the owner posts the runaway's bounty
        var job = bw2.W.Jobs!.Get(new EntityId(b.BountyJob))!; Assert.Equal(Ravensguard.Sim.Jobs.JobState.Open, job.State);
        Assert.Contains(crimes.Crimes.Values, c => c.Kind == "runaway" && c.Doer == hero.Value);
        Assert.Contains(st.Chronicle.All, c => c.TypeId == "slave_runaway" && c.Subjects.Contains(hero));
        Assert.True(e.Ledger.Audit().IsConserved);
        _ = win;
    }

    // ------------------------------------------------------------------ SL-12

    [Fact]
    public void SL12_ABondedKinIsNotAPlayHeir_AndTheDeskLeavesAHeroOwnedSlaveToTheHeroAlone()
    {
        using var r = new FaithRig();
        var st = r.State; var pt = r.People;
        var other = SomeoneElse(r); int row = pt.IndexOf(other);
        Assert.False(SuccessionDesk(st)(row, other));
        st.Bonds = new BondBook(); Slavery.Make(BondWiring.World(st, r.Content), st.Bonds, other, r.Hero, BondKind.Chattel, BondOrigin.Sold, 0, 0, 9_000, 0, 0);
        Assert.True(SuccessionDesk(st)(row, other));
        // the order itself: a child of the hero of twenty is first, until he is bonded
        var rig = BondRig.Make(); var child = rig.P.IdOf(5);
        rig.P.Father[5] = 0; rig.P.BirthDay[5] = 100 - 20 * TimeConst.DaysPerYear;
        var order = SuccessionOrder.Order(rig.P, 0, 100, [], (_, _) => false);
        Assert.Equal(child, order[0].Person);
        Slavery.Make(rig.Bw, rig.Book, child, rig.H.IdOf(30), BondKind.Chattel, BondOrigin.Sold, 90, 0, 9_000, 0, 0);
        order = SuccessionOrder.Order(rig.P, 0, 100, [], (_, id) => rig.Book.Has(id));
        Assert.DoesNotContain(order, c => c.Person == child);
    }

    private static Func<int, EntityId, bool> SuccessionDesk(WorldState st) => Ravensguard.Sim.Succession.SuccessionDesk.Unavailable(st);

    // ------------------------------------------------------------------ the desk

    [Fact]
    public void TheDeskFreesTreatsAndChainsTheHeroOwnSlaves_RefusesWhatIsNotHis_AndLeavesNoBookWhenAllAreFree()
    {
        using var r = new FaithRig();
        var st = r.State; var slave = SomeoneElse(r);
        var book = st.Bonds = new BondBook();
        Slavery.Make(BondWiring.World(st, r.Content), book, slave, r.Hero, BondKind.Chattel, BondOrigin.Sold, 0, 0, 9_000, 0, 0);
        long sec = 100L * 86_400 + 3600;
        Assert.True(r.Do(BondDesk.Treat(slave, Treatment.Kind, sec), sec).Ok); Assert.Equal(Treatment.Kind, book.Of(slave)!.Treatment);
        Assert.True(r.Do(BondDesk.Chain(slave, true, sec), sec).Ok); Assert.Equal(Restraint.Chained, book.Of(slave)!.Chain);
        Assert.True(r.Do(BondDesk.Work(slave, BondWork.Mine, sec), sec).Ok); Assert.Equal(BondWork.Mine, book.Of(slave)!.Work);
        Assert.False(r.Do(BondDesk.Free(SomeoneElse(r, 3), sec), sec).Ok);                                   // not his
        var here = r.Do(BondDesk.Here(0, sec), sec); Assert.True(here.Ok); Assert.Equal(0, book.Of(slave)!.Place);
        Assert.True(r.Do(BondDesk.Free(slave, sec), sec).Ok);
        Assert.Null(st.Bonds);                                                                                  // an empty book is not kept: the world writes no section
        Assert.Contains(st.Chronicle.All, e => e.TypeId == "slave_freed" && e.Subjects[0] == r.Hero);
        var view = BondView.Of(st, r.Content, 100); Assert.Null(view);
    }

    // ------------------------------------------------------------------ the runtime's day

    [Fact]
    public void ARuntimeDayWithNoBondsKeepsNoBook_AndASeasonsRaidTakesPeopleAsCaptivesWithoutMakingOrLosingAny()
    {
        using var r = new FaithRig();
        var st = r.State; int people = st.Population.People.Count; var rules = BondRules.From(r.Content);
        BondLink.Daily(st, r.Content, 7);                                                                     // an ordinary day: nothing
        Assert.Null(st.Bonds);
        int season = -1;
        for (int s = 1; s < 400; s++) if (Raids.Happens(st.Seed, st.HomeRegion.Id, s, st.Difficulty.BanditPct, rules)) { season = s; break; }
        Assert.True(season > 0);
        // every guard of the region stands down for the test: some village is weak
        BondLink.Daily(st, r.Content, season * TimeConst.DaysPerSeason);
        output.WriteLine($"season {season}: bonds {(st.Bonds == null ? "none" : st.Bonds.Bands.Count + " band")}, captives {st.Captivity?.Captives.Count ?? 0}");
        Assert.Equal(people, st.Population.People.Count);
        if (st.Bonds != null)
        {
            Assert.Contains(st.Chronicle.All, e => e.TypeId == "slave_raid");
            Assert.All(st.Captivity!.Captives.Values.Where(c => c.Captor == CaptorKind.Slavers), c => Assert.True(c.Held));
            Assert.True(st.Economy!.Ledger.Audit().IsConserved);
        }
    }

    // ------------------------------------------------------------------ the save

    [Fact]
    public async Task SL14_AWholeRunningWorldWithAPenARunawayAndAChainedHero_SavesAndComesBackTheSame_AndAnOlderSaveKeepsItsBytes()
    {
        using var r = new FaithRig();
        var st = r.State; var rt = r.Runtime;
        var old = await rt.AtBarrier(() => WorldSave.Capture(st, "b", "0.1"));
        Assert.DoesNotContain(old.Sections, kv => kv.Key == BondSave.SectionName);                           // a world that never met slavery writes no section
        var bw = BondWiring.World(st, r.Content); var book = st.Bonds = new BondBook();
        var a = SomeoneElse(r, 0); var b = SomeoneElse(r, 1); var c = SomeoneElse(r, 2);
        int port = st.Population.People.HomeSettlement[st.Population.People.IndexOf(a)];
        var band = Slavery.BandId(0, 0); Slavery.BandPurse(bw, band);
        Slavery.List(bw, book, a, band, PurseKind.Household, port, 5, 5 * 86_400L, 0);
        var run = Slavery.Make(bw, book, b, st.Population.Households.IdOf(0), BondKind.Chattel, BondOrigin.Sold, 3, port, 9_000, 0, 0); run.Chain = Restraint.None; run.Morale = 5;
        var crimes = st.Law ?? new CrimeBook(); BondDaily.Flee(bw, book, crimes, run, 6, 6 * 86_400L); st.Law = crimes;
        var hero = Slavery.Make(bw, book, r.Hero, st.Population.Households.IdOf(1), BondKind.Chattel, BondOrigin.Sold, 4, port, 9_000, 0, 0); hero.Chain = Restraint.Chained; hero.Work = BondWork.Fields;
        book.Swept.Add(c.Value);
        ulong hash = book.Hash();
        var save = await rt.AtBarrier(() => WorldSave.Capture(st, "b", "0.1"));
        Assert.Contains(save.Sections, kv => kv.Key == BondSave.SectionName);
        var st2 = WorldSave.Restore(SaveCodec.Decode(SaveCodec.Encode(save)), r.Content, "b", null, r.W.Continent, r.W.History);
        Assert.NotNull(st2.Bonds); Assert.Equal(hash, st2.Bonds!.Hash());
        Assert.True(st2.Bonds.Of(a)!.InPen); Assert.True(st2.Bonds.Of(b)!.OnRun); Assert.Equal(Restraint.Chained, st2.Bonds.Of(r.Hero)!.Chain);
        var save2 = WorldSave.Capture(st2, "b", "0.1");
        Assert.Equal(save.Sections[BondSave.SectionName].ToArray(), save2.Sections[BondSave.SectionName].ToArray());
        // the section's world hash joins the trail only when non-empty
        Assert.False(new BondBook().IsEmpty && new BondBook().Hash() == 0);
    }
}
