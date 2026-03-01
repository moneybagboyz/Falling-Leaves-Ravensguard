## SettlementState — plain data class for one settlement's simulation state.
##
## Must never be owned by a scene. Scenes READ from this via the UI layer.
## Serialised/deserialised as part of WorldState.
class_name SettlementState
extends RefCounted

var settlement_id: String = ""
var name: String = ""
var cell_id: String = ""
var faction_id: String = ""

## Tier: 0=Hamlet, 1=Village, 2=Town, 3=City, 4=Metropolis
var tier: int = 0

## Grid position of the settlement's anchor tile.
var tile_x: int = 0
var tile_y: int = 0

## Whether this is a province hub (true) or spoke (false).
var is_hub: bool = false

## True for camps founded by the player (tier-0). Disables trade-party
## spawning and NPC population growth in SettlementPulse.
var is_player_camp: bool = false

## Province ID (string form of province int index).
var province_id: String = ""

## Fraction of expected road links actually built (0–1).
var connectivity_rate: float = 0.0

## Population by class. Keys are population class IDs (from dev plan: peasant,
## artisan, merchant, noble). Values are integer headcounts.
var population: Dictionary = {}

## 0.0–1.0. 0.5 is neutral. Drives growth/decline decisions.
var prosperity: float = 0.5

## 0.0–1.0. Rises with shortages, unpaid taxes, violence.
var unrest: float = 0.0

## Current stockpile: good_id -> float quantity.
var inventory: Dictionary = {}

## Current regional price for each good: good_id -> float price.
## Starts at base_value; updated each production pulse by PriceLedger.
var prices: Dictionary = {}

## Short-term shortages this pulse: good_id -> float (demand unmet).
## Resets to {} at the start of each SettlementPulse tick.
var shortages: Dictionary = {}

## Rolling log of recent production events (last 20 entries).
## Each entry: {tick, recipe_id, amount, note}. Debug and UI use only.
var production_log: Array = []
const PRODUCTION_LOG_MAX: int = 20

## List of building instance IDs (EntityRegistry IDs) present here.
var buildings: Array = []

## Ordered list of labor slots available in this settlement.
## Each entry: {slot_id, building_id, cell_id, role_id, is_filled: bool, worker_id: String}
## Built by BuildingPlacer (P3-06) and updated as workers are hired.
var labor_slots: Array = []

## Housing slots — one entry per building that has housing_capacity > 0.
## Each entry: {building_id, cell_id, capacity}
## Built by BuildingPlacer alongside labor_slots.
var housing_slots: Array = []

## Player-visible market stock: good_id -> float quantity.
## Refreshed each production pulse as a fraction of the ledger inventory surplus.
## Distinct from `inventory` which is bulk simulation state.
var market_inventory: Dictionary = {}

## Ordered list of cell_ids ("x,y") owned by this settlement.
## Populated by BuildingPlacer; used by SettlementView and BuildingPlacer output.
var territory_cell_ids: Array[String] = []

## Acreage ledger (populated in Phase 2).
var acreage: Dictionary = {
	"total_acres":    0,
	"arable_acres":   0,
	"worked_acres":   0,
	"fallow_acres":   0,
	"pasture_acres":  0,
	"woodlot_acres":  0,
}


func total_population() -> int:
	var total := 0
	for cls in population:
		total += population[cls]
	return total


# ---------------------------------------------------------------------------
# Serialisation
# ---------------------------------------------------------------------------

func to_dict() -> Dictionary:
	return {
		"settlement_id":    settlement_id,
		"name":             name,
		"cell_id":          cell_id,
		"faction_id":       faction_id,
		"tier":             tier,
		"tile_x":           tile_x,
		"tile_y":           tile_y,
		"is_hub":           is_hub,
		"is_player_camp":   is_player_camp,
		"province_id":      province_id,
		"connectivity_rate": connectivity_rate,
		"population":       population.duplicate(),
		"prosperity":       prosperity,
		"unrest":           unrest,
		"inventory":        inventory.duplicate(),
		"prices":           prices.duplicate(),
		"shortages":        shortages.duplicate(),
		"production_log":   production_log.duplicate(),
		"buildings":        buildings.duplicate(),
		"labor_slots":      labor_slots.duplicate(true),
		"housing_slots":    housing_slots.duplicate(true),
		"market_inventory": market_inventory.duplicate(),
		"territory_cell_ids": territory_cell_ids.duplicate(),
		"acreage":          acreage.duplicate(),
	}


static func from_dict(data: Dictionary) -> SettlementState:
	var s := SettlementState.new()
	s.settlement_id     = data.get("settlement_id",    "")
	s.name              = data.get("name",             "")
	s.cell_id           = data.get("cell_id",          "")
	s.faction_id        = data.get("faction_id",       "")
	s.tier              = data.get("tier",             0)
	s.tile_x            = data.get("tile_x",           0)
	s.tile_y            = data.get("tile_y",           0)
	s.is_hub            = data.get("is_hub",           false)
	s.is_player_camp    = data.get("is_player_camp",   false)
	s.province_id       = data.get("province_id",      "")
	s.connectivity_rate = data.get("connectivity_rate", 0.0)
	s.population        = data.get("population",       {})
	s.prosperity        = data.get("prosperity",       0.5)
	s.unrest            = data.get("unrest",           0.0)
	s.inventory         = data.get("inventory",        {})
	s.prices            = data.get("prices",            {})
	s.shortages         = data.get("shortages",         {})
	s.production_log    = data.get("production_log",    [])
	s.buildings         = data.get("buildings",         [])
	s.labor_slots       = data.get("labor_slots",       []).duplicate(true)
	s.housing_slots     = data.get("housing_slots",     []).duplicate(true)
	s.market_inventory  = data.get("market_inventory",  {}).duplicate()
	s.territory_cell_ids.assign(data.get("territory_cell_ids", []))
	s.acreage           = data.get("acreage",          {
		"total_acres": 0, "arable_acres": 0, "worked_acres": 0,
		"fallow_acres": 0, "pasture_acres": 0, "woodlot_acres": 0,
	})
	return s
