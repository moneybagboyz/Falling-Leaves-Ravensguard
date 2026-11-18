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
    private MoveInput _input; private SimSpeed _speed; private bool _torch; private bool _haveInput;

    public SessionRecorder(LiveWorld world)
    {
        World = world;
        _speed = world.Clock.Speed; _torch = world.Torch;
        Rebase();
    }

    /// <summary>Called when an autosave is taken: the window restarts at this tick, carrying the standing input, speed and torch, plus the hash of the saved state.</summary>
    public void Rebase()
    {
        long t = World.Tick;
        Commands.Rebase(t, [ReplayCommand.Input(t, _input), ReplayCommand.Speed(t, _speed), ReplayCommand.Torch(t, _torch)]);
        Hashes.Clear();
        Hashes.Add(new HashRecord(t, StateHashTrail.KindLive, LiveStateHash.Compute(World)));
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

    public void Step()
    {
        World.Step();
        long t = World.Tick;
        Commands.NoteTick(t);
        if (t % StateHashTrail.Interval == 0) Hashes.Add(new HashRecord(t, StateHashTrail.KindLive, LiveStateHash.Compute(World)));
    }

    /// <summary>The trace and the hashes as the files of a report (<c>replay.trace</c>, <c>hashes.csv</c>).</summary>
    public (byte[] Trace, string HashesCsv) Export() => (Commands.ToBytes(), Hashes.ToCsv());
}
