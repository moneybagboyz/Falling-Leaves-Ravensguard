using Ravensguard.Sim.Core;

namespace Ravensguard.Sim.People;

/// <summary>
/// Struct-of-arrays store of every Person (plan §2.1a). A person is an index; ids are dense counters so
/// <see cref="IdOf"/> and <see cref="IndexOf"/> are O(1). Links to other people are indices, -1 for none.
/// </summary>
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
        var c = new PersonTable(Count, _baseCounter);
        for (int i = 0; i < Count; i++) c.Add();
        void Cp<T>(T[] src, T[] dst) => Array.Copy(src, dst, Math.Min(src.Length, dst.Length));
        Cp(Given, c.Given); Cp(Family, c.Family); Cp(Byname, c.Byname); Cp(Sex, c.Sex); Cp(BirthDay, c.BirthDay); Cp(Culture, c.Culture); Cp(Household, c.Household);
        Cp(Father, c.Father); Cp(Mother, c.Mother); Cp(Spouse, c.Spouse); Cp(Occupation, c.Occupation); Cp(Employer, c.Employer); Cp(WorkSettlement, c.WorkSettlement);
        Cp(WorkLot, c.WorkLot); Cp(Class, c.Class); Cp(Attr, c.Attr); Cp(Skills, c.Skills); Cp(Traits, c.Traits); Cp(HomeSettlement, c.HomeSettlement); Cp(HomeLot, c.HomeLot);
        Cp(Bed, c.Bed); Cp(Homeless, c.Homeless); Cp(Notable, c.Notable); Cp(Dynasty, c.Dynasty); Cp(Status, c.Status); Cp(DeathDay, c.DeathDay); Cp(DeathCause, c.DeathCause);
        Cp(PregnantUntilDay, c.PregnantUntilDay); Cp(FatherOfUnborn, c.FatherOfUnborn); Cp(LastBirthDay, c.LastBirthDay); Cp(MarriageWait, c.MarriageWait);
        foreach (var (i, id) in ForeignIds) c.SetForeignId(i, id);
        return c;
    }

    // People who moved in from another region keep the id their origin region gave them (E2, spec 3.6): row -> id, and back.
    private SortedDictionary<int, EntityId>? _foreign;
    private Dictionary<EntityId, int>? _foreignIndex;

    /// <summary>Rows whose id was made in another region, ascending row.</summary>
    public IReadOnlyDictionary<int, EntityId> ForeignIds => _foreign ?? (IReadOnlyDictionary<int, EntityId>)EmptyForeign;
    private static readonly SortedDictionary<int, EntityId> EmptyForeign = [];

    /// <summary>Gives row <paramref name="index"/> an id from another region (an arrival keeps its origin id).</summary>
    public void SetForeignId(int index, EntityId id)
    {
        (_foreign ??= [])[index] = id;
        (_foreignIndex ??= [])[id] = index;
    }

    public EntityId IdOf(int index) => _foreign != null && _foreign.TryGetValue(index, out var f) ? f : EntityId.Make(IdKind.Person, _baseCounter + (ulong)index);
    public int IndexOf(EntityId id)
    {
        if (_foreignIndex != null && _foreignIndex.TryGetValue(id, out int fi)) return fi;
        return id.Kind == IdKind.Person && id.Counter >= _baseCounter && id.Counter - _baseCounter < (ulong)Count ? (int)(id.Counter - _baseCounter) : -1;
    }

    public int Add()
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

    public int AgeYears(int i, int nowDay) => Math.Max(0, (nowDay - BirthDay[i]) / TimeConst.DaysPerYear);
    public string FullName(int i) => Byname[i].Length > 0 ? $"{Given[i]} {Family[i]} {Byname[i]}" : $"{Given[i]} {Family[i]}";

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
