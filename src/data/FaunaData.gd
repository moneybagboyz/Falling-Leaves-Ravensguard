class_name FaunaData
extends RefCounted

# Cached fauna table loaded from JSON
static var _fauna_table: Dictionary = {}
static var _loaded: bool = false

# Load fauna data from JSON file
static func get_fauna_table() -> Dictionary:
	if _loaded:
		return _fauna_table
	
	var file_path = "res://data/fauna_table.json"
	if FileAccess.file_exists(file_path):
		var file = FileAccess.open(file_path, FileAccess.READ)
		if file:
			var json_string = file.get_as_text()
			file.close()
			
			var json = JSON.new()
			var error = json.parse(json_string)
			if error == OK:
				_fauna_table = json.data
				_loaded = true
				print("Fauna table loaded: ", _fauna_table.keys().size(), " biomes")
			else:
				push_error("Failed to parse fauna_table.json: " + json.get_error_message())
		else:
			push_error("Failed to open fauna_table.json")
	else:
		push_error("fauna_table.json not found at: " + file_path)
	
	return _fauna_table

# Get fauna for a specific biome
static func get_fauna_for_biome(biome: String) -> Array:
	var table = get_fauna_table()
	return table.get(biome, [])

# Reload fauna data (useful for development/modding)
static func reload():
	_loaded = false
	_fauna_table.clear()
	return get_fauna_table()
