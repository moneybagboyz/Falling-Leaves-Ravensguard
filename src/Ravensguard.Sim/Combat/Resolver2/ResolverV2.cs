using Ravensguard.Content;
using Ravensguard.Sim.Core;

namespace Ravensguard.Sim.Combat.Resolver2;

/// <summary>
/// Resolver v2 (docs/specs/phase4/resolver-v2.md): one blow through momentum, block, hit chance, coverage and the four worn layers, the
/// tissues, the structures inside the muscle, the hosted parts, the organs behind the bone, and severing. Pure and integer-only: all chance comes
/// from counter draws keyed by <see cref="BlowKey"/> in a fixed order (hit 0x410, redirect 0x411, coverage 0x412 outside in, structures 0x413
/// in part order with insets after, organs 0x414, artery rate 0x415, cord residual 0x416), recorded in the result for test RS-22.
/// <para>What v2 changes from v1 (the spike's findings, docs/spikes/02-melee.md): a cut costs 0.6 of the layer's need instead of always keeping
/// 95% (C1); bone resists an edge x2.5 and a crush x5, and an edge that fails bone meets the crush test at half its ratio (C2); padding
/// absorbs a crushed blow (C3); a part is severed only when the blow is still edged after the bone, the reach left after armour exceeds the
/// part's depth and 30% of the starting momentum remains (C4); a failed edge turns the blow blunt for good.</para>
/// </summary>
public sealed class ResolverV2(CombatTablesV2 tables)
{
    public CombatTablesV2 Tables { get; } = tables;
    private CalibrationV2 C => Tables.Cal;

    public const int BleedCapillaryX10 = 5, BleedMuscleX10 = 30, BleedMuscleCrushX10 = 10, BleedVeinX10 = 150, BleedSeveredX10 = 900;

    // ------------------------------------------------------------------ momentum and hit chance

    /// <summary><c>M = M0 x skill x style (x2 prone) x MomentumPm + extras</c>, clamped to [0, 2,000,000] milli (resolver-v2 4.1 step 1).</summary>
    public long InitialMomentumMilli(in StrikeV2 s)
    {
        if (s.Missile) return s.ExtraMomentumMilli < 0 ? 0 : s.ExtraMomentumMilli > C.MaxMomentumMilli ? C.MaxMomentumMilli : s.ExtraMomentumMilli;
        var atk = s.Weapon.Attacks[s.Attack];
        long m = LayerTest.BaseMomentumMilli(s.SpeciesSizeCm3, s.Strength, atk.VelPm, s.Weapon.MassMg, s.SizeCm3);
        int skill = s.Skill < 0 ? 0 : s.Skill > 100 ? 100 : s.Skill;
        m = LayerTest.MulPm(m, 1000 + 10 * skill);
        m = LayerTest.MulPm(m, s.Style switch { AttackStyle.Quick => 500, AttackStyle.Heavy => 1500, _ => 1000 });
        if (s.Prone) m *= 2;
        if (s.MomentumPm != 1000 && s.MomentumPm > 0) m = LayerTest.MulPm(m, s.MomentumPm);
        m += s.ExtraMomentumMilli;
        return m < 0 ? 0 : m > C.MaxMomentumMilli ? C.MaxMomentumMilli : m;
    }

    /// <summary>
    /// Hit chance in basis points on the zone aimed at, before any redirect (resolver-v2 4.1 step 3):
    /// <c>(200 + 800 x size / largest) x (500 + 5 x delta) / 10^6</c>, x0.6 guarded, x0.35 aimed at a named gap, x1.25 flank, x1.5 rear, clamp
    /// [2%, 98%]; a hosted part is the host's chance x its aim factor. The delta adds <see cref="StrikeV2.SituationPoints"/> and, from the front,
    /// the plan's target bonus (a horse is a large target).
    /// </summary>
    public int HitChanceBp(in StrikeV2 s)
    {
        var plan = s.Plan;
        var part = plan.Parts[s.TargetPart];
        int zoneIndex = part.IsHosted ? part.Host : part.Index;
        var zone = plan.Parts[zoneIndex];
        long zf = 200_000 + 800_000L * zone.RelPpm / plan.LargestPpm;
        int delta = s.Skill - s.DefenderSkill + s.SituationPoints + (s.Facing == Facing.Front ? plan.TargetBonusPoints : 0);
        long p = zf * (500 + 5L * delta) / 100_000;
        if ((s.GuardedMask & (1UL << zoneIndex)) != 0) p = p * C.GuardedPm / 1000;
        if (!part.IsHosted && GapOn(s, zone)) p = p * C.GapPm / 1000;
        if (s.Facing == Facing.Flank) p = p * C.FlankPm / 1000;
        else if (s.Facing == Facing.Rear) p = p * C.RearPm / 1000;
        p = Clamp(p);
        if (part.IsHosted) p = Clamp(p * part.AimFactorPm / 1000);
        return (int)p;
    }

    private long Clamp(long p) => p < C.HitMinBp ? C.HitMinBp : p > C.HitMaxBp ? C.HitMaxBp : p;

    /// <summary>The blow is aimed at a named gap that lies on this zone (blunt blows never slip through a gap).</summary>
    private static bool GapOn(in StrikeV2 s, PartV2 zone) =>
        s.TargetGap >= 0 && s.TargetGap < s.Plan.Gaps.Length && s.Plan.Gaps[s.TargetGap].OnBase(zone.BaseIndex)
        && s.Weapon.Attacks[s.Attack].Type != AttackType.Blunt && !s.Blunted;

    // ------------------------------------------------------------------ entry points

    /// <summary>Full resolution: block or dodge pre-step, hit roll, a 50% redirect of a miss, then the blow (resolver-v2 4.1).</summary>
    public void Resolve(in StrikeV2 s, in BlowKey key, BlowResult r)
    {
        r.Reset();
        long m0 = InitialMomentumMilli(s);
        r.InitialMilli = m0;
        if (s.Block.Kind != BlockKind.None)
        {
            var bo = Blocks.Resolve(s, key, m0, C, r);
            r.Block = bo;
            if (bo.Outcome != LayerOutcome.NotCovered)
            {
                r.Blocked = bo.Outcome == LayerOutcome.Blocked; r.Dodged = bo.Outcome == LayerOutcome.Dodged; r.Parried = bo.Outcome == LayerOutcome.Parried;
                r.Add(new LayerStep { Piece = -1, Tissue = -1, Part = -1, Outcome = bo.Outcome, InMilli = m0, OutMilli = bo.ExcessMilli });
                if (bo.Stopped) { r.FinalMilli = 0; return; }
                // the excess goes on, blunt, into the blocking forearm (no hit roll: the shield arm is where the blow is)
                Blow(s, key, r, s.Block.ArmPart, bo.ExcessMilli, bo.ExcessMilli, forceBlunt: true, gap: -1);
                return;
            }
        }
        if ((s.MissingMask & (1UL << s.TargetPart)) == 0 && Draw(key, r, RngStreams.HitRoll, 0, 0, 10_000) < HitChanceBp(s))
        {
            Blow(s, key, r, s.TargetPart, m0, m0, false, s.TargetGap);
            return;
        }
        if (Draw(key, r, RngStreams.Redirect, 0, 0, 1000) < C.RedirectPm)
        {
            var plan = s.Plan; var t = plan.Parts[s.TargetPart];
            int other = -1;
            if (t.IsHosted) other = t.Host;                                   // a missed hosted-part shot lands on the host
            else
            {
                int n = plan.NeighbourCount(t.Index);
                if (n > 0) other = plan.Neighbour(t.Index, Draw(key, r, RngStreams.Redirect, 1, 0, n));
            }
            if (other >= 0 && (s.MissingMask & (1UL << other)) == 0)
            {
                r.Redirected = true;
                Blow(s, key, r, other, m0, m0, false, -1);
                return;
            }
        }
        r.Hit = false; r.FinalMilli = 0;
    }

    /// <summary>A blow known to land on <see cref="StrikeV2.TargetPart"/> (projectile paths, thrown objects, the layer goldens): no hit roll.</summary>
    public void ResolveImpact(in StrikeV2 s, in BlowKey key, BlowResult r)
    {
        r.Reset();
        long m0 = InitialMomentumMilli(s);
        r.InitialMilli = m0;
        Blow(s, key, r, s.TargetPart, m0, m0, false, s.TargetGap);
    }

    /// <summary>A fall (resolver-v2 4.9): a blunt blow of area 40 with the fall's momentum on the landing part; armour absorbs as for any blunt blow.</summary>
    public void ResolveFall(in StrikeV2 s, long momentumMilli, in BlowKey key, BlowResult r)
    {
        r.Reset();
        r.InitialMilli = momentumMilli;
        Blow(s, key, r, s.TargetPart, momentumMilli, momentumMilli, true, -1, fallArea: 40_000);
    }

    // ------------------------------------------------------------------ the blow

    private static int Draw(in BlowKey key, BlowResult r, uint stream, int k, int min, int maxExclusive)
    {
        r.Drew(stream, k);
        return DetRng.Range(key.Seed, key.FightTick, key.Attacker, stream, key.Draw(k), min, maxExclusive);
    }

    private int Bleed(BodyPlanV2 plan, int x10) => plan.Bleeds ? (int)((long)x10 * plan.BleedScalePm / 1000) : 0;

    private void Blow(in StrikeV2 s, in BlowKey key, BlowResult r, int partIndex, long m, long mStart, bool forceBlunt, int gap, long fallArea = 0)
    {
        var plan = s.Plan;
        var part = plan.Parts[partIndex];
        if (part.Shell) part = plan.Parts[part.Host];                        // a shell is the host's bone layer
        var host = part.IsHosted ? plan.Parts[part.Host] : part;
        r.Hit = true; r.Part = part.Index;

        AttackType type; long area, pen;
        if (fallArea > 0) { type = AttackType.Blunt; area = fallArea; pen = 0; }
        else { var a = s.Weapon.Attacks[s.Attack]; type = a.Type; area = a.AreaMilli; pen = a.PenUm; }
        bool edged = !forceBlunt && !s.Blunted && type != AttackType.Blunt;
        if (!edged) pen = 0;
        // the named gap aimed at: explicit, or a hosted part's exposure gap (a called shot at an eye goes through the face)
        if (gap < 0 && part.IsHosted && part.ExposureGap >= 0) gap = part.ExposureGap;
        bool gapHere = edged && gap >= 0 && plan.Gaps[gap].OnBase(host.BaseIndex);
        long partSizeMilli = (long)s.DefenderSizeCm3 * host.RelPpm / 1000;

        // ---- worn pieces, outside in: cover, armour, over, under (layer, then order, then shape) ----
        bool armourCaught = false;
        int n = s.Worn == null ? 0 : Math.Min(s.WornCount, s.Worn.Length);
        Span<int> rank = stackalloc int[n];
        for (int i = 0; i < n; i++) rank[i] = i;
        for (int i = 1; i < n; i++)
        {
            int v = rank[i], j = i - 1;
            while (j >= 0 && Outer(s.Worn![v], v, s.Worn![rank[j]], rank[j])) { rank[j + 1] = rank[j]; j--; }
            rank[j + 1] = v;
        }
        for (int k = 0; k < n && m > 0; k++)
        {
            int wi = rank[k];
            ref var w = ref s.Worn![wi];
            int cov = w.Stats.CoverageByPlan[plan.Index][host.BaseIndex];
            // a breastplate is not met by a blow from behind, a backplate not by one from the front (from the flank both)
            if (cov <= 0 || w.Stats.Side == 1 && s.Facing == Facing.Rear || w.Stats.Side == 2 && s.Facing == Facing.Front) continue;
            if (!Fit.Fits(w.FitCm3, s.DefenderSizeCm3)) cov -= C.MisfitCoveragePm;
            if ((w.BreachMask & (1UL << host.BaseIndex)) != 0) cov = cov * (1000 - C.BreachCoveragePm) / 1000;     // burst or cut-open rings
            if (w.Slipped) cov = cov * C.SlipCoveragePm / 1000;                                                    // a broken strap
            bool covered = Draw(key, r, RngStreams.Coverage, k, 0, 1000) < cov;
            long mIn = m;
            var step = new LayerStep { Piece = (short)wi, Tissue = (short)w.Stats.Material.Index, Part = (short)part.Index, IsArmour = true, InMilli = mIn, Zone = (short)host.BaseIndex };
            if (!covered) { step.Outcome = LayerOutcome.NotCovered; step.OutMilli = m; r.Add(step); continue; }
            if (gapHere && w.Stats.Layer == ArmourLayer.Armour
                && ((w.Stats.GapMaskByPlan[plan.Index] & (1UL << gap)) != 0 || (w.Stats.Shape.Visor && w.VisorOpen && plan.Gaps[gap].Name == "face")))
            { step.Outcome = LayerOutcome.Bypassed; step.OutMilli = m; r.Add(step); continue; }

            long a = Math.Min(area, partSizeMilli * cov / 1000);
            int cm = LayerTest.ConditionPm(w.ConditionPm);
            var st = w.Stats;
            LayerOutcome res;
            if (edged)
            {
                long need = LayerTest.CutNeedMilli(st.ShearYield, st.ShearFracture, s.Weapon.Head, s.Weapon.QwPm, a, w.QualityPm, cm, 1000);
                step.NeedMilli = need;
                if (m >= need)
                {
                    m = LayerTest.AfterCut(m, need, C);
                    pen -= st.ThicknessUm;
                    if (pen <= 0) { pen = 0; edged = false; }
                    res = LayerOutcome.Cut; step.WearPm = st.WearCutPm;
                    if (st.Elastic && st.Material.Category == "metal") step.Harm = ArmourHarm.Burst;      // the rings are cut: the mail is open there
                }
                else res = BluntArmour(s, st, w.QualityPm, cm, a, true, fallArea > 0, ref m, ref step);
                if (res != LayerOutcome.Cut) edged = false;                     // the edge failed: the blow is blunt for good
            }
            else res = BluntArmour(s, st, w.QualityPm, cm, a, false, fallArea > 0, ref m, ref step);
            step.Outcome = res; step.OutMilli = m;
            if (st.Rigid && step.Harm is ArmourHarm.Dented or ArmourHarm.Buckled && !w.Slipped && w.ConditionPm - step.WearPm < C.StrapBelowPm) step.Harm = ArmourHarm.Strap;  // battered loose
            r.Add(step);
            if (res == LayerOutcome.Deflected) { r.Deflected = true; m = 0; r.FinalMilli = 0; return; }
            armourCaught = true;
        }
        long penAfterArmour = edged ? pen : 0;

        // ---- body tissues ----
        var layers = part.Layers;
        int woundPart = part.Index;
        bool allCut = edged, softBreached = true, insetsRolled = part.IsHosted, boneCut = false, edgedAtBone = false;
        long penAtBone = 0, mAfterBone = 0;
        var boneGrade = BoneGrade.Sound; bool boneReached = false;
        int width = LayerTest.WidthPm(area);
        int gapAimed = gapHere && !part.IsHosted ? gap : -1;
        for (int li = 0; li < layers.Length; li++)
        {
            var L = layers[li];
            if (m <= 0) { allCut = false; break; }
            if (!insetsRolled && !L.IsSoft)
            {
                insetsRolled = true;
                if (softBreached) RollInsets(s, key, r, host, m, edged, pen, area, armourCaught);
            }
            bool wasEdged = edged;
            long mIn = m, penIn = pen;
            var res = TestTissue(s, L, Math.Min(area, partSizeMilli), ref m, ref edged, ref pen, out long need, out long ratioPm, out int depthPm);
            if (fallArea > 0 && res == LayerOutcome.Cut) res = LayerOutcome.Crushed;
            r.Add(new LayerStep { Piece = -1, Tissue = (short)L.Tissue.Index, Part = (short)part.Index, Outcome = res, InMilli = mIn, OutMilli = m, NeedMilli = need });
            if (res != LayerOutcome.Cut) allCut = false;
            if (L.IsSoft && res is not (LayerOutcome.Cut or LayerOutcome.Crushed)) softBreached = false;

            if (L.IsBone)
            {
                boneReached = true; edgedAtBone = wasEdged; penAtBone = penIn; mAfterBone = m;
                int bonePart = woundPart;
                if (!part.IsHosted && part.Shells.Count == 1) bonePart = part.Shells[0];
                else if (!part.IsHosted && part.Shells.Count > 1) bonePart = part.Shells[Draw(key, r, RngStreams.StructureRoll, 62, 0, part.Shells.Count)];
                var w = new WoundV2 { Part = (short)bonePart, Tissue = (short)L.Tissue.Index, Structure = -1, Organ = -1 };
                if (res == LayerOutcome.Cut) { boneCut = true; w.Kind = WoundKind.Cut; w.DepthPm = depthPm; boneGrade = depthPm >= 1000 ? BoneGrade.Broken : BoneGrade.Cracked; }
                else if (res == LayerOutcome.Crushed) { w.Kind = WoundKind.Crush; w.DepthPm = 1000; boneGrade = LayerTest.Fracture(ratioPm, C); }
                else { w.Kind = WoundKind.Crush; w.DepthPm = (int)Math.Min(999, ratioPm); boneGrade = BoneGrade.Sound; }  // bruised bone
                w.Grade = (byte)boneGrade;
                if (boneGrade >= BoneGrade.Displaced) w.Flags |= WoundFlags.Displaced;
                if (boneGrade == BoneGrade.Shattered) w.Flags |= WoundFlags.Compound;
                if (boneGrade >= BoneGrade.Broken) w.Flags |= WoundFlags.BoneBroken;
                if (boneGrade >= BoneGrade.Broken && plan.Parts[bonePart].BaseId == "skull") w.Flags |= WoundFlags.Concussion;
                if (part.IsHosted) w.Flags |= WoundFlags.InsetHit;
                r.Add(w);
                if (res == LayerOutcome.Crushed && boneGrade >= BoneGrade.Broken && !part.IsHosted) JointWrench(s, key, r, part, boneGrade, width);
                if (res == LayerOutcome.Crushed && boneGrade == BoneGrade.Shattered && !part.IsHosted) CordCrush(s, key, r, part);
                continue;
            }

            if (res == LayerOutcome.Cut)
            {
                var w = new WoundV2 { Part = (short)woundPart, Tissue = (short)L.Tissue.Index, Structure = -1, Organ = -1, Kind = WoundKind.Cut, DepthPm = depthPm };
                if (L.IsMuscle) { w.Grade = (byte)(depthPm < 500 ? MuscleGrade.Bruised : MuscleGrade.Torn); w.BleedX10 = Bleed(plan, BleedMuscleX10); }
                else w.BleedX10 = Bleed(plan, BleedCapillaryX10);
                if (part.IsHosted) w.Flags |= WoundFlags.InsetHit;
                r.Add(w);
                if (L.IsMuscle && !part.IsHosted)
                {
                    RollStructures(s, key, r, part, depthPm, width, gapAimed);
                    RollOrgans(s, key, r, part, area, type, beforeBone: true, mAfterBone: m, viaBlunt: false, boneGrade: BoneGrade.Sound);
                }
            }
            else if (res == LayerOutcome.Crushed || (res == LayerOutcome.Stopped && L.IsMuscle))
            {
                var w = new WoundV2 { Part = (short)woundPart, Tissue = (short)L.Tissue.Index, Structure = -1, Organ = -1, Kind = WoundKind.Crush, DepthPm = 1000 };
                if (L.IsMuscle)
                {
                    // graded by the momentum that passed through the muscle (table 4.4: mangled at 101.2 out of it under mail, torn at 86.5 under a mace)
                    w.Grade = (byte)(res == LayerOutcome.Stopped || m < C.MuscleBruisedBelowMilli ? MuscleGrade.Bruised
                        : m < C.MuscleTornBelowMilli ? MuscleGrade.Torn : MuscleGrade.Mangled);
                    w.BleedX10 = res == LayerOutcome.Crushed ? Bleed(plan, BleedMuscleCrushX10) : 0;
                }
                if (part.IsHosted) w.Flags |= WoundFlags.InsetHit;
                r.Add(w);
            }
        }
        if (!insetsRolled && softBreached) RollInsets(s, key, r, host, m, edged, pen, area, armourCaught);

        // ---- severing (rule C4): still edged after every layer including bone, reach left after armour >= the part's depth, 30% of M0 left ----
        if (part.Severable && type == AttackType.Edge && edged && allCut && penAfterArmour >= part.DepthUm && m * 1000 >= mStart * C.SeverMomentumPm)
        {
            var w = new WoundV2
            {
                Part = (short)part.Index, Tissue = (short)layers[^1].Tissue.Index, Structure = -1, Organ = -1, Kind = WoundKind.Cut, DepthPm = 1000,
                BleedX10 = part.IsHosted ? Bleed(plan, BleedCapillaryX10 * 4) : Bleed(plan, BleedSeveredX10), Flags = WoundFlags.Severed,
            };
            if (part.IsHosted) { w.Flags |= WoundFlags.InsetHit; w.Count = (byte)Math.Min(4, 1 + area / 20_000); }
            r.Add(w);
            r.Severed = true;
            r.FinalMilli = m;
            return;                                                             // organs of a severed part are lost with it
        }

        // ---- organs behind the bone (4.7) ----
        if (!part.IsHosted && part.Organs.Length > 0 && boneReached)
        {
            bool reach = false, viaBlunt = false;
            if (boneCut) reach = true;
            else if (type == AttackType.Point && edgedAtBone && part.RibSlip && penAtBone >= C.RibSlipPenUm)
            {
                if (Draw(key, r, RngStreams.StructureRoll, 63, 0, 1000) < C.RibSlipPm) { reach = true; r.RibSlip = true; }
            }
            else if (boneGrade >= BoneGrade.Displaced && part.Shells.Count > 0) { reach = true; viaBlunt = true; }
            if (reach) RollOrgans(s, key, r, part, area, type, beforeBone: false, mAfterBone: mAfterBone, viaBlunt: viaBlunt, boneGrade: boneGrade);
        }
        r.FinalMilli = m;
    }

    private static bool Outer(in WornV2 a, int ai, in WornV2 b, int bi)
    {
        if (a.Stats.Layer != b.Stats.Layer) return a.Stats.Layer > b.Stats.Layer;
        if (a.Stats.Order != b.Stats.Order) return a.Stats.Order > b.Stats.Order;
        if (a.Stats.ShapeIndex != b.Stats.ShapeIndex) return a.Stats.ShapeIndex < b.Stats.ShapeIndex;
        return ai < bi;
    }

    /// <summary>
    /// A worn layer tested blunt (or after a failed edge): deflect, crush (padding absorbs), or fail (the strain conversion). Only a rigid shell bounces a light weapon (cloth and
    /// mail give with the blow: a club does not bounce off a shirt or a hauberk), and a failed edge or point skids off a rigid shell (the strain rule) instead of crushing it at its
    /// point's small area (close-out item 2).
    /// Wear (item 4): a blunt blow dents a rigid shell in proportion to its momentum over the crush need (buckled at the need or more); a heavy blow bursts mail's rings.
    /// </summary>
    private LayerOutcome BluntArmour(in StrikeV2 s, ArmourV2 st, int qaPm, int cm, long a, bool failedEdge, bool fall, ref long m, ref LayerStep step)
    {
        if (!fall && st.Rigid && LayerTest.Deflects(s.Weapon, a, st.DensityKgM3)) { step.NeedMilli = 0; return LayerOutcome.Deflected; }
        long need = LayerTest.BluntNeedMilli(st.ImpactFracture, st.ImpactYield, a, qaPm, cm, 1000);
        if (!failedEdge) step.NeedMilli = need;
        long mIn = m;
        step.WearPm = st.WearCrushPm;
        if (m >= need && !(failedEdge && st.Rigid && !fall))
        {
            m = LayerTest.AfterCrush(m, need, st.Elastic, st.BluntAbsorbPm, C);
            if (!fall) ArmourHarmOf(st, mIn, need, failedEdge, ref step);
            return LayerOutcome.Crushed;
        }
        m = LayerTest.AfterFail(m, failedEdge ? st.ShearStrain : st.ImpactStrain, C);
        if (!fall) ArmourHarmOf(st, mIn, need, failedEdge, ref step);
        return LayerOutcome.Stopped;
    }

    /// <summary>The blunt blow's damage to the piece: a rigid shell dents by <c>dent_full x min(M / need, 2)</c> (a failed edge only scuffs), buckling at the need; mail bursts at
    /// <c>burst_ratio</c> of its crush need.</summary>
    private void ArmourHarmOf(ArmourV2 st, long mIn, long need, bool failedEdge, ref LayerStep step)
    {
        if (need <= 0) need = 1;
        long ratioPm = Math.Min(2000, mIn * 1000 / need);
        if (st.Rigid && !failedEdge)
        {
            int dent = (int)(C.DentFullPm * ratioPm / 1000);
            if (dent > step.WearPm) step.WearPm = dent;
            step.Harm = ratioPm >= 1000 ? ArmourHarm.Buckled : dent >= C.DentNoticePm ? ArmourHarm.Dented : ArmourHarm.None;
        }
        else if (st.Elastic && st.Material.Category == "metal" && mIn * 1000 / need >= C.BurstRatioPm) { step.Harm = ArmourHarm.Burst; step.WearPm = Math.Max(step.WearPm, st.WearCutPm * 3); }
    }

    /// <summary>
    /// One tissue layer. An edge that clears the need cuts (penetration spent; a short reach leaves a partial depth and ends the edge). An edge
    /// that fails turns the blow blunt for good; on bone it meets the crush test at its ratio over the divisor (rule C2) and spends its force
    /// sliding (the crush momentum rule, not the strain rule); elsewhere the layer is tested blunt.
    /// </summary>
    private LayerOutcome TestTissue(in StrikeV2 s, LayerV2 L, long area, ref long m, ref bool edged, ref long pen, out long need, out long ratioPm, out int depthPm)
    {
        var mat = L.Tissue;
        depthPm = 0; ratioPm = 0;
        if (edged)
        {
            need = LayerTest.CutNeedMilli(mat.ShearYield, mat.ShearFracture, s.Weapon.Head, s.Weapon.QwPm, area, 1000, 1000, mat.CutResistPm);
            if (m >= need)
            {
                m = LayerTest.AfterCut(m, need, C);
                if (pen >= L.ThicknessUm) { depthPm = 1000; pen -= L.ThicknessUm; if (pen <= 0) { pen = 0; edged = false; } }
                else { depthPm = L.ThicknessUm > 0 ? (int)(pen * 1000 / L.ThicknessUm) : 1000; pen = 0; edged = false; }
                return LayerOutcome.Cut;
            }
            edged = false;
            long crush = Math.Max(1, LayerTest.BluntNeedMilli(mat.ImpactFracture, mat.ImpactYield, area, 1000, 1000, mat.CrushResistPm));
            need = crush;
            if (L.IsBone)
            {
                ratioPm = m * 1000 / crush / C.BoneEdgeFailDivisor;
                m = LayerTest.AfterCrush(m, crush, false, mat.BluntAbsorbPm, C);
                return ratioPm >= 1000 ? LayerOutcome.Crushed : LayerOutcome.Stopped;
            }
            ratioPm = m * 1000 / crush;
            if (m >= crush) { m = LayerTest.AfterCrush(m, crush, false, mat.BluntAbsorbPm, C); return LayerOutcome.Crushed; }
            m = LayerTest.AfterFail(m, mat.ShearStrain, C);
            return LayerOutcome.Stopped;
        }
        need = Math.Max(1, LayerTest.BluntNeedMilli(mat.ImpactFracture, mat.ImpactYield, area, 1000, 1000, mat.CrushResistPm));
        ratioPm = m * 1000 / need;
        if (m >= need) { m = LayerTest.AfterCrush(m, need, false, mat.BluntAbsorbPm, C); return LayerOutcome.Crushed; }
        m = LayerTest.AfterFail(m, mat.ImpactStrain, C);
        return LayerOutcome.Stopped;
    }

    // ------------------------------------------------------------------ structures, insets, organs

    /// <summary>Structures inside the cut muscle, in content order (stream 0x413, k = index): <c>min(1, area x width x gap boost x facing)</c>.</summary>
    private void RollStructures(in StrikeV2 s, in BlowKey key, BlowResult r, PartV2 part, int depthPm, int width, int gapAimed)
    {
        var plan = s.Plan;
        int facing = (int)s.Facing;
        for (int si = 0; si < part.Structures.Length; si++)
        {
            var st = part.Structures[si];
            long ch = (long)st.AreaPpm * width / 1000;
            if (gapAimed >= 0 && st.GapBoostPm[gapAimed] > 0) ch = ch * st.GapBoostPm[gapAimed] / 1000;
            ch = ch * st.FacingPm[facing] / 1000;
            if (ch > 1_000_000) ch = 1_000_000;
            if (Draw(key, r, RngStreams.StructureRoll, si, 0, 1_000_000) >= ch) continue;
            var w = new WoundV2 { Part = (short)part.Index, Tissue = (short)st.Tissue.Index, Structure = (short)si, Organ = -1, Kind = WoundKind.Cut, DepthPm = depthPm };
            switch (st.Kind)
            {
                case StructureKind.Artery:
                    w.Flags = WoundFlags.ArteryHit;
                    w.BleedX10 = Bleed(plan, Draw(key, r, RngStreams.BleedRate, si, 80, 121) * 10);
                    break;
                case StructureKind.Vein: w.Flags = WoundFlags.VeinHit; w.BleedX10 = Bleed(plan, BleedVeinX10); break;
                case StructureKind.Tendon: w.Flags = WoundFlags.TendonHit; break;
                case StructureKind.Nerve: w.Flags = WoundFlags.NerveHit; break;
                case StructureKind.MotorNerve: w.Flags = WoundFlags.MotorNerveHit; break;
                case StructureKind.SensoryNerve: w.Flags = WoundFlags.SensoryNerveHit; break;
                case StructureKind.SpinalCord: w.Flags = WoundFlags.SpineHit; break;
                case StructureKind.Ligament:
                    w.Flags = WoundFlags.LigamentHit;
                    w.Grade = (byte)(depthPm < 340 ? JointGrade.Sprained : depthPm < 1000 ? JointGrade.Torn : JointGrade.Ruptured);
                    break;
            }
            r.Add(w);
        }
    }

    /// <summary>A heavy blunt blow that breaks the bone wrenches the part's joint: its ligaments roll (k = 32 + index), graded by the fracture.</summary>
    private static void JointWrench(in StrikeV2 s, in BlowKey key, BlowResult r, PartV2 part, BoneGrade g, int width)
    {
        for (int si = 0; si < part.Structures.Length; si++)
        {
            var st = part.Structures[si];
            if (st.Kind != StructureKind.Ligament) continue;
            long ch = Math.Min(1_000_000, (long)st.AreaPpm * width / 1000);
            if (Draw(key, r, RngStreams.StructureRoll, 32 + si, 0, 1_000_000) >= ch) continue;
            r.Add(new WoundV2
            {
                Part = (short)part.Index, Tissue = (short)st.Tissue.Index, Structure = (short)si, Organ = -1, Kind = WoundKind.Crush, DepthPm = 1000,
                Grade = (byte)(g >= BoneGrade.Shattered ? JointGrade.Ruptured : g >= BoneGrade.Displaced ? JointGrade.Torn : JointGrade.Sprained),
                Flags = WoundFlags.LigamentHit,
            });
        }
    }

    /// <summary>A shattered vertebra crushes the cord there: the same effect as a cut, but each limb group keeps 400 pm with 20% (stream 0x416, drawn once).</summary>
    private static void CordCrush(in StrikeV2 s, in BlowKey key, BlowResult r, PartV2 part)
    {
        for (int si = 0; si < part.Structures.Length; si++)
        {
            var st = part.Structures[si];
            if (st.Kind != StructureKind.SpinalCord) continue;
            byte residual = 0;
            for (int g = 0; g < 4; g++) if (Draw(key, r, RngStreams.HitJitter, g, 0, 1000) < 200) residual |= (byte)(1 << g);
            r.Add(new WoundV2
            {
                Part = (short)part.Index, Tissue = (short)st.Tissue.Index, Structure = (short)si, Organ = -1, Kind = WoundKind.Crush, DepthPm = 1000,
                Residual = residual, Flags = WoundFlags.SpineHit,
            });
        }
    }

    /// <summary>
    /// Hosted parts (eyes, ears, nose, jaw, teeth, collarbones, kneecaps, thumb, fingers) roll once the host's soft layers are breached
    /// (k = structure count + index); one with an exposure gap only when no armour piece caught the blow (4.5). A hit runs the inset's own
    /// layers with the momentum at that depth, beside the main path.
    /// </summary>
    private void RollInsets(in StrikeV2 s, in BlowKey key, BlowResult r, PartV2 host, long m, bool edged, long pen, long area, bool armourCaught)
    {
        if (host.Hosted.Count == 0 || m <= 0) return;
        var plan = s.Plan;
        int width = LayerTest.WidthPm(area);
        for (int hi = 0; hi < host.Hosted.Count; hi++)
        {
            var h = plan.Parts[host.Hosted[hi]];
            if (h.ExposureGap >= 0 && armourCaught) continue;
            long ch = Math.Min(1_000_000, (long)h.AreaPpm * width / 1000);
            if (Draw(key, r, RngStreams.StructureRoll, host.Structures.Length + hi, 0, 1_000_000) >= ch) continue;
            long mm = m, pp = pen; bool ed = edged, allCut = edged;
            long hSize = Math.Max(1000, (long)s.DefenderSizeCm3 * h.RelPpm / 1000);
            long a = Math.Min(area, hSize);
            foreach (var L in h.Layers)
            {
                if (mm <= 0) { allCut = false; break; }
                long mIn = mm;
                var res = TestTissue(s, L, a, ref mm, ref ed, ref pp, out _, out long ratio, out int depth);
                if (res != LayerOutcome.Cut) allCut = false;
                var w = new WoundV2 { Part = (short)h.Index, Tissue = (short)L.Tissue.Index, Structure = -1, Organ = -1, Flags = WoundFlags.InsetHit };
                if (res == LayerOutcome.Cut)
                {
                    w.Kind = WoundKind.Cut; w.DepthPm = depth; w.BleedX10 = Bleed(plan, BleedCapillaryX10);
                    if (L.IsBone) { w.Grade = (byte)(depth >= 1000 ? BoneGrade.Broken : BoneGrade.Cracked); if (depth >= 1000) w.Flags |= WoundFlags.BoneBroken; }
                    if (L.IsMuscle) w.Grade = (byte)(depth < 500 ? MuscleGrade.Bruised : MuscleGrade.Torn);
                }
                else if (res == LayerOutcome.Crushed)
                {
                    w.Kind = WoundKind.Crush; w.DepthPm = 1000;
                    if (L.IsBone) { var g = LayerTest.Fracture(ratio, C); w.Grade = (byte)g; if (g >= BoneGrade.Broken) w.Flags |= WoundFlags.BoneBroken; if (h.BaseId == "teeth") w.Count = (byte)Math.Max(1, (int)g); }
                    if (L.IsMuscle) w.Grade = (byte)(mm < C.MuscleBruisedBelowMilli ? MuscleGrade.Bruised : mm < C.MuscleTornBelowMilli ? MuscleGrade.Torn : MuscleGrade.Mangled);
                }
                else { if (!L.IsBone) break; w.Kind = WoundKind.Crush; w.DepthPm = (int)Math.Min(999, ratio); }
                r.Add(w);
            }
            if (h.Severable && ed && allCut && pp >= 0 && edged && pen >= h.DepthUm && mm * 1000 >= m * C.SeverMomentumPm)
                r.Add(new WoundV2
                {
                    Part = (short)h.Index, Tissue = (short)h.Layers[^1].Tissue.Index, Structure = -1, Organ = -1, Kind = WoundKind.Cut, DepthPm = 1000,
                    BleedX10 = Bleed(plan, BleedCapillaryX10 * 4), Flags = WoundFlags.Severed | WoundFlags.InsetHit, Count = (byte)Math.Min(4, 1 + area / 20_000),
                });
        }
    }

    /// <summary>
    /// Organs (stream 0x414, k = organ index): those in front of the bone (trachea) when the muscle is cut; the rest when the bone was cut, a point
    /// slipped between the ribs, or a blunt blow displaced the shell. Chance <c>area x reach</c>. The brain is destroyed only by a cut through the skull
    /// with 40 left or a shattered skull through the skin; otherwise it is a concussion.
    /// </summary>
    private void RollOrgans(in StrikeV2 s, in BlowKey key, BlowResult r, PartV2 part, long area, AttackType type, bool beforeBone, long mAfterBone, bool viaBlunt, BoneGrade boneGrade)
    {
        var plan = s.Plan;
        int reach = viaBlunt ? 500 : LayerTest.ReachPm(type, area);
        for (int oi = 0; oi < part.Organs.Length; oi++)
        {
            var o = part.Organs[oi];
            if (o.BeforeBone != beforeBone) continue;
            long ch = Math.Min(1_000_000, (long)o.AreaPpm * reach / 1000);
            if (Draw(key, r, RngStreams.OrganRoll, oi, 0, 1_000_000) >= ch) continue;
            var w = new WoundV2
            {
                Part = (short)part.Index, Tissue = (short)o.Tissue.Index, Structure = -1, Organ = (short)oi, DepthPm = 1000,
                Kind = viaBlunt ? WoundKind.Crush : WoundKind.Cut, BleedX10 = Bleed(plan, o.BleedX10), Flags = WoundFlags.OrganHit,
            };
            if (o.Id == "brain")
            {
                bool destroyed = viaBlunt ? boneGrade == BoneGrade.Shattered : mAfterBone >= C.BrainCutMilli;
                if (destroyed) w.Flags |= WoundFlags.Vital; else { w.Flags |= WoundFlags.Concussion; w.Kind = WoundKind.Crush; w.BleedX10 = 0; }
            }
            else if (o.Vital) w.Flags |= WoundFlags.Vital;
            r.Add(w);
        }
    }
}
