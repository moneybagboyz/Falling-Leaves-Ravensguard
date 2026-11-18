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

    // ---------- reserved by the overview (no draw yet) ----------
    public const uint ScheduleJitter = 0x2A0, CatchUp = 0x2B0;

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
