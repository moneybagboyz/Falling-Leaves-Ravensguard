using Ravensguard.Sim.Characters;
using Ravensguard.Sim.Codex;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.People;
using Ravensguard.Sim.Save;
using Ravensguard.Sim.WorldGen.Region;
using Xunit.Abstractions;

namespace Ravensguard.Sim.Tests.Codex;

public class CodexTests(ITestOutputHelper output)
{
    private static readonly CodexRule[] Rules =
    [
        new("rule.controls.world", "Controls: World", "Move: W A S D\nCodex: K"),
        new("rule.controls.menu", "Controls: Menus", "Back: Esc"),
    ];

    private static (TestWorld W, CodexBook Book) Make(ulong seed)
    {
        var w = TestWorld.Get(seed);
        LandmarkPlacer.Place(w.Region, w.History, w.Content);
        return (w, CodexBook.Build(w.History, w.Region, Rules));
    }

    [Fact]
    public void Book_holds_every_category_and_unlocks_by_region_realm_or_site()
    {
        var (w, book) = Make(2);
        foreach (var c in Enum.GetValues<CodexCategory>()) Assert.NotEmpty(book.In(c));
        output.WriteLine($"{book.Entries.Count} entries, {book.UnlockedCount} unlocked; " + string.Join(", ", Enum.GetValues<CodexCategory>().Select(c => $"{c} {book.In(c).Count(e => e.Unlocked)}/{book.In(c).Count()}")));
        Assert.Equal(book.Entries.Count, book.Entries.Select(e => e.Id).Distinct().Count());
        Assert.All(book.In(CodexCategory.Rule), e => Assert.True(e.Unlocked));
        Assert.All(book.In(CodexCategory.Landmark), e => Assert.False(e.Unlocked));
        Assert.Contains(book.In(CodexCategory.Legend), e => e.Unlocked);
        Assert.Contains(book.In(CodexCategory.Legend), e => !e.Unlocked); // the rest of the world is still unheard of

        // every realm of the embark region is open, and a realm outside it is not
        var inside = w.Region.Realms.Select(r => r.HistoryIndex).ToHashSet();
        foreach (var r in w.History.Realms) Assert.Equal(inside.Contains(r.Index), book.Get("realm." + r.Index)!.Unlocked);
        // an event a site of the region stands on is open
        var siteEvent = w.Region.Sites.First(s => s.HistoryEvent != EntityId.None && CodexBook.MajorTypes.Contains(w.History.Chronicle.Get(s.HistoryEvent)!.TypeId)).HistoryEvent;
        Assert.True(book.Get("legend." + siteEvent.Counter)!.Unlocked);
        // legends point at real chronicle events
        foreach (var e in book.In(CodexCategory.Legend).Where(e => e.Id.StartsWith("legend.")))
        {
            Assert.Single(e.Sources);
            Assert.NotNull(w.History.Chronicle.Get(e.Sources[0]));
        }
    }

    [Fact]
    public void Visiting_a_landmark_opens_its_page_and_the_legend_it_hangs_on()
    {
        var (w, book) = Make(2);
        var m = w.Region.Landmarks!.First(l => l.Attached);
        var page = book.Get("landmark." + m.Index)!;
        Assert.False(page.Unlocked);
        Assert.Empty(book.Visit(m.X + 500, m.Y));
        var opened = book.Visit(m.X + 10, m.Y - 10);
        Assert.Contains(page, opened);
        Assert.True(page.Unlocked);
        Assert.True(book.Get("legend." + m.Event.Counter)?.Unlocked ?? true);
        Assert.Contains(m.EventTitle, page.Text);
        Assert.Empty(book.Visit(m.X, m.Y)); // already known
    }

    [Fact]
    public void Search_only_finds_what_is_unlocked_and_filters_by_category()
    {
        var (w, book) = Make(2);
        var rm = w.History.Realms.First(r => book.Get("realm." + r.Index)!.Unlocked);
        var hits = book.Search(rm.Name.ToLowerInvariant()).ToList();
        Assert.Contains(hits, e => e.Id == "realm." + rm.Index);
        Assert.All(hits, e => Assert.True(e.Unlocked));
        Assert.All(book.Search(rm.Name, CodexCategory.Place), e => Assert.Equal(CodexCategory.Place, e.Category));
        Assert.Contains(book.Search("move", CodexCategory.Rule), e => e.Id == "rule.controls.world");
        Assert.Empty(book.Search("zzzqqq"));
        var locked = book.Entries.First(e => !e.Unlocked);
        Assert.DoesNotContain(book.Search(locked.Title), e => e == locked);
        Assert.Equal(book.Entries.Count, book.Search("").Count()); // an empty query lists every entry (locked ones show as "?")
    }

    [Fact]
    public void Progress_roundtrips_and_the_codex_section_keeps_the_save_byte_identical()
    {
        var (w, book) = Make(2);
        var m = w.Region.Landmarks![0];
        book.Visit(m.X, m.Y);
        var progress = book.ToProgress();
        Assert.DoesNotContain(progress.Unlocked, id => id.StartsWith("rule."));
        Assert.Contains("landmark." + m.Index, progress.Unlocked);

        var fresh = CodexBook.Build(w.History, w.Region, Rules);
        Assert.False(fresh.Get("landmark." + m.Index)!.Unlocked);
        fresh.Apply(progress);
        Assert.Equal(book.UnlockedCount, fresh.UnlockedCount);
        fresh.Apply(new CodexProgress { Unlocked = { "no.such.entry" } }); // unknown ids are ignored
        Assert.Equal(book.UnlockedCount, fresh.UnlockedCount);

        // through the world save
        var pop = Populator.Populate(w.Region, w.History, w.Content);
        var world = new WorldState
        {
            ContinentSettings = Settings(w, out var hs, out var rs), HistorySettings = hs, RegionSettings = rs, EmbarkCellX = w.Pick.CellX, EmbarkCellY = w.Pick.CellY,
            Continent = w.Continent, History = w.History, Population = pop, Clock = new SimClock(GameTime.FromCalendar(pop.NowYear, Season.Spring, 1, 8)),
            Ids = new IdAllocator(), Chunks = new ChunkStore(), Chronicle = new Chronicle(), Codex = progress,
            Hero = HeroRecord.FromOrigin("Edda Cole", true, w.Content.Get<Ravensguard.Content.OriginDef>("peddler")),
        };
        var bytes = SaveCodec.Encode(WorldSave.Capture(world, w.ContentHash, "0.1.0"));
        var loaded = WorldSave.Restore(SaveCodec.Decode(bytes), w.Content, w.ContentHash);
        Assert.Equal(progress.Unlocked, loaded.Codex!.Unlocked);
        Assert.Equal("Edda Cole", loaded.Hero!.Name);
        Assert.NotNull(loaded.Population.Region.Landmarks);
        Assert.Equal(w.Region.Landmarks!.Count, loaded.Population.Region.Landmarks!.Count);
        var again = SaveCodec.Encode(WorldSave.Capture(loaded, w.ContentHash, "0.1.0"));
        Assert.Equal(bytes, again);
        // a world without hero or codex writes neither section
        var bare = WorldSave.Capture(new WorldState
        {
            ContinentSettings = world.ContinentSettings, HistorySettings = hs, RegionSettings = rs, EmbarkCellX = world.EmbarkCellX, EmbarkCellY = world.EmbarkCellY,
            Continent = w.Continent, History = w.History, Population = pop, Clock = world.Clock, Ids = new IdAllocator(), Chunks = new ChunkStore(), Chronicle = new Chronicle(),
        }, w.ContentHash, "0.1.0");
        Assert.DoesNotContain(WorldSave.HeroSection, bare.Sections.Keys);
        Assert.DoesNotContain(WorldSave.CodexSection, bare.Sections.Keys);
        Assert.Contains(WorldSave.HeroSection, SaveCodec.Decode(bytes).Sections.Keys);
    }

    private static Ravensguard.Sim.WorldGen.ContinentSettings Settings(TestWorld w, out Ravensguard.Sim.WorldGen.History.HistorySettings hs, out RegionSettings rs)
    {
        var cs = Ravensguard.Sim.WorldGen.ContinentSettings.LoadDefaults(w.ContentDir); cs.Seed = w.Seed;
        hs = Ravensguard.Sim.WorldGen.History.HistorySettings.LoadDefaults(w.ContentDir); hs.Seed = w.Seed;
        rs = w.RegionSettings;
        return cs;
    }
}
