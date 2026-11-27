using Ravensguard.Sim.Combat.Melee2;
using Ravensguard.Sim.Core;

namespace Ravensguard.Sim.Naval;

public enum JumpResult : byte { Cannot, Boarded, Overboard }

/// <summary>
/// P8-LW-2: boarding a ship that is pushing off (live-war 3.2): a gap of 1.5 m or less may be jumped, on an Athletics check (stream 0x859 keyed on the person and the tick, <c>500 + 8 x Athletics</c> permille, 100 to 950); a miss is a man
/// overboard (<see cref="CarrierLayer.Fall"/>, the Phase 7 breath rule from there: mail drowns a man who cannot reach a hull or the shore). A wider gap cannot be jumped, and nothing is drawn.
/// </summary>
public static class PushOff
{
    public const int JumpMaxMm = 1500, BasePm = 500, PerSkillPm = 8;

    public static int JumpPm(int athletics) => Math.Clamp(BasePm + PerSkillPm * athletics, 100, 950);

    public static bool Roll(ulong seed, ulong person, long tick, int athletics) => DetRng.Range(seed, tick, person, RngStreams.BoardingJumps, 0, 0, 1000) < JumpPm(athletics);

    /// <summary>The man on the quay jumps the gap onto cell (cx, cy) of the ship's deck 0.</summary>
    public static JumpResult Jump(CarrierLayer layer, ulong seed, Fighter f, ShipFrame ship, int cx, int cy, int gapMm)
    {
        if (gapMm > JumpMaxMm || gapMm < 0 || !ship.Deck.Walkable(cx, cy, 0)) return JumpResult.Cannot;
        if (Roll(seed, f.Person != 0 ? f.Person : (ulong)f.Id, layer.World.Tick, layer.AthleticsOf(f)))
        {
            layer.BoardAt(f, ship, cx, cy, 0);
            return JumpResult.Boarded;
        }
        layer.Fall(f, "jump");
        return JumpResult.Overboard;
    }
}
