using System.Diagnostics;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Live;
using Ravensguard.Sim.Save;
using Ravensguard.Sim.WorldGen.Region;
using Ravensguard.Sim.Worlds;

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
    /// <summary>Most chunks generated on worker threads at once when <see cref="ChunkStreamer.Update"/> is asked to work in the background.</summary>
    public int MaxBackgroundJobs { get; set; } = 3;
}

/// <summary>
/// Keeps the chunks around the hero loaded (plan 2.2, 2.3, 2.11). A chunk is generated on its first visit and snapshotted into the
/// <see cref="ChunkStore"/>; every later visit rebuilds it from snapshot plus deltas, so a changed tile survives unloading. Generation is
/// time-sliced by <see cref="Update"/> (optionally on worker threads); <see cref="LoadSync"/> and <see cref="LoadAround"/> are the synchronous path for tests and start-up.
/// </summary>
public sealed class ChunkStreamer
{
    private readonly Dictionary<(int X, int Y), LoadedChunk> _loaded = [];
    private readonly (int Dx, int Dy)[] _order;
    private readonly Stopwatch _sw = new();
    private double _avgLoadMs = 1.0;
    private readonly Dictionary<(int X, int Y), Task<Prepared>> _prefetch = [];
    /// <summary>Chunks whose generation ran on a worker and was picked up by the main thread.</summary>
    public long PrefetchedCount { get; private set; }

    /// <summary>Chunks per region side (32 km / 32 m).</summary>
    public const int RegionChunks = 1000;
    /// <summary>True for chunk coordinates inside the home region (0..RegionChunks-1), the frame's origin region.</summary>
    public static bool InRegion(int cx, int cy) => (uint)cx < RegionChunks && (uint)cy < RegionChunks;

    /// <summary>
    /// The generator of the home (embark) region. Chunk coordinates are world coordinates in the frame whose origin is the north-west corner of the home region, so
    /// the home region keeps coordinates 0..RegionChunks-1 and a neighbour to the west or north has negative ones, one to the east or south 1000 and up
    /// (seamless-regions spec 2.1, "region + local with a mapping"). Without <see cref="Regions"/> the home region is the whole world and everything beyond it is closed.
    /// </summary>
    public ChunkGenerator Generator { get; }
    /// <summary>The generated regions around the home region (null for a single-region world).</summary>
    public RegionManager? Regions { get; }
    /// <summary>The grid square of the home region (the frame's origin); meaningful with <see cref="Regions"/>.</summary>
    public RegionCoord Home { get; }
    /// <summary>Continent metres of the frame's origin.</summary>
    public int HomeOriginXM => Home.OriginXM;
    public int HomeOriginYM => Home.OriginYM;
    private readonly HashSet<(int X, int Y)> _neutral = [];
    /// <summary>Chunks standing in for a region that is not ready yet (neutral ground).</summary>
    public int NeutralCount => _neutral.Count;
    /// <summary>The traveller id the hero reports under to <see cref="Regions"/>.</summary>
    public const int HeroTraveller = 1;
    public ChunkStore Store { get; }
    public ChunkStreamerSettings Settings { get; }
    /// <summary>Bumps whenever a chunk is loaded or unloaded.</summary>
    public int Revision { get; private set; }
    public int LoadedCount => _loaded.Count;
    public IEnumerable<LoadedChunk> Loaded => _loaded.Values;

    // statistics
    public long GeneratedCount { get; private set; }
    /// <summary>Chunks made before a growth change and made again before their first visit was recorded (review finding 1).</summary>
    public long StaleGenerated { get; private set; }
    public long ReloadedCount { get; private set; }
    public long UnloadedCount { get; private set; }
    /// <summary>Snapshots found whose generator version is older than the code's: kept as stored (a visited place never changes under the player).</summary>
    public long StaleSnapshots { get; private set; }
    public double GenerateMsTotal { get; private set; }
    public double LastLoadMs { get; private set; }
    public double MaxLoadMs { get; private set; }

    /// <summary>A world of one region: chunks beyond it are closed ground.</summary>
    public ChunkStreamer(ChunkGenerator generator, ChunkStore? store = null, ChunkStreamerSettings? settings = null) : this(generator, null, default, store, settings) { }

    /// <summary>The seamless world: chunks of any generated region around <paramref name="home"/> (whose generator must be the manager's), neutral ground where a region is not ready yet.</summary>
    public ChunkStreamer(RegionManager regions, RegionCoord home, ChunkStore? store = null, ChunkStreamerSettings? settings = null)
        : this((regions.TryGet(home) ?? throw new ArgumentException($"home region {home} is not in the manager", nameof(home))).Generator, regions, home, store, settings) { }

    private ChunkStreamer(ChunkGenerator generator, RegionManager? regions, RegionCoord home, ChunkStore? store, ChunkStreamerSettings? settings)
    {
        Generator = generator; Regions = regions; Home = home; Store = store ?? new ChunkStore(); Settings = settings ?? new ChunkStreamerSettings();
        Store.AdoptPalette(generator.Palette); // saved chunks keep their furniture whatever pieces the content has added since (they are renumbered to this palette)
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

    private enum Where { Off, Pending, Ready }

    /// <summary>Which region a world chunk belongs to and, when it is built, the generator and the chunk's coordinates inside that region.</summary>
    private Where Resolve(int cx, int cy, out ChunkGenerator? gen, out int lx, out int ly)
    {
        int rx = FloorDiv(cx, RegionChunks), ry = FloorDiv(cy, RegionChunks);
        lx = cx - rx * RegionChunks; ly = cy - ry * RegionChunks; gen = null;
        if (Regions == null) { if (rx != 0 || ry != 0) return Where.Off; gen = Generator; return Where.Ready; }
        var at = Home.Offset(rx, ry);
        if (!at.IsValid) return Where.Off;
        if (rx == 0 && ry == 0) { gen = Generator; return Where.Ready; }
        gen = Regions.TryGet(at)?.Generator;
        return gen == null ? Where.Pending : Where.Ready;
    }

    /// <summary>
    /// Makes the region holding a world chunk ready now, on the calling thread, if it is not built yet: for a hero put down in a region (a teleport, a load, a start at a
    /// border) where walking in from a neighbour would have had it built long before. Nothing to do in a single-region world.
    /// </summary>
    public void EnsureReady(int cx, int cy)
    {
        if (Regions == null) return;
        var at = RegionOfChunk(cx, cy);
        if (at.IsValid) _ = Regions.GenerateNow(at).Generator; // the streamer is about to use it: made here, not lazily on the first chunk
    }

    /// <summary>The region square that holds a world chunk (Home itself without a manager and inside the home region).</summary>
    public RegionCoord RegionOfChunk(int cx, int cy) => Home.Offset(FloorDiv(cx, RegionChunks), FloorDiv(cy, RegionChunks));

    /// <summary>The generator of the region holding a world chunk, null when that region is not built (yet) or off the continent.</summary>
    public ChunkGenerator? GeneratorOfChunk(int cx, int cy) { Resolve(cx, cy, out var g, out _, out _); return g; }

    private static LoadedChunk NeutralChunk(int cx, int cy, int version)
    {
        var c = new LoadedChunk(cx, cy);
        var l = c.Level(0);
        int code = TileCode.Make(GroundKind.Grass, 0, 0, 0);
        for (int i = 0; i < l.Tiles.Length; i++) l.Tiles[i] = code;
        c.Version = version;
        return c;
    }

    /// <summary>Loads one chunk now (no-op when loaded) and returns it.</summary>
    public LoadedChunk LoadSync(int cx, int cy)
    {
        if (_loaded.TryGetValue((cx, cy), out var have)) return have;
        // Beyond the world there is nothing to generate or remember: an empty chunk (closed cells) is "loaded" so the hero never waits on it, and it is never stored.
        var where = Resolve(cx, cy, out var gen0, out int lx0, out int ly0);
        if (where == Where.Off) { var empty = new LoadedChunk(cx, cy); _loaded[(cx, cy)] = empty; Revision++; return empty; }
        // A region that is not built yet shows neutral ground; the chunk is rebuilt from the real region once it is attached (see Update).
        if (where == Where.Pending) { var n = NeutralChunk(cx, cy, 0); _loaded[(cx, cy)] = n; _neutral.Add((cx, cy)); Revision++; return n; }
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
            var prep = TakePrefetched(cx, cy);
            // the first visit is recorded under the region's growth lock and only from the region as it stands now: a chunk made (here or on a worker) before a growth change that
            // has run since is made again, so the snapshot never misses a building the change wrote no delta for (review finding 1)
            var region = gen0!.Region;
            using (region.ReadGrowth())
            {
                var gen = prep?.Gen;
                if (gen == null || gen.GrowthVersion != region.GrowthVersion) { if (gen != null) StaleGenerated++; gen = gen0.GenerateLevels(lx0, ly0); prep = null; }
                foreach (var (z, content) in gen.Levels)
                {
                    if (prep != null && prep.Snapshots.TryGetValue(z, out var snap)) Store.RecordFirstVisit(new ChunkKey(cx, cy, z), snap, content);
                    else Store.RecordFirstVisit(new ChunkKey(cx, cy, z), ChunkGenerator.Version, gen.ContentHash, content);
                    LoadedChunk.Decode(c, z, content);
                }
            }
            GeneratedCount++;
        }
        double ms = _sw.Elapsed.TotalMilliseconds;
        LastLoadMs = ms; MaxLoadMs = Math.Max(MaxLoadMs, ms); GenerateMsTotal += ms; _avgLoadMs = _avgLoadMs * 0.8 + ms * 0.2;
        _loaded[(cx, cy)] = c;
        Revision++;
        return c;
    }

    /// <summary>
    /// Replaces neutral chunks whose region has been attached since: each is rebuilt in place with a new version so a window stamps it again. At most
    /// <paramref name="budgetMs"/> of work per call (at least one chunk), nearest the hero first.
    /// </summary>
    private int UpgradeNeutral(int heroChunkX, int heroChunkY, double budgetMs)
    {
        if (Regions == null || _neutral.Count == 0) return 0;
        var ready = new List<(int X, int Y)>();
        foreach (var k in _neutral) if (Resolve(k.X, k.Y, out _, out _, out _) != Where.Pending) ready.Add(k);
        if (ready.Count == 0) return 0;
        ready.Sort((a, b) =>
        {
            int da = Math.Max(Math.Abs(a.X - heroChunkX), Math.Abs(a.Y - heroChunkY)), db = Math.Max(Math.Abs(b.X - heroChunkX), Math.Abs(b.Y - heroChunkY));
            return da != db ? da.CompareTo(db) : a.Y != b.Y ? a.Y.CompareTo(b.Y) : a.X.CompareTo(b.X);
        });
        var t0 = Stopwatch.GetTimestamp();
        int n = 0;
        foreach (var k in ready)
        {
            if (n > 0 && Stopwatch.GetElapsedTime(t0).TotalMilliseconds >= budgetMs) break;
            int v = _loaded.TryGetValue(k, out var old) ? old.Version + 1 : 1;
            _neutral.Remove(k); _loaded.Remove(k);
            var c = LoadSync(k.X, k.Y);
            c.Version = v;
            // a built chunk beside it was drawn against neutral ground (road rims, fence joins): it is stamped and drawn again
            foreach (var (dx, dy) in new[] { (0, -1), (1, 0), (0, 1), (-1, 0) })
                if (_loaded.TryGetValue((k.X + dx, k.Y + dy), out var nb) && !_neutral.Contains((k.X + dx, k.Y + dy))) nb.Version++;
            n++;
        }
        return n;
    }

    private bool NeedsGenerating(int cx, int cy) => !_loaded.ContainsKey((cx, cy)) && Resolve(cx, cy, out _, out _, out _) == Where.Ready && !Store.Contains(new ChunkKey(cx, cy, 0));

    /// <summary>Starts workers on the nearest chunks that still have to be generated, keeping at most <see cref="ChunkStreamerSettings.MaxBackgroundJobs"/> running (finished jobs waiting to be picked up do not count).</summary>
    private void StartBackgroundJobs(int heroChunkX, int heroChunkY)
    {
        int running = 0;
        foreach (var t in _prefetch.Values) if (!t.IsCompleted) running++;
        if (running >= Settings.MaxBackgroundJobs) return;
        foreach (var (dx, dy) in _order)
        {
            int x = heroChunkX + dx, y = heroChunkY + dy;
            if (!NeedsGenerating(x, y) || _prefetch.ContainsKey((x, y))) continue;
            Resolve(x, y, out var g, out int lx, out int ly);
            _prefetch[(x, y)] = Task.Run(() => Prepare(g!, lx, ly));
            if (++running >= Settings.MaxBackgroundJobs) break;
        }
    }

    /// <summary>A chunk made on a worker: the generated levels and their stored snapshots (the expensive part of recording a first visit).</summary>
    private sealed record Prepared(GeneratedChunk Gen, Dictionary<int, ChunkSnapshot> Snapshots);

    private Prepared Prepare(ChunkGenerator generator, int cx, int cy)
    {
        var gen = generator.GenerateLevels(cx, cy);
        var snaps = new Dictionary<int, ChunkSnapshot>();
        foreach (var (z, content) in gen.Levels) if (!content.IsEmpty) snaps[z] = ChunkSnapshot.Create(ChunkGenerator.Version, gen.ContentHash, content);
        return new Prepared(gen, snaps);
    }

    /// <summary>The generated chunk a worker made (waiting for it if it is still running), or null when none was started or it failed.</summary>
    private Prepared? TakePrefetched(int cx, int cy)
    {
        if (!_prefetch.Remove((cx, cy), out var task)) return null;
        try { var r = task.GetAwaiter().GetResult(); PrefetchedCount++; return r; }
        catch (Exception) { return null; } // generate it here instead
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
    public int Update(int heroChunkX, int heroChunkY, double? budgetMs = null, bool background = false)
    {
        int loaded = 0;
        double budget = budgetMs ?? Settings.BudgetMs;
        if (Regions != null) { TrackHero(heroChunkX, heroChunkY); Regions.Poll(); }
        UpgradeNeutral(heroChunkX, heroChunkY, budget);
        int sync = Settings.SyncRadius;
        foreach (var (dx, dy) in _order)
        {
            if (Math.Max(Math.Abs(dx), Math.Abs(dy)) > sync) break;
            if (!_loaded.ContainsKey((heroChunkX + dx, heroChunkY + dy))) { LoadSync(heroChunkX + dx, heroChunkY + dy); loaded++; }
        }
        if (background) StartBackgroundJobs(heroChunkX, heroChunkY);
        var t0 = Stopwatch.GetTimestamp();
        int extra = 0;
        foreach (var (dx, dy) in _order)
        {
            if (Math.Max(Math.Abs(dx), Math.Abs(dy)) <= sync) continue;
            if (_loaded.ContainsKey((heroChunkX + dx, heroChunkY + dy))) continue;
            if (background && NeedsGenerating(heroChunkX + dx, heroChunkY + dy) && !(_prefetch.TryGetValue((heroChunkX + dx, heroChunkY + dy), out var job) && job.IsCompleted))
                continue; // being made on a worker: picked up on a later call
            // do not start a chunk that is expected to run past the budget (the average of the loads so far), except the first one beyond the sync ring
            if (extra > 0 && Stopwatch.GetElapsedTime(t0).TotalMilliseconds + _avgLoadMs > budget) break;
            LoadSync(heroChunkX + dx, heroChunkY + dy); loaded++; extra++;
            if (Stopwatch.GetElapsedTime(t0).TotalMilliseconds >= budget) break;
        }
        int keep = Settings.KeepRadius;
        if (_prefetch.Count > 0)
            foreach (var k in _prefetch.Keys.ToArray())
                if (Math.Max(Math.Abs(k.X - heroChunkX), Math.Abs(k.Y - heroChunkY)) > keep) _prefetch.Remove(k);
        List<(int, int)>? gone = null;
        foreach (var k in _loaded.Keys)
            if (Math.Max(Math.Abs(k.X - heroChunkX), Math.Abs(k.Y - heroChunkY)) > keep) (gone ??= []).Add(k);
        if (gone != null) foreach (var k in gone) Unload(k.Item1, k.Item2);
        return loaded;
    }

    private int _trackedX = int.MinValue, _trackedY = int.MinValue;

    /// <summary>Tells the region manager where the hero is (continent metres of the chunk's centre) when the hero's chunk changes; it queues neighbours near an edge.</summary>
    private void TrackHero(int heroChunkX, int heroChunkY)
    {
        if (heroChunkX == _trackedX && heroChunkY == _trackedY) return;
        _trackedX = heroChunkX; _trackedY = heroChunkY;
        Regions!.Track(HeroTraveller, (long)HomeOriginXM + heroChunkX * 32L + 16, (long)HomeOriginYM + heroChunkY * 32L + 16);
    }

    /// <summary>Drops a chunk from memory. Its snapshot and deltas stay in the store.</summary>
    public bool Unload(int cx, int cy)
    {
        if (!_loaded.Remove((cx, cy))) return false;
        _neutral.Remove((cx, cy));
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

    /// <summary>
    /// P5-B: lays a built (or cleared) piece into a chunk that is loaded, without a delta: construction has already written the delta to the store on the tick worker (or the chunk was never
    /// visited and is laid from the construction book on every load). The window re-stamps the chunk. False when the chunk is not loaded or nothing changed.
    /// </summary>
    public bool SetLoadedTile(int wx, int wy, int z, string? structure, string? fixture)
    {
        int cx = FloorDiv(wx, ChunkContent.Size), cy = FloorDiv(wy, ChunkContent.Size);
        var c = TryGet(cx, cy);
        if (c == null || z < ChunkKey.MinZ || z > ChunkKey.MaxZ) return false;
        int lx = wx - cx * ChunkContent.Size, ly = wy - cy * ChunkContent.Size;
        var lv = c.Level(z);
        int i = ly * ChunkContent.Size + lx, cur = lv.Tiles[i];
        int code = TileCode.WithFixture(TileCode.WithStructure(cur, Generator.Palette.Of(structure)), Generator.Palette.Of(fixture));
        if (code == cur) return false;
        lv.Tiles[i] = code;
        c.Version++;
        return true;
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
