using Ravensguard.Content;
using Ravensguard.Sim.Goods;

namespace Ravensguard.Sim.Production;

/// <summary>A recipe's standard pattern (P7-ES2, economies-of-scale 4.6), resolved from <c>scale.jsonc</c>: a run of up to <see cref="RunMax"/> identical pieces takes <c>setup + k x piece</c> minutes, quality capped at <see cref="Cap"/>.</summary>
public sealed class RecipePattern
{
    public int SetupMin, PieceMin, RunMax;
    public ItemQuality Cap = ItemQuality.Common;
}

/// <summary>Munition-grade patterns, pure rules (4.6): the hours of a run, the practice a business earns, the quality cap. Integer arithmetic only.</summary>
public static class Patterns
{
    /// <summary>The pattern row of a recipe, resolved (null when it has none).</summary>
    public static RecipePattern? Of(ScaleSettingsDef s, string recipeId)
    {
        foreach (var row in s.Patterns)
        {
            if (row.Recipe != recipeId) continue;
            if (row.RunMax <= 0 || row.SetupH < 0 || row.PieceH <= 0) throw new InvalidDataException($"scale_settings: pattern '{recipeId}' needs a positive run_max and piece_h");
            var cap = Enum.TryParse<ItemQuality>(row.QualityCap.Length > 0 ? row.QualityCap : s.PatternQualityCap, true, out var q) ? q : ItemQuality.Common;
            return new RecipePattern { SetupMin = (int)Math.Round(row.SetupH * 60), PieceMin = (int)Math.Round(row.PieceH * 60), RunMax = row.RunMax, Cap = cap };
        }
        return null;
    }

    /// <summary>The minutes of a run of <paramref name="pieces"/> pieces: the setup once, then each piece (<c>piece_h x practice / 1,000</c> with practice). ES-11: 20 spearheads 1,800 minutes (30 h, 1.5 each); with practice 1,584 (26.4 h, 1.32 each).</summary>
    public static long RunMinutes(RecipePattern p, long pieces, bool practised, int practicePm) => p.SetupMin + pieces * p.PieceMin * (practised ? practicePm : 1000) / 1000;

    /// <summary>
    /// The batch's <c>worker-hours x scale permille</c> (the form the hourly work uses for plain recipes, <c>worker_hours x scale</c>): the setup is paid whole, the pieces are <c>run_max x scale / 1,000</c> of them,
    /// each at the piece hours (less with practice).
    /// </summary>
    public static long HoursTimesScale(RecipePattern p, int scalePm, bool practised, int practicePm)
    {
        long minutesX1000 = (long)p.SetupMin * 1000 + (long)p.RunMax * p.PieceMin * (practised ? practicePm : 1000) / 1000 * scalePm;
        return minutesX1000 / 60;
    }

    /// <summary>A run's whole worker-hours at full scale, rounded up: the figure a recipe row carries as its <c>worker_hours</c> (the price solver and the planner read it).</summary>
    public static int FullHours(RecipePattern p) => (int)((RunMinutes(p, p.RunMax, false, 1000) + 59) / 60);

    /// <summary>A pattern's quality never exceeds its cap (4.6): a master's masterwork spearhead from a run is common.</summary>
    public static ItemQuality Capped(RecipePattern p, ItemQuality q) => q > p.Cap ? p.Cap : q;
}
