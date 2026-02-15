class_name UIMainMenu
extends RefCounted

const UIPanels = preload("res://src/utils/UIPanels.gd")
const GameEnums = preload("res://src/core/GameEnums.gd")

static func render(main, state: GameEnums.GameMode):
	"""Render menu-related screens"""
	match state:
		GameEnums.GameMode.MENU:
			_render_main_menu(main)
		GameEnums.GameMode.WORLD_CREATION:
			_render_world_creation(main)
		GameEnums.GameMode.CHARACTER_CREATION:
			_render_character_creation(main)
		GameEnums.GameMode.PLAY_SELECT:
			_render_play_select(main)
		GameEnums.GameMode.BATTLE_CONFIG:
			_render_battle_config(main)
		GameEnums.GameMode.CODEX:
			_render_codex(main)

static func _render_main_menu(main):
	var map_display = main.map_display
	var has_world = main.generated_world != null
	var has_char = main.saved_character != null
	
	map_display.text = UIPanels.render_menu(main.menu_options, main.menu_idx, has_world, has_char)
	main.set_panels_visible(false, false, "[center]FALLING LEAVES[/center]")
	map_display.visible = true

static func _render_world_creation(main):
	var map_display = main.map_display
	
	map_display.bbcode_enabled = true
	map_display.text = UIPanels.render_world_creation(main.world_config, main.world_config_idx)
	main.set_panels_visible(false, false, "[center]WORLD GENERATOR[/center]")

static func _render_character_creation(main):
	var map_display = main.map_display
	
	map_display.bbcode_enabled = true
	map_display.text = UIPanels.render_character_creation_tabbed(
		main.player_config,
		main.player_config_idx,
		main.trait_selection_idx,
		main.calculate_creation_points(),
		main.cc_tab,
		main.cc_shop_items,
		main.cc_purchases,
		main.calculate_available_crowns(),
		main.CC_MATERIALS[main.cc_mat_idx],
		main.CC_QUALITIES[main.cc_qual_idx]
	)
	main.set_panels_visible(false, false)

static func _render_play_select(main):
	var map_display = main.map_display
	var current_loc = main.location_list[main.location_idx]
	
	map_display.bbcode_enabled = true
	map_display.text = UIPanels.render_location_select(GameState, current_loc)
	main.set_panels_visible(false, false)

static func _render_battle_config(main):
	var map_display = main.map_display
	
	map_display.bbcode_enabled = true
	map_display.text = UIPanels.render_battle_config(main.sim_config, main.sim_config_idx)
	main.set_panels_visible(false, false, "[center]BATTLE SIMULATOR[/center]")

static func _render_codex(main):
	var map_display = main.map_display
	const CodexData = preload("res://src/data/CodexData.gd")
	
	map_display.text = UIPanels.render_codex(
		GameState,
		CodexData,
		main.codex_cat_idx,
		main.codex_entry_idx,
		main.codex_focus
	)
	main.set_panels_visible(false, false)
