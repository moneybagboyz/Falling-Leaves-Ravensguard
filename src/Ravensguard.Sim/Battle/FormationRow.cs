using Ravensguard.Sim.Combat.Watch;

namespace Ravensguard.Sim.Battle;

/// <summary>
/// One formation (battle-bubble-and-formations 3.2): its members in slot order, captain and banner, shape, anchor and facing, the order in force and the queue behind it, the orders on their way,
/// and what the formation layer keeps between ticks (who holds which slot, the vacancies closing, cohesion, the braking pace). Integers only; <see cref="FormationBook"/> is what steps and saves it.
/// </summary>
public sealed class FormationRow
{
    /// <summary>Its place in the book (global, 0-based) and its number within its side (1..32; 1 is the commander's own).</summary>
    public int Index;
    public byte Id;
    public int Side;
    public FormationKind Kind;
    /// <summary>Fighter ids in the slot order they were given; a member's index here is stable for the formation's life.</summary>
    public int[] Members = [];
    public int Captain = -1, Banner = -1, Leader = -1;
    public Formation Shape = Formation.Line;
    public int Ranks = 4;
    /// <summary>How many slots the layout has now (the members still slotted when the shape last changed).</summary>
    public int LayoutN;
    public int AnchorX, AnchorY, FaceX = 1024, FaceY;
    public FormationOrder Order = FormationOrder.Of(OrderKind.None);
    public FormationOrder[] Queue = new FormationOrder[4];
    public int QueueCount;
    public FormationOrder[] Pending = new FormationOrder[8];
    public int PendingCount;
    public FireMode Fire;
    /// <summary>The enemy row (global index) a shoot-at-target order names, -1 none.</summary>
    public int FireTargetRow = -1;
    public int Drill, Micro = -100_000;
    public bool CaptainAlways;
    public int StartCount;
    public int DeployStart = -1, DeployTicks;
    public int TurnAcc, OrderGo, SlotChanges, NextDecision, Cohesion = 1000, BrakeMmps = int.MaxValue, PaceNow;
    public bool Cascaded, InContact, WallByCaptain;
    public int TgtRow = -1, TgtX, TgtY;
    /// <summary>A facing wanted (a Q10 vector; 0, 0 none) and the flank a wheel pivots on (+1 right, -1 left, 0 turn about the centre).</summary>
    public int WantFx, WantFy, WheelSign;

    // per member (by member index)
    public int[] Slot = [], Leadership = [], Pace = [], FlankBlow = [], Officer = [];
    // per slot
    public int[] Occ = [], VacSince = [];
    public int VacCount;
    // derived layout, never saved
    public int[] Lat = [], Back = [], ColLat = [], ColBack = [];
    public int Files = 1;
    public int ActiveNow, RoutingNow, TotalNow;

    public int Active => ActiveNow;

    /// <summary>The slot a member index holds now (-1: none).</summary>
    public int SlotOfMember(int memberIndex) => Slot[memberIndex];

    /// <summary>The member index at a slot (-1: empty).</summary>
    public int MemberAt(int slot) => (uint)slot < (uint)Occ.Length ? Occ[slot] : -1;

    /// <summary>The fighter at a slot (-1: empty).</summary>
    public int FighterAt(int slot) { int m = MemberAt(slot); return m < 0 ? -1 : Members[m]; }

    public int IndexOfFighter(int fighter) { for (int i = 0; i < Members.Length; i++) if (Members[i] == fighter) return i; return -1; }

    public int Rank(int slot) => slot / Math.Max(1, Files);
}
