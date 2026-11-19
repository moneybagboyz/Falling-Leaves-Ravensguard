namespace Ravensguard.Sim.Combat.Resolver2;

/// <summary>
/// What a blow does to the worn pieces it met (resolver-v2 4.3 wear, close-out item 4): condition lost (a blunt blow dents a rigid shell, so its protection falls with the
/// condition multiplier), mail's rings burst or cut open at the struck zone (the piece covers less there), a battered shell's strap broken (it hangs loose). Applied by the fight to
/// the fighter's pieces, by the weapon matrix to its harness, and (condition only) by <see cref="ClothingArmour.ApplyWear"/> to the items of a clothing book.
/// </summary>
public static class ArmourWear
{
    /// <summary>Applies the blow's wear to the pieces. Calls <paramref name="told"/> (piece, harm) once per piece that took visible damage.</summary>
    public static int Apply(WornV2[] worn, BlowResult r, Action<int, ArmourHarm>? told = null)
    {
        int n = 0;
        foreach (var s in r.StepSpan)
        {
            if (!s.IsArmour || s.Piece < 0 || s.Piece >= worn.Length || s.WearPm <= 0 && s.Harm == ArmourHarm.None) continue;
            ref var w = ref worn[s.Piece];
            var tiers = Goods.QualityTiers.Default;
            w.ConditionPm = Math.Max(0, w.ConditionPm - tiers.ScaleWear(s.WearPm, tiers.OfCombatPm(w.QualityPm)));     // P5-A1: a fine piece takes 800 pm of the wear, a crude one 1,600
            var harm = s.Harm;
            if (harm == ArmourHarm.Burst)
            {
                ulong bit = s.Zone >= 0 && s.Zone < 64 ? 1UL << s.Zone : 0;
                if ((w.BreachMask & bit) != 0) harm = ArmourHarm.None;           // already open there
                w.BreachMask |= bit;
            }
            else if (harm == ArmourHarm.Strap) { if (w.Slipped) harm = ArmourHarm.None; w.Slipped = true; }
            n++;
            if (harm != ArmourHarm.None) told?.Invoke(s.Piece, harm);
        }
        return n;
    }
}
