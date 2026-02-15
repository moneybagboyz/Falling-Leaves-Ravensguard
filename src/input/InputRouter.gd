class_name InputRouter
extends RefCounted

# Input routing system - delegates input events to mode-specific handlers
# Extracted from Main.gd (2639 lines) to improve separation of concerns

var main_node: Node
var game_enums

func _init(main_ref: Node):
	main_node = main_ref
	game_enums = load("res://src/core/GameEnums.gd")

func route_input(event, current_state) -> bool:
	"""
	Route input event to appropriate handler based on game state.
	Returns true if input was handled, false otherwise.
	"""
	
	# Global shortcuts
	if event is InputEventKey and event.pressed:
		# F12 - Turbo Simulation
		if event.keycode == KEY_F12:
			GameState.run_turbo_simulation()
			return true
		
		# Font scaling (Zoom)
		if event.keycode == KEY_EQUAL or event.keycode == KEY_KP_ADD:
			main_node.current_font_size = min(main_node.current_font_size + 2, 48)
			main_node._on_map_updated()
			return true
		if event.keycode == KEY_MINUS or event.keycode == KEY_KP_SUBTRACT:
			main_node.current_font_size = max(main_node.current_font_size - 2, 4)
			main_node._on_map_updated()
			return true

	# ESC - Context-aware exit
	if event.is_action_pressed("ui_cancel"):
		if current_state in [
			game_enums.GameMode.WORLD_LIBRARY,
			game_enums.GameMode.CHARACTER_LIBRARY,
			game_enums.GameMode.GAME_SETUP,
			game_enums.GameMode.WORLD_CREATION,
			game_enums.GameMode.CHARACTER_CREATION,
			game_enums.GameMode.BATTLE_CONFIG,
			game_enums.GameMode.CODEX,
			game_enums.GameMode.MANAGEMENT,
			game_enums.GameMode.PARTY_INFO,
			game_enums.GameMode.FIEF_INFO,
			game_enums.GameMode.HISTORY,
			game_enums.GameMode.DIALOGUE
		]:
			# Check if we're in overworld or should go to menu
			if current_state in [game_enums.GameMode.WORLD_LIBRARY, game_enums.GameMode.CHARACTER_LIBRARY, game_enums.GameMode.GAME_SETUP]:
				main_node.state = game_enums.GameMode.MENU
			else:
				main_node.state = game_enums.GameMode.OVERWORLD
			main_node._on_map_updated()
		main_node.party_panel.visible = false
		main_node.fief_panel.visible = false
		return true

	# Route to mode-specific handlers
	match current_state:
		game_enums.GameMode.MENU:
			main_node.handle_menu_input(event)
			return true
		
		game_enums.GameMode.WORLD_LIBRARY:
			main_node.handle_world_library_input(event)
			return true
		
		game_enums.GameMode.CHARACTER_LIBRARY:
			main_node.handle_character_library_input(event)
			return true
		
		game_enums.GameMode.GAME_SETUP:
			main_node.handle_game_setup_input(event)
			return true
		
		game_enums.GameMode.BATTLE_CONFIG:
			main_node.handle_battle_config_input(event)
			return true
		
		game_enums.GameMode.WORLD_CREATION:
			main_node.handle_world_creation_input(event)
			return true
		
		game_enums.GameMode.WORLD_PREVIEW:
			return handle_world_preview(event)
		
		game_enums.GameMode.CHARACTER_CREATION:
			main_node.handle_character_creation_input(event)
			return true
		
		game_enums.GameMode.PLAY_SELECT:
			main_node.handle_location_select_input(event)
			return true
		
		game_enums.GameMode.CITY:
			return handle_city_mode(event)
		
		game_enums.GameMode.OVERWORLD:
			return handle_overworld_mode(event)
		
		game_enums.GameMode.REGION:
			return handle_region_mode(event)
		
		game_enums.GameMode.BATTLE:
			if main_node.battle_ctrl:
				main_node.battle_ctrl.handle_input(event)
			return true
		
		game_enums.GameMode.DUNGEON:
			if main_node.dungeon_ctrl:
				main_node.dungeon_ctrl.handle_input(event)
			return true
		
		game_enums.GameMode.MANAGEMENT:
			main_node.handle_management_input(event)
			return true
		
		game_enums.GameMode.CODEX:
			main_node.handle_codex_input(event)
			return true
		
		game_enums.GameMode.DIALOGUE:
			return handle_dialogue_mode(event)
	
	return false

func handle_world_preview(event) -> bool:
	if event.is_action_pressed("ui_cancel") or (event is InputEventKey and event.pressed and event.keycode == KEY_TAB):
		main_node.state = game_enums.GameMode.OVERWORLD
		main_node._on_map_updated()
		return true
	return main_node.handle_world_preview_input(event)

func handle_city_mode(event) -> bool:
	if main_node.city_ctrl:
		main_node.city_ctrl.handle_input(event)
		if event.is_action_pressed("ui_cancel"):
			main_node.state = game_enums.GameMode.MENU
			main_node._on_map_updated()
			return true
		
		if not main_node.city_ctrl.active:
			main_node.state = game_enums.GameMode.MENU
			main_node._on_map_updated()
	else:
		main_node.state = game_enums.GameMode.OVERWORLD
		main_node._on_map_updated()
	return true

func handle_overworld_mode(event) -> bool:
	main_node.overworld_ctrl.handle_input(event)
	
	# Codex shortcut
	if event is InputEventKey and event.pressed and event.keycode == KEY_K:
		main_node._try_open_codex_contextual()
	
	# History navigation
	if event is InputEventKey and event.pressed:
		match event.keycode:
			KEY_ESCAPE, KEY_H:
				main_node.state = game_enums.GameMode.OVERWORLD
				main_node._on_map_updated()
			KEY_PAGEDOWN:
				main_node.history_offset += 25
				main_node._on_map_updated()
			KEY_PAGEUP:
				main_node.history_offset = max(0, main_node.history_offset - 25)
				main_node._on_map_updated()
	
	# Update info label based on cursor/player
	main_node._sync_tile_info()
	return true

func handle_region_mode(event) -> bool:
	if main_node.region_ctrl:
		main_node.region_ctrl.handle_input(event)
		if not main_node.region_ctrl.active and main_node.state == game_enums.GameMode.REGION:
			if main_node.saved_character and main_node.generated_world:
				main_node.state = game_enums.GameMode.OVERWORLD
			else:
				main_node.state = game_enums.GameMode.WORLD_PREVIEW
				main_node.preview_pos = GameState.player.pos
			main_node._on_map_updated()
	return true

func handle_dialogue_mode(event) -> bool:
	if event.is_action_pressed("ui_up"):
		main_node.dialogue_idx = posmod(main_node.dialogue_idx - 1, main_node.dialogue_options.size())
		main_node._on_map_updated()
	elif event.is_action_pressed("ui_down"):
		main_node.dialogue_idx = posmod(main_node.dialogue_idx + 1, main_node.dialogue_options.size())
		main_node._on_map_updated()
	elif event.is_action_pressed("ui_accept"):
		main_node.handle_dialogue_choice(main_node.dialogue_options[main_node.dialogue_idx])
	elif event.is_action_pressed("ui_cancel"):
		main_node.state = game_enums.GameMode.OVERWORLD
		main_node._on_map_updated()
	return true
