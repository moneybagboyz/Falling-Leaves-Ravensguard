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

    /// <summary>First counter of this table's household ids: row <c>i</c> has counter <c>BaseCounter + i</c>.</summary>
    public ulong BaseCounter => _baseCounter;

    /// <summary>
    /// Adds a member to a household that already exists (a birth, a marriage): appends in place when the household's run is the last one in
    /// <see cref="MemberIdx"/>, else moves the run to the end first (the old run becomes -1 holes; <see cref="CompactMembers"/> reclaims them).
    /// </summary>
    public void AddMemberLater(int household, int person)
    {
        int s = MemberStart[household], n = MemberCount[household];
        if (s + n != MemberIdx.Count)
        {
            int ns = MemberIdx.Count;
            for (int k = 0; k < n; k++) { MemberIdx.Add(MemberIdx[s + k]); MemberIdx[s + k] = -1; }
            MemberStart[household] = ns;
        }
        MemberIdx.Add(person);
        MemberCount[household]++;
    }

    /// <summary>Removes a member by swapping it with the last of the run (population spec 3.1). False when the person is not a member.</summary>
    public bool RemoveMember(int household, int person)
    {
        int s = MemberStart[household], n = MemberCount[household];
        for (int k = 0; k < n; k++)
        {
            if (MemberIdx[s + k] != person) continue;
            MemberIdx[s + k] = MemberIdx[s + n - 1];
            MemberIdx[s + n - 1] = -1;
            MemberCount[household]--;
            if (s + n == MemberIdx.Count) MemberIdx.RemoveAt(MemberIdx.Count - 1); // the run was last: give the freed slot back
            return true;
        }
        return false;
    }

    /// <summary>Rewrites <see cref="MemberIdx"/> without holes, households in index order. Member order inside each run is kept.</summary>
    public void CompactMembers()
    {
        var fresh = new List<int>(MemberIdx.Count);
        for (int h = 0; h < Count; h++)
        {
            int s = MemberStart[h], n = MemberCount[h];
            MemberStart[h] = fresh.Count;
            for (int k = 0; k < n; k++) fresh.Add(MemberIdx[s + k]);
        }
        MemberIdx.Clear(); MemberIdx.AddRange(fresh);
    }

    /// <summary>Deep copy of the table.</summary>
    public HouseholdTable Clone()
    {
        var c = new HouseholdTable(Count, _baseCounter);
        for (int i = 0; i < Count; i++) c.Add(0, 0, default, 0);
        void Cp<T>(T[] src, T[] dst) => Array.Copy(src, dst, Math.Min(src.Length, dst.Length));
        Cp(Settlement, c.Settlement); Cp(Head, c.Head); Cp(MemberStart, c.MemberStart); Cp(Lot, c.Lot); Cp(MemberCount, c.MemberCount); Cp(Beds, c.Beds); Cp(ParcelCount, c.ParcelCount);
        Cp(Dwelling, c.Dwelling); Cp(Deed, c.Deed); Cp(Class, c.Class); Cp(Purse, c.Purse); Cp(Name, c.Name);
        c.MemberIdx.Clear(); c.MemberIdx.AddRange(MemberIdx);
        return c;
    }

    public IEnumerable<int> Members(int household)
    {
        int s = MemberStart[household], n = MemberCount[household];
        for (int k = 0; k < n; k++) yield return MemberIdx[s + k];
    }

    public long ArrayBytes() => (long)Count * (4 * 3 + 2 * 4 + 1 * 3 + 4 + IntPtr.Size) + MemberIdx.Count * 4L;
}
