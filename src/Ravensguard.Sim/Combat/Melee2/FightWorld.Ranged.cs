using Ravensguard.Content;
using Ravensguard.Sim.Combat.Resolver2;
using Ravensguard.Sim.Core;

namespace Ravensguard.Sim.Combat.Melee2;

/// <summary>
/// An arrow, a bolt or a thrown object in flight (melee-and-ranged 4.5, 4.6): a straight segment per tick in the ground plane with a height that follows the aim's slope
/// (plus the arc of an auto-lob), tested against every body's capsule (radius 250 mm, 1.8 m tall; 0.4 m lying down). It hits the first body on its path, friend or foe.
/// </summary>
public sealed class Projectile
{
    public uint Serial;
    public int Shooter, Team;
    public int X0, Y0, Z0;
    /// <summary>Ground-plane direction (Q10) and the height change per mm flown, millionths.</summary>
    public int DirX, DirY, SlopePpm;
    public int StepMm, RangeMm;
    public long Flown;
    /// <summary>Auto-lob: the arc's apex (mm) over the flight's planned length.</summary>
    public int ApexMm, PlanMm;
    /// <summary>The ammunition id (an arrow) or, for a throw, the thrown shape and material.</summary>
    public string Ammo = "", Shape = "", Material = "";
    public bool Thrown, Improvised;
    /// <summary>Launch speed (mm/s) and power (permille) for the launcher momentum; mass in grams.</summary>
    public int V0Mmps, PowerPm, MassG;
    /// <summary>The part the shooter called (T), or -1.</summary>
    public int AimPart = -1;
    public bool Done;

    public int Z(long f)
    {
        long z = Z0 + SlopePpm * f / 1_000_000;
        if (ApexMm > 0 && PlanMm > 0) { long q = Math.Clamp(f, 0, PlanMm); z += 4L * ApexMm * q / PlanMm * (PlanMm - q) / PlanMm; }
        return (int)z;
    }

    public (int X, int Y) At(long f) => (X0 + (int)(DirX * f / FightMath.One), Y0 + (int)(DirY * f / FightMath.One));

    public ulong Hash(ulong h)
    {
        h = FightMath.Mix(h, Serial); h = FightMath.Mix(h, Shooter); h = FightMath.Mix(h, X0); h = FightMath.Mix(h, Y0); h = FightMath.Mix(h, Z0);
        h = FightMath.Mix(h, DirX); h = FightMath.Mix(h, DirY); h = FightMath.Mix(h, SlopePpm); h = FightMath.Mix(h, Flown); h = FightMath.Mix(h, ApexMm); h = FightMath.Mix(h, Done ? 1 : 0);
        return h;
    }
}

public sealed partial class FightWorld
{
    // ------------------------------------------------------------------ launchers (4.5)

    /// <summary>Momentum of a launched missile at <paramref name="flownMm"/>: <c>mass_g x v x power x 485 / 10,000</c>, with <c>v(d) = v0 x (1 - 1.2 d_m / 1000)</c> (milli-units).</summary>
    public long LauncherMomentumMilli(int massG, int v0Mmps, int powerPm, long flownMm)
    {
        long dm = flownMm / 1000;
        long v = (long)v0Mmps * Math.Max(0, 1_000_000 - K.DragPpmPerM * dm) / 1_000_000;
        return (long)massG * v * powerPm * 485 / 10_000_000;
    }

    /// <summary>A thrown object's launch speed (mm/s): <c>18 x 3 / (3 + kg) x clamp(strength / 1250, 0.5, 1.6)</c>.</summary>
    public int ThrowSpeedMmps(int massG, int strength)
    {
        long sp = Math.Clamp(strength * 1000L / K.ThrowStrengthRef, K.ThrowStrengthMinPm, K.ThrowStrengthMaxPm);
        return (int)((long)K.ThrowVMmps * K.ThrowMassRefG / (K.ThrowMassRefG + massG) * sp / 1000);
    }

    /// <summary>A thrown object's momentum: <c>mass_g x v x 9.7</c> milli-units.</summary>
    public long ThrowMomentumMilli(int massG, int vMmps) => (long)massG * vMmps * K.ThrowMomentumK / 10_000;

    public int ThrowRangeMm(int vMmps) => Math.Min(K.ThrowRangeCapM * 1000, (int)((long)vMmps * K.ThrowRangePm / 1000));

    public int DrawTicks(Fighter f) => f.Launcher is not { } l || l.DrawTicks <= 0 ? 0 : Scaled(l.DrawTicks, f.ArcherySkill, f.OverKg, 1);

    public int AiAimTicks(Fighter f) => K.AimFullTicks;

    /// <summary>Aim sigma in millidegrees: <c>3 x (1 - skill / 120)</c> at full draw and a full second of aim; x2 released early, x3 a quick shot, x1.5 moving, x1.4 one eye.</summary>
    public int AimSigmaMdeg(int skill, bool early, int aimedTicks, bool moving, bool oneEye, int baseMdeg = 0)
    {
        long s = (long)(baseMdeg > 0 ? baseMdeg : K.AimSigmaMdeg) * Math.Max(0, K.AimSkillDivisor - Math.Clamp(skill, 0, 100)) / K.AimSkillDivisor;
        if (early) s = s * K.AimEarlyPm / 1000;
        else if (aimedTicks < K.AimQuickTicks) s = s * K.AimQuickPm / 1000;
        else if (aimedTicks < K.AimFullTicks) s = s * (2000 - 1000L * (aimedTicks - K.AimQuickTicks) / Math.Max(1, K.AimFullTicks - K.AimQuickTicks)) / 1000;
        if (moving) s = s * K.AimMovingPm / 1000;
        if (oneEye) s = s * K.AimOneEyePm / 1000;
        return (int)s;
    }

    /// <summary>The offset (mm) at <paramref name="distMm"/> of an aim error of <paramref name="sigmaMdeg"/> for a uniform draw <paramref name="u"/> (0-255) of the inverse-normal table.</summary>
    public static int AimOffsetMm(long distMm, int sigmaMdeg, int u)
    {
        long theta = (long)sigmaMdeg * 17_453 / 1000 * FightMath.InvNormal256[u & 255] / 1000;     // microradians
        return (int)(distMm * theta / 1_000_000);
    }

    /// <summary>Height (mm) of the centre of a body part, for a called shot's aim point (melee 4.5).</summary>
    public static int PartHeightMm(PartV2 p) => (p.IsHosted ? "" : p.BaseId) switch
    {
        "head" => 1700, "neck" => 1550, "upper_body" => 1300, "lower_body" => 1000, "upper_arm" => 1350, "lower_arm" => 1050, "hand" => 900,
        "upper_leg" => 700, "lower_leg" => 300, "foot" => 50, _ => 1250,
    };

    private void StartReload(Fighter f)
    {
        if (f.Launcher == null || f.Ammo <= 0) return;
        f.Act = FAct.Reloading; f.ActTicks = Math.Max(1, f.Launcher.ReloadTicks);
    }

    private void StartDraw(Fighter f, AttackReq req)
    {
        if (!f.Loaded)
        {
            if (f.Ammo > 0 && f.Act != FAct.Reloading) StartReload(f);
            else if (f.Ammo <= 0) Emit(FightEventKind.OutOfAmmo, f.Id, -1);
            return;
        }
        f.Req = req; f.Req.Kind = BlowKind.Melee;
        f.DrawElapsed = 0;
        if (DrawTicks(f) <= 0) { f.Act = FAct.Aiming; f.AimSince = Tick; }
        else f.Act = FAct.Drawing;
    }

    /// <summary>Lets the arrow or bolt go at the aim point (the target's centre, or the called part's height), with the aim error drawn from stream 0x440.</summary>
    private void Loose(Fighter f)
    {
        if (f.Launcher is not { } l || !f.Loaded || f.AmmoDef == null) { f.Act = FAct.Idle; return; }
        bool early = f.Act == FAct.Drawing && f.DrawElapsed < DrawTicks(f);
        int aimed = f.Act == FAct.Aiming ? Tick - f.AimSince : 0;
        Fighter? t = f.Req.Target >= 0 && f.Req.Target < Fighters.Count ? Fighters[f.Req.Target] : NearestEnemyOf(f);
        int tx, ty, tz;
        if (t != null) { tx = t.X; ty = t.Y; tz = f.Req.Part >= 0 && f.Req.Part < t.Plan.Parts.Length ? PartHeightMm(t.Plan.Parts[f.Req.Part]) : K.AimHeightMm; }
        else { tx = f.X + f.FX * 30; ty = f.Y + f.FY * 30; tz = K.AimHeightMm; }
        int sigma = AimSigmaMdeg(f.ArcherySkill, early, aimed, f.LastMoveMm * 20 > K.MoveCancelMmps, f.OneEye);
        if (Mounts != null) sigma = MountedSigma(f, sigma);                                  // [P7 CV-B] a rider's shot: the mounted aim penalty widens the cone
        int power = l.MinStrength > 0 ? (int)Math.Min(l.PowerPm, (long)l.PowerPm * f.Kit.Strength * f.StrengthPm / 1000 / l.MinStrength) : l.PowerPm;
        int lvRange = l.RangeMaxM * 1000;
        if (Walls is ILevelWalls lw0) { int bs = lw0.BaseMm(f.Id), bt = t != null ? lw0.BaseMm(t.Id) : bs; tz += bt; if (bs >= bt + AssaultRules.LevelMm / 2) { sigma = sigma * AssaultRules.HeightSigmaPm / 1000; lvRange = lvRange * AssaultRules.HeightRangePm / 1000; } }   // [P7-SG-C] aim at his floor; a level up is range and a steadier aim
        var p = Launch(f, tx, ty, tz, sigma, RngStreams.AimError, f.ShotSerial, l.V0Mps * 1000, lvRange);
        p.Ammo = f.AmmoDef.Id; p.V0Mmps = l.V0Mps * 1000; p.PowerPm = power; p.MassG = f.AmmoDef.MassG;
        p.AimPart = t != null ? f.Req.Part : -1;
        f.Loaded = false; f.ShotSerial++;
        f.Act = FAct.Recover; f.ActTicks = Math.Max(1, l.ReleaseTicks);
        f.Thrown++;
        Emit(FightEventKind.Shot, f.Id, t?.Id ?? -1, p.AimPart, sigma);
        if (f.Ammo <= 0) Emit(FightEventKind.OutOfAmmo, f.Id, -1);
    }

    public Fighter? NearestEnemy(Fighter f)
    {
        Fighter? best = null; long bd = long.MaxValue;
        foreach (var e in Fighters) { if (e.Team == f.Team || !e.Active) continue; long d = DistMm(f, e); if (d < bd) { bd = d; best = e; } }
        return best;
    }

    /// <summary>Builds a projectile from <paramref name="f"/> toward (tx, ty) at height tz with the aim error (two draws: lateral, vertical) and the auto-lob over friends.</summary>
    private Projectile Launch(Fighter f, int tx, int ty, int tz, int sigmaMdeg, uint stream, uint serial, int speedMmps, int rangeMm)
    {
        long dx = tx - f.X, dy = ty - f.Y;
        long d = Math.Max(1, FightMath.Len(dx, dy));
        // auto-lob: a friend in the way and the target at least 25 m off -> an arc over the crowd (sigma x1.5, range +15%)
        bool lob = false;
        if (d >= K.AutoLobMinM * 1000L && !Thrown(stream))
        {
            var (ux, uy) = FightMath.Norm(dx, dy);
            // a friend counts only when along < d x LobSkip and lat <= 2 capsules, so he lies within that and a little more of the shooter
            foreach (int oid in Near(0, f, Math.Max(d, d * K.LobSkipPm / 1000) + d / 50 + 2 * K.CapsuleRadiusMm + 1000))
            {
                var o = Fighters[oid];
                if (o.Id == f.Id || o.Team != f.Team || !o.Active) continue;
                long rx = o.X - f.X, ry = o.Y - f.Y;
                long along = (rx * ux + ry * uy) / FightMath.One, lat = Math.Abs((rx * uy - ry * ux) / FightMath.One);
                if (along > 500 && along < d * K.LobSkipPm / 1000 && lat <= K.CapsuleRadiusMm * 2) { lob = true; break; }
            }
        }
        if (lob) { sigmaMdeg = sigmaMdeg * K.LobSigmaPm / 1000; rangeMm = rangeMm * 1150 / 1000; }
        if (WeatherRow != null) { sigmaMdeg = (int)((long)sigmaMdeg * WeatherSigmaPm(d) / 1000 * WeatherVisionPm(d) / 1000); rangeMm = WeatherRange(rangeMm); }   // [exploration] hook: weather in combat (4.8); [P4 close-out] the vision cap and the bow's range
        int lat0 = AimOffsetMm(d, sigmaMdeg, DetRng.Range(Seed, Tick, (ulong)(f.Id + 1), stream, serial * 2, 0, 256));
        int vert = AimOffsetMm(d, sigmaMdeg, DetRng.Range(Seed, Tick, (ulong)(f.Id + 1), stream, serial * 2 + 1, 0, 256));
        var (nx, ny) = FightMath.Norm(dx, dy);
        long ax = tx + (long)(-ny) * lat0 / FightMath.One, ay = ty + (long)nx * lat0 / FightMath.One;
        var (dirX, dirY) = FightMath.Norm(ax - f.X, ay - f.Y);
        long d2 = Math.Max(1, FightMath.Len(ax - f.X, ay - f.Y));
        var p = new Projectile
        {
            Serial = (uint)Projectiles.Count + 1, Shooter = f.Id, Team = f.Team, X0 = f.X, Y0 = f.Y, Z0 = K.ShooterHeightMm + BaseOf(f), DirX = dirX, DirY = dirY,
            SlopePpm = (int)((tz + vert - K.ShooterHeightMm - BaseOf(f)) * 1_000_000L / d2), StepMm = Math.Max(1, speedMmps / 20), RangeMm = rangeMm,
            ApexMm = lob ? (int)(d * K.LobApexPm / 1000) : 0, PlanMm = lob ? (int)d : 0,
        };
        Projectiles.Add(p);
        return p;
    }

    private static bool Thrown(uint stream) => stream == RngStreams.ThrownAim;

    /// <summary>Fires an arrow of <paramref name="f"/>'s quiver at a point with a given aim sigma (tests, scripted volleys): the same path, lob and hit rules as a loosed shot.</summary>
    public Projectile LaunchAt(Fighter f, int tx, int ty, int tz, int sigmaMdeg)
    {
        var l = f.Launcher ?? throw new InvalidOperationException("no launcher");
        var p = Launch(f, tx, ty, tz, sigmaMdeg, RngStreams.AimError, f.ShotSerial++, l.V0Mps * 1000, l.RangeMaxM * 1000);
        p.Ammo = f.AmmoDef!.Id; p.V0Mmps = l.V0Mps * 1000; p.PowerPm = l.PowerPm; p.MassG = f.AmmoDef.MassG;
        return p;
    }

    // ------------------------------------------------------------------ thrown weapons (4.6)

    private static bool ThrowableShape(string id) => id is "javelin" or "dagger" or "hand_axe" or "spear";

    public bool CanThrow(Fighter f) => f.Throwables.Count > 0 || ThrowableShape(f.Weapon.Shape.Id) || f.Improvised != null;

    private void StartThrow(Fighter f, int x, int y)
    {
        f.Req = new AttackReq { Attack = 0, Target = -1, Part = -1, Gap = -1, Auto = true, Kind = BlowKind.Throw };
        f.ThrowTargetX = x; f.ThrowTargetY = y;
        f.Act = FAct.Windup; f.WindElapsed = 0; f.WindStart = Tick; f.WindNeeded = K.ThrowWindupTicks; f.Heavy = false; f.ImpactAt = -1;
        f.Serial++;
        Emit(FightEventKind.Windup, f.Id, -1, -1, f.WindNeeded, 1);
    }

    private void ReleaseThrow(Fighter f)
    {
        string shape, material; bool improvised = false; int massG;
        if (f.Throwables.Count > 0)
        {
            var st = f.Throwables[0];
            shape = st.Shape; material = st.Material; improvised = st.Improvised;
            massG = improvised ? (int)(Content.Improvised[shape].VolumeCm3 * T.Material(material).DensityKgM3 / 1000) : (int)(T.Weapon(shape, material).MassMg / 1000);
            if (st.Count <= 1) f.Throwables.RemoveAt(0); else f.Throwables[0] = st with { Count = st.Count - 1 };
        }
        else if (f.Improvised != null)
        {
            shape = f.Improvised.Id; material = f.Improvised.Material; improvised = true; massG = (int)(f.Weapon.MassMg / 1000);
            Disarm(f);
        }
        else
        {
            shape = f.Weapon.Shape.Id; material = f.Weapon.Head.Id; massG = (int)(f.Weapon.MassMg / 1000);
            Disarm(f);
        }
        int v = ThrowSpeedMmps(massG, (int)((long)f.Kit.Strength * f.StrengthPm / 1000));
        int sigma = AimSigmaMdeg(f.ThrowSkill, false, K.AimFullTicks, f.LastMoveMm * 20 > K.MoveCancelMmps, f.OneEye, K.ThrowSigmaMdeg);
        Fighter? aimed = null; long bd = long.MaxValue;
        foreach (int eid in Near(0, f.ThrowTargetX, f.ThrowTargetY, 1500))
        {
            var e = Fighters[eid];
            if (e.Team == f.Team || !e.Active) continue;
            long d = FightMath.Len(e.X - f.ThrowTargetX, e.Y - f.ThrowTargetY);
            if (d < 1500 && d < bd) { bd = d; aimed = e; }
        }
        int tx = aimed?.X ?? f.ThrowTargetX, ty = aimed?.Y ?? f.ThrowTargetY;
        var p = Launch(f, tx, ty, K.AimHeightMm, sigma, RngStreams.ThrownAim, f.Serial, v, ThrowRangeMm(v));
        p.Thrown = true; p.Improvised = improvised; p.Shape = shape; p.Material = material; p.V0Mmps = v; p.MassG = massG;
        long kgX100 = massG / 10;
        f.Stamina = Math.Max(0, f.Stamina - K.ThrowBaseX100 - (int)(K.SwingPerKgX100 * kgX100 / 100));
        f.Act = FAct.Recover; f.ActTicks = K.ThrowRecoverTicks;
        f.Thrown++;
        Emit(FightEventKind.Throw, f.Id, aimed?.Id ?? -1, -1, v);
    }

    // ------------------------------------------------------------------ flight (after the fighters, before the next tick's wounds)

    private void StepProjectiles()
    {
        for (int pi = 0; pi < Projectiles.Count; pi++)
        {
            var p = Projectiles[pi];
            if (p.Done) continue;
            long f0 = p.Flown, f1 = p.Flown + p.StepMm;
            long wallAt = Walls != null ? MissileWall(p, f0, f1) : -1;      // [heists] a bolt does not pass a wall
            if (wallAt >= 0) f1 = wallAt;
            Fighter? hit = null; long bestT = long.MaxValue; long hitLat = 0;
            // only the fighters near the segment this tick can be hit: the capsule test below needs t in [f0, f1] +- a capsule and |lat| <= a capsule (a little extra for the Q10 direction)
            var (mx, my) = p.At((f0 + f1) / 2);
            foreach (int eid in Near(0, mx, my, Math.Abs(f1 - f0) / 2 + 2 * K.CapsuleRadiusMm + Math.Max(f0, f1) / 50 + 1000))
            {
                var e = Fighters[eid];
                if (e.Act is FAct.Dead or FAct.Fled) continue;
                long rx = e.X - p.X0, ry = e.Y - p.Y0;
                long t = (rx * p.DirX + ry * p.DirY) / FightMath.One;
                if (t < f0 - K.CapsuleRadiusMm || t > f1 + K.CapsuleRadiusMm) continue;
                if (e.Id == p.Shooter && t < 800) continue;
                if (Mounts != null && t < 800 && Mounts.PairOf(e.Id) is { } pe && pe.Horse == e.Id && pe.Rider == p.Shooter) continue;       // [P7 CV-B] a rider's arrow leaves over his own horse
                long lat = (rx * p.DirY - ry * p.DirX) / FightMath.One;
                if (Math.Abs(lat) > K.CapsuleRadiusMm) continue;
                int z = p.Z(Math.Max(0, t)) - BaseOf(e);                    // [P7-SG-C] the height above his own floor
                int height = e.Act == FAct.Down ? 400 : K.CapsuleHeightMm;
                if (z < 0 || z > height) continue;
                if (t < bestT) { bestT = t; hit = e; hitLat = lat; }
            }
            if (hit != null) { p.Flown = Math.Max(f0, bestT); p.Done = true; ProjectileHit(p, hit, hitLat); continue; }
            if (wallAt >= 0)
            {
                p.Flown = wallAt; p.Done = true;
                var (wx, wy) = p.At(wallAt);
                Emit(FightEventKind.ProjectileMiss, p.Shooter, -1, -1, (int)(wallAt / 1000));
                Spent(p, wx, wy, hit: false);
                continue;
            }
            p.Flown = f1;
            if (p.Z(f1) < 0 || f1 >= p.RangeMm)
            {
                p.Done = true;
                var (gx, gy) = p.At(Math.Min(f1, p.RangeMm));
                Emit(FightEventKind.ProjectileMiss, p.Shooter, -1, -1, (int)(p.Flown / 1000));
                Spent(p, gx, gy, hit: false);
            }
        }
    }

    private void Spent(Projectile p, int x, int y, bool hit)
    {
        Carriers?.Landed(p.Ammo, x, y);                                                  // [P7-SH-D] a fire arrow on a ship's deck
        if (p.Thrown) { Ground.Add(new GroundItem(p.Shape, p.Material, x, y, !p.Improvised && DetRng.Range(Seed, (long)FightId, p.Serial, RngStreams.AmmoBreak, 1, 0, 1000) < 250)); return; }
        if (!Content.Ammo.TryGetValue(p.Ammo, out var a)) return;
        int roll = DetRng.Range(Seed, (long)FightId, p.Serial, RngStreams.AmmoBreak, 0, 0, 1000);
        if (roll >= (hit ? a.BreakHitPm : a.BreakMissPm)) SpentAmmo[p.Ammo] = SpentAmmo.GetValueOrDefault(p.Ammo) + 1;
    }

    /// <summary>The part a missile strikes, from its height at impact (melee 4.5 height table) and, for the arms, its offset from the body's centre line.</summary>
    public static int PartAtHeight(BodyPlanV2 plan, int zMm, long latMm, bool rightSide)
    {
        string side = rightSide ? "_r" : "_l";
        string id = zMm < 100 ? "foot" + side : zMm < 500 ? "lower_leg" + side : zMm < 900 ? "upper_leg" + side : zMm < 1100 ? "lower_body"
            : zMm < 1500 ? (Math.Abs(latMm) > 180 ? (zMm >= 1250 ? "upper_arm" : "lower_arm") + side : "upper_body") : zMm < 1600 ? "neck" : "head";
        int i = plan.IndexOf(id);
        return i >= 0 ? i : Math.Max(0, plan.IndexOf("upper_body"));
    }

    private void ProjectileHit(Projectile p, Fighter t, long lat)
    {
        var shooter = Fighters[p.Shooter];
        int z = p.Z(p.Flown) - BaseOf(t);                                          // [P7-SG-C] the height above his own floor
        // which side of the target the missile meets: the body's right is its facing turned a quarter clockwise (screen y down)
        var (hx, hy) = p.At(p.Flown);
        long ox = hx - t.X, oy = hy - t.Y;
        bool right = (ox * -t.FY + oy * t.FX) > 0;
        int part = p.AimPart >= 0 && p.AimPart < t.Plan.Parts.Length && Math.Abs(PartHeightMm(t.Plan.Parts[p.AimPart]) - z) < 120 ? p.AimPart : PartAtHeight(t.Plan, t.Act == FAct.Down ? 1250 : z, lat, right);
        var facing = FacingOf(t, p.X0, p.Y0);
        WeaponV2 w; int attack = 0; long m;
        if (p.Thrown)
        {
            w = p.Improvised ? Content.ImprovisedWeapon(Content.Improvised[p.Shape]) : T.Weapon(p.Shape, p.Material);
            // point first only if it is made to be thrown that way (javelin, dagger, hand axe); anything else lands blunt
            attack = -1;
            if (ThrowableShape(p.Shape)) for (int i = 0; i < w.Attacks.Length; i++) if (w.Attacks[i].Type != AttackType.Blunt) { attack = i; break; }
            if (attack < 0) for (int i = 0; i < w.Attacks.Length; i++) if (w.Attacks[i].Type == AttackType.Blunt) { attack = i; break; }
            if (attack < 0) attack = 0;
            m = ThrowMomentumMilli(p.MassG, p.V0Mmps);
        }
        else
        {
            w = Content.AmmoWeapon(Content.Ammo[p.Ammo]);
            m = LauncherMomentumMilli(p.MassG, p.V0Mmps, p.PowerPm, p.Flown);
        }
        var s = new StrikeV2
        {
            Strength = shooter.Kit.Strength, Skill = shooter.ArcherySkill, SizeCm3 = shooter.Kit.SizeCm3, SpeciesSizeCm3 = shooter.Plan.SizeCm3, MomentumPm = 1000,
            Weapon = w, Attack = attack, Style = AttackStyle.Normal, Plan = t.Plan, DefenderSizeCm3 = t.Kit.SizeCm3, DefenderSkill = t.Skill, Worn = t.Worn, WornCount = t.Worn.Length,
            MissingMask = t.MissingMask, Prone = false, TargetPart = part, TargetGap = -1, Facing = facing, Missile = true, ExtraMomentumMilli = m,
        };
        var key = new BlowKey(Seed, Tick, (ulong)(shooter.Id + 1) | 0x8000_0000UL, p.Serial);
        // a raised shield catches a missile from the front
        if (t.Act == FAct.Block && t.Shield != null && t.ShieldArm && t.ShieldPm > 0 && facing == Facing.Front
            && DefenceRoll(t, shooter.Id, p.Serial | 0x10000u, 3) < Math.Min(K.ShieldCoverCapPm, MountingCover(t, t.Shield.CoverPm + BracedNeighbours(t) * K.ShieldWallBonusPm)))
        {
            s.Block = new BlockInput { Kind = BlockKind.Shield, Shield = t.Shield, ShieldConditionPm = t.ShieldPm, BlockerSkill = t.ShieldSkill, BracedNeighbours = BracedNeighbours(t), ArmPart = ArmPart(t, 0) };
            Resolver.Resolve(s, key, _r);
        }
        else Resolver.ResolveImpact(s, key, _r);
        OnBlow?.Invoke(shooter, t, _r, s);
        WearArmour(shooter, t);
        if (_r.Blocked)
        {
            WearShield(t, _r.Block.ShieldLossPm);
            t.BlockedBy++;
            Emit(FightEventKind.Blocked, t.Id, shooter.Id, part, 1, (int)(_r.Block.ExcessMilli / 1000));
            if (_r.WoundCount > 0) Land(shooter, t, facing, _r);
        }
        else if (_r.WoundCount == 0) Emit(FightEventKind.Stopped, shooter.Id, t.Id, part, (int)(m / 1000));
        else
        {
            Emit(FightEventKind.ProjectileHit, shooter.Id, t.Id, part, (int)(m / 1000), p.Thrown ? 1 : 0);
            Land(shooter, t, facing, _r);
        }
        TellArmourHarm();
        Spent(p, t.X, t.Y, hit: true);
    }
}
