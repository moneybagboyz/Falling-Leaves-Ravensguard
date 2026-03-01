## WorldState — top-level simulation state for the active region.
##
## This is the authoritative simulation state. It is NEVER owned by a scene.
## All scenes and UI nodes receive read-only copies of sub-states or subscribe
## to change signals.
##
## Serialised by SaveManager to produce versioned save files.
class_name WorldState
extends RefCounted

## Stable ID for this region (EntityRegistry ID).
var region_id: String = ""

## World generation seed — must be preserved for determinism.
var world_seed: int = 0

## Current simulation tick when the save was created.
var current_tick: int = 0

## settlement_id -> SettlementState dict (or SettlementState instance).
var settlements: Dictionary = {}

## world_tile_key -> RegionCell data dict (terrain, elevation, resources, etc.)
## Key format: "x,y" in the 512×512 world grid.
var world_tiles: Dictionary = {}

## Lazily-generated sub-region grids, keyed by world tile key.
## Each value is a Dictionary of 250×250 region cells keyed by "rx,ry".
## Populated on demand by SubRegionGenerator when the player enters a tile.
var region_grids: Dictionary = {}

## Persistent inventory of chest tiles in the world, keyed by
## "wt_x,wt_y:building_id" (one chest per building per world tile).
## Value: Array of item-type ID strings. Populated from building default
## chest_items on first visit; mutated as the player takes or deposits items.
var chest_contents: Dictionary = {}

## faction_id -> faction runtime state dict.
var factions: Dictionary = {}

## Global flags set by simulation events.
var world_flags: Dictionary = {}

## Route network: settlement_id -> Array[{to_id: String, cost: float}]
var routes: Dictionary = {}

## Province names indexed by province int ID.
var province_names: Array = []

## Province adjacency: province_int_id -> Array[province_int_id]
var province_adjacency: Dictionary = {}

## Active trade parties: party_id -> TradePartyState dict.
var trade_parties: Dictionary = {}

## Ownership ledger: building_instance_id (or cell_id) -> owner_person_id.
## Written and read exclusively by PropertyCore.
var property_ledger: Dictionary = {}

## Active construction jobs: job_id -> ConstructionJob dict.
## Written by ConstructionSystem (P5-10).
var construction_jobs: Dictionary = {}

## Player's armed group state (GroupState serialised to dict). Empty dict = no group.
var player_group: Dictionary = {}

## All named characters (player + any persisted NPCs): person_id -> PersonState.
var characters: Dictionary = {}

## EntityRegistry ID of the player's PersonState in `characters`.
var player_character_id: String = ""

## Active tactical battle. Null when no battle is in progress (the normal state).
## Set by the combat trigger (P4-11); cleared by post-battle resolution (P4-19).
var active_battle: BattleState = null

## Player's current position in the world.
## cell_id (wt_key):  world tile key in the 512×512 grid ("wt_x,wt_y").
## wt_x / wt_y:       world tile coords (mirrors cell_id for convenience).
## rx / ry:           region cell coords within the current world tile (0–249).
## lx / ly:           local tile coords within the region cell (0–24). Unused until P4-01.
## z_level:           0 = ground, 1 = upper floor, -1 = cellar.
var player_location: Dictionary = {
	"cell_id": "",
	"wt_x":    0,
	"wt_y":    0,
	"rx":      125,
	"ry":      125,
	"lx":      0,
	"ly":      0,
	"z_level": 0,
}


func add_settlement(state: SettlementState) -> void:
	settlements[state.settlement_id] = state


func get_settlement(id: String) -> SettlementState:
	return settlements.get(id, null)


func has_settlement(id: String) -> bool:
	return settlements.has(id)


## Returns an settlement's fields as a plain Dictionary regardless of whether
## the in-memory value is a SettlementState object or a serialised dict.
## Prefer typed access via get_settlement(); use this only for rendering / debug.
func get_settlement_dict(id: String) -> Dictionary:
	var sv = settlements.get(id)
	if sv == null:
		return {}
	if sv is SettlementState:
		return sv.to_dict()
	return sv as Dictionary


func get_all_settlements() -> Array:
	return settlements.values()


# ---------------------------------------------------------------------------
# Serialisation
# ---------------------------------------------------------------------------

func to_dict() -> Dictionary:
	var settlements_dict := {}
	for sid in settlements:
		var sv = settlements[sid]
		if sv is SettlementState:
			settlements_dict[sid] = sv.to_dict()
		else:
			settlements_dict[sid] = sv
	return {
		"region_id":          region_id,
		"world_seed":         world_seed,
		"current_tick":       current_tick,
		"settlements":        settlements_dict,
		"world_tiles":        world_tiles.duplicate(true),
		"factions":           factions.duplicate(true),
		"world_flags":        world_flags.duplicate(true),
		"routes":             routes.duplicate(true),
		"trade_parties":      trade_parties.duplicate(true),
		"property_ledger":    property_ledger.duplicate(),
		"construction_jobs":  construction_jobs.duplicate(true),
		"player_group":       player_group.duplicate(true),
		"province_names":     province_names.duplicate(),
		"province_adjacency": province_adjacency.duplicate(true),
		"player_character_id": player_character_id,
		"player_location":    player_location.duplicate(),
		"chest_contents":     chest_contents.duplicate(true),
		"active_battle":      active_battle.to_dict() if active_battle != null else null,
		# Serialise all characters (player + persisted NPCs).
		# npc_pool removed (CDDA rewrite): all NPCs now live in characters.
		"characters": (func() -> Dictionary:
			var cd: Dictionary = {}
			for pid in characters:
				var pv = characters[pid]
				cd[pid] = pv.to_dict() if pv is PersonState else pv
			return cd).call(),
	}


static func from_dict(data: Dictionary) -> WorldState:
	var ws := WorldState.new()
	ws.region_id      = data.get("region_id",      "")
	ws.world_seed     = data.get("world_seed",      0)
	ws.current_tick   = data.get("current_tick",    0)
	ws.world_tiles    = data.get("world_tiles",     data.get("region_cells", {}))
	ws.factions       = data.get("factions",        {})
	ws.world_flags    = data.get("world_flags",     {})
	ws.routes             = data.get("routes",             {})
	ws.trade_parties      = data.get("trade_parties",      {})
	ws.property_ledger    = data.get("property_ledger",    {}).duplicate()
	ws.construction_jobs  = data.get("construction_jobs",  {}).duplicate(true)
	ws.player_group       = data.get("player_group",       {}).duplicate(true)
	ws.province_names     = data.get("province_names",     [])
	ws.province_adjacency = data.get("province_adjacency", {})
	ws.player_character_id = data.get("player_character_id", "")
	ws.player_location    = data.get("player_location", {
		"cell_id": "", "wt_x": 0, "wt_y": 0, "rx": 125, "ry": 125, "lx": 0, "ly": 0, "z_level": 0
	}).duplicate()
	ws.chest_contents     = data.get("chest_contents", {}).duplicate(true)
	var battle_data = data.get("active_battle", null)
	if battle_data is Dictionary:
		ws.active_battle = BattleState.from_dict(battle_data)
	var chars_data: Dictionary = data.get("characters", {})
	for pid in chars_data:
		var pv = chars_data[pid]
		if pv is Dictionary:
			ws.characters[pid] = PersonState.from_dict(pv)
		else:
			ws.characters[pid] = pv
	var settlements_data: Dictionary = data.get("settlements", {})
	for sid in settlements_data:
		var sv = settlements_data[sid]
		if sv is Dictionary:
			ws.settlements[sid] = SettlementState.from_dict(sv)
		else:
			ws.settlements[sid] = sv
	return ws
