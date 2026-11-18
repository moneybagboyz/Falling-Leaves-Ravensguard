using Ravensguard.Content;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Dialogue;
using Ravensguard.Sim.People;
using Ravensguard.Sim.Rumours;
using Ravensguard.Sim.Save;

namespace Ravensguard.Sim.Tests.Dialogue;

/// <summary>R-1 to R-6: rumours from the chronicle, spreading person to person, the trace back to the source, and the save.</summary>
public class RumourTests
{
    private static readonly LineBook Book = LineBook.Default;

    /// <summary>Runs <paramref name="days"/> game days of hours on the town from the first hour after the entry; returns the state.</summary>
    private static TalkState Run(TalkWorld w, int days, long fromHour, TalkState? ts = null, Action<long>? each = null)
    {
        ts ??= new TalkState();
        for (long h = fromHour; h < fromHour + days * 24L; h++) { TalkSystem.AfterHour(w, ts, Book, h); each?.Invoke(h); }
        return ts;
    }

    private static int Reach(TalkState ts, EntityId r) => ts.Rumours.HearingsOf(r).Count;

    /// <summary>Median day on which half of the residents hold the rumour (days after the entry), over runs with different seeds.</summary>
    private static double MedianHalfDay(int people, int runs, string type)
    {
        var days = new List<double>();
        for (int run = 0; run < runs; run++)
        {
            var (w, c) = TalkKit.Town(people, seed: (ulong)(1000 + run * 37));
            var id = TalkKit.AddEntry(c, type, 100, "Ashby"); var ts = new TalkState(); double found = 99;
            for (long h = 100 * 24 + 7; h < 100 * 24 + 30 * 24; h++)
            {
                TalkSystem.AfterHour(w, ts, Book, h);
                if (Reach(ts, id) * 2 >= people) { found = (h - (100 * 24 + 6)) / 24.0; break; }
            }
            days.Add(found);
        }
        days.Sort(); return days[days.Count / 2];
    }

    [Fact]
    public void A_chronicle_entry_becomes_a_rumour_with_witnesses_who_hold_a_hop_zero_hearing()
    {
        var (w, c) = TalkKit.Town(300);
        var id = TalkKit.AddEntry(c, "fire", 100, "Ashby"); var ts = new TalkState();
        TalkSystem.AfterHour(w, ts, Book, 100 * 24 + 7);
        var r = ts.Rumours.Get(id)!;
        Assert.Equal(RumourKind.Fire, r.Kind); Assert.Equal("Ashby", r.Place); Assert.Equal(id, r.Source);
        Assert.Equal(5, r.Witnesses.Length);
        foreach (int p in r.Witnesses) { var h = ts.Rumours.Find(id, p)!.Value; Assert.Equal(-1, h.Teller); Assert.Equal(0, h.Hop); Assert.Equal(HearHow.Witnessed, h.How); }
        Assert.Equal(12, r.LifeDays); Assert.Equal(800, r.InterestPm); Assert.Equal(112, r.ExpiresDay);
        // an entry with no row, or too old to be talked of, makes none
        TalkKit.AddEntry(c, "birth", 100, "Ashby"); TalkKit.AddEntry(c, "fire", 10, "Ashby");
        TalkSystem.AfterHour(w, ts, Book, 100 * 24 + 8);
        Assert.Equal(1, ts.Rumours.Count);
    }

    [Fact]
    public void R1_every_hearing_traces_back_to_a_witness_and_the_chronicle_entry_with_hops_strictly_decreasing()
    {
        var (w, c) = TalkKit.Town(1000);
        var id = TalkKit.AddEntry(c, "famine", 100, "Ashby", ("hungry_households", "40")); TalkKit.AddEntry(c, "outbreak", 101, "Ashby", ("severity", "mild"));
        var ts = Run(w, 14, 100 * 24 + 7);
        int checkedN = 0;
        foreach (var (rid, r) in ts.Rumours.Rumours)
            foreach (var h in ts.Rumours.HearingsOf(rid))
            {
                var chain = RumourTrace.Walk(ts.Rumours, rid, h.Listener);
                Assert.True(RumourTrace.Valid(ts.Rumours, c, rid, chain, out var why), $"hearing of {rid} by {h.Listener}: {why}");
                Assert.Equal(h.Hop, chain[0].Hop); checkedN++;
            }
        Assert.True(checkedN >= 800, $"only {checkedN} hearings traced");
        Assert.True(ts.Rumours.Rumours.Values.Any(r => ts.Rumours.HearingsOf(r.Id).Max(h => h.Hop) >= 3), "no rumour travelled three retellings");
    }

    [Fact]
    public void R2_the_done_when_the_hero_asks_the_news_in_ten_towns_and_every_journal_rumour_expands_to_a_chain_ending_at_a_witness()
    {
        var (w, c) = TalkKit.Town(150, settlements: 10);
        for (int s = 0; s < 10; s++) TalkKit.AddEntry(c, s % 2 == 0 ? "famine" : "outbreak", 100 + s % 3, w.SettlementNames[s], ("hungry_households", "12"), ("severity", "severe"));
        var ts = new TalkState(); int asked = 0, cards = 0;
        for (long h = 100 * 24 + 8; h < 112 * 24; h++)
        {
            TalkSystem.AfterHour(w, ts, Book, h);
            if (h % 24 != 20 || h < 102 * 24) continue;                       // each evening from day 102 the hero asks in each town
            for (int s = 0; s < 10; s++)
            {
                foreach (int p in Enumerable.Range(s * 150, 150).Where(p => ts.Rumours.HeldBy(p).Count > 0).Take(1))
                {
                    asked++;
                    var told = TalkSystem.WhatTheyTell(ts, p, h);
                    if (told == null) continue;
                    var hh = TalkSystem.HeroHears(w, ts, told.Rumour, p, h, HearHow.Told)!; cards++;
                    Assert.True(RumourTrace.Valid(ts.Rumours, c, hh.Rumour, hh.Chain, out var why), why);
                    Assert.Equal(hh.Hop, hh.Chain[0].Hop + 1);
                    Assert.True(w.Chronicle.Get(ts.Rumours.Get(hh.Rumour)!.Source) != null);
                }
            }
        }
        Assert.True(asked >= 50 && cards >= 8, $"asked {asked}, heard {cards}");
        var view = TalkSystem.Journal(w, ts, 112 * 24);
        Assert.Equal(cards, view.Cards.Count);
        foreach (var card in view.Cards)
        {
            Assert.True(card.Source.Found); Assert.NotEmpty(card.Steps); Assert.Equal("", card.Steps[^1].From);        // the last step is a witness
            Assert.Equal(card.PassedThrough, card.Steps.Count); Assert.NotEmpty(card.TellerName);
            Assert.NotEmpty(card.Source.Causes);
        }
    }

    [Fact]
    public void R3_spread_rate_a_fire_reaches_half_a_town_in_6_to_10_days_and_half_a_village_in_3_to_6()
    {
        double town = MedianHalfDay(1000, 20, "fire"), village = MedianHalfDay(150, 20, "fire");
        Assert.InRange(town, 6, 10); Assert.InRange(village, 3, 6);
    }

    [Fact]
    public void Decay_a_rumour_is_told_less_as_it_ages_and_expires_with_its_hearings_unless_the_hero_keeps_it()
    {
        var (w, c) = TalkKit.Town(400);
        var id = TalkKit.AddEntry(c, "tax_evasion", 100, "Ashby", ("households", "6")); var other = TalkKit.AddEntry(c, "riot", 100, "Ashby", ("rioters", "9"));
        var ts = new TalkState(); int early = 0, late = 0;
        for (long h = 100 * 24 + 7; h < 100 * 24 + 24 * 4; h++) { int b = ts.Rumours.Get(id)?.Witnesses.Length ?? 0; TalkSystem.AfterHour(w, ts, Book, h); }
        early = Reach(ts, id);
        // the same rumour seeded when almost dead spreads far less
        var (w2, c2) = TalkKit.Town(400); var old = TalkKit.AddEntry(c2, "tax_evasion", 100, "Ashby", ("households", "6")); var ts2 = new TalkState();
        TalkSystem.AfterHour(w2, ts2, Book, 100 * 24 + 7);
        for (long h = 109 * 24; h < 109 * 24 + 24 * 3; h++) TalkSystem.AfterHour(w2, ts2, Book, h);
        late = Reach(ts2, old);
        Assert.True(early > late * 2 || late <= 12, $"early {early}, late {late}");
        // the hero holds the riot; at its end the other is purged and the riot is kept
        var hero = TalkSystem.HeroHears(w, ts, other, ts.Rumours.Get(other)!.Witnesses[0], 100 * 24 + 100, HearHow.Told);
        Assert.NotNull(hero);
        for (long h = 100 * 24 + 100; h < 130 * 24; h++) TalkSystem.AfterHour(w, ts, Book, h);
        Assert.Null(ts.Rumours.Get(id)); Assert.NotNull(ts.Rumours.Get(other));
        Assert.Empty(ts.Rumours.HearingsOf(id));
    }

    [Fact]
    public void R4_distortion_is_five_percent_of_hops_and_one_percent_misattributed_and_only_flags()
    {
        var (w, c) = TalkKit.Town(10, settlements: 3);
        var r = new Rumour { Id = new EntityId(1), Settlement = 0, Place = "Ashby", Number = 8 };
        int any = 0, mis = 0, vague = 0, exag = 0; const int n = 10_000;
        for (int i = 0; i < n; i++)
        {
            var (f, alt) = RumourSpreader.Distort(w, Book.Settings, r, i, 5000);
            if (f != Distortion.None) any++; if (f == Distortion.Misattributed) { mis++; Assert.InRange((int)alt, 1, 2); } if (f == Distortion.Vague) vague++; if (f == Distortion.Exaggerated) exag++;
        }
        Assert.InRange(any / (double)n, 0.045, 0.055); Assert.InRange(mis / (double)n, 0.005, 0.015); Assert.True(vague > 100 && exag > 100);
        // what is told changes, what is true does not
        var text = TalkKit.Text();
        string truth = RumourText.Render(new Rumour { Key = "R_RIOT", Place = "Ashby", Number = 8 }, null, w.SettlementNames, text);
        var bad = new Hearing { Flags = Distortion.Exaggerated | Distortion.Vague };
        Assert.Contains("8", truth); Assert.Contains("Ashby", truth);
        string told = RumourText.Render(new Rumour { Key = "R_RIOT", Place = "Ashby", Number = 8 }, bad, w.SettlementNames, text);
        Assert.Contains("10", told); Assert.DoesNotContain("Ashby", told); Assert.Contains("somewhere in the east", told);
        Assert.Equal(8, r.Number); Assert.Equal("Ashby", r.Place);
    }

    [Fact]
    public void R5_caps_hold_and_the_table_hash_is_the_same_after_a_save_and_load()
    {
        var (w, c) = TalkKit.Town(600);
        for (int i = 0; i < 12; i++) TalkKit.AddEntry(c, i % 2 == 0 ? "famine" : "riot", 100 + i % 3, "Ashby", ("hungry_households", "9"), ("rioters", "7"));
        var ts = Run(w, 6, 100 * 24 + 7);
        int tellable(int p) => ts.Rumours.HeldBy(p).Count;
        Assert.True(ts.Rumours.Held.Keys.All(p => tellable(p) <= 6)); Assert.True(ts.Rumours.Held.Keys.Any(p => tellable(p) == 6), "no one held six");
        Assert.True(ts.Rumours.HearingCount <= Book.Settings.HearingsMax);
        // a dropped hearing is kept for its trace
        foreach (var (rid, _) in ts.Rumours.Rumours) foreach (var h in ts.Rumours.HearingsOf(rid))
            Assert.True(RumourTrace.Valid(ts.Rumours, c, rid, RumourTrace.Walk(ts.Rumours, rid, h.Listener), out var why), why);
        var before = ts.Hash();
        var back = TalkSave.Capture(ts).Restore();
        Assert.Equal(before, back.Hash());
    }

    [Fact]
    public void Save_load_save_is_byte_identical_with_rumours_in_flight_and_the_hero_holding_some()
    {
        var (w, c) = TalkKit.Town(400);
        TalkKit.AddEntry(c, "famine", 100, "Ashby", ("hungry_households", "20")); TalkKit.AddEntry(c, "caravan_lost", 101, "Ashby");
        var ts = Run(w, 5, 100 * 24 + 7);
        var teller = ts.Rumours.Held.Keys.OrderBy(k => k).First(k => ts.Rumours.HeldBy(k).Count > 0);
        TalkSystem.HeroHears(w, ts, ts.Rumours.HeldBy(teller)[0], teller, 105 * 24, HearHow.Overheard);
        TalkSystem.AddAuthored(w, ts, "road_bandits", "the ford", teller, 105 * 24);
        ts.Flags.Add("cm_gave_first_job"); ts.Relations.Met(3, 4, 105, Book.Settings);
        var a = SaveCodec.SerializeCompressed(TalkSave.Capture(ts));
        var back = SaveCodec.DeserializeCompressed<TalkSave>(a).Restore();
        var b = SaveCodec.SerializeCompressed(TalkSave.Capture(back));
        Assert.Equal(a, b);
        Assert.Equal(ts.Hash(), back.Hash()); Assert.Equal(2, back.Heard.Count); Assert.Contains("cm_gave_first_job", back.Flags);
        // and the world carries on the same from either
        for (long h = 105 * 24; h < 108 * 24; h++) { TalkSystem.AfterHour(w, ts, Book, h); TalkSystem.AfterHour(w, back, Book, h); }
        Assert.Equal(ts.Hash(), back.Hash());
    }

    [Fact]
    public void The_same_world_run_twice_gives_the_same_talk_hash_every_day_and_a_different_seed_a_different_one()
    {
        ulong[] Hashes(ulong seed)
        {
            var (w, c) = TalkKit.Town(500, seed); TalkKit.AddEntry(c, "outbreak", 100, "Ashby", ("severity", "mild")); var ts = new TalkState(); var res = new List<ulong>();
            for (long h = 100 * 24 + 7; h < 105 * 24; h++) { TalkSystem.AfterHour(w, ts, Book, h); if (h % 24 == 23) res.Add(ts.Hash()); }
            return [.. res];
        }
        Assert.Equal(Hashes(11), Hashes(11)); Assert.NotEqual(Hashes(11), Hashes(12));
    }

    [Fact]
    public void Overhearing_gives_the_hero_a_rumour_a_teller_near_them_just_passed()
    {
        var (w, c) = TalkKit.Town(300);
        TalkKit.AddEntry(c, "riot", 100, "Ashby", ("rioters", "9")); var ts = new TalkState();
        long h = 100 * 24 + 7; TalkSystem.AfterHour(w, ts, Book, h);
        Pass? pass = null;
        for (; h < 102 * 24 && pass == null; h++) { TalkSystem.AfterHour(w, ts, Book, h); if (ts.Passes.Count > 0) pass = ts.Passes[0]; }
        Assert.NotNull(pass);
        Assert.Null(TalkSystem.Overhear(w, ts, 0, new HashSet<int> { 299 }, h));                              // nobody of the group near the hero
        var got = TalkSystem.Overhear(w, ts, 0, new HashSet<int> { pass!.Value.Teller }, h - 1 + 0)!;
        Assert.Equal(HearHow.Overheard, got.How); Assert.Equal(pass.Value.Teller, got.Teller);
        Assert.True(RumourTrace.Valid(ts.Rumours, c, got.Rumour, got.Chain, out var why), why);
        Assert.Null(TalkSystem.Overhear(w, ts, 0, new HashSet<int> { pass.Value.Teller }, h));                // not twice
    }

    [Fact]
    public void An_authored_add_rumour_traces_to_the_chronicle_when_the_world_has_one_of_that_kind_and_says_so_when_not()
    {
        var (w, c) = TalkKit.Town(200);
        var ts = new TalkState();
        var none = TalkSystem.AddAuthored(w, ts, "road_bandits", "the ford", 0, 100 * 24)!;
        Assert.True(ts.Rumours.Get(none.Rumour)!.Authored);
        var id = TalkKit.AddEntry(c, "caravan_lost", 100, "Ashby"); TalkSystem.AfterHour(w, ts, Book, 100 * 24 + 7);
        var real = TalkSystem.AddAuthored(w, ts, "road_bandits", "the ford", 0, 100 * 24 + 8)!;
        Assert.Equal(id, real.Rumour); Assert.False(ts.Rumours.Get(id)!.Authored);
    }
}
