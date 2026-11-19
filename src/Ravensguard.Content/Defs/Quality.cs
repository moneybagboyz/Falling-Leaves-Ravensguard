namespace Ravensguard.Content;

/// <summary>
/// DF item quality (DF2014:Item quality). The armour multiplier Qa is the to-hit / armour-deflect column: 1, 1.2, 1.4, 1.6, 1.8, 2 masterwork, 3 artifact. The weapon's
/// edge multiplier Qw is the sharpness column relative to a standard item's 50%: 1, 1.2, 1.4, 1.6, 1.8, 2 masterwork, and 2 for an artifact too (an artifact's edge is the
/// material's maximum edge, as a masterwork's is: Material science, "2x for masterwork (or artifact)"; the x3 is Qa only).
/// </summary>
public enum Quality { Standard = 0, WellCrafted = 1, FinelyCrafted = 2, Superior = 3, Exceptional = 4, Masterwork = 5, Artifact = 6 }

public static class QualityTable
{
    private static readonly double[] Mult = [1.0, 1.2, 1.4, 1.6, 1.8, 2.0, 3.0];
    private static readonly double[] Edge = [1.0, 1.2, 1.4, 1.6, 1.8, 2.0, 2.0];
    public const int Count = 7;
    /// <summary>Qa, the armour quality multiplier (an artifact x3).</summary>
    public static double Multiplier(Quality q) => Mult[(int)q];
    /// <summary>Qw, the weapon's edge multiplier (sharpness over a standard item's; an artifact's edge is a masterwork's, x2).</summary>
    public static double EdgeMultiplier(Quality q) => Edge[(int)q];
}
