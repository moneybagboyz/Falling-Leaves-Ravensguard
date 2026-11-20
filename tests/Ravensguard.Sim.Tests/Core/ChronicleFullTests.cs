using System.Text.RegularExpressions;
using Ravensguard.Content;
using Ravensguard.Sim.Abstract;
using Ravensguard.Sim.Combat.Abstract;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Save;
using Ravensguard.Sim.Tests.Abstract;
using Ravensguard.Sim.Tests.Economy;
using Ravensguard.Sim.Worlds;
using Xunit;
using Xunit.Abstractions;

namespace Ravensguard.Sim.Tests.Core;

/// <summary>
/// The chronicle at full depth, part A2 (docs/specs/phase6/chronicle-full.md 7): CH-1 (the type table for every existing type), CH-2 (the Phase 4 S7 sites are keys), CH-3 (legacy notes load
/// and show), CH-6 (chains resolve in time order), plus the pieces they stand on: specific coverage, the effect index, the chronicle hash, the keys' save round trip and the seeding pass skeleton.
/// </summary>
public class ChronicleFullTests(ITestOutputHelper output)
{
    private static string Root()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "src", "Ravensguard.Sim", "Core", "RngStreams.cs"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("repo root not found");
    }

    private static Dictionary<string, string> EnglishRows()
    {
        var d = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var line in File.ReadAllLines(Path.Combine(Root(), "src", "Ravensguard.Game", "localization", "en.csv")))
        {
            int c = line.IndexOf(',');
            if (c <= 0) continue;
            string v = line[(c + 1)..];
            if (v.Length >= 2 && v[0] == '"' && v[^1] == '"') v = v[1..^1].Replace("\"\"", "\"");
            d[line[..c]] = v;
        }
        return d;
    }

    private static readonly Lazy<ChronicleTypes> Types = new(() => ChronicleTypes.From(EconFixture.Content));
    private static ChronicleEntry E(ulong n, long sec, string type, params Cause[] causes) => new(new EntityId(n), new GameTime(sec), type, [], causes, new Dictionary<string, string>());

    // ---------------------------------------------------------------- CH-1

    [Fact]
    public void CH1_TheTypeTableLoads_AndEveryRowValidates_KeysInEnCsvAndParametersTyped()
    {
        var types = Types.Value;
        Assert.True(types.Rows.Count >= 150, $"only {types.Rows.Count} rows");
        var en = EnglishRows();
        var errs = types.Validate(EconFixture.Content, en.ContainsKey);
        Assert.True(errs.Count == 0, string.Join("\n", errs));
        // the keyed battle report's parameters are all typed and its sentence names every one of them
        var row = types.Get(BattleChronicle.TypeId)!;
        string sentence = en[row.TextKey];
        foreach (var p in row.Def.Params.Where(p => p.Name != "place")) Assert.Contains("{" + p.Name + "}", sentence);
    }

    [Fact]
    public void CH1_TheValidatorCatchesABadRow()
    {
        var db = EconFixture.Content;
        var bad = new ChronicleTypeDef { Id = "x", TextKey = "no_such_key", Params = [new() { Name = "a", Type = "person" }, new() { Name = "a", Type = "banana" }], CauseKinds = ["Nonsense"], Rumour = "nope", SpecificRequires = "note", Chance = true, Root = true };
        var t = ChronicleTypes.Of([bad]);
        var errs = t.Validate(db, _ => false);
        Assert.Contains(errs, e => e.Contains("not in en.csv")); Assert.Contains(errs, e => e.Contains("unknown type 'banana'")); Assert.Contains(errs, e => e.Contains("repeated"));
        Assert.Contains(errs, e => e.Contains("unknown cause kind")); Assert.Contains(errs, e => e.Contains("unknown rumour_type")); Assert.Contains(errs, e => e.Contains("needs note_keys")); Assert.Contains(errs, e => e.Contains("root or a chance"));
    }

    /// <summary>Every type id a system writes, found two ways: by running (the seed-1234 history and a headless region for four game months) and by reading the sources for literals passed to the chronicle writers.</summary>
    [Fact]
    public void CH1_EveryTypeAnySystemWrites_HasARow()
    {
        var types = Types.Value;
        var seen = new SortedSet<string>(StringComparer.Ordinal);
        var hw = HeadlessWorld.Create(1234);
        foreach (var e in hw.History.Chronicle.All) seen.Add(e.TypeId);
        using (var t = hw.NewTicker()) t.AdvanceDays(120);
        foreach (var e in hw.World.Chronicle.All) seen.Add(e.TypeId);
        int fromRun = seen.Count;
        foreach (var t in SourceTypes()) seen.Add(t);
        output.WriteLine($"{fromRun} types seen in the run, {seen.Count} with the sources; {types.Rows.Count} rows");
        var missing = seen.Where(t => !types.Has(t)).ToList();
        Assert.True(missing.Count == 0, "no chronicle_type row (content/base/chronicle/types.jsonc) for: " + string.Join(", ", missing));
    }

    private static readonly Regex[] TypeLiterals =
    [
        new(@"\b(?:Chronicle|Chron|chron|Event|WriteChronicle|Ev)\w*\(\s*[^;""]{0,60}?""(?<t>[a-z][a-z_.0-9]+)""", RegexOptions.Compiled),
        new(@"new ChronicleEntry\([^;""]{0,120}?""(?<t>[a-z][a-z_.0-9]+)""", RegexOptions.Compiled),
        new(@"Chronicle(?:\?)?\.Invoke\([^;""]{0,120}?""(?<t>[a-z][a-z_.0-9]+)""", RegexOptions.Compiled),
        new(@"\bAdd\(\(""(?<t>site_[a-z_]+|taboo_robbed)""", RegexOptions.Compiled),
        new(@"\bAdd\(""(?<t>fight_[a-z_]+)""", RegexOptions.Compiled),
        new(@"\bEv\([^,""]*(?:""(?<t>[a-z_]+)""[^,""]*)+,", RegexOptions.Compiled),
    ];

    private static HashSet<string> SourceTypes()
    {
        var found = new HashSet<string>(StringComparer.Ordinal) { "autopilot_attacked", "autopilot_injured", "autopilot_bigchoice", "autopilot_arrived", "loan_angry", "loan_lost", "loan_returned", "artefact_made" };
        foreach (var f in Directory.EnumerateFiles(Path.Combine(Root(), "src", "Ravensguard.Sim"), "*.cs", SearchOption.AllDirectories).Where(f => !f.Contains("/bin/") && !f.Contains("/obj/")))
        {
            string text = File.ReadAllText(f);
            foreach (var re in TypeLiterals)
                foreach (Match m in re.Matches(text))
                    foreach (Capture c in m.Groups["t"].Captures) found.Add(c.Value);
        }
        // not types: the helper's own parameter names and prefixes the regexes can meet
        found.RemoveWhere(t => t is "autopilot_" or "loan_" or "maker" or "material" or "hire" or "quit");
        return found;
    }

    // ---------------------------------------------------------------- CH-2

    private static readonly Regex NoteCall = new(@"new Cause\(|\bKill\(", RegexOptions.Compiled);
    private static readonly Regex Literal = new(@"\$?""(?<s>(?:[^""\\\r\n]|\\.)*)""", RegexOptions.Compiled);

    /// <summary>A line that builds a Cause or kills a person and has a string literal holding a space: a sentence passed as a note (keys and ids have none).</summary>
    private static bool PassesSentence(string line) => NoteCall.IsMatch(line) && Literal.Matches(line).Any(m => m.Groups["s"].Value.Contains(' '));

    private static Dictionary<string, int> SentenceNotes()
    {
        var r = new Dictionary<string, int>(StringComparer.Ordinal);
        string sim = Path.Combine(Root(), "src", "Ravensguard.Sim");
        foreach (var f in Directory.EnumerateFiles(sim, "*.cs", SearchOption.AllDirectories).Where(f => !f.Contains("/bin/") && !f.Contains("/obj/")))
        {
            int n = 0;
            foreach (var line in File.ReadLines(f).Where(l => !l.TrimStart().StartsWith("//") && !l.TrimStart().StartsWith("///")))
                if (PassesSentence(line)) n++;
            if (n > 0) r[Path.GetRelativePath(sim, f).Replace('\\', '/')] = n;
        }
        return r;
    }

    /// <summary>
    /// CH-2 for the Phase 4 S7 sites: the ambush's place and cause notes, the death descriptions and the battle chronicle pass keys and parameters, no sentence. Every other file's count of sentence
    /// notes (Phase 2-5 writers and history v0, which each Phase 6 step moves) is pinned: it may only fall.
    /// </summary>
    [Fact]
    public void CH2_ThePhase4Sites_PassKeysNotSentences_AndTheRestOnlyShrinks()
    {
        var found = SentenceNotes();
        foreach (var f in new[] { "Caravans/CaravanAmbush.cs", "Combat/Wounds/WoundSystem.cs", "Combat/Abstract/BattleChronicle.cs", "Combat/Abstract/AbstractBattles.cs", "Combat/Abstract/AbstractBattle.cs" })
            Assert.False(found.ContainsKey(f), $"{f} passes an English sentence as a cause or death note: use a key and parameters (chronicle-full 4.1)");
        int total = found.Values.Sum();
        output.WriteLine($"sentence notes left in the sim: {total} in {found.Count} files");
        Assert.True(total <= LegacySentenceNotes, $"sentence notes grew to {total} (pinned {LegacySentenceNotes}): a new writer must pass NoteKey and NoteParams");
        if (total < LegacySentenceNotes) output.WriteLine($"lower LegacySentenceNotes to {total}");
    }

    /// <summary>The count of sentence notes left in the sim: 83 at P6-A2, 3 after P6-E4 (the continent tick's word_reached, which part F moves, and history v0's two seed causes, frozen with v0 worlds). Lower it as writers move to keys.</summary>
    private const int LegacySentenceNotes = 3;

    // ---------------------------------------------------------------- CH-3

    [Fact]
    public void CH3_LegacyEnglishNotesLoadAndShow_NewEntriesCarryKeys_AndBothSaveByteIdentical()
    {
        var w = AbstractFixture.NewWorld();
        var legacy = new ChronicleEntry(new EntityId(900_001), new GameTime(1000), "famine", [], [new Cause(CauseKind.Famine, null, "two poor harvests running")], new Dictionary<string, string> { ["title"] = "A famine", ["text"] = "People starved." });
        var cause = new Cause(CauseKind.Raid, null, "", "ch_note_ambush", [ChronParam.Count("band", 4), ChronParam.Place("place", new EntityId(77))]);
        var keyed = new ChronicleEntry(new EntityId(900_002), new GameTime(2000), "battle_report", [new EntityId(5)], [cause, new Cause(CauseKind.War, new EntityId(900_001))], new Dictionary<string, string> { ["lost_a"] = "2" },
            "ch_report_battle", [ChronParam.Place("settlement", new EntityId(77)), ChronParam.TextKey("a", "ch_side_bandits"), ChronParam.Mites("fee", 12_345), ChronParam.Days("age", 3)]);
        w.Chronicle.Add(legacy); w.Chronicle.Add(keyed);
        var ws = AbstractFixture.AsWorldState(w, GameTime.FromGameHours(w.State.HourDone));
        var file = WorldSave.Capture(ws, EconFixture.ContentHash, "0.1");
        var back = WorldSave.Restore(file, EconFixture.Content, EconFixture.ContentHash);
        var l2 = back.Chronicle.Get(legacy.Id)!; var k2 = back.Chronicle.Get(keyed.Id)!;
        Assert.Equal("", l2.TextKey); Assert.Null(l2.Params); Assert.Equal("two poor harvests running", l2.Causes[0].Note); Assert.Equal("", l2.Causes[0].NoteKey);
        Assert.Equal("ch_report_battle", k2.TextKey); Assert.Equal(4, k2.Params!.Count); Assert.Equal(keyed.Params, k2.Params); Assert.Equal("ch_note_ambush", k2.Causes[0].NoteKey); Assert.Equal(cause.NoteParams, k2.Causes[0].NoteParams);
        // shown as written: the legacy entry reads its English, the keyed one the sentence of en.csv
        var en = EnglishRows();
        string Tr(string k) => en.TryGetValue(k, out var v) ? v : k;
        Assert.Equal("People starved.", ChronicleTypes.Format(l2, Tr, (_, _) => "?"));
        Assert.Equal("two poor harvests running", ChronicleTypes.FormatNote(l2.Causes[0], Tr, (_, _) => "?"));
        Assert.Equal("Word reached Oakford that bandits and {b} fought {where}; bandits lost {lost_a}, {b} lost {lost_b}, and {outcome}.",
            ChronicleTypes.Format(k2, Tr, (kind, id) => kind == ChronParamKind.Place ? "Oakford" : "?"));                 // the parameters this test left out stay as holes
        Assert.Equal("Bandits (4) ambushed a caravan on the road from Oakford.", ChronicleTypes.FormatNote(k2.Causes[0], Tr, (_, _) => "Oakford"));
        AssertSameBytes(file, WorldSave.Capture(back, EconFixture.ContentHash, "0.1"));
        // the effect index built at load equals the one built live (CH-11's part), and the hash covers the keys
        Assert.Equal(w.Chronicle.Effects.Hash(), back.Chronicle.Effects.Hash());
        Assert.Equal(new[] { keyed.Id }, back.Chronicle.Effects.After(legacy.Id).ToArray());
        Assert.Equal(w.Chronicle.Hash(), back.Chronicle.Hash());
    }

    /// <summary>A v8 save's chronicle section has only the old keys: it still decodes, with empty keys.</summary>
    [Fact]
    public void CH3_AV8ChronicleSection_WithTheOldKeysOnly_DecodesWithEmptyKeys()
    {
        var old = new OldChronicle { Entries = [new OldEntry { Id = 5, When = 90, TypeId = "famine", Subjects = [1], Causes = [new OldCause { Kind = 7, Note = "two poor harvests" }], Data = ["title", "A famine"] }] };
        byte[] bytes = SaveCodec.SerializeCompressed(old);
        var now = SaveCodec.DeserializeCompressed<ChronicleData>(bytes);
        var c = WorldSave.DecodeChronicle(now);
        var e = c.Get(new EntityId(5))!;
        Assert.Equal("", e.TextKey); Assert.Null(e.Params); Assert.Equal("two poor harvests", e.Causes[0].Note); Assert.Equal("", e.Causes[0].NoteKey); Assert.Equal("A famine", e.Data["title"]);
        Assert.Equal("A famine", ChronicleTypes.Format(e, k => k, (_, _) => "?"));
    }

    [MessagePack.MessagePackObject] public sealed class OldChronicle { [MessagePack.Key(0)] public List<OldEntry> Entries { get; set; } = []; }
    [MessagePack.MessagePackObject] public sealed class OldEntry
    {
        [MessagePack.Key(0)] public ulong Id { get; set; } [MessagePack.Key(1)] public long When { get; set; } [MessagePack.Key(2)] public string TypeId { get; set; } = "";
        [MessagePack.Key(3)] public ulong[] Subjects { get; set; } = []; [MessagePack.Key(4)] public List<OldCause> Causes { get; set; } = []; [MessagePack.Key(5)] public string[] Data { get; set; } = [];
    }
    [MessagePack.MessagePackObject] public sealed class OldCause { [MessagePack.Key(0)] public int Kind { get; set; } [MessagePack.Key(1)] public ulong EventId { get; set; } [MessagePack.Key(2)] public bool HasEvent { get; set; } [MessagePack.Key(3)] public string Note { get; set; } = ""; }

    private static void AssertSameBytes(SaveFile a, SaveFile b)
    {
        Assert.Equal(a.Sections.Keys.OrderBy(k => k, StringComparer.Ordinal), b.Sections.Keys.OrderBy(k => k, StringComparer.Ordinal));
        foreach (var (k, v) in a.Sections) Assert.True(v.Span.SequenceEqual(b.Sections[k].Span), $"section {k} differs");
    }

    // ---------------------------------------------------------------- CH-6 and coverage

    [Fact]
    public void CH6_EveryChainResolvesToAnEarlierEntry_RootsOnlyOfTheAllowedKinds()
    {
        var types = Types.Value;
        var hw = HeadlessWorld.Create(1234);
        var h = ChronicleChains.Check(hw.History.Chronicle, types);
        using (var t = hw.NewTicker()) t.AdvanceDays(120);
        var r = ChronicleChains.Check(hw.World.Chronicle, types);
        output.WriteLine($"history: {h.Entries} entries, {h.Roots} roots; region: {r.Entries} entries, {r.Roots} roots");
        Assert.True(h.Sound, string.Join("\n", h.Problems)); Assert.True(r.Sound, string.Join("\n", r.Problems));
        Assert.True(h.Entries > 1000);
    }

    [Fact]
    public void CH6_TheWalkerFindsAnUnresolvedACauseOutOfOrderAndAnIllegalRoot()
    {
        var c = new Chronicle();
        c.Add(E(1, 100, "bad_root", new Cause(CauseKind.Law)));                                                    // a root of an unknown type
        c.Add(E(2, 50, "x", new Cause(CauseKind.Raid, new EntityId(1))));                                           // names an entry but is earlier in time than it
        c.Add(E(3, 200, "x", new Cause(CauseKind.Raid, new EntityId(99))));                                         // names nothing
        c.Add(E(4, 300, "x", new Cause(CauseKind.Raid, new EntityId(5))));                                          // names a later entry
        c.Add(E(5, 400, "x", new Cause(CauseKind.Raid, new EntityId(3))));
        var r = ChronicleChains.Check(c, Types.Value);
        Assert.Equal(1, r.Unresolved); Assert.Equal(2, r.OutOfOrder); Assert.Equal(1, r.IllegalRoots); Assert.False(r.Sound);
    }

    [Fact]
    public void SpecificCoverage_CountsEventsAndAllowedNotes_ExcludesDriftLabels_AndIgnoresOtherAndUnknown()
    {
        var c = new Chronicle();
        c.Add(E(1, 10, "famine", new Cause(CauseKind.Weather, null, "a drought")));                                  // not specific: no entry, no key
        c.Add(E(2, 20, "outbreak", new Cause(CauseKind.Disease, new EntityId(1))));                                  // specific: names an earlier entry
        c.Add(E(3, 30, "outbreak", new Cause(CauseKind.Other, new EntityId(1))));                                    // Other is never specific
        c.Add(E(4, 40, "business_opened", new Cause(CauseKind.MarketDrift, null, "dear and short")));                // a drift label: apart, out of the denominator
        c.Add(E(5, 50, "battle_report", new Cause(CauseKind.Raid, null, "", "ch_note_ambush")));                     // specific by note: the row allows that key
        c.Add(E(6, 60, "battle_report", new Cause(CauseKind.Raid, null, "", "ch_note_unlisted")));                   // a key the row does not list
        c.Add(E(7, 5, "outbreak", new Cause(CauseKind.Disease, new EntityId(2))));                                   // names a later entry (id 2 is at 20): not earlier in time
        var rep = c.SpecificCoverage(Types.Value);
        Assert.Equal(7, rep.Entries); Assert.Equal(2, rep.Specific); Assert.Equal(1, rep.Drift); Assert.Equal(0, rep.NoCause); Assert.Equal(6, rep.Denominator);
        Assert.Equal(2.0 / 6, rep.Ratio, 6);
        Assert.Equal(1.0, new Chronicle().SpecificCoverage().Ratio);
        // the old measure stays: any cause but drift and unknown
        Assert.Equal(6.0 / 7, c.CauseCoverage(), 6);
    }

    [Fact]
    public void TheEffectIndex_ListsWhatFollowed_InAppendOrder_AndTheHashCoversKeysNotText()
    {
        var c = new Chronicle();
        c.Add(E(1, 10, "famine")); c.Add(E(2, 20, "death", new Cause(CauseKind.Famine, new EntityId(1)), new Cause(CauseKind.Famine, new EntityId(1)))); c.Add(E(3, 30, "death", new Cause(CauseKind.Famine, new EntityId(1))));
        Assert.Equal(new[] { new EntityId(2), new EntityId(3) }, c.Effects.After(new EntityId(1)).ToArray());
        Assert.Empty(c.Effects.After(new EntityId(3)));
        var a = new Chronicle(); var b = new Chronicle();
        var p = new[] { ChronParam.Person("who", new EntityId(9)) };
        a.Add(new ChronicleEntry(new EntityId(1), new GameTime(5), "x", [], [], new Dictionary<string, string> { ["title"] = "English one" }, "ch_k", p));
        b.Add(new ChronicleEntry(new EntityId(1), new GameTime(5), "x", [], [], new Dictionary<string, string> { ["title"] = "English two" }, "ch_k", p));
        Assert.Equal(a.Hash(), b.Hash());                                                                              // data values are machine fields; the hash covers type, key, params and causes
        var d = new Chronicle(); d.Add(new ChronicleEntry(new EntityId(1), new GameTime(5), "x", [], [], new Dictionary<string, string>(), "ch_k", [ChronParam.Person("who", new EntityId(10))]));
        Assert.NotEqual(a.Hash(), d.Hash());
    }

    [Fact]
    public void TheAmbushDeathChain_FormatsFromEnCsvWithNestedKeys()
    {
        var en = EnglishRows(); string Tr(string k) => en[k];
        var br = new BattleReport { PlaceKey = BattleChronicle.RoadFrom, PlaceId = new EntityId(77) };
        var n = new BattleNotable(1, "Wynn", 0, BattleFate.Dead, OutcomeClass.Grave, WeaponClass.Dagger, false);
        var chain = BattleChronicle.DeathChain(n, br, "ch_began_ambush", [ChronParam.Count("band", 4), ChronParam.Place("place", new EntityId(77))]);
        Assert.Equal("Killed by a dagger (a grave wound) at the fight on the road from Oakford, which began when bandits (4) ambushed a caravan on the road from Oakford.",
            ChronicleTypes.FormatNote(chain, Tr, (_, _) => "Oakford"));
        var plain = BattleChronicle.DeathChain(null, new BattleReport(), "ch_began_ambush", [ChronParam.Count("band", 2)]);
        Assert.StartsWith("Killed in a fight, which began when bandits (2) ambushed", ChronicleTypes.FormatNote(plain, Tr, (_, _) => "?"));
    }

    // ---------------------------------------------------------------- the seeding pass skeleton (overview 7)

    private sealed class FakeStep(string name, string section, Action<Phase6SeedContext>? act = null) : IPhase6SeedStep
    {
        public int Runs; public Phase6SeedContext? Last;
        public string Name => name; public string Section => section;
        public bool Seed(Phase6SeedContext ctx) { Runs++; Last = ctx; act?.Invoke(ctx); return true; }
    }

    [Fact]
    public void Phase6Seeding_NoStepsRegistered_ChangesNothingAndSaysNothing()
    {
        Assert.DoesNotContain(Phase6Seeding.DefaultSteps, s => !Phase6Seeding.SectionNames.Contains(s.Section));       // a registered step names one of the optional sections
        var st = EconFixture.WorldWith(null);
        Assert.Empty(Phase6Seeding.Run(st, EconFixture.Content, null));
        Assert.Empty(Phase6Seeding.Run(st, EconFixture.Content, ["meta", "people"]));
        Assert.Equal(13, Phase6Seeding.SectionNames.Length);
    }

    [Fact]
    public void Phase6Seeding_RunsAtANewGameAndAtTheFirstLoadOfAnOldSave_ButNotWhenItsSectionIsSaved()
    {
        var st = EconFixture.WorldWith(null);
        var a = new FakeStep("a", "medicine"); var b = new FakeStep("b", "faith");
        // a new game: nothing is saved yet; both run, in order, with the world seed; no load note
        Assert.Empty(Phase6Seeding.Run(st, EconFixture.Content, null, [a, b]));
        Assert.Equal(1, a.Runs); Assert.Equal(1, b.Runs); Assert.False(a.Last!.FromSave); Assert.Equal(st.Seed, a.Last.Seed);
        // a v8 save from Phase 5: no Phase 6 section; both run and the report says so
        var notes = Phase6Seeding.Run(st, EconFixture.Content, ["meta", "people"], [a, b]);
        Assert.Equal([Phase6Seeding.LoadNote], notes); Assert.Equal("Phase 6 state derived at load", notes[0]); Assert.True(a.Last!.FromSave);
        // a save holding the faith section: only the medicine step derives
        Phase6Seeding.Run(st, EconFixture.Content, ["meta", "faith"], [a, b]);
        Assert.Equal(3, a.Runs); Assert.Equal(2, b.Runs);
        Assert.True(Phase6Seeding.HasAnySection(["meta", "fiefs"])); Assert.False(Phase6Seeding.HasAnySection(["meta", "people"]));
    }

    [Fact]
    public void Phase6Seeding_IsAFunctionOfTheSeedAndIdsOnly_SoANewGameAndALoadDeriveTheSame()
    {
        // a step that draws from a counter stream keyed on the seed and a person id, never the clock
        static ulong Derive(Phase6SeedContext c, ref ulong acc)
        {
            for (int p = 0; p < 50; p++) acc = acc * 31 + (ulong)DetRng.Range(c.Seed, 0, (ulong)p, RngStreams.Physician, 0, 0, 1000);
            return acc;
        }
        ulong fromNewGame = 0, fromLoad = 0;
        var step1 = new FakeStep("p", "medicine", c => { ulong a = 7; fromNewGame = Derive(c, ref a); });
        var step2 = new FakeStep("p", "medicine", c => { ulong a = 7; fromLoad = Derive(c, ref a); });
        var s1 = EconFixture.WorldWith(null); var s2 = EconFixture.WorldWith(null);
        s2.Clock.Advance(100.0);                                                                                     // the day it runs differs
        Phase6Seeding.Run(s1, EconFixture.Content, null, [step1]); Phase6Seeding.Run(s2, EconFixture.Content, ["meta"], [step2]);
        Assert.Equal(fromNewGame, fromLoad); Assert.NotEqual(0UL, fromNewGame);
    }

    [Fact]
    public void Phase6Seeding_TheDefaultPassAtLoad_LeavesAPhase5SavesNotesAndBytesAlone()
    {
        var w = AbstractFixture.NewWorld();
        var ws = AbstractFixture.AsWorldState(w, GameTime.FromGameHours(w.State.HourDone));
        var file = WorldSave.Capture(ws, EconFixture.ContentHash, "0.1");
        foreach (var s in Phase6Seeding.SectionNames) Assert.DoesNotContain(s, file.Sections.Keys);
        var back = WorldSave.Restore(file, EconFixture.Content, EconFixture.ContentHash);
        Assert.DoesNotContain(Phase6Seeding.LoadNote, back.LoadNotes);
        AssertSameBytes(file, WorldSave.Capture(back, EconFixture.ContentHash, "0.1"));
    }
}
