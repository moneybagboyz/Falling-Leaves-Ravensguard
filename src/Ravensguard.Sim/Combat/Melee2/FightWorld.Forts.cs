using Ravensguard.Sim.Battle;

namespace Ravensguard.Sim.Combat.Melee2;

/// <summary>
/// [P8-LW-3] Forts and chariots in a live fight (live-war 3.3): with <see cref="Fort"/> set, a wagon fort's boards are the fight's walls (<see cref="IFightWalls"/>), count as braced polearms against a charge, and a cheval de frise slows the foot
/// and turns horses; with <see cref="Chariots"/> set, chariots drive, carry their crews and shoot from the car. A fight with neither runs exactly as before and its hash is unchanged.
/// </summary>
public sealed partial class FightWorld
{
    /// <summary>The wagon fort and the chevaux de frise laid over this fight; null: none.</summary>
    public FortLayer? Fort { get; set; }

    /// <summary>The chariots of this fight; null: none.</summary>
    public ChariotLayer? Chariots { get; set; }

    /// <summary>A footman's share of his walk at a point (permille): a third across a cheval de frise.</summary>
    private int FortFootPm(Fighter f) => Fort!.FootPm(f.X, f.Y);
}
