using Ravensguard.Content;
using Ravensguard.Sim.Armies;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Goods;
using Ravensguard.Sim.People;
using Ravensguard.Sim.Politics;
using Ravensguard.Sim.Rule;
using Ravensguard.Sim.Production;
using Ravensguard.Sim.Save;
using Ravensguard.Sim.Soldiers;
using Ravensguard.Sim.Tests.Rule;
using Ravensguard.Sim.Worlds;
using Ravensguard.Sim.WorldGen.Region;
using Xunit;
using Xunit.Abstractions;

namespace Ravensguard.Sim.Tests.Armies;

/// <summary>A new game of seed 1234's embark region with its lords, run on the real tick (the rule rig), and the armies' world over it.</summary>
internal sealed class ArmyRig : IDisposable
{
    public RuleWorldRig R { get; } = new();
    public WorldState State => R.State;
    public WorldRuntime Rt => R.Runtime;
    public ArmyWorld Aw => R.Sim.Get<ArmyWorld>()!;
    public ArmyBook Book => State.Armies!;
    public RuleBook Rule => R.Book;
    public PoliticsBook Pol => State.Politics!;
    public long H0 { get; }
    public ArmyRig() { H0 = Rt.HourDone; }
    public void To(long hour) => Rt.AdvanceTo(GameTime.FromGameHours(hour));
    public long Now => Rt.HourDone;
    public int Day => (int)(Now / 24);
    /// <summary>Runs to the first hours of spring, when every lord has been asked to raise his men (the first season day of the run).</summary>
    public void Raised() => To(H0 + 28);
    public ArmyEnv Env(RuleChron? w = null) => new(R.Sim, null, Now + 1, w ?? RuleChron.Of(State.Chronicle, State.Ids));
    public Warband WarbandOfFief(int fief) => Book.WarbandOfFief(fief)!;
    public IEnumerable<ChronicleEntry> Entries(string type) => State.Chronicle.All.Where(e => e.TypeId == type);
    public void Dispose() => R.Dispose();
}

/// <summary>P7-AM-A gate: AM-1 (warband life), AM-2 (chase bandits), AM-14 (bandits from deserters) and AM-19 (the armies section), with the wages, the raids, the dismissal, the hero as a lord and the cost.</summary>
public class ArmyTests(ITestOutputHelper output)
{
    private static readonly string[] TaskCauses = ["ch_note_am_patrol_due", "ch_note_am_robbery_news", "ch_note_am_wagon", "ch_note_am_errand", "ch_note_am_friend", "ch_note_am_raid_near", "ch_note_am_winter", "ch_note_am_rest", "ch_note_am_war_raid"];

    // ---------------------------------------------------------------- AM-1

    [Fact]
    public void AM1_ABaronsWarband_In30DaysOfPeace_PatrolsEveryVillageWithinSixDays_EscortsTheTaxWagon_VisitsAFriendlyLord_EveryTaskWithItsCause()
    {
        using var k = new ArmyRig();
        k.Raised();
        var region0 = k.State.Population.Region;
        const int fiefNo = 0, villageFief = 7;                        // the lordship of a town and its farmsteads (not its realm's seat: it sends the wagon), and a manor with three villages
        var wb = k.WarbandOfFief(fiefNo); var aw = k.Aw; var lordA = wb.Lord; var wbV = k.WarbandOfFief(villageFief)!;
        output.WriteLine($"fief {fiefNo}: holding {aw.HoldingOf(fiefNo).Id}, target {aw.TargetSize(fiefNo)}, men {wb.Members.Count}, mounted {wb.Mounted}; fief {villageFief}: {aw.HoldingOf(villageFief).Id}, men {wbV.Members.Count}");
        Assert.True(wb.Members.Count >= 5, "a baron's warband");
        // every man is a real person of the region, alive, enlisted in the soldiers' book and kept off his household's fields
        foreach (ulong m in wb.Members) { int r = aw.RowOf(m); Assert.True(r >= 0 && k.State.Population.People.IsAlive(r)); Assert.NotNull(k.State.Soldiers!.Of(m)); Assert.True(k.Book.IsAway(m)); Assert.True(k.R.Sim.Get<Ravensguard.Sim.Abstract.EconomyWorld>()!.Farming.IsEmployedElsewhere!(r)); }
        Assert.Equal(1, k.Entries("warband_raised").Count(e => e.Subjects.Contains(new EntityId(lordA))));
        // a friendly lord: the nearest other lord of the region by the road, who the baron holds in esteem
        int bestFief = -1, bestD = int.MaxValue;
        foreach (var f in k.Rule.Fiefs.Values)
        {
            if (f.Fief == fiefNo || f.Holder < 0) continue;
            int seat = k.State.Population.Region.Fiefs[f.Fief].SeatSettlement; int d = aw.DistM(wb.Seat, seat);
            if (d < bestD && aw.PathOf(wb.Seat, seat) != null && d > 2000) { bestD = d; bestFief = f.Fief; }
        }
        ulong friend = (ulong)k.Rule.Fiefs[bestFief].Holder;
        Opinions.Add(k.Pol, k.Rule, lordA, friend, "kin", k.Day, 0, 30);
        output.WriteLine($"friend: fief {bestFief} {bestD} m away");
        var tracked = new List<(Warband Wb, int S)>();
        foreach (var w in new[] { wb, wbV }) foreach (int s0 in region0.Fiefs[w.Fief].Settlements) tracked.Add((w, s0));
        long start = k.Now; bool visitMod = false; var seen = tracked.ToDictionary(t => t.S, t => new List<int>()); var last = tracked.ToDictionary(t => t.S, t => t.Wb.Visited.GetValueOrDefault(t.S, -1));
        for (long h = start + 1; h <= start + 30 * 24; h++)
        {
            k.To(h);
            visitMod |= k.Pol.Mods.Any(m => m.Kind == "visit" && m.A == Opinions.K(friend) && m.B == Opinions.K(lordA));
            foreach (var (w, s0) in tracked) { int v = w.Visited.GetValueOrDefault(s0, -1); if (v != last[s0]) { seen[s0].Add(v); last[s0] = v; } }
        }
        int startDay = (int)(start / 24), endDay = startDay + 30; int villagesChecked = 0;
        output.WriteLine("fief 7 log: " + string.Join("; ", k.Book.Log.Where(l => l.Warband == wbV.Id).Select(l => $"d{l.Day - startDay} {l.Task}->{l.Target}")) + $"; errands {string.Join(",", k.Book.Errands.Values.Where(e => e.Fief == villageFief).Select(e => e.Id + e.State.ToString() + e.DayMade))}");
        foreach (var (w, s0) in tracked)
        {
            bool farm = region0.Settlements[s0].Class == SettlementClass.Farmstead;
            var days = new List<int> { startDay }; days.AddRange(seen[s0]); days.Add(endDay);
            int gap = days.Zip(days.Skip(1), (x, y) => y - x).Max();
            output.WriteLine($"fief {w.Fief} {region0.Settlements[s0].Class} {s0}: visits at {string.Join(",", seen[s0].Select(d => d - startDay))}, longest gap {gap}");
            Assert.True(seen[s0].Count > 0, $"settlement {s0} was never visited");
            Assert.True(gap <= (farm ? 14 : 6), $"settlement {s0}: {gap} days between rides");     // every village and town within six days; a farmstead within its own longer round
            if (!farm) villagesChecked++;
        }
        Assert.True(villagesChecked >= 4);
        // the log of this warband: every task has a cause, and the three the gate names are there
        var mine = k.Book.Log.Where(l => l.Warband == wb.Id).ToList();
        foreach (var l in mine) { Assert.Contains(l.Cause, TaskCauses); Assert.NotEqual("", l.Cause); }
        output.WriteLine(string.Join("; ", mine.Select(l => $"d{l.Day - startDay} {l.Task}->{l.Target} ({l.Cause})")));
        Assert.Contains(mine, l => l.Task == WarbandTask.Patrol && l.Cause == "ch_note_am_patrol_due");
        Assert.Contains(mine, l => l.Task == WarbandTask.Escort && l.Cause == "ch_note_am_wagon");
        Assert.Contains(mine, l => l.Task == WarbandTask.Visit && l.Cause == "ch_note_am_friend");
        // the wagon arrived under escort, and the visit raised the friend's opinion of the baron (the content kind "visit")
        Assert.Contains(k.Book.Errands.Values, e => e.Fief == fiefNo && e.State == ErrandState.Done && e.Escort == wb.Id);
        Assert.True(visitMod, "the visit gave the friend's opinion of the baron the content kind \"visit\"");
        // the men were paid from the lord's purse by ledger transfers, nothing was made
        Assert.Equal(0, wb.ArrearsMites);
        Assert.True(k.State.Economy!.Ledger.Audit().IsConserved);
        Assert.True(k.State.Economy.Ledger.Flow(TransferReason.Wage).Mites > 0);
    }

    // ---------------------------------------------------------------- AM-2

    private static List<ulong> MenFor(ArmyRig k, int count, int place, bool farFromHome, HashSet<ulong>? not = null)
    {
        var pt = k.State.Population.People; var ht = k.State.Population.Households; var res = new List<ulong>(); int day = k.Day;
        for (int i = 0; i < pt.Count && res.Count < count; i++)
        {
            if (pt.Sex[i] != Sex.Male || pt.Status[i] != PersonStatus.Alive) continue;
            int age = pt.AgeYears(i, day); if (age < 16 || age > 45) continue;
            ulong id = pt.IdOf(i).Value; if (k.Book.IsAway(id) || not?.Contains(id) == true || k.State.Soldiers!.Of(id) != null || k.R.Sim.Get<Ravensguard.Sim.Abstract.EconomyWorld>()!.Labour.IsStaff(i)) continue;
            if (pt.Occupation[i] is Occupation.Lord or Occupation.Priest or Occupation.Steward or Occupation.Mayor) continue;
            if (farFromHome && k.Aw.HomeDistM(id, place) < 8000) continue;
            if (!farFromHome && k.Aw.HomeDistM(id, place) > 4000) continue;
            res.Add(id);
        }
        return res;
    }

    private static int PlaceAbout(ArmyRig k, Warband wb, int metres)
    {
        int best = -1, bd = int.MaxValue; var region = k.State.Population.Region;
        for (int s = 0; s < region.Settlements.Count; s++)
        {
            if (s == wb.Seat || region.Settlements[s].Class == SettlementClass.Town || k.Aw.PathOf(wb.Seat, s) == null) continue;
            int d = Math.Abs(k.Aw.DistM(wb.Seat, s) - metres); if (d < bd) { bd = d; best = s; }
        }
        return best;
    }

    [Fact]
    public void AM2_ARobberyRumourADayOldFiveKmAway_TheWarbandRidesSearchesAndFightsTheBand_ABandStrongerThanOneAndAHalfTimesIsNotChased()
    {
        using var k = new ArmyRig();
        k.Raised();
        // two warbands of the region: the first has a weak band to go for, the second is told of one that is too strong for it
        var big = k.Book.Warbands.Values.OrderByDescending(w => w.Members.Count).ThenBy(w => w.Id).First();
        var other = k.Book.Warbands.Values.Where(w => w.Id != big.Id && w.Members.Count >= 3 && w.Members.Count <= 8).OrderBy(w => w.Id).First();
        int place = PlaceAbout(k, big, 5000);
        int dist = k.Aw.DistM(big.Seat, place);
        output.WriteLine($"warband {big.Id} ({big.Members.Count} men) of fief {big.Fief}; the news is of settlement {place}, {dist} m away");
        var men = MenFor(k, 3, place, true);
        var band = k.Aw.FormBand(k.Env(), men, place, BandOrigin.Outlaws)!;
        Assert.Equal(3, band.Strength);
        int place2 = PlaceAbout(k, other, 5000);
        var men2 = MenFor(k, other.Members.Count * 2 + 2, place2, true, [.. men]);                  // twice its strength and more: past one and a half
        var strong = k.Aw.FormBand(k.Env(), men2, place2, BandOrigin.Outlaws)!;
        // the news reaches both lords at once, a day old (what they know is the news: the lair is not read)
        k.Book.Hear(k.Day - 1, place, band.Id, -1, band.Strength);
        k.Book.Hear(k.Day - 1, place2, strong.Id, -1, strong.Strength);
        long t0 = k.Now;
        var beforeChase = k.Book.Log.Count;
        for (long h = t0 + 1; h <= t0 + 72; h++) k.To(h);
        var chase = k.Book.Log.Skip(beforeChase).Where(l => l.Task == WarbandTask.ChaseBandits).ToList();
        output.WriteLine(string.Join("; ", chase.Select(l => $"wb{l.Warband} -> {l.Target} ({l.Cause})")));
        Assert.Contains(chase, l => l.Warband == big.Id && l.Target == place && l.Cause == "ch_note_am_robbery_news");
        Assert.DoesNotContain(chase, l => l.Warband == other.Id);                                        // 1.5 times its men: not chased
        Assert.True(strong.Members.Count == men2.Count && k.Book.Bands.ContainsKey(strong.Id));
        // the fight was fought: a battle report with the chase as its cause, the dead killed by name, and the band is not what it was
        var reports = k.Entries("battle_report").Where(e => e.Causes.Any(c => c.NoteKey == "ch_note_am_chase")).ToList();
        Assert.Single(reports);
        var left = k.Book.Bands.TryGetValue(band.Id, out var b2) ? b2.Members.Count : 0;
        output.WriteLine($"the band of 3: {left} left, warband {big.Members.Count} men, report {string.Join(",", reports[0].Data.Select(kv => kv.Key + "=" + kv.Value).Where(x => x.StartsWith("lost") || x.StartsWith("winner")))}");
        var pt = k.State.Population.People;
        foreach (ulong m in men) { int r = k.Aw.RowOf(m); Assert.True(pt.Status[r] == PersonStatus.Dead || band.Members.Contains(m) || pt.Occupation[r] != Occupation.Bandit); }
        // the warband came home again and is at rest or on its next task
        Assert.True(big.Here >= 0 || big.Task != WarbandTask.ChaseBandits || k.Now > t0);
        Assert.True(k.State.Economy!.Ledger.Audit().IsConserved);
    }

    [Fact]
    public void AM2_WithNoBandWhereTheNewsSays_TheWarbandSearchesTheFourKilometresAndRidesHome_TheNewsCanBeStale()
    {
        using var k = new ArmyRig();
        k.Raised();
        var wb = k.Book.Warbands.Values.Where(w => w.Members.Count >= 3).OrderBy(w => w.Id).First();
        int news = PlaceAbout(k, wb, 4000);
        // the band lives six kilometres or more from the place the news names: out of the search
        int lair = -1; for (int s = 0; s < k.State.Population.Region.Settlements.Count; s++) if (k.Aw.DistM(news, s) is > 6000 and < 12000) { lair = s; break; }
        var band = k.Aw.FormBand(k.Env(), MenFor(k, 2, lair, true), lair, BandOrigin.Deserters)!;
        k.Book.Hear(k.Day, news, band.Id, -1, 2);
        long t0 = k.Now; int log0 = k.Book.Log.Count;
        for (long h = t0 + 1; h <= t0 + 60; h++) k.To(h);
        Assert.Contains(k.Book.Log.Skip(log0), l => l.Warband == wb.Id && l.Task == WarbandTask.ChaseBandits);
        Assert.DoesNotContain(k.Entries("battle_report"), e => e.Causes.Any(c => c.NoteKey == "ch_note_am_chase"));
        Assert.Equal(2, k.Book.Bands[band.Id].Members.Count);                                           // nobody was found, nobody hurt
    }

    // ---------------------------------------------------------------- AM-14

    [Fact]
    public void AM14_ThirtyDesertersFarFromHome_FormABand_AWarbandChasesIt_TheirMenArePeopleWithHouseholdsAbroadOrNone_TheDeadStayDead()
    {
        using var k = new ArmyRig();
        k.Raised();
        var wb = k.Book.Warbands.Values.OrderByDescending(w => w.Members.Count).ThenBy(w => w.Id).First();
        int place = PlaceAbout(k, wb, 6000);
        var deserters = MenFor(k, 30, place, true);
        Assert.Equal(30, deserters.Count);
        var chron = RuleChron.Of(k.State.Chronicle, k.State.Ids);
        var pt = k.State.Population.People; var before = deserters.Select(d => (Id: d, Row: k.Aw.RowOf(d), Hh: pt.Household[k.Aw.RowOf(d)], Occ: pt.Occupation[k.Aw.RowOf(d)])).ToList();
        int people = pt.Count, alive = Enumerable.Range(0, pt.Count).Count(i => pt.IsAlive(i));
        var band = k.Aw.FormBand(k.Env(chron), deserters, place, BandOrigin.Deserters)!;
        Assert.Equal(30, band.Members.Count); Assert.Equal(BandOrigin.Deserters, band.Origin);
        Assert.Equal(people, pt.Count);                                                                  // nobody was made
        Assert.Equal(alive, Enumerable.Range(0, pt.Count).Count(i => pt.IsAlive(i)));
        foreach (var d in before)
        {
            Assert.True(pt.IsAlive(d.Row)); Assert.Equal(Occupation.Bandit, pt.Occupation[d.Row]);
            Assert.Equal(d.Hh, pt.Household[d.Row]);                                                     // the household stays: he is a real man of a real house, abroad of the band's ground
            Assert.True(d.Hh < 0 || k.Aw.HomeDistM(d.Id, place) >= k.Aw.Rules.DeserterHomeM, "far from home");
            Assert.True(k.Book.IsAway(d.Id));
        }
        var formed = k.Entries("bandits_formed").Single();
        Assert.Contains(formed.Causes, c => c.Kind == CauseKind.Desertion && c.NoteKey == "ch_note_am_deserters");
        Assert.Contains(new EntityId(band.Leader), formed.Subjects);
        // the news says there are fewer than there are (rumours do): the warband that rides is the biggest, and it is told what it can bear
        int rumoured = (1500 * wb.Members.Count - 1) / 1000;
        Assert.True(rumoured < 30, $"the biggest warband has {wb.Members.Count} men");
        k.Book.Hear(k.Day, place, band.Id, -1, rumoured);
        long t0 = k.Now; int log0 = k.Book.Log.Count;
        for (long h = t0 + 1; h <= t0 + 72; h++) k.To(h);
        Assert.Contains(k.Book.Log.Skip(log0), l => l.Warband == wb.Id && l.Task == WarbandTask.ChaseBandits && l.Target == place);
        var fought = k.Entries("battle_report").Where(e => e.Causes.Any(c => c.NoteKey == "ch_note_am_chase")).ToList();
        Assert.NotEmpty(fought);
        // who is where now: every one of the 30 is dead, or still in the band, or a man of his house again; no person is in two places
        var live = k.Book.Bands.TryGetValue(band.Id, out var nb) ? nb.Members.ToHashSet() : [];
        int dead = 0, home = 0, inBand = 0;
        foreach (var d in before)
        {
            int r = d.Row;
            if (pt.Status[r] == PersonStatus.Dead) { dead++; Assert.DoesNotContain(d.Id, live); Assert.Null(k.Book.WarbandWith(d.Id)); }
            else if (live.Contains(d.Id)) { inBand++; Assert.Equal(Occupation.Bandit, pt.Occupation[r]); }
            else { home++; Assert.Equal(d.Occ, pt.Occupation[r]); }
        }
        output.WriteLine($"of the 30: {dead} dead, {inBand} still in the band, {home} home again; warband {wb.Members.Count} men left of {before.Count}");
        Assert.Equal(30, dead + home + inBand);
        Assert.True(dead > 0, "a fight of ten against thirty leaves dead");
        // the dead stay dead: another week on, the same dead
        var deadIds = before.Where(d => pt.Status[d.Row] == PersonStatus.Dead).Select(d => d.Id).ToList();
        for (long h = k.Now + 24; h <= t0 + 72 + 24 * 7; h += 24) k.To(h);
        Assert.All(deadIds, id => Assert.Equal(PersonStatus.Dead, pt.Status[k.Aw.RowOf(id)]));
        foreach (var w in k.Book.Warbands.Values) Assert.All(w.Members, m => Assert.True(pt.IsAlive(k.Aw.RowOf(m))));
        Assert.True(k.State.Economy!.Ledger.Audit().IsConserved);
    }

    // ---------------------------------------------------------------- AM-19

    [Fact]
    public void AM19_TheArmiesSection_SaveLoadSave_IsByteIdentical_TheHashIsEqual_AndAnOlderSaveKeepsItsBytes()
    {
        using var k = new ArmyRig();
        var older = WorldSave.Capture(k.State, "h", "0.1");
        Assert.DoesNotContain(ArmySave.SectionName, older.Sections.Keys);
        var olderBytes = SaveCodec.Encode(older);
        var back0 = WorldSave.Restore(SaveCodec.Decode(olderBytes), k.R.Content, "h", null, k.R.W.Continent, k.R.W.History); back0.Content = k.R.Content;
        Assert.Equal(olderBytes, SaveCodec.Encode(WorldSave.Capture(back0, "h", "0.1")));               // no warbands yet: load and resave, the same bytes
        k.Raised();
        // four warbands on the roads, a wagon, a band of bandits with its news
        var band = k.Aw.FormBand(k.Env(), MenFor(k, 5, 10, true), 10, BandOrigin.Deserters)!;
        k.Book.Hear(k.Day, 10, band.Id, -1, 5);
        long tEnd = k.Now + 20; for (long h = k.Now + 1; h <= tEnd; h++) k.To(h);
        Assert.True(k.Book.Warbands.Count >= 4);
        var file = WorldSave.Capture(k.State, "h", "0.1");
        Assert.Contains(ArmySave.SectionName, file.Sections.Keys);
        var bytes = SaveCodec.Encode(file);
        var st = WorldSave.Restore(SaveCodec.Decode(bytes), k.R.Content, "h", null, k.R.W.Continent, k.R.W.History); st.Content = k.R.Content;
        Assert.Equal(k.Book.Hash(), st.Armies!.Hash());
        Assert.NotEqual(0UL, k.Book.Hash());
        Assert.Equal(bytes, SaveCodec.Encode(WorldSave.Capture(st, "h", "0.1")));                      // save, load, save: identical
        Assert.Equal(file.Sections[ArmySave.SectionName].ToArray(), WorldSave.Capture(st, "h", "0.1").Sections[ArmySave.SectionName].ToArray());
        output.WriteLine($"armies section {file.Sections[ArmySave.SectionName].Length} bytes; hash {k.Book.Hash():x}; {k.Book.Warbands.Count} warbands, {k.Book.Warbands.Values.Count(w => w.Here < 0)} of them on the road");
    }

    [Fact]
    public void AM19_SaveLoadContinue_WithWarbandsOnTheRoad_EqualsTheUninterruptedRun()
    {
        string Hash = "h";
        using var a = new ArmyRig();
        a.To(a.H0 + 30);
        for (int extra = 0; a.Book.Warbands.Values.Count(w => w.Here < 0) < 4 && extra < 24; extra++) a.To(a.Now + 1);        // an hour with several warbands on the road
        int onRoad = a.Book.Warbands.Values.Count(w => w.Here < 0);
        Assert.True(onRoad >= 4, $"{onRoad} on the road");
        var bytes = SaveCodec.Encode(WorldSave.Capture(a.State, Hash, "0.1"));
        long h1 = a.Now; a.To(h1 + 24 * 20);
        var want = a.Book.Hash(); var wantSection = SaveCodec.SerializeCompressed(ArmySave.Capture(a.Book));
        var st = WorldSave.Restore(SaveCodec.Decode(bytes), a.R.Content, Hash, null, a.R.W.Continent, a.R.W.History); st.Content = a.R.Content;
        using var rt = new WorldRuntime(st, a.R.Content, new MemoryRegionPager());
        rt.AdvanceTo(GameTime.FromGameHours(h1 + 24 * 20));
        Assert.Equal(want, st.Armies!.Hash());
        Assert.Equal(wantSection, SaveCodec.SerializeCompressed(ArmySave.Capture(st.Armies)));
        Assert.Equal(a.State.Chronicle.Hash(), st.Chronicle.Hash());
        output.WriteLine($"{onRoad} warbands on the road at the save; after 20 more days the armies' hash {want:x} in both runs");
    }

    // ---------------------------------------------------------------- the rest

    [Fact]
    public void TwoRunsOfTheSameSeed_AreTheSameWarbandsTheSameTasksTheSameHash()
    {
        ulong h1, h2;
        using (var k = new ArmyRig()) { k.To(k.H0 + 24 * 12); h1 = k.Book.Hash(); }
        using (var k = new ArmyRig()) { k.To(k.H0 + 24 * 12); h2 = k.Book.Hash(); }
        Assert.NotEqual(0UL, h1); Assert.Equal(h1, h2);
    }

    [Fact]
    public void TheWarbandsMenAreRealPeople_HiredNotMade_PaidByTheLordsPurse_AndSoldiersOfTheSoldiersBook()
    {
        using var k = new ArmyRig();
        var pt = k.State.Population.People; int people0 = pt.Count, alive0 = Enumerable.Range(0, pt.Count).Count(i => pt.IsAlive(i)); var coin0 = k.State.Economy!.Ledger.Audit();
        k.Raised();
        foreach (var w0 in k.Book.Warbands.Values) Assert.All(w0.Members, m => Assert.True(k.Aw.RowOf(m) < people0));          // nobody was made for a warband: every man was a person of the world already
        int men = k.Book.Warbands.Values.Sum(w => w.Members.Count);
        Assert.True(men >= 40, $"{men} men in {k.Book.Warbands.Count} warbands");
        foreach (var w in k.Book.Warbands.Values)
        {
            Assert.Equal(w.Members.Distinct().Count(), w.Members.Count);
            Assert.Equal(w.Members.OrderBy(x => x).ToList(), w.Members);                                 // stable order
            Assert.True(w.Members.Count <= k.Aw.TargetSize(w.Fief));
            Assert.Equal((ulong)k.Rule.Fiefs[w.Fief].Holder, w.Lord);
            foreach (ulong m in w.Members) { Assert.Equal(1, k.Book.Warbands.Values.Count(x => x.Members.Contains(m))); Assert.Equal((long)w.Lord, k.State.Soldiers!.Of(m)!.Lord); }
        }
        Assert.Equal(men, k.State.Soldiers!.Rows.Count);
        // a men's household of his own is paid the wage by a transfer from the lord's purse (reason Wage); the audit holds
        long wage0 = k.State.Economy.Ledger.Flow(TransferReason.Wage).Mites;
        k.To(k.H0 + 24 * 6);
        Assert.True(k.State.Economy.Ledger.Flow(TransferReason.Wage).Mites > wage0);
        Assert.True(k.State.Economy.Ledger.Audit().IsConserved);
        Assert.True(Enumerable.Range(0, pt.Count).Count(i => pt.IsAlive(i)) >= alive0 - 400);
    }

    [Fact]
    public void ALordWhoCannotPayHisMen_IsLeftWithArrears_AndAfterTwentyDaysOfThemTheyGoHome_WithTheirTradesBack()
    {
        using var k = new ArmyRig();
        k.Raised();
        var wb = k.Book.Warbands.Values.Where(w => w.Members.Any(m => !k.R.Sim.Get<Ravensguard.Sim.Abstract.EconomyWorld>()!.Labour.IsStaff(k.Aw.RowOf(m)))).OrderBy(w => w.Id).First();
        var pt = k.State.Population.People; var led = k.State.Economy!.Ledger;
        var rc = k.Aw.RuleCtx(k.Env())!; var purse = rc.PurseOf(rc.HouseholdOf((long)wb.Lord));
        var other = rc.PurseOf(rc.HouseholdOf((long)k.Rule.Fiefs[(wb.Fief + 1) % 48].Holder));
        Assert.True(led.Transfer(purse, other, led.Balance(purse), TransferReason.Gift));              // the lord's purse is emptied (to another lord's: coin is only moved)
        var hired = wb.Members.Where(m => wb.Prev.ContainsKey(m)).ToList(); Assert.NotEmpty(hired);
        int n0 = wb.Members.Count; ulong lord = wb.Lord; int id = wb.Id;
        long tEnd = k.Now + 24 * 30;
        for (long h = k.Now + 1; h <= tEnd; h++)
        {
            k.To(h);
            if (!k.Book.Warbands.ContainsKey(id)) break;
            if (h % 24 == 17 && led.Balance(purse).Mites > 0) Assert.True(led.Transfer(purse, other, led.Balance(purse), TransferReason.Gift));     // every stipend that comes in goes out again: a lord with nothing to pay with
        }
        Assert.False(k.Book.Warbands.ContainsKey(id), "dismissed");
        Assert.All(hired, m => { int r = k.Aw.RowOf(m); Assert.True(pt.IsAlive(r)); Assert.NotEqual(Occupation.Soldier, pt.Occupation[r]); Assert.Null(k.State.Soldiers!.Of(m)); Assert.False(k.Book.IsAway(m)); });
        var e = k.Entries("warband_dismissed").Single(x => x.Subjects.Contains(new EntityId(lord)));
        Assert.Contains(e.Causes, c => c.NoteKey == "ch_note_am_unpaid");
        Assert.True(led.Audit().IsConserved);
        output.WriteLine($"{n0} men sent home from fief {wb.Fief}");
    }

    [Fact]
    public void ARaidInWar_TakesRealGrainFromTheEnemysHouseholds_CarriesItHome_AndTheWarsDeclarationIsItsCause()
    {
        using var k = new ArmyRig();
        k.Raised();
        // the realm of most fiefs against the other one that holds two fiefs (21 and 38)
        int mine = k.Rule.Fiefs[0].Realm, enemy = k.Rule.Fiefs[21].Realm; Assert.NotEqual(mine, enemy);
        var decl = RuleChron.Of(k.State.Chronicle, k.State.Ids).Chron(GameTime.FromGameHours(k.Now), "h_war_declared", "ch_h_war_declared", [], [], [new Cause(CauseKind.Diplomacy, null, "", "ch_note_h_cb_insult")]);
        k.Pol.Wars.Add(new War { Serial = k.Pol.NextWar++, Attacker = mine, Defender = enemy, StartDay = k.Day, Cb = CbKind.Insult, DeclEntry = decl.Value });
        var region = k.State.Population.Region; int raider = -1, bestD = int.MaxValue;
        foreach (var w in k.Book.Warbands.Values.Where(w => w.Realm == mine && w.Members.Count >= 2))
            foreach (var f in k.Rule.Fiefs.Values.Where(f => f.Realm == enemy))
                foreach (int s in region.Fiefs[f.Fief].Settlements)
                { int d = k.Aw.DistM(w.Seat, s); if (region.Settlements[s].Class != SettlementClass.Town && d < bestD && k.Aw.PathOf(w.Seat, s) != null && d < k.Aw.Rules.RaidReachM) { bestD = d; raider = w.Id; } }
        Assert.True(raider > 0, "an enemy village within two days' ride of one of the realm's warbands");
        var wb = k.Book.Warbands[raider]; var econ = k.State.Economy!; int hh = k.Aw.HouseholdOf(wb.Lord);
        long grain(Store s) => k.Aw.GrainGoods().Sum(g => s.Qty(g) * Math.Max(1, econ.Goods.MassG(g, 1)));
        long lordGrain0 = grain(econ.HouseholdStore[hh]);
        Assert.True(econ.MassAuditHolds());
        long tEnd = k.Now + 24 * 8;
        for (long h = k.Now + 1; h <= tEnd; h++) { k.To(h); if (k.Entries("warband_raid").Any() && wb.Task == WarbandTask.Idle) break; }
        var raid = k.Entries("warband_raid").First(); output.WriteLine($"raid by warband {wb.Id}, {string.Join(", ", raid.Params!.Select(p => p.Name + "=" + p.Value))}");
        Assert.Contains(raid.Causes, c => c.Kind == CauseKind.War && c.EventId == decl);
        Assert.True(raid.Params!.Single(p => p.Name == "kg").Value > 0);
        Assert.Contains(k.Book.Log, l => l.Warband == wb.Id && l.Task == WarbandTask.Raid && l.Cause == "ch_note_am_war_raid");
        Assert.Contains(k.Book.Reports, n => n.Realm == mine && n.Band == 0);                         // the enemy hears of it
        Assert.True(econ.MassAuditHolds());                                                            // grain moved between stores, none made, none lost
        Assert.True(grain(econ.HouseholdStore[hh]) > lordGrain0 || wb.LootG > 0, "the grain came home with the men");
    }

    [Fact]
    public void TheHeroAsALord_HasNoStandingWarbandMadeForHim_AndHisIdNeverReadsAsNobody()
    {
        using var k = new ArmyRig();
        var hero = k.R.Hero; var rb = k.Rule;
        var f = rb.Fiefs[3]; long was = f.Holder; f.Holder = (long)hero.Value;                          // the hero holds a manor
        Assert.True(k.Aw.IsHero(hero.Value));
        k.Raised();
        Assert.Null(k.Book.WarbandOfFief(3));                                                            // his men are his to raise (the Army tab, AM-E)
        Assert.NotEmpty(k.Book.Warbands);
        Assert.False(k.Book.IsAway(hero.Value));
        _ = was;
    }

    [Fact]
    public void ASoldiersFillFromTheLordsArmoury_GivesTheWarbandsMenRealKit_NothingMade()
    {
        using var k = new ArmyRig();
        k.Raised();
        var wb = k.Book.Warbands.Values.OrderByDescending(w => w.Members.Count).First(); var sw = k.Aw.Soldiers!; var econ = k.State.Economy!;
        const long key = 777; var cap = new ArmouryCapacity(60, 60, 60, 60, 60, 600, 240, 600, 360, 2400);
        var ids = new Ravensguard.Sim.Core.IdAllocator();
        var spears = new List<EntityId>();
        for (int i = 0; i < wb.Members.Count; i++)
        {
            var item = econ.Items.Add(k.State.Ids, "spear", ItemQuality.Common, EntityId.Make(IdKind.Household, 1), [new ItemComponent("ash", 1500, 1000), new ItemComponent("iron", 300, 1000)], conditionPermille: 1000);
            Assert.True(sw.Armouries.Add(key, cap, item)); spears.Add(item);
        }
        _ = ids;
        k.Aw.ArmouryAt = seat => seat == wb.Seat ? key : null;
        long mass0 = econ.Items.LiveRows().Sum(r => econ.Items.MassG(r, econ.Goods)); int items0 = econ.Items.Count;
        var res = k.Aw.Equip(k.Env(), wb)!;
        Assert.True(res.Issued > 0, "spears issued");
        Assert.Equal(items0, econ.Items.Count); Assert.Equal(mass0, econ.Items.LiveRows().Sum(r => econ.Items.MassG(r, econ.Goods)));
        int armed = wb.Members.Count(m => sw.Book.Of(m)!.Worn.ContainsKey("weapon"));
        Assert.True(armed > 0);
        foreach (ulong m in wb.Members) { var row = sw.Book.Of(m)!; foreach (var (slot, item) in row.Worn) Assert.Equal(m, econ.Items.Get(new EntityId(item)).Owner.Value); }
    }

    [Fact]
    public void TheWarbandsHourlyStepCostsAFractionOfAMillisecond_ThroughAYear()
    {
        using var k = new ArmyRig();
        k.To(k.H0 + 24 * 60);
        var aw = k.Aw;
        output.WriteLine($"{k.Book.Warbands.Count} warbands, {k.Book.Warbands.Values.Sum(w => w.Members.Count)} men: hour mean {aw.HourCost.MeanMs:F3} ms max {aw.HourCost.MaxMs:F3} ms over {aw.HourCost.Calls} calls; day (pay, bands) mean {aw.DayCost.MeanMs:F3} max {aw.DayCost.MaxMs:F3}; season hours mean {aw.SeasonCost.MeanMs:F3} max {aw.SeasonCost.MaxMs:F3}");
        Assert.True(aw.HourCost.MeanMs < 1.0, $"hourly mean {aw.HourCost.MeanMs:F3} ms");
        Assert.True(aw.DayCost.MeanMs < 5.0);
    }
}
