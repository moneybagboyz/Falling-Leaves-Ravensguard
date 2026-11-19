using Ravensguard.Sim.Core;
using Ravensguard.Sim.Diagnostics;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Goods;
using Ravensguard.Sim.Jobs;
using Ravensguard.Sim.Lighting;
using Ravensguard.Sim.Live;
using Ravensguard.Sim.Save;
using Ravensguard.Sim.Tests.Economy;
using Ravensguard.Sim.Tests.Trade;
using Ravensguard.Sim.Trade;
using Ravensguard.Sim.Travel;
using Ravensguard.Sim.World;

namespace Ravensguard.Sim.Tests.Review;

/// <summary>
/// Phase 3 review finding 6 (game half): the rules the street view used to hold (the away-from-the-job rule, job claim and abandon, the camp job, the current encounter, the wanted speed) live in the
/// sim, are saved, and are recorded, so a save mid-job or mid-encounter continues like an uninterrupted run and a replay reproduces the record hashes.
/// </summary>
public sealed class Phase3GameRulesTests
{
    private static readonly EntityId Hero = EconomyState.HeroOwner;
    private const long Far = 10_000_000;                       // ten kilometres from any work

    private static (EconomyState E, JobEnv Env, JobTable T, Job J) HeroJob()
    {
        var e = EconFixture.NewEconomy(); e.SeedHero(10); var t = new JobTable();
        var env = new JobEnv { Ledger = e.Ledger, Econ = e, Pop = EconFixture.Pop };
        long wage = JobPay.Labour(env.Tuning, env.UnskilledWage, 8);
        var j = t.Post(env, OddJobKind.FarmWork, EconFixture.Pop.Households.IdOf(3), e.HouseholdPurse[3], 0, 1, 0, 3 * 86400, wage, x => { x.P0 = 5000; x.P1 = 5000; x.P4 = 8; x.P5 = 7; x.SrcOwner = EconFixture.Pop.Households.IdOf(3); x.Title = "Field work"; })!;
        Assert.Equal(ClaimResult.Taken, t.Claim(env, new ClaimRequest(j.Id, Hero, -1, 1, 0, 0, 0, 20, 25), 1));
        return (e, env, t, j);
    }

    /// <summary>The game second the rule fires at when the view looks every <paramref name="stride"/> game seconds of the clock, which runs at <paramref name="speed"/>.</summary>
    private static long FireSecond(SimSpeed speed, int stride)
    {
        var (_, _, t, j) = HeroJob();
        var clock = new SimClock(new GameTime(0)) { Speed = speed };
        long last = 0;
        for (int tick = 0; tick < 200_000; tick++)
        {
            clock.Advance(LiveWorld.TickSeconds); long now = clock.Now.Seconds;
            if (now - last < stride) continue;
            last = now;
            if (t.WatchHero(Hero, now, Far, Far) is { } lost) { Assert.Equal(j.Id, lost); return now; }
        }
        return -1;
    }

    [Fact]
    public void The_away_rule_fires_after_five_game_minutes_the_same_at_1x_and_8x()
    {
        long at1 = FireSecond(SimSpeed.X1, 40), at8 = FireSecond(SimSpeed.X8, 40);
        Assert.Equal(at1, at8);
        Assert.InRange(at1, 340, 340 + 40);                    // first seen away at second 40; lost once 300 game seconds have passed
        // a hero who comes back inside 60 m (or is looked at less than five minutes) keeps it
        var (_, _, t, j) = HeroJob();
        Assert.Null(t.WatchHero(Hero, 40, Far, Far)); Assert.Equal(40, t.HeroAwaySince);
        Assert.Null(t.WatchHero(Hero, 300, Far, Far));
        var (wx, wy, _, _) = JobTable.PositionAt(j, 340);
        Assert.Null(t.WatchHero(Hero, 340, wx + 59_000, wy)); Assert.Equal(-1, t.HeroAwaySince);
        Assert.Null(t.WatchHero(Hero, 400, Far, Far)); Assert.Null(t.WatchHero(Hero, 699, Far, Far)); Assert.Equal(j.Id, t.WatchHero(Hero, 700, Far, Far));
        // the mentor's first contract is forgiving
        var (_, _, t2, j2) = HeroJob();
        Assert.Null(t2.WatchHero(Hero, 40, Far, Far, j2.Id)); Assert.Null(t2.WatchHero(Hero, 4000, Far, Far, j2.Id)); Assert.Equal(-1, t2.HeroAwaySince);
    }

    [Fact]
    public void A_save_taken_while_the_hero_is_away_loses_the_job_at_the_same_second_as_an_uninterrupted_run()
    {
        var (_, _, t, j) = HeroJob();
        Assert.Null(t.WatchHero(Hero, 80, Far, Far)); Assert.Null(t.WatchHero(Hero, 120, Far, Far));
        var saved = SaveCodec.Serialize(JobsSave.Capture(t));
        var back = SaveCodec.Deserialize<JobsSave>(saved).Restore();
        Assert.Equal(Convert.ToHexString(saved), Convert.ToHexString(SaveCodec.Serialize(JobsSave.Capture(back))));
        Assert.Equal(80, back.HeroAwaySince);
        long Run(JobTable x) { for (long s = 160; s < 2000; s += 40) if (x.WatchHero(Hero, s, Far, Far) != null) return s; return -1; }
        long a = Run(t), b = Run(back);
        Assert.True(a == b && a == 400, $"{a} {b} {t.HeroAwaySince} {back.HeroAwaySince}");              // 80 + 300 is 380: the first look after it is at 400
        Assert.False(back.IsEmpty);
    }

    [Fact]
    public void Job_claim_and_abandon_are_hero_desk_commands_applied_at_the_barrier()
    {
        var w = new TradeWorld(); var (e, env, table, _) = HeroJob();
        var j2 = table.Post(env, OddJobKind.FarmWork, EconFixture.Pop.Households.IdOf(4), e.HouseholdPurse[4], 0, 1, w.Sec - 600, w.Sec + 3 * 86400, 140, x => { x.P0 = 6000; x.P1 = 6000; x.P4 = 8; x.P5 = 7; x.SrcOwner = EconFixture.Pop.Households.IdOf(4); x.Title = "More work"; })!;
        table.Abandon(env, table.JobOf(Hero)!.Id, 10);       // (its own time is long past: it is given up before the clock moves on)         // the first job is given up so the hero is free
        var ctx = new HeroContext(w.State, w.State.Party!, EconFixture.Content) { Jobs = () => (env, table) };
        var desk = new HeroDesk();
        long sec = w.Sec;
        var claim = desk.Issue(new HeroCommand { Kind = HeroCommandKind.JobClaim, IssuedSec = sec, Job = j2.Id, X = 6000, Y = 6000, Origin = CommandOrigin.Human });
        Assert.False(table.HasJob(Hero));                      // nothing happens until the barrier
        desk.ApplyDue(ctx, claim.Hour);
        Assert.True(table.HasJob(Hero)); Assert.Equal(j2.Id, table.JobOf(Hero)!.Id);
        Assert.True(desk.View!.Results.Last(r => r.Seq == claim.Seq).Ok);
        var nope = desk.Issue(new HeroCommand { Kind = HeroCommandKind.JobAbandon, IssuedSec = sec + 5, Job = EntityId.Make(IdKind.Other, 99999) });
        desk.ApplyDue(ctx, nope.Hour); Assert.Equal(HeroResultCode.Invalid, desk.View!.Results.Last(r => r.Seq == nope.Seq).Code); Assert.True(table.HasJob(Hero));
        var drop = desk.Issue(new HeroCommand { Kind = HeroCommandKind.JobAbandon, IssuedSec = sec + 60, Job = j2.Id, Tag = "away" });
        desk.ApplyDue(ctx, drop.Hour);
        Assert.True(desk.View!.Results.Last(r => r.Seq == drop.Seq).Ok); Assert.False(table.HasJob(Hero)); Assert.Equal(JobState.Failed, table.Get(j2.Id)!.State);
        Assert.True(e.Ledger.Audit().IsConserved);
        // waiting commands survive a save (hero.pending) like every other
        var pend = HeroPendingSave.Capture([claim with { Seq = 9 }, drop with { Seq = 10 }]).Restore();
        Assert.Equal([HeroCommandKind.JobClaim, HeroCommandKind.JobAbandon], pend.Select(c => c.Kind).ToArray()); Assert.Equal("away", pend[1].Tag); Assert.Equal(j2.Id, pend[0].Job);
    }

    private static TravelState NewState()
    {
        var goods = EconFixture.Catalog();
        return new TravelState(EconFixture.Content, 11, goods, new MassLedger(goods.Count)) { HeroId = EntityId.Make(IdKind.Person, 1) };
    }

    [Fact]
    public void A_save_mid_camp_job_and_mid_encounter_continues_like_an_uninterrupted_run()
    {
        var t = NewState();
        Assert.True(t.IsEmpty);
        t.StartCampJob(CampJobKind.Talk, 1200, 5000);
        t.Current = new EncounterEvent(5100, "toll_bridge", "Demand", 100, 200, 300, 400, 3, [7, 8], "Halt!"); t.CurrentOpen = true; t.ScreenPending = true;
        Assert.False(t.IsEmpty);
        var a = SaveCodec.Serialize(TravelSave.Capture(t));
        var u = NewState(); SaveCodec.Deserialize<TravelSave>(a).Restore(u);
        Assert.Equal(Convert.ToHexString(a), Convert.ToHexString(SaveCodec.Serialize(TravelSave.Capture(u))));
        Assert.Equal(t.CampJob, u.CampJob); Assert.Equal(t.Current!.Id, u.Current!.Id); Assert.Equal([7UL, 8UL], u.Current.Cast); Assert.True(u.CurrentOpen); Assert.True(u.ScreenPending);
        // both go on: the job comes due at the same game second, and only once
        foreach (var x in new[] { t, u })
        {
            Assert.Null(x.TakeDueCampJob(6199)); Assert.Equal(new CampJobState(CampJobKind.Talk, 6200), x.TakeDueCampJob(6200)); Assert.Null(x.TakeDueCampJob(9000));
            x.CurrentOpen = false; x.ScreenPending = false; x.ClearCurrent();
        }
        Assert.Equal(Convert.ToHexString(SaveCodec.Serialize(TravelSave.Capture(t))), Convert.ToHexString(SaveCodec.Serialize(TravelSave.Capture(u))));
    }

    private static readonly GameTime Noon = GameTime.FromCalendar(1, Season.Spring, 8, 12);

    private static LiveWorld Field()
    {
        var g = new LevelGrid(60, 30, 0, 1);
        g.FillRect(0, 0, 59, 29, 0, true);
        return new LiveWorld(g, new LightGrid(g), new Cell3(5, 5, 0), Noon);
    }

    [Fact]
    public void The_wanted_speed_is_hero_state_that_a_resume_keeps()
    {
        var w = Field(); Assert.Equal(SimSpeed.X8, w.WantedSpeed);
        w.SetWantedSpeed(SimSpeed.X2);
        Assert.Equal(SimSpeed.X2, w.CaptureResume().Wanted);
        Assert.NotEqual(LiveStateHash.Compute(w), LiveStateHash.Compute(Field()));
    }

    /// <summary>A job claimed, an encounter choice, a camp with its job at 8x, an abandon: every one recorded, with the speed asked for.</summary>
    private static SessionRecorder RecordSession()
    {
        var rec = new SessionRecorder(Field());
        var job = EntityId.Make(IdKind.Other, 4242);
        rec.Hero(new HeroCommand { Seq = 1, IssuedSec = rec.World.Clock.Now.Seconds, Kind = HeroCommandKind.JobClaim, Job = job, X = 6000, Y = 6000, Origin = CommandOrigin.Human });
        for (int i = 0; i < 100; i++) rec.Step();
        rec.SetWantedSpeed(SimSpeed.X1);
        rec.Action(HeroAction.EncounterChoice, 2, rec.World.Clock.Now.Seconds);
        rec.Hero(new HeroCommand { Seq = 2, IssuedSec = rec.World.Clock.Now.Seconds, Kind = HeroCommandKind.EncounterPay, CoinGive = 120, Tag = "toll_bridge", Origin = CommandOrigin.Human });
        rec.Action(HeroAction.CampMake, 9_000, 9_000);
        rec.Action(HeroAction.CampJob, 1200, rec.World.Clock.Now.Seconds); rec.SetSpeed(SimSpeed.X8);
        for (int i = 0; i < 160; i++) rec.Step();
        rec.SetWantedSpeed(SimSpeed.X4); rec.SetSpeed(SimSpeed.X1);
        rec.Action(HeroAction.CampBreak);
        rec.Hero(new HeroCommand { Seq = 3, IssuedSec = rec.World.Clock.Now.Seconds, Kind = HeroCommandKind.JobAbandon, Job = job, Tag = "away", Origin = CommandOrigin.Human });
        rec.SetInput(new MoveInput(1, 0));
        for (int i = 0; i < 120; i++) rec.Step();
        return rec;
    }

    [Fact]
    public void A_session_with_a_job_claim_an_abandon_an_encounter_choice_and_a_camp_replays_to_identical_record_hashes()
    {
        var rec = RecordSession();
        var (trace, csv) = rec.Export();
        var log = CommandLog.FromBytes(trace);
        Assert.Equal([HeroCommandKind.JobClaim, HeroCommandKind.EncounterPay, HeroCommandKind.JobAbandon], log.Items.Where(c => c.Kind == ReplayKind.Hero).Select(c => (HeroCommandKind)(int)(c.A & 0xFF)).ToArray());
        Assert.Equal(3, log.Items.Count(c => c.Kind == ReplayKind.WantedSpeed));            // the window's own, then the two asked for
        var seen = new List<ReplayCommand>();
        var fresh = Field();
        var res = ReplayRunner.Run(fresh, log, StateHashTrail.FromCsv(csv), new ReplayOptions { OnRecordCommand = seen.Add });
        Assert.True(res.Matched, res.Describe()); Assert.True(res.Completed);
        Assert.Equal(StateHashTrail.FromCsv(csv).Items, res.Replayed.Items);
        Assert.Equal(SimSpeed.X4, fresh.WantedSpeed);
        Assert.Equal(LiveStateHash.Compute(rec.World), LiveStateHash.Compute(fresh));
        Assert.Equal(log.Items.Where(c => c.IsRecordCommand).ToArray(), seen.ToArray());
        // without the wanted-speed record the replay diverges (the speed asked for is part of the state)
        var old = new CommandLog(); old.Rebase(log.BaseTick, []);
        foreach (var c in log.Items) if (c.Kind != ReplayKind.WantedSpeed) old.Append(c);
        old.NoteTick(log.EndTick);
        Assert.NotNull(ReplayRunner.Run(Field(), old, StateHashTrail.FromCsv(csv)).Divergence);
    }
}
