using Ravensguard.Content;
using Ravensguard.Sim.Animals;
using Ravensguard.Sim.Carts;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Goods;
using Ravensguard.Sim.Naval;
using Ravensguard.Sim.Save;
using Ravensguard.Sim.Siege;
using Ravensguard.Sim.Tests.Economy;
using Ravensguard.Sim.World;
using Ravensguard.Sim.WorldGen.Chunks;
using Xunit.Abstractions;

namespace Ravensguard.Sim.Tests.Siege;

/// <summary>P7-SG-B: engines are targets (SG-7), where they roll (pushing by people), the same shots in the live and the abstract tier, and the save of an engine mid-shot (sieges-and-engines 4.2, 7).</summary>
public class EngineDamageTests(ITestOutputHelper output)
{
    private static ContentDatabase Db => EconFixture.Content;
    private static readonly EntityId Lord = EntityId.Make(IdKind.Person, 77);

    /// <summary>The world point of the middle of a cell of a vehicle.</summary>
    private static (long X, long Y) Centre(EngineBook book, VehicleRow v, int cx, int cy)
    {
        var (w, l) = book.Size(v);
        var (cos, sin) = FrameMath.CosSin(v.Facing << 16);
        var (x, y) = FrameMath.ToWorld((int)v.XMm, (int)v.YMm, cos, sin, cx * 1000 + 500 - w * 500, cy * 1000 + 500 - l * 500);
        return (x, y);
    }

    private static IEnumerable<EntityId> People(int n, ulong from = 700) => Enumerable.Range(0, n).Select(i => EntityId.Make(IdKind.Person, from + (ulong)i));

    // ------------------------------------------------------------------ SG-7

    [Fact]
    public void SG7_ASpringaldBoltBreaksATowersWheel_AndTheTowerStops()
    {
        var f = EngineTests.Field.Make(); f.W.Engineering = _ => 100;
        var (tv, te) = f.Raise("siege_tower", 100_000, 0);
        var (sv, se) = f.Raise("springald", 40_000, 0);
        f.Book.Assign(se, People(2)); f.Book.Assign(te, [], People(30, 800));
        Assert.Equal(25, f.Book.SpeedOn(te, Surface.DryRoad, 0).SpeedCmps);                 // pushed by thirty on dry level ground: 0.25 m/s
        var wheel = Centre(f.Book, tv, 0, 0);
        Assert.Equal(AimResult.Ok, f.Book.Aim(se, wheel.X, wheel.Y));
        // one bolt on the cell breaks the wheel (1 kg x 60 m/s = 60 kg m/s: 40 over the part threshold x 5 hit points is 200 against the wheel's 150) and no more than it
        var stack = f.Book.Stack(tv, 0, 0).Where(i => tv.Parts[i].Deck == 0).ToList();                // a bolt flies along the ground level
        Assert.Equal("siege_wheel", f.B.Templates.PartList[tv.Parts[stack[0]].Part].Id);
        Assert.Equal(1, f.Book.StrikeCell(tv, 0, 0, 60, 0, null, fromDeck: 0));
        Assert.Equal(PartState.Broken, tv.Parts[stack[0]].State);
        Assert.Equal(PartState.Built, tv.Parts[stack[1]].State);                           // the axle under it held
        Assert.True(tv.Broken);
        Assert.True(f.Book.SpeedOn(te, Surface.DryRoad, 0).CannotMove);                    // the tower stops: no pushing frees a broken wheel
        Assert.False(f.Book.CanRoll(te));
        // the same through a springald's own shots: with Engineering 100 the cone is a third of a degree and a 600-second reload; mend the wheel, shoot until it breaks again
        f.B.Vehicles.RepairPart(tv, stack[0], 1000);
        Assert.False(tv.Broken);
        long t = 0; int shots = 0;
        while (!tv.Broken && shots < 60) { t += 600; shots += f.Book.Advance(se, t, f.W); }
        output.WriteLine($"a springald broke the wheel with its {shots}th bolt at 60 m");
        Assert.True(tv.Broken); Assert.InRange(shots, 1, 40);
    }

    [Fact]
    public void SG7_FireOnADryPenthouseBurns_OnWetHidesIgnitionIsATenth()
    {
        var f = EngineTests.Field.Make();
        var (v, e) = f.Raise("ram_covered", 0, 0, now: 0);
        f.W.Chronicle = (when, type, key, ps, subjects, causes, data) => new EntityId(1);
        var cell = Centre(f.Book, v, 0, 2);                                                 // 'c': a beam under a penthouse roof under wet hides
        int Roll(bool wet, int n)
        {
            var q = EngineTests.Field.Make(); var (qv, qe) = q.Raise("ram_covered", 0, 0);
            qe.WetUntilSec = wet ? long.MaxValue : 0; int lit = 0;
            for (int i = 0; i < n; i++) if (q.Book.Ignite(qe, cell.X, cell.Y, 100)) lit++;
            return lit;
        }
        int dry = Roll(false, 2000), wet = Roll(true, 2000);
        output.WriteLine($"ignition of 2,000 torches: dry {dry}, under wet hides {wet}");
        Assert.InRange(dry, 1100, 1300);                                                   // 600 permille
        Assert.InRange(wet, 80, 160);                                                      // 60 permille: a tenth
        // a dry cell burns from the top: the hides (80 hp at 40 a minute) in 2 minutes, then the roof (160) in 4, then the beam (250) in 6.25
        e.WetUntilSec = 0; f.Book.Advance(e, 100, f.W);
        while (!f.Book.Ignite(e, cell.X, cell.Y, 100)) { }
        var stack = f.Book.Stack(v, 0, 2);
        Assert.Equal(3, stack.Count);
        string Id(int i) => f.B.Templates.PartList[v.Parts[i].Part].Id;
        Assert.Equal(["hide_cover", "penthouse_roof", "siege_beam"], stack.Select(Id));
        f.Book.Advance(e, 100 + 119, f.W); Assert.Equal(PartState.Built, v.Parts[stack[0]].State);
        f.Book.Advance(e, 100 + 121, f.W); Assert.Equal(PartState.Broken, v.Parts[stack[0]].State); Assert.Equal(PartState.Built, v.Parts[stack[1]].State);
        f.Book.Advance(e, 100 + 120 + 239, f.W); Assert.Equal(PartState.Built, v.Parts[stack[1]].State);
        f.Book.Advance(e, 100 + 120 + 241, f.W); Assert.Equal(PartState.Broken, v.Parts[stack[1]].State);       // the dry penthouse burnt through
        f.Book.Advance(e, 100 + 120 + 240 + 400, f.W); Assert.Equal(PartState.Broken, v.Parts[stack[2]].State);  // and the beam under it
        Assert.Empty(e.Burning);
        // wet hides do not burn at all; the crew douse a fire and the hides are wet again
        var (v2, e2) = f.Raise("ram_covered", 0, 50_000, now: 0);
        e2.WetUntilSec = 3600;
        var c2 = Centre(f.Book, v2, 0, 2);
        Assert.Contains(true, Enumerable.Range(0, 200).Select(_ => f.Book.Ignite(e2, c2.X, c2.Y, 100)).ToList());
        Assert.Equal("penthouse_roof", f.B.Templates.PartList[v2.Parts[e2.Burning[0]].Part].Id);   // the roof under the hides, not the hides
        f.Book.Douse(e2, 200); Assert.Empty(e2.Burning); Assert.Equal(200 + 6 * 3600, e2.WetUntilSec);
        // iron and the ballast never burn
        Assert.DoesNotContain(e2.Burning, i => f.B.Templates.PartList[v2.Parts[i].Part].Tags.Contains("iron"));
    }

    [Fact]
    public void SG7_ABoardStopsMissilesForTheCrew_AndBreaksUnderThem()
    {
        var f = EngineTests.Field.Make();
        var (v, e) = f.Raise("ram_covered", 0, 0);
        Assert.Equal(800, f.Book.CoverPm(e));                                              // the penthouse and its hides all stand
        int stopped = 0; for (int i = 0; i < 1000; i++) if (f.Book.Covered(e, 10_000, 0)) stopped++;      // an arrow (10 kg m/s) is under the part threshold: it only stops
        output.WriteLine($"arrows stopped: {stopped} of 1000");
        Assert.InRange(stopped, 740, 860);
        Assert.Equal(800, f.Book.CoverPm(e));                                              // and no board is hurt
        // a heavy bolt (60 kg m/s) breaks the board it meets: the cover shrinks with its boards
        for (int i = 0; i < 3000; i++) f.Book.Covered(e, 60_000, 0);
        output.WriteLine($"after 3000 bolts the cover is {f.Book.CoverPm(e)} permille");
        Assert.True(f.Book.CoverPm(e) < 800);
        // a mantlet gives its 900 and a tower 700 (content)
        Assert.Equal(900, f.Book.CoverPm(f.Raise("mantlet", 5000, 5000).E));
    }

    [Fact]
    public void ATowerThatLosesItsBaseTopples_AndTheChronicleSaysSoOnce()
    {
        var f = EngineTests.Field.Make(); var chron = new List<(string Type, IReadOnlyList<Cause> Causes)>();
        f.W.Chronicle = (when, type, key, ps, subjects, causes, data) => { chron.Add((type, causes)); return new EntityId(900 + (ulong)chron.Count); };
        var (v, e) = f.Raise("siege_tower", 0, 0);
        Assert.True(f.Book.Works(e));
        int down = 0;
        for (int cy = 0; cy < 4 && !e.Toppled; cy++)
            for (int cx = 0; cx < 4 && !e.Toppled; cx++) { f.Book.StrikeCell(v, cx, cy, 4500, 50, f.W); down++; }
        Assert.True(e.Toppled); Assert.InRange(down, 8, 12);
        Assert.False(f.Book.Works(e)); Assert.True(e.Disabled);
        Assert.True(f.Book.SpeedOn(e, Surface.DryRoad, 0).CannotMove);
        f.Book.StrikeCell(v, 3, 3, 4500, 60, f.W);
        Assert.Single(chron, c => c.Type == "engine_wrecked");
        Assert.All(chron, c => Assert.Equal(CauseKind.Siege, c.Causes[0].Kind));
    }

    [Fact]
    public void TheTowersThreeDecksAreOneWalk_FromTheGroundToTheTopFloorWhereTheDrawbridgeIs()
    {
        var f = EngineTests.Field.Make();
        var (v, e) = f.Raise("siege_tower", 0, 0);
        var dm = DeckMap.Build(v.Parts, f.B.Templates, 4, 4);
        Assert.Equal(3, dm.Decks); Assert.Equal(f.Book.Stat(e).Floors, dm.Decks); Assert.Equal(1, f.Book.Stat(e).TopZ);
        var path = dm.Path(new Cell3(1, 0, 0), new Cell3(2, 2, 2));
        Assert.NotNull(path);                                                               // up the ladders, deck by deck
        Assert.Contains(v.Parts, p => p.Deck == 2 && f.B.Templates.PartList[p.Part].Tags.Contains("drawbridge"));
        Assert.Equal(2, v.Parts.Count(p => f.B.Templates.PartList[p.Part].Tags.Contains("ladder")));
    }

    // ------------------------------------------------------------------ moving

    [Fact]
    public void Pushing_ATowerIs0_25MetresASecondOnFirmLevelGround_AndStopsInMudOnASlopeOverFivePercent()
    {
        var f = EngineTests.Field.Make();
        var (tv, te) = f.Raise("siege_tower", 0, 0); f.Book.Assign(te, [], People(30, 800));
        Assert.Equal(25, f.Book.SpeedOn(te, Surface.DryRoad, 0).SpeedCmps);
        Assert.True(f.Book.SpeedOn(te, Surface.MudRoad, 0).CannotMove);
        Assert.True(f.Book.SpeedOn(te, Surface.DeepMud, 0).CannotMove);
        Assert.True(f.Book.SpeedOn(te, Surface.DryRoad, 60).CannotMove);                    // 6%
        Assert.True(f.Book.SpeedOn(te, Surface.DryRoad, 40).SpeedCmps is > 0 and < 25);     // 4%: slower, still moving
        f.Book.Assign(te, [], People(10, 800));
        Assert.True(f.Book.SpeedOn(te, Surface.DryRoad, 0).SpeedCmps < 25);                 // ten pushers are too few for full speed
        f.Book.Assign(te, [], []); Assert.True(f.Book.SpeedOn(te, Surface.DryRoad, 0).CannotMove);
        // a ram: six pushers, half a metre a second
        var (rv, re) = f.Raise("ram_covered", 0, 100_000); f.Book.Assign(re, People(10), People(6, 820));
        int cmps = f.Book.SpeedOn(re, Surface.DryRoad, 0).SpeedCmps;
        Assert.InRange(cmps, 45, 50);
        // the tower moves 13 mm a game second: 100 seconds, 1.3 m, toward the point it is pushed to, facing it
        f.Book.Assign(te, [], People(30, 800));
        long moved = f.Book.Push(te, 0, 50_000, 100, Surface.DryRoad, 0);
        Assert.Equal(1300, moved); Assert.Equal(1300, tv.YMm); Assert.Equal(0, tv.XMm);
        Assert.Equal(16 * 4, tv.Facing);                                                    // +y is a quarter turn
        // a springald is carried by six: a man carries 80 kg
        var (sv, se) = f.Raise("springald", 0, 200_000);
        Assert.True(f.Book.SpeedOn(se, Surface.Grass, 0).CannotMove);
        f.Book.Assign(se, People(2), People(6, 840));
        Assert.Equal(100, f.Book.SpeedOn(se, Surface.Grass, 0).SpeedCmps);
        // a mantlet is pushed by two at a metre a second
        var (mv, me) = f.Raise("mantlet", 0, 300_000); f.Book.Assign(me, [], People(2, 860));
        Assert.InRange(f.Book.SpeedOn(me, Surface.DryRoad, 0).SpeedCmps, 95, 100);
        // a broken axle stops the ram (the cart rule)
        var axle = re.Kind == EngineKind.Ram ? rv.Parts.FindIndex(p => f.B.Templates.PartList[p.Part].Tags.Contains("axle")) : -1;
        f.B.Vehicles.Damage(rv, axle, 999_999_999);
        Assert.True(f.Book.SpeedOn(re, Surface.DryRoad, 0).CannotMove);
    }

    // ------------------------------------------------------------------ the same shots in both tiers

    private static (string Walls, ulong Book, List<(long, long, WallBlowKind)> Shots) Run(bool live)
    {
        var f = EngineTests.Field.Make();
        var (v, e) = f.Raise("trebuchet", 45_500, -70_000);
        var eco = EconFixture.NewEconomy(false); var store = eco.OpenStore(StoreKind.House, EntityId.Make(IdKind.Person, 9500), 0, 0, false);
        store.Create(eco.Goods.IdOf("stone_raw"), 4_000_000_000L, CreateReason.Import); f.W.AmmoOf = _ => store; f.W.Goods = eco.Goods;
        f.Book.Assign(e, People(8)); f.Book.Aim(e, 46_000, 41_500);
        if (live) for (long t = 1; t <= 3 * 86400; t++) f.Book.Advance(e, t, f.W, daylightOnly: true);
        else for (long h = 0; h < 72; h++) f.Book.Hour(h, f.W);
        return (Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(f.Rig.Store.ToSection())), f.Book.Hash(), [.. f.Shots.Select(s => (s.X, s.Y, s.Wall))]);
    }

    [Fact]
    public void TheLiveTierAndTheAbstractHour_LooseTheSameShotsAtTheSameSeconds_AndTheWallsEndTheSame()
    {
        var a = Run(live: false); var b = Run(live: true);
        output.WriteLine($"{a.Shots.Count} shots in three days");
        Assert.InRange(a.Shots.Count, 50, 56);                                              // 18 a day (12 shooting hours at 2,400 seconds a shot)
        Assert.Equal(a.Shots, b.Shots);
        Assert.Equal(a.Book, b.Book);
        Assert.Equal(a.Walls, b.Walls);
    }

    // ------------------------------------------------------------------ SG save: an engine mid-shot

    [Fact]
    public void Save_AnEngineMidShot_SavedLoadedAndContinued_EqualsTheUninterruptedRun_AndNoEngineIsNoSection()
    {
        ulong seed = EconFixture.Shared.Cs.Seed;
        // the world with an engine
        EngineTests.Field Make(out WorldState world, out Store store)
        {
            var econ = EconFixture.NewEconomy(false);
            var rig = WallRig.Make(Db, 1234);
            var rides = new RideBook(Db, econ, seed);
            var book = new EngineBook(Db, rides, seed);
            world = EconFixture.WorldWith(econ); world.Mounts = rides; world.Engines = book;
            var shots = new List<ShotReport>();
            var f = new EngineTests.Field { Rig = rig, B = rides, Book = book, Shots = shots, W = new EngineWorld { Walls = rig.W, Engineering = _ => 40, OnShot = (e, r) => shots.Add(r), Goods = econ.Goods } };
            store = econ.OpenStore(StoreKind.House, EntityId.Make(IdKind.Person, 9500), 0, 0, false);
            store.Create(econ.Goods.IdOf("stone_raw"), 4_000_000_000L, CreateReason.Import);
            Store s = store; f.W.AmmoOf = _ => s;
            var v = rides.Vehicles.Create(rides.Templates.Get("trebuchet"), Lord, 45_500, -70_000);
            var e = book.Raise(v, 0)!; book.Assign(e, People(8)); book.Aim(e, 46_000, 41_500);
            return f;
        }
        var a = Make(out var worldA, out var storeA);
        var ea = a.Book.Engines.Values.Single();
        a.Book.Advance(ea, 9000, a.W);                                                      // three shots away, the fourth wound halfway
        Assert.Equal(3, ea.Fired); Assert.True(ea.ReadyAtSec > 9000);
        var rigBytes = a.Rig.Store.ToSection();
        var file = WorldSave.Capture(worldA, EconFixture.ContentHash, "0.1.0");
        Assert.True(file.Sections.ContainsKey(SiegeSave.SectionName));
        var bytes = SaveCodec.Encode(file);
        var back = WorldSave.Restore(SaveCodec.Decode(bytes), Db, EconFixture.ContentHash);
        Assert.Equal(bytes, SaveCodec.Encode(WorldSave.Capture(back, EconFixture.ContentHash, "0.1.0")));      // save, load, save
        Assert.Equal(a.Book.Hash(), back.Engines!.Hash());
        // the resumed run: the restored book over the restored world, the walls reloaded from their bytes
        var rigB = WallRig.Make(Db, 1234, ChunkStore.FromSection(rigBytes), stamp: false);
        var shotsB = new List<ShotReport>();
        var storeB = back.Economy!.OtherStores[EntityId.Make(IdKind.Person, 9500)];
        var wB = new EngineWorld { Walls = rigB.W, Engineering = _ => 40, OnShot = (e, r) => shotsB.Add(r), Goods = back.Economy.Goods, AmmoOf = _ => storeB };
        var eb = back.Engines!.Engines.Values.Single();
        int before = a.Shots.Count;
        a.Book.Advance(ea, 40_000, a.W);
        back.Engines.Advance(eb, 40_000, wB);
        output.WriteLine($"after the load {shotsB.Count} more shots; the uninterrupted run {a.Shots.Count - before}");
        Assert.True(shotsB.Count >= 10);
        Assert.Equal(a.Shots.Skip(before).Select(s => (s.X, s.Y, s.Wall)), shotsB.Select(s => (s.X, s.Y, s.Wall)));
        Assert.Equal(a.Book.Hash(), back.Engines.Hash());
        Assert.Equal(a.Rig.Store.ToSection(), rigB.Store.ToSection());
        Assert.Equal(storeA.Qty(a.B.Goods.IdOf("stone_raw")), storeB.Qty(back.Mounts!.Goods.IdOf("stone_raw")));
        Assert.Equal(VehicleHash.Of(a.B), VehicleHash.Of(back.Mounts));
        // a world with no engine writes no section: its bytes are what they were
        var plain = EconFixture.WorldWith(EconFixture.NewEconomy(false));
        var before0 = SaveCodec.Encode(WorldSave.Capture(plain, EconFixture.ContentHash, "0.1.0"));
        plain.Engines = new EngineBook(Db, new RideBook(Db, plain.Economy!, seed), seed);
        Assert.False(WorldSave.Capture(plain, EconFixture.ContentHash, "0.1.0").Sections.ContainsKey(SiegeSave.SectionName));
        Assert.Equal(before0, SaveCodec.Encode(WorldSave.Capture(plain, EconFixture.ContentHash, "0.1.0")));
        Assert.Contains(SiegeSave.SectionName, Ravensguard.Sim.Worlds.Phase7Seeding.SectionNames);
    }
}
