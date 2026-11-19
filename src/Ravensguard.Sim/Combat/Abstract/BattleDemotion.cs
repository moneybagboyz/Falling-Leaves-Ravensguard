using Ravensguard.Sim.Combat.Melee2;
using Ravensguard.Sim.Combat.Morale;
using Ravensguard.Sim.Combat.Wounds;

namespace Ravensguard.Sim.Combat.Abstract;

/// <summary>
/// Live to abstract and back (abstract-battles-v1 4.7). Demotion: a live fight the bubble leaves becomes an <see cref="AbstractBattle"/> from its state at that instant: every fighter
/// (not the horses: a rider is a mounted soldier) where he stands now, his own wound book copied (blood, pain, wounds, state), his status (down, dead, fled, yielded, routing) and
/// the arrows left; step 0 is the game second the live fight had reached. No soldier is lost or made twice (test AB-8). Promotion: an abstract battle's soldiers back into a
/// <see cref="FightSetup"/> at the bubble's edge (the lines 14 m apart, archers behind), their wounds kept in the books the setup's ledger hands the live fight.
/// </summary>
public static class BattleDemotion
{
    public static BattleInput InputOf(FightWorld w, string cause = "demoted")
    {
        var kits = new List<FighterKit>(); var books = new List<WoundBook?>(); var st = new List<SoldierStatus>(); var rt = new List<bool>();
        var mounted = new HashSet<int>();
        foreach (var f in w.Fighters)
        {
            if (w.IsHorse(f)) continue;
            if (w.Riding(f) != null) mounted.Add(kits.Count);
            kits.Add(f.Kit with { X = f.X, Y = f.Y, FaceX = f.FX, FaceY = f.FY, AmmoCount = f.Launcher != null ? f.Ammo + (f.Loaded ? 1 : 0) : f.Kit.AmmoCount });
            books.Add(f.Book);
            st.Add(f.Act switch { FAct.Dead => SoldierStatus.Dead, FAct.Down => SoldierStatus.Down, FAct.Fled => SoldierStatus.Fled, FAct.Surrendered => SoldierStatus.Surrendered, _ => SoldierStatus.Active });
            rt.Add(f.Routing);
        }
        var sides = new Dictionary<int, SideKind>();
        foreach (int team in kits.Select(k => k.Team).Distinct()) sides[team] = w.SideOf(team);
        return new BattleInput(kits, w.Seed, w.FightId, w.GameSec, cause) { Books = books, Statuses = st, Routing = rt, Mounted = mounted, SideKinds = sides, Leaders = false };
    }

    /// <summary>Promotion: the still-standing soldiers of a battle as a live fight's setup (each side's line where the battle put it, 14 m apart; archers 4 m behind), the same ids.</summary>
    public static FightSetup Promote(AbstractBattle b, FightKind kind = FightKind.Raid)
    {
        var kits = new List<FighterKit>();
        foreach (var s in b.Soldiers)
        {
            if (s.Status != SoldierStatus.Active) continue;
            int sign = s.Side == 0 ? -1 : 1;
            int n = kits.Count(k => k.Team == s.Kit.Team);
            kits.Add(s.Kit with { X = sign * (7000 + (s.Shooter != null ? 4000 : 0)), Y = (n % 20 - 10) * 1300, FaceX = -sign * FightMath.One, FaceY = 0, AmmoCount = s.Shooter != null ? s.Ammo : s.Kit.AmmoCount });
        }
        return new FightSetup(kind, kits, b.Input.Seed, b.Input.BattleId, b.Input.GameSec + (long)b.Step * b.K.StepS * AbstractBattle.TicksPerS, b.Input.Cause) { SideKinds = b.Input.SideKinds };
    }
}
