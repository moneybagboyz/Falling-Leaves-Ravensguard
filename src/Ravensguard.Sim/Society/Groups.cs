using Ravensguard.Sim.Core;

namespace Ravensguard.Sim.Society;

/// <summary>What kind of group a standing is held with (reputation spec 3). Append only.</summary>
public enum GroupKind : byte { Settlement, Fief, Realm, Religion, Guild, Band, Household }

/// <summary>A group of people (reputation spec 3, 4 bytes: kind and index). Its members are the persons whose home, fief, realm, faith, guild or band it is.</summary>
public readonly record struct GroupRef(GroupKind Kind, int Index) : IComparable<GroupRef>
{
    public static GroupRef Settlement(int i) => new(GroupKind.Settlement, i);
    public static GroupRef Fief(int i) => new(GroupKind.Fief, i);
    public static GroupRef Realm(int i) => new(GroupKind.Realm, i);
    public static GroupRef Religion(int i) => new(GroupKind.Religion, i);
    public bool IsNone => Index < 0;
    /// <summary>The 4-byte form saved and hashed: the kind in the top byte, the index below (24 bits).</summary>
    public uint Packed => (uint)((byte)Kind << 24 | Index & 0xFFFFFF);
    public static GroupRef Unpack(uint v) => new((GroupKind)(v >> 24), (int)(v & 0xFFFFFF));
    public int CompareTo(GroupRef other) => Packed.CompareTo(other.Packed);
    public override string ToString() => $"{Kind}:{Index}";
}

/// <summary>
/// How groups relate and how many people each holds, as the standing book reads them (reputation 4.1): a fief and a realm are sets of settlements, a religion is a set of persons. Built from the
/// region by <see cref="RegionGroups"/>, or by hand in a test. Members are the living persons of the group.
/// </summary>
public interface IGroupMap
{
    /// <summary>The fief a settlement belongs to, or <see cref="GroupRef"/> with a negative index.</summary>
    GroupRef FiefOf(int settlement);
    GroupRef RealmOf(int settlement);
    /// <summary>The settlements a group spans (a settlement is its own; a religion spans the settlements where someone of the faith lives, P6-B4).</summary>
    IReadOnlyList<int> SettlementsOf(GroupRef g);
    /// <summary>The living persons in the group, 0 when unknown.</summary>
    int Members(GroupRef g);
    /// <summary>The living adults of the whole region (the denominator of renown).</summary>
    int RegionAdults { get; }
    /// <summary>The settlement a person lives in (an index into the region's people), -1 when unknown.</summary>
    int HomeOf(EntityId person);
    /// <summary>The religion of a settlement (its culture's), a group with a negative index when it has none (P6-B4: religion groups count).</summary>
    GroupRef ReligionOf(int settlement) => new(GroupKind.Religion, -1);
    /// <summary>The religion of a person (their own culture's), a group with a negative index when unknown.</summary>
    GroupRef ReligionOfPerson(EntityId person) => new(GroupKind.Religion, -1);
}
