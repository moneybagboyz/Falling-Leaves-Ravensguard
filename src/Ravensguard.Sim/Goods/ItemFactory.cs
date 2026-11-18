using Ravensguard.Content;
using Ravensguard.Sim.Core;

namespace Ravensguard.Sim.Goods;

/// <summary>
/// Builds item instances from content shapes and materials, and converts between counted piece stock and instances
/// (<c>Materialise</c> / <c>Dematerialise</c>, items spec 2.3). Mass comes from shape volume x material density in whole grams per part.
/// </summary>
public sealed class ItemFactory
{
    private readonly ContentDatabase _db;
    private readonly GoodCatalog _cat;
    private readonly IReadOnlyDictionary<string, ResolvedMaterial> _materials;

    public ItemFactory(ContentDatabase db, GoodCatalog catalog)
    {
        _db = db; _cat = catalog; _materials = MaterialResolver.ResolveAll(db);
    }

    public ResolvedMaterial Material(string id) => _materials[id];
    public double ValueCrownsPerKg(string material) => _materials[material].ValuePerKg;

    /// <summary>Head and haft parts of a weapon shape in a head material (the haft uses the shape's haft material, else the head's).</summary>
    public ItemComponent[] WeaponParts(WeaponShapeDef shape, string headMaterial)
    {
        var head = _materials[headMaterial];
        var parts = new List<ItemComponent> { new(head.Id, (int)ItemMath.ComponentMassG(shape.HeadVolumeCm3, head.DensityKgM3), 1000) };
        if (shape.HaftVolumeCm3 > 0)
        {
            var haft = shape.HaftMaterial != null ? _materials[shape.HaftMaterial] : head;
            parts.Add(new(haft.Id, (int)ItemMath.ComponentMassG(shape.HaftVolumeCm3, haft.DensityKgM3), 1000));
        }
        return parts.ToArray();
    }

    public EntityId CreateWeapon(ItemTable items, IdAllocator ids, string shapeId, string headMaterial, ItemQuality quality, EntityId owner,
        int labourHours = 0, int skill = 0, ushort good = ItemTable.NoGood)
        => items.Add(ids, shapeId, quality, owner, WeaponParts(_db.Get<WeaponShapeDef>(shapeId), headMaterial), good, labourHours, skill);

    /// <summary>
    /// Turns one counted piece of <paramref name="good"/> from <paramref name="store"/> into an instance at Common quality and condition 1000
    /// (stock minus one, destroyed as Materialised). Null when the store has none or the good has no shape and material.
    /// </summary>
    public EntityId? Materialise(Store store, ItemTable items, IdAllocator ids, ushort good, EntityId owner)
    {
        var def = _cat.Def(good);
        if (def.Unit != GoodUnit.Piece || def.Shape == null || def.Material == null) return null;
        if (!store.Destroy(good, 1, DestroyReason.Materialised)) return null;
        return CreateWeapon(items, ids, def.Shape, def.Material, ItemQuality.Common, owner, good: good);
    }

    /// <summary>
    /// Folds an undamaged Common instance back into counted stock (created as Dematerialised) and frees the row; the id is not reused.
    /// A damaged, stained or non-Common instance is not folded: false, and the caller sells it for its value only.
    /// </summary>
    public bool Dematerialise(Store store, ItemTable items, EntityId item)
    {
        if (!items.IsLive(item)) return false;
        ref var r = ref items.Get(item);
        if (r.Good == ItemTable.NoGood || r.Quality != ItemQuality.Common || r.ConditionPermille < 1000 || r.Stains > 0) return false;
        if (!store.Create(r.Good, 1, CreateReason.Dematerialised)) return false;
        items.Free(item);
        return true;
    }

    public long ValueMites(ItemTable items, EntityId item, int wageMitesPerHour = ItemMath.WageSkilled)
        => items.ValueMites(items.IndexOf(item), ValueCrownsPerKg, wageMitesPerHour);
}
