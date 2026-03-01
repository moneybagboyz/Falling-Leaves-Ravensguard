## CampManager — player-initiated camp founding.
##
## Static API. Called from SettlementView when the player interacts with
## an open_land tile outside all settlement territories and confirms founding.
##
## A camp is a SettlementState with tier=0, is_player_camp=true.
## The camp's coin pool is keyed in property_ledger as "camp:<settlement_id>"
## → owner_person_id, so SettlementPulse can redirect income to the owner.
class_name CampManager
extends RefCounted

## Resources consumed from player's carried_items to found a camp.
const FOUNDING_COST: Dictionary = {
	"timber_log": 5,
}

## Name templates.
const NAME_PREFIXES: Array[String] = [
	"Raven's", "Grey", "Iron", "Warden's", "Old", "Black", "River",
]
const NAME_SUFFIXES: Array[String] = [
	"Camp", "Post", "Encampment", "Outpost", "Refuge", "Hold",
]


## Attempt to found a camp at `cell_id` (world tile key "wt_x,wt_y").
## Returns "" on success or an error string.
static func found_camp(
		player:  PersonState,
		cell_id: String,
		ws:      WorldState,
		rng:     RandomNumberGenerator) -> String:

	# ── Validation ────────────────────────────────────────────────────────────
	var tile: Dictionary = ws.world_tiles.get(cell_id, {})
	if tile.is_empty():
		return "Invalid location."

	var existing_bid: String = tile.get("building_id", "")
	if existing_bid != "" and existing_bid != "open_land":
		return "This tile is already occupied."

	# Ensure not inside a settlement territory.
	for sid: String in ws.settlements.keys():
		var ss: SettlementState = ws.get_settlement(sid)
		if ss == null:
			continue
		if cell_id in ss.territory_cell_ids:
			return "This tile belongs to %s." % ss.name

	# ── Resource check ────────────────────────────────────────────────────────
	for good_id: String in FOUNDING_COST.keys():
		var needed: int = FOUNDING_COST[good_id]
		var held: int   = player.carried_items.count(good_id)
		if held < needed:
			return "Need %d %s to found a camp (have %d)." % [needed, good_id, held]

	# ── Deduct resources ──────────────────────────────────────────────────────
	for good_id: String in FOUNDING_COST.keys():
		var needed: int = FOUNDING_COST[good_id]
		for _i: int in range(needed):
			var idx: int = player.carried_items.find(good_id)
			if idx >= 0:
				player.carried_items.remove_at(idx)

	# ── Create SettlementState ────────────────────────────────────────────────
	var parts: PackedStringArray = cell_id.split(",")
	var wt_x: int = int(parts[0]) if parts.size() == 2 else 0
	var wt_y: int = int(parts[1]) if parts.size() == 2 else 0

	var camp := SettlementState.new()
	camp.settlement_id  = EntityRegistry.generate_id("settlement")
	camp.name           = _random_name(rng)
	camp.cell_id        = cell_id
	camp.faction_id     = ""
	camp.tier           = 0
	camp.tile_x         = wt_x
	camp.tile_y         = wt_y
	camp.is_hub         = false
	camp.is_player_camp = true
	camp.province_id    = tile.get("province_id", "")
	camp.population     = { "peasant": 0 }
	camp.prosperity     = 0.5
	camp.unrest         = 0.0
	camp.inventory      = { "wheat_bushel": 10.0, "coin": 0.0 }
	camp.territory_cell_ids.append(cell_id)

	ws.settlements[camp.settlement_id] = camp

	# ── Stamp tile ────────────────────────────────────────────────────────────
	tile["building_id"]    = "bandit_camp"  # visual: rough shelters
	tile["is_player_camp"] = true
	ws.world_tiles[cell_id] = tile

	# Invalidate cached sub-region so it regenerates with the camp.
	ws.region_grids.erase(cell_id)

	# ── Register ownership ────────────────────────────────────────────────────
	PropertyCore.register_ownership(ws, "camp:" + camp.settlement_id, player.person_id)
	player.ownership_refs.append("camp:" + camp.settlement_id)

	print("[CampManager] Camp '%s' founded at %s by '%s'." \
		% [camp.name, cell_id, player.person_id])
	return ""


## Returns Array of {settlement_id, name, cell_id} for all player-owned camps.
static func get_player_camps(player: PersonState, ws: WorldState) -> Array:
	var out: Array = []
	for sid: String in ws.settlements.keys():
		var ss: SettlementState = ws.get_settlement(sid)
		if ss == null or not ss.is_player_camp:
			continue
		var owner: String = ws.property_ledger.get("camp:" + sid, "")
		if owner != player.person_id:
			continue
		out.append({"settlement_id": sid, "name": ss.name, "cell_id": ss.cell_id})
	return out


# ── Helpers ───────────────────────────────────────────────────────────────────

static func _random_name(rng: RandomNumberGenerator) -> String:
	var prefix: String = NAME_PREFIXES[rng.randi() % NAME_PREFIXES.size()]
	var suffix: String = NAME_SUFFIXES[rng.randi() % NAME_SUFFIXES.size()]
	return prefix + " " + suffix
