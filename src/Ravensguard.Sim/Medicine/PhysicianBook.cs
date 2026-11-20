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

    /// <summary>The hero's treatment queue (medicine 4.1, MT-13): saved with the physicians in the optional <c>medicine</c> section.</summary>
    public TreatQueue Queue { get; private set; } = new();

    public IReadOnlyList<PhysicianRow> Rows => _rows;
    public int Count => _rows.Count;
    public bool IsEmpty => _rows.Count == 0;
    /// <summary>True when there is nothing to save: no physician and nothing queued.</summary>
    public bool IsBlank => _rows.Count == 0 && Queue.IsEmpty;

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

    public MedicineSave Capture() => new() { Physicians = [.. _rows], Queue = Queue.IsEmpty ? null : Queue.Capture() };

    public static PhysicianBook Restore(MedicineSave s)
    {
        var b = new PhysicianBook();
        foreach (var r in s.Physicians) b.Add(r);
        b.Queue = TreatQueue.Restore(s.Queue);
        return b;
    }
}

/// <summary>The optional <c>medicine</c> save section (overview 7): the physicians, 16 bytes a row. Absent while the region has none.</summary>
[MessagePackObject]
public sealed class MedicineSave
{
    public const string SectionName = "medicine";
    [Key(0)] public PhysicianRow[] Physicians { get; set; } = [];
    /// <summary>The hero's treatment steps still waiting (absent while none).</summary>
    [Key(1)] public TreatStep[]? Queue { get; set; }
}
