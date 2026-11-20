using Ravensguard.Content;
using Ravensguard.Sim.Combat;
using Ravensguard.Sim.Combat.Melee2;
using Ravensguard.Sim.Combat.Resolver2;
using Ravensguard.Sim.Combat.Wounds;
using Ravensguard.Sim.Core;

namespace Ravensguard.Sim.Sites;

/// <summary>
/// Blows that are not a fight's (exploration-and-sites 4.5, 4.6): a trap's spears or deadfall, a cave-in's rubble, a fall into a pit or through a floor, through the one resolver
/// (resolver-v2 4.1, 4.9) onto the person's wound book. Keys are game time and ids (overview 6.1): the trap or event, the sim-second, a serial.
/// </summary>
public sealed class SiteBlows
{
    private readonly FightContent _fc;
    private readonly ResolverV2 _r;
    private readonly BlowResult _res = new();
    private readonly Dictionary<string, WeaponV2> _synthetic = new(StringComparer.Ordinal);

    public SiteBlows(FightContent fc) { _fc = fc; _r = new ResolverV2(fc.T); }

    public BlowResult Last => _res;
    public CombatTablesV2 T => _fc.T;

    /// <summary>The worn pieces of a kit as the resolver sees them.</summary>
    public WornV2[] Worn(FighterKit k)
    {
        var a = new WornV2[k.Worn.Count];
        for (int i = 0; i < a.Length; i++) a[i] = WornV2.Of(_fc.T.Armour(k.Worn[i].ShapeId, k.Worn[i].MaterialId), _fc.T.Qa(k.Worn[i].Quality), fitCm3: k.SizeCm3);
        return a;
    }

    private WeaponV2 Synthetic(string type, int areaMm2, int penMm)
    {
        string key = $"site_{type}_{areaMm2}_{penMm}";
        if (_synthetic.TryGetValue(key, out var w)) return w;
        var t = type switch { "point" => AttackType.Point, "edge" => AttackType.Edge, _ => AttackType.Blunt };
        var shape = new WeaponShapeDef
        {
            Id = key, Name = key, HeadVolumeCm3 = 200, Family = "hafted", ReachMm = 1000,
            Attacks = [new AttackDef { Id = "trap", Name = "Trap", Type = t, ContactArea = areaMm2, PenetrationMm = penMm, Velocity = 1.0 }],
        };
        w = _fc.T.Synthetic(shape, "iron");
        _synthetic[key] = w;
        return w;
    }

    /// <summary>A part by base id ("upper_leg" draws a side), else upper body.</summary>
    public static int Part(BodyPlanV2 plan, string baseId, bool right)
    {
        int i = plan.IndexOf(baseId);
        if (i < 0) i = plan.IndexOf(baseId + (right ? "_r" : "_l"));
        if (i < 0) i = plan.IndexOf(baseId + (right ? "_l" : "_r"));
        return i < 0 ? Math.Max(0, plan.IndexOf("upper_body")) : i;
    }

    /// <summary>A blow of a set momentum (a trap's spears, a deadfall, rubble) on a part; the wounds go into <paramref name="book"/>. Returns how many wounds it made.</summary>
    public int Blow(WoundBook book, BodyPlanV2 plan, WornV2[] worn, int sizeCm3, string type, long momentumMilli, int areaMm2, int penMm, int part, in BlowKey key, long gameSec, ulong cause)
    {
        var s = StrikeV2.Create(Synthetic(type, areaMm2, penMm), 0, plan, part);
        s.Worn = worn; s.WornCount = worn.Length; s.DefenderSizeCm3 = sizeCm3 > 0 ? sizeCm3 : plan.SizeCm3; s.Facing = Facing.Front; s.Missile = true; s.ExtraMomentumMilli = momentumMilli;
        _r.ResolveImpact(s, key, _res);
        if (_res.WoundCount > 0) book.Apply(plan, _res, gameSec, key.FightTick, cause, dirty: true);
        return _res.WoundCount;
    }

    /// <summary>A fall of <paramref name="heightMm"/> (resolver-v2 4.9: <c>100 x kg x (3 x m + 0.5 x m/s)</c>) on the landing part.</summary>
    public int Fall(WoundBook book, BodyPlanV2 plan, WornV2[] worn, int sizeCm3, int massKg, int heightMm, int part, in BlowKey key, long gameSec, ulong cause)
    {
        var s = StrikeV2.Create(_fc.T.Weapon("fist", "bone"), 0, plan, part);
        s.Worn = worn; s.WornCount = worn.Length; s.DefenderSizeCm3 = sizeCm3 > 0 ? sizeCm3 : plan.SizeCm3; s.Facing = Facing.Front;
        _r.ResolveFall(s, LayerTest.FallMomentumMilli(massKg, heightMm, 0), key, _res);
        if (_res.WoundCount > 0) book.Apply(plan, _res, gameSec, key.FightTick, cause);
        return _res.WoundCount;
    }
}

/// <summary>The rules of traps (4.6): detection, disarming, the skip when sneaking light, and what each trap does.</summary>
public static class SiteTraps
{
    /// <summary>Detection chance per sim-second, permille (EX-10): <c>clamp(5 + (Perception - difficulty)/2 + light, 2, 80)</c> percent, light +10 at 120 and up, -15 below 30; x1.5 sneaking.</summary>
    public static int DetectPm(int perception, int difficulty, int light, bool sneaking)
    {
        int lb = light >= 120 ? 10 : light < 30 ? -15 : 0;
        int pct10 = 50 + (perception - difficulty) * 5 + lb * 10;           // tenths of a percent
        pct10 = Math.Clamp(pct10, 20, 800);
        if (sneaking) pct10 = Math.Min(800, pct10 * 3 / 2);
        return pct10;
    }

    /// <summary>Disarm success, permille (EX-10: Lockpicking 50 against 40 is 75%): <c>clamp(50 + 2.5 x (skill - difficulty), 5, 95)</c> percent.</summary>
    public static int DisarmPm(int lockpicking, int difficulty) => Math.Clamp(500 + 25 * (lockpicking - difficulty), 50, 950);

    /// <summary>True when the person faces within 90 degrees of the trap (a dot of the facing and the direction to it, Q10, of zero or more).</summary>
    public static bool Facing(int faceX, int faceY, int dx, int dy) => (long)faceX * dx + (long)faceY * dy >= 0;
}
