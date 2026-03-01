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

## Maximum NPCs to spawn per settlement.
const MAX_NPC_PER_SETTLEMENT: int = 40

## Maps population_class → compatible labor slot_ids (first-match assignment).
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

## Idempotent version: only spawns NPCs for settlements that have none yet.
## Called after loading a save to transparently migrate old saves created before
## the persistent-NPC system was introduced.
static func ensure_spawned(world_state: WorldState, world_seed: int) -> void:
	# Build the set of settlement IDs that already have at least one NPC.
	var populated: Dictionary = {}
	for pid: String in world_state.characters:
		var p: PersonState = world_state.characters[pid]
		if p.home_settlement_id != "":
			populated[p.home_settlement_id] = true

	for sid: String in world_state.settlements:
		if populated.has(sid):
			continue
		var ss: SettlementState = world_state.get_settlement(sid)
		if ss != null:
			_spawn_settlement(world_state, ss, world_seed)


# ── Per-settlement spawner ────────────────────────────────────────────────────

static func _spawn_settlement(
		world_state: WorldState,
		ss: SettlementState,
		world_seed: int) -> void:

	var rng := RandomNumberGenerator.new()
	rng.seed = hash(ss.settlement_id) ^ world_seed

	var total_pop: int = ss.total_population()
	if total_pop == 0:
		return

	var class_counts: Dictionary = {}
	for cls: String in ss.population:
		var frac: float = float(ss.population[cls]) / float(total_pop)
		class_counts[cls] = maxi(1, int(frac * MAX_NPC_PER_SETTLEMENT)) if ss.population[cls] > 0 else 0

	# Cap total to MAX_NPC_PER_SETTLEMENT.
	var actual_total := 0
	for cls: String in class_counts:
		actual_total += class_counts[cls]
	if actual_total > MAX_NPC_PER_SETTLEMENT:
		for cls: String in class_counts:
			class_counts[cls] = int(float(class_counts[cls]) / float(actual_total) * MAX_NPC_PER_SETTLEMENT)

	# Build the list of unfilled labor slots.
	var open_slots: Array = []
	for slot: Dictionary in ss.labor_slots:
		if not bool(slot.get("is_filled", false)):
			open_slots.append(slot)

	# Spawn NPCs and register them as permanent characters.
	for cls: String in class_counts:
		var count: int = class_counts[cls]
		for _i: int in count:
			var npc := _make_npc(rng, ss, cls, open_slots, world_seed)
			world_state.characters[npc.person_id] = npc


# ── NPC factory ───────────────────────────────────────────────────────────────

static func _make_npc(
		rng: RandomNumberGenerator,
		ss: SettlementState,
		pop_class: String,
		open_slots: Array,
		_world_seed: int) -> PersonState:

	var npc := PersonState.new()
	npc.person_id          = EntityRegistry.generate_id("person")
	npc.name               = _random_name(rng)
	npc.population_class   = pop_class
	npc.home_settlement_id = ss.settlement_id
	npc.background_id      = CLASS_BACKGROUND.get(pop_class, "wanderer")
	npc.active_role        = pop_class

	# Assign to a compatible labor slot if one is available.
	var preferred_slots: Array = CLASS_SLOTS.get(pop_class, [])
	var assigned_slot_idx: int = -1
	for si: int in open_slots.size():
		var slot: Dictionary = open_slots[si]
		if slot.get("slot_id", "") in preferred_slots:
			assigned_slot_idx = si
			break

	var home_wt_key: String = ss.cell_id  # "wt_x,wt_y" world-tile key

	if assigned_slot_idx >= 0:
		var slot: Dictionary = open_slots[assigned_slot_idx]
		npc.work_cell_id     = slot.get("cell_id",     ss.cell_id)
		npc.active_role      = slot.get("slot_id",     pop_class)
		npc.home_building_id = slot.get("building_id", "")
		home_wt_key          = npc.work_cell_id
		open_slots.remove_at(assigned_slot_idx)
	else:
		# Unassigned — pick a random territory cell as home.
		if not ss.territory_cell_ids.is_empty():
			var idx := rng.randi_range(0, ss.territory_cell_ids.size() - 1)
			home_wt_key = ss.territory_cell_ids[idx]
		npc.work_cell_id = home_wt_key

	# Parse wt_x / wt_y from the key for the location dict.
	var parts := home_wt_key.split(",")
	var wt_x: int = int(parts[0]) if parts.size() == 2 else ss.tile_x
	var wt_y: int = int(parts[1]) if parts.size() == 2 else ss.tile_y

	# NPC starts on their home world tile; region/local coords resolved lazily.
	npc.location = {
		"wt_x": wt_x, "wt_y": wt_y,
		"rx":   -1,   "ry":   -1,   # resolved when the region grid is first generated
		"lx":   -1,   "ly":   -1,   # resolved when entering LocalView
		"z_level": 0,
	}
	npc.schedule_state = "working"

	return npc


static func _random_name(rng: RandomNumberGenerator) -> String:
	var use_masc := rng.randf() < 0.55
	var first_arr := NAMES_MASC if use_masc else NAMES_FEM
	var first: String = first_arr[rng.randi_range(0, first_arr.size() - 1)]
	var surname: String = SURNAMES[rng.randi_range(0, SURNAMES.size() - 1)]
	return "%s %s" % [first, surname]
