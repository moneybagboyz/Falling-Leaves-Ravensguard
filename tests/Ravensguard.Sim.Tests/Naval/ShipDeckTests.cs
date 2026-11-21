using Ravensguard.Sim.Animals;
using Ravensguard.Sim.Carts;
using Ravensguard.Sim.Combat.Melee2;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Goods;
using Ravensguard.Sim.Naval;
using Ravensguard.Sim.Tests.Combat.Melee2;
using Ravensguard.Sim.Tests.Economy;
using Ravensguard.Sim.World;
using Xunit.Abstractions;

namespace Ravensguard.Sim.Tests.Naval;

/// <summary>
/// P7-SH-A (ships-and-naval-war 10): SH-1 (the cog and the longship float, the designer warns on an overloaded cog), SH-12's sim side (the hero boards a longship pushing off; a fight on its moving deck; a man in mail goes over the side and
/// drowns by the breath rule) and SH-13 (fighters on a moving ship keep their places through the transform, exactly; the fight hash is equal across runs), with the decks, the planks, the water and the save round trip.
/// </summary>
[Collection("fight")]
public class ShipDeckTests(FightFixture fx, ITestOutputHelper output)
{
    private static readonly VehicleCatalog Cat = VehicleCatalog.From(EconFixture.Content);

    private static (BoatLoad Full, BoatLoad Empty) Loads(VehicleBlueprint b)
    {
        var st = b.Stats; var d = b.Def; var wt = Cat.Water;
        int crew = (d.Driver + d.Passengers + st.Rowers) * BoatMath.PersonKg;
        return (BoatMath.Load(st, st.CargoKg, crew, wt), BoatMath.Load(st, 0, BoatMath.PersonKg, wt));
    }

    private static ShipFrame Frame(string id, ulong n, int x = 0, int y = 0, int heading = 0) => ShipFrame.Of(new EntityId(n), Cat.Get(id), Cat, x, y, heading);

    // ------------------------------------------------------------------ SH-1

    [Fact]
    public void SH1_TheCogAndTheLongshipFloat_At22And08Metres_WithGmAboveTheTenderLine_TheDesignerWarnsOnAnOverloadedCog()
    {
        Assert.Empty(Cat.Errors);
        var wt = Cat.Water;
        var cog = Cat.Get("cog"); var longship = Cat.Get("longship"); var war = Cat.Get("war_cog");
        foreach (var b in new[] { cog, longship, war })
        {
            var (full, empty) = Loads(b);
            output.WriteLine($"{b.Id}: {b.Stats.MassKg} kg empty, {b.Stats.CargoKg} kg cargo, full {full.TotalKg} kg draft {full.DraftMm} mm freeboard {full.FreeboardMm} GM {full.GmMm}; empty draft {empty.DraftMm} GM {empty.GmMm}; sail {b.Stats.SailM2} m2, rowers {b.Stats.Rowers}, steer {b.Stats.Steer}");
            Assert.DoesNotContain(b.Warnings, w => w.IsError);
            Assert.DoesNotContain(b.Warnings, w => w.Code is "tender" or "overloaded");
            Assert.True(full.GmMm >= wt.TenderGmMm && empty.GmMm >= wt.TenderGmMm, $"{b.Id} GM {full.GmMm}/{empty.GmMm}");
            Assert.True(full.FreeboardMm > 0 && !full.Swamped);
            Assert.True(b.Stats.Boat && b.Stats.SailM2 > 0);
        }
        // draft loaded: the cog 2.2 m, the longship 0.8 m (within 5%)
        Assert.InRange(Loads(cog).Full.DraftMm, 2090, 2310);
        Assert.InRange(Loads(longship).Full.DraftMm, 760, 840);
        // the table of 3.1: masses, cargo, sail, oars, steering, grids
        Assert.InRange(longship.Stats.MassKg, 8500, 9500); Assert.InRange(longship.Stats.CargoKg, 7500, 8500); Assert.Equal(70, longship.Stats.SailM2); Assert.Equal(32, longship.Stats.Rowers);
        Assert.Equal((5, 24), (longship.Stats.WidthCells, longship.Stats.LengthCells)); Assert.Equal("steering_oar", longship.Stats.Steer);
        Assert.InRange(cog.Stats.MassKg, 38000, 42000); Assert.InRange(cog.Stats.CargoKg, 95000, 105000); Assert.Equal(160, cog.Stats.SailM2); Assert.Equal(0, cog.Stats.Rowers);
        Assert.Equal((7, 22), (cog.Stats.WidthCells, cog.Stats.LengthCells)); Assert.Equal("rudder", cog.Stats.Steer);
        Assert.Equal(2, cog.Def.Levels.Count + 1);                                              // the main deck and the castles at z+1
        Assert.Equal(3, war.Def.Levels.Count + 1);                                              // and the fighting top at z+2
        Assert.Contains(war.Parts, p => Cat.PartList[p.Part].Id == "fighting_top" && p.Deck == 2);
        Assert.InRange(war.Stats.CargoKg, 55000, 65000);

        // the designer warns on an overloaded cog: one more layer of cargo planking on every cell leaves under a fifth of the hull's side above the water
        var def = cog.Def with
        {
            Id = "player.cog_overloaded", Name = "Overloaded cog",
            Legend = cog.Def.Legend.ToDictionary(kv => kv.Key, kv => kv.Value.Contains("deck_plank") ? [.. kv.Value.Take(kv.Value.IndexOf("deck_plank") + 1), "deck_plank", .. kv.Value.Skip(kv.Value.IndexOf("deck_plank") + 1)] : kv.Value, StringComparer.Ordinal),
        };
        var errors = new List<string>();
        var over = Cat.Register(def, errors);
        Assert.Empty(errors); Assert.NotNull(over);
        var (ofull, _) = Loads(over!);
        output.WriteLine($"overloaded cog: full {ofull.TotalKg} kg, draft {ofull.DraftMm}, freeboard {ofull.FreeboardMm}; warnings [{string.Join(", ", over!.Warnings.Select(w => w.Code))}]");
        Assert.Contains(over.Warnings, w => w.Code == "overloaded" && !w.IsError);
        Assert.True(ofull.FreeboardMm > 0);
        Assert.Contains("overloaded", PartGrid.Describe(over.Warnings.First(w => w.Code == "overloaded"), def, Cat));
    }

    // ------------------------------------------------------------------ decks as levels, ladders and planks

    [Fact]
    public void Decks_TheLongshipIsAnOpenDeckWithARailAMastAndTwoPlanks_TheCogHasCastlesAtZPlusOneJoinedByLadders_TheWarCogAFightingTopOnItsMast()
    {
        var ls = Frame("longship", 1).Deck;
        Assert.Equal((5, 24, 1), (ls.Width, ls.Length, ls.Decks));
        Assert.Equal(DeckKind.Gunwale, ls.Kind(0, 0, 0)); Assert.Equal(DeckKind.Gunwale, ls.Kind(4, 12, 0)); Assert.Equal(DeckKind.Gunwale, ls.Kind(2, 23, 0));      // the rail round the hull, the stem post at the bow
        Assert.Equal(DeckKind.Floor, ls.Kind(2, 2, 0)); Assert.Equal(DeckKind.Floor, ls.Kind(1, 5, 0)); Assert.Equal(DeckKind.Mast, ls.Kind(2, 9, 0));
        Assert.Equal(new[] { (0, 5, -1, 0), (4, 5, 1, 0) }, ls.Planks.ToArray());                // two boarding planks amidships, run out across the beam
        Assert.Equal(DeckKind.Plank, ls.Kind(0, 5, 0)); Assert.True(ls.Walkable(0, 5, 0));
        var around = ls.Path(new Cell3(2, 2, 0), new Cell3(2, 21, 0));
        Assert.NotNull(around); Assert.DoesNotContain(around!.Cells, c => c.X == 2 && c.Y == 9);                // the mast is walked round

        var cog = Frame("cog", 2).Deck;
        Assert.Equal((7, 22, 2), (cog.Width, cog.Length, cog.Decks));
        Assert.Equal(DeckKind.Ladder, cog.Kind(3, 4, 0)); Assert.Equal(DeckKind.Floor, cog.Kind(3, 4, 1));          // the ladder comes up through the gap in the sterncastle's parapet
        Assert.Equal(DeckKind.Parapet, cog.Kind(0, 0, 1)); Assert.Equal(DeckKind.Parapet, cog.Kind(6, 2, 1)); Assert.Equal(DeckKind.Floor, cog.Kind(3, 2, 1));
        Assert.Equal(DeckKind.None, cog.Kind(3, 10, 1));                                                          // amidships there is no deck above the waist
        Assert.Equal(DeckKind.Ladder, cog.Kind(3, 17, 0)); Assert.Equal(DeckKind.Floor, cog.Kind(3, 17, 1));
        Assert.Equal(2, cog.Grid.Portals.Count);
        var up = cog.Path(new Cell3(3, 8, 0), new Cell3(3, 2, 1));                                                // from the waist to the sterncastle's deck: by the ladder
        Assert.NotNull(up); Assert.Single(up!.PortalsUsed); Assert.Equal(1, up.Cells[^1].Z);
        Assert.Null(cog.Path(new Cell3(3, 8, 0), new Cell3(3, 10, 1)));

        var war = Frame("war_cog", 3).Deck;
        Assert.Equal(3, war.Decks);
        Assert.Equal(DeckKind.Ladder, war.Kind(3, 10, 0)); Assert.Equal(DeckKind.Ladder, war.Kind(3, 10, 1)); Assert.Equal(DeckKind.Floor, war.Kind(3, 10, 2));   // the shrouds, up the mast to the top
        var top = war.Path(new Cell3(3, 9, 0), new Cell3(3, 10, 2));
        Assert.NotNull(top); Assert.Equal(2, top!.PortalsUsed.Count);
    }

    // ------------------------------------------------------------------ the frame: the transform, motion, riders

    [Fact]
    public void TheFrame_PutsEverythingAboardThroughTheTransform_ARiderKeepsItsLocalPlaceExactlyThroughAFullTurn()
    {
        var f = Frame("longship", 1, 10_000, -20_000, FrameMath.FromDegrees(30));
        f.TurnPerTick = FrameMath.Turn / 2048; f.SpeedMmps = 2000;
        var placed = new List<(RiderKind K, ulong Id, int Lx, int Ly)>();
        foreach (var (k, id, cx, cy) in new[] { (RiderKind.Person, 5UL, 2, 3), (RiderKind.Person, 3UL, 1, 20), (RiderKind.Item, 99UL, 3, 15), (RiderKind.Light, 1UL, 2, 22) })
        { var r = f.BoardAt(k, id, cx, cy, 0); placed.Add((k, id, r.Lx, r.Ly)); }
        Assert.Equal(new[] { RiderKind.Person, RiderKind.Person, RiderKind.Item, RiderKind.Light }, f.Riders.Select(r => r.Kind).ToArray());            // (kind, id) order
        Assert.Equal(new[] { 3UL, 5UL }, f.Riders.Where(r => r.Kind == RiderKind.Person).Select(r => r.Id).ToArray());
        var first = f.WorldOf(f.RiderOf(RiderKind.Person, 5)!);
        for (int t = 0; t < 1024; t++) f.Step();
        Assert.NotEqual(first, f.WorldOf(f.RiderOf(RiderKind.Person, 5)!));                              // half a circle on, the ship has carried him to the far side of it
        for (int t = 1024; t < 2048; t++) f.Step();
        Assert.Equal(FrameMath.FromDegrees(30), f.Heading);                                              // 2048 ticks of 1/2048 turn: exactly one turn
        foreach (var (k, id, lx, ly) in placed) { var r = f.RiderOf(k, id)!; Assert.Equal((lx, ly), (r.Lx, r.Ly)); }
        Assert.Equal(f.ToWorld(placed[0].Lx, placed[0].Ly), f.WorldOf(f.RiderOf(RiderKind.Person, 5)!));
        // the world sees a rider where the transform puts him: the bow end of the keel line is `ship length / 2` along the heading from the frame's origin, to within a millimetre of rounding
        var g = Frame("longship", 2, 0, 0, FrameMath.FromDegrees(90));
        var bow = g.ToWorld(0, g.Deck.CentreY(23));
        Assert.InRange(bow.X, -1, 1); Assert.Equal(11_500, bow.Y);
        var (lx2, ly2) = g.ToLocal(bow.X, bow.Y); Assert.InRange(lx2, -1, 1); Assert.InRange(ly2, 11_499, 11_501);
    }

    /// <summary>A cog under sail at 8 m/s from astern: the speed the Phase 5 boat rules give it, a rider walking its deck, saved and loaded in the middle, equals the uninterrupted run.</summary>
    [Fact]
    public void ASaveInTheMiddleOfAShipUnderSail_LoadedAndContinued_EqualsTheUninterruptedRun()
    {
        var cog = Cat.Get("cog"); var st = cog.Stats; var wt = Cat.Water;
        var (full, _) = Loads(cog);
        long push = BoatMath.SailN(st.SailM2, 8, 1000);
        int speed = BoatMath.WaterSpeedMmps(push, wt.DragK, BoatMath.WetMm2(st.WaterplaneMm2, st.PerimeterM, full.DraftMm));
        Assert.InRange(speed, 1000, 6000);                                                                  // the Phase 5 drag at 8 m/s from astern (SH-B tunes the sea numbers)
        ShipFrame Make()
        {
            var f = Frame("cog", 7, 100_000, 40_000, FrameMath.FromDegrees(20)); f.SpeedMmps = speed; f.TurnPerTick = ShipFrame.TurnOf(3);
            f.BoardAt(RiderKind.Person, 1, 3, 2, 0); f.BoardAt(RiderKind.Item, 2, 2, 8, 0); f.BoardAt(RiderKind.Light, 3, 3, 4, 1);
            return f;
        }
        void Walk(ShipFrame f, int t)
        {
            var r = f.RiderOf(RiderKind.Person, 1)!;
            if (t % 7 == 0) r.Ly += 150; if (t % 11 == 0) r.Lx += t % 22 == 0 ? 120 : -100;               // a man pacing the deck (scripted)
        }
        var whole = Make(); for (int t = 0; t < 1000; t++) { whole.Step(); Walk(whole, t); }
        var a = Make(); for (int t = 0; t < 400; t++) { a.Step(); Walk(a, t); }
        var saved = a.Export();
        var b = ShipFrame.Restore(saved, DeckMap.Build(cog.Parts, Cat, st.WidthCells, st.LengthCells));
        for (int t = 400; t < 1000; t++) { b.Step(); Walk(b, t); }
        Assert.Equal(whole.Export(), b.Export(), new StateEq());
        Assert.Equal(whole.Hash(0), b.Hash(0));
        Assert.NotEqual((100_000, 40_000), (whole.XMm, whole.YMm));                                          // the ship sailed
    }

    private sealed class StateEq : IEqualityComparer<ShipFrameState>
    {
        public bool Equals(ShipFrameState? a, ShipFrameState? b) => a != null && b != null && a.Id == b.Id && (a.X, a.Y, a.Heading, a.Speed, a.TurnPerTick, a.AccX, a.AccY) == (b.X, b.Y, b.Heading, b.Speed, b.TurnPerTick, b.AccX, b.AccY)
            && a.RiderIds.SequenceEqual(b.RiderIds) && a.RiderLx.SequenceEqual(b.RiderLx) && a.RiderLy.SequenceEqual(b.RiderLy) && a.RiderKinds.SequenceEqual(b.RiderKinds) && a.RiderDecks.SequenceEqual(b.RiderDecks);
        public int GetHashCode(ShipFrameState s) => s.Id.GetHashCode();
    }

    // ------------------------------------------------------------------ SH-13: carriers in a fight

    private sealed class DeckFight
    {
        public required FightWorld W; public required CarrierLayer Layer; public required ShipFrame Ship;
        public required List<Fighter> All; public required Fighter Stander;
    }

    private DeckFight MakeDeckFight(ulong seed, bool turn)
    {
        var w = fx.World(seed);
        var ship = Frame("longship", 1, 60_000, 60_000);
        ship.SpeedMmps = 2500; ship.TurnPerTick = turn ? ShipFrame.TurnOf(6) : 0;
        var layer = new CarrierLayer(w, new Swimmers(seed));
        var all = new List<Fighter>();
        Fighter Man(string n, int team, int cx, int cy, ulong person, bool player = false, string? shield = null)
        {
            var kit = fx.Kit(n, team, 0, 0, skill: 50 + (int)person % 7, shield: shield, worn: [new("tunic", "linen"), new("gambeson", "linen")]) with { Person = person };
            var f = w.Add(kit, player); layer.BoardAt(f, ship, cx, cy, 0); all.Add(f); return f;
        }
        // forward is local +y; the sides face along it: team 0 at the stern, team 1 ahead of them (both sides away from the mast at row 9)
        Man("A1", 0, 1, 3, 11, shield: "heater"); Man("A2", 0, 2, 3, 12); Man("A3", 0, 3, 3, 13, shield: "heater");
        Man("B1", 1, 1, 7, 21, shield: "heater"); Man("B2", 1, 2, 7, 22); Man("B3", 1, 3, 7, 23);
        var stander = Man("Still", 0, 3, 14, 99, player: true);                                       // stands at the bow and does nothing (pushed only if the fight reaches him)
        ship.BoardAt(RiderKind.Item, 500, 1, 15, 0); ship.BoardAt(RiderKind.Light, 501, 3, 20, 0);   // a chest and a lantern: nobody ever moves them
        return new DeckFight { W = w, Layer = layer, Ship = ship, All = all, Stander = stander };
    }

    [Fact]
    public void SH13_FightersOnAMovingShipKeepTheirDeckPlacesAsItTurns_ExactToTheMillimetreThroughTheTransform_AndTheFightHashIsEqualAcrossRuns()
    {
        ulong Run(bool turn, bool check, out DeckFight df)
        {
            df = MakeDeckFight(5, turn);
            var chest = df.Ship.RiderOf(RiderKind.Item, 500)!; var lamp = df.Ship.RiderOf(RiderKind.Light, 501)!; var (clx, cly, llx, lly) = (chest.Lx, chest.Ly, lamp.Lx, lamp.Ly);
            var deadAt = new Dictionary<int, (int, int)>();
            var seen = new HashSet<int>();
            for (int t = 0; t < 600; t++)
            {
                df.W.StepWithAi();
                if (!check) continue;
                foreach (var f in df.All)
                {
                    var r = df.Layer.RiderOf(f.Id); if (r == null) continue;
                    Assert.Equal(df.Ship.ToWorld(r.Lx, r.Ly), (f.X, f.Y));                                   // the world point is the transform of the local one, every tick
                    var kind = df.Ship.Deck.KindAtMm(r.Lx, r.Ly, 0);
                    Assert.True(kind != DeckKind.None && !DeckMap.Blocks(kind), $"tick {t}: {f.Name} stands on {kind}");               // the rail and the mast stop a body, so nobody is ever in them
                    if (f.Id != df.Stander.Id && (f.Act != FAct.Idle)) seen.Add(f.Id);
                    if (f.Act == FAct.Dead) { if (!deadAt.TryAdd(f.Id, (r.Lx, r.Ly))) Assert.Equal(deadAt[f.Id], (r.Lx, r.Ly)); }                 // the dead lie where they fell, turn after turn
                }
                Assert.Equal((clx, cly, llx, lly), (chest.Lx, chest.Ly, lamp.Lx, lamp.Ly));                  // the chest and the lantern stay on their planks through every turn
            }
            if (check)
            {
                Assert.True(seen.Count >= 4, "the fight was joined");
                if (turn) Assert.InRange(df.Ship.Heading, 1, FrameMath.Turn - 1);
                Assert.Equal(df.Ship.ToWorld(clx, cly), df.Ship.WorldOf(chest));
            }
            return df.W.Hash();
        }
        ulong h1 = Run(true, true, out var d1), h2 = Run(true, false, out var d2), straight = Run(false, false, out _);
        output.WriteLine($"fight hash {h1:X16}; ship at {d1.Ship.XMm},{d1.Ship.YMm} heading {d1.Ship.HeadingQ10}/1024; {d1.All.Count(f => f.Act is FAct.Dead or FAct.Down)} down");
        Assert.Equal(h1, h2);                                                                                  // equal across runs
        Assert.NotEqual(h1, straight);                                                                          // and the ship's motion is in it
        Assert.Equal(d1.Ship.Export().X, d2.Ship.Export().X);
        // carriers do not touch a fight without ships
        var plain = fx.Duel(3); for (int t = 0; t < 200; t++) plain.W.StepWithAi();
        var plain2 = fx.Duel(3); for (int t = 0; t < 200; t++) plain2.W.StepWithAi();
        Assert.Null(plain.W.Carriers); Assert.Equal(plain.W.Hash(), plain2.W.Hash());
    }

    // ------------------------------------------------------------------ gangplanks

    [Fact]
    public void ABoardingPlank_JoinsAShipToAnotherShipOrTheQuayWhileItReaches()
    {
        var a = Frame("longship", 1); var b = Frame("cog", 2, -4000, 8000);
        int pa = 1, pb = 0;                                                                                  // A's starboard plank (4, 5) to B's port plank (0, 8)
        Assert.Equal((4, 5), (a.Deck.Planks[pa].X, a.Deck.Planks[pa].Y)); Assert.Equal((0, 8), (b.Deck.Planks[pb].X, b.Deck.Planks[pb].Y));
        Assert.True(Gangways.Reaches(a, pa, b, pb));
        a.BoardAt(RiderKind.Person, 7, 2, 5, 0);
        Assert.False(Gangways.Cross(a, pa, b, pb, RiderKind.Person, 7));                                     // not on the plank cell: he must walk to it
        a.BoardAt(RiderKind.Person, 7, 4, 5, 0);
        Assert.True(Gangways.Cross(a, pa, b, pb, RiderKind.Person, 7));
        Assert.Null(a.RiderOf(RiderKind.Person, 7)); var onB = b.RiderOf(RiderKind.Person, 7)!;
        Assert.Equal((0, 8), (b.Deck.CellX(onB.Lx), b.Deck.CellY(onB.Ly)));
        b.XMm += 9000; Assert.False(Gangways.Reaches(a, pa, b, pb));                                          // the ships drifted apart: the plank no longer joins them
        // the quay
        var (qx, qy) = a.PlankEnd(0);
        Assert.True(Gangways.Reaches(a, 0, qx, qy));
        Assert.True(Gangways.CrossFromQuay(a, 0, qx, qy, RiderKind.Person, 8));
        var q = Gangways.CrossToQuay(a, 0, qx, qy, RiderKind.Person, 8);
        Assert.Equal(((int, int)?)(qx, qy), q);
        a.XMm += 6000; Assert.False(Gangways.Reaches(a, 0, qx, qy));
    }

    // ------------------------------------------------------------------ SH-12: the deck, the rail, the water
    [Fact]
    public void SH12_TheHeroBoardsALongshipPushingOff_AFightOnItsMovingDeck_AManInMailGoesOverTheSideAndDrownsByTheBreathRule_AnotherSwimsAshore()
    {
        const ulong Hero = 7, Mailed = 301, Swimmer2 = 302;
        var w = fx.World(12);
        var ship = Frame("longship", 1, 0, 0);
        var water = new Swimmers(12);
        var layer = new CarrierLayer(w, water);
        layer.Add(ship);
        layer.KitOf = f => f.Person switch
        {
            Mailed => [GearPiece.OfShield("heater", 3000), GearPiece.OfArmour("mail_hauberk", 12_000), GearPiece.OfArmour("helm", 2500)],       // 17.5 kg: over the 15 kg line
            Swimmer2 => [GearPiece.OfShield("heater", 3000), GearPiece.OfArmour("gambeson", 12_500)],                                          // 15.5 kg: sinks, until the shield is dropped
            _ => [GearPiece.OfArmour("tunic", 1000)],
        };
        layer.AthleticsOf = f => f.Person == Swimmer2 ? 60 : 10;
        var sunk = new List<(ulong P, string Why)>(); var drowned = new List<(ulong P, long Tick)>();
        water.OnSank = s => sunk.Add((s.Person, s.Cause)); water.OnDrowned += s => drowned.Add((s.Person, water.Tick));

        // the hero stands on the quay at the end of the port plank; he walks it, and the ship pushes off with raiders aboard
        var (qx, qy) = ship.PlankEnd(0);
        var hero = w.Add(fx.Kit("Hero", 0, qx, qy, skill: 60, shield: "heater", worn: [new("tunic", "linen"), new("gambeson", "linen")], faceX: FightMath.One) with { Person = Hero });
        var mate = w.Add(fx.Kit("Mate", 0, 0, 0, skill: 45, worn: [new("tunic", "linen")]) with { Person = 8 });
        var r1 = w.Add(fx.Kit("Raider1", 1, 0, 0, skill: 45, shield: "heater", worn: [new("tunic", "linen"), new("gambeson", "linen"), new("mail_hauberk", "iron")]) with { Person = Mailed }, player: true);
        var r2 = w.Add(fx.Kit("Raider2", 1, 0, 0, skill: 40, shield: "buckler", worn: [new("tunic", "linen"), new("gambeson", "linen")]) with { Person = Swimmer2 }, player: true);
        var r3 = w.Add(fx.Kit("Raider3", 1, 0, 0, skill: 40, worn: [new("tunic", "linen")]) with { Person = 303 });
        layer.BoardAt(mate, ship, 1, 6, 0); layer.BoardAt(r1, ship, 1, 12, 0); layer.BoardAt(r2, ship, 3, 12, 0); layer.BoardAt(r3, ship, 2, 14, 0);
        water.Refuges.Add(new Refuge { Id = 1, XMm = qx - 9000, YMm = qy - 1000 });                       // the quay wall behind her

        Assert.True(Gangways.Reaches(ship, 0, qx, qy));
        Assert.True(Gangways.CrossFromQuay(ship, 0, qx, qy, RiderKind.Person, Hero));                      // he walks the plank
        layer.BoardAt(hero, ship, 0, 5, 0);
        Assert.Equal((0, 5), (ship.Deck.CellX(ship.RiderOf(RiderKind.Person, Hero)!.Lx), ship.Deck.CellY(ship.RiderOf(RiderKind.Person, Hero)!.Ly)));
        ship.SpeedMmps = 800; ship.TurnPerTick = ShipFrame.TurnOf(4);                                        // she pushes off, swinging her stern

        long enteredAt = -1;
        for (int t = 0; t < 1200; t++)
        {
            w.StepWithAi();
            if (t == 10)
            {
                // two raiders at the rail are thrown over it: one in mail, one in a gambeson, one to each side
                var (ax, ay) = ship.ToWorld(ship.Deck.CentreX(1) - 1000, ship.Deck.CentreY(12)); var (bx, by) = ship.ToWorld(ship.Deck.CentreX(1), ship.Deck.CentreY(12));
                Assert.True(layer.TryKnockOver(r1.Id, ax - bx, ay - by));
                var (cx, cy) = ship.ToWorld(ship.Deck.CentreX(3) + 1000, ship.Deck.CentreY(12)); var (dx, dy) = ship.ToWorld(ship.Deck.CentreX(3), ship.Deck.CentreY(12));
                Assert.True(layer.TryKnockOver(r2.Id, cx - dx, cy - dy));
                enteredAt = water.Tick;
                Assert.False(layer.Rides(r1.Id)); Assert.Equal(FAct.Fled, r1.Act);
                Assert.Equal(SwimState.Sinking, water.All.First(s => s.Person == Mailed).State);
                Assert.Equal(new[] { (Mailed, "blow"), (Swimmer2, "blow") }, sunk.ToArray());                       // both sank at once: 17.5 and 15.5 kg are over the line
            }
            if (t == 60) Assert.False(Gangways.Reaches(ship, 0, qx, qy), "the plank no longer reaches the quay once she is under way");
            if (t > 10 && t < 10 + 34 * 20) Assert.Empty(drowned);                                                    // nobody drowns before the breath is out
            foreach (var f in w.Fighters) if (layer.RiderOf(f.Id) is { } r) Assert.Equal(ship.ToWorld(r.Lx, r.Ly), (f.X, f.Y));
        }
        var m = water.All.First(s => s.Person == Mailed); var o = water.All.First(s => s.Person == Swimmer2);
        output.WriteLine($"mailed man: {m.State} after {m.UnderS} s under (breath {water.K.HoldBreathS + m.Athletics / 2} s); the other: {o.State}, {o.Gear.Count} piece of kit left; the fight: hero {hero.Act}, mate {mate.Act}, raider {r3.Act}");
        // the man in mail cannot swim and does not think to shed: sunk at once, drowned when the breath ran out: HoldBreathS 30 + Athletics 10 / 2 = 35 s = 700 ticks after he went in
        Assert.Equal(SwimState.Drowned, m.State);
        Assert.Equal(35, m.UnderS);
        Assert.Single(drowned); Assert.Equal(Mailed, drowned[0].P); Assert.Equal(enteredAt + 700, drowned[0].Tick);
        Assert.Equal(FAct.Dead, r1.Act);
        Assert.Contains(layer.Events, e => e.Kind == CarrierEventKind.Drowned && e.Person == Mailed);
        // the other dropped his shield (a second's passed test), came up, swam to the quay wall and climbed out in four seconds
        Assert.Equal(SwimState.Out, o.State); Assert.Single(o.Gear); Assert.Equal("gambeson", o.Gear[0].Name);
        Assert.Contains(layer.Events, e => e.Kind == CarrierEventKind.ClimbedOut && e.Person == Swimmer2);
        Assert.Equal(2, layer.Events.Count(e => e.Kind == CarrierEventKind.KnockedOver));
        // the fight went on aboard on the moving frame; the hero is carried (his place is the transform of his local one), the ship has gone
        Assert.True(layer.Rides(hero.Id) || hero.Act is FAct.Dead or FAct.Down);
        Assert.NotEqual((0, 0), (ship.XMm, ship.YMm));
        Assert.True(w.Fighters.Count(f => f.Act is FAct.Dead or FAct.Down or FAct.Stagger or FAct.Windup or FAct.Strike or FAct.Recover or FAct.Block) >= 2, "blows were struck aboard");
    }


    // ------------------------------------------------------------------ swimming and drowning (4.7)

    private static Swimmers Pool(ulong seed = 3) => new(seed);

    private static void Run(Swimmers s, int ticks) { for (int i = 0; i < ticks; i++) s.Step(); }

    [Fact]
    public void ASwimmerInLightKit_SwimsAt06MetresASecond_09FromAthletics40_AndClimbsOutOfTheWaterInFourSeconds()
    {
        foreach (var (ath, mmps) in new[] { (10, 600), (40, 900), (80, 900) })
        {
            var s = Pool(); s.Refuges.Add(new Refuge { Id = 1, XMm = 20_000, YMm = 0 });
            var sw = s.Enter(1, 0, 0, ath, [GearPiece.OfArmour("gambeson", 6000)], "fall");
            Assert.Equal(SwimState.Swimming, sw.State);
            Run(s, 100);                                                                                          // five seconds
            Assert.InRange(sw.XMm, mmps * 5 - 30, mmps * 5 + 1);
            Assert.Equal(0, sw.YMm);
            // to the refuge: within a metre he stops and climbs for 4 s (80 ticks)
            int climbs = 0; while (sw.State == SwimState.Swimming) { s.Step(); climbs++; Assert.True(climbs < 2000); }
            Assert.Equal(SwimState.Climbing, sw.State);
            Run(s, 79); Assert.Equal(SwimState.Climbing, sw.State); Run(s, 1);
            Assert.Equal(SwimState.Out, sw.State);
            Assert.True(sw.XMm >= 19_000 - mmps / 20);
        }
    }

    [Fact]
    public void AboveFifteenKilograms_ASwimmerSinks_UnlessHeHoldsSomethingThatFloatsOrDropsKit_AndDrownsWhenTheBreathRunsOut()
    {
        // 16 kg of kit sinks; the breath is 30 s + Athletics / 2
        foreach (int ath in new[] { 0, 10, 19 })
        {
            var s = Pool(); var sw = s.Enter(1, 0, 0, ath, [GearPiece.OfArmour("mail", 16_000)], "blow");
            Assert.Equal(SwimState.Sinking, sw.State); Assert.False(sw.Sheds);                                    // a man below Athletics 20 does not think to drop it
            int limit = (30 + ath / 2) * 20;
            Run(s, limit - 1); Assert.Equal(SwimState.Sinking, sw.State);
            Run(s, 1); Assert.Equal(SwimState.Drowned, sw.State); Assert.Equal(limit, sw.UnderTicks);
        }
        // exactly 15 kg sinks (the rule is "under 15 kg"); a plank under his arm lifts 5 kg of it
        Assert.Equal(SwimState.Sinking, Pool().Enter(1, 0, 0, 0, [GearPiece.OfArmour("mail", 15_000)], "fall").State);
        Assert.Equal(SwimState.Swimming, Pool().Enter(1, 0, 0, 0, [GearPiece.OfArmour("mail", 14_999)], "fall").State);
        Assert.Equal(SwimState.Swimming, Pool().Enter(1, 0, 0, 0, [GearPiece.OfArmour("mail", 18_000)], "fall", floatG: 5000).State);
        // the sinking raises the callback once; the breath of a strong swimmer is longer
        var s2 = Pool(); int sank = 0; s2.OnSank = _ => sank++;
        var strong = s2.Enter(2, 0, 0, 100, [GearPiece.OfArmour("plate", 30_000)], "fall", sheds: false);
        Assert.Equal(1, sank); Run(s2, (30 + 50) * 20 - 1); Assert.Equal(SwimState.Sinking, strong.State); Run(s2, 1); Assert.Equal(SwimState.Drowned, strong.State);
    }

    [Fact]
    public void AMan_WhoSheds_DropsAShieldInASecondAndMailAtASecondAKilogram_AnAthleticsTestEachSecond_AndSurfacesWhenUnderTheLine()
    {
        var drops = new List<(string Name, long Tick)>();
        var s = Pool(11); s.OnShed = (_, p) => drops.Add((p.Name, s.Tick));
        // 3 kg shield + 12 kg mail + 2.5 kg helm = 17.5 kg: dropping the shield (1 s) leaves 14.5 kg, under the line
        var a = s.Enter(1, 0, 0, 60, [GearPiece.OfArmour("mail", 12_000), GearPiece.OfShield("heater", 3000), GearPiece.OfArmour("helm", 2500)], "blow");
        Assert.True(a.Sheds); Assert.Equal("heater", a.Gear[0].Name);                                                // shields go first
        Assert.Equal(SwimState.Sinking, a.State);
        Run(s, 20 * 6);
        Assert.Equal([("heater", drops[0].Tick)], drops.Take(1).ToArray());
        Assert.Equal(SwimState.Swimming, a.State); Assert.Equal(0, a.UnderTicks);                                    // he surfaced and his breath is his again
        Assert.Equal(["mail", "helm"], a.Gear.Select(g => g.Name).ToArray());
        // a mail shirt alone, 16 kg: sixteen seconds of passed tests, a test a second at 500 + 10 x Athletics permille; with Athletics 90 (950) he is up in about 17 s
        var s2 = Pool(11); var b = s2.Enter(2, 0, 0, 90, [GearPiece.OfArmour("mail", 16_000)], "blow");
        int secs = 0; while (b.State == SwimState.Sinking && secs < 60) { Run(s2, 20); secs++; }
        output.WriteLine($"shedding 16 kg of mail at Athletics 90 took {secs} s");
        Assert.Equal(SwimState.Swimming, b.State); Assert.InRange(secs, 16, 30);
        // the draws are the 0x7D2 stream: the same seed gives the same seconds, another seed other ones
        var s3 = Pool(11); var c = s3.Enter(2, 0, 0, 90, [GearPiece.OfArmour("mail", 16_000)], "blow");
        int secs3 = 0; while (c.State == SwimState.Sinking && secs3 < 60) { Run(s3, 20); secs3++; }
        Assert.Equal(secs, secs3);
    }

    [Fact]
    public void ACurrentCarriesSwimmers_AndTheWaterIsDeterministic()
    {
        ulong Hash(ulong seed)
        {
            var s = new Swimmers(seed) { CurrentXMmps = 800, CurrentYMmps = -300 };
            s.Refuges.Add(new Refuge { Id = 2, XMm = 5000, YMm = 5000 });
            s.Enter(1, 0, 0, 30, [GearPiece.OfArmour("gambeson", 5000)], "fall"); s.Enter(2, 100, 0, 5, [GearPiece.OfArmour("mail", 20_000)], "blow"); s.Enter(3, 0, 900, 30, [GearPiece.OfArmour("mail", 20_000)], "jump");
            Run(s, 400);
            return s.Hash(0);
        }
        Assert.Equal(Hash(4), Hash(4)); Assert.NotEqual(Hash(4), Hash(5));
        var d = new Swimmers(1) { CurrentXMmps = 800, CurrentYMmps = -300 };
        var w = d.Enter(9, 0, 0, 40, [GearPiece.OfArmour("mail", 20_000)], "fall", sheds: false);
        Run(d, 100);                                                                                           // five seconds at 0.8 m/s east and 0.3 m/s north
        Assert.Equal((4000, -1500), (w.XMm, w.YMm));
    }

    [Fact]
    public void ASinkingBoat_PutsItsPeopleInTheWater_NotOnTheBank_AShipThatFoundersAbandonsEveryoneAboard()
    {
        var goods = EconFixture.Catalog(); var book = new RideBook(EconFixture.Content, goods, new MassLedger(goods.Count), 7);
        var boat = book.Vehicles.Create(book.Templates.Get("rowing_boat"), EntityId.None);
        int told = 0; book.OnSunk = v => { Assert.Equal(boat.Id, v.Id); told++; };
        book.Sink(boat);
        Assert.Equal(1, told); Assert.Equal(WaterState.Sunk, boat.Water);
        // a frame's people all go in where they stood
        var ship = Frame("cog", 4, 5000, 5000, FrameMath.FromDegrees(45));
        ship.BoardAt(RiderKind.Person, 11, 2, 9, 0); ship.BoardAt(RiderKind.Person, 12, 4, 12, 0); ship.BoardAt(RiderKind.Item, 5, 3, 3, 0);
        var water = new Swimmers(1); var here = ship.WorldOf(ship.RiderOf(RiderKind.Person, 11)!);
        var swimmers = water.Abandon(ship, id => (id == 11 ? 50 : 10, id == 11 ? [GearPiece.OfArmour("gambeson", 7000)] : [GearPiece.OfArmour("mail", 18_000)]), "sinking", wreckageG: 0);
        Assert.Equal([11UL, 12UL], swimmers.Select(s => s.Person).ToArray());
        Assert.Equal((here.X, here.Y), (swimmers[0].XMm, swimmers[0].YMm));
        Assert.Equal([SwimState.Swimming, SwimState.Sinking], swimmers.Select(s => s.State).ToArray());
        Assert.Single(ship.Riders); Assert.Equal(RiderKind.Item, ship.Riders[0].Kind);                           // the cargo stays with the wreck
        // a barrel from the wreck holds the heavy man up
        var water2 = new Swimmers(1); var held = water2.Enter(12, 0, 0, 10, [GearPiece.OfArmour("mail", 18_000)], "sinking", floatG: 6000);
        Assert.Equal(SwimState.Swimming, held.State);
    }

    [Fact]
    public void TheWaterReadsTheCrawlsNumbers_AndTheColdTakesStaminaUnderTenDegrees()
    {
        var k = SwimTuning.From(EconFixture.Content);
        Assert.Equal((30, 15_000, 500), (k.HoldBreathS, k.SinkGearG, k.SinkCheckPm));
        Assert.Equal((10, 0), (k.ColdDrainPmS(4), k.ColdDrainPmS(12)));
    }
}
