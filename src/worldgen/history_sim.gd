## HistorySim — P1-06 stub.
##
## Assigns faction ownership to province capitals from ContentRegistry faction
## templates and seeds starting diplomatic relations.
##
## This is intentionally minimal; deeper political history simulation is deferred
## to a later phase. The stub must not crash when factions are absent.
class_name HistorySim
extends RefCounted

## Run history simulation against an assembled WorldState.
## Mutates ws.settlements faction_id fields in-place.
static func run(ws: WorldState, _data: WorldGenData, world_seed: int) -> void:
	var rng := RandomNumberGenerator.new()
	rng.seed = world_seed ^ 0xBEEFCAFE

	# Collect available faction IDs from ContentRegistry.
	var faction_ids: Array[String] = []
	for fid in ContentRegistry.get_all("faction").keys():
		faction_ids.append(fid)

	if faction_ids.is_empty():
		# No factions loaded (e.g. headless test) — assign placeholder names.
		faction_ids = ["faction_0", "faction_1", "faction_2"]

	# Assign a faction to each hub settlement using province_id → faction mapping.
	# Multiple consecutive provinces may share a faction to model territories.
	var num_provinces: int   = ws.province_names.size()
	var prov_faction: Array  = []
	prov_faction.resize(num_provinces)
	for pid in range(num_provinces):
		prov_faction[pid] = faction_ids[rng.randi() % faction_ids.size()]

	# Apply faction_id to all settlements.
	for sid: String in ws.settlements.keys():
		var sv = ws.settlements[sid]
		var pid: int = -1
		if sv is SettlementState:
			pid = int(sv.province_id)
			if pid >= 0 and pid < prov_faction.size():
				sv.faction_id = prov_faction[pid]
			else:
				sv.faction_id = faction_ids[0]
		elif sv is Dictionary:
			pid = int(sv.get("province_id", "-1"))
			if pid >= 0 and pid < prov_faction.size():
				sv["faction_id"] = prov_faction[pid]
			else:
				sv["faction_id"] = faction_ids[0]
			ws.settlements[sid] = sv

	# Seed world flags for starting relations (placeholder escalation factor).
	ws.world_flags["history_sim_run"] = true
	ws.world_flags["starting_year"]   = 1200 + rng.randi_range(-50, 50)

	# Seed a few inter-faction tensions (simple numeric 0-100).
	var relations: Dictionary = {}
	for fid_a: String in faction_ids:
		for fid_b: String in faction_ids:
			if fid_a == fid_b:
				continue
			var key: String = "%s__%s" % [fid_a, fid_b]
			relations[key] = rng.randi_range(20, 80)
	ws.world_flags["faction_relations"] = relations
