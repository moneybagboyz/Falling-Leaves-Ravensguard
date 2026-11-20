using Ravensguard.Sim.Building;
using Ravensguard.Sim.Burning;
using Ravensguard.Sim.Construction;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Goods;
using Ravensguard.Sim.Tests.Construction;
using Xunit.Abstractions;

namespace Ravensguard.Sim.Tests.Fire;

/// <summary>FB-7 collapse and FB-8 goods burn, through Phase 5 construction (the built pieces, the support rules, the stores, the mass audit).</summary>
public class FireConstructionTests(ITestOutputHelper output)
{
    private const long T0 = ConstructionRig.Day10;

    private static FireRig OverConstruction(ConstructionRig r, ulong seed = 1)
    {
        var fuels = new FuelTable(r.Content);
        var fw = new FireWorld(new FireBook(), r.W.Rooms, fuels, seed);
        var rig = new FireRig { Fw = fw, Rooms = r.W.Rooms, Sec = T0 };
        fw.Chronicle = (when, type, key, ps, subjects, causes, data) =>
        {
            var id = rig.Ids.Allocate(IdKind.Event);
            rig.Chron.Add(new ChronicleEntry(id, when, type, subjects, causes, data.ToDictionary(d => d.Key, d => d.Value), key, ps));
            return id;
        };
        fw.Destroy = (x, y, z, fixture, burnt, sec, cause) => r.W.FireDestroy(x, y, z, fixture, burnt, fuels.BurnableGoodClasses, sec, cause);
        fw.StoresAt = (x, y, z) => r.W.Book.Placed.Values.Where(p => p.X == x && p.Y == y && p.Z == z && r.Econ.OtherStores.ContainsKey(p.Owner)).Select(p => r.Econ.OtherStores[p.Owner]);
        fw.PeopleNear = l => l.AddRange(r.Standing.Select(s => new FirePerson(s.P.Value, s.X, s.Y, s.Z)));
        return rig;
    }

    private static void BuildNow(ConstructionRig r, IReadOnlyList<PlaceCell> cells)
    {
        var placed = r.W.Place(r.Hero, PurseKind.Person, -1, cells, T0);
        Assert.True(placed.Ok, placed.Key);
        r.W.DevStock(placed.Site, T0);
        r.W.DevFinish(placed.Site, 99, 0, 0, T0);
    }

    private static ConstructionRig Platform()
    {
        var r = ConstructionRig.Make();
        BuildNow(r, [new PlaceCell(705, 705, 0, "post_timber")]);
        var floor = new List<PlaceCell>();
        for (int y = 704; y <= 706; y++) for (int x = 704; x <= 706; x++) floor.Add(new PlaceCell(x, y, 1, "floor_plank"));
        BuildNow(r, floor);
        Assert.Equal(10, r.W.Book.Built.Count);
        return r;
    }

    [Fact]
    public void FB7_ABurntPost_BringsTheFloorAboveDown_TheGroanThenTheFall_ByTheSupportRules()
    {
        var r = Platform();
        var man = EntityId.Make(IdKind.Person, 7_000_001);
        var rig = OverConstruction(r);
        // a person who stands on the floor when it falls (he is not in a burning cell, so the fire's flame does not touch him)
        r.Standing.Add((man, 704, 704, 1));
        long massBefore = r.Econ.OtherStores.Values.Sum(s => s.TotalMassG()) + r.W.Book.Built.Values.Sum(b => r.W.Costs[b.Piece].TotalGrams);
        Assert.True(rig.Fw.Ignite(705, 705, 0, rig.Sec, FireCause.Test));                  // the post catches
        long groanAt = -1, fellAt = -1;
        for (int s = 1; s <= 900 && fellAt < 0; s++)
        {
            rig.Run(1);
            r.W.AdvanceTo(rig.Sec);
            if (groanAt < 0 && r.W.Book.Groans.Count > 0) groanAt = r.W.Book.Groans[0].StartSec;
            if (r.Falls.Count > 0) fellAt = r.Falls[0].Sec;
        }
        output.WriteLine($"the post burnt out {(groanAt - T0) / 20} sim-s after the start; the floor fell at {(fellAt - T0) / 20}");
        Assert.True(groanAt > 0 && fellAt > 0);
        Assert.Equal(groanAt + 10, fellAt);                                                     // the groan, 10 game seconds, then the fall
        Assert.InRange((groanAt - T0) / 20, 290, 340);                                          // a post burns 300 sim-s
        Assert.Equal(3000, r.Falls.Single().Mm);
        Assert.Contains(r.Chron, c => c.Type == "collapse");
        Assert.Contains(rig.Chron.All, e => e.TypeId == "fire_started");
        // the burnt pieces went to the Burn sink and the audits are exact
        long burned = 0;
        for (ushort g = 0; g < r.Econ.Goods.Count; g++) burned += r.Econ.Mass.Destroyed(g, DestroyReason.Burned);
        Assert.True(burned > 0, "wood burnt");
        r.AssertMass();
        r.AssertCoin();
        long after = r.Econ.OtherStores.Values.Sum(s => s.TotalMassG()) + r.W.Book.Built.Values.Sum(b => r.W.Costs[b.Piece].TotalGrams);
        Assert.True(massBefore > after);                                                        // the burnt mass left the world (a documented sink)
    }

    [Fact]
    public void FB8_ABarnOfGrain_Burns_TwoTonnesToTheBurnSink_TheMassAuditExact()
    {
        var r = ConstructionRig.Make();
        BuildNow(r, [new PlaceCell(800, 800, 0, "floor_plank"), new PlaceCell(801, 800, 0, "floor_plank")]);
        var chest = ConstructionIds.Make(0, ConstructionIds.ChestSlot, 77);
        var store = r.Econ.OpenStore(StoreKind.Chest, chest);
        ushort wheat = r.G("wheat");
        store.Create(wheat, 2_000_000, CreateReason.Import);                                      // 2 t
        r.W.Book.Placed[chest] = new PlacedStore(chest, 800, 800, 0);
        var rig = OverConstruction(r);
        Assert.True(rig.Fw.Ignite(800, 800, 0, rig.Sec, FireCause.Hearth));
        rig.RunOut(1200);
        Assert.Equal(0, store.Qty(wheat));
        Assert.Equal(2_000_000, r.Econ.Mass.Destroyed(wheat, DestroyReason.Burned));
        r.AssertMass(); r.AssertCoin();
        var ended = Assert.Single(rig.Chron.ByType("fire_ended"));
        long mites = ended.Params!.Single(p => p.Name == "goods").Value;
        Assert.True(mites >= (long)Math.Round(r.Econ.Goods.Def(wheat).BasePriceMitesPerKg * 2000), $"{mites} mites");        // 2 t at its reference price (and whatever else lay in the site's pile there)
    }
}
