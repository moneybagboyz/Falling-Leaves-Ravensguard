using System.Text.Json.Nodes;
using Ravensguard.Content;
using Ravensguard.Sim.Dialogue;
using Ravensguard.Sim.Rumours;

namespace Ravensguard.Sim.Tests.Dialogue;

/// <summary>D-1, D-5, D-6: the shipped talk content loads, validates and is complete in en.csv.</summary>
public class TalkContentTests
{
    private static readonly LineBook Book = LineBook.Default;

    [Fact]
    public void D1_the_base_content_loads_through_the_loader_with_the_typed_vocabulary_and_matches_the_embedded_book()
    {
        var db = TalkKit.LoadBase();                                         // throws with the report on any unknown condition or effect id or a broken dialogue graph
        Assert.Equal(Book.Lines.Count, db.All<LineDef>().Count());
        Assert.Equal(Book.Barks.Count, db.All<BarkDef>().Count());
        Assert.Equal(Book.Dialogues.Count, db.All<DialogueDef>().Count());
        Assert.InRange(Book.Lines.Count, 290, 310);
        Assert.InRange(Book.Barks.Count, 100, 125);
        var viaDb = LineBook.From(db);
        Assert.Equal(Book.Lines.Select(l => l.Id), viaDb.Lines.Select(l => l.Id));
        foreach (var t in LineBook.Topics) Assert.NotEmpty(Book.OfTopic(t));
    }

    [Fact]
    public void D1_every_key_exists_in_en_csv_and_every_param_is_declared_and_resolves()
    {
        var csv = TalkKit.EnCsv(); var rich = TalkKit.Rich(); var bad = new List<string>();
        void Check(string id, string key, IReadOnlyList<string> declared)
        {
            if (!csv.TryGetValue(key, out var text)) { bad.Add($"{id}: key {key} missing from en.csv"); return; }
            foreach (var p in ParamResolver.ParamsOf(text))
            {
                if (!declared.Contains(p)) bad.Add($"{id}: {{{p}}} used but not declared");
                if (Array.IndexOf(ParamResolver.Known, p) < 0) bad.Add($"{id}: {{{p}}} is not a known parameter");
            }
            foreach (var p in declared) if (!text.Contains("{" + p + "}") && !text.Contains("{#" + p + "|")) bad.Add($"{id}: {{{p}}} declared but not used");
            string said = ParamResolver.Format(text, rich.Params);
            if (said.Contains('{')) bad.Add($"{id}: unresolved after filling: {said}");
        }
        foreach (var l in Book.Lines) Check(l.Id, l.Key, l.Params);
        foreach (var b in Book.Barks) Check(b.Id, b.Key, b.Params);
        foreach (var r in Book.RumourTypes) if (!csv.ContainsKey(r.Key)) bad.Add($"rumour {r.Id}: key {r.Key} missing");
        foreach (var d in Book.Dialogues) foreach (var m in DialogueGraph.MissingKeys(d, csv.ContainsKey)) bad.Add(m);
        Assert.True(bad.Count == 0, string.Join("\n", bad));
    }

    [Fact]
    public void D1_no_talk_key_of_the_content_is_unused_and_every_parameter_name_is_known()
    {
        var used = new HashSet<string>(Book.Lines.Select(l => l.Key).Concat(Book.Barks.Select(b => b.Key)).Concat(Book.RumourTypes.Select(r => r.Key)));
        foreach (var d in Book.Dialogues) foreach (var n in d.Nodes.Values) { used.Add(n.Text); foreach (var c in n.Choices) used.Add(c.Text); }
        var csv = TalkKit.EnCsv();
        var unused = csv.Keys.Where(k => (k.StartsWith("D_") || k.StartsWith("B_")) && !used.Contains(k)).ToList();
        Assert.True(unused.Count == 0, "unused: " + string.Join(", ", unused.Take(10)));
        Assert.Equal(Book.Lines.Count, Book.Lines.Select(l => l.Key).Distinct().Count());
    }

    [Fact]
    public void D1_every_topic_job_class_mood_band_and_relationship_tier_has_a_line()
    {
        foreach (var topic in LineBook.Topics)
            foreach (var cls in new[] { "outdoor", "craft", "trade", "civic" })
                foreach (var mood in new[] { -50, 0, 50 })
                    foreach (var tier in Enum.GetValues<RelationTier>())
                    {
                        var x = new TalkContext { Job = cls switch { "outdoor" => "farmer", "craft" => "smith", "trade" => "merchant", _ => "guard" }, Mood = mood };
                        (x.Familiarity, x.TrustPm) = tier switch { RelationTier.Stranger => (0, 0), RelationTier.Acquaintance => (20, 100), RelationTier.Friend => (50, 400), RelationTier.Trusted => (80, 700), RelationTier.Disliked => (20, -300), _ => (20, -700) };
                        Assert.Equal(tier, x.Tier);
                        Assert.True(LineSelector.Candidates(Book, topic, x).Count > 0, $"no {topic} line for {cls}/{mood}/{tier}");
                    }
    }

    [Fact]
    public void D5_a_broken_dialogue_is_rejected_with_the_dialogue_and_node_named()
    {
        var good = Book.Dialogues.Single(d => d.Id == "caravan_master.intro");
        Assert.Empty(DialogueGraph.Problems(good));
        // an unreachable node
        var nodes = new Dictionary<string, DialogueNodeDef>(good.Nodes) { ["orphan"] = new() { Text = "D_CM_AFTER", Goto = "end" } };
        var errs = DialogueGraph.Problems(good with { Nodes = nodes });
        Assert.Contains(errs, e => e.Contains("caravan_master.intro") && e.Contains("'orphan'") && e.Contains("unreachable"));
        // a goto to nowhere
        nodes = new Dictionary<string, DialogueNodeDef>(good.Nodes) { ["after"] = new() { Text = "D_CM_AFTER", Goto = "nowhere" } };
        Assert.Contains(DialogueGraph.Problems(good with { Nodes = nodes }), e => e.Contains("'after'") && e.Contains("'nowhere'"));
        // a cycle with no way out
        nodes = new Dictionary<string, DialogueNodeDef> { ["a"] = new() { Text = "D_CM_AFTER", Goto = "b" }, ["b"] = new() { Text = "D_CM_AFTER", Goto = "a" } };
        Assert.Contains(DialogueGraph.Problems(good with { Start = "a", Repeat = "", Nodes = nodes }), e => e.Contains("no way to leave"));
        // a missing key
        nodes = new Dictionary<string, DialogueNodeDef> { ["a"] = new() { Text = "D_NOT_THERE", Goto = "end" } };
        Assert.Contains(DialogueGraph.MissingKeys(good with { Start = "a", Repeat = "", Nodes = nodes }, TalkKit.EnCsv().ContainsKey), e => e.Contains("'a'") && e.Contains("D_NOT_THERE"));
        // an unknown condition and an unknown effect, found by the vocabulary with the file named
        var json = JsonNode.Parse("""{ "nodes": { "n1": { "text": "D_CM_AFTER", "goto": "end", "conditions": [ { "id": "moon_is_full" } ], "effects": [ { "id": "summon_dragon" } ] } } }""")!;
        var report = new ContentReport();
        BaseVocabulary.Create().Validate(json, new SourceLocation("content/base/dialogue/unique/x.jsonc", 3, 1), "dialogue 'x'", report);
        Assert.Contains(report.Errors, e => e.Message.Contains("moon_is_full") && e.Location!.Value.File.EndsWith("x.jsonc"));
        Assert.Contains(report.Errors, e => e.Message.Contains("summon_dragon"));
    }

    [Fact]
    public void The_sample_unique_npc_has_20_to_50_lines_and_every_condition_is_one_the_talk_layer_knows()
    {
        var d = Book.Dialogues.Single(x => x.Speaker == "caravan_master");
        var keys = d.Nodes.Values.SelectMany(n => new[] { n.Text }.Concat(n.Choices.Select(c => c.Text))).ToHashSet();
        Assert.InRange(keys.Count, 20, 50);
        foreach (var c in Book.Lines.SelectMany(l => l.Conditions).Concat(Book.Barks.SelectMany(b => b.Conditions)).Concat(d.Nodes.Values.SelectMany(n => n.Conditions.Concat(n.Choices.SelectMany(c => c.Conditions)))))
            Assert.Contains(c.Id, TalkConditions.Supported);
    }

    [Fact]
    public void D6_at_forty_percent_more_text_every_systemic_line_still_fits_its_hard_cap_of_160_characters()
    {
        var csv = TalkKit.EnCsv(); var rich = TalkKit.Rich();
        foreach (var k in Book.Lines.Select(l => l.Key).Concat(Book.Barks.Select(b => b.Key)))
        {
            string said = ParamResolver.Format(csv[k], rich.Params);
            Assert.True(said.Length * 14 / 10 <= 160, $"{k}: {said.Length} characters");
        }
    }
}
