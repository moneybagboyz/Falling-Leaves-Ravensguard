using Ravensguard.Sim.Combat.Resolver2;

namespace Ravensguard.Sim.Combat.Wounds;

/// <summary>
/// The medic's choices (wounds 4.3 "AI use"): what a helper does for a patient next, in the order that saves the most blood first (the 30-second game): a tourniquet on a limb
/// that spurts, a bandage (else a pressure hold) on the worst bleeder, then, once nothing bleeds fast, the set and splint of a broken bone, stitches and cleaning.
/// <see cref="Score"/> is the utility score <c>Treat</c> gets: bleeding allies in 6 m, with a bonus for the <c>first_aid</c> proficiency.
/// </summary>
public static class TreatAi
{
    /// <summary>A rate (ml/s x 10) at or above which a limb wound is worth a tourniquet: veins, arteries and severances, not a muscle cut.</summary>
    public const int TourniquetBleedX10 = 100;

    public static int Score(int bleedingAlliesNear, int firstAidPm) => bleedingAlliesNear <= 0 ? 0 : Math.Min(1000, 500 + 150 * Math.Min(3, bleedingAlliesNear) + firstAidPm / 10);

    /// <summary>The topmost part of the limb chain that holds <paramref name="part"/>, or -1 when it is on the trunk or head.</summary>
    public static int LimbRoot(BodyPlanV2 plan, int part)
    {
        var p = plan.Parts[part]; if (p.IsHosted && p.Host >= 0) { part = p.Host; p = plan.Parts[part]; }
        if (p.Limb == null) return -1;
        int root = part;
        while (plan.Parts[root].Parent >= 0 && plan.Parts[plan.Parts[root].Parent].Limb == p.Limb && plan.Parts[plan.Parts[root].Parent].Side == p.Side) root = plan.Parts[root].Parent;
        return root;
    }

    /// <summary>The next treatment a helper with <paramref name="items"/> and <paramref name="skill"/> gives, or null when there is nothing worth doing.</summary>
    public static TreatRequest? Next(WoundBook patient, BodyPlanV2 plan, ITreatItems items, long now, int skill, ulong actor)
    {
        if (patient.Dead) return null;
        int i = Treatment.WorstBleeder(patient, -1, now);
        if (i >= 0)
        {
            var w = patient.Records[i];
            int root = LimbRoot(plan, w.Part);
            if (root >= 0 && w.BleedX10 >= TourniquetBleedX10 && (items.Has("rope", 1) || items.Has("bandage", 2))) return new TreatRequest(TreatKind.Tourniquet, root, now, skill, actor);
            if (items.Has("bandage", 1)) return new TreatRequest(TreatKind.Bandage, w.Part, now, skill, actor);
            return new TreatRequest(TreatKind.Pressure, w.Part, now, skill, actor);
        }
        foreach (var w in patient.Records)
        {
            if (w.Is(Aid.Resolved) || w.State == WoundState.Scar) continue;
            if (w.Cat == WoundCat.Bone && w.Grade >= 2 && !w.Is(Aid.Set) && !w.Is(Aid.SetFailed)) return new TreatRequest(TreatKind.SetBone, w.Part, now, skill, actor);
            if (w.Cat == WoundCat.Bone && w.Grade >= 1 && !w.Is(Aid.Splinted) && items.Has("splint", 1)) return new TreatRequest(TreatKind.Splint, w.Part, now, skill, actor);
            if (w.Cat is WoundCat.Skin or WoundCat.Muscle && w.Kind == WoundKind.Cut && w.DepthPm >= 500 && w.State != WoundState.Stitched && skill >= 10 && items.Has("needle_and_thread", 1)) return new TreatRequest(TreatKind.Stitch, w.Part, now, skill, actor);
            if (w.InfectionLevel > 0) return new TreatRequest(TreatKind.Clean, w.Part, now, skill, actor);
        }
        return null;
    }
}
