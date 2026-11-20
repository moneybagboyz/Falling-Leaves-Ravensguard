using Ravensguard.Content;
using Ravensguard.Sim.Characters;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Jobs;
using Ravensguard.Sim.Law;
using Ravensguard.Sim.People;
using Ravensguard.Sim.Save;
using Ravensguard.Sim.Tests.Seamless;
using Ravensguard.Sim.Trade;
using Ravensguard.Sim.WorldGen.Region;
using Ravensguard.Sim.Worlds;
using Xunit;
using Xunit.Abstractions;

namespace Ravensguard.Sim.Tests.Law;

/// <summary>
/// The law on a whole running world (P6-B2): the hero's act goes through the desk as a recorded command, the day's pass reports it and the lord posts a bounty from the real fief treasury, NPC crime
/// runs at hour 4, and a save in the middle loads and continues byte for byte (the "law" section, the jobs and the chronicle all equal one uninterrupted run).
/// </summary>
public class LawRuntimeTests(ITestOutputHelper output)
{
    private const string Hash = "h";

    private static (SeamWorld W, WorldState State) Game()
    {
        var w = SeamWorld.Get(1234);
        var home = w.BlockOrigin.Offset(1, 1);
        var dir = Ravensguard.Sim.WorldGen.ContinentSettings.FindBaseContent()!;
        var cs = Ravensguard.Sim.WorldGen.ContinentSettings.LoadDefaults(dir); cs.Seed = 1234;
        var hs = Ravensguard.Sim.WorldGen.History.HistorySettings.LoadDefaults(dir); hs.Seed = 1234;
        var region = RegionGenerator.Generate(w.Continent, w.History, home, w.Settings);
        LandmarkPlacer.Place(region, w.History, w.Content);
        var pop = Populator.Populate(region, w.History, w.Content);
        var h = HeroRecord.FromOrigin("Aldric Vane", false, w.Content.All<OriginDef>().OrderBy(o => o.Order).First());
        return (w, NewGameWorld.Create(w.Content, w.Continent, w.History, cs, hs, w.Settings, home.CellX, home.CellY, pop, h, null));
    }

    private static SortedDictionary<string, byte[]> Sections(WorldRuntime rt) => rt.AtBarrier(() =>
    {
        var d = new SortedDictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (var (k, v) in WorldSave.Capture(rt.State, Hash, "0.1").Sections) if (k != WorldSave.MetaSection) d[k] = v.ToArray();
        return d;
    }).GetAwaiter().GetResult();

    /// <summary>The hero lifts something worth 240 mites in his home town under the eyes of three townsfolk, through the desk as the game sends it.</summary>
    private static void Steal(WorldRuntime rt, ContentDatabase content)
    {
        var s = rt.State; var pt = s.Population.People; int town = pt.HomeSettlement[s.Hero!.PersonIndex];
        var witnesses = Enumerable.Range(0, pt.Count).Where(i => pt.IsAlive(i) && pt.HomeSettlement[i] == town && pt.Status[i] == PersonStatus.Alive && pt.AgeYears(i, (int)(rt.HourDone / 24)) >= 18 && i != s.Hero.PersonIndex).Take(3).ToList();
        var ctx = new HeroContext(s, s.Party!, content);
        var desk = new HeroDesk();
        desk.Issue(CrimeDesk.Act("theft", pt.IdOf(witnesses[0]), town, 240, 0, "ch_note_crime_hero", new ActorLook([0xA03030u], 2),
            [new WitnessRow(pt.IdOf(witnesses[0]).Value, WitnessRow.KnewIt), new WitnessRow(pt.IdOf(witnesses[1]).Value, WitnessRow.KnewIt), new WitnessRow(pt.IdOf(witnesses[2]).Value, 0)], null, rt.HourDone * 3600 + 600));
        desk.ApplyDue(ctx, rt.HourDone + 1);
        Assert.All(desk.View!.Results, r => Assert.True(r.Ok, r.Text.ToString()));
    }

    [Fact]
    public void AHeroTheftThroughTheDesk_IsReportedAtTheNextDaysStart_PostsABountyFromTheFiefTreasury_AndTheSaveResumesByteForByte()
    {
        var (w, a) = Game();
        using var straight = new WorldRuntime(a, w.Content, new MemoryRegionPager());
        LawLink.Attach(straight);
        long h0 = straight.HourDone + (24 - straight.HourDone % 24) % 24;          // a day's first hour
        straight.AdvanceTo(GameTime.FromGameHours(h0 + 8));
        Steal(straight, w.Content);
        var rec = straight.State.Law!.Crimes.Values.Single(c => c.Kind == "theft" && c.Witnesses.Count == 3);
        Assert.Equal(CrimeState.Seen, rec.State); Assert.True(rec.Known);
        straight.AdvanceTo(GameTime.FromGameHours(h0 + 30));                       // past the next day's hour 0
        Assert.Equal(CrimeState.Reported, rec.State);
        var job = straight.State.Jobs!.Get(new EntityId(rec.BountyJob))!;
        Assert.Equal(OddJobKind.Bounty, job.Kind); Assert.Equal(580, job.WageMites);
        Assert.True(straight.State.Economy!.Ledger.Audit().IsConserved);
        Assert.Equal(580, straight.State.Economy.Ledger.Balance(new PurseId(job.Escrow)).Mites);        // escrowed from the treasury, not spent
        output.WriteLine($"{straight.State.Law.Crimes.Count} crimes after a day and a half ({straight.State.Law.Crimes.Values.Count(c => c.Kind == "theft")} thefts), law hash {straight.State.Law.Hash():X}");

        var file = rt_Save(straight);
        Assert.Contains("law", file.Sections.Keys);
        var bytes = SaveCodec.Encode(file);
        straight.AdvanceTo(GameTime.FromGameHours(h0 + 30 + 72));
        var want = Sections(straight);

        var st = WorldSave.Restore(SaveCodec.Decode(bytes), w.Content, Hash, null, w.Continent, w.History);
        st.Content = w.Content;
        using var resumed = new WorldRuntime(st, w.Content, new MemoryRegionPager());
        LawLink.Attach(resumed);
        resumed.AdvanceTo(GameTime.FromGameHours(h0 + 30 + 72));
        var got = Sections(resumed);
        Assert.Equal(want.Keys, got.Keys);
        foreach (var k in want.Keys) Assert.True(want[k].AsSpan().SequenceEqual(got[k]), $"section {k} differs after the resume");
        Assert.Equal(straight.State.Law!.Hash(), resumed.State.Law!.Hash());
        Assert.Equal(straight.State.Chronicle.Hash(), resumed.State.Chronicle.Hash());
    }

    private static SaveFile rt_Save(WorldRuntime rt) => rt.AtBarrier(() => WorldSave.Capture(rt.State, Hash, "0.1")).GetAwaiter().GetResult();

    [Fact]
    public void AWorldThatNeverSawACrime_KeepsNoLawSection()
    {
        var (w, a) = Game();
        using var rt = new WorldRuntime(a, w.Content, new MemoryRegionPager());
        // no link attached: nothing writes the book
        rt.AdvanceTo(GameTime.FromGameHours(rt.HourDone + 30));
        Assert.Null(rt.State.Law);
        Assert.DoesNotContain("law", rt_Save(rt).Sections.Keys);
    }
}
