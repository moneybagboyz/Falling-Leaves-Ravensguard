class_name GameEnums
extends Object

# Game State Enum - replaces string-based state management
enum GameMode {
	MENU,
	LOADING,
	WORLD_LIBRARY,      # Browse/manage saved worlds
	CHARACTER_LIBRARY,  # Browse/manage saved characters
	GAME_SETUP,         # Select world + character before starting
	WORLD_CREATION,
	WORLD_PREVIEW,
	CHARACTER_CREATION,
	PLAY_SELECT,
	OVERWORLD,
	BATTLE,
	BATTLE_CONFIG,
	MANAGEMENT,
	DUNGEON,
	DIALOGUE,
	CODEX,
	CITY,
	REGION,
	PARTY_INFO,
	FIEF_INFO,
	HISTORY
}

# Management Tab Enum
enum ManagementTab {
	CHARACTER,
	ROSTER,
	MARKET,
	OFFICE
}

# Map Mode Enum
enum MapMode {
	TERRAIN,
	POLITICAL,
	PROVINCE,
	RESOURCE
}

# Render Mode Enum
enum RenderMode {
	ASCII,
	GRID
}

# Travel Mode Enum
enum TravelMode {
	FAST,
	REGION,
	LOCAL
}

# Helper function to convert old string states to enum (for migration)
static func state_from_string(state_str: String) -> GameMode:
	match state_str:
		"menu": return GameMode.MENU
		"loading": return GameMode.LOADING
		"world_library": return GameMode.WORLD_LIBRARY
		"character_library": return GameMode.CHARACTER_LIBRARY
		"game_setup": return GameMode.GAME_SETUP
		"world_creation": return GameMode.WORLD_CREATION
		"world_preview": return GameMode.WORLD_PREVIEW
		"character_creation": return GameMode.CHARACTER_CREATION
		"play_select": return GameMode.PLAY_SELECT
		"overworld": return GameMode.OVERWORLD
		"battle": return GameMode.BATTLE
		"battle_config": return GameMode.BATTLE_CONFIG
		"management": return GameMode.MANAGEMENT
		"dungeon": return GameMode.DUNGEON
		"dialogue": return GameMode.DIALOGUE
		"codex": return GameMode.CODEX
		"city": return GameMode.CITY
		"region": return GameMode.REGION
		"party_info": return GameMode.PARTY_INFO
		"fief_info": return GameMode.FIEF_INFO
		"history": return GameMode.HISTORY
		_: return GameMode.MENU

# Helper function to convert enum to string (for debugging/saving)
static func state_to_string(mode: GameMode) -> String:
	match mode:
		GameMode.MENU: return "menu"
		GameMode.LOADING: return "loading"
		GameMode.WORLD_LIBRARY: return "world_library"
		GameMode.CHARACTER_LIBRARY: return "character_library"
		GameMode.GAME_SETUP: return "game_setup"
		GameMode.WORLD_CREATION: return "world_creation"
		GameMode.WORLD_PREVIEW: return "world_preview"
		GameMode.CHARACTER_CREATION: return "character_creation"
		GameMode.PLAY_SELECT: return "play_select"
		GameMode.OVERWORLD: return "overworld"
		GameMode.BATTLE: return "battle"
		GameMode.BATTLE_CONFIG: return "battle_config"
		GameMode.MANAGEMENT: return "management"
		GameMode.DUNGEON: return "dungeon"
		GameMode.DIALOGUE: return "dialogue"
		GameMode.CODEX: return "codex"
		GameMode.CITY: return "city"
		GameMode.REGION: return "region"
		GameMode.PARTY_INFO: return "party_info"
		GameMode.FIEF_INFO: return "fief_info"
		GameMode.HISTORY: return "history"
		_: return "menu"
