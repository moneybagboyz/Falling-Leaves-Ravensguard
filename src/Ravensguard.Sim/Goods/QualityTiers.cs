using Ravensguard.Content;

namespace Ravensguard.Sim.Goods;

/// <summary>
/// The one quality scale (items-quality-and-wear 3, P5-A1) as integer arrays indexed by <see cref="ItemQuality"/>: combat Qw and Qa, value, the wear an item takes and the best a repair
/// restores, all permille, and the crafting score each tier starts at. <see cref="Default"/> is the base content's row (<c>goods/quality.jsonc</c>) and what the static rules read
/// (<see cref="ItemQualityRules"/>, <see cref="ItemMath"/>); the combat tables take the loaded row (<see cref="From"/>). Any new effect of quality is added here (overview 8.8).
/// </summary>
public sealed class QualityTiers
{
    public const int Count = 5;
    public int[] CombatPm { get; }
    public int[] ValuePm { get; }
    public int[] WearPm { get; }
    public int[] RepairCapPm { get; }
    public int[] ScoreFrom { get; }

    private QualityTiers(int[] combat, int[] value, int[] wear, int[] cap, int[] score) { CombatPm = combat; ValuePm = value; WearPm = wear; RepairCapPm = cap; ScoreFrom = score; }

    /// <summary>The base scale: combat 800/900/1000/1400/2000 (DF's standard, fine and masterwork, with crude and poor below its floor), value 500/800/1000/1600/3000, wear
    /// 1600/1250/1000/800/600, repair cap 700/800/900/950/1000, scores from 0/15/40/70/90. Common is 1,000 everywhere, so no Phase 3 or 4 number moves.</summary>
    public static QualityTiers Default { get; } = new([800, 900, 1000, 1400, 2000], [500, 800, 1000, 1600, 3000], [1600, 1250, 1000, 800, 600], [700, 800, 900, 950, 1000], [0, 15, 40, 70, 90]);

    /// <summary>The content's row (the first by id), each array of five; <see cref="Default"/> when the content has none or a row is malformed.</summary>
    public static QualityTiers From(ContentDatabase? db)
    {
        var d = db?.All<QualityTiersDef>().OrderBy(x => x.Id, StringComparer.Ordinal).FirstOrDefault();
        if (d == null || d.CombatPm.Count != Count || d.ValuePm.Count != Count || d.WearPm.Count != Count || d.RepairCapPm.Count != Count || d.ScoreFrom.Count != Count) return Default;
        return new([.. d.CombatPm], [.. d.ValuePm], [.. d.WearPm], [.. d.RepairCapPm], [.. d.ScoreFrom]);
    }

    public int Combat(ItemQuality q) => CombatPm[Index(q)];
    public int Value(ItemQuality q) => ValuePm[Index(q)];
    public int Wear(ItemQuality q) => WearPm[Index(q)];
    public int RepairCap(ItemQuality q) => RepairCapPm[Index(q)];
    private static int Index(ItemQuality q) => Math.Clamp((int)q, 0, Count - 1);

    /// <summary>The tier a crafting score reaches (before the masterwork inspiration roll).</summary>
    public ItemQuality FromScore(int score)
    {
        for (int t = Count - 1; t > 0; t--) if (score >= ScoreFrom[t]) return (ItemQuality)t;
        return ItemQuality.Crude;
    }

    /// <summary>The tier whose combat multiplier is <paramref name="combatPm"/> (a worn layer keeps Qa only); Common for a value off the scale.</summary>
    public ItemQuality OfCombatPm(int combatPm)
    {
        for (int t = 0; t < Count; t++) if (CombatPm[t] == combatPm) return (ItemQuality)t;
        return ItemQuality.Common;
    }

    /// <summary>
    /// Wear taken (4.1): a wear amount times the tier's <c>wear_pm / 1000</c>, rounded down, at least 1 when the base was at least 1 (a fine breastplate takes 8 of a 10 pm blow, a crude one 16).
    /// </summary>
    public int ScaleWear(int basePm, ItemQuality q)
    {
        if (basePm <= 0) return basePm;
        return Math.Max(1, (int)((long)basePm * Wear(q) / 1000));
    }

    /// <summary>
    /// The repair cap (4.5): <c>min(cap[item tier], cap[maker tier] + 50)</c>, the maker's tier being <see cref="FromScore"/> of the repairer's effective skill (no roll). A fine sword
    /// (950) re-edged at skill 45 (common: 900 + 50) is 950; at skill 20 (poor: 800 + 50) 850.
    /// </summary>
    public int RepairCapFor(ItemQuality item, int repairerSkill) => Math.Min(RepairCap(item), RepairCap(FromScore(repairerSkill)) + 50);

    /// <summary>
    /// The edge a weapon cuts with (4.1): Qw times <c>condition_factor = (400 + 600 x condition / 1000) / 1000</c>, edged attacks only. A dull blade at condition 500 cuts at 700 pm of its
    /// edge, a ruined one at 400; at full condition (all Phase 4 kit) Qw is unchanged.
    /// </summary>
    public static int EdgePm(int qwPm, int conditionPm)
    {
        int cond = Math.Clamp(conditionPm, 0, 1000);
        return (int)((long)qwPm * ConditionFactorPm(cond) / 1000);
    }

    /// <summary><c>400 + 600 x condition / 1000</c>, permille.</summary>
    public static int ConditionFactorPm(int conditionPm) => 400 + 600 * Math.Clamp(conditionPm, 0, 1000) / 1000;

    // ------------------------------------------------------------------ masterwork inspiration (4.2)

    /// <summary>The masterwork chance of a score of 90 or more, permille: <c>clamp(50 + 10 x (score - 90), 50, 300)</c>; 0 below 90.</summary>
    public static int InspirePm(int score) => score < 90 ? 0 : Math.Clamp(50 + 10 * (score - 90), 50, 300);

    /// <summary>
    /// The crafted tier (4.2): <see cref="FromScore"/>, but a score that reaches Masterwork gives it only when the inspiration roll (a draw 0..999 of stream 0x530) is under
    /// <see cref="InspirePm"/>; otherwise Fine. A master (skill 75, best tools +10) makes Masterwork 4.9%, Fine 82.9%, Common 12.2% of weapons (IQ-5).
    /// </summary>
    public ItemQuality Crafted(int score, int inspireDraw)
    {
        var t = FromScore(score);
        if (t == ItemQuality.Masterwork && inspireDraw >= InspirePm(score)) return ItemQuality.Fine;
        return t;
    }

    // ------------------------------------------------------------------ the repair nibble and the value of repairs (4.1)

    /// <summary>The repair count kept in the top four bits of an item row's <c>Wear</c> (the low twelve bits are the fraction its own wear clock carries).</summary>
    public static int RepairCount(ushort wear) => wear >> 12;
    public static ushort WithRepairCount(ushort wear, int count) => (ushort)((Math.Clamp(count, 0, 15) << 12) | (wear & 0x0FFF));
    public static int WearFraction(ushort wear) => wear & 0x0FFF;
    public static ushort WithWearFraction(ushort wear, int fraction) => (ushort)((wear & 0xF000) | (Math.Clamp(fraction, 0, 0x0FFF)));

    /// <summary>Each repair takes 100 pm off the value, at most 300 (Phase 3 wrote it; Phase 5 applies it).</summary>
    public static int RepairValuePm(ushort wear) => 1000 - Math.Min(300, 100 * RepairCount(wear));
}
