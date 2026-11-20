using Ravensguard.Content;
using Ravensguard.Sim.Characters;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Law;
using Ravensguard.Sim.People;
using Ravensguard.Sim.Save;
using Ravensguard.Sim.Tests.Economy;
using Ravensguard.Sim.Tests.Seamless;
using Ravensguard.Sim.Trade;
using Ravensguard.Sim.WorldGen.Region;
using Ravensguard.Sim.Worlds;
using Xunit;
using Xunit.Abstractions;

namespace Ravensguard.Sim.Tests.Core;

/// <summary>
/// The coverage soak of the chronicle and the Chronicle screen's logic (chronicle-full 4.2, 4.4, 4.5, 7): CH-4 (specific coverage >= 90% over ten game years of a new game with the hero's own thefts),
/// CH-5 (every entry has a cause: a specific one or the market drift label), CH-9 (compaction), CH-10 (the browser: why, what followed, back), CH-11 (determinism and the save). One ten-year run
/// (600 days, about two minutes) serves CH-4, CH-5 and CH-11; the rest are synthetic. The 100-year soak with every system is the close-out's.
/// </summary>
public class ChronicleSoakTests(ITestOutputHelper output)
{
    private const string Hash = "h";

    internal static (SeamWorld W, WorldState State) Game(ulong seed = 1234)
    {
        var w = SeamWorld.Get(seed);
        var home = w.BlockOrigin.Offset(1, 1);
        var dir = Ravensguard.Sim.WorldGen.ContinentSettings.FindBaseContent()!;
        var cs = Ravensguard.Sim.WorldGen.ContinentSettings.LoadDefaults(dir); cs.Seed = seed;
        var hs = Ravensguard.Sim.WorldGen.History.HistorySettings.LoadDefaults(dir); hs.Seed = seed;
        var region = RegionGenerator.Generate(w.Continent, w.History, home, w.Settings);
        LandmarkPlacer.Place(region, w.History, w.Content);
        var pop = Populator.Populate(region, w.History, w.Content);
        var h = HeroRecord.FromOrigin("Aldric Vane", false, w.Content.All<OriginDef>().OrderBy(o => o.Order).First());
        var st = NewGameWorld.Create(w.Content, w.Continent, w.History, cs, hs, w.Settings, home.CellX, home.CellY, pop, h, null);
        st.Content = w.Content;
        return (w, st);
    }

    /// <summary>The hero bot's one act: a theft in front of three of his town's adults (the same script as the justice soak's), recorded through the hero desk.</summary>
    private static void Steal(WorldRuntime rt, ContentDatabase content)
    {
        rt.AtBarrier(() =>
        {
            var s = rt.State; var pt = s.Population.People; int town = pt.HomeSettlement[s.Hero!.PersonIndex];
            var witnesses = Enumerable.Range(0, pt.Count).Where(i => pt.IsAlive(i) && pt.HomeSettlement[i] == town && pt.AgeYears(i, (int)(rt.HourDone / 24)) >= 18 && i != s.Hero.PersonIndex).Take(3).ToList();
            if (witnesses.Count < 3) return 0;
            var ctx = new HeroContext(s, s.Party!, content);
            var desk = new HeroDesk();
            desk.Issue(CrimeDesk.Act("theft", pt.IdOf(witnesses[0]), town, 240, 0, "ch_note_crime_hero", new ActorLook([0xA03030u], 2),
                [new WitnessRow(pt.IdOf(witnesses[0]).Value, WitnessRow.KnewIt), new WitnessRow(pt.IdOf(witnesses[1]).Value, WitnessRow.KnewIt), new WitnessRow(pt.IdOf(witnesses[2]).Value, 0)], null, rt.HourDone * 3600 + 600));
            desk.ApplyDue(ctx, rt.HourDone + 1);
            return 1;
        }).GetAwaiter().GetResult();
    }

    private static T At<T>(WorldRuntime rt, Func<T> job) => rt.AtBarrier(job).GetAwaiter().GetResult();

    private static (WorldRuntime Rt, WorldState St, SeamWorld W) Run(int days, int stealEveryDays)
    {
        var (w, st) = Game();
        var rt = new WorldRuntime(st, w.Content, new MemoryRegionPager());
        long start = rt.HourDone + 1;
        for (int d = stealEveryDays; d <= days; d += stealEveryDays)
        {
            rt.AdvanceTo(GameTime.FromGameHours(start + d * 24L));
            Steal(rt, w.Content);
        }
        rt.AdvanceTo(GameTime.FromGameHours(start + days * 24L));
        return (rt, st, w);
    }

    // ---------------------------------------------------------------- CH-4, CH-5, CH-11

    [Fact]
    public void CH4_CH5_CH11_TenGameYearsOfANewGame_AreNinetyPercentSpecific_EveryEntryHasACause_AndSaveAndLoadAgree()
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var (rt, st, w) = Run(600, 60);
        using var _ = rt;
        var types = ChronicleTypes.From(w.Content); var c = st.Chronicle; var hist = w.History.Chronicle;
        var rep = c.SpecificCoverage(types, older: hist);
        output.WriteLine($"{c.Count} entries in 600 days ({sw.Elapsed.TotalSeconds:0}s): specific {rep.Specific}, drift labels {rep.Drift}, no cause {rep.NoCause}, summaries {rep.Summaries}; ratio {rep.Ratio:0.000}");
        foreach (var g in c.All.Where(e => !e.Causes.Any(k => k.Kind == CauseKind.MarketDrift) && !e.Causes.Any(k => c.IsSpecific(k, e, types.Get(e.TypeId), hist))).GroupBy(e => e.TypeId).OrderByDescending(g => g.Count()))
            output.WriteLine($"  not specific: {g.Key} x{g.Count()} ({string.Join(",", g.SelectMany(e => e.Causes).Select(k => k.Kind).Distinct())})");
        output.WriteLine("types: " + string.Join(", ", c.All.GroupBy(e => e.TypeId).OrderByDescending(g => g.Count()).Take(30).Select(g => g.Key + " " + g.Count())));
        Assert.True(rep.Entries > 3000, $"only {rep.Entries} entries");
        Assert.True(rep.Ratio >= 0.90, $"specific coverage {rep.Ratio:0.000} is under the 0.90 done-when");                                  // CH-4
        var p6 = c.SpecificCoverage(types, e => types.Get(e.TypeId) is { Def.Legacy: false }, hist);                                           // CH-8's dynamic half: the Phase 6 types the run wrote
        output.WriteLine($"Phase 6 types: {p6.Entries} entries, ratio {p6.Ratio:0.000}");
        Assert.True(p6.Entries == 0 || p6.Ratio >= 0.95, $"the Phase 6 entries are {p6.Ratio:0.000} specific (the gate is 0.95)");
        Assert.True(rep.Drift > 0, "the drift labels are counted apart");
        Assert.Equal(0, rep.NoCause);                                                                                                           // CH-5: a cause on every entry,
        Assert.All(c.All, e => Assert.True(e.Causes.Count > 0, e.TypeId));
        var chains = ChronicleChains.Check(c, types, older: hist);                                                                              // and the chains resolve to earlier entries
        output.WriteLine($"chains: {chains.Entries} entries, {chains.Roots} roots, unresolved {chains.Unresolved}, out of order {chains.OutOfOrder}, illegal roots {chains.IllegalRoots}");
        Assert.Equal(0, chains.Unresolved); Assert.Equal(0, chains.OutOfOrder);
        // the hero's thefts and what they caused are in it
        Assert.Contains(c.All, e => e.TypeId == "crime_witnessed"); Assert.Contains(c.All, e => e.TypeId == "bounty_posted");

        // CH-11: ChronicleHash equal across a save and a load, the effect index rebuilt at load equals the live one, and the chronicle section is byte-identical when saved again
        var file = At(rt, () => WorldSave.Capture(rt.State, Hash, "0.1"));
        var back = WorldSave.Restore(SaveCodec.Decode(SaveCodec.Encode(file)), w.Content, Hash, null, w.Continent, w.History);
        Assert.Equal(c.Hash(), back.Chronicle.Hash()); Assert.Equal(c.Effects.Hash(), back.Chronicle.Effects.Hash());
        var again = WorldSave.Capture(back, Hash, "0.1");
        Assert.True(file.Sections[WorldSave.ChronicleSection].Span.SequenceEqual(again.Sections[WorldSave.ChronicleSection].Span), "the chronicle section is not byte-identical after a load");
        // the screen's view of it: the deepest chain walks back to its root and forward again
        var view = ChronicleView.Of(st, -1);
        var br = new ChronicleBrowser(view, types, e => ChronicleReader.Line(e, types, view, k => k, x => x));
        var deepest = br.Deepest(); Assert.NotNull(deepest);
        br.Open(deepest!.Id); int steps = 0;
        while (br.FollowCause()) steps++;
        output.WriteLine($"the deepest chain: {deepest.TypeId}, {steps} steps back to {br.Current!.TypeId}");
        Assert.True(steps >= 2);
        for (int i = 0; i < steps; i++) Assert.True(br.Back());
        Assert.Equal(deepest.Id, br.Current!.Id);
    }

    /// <summary>CH-11 again across two runs: the same seed and script give the same chronicle hash, and a run is not touched by whether anyone looked at it (a view is built and dropped in between).</summary>
    [Fact]
    public void CH11_TwoRunsOfTheSameScript_HaveTheSameChronicleHash_ALookInBetweenChangesNothing()
    {
        ulong Once(bool look)
        {
            var (w, st) = Game();
            using var rt = new WorldRuntime(st, w.Content, new MemoryRegionPager());
            long start = rt.HourDone + 1;
            rt.AdvanceTo(GameTime.FromGameHours(start + 20 * 24L));
            Steal(rt, w.Content);
            if (look) GC.KeepAlive(At(rt, () => ChronicleView.Of(rt.State, 0)));
            rt.AdvanceTo(GameTime.FromGameHours(start + 40 * 24L));
            return At(rt, () => rt.State.Chronicle.Hash());
        }
        Assert.Equal(Once(false), Once(true));
    }

    // ---------------------------------------------------------------- CH-9

    private static ChronicleEntry E(Chronicle c, IdAllocator ids, int year, string type, EntityId[] subjects, params Cause[] causes)
    {
        var e = new ChronicleEntry(ids.Allocate(IdKind.Event), GameTime.FromCalendar(year, Season.Spring, 3), type, subjects, causes, new Dictionary<string, string>());
        c.Add(e); return e;
    }

    [Fact]
    public void CH9_OldCommonersEntriesWithNoKnownSubjectAndNoKeptEffectsBecomeYearlySummaries_NoKeptChainIsBroken()
    {
        var ids = new IdAllocator(); var c = new Chronicle();
        var hero = EntityId.Make(IdKind.Person, 1); var stranger = EntityId.Make(IdKind.Person, 2); var neighbour = EntityId.Make(IdKind.Person, 3); var town = EntityId.Make(IdKind.Settlement, 4);
        var born = E(c, ids, 1, "birth", [stranger], Why.Of(CauseKind.Family, "ch_why_birth"));                       // year 1, a stranger's: summarised
        var wed = E(c, ids, 1, "marriage", [stranger, neighbour], Why.Of(CauseKind.Family, "ch_why_wedding"));          // year 1, summarised
        var wedBorn = E(c, ids, 2, "birth", [neighbour], Why.After(CauseKind.Family, wed.Id, "ch_why_birth"));           // year 2: named by nothing kept, summarised (with the wedding it names)
        var feud = E(c, ids, 2, "quarrel", [stranger], Why.Of(CauseKind.Crime, "ch_why_riot_unrest"));                  // year 2, a stranger's but named by a kept entry below: kept
        var fire = E(c, ids, 3, "fire_started", [town], Why.After(CauseKind.Crime, feud.Id, "ch_why_riot_unrest"));       // a town's entry (not a commoner's): kept, and with it the feud it names
        var heroOld = E(c, ids, 3, "birth", [hero], Why.Of(CauseKind.Family, "ch_why_birth"));                           // the hero's own: kept at any age
        var recent = E(c, ids, 14, "birth", [stranger], Why.Of(CauseKind.Family, "ch_why_birth"));                        // not ten years old yet: kept
        var now = GameTime.FromCalendar(15, Season.Spring, 1);
        int dropped = ChronicleCompaction.Compact(c, now, id => id == hero, ids);
        var kept = c.All.Select(e => e.Id).ToHashSet();
        Assert.Equal(3, dropped);                                                                                       // born, wed, wedBorn
        Assert.DoesNotContain(born.Id, kept); Assert.DoesNotContain(wed.Id, kept); Assert.DoesNotContain(wedBorn.Id, kept);
        Assert.Contains(feud.Id, kept); Assert.Contains(fire.Id, kept); Assert.Contains(heroOld.Id, kept); Assert.Contains(recent.Id, kept);
        var summaries = c.All.Where(e => e.TypeId == ChronicleCompaction.SummaryType).ToList();
        Assert.Equal(2, summaries.Count);                                                                               // year 1 (two entries) and year 2 (one)
        Assert.Equal([2L, 1L], summaries.Select(s => s.Params!.Single(p => p.Name == "n").Value).ToArray());
        // no kept chain is broken: every cause that names an entry still resolves, in order
        var chains = ChronicleChains.Check(c); Assert.Equal(0, chains.Unresolved); Assert.Equal(0, chains.OutOfOrder);
        // the summaries are bookkeeping, counted apart; the index was rebuilt
        Assert.Equal(2, c.SpecificCoverage(null).Summaries);
        Assert.Equal([fire.Id], c.Effects.After(feud.Id).ToArray());
        // nothing more to do on a second pass; and a chronicle with only strangers' old entries folds to its summaries
        Assert.Equal(0, ChronicleCompaction.Compact(c, now, id => id == hero, ids));
        var ids2 = new IdAllocator(); var c2 = new Chronicle();
        E(c2, ids2, 1, "birth", [stranger], Why.Of(CauseKind.Family, "ch_why_birth")); E(c2, ids2, 1, "marriage", [stranger, neighbour], Why.Of(CauseKind.Family, "ch_why_wedding"));
        Assert.Equal(2, ChronicleCompaction.Compact(c2, now, _ => false, ids2));
        Assert.Equal(1, c2.Count);
    }

    // ---------------------------------------------------------------- CH-10

    [Fact]
    public void CH10_TheBrowser_ListsTheViews_FollowsCausesBackAndEffectsForward_AndBackspaceReturns()
    {
        var types = ChronicleTypes.Of([]);
        var hero = EntityId.Make(IdKind.Person, 1); var town = EntityId.Make(IdKind.Settlement, 7); var other = EntityId.Make(IdKind.Person, 9);
        var hist = new Chronicle(); var ids = new IdAllocator();
        var war = new ChronicleEntry(ids.Allocate(IdKind.Event), GameTime.FromCalendar(0, Season.Spring, 1), "war_declared", [], [new Cause(CauseKind.War)], new Dictionary<string, string> { ["title"] = "The old war", ["text"] = "A war began." });
        hist.Add(war);
        var region = new Chronicle();
        var raid = new ChronicleEntry(ids.Allocate(IdKind.Event), GameTime.FromCalendar(1, Season.Summer, 2), "bandit_raid", [town], [Why.After(CauseKind.War, war.Id, "ch_why_cull")], new Dictionary<string, string> { ["title"] = "A raid" });
        var wound = new ChronicleEntry(ids.Allocate(IdKind.Event), GameTime.FromCalendar(1, Season.Summer, 3), "wound", [hero], [Why.After(CauseKind.Raid, raid.Id, "ch_why_violence")], new Dictionary<string, string> { ["title"] = "Aldric is wounded" });
        var death = new ChronicleEntry(ids.Allocate(IdKind.Event), GameTime.FromCalendar(1, Season.Autumn, 1), "death", [other], [Why.After(CauseKind.Wound, raid.Id, "ch_why_bloodloss")], new Dictionary<string, string> { ["title"] = "A neighbour dies" });
        foreach (var e in new[] { raid, wound, death }) region.Add(e);
        var view = new ChronicleView
        {
            Entries = region.All.ToArray(), History = hist, Names = new Dictionary<ulong, string> { [hero.Value] = "Aldric", [town.Value] = "Oakford" }, Goods = new Dictionary<int, string>(),
            Mine = new HashSet<ulong> { hero.Value }, Here = new HashSet<ulong> { town.Value }, Realm = new HashSet<ulong>(), Hero = hero, HereName = "Oakford",
        };
        var b = new ChronicleBrowser(view, types, e => e.Data["title"]);
        // My life: the hero's entries only; Here: the town's; Legends: the history
        Assert.Equal([wound.Id], b.Rows.Select(r => r.Id).ToArray());
        b.SetScope(ChronicleScope.Here); Assert.Equal([raid.Id], b.Rows.Select(r => r.Id).ToArray());
        b.SetScope(ChronicleScope.Legends); Assert.Equal([war.Id], b.Rows.Select(r => r.Id).ToArray());
        b.SetScope(ChronicleScope.MyLife);
        // open the wound: F follows its cause back (the raid), again (the war, in the history: the Legends view)
        Assert.True(b.Open(wound.Id)); Assert.Equal(wound.Id, b.Current!.Id);
        Assert.True(b.FollowCause()); Assert.Equal(raid.Id, b.Current!.Id);
        Assert.True(b.FollowCause()); Assert.Equal(war.Id, b.Current!.Id); Assert.Equal(ChronicleScope.Legends, b.Scope); Assert.True(b.IsLegend(b.Current));
        Assert.False(b.FollowCause());                                                                                                       // the root: nothing earlier
        // D follows an effect forward: what followed the war is the raid (the region's entry that names a history entry)
        Assert.Equal([raid.Id], b.Followed(war).Select(f => f.Id).ToArray());
        Assert.True(b.FollowEffect()); Assert.Equal(raid.Id, b.Current!.Id);
        Assert.Equal([wound.Id, death.Id], b.Followed(raid).Select(f => f.Id).ToArray());
        Assert.True(b.FollowEffect(1)); Assert.Equal(death.Id, b.Current!.Id);                                                              // the second consequence
        // Backspace walks the trail back to where it began
        Assert.True(b.Back()); Assert.Equal(raid.Id, b.Current!.Id);
        Assert.True(b.Back()); Assert.Equal(war.Id, b.Current!.Id);
        Assert.True(b.Back()); Assert.Equal(raid.Id, b.Current!.Id);
        Assert.True(b.Back()); Assert.Equal(wound.Id, b.Current!.Id);
        Assert.False(b.Back());
        // filters: the family of a cause, the search by words and names
        b.SetScope(ChronicleScope.Realm); Assert.Contains(raid.Id, b.Rows.Select(r => r.Id));                                                // a war-family entry is the realm's
        b.SetFamily("war"); Assert.All(b.Rows, r => Assert.Equal("war", ChronicleFamilies.Of(r)));
        b.SetFamily(""); b.SetScope(ChronicleScope.Here); b.SetSearch("oakford"); Assert.Equal([raid.Id], b.Rows.Select(r => r.Id).ToArray());
        b.SetSearch("zzz"); Assert.Empty(b.Rows);
        Assert.Equal(death.Id, b.Deepest()!.Id);                                                                                             // two causes back (to the war): the newest of the deepest
    }

    // ---------------------------------------------------------------- CH-8

    /// <summary>
    /// CH-8's static half: no row of a type a Phase 6 writer owns (every row not marked legacy) allows an Unknown cause, and the writers that still pass the old Other kind (never specific) are counted and
    /// may only shrink: the meta entry of a difficulty change (the player's own choice), two construction collapse entries (a person knocking out a support), the fire test cause. Phase 2-5 writers
    /// and history v0 are moved to specific kinds (P6-E4); the continent tick's word_reached is part F's.
    /// </summary>
    [Fact]
    public void CH8_NoPhase6RowAllowsAnUnknownCause_AndTheWritersOfTheOldOtherKindOnlyShrink()
    {
        var types = ChronicleTypes.From(EconFixture.Content);
        foreach (var row in types.Rows.Where(r => !r.Def.Legacy)) Assert.DoesNotContain("Unknown", row.Def.CauseKinds);
        string sim = Path.Combine(Root(), "src", "Ravensguard.Sim");
        var skip = new[] { Path.Combine("WorldGen", "History"), Path.Combine("Core", "Chronicle"), Path.Combine("Continent", "ContinentTick") };
        int n = 0;
        foreach (var f in Directory.EnumerateFiles(sim, "*.cs", SearchOption.AllDirectories).Where(f => !f.Contains("/bin/") && !f.Contains("/obj/")))
        {
            string rel = Path.GetRelativePath(sim, f);
            if (skip.Any(s => rel.StartsWith(s, StringComparison.Ordinal))) continue;
            n += File.ReadLines(f).Count(l => !l.TrimStart().StartsWith("//") && (l.Contains("CauseKind.Other") || l.Contains("CauseKind.Unknown")));
        }
        output.WriteLine($"writers of the Other and Unknown cause kinds: {n}");
        Assert.True(n <= OtherWriters, $"{n} writers of CauseKind.Other or Unknown (pinned {OtherWriters}): a new writer names what happened with a specific kind");
    }

    /// <summary>Writers of the old Other kind left in the sim at P6-E4 (see CH8's comment). Lower it as they move.</summary>
    private const int OtherWriters = 4;

    private static string Root()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "src", "Ravensguard.Sim", "Core", "RngStreams.cs"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("repo root not found");
    }
}
