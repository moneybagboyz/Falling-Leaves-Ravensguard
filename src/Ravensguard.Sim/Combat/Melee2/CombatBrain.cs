using Ravensguard.Content;
using Ravensguard.Sim.Combat.Resolver2;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Combat.Watch;

namespace Ravensguard.Sim.Combat.Melee2;

/// <summary>
/// The combat AI (melee-and-ranged 4.7): one brain for NPCs, enemies, party members and the hero in watch mode (plan §6: the hero on watch mode uses exactly the NPC combat
/// AI). It writes only <see cref="Fighter.Cmd"/>. Reactions to an incoming blow are a trigger, decided once per blow inside the last 0.2 s and carried out on time whatever
/// the level of detail; offence, target and footwork are thought at 20 Hz near the player and 5 Hz beyond (melee 5), which changes only how often, never what (ML-16).
/// The stance (<see cref="CombatStanceDef"/>) biases the same rules. Randomness: 0x430 reactions, 0x431 target, 0x432 noise, keyed (FightTick, fighter).
/// </summary>
public static class CombatBrain
{
    public const byte ReactNone = 0, ReactParry = 1, ReactDodge = 2, ReactBlock = 3, ReactTake = 4;

    public static bool IsNear(FightWorld w, Fighter f)
    {
        if (w.ForceNear is { } n) return n;
        if (f.Id == w.LodFighter) return true;                       // P4-F: the hero (watched or not) is always thought at 20 Hz
        if (w.LodCentre is not { } c) return true;
        return FightMath.Len(f.X - c.X, f.Y - c.Y) <= w.K.AiNearRadiusMm;
    }

    private static int Noise(FightWorld w, Fighter f, uint draw, int max)
    {
        w.AiDrawLog?.Invoke(f.Id, RngStreams.StanceNoise);           // P4-F: the stream draw log of test WM-3 (null in play)
        return DetRng.Range(w.Seed, w.Tick + 1, (ulong)(f.Id + 1), RngStreams.StanceNoise, draw, 0, max);
    }

    public static void Think(FightWorld w, Fighter f)
    {
        var c = f.Cmd;
        if (!f.Active)
        {
            c.ClearAll();
            if (f.Act == FAct.Down && f.Crawling) Crawl(w, f);
            return;
        }
        int tick = w.Tick + 1;             // the step this decision is for
        React(w, f, tick);
        bool near = IsNear(w, f);
        if (tick <= f.TreatBusyUntil) { c.MoveX = c.MoveY = 0; c.Sprint = false; c.Start = null; return; }   // P4-F: kneeling to press or bandage (watch-mode 4.3)
        if (!near && (tick + f.Id) % Math.Max(1, w.K.AiFarEveryTicks) != 0) { Carry(w, f, tick); return; }
        Decide(w, f, tick);
        if (f.FmRow >= 0 && w.Formations != null) w.Formations.Leash(w, f, tick);          // [P7 BB-B] a fighting member keeps to his leash (4.4)
    }

    /// <summary>Between far thinks: the reaction plan still runs (it is a trigger) and the held move and guard stay as they were.</summary>
    private static void Carry(FightWorld w, Fighter f, int tick)
    {
        var c = f.Cmd;
        if (f.Brain.React is ReactBlock or ReactParry && tick <= f.Brain.HoldBlockUntil) return;
        if (tick > f.Brain.HoldBlockUntil && f.Brain.React != ReactNone) { c.Block = GuardWanted(w, f); f.Brain.React = ReactNone; }
    }

    private static void Crawl(FightWorld w, Fighter f)
    {
        var e = w.NearestEnemyOf(f);
        if (e == null) return;
        var (ux, uy) = FightMath.Norm(f.X - e.X, f.Y - e.Y);
        f.Cmd.MoveX = ux; f.Cmd.MoveY = uy;
    }

    // ------------------------------------------------------------------ 1. reactions (a trigger: every tick)

    /// <summary>The impact tick of a foe's blow (an AI swing releases itself; a player's is estimated at the end of its wind-up).</summary>
    public static int ImpactTick(Fighter e) => e.Req.Auto && e.ImpactAt >= 0 ? e.ImpactAt : e.WindStart + e.WindNeeded - 1;

    private static void React(FightWorld w, Fighter f, int tick)
    {
        var c = f.Cmd; var K = w.K; ref var m = ref f.Brain;
        // follow the plan already made
        if (m.React != ReactNone)
        {
            if (m.React == ReactParry && tick == m.ReactAt) c.Block = true;
            else if (m.React == ReactParry && tick < m.ReactAt) c.Block = false;
            else if (m.React == ReactDodge && tick == m.ReactAt && f.Act is FAct.Idle or FAct.Block)
            {
                var foe = w.Fighters[m.ReactSerialFoe];
                var (ux, uy) = FightMath.Norm(f.X - foe.X, f.Y - foe.Y);
                int side = (m.ReactSerial & 1) == 0 ? 1 : -1;
                c.DodgeX = -uy * side + ux / 2; c.DodgeY = ux * side + uy / 2;
                c.Block = false;
            }
            if (tick > m.HoldBlockUntil) { m.React = ReactNone; c.Block = GuardWanted(w, f); }
        }
        if (f.Act is not (FAct.Idle or FAct.Block or FAct.Recover or FAct.Stagger or FAct.Aiming or FAct.Reloading)) return;
        // the most imminent blow aimed at this fighter
        Fighter? foeNext = null; int best = int.MaxValue;
        foreach (int eid in w.Near(0, f, w.Grid.MaxReachMm + K.ReachSlackMm + 600))        // nobody reaches further than the longest weapon in the fight
        {
            var e = w.Fighters[eid];
            if (e.Team == f.Team || e.Act != FAct.Windup || e.Req.Kind != BlowKind.Melee) continue;
            if (e.Req.Target >= 0 && e.Req.Target != f.Id) continue;
            if (w.DistMm(e, f) > w.ReachAgainst(e, f) + K.ReachSlackMm + 600) continue;
            int at = ImpactTick(e);
            if (at < tick || at - tick > K.ReactLeadTicks) continue;
            if (at < best) { best = at; foeNext = e; }
        }
        if (foeNext == null) return;
        if (m.ReactSerialFoe == foeNext.Id && m.ReactSerial == (int)foeNext.Serial) return;     // decided already
        m.ReactSerialFoe = foeNext.Id; m.ReactSerial = (int)foeNext.Serial;
        int toImpact = best - tick;
        var st = f.Stance;
        w.AiDrawLog?.Invoke(f.Id, RngStreams.MeleeDefence);
        int roll = DetRng.Range(w.Seed, tick, FightWorld.DefKey(f.Id, foeNext.Id), RngStreams.MeleeDefence, foeNext.Serial * 8u + 6, 0, 1000);
        int pParry = Math.Min(K.ReactParryCapPm, K.ReactParryBasePm + f.Skill * 1000 / K.ReactParrySkillDivisor) + (st.ParryPoints + st.ReactPoints) * 10;
        // a fighter still recovering from its own blow can act once the recover ends: from that tick on (it presses or dodges then, if that is in time)
        int freeAt = f.Act is FAct.Recover or FAct.Stagger ? tick + f.ActTicks : tick;
        bool free = freeAt <= best;
        bool canDodge = free && freeAt >= f.DodgeReady && f.Stamina >= K.DodgeX100 && f.CanStand && f.WalkPm > 0 && f.Act is FAct.Idle or FAct.Block or FAct.Recover or FAct.Stagger;
        int pDodge = canDodge ? Math.Max(0, K.ReactDodgePm + st.ReactPoints * 10 / 3) : 0;
        // a dodge on cooldown is a block instead (the share of "take it" stays what the stance leaves)
        int pBlock = Math.Max(0, K.ReactBlockPm + st.ReactPoints * 10 / 3) + (canDodge ? 0 : K.ReactDodgePm);
        bool shieldFront = f.Shield != null && f.ShieldArm && w.FacingOf(f, foeNext.X, foeNext.Y) == Facing.Front;
        bool canParry = free && f.WeaponArm && f.Launcher == null && f.Act is FAct.Idle or FAct.Block or FAct.Recover or FAct.Stagger;
        byte r;
        if (f.Stamina < K.AiGuardBelowX100) r = ReactBlock;
        else if (canParry && roll < pParry) r = ReactParry;
        else if (shieldFront) r = ReactBlock;                                           // always block with a shield on a front-arc blow
        else if (roll < pParry + pDodge) r = ReactDodge;
        else if (roll < pParry + pDodge + pBlock) r = ReactBlock;
        else r = ReactTake;
        if (r == ReactParry && !canParry) r = ReactBlock;
        if (r == ReactDodge && !canDodge) r = ReactBlock;
        m.React = r;
        m.HoldBlockUntil = best + 2;
        int lead = Math.Min(K.ParryWindowTicks - 1, Math.Max(0, toImpact - (f.Act == FAct.Block ? 1 : 0)));
        w.AiDrawLog?.Invoke(f.Id, RngStreams.MeleeDefence);
        int jitter = DetRng.Range(w.Seed, tick, (ulong)(f.Id + 1), RngStreams.MeleeDefence, foeNext.Serial * 8u + 7, 0, lead + 1);
        switch (r)
        {
            case ReactParry:
                m.ReactAt = Math.Max(freeAt, best - jitter);
                if (m.ReactAt <= tick) { m.ReactAt = tick; c.Block = f.Act != FAct.Block; if (f.Act == FAct.Block) { m.ReactAt = tick + 1; c.Block = false; } }
                else c.Block = false;
                break;
            case ReactDodge:
                m.ReactAt = Math.Max(freeAt, best - 1 - Math.Min(jitter, 2));
                if (m.ReactAt > best) m.ReactAt = best;
                if (m.ReactAt == tick) React(w, f, tick);         // carry it out now
                break;
            case ReactBlock:
                c.Block = true;
                break;
        }
    }

    // ------------------------------------------------------------------ 2-4. offence, target, footwork

    private static bool GuardWanted(FightWorld w, Fighter f) =>
        f.Stamina < w.K.AiGuardBelowX100 || f.Stance.Posture == "defensive" && f.Shield != null;

    private static void Decide(FightWorld w, Fighter f, int tick)
    {
        var c = f.Cmd; var K = w.K; ref var m = ref f.Brain; var st = f.Stance;
        c.MoveX = c.MoveY = 0; c.Sprint = false;
        if (f.FmRow >= 0 && w.Formations != null) w.Formations.ClearPace(f);
        bool reacting = m.React is ReactBlock or ReactParry && tick <= m.HoldBlockUntil;
        if (!reacting && m.React == ReactNone) c.Block = GuardWanted(w, f);

        if (f.Routing) { Flee(w, f); return; }
        if (w.MoraleStepBack(f)) return;                // P4-D hook: a wavering fighter's pace back (morale 4.1)
        var orders = w.OrdersOf(f.Team);
        if (StanceBias.FallBack(w, f, orders)) return;      // P4-F: "fall back to me" (the order wheel, F4)
        if (f.FmRow >= 0 && w.Formations != null && w.Formations.Steer(w, f, tick)) return;   // [P7 BB-B] a formation member steers to his slot until an enemy is within 3 m (4.4)
        var t = PickTarget(w, f, tick);
        if (t == null) { c.Block = false; if (w.ProtectOf(f) is { } alone) StanceBias.FollowCharge(w, f, alone); return; }   // P4-F: no threat: keep by the charge
        m.Target = t.Id;
        long dx = t.X - f.X, dy = t.Y - f.Y;
        long dist = FightMath.Len(dx, dy);
        var (ux, uy) = FightMath.Norm(dx, dy);
        c.FaceX = ux; c.FaceY = uy;

        // ranged: keep 15 to 40 m, shoot when loaded, nock or span when safe ("hold fire" keeps the arrow nocked)
        if (f.Ranged && st.UseRanged && (f.Loaded || f.Ammo > 0))
        {
            if (orders.HoldFire || w.FormationHoldsFire(f)) { if (!f.Loaded && f.Act == FAct.Idle) c.Reload = true; return; }                   // P4-F: hold fire: nocked and waiting
            bool meleeFoe = !t.Ranged;
            // an archer backs off and nocks as he goes (close-out item 3: he used to run without nocking, outpace the swordsman for ever and end the fight walked away);
            // not a crossbowman, whose windlass needs him standing
            if (dist < K.ArcherMinMm && meleeFoe && dist > 2500 && f.Act is FAct.Idle or FAct.Reloading && !f.Loaded && f.Launcher is { Holds: false })
            {
                c.MoveX = -ux; c.MoveY = -uy; c.Sprint = dist < 8000;
                if (f.Act == FAct.Idle && f.Ammo > 0) c.Reload = true;
                return;
            }
            if (dist > K.ArcherMaxMm) { c.MoveX = ux; c.MoveY = uy; return; }
            if (!f.Loaded && f.Act == FAct.Idle) { c.Reload = true; return; }
            if (f.Loaded && f.Act is FAct.Idle or FAct.Block && w.FormationSafeShot(f, t)) c.Start = new AttackReq { Attack = 0, Target = t.Id, Part = -1, Gap = -1, Auto = true };
            return;
        }

        // a javelin or a carried throwable at middle range
        if (f.Throwables.Count > 0 && dist is > 5000 and < 20000 && f.Act == FAct.Idle && Noise(w, f, 11, 1000) < 300)
        {
            c.Throw = (t.X, t.Y);
            return;
        }

        int strikeD = w.ReachAgainst(f, t) + K.ReachSlackMm - 100;
        bool polearm = f.Family == "polearms";
        // close-out item 1: past a longer weapon's point (parried or slipped), press inside its reach; a crowded polearm steps back to use it again
        bool inside = tick <= f.InsideUntil && f.InsideOf == t.Id && !polearm;
        bool crowded = polearm && K.CrowdedStepBack != 0 && dist < K.PolearmTooCloseMm;
        int hold = polearm ? f.ReachMm - K.PolearmHoldSlackMm : inside ? Math.Min(strikeD - 250, K.InsideHoldMm) : strikeD - 250;
        bool exhausted = f.Stamina < K.AiGuardBelowX100;

        // feint against a guard: skill / 300 per swing
        if (f.Act == FAct.Windup && f.Req.Kind == BlowKind.Melee && m.FeintChecked != (int)f.Serial && f.WindElapsed * 2 < f.WindNeeded && tick >= f.FeintReady)
        {
            m.FeintChecked = (int)f.Serial;
            if (t.Act == FAct.Block && Noise(w, f, 12, 1000) < f.Skill * 1000 / K.FeintSkillDivisor) { c.Feint = true; m.AttackAfter = tick + 2; return; }
        }

        var guarded = w.ProtectOf(f);                         // P4-F: the protect stance stays by its charge (watch-mode 4.3)
        bool placed = guarded == null || w.DistMm(f, guarded) <= StanceBias.ProtectAttackMm && !guarded.Routing;
        // offence: opportunistic after the foe's blow or a parry, otherwise when the patience runs out
        if (placed && f.Act is FAct.Idle or FAct.Block && f.WeaponArm && f.Stamina > 0 && !exhausted && dist <= strikeD && tick >= m.AttackAfter && !(reacting && tick <= m.HoldBlockUntil && tick > f.RiposteUntil))
        {
            bool opening = t.Act is FAct.Recover or FAct.Stagger or FAct.Down || tick <= f.RiposteUntil;
            if (st.NoLethal && t.Act == FAct.Down) opening = false;
            if (m.PatienceSet <= 0)
            {
                int span = Math.Max(1, K.PatienceMaxTicks - K.PatienceMinTicks);
                int pat = K.PatienceMinTicks + Noise(w, f, 13, span);
                pat = (int)((long)pat * Math.Max(100, 1000 - st.AttackBiasPm) / 1000);
                m.PatienceSet = tick + pat;
            }
            bool go = opening ? Noise(w, f, 14, 1000) < K.AttackOnOpeningPm : !st.OpeningsOnly && tick >= m.PatienceSet && !crowded;
            if (go && !(t.Act == FAct.Down && st.NoLethal))
            {
                var req = new AttackReq { Target = t.Id, Part = PickPart(w, f, t, tick), Gap = -1, Auto = true };
                req.Attack = AutoAttack(w, f, t, req.Part);
                // the zone is under rigid plate: a skilled point aims through a gap the harness leaves (armpit, groin, face, back of the knee)
                if (req.Part >= 0 && f.Stance.CalledShots && ArmourOn(t, req.Part) == 2 && PointAttack(f) is int pa && pa >= 0 && GapAim(w, f, t) is { } g)
                { req.Part = g.Part; req.Gap = g.Gap; req.Attack = pa; }
                if (opening && st.HeavyOnOpening && f.Stamina >= 5000 && t.Act is FAct.Recover or FAct.Stagger) req.Style = SwingStyle.Heavy;
                c.Start = req; c.Block = false;
                m.AttackAfter = tick + 3; m.PatienceSet = 0;
                return;
            }
        }

        // footwork: close in, hold the polearm's distance, back off to breathe
        if (guarded != null && StanceBias.ProtectFootwork(w, f, guarded, t, dist, strikeD)) return;          // P4-F: by the guarded person's side, tired or not
        if (exhausted && dist < strikeD + 1500 && f.Act is FAct.Idle or FAct.Block) { c.MoveX = -ux; c.MoveY = -uy; c.Block = true; return; }
        if (StanceBias.FormationFootwork(w, f, t, dist, orders)) return;                                     // P4-F: the side's formation (4.4)
        if (dist > hold)
        {
            c.MoveX = ux; c.MoveY = uy;
            c.Sprint = dist > strikeD + 3000 && f.Stamina > 4000
                || inside && f.Stamina > 2000
                || K.CloseInSprint != 0 && t.ReachMm > f.ReachMm + 500 && t.Act is FAct.Recover or FAct.Stagger && f.Stamina > 3000
                || st.ChargeWithinM > 0 && dist <= st.ChargeWithinM * 1000L && f.Stamina > 3000;           // P4-F: the aggressive charge from 6 m
            StanceBias.ShieldToArchers(w, f, dist);                                                          // P4-F: defensive: the shield toward archers while closing
        }
        else if (polearm && dist < K.PolearmTooCloseMm) { c.MoveX = -ux; c.MoveY = -uy; }
    }

    private static Fighter? PickTarget(FightWorld w, Fighter f, int tick)
    {
        ref var m = ref f.Brain;
        long pursue = f.Stance.PursueM * 1000L;                 // routers are chased this far (aggressive 20 m, else 10 m)
        if (StanceBias.OrderedTarget(w, f, pursue) is { } ordered) { m.Target = ordered.Id; return ordered; }   // P4-F: the guarded person's nearest enemy, the side's focus
        Fighter? cur = m.Target >= 0 && m.Target < w.Fighters.Count ? w.Fighters[m.Target] : null;
        bool keep = cur != null && cur.Team != f.Team && cur.Active && !(cur.Routing && w.DistMm(f, cur) > pursue) && tick % 20 != f.Id % 20;
        if (keep) return cur;
        // the nearest enemy by distance (a foe aiming at us counts 2 m nearer under the "threat" priority), searched outward in doubling boxes: everything outside a box of r is
        // farther than r, so once the best is no farther than r - 2000 nothing outside can beat or tie it. Same candidates in the same ascending order as the full scan.
        Fighter? best = null; long bd = long.MaxValue;
        for (long r = 16_000; ; r *= 2)
        {
            bool all = !w.UseGrid || r >= (1L << 33);
            best = null; bd = long.MaxValue;
            foreach (int oid in all ? w.Near(0, f.X, f.Y, 1L << 41) : w.Near(0, f, r))
            {
                var o = w.Fighters[oid];
                if (o.Team == f.Team || !o.Active) continue;
                long d = w.DistMm(f, o);
                if (o.Routing && d > pursue) continue;
                if (f.Stance.TargetPriority == "threat" && o.Brain.Target == f.Id) d -= 2000;
                if (d < bd || (d == bd && best != null && o.Id < best.Id)) { bd = d; best = o; }
            }
            if (all || best != null && bd <= r - 2000 - 2) break;
        }
        return best;
    }

    private static void Flee(FightWorld w, Fighter f)
    {
        var c = f.Cmd;
        c.Block = false;
        long ax = 0, ay = 0;
        foreach (int eid in w.Near(0, f, 25_000))
        {
            var e = w.Fighters[eid];
            if (e.Team == f.Team || !e.Active) continue;
            long dx = f.X - e.X, dy = f.Y - e.Y, d = Math.Max(1, FightMath.Len(dx, dy));
            if (d < 25_000) { ax += dx * 1_000_000 / (d * d / 1000 + 1); ay += dy * 1_000_000 / (d * d / 1000 + 1); }
        }
        if (ax == 0 && ay == 0) { ax = -f.FX; ay = -f.FY; }
        var (ux, uy) = FightMath.Norm(ax, ay);
        if (w.Formations?.EntryDir(f) is { } en)             // [P7 BB-B] the routed run toward their side's entry point (4.6)
        {
            if (ax == 0 && ay == 0) (ux, uy) = en; else { var (mx, my) = FightMath.Norm(ux + en.X, uy + en.Y); (ux, uy) = mx == 0 && my == 0 ? en : (mx, my); }
        }
        c.MoveX = ux; c.MoveY = uy; c.FaceX = ux; c.FaceY = uy; c.Sprint = f.Stamina > 1500;
    }

    // ------------------------------------------------------------------ the part and the attack

    /// <summary>
    /// Zone choice: with chance skill/150 (and the stance's called shots on) the weakest zone by vulnerability (size over armour cover); otherwise by area alone. The subdue
    /// stance never aims at the head, neck or upper body.
    /// </summary>
    public static int PickPart(FightWorld w, Fighter f, Fighter t, int tick)
    {
        var plan = t.Plan;
        ulong exclude = 0;
        if (f.Stance.NoLethal) foreach (var p in plan.Parts) if (p.BaseId is "head" or "neck" or "upper_body") exclude |= 1UL << p.Index;
        bool smart = f.Stance.CalledShots && Noise(w, f, 15, 1000) < f.Skill * 1000 / w.K.SmartAimSkillDivisor;
        if (!smart) return w.AreaPart(f, plan, f.Serial + 1, exclude);
        int best = -1; long bestV = long.MinValue;
        foreach (var p in plan.Parts)
        {
            if (p.IsHosted || (exclude & (1UL << p.Index)) != 0 || (t.MissingMask & (1UL << p.Index)) != 0) continue;
            long prot = 0;
            foreach (var wv in t.Worn)
            {
                var a = wv.Stats;
                int cov = a.CoverageByPlan[plan.Index][p.BaseIndex];
                if (cov <= 0) continue;
                prot += (long)cov * (a.Layer switch { ArmourLayer.Armour => 2000, ArmourLayer.Over => 700, ArmourLayer.Cover => 300, _ => 200 }) / 1000;
            }
            long v = p.RelPpm * 1000L / (1000 + 1500 * prot / 1000);
            v += Noise(w, f, 16 + (uint)p.Index, 50);           // ties broken by noise, not by order
            if (v > bestV) { bestV = v; best = p.Index; }
        }
        return best >= 0 ? best : w.AreaPart(f, plan, f.Serial + 1, exclude);
    }

    /// <summary>
    /// The attack by situation (Auto): against hard armour on the zone blunt, then point, then edge; against soft or none edge, then point, then blunt; ties to the faster, then
    /// the lower index. No-lethal stances use only blunt attacks.
    /// </summary>
    public static int AutoAttack(FightWorld w, Fighter f, Fighter? t, int part)
    {
        var atks = f.Weapon.Attacks;
        // rigid plate on the zone: blunt, then point, then edge; mail (elastic metal): point, then edge (mail turns a cut into a blunt blow at 95%), then blunt;
        // soft or none: edge, point, blunt. Ties to the faster, then the lower index. No-lethal stances use only blunt attacks.
        int armour = t != null && part >= 0 ? ArmourOn(t, part) : 0;
        int best = 0, bestRank = int.MaxValue, bestVel = 0;
        for (int i = 0; i < atks.Length; i++)
        {
            var a = atks[i];
            if (f.Stance.NoLethal && a.Type != AttackType.Blunt) continue;
            int rank = armour switch
            {
                2 => a.Type switch { AttackType.Blunt => 0, AttackType.Point => 1, _ => 2 },
                1 => a.Type switch { AttackType.Point => 0, AttackType.Edge => 1, _ => 2 },
                _ => a.Type switch { AttackType.Edge => 0, AttackType.Point => 1, _ => 2 },
            };
            if (rank < bestRank || rank == bestRank && a.VelPm > bestVel) { best = i; bestRank = rank; bestVel = a.VelPm; }
        }
        return best;
    }

    private static int PointAttack(Fighter f)
    {
        for (int i = 0; i < f.Weapon.Attacks.Length; i++) if (f.Weapon.Attacks[i].Type == AttackType.Point && !f.Stance.NoLethal) return i;
        return -1;
    }

    /// <summary>A gap left by the target's worn pieces, aimed with chance skill/150 (the same rule as a smart zone), on the largest part it opens onto.</summary>
    public static (int Part, int Gap)? GapAim(FightWorld w, Fighter f, Fighter t)
    {
        if (Noise(w, f, 17, 1000) >= f.Skill * 1000 / w.K.SmartAimSkillDivisor) return null;
        var plan = t.Plan;
        ulong gaps = 0;
        foreach (var wv in t.Worn) gaps |= wv.Stats.GapMaskByPlan[plan.Index];
        if (gaps == 0) return null;
        int n = System.Numerics.BitOperations.PopCount(gaps), pick = Noise(w, f, 18, n), k = 0;
        for (int g = 0; g < plan.Gaps.Length; g++)
        {
            if ((gaps & (1UL << g)) == 0) continue;
            if (k++ != pick) continue;
            int best = -1, bestPpm = -1;
            foreach (var p in plan.Parts) if (!p.IsHosted && plan.Gaps[g].OnBase(p.BaseIndex) && (t.MissingMask & (1UL << p.Index)) == 0 && p.RelPpm > bestPpm) { best = p.Index; bestPpm = p.RelPpm; }
            return best < 0 ? null : (best, g);
        }
        return null;
    }

    /// <summary>0 nothing hard on the zone, 1 mail (elastic metal), 2 rigid metal plate (the outermost hard piece covering at least half of it decides).</summary>
    private static int ArmourOn(Fighter t, int part)
    {
        var p = t.Plan.Parts[part];
        int best = 0;
        foreach (var wv in t.Worn)
        {
            var a = wv.Stats;
            if (a.Layer != ArmourLayer.Armour || a.Material.Category != "metal" || a.CoverageByPlan[t.Plan.Index][p.BaseIndex] < 500) continue;
            best = Math.Max(best, a.Elastic ? 1 : 2);
        }
        return best;
    }

}
