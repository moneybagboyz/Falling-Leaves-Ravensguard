using Ravensguard.Content;
using Ravensguard.Sim.Building;

namespace Ravensguard.Sim.Burning;

/// <summary>A roof or wall due for re-thatching or re-daubing (fire 4.7): the structure, the fuel class that rots ("thatch" or "daub") and the lowest condition among its pieces.</summary>
public readonly record struct RepairDue(int Structure, string Kind, int ConditionPm);

public sealed partial class FireWorld
{
    /// <summary>Marks the piece at a cell as made or mended at <paramref name="sec"/> (the construction hook writes it when a piece is finished; a re-thatch resets it).</summary>
    public void MarkMade(int x, int y, int z, long sec) => Book.Made[Key(x, y, z)] = sec;

    /// <summary>The condition of the structure piece at a cell, permille (1000 when it has no mark or does not rot).</summary>
    public int ConditionAt(int x, int y, int z, long sec)
    {
        var p = Rooms.PieceAt(x, y, z);
        return p == null ? 1000 : ConditionPm(Key(x, y, z), Fuels.ClassOf(p.Id), sec);
    }

    /// <summary>A roof of thatch below the leak line leaks rain into the room under it (wet exposure, Phase 3).</summary>
    public bool Leaking(int x, int y, int z, long sec)
    {
        var p = Rooms.PieceAt(x, y, z);
        return p is { Kind: PieceKind.Roof } && Fuels.ClassOf(p.Id).Burns && ConditionPm(Key(x, y, z), Fuels.ClassOf(p.Id), sec) < Fuels.Upkeep.LeakBelowPm;
    }

    /// <summary>
    /// The structures whose roof (thatch) or daub walls have fallen below the repair line: the owner's household or steward posts a re-thatch or re-daub job for each (a construction job; the
    /// host decides who pays). In structure id order.
    /// </summary>
    public List<RepairDue> RepairsDue(long sec)
    {
        var due = new List<RepairDue>();
        foreach (var st in Rooms.StructureIds)
        {
            int thatch = 1000, daub = 1000;
            foreach (var k in Rooms.StructureCells(st))
            {
                if (!Book.Made.ContainsKey(k)) continue;
                var (x, y, z) = Unpack(k);
                var p = Rooms.PieceAt(x, y, z);
                if (p == null) continue;
                var c = Fuels.ClassOf(p.Id);
                int cond = ConditionPm(k, c, sec);
                if (c.Id == "thatch" && p.Kind == PieceKind.Roof) thatch = Math.Min(thatch, cond);
                else if (c.Id == "daub") daub = Math.Min(daub, cond);
            }
            if (thatch < Fuels.Upkeep.RepairBelowPm) due.Add(new RepairDue(st, "thatch", thatch));
            if (daub < Fuels.Upkeep.RepairBelowPm) due.Add(new RepairDue(st, "daub", daub));
        }
        return due;
    }

    /// <summary>A re-thatch or re-daub is done: every piece of that class in the structure is as new at <paramref name="sec"/>.</summary>
    public int Mend(int structure, string kind, long sec)
    {
        int n = 0;
        foreach (var k in Rooms.StructureCells(structure).ToList())
        {
            var (x, y, z) = Unpack(k);
            var p = Rooms.PieceAt(x, y, z);
            if (p == null || Fuels.ClassOf(p.Id).Id != kind) continue;
            Book.Made[k] = sec; n++;
        }
        return n;
    }
}
