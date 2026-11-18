using Ravensguard.Sim.Core;

namespace Ravensguard.Sim.Goods;

/// <summary>The one quality scale (items spec 3.2): Crude, Poor, Common, Fine, Masterwork.</summary>
public enum ItemQuality : byte { Crude, Poor, Common, Fine, Masterwork }

public static class ItemQualityRules
{
    /// <summary>Value multiplier in permille: 0.5, 0.8, 1.0, 1.6, 3.0.</summary>
    public static int ValuePermille(ItemQuality q) => q switch
    {
        ItemQuality.Crude => 500, ItemQuality.Poor => 800, ItemQuality.Common => 1000, ItemQuality.Fine => 1600, _ => 3000,
    };

    /// <summary>Thresholds 15 / 40 / 70 / 90 on the crafting score.</summary>
    public static ItemQuality FromScore(int score) => score < 15 ? ItemQuality.Crude : score < 40 ? ItemQuality.Poor : score < 70 ? ItemQuality.Common : score < 90 ? ItemQuality.Fine : ItemQuality.Masterwork;

    /// <summary><c>score = skill + Range(-20, 21) + tool_bonus</c> (stream 0x223, key = crafter id, draw = output serial). tool_bonus is -10..+10.</summary>
    public static int Score(ulong seed, long tick, EntityId crafter, uint outputSerial, int skill, int toolBonus)
        => skill + DetRng.Range(seed, tick, crafter.Value, 0x223, outputSerial, -20, 21) + Math.Clamp(toolBonus, -10, 10);
}
