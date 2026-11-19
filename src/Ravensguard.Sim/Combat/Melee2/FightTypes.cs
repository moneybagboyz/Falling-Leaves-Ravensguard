using Ravensguard.Sim.Combat.Resolver2;

namespace Ravensguard.Sim.Combat.Melee2;

// The shipped fighting layer (melee-and-ranged.md, P4-C): the spike's state machine on integers. Positions in millimetres, facing as a Q10 unit vector
// (1024 = 1.0), stamina in hundredths, time in 20 Hz fight ticks (FightTick, overview 6.1). No floating point in this folder.

/// <summary>What a fighter is doing (melee-and-ranged 3.3). Append only: saved in the <c>FightRecord</c>.</summary>
public enum FAct : byte { Idle, Windup, Strike, Recover, Block, Dodge, Stagger, Down, Dead, Fled, Surrendered, Bracing, Reloading, Drawing, Aiming }

/// <summary>The swing's style (4.1): quick (released inside the wind-up), normal, heavy (held past it).</summary>
public enum SwingStyle : byte { Normal, Quick, Heavy }

/// <summary>What a blow or shot is (an attack of the weapon in hand, a throw).</summary>
public enum BlowKind : byte { Melee, Throw }

public enum FightEventKind : byte
{
    Windup, Feint, Whiff, Blocked, Parried, Dodged, Hit, Stopped, Missed, Down, Wake, Death, Rout, Rally, Surrender, Fled, Riposte, Overpowered,
    Shot, ProjectileHit, ProjectileMiss, Throw, Broke, Reloaded, Shove, Interrupted, Disarmed, OutOfAmmo,
    // mounted-combat (Combat/Mounted): A, B as each emitter documents
    Charge, Unhorsed, Fell, HorseFell, Balked, Reared, Bolted, Trampled, BraceStrike, Shattered,
}

/// <summary>One thing that happened in the fight, for the log, the flavour text (feedback-flavour-log) and the statistics. Text is the presentation's.</summary>
public readonly record struct FightEvent(int Tick, FightEventKind Kind, int Actor, int Target, short Part = -1, int A = 0, int B = 0);

/// <summary>An attack asked for: the attack index (-1: the AI's pick, Auto), the target fighter (-1: the nearest in the cone at impact), the part aimed at (-1: by area) and a named gap.</summary>
public struct AttackReq
{
    public int Attack, Target, Part, Gap;
    public SwingStyle Style;
    /// <summary>Released by itself when the wind-up completes (the AI); a player's swing waits for the button.</summary>
    public bool Auto;
    public BlowKind Kind;
    public static AttackReq Of(int attack = -1, int target = -1, int part = -1, int gap = -1, bool auto = true) =>
        new() { Attack = attack, Target = target, Part = part, Gap = gap, Auto = auto };
}

/// <summary>One fighter's intent for the next step (the spike's MeleeCommand on integers). Held fields persist; one-shots are consumed by the step.</summary>
public sealed class FightCommand
{
    /// <summary>Desired walk direction (Q10, zero = stand).</summary>
    public int MoveX, MoveY;
    /// <summary>Desired facing (Q10, zero = keep).</summary>
    public int FaceX, FaceY;
    public bool Sprint, Block, Hold;
    // one-shots
    public AttackReq? Start;
    public bool Feint, Release, Reload;
    public int DodgeX, DodgeY;
    /// <summary>Throw at a point (mm, fight frame): the thrown weapon or improvised object in hand, or the next throwable carried.</summary>
    public (int X, int Y)? Throw;

    public bool HasDodge => DodgeX != 0 || DodgeY != 0;
    public void ClearOneShots() { Start = null; Feint = false; Release = false; Reload = false; DodgeX = DodgeY = 0; Throw = null; }
    public void ClearAll() { ClearOneShots(); MoveX = MoveY = FaceX = FaceY = 0; Sprint = Block = Hold = false; }
}

/// <summary>A player's combat input (the hero, in the combat stance): what the game sends and the session recorder logs, so a replay repeats the fight exactly.</summary>
public enum FightInputKind : byte
{
    /// <summary>A = move direction index (0-63, or 64 for none), B = 1 sprint.</summary>
    Move = 1,
    /// <summary>A = facing direction index 0-63.</summary>
    Face = 2,
    BlockDown = 3, BlockUp = 4,
    /// <summary>LMB down: A = attack index (-1 Auto), B = part index (-1 none), C = gap index (-1).</summary>
    AttackDown = 5,
    AttackUp = 6,
    /// <summary>A = direction index 0-63.</summary>
    Dodge = 7,
    Reload = 8,
    /// <summary>A, B = target point in mm.</summary>
    Throw = 9,
    /// <summary>A = target fighter (focus).</summary>
    Target = 10,
    /// <summary>A called shot struck from the paused menu (T, then F): A = attack index, B = part index, C = gap index (-1); the swing releases itself at the end of its wind-up.</summary>
    CalledShot = 11,
}

public readonly record struct FightInput(FightInputKind Kind, int A = 0, int B = 0, int C = 0);

/// <summary>Integer geometry for the fight: Q10 unit vectors, an integer square root, a 64-entry rotation table, and the 256-entry inverse-normal table of the aim cone.</summary>
public static class FightMath
{
    public const int One = 1024;

    /// <summary>cos and sin of k x 5.625 degrees, x1024 (64 directions).</summary>
    public static readonly int[] Cos64 = [1024,1019,1004,980,946,903,851,792,724,650,569,483,392,297,200,100,0,-100,-200,-297,-392,-483,-569,-650,-724,-792,-851,-903,-946,-980,-1004,-1019,-1024,-1019,-1004,-980,-946,-903,-851,-792,-724,-650,-569,-483,-392,-297,-200,-100,0,100,200,297,392,483,569,650,724,792,851,903,946,980,1004,1019];
    public static readonly int[] Sin64 = [0,100,200,297,392,483,569,650,724,792,851,903,946,980,1004,1019,1024,1019,1004,980,946,903,851,792,724,650,569,483,392,297,200,100,0,-100,-200,-297,-392,-483,-569,-650,-724,-792,-851,-903,-946,-980,-1004,-1019,-1024,-1019,-1004,-980,-946,-903,-851,-792,-724,-650,-569,-483,-392,-297,-200,-100];

    /// <summary>The standard normal's quantiles at (i + 0.5) / 256, x1000 (the aim error draw).</summary>
    public static readonly int[] InvNormal256 = [-2886,-2521,-2335,-2207,-2107,-2024,-1953,-1891,-1836,-1785,-1739,-1696,-1656,-1619,-1584,-1550,-1518,-1488,-1459,-1431,-1405,-1379,-1354,-1330,-1306,-1284,-1262,-1240,-1219,-1199,-1179,-1160,-1141,-1122,-1104,-1086,-1069,-1052,-1035,-1018,-1002,-986,-970,-954,-939,-924,-909,-894,-880,-866,-851,-837,-824,-810,-796,-783,-770,-757,-744,-731,-718,-706,-693,-681,-668,-656,-644,-632,-620,-608,-597,-585,-573,-562,-550,-539,-528,-517,-505,-494,-483,-472,-461,-451,-440,-429,-418,-408,-397,-386,-376,-365,-355,-345,-334,-324,-313,-303,-293,-283,-273,-262,-252,-242,-232,-222,-212,-202,-192,-182,-172,-162,-152,-142,-133,-123,-113,-103,-93,-83,-74,-64,-54,-44,-34,-24,-15,-5,5,15,24,34,44,54,64,74,83,93,103,113,123,133,142,152,162,172,182,192,202,212,222,232,242,252,262,273,283,293,303,313,324,334,345,355,365,376,386,397,408,418,429,440,451,461,472,483,494,505,517,528,539,550,562,573,585,597,608,620,632,644,656,668,681,693,706,718,731,744,757,770,783,796,810,824,837,851,866,880,894,909,924,939,954,970,986,1002,1018,1035,1052,1069,1086,1104,1122,1141,1160,1179,1199,1219,1240,1262,1284,1306,1330,1354,1379,1405,1431,1459,1488,1518,1550,1584,1619,1656,1696,1739,1785,1836,1891,1953,2024,2107,2207,2335,2521,2886];

    public static long ISqrt(long v)
    {
        if (v <= 0) return 0;
        // Newton's method on integers (no floating point in the fight step): start above the root and come down
        long x = v, r = (v >> 1) + 1;
        if (v < 4) return 1;
        int bits = 64 - System.Numerics.BitOperations.LeadingZeroCount((ulong)v);
        r = 1L << ((bits + 1) / 2);
        while (true) { x = (r + v / r) >> 1; if (x >= r) break; r = x; }
        while (r * r > v) r--;
        while ((r + 1) * (r + 1) <= v) r++;
        return r;
    }

    public static long Len(long x, long y) => ISqrt(x * x + y * y);

    /// <summary>The Q10 unit vector of (x, y); zero stays zero.</summary>
    public static (int X, int Y) Norm(long x, long y)
    {
        long l = Len(x, y);
        if (l == 0) return (0, 0);
        return ((int)(x * One / l), (int)(y * One / l));
    }

    public static int Dot(int ax, int ay, int bx, int by) => (int)(((long)ax * bx + (long)ay * by) >> 10);

    /// <summary>The nearest of the 64 directions to (x, y), or 64 for zero.</summary>
    public static int DirIndex(int x, int y)
    {
        if (x == 0 && y == 0) return 64;
        int best = 0; long bd = long.MinValue;
        for (int k = 0; k < 64; k++) { long d = (long)Cos64[k] * x + (long)Sin64[k] * y; if (d > bd) { bd = d; best = k; } }
        return best;
    }

    public static (int X, int Y) Dir(int index) => index is < 0 or >= 64 ? (0, 0) : (Cos64[index], Sin64[index]);

    /// <summary>Turns the facing (fx, fy) toward (wx, wy) by at most <paramref name="steps"/> table steps (5.625 degrees each); snaps when closer than one step.</summary>
    public static (int X, int Y) TurnToward(int fx, int fy, int wx, int wy, int steps)
    {
        var (nx, ny) = Norm(wx, wy);
        if (nx == 0 && ny == 0) return (fx, fy);
        if (fx == 0 && fy == 0) return (nx, ny);
        int dot = Dot(fx, fy, nx, ny);
        int cosLimit = Cos64[Math.Clamp(steps, 0, 16)];
        if (dot >= cosLimit) return (nx, ny);
        long cross = (long)fx * ny - (long)fy * nx;
        int k = Math.Clamp(steps, 1, 16);
        int c = Cos64[k], s = cross >= 0 ? Sin64[k] : -Sin64[k];
        long rx = ((long)fx * c - (long)fy * s) >> 10, ry = ((long)fx * s + (long)fy * c) >> 10;
        return Norm(rx, ry);
    }

    /// <summary>Integer cube root (floor).</summary>
    public static long ICbrt(long v)
    {
        if (v <= 0) return 0;
        long r = 0, hi = 2_097_152;
        while (r < hi) { long m = (r + hi + 1) / 2; if (m * m * m <= v) r = m; else hi = m - 1; }
        return r;
    }

    /// <summary>The FNV-1a step used by every fight hash.</summary>
    public static ulong Mix(ulong h, long v) { unchecked { for (int i = 0; i < 8; i++) { h ^= (byte)(v >> (8 * i)); h *= 1099511628211UL; } } return h; }
}
