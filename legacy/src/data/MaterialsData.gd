extends Object
class_name MaterialsData

static var _materials_cache = null

static func get_materials() -> Dictionary:
	if _materials_cache == null:
		var file = FileAccess.open("res://data/materials.json", FileAccess.READ)
		if file:
			var json_string = file.get_as_text()
			file.close()
			var json = JSON.new()
			var error = json.parse(json_string)
			if error == OK:
				_materials_cache = json.data
			else:
				push_error("Failed to parse materials.json: " + json.get_error_message())
				_materials_cache = {}
		else:
			push_error("Failed to load materials.json")
			_materials_cache = {}
	return _materials_cache

static func get_material(mat_key: String) -> Dictionary:
	var materials = get_materials()
	return materials.get(mat_key, {})

static func get_all_material_names() -> Array:
	return get_materials().keys()

static func has_material(mat_key: String) -> bool:
	return get_materials().has(mat_key)
