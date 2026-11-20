using System.Diagnostics;
using Ravensguard.Sim.Animals;
using Ravensguard.Sim.Carts;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Goods;
using Ravensguard.Sim.Tests.Economy;
using Xunit.Abstractions;

namespace Ravensguard.Sim.Tests.VehicleDesign;

/// <summary>P5-G budgets (vehicle-designer 9, overview 9): stats recomputed on a part change, the designer's update, 30 vehicles and 10 boats a tick, and no allocation in wear or a boat's step.</summary>
[Collection(TimingCollection.Name)]
public class VehicleBudgetTests(ITestOutputHelper output)
{
    [Fact]
    public void Budgets_StatsOnAPartChange_TheDesignerEdit_FortyVehiclesATick_AndNoAllocationInWearOrABoatStep()
    {
        var goods = EconFixture.Catalog(); var b = new RideBook(EconFixture.Content, goods, new MassLedger(goods.Count), 7);
        var carts = new List<VehicleRow>(); var boats = new List<VehicleRow>();
        for (int i = 0; i < 30; i++) { var v = b.Vehicles.Create(b.Templates.Get(i % 2 == 0 ? "ox_wagon" : "cart"), EntityId.None); v.Cargo.Create(goods.IdOf("wheat"), 300_000, CreateReason.Other); carts.Add(v); }
        for (int i = 0; i < 10; i++) boats.Add(b.Vehicles.Create(b.Templates.Get(i % 2 == 0 ? "barge" : "rowing_boat"), EntityId.None));
        b.WaterNow = () => new BoatWater(1800, 800, 0, 0, 0, false, 0);
        // a part change: the stats recomputed (the cache dropped)
        var ox = carts[0];
        for (int k = 0; k < 50; k++) { ox.Touch(); _ = b.Vehicles.StatsOf(ox); }
        double recompute = TestBudget.BestPass(0.05, 5, () => { var sw = Stopwatch.StartNew(); for (int k = 0; k < 200; k++) { ox.Touch(); _ = b.Vehicles.StatsOf(ox); } return sw.Elapsed.TotalMilliseconds / 200; });
        // the designer: one edit and its stats pane
        var des = new VehicleDesigner(b.Templates, b.Breeds); des.Load(b.Templates.Get("ox_wagon").Def);
        for (int k = 0; k < 5; k++) { des.Mirror(); _ = des.Stats(); }
        double edit = TestBudget.BestPass(2.0, 5, () => { var sw = Stopwatch.StartNew(); for (int k = 0; k < 20; k++) { des.Mirror(); _ = des.Stats(); } return sw.Elapsed.TotalMilliseconds / 20; });
        // a tick of 30 carts and 10 boats: each a speed, a step's wear and its accounting
        void Tick(long sec)
        {
            foreach (var v in carts) { _ = b.Vehicles.Speed(v, Surface.DryRoad, 0, 85, sec); b.Vehicles.Wear(v, 1250); }
            foreach (var v in boats) { b.Driving = v.Id; var st = b.Plan(false, false, sec); b.Account(st, st.SpeedMmPerGs, sec, false); }
            b.Driving = EntityId.None;
        }
        for (int k = 0; k < 200; k++) Tick(k);
        double tick = TestBudget.BestPass(0.5, 5, () => { var sw = Stopwatch.StartNew(); for (int k = 0; k < 200; k++) Tick(1000 + k); return sw.Elapsed.TotalMilliseconds / 200; });
        // allocations: wear and a boat's step allocate nothing
        long a0 = GC.GetAllocatedBytesForCurrentThread();
        for (int k = 0; k < 100; k++) { b.Vehicles.Wear(carts[1], 1250); b.Driving = boats[0].Id; var st = b.Plan(false, false, 5000 + k); b.Account(st, st.SpeedMmPerGs, 5000 + k, false); }
        long alloc = GC.GetAllocatedBytesForCurrentThread() - a0;
        b.Driving = EntityId.None;
        output.WriteLine($"stats recompute {recompute * 1000:0.0} us, designer edit {edit:0.000} ms, a tick of 30 carts and 10 boats {tick:0.000} ms, {alloc} bytes allocated in 100 wear and boat steps");
        Assert.True(recompute < TestBudget.Ms(0.05), $"stats {recompute} ms");
        Assert.True(edit < TestBudget.Ms(2.0), $"designer {edit} ms");
        Assert.True(tick < TestBudget.Ms(0.5), $"tick {tick} ms");
        Assert.Equal(0, alloc);
    }
}
