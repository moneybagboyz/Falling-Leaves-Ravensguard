extends Object
class_name FloraData

static var _flora_table_cache = null

static func get_flora_table() -> Dictionary:
	if _flora_table_cache == null:
		var file = FileAccess.open("res://data/flora_table.json", FileAccess.READ)
		if file:
			var json_string = file.get_as_text()
			file.close()
			var json = JSON.new()
			var error = json.parse(json_string)
			if error == OK:
				_flora_table_cache = json.data
			else:
				push_error("Failed to parse flora_table.json: " + json.get_error_message())
				_flora_table_cache = {}
		else:
			push_error("Failed to load flora_table.json")
			_flora_table_cache = {}
	return _flora_table_cache

static func get_flora_for_biome(biome: String) -> Array:
	var table = get_flora_table()
	return table.get(biome, [])

static func get_all_biomes() -> Array:
	return get_flora_table().keys()
