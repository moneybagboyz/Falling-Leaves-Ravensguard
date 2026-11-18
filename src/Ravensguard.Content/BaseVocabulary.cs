namespace Ravensguard.Content;

/// <summary>The base game's condition and effect ids (plan §2.4: deliberately small and typed, not a scripting language).</summary>
public static class BaseVocabulary
{
    public static readonly string[] Conditions =
    [
        "has_proficiency", "has_item", "has_coin_at_least", "has_followers_at_least", "is_in_place_kind", "owns_building",
        "knows_person", "season_is", "near_fire", "has_reputation_at_least", "completed_contract_count", "has_rank",
        // travel (docs/specs/phase3/travel-encounters-camp.md 3.1): the encounter table's conditions
        "road_tier_at_most", "danger_at_least", "terrain_near", "hero_party_strength_below", "holy_day_within", "chronicle_within", "lords_land",
        "near_tavern", "evening", "daytime", "night", "rain_within", "storm_within", "river_crossing", "heavy_rain", "gate_or_bridge", "hungry_year",
        "deep_woodland", "war_within_km", "weather_is", "forage_stock_at_least", "hero_is_armed",
    ];

    public static readonly string[] Effects = ["start_contract", "give_item", "add_rumour", "set_flag"];

    public static ContentVocabulary Create() => new ContentVocabulary().RegisterCondition(Conditions).RegisterEffect(Effects);
}
