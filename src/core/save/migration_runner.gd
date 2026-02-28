## MigrationRunner — applies ordered save-schema migrations.
##
## When a save file's schema_version is older than the current version,
## SaveManager calls MigrationRunner.migrate() which chains applicable
## migration functions until the save reaches the current version.
##
## Each migration is registered with register_migration(from, to, fn) where
## fn is a Callable that takes a save dict and returns the updated dict.
##
## Rule: migration functions must be registered AT THE SAME TIME the schema
## change is made — never retroactively.
extends Node

## A registered migration entry.
## { "from": String, "to": String, "fn": Callable }
var _migrations: Array = []


# ---------------------------------------------------------------------------
# Registration
# ---------------------------------------------------------------------------

func register_migration(from_version: String, to_version: String, fn: Callable) -> void:
	# Prevent duplicates.
	for m in _migrations:
		if m["from"] == from_version and m["to"] == to_version:
			push_warning("MigrationRunner: Duplicate migration %s -> %s ignored." % [from_version, to_version])
			return
	_migrations.append({"from": from_version, "to": to_version, "fn": fn})


# ---------------------------------------------------------------------------
# Migration execution
# ---------------------------------------------------------------------------

## Walk the migration graph from from_version to target_version.
## Returns the mutated save data dict with schema_version set to target_version.
func migrate(data: Dictionary, from_version: String, target_version: String) -> Dictionary:
	if from_version == target_version:
		return data

	var result := data.duplicate(true)
	var current := from_version
	const MAX_STEPS := 100
	var steps := 0

	while current != target_version:
		if steps >= MAX_STEPS:
			push_error("MigrationRunner: Migration chain exceeded %d steps — aborting. " \
				+ "Possible cycle or missing migration from '%s' to '%s'." % [MAX_STEPS, current, target_version])
			break

		var next_migration: Dictionary = _find_migration(current)
		if next_migration.is_empty():
			push_error("MigrationRunner: No migration registered from version '%s'. " \
				+ "Cannot reach target '%s'." % [current, target_version])
			break

		result = next_migration["fn"].call(result)
		current = next_migration["to"]
		steps += 1

	result["schema_version"] = target_version
	return result


func _find_migration(from_version: String) -> Dictionary:
	for m in _migrations:
		if m["from"] == from_version:
			return m
	return {}


func has_migration(from_version: String) -> bool:
	return not _find_migration(from_version).is_empty()


func get_registered_migrations() -> Array:
	return _migrations.duplicate()


func clear() -> void:
	_migrations.clear()
