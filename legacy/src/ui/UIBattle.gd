class_name UIBattle
extends RefCounted

const UIPanels = preload("res://src/utils/UIPanels.gd")
const GameEnums = preload("res://src/core/GameEnums.gd")

static func render(main, vw: int, vh: int):
	"""Render battle screen"""
	var map_display = main.map_display
	var info_label = main.info_label
	var log_label = main.log_label
	var battle_ctrl = main.battle_ctrl
	
	# Reset font sizes
	info_label.add_theme_font_size_override("normal_font_size", 16)
	log_label.add_theme_font_size_override("normal_font_size", 16)
	
	# Show panels
	main.set_panels_visible(true, true)
	info_label.visible = true
	
	var map_lines = UIPanels.render_battle(GameState, battle_ctrl, vw, vh)
	var side_lines = UIPanels.get_battle_side_panel(GameState, battle_ctrl)
	
	# Determine enemy type for header
	var e_type = "battle"
	if battle_ctrl.enemy_ref is Dictionary:
		e_type = battle_ctrl.enemy_ref.get("type", "battle")
	elif battle_ctrl.enemy_ref:
		e_type = battle_ctrl.enemy_ref.type
	
	var header = "[ BATTLE - %s ]" % e_type.to_upper()
	
	var frame = UIPanels.get_master_frame(
		GameState,
		map_lines,
		side_lines,
		header,
		vw,
		battle_ctrl.battle_log,
		10,
		battle_ctrl.log_offset
	)
	
	map_display.text = frame.map
	info_label.text = frame.side
	log_label.text = frame.log
	main.get_node("MainLayout/ScreenHeader").text = "[center]%s[/center]" % frame.header

static func render_city(main, vw: int, vh: int):
	"""Render city exploration screen"""
	var map_display = main.map_display
	var info_label = main.info_label
	var log_label = main.log_label
	var city_ctrl = main.city_ctrl
	
	# Reset font sizes
	info_label.add_theme_font_size_override("normal_font_size", 16)
	log_label.add_theme_font_size_override("normal_font_size", 16)
	
	# Show panels
	main.set_panels_visible(true, true)
	info_label.visible = true
	
	var map_lines = UIPanels.render_city(GameState, city_ctrl, vw, vh)
	var side_lines = ["City: " + city_ctrl.city_name, "Pos: " + str(city_ctrl.player_pos)]
	var header = "[ CITY EXPLORER - %s ]" % city_ctrl.city_name.to_upper()
	
	var frame = UIPanels.get_master_frame(
		GameState,
		map_lines,
		side_lines,
		header,
		vw,
		GameState.event_log,
		10
	)
	
	map_display.text = frame.map
	info_label.text = frame.side
	log_label.text = frame.log
	main.get_node("MainLayout/ScreenHeader").text = "[center]%s[/center]" % frame.header

static func render_dungeon(main, vw: int, vh: int):
	"""Render dungeon exploration screen"""
	var map_display = main.map_display
	var info_label = main.info_label
	var log_label = main.log_label
	var dungeon_ctrl = main.dungeon_ctrl
	
	# Reset font sizes
	info_label.add_theme_font_size_override("normal_font_size", 16)
	log_label.add_theme_font_size_override("normal_font_size", 16)
	
	# Show panels
	main.set_panels_visible(true, true)
	info_label.visible = true
	
	var map_lines = UIPanels.render_dungeon(GameState, dungeon_ctrl, vw, vh)
	var side_lines = UIPanels.get_side_panel(GameState, dungeon_ctrl)
	var header = "[ %s - Floor %d ]" % [dungeon_ctrl.dungeon_name.to_upper(), dungeon_ctrl.current_floor]
	
	var frame = UIPanels.get_master_frame(
		GameState,
		map_lines,
		side_lines,
		header,
		vw,
		dungeon_ctrl.messages,
		10,
		dungeon_ctrl.log_offset
	)
	
	map_display.text = frame.map
	info_label.text = frame.side
	log_label.text = frame.log
	main.get_node("MainLayout/ScreenHeader").text = "[center]%s[/center]" % frame.header

static func render_city_studio(main):
	"""Render city design studio"""
	var map_display = main.map_display
	
	map_display.text = UIPanels.render_city_studio(GameState.city_studio_config, GameState.city_studio_idx)
	main.set_panels_visible(false, false, "[center]CITY DESIGN STUDIO[/center]")
	map_display.visible = true
