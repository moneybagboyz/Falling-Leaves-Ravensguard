#pragma warning disable xUnit1031 // these tests drive the tick worker on purpose and wait for its tasks
using Ravensguard.Content;
using Ravensguard.Sim.Characters;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Dialogue;
using Ravensguard.Sim.People;
using Ravensguard.Sim.Rumours;
using Ravensguard.Sim.Save;
using Ravensguard.Sim.Tests.Economy;
using Ravensguard.Sim.Tests.Seamless;
using Ravensguard.Sim.Tests.Trade;
using Ravensguard.Sim.WorldGen.Region;
using Ravensguard.Sim.Worlds;

namespace Ravensguard.Sim.Tests.Dialogue;

/// <summary>The talk systems on the real world: the hour barrier seeds and spreads rumours the same at 1x and 8x, and the section round-trips in a real save.</summary>
public class TalkRuntimeTests
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
        var pop = Populator.Populate(region, w.History, w.Content);
        var origin = w.Content.All<OriginDef>().OrderBy(o => o.Order).First();
        var hero = HeroRecord.FromOrigin("Aldric Vane", false, origin);
        return (w, NewGameWorld.Create(w.Content, w.Continent, w.History, cs, hs, w.Settings, home.CellX, home.CellY, pop, hero, null));
    }

    private static (ulong Hash, int Hearings, int Rumours, int Bad) Play(int gameSecPerTick)
    {
        var (w, s) = Game();
        string place = s.Population.Region.Settlements.OrderByDescending(x => s.Population.SettlementPeople[s.Population.Region.Settlements.IndexOf(x)]).First().Name;
        long start = s.Abstract!.HourDone * 3600;
        s.Chronicle.Add(new ChronicleEntry(s.Ids.Allocate(IdKind.Event), new GameTime(start), "famine", [], [new Cause(CauseKind.Famine, null, "two poor harvests running")],
            new Dictionary<string, string> { ["settlement"] = place, ["hungry_households"] = "14", ["households"] = "60" }));
        using var rt = new WorldRuntime(s, w.Content, new MemoryRegionPager());
        var link = TalkLink.Attach(rt, LineBook.From(w.Content));
        rt.Start();
        var home = s.HomeRegion; long end = start + 40 * 3600;
        for (long t = start; t <= end; t += gameSecPerTick) rt.Pump(new GameTime(t), home.OriginXM + 16000, home.OriginYM + 16000);
        while (rt.HourDone < end / 3600) { rt.Pump(new GameTime(end), home.OriginXM + 16000, home.OriginYM + 16000); Thread.Sleep(2); }
        rt.Quiesce();
        Assert.Null(rt.Fault);
        return rt.AtBarrier(() =>
        {
            var ts = s.Talk!; int bad = 0;
            foreach (var (rid, _) in ts.Rumours.Rumours)
                foreach (var h in ts.Rumours.HearingsOf(rid))
                    if (!RumourTrace.Valid(ts.Rumours, s.Chronicle, rid, RumourTrace.Walk(ts.Rumours, rid, h.Listener), out _)) bad++;
            return (ts.Hash(), ts.Rumours.HearingCount, ts.Rumours.Count, bad);
        }).GetAwaiter().GetResult();
    }

    [Fact]
    public void The_hour_barrier_seeds_and_spreads_the_same_rumours_at_1x_and_8x_and_every_hearing_traces()
    {
        var a = Play(1); var b = Play(8);
        Assert.Equal(a, b);
        Assert.True(a.Rumours >= 1, "the famine entry made no rumour");
        Assert.True(a.Hearings > 5, $"only {a.Hearings} hearings after 40 hours");
        Assert.Equal(0, a.Bad);
    }

    [Fact]
    public void The_talk_section_is_in_the_real_save_and_save_load_save_is_byte_identical_with_rumours_in_flight()
    {
        var w = new TradeWorld(); var st = w.State;
        string place = st.Population.Region.Settlements[0].Name; long hour = st.Clock.Now.TotalHours;
        st.Chronicle.Add(new ChronicleEntry(st.Ids.Allocate(IdKind.Event), new GameTime(hour * 3600), "famine", [], [new Cause(CauseKind.Famine, null, "no grain")], new Dictionary<string, string> { ["settlement"] = place, ["hungry_households"] = "9" }));
        var tw = TalkWorld.From(st); st.Talk = new TalkState();
        for (long h = hour + 1; h < hour + 72; h++) TalkSystem.AfterHour(tw, st.Talk, LineBook.Default, h);
        Assert.True(st.Talk.Rumours.HearingCount > 5);
        int teller = st.Talk.Rumours.Held.Keys.Min();
        TalkSystem.HeroHears(tw, st.Talk, st.Talk.Rumours.HeldBy(teller)[0], teller, hour + 72, HearHow.Told);
        st.Markets = null;
        var bytes = SaveCodec.Encode(WorldSave.Capture(st, EconFixture.ContentHash, "0.1.0"));
        var back = WorldSave.Restore(SaveCodec.Decode(bytes), EconFixture.Content, EconFixture.ContentHash);
        Assert.NotNull(back.Talk);
        Assert.Equal(st.Talk.Hash(), back.Talk!.Hash());
        Assert.Equal(bytes, SaveCodec.Encode(WorldSave.Capture(back, EconFixture.ContentHash, "0.1.0")));
        Assert.Single(back.Talk.Heard);
    }

    [Fact]
    public void The_hero_asks_the_news_through_the_link_and_the_journal_card_traces_to_the_chronicle()
    {
        var (w, s) = Game();
        string place = s.Population.Region.Settlements.OrderByDescending(x => s.Population.SettlementPeople[s.Population.Region.Settlements.IndexOf(x)]).First().Name;
        long start = s.Abstract!.HourDone * 3600;
        s.Chronicle.Add(new ChronicleEntry(s.Ids.Allocate(IdKind.Event), new GameTime(start), "outbreak", [], [new Cause(CauseKind.Disease, null, "winter coughs and fevers")], new Dictionary<string, string> { ["settlement"] = place, ["severity"] = "mild" }));
        using var rt = new WorldRuntime(s, w.Content, new MemoryRegionPager());
        var link = TalkLink.Attach(rt); rt.Start();
        var home = s.HomeRegion; long end = start + 30 * 3600;
        for (long t = start; t <= end; t += 4) rt.Pump(new GameTime(t), home.OriginXM + 16000, home.OriginYM + 16000);
        while (rt.HourDone < end / 3600) { rt.Pump(new GameTime(end), home.OriginXM + 16000, home.OriginYM + 16000); Thread.Sleep(2); }
        rt.Quiesce();
        var teller = rt.AtBarrier(() => s.Talk!.Rumours.Held.Keys.OrderBy(k => k).First(k => s.Talk.Rumours.HeldBy(k).Count > 0)).GetAwaiter().GetResult();
        var id = s.Population.People.IdOf(teller);
        var speaker = link.Speaker(id).GetAwaiter().GetResult()!;
        Assert.Equal(teller, speaker.Index); Assert.InRange(speaker.Mood, -100, 100);
        var ans = link.AskNews(id).GetAwaiter().GetResult();
        Assert.NotNull(ans.Card);
        var card = ans.Card!;
        Assert.True(card.Source.Found); Assert.Equal("outbreak", card.Source.TypeId); Assert.NotEmpty(card.Steps); Assert.Contains("winter coughs", string.Join(" ", card.Source.Causes));
        Assert.Single(link.View!.Cards);
        Assert.Null(link.AskNews(id).GetAwaiter().GetResult().Card is { } c2 && c2.Rumour == card.Rumour ? c2 : null);   // the same rumour is not told twice
        Assert.True(link.Apply("set_flag", new Dictionary<string, string> { ["flag"] = "x" }, id).GetAwaiter().GetResult());
        Assert.Contains("x", link.Flags().GetAwaiter().GetResult());
    }
}
