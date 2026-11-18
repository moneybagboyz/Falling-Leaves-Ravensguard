using Ravensguard.Content;
using Ravensguard.Sim.Building;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Live;
using Ravensguard.Sim.Save;

namespace Ravensguard.Sim.WorldGen.Chunks;

/// <summary>
/// The integer stored per cell in a <see cref="ChunkContent"/> (plan 2.2): ground kind (6 bits), ground variant (4 bits),
/// structure piece (9 bits, palette index + 1) and fixture piece (9 bits, palette index + 1). Zero means "nothing here".
/// </summary>
public static class TileCode
{
    public const int GroundBits = 6, VariantBits = 4, PieceBits = 9;
    public const int MaxPieces = (1 << PieceBits) - 1;

    public static int Make(GroundKind ground, int variant, int structure, int fixture)
        => (int)ground | ((variant & 15) << GroundBits) | (structure << (GroundBits + VariantBits)) | (fixture << (GroundBits + VariantBits + PieceBits));

    public static GroundKind Ground(int code) => (GroundKind)(code & ((1 << GroundBits) - 1));
    public static int Variant(int code) => (code >> GroundBits) & 15;
    public static int Structure(int code) => (code >> (GroundBits + VariantBits)) & MaxPieces;
    public static int Fixture(int code) => (code >> (GroundBits + VariantBits + PieceBits)) & MaxPieces;

    public static int WithStructure(int code, int structure) => Make(Ground(code), Variant(code), structure, Fixture(code));
    public static int WithFixture(int code, int fixture) => Make(Ground(code), Variant(code), Structure(code), fixture);
    public static int WithGround(int code, GroundKind g, int variant) => Make(g, variant, Structure(code), Fixture(code));

    /// <summary>Can a person stand on this ground (the only blocker is deep water).</summary>
    public static bool Walkable(GroundKind g) => g != GroundKind.DeepWater && g != GroundKind.None;
}

/// <summary>
/// Numbering of the building pieces, so a tile code names a piece without carrying a string. Numbers must not move when content grows (Phase 3 cleanup: onboarding's three
/// yard pieces renumbered every piece sorted after them, which changed the golden chunk hashes and would have scrambled the furniture of saved chunks):
/// the pieces of <see cref="LegacyIds"/> keep the numbers the old sorted palette gave them (1..71, a missing one leaves its number empty), and every other piece is numbered after
/// them in sorted order. That tail can still shift when a mod adds pieces, so a chunk section stores the palette it was written with (<see cref="ChunkStore.Palette"/>) and
/// is remapped to the running palette when a streamer adopts it, and chunk content hashes are taken over piece ids (<see cref="HashKey"/>), never over the tail's numbers.
/// </summary>
public sealed class PiecePalette
{
    /// <summary>The base game's pieces before the palette was frozen, sorted by id (the old palette's numbers 1..71). Append-only: never edit or reorder.</summary>
    public static readonly string[] LegacyIds = ["altar", "anvil", "barrel", "bed", "bell", "brake_wheel", "brewing_vat", "bunk", "cask_rack", "charcoal_clamp", "chest", "cooper_bench", "door_barn", "door_cell", "door_wood", "dovecote_nests", "fence_wood", "floor_flagstone", "floor_packed_earth", "floor_plank", "floor_wall_walk", "font", "forge", "gate_iron", "gate_wood", "grain_bin", "ground_dirt", "hay_stack", "hearth", "hearth_central", "hearth_chimney", "high_seat", "hurdle", "kiln", "ladder", "loom", "malting_floor", "market_stall", "millstone", "oven", "palisade", "pew", "post_timber", "potters_wheel", "sawpit", "shelf", "shop_counter", "stairs_down", "stairs_stone_down", "stairs_stone_up", "stairs_up", "stall", "straw_pallet", "table", "tanning_pit", "tanning_rack", "toll_counter", "tomb", "trapdoor", "wall_brick", "wall_crenellated", "wall_stone", "wall_timber", "wall_wattle", "waterwheel", "weapon_rack", "well", "window_arrow_slit", "window_open", "window_shuttered", "workbench"];
    private static readonly Dictionary<string, int> LegacyIndex = LegacyIds.Select((id, i) => (id, i)).ToDictionary(t => t.id, t => t.i + 1, StringComparer.Ordinal);

    private readonly string?[] _ids;
    private readonly Dictionary<string, int> _index;

    public PiecePalette(PieceCatalog pieces) : this(pieces.All.Select(p => p.Id)) { }

    public PiecePalette(IEnumerable<string> pieceIds)
    {
        var all = pieceIds.Distinct(StringComparer.Ordinal).ToHashSet(StringComparer.Ordinal);
        var tail = all.Where(id => !LegacyIndex.ContainsKey(id)).OrderBy(i => i, StringComparer.Ordinal);
        _ids = [.. LegacyIds.Select(id => all.Contains(id) ? id : null), .. tail];
        if (_ids.Length > TileCode.MaxPieces) throw new InvalidOperationException($"more than {TileCode.MaxPieces} build pieces");
        _index = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int i = 0; i < _ids.Length; i++) if (_ids[i] is { } id) _index[id] = i + 1;
    }

    /// <summary>Highest number in use.</summary>
    public int Count => _ids.Length;
    /// <summary>Palette number of a piece id, 0 for none.</summary>
    public int Of(string? id) => id != null && _index.TryGetValue(id, out var i) ? i : 0;
    public string? Id(int number) => number <= 0 || number > _ids.Length ? null : _ids[number - 1];
    /// <summary>The id of each number from 1 ("" where a legacy piece is missing): what a chunk section stores to name its numbers.</summary>
    public IReadOnlyList<string> Ids => [.. _ids.Select(i => i ?? "")];

    /// <summary>
    /// What a chunk content hash takes for a piece number: the legacy number for a legacy piece (so the hashes of worlds made of legacy pieces are those of the old palette), else a key
    /// of the id itself (FNV-1a, above the 9-bit range), so adding an unrelated piece changes no world's hash.
    /// </summary>
    public long HashKey(int number)
    {
        if (number <= 0) return 0;
        var id = Id(number);
        if (id == null) return number;
        if (LegacyIndex.TryGetValue(id, out int legacy)) return legacy;
        ulong h = 14695981039346656037UL;
        foreach (char c in id) { h ^= c; h *= 1099511628211UL; }
        return (long)(h | (1UL << 62)) & long.MaxValue;
    }

    /// <summary>A tile code for hashing: itself when both its pieces are legacy (the old code), else ground and variant with the two pieces' <see cref="HashKey"/>s mixed in.</summary>
    public long HashCodeOf(int code)
    {
        long s = HashKey(TileCode.Structure(code)), f = HashKey(TileCode.Fixture(code));
        if (s < (1 << TileCode.PieceBits) && f < (1 << TileCode.PieceBits)) return TileCode.Make(TileCode.Ground(code), TileCode.Variant(code), (int)s, (int)f);
        return (code & ((1 << (TileCode.GroundBits + TileCode.VariantBits)) - 1)) ^ (long)DetRng.Mix((ulong)s * 31 + (ulong)f);
    }

    /// <summary>
    /// The number map from a palette a section was written with (<paramref name="savedIds"/>: id of number i+1; empty or null means the legacy palette of saves before v4) to this one:
    /// <c>map[old] = new</c>, 0 for a piece this content no longer has. Null when the two number every piece alike (nothing to remap).
    /// </summary>
    public int[]? MapFrom(IReadOnlyList<string>? savedIds)
    {
        IReadOnlyList<string> from = savedIds is { Count: > 0 } ? savedIds : LegacyIds;
        var map = new int[Math.Max(from.Count, _ids.Length) + 1];
        bool same = true;
        for (int i = 0; i < map.Length - 1; i++)
        {
            string? id = i < from.Count && from[i].Length > 0 ? from[i] : null;
            int to = id == null ? 0 : Of(id);
            map[i + 1] = id == null ? (i < _ids.Length ? i + 1 : 0) : to;   // a number the saved palette left empty keeps its value (it names nothing either way)
            if (map[i + 1] != i + 1) same = false;
        }
        return same ? null : map;
    }

    /// <summary>A tile code renumbered with a <see cref="MapFrom"/> map.</summary>
    public static int Remap(int code, int[] map)
    {
        int s = TileCode.Structure(code), f = TileCode.Fixture(code);
        int ns = s < map.Length ? map[s] : 0, nf = f < map.Length ? map[f] : 0;
        return ns == s && nf == f ? code : TileCode.Make(TileCode.Ground(code), TileCode.Variant(code), ns, nf);
    }
}

/// <summary>What a prop object is. Trees, rocks and the like are objects in the chunk (keyed by stable ids, so a felled tree is a RemoveObject delta).</summary>
public enum PropKind : byte
{
    Tree, Bush, Rocks, Boulder, Hedge, Hurdle, Well, Stall, Brazier, Sign, Hay, Haystack, Sheaves, Scarecrow, Barrel, Crate, Cart,
    Woodpile, Stump, Signpost, SiteBarrow, SiteCave, SiteMine, SiteRuin, SiteBattlefield, SiteHoard,
    // settlement dressing (appended, so stored records keep their numbers)
    Headstone, GraveMound, FruitTree, Trough, LowWall, Rail, Sacks, Bench, Gate,
    Lychgate, FieldWall,
}

/// <summary>How a prop looks and behaves. Sprite is the preferred baked sprite id; the view falls back when the atlas lacks it.</summary>
public readonly record struct PropInfo(string Sprite, bool Wide, bool Blocks, int LightRadius, int LightIntensity);

/// <summary>A prop as stored in <see cref="ObjectRecord.Data"/>: kind, variant, local cell and an extra number (site index for site markers).</summary>
public readonly record struct PropRecord(PropKind Kind, byte Variant, byte X, byte Y, ushort Extra = 0)
{
    public const int Bytes = 6;

    public byte[] Encode() => [(byte)Kind, Variant, X, Y, (byte)(Extra & 255), (byte)(Extra >> 8)];

    public static PropRecord Decode(byte[] d) => new((PropKind)d[0], d[1], d[2], d[3], (ushort)(d[4] | (d[5] << 8)));

    public PropInfo Info => PropTable.Describe(Kind, Variant);

    /// <summary>Stable object id of this prop in a chunk: a hash of the chunk, the level, the cell and the kind (one prop per cell and kind).</summary>
    public ulong Id(int chunkX, int chunkY, int z)
    {
        ulong h = DetRng.Mix(0x50524F50UL ^ ((ulong)(uint)chunkX << 32 | (uint)chunkY));
        h = DetRng.Mix(h ^ ((ulong)(z + 8) << 24 | (ulong)X << 16 | (ulong)Y << 8 | (byte)Kind));
        return h;
    }
}

public static class PropTable
{
    public static PropInfo Describe(PropKind kind, int variant) => kind switch
    {
        PropKind.Tree => new($"object.tree.{variant % 3}", false, true, 0, 0),
        PropKind.Bush => new("object.bush", false, true, 0, 0),
        PropKind.Rocks => new("object.rocks", false, false, 0, 0),
        PropKind.Boulder => new("object.boulder", false, true, 0, 0),
        PropKind.Hedge => new("object.hedge", false, true, 0, 0),
        PropKind.Hurdle => new("object.hurdle", false, true, 0, 0),
        PropKind.Well => new("object.well", false, true, 0, 0),
        PropKind.Stall => new((variant & 7) switch { 0 => "object.stall_red", 1 => "object.stall_blue", 2 => "object.stall_green", 3 => "object.stall_yellow", 4 => "object.stall_brown", _ => "object.stall_plain" }, true, true, 0, 0),
        PropKind.Brazier => new("object.brazier_lit", false, true, 7, 235),
        PropKind.Sign => new(variant == 1 ? "object.sign_inn" : "object.sign", false, true, 0, 0),
        PropKind.Hay => new("object.hay", false, true, 0, 0),
        PropKind.Haystack => new("object.haystack", false, true, 0, 0),
        PropKind.Sheaves => new("object.sheaves", false, false, 0, 0),
        PropKind.Scarecrow => new("object.scarecrow", false, true, 0, 0),
        PropKind.Barrel => new("object.barrel", false, true, 0, 0),
        PropKind.Crate => new("object.crate", false, true, 0, 0),
        PropKind.Cart => new((variant & 3) switch { 1 => "object.cart_hay", 2 => "object.cart_barrels", 3 => "object.cart_goods", _ => "object.cart" }, true, true, 0, 0),
        PropKind.Woodpile => new("object.woodpile", false, true, 0, 0),
        PropKind.Stump => new($"object.stump.{variant & 1}", false, false, 0, 0),
        PropKind.Headstone => new((variant % 3) switch { 0 => "object.headstone_cross", 1 => "object.headstone_round", _ => "object.headstone_slab" }, false, true, 0, 0),
        PropKind.GraveMound => new("object.grave_mound", false, false, 0, 0),
        PropKind.FruitTree => new("object.tree.apple.summer", false, true, 0, 0),
        PropKind.Trough => new((variant & 1) == 0 ? "object.trough_water" : "object.trough_feed", false, true, 0, 0),
        PropKind.LowWall => new("object.wall_low", false, true, 0, 0),
        PropKind.Rail => new("object.rail", false, true, 0, 0),
        PropKind.Sacks => new((variant & 1) == 0 ? "object.sack_pile" : "object.sacks", false, true, 0, 0),
        PropKind.Bench => new("object.bench", false, false, 0, 0),
        PropKind.Gate => new("object.gate_open", false, false, 0, 0),
        PropKind.Lychgate => new("object.lychgate", false, false, 0, 0),
        PropKind.FieldWall => new("object.wall_field", false, true, 0, 0),
        PropKind.Signpost => new("object.signpost", false, true, 0, 0),
        PropKind.SiteBarrow => new("object.barrow", false, false, 0, 0),
        PropKind.SiteCave => new("object.boulder", false, false, 0, 0),
        PropKind.SiteMine => new("object.rocks", false, false, 0, 0),
        PropKind.SiteRuin => new($"object.rubble.{variant & 1}", false, false, 0, 0),
        PropKind.SiteBattlefield => new("object.grave_mound", false, false, 0, 0),
        PropKind.SiteHoard => new("object.rocks", false, false, 0, 0),
        _ => new("object.rocks", false, false, 0, 0),
    };
}
