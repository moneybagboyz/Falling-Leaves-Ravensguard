using System.Diagnostics;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Save;

namespace Ravensguard.Sim.WorldGen.Chunks;

public sealed class ChunkStreamerSettings
{
    /// <summary>Chunks within this Chebyshev distance of the hero's chunk are kept loaded (3 = a 7 x 7 block, 224 m across).</summary>
    public int LoadRadius { get; set; } = 3;
    /// <summary>Chunks beyond this distance are unloaded (one more than the load radius, so crossing a seam does not thrash).</summary>
    public int KeepRadius { get; set; } = 4;
    /// <summary>Chunks within this distance are always loaded before <see cref="ChunkStreamer.Update"/> returns, whatever the budget: the ground the hero can reach in a tick.</summary>
    public int SyncRadius { get; set; } = 1;
    /// <summary>Milliseconds of generation per Update call beyond the sync ring (plan 2.11: 4 ms).</summary>
    public double BudgetMs { get; set; } = 4.0;
}

/// <summary>
/// Keeps the chunks around the hero loaded (plan 2.2, 2.3, 2.11). A chunk is generated on its first visit and snapshotted into the
/// <see cref="ChunkStore"/>; every later visit rebuilds it from snapshot plus deltas, so a changed tile survives unloading. Generation is
/// time-sliced by <see cref="Update"/>; <see cref="LoadSync"/> and <see cref="LoadAround"/> are the synchronous path for tests and start-up.
/// </summary>
public sealed class ChunkStreamer
{
    private readonly Dictionary<(int X, int Y), LoadedChunk> _loaded = [];
    private readonly (int Dx, int Dy)[] _order;
    private readonly Stopwatch _sw = new();
    private double _avgLoadMs = 1.0;

    /// <summary>Chunks per region side (32 km / 32 m); chunk coordinates outside 0..RegionChunks-1 are off the region.</summary>
    public const int RegionChunks = 1000;
    public static bool InRegion(int cx, int cy) => (uint)cx < RegionChunks && (uint)cy < RegionChunks;

    public ChunkGenerator Generator { get; }
    public ChunkStore Store { get; }
    public ChunkStreamerSettings Settings { get; }
    /// <summary>Bumps whenever a chunk is loaded or unloaded.</summary>
    public int Revision { get; private set; }
    public int LoadedCount => _loaded.Count;
    public IEnumerable<LoadedChunk> Loaded => _loaded.Values;

    // statistics
    public long GeneratedCount { get; private set; }
    public long ReloadedCount { get; private set; }
    public long UnloadedCount { get; private set; }
    /// <summary>Snapshots found whose generator version is older than the code's: kept as stored (a visited place never changes under the player).</summary>
    public long StaleSnapshots { get; private set; }
    public double GenerateMsTotal { get; private set; }
    public double LastLoadMs { get; private set; }
    public double MaxLoadMs { get; private set; }

    public ChunkStreamer(ChunkGenerator generator, ChunkStore? store = null, ChunkStreamerSettings? settings = null)
    {
        Generator = generator; Store = store ?? new ChunkStore(); Settings = settings ?? new ChunkStreamerSettings();
        var o = new List<(int, int)>();
        int r = Settings.LoadRadius;
        for (int dy = -r; dy <= r; dy++) for (int dx = -r; dx <= r; dx++) o.Add((dx, dy));
        o.Sort((a, b) =>
        {
            int d = (a.Item1 * a.Item1 + a.Item2 * a.Item2).CompareTo(b.Item1 * b.Item1 + b.Item2 * b.Item2);
            return d != 0 ? d : a.Item2 != b.Item2 ? a.Item2.CompareTo(b.Item2) : a.Item1.CompareTo(b.Item1);
        });
        _order = [.. o];
    }

    public bool IsLoaded(int cx, int cy) => _loaded.ContainsKey((cx, cy));
    public LoadedChunk? TryGet(int cx, int cy) => _loaded.TryGetValue((cx, cy), out var c) ? c : null;

    /// <summary>Loads one chunk now (no-op when loaded) and returns it.</summary>
    public LoadedChunk LoadSync(int cx, int cy)
    {
        if (_loaded.TryGetValue((cx, cy), out var have)) return have;
        // Off the region there is nothing to generate or remember: an empty chunk (closed cells) is "loaded" so the hero never waits on it, and it is never stored.
        if (!InRegion(cx, cy)) { var empty = new LoadedChunk(cx, cy); _loaded[(cx, cy)] = empty; Revision++; return empty; }
        _sw.Restart();
        var c = new LoadedChunk(cx, cy);
        if (Store.Contains(new ChunkKey(cx, cy, 0)))
        {
            for (int z = ChunkKey.MinZ; z <= ChunkKey.MaxZ; z++)
            {
                var key = new ChunkKey(cx, cy, z);
                if (!Store.Contains(key)) continue;
                var content = Store.Rebuild(key);
                if (content != null) LoadedChunk.Decode(c, z, content);
                if (z == 0 && Store.GetSnapshot(key) is { } snap && snap.GeneratorVersion != ChunkGenerator.Version) StaleSnapshots++;
            }
            ReloadedCount++;
        }
        else
        {
            var gen = Generator.GenerateLevels(cx, cy);
            foreach (var (z, content) in gen.Levels)
            {
                Store.RecordFirstVisit(new ChunkKey(cx, cy, z), ChunkGenerator.Version, gen.ContentHash, content);
                LoadedChunk.Decode(c, z, content);
            }
            GeneratedCount++;
        }
        double ms = _sw.Elapsed.TotalMilliseconds;
        LastLoadMs = ms; MaxLoadMs = Math.Max(MaxLoadMs, ms); GenerateMsTotal += ms; _avgLoadMs = _avgLoadMs * 0.8 + ms * 0.2;
        _loaded[(cx, cy)] = c;
        Revision++;
        return c;
    }

    /// <summary>Loads every chunk within the radius now.</summary>
    public void LoadAround(int cx, int cy, int radius)
    {
        for (int dy = -radius; dy <= radius; dy++) for (int dx = -radius; dx <= radius; dx++) LoadSync(cx + dx, cy + dy);
    }

    /// <summary>
    /// One frame of streaming around the hero's chunk: the sync ring is loaded regardless; further chunks nearest first until the budget
    /// (default 4 ms) is spent, at least one per call while any are missing; chunks past the keep radius unload. Returns how many were loaded.
    /// </summary>
    public int Update(int heroChunkX, int heroChunkY, double? budgetMs = null)
    {
        int loaded = 0;
        double budget = budgetMs ?? Settings.BudgetMs;
        int sync = Settings.SyncRadius;
        foreach (var (dx, dy) in _order)
        {
            if (Math.Max(Math.Abs(dx), Math.Abs(dy)) > sync) break;
            if (!_loaded.ContainsKey((heroChunkX + dx, heroChunkY + dy))) { LoadSync(heroChunkX + dx, heroChunkY + dy); loaded++; }
        }
        var t0 = Stopwatch.GetTimestamp();
        int extra = 0;
        foreach (var (dx, dy) in _order)
        {
            if (Math.Max(Math.Abs(dx), Math.Abs(dy)) <= sync) continue;
            if (_loaded.ContainsKey((heroChunkX + dx, heroChunkY + dy))) continue;
            // do not start a chunk that is expected to run past the budget (the average of the loads so far), except the first one beyond the sync ring
            if (extra > 0 && Stopwatch.GetElapsedTime(t0).TotalMilliseconds + _avgLoadMs > budget) break;
            LoadSync(heroChunkX + dx, heroChunkY + dy); loaded++; extra++;
            if (Stopwatch.GetElapsedTime(t0).TotalMilliseconds >= budget) break;
        }
        int keep = Settings.KeepRadius;
        List<(int, int)>? gone = null;
        foreach (var k in _loaded.Keys)
            if (Math.Max(Math.Abs(k.X - heroChunkX), Math.Abs(k.Y - heroChunkY)) > keep) (gone ??= []).Add(k);
        if (gone != null) foreach (var k in gone) Unload(k.Item1, k.Item2);
        return loaded;
    }

    /// <summary>Drops a chunk from memory. Its snapshot and deltas stay in the store.</summary>
    public bool Unload(int cx, int cy)
    {
        if (!_loaded.Remove((cx, cy))) return false;
        UnloadedCount++; Revision++;
        return true;
    }

    // ------------------------------------------------------------------ changing the world (ID-keyed deltas)

    /// <summary>Sets one cell's tile code and records it as a delta, so it survives unload and reload.</summary>
    public void SetTile(int wx, int wy, int z, int tileCode)
    {
        int cx = FloorDiv(wx, ChunkContent.Size), cy = FloorDiv(wy, ChunkContent.Size);
        int lx = wx - cx * ChunkContent.Size, ly = wy - cy * ChunkContent.Size;
        var c = LoadSync(cx, cy);
        Store.AppendDelta(new ChunkKey(cx, cy, z), [ChunkOp.SetTile(lx, ly, tileCode)]);
        c.Level(z).Tiles[ly * ChunkContent.Size + lx] = tileCode;
        c.Version++;
    }

    /// <summary>Removes a prop (a felled tree) by its stable id.</summary>
    public bool RemoveProp(int cx, int cy, int z, ulong id)
    {
        var c = LoadSync(cx, cy);
        if (!c.Level(z).Props.ContainsKey(id)) return false;
        Store.AppendDelta(new ChunkKey(cx, cy, z), [ChunkOp.RemoveObject(new EntityId(id))]);
        c.Level(z).Props.Remove(id);
        c.Version++;
        return true;
    }

    public int TileAt(int wx, int wy, int z)
    {
        int cx = FloorDiv(wx, ChunkContent.Size), cy = FloorDiv(wy, ChunkContent.Size);
        var c = TryGet(cx, cy);
        return c == null ? 0 : c.TileAt(wx - cx * ChunkContent.Size, wy - cy * ChunkContent.Size, z);
    }

    public static int FloorDiv(int a, int b) { int q = a / b; return (a % b != 0 && (a < 0) != (b < 0)) ? q - 1 : q; }

    /// <summary>Walkable cell test on the loaded content: ground that is not deep water, a walkable piece if built on, no blocking fixture or prop.</summary>
    public bool IsWalkable(int wx, int wy, int z = 0)
    {
        int cx = FloorDiv(wx, ChunkContent.Size), cy = FloorDiv(wy, ChunkContent.Size);
        var c = LoadSync(cx, cy);
        int lx = wx - cx * ChunkContent.Size, ly = wy - cy * ChunkContent.Size;
        int code = c.TileAt(lx, ly, z);
        var pieces = Generator.Library.Pieces;
        var st = pieces.KindOf(Generator.Palette.Id(TileCode.Structure(code)));
        var fx = pieces.KindOf(Generator.Palette.Id(TileCode.Fixture(code)));
        bool open;
        if (st == null) open = z == 0 && TileCode.Walkable(TileCode.Ground(code));
        else open = (st == Ravensguard.Content.PieceKind.Ground ? TileCode.Walkable(TileCode.Ground(code)) : Building.PieceCatalog.IsWalkable(st.Value))
                    && !(fx is { } f && Building.PieceCatalog.IsSolidFixture(f));
        if (!open) return false;
        foreach (var p in c.Level(z).Props.Values)
        {
            var info = p.Info;
            if (!info.Blocks) continue;
            if (p.Y == ly && (p.X == lx || (info.Wide && p.X + 1 == lx))) return false;
        }
        return true;
    }
}
