## WorkshopManager — handles player purchase and build initiation for workshops.
##
## Purchase path: player buys an unowned production building from a settlement.
## Build path:    player commissions a new building on an open_land tile,
##                deducting resources and creating a ConstructionJob.
##
## Static API. Called from SettlementView / LocalView interaction dialogs.
class_name WorkshopManager
extends RefCounted

## Categories considered "production buildings" for the purchase/build UI.
const PRODUCTION_CATEGORIES: Array[String] = [
	"production", "extraction", "crafting",
]


# ── Purchase path ─────────────────────────────────────────────────────────────

## Attempt to purchase ownership of building at `instance_key` in settlement.
## instance_key format: "building_id:cell_id"
## Returns "" on success or an error string.
static func purchase(
		player:       PersonState,
		instance_key: String,
		ss:           SettlementState,
		ws:           WorldState) -> String:

	# Already owned?
	var current_owner: String = PropertyCore.owner_of(ws, instance_key)
	if current_owner != "":
		if current_owner == player.person_id:
			return "You already own this building."
		return "This building is already owned by someone else."

	# Derive building id from instance_key ("building_id:cell_id").
	var parts: PackedStringArray = instance_key.split(":", true, 1)
	if parts.size() < 2:
		return "Invalid building reference."
	var bid: String = parts[0]

	var bdef: Dictionary = ContentRegistry.get_content("building", bid)
	if bdef.is_empty():
		return "Unknown building type '%s'." % bid

	var cat: String = bdef.get("category", "")
	if cat not in PRODUCTION_CATEGORIES:
		return "Only production buildings can be purchased."

	# Purchase price = construction_cost.coin (or 50 default).
	var price: float = float(
		(bdef.get("construction_cost", {}) as Dictionary).get("coin", 50.0))

	if player.coin < price:
		return "Not enough coin (need %.0f, have %.0f)." % [price, player.coin]

	# Execute.
	player.coin -= price
	PropertyCore.register_ownership(ws, instance_key, player.person_id)
	if instance_key not in player.ownership_refs:
		player.ownership_refs.append(instance_key)

	# Small reputation gain in settlement for investment.
	ReputationEvents.gain(player, "trade_completed", [ss.settlement_id])

	print("[WorkshopManager] '%s' purchased '%s'." % [player.person_id, instance_key])
	return ""


# ── Build path ────────────────────────────────────────────────────────────────

## Initiate construction of `building_id` at world-tile `cell_id`.
## Returns "" on success or an error string.
static func begin_construction(
		player:      PersonState,
		building_id: String,
		cell_id:     String,
		settlement_id: String,
		ws:          WorldState) -> String:

	# Validate tile.
	var tile: Dictionary = ws.world_tiles.get(cell_id, {})
	if tile.is_empty():
		return "Invalid location."
	var existing: String = tile.get("building_id", "")
	if existing != "" and existing != "open_land":
		return "Tile is already occupied."

	# Validate building definition.
	var bdef: Dictionary = ContentRegistry.get_content("building", building_id)
	if bdef.is_empty():
		return "Unknown building type '%s'." % building_id
	var cat: String = bdef.get("category", "")
	if cat not in PRODUCTION_CATEGORIES:
		return "Only production buildings can be constructed this way."

	# Check and deduct resources from player's carried_items or linked camp stock.
	var cost: Dictionary = bdef.get("construction_cost", {})
	var error: String = _deduct_construction_resources(player, cost, ws)
	if error != "":
		return error

	# Create the job.
	var job := ConstructionJob.new()
	job.job_id          = EntityRegistry.generate_id("job")
	job.building_id     = building_id
	job.cell_id         = cell_id
	job.owner_id        = player.person_id
	job.settlement_id   = settlement_id
	job.ticks_remaining = float(cost.get("labor_days", 30))
	job.resources_committed = _filter_goods(cost)
	job.started         = true
	ws.construction_jobs[job.job_id] = job.to_dict()

	# Mark tile as "under construction" so SubRegionGenerator shows scaffolding.
	tile["building_id"]          = "open_land"
	tile["construction_job_id"]  = job.job_id
	ws.world_tiles[cell_id]      = tile
	ws.region_grids.erase(cell_id)

	print("[WorkshopManager] Construction of '%s' started at %s (job %s)."
		% [building_id, cell_id, job.job_id])
	return ""


## Returns buildings that can be purchased in a settlement (unowned + production).
static func get_purchasable(ss: SettlementState, ws: WorldState) -> Array:
	var out: Array = []
	for slot: Dictionary in ss.labor_slots:
		var bid: String  = slot.get("building_id", "")
		var cid: String  = slot.get("cell_id",     "")
		if bid == "" or cid == "":
			continue
		var bdef: Dictionary = ContentRegistry.get_content("building", bid)
		if bdef.get("category", "") not in PRODUCTION_CATEGORIES:
			continue
		var key: String = bid + ":" + cid
		if PropertyCore.owner_of(ws, key) != "":
			continue  # already owned
		var price: float = float(
			(bdef.get("construction_cost", {}) as Dictionary).get("coin", 50.0))
		out.append({
			"instance_key": key,
			"building_id":  bid,
			"cell_id":      cid,
			"name":         bdef.get("name", bid),
			"price":        price,
		})
		# Deduplicate: only first slot per building instance.
		var keys_seen: Array = []
		out = out.filter(func(e: Dictionary) -> bool:
			if e["instance_key"] in keys_seen:
				return false
			keys_seen.append(e["instance_key"])
			return true)
	return out


## Returns buildable categories visible to the player for the build dialog.
static func get_buildable_types() -> Array:
	var out: Array = []
	var all: Dictionary = ContentRegistry.get_all("building")
	for bid: String in all.keys():
		var bdef: Dictionary = all[bid]
		if bdef.get("category", "") in PRODUCTION_CATEGORIES:
			var cost: Dictionary = bdef.get("construction_cost", {})
			out.append({
				"building_id": bid,
				"name":        bdef.get("name", bid),
				"cost":        cost,
				"description": bdef.get("description", ""),
			})
	return out


# ── Helpers ───────────────────────────────────────────────────────────────────

static func _deduct_construction_resources(
		player: PersonState,
		cost: Dictionary,
		ws: WorldState) -> String:

	for key: String in cost.keys():
		if key in ["coin", "labor_days"]:
			continue
		var needed: int = int(cost[key])
		var held: int   = player.carried_items.count(key)

		# Also check camp stock.
		var camp_have: float = 0.0
		var camp_ss: SettlementState = null
		for sid: String in ws.settlements:
			var ss: SettlementState = ws.get_settlement(sid)
			if ss != null and ss.is_player_camp:
				var owner: String = ws.property_ledger.get("camp:" + sid, "")
				if owner == player.person_id:
					camp_have = float(ss.inventory.get(key, 0.0))
					camp_ss   = ss
					break

		if held + int(camp_have) < needed:
			return "Need %d %s (have %d in inventory + %.0f in camp)." \
				% [needed, key, held, camp_have]

		# Deduct from carried first, then camp.
		var remain: int = needed
		var from_inv: int = mini(held, remain)
		for _i: int in range(from_inv):
			var idx: int = player.carried_items.find(key)
			if idx >= 0:
				player.carried_items.remove_at(idx)
		remain -= from_inv
		if remain > 0 and camp_ss != null:
			camp_ss.inventory[key] = maxf(float(camp_ss.inventory.get(key, 0.0)) - remain, 0.0)

	# Coin deduction.
	var coin_cost: float = float(cost.get("coin", 0.0))
	if player.coin < coin_cost:
		return "Need %.0f coin (have %.0f)." % [coin_cost, player.coin]
	player.coin -= coin_cost

	return ""


static func _filter_goods(cost: Dictionary) -> Dictionary:
	var out: Dictionary = {}
	for k: String in cost.keys():
		if k not in ["labor_days"]:
			out[k] = cost[k]
	return out
