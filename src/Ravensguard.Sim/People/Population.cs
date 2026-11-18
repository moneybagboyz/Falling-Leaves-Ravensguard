using System.Security.Cryptography;
using System.Text;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.WorldGen.Region;
using RegionData = Ravensguard.Sim.WorldGen.Region.Region;

namespace Ravensguard.Sim.People;

/// <summary>Stage 3 part B output (plan §1A, §2.1a): every person and household of the embark region plus the bindings to its lots, fiefs and parcels.</summary>
public sealed class Population
{
    public required RegionData Region { get; init; }
    public required PersonTable People { get; init; }
    public required HouseholdTable Households { get; init; }
    /// <summary>The "now" of world generation as a game day (history end, spring day 1).</summary>
    public int NowDay { get; init; }
    public int NowYear => NowDay / TimeConst.DaysPerYear;

    /// <summary>Per settlement, per lot: the household living there (-1 none).</summary>
    public required int[][] LotHousehold { get; init; }
    /// <summary>Per settlement, per lot: beds in the dwelling (0 for workplaces and civic lots).</summary>
    public required int[][] LotBeds { get; init; }
    /// <summary>Per settlement, per lot: the person who runs the workplace (smith, innkeeper, miller, priest) or lives in the manor as lord, -1 none.</summary>
    public required int[][] LotKeeper { get; init; }
    /// <summary>Per fief: the lord (person index) and his household, -1 when none.</summary>
    public required int[] FiefLord { get; init; }
    public required int[] FiefLordHousehold { get; init; }
    /// <summary>Per region parcel: the household that holds or works it (lord for demesne, priest for glebe), -1 none.</summary>
    public required int[] ParcelHolder { get; init; }
    /// <summary>Per settlement: the priest's household, -1 none.</summary>
    public required int[] PriestHousehold { get; init; }
    /// <summary>Per settlement: the people created there (count).</summary>
    public required int[] SettlementPeople { get; init; }

    private KinGraph? _kin;
    public KinGraph Kin => _kin ??= new KinGraph(People);

    /// <summary>Drops the cached kin graph; the next <see cref="Kin"/> read rebuilds it from the person links (after births).</summary>
    public void ResetKin() => _kin = null;

    /// <summary>
    /// Deep copy of everything that play changes (people, households, lot bindings); the generated region is shared. A copy is how a test or a
    /// what-if run mutates a world without touching the shared fixture.
    /// </summary>
    public Population Clone() => new()
    {
        Region = Region, People = People.Clone(), Households = Households.Clone(), NowDay = NowDay,
        LotHousehold = CopyJ(LotHousehold), LotBeds = CopyJ(LotBeds), LotKeeper = CopyJ(LotKeeper),
        FiefLord = (int[])FiefLord.Clone(), FiefLordHousehold = (int[])FiefLordHousehold.Clone(), ParcelHolder = (int[])ParcelHolder.Clone(),
        PriestHousehold = (int[])PriestHousehold.Clone(), SettlementPeople = (int[])SettlementPeople.Clone(),
    };

    private static int[][] CopyJ(int[][] a) { var r = new int[a.Length][]; for (int i = 0; i < a.Length; i++) r[i] = (int[])a[i].Clone(); return r; }

    /// <summary>
    /// Hash of the Phase 2 vital columns (status, death, pregnancy, marriage wait). Kept apart from <see cref="ComputeHash"/> so the generation hashes that
    /// the golden tests pin do not change; a running world hashes both.
    /// </summary>
    public string ComputeSimHash()
    {
        using var h = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var b8 = new byte[8];
        void L(long v) { System.Buffers.Binary.BinaryPrimitives.WriteInt64LittleEndian(b8, v); h.AppendData(b8); }
        var p = People;
        L(p.Count);
        for (int i = 0; i < p.Count; i++)
        {
            L((long)p.Status[i]); L(p.DeathDay[i]); L((long)p.DeathCause[i]); L(p.PregnantUntilDay[i]); L(p.FatherOfUnborn[i]); L(p.LastBirthDay[i]); L(p.MarriageWait[i]);
        }
        return Convert.ToHexString(h.GetHashAndReset());
    }

    public int Age(int person) => People.AgeYears(person, NowDay);

    /// <summary>Households living in a settlement, ascending index.</summary>
    public IEnumerable<int> HouseholdsOf(int settlement)
    {
        for (int h = 0; h < Households.Count; h++) if (Households.Settlement[h] == settlement) yield return h;
    }

    /// <summary>Approximate managed bytes per person including shared string storage, for the 2 KB budget (plan §2.1a).</summary>
    public long ApproxBytes()
    {
        long total = People.ArrayBytes() + Households.ArrayBytes();
        var seen = new HashSet<string>(ReferenceEqualityComparer.Instance as IEqualityComparer<string>);
        void Str(string s) { if (s.Length > 0 && seen.Add(s)) total += 22 + 2L * s.Length; }
        for (int i = 0; i < People.Count; i++) { Str(People.Given[i]); Str(People.Family[i]); Str(People.Byname[i]); }
        for (int h = 0; h < Households.Count; h++) Str(Households.Name[h]);
        return total;
    }

    public string ComputeHash()
    {
        using var h = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var b8 = new byte[8];
        void L(long v) { System.Buffers.Binary.BinaryPrimitives.WriteInt64LittleEndian(b8, v); h.AppendData(b8); }
        void S(string s) { L(s.Length); h.AppendData(Encoding.UTF8.GetBytes(s)); }
        var p = People;
        L(p.Count); L(NowDay);
        for (int i = 0; i < p.Count; i++)
        {
            S(p.Given[i]); S(p.Family[i]); S(p.Byname[i]); L((long)p.Sex[i]); L(p.BirthDay[i]); L(p.Culture[i]); L(p.Household[i]);
            L(p.Father[i]); L(p.Mother[i]); L(p.Spouse[i]); L((long)p.Occupation[i]); L(p.Employer[i]); L(p.WorkSettlement[i]); L(p.WorkLot[i]); L((long)p.Class[i]);
            for (int a = 0; a < PersonTable.AttrCount; a++) L(p.Attr[i * PersonTable.AttrCount + a]);
            for (int s = 0; s < PersonTable.SkillSlots * 2; s++) L(p.Skills[i * PersonTable.SkillSlots * 2 + s]);
            L(p.Traits[i]); L(p.HomeSettlement[i]); L(p.HomeLot[i]); L(p.Bed[i]); L((long)p.Homeless[i]); L((long)p.Notable[i].Value); L((long)p.Dynasty[i].Value);
        }
        var hh = Households;
        L(hh.Count);
        for (int i = 0; i < hh.Count; i++)
        {
            L(hh.Settlement[i]); L(hh.Lot[i]); L(hh.Head[i]); L(hh.MemberStart[i]); L(hh.MemberCount[i]); L(hh.Beds[i]); L(hh.ParcelCount[i]);
            L((long)hh.Dwelling[i]); L((long)hh.Deed[i]); L((long)hh.Class[i]); L(hh.Purse[i]); S(hh.Name[i]);
        }
        foreach (int m in hh.MemberIdx) L(m);
        foreach (var a in LotHousehold) foreach (int v in a) L(v);
        foreach (var a in LotKeeper) foreach (int v in a) L(v);
        foreach (int v in FiefLord) L(v);
        foreach (int v in ParcelHolder) L(v);
        foreach (var a in LotBeds) foreach (int v in a) L(v);
        foreach (int v in FiefLordHousehold) L(v);
        foreach (int v in PriestHousehold) L(v);
        foreach (int v in SettlementPeople) L(v);
        return Convert.ToHexString(h.GetHashAndReset());
    }
}
