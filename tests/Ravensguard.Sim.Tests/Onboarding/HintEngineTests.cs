using Ravensguard.Content;
using Ravensguard.Sim.Onboarding;
using Ravensguard.Sim.Tests.Dialogue;

namespace Ravensguard.Sim.Tests.Onboarding;

public class HintEngineTests
{
    static readonly IReadOnlyList<HintDef> Hints = OnboardingContent.From(OnbKit.Db).Hints;
    static string Text(string k) => TalkKit.EnCsv()[k];
    static HintEngine Make(HintSetting s, Func<string, string>? keys = null, HintProfile? p = null)
        => new(Hints, p ?? new HintProfile { Setting = s }, Text, keys ?? (a => "[" + a + "]"));

    /// <summary>Raises the event of every hint with its own kind and threshold, as a scripted hour would.</summary>
    static void RaiseAll(HintEngine e, double at = 0)
    {
        foreach (var h in Hints)
        {
            long v = h.Trigger.Args.TryGetValue("min", out var m) ? long.Parse(m) : 0;
            e.Raise(h.Trigger.Event, h.Trigger.Args.GetValueOrDefault("kind", ""), v, at);
        }
    }

    /// <summary>A scripted hour: every event is raised again at each step (so none ages out), a toast is taken every seven seconds.</summary>
    static List<string> Drain(HintEngine e, double start = 0, bool blocked = false)
    {
        var shown = new List<string>(); double t = start;
        for (int i = 0; i < 200; i++, t += 7) { RaiseAll(e, t); var x = e.Next(t, blocked); if (x != null) shown.Add(x.Id); }
        return shown;
    }

    [Fact]
    public void O8_every_hint_fires_once_per_profile_and_never_again_in_a_new_world()
    {
        var p = new HintProfile { Setting = HintSetting.Full };
        var e = Make(HintSetting.Full, p: p);
        var shown = Drain(e);
        Assert.Equal(40, shown.Count); Assert.Equal(40, shown.Distinct().Count());
        // the same events again: nothing
        Assert.Empty(Drain(e, 10_000));
        // a new world on the same profile (stored as json, not in the world save): still nothing
        var again = Make(HintSetting.Full, p: HintProfile.FromJson(p.ToJson()));
        Assert.Empty(Drain(again));
        // resetting the hints in Settings teaches them again
        p.ResetSeen(); Assert.Equal(40, Drain(e, 20_000).Count);
    }

    [Fact]
    public void O8_a_hint_waits_for_a_modal_or_a_dialogue_and_shows_one_at_a_time_for_six_seconds()
    {
        var e = Make(HintSetting.Full); RaiseAll(e);
        Assert.Null(e.Next(1, blocked: true));                        // in dialogue, combat or a modal: nothing
        var first = e.Next(2, false); Assert.NotNull(first);
        Assert.Null(e.Next(7.9, false));                              // the first toast is still up (6 s)
        Assert.NotNull(e.Next(8.1, false));
    }

    [Fact]
    public void O8_hints_older_than_twenty_seconds_are_dropped_and_the_highest_priority_goes_first()
    {
        var e = Make(HintSetting.Full);
        e.Raise("need_above", "hunger", 700, 0); e.Raise("need_above", "cold", 400, 0);
        Assert.Equal("hint.cold", e.Next(1, false)!.Id);              // cold (88) before hunger (85)
        Assert.Equal("hint.hunger", e.Next(8, false)!.Id);
        e.Raise("need_above", "fatigue", 800, 0);
        Assert.Null(e.Next(30, false));                               // waited 30 s while blocked: dropped
        e.Raise("need_above", "fatigue", 800, 40); e.Satisfied("hint.fatigue");
        Assert.Null(e.Next(41, false));                               // the player already did it
    }

    [Fact]
    public void O8_key_tokens_follow_the_bindings_so_a_rebound_key_shows_correctly()
    {
        var e = Make(HintSetting.Full, a => a == "world.act" ? "[F]" : "[E]");
        e.Raise("near_object", "rideable_animal", 3, 0);
        var t = e.Next(0, false)!;
        Assert.Equal("hint.mount", t.Id); Assert.StartsWith("Press [F] to mount.", t.Text); Assert.DoesNotContain("{@", t.Text);
        Assert.Equal("rule.horses", t.LearnMore);
        var e2 = Make(HintSetting.Full, a => a == "world.act" ? "[E]" : "[?]");
        e2.Raise("near_object", "rideable_animal", 3, 0); Assert.StartsWith("Press [E] to mount.", e2.Next(0, false)!.Text);
    }

    [Fact]
    public void O9_off_shows_nothing_minimal_shows_only_the_twelve_and_full_shows_all_that_triggered()
    {
        var off = Make(HintSetting.Off); Assert.Empty(Drain(off)); Assert.Empty(off.Shown);
        var min = Make(HintSetting.Minimal);
        var m = Drain(min);
        Assert.Equal(12, m.Count);
        Assert.All(m, id => Assert.Equal("minimal", Hints.Single(h => h.Id == id).Level));
        var full = Make(HintSetting.Full); Assert.Equal(40, Drain(full).Count);
        // only what triggered: two events raised, two hints
        var some = Make(HintSetting.Full); some.Raise("trade_opened"); some.Raise("job_board_seen");
        var two = new List<string>(); for (double t = 0; t < 100; t += 7) if (some.Next(t, false) is { } x) two.Add(x.Id);
        Assert.Equal(2, two.Count);
    }

    [Fact]
    public void The_default_setting_is_full_for_the_first_sixty_minutes_of_the_first_character_and_minimal_after()
    {
        var p = new HintProfile();
        var e = Make(HintSetting.Auto, p: p);
        Assert.Equal(HintLevel.Full, e.Level);
        p.FirstCharacterMinutes = 59; Assert.Equal(HintLevel.Full, e.Level);
        p.FirstCharacterMinutes = 60; Assert.Equal(HintLevel.Minimal, e.Level);
        p.FirstCharacterMinutes = 0; p.OnboardingComplete = true; Assert.Equal(HintLevel.Minimal, e.Level);
        p.Setting = HintSetting.Full; Assert.Equal(HintLevel.Full, e.Level);       // the Settings screen sets it explicitly
    }

    [Fact]
    public void Every_hint_has_a_codex_page_name_a_priority_and_text_that_names_only_known_key_tokens()
    {
        var tokens = new System.Text.RegularExpressions.Regex(@"\{@([a-z_.]+)\}");
        foreach (var h in Hints)
        {
            Assert.StartsWith("rule.", h.LearnMore);
            foreach (System.Text.RegularExpressions.Match m in tokens.Matches(Text(h.Text))) Assert.Matches(@"^(world|mounted|trade|menu)\.[a-z_0-9]+$", m.Groups[1].Value);
            Assert.True(Text(h.Text).Length <= 160, h.Id);
        }
    }
}
