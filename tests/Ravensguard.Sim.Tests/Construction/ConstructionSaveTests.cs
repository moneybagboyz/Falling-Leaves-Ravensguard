using Ravensguard.Content;
using Ravensguard.Sim.Characters;
using Ravensguard.Sim.Construction;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Goods;
using Ravensguard.Sim.Land;
using Ravensguard.Sim.Save;
using Ravensguard.Sim.Tests.Save;
using Ravensguard.Sim.Tests.Seamless;
using Ravensguard.Sim.Trade;
using Ravensguard.Sim.WorldGen.Region;
using Ravensguard.Sim.Worlds;
using Xunit.Abstractions;

namespace Ravensguard.Sim.Tests.Construction;

/// <summary>BC-18 and the save rules (overview 7): the optional "construction" section, save, load, save byte for byte with ghosts in progress and a groan pending; the loaded world collapses at the same game second.</summary>
public class ConstructionSaveTests(ITestOutputHelper output)
{
    private static (SeamWorld W, WorldState State) Game()
    {
        var w = SeamWorld.Get(1234);
        var home = w.BlockOrigin.Offset(1, 1);
        var dir = Ravensguard.Sim.WorldGen.ContinentSettings.FindBaseContent()!;
        var cs = Ravensguard.Sim.WorldGen.ContinentSettings.LoadDefaults(dir); cs.Seed = 1234;
        var hs = Ravensguard.Sim.WorldGen.History.HistorySettings.LoadDefaults(dir); hs.Seed = 1234;
        var region = RegionGenerator.Generate(w.Continent, w.History, home, w.Settings);
        LandmarkPlacer.Place(region, w.History, w.Content);
        var pop = Ravensguard.Sim.People.Populator.Populate(region, w.History, w.Content);
        var origin = w.Content.All<OriginDef>().OrderBy(o => o.Order).First();
        var hero = HeroRecord.FromOrigin("Aldric Vane", false, origin);
        return (w, NewGameWorld.Create(w.Content, w.Continent, w.History, cs, hs, w.Settings, home.CellX, home.CellY, pop, hero, null));
    }

    private static T Barrier<T>(WorldRuntime rt, Func<T> f) => rt.AtBarrier(f).GetAwaiter().GetResult();

    [Fact]
    public void BC18_SaveMidSite_WithGhostsInProgressAndAGroanPending_SaveLoadSaveIsByteIdentical_AndTheCollapseComesAtTheSameSecond()
    {
        var (w, a) = Game();
        var content = w.Content;
        using var ra = new WorldRuntime(a, content, new MemoryRegionPager());
        var cw = a.HomeRecords!.Sim.Get<ConstructionWorld>()!;
        Assert.NotNull(cw);
        Assert.True(a.Construction!.IsEmpty);
        // a world that never built saves no construction section
        Assert.DoesNotContain(ConstructionSave.SectionName, WorldSave.Capture(a, "h", "0.1").Sections.Keys);
        var ctx = new HeroContext(a, a.Party!, content);
        a.Economy!.Ledger.Mint(ctx.Purse, Coin.FromCrowns(3000), SourceReason.Seed);
        var desk = new HeroDesk();
        long sec = ra.HourDone * 3600;
        var lw = a.HomeRecords.Sim.Get<LandWorld>()!;
        var reg = a.Population.Region;
        int town = reg.Settlements.FindIndex(x => x.Class == SettlementClass.Town && lw.Listings(reg.Settlements.IndexOf(x)).Count > 0);
        var lot = lw.Listings(town)[0];
        int x0 = lot.Rect.X + 1, y0 = lot.Rect.Y + 1;
        desk.Issue(LandDesk.Buy(lot, sec));
        desk.Issue(ConstructionDesk.Plan("test_cottage", x0, y0, 0, false, sec));
        desk.Issue(ConstructionDesk.Place("post_timber", x0 + 1, y0 + 8, x0 + 1, y0 + 8, 0, "one", 0, sec));
        desk.Issue(ConstructionDesk.Place("floor_plank", x0 + 1, y0 + 9, x0 + 2, y0 + 9, 1, "line", 0, sec));
        desk.ApplyDue(ctx, ra.HourDone);
        Assert.All(desk.View!.Results, r => Assert.True(r.Ok, r.Text));
        var cottage = a.Construction.Sites.Values.First(s => s.Blueprint == "test_cottage");
        Assert.Single(a.Construction.Sites);                                                  // the post and the floor join the cottage's site (the owner's site within 24 m)
        // materials: logs carried in the pack and unloaded at the pile through the desk; the rest put into the piles (the test's faucet)
        ctx.Pack.Create(a.Economy.Goods.IdOf("logs"), 20_000, CreateReason.Import);
        desk.Issue(ConstructionDesk.Deliver(cottage.Id, "logs", 20_000, cottage.PileX, cottage.PileY, sec));
        foreach (var s in new[] { cottage })
        {
            var pile = cw.PileOf(s);
            foreach (var (g, q) in cw.Needed(s)) { long need = q - pile.Qty(g) - (g == a.Economy.Goods.IdOf("logs") && s == cottage ? 20_000 : 0); if (need > 0) pile.Create(g, need, CreateReason.Import); }
        }
        desk.Issue(ConstructionDesk.Hire(cottage.Id, 4, false, sec));
        desk.Issue(ConstructionDesk.Hire(cottage.Id, 2, true, sec));
        desk.ApplyDue(ctx, ra.HourDone);
        Assert.All(desk.View!.Results, r => Assert.True(r.Ok, r.Text));
        Assert.Equal(6, cottage.Crew.Count);
        ra.AdvanceTo(GameTime.FromGameHours(ra.HourDone + 60));
        Assert.True(cw.BuiltAt(x0 + 1, y0 + 9, 1) != null, "the upper floor stands");
        Assert.True(cw.GhostsOf(cottage.Id).Any(g => g.WorkDone > 0 || g.Materialised) || cw.GhostsOf(cottage.Id).Any(), "the cottage is still being built");
        // the post goes five seconds before the hour's end: the floor groans across the save
        long end = (ra.HourDone + 1) * 3600;
        Barrier(ra, () => cw.Destroy(x0 + 1, y0 + 8, 0, end - 5, EntityId.None, "test"));
        var groan = Assert.Single(a.Construction.Groans);
        Assert.Equal(end + 5, groan.FallSec);
        var bytes1 = SaveCodec.Encode(WorldSave.Capture(a, "h", "0.1"));
        var file1 = SaveCodec.Decode(bytes1);
        Assert.Contains(ConstructionSave.SectionName, file1.Sections.Keys);
        output.WriteLine($"construction section {file1.Sections[ConstructionSave.SectionName].Length} bytes: {a.Construction.Ghosts.Count} ghosts, {a.Construction.Built.Count} built, {a.Construction.Sites.Count} sites");
        var b = WorldSave.Restore(file1, content, "h", null, w.Continent, w.History);
        b.Content = content;
        Assert.Equal(a.Construction.ComputeHash(), b.Construction!.ComputeHash());
        var bytes2 = SaveCodec.Encode(WorldSave.Capture(b, "h", "0.1"));
        var f2 = SaveCodec.Decode(bytes2);
        var diff = file1.Sections.Keys.Union(f2.Sections.Keys).Where(k => !file1.Sections.TryGetValue(k, out var x) || !f2.Sections.TryGetValue(k, out var y) || !x.Span.SequenceEqual(y.Span)).ToList();
        Assert.True(diff.Count == 0, "sections differ after save-load-save: " + string.Join(", ", diff));
        Assert.Equal(bytes1, bytes2);
        // go on: the loaded world and the one that never stopped
        using var rb = new WorldRuntime(b, content, new MemoryRegionPager());
        var cwb = b.HomeRecords!.Sim.Get<ConstructionWorld>()!;
        Assert.Equal(cw.Rooms.FactsHash(), cwb.Rooms.FactsHash());
        long target = ra.HourDone + 30;
        ra.AdvanceTo(GameTime.FromGameHours(target)); rb.AdvanceTo(GameTime.FromGameHours(target));
        Assert.Empty(a.Construction.Groans); Assert.Empty(b.Construction.Groans);
        Assert.Null(cw.BuiltAt(x0 + 1, y0 + 9, 1));
        static List<long> Collapses(WorldState s) => [.. s.Chronicle.ByType("collapse").Concat(ReferenceEquals(s.HomeRecords!.Chronicle, s.Chronicle) ? [] : s.HomeRecords.Chronicle.ByType("collapse")).Select(e => e.When.Seconds)];
        var ca = Collapses(a); var cb = Collapses(b);
        Assert.Equal(new[] { end + 5 }, ca);
        Assert.Equal(ca, cb);
        Assert.Equal(a.Construction.ComputeHash(), b.Construction.ComputeHash());
        Assert.Equal(cw.Rooms.FactsHash(), cwb.Rooms.FactsHash());
        var fa = WorldSave.Capture(a, "h", "0.1"); var fb = WorldSave.Capture(b, "h", "0.1");
        Assert.True(fa.Sections[ConstructionSave.SectionName].Span.SequenceEqual(fb.Sections[ConstructionSave.SectionName].Span));
    }
}
