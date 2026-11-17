using Ravensguard.Sim.Save;

namespace Ravensguard.Sim.WorldGen.Chunks;

/// <summary>One decoded level of a chunk: a dense 32 x 32 array of tile codes (0 = nothing) and its props.</summary>
public sealed class ChunkLevel
{
    public int Z { get; }
    public int[] Tiles { get; } = new int[ChunkContent.Size * ChunkContent.Size];
    public Dictionary<ulong, PropRecord> Props { get; } = [];

    public ChunkLevel(int z) { Z = z; }
    public bool IsEmpty => Props.Count == 0 && Array.TrueForAll(Tiles, t => t == 0);
}

/// <summary>A chunk in memory: its levels decoded from snapshot + deltas (or straight from the generator).</summary>
public sealed class LoadedChunk
{
    public int X { get; }
    public int Y { get; }
    public SortedDictionary<int, ChunkLevel> Levels { get; } = [];
    /// <summary>Bumps when a tile or prop changes after loading, so a view knows to redraw.</summary>
    public int Version { get; internal set; }

    public LoadedChunk(int x, int y) { X = x; Y = y; }

    public ChunkLevel Level(int z)
    {
        if (!Levels.TryGetValue(z, out var l)) Levels[z] = l = new ChunkLevel(z);
        return l;
    }

    public int TileAt(int localX, int localY, int z)
        => Levels.TryGetValue(z, out var l) ? l.Tiles[localY * ChunkContent.Size + localX] : 0;

    /// <summary>Decodes one level of stored content.</summary>
    public static void Decode(LoadedChunk into, int z, ChunkContent c)
    {
        var l = into.Level(z);
        foreach (var (i, code) in c.Tiles) if ((uint)i < (uint)l.Tiles.Length) l.Tiles[i] = code;
        foreach (var (id, rec) in c.Objects) if (rec.Data.Length >= PropRecord.Bytes) l.Props[id] = PropRecord.Decode(rec.Data);
    }

    /// <summary>Encodes a level back to storable content (sparse: zero tiles are left out).</summary>
    public static ChunkContent Encode(ChunkLevel l)
    {
        var c = new ChunkContent();
        for (int i = 0; i < l.Tiles.Length; i++) if (l.Tiles[i] != 0) c.Tiles[i] = l.Tiles[i];
        foreach (var (id, rec) in l.Props) c.Objects[id] = new ObjectRecord { Id = id, Data = rec.Encode() };
        return c;
    }
}
