namespace Ravensguard.Sim.Core;

public enum IdKind : byte { Person, Household, Item, Settlement, Faction, Parcel, Field, Chunk, Event, Other }

/// <summary>64-bit id: kind in the top 8 bits, per-kind counter in the low 56.</summary>
public readonly record struct EntityId(ulong Value) : IComparable<EntityId>
{
    public const int CounterBits = 56;
    public static readonly EntityId None = new(0);

    public static EntityId Make(IdKind kind, ulong counter) => new(((ulong)kind << CounterBits) | (counter & ((1UL << CounterBits) - 1)));
    /// <summary>E2: bits 48-55 of the counter hold the origin region (0..255), bits 0-47 the serial inside that region.</summary>
    public const int SerialBits = 48;
    public const ulong SerialMask = (1UL << SerialBits) - 1;
    public ulong Serial => Value & SerialMask;
    public int Region => (int)((Value >> SerialBits) & 0xFF);
    /// <summary>First counter of ids made in <paramref name="region"/>: the region bits with serial 0.</summary>
    public static ulong RegionBase(int region) => (ulong)(region & 0xFF) << SerialBits;
    public static EntityId MakeIn(IdKind kind, int region, ulong serial) => Make(kind, RegionBase(region) | (serial & SerialMask));
    /// <summary>
    /// Serial bit 47: ids made outside a region's allocator (jobs, generated road travellers) set it, so they never meet a record's id; an allocator counts from 1 and never
    /// reaches 2^47 serials. Each user keeps its own kind (jobs are <see cref="IdKind.Other"/>, travellers <see cref="IdKind.Person"/>), so two such spaces never meet either.
    /// </summary>
    public const ulong ReservedSerialBit = 1UL << 47;
    public bool IsReservedSerial => (Value & ReservedSerialBit) != 0;
    /// <summary>The users of the reserved serial space, in bits 40-46 under <see cref="ReservedSerialBit"/> (space 0 is the jobs', as they always were).</summary>
    public enum ReservedSpace : byte { Jobs = 0, Travellers = 1, Camps = 2, Carcasses = 3 }
    /// <summary>The <paramref name="n"/>th id of a reserved space (n below 2^40).</summary>
    public static EntityId Reserved(IdKind kind, int region, ReservedSpace space, ulong n) => MakeIn(kind, region, ReservedSerialBit | ((ulong)space << 40) | (n & ((1UL << 40) - 1)));
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

    /// <summary>An allocator whose every counter starts at serial 1 of <paramref name="region"/> (E2), so records of two regions never share an id.</summary>
    public static IdAllocator ForRegion(int region) { var a = new IdAllocator(); Array.Fill(a._next, EntityId.RegionBase(region) | 1UL); return a; }

    public EntityId Allocate(IdKind kind) => EntityId.Make(kind, _next[(int)kind]++);

    /// <summary>The id the next <see cref="Allocate"/> of this kind will return.</summary>
    public EntityId Peek(IdKind kind) => EntityId.Make(kind, _next[(int)kind]);

    /// <summary>Raises the next counter of <paramref name="kind"/> to at least <paramref name="nextCounter"/> (never lowers it). Used where a table derives ids from its own base counter.</summary>
    public void EnsureNext(IdKind kind, ulong nextCounter) { if (_next[(int)kind] < nextCounter) _next[(int)kind] = nextCounter; }

    /// <summary>Next counter per kind, indexed by IdKind.</summary>
    public ulong[] GetState() => (ulong[])_next.Clone();

    public void SetState(IReadOnlyList<ulong> state)
    {
        if (state.Count != _next.Length) throw new ArgumentException("State length mismatch.", nameof(state));
        for (int i = 0; i < _next.Length; i++) _next[i] = state[i];
    }

    public static IdAllocator FromState(IReadOnlyList<ulong> state) { var a = new IdAllocator(); a.SetState(state); return a; }
}
