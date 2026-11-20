using Ravensguard.Content;
using Ravensguard.Sim.Combat.Resolver2;
using Ravensguard.Sim.Core;

namespace Ravensguard.Sim.Combat.Melee2;

public sealed partial class FightWorld
{
    // ------------------------------------------------------------------ defence rolls (stream 0x430: FightTick, defender, blow serial)

    /// <summary>A defence roll of <paramref name="t"/> against attacker <paramref name="a"/>'s blow <paramref name="serial"/>, 0..999.</summary>
    public int DefenceRoll(Fighter t, int attacker, uint serial, int k) =>
        DetRng.Range(Seed, Tick, DefKey(t.Id, attacker), RngStreams.MeleeDefence, serial * 8u + (uint)k, 0, 1000);

    /// <summary>The defence streams' entity: defender and attacker together, so two blows on one defender in a tick draw apart.</summary>
    public static ulong DefKey(int defender, int attacker) => ((ulong)(uint)(defender + 1) << 20) | (uint)(attacker + 1);

    private readonly Dictionary<int, ulong> _guardMasks = [];
    /// <summary>Diagnostics (the calibration tool): impacts counted by the defender's action at the impact tick.</summary>
    public int[]? ImpactStates;
    public static bool DebugIdle;

    /// <summary>The zones a guard covers (head, neck, upper body: hit chance x0.6).</summary>
    public ulong GuardMask(BodyPlanV2 plan)
    {
        if (_guardMasks.TryGetValue(plan.Index, out var m)) return m;
        m = 0;
        foreach (var p in plan.Parts) if (!p.IsHosted && p.BaseId is "head" or "neck" or "upper_body") m |= 1UL << p.Index;
        _guardMasks[plan.Index] = m;
        return m;
    }

    /// <summary>An unaimed blow's part: by area (non-hosted parts weighted by their size), from the attacker's noise stream.</summary>
    public int AreaPart(Fighter a, BodyPlanV2 plan, uint serial, ulong exclude = 0)
    {
        long sum = 0;
        foreach (var p in plan.Parts) if (!p.IsHosted && (exclude & (1UL << p.Index)) == 0) sum += p.RelPpm;
        if (sum <= 0) return 0;
        long roll = (long)(DetRng.NextUInt(Seed, Tick, (ulong)(a.Id + 1), RngStreams.StanceNoise, serial * 8u + 7) % (ulong)sum);
        foreach (var p in plan.Parts)
        {
            if (p.IsHosted || (exclude & (1UL << p.Index)) != 0) continue;
            roll -= p.RelPpm;
            if (roll < 0) return p.Index;
        }
        return 0;
    }

    // ------------------------------------------------------------------ the blow lands (4.1, 4.2)

    private Fighter? FindTarget(Fighter a)
    {
        if (a.Req.Target >= 0 && a.Req.Target < Fighters.Count)
        {
            var t = Fighters[a.Req.Target];
            if (t.Team != a.Team && Hittable(t) && InStrike(a, t, ReachAgainst(a, t))) return t;
        }
        Fighter? best = null; long bd = long.MaxValue;
        foreach (var t in Fighters)
        {
            if (t.Team == a.Team || !Hittable(t) || !InStrike(a, t, ReachAgainst(a, t))) continue;
            long d = DistMm(a, t) + (_snapAct[t.Id] == FAct.Down ? 5000 : 0);       // prefer someone still standing
            if (d < bd) { bd = d; best = t; }
        }
        return best;
    }

    private bool Hittable(Fighter t) => _snapAct[t.Id] is not (FAct.Dead or FAct.Fled or FAct.Surrendered);

    private static AttackStyle StyleOf(SwingStyle s) => s switch { SwingStyle.Quick => AttackStyle.Quick, SwingStyle.Heavy => AttackStyle.Heavy, _ => AttackStyle.Normal };

    private StrikeV2 StrikeOf(Fighter a, Fighter t, int attack, int part, int gap, Facing facing, AttackStyle style, bool prone)
    {
        int skill = a.Skill + (a.Improvised != null ? K.ImprovisedHitPoints : 0);
        var s = new StrikeV2
        {
            Strength = (int)((long)a.Kit.Strength * a.StrengthPm / 1000), Skill = Math.Max(0, skill), SizeCm3 = a.Kit.SizeCm3, SpeciesSizeCm3 = a.Plan.SizeCm3,
            MomentumPm = a.BlowPm * (a.Kit.Blunted ? 500 : 1000) / 1000 * MoraleRearPm(t, facing) / 1000 * CrowdedMomentumPm(a, t) / 1000, Blunted = a.Kit.Blunted, Weapon = a.Weapon, Attack = Math.Clamp(attack, 0, a.Weapon.Attacks.Length - 1), Style = style,
            Plan = t.Plan, DefenderSizeCm3 = t.Kit.SizeCm3, DefenderSkill = prone ? 0 : t.Skill, Worn = t.Worn, WornCount = t.Worn.Length,
            GuardedMask = _snapAct.Length > t.Id && _snapAct[t.Id] == FAct.Block ? GuardMask(t.Plan) : 0, MissingMask = t.MissingMask | (a.Stance.NoLethal ? NoLethalMask(t.Plan) : 0), Prone = prone,
            TargetPart = part, TargetGap = gap, Facing = facing, SituationPoints = (a.Improvised != null ? 0 : 0) - (Crowded(a, t) ? K.CrowdedPoints : 0) + SitePoints(a, t),   // [exploration] hook: light, air, fog (4.8)
        };
        if (Mounts != null) MountedStrike(a, t, ref s);              // [mounted-combat] the charge's momentum, mounted accuracy
        return s;
    }

    /// <summary>A long weapon used inside its own comfortable range (closer than the polearm hold-off).</summary>
    private bool Crowded(Fighter a, Fighter t) => a.Family == "polearms" && K.CrowdedPoints != 0 && DistMm(a, t) < K.PolearmTooCloseMm;

    /// <summary>A polearm choked up inside its hold-off: the shortened grip's share of the momentum (close-out item 1).</summary>
    private int CrowdedMomentumPm(Fighter a, Fighter t) => a.Family == "polearms" && DistMm(a, t) < K.PolearmTooCloseMm ? K.CrowdedMomentumPm : 1000;

    /// <summary>Close-out item 1: the defender who parried or slipped a longer weapon's point is past it and presses inside its reach (the brain closes; the polearm is crowded).</summary>
    private void PassedThePoint(Fighter t, Fighter a)
    {
        if (K.InsideTicks <= 0 || a.Family != "polearms" || a.ReachMm < t.ReachMm + K.InsideReachMm || t.Family == "polearms") return;
        t.InsideUntil = Tick + K.InsideTicks; t.InsideOf = a.Id;
    }

    private BlowKey KeyOf(Fighter a, uint serial) => new(Seed, Tick, (ulong)(a.Id + 1), serial);

    private void ResolveImpact(Fighter a)
    {
        ResolveImpactCore(a);
        TellArmourHarm();
    }

    // ------------------------------------------------------------------ armour damage (close-out item 4)

    private readonly List<(int Wearer, int Attacker, int Piece, ArmourHarm Harm)> _harms = [];
    private int _harmWearer, _harmAttacker;
    private Action<int, ArmourHarm>? _onHarm;

    /// <summary>The blow's wear goes to the struck fighter's pieces (dents, burst rings, broken straps); what shows is told after the blow's own line.</summary>
    private void WearArmour(Fighter a, Fighter t)
    {
        if (t.Worn.Length == 0 || _r.StepCount == 0) return;
        _harmWearer = t.Id; _harmAttacker = a.Id;
        ArmourWear.Apply(t.Worn, _r, _onHarm ??= (piece, harm) => _harms.Add((_harmWearer, _harmAttacker, piece, harm)));
    }

    private void TellArmourHarm()
    {
        foreach (var (wearer, attacker, piece, harm) in _harms) Emit(FightEventKind.Broke, wearer, attacker, piece, 3, (int)harm);
        _harms.Clear();
    }

    private void ResolveImpactCore(Fighter a)
    {
        var req = a.Req;
        var t = FindTarget(a);
        if (t == null) { a.Whiffs++; Emit(FightEventKind.Whiff, a.Id, req.Target); return; }
        if (Mounts != null) MountedRetarget(a, ref t, ref req);      // [mounted-combat] rider or horse
        var ts = _snapAct[t.Id];
        var facing = FacingOf(t, a.X, a.Y);
        uint serial = a.Serial;
        if (ImpactStates != null) { ImpactStates[(int)ts]++; if (ts == FAct.Idle && DebugIdle) Console.WriteLine($"DBG idle impact tick {Tick} att {a.Id} def {t.Id} react {t.Brain.React} at {t.Brain.ReactAt} hold {t.Brain.HoldBlockUntil} cmdBlock {t.Cmd.Block} att.windStart {a.WindStart} need {a.WindNeeded} impactAt {a.ImpactAt}"); if (ts == FAct.Idle) ImpactStates[16 + t.Brain.React + (t.Brain.ReactSerialFoe == a.Id && t.Brain.ReactSerial == (int)a.Serial ? 0 : 8)]++; }
        int part = req.Part >= 0 && req.Part < t.Plan.Parts.Length ? req.Part : AreaPart(a, t.Plan, serial);
        var s = StrikeOf(a, t, req.Attack, part, req.Part >= 0 ? req.Gap : -1, facing, StyleOf(req.Style), ts == FAct.Down);
        var atk = a.Weapon.Attacks[s.Attack];

        // dodge: the 7 ticks move the target out of the blow (80%, -25 points in heavy armour)
        if (ts == FAct.Dodge)
        {
            int p = K.DodgeEvadePm - (t.ArmourG > K.HeavyArmourKg * 1000 ? K.HeavyArmourDodgePm : 0);
            if (DefenceRoll(t, a.Id, serial, 0) < p)
            {
                t.DodgedBy++;
                PassedThePoint(t, a);
                Emit(FightEventKind.Dodged, t.Id, a.Id, part);
                return;
            }
        }
        else if (ts is FAct.Block or FAct.Recover or FAct.Stagger or FAct.Aiming && facing != Facing.Rear)
        {
            bool guard = ts == FAct.Block;
            // parry: RMB pressed in the 4 ticks before the impact (4.2)
            if (guard && Tick - _snapBlockSince[t.Id] < K.ParryWindowTicks && Tick >= t.ParryDeadUntil && t.WeaponArm && t.Launcher == null)
            {
                int p = Math.Min(K.ParryCapPm, t.Skill * 1000 / K.ParrySkillDivisor + (t.Shield?.Def.ParryBonusPoints ?? 0) * 10) * atk.Def.ParryPm / 1000;
                if (DefenceRoll(t, a.Id, serial, 1) < p)
                {
                    if (a.Act == FAct.Strike) { a.Act = FAct.Stagger; a.ActTicks = RecoverTicks(a) + K.ParryStaggerExtraTicks; }
                    t.RiposteUntil = Tick + K.RiposteTicks;
                    t.ParriedBy++;
                    PassedThePoint(t, a);
                    Emit(FightEventKind.Parried, t.Id, a.Id, part);
                    Emit(FightEventKind.Riposte, t.Id, a.Id);
                    return;
                }
                t.Stamina = Math.Max(0, t.Stamina - K.ParryFailX100);
            }
            // the block roll: shield (front only, not against a blow over the wall), then the weapon
            bool over = OverWall(a);
            int shieldCover = ShieldCover(t, facing, atk, over), weaponCover = WeaponCover(t, facing);
            int st = _snapStamina[t.Id];
            int loss = st <= 0 ? K.EmptyCoverLossPm : st < K.ExhaustedBelowX100 ? K.ExhaustedCoverLossPm : 0;
            shieldCover = Math.Max(0, shieldCover - loss); weaponCover = Math.Max(0, weaponCover - loss);
            if (!guard) { int pm = ts == FAct.Stagger ? K.StaggerCoverPm : K.RecoverCoverPm; shieldCover = shieldCover * pm / 1000; weaponCover = weaponCover * pm / 1000; }
            int combined = 1000 - (1000 - shieldCover) * (1000 - weaponCover) / 1000;
            int roll = DefenceRoll(t, a.Id, serial, 2);
            if (roll < shieldCover)
                s.Block = new BlockInput { Kind = BlockKind.Shield, Shield = t.Shield, ShieldConditionPm = t.ShieldPm, BlockerSkill = t.ShieldSkill, BracedNeighbours = BracedNeighbours(t), ArmPart = ArmPart(t, 0) };
            else if (roll < combined)
                s.Block = new BlockInput { Kind = BlockKind.Weapon, BlockerSkill = t.Skill, ArmPart = ArmPart(t, 1) };
        }

        Resolver.Resolve(s, KeyOf(a, serial), _r);
        OnBlow?.Invoke(a, t, _r, s);
        WearArmour(a, t);
        BrittleBlade(a);
        if (Mounts != null) MountedOnBlow(a, t, s);                  // [mounted-combat] unhorse, lance shatter
        if (_r.Blocked)
        {
            ref var bo = ref _r.Block;
            t.Stamina = Math.Max(0, t.Stamina - bo.StaminaX100);
            t.BlockedBy++;
            if (bo.Stopped && atk.Type == AttackType.Point) PassedThePoint(t, a);      // the point beaten aside: inside it
            if (s.Block.Kind == BlockKind.Shield) WearShield(t, bo.ShieldLossPm);
            Wear(a, _r.InitialMilli);
            if (bo.Stagger && t.Act is FAct.Block or FAct.Idle or FAct.Recover) { t.Act = FAct.Stagger; t.ActTicks = K.OverpoweredStaggerTicks; Emit(FightEventKind.Overpowered, t.Id, a.Id); }
            Emit(FightEventKind.Blocked, t.Id, a.Id, part, s.Block.Kind == BlockKind.Shield ? 1 : 0, (int)(bo.ExcessMilli / 1000));
            if (_r.WoundCount > 0) Land(a, t, facing, _r);        // the excess reached the forearm
            return;
        }
        Wear(a, _r.InitialMilli);
        if (!_r.Hit) { Emit(FightEventKind.Missed, a.Id, t.Id, part); return; }
        if (_r.WoundCount == 0) { Emit(FightEventKind.Stopped, a.Id, t.Id, _r.Part, (int)(_r.InitialMilli / 1000)); return; }
        Land(a, t, facing, _r);
    }

    /// <summary>A guarding shield bearer's cover against a blow (4.2, 4.4): the shield's cover plus 100 per braced wall neighbour (max 2), times the attack's share (a flail 500),
    /// capped at 950; only from the front, and never against a polearm striking over the wall.</summary>
    public int ShieldCover(Fighter t, Facing facing, AttackV2 atk, bool overWall)
    {
        if (t.Shield == null || !t.ShieldArm || t.ShieldPm <= 0 || facing != Facing.Front || overWall) return 0;
        int c = t.Shield.CoverPm + BracedNeighbours(t) * K.ShieldWallBonusPm;
        c = c * atk.ShieldCoverPm / 1000;
        return Math.Min(K.ShieldCoverCapPm, c);
    }

    /// <summary>A guarding weapon's cover: <c>base + 2 x skill</c> permille, halved on the flank, nothing from behind or with no hand to hold it.</summary>
    public int WeaponCover(Fighter t, Facing facing)
    {
        if (!t.WeaponArm || t.Launcher != null || facing == Facing.Rear) return 0;
        int c = K.WeaponCoverBasePm + K.WeaponCoverPerSkillPm * t.Skill;
        return facing == Facing.Flank ? c * K.FlankWeaponCoverPm / 1000 : c;
    }

    private static int ArmPart(Fighter t, int side)
    {
        int i = t.Plan.IndexOf(side == 0 ? "lower_arm_l" : "lower_arm_r");
        return i >= 0 ? i : 0;
    }

    private void WearShield(Fighter t, int lossPm)
    {
        if (t.Shield == null || lossPm <= 0) return;
        t.ShieldPm -= lossPm;
        if (t.ShieldPm > 0) return;
        t.ShieldPm = 0;
        Emit(FightEventKind.Broke, t.Id, -1, -1, 1);
        Ground.Add(new GroundItem(t.Shield.Def.Id, t.Shield.Def.DefaultMaterial ?? "oak", t.X, t.Y, true));
        t.Shield = null;
    }

    /// <summary>P5-I (crafting-full 4.3): an untempered blade whose blow a rigid layer stopped breaks with <see cref="Crafting.Smithing.BrittleBreakPm"/> permille (stream 0x532, fight tick, item id); the fighter is left bare-handed.</summary>
    private void BrittleBlade(Fighter a)
    {
        if (a.Kit.Weapon is not { Brittle: true } w || a.Broke || !_r.RigidStop) return;
        ulong item = w.ItemId != 0 ? w.ItemId : (ulong)(a.Id + 1);
        if (!Crafting.Smithing.BrittleBreaks(Seed, Tick, new EntityId(item), true, true)) return;
        a.Broke = true;
        Emit(FightEventKind.Broke, a.Id, -1, -1, 2);
        Disarm(a);
    }

    /// <summary>A fragile improvised weapon loses condition by the momentum of each blow (melee 4.6); a bottle breaks on its first.</summary>
    private void Wear(Fighter a, long momentumMilli)
    {
        if (a.Improvised is not { } imp) return;
        a.ImprovisedBlows++;
        if (imp.Fragile) a.ImprovisedPm -= (int)(momentumMilli / 1000 * K.FragileLossPerUnitPm / 1000);
        if (imp.BreaksAfter > 0 && a.ImprovisedBlows >= imp.BreaksAfter) a.ImprovisedPm = 0;
        if (a.ImprovisedPm > 0) return;
        Emit(FightEventKind.Broke, a.Id, -1, -1, 2);
        if (imp.Leaves.Length > 0) Ground.Add(new GroundItem(imp.Leaves, imp.Material, a.X, a.Y, true));
        Disarm(a);
    }

    /// <summary>Drops to bare hands (a broken stool, a thrown weapon gone).</summary>
    internal void Disarm(Fighter a)
    {
        a.Improvised = null;
        a.Weapon = T.Weapon("fist", "bone");
        a.Family = "melee"; a.ReachMm = Math.Max(500, a.Weapon.Shape.ReachMm); a.StrikeTicks = Math.Max(1, a.Weapon.Shape.StrikeTicks);
        a.BaseWindTicks = TicksOf(a.Weapon.Shape.WindUpS); a.BaseRecoverTicks = TicksOf(a.Weapon.Shape.RecoverS);
        a.Skill = Math.Max(a.Skill * 8 / 10, 0);
    }

    /// <summary>The wounds of a landed blow go into the person's wound book (wounds 4.1); the body is re-read, morale pays, a wind-up is broken.</summary>
    private void Land(Fighter a, Fighter t, Facing facing, BlowResult r)
    {
        a.Landed++;
        t.Book.Apply(t.Plan, r, GameSec, Tick, a.Person != 0 ? a.Person : (ulong)(a.Id + 1));
        RefreshBody(t);
        MoraleOnHit(t, facing);                         // P4-D hook: a blow taken, a flank blow (morale 4.1 c, d)
        Emit(FightEventKind.Hit, a.Id, t.Id, r.Part, r.WoundCount, (r.Severed ? 1 : 0) | (r.Redirected ? 2 : 0) | (r.RibSlip ? 4 : 0) | ((int)facing << 4));
        if (t.Act == FAct.Windup && t.Req.Kind == BlowKind.Melee) { t.Act = FAct.Recover; t.ActTicks = K.HitInterruptTicks; Emit(FightEventKind.Interrupted, t.Id, a.Id); }
        else if (t.Act is FAct.Drawing or FAct.Aiming) { t.Act = FAct.Idle; Emit(FightEventKind.Interrupted, t.Id, a.Id); }
        if (t.Book.Dead) Kill(t);
        else if (!t.Book.Conscious && t.Act != FAct.Down) GoDown(t);
    }
}
