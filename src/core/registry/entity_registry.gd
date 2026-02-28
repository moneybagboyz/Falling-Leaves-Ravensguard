## EntityRegistry — generates and resolves stable string entity IDs.
## IDs are never reassigned or reused once generated.
## The ID→type map is persisted in every save file.
extends Node

# _entity_map[entity_id] = type_key (e.g. "settlement", "person", "party")
var _entity_map: Dictionary = {}
var _next_uid: int = 0


## Generate a new globally unique ID for the given type and register it.
func generate_id(type: String) -> String:
	var uid := "%s_%08x" % [type, _next_uid]
	_next_uid += 1
	_entity_map[uid] = type
	return uid


## Resolve a known entity ID to its type. Returns "" if unknown.
func resolve_type(entity_id: String) -> String:
	return _entity_map.get(entity_id, "")


## Manually register an existing ID (used when loading from save or generating
## content that already has a canonical ID).
func register_id(entity_id: String, type: String) -> void:
	if _entity_map.has(entity_id):
		push_warning("EntityRegistry: ID '%s' already registered as '%s'; re-registering as '%s'" % [
			entity_id, _entity_map[entity_id], type
		])
	_entity_map[entity_id] = type


func has_id(entity_id: String) -> bool:
	return _entity_map.has(entity_id)


func get_all_of_type(type: String) -> Array[String]:
	var result: Array[String] = []
	for id in _entity_map:
		if _entity_map[id] == type:
			result.append(id)
	return result


## Serialise to dict for inclusion in save files.
func to_dict() -> Dictionary:
	return {
		"next_uid": _next_uid,
		"entity_map": _entity_map.duplicate(),
	}


## Restore from a save dict.
func from_dict(data: Dictionary) -> void:
	_next_uid = data.get("next_uid", 0)
	_entity_map = data.get("entity_map", {})


func clear() -> void:
	_next_uid = 0
	_entity_map.clear()
