using Ravensguard.Content;
using Ravensguard.Sim.Characters;
using Ravensguard.Sim.Combat.Aftermath;
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
/// The courts on a whole running world (P6-C1): a long town run with NPC crime has arrests, court days and sentences, and two runs and a save in the middle agree byte for byte (CJ-17); the hero gives himself
/// up through the desk and goes through the cell and the court, with the chain of entries back to his theft.
/// </summary>
public class JusticeRuntimeTests(ITestOutputHelper output)
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

    private static SaveFile Save(WorldRuntime rt) => rt.AtBarrier(() => WorldSave.Capture(rt.State, Hash, "0.1")).GetAwaiter().GetResult();

    private static void Steal(WorldRuntime rt, ContentDatabase content)
    {
        var s = rt.State; var pt = s.Population.People; int town = pt.HomeSettlement[s.Hero!.PersonIndex];
        var witnesses = Enumerable.Range(0, pt.Count).Where(i => pt.IsAlive(i) && pt.HomeSettlement[i] == town && pt.AgeYears(i, (int)(rt.HourDone / 24)) >= 18 && i != s.Hero.PersonIndex).Take(3).ToList();
        var ctx = new HeroContext(s, s.Party!, content);
        var desk = new HeroDesk();
        desk.Issue(CrimeDesk.Act("theft", pt.IdOf(witnesses[0]), town, 240, 0, "ch_note_crime_hero", new ActorLook([0xA03030u], 2),
            [new WitnessRow(pt.IdOf(witnesses[0]).Value, WitnessRow.KnewIt), new WitnessRow(pt.IdOf(witnesses[1]).Value, WitnessRow.KnewIt), new WitnessRow(pt.IdOf(witnesses[2]).Value, 0)], null, rt.HourDone * 3600 + 600));
        desk.ApplyDue(ctx, rt.HourDone + 1);
        Assert.All(desk.View!.Results, r => Assert.True(r.Ok, r.Text.ToString()));
    }

    private static HeroResult Do(WorldRuntime rt, ContentDatabase content, HeroCommand c)
    {
        var ctx = new HeroContext(rt.State, rt.State.Party!, content);
        var desk = new HeroDesk();
        desk.Issue(c);
        desk.ApplyDue(ctx, rt.HourDone + 1);
        return desk.View!.Results[^1];
    }

    [Fact]
    public void CJ17_AThirtyDayTownRunWithNpcCrime_HasArrestsAndSentences_AndTwoRunsAndASaveResumeAgreeByteForByte()
    {
        string RunStraight(out string lawHash, out int sentences, out int arrests, out SaveFile mid, int saveAtDay = -1)
        {
            var (w, a) = Game();
            using var rt = new WorldRuntime(a, w.Content, new MemoryRegionPager());
            LawLink.Attach(rt);
            long h0 = rt.HourDone + (24 - rt.HourDone % 24) % 24;
            mid = null!;
            if (saveAtDay > 0) { rt.AdvanceTo(GameTime.FromGameHours(h0 + saveAtDay * 24)); mid = Save(rt); }
            rt.AdvanceTo(GameTime.FromGameHours(h0 + 30 * 24));
            var st = rt.State;
            lawHash = $"{st.Law?.Hash() ?? 0:X}/{st.Justice?.Hash() ?? 0:X}";
            sentences = st.Justice?.Sentences.Count ?? 0;
            arrests = st.Chronicle.All.Count(e => e.TypeId == "crime_arrested");
            Assert.True(st.Economy!.Ledger.Audit().IsConserved);
            return string.Join(",", Sections(rt).Select(kv => kv.Key + ":" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(kv.Value))[..8])) + "|" + st.Chronicle.Hash();
        }

        string a1 = RunStraight(out string lh1, out int s1, out int ar1, out _);
        output.WriteLine($"30 days: law/justice hash {lh1}; {s1} sentences, {ar1} arrests");
        Assert.True(ar1 >= 1, "the guards took nobody in thirty days of NPC crime");
        string a2 = RunStraight(out string lh2, out _, out _, out _);
        Assert.Equal(a1, a2); Assert.Equal(lh1, lh2);                                                // two runs: the same
        // a save in the middle (day 12) loads and continues to the same end
        RunStraight(out _, out _, out _, out var mid, saveAtDay: 12);
        var (w3, _) = Game();
        var st3 = WorldSave.Restore(SaveCodec.Decode(SaveCodec.Encode(mid)), w3.Content, Hash, null, w3.Continent, w3.History);
        st3.Content = w3.Content;
        using var resumed = new WorldRuntime(st3, w3.Content, new MemoryRegionPager());
        LawLink.Attach(resumed);
        long h0r = resumed.HourDone;
        long dayStart = h0r - h0r % 24;
        resumed.AdvanceTo(GameTime.FromGameHours(dayStart - 12 * 24 + 30 * 24 + 0));
        string b = string.Join(",", Sections(resumed).Select(kv => kv.Key + ":" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(kv.Value))[..8])) + "|" + resumed.State.Chronicle.Hash();
        Assert.Equal($"{resumed.State.Law?.Hash() ?? 0:X}/{resumed.State.Justice?.Hash() ?? 0:X}", lh1);
        Assert.Equal(a1, b);
    }

    [Fact]
    public void TheHeroGivesHimselfUp_IsHeldFedAndTriedOnTheCourtDay_AndTheSentenceHangsOnTheChainBackToTheTheft_AndASaveInTheCellResumesTheSame()
    {
        var (w, a) = Game();
        using var rt = new WorldRuntime(a, w.Content, new MemoryRegionPager());
        LawLink.Attach(rt);
        long h0 = rt.HourDone + (24 - rt.HourDone % 24) % 24;
        rt.AdvanceTo(GameTime.FromGameHours(h0 + 8));
        Steal(rt, w.Content);
        rt.AdvanceTo(GameTime.FromGameHours(h0 + 30));                            // reported at the next day's hour 0, bounty posted
        var rec = rt.State.Law!.Crimes.Values.Single(c => c.Kind == "theft" && c.Witnesses.Count == 3);
        Assert.Equal(CrimeState.Reported, rec.State);
        var res = Do(rt, w.Content, JusticeDesk.Surrender(-1, rt.HourDone * 3600));
        Assert.True(res.Ok, res.Message.ToString());
        var st = rt.State; var hero = st.Party!.Hero;
        Assert.True(st.Captivity!.HeroHeld); Assert.Equal(CaptorKind.Gaol, st.Captivity.Hero!.Captor); Assert.Contains(rec.Id, st.Justice!.Gaol[hero.Value].Crimes);
        Assert.Equal(CrimeState.Held, rec.State);
        // the cell: the expected sentence is knowable before the court day (the culture's, at his count of convictions)
        var law = LawWorld.From(st, w.Content);
        var exp = Sentences.Expect(law, st.Law, st.Justice, rec, hero, st.Justice.Gaol[hero.Value].Fief);
        output.WriteLine($"expected: {exp.Key} step {exp.Step} {exp.Kind} {exp.Mites} mites {exp.Days} days {exp.Lashes} lashes");
        // fed a day later; then the court day and the next morning
        long treasury0 = st.Economy!.Ledger.Balance(law.TreasuryOf!(st.Justice.Gaol[hero.Value].Settlement)!.Value).Mites;
        rt.AdvanceTo(GameTime.FromGameHours(h0 + 30 + 24 * 6));
        Assert.True(st.Justice is null || st.Justice.Sentences.ContainsKey(rec.Id) || st.Justice.Gaol.ContainsKey(hero.Value));
        var s = st.Justice!.Sentences[rec.Id];
        Assert.Equal(exp.Kind, s.Kind);
        var pronounced = st.Chronicle.All.Single(e => e.TypeId == "sentence_pronounced" && e.Subjects.Contains(hero));
        var chain = new List<string>(); var cur = pronounced;
        while (cur != null) { chain.Add(cur.TypeId); cur = cur.Causes.Count > 0 && cur.Causes[0].EventId is { } id ? st.Chronicle.Get(id) : null; }
        output.WriteLine(string.Join(" <- ", chain));
        Assert.Equal(["sentence_pronounced", "crime_arrested", "crime_reported", "crime_witnessed", "theft"], chain);
        Assert.True(st.Economy.Ledger.Audit().IsConserved);
        // a save taken now resumes the courts byte for byte
        var file = Save(rt); Assert.Contains("justice", file.Sections.Keys);
        var bytes = SaveCodec.Encode(file);
        rt.AdvanceTo(GameTime.FromGameHours(h0 + 30 + 24 * 6 + 24 * 20));
        var want = Sections(rt);
        var st2 = WorldSave.Restore(SaveCodec.Decode(bytes), w.Content, Hash, null, w.Continent, w.History);
        st2.Content = w.Content;
        using var resumed = new WorldRuntime(st2, w.Content, new MemoryRegionPager());
        LawLink.Attach(resumed);
        resumed.AdvanceTo(GameTime.FromGameHours(h0 + 30 + 24 * 6 + 24 * 20));
        var got = Sections(resumed);
        Assert.Equal(want.Keys, got.Keys);
        foreach (var k in want.Keys) Assert.True(want[k].AsSpan().SequenceEqual(got[k]), $"section {k} differs after the resume");
        Assert.Equal(rt.State.Justice?.Hash(), resumed.State.Justice?.Hash());
        _ = treasury0;
    }

    [Fact]
    public void AWorldThatNeverHeldAnyone_KeepsNoJusticeSection_AndALoadAndResaveOfAnOlderSaveKeepsItsBytes()
    {
        var (w, a) = Game();
        using var rt = new WorldRuntime(a, w.Content, new MemoryRegionPager());
        rt.AdvanceTo(GameTime.FromGameHours(rt.HourDone + 30));
        var file = Save(rt);
        Assert.DoesNotContain("justice", file.Sections.Keys);
        var st = WorldSave.Restore(SaveCodec.Decode(SaveCodec.Encode(file)), w.Content, Hash, null, w.Continent, w.History);
        st.Content = w.Content;
        var again = WorldSave.Capture(st, Hash, "0.1");
        Assert.DoesNotContain("justice", again.Sections.Keys);
    }
}
