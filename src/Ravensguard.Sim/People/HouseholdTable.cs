using Ravensguard.Sim.Core;
using Ravensguard.Sim.WorldGen.Region;

namespace Ravensguard.Sim.People;

/// <summary>
/// Struct-of-arrays store of every household (plan §4A.2): the unit of dwelling, deed and class.
/// Members are a contiguous slice of <see cref="MemberIdx"/> per household.
/// </summary>
public sealed class HouseholdTable
{
    private readonly ulong _baseCounter;
    private int _cap;
    public int Count { get; private set; }

    public int[] Settlement, Head, MemberStart;
    public short[] Lot, MemberCount, Beds, ParcelCount;
    public DwellingType[] Dwelling;
    public DeedKind[] Deed;
    public SocialClass[] Class;
    /// <summary>Coin in the household purse (placeholder until the economy lands).</summary>
    public int[] Purse;
    public string[] Name;
    public List<int> MemberIdx { get; } = [];

    public HouseholdTable(int capacity, ulong baseCounter)
    {
        _baseCounter = baseCounter;
        _cap = Math.Max(16, capacity);
        Settlement = new int[_cap]; Head = new int[_cap]; MemberStart = new int[_cap]; Lot = new short[_cap]; MemberCount = new short[_cap]; Beds = new short[_cap];
        ParcelCount = new short[_cap]; Dwelling = new DwellingType[_cap]; Deed = new DeedKind[_cap]; Class = new SocialClass[_cap]; Purse = new int[_cap]; Name = new string[_cap];
    }

    public EntityId IdOf(int index) => EntityId.Make(IdKind.Household, _baseCounter + (ulong)index);
    public int IndexOf(EntityId id) => id.Kind == IdKind.Household && id.Counter >= _baseCounter && id.Counter - _baseCounter < (ulong)Count ? (int)(id.Counter - _baseCounter) : -1;

    public int Add(int settlement, int lot, DwellingType dwelling, int beds)
    {
        if (Count == _cap) Grow();
        int i = Count++;
        Settlement[i] = settlement; Lot[i] = (short)lot; Dwelling[i] = dwelling; Beds[i] = (short)beds; Head[i] = -1; MemberStart[i] = MemberIdx.Count; MemberCount[i] = 0; Name[i] = "";
        return i;
    }

    private void Grow()
    {
        int n = _cap * 2;
        Settlement = G(Settlement, n); Head = G(Head, n); MemberStart = G(MemberStart, n); Lot = G(Lot, n); MemberCount = G(MemberCount, n); Beds = G(Beds, n);
        ParcelCount = G(ParcelCount, n); Dwelling = G(Dwelling, n); Deed = G(Deed, n); Class = G(Class, n); Purse = G(Purse, n); Name = G(Name, n);
        _cap = n;
    }

    private static T[] G<T>(T[] a, int n) { var r = new T[n]; Array.Copy(a, r, a.Length); return r; }

    /// <summary>Members must be appended in one run right after <see cref="Add"/>, before the next household is added.</summary>
    public void AddMember(int household, int person)
    {
        MemberIdx.Add(person);
        MemberCount[household]++;
    }

    public IEnumerable<int> Members(int household)
    {
        int s = MemberStart[household], n = MemberCount[household];
        for (int k = 0; k < n; k++) yield return MemberIdx[s + k];
    }

    public long ArrayBytes() => (long)Count * (4 * 3 + 2 * 4 + 1 * 3 + 4 + IntPtr.Size) + MemberIdx.Count * 4L;
}
