class_name UIManagement
extends RefCounted

const UIPanels = preload("res://src/utils/UIPanels.gd")
const GameEnums = preload("res://src/core/GameEnums.gd")

static func render(main):
	"""Render management/party info screen"""
	var map_display = main.map_display
	
	var content = UIPanels.get_management_screen(
		GameState,
		main.mgmt_tab,
		main.mgmt_focus,
		main.mgmt_idx_l,
		main.mgmt_idx_r,
		main.mgmt_is_designing,
		main.mgmt_design_slot,
		main.mgmt_design_prop
	)
	
	map_display.text = content
	main.set_panels_visible(false, false)

static func render_dialogue(main):
	"""Render dialogue screen"""
	var map_display = main.map_display
	
	map_display.text = UIPanels.get_dialogue_screen(
		GameState,
		main.dialogue_target,
		main.dialogue_options,
		main.dialogue_idx
	)
	main.set_panels_visible(false, false)

static func render_loading(main):
	"""Render loading screen with full-resolution world preview"""
	var map_display = main.map_display
	var info_label = main.info_label
	
	# Show panels and set header
	main.set_panels_visible(true, false, "[center][b]GENERATING WORLD[/b][/center]")
	
	# Side panel - show progress
	info_label.text = "[b]PROGRESS[/b]\n\n" + main.loading_stage
	
	# Map - show full-resolution world grid if available
	map_display.bbcode_enabled = true
	
	if GameState.grid.size() > 0:
		# Calculate viewport dimensions
		var dims = main.get_char_dims()
		var vw = max(10, dims.x)
		var vh = max(5, dims.y - 12)
		
		# Render full world map centered on world center
		var center = Vector2i(GameState.width / 2, GameState.height / 2)
		var world_lines = UIPanels.render_world_map(GameState, vw, vh, center)
		map_display.text = "\n".join(world_lines)
	else:
		# Initial state before grid exists
		map_display.text = "\n\n\n\n[center][color=gray]... Procedural Matrix Initializing ...[/color][/center]"
	
	map_display.visible = true
