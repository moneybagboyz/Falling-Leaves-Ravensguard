using Ravensguard.Content;
using Ravensguard.Sim.Core;

namespace Ravensguard.Sim.Combat;

/// <summary>
/// Combat resolver v1: one blow through the §2.6 pipeline. Pure: all randomness comes from the caller's
/// <see cref="RngStream"/>, drawn in a fixed order (hit, redirect, coverage rolls outside-in, structures, organs).
/// <para>Interpretations of the spec (see also the report): the zone area factor is normalised to the largest part
/// (0.2 + 0.8·size/largest); Qa is 1 for body tissue; the 5% momentum loss and the strain-at-yield damping apply as in the DF page;
/// a blow that fails both tests on a layer is blunt for good but keeps being tested on the layers behind it;
/// the deflect test only applies to worn layers; blunt weapon quality does not matter (only Qw in the edged test).</para>
/// </summary>
public sealed class StrikeResolver(CombatTables tables)
{
    public const double LayerMomentumKept = 0.95;
    public const double BleedCapillary = 0.5, BleedMuscle = 3, BleedVein = 15, BleedArteryMin = 80, BleedArteryMax = 120, BleedSevered = 90;
    private const double StrainReference = 50_000;

    public CombatTables Tables { get; } = tables;

    // ---- hit chance (plan §2.6 step 2) ----

    /// <summary>P(hit) = zone area × (0.5 + Δskill/200); ×0.6 guarded, ×0.35 armour gap, flank ×1.25, rear ×1.5. Clamped to [2%, 98%].</summary>
    public static double HitChance(double relativeSize, double largestRelativeSize, int attackerSkill, int defenderSkill,
        bool guarded, bool armourGap, Facing facing)
    {
        double area = 0.2 + 0.8 * (relativeSize / largestRelativeSize);
        double p = area * (0.5 + (attackerSkill - defenderSkill) / 200.0);
        if (guarded) p *= 0.6;
        if (armourGap) p *= 0.35;
        if (facing == Facing.Flank) p *= 1.25;
        else if (facing == Facing.Rear) p *= 1.5;
        return Math.Clamp(p, 0.02, 0.98);
    }

    public double HitChance(StrikeInput input)
    {
        var plan = input.Defender.Plan;
        var part = plan.ById[input.TargetPart];
        bool guarded = input.Defender.GuardedParts?.Contains(part.Id) == true;
        return HitChance(part.RelativeSize, plan.MaxRelativeSize, input.Attacker.Skill, input.Defender.Skill,
            guarded, GapApplies(input, part), input.Facing);
    }

    private bool GapApplies(StrikeInput input, BodyPart part)
    {
        if (input.TargetGap == null) return false;
        if (!input.Defender.Plan.NamedGaps.TryGetValue(input.TargetGap, out var gapPart) || gapPart != part.BaseId) return false;
        return input.Defender.Worn.Any(w => Tables.Armour(w.ShapeId, w.MaterialId).Shape is { Layer: ArmourLayer.Armour } s && s.Gaps.Contains(input.TargetGap));
    }

    // ---- entry points ----

    /// <summary>Full resolution: hit roll on the targeted zone (before any redirect), 50% redirect on a miss, then the blow.</summary>
    public StrikeResult Resolve(StrikeInput input, ref RngStream rng)
    {
        double initial = InitialMomentum(input);
        double p = HitChance(input);
        if (rng.Range(0, 10_000) < (int)(p * 10_000.0))
            return ResolveBlow(input, input.TargetPart, false, initial, ref rng);

        if (rng.Range(0, 2) == 0)
        {
            var neigh = input.Defender.Plan.Neighbours(input.TargetPart).ToList();
            if (neigh.Count > 0)
            {
                string other = neigh[rng.Range(0, neigh.Count)];
                return ResolveBlow(input with { TargetGap = null }, other, true, initial, ref rng);
            }
        }
        return StrikeResult.Miss(Momentum.Milli(initial));
    }

    /// <summary>Resolves a blow that is known to land on <paramref name="partId"/> (no hit roll).</summary>
    public StrikeResult ResolveImpact(StrikeInput input, ref RngStream rng) =>
        ResolveBlow(input, input.TargetPart, false, InitialMomentum(input), ref rng);

    public double InitialMomentum(StrikeInput input)
    {
        var w = Tables.Weapon(input.Weapon);
        var atk = w.Attack(input.AttackId);
        double m = Momentum.Base(input.Attacker.Strength, atk.Velocity, input.Attacker.SpeciesSizeCm3, input.Attacker.SizeCm3, w.TotalMassMg);
        m *= Momentum.SkillMultiplier(input.Attacker.Skill) * Momentum.StyleMultiplier(input.Style);
        if (input.Defender.Prone) m *= Momentum.ProneMultiplier;
        return m;
    }

    // ---- the blow ----

    private StrikeResult ResolveBlow(StrikeInput input, string partId, bool redirected, double initialM, ref RngStream rng)
    {
        var def = input.Defender;
        var part = def.Plan.ById[partId];
        var w = Tables.Weapon(input.Weapon);
        var atk = w.Attack(input.AttackId);
        double partSize = def.SizeCm3 * part.RelativeSize;

        double m = initialM;
        bool edged = atk.Type != AttackType.Blunt;
        double pen = atk.PenetrationMm;
        var wounds = new List<Wound>();
        var trace = new List<LayerTrace>();
        bool deflected = false;

        // --- worn layers, outside in: cover, armour, over, under; coverage roll per piece ---
        var pieces = def.Worn.Select(i => (Item: i, Stats: Tables.Armour(i.ShapeId, i.MaterialId)))
            .OrderByDescending(x => (int)x.Stats.Shape.Layer).ThenByDescending(x => x.Stats.Shape.Order)
            .ThenBy(x => x.Item.ShapeId, StringComparer.Ordinal).ToList();
        foreach (var (item, stats) in pieces)
        {
            if (m < 0.001) break;
            if (!stats.Shape.Coverage.TryGetValue(part.BaseId, out var cov) || cov <= 0) continue;
            string name = stats.Shape.Id;
            bool covered = rng.Range(0, 10_000) < (int)(cov * 10_000.0);
            long mIn = Momentum.Milli(m);
            if (!covered) { trace.Add(new(name, true, LayerOutcome.NotCovered, mIn, mIn)); continue; }
            if (input.TargetGap != null && stats.Shape.Layer == ArmourLayer.Armour && stats.Shape.Gaps.Contains(input.TargetGap)
                && def.Plan.NamedGaps.TryGetValue(input.TargetGap, out var gp) && gp == part.BaseId)
            { trace.Add(new(name, true, LayerOutcome.Bypassed, mIn, mIn)); continue; }

            double qa = QualityTable.Multiplier(item.Quality);
            double area = Math.Min(atk.ContactArea, partSize * cov);
            var res = TestLayer(w, atk, stats.Props, qa, area, true, ref m, ref edged);
            if (res == LayerOutcome.Cut) ConsumePen(ref pen, ref edged, stats.Shape.ThicknessMm);
            if (res == LayerOutcome.Deflected) { m = 0; deflected = true; }
            trace.Add(new(name, true, res, mIn, Momentum.Milli(m)));
            if (deflected) break;
        }

        // --- body tissue layers ---
        double penAfterArmour = edged ? pen : 0; // what is left of the edge's reach once armour has been cut
        bool breachedAll = false, allCutFull = edged;
        if (!deflected)
        {
            breachedAll = true;
            for (int i = 0; i < part.Layers.Count; i++)
            {
                var layer = part.Layers[i];
                if (m < 0.001) { breachedAll = false; break; }
                bool wasEdged = edged;
                double penBefore = pen;
                long mIn = Momentum.Milli(m);
                double area = Math.Min(atk.ContactArea, partSize);
                var res = TestLayer(w, atk, layer.Props, 1.0, area, false, ref m, ref edged);
                trace.Add(new(layer.TissueId, false, res, mIn, Momentum.Milli(m)));
                if (res is LayerOutcome.Stopped) { breachedAll = false; allCutFull = false; continue; }
                if (res == LayerOutcome.Cut)
                {
                    double depth = 1.0;
                    if (pen < layer.ThicknessMm) { depth = pen / layer.ThicknessMm; allCutFull = false; edged = false; pen = 0; }
                    else pen -= layer.ThicknessMm;
                    bool isMuscle = layer.TissueId == "muscle";
                    wounds.Add(new Wound(part.Id, layer.TissueId, null, WoundKind.Cut, PermilleFixed(depth),
                        Rate(CutBleed(layer.TissueId)), WoundFlags.None));
                    if (isMuscle) RollStructures(part, atk, ref rng, wounds);
                }
                else // Crushed (the edge, if any, only failed for this layer; allCutFull no longer true)
                {
                    if (wasEdged) allCutFull = false;
                    var flags = IsBone(layer.TissueId) ? WoundFlags.BoneBroken : WoundFlags.None;
                    wounds.Add(new Wound(part.Id, layer.TissueId, null, WoundKind.Crush, Fixed.One,
                        Rate(layer.TissueId == "muscle" ? 1.0 : 0), flags));
                }
            }
        }

        // --- sever, then organs behind the part ---
        bool severed = false;
        if (breachedAll && part.Severable && atk.Type == AttackType.Edge && edged && allCutFull && penAfterArmour >= part.DepthMm)
        {
            severed = true;
            wounds.Add(new Wound(part.Id, part.Layers[^1].TissueId, null, WoundKind.Cut, Fixed.One, Rate(BleedSevered), WoundFlags.Severed));
        }
        else if (breachedAll && part.Organs.Count > 0)
        {
            double reach = atk.Type switch
            {
                AttackType.Point => 2.0,
                AttackType.Edge => 1.0 + Math.Min(atk.ContactArea, 60) / 20.0,
                _ => 0.5,
            };
            foreach (var o in part.Organs)
            {
                if (!(rng.Range(0, 10_000) < (int)(Math.Min(1.0, o.AreaFraction * reach) * 10_000.0))) continue;
                var f = WoundFlags.OrganHit | (o.Vital ? WoundFlags.Vital : WoundFlags.None);
                wounds.Add(new Wound(part.Id, o.Tissue, o.Id, edged ? WoundKind.Cut : WoundKind.Crush, Fixed.One, Rate(o.BleedMlPerS), f));
            }
        }

        return new StrikeResult(true, part.Id, redirected, wounds, trace, Momentum.Milli(initialM), Momentum.Milli(m), severed, deflected);
    }

    private void RollStructures(BodyPart part, AttackDef atk, ref RngStream rng, List<Wound> wounds)
    {
        double width = 1.0 + Math.Min(atk.ContactArea, 60) / 20.0;
        foreach (var s in part.Structures)
        {
            if (!(rng.Range(0, 10_000) < (int)(Math.Min(1.0, s.AreaFraction * width) * 10_000.0))) continue;
            WoundFlags f; double bleed;
            switch (s.Kind)
            {
                case StructureKind.Artery: f = WoundFlags.ArteryHit; bleed = rng.Range((int)BleedArteryMin, (int)BleedArteryMax + 1); break;
                case StructureKind.Vein: f = WoundFlags.VeinHit; bleed = BleedVein; break;
                case StructureKind.Nerve or StructureKind.MotorNerve or StructureKind.SpinalCord: f = WoundFlags.NerveHit; bleed = 0; break;
                case StructureKind.SensoryNerve: f = WoundFlags.SensoryNerveHit; bleed = 0; break;
                case StructureKind.Ligament: f = WoundFlags.LigamentHit; bleed = 0; break;
                default: f = WoundFlags.TendonHit; bleed = 0; break;
            }
            wounds.Add(new Wound(part.Id, s.Tissue, s.Id, WoundKind.Cut, Fixed.One, Rate(bleed), f));
        }
    }

    private static void ConsumePen(ref double pen, ref bool edged, double thicknessMm)
    {
        pen -= thicknessMm;
        if (pen <= 0) { pen = 0; edged = false; }
    }

    // ---- one layer: DF edged test, blunt deflect, blunt test, else permanent blunt ----

    private static LayerOutcome TestLayer(WeaponStats w, AttackDef atk, LayerProps layer, double qa, double area, bool isArmour,
        ref double m, ref bool edged)
    {
        if (edged && w.EdgeFactor > 0 && w.Head.ShearYield > 0 && w.Head.ShearFracture > 0)
        {
            double rSY = layer.ShearYield / w.Head.ShearYield;
            double rSF = layer.ShearFracture / w.Head.ShearFracture;
            double need = (rSY + (area + 1.0) * rSF) * (10.0 + 2.0 * qa) / w.EdgeFactor;
            if (m >= need) { m *= LayerMomentumKept; return LayerOutcome.Cut; }
        }
        // blunt for this layer
        if (isArmour && 2.0 * w.VolumeCm3 * w.Head.ImpactYieldMpa < area * layer.DensityGcm3)
            return LayerOutcome.Deflected;
        double bluntNeed = layer.BluntBase * (2.0 + 0.4 * qa) * area;
        if (m >= bluntNeed) { m *= LayerMomentumKept; return LayerOutcome.Crushed; }

        // failed both: permanently blunt, momentum × strain-at-yield / 50000
        double strain = edged ? layer.ShearStrain : layer.ImpactStrain;
        m *= Math.Min(1.0, strain / StrainReference);
        edged = false;
        return LayerOutcome.Stopped;
    }

    private static bool IsBone(string tissueId) => tissueId.StartsWith("bone", StringComparison.Ordinal);

    private static double CutBleed(string tissueId) => tissueId switch
    {
        "muscle" => BleedMuscle,
        "skin" or "fat" or "hair" or "cartilage" => BleedCapillary,
        _ => 0,
    };

    private static Fixed PermilleFixed(double fraction) => Fixed.FromRatio((int)(fraction * 1000.0), 1000);
    private static Fixed Rate(double mlPerS) => Fixed.FromRatio((int)(mlPerS * 10.0), 10);
}
