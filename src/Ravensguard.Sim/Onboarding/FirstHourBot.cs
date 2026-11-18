using System.Text;
using Ravensguard.Content;
using Ravensguard.Sim.Abstract;
using Ravensguard.Sim.Animals;
using Ravensguard.Sim.Behaviour;
using Ravensguard.Sim.Characters;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Dialogue;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Jobs;
using Ravensguard.Sim.Save;
using Ravensguard.Sim.Trade;
using Ravensguard.Sim.WorldGen.Chunks;

namespace Ravensguard.Sim.Onboarding;

/// <summary>What the bot did, second by second of game time.</summary>
public sealed record BotEvent(long Sec, string Text);

/// <summary>The result of one scripted first hour (onboarding test O-4).</summary>
public sealed class BotReport
{
    public string Origin = "", Failure = "", StartKind = "", MentorId = "";
    public ulong Seed;
    public bool Passed => Failure.Length == 0;
    public List<BotEvent> Timeline { get; } = [];
    public long[] StepSec = [];
    public long StartSec;
    public long FirstJobGameSec, FirstJobGameMinutesAt1x;
    public long RideMetres, MountedMetres;
    public string LoanState = "";
    public int Steps;

    /// <summary>Real minutes at 1x of the span between two game seconds (20 game seconds per real second).</summary>
    public static double RealMin(long gameSec) => gameSec / (double)TimeConst.GameSecondsPerRealSecond / 60.0;

    public string Render()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"[autoplay] origin {Origin} seed {Seed} mentor {MentorId} start {StartKind}: {(Passed ? "PASS" : "FAIL " + Failure)}  steps {Steps}/8");
        foreach (var e in Timeline) sb.AppendLine($"  +{RealMin(e.Sec - StartSec):0.0} min  {e.Text}");
        return sb.ToString();
    }
}

/// <summary>
/// The <c>--autoplay</c> bot (onboarding spec 7, O-4): drives a new game headlessly through the first hour for one origin, using only the commands a player has: it talks to the mentor by choosing lines of the authored dialogue
/// (and the effects go where the talk window sends them), takes the job through a claim like the work panel, lets the job run, buys something through the hero's desk, borrows the animal in a second talk, mounts and rides to the
/// errand place by the game's own ride rules (speed, stamina, gait), delivers, rides back and hands the animal over, then rests the night. It never writes to the script's state and never uses a dev command. The timeline is the
/// report; a failed assertion is the first sentence of <see cref="BotReport.Failure"/>.
/// </summary>
public static class FirstHourBot
{
    /// <summary>A text source that echoes the key, so the bot can choose a line by its key.</summary>
    private sealed class KeyText : ITextSource { public string T(string key) => key; public bool Has(string key) => true; }

    private sealed class Run
    {
        public required HeadlessWorld Hw; public required ContentDatabase Db; public required WorldState State; public required HeroContext Ctx; public required HeroDesk Desk;
        public required JobTable Table; public required JobEnv Env; public required OnboardingBook Book; public required RideBook Rides; public required BotReport Rep; public required OnboardingContent Cfg;
        public long Now; public long X, Y;
        public readonly HashSet<string> Flags = new(StringComparer.Ordinal);
        public EntityId HeroId => Ctx.Party.Hero;
        public void Log(string t) => Rep.Timeline.Add(new BotEvent(Now, t));
        public void Fail(string t) { if (Rep.Failure.Length == 0) Rep.Failure = t; Log("FAIL " + t); }
    }

    public static BotReport Play(HeadlessWorld hw, ContentDatabase db, OriginDef origin, ulong seed, Action<WorldState, string>? observer = null)
    {
        var rep = new BotReport { Origin = origin.Id, Seed = seed, MentorId = origin.Mentor };
        try { Script(hw, db, origin, seed, rep, observer); }
        catch (Exception e) { if (rep.Failure.Length == 0) rep.Failure = "exception: " + e.GetType().Name + ": " + e.Message; rep.Timeline.Add(new BotEvent(0, "EXCEPTION " + e)); }
        return rep;
    }

    private static void Script(HeadlessWorld hw, ContentDatabase db, OriginDef origin, ulong seed, BotReport rep, Action<WorldState, string>? obs)
    {
        var econ = hw.World.Economy; var hero = HeroRecord.FromOrigin("Bot", false, origin);
        econ.SeedHero(hero.Coin);
        long start = GameTime.FromCalendar(hw.Population.NowYear, Season.Spring, 1, 8).Seconds;
        var st = new WorldState
        {
            ContinentSettings = hw.ContinentSettings, HistorySettings = hw.HistorySettings, RegionSettings = hw.RegionSettings, EmbarkCellX = hw.CellX, EmbarkCellY = hw.CellY,
            Continent = hw.Continent, History = hw.History, Population = hw.Population, Clock = new SimClock(new GameTime(start)), Ids = new IdAllocator(), Chunks = new ChunkStore(), Chronicle = new Chronicle(),
            Economy = econ, Hero = hero, Markets = hw.Economy.Markets, Content = db,
        };
        st.Party = HeroParty.Create(econ, db, st.Ids, hero);
        var table = new JobTable(); st.Jobs = table;
        var env = new JobEnv { Ledger = econ.Ledger, Econ = econ, Pop = hw.Population, Tuning = BehaviourContent.From(db).OddJobs };
        var ctx = new HeroContext(st, st.Party, db) { Jobs = () => (env, table) };
        var cfg = OnboardingContent.From(db);
        var setup = OnboardingSetup.Create(st, db, origin, start, seed);
        rep.StartSec = start;
        if (setup.Book == null) { rep.Failure = "no start: " + setup.Message; return; }
        var book = setup.Book; st.Onboarding = book; rep.StartKind = setup.Site.Kind + (setup.Site.Relaxed ? "*" : "");
        var rides = st.Mounts = new RideBook(db, econ, seed);
        var r = new Run { Hw = hw, Db = db, State = st, Ctx = ctx, Desk = new HeroDesk(), Table = table, Env = env, Book = book, Rides = rides, Rep = rep, Cfg = cfg, Now = start, X = book.StartX, Y = book.StartY };
        rides.PlaceHero(r.X, r.Y); rides.RidingSkill = Math.Max(10, hero.Skill("riding"));
        var sleep = new SleepTracker();
        r.Log($"new game: {origin.Name}, start {setup.Site.Kind} at settlement {book.MentorSettlement}, mentor {origin.Mentor}; job {(book.HasFirstJob ? "posted" : "NOT posted")}");
        if (!book.HasFirstJob) { r.Fail("the mentor's first job was not posted"); return; }

        // 1. meet the mentor, ask for work, accept
        Talk(r, origin.Mentor, ["ASK_WORK", "ACCEPT_Q"]);
        if (!book.Done(OnboardingStep.MeetMentor)) { r.Fail("step 1 (meet_mentor) not done after the talk"); return; }
        if (!book.JobOffered) { r.Fail("the mentor's talk did not offer the job"); return; }

        // 2. take the job (a claim, like the work panel's Take)
        var job = table.Get(book.FirstJob)!;
        if (job.Slots != 1 || job.DeadlineSec - job.PostedSec != cfg.Settings.JobDeadlineHours * 3600L) { r.Fail("first job is not Slots 1 with the 12 hour deadline"); return; }
        long purseBefore = econ.Ledger.Balance(ctx.Purse).Mites;
        var posterPurse = new PurseId(job.PosterPurse); long posterBefore = econ.Ledger.Balance(posterPurse).Mites; obs?.Invoke(st, "met");
        var claim = table.Claim(env, new ClaimRequest(job.Id, r.HeroId, -1, 0, 0, r.X, r.Y, 20, 25) { Origin = CommandOrigin.Human }, r.Now);
        if (claim != ClaimResult.Taken) { r.Fail("claim refused: " + claim); return; }
        book.OnJobTaken(job.Id, r.Now); r.Log($"took the job '{job.Title}' ({job.Kind}) for {job.WageMites} mites");
        long taken = r.Now;

        // 3. do the job: game time passes, the job runs its legs and pays
        for (int guard = 0; guard < 4000 && !job.Closed; guard++) { r.Now += 30; table.Advance(env, r.Now); }
        if (job.State != JobState.Done) { r.Fail("job ended " + job.State); return; }
        rep.FirstJobGameSec = r.Now - taken; rep.FirstJobGameMinutesAt1x = (long)BotReport.RealMin(r.Now - taken);
        long gained = econ.Ledger.Balance(ctx.Purse).Mites - purseBefore;
        if (gained != job.PaidMites || job.PaidMites < cfg.Settings.MinWageMites) { r.Fail($"pay: purse +{gained}, job paid {job.PaidMites}, floor {cfg.Settings.MinWageMites}"); return; }
        if (econ.Ledger.Balance(new PurseId(job.Escrow)).Mites != 0 || econ.Ledger.Balance(posterPurse).Mites != posterBefore) { r.Fail("first job pay: the escrow is not empty or the poster's purse moved after posting"); return; }
        book.OnJobDone(job.Id, job.PaidMites, r.Now, cfg.Settings.MinWageMites); r.Log($"job done: +{job.PaidMites} mites in the purse (poster's escrow {job.WageMites} -> 0)"); obs?.Invoke(st, "job_done");
        if (rep.FirstJobGameSec > cfg.Settings.JobDeadlineHours * 3600L) { r.Fail("first job took longer than its 12 game hour deadline"); return; }

        // 4. spend coin at the market through the hero's desk
        if (!Spend(r)) { r.Fail($"could not buy anything at the market (coin {r.Ctx.Coin} mites, cheapest unit {Cheapest(r)})"); return; }

        // 5. a second talk (the dialogue starts at its repeat node) borrows the animal
        if (origin.Mentor == "caravan_master") Talk(r, origin.Mentor, ["ASK_LOAN"]); else Talk(r, origin.Mentor, ["REPEAT_HORSE", "HORSE_ACCEPT"]);
        if (!book.Done(OnboardingStep.BorrowHorse) || book.Loans.Active == null) { r.Fail("no loan after asking for the animal"); return; }
        if (!book.HasErrandJob) { r.Fail("no errand job posted with the loan"); return; }
        obs?.Invoke(st, "borrowed");
        var loan = book.Loans.Active!; r.Log($"loan: animal {loan.Animal.Value % 100000} due in {cfg.Settings.LoanDays} days, return to settlement {loan.ReturnPlace}");
        if (loan.DueSec - loan.FromSec != cfg.Settings.LoanDays * 86_400L) { r.Fail("loan is not 3 game days"); return; }

        // 6. mount and ride to the errand place, claim and hand over the errand
        var animal = rides.Animals.Find(loan.Animal)!;
        animal.XMm = r.X; animal.YMm = r.Y;
        if (!Mount(r)) { r.Fail("could not mount the lent animal"); return; }
        if (!RideTo(r, book.ErrandX, book.ErrandY, "the errand place", true)) return;
        var errand = table.Get(book.ErrandJob)!;
        if (table.Claim(env, new ClaimRequest(errand.Id, r.HeroId, -1, 1, 0, r.X, r.Y, 20, 25) { Origin = CommandOrigin.Human }, r.Now) != ClaimResult.Taken) { r.Fail("errand claim refused"); return; }
        book.OnJobTaken(errand.Id, r.Now); long errBefore = econ.Ledger.Balance(ctx.Purse).Mites;
        for (int guard = 0; guard < 4000 && !errand.Closed; guard++) { r.Now += 5; table.Advance(env, r.Now); }
        if (errand.State != JobState.Done) { r.Fail("errand job ended " + errand.State); return; }
        if (econ.Ledger.Balance(ctx.Purse).Mites - errBefore != errand.PaidMites) { r.Fail("errand pay not in the purse"); return; }
        book.OnJobDone(errand.Id, errand.PaidMites, r.Now, cfg.Settings.MinWageMites); r.Log($"errand delivered: +{errand.PaidMites} mites");

        // 7. ride back and hand the animal over at its place
        if (!RideTo(r, book.MentorX, book.MentorY, "the mentor's place", false)) return;
        DismountNow(r);
        var ret = OnboardingSetup.ReturnAnimal(st, db, book, book.MentorSettlement, r.Now);
        if (ret == null || ret.Value.Loan.State != LoanState.Returned) { r.Fail("the animal could not be handed back at its place"); return; }
        r.Log($"animal returned: mentor trust {ret.Value.TrustPm:+#;-#;0}");

        // 8. the first night: rest under a roof from evening
        long dusk = (r.Now / 86400) * 86400 + 20 * 3600; if (dusk < r.Now) dusk += 86400;
        r.Now = dusk;
        for (int h = 0; h < 12 && !book.Done(OnboardingStep.FirstNight); h++)
        {
            r.Now += 3600; rides.Hour(r.Now / 3600);
            int rested = sleep.Feed(r.Now, r.X, r.Y, true);
            if (rested > 0) book.OnSlept(rested, r.Now, cfg.Settings.FirstNightHours);
            OnboardingSetup.Hour(st, db, book, r.Now);
        }
        if (book.Done(OnboardingStep.FirstNight)) r.Log("slept the night");

        rep.StepSec = [.. book.DoneSec]; rep.Steps = book.DoneCount; rep.RideMetres = book.RideMm / 1000; rep.MountedMetres = book.MountedMm / 1000; rep.LoanState = loan.State.ToString();
        for (int i = 0; i < OnboardingBook.StepCount; i++) if (!book.Done((OnboardingStep)i)) { r.Fail("step " + (i + 1) + " (" + OnboardingBook.StepIds[i] + ") not done"); return; }
        if (!book.Complete) r.Fail("book not marked complete");
        if (!econ.Ledger.Audit().IsConserved) r.Fail("the coin audit is not conserved");
    }

    private static void Talk(Run r, string mentorId, string[] picks)
    {
        var book = LineBook.From(r.Db);
        var def = book.Dialogues.First(d => d.Speaker == mentorId);
        var ctx = new TalkContext { Job = "none", HeroName = "Bot" };
        foreach (var f in r.Flags) ctx.Flags.Add(f);
        var run = new DialogueRunner(def, ctx, new KeyText());
        string prefix = "D_";
        int safety = 0;
        void Flush() { foreach (var e in run.Effects.ToList()) { if (OnboardingSetup.Apply(r.State, r.Db, r.Book, e.Id, e.Args, r.Now, r.X, r.Y)) r.Log($"talk effect {e.Id} {string.Join(",", e.Args.Values)}"); } run.Effects.Clear(); }
        Flush();
        int p = 0;
        while (!run.Ended && safety++ < 40)
        {
            if (run.NeedsAdvance) { run.Advance(); Flush(); continue; }
            if (p >= picks.Length) break;
            var c = run.Choices.FirstOrDefault(x => !x.Locked && x.Text.EndsWith("_" + picks[p], StringComparison.Ordinal) && x.Text.StartsWith(prefix, StringComparison.Ordinal));
            if (c == null) { r.Fail($"talk: no choice '{picks[p]}' at node {run.NodeId} (have {string.Join(" ", run.Choices.Select(x => x.Text))})"); return; }
            run.Choose(c.Index); Flush(); p++;
            r.Now += 20;
        }
        foreach (var f in ctx.Flags) r.Flags.Add(f);
        // a talk that ended by a goto: walk the rest by advancing
        while (!run.Ended && run.NeedsAdvance && safety++ < 60) { run.Advance(); Flush(); }
        r.Log($"talked to the mentor ({string.Join(" > ", picks)})");
    }

    private static bool Spend(Run r)
    {
        var s = r.Book.MentorSettlement; var b = r.Ctx.Book(s); if (b == null) return false;
        r.Now = Math.Max(r.Now, (r.Now / 86400) * 86400 + 10 * 3600);   // the stalls are open from 06:00 to 18:00
        long coin = r.Ctx.Coin;
        for (ushort g = 0; g < b.Goods.Length; g++)
        {
            if (b.Goods[g].StockUnits < 1) continue;
            b.Sync(g);
            foreach (long qty in new long[] { 1, 500, 2000, 10_000, 100_000 })        // a piece, or a few hundred grams: whatever the unit of the good makes a small purchase
            {
                if (b.Goods[g].StockUnits < qty) break;
                var q = Quotes.Buy(b, g, qty);
                if (q.TotalMites <= 0 || q.TotalMites > coin / 2) continue;
                var cmd = r.Desk.Issue(new HeroCommand { Kind = HeroCommandKind.Buy, Settlement = s, Good = g, Qty = qty, Limit = q.TotalMites + 5, IssuedSec = r.Now, Origin = CommandOrigin.Human });
                r.Desk.ApplyDue(r.Ctx, cmd.Hour);
                var res = r.Desk.View?.Results.LastOrDefault(x => x.Seq == cmd.Seq);
                if (res is { Ok: true }) { r.Book.OnTraded(r.Now); r.Log("bought: " + res.Text); return true; }
                break;
            }
        }
        return false;
    }

    private static string Cheapest(Run r)
    {
        var b = r.Ctx.Book(r.Book.MentorSettlement); if (b == null) return "no market";
        long best = long.MaxValue; int n = 0;
        for (ushort g = 0; g < b.Goods.Length; g++) { if (b.Goods[g].StockUnits < 1) continue; n++; b.Sync(g); best = Math.Min(best, Quotes.Buy(b, g, 1).TotalMites); }
        return $"{best} of {n} goods in stock";
    }

    private static bool Mount(Run r)
    {
        var rides = r.Rides; rides.PlaceHero(r.X, r.Y);
        var res = rides.Interact(r.Now);
        if (res != MountResult.Ok) { r.Log("mount refused: " + res); return false; }
        for (int i = 0; i < MountRules.MountSeconds + 2 && !rides.IsMounted; i++) { r.Now++; rides.TickBusy(); }
        if (rides.IsMounted) r.Log("mounted");
        return rides.IsMounted;
    }

    private static void DismountNow(Run r) { if (r.Rides.IsMounted) { r.Rides.Dismount(); r.Log("dismounted"); } }

    /// <summary>Rides to a point, a game second at a time, by the ride book's own plan (gait, speed, stamina). Fails the run when it is too slow or the animal refuses.</summary>
    private static bool RideTo(Run r, long tx, long ty, string where, bool outward)
    {
        var rides = r.Rides; var book = r.Book; long t0 = r.Now;
        for (int guard = 0; guard < 200_000; guard++)
        {
            long dx = tx - r.X, dy = ty - r.Y; long dist = (long)Math.Sqrt((double)dx * dx + (double)dy * dy);
            if (dist <= 30_000) { r.Log($"arrived at {where} after {(r.Now - t0) / 60} game min"); book.OnRide(0, rides.IsMounted, outward, r.Now, r.Cfg.Settings.MinRideM, r.Cfg.Settings.MountedSharePm); return true; }
            var step = rides.Plan(false, false, r.Now);
            long mv = Math.Min(step.SpeedMmPerGs, dist);
            if (step.Blocked || mv <= 0) { r.Fail($"the animal cannot move ({(step.Blocked ? "blocked" : "no speed")}) on the way to {where}"); return false; }
            r.X += dx * mv / Math.Max(1, dist); r.Y += dy * mv / Math.Max(1, dist);
            rides.PlaceHero(r.X, r.Y); rides.Account(step, mv, r.Now, false);
            book.OnRide(mv, rides.IsMounted, false, r.Now, r.Cfg.Settings.MinRideM, r.Cfg.Settings.MountedSharePm);
            r.Now++;
            if (r.Now % 3600 == 0) { rides.Hour(r.Now / 3600); rides.ExpireLoans(r.Now); }
        }
        r.Fail("the ride to " + where + " did not end");
        return false;
    }

    public static string Summary(IEnumerable<BotReport> reports)
    {
        var l = reports.ToList();
        return $"[autoplay] {l.Count(x => x.Passed)}/{l.Count} first hours passed";
    }
}
