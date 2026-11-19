namespace Ravensguard.Sim.People;

public enum Sex : byte { Male, Female }
public enum SocialClass : byte { Peasant, Burgher, Gentry }
/// <summary>How a household holds its dwelling (plan §4A.2): Owned outright, Rented from a landlord, or Tenant of the lord of the fief.</summary>
public enum DeedKind : byte { None, Owned, Rented, Tenant }
public enum HomelessReason : byte { None, NoRoom, Vagrant }
/// <summary>Life state of a person row (population spec 2): dead and departed rows are kept so kin links and ids never dangle.</summary>
public enum PersonStatus : byte { Alive, Dead, Departed }
/// <summary>Why a person died (population spec 2, chronicle cause kinds); <see cref="None"/> while alive.</summary>
public enum DeathCause : byte
{
    None, Age, Hunger, Disease, Cold, Violence, Other,
    // Phase 4 (wounds 3.1, appended): what a wound killed by; the chronicle names the cause chain back to the blow
    BloodLoss, Organ, Brain, Infection, Suffocation, Ischaemia,
}

/// <summary>Jobs by production chain (plan §4A.1). None covers children and the retired.</summary>
public enum Occupation : byte
{
    None, Farmer, Ploughman, Shepherd, Reeve, Miller, Baker, Brewer, Butcher, Smith, Carpenter, Mason, Weaver, Spinner, Tanner, Cobbler,
    Merchant, Innkeeper, Priest, Guard, Labourer, Servant, Fisher, Woodcutter, Miner, CharcoalBurner,
    Lord, Steward, Mayor, GuildMaster,
}

public enum Skill : byte
{
    None, Farming, Husbandry, Milling, Baking, Brewing, Butchery, Smithing, Carpentry, Masonry, Weaving, Spinning, Tanning, Cobbling,
    Trading, Leadership, Melee, Archery, Literacy, Fishing, Forestry, Mining, Cooking, Housekeeping, Law, Riding, Theology,
    // Phase 3 (append only, plan skill list): used by crafting, clothing, trade and travel
    Tailoring, Surgery, Speech, Athletics, Tactics, Stealth, Lockpicking,
}

/// <summary>A skill slot of a Person: which skill and its 0..100 level.</summary>
public readonly record struct SkillLevel(Skill Skill, int Level);

public static class OccupationInfo
{
    /// <summary>True for crafts and trades whose masters count as burghers in a town.</summary>
    public static bool IsTrade(Occupation o) => o is Occupation.Baker or Occupation.Brewer or Occupation.Butcher or Occupation.Smith or Occupation.Carpenter
        or Occupation.Mason or Occupation.Weaver or Occupation.Tanner or Occupation.Cobbler or Occupation.Merchant or Occupation.Innkeeper
        or Occupation.Miller or Occupation.Mayor or Occupation.GuildMaster;

    /// <summary>Youngest age to hold the job: 12 for plain farm and household work (a child helping), 16 for crafts, trades, management and arms.</summary>
    public static int MinWorkAge(Occupation o) => o switch
    {
        Occupation.None or Occupation.Farmer or Occupation.Ploughman or Occupation.Shepherd or Occupation.Spinner or Occupation.Labourer or Occupation.Servant
            or Occupation.Fisher or Occupation.Woodcutter or Occupation.CharcoalBurner => 12,
        _ => 16,
    };

    /// <summary>Primary and secondary skills a job trains (levels are set by age and a roll).</summary>
    public static (Skill A, Skill B) Skills(Occupation o) => o switch
    {
        Occupation.Farmer => (Skill.Farming, Skill.Husbandry),
        Occupation.Ploughman => (Skill.Farming, Skill.Husbandry),
        Occupation.Shepherd => (Skill.Husbandry, Skill.Melee),
        Occupation.Reeve => (Skill.Leadership, Skill.Farming),
        Occupation.Miller => (Skill.Milling, Skill.Trading),
        Occupation.Baker => (Skill.Baking, Skill.Trading),
        Occupation.Brewer => (Skill.Brewing, Skill.Trading),
        Occupation.Butcher => (Skill.Butchery, Skill.Trading),
        Occupation.Smith => (Skill.Smithing, Skill.Trading),
        Occupation.Carpenter => (Skill.Carpentry, Skill.Trading),
        Occupation.Mason => (Skill.Masonry, Skill.Trading),
        Occupation.Weaver => (Skill.Weaving, Skill.Spinning),
        Occupation.Spinner => (Skill.Spinning, Skill.Housekeeping),
        Occupation.Tanner => (Skill.Tanning, Skill.Trading),
        Occupation.Cobbler => (Skill.Cobbling, Skill.Trading),
        Occupation.Merchant => (Skill.Trading, Skill.Literacy),
        Occupation.Innkeeper => (Skill.Brewing, Skill.Trading),
        Occupation.Priest => (Skill.Theology, Skill.Literacy),
        Occupation.Guard => (Skill.Melee, Skill.Archery),
        Occupation.Labourer => (Skill.Farming, Skill.Carpentry),
        Occupation.Servant => (Skill.Housekeeping, Skill.Cooking),
        Occupation.Fisher => (Skill.Fishing, Skill.Carpentry),
        Occupation.Woodcutter => (Skill.Forestry, Skill.Carpentry),
        Occupation.Miner => (Skill.Mining, Skill.Masonry),
        Occupation.CharcoalBurner => (Skill.Forestry, Skill.Smithing),
        Occupation.Lord => (Skill.Leadership, Skill.Melee),
        Occupation.Steward => (Skill.Literacy, Skill.Law),
        Occupation.Mayor => (Skill.Law, Skill.Trading),
        Occupation.GuildMaster => (Skill.Trading, Skill.Leadership),
        _ => (Skill.None, Skill.None),
    };

    /// <summary>Attribute bonuses (strength, toughness, dexterity, perception, wits) in points.</summary>
    public static (int Str, int Tou, int Dex, int Per, int Wit) Bonus(Occupation o) => o switch
    {
        Occupation.Farmer or Occupation.Ploughman or Occupation.Labourer => (6, 6, 0, 0, 0),
        Occupation.Smith or Occupation.Mason or Occupation.Miner or Occupation.Woodcutter => (10, 6, 0, 0, 0),
        Occupation.Carpenter or Occupation.Cobbler or Occupation.Weaver or Occupation.Spinner => (0, 0, 8, 2, 0),
        Occupation.Guard or Occupation.Lord => (6, 6, 4, 2, 0),
        Occupation.Merchant or Occupation.Steward or Occupation.Mayor or Occupation.GuildMaster or Occupation.Priest => (0, 0, 0, 2, 10),
        Occupation.Fisher or Occupation.Shepherd => (2, 4, 0, 6, 0),
        _ => (0, 0, 0, 0, 0),
    };
}
