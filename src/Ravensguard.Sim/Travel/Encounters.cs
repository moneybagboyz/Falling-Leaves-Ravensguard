using Ravensguard.Content;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Weather;

namespace Ravensguard.Sim.Travel;

/// <summary>The region danger (pm 0..1000) from records (spec 4.1). Pure; recomputed daily per 2 km cell by the caller, never stored.</summary>
public static class RegionDanger
{
    public static int Compute(TravelSettingsDef c, int vagrantsPer1000, bool raidWithin15Days10Km, bool warFrontWithin20Km, bool famineYear, bool night, bool campFireNear = false)
    {
        long d = c.DangerBasePm + (long)c.DangerPerVagrantPer1000 * vagrantsPer1000;
        if (raidWithin15Days10Km) d += c.DangerRaidPm;
        if (warFrontWithin20Km) d += c.DangerWarPm;
        if (famineYear) d += c.DangerFamineYearPm;
        if (night) d += c.DangerNightPm;
        if (campFireNear) d += c.DangerCampFirePm;
        return (int)Math.Clamp(d, 0, 1000);
    }
}

/// <summary>The encounter table: every <see cref="EncounterDef"/> in id order, with the checks of V-1.</summary>
public sealed class EncounterTable
{
    public IReadOnlyList<EncounterDef> Defs { get; }
    public TravelSettingsDef Cfg { get; }
    private EncounterTable(IReadOnlyList<EncounterDef> defs, TravelSettingsDef cfg) { Defs = defs; Cfg = cfg; }

    public static EncounterTable From(ContentDatabase db)
        => new([.. db.All<EncounterDef>().OrderBy(d => d.Id, StringComparer.Ordinal)], db.All<TravelSettingsDef>().OrderBy(d => d.Id, StringComparer.Ordinal).FirstOrDefault() ?? new TravelSettingsDef { Id = "travel" });

    public static readonly string[] ScriptNames = ["approach_talk", "demand_toll", "ambush_attack", "flee", "block_road", "beg", "trade", "ask_help", "pass_by"];

    /// <summary>Problems in the table (empty when valid): weights positive, scripts known, conditions known, archetype groups sane.</summary>
    public IEnumerable<string> Problems(ContentVocabulary vocab)
    {
        foreach (var d in Defs)
        {
            if (d.WeightPm <= 0) yield return $"encounter '{d.Id}': weight must be positive";
            if (Array.IndexOf(ScriptNames, d.Script) < 0) yield return $"encounter '{d.Id}': unknown script '{d.Script}'";
            foreach (var c in d.Conditions) if (!vocab.IsCondition(c.Id)) yield return $"encounter '{d.Id}': unknown condition '{c.Id}'";
            foreach (var g in d.Groups)
            {
                if (g.Archetype.Length == 0) yield return $"encounter '{d.Id}': a group has no archetype";
                if (g.Count.Count != 2 || g.Count[0] < 1 || g.Count[1] < g.Count[0] || g.Count[1] > 12) yield return $"encounter '{d.Id}': count must be [min, max] within 1..12";
            }
        }
    }
}

/// <summary>The encounter conditions (BaseVocabulary): evaluated against a leg sample and the hero's status.</summary>
public static class EncounterConditions
{
    private static int Arg(ConditionRefDef c, string key, int dflt) => c.Args.TryGetValue(key, out var v) && int.TryParse(v, out int i) ? i : dflt;
    private static string Str(ConditionRefDef c, string key) => c.Args.TryGetValue(key, out var v) ? v : "";

    public static RoadTier TierOf(string s) => s.ToLowerInvariant() switch { "highway" or "paved" => RoadTier.Highway, "road" => RoadTier.Road, "track" => RoadTier.Track, _ => RoadTier.OffRoad };

    public static bool Holds(ConditionRefDef c, in LegSample s, in TravelStatus h)
    {
        switch (c.Id)
        {
            case "road_tier_at_most": return s.Tier <= TierOf(Str(c, "tier"));
            case "danger_at_least": return s.DangerPm >= Arg(c, "pm", 0);
            case "terrain_near":
                return Str(c, "kind") switch
                {
                    "woodland" => (s.Terrain & (TerrainFlags.Woodland | TerrainFlags.DeepWoodland)) != 0,
                    "pasture" => (s.Terrain & TerrainFlags.Pasture) != 0,
                    "marsh" => (s.Terrain & TerrainFlags.Marsh) != 0,
                    "field" => (s.Terrain & TerrainFlags.Field) != 0,
                    _ => false,
                };
            case "hero_party_strength_below": return h.PartyStrengthPm < Arg(c, "pm", 1000);
            case "holy_day_within": return s.HolyDayInDays <= Arg(c, "days", 0);
            case "chronicle_within":
            {
                int days = Arg(c, "days", 0);
                int ago = Str(c, "kind") switch { "famine" => s.FamineDaysAgo, "raid" => s.RaidDaysAgo, "war" => s.WarDaysAgo, "battle" => s.BattleDaysAgo, _ => int.MaxValue };
                return ago <= days;
            }
            case "lords_land": return (s.Terrain & TerrainFlags.LordsLand) != 0;
            case "near_tavern": return s.NearestTavernM <= Arg(c, "within_m", 2000);
            case "evening": return s.Evening;
            case "daytime": return s.Daytime;
            case "night": return s.Night;
            case "rain_within": return s.RainDaysAgo <= Arg(c, "days", 0);
            case "storm_within": return s.StormDaysAgo <= Arg(c, "days", 0);
            case "river_crossing": return (s.Terrain & TerrainFlags.RiverCrossing) != 0;
            case "heavy_rain": return s.Weather is WeatherState.HeavyRain or WeatherState.Thunderstorm;
            case "gate_or_bridge": return (s.Terrain & TerrainFlags.GateOrBridge) != 0;
            case "hungry_year": return s.HungryYear || s.Season == Season.Winter;
            case "deep_woodland": return (s.Terrain & TerrainFlags.DeepWoodland) != 0;
            case "war_within_km": return s.WarKm <= Arg(c, "km", 0);
            case "weather_is": return string.Equals(s.Weather.ToString(), Str(c, "state"), StringComparison.OrdinalIgnoreCase);
            case "forage_stock_at_least": return s.ForageStockG >= Arg(c, "g", 0);
            case "hero_is_armed": return h.HeroArmed;
            case "season_is": return string.Equals(s.Season.ToString(), Str(c, "season"), StringComparison.OrdinalIgnoreCase);
            default: return false;       // a vocabulary id this table has no rule for never holds
        }
    }
}

/// <summary>One encounter that happened: where, when, who (real person ids), and what the game should do with it.</summary>
public sealed record EncounterEvent(long Sec, string Id, string Kind, long XMm, long YMm, long SpawnXMm, long SpawnYMm, int Leg, ulong[] Cast, string Bark);

/// <summary>
/// The director (spec 4.1): every 10 game minutes of leg time one roll, <c>p = base(tier) x night x danger/500 x calm x first-days scale</c>, then a table choice among the eligible rows. Both draws are pure functions of
/// (seed, game hour, hero id, slot), so 1x and 8x give the same encounters (V-2).
/// </summary>
public static class EncounterDirector
{
    /// <summary>Chance of an encounter this slot, in parts per million.</summary>
    public static long ChancePpm(TravelSettingsDef c, in LegSample s, long sec, long gameStartSec)
    {
        string tier = s.Tier switch { RoadTier.Highway => "highway", RoadTier.Road => "road", RoadTier.Track => "track", _ => "offroad" };
        long p = (c.BasePm.TryGetValue(tier, out int b) ? b : 40) * 1000L;                       // pm -> ppm
        if (s.Night) p = p * c.NightMultPm / 1000;
        p = p * s.DangerPm / Math.Max(1, c.DangerRefPm);
        if (s.NearestSettlementM < c.CalmWithinM) p = p * c.CalmMultPm / 1000;
        if (sec - gameStartSec < c.SafeDays * TimeConst.SecondsPerDay) p = p * c.SafeScalePm / 1000;
        return Math.Clamp(p, 0, 1_000_000);
    }

    /// <summary>The weight of a row: the calibrated weight, except that road bandits (ambush, toll demand, block) scale with the bandit frequency slider.</summary>
    private static int Weight(TravelState st, EncounterDef d)
        => st.BanditPct == 100 || d.Kind is not ("Ambush" or "Demand" or "Block") ? d.WeightPm : Math.Max(1, (int)((long)d.WeightPm * st.BanditPct / 100));

    public static bool IsEarlyDisabled(TravelSettingsDef c, EncounterDef d) => c.SafeDisabledKinds.Contains(d.Kind);

    /// <summary>
    /// The roll for the slot that starts at <paramref name="sec"/> (a multiple of the slot length). Returns the chosen row or null. The draws are made whether or not the gap or cooldowns let one through,
    /// so the stream never shifts.
    /// </summary>
    public static EncounterDef? Roll(TravelState st, in LegSample s, in TravelStatus h, long sec)
    {
        var cfg = st.Cfg;
        long hour = sec / TimeConst.SecondsPerHour; uint slot = (uint)(sec % TimeConst.SecondsPerHour / (cfg.SlotMinutes * TimeConst.SecondsPerMinute));
        long p = ChancePpm(cfg, s, sec, st.GameStartSec);
        int roll = DetRng.Range(st.Seed, hour, st.HeroId.Value, RngStreams.EncounterRoll, slot, 0, 1_000_000);
        long weightTotal = 0;
        bool early = sec - st.GameStartSec < cfg.SafeDays * TimeConst.SecondsPerDay;
        var elig = new List<EncounterDef>();
        foreach (var d in st.Table.Defs)
        {
            if (early && IsEarlyDisabled(cfg, d)) continue;
            if (h.EscortedByMentor && IsEarlyDisabled(cfg, d)) continue;
            if (d.MinFromSettlementM > s.NearestSettlementM) continue;
            if (d.OncePerLeg && st.LegFired.Contains(d.Id)) continue;
            if (st.LastById.TryGetValue(d.Id, out long last) && sec - last < d.CooldownGameHours * TimeConst.SecondsPerHour) continue;
            bool ok = true; foreach (var c in d.Conditions) if (!EncounterConditions.Holds(c, s, h)) { ok = false; break; }
            if (!ok) continue;
            elig.Add(d); weightTotal += Weight(st, d);
        }
        int pick = weightTotal > 0 ? DetRng.Range(st.Seed, hour, st.HeroId.Value, RngStreams.EncounterRoll, 6 + slot, 0, (int)weightTotal) : 0;
        if (roll >= p || elig.Count == 0) return null;
        if (st.LastEncounterSec != long.MinValue && sec - st.LastEncounterSec < cfg.MinGapMinutes * TimeConst.SecondsPerMinute) return null;
        foreach (var d in elig) { int wt = Weight(st, d); if (pick < wt) return d; pick -= wt; }
        return elig[^1];
    }
}
