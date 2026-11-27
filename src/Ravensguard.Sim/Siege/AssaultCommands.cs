using Ravensguard.Sim.Battle;
using Ravensguard.Sim.Combat.Melee2;

namespace Ravensguard.Sim.Siege;

/// <summary>The hero's assault orders as recorded fight inputs (P8-LW-1): encoded like the formation orders, and given to the layer after the same delay (the hero's Leadership, the voice, the horn or a runner; 2 s with no formations).</summary>
public static class AssaultCommands
{
    public static FightInput Order(AssaultOrder kind, int x0, int y0, int x1, int y1) => new(FightInputKind.AssaultOrder, (int)kind, (x0 & 0xFFFF) | (y0 & 0xFFFF) << 16, (x1 & 0xFFFF) | (y1 & 0xFFFF) << 16);

    /// <summary>The delay in ticks an order would take now, for the wheel's tooltip.</summary>
    public static int DelayOf(FightWorld w, Fighter hero)
    {
        if (w.Formations is { } book && (book.Find(hero.Team, 1) ?? book.Rows.FirstOrDefault(r => r.Side == hero.Team)) is { } row)
        {
            var o = FormationOrder.Of(OrderKind.Advance);
            return book.DelayOf(w, row, ref o, hero.Id, book.HeroLeadership, out _);
        }
        return 40;
    }

    public static void Apply(FightWorld w, Fighter hero, FightInput i)
    {
        if (w.Assault is not { Live: true } layer || i.A <= 0 || i.A > (int)AssaultOrder.Sally) return;
        layer.Command((AssaultOrder)i.A, hero.Team, i.B & 0xFFFF, i.B >>> 16, i.C & 0xFFFF, i.C >>> 16, DelayOf(w, hero));
    }
}
