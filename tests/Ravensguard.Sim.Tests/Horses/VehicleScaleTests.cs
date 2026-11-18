using Ravensguard.Sim.Animals;
using Ravensguard.Sim.Carts;
using Ravensguard.Sim.Goods;
using Ravensguard.Sim.Tests.Economy;
using Xunit.Abstractions;

namespace Ravensguard.Sim.Tests.Horses;

/// <summary>The world is one cell to one metre (REDESIGN_PLAN 1, 3h), so a vehicle template's grid is its real size: the tests hold every template to the sizes and loads of medieval vehicles.</summary>
public class VehicleScaleTests(ITestOutputHelper output)
{
    // width m, body m (the part with the bed, wheels and seat), hitch m (pole, shafts or traces; 0 when pushed), cargo kg, and the mass a whole vehicle may have
    private sealed record Real(string Id, (int Min, int Max) Width, (int Min, int Max) Body, (int Min, int Max) Hitch, (int Min, int Max) CargoKg, (int Min, int Max) MassKg);

    private static RideBook Book() { var goods = EconFixture.Catalog(); return new RideBook(EconFixture.Content, goods, new MassLedger(goods.Count), 7); }

    private static readonly Real[] Table =
    [
        new("wheelbarrow",   (1, 1), (1, 2), (0, 0), (50, 120),   (15, 60)),                // 0.6-1 m wide, 1.2-1.8 m long
        new("handcart",      (1, 1), (1, 2), (0, 0), (100, 200),  (25, 80)),
        new("cart",          (2, 2), (3, 3), (2, 2), (400, 600),  (120, 300)),               // two-wheeled: 1.5-2 m wide, 2.5-3 m long plus shafts
        new("hay_wain",      (2, 2), (4, 5), (2, 3), (500, 1000), (150, 400)),               // four-wheeled: about 2 m wide, 4-5 m long plus a pole of 2-3 m
        new("ox_wagon",      (2, 2), (4, 5), (2, 3), (800, 1200), (200, 450)),
        new("covered_wagon", (2, 2), (4, 5), (2, 3), (600, 1000), (250, 500)),
        new("sledge",        (1, 2), (2, 3), (1, 1), (300, 600),  (40, 150)),
    ];

    [Fact]
    public void EveryTemplate_IsTheSizeAndLoadOfARealVehicle_AtOneMetrePerCell()
    {
        var cat = Book().Templates;
        Assert.Empty(cat.Errors); Assert.Equal(Table.Length, cat.All.Count);
        foreach (var t in Table)
        {
            var b = cat.Get(t.Id); var st = b.Stats;
            int hitchRows = HitchRows(cat, b), body = st.LengthCells - hitchRows;
            output.WriteLine($"{t.Id}: {st.WidthCells} x {st.LengthCells} m (body {body}, hitch {hitchRows}), {st.MassKg} kg, cargo {st.CargoKg} kg / {st.CargoL} L, {st.Wheels} wheels, up to {st.MaxAnimals} animals");
            Assert.InRange(st.WidthCells, t.Width.Min, t.Width.Max);
            Assert.InRange(body, t.Body.Min, t.Body.Max);
            Assert.InRange(hitchRows, t.Hitch.Min, t.Hitch.Max);
            Assert.InRange(st.CargoKg, t.CargoKg.Min, t.CargoKg.Max);
            Assert.InRange(st.MassKg, t.MassKg.Min, t.MassKg.Max);
            Assert.True(st.CargoL >= st.CargoKg, $"{t.Id}: a litre of load weighs at most a kilogram");
            Assert.True(st.WidthCells <= 2, $"{t.Id}: no vehicle is wider than a 2 m track");
            if (st.Wheels > 0 && st.AxleLimitKg > 0) Assert.True(st.AxleLimitKg >= st.MassKg + st.CargoKg * 8 / 10, $"{t.Id}: the axles carry at least 80 percent of a full load");
        }
    }

    [Fact]
    public void TheCapacitiesAreTheHistoricalOnes_WhateverTheSizeOfTheGrid()
    {
        var cat = Book().Templates;
        Assert.Equal((500, 1000), (cat.Get("cart").Stats.CargoKg, cat.Get("cart").Stats.CargoL));
        Assert.Equal((1000, 2000), (cat.Get("ox_wagon").Stats.CargoKg, cat.Get("ox_wagon").Stats.CargoL));
        Assert.Equal((150, 300), (cat.Get("handcart").Stats.CargoKg, cat.Get("handcart").Stats.CargoL));
        Assert.Equal(100, Book().Breeds.Get("horse_packhorse").CarryKg);                                                  // a pack horse carries about 100 kg (plan 4A.1)
    }

    [Fact]
    public void HitchLimits_AreOneTeamAtLeast_AndRealisticAtMost()
    {
        var cat = Book().Templates;
        Assert.Equal((2, 8), (cat.Get("ox_wagon").Stats.HitchSlots, cat.Get("ox_wagon").Stats.MaxAnimals));          // up to four yoke pairs of oxen
        Assert.Equal((2, 6), (cat.Get("hay_wain").Stats.HitchSlots, cat.Get("hay_wain").Stats.MaxAnimals));          // three pairs
        Assert.Equal((1, 2), (cat.Get("cart").Stats.HitchSlots, cat.Get("cart").Stats.MaxAnimals));                  // one horse, or two in tandem
        Assert.Equal((0, 0), (cat.Get("handcart").Stats.HitchSlots, cat.Get("handcart").Stats.MaxAnimals));          // pushed by people
        foreach (var b in cat.All) Assert.InRange(b.Stats.MaxAnimals, b.Stats.HitchSlots, 8);
    }

    /// <summary>Rows at the front that hold nothing but the hitch (pole, yoke, shafts, traces): the metres in front of the body.</summary>
    private static int HitchRows(VehicleCatalog cat, VehicleBlueprint b)
    {
        int n = 0;
        for (int r = b.Def.Rows.Count - 1; r >= 0; r--)
        {
            bool only = true;
            foreach (char c in b.Def.Rows[r])
            {
                if (c == '_') continue;
                foreach (var id in b.Def.Legend[c.ToString()]) if (!cat.Parts[id].Tags.Contains("hitch") && !cat.Parts[id].Tags.Contains("pole") && id != "iron_fitting") only = false;
            }
            if (!only) break; n++;
        }
        return n;
    }
}
