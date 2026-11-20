using MessagePack;
using Ravensguard.Sim.Combat.Resolver2;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.People;

namespace Ravensguard.Sim.Medicine;

/// <summary>Physician (a town's learned man) or barber-surgeon (a village's, cheaper and less able).</summary>
public enum PhysicianKind : byte { Physician, Barber }

/// <summary>One physician (medicine 3): the person (his id), his business (index, -1 while he has none) and the settlement he treats in.</summary>
[MessagePackObject]
public readonly record struct PhysicianRow([property: Key(0)] ulong Person, [property: Key(1)] int Business, [property: Key(2)] PhysicianKind Kind, [property: Key(3)] int Settlement);

/// <summary>
/// Local stand-ins for what the Phase 6 shared plumbing (A2) registers: the stream of physician seeding and <c>AbstractPhysic</c>, <c>Occupation.Physician</c> and
/// <c>TransferReason.Fee</c>. TODO(P6 merge): replace with <c>RngStreams.Physician</c> (0x600), <c>Occupation.Physician</c> and <c>TransferReason.Fee</c> and delete this class; until
/// then the occupation and the reason are looked up by name, so the code works before and after the enums grow, and the occupation stays unchanged while it does not exist.
/// </summary>
public static class MedicineEnums
{
    /// <summary>Physician seeding (key person id, draw 0) and <c>AbstractPhysic</c> (key person id, tick = game day, draw 1); overview 5, 0x600.</summary>
    public const uint PhysicianStream = 0x600;
    public static readonly Occupation? PhysicianOccupation = Enum.TryParse<Occupation>("Physician", out var o) ? o : null;
    /// <summary>The fee's ledger reason: <c>Fee</c> when A2's enum has it, else a <c>Gift</c> (the coin moves either way and the audit holds).</summary>
    public static readonly TransferReason FeeReason = Enum.TryParse<TransferReason>("Fee", out var r) ? r : TransferReason.Gift;
}

/// <summary>
/// The physicians and barber-surgeons of a region (medicine 4.4), kept in the world's service bag and saved in the optional <c>medicine</c> section (absent while empty). Rows are
/// kept in ascending person id, so every walk is in stable-id order.
/// </summary>
public sealed class PhysicianBook
{
    private readonly List<PhysicianRow> _rows = [];
    private readonly Dictionary<ulong, int> _byPerson = [];
    private Dictionary<int, List<PhysicianRow>>? _bySettlement;

    /// <summary>Resolves a person's body plan by id for the abstract tier's tourniquets; null leaves the physic to bandages and pressure (set by the host that holds the combat content).</summary>
    public Func<string, BodyPlanV2?>? Plans { get; set; }

    public IReadOnlyList<PhysicianRow> Rows => _rows;
    public int Count => _rows.Count;
    public bool IsEmpty => _rows.Count == 0;

    public void Add(PhysicianRow row)
    {
        if (_byPerson.ContainsKey(row.Person)) return;
        int i = _rows.BinarySearch(row, Comparer<PhysicianRow>.Create((a, b) => a.Person.CompareTo(b.Person)));
        _rows.Insert(~i, row);
        _byPerson.Clear(); for (int k = 0; k < _rows.Count; k++) _byPerson[_rows[k].Person] = k;
        _bySettlement = null;
    }

    public bool TryGet(ulong person, out PhysicianRow row)
    {
        if (_byPerson.TryGetValue(person, out int i)) { row = _rows[i]; return true; }
        row = default; return false;
    }

    /// <summary>The physicians and barbers of a settlement, physicians first, each in person id order.</summary>
    public IReadOnlyList<PhysicianRow> In(int settlement)
    {
        if (_bySettlement == null)
        {
            _bySettlement = [];
            foreach (var r in _rows.OrderBy(r => r.Kind).ThenBy(r => r.Person)) { if (!_bySettlement.TryGetValue(r.Settlement, out var l)) _bySettlement[r.Settlement] = l = []; l.Add(r); }
        }
        return _bySettlement.TryGetValue(settlement, out var list) ? list : [];
    }

    public int Physicians(int settlement) => In(settlement).Count(r => r.Kind == PhysicianKind.Physician);
    public int Barbers(int settlement) => In(settlement).Count(r => r.Kind == PhysicianKind.Barber);

    /// <summary>Drops a person (he died or left): his row goes; the settlement may seat another at the next seeding.</summary>
    public bool Remove(ulong person)
    {
        int i = _rows.FindIndex(r => r.Person == person); if (i < 0) return false;
        _rows.RemoveAt(i); _byPerson.Clear(); for (int k = 0; k < _rows.Count; k++) _byPerson[_rows[k].Person] = k; _bySettlement = null;
        return true;
    }

    public MedicineSave Capture() => new() { Physicians = [.. _rows] };

    public static PhysicianBook Restore(MedicineSave s)
    {
        var b = new PhysicianBook();
        foreach (var r in s.Physicians) b.Add(r);
        return b;
    }
}

/// <summary>The optional <c>medicine</c> save section (overview 7): the physicians, 16 bytes a row. Absent while the region has none.</summary>
[MessagePackObject]
public sealed class MedicineSave
{
    public const string SectionName = "medicine";
    [Key(0)] public PhysicianRow[] Physicians { get; set; } = [];
}
