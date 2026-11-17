namespace Ravensguard.Content;

/// <summary>The base game's condition and effect ids (plan §2.4: deliberately small and typed, not a scripting language).</summary>
public static class BaseVocabulary
{
    public static readonly string[] Conditions =
    [
        "has_proficiency", "has_item", "has_coin_at_least", "has_followers_at_least", "is_in_place_kind", "owns_building",
        "knows_person", "season_is", "near_fire", "has_reputation_at_least", "completed_contract_count", "has_rank",
    ];

    public static readonly string[] Effects = ["start_contract", "give_item", "add_rumour", "set_flag"];

    public static ContentVocabulary Create() => new ContentVocabulary().RegisterCondition(Conditions).RegisterEffect(Effects);
}
