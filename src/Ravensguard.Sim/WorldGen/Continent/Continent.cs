using System.Security.Cryptography;

namespace Ravensguard.Sim.WorldGen;

public enum Biome : byte { Sea, Lake, Tundra, Taiga, TemperateForest, Grassland, Steppe, Desert, Marsh, Mountain, RiverValley }
public enum Strata : byte { Sedimentary, Granite, Volcanic, Metamorphic }
public enum MineralType : byte { Iron, Copper, Tin, Silver, Gold, Coal, Peat, Salt, Stone }

/// <summary>A mineral vein, kept apart from the strata layer. Tonnage in metric tonnes.</summary>
public readonly record struct MineralVein(MineralType Type, int Cell, int Tonnage);
public sealed record Plate(int Id, int SeedX, int SeedY, bool Continental, int VelX, int VelY);
/// <summary>River polyline of cell indices, upstream to downstream (last cell may be the sea cell at the mouth).</summary>
public sealed record River(int[] Cells, int MaxFlow);
public sealed record Culture(int Id, string Name, int SeedCell);
public sealed record Realm(int Id, string Name, int SeedCell, int CultureId);

/// <summary>Stage 1 output: the continent on a 256x256 grid of 2 km cells (plan §1, §1A). Index = y * Size + x; y grows southward.</summary>
public sealed class Continent
{
    public const int Size = ContinentSettings.Size;
    public const int Cells = Size * Size;
    public const byte None = 255;

    public ContinentSettings Settings { get; }
    public int SeaLevelRaw => Settings.SeaLevelRaw;

    /// <summary>Q16.16 raw elevation, 0..65535. Sea level is <see cref="SeaLevelRaw"/>.</summary>
    public int[] Elevation { get; } = new int[Cells];
    public byte[] PlateId { get; } = new byte[Cells];
    /// <summary>Mean annual temperature in tenths of a degree C.</summary>
    public short[] Temperature { get; } = new short[Cells];
    /// <summary>Annual rainfall, mm.</summary>
    public short[] Rainfall { get; } = new short[Cells];
    /// <summary>Accumulated runoff (rain-weighted upstream cell count) after priority-flood and D8 routing.</summary>
    public int[] RiverFlow { get; } = new int[Cells];
    /// <summary>D8 receiver cell index (every land cell drains to the sea), -1 for ocean cells and cells where water leaves the map.</summary>
    public int[] FlowTo { get; } = new int[Cells];
    public bool[] Lake { get; } = new bool[Cells];
    public bool[] Ocean { get; } = new bool[Cells];
    public Biome[] Biomes { get; } = new Biome[Cells];
    /// <summary>0..100.</summary>
    public byte[] Fertility { get; } = new byte[Cells];
    /// <summary>0..100, 100 = freely drained.</summary>
    public byte[] Drainage { get; } = new byte[Cells];
    /// <summary>0..100 danger (savagery overlay).</summary>
    public byte[] Savagery { get; } = new byte[Cells];
    /// <summary>Cells to the nearest ocean for land (>=1), negative for ocean (distance to nearest land).</summary>
    public short[] CoastDistance { get; } = new short[Cells];
    public Strata[] Strata { get; } = new Strata[Cells];
    public byte[] CultureId { get; } = new byte[Cells];
    public byte[] RealmId { get; } = new byte[Cells];

    public List<Plate> Plates { get; } = [];
    public List<River> Rivers { get; } = [];
    public List<MineralVein> Veins { get; } = [];
    public List<Culture> Cultures { get; } = [];
    public List<Realm> Realms { get; } = [];

    public Continent(ContinentSettings settings) { Settings = settings; }

    public static int Index(int x, int y) => y * Size + x;
    public bool IsLand(int i) => !Ocean[i];

    public double LandFraction()
    {
        int n = 0;
        for (int i = 0; i < Cells; i++) if (!Ocean[i]) n++;
        return n / (double)Cells;
    }

    /// <summary>SHA-256 over every array and list, for determinism tests. Integers are hashed little-endian.</summary>
    public string ComputeHash()
    {
        using var h = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var b4 = new byte[4];
        void I(int v) { System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(b4, v); h.AppendData(b4); }
        void Ints(int[] a) { foreach (var v in a) I(v); }
        void Shorts(short[] a) { foreach (var v in a) I(v); }
        void Bytes(byte[] a) => h.AppendData(a);
        void Bools(bool[] a) { var t = new byte[a.Length]; for (int i = 0; i < a.Length; i++) t[i] = a[i] ? (byte)1 : (byte)0; h.AppendData(t); }

        Ints(Elevation); Bytes(PlateId); Shorts(Temperature); Shorts(Rainfall); Ints(RiverFlow); Ints(FlowTo);
        Bools(Lake); Bools(Ocean);
        foreach (var b in Biomes) h.AppendData([(byte)b]);
        Bytes(Fertility); Bytes(Drainage); Bytes(Savagery); Shorts(CoastDistance);
        foreach (var s in Strata) h.AppendData([(byte)s]);
        Bytes(CultureId); Bytes(RealmId);
        foreach (var p in Plates) { I(p.Id); I(p.SeedX); I(p.SeedY); I(p.Continental ? 1 : 0); I(p.VelX); I(p.VelY); }
        foreach (var r in Rivers) { I(r.Cells.Length); I(r.MaxFlow); foreach (var c in r.Cells) I(c); }
        foreach (var v in Veins) { I((int)v.Type); I(v.Cell); I(v.Tonnage); }
        foreach (var c in Cultures) { I(c.Id); I(c.SeedCell); h.AppendData(System.Text.Encoding.UTF8.GetBytes(c.Name)); }
        foreach (var r in Realms) { I(r.Id); I(r.SeedCell); I(r.CultureId); h.AppendData(System.Text.Encoding.UTF8.GetBytes(r.Name)); }
        return Convert.ToHexString(h.GetHashAndReset());
    }
}
