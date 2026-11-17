namespace Ravensguard.Sim.Core;

public enum IdKind : byte { Person, Household, Item, Settlement, Faction, Parcel, Field, Chunk, Event, Other }

/// <summary>64-bit id: kind in the top 8 bits, per-kind counter in the low 56.</summary>
public readonly record struct EntityId(ulong Value) : IComparable<EntityId>
{
    public const int CounterBits = 56;
    public static readonly EntityId None = new(0);

    public static EntityId Make(IdKind kind, ulong counter) => new(((ulong)kind << CounterBits) | (counter & ((1UL << CounterBits) - 1)));
    public IdKind Kind => (IdKind)(Value >> CounterBits);
    public ulong Counter => Value & ((1UL << CounterBits) - 1);
    public int CompareTo(EntityId other) => Value.CompareTo(other.Value);
    public override string ToString() => $"{Kind}#{Counter}";
}

/// <summary>Hands out monotonically increasing ids per kind (counters start at 1). State is serializable.</summary>
public sealed class IdAllocator
{
    private readonly ulong[] _next = new ulong[Enum.GetValues<IdKind>().Length];

    public IdAllocator() { Array.Fill(_next, 1UL); }

    public EntityId Allocate(IdKind kind) => EntityId.Make(kind, _next[(int)kind]++);

    /// <summary>Next counter per kind, indexed by IdKind.</summary>
    public ulong[] GetState() => (ulong[])_next.Clone();

    public void SetState(IReadOnlyList<ulong> state)
    {
        if (state.Count != _next.Length) throw new ArgumentException("State length mismatch.", nameof(state));
        for (int i = 0; i < _next.Length; i++) _next[i] = state[i];
    }

    public static IdAllocator FromState(IReadOnlyList<ulong> state) { var a = new IdAllocator(); a.SetState(state); return a; }
}
