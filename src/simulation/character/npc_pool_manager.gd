## NpcPoolManager — spawns persistent NPC characters for all settlements.
##
## CDDA-style: NPCs are born once at world-gen time and stored permanently in
## WorldState.characters. There is no transient pool, no cull, no re-generation.
##
## Entry points:
##   spawn_all(ws, seed)      — called once by RegionGenerator at end of world-gen.
##   ensure_spawned(ws, seed) — idempotent; called on save load for old-save migration.
class_name NpcPoolManager

# ── Name tables ───────────────────────────────────────────────────────────────
const NAMES_MASC: Array[String] = [
	"Aldric", "Edmund", "Gareth", "Harold", "Oswin", "Cormac",
	"Bryn",   "Leofric","Wulfric","Edric",  "Bertram","Godwin",
	"Hereward","Alden", "Roland", "Ulfric", "Sigurd", "Erwin",
	"Aelfric","Dunstan","Osbern", "Renald", "Caelan", "Merric",
]
const NAMES_FEM: Array[String] = [
	"Maren",  "Sigrid", "Edith",  "Rowena", "Isolde", "Aelith",
	"Gwyneth","Bertha", "Hildred","Morwen", "Seren",  "Aldith",
	"Brunhild","Ceridwen","Elspeth","Frieda","Ingrid", "Mildred",
	"Nessa",  "Oswyn",  "Petra",  "Rhoswen","Sylva",  "Thilda",
]
const SURNAMES: Array[String] = [
	"Blackwood","Cooper", "Miller", "Tanner",  "Smith",   "Fletcher",
	"Thatcher", "Mason",  "Carter", "Wheeler", "Sawyer",  "Fisher",
	"Hunter",   "Baker",  "Weaver", "Fuller",  "Chandler","Parker",
	"Turner",   "Harper", "Ward",   "Garrett", "Finch",   "Hollow",
]

## Maps population_class → compatible labor slot_ids.
const CLASS_SLOTS: Dictionary = {
	"peasant":  ["farm_hand", "grain_keeper", "woodcutter", "laborer"],
	"artisan":  ["smith",     "carpenter",    "tanner",    "brewer"],
	"merchant": ["trader",    "innkeeper",    "server",    "market_keeper"],
	"noble":    ["steward",   "guard_captain","official"],
}
## Background ID to use for each population class.
const CLASS_BACKGROUND: Dictionary = {
	"peasant":  "farmer",
	"artisan":  "farmer",      # best available until artisan background is added
	"merchant": "merchant",
	"noble":    "wanderer",    # placeholder
}


# ── Entry point: spawn_all ────────────────────────────────────────────────────

## Spawn NPCs for every settlement into world_state.characters.
## Called once at the end of world generation.
static func spawn_all(world_state: WorldState, world_seed: int) -> void:
	for sid: String in world_state.settlements:
		var ss: SettlementState = world_state.get_settlement(sid)
		if ss != null:
			_spawn_settlement(world_state, ss, world_seed)


# ── Entry point: ensure_spawned ───────────────────────────────────────────────

## Idempotent version: spawns NPCs for settlements with missing coverage.
## Called after loading a save to transparently migrate old saves created before
## the persistent-NPC system was introduced.
##   • Settlements with zero NPCs → full _spawn_settlement (workers + residents).
##   • Settlements with workers but zero residents → residents-only pass from
##     housing_slots (handles saves from before the resident system was added).
static func ensure_spawned(world_state: WorldState, world_seed: int) -> void:
	# Build per-settlement presence flags.
	var has_workers:   Dictionary = {}
	var has_residents: Dictionary = {}
	for pid: String in world_state.characters:
		var p: PersonState = world_state.characters[pid]
		if p.home_settlement_id == "":
			continue
		if p.active_role == "resident":
			has_residents[p.home_settlement_id] = true
		else:
			has_workers[p.home_settlement_id] = true

	for sid: String in world_state.settlements:
		var ss: SettlementState = world_state.get_settlement(sid)
		if ss == null:
			continue
		if not has_workers.has(sid) and not has_residents.has(sid):
			# No NPCs at all — full spawn.
			_spawn_settlement(world_state, ss, world_seed)
		elif not has_residents.has(sid):
			# Workers exist but residents were never created.
			# Use ss.housing_slots if available; otherwise re-harvest from world_tiles
			# (handles saves created before housing_slots was persisted).
			var slots: Array = ss.housing_slots
			if slots.is_empty() and ss.territory_cell_ids.size() > 0:
				for cid: String in ss.territory_cell_ids:
					var bid: String = world_state.world_tiles.get(cid, {}).get("building_id", "")
					if bid == "" or bid == "open_land" or bid == "derelict":
						continue
					var bdef: Dictionary = ContentRegistry.get_content("building", bid)
					var cap: int = int(bdef.get("housing_capacity", 0))
					if cap > 0:
						slots.append({"building_id": bid, "cell_id": cid, "capacity": cap})
			if slots.is_empty():
				continue
			var rng := RandomNumberGenerator.new()
			rng.seed = hash(ss.settlement_id) ^ world_seed
			for slot: Dictionary in slots:
				var capacity: int   = int(slot.get("capacity", 0))
				var cell_id: String = slot.get("cell_id",     ss.cell_id)
				var bid: String     = slot.get("building_id", "")
				for _i: int in capacity:
					var pop_class := _pick_resident_class(rng, ss)
					var npc := _make_resident(rng, ss, pop_class, cell_id, bid)
					world_state.characters[npc.person_id] = npc


# ── Per-settlement spawner ────────────────────────────────────────────────────
## NPC count is driven entirely by placed buildings — no flat cap.
##   Workers: one NPC per unfilled labor slot (class from slot type).
##   Residents: one NPC per housing_capacity unit (class proportional to population).
## Total is naturally bounded by the settlement's physical footprint.

static func _spawn_settlement(
		world_state: WorldState,
		ss: SettlementState,
		world_seed: int) -> void:

	var rng := RandomNumberGenerator.new()
	rng.seed = hash(ss.settlement_id) ^ world_seed

	# 1. One NPC per unfilled labor slot.
	for idx: int in ss.labor_slots.size():
		var slot: Dictionary = ss.labor_slots[idx]
		if slot.get("is_filled", false):
			continue
		var pop_class := _class_for_slot(slot.get("slot_id", ""))
		var npc := _make_worker(rng, ss, pop_class, slot)
		world_state.characters[npc.person_id] = npc
		ss.labor_slots[idx]["is_filled"] = true
		ss.labor_slots[idx]["worker_id"] = npc.person_id

	# 2. Residents per housing slot.
	for slot: Dictionary in ss.housing_slots:
		var capacity: int   = int(slot.get("capacity", 0))
		var cell_id: String = slot.get("cell_id",     ss.cell_id)
		var bid: String     = slot.get("building_id", "")
		for _i: int in capacity:
			var pop_class := _pick_resident_class(rng, ss)
			var npc := _make_resident(rng, ss, pop_class, cell_id, bid)
			world_state.characters[npc.person_id] = npc


# ── NPC factories ─────────────────────────────────────────────────────────────

static func _make_worker(
		rng: RandomNumberGenerator,
		ss: SettlementState,
		pop_class: String,
		slot: Dictionary) -> PersonState:

	var npc := PersonState.new()
	npc.person_id          = EntityRegistry.generate_id("person")
	npc.name               = _random_name(rng)
	npc.population_class   = pop_class
	npc.home_settlement_id = ss.settlement_id
	npc.background_id      = CLASS_BACKGROUND.get(pop_class, "wanderer")
	npc.active_role        = slot.get("slot_id", pop_class)
	npc.work_cell_id       = slot.get("cell_id", ss.cell_id)
	npc.home_building_id   = slot.get("building_id", "")

	var parts := npc.work_cell_id.split(",")
	npc.location = {
		"wt_x": int(parts[0]) if parts.size() == 2 else ss.tile_x,
		"wt_y": int(parts[1]) if parts.size() == 2 else ss.tile_y,
		"rx": -1, "ry": -1,
		"lx": -1, "ly": -1,
		"z_level": 0,
	}
	npc.schedule_state = "working"
	return npc


static func _make_resident(
		rng: RandomNumberGenerator,
		ss: SettlementState,
		pop_class: String,
		cell_id: String,
		building_id: String) -> PersonState:

	var npc := PersonState.new()
	npc.person_id          = EntityRegistry.generate_id("person")
	npc.name               = _random_name(rng)
	npc.population_class   = pop_class
	npc.home_settlement_id = ss.settlement_id
	npc.background_id      = CLASS_BACKGROUND.get(pop_class, "wanderer")
	npc.active_role        = "resident"
	npc.work_cell_id       = cell_id   # home cell doubles as "where they spend time"
	npc.home_building_id   = building_id

	var parts := cell_id.split(",")
	npc.location = {
		"wt_x": int(parts[0]) if parts.size() == 2 else ss.tile_x,
		"wt_y": int(parts[1]) if parts.size() == 2 else ss.tile_y,
		"rx": -1, "ry": -1,
		"lx": -1, "ly": -1,
		"z_level": 0,
	}
	npc.schedule_state = "resting"
	return npc


## Map a labor slot_id to the population class that fills it.
static func _class_for_slot(slot_id: String) -> String:
	if slot_id in ["farm_hand", "grain_keeper", "woodcutter", "laborer"]:
		return "peasant"
	if slot_id in ["smith", "carpenter", "tanner", "brewer"]:
		return "artisan"
	if slot_id in ["innkeeper", "server", "trader", "market_keeper"]:
		return "merchant"
	if slot_id in ["steward", "guard_captain", "official"]:
		return "noble"
	return "peasant"


## Pick a resident population class weighted by the settlement's headcounts.
static func _pick_resident_class(rng: RandomNumberGenerator, ss: SettlementState) -> String:
	var total := ss.total_population()
	if total <= 0:
		return "peasant"
	var roll := rng.randi_range(0, total - 1)
	var acc := 0
	for cls: String in ss.population:
		acc += ss.population[cls]
		if roll < acc:
			return cls
	return "peasant"


static func _random_name(rng: RandomNumberGenerator) -> String:
	var use_masc := rng.randf() < 0.55
	var first_arr := NAMES_MASC if use_masc else NAMES_FEM
	var first: String = first_arr[rng.randi_range(0, first_arr.size() - 1)]
	var surname: String = SURNAMES[rng.randi_range(0, SURNAMES.size() - 1)]
	return "%s %s" % [first, surname]
