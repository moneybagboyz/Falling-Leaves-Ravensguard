using Ravensguard.Content;

namespace Ravensguard.Sim.Combat.Resolver2;

/// <summary>One part's state, summarised from its wounds (anatomy 3.5): graded muscle, bone, worst joint, and the permanent nerve and cord facts.</summary>
public struct PartState
{
    public MuscleGrade Muscle;
    /// <summary>Muscle wounds on the part (each beyond the first subtracts 100 pm of strength).</summary>
    public byte MuscleWounds;
    public BoneGrade Bone;
    public JointGrade Joint;
    public bool Severed, Paralysed, Numb, SkinOpen, Destroyed, TendonCut;
    /// <summary>Fingers or fifths of teeth lost.</summary>
    public byte Lost;
    /// <summary>A partial cord crush left this part 400 pm.</summary>
    public bool Residual;
    public bool Splinted;
}

/// <summary>Aids that change the derived capacities: a crutch, splints (bit per part index), ligament surgery (bit per part index).</summary>
public readonly record struct BodyAids(bool Crutch = false, ulong SplintedMask = 0, ulong RepairedMask = 0);

/// <summary>
/// The derived capacities of a body (anatomy 3.6): a pure function of the plan and the wound list, recomputed when a wound changes, never per tick.
/// All values permille. Side index 0 = left, 1 = right; legs are counted per limb chain (two for a human, four for a horse).
/// </summary>
public sealed class BodyState
{
    public const int CordNeckUnconsciousS = 30, CordNeckDeadS = 240;
    private static readonly int[] MuscleStrength = [1000, 750, 400, 0];

    public required BodyPlanV2 Plan { get; init; }
    public required PartState[] Parts { get; init; }
    public int Sight, Hearing, Breath, Speech, Balance, WalkSpeedPm, Lameness;
    public readonly int[] Arm = new int[2], Grip = new int[2], BlowMomentumPm = [1000, 1000], ShieldCoverDeltaPm = new int[2];
    public readonly bool[] WeaponHand = new bool[2], Overhead = [true, true];
    /// <summary>Walk capacity per leg chain, in the plan's leg order.</summary>
    public int[] Walk { get; init; } = [];
    /// <summary>A complete cut of the cord in the neck: breathing stops (unconscious in 30 s, dead in 240 s).</summary>
    public bool BreathStopped;
    public bool Concussed;
    /// <summary>A monster's animating core is destroyed (the draugr is undone).</summary>
    public bool Undone;
    public bool Bleeds => Plan.Bleeds;
    public bool FeelsPain => Plan.FeelsPain;

    /// <summary>Pain multiplier for a wound on this part, permille: 0 where a cut sensory nerve took the feeling (or the plan feels no pain).</summary>
    public int PainPm(int part) => !Plan.FeelsPain || Parts[part].Numb ? 0 : Plan.Parts[part].PainPm;

    public static BodyState From(BodyPlanV2 plan, ReadOnlySpan<WoundV2> wounds, BodyAids aids = default, IReadOnlyList<bool>? burnedThrough = null)
    {
        var parts = new PartState[plan.Parts.Length];
        for (int i = 0; i < parts.Length; i++) parts[i].Splinted = (aids.SplintedMask & (1UL << i)) != 0;
        bool breathStopped = false, concussed = false;
        int lungs = 0;
        foreach (var w in wounds)
        {
            ref var ps = ref parts[w.Part];
            var part = plan.Parts[w.Part];
            if (w.Has(WoundFlags.Severed)) { ps.Severed = true; ps.Lost = (byte)Math.Min(4, ps.Lost + Math.Max((byte)1, w.Count)); continue; }
            if (w.Has(WoundFlags.Concussion)) concussed = true;
            if (w.Kind == WoundKind.Burn && w.DepthPm >= 1000) ps.Destroyed = true;
            if (w.Organ >= 0)
            {
                if (part.Organs[w.Organ].Id.StartsWith("lung", StringComparison.Ordinal)) lungs++;
                continue;
            }
            if (w.Structure >= 0)
            {
                var st = part.Structures[w.Structure];
                switch (st.Kind)
                {
                    case StructureKind.MotorNerve or StructureKind.Nerve: foreach (var d in st.Distal) parts[d].Paralysed = true; break;
                    case StructureKind.SensoryNerve: foreach (var d in st.Distal) parts[d].Numb = true; break;
                    case StructureKind.Tendon: ps.TendonCut = true; break;
                    case StructureKind.Ligament:
                        if ((aids.RepairedMask & (1UL << w.Part)) == 0 && w.Joint > ps.Joint) ps.Joint = w.Joint;
                        break;
                    case StructureKind.SpinalCord:
                        if (w.Kind == WoundKind.Cut && w.DepthPm < 500) break;     // a nick, not a cut through
                        Cord(plan, parts, st.Id, w.Kind == WoundKind.Crush ? w.Residual : (byte)0);
                        if (st.Id == "cord_neck" && w.Kind == WoundKind.Cut && w.DepthPm >= 1000) breathStopped = true;
                        break;
                }
                continue;
            }
            var layerRole = RoleOf(plan, w);
            if (layerRole == TissueRole.Muscle)
            {
                ps.MuscleWounds++;
                if ((MuscleGrade)w.Grade > ps.Muscle) ps.Muscle = (MuscleGrade)w.Grade;
            }
            else if (layerRole == TissueRole.Bone)
            {
                if ((BoneGrade)w.Grade > ps.Bone) ps.Bone = (BoneGrade)w.Grade;
                if (part.BaseId == "teeth") ps.Lost = (byte)Math.Min(5, ps.Lost + Math.Max((byte)1, w.Count));
            }
            else if (w.Kind == WoundKind.Cut && layerRole == TissueRole.Soft) ps.SkinOpen = true;
            // an eye struck through is lost
            if (part.FunctionPm("sight_pm") > 0 && (w.Kind == WoundKind.Cut || w.Kind == WoundKind.Crush)) ps.Destroyed = true;
        }
        if (burnedThrough != null) for (int i = 0; i < parts.Length && i < burnedThrough.Count; i++) if (burnedThrough[i]) parts[i].Destroyed = true;
        // a severed part takes everything distal and its hosted parts with it
        for (int i = 0; i < parts.Length; i++) if (parts[i].Severed) MarkLost(plan, parts, i);

        var legs = plan.Parts.Where(p => p.Limb == "leg" && !p.IsHosted && (p.Parent < 0 || plan.Parts[p.Parent].Limb != "leg")).Select(p => p.Index).ToArray();
        var bs = new BodyState { Plan = plan, Parts = parts, Walk = new int[legs.Length], BreathStopped = breathStopped, Concussed = concussed };

        // senses
        bs.Sight = Channel(plan, parts, "sight_pm");
        bs.Hearing = Channel(plan, parts, "hearing_pm");

        // arms and grip, per side
        for (int side = 0; side < 2; side++)
        {
            int sd = side + 1;
            int arm = 1000;
            bool any = false;
            foreach (var p in plan.Parts)
            {
                if (p.Limb != "arm" || p.Side != sd || p.IsHosted || p.BaseId == "hand") continue;
                any = true;
                var ps = parts[p.Index];
                arm = Math.Min(arm, Strength(ps));
                if (ps.Severed || ps.Paralysed && !ps.Residual) arm = 0;
                if (ps.Bone >= BoneGrade.Broken && !ps.Splinted) arm = 0;
                if (ps.Joint == JointGrade.Ruptured) arm = 0;
                else if (ps.Joint == JointGrade.Torn) arm = arm * (p.BaseId == "upper_arm" ? 600 : 700) / 1000;
                if (ps.Residual) arm = Math.Min(arm, 400);
            }
            if (!any) arm = 0;
            bs.Arm[side] = arm;

            int grip = 0;
            var hand = plan.Parts.FirstOrDefault(p => p.Limb == "arm" && p.Side == sd && p.BaseId == "hand");
            if (hand != null)
            {
                var hs = parts[hand.Index];
                bool tendon = false, motor = hs.Paralysed && !hs.Residual;
                var fore = hand.Parent >= 0 ? parts[hand.Parent] : default;
                if (fore.TendonCut) tendon = true;
                int thumb = 0, fingers = 0, lostFingers = 0;
                foreach (var hi in hand.Hosted)
                {
                    var h = plan.Parts[hi]; var st = parts[hi];
                    if (h.BaseId == "thumb") thumb = st.Severed || st.Bone >= BoneGrade.Broken ? 0 : h.FunctionPm("grip_pm");
                    if (h.BaseId == "fingers") { fingers = h.FunctionPm("grip_pm"); lostFingers = Math.Min(4, st.Lost + (st.Bone >= BoneGrade.Broken ? 2 : 0)); if (st.Severed && st.Lost == 0) lostFingers = 4; }
                }
                if (hand.Hosted.Count == 0) { thumb = 300; fingers = 700; }
                grip = hs.Severed || tendon || motor ? 0 : (int)((long)arm * (thumb + fingers * (4 - lostFingers) / 4) / 1000);
                if (hs.Joint == JointGrade.Ruptured) grip = 0; else if (hs.Joint == JointGrade.Torn) grip = grip * 700 / 1000;
                grip = grip * Strength(hs) / 1000;
                // sensory: blows struck with that hand lose 15%
                foreach (var p in plan.Parts) if (p.Limb == "arm" && p.Side == sd && parts[p.Index].Numb) { bs.BlowMomentumPm[side] = 850; break; }
            }
            bs.Grip[side] = grip;
            bs.WeaponHand[side] = grip >= 400 && arm >= 400;
            var cb = plan.Parts.FirstOrDefault(p => p.BaseId == "collarbone" && p.Side == sd);
            if (cb != null && parts[cb.Index].Bone >= BoneGrade.Broken) { bs.Overhead[side] = false; bs.ShieldCoverDeltaPm[side] = -150; }
        }

        // legs
        int unstable = 0, missingLegs = 0, zero = 0, lame = 0;
        bool pelvis = false;
        foreach (var p in plan.Parts)
        {
            var ps = parts[p.Index];
            if (ps.Joint >= JointGrade.Torn) unstable++;
            if (p.BaseId == "pelvis" && ps.Bone >= BoneGrade.Broken) pelvis = true;
        }
        for (int li = 0; li < legs.Length; li++)
        {
            int walk = 1000; bool missing = false;
            foreach (int pi in Chain(plan, legs[li]))
            {
                var p = plan.Parts[pi]; var ps = parts[pi];
                if (p.IsHosted)
                {
                    if (p.BaseId == "kneecap" && ps.Bone >= BoneGrade.Broken) walk = 0;
                    continue;
                }
                walk = Math.Min(walk, Strength(ps));
                if (ps.Severed) { walk = 0; missing = true; }
                if (ps.Paralysed && !ps.Residual) walk = 0;
                if (ps.Residual) walk = Math.Min(walk, 400);
                if (ps.Bone >= BoneGrade.Broken) walk = ps.Splinted ? Math.Min(walk, 400) : 0;
                else if (ps.Bone == BoneGrade.Cracked) walk = walk * 600 / 1000;
                if (ps.Joint == JointGrade.Ruptured) walk = 0; else if (ps.Joint == JointGrade.Torn) walk = walk * 700 / 1000;
                if (ps.TendonCut && p.Parent >= 0 && plan.Parts[p.Parent].Limb == "leg") lame = 3;
                else if (ps.Muscle >= MuscleGrade.Torn) lame = Math.Max(lame, 1);
            }
            if (missing) missingLegs++;
            if (walk == 0) zero++;
            bs.Walk[li] = walk;
        }
        if (legs.Length == 0) bs.WalkSpeedPm = 1000;
        else if (legs.Length >= 4) bs.WalkSpeedPm = zero > 0 ? 0 : bs.Walk.Min();          // a horse on three legs is down
        else if (pelvis || zero >= 2) bs.WalkSpeedPm = 100;                                  // crawl
        else if (zero == 1) bs.WalkSpeedPm = aids.Crutch ? 550 : 250;                        // hop
        else bs.WalkSpeedPm = bs.Walk.Min();
        bs.Lameness = lame;

        // breath, speech, balance
        int brokenRibs = 0;
        foreach (var p in plan.Parts) if (p.BaseId == "ribs" && parts[p.Index].Bone >= BoneGrade.Broken) brokenRibs++;
        bs.Breath = Math.Max(0, 1000 - 250 * brokenRibs - 500 * lungs);
        int speech = 1000, teeth = 0;
        foreach (var p in plan.Parts)
        {
            if (p.BaseId == "jaw" && parts[p.Index].Bone >= BoneGrade.Broken) speech = 500;
            if (p.BaseId == "teeth") teeth = parts[p.Index].Lost;
        }
        bs.Speech = Math.Max(0, speech - 40 * teeth);
        bs.Balance = Math.Max(0, 1000 - 150 * unstable - 300 * missingLegs);

        // the animating core (draugr): head severed, skull shattered or burnt through; or the upper body severed or both rib sides shattered
        if (plan.AnimatingCore >= 0)
        {
            var core = plan.Parts[plan.AnimatingCore];
            var cs = parts[core.Index];
            bool undone = cs.Severed || cs.Destroyed;
            foreach (int sh in core.Shells) if (parts[sh].Bone == BoneGrade.Shattered) undone = true;
            if (cs.Bone == BoneGrade.Shattered) undone = true;
            foreach (var p in plan.Parts)
            {
                if (p.BaseId == "upper_body" && parts[p.Index].Severed) undone = true;
                if (p.Shell && p.BaseId == "ribs" && plan.Parts[p.Host].Shells.All(s => parts[s].Bone == BoneGrade.Shattered)) undone = true;
            }
            bs.Undone = undone;
        }
        return bs;
    }

    private static TissueRole RoleOf(BodyPlanV2 plan, in WoundV2 w)
    {
        var part = plan.Parts[w.Part];
        foreach (var l in part.Layers) if (l.Tissue.Index == w.Tissue) return l.Role;
        if (part.Shell) return TissueRole.Bone;
        return TissueRole.Other;
    }

    private static int Strength(in PartState ps) =>
        Math.Max(0, MuscleStrength[(int)ps.Muscle] - (ps.MuscleWounds > 1 ? 100 * (ps.MuscleWounds - 1) : 0));

    private static int Channel(BodyPlanV2 plan, PartState[] parts, string channel)
    {
        int sum = 0; bool any = false;
        foreach (var p in plan.Parts)
        {
            int v = p.FunctionPm(channel);
            if (v <= 0) continue;
            any = true;
            if (!parts[p.Index].Destroyed && !parts[p.Index].Severed) sum += v;
        }
        return any ? sum : 1000;
    }

    private static IEnumerable<int> Chain(BodyPlanV2 plan, int root)
    {
        yield return root;
        foreach (var h in plan.Parts[root].Hosted) yield return h;
        foreach (var c in plan.Parts[root].Children) foreach (var x in Chain(plan, c)) yield return x;
    }

    private static void MarkLost(BodyPlanV2 plan, PartState[] parts, int i)
    {
        foreach (var h in plan.Parts[i].Hosted) { parts[h].Severed = true; }
        foreach (var c in plan.Parts[i].Children) { parts[c].Severed = true; MarkLost(plan, parts, c); }
    }

    /// <summary>The spinal cord (anatomy 3.6): the neck takes all four limbs, the upper back the legs and the lower body, the lower back the legs.</summary>
    private static void Cord(BodyPlanV2 plan, PartState[] parts, string cordId, byte residual)
    {
        bool arms = cordId == "cord_neck";
        foreach (var p in plan.Parts)
        {
            bool leg = p.Limb == "leg", arm = p.Limb == "arm";
            bool hit = leg || (arms && arm) || (cordId == "cord_back_u" && p.BaseId == "lower_body");
            if (!hit) continue;
            int group = arm ? (p.Side == 2 ? 1 : 0) : (p.Side == 2 ? 3 : 2);
            parts[p.Index].Paralysed = true;
            if ((residual & (1 << group)) != 0) parts[p.Index].Residual = true;
            foreach (var h in p.Hosted) parts[h].Paralysed = true;
        }
    }

    /// <summary>The BodyHash of the daily trail (anatomy 6): FNV-1a over every part state.</summary>
    public ulong Hash(ulong h = 14695981039346656037UL)
    {
        unchecked
        {
            foreach (var p in Parts)
            {
                int v = (int)p.Muscle | p.MuscleWounds << 2 | (int)p.Bone << 8 | (int)p.Joint << 11 | (p.Severed ? 1 << 13 : 0) | (p.Paralysed ? 1 << 14 : 0)
                    | (p.Numb ? 1 << 15 : 0) | (p.SkinOpen ? 1 << 16 : 0) | (p.Destroyed ? 1 << 17 : 0) | (p.TendonCut ? 1 << 18 : 0) | p.Lost << 19 | (p.Residual ? 1 << 24 : 0);
                for (int i = 0; i < 4; i++) { h ^= (byte)(v >> (8 * i)); h *= 1099511628211UL; }
            }
            foreach (var v in new[] { Sight, Hearing, Breath, Speech, Balance, WalkSpeedPm, Arm[0], Arm[1], Grip[0], Grip[1] })
                for (int i = 0; i < 4; i++) { h ^= (byte)(v >> (8 * i)); h *= 1099511628211UL; }
        }
        return h;
    }
}
