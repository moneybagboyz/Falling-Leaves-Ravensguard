class_name SaveManager
extends RefCounted

## Manages persistent save/load of worlds, characters, and game states
## Similar to Dwarf Fortress / CDDA save systems

const SAVE_VERSION = "1.0"
const WORLDS_DIR = "user://saves/worlds/"
const CHARACTERS_DIR = "user://saves/characters/"
const GAMES_DIR = "user://saves/games/"

# Ensure save directories exist
static func _ensure_directories():
	for dir_path in [WORLDS_DIR, CHARACTERS_DIR, GAMES_DIR]:
		if not DirAccess.dir_exists_absolute(dir_path):
			DirAccess.make_dir_recursive_absolute(dir_path)

# ============================================================================
# WORLD SAVE/LOAD
# ============================================================================

static func save_world(world_data: Dictionary) -> bool:
	"""Save a generated world to disk"""
	_ensure_directories()
	
	var world_id = world_data.get("id", "world_" + str(Time.get_unix_time_from_system()))
	var world_name = world_data.get("name", "Unnamed World")
	var safe_name = world_name.replace(" ", "_").replace("/", "-")
	var filename = "%s_%s.json" % [world_id, safe_name]
	var filepath = WORLDS_DIR + filename
	
	# Add metadata
	world_data["id"] = world_id
	world_data["saved_at"] = Time.get_datetime_string_from_system()
	world_data["version"] = SAVE_VERSION
	
	var file = FileAccess.open(filepath, FileAccess.WRITE)
	if not file:
		push_error("SaveManager: Failed to save world to %s" % filepath)
		return false
	
	file.store_string(JSON.stringify(world_data, "\t"))
	file.close()
	
	print("SaveManager: World '%s' saved to %s" % [world_name, filepath])
	return true

static func load_world(world_id: String) -> Dictionary:
	"""Load a world by ID"""
	var worlds = list_worlds()
	for world_meta in worlds:
		if world_meta.get("id") == world_id:
			var file = FileAccess.open(world_meta.get("filepath"), FileAccess.READ)
			if not file:
				push_error("SaveManager: Failed to load world %s" % world_id)
				return {}
			
			var json_string = file.get_as_text()
			file.close()
			
			var json = JSON.new()
			var error = json.parse(json_string)
			if error != OK:
				push_error("SaveManager: Failed to parse world JSON: %s" % json.get_error_message())
				return {}
			
			return json.data
	
	push_error("SaveManager: World %s not found" % world_id)
	return {}

static func list_worlds() -> Array:
	"""List all saved worlds with metadata"""
	_ensure_directories()
	
	var worlds = []
	var dir = DirAccess.open(WORLDS_DIR)
	
	if not dir:
		return worlds
	
	dir.list_dir_begin()
	var filename = dir.get_next()
	
	while filename != "":
		if not dir.current_is_dir() and filename.ends_with(".json"):
			var filepath = WORLDS_DIR + filename
			var file = FileAccess.open(filepath, FileAccess.READ)
			
			if file:
				var json_string = file.get_as_text()
				file.close()
				
				var json = JSON.new()
				if json.parse(json_string) == OK:
					var data = json.data
					# Create lightweight metadata
					worlds.append({
						"id": data.get("id", ""),
						"name": data.get("name", "Unnamed"),
						"width": data.get("width", 0),
						"height": data.get("height", 0),
						"seed": data.get("seed", 0),
						"saved_at": data.get("saved_at", "Unknown"),
						"filepath": filepath,
						"num_settlements": data.get("settlements", {}).size() if data.has("settlements") else 0,
						"num_factions": data.get("factions", []).size() if data.has("factions") else 0
					})
		
		filename = dir.get_next()
	
	dir.list_dir_end()
	return worlds

static func delete_world(world_id: String) -> bool:
	"""Delete a saved world"""
	var worlds = list_worlds()
	for world_meta in worlds:
		if world_meta.get("id") == world_id:
			var filepath = world_meta.get("filepath")
			if DirAccess.remove_absolute(filepath) == OK:
				print("SaveManager: Deleted world %s" % world_id)
				return true
			else:
				push_error("SaveManager: Failed to delete world %s" % filepath)
				return false
	return false

# ============================================================================
# CHARACTER SAVE/LOAD
# ============================================================================

static func save_character(char_data: Dictionary) -> bool:
	"""Save a character to disk"""
	_ensure_directories()
	
	var char_id = char_data.get("id", "char_" + str(Time.get_unix_time_from_system()))
	var char_name = char_data.get("name", "Unnamed")
	var safe_name = char_name.replace(" ", "_").replace("/", "-")
	var filename = "%s_%s.json" % [char_id, safe_name]
	var filepath = CHARACTERS_DIR + filename
	
	# Add metadata
	char_data["id"] = char_id
	char_data["saved_at"] = Time.get_datetime_string_from_system()
	char_data["version"] = SAVE_VERSION
	
	var file = FileAccess.open(filepath, FileAccess.WRITE)
	if not file:
		push_error("SaveManager: Failed to save character to %s" % filepath)
		return false
	
	file.store_string(JSON.stringify(char_data, "\t"))
	file.close()
	
	print("SaveManager: Character '%s' saved to %s" % [char_name, filepath])
	return true

static func load_character(char_id: String) -> Dictionary:
	"""Load a character by ID"""
	var characters = list_characters()
	for char_meta in characters:
		if char_meta.get("id") == char_id:
			var file = FileAccess.open(char_meta.get("filepath"), FileAccess.READ)
			if not file:
				push_error("SaveManager: Failed to load character %s" % char_id)
				return {}
			
			var json_string = file.get_as_text()
			file.close()
			
			var json = JSON.new()
			var error = json.parse(json_string)
			if error != OK:
				push_error("SaveManager: Failed to parse character JSON: %s" % json.get_error_message())
				return {}
			
			return json.data
	
	push_error("SaveManager: Character %s not found" % char_id)
	return {}

static func list_characters() -> Array:
	"""List all saved characters with metadata"""
	_ensure_directories()
	
	var characters = []
	var dir = DirAccess.open(CHARACTERS_DIR)
	
	if not dir:
		return characters
	
	dir.list_dir_begin()
	var filename = dir.get_next()
	
	while filename != "":
		if not dir.current_is_dir() and filename.ends_with(".json"):
			var filepath = CHARACTERS_DIR + filename
			var file = FileAccess.open(filepath, FileAccess.READ)
			
			if file:
				var json_string = file.get_as_text()
				file.close()
				
				var json = JSON.new()
				if json.parse(json_string) == OK:
					var data = json.data
					# Create lightweight metadata
					characters.append({
						"id": data.get("id", ""),
						"name": data.get("name", "Unnamed"),
						"scenario": data.get("scenario", "unknown"),
						"profession": data.get("profession", "unknown"),
						"strength": data.get("strength", 10),
						"agility": data.get("agility", 10),
						"endurance": data.get("endurance", 10),
						"intelligence": data.get("intelligence", 10),
						"traits": data.get("traits", []),
						"saved_at": data.get("saved_at", "Unknown"),
						"filepath": filepath
					})
		
		filename = dir.get_next()
	
	dir.list_dir_end()
	return characters

static func delete_character(char_id: String) -> bool:
	"""Delete a saved character"""
	var characters = list_characters()
	for char_meta in characters:
		if char_meta.get("id") == char_id:
			var filepath = char_meta.get("filepath")
			if DirAccess.remove_absolute(filepath) == OK:
				print("SaveManager: Deleted character %s" % char_id)
				return true
			else:
				push_error("SaveManager: Failed to delete character %s" % filepath)
				return false
	return false

# ============================================================================
# GAME STATE SAVE/LOAD (Active Campaign)
# ============================================================================

static func save_game(game_name: String, world_data: Dictionary, player_data: Dictionary, game_state: Dictionary) -> bool:
	"""Save an active game session"""
	_ensure_directories()
	
	var save_data = {
		"version": SAVE_VERSION,
		"game_name": game_name,
		"saved_at": Time.get_datetime_string_from_system(),
		"world": world_data,
		"player": player_data,
		"state": game_state
	}
	
	var safe_name = game_name.replace(" ", "_").replace("/", "-")
	var filename = "save_%s.json" % safe_name
	var filepath = GAMES_DIR + filename
	
	var file = FileAccess.open(filepath, FileAccess.WRITE)
	if not file:
		push_error("SaveManager: Failed to save game to %s" % filepath)
		return false
	
	file.store_string(JSON.stringify(save_data, "\t"))
	file.close()
	
	print("SaveManager: Game '%s' saved" % game_name)
	return true

static func load_game(game_name: String) -> Dictionary:
	"""Load an active game session"""
	var safe_name = game_name.replace(" ", "_").replace("/", "-")
	var filename = "save_%s.json" % safe_name
	var filepath = GAMES_DIR + filename
	
	var file = FileAccess.open(filepath, FileAccess.READ)
	if not file:
		push_error("SaveManager: Failed to load game %s" % game_name)
		return {}
	
	var json_string = file.get_as_text()
	file.close()
	
	var json = JSON.new()
	var error = json.parse(json_string)
	if error != OK:
		push_error("SaveManager: Failed to parse game JSON: %s" % json.get_error_message())
		return {}
	
	return json.data

static func list_game_saves() -> Array:
	"""List all saved games"""
	_ensure_directories()
	
	var saves = []
	var dir = DirAccess.open(GAMES_DIR)
	
	if not dir:
		return saves
	
	dir.list_dir_begin()
	var filename = dir.get_next()
	
	while filename != "":
		if not dir.current_is_dir() and filename.ends_with(".json"):
			var filepath = GAMES_DIR + filename
			var file = FileAccess.open(filepath, FileAccess.READ)
			
			if file:
				var json_string = file.get_as_text()
				file.close()
				
				var json = JSON.new()
				if json.parse(json_string) == OK:
					var data = json.data
					saves.append({
						"game_name": data.get("game_name", "Unnamed Save"),
						"saved_at": data.get("saved_at", "Unknown"),
						"filepath": filepath,
						"world_name": data.get("world", {}).get("name", "Unknown"),
						"character_name": data.get("player", {}).get("name", "Unknown")
					})
		
		filename = dir.get_next()
	
	dir.list_dir_end()
	return saves

static func get_last_save() -> Dictionary:
	"""Get the most recent save"""
	var saves = list_game_saves()
	if saves.is_empty():
		return {}
	
	# Sort by saved_at timestamp (most recent first)
	saves.sort_custom(func(a, b): return a.get("saved_at", "") > b.get("saved_at", ""))
	return saves[0]
