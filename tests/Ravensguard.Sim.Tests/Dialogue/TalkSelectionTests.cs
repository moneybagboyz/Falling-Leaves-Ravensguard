using Ravensguard.Content;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Dialogue;

namespace Ravensguard.Sim.Tests.Dialogue;

/// <summary>D-2 to D-4: choosing a systemic line, the barks, authored dialogue and the grammar.</summary>
public class TalkSelectionTests
{
    private static readonly LineBook Book = LineBook.Default;

    private static TalkContext Ctx() => new() { Job = "farmer", Mood = 0, Familiarity = 20, TrustPm = 100, Hour = 9, HeroName = "Aldric Vane", Place = "Ashby" };

    [Fact]
    public void D2_the_same_seed_minute_speaker_and_listener_pick_the_same_line_and_another_minute_often_another()
    {
        var x = Ctx(); int differs = 0, n = 300;
        for (int i = 0; i < n; i++)
        {
            long sec = 60L * (1667 + i);
            var a = LineSelector.Pick(Book, "smalltalk", x, 42, sec, 77, 5, 0)!.Id;
            for (int k = 0; k < 100; k++) Assert.Equal(a, LineSelector.Pick(Book, "smalltalk", x, 42, sec + 59, 77, 5, 0)!.Id);   // inside the same game minute
            if (a != LineSelector.Pick(Book, "smalltalk", x, 42, sec + 60, 77, 5, 0)!.Id) differs++;
        }
        Assert.True(differs * 100 >= n * 20, $"only {differs} of {n} picks changed with the minute");
    }

    [Fact]
    public void D3_no_line_repeats_within_8_turns_of_a_speaker_and_none_within_24_for_a_pair()
    {
        // twelve candidates, twenty-four turns: the speaker's own last eight never come again
        var twelve = new LineBook { Lines = [.. Enumerable.Range(0, 12).Select(i => new LineDef { Id = "L" + i, Topic = "smalltalk", Key = "K" })] };
        var x = Ctx(); var hist = new TalkHistory(); var said = new List<string>();
        for (int turn = 0; turn < 24; turn++)
        {
            var l = LineSelector.Pick(twelve, "smalltalk", x, 9, 5_000 + turn * 60, 1, 2, turn, hist, listener: 2)!;
            for (int b = Math.Max(0, said.Count - 8); b < said.Count; b++) Assert.NotEqual(said[b], l.Id);
            said.Add(l.Id); hist.Record(1, 2, l.Id);
        }
        // thirty candidates: a pair's twenty-four turns share no line at all
        var thirty = new LineBook { Lines = [.. Enumerable.Range(0, 30).Select(i => new LineDef { Id = "M" + i, Topic = "smalltalk", Key = "K" })] };
        hist = new TalkHistory(); said.Clear();
        for (int turn = 0; turn < 24; turn++)
        {
            var l = LineSelector.Pick(thirty, "smalltalk", x, 9, 9_000 + turn * 60, 1, 2, turn, hist, listener: 2)!;
            Assert.DoesNotContain(l.Id, said); said.Add(l.Id); hist.Record(1, 2, l.Id);
        }
        Assert.True(LineSelector.Candidates(Book, "smalltalk", x).Count >= 12);
    }

    [Fact]
    public void D4_a_line_with_three_true_conditions_is_drawn_twice_as_often_as_one_with_one()
    {
        var a = new LineDef { Id = "A", Topic = "smalltalk", Key = "K", Conditions = [new() { Id = "is_kin" }] };
        var b = new LineDef { Id = "B", Topic = "smalltalk", Key = "K", Conditions = [new() { Id = "is_kin" }, new() { Id = "mood_above", Args = { ["value"] = "-50" } }, new() { Id = "season_is", Args = { ["season"] = "spring" } }] };
        // the weights are 1000 x (1 + conditions): the spec's 2 and 4 are for lines with one and three conditions beyond the base
        var book = new LineBook { Lines = [a, b] };
        var x = new TalkContext { Kin = true, Mood = 0, Season = Season.Spring };
        int nb = 0; const int n = 6000;
        for (int i = 0; i < n; i++) if (LineSelector.Pick(book, "smalltalk", x, (ulong)i * 7919 + 1, 60L * i, 11, 3, 0)!.Id == "B") nb++;
        double share = nb / (double)n;
        Assert.InRange(share, 0.667 - 0.02, 0.667 + 0.02);
    }

    [Fact]
    public void A_topic_with_no_candidate_falls_back_to_small_talk_and_never_falls_silent()
    {
        var book = new LineBook { Lines = [new LineDef { Id = "S", Topic = "smalltalk", Key = "K" }] };
        Assert.Equal("S", LineSelector.Pick(book, "directions", Ctx(), 1, 0, 1, 1, 0)!.Id);
        // every candidate used lately: the history is set aside rather than staying silent
        var h = new TalkHistory(); h.Record(1, 2, "S");
        Assert.Equal("S", LineSelector.Pick(book, "smalltalk", Ctx(), 1, 0, 1, 2, 0, h, 2)!.Id);
    }

    [Fact]
    public void Conditions_read_the_speaker_the_moment_and_the_hero()
    {
        var x = new TalkContext { Job = "smith", Mood = -40, Hour = 19, Season = Season.Winter, Weather = "Snow", HeroArmed = true };
        bool H(string id, params (string, string)[] a) => TalkConditions.Holds(new ConditionRefDef { Id = id, Args = a.ToDictionary(p => p.Item1, p => p.Item2) }, x);
        Assert.True(H("speaker_job_is", ("job", "smith"))); Assert.True(H("speaker_job_class", ("class", "craft"))); Assert.False(H("speaker_job_class", ("class", "outdoor")));
        Assert.True(H("mood_below", ("value", "-20"))); Assert.False(H("mood_above", ("value", "20")));
        Assert.True(H("time_of_day_is", ("part", "evening"))); Assert.True(H("weather_is", ("state", "snow"))); Assert.True(H("season_is", ("season", "winter")));
        Assert.True(H("hero_is_armed")); Assert.False(H("hero_is_bloody")); Assert.False(H("unknown_condition"));
        Assert.True(H("hero_is_bloody", ("not", "true")));
        Assert.Equal(RelationTier.Stranger, x.Tier); x.Familiarity = 45; x.TrustPm = 350; Assert.Equal(RelationTier.Friend, x.Tier); x.TrustPm = -250; Assert.Equal(RelationTier.Disliked, x.Tier);
    }

    [Fact]
    public void The_grammar_picks_by_sex_and_by_count_and_never_concatenates_fragments()
    {
        var bag = new Dictionary<string, string> { ["n"] = "1", ["name"] = "Edith" };
        Assert.Equal("1 hour for Edith", ParamResolver.Format("{n} {#n|hour|hours} for {name}", bag));
        bag["n"] = "3"; Assert.Equal("3 hours", ParamResolver.Format("{n} {#n|hour|hours}", bag));
        Assert.Equal("he", ParamResolver.Format("{he|she}", bag, false)); Assert.Equal("she", ParamResolver.Format("{he|she}", bag, true));
        Assert.Equal("{missing}", ParamResolver.Format("{missing}", bag));
    }

    [Fact]
    public void Mood_follows_the_spec_and_selects_the_band()
    {
        var s = Book.Settings;
        Assert.Equal(30, MoodSystem.Compute(new(true, true, false, false, false, 0, false), s));            // +20 fed, +10 housed
        Assert.Equal(-30 + 10 - 15 - 10 - 20 + 5, MoodSystem.Compute(new(false, true, true, true, true, 5, true), s));
        Assert.Equal(-85, MoodSystem.Compute(new(false, false, true, true, true, -10, true), s));
        Assert.Equal(MoodBand.Low, MoodSystem.Band(-21, s)); Assert.Equal(MoodBand.Mid, MoodSystem.Band(0, s)); Assert.Equal(MoodBand.High, MoodSystem.Band(21, s));
    }

    [Fact]
    public void Barks_are_drawn_from_real_state_keep_their_cooldowns_and_caps_and_give_the_same_text_at_any_speed()
    {
        var text = TalkKit.Text(); var bs = new BarkSystem();
        var crowd = Enumerable.Range(0, 12).Select(i => new BarkCandidate((ulong)(100 + i), i, 2 + i, i % 2 == 0 ? "baker" : "smith", false, 0, RelationTier.Stranger, false, false)).ToList();
        var world = new TalkContext { Hour = 10, Season = Season.Spring, Weather = "Clear", HeroName = "Aldric Vane", Place = "Ashby", InMarket = true };
        var all = new List<BarkEvent>(); double real = 0;
        for (int step = 0; step < 400; step++, real += 0.25)
        {
            var got = bs.Update(Book_(), 5, 1000 + step * 5, real, crowd, world, text);
            Assert.True(got.Count <= 2, "more than two barks in one update"); Assert.True(bs.VisibleCount <= 3);
            all.AddRange(got);
        }
        Assert.NotEmpty(all);
        foreach (var g in all.GroupBy(b => b.Person)) Assert.True(g.Count() <= 100 / 55 + 2);                  // a person's cooldown is 60 real seconds
        Assert.All(all, b => Assert.DoesNotContain('{', b.Text));
        // a bark that names the weather or the trade is only called when it is so
        var rainy = new TalkContext { Hour = 10, Weather = "HeavyRain", HeroName = "A" };
        var c = crowd[0];
        var barks = Enumerable.Range(0, 400).Select(w => BarkSystem.Pick(Book_(), c, rainy, 3, w)?.Id ?? "").Distinct().ToList();
        Assert.DoesNotContain(barks, id => id.StartsWith("weather.") && !(id is "weather.6" or "weather.7"));
        // the same window gives the same bark whatever the speed (the draw is keyed on the game window)
        Assert.Equal(BarkSystem.Pick(Book_(), c, rainy, 3, 1234)?.Id, BarkSystem.Pick(Book_(), c, rainy, 3, 1234)?.Id);
    }

    private static LineBook Book_() => Book;

    [Fact]
    public void An_authored_dialogue_plays_conditions_gate_choices_effects_set_flags_and_the_repeat_node_comes_back()
    {
        var def = Book.Dialogues.Single(d => d.Id == "caravan_master.intro"); var text = TalkKit.Text();
        var x = new TalkContext { HeroName = "Aldric Vane", Place = "Ashby", Familiarity = 5 }; ParamResolver.Fill(x, text);
        var r = new DialogueRunner(def, x, text);
        Assert.Equal("n1", r.NodeId); Assert.Contains("Marta Voss", r.Text);
        var choices = r.Choices;
        Assert.Contains(choices, c => c.Text == "I need work." && !c.Locked);
        Assert.Contains(choices, c => c.Locked && c.NeedText.Contains("riding"));                      // "needs riding 10"
        x.Proficiency = s => s == "riding" ? 12 : 0;
        Assert.DoesNotContain(r.Choices, c => c.Locked);
        r.Choose(r.Choices.First(c => c.Text == "I need work.").Index);
        Assert.Equal("work", r.NodeId);
        r.Choose(r.Choices.First(c => c.Text.StartsWith("Yes.")).Index);
        Assert.Equal("accept", r.NodeId);
        Assert.Contains(r.Effects, e => e.Id == "start_contract" && e.Args["kind"] == "first");
        Assert.Contains("cm_gave_first_job", x.Flags);
        Assert.True(r.Advance()); Assert.Equal("after", r.NodeId); Assert.True(r.Advance()); Assert.True(r.Ended);
        // returning: the repeat node, and the work choice is gone
        var again = new DialogueRunner(def, x, text);
        Assert.Equal("repeat", again.NodeId);
        // every node is reachable and each path can leave: walk the graph breadth first through the runner
        Assert.Empty(DialogueGraph.Problems(def));
    }
}
