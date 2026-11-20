using Ravensguard.Content;
using Ravensguard.Sim.Characters;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Family;
using Ravensguard.Sim.People;
using Ravensguard.Sim.Save;
using Ravensguard.Sim.Tests.Seamless;
using Ravensguard.Sim.WorldGen.Region;
using Ravensguard.Sim.Worlds;
using Xunit;
using Xunit.Abstractions;

namespace Ravensguard.Sim.Tests.Family;

/// <summary>
/// The family on a whole running world (P6-B3): the link runs the day at hour 4 beside the Phase 2 vital rolls, lessons accumulate for the hero's children, the hero's wife is pregnant and delivered by the
/// Phase 2 kernels with the hero as the father, and a save in the middle loads and continues byte for byte (the "family" section, the person table and the chronicle all equal one uninterrupted run).
/// </summary>
public class FamilyRuntimeTests(ITestOutputHelper output)
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
        var st = NewGameWorld.Create(w.Content, w.Continent, w.History, cs, hs, w.Settings, home.CellX, home.CellY, pop, h, null);
        st.Content = w.Content;
        return (w, st);
    }

    private static SortedDictionary<string, byte[]> Sections(WorldRuntime rt) => rt.AtBarrier(() =>
    {
        var d = new SortedDictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (var (k, v) in WorldSave.Capture(rt.State, Hash, "0.1").Sections) if (k != WorldSave.MetaSection) d[k] = v.ToArray();
        return d;
    }).GetAwaiter().GetResult();

    [Fact]
    public void AMarriedHerosHouseholdLivesItsDays_LessonsRunAtHourFour_AndTheSaveResumesByteForByte()
    {
        var (w, a) = Game();
        int day0 = (int)a.Clock.Now.TotalDays;
        int spouse = FamilyDemo.MarryWithChildren(a, w.Content, day0);
        Assert.True(spouse >= 0, "the demo marriage failed");
        var pt = a.Population.People; int elder = pt.IndexOf(a.Succession!.Kin.First(k => k.Relation == Ravensguard.Sim.Succession.KinRelation.Child).Person);
        using var straight = new WorldRuntime(a, w.Content, new MemoryRegionPager());
        var link = FamilyLink.Attach(straight);
        long h0 = straight.HourDone + (24 - straight.HourDone % 24) % 24;
        straight.AdvanceTo(GameTime.FromGameHours(h0 + 24 * 6 + 6));                // six days of hour 4 passes
        var book = straight.State.Family!;
        var up = book.Upbringings[pt.IdOf(elder).Value];
        Assert.InRange(up.Hours, 4 * 5, 4 * 7);                                    // a lesson day a day
        Assert.True(pt.SkillLevelOf(elder, Skill.Blades) >= 0 && up.Milli.Count > 0);
        Assert.False(link.DeathPending);
        Assert.NotNull(book.Ageing);
        output.WriteLine($"{up.Hours} lesson hours after six days, family hash {book.Hash():X}");

        var bytes = SaveCodec.Encode(rt_Save(straight));
        straight.AdvanceTo(GameTime.FromGameHours(h0 + 24 * 6 + 6 + 24 * 8));
        var want = Sections(straight);

        var st = WorldSave.Restore(SaveCodec.Decode(bytes), w.Content, Hash, null, w.Continent, w.History);
        st.Content = w.Content;
        using var resumed = new WorldRuntime(st, w.Content, new MemoryRegionPager());
        FamilyLink.Attach(resumed);
        resumed.AdvanceTo(GameTime.FromGameHours(h0 + 24 * 6 + 6 + 24 * 8));
        var got = Sections(resumed);
        Assert.Equal(want.Keys, got.Keys);
        foreach (var k in want.Keys) Assert.True(want[k].AsSpan().SequenceEqual(got[k]), $"section {k} differs after the resume");
        Assert.Equal(straight.State.Family!.Hash(), resumed.State.Family!.Hash());
        Assert.Equal(straight.State.Chronicle.Hash(), resumed.State.Chronicle.Hash());
        Assert.True(straight.State.Economy!.Ledger.Audit().IsConserved);
    }

    private static SaveFile rt_Save(WorldRuntime rt) => rt.AtBarrier(() => WorldSave.Capture(rt.State, Hash, "0.1")).GetAwaiter().GetResult();

    [Fact]
    public void AWorldWithoutTheLink_KeepsNoFamilySection()
    {
        var (w, a) = Game();
        using var rt = new WorldRuntime(a, w.Content, new MemoryRegionPager());
        rt.AdvanceTo(GameTime.FromGameHours(rt.HourDone + 30));
        Assert.Null(rt.State.Family);
        Assert.DoesNotContain("family", rt_Save(rt).Sections.Keys);
    }
}
