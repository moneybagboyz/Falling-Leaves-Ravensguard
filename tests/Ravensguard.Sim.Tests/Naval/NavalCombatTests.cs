using Ravensguard.Sim.Carts;
using Ravensguard.Sim.Combat.Melee2;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Naval;
using Ravensguard.Sim.Save;
using Ravensguard.Sim.Siege;
using Ravensguard.Sim.Tests.Armies;
using Ravensguard.Sim.Tests.Combat.Melee2;
using Ravensguard.Sim.Tests.Economy;
using Ravensguard.Sim.Animals;
using Xunit.Abstractions;

namespace Ravensguard.Sim.Tests.Naval;

/// <summary>
/// P7-SH-D (ships-and-naval-war 10): SH-12 (the deck fight now on levels: a castle shoots down, a parapet covers), SH-14 (a cog rams a longship by the wall rule, water comes in, she sinks; a fire arrow lights a sail and it drops), SH-15
/// (grapples bind two ships in 20 to 40 s, a climb of 4 s a man a rope onto a cog, a taken ship is a prize with its cargo) and SH-16 (ship rows, binds and frames in the <c>naval</c> section: save, load, save byte-identical).
/// </summary>
[Collection("fight")]
public class NavalCombatTests(FightFixture fx, ITestOutputHelper output)
{
    private static readonly VehicleCatalog Cat = VehicleCatalog.From(EconFixture.Content);

    private static ShipFrame Frame(string id, ulong n, int x = 0, int y = 0, int heading = 0) => ShipFrame.Of(new EntityId(n), Cat.Get(id), Cat, x, y, heading);

    private static (int X, int Y, int Deck) FirstPart(VehicleRow v, VehicleCatalog cat, string tag, int deck = -1)
    {
        foreach (var p in v.Parts) if (p.Built && cat.PartList[p.Part].Tags.Contains(tag) && (deck < 0 || p.Deck == deck)) return (p.X, p.Y, p.Deck);
        throw new InvalidOperationException("no part tagged " + tag);
    }

    private static List<(int X, int Y)> Cells(ShipFrame f, int deck, DeckKind kind)
    {
        var l = new List<(int, int)>();
        for (int y = 0; y < f.Deck.Length; y++) for (int x = 0; x < f.Deck.Width; x++) if (f.Deck.Kind(x, y, deck) == kind) l.Add((x, y));
        return l;
    }

    // ------------------------------------------------------------------ SH-14: ramming

    [Fact]
    public void SH14_ACogRamsALongshipAt3MetresASecond_TheStruckCellTakesTheWallRulesHitPoints_WaterComesIn_ShePutsDownBySinking_AndTheChronicleSaysSo()
    {
        using var k = new ArmyRig(); k.Raised();
        var rides = VehicleDesk.BookOf(k.State, EconFixture.Content); var cat = rides.Templates;
        var pt = k.State.Population.People; var master = pt.IdOf(0);
        var cog = rides.Vehicles.Create(cat.Get("cog"), pt.IdOf(1), 0, 0); cog.Water = WaterState.Afloat;
        var longship = rides.Vehicles.Create(cat.Get("longship"), master, 40_000, 0); longship.Water = WaterState.Afloat;
        var book = k.State.Naval ??= new NavalBook();
        var combat = k.Aw.ShipsAt()!; Assert.NotNull(combat);
        var (cx, cy, _) = FirstPart(longship, cat, "hull", 0);

        // the rule: M = mass x v_c / 10, and max(0, M - 600) / 60 hit points a second of contact, the closing speed halving
        long mass = rides.Vehicles.GrossKg(cog);
        long expect = 0; for (int s = 0; s < 3; s++) expect += WallDamage.Impact(mass * (3000 >> s) / 10_000, 600, 60);
        Assert.InRange(mass * 3000 / 10_000, 11_000, 12_600);                                  // a 40 t cog at 3 m/s: 12,000
        var ram = combat.Ram(cog.Id.Value, longship.Id.Value, cx, cy, 3000);
        output.WriteLine($"cog {mass} kg: M {ram.MomentumKgMs}, {ram.HullHp} hp off the cell, bow {ram.BowHp}, holed {ram.Holed}");
        Assert.True(ram.Struck); Assert.Equal(expect, ram.HullHp); Assert.True(ram.Holed, "320 hp breaks a 250 hp clinker strake");
        Assert.Equal(1, combat.Holes(longship));
        Assert.Equal((int)(expect / 3), ram.BowHp);                                            // the rammer's bow takes a third
        Assert.Contains(cog.Parts, p => p.Deck == 0 && cat.PartList[p.Part].Tags.Contains("hull") && p.HpMilli == cat.PartList[p.Part].Hp * 1000 - (int)(expect / 3) * 1000);   // her bow took the third

        // a 9 t longship at 4 m/s on a cog's 400 hp planking: 3,600 and no hole
        var back = combat.Ram(longship.Id.Value, cog.Id.Value, FirstPart(cog, cat, "hull", 0).X, FirstPart(cog, cat, "hull", 0).Y, 4000);
        Assert.InRange(back.MomentumKgMs, 3400, 3700); Assert.False(back.Holed);
        Assert.False(ShipCombat.WouldRam(rides.Vehicles.GrossKg(longship), rides.Vehicles.GrossKg(cog), false));
        Assert.True(ShipCombat.WouldRam(rides.Vehicles.GrossKg(cog), rides.Vehicles.GrossKg(longship), false));
        Assert.False(ShipCombat.WouldRam(rides.Vehicles.GrossKg(cog), rides.Vehicles.GrossKg(longship), true));

        // water comes in at 20 L/s a hole until her freeboard is gone: she founders within the hours, and no coin or goods are lost
        long before = k.State.Economy!.Ledger.TotalOf(Ravensguard.Sim.Economy.PurseKind.Household).Mites;
        var ends = new List<ShipEnd>(); long sec = k.Now * 3600L;
        for (int i = 0; i < 40 && ends.Count == 0; i++) { sec += 600; ends.AddRange(combat.Step(600, sec)); }
        output.WriteLine($"{longship.WaterInL} L aboard, {(ends.Count > 0 ? ends[0].Cause : "afloat")} at {sec - k.Now * 3600L} s");
        Assert.Single(ends); Assert.Equal(ShipEndKind.Sunk, ends[0].Kind); Assert.Equal("holed", ends[0].Cause);
        Assert.Equal(WaterState.Sunk, longship.Water); Assert.Equal(ShipState.Sunk, book.Ships[longship.Id.Value].State);
        Assert.Equal(master, ends[0].Master);
        k.Aw.RecordShipEnds(k.Env(), ends);
        var entry = k.Entries("ship_sunk").Single();
        Assert.Contains(entry.Causes, c => c.Kind == CauseKind.Naval && c.NoteKey == "ch_note_sh_holed");
        Assert.Equal(entry.Id.Value, book.Ships[longship.Id.Value].Entry);
        Assert.True(k.State.Economy!.MassAuditHolds());
        Assert.Equal(before, k.State.Economy!.Ledger.TotalOf(Ravensguard.Sim.Economy.PurseKind.Household).Mites);
        Assert.Empty(combat.Step(3600, sec + 3600));                                           // a sunk ship goes no further
    }

    [Fact]
    public void SH14_AFireArrowLightsASail_TheCanvasBurnsAndDrops_TheMastTakesABlow_ABucketCrewPutsItOutAtSeaTwiceAsFast()
    {
        using var k = new ArmyRig(); k.Raised();
        var rides = VehicleDesk.BookOf(k.State, EconFixture.Content); var cat = rides.Templates;
        var cog = rides.Vehicles.Create(cat.Get("cog"), k.State.Population.People.IdOf(1), 0, 0); cog.Water = WaterState.Afloat;
        k.State.Naval ??= new NavalBook();
        var combat = k.Aw.ShipsAt()!;
        var (sx, sy, sd) = FirstPart(cog, cat, "sail");
        int sailBefore = rides.Vehicles.StatsOf(cog).SailM2;
        int mastHp = cog.Parts.First(p => cat.PartList[p.Part].Tags.Contains("mast")).HpMilli;

        // the fire arrow lands in the fight: the layer finds the ship's cell under it and the ship's fire draws (0x7D6)
        var w = fx.World(14);
        var frame = Frame("cog", cog.Id.Value);
        var layer = new CarrierLayer(w, new Swimmers(14)) { Combat = combat }; layer.Add(frame);
        var (wx, wy) = frame.ToWorld(frame.Deck.CentreX(sx), frame.Deck.CentreY(sy));
        Assert.Equal(0, sd);
        int landed = 0;
        for (; landed < 40 && !combat.Row(cog.Id.Value).Burning.Any(); landed++) layer.Landed("fire_arrow", wx, wy);
        layer.Landed("arrow", wx, wy);                                                         // an ordinary arrow lights nothing
        var row = combat.Row(cog.Id.Value);
        output.WriteLine($"sail lit after {landed} arrows; burning parts {row.Burning.Count}");
        Assert.NotEmpty(row.Burning); Assert.True(landed < 40);
        Assert.All(row.Burning, i => Assert.Contains("sail", cat.PartList[cog.Parts[i].Part].Tags));

        // canvas of 40 hp burns off at 12 hp a minute: gone inside ten minutes; the fire goes down to the mast, which takes 30 hp as the sail drops
        long sec = k.Now * 3600L;
        for (int i = 0; i < 60; i++) { sec += 10; combat.Step(10, sec); }
        Assert.True(row.SailDown);
        Assert.True(rides.Vehicles.StatsOf(cog).SailM2 < sailBefore, "a burnt panel is no sail");
        Assert.True(cog.Parts.First(p => cat.PartList[p.Part].Tags.Contains("mast")).HpMilli < mastHp);

        // a crew of four with buckets: 4 x 20 s x 2 / 20 = 8 parts put out in twenty seconds (the Phase 6 rate would be 4)
        Assert.Equal(0, combat.Ignite(cog.Id.Value, 99, 99, 0, IgniteKind.Arrow));
        for (int tries = 0; tries < 60 && row.Burning.Count == 0; tries++) combat.Ignite(cog.Id.Value, sx, sy, sd, IgniteKind.Pitch);
        int burning = row.Burning.Count; Assert.True(burning > 0);
        Assert.Equal(Math.Min(burning, 8), combat.Douse(cog.Id.Value, 4, 20));
    }

    // ------------------------------------------------------------------ SH-12: the deck on levels

    private sealed class Duel { public required FightWorld W; public required CarrierLayer L; public required ShipFrame Ship; public required Fighter Archer, Target; }

    private Duel Shooters(bool fromCastle)
    {
        var w = fx.World(12); var ship = Frame("cog", 7, 30_000, 30_000);
        var layer = new CarrierLayer(w, new Swimmers(12)); layer.Add(ship);
        var castle = Cells(ship, 1, DeckKind.Floor).OrderByDescending(c => c.Y).ThenBy(c => c.X).First();
        var archer = w.Add(fx.Kit("Archer", 0, 0, 0, launcher: "longbow", ammo: "bodkin_arrow", ammoCount: 20, skill: 50, faceX: 0, faceY: -FightMath.One), player: false);
        var target = w.Add(fx.Kit("Target", 1, 0, 0, skill: 40, faceX: 0, faceY: FightMath.One), player: true);
        layer.BoardAt(archer, ship, castle.X, castle.Y, fromCastle ? 1 : 0);
        layer.BoardAt(target, ship, castle.X, 3, 0);
        return new Duel { W = w, L = layer, Ship = ship, Archer = archer, Target = target };
    }

    [Fact]
    public void SH12_ACastleShootsDown_TheDecksAreLevels_AnAimSteadierAndALongerReach_AndAParapetCoversFromBelowByTheCrenelRule()
    {
        var up = Shooters(true); var down = Shooters(false);
        Assert.Equal(3000, up.L.BaseMm(up.Archer.Id)); Assert.Equal(0, down.L.BaseMm(down.Archer.Id));
        Assert.False(up.L.CanStrike(up.Archer.Id, up.Target.Id)); Assert.True(down.L.CanStrike(down.Archer.Id, down.Target.Id));
        int? sUp = null, sDown = null;
        for (int t = 0; t < 140 && (sUp == null || sDown == null); t++)
        {
            up.W.StepWithAi(); down.W.StepWithAi();
            sUp ??= up.W.Events.Where(e => e.Kind == FightEventKind.Shot).Select(e => (int?)e.A).FirstOrDefault();
            sDown ??= down.W.Events.Where(e => e.Kind == FightEventKind.Shot).Select(e => (int?)e.A).FirstOrDefault();
        }
        output.WriteLine($"first shot's aim error: from the castle {sUp}, from the deck {sDown} milli-degrees");
        Assert.NotNull(sUp); Assert.NotNull(sDown);
        Assert.Equal(sDown!.Value * 800 / 1000, sUp!.Value);                                   // a level up is a steadier aim (the assault rules' 800)

        // the crenel rule: a parapet on the castle's edge stops a missile from below with the content's share, none from the castle's own floor
        var ship = up.Ship; var l = up.L;
        var para = Cells(ship, 1, DeckKind.Parapet).First();
        var (px, py) = ship.ToWorld(ship.Deck.CentreX(para.X), ship.Deck.CentreY(para.Y));
        int merlon = Ravensguard.Sim.Siege.AssaultLayer.DefaultRules.MerlonPm, stopped = 0, own = 0, n = 1000;
        for (uint s = 0; s < n; s++) { if (l.MissileStopped(px, py, 3000 + 1400, up.Target.Id, s)) stopped++; if (l.MissileStopped(px, py, 3000 + 1400, up.Archer.Id, s)) own++; }
        output.WriteLine($"a missile from the deck meets the parapet {stopped} in {n} (content {merlon}); from the castle {own}");
        Assert.InRange(stopped, merlon - 80, merlon + 80); Assert.Equal(0, own);
        // a cabin stops any missile on its deck
        Assert.False(l.MissileStopped(px, py, 800, up.Target.Id, 1));
    }

    // ------------------------------------------------------------------ SH-15: grapples, boarding, prizes

    private sealed class Rig : IDisposable { public required ArmyRig K; public required RideBook Rides; public required ShipCombat C; public required VehicleRow Cog, Long; public required ShipFrame FCog, FLong; public void Dispose() => K.Dispose(); }

    private Rig Pair()
    {
        var k = new ArmyRig(); k.Raised();
        var rides = VehicleDesk.BookOf(k.State, EconFixture.Content); var cat = rides.Templates; var pt = k.State.Population.People;
        var cog = rides.Vehicles.Create(cat.Get("cog"), pt.IdOf(1), 0, 0); cog.Water = WaterState.Afloat;
        var lg = rides.Vehicles.Create(cat.Get("longship"), pt.IdOf(0), 9_000, 0); lg.Water = WaterState.Afloat;
        k.State.Naval ??= new NavalBook();
        return new Rig { K = k, Rides = rides, C = k.Aw.ShipsAt()!, Cog = cog, Long = lg, FCog = Frame("cog", cog.Id.Value, 0, 0), FLong = Frame("longship", lg.Id.Value, 9_000, 0) };
    }

    [Fact]
    public void SH15_TwoGrapplesBindTheShipsAfterAHaulOf20To40Seconds_TheyMoveAsOne_AClimbOnARopeTakesFourSeconds_AndAOneManARopeLimitHolds()
    {
        using var p = Pair();
        var c = p.C; ulong a = p.Cog.Id.Value, b = p.Long.Id.Value;
        Assert.False(c.Throw(a, b, 7000, 50, 0));                                              // out of range (6 m)
        int throws = 0; while (c.BindOf(a, b) is not { Lines: >= 2 } && throws++ < 40) c.Throw(a, b, 4000, 50, 400);
        var bind = c.BindOf(a, b)!; output.WriteLine($"{bind.Throws} throws caught {bind.Lines} lines");
        Assert.True(bind.Lines >= 2); Assert.Equal(bind.Throws, c.Book.Binds.Single().Throws);
        Assert.Equal(40, c.HaulSeconds(4)); Assert.Equal(20, c.HaulSeconds(8)); Assert.Equal(30, c.HaulSeconds(6)); Assert.Equal(20, c.HaulSeconds(30)); Assert.Equal(40, c.HaulSeconds(1));
        Assert.False(c.Haul(bind, 4, 39)); Assert.True(c.Haul(bind, 4, 1));                    // 40 s with four men
        // one line only is no bind
        var one = new GrappleBind { A = 1, B = 2, Lines = 1 }; Assert.False(c.Haul(one, 20, 600));

        // the frames join: the cog is under way and the longship goes with her, turning round her
        var w = fx.World(15); var layer = new CarrierLayer(w, new Swimmers(15)) { Combat = c };
        layer.Add(p.FCog); layer.Add(p.FLong);
        p.FCog.SpeedMmps = 2000; p.FCog.TurnPerTick = ShipFrame.TurnOf(3);
        Assert.True(layer.Join(p.FCog, p.FLong)); Assert.False(layer.Join(p.FCog, p.FLong));
        var rel0 = p.FCog.ToLocal(p.FLong.XMm, p.FLong.YMm);
        var raider = w.Add(fx.Kit("Raider", 1, 0, 0, skill: 55, worn: [new("tunic", "linen")]) with { Person = 41 }, player: true);
        var raider2 = w.Add(fx.Kit("Raider2", 1, 0, 0, skill: 55, worn: [new("tunic", "linen")]) with { Person = 42 }, player: true);
        var raider3 = w.Add(fx.Kit("Raider3", 1, 0, 0, skill: 55, worn: [new("tunic", "linen")]) with { Person = 43 }, player: true);
        var floors = Cells(p.FLong, 0, DeckKind.Floor); var dest = Cells(p.FCog, 0, DeckKind.Floor);
        layer.BoardAt(raider, p.FLong, floors[10].X, floors[10].Y, 0); layer.BoardAt(raider2, p.FLong, floors[11].X, floors[11].Y, 0); layer.BoardAt(raider3, p.FLong, floors[12].X, floors[12].Y, 0);
        Assert.Equal(2200, Math.Abs(p.FCog.SideMm - p.FLong.SideMm));                          // a cog's side 2.2 m over the longship's
        Assert.Equal(80, layer.ClimbTicks(p.FLong, p.FCog));
        Assert.True(layer.BeginBoard(raider, p.FCog, dest[20].X, dest[20].Y, 0));
        Assert.False(layer.BeginBoard(raider2, p.FCog, dest[21].X, dest[21].Y, 0), "one man a rope");
        Assert.True(layer.BeginBoard(raider3, p.FCog, dest[22].X, dest[22].Y, 1));            // the other rope
        int start = w.Tick;
        for (int t = 0; t < 200; t++)
        {
            w.StepWithAi();
            if (w.Tick == start + 79) Assert.Equal(p.FLong.Id.Value, layer.FrameOf(raider.Id)!.Id.Value);
            if (w.Tick == start + 81) { Assert.Equal(p.FCog.Id.Value, layer.FrameOf(raider.Id)!.Id.Value); Assert.Equal(p.FCog.Id.Value, layer.FrameOf(raider3.Id)!.Id.Value); }
            if (t == 85) Assert.True(layer.BeginBoard(raider2, p.FCog, dest[21].X, dest[21].Y, 0), "the rope is free again");
        }
        Assert.Equal(p.FCog.Id.Value, layer.FrameOf(raider2.Id)!.Id.Value);
        Assert.Equal(rel0, p.FCog.ToLocal(p.FLong.XMm, p.FLong.YMm));                          // still the same place against the cog, 200 ticks and a turn on
        Assert.Equal(p.FCog.Heading, (p.FLong.Heading - ((p.FLong.Heading - p.FCog.Heading) & FrameMath.Mask)) & FrameMath.Mask);
        Assert.NotEqual(0, p.FCog.XMm);
        Assert.Equal(3, layer.Events.Count(e => e.Kind == CarrierEventKind.Boarded && e.Frame == p.FCog.Id.Value));
    }

    [Fact]
    public void SH15_ShipWhoseCrewAreAllDown_IsTaken_APrizeWithHerCargo_TheChronicleWritesShipTaken_AndNothingIsMadeOrLost()
    {
        using var p = Pair();
        var c = p.C; var rides = p.Rides; var k = p.K;
        // grain in the longship's hold (real goods from a real store)
        var src = k.State.Economy!.HouseholdStore.First(st => st.Rows().Any());
        var (good, qty) = src.Rows().First(); Assert.True(Ravensguard.Sim.Goods.Store.Move(src, p.Long.Cargo, good, Math.Min(qty, 5)));
        long massBefore = p.Long.Cargo.TotalMassG(); Assert.True(massBefore > 0);

        var w = fx.World(16); var layer = new CarrierLayer(w, new Swimmers(16)) { Combat = c }; layer.Add(p.FLong);
        var crew1 = w.Add(fx.Kit("Crew1", 1, 0, 0, skill: 30, worn: [new("tunic", "linen")]) with { Person = 51 }, player: true);
        var crew2 = w.Add(fx.Kit("Crew2", 1, 0, 0, skill: 30, worn: [new("tunic", "linen")]) with { Person = 52 }, player: true);
        var boarder = w.Add(fx.Kit("Boarder", 0, 0, 0, skill: 60, worn: [new("tunic", "linen")]) with { Person = 53 }, player: true);
        var f = Cells(p.FLong, 0, DeckKind.Floor);
        layer.BoardAt(crew1, p.FLong, f[5].X, f[5].Y, 0); layer.BoardAt(crew2, p.FLong, f[6].X, f[6].Y, 0); layer.BoardAt(boarder, p.FLong, f[30].X, f[30].Y, 0);
        Assert.False(layer.IsTaken(p.FLong, 1, out _));                                        // two fit men: no prize yet
        crew1.Act = FAct.Down; crew2.Act = FAct.Surrendered;                                   // one beaten down, one yielding
        Assert.True(layer.IsTaken(p.FLong, 1, out int team)); Assert.Equal(0, team);
        Assert.False(layer.IsTaken(p.FLong, 0, out _), "the boarder is fit: her crew are the ones who lost");
        var captor = k.State.Population.People.IdOf(5); var master = p.Long.Owner;
        // P7R-7: coin in the hull's strongbox goes to the captor's purse with the prize
        var led = k.State.Economy!.Ledger; var box = led.Open(PurseKind.Chest, p.Long.Id);
        var rich = Enumerable.Range(0, k.State.Economy.HouseholdPurse.Length).First(h => led.Balance(k.State.Economy.HouseholdPurse[h]).Mites >= 100);
        Assert.True(led.Transfer(k.State.Economy.HouseholdPurse[rich], box, new Coin(100), TransferReason.Plunder));
        var captorPurse = k.State.Economy.HouseholdPurse[k.State.Population.People.Household[5]]; long cap0 = led.Balance(captorPurse).Mites;
        var end = c.Take(p.Long.Id.Value, captor, k.Now * 3600L)!;
        Assert.Equal(0, led.Balance(box).Mites); Assert.Equal(cap0 + 100, led.Balance(captorPurse).Mites);
        Assert.Equal(ShipEndKind.Taken, end.Kind); Assert.Equal(master, end.Master); Assert.Equal(captor, p.Long.Owner);
        Assert.Null(c.Take(p.Long.Id.Value, captor, 0));                                       // taken once
        Assert.Equal(massBefore, p.Long.Cargo.TotalMassG());                                   // the hold went with her
        k.Aw.RecordShipEnds(k.Env(), [end]);
        var entry = k.Entries("ship_taken").Single();
        Assert.Contains(entry.Causes, cz => cz.Kind == CauseKind.Naval && cz.NoteKey == "ch_note_sh_boarded");
        Assert.True(k.State.Economy!.MassAuditHolds());
    }

    // ------------------------------------------------------------------ SH-16: the section

    [Fact]
    public void SH16_ShipRowsBindsAndFrames_AreInTheNavalSection_SaveLoadSaveIsByteIdentical_AndTheHashIsEqualAcrossRuns()
    {
        ulong Run(out byte[] bytes)
        {
            using var p = Pair();
            var c = p.C; var k = p.K;
            c.Ram(p.Cog.Id.Value, p.Long.Id.Value, FirstPart(p.Long, p.Rides.Templates, "hull", 0).X, FirstPart(p.Long, p.Rides.Templates, "hull", 0).Y, 3000);
            var (sx, sy, sd) = FirstPart(p.Cog, p.Rides.Templates, "sail");
            for (int i = 0; i < 80 && c.Row(p.Cog.Id.Value).Burning.Count == 0; i++) c.Ignite(p.Cog.Id.Value, sx, sy, sd, IgniteKind.Pitch);
            c.Step(120, k.Now * 3600L);
            while (c.BindOf(p.Cog.Id.Value, p.Long.Id.Value) is not { Lines: >= 2 }) c.Throw(p.Cog.Id.Value, p.Long.Id.Value, 3000, 50, 0);
            c.Haul(c.BindOf(p.Cog.Id.Value, p.Long.Id.Value)!, 6, 30);
            p.FCog.SpeedMmps = 1500; p.FCog.Board(RiderKind.Person, 77, 500, -1500, 0); for (int i = 0; i < 50; i++) p.FCog.Step();
            k.State.Naval!.Frames[p.FCog.Id.Value] = p.FCog.Export(); k.State.Naval.Frames[p.FLong.Id.Value] = p.FLong.Export();
            c.Take(p.Long.Id.Value, k.State.Population.People.IdOf(5), k.Now * 3600L);
            var book = k.State.Naval!;
            Assert.NotEmpty(book.Ships[p.Cog.Id.Value].Burning); Assert.Equal(ShipState.Taken, book.Ships[p.Long.Id.Value].State);
            var file = WorldSave.Capture(k.State, "h", "0.1");
            Assert.Contains(NavalSave.SectionName, file.Sections.Keys);
            bytes = SaveCodec.Encode(file);
            var back = WorldSave.Restore(SaveCodec.Decode(bytes), k.R.Content, "h", null, k.R.W.Continent, k.R.W.History); back.Content = k.R.Content;
            Assert.Equal(book.Hash(), back.Naval!.Hash());
            Assert.Equal(book.Ships[p.Cog.Id.Value].Burning, back.Naval.Ships[p.Cog.Id.Value].Burning);
            Assert.Equal(book.Binds.Select(x => (x.A, x.B, x.Lines, x.Joined)), back.Naval.Binds.Select(x => (x.A, x.B, x.Lines, x.Joined)));
            Assert.Equal(p.FCog.Export().RiderIds, back.Naval.Frames[p.FCog.Id.Value].RiderIds);
            Assert.Equal(bytes, SaveCodec.Encode(WorldSave.Capture(back, "h", "0.1")));          // save, load, save: identical
            output.WriteLine($"naval section {file.Sections[NavalSave.SectionName].Length} bytes");
            return book.Hash();
        }
        ulong h1 = Run(out var b1), h2 = Run(out var b2);
        Assert.NotEqual(0UL, h1); Assert.Equal(h1, h2); Assert.Equal(b1, b2);
    }
}
