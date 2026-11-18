using Ravensguard.Content;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Goods;

namespace Ravensguard.Sim.Inventory;

/// <summary>One carried thing as the inventory screen lists it (a copy; the item table belongs to the tick worker).</summary>
public sealed record CarriedItem(EntityId Id, string Shape, string Name, ItemQuality Quality, int ConditionPermille, long MassG, long VolumeMl, EntityId Container, EquipSlot Slot,
    long ValueMites, bool IsContainer, long CapacityMl, long UsedMl, ItemFlags Flags, int Depth, string Material = "");

/// <summary>One stack of counted goods in the pack, with the container it sits in (None: loose, in the arms).</summary>
public sealed record CarriedGood(ushort Good, string Id, string Name, long Qty, long MassG, long VolumeMl, EntityId Container, bool Piece);

/// <summary>
/// What one person carries (trade-inventory-character spec 3.2, 4.3), as data over the world's records: discrete things are rows of the economy's <see cref="ItemTable"/> owned by the
/// person (a container's contents point at it through <see cref="ItemRow.Parent"/>), counted goods sit in the person's pack <see cref="Store"/> (an entry of the economy's other stores,
/// so the mass ledger and the goods audit count them), and this class keeps which slot each worn thing is in and which container each stack of goods sits in. Coin is the ledger purse.
/// Every operation only moves things: nothing is created or destroyed here except the starting kit (a counted Seed faucet).
/// </summary>
public sealed class Carried
{
    public EntityId Owner { get; init; }
    /// <summary>Worn or held things and their slot.</summary>
    public SortedDictionary<EntityId, EquipSlot> Equip { get; } = [];
    /// <summary>The container each stack of counted goods sits in (absent: loose).</summary>
    public SortedDictionary<ushort, EntityId> GoodIn { get; } = [];
    /// <summary>The quick bar 1 to 8 (item references; None when empty).</summary>
    public EntityId[] QuickBar { get; } = new EntityId[8];

    public Carried(EntityId owner) { Owner = owner; }

    /// <summary>The person's pack (counted goods), opened on first use.</summary>
    public Store Pack(EconomyState e) => e.OpenStore(StoreKind.House, Owner, 0, 0, false);

    /// <summary>Live item rows owned by the person, ascending row.</summary>
    public IEnumerable<int> Rows(EconomyState e) { foreach (int i in e.Items.LiveRows()) if (e.Items.At(i).Owner == Owner) yield return i; }

    // ------------------------------------------------------------------ measuring

    /// <summary>Everything carried in grams: item rows (parts and contents), the pack's goods and the coin of <paramref name="purse"/>.</summary>
    public long MassG(EconomyState e, PurseId? purse = null)
    {
        long m = 0;
        foreach (int i in Rows(e)) m += e.Items.MassG(i, e.Goods);
        m += Pack(e).TotalMassG();
        if (purse is { } p && p != PurseId.None) m += Encumbrance.CoinMassG(e.Ledger.Balance(p).Mites);
        return m;
    }

    /// <summary>How deep a row sits: 1 for a thing not inside anything, 2 inside a container that is not inside anything, ...</summary>
    public static int Depth(ItemTable items, int row)
    {
        int d = 1, guard = 0;
        for (int p = items.At(row).Parent; p >= 0 && guard < 16; p = items.At(p).Parent, guard++) d++;
        return d;
    }

    /// <summary>Millilitres used inside a container row: the things whose parent it is and the goods that sit in it.</summary>
    public long UsedMl(EconomyState e, ContainerRules rules, int containerRow)
    {
        long v = 0; var id = e.Items.At(containerRow).Id;
        foreach (int i in Rows(e)) if (e.Items.At(i).Parent == containerRow) v += rules.VolumeMl(e.Items, i);
        var pack = Pack(e);
        foreach (var (g, c) in GoodIn) if (c == id) v += e.Goods.VolumeMl(g, pack.Qty(g));
        return v;
    }

    public long CapacityMl(EconomyState e, ContainerRules rules, int containerRow) => (rules.Container(e.Items.At(containerRow).Shape)?.VolumeL ?? 0) * 1000L;

    // ------------------------------------------------------------------ the rules (spec 3.2)

    /// <summary>
    /// Whether an item row may go into a container row: it must be a container, not the item itself or inside it, the item must fit through the mouth (<c>max_item_cm</c>), the
    /// volumes must fit (<c>sum(volume) &lt;= volume_l x 1000</c>) and a container may not end up deeper than 3.
    /// </summary>
    public PutRefusal CanPutItem(EconomyState e, ContainerRules rules, int itemRow, int containerRow)
    {
        var items = e.Items;
        if (items.At(itemRow).Owner != Owner || items.At(containerRow).Owner != Owner) return PutRefusal.NotOwned;
        var c = rules.Container(items.At(containerRow).Shape);
        if (c == null) return PutRefusal.NotAContainer;
        if (itemRow == containerRow) return PutRefusal.Itself;
        for (int p = containerRow, guard = 0; p >= 0 && guard < 16; p = items.At(p).Parent, guard++) if (p == itemRow) return PutRefusal.Itself;
        if (rules.LongestCm(items, itemRow) > c.MaxItemCm) return PutRefusal.TooLong;
        bool isContainer = rules.IsContainer(items.At(itemRow).Shape);
        if (isContainer && Depth(items, containerRow) + 1 > ContainerRules.MaxDepth) return PutRefusal.TooDeep;
        long used = UsedMl(e, rules, containerRow) - (items.At(itemRow).Parent == containerRow ? rules.VolumeMl(items, itemRow) : 0);
        if (used + rules.VolumeMl(items, itemRow) > c.VolumeL * 1000L) return PutRefusal.Volume;
        return PutRefusal.None;
    }

    /// <summary>Whether <paramref name="qty"/> more of a counted good fit in a container (goods volume = grams x bulk litres per kg); liquids only in a liquid container.</summary>
    public PutRefusal CanPutGood(EconomyState e, ContainerRules rules, ushort good, long totalQty, int containerRow)
    {
        var c = rules.Container(e.Items.At(containerRow).Shape);
        if (c == null) return PutRefusal.NotAContainer;
        if (e.Goods.Def(good).Unit == GoodUnit.Litre && !c.Liquid && !e.Goods.HasTag(good, "ambient")) return PutRefusal.Liquid;
        var id = e.Items.At(containerRow).Id;
        long used = UsedMl(e, rules, containerRow) - (GoodIn.TryGetValue(good, out var cur) && cur == id ? e.Goods.VolumeMl(good, Pack(e).Qty(good)) : 0);
        if (used + e.Goods.VolumeMl(good, totalQty) > c.VolumeL * 1000L) return PutRefusal.Volume;
        return PutRefusal.None;
    }

    // ------------------------------------------------------------------ moving

    /// <summary>Puts an item into a container (or takes it out with <paramref name="container"/> = None). Unequips it first. False with the reason when refused.</summary>
    public PutRefusal PutItem(EconomyState e, ContainerRules rules, EntityId item, EntityId container)
    {
        int row = e.Items.IndexOf(item); if (row < 0) return PutRefusal.NotOwned;
        if (e.Items.At(row).Owner != Owner) return PutRefusal.NotOwned;
        if (container == EntityId.None) { e.Items.At(row).Parent = -1; return PutRefusal.None; }
        int crow = e.Items.IndexOf(container); if (crow < 0) return PutRefusal.NotAContainer;
        var why = CanPutItem(e, rules, row, crow);
        if (why != PutRefusal.None) return why;
        Equip.Remove(item);
        e.Items.At(row).Parent = crow;
        return PutRefusal.None;
    }

    /// <summary>Moves the whole stack of a counted good into a container (or loose with None).</summary>
    public PutRefusal PutGood(EconomyState e, ContainerRules rules, ushort good, EntityId container)
    {
        if (container == EntityId.None) { GoodIn.Remove(good); return PutRefusal.None; }
        int crow = e.Items.IndexOf(container); if (crow < 0 || e.Items.At(crow).Owner != Owner) return PutRefusal.NotOwned;
        var why = CanPutGood(e, rules, good, Pack(e).Qty(good), crow);
        if (why != PutRefusal.None) return why;
        GoodIn[good] = container;
        return PutRefusal.None;
    }

    /// <summary>Finds a home for a stack that just grew (a purchase, a craft): where it already sits if it still fits, else the first worn container with room (ascending id), else loose.</summary>
    public void Stow(EconomyState e, ContainerRules rules, ushort good)
    {
        var pack = Pack(e); long q = pack.Qty(good);
        if (q <= 0) { GoodIn.Remove(good); return; }
        if (GoodIn.TryGetValue(good, out var cur))
        {
            int crow = e.Items.IndexOf(cur);
            if (crow >= 0 && e.Items.At(crow).Owner == Owner && CanPutGood(e, rules, good, q, crow) == PutRefusal.None) return;
            GoodIn.Remove(good);
        }
        foreach (int r in Rows(e))
        {
            if (!rules.IsContainer(e.Items.At(r).Shape) || rules.Container(e.Items.At(r).Shape)!.Slot == "horse") continue;
            if (CanPutGood(e, rules, good, q, r) == PutRefusal.None) { GoodIn[good] = e.Items.At(r).Id; return; }
        }
    }

    /// <summary>Forgets goods that are gone and things no longer owned (after a sale, a craft, spoilage).</summary>
    public void Tidy(EconomyState e)
    {
        var pack = Pack(e);
        foreach (var g in GoodIn.Keys.ToArray()) if (pack.Qty(g) <= 0 || !e.Items.IsLive(GoodIn[g]) || e.Items.Get(GoodIn[g]).Owner != Owner) GoodIn.Remove(g);
        foreach (var id in Equip.Keys.ToArray()) if (!e.Items.IsLive(id) || e.Items.Get(id).Owner != Owner) Equip.Remove(id);
        for (int i = 0; i < QuickBar.Length; i++) if (QuickBar[i] != EntityId.None && (!e.Items.IsLive(QuickBar[i]) || e.Items.Get(QuickBar[i]).Owner != Owner)) QuickBar[i] = EntityId.None;
    }

    /// <summary>Wears or wields an item: it leaves any container; what was in the same slot is unequipped (it stays carried, loose).</summary>
    public bool EquipItem(EconomyState e, ContainerRules rules, EntityId item, EquipSlot slot = EquipSlot.None)
    {
        int row = e.Items.IndexOf(item); if (row < 0 || e.Items.At(row).Owner != Owner) return false;
        if (slot == EquipSlot.None) slot = rules.SlotFor(e.Items.At(row).Shape);
        if (slot == EquipSlot.None) return false;
        bool layered = slot is EquipSlot.Body or EquipSlot.Legs or EquipSlot.Head or EquipSlot.Feet or EquipSlot.Hands; // clothing layers stack (the clothing spec decides warmth)
        if (!layered) foreach (var (k, s) in Equip.ToArray()) if (s == slot || (slot == EquipSlot.BothHands && s is EquipSlot.MainHand or EquipSlot.OffHand) || (s == EquipSlot.BothHands && slot is EquipSlot.MainHand or EquipSlot.OffHand)) Equip.Remove(k);
        e.Items.At(row).Parent = -1;
        Equip[item] = slot;
        return true;
    }

    public bool Unequip(EntityId item) => Equip.Remove(item);

    // ------------------------------------------------------------------ listing

    public List<CarriedItem> ListItems(EconomyState e, ContainerRules rules, Func<string, double> valuePerKg)
    {
        var list = new List<CarriedItem>();
        foreach (int r in Rows(e))
        {
            var row = e.Items.At(r);
            bool cont = rules.IsContainer(row.Shape);
            list.Add(new CarriedItem(row.Id, row.Shape, rules.Name(row.Shape), row.Quality, row.ConditionPermille, e.Items.MassG(r, e.Goods), rules.VolumeMl(e.Items, r),
                row.Parent >= 0 ? e.Items.At(row.Parent).Id : EntityId.None, Equip.GetValueOrDefault(row.Id), e.Items.ValueMites(r, m => SafeValue(valuePerKg, m)), cont,
                cont ? CapacityMl(e, rules, r) : 0, cont ? UsedMl(e, rules, r) : 0, row.Flags, Depth(e.Items, r), row.ComponentCount > 0 ? e.Items.Component(row.ComponentStart).Material : ""));
        }
        return list;
    }

    private static double SafeValue(Func<string, double> f, string m) { try { return f(m); } catch (KeyNotFoundException) { return 1.0; } }

    public List<CarriedGood> ListGoods(EconomyState e)
    {
        var list = new List<CarriedGood>();
        foreach (var (g, q) in Pack(e).Rows())
            list.Add(new CarriedGood(g, e.Goods.StringId(g), e.Goods.Def(g).Name, q, e.Goods.MassG(g, q), e.Goods.VolumeMl(g, q), GoodIn.GetValueOrDefault(g), e.Goods.IsPiece(g)));
        return list;
    }

    // ------------------------------------------------------------------ the kit

    /// <summary>
    /// Makes an item row the person owns: a container from its definition (one component of its material), a weapon from its shape (head and haft), a garment from its shape mass, at
    /// Common quality and full condition. Returns the id.
    /// </summary>
    public EntityId MakeItem(EconomyState e, ContentDatabase db, ContainerRules rules, ItemFactory? f, IdAllocator ids, string shape, string? material = null, ItemQuality q = ItemQuality.Common)
    {
        if (rules.Container(shape) is { } c)
            return e.Items.Add(ids, shape, q, Owner, [new ItemComponent(material ?? c.Material, Math.Max(1, c.EmptyMassG), 1000)]);
        if (db.TryGet<WeaponShapeDef>(shape, out var w) && f != null)
            return e.Items.Add(ids, shape, q, Owner, f.WeaponParts(w, material ?? Characters.HeroRecord.DefaultMaterial(shape)));
        int mass = db.TryGet<ArmourShapeDef>(shape, out var a) && a.MassG > 0 ? a.MassG : 1500;
        string mat = material ?? (a?.DefaultMaterial ?? Characters.HeroRecord.DefaultMaterial(shape));
        return e.Items.Add(ids, shape, q, Owner, [new ItemComponent(mat, mass, 1000)]);
    }
}
