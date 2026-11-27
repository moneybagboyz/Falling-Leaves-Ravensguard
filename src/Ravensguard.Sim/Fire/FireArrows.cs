using Ravensguard.Sim.Core;

namespace Ravensguard.Sim.Burning;

/// <summary>
/// P8-LW-4 (live-war 3.4): a fire arrow's hit on thatch, a sail or dry timber lights it at 200 permille (wet: 20), through the Phase 6 fire world (<see cref="FireWorld.Ignite"/>). Stone and what does not burn never light. The draw is
/// stream 0x851 keyed on the shot's serial and the cell, so a replay lights the same cells. A ship's deck has its own rule (<c>ShipCombat.Ignite</c>, canvas 600, planking 200); this is the land's.
/// </summary>
public static class FireArrows
{
    public const int DryPm = 200, WetPm = 20;

    /// <summary>The chance a hit on this fuel lights it: 200 pm dry, 20 pm wet; 0 for a class that does not burn.</summary>
    public static int ChancePm(FuelClass fuel, bool wet) => fuel.Burns ? (wet ? WetPm : DryPm) : 0;

    /// <summary>The cause of a fire an arrow lit in a battle: a war's, a root of the chronicle, naming the shooter.</summary>
    public static FireCause Cause(EntityId shooter) => new("fire_arrow", "ch_note_fire_arrow", [new Cause(CauseKind.War, null, "", "ch_note_fire_arrow")], shooter.Value == 0 ? [] : [shooter]);

    /// <summary>A fire arrow has hit the cell (x, y, z): lights it with its chance. Returns whether it took light.</summary>
    public static bool Strike(FireWorld fire, int x, int y, int z, long gameSec, ulong seed, ulong serial, bool wet, EntityId shooter)
    {
        int pm = ChancePm(fire.FuelAt(x, y, z), wet); if (pm <= 0) return false;
        ulong cell = ((ulong)(uint)x << 32) | (uint)y;
        if (DetRng.Range(seed, (long)serial, cell, RngStreams.FireArrowIgnite, (uint)z, 0, 1000) >= pm) return false;
        return fire.Ignite(x, y, z, gameSec, Cause(shooter));
    }
}
