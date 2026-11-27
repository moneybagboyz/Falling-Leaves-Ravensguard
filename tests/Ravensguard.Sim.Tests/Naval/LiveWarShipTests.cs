using Ravensguard.Sim.Carts;
using Ravensguard.Sim.Combat.Melee2;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Naval;
using Ravensguard.Sim.Tests.Armies;
using Ravensguard.Sim.Tests.Combat.Melee2;
using Ravensguard.Sim.Tests.Economy;
using Ravensguard.Sim.Travel;
using Xunit.Abstractions;

namespace Ravensguard.Sim.Tests.Naval;

/// <summary>P8-LW-2, the deck: LW-8 (the captain's brain and its drive) and LW-9 (pushing off, overboard).</summary>
[Collection("fight")]
public class LiveWarShipTests(FightFixture fx, ITestOutputHelper output)
{
    private static readonly VehicleCatalog Cat = VehicleCatalog.From(EconFixture.Content);
    private static ShipFrame Frame(string id, ulong n, int x = 0, int y = 0, int heading = 0) => ShipFrame.Of(new EntityId(n), Cat.Get(id), Cat, x, y, heading);

    private static CaptainView V(bool stem = true, int closing = 3000, int ramDeg = 20, int dist = 10_000, int headDeg = 90, bool grappled = false, int fit = 30, int start = 30, int enemy = 30, bool water = false)
        => new(stem, closing, ramDeg, dist, headDeg, grappled, fit, start, enemy, water);

    [Fact]
    public void LW8_ALongshipWithAStemPost_RamsAt25OrMoreAndWithin30Degrees_AWeakerCrewShootsRatherThanBoards_ACrewUnder40PercentFlees()
    {
        Assert.Equal(CaptainAct.Ram, CaptainBrain.Decide(V(closing: 2500, ramDeg: 30)));
        Assert.Equal(CaptainAct.Approach, CaptainBrain.Decide(V(closing: 2499)));                       // too slow
        Assert.Equal(CaptainAct.Approach, CaptainBrain.Decide(V(ramDeg: 31)));                          // too slanting
        Assert.Equal(CaptainAct.Approach, CaptainBrain.Decide(V(stem: false)));                         // a cog has no stem post
        Assert.Equal(CaptainAct.Approach, CaptainBrain.Decide(V(dist: 30_000)));                        // not yet in reach
        Assert.Equal(CaptainAct.Grapple, CaptainBrain.Decide(V(dist: 6000, headDeg: 25)));              // within 6 m and side by side
        Assert.Equal(CaptainAct.Approach, CaptainBrain.Decide(V(dist: 6001, headDeg: 25, closing: 0)));
        Assert.Equal(CaptainAct.Board, CaptainBrain.Decide(V(grappled: true, fit: 24, enemy: 30)));     // 0.8 exactly boards
        Assert.Equal(CaptainAct.Shoot, CaptainBrain.Decide(V(grappled: true, fit: 23, enemy: 30)));     // weaker: shoots from the castles
        Assert.Equal(CaptainAct.Flee, CaptainBrain.Decide(V(grappled: true, fit: 11, start: 30)));      // under 40% of the start: flees even grappled
        Assert.Equal(CaptainAct.Board, CaptainBrain.Decide(V(grappled: true, fit: 12, start: 30, enemy: 15)));
        Assert.Equal(CaptainAct.Flee, CaptainBrain.Decide(V(water: true)));                             // the hull takes water faster than the pumps clear it
        // the side: the upwind one, and a draw on 0x858 keyed on the ship and the minute when both are equal
        Assert.Equal(-1, CaptainBrain.ApproachSide(7, 5, 100, -1)); Assert.Equal(1, CaptainBrain.ApproachSide(7, 5, 100, 1));
        var draws = Enumerable.Range(0, 64).Select(m => CaptainBrain.ApproachSide(7, 5, m, 0)).ToList();
        Assert.Contains(-1, draws); Assert.Contains(1, draws);
        Assert.Equal(draws, Enumerable.Range(0, 64).Select(m => CaptainBrain.ApproachSide(7, 5, m, 0)).ToList());
    }

    [Fact]
    public void LW8_TheGeometry_ALongshipClosingSquareOnAt3MetresASecond_IsSeenAsARam_AndTheDriveStrikesWithTheNavalRam()
    {
        using var k = new ArmyRig(); k.Raised();
        var rides = VehicleDesk.BookOf(k.State, EconFixture.Content); var cat = rides.Templates; var pt = k.State.Population.People;
        var cog = rides.Vehicles.Create(cat.Get("cog"), pt.IdOf(1), 9_000, 0); cog.Water = WaterState.Afloat;
        var lg = rides.Vehicles.Create(cat.Get("longship"), pt.IdOf(0), 0, 0); lg.Water = WaterState.Afloat;
        k.State.Naval ??= new NavalBook();
        var combat = k.Aw.ShipsAt()!;
        var w = fx.World(31); var layer = new CarrierLayer(w, new Swimmers(31)) { Combat = combat };
        var fl = Frame("longship", lg.Id.Value, 0, 0, 0); var fc = Frame("cog", cog.Id.Value, 9_000, 0, FrameMath.Turn / 4);   // the cog lies across her bow, her side to the longship
        layer.Add(fl); layer.Add(fc);
        fl.SpeedMmps = 3000;
        var v = CaptainBrain.View(layer, fl, fc, 0, 0, true, false, false);
        output.WriteLine($"closing {v.ClosingMmps} ram {v.RamAngleDeg} dist {v.DistMm} head {v.HeadingDiffDeg}");
        Assert.Equal(3000, v.ClosingMmps); Assert.Equal(CaptainBrain.RamMaxAngleDeg, v.RamAngleDeg); Assert.Equal(CaptainAct.Ram, CaptainBrain.Decide(v));
        // the same ships side by side (parallel headings) are a grapple, not a ram
        var fc2 = Frame("cog", cog.Id.Value, 9_000, 0, 0); fc2.SpeedMmps = 0;
        Assert.Equal(CaptainBrain.GrappleHeadingDeg, CaptainBrain.View(layer, fl, fc2, 0, 0, true, false, false).HeadingDiffDeg);
        // the drive: on its 2 s beat the captain rams through ShipCombat.Ram, and does not ram twice
        var drive = new CaptainDrive(layer, combat, 31); var cap = drive.Add(fl, 0, fc);
        for (int t = 0; t < 100 && !cap.Rammed; t++) { w.StepWithAi(); drive.Step(); }
        Assert.True(cap.Rammed); Assert.Equal(CaptainAct.Ram, cap.Last);
        Assert.True(combat.Book.Ships.ContainsKey(cog.Id.Value), "the struck hull is in the book");
    }

    [Fact]
    public void LW9_TheHeroJumpsAGapOf14MetresAndBoards_At16MetresHeCannot_AMissIsAManOverboard_AManInMailInDeepWaterDrowns()
    {
        var w = fx.World(32); var water = new Swimmers(32); var layer = new CarrierLayer(w, water);
        var ship = Frame("longship", 9, 0, 0); layer.Add(ship);
        layer.AthleticsOf = f => f.Person == 77 ? 10 : 60; layer.KitOf = f => f.Person == 77 ? [GearPiece.OfShield("heater", 3000), GearPiece.OfArmour("mail_hauberk", 12_000), GearPiece.OfArmour("helm", 2500)] : [GearPiece.OfArmour("tunic", 1000)];
        var floors = new List<(int X, int Y)>(); for (int y = 0; y < ship.Deck.Length; y++) for (int x = 0; x < ship.Deck.Width; x++) if (ship.Deck.Walkable(x, y, 0)) floors.Add((x, y));
        var cell = floors[10];
        var hero = w.Add(fx.Kit("Hero", 0, -5000, 0, skill: 60, worn: [new("tunic", "linen")]) with { Person = 7 });
        Assert.Equal(JumpResult.Cannot, PushOff.Jump(layer, 1, hero, ship, cell.X, cell.Y, 1600));             // 1.6 m: he cannot
        Assert.False(layer.Rides(hero.Id)); Assert.NotEqual(FAct.Fled, hero.Act);
        Assert.Equal(950, PushOff.JumpPm(60));
        // 1.4 m: the check is 0x859 on the person and the tick; the same tick and person always give the same answer
        JumpResult r = PushOff.Jump(layer, 1, hero, ship, cell.X, cell.Y, 1400);
        int boarded = r == JumpResult.Boarded ? 1 : 0, tried = 1;
        for (ulong p = 100; p < 160; p++)
        {
            var m = w.Add(fx.Kit("M" + p, 0, -5000, 0, skill: 40, worn: [new("tunic", "linen")]) with { Person = p });
            var res = PushOff.Jump(layer, 1, m, ship, cell.X, cell.Y, 1400); Assert.NotEqual(JumpResult.Cannot, res); tried++;
            if (res == JumpResult.Boarded) { boarded++; Assert.True(layer.Rides(m.Id)); } else { Assert.Equal(FAct.Fled, m.Act); }
        }
        output.WriteLine($"{boarded} of {tried} boarded at 1.4 m");
        Assert.InRange(boarded, tried * 7 / 10, tried - 1);                                                   // most board; some misses go overboard
        Assert.Equal(PushOff.Roll(1, 7, w.Tick, 60), PushOff.Roll(1, 7, w.Tick, 60));
        // a mailed man who misses sinks and drowns by the breath rule; one in a tunic floats
        var mail = w.Add(fx.Kit("Mailed", 0, -5000, 0, skill: 40, worn: [new("tunic", "linen"), new("mail_hauberk", "iron")]) with { Person = 77 });
        var drowned = new List<ulong>(); water.OnDrowned += s => drowned.Add(s.Person);
        var sw = layer.Fall(mail, "jump"); Assert.NotNull(sw);
        for (int t = 0; t < 20 * 120; t++) water.Step();
        Assert.Contains(77UL, drowned);
        Assert.DoesNotContain(7UL, drowned);
    }
}
