using System.Collections.Concurrent;
using System.Diagnostics;
using Ravensguard.Content;
using Ravensguard.Sim.People;
using Ravensguard.Sim.WorldGen;
using Ravensguard.Sim.WorldGen.Chunks;
using Ravensguard.Sim.WorldGen.History;
using Ravensguard.Sim.WorldGen.Region;
using RegionData = Ravensguard.Sim.WorldGen.Region.Region;

namespace Ravensguard.Sim.Worlds;

/// <summary>One generated region with what hangs on it: its people, its landmarks (on <see cref="Region"/>) and the generator that makes its chunks.</summary>
public sealed class GeneratedRegion
{
    public required RegionCoord Coord { get; init; }
    public required RegionData Region { get; init; }
    public required Population Population { get; init; }
    /// <summary>The region's chunk generator. Made on first use (5-6 MB of road and lot indices) unless handed in: a neighbour nobody walks in never pays for it.</summary>
    public ChunkGenerator Generator
    {
        get
        {
            if (_gen is { } g) return g;
            lock (_genLock) return _gen ??= ChunkGenerator.For(Region, Population, Content ?? throw new InvalidOperationException("no content for the chunk generator"));
        }
        init => _gen = value;
    }
    private ChunkGenerator? _gen;
    private readonly object _genLock = new();
    internal ContentDatabase? Content { get; init; }
    /// <summary>True once <see cref="Generator"/> has been made.</summary>
    public bool HasGenerator => _gen != null;
    /// <summary>Lets go of the generator (5-6 MB); the next use makes it again. For a region nobody is near.</summary>
    internal void ReleaseGenerator() { lock (_genLock) { var g = _gen; if (g == null) return; _gen = null; ChunkGenerator.Forget(Region); } }
    /// <summary>Wall-clock milliseconds the build took (region, landmarks, people, chunk generator); 0 for a region handed in already built.</summary>
    public double BuildMs { get; init; }
    /// <summary>The region's records (people, economy ...), made on the worker with the region when the manager has a <see cref="RegionManager.RecordsFactory"/>; null otherwise.</summary>
    public RegionWorld? Records { get; init; }
}

/// <summary>Counters for the perf report of neighbour generation.</summary>
public sealed record RegionManagerStats(int Generated, int Queued, int Running, double LastMs, double MaxMs, double TotalMs, double MaxAttachMs);

/// <summary>
/// Owns the generated regions (seamless-regions spec 2.2, 3.3). Every region is a pure function of the continent and history, so the order and the
/// thread they are built on never change their content. Travellers (the hero, later squads and caravans) report where they are in continent metres; when one is
/// within <see cref="BorderTriggerM"/> of an edge (or of a corner, for the diagonal neighbour) the neighbour joins a queue, nearest first, and is built on worker
/// threads (at most <see cref="MaxConcurrent"/> at a time). A finished region is attached by <see cref="Poll"/> on the caller's thread as one dictionary insert.
/// </summary>
public sealed class RegionManager
{
    public Continent Continent { get; }
    public WorldHistory History { get; }
    public RegionSettings Settings { get; }
    public ContentDatabase Content { get; }

    /// <summary>A neighbour is queued when a traveller is nearer than this to the shared edge (metres).</summary>
    public int BorderTriggerM { get; set; } = 4000;
    /// <summary>A queued neighbour is dropped (if not yet started) when no traveller is within this distance of it.</summary>
    public int HysteresisM { get; set; } = 8000;
    public int MaxConcurrent { get; set; } = 2;
    /// <summary>Build inline in <see cref="Track"/> instead of on workers: tests and the loading of a saved game.</summary>
    public bool Synchronous { get; set; }
    /// <summary>Release the terrain layers nothing reads after generation (<see cref="Region.TrimDerived"/>) in every region built here (not the adopted home region): the game's setting, off for tests that read heights.</summary>
    public bool TrimTerrain { get; set; }
    /// <summary>Raised (under the manager's lock, on the thread that attached) when a built region becomes ready; <see cref="RegionRecordSet"/> takes the region's records in at once, so a chunk of it never streams before its saved chunk entries are in the store.</summary>
    public event Action<GeneratedRegion>? Attached;
    /// <summary>Make every region's chunk generator when it is built, however far the travellers are (tests that stream any region at once).</summary>
    public bool KeepGenerators { get; set; }

    /// <summary>
    /// Releases the chunk generators of regions no traveller is within <see cref="HysteresisM"/> of (never the home region's, which the streamer holds); called at a barrier.
    /// Returns how many went.
    /// </summary>
    public int ReleaseDistantGenerators(RegionCoord home)
    {
        List<GeneratedRegion> far = [];
        lock (_gate) foreach (var g in _ready.Values) if (g.HasGenerator && g.Coord != home && !NearAnyTraveller(g.Coord, HysteresisM)) far.Add(g);
        foreach (var g in far) g.ReleaseGenerator();
        return far.Count;
    }
    /// <summary>
    /// Drops regions that have only terrain (sea and wilderness: no records, so the scheduler never pages them) once no traveller is within <see cref="HysteresisM"/> of them
    /// and they are not within one region of <paramref name="keepAround"/> (3.2 MB each, up to 256 of them otherwise). They are built again, identically, if approached;
    /// their chunk entries stay in the world's chunk store. Called at a barrier; returns how many went.
    /// </summary>
    public int EvictDistantTerrainOnly(RegionCoord home, RegionCoord keepAround)
    {
        int n = 0;
        lock (_gate)
        {
            foreach (var g in _ready.Values.Where(g => g.Records == null && g.Coord != home && g.Coord.Chebyshev(keepAround) > 1 && !NearAnyTraveller(g.Coord, HysteresisM)).ToList())
            { _ready.Remove(g.Coord.Id); n++; }
            if (n > 0) Revision++;
        }
        return n;
    }

    /// <summary>
    /// Makes the chunk generator (about 100 ms of indices) of the nearest built region that has none and that a traveller is within <see cref="BorderTriggerM"/> of, on the calling
    /// thread (the tick worker), so the frame that first streams a chunk of it does not pay. One per call keeps an hour step short. True when one was made.
    /// </summary>
    public bool PrewarmGenerator()
    {
        GeneratedRegion? pick = null; double best = double.MaxValue;
        lock (_gate)
            foreach (var g in _ready.Values)
            {
                if (g.HasGenerator) continue;
                double d = DistanceToNearestTraveller(g.Coord);
                if (d <= BorderTriggerM && d < best) { best = d; pick = g; }
            }
        if (pick == null) return false;
        _ = pick.Generator;
        return true;
    }

    private double DistanceToNearestTraveller(RegionCoord r)
    {
        double best = double.MaxValue;
        foreach (var (_, p) in _travellers)
        {
            long dx = Math.Max(Math.Max(r.OriginXM - p.X, 0), p.X - (r.OriginXM + RegionCoord.RegionM));
            long dy = Math.Max(Math.Max(r.OriginYM - p.Y, 0), p.Y - (r.OriginYM + RegionCoord.RegionM));
            best = Math.Min(best, Math.Sqrt((double)dx * dx + (double)dy * dy));
        }
        return best;
    }

    /// <summary>Makes a built region's records on the worker (fresh, or restored from a paged section). Set by <see cref="RegionRecordSet"/>.</summary>
    public Func<RegionCoord, RegionData, Population, RegionWorld?>? RecordsFactory { get; set; }

    /// <summary>The game hour now (the game clock; set by the runtime's pump). A region asked for gets this as its request hour, which anchors how it catches up (<see cref="RegionWorld.SetCatchUpAnchor"/>).</summary>
    public long NowHour { get => Interlocked.Read(ref _nowHour); set => Interlocked.Exchange(ref _nowHour, value); }
    private long _nowHour;
    /// <summary>Attempts at building a region before it is marked failed and left alone.</summary>
    public int MaxBuildAttempts { get; set; } = 3;
    /// <summary>Wait after the first failed build; it doubles with each further failure. Wall-clock infrastructure, not part of the simulation.</summary>
    public int RetryBackoffMs { get; set; } = 2000;
    /// <summary>Raised once, on the thread that polls, when a region's build has failed <see cref="MaxBuildAttempts"/> times and it is given up on (the game logs it).</summary>
    public event Action<RegionCoord, Exception>? BuildFailed;
    private readonly Dictionary<int, int> _attempts = [];
    private readonly Dictionary<int, long> _retryAt = [];     // region id -> Stopwatch timestamp before which it is not tried again
    private readonly Dictionary<int, Exception> _failed = [];
    private readonly Dictionary<int, long> _requestHour = [];

    /// <summary>Regions given up on after repeated failed builds, ascending id, with the last error.</summary>
    public IReadOnlyList<(RegionCoord Region, Exception Error)> Failed { get { lock (_gate) return [.. _failed.OrderBy(kv => kv.Key).Select(kv => (RegionCoord.FromId(kv.Key), kv.Value))]; } }
    /// <summary>Lets a region given up on be tried again (after the cause was fixed).</summary>
    public void ClearFailure(RegionCoord at) { lock (_gate) { _failed.Remove(at.Id); _attempts.Remove(at.Id); _retryAt.Remove(at.Id); } }

    /// <summary>The game hour a region was first asked for at (while it is queued, building or built-but-not-yet-taken): <see cref="NowHour"/> when no request is on record (an inline build).</summary>
    public long RequestedHour(RegionCoord at) { lock (_gate) return _requestHour.TryGetValue(at.Id, out long h) ? h : NowHour; }

    private bool Blocked(int id) => _failed.ContainsKey(id) || (_retryAt.TryGetValue(id, out long t) && Stopwatch.GetTimestamp() < t);
    private void Stamp(int id, long? hour) { long h = hour ?? NowHour; _requestHour[id] = _requestHour.TryGetValue(id, out long old) ? Math.Min(old, h) : h; }

    private readonly Dictionary<int, GeneratedRegion> _ready = [];
    private readonly Dictionary<int, double> _queued = []; // region id -> priority (distance, smaller first)
    private readonly Dictionary<int, Task> _running = [];
    private readonly HashSet<int> _requested = [];
    private readonly ConcurrentQueue<(int Id, GeneratedRegion? Result, Exception? Error)> _done = new();
    private readonly Dictionary<int, (long X, long Y)> _travellers = [];
    private readonly object _gate = new();
    private int _generated;
    private double _lastMs, _maxMs, _totalMs, _maxAttachMs;

    /// <summary>Bumps whenever a region is attached.</summary>
    public int Revision { get; private set; }

    public RegionManager(Continent continent, WorldHistory history, RegionSettings settings, ContentDatabase content)
    {
        Continent = continent; History = history; Settings = settings; Content = content;
    }

    /// <summary>Hands in a region that was built elsewhere (the embark region), so it is never built twice.</summary>
    public GeneratedRegion Adopt(RegionCoord at, RegionData region, Population population)
    {
        var g = new GeneratedRegion { Coord = at, Region = region, Population = population, Generator = ChunkGenerator.For(region, population, Content), Content = Content };
        lock (_gate) { _ready[at.Id] = g; Revision++; }
        return g;
    }

    /// <summary>Drops a region from memory (it was paged out); it can be built again, identically, by <see cref="Request"/> or a traveller.</summary>
    public bool Evict(RegionCoord at) { lock (_gate) { bool r = _ready.Remove(at.Id); if (r) Revision++; return r; } }

    /// <summary>Queues a region for building (nearest first by <paramref name="priority"/>); in synchronous mode builds it now.</summary>
    public void Request(RegionCoord at, double priority = 0, long? hour = null)
    {
        lock (_gate)
        {
            if (!at.IsValid || _ready.ContainsKey(at.Id) || _running.ContainsKey(at.Id) || Blocked(at.Id)) return;
            _queued[at.Id] = priority; _requested.Add(at.Id); Stamp(at.Id, hour);
        }
        Pump();
    }

    public GeneratedRegion? TryGet(RegionCoord at) { lock (_gate) return at.IsValid && _ready.TryGetValue(at.Id, out var g) ? g : null; }
    public bool IsReady(RegionCoord at) => TryGet(at) != null;
    public IReadOnlyList<GeneratedRegion> Ready { get { lock (_gate) return [.. _ready.Values.OrderBy(g => g.Coord.Id)]; } }

    public RegionManagerStats Stats { get { lock (_gate) return new(_generated, _queued.Count, _running.Count, _lastMs, _maxMs, _totalMs, _maxAttachMs); } }

    /// <summary>Builds one region from the continent and history alone (region, landmarks, people, chunk generator). Pure: the same result in any order, on any thread.</summary>
    public GeneratedRegion Build(RegionCoord at)
    {
        var sw = Stopwatch.StartNew();
        var region = RegionGenerator.Generate(Continent, History, at, Settings);
        LandmarkPlacer.Place(region, History, Content);
        var pop = Populator.Populate(region, History, Content);
        var rec = RecordsFactory?.Invoke(at, region, pop);
        pop = rec?.Population ?? pop;
        if (TrimTerrain) region.TrimDerived();
        var g = new GeneratedRegion { Coord = at, Region = region, Population = pop, Content = Content, Records = rec, BuildMs = sw.Elapsed.TotalMilliseconds };
        // a region a traveller is near will be streamed soon: its chunk generator (about 100 ms of indices) is made here, on the worker, not on the frame that first needs it;
        // one built for the scheduler's sake (a ring-1 region far from the hero) makes it on first use
        bool near; lock (_gate) near = NearAnyTraveller(at, HysteresisM);
        if (near || KeepGenerators) _ = g.Generator;
        return g;
    }

    /// <summary>Makes a region ready now on the calling thread (a teleport target, a test, a load): the region if already built.</summary>
    public GeneratedRegion GenerateNow(RegionCoord at)
    {
        if (TryGet(at) is { } have) return have;
        var g = Build(at);
        lock (_gate) { Attach(g); }
        return TryGet(at)!;
    }

    private void Attach(GeneratedRegion g)
    {
        var t = Stopwatch.GetTimestamp();
        if (!_ready.ContainsKey(g.Coord.Id)) { _ready[g.Coord.Id] = g; _generated++; Revision++; Attached?.Invoke(g); }
        _lastMs = g.BuildMs; _maxMs = Math.Max(_maxMs, g.BuildMs); _totalMs += g.BuildMs;
        _queued.Remove(g.Coord.Id); _requested.Remove(g.Coord.Id); _requestHour.Remove(g.Coord.Id); _attempts.Remove(g.Coord.Id); _retryAt.Remove(g.Coord.Id);
        _maxAttachMs = Math.Max(_maxAttachMs, Stopwatch.GetElapsedTime(t).TotalMilliseconds);
    }

    // ------------------------------------------------------------------ travellers

    /// <summary>Reports where a traveller is, in continent metres, and queues the neighbours it is close to; starts or finishes jobs.</summary>
    public void Track(int travellerId, long xMetres, long yMetres)
    {
        lock (_gate) _travellers[travellerId] = (xMetres, yMetres);
        Evaluate();
    }

    public void Forget(int travellerId) { lock (_gate) _travellers.Remove(travellerId); }

    /// <summary>The neighbours (and their distances in metres) a position would queue: the side neighbours within the trigger distance, the diagonal when the corner is within it.</summary>
    public IEnumerable<(RegionCoord Region, double DistanceM)> Triggered(long xMetres, long yMetres)
    {
        var at = RegionCoord.OfMetres(xMetres, yMetres);
        long x = xMetres - at.OriginXM, y = yMetres - at.OriginYM, m = RegionCoord.RegionM;
        long dW = x, dE = m - x, dN = y, dS = m - y;
        var res = new List<(RegionCoord, double)>();
        void Side(int dx, int dy, long d) { var n = at.Offset(dx, dy); if (n.IsValid && d < BorderTriggerM) res.Add((n, d)); }
        void Corner(int dx, int dy, long cx, long cy)
        {
            var n = at.Offset(dx, dy);
            double d = Math.Sqrt((double)cx * cx + (double)cy * cy);
            if (n.IsValid && d < BorderTriggerM) res.Add((n, d));
        }
        Side(0, -1, dN); Side(1, 0, dE); Side(0, 1, dS); Side(-1, 0, dW);
        Corner(-1, -1, dW, dN); Corner(1, -1, dE, dN); Corner(1, 1, dE, dS); Corner(-1, 1, dW, dS);
        return res;
    }

    private void Evaluate()
    {
        Poll();
        lock (_gate)
        {
            foreach (var (_, pos) in _travellers)
            {
                // the region a traveller stands in comes first (a hero put down in an unbuilt region)
                var own = RegionCoord.OfMetres(pos.X, pos.Y);
                if (own.IsValid && !_ready.ContainsKey(own.Id) && !_running.ContainsKey(own.Id) && !Blocked(own.Id)) { _queued[own.Id] = -1; Stamp(own.Id, null); }
                foreach (var (n, d) in Triggered(pos.X, pos.Y))
                {
                    if (_ready.ContainsKey(n.Id) || _running.ContainsKey(n.Id) || Blocked(n.Id)) continue;
                    if (!_queued.TryGetValue(n.Id, out var old) || d < old) _queued[n.Id] = d;
                    Stamp(n.Id, null);
                }
            }
            // drop jobs not started whose region no traveller is near any more
            if (_queued.Count > 0)
                foreach (int id in _queued.Keys.ToArray())
                    if (!_requested.Contains(id) && !NearAnyTraveller(RegionCoord.FromId(id), HysteresisM)) { _queued.Remove(id); _requestHour.Remove(id); }
        }
        Pump();
    }

    private bool NearAnyTraveller(RegionCoord r, double withinM)
    {
        foreach (var (_, p) in _travellers)
        {
            long dx = Math.Max(Math.Max(r.OriginXM - p.X, 0), p.X - (r.OriginXM + RegionCoord.RegionM));
            long dy = Math.Max(Math.Max(r.OriginYM - p.Y, 0), p.Y - (r.OriginYM + RegionCoord.RegionM));
            if (Math.Sqrt((double)dx * dx + (double)dy * dy) <= withinM) return true;
        }
        return false;
    }

    /// <summary>Starts queued jobs (nearest first) while fewer than <see cref="MaxConcurrent"/> run; in synchronous mode builds them all now.</summary>
    private void Pump()
    {
        while (true)
        {
            int id;
            lock (_gate)
            {
                if (_queued.Count == 0 || (!Synchronous && _running.Count >= MaxConcurrent)) return;
                id = _queued.OrderBy(kv => kv.Value).ThenBy(kv => kv.Key).First().Key;
                _queued.Remove(id);
                if (_ready.ContainsKey(id) || Blocked(id)) continue;
            }
            var rc = RegionCoord.FromId(id);
            if (Synchronous)
            {
                GeneratedRegion? g = null; Exception? err = null;
                try { g = Build(rc); } catch (Exception e) { err = e; }
                if (g != null) lock (_gate) Attach(g); else RecordFailure(id, err!);
                continue;
            }
            lock (_gate)
            {
                _running[id] = Task.Run(() =>
                {
                    try { _done.Enqueue((id, Build(rc), null)); }
                    catch (Exception e) { _done.Enqueue((id, null, e)); }
                });
            }
        }
    }

    /// <summary>Attaches finished regions (on the caller's thread: one dictionary insert each) and starts the next queued ones. Returns how many were attached.</summary>
    public int Poll()
    {
        int n = 0;
        while (_done.TryDequeue(out var d))
        {
            lock (_gate)
            {
                _running.Remove(d.Id);
                if (d.Result != null) { Attach(d.Result); n++; }
            }
            if (d.Error != null) RecordFailure(d.Id, d.Error);
        }
        if (n > 0) Pump();
        return n;
    }

    /// <summary>A build failed: count it, back off, and after <see cref="MaxBuildAttempts"/> give the region up (logged once through <see cref="BuildFailed"/>) instead of retrying for ever and throwing on the caller.</summary>
    private void RecordFailure(int id, Exception error)
    {
        bool giveUp;
        lock (_gate)
        {
            _running.Remove(id); _queued.Remove(id); _requested.Remove(id);
            int n = _attempts.GetValueOrDefault(id) + 1; _attempts[id] = n;
            giveUp = n >= MaxBuildAttempts;
            if (giveUp) { _failed[id] = error; _requestHour.Remove(id); }
            else _retryAt[id] = Stopwatch.GetTimestamp() + (long)(Stopwatch.Frequency * (RetryBackoffMs << (n - 1)) / 1000.0);
        }
        if (giveUp) BuildFailed?.Invoke(RegionCoord.FromId(id), error);
    }

    /// <summary>Waits for every queued and running job (tests, shutdown).</summary>
    public void WaitIdle()
    {
        while (true)
        {
            Task[] t;
            lock (_gate) { t = [.. _running.Values]; if (t.Length == 0 && _queued.Count == 0 && _done.IsEmpty) return; }
            if (t.Length > 0) Task.WaitAll(t);
            Poll();
            Pump();
        }
    }

    public bool IsBusy { get { lock (_gate) return _running.Count > 0 || _queued.Count > 0 || !_done.IsEmpty; } }
}
