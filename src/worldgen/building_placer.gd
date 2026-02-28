## BuildingPlacer — stamps building templates onto region cells for each settlement.
##
## Called once during worldgen, after SettlementPlacer and route generation
## have finished building the WorldState. Pure data pass: no scene, no UI.
##
## For each settlement:
##   1. Computes territory cells (Chebyshev radius around anchor tile, tier-scaled).
##   2. Claims unclaimed land cells for the settlement.
##   3. Assigns building IDs from a tier-scaled distribution.
##   4. Compiles labor_slots on SettlementState from building templates.
##   5. Seeds market_inventory with starter goods proportional to tier.
class_name BuildingPlacer

# ── Constants ─────────────────────────────────────────────────────────────────

## Chebyshev (Chebyshev = max(|dx|,|dy|)) territory radius by tier.
## Tier 0 → 1 (3×3 = 9 cells max); tier 4 → 5 (11×11 = 121 cells max).
const TIER_RADIUS: Array[int] = [1, 2, 3, 4, 5]

## Tier-scaled building counts. -1 = "fill remaining with this type".
## Key: building_id. Value: count (or -1 = remainder fill).
const TIER_DISTRIBUTION: Array[Dictionary] = [
	# tier 0 — hamlet
	{"inn": 1, "well": 1, "farm_plot": 2, "house": 2, "open_land": -1},
	# tier 1 — village
	{"inn": 1, "well": 1, "farm_plot": 5, "granary": 1, "house": 5, "open_land": -1},
	# tier 2 — town
	{"inn": 2, "well": 2, "farm_plot": 8, "granary": 2, "market_stall": 1, "open_land": 4, "house": -1},
	# tier 3 — city
	{"inn": 3, "well": 3, "farm_plot": 14, "granary": 3, "market_stall": 3, "open_land": 6, "house": -1},
	# tier 4 — metropolis
	{"inn": 5, "well": 4, "farm_plot": 24, "granary": 5, "market_stall": 6, "open_land": 8, "house": -1},
]

## Starter market goods and per-tier base quantities (multiplied by tier+1).
const MARKET_STARTER: Dictionary = {
	"wheat_bushel": 30.0,
	"timber_log":    8.0,
	"coin":         20.0,
}


# ── Entry point ───────────────────────────────────────────────────────────────

## Run building placement for every settlement in world_state.
## Modifies world_state in place (settlement labor_slots, market_inventory,
## territory_cell_ids; region_cell dicts gain building_id and owner fields).
static func place(world_state: WorldState, world_seed: int) -> void:
	for sid: String in world_state.settlements:
		var ss: SettlementState = world_state.get_settlement(sid)
		if ss == null:
			push_warning("BuildingPlacer: settlement '%s' is null — skipping." % sid)
			continue
		_place_settlement(world_state, ss, world_seed)


# ── Per-settlement placement ──────────────────────────────────────────────────

static func _place_settlement(
		world_state: WorldState,
		ss: SettlementState,
		world_seed: int) -> void:

	var rng := RandomNumberGenerator.new()
	rng.seed = hash(ss.settlement_id) ^ world_seed

	var tier:   int = clampi(ss.tier, 0, TIER_RADIUS.size() - 1)
	var radius: int = TIER_RADIUS[tier]
	var tx:     int = ss.tile_x
	var ty:     int = ss.tile_y

	# ── 1. Collect territory cells ─────────────────────────────────────────
	var territory: Array[String] = []
	for cx: int in range(tx - radius, tx + radius + 1):
		for cy: int in range(ty - radius, ty + radius + 1):
			var cid := "%d,%d" % [cx, cy]
			if not world_state.world_tiles.has(cid):
				continue
			var cell: Dictionary = world_state.world_tiles[cid]
			if cell.get("is_water", true):
				continue
			var owner: String = cell.get("owner_settlement_id", "")
			if owner != "" and owner != ss.settlement_id:
				continue  # already claimed by another settlement
			territory.append(cid)

	ss.territory_cell_ids.assign(territory)

	# ── 2. Claim cells ─────────────────────────────────────────────────────
	for cid: String in territory:
		world_state.world_tiles[cid]["owner_settlement_id"] = ss.settlement_id

	# ── 3. Build ordered placement list ────────────────────────────────────
	var dist: Dictionary = TIER_DISTRIBUTION[tier]
	var to_place: Array[String] = []
	var fill_type: String = "open_land"
	for btype: String in dist:
		if dist[btype] == -1:
			fill_type = btype
			continue
		for _i: int in dist[btype]:
			to_place.append(btype)

	# Fisher-Yates shuffle of to_place using seeded rng.
	for i: int in range(to_place.size() - 1, 0, -1):
		var j: int = rng.randi_range(0, i)
		var tmp: String = to_place[i]
		to_place[i] = to_place[j]
		to_place[j] = tmp

	# ── 4. Stamp buildings onto cells ──────────────────────────────────────
	# Ensure the anchor cell is always the first territory cell to get a building.
	var anchor_cid := "%d,%d" % [tx, ty]
	var ordered_territory: Array[String] = []
	if territory.has(anchor_cid):
		ordered_territory.append(anchor_cid)
	for cid: String in territory:
		if cid != anchor_cid:
			ordered_territory.append(cid)

	var labor_slots: Array = []
	var place_idx: int = 0

	for cid: String in ordered_territory:
		var bid: String
		if place_idx < to_place.size():
			bid = to_place[place_idx]
			place_idx += 1
		else:
			bid = fill_type
		world_state.world_tiles[cid]["building_id"] = bid

		# Stamp z_levels from the building template.
		var z_lev := [0]
		if bid != "open_land" and bid != "derelict":
			var bdef: Dictionary = ContentRegistry.get_content("building", bid)
			z_lev = bdef.get("z_levels", [0])
			# Harvest labor slots.
			for slot_tmpl: Dictionary in bdef.get("labor_slots", []):
				var count: int = int(slot_tmpl.get("count", 1))
				for _k: int in count:
					labor_slots.append({
						"slot_id":        slot_tmpl.get("slot_id",  "worker"),
						"building_id":    bid,
						"cell_id":        cid,
						"wage_per_day":   slot_tmpl.get("wage_per_day", 1),
						"skill_required": slot_tmpl.get("skill_required", ""),
						"is_filled":      false,
						"worker_id":      "",
					})
		world_state.world_tiles[cid]["z_levels"] = z_lev

	ss.labor_slots = labor_slots

	# ── 5. Seed market inventory ───────────────────────────────────────────
	var tier_mult: float = float(ss.tier + 1)
	var market: Dictionary = {}
	for good: String in MARKET_STARTER:
		market[good] = MARKET_STARTER[good] * tier_mult
	ss.market_inventory = market
