@warning_ignore("shadowed_global_identifier")
class_name FactionManager
extends Node

@warning_ignore("shadowed_global_identifier")
static func get_relation(f1, f2, factions):
	var id1 = f1.id if f1 is Object else f1
	var id2 = f2.id if f2 is Object else f2
	
	if id1 == "bandits" or id2 == "bandits": return "war"
	if id1 == id2: return "peace"
	
	var f1_data = get_faction(id1, factions)
	if not f1_data: return "peace"
	return f1_data.relations.get(id2, "peace")

@warning_ignore("shadowed_global_identifier")
static func get_faction(id, factions):
	for f_data in factions:
		if f_data.id == id:
			return f_data
	return null

static func get_entity_name(entity, factions):
	var f_data = get_faction(entity.faction, factions)
	var f_name = f_data.name if f_data else entity.faction.capitalize()
	
	if entity.type == "player": return "Your Company"
	if entity.type == "lord": return "%s Lord" % f_name
	if entity.type == "bandit": return "Bandits"
	if entity.type == "caravan": return "%s Caravan" % f_name
	if entity.type == "army": return "%s Army" % f_name
	if "name" in entity: return entity.name
	return "Unknown Force"

static func process_faction_upkeep(factions, _armies, _settlements):
	for f_data in factions:
		# This logic is a bit complex because it involves lords drawing from fiefs
		pass

static func handle_caravan_tax(caravan, s_data, faction_obj):
	if caravan.faction == s_data.faction and caravan.faction == faction_obj.id:
		if caravan.crowns > Globals.CARAVAN_TAX_THRESHOLD:
			var tax = caravan.crowns - Globals.CARAVAN_TAX_THRESHOLD
			caravan.crowns = Globals.CARAVAN_TAX_THRESHOLD
			faction_obj.treasury += tax
