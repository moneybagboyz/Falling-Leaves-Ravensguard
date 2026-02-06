class_name Globals
extends Node

# --- WORLD GEN ---
const WORLD_W = 300
const WORLD_H = 300
const SEED_VAL = 12345
const MAX_BANDITS = 15

# --- TIME ---
const TURNS_PER_DAY = 24
const DAYS_PER_MONTH = 30
const MONTHS_PER_YEAR = 12
const DAYS_PER_YEAR = 360  # DAYS_PER_MONTH * MONTHS_PER_YEAR

# --- POPULATION & GROWTH ---
const STARVATION_DEATH_RATE = 0.02 # Lowered from 0.1 to allow more time for rescue/trade
const STARVATION_BASE_DEATH = 2    # Lowered from 20 to prevent hamlets from instant-wiping
const STARVATION_UNREST_INC = 10
const STARVATION_HAPPINESS_DEC = 20
const GROWTH_RATE = 0.0001 # 0.1% -> 0.01% daily (~3.6% annual). More historically accurate.
const GROWTH_BASE = 2
const HOUSE_CAPACITY = 5
const MIGRATION_THRESHOLD_HAPPINESS = 40
const MIGRATION_CHANCE = 0.05

# --- AGRICULTURE (ACRE-BASED) ---
const ACRES_PER_TILE = 250
const BUSHELS_PER_ACRE_BASE = 12.0 # Buffed from 10 to reflect High Medieval yields
const SEED_RATIO_INV = 0.20 # Buffed from 0.25 (1/5th reserved for seed)
const ACRES_WORKED_PER_LABORER = 10
const BUSHELS_PER_PERSON_YEAR = 15.0
const DAILY_BUSHELS_PER_PERSON = BUSHELS_PER_PERSON_YEAR / (30.0 * 12.0)

# --- WILDERNESS YIELDS (PER 360 DAYS, PER ACRE WORKED) ---
# These are used in EconomyManager to calculate daily production from forest labor
const FORAGING_YIELD_GRAIN = 3.5   # Wild berries/grains (represents ~30% of farm yield)
const HUNTING_YIELD_MEAT = 2.5    # Meat from game
const HUNTING_YIELD_HIDES = 4.0   # Hides from game
const FORESTRY_YIELD_WOOD = 100.0  # Massive buff: woodcutters are now 10x more efficient
const FISHING_YIELD_BASE = 30.0    # Fixed to 30 to prevent massive fish surpluses

# --- BIOME EXTRACTION YIELDS (PER 360 DAYS, PER WORKER) ---
const PEAT_YIELD = 40.0           # Swamp fuel (lower than wood but easy access)
const CLAY_YIELD = 25.0           # Construction material
const SALT_YIELD = 15.0           # High value preservation agent
const SAND_YIELD = 60.0           # Desert construction material
const FUR_YIELD = 8.0             # Luxury tundra resource
const SIFTING_YIELD_GOLD = 0.5    # River sifting (very rare)
const SIFTING_YIELD_TIN = 2.0     # River sifting (industrial metal)

const PASTURE_YIELD_WOOL = 15.0    # Improved pastoral output
const PASTURE_YIELD_HIDES = 10.0
const PASTURE_YIELD_MEAT = 4.0
const PASTURE_YIELD_HORSES = 0.2   # 1 horse per 5 acres per year

# --- INDUSTRY & SOCIAL CLASSES ---
const MIN_LABORER_PERCENT = 0.5    # At least 50% of pop must be laborers for food security
const POP_PER_INDUSTRY_SLOT = 50   # 1 Micro-Workshop per 50 people
const BURGHERS_PER_SLOT = 5        # Each workshop employs exactly 5 burghers
const BURGHER_UPKEEP_CROWNS = 2    # Daily cost to support a burgher class
const CLOTH_CONSUMPTION_RATE = 0.005 # 1.8 units per person per year
const LEATHER_CONSUMPTION_RATE = 0.002 # 0.7 units per person per year
const NOBILITY_TARGET_PERCENT = 0.007 # 0.7% of population are nobility
const BURGHER_TARGET_PERCENT = 0.10  # 10% of population are burghers
const PRICE_MIN_MULT = 0.2
const PRICE_MAX_MULT = 4.0
const PRICE_ZERO_STOCK_MULT = 5.0
const MARKET_EXPORT_THRESHOLD_BASIC = 500
const MARKET_EXPORT_THRESHOLD_VALUE = 100
const WOOD_FUEL_POP_DIVISOR = 50.0 # 1 wood unit warms 50 people
const WOOD_FUEL_BUILDING_MULT = 0.5 # Each building level costs 0.5 wood/day

# --- LOGISTICS & CARAVANS ---
const CARAVAN_CAPACITY_BULK = 500
const CARAVAN_CAPACITY_VALUE = 100
const CARAVAN_TAX_THRESHOLD = 5000
const CARAVAN_PROFIT_DISTANCE_PENALTY = 10.0
const CARAVAN_BUILD_COST_CROWNS = 1000
const CARAVAN_BUILD_COST_WOOD = 250
const CARAVAN_BUILD_COST_HORSES = 10
const GUILD_CARAVANS_PER_LEVEL = 2
const VILLAGER_TRANSFER_FOOD_DAYS = 10
const VILLAGER_SUPPORT_THRESHOLD_DAYS = 3
const VILLAGER_SUPPORT_SEND_DAYS = 14
const VILLAGER_SUPPORT_CITY_MIN_DAYS = 30

# --- MILITARY & FACTIONS ---
const LORD_UPKEEP_PER_UNIT = 2
const LORD_DESERTION_RATE = 0.1
const RECRUITMENT_COST = 500
const RECRUITMENT_COUNT = 10
const FACTION_STARTING_TREASURY = 5000

# --- SETTLEMENT DEVELOPMENT ---
const HAMLET_PROMOTION_STABILITY = 50
const VILLAGE_PROMOTION_POP = 500
const SETTLER_PARTY_COST = 5000
const CITY_EXPANSION_CROWNS = 1000
const CITY_EXPANSION_GRAIN = 100
const SPONSOR_BUILDING_INFLUENCE = 10

# --- PLAYER FOUNDING ---
const PLAYER_FOUND_COST_CROWNS = 5000
const PLAYER_FOUND_COST_GRAIN = 500
const PLAYER_FOUND_BUILD_DAYS = 14
const PLAYER_FOUND_MIN_DIST = 10
const DONATE_RESOURCE_INFLUENCE_DIVISOR = 10.0
const WOOD_PER_HOUSE = 20
const LABOR_PER_HOUSE = 50.0 
