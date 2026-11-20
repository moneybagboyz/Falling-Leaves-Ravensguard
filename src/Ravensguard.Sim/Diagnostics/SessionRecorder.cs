using Ravensguard.Sim.Core;
using Ravensguard.Sim.Live;
using Ravensguard.Sim.World;

namespace Ravensguard.Sim.Diagnostics;

/// <summary>
/// The live sim as the game drives it, with every player command written to the <see cref="CommandLog"/> at the tick it was issued and a record hash taken every
/// 400 steps. The view calls these methods instead of the <see cref="LiveWorld"/> ones. Only the main thread calls it; the logs are thread safe for readers
/// (the crash handler, the report builder).
/// </summary>
public sealed class SessionRecorder
{
    public LiveWorld World { get; }
    public CommandLog Commands { get; } = new();
    public StateHashTrail Hashes { get; } = new();
    private MoveInput _input; private SimSpeed _speed, _wanted; private bool _torch; private bool _haveInput; private int _loadPm = 1000;

    public SessionRecorder(LiveWorld world)
    {
        World = world;
        _speed = world.Clock.Speed; _wanted = world.WantedSpeed; _torch = world.Torch; _loadPm = world.LoadSpeedPm;
        Rebase();
    }

    /// <summary>Called when an autosave is taken: the window restarts at this tick, carrying the standing input, speed and torch, plus the hash of the saved state.</summary>
    public void Rebase()
    {
        long t = World.Tick;
        Commands.Rebase(t, [ReplayCommand.Input(t, _input), ReplayCommand.Speed(t, _speed), ReplayCommand.Torch(t, _torch), ReplayCommand.LoadSpeed(t, _loadPm), ReplayCommand.WantedSpeed(t, _wanted)]);
        Hashes.Clear();
        Hashes.Add(new HashRecord(t, StateHashTrail.KindStart, LiveStateHash.Saved(World)));
    }

    public void SetInput(MoveInput input)
    {
        if (!_haveInput || input != _input) { Commands.Append(ReplayCommand.Input(World.Tick, input)); _input = input; _haveInput = true; }
        World.SetInput(input);
    }

    public void SetSpeed(SimSpeed s)
    {
        if (s != _speed) { Commands.Append(ReplayCommand.Speed(World.Tick, s)); _speed = s; }
        World.SetSpeed(s);
    }

    /// <summary>The speed asked for (the speed keys), recorded when it changes: a replay then gates the clock the same way.</summary>
    public void SetWantedSpeed(SimSpeed s)
    {
        if (s != _wanted) { Commands.Append(ReplayCommand.WantedSpeed(World.Tick, s)); _wanted = s; }
        World.SetWantedSpeed(s);
    }

    public void SetTorch(bool on)
    {
        if (on != _torch) { Commands.Append(ReplayCommand.Torch(World.Tick, on)); _torch = on; }
        World.SetTorch(on);
    }

    public void ToggleTorch() => SetTorch(!World.Torch);
    public void ClearPath() { Commands.Append(ReplayCommand.ClearPath(World.Tick)); World.ClearPath(); }

    public bool CommandMoveTo(Cell3 target)
    {
        Commands.Append(ReplayCommand.MoveTo(World.Tick, target));
        return World.CommandMoveTo(target);
    }

    public bool UsePortal()
    {
        bool has = World.UsePortal();
        if (has) Commands.Append(ReplayCommand.UsePortal(World.Tick)); // recorded only when it will act: the replay then repeats exactly what happened
        return has;
    }

    /// <summary>E, hitch, drive, push, unload and dig (<see cref="RideCode"/>): recorded so a replay of a ride repeats it.</summary>
    public void RideCommand(int code) { Commands.Append(ReplayCommand.Ride(World.Tick, code)); World.RideCommand(code); }

    /// <summary>Auto-travel (and the autopilot's long walk) puts the hero at a point of the route: recorded every time, so a replay walks the same trip.</summary>
    public void TravelPlace(long xMm, long yMm, Facing facing)
    {
        Commands.Append(ReplayCommand.TravelPlace(World.Tick, xMm, yMm, facing));
        World.TravelPlace(xMm, yMm, facing);
    }

    /// <summary>The carried load's speed factor (permille): recorded when it changes (a trade, a craft or picking something up changes the load, and with it how far a tick walks).</summary>
    public void SetLoadSpeed(int pm)
    {
        if (pm != _loadPm) { Commands.Append(ReplayCommand.LoadSpeed(World.Tick, pm)); _loadPm = pm; }
        World.LoadSpeedPm = pm;
    }

    /// <summary>A command the hero asked of the world's records (trade, crafting, talk, hire, an encounter's toll): kind, sequence number, game second and a digest of the command.</summary>
    public void Hero(int kind, long seq, long issuedSec, long digest) => Commands.Append(ReplayCommand.Hero(World.Tick, kind, seq, issuedSec, digest));

    /// <summary>The same for a stamped desk command (hook it to <see cref="Ravensguard.Sim.Trade.HeroWorld.Issued"/>).</summary>
    public void Hero(Ravensguard.Sim.Trade.HeroCommand c) => Hero((int)c.Kind, c.Seq, c.IssuedSec, DigestOf(c));

    /// <summary>A digest of what a desk command asks (FNV-1a over its fields; strings by their UTF-8 bytes), the same in every process.</summary>
    public static long DigestOf(Ravensguard.Sim.Trade.HeroCommand c)
    {
        ulong h = 14695981039346656037UL;
        void L(long v) { for (int i = 0; i < 8; i++) { h ^= (byte)(v >> (i * 8)); h *= 1099511628211UL; } }
        void S(string? s) { if (s == null) { L(-1); return; } foreach (byte b in System.Text.Encoding.UTF8.GetBytes(s)) { h ^= b; h *= 1099511628211UL; } L(s.Length); }
        L((long)c.Kind); L(c.Seq); L(c.IssuedSec); L(c.Settlement); L(c.Good); L(c.Qty); L(c.Limit); L((long)c.Item.Value); L((long)c.Container.Value); L((long)c.Person.Value);
        L((long)c.Slot); L(c.Wage); L(c.Days); L((long)c.Terms); L((long)c.Danger); L((long)c.Order); L(c.X); L(c.Y); L((long)c.Job.Value); L(c.Recipe); L(c.Batch); L(c.UntilSec); L((long)c.Origin);
        L(c.CoinGive); L(c.CoinTake); L(c.Region); S(c.Tag);
        if (c.Choice != null) foreach (int x in c.Choice) L(x);
        foreach (var (g, q) in c.Give) { L(g); L(q); }
        foreach (var (g, q) in c.Take) { L(g); L(q); }
        foreach (var e in c.GiveItems) L((long)e.Value);
        if (c.Site is { } s) { L(s.Light); L(s.Water ? 1 : 0); foreach (var t in s.Tools) S(t); foreach (var t in s.Stations) S(t); foreach (var t in s.Improvised) S(t); }
        return (long)h;
    }

    /// <summary>A hero action outside the desk (camp, autopilot directives, a talk choice, a journey begun or cancelled).</summary>
    public void Action(HeroAction a, long b = 0, long c = 0) => Commands.Append(ReplayCommand.Action(World.Tick, a, b, c));

    /// <summary>The starting record of every fight begun in this window (by fight id), so a replay rebuilds the fight where it began (<see cref="LiveWorld.FightFactory"/>).</summary>
    public Dictionary<long, byte[]> FightStarts { get; } = [];

    /// <summary>A fight begins in the bubble (a road encounter, the yard): recorded with its starting state.</summary>
    public void StartFight(Ravensguard.Sim.Combat.Melee2.FightWorld fight, int heroFighter)
    {
        FightStarts[(long)fight.FightId] = Ravensguard.Sim.Save.FightRecord.Capture(fight).ToBytes();
        Commands.Append(ReplayCommand.FightStart(World.Tick, (long)fight.FightId, heroFighter));
        World.AttachFight(fight, heroFighter);
    }

    public void EndFight() { Commands.Append(ReplayCommand.FightEnd(World.Tick)); World.EndFight(); }

    /// <summary>Every combat input of the hero (move, face, block, attack, dodge, reload, throw, target, called shot) is recorded: a replay of a fight repeats it exactly.</summary>
    public void FightInput(Ravensguard.Sim.Combat.Melee2.FightInput i) { Commands.Append(ReplayCommand.Fight(World.Tick, i)); World.FightInput(i); }

    // P4-H: the hero underground; the crawl's inputs are recorded like any live input, its start and end too (the game's SiteFactory rebuilds it for a replay)
    /// <summary>The floor under the hero fell (P5-B): he lands on level <paramref name="z"/>, recorded so a replay drops him at the same tick (P5-I review, finding 4).</summary>
    public void DropToLevel(int z) { Commands.Append(ReplayCommand.DropLevel(World.Tick, z)); World.DropToLevel(z); }
    public void EnterSite(Ravensguard.Sim.Sites.SiteCrawl crawl) { Commands.Append(ReplayCommand.SiteStart(World.Tick, crawl.Map.SiteId)); World.AttachSite(crawl); }
    public void LeaveSite() { Commands.Append(ReplayCommand.SiteEnd(World.Tick)); World.DetachSite(); }
    public void SiteInput(Ravensguard.Sim.Sites.CrawlInput i) { Commands.Append(ReplayCommand.Site(World.Tick, i)); World.SiteInput(i); }

    public void Step()
    {
        World.Step();
        long t = World.Tick;
        Commands.NoteTick(t);
        if (t % StateHashTrail.Interval == 0) Hashes.Add(new HashRecord(t, StateHashTrail.KindLive, LiveStateHash.Compute(World)));
    }

    /// <summary>The trace and the hashes as the files of a report (<c>replay.trace</c>, <c>hashes.csv</c>).</summary>
    /// <summary>Includes one more hash for the moment of the export (unless it is on the 400-tick grid already), so a replay checks the state it ends in too.</summary>
    public (byte[] Trace, string HashesCsv) Export()
    {
        var copy = new StateHashTrail();
        foreach (var r in Hashes.Items) copy.Add(r);
        long t = World.Tick;
        if (t % StateHashTrail.Interval != 0 && t != Commands.BaseTick) copy.Add(new HashRecord(t, StateHashTrail.KindLive, LiveStateHash.Compute(World)));
        Commands.NoteTick(t);
        return (Commands.ToBytes(), copy.ToCsv());
    }
}
