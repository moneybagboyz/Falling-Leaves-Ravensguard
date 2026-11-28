using Ravensguard.Content;
using Ravensguard.Sim.Combat.Resolver2;
using Ravensguard.Sim.Combat.Wounds;
using Ravensguard.Sim.Core;

namespace Ravensguard.Sim.Combat.Melee2;

/// <summary>One hold (P8-LM-3, bestiary 3.3): the holder's plan names the kind (a drag toward water, a constrict) and the force in newtons; the victim cannot walk while held.</summary>
public sealed class HoldRow
{
    public int Holder, Victim, ForceN, Since;
    public bool Drag;
    /// <summary>A drag's aim: the water cell the holder hauls the victim toward (mm), refreshed each sim-second.</summary>
    public int AimX, AimY;
    /// <summary>Seconds the victim has been held under water (a drag) or squeezed (a constrict).</summary>
    public int UnderS;
}

/// <summary>
/// The bestiary's two traits that need the fight's own step (P8-LM-3, D8): a creature's <b>hold</b> (<see cref="GraspDef"/>: the bog-thing's drag toward water, the lindworm's constrict at
/// 6,000 N) and its <b>fright</b> (<see cref="FearDef"/>: the barghest's howl at night). Neither is a rule of its own: the hold is taken on the stream 0x831 and a drowning or crushed
/// man dies by the breath rule and the one resolver (a constrict is a blunt blow of the plan's squeeze through the victim's worn pieces onto its chest, every sim-second, and the
/// breath stops with it); a fright is a morale penalty. All of it is off (no plan carries a grasp or a fear) in every other fight, so no other fight's bytes move.
/// </summary>
public sealed partial class FightWorld
{
    private bool _monsterPlans;
    private HashSet<int>? _frightened;
    private uint _squeezeSerial;
    private WeaponV2? _squeezeWeapon;
    private readonly BlowResult _hr = new();

    /// <summary>The holds in force, by holder id (stable order).</summary>
    public List<HoldRow> Holds { get; } = [];
    /// <summary>Is this fight position (mm) in a water cell (a bog, a flooded level)? Set by the site crawl; null: no water in this fight.</summary>
    public Func<int, int, bool>? WaterAt { get; set; }
    /// <summary>Is it night where this fight is (the barghest's howl works only in the dark of night)? Set by the site crawl from the game clock; null: day.</summary>
    public Func<bool>? NightNow { get; set; }

    /// <summary>A scripted scene (a captive bound hand and foot, a sleeper) where the held man cannot struggle; false in every fight the AI or a player runs.</summary>
    public bool NoStruggle { get; set; }

    private bool Night => NightNow?.Invoke() ?? false;

    /// <summary>The holder takes hold of the victim (the AI does it by a roll each sim-second; a test or a scripted scene may do it outright). False when it already holds, or the victim is held, dead or out of the fight.</summary>
    public bool Seize(int holder, int victim)
    {
        if ((uint)holder >= (uint)Fighters.Count || (uint)victim >= (uint)Fighters.Count || holder == victim) return false;
        var h = Fighters[holder]; var v = Fighters[victim];
        if (h.Plan.Grasp is not { } g || !h.Active || v.Act == FAct.Dead || v.HeldBy >= 0 || HoldOf(holder) != null) return false;
        var row = new HoldRow { Holder = holder, Victim = victim, ForceN = g.ForceN, Since = Tick, Drag = g.Kind == "drag" };
        Holds.Add(row); Holds.Sort((a, b) => a.Holder.CompareTo(b.Holder));
        v.HeldBy = holder;
        _monsterPlans = true;
        if (row.Drag) AimAtWater(row);
        Emit(FightEventKind.Seized, holder, victim, -1, g.ForceN);
        return true;
    }

    public HoldRow? HoldOf(int holder) { foreach (var r in Holds) if (r.Holder == holder) return r; return null; }

    /// <summary>The hold on a victim, or null.</summary>
    public HoldRow? HeldRow(int victim) { foreach (var r in Holds) if (r.Victim == victim) return r; return null; }

    /// <summary>Lets go: the victim walks again and, if the breath had been stopped only by the hold, draws it.</summary>
    public void Release(HoldRow row)
    {
        if (!Holds.Remove(row)) return;
        var v = Fighters[row.Victim];
        v.HeldBy = -1;
        if (row.UnderS > 0) v.Book.ResumeBreath(v.Plan);
        Emit(FightEventKind.Released, row.Holder, row.Victim);
    }

    /// <summary>[bestiary] Each tick after morale: a fright is thrown the moment a foe is within its radius; each sim-second holds are taken, kept, squeezed or let go; each tick a drag moves.</summary>
    private void MonsterStep()
    {
        foreach (var f in Fighters)
        {
            if (!f.Active || f.Plan.Fear is not { } fe || _frightened != null && _frightened.Contains(f.Id)) continue;
            if (fe.NightOnly && !Night) continue;
            long r = fe.RadiusM * 1000L; int hit = 0;
            foreach (var o in Fighters)
                if (o.Team != f.Team && o.Active && DistMm(f, o) <= r) hit++;
            if (hit == 0) continue;
            foreach (var o in Fighters)
            {
                if (o.Team == f.Team || !o.Active || DistMm(f, o) > r) continue;
                EnsureMorale(o);
                o.M.PenaltyX100 += fe.ShockPm * 10; o.M.LastLossTick = Tick;
            }
            (_frightened ??= []).Add(f.Id);
            Emit(FightEventKind.Frightened, f.Id, -1, -1, fe.ShockPm, hit);
        }
        if (Tick % 20 == 0) HoldSecond();
        foreach (var row in Holds) HoldMove(row);
    }

    private void HoldSecond()
    {
        // take: a free holder in reach of a foe rolls once a sim-second (stream 0x831 keyed on the fight, the holder and the second)
        foreach (var h in Fighters)
        {
            if (h.Plan.Grasp is not { } g || !h.Active || h.Routing || HoldOf(h.Id) != null) continue;
            Fighter? best = null; long bd = long.MaxValue;
            foreach (var o in Fighters)
            {
                if (o.Team == h.Team || o.Act is FAct.Dead or FAct.Fled or FAct.Surrendered || o.HeldBy >= 0) continue;
                long d = DistMm(h, o);
                if (d <= g.RangeMm && d < bd) { bd = d; best = o; }
            }
            if (best == null) continue;
            if (DetRng.Range(Seed, Tick, (FightId << 12) + (ulong)h.Id, RngStreams.LairMonsterBehaviour, (uint)best.Id, 0, 1000) < g.ChancePm) Seize(h.Id, best.Id);
        }
        // keep: the escape, the water, the crush
        foreach (var row in Holds.ToList())
        {
            var h = Fighters[row.Holder]; var v = Fighters[row.Victim]; var g = h.Plan.Grasp!;
            if (!h.Active || v.Act == FAct.Dead || DistMm(h, v) > g.RangeMm * 2) { Release(row); continue; }
            // a conscious victim struggles once a sim-second: strength against the hold's force, 20 to 400 permille (a quarter of the ratio: the hold is strong)
            if (!NoStruggle && v.Active && v.Book.Conscious)
            {
                int esc = Math.Clamp((int)((long)v.Kit.Strength * 250 / Math.Max(1, v.Kit.Strength + row.ForceN)), 20, 400);
                if (DetRng.Range(Seed, Tick, (FightId << 12) + (ulong)v.Id, RngStreams.LairMonsterBehaviour, 100u + (uint)row.Holder, 0, 1000) < esc) { Release(row); continue; }
            }
            if (row.Drag)
            {
                AimAtWater(row);
                if (WaterAt?.Invoke(v.X, v.Y) == true) { row.UnderS++; v.Book.StopBreath(GameSec); }
            }
            else
            {
                row.UnderS++;
                Squeeze(h, v, g);
                if (row.UnderS >= 3) v.Book.StopBreath(GameSec);                 // a chest held at 6,000 N cannot fill
            }
        }
    }

    /// <summary>One sim-second of a constrict: a blunt blow of the plan's squeeze on the victim's chest through whatever it wears (the one resolver; the wounds go into its book).</summary>
    private void Squeeze(Fighter h, Fighter v, GraspDef g)
    {
        int part = v.Plan.IndexOf(g.SqueezePart);
        if (part < 0) part = Math.Max(0, v.Plan.IndexOf("upper_body"));
        _squeezeWeapon ??= T.Synthetic(new WeaponShapeDef
        {
            Id = "monster_squeeze", Name = "Squeeze", HeadVolumeCm3 = 2000, Family = "hafted", ReachMm = 1000,
            Attacks = [new AttackDef { Id = "squeeze", Name = "Squeeze", Type = AttackType.Blunt, ContactArea = g.SqueezeAreaMm2, PenetrationMm = 0, Velocity = 1.0 }],
        }, "bone");
        var s = StrikeV2.Create(_squeezeWeapon, 0, v.Plan, part);
        s.Worn = v.Worn; s.WornCount = v.Worn.Length; s.DefenderSizeCm3 = v.Kit.SizeCm3 > 0 ? v.Kit.SizeCm3 : v.Plan.SizeCm3; s.Facing = Facing.Front; s.Missile = true; s.ExtraMomentumMilli = g.SqueezeMilli;
        var key = new BlowKey(Seed, Tick, (FightId << 12) + (ulong)h.Id, ++_squeezeSerial);
        Resolver.ResolveImpact(s, key, _hr);
        if (_hr.WoundCount > 0) { v.Book.Apply(v.Plan, _hr, GameSec, Tick, 0xC0E5UL, dirty: true); RefreshBody(v); }
    }

    private static readonly (int X, int Y)[] Ring = [(1, 0), (1, 1), (0, 1), (-1, 1), (-1, 0), (-1, -1), (0, -1), (1, -1)];

    /// <summary>The nearest water cell within 12 m of the holder (rings of a metre, the eight directions in a fixed order); with no water the holder holds where it stands.</summary>
    private void AimAtWater(HoldRow row)
    {
        var h = Fighters[row.Holder];
        row.AimX = h.X; row.AimY = h.Y;
        if (WaterAt == null) return;
        if (WaterAt(h.X, h.Y)) return;
        for (int r = 1; r <= 12; r++)
            foreach (var (dx, dy) in Ring)
            {
                int x = h.X + dx * r * 1000, y = h.Y + dy * r * 1000;
                if (WaterAt(x, y)) { row.AimX = x; row.AimY = y; return; }
            }
    }

    /// <summary>A held victim keeps within a metre of the holder's front; a drag walks the pair toward the water at the plan's drag speed.</summary>
    private void HoldMove(HoldRow row)
    {
        var h = Fighters[row.Holder]; var v = Fighters[row.Victim];
        if (row.Drag && h.Active && h.Plan.Grasp is { } g && (row.AimX != h.X || row.AimY != h.Y))
        {
            long dx = row.AimX - h.X, dy = row.AimY - h.Y, len = FightMath.Len(dx, dy);
            int step = (int)Math.Min(len, g.DragMmps / 20);
            if (len > 0) { h.X += (int)(dx * step / len); h.Y += (int)(dy * step / len); }
        }
        if (h.Active || v.HeldBy == h.Id)
        {
            var (fx, fy) = FightMath.Norm(v.X - h.X, v.Y - h.Y);
            if (fx == 0 && fy == 0) fx = FightMath.One;
            long dist = 2 * K.BodyRadiusMm;                                 // exactly where the bodies stand apart, so the separation pass does not push the pair back
            v.X = h.X + (int)((long)fx * dist >> 10); v.Y = h.Y + (int)((long)fy * dist >> 10);
        }
    }
}
