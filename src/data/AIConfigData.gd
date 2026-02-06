extends Object
class_name AIConfigData

static var _config_cache = null

static func get_config() -> Dictionary:
	if _config_cache == null:
		var file = FileAccess.open("res://data/ai_config.json", FileAccess.READ)
		if file:
			var json_string = file.get_as_text()
			file.close()
			var json = JSON.new()
			var error = json.parse(json_string)
			if error == OK:
				_config_cache = json.data
			else:
				push_error("Failed to parse ai_config.json: " + json.get_error_message())
				_config_cache = {}
		else:
			push_error("Failed to load ai_config.json")
			_config_cache = {}
	return _config_cache

static func get_governor_personalities() -> Array:
	return get_config().get("governor_personalities", [])

static func get_lord_doctrines() -> Array:
	return get_config().get("lord_doctrines", [])

static func get_material_tiers() -> Dictionary:
	var tiers = get_config().get("material_tiers", {})
	# Convert string keys back to integers
	var result = {}
	for key in tiers:
		result[int(key)] = tiers[key]
	return result

static func get_material_for_tier(tier: int) -> String:
	var tiers = get_material_tiers()
	return tiers.get(tier, "iron")
