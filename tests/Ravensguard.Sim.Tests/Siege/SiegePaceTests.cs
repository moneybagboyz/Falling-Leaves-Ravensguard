using Ravensguard.Content;
using Ravensguard.Sim.Building;
using Ravensguard.Sim.Construction;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Goods;
using Ravensguard.Sim.Save;
using Ravensguard.Sim.Siege;
using Ravensguard.Sim.Tests.Economy;
using Ravensguard.Sim.World;
using Ravensguard.Sim.WorldGen.Chunks;
using Xunit.Abstractions;

namespace Ravensguard.Sim.Tests.Siege;

/// <summary>The user's siege pace (overview 10g, 22 Nov 2026): walls fall in days as sieges-and-engines says. One skilled trebuchet at 50 m breaches a 3 m stone curtain in about 3 days (2 to 4 here), a palisade in hours.</summary>
public class SiegePaceTests(ITestOutputHelper output)
{
    /// <summary>Days (of 24 game hours) a trebuchet at <paramref name="rangeM"/> with the crew's Engineering <paramref name="skill"/> takes to open a breach, shooting the abstract tier's hours (06:00 to 18:00).</summary>
    internal static (double Days, int Shots, long Hours) Breach(EngineTests.Field f, int rangeM, int skill, long aimX, long aimY, Func<bool> breached, int maxDays = 400)
    {
        f.W.Engineering = _ => skill;
        var (_, e) = f.Raise("trebuchet", 45_500, 40_500 - rangeM * 1000L);
        f.Book.Assign(e, Enumerable.Range(0, 8).Select(i => EntityId.Make(IdKind.Person, 700UL + (ulong)i)));
        var eco = EconFixture.NewEconomy(false); var store = eco.OpenStore(StoreKind.House, EntityId.Make(IdKind.Person, 9500), 0, 0, false);
        store.Create(eco.Goods.IdOf("stone_raw"), 4_000_000_000L, CreateReason.Import); f.W.AmmoOf = _ => store; f.W.Goods = eco.Goods;
        Assert.Equal(AimResult.Ok, f.Book.Aim(e, aimX, aimY));
        long h = 0; int shots = 0;
        for (; h < 24L * maxDays && !breached(); h++) shots += f.Book.Hour(h, f.W);
        Assert.True(breached(), $"no breach in {maxDays} days");
        return (h / 24.0, shots, h);
    }

    /// <summary>A palisade one cell thick along x = 36..55 at y = 40 (a ring of stakes: the cheapest wall).</summary>
    internal static EngineTests.Field PalisadeField()
    {
        var db = EconFixture.Content;
        var f = EngineTests.Field.Make();
        var pal = new PiecePalette(PieceCosts.Of(db).Library.Pieces);
        var store = new ChunkStore();
        var ground = new ChunkContent();
        for (int i = 0; i < ChunkContent.Size * ChunkContent.Size; i++) ground.Tiles[i] = TileCode.Make(Ravensguard.Sim.Live.GroundKind.Dirt, 0, 0, 0);
        for (int x = 36; x <= 55; x++) ground.Tiles[ChunkContent.TileIndex(x - 32, 40 - 32)] = TileCode.Make(Ravensguard.Sim.Live.GroundKind.Dirt, 0, pal.Of("palisade"), 0);
        Assert.True(store.RecordFirstVisit(new ChunkKey(1, 1, 0), 1, "h0", ground));
        var rig = WallRig.Make(db, 1234, store, stamp: false);
        return new EngineTests.Field { Rig = rig, B = f.B, Book = f.Book, Shots = f.Shots, W = new EngineWorld { Walls = rig.W, Engineering = _ => 0, OnShot = (e, r) => f.Shots.Add(r) } };
    }

    [Fact]
    public void ASkilledTrebuchetAt50m_BreachesA3mStoneCurtainInAboutThreeDays_AndAPalisadeInHours()
    {
        // the curtain: the SG-B worked example, aimed at the shared edge of two columns, the middle of the thickness
        var f = EngineTests.Field.Make();
        var (days, shots, hours) = Breach(f, 50, 60, 46_000, 41_500, () => f.Rig.W.BreachesNear(45, 41).Count > 0);
        output.WriteLine($"stone curtain, 50 m, Engineering 60: {shots} shots, {hours} hours = {days:0.0} days of one trebuchet ({shots / 18.0:0.0} days at 18 shots a day)");
        Assert.InRange(days, 2.0, 4.0);

        // a palisade: 500 hp a stake wall, one cell thick
        var p = PalisadeField();
        var (pd, ps, ph) = Breach(p, 50, 60, 46_000, 40_500, () => p.Rig.W.BreachesNear(45, 40).Count > 0);
        output.WriteLine($"palisade, 50 m, Engineering 60: {ps} shots, {ph} hours");
        Assert.True(ph <= 18, $"{ph} hours");
    }

    [Fact]
    public void ThePaceByRangeAndSkill_IsReported()
    {
        foreach (var (range, skill) in new[] { (50, 0), (50, 60), (120, 0), (120, 60) })
        {
            var f = EngineTests.Field.Make();
            var (days, shots, hours) = Breach(f, range, skill, 46_000, 41_500, () => f.Rig.W.BreachesNear(45, 41).Count > 0);
            output.WriteLine($"range {range} m, Engineering {skill}: {shots} shots, {days:0.0} days");
        }
    }
}
