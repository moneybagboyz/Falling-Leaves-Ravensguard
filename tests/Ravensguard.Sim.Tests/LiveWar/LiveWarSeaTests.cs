using Ravensguard.Sim.Armies;
using Ravensguard.Sim.Building;
using Ravensguard.Sim.Burning;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.People;
using Ravensguard.Sim.Save;
using Ravensguard.Sim.Soldiers;
using Ravensguard.Sim.Tests.Armies;
using Ravensguard.Sim.Tests.Building;
using Ravensguard.Sim.Tests.Fire;
using Xunit;
using Xunit.Abstractions;

namespace Ravensguard.Sim.Tests.LiveWar;

/// <summary>P8-LW-4 gate: LW-15 (fire arrows light thatch at 200 pm, wet 20) and LW-16 (a season of ferries, fishing and a voyage in a running world, mass exact), with the drill and the ship parts.</summary>
public class LiveWarSeaTests(BuildingFixture fx, ITestOutputHelper output) : IClassFixture<BuildingFixture>
{
    [Fact]
    public void LW15_FireArrows_ADryThatchRoofLightsAt200Pm_OverAThousandHits_WetAt20_StoneNever()
    {
        var rig = FireRig.Make(fx, 7); rig.House(0, 0); rig.House(20, 0, stone: true);
        var fw = rig.Fw; var archer = EntityId.Make(IdKind.Person, 5);
        Assert.Equal("thatch", fw.FuelAt(3, 2, 1).Id);
        int Shots(bool wet, int x, int y, int z, int n)
        {
            int lit = 0;
            for (uint s = 1; s <= n; s++)
            {
                if (FireArrows.Strike(fw, x, y, z, 100, 7, s, wet, archer)) lit++;
                fw.Book.Cells.Remove(FireWorld.Key(x, y, z));                                      // put it out between shots so the next hit meets unburnt thatch
            }
            return lit;
        }
        int dry = Shots(false, 3, 2, 1, 1000), wet = Shots(true, 3, 2, 1, 1000);
        output.WriteLine($"1000 fire arrows on thatch: dry {dry} lit, wet {wet} lit");
        Assert.InRange(dry, 200 - 20, 200 + 20); Assert.InRange(wet, 20 - 12, 20 + 12);
        Assert.Equal(0, Shots(false, 23, 2, 1, 1000));                                             // a slate roof does not light
        Assert.Equal(200, FireArrows.ChancePm(fw.FuelAt(3, 2, 1), false)); Assert.Equal(20, FireArrows.ChancePm(fw.FuelAt(3, 2, 1), true));
        Assert.Equal(Shots(false, 3, 2, 1, 300), Shots(false, 3, 2, 1, 300));                      // the same shots light the same cells
        var started = rig.Chron.All.Where(e => e.TypeId == "fire_started").ToList();
        Assert.NotEmpty(started); Assert.All(started, e => { Assert.NotEmpty(e.Causes); Assert.Contains(e.Causes, c => c.NoteKey == "ch_note_fire_arrow"); });
    }

    [Fact]
    public void LW16_ARunningSea_ASeasonOfFishingFerriesAndABarge_MassAndCoinExact_SavedAndResumedByteForByte()
    {
        using var k = new ArmyRig(); k.Raised(); var aw = k.Aw; var eco = k.State.Economy!; var led = eco.Ledger;
        ushort fish = eco.Goods.IdOf("fish_fresh"); var rides = Ravensguard.Sim.Carts.VehicleDesk.BookOf(k.State, k.State.Content!);
        // the test region is inland: a sea is laid along the east of the busiest village (a column of sea tiles 12 tiles out), and a wide ford is put on a road beside it
        var region = k.State.Population.Region; int v = Enumerable.Range(0, region.Settlements.Count).OrderByDescending(x => aw.People.HomeSettlement.Take(aw.People.Count).Count(h => h == x)).First();
        int S = Ravensguard.Sim.WorldGen.Region.Region.Size, T = Ravensguard.Sim.WorldGen.Region.Region.TileM, tx = region.Settlements[v].CenterX / T + 12, ty = region.Settlements[v].CenterY / T;
        for (int y = Math.Max(0, ty - 40); y < Math.Min(S, ty + 40); y++) for (int x = tx; x < S; x++) region.Water[y * S + x] |= Ravensguard.Sim.WorldGen.Region.WaterFlags.Sea;
        for (int y = Math.Max(0, ty - 40); y < Math.Min(S, ty + 40); y++) { region.SlopePermille[y * S + tx - 1] = 20; region.HeightCm[y * S + tx - 1] = 100; }       // a low flat strand: sand
        int day0 = k.Day; var fishers = Enumerable.Range(0, aw.People.Count).Where(i => aw.People.IsAlive(i) && aw.People.Sex[i] == Sex.Male && aw.People.HomeSettlement[i] == v && aw.People.AgeYears(i, day0) is >= 18 and <= 40 && !aw.Book.IsAway(aw.People.IdOf(i).Value)).Take(4).ToList();
        foreach (int i in fishers) aw.People.Occupation[i] = Occupation.Fisher;                                                              // the village's fishers (nobody is taken from a field or a shop to row)
        Assert.True(fishers.Count >= 3, "the village has men to fish");
        region.RoadEdges[0].Crossings.Add(new Ravensguard.Sim.WorldGen.Region.RoadCrossing(new Ravensguard.Sim.WorldGen.Region.PointM(region.Settlements[v].CenterX + 100, region.Settlements[v].CenterY), Ravensguard.Sim.WorldGen.Region.CrossingKind.Ford, -1, 800));
        long start = (k.Now / 24 + 1) * 24; k.To(start + 59 * 24 + 1);                              // two months, a season's worth of days
        var book = k.State.Naval; Assert.NotNull(book);
        int boats = book!.Trade.Keys.Count(x => x.Kind == 0), ferries = book.Trade.Keys.Count(x => x.Kind == 1), barges = book.Trade.Keys.Count(x => x.Kind == 2);
        long caught = eco.Mass.Created(fish, Ravensguard.Sim.Goods.CreateReason.Harvest);
        int sailors = Enumerable.Range(0, aw.People.Count).Count(i => aw.People.Occupation[i] == Occupation.Sailor && aw.People.IsAlive(i));
        output.WriteLine($"{boats} fishing boats, {ferries} ferries, {barges} barges, {sailors} sailors, {caught} g of fish landed");
        Assert.True(boats > 0, "a coastal village keeps a fishing boat"); Assert.True(ferries > 0, "a wide ford keeps a ferry"); Assert.True(sailors >= boats && caught > 0);
        Assert.True(book.Trade.Where(x => x.Key.Kind == 3).Sum(x => x.Value) <= caught + 1, "the water gave what the catch says");
        Assert.True(eco.MassAuditHolds());
        var a0 = led.Audit(); var day = aw.SeaTradeDay(k.Env()); var a1 = led.Audit();                                                  // a day of the sea on its own: fares move coin purse to purse, nothing is made or sunk
        output.WriteLine($"one day: {day.Boats} boats, {day.CaughtG} g, {day.Crossings} crossings, {day.Fares} mites in fares");
        Assert.Equal(a0.InPurses.Mites, a1.InPurses.Mites); Assert.Equal(a0.Sourced, a1.Sourced); Assert.Equal(a0.Sunk, a1.Sunk); Assert.True(eco.MassAuditHolds());
        if (barges > 0) Assert.True(rides.Works.Voyages.Count > 0, "a barge is on a voyage");
        // the naval section carries the sea: saved, loaded and saved again it is the same bytes, the same sea and the same hash (an optional key of v8; the region was edited by this test, so the section is round-tripped alone)
        var s1 = SaveCodec.SerializeCompressed(NavalSave.CaptureOrNull(book)); var back = SaveCodec.DeserializeCompressed<NavalSave>(s1).Restore();
        Assert.Equal(book.Trade, back.Trade); Assert.Equal(book.Hash(), back.Hash());
        Assert.Equal(s1, SaveCodec.SerializeCompressed(NavalSave.CaptureOrNull(back)));
    }

    [Fact]
    public void Drill_Grows_StrengthThenToughness_OnePointForEach30DrillDays_ToTheCap()
    {
        using var k = new ArmyRig(); k.Raised(); var aw = k.Aw; var sw = aw.Soldiers!; var pt = aw.People;
        var man = sw.Book.Rows.Values.First(r => pt.IndexOf(new EntityId(r.Person)) >= 0).Person; int row = pt.IndexOf(new EntityId(man));
        int hours = sw.Roles.Rules.DrillHoursDay, str = pt.Attr[row * PersonTable.AttrCount], tou = pt.Attr[row * PersonTable.AttrCount + 1];
        for (int d = 0; d < 29; d++) Ravensguard.Sim.Soldiers.Growth.Drill(sw, man, Skill.Blades, hours, d);
        Assert.Equal((str, tou), (pt.Attr[row * PersonTable.AttrCount], pt.Attr[row * PersonTable.AttrCount + 1]));     // 29 days: nothing yet
        Ravensguard.Sim.Soldiers.Growth.Drill(sw, man, Skill.Blades, hours, 29); Assert.Equal(str + 1, pt.Attr[row * PersonTable.AttrCount]);
        for (int d = 30; d < 60; d++) Ravensguard.Sim.Soldiers.Growth.Drill(sw, man, Skill.Blades, hours, d); Assert.Equal(tou + 1, pt.Attr[row * PersonTable.AttrCount + 1]); Assert.Equal(str + 1, pt.Attr[row * PersonTable.AttrCount]);
        pt.Attr[row * PersonTable.AttrCount] = Ravensguard.Sim.Soldiers.Growth.AttrCap;
        for (int d = 60; d < 120; d++) Ravensguard.Sim.Soldiers.Growth.Drill(sw, man, Skill.Blades, hours, d); Assert.Equal(Ravensguard.Sim.Soldiers.Growth.AttrCap, pt.Attr[row * PersonTable.AttrCount]);   // never past the cap
    }

    [Fact]
    public void ShipParts_AnAnchorBallastABilgePumpAndAnOarPort_AreInTheContent()
    {
        var parts = Ravensguard.Sim.Tests.Economy.EconFixture.Content.All<Ravensguard.Content.VehiclePartDef>().Select(p => p.Id).ToHashSet();
        foreach (var id in new[] { "anchor", "ballast", "bilge_pump", "oar_port" }) Assert.Contains(id, parts);
    }
}
