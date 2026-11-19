using Ravensguard.Content;

namespace Ravensguard.Sim.Combat.Melee2;

/// <summary>
/// [P4 close-out] Weather in combat, the rest of the 4.8 table (exploration-and-sites; <c>combat/weather_mods.jsonc</c>): with a <see cref="WeatherRow"/> (an outdoor fight, its row set from the
/// live weather by the game, saved in the fight record) the fight reads the dodge cooldown, the footing and evasion points, the walking speed, the stamina regeneration and the heat's cost of
/// heavy armour, the bow's range and the vision cap (a shot at a target the shooter cannot see clearly flies twice as wide), alongside the ranged sigma and the fog's surprise of step 7.
/// With no row every helper returns its input, so other fights and their hashes are unchanged.
/// </summary>
public sealed partial class FightWorld
{
    /// <summary>The row of a weather state name ("" or unknown: none). The setup's old words "rain", "storm" and "snow" name the heavy-rain, thunderstorm and snow rows.</summary>
    public static WeatherCombatDef? WeatherRowOf(ContentDatabase db, string state)
    {
        if (state.Length == 0) return null;
        string s = state switch { "rain" => "HeavyRain", "storm" => "Thunderstorm", "snow" => "Snow", _ => state };
        foreach (var w in db.All<WeatherCombatDef>()) if (w.State == s) return w;
        return null;
    }

    /// <summary>The extra ticks a dodge's cooldown lasts (wet and muddy ground: 4.8).</summary>
    internal int WeatherDodgeTicks() => WeatherRow?.DodgeCooldownTicks ?? 0;

    /// <summary>Walking and sprinting speed under the weather (snow x0.8, a blizzard x0.6).</summary>
    internal int WeatherSpeed(int mmps) => WeatherRow is { SpeedPm: not 1000 } w ? (int)((long)mmps * w.SpeedPm / 1000) : mmps;

    /// <summary>Stamina regeneration under the weather (a blizzard x0.8; heat over the armour limit its own factor).</summary>
    internal int WeatherRegen(Fighter f, int regenX100)
    {
        if (WeatherRow is not { } w) return regenX100;
        long r = (long)regenX100 * w.StaminaRegenPm / 1000;
        if (w.HeavyArmourKg > 0 && f.ArmourG > w.HeavyArmourKg * 1000) r = r * w.HeavyRegenPm / 1000;
        return (int)r;
    }

    /// <summary>A swing's stamina cost under the weather (heat in heavy armour x1.1).</summary>
    internal long WeatherSwing(Fighter f, long costX100) => WeatherRow is { HeavyArmourKg: > 0 } w && f.ArmourG > w.HeavyArmourKg * 1000 ? costX100 * w.HeavySwingCostPm / 1000 : costX100;

    /// <summary>Points of the weather on a blow's skill delta: the defender's footing for evading (rain -5: the attacker gains) and the attacker's own grip (frost -5).</summary>
    internal int WeatherPoints() => WeatherRow is { } w ? w.GripPoints - w.EvadePoints : 0;

    /// <summary>A launcher's range under the weather (heavy rain x0.85).</summary>
    internal int WeatherRange(int rangeMm) => WeatherRow is { BowRangePm: not 1000 } w ? (int)((long)rangeMm * w.BowRangePm / 1000) : rangeMm;

    /// <summary>The ranged sigma beyond the weather's vision cap (heavy rain 60 m, fog 15 m, a blizzard 10 m): twice as wide, the target is only a shape; permille.</summary>
    internal int WeatherVisionPm(long rangeMm) => WeatherRow is { VisionCapM: > 0 } w && rangeMm > w.VisionCapM * 1000L ? 2000 : 1000;

    /// <summary>The weather joins the <c>FightHash</c> only when a row is set.</summary>
    internal ulong WeatherHash(ulong h)
    {
        if (WeatherRow is not { } w) return h;
        foreach (char ch in w.State) h = FightMath.Mix(h, ch);
        return h;
    }
}
