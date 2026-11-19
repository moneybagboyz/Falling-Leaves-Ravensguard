using Ravensguard.Content;
using Ravensguard.Sim.Core;

namespace Ravensguard.Sim.Combat.Resolver2;

/// <summary>
/// Resolver v2 behind the v1 call shape (<see cref="Melee.MeleeWorld.StrikeFn"/>), so the Phase 0 melee spike and its callers can run on v2
/// unchanged until the P4-C <c>FightWorld</c> replaces them. The key of every draw is the caller's stream key (seed, tick, attacker) with the
/// stream's draw count as the swing serial. Wounds come back as v1 records; the v2-only facts map onto the flags the spike reads:
/// a motor nerve or the cord sets <see cref="WoundFlags.NerveHit"/>, a ruptured ligament or mangled muscle <see cref="WoundFlags.TendonHit"/>
/// (all disable the limb), and a concussion an extra crush on the brain (a knock-out).
/// </summary>
public sealed class V1Adapter
{
    private readonly CombatTables _tables;
    private readonly ResolverV2 _v2;
    private readonly BlowResult _r = new();
    private readonly Dictionary<IReadOnlyList<WornItem>, WornV2[]> _worn = new(ReferenceEqualityComparer.Instance);

    public V1Adapter(CombatTables tables) { _tables = tables; _v2 = new ResolverV2(tables.V2); }
    public ResolverV2 Resolver => _v2;
    /// <summary>The last blow's full v2 result (trace, grades), for statistics.</summary>
    public BlowResult Last => _r;
    /// <summary>Called after every blow with the v2 result (the calibration harness counts mail conversions and soaks from it).</summary>
    public Action<BlowResult, StrikeV2>? OnBlow { get; set; }

    public StrikeV2 ToV2(StrikeInput input)
    {
        var t = _tables.V2;
        var plan = t.Plan(input.Defender.Plan.Id);
        var w = t.Weapon(input.Weapon.ShapeId, input.Weapon.MaterialId, input.Weapon.Quality);
        if (!_worn.TryGetValue(input.Defender.Worn, out var worn))
        {
            worn = [.. input.Defender.Worn.Select(i => WornV2.Of(t.Armour(i.ShapeId, i.MaterialId), CombatTablesV2.QualityPm[(int)i.Quality]))];
            _worn[input.Defender.Worn] = worn;
        }
        ulong guarded = 0;
        if (input.Defender.GuardedParts != null) foreach (var g in input.Defender.GuardedParts) { int i = plan.IndexOf(g); if (i >= 0) guarded |= 1UL << i; }
        return new StrikeV2
        {
            Strength = input.Attacker.Strength, Skill = input.Attacker.Skill, SizeCm3 = input.Attacker.SizeCm3, SpeciesSizeCm3 = input.Attacker.SpeciesSizeCm3,
            MomentumPm = 1000, Weapon = w, Attack = Math.Max(0, w.AttackIndex(input.AttackId)), Style = input.Style,
            Plan = plan, DefenderSizeCm3 = input.Defender.SizeCm3, DefenderSkill = input.Defender.Skill, Worn = worn, WornCount = worn.Length,
            GuardedMask = guarded, Prone = input.Defender.Prone, TargetPart = Math.Max(0, plan.IndexOf(input.TargetPart)),
            TargetGap = input.TargetGap == null ? -1 : plan.GapIndex(input.TargetGap), Facing = input.Facing,
        };
    }

    public StrikeResult Resolve(StrikeInput input, ref RngStream rng)
    {
        var s = ToV2(input);
        var key = new BlowKey(rng.Seed, rng.Tick, rng.Entity, rng.Draws + 1);
        _v2.Resolve(s, key, _r);
        OnBlow?.Invoke(_r, s);
        return ToV1(s.Plan, _r);
    }

    public StrikeResult ToV1(BodyPlanV2 plan, BlowResult r)
    {
        if (!r.Hit) return StrikeResult.Miss(r.InitialMilli);
        var mats = _tables.V2.MaterialList;
        var wounds = new List<Wound>(r.WoundCount + 1);
        foreach (var w in r.WoundSpan)
        {
            var part = plan.Parts[w.Part];
            string? sid = w.Structure >= 0 ? part.Structures[w.Structure].Id : w.Organ >= 0 ? part.Organs[w.Organ].Id : null;
            var f = w.Flags;
            if ((f & (WoundFlags.MotorNerveHit | WoundFlags.SpineHit)) != 0) f |= WoundFlags.NerveHit;
            if ((f & WoundFlags.LigamentHit) != 0 && w.Joint == JointGrade.Ruptured) f |= WoundFlags.TendonHit;
            string tissue = mats[w.Tissue].Id;
            bool muscle = sid == null && part.Layers.Any(l => l.Tissue.Index == w.Tissue && l.IsMuscle);
            if (muscle && w.Muscle == MuscleGrade.Mangled) f |= WoundFlags.TendonHit;
            var kind = w.Kind;
            if (sid == "brain") kind = (f & WoundFlags.Vital) != 0 ? WoundKind.Cut : WoundKind.Crush;   // the spike kills on a cut brain, knocks out on a crushed one
            wounds.Add(new Wound(part.Id, tissue, sid, kind, Fixed.FromRatio(Math.Min(1000, w.DepthPm), 1000), Fixed.FromRatio(w.BleedX10, 10), f));
            if ((w.Flags & WoundFlags.Concussion) != 0 && sid != "brain")
                wounds.Add(new Wound(part.Id, "organ", "brain", WoundKind.Crush, Fixed.One, Fixed.Zero, WoundFlags.OrganHit | WoundFlags.Concussion));
        }
        var trace = new List<LayerTrace>(r.StepCount);
        foreach (var st in r.StepSpan)
            trace.Add(new LayerTrace(st.IsArmour && st.Piece >= 0 ? "armour" : st.Tissue >= 0 ? mats[st.Tissue].Id : "block", st.IsArmour, st.Outcome, st.InMilli, st.OutMilli));
        return new StrikeResult(true, plan.Parts[r.Part].Id, r.Redirected, wounds, trace, r.InitialMilli, r.FinalMilli, r.Severed, r.Deflected);
    }
}
