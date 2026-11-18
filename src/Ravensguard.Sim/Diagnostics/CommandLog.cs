using System.Text;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Live;
using Ravensguard.Sim.World;

namespace Ravensguard.Sim.Diagnostics;

/// <summary>The player commands the live sim takes. Never key codes: only what the sim was asked to do.</summary>
public enum ReplayKind : byte { Input = 1, Speed = 2, Torch = 3, MoveTo = 4, UsePortal = 5, ClearPath = 6, Ride = 7 }

/// <summary>One command and the live tick it was issued at (it takes effect at the next step).</summary>
public readonly record struct ReplayCommand(long Tick, ReplayKind Kind, long A = 0, long B = 0, long C = 0)
{
    public static ReplayCommand Input(long tick, MoveInput i) => new(tick, ReplayKind.Input, i.Dx, i.Dy, (i.Sprint ? 1 : 0) | (i.Walk ? 2 : 0));
    public static ReplayCommand Speed(long tick, SimSpeed s) => new(tick, ReplayKind.Speed, (int)s);
    public static ReplayCommand Torch(long tick, bool on) => new(tick, ReplayKind.Torch, on ? 1 : 0);
    public static ReplayCommand MoveTo(long tick, Cell3 c) => new(tick, ReplayKind.MoveTo, c.X, c.Y, c.Z);
    public static ReplayCommand UsePortal(long tick) => new(tick, ReplayKind.UsePortal);
    public static ReplayCommand ClearPath(long tick) => new(tick, ReplayKind.ClearPath);
    public static ReplayCommand Ride(long tick, int code) => new(tick, ReplayKind.Ride, code);

    /// <summary>One readable line for crash reports ("tick 4021 move_to 12,40,0").</summary>
    public override string ToString() => Kind switch
    {
        ReplayKind.Input => $"tick {Tick} input dx={A} dy={B} sprint={(C & 1) != 0} walk={(C & 2) != 0}",
        ReplayKind.Speed => $"tick {Tick} speed {A}",
        ReplayKind.Torch => $"tick {Tick} torch {(A != 0 ? "on" : "off")}",
        ReplayKind.MoveTo => $"tick {Tick} move_to {A},{B},{C}",
        ReplayKind.UsePortal => $"tick {Tick} use_portal",
        ReplayKind.Ride => $"tick {Tick} ride {A}",
        _ => $"tick {Tick} clear_path",
    };

    /// <summary>Applies this command to a live world the way the game did.</summary>
    public void Apply(LiveWorld w)
    {
        switch (Kind)
        {
            case ReplayKind.Input: w.SetInput(new MoveInput((int)A, (int)B, (C & 1) != 0, (C & 2) != 0)); break;
            case ReplayKind.Speed: w.SetSpeed((SimSpeed)(int)A); break;
            case ReplayKind.Torch: w.SetTorch(A != 0); break;
            case ReplayKind.MoveTo: w.CommandMoveTo(new Cell3((int)A, (int)B, (int)C)); break;
            case ReplayKind.UsePortal: w.UsePortal(); break;
            case ReplayKind.ClearPath: w.ClearPath(); break;
            case ReplayKind.Ride: w.RideCommand((int)A); break;
        }
    }
}

/// <summary>
/// The commands since the last autosave (T0 = <see cref="BaseTick"/>) for deterministic replay (testers spec 3.6, 4.5). Thread safe. Saved next to autosaves as
/// <c>replay.trace</c>, compactly: delta-coded ticks and zig-zag varints, so ten minutes of play stays far under 40 KB.
/// </summary>
public sealed class CommandLog
{
    private const uint Magic = 0x4C434752; // "RGCL"
    private const int FormatVersion = 1;
    private readonly List<ReplayCommand> _items = [];
    private readonly object _gate = new();

    /// <summary>The live tick of the autosave this trace starts from.</summary>
    public long BaseTick { get; private set; }
    /// <summary>The newest tick the trace knows (the report moment, or the crash tick).</summary>
    public long EndTick { get; private set; }
    /// <summary>The most commands kept; the oldest go first (a trace is also bounded by the autosave cadence).</summary>
    public int Capacity { get; init; } = 200_000;

    public int Count { get { lock (_gate) return _items.Count; } }
    public IReadOnlyList<ReplayCommand> Items { get { lock (_gate) return _items.ToArray(); } }

    public void Append(ReplayCommand c)
    {
        lock (_gate)
        {
            if (_items.Count >= Capacity) _items.RemoveAt(0);
            _items.Add(c);
            if (c.Tick > EndTick) EndTick = c.Tick;
        }
    }

    public void NoteTick(long tick) { lock (_gate) if (tick > EndTick) EndTick = tick; }

    /// <summary>Starts a new window at an autosave: the log restarts at <paramref name="tick"/> carrying the standing state (input held, speed, torch) so a replay from the save begins right.</summary>
    public void Rebase(long tick, IEnumerable<ReplayCommand> standing)
    {
        lock (_gate) { _items.Clear(); BaseTick = tick; EndTick = tick; foreach (var c in standing) _items.Add(c with { Tick = tick }); }
    }

    /// <summary>The last <paramref name="n"/> commands as text lines (crash.txt).</summary>
    public string Recent(int n = 50)
    {
        lock (_gate) return string.Join('\n', _items.Skip(Math.Max(0, _items.Count - n)).Select(c => c.ToString()));
    }

    public byte[] ToBytes()
    {
        lock (_gate)
        {
            using var ms = new MemoryStream();
            using var w = new BinaryWriter(ms, Encoding.UTF8, true);
            w.Write(Magic); w.Write((byte)FormatVersion);
            V(w, BaseTick); V(w, EndTick); V(w, _items.Count);
            long prev = BaseTick;
            foreach (var c in _items) { V(w, c.Tick - prev); prev = c.Tick; w.Write((byte)c.Kind); V(w, c.A); V(w, c.B); V(w, c.C); }
            w.Flush();
            return ms.ToArray();
        }
    }

    public static CommandLog FromBytes(byte[] data)
    {
        using var r = new BinaryReader(new MemoryStream(data), Encoding.UTF8);
        if (r.ReadUInt32() != Magic) throw new InvalidDataException("not a replay trace");
        int ver = r.ReadByte();
        if (ver != FormatVersion) throw new InvalidDataException($"replay trace version {ver} is not supported");
        var log = new CommandLog { BaseTick = R(r), EndTick = 0 };
        long end = R(r); int n = (int)R(r);
        long prev = log.BaseTick;
        for (int i = 0; i < n; i++)
        {
            prev += R(r);
            var kind = (ReplayKind)r.ReadByte();
            log._items.Add(new ReplayCommand(prev, kind, R(r), R(r), R(r)));
        }
        log.EndTick = end;
        return log;
    }

    private static void V(BinaryWriter w, long v)
    {
        ulong z = (ulong)((v << 1) ^ (v >> 63));
        while (z >= 0x80) { w.Write((byte)(z | 0x80)); z >>= 7; }
        w.Write((byte)z);
    }

    private static long R(BinaryReader r)
    {
        ulong z = 0; int shift = 0;
        while (true)
        {
            byte b = r.ReadByte();
            z |= (ulong)(b & 0x7F) << shift;
            if ((b & 0x80) == 0) break;
            shift += 7;
            if (shift > 63) throw new InvalidDataException("bad varint");
        }
        return (long)(z >> 1) ^ -(long)(z & 1);
    }
}
