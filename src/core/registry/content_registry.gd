## ContentRegistry — global store for all data definitions loaded from /data/.
## Autoload singleton. Populated by DataLoader at startup.
## Key invariant: duplicate IDs for the same type are fatal in debug builds.
extends Node

# _registry[type_key][content_id] = data Dictionary
var _registry: Dictionary = {}


func register(type: String, id: String, data: Dictionary) -> void:
	if not _registry.has(type):
		_registry[type] = {}
	if _registry[type].has(id):
		var msg := "ContentRegistry: Duplicate ID '%s' for type '%s'" % [id, type]
		push_error(msg)
		assert(false, msg)
		return
	_registry[type][id] = data


func get_content(type: String, id: String) -> Dictionary:
	if not _registry.has(type) or not _registry[type].has(id):
		push_error("ContentRegistry: Unknown content '%s' of type '%s'" % [id, type])
		return {}
	return _registry[type][id]


func get_all(type: String) -> Dictionary:
	return _registry.get(type, {}).duplicate()


func has_content(type: String, id: String) -> bool:
	return _registry.has(type) and _registry[type].has(id)


func get_types() -> Array:
	return _registry.keys()


func clear() -> void:
	_registry.clear()


## Returns a summary dict for the StateInspector: type -> count.
func get_summary() -> Dictionary:
	var summary := {}
	for type in _registry:
		summary[type] = _registry[type].size()
	return summary
