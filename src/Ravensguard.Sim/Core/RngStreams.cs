namespace Ravensguard.Sim.Core;

/// <summary>
/// The stream ids of every counter draw in the simulation (overview 2.3): <c>DetRng.*(seed, tickKey, entityId, stream, draw)</c>. Phase 2 owns 0x200..0x2FF; the
/// weather's long conditions use 0xC0. A stream is one use: two uses never share an id unless the pair is listed in <see cref="SharedPairs"/> with the reason their
/// keys cannot meet (a uniqueness test checks this). Append only; never renumber (a renumbered stream changes every world built from a seed).
/// </summary>
public static class RngStreams
{
    // ---------- weather ----------
    /// <summary>Drought, wet year and cold summer per culture region and year (key culture id + 0x100, tick = year).</summary>
    public const uint WeatherLong = 0xC0;

    // ---------- population (people spec) ----------
    public const uint Death = 0x200, Conception = 0x201, Marriage = 0x202, Migrant = 0x203, Outbreak = 0x204;

    // ---------- farming ----------
    /// <summary>Hail, blight and frost picks (key parcel or culture-year), and the coin-or-kind lease pick at world start (draw 7).</summary>
    public const uint FarmEvents = 0x210;
    /// <summary>Yield variance per field, +-4 percent.</summary>
    public const uint YieldVariance = 0x211;
    /// <summary>The herds: whole animals from yearly rates (births, culls, deaths; key settlement, draw by species).</summary>
    public const uint Herds = 0x212;

    // ---------- markets, goods and production ----------
    /// <summary>The order in which a session fills its buy orders (key buyer, tick = session hour).</summary>
    public const uint MarketOrder = 0x220;
    /// <summary>The seed money's tie-break at world start (key purse owner, tick 0). Shares <see cref="MarketOrder"/>, see <see cref="SharedPairs"/>.</summary>
    public const uint MoneySeed = MarketOrder;
    /// <summary>A recipe's tool wear per batch (key business, draw = recipe index). The overview's table had it as "stochastic rounding of goods", which nothing uses.</summary>
    public const uint ToolWear = 0x221;
    /// <summary>Spoilage rounding of a store (key owner, draw = good).</summary>
    public const uint Spoilage = 0x222;
    /// <summary>Craft quality roll (key crafter, draw = output serial).</summary>
    public const uint CraftQuality = 0x223;

    // ---------- caravans and the continent ----------
    public const uint CaravanEncounter = 0x230, CaravanTrip = 0x231;
    /// <summary>Extraction variance: the hunting roll (key tile, tick = day) and the fish stock at world build (key tile, tick 0, draw 1).</summary>
    public const uint Extraction = 0x240;
    /// <summary>Mine flood and cave-in (key site).</summary>
    public const uint MineHazard = 0x241;
    /// <summary>The continent tick's region flows (key region / settlement x 16 + good, tick = year).</summary>
    public const uint Continent = 0x250;

    // ---------- taxes ----------
    /// <summary>Tax evasion and unrest rolls (key household or settlement, tick = day).</summary>
    public const uint TaxEvents = 0x260;

    // ---------- background growth (docs/specs/phase3/background-growth.md) ----------
    /// <summary>Which plot, tile and household a cottage, a field clearing or a new business takes when the scores tie (key settlement, tick = the season's first hour, draw by use).</summary>
    public const uint GrowthPick = 0x270;
    /// <summary>The wobble of a build's or a clearing's hours (key project serial, tick = the season's first hour).</summary>
    public const uint GrowthWork = 0x271;

    // ---------- schedules and catch-up ----------
    /// <summary>The spot of a person inside a place on a day, the day's field tile, and whether they shop (key person, tick = game day, draw = place x 4 + axis; 14 = shops, 15 = field tile). Live/Schedule.cs.</summary>
    public const uint ScheduleJitter = 0x2A0;
    /// <summary>Reserved by the overview (no draw yet).</summary>
    public const uint CatchUp = 0x2B0;

    // ---------- live behaviour and jobs (docs/specs/phase3/live-behaviour-and-utility-ai.md section 5; Phase 3 owns 0x300..0x3FF) ----------
    /// <summary>The utility scorer's noise and ties (key person, tick = game second / 20, draw = action index).</summary>
    public const uint UtilityNoise = 0x300;
    /// <summary>The rank of a claimant of a job (key job, tick = game hour, draw = claimant person index): the lowest rank wins.</summary>
    public const uint JobClaim = 0x301;
    /// <summary>Where a person without a bed sleeps on the floor of the sleeping room (key person, tick = 0).</summary>
    public const uint BedFallback = 0x302;
    /// <summary>The routine jobs a settlement's households post (key settlement, tick = game day).</summary>
    public const uint RoutineJobs = 0x303;

    // ---------- trade, inventory and character (docs/specs/phase3/trade-inventory-character.md section 5) ----------
    /// <summary>Attribute rounding and skill-use jitter (key person; reserved, nothing draws it in v1).</summary>
    public const uint TradeAttrJitter = 0x320;
    /// <summary>Hire and recruit mood noise (tick = game day, key person).</summary>
    public const uint HireMood = 0x321;
    /// <summary>Quality of goods for sale in an NPC pack (key owner, at stocking time only).</summary>
    public const uint PackQuality = 0x322;

    // ---------- clothing and exposure (docs/specs/phase3/clothing-warmth-exposure.md section 5) ----------
    /// <summary>Stain accumulation rounding and tear rolls (key item).</summary>
    public const uint Stains = 0x330;
    /// <summary>A person's cold tolerance jitter at creation, plus or minus 3% (key person).</summary>
    public const uint ColdTolerance = 0x331;
    /// <summary>Which pieces of the class wardrobe a person owns, and the dye of their outer pieces (key person, draw = piece index; 99 = dye).</summary>
    public const uint Wardrobe = 0x333;

    // ---------- crafting (docs/specs/phase3/basic-crafting.md section 5) ----------
    /// <summary>The failure roll at a recipe's critical step (key crafter, tick = job serial, draw = step index).</summary>
    public const uint CraftFailure = 0x360;
    /// <summary>By-product chance rolls (ash, offcuts; key job id, tick = job serial, draw = by-product index).</summary>
    public const uint CraftByproduct = 0x361;
    /// <summary>Learning a recipe by watching (key person, tick 0, draw = recipe index x 16 + attempt).</summary>
    public const uint CraftWatch = 0x362;

    // ---------- people records ----------
    /// <summary>A newborn's or immigrant's name and attributes.</summary>
    public const uint Name = 0x2C0, Attributes = 0x2C1;
    /// <summary>The wear of a household's shoes, clothes and tools (key household, draw = durable kind).</summary>
    public const uint Durables = 0x2C8;

    // ---------- abstract tick ----------
    /// <summary>The stand-in environment's daily temperature jitter (key 0, tick = day), for worlds without a weather model.</summary>
    public const uint EnvJitter = 0x2E0;

    /// <summary>Stream ids that two named uses share on purpose, with why their draws never meet.</summary>
    public static readonly (string A, string B, string Why)[] SharedPairs =
    [
        (nameof(MarketOrder), nameof(MoneySeed), "the seed money draws once at tick 0 before day 0; sessions run at the market's open hours (never hour 0 of day 0)"),
    ];
}
