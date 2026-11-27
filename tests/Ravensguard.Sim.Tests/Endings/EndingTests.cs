using Ravensguard.Content;
using Ravensguard.Sim.Autopilot;
using Ravensguard.Sim.Characters;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Endings;
using Ravensguard.Sim.People;
using Ravensguard.Sim.Save;
using Ravensguard.Sim.Tests.Autopilot;
using Ravensguard.Sim.Trade;
using Ravensguard.Sim.Travel;
using Ravensguard.Sim.Worlds;
using Xunit;
using Xunit.Abstractions;

namespace Ravensguard.Sim.Tests.Endings;

/// <summary>P8-EN-1 (endings spec 6): EN-1 the offer, EN-2 decline, EN-3 freeze, EN-4 beats, EN-5 same run same words, EN-6 the hero to the world; plus the library and the save.</summary>
public class EndingTests(ITestOutputHelper output)
{
    private static ConditionRefDef Cond(string id, params (string, string)[] args) => new() { Id = id, Args = args.ToDictionary(a => a.Item1, a => a.Item2) };
    private static AmbitionDef Great(string id, string ending, params ConditionRefDef[] steps) => new()
    {
        Id = id, Name = id.Replace('_', ' '), Description = "test", Tier = "great", Ending = ending,
        Steps = [.. steps.Select((c, i) => new AmbitionStepDef { Id = "s" + i, Name = "step " + i, Conditions = [c] })],
    };

    private static string Root()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "src", "Ravensguard.Sim", "Core", "RngStreams.cs"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("repo root not found");
    }

    private static Dictionary<string, string> English()
    {
        var d = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var line in File.ReadAllLines(Path.Combine(Root(), "src", "Ravensguard.Game", "localization", "en.csv")))
        {
            int c = line.IndexOf(','); if (c <= 0) continue;
            string v = line[(c + 1)..];
            if (v.Length >= 2 && v[0] == '"' && v[^1] == '"') v = v[1..^1].Replace("\"\"", "\"");
            d[line[..c]] = v;
        }
        return d;
    }

    private static EpilogueText TextOf(WorldState st, Dictionary<string, string> en, ChronGrammar? g = null) => new()
    {
        Tr = k => en.TryGetValue(k, out var v) ? v : "<" + k + ">",
        NameOf = (kind, id) => kind == ChronParamKind.Person ? "Person" + (id % 1000) : "Place" + id,
        Grammar = g ?? ChronGrammar.English, Chronicle = st.Chronicle,
    };

    private static ApWorld GreatDone(string ending = "dynasty", bool autopilotAi = false)
    {
        var w = ApWorld.Soak(heroCrowns: 50);
        var g = Great("test_great", ending, Cond("has_coin_at_least", ("pence", "10")));
        w.Ap.Tree = new AmbitionTree([g]);
        w.State.Hero!.AmbitionId = "test_great";
        var st = w.Ap.Enable(w.Hero, w.Sec, Directive.Rest(1));
        if (autopilotAi) for (int i = 0; i < st.Policy.Length; i++) st.Policy[i] = InterruptPolicy.Ai;
        w.RunHours(3);
        return w;
    }

    private void Chron(WorldState st, string type, EntityId hero, long sec, string text = "ch_p8_ambition_chosen", IReadOnlyList<Cause>? causes = null, IReadOnlyDictionary<string, string>? data = null) =>
        st.Chronicle.Add(new ChronicleEntry(st.Ids.Allocate(IdKind.Event), new GameTime(sec), type, [hero], causes ?? [], data ?? new Dictionary<string, string>(), text, [ChronParam.Person("person", hero)]));

    // ------------------------------------------------------------------ EN-1

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EN1_FinishingAGreatAmbitionPausesWithTheOffer_OnAutopilotToo(bool autopilot)
    {
        using var w = GreatDone(autopilotAi: autopilot);
        var st = w.Book.Get(w.Hero)!;
        Assert.Contains(w.State.Chronicle.All, e => e.TypeId == "great_ambition_done");
        Assert.Equal(new[] { "dynasty" }, w.State.Hero!.EndingOffers.Select(o => o.Id));
        Assert.Equal("dynasty", w.Ap.PendingEnding);
        Assert.Equal(InterruptPolicy.Pause, InterruptRouter.PolicyOf(st, InterruptKind.EndingOffered));       // Ai or not, the ending always pauses
        Assert.True(w.Ap.BusOf(st).Paused);
        Assert.Contains(w.Book.Log.Lines, l => l.Code == LogCode.Interrupt && l.Arg == (byte)InterruptKind.EndingOffered);
        output.WriteLine($"autopilot {autopilot}: offers {w.State.Hero.EndingOffers.Count}");
    }

    [Fact]
    public void EN1_TheOfferIsMadeOnceAndAMiddleAmbitionOffersNoEnding()
    {
        using var w = GreatDone();
        w.RunHours(48);
        Assert.Single(w.State.Chronicle.All, e => e.TypeId == "great_ambition_done");
        Assert.Single(w.State.Hero!.EndingOffers);
        using var m = ApWorld.Soak(heroCrowns: 50);
        m.Ap.Tree = new AmbitionTree([Great("test_mid", "", Cond("has_coin_at_least", ("pence", "10"))) with { Tier = "middle" }]);
        m.State.Hero!.AmbitionId = "test_mid"; m.Ap.Enable(m.Hero, m.Sec, Directive.Rest(1)); m.RunHours(3);
        Assert.Empty(m.State.Hero.EndingOffers);
    }

    // ------------------------------------------------------------------ EN-2

    [Fact]
    public void EN2_CarryOnKeepsTheOffer_AndItEndsAfterThirtyDaysWithoutTheCondition()
    {
        using var w = GreatDone();
        w.Ap.CarryOn();
        Assert.Null(w.Ap.PendingEnding);
        var h = w.State.Hero!; Assert.Single(h.EndingOffers);
        var tree = new AmbitionTree([Great("test_great", "dynasty", Cond("has_coin_at_least", ("pence", "10")))]);
        var holds = new AmbitionFacts { CoinPence = 100 }; var lost = new AmbitionFacts { CoinPence = 0 };
        Assert.Empty(EndingDesk.CheckOffers(h, tree, holds, 100));
        Assert.Empty(EndingDesk.CheckOffers(h, tree, lost, 101));                           // the clock starts
        Assert.Empty(EndingDesk.CheckOffers(h, tree, lost, 101 + 29));
        Assert.Empty(EndingDesk.CheckOffers(h, tree, holds, 101 + 30));                      // it came back: the clock is cleared
        Assert.Equal(-1, h.EndingOffers[0].LapseDay);
        Assert.Empty(EndingDesk.CheckOffers(h, tree, lost, 200));
        Assert.Empty(EndingDesk.CheckOffers(h, tree, lost, 229));
        Assert.Equal(new[] { "dynasty" }, EndingDesk.CheckOffers(h, tree, lost, 230));       // 30 days
        Assert.Empty(h.EndingOffers);
    }

    [Fact]
    public void EN2_ALapsedOfferIsChronicledAsARootAndAnHeirStartsWithoutOne()
    {
        using var w = GreatDone();
        var h = w.State.Hero!;
        h.EndingOffers[0] = ("dynasty", (int)(w.Sec / 86400) - 40);                          // the condition failed 40 days ago ...
        w.Ap.Tree = new AmbitionTree([Great("test_great", "dynasty", Cond("has_coin_at_least", ("pence", "999999999")))]);   // ... and does not hold now
        w.RunHours(30);
        Assert.Empty(h.EndingOffers);
        var e = Assert.Single(w.State.Chronicle.All, x => x.TypeId == "ending_lapsed");
        Assert.NotEmpty(e.Causes);
        Assert.Empty(HeroRecord.FromSave(h.ToSave()).EndingOffers);
        Assert.Empty(new HeroRecord { Name = "Heir", Origin = "heir", Attributes = [50, 50, 50, 50, 50] }.EndingOffers);   // an heir must earn their own
    }

    // ------------------------------------------------------------------ EN-3

    [Fact]
    public void EN3_TakingItWritesTheEndedSave_ItLoadsFrozen_AndSaveLoadSaveIsByteIdentical()
    {
        using var w = GreatDone();
        var before = WorldSave.Capture(w.State, "h", "0.1");
        Assert.DoesNotContain(EndingSave.SectionName, before.Sections.Keys);
        var book = EndingDesk.Take(w.State, "dynasty", w.Sec);
        Assert.NotNull(book);
        var file = WorldSave.Capture(w.State, "h", "0.1");
        Assert.Contains(EndingSave.SectionName, file.Sections.Keys);
        var bytes = SaveCodec.Encode(file);
        var back = WorldSave.Restore(SaveCodec.Decode(bytes), w.Content, "h", null, w.State.Continent, w.State.History);
        Assert.NotNull(back.Ending); Assert.True(EndingDesk.IsEnded(back));
        Assert.Equal(w.State.Hero!.EndedAt, back.Hero!.EndedAt);
        Assert.Equal(book!.Beats.Count, back.Ending!.Beats.Count);
        var again = WorldSave.Capture(back, "h", "0.1");
        // save, load, save: every section the bare test world can restore comes back byte for byte (its markets need a production book it does not have)
        Assert.Empty(again.Sections.Keys.Except(file.Sections.Keys));
        Assert.Subset(new HashSet<string> { "markets" }, file.Sections.Keys.Except(again.Sections.Keys).ToHashSet());
        foreach (var (k, v) in again.Sections) Assert.True(v.Span.SequenceEqual(file.Sections[k].Span), "section " + k + " differs");
        Assert.True(again.Sections[EndingSave.SectionName].Span.SequenceEqual(file.Sections[EndingSave.SectionName].Span));
        Assert.True(again.Sections[WorldSave.HeroSection].Span.SequenceEqual(file.Sections[WorldSave.HeroSection].Span));
        Assert.Null(EndingDesk.Take(w.State, "dynasty", w.Sec));                              // final: no second ending
        Assert.Equal([EndingSave.SectionName], Phase8Saves.SectionNames);                      // the registry's one section is the one P8-0A named
    }

    [Fact]
    public void EN3_ARunThatNeverEndedKeepsItsBytes_NoEndingSectionAndNoOffersKey()
    {
        using var w = ApWorld.Soak(heroCrowns: 50);
        var file = WorldSave.Capture(w.State, "h", "0.1");
        Assert.DoesNotContain(EndingSave.SectionName, file.Sections.Keys);
        var hs = w.State.Hero!.ToSave(); Assert.Null(hs.EndedAt); Assert.Null(hs.EndingOffers);
    }

    // ------------------------------------------------------------------ EN-4, EN-5

    private (ApWorld W, EndingBook Book) Scripted()
    {
        var w = GreatDone("crown");
        var st = w.State; var hero = w.Hero; var pt = st.Population.People; long now = w.Sec;
        var done = st.Chronicle.All.First(e => e.TypeId == "great_ambition_done");
        Chron(st, "battle_hero_led", hero, now - 5000, causes: [new Cause(CauseKind.Battle, done.Id)]);
        Chron(st, "fief_granted", hero, now - 4000);
        Chron(st, "hero_wedding", hero, now - 3000);
        Chron(st, "murder", hero, now - 2000);
        Chron(st, "contract_done", hero, now - 1500);
        Chron(st, "autopilot_start", hero, now - 1400);
        // two companions: one still with him, one dead
        int row = Enumerable.Range(0, pt.Count).First(i => pt.IsAlive(i) && pt.AgeYears(i, (int)(now / 86400)) is >= 20 and < 40);
        int dead = Enumerable.Range(row + 1, pt.Count - row - 1).First(i => pt.IsAlive(i));
        st.Party!.Squad.Members.Add(new Follower { Person = pt.IdOf(row), PersonIndex = row, Name = "a", State = HireState.Active, Loyalty = 500, JoinedDay = 1 });
        st.Party.Squad.Members.Add(new Follower { Person = pt.IdOf(dead), PersonIndex = dead, Name = "b", State = HireState.Active, Loyalty = 500, JoinedDay = 1 });
        pt.Status[dead] = PersonStatus.Dead;
        Chron(st, "fight_death", pt.IdOf(dead), now - 1000);
        // a child of the hero
        int heroRow = Enumerable.Range(dead + 1, pt.Count - dead - 1).First(i => pt.IsAlive(i));
        int child = Enumerable.Range(heroRow + 1, pt.Count - heroRow - 1).First(i => pt.IsAlive(i));
        st.Hero!.PersonIndex = heroRow; pt.Status[heroRow] = PersonStatus.Hero; pt.Father[child] = heroRow;
        var book = EndingDesk.Take(st, "crown", now)!;
        return (w, book);
    }

    [Fact]
    public void EN4_AScriptedRunsEpilogueHasEveryRequiredBeatInOrder_EachWithEntryLinks_AndReadsInFull()
    {
        var (w, book) = Scripted(); using var _ = w;
        var kinds = book.Beats.Select(b => b.Kind).ToList();
        output.WriteLine(string.Join(" ", kinds));
        int Index(string k) => kinds.IndexOf(k);
        Assert.Equal("origin", kinds[0]);
        Assert.Equal("end", kinds[^1]);
        Assert.True(Index("ambition") > Index("origin") && Index("deed") > Index("ambition") && Index("companion") > Index("deed") && Index("heirs") > Index("companion")
                    && Index("holdings") > Index("heirs") && Index("became") > Index("holdings") && Index("end") > Index("became"));
        int deeds = kinds.Count(k => k == "deed"); Assert.InRange(deeds, Epilogue.MinDeeds, Epilogue.MaxDeeds);
        Assert.Equal(2, kinds.Count(k => k == "companion"));
        Assert.InRange(book.Beats.Count, 8, 14);
        Assert.All(book.Beats, b => { Assert.NotEmpty(b.Entries); Assert.All(b.Entries, id => Assert.NotNull(w.State.Chronicle.Get(new EntityId(id)))); });
        // the deeds are the heaviest that name the hero, told in the order they happened; none of the bookkeeping entries is one
        var deedTypes = book.Beats.Where(b => b.Kind == "deed").Select(b => b.Ids[0]).ToList();
        Assert.Contains("battle_hero_led", deedTypes); Assert.Contains("fief_granted", deedTypes);
        Assert.DoesNotContain("autopilot_start", deedTypes); Assert.DoesNotContain("ending_taken", deedTypes);
        Assert.Contains(book.Beats, b => b is { Kind: "companion" } && b.Ids[0] == "alive");
        Assert.Contains(book.Beats, b => b is { Kind: "companion" } && b.Ids[0] == "dead" && b.Nums[0] != 0);
        Assert.Equal(1, book.Beats.Single(b => b.Kind == "heirs").Nums[0]);
        // the ending's entry: caused by the great ambition's
        var taken = Assert.Single(w.State.Chronicle.All, e => e.TypeId == "ending_taken");
        Assert.Contains(taken.Causes, c => c.EventId == w.State.Chronicle.All.First(e => e.TypeId == "great_ambition_done").Id);
        // every key exists, no hole is left, and the paragraphs read in order
        var en = English(); var text = TextOf(w.State, en);
        foreach (var b in book.Beats) foreach (var k in Epilogue.KeysOf(b)) Assert.True(en.ContainsKey(k), "missing " + k);
        var paras = Epilogue.Paragraphs(book.Beats, text);
        Assert.Equal(book.Beats.Count, paras.Count);
        Assert.All(paras, p => { Assert.False(string.IsNullOrWhiteSpace(p)); Assert.DoesNotContain("{", p); Assert.DoesNotContain("<ep_", p); });
        foreach (var p in paras) output.WriteLine(p);
    }

    [Fact]
    public void EN5_TwoBuildsOfTheSameEndedSaveGiveTheSameVariantsAndTheSameWords_AndAnotherRunGetsOthers()
    {
        var (w, book) = Scripted(); using var _ = w;
        var en = English(); var text = TextOf(w.State, en);
        var bytes = SaveCodec.Encode(WorldSave.Capture(w.State, "h", "0.1"));
        var a = WorldSave.Restore(SaveCodec.Decode(bytes), w.Content, "h", null, w.State.Continent, w.State.History).Ending!;
        var b = WorldSave.Restore(SaveCodec.Decode(bytes), w.Content, "h", null, w.State.Continent, w.State.History).Ending!;
        Assert.Equal(a.Beats.Select(x => x.Variant), b.Beats.Select(x => x.Variant));
        Assert.Equal(Epilogue.Paragraphs(a.Beats, text), Epilogue.Paragraphs(b.Beats, text));
        Assert.Equal(book.Beats.Select(x => x.Variant), a.Beats.Select(x => x.Variant));
        // the variant is stream 0x810 keyed on the run and the beat: a pure function, and it varies with both
        Assert.Equal(Epilogue.VariantOf(1, 77, 3), Epilogue.VariantOf(1, 77, 3));
        var seen = Enumerable.Range(0, 60).Select(i => Epilogue.VariantOf(1, 77, i)).Distinct().Count(); Assert.Equal(Epilogue.Variants, seen);
        Assert.NotEqual(Enumerable.Range(0, 40).Select(i => Epilogue.VariantOf(1, 77, i)), Enumerable.Range(0, 40).Select(i => Epilogue.VariantOf(1, 78, i)));
        // another grammar changes names' forms, not the beats
        var other = Epilogue.Paragraphs(a.Beats, TextOf(w.State, en, ChronGrammar.Test));
        Assert.Equal(a.Beats.Count, other.Count);
    }

    [Fact]
    public void EpilogueText_EveryEndingVariantAndSupportKeyIsInEnCsv_WithNoMoneyWords_AndTheTemplatesAreSound()
    {
        var en = English(); var bad = new List<string>();
        var money = new System.Text.RegularExpressions.Regex(@"\b(mites|crowns?|cr|pence|gp)\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        foreach (var e in EndingRules.All) for (int v = 1; v <= Epilogue.Variants; v++) if (!en.ContainsKey($"ep_end_{e}_{v}")) bad.Add($"ep_end_{e}_{v}");
        foreach (var (k, v) in en.Where(kv => kv.Key.StartsWith("ep_", StringComparison.Ordinal)))
        {
            if (money.IsMatch(v)) bad.Add(k + " has a money word");
            if (k.Contains("_offer_") || k.StartsWith("ep_journal") || k.StartsWith("ep_title") || k.StartsWith("ep_subtitle") || k.StartsWith("ep_ended") || k.StartsWith("ep_name") || k is "ep_back" or "ep_chronicle" or "ep_legends" or "ep_lost_entry") continue;
            var names = new[] { "person", "origin", "ambition", "year", "cause", "entry", "companion", "n", "names", "coin", "realm", "heir", "standing", "age" };
            foreach (var err in ChronGrammar.English.Check(v, names)) bad.Add(k + ": " + err);
        }
        Assert.Empty(bad);
    }

    // ------------------------------------------------------------------ EN-6

    [Fact]
    public void EN6_TheHeroPassesToTheWorld_AnOrdinaryPersonOnTheNpcAi_KeepingEveryDeed_AndCoinMassAndPeopleAreExact()
    {
        var (w0, _) = Scripted(); w0.Dispose();                                                // (the scripted run only for its shape; the check below is on a fresh one)
        using var w = GreatDone();
        var st = w.State; var pt = st.Population.People; long now = w.Sec;
        int heroRow = Enumerable.Range(0, pt.Count).First(i => pt.IsAlive(i)); pt.Status[heroRow] = PersonStatus.Hero; st.Hero!.PersonIndex = heroRow;
        int people = pt.Count; var ids = Enumerable.Range(0, pt.Count).Select(i => pt.IdOf(i)).ToList();
        long coin = w.Ctx.Coin; var heroId = w.Hero; var inv = st.Hero.Inventory.Count;
        var squad = new Follower { Person = pt.IdOf(heroRow == 0 ? 1 : 0), PersonIndex = heroRow == 0 ? 1 : 0, Name = "c", State = HireState.Active, Loyalty = 500, JoinedDay = 1 }; st.Party!.Squad.Members.Add(squad);
        Assert.NotNull(EndingDesk.Take(st, "dynasty", now));
        Assert.Equal(PersonStatus.Alive, pt.Status[heroRow]);                                  // not Hero: every NPC system treats him as a person now
        Assert.False(w.Book.Get(heroId)!.Enabled);
        Assert.Equal(HireState.Ended, squad.State);                                            // the companions leave as ordinary people
        Assert.Equal(people, pt.Count); Assert.Equal(ids, Enumerable.Range(0, pt.Count).Select(i => pt.IdOf(i)).ToList());
        Assert.Equal(coin, w.Ctx.Coin); Assert.Equal(inv, st.Hero.Inventory.Count);
        Assert.Equal(heroId, st.Party.Hero);
        // the world goes on a week without him: he is a person, so the people stay exact and nobody is lost
        w.Ticker.AdvanceHours(24 * 7);
        Assert.True(pt.Count >= people);
        Assert.NotEqual(PersonStatus.Hero, pt.Status[heroRow]);
        // the ended save keeps him as the ordinary person: a load does not make him the hero again
        var back = WorldSave.Restore(SaveCodec.Decode(SaveCodec.Encode(WorldSave.Capture(st, "h", "0.1"))), w.Content, "h", null, st.Continent, st.History);
        Assert.NotEqual(PersonStatus.Hero, back.Population.People.Status[heroRow]);
        Assert.Equal(st.Hero.EndedAt, back.Hero!.EndedAt);
    }

    // ------------------------------------------------------------------ the library

    [Fact]
    public void TheLibraryKeepsTheEndedRun_ItsRunFileGainsTheEnding_AndOlderRunFilesReadAsNotEnded()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ravens-en1-" + Guid.NewGuid().ToString("N"));
        try
        {
            var lib = new WorldLibrary(dir);
            Directory.CreateDirectory(lib.RunsDirectory("w1"));
            lib.WriteRunInfo("w1", "a", new WorldLibrary.RunInfo("Aldric", false, "peddler", "make_your_fortune", true, 3, 4, "now"));
            lib.WriteRunInfo("w1", "b", new WorldLibrary.RunInfo("Maud", true, "outlaw", "clear_your_name", false, 3, 4, "now"));
            var plain = File.ReadAllText(Path.Combine(lib.RunsDirectory("w1"), "a.run.json"));
            Assert.DoesNotContain("EndingId", plain);                                           // an unended run file keeps its bytes
            Assert.False(lib.ReadRunInfo("w1", "a")!.Ended);
            Assert.Empty(lib.Legends("w1"));
            Assert.True(lib.MarkEnded("w1", "b", "merchant_prince", 4321));
            Assert.False(lib.MarkEnded("w1", "nope", "crown", 1));
            var l = Assert.Single(lib.Legends("w1"));
            Assert.Equal(("b", "merchant_prince", 4321), (l.Run, l.Info.EndingId, l.Info.EndDay));
            Assert.Equal("Maud", l.Info.CharacterName); Assert.True(l.Info.Ended);
            Assert.Equal(plain, File.ReadAllText(Path.Combine(lib.RunsDirectory("w1"), "a.run.json")));
        }
        finally { try { Directory.Delete(dir, true); } catch (IOException) { } }
    }
}
