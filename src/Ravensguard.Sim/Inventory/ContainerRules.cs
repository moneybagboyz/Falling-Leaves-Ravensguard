using Ravensguard.Content;
using Ravensguard.Sim.Goods;

namespace Ravensguard.Sim.Inventory;

/// <summary>Where a carried thing sits on the body (trade-inventory-character spec 4.3; clothing layers are the clothing spec's).</summary>
public enum EquipSlot : byte { None, MainHand, OffHand, Head, Body, Legs, Feet, Hands, Cloak, Belt, Shoulder, Back, BothHands }

/// <summary>Why a put or an equip was refused.</summary>
public enum PutRefusal : byte { None, NotAContainer, Volume, TooLong, TooDeep, Liquid, Itself, NotOwned, Hands }

/// <summary>
/// What the inventory needs to know about item shapes and containers (spec 3.2): the container definitions of <c>items/containers.jsonc</c>, the volume and longest dimension of
/// an item row, and the slot it is worn in. Built once from the content; pure lookups.
/// </summary>
public sealed class ContainerRules
{
    public const int MaxDepth = 3;
    private readonly Dictionary<string, ContainerDef> _defs = new(StringComparer.Ordinal);
    private readonly ContentDatabase? _db;
    public GoodCatalog Goods { get; }

    public ContainerRules(ContentDatabase db, GoodCatalog goods)
    {
        _db = db; Goods = goods;
        foreach (var d in db.All<ContainerDef>().OrderBy(d => d.Id, StringComparer.Ordinal)) _defs[d.Id] = d;
        foreach (var d in Defaults()) _defs.TryAdd(d.Id, d);
    }

    /// <summary>The spec's table, used for any container the content does not define.</summary>
    public static IEnumerable<ContainerDef> Defaults() =>
    [
        new() { Id = "belt_pouch", Name = "Belt pouch", VolumeL = 2, EmptyMassG = 120, MaxItemCm = 20, Slot = "belt" },
        new() { Id = "satchel", Name = "Satchel", VolumeL = 8, EmptyMassG = 600, MaxItemCm = 45, Slot = "shoulder" },
        new() { Id = "rucksack", Name = "Rucksack", VolumeL = 30, EmptyMassG = 1800, MaxItemCm = 70, Slot = "back" },
        new() { Id = "sack", Name = "Sack", VolumeL = 50, EmptyMassG = 400, MaxItemCm = 90, Slot = "both_hands", Hands = 2, Material = "linen" },
        new() { Id = "basket", Name = "Basket", VolumeL = 20, EmptyMassG = 700, MaxItemCm = 50, Slot = "hand", Hands = 1, Soft = false, Material = "linen" },
    ];

    public IReadOnlyDictionary<string, ContainerDef> Defs => _defs;
    public ContainerDef? Container(string shape) => _defs.GetValueOrDefault(shape);
    public bool IsContainer(string shape) => _defs.ContainsKey(shape);

    /// <summary>Outer volume of an item in millilitres: a container its whole volume (it is carried full or not), a weapon its head and haft, a garment folded (2 ml a gram), anything else its mass.</summary>
    public long VolumeMl(ItemTable items, int row)
    {
        var r = items.At(row);
        if (_defs.TryGetValue(r.Shape, out var c)) return c.VolumeL * 1000L;
        if (_db != null && _db.TryGet<WeaponShapeDef>(r.Shape, out var w)) return (long)Math.Ceiling(w.HeadVolumeCm3 + w.HaftVolumeCm3);
        long m = items.MassG(row, Goods);
        if (_db != null && _db.TryGet<ArmourShapeDef>(r.Shape, out _)) return Math.Max(100, m * 2);
        return Math.Max(50, m);
    }

    /// <summary>Longest dimension in cm (what decides whether it fits through a container's mouth): a container its own size class, a weapon by its build, else 30.</summary>
    public int LongestCm(ItemTable items, int row)
    {
        var r = items.At(row);
        if (_defs.TryGetValue(r.Shape, out var c)) return Math.Max(10, (int)Math.Round(Math.Cbrt(c.VolumeL * 1000.0)) + 5);
        if (_db != null && _db.TryGet<WeaponShapeDef>(r.Shape, out var w))
            return r.Shape switch { "dagger" or "knife" => 35, "spear" or "pike" or "halberd" or "poleaxe" => 220, "staff" or "quarterstaff" or "longbow" => 180, "bow" or "shortbow" => 130, _ => w.TwoHanded ? 120 : w.HaftVolumeCm3 > 0 ? 80 : 95 };
        return 30;
    }

    /// <summary>The slot an item is worn in: a weapon in the main hand (both for two-handed), a container where it is carried, a garment by its main coverage.</summary>
    public EquipSlot SlotFor(string shape)
    {
        if (_defs.TryGetValue(shape, out var c))
            return c.Slot switch { "belt" => EquipSlot.Belt, "back" => EquipSlot.Back, "hand" => EquipSlot.OffHand, "both_hands" => EquipSlot.BothHands, "horse" => EquipSlot.None, _ => EquipSlot.Shoulder };
        if (_db != null && _db.TryGet<WeaponShapeDef>(shape, out var w)) return w.TwoHanded ? EquipSlot.BothHands : EquipSlot.MainHand;
        if (_db != null && _db.TryGet<ArmourShapeDef>(shape, out var a))
        {
            string main = a.Coverage.Count == 0 ? "" : a.Coverage.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key, StringComparer.Ordinal).First().Key;
            if (a.Layer == ArmourLayer.Cover && main is not ("head" or "neck" or "face")) return EquipSlot.Cloak;   // a hood is a cover layer worn on the head
            return main switch
            {
                "head" or "neck" or "face" => EquipSlot.Head,
                "left_foot" or "right_foot" or "foot" or "feet" => EquipSlot.Feet,
                "left_hand" or "right_hand" or "hand" or "hands" => EquipSlot.Hands,
                var s when s.Contains("leg") || s.Contains("thigh") || s.Contains("shin") => EquipSlot.Legs,
                _ => EquipSlot.Body,
            };
        }
        return EquipSlot.None;
    }

    /// <summary>The name to show for an item shape.</summary>
    public string Name(string shape)
    {
        if (_defs.TryGetValue(shape, out var c) && c.Name.Length > 0) return c.Name;
        if (_db != null && _db.TryGet<WeaponShapeDef>(shape, out var w)) return w.Name;
        if (_db != null && _db.TryGet<ArmourShapeDef>(shape, out var a)) return a.Name;
        return shape.Replace('_', ' ');
    }
}
