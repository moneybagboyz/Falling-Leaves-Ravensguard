## SaveManager — writes versioned JSON saves and restores from them.
##
## Every save written by this manager contains a "schema_version" field.
## On load, if the saved version differs from CURRENT_SCHEMA_VERSION,
## MigrationRunner is invoked to bring the data up to date before deserialising.
##
## Non-negotiable rules:
##   - schema_version is ALWAYS written.
##   - Data is NEVER loaded without checking schema_version first.
##   - Migration functions must exist before they are needed.
extends Node

## Bump this any time the save schema changes — and add a migration at the same time.
const CURRENT_SCHEMA_VERSION := "0.1.0"

const SAVE_DIR := "user://saves/"

signal save_completed(slot: String)
signal load_completed(slot: String)
signal save_failed(slot: String, reason: String)
signal load_failed(slot: String, reason: String)
signal migration_applied(from_version: String, to_version: String)


func _ready() -> void:
	_ensure_save_dir()


func _ensure_save_dir() -> void:
	if not DirAccess.dir_exists_absolute(SAVE_DIR):
		DirAccess.make_dir_recursive_absolute(SAVE_DIR)


# ---------------------------------------------------------------------------
# Save
# ---------------------------------------------------------------------------

func save(world_state: WorldState, slot: String = "default") -> Error:
	_ensure_save_dir()
	var save_data := {
		"schema_version":  CURRENT_SCHEMA_VERSION,
		"timestamp":       Time.get_unix_time_from_system(),
		"clock":           SimulationClock.to_dict(),
		"entity_registry": EntityRegistry.to_dict(),
		"world_state":     world_state.to_dict(),
	}

	var path := _slot_path(slot)
	var json_str := JSON.stringify(save_data, "\t")
	var file := FileAccess.open(path, FileAccess.WRITE)
	if file == null:
		var reason := "Cannot open path for writing: %s" % path
		push_error("SaveManager: " + reason)
		save_failed.emit(slot, reason)
		return FAILED

	file.store_string(json_str)
	save_completed.emit(slot)
	return OK


# ---------------------------------------------------------------------------
# Load
# ---------------------------------------------------------------------------

func load_save(slot: String = "default") -> WorldState:
	var path := _slot_path(slot)
	if not FileAccess.file_exists(path):
		var reason := "Save file not found: %s" % path
		push_error("SaveManager: " + reason)
		load_failed.emit(slot, reason)
		return null

	var file := FileAccess.open(path, FileAccess.READ)
	if file == null:
		var reason := "Cannot open save file for reading: %s" % path
		push_error("SaveManager: " + reason)
		load_failed.emit(slot, reason)
		return null

	var json := JSON.new()
	if json.parse(file.get_as_text()) != OK:
		var reason := "JSON parse error in %s: %s" % [path, json.get_error_message()]
		push_error("SaveManager: " + reason)
		load_failed.emit(slot, reason)
		return null

	if not json.data is Dictionary:
		load_failed.emit(slot, "Root element is not a JSON object")
		return null

	var data: Dictionary = json.data
	var saved_version: String = data.get("schema_version", "0.0.0")

	if saved_version != CURRENT_SCHEMA_VERSION:
		push_warning("SaveManager: Migrating save '%s' from %s → %s" % [
			slot, saved_version, CURRENT_SCHEMA_VERSION
		])
		data = MigrationRunner.migrate(data, saved_version, CURRENT_SCHEMA_VERSION)
		migration_applied.emit(saved_version, CURRENT_SCHEMA_VERSION)

	if data.has("clock"):
		SimulationClock.from_dict(data["clock"])
	if data.has("entity_registry"):
		EntityRegistry.from_dict(data["entity_registry"])

	var ws := WorldState.from_dict(data.get("world_state", {}))
	load_completed.emit(slot)
	return ws


# ---------------------------------------------------------------------------
# Utilities
# ---------------------------------------------------------------------------

func save_exists(slot: String = "default") -> bool:
	return FileAccess.file_exists(_slot_path(slot))


func list_saves() -> Array[String]:
	var dir := DirAccess.open(SAVE_DIR)
	if dir == null:
		return []
	var slots: Array[String] = []
	dir.list_dir_begin()
	var f := dir.get_next()
	while f != "":
		if not dir.current_is_dir() and f.ends_with(".json"):
			slots.append(f.trim_suffix(".json"))
		f = dir.get_next()
	return slots


func delete_save(slot: String) -> void:
	var path := _slot_path(slot)
	if FileAccess.file_exists(path):
		DirAccess.remove_absolute(path)


func _slot_path(slot: String) -> String:
	return SAVE_DIR + slot + ".json"
