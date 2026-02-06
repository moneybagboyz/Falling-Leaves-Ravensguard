@warning_ignore("shadowed_global_identifier")
class_name WarManager
extends Node

static func process_diplomacy(gs):
	# Don't change things too often
	if gs.day % 7 != 0: return 
	
	for i in range(gs.factions.size()):
		var f1 = gs.factions[i]
		if f1.id in ["neutral", "bandits", "player"]: continue
		
		for j in range(i + 1, gs.factions.size()):
			var f2 = gs.factions[j]
			if f2.id in ["neutral", "bandits", "player"]: continue
			
			var rel = gs.get_relation(f1.id, f2.id)
			var roll = gs.rng.randf()
			
			if rel == "peace":
				# Declare War?
				if roll < 0.12: # Increased from 5% to 12% per week
					gs.set_relation(f1.id, f2.id, "war")
					gs.add_history_event("Old rivalries ignite as %s declares war on %s!" % [f1.name, f2.name])
			elif rel == "war":
				# Make Peace?
				if roll < 0.05: # Reduced from 10% to 5% chance per week (longer wars)
					gs.set_relation(f1.id, f2.id, "peace")
					gs.add_history_event("A weary peace is bartered between %s and %s." % [f1.name, f2.name])
	
	_update_campaigns(gs)

static func _update_campaigns(gs):
	# 1. Clean up or Advance campaigns
	for i in range(gs.military_campaigns.size() - 1, -1, -1):
		var camp = gs.military_campaigns[i]
		var target_s = gs.settlements.get(camp.target_pos)
		
		# If target is already captured by us or peace is signed
		if target_s and target_s.faction == camp.faction:
			camp.status = "success"
		elif gs.get_relation(camp.faction, target_s.faction if target_s else "") != "war":
			camp.status = "cancelled"
		
		# MANAGE GATHERING STATUS
		if camp.status == "gathering":
			var total_power = 0.0
			var count = 0
			# Check how many lords are at or near the gathering point
			var nearby = gs.get_entities_near(camp.gathering_pos, 4) # Increased radius from 3 to 4
			for e in nearby:
				if "type" in e and e.type == "lord" and e.faction == camp.faction:
					total_power += e.strength
					count += 1
			
			# Campaign launches if 2 lords gather or they hit 3500 power (approx 3.5 strong lords)
			if count >= 2 or total_power > 3500: 
				camp.status = "marching"
				gs.add_log("[MARSHAL] The forces of %s have finished gathering! The march on %s begins." % [camp.faction, target_s.name])

		if camp.status in ["success", "cancelled", "failed"]:
			gs.military_campaigns.remove_at(i)

	# 2. Forge new Campaigns
	for f in gs.factions:
		if f.id in ["neutral", "bandits", "player"]: continue
		
		# Limit: One campaign per faction at a time
		var active = false
		for camp in gs.military_campaigns:
			if camp.faction == f.id:
				active = true
				break
		if active: continue
		
		# Strategic Marshal: Find most vulnerable enemy border city
		var potential_targets = []
		for s_pos in gs.settlements:
			var s_data = gs.settlements[s_pos]
			if gs.get_relation(f.id, s_data.faction) == "war":
				# Calculate vulnerability
				var dist_to_f = 999
				for fs_pos in gs.settlements:
					if gs.settlements[fs_pos].faction == f.id:
						dist_to_f = min(dist_to_f, s_pos.distance_to(fs_pos))
				
				if dist_to_f < 30: # Within strike distance
					var vulnerability = 100 - s_data.garrison
					potential_targets.append({"pos": s_pos, "val": vulnerability - dist_to_f})
		
		if not potential_targets.is_empty():
			potential_targets.sort_custom(func(a, b): return a.val > b.val)
			var target = potential_targets[0]
			
			# FIND GATHERING POINT: The friendly settlement closest to the target
			var gathering_pos = Vector2i(-1, -1)
			var min_dist = 999
			for s_pos in gs.settlements:
				if gs.settlements[s_pos].faction == f.id:
					var d = s_pos.distance_to(target.pos)
					if d < min_dist:
						min_dist = d
						gathering_pos = s_pos
			
			var camp_id = gs.rng.randi()
			gs.military_campaigns.append({
				"id": camp_id,
				"faction": f.id,
				"target_pos": target.pos,
				"gathering_pos": gathering_pos,
				"type": "conquest",
				"status": "gathering"
			})
			gs.add_log("[MARSHAL] %s is gathering forces at %s for a campaign against %s!" % [f.name, gs.settlements[gathering_pos].name, gs.settlements[target.pos].name])
