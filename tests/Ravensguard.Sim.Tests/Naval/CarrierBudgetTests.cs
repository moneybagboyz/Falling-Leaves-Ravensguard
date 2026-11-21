using System.Diagnostics;
using Ravensguard.Sim.Carts;
using Ravensguard.Sim.Combat.Melee2;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Naval;
using Ravensguard.Sim.Tests.Combat.Melee2;
using Ravensguard.Sim.Tests.Economy;
using Xunit.Abstractions;

namespace Ravensguard.Sim.Tests.Naval;

/// <summary>P7-SH-A budgets (ships-and-naval-war 11): carriers cost at most 0.2 ms a tick for two ships and 100 people; deriving a ship's decks and the NavalHash are cheap.</summary>
[Collection(TimingCollection.Name)]
public class CarrierBudgetTests(ITestOutputHelper output)
{
    [Fact]
    public void TwoShipsAndAHundredPeople_CostUnderTwoTenthsOfAMillisecondATick_TheDecksAreDerivedFast_TheNavalHashIsTheFramesAndTheWater()
    {
        var cat = VehicleCatalog.From(EconFixture.Content);
        var fx = new FightFixture();
        var w = fx.World(2);
        var layer = new CarrierLayer(w, new Swimmers(2));
        var a = ShipFrame.Of(new EntityId(1), cat.Get("cog"), cat, 0, 0); var b = ShipFrame.Of(new EntityId(2), cat.Get("cog"), cat, 40_000, 0, FrameMath.FromDegrees(90));
        a.SpeedMmps = 3000; a.TurnPerTick = ShipFrame.TurnOf(3); b.SpeedMmps = 2500; b.TurnPerTick = ShipFrame.TurnOf(-2);
        int n = 0;
        foreach (var ship in new[] { a, b })
            for (int y = 1; y < 21 && n < (ship == a ? 50 : 100); y++)
                for (int x = 1; x < 6 && n < (ship == a ? 50 : 100); x++)
                {
                    if (!ship.Deck.Walkable(x, y, 0) || ship.Deck.Kind(x, y, 0) != DeckKind.Floor) continue;
                    var f = w.Add(fx.Kit("M" + n, n % 2, 0, 0) with { Person = (ulong)(100 + n) }, player: true); layer.BoardAt(f, ship, x, y, 0); n++;
                }
        Assert.Equal(100, n);
        for (int k = 0; k < 20; k++) { layer.Before(); layer.After(); }
        double tick = TestBudget.BestPass(0.2, 5, () => { var sw = Stopwatch.StartNew(); for (int k = 0; k < 200; k++) { layer.Before(); layer.After(); } return sw.Elapsed.TotalMilliseconds / 200; });
        double decks = TestBudget.BestPass(5.0, 5, () => { var sw = Stopwatch.StartNew(); for (int k = 0; k < 20; k++) _ = DeckMap.Build(cat.Get("war_cog").Parts, cat, 7, 22); return sw.Elapsed.TotalMilliseconds / 20; });
        output.WriteLine($"carriers {tick * 1000:0.0} us a tick for 2 ships and 100 riders; decks of a war cog {decks:0.00} ms");
        Assert.True(tick < TestBudget.Ms(0.2), $"carriers {tick} ms");
        Assert.True(decks < TestBudget.Ms(5.0), $"decks {decks} ms");
        Assert.Equal(NavalHash.Of([a, b], layer.Water), NavalHash.Of([b, a], layer.Water));                               // id order, not list order
        a.XMm += 1; Assert.NotEqual(NavalHash.Of([a, b]), NavalHash.Of([b, a, ShipFrame.Of(new EntityId(3), cat.Get("longship"), cat, 0, 0)]));
    }
}
