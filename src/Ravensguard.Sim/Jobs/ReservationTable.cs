using Ravensguard.Sim.Core;

namespace Ravensguard.Sim.Jobs;

/// <summary>
/// What is held for a job while it runs (live-behaviour spec 3.3): a bed, a tile, a station, a stock lot, a cart. One holder at a time; a hold lapses at a game second (never a tick count), so it lapses at
/// the same game time at any speed. Keys are ordered, so every walk of the table is in one order on every run.
/// </summary>
public sealed class ReservationTable
{
    public readonly record struct Key(ReserveKind Kind, ulong Id) : IComparable<Key>
    {
        public int CompareTo(Key o) => Kind != o.Kind ? Kind.CompareTo(o.Kind) : Id.CompareTo(o.Id);
    }
    public readonly record struct Hold(EntityId Holder, EntityId Job, long ExpiresSec);

    private readonly SortedDictionary<Key, Hold> _holds = [];
    public int Count => _holds.Count;
    public IReadOnlyDictionary<Key, Hold> Holds => _holds;

    /// <summary>Takes the hold, or confirms it when this job already has it. False when another job holds it and the hold has not lapsed.</summary>
    public bool TryReserve(ReserveKind kind, ulong id, EntityId holder, EntityId job, long nowSec, long ttlSec)
    {
        var k = new Key(kind, id);
        if (_holds.TryGetValue(k, out var h) && h.ExpiresSec > nowSec && h.Job != job) return false;
        _holds[k] = new Hold(holder, job, nowSec + ttlSec);
        return true;
    }

    public bool IsHeldByOther(ReserveKind kind, ulong id, EntityId job, long nowSec) => _holds.TryGetValue(new Key(kind, id), out var h) && h.ExpiresSec > nowSec && h.Job != job;
    public bool TryHolder(ReserveKind kind, ulong id, long nowSec, out Hold hold) { if (_holds.TryGetValue(new Key(kind, id), out hold) && hold.ExpiresSec > nowSec) return true; hold = default; return false; }

    /// <summary>Extends every hold of the job (a step that is running keeps what it needs).</summary>
    public void Refresh(EntityId job, long nowSec, long ttlSec)
    {
        foreach (var k in _holds.Keys.ToList()) if (_holds[k].Job == job) _holds[k] = _holds[k] with { ExpiresSec = nowSec + ttlSec };
    }

    public void Release(EntityId job) { foreach (var k in _holds.Where(kv => kv.Value.Job == job).Select(kv => kv.Key).ToList()) _holds.Remove(k); }

    /// <summary>Drops the holds that lapsed by <paramref name="nowSec"/>; returns how many.</summary>
    public int Expire(long nowSec)
    {
        var dead = _holds.Where(kv => kv.Value.ExpiresSec <= nowSec).Select(kv => kv.Key).ToList();
        foreach (var k in dead) _holds.Remove(k);
        return dead.Count;
    }

    public void Restore(Key k, Hold h) => _holds[k] = h;
}
