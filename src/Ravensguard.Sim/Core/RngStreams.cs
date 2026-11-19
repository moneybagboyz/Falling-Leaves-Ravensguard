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
    /// <summary>Autopilot-only choices that are not utility ties (docs/specs/phase3/autopilot-v1.md 5): none in v1, and a test asserts it is never drawn (<c>DetRng.Draws</c> style guard in <c>AutopilotRng</c>).</summary>
    public const uint AutopilotReserved = 0x380;

    // ---------- dialogue, barks and rumours (docs/specs/phase3/dialogue-barks-rumours.md section 5) ----------
    /// <summary>The systemic line pick (key speaker, tick = game second / 60, draw = listener index x 16 + turn mod 16).</summary>
    public const uint LinePick = 0x310;
    /// <summary>Bark pick and cooldown jitter (key person, tick = game second / 20).</summary>
    public const uint BarkPick = 0x311;
    /// <summary>Which people witnessed a chronicle entry (key entry id, tick 0, draw = candidate rank).</summary>
    public const uint RumourWitness = 0x312;
    /// <summary>Whether a listener hears a rumour from a teller (key teller, tick = game hour, draw = listener index).</summary>
    public const uint RumourHear = 0x313;
    /// <summary>The distortion roll of a retelling (key listener, tick = game hour, draw 0 = whether, 1 = which, 2 = misattribution).</summary>
    public const uint RumourDistort = 0x314;
    /// <summary>Who socialises in an hour and which gathering they join (key settlement, tick = game hour, draw = person index x 2 + 0 or 1).</summary>
    public const uint GatherPartition = 0x315;

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
    /// <summary>Butchery yield variance, plus or minus 4 percent (key carcass id, draw = tissue index; docs/specs/phase3/clothing-warmth-exposure.md 4.8).</summary>
    public const uint ButcheryVariance = 0x332;
    /// <summary>Which pieces of the class wardrobe a person owns, and the dye of their outer pieces (key person, draw = piece index; 99 = dye).</summary>
    public const uint Wardrobe = 0x333;

    // ---------- travel (docs/specs/phase3/travel-encounters-camp.md section 5) ----------
    /// <summary>The encounter roll (key hero, tick = game hour, draw = 10-minute slot 0..5) and the table choice (draw = 6 + slot).</summary>
    public const uint EncounterRoll = 0x340;
    /// <summary>Encounter placement: distance, side and count of a group (key hero, tick = game hour, draw = slot x 16 + group x 4 + what).</summary>
    public const uint EncounterPlace = 0x341;
    /// <summary>Forage poisoning and item mix (key person, tick = game second / 200, draw = item index).</summary>
    public const uint ForageDraw = 0x342;
    /// <summary>Hunting: which animal a tile offers and snare catches (key tile, tick = game hour, draw).</summary>
    public const uint HuntDraw = 0x343;
    /// <summary>Sentry notice rolls (key sentry, tick = game second / 20, draw = approacher index).</summary>
    public const uint SentryNotice = 0x344;
    /// <summary>Fair events and contest results (key settlement, tick = day).</summary>
    public const uint FairDraw = 0x345;
    /// <summary>Camp night events (key camp, tick = game hour).</summary>
    public const uint CampEvent = 0x346;
    /// <summary>Making a fire: the success roll (key person, tick = game second / 60, draw = attempt).</summary>
    public const uint FireMaking = 0x347;
    /// <summary>What a stubbed fight turns into, robbed or fled (key hero, tick = game hour, draw = slot; Phase 4 replaces it with real combat).</summary>
    public const uint EncounterOutcome = 0x348;
    /// <summary>Fair stalls and odd jobs a fair brings (key settlement, tick = game day, draw = what).</summary>
    public const uint FairStall = 0x349;

    // ---------- horses and carts (docs/specs/phase3/horses-and-carts.md section 5) ----------
    /// <summary>Forced-exertion injury of a ridden animal at 0 stamina (key animal, tick = game second / 20).</summary>
    public const uint AnimalInjury = 0x350;
    /// <summary>A vehicle's stuck check (key vehicle, tick = game hour, draw = odometer metres / 20) and an abstract leg's stuck duration (draw = leg index).</summary>
    public const uint VehicleStuck = 0x351;
    /// <summary>Temper and training of an animal materialised from a herd (key settlement, draw = serial x 2 + 0 or 1).</summary>
    public const uint AnimalBreed = 0x352;
    /// <summary>A lost shoe (key animal, draw = odometer metres / 100 x 2, + 1 picks the hoof).</summary>
    public const uint ShoeLoss = 0x353;
    /// <summary>A try at taming (key animal, tick = game second / 600, draw = rider person index).</summary>
    public const uint Taming = 0x354;

    // ---------- crafting (docs/specs/phase3/basic-crafting.md section 5) ----------
    /// <summary>The failure roll at a recipe's critical step (key crafter, tick = job serial, draw = step index).</summary>
    public const uint CraftFailure = 0x360;
    /// <summary>By-product chance rolls (ash, offcuts; key job id, tick = job serial, draw = by-product index).</summary>
    public const uint CraftByproduct = 0x361;
    /// <summary>Learning a recipe by watching (key person, tick 0, draw = recipe index x 16 + attempt).</summary>
    public const uint CraftWatch = 0x362;
    /// <summary>The fraction of a unit a failed step wastes (key job id, tick = job serial, draw = good): q x pct / 100 rounds up with probability of its remainder, so small stacks lose their share on average (review finding 8).</summary>
    public const uint CraftWaste = 0x363;

    // ---------- onboarding (docs/specs/phase3/onboarding-v1.md section 5) ----------
    /// <summary>Start-site tie-break among equally scored candidates and the mentor's spawn jitter (key world seed, tick = origin hash).</summary>
    public const uint OnboardingSite = 0x370;
    /// <summary>Onboarding job parameters: which good, which route (key new-game seed).</summary>
    public const uint OnboardingJob = 0x371;

    // ---------- people records ----------
    /// <summary>A newborn's or immigrant's name and attributes.</summary>
    public const uint Name = 0x2C0, Attributes = 0x2C1;
    /// <summary>The wear of a household's shoes, clothes and tools (key household, draw = durable kind).</summary>
    public const uint Durables = 0x2C8;

    // ---------- abstract tick ----------
    /// <summary>The stand-in environment's daily temperature jitter (key 0, tick = day), for worlds without a weather model.</summary>
    public const uint EnvJitter = 0x2E0;

    // ---------- Phase 4: combat (docs/specs/phase4/00-phase4-overview.md 6.2; Phase 4 owns 0x400..0x4FF) ----------
    // Combat streams key on the fight's own FightTick (overview 6.1), the attacker, and draw = swing serial x 64 + k.
    /// <summary>Body variation at creation: stature +-8%, blood volume +-5%, handedness (key person, tick 0).</summary>
    public const uint BodyVariation = 0x400;
    /// <summary>Monster size variation at spawn (key creature id).</summary>
    public const uint MonsterVariation = 0x401;
    /// <summary>Resolver v2: the hit roll (k = 0).</summary>
    public const uint HitRoll = 0x410;
    /// <summary>Resolver v2: redirect of a missed called shot (k = 0 yes or no, k = 1 which neighbour).</summary>
    public const uint Redirect = 0x411;
    /// <summary>Resolver v2: coverage rolls outside in (k = the piece's rank).</summary>
    public const uint Coverage = 0x412;
    /// <summary>Resolver v2: structures, ligaments, nerves, cord, hosted parts and rib slip (k = structure index, insets after, 32 + i joint wrench, 62 rib side, 63 rib slip).</summary>
    public const uint StructureRoll = 0x413;
    /// <summary>Resolver v2: organ rolls (k = organ index).</summary>
    public const uint OrganRoll = 0x414;
    /// <summary>Resolver v2: artery bleed rate 80-120 ml/s (k = structure index).</summary>
    public const uint BleedRate = 0x415;
    /// <summary>Falls and joint-wrench placement, partial-cord residual strength (k = limb group).</summary>
    public const uint HitJitter = 0x416;
    /// <summary>Stumble from an unstable joint (key person, tick = GameSec / 20).</summary>
    public const uint Stumble = 0x417;
    /// <summary>Wounds: daily infection (draw = wound id x 4 + 1), the level rising (+2) and the sepsis death day (+3); key game day, person.</summary>
    public const uint Infection = 0x420;
    /// <summary>Wounds: malunion at healing completion (key person, draw wound id).</summary>
    public const uint Malunion = 0x421;
    /// <summary>Wounds: set, tendon, ligament, organ and infection-clearing outcomes (key GameSec / 20, surgeon, draw wound id).</summary>
    public const uint SurgeryResult = 0x422;
    /// <summary>Wounds: concussion knock-out duration +-10% (key FightTick, person).</summary>
    public const uint ConcussionJitter = 0x423;
    /// <summary>Wounds: waking delay 0-4 sim-seconds (key sim-second, person).</summary>
    public const uint WakeJitter = 0x424;
    /// <summary>Melee defence: dodge and parry success (key FightTick, defender, draw = blow serial); the resolver's dodge roll uses it.</summary>
    public const uint MeleeDefence = 0x430;
    /// <summary>Combat AI: target choice (key FightTick, fighter).</summary>
    public const uint AiTarget = 0x431;
    /// <summary>Combat AI: stance noise and tie-breaks, patience, part and attack picks (key FightTick, fighter).</summary>
    public const uint StanceNoise = 0x432;
    /// <summary>Stagger and knock-down duration (key FightTick, fighter).</summary>
    public const uint Stagger = 0x433;
    /// <summary>Ranged aim error, two draws per shot: lateral, vertical (key FightTick, shooter, draw = shot serial x 2 + k).</summary>
    public const uint AimError = 0x440;
    /// <summary>Ammunition breakage and recovery (key fight id, ammo serial).</summary>
    public const uint AmmoBreak = 0x441;
    /// <summary>Thrown aim error (key FightTick, thrower).</summary>
    public const uint ThrownAim = 0x442;
    // mounted-combat.md 6 (0x470-0x473), keyed FightTick
    /// <summary>Mounted: unhorse roll (draw 1), pin chance (draw 2), lance shatter (draw 3); key FightTick, rider (shatter: the lancer), draw + 8 x (attacker + 1).</summary>
    public const uint UnhorseRoll = 0x470;
    /// <summary>Mounted: the horse's panic effects (rearing throws, bolt direction) and refusal before braced polearms (key FightTick, horse).</summary>
    public const uint HorsePanic = 0x471;
    /// <summary>Mounted: where an unhorsed rider lands (draw 0 the limb group, draw 1 the side; key FightTick, rider).</summary>
    public const uint FallPlacement = 0x472;
    /// <summary>Mounted: a galloping horse slips on ice or stumbles on rubble (key FightTick, horse).</summary>
    public const uint TerrainSlip = 0x473;

    // ---------- feedback and flavour (P4-D): cosmetic, never read by the simulation, never hashed ----------
    /// <summary>Flavour text: which template of each slot a line uses (key FightTick, line serial, draw = slot x 8 + attempt). Presentation only.</summary>
    public const uint FlavourPick = 0x480;
    /// <summary>Cosmetic jitter: shake direction, decal offsets, audio pitch (key line serial). Presentation only.</summary>
    public const uint CosmeticJitter = 0x481;
    // ---- morale, capture and succession (morale-capture-succession 6; P4-D owns 0x450..0x457) ----
    /// <summary>Morale: the rally and the wavering step-back rolls (key FightTick, fighter).</summary>
    public const uint MoraleRally = 0x450;
    /// <summary>Morale: the winner's acceptance of a surrender (key FightTick, winner).</summary>
    public const uint Surrender = 0x451;
    /// <summary>Defeat: loot choices, the mercy bandage, the cruel kill, the bandit style (key fight id, loser).</summary>
    public const uint Loot = 0x452;
    /// <summary>Captivity: guard shifts, notice rolls, the guards' alertness, bandit release fates (key GameSec / 20, captive).</summary>
    public const uint CaptiveEvent = 0x453;
    /// <summary>Captivity: escape windows on a transfer, the rescue party's daily chance (key game day, captive).</summary>
    public const uint EscapeWindow = 0x454;
    /// <summary>Captivity: lock picking and slipping the bonds (key GameSec / 20, captive, draw = attempt).</summary>
    public const uint LockPick = 0x455;
    /// <summary>Captivity: ransom negotiation (key game day, captive).</summary>
    public const uint RansomNegotiation = 0x456;
    /// <summary>Reserved: succession draws nothing (a pure function of the tables; asserted by test SU-5).</summary>
    public const uint SuccessionReserved = 0x457;

    /// <summary>Stream ids that two named uses share on purpose, with why their draws never meet.</summary>
    public static readonly (string A, string B, string Why)[] SharedPairs =
    [
        (nameof(MarketOrder), nameof(MoneySeed), "the seed money draws once at tick 0 before day 0; sessions run at the market's open hours (never hour 0 of day 0)"),
    ];
}
