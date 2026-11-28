using Ravensguard.Sim.Core;

namespace Ravensguard.Sim.People;

/// <summary>
/// Struct-of-arrays store of every Person (plan §2.1a). A person is an index; ids are dense counters so
/// <see cref="IdOf"/> and <see cref="IndexOf"/> are O(1). Links to other people are indices, -1 for none.
/// </summary>
/// <summary>Which kin link of a person a cross-region reference stands for.</summary>
public enum KinLink : byte { Father = 0, Mother = 1, Spouse = 2 }

/// <summary>An authored character's record (P8-LM-2): the content id, the post in region metres (-1 when the character has none, a wanderer or townsperson) and the name of the heirloom carried ("" none).</summary>
public sealed record AuthoredInfo(string Id, int X, int Y, string Heirloom);

public sealed class PersonTable
{
    public const int SkillSlots = 4;
    public const int AttrCount = 5;
    public const int StrengthIdx = 0, ToughnessIdx = 1, DexterityIdx = 2, PerceptionIdx = 3, WitsIdx = 4;

    private readonly ulong _baseCounter;
    private int _cap;
    public int Count { get; private set; }

    public string[] Given, Family, Byname;
    public Sex[] Sex;
    /// <summary>Birth day in game days since year 0 (plan calendar: 60 days a year).</summary>
    public int[] BirthDay;
    public byte[] Culture;
    public int[] Household, Father, Mother, Spouse;
    public Occupation[] Occupation;
    /// <summary>Person who employs this one (lord, master, head of household), -1 for self-employed or none.</summary>
    public int[] Employer;
    /// <summary>Settlement and lot (-1 for fields or none) where the job is done.</summary>
    public int[] WorkSettlement;
    public short[] WorkLot;
    public SocialClass[] Class;
    /// <summary>5 bytes per person: strength, toughness, dexterity, perception, wits.</summary>
    public byte[] Attr;
    /// <summary>4 slots of (skill, level) per person.</summary>
    public byte[] Skills;
    /// <summary>Trait bit set; placeholder until the traits system.</summary>
    public ushort[] Traits;
    public int[] HomeSettlement;
    public short[] HomeLot, Bed;
    public HomelessReason[] Homeless;
    /// <summary>History notable this person is (lords and their kin), else None.</summary>
    public EntityId[] Notable;
    public EntityId[] Dynasty;

    // ---- Phase 2 vital columns (population spec 2). Defaults for a person created at world generation: alive, not pregnant, never gave birth.
    public PersonStatus[] Status;
    /// <summary>Game day of death, -1 while alive.</summary>
    public int[] DeathDay;
    public DeathCause[] DeathCause;
    /// <summary>Game day the pregnancy ends, -1 when not pregnant.</summary>
    public int[] PregnantUntilDay;
    /// <summary>Person index of the father of the unborn child (the spouse at conception), -1 none.</summary>
    public int[] FatherOfUnborn;
    /// <summary>Game day of the last birth (postpartum gap), int.MinValue/2 when none.</summary>
    public int[] LastBirthDay;
    /// <summary>Seasons spent eligible to marry without a home (population spec 3.3).</summary>
    public byte[] MarriageWait;
    public const int NeverBorn = int.MinValue / 2;

    public PersonTable(int capacity, ulong baseCounter)
    {
        _baseCounter = baseCounter;
        _cap = Math.Max(16, capacity);
        Given = new string[_cap]; Family = new string[_cap]; Byname = new string[_cap]; Sex = new Sex[_cap]; BirthDay = new int[_cap]; Culture = new byte[_cap];
        Household = new int[_cap]; Father = new int[_cap]; Mother = new int[_cap]; Spouse = new int[_cap]; Occupation = new Occupation[_cap]; Employer = new int[_cap];
        WorkSettlement = new int[_cap]; WorkLot = new short[_cap]; Class = new SocialClass[_cap]; Attr = new byte[_cap * AttrCount]; Skills = new byte[_cap * SkillSlots * 2];
        Traits = new ushort[_cap]; HomeSettlement = new int[_cap]; HomeLot = new short[_cap]; Bed = new short[_cap]; Homeless = new HomelessReason[_cap];
        Notable = new EntityId[_cap]; Dynasty = new EntityId[_cap];
        Status = new PersonStatus[_cap]; DeathDay = new int[_cap]; DeathCause = new DeathCause[_cap]; PregnantUntilDay = new int[_cap]; FatherOfUnborn = new int[_cap];
        LastBirthDay = new int[_cap]; MarriageWait = new byte[_cap];
    }

    /// <summary>First counter of this table's person ids: row <c>i</c> has counter <c>BaseCounter + i</c>.</summary>
    public ulong BaseCounter => _baseCounter;

    public bool IsAlive(int i) => Status[i] == PersonStatus.Alive;

    /// <summary>Deep copy of the table (strings are immutable and shared).</summary>
    public PersonTable Clone()
    {
        var c = new PersonTable(Count, _baseCounter) { AgingPace = AgingPace };
        for (int i = 0; i < Count; i++) c.Add();
        void Cp<T>(T[] src, T[] dst) => Array.Copy(src, dst, Math.Min(src.Length, dst.Length));
        Cp(Given, c.Given); Cp(Family, c.Family); Cp(Byname, c.Byname); Cp(Sex, c.Sex); Cp(BirthDay, c.BirthDay); Cp(Culture, c.Culture); Cp(Household, c.Household);
        Cp(Father, c.Father); Cp(Mother, c.Mother); Cp(Spouse, c.Spouse); Cp(Occupation, c.Occupation); Cp(Employer, c.Employer); Cp(WorkSettlement, c.WorkSettlement);
        Cp(WorkLot, c.WorkLot); Cp(Class, c.Class); Cp(Attr, c.Attr); Cp(Skills, c.Skills); Cp(Traits, c.Traits); Cp(HomeSettlement, c.HomeSettlement); Cp(HomeLot, c.HomeLot);
        Cp(Bed, c.Bed); Cp(Homeless, c.Homeless); Cp(Notable, c.Notable); Cp(Dynasty, c.Dynasty); Cp(Status, c.Status); Cp(DeathDay, c.DeathDay); Cp(DeathCause, c.DeathCause);
        Cp(PregnantUntilDay, c.PregnantUntilDay); Cp(FatherOfUnborn, c.FatherOfUnborn); Cp(LastBirthDay, c.LastBirthDay); Cp(MarriageWait, c.MarriageWait);
        foreach (var (i, id) in ForeignIds) c.SetForeignId(i, id);
        foreach (var (row, link, id) in ForeignKin) c.SetForeignKin(row, link, id);
        if (_authored != null) c._authored = new SortedDictionary<int, AuthoredInfo>(_authored);
        if (_free != null) c._free = new SortedSet<int>(_free);
        if (_retired != null) c._retired = new SortedSet<int>(_retired);
        c.Ids = Ids;
        return c;
    }

    // ---- P8-LM-2: unique NPCs are ordinary rows; this sparse map is what says which authored character a row is (spec 4: an "Authored" id key), where they keep their post and what they carry
    private SortedDictionary<int, AuthoredInfo>? _authored;
    /// <summary>The rows that are authored characters (ascending row).</summary>
    public IReadOnlyDictionary<int, AuthoredInfo> Authored => _authored ?? (IReadOnlyDictionary<int, AuthoredInfo>)EmptyAuthored;
    private static readonly SortedDictionary<int, AuthoredInfo> EmptyAuthored = [];
    public void SetAuthored(int row, AuthoredInfo info) => (_authored ??= [])[row] = info;
    public AuthoredInfo? AuthoredOf(int row) => _authored != null && _authored.TryGetValue(row, out var a) ? a : null;
    public bool IsAuthored(int row) => _authored != null && _authored.ContainsKey(row);
    /// <summary>The row of the authored character <paramref name="id"/>, or -1 (a dead one still has a row until the yearly compaction drops it).</summary>
    public int RowOfAuthored(string id) { if (_authored != null) foreach (var (r, a) in _authored) if (a.Id == id) return r; return -1; }

    // People who moved in from another region keep the id their origin region gave them (E2, spec 3.6): row -> id, and back.
    private SortedDictionary<int, EntityId>? _foreign;
    private Dictionary<EntityId, int>? _foreignIndex;

    /// <summary>Rows whose id was made in another region, ascending row.</summary>
    public IReadOnlyDictionary<int, EntityId> ForeignIds => _foreign ?? (IReadOnlyDictionary<int, EntityId>)EmptyForeign;
    private static readonly SortedDictionary<int, EntityId> EmptyForeign = [];

    /// <summary>Gives row <paramref name="index"/> an id from another region (an arrival keeps its origin id).</summary>
    public void SetForeignId(int index, EntityId id)
    {
        _foreign ??= []; _foreignIndex ??= [];
        if (_foreign.TryGetValue(index, out var old)) _foreignIndex.Remove(old);
        _foreign[index] = id;
        _foreignIndex[id] = index;
    }

    // A kin link whose other end lives in another region cannot be a row number: it is kept as that person's id (spec 3.6), keyed by (row, link).
    private SortedDictionary<long, EntityId>? _foreignKin;

    /// <summary>Kin links to people of other regions (a person who moved in, whose parent or spouse stayed), ascending row then link.</summary>
    public IEnumerable<(int Row, KinLink Link, EntityId Id)> ForeignKin
    {
        get { if (_foreignKin != null) foreach (var (k, id) in _foreignKin) yield return ((int)(k / 4), (KinLink)(k % 4), id); }
    }

    /// <summary>Records that <paramref name="link"/> of row <paramref name="row"/> is the person <paramref name="id"/> of another region.</summary>
    public void SetForeignKin(int row, KinLink link, EntityId id) => (_foreignKin ??= [])[(long)row * 4 + (int)link] = id;

    /// <summary>The other-region person a kin link points at, or <see cref="EntityId.None"/>.</summary>
    public EntityId ForeignKinOf(int row, KinLink link) => _foreignKin != null && _foreignKin.TryGetValue((long)row * 4 + (int)link, out var id) ? id : EntityId.None;

    public EntityId IdOf(int index) => _foreign != null && _foreign.TryGetValue(index, out var f) ? f : EntityId.Make(IdKind.Person, _baseCounter + (ulong)index);
    public int IndexOf(EntityId id)
    {
        if (_foreignIndex != null && _foreignIndex.TryGetValue(id, out int fi)) return fi;
        if (id.Kind != IdKind.Person || id.Counter < _baseCounter || id.Counter - _baseCounter >= (ulong)Count) return -1;
        int r = (int)(id.Counter - _baseCounter);
        return _retired != null && _retired.Contains(r) ? -1 : r;          // P8-PF-2: a dropped or reused row no longer answers to its first id
    }

    // ---- P8-PF-2: dropping dead rows (09-performance-and-tooling 3.3). A dropped row is blanked and kept as a blank dead row (the free list is the record of them); births append under new ids from the allocator, so an id and a row are never reused. Rows whose first id (base counter + row) has been retired are in _retired and found only through the id map.
    private SortedSet<int>? _free, _retired;
    /// <summary>The allocator new people take their ids from once a row has been dropped (set by the runtime and the loader); with none, a table that has dropped rows cannot make people.</summary>
    public IdAllocator? Ids;
    /// <summary>Rows waiting for a new person, ascending.</summary>
    public IReadOnlyCollection<int> FreeRows => (IReadOnlyCollection<int>?)_free ?? Array.Empty<int>();
    /// <summary>Rows whose first id is retired (dropped, or reused under another id), ascending.</summary>
    public IReadOnlyCollection<int> RetiredRows => (IReadOnlyCollection<int>?)_retired ?? Array.Empty<int>();
    public bool IsFree(int row) => _free != null && _free.Contains(row);
    /// <summary>Restores the free list and the retired set of a loaded save.</summary>
    public void RestoreRetired(IEnumerable<int> free, IEnumerable<int> retired)
    {
        _free = new SortedSet<int>(free); _retired = new SortedSet<int>(retired);
        if (_free.Count == 0) _free = null;
        if (_retired.Count == 0) _retired = null;
    }

    /// <summary>Drops the dead person of <paramref name="row"/>: every column goes back to a new row's, the row stays dead, and it joins the free list. The caller has checked that nothing names the person.</summary>
    public void Drop(int row)
    {
        _authored?.Remove(row);
        if (_foreign != null && _foreign.Remove(row, out var f)) _foreignIndex?.Remove(f);
        if (_foreignKin != null) for (int l = 0; l < 4; l++) _foreignKin.Remove((long)row * 4 + l);
        Blank(row);
        Status[row] = PersonStatus.Dead;
        (_retired ??= []).Add(row);
        (_free ??= []).Add(row);
    }

    private void Blank(int i)
    {
        Given[i] = Family[i] = Byname[i] = "";
        Sex[i] = default; BirthDay[i] = 0; Culture[i] = 0; Occupation[i] = default; WorkLot[i] = HomeLot[i] = Bed[i] = -1; Class[i] = default; Traits[i] = 0; Homeless[i] = default;
        Household[i] = Father[i] = Mother[i] = Spouse[i] = Employer[i] = WorkSettlement[i] = HomeSettlement[i] = -1;
        Notable[i] = EntityId.None; Dynasty[i] = EntityId.None;
        Array.Clear(Attr, i * AttrCount, AttrCount); Array.Clear(Skills, i * SkillSlots * 2, SkillSlots * 2);
        Status[i] = default; DeathDay[i] = PregnantUntilDay[i] = FatherOfUnborn[i] = -1; DeathCause[i] = default; LastBirthDay[i] = NeverBorn; MarriageWait[i] = 0;
    }

    /// <summary>A new person with the id <paramref name="id"/> (from the allocator), always on a new row at the end. Dropped rows are never taken again: many books hold a person's row number (works owners, crews, relation pairs, the live tier), and a stale number must meet a blank dead row, never a newborn.</summary>
    public int Add(EntityId id)
    {
        int r = AddRow();
        if (id == EntityId.Make(IdKind.Person, _baseCounter + (ulong)r)) return r;
        (_retired ??= []).Add(r);
        SetForeignId(r, id);
        return r;
    }

    public int Add()
    {
        if (_retired != null)
        {
            if (Ids == null) throw new InvalidOperationException("a person table that has dropped rows needs its id allocator to make people");
            return Add(Ids.Allocate(IdKind.Person));
        }
        return AddRow();
    }

    private int AddRow()
    {
        if (Count == _cap) Grow();
        int i = Count++;
        Given[i] = Family[i] = Byname[i] = "";
        Household[i] = Father[i] = Mother[i] = Spouse[i] = Employer[i] = WorkSettlement[i] = HomeSettlement[i] = -1;
        WorkLot[i] = HomeLot[i] = Bed[i] = -1;
        DeathDay[i] = PregnantUntilDay[i] = FatherOfUnborn[i] = -1; LastBirthDay[i] = NeverBorn;
        return i;
    }

    private void Grow()
    {
        int n = _cap * 2;
        Given = G(Given, n); Family = G(Family, n); Byname = G(Byname, n); Sex = G(Sex, n); BirthDay = G(BirthDay, n); Culture = G(Culture, n);
        Household = G(Household, n); Father = G(Father, n); Mother = G(Mother, n); Spouse = G(Spouse, n); Occupation = G(Occupation, n); Employer = G(Employer, n);
        WorkSettlement = G(WorkSettlement, n); WorkLot = G(WorkLot, n); Class = G(Class, n); Attr = G(Attr, n * AttrCount); Skills = G(Skills, n * SkillSlots * 2);
        Traits = G(Traits, n); HomeSettlement = G(HomeSettlement, n); HomeLot = G(HomeLot, n); Bed = G(Bed, n); Homeless = G(Homeless, n);
        Notable = G(Notable, n); Dynasty = G(Dynasty, n);
        Status = G(Status, n); DeathDay = G(DeathDay, n); DeathCause = G(DeathCause, n); PregnantUntilDay = G(PregnantUntilDay, n); FatherOfUnborn = G(FatherOfUnborn, n);
        LastBirthDay = G(LastBirthDay, n); MarriageWait = G(MarriageWait, n);
        _cap = n;
    }

    private static T[] G<T>(T[] a, int n) { var r = new T[n]; Array.Copy(a, r, a.Length); return r; }

    /// <summary>The world's ageing pace (1 to 4, <c>aging_pace</c>): only the ageing clock scales, so a person is <c>(days lived) x pace / 60</c> years old and the per-year vital chances stay per game year.</summary>
    public int AgingPace = 1;
    public int AgeYears(int i, int nowDay) => AgeYearsOf(BirthDay[i], nowDay, AgingPace);
    public static int AgeYearsOf(int birthDay, long nowDay, int pace) => (int)Math.Max(0, (nowDay - birthDay) * Math.Max(1, pace) / TimeConst.DaysPerYear);
    /// <summary>The birth day of someone <paramref name="ageYears"/> years and <paramref name="extraDays"/> (0..59, in ageing days) old on <paramref name="nowDay"/> at <paramref name="pace"/>.</summary>
    public static int BirthDayFor(long nowDay, int ageYears, int extraDays, int pace) => (int)(nowDay - ((long)ageYears * TimeConst.DaysPerYear + extraDays) / Math.Max(1, pace));
    public string FullName(int i)
    {
        string n = Family[i].Length > 0 ? $"{Given[i]} {Family[i]}" : Given[i];      // P5-I: no trailing space when there is no family name
        return Byname[i].Length > 0 ? $"{n} {Byname[i]}" : n;
    }

    public int Attribute(int i, int which) => Attr[i * AttrCount + which];
    public SkillLevel SkillAt(int i, int slot) => new((Skill)Skills[(i * SkillSlots + slot) * 2], Skills[(i * SkillSlots + slot) * 2 + 1]);
    public int SkillLevelOf(int i, Skill s)
    {
        for (int k = 0; k < SkillSlots; k++) if ((Skill)Skills[(i * SkillSlots + k) * 2] == s) return Skills[(i * SkillSlots + k) * 2 + 1];
        return 0;
    }
    public void SetSkill(int i, int slot, Skill s, int level) { Skills[(i * SkillSlots + slot) * 2] = (byte)s; Skills[(i * SkillSlots + slot) * 2 + 1] = (byte)Math.Clamp(level, 0, 100); }

    /// <summary>Bytes held by the arrays (strings counted as references), per person.</summary>
    public long ArrayBytes()
    {
        long per = 3 * IntPtr.Size + 1 + 4 + 1 + 4 * 4 + 1 + 4 + 4 + 2 + 1 + AttrCount + SkillSlots * 2 + 2 + 4 + 2 + 2 + 1 + 8 + 8;
        return per * Count;
    }
}
