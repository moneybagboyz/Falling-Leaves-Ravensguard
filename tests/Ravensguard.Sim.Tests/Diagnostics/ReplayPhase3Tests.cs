using Ravensguard.Sim.Core;
using Ravensguard.Sim.Diagnostics;
using Ravensguard.Sim.Lighting;
using Ravensguard.Sim.Live;
using Ravensguard.Sim.Trade;
using Ravensguard.Sim.World;

namespace Ravensguard.Sim.Tests.Diagnostics;

/// <summary>
/// Phase 3 cleanup: the hero commands added in Phase 3 go through the session recorder too (auto-travel's placement on the route, the carried load's speed, the desk's trade and craft
/// commands, camp and autopilot actions, talk choices), so a replay of a session that uses them reproduces every record hash.
/// </summary>
public sealed class ReplayPhase3Tests
{
    private static readonly GameTime Noon = GameTime.FromCalendar(1, Season.Spring, 8, 12);

    private static LiveWorld Field()
    {
        var g = new LevelGrid(60, 30, 0, 1);
        g.FillRect(0, 0, 59, 29, 0, true);
        for (int y = 0; y < 30; y++) if (y != 15) g.SetOpen(30, y, 0, false);
        return new LiveWorld(g, new LightGrid(g), new Cell3(5, 5, 0), Noon);
    }

    /// <summary>A trip on the route, a trade and a craft at the desk (each changes the load), a camp at 8x, then a walk home under the new load.</summary>
    private static SessionRecorder RecordTripTradeCraftCamp()
    {
        var rec = new SessionRecorder(Field());
        rec.SetSpeed(SimSpeed.X8);
        rec.Action(HeroAction.TravelBegin, 22_000, rec.World.Clock.Now.Seconds);
        // the journey places the hero on the route every tick, a little under a metre apart (the route is the travel state's, outside the live sim)
        for (int i = 0; i < 260; i++) { rec.TravelPlace(5_500 + i * 85, 5_500 + (i * 37) % 9_000, i % 50 < 25 ? Facing.East : Facing.South); rec.Step(); }
        rec.SetSpeed(SimSpeed.X1);
        // a trade: the desk buys a sack of flour; the load slows the hero
        rec.Hero(new HeroCommand { Seq = 1, IssuedSec = rec.World.Clock.Now.Seconds, Kind = HeroCommandKind.Buy, Settlement = 0, Good = 3, Qty = 25, Limit = 900 });
        rec.SetLoadSpeed(780);
        rec.SetInput(new MoveInput(1, 0));
        for (int i = 0; i < 150; i++) rec.Step();
        // a craft: started at the desk; the goods it uses lighten the pack a little
        rec.Hero(new HeroCommand { Seq = 2, IssuedSec = rec.World.Clock.Now.Seconds, Kind = HeroCommandKind.CraftStart, Recipe = 7, Batch = 2, Site = new CraftSiteSpec(800, true, ["knife"], [], []) });
        rec.SetLoadSpeed(820);
        rec.SetInput(new MoveInput(0, 1, Walk: true));
        for (int i = 0; i < 120; i++) rec.Step();
        // a camp: pitched, a job at 8x, broken
        rec.SetInput(MoveInput.None);
        rec.Action(HeroAction.CampMake, 9_000, 9_000);
        rec.Action(HeroAction.CampJob, 1200, rec.World.Clock.Now.Seconds); rec.SetSpeed(SimSpeed.X8);
        for (int i = 0; i < 200; i++) rec.Step();
        rec.Action(HeroAction.CampBreak); rec.SetSpeed(SimSpeed.X1);
        // home: a click-to-move route and a walk with the load
        rec.CommandMoveTo(new Cell3(20, 25, 0));
        for (int i = 0; i < 450; i++) rec.Step();
        return rec;
    }

    [Fact]
    public void A_session_with_a_trip_a_trade_a_craft_and_a_camp_replays_to_identical_record_hashes()
    {
        var rec = RecordTripTradeCraftCamp();
        var (trace, csv) = rec.Export();
        var log = CommandLog.FromBytes(trace);
        Assert.Contains(log.Items, c => c.Kind == ReplayKind.TravelPlace);
        Assert.Contains(log.Items, c => c.Kind == ReplayKind.LoadSpeed);
        Assert.Equal(2, log.Items.Count(c => c.Kind == ReplayKind.Hero));
        Assert.True(trace.Length < 8 * 1024, $"trace {trace.Length} bytes: the route is delta-coded");

        var seen = new List<ReplayCommand>();
        var fresh = Field();
        var res = ReplayRunner.Run(fresh, log, StateHashTrail.FromCsv(csv), new ReplayOptions { OnRecordCommand = seen.Add });
        Assert.True(res.Matched, res.Describe());
        Assert.True(res.Completed);
        Assert.Equal(StateHashTrail.FromCsv(csv).Items, res.Replayed.Items);
        Assert.True(res.Replayed.Count >= 3);
        Assert.Equal(LiveStateHash.Compute(rec.World), LiveStateHash.Compute(fresh));
        // the record-side commands reach the host in order, at the ticks they were issued
        Assert.Equal([ReplayKind.Action, ReplayKind.Hero, ReplayKind.Hero, ReplayKind.Action, ReplayKind.Action, ReplayKind.Action], seen.Select(c => c.Kind).ToArray());
        Assert.Equal((long)HeroCommandKind.Buy, seen[1].A & 0xFF); Assert.Equal(1, seen[1].A >> 8);
        Assert.Equal((long)HeroCommandKind.CraftStart, seen[2].A & 0xFF);
        Assert.Equal(SessionRecorder.DigestOf(new HeroCommand { Seq = 2, IssuedSec = seen[2].B, Kind = HeroCommandKind.CraftStart, Recipe = 7, Batch = 2, Site = new CraftSiteSpec(800, true, ["knife"], [], []) }), seen[2].C);
    }

    [Fact]
    public void Without_the_trip_and_the_load_the_replay_diverges()
    {
        var rec = RecordTripTradeCraftCamp();
        var (trace, csv) = rec.Export();
        var log = CommandLog.FromBytes(trace);
        // the same trace with the new kinds dropped (what the recorder wrote before): the hashes no longer match
        var old = new CommandLog();
        old.Rebase(log.BaseTick, []);
        foreach (var c in log.Items) if (c.Kind is not (ReplayKind.TravelPlace or ReplayKind.LoadSpeed)) old.Append(c);
        old.NoteTick(log.EndTick);
        var res = ReplayRunner.Run(Field(), old, StateHashTrail.FromCsv(csv));
        Assert.NotNull(res.Divergence);
    }

    [Fact]
    public void The_trace_round_trips_and_reads_a_version_1_trace()
    {
        var rec = RecordTripTradeCraftCamp();
        var log = rec.Commands;
        var back = CommandLog.FromBytes(log.ToBytes());
        Assert.Equal(log.Items, back.Items);
        Assert.Equal(log.BaseTick, back.BaseTick); Assert.Equal(log.EndTick, back.EndTick);
        // a version 1 trace (no travel kinds, absolute coordinates) still reads
        var v1 = new MemoryStream(); var w = new BinaryWriter(v1);
        w.Write(0x4C434752u); w.Write((byte)1); w.Write((byte)0); w.Write((byte)(10 << 1)); w.Write((byte)(1 << 1));
        w.Write((byte)(3 << 1)); w.Write((byte)ReplayKind.MoveTo); w.Write((byte)(4 << 1)); w.Write((byte)(5 << 1)); w.Write((byte)0);
        var one = CommandLog.FromBytes(v1.ToArray());
        Assert.Equal([new ReplayCommand(3, ReplayKind.MoveTo, 4, 5, 0)], one.Items);
        Assert.Contains("travel_place", log.Recent(500));
    }
}
