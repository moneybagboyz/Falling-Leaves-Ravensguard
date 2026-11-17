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

/// <summary>Stable numbering of the building pieces (sorted by id), so a tile code names a piece without carrying a string.</summary>
public sealed class PiecePalette
{
    private readonly string[] _ids;
    private readonly Dictionary<string, int> _index;

    public PiecePalette(PieceCatalog pieces)
    {
        _ids = pieces.All.Select(p => p.Id).OrderBy(i => i, StringComparer.Ordinal).ToArray();
        if (_ids.Length > TileCode.MaxPieces) throw new InvalidOperationException($"more than {TileCode.MaxPieces} build pieces");
        _index = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int i = 0; i < _ids.Length; i++) _index[_ids[i]] = i + 1;
    }

    public int Count => _ids.Length;
    /// <summary>Palette number of a piece id, 0 for none.</summary>
    public int Of(string? id) => id != null && _index.TryGetValue(id, out var i) ? i : 0;
    public string? Id(int number) => number <= 0 || number > _ids.Length ? null : _ids[number - 1];
    public IReadOnlyList<string> Ids => _ids;
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
