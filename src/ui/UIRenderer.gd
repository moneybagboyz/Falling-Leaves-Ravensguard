class_name UIRenderer
extends RefCounted

const UIMainMenu = preload("res://src/ui/UIMainMenu.gd")
const UIOverworld = preload("res://src/ui/UIOverworld.gd")
const UIBattle = preload("res://src/ui/UIBattle.gd")
const UIManagement = preload("res://src/ui/UIManagement.gd")
const UILibraries = preload("res://src/ui/UILibraries.gd")
const GameEnums = preload("res://src/core/GameEnums.gd")

static func render(main, state: GameEnums.GameMode, dims: Vector2i):
	"""Route rendering to appropriate UI module based on state"""
	var vw = dims.x
	var vh = dims.y
	
	match state:
		# Menu States
		GameEnums.GameMode.MENU, \
		GameEnums.GameMode.WORLD_CREATION, \
		GameEnums.GameMode.CHARACTER_CREATION, \
		GameEnums.GameMode.PLAY_SELECT, \
		GameEnums.GameMode.BATTLE_CONFIG, \
		GameEnums.GameMode.CODEX:
			UIMainMenu.render(main, state)
		
		# Library States (DF/CDDA style)
		GameEnums.GameMode.WORLD_LIBRARY:
			_render_world_library(main)
		
		GameEnums.GameMode.CHARACTER_LIBRARY:
			_render_character_library(main)
		
		GameEnums.GameMode.GAME_SETUP:
			_render_game_setup(main)
		
		# Loading State
		GameEnums.GameMode.LOADING:
			UIManagement.render_loading(main)
		
		# Overworld States
		GameEnums.GameMode.OVERWORLD:
			UIOverworld.render(main, vw, vh)
		
		GameEnums.GameMode.WORLD_PREVIEW:
			UIOverworld.render_world_preview(main, vw, vh)
		
		GameEnums.GameMode.REGION:
			UIOverworld.render_region(main, vw, vh)
		
		# Battle/Exploration States
		GameEnums.GameMode.BATTLE:
			UIBattle.render(main, vw, vh)
		
		GameEnums.GameMode.CITY:
			# Check if in studio mode or exploration mode
			if main.city_ctrl and main.city_ctrl.active:
				UIBattle.render_city(main, vw, vh)
			else:
				UIBattle.render_city_studio(main)
		
		GameEnums.GameMode.DUNGEON:
			UIBattle.render_dungeon(main, vw, vh)
		
		# Management States
		GameEnums.GameMode.MANAGEMENT:
			UIManagement.render(main)
		
		GameEnums.GameMode.DIALOGUE:
			UIManagement.render_dialogue(main)

static func should_use_viewport(state: GameEnums.GameMode) -> bool:
	"""Check if state should use viewport rendering instead of text"""
	var graphical_states = [
		GameEnums.GameMode.OVERWORLD,
		GameEnums.GameMode.BATTLE,
		GameEnums.GameMode.DUNGEON,
		GameEnums.GameMode.CITY
		# Note: WORLD_PREVIEW uses text rendering (minimap-style ASCII)
	]
	return state in graphical_states

static func get_target_font_size(main, state: GameEnums.GameMode) -> int:
	"""Get appropriate font size for current state"""
	match state:
		GameEnums.GameMode.WORLD_PREVIEW:
			# Shader renderer handles zoom, use reasonable fallback for ASCII mode
			return 8
		GameEnums.GameMode.OVERWORLD:
			GameState.player.camera_zoom = main.current_font_size / 16.0
			return main.current_font_size
		GameEnums.GameMode.BATTLE:
			if is_instance_valid(main.battle_ctrl):
				main.battle_ctrl.camera_zoom = main.current_font_size / 16.0
			return main.current_font_size
		_:
			return main.current_font_size

# ============================================================================
# LIBRARY RENDERING (DF/CDDA style)
# ============================================================================

static func _render_world_library(main):
	"""Render the world library screen"""
	var map_display = main.map_display
	map_display.text = UILibraries.render_world_library(
		main.saved_worlds,
		main.world_library_idx,
		main.library_focus
	)
	main.set_panels_visible(false, false, "[center]WORLD LIBRARY[/center]")

static func _render_character_library(main):
	"""Render the character library screen"""
	var map_display = main.map_display
	map_display.text = UILibraries.render_character_library(
		main.saved_characters,
		main.char_library_idx,
		main.library_focus
	)
	main.set_panels_visible(false, false, "[center]CHARACTER LIBRARY[/center]")

static func _render_game_setup(main):
	"""Render the game setup screen (select world + character)"""
	var map_display = main.map_display
	
	# Get selected indices based on IDs
	var world_idx = -1
	var char_idx = -1
	
	for i in range(main.saved_worlds.size()):
		if main.saved_worlds[i].get("id") == main.selected_world_id:
			world_idx = i
			break
	
	for i in range(main.saved_characters.size()):
		if main.saved_characters[i].get("id") == main.selected_char_id:
			char_idx = i
			break
	
	map_display.text = UILibraries.render_game_setup(
		main.saved_worlds,
		main.saved_characters,
		world_idx,
		char_idx,
		main.game_setup_focus
	)
	main.set_panels_visible(false, false, "[center]NEW GAME SETUP[/center]")
