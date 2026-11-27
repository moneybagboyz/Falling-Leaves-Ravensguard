using Ravensguard.Sim.Animals;
using Ravensguard.Sim.Battle;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Tests.Combat.Battle;
using Ravensguard.Sim.Tests.Combat.Melee2;
using Ravensguard.Sim.Tests.Economy;

namespace Ravensguard.Sim.Tests.Siege;

/// <summary>P8-LW-3 on a battle opening: a side whose lord owns war wagons or chariots opens the battle with the fort and the cars; a side with none, or an opening with no ride book, is untouched.</summary>
[Collection("fight")]
public class LiveWarVehiclesOnOpeningTests(FightFixture fx)
{
    private static readonly EntityId Lord = EntityId.Make(IdKind.Person, 77);

    private BattleOpening Open(RideBook? rides, bool noFort = false)
    {
        var op = new BubbleScenario(fx) { Persons = true }.Opening(60, 60, 200, 30, 60_000);
        op.Sides[1].VehicleOwners.Add(Lord); op.Rides = rides; op.NoFort = noFort;
        return op;
    }

    private static RideBook Rides(int wagons, int chariots)
    {
        var b = new RideBook(EconFixture.Content, EconFixture.NewEconomy(false), 7);
        for (int i = 0; i < wagons; i++) b.Vehicles.Create(b.Templates.Get("war_wagon"), Lord, 0, 0);
        for (int i = 0; i < chariots; i++) b.Vehicles.Create(b.Templates.Get("war_chariot"), Lord, 0, 0);
        return b;
    }

    [Fact]
    public void AnArmyThatOwnsWagonsAndChariots_BringsTheFortAndTheCarsToTheBattle()
    {
        var bubble = BattleBubble.Open(fx.C, Open(Rides(6, 2)));
        Assert.NotNull(bubble.W.Fort); Assert.Equal(6, ((FortLayer)bubble.W.Fort!).Wagons.Count); Assert.Equal(1, ((FortLayer)bubble.W.Fort).Team);
        Assert.NotNull(bubble.W.Chariots); Assert.Equal(2, ((ChariotLayer)bubble.W.Chariots!).Cars.Count);
        Assert.All(((ChariotLayer)bubble.W.Chariots).Cars, c => Assert.Equal(1, c.Team));
        for (int i = 0; i < 100; i++) bubble.W.Step();                                       // the layers run in the fight's tick
    }

    [Fact]
    public void NoRideBook_NoOwnedVehicles_OrAnAssault_BringsNoFort()
    {
        var plain = BattleBubble.Open(fx.C, Open(null));
        Assert.Null(plain.W.Fort); Assert.Null(plain.W.Chariots);
        var none = BattleBubble.Open(fx.C, Open(Rides(0, 0)));
        Assert.Null(none.W.Fort); Assert.Null(none.W.Chariots);
        var assault = BattleBubble.Open(fx.C, Open(Rides(6, 1), noFort: true));
        Assert.Null(assault.W.Fort); Assert.NotNull(assault.W.Chariots);
    }
}
