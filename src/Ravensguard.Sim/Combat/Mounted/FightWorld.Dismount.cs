using Ravensguard.Content;
using Ravensguard.Sim.Combat.Mounted;
using Ravensguard.Sim.Combat.Resolver2;
using Ravensguard.Sim.Core;

namespace Ravensguard.Sim.Combat.Melee2;

/// <summary>
/// [P7 CV-B] The rest of mounted war (cavalry-and-mounted-war 4.3, 4.5, 4.6, 4.7): a man getting down from his horse and up again (the pair's <c>Phase</c>), the horse-holders who lead the horses
/// to the rear, a horse's kick and bite, a fallen horse as an obstacle, the horse archer's wide aim and a mounting man's halved guard. Every blow goes through the one resolver, the draws come from
/// the registered streams 0x790 and 0x791, and nothing here reads the camera. A fight with no horses never gets here (<see cref="Mounts"/> is null).
/// </summary>
public sealed partial class FightWorld
{
    // ------------------------------------------------------------------ getting down and up

    /// <summary>The rider starts to get down (2 s: <c>dismount_ticks</c>); he stands while he does. False when he is not in the saddle, is busy with another change or is out of the fight.</summary>
    public bool BeginDismount(MountedFighter m, bool solo = false)
    {
        var L = Mounts!; var r = Fighters[m.Rider]; var h = Fighters[m.Horse];
        if (!m.Mounted || m.Phase != MountedFighter.PhNone || !r.Active || !h.Active) return false;
        m.Phase = MountedFighter.PhDismounting; m.PhaseAt = Tick + L.Cv.DismountTicks; m.Solo = solo;
        return true;
    }

    /// <summary>The man on foot by his own choice walks to his horse and mounts (4 s, 2 s at Riding 60 or more). False when his horse is gone.</summary>
    public bool BeginMount(MountedFighter m)
    {
        var r = Fighters[m.Rider]; var h = Fighters[m.Horse];
        if (m.Mounted || m.Phase != MountedFighter.PhFoot || !r.Active || !h.Active) return false;
        m.Phase = MountedFighter.PhFetch; m.Holding = false;
        return true;
    }

    /// <summary>E: the hero gets down, or, on foot beside his horse, mounts.</summary>
    internal void ToggleMount(Fighter f)
    {
        var m = Mounts!.PairOf(f.Id);
        if (m == null || m.Rider != f.Id) return;
        if (m.Mounted) BeginDismount(m);
        else if (m.Phase == MountedFighter.PhFoot) BeginMount(m);
    }

    /// <summary>The pair's change of state for this tick (before the commands run, after the horse's nerves): the transitions of getting down, walking to the horse and mounting, and a holder's duty.</summary>
    private void PhaseStep(MountedFighter m, Fighter r, Fighter h)
    {
        var Cv = Mounts!.Cv;
        if (m.Holding && m.Phase == MountedFighter.PhFoot)
        {
            if (!r.Active || AnyEnemyWithin(r, Cv.HolderReleaseMm)) ReleaseHorses(m);
            else if (m.Remount && !HoldsOthers(m)) { m.Holding = false; m.Remount = false; BeginMount(m); }
            return;
        }
        if (!r.Active) return;
        switch (m.Phase)
        {
            case MountedFighter.PhDismounting:
                if (!h.Active) { m.Phase = MountedFighter.PhNone; m.Solo = false; }
                else if (Tick >= m.PhaseAt) FinishDismount(m, r, h);
                break;
            case MountedFighter.PhFoot:
                if (m.Solo && r.Launcher is { Holds: true } && r.Loaded && h.Active && !AnyEnemyWithin(r, 12_000)) BeginMount(m);       // the crossbowman is loaded: up again
                break;
            case MountedFighter.PhFetch:
                if (!h.Active) { m.Phase = MountedFighter.PhFoot; m.Solo = false; }
                else if (DistMm(r, h) <= Cv.FetchWithinMm) { m.Phase = MountedFighter.PhMounting; m.PhaseAt = Tick + (m.Riding >= Cv.MountSkilledRiding ? Cv.MountSkilledTicks : Cv.MountTicks); }
                break;
            case MountedFighter.PhMounting:
                if (!h.Active) { m.Phase = MountedFighter.PhFoot; m.Solo = false; }
                else if (Tick >= m.PhaseAt) FinishMount(m, r, h);
                break;
        }
    }

    private void FinishDismount(MountedFighter m, Fighter r, Fighter h)
    {
        var L = Mounts!;
        m.Mounted = false; m.Couched = false; m.Speed = 0; m.VX = m.VY = 0; m.Phase = MountedFighter.PhFoot;
        if (!IsLance(r)) r.ReachMm = Math.Max(500, r.ReachMm - L.D.MountedReachMm);
        h.X = r.X; h.Y = r.Y;
        Emit(FightEventKind.Dismounted, r.Id, h.Id, -1, m.Solo ? 1 : 0);
    }

    private void FinishMount(MountedFighter m, Fighter r, Fighter h)
    {
        var L = Mounts!;
        m.Mounted = true; m.Phase = MountedFighter.PhNone; m.Solo = false; m.Remount = false; m.Holding = false; m.HeldBy = -1; m.HeldSlot = 0;
        m.Speed = 0; m.VX = m.VY = 0; m.LeftAt = -1;
        r.X = h.X; r.Y = h.Y;
        if (r.FX != 0 || r.FY != 0) { m.HX = r.FX; m.HY = r.FY; }
        for (int i = 0; i < 4; i++) { m.HistX[i] = r.X; m.HistY[i] = r.Y; }
        h.FX = m.HX; h.FY = m.HY;
        if (!IsLance(r)) r.ReachMm += L.D.MountedReachMm;
        Emit(FightEventKind.Remounted, r.Id, h.Id, -1, 0);
    }

    /// <summary>What a man in a change of state does this tick (after the brain has thought): he stands to get down or up, walks to his horse, or, a holder, to his post; none of them strikes.</summary>
    private void PhaseCommands(MountedFighter m, Fighter r)
    {
        var c = r.Cmd;
        switch (m.Phase)
        {
            case MountedFighter.PhDismounting:
            case MountedFighter.PhMounting:
                Hush(c); break;
            case MountedFighter.PhFetch:
                {
                    var h = Fighters[m.Horse];
                    Hush(c);
                    var (ux, uy) = FightMath.Norm(h.X - r.X, h.Y - r.Y);
                    c.MoveX = ux; c.MoveY = uy; if (ux != 0 || uy != 0) { c.FaceX = ux; c.FaceY = uy; }
                    break;
                }
            case MountedFighter.PhFoot:
                if (m.Holding)
                {
                    Hush(c);
                    long dx = m.PostX - r.X, dy = m.PostY - r.Y;
                    if (FightMath.Len(dx, dy) > 1500) { var (ux, uy) = FightMath.Norm(dx, dy); c.MoveX = ux; c.MoveY = uy; c.FaceX = ux; c.FaceY = uy; }
                }
                else if (m.Solo)
                {
                    c.Start = null; c.Throw = null; c.MoveX = c.MoveY = 0; c.Sprint = false;                       // the crossbowman stands to span his bow
                    if (!r.Loaded && r.Act == FAct.Idle && r.Ammo > 0) c.Reload = true;
                }
                break;
        }
    }

    private static void Hush(FightCommand c) { c.Start = null; c.Feint = false; c.Throw = null; c.Reload = false; c.MoveX = c.MoveY = 0; c.Sprint = false; }

    /// <summary>Whether a holder still leads any horse but his own whose rider is on his feet and means to ride.</summary>
    private bool HoldsOthers(MountedFighter holder)
    {
        foreach (var p in Mounts!.Pairs)
            if (p != holder && p.HeldBy == holder.Rider && !p.Mounted && Fighters[p.Rider].Active) return true;
        return false;
    }

    // ------------------------------------------------------------------ the horse-holders

    /// <summary>Makes <paramref name="holder"/> a horse-holder who stands at the given post and leads the horses given to him (<see cref="LeadHorse"/>).</summary>
    public void MakeHolder(MountedFighter holder, int postX, int postY)
    {
        holder.Holding = true; holder.PostX = postX; holder.PostY = postY;
    }

    /// <summary>The horse of <paramref name="held"/> is led by <paramref name="holder"/>, at place <paramref name="slot"/> (0 to 3) at his side.</summary>
    public void LeadHorse(MountedFighter held, MountedFighter holder, int slot) { held.HeldBy = holder.Rider; held.HeldSlot = slot; }

    /// <summary>A holder lets go (an enemy within 10 m, or he is down): every horse he leads bolts away from the nearest enemy and he fights as any man on foot.</summary>
    private void ReleaseHorses(MountedFighter holder)
    {
        var L = Mounts!;
        var hf = Fighters[holder.Rider];
        var e = NearestEnemyOf(hf);
        var (ax, ay) = e != null ? FightMath.Norm(hf.X - e.X, hf.Y - e.Y) : (-hf.FX, -hf.FY);
        if (ax == 0 && ay == 0) (ax, ay) = (1024, 0);
        foreach (var p in L.Pairs)
        {
            if (p.HeldBy != holder.Rider) continue;
            p.HeldBy = -1; p.HeldSlot = 0;
            var h = Fighters[p.Horse];
            if (p.Mounted || !h.Active) continue;
            h.Act = FAct.Fled; h.EndedAt = Tick;
            p.LeftAt = Tick; p.RunX = ax; p.RunY = ay;
            Emit(FightEventKind.HorseLetGo, hf.Id, h.Id);
        }
        holder.Holding = false; holder.Remount = false;
    }

    /// <summary>Led horses follow their holder at a walk, each at its place at his side (after the movement, before the grid is rebuilt).</summary>
    private void LeadHorses()
    {
        var L = Mounts!; var Cv = L.Cv;
        foreach (var m in L.Pairs)
        {
            if (m.HeldBy < 0 || m.Mounted) continue;
            var h = Fighters[m.Horse];
            if (!h.Active) { m.HeldBy = -1; continue; }
            var hd = Fighters[m.HeldBy];
            int side = (2 * m.HeldSlot - 3) * Cv.HorseSpacingMm / 2;
            int tx = hd.X + (int)((long)-hd.FY * side >> 10) - (int)((long)hd.FX * 900 >> 10), ty = hd.Y + (int)((long)hd.FX * side >> 10) - (int)((long)hd.FY * 900 >> 10);
            long dx = tx - h.X, dy = ty - h.Y, d = FightMath.Len(dx, dy);
            int step = m.Gaits.WalkMmps / 20;
            if (d <= step) { h.X = tx; h.Y = ty; }
            else { h.X += (int)(dx * step / d); h.Y += (int)(dy * step / d); }
            h.FX = hd.FX; h.FY = hd.FY;
        }
    }

    // ------------------------------------------------------------------ kicks and bites (4.6)

    /// <summary>
    /// Whether horses kick and bite: null (the default) is on in every fight (D2, P8-WT-3); a test or a setup may force it either way.
    /// </summary>
    public bool? Kicks;
    private bool KicksOn => Kicks ?? true;

    /// <summary>
    /// A war horse (trained 900 or more) or a panicked one (600 or more), under a rider or led, kicks a foe within 1.2 m in its rear 90 degrees every 3 s at most: a blunt blow of area 60 cm2 and
    /// <c>60 x kg</c> milli through the resolver, at the lower body (60%) or the upper body (stream 0x791). Only a horse at a walk or less kicks or bites (a ridden one; a led one stands): on mud, which caps a gallop to
    /// a 5 m/s trot, 99% of the kicks the ungated rule gave came at that trot, riders won 92% of fights against 25% without kicks. A war horse also bites forward at a man pressing its rider, area 20 and <c>8 x kg</c> milli, each 5 s.
    /// </summary>
    private void HorseKicks(MountedFighter m)
    {
        if (!KicksOn) return;
        var L = Mounts!; var Cv = L.Cv;
        var h = Fighters[m.Horse];
        if (!h.Active || !(m.Mounted || m.HeldBy >= 0 || m.Phase != MountedFighter.PhNone)) return;
        bool war = m.TrainedPm >= Cv.KickTrainedPm;
        if (!war && m.PanicPm < Cv.KickPanicPm) return;
        if ((Tick + h.Id) % 4 != 0) return;
        if (m.Mounted && m.Speed > m.Gaits.WalkMmps) return;                                       // P8-WT-3b: a horse above a walk has no hind foot planted to kick from nor a head to turn: in mud it is held at the 5 m/s cap and rides through the foot all fight
        bool kickReady = Tick - m.KickAt >= Cv.KickEveryTicks, biteReady = war && m.Mounted && Tick - m.BiteAt >= Cv.BiteEveryTicks;
        if (!kickReady && !biteReady) return;
        Fighter? back = null, front = null; long bd = long.MaxValue, fd = long.MaxValue;
        int hx = h.FX, hy = h.FY;
        foreach (int id in NearUnsorted(3, h, Cv.KickReachMm))
        {
            var t = Fighters[id];
            if (t.Team == h.Team || !t.Active || L.IsHorse(t.Id)) continue;
            long d = DistMm(h, t);
            if (d > Cv.KickReachMm) continue;
            var (ux, uy) = FightMath.Norm(t.X - h.X, t.Y - h.Y);
            int dot = FightMath.Dot(hx, hy, ux, uy);
            if (kickReady && dot <= Cv.KickArcQ10 && (d < bd || d == bd && t.Id < back!.Id)) { bd = d; back = t; }
            if (biteReady && dot >= -Cv.KickArcQ10 && t.Brain.Target == m.Rider && (d < fd || d == fd && t.Id < front!.Id)) { fd = d; front = t; }
        }
        if (back != null) { m.KickAt = Tick; HorseBlow(m, h, back, L.Tuning.Kick, (long)Cv.KickMilliPerKg * m.Gaits.MassKg, true, FightEventKind.Kicked); }
        if (front != null) { m.BiteAt = Tick; HorseBlow(m, h, front, L.Tuning.Bite, (long)Cv.BiteMilliPerKg * m.Gaits.MassKg, false, FightEventKind.Bitten); }
    }

    private void HorseBlow(MountedFighter m, Fighter h, Fighter t, WeaponV2 weapon, long milli, bool kick, FightEventKind kind)
    {
        var L = Mounts!; var Cv = L.Cv;
        // the part: a kick takes the lower body 60% and the upper body 40%; a bite the upper body 60% and the lower arm 40% (stream 0x791, keyed on the horse and the tick)
        int roll = DetRng.Range(Seed, Tick, (ulong)(h.Id + 1), RngStreams.KickAndBite, kick ? 0u : 1u, 0, 1000);
        string id = kick ? roll < Cv.KickLowerPm ? "lower_body" : "upper_body" : roll < Cv.KickLowerPm ? "upper_body" : (roll & 1) == 0 ? "lower_arm_l" : "lower_arm_r";
        int part = t.Plan.IndexOf(id);
        if (part < 0 || (t.MissingMask & (1UL << part)) != 0) part = Math.Max(0, t.Plan.IndexOf("upper_body"));
        var facing = FacingOf(t, h.X, h.Y);
        var s = new StrikeV2
        {
            Strength = 0, Skill = Cv.KickSkill, SizeCm3 = h.Kit.SizeCm3, SpeciesSizeCm3 = h.Plan.SizeCm3, MomentumPm = 1000, Weapon = weapon, Attack = 0, Style = AttackStyle.Normal,
            Plan = t.Plan, DefenderSizeCm3 = t.Kit.SizeCm3, DefenderSkill = t.Skill, Worn = t.Worn, WornCount = t.Worn.Length, MissingMask = t.MissingMask,
            Prone = false, TargetPart = part, TargetGap = -1, Facing = facing, Missile = true, ExtraMomentumMilli = milli,
        };
        h.Serial++;
        Resolver.Resolve(s, KeyOf(h, h.Serial), _r);
        OnBlow?.Invoke(h, t, _r, s);
        Emit(kind, h.Id, t.Id, _r.Part, (int)(milli / 1000), _r.Hit ? 1 : 0);
        if (_r.WoundCount > 0) Land(h, t, facing, _r);
    }

    // ------------------------------------------------------------------ fallen horses (4.3)

    /// <summary>The horses that are down or dead this tick (the obstacles), in pair order.</summary>
    private void FindFallen()
    {
        var L = Mounts!;
        L.Fallen.Clear();
        foreach (var m in L.Pairs) { var h = Fighters[m.Horse]; if (h.Act is FAct.Down or FAct.Dead) L.Fallen.Add(m.Horse); }
    }

    private bool InFallenRect(Fighter f, int x, int y)
    {
        var Cv = Mounts!.Cv;
        long dx = x - f.X, dy = y - f.Y;
        long along = (dx * f.FX + dy * f.FY) >> 10, across = (dx * -f.FY + dy * f.FX) >> 10;
        return Math.Abs(along) <= Cv.FallenHalfLenMm + Cv.FallenPadMm && Math.Abs(across) <= Cv.FallenHalfWidMm + Cv.FallenPadMm;
    }

    /// <summary>
    /// A mount about to step onto a fallen horse's 2 m x 1 m (4.3): at a gallop it meets the obstacle once: a horse trained 900 with a rider of Riding 40 jumps it, any other falls with 300 permille and the rest
    /// jump it (stream 0x790, keyed on the horse and the obstacle); below a gallop it cannot go through and slides along the body.
    /// </summary>
    private void FallenAhead(MountedFighter m, Fighter r, Fighter h, ref int nx, ref int ny, int mx, int my)
    {
        var L = Mounts!; var Cv = L.Cv; var g = m.Gaits;
        foreach (int id in L.Fallen)
        {
            if (id == m.Horse) continue;
            var f = Fighters[id];
            if (Math.Abs(f.X - nx) > 3000 || Math.Abs(f.Y - ny) > 3000) { if (m.ObstacleId == id) m.ObstacleId = -1; continue; }
            if (!InFallenRect(f, nx, ny)) { if (m.ObstacleId == id && !InFallenRect(f, r.X, r.Y)) m.ObstacleId = -1; continue; }
            if (m.ObstacleId == id) continue;                                                         // already over it
            if (m.Speed > g.TrotMmps + 300)
            {
                m.ObstacleId = id;
                bool jumps = m.TrainedPm >= Cv.JumpTrainedPm && m.Riding >= Cv.JumpRiding
                    || DetRng.Range(Seed, Tick, (ulong)(h.Id + 1), RngStreams.HorseFall, (uint)id, 0, 1000) >= Cv.FallenHitPm;
                if (jumps) Emit(FightEventKind.Jumped, h.Id, id, -1, m.TrainedPm, m.Speed);
                else { Emit(FightEventKind.HorseFell, h.Id, r.Id, -1, 3); _slipped.Add(m.Index); }
                continue;
            }
            // the way is shut: slide along the body (the step's part along the surface), else stand
            var (ox, oy) = FightMath.Norm(r.X - f.X, r.Y - f.Y);
            long tx = -oy, ty = ox, along = (mx * tx + my * ty) >> 10;
            int sx = r.X + (int)(tx * along >> 10), sy = r.Y + (int)(ty * along >> 10);
            if (along != 0 && !InFallenRect(f, sx, sy)) { nx = sx; ny = sy; } else { nx = r.X; ny = r.Y; }
            m.Speed = Math.Min(m.Speed, g.WalkMmps);
        }
    }

    // ------------------------------------------------------------------ the horse archer and the man mounting (4.5, 4.7)

    /// <summary>
    /// A rider's shot is wider by the mounted aim penalty (<see cref="ChargeMath.AimPoints"/>: -10 points at a trot, less by Riding / 150; none at a walk), each point 3% of the aim cone: the same missile
    /// path and the same resolver, only the cone is the rider's own.
    /// </summary>
    public int MountedSigma(Fighter f, int sigmaMdeg)
    {
        var m = Riding(f);
        if (m == null) return sigmaMdeg;
        var D = Mounts!.D;
        int points = -ChargeMath.AimPoints(m.Speed, false, m.Riding, D.GallopChargeMinMmps, D.TrampleMinMmps, D.AimChargePoints, D.AimTrotPoints, D.AimGallopSwingPoints);
        return (int)((long)sigmaMdeg * (1000 + (long)Mounts.Cv.ArcherSigmaPerPointPm * points) / 1000);
    }

    /// <summary>A man mounting guards at half cover (shield and weapon).</summary>
    internal int MountingCover(Fighter t, int coverPm) => Mounts != null && Mounts.PairOf(t.Id) is { Phase: MountedFighter.PhMounting } p && p.Rider == t.Id ? coverPm * Mounts.Cv.MountingCoverPm / 1000 : coverPm;

    /// <summary>The nearest active enemy of <paramref name="f"/> that is not a horse, within <paramref name="maxMm"/> (the lowest id among equals); null when none.</summary>
    public Fighter? NearestNonHorseEnemy(Fighter f, long maxMm)
    {
        long r = 30_000;
        while (true)
        {
            bool all = !UseGrid || r >= maxMm;
            var ids = all ? Near(3, f.X, f.Y, maxMm) : Near(3, f.X, f.Y, r);
            Fighter? best = null; long bd = long.MaxValue;
            foreach (int id in ids)
            {
                var e = Fighters[id];
                if (e.Team == f.Team || !e.Active || Mounts!.IsHorse(id)) continue;
                long d = DistMm(f, e);
                if (d > maxMm) continue;
                if (d < bd) { bd = d; best = e; }
            }
            if (all || best != null && bd <= r) return best;
            r *= 2;
        }
    }
}
