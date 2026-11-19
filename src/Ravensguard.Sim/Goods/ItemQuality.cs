using Ravensguard.Sim.Core;

namespace Ravensguard.Sim.Goods;

/// <summary>
/// The one quality scale of the game (items-quality-and-wear, P5-A1): crafting, value, wear, repair and combat all read it through <see cref="QualityTiers"/>. The byte values are saved
/// (item rows, the fight record), so the order never changes. There is no tier above Masterwork: a legendary piece is a named masterwork (<see cref="ItemFlags.Named"/>).
/// </summary>
public enum ItemQuality : byte { Crude, Poor, Common, Fine, Masterwork }

public static class ItemQualityRules
{
    /// <summary>Value multiplier in permille: 0.5, 0.8, 1.0, 1.6, 3.0.</summary>
    public static int ValuePermille(ItemQuality q) => QualityTiers.Default.Value(q);

    /// <summary>Thresholds 15 / 40 / 70 / 90 on the crafting score.</summary>
    public static ItemQuality FromScore(int score) => QualityTiers.Default.FromScore(score);

    /// <summary><c>score = skill + Range(-20, 21) + tool_bonus</c> (stream 0x223, key = crafter id, draw = output serial). tool_bonus is -10..+10.</summary>
    public static int Score(ulong seed, long tick, EntityId crafter, uint outputSerial, int skill, int toolBonus)
        => skill + DetRng.Range(seed, tick, crafter.Value, RngStreams.CraftQuality, outputSerial, -20, 21) + Math.Clamp(toolBonus, -10, 10);

    /// <summary>The masterwork inspiration draw 0..999 of a crafted unit (stream 0x530, key crafter, job id x 64 + unit).</summary>
    public static int InspireDraw(ulong seed, long tick, EntityId crafter, uint jobUnit) => DetRng.Range(seed, tick, crafter.Value, RngStreams.MasterworkInspiration, jobUnit, 0, 1000);

    /// <summary>The crafted tier of a unit: the score's tier, a Masterwork only on the inspiration roll (items-quality-and-wear 4.2).</summary>
    public static ItemQuality Crafted(ulong seed, long tick, EntityId crafter, uint jobUnit, int score)
        => QualityTiers.Default.Crafted(score, score >= 90 ? InspireDraw(seed, tick, crafter, jobUnit) : 0);
}
