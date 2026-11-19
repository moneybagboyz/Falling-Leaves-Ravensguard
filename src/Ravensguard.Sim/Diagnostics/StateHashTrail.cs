using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Ravensguard.Sim.Live;

namespace Ravensguard.Sim.Diagnostics;

/// <summary>One recorded hash: the live tick, which state it covers and 64 bits of its SHA-256.</summary>
public readonly record struct HashRecord(long Tick, string Kind, ulong Hash);

/// <summary>Where a replay stopped matching the recording.</summary>
public sealed record Divergence(int Index, long Tick, string Kind, ulong Expected, ulong Actual)
{
    public override string ToString() => $"first mismatch at hash #{Index}: kind '{Kind}', tick {Tick}, recorded {Expected:x16}, replayed {Actual:x16}";
}

/// <summary>The record hashes of a session (testers spec 3.6): a live-state hash every <see cref="Interval"/> ticks. Saved as <c>hashes.csv</c>.</summary>
public sealed class StateHashTrail
{
    /// <summary>One hash every 400 ticks = 20 real seconds at 20 Hz.</summary>
    public const int Interval = 400;
    public const string KindLive = "live";
    /// <summary>The state at T0 (what a save keeps).</summary>
    public const string KindStart = "start";
    private readonly List<HashRecord> _items = [];
    private readonly object _gate = new();

    public IReadOnlyList<HashRecord> Items { get { lock (_gate) return _items.ToArray(); } }
    public int Count { get { lock (_gate) return _items.Count; } }
    public void Add(HashRecord r) { lock (_gate) _items.Add(r); }
    public void Clear() { lock (_gate) _items.Clear(); }

    public string ToCsv()
    {
        var sb = new StringBuilder("tick,kind,hash\n");
        foreach (var r in Items) sb.Append(r.Tick.ToString(CultureInfo.InvariantCulture)).Append(',').Append(r.Kind).Append(',').Append(r.Hash.ToString("x16")).Append('\n');
        return sb.ToString();
    }

    public static StateHashTrail FromCsv(string csv)
    {
        var t = new StateHashTrail();
        foreach (var line in csv.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Skip(1))
        {
            var p = line.Split(',');
            if (p.Length != 3) continue;
            t._items.Add(new HashRecord(long.Parse(p[0], CultureInfo.InvariantCulture), p[1], ulong.Parse(p[2], NumberStyles.HexNumber, CultureInfo.InvariantCulture)));
        }
        return t;
    }

    /// <summary>The first entry of <paramref name="recorded"/> that <paramref name="replayed"/> does not reproduce (a missing entry counts), or null when they agree.</summary>
    public static Divergence? FirstMismatch(StateHashTrail recorded, StateHashTrail replayed)
    {
        var a = recorded.Items; var b = replayed.Items;
        var byKey = b.ToDictionary(r => (r.Tick, r.Kind), r => r.Hash);
        for (int i = 0; i < a.Count; i++)
        {
            if (!byKey.TryGetValue((a[i].Tick, a[i].Kind), out var h)) { if (a[i].Tick <= (b.Count > 0 ? b[^1].Tick : long.MinValue)) return new Divergence(i, a[i].Tick, a[i].Kind, a[i].Hash, 0); continue; }
            if (h != a[i].Hash) return new Divergence(i, a[i].Tick, a[i].Kind, a[i].Hash, h);
        }
        return null;
    }
}

/// <summary>
/// The live-state hashes: the hero, the clock and the path, all integers (Q16.16), so they are the same on every machine. Positions are world positions (the window
/// origin added), so a world resumed from a save, whose window may be cut differently, hashes like the original. The tick counter is left out (a resumed world counts from 0).
/// </summary>
public static class LiveStateHash
{
    /// <summary>The whole state: used at every 400th tick after the start.</summary>
    public static ulong Compute(LiveWorld w) => Hash(w, full: true);

    /// <summary>Only what a save keeps (clock, position, facing, torch, the walk in progress and its gait): the check of the starting state T0.</summary>
    public static ulong Saved(LiveWorld w) => Hash(w, full: false);

    private static ulong Hash(LiveWorld w, bool full)
    {
        var s = w.Snapshot; var h = s.Hero;
        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var b = new byte[8];
        void L(long v) { System.Buffers.Binary.BinaryPrimitives.WriteInt64LittleEndian(b, v); sha.AppendData(b); }
        long ox = (long)w.OriginX << Ravensguard.Sim.Core.Fixed.FracBits, oy = (long)w.OriginY << Ravensguard.Sim.Core.Fixed.FracBits;
        L(w.Clock.Now.Seconds); L(w.Clock.GameMsRemainder); L((int)w.Clock.Speed); L((int)w.WantedSpeed);
        L(ox + h.X.Raw); L(oy + h.Y.Raw); L(h.Z); L((int)h.Facing); L(h.Torch ? 1 : 0);
        if (full) { L(ox + h.PrevX.Raw); L(oy + h.PrevY.Raw); L(h.PrevZ); L(h.Moving ? 1 : 0); L((int)h.Mode); L(h.PathRemaining); if (w.Fight is { } f) { L((long)f.Hash()); if (f.Watch is { } wt) L((long)wt.Hash()); } }     // P4-F: the WatchHash joins the trail
        else L(h.PathRemaining);                       // the walk in progress is saved (Phase 3 cleanup), so a load starts mid-route exactly where the autosave left it
        return BitConverter.ToUInt64(sha.GetHashAndReset(), 0);
    }
}
