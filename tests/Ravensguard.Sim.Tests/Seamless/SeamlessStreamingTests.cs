using System.Diagnostics;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Live;
using Ravensguard.Sim.World;
using Ravensguard.Sim.WorldGen;
using Ravensguard.Sim.WorldGen.Chunks;
using Ravensguard.Sim.WorldGen.Region;
using Ravensguard.Sim.Worlds;
using Xunit.Abstractions;
using RegionData = Ravensguard.Sim.WorldGen.Region.Region;

namespace Ravensguard.Sim.Tests.Seamless;

/// <summary>Part A of the seamless continent: neighbour generation ahead of a traveller, chunks resolved across region edges, and a walk through three regions.</summary>
public class SeamlessStreamingTests(ITestOutputHelper output)
{
    private static RegionManager NewManager(SeamWorld w, RegionCoord home)
    {
        var m = new RegionManager(w.Continent, w.History, w.Settings, w.Content);
        m.GenerateNow(home);
        return m;
    }

    private static (RegionCoord Home, long X, long Y) At(SeamWorld w, long dxFromEastM, long dyFromNorthM)
    {
        var home = w.BlockOrigin.Offset(1, 1);
        return (home, home.OriginXM + RegionCoord.RegionM - dxFromEastM, home.OriginYM + dyFromNorthM);
    }

    [Fact]
    public void Trigger_distance_queues_the_side_neighbours_and_the_corner_as_the_spec_says()
    {
        var w = SeamWorld.Get(1234);
        var m = new RegionManager(w.Continent, w.History, w.Settings, w.Content);
        var home = w.BlockOrigin.Offset(1, 1);
        IEnumerable<RegionCoord> Q(long fromEast, long fromNorth) => m.Triggered(home.OriginXM + 32000 - fromEast, home.OriginYM + fromNorth).Select(t => t.Region).OrderBy(r => r.Id).ToList();
        Assert.Equal([home.Offset(1, 0)], Q(3900, 16000));
        Assert.Empty(Q(4100, 16000));
        // hero 3.2 km from the east border and 3.8 km from the south border: E and S, the SE corner is 4.97 km away
        Assert.Equal(new[] { home.Offset(1, 0), home.Offset(0, 1) }.OrderBy(r => r.Id), Q(3200, 32000 - 3800));
        // 2.0 km east, 3.0 km south: the corner is 3.6 km away, so the diagonal joins
        Assert.Equal(new[] { home.Offset(1, 0), home.Offset(0, 1), home.Offset(1, 1) }.OrderBy(r => r.Id), Q(2000, 32000 - 3000));
        Assert.Empty(Q(16000, 16000));
    }

    [Fact]
    public void Neighbours_are_generated_on_workers_before_the_hero_reaches_the_border_and_equal_a_fresh_build()
    {
        var w = SeamWorld.Get(1234);
        var (home, x, y) = At(w, 3900, 16000);
        var m = NewManager(w, home);
        var east = home.Offset(1, 0);
        Assert.False(m.IsReady(east));
        var sw = Stopwatch.StartNew();
        m.Track(1, x, y);
        double queueMs = sw.Elapsed.TotalMilliseconds;
        Assert.True(queueMs < 50, $"Track returned in {queueMs:0.0} ms: generation must not run on the calling thread");
        m.WaitIdle();
        Assert.True(m.IsReady(east));
        var g = m.TryGet(east)!;
        output.WriteLine($"east neighbour built on a worker in {g.BuildMs:0} ms (region, landmarks, people, chunk generator); attach {m.Stats.MaxAttachMs:0.000} ms");
        Assert.True(g.BuildMs < TestBudget.Ms(3000), $"{g.BuildMs:0} ms");
        Assert.True(m.Stats.MaxAttachMs < TestBudget.Ms(3.0), $"attach {m.Stats.MaxAttachMs} ms");
        Assert.Equal(w.Fresh(east).ComputeHash(), g.Region.ComputeHash());
        // the corner neighbour joined only when a traveller is near the corner
        Assert.False(m.IsReady(home.Offset(1, 1)));
        m.Track(1, home.OriginXM + 32000 - 2000, home.OriginYM + 32000 - 3000);
        m.WaitIdle();
        Assert.True(m.IsReady(home.Offset(1, 1)) && m.IsReady(home.Offset(0, 1)));
    }

    [Fact]
    public void Chunks_across_a_border_come_from_the_neighbour_and_are_neutral_until_it_is_ready()
    {
        var w = SeamWorld.Get(1234);
        var home = w.BlockOrigin.Offset(1, 1);
        var m = NewManager(w, home);
        var s = new ChunkStreamer(m, home);
        // the chunk just east of the home region is neutral ground while the region is missing...
        var c = s.LoadSync(1000, 500);
        Assert.Equal(1, s.NeutralCount);
        Assert.Equal(Ravensguard.Sim.Live.GroundKind.Grass, TileCode.Ground(c.TileAt(5, 5, 0)));
        // ...and a chunk west of the continent edge of the home region is a real chunk of the home region
        Assert.NotNull(s.GeneratorOfChunk(999, 500));
        Assert.Null(s.GeneratorOfChunk(1000, 500));
        m.GenerateNow(home.Offset(1, 0));
        int v0 = c.Version;
        s.Update(1000, 500, 50);
        var real = s.TryGet(1000, 500)!;
        Assert.Equal(0, s.NeutralCount);
        Assert.True(real.Version > v0, "the replaced chunk has a new version so a window stamps it again");
        // the content equals what the neighbour's own generator makes for its chunk (0, 500)
        var direct = m.TryGet(home.Offset(1, 0))!.Generator.GenerateLevels(0, 500);
        Assert.Equal(direct.Levels[0].Tiles.Count(t => t.Value != 0), real.Level(0).Tiles.Count(t => t != 0));
        // the store records the chunk under its world coordinates (frame of the home region)
        Assert.True(s.Store.Contains(new Ravensguard.Sim.Save.ChunkKey(1000, 500, 0)));
    }

    [Fact]
    public void A_road_gate_is_a_road_cell_on_both_sides_of_the_border()
    {
        var w = SeamWorld.Get(1234);
        var home = w.Block3().Where(r => r.X < w.BlockOrigin.X + 2).First(r => BorderGates.For(w.History).Of(r, BorderSide.East).Count > 0);
        var m = NewManager(w, home);
        m.GenerateNow(home.Offset(1, 0));
        var s = new ChunkStreamer(m, home);
        var gates = BorderGates.For(w.History).Of(home, BorderSide.East);
        Assert.NotEmpty(gates);
        int roadBoth = 0;
        foreach (var g in gates)
        {
            int wy = g.Tile * 64 + 32;
            // 5 m either side of the border at the gate row
            var west = TileCode.Ground(s.LoadSync(999, wy / 32).TileAt(32 - 3, wy % 32, 0));
            var east = TileCode.Ground(s.LoadSync(1000, wy / 32).TileAt(2, wy % 32, 0));
            bool Road(GroundKind k) => k is GroundKind.Dirt or GroundKind.Cobble or GroundKind.Gravel or GroundKind.Bridge or GroundKind.Ford;
            output.WriteLine($"gate tile {g.Tile} class {g.Class}: west {west} east {east}; roadtile A {w.Region(home).Road[g.Tile * 500 + 499]} B {w.Region(home.Offset(1, 0)).Road[g.Tile * 500]}");
            if (Road(west) && Road(east)) roadBoth++;
        }
        output.WriteLine($"{roadBoth} of {gates.Count} east gates of {home} show road ground 3 m either side of the border");
        Assert.True(roadBoth * 2 >= gates.Count, "most gates carry a road on both sides");
    }

    // ------------------------------------------------------------------ the walk

    // ------------------------------------------------------------------ the walk

    /// <summary>Nodes of a region's road graph with the shortest road distance from a source node (Dijkstra over edge lengths).</summary>
    private static (long[] Dist, int[] Via) Roads(RegionData r, int src)
    {
        int n = r.RoadNodes.Count;
        var adj = new List<(int To, int Edge)>[n];
        for (int i = 0; i < n; i++) adj[i] = [];
        foreach (var e in r.RoadEdges) { adj[e.A].Add((e.B, e.Id)); adj[e.B].Add((e.A, e.Id)); }
        var d = new long[n]; Array.Fill(d, long.MaxValue); var via = new int[n]; Array.Fill(via, -1); d[src] = 0;
        var pq = new PriorityQueue<int, long>(); pq.Enqueue(src, 0);
        while (pq.TryDequeue(out int u, out long du))
        {
            if (du > d[u]) continue;
            foreach (var (v, ei) in adj[u])
            {
                // a bridge is avoided where any other way exists (a deck at a bend of a wide river can be a dead end for a walker; fords are fine)
                var e = r.RoadEdges[ei];
                long nd = du + e.LengthM + (e.Crossings.Any(c => c.Kind == CrossingKind.Bridge) ? 5_000_000 : 0);
                if (nd < d[v]) { d[v] = nd; via[v] = ei; pq.Enqueue(v, nd); }
            }
        }
        return (d, via);
    }

    /// <summary>The road from <paramref name="src"/> to <paramref name="dst"/> as points in continent metres.</summary>
    private static List<(long X, long Y)> RoadPath(RegionData r, RegionCoord at, int src, int dst, (long[] Dist, int[] Via) t)
    {
        var edges = new List<(int Edge, bool Forward)>();
        for (int cur = dst; cur != src;)
        {
            var e = r.RoadEdges[t.Via[cur]];
            bool toB = e.B == cur; // walked A -> B on the way to cur
            edges.Add((e.Id, toB));
            cur = toB ? e.A : e.B;
        }
        edges.Reverse();
        var pts = new List<(long, long)>();
        foreach (var (ei, fwd) in edges)
        {
            var e = r.RoadEdges[ei];
            var seq = fwd ? e.Points : e.Points.Reverse().ToArray();
            foreach (var p in seq) pts.Add((at.OriginXM + p.X, at.OriginYM + p.Y));
        }
        return pts;
    }

    /// <summary>A route along real roads: starts on the road of the first east gate of <paramref name="home"/> about <paramref name="leadM"/> metres before it, crosses region 2 gate to gate, and ends <paramref name="tailM"/> metres into region 3.</summary>
    private static List<(long X, long Y)>? RoadRoute(SeamWorld w, RegionCoord home, int leadM, int tailM)
    {
        var r1 = w.Region(home); var r2 = w.Region(home.Offset(1, 0)); var r3 = w.Region(home.Offset(2, 0));
        foreach (var g1 in r1.Gates.Where(g => g.Side == BorderSide.East).OrderByDescending(g => g.Class))
        {
            int n1 = r1.RoadNodes.FindIndex(n => n.Kind == RoadNodeKind.Gate && n.X == RegionSettings.RegionM && n.Y / 64 == g1.Tile);
            int w2 = r2.RoadNodes.FindIndex(n => n.Kind == RoadNodeKind.Gate && n.X == 0 && n.Y / 64 == g1.Tile);
            if (n1 < 0 || w2 < 0) continue;
            // region 1: the farthest node within reach of the lead distance, then back to the gate
            var t1 = Roads(r1, n1);
            int s1 = Enumerable.Range(0, r1.RoadNodes.Count).Where(i => t1.Dist[i] != long.MaxValue && t1.Dist[i] <= leadM).OrderByDescending(i => t1.Dist[i]).First();
            if (t1.Dist[s1] < leadM / 3) continue;
            var leg1 = RoadPath(r1, home, n1, s1, t1); leg1.Reverse(); // gate -> start, reversed: start -> gate
            // region 2: west gate to the nearest reachable east gate of region 2 that has a mirror in region 3
            var t2 = Roads(r2, w2);
            var east = r2.RoadNodes.Where(n => n.Kind == RoadNodeKind.Gate && n.X == RegionSettings.RegionM && t2.Dist[n.Id] != long.MaxValue
                && r3.RoadNodes.Any(m => m.Kind == RoadNodeKind.Gate && m.X == 0 && m.Y == n.Y)).OrderBy(n => t2.Dist[n.Id]).ToList();
            if (east.Count == 0) continue;
            var e2 = east[0];
            if (t2.Dist[e2.Id] >= 5_000_000) continue; // every way across region 2 uses a bridge
            var leg2 = RoadPath(r2, home.Offset(1, 0), w2, e2.Id, t2);
            int w3 = r3.RoadNodes.First(m => m.Kind == RoadNodeKind.Gate && m.X == 0 && m.Y == e2.Y).Id;
            var t3 = Roads(r3, w3);
            int s3 = Enumerable.Range(0, r3.RoadNodes.Count).Where(i => t3.Dist[i] != long.MaxValue && t3.Dist[i] <= tailM).OrderByDescending(i => t3.Dist[i]).First();
            var leg3 = RoadPath(r3, home.Offset(2, 0), w3, s3, t3);
            var all = new List<(long X, long Y)>(leg1); all.AddRange(leg2); all.AddRange(leg3);
            return all;
        }
        return null;
    }

    private static List<(long X, long Y)> Resample(List<(long X, long Y)> pts, int stepM)
    {
        var res = new List<(long X, long Y)> { pts[0] };
        double carry = 0;
        for (int i = 1; i < pts.Count; i++)
        {
            double dx = pts[i].X - pts[i - 1].X, dy = pts[i].Y - pts[i - 1].Y, len = Math.Sqrt(dx * dx + dy * dy);
            if (len < 0.5) continue;
            double at = stepM - carry;
            for (; at <= len; at += stepM) res.Add((pts[i - 1].X + (long)(dx * at / len), pts[i - 1].Y + (long)(dy * at / len)));
            carry = len - (at - stepM);
        }
        res.Add(pts[^1]);
        return res;
    }

    [Fact]
    public void Walking_along_the_roads_at_8x_through_three_regions_never_stops_and_keeps_every_tick_short()
    {
        // three regions in a row joined by road gates, found from the (cheap, pure) gate lists: the first triple, over both test seeds, with a bridge-free road across the middle one
        SeamWorld? w = null; RegionCoord home = default; List<(long X, long Y)>? route = null;
        int tried = 0;
        foreach (ulong seed in new[] { 1234UL, 1UL })
        {
            var ws = SeamWorld.Get(seed);
            var gates = BorderGates.For(ws.History);
            for (int id = 0; id < 256 && route == null && tried < 14; id++)
            {
                var h = RegionCoord.FromId(id);
                if (h.X > 13 || gates.Of(h, BorderSide.East).Count == 0 || gates.Of(h.Offset(1, 0), BorderSide.East).Count == 0) continue;
                bool Land(RegionCoord r) { int land = 0; for (int y = 0; y < 16; y++) for (int x = 0; x < 16; x++) if (!ws.Continent.Ocean[(r.CellY + y) * Continent.Size + r.CellX + x]) land++; return land > 200; }
                if (!Land(h) || !Land(h.Offset(1, 0)) || !Land(h.Offset(2, 0))) continue;
                tried++;
                var rt = RoadRoute(ws, h, 4500, 1500);
                if (rt != null) { route = rt; w = ws; home = h; }
            }
            if (route != null) break;
        }
        Assert.NotNull(route);
        var path = Resample(route!, 30);
        double routeM = 0; for (int i = 1; i < route!.Count; i++) routeM += Math.Sqrt(Math.Pow(route[i].X - route[i - 1].X, 2) + Math.Pow(route[i].Y - route[i - 1].Y, 2));
        output.WriteLine($"seed {w!.Seed}: road route of {routeM / 1000:0.0} km from {home} through {home.Offset(1, 0)} into {home.Offset(2, 0)}");

        var regions = new RegionManager(w.Continent, w.History, w.Settings, w.Content);
        regions.GenerateNow(home);
        var streamer = new ChunkStreamer(regions, home);
        int ox = home.OriginXM, oy = home.OriginYM;
        var start = RegionSpawn.NearestWalkable(streamer, (int)(path[0].X - ox), (int)(path[0].Y - oy), 12, paved: false);
        Assert.NotNull(start);
        var live = new LiveWorld(streamer, start!.Value, GameTime.FromCalendar(1, Season.Spring, 8, 12)) { SynchronousStreaming = true };
        live.SetSpeed(SimSpeed.X8);
        long border1 = home.OriginXM + 32000 - ox, border2 = home.OriginXM + 64000 - ox; // world x of the two borders
        double endX = path[^1].X - ox;

        var bot = new RoadWalker(live, path.Select(p => (X: p.X - ox, Y: p.Y - oy)).ToList());
        var times = new List<double>(40000);
        var xs = new List<double>(40000);
        var readyAt = new Dictionary<int, (double X, double Border)>();
        int lastRev = regions.Revision, neutralTicks = 0, ticks = 0, stuck = 0;
        double lastProgress = 0, best = double.MinValue;
        var sw = new Stopwatch();
        var wall = Stopwatch.StartNew();
        while (ticks < 120_000)
        {
            bot.Steer();
            sw.Restart(); live.Step(); double ms = sw.Elapsed.TotalMilliseconds;
            ticks++; times.Add(ms);
            double x = live.OriginX + live.Snapshot.Hero.X.ToDouble(); xs.Add(x);
            if (ticks % 200 == 0 && Environment.GetEnvironmentVariable("WALKLOG") is { } f) File.AppendAllText(f, $"tick {ticks} x={x:0} wall {wall.Elapsed.TotalSeconds:0}s waypoint {bot.Index}/{path.Count} fails {bot.Failures}\n");
            if (regions.Revision != lastRev)
            {
                lastRev = regions.Revision;
                foreach (var g in regions.Ready)
                    if (!readyAt.ContainsKey(g.Coord.Id) && g.Coord != home && g.Coord.Y == home.Y) readyAt[g.Coord.Id] = (x, (g.Coord.X - home.X) * 32000.0);
            }
            int hcx = ChunkStreamer.FloorDiv((int)x, 32), hcy = ChunkStreamer.FloorDiv(live.OriginY + live.Snapshot.Hero.Y.Floor(), 32);
            if (streamer.NeutralCount > 0 && streamer.GeneratorOfChunk(hcx, hcy) == null) neutralTicks++;
            if (regions.IsBusy) Thread.Sleep(50); // while a region builds, tick at the real rate of the game (one tick every 50 ms) so the hero walks only as far as it really would meanwhile
            if (bot.Done) break;
            // give up when the hero has made no progress along the route for a long time (it would never finish)
            if (bot.Index > best) { best = bot.Index; lastProgress = ticks; } else if (ticks - lastProgress > 3000) { stuck = bot.Index; break; }
        }
        Assert.Equal(0, stuck);
        times.Sort();
        double max = times[^1], p99 = times[(int)(times.Count * 0.99)], avg = times.Average();
        // stalls: the longest run of ticks with (almost) no progress within 300 m of each border (the hero walks the road, so any x advance counts)
        int WorstStall(double border)
        {
            int worst = 0, run = 0;
            for (int i = 1; i < xs.Count; i++)
            {
                if (Math.Abs(xs[i] - border) > 300) { run = 0; continue; }
                if (xs[i] - xs[i - 1] < 0.05) worst = Math.Max(worst, ++run); else run = 0;
            }
            return worst;
        }
        int stall1 = WorstStall(border1), stall2 = WorstStall(border2);
        output.WriteLine($"8x walk {routeM / 1000:0.0} km in {ticks} ticks ({ticks / 20 / 60.0:0.0} game-real minutes at 20 Hz): avg {avg:0.00} ms, p99 {p99:0.00} ms, max {max:0.00} ms; window swaps {live.WindowSwaps}; chunks generated {streamer.GeneratedCount}");
        output.WriteLine($"stall (ticks with no progress) within 300 m of border 1: {stall1}, border 2: {stall2}; ticks standing on neutral ground: {neutralTicks}; replans {bot.Replans}, {bot.Failures} without a path");
        foreach (var (id, (x, border)) in readyAt.OrderBy(k => k.Key))
            output.WriteLine($"region {RegionCoord.FromId(id)} became ready with the hero at x={x:0} ({border - x:0} m before its west edge); built {regions.TryGet(RegionCoord.FromId(id))!.BuildMs:0} ms");
        output.WriteLine($"region manager: {regions.Stats}");
        Assert.True(bot.Done, "the hero reached the end of the route");
        Assert.True(regions.IsReady(home.Offset(1, 0)) && regions.IsReady(home.Offset(2, 0)), "both neighbours were generated on the way");
        // ready well ahead: queued within the 4 km trigger (or at the start, when the road begins closer) and ready before the hero had walked 150 m more
        double startLead = border1 - xs[0];
        var r1 = readyAt[home.Offset(1, 0).Id];
        Assert.True(r1.Border - r1.X >= Math.Min(3000, startLead) - 200, $"the first neighbour was ready {r1.Border - r1.X:0} m before the border");
        var r2 = readyAt[home.Offset(2, 0).Id];
        Assert.True(r2.Border - r2.X >= 3000, $"the second neighbour was ready {r2.Border - r2.X:0} m before its border");
        Assert.Equal(0, neutralTicks);
        Assert.True(stall1 < 40 && stall2 < 40, $"stalled {stall1}/{stall2} ticks at the borders");
        Assert.True(p99 < TestBudget.Ms(60), $"99th percentile tick {p99:0.0} ms");
        Assert.True(max < TestBudget.Ms(250), $"slowest tick {max:0.0} ms");
        // hashes unchanged by the walk: the regions it generated equal fresh ones
        Assert.Equal(w.Fresh(home.Offset(1, 0)).ComputeHash(), regions.TryGet(home.Offset(1, 0))!.Region.ComputeHash());
    }

    /// <summary>Follows a road polyline (world metres): paths to a point about 60 m ahead of the nearest waypoint, re-planning every second.</summary>
    private sealed class RoadWalker(LiveWorld live, List<(long X, long Y)> path)
    {
        private int _tick;
        public int Index { get; private set; }
        public int Replans, Failures;
        public bool Done => Index >= path.Count - 2;

        public void Steer()
        {
            _tick++;
            var h = live.Snapshot.Hero;
            double wx = live.OriginX + h.X.ToDouble(), wy = live.OriginY + h.Y.ToDouble();
            // advance past every waypoint the hero is within 20 m of (never backwards)
            while (Index < path.Count - 1 && Math.Abs(path[Index].X - wx) < 20 && Math.Abs(path[Index].Y - wy) < 20) Index++;
            // or the nearest waypoint ahead when the hero ended up beside the road
            for (int k = Index; k < Math.Min(path.Count, Index + 6); k++) if (Math.Abs(path[k].X - wx) < 12 && Math.Abs(path[k].Y - wy) < 12) Index = Math.Max(Index, k + 1);
            if (h.PathRemaining > 4 && _tick % 20 != 0) return;
            Replans++;
            for (int ahead = Math.Min(path.Count - 1, Index + 2); ahead >= Index; ahead--)
            {
                int tx = (int)(path[ahead].X - live.OriginX), ty = (int)(path[ahead].Y - live.OriginY);
                for (int r = 0; r <= 3; r++)
                    for (int dy = -r; dy <= r; dy++)
                        for (int dx = -r; dx <= r; dx++)
                        {
                            if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != r) continue;
                            int cx = tx + dx, cy = ty + dy;
                            if ((uint)cx >= (uint)live.Grid.Width || (uint)cy >= (uint)live.Grid.Height || !live.Grid.IsOpen(cx, cy, 0)) continue;
                            if (live.CommandMoveTo(new Cell3(cx, cy, 0))) { live.SetInput(MoveInput.None); return; }
                        }
            }
            Failures++;
            live.SetInput(new MoveInput(Math.Sign(path[Math.Min(path.Count - 1, Index + 1)].X - wx) is var sx && sx != 0 ? (int)sx : 0, Math.Sign(path[Math.Min(path.Count - 1, Index + 1)].Y - wy) is var sy && sy != 0 ? (int)sy : 0, Sprint: true));
        }
    }
}
