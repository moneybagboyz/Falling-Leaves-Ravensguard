using Ravensguard.Sim.Combat.Aftermath;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Dialogue;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Law;
using Ravensguard.Sim.Politics;
using Ravensguard.Sim.Rule;
using Ravensguard.Sim.Rumours;
using Ravensguard.Sim.Save;
using Ravensguard.Sim.Worlds;
using Xunit;
using Xunit.Abstractions;

namespace Ravensguard.Sim.Tests.Politics;

/// <summary>P7-0B: the Phase 6 debts that touch coin, the realms and the runtime (S14, S11, G1, rulers' old age, the Plunder reason). S10 is in JusticeTests, S13 in TaxTests.</summary>
public class P70BDebtsTests(ITestOutputHelper output)
{
    // ------------------------------------------------------------------ S14

    [Fact]
    public void EA_F3_S14_TributeBetweenTheHomeRealmAndAnAbstractOne_MovesRealCoin_BothWays_AndConservesIt()
    {
        using var r = new PoliticsRig(); var c = r.C();
        int x = r.Fief().Realm;
        int y = r.C().AliveRealms().Where(k => k != x && r.C().R.PurseOf(Party.OfRealm(k)).IsNone).DefaultIfEmpty(-1).First();
        output.WriteLine($"home realm {x}, abstract realm {y}"); Assert.True(y >= 0, "every live realm keeps a purse here");
        var px = c.R.PurseOf(Party.OfRealm(x)); Assert.False(px.IsNone); Assert.True(c.R.PurseOf(Party.OfRealm(y)).IsNone);   // the home realm keeps a purse, the other is abstract
        r.Fund(c, Party.OfRealm(x), 10_000);
        var led = r.State.Economy!.Ledger; var holding = Diplomacy.AbstractHolding(c);
        long homeBefore = led.Balance(px).Mites, holdBefore = led.Balance(holding).Mites, totalBefore = led.Audit().InPurses.Mites;
        // the home realm owes tribute to the abstract one: the real treasury pays
        var owes = new Pact { Serial = 1, Kind = PactKind.Truce, A = x, B = y, TributeFrom = x, TributeMites = 500, TributeUntilDay = c.Day + 90 };
        Assert.Equal(500, Diplomacy.PayTribute(c, owes));
        Assert.Equal(homeBefore - 500, led.Balance(px).Mites);                                     // fails before P7-0B: the treasury paid nothing
        Assert.Equal(holdBefore + 500, led.Balance(holding).Mites);
        Assert.Equal(500, r.B.RealmOf(y).TreasuryDelta);
        // the abstract realm owes it back: real coin comes in, up to what the holding purse has
        var back = new Pact { Serial = 2, Kind = PactKind.Truce, A = x, B = y, TributeFrom = y, TributeMites = 800, TributeUntilDay = c.Day + 90 };
        Assert.Equal(500, Diplomacy.PayTribute(c, back));
        Assert.Equal(homeBefore, led.Balance(px).Mites); Assert.Equal(holdBefore, led.Balance(holding).Mites);
        Assert.Equal(0, r.B.RealmOf(y).TreasuryDelta);
        Assert.Equal(totalBefore, led.Audit().InPurses.Mites); Assert.True(led.Audit().IsConserved);
        Assert.True(led.Flow(TransferReason.Tribute).Mites >= 1000);
    }

    // ------------------------------------------------------------------ S11

    [Fact]
    public void EA_S11_TheLawsOpinionOfAPerson_IsTheTalkBooksTrust()
    {
        using var r = new PoliticsRig(); var st = r.State;
        var people = st.Population.People; var a = people.IdOf(5); var b = people.IdOf(6); var other = people.IdOf(7);
        var talk = st.Talk ??= new TalkState();
        talk.Relations.Met(5, 6, 1, new Ravensguard.Content.TalkSettingsDef { Id = "talk" }, trustDelta: 650);
        var w = LawWorld.From(st, r.R.Content);
        Assert.Equal(650, w.OpinionPm(a, b)); Assert.Equal(650, w.OpinionPm(b, a));                // fails before P7-0B: always 0
        Assert.Equal(0, w.OpinionPm(a, other));
        talk.Relations.Met(5, 7, 1, new Ravensguard.Content.TalkSettingsDef { Id = "talk" }, trustDelta: -900);
        Assert.True(LawWorld.From(st, r.R.Content).OpinionPm(a, other) <= w.S.HarshOpinionPm);        // a hated man is sentenced harshly (Sentences.cs)
    }

    // ------------------------------------------------------------------ G1

    [Fact]
    public void EA_G1_TheRuntimeAttachesTheHourlyLinksItself_InOneOrder_AndOnlyOnce()
    {
        using var r = new Ravensguard.Sim.Tests.Rule.RuleWorldRig();
        var rt = r.Runtime; Assert.NotNull(rt.Phase6);                                              // fails before P7-0B: only the street view attached them
        Assert.Same(rt.Phase6, Phase6Links.AttachAll(rt, r.Content));
        Assert.Same(rt.Phase6!.Talk, TalkLink.Attach(rt));                                           // a second Attach by hand is the same link: no hour runs twice
        Assert.Same(rt.Phase6.Family, Ravensguard.Sim.Family.FamilyLink.Attach(rt));
        rt.AdvanceTo(GameTime.FromGameHours(rt.HourDone + 1 + 24 * 40));
        Assert.NotNull(r.State.Law);                                                                 // a headless run now sees the courts' book
        Assert.True(r.State.Chronicle.All.Any(e => e.TypeId is "crime_reported" or "crime_committed" or "work_posted"), "no crime or contract entry in forty days");
        using var bare = new Ravensguard.Sim.Tests.Rule.RuleWorldRig(runtime: false);
        using var rt2 = new WorldRuntime(bare.State, bare.Content, new Ravensguard.Sim.Worlds.MemoryRegionPager(), phase6Links: false);
        Assert.Null(rt2.Phase6);
    }

    // ------------------------------------------------------------------ the Plunder reason

    [Fact]
    public void AftermathCoinPlunder_IsItsOwnReason_NoLongerFine()
    {
        Assert.Equal(TransferReason.Plunder, AftermathCoin.Plunder);
        Assert.NotEqual(TransferReason.Fine, AftermathCoin.Plunder);
    }

    // ------------------------------------------------------------------ rulers' old age

    [Fact]
    public void EA_RulersGrowOldInPlay_TheCrownPassesByTheHistorysHeirOrder_DeterministicallyAndSaved()
    {
        (string Hash, int Deaths, byte[] Bytes) Run()
        {
            using var r = new PoliticsRig();             var rulers0 = r.C().AliveRealms().ToDictionary(k => k, k => r.C().RulerOf(k));
            int day0 = PoliticsRig.NextSeasonStart(r.Day);
            for (int s = 0; s < 4 * 70; s++) RulerSuccession.Season(r.C(day0 + s * TimeConst.DaysPerSeason));      // seventy years of seasons
            int deaths = r.B.RulerDeaths.Count;
            foreach (var e in r.Entries("realm_ruler_died")) Assert.Equal(CauseKind.Death, e.Causes[0].Kind);
            var succ = r.Entries("realm_succession").ToList(); Assert.Equal(deaths, succ.Count);
            foreach (var e in succ) Assert.Equal(CauseKind.Death, e.Causes[0].Kind);
            foreach (var (k, was) in rulers0) if (r.B.RulerDeaths.ContainsKey(was)) Assert.NotEqual(was, r.C().RulerOf(k));   // the heir rules now
            var bytes = SaveCodec.SerializeCompressed(PoliticsSave.Capture(r.B));
            var back = SaveCodec.DeserializeCompressed<PoliticsSave>(bytes).Restore();
            Assert.Equal(r.B.Hash(), back.Hash()); Assert.Equal(bytes, SaveCodec.SerializeCompressed(PoliticsSave.Capture(back)));
            return (r.B.Hash().ToString("X"), deaths, bytes);
        }
        var a = Run(); var b = Run();
        output.WriteLine($"{a.Deaths} rulers died in seventy years");
        Assert.True(a.Deaths > 0, "no realm ruler died of age in seventy years");
        Assert.Equal(a.Hash, b.Hash); Assert.Equal(a.Bytes, b.Bytes);
    }

    [Fact]
    public void ASaveWithNoRulerDeaths_KeepsItsCountersAsTheyWere()
    {
        var book = new PoliticsBook { Imported = true };
        Assert.Equal(7, PoliticsSave.Capture(book).Counters.Length);
        book.RulerDeaths[42] = 10;
        Assert.Equal(9, PoliticsSave.Capture(book).Counters.Length);
        Assert.Equal(10, PoliticsSave.Capture(book).Restore().RulerDeaths[42]);
    }
}
