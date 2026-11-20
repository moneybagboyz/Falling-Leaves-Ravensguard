using Ravensguard.Content;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.People;
using Ravensguard.Sim.Rumours;

namespace Ravensguard.Sim.Dialogue;

public enum RelationTier : byte { Stranger, Acquaintance, Friend, Trusted, Disliked, Hated }
public enum MoodBand : byte { Low, Mid, High }

/// <summary>Where the text of a key comes from: the game's <c>tr()</c>, or a CSV in a test. Lines never concatenate fragments, so one lookup is one whole sentence.</summary>
public interface ITextSource { string T(string key); bool Has(string key); }

/// <summary>A text source over a dictionary (tests, tools).</summary>
public sealed class DictText(IReadOnlyDictionary<string, string> map) : ITextSource
{
    public string T(string key) => map.TryGetValue(key, out var s) ? s : key;
    public bool Has(string key) => map.ContainsKey(key);
}

public static class Relations
{
    /// <summary>Relationship tiers (spec 3.2): stranger (familiarity under 10), acquaintance (10-39), friend (40+ and trust 300+), trusted (70+ and trust 600+), disliked (trust under -200), hated (under -600).</summary>
    public static RelationTier Tier(int familiarity, int trustPm)
    {
        if (trustPm < -600) return RelationTier.Hated;
        if (trustPm < -200) return RelationTier.Disliked;
        if (familiarity >= 70 && trustPm >= 600) return RelationTier.Trusted;
        if (familiarity >= 40 && trustPm >= 300) return RelationTier.Friend;
        return familiarity >= 10 ? RelationTier.Acquaintance : RelationTier.Stranger;
    }

    public static string TierName(RelationTier t) => t.ToString().ToLowerInvariant();
}

/// <summary>What the mood rules read of a person (spec 4.3): fed, housed, hungry, cold, crowded, job satisfaction (-10..+10) and a kin death within 15 days.</summary>
public readonly record struct MoodInputs(bool Fed, bool Housed, bool Hungry, bool Cold, bool Crowded, int JobSatisfaction, bool Bereaved);

/// <summary>The daily mood (-100..+100): <c>+20</c> fed, <c>+10</c> housed, <c>-30</c> hungry, <c>-15</c> cold, <c>-10</c> crowded, <c>+/-10</c> job, <c>-20</c> bereaved. Other systems read the number; lines read the band.</summary>
public static class MoodSystem
{
    public static int Compute(in MoodInputs m, TalkSettingsDef s)
    {
        int v = (m.Fed ? s.MoodFed : 0) + (m.Housed ? s.MoodHoused : 0) + (m.Hungry ? s.MoodHungry : 0) + (m.Cold ? s.MoodCold : 0) + (m.Crowded ? s.MoodCrowded : 0)
              + Math.Clamp(m.JobSatisfaction, -10, 10) + (m.Bereaved ? s.MoodBereaved : 0);
        return Math.Clamp(v, -100, 100);
    }

    public static MoodBand Band(int mood, TalkSettingsDef s) => mood < s.MoodLowBelow ? MoodBand.Low : mood > s.MoodHighAbove ? MoodBand.High : MoodBand.Mid;

    /// <summary>P6-C2 (mood in barks): the weather moves the day's mood: rain and frost -8, a storm or a blizzard -12, a fine day or a heatwave nothing (spec 4.5; the numbers are the same for everyone).</summary>
    public static int Weathered(int mood, string weather, TalkSettingsDef s) => weather switch { "LightRain" or "HeavyRain" or "Rain" or "Frost" or "Snow" => mood - 8, "Thunderstorm" or "Blizzard" or "Storm" => mood - 12, _ => mood };

    /// <summary>P6-C2: a household's open grievances (tax, the lord, crime) weigh on its people: -5 each.</summary>
    public static int Grieved(int mood, Ravensguard.Sim.Economy.Grievance g, TalkSettingsDef s) => mood - 5 * (((g & Ravensguard.Sim.Economy.Grievance.Tax) != 0 ? 1 : 0) + ((g & Ravensguard.Sim.Economy.Grievance.Lord) != 0 ? 1 : 0) + ((g & Ravensguard.Sim.Economy.Grievance.Crime) != 0 ? 1 : 0));

    /// <summary>P6-C2: the mood a bark reads for a person of the abstract world: the daily mood from needs, the grievances of his house, the weather; and whether he is hungry (a day's food or less at home).</summary>
    public static (int Mood, bool Hungry) ForBark(Population pop, Ravensguard.Sim.Economy.EconomyState? econ, int person, int day, string weather, TalkSettingsDef s)
    {
        int hh = pop.People.Household[person]; int mood = Of(pop, econ, person, day, s);
        if (econ != null && hh >= 0 && hh < econ.Grievances.Length) mood = Grieved(mood, econ.Grievances[hh], s);
        bool hungry = econ != null && hh >= 0 && hh < econ.FoodCoverDaysX10.Length && econ.FoodCoverDaysX10[hh] < 10;
        return (Math.Clamp(Weathered(mood, weather, s), -100, 100), hungry);
    }

    /// <summary>
    /// The mood of a person of the abstract world today, from what the records hold: the household's food cover (3 days and more is fed, under one day hungry), a home, a crowded dwelling
    /// (more people than beds), winter without fuel cover as cold, and a parent or spouse dead within 15 days.
    /// </summary>
    public static int Of(Population pop, Ravensguard.Sim.Economy.EconomyState? econ, int person, int day, TalkSettingsDef s)
    {
        var p = pop.People; int hh = p.Household[person];
        bool housed = p.Homeless[person] == HomelessReason.None && p.HomeLot[person] >= 0;
        bool fed = true, hungry = false, cold = false, crowded = false;
        if (hh >= 0 && hh < pop.Households.Count)
        {
            crowded = pop.Households.Beds[hh] > 0 && pop.Households.MemberCount[hh] > pop.Households.Beds[hh];
            if (econ != null && hh < econ.FoodCoverDaysX10.Length)
            {
                int cover = econ.FoodCoverDaysX10[hh]; fed = cover >= 30; hungry = cover < 10;
                cold = new GameTime((long)day * TimeConst.SecondsPerDay).Season == Season.Winter && hh < econ.FuelCoverDaysX10.Length && econ.FuelCoverDaysX10[hh] < 10;
            }
        }
        bool bereaved = false;
        foreach (int k in new[] { p.Father[person], p.Mother[person], p.Spouse[person] })
            if (k >= 0 && k < p.Count && p.Status[k] == PersonStatus.Dead && day - p.DeathDay[k] <= 15 && p.DeathDay[k] >= 0) bereaved = true;
        return Compute(new MoodInputs(fed, housed, hungry, cold, crowded, 0, bereaved), s);
    }
}

/// <summary>
/// What a line's conditions read (spec 3.1): the speaker, the hero, the moment and, while a rumour is told, the rumour. A plain bag so the same selection runs for a person in the live tier, one in the
/// abstract tier and a test. Authored dialogue also reads the hero's skills, items and flags through the delegates.
/// </summary>
public sealed class TalkContext
{
    // ---- the speaker
    public string Job { get; set; } = "";                    // lower-case occupation: smith, farmer, none
    public int Mood { get; set; }
    public int Familiarity { get; set; }
    public int TrustPm { get; set; } = 100;
    public bool Kin { get; set; }
    public bool Female { get; set; }
    public bool Child { get; set; }
    public bool Shopkeeper { get; set; }
    public string Name { get; set; } = "";
    public HashSet<string> Needs { get; } = new(StringComparer.Ordinal);
    // ---- the moment
    public int Hour { get; set; } = 12;
    public Season Season { get; set; } = Season.Spring;
    public string Weather { get; set; } = "Clear";
    public bool FeastToday { get; set; }
    public string FeastName { get; set; } = "";
    public int DaysToFeast { get; set; } = 7;
    public string Place { get; set; } = "";
    public bool InMarket { get; set; }
    public bool MarketDear { get; set; }
    public bool UnrestHigh { get; set; }
    public bool AlarmNear { get; set; }
    public bool InCombat { get; set; }
    // ---- the hero
    public string HeroName { get; set; } = "";
    public bool HeroFemale { get; set; }
    public bool HeroArmed { get; set; }
    public bool HeroBloody { get; set; }
    public bool HeroRich { get; set; }
    public bool HeroRagged { get; set; }
    public Func<string, int> Proficiency { get; set; } = _ => 0;
    public Func<string, int> ItemCount { get; set; } = _ => 0;
    public long Coin { get; set; }
    public int Followers { get; set; }
    public int Contracts { get; set; }
    public Func<string, bool> KnowsPerson { get; set; } = _ => false;
    public HashSet<string> Flags { get; } = new(StringComparer.Ordinal);
    // ---- a rumour being told
    public RumourKind? RumourKind { get; set; }
    public HashSet<RumourKind> KnownKinds { get; } = [];
    public int RumourHop { get; set; } = -1;
    public bool RumourViaTraveller { get; set; }
    public bool RumourGarbled { get; set; }
    // ---- the text parameters (ParamResolver fills what it can from the above, a caller adds the rest)
    public Dictionary<string, string> Params { get; } = new(StringComparer.Ordinal);

    public RelationTier Tier => Relations.Tier(Familiarity, TrustPm);
    public static string DayPart(int hour) => hour switch { >= 5 and < 8 => "dawn", >= 8 and < 11 => "morning", >= 11 and < 14 => "noon", >= 14 and < 18 => "afternoon", >= 18 and < 22 => "evening", _ => "night" };

    /// <summary>The job class of a lower-case occupation: outdoor, craft, trade or civic.</summary>
    public static string JobClassOf(string job) => job switch
    {
        "farmer" or "ploughman" or "shepherd" or "fisher" or "woodcutter" or "miner" or "charcoalburner" or "labourer" => "outdoor",
        "miller" or "baker" or "brewer" or "butcher" or "smith" or "carpenter" or "mason" or "weaver" or "spinner" or "tanner" or "cobbler" => "craft",
        "merchant" or "innkeeper" => "trade",
        _ => "civic",
    };
    public string JobClass => JobClassOf(Job);
}

/// <summary>The typed condition vocabulary applied to a <see cref="TalkContext"/>. A condition with <c>"not": "true"</c> in its args is inverted; an id the talk layer does not know never holds.</summary>
public static class TalkConditions
{
    /// <summary>The ids a line, bark or dialogue may use (all are also in <see cref="BaseVocabulary"/>).</summary>
    public static readonly HashSet<string> Supported = new(StringComparer.Ordinal)
    {
        "speaker_job_is", "speaker_job_class", "mood_below", "mood_above", "relationship_at_least", "relationship_tier", "is_kin", "time_of_day_is", "weather_is", "knows_rumour_kind", "hero_is_armed",
        "hero_is_bloody", "hero_wealth_visible", "speaker_has_need", "feast_today", "flag_set", "speaker_is_shopkeeper", "speaker_is_child", "speaker_sex", "season_is", "daytime", "night", "evening",
        "is_in_place_kind", "market_dear", "unrest_high", "alarm_near", "in_combat", "hero_is_ragged", "rumour_hop_is", "rumour_via", "rumour_garbled",
        "has_proficiency", "has_item", "has_coin_at_least", "has_followers_at_least", "knows_person", "completed_contract_count",
    };

    public static string Arg(ConditionRefDef c, string k, string d = "") => c.Args.TryGetValue(k, out var v) ? v : d;
    private static int IArg(ConditionRefDef c, string k, int d) => c.Args.TryGetValue(k, out var v) && int.TryParse(v, out int i) ? i : d;

    public static bool Holds(ConditionRefDef c, TalkContext x)
    {
        bool r = Eval(c, x);
        return Arg(c, "not") == "true" ? !r : r;
    }

    public static bool All(IReadOnlyList<ConditionRefDef> cs, TalkContext x)
    {
        for (int i = 0; i < cs.Count; i++) if (!Holds(cs[i], x)) return false;
        return true;
    }

    private static bool Eval(ConditionRefDef c, TalkContext x)
    {
        switch (c.Id)
        {
            case "speaker_job_is": return string.Equals(x.Job, Arg(c, "job"), StringComparison.OrdinalIgnoreCase);
            case "speaker_job_class": return x.JobClass == Arg(c, "class");
            case "mood_below": return x.Mood < IArg(c, "value", 0);
            case "mood_above": return x.Mood > IArg(c, "value", 0);
            case "relationship_at_least": return x.TrustPm >= IArg(c, "trust", int.MinValue) && x.Familiarity >= IArg(c, "familiarity", 0);
            case "relationship_tier": return Relations.TierName(x.Tier) == Arg(c, "tier");
            case "is_kin": return x.Kin;
            case "time_of_day_is": return TalkContext.DayPart(x.Hour) == Arg(c, "part");
            case "daytime": return x.Hour is >= 6 and < 20;
            case "night": return x.Hour is >= 22 or < 5;
            case "evening": return x.Hour is >= 18 and < 22;
            case "weather_is": return string.Equals(x.Weather, Arg(c, "state"), StringComparison.OrdinalIgnoreCase);
            case "season_is": return string.Equals(x.Season.ToString(), Arg(c, "season"), StringComparison.OrdinalIgnoreCase);
            case "knows_rumour_kind":
                return Enum.TryParse<RumourKind>(Arg(c, "kind"), true, out var k) && (x.RumourKind is { } cur ? cur == k : x.KnownKinds.Contains(k));
            case "hero_is_armed": return x.HeroArmed;
            case "hero_is_bloody": return x.HeroBloody;
            case "hero_wealth_visible": return x.HeroRich;
            case "hero_is_ragged": return x.HeroRagged;
            case "speaker_has_need": return x.Needs.Contains(Arg(c, "need"));
            case "feast_today": return x.FeastToday;
            case "flag_set": return x.Flags.Contains(Arg(c, "flag"));
            case "speaker_is_shopkeeper": return x.Shopkeeper;
            case "speaker_is_child": return x.Child;
            case "speaker_sex": return (Arg(c, "sex") == "female") == x.Female;
            case "is_in_place_kind": return Arg(c, "kind") != "market" || x.InMarket;
            case "market_dear": return x.MarketDear;
            case "unrest_high": return x.UnrestHigh;
            case "alarm_near": return x.AlarmNear;
            case "in_combat": return x.InCombat;
            case "rumour_hop_is": { int h = IArg(c, "hop", 0); return h >= 2 ? x.RumourHop >= 2 && !x.RumourViaTraveller : x.RumourHop == h && !x.RumourViaTraveller; }
            case "rumour_via": return Arg(c, "via") == "traveller" ? x.RumourViaTraveller : !x.RumourViaTraveller;
            case "rumour_garbled": return x.RumourGarbled;
            case "has_proficiency": return x.Proficiency(Arg(c, "skill")) >= IArg(c, "level", 0);
            case "has_item": return x.ItemCount(Arg(c, "item")) >= IArg(c, "count", 1);
            case "has_coin_at_least": return x.Coin >= IArg(c, "mites", 0);
            case "has_followers_at_least": return x.Followers >= IArg(c, "count", 1);
            case "knows_person": return x.KnowsPerson(Arg(c, "person"));
            case "completed_contract_count": return x.Contracts >= IArg(c, "count", 1);
            default: return false;
        }
    }
}
