using System.Diagnostics;
using Ravensguard.Sim.Construction;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Goods;
using Xunit.Abstractions;

namespace Ravensguard.Sim.Tests.Construction;

/// <summary>BC-21: thirty sites built by paid crews and half of them taken down again (one by a collapse) over a game year; coin and mass exact, no ghost lost or doubled.</summary>
public class ConstructionSoakTests(ITestOutputHelper output)
{
    [Fact, Trait("Category", "Soak")]
    public void BC21_ThirtySitesBuilt_HalfDemolished_OverAGameYear_CoinAndMassExact_NoGhostLostOrDuplicated()
    {
        var r = ConstructionRig.Make(crowns: 20_000);
        long t = ConstructionRig.Day10 - 3600;
        var placed = new HashSet<EntityId>();
        var sites = new List<EntityId>();
        for (int i = 0; i < 30; i++)
        {
            int ox = 2000 + (i % 6) * 40, oy = 2000 + (i / 6) * 40;
            // a shed: wattle or timber walls round a 4 x 3 floor, a door, a roof; every third has a loft floor on a post
            var cells = new List<PlaceCell>();
            string wall = i % 2 == 0 ? "wall_wattle" : "wall_timber";
            for (int y = 0; y < 5; y++) for (int x = 0; x < 6; x++)
                {
                    bool edge = x == 0 || y == 0 || x == 5 || y == 4;
                    cells.Add(new PlaceCell(ox + x, oy + y, 0, edge ? (x == 2 && y == 4 ? "door_wood" : wall) : "floor_packed_earth"));
                }
            var pr = r.W.Place(r.Hero, PurseKind.Person, -1, cells, t);
            Assert.True(pr.Ok && pr.Refused.Count == 0, string.Join(",", pr.Refused.Select(x => x.Key)));
            var roof = new List<PlaceCell>();
            for (int y = 0; y < 5; y++) for (int x = 0; x < 6; x++) roof.Add(new PlaceCell(ox + x, oy + y, 1, "roof_thatch"));
            if (i % 3 == 0) roof.Add(new PlaceCell(ox + 10, oy, 0, "post_timber"));
            var pr2 = r.W.Place(r.Hero, PurseKind.Person, -1, roof, t, pr.Site);
            Assert.True(pr2.Ok && pr2.Refused.Count == 0, string.Join(",", pr2.Refused.Select(x => x.Key)));
            if (i % 3 == 0) Assert.True(r.W.Place(r.Hero, PurseKind.Person, -1, [new PlaceCell(ox + 10, oy, 1, "floor_plank"), new PlaceCell(ox + 10, oy + 1, 1, "floor_plank")], t, pr.Site).Ok);
            foreach (var g in r.W.GhostsOf(pr.Site)) placed.Add(g.Id);
            r.StockAll(pr.Site);
            for (int k = 0; k < 2; k++) Assert.True(r.W.AddWorker(pr.Site, EntityId.Make(IdKind.Person, 3_000_000UL + (ulong)(i * 4 + k)), -1, CrewClass.Labourer, 150, t).Ok);
            sites.Add(pr.Site);
        }
        Assert.Equal(30, sites.Distinct().Count());
        int ghostsPlaced = placed.Count;
        var sw = Stopwatch.StartNew();
        long yearEnd = t + 60L * 86400;
        bool demolished = false;
        int took = 0;
        for (; t < yearEnd; t += 3600)
        {
            r.W.AdvanceTo(t);
            if (!demolished && sites.All(s => !r.W.GhostsOf(s).Any()))
            {
                demolished = true;
                Assert.Equal(ghostsPlaced, r.W.Book.Built.Count);                      // every ghost became one built piece
                // half the sites are taken down by hand (fixtures none; roofs first, then the rest), and one loses its loft's post at once
                for (int i = 0; i < 30; i += 2)
                {
                    var s = r.W.Site(sites[i])!;
                    foreach (var b in r.W.Book.Built.Values.Where(b => b.Site == s.Id).OrderByDescending(b => b.Z).ThenBy(b => b.SlotKey).ToList())
                        if (r.W.MarkDeconstruct(r.Hero, PurseKind.Person, -1, b.X, b.Y, b.Z, b.Slot, t).Ok) took++;
                    if (r.W.Site(sites[i])!.Crew.Count == 0)
                        for (int k = 0; k < 2; k++) r.W.AddWorker(s.Id, EntityId.Make(IdKind.Person, 3_100_000UL + (ulong)(i * 4 + k)), -1, CrewClass.Labourer, 150, t);
                }
                var post = r.W.Book.Built.Values.First(b => b.Piece == "post_timber" && r.W.Site(b.Site)!.Id == sites[3]);
                Assert.True(r.W.Destroy(post.X, post.Y, post.Z, t, EntityId.None, "soak").Ok);
                Assert.Single(r.W.Book.Groans);
            }
        }
        sw.Stop();
        Assert.True(demolished, "the thirty sites were not built within the year");
        output.WriteLine($"a game year of 30 sites in {sw.ElapsedMilliseconds} ms; {took} pieces marked to come down; {r.W.Book.Built.Count} pieces stand");
        Assert.Empty(r.W.Book.Ghosts);                                                   // everything marked came down
        Assert.Empty(r.W.Book.Groans);
        Assert.Equal(ghostsPlaced - took - 3, r.W.Book.Built.Count);                     // the post went, and the two loft floors on it fell
        // no two pieces on one slot; every ghost id distinct
        Assert.Equal(r.W.Book.Built.Count, r.W.Book.Built.Values.Select(b => b.SlotKey).Distinct().Count());
        Assert.Equal(ghostsPlaced, placed.Count);
        r.AssertMass(); r.AssertCoin();
        long paid = r.W.Book.Sites.Values.Sum(s => s.PaidMites);
        Assert.True(paid > 0);
        output.WriteLine($"wages paid {paid} mites, refunded {r.W.Book.Sites.Values.Sum(s => s.RefundedMites)}");
    }
}
