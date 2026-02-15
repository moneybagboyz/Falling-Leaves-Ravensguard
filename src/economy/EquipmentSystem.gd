extends RefCounted
class_name EquipmentSystem

## Equipment System
## Handles item creation, commissioning, equipping, and class-based loadouts

# GameData and GameState are autoloads - no need to preload
const PricingSystem = preload("res://src/economy/PricingSystem.gd")

## Create an item from templates
static func create_item(type_key, material_key, quality := "standard"):
	var item = null
	
	# Check weapon templates
	if GameData.WEAPON_TEMPLATES.has(type_key):
		var tmpl = GameData.WEAPON_TEMPLATES[type_key]
		item = tmpl.duplicate(true)
		item["id"] = type_key
		item["type_key"] = type_key
		item["material"] = material_key
		item["quality"] = quality
		
		# Apply material properties
		match material_key:
			"wood":
				item["dmg_mod"] = -3
				item["penetration_mod"] = 0.6
			"stone":
				item["dmg_mod"] = -1
				item["penetration_mod"] = 0.8
			"bronze":
				item["dmg_mod"] = 0
				item["penetration_mod"] = 0.9
			"iron":
				item["dmg_mod"] = 1
				item["penetration_mod"] = 1.0
			"steel":
				item["dmg_mod"] = 3
				item["penetration_mod"] = 1.3
	
	# Check armor templates
	elif GameData.ARMOR_TEMPLATES.has(type_key):
		var tmpl = GameData.ARMOR_TEMPLATES[type_key]
		item = tmpl.duplicate(true)
		item["id"] = type_key
		item["type_key"] = type_key
		item["material"] = material_key
		item["quality"] = quality
		
		# Apply material properties for armor
		match material_key:
			"linen", "wool", "cloth":
				item["armor_mod"] = -2
				item["weight_mod"] = 0.7
			"leather":
				item["armor_mod"] = 0
				item["weight_mod"] = 0.9
			"bronze":
				item["armor_mod"] = 1
				item["weight_mod"] = 1.1
			"iron":
				item["armor_mod"] = 2
				item["weight_mod"] = 1.0
			"steel":
				item["armor_mod"] = 4
				item["weight_mod"] = 0.9
	
	# Handle ammo specially
	elif type_key in ["arrows", "bolts"]:
		item = {
			"id": type_key,
			"type_key": type_key,
			"name": type_key.capitalize(),
			"material": material_key,
			"quality": quality,
			"slot": "ammo",
			"quantity": 50,
			"weight": 2.0
		}
		
		# Material affects ammo damage
		match material_key:
			"wood":
				item["dmg_mod"] = -2
				item["penetration_mod"] = 0.8
			"iron":
				item["dmg_mod"] = 0
				item["penetration_mod"] = 1.0
			"steel":
				item["dmg_mod"] = 2 if type_key == "arrows" else 3
				item["penetration_mod"] = 1.5 if type_key == "arrows" else 1.8
	
	return item

## Get quality rank for comparison
@warning_ignore("shadowed_global_identifier")
static func get_quality_rank(q) -> int:
	match q:
		"rusty": return 0
		"standard": return 1
		"fine": return 2
		"masterwork": return 3
	return 0

## Get cost of full kit for a unit class
@warning_ignore("shadowed_global_identifier")
static func get_kit_cost(player_obj, c_name, is_commission = false) -> int:
	if not player_obj.unit_classes.has(c_name): return 0
	var bp = player_obj.unit_classes[c_name]
	var total = 0
	for slot in bp:
		var req = bp[slot]
		if req.get("type") != "none":
			total += PricingSystem.get_item_price(req.get("type"), req.get("material"), req.get("quality"), is_commission)
	return total

## Get cost to reequip all units of a class
@warning_ignore("shadowed_global_identifier")
static func get_reequip_cost(player_obj, c_name) -> int:
	var total = 0
	if not player_obj.unit_classes.has(c_name): return 0
	var bp = player_obj.unit_classes[c_name]
	for u_obj in player_obj.roster:
		if u_obj.assigned_class == c_name:
			var readiness = check_readiness(player_obj, u_obj)
			for slot in readiness.get("missing", []):
				var req = bp[slot]
				total += PricingSystem.get_item_price(req.get("type"), req.get("material"), req.get("quality"), true)
	return total

## Fund commissions for all units of a class
@warning_ignore("shadowed_global_identifier")
static func fund_class_commissions(gs, s_pos, c_name):
	var player_obj = gs.player
	if not player_obj.unit_classes.has(c_name): return
	var cost = get_reequip_cost(player_obj, c_name)
	if cost <= 0:
		gs.add_log("All units of class %s are already equipped." % c_name)
		return
		
	if player_obj.crowns >= cost:
		player_purchase_reequip(gs, s_pos, c_name, cost)
	else:
		gs.add_log("Cannot afford bulk order for %s (Need %dg)." % [c_name, cost])

## Purchase and commission equipment for a class
@warning_ignore("shadowed_global_identifier")
static func player_purchase_reequip(gs, s_pos, c_name, cost):
	var player_obj = gs.player
	player_obj.crowns -= cost
	var bp = player_obj.unit_classes[c_name]
	var orders = {} # { "type_mat_qual": count }
	
	for u_obj in player_obj.roster:
		if u_obj.assigned_class == c_name:
			var readiness = check_readiness(player_obj, u_obj)
			for slot in readiness.get("missing", []):
				var req = bp[slot]
				var key = "%s|%s|%s" % [req.get("type"), req.get("material"), req.get("quality")]
				orders[key] = orders.get(key, 0) + 1
	
	for key in orders:
		var parts = key.split("|")
		var count = orders[key]
		var item_template = create_item(parts[0], parts[1], parts[2])
		player_obj.commissions.append({
			"item_data": item_template,
			"count": count,
			"remaining_turns": 24 + (count * 2),
			"s_pos": s_pos
		})
		
	gs.add_log("Funded bulk commissions for %s: %dg spent." % [c_name, cost])
	gs.emit_signal("map_updated")

## Create a new unit class
@warning_ignore("shadowed_global_identifier")
static func create_class(gs, c_name, reqs):
	gs.player.unit_classes[c_name] = reqs
	gs.add_log("Created unit class: %s" % c_name)
	gs.emit_signal("map_updated")

## Assign a unit to a class
@warning_ignore("shadowed_global_identifier")
static func assign_class(gs, unit_idx, c_name):
	var player_obj = gs.player
	if unit_idx < 0 or unit_idx >= player_obj.roster.size(): return
	player_obj.roster[unit_idx].assigned_class = c_name
	gs.add_log("Assigned %s to class %s." % [player_obj.roster[unit_idx].name, c_name])
	gs.emit_signal("map_updated")

## Check if unit meets class requirements
@warning_ignore("shadowed_global_identifier")
static func check_readiness(player_obj, u_obj) -> Dictionary:
	if u_obj.assigned_class == "" or not player_obj.unit_classes.has(u_obj.assigned_class):
		return {"is_ready": true, "missing": []}
		
	var blueprint = player_obj.unit_classes[u_obj.assigned_class]
	var missing = []
	
	for bp_key in blueprint:
		var req = blueprint[bp_key]
		if not req or req.get("type") == "none": continue
		
		var item = null
		if bp_key in ["main_hand", "off_hand"]:
			item = u_obj.equipment.get(bp_key)
		elif bp_key == "cover":
			item = u_obj.equipment.get("torso", {}).get("cover")
		else:
			var parts = bp_key.split("_")
			if parts.size() >= 2:
				var layer = parts[parts.size() - 1]
				var slot = bp_key.trim_suffix("_" + layer)
				if slot == "arms": slot = "l_arm"
				elif slot == "hands": slot = "l_hand"
				elif slot == "legs": slot = "l_leg"
				elif slot == "feet": slot = "l_foot"
				if u_obj.equipment.has(slot) and u_obj.equipment[slot] is Dictionary:
					item = u_obj.equipment[slot].get(layer)
		
		if not item:
			missing.append(bp_key)
			continue
			
		var matches = true
		if item.get("type_key") != req.get("type"): matches = false
		if req.get("material") != "any" and item.get("material") != req.get("material"): matches = false
		if get_quality_rank(item.get("quality", "standard")) < get_quality_rank(req.get("quality", "standard")): matches = false
		
		if not matches:
			missing.append(bp_key)
			
	return {"is_ready": missing.size() == 0, "missing": missing}

## Auto-equip all units from stash
@warning_ignore("shadowed_global_identifier")
static func auto_equip_all(gs):
	var player_obj = gs.player
	var count = 0
	for i in range(player_obj.roster.size()):
		var u_obj = player_obj.roster[i]
		if u_obj.assigned_class == "" or not player_obj.unit_classes.has(u_obj.assigned_class): continue
		
		var blueprint = player_obj.unit_classes[u_obj.assigned_class]
		for bp_key in blueprint:
			var req = blueprint[bp_key]
			if not req or req.get("type") == "none": continue
			
			var current_item = null
			if bp_key in ["main_hand", "off_hand"]:
				current_item = u_obj.equipment.get(bp_key)
			elif bp_key == "cover":
				current_item = u_obj.equipment.get("torso", {}).get("cover")
			else:
				var parts = bp_key.split("_")
				if parts.size() >= 2:
					var layer = parts[parts.size() - 1]
					var slot = bp_key.trim_suffix("_" + layer)
					if slot == "arms": slot = "l_arm"
					elif slot == "hands": slot = "l_hand"
					elif slot == "legs": slot = "l_leg"
					elif slot == "feet": slot = "l_foot"
					if u_obj.equipment.has(slot):
						current_item = u_obj.equipment[slot].get(layer)
			
			if current_item:
				var matches = true
				if current_item.get("type_key") != req.get("type"): matches = false
				if req.get("material") != "any" and current_item.get("material") != req.get("material"): matches = false
				if get_quality_rank(current_item.get("quality", "standard")) < get_quality_rank(req.get("quality", "standard")): matches = false
				if matches: continue
			
			var best_idx = -1
			var best_rank = -1
			for j in range(player_obj.stash.size()):
				var item = player_obj.stash[j]
				if item.get("id") == req.get("type"):
					if req.get("material") != "any" and item.get("material") != req.get("material"): continue
					var rank = get_quality_rank(item.get("quality", "standard"))
					if rank >= get_quality_rank(req.get("quality", "standard")) and rank > best_rank:
						best_rank = rank
						best_idx = j
			
			if best_idx != -1:
				var item = player_obj.stash[best_idx]
				player_obj.stash.remove_at(best_idx)
				if bp_key in ["main_hand", "off_hand"]:
					u_obj.equipment[bp_key] = item
				elif bp_key == "cover":
					u_obj.equipment["torso"]["cover"] = item
				else:
					var parts = bp_key.split("_")
					var layer = parts[parts.size() - 1]
					var slot = bp_key.trim_suffix("_" + layer)
					if slot == "arms":
						u_obj.equipment["l_arm"][layer] = item
						u_obj.equipment["r_arm"][layer] = item.duplicate()
					elif slot == "hands":
						u_obj.equipment["l_hand"][layer] = item
						u_obj.equipment["r_hand"][layer] = item.duplicate()
					elif slot == "legs":
						u_obj.equipment["l_leg"][layer] = item
						u_obj.equipment["r_leg"][layer] = item.duplicate()
					elif slot == "feet":
						u_obj.equipment["l_foot"][layer] = item
						u_obj.equipment["r_foot"][layer] = item.duplicate()
					else:
						u_obj.equipment[slot][layer] = item
				count += 1
	if count > 0:
		for u in player_obj.roster:
			u.base_speed = GameData.calculate_unit_speed(u)
			u.speed = u.base_speed
		player_obj.commander.base_speed = GameData.calculate_unit_speed(player_obj.commander)
		player_obj.commander.speed = player_obj.commander.base_speed
		gs.add_log("Auto-equipped %d items from stash." % count)
		gs.emit_signal("map_updated")

## Commission items from a settlement
@warning_ignore("shadowed_global_identifier")
static func commission_items(gs, s_pos, type_key, mat_key, qual, count):
	var player_obj = gs.player
	var item_template = create_item(type_key, mat_key, qual)
	if not item_template: return
	
	var unit_price = PricingSystem.get_item_price(type_key, mat_key, qual, true)
	
	var total_cost = unit_price * count
	if player_obj.crowns >= total_cost:
		player_obj.crowns -= total_cost
		player_obj.commissions.append({
			"item_data": item_template,
			"count": count,
			"remaining_turns": 24 + (count * 2),
			"s_pos": s_pos
		})
		gs.add_log("Commissioned %dx %s for %dg." % [count, item_template.get("name", "Item"), total_cost])
		gs.emit_signal("map_updated")
	else:
		gs.add_log("Cannot afford commission (Need %dg)." % total_cost)

## Equip item from stash to unit
@warning_ignore("shadowed_global_identifier")
static func perform_equip(gs, u_obj, stash_idx):
	var player_obj = gs.player
	if stash_idx < 0 or stash_idx >= player_obj.stash.size(): return
	var item = player_obj.stash[stash_idx]
	var slot = item.get("slot", "main_hand")
	
	if slot in ["main_hand", "off_hand"]:
		if slot == "main_hand" and item.get("hands", 1) == 2:
			if u_obj.equipment.get("off_hand") != null:
				player_obj.stash.append(u_obj.equipment["off_hand"])
				u_obj.equipment["off_hand"] = null
				gs.add_log("Unequipped off-hand to use two-handed %s." % item.get("name", "Item"))
		elif slot == "off_hand":
			var main = u_obj.equipment.get("main_hand")
			if main and main.get("hands", 1) == 2:
				player_obj.stash.append(main)
				u_obj.equipment["main_hand"] = null
				gs.add_log("Unequipped two-handed %s to use off-hand." % main.get("name", "Item"))

		if u_obj.equipment.get(slot) != null:
			player_obj.stash.append(u_obj.equipment[slot])
		u_obj.equipment[slot] = item
	else:
		var layer = item.get("layer", "armor")
		var coverage = item.get("coverage", [slot])
		
		var to_remove = []
		for part in coverage:
			var existing = u_obj.equipment.get(part, {}).get(layer)
			if existing and not existing in to_remove:
				to_remove.append(existing)
		
		for old_item in to_remove:
			var old_cov = old_item.get("coverage", [])
			for part in old_cov:
				if u_obj.equipment.get(part, {}).get(layer) == old_item:
					u_obj.equipment[part][layer] = null
			player_obj.stash.append(old_item)
			
		for part in coverage:
			u_obj.equipment[part][layer] = item
	
	player_obj.stash.remove_at(stash_idx)
	u_obj.base_speed = GameData.calculate_unit_speed(u_obj)
	u_obj.speed = u_obj.base_speed
	gs.add_log("Equipped %s to %s." % [item.get("name", "Item"), u_obj.name])
	gs.emit_signal("map_updated")

## Unequip item from unit to stash
@warning_ignore("shadowed_global_identifier")
static func perform_unequip(gs, u_obj, slot, layer = ""):
	var player_obj = gs.player
	var item = null
	if slot in ["main_hand", "off_hand"]:
		item = u_obj.equipment[slot]
		u_obj.equipment[slot] = null
	elif u_obj.equipment.has(slot) and layer != "":
		item = u_obj.equipment[slot][layer]
		if item:
			var coverage = item.get("coverage", [slot])
			for part in coverage:
				if u_obj.equipment[part][layer] == item:
					u_obj.equipment[part][layer] = null
	
	if item != null:
		player_obj.stash.append(item)
		u_obj.base_speed = GameData.calculate_unit_speed(u_obj)
		u_obj.speed = u_obj.base_speed
		gs.add_log("Unequipped %s from %s." % [item.get("name", "Item"), u_obj.name])
		gs.emit_signal("map_updated")
