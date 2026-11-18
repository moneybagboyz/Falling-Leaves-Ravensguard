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
    public required ChunkGenerator Generator { get; init; }
    /// <summary>Wall-clock milliseconds the build took (region, landmarks, people, chunk generator); 0 for a region handed in already built.</summary>
    public double BuildMs { get; init; }
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

    private readonly Dictionary<int, GeneratedRegion> _ready = [];
    private readonly Dictionary<int, double> _queued = []; // region id -> priority (distance, smaller first)
    private readonly Dictionary<int, Task> _running = [];
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
        var g = new GeneratedRegion { Coord = at, Region = region, Population = population, Generator = ChunkGenerator.For(region, population, Content) };
        lock (_gate) { _ready[at.Id] = g; Revision++; }
        return g;
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
        var gen = ChunkGenerator.For(region, pop, Content);
        return new GeneratedRegion { Coord = at, Region = region, Population = pop, Generator = gen, BuildMs = sw.Elapsed.TotalMilliseconds };
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
        if (!_ready.ContainsKey(g.Coord.Id)) { _ready[g.Coord.Id] = g; _generated++; Revision++; }
        _lastMs = g.BuildMs; _maxMs = Math.Max(_maxMs, g.BuildMs); _totalMs += g.BuildMs;
        _queued.Remove(g.Coord.Id);
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
                foreach (var (n, d) in Triggered(pos.X, pos.Y))
                {
                    if (_ready.ContainsKey(n.Id) || _running.ContainsKey(n.Id)) continue;
                    if (!_queued.TryGetValue(n.Id, out var old) || d < old) _queued[n.Id] = d;
                }
            // drop jobs not started whose region no traveller is near any more
            if (_queued.Count > 0)
                foreach (int id in _queued.Keys.ToArray())
                    if (!NearAnyTraveller(RegionCoord.FromId(id), HysteresisM)) _queued.Remove(id);
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
                if (_ready.ContainsKey(id)) continue;
            }
            var rc = RegionCoord.FromId(id);
            if (Synchronous)
            {
                var g = Build(rc);
                lock (_gate) Attach(g);
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
            if (d.Error != null) throw new InvalidOperationException($"region {RegionCoord.FromId(d.Id)} failed to generate", d.Error);
        }
        if (n > 0) Pump();
        return n;
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
