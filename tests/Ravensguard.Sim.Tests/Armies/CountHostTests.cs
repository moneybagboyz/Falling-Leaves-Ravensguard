using Ravensguard.Content;
using Ravensguard.Sim.Abstract;
using Ravensguard.Sim.Armies;
using Ravensguard.Sim.Combat.Abstract;
using Ravensguard.Sim.Combat.Wounds;
using Ravensguard.Sim.Continental;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.People;
using Ravensguard.Sim.People.Vital;
using Ravensguard.Sim.Save;
using Ravensguard.Sim.Tests.Combat.Melee2;
using Ravensguard.Sim.WarSupply;
using Ravensguard.Sim.WorldGen.Region;
using Xunit;
using Xunit.Abstractions;

namespace Ravensguard.Sim.Tests.Armies;

/// <summary>
/// P7-AW-D gate, the continent half: AW-11 (count hosts march, forage, fight and besiege across six summary regions, and grain, people and coin add up exactly), AW-12 (a count host
/// of 800 enters a detailed region, fights a v2 battle and the survivors leave: counts and persons exact on both sides), the count battle's own conservation and the saved book.
/// </summary>
[Collection("fight")]
public class CountHostTests(FightFixture fx, ITestOutputHelper output)
{
    private CountBattleDef Def => fx.Db.All<CountBattleDef>().OrderBy(d => d.Id, StringComparer.Ordinal).First();

    /// <summary>Six summary regions in a chain (0 to 5), 0 to 2 of realm 1 and 3 to 5 of realm 2, each with people, grain and coin.</summary>
    private static ContinentState Chain(long grainDays = 40)
    {
        var st = new ContinentState(77UL, 63);
        var edges = new List<ContinentEdge>();
        for (int i = 0; i < 6; i++)
        {
            var s = st.Summaries[i];
            s.Detailed = false; s.Realm = (byte)(i < 3 ? 1 : 2); s.Pop = 20_000 + i * 500;
            s.StockG[0] = (long)s.Pop * CountRules.GramsPerManDay * grainDays;
            s.CoinTotal = 9_000_000 + i * 1000; s.TreasuryCoin = 500_000 + i;
            if (i > 0) edges.Add(new ContinentEdge { A = i - 1, B = i, Gates = 1, Class = RoadClass.Road });
        }
        st.SetEdges(edges);
        return st;
    }

    private static long Coin(ContinentState st) { long c = 0; foreach (var s in st.Summaries) c += s.CoinTotal + s.TreasuryCoin; return c; }

    private (ContinentState St, ContinentArmies Ar, ArmyBook Book) Scenario(long grainDays = 40)
    {
        var st = Chain(grainDays); var book = new ArmyBook(); var ar = new ContinentArmies(book, Def);
        // realm 1 marches from region 1 on region 4; realm 2 stands in region 3 to stop it; a smaller raid of realm 1 goes for region 5 by the same road
        Assert.NotNull(ar.Raise(st, 1, 1, [200, 150, 60, 20, 70], foe: 2, war: 1, objective: 4, day: 0));
        Assert.NotNull(ar.Raise(st, 2, 4, [150, 120, 50, 10, 40], foe: 1, war: 1, objective: 3, day: 0));
        Assert.NotNull(ar.Raise(st, 1, 0, [100, 60, 20, 0, 20], foe: 2, war: 1, objective: 5, day: 0));
        return (st, ar, book);
    }

    [Fact]
    public void AW11_ASeasonOfCountHosts_MarchingForagingFightingAndBesieging_ConservesGrainPeopleAndCoin()
    {
        var (st, ar, book) = Scenario(grainDays: 14);
        var (people0, grain0) = ar.Totals(st); long coin0 = Coin(st);
        for (int day = 0; day < 4 * TimeConst.DaysPerSeason; day++)
        {
            ar.Day(st, day);
            var (p, g) = ar.Totals(st);
            Assert.Equal(people0, p);
            Assert.Equal(grain0, g);
            Assert.Equal(coin0, Coin(st));
            foreach (var s in st.Summaries) { Assert.True(s.Pop >= 0 && s.StockG[0] >= 0); }
        }
        var l = book.Count.Ledger;
        output.WriteLine($"battles {l[CountLedger.Battles]}, storms {l[CountLedger.Storms]}, places {l[CountLedger.PlacesTaken]}, dead {l[CountLedger.MenDead]}, crippled {l[CountLedger.MenCrippled]}, fled {l[CountLedger.MenFled]}, deserted {l[CountLedger.MenDeserted]}, " +
            $"eaten {l[CountLedger.GrainEaten] / 1000} kg, foraged {l[CountLedger.GrainForaged] / 1000} kg, wasted {l[CountLedger.GrainWasted] / 1000} kg, siege {l[CountLedger.SiegeEaten] / 1000} kg, events {book.WarEvents.Count}, hosts left {book.Count.Hosts.Count}");
        Assert.True(l[CountLedger.GrainEaten] > 0, "the hosts ate");
        Assert.True(l[CountLedger.GrainForaged] > 0, "a host foraged the enemy's fields");
        Assert.True(l[CountLedger.Battles] >= 1, "hosts of foes met");
        Assert.True(l[CountLedger.MenDead] > 0);
        Assert.True(l[CountLedger.SiegeEaten] > 0 || l[CountLedger.PlacesTaken] > 0, "a town was besieged");
        Assert.NotEmpty(book.WarEvents);
    }

    [Fact]
    public void P7R5_ATownAtPopZeroIsNotStormed_AndPeopleStayConserved()
    {
        var (st, ar, book) = Scenario(grainDays: 400);
        for (int day = 0; day < 4 * TimeConst.DaysPerSeason; day++)
        {
            for (int i = 3; i < 6; i++) st.Summaries[i].Pop = 0;                   // the towns are empty every morning
            var (before, _) = ar.Totals(st);
            ar.Day(st, day);
            Assert.Equal(before, ar.Totals(st).Item1);
            for (int i = 3; i < 6; i++) Assert.True(st.Summaries[i].Pop >= 0);
        }
        Assert.Equal(0, book.Count.Ledger[CountLedger.Storms]);          // an empty town has no levy to storm
    }

    [Fact]
    public void AW11_TheSeasonIsDeterministic_AndTheBookSavesAndLoadsByteForByte()
    {
        ulong Run(out byte[] bytes)
        {
            var (st, ar, book) = Scenario(14);
            for (int day = 0; day < 3 * TimeConst.DaysPerSeason; day++) ar.Day(st, day);
            var save = ArmySave.Capture(book);
            bytes = MessagePack.MessagePackSerializer.Serialize(save);
            var back = MessagePack.MessagePackSerializer.Deserialize<ArmySave>(bytes).Restore();
            Assert.Equal(book.Hash(), back.Hash());
            Assert.Equal(bytes, MessagePack.MessagePackSerializer.Serialize(ArmySave.Capture(back)));
            return book.Hash();
        }
        ulong a = Run(out var b1), b = Run(out var b2);
        Assert.Equal(a, b); Assert.Equal(b1, b2);
    }

    [Fact]
    public void AW11_AnEmptyCountBook_AddsNothingToTheArmyBookHash()
    {
        var book = new ArmyBook();
        Assert.True(book.Count.IsEmpty);
        var ar = new ContinentArmies(book, Def); var st = Chain();
        for (int d = 0; d < 20; d++) ar.Day(st, d);
        Assert.True(book.IsEmpty);                       // no host, no march, no ledger: the section stays absent
    }

    [Fact]
    public void AW11_TheContinentTickRunsTheHosts_ThroughItsHook()
    {
        var (st, ar, book) = Scenario(14);
        st.Armies = ar;
        var (people0, grain0) = ar.Totals(st);
        var now = GameTime.FromCalendar(5, Season.Spring, 2);
        st.Armies!.Season(st, now);
        var (p, g) = ar.Totals(st);
        Assert.Equal(people0, p); Assert.Equal(grain0, g);
        Assert.True(book.Count.Ledger[CountLedger.GrainEaten] > 0);
        Assert.All(book.Count.Hosts.Values, h => Assert.True(h.OnEdge || h.Region >= 0));
    }

    [Fact]
    public void AW11_ACountBattle_AccountsForEveryMan_AndTheSameSeedGivesTheSameBattle()
    {
        var d = Def;
        for (int i = 0; i < 40; i++)
        {
            var spec = CountScenarios.Gate[i];
            var r = CountBattle.Fight(d, 5, (ulong)i, [spec.A, spec.B]);
            Assert.True(r.Conserves(), spec.Name);
            var r2 = CountBattle.Fight(d, 5, (ulong)i, [spec.A, spec.B]);
            Assert.Equal((r.Winner, r.DurationS, r.Casualties(0), r.Casualties(1)), (r2.Winner, r2.DurationS, r2.Casualties(0), r2.Casualties(1)));
        }
    }

    // ---------------------------------------------------------------- AW-12

    private static readonly Roster None = new();

    [Fact]
    public void AW12_ACountHostOf800_EntersADetailedRegion_FightsAV2Battle_AndTheSurvivorsLeave_CountsAndPersonsExactOnBothSides()
    {
        using var r = new MarchRig(); var aw = r.Aw; var pt = aw.People; var w = r.K.R.Sim;
        var h = new CountHost { Id = 1, Realm = 1, Home = 1, Region = 1, Men = [300, 300, 100, 0, 100] };
        Assert.Equal(800, h.Total);
        var host = r.Host(CountHostGate.RosterOf(h), outside: 800);
        long alive0 = w.Vital.Alive, rows0 = pt.Count, start = 700 * 24 + MarchConfig.DayStartHour;
        Assert.Equal(800, CountHostGate.Enter(aw, r.Env(start), h, host, -1));
        Assert.Equal(CountState.InDetail, h.State);
        Assert.Equal(rows0 + 800, pt.Count); Assert.Equal(alive0 + 800, w.Vital.Alive);
        var ids = aw.ColumnPersons(host); Assert.Equal(800, ids.Count);
        var roleOf = new Dictionary<ulong, byte>(aw.Book.Marches[host.Value].Outsiders);
        // the v2 battle: the host's persons against 600 of the other realm's men
        var spec = new CountSpec("gate", h.Men, [150, 250, 100, 0, 100]);
        var input = CountScenarios.InputOf(spec, 31);
        var kits = new List<Ravensguard.Sim.Combat.Melee2.FighterKit>(input.Kits);
        int at = 0;
        for (int i = 0; i < kits.Count; i++) if (kits[i].Team == 0) kits[i] = kits[i] with { Person = ids[at++] };
        Assert.Equal(800, at);
        var b = new AbstractBattle(fx.C, WoundTables.For(fx.Db)!, input with { Kits = kits }, AbstractBattle.V2Def(fx.Db));
        b.Run();
        var dead = b.Soldiers.Where(s => s.Side == 0 && s.Status == SoldierStatus.Dead).Select(s => s.Person).ToList();
        var ob = new RegionOutbox(w.Ids);
        foreach (var id in dead) Demography.Kill(w, aw.RowOf(id), DeathCause.Violence, new GameTime(start * 3600L + 5000), ob, note: "ch_why_violence");
        int left = CountHostGate.Leave(aw, r.Env(start + 2), h, host, 2);
        output.WriteLine($"v2 battle: {dead.Count} of 800 dead, {left} leave; the count host has {h.Total}: {string.Join("/", h.Men)}");
        Assert.True(dead.Count > 0);
        Assert.Equal(800 - dead.Count, left);
        Assert.Equal(left, h.Total);                                                           // counts exact
        Assert.Equal(dead.Count, ids.Count(id => pt.Status[aw.RowOf(id)] == PersonStatus.Dead));
        Assert.Equal(left, ids.Count(id => pt.Status[aw.RowOf(id)] == PersonStatus.Departed)); // persons exact
        Assert.Equal(alive0, w.Vital.Alive);
        Assert.Equal(dead.Count, (int)aw.Book.Count.Ledger[CountLedger.MenDead]);
        // by class: each class lost exactly the men of its roles who died
        int[] lostBy = new int[CountScenarios.Classes];
        foreach (var id in dead) lostBy[(int)CountHostGate.ClassOf((TroopRole)roleOf[id])]++;
        int[] start0 = [300, 300, 100, 0, 100];
        for (int c = 0; c < CountScenarios.Classes; c++) Assert.Equal(start0[c] - lostBy[c], h.Men[c]);
        Assert.Equal(CountState.Standing, h.State); Assert.Equal(2, h.Region);
    }

    [Fact]
    public void AW12_190DieHere_610Leave_AndComeBackAsThemselvesOnTheNextVisit()
    {
        using var r = new MarchRig(); var aw = r.Aw; var pt = aw.People; var w = r.K.R.Sim;
        var h = new CountHost { Id = 1, Realm = 1, Home = 1, Region = 1, Men = [300, 300, 100, 0, 100] };
        var host = r.Host(CountHostGate.RosterOf(h), outside: 800);
        long start = 710 * 24 + MarchConfig.DayStartHour; long alive0 = w.Vital.Alive;
        CountHostGate.Enter(aw, r.Env(start), h, host, -1);
        var ids = aw.ColumnPersons(host);
        var ob = new RegionOutbox(w.Ids);
        for (int i = 0; i < 190; i++) Demography.Kill(w, aw.RowOf(ids[i * 4]), DeathCause.Violence, new GameTime(start * 3600L + 5000), ob, note: "ch_why_violence");
        Assert.Equal(610, CountHostGate.Leave(aw, r.Env(start + 2), h, host, 2));
        Assert.Equal(610, h.Total);
        Assert.Equal(610, r.Hs.Account(host).Total);
        Assert.Equal(190, (int)aw.Book.Count.Ledger[CountLedger.MenDead]);
        Assert.Equal(alive0, w.Vital.Alive);
        int rows = pt.Count;
        Assert.Equal(610, CountHostGate.Enter(aw, r.Env(start + 30), h, host, -1));            // nobody is made again
        Assert.Equal(rows, pt.Count);
    }
}
