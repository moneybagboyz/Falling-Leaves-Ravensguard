## EconomyConfig — lazy-loaded singleton for economy balance parameters.
##
## Reads data/config/economy_config.json once and caches the result.
## All economy subsystems call EconomyConfig.get_config() instead of
## hardcoding magic numbers directly in their const declarations.
##
## Usage:
##     var cfg := EconomyConfig.get_config()
##     var threshold: float = cfg.get("surplus_threshold", 20.0)
##
## Call EconomyConfig.reload() in tests or after live-patching the JSON.
class_name EconomyConfig
extends Object

const _PATH: String = "res://data/config/economy_config.json"

static var _cache:  Dictionary = {}
static var _loaded: bool       = false


## Returns the full config dictionary. Loads from disk on the first call.
static func get_config() -> Dictionary:
	if not _loaded:
		_load()
	return _cache


## Forces a reload from disk — useful in tests and hot-reload scenarios.
static func reload() -> void:
	_loaded = false
	_cache  = {}
	_load()


static func _load() -> void:
	_loaded = true   # set early so recursive calls don't loop
	if not FileAccess.file_exists(_PATH):
		push_warning("EconomyConfig: config file not found at '%s'; using defaults." % _PATH)
		return
	var file := FileAccess.open(_PATH, FileAccess.READ)
	if file == null:
		push_error("EconomyConfig: cannot open '%s'." % _PATH)
		return
	var json := JSON.new()
	if json.parse(file.get_as_text()) != OK:
		push_error("EconomyConfig: JSON parse error in '%s': %s" % [_PATH, json.get_error_message()])
		return
	if json.data is Dictionary:
		_cache = json.data
	else:
		push_error("EconomyConfig: root element of '%s' is not a JSON object." % _PATH)
