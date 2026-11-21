using Ravensguard.Sim.Naval;

namespace Ravensguard.Sim.Combat.Melee2;

/// <summary>
/// [P7-SH-A] Carriers (ships-and-naval-war 4.6): with <see cref="Carriers"/> set, the ships' frames move at the start of each tick and carry their riders, the fight walks them against the ship's decks, and a rider's place in his frame follows him after the
/// step (Naval/CarrierLayer.cs). A fight with no ships has none, runs exactly as before and its hash is unchanged.
/// </summary>
public sealed partial class FightWorld
{
    /// <summary>The ships of this fight and the water around them; null: no carriers.</summary>
    public CarrierLayer? Carriers { get; set; }

    private void CarriersBefore() => Carriers!.Before();
    private void CarriersAfter() => Carriers!.After();
}
