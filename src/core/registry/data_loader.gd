## DataLoader — scans /data/, loads JSON definitions, validates against schemas,
## and populates ContentRegistry.
##
## Called once at startup from Bootstrap._ready().
## In debug builds, validation failures are fatal (assert). In release builds
## they log an error and skip the offending file.
extends Node

const DATA_DIR := "res://data/"
const SCHEMA_DIR := "res://data/schemas/"

## Maps subdirectory name to the content-type key used in ContentRegistry.
const DIR_TO_TYPE: Dictionary = {
	"goods":              "good",
	"buildings":          "building",
	"backgrounds":        "background",
	"traits":             "trait",
	"skills":             "skill",
	"combat":             "combat",
	"factions":           "faction",
	"recipes":            "recipe",
	"population_classes": "population_class",
	"biomes":             "biome",
	"terrain_types":      "terrain_type",
	"armor":              "armor",
	"weapons":            "weapon",
	"materials":          "material",
	"body_zones":         "body_zone",
	"body_plans":         "body_plan",
}

# Loaded schemas: type_key -> schema dict (JSON Schema subset)
var _schemas: Dictionary = {}

signal loading_started()
signal loading_completed(count: int)
signal file_load_failed(path: String, reason: String)


func load_all() -> void:
	loading_started.emit()
	var total := 0
	_schemas.clear()
	_load_schemas()
	for dir_name in DIR_TO_TYPE:
		var type_key: String = DIR_TO_TYPE[dir_name]
		total += _load_directory(DATA_DIR + dir_name + "/", type_key)
	loading_completed.emit(total)


func _load_schemas() -> void:
	var dir := DirAccess.open(SCHEMA_DIR)
	if dir == null:
		push_warning("DataLoader: No schemas directory at '%s' — skipping schema validation." % SCHEMA_DIR)
		return
	dir.list_dir_begin()
	var file_name := dir.get_next()
	while file_name != "":
		if not dir.current_is_dir() and file_name.ends_with(".schema.json"):
			var type_key := file_name.replace(".schema.json", "")
			var schema := _load_json_file(SCHEMA_DIR + file_name)
			if not schema.is_empty():
				_schemas[type_key] = schema
		file_name = dir.get_next()


func _load_directory(path: String, type_key: String) -> int:
	var dir := DirAccess.open(path)
	if dir == null:
		return 0
	var count := 0
	dir.list_dir_begin()
	var file_name := dir.get_next()
	while file_name != "":
		if not dir.current_is_dir() and file_name.ends_with(".json") \
				and not file_name.ends_with(".schema.json"):
			count += _load_json_file_into_registry(path + file_name, type_key)
		file_name = dir.get_next()
	return count


## Loads a JSON file and registers all records into ContentRegistry.
## Supports both a root-level object (single record) and a root-level array
## (multiple records). Returns the number of successfully registered items.
func _load_json_file_into_registry(path: String, type_key: String) -> int:
	var file := FileAccess.open(path, FileAccess.READ)
	if file == null:
		_fail(path, "Cannot open file")
		return 0
	var text := file.get_as_text()
	var json := JSON.new()
	var err := json.parse(text)
	if err != OK:
		_fail(path, "JSON parse error: %s (line %d)" % [json.get_error_message(), json.get_error_line()])
		return 0
	if json.data is Array:
		var count := 0
		for item in json.data:
			if item is Dictionary:
				if _validate_and_register(type_key, item, path):
					count += 1
			else:
				_fail(path, "Array element is not a JSON object")
		return count
	if json.data is Dictionary:
		if _validate_and_register(type_key, json.data, path):
			return 1
		return 0
	_fail(path, "Expected a JSON object or array at root level")
	return 0

## Legacy helper used by _load_schemas — returns a single Dictionary or {}.
func _load_json_file(path: String) -> Dictionary:
	var file := FileAccess.open(path, FileAccess.READ)
	if file == null:
		_fail(path, "Cannot open file")
		return {}
	var text := file.get_as_text()
	var json := JSON.new()
	var err := json.parse(text)
	if err != OK:
		_fail(path, "JSON parse error: %s (line %d)" % [json.get_error_message(), json.get_error_line()])
		return {}
	if not json.data is Dictionary:
		_fail(path, "Expected a JSON object at root level")
		return {}
	return json.data as Dictionary


func _validate_and_register(type_key: String, data: Dictionary, source_path: String) -> bool:
	# Every data record must have an "id" field.
	var id: String = data.get("id", "")
	if id.is_empty():
		_fail(source_path, "Missing required field 'id'")
		return false

	# Run JSON-schema-lite validation (required fields only).
	if _schemas.has(type_key):
		var schema: Dictionary = _schemas[type_key]
		var required: Array = schema.get("required", [])
		for field in required:
			if not data.has(field):
				_fail(source_path, "Required field '%s' missing (schema: %s)" % [field, type_key])
				return false

	ContentRegistry.register(type_key, id, data)
	return true


func _fail(path: String, reason: String) -> void:
	var msg := "DataLoader: %s — %s" % [path, reason]
	push_error(msg)
	file_load_failed.emit(path, reason)
	if OS.is_debug_build():
		assert(false, msg)


## Returns true if the named schema was loaded.
func has_schema(type_key: String) -> bool:
	return _schemas.has(type_key)


func get_schema(type_key: String) -> Dictionary:
	return _schemas.get(type_key, {})
