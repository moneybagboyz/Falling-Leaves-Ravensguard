using Ravensguard.Content;
using Ravensguard.Sim.Core;

namespace Ravensguard.Sim.Combat.Melee;

/// <summary>
/// Real-time melee model (plan §4A.3 / §2.8): per-fighter action state machine at 20 Hz fixed step, block / parry / dodge / feint,
/// stamina, facing, then <see cref="StrikeResolver"/> on impact, with wounds feeding bleeding, pain, shock and morale.
/// Pure C#: all chance comes from <see cref="DetRng"/> keyed by (seed, tick, fighter, stream); no clock, no engine types.
/// </summary>
public sealed class MeleeWorld
{
    public const uint StreamImpact = 1, StreamAi = 2, StreamMisc = 3;

    public CombatTables Tables { get; }
    public StrikeResolver Resolver { get; }
    public ulong Seed { get; }
    public int Tick { get; private set; }
    public double TimeS => Tick * MeleeRules.Dt;
    public List<Fighter> Fighters { get; } = [];
    public List<MeleeEvent> Events { get; } = [];
    /// <summary>Wound records applied this world's lifetime, for statistics (kind/flags per wound).</summary>
    public List<(int Victim, Wound Wound)> WoundLog { get; } = [];
    public bool EmitText = true;

    private readonly List<Fighter> _impacts = [];

    public MeleeWorld(CombatTables tables, ulong seed)
    {
        Tables = tables; Resolver = new StrikeResolver(tables); Seed = seed;
    }

    // ------------------------------------------------------------------ setup

    public Fighter Add(FighterSpec spec, bool isPlayer = false, string planId = "human")
    {
        var plan = Tables.BodyPlans[planId];
        var ws = Tables.Weapon(spec.Weapon);
        double armourKg = 0;
        foreach (var w in spec.Worn) armourKg += Tables.Armour(w.ShapeId, w.MaterialId).MassKg(plan, plan.SizeCm3);
        double cap = 15 + 2.5 * (spec.Strength / 125.0);
        var cls = MeleeRules.ClassOf(spec.Weapon.ShapeId);
        var f = new Fighter
        {
            Id = Fighters.Count, Spec = spec, Plan = plan, WeaponStats = ws, Class = cls, Timing = MeleeRules.Timing(cls),
            IsPlayer = isPlayer, Pos = spec.Start, Face = spec.FaceDir.Normalized(),
            ArmourKg = armourKg, WeaponKg = ws.MassKg, OverKg = Math.Max(0, armourKg + ws.MassKg - cap),
            MaxBlood = plan.BloodMl, Blood = plan.BloodMl,
            Morale = spec.Courage,
        };
        f.PartIds = plan.Parts.Select(p => p.Id).ToArray();
        f.AreaWeights = plan.Parts.Select(p => p.RelativeSize).ToArray();
        f.VulnWeights = plan.Parts.Select(p =>
        {
            double prot = 0;
            foreach (var w in spec.Worn)
            {
                var sh = Tables.Armour(w.ShapeId, w.MaterialId).Shape;
                if (!sh.Coverage.TryGetValue(p.BaseId, out var cov)) continue;
                prot += cov * sh.Layer switch { ArmourLayer.Armour => 2.0, ArmourLayer.Over => 0.7, ArmourLayer.Cover => 0.3, _ => 0.2 };
            }
            return p.RelativeSize / (1.0 + 1.5 * prot);
        }).ToArray();
        Fighters.Add(f);
        return f;
    }

    public IEnumerable<Fighter> Enemies(Fighter f) => Fighters.Where(o => o.Team != f.Team);
    public bool IsOver => Fighters.Where(f => f.Active).Select(f => f.Team).Distinct().Count() <= 1;

    private RngStream Rng(int fighter, uint stream) => new(Seed, Tick, (ulong)fighter, stream);

    // ------------------------------------------------------------------ step

    /// <summary>Advances one 20 Hz tick. Controllers (AI or player) must have written each fighter's <see cref="Fighter.Cmd"/> first.</summary>
    public void Step()
    {
        Tick++;
        _impacts.Clear();
        foreach (var f in Fighters) UpdateVitals(f);
        if (Tick % MeleeRules.MoraleEveryTicks == 0) foreach (var f in Fighters) UpdateMorale(f);
        foreach (var f in Fighters) { ApplyCommand(f); AdvanceState(f); }
        foreach (var f in Fighters) Move(f);
        Separate();
        foreach (var a in _impacts) ResolveImpact(a);
        foreach (var f in Fighters) f.Cmd.ClearOneShots();
    }

    /// <summary>Convenience for headless runs: AI thinks for every non-player fighter, then one tick.</summary>
    public void StepWithAi()
    {
        foreach (var f in Fighters) if (!f.IsPlayer) MeleeAi.Think(this, f);
        Step();
    }

    // ------------------------------------------------------------------ timings

    public int WindupTicks(Fighter f, bool riposte)
    {
        double s = f.Timing.WindupS * MeleeRules.SkillScale(f.Spec.Skill) * (1 + 0.01 * f.OverKg);
        if (f.Stamina < MeleeRules.ExhaustedBelow) s *= MeleeRules.ExhaustedWindupScale;
        if (f.Weakened) s *= 1.2;
        if (riposte) s *= MeleeRules.RiposteWindupScale;
        return MeleeRules.Ticks(s);
    }

    public int RecoverTicks(Fighter f)
    {
        double s = f.Timing.RecoverS * MeleeRules.SkillScale(f.Spec.Skill) * (1 + 0.01 * f.OverKg);
        if (f.Weakened) s *= 1.2;
        return MeleeRules.Ticks(s);
    }

    public int StrikeTicks(Fighter f) => MeleeRules.Ticks(f.Timing.StrikeS);

    public double SwingStaminaCost(Fighter f) => MeleeRules.SwingBase + MeleeRules.SwingPerKg * f.WeaponKg;

    // ------------------------------------------------------------------ commands and state machine

    private void ApplyCommand(Fighter f)
    {
        if (!f.CanAct) return;
        var c = f.Cmd;
        if (!c.Face.IsZero) Turn(f, c.Face, f.State is Act.Strike or Act.Recover or Act.Dodge ? 0.1 : 0.5);

        if (c.Dodge is { } dd && !dd.IsZero && Tick >= f.DodgeReadyTick && !f.LegsLost && f.Stamina >= MeleeRules.DodgeCost
            && f.State is Act.Idle or Act.Block or Act.Windup)
        {
            f.State = Act.Dodge; f.StateTicks = MeleeRules.DodgeTicks; f.DodgeDir = dd.Normalized();
            f.DodgeReadyTick = Tick + MeleeRules.DodgeTicks - 1 + MeleeRules.DodgeCooldownTicks; // cooldown counts from the last dodge tick
            f.Stamina -= MeleeRules.DodgeCost; f.Req = null;
            Emit(EventKind.Info, f.Id, -1, $"{f.Name} dodges");
            return;
        }

        if (c.Feint && f.State == Act.Windup && f.WindupElapsed * 2 < f.WindupNeeded)
        {
            f.State = Act.Recover; f.StateTicks = MeleeRules.FeintRecoverTicks; f.Req = null; f.Stamina -= MeleeRules.FeintCost;
            Emit(EventKind.Feint, f.Id, -1, $"{f.Name} feints");
            return;
        }

        if (c.Start != null && f.State is Act.Idle or Act.Block && !f.WeaponArmLost)
        {
            var req = c.Start;
            if (req.AttackId.Length == 0) req.AttackId = f.WeaponStats.Shape.Attacks[0].Id;
            f.Req = req;
            f.RiposteWindup = Tick <= f.RiposteUntil;
            f.State = Act.Windup; f.WindupElapsed = 0; f.WindupStartTick = Tick; f.Heavy = false;
            f.WindupNeeded = WindupTicks(f, f.RiposteWindup);
            f.Stamina = Math.Max(0, f.Stamina - SwingStaminaCost(f));
            f.BlowsThrown++;
            Emit(EventKind.Windup, f.Id, req.TargetId, $"{f.Name} winds up ({f.Class})");
            return;
        }

        if (f.State == Act.Idle && c.Block) { f.State = Act.Block; f.BlockSince = Tick; }
        else if (f.State == Act.Block && !c.Block) f.State = Act.Idle;
    }

    private void AdvanceState(Fighter f)
    {
        switch (f.State)
        {
            case Act.Windup:
                f.WindupElapsed++;
                if (f.WindupElapsed >= f.WindupNeeded)
                {
                    bool holding = f.Cmd.Hold && f.Req is { AutoRelease: false } && f.WindupElapsed < f.WindupNeeded + MeleeRules.MaxHoldTicks;
                    if (holding)
                    {
                        if (f.WindupElapsed >= f.WindupNeeded + MeleeRules.HeavyHoldTicks) f.Heavy = true;
                        f.Stamina = Math.Max(0, f.Stamina - 3 * MeleeRules.Dt);
                        break;
                    }
                    f.State = Act.Strike; f.StateTicks = StrikeTicks(f);
                    _impacts.Add(f);
                }
                break;
            case Act.Strike:
                if (--f.StateTicks <= 0) { f.State = Act.Recover; f.StateTicks = RecoverTicks(f); }
                break;
            case Act.Recover:
                if (--f.StateTicks <= 0) f.State = Act.Idle;
                break;
            case Act.Stagger:
                if (--f.StateTicks <= 0) f.State = Act.Idle;
                break;
            case Act.Dodge:
                if (--f.StateTicks <= 0) f.State = Act.Idle;
                break;
        }
    }

    private static void Turn(Fighter f, Vec2 want, double rate)
    {
        var n = (f.Face + (want.Normalized() - f.Face) * rate).Normalized();
        if (!n.IsZero) f.Face = n;
    }

    // ------------------------------------------------------------------ movement

    private void Move(Fighter f)
    {
        if (f.State is Act.Down or Act.Dead or Act.Fled or Act.Surrendered or Act.Stagger) return;
        if (f.State == Act.Dodge) { f.Pos += f.DodgeDir * (MeleeRules.DodgeSpeed * MeleeRules.Dt); return; }
        var c = f.Cmd;
        if (c.Move.IsZero) return;
        double speed = MeleeRules.WalkSpeed;
        bool sprint = c.Sprint && f.State == Act.Idle && f.Stamina > 0;
        if (sprint) { speed = MeleeRules.SprintSpeed; f.Stamina = Math.Max(0, f.Stamina - MeleeRules.SprintPerS * MeleeRules.Dt); }
        speed *= f.State switch { Act.Windup or Act.Strike or Act.Recover => 0.3, Act.Block => 0.5, _ => 1.0 };
        if (f.Weakened) speed *= 0.8;
        if (f.LegsLost) speed *= 0.3;
        f.Pos += c.Move.Normalized() * (speed * MeleeRules.Dt);
    }

    private void Separate()
    {
        double min = 2 * MeleeRules.BodyRadius;
        for (int i = 0; i < Fighters.Count; i++)
        {
            var a = Fighters[i];
            if (a.State is Act.Dead or Act.Fled or Act.Surrendered) continue;
            for (int j = i + 1; j < Fighters.Count; j++)
            {
                var b = Fighters[j];
                if (b.State is Act.Dead or Act.Fled or Act.Surrendered) continue;
                var d = b.Pos - a.Pos; double len = d.Length;
                if (len >= min) continue;
                var dir = len < 1e-6 ? new Vec2(1, 0) : d * (1.0 / len);
                double push = (min - len) * 0.5;
                // downed bodies are pushed, not pushing
                bool aFixed = a.State == Act.Down, bFixed = b.State == Act.Down;
                if (aFixed && !bFixed) b.Pos += dir * (push * 2);
                else if (bFixed && !aFixed) a.Pos += dir * (-push * 2);
                else { a.Pos += dir * -push; b.Pos += dir * push; }
            }
        }
    }

    // ------------------------------------------------------------------ vitals

    private void UpdateVitals(Fighter f)
    {
        if (f.State == Act.Dead) return;
        double dt = MeleeRules.Dt;

        // bleeding: minor wounds clot (90 s half-life); arteries, veins, organs and severances never do
        double bleed = 0;
        foreach (var w in f.Wounds)
        {
            bleed += w.Rate;
            if (w.Clots) w.Rate *= MeleeRules.ClotPerTick;
        }
        f.Blood -= bleed * dt;
        f.Pain *= MeleeRules.PainDecayPerTick;

        double loss = f.BloodLossFrac;
        f.Weakened = loss >= MeleeRules.ShockWeak;
        if (loss >= MeleeRules.ShockDead) { Kill(f, "bled out"); return; }
        if (f.CollapseTick >= 0 && Tick >= f.CollapseTick && f.State != Act.Down) GoDown(f, DownReason.Heart, "collapses (heart wound)");
        else if (loss >= MeleeRules.ShockCollapse && f.State != Act.Down)
            GoDown(f, DownReason.Blood, loss >= MeleeRules.ShockUnconscious ? "falls unconscious from blood loss" : "collapses from blood loss");
        else if (f.Pain >= f.KnockoutPain && f.State != Act.Down) GoDown(f, DownReason.Pain, "is knocked out by pain");
        else if (f.State == Act.Down && f.Down == DownReason.Pain && f.Pain < 0.6 * f.KnockoutPain && loss < MeleeRules.ShockCollapse)
        { f.Down = DownReason.None; f.State = Act.Idle; Emit(EventKind.Wake, f.Id, -1, $"{f.Name} comes to"); }

        // stamina (plan §4A.3)
        if (f.State is Act.Idle or Act.Recover or Act.Stagger or Act.Down or Act.Block)
        {
            bool sprinting = f.State == Act.Idle && f.Cmd.Sprint && !f.Cmd.Move.IsZero && f.Stamina > 0;
            if (!sprinting)
            {
                double r = f.State == Act.Block || f.ArmourKg > MeleeRules.HeavyArmourKg ? MeleeRules.RegenGuard : MeleeRules.RegenRest;
                if (f.Weakened) r *= 0.7;
                f.Stamina = Math.Min(MeleeRules.StaminaMax, f.Stamina + r * dt);
            }
        }
    }

    private void Kill(Fighter f, string why)
    {
        if (f.State == Act.Dead) return;
        f.State = Act.Dead; f.Req = null;
        Emit(EventKind.Death, f.Id, -1, $"{f.Name} dies ({why})", severe: true);
        Announce(f);
    }

    private void GoDown(Fighter f, DownReason why, string text)
    {
        f.State = Act.Down; f.Down = why; f.Req = null;
        Emit(EventKind.Down, f.Id, -1, $"{f.Name} {text}", severe: true);
        Announce(f);
    }

    /// <summary>A friend falling costs morale of teammates within 10 m (once per fighter).</summary>
    private void Announce(Fighter f)
    {
        if (f.Announced) return;
        f.Announced = true;
        foreach (var o in Fighters)
            if (o.Id != f.Id && o.Team == f.Team && o.Active && (o.Pos - f.Pos).Length <= 10) o.MoralePenalty += MeleeRules.FriendDeathPenalty;
    }

    private void UpdateMorale(Fighter f)
    {
        if (!f.Active) return;
        f.MoralePenalty = Math.Max(0, f.MoralePenalty - MeleeRules.PenaltyDecayPerS * 0.5);
        foreach (var o in Fighters)
            if (o.Id != f.Id && o.Team == f.Team && o.Routing && o.Active && (o.Pos - f.Pos).Length <= 6)
                f.MoralePenalty += MeleeRules.RoutingNeighbourPerS * 0.5;
        f.Morale = Math.Clamp(f.Spec.Courage - f.MoralePenalty - 0.35 * f.Pain - 100.0 * f.BloodLossFrac, 0, 100);
        if (f.IsPlayer) return;

        if (!f.Routing && f.Morale < MeleeRules.RoutBelow)
        {
            f.Routing = true; f.Req = null; f.Cmd.Block = false;
            if (f.State is Act.Windup or Act.Block) f.State = Act.Idle;
            Emit(EventKind.Rout, f.Id, -1, $"{f.Name} breaks and runs!", severe: true);
        }
        else if (f.Routing && f.Morale > MeleeRules.RallyAbove)
        {
            f.Routing = false;
            Emit(EventKind.Rally, f.Id, -1, $"{f.Name} rallies");
        }

        if (f.Routing)
        {
            Fighter? near = null; double nd = double.MaxValue;
            foreach (var e in Enemies(f)) if (e.Active) { double d = (e.Pos - f.Pos).Length; if (d < nd) { nd = d; near = e; } }
            if (near == null || nd > 20)
            {
                f.State = Act.Fled; f.FledOrSurrenderTick = Tick; f.Req = null;
                Emit(EventKind.Fled, f.Id, -1, $"{f.Name} escapes the field");
                Announce(f);
            }
            else if (f.Morale < MeleeRules.SurrenderBelow && nd <= 2 * MeleeRules.BodyRadius + 0.9)
            {
                f.State = Act.Surrendered; f.FledOrSurrenderTick = Tick; f.Req = null;
                Emit(EventKind.Surrender, f.Id, near!.Id, $"{f.Name} throws down their weapon and yields", severe: true);
                Announce(f);
            }
        }
    }

    // ------------------------------------------------------------------ impact

    /// <summary>Position of <paramref name="attacker"/> relative to the defender's facing.</summary>
    public static Facing FacingOf(Fighter defender, Fighter attacker)
    {
        double dot = defender.Face.Dot((attacker.Pos - defender.Pos).Normalized());
        return dot >= MeleeRules.FrontArcCos ? Facing.Front : dot <= MeleeRules.RearArcCos ? Facing.Rear : Facing.Flank;
    }

    private Fighter? FindTarget(Fighter a)
    {
        double maxD = a.Reach + 2 * MeleeRules.BodyRadius + 0.15;
        if (a.Req!.TargetId >= 0)
        {
            var t = Fighters[a.Req.TargetId];
            return InStrikeRange(a, t, maxD) ? t : null;
        }
        Fighter? best = null; double bd = double.MaxValue;
        foreach (var t in Fighters)
        {
            if (t.Team == a.Team || !InStrikeRange(a, t, maxD)) continue;
            double d = (t.Pos - a.Pos).Length + (t.State == Act.Down ? 5 : 0); // prefer someone still standing
            if (d < bd) { bd = d; best = t; }
        }
        return best;
    }

    private static bool InStrikeRange(Fighter a, Fighter t, double maxD)
    {
        if (t.State is Act.Dead or Act.Fled or Act.Surrendered) return false;
        var d = t.Pos - a.Pos; double len = d.Length;
        return len <= maxD && a.Face.Dot(len < 1e-6 ? a.Face : d * (1.0 / len)) >= MeleeRules.AttackConeCos;
    }

    private void ResolveImpact(Fighter a)
    {
        var req = a.Req;
        if (req == null) return;
        var t = FindTarget(a);
        if (t == null) { Emit(EventKind.Whiff, a.Id, req.TargetId, $"{a.Name}'s blow finds only air"); return; }

        var rng = Rng(a.Id, StreamImpact);
        var atk = a.WeaponStats.Attack(req.AttackId);
        var facing = FacingOf(t, a);

        // dodge (0.35 s window; moves the target out of the blow)
        if (t.State == Act.Dodge)
        {
            if (rng.Range(0, 1000) < (int)(MeleeRules.DodgeEvadeChance * 1000))
            { Emit(EventKind.Dodged, t.Id, a.Id, $"{t.Name} dodges {a.Name}'s {atk.Name.ToLowerInvariant()}"); return; }
        }
        // parry (Block pressed within 0.20 s before impact), then block (hold)
        else if (t.State == Act.Block && facing != Facing.Rear)
        {
            double swingV = atk.Velocity * 6.0, joules = 0.5 * a.WeaponKg * swingV * swingV;
            if (Tick - t.BlockSince <= MeleeRules.ParryWindowTicks)
            {
                double p = Math.Min(0.95, t.Spec.Skill / 120.0);
                t.Stamina = Math.Max(0, t.Stamina - (MeleeRules.BlockBase + joules * MeleeRules.BlockPerJoule));
                if (rng.Range(0, 1000) < (int)(p * 1000))
                {
                    a.State = Act.Stagger; a.StateTicks = RecoverTicks(a) + 4; a.Req = null;
                    t.RiposteUntil = Tick + MeleeRules.RiposteTicks;
                    Emit(EventKind.Parried, t.Id, a.Id, $"{t.Name} parries {a.Name}'s {atk.Name.ToLowerInvariant()} -- riposte open");
                    Emit(EventKind.Riposte, t.Id, a.Id, $"{t.Name} may riposte");
                    return;
                }
            }
            double cover = 0;
            double shield = facing == Facing.Front ? t.ShieldCover : 0;
            if (shield > 0)
            {
                int braced = 0;
                foreach (var o in Fighters)
                    if (o.Id != t.Id && o.Team == t.Team && o.State == Act.Block && o.ShieldCover > 0 && (o.Pos - t.Pos).Length <= 1.3) braced++;
                shield += MeleeRules.WallBonusPerNeighbour * Math.Min(2, braced);
                if (t.Stamina < MeleeRules.ExhaustedBelow) shield -= MeleeRules.ExhaustedCoverLoss;
            }
            double weapon = MeleeRules.WeaponBlockBase + MeleeRules.WeaponBlockPerSkill * t.Spec.Skill;
            if (facing == Facing.Flank) weapon *= 0.5;
            if (t.Stamina < MeleeRules.ExhaustedBelow) weapon -= MeleeRules.ExhaustedCoverLoss;
            cover = 1.0 - (1.0 - Math.Max(0, shield)) * (1.0 - Math.Max(0, weapon));
            if (rng.Range(0, 1000) < (int)(cover * 1000))
            {
                t.Stamina = Math.Max(0, t.Stamina - (MeleeRules.BlockBase + joules * MeleeRules.BlockPerJoule));
                Emit(EventKind.Blocked, t.Id, a.Id, $"{t.Name} blocks {a.Name}'s {atk.Name.ToLowerInvariant()}" + (shield > 0 ? " on the shield" : ""));
                return;
            }
        }

        // the blow lands: hit roll, coverage, layers, tissues (StrikeResolver)
        var style = a.Heavy ? AttackStyle.Heavy : AttackStyle.Normal;
        IReadOnlySet<string>? guarded = t.State == Act.Block ? Guarded : null;
        var def = new Defender(t.Plan, t.Plan.SizeCm3, t.Spec.Worn, t.Spec.Skill, guarded, t.State == Act.Down);
        double str = a.Spec.Strength * (a.Weakened ? 0.8 : 1.0);
        var attacker = new Attacker((int)str, a.Spec.Skill, a.Plan.SizeCm3, 70000);
        var res = Resolver.Resolve(new StrikeInput(attacker, a.Spec.Weapon, req.AttackId, req.PartId, def, req.Gap, facing, style), ref rng);

        if (!res.Hit || res.Wounds.Count == 0)
        {
            string why = !res.Hit ? "misses" : res.Deflected ? "glances off the armour of" : "is stopped by the armour of";
            Emit(EventKind.Whiff, a.Id, t.Id, $"{a.Name} {atk.Verb} at {PartName(t, req.PartId)} but {why} {t.Name}");
            return;
        }
        a.HitsLanded++;
        ApplyWounds(a, t, atk, res, facing);
    }

    private static readonly IReadOnlySet<string> Guarded = new HashSet<string>(["head", "upper_body", "neck"], StringComparer.Ordinal);

    private static string PartName(Fighter t, string partId) => t.Plan.ById.TryGetValue(partId, out var p) ? p.Name.ToLowerInvariant() : partId;

    private void ApplyWounds(Fighter a, Fighter t, AttackDef atk, StrikeResult res, Facing facing)
    {
        t.WoundsTaken++;
        t.MoralePenalty += MeleeRules.HitTakenPenalty;
        if (facing != Facing.Front && Tick >= t.FlankedUntil)
        {
            t.MoralePenalty += MeleeRules.FlankedPenalty; t.FlankedUntil = Tick + 5 * MeleeRules.TickHz;
        }
        string partName = PartName(t, res.PartId!);
        string verb = atk.Verb;
        var parts = new List<string>();
        bool severe = false;
        foreach (var w in res.Wounds)
        {
            WoundLog.Add((t.Id, w));
            double rate = w.BleedMlPerS.ToDouble();
            bool structure = w.StructureId != null;
            var aw = new ActiveWound
            {
                PartId = w.PartId, TissueId = w.TissueId, StructureId = w.StructureId, Rate = rate, Severed = w.IsSevered,
                Clots = !structure && !w.IsSevered,
            };
            if (rate > 0) t.Wounds.Add(aw);
            t.Pain += PainOf(w);
            severe |= (w.Flags & (WoundFlags.ArteryHit | WoundFlags.Severed | WoundFlags.Vital | WoundFlags.BoneBroken | WoundFlags.OrganHit)) != 0;
            parts.Add(Describe(w));
            ApplyFunctionLoss(t, w);
            if (w.StructureId == "brain")
            {
                if (w.Kind == WoundKind.Cut) { Kill(t, "brain destroyed"); }
                else { t.Pain += 200; }
            }
            if (w.StructureId == "heart" && t.CollapseTick < 0) t.CollapseTick = Tick + 6 * MeleeRules.TickHz;
        }
        string txt = $"{a.Name} {verb} {t.Name} in the {partName}: {string.Join(", ", parts.Distinct())}" + (res.Redirected ? " (glancing)" : "");
        Emit(EventKind.Wound, a.Id, t.Id, txt, res.PartId, severe);
        if (t.Alive && t.Pain >= t.KnockoutPain && t.State != Act.Down) GoDown(t, DownReason.Pain, "is knocked out by pain");
        // a struck fighter loses any wind-up in progress
        if (t.State is Act.Windup && !t.IsPlayer) { t.State = Act.Recover; t.StateTicks = MeleeRules.Ticks(0.2); t.Req = null; }
    }

    private static double PainOf(Wound w)
    {
        double p = w.TissueId switch
        {
            "skin" or "fat" or "hair" or "cartilage" => 1.5,
            "muscle" => 4,
            "bone_tissue" => 6,
            "organ" => 10,
            _ => 3,
        };
        if (w.Kind == WoundKind.Crush) p *= 1.3;
        p *= 0.5 + 0.5 * w.Depth.ToDouble();
        if ((w.Flags & WoundFlags.BoneBroken) != 0) p += 22;
        if ((w.Flags & WoundFlags.NerveHit) != 0) p += 16;
        if ((w.Flags & WoundFlags.TendonHit) != 0) p += 8;
        if ((w.Flags & (WoundFlags.ArteryHit | WoundFlags.VeinHit)) != 0) p += 4;
        if ((w.Flags & WoundFlags.OrganHit) != 0) p += 8;
        if ((w.Flags & WoundFlags.Severed) != 0) p += 40;
        return p;
    }

    private void ApplyFunctionLoss(Fighter t, Wound w)
    {
        bool arm = w.PartId.Contains("arm") || w.PartId.StartsWith("hand", StringComparison.Ordinal);
        bool leg = w.PartId.Contains("leg") || w.PartId.StartsWith("foot", StringComparison.Ordinal);
        bool disabling = (w.Flags & (WoundFlags.Severed | WoundFlags.TendonHit | WoundFlags.NerveHit | WoundFlags.BoneBroken)) != 0;
        if (!disabling) return;
        if (arm && w.PartId.EndsWith("_r", StringComparison.Ordinal) && !t.WeaponArmLost)
        {
            t.WeaponArmLost = true; t.MoralePenalty += MeleeRules.LimbLossPenalty;
            if (t.State is Act.Windup) { t.State = Act.Idle; t.Req = null; }
            Emit(EventKind.Info, t.Id, -1, $"{t.Name} can no longer wield the weapon");
        }
        else if (arm && w.PartId.EndsWith("_l", StringComparison.Ordinal) && !t.ShieldLost && t.Spec.ShieldCover > 0)
        { t.ShieldLost = true; Emit(EventKind.Info, t.Id, -1, $"{t.Name} drops the shield"); }
        else if (leg && !t.LegsLost)
        { t.LegsLost = true; t.MoralePenalty += MeleeRules.LimbLossPenalty; Emit(EventKind.Info, t.Id, -1, $"{t.Name} can barely stand"); }
    }

    public static string Describe(Wound w)
    {
        if (w.IsSevered) return "SEVERED";
        if (w.StructureId != null)
        {
            string s = w.StructureId.Replace('_', ' ');
            return (w.Flags & WoundFlags.ArteryHit) != 0 ? $"{s} cut, bleeding heavily"
                 : (w.Flags & WoundFlags.VeinHit) != 0 ? $"{s} cut, bleeding"
                 : (w.Flags & (WoundFlags.NerveHit | WoundFlags.TendonHit)) != 0 ? $"{s} severed"
                 : $"{s} {(w.Kind == WoundKind.Cut ? "pierced" : "crushed")}";
        }
        if ((w.Flags & WoundFlags.BoneBroken) != 0) return "bone broken";
        string tissue = w.TissueId.Replace("bone_tissue", "bone").Replace('_', ' ');
        return w.Kind == WoundKind.Cut ? $"cut {tissue}" : $"bruised {tissue}";
    }

    private void Emit(EventKind kind, int actor, int target, string text, string? part = null, bool severe = false)
    {
        if (!EmitText) text = "";
        Events.Add(new MeleeEvent(Tick, kind, actor, target, text, part, severe));
    }

    // ------------------------------------------------------------------ results

    public Outcome OutcomeOf(Fighter f) => f.State switch
    {
        Act.Dead => Outcome.Death,
        Act.Down => Outcome.Knockout,
        Act.Fled => Outcome.Rout,
        Act.Surrendered => Outcome.Surrender,
        _ => f.Routing ? Outcome.Rout : Outcome.Stalemate,
    };
}
