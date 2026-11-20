using Ravensguard.Sim.Abstract;
using Ravensguard.Sim.Construction;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Growth;
using Ravensguard.Sim.Jobs;
using Ravensguard.Sim.Save;
using Ravensguard.Sim.Tests.Economy;
using Ravensguard.Sim.Tests.Land;
using Ravensguard.Sim.WorldGen.Chunks;
using Ravensguard.Sim.WorldGen.Region;

namespace Ravensguard.Sim.Tests.Construction;

/// <summary>
/// P5-D: a construction world over a private economy of seed 1234 (its markets, businesses and growth) wired as the runtime wires it: the land rule, the job board for hauls, the region's
/// builders and markets, and growth made visible over a chunk store the test marks as visited. The clock is driven hour by hour as the abstract tick drives it (hauls, then the crews).
/// </summary>
internal sealed class ContractRig : IDisposable
{
    public required LandRig L { get; init; }
    public required ConstructionWorld W { get; init; }
    public required ChunkStore Chunks { get; init; }
    public required ChunkGenerator Gen { get; init; }
    public JobTable Jobs { get; } = new();
    public EntityId Hero => EconomyState.HeroOwner;
    public GrowthSystem Growth => L.W.Economy.Growth;
    public Ledger Led => L.Led;

    public static ContractRig Make(int crowns = 2000)
    {
        var lr = LandRig.Make(crowns);
        var cw = new ConstructionWorld(new ConstructionBook(), lr.W.Econ, PieceCosts.Of(EconFixture.Content)) { Pop = lr.W.Pop, Content = EconFixture.Content, Seed = 1234 };
        ConstructionWiring.HookLand(cw, lr.L, lr.W.Pop);
        cw.Chronicle = (when, type, subjects, causes, data) => lr.C(when, type, subjects, causes, data);
        cw.PayeeOf = p => lr.Led.TryFind(PurseKind.Person, p, out var pp) ? pp
            : lr.W.Pop.People.IndexOf(p) is int i and >= 0 && lr.W.Pop.People.Household[i] is int h and >= 0 ? lr.W.Econ.HouseholdPurse[h] : null;
        var store = new ChunkStore();
        var rig = new ContractRig { L = lr, W = cw, Chunks = store, Gen = ChunkGenerator.For(lr.W.Pop.Region, lr.W.Pop, EconFixture.Content) };
        var env = new JobEnv { Ledger = lr.Led, Econ = lr.W.Econ, Pop = lr.W.Pop, Tuning = Ravensguard.Sim.Behaviour.BehaviourContent.From(EconFixture.Content).OddJobs };
        cw.Jobs = () => (env, rig.Jobs);
        ConstructionWiring.HookEconomy(cw, lr.W.Economy, store, EconFixture.Content);
        // built pieces into the visited chunks as deltas, and growth's finished lots too (the runtime's sinks)
        cw.Tiles = new ChunkTileSink(store, rig.Gen.Palette);
        lr.W.Economy.Growth.ChunkSink = new ChunkDeltaWriter(rig.Gen, store);
        return rig;
    }

    /// <summary>Hour by hour to <paramref name="toSec"/>, as the construction's hourly system: haul jobs, then the crews (stages and the board at each hour's start), then growth's look.</summary>
    public void Run(ref long t, long toSec)
    {
        while (t < toSec)
        {
            t = Math.Min(toSec, (t / 3600 + 1) * 3600);
            W.AdvanceHauls(t);
            W.AdvanceTo(t);
            W.GrowthSites?.LinkSeen(new GameTime(t));
        }
    }

    /// <summary>Marks every chunk under a rectangle as visited (a first-visit snapshot of the chunk as generated now).</summary>
    public void Visit(RectM r)
    {
        for (int cy = ChunkStreamer.FloorDiv(r.Y, 32); cy <= ChunkStreamer.FloorDiv(r.Bottom - 1, 32); cy++)
            for (int cx = ChunkStreamer.FloorDiv(r.X, 32); cx <= ChunkStreamer.FloorDiv(r.Right - 1, 32); cx++)
            {
                var key = new ChunkKey(cx, cy, 0);
                if (Chunks.GetSnapshot(key) != null) continue;
                var g = Gen.GenerateLevels(cx, cy);
                Chunks.RecordFirstVisit(key, ChunkGenerator.Version, g.ContentHash, g.Levels[0]);
            }
    }

    public RegionOutbox Outbox() => new(L.W.Sim.Ids);

    public void AssertCoin() => Assert.Equal(0, Led.Audit().Imbalance.Mites);

    /// <summary>Every good: stores hold what the mass ledger made less what it destroyed; construction's own count equals what stands built.</summary>
    public void AssertMass()
    {
        var e = L.W.Econ;
        Assert.True(e.MassAuditHolds(), "the mass ledger equals the goods in every store");
        var built = new SortedDictionary<ushort, long>();
        foreach (var b in W.Book.Built.Values) foreach (var (good, grams) in W.Costs[b.Piece].Materials) built[e.Goods.IdOf(good)] = built.GetValueOrDefault(e.Goods.IdOf(good)) + grams;
        foreach (var k in W.Book.MaterialisedG.Keys.Union(W.Book.DematerialisedG.Keys).Union(built.Keys))
            Assert.Equal(built.GetValueOrDefault(k), W.Book.MaterialisedG.GetValueOrDefault(k) - W.Book.DematerialisedG.GetValueOrDefault(k));
    }

    public void Dispose() => L.Dispose();
}
