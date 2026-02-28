## NpcPoolManager — generates and culls NPC records for SettlementView.
##
## Called on map entry to populate WorldState.npc_pool with up to 40 NPC
## PersonState records per settlement (generated from headcount data).
## Called on map exit to cull transient NPCs (keeping only those in the
## player's social_links, which are merged into WorldState.characters).
##
## Static class — no instance required.
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

## Maximum NPCs to instantiate per settlement.
const MAX_NPC_POOL: int = 40

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


# ── Entry point: populate ─────────────────────────────────────────────────────

## If no NPCs for settlement_id are in world_state.npc_pool, generate them.
## world_seed is combined with settlement_id for determinism.
static func populate(
		world_state: WorldState,
		ss: SettlementState,
		world_seed: int) -> void:

	# Check if already populated.
	for pid: String in world_state.npc_pool:
		var npc: PersonState = world_state.npc_pool[pid]
		if npc.home_settlement_id == ss.settlement_id:
			return  # already done

	var rng := RandomNumberGenerator.new()
	rng.seed = hash(ss.settlement_id) ^ world_seed

	# Compute how many NPCs of each class to spawn (proportional to headcount).
	var total_pop: int = ss.total_population()
	if total_pop == 0:
		return

	var class_counts: Dictionary = {}
	for cls: String in ss.population:
		var frac: float = float(ss.population[cls]) / float(total_pop)
		class_counts[cls] = maxi(1, int(frac * MAX_NPC_POOL)) if ss.population[cls] > 0 else 0
	# Cap total to MAX_NPC_POOL.
	var actual_total := 0
	for cls: String in class_counts:
		actual_total += class_counts[cls]
	if actual_total > MAX_NPC_POOL:
		# Scale down proportionally.
		for cls: String in class_counts:
			class_counts[cls] = int(float(class_counts[cls]) / float(actual_total) * MAX_NPC_POOL)

	# Build an inventory of unassigned labor slots.
	var open_slots: Array = []
	for slot: Dictionary in ss.labor_slots:
		if not bool(slot.get("is_filled", false)):
			open_slots.append(slot)

	# Spawn NPCs.
	for cls: String in class_counts:
		var count: int = class_counts[cls]
		for _i: int in count:
			var npc := _make_npc(rng, ss, cls, open_slots, world_seed)
			world_state.npc_pool[npc.person_id] = npc


# ── Entry point: cull ─────────────────────────────────────────────────────────

## Remove all NPCs for settlement_id from npc_pool.
## NPCs that appear in player_state.social_links are preserved in
## world_state.characters so they persist between visits.
static func cull(
		world_state: WorldState,
		settlement_id: String,
		player_state: PersonState) -> void:

	if player_state == null:
		return

	# Build known set from player social_links.
	var known_ids: Dictionary = {}
	for link: Dictionary in player_state.social_links:
		var pid: String = link.get("person_id", "")
		if pid != "":
			known_ids[pid] = true

	var to_remove: Array[String] = []
	for pid: String in world_state.npc_pool:
		var npc: PersonState = world_state.npc_pool[pid]
		if npc.home_settlement_id != settlement_id:
			continue
		if known_ids.has(pid):
			# Promote to persistent characters.
			world_state.characters[pid] = npc
		to_remove.append(pid)

	for pid: String in to_remove:
		world_state.npc_pool.erase(pid)


# ── NPC factory ───────────────────────────────────────────────────────────────

static func _make_npc(
		rng: RandomNumberGenerator,
		ss: SettlementState,
		pop_class: String,
		open_slots: Array,
		_world_seed: int) -> PersonState:

	var npc := PersonState.new()
	npc.person_id         = EntityRegistry.generate_id("person")
	npc.name              = _random_name(rng)
	npc.population_class  = pop_class
	npc.home_settlement_id = ss.settlement_id
	npc.background_id     = CLASS_BACKGROUND.get(pop_class, "wanderer")
	npc.active_role       = pop_class

	# Assign to a labor slot if compatible open slot exists.
	var preferred_slots: Array = CLASS_SLOTS.get(pop_class, [])
	var assigned_slot_idx: int = -1
	for si: int in open_slots.size():
		var slot: Dictionary = open_slots[si]
		if slot.get("slot_id", "") in preferred_slots:
			assigned_slot_idx = si
			break

	var home_cell: String = ss.cell_id  # default to anchor cell

	if assigned_slot_idx >= 0:
		var slot: Dictionary = open_slots[assigned_slot_idx]
		npc.work_cell_id = slot.get("cell_id", ss.cell_id)
		npc.active_role  = slot.get("slot_id", pop_class)
		home_cell        = npc.work_cell_id
		# Mark slot as filled in the open_slots list (not the SettlementState copy).
		open_slots.remove_at(assigned_slot_idx)
	else:
		# Pick a random territory cell.
		if not ss.territory_cell_ids.is_empty():
			var idx := rng.randi_range(0, ss.territory_cell_ids.size() - 1)
			home_cell = ss.territory_cell_ids[idx]
		npc.work_cell_id = home_cell

	npc.location = {
		"cell_id":  home_cell,
		"lx":       0,
		"ly":       0,
		"z_level":  0,
	}
	npc.schedule_state = "working"

	return npc


static func _random_name(rng: RandomNumberGenerator) -> String:
	var use_masc := rng.randf() < 0.55
	var first_arr := NAMES_MASC if use_masc else NAMES_FEM
	var first: String = first_arr[rng.randi_range(0, first_arr.size() - 1)]
	var surname: String = SURNAMES[rng.randi_range(0, SURNAMES.size() - 1)]
	return "%s %s" % [first, surname]
