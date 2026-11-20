using Ravensguard.Content;
using Ravensguard.Sim.Characters;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Goods;
using Ravensguard.Sim.Land;
using Ravensguard.Sim.People;
using Ravensguard.Sim.Save;
using Ravensguard.Sim.Tests.Seamless;
using Ravensguard.Sim.Trade;
using Ravensguard.Sim.WorldGen.Region;
using Ravensguard.Sim.Worlds;
using Xunit;
using Xunit.Abstractions;

namespace Ravensguard.Sim.Tests.Abstract;

/// <summary>
/// Save, load, continue at the level of a whole <see cref="WorldRuntime"/> (the plan's rule, section 6, one level above <see cref="ResumeDeterminismTests"/>): a new game runs N hours,
/// is saved with <see cref="WorldSave.Capture"/> at a barrier, goes through the codec, is loaded with <see cref="WorldSave.Restore"/> into a fresh runtime and runs M more hours. Every save
/// section, the abstract continent (compared directly, so a section that is never written cannot hide it) and every resident region's records must equal one uninterrupted run of N + M hours.
/// The regression test of docs/reviews/resume-drift-diagnosis.md (the continent section was never written; the farming seed-debt table was not grown after a load). The last case adds the
/// Phase 5 step 1 sections: a hero (a person row since P5-A4, with a party), land bought and a stall rented through the hero's desk (P5-A2) and a named artefact in the crafting section (P5-A1).
/// </summary>
public class WorldRuntimeResumeTests(ITestOutputHelper output)
{
    private const string Hash = "h";

    private static (SeamWorld W, WorldState State) Game(bool hero)
    {
        var w = SeamWorld.Get(1234);
        var home = w.BlockOrigin.Offset(1, 1);
        var dir = Ravensguard.Sim.WorldGen.ContinentSettings.FindBaseContent()!;
        var cs = Ravensguard.Sim.WorldGen.ContinentSettings.LoadDefaults(dir); cs.Seed = 1234;
        var hs = Ravensguard.Sim.WorldGen.History.HistorySettings.LoadDefaults(dir); hs.Seed = 1234;
        var region = RegionGenerator.Generate(w.Continent, w.History, home, w.Settings);
        LandmarkPlacer.Place(region, w.History, w.Content);
        var pop = Populator.Populate(region, w.History, w.Content);
        var h = hero ? HeroRecord.FromOrigin("Aldric Vane", false, w.Content.All<OriginDef>().OrderBy(o => o.Order).First()) : null;
        return (w, NewGameWorld.Create(w.Content, w.Continent, w.History, cs, hs, w.Settings, home.CellX, home.CellY, pop, h, null));
    }

    /// <summary>
    /// The Phase 5 step 1 state, before the save (so the same in both runs): the hero buys a town lot and rents a stall through the hero's desk, as the game sends them (the "land" section),
    /// and a named artefact of the hero's making is on the books (the "crafting" section's names).
    /// </summary>
    private static void Play(WorldRuntime rt, ContentDatabase content)
    {
        var s = rt.State;
        var lw = s.HomeRecords!.Sim.Get<LandWorld>()!;
        var ctx = new HeroContext(s, s.Party!, content);
        s.Economy!.Ledger.Mint(ctx.Purse, Coin.FromCrowns(2000), SourceReason.Seed);
        var reg = s.Population.Region;
        int town = reg.Settlements.FindIndex(x => x.Class == SettlementClass.Town && lw.Listings(reg.Settlements.IndexOf(x)).Count > 0);
        long sec = rt.HourDone * 3600;
        var desk = new HeroDesk();
        desk.Issue(LandDesk.Buy(lw.Listings(town)[0], sec));
        desk.Issue(LandDesk.Stall(town, 0, false, sec));
        desk.ApplyDue(ctx, rt.HourDone);
        Assert.All(desk.View!.Results, r => Assert.True(r.Ok, r.Text));
        s.Crafting ??= new Ravensguard.Sim.Crafting.CraftState();
        var heroId = s.Population.People.IdOf(s.Hero!.PersonIndex);
        s.Crafting.Artefacts.Named[EntityId.Make(IdKind.Item, 0xA27E)] = new Artefact(Artefacts.NameFor(content, s.Seed, new EntityId(77), "Aldric Vane"), heroId, "Aldric Vane", sec);
    }

    /// <summary>The ring-1 neighbours, built now and taken in (they are then stepped hourly, in parallel with home).</summary>
    private static void AddNeighbours(WorldRuntime rt)
    {
        rt.Manager.Synchronous = true;
        var home = rt.State.HomeRegion;
        foreach (var (dx, dy) in new[] { (1, 0), (0, 1), (-1, 0), (0, -1), (1, 1) }) { var rc = home.Offset(dx, dy); if (rc.IsValid) rt.Manager.GenerateNow(rc); }
        rt.Records.Sync();
    }

    /// <summary>Steps to <paramref name="to"/>: on the calling thread, or on the tick worker with barriers, pumped the way the street view pumps it.</summary>
    private static void Run(WorldRuntime rt, long to, bool worker)
    {
        if (!worker) { rt.AdvanceTo(GameTime.FromGameHours(to)); return; }
        rt.Start();
        var home = rt.State.HomeRegion; var t = GameTime.FromGameHours(to);
        while (rt.HourDone < to) { rt.Pump(t, home.OriginXM + 16000, home.OriginYM + 16000); Thread.Sleep(1); }
        rt.Quiesce();
        Assert.Null(rt.Fault);
    }

    /// <summary>The save, taken at a barrier (as the game takes it).</summary>
    private static SaveFile SaveAt(WorldRuntime rt) => rt.AtBarrier(() => WorldSave.Capture(rt.State, Hash, "0.1")).GetAwaiter().GetResult();

    private static SortedDictionary<string, byte[]> Sections(WorldRuntime rt) => rt.AtBarrier(() =>
    {
        var d = new SortedDictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (var (k, v) in WorldSave.Capture(rt.State, Hash, "0.1").Sections) if (k != WorldSave.MetaSection) d[k] = v.ToArray();
        d["~continent"] = SaveCodec.SerializeCompressed(ContinentSave.Capture(rt.State.ContinentState!));
        foreach (var r in rt.Records.Resident) d[$"~records.{r.Coord.Id:D3}"] = System.Text.Encoding.ASCII.GetBytes(r.RecordsHash());
        return d;
    }).GetAwaiter().GetResult();

    [Theory]
    [InlineData(13, 48, false, false, false)]    // mid-day
    [InlineData(48, 48, false, false, false)]    // on a day boundary
    [InlineData(385, 48, false, false, false)]   // the continent's season tick (hour 0 of day 2 of summer) falls inside M
    [InlineData(1430, 48, false, false, false)]  // across the year boundary (a game year is 1,440 hours)
    [InlineData(48, 48, true, false, false)]     // on the tick worker with barriers
    [InlineData(13, 48, true, true, false)]      // the worker, five resident neighbours stepped in parallel
    [InlineData(340, 72, false, false, true)]    // a hero with land, a stall and a named artefact; the season's turn (dues) and the continent's tick fall inside M
    public void SaveLoadContinue_OfAWholeRuntime_EqualsTheUninterruptedRun(int n, int m, bool worker, bool neighbours, bool hero)
    {
        var (w, a) = Game(hero);
        using var straight = new WorldRuntime(a, w.Content, new MemoryRegionPager());
        if (neighbours) AddNeighbours(straight);
        if (hero) Play(straight, w.Content);
        long h0 = straight.HourDone;
        Run(straight, h0 + n, worker);
        var file = SaveAt(straight);
        Assert.Contains(WorldSave.ContinentSection, file.Sections.Keys);                      // fix 1: the continent is written
        var bytes = SaveCodec.Encode(file);
        Run(straight, h0 + n + m, worker);
        var want = Sections(straight);

        var st = WorldSave.Restore(SaveCodec.Decode(bytes), w.Content, Hash, null, w.Continent, w.History);
        st.Content = w.Content;
        using var resumed = new WorldRuntime(st, w.Content, new MemoryRegionPager());
        if (neighbours) resumed.Manager.Synchronous = true; // the saved neighbours page back in at the first barrier
        Run(resumed, h0 + n + m, worker);
        var got = Sections(resumed);

        if (hero)
        {
            // the Phase 5 step 1 sections are there to compare: land, the hero (a person row in "people", with the party) and the crafting section's named artefact
            foreach (var k in new[] { LandSave.SectionName, WorldSave.HeroSection, WorldSave.PartySection, WorldSave.CraftingSection, WorldSave.PeopleSection })
                Assert.Contains(k, want.Keys);
            foreach (var s in new[] { straight.State, resumed.State })
            {
                Assert.Equal(PersonStatus.Hero, s.Population.People.Status[s.Hero!.PersonIndex]);
                Assert.Equal(1, s.Crafting!.Artefacts.Count);
                Assert.False(s.Land!.IsEmpty);
            }
            Assert.Equal(straight.State.Hero!.PersonIndex, resumed.State.Hero!.PersonIndex);
            Assert.Equal(straight.State.Land!.ComputeHash(), resumed.State.Land!.ComputeHash());
            Assert.Equal(straight.State.Crafting!.Artefacts.Hash(), resumed.State.Crafting!.Artefacts.Hash());
        }
        var bad = want.Keys.Union(got.Keys).Where(k => !(want.TryGetValue(k, out var x) && got.TryGetValue(k, out var y) && x.AsSpan().SequenceEqual(y))).ToList();
        output.WriteLine($"N {n} M {m} worker {worker} neighbours {neighbours} hero {hero}: {want.Count} sections [{string.Join(", ", want.Keys)}], differing [{string.Join(", ", bad)}]");
        Assert.Empty(bad);
    }
}
