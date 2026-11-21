namespace Ravensguard.Sim.Core;

public enum PlagueSeverity : byte { Normal = 0, Off = 1, Rare = 2, Frequent = 3 }
public enum EconomyHarshness : byte { Normal = 0, Lenient = 1, Harsh = 2 }

/// <summary>
/// The honest named sliders (difficulty-sliders.md): each names what it changes and changes only that; the default record is the game as calibrated. Nothing reads the hero's success, wealth or
/// deaths to move any of them. Plain integers, all multipliers in percent. <see cref="AgingPace"/> is a world setting (history uses it), so it is fixed when the world is made.
/// </summary>
public sealed record Difficulty
{
    public static readonly int[] WoundChoices = [100, 85, 70, 130];
    public static readonly int[] BanditChoices = [100, 50, 150, 200];
    public static readonly int[] PaceChoices = [1, 2, 3, 4];
    public static readonly int[] IndustryOutputChoices = [100, 200, 300, 400, 500];
    /// <summary>The six values every save has held since Phase 4: the minimum an array must have to be read at all (a shorter one reads as the default).</summary>
    public const int ValueCount = 6;
    /// <summary>P7-0A: with the seventh value (<see cref="IndustryOutputPct"/>).</summary>
    public const int ValueCountP7 = 7;

    /// <summary>Bleed rate at a wound's creation and the infection chance per day, percent of the calibrated value.</summary>
    public int WoundLethalityPct { get; init; } = 100;
    /// <summary>Ambush, toll and road-block encounter weights (and the bandit and slaver spawns when they exist), percent.</summary>
    public int BanditPct { get; init; } = 100;
    public EconomyHarshness Economy { get; init; } = EconomyHarshness.Normal;
    public PlagueSeverity Plague { get; init; } = PlagueSeverity.Normal;
    public bool Ironman { get; init; }
    /// <summary>1 (historical), 2, 3 or 4: the ageing clock only; per-year vital chances stay per game year.</summary>
    public int AgingPace { get; init; } = 1;
    /// <summary>P7-0A (overview 8.1): industry output, 100 to 500 percent in steps of 100 (the shortfall of a hand-built works against the sheets). Read by the industry stage runtime (P7-IN1).</summary>
    public int IndustryOutputPct { get; init; } = 100;

    public static readonly Difficulty Default = new();
    public bool IsDefault => this == Default;

    // ---- the readers: wound lethality, bandits, economy (coin, harvest variance, spoilage), plague (one accessor pair), ageing (PersonTable) ----
    public int OriginCoinPct => Economy switch { EconomyHarshness.Lenient => 150, EconomyHarshness.Harsh => 75, _ => 100 };
    public int HarvestVariancePct => Economy switch { EconomyHarshness.Lenient => 50, EconomyHarshness.Harsh => 150, _ => 100 };
    public int SpoilagePct => Economy switch { EconomyHarshness.Lenient => 75, EconomyHarshness.Harsh => 125, _ => 100 };
    public int ScaleOriginCoin(int coin) => (int)((long)coin * OriginCoinPct / 100);

    /// <summary>The great plague's start chance a continent-year, in permille x10 (1% = 100, so 0.5% = 50): Normal 1%, Off 0, Rare 0.5%, Frequent 2.5%.</summary>
    public int PlagueStartPermilleX10 => Plague switch { PlagueSeverity.Off => 0, PlagueSeverity.Rare => 50, PlagueSeverity.Frequent => 250, _ => 100 };
    /// <summary>Case fatality percent after the slider (Rare x0.5, Frequent x1.5), capped at 90.</summary>
    public int PlagueFatalityPct(int basePct) => Math.Min(90, basePct * (Plague == PlagueSeverity.Rare ? 50 : Plague == PlagueSeverity.Frequent ? 150 : 100) / 100);

    /// <summary>The visible values in a fixed order (wound index, bandit index, economy, plague, ironman, pace, then the industry output index only when it is not the default 100, so a world that never touched it keeps its bytes) for saves and the library.</summary>
    public int[] ToArray() => [Array.IndexOf(WoundChoices, WoundLethalityPct), Array.IndexOf(BanditChoices, BanditPct), (int)Economy, (int)Plague, Ironman ? 1 : 0, AgingPace, .. IndustryOutputPct == 100 ? Array.Empty<int>() : new[] { Array.IndexOf(IndustryOutputChoices, IndustryOutputPct) }];

    public static Difficulty FromArray(int[]? v)
    {
        if (v == null || v.Length < ValueCount) return Default;
        return new Difficulty
        {
            WoundLethalityPct = WoundChoices[Math.Clamp(v[0], 0, WoundChoices.Length - 1)], BanditPct = BanditChoices[Math.Clamp(v[1], 0, BanditChoices.Length - 1)],
            Economy = (EconomyHarshness)Math.Clamp(v[2], 0, 2), Plague = (PlagueSeverity)Math.Clamp(v[3], 0, 3), Ironman = v[4] != 0, AgingPace = Math.Clamp(v[5], 1, 4),
            IndustryOutputPct = v.Length > ValueCount ? IndustryOutputChoices[Math.Clamp(v[6], 0, IndustryOutputChoices.Length - 1)] : 100,      // a six-value array (every save before Phase 7) reads as 100
        };
    }

    /// <summary>The same record with every value snapped to a legal choice.</summary>
    public Difficulty Normalised() => FromArray(ToArray().Select((x, i) => x < 0 ? 0 : x).ToArray());
}

/// <summary>One recorded change of a slider outside Ironman: when, and the values after it.</summary>
public sealed record DifficultyChange(long Sec, int[] Values);
