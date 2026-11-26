using Ravensguard.Sim.Combat.Abstract;
using Ravensguard.Sim.WarSupply;

namespace Ravensguard.Sim.Armies;

/// <summary>
/// The gate between the continent's count hosts and a detailed region (abstract-war 4.4, armies spec 4.4): a count host that enters becomes people at the gate, exactly its counts (the
/// persons are made by 0x7A3 through <see cref="ArmyWorld.EnterRegion"/>), and when it leaves the living become counts again, the dead staying dead here with their causes. The count
/// host is the supply book's host while it is in the region (<see cref="CountHost.Supply"/>); a battle there is v2 or live, never a count battle.
/// </summary>
public static class CountHostGate
{
    /// <summary>The supply roster of a count host's men: levy to levy, foot to spearmen, missile to archers, horse to knights, elite to men-at-arms.</summary>
    public static Roster RosterOf(CountHost h) => new([(TroopRole.Levy, h.Men[(int)CountClass.Levy]), (TroopRole.Spearman, h.Men[(int)CountClass.Foot]), (TroopRole.Archer, h.Men[(int)CountClass.Missile]),
        (TroopRole.Knight, h.Men[(int)CountClass.Horse]), (TroopRole.ManAtArms, h.Men[(int)CountClass.Elite])]);

    public static CountClass ClassOf(TroopRole r) => r switch
    {
        TroopRole.Levy => CountClass.Levy,
        TroopRole.Archer or TroopRole.Crossbowman => CountClass.Missile,
        TroopRole.Knight => CountClass.Horse,
        TroopRole.ManAtArms or TroopRole.Captain => CountClass.Elite,
        _ => CountClass.Foot,
    };

    /// <summary>The host enters the region at a gate: its men are made persons (none that were persons before are made again). Returns the persons in the region.</summary>
    public static int Enter(ArmyWorld aw, ArmyEnv e, CountHost h, HostId host, int gate)
    {
        int n = aw.EnterRegion(e, host, gate);
        h.State = CountState.InDetail; h.Supply = host.Value; h.Region = -1;
        aw.Book.Touch();
        return n;
    }

    /// <summary>
    /// The host leaves the region at a gate into the summary region <paramref name="into"/>: the living persons become counts, the roster as it stands after the dead were taken off, and the
    /// count host's men are read back from it by class. The dead of the bubble are entered in the ledger. Returns how many men left.
    /// </summary>
    public static int Leave(ArmyWorld aw, ArmyEnv e, CountHost h, HostId host, int into)
    {
        int before = h.Total;
        int left = aw.LeaveRegion(e, host);
        var a = aw.Supply!.Account(host);
        Array.Clear(h.Men);
        for (int r = 0; r < Roster.Roles; r++) h.Men[(int)ClassOf((TroopRole)r)] += a.Men[r];
        int dead = before - h.Total;
        aw.Book.Count.Ledger[CountLedger.MenDead] += dead;
        h.State = CountState.Standing; h.Supply = 0; h.Region = into;
        aw.Book.Touch();
        return left;
    }
}
