using Ravensguard.Sim.Core;

namespace Ravensguard.Sim.Goods;

/// <summary>Item flags. Appended in Phase 5 (items-quality-and-wear, overview 8.1): <see cref="Named"/> (a named artefact), <see cref="Oiled"/> (no rust for 30 days),
/// <see cref="Brittle"/> (an untempered blade, crafting-full 4.3). The byte is now full: the next flag widens it with a save migration.</summary>
[Flags] public enum ItemFlags : byte { None = 0, Stained = 1, Burning = 2, Wet = 4, Repaired = 8, Stolen = 16, Named = 32, Oiled = 64, Brittle = 128 }

/// <summary>One part of an item: the material (content id), its mass in grams and its own condition (items spec 2.3).</summary>
public readonly record struct ItemComponent(string Material, int MassG, short ConditionPermille);

/// <summary>An item instance row (items spec 2.3): shape, quality, condition, wear, owner, container parent and the slice of its components.</summary>
public struct ItemRow
{
    public EntityId Id;
    public string Shape;
    /// <summary>Counted good this item folds back into (0xFFFF when none).</summary>
    public ushort Good;
    public ItemQuality Quality;
    public short ConditionPermille;
    public ushort Wear;
    public ItemFlags Flags;
    public byte Stains;
    /// <summary>Crafting labour baked into the value and the crafter's skill at the time (value formula, items spec 3.2).</summary>
    public ushort LabourHours;
    public byte Skill;
    public EntityId Owner;
    /// <summary>Row index of the container item, -1 for none.</summary>
    public int Parent;
    public int ComponentStart, ComponentCount;
    /// <summary>Bulk goods held inside (a barrel of ale is one row with contents, not forty instances).</summary>
    public ushort ContentsGood;
    public long ContentsQty;
}

/// <summary>
/// Instances of discrete items (items spec 2.3): created only when a thing becomes individual, folded back into counted stock when sold to a shop.
/// Ids come from the world's allocator (<see cref="IdKind.Item"/>), so a freed row's serial is never reused. Components are optional parts of a row.
/// </summary>
public sealed class ItemTable
{
    public const ushort NoGood = ushort.MaxValue;
    private ItemRow[] _rows = new ItemRow[16];
    private readonly List<ItemComponent> _components = [];
    private readonly Dictionary<EntityId, int> _byId = [];
    private readonly Stack<int> _free = new();

    /// <summary>Number of live instances.</summary>
    public int Count => _byId.Count;
    public bool IsLive(EntityId id) => _byId.ContainsKey(id);
    public int IndexOf(EntityId id) => _byId.TryGetValue(id, out var i) ? i : -1;
    public ref ItemRow At(int index) => ref _rows[index];
    public ref ItemRow Get(EntityId id) => ref _rows[_byId[id]];
    public ItemComponent Component(int index) => _components[index];
    public IEnumerable<ItemComponent> ComponentsOf(int row) { var r = _rows[row]; for (int k = 0; k < r.ComponentCount; k++) yield return _components[r.ComponentStart + k]; }

    /// <summary>Live row indices ascending.</summary>
    public IEnumerable<int> LiveRows()
    {
        for (int i = 0; i < _rows.Length && i < _high; i++) if (_rows[i].Id != EntityId.None) yield return i;
    }
    private int _high;

    /// <summary>Creates an instance with the given parts; the id is allocated from <paramref name="ids"/>.</summary>
    public EntityId Add(IdAllocator ids, string shape, ItemQuality quality, EntityId owner, IReadOnlyList<ItemComponent> parts, ushort good = NoGood,
        int labourHours = 0, int skill = 0, int conditionPermille = 1000, int parent = -1)
    {
        int i;
        if (_free.Count > 0) i = _free.Pop();
        else { if (_high == _rows.Length) Array.Resize(ref _rows, _rows.Length * 2); i = _high++; }
        var id = ids.Allocate(IdKind.Item);
        _rows[i] = new ItemRow
        {
            Id = id, Shape = shape, Good = good, Quality = quality, ConditionPermille = (short)conditionPermille, Owner = owner, Parent = parent,
            ComponentStart = _components.Count, ComponentCount = parts.Count, LabourHours = (ushort)labourHours, Skill = (byte)skill, ContentsGood = NoGood,
        };
        foreach (var p in parts) _components.Add(p);
        _byId[id] = i;
        return id;
    }

    /// <summary>Frees the row (its id is gone for good). Contained items keep their parent index until reparented, so free containers last.</summary>
    public bool Free(EntityId id)
    {
        if (!_byId.Remove(id, out int i)) return false;
        _rows[i] = default; _rows[i].Parent = -1; _free.Push(i);
        return true;
    }

    /// <summary>Total mass in grams of the parts plus the bulk contents of the row.</summary>
    public long MassG(int row, GoodCatalog cat)
    {
        var r = _rows[row]; long m = 0;
        for (int k = 0; k < r.ComponentCount; k++) m += _components[r.ComponentStart + k].MassG;
        if (r.ContentsGood != NoGood) m += cat.MassG(r.ContentsGood, r.ContentsQty);
        return m;
    }

    /// <summary>Value in mites at the row's quality, condition and stains (items spec 3.2). <paramref name="valueCrownsPerKg"/> looks up a material's value.</summary>
    public long ValueMites(int row, Func<string, double> valueCrownsPerKg, int wageMitesPerHour = ItemMath.WageSkilled)
    {
        var r = _rows[row]; long basis = 0;
        for (int k = 0; k < r.ComponentCount; k++) { var c = _components[r.ComponentStart + k]; basis += ItemMath.MaterialMites(c.MassG, valueCrownsPerKg(c.Material)); }
        basis += ItemMath.LabourMites(r.LabourHours, wageMitesPerHour, r.Skill);
        return ItemMath.ValueMites(basis, r.Quality, r.ConditionPermille, r.Stains, r.Wear);
    }

    // ---- save support: live rows only, in row order, with parents remapped (the Compact of items spec 4) ----

    public sealed record Snapshot(ItemRow[] Rows, ItemComponent[] Components);

    public Snapshot Compact()
    {
        var map = new Dictionary<int, int>(); var rows = new List<ItemRow>(); var comps = new List<ItemComponent>();
        foreach (int i in LiveRows()) { map[i] = rows.Count; rows.Add(_rows[i]); }
        for (int k = 0; k < rows.Count; k++)
        {
            var r = rows[k]; int start = comps.Count;
            for (int c = 0; c < r.ComponentCount; c++) comps.Add(_components[r.ComponentStart + c]);
            r.ComponentStart = start;
            r.Parent = r.Parent >= 0 && map.TryGetValue(r.Parent, out var np) ? np : -1;
            rows[k] = r;
        }
        return new Snapshot(rows.ToArray(), comps.ToArray());
    }

    public static ItemTable FromSnapshot(Snapshot s)
    {
        var t = new ItemTable();
        t._rows = new ItemRow[Math.Max(16, s.Rows.Length)];
        for (int i = 0; i < s.Rows.Length; i++) { t._rows[i] = s.Rows[i]; t._byId[s.Rows[i].Id] = i; }
        t._high = s.Rows.Length; t._components.AddRange(s.Components);
        return t;
    }
}
