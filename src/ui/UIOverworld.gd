class_name UIOverworld
extends RefCounted

const UIPanels = preload("res://src/utils/UIPanels.gd")
const GameEnums = preload("res://src/core/GameEnums.gd")

static func render(main, vw: int, vh: int):
	"""Render overworld screen with viewport and side panels"""
	var map_display = main.map_display
	var info_label = main.info_label
	var log_label = main.log_label
	
	# Reset font sizes
	info_label.add_theme_font_size_override("normal_font_size", 16)
	log_label.add_theme_font_size_override("normal_font_size", 16)
	
	# Show panels
	main.set_panels_visible(true, true)
	info_label.visible = true
	
	var map_lines = []
	var header_override = ""
	
	# Check travel mode
	if GameState.travel_mode == GameState.TravelMode.LOCAL:
		# Local mode - use battle controller's tactical map
		if main.battle_ctrl.last_map_pos != GameState.player.pos:
			main.battle_ctrl.generate_map()
		map_lines = UIPanels.render_local_viewport(GameState, main.battle_ctrl, vw, vh)
		header_override = "[ TRAVEL - LOCAL MODE ]"
	else:
		# World/Region mode - standard viewport
		map_lines = UIPanels.render_viewport(GameState, vw, vh, main.last_calculated_path)
	
	var side_lines = UIPanels.get_side_panel(GameState)
	var header = header_override if header_override != "" else "[ %s ]" % GameState.get_date_string().to_upper()
	
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

static func render_world_preview(main, vw: int, vh: int):
	"""Render world preview with map and minimap"""
	var map_display = main.map_display
	var info_label = main.info_label
	var log_label = main.log_label
	
	var frame = UIPanels.render_world_preview(GameState, main.preview_pos)
	info_label.text = frame.side
	main.set_panels_visible(true, true, frame.header)
	log_label.text = "WASD: Pan | +/- or Mouse Wheel: Zoom | 1-4: Map Modes | ENTER: Accept | R: Reroll"
	
	# Calculate zoomed viewport dimensions
	var zoom = main.preview_zoom if "preview_zoom" in main else 1.0
	var zoomed_vw = int(vw / zoom)
	var zoomed_vh = int(vh / zoom)
	
	# Render world map with zoom
	var world_lines = UIPanels.render_world_map(GameState, zoomed_vw, zoomed_vh, main.preview_pos)
	map_display.text = "\n".join(world_lines)

static func render_region(main, vw: int, vh: int):
	"""Render region view"""
	var map_display = main.map_display
	var info_label = main.info_label
	var log_label = main.log_label
	
	info_label.add_theme_font_size_override("normal_font_size", 16)
	log_label.add_theme_font_size_override("normal_font_size", 16)
	
	main.set_panels_visible(true, true)
	info_label.visible = true
	
	var map_lines = UIPanels.render_region(GameState, main.region_ctrl, vw, vh)
	var side_lines = UIPanels.get_side_panel(GameState)
	var p_pos_i = Vector2i(main.region_ctrl.player_pos)
	var header = "[ REGION VIEW - %d, %d ]" % [p_pos_i.x, p_pos_i.y]
	
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

static func render_history(main):
	"""Render history screen"""
	var map_display = main.map_display
	
	map_display.text = UIPanels.render_history(GameState, main.history_offset)
	main.set_panels_visible(false, false)
