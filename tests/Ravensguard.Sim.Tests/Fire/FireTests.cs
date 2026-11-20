using Ravensguard.Content;
using Ravensguard.Sim.Building;
using Ravensguard.Sim.Combat;
using Ravensguard.Sim.Burning;
using Ravensguard.Sim.Combat.Resolver2;
using Ravensguard.Sim.Combat.Wounds;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Save;
using Ravensguard.Sim.Tests.Building;
using Ravensguard.Sim.Tests.Combat.Resolver2;
using Xunit.Abstractions;

namespace Ravensguard.Sim.Tests.Fire;

/// <summary>
/// Fire and burning (docs/specs/phase6/fire-and-burning.md 7): FB-1 the fuel table, FB-2 one cottage, FB-3 a thatched street (the done-when), FB-4 rain, FB-5 firefighting, FB-6 smoke, FB-7 collapse
/// through Phase 5 construction (in <see cref="FireConstructionTests"/>), FB-8 goods burn (there too), FB-9 the abstract rule, FB-10 arson, FB-11 upkeep, FB-12 firedamp, FB-13 the bubble handoff, FB-14 determinism and saves.
/// </summary>
public class FireTests(BuildingFixture fx, ITestOutputHelper output) : IClassFixture<BuildingFixture>
{
    private static readonly FireWeather Wind5FromWest = new(5000, 270, 0, false, false);       // blowing along the street, eastward
    private static readonly FireWeather Wind5FromNorth = new(5000, 0, 0, false, false);        // across the street, southward
    private static readonly FireWeather Rain = new(5000, 270, 3, false, false);

    private FireRig Rig(ulong seed = 1) => FireRig.Make(fx, seed);

    // ------------------------------------------------------------------ FB-1

    [Fact]
    public void FB1_EveryPiecesFuelClassFollowsItsMaterial_StoneBrickSlateAndTileNeverIgnite()
    {
        var db = fx.Load.Database;
        var fuels = FireFixture.Fuels(fx);
        string[] stone = ["granite", "limestone", "sandstone", "fired_brick", "wrought_iron", "bronze"];
        int checkedPieces = 0;
        foreach (var p in db.All<BuildPieceDef>())
        {
            var c = fuels.ClassOf(p.Id);
            if (p.Id is "straw_pallet" or "hay_stack") { Assert.Equal("thatch", c.Id); continue; }
            if (p.Material == null) { Assert.False(c.Burns, $"{p.Id} has no material and burns"); continue; }
            if (stone.Contains(p.Material)) Assert.False(c.Burns, $"{p.Id} ({p.Material}) burns");
            else if (p.Material == "oak") { Assert.True(c.Burns, p.Id); Assert.Equal(p.Kind is PieceKind.Floor or PieceKind.Beam or PieceKind.StairsUp or PieceKind.StairsDown or PieceKind.Ladder or PieceKind.Trapdoor ? "floor_beam" : "timber", c.Id); }
            else if (p.Material == "thatch") Assert.Equal("thatch", c.Id);
            else if (p.Material == "daub") Assert.Equal("daub", c.Id);
            checkedPieces++;
        }
        Assert.True(checkedPieces > 60);
        // the table's numbers are the spec's
        Assert.Equal((250, 350, 60, 3), (fuels.Find("thatch")!.IgnitionC, fuels.Find("thatch")!.SpreadPm, fuels.Find("thatch")!.BurnS, fuels.Find("thatch")!.MaxIntensity));
        Assert.Equal((300, 120, 300), (fuels.Find("timber")!.IgnitionC, fuels.Find("timber")!.SpreadPm, fuels.Find("timber")!.BurnS));
        Assert.Equal((300, 90, 400), (fuels.Find("floor_beam")!.IgnitionC, fuels.Find("floor_beam")!.SpreadPm, fuels.Find("floor_beam")!.BurnS));
        Assert.Equal((350, 40, 200, 1), (fuels.Find("daub")!.IgnitionC, fuels.Find("daub")!.SpreadPm, fuels.Find("daub")!.BurnS, fuels.Find("daub")!.MaxIntensity));
        Assert.Equal((400, 15, 600), (fuels.Find("turf")!.IgnitionC, fuels.Find("turf")!.SpreadPm, fuels.Find("turf")!.BurnS));
        // a flame held to stone does nothing
        var r = Rig();
        r.House(0, 0, stone: true);
        Assert.False(r.Fw.Ignite(0, 0, 0, r.Sec, FireCause.Test));      // a stone wall
        Assert.False(r.Fw.Ignite(3, 2, 1, r.Sec, FireCause.Test));      // a slate roof
        Assert.Empty(r.Fw.Book.Cells);
        Assert.True(r.Fw.SetFireSeconds(1, 1, 0) == 0 || r.Fw.SetFireSeconds(1, 1, 0) == 10);
    }

    // ------------------------------------------------------------------ FB-2

    [Fact]
    public void FB2_OneCottage_LitAtARoofCorner_InStillAir_RoofGoneIn120To360_WallsBy900_TheStoneHearthStands()
    {
        var results = new List<(int Roof, int Walls)>();
        for (ulong seed = 1; seed <= 10; seed++)
        {
            var r = Rig(seed);
            r.House(0, 0);
            Assert.True(r.Light(0, 0));
            int roofGone = -1, wallsGone = -1;
            for (int s = 1; s <= 1200 && wallsGone < 0; s++)
            {
                r.Run(1);
                if (roofGone < 0 && r.RoofCells(0, 0) == 0) roofGone = s;
                bool walls = false; for (int x = 0; x < 8 && !walls; x++) for (int y = 0; y < 6 && !walls; y++) if (r.Rooms.PieceAt(x, y, 0)?.Kind == PieceKind.Wall) walls = true;
                if (!walls) wallsGone = s;
            }
            Assert.True(roofGone is >= 120 and <= 360, $"seed {seed}: the roof was gone at {roofGone} sim-s");
            Assert.True(wallsGone is > 0 and <= 900, $"seed {seed}: the walls were gone at {wallsGone} sim-s");
            Assert.Equal("hearth", r.Rooms.FixtureAt(1, 1, 0)?.Id);          // the stone hearth stands
            results.Add((roofGone, wallsGone));
        }
        output.WriteLine("roof / walls gone (sim-s): " + string.Join(", ", results.Select(x => $"{x.Roof}/{x.Walls}")));
        // the chronicle: fire_started names the cause, fire_ended the losses
        var one = Rig(3); one.House(0, 0); one.Light(0, 0, FireCause.Hearth); one.RunOut(2000);
        var started = Assert.Single(one.Chron.ByType("fire_started"));
        Assert.Equal("ch_fire_started", started.TextKey);
        var ended = Assert.Single(one.Chron.ByType("fire_ended"));
        Assert.Equal(started.Id, ended.Causes[0].EventId);
        Assert.Contains(ended.Params!, p => p.Name == "buildings" && p.Value >= 1);
    }

    // ------------------------------------------------------------------ FB-3 (done-when, OV-4)

    private (int Burnt, int Sim, bool StoneBurnt) StreetFire(ulong seed, FireWeather wx, int houses = 10, bool stone = true, int maxSim = 3000)
    {
        var r = Rig(seed); r.Wx = wx;
        var street = r.Street(houses, stone);
        r.Light(0, 0);
        int sim = r.RunOut(maxSim);
        int burnt = street.Take(houses).Count(h => r.Burnt(h.X, h.Y));
        return (burnt, sim, stone && r.Burnt(street[^1].X, street[^1].Y));
    }

    [Fact]
    public void FB3_AThatchedStreet_WindAlongIt_BurnsEndToEnd_In600To1800_SimSeconds_AStoneHouseDoesNot()
    {
        var times = new List<int>();
        for (ulong seed = 1; seed <= 10; seed++)
        {
            var (burnt, sim, stoneBurnt) = StreetFire(seed, Wind5FromWest);
            Assert.Equal(10, burnt);
            Assert.False(stoneBurnt);
            Assert.True(sim is >= 600 and <= 1800, $"seed {seed}: the street took {sim} sim-s");
            times.Add(sim);
        }
        output.WriteLine("end to end, sim-s: " + string.Join(", ", times));
    }

    [Fact]
    public void FB3_WindAcross_30To70PercentOfTheHousesBurn()
    {
        int total = 0, burnt = 0;
        var per = new List<int>();
        for (ulong seed = 1; seed <= 20; seed++)
        {
            var (b, _, stoneBurnt) = StreetFire(seed, Wind5FromNorth);
            Assert.False(stoneBurnt);
            burnt += b; total += 10; per.Add(b);
        }
        output.WriteLine("houses burnt of 10 across seeds: " + string.Join(", ", per));
        double pct = 100.0 * burnt / total;
        Assert.InRange(pct, 30, 70);
    }

    // ------------------------------------------------------------------ FB-4

    [Fact]
    public void FB4_TheSameStreetInSteadyRain_StaysInTheFirstOneOrTwoHouses()
    {
        var per = new List<int>();
        for (ulong seed = 1; seed <= 10; seed++) per.Add(StreetFire(seed, Rain).Burnt);
        output.WriteLine("houses burnt in rain: " + string.Join(", ", per));
        Assert.True(per.Count(b => b <= 2) >= 9, "rain held the fire to two houses in " + per.Count(b => b <= 2) + " of 10");
        Assert.True(per.Average() <= 2.0);
    }

    // ------------------------------------------------------------------ FB-5

    private int FoughtStreet(ulong seed, int villagers, bool hooks)
    {
        var r = Rig(seed); r.Wx = Wind5FromWest;
        var street = r.Street(10, true);
        r.Light(0, 0);
        // a well 30 m from the fire's start, villagers in a ring by the first houses
        r.Fw.WaterSources = () => [(-30, 3)];
        for (int i = 0; i < villagers; i++) r.Fighters.Add(new FireFighter(100UL + (ulong)i, 4 + i, 8, 0, hooks && i % 6 == 0));
        r.RunOut(3000);
        return street.Take(10).Count(h => r.Burnt(h.X, h.Y));
    }

    [Fact]
    public void FB5_TwelveVillagersWithBucketsAndAWell30mAway_FormAChain_AndHoldTheFireToThreeHousesOrFewer_In8Of10Seeds()
    {
        var held = new List<int>(); var alone = new List<int>();
        for (ulong seed = 1; seed <= 10; seed++) { held.Add(FoughtStreet(seed, 12, hooks: true)); alone.Add(FoughtStreet(seed, 0, hooks: false)); }
        output.WriteLine("with 12 fighters: " + string.Join(", ", held) + "; without: " + string.Join(", ", alone));
        Assert.True(held.Count(b => b <= 3) >= 8, "held to three houses in " + held.Count(b => b <= 3) + " of 10 seeds");
        Assert.True(held.Average() < alone.Average());
    }

    [Fact]
    public void FB5_ABucketChainFormsWithFourOrMore_ThreeBucketsKnockOneLevelOff_SoakedThatchResists()
    {
        var r = Rig(1); r.House(0, 0); r.Light(0, 0);
        r.Fw.WaterSources = () => [(-5, 0)];
        r.Run(45);                                                    // the roof corner has risen to its high intensity
        var cell = r.Fw.CellAt(1, 1, 1)!; Assert.True(cell.Intensity >= 2);
        // fewer than four: individual round trips
        for (int i = 0; i < 3; i++) r.Fighters.Add(new FireFighter(1UL + (ulong)i, 0, 0, 0));
        long before = r.Fw.Book.Cells.Values.Sum(c => c.Knock + c.Buckets);
        r.Run(10);
        long few = r.Fw.Book.Cells.Values.Sum(c => c.Knock + c.Buckets) - before;
        // four or more: a chain, one bucket per three sim-seconds a chain
        var r2 = Rig(1); r2.House(0, 0); r2.Light(0, 0); r2.Fw.WaterSources = () => [(-5, 0)]; r2.Run(45);
        for (int i = 0; i < 8; i++) r2.Fighters.Add(new FireFighter(1UL + (ulong)i, 0, 0, 0));
        long before2 = r2.Fw.Book.Cells.Values.Sum(c => c.Knock + c.Buckets);
        r2.Run(10);
        long chain = r2.Fw.Book.Cells.Values.Sum(c => c.Knock + c.Buckets) - before2;
        Assert.True(chain > few, $"a chain of 8 threw more than 3 alone ({chain} vs {few})");
        // a soaked roof cell resists: x0.3 for two game hours
        var r3 = Rig(2); r3.House(0, 0);
        r3.Fw.Book.Soaked[FireWorld.Key(2, 1, 1)] = 2 * 3600;
        Assert.True(r3.Fw.Book.Soaked.ContainsKey(FireWorld.Key(2, 1, 1)));
    }

    // ------------------------------------------------------------------ FB-6

    /// <summary>A stone room of 4 x 3 cells (36 m3), a slate roof, four beds that burn: exactly four burning cells in the room.</summary>
    private FireRig SmokeRoom(ulong seed, out ulong sleeper, BodyPlanV2 plan, WoundLedger ledger)
    {
        var r = Rig(seed);
        var e = new List<CellEdit>();
        for (int y = 0; y < 5; y++) for (int x = 0; x < 6; x++) { bool ring = x == 0 || y == 0 || x == 5 || y == 4; e.Add(new CellEdit(x, y, 0, CellLayer.Structure, ring ? "wall_stone" : "floor_flagstone")); }
        r.Rooms.Apply(e); r.Rooms.RoofOver("roof_slate");
        r.Fw.Wounds = ledger; r.Fw.PlanOf = _ => plan;
        sleeper = 77;
        return r;
    }

    [Fact]
    public void FB6_ASleepersAirLastsAboutFiveSeconds_ThenUnconsciousAt30_DeadAt240_UnlessPulledOut()
    {
        var v2 = new V2Fixture();
        var ledger = new WoundLedger(1, 0);
        var r = SmokeRoom(5, out _, v2.Human, ledger);
        var room = r.Rooms.RoomAt(1, 1, 0)!; Assert.True(room.Indoor); Assert.Equal(12, room.Area);
        // four beds on four cells (a bed is the only thing that burns: stone floor, walls and roof do not)
        foreach (var (x, y) in new[] { (1, 1), (3, 1), (1, 3), (3, 3) }) r.Rooms.SetFixture(x, y, 0, "bed");
        foreach (var (x, y) in new[] { (1, 1), (3, 1), (1, 3), (3, 3) }) Assert.True(r.Fw.Ignite(x, y, 0, r.Sec, FireCause.Test));
        foreach (var c in r.Fw.Book.Cells.Values) c.FuelLeftS = 100000;
        r.People.Add(new FirePerson(77, 2, 2, 0, Asleep: true));          // asleep in the smoke
        r.People.Add(new FirePerson(78, 2, 2, 0, Asleep: true));          // asleep, and pulled out at 100 s
        long startedAt = r.Sec / 20;
        int stopped = -1;
        for (int s = 1; s <= 260; s++)
        {
            r.Run(1);
            if (s == 100) { r.People.RemoveAll(p => p.Person == 78); r.People.Add(new FirePerson(78, -3, -3, 0)); }          // carried out of the room, into the street
            if (stopped < 0 && r.Fw.Book.Exposed.TryGetValue(77, out var ex) && ex.StopSec >= 0) stopped = s;
            lock (ledger.Sync) ledger.AdvanceTo(r.Sec);
            if (s == 30) { Assert.True(stopped is >= 4 and <= 6, $"the air lasted {stopped} sim-s (36 m3 over 4 cells x 2)"); }
            if (s == stopped + 20) Assert.True(ledger.Find(77)!.Conscious, "still awake 20 s after his breath stopped");
            if (s == stopped + 34) Assert.False(ledger.Find(77)!.Conscious, "unconscious at about 30 s without air");
        }
        Assert.True(ledger.Find(77)!.Dead);                                 // dead at 240 s without air
        Assert.Equal(DeathKind.Breath, ledger.Find(77)!.Death);
        var rescued = ledger.Find(78);
        Assert.True(rescued == null || !rescued.Dead);                      // pulled out at 100 s: breathing again
        Assert.True(r.Fw.Book.Exposed.TryGetValue(78, out var e78) ? e78.StopSec < 0 : true);
        Assert.Equal(77UL, r.Fw.Book.Exposed.Keys.Where(k => k == 77).Single());
        Assert.Equal(startedAt, r.Fw.Book.Cells.Values.First().StartS / 20);
    }

    [Fact]
    public void FB6_StandingInAFlame_BurnsTheExposedParts_GradedByIntensity()
    {
        var v2 = new V2Fixture();
        var ledger = new WoundLedger(1, 0);
        var r = SmokeRoom(6, out _, v2.Human, ledger);
        r.Rooms.SetFixture(2, 2, 0, "bed");
        Assert.True(r.Fw.Ignite(2, 2, 0, r.Sec, FireCause.Test));
        r.Fw.Book.Cells.Values.First().FuelLeftS = 10000;
        r.People.Add(new FirePerson(9, 2, 2, 0));
        r.Run(60);
        var book = ledger.Find(9)!;
        var burns = book.Records.Where(w => w.Kind == WoundKind.Burn).ToList();
        Assert.True(burns.Count >= 8, $"{burns.Count} burn wounds in 60 s (one every 5 sim-s)");
        Assert.Contains(burns, w => w.DepthPm >= 900);                      // intensity 3 deep burns once it has risen
        Assert.Contains(burns, w => w.DepthPm == 250);                      // and a scorch at the start
        Assert.True(book.PainX100 > 0);
        Assert.Equal(1UL, r.Fw.Book.Exposed.ContainsKey(9) ? 1UL : 0UL);
    }

    // ------------------------------------------------------------------ FB-9

    [Fact]
    public void FB9_Accidents_TenThousandTownThatchedDwellingYears_GiveTwentyPlusMinusFive_AndTheStonTownLosesNoneToSpread()
    {
        // the daily roll of FireWorld.DayAccidents (stream 0x6C3, key structure and day), 0.2% a game year in a town
        long ppb = FireWorld.AccidentPpbPerDay(town: true, winter: false, drought: false);
        Assert.Equal(2_000_000 / 60, ppb);
        var runs = new List<int>();
        foreach (ulong seed in new ulong[] { 1234, 1, 2, 3, 4 })
        {
            int fires = 0;
            for (ulong structure = 1; structure <= 10_000; structure++)
                for (long day = 0; day < 60; day++)
                    if (DetRng.Range(seed, day, structure, RngStreams.FireAccidents, 0, 0, 1_000_000_000) < ppb) fires++;
            runs.Add(fires);
        }
        output.WriteLine("accidental fires in 10,000 dwelling-years, five seeds: " + string.Join(", ", runs));
        Assert.InRange(runs.Average(), 15, 25);
        Assert.All(runs, f => Assert.InRange(f, 8, 32));
        Assert.Equal(2 * 2_000_000 / 60, FireWorld.AccidentPpbPerDay(true, true, false));         // x2 in winter
        Assert.Equal(3 * 1_000_000 / 60, FireWorld.AccidentPpbPerDay(false, false, true));        // x3 in a drought, villages 0.1%
        // the abstract spread: the table's chances per hour, measured
        int caught = 0, trials = 400;
        for (ulong seed = 1; seed <= (ulong)trials; seed++)
        {
            var r = Rig(seed);
            int a = r.House(0, 0), b = r.House(14, 0);
            Assert.True(r.Fw.IgniteBuilding(a, 0, FireCause.Hearth));
            r.Fw.HourStep(0);
            if (r.Fw.Book.Buildings.ContainsKey(b)) caught++;
        }
        double pct = 100.0 * caught / trials;
        output.WriteLine($"thatch next door, 6 m apart, calm and dry: {pct:F1}% in the first hour (table: 40%)");
        Assert.InRange(pct, 33, 47);
        // a stone town: stone walls under slate lose none, whatever burns beside them
        int stoneCaught = 0;
        for (ulong seed = 1; seed <= 100; seed++)
        {
            var r = Rig(seed);
            int a = r.House(0, 0), b = r.House(14, 0, stone: true), c = r.House(0, 14, stone: true);
            r.Fw.IgniteBuilding(a, 0, FireCause.Hearth);
            for (int h = 0; h < 6; h++) r.Fw.HourStep(h);
            if (r.Fw.Book.Buildings.ContainsKey(b) || r.Fw.Book.Buildings.ContainsKey(c)) stoneCaught++;
        }
        Assert.Equal(0, stoneCaught);
        // a burning building is out after three game hours, gutted (thatch and timber 1000 pm)
        var g = Rig(1); int gb = g.House(0, 0); g.Fw.IgniteBuilding(gb, 0, FireCause.Hearth);
        for (int h = 0; h < 3; h++) g.Fw.HourStep(h);
        Assert.Empty(g.Fw.Book.Buildings);
        Assert.Equal(0, g.RoofCells(0, 0));
        Assert.DoesNotContain(g.Chron.All, e => e.TypeId == "x");
        Assert.Single(g.Chron.ByType("fire_ended"));
    }

    [Fact]
    public void FB9_WindAndWetAndTheBucketChain_MoveTheAbstractSpread()
    {
        double Rate(FireWeather w, bool adults)
        {
            int caught = 0, n = 300;
            for (ulong seed = 1; seed <= (ulong)n; seed++)
            {
                var r = Rig(seed); r.Wx = w;
                if (adults) r.Fw.AbleAdultsNear = (_, _) => 12;
                int a = r.House(0, 0), b = r.House(14, 0);
                r.Fw.IgniteBuilding(a, 0, FireCause.Hearth); r.Fw.HourStep(0);
                if (r.Fw.Book.Buildings.ContainsKey(b)) caught++;
            }
            return 100.0 * caught / n;
        }
        double calm = Rate(FireWeather.Calm, false), down = Rate(Wind5FromWest, false), up = Rate(new FireWeather(5000, 90, 0, false, false), false), rain = Rate(new FireWeather(0, 0, 3, false, false), false), chain = Rate(FireWeather.Calm, true);
        output.WriteLine($"calm {calm:F0}%, downwind {down:F0}%, upwind {up:F0}%, rain {rain:F0}%, bucket chain {chain:F0}%");
        Assert.True(down > calm + 20 && up < calm - 10 && rain < calm / 3 && chain < calm * 0.7);
    }

    // ------------------------------------------------------------------ FB-10

    [Fact]
    public void FB10_ASetFire_NamesTheHerosAct_TheDeathsChainToIt_TheArsonBountyIs2000PlusDamageOverTen()
    {
        var hero = new EntityId(0x1_0000_0001);
        var r = Rig(1);
        r.House(0, 0);
        Assert.True(r.Fw.Ignite(1, 1, 1, r.Sec, FireCause.Person(hero, "arson")));
        var started = Assert.Single(r.Chron.ByType("fire_started"));
        Assert.Equal(hero, Assert.Single(started.Subjects));
        Assert.Equal(CauseKind.Crime, Assert.Single(started.Causes).Kind);
        Assert.Equal("ch_note_fire_arson", started.Causes[0].NoteKey);
        Assert.Equal("arson", started.Data["cause"]);
        // someone burns in it and dies: the cause chain of his death is the fire's entry
        r.Fw.Book.Exposed[5] = new Exposure { Person = 5, Fire = started.Id.Value, SmokeS = 3 };
        Assert.Equal(started.Id, r.Fw.FireOfDeath(5));
        r.Fw.NoteDeath(5);
        r.RunOut(1500);
        var ended = Assert.Single(r.Chron.ByType("fire_ended"));
        Assert.Equal(started.Id, ended.Causes[0].EventId);
        Assert.Equal("1", ended.Data["deaths"]);
        Assert.Contains(ended.Subjects, s => s.Value == 5);
        Assert.Equal(2_000 + 450, FireWorld.ArsonBountyMites(4_500));
        Assert.Equal(2_000, FireWorld.ArsonBountyMites(0));
        // a riot's fire and a hearth's: the cause rides in the entry
        var r2 = Rig(2); r2.House(0, 0); var riot = r2.Ids.Allocate(IdKind.Event);
        r2.Fw.Ignite(1, 1, 1, r2.Sec, FireCause.Riot(riot));
        Assert.Equal(riot, r2.Chron.ByType("fire_started").Single().Causes[0].EventId);
    }

    // ------------------------------------------------------------------ FB-11

    [Fact]
    public void FB11_Thatch_LosesFiftyAYear_AtThreeHundredTheRoofLeaks_AndCatchesAtOnePointFiveTimes()
    {
        var r = Rig(1); r.House(0, 0);
        long year = TimeConst.SecondsPerYear;
        r.Fw.MarkMade(3, 2, 1, 0);
        Assert.Equal(1000, r.Fw.ConditionAt(3, 2, 1, 0));
        Assert.Equal(950, r.Fw.ConditionAt(3, 2, 1, year));
        Assert.Equal(900, r.Fw.ConditionAt(3, 2, 1, 2 * year));
        Assert.Equal(300, r.Fw.ConditionAt(3, 2, 1, 14 * year));
        Assert.False(r.Fw.Leaking(3, 2, 1, 13 * year));
        Assert.True(r.Fw.Leaking(3, 2, 1, 15 * year));
        Assert.Contains(r.Fw.RepairsDue(13 * year), d => d.Kind == "thatch" && d.ConditionPm < 400);        // posted below 400
        Assert.DoesNotContain(r.Fw.RepairsDue(5 * year), d => d.Kind == "thatch");
        Assert.Equal(1, r.Fw.Mend(r.Rooms.StructureAt(3, 2, 0), "thatch", 0) > 0 ? 1 : 0);
        Assert.Equal(1000, r.Fw.ConditionAt(3, 2, 1, 0));
        // daub and timber rot at their own rates
        Assert.Equal(100, r.Fw.Fuels.DecayPmPerYear(r.Fw.Fuels.Find("daub")!));
        Assert.Equal(10, r.Fw.Fuels.DecayPmPerYear(r.Fw.Fuels.Find("timber")!));
        // the spread chance at 1.5x: rotten thatch next to a burning cell catches 1.5 times as often as sound thatch
        int Caught(bool rotten)
        {
            int n = 0;
            for (ulong seed = 1; seed <= 1500; seed++)
            {
                var q = FireRig.Make(fx, seed);
                q.Rooms.SetPiece(0, 0, 1, "roof_thatch"); q.Rooms.SetPiece(1, 0, 1, "roof_thatch");
                if (rotten) q.Fw.MarkMade(1, 0, 1, 0);
                q.Sec = 15 * year + 100;                                          // the rotten one is 15 years old
                q.Fw.Ignite(0, 0, 1, q.Sec, FireCause.Test);
                var c = q.Fw.CellAt(0, 0, 1)!; c.StartS = q.Sec - 20 * 20; c.FuelLeftS = 10000;       // intensity 2
                q.Fw.Book.SecDone = q.Sec / 20 - 1;
                q.Run(5);
                if (q.Fw.IsBurning(1, 0, 1) || q.Rooms.PieceAt(1, 0, 1) == null) n++;
            }
            return n;
        }
        int sound = Caught(false), rot = Caught(true);
        output.WriteLine($"caught of 1500: sound {sound}, rotten {rot}");
        Assert.InRange((double)rot / sound, 1.3, 1.7);
    }

    // ------------------------------------------------------------------ FB-12

    private static (int X, int Y) FindGas(ulong seed, ulong site, bool coal)
    {
        for (int y = 0; y < 200; y++) for (int x = 0; x < 200; x++) if (Firedamp.Gassy(seed, site, x, y, -3, coal)) return (x, y);
        throw new InvalidOperationException("no gas in 200 x 200 cells");
    }

    [Fact]
    public void FB12_Firedamp_ATorchInAGassyCellBlastsRadius8_WoundsByDistance_ACandleBurnsBlueBefore()
    {
        const ulong site = 7;
        int gas = 0, n = 20_000;
        for (int i = 0; i < n; i++) if (Firedamp.Gassy(1234, site, i % 200, i / 200, -3, true)) gas++;
        output.WriteLine($"gassy coal-seam cells: {gas} of {n}");
        Assert.InRange(100.0 * gas / n, 8, 12);                                          // 10% of a coal seam
        Assert.False(Firedamp.Gassy(1234, site, 5, 5, -1, false));                       // shallow rock holds none
        var (gx, gy) = FindGas(1234, site, true);
        var book = new FireBook();
        // the warning: a flame within two cells of the gas burns blue, and the gas is only blasted by a flame in the cell itself
        Assert.True(Firedamp.BlueFlame(1234, site, gx + 2, gy, -3, true, book));
        var v2 = new V2Fixture(); var ledger = new WoundLedger(1, 0);
        var r = Rig(1234); r.Fw.Wounds = ledger; r.Fw.PlanOf = _ => v2.Human;
        var crush = new List<(ulong P, long Momentum)>(); var down = new List<ulong>(); var checks = new List<int>();
        r.Fw.Crush = (p, m, _, _) => crush.Add((p, m)); r.Fw.KnockDown = (p, _) => down.Add(p); r.Fw.CaveInChecks = (cells, _) => checks.Add(cells.Count);
        // five people at 0, 3, 6, 8 and 12 m
        int[] dist = [0, 3, 6, 8, 12];
        for (int i = 0; i < dist.Length; i++) r.People.Add(new FirePerson(200UL + (ulong)i, gx + dist[i], gy, -3));
        var rep = r.Fw.FlameIn(site, gx, gy, -3, r.Sec, carrier: 200, coalSeam: true);
        Assert.NotNull(rep);
        Assert.True(rep!.Vented >= 1);
        Assert.Equal(4, rep.Burned);                                                     // 12 m is out of range
        Assert.True(rep.CarrierDown); Assert.Equal([200UL], down);
        Assert.Single(checks); Assert.True(checks[0] > 100);                             // a cave-in check on every cell in range
        Assert.Equal(4, crush.Count);
        Assert.True(crush[0].Momentum > crush[1].Momentum && crush[1].Momentum > crush[3].Momentum);
        int Depth(ulong p) => ledger.Find(p)!.Records.Where(w => w.Kind == WoundKind.Burn).Max(w => w.DepthPm);
        Assert.Equal(900, Depth(200)); Assert.Equal(600, Depth(201)); Assert.Equal(250, Depth(203));   // 3 at the centre, 2, 1 at the edge
        Assert.Null(ledger.Find(204));
        Assert.Null(r.Fw.FlameIn(site, gx, gy, -3, r.Sec, 200, true));                  // the gas is gone
        Assert.Single(r.Chron.ByType("firedamp_blast"));
        Assert.False(Firedamp.BlueFlame(1234, site, gx + 2, gy + 30, -3, true, book) && false);
        Assert.True(Firedamp.Names(40)); Assert.False(Firedamp.Names(39));
    }

    // ------------------------------------------------------------------ FB-13

    [Fact]
    public void FB13_ABuildingBurningInTheAbstract_BecomesLiveCellsOnTheSamePieces_TheDamageIsNotCountedTwice()
    {
        var r = Rig(1);
        int st = r.House(0, 0);
        var profile = r.Fw.ProfileOf(st)!;
        int flammable = profile.FlammableCells;
        Assert.True(flammable > 40);
        Assert.True(r.Fw.IgniteBuilding(st, 0, FireCause.Hearth));
        r.Fw.HourStep(0);                                                                // one hour: a third of it burnt
        var bf = r.Fw.Book.Buildings[st];
        Assert.Equal(333, bf.DamagePm);
        int goneAbstract = bf.Taken;
        Assert.InRange(goneAbstract, flammable / 4, flammable / 2);
        int standing = Flammable(r, 0, 0);
        Assert.Equal(flammable - goneAbstract, standing);
        // the hero arrives: the rest of the front is live fire on the pieces that still stand
        r.Sec = 3600;
        int live = r.Fw.EnterBubble(st, r.Sec);
        Assert.True(live > 0);
        Assert.DoesNotContain(st, r.Fw.Book.Buildings.Keys);
        foreach (var c in r.Fw.Book.Cells.Values) { var (x, y, z) = FireWorld.Unpack(c.Key); Assert.True(r.Rooms.PieceAt(x, y, z) != null || r.Rooms.FixtureAt(x, y, z) != null, "a live cell stands on a piece"); }
        Assert.Single(r.Chron.ByType("fire_started"));                                    // the same fire, not a new one
        r.RunOut(2000);
        // gutted: every flammable piece gone once, and the scars count each cell once
        Assert.Equal(0, Flammable(r, 0, 0));
        Assert.Equal(flammable, r.Fw.Book.Scars.Count + 0 * 1);
        Assert.Single(r.Chron.ByType("fire_ended"));
        // and the other way: a live fire the live tier stopped stepping goes back to a building fire
        var q = Rig(2); int qs = q.House(0, 0); q.Light(0, 0); q.Run(30);
        Assert.Equal(1, q.Fw.Retire(5));
        Assert.Empty(q.Fw.Book.Cells); Assert.True(q.Fw.Book.Buildings.ContainsKey(qs));
    }

    private static int Flammable(FireRig r, int x0, int y0)
    {
        int n = 0;
        for (int z = 0; z <= 1; z++) for (int y = 0; y < 6; y++) for (int x = 0; x < 8; x++)
        {
            if (r.Fw.FuelAt(x0 + x, y0 + y, z).Burns) n++;
        }
        return n;
    }

    // ------------------------------------------------------------------ FB-14

    /// <summary>The generated houses of a street (8 x 6 m, timber, a hearth and a bed, no roof: the fire roofs them when it adopts them), 14 m apart along x, as the chunk store would hand them over.</summary>
    private static void GeneratedStreet(FireWorld fw, int n)
    {
        fw.Adopt = (x0, y0, x1, y1) => Enumerable.Range(0, n).SelectMany(i => HouseEdits(i * 14, 0)).Where(e => e.X >= x0 && e.X <= x1 && e.Y >= y0 && e.Y <= y1).ToList();
    }

    private static IEnumerable<CellEdit> HouseEdits(int x0, int y0)
    {
        for (int y = 0; y < 6; y++)
            for (int x = 0; x < 8; x++)
            {
                bool ring = x == 0 || y == 0 || x == 7 || y == 5;
                yield return new CellEdit(x0 + x, y0 + y, 0, CellLayer.Structure, ring ? (x == 3 && y == 5 ? "door_wood" : "wall_timber") : "floor_plank");
            }
        yield return new CellEdit(x0 + 1, y0 + 1, 0, CellLayer.Fixture, "hearth"); yield return new CellEdit(x0 + 5, y0 + 1, 0, CellLayer.Fixture, "bed");
    }

    /// <summary>What a real load does: a room world of its own (construction's, rebuilt from the built cells only, so none here), the book as saved, the sections as bytes, the world attached over them.</summary>
    private FireRig Load(FireRig a, ulong seed, int houses)
    {
        var fire = SaveCodec.SerializeCompressed(FireSave.Capture(a.Fw.Book));
        var adopted = FireAdoptedSave.Capture(a.Fw.Book) is { } ad ? SaveCodec.SerializeCompressed(ad) : null;
        var book = SaveCodec.DeserializeCompressed<FireSave>(fire).Restore();
        if (adopted != null) SaveCodec.DeserializeCompressed<FireAdoptedSave>(adopted).RestoreInto(book);
        var b = FireRig.Make(fx, seed, rooms: fx.Lib.NewWorld(), book: book);
        GeneratedStreet(b.Fw, houses);
        b.Fw.RestoreAdopted();
        b.Wx = a.Wx; b.Sec = a.Sec;
        return b;
    }

    [Fact]
    public void FB14_SaveMidFire_Load_Save_AreTheSameBytes_AndTheFireContinuesToTheSameCells()
    {
        // the street is generated and adopted by the fire as it spreads; the second world is rebuilt as a load does, over an empty room world (S15)
        var a = Rig(7); a.Wx = Wind5FromWest;
        GeneratedStreet(a.Fw, 4);
        a.Fw.EnsureAdopted(0, 0);
        a.Light(0, 0);
        a.Run(150);
        Assert.True(a.Fw.Book.Cells.Count > 5);
        Assert.NotEmpty(a.Fw.Book.AdoptedBlocks);
        var s1 = SaveCodec.SerializeCompressed(FireSave.Capture(a.Fw.Book));
        var restored = SaveCodec.DeserializeCompressed<FireSave>(s1).Restore();
        var s2 = SaveCodec.SerializeCompressed(FireSave.Capture(restored));
        Assert.True(s1.AsSpan().SequenceEqual(s2), "save, load, save are the same bytes");
        Assert.Equal(a.Fw.Book.Hash(), restored.Hash());
        var b = Load(a, 7, 4);
        Assert.Equal(a.Rooms.StructureParts().Select(p => (p.Id, string.Join(',', p.Cells))), b.Rooms.StructureParts().Select(p => (p.Id, string.Join(',', p.Cells))));
        Assert.Equal(a.Rooms.Dump(), b.Rooms.Dump());
        var d1 = SaveCodec.SerializeCompressed(FireAdoptedSave.Capture(a.Fw.Book)!); var d2 = SaveCodec.SerializeCompressed(FireAdoptedSave.Capture(b.Fw.Book)!);
        Assert.True(d1.AsSpan().SequenceEqual(d2), "the adopted section saves the same bytes after a load");
        a.Run(400); b.Run(400);
        Assert.Equal(a.Fw.Book.Hash(), b.Fw.Book.Hash());
        Assert.Equal(a.Fw.Book.Cells.Keys, b.Fw.Book.Cells.Keys);
        Assert.Equal(a.Fw.Book.Scars.Keys, b.Fw.Book.Scars.Keys);
        // the book is optional: an untouched one is empty and writes nothing, and a fire that adopted nothing writes no adopted section
        Assert.True(new FireBook().IsEmpty);
        Assert.False(a.Fw.Book.IsEmpty);
        Assert.Null(FireAdoptedSave.Capture(new FireBook { SecDone = 5 }));
    }

    [Fact]
    public void S1_ABuildingBurningInTheAbstract_InAnAdoptedGeneratedHouse_SurvivesSaveAndLoad_AndTheDailyRollsSeeTheSameHouses()
    {
        var a = Rig(3);
        GeneratedStreet(a.Fw, 1);
        a.Fw.EnsureAdopted(2, 2);
        int st = a.Rooms.StructureAt(2, 2, 0);
        Assert.True(st > 0 && a.Fw.ProfileOf(st) != null);
        Assert.True(a.Fw.IgniteBuilding(st, 0, FireCause.Test));
        var b = Load(a, 3, 1);
        Assert.Equal(st, b.Rooms.StructureAt(2, 2, 0));                               // the same structure id
        Assert.NotNull(b.Fw.ProfileOf(st));                                           // a profile to burn
        Assert.Equal(a.RoofCells(0, 0), b.RoofCells(0, 0));
        a.Fw.HourStep(1); b.Fw.HourStep(1);
        Assert.Single(a.Fw.Book.Buildings); Assert.Single(b.Fw.Book.Buildings);       // the load did not drop the fire
        Assert.Equal(a.Fw.Book.Hash(), b.Fw.Book.Hash());
        Assert.Equal(a.Rooms.StructureIds.ToList(), b.Rooms.StructureIds.ToList());   // the daily and storm rolls run over the same dwellings
    }

    [Fact]
    public void FB14_TwoRunsOfOneSeed_AreOne_AndAnotherSeedGoesAnotherWay()
    {
        ulong Hash(ulong seed) { var r = Rig(seed); r.Wx = Wind5FromNorth; r.Street(6); r.Light(0, 0); r.Run(900); return r.Fw.Book.Hash(); }
        Assert.Equal(Hash(3), Hash(3));
        Assert.NotEqual(Hash(3), Hash(4));
    }

    // ------------------------------------------------------------------ the faith hook and the response

    [Fact]
    public void AFireThatStopsAtAChurchDoor_HasANaturalExplanation_AStoneWallARainOrAWindChange()
    {
        var r = Rig(1); r.Wx = Wind5FromWest;
        r.House(0, 0);
        // a stone church 8 m east with an altar and a slate roof
        int church = r.House(16, 0, stone: true);
        r.Rooms.SetFixture(17 + 1, 1, 0, "altar");
        r.Light(0, 0);
        r.RunOut(2000);
        Assert.False(r.Burnt(16, 0));
        var stop = Assert.Single(r.Fw.Book.Stops);
        Assert.True(stop.Sacred); Assert.Equal(church, stop.Structure);
        Assert.Contains(stop.Reason, new[] { "stone_wall", "rain", "wind_change" });
    }

    [Fact]
    public void TheResponse_FightScoresAndFleeThreat()
    {
        Assert.True(FireAi.Hears(100)); Assert.False(FireAi.Hears(101));
        int owner = FireAi.FightScore(true, false, false, false, false, 5, 600, false);
        int neighbour = FireAi.FightScore(false, false, true, false, false, 30, 600, false);
        Assert.True(owner > neighbour && neighbour > 0);
        Assert.Equal(1000, FireAi.FightScore(false, false, false, false, true, 0, 600, false));       // his own house downwind: always
        Assert.Equal(0, FireAi.FightScore(true, false, false, false, false, 5, 600, true));            // not from his bed
        Assert.Equal(0, FireAi.FightScore(false, false, false, false, false, 5, 600, false));          // a stranger with no bucket
        Assert.True(FireAi.FleeThreatPm(0) == 1000 && FireAi.FleeThreatPm(5) == 500 && FireAi.FleeThreatPm(10) == 0);
        Assert.True(FireAi.FightScore(true, false, false, false, false, 5, 200, false) < owner);
    }

    [Fact]
    public void Fields_StandingCropsBurnLikeThatch_AndTheyAreArson()
    {
        var r = Rig(1);
        r.Fw.CropAt = (x, y, z) => z == 0 && x is >= 0 and < 6 && y is >= 0 and < 6;
        Assert.True(r.Fw.Ignite(2, 2, 0, r.Sec, FireCause.Person(new EntityId(0x1_0000_0001), "arson")));
        int sim = r.RunOut(600);
        Assert.True(sim > 0);
        Assert.True(r.Fw.Book.Scars.Count >= 6, "the field burnt: " + r.Fw.Book.Scars.Count);
    }
}
