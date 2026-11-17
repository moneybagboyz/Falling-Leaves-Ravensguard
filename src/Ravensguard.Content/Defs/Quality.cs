namespace Ravensguard.Content;

/// <summary>DF item quality. Multipliers: 1 standard, 1.4 fine, 2 masterwork, 3 artifact (Material science page).</summary>
public enum Quality { Standard = 0, WellCrafted = 1, FinelyCrafted = 2, Superior = 3, Exceptional = 4, Masterwork = 5, Artifact = 6 }

public static class QualityTable
{
    private static readonly double[] Mult = [1.0, 1.2, 1.4, 1.6, 1.8, 2.0, 3.0];
    public const int Count = 7;
    /// <summary>Qw (weapon sharpness) and Qa (armour) multiplier.</summary>
    public static double Multiplier(Quality q) => Mult[(int)q];
}
