using Ravensguard.WorldTool;
using Xunit.Abstractions;

namespace Ravensguard.Sim.Tests.Review;

/// <summary>Phase 1 done-when (plan section 1): towns stand 6-10 km apart, and walking between them takes the time the plan says.</summary>
public class TownSpacingTests(ITestOutputHelper output)
{
    public static IEnumerable<object[]> Seeds => new ulong[] { 1, 2, 7, 42, 1234 }.Select(s => new object[] { s });

    private static List<(double Straight, double Road)> Links(ulong seed)
    {
        var r = TestWorld.Get(seed).Region;
        var towns = r.Towns.ToList();
        int n = r.RoadNodes.Count;
        var adj = new List<(int To, int Len)>[n];
        for (int i = 0; i < n; i++) adj[i] = [];
        foreach (var e in r.RoadEdges) { adj[e.A].Add((e.B, e.LengthM)); adj[e.B].Add((e.A, e.LengthM)); }
        long[] Shortest(int src)
        {
            var d = new long[n]; Array.Fill(d, long.MaxValue); d[src] = 0;
            var pq = new PriorityQueue<int, long>(); pq.Enqueue(src, 0);
            while (pq.TryDequeue(out int u, out long du))
            {
                if (du > d[u]) continue;
                foreach (var (v, w) in adj[u]) if (du + w < d[v]) { d[v] = du + w; pq.Enqueue(v, d[v]); }
            }
            return d;
        }
        var res = new List<(double, double)>();
        foreach (var t in towns)
        {
            var near = towns.Where(o => o != t).OrderBy(o => Math.Pow(t.CenterX - o.CenterX, 2) + Math.Pow(t.CenterY - o.CenterY, 2)).First();
            double straight = Math.Sqrt(Math.Pow(t.CenterX - near.CenterX, 2) + Math.Pow(t.CenterY - near.CenterY, 2));
            res.Add((straight, Shortest(t.RoadNode)[near.RoadNode]));
        }
        return res;
    }

    [Fact]
    public void Most_nearest_town_links_are_six_to_ten_kilometres_across_the_auto_picked_regions()
    {
        int good = 0, total = 0;
        foreach (ulong seed in new ulong[] { 1, 2, 7, 42, 1234 })
        {
            var links = Links(seed);
            Assert.True(links.Count >= 3, $"seed {seed}: {links.Count} towns");
            int g = links.Count(l => l.Straight is >= 6000 and <= 10000);
            good += g; total += links.Count;
            output.WriteLine($"seed {seed}: nearest-town km {string.Join(", ", links.Select(l => (l.Straight / 1000).ToString("0.0")))} ({g}/{links.Count})");
        }
        output.WriteLine($"{good}/{total} = {100.0 * good / total:0}%");
        Assert.True(good * 100 >= total * 75, $"only {good} of {total} nearest-town links are 6-10 km");
    }

    [Theory, MemberData(nameof(Seeds))]
    public void Walking_between_neighbouring_towns_takes_what_the_plan_says(ulong seed)
    {
        // plan section 1: 6-10 km at the 3 m/s jog = 33-56 real minutes; the road route has to land within 10% of that
        foreach (var (straight, road) in Links(seed))
        {
            Assert.True(road < long.MaxValue / 2, $"seed {seed}: a town is not joined to its neighbour by road");
            double minutes = road / 3.0 / 60.0;
            Assert.InRange(minutes, 6000 / 3.0 / 60.0 * 0.9, 10000 / 3.0 / 60.0 * 1.1);
            Assert.True(road >= straight - 1, "the road cannot be shorter than the straight line");
        }
    }

    [Fact]
    public void The_tools_say_clearly_when_base_content_is_missing()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => BaseContent.Require(() => null));
        Assert.Contains("content/base", ex.Message);
        Assert.Equal("some/dir", BaseContent.Require(() => "some/dir"));
    }
}
