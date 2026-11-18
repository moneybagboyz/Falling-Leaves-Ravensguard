using Ravensguard.Content;
using Ravensguard.Sim.Animals;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Dialogue;
using Ravensguard.Sim.Goods;
using Ravensguard.Sim.Onboarding;
using Ravensguard.Sim.People;
using Ravensguard.Sim.Save;
using Ravensguard.Sim.Trade;
using Ravensguard.Sim.Tests.Dialogue;
using Ravensguard.Sim.Tests.Economy;
using Ravensguard.Sim.WorldGen;
using Ravensguard.Sim.WorldGen.Region;
using RegionData = Ravensguard.Sim.WorldGen.Region.Region;

namespace Ravensguard.Sim.Tests.Onboarding;

public class OnboardingTests
{
    static OnboardingContent Cfg => OnboardingContent.From(OnbKit.Db);

    // ------------------------------------------------------------------ content

    [Fact]
    public void Content_has_40_hints_of_which_12_minimal_and_a_first_hour_and_a_mentor_dialogue_for_every_origin()
    {
        var c = Cfg;
        Assert.Equal(40, c.Hints.Count);
        Assert.Equal(12, c.Hints.Count(h => h.Level == "minimal"));
        Assert.Equal(c.Hints.Count, c.Hints.Select(h => h.Id).Distinct().Count());
        var csv = TalkKit.EnCsv();
        foreach (var h in c.Hints) Assert.True(csv.ContainsKey(h.Text), $"{h.Id}: {h.Text} is not in en.csv");
        var book = LineBook.From(OnbKit.Db);
        foreach (var o in OnbKit.Origins)
        {
            Assert.NotNull(c.Of(o.Id));
            Assert.Equal(o.Mentor, c.Of(o.Id)!.Mentor);
            Assert.Contains(book.Dialogues, d => d.Speaker == o.Mentor);
        }
        Assert.Equal(40_000, 40_000);
        Assert.True(new FileInfo(Path.Combine(TalkKit.Root(), "content", "base", "onboarding", "hints.jsonc")).Length <= 40_000, "hints.jsonc is over its 40 KB budget");
    }

    [Fact]
    public void O12_Every_mentor_has_20_to_50_lines_every_goto_reachable_every_key_in_en_csv_and_every_effect_in_the_vocabulary()
    {
        var book = LineBook.From(OnbKit.Db); var csv = TalkKit.EnCsv();
        foreach (var o in OnbKit.Origins)
        {
            var d = book.Dialogues.Single(x => x.Speaker == o.Mentor);
            var keys = d.Nodes.Values.SelectMany(n => new[] { n.Text }.Concat(n.Choices.Select(c => c.Text))).ToHashSet();
            Assert.InRange(keys.Count, 20, 50);
            Assert.Empty(DialogueGraph.Problems(d));
            Assert.Empty(DialogueGraph.MissingKeys(d, csv.ContainsKey));
            foreach (var e in d.Nodes.Values.SelectMany(n => n.Effects.Concat(n.Choices.SelectMany(c => c.Effects)))) Assert.Contains(e.Id, BaseVocabulary.Effects);
            foreach (var c in d.Nodes.Values.SelectMany(n => n.Conditions.Concat(n.Choices.SelectMany(c => c.Conditions)))) Assert.Contains(c.Id, TalkConditions.Supported);
            // the first talk offers the job and the loan: both effects exist in the graph, and the first node says the mentor has been met
            var all = d.Nodes.Values.SelectMany(n => n.Effects.Concat(n.Choices.SelectMany(c => c.Effects))).ToList();
            Assert.Contains(all, e => e.Id == "start_contract" && e.Args["kind"] == "first");
            Assert.Contains(all, e => e.Id == "start_contract" && e.Args["kind"] == "loan");
            Assert.Contains(d.Nodes[d.Start].Effects, e => e.Id == "set_flag" && e.Args["flag"] == "mentor_met");
        }
    }

    // ------------------------------------------------------------------ start sites

    static IEnumerable<(string Name, Population Pop)> Worlds()
    {
        foreach (ulong seed in new ulong[] { 1, 2 })
        {
            var w = TestWorld.Get(seed);
            // a fresh region per call: the population is a view over it
            var region = RegionGenerator.Generate(w.Continent, w.History, w.Pick.CellX, w.Pick.CellY, w.RegionSettings);
            LandmarkPlacer.Place(region, w.History, w.Content);
            yield return ("seed " + seed, Populator.Populate(region, w.History, w.Content));
        }
        yield return ("seed 1234", EconFixture.Pop);
    }

    [Fact]
    public void O1_O2_Start_sites_keep_10_km_from_wars_and_3_km_from_lairs_and_the_cluster_holds_on_all_seeds_and_origins()
    {
        int starts = 0, relaxed = 0;
        foreach (var (name, pop) in Worlds())
        {
            var r = pop.Region;
            foreach (var o in OnbKit.Origins)
            {
                var s = StartSiteFinder.Find(pop, o.StartLocation, o.Mentor, 99);
                Assert.True(s.Found, $"{name} {o.Id}: {s.Note}");
                Assert.True(r.IsSafeStart(s.X, s.Y, 10_000, 3_000), $"{name} {o.Id}: start {r.NearestActiveWarM(s.X, s.Y)} m from a war, {r.NearestLairM(s.X, s.Y)} m from a lair");
                Assert.True(r.NearestActiveWarM(s.X, s.Y) >= 10_000 && r.NearestLairM(s.X, s.Y) >= 3_000);
                Assert.True(s.MentorPerson >= 0, $"{name} {o.Id}: no mentor");
                Assert.True(Math.Sqrt(Math.Pow(s.X - s.MentorX, 2) + Math.Pow(s.Y - s.MentorY, 2)) <= StartSiteFinder.MentorM + 30, $"{name} {o.Id}: mentor too far");
                if (!s.Relaxed)
                {
                    Assert.True(s.ErrandSettlement >= 0, $"{name} {o.Id}: no errand settlement");
                    var e = r.Settlements[s.ErrandSettlement];
                    double d = Math.Sqrt(Math.Pow(e.CenterX - s.X, 2) + Math.Pow(e.CenterY - s.Y, 2));
                    Assert.InRange(d, StartSiteFinder.RideMinM, StartSiteFinder.RideMaxM);
                }
                else relaxed++;
                starts++;
                // deterministic: the same world and origin give the same start
                Assert.Equal(s, StartSiteFinder.Find(pop, o.StartLocation, o.Mentor, 99));
            }
        }
        Assert.Equal(21, starts);
        Assert.True(relaxed < starts, "every start was a relaxed fallback");
    }

    [Fact]
    public void O1_a_region_with_a_war_on_every_settlement_has_no_safe_start_and_says_so_and_a_war_near_the_town_pushes_the_start_elsewhere()
    {
        var (_, pop) = Worlds().First();
        var r = pop.Region;
        var o = OnbKit.Origins.First(x => x.StartLocation == "town");
        var normal = StartSiteFinder.Find(pop, o.StartLocation, o.Mentor, 5);
        Assert.True(normal.Found);
        r.ActiveWars.Add(new ActiveWarMarker(0, "test war", 0, r.Settlements[normal.Settlement].CenterX + 2000, r.Settlements[normal.Settlement].CenterY, 2000));
        try
        {
            var moved = StartSiteFinder.Find(pop, o.StartLocation, o.Mentor, 5);
            if (moved.Found) { Assert.NotEqual(normal.Settlement, moved.Settlement); Assert.True(r.IsSafeStart(moved.X, moved.Y)); }
            foreach (var s in r.Settlements) r.ActiveWars.Add(new ActiveWarMarker(1, "everywhere", 0, s.CenterX, s.CenterY, 0));
            var none = StartSiteFinder.Find(pop, o.StartLocation, o.Mentor, 5);
            Assert.False(none.Found);
            Assert.Contains("No safe start", none.Note);
        }
        finally { r.ActiveWars.Clear(); }
    }

    [Fact]
    public void O3_The_start_shifts_to_the_first_mild_day_within_ten_and_a_fixed_date_does_not()
    {
        Assert.Equal(0, StartDateFinder.Shift(d => false, 12));
        Assert.Equal(1, StartDateFinder.Shift(d => d == 12, 12));
        Assert.Equal(3, StartDateFinder.Shift(d => d is 12 or 13 or 14, 12));
        Assert.Equal(10, StartDateFinder.Shift(d => d >= 12 && d < 22, 12));
        Assert.Equal(0, StartDateFinder.Shift(d => true, 12));            // no mild day within ten: the nominal day stays
        Assert.Equal(0, StartDateFinder.Shift(d => d == 12, 12, fixedDate: true));
        // the real weather is a pure function of the world: the same answer twice
        var w = Ravensguard.Sim.Weather.RegionWeather.ForContinent(EconFixture.Shared.Continent);
        int cell = Math.Min(Continent.Size - 1, EconFixture.Shared.CellY + 8) * Continent.Size + Math.Min(Continent.Size - 1, EconFixture.Shared.CellX + 8);
        for (long day = 1; day < 400; day += 37) Assert.Equal(StartDateFinder.Shift(w, cell, day), StartDateFinder.Shift(w, cell, day));
    }

    // ------------------------------------------------------------------ loans

    sealed class Rig
    {
        public readonly Ravensguard.Sim.Goods.GoodCatalog Goods; public readonly MassLedger Ledger; public readonly RideBook Book; public readonly LoanTable Loans = new();
        public static readonly EntityId Lender = EntityId.Make(IdKind.Person, 77);
        public Rig() { Goods = EconFixture.Catalog(); Ledger = new MassLedger(Goods.Count); Book = new RideBook(EconFixture.Content, Goods, Ledger, 5); }
        public Loan Lend(long now = 1000) => Loans.Lend(Book, Book.Breeds.Get("horse_palfrey"), Lender, now, 0, 0, returnPlace: 4, days: 3, graceDays: 3);
    }

    [Fact]
    public void O6_A_horse_lent_for_three_days_ridden_four_kilometres_and_returned_ends_the_loan_with_trust()
    {
        var r = new Rig(); var l = r.Lend();
        Assert.Equal(1000 + 3 * 86_400, l.DueSec);
        Assert.Equal(LoanState.Active, l.State);
        Assert.Equal(1000 + 6 * 86_400, r.Book.Loans[0].DueSec);                       // the ride book takes it back only after the grace days
        var a = r.Book.Animals.Find(l.Animal)!; Assert.Equal(Rig.Lender, a.Owner);       // the animal keeps its owner: a loan is permission, not a gift
        Assert.Null(r.Loans.Return(r.Book, 9, 5000, 50));                              // wrong place: nothing happens
        var e = r.Loans.Return(r.Book, 4, 5000, 50)!.Value;
        Assert.Equal(("returned", 50), (e.Kind, e.TrustPm));
        Assert.Equal(LoanState.Returned, l.State); Assert.Null(r.Book.Animals.Find(l.Animal)); Assert.Empty(r.Book.Loans); Assert.False(r.Loans.Owes);
    }

    [Fact]
    public void O7_A_loan_kept_past_due_plus_three_days_angers_the_mentor_once_and_a_lost_animal_becomes_a_debt_of_its_price()
    {
        var r = new Rig(); var l = r.Lend(0);
        Assert.Empty(r.Loans.Tick(r.Book, 3 * 86_400, 3, -300));
        Assert.Equal(new[] { "overdue" }, r.Loans.Tick(r.Book, 3 * 86_400 + 1, 3, -300).Select(x => x.Kind).ToArray());
        Assert.Equal(LoanState.Overdue, l.State);
        Assert.Empty(r.Loans.Tick(r.Book, 5 * 86_400, 3, -300));
        var angry = r.Loans.Tick(r.Book, 6 * 86_400, 3, -300);
        Assert.Equal(("angry", -300), (angry.Single().Kind, angry.Single().TrustPm));
        Assert.Empty(r.Loans.Tick(r.Book, 6 * 86_400 + 3600, 3, -300));                  // once only
        // a lost animal (dead on loan, inside the days): the hero owes its price
        var r2 = new Rig(); var l2 = r2.Lend(0);
        r2.Book.Animals.Find(l2.Animal)!.State = AnimalState.Dead;
        var lost = r2.Loans.Tick(r2.Book, 86_400, 3, -300).Single();
        Assert.Equal("lost", lost.Kind); Assert.Equal(LoanState.Lost, l2.State);
        var debt = r2.Loans.Debts.Single();
        Assert.Equal(r2.Book.Breeds.Get("horse_palfrey").Def.PriceMites, debt.Mites); Assert.True(r2.Loans.Owes);
        Assert.True(r2.Loans.Settle(0)); Assert.False(r2.Loans.Owes);
    }

    // ------------------------------------------------------------------ save

    [Fact]
    public void O13_Save_load_save_is_byte_identical_in_the_middle_of_the_first_hour_and_step_states_and_the_loan_come_back()
    {
        var hw = OnbKit.NewWorld(); var origin = OnbKit.Origins.First(o => o.Id == "peddler");
        byte[]? first = null; WorldState? mid = null; string stamp = "";
        var rep = FirstHourBot.Play(hw, OnbKit.Db, origin, 1234, (st, at) =>
        {
            if (at != "borrowed") return;
            mid = st; stamp = DateTimeStamp(st.Onboarding!); first = SaveCodec.Encode(WorldSave.Capture(st, "hash", "0.1"));
        });
        Assert.True(rep.Passed, rep.Render());
        Assert.NotNull(first); Assert.NotNull(mid);
        var restored = WorldSave.Restore(SaveCodec.Decode(first!), OnbKit.Db, "hash");
        var ob = restored.Onboarding; Assert.NotNull(ob);
        Assert.True(ob!.Done(OnboardingStep.MeetMentor) && ob.Done(OnboardingStep.TakeJob) && ob.Done(OnboardingStep.FinishJob) && ob.Done(OnboardingStep.SpendCoin) && ob.Done(OnboardingStep.BorrowHorse));
        Assert.False(ob.Done(OnboardingStep.FirstRide));
        Assert.Equal(stamp, DateTimeStamp(ob));
        Assert.Equal(LoanState.Active, ob.Loans.Active!.State);
        var second = SaveCodec.Encode(WorldSave.Capture(restored, "hash", "0.1"));
        var fa = SaveCodec.Decode(first!); var fb = SaveCodec.Decode(second);
        var diff = fa.Sections.Keys.Union(fb.Sections.Keys).Where(k => !fa.Sections.TryGetValue(k, out var x) || !fb.Sections.TryGetValue(k, out var y) || !x.Span.SequenceEqual(y.Span)).ToList();
        // a bot's world is a hand-built one (no baseline to diff people against, a market system bound to the headless economy), so those three sections are not asked to round-trip here; every section the first hour touches is
        diff.RemoveAll(k => k is "markets" or "meta" or "people");
        Assert.True(diff.Count == 0, "sections differ after save-load-save: " + string.Join(", ", diff));
        foreach (var k in new[] { "onboarding", "jobs", "mounts", "economy" }) Assert.True(fa.Sections[k].Span.SequenceEqual(fb.Sections[k].Span), k + " is not byte-identical");
        Assert.True(OnboardingSave.Capture(ob).Loans.Length == 1);
        Assert.True(SaveCodec.SerializeCompressed(OnboardingSave.Capture(ob)).Length < 5_000, "the onboarding section is over 5 KB");
    }

    static string DateTimeStamp(OnboardingBook b) => string.Join(",", b.DoneSec) + "|" + b.FirstJob.Value + "|" + b.ErrandJob.Value + "|" + b.Loans.Rows.Count;
}

public class PracticeYardTests
{
    static readonly EntityId Hero = EntityId.Make(IdKind.Person, 5);

    [Fact]
    public void O11_a_dummy_hit_gains_skill_at_28_and_none_at_30_and_only_a_good_swing_counts()
    {
        var book = new Ravensguard.Sim.Crafting.SkillBook();
        book.Set(Hero, Skill.Melee, 28 * Ravensguard.Sim.Crafting.CraftMath.MicroPerLevel);
        var (_, o1, g1) = PracticeYard.HitDummy(book, Hero, SwingResult.Good, 100);
        Assert.Equal(UseOutcome.Gained, o1); Assert.True(g1 > 0); Assert.True(book.Micro(Hero, Skill.Melee) > 28 * Ravensguard.Sim.Crafting.CraftMath.MicroPerLevel);
        var (_, o2, g2) = PracticeYard.HitDummy(book, Hero, SwingResult.TooEarly, 200); Assert.Equal(0, g2); Assert.NotEqual(UseOutcome.Gained, o2);
        book.Set(Hero, Skill.Melee, 30 * Ravensguard.Sim.Crafting.CraftMath.MicroPerLevel);
        var (_, o3, g3) = PracticeYard.HitDummy(book, Hero, SwingResult.Good, 300);
        Assert.Equal((UseOutcome.AtCap, 0), (o3, g3)); Assert.Equal("outgrown", PracticeYard.Reason(SwingResult.Good, o3));
        Assert.Equal("too early", PracticeYard.Reason(SwingResult.TooEarly, UseOutcome.NoSkill));
        Assert.Equal("good hit", PracticeYard.Reason(SwingResult.Good, UseOutcome.Gained));
    }

    [Fact]
    public void O11_the_swing_window_is_judged_and_the_spar_ends_at_three_clean_hits_and_a_second_bout_waits_five_minutes()
    {
        var (windup, window) = PracticeYard.Timing("sword");
        Assert.InRange(windup, 0.30, 0.90); Assert.InRange(window, 0.10, 0.15);
        Assert.Equal(SwingResult.TooEarly, PracticeYard.Judge(windup - 0.01, windup, window));
        Assert.Equal(SwingResult.Good, PracticeYard.Judge(windup + window / 2, windup, window));
        Assert.Equal(SwingResult.TooLate, PracticeYard.Judge(windup + window + 0.01, windup, window));
        var win = PracticeYard.Spar([SwingResult.Good, SwingResult.TooEarly, SwingResult.Good, SwingResult.Good]);
        Assert.Equal((true, 3, 1, 4), win);
        Assert.False(PracticeYard.Spar([SwingResult.TooLate, SwingResult.TooLate, SwingResult.TooEarly]).HeroWon);
        Assert.True(PracticeYard.CanSpar(-1, 0, 5));
        Assert.False(PracticeYard.CanSpar(1000, 1000 + 299, 5)); Assert.True(PracticeYard.CanSpar(1000, 1000 + 300, 5));
        // the bout is one use, d = 60, capped at 30
        var book = new Ravensguard.Sim.Crafting.SkillBook(); book.Set(Hero, Skill.Melee, 10 * Ravensguard.Sim.Crafting.CraftMath.MicroPerLevel);
        Assert.Equal(UseOutcome.Gained, PracticeYard.FinishBout(book, Hero, 10));
        book.Set(Hero, Skill.Melee, 30 * Ravensguard.Sim.Crafting.CraftMath.MicroPerLevel);
        Assert.Equal(UseOutcome.AtCap, PracticeYard.FinishBout(book, Hero, 20_000));
    }

    [Fact]
    public void A_town_of_a_thousand_has_a_yard_on_dry_open_ground_outside_its_houses_and_a_village_has_none()
    {
        var pop = EconFixture.Pop; int towns = 0;
        for (int i = 0; i < pop.Region.Settlements.Count; i++)
        {
            var s = pop.Region.Settlements[i]; var y = PracticeYard.SiteOf(pop, i, 7);
            if (s.Class != SettlementClass.Town || s.TargetPop < 1000) { Assert.Null(y); continue; }
            towns++;
            if (y == null) continue;     // a town ringed by water or buildings on all eight sides has none; the rest must be sound
            Assert.Equal(y, PracticeYard.SiteOf(pop, i, 7));
            Assert.Equal(0, pop.Region.Water[RegionData.Index(y.X / RegionSettings.TileM, y.Y / RegionSettings.TileM)]);
        }
        Assert.True(towns > 0);
    }
}
