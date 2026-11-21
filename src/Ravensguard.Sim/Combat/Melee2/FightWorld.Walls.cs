namespace Ravensguard.Sim.Combat.Melee2;

/// <summary>The walls of an indoor fight (heists-and-jailbreaks 4.4): one-metre cells in millimetres of the fight's own plane. A cell that blocks movement (a wall, rock, a shut door, a cell's bars) and one that blocks a missile and the sight of a blow (a wall, rock, a shut door; bars let them through).</summary>
public interface IFightWalls
{
    bool BlocksMove(int xMm, int yMm);
    bool BlocksMissile(int xMm, int yMm);
}

/// <summary>P7-SH-A: walls that depend on who walks (a ship's decks: a man on a castle and a man under it stand on different floors). <see cref="Using"/> names the fighter whose body is about to be tested.</summary>
public interface IFighterWalls : IFightWalls
{
    void Using(int fighterId);
}

/// <summary>
/// [heists] Indoor fights use walls (heists-and-jailbreaks 4.4): with <see cref="Walls"/> set, a fighter cannot walk into a blocked cell (his body is a circle, so a doorway one metre wide holds one man at a time), a body pushed by another goes back
/// out of a wall, no blow lands through a wall, and a missile stops at the first wall in its way. The fight's settle path also reports what its blades suffered (<see cref="WeaponKeepPm"/>): each blow that was blocked, stopped by armour or landed
/// takes a little off the weapon's condition, and a brittle blade that broke is gone, so the barrier can write the wear back to the item rows. With no walls and no weapons with item rows every helper does nothing, so other fights and their hashes are
/// unchanged (neither is in the <c>FightHash</c>).
/// </summary>
public sealed partial class FightWorld
{
    /// <summary>The walls of the place; null: an open arena (every other fight).</summary>
    public IFightWalls? Walls { get; set; }

    private bool FreeAt(int x, int y)
    {
        int r = K.BodyRadiusMm;
        return !Walls!.BlocksMove(x - r, y - r) && !Walls.BlocksMove(x + r, y - r) && !Walls.BlocksMove(x - r, y + r) && !Walls.BlocksMove(x + r, y + r);
    }

    /// <summary>The step cut down to what the walls allow: all of it, else along one axis (a slide along a wall), else none.</summary>
    private (int, int) WallSlide(Fighter f, int mx, int my)
    {
        if (Walls is IFighterWalls fw) fw.Using(f.Id);
        if (FreeAt(f.X + mx, f.Y + my)) return (mx, my);
        if (mx != 0 && FreeAt(f.X + mx, f.Y)) return (mx, 0);
        if (my != 0 && FreeAt(f.X, f.Y + my)) return (0, my);
        return (0, 0);
    }

    private (int X, int Y)[] SavePositions() { var a = new (int, int)[Fighters.Count]; for (int i = 0; i < a.Length; i++) a[i] = (Fighters[i].X, Fighters[i].Y); return a; }

    private void KeepOutOfWalls((int X, int Y)[] before)
    {
        for (int i = 0; i < Fighters.Count; i++)
        {
            var f = Fighters[i];
            if (Walls is IFighterWalls fw) fw.Using(f.Id);
            if (FreeAt(f.X, f.Y)) continue;
            if (FreeAt(before[i].X, f.Y)) f.X = before[i].X;
            else if (FreeAt(f.X, before[i].Y)) f.Y = before[i].Y;
            else { f.X = before[i].X; f.Y = before[i].Y; }
        }
    }

    /// <summary>Whether a wall lies on the straight line between two points (sampled every 250 mm).</summary>
    public bool WallBetween(int x0, int y0, int x1, int y1)
    {
        long dx = x1 - x0, dy = y1 - y0, len = FightMath.Len(dx, dy);
        int n = (int)Math.Max(1, len / 250);
        for (int i = 1; i < n; i++) if (Walls!.BlocksMissile((int)(x0 + dx * i / n), (int)(y0 + dy * i / n))) return true;
        return false;
    }

    /// <summary>How far along its flight a missile first meets a wall in [f0, f1], or -1.</summary>
    private long MissileWall(Projectile p, long f0, long f1)
    {
        for (long f = f0 + 100; f <= f1; f += 100) { var (x, y) = p.At(f); if (Walls!.BlocksMissile(x, y)) return Math.Max(f0, f - 100); }
        return -1;
    }

    // ------------------------------------------------------------------ the blades

    private Dictionary<int, int>? _bladeLoss;

    /// <summary>A blow's toll on the blades: the attacker's weapon loses 8 permille when the blow was blocked, 5 when armour stopped it, 2 when it landed; a weapon that blocked loses 6.</summary>
    private void WearBlades(Fighter a, Fighter t, bool weaponBlock)
    {
        if (a.Kit.Weapon is { ItemId: not 0 })
        {
            int loss = _r.Blocked ? 8 : _r.Hit && _r.WoundCount == 0 ? 5 : _r.Hit ? 2 : 0;
            if (loss > 0) Lose(a, loss);
        }
        if (_r.Blocked && weaponBlock && t.Kit.Weapon is { ItemId: not 0 }) Lose(t, 6);
    }

    private void Lose(Fighter f, int pm) { _bladeLoss ??= []; _bladeLoss[f.Id] = Math.Min(1000, _bladeLoss.GetValueOrDefault(f.Id) + pm); }

    /// <summary>The share (permille) of its condition a fighter's weapon keeps after this fight: 1000 untouched, 0 for a brittle blade that broke. Only weapons that have an item row are reported.</summary>
    public IEnumerable<(ulong Item, int KeepPm)> WeaponKeepPm()
    {
        foreach (var f in Fighters)
        {
            if (f.Kit.Weapon is not { ItemId: not 0 } w) continue;
            int keep = f.Broke ? 0 : 1000 - (_bladeLoss?.GetValueOrDefault(f.Id) ?? 0);
            if (keep < 1000) yield return (w.ItemId, keep);
        }
    }
}
