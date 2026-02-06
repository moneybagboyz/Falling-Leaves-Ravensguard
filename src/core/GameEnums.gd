class_name GameEnums
extends Object

# Game State Enum - replaces string-based state management
enum GameMode {
	MENU,
	LOADING,
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
	REGION
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
		_: return GameMode.MENU

# Helper function to convert enum to string (for debugging/saving)
static func state_to_string(mode: GameMode) -> String:
	match mode:
		GameMode.MENU: return "menu"
		GameMode.LOADING: return "loading"
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
		_: return "menu"
