extends Control

const UIPanels = preload("res://src/utils/UIPanels.gd")
const CodexData = preload("res://src/data/CodexData.gd")

@onready var map_display = $MainLayout/ContentLayout/MapPanel/MapDisplay
@onready var info_label = $MainLayout/ContentLayout/SidePanel/InfoLabel
@onready var party_panel = $MainLayout/ContentLayout/SidePanel/PartyPanel
@onready var fief_panel = $MainLayout/ContentLayout/SidePanel/FiefPanel
@onready var log_label = $MainLayout/LogPanel/LogLabel
@onready var battle_ui = $BattleUI
var mouse_cursor
var tile_map: TileMap
var graphical_mode = true

@onready var overworld_ctrl = $OverworldController
@onready var battle_ctrl = $BattleController
var dungeon_ctrl
var city_ctrl
var region_ctrl
var mono_font

var state = "menu" # overworld, battle, management, dungeon, dialogue, loading, menu, battle_config, codex, city, world_creation, world_preview, character_creation, play_select
var loading_stage = ""
var history_offset = 0
var menu_idx = 0
var menu_options = ["NEW WORLD GEN", "CHARACTER CREATOR", "START ADVENTURE", "BATTLE SIMULATOR", "CITY GENERATOR", "THE GREAT ARCHIVE", "RENDERING: SOLID GRID"]

var world_config = {
	"name": "Aequor",
	"width": 200,
	"height": 200,
	"num_plates": 15,
	"num_factions": 5, # 2-10
	"layout": "Pangea", # Pangea, Continents, Archipelago
	"mineral_density": 5, # 1-10
	"savagery": 5, # 1-10
	"moisture": 1.0, 
	"temperature": 1.0,
	"seed": 0
}
var world_config_idx = 0
var generated_world = null # Store successful world data

var player_config = {
	"name": "Survivor",
	"scenario": "trader_caravan",
	"profession": "mercenary",
	"traits": [], # List of trait keys
	"strength": 8,
	"agility": 8,
	"endurance": 8,
	"intelligence": 8,
}
var player_config_idx = 0
var trait_selection_idx = 0 # To navigate the trait list separately if needed
var cc_tab = 0 # 0: Background, 1: Stats & Traits, 2: Loadout, 3: Summary
var cc_shop_idx = 0
var cc_purchases = [] # Array of dictionaries: {"id": "shortsword", "mat": "iron", "qual": "average"}
const CC_MATERIALS = ["cloth", "leather", "wood", "copper", "iron", "steel"]
const CC_QUALITIES = ["shoddy", "average", "well_made", "masterwork"]
var cc_mat_idx = 4 # Default to iron
var cc_qual_idx = 1 # Default to average
var cc_shop_items = [
	"shortsword", "hand_axe", "mace", "dagger", "club",
	"spear", "shortbow", "arrows",
	"shirt", "trousers", "tunic", "gambeson", "leather_armor",
	"helmet", "cap", "boots", "gloves",
	"heater_shield", "buckler",
	"mule", "horse", "cart"
]
var saved_character = null # Store custom character
var location_list = []
var location_idx = 0

var preview_zoom = 1.0
var preview_pos = Vector2i(100, 100)

var sim_config = {
	"p_lineup": [
		{"type": "spearman", "cnt": 150, "lvl": 3},
		{"type": "archer", "cnt": 90, "lvl": 3},
		{"type": "catapult", "cnt": 5, "lvl": 3},
		{"type": "ballista", "cnt": 5, "lvl": 3}
	],
	"e_lineup": [
		{"type": "spearman", "cnt": 150, "lvl": 3},
		{"type": "archer", "cnt": 90, "lvl": 3},
		{"type": "catapult", "cnt": 5, "lvl": 3},
		{"type": "ballista", "cnt": 5, "lvl": 3}
	]
}
var sim_config_idx = 0
var current_font_size = 16
var mgmt_tab = "CHARACTER" # CHARACTER, ROSTER, MARKET, OFFICE
var mgmt_focus = 0 # 0: Left, 1: Right
var mgmt_idx_l = 0
var mgmt_idx_r = 0

# Dialogue State
var dialogue_target = null
var dialogue_options = []
var dialogue_idx = 0

# Mouse State
var mouse_grid_pos = Vector2i.ZERO
var mouse_in_map = false
var last_hover_world_pos = Vector2i(-1, -1)
var last_calculated_path = [] # Task 5: Store the current path for rendering

# Designer State
var mgmt_is_designing = false
var mgmt_design_slot = 0 # 0:head, 1:torso, 2:main, 3:off
var mgmt_design_prop = 0 # 0:type, 1:mat, 2:qual

# Codex State
var codex_cat_idx = 0
var codex_entry_idx = 0
var codex_focus = 0 # 0: Categories, 1: Entries

const TILE_SIZE = 16

func _ready():
	# 1. Setup Panels and Border
	setup_classic_ui()
	
	# Mouse Cursor setup
	var rect_cursor = ColorRect.new()
	rect_cursor.name = "MouseCursor"
	rect_cursor.color = Color(1, 1, 1, 0.15)
	rect_cursor.mouse_filter = Control.MOUSE_FILTER_IGNORE
	$MainLayout/ContentLayout/MapPanel.add_child(rect_cursor)
	mouse_cursor = rect_cursor
	
	_setup_view_buttons()

	# 2. Setup Monospaced Font for ASCII Grid
	mono_font = SystemFont.new()
	mono_font.font_names = PackedStringArray(["Consolas", "Courier New", "Monospace"])
	
	setup_tile_map()
	GameState.graphical_mode_active = graphical_mode
	
	var header_node = $MainLayout/ScreenHeader
	var battle_info = $BattleUI/VBoxContainer/BattleInfo
	for node in [map_display, info_label, party_panel, fief_panel, log_label, header_node, battle_info]:
		if not node: continue
		node.add_theme_font_override("normal_font", mono_font)
		node.add_theme_font_override("bold_font", mono_font)
		node.add_theme_font_override("mono_font", mono_font)
		node.add_theme_font_size_override("normal_font_size", current_font_size)
		if node is RichTextLabel:
			node.bbcode_enabled = true
			node.autowrap_mode = TextServer.AUTOWRAP_OFF
			_update_node_theme(node)

	# Apply to battle map too
	var b_map = $BattleUI/VBoxContainer/BattleMap
	if b_map:
		b_map.add_theme_font_override("normal_font", mono_font)
		b_map.add_theme_font_override("mono_font", mono_font)
		b_map.autowrap_mode = TextServer.AUTOWRAP_OFF
		_update_node_theme(b_map)

	dungeon_ctrl = load("res://src/controllers/DungeonController.gd").new()
	add_child(dungeon_ctrl)

	city_ctrl = load("res://src/controllers/CityController.gd").new()
	add_child(city_ctrl)

	region_ctrl = load("res://src/controllers/RegionController.gd").new()
	add_child(region_ctrl)
	GameState.region_ctrl = region_ctrl
	region_ctrl.settlement_entered.connect(_on_settlement_entered)

	GameState.connect("map_updated", _on_map_updated)
	GameState.connect("log_updated", _on_log_updated)
	GameState.connect("battle_started", _on_battle_started)
	GameState.connect("battle_ended", _on_battle_ended)
	GameState.connect("dungeon_started", _on_dungeon_started)
	GameState.connect("dungeon_ended", _on_dungeon_ended)
	GameState.connect("dialogue_started", _on_dialogue_started)
	GameState.world_gen_updated.connect(_on_world_gen_updated)
	get_viewport().size_changed.connect(_on_map_updated)
	
	map_display.gui_input.connect(_on_map_display_gui_input)
	map_display.mouse_entered.connect(func(): mouse_in_map = true)
	map_display.mouse_exited.connect(func(): mouse_in_map = false)
	
	# Initial render
	if state == "loading":
		_on_world_gen_updated("CONNECTING TO AEQUOR...")
	else:
		call_deferred("_on_map_updated")
		_on_log_updated()
		# Force initial info label update
		var cursor = GameState.player.pos + GameState.player.camera_offset
		info_label.text = UIPanels.get_tile_info(GameState, cursor)

func _setup_view_buttons():
	var hbox = HBoxContainer.new()
	hbox.position = Vector2(400, 10) # Top centerish
	hbox.add_theme_constant_override("separation", 10)
	
	var btn_toggle = Button.new()
	btn_toggle.text = " View Mode [T] "
	btn_toggle.pressed.connect(func(): overworld_ctrl.toggle_travel_mode())
	
	hbox.add_child(btn_toggle)
	print("Added View Mode button to MainLayout")
	$MainLayout.add_child(hbox)

func _set_view_mode(mode):
	# Cycle until we hit the mode (simple hack since toggle_travel_mode cycles)
	var safety = 0
	while GameState.travel_mode != mode and safety < 4:
		overworld_ctrl.toggle_travel_mode()
		safety += 1

func _update_node_theme(node: RichTextLabel, force_fs: int = -1):
	# If we are in loading/menu/creation states, we don't want the squished grid logic
	var is_map_state = state in ["overworld", "battle", "dungeon", "city", "world_preview", "region"]
	
	if GameState.render_mode == "grid" and is_map_state:
		var font = node.get_theme_font("normal_font")
		var font_size = force_fs if force_fs > 0 else node.get_theme_font_size("normal_font_size")
		
		# Measure natural dimensions - FORCE INTEGER for pixel-perfect alignment
		var char_w = int(font.get_string_size("█", HORIZONTAL_ALIGNMENT_LEFT, -1, font_size).x)
		var char_h = font.get_height(font_size)
		
		# Square Logic: Force line height to match character width
		var target_line_sep = char_w - char_h
		
		node.add_theme_constant_override("line_separation", target_line_sep)
		
		# Optimization: Ensure internal padding is zeroed out to prevent drift
		node.add_theme_constant_override("outline_size", 0)
	else:
		node.add_theme_constant_override("line_separation", 0)

func _update_all_ui_themes():
	var header_node = $MainLayout/ScreenHeader
	var battle_info = $BattleUI/VBoxContainer/BattleInfo
	var battle_map = $BattleUI/VBoxContainer/BattleMap
	for node in [map_display, info_label, party_panel, fief_panel, log_label, header_node, battle_info, battle_map]:
		if node is RichTextLabel:
			_update_node_theme(node)
			node.add_theme_font_size_override("normal_font_size", current_font_size)
	
	if battle_map:
		battle_map.add_theme_font_override("normal_font", mono_font)
		battle_map.add_theme_font_override("mono_font", mono_font)
		battle_map.autowrap_mode = TextServer.AUTOWRAP_OFF

func _on_map_display_gui_input(event):
	if state == "loading": return
	
	if event is InputEventMouseButton and event.pressed:
		if event.button_index == MOUSE_BUTTON_WHEEL_UP:
			if state == "overworld":
				GameState.player.camera_zoom = clamp(GameState.player.camera_zoom + 0.1, 0.5, 3.0)
				_on_map_updated()
				return
		elif event.button_index == MOUSE_BUTTON_WHEEL_DOWN:
			if state == "overworld":
				GameState.player.camera_zoom = clamp(GameState.player.camera_zoom - 0.1, 0.5, 3.0)
				_on_map_updated()
				return

	if event is InputEventMouseMotion or (event is InputEventMouseButton and event.pressed):
		var char_size = _get_actual_char_size()
		var local_pos = event.position - Vector2(10, 10) # Offset for StyleBox margin
		
		var grid_x = int(local_pos.x / char_size.x)
		var grid_y = int(local_pos.y / char_size.y)
		
		# Move visual cursor
		if is_instance_valid(mouse_cursor):
			mouse_cursor.size = char_size
			mouse_cursor.position = Vector2(grid_x * char_size.x, grid_y * char_size.y) + Vector2(10, 10)
		
		var world_pos = _screen_to_world(Vector2i(grid_x, grid_y))
		
		if event is InputEventMouseMotion:
			_handle_mouse_hover(world_pos, Vector2i(grid_x, grid_y))
		
		if event is InputEventMouseButton and event.pressed:
			if event.button_index == MOUSE_BUTTON_LEFT:
				_handle_mouse_click(world_pos, Vector2i(grid_x, grid_y))

func _handle_mouse_hover(world_pos, grid_pos):
	if state == "overworld":
		if world_pos != last_hover_world_pos:
			last_hover_world_pos = world_pos
			_sync_info_at(world_pos)
			
			# TASK 5: Path Visualization
			if GameState.player.pos.distance_to(world_pos) < 50: 
				last_calculated_path = GameState.astar.get_id_path(GameState.player.pos, world_pos)
			else:
				last_calculated_path = []
			_on_map_updated()
	elif state == "dialogue":
		# Dialogue start offset is roughly 10 lines down (2 top, 1 title, 2 encounter, 1 rel, 1 inv, 1 prompt, 1 space)
		var option_idx = grid_pos.y - 9 
		if option_idx >= 0 and option_idx < dialogue_options.size():
			if dialogue_idx != option_idx:
				dialogue_idx = option_idx
				_on_map_updated()
	elif state == "menu":
		# Menu starts roughly at line 4
		var option_idx = grid_pos.y - 4
		if option_idx >= 0 and option_idx < menu_options.size():
			if menu_idx != option_idx:
				menu_idx = option_idx
				_on_map_updated()
	elif state == "management":
		_handle_mgmt_hover(grid_pos)

func _handle_mgmt_hover(grid_pos):
	# Mgmt screen: Left column is roughly 0-42, Right is 45-80
	# Lines start after header (roughly line 5)
	if grid_pos.y < 5: return
	
	if grid_pos.x < 42:
		mgmt_focus = 0
		mgmt_idx_l = grid_pos.y - 6
	else:
		mgmt_focus = 1
		mgmt_idx_r = grid_pos.y - 6
	# We don't force _on_map_updated on EVERY hover to avoid flicker, 
	# but clicking will definitely trigger it.

func _sync_info_at(pos: Vector2i):
	info_label.text = UIPanels.get_tile_info(GameState, pos)

func _handle_mouse_click(world_pos, grid_pos):
	if state == "overworld":
		if GameState.is_walkable(world_pos):
			if world_pos == GameState.player.pos:
				overworld_ctrl.try_interact()
			else:
				var path = GameState.astar.get_id_path(GameState.player.pos, world_pos)
				if path.size() > 1:
					var next = path[1]
					var diff = next - GameState.player.pos
					overworld_ctrl.handle_movement_delta(diff)
	elif state == "battle":
		_handle_battle_click(world_pos)
	elif state == "dialogue":
		var option_idx = grid_pos.y - 9
		if option_idx >= 0 and option_idx < dialogue_options.size():
			dialogue_idx = option_idx
			handle_dialogue_choice(dialogue_options[dialogue_idx])
	elif state == "menu":
		var option_idx = grid_pos.y - 4
		if option_idx >= 0 and option_idx < menu_options.size():
			menu_idx = option_idx
			handle_dialogue_choice(menu_options[menu_idx])
	elif state == "management":
		_on_map_updated() # Update to show selection

func _handle_battle_click(world_pos):
	if not battle_ctrl.active: return
	
	for u in battle_ctrl.units:
		if u.pos == world_pos and u.team == "enemy":
			battle_ctrl.execute_player_attack(u)
			return
	
	if battle_ctrl.player_unit and battle_ctrl.is_in_bounds(world_pos):
		var diff = world_pos - battle_ctrl.player_unit.pos
		var dir = Vector2i(sign(diff.x), sign(diff.y))
		if dir.x != 0 and dir.y != 0: dir.y = 0
		battle_ctrl.move_player(dir)

func _get_actual_char_size() -> Vector2:
	var font = map_display.get_theme_font("normal_font")
	var font_size = map_display.get_theme_font_size("normal_font_size")
	var char_w = int(font.get_string_size("█", HORIZONTAL_ALIGNMENT_LEFT, -1, font_size).x)
	var char_h = char_w if GameState.render_mode == "grid" else int(font.get_height(font_size))
	return Vector2(char_w, char_h)

func _screen_to_world(grid_pos: Vector2i) -> Vector2i:
	var dims = get_char_dims()
	if state == "overworld":
		var center = GameState.player.pos + GameState.player.camera_offset
		var start_x = clamp(center.x - dims.x / 2.0, 0, max(0, GameState.width - dims.x))
		var start_y = clamp(center.y - dims.y / 2.0, 0, max(0, GameState.height - dims.y))
		return Vector2i(int(start_x + grid_pos.x), int(start_y + grid_pos.y))
	elif state == "battle" and is_instance_valid(battle_ctrl):
		var center = Vector2(battle_ctrl.MAP_W/2, battle_ctrl.MAP_H/2)
		if battle_ctrl.camera_locked and battle_ctrl.player_unit:
			center = Vector2(battle_ctrl.player_unit.pos)
		elif "camera_pos" in battle_ctrl:
			center = battle_ctrl.camera_pos
		
		var start_x = clamp(center.x - dims.x / 2.0, 0, max(0, battle_ctrl.MAP_W - dims.x))
		var start_y = clamp(center.y - dims.y / 2.0, 0, max(0, battle_ctrl.MAP_H - dims.y))
		return Vector2i(int(start_x + grid_pos.x), int(start_y + grid_pos.y))
	elif state == "dungeon":
		var start_x = clamp(dungeon_ctrl.player_pos.x - dims.x / 2.0, 0, max(0, dungeon_ctrl.width - dims.x))
		var start_y = clamp(dungeon_ctrl.player_pos.y - dims.y / 2.0, 0, max(0, dungeon_ctrl.height - dims.y))
		return Vector2i(int(start_x + grid_pos.x), int(start_y + grid_pos.y))
	elif state == "city":
		var start_x = clamp(city_ctrl.player_pos.x - dims.x / 2.0, 0, max(0, city_ctrl.width - dims.x))
		var start_y = clamp(city_ctrl.player_pos.y - dims.y / 2.0, 0, max(0, city_ctrl.height - dims.y))
		return Vector2i(int(start_x + grid_pos.x), int(start_y + grid_pos.y))
	return grid_pos

func _on_world_gen_updated(stage: String):
	loading_stage = stage
	_on_map_updated()
	if stage == "FINISHING...": 
		state = "world_preview"
		_on_map_updated()

func setup_classic_ui():
	var style = StyleBoxFlat.new()
	style.bg_color = Color.BLACK
	style.border_width_left = 2
	style.border_width_top = 2
	style.border_width_right = 2
	style.border_width_bottom = 2
	style.border_color = Color.GRAY
	style.content_margin_left = 10
	style.content_margin_top = 10
	style.content_margin_right = 10
	style.content_margin_bottom = 10
	
	# Add a border-only style for separators
	
	$MainLayout/ContentLayout/MapPanel.add_theme_stylebox_override("panel", style)
	$MainLayout/ContentLayout/MapPanel.clip_contents = true
	$MainLayout/ContentLayout/SidePanel.mouse_filter = Control.MOUSE_FILTER_IGNORE # Purely visual
	$MainLayout/LogPanel.add_theme_stylebox_override("panel", style)
	
	# Apply spacing to the Layout container
	$MainLayout/ContentLayout.add_theme_constant_override("separation", 0)

func setup_tile_map():
	tile_map = TileMap.new()
	tile_map.name = "BackgroundGrid"
	$MainLayout/ContentLayout/MapPanel.add_child(tile_map)
	$MainLayout/ContentLayout/MapPanel.move_child(tile_map, 0)
	
	var ts = TileSet.new()
	ts.tile_size = Vector2i(TILE_SIZE, TILE_SIZE)
	
	# LAYER 0: White Solid Tile for Backgrounds
	var img = Image.create(TILE_SIZE, TILE_SIZE, false, Image.FORMAT_RGBA8)
	img.fill(Color.WHITE)
	var tex = ImageTexture.create_from_image(img)
	var source_bg = TileSetAtlasSource.new()
	source_bg.texture = tex
	source_bg.texture_region_size = Vector2i(TILE_SIZE, TILE_SIZE)
	source_bg.create_tile(Vector2i(0, 0))
	ts.add_source(source_bg, 0)
	
	# LAYER 1: Character Atlas (Generated at runtime)
	# We use a Label to bake characters into a texture
	var char_tex_size = TILE_SIZE
	
	# Instead of complex baking, we use Font.draw_char if possible, 
	# but for maximum compatibility with the Pure TileMap request, 
	# we'll build a character source.
	# We'll use a secondary atlas source for characters.
	# I will bake the characters into an image using a hidden Label/SubViewport
	var bake_vp = SubViewport.new()
	bake_vp.size = Vector2i(char_tex_size * 16, char_tex_size * 16)
	bake_vp.transparent_bg = true
	bake_vp.render_target_update_mode = SubViewport.UPDATE_ONCE
	add_child(bake_vp)
	
	var bake_node = Control.new()
	bake_node.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
	bake_vp.add_child(bake_node)
	
	# Draw characters 0-255 in a 16x16 grid
	for i in range(256):
		var l = Label.new()
		l.text = char(i)
		l.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
		l.vertical_alignment = VERTICAL_ALIGNMENT_CENTER
		l.autowrap_mode = TextServer.AUTOWRAP_OFF
		l.add_theme_font_override("font", mono_font)
		l.add_theme_font_size_override("font_size", TILE_SIZE - 2) # Leave tiny bleed margin
		l.add_theme_stylebox_override("normal", StyleBoxEmpty.new())
		l.custom_minimum_size = Vector2(TILE_SIZE, TILE_SIZE)
		l.position = Vector2((i % 16) * TILE_SIZE, (i / 16) * TILE_SIZE)
		l.size = Vector2(TILE_SIZE, TILE_SIZE)
		bake_node.add_child(l)
	
	# Wait for a frame to bake
	await get_tree().process_frame
	await get_tree().process_frame
	
	var baked_tex = ImageTexture.create_from_image(bake_vp.get_texture().get_image())
	var source_chars = TileSetAtlasSource.new()
	source_chars.texture = baked_tex
	source_chars.texture_region_size = Vector2i(TILE_SIZE, TILE_SIZE)
	for i in range(256):
		source_chars.create_tile(Vector2i(i % 16, i / 16))
	ts.add_source(source_chars, 1)
	
	tile_map.tile_set = ts
	tile_map.add_layer(1) # Character layer
	tile_map.add_layer(2) # Selection/UI layer
	tile_map.visible = false
	
	# Cleanup bake system
	bake_vp.queue_free()

var color_tile_cache = {}
var char_tile_cache = {}

func get_tile_for_color(color: Color) -> int:
	var c_key = color.to_html()
	if color_tile_cache.has(c_key):
		return color_tile_cache[c_key]
	
	var source = tile_map.tile_set.get_source(0) as TileSetAtlasSource
	var alt_id = source.create_alternative_tile(Vector2i(0, 0))
	var tile_data = source.get_tile_data(Vector2i(0, 0), alt_id)
	if tile_data:
		tile_data.modulate = color
	
	color_tile_cache[c_key] = alt_id
	return alt_id

func get_tile_for_char_color(atlas_pos: Vector2i, color: Color) -> int:
	var key = str(atlas_pos) + "_" + color.to_html()
	if char_tile_cache.has(key):
		return char_tile_cache[key]
	
	var source = tile_map.tile_set.get_source(1) as TileSetAtlasSource
	var alt_id = source.create_alternative_tile(atlas_pos)
	var tile_data = source.get_tile_data(atlas_pos, alt_id)
	if tile_data:
		tile_data.modulate = color
	
	char_tile_cache[key] = alt_id
	return alt_id

func _input(event):
	if event is InputEventKey and event.pressed:
		if event.keycode == KEY_F12:
			GameState.run_turbo_simulation()
			return
		if event.keycode == KEY_F11:
			GameState.run_annual_simulation()
			return
		if event.keycode == KEY_G:
			graphical_mode = !graphical_mode
			GameState.add_log("Toggled Graphical Mode: %s" % ("ON" if graphical_mode else "OFF"))
			_on_map_updated()
			return
		
		# ZOOM SYSTEM (Font Scaling)
		if event.keycode == KEY_EQUAL or event.keycode == KEY_KP_ADD: # Usually + is on Equal key
			current_font_size = min(current_font_size + 2, 48)
			_on_map_updated()
			return
		if event.keycode == KEY_MINUS or event.keycode == KEY_KP_SUBTRACT:
			current_font_size = max(current_font_size - 2, 4)
			_on_map_updated()
			return

	if event.is_action_pressed("ui_cancel"): # ESC
		if state in ["world_creation", "character_creation", "battle_config", "codex", "management", "party_info", "fief_info", "history", "dialogue"]:
			state = "overworld"
			_on_map_updated()
		party_panel.visible = false
		fief_panel.visible = false
		return

	elif state == "menu":
		handle_menu_input(event)
	
	elif state == "battle_config":
		handle_battle_config_input(event)
		
	elif state == "world_creation":
		handle_world_creation_input(event)
		
	elif state == "world_preview":
		handle_world_preview_input(event)
		
	elif state == "character_creation":
		handle_character_creation_input(event)
		
	elif state == "location_select":
		handle_location_select_input(event)
		
	elif state == "city_studio":
		handle_city_studio_input(event)
		
	elif state == "overworld":
		overworld_ctrl.handle_input(event)
		if event is InputEventKey and event.pressed and event.keycode == KEY_K:
			_try_open_codex_contextual()
		# Update info label based on cursor/player
		_sync_tile_info()
	elif state == "region":
		if region_ctrl:
			region_ctrl.handle_input(event)
			if not region_ctrl.active and state == "region":
				if saved_character and generated_world:
					state = "overworld"
				else:
					state = "world_preview"
					preview_pos = GameState.player.pos
				_on_map_updated()
	elif state == "world_map":
		if event.is_action_pressed("ui_cancel") or (event is InputEventKey and event.pressed and event.keycode == KEY_TAB):
			state = "overworld"
			_on_map_updated()
		
	elif state == "history":
		if event is InputEventKey and event.pressed:
			match event.keycode:
				KEY_ESCAPE, KEY_H:
					state = "overworld"
					_on_map_updated()
				KEY_PAGEDOWN:
					history_offset += 25
					_on_map_updated()
				KEY_PAGEUP:
					history_offset = max(0, history_offset - 25)
					_on_map_updated()

	elif state == "battle":
		if battle_ctrl: battle_ctrl.handle_input(event)
		
	elif state == "dungeon":
		if dungeon_ctrl: dungeon_ctrl.handle_input(event)
		
	elif state == "city":
		if city_ctrl:
			city_ctrl.handle_input(event)
			if event.is_action_pressed("ui_cancel"):
				state = "menu"
				_on_map_updated()
				return
				
			if not city_ctrl.active:
				state = "menu"
				_on_map_updated()
		else:
			state = "overworld"
			_on_map_updated()
		
	elif state == "management":
		handle_management_input(event)
	
	elif state == "codex":
		handle_codex_input(event)
		
	elif state == "dialogue":
		if event.is_action_pressed("ui_up"):
			dialogue_idx = posmod(dialogue_idx - 1, dialogue_options.size())
			_on_map_updated()
		elif event.is_action_pressed("ui_down"):
			dialogue_idx = posmod(dialogue_idx + 1, dialogue_options.size())
			_on_map_updated()
		elif event.is_action_pressed("ui_accept"):
			handle_dialogue_choice(dialogue_options[dialogue_idx])
		elif event.is_action_pressed("ui_cancel"):
			state = "overworld"
			_on_map_updated()

func _try_open_codex_contextual():
	var search_pos = last_hover_world_pos
	if search_pos == Vector2i(-1, -1):
		search_pos = GameState.player.pos
	
	var target_entry = ""
	
	# 1. Check for settlements
	if search_pos in GameState.settlements:
		target_entry = "Settlement:" + GameState.settlements[search_pos].name
	
	# 2. Check for armies/lords (Search near pos)
	if target_entry == "":
		for a in GameState.armies:
			if a.pos.distance_to(search_pos) < 2 and a.lord_id != "":
				target_entry = "NPC:" + a.lord_id
				break
				
	# 3. Check for specific tile types
	if target_entry == "":
		var t = GameState.grid[search_pos.y][search_pos.x]
		if t == '^': target_entry = "Terrain"
		# etc..
	
	if target_entry != "":
		var indices = CodexData.find_entry_indices(target_entry, GameState)
		if indices.x != -1:
			codex_cat_idx = indices.x
			codex_entry_idx = indices.y
			codex_focus = 1 # Focus on entries
			state = "codex"
			_on_map_updated()
			return

	# Fallback to default
	state = "codex"
	codex_cat_idx = 0
	codex_entry_idx = 0
	codex_focus = 0
	_on_map_updated()

func handle_codex_input(event):
	if event.is_action_pressed("ui_cancel"):
		state = "overworld"
		_on_map_updated()
		return

	# Support Q/E for rapid tab cycling always
	var categories = CodexData.get_categories()
	if event is InputEventKey and event.pressed:
		if event.keycode == KEY_Q:
			codex_cat_idx = posmod(codex_cat_idx - 1, categories.size())
			codex_entry_idx = 0
			_on_map_updated()
			return
		elif event.keycode == KEY_E:
			codex_cat_idx = posmod(codex_cat_idx + 1, categories.size())
			codex_entry_idx = 0
			_on_map_updated()
			return

	if event.is_action_pressed("ui_focus_next") or (event is InputEventKey and event.pressed and event.keycode == KEY_TAB):
		codex_focus = 1 - codex_focus
		get_viewport().set_input_as_handled() # Prevent Godot focus stealing
		_on_map_updated()
		return

	var cat_name = categories[codex_cat_idx]
	var entries = CodexData.get_entries_for(cat_name, GameState)

	if codex_focus == 0: # Header focus
		if event.is_action_pressed("ui_left") or (event is InputEventKey and event.pressed and event.keycode == KEY_A):
			codex_cat_idx = posmod(codex_cat_idx - 1, categories.size())
			codex_entry_idx = 0
			_on_map_updated()
		elif event.is_action_pressed("ui_right") or (event is InputEventKey and event.pressed and event.keycode == KEY_D):
			codex_cat_idx = posmod(codex_cat_idx + 1, categories.size())
			codex_entry_idx = 0
			_on_map_updated()
		elif event.is_action_pressed("ui_down") or (event is InputEventKey and event.pressed and event.keycode == KEY_S):
			codex_focus = 1 # Jump to list
			_on_map_updated()
	else: # Entry focus
		if entries.size() > 0:
			if event.is_action_pressed("ui_up") or (event is InputEventKey and event.pressed and event.keycode == KEY_W):
				if codex_entry_idx == 0:
					codex_focus = 0 # Jump back to header
				else:
					codex_entry_idx = posmod(codex_entry_idx - 1, entries.size())
				_on_map_updated()
			elif event.is_action_pressed("ui_down") or (event is InputEventKey and event.pressed and event.keycode == KEY_S):
				codex_entry_idx = posmod(codex_entry_idx + 1, entries.size())
				_on_map_updated()
			elif event.is_action_pressed("ui_left") or (event is InputEventKey and event.pressed and event.keycode == KEY_A):
				codex_cat_idx = posmod(codex_cat_idx - 1, categories.size())
				codex_entry_idx = 0
				_on_map_updated()
			elif event.is_action_pressed("ui_right") or (event is InputEventKey and event.pressed and event.keycode == KEY_D):
				codex_cat_idx = posmod(codex_cat_idx + 1, categories.size())
				codex_entry_idx = 0
				_on_map_updated()

func _start_tournament_round():
	var s = GameState.settlements.get(GameState.player.pos)
	var r = GameState.player.tournament_round
	var idx = r - 1
	
	if idx < s.tournament_participants.size():
		var opp_id = s.tournament_participants[idx]
		var opp = GameState.find_npc(opp_id)
		if opp:
			GameState.add_log("[color=yellow]TOURNAMENT:[/color] Round %d vs %s!" % [r, opp.name])
			state = "battle"
			battle_ctrl.start(opp, true, s.tournament_prize_pool)
		else:
			GameState.player.tournament_round += 1
			_start_tournament_round()
	else:
		GameState.add_log("No more opponents left! You are the champion!")
		dialogue_options = ["Claim Prize"]
		dialogue_idx = 0
		_on_map_updated()

func handle_dialogue_choice(choice):
	if dialogue_target is Dictionary and dialogue_target.get("type") == "animal":
		if choice == "Hunt (Kill)":
			var data = dialogue_target
			var killed_list = GameState.killed_fauna.get(data.world_pos, [])
			killed_list.append(data.local_pos)
			GameState.killed_fauna[data.world_pos] = killed_list
			
			var loot_str = ""
			for item in data.loot:
				var amt = data.loot[item]
				GameState.player.inventory[item] = GameState.player.inventory.get(item, 0) + amt
				loot_str += "%d %s, " % [amt, item]
			
			GameState.add_log("HUNT: You killed the %s. Collected: %s" % [data.name, loot_str.trim_suffix(", ")])
			state = "overworld"
			_on_map_updated()
			return
		
		if choice == "Attempt Capture":
			var data = dialogue_target
			var chance = 30
			if GameState.rng.randf() * 100 < chance:
				var killed_list = GameState.killed_fauna.get(data.world_pos, [])
				killed_list.append(data.local_pos)
				GameState.killed_fauna[data.world_pos] = killed_list
				
				if data.get("capturable", false):
					GameState.add_log("CAPTURE: Success! You captured the %s." % data.name)
					var key = "captured_" + data.name.to_lower().replace(" ", "_")
					GameState.player.inventory[key] = GameState.player.inventory.get(key, 0) + 1
				else:
					GameState.add_log("CAPTURE: Success! You have captured a live %s." % data.name)
					GameState.player.inventory["live_animals"] = GameState.player.inventory.get("live_animals", 0) + 1
			else:
				GameState.add_log("CAPTURE: The %s evaded your grasp!" % data.name)
			
			state = "overworld"
			_on_map_updated()
			return
			
		if choice == "Leave":
			state = "overworld"
			_on_map_updated()
			return

	if choice.begins_with("Sign Contract"):
		var f_data = GameState.get_faction(dialogue_target.faction)
		var wage = 50 + (GameState.player.strength / 10)
		GameState.player.active_contract = {
			"faction_id": dialogue_target.faction,
			"daily_wage": wage,
			"expires_day": GameState.day + 14
		}
		GameState.add_log("Contract Signed: You are now a mercenary for %s." % f_data.name)
		state = "overworld"
		_on_map_updated()
		return

	if choice == "Swear Fealty (Accept Fief)":
		var f_id = dialogue_target.faction
		var f_data = GameState.get_faction(f_id)
		GameState.player.faction = f_id
		GameState.player.active_contract = {}
		
		var granted_s = null
		var lowest_tier = 99
		for pos in GameState.settlements:
			var s = GameState.settlements[pos]
			if s.faction == f_id and not s.is_capital:
				if s.tier < lowest_tier:
					lowest_tier = s.tier
					granted_s = s
		
		if granted_s:
			granted_s.lord_id = GameState.player.id
			GameState.player.fief_ids.append(granted_s.pos)
			GameState.add_log("FEALTY: You have accepted the fief of %s and sworn an oath to %s!" % [granted_s.name, f_data.name])
		else:
			GameState.add_log("FEALTY: You have sworn fealty to %s!" % f_data.name)
		
		state = "overworld"
		_on_map_updated()
		return

	if choice == "Swear Fealty (Royal Charter)":
		var f_id = dialogue_target.faction
		var f_data = GameState.get_faction(f_id)
		GameState.player.faction = f_id
		GameState.player.active_contract = {}
		
		GameState.player.charters += 1
		GameState.add_log("FEALTY: You have accepted a Royal Charter and sworn an oath to %s!" % f_data.name)
		
		state = "overworld"
		_on_map_updated()
		return

	if choice == "COMMAND: Follow Me":
		dialogue_target.set_meta("player_command", "follow")
		GameState.add_log("The lord bows. 'We follow your lead, Commander.'")
		state = "overworld"
		_on_map_updated()
		return

	if choice == "COMMAND: Garrison Nearest":
		var best_dist = 9999
		var best_s = null
		for s_pos in GameState.settlements:
			var s = GameState.settlements[s_pos]
			if s.faction == dialogue_target.faction:
				var d = dialogue_target.pos.distance_to(s_pos)
				if d < best_dist:
					best_dist = d
					best_s = s_pos
		
		if best_s:
			dialogue_target.set_meta("player_command", "defend")
			dialogue_target.set_meta("command_target", best_s)
			GameState.add_log("The lord nods. 'We will secure the walls of %s.'" % GameState.settlements[best_s].name)
		state = "overworld"
		_on_map_updated()
		return

	if choice == "COMMAND: Resume duties":
		dialogue_target.set_meta("player_command", "")
		GameState.add_log("'As you command. We return to our previous patrol.'")
		state = "overworld"
		_on_map_updated()
		return

	if choice.begins_with("Request Charter"):
		if GameState.player.crowns >= 2500:
			GameState.player.crowns -= 2500
			GameState.player.charters += 1
			GameState.add_log("CHARTER: You have purchased a Royal Charter for 2500 Crowns. You may now found another settlement.")
		else:
			GameState.add_log("The King looks at your empty purse with disapproval.")
		state = "overworld"
		_on_map_updated()
		return

	if choice == "Enter Tournament":
		var s = GameState.settlements.get(GameState.player.pos)
		GameState.add_log("Tournament Master: 'Welcome to the Lists! The entry fee is 50 Crowns. The prize is %d Crowns. Are you ready?'" % s.tournament_prize_pool)
		dialogue_options = ["Pay Entrance Fee (50cr)", "Leave"]
		dialogue_idx = 0
		_on_map_updated()
		return
		
	if choice == "Pay Entrance Fee (50cr)":
		if GameState.player.crowns >= 50:
			GameState.player.crowns -= 50
			_start_tournament_round()
		else:
			GameState.add_log("Master: 'No coin, no glory. Come back when you're solvent.'")
			state = "overworld"
			_on_map_updated()
		return

	if choice == "Next Round":
		_start_tournament_round()
		return

	if choice == "Claim Prize":
		var p_pos = GameState.player.pos
		var s = GameState.settlements.get(p_pos)
		var p = s.tournament_prize_pool
		GameState.player.crowns += p
		GameState.player.fame += 100
		s.tournament_active = false
		GameState.add_log("[color=yellow]CHAMPION![/color] You have won the tournament and %d Crowns!" % p)
		state = "overworld"
		_on_map_updated()
		return

	if choice == "Join Attacker":
		var b = dialogue_target
		state = "battle"
		battle_ctrl.start(b.defender, false, 0, b.attacker)
		_on_map_updated()
		return
		
	if choice == "Join Defender":
		var b = dialogue_target
		state = "battle"
		battle_ctrl.start(b.attacker, false, 0, b.defender)
		_on_map_updated()
		return

	match choice:
		"Talk":
			var l_id = dialogue_target.lord_id if "lord_id" in dialogue_target else ""
			var npc = GameState.find_npc(l_id)
			if npc:
				GameState.add_log("%s: 'Greetings, traveler. What brings you to these lands?'" % npc.name)
			else:
				GameState.add_log("The party lead looks at you with caution.")
			_on_map_updated()
		"Ask for Work":
			var l_id = dialogue_target.lord_id if "lord_id" in dialogue_target else ""
			var npc = GameState.find_npc(l_id)
			var name_str = npc.name if npc else "The Lord"
			
			if GameState.player.active_contract.has("faction_id"):
				GameState.add_log("%s: 'You are already in service. Honor your current contract first.'" % name_str)
				_on_map_updated()
			else:
				var wage = 50 + (GameState.player.strength / 10)
				dialogue_options = ["Sign Contract (%d cr/day)" % wage, "Leave"]
				dialogue_idx = 0
				GameState.add_log("%s: 'I have need of blades. I can offer %d Crowns a day for 14 days. Interested?'" % [name_str, wage])
				_on_map_updated()
		"Attack":
			if dialogue_target.faction != "" and dialogue_target.faction != "bandits":
				if GameState.get_relation("player", dialogue_target.faction) != "war":
					GameState.set_relation("player", dialogue_target.faction, "war")
					GameState.add_log("You attacked a %s party! They are now at WAR with you." % dialogue_target.faction.capitalize())
			
			# Trigger battle via GameState signal
			GameState.emit_signal("battle_started", dialogue_target)
		"Leave":
			state = "overworld"
			_on_map_updated()
		"Trade":
			# Future: Open trade menu
			GameState.add_log("Trade not yet implemented.")
			state = "overworld"
			_on_map_updated()
		"Demand Toll":
			# Future: Intimidate for gold
			GameState.add_log("Intimidation not yet implemented.")
			state = "overworld"
			_on_map_updated()

func toggle_management_ui():
	state = "management"
	mgmt_tab = "CHARACTER"
	mgmt_focus = 0
	mgmt_idx_l = 0
	mgmt_idx_r = 0
	_on_map_updated()

func handle_menu_input(event):
	if event.is_action_pressed("ui_up") or (event is InputEventKey and event.keycode == KEY_W):
		menu_idx = posmod(menu_idx - 1, menu_options.size())
		_on_map_updated()
	elif event.is_action_pressed("ui_down") or (event is InputEventKey and event.keycode == KEY_S):
		menu_idx = posmod(menu_idx + 1, menu_options.size())
		_on_map_updated()
	elif event.is_action_pressed("ui_accept"):
		match menu_idx:
			0: # NEW WORLD GEN
				state = "world_creation"
				world_config_idx = 0
				world_config["seed"] = randi()
				_on_map_updated()
			1: # CHARACTER CREATOR
				state = "character_creation"
				player_config_idx = 0
				cc_tab = 0 # Start at Background
				_ensure_valid_cc_material()
				_on_map_updated()
			2: # START ADVENTURE
				# In a real game, this might open "play_select"
				# For now, we'll start if world and char exist
				if generated_world and saved_character:
					_start_adventure()
				elif not generated_world:
					GameState.add_log("No world generated yet!")
					_on_map_updated()
				elif not saved_character:
					GameState.add_log("No character created yet!")
					state = "character_creation"
					_on_map_updated()
			3: # BATTLE SIMULATOR
				state = "battle_config"
				sim_config_idx = 0
				_on_map_updated()
			4: # CITY GENERATOR
				state = "city_studio"
				GameState.city_studio_idx = 0
				_on_map_updated()
			5: # THE GREAT ARCHIVE
				state = "codex"
				codex_cat_idx = 0
				codex_entry_idx = 0
				codex_focus = 0
				_on_map_updated()
			6: # TOGGLE RENDERING
				if GameState.render_mode == "ascii":
					GameState.render_mode = "grid"
					menu_options[6] = "RENDERING: SOLID GRID"
					GameState.add_log("Rendering Mode: SOLID GRID")
				else:
					GameState.render_mode = "ascii"
					menu_options[6] = "RENDERING: CLASSIC ASCII"
					GameState.add_log("Rendering Mode: CLASSIC ASCII")
				_update_all_ui_themes()
				_on_map_updated()

func _test_city_generator():
	# 1. Ensure we have a dummy world state if none exists
	if not GameState.player:
		GameState.player = GDPlayer.new()
		GameState.player.pos = Vector2i(100, 100)
		
		var cmd = GDUnit.new()
		cmd.name = "Architect"
		cmd.type = "hero"
		cmd.faction = "player"
		cmd.tier = 1
		GameState.player.commander = cmd
		GameState.player.roster = []
	
	# 2. Add a settlement to the dummy world at player pos if not exists
	var test_pos = Vector2i(100, 100)
	if not GameState.settlements.has(test_pos):
		var s = GDSettlement.new()
		s.name = "Procedural Metropolis"
		s.type = "city"
		s.pos = test_pos
		s.tier = 4
		s.population = 25000 # Large population will make a big radius
		s.faction = "Neutral"
		# Add upgrades to see changes in generation
		s.buildings["wall"] = 3    # Level 3 Stone walls (Heavy Wall symbol 'H')
		s.buildings["keep"] = 2    # Level 2 Citadel
		s.buildings["market"] = 1  # Markets enabled ('M' symbol)
		s.buildings["smithy"] = 1  # Smithies enabled ('S' symbol)
		GameState.settlements[test_pos] = s
	
	# 3. Ensure geology exists so generation doesn't crash
	if not GameState.geology.has(test_pos):
		GameState.geology[test_pos] = {"elevation": 0.5, "temp": 0.5, "rain": 0.5}
	
	# 4. Fill grid if empty to avoid bounds issues
	if GameState.grid.is_empty():
		GameState.width = 200
		GameState.height = 200
		for y in range(200):
			var row = []
			for x in range(200):
				row.append(".")
			GameState.grid.append(row)
	
	# 5. Set local offset to center of the tile (500m, 500m)
	GameState.player.pos = test_pos
	GameState.local_offset = Vector2(500.0, 500.0)
	
	# 6. Start Battle Controller in local mode
	# We can pass null as enemy to just wander
	state = "battle"
	battle_ctrl.start(null) 
	_on_map_updated()

func handle_battle_config_input(event):
	if event is InputEventMouseMotion: return
	if not event.is_pressed(): return
	
	# Lineup indexing: [P0, P1, P2, P3, E0, E1, E2, E3, START]
	# Each lineup entry has: Type (0), Count (1), Level (2)
	var max_entries = 4
	var total_fields = (max_entries * 2) + 1 # 4 Player, 4 Enemy, 1 Start
	
	if event.is_action_pressed("ui_up") or (event is InputEventKey and event.keycode == KEY_W):
		sim_config_idx = posmod(sim_config_idx - 1, total_fields)
		_on_map_updated()
	elif event.is_action_pressed("ui_down") or (event is InputEventKey and event.keycode == KEY_S):
		sim_config_idx = posmod(sim_config_idx + 1, total_fields)
		_on_map_updated()
	
	if sim_config_idx < (max_entries * 2):
		var is_player = sim_config_idx < max_entries
		var list = sim_config["p_lineup"] if is_player else sim_config["e_lineup"]
		var local_idx = sim_config_idx % max_entries
		
		# Ensure entry exists
		while list.size() <= local_idx:
			list.append({"type": "none", "cnt": 0, "lvl": 3})
		
		var entry = list[local_idx]
		var arch_keys = ["none"] + GameData.ARCHETYPES.keys()
		
		# Sub-slot selection (1, 2, 3 for Type, Count, Level)
		if event is InputEventKey and event.keycode == KEY_1: # Change Type
			var cur_idx = arch_keys.find(entry["type"])
			entry["type"] = arch_keys[posmod(cur_idx + 1, arch_keys.size())]
			if entry["type"] != "none" and entry["cnt"] == 0: entry["cnt"] = 10
			_on_map_updated()
		elif event.is_action_pressed("ui_left") or (event is InputEventKey and event.keycode == KEY_A):
			entry["cnt"] = max(0, entry["cnt"] - 5)
			_on_map_updated()
		elif event.is_action_pressed("ui_right") or (event is InputEventKey and event.keycode == KEY_D):
			entry["cnt"] += 5
			_on_map_updated()
		elif event is InputEventKey and event.keycode == KEY_Q:
			entry["lvl"] = max(1, entry["lvl"] - 1)
			_on_map_updated()
		elif event is InputEventKey and event.keycode == KEY_E:
			entry["lvl"] = min(5, entry["lvl"] + 1)
			_on_map_updated()

	if event.is_action_pressed("ui_accept") and sim_config_idx == (max_entries * 2):
		setup_battle_simulator()
	elif event.is_action_pressed("ui_cancel"):
		state = "menu"
		_on_map_updated()

func handle_world_creation_input(event):
	var keys = world_config.keys()
	if event.is_action_pressed("ui_up") or (event is InputEventKey and event.keycode == KEY_W):
		world_config_idx = posmod(world_config_idx - 1, keys.size() + 1)
		_on_map_updated()
	elif event.is_action_pressed("ui_down") or (event is InputEventKey and event.keycode == KEY_S):
		world_config_idx = posmod(world_config_idx + 1, keys.size() + 1)
		_on_map_updated()
	
	elif event.is_action_pressed("ui_left") or (event is InputEventKey and event.keycode == KEY_A):
		_adjust_world_config(-1)
		_on_map_updated()
	elif event.is_action_pressed("ui_right") or (event is InputEventKey and event.keycode == KEY_D):
		_adjust_world_config(1)
		_on_map_updated()
	
	elif event.is_action_pressed("ui_accept"):
		if world_config_idx == keys.size(): # START GENERATION
			state = "loading"
			GameState.init_world(world_config)
			_on_map_updated()
		elif keys[world_config_idx] == "seed":
			world_config["seed"] = randi()
			_on_map_updated()

func handle_world_preview_input(event):
	if event is InputEventMouseButton and event.pressed:
		if event.button_index == MOUSE_BUTTON_WHEEL_UP:
			preview_zoom = clamp(preview_zoom + 0.1, 0.5, 4.0)
			_on_map_updated()
		elif event.button_index == MOUSE_BUTTON_WHEEL_DOWN:
			preview_zoom = clamp(preview_zoom - 0.1, 0.5, 4.0)
			_on_map_updated()
		return

	if not event is InputEventKey or not event.pressed: return
	
	var move_speed = 5
	if preview_zoom > 2.0: move_speed = 2
	
	match event.keycode:
		KEY_W, KEY_UP:
			preview_pos.y -= move_speed
			_on_map_updated()
		KEY_S, KEY_DOWN:
			preview_pos.y += move_speed
			_on_map_updated()
		KEY_A, KEY_LEFT:
			preview_pos.x -= move_speed
			_on_map_updated()
		KEY_D, KEY_RIGHT:
			preview_pos.x += move_speed
			_on_map_updated()
		
		# MAP FILTERS
		KEY_1:
			GameState.map_mode = "terrain"
			_on_map_updated()
		KEY_2:
			GameState.map_mode = "political"
			_on_map_updated()
		KEY_3:
			GameState.map_mode = "province"
			_on_map_updated()
		KEY_4:
			GameState.map_mode = "resource"
			_on_map_updated()

		KEY_T:
			state = "region"
			region_ctrl.activate(preview_pos)
			_on_map_updated()

		KEY_ENTER: # ACCEPT
			generated_world = world_config.duplicate()
			state = "menu"
			GameState.add_log("World '%s' accepted!" % world_config["name"])
			_on_map_updated()
		KEY_R: # REROLL
			world_config["seed"] = randi()
			GameState.init_world(world_config)
			_on_map_updated()
		KEY_ESCAPE: # BACK TO SETTINGS
			state = "world_creation"
			_on_map_updated()
	
	preview_pos.x = clamp(preview_pos.x, 0, GameState.width)
	preview_pos.y = clamp(preview_pos.y, 0, GameState.height)

func handle_city_studio_input(event):
	var config = GameState.city_studio_config
	var keys = config.keys()
	if event.is_action_pressed("ui_up") or (event is InputEventKey and event.keycode == KEY_W):
		GameState.city_studio_idx = posmod(GameState.city_studio_idx - 1, keys.size() + 1)
		_on_map_updated()
	elif event.is_action_pressed("ui_down") or (event is InputEventKey and event.keycode == KEY_S):
		GameState.city_studio_idx = posmod(GameState.city_studio_idx + 1, keys.size() + 1)
		_on_map_updated()
	elif event.is_action_pressed("ui_left") or (event is InputEventKey and event.keycode == KEY_A):
		_adjust_city_studio_config(-1)
		_on_map_updated()
	elif event.is_action_pressed("ui_right") or (event is InputEventKey and event.keycode == KEY_D):
		_adjust_city_studio_config(1)
		_on_map_updated()
	elif event.is_action_pressed("ui_accept"):
		if GameState.city_studio_idx == keys.size(): # GENERATE
			_build_studio_city()
		_on_map_updated()

func _adjust_city_studio_config(delta):
	var config = GameState.city_studio_config
	var keys = config.keys()
	if GameState.city_studio_idx >= keys.size(): return
	var key = keys[GameState.city_studio_idx]
	match key:
		"type":
			var types = ["hamlet", "village", "town", "city", "metropolis", "castle"]
			var idx = types.find(config[key])
			config[key] = types[posmod(idx + delta, types.size())]
			
			# Enforce rules on type change
			match config[key]:
				"hamlet": config["pop"] = min(config["pop"], 200)
				"village": config["pop"] = min(config["pop"], 500)
				"town": config["pop"] = min(config["pop"], 2000)
		"size":
			config[key] = clamp(config[key] + (delta * 50), 100, 500)
		"walls":
			var walls = ["none", "wood", "stone", "multi-layered"]
			var idx = walls.find(config[key])
			config[key] = walls[posmod(idx + delta, walls.size())]
		"rivers":
			config[key] = !config[key]
		"pop":
			var step = 100
			if config["type"] in ["city", "metropolis", "castle"]: step = 1000
			
			var max_pop = 100000
			match config["type"]:
				"hamlet": max_pop = 200
				"village": max_pop = 500
				"town": max_pop = 2000
				
			config[key] = clamp(config[key] + (delta * step), 50, max_pop)
		"seed":
			config[key] = config[key] + delta

func _build_studio_city():
	var config = GameState.city_studio_config
	
	# Override world seed for studio specifically to ensure design remains static across sessions
	var old_seed = GameState.world_seed
	GameState.world_seed = config["seed"]
	
	# 1. Setup minimal GameState for preview
	if not GameState.player:
		GameState.player = GDPlayer.new()
		GameState.player.pos = Vector2i(100, 100)
		
	var s = GDSettlement.new()
	s.name = "Custom Settlement"
	s.type = config["type"]
	s.pos = Vector2i(100, 100)
	s.population = config["pop"]
	s.radius = config["size"] # Use for spatial mapping
	s.river_acres = 500 if config["rivers"] else 0
	
	# Ensure GameState grid and geology exist for the studio preview
	if GameState.grid.is_empty():
		GameState.width = 200
		GameState.height = 200
		for y in range(GameState.height):
			var row = []
			for x in range(GameState.width):
				row.append(".") # Default to plains
			GameState.grid.append(row)
	
	if not GameState.geology.has(s.pos):
		GameState.geology[s.pos] = {"temp": 0.5, "rain": 0.5, "elevation": 0.3}

	match config["walls"]:
		"none": s.buildings["wall"] = 0
		"wood": s.buildings["wall"] = 1
		"stone": s.buildings["wall"] = 2
		"multi-layered": s.buildings["wall"] = 4
		
	GameState.settlements[s.pos] = s
	city_ctrl.activate(s, s.pos, GameState.world_seed)
	
	# Set player into region map near city
	region_ctrl.activate(s.pos)
	state = "region"
	
	# Restore seed if we were mid-game (unlikely but safe)
	GameState.world_seed = old_seed
	
	_on_map_updated()

func calculate_available_crowns() -> int:
	var sce = GameData.SCENARIOS.get(player_config["scenario"], {})
	var total = sce.get("gold", 0) # Base crowns from scenario
	
	for purchase in cc_purchases:
		var item_id = purchase["id"]
		var mat = purchase["mat"]
		var qual = purchase["qual"]
		
		var price = 50
		if GameData.ITEMS.has(item_id):
			var itm = GameData.ITEMS[item_id]
			# Base price based on stats
			price = int(itm.get("weight", 1.0) * 20 + itm.get("prot", 0) * 15 + itm.get("dmg", 0) * 20)
			
			# Material Multiplier (Crude estimate)
			var mat_mult = 1.0
			match mat:
				"cloth": mat_mult = 0.5
				"leather": mat_mult = 0.8
				"wood": mat_mult = 0.4
				"copper": mat_mult = 1.0
				"iron": mat_mult = 1.5
				"steel": mat_mult = 3.0
			
			# Quality Multiplier
			var qual_mult = 1.0
			match qual:
				"shoddy": qual_mult = 0.5
				"average": qual_mult = 1.0
				"well_made": qual_mult = 2.0
				"masterwork": qual_mult = 5.0
				
			price = int(price * mat_mult * qual_mult)
			price = max(10, price)
		total -= price
	return total

func _ensure_valid_cc_material():
	var item_key = cc_shop_items[player_config_idx]
	if not GameData.is_valid_material(item_key, CC_MATERIALS[cc_mat_idx]):
		# Try to find a valid one
		for i in range(CC_MATERIALS.size()):
			if GameData.is_valid_material(item_key, CC_MATERIALS[i]):
				cc_mat_idx = i
				return

func _cycle_cc_material(dir: int):
	var item_key = cc_shop_items[player_config_idx]
	var start_idx = cc_mat_idx
	while true:
		cc_mat_idx = posmod(cc_mat_idx + dir, CC_MATERIALS.size())
		if GameData.is_valid_material(item_key, CC_MATERIALS[cc_mat_idx]):
			break
		if cc_mat_idx == start_idx: # No valid material found?
			break

func handle_character_creation_input(event):
	var tab_keys = [
		["scenario", "profession"], # Background
		["strength", "agility", "endurance", "intelligence", "traits"], # Character
		["shop"], # Loadout
		["name"] # Summary
	]
	
	# Tab Navigation (Q/E or TAB/Shift+TAB)
	if event is InputEventKey and event.pressed:
		if event.keycode == KEY_E or event.keycode == KEY_TAB:
			cc_tab = posmod(cc_tab + 1, 4)
			player_config_idx = 0
			if cc_tab == 2: _ensure_valid_cc_material()
			_on_map_updated()
			return
		elif event.keycode == KEY_Q:
			cc_tab = posmod(cc_tab - 1, 4)
			player_config_idx = 0
			if cc_tab == 2: _ensure_valid_cc_material()
			_on_map_updated()
			return
		elif event.keycode == KEY_Z:
			cc_qual_idx = posmod(cc_qual_idx - 1, CC_QUALITIES.size())
			_on_map_updated()
			return
		elif event.keycode == KEY_X:
			cc_qual_idx = posmod(cc_qual_idx + 1, CC_QUALITIES.size())
			_on_map_updated()
			return
		elif event.keycode == KEY_BACKSPACE:
			if cc_tab == 2:
				var item_key = cc_shop_items[player_config_idx]
				# Remove the most recent purchase of this specific item ID
				for i in range(cc_purchases.size() - 1, -1, -1):
					if cc_purchases[i]["id"] == item_key:
						cc_purchases.remove_at(i)
						break
				_on_map_updated()
			return

	var current_keys = tab_keys[cc_tab]
	var max_idx = current_keys.size()
	if cc_tab == 3: max_idx += 1 # For the Finish button
	if cc_tab == 2: max_idx = cc_shop_items.size() # Shop index

	if event.is_action_pressed("ui_up") or (event is InputEventKey and event.keycode == KEY_W):
		player_config_idx = posmod(player_config_idx - 1, max_idx)
		if cc_tab == 2:
			_ensure_valid_cc_material()
		_on_map_updated()
	elif event.is_action_pressed("ui_down") or (event is InputEventKey and event.keycode == KEY_S):
		player_config_idx = posmod(player_config_idx + 1, max_idx)
		if cc_tab == 2:
			_ensure_valid_cc_material()
		_on_map_updated()
	
	elif event.is_action_pressed("ui_left") or (event is InputEventKey and event.keycode == KEY_A):
		if cc_tab == 2:
			_cycle_cc_material(-1)
		else:
			_adjust_player_config_tabbed(-1, current_keys)
		_on_map_updated()
	elif event.is_action_pressed("ui_right") or (event is InputEventKey and event.keycode == KEY_D):
		if cc_tab == 2:
			_cycle_cc_material(1)
		else:
			_adjust_player_config_tabbed(1, current_keys)
		_on_map_updated()
	
	elif event.is_action_pressed("ui_accept"):
		if cc_tab == 3 and player_config_idx == current_keys.size(): # FINISH
			_save_character_and_return()
		elif cc_tab == 1 and player_config_idx < current_keys.size() and current_keys[player_config_idx] == "traits":
			var t_id = GameData.TRAITS.keys()[trait_selection_idx]
			if t_id in player_config["traits"]:
				player_config["traits"].erase(t_id)
			else:
				player_config["traits"].append(t_id)
			_on_map_updated()
		elif cc_tab == 2: # Toggle Shopping
			var item_key = cc_shop_items[player_config_idx]
			var mat = CC_MATERIALS[cc_mat_idx]
			var qual = CC_QUALITIES[cc_qual_idx]
			
			# Check if already bought exact same spec
			var found_idx = -1
			for i in range(cc_purchases.size()):
				var p = cc_purchases[i]
				if p["id"] == item_key and p["mat"] == mat and p["qual"] == qual:
					found_idx = i
					break
			
			if found_idx != -1:
				cc_purchases.remove_at(found_idx)
			else:
				if calculate_available_crowns() > 10: 
					cc_purchases.append({"id": item_key, "mat": mat, "qual": qual})
			_on_map_updated()

func _adjust_player_config_tabbed(dir: int, keys: Array):
	if cc_tab == 2: return # Shop is handled by ui_accept toggles
	if player_config_idx >= keys.size(): return
	var key = keys[player_config_idx]
	
	match key:
		"scenario":
			var opts = GameData.SCENARIOS.keys()
			var cur = opts.find(player_config["scenario"])
			player_config["scenario"] = opts[posmod(cur + dir, opts.size())]
			# Reset purchases when scenario changes (since gold changes)
			cc_purchases.clear()
		"profession":
			var opts = GameData.PROFESSIONS.keys()
			var cur = opts.find(player_config["profession"])
			player_config["profession"] = opts[posmod(cur + dir, opts.size())]
		"traits":
			var opts = GameData.TRAITS.keys()
			trait_selection_idx = posmod(trait_selection_idx + dir, opts.size())
		"strength", "agility", "endurance", "intelligence":
			player_config[key] = clampi(player_config[key] + dir, 0, 20)
		"name":
			var names = ["Aris", "Bran", "Cid", "Dorn", "Elara", "Finn", "Gwen", "Hark", "Iora", "Jace"]
			var cur_idx = names.find(player_config["name"])
			if cur_idx == -1: cur_idx = 0
			player_config["name"] = names[posmod(cur_idx + dir, names.size())]

func calculate_creation_points() -> int:
	var total = 0
	var sce = GameData.SCENARIOS.get(player_config["scenario"], {})
	total += sce.get("points", 0)
	
	var prof = GameData.PROFESSIONS.get(player_config["profession"], {})
	total -= prof.get("cost", 0)
	
	for stat in ["strength", "agility", "endurance", "intelligence"]:
		# 8 is base. 1 point per level above, -1 per level below
		var val = player_config.get(stat, 8)
		total -= (val - 8)
		
	for t_id in player_config["traits"]:
		var t_data = GameData.TRAITS.get(t_id, {})
		total -= t_data.get("cost", 0)
		
	return total

func _save_character_and_return():
	saved_character = player_config.duplicate()
	saved_character["purchases"] = cc_purchases.duplicate()
	saved_character["final_crowns"] = calculate_available_crowns()
	state = "menu"
	GameState.add_log("Character '%s' saved!" % saved_character["name"])
	_on_map_updated()

func handle_location_select_input(event):
	if event.is_action_pressed("ui_left") or (event is InputEventKey and event.keycode == KEY_A):
		location_idx = posmod(location_idx - 1, location_list.size())
		_on_map_updated()
	elif event.is_action_pressed("ui_right") or (event is InputEventKey and event.keycode == KEY_D):
		location_idx = posmod(location_idx + 1, location_list.size())
		_on_map_updated()
	elif event.is_action_pressed("ui_accept"):
		_confirm_embark()
	elif event.is_action_pressed("ui_cancel"):
		state = "menu"
		_on_map_updated()

func _start_adventure():
	if not generated_world or not saved_character: return
	
	# Populate location list from settlements
	location_list = GameState.settlements.values()
	# Sort by population descending so capital/cities show up first
	location_list.sort_custom(func(a, b): return a.population > b.population)
	
	if location_list.is_empty():
		# Fallback if no settlements somehow (shouldn't happen with WorldGen we have)
		location_list = [{"pos": GameState.start_pos, "name": "Wilderness Embark", "type": "wilds", "faction": "None"}]
	
	location_idx = 0
	state = "location_select"
	_on_map_updated()

func _confirm_embark():
	var loc = location_list[location_idx]
	GameState.player.pos = loc.pos
	
	var sce = GameData.SCENARIOS[saved_character["scenario"]]
	var prof = GameData.PROFESSIONS[saved_character["profession"]]
	
	# Apply Basic Data
	GameState.player.commander.name = saved_character["name"]
	GameState.player.commander.attributes["strength"] = saved_character["strength"] + prof.stats.get("strength", 0)
	GameState.player.commander.attributes["agility"] = saved_character["agility"] + prof.stats.get("agility", 0)
	GameState.player.commander.attributes["endurance"] = saved_character["endurance"] + prof.stats.get("endurance", 0)
	GameState.player.commander.attributes["intelligence"] = saved_character["intelligence"] + prof.stats.get("intelligence", 0)
	
	GameState.player.crowns = saved_character.get("final_crowns", sce.get("gold", 0))
	GameState.player.fame = sce.get("fame", 0)
	
	# Reset relations
	var player_f = GameState.get_faction("player")
	if player_f:
		player_f.relations.clear()
	
	# ... relations logic ...
	for f_key in sce.get("relations", {}):
		var val = sce["relations"][f_key]
		if f_key.begins_with("faction_"):
			var idx = int(f_key.replace("faction_", ""))
			var real_factions = GameState.factions.filter(func(f): return f.id != "player")
			if idx < real_factions.size():
				var other_f = real_factions[idx]
				if player_f:
					player_f.relations[other_f.id] = val
				other_f.relations["player"] = val
	
	# Initial Items & Roster
	GameState.player.stash.clear()
	GameState.player.roster.clear() 
	
	# Spawn Scenario Roster
	for u_info in sce.get("start_roster", []):
		var u = null
		var type = u_info["type"]
		var tier = u_info.get("tier", 1)
		
		if type == "laborer":
			u = GameData.generate_laborer(GameState.rng)
		else:
			u = GameData.generate_recruit(GameState.rng, tier)
		
		if u:
			GameState.player.roster.append(u)
	
	# Scenario & Profession equipment (Defaults)
	var default_items = sce.get("items", []).duplicate()
	default_items.append_array(prof.get("equipment", []))
	for it_id in default_items:
		var itm = GameData.create_item_data(it_id, "iron", "standard")
		if itm: GameState.player.stash.append(itm)
		
	# Shop purchases (Custom spec)
	for p in saved_character.get("purchases", []):
		var itm = GameData.create_item_data(p["id"], p["mat"], p["qual"])
		if itm: GameState.player.stash.append(itm)
	
	# Initial Skills
	for s_name in prof.get("skills", {}):
		if s_name in GameState.player.commander.skills:
			GameState.player.commander.skills[s_name] = prof["skills"][s_name]

	# Traits
	GameState.player.commander.traits = saved_character["traits"].duplicate()
	
	state = "overworld"
	GameState.add_log("The adventure of %s (%s %s) begins in %s at %s!" % [saved_character["name"], prof.name, sce.name, generated_world["name"], loc.name])
	_on_map_updated()

func _adjust_world_config(dir: int):
	var keys = world_config.keys()
	if world_config_idx >= keys.size(): return
	
	var key = keys[world_config_idx]
	match key:
		"width", "height":
			world_config[key] = clampi(world_config[key] + (dir * 10), 40, 300)
		"num_plates":
			world_config[key] = clampi(world_config[key] + dir, 4, 30)
		"num_factions":
			world_config[key] = clampi(world_config[key] + dir, 2, 12)
		"layout":
			var layouts = ["Pangea", "Continents", "Archipelago"]
			var cur_idx = layouts.find(world_config[key])
			world_config[key] = layouts[posmod(cur_idx + dir, layouts.size())]
		"mineral_density":
			world_config[key] = clampi(world_config[key] + dir, 1, 10)
		"savagery":
			world_config[key] = clampi(world_config[key] + dir, 1, 10)
		"moisture", "temperature":
			world_config[key] = clampf(world_config[key] + (dir * 0.1), 0.1, 3.0)
		"seed":
			world_config[key] += dir

func setup_battle_simulator():
	# Ensure Player Object Exists for the sim
	if not GameState.player:
		GameState.player = GDPlayer.new()
		GameState.player.pos = Vector2i(50, 50)
		
	# Ensure Commander Exists
	if not GameState.player.commander:
		GameState.player.commander = GameData.generate_unit("mercenary_captain", 5)
		GameState.player.commander.name = "Sim Commander"

	# 1. Prepare Player Roster
	GameState.player.roster.clear()
	for entry in sim_config["p_lineup"]:
		if entry["type"] == "none" or entry["cnt"] <= 0: continue
		for i in range(entry["cnt"]):
			var u: GDUnit
			if entry["type"] in ["catapult", "ballista", "siege_engine", "battering_ram", "siege_tower"]:
				u = GameData.generate_unit("laborer", entry["lvl"])
				u.type = "siege_engine"
				u.engine_type = entry["type"]
				if entry["type"] == "siege_engine": u.engine_type = "catapult" # Fallback
			else:
				u = GameData.generate_unit(entry["type"], entry["lvl"])
			GameState.player.roster.append(u)

	# 2. Prepare Enemy
	var enemy = GDArmy.new(Vector2i.ZERO, "bandits")
	enemy.name = "Simulator Rivals"
	for entry in sim_config["e_lineup"]:
		if entry["type"] == "none" or entry["cnt"] <= 0: continue
		for i in range(entry["cnt"]):
			var u: GDUnit
			if entry["type"] in ["catapult", "ballista", "siege_engine", "battering_ram", "siege_tower"]:
				u = GameData.generate_unit("laborer", entry["lvl"])
				u.type = "siege_engine"
				u.engine_type = entry["type"]
				if entry["type"] == "siege_engine": u.engine_type = "catapult"
			else:
				u = GameData.generate_unit(entry["type"], entry["lvl"])
			enemy.roster.append(u)
		
	# Ensure GameState has some minimal grid data for the simulator if world not gen'd
	if GameState.grid.is_empty():
		GameState.width = 120
		GameState.height = 120
		for y in range(GameState.height):
			var row = []
			for x in range(GameState.width):
				row.append('.')
			GameState.grid.append(row)
		GameState.player.pos = Vector2i(50, 50)
		# Fake geology to prevent lookups failing
		for y in range(GameState.height):
			for x in range(GameState.width):
				GameState.geology[Vector2i(x,y)] = {"elevation": 0.5, "temp": 0.5, "rain": 0.5}

	state = "battle"
	# Reset battle controller state
	battle_ctrl.active = false
	battle_ctrl.units.clear()
	battle_ctrl.battalions.clear()
	
	battle_ctrl.start(enemy)
	_on_map_updated()

func setup_dungeon_simulator():
	# Ensure GameState has some minimal data
	if GameState.grid.is_empty():
		GameState.width = 120
		GameState.height = 120
		for y in range(GameState.height):
			var row = []
			for x in range(GameState.width):
				row.append('.')
			GameState.grid.append(row)
	
	var ruin = {
		"name": "Simulated Ruin",
		"type": "tomb",
		"depth": 3
	}
	state = "dungeon"
	GameState.active_ruin_pos = Vector2i(0, 0)
	GameState.emit_signal("dungeon_started", ruin)
	_on_map_updated()

func setup_city_simulator():
	state = "city"
	city_ctrl.generate_test_city()
	_on_map_updated()

func setup_test_siege():
	# 1. Generate City Grid
	city_ctrl.generate_test_city()
	
	# 2. Setup Mock Attacker (Player)
	var p = GameState.player
	p.roster = []
	# Give player some engines
	for i in range(2): p.roster.append({"type": "siege_engine", "engine_type": "battering_ram"})
	for i in range(2): p.roster.append({"type": "siege_engine", "engine_type": "siege_tower"})
	for i in range(30): p.roster.append({"type": "infantry", "archetype": "swordsman"})
	for i in range(20): p.roster.append({"type": "archer", "archetype": "bowman"})
	
	# 3. Setup Mock Defender (Enemy)
	var enemy = {
		"name": "Imperial Defenders",
		"type": "siege",
		"commander": {"name": "Baron Test", "type": "commander"},
		"roster": []
	}
	# Add defenders (Archers for walls, Infantry for keep)
	for i in range(40): enemy.roster.append({"type": "archer", "archetype": "bowman"})
	for i in range(30): enemy.roster.append({"type": "infantry", "archetype": "guardsman"})
	
	# 4. Trigger Siege
	state = "battle"
	var siege_data = {
		"grid": city_ctrl.grid,
		"wall_segments": city_ctrl.wall_segments,
		"gates": city_ctrl.gates,
		"towers": city_ctrl.towers,
		"keep_pos": city_ctrl.capture_points[0].pos if city_ctrl.capture_points.size() > 0 else Vector2i(130, 50)
	}
	battle_ctrl.start(enemy, false, 0, null, true, siege_data)
	_on_map_updated()

func toggle_party_info():
	if state == "party_info":
		state = "overworld"
	else:
		state = "party_info"
	_on_map_updated()

func toggle_fief_info():
	if state == "management" and mgmt_tab == "FIEF":
		state = "overworld"
	else:
		state = "management"
		mgmt_tab = "FIEF"
	_on_map_updated()

func toggle_history():
	if state == "history":
		state = "overworld"
	else:
		state = "history"
	history_offset = 0
	_on_map_updated()

func handle_management_input(event):
	if not event is InputEventKey or not event.pressed: return
	
	var gs = GameState
	var p = gs.player
	var s_pos = p.pos
	var s = gs.settlements.get(s_pos)
	
	if mgmt_is_designing:
		handle_designer_input(event)
		return

	match event.keycode:
		KEY_TAB: # Cycle Tabs
			if mgmt_tab == "CHARACTER": mgmt_tab = "TRAINING"
			elif mgmt_tab == "TRAINING": mgmt_tab = "ROSTER"
			elif mgmt_tab == "ROSTER": mgmt_tab = "MARKET"
			elif mgmt_tab == "MARKET": mgmt_tab = "RECRUIT"
			elif mgmt_tab == "RECRUIT": 
				if not GameState.player.fief_ids.is_empty(): mgmt_tab = "FIEF"
				else: mgmt_tab = "TRADE"
			elif mgmt_tab == "FIEF": mgmt_tab = "TRADE"
			elif mgmt_tab == "TRADE": mgmt_tab = "WORLD"
			elif mgmt_tab == "WORLD": mgmt_tab = "OFFICE"
			elif mgmt_tab == "OFFICE": mgmt_tab = "SQUARE"
			else: mgmt_tab = "CHARACTER"
			mgmt_focus = 0
			mgmt_idx_l = 0
			mgmt_idx_r = 0
		
		KEY_LEFT, KEY_A, KEY_KP_4: # Switch Focus Left
			mgmt_focus = 0
			_on_map_updated()
		
		KEY_RIGHT, KEY_D, KEY_KP_6: # Switch Focus Right
			mgmt_focus = 1
			_on_map_updated()
			
		KEY_W, KEY_UP, KEY_KP_8: # Navigate Up
			if mgmt_focus == 0: mgmt_idx_l = max(0, mgmt_idx_l - 1)
			else: mgmt_idx_r = max(0, mgmt_idx_r - 1)
			_on_map_updated()
			
		KEY_S, KEY_DOWN, KEY_KP_2: # Navigate Down
			var limit = 0
			if mgmt_focus == 0:
				if mgmt_tab == "CHARACTER": limit = 21 
				elif mgmt_tab == "TRAINING": limit = p.commander.attributes.size() + p.commander.skills.size()
				elif mgmt_tab == "ROSTER": limit = p.roster.size()
				elif mgmt_tab == "MARKET":
					var inv_keys = p.inventory.keys()
					var active_keys = []
					for k in inv_keys: 
						if p.inventory[k] > 0: active_keys.append(k)
					limit = active_keys.size()
				elif mgmt_tab == "RECRUIT": limit = s.recruit_pool.size() if s else 0
				elif mgmt_tab == "TRADE": limit = 10 
				elif mgmt_tab == "WORLD": limit = min(gs.world_market_orders.size(), 30)
				elif mgmt_tab == "OFFICE": limit = p.unit_classes.size() + GameData.BUILDINGS.keys().size()
				elif mgmt_tab == "SQUARE": limit = s.npcs.size() if s else 0
				mgmt_idx_l = min(mgmt_idx_l + 1, max(0, limit - 1))
			else:
				if mgmt_tab in ["CHARACTER", "ROSTER", "MARKET"]: limit = p.stash.size()
				elif mgmt_tab == "RECRUIT": limit = p.prisoners.size()
				elif mgmt_tab == "TRADE": limit = p.inventory.keys().size()
				elif mgmt_tab == "WORLD": limit = min(gs.logistical_pulses.size(), 30)
				elif mgmt_tab == "OFFICE": limit = p.commissions.size()
				elif mgmt_tab == "SQUARE":
					if s and mgmt_idx_l < s.npcs.size():
						limit = s.npcs[mgmt_idx_l].quests.size()
				mgmt_idx_r = min(mgmt_idx_r + 1, max(0, limit - 1))
			_on_map_updated()

		KEY_E: # Interaction Key
			if mgmt_tab == "CHARACTER" and p.stash.size() > 0:
				var slots = [
					"main_hand", "off_hand",
					"head_under", "head_over", "head_armor",
					"torso_under", "torso_over", "torso_armor",
					"arms_under", "arms_over", "arms_armor",
					"hands_under", "hands_over", "hands_armor",
					"legs_under", "legs_over", "legs_armor",
					"feet_under", "feet_over", "feet_armor",
					"cover"
				]
				if mgmt_idx_l < slots.size():
					gs.equip_commander_item(slots[mgmt_idx_l], mgmt_idx_r)
			elif mgmt_tab == "TRAINING":
				var cmd = p.commander
				var attr_keys = cmd.attributes.keys()
				if mgmt_idx_l < attr_keys.size():
					if cmd.stat_points > 0:
						cmd.stat_points -= 1
						cmd.attributes[attr_keys[mgmt_idx_l]] += 1
						gs.add_log("Training: Your %s increased to %d!" % [attr_keys[mgmt_idx_l].capitalize(), cmd.attributes[attr_keys[mgmt_idx_l]]])
					else:
						gs.add_log("You lack the stat points required to train further.")
				else:
					var skill_keys = cmd.skills.keys()
					var sk_idx = mgmt_idx_l - attr_keys.size()
					if sk_idx < skill_keys.size():
						if cmd.skill_points > 0:
							cmd.skill_points -= 1
							cmd.skills[skill_keys[sk_idx]] += 1
							gs.add_log("Practice: Your %s improved to %d!" % [skill_keys[sk_idx].capitalize().replace("_", " "), cmd.skills[skill_keys[sk_idx]]])
						else:
							gs.add_log("You lack the skill points required to practice further.")
			elif mgmt_tab == "ROSTER" and p.stash.size() > 0:
				gs.equip_item(mgmt_idx_l, mgmt_idx_r)
			elif mgmt_tab == "RECRUIT":
				if mgmt_focus == 0 and s:
					gs.hire_recruit(s_pos, mgmt_idx_l)
				elif mgmt_focus == 1 and p.prisoners.size() > 0:
					gs.recruit_prisoner(mgmt_idx_r)
			elif mgmt_tab == "MARKET" and mgmt_focus == 0 and s:
				gs.buy_item(s_pos, mgmt_idx_l)
			elif mgmt_tab == "TRADE" and s:
				if mgmt_focus == 0:
					var resources = ["grain", "fish", "game", "meat", "wood", "stone", "iron", "horses", "copper", "tin", "lead", "silver", "gold", "bronze", "steel", "livestock", "wool", "leather", "cloth", "ale", "fine_garments", "jewelry", "furs", "peat", "salt", "sand", "glass_sand", "spices", "ivory", "coal", "clay", "marble", "tools", "bricks"]
					if mgmt_idx_l < resources.size():
						gs.buy_resource(s_pos, resources[mgmt_idx_l])
				else:
					var p_res = p.inventory.keys()
					if mgmt_idx_r < p_res.size():
						gs.sell_resource(s_pos, p_res[mgmt_idx_r])
			elif mgmt_tab == "OFFICE" and p.unit_classes.size() > 0:
				mgmt_is_designing = true
			elif mgmt_tab == "SQUARE":
				if mgmt_focus == 1 and s and mgmt_idx_l < s.npcs.size():
					gs.accept_quest(s_pos, mgmt_idx_l, mgmt_idx_r)
				elif mgmt_focus == 0 and s and mgmt_idx_l < s.npcs.size():
					# Talking to an NPC focuses their quests
					mgmt_focus = 1
					mgmt_idx_r = 0
		
		KEY_R: # Ransom (Recruit Tab)
			if mgmt_tab == "RECRUIT" and mgmt_focus == 1 and p.prisoners.size() > 0:
				if s:
					gs.ransom_prisoner(mgmt_idx_r)
				else:
					gs.add_log("You must be at a settlement to ransom prisoners.")
				
		KEY_U: # Unequip
			if mgmt_tab == "CHARACTER":
				var slots = [
					"main_hand", "off_hand",
					"head_under", "head_over", "head_armor",
					"torso_under", "torso_over", "torso_armor",
					"arms_under", "arms_over", "arms_armor",
					"hands_under", "hands_over", "hands_armor",
					"legs_under", "legs_over", "legs_armor",
					"feet_under", "feet_over", "feet_armor",
					"cover"
				]
				if mgmt_idx_l < slots.size():
					gs.unequip_commander_item(slots[mgmt_idx_l])
			elif mgmt_tab == "ROSTER":
				var u_obj = p.roster[mgmt_idx_l]
				if u_obj.equipment.main_hand: gs.unequip_item(mgmt_idx_l, "main_hand")
				elif u_obj.equipment.off_hand: gs.unequip_item(mgmt_idx_l, "off_hand")
				else:
					for slot in ["head", "torso", "l_arm", "r_arm", "l_leg", "r_leg"]:
						for layer in ["cover", "armor", "under"]:
							if u_obj.equipment[slot][layer]:
								gs.unequip_item(mgmt_idx_l, slot, layer)
								return
		
		KEY_B: # Buy (Market Tab)
			if mgmt_tab == "MARKET" and mgmt_focus == 0 and s and not s.type.ends_with("_hamlet"):
				gs.buy_item(s_pos, mgmt_idx_l)
			elif mgmt_tab == "TRADE" and mgmt_focus == 0 and s:
				var resources = ["grain", "fish", "game", "meat", "wood", "stone", "iron", "horses", "copper", "tin", "lead", "silver", "gold", "bronze", "steel", "livestock", "wool", "leather", "cloth", "ale", "fine_garments", "jewelry", "furs", "peat", "salt", "sand", "glass_sand", "spices", "ivory", "coal", "clay", "marble", "tools", "bricks"]
				if mgmt_idx_l < resources.size():
					gs.buy_resource(s_pos, resources[mgmt_idx_l])
		
		KEY_L: # Sell (Trade Tab)
			if mgmt_tab == "TRADE" and mgmt_focus == 1 and s:
				var p_res = p.inventory.keys()
				if mgmt_idx_r < p_res.size():
					gs.sell_resource(s_pos, p_res[mgmt_idx_r])

		KEY_K: # Commission (Market Tab) or Fund (Office Tab)
			if mgmt_tab == "MARKET" and mgmt_focus == 0 and s and not s.type.ends_with("_hamlet"):
				var shop = s.shop_inventory
				if mgmt_idx_l < shop.size():
					var item = shop[mgmt_idx_l]
					gs.commission_items(s_pos, item.type_key, item.material, item.quality, 10)
			elif mgmt_tab == "OFFICE" and s:
				var keys = p.unit_classes.keys()
				if mgmt_idx_l < keys.size():
					gs.fund_class_commissions(s_pos, keys[mgmt_idx_l])
				
		KEY_C: # Create Class (Roster Tab)
			if mgmt_tab == "ROSTER":
				var u = p.roster[mgmt_idx_l]
				var c_name = "Class_" + str(p.unit_classes.size() + 1)
				var reqs = {}
				for slot in ["main_hand", "off_hand"]:
					var item = u.equipment[slot]
					if item: reqs[slot] = {"type": item.type_key, "material": item.material, "quality": item.quality}
					else: reqs[slot] = {"type": "none", "material": "any", "quality": "standard"}
				
				var slots = ["head_under", "head_armor", "torso_under", "torso_armor", "arms_under", "arms_armor", "legs_under", "legs_armor", "cover"]
				for s_key in slots:
					var item = null
					if s_key == "cover": item = u.equipment.torso.cover
					else:
						var parts = s_key.split("_")
						var layer = parts[parts.size()-1]
						var slot = s_key.trim_suffix("_" + layer)
						if slot == "arms": slot = "l_arm"
						elif slot == "legs": slot = "l_leg"
						item = u.equipment[slot][layer]
					
					if item: reqs[s_key] = {"type": item.type_key, "material": item.material, "quality": item.quality}
					else: reqs[s_key] = {"type": "none", "material": "any", "quality": "standard"}
				gs.create_class(c_name, reqs)
		
		KEY_A: # Assign Class (Roster Tab)
			if mgmt_tab == "ROSTER":
				var keys = p.unit_classes.keys()
				if keys.size() > 0:
					var u = p.roster[mgmt_idx_l]
					var current_idx = keys.find(u.assigned_class)
					var next_idx = (current_idx + 1) % (keys.size() + 1)
					if next_idx == keys.size():
						gs.assign_class(mgmt_idx_l, "")
					else:
						gs.assign_class(mgmt_idx_l, keys[next_idx])

		KEY_P: # Auto-Equip All (Office Tab)
			if mgmt_tab == "OFFICE":
				gs.auto_equip_all()
		
		KEY_G: # Sponsor Project (Office Tab)
			if mgmt_tab == "OFFICE" and s:
				var keys = p.unit_classes.keys()
				var b_list = GameData.BUILDINGS.keys()
				var b_idx = mgmt_idx_l - keys.size()
				if b_idx >= 0 and b_idx < b_list.size():
					gs.sponsor_building(s_pos, b_list[b_idx])
		
		KEY_D: # Donate Resource (Office Tab)
			if mgmt_tab == "OFFICE" and s:
				var _resources = ["wood", "stone", "iron", "grain"]
				# For simplicity, donate whatever resource is needed by the current project
				if not s.construction_queue.is_empty():
					var q = s.construction_queue[0]
					var res_to_donate = ""
					if s.inventory.get("wood", 0) < q.wood_needed: res_to_donate = "wood"
					elif s.inventory.get("stone", 0) < q.stone_needed: res_to_donate = "stone"
					elif s.inventory.get("iron", 0) < q.iron_needed: res_to_donate = "iron"
					
					if res_to_donate != "":
						gs.donate_resource(s_pos, res_to_donate, 10)
					else:
						gs.donate_resource(s_pos, "grain", 10)
				else:
					gs.donate_resource(s_pos, "grain", 10)
		
		KEY_N: # New Class (Office Tab)
			if mgmt_tab == "OFFICE":
				var c_name = "Class_" + str(p.unit_classes.size() + 1)
				var blank = {
					"main_hand": {"type": "none", "material": "any", "quality": "standard"},
					"off_hand": {"type": "none", "material": "any", "quality": "standard"},
					"torso_armor": {"type": "none", "material": "any", "quality": "standard"}
				}
				gs.create_class(c_name, blank)
				mgmt_idx_l = p.unit_classes.size() - 1
				mgmt_is_designing = true

	_on_map_updated()

func handle_designer_input(event):
	var p = GameState.player
	var keys = p.unit_classes.keys()
	if mgmt_idx_l >= keys.size(): 
		mgmt_is_designing = false
		return
		
	var c_name = keys[mgmt_idx_l]
	var bp = p.unit_classes[c_name]
	
	var slots = [
		"main_hand", "off_hand",
		"head_under", "head_armor",
		"torso_under", "torso_armor",
		"arms_under", "arms_armor",
		"legs_under", "legs_armor",
		"cover"
	]
			
	mgmt_design_slot = clamp(mgmt_design_slot, 0, slots.size() - 1)
	var slot_key = slots[mgmt_design_slot]
	if not bp.has(slot_key):
		bp[slot_key] = {"type": "none", "material": "any", "quality": "standard"}
	
	# Filter types by slot
	var types = ["none"]
	for t_name in GameData.ITEMS:
		var item = GameData.ITEMS[t_name]
		var valid = false
		if slot_key == "main_hand" and item["slot"] in ["main_hand", "both_hands"]: valid = true
		elif slot_key == "off_hand" and item["slot"] in ["off_hand", "main_hand"]: valid = true
		elif "_under" in slot_key and item["layer"] == "under":
			if "head" in slot_key and item["slot"] == "head": valid = true
			elif "head" not in slot_key and item["slot"] in ["torso", "arms", "legs"]: valid = true
		elif "_armor" in slot_key and item["layer"] == "armor":
			if "head" in slot_key and item["slot"] == "head": valid = true
			elif "torso" in slot_key and item["slot"] == "torso": valid = true
			elif "arms" in slot_key and item["slot"] == "arms": valid = true
			elif "legs" in slot_key and item["slot"] == "legs": valid = true
		elif slot_key == "cover" and item["layer"] == "cover": valid = true
		
		if valid: types.append(t_name)
		
	var mats = ["any"] + GameData.MATERIALS.keys()
	var quals = ["rusty", "standard", "fine", "masterwork"]
	
	match event.keycode:
		KEY_W: mgmt_design_slot = posmod(mgmt_design_slot - 1, slots.size())
		KEY_S: mgmt_design_slot = posmod(mgmt_design_slot + 1, slots.size())
		KEY_A: mgmt_design_prop = posmod(mgmt_design_prop - 1, 3)
		KEY_D: mgmt_design_prop = posmod(mgmt_design_prop + 1, 3)
		
		KEY_LEFT, KEY_RIGHT:
			var dir = 1 if event.keycode == KEY_RIGHT else -1
			var current = bp[slot_key]
			if mgmt_design_prop == 0: # Type
				var idx = types.find(current["type"])
				if idx == -1: idx = 0
				current["type"] = types[posmod(idx + dir, types.size())]
			elif mgmt_design_prop == 1: # Mat
				var idx = mats.find(current["material"])
				if idx == -1: idx = 0
				current["material"] = mats[posmod(idx + dir, mats.size())]
			elif mgmt_design_prop == 2: # Qual
				var idx = quals.find(current["quality"])
				if idx == -1: idx = 1
				current["quality"] = quals[posmod(idx + dir, quals.size())]
		
		KEY_ENTER, KEY_ESCAPE:
			mgmt_is_designing = false
			GameState.add_log("Saved changes to %s." % c_name)
		
		KEY_K: # Fund from within designer
			var s_pos = p.pos
			if GameState.settlements.has(s_pos):
				GameState.fund_class_commissions(s_pos, c_name)
	
	_on_map_updated()

func render_to_tilemap():
	if not graphical_mode or not tile_map:
		if tile_map: tile_map.visible = false
		map_display.visible = true
		return
		
	tile_map.visible = true
	if state in ["overworld", "battle", "dungeon", "city", "world_preview"]:
		map_display.visible = false
	else:
		map_display.visible = true
		tile_map.visible = false
		return

	tile_map.clear()
	
	var dims = get_char_dims()
	var vw = dims.x
	var vh = dims.y
	
	var font_size = map_display.get_theme_font_size("normal_font_size")
	var font = map_display.get_theme_font("normal_font")
	var char_w = int(font.get_string_size("█", HORIZONTAL_ALIGNMENT_LEFT, -1, font_size).x)
	
	tile_map.tile_set.tile_size = Vector2i(TILE_SIZE, TILE_SIZE)
	tile_map.position = Vector2(10, 10)
	tile_map.scale = Vector2(char_w / float(TILE_SIZE), char_w / float(TILE_SIZE))
	
	var center = Vector2i.ZERO
	var g_w = 0
	var g_h = 0
	var grid_ref = []
	var scope = "global"
	var start_x = 0
	var start_y = 0
	
	if state == "overworld":
		center = GameState.player.pos + GameState.player.camera_offset
		g_w = GameState.width
		g_h = GameState.height
		grid_ref = GameState.grid
		scope = "world"
		start_x = center.x - vw / 2
		start_y = center.y - vh / 2
	elif state == "city":
		center = city_ctrl.player_pos
		g_w = city_ctrl.width
		g_h = city_ctrl.height
		grid_ref = city_ctrl.grid
		scope = "local"
		start_x = center.x - vw / 2
		start_y = center.y - vh / 2
	elif state == "dungeon":
		center = dungeon_ctrl.player_pos
		g_w = dungeon_ctrl.width
		g_h = dungeon_ctrl.height
		grid_ref = dungeon_ctrl.grid
		scope = "local"
		start_x = center.x - vw / 2
		start_y = center.y - vh / 2
	elif state == "battle":
		if is_instance_valid(battle_ctrl.player_unit):
			center = battle_ctrl.player_unit.pos
		else:
			center = Vector2i(battle_ctrl.MAP_W / 2, battle_ctrl.MAP_H / 2)
		g_w = battle_ctrl.MAP_W
		g_h = battle_ctrl.MAP_H
		grid_ref = battle_ctrl.grid
		scope = "battle"
		start_x = center.x - vw / 2
		start_y = center.y - vh / 2
	elif state == "world_preview":
		center = preview_pos
		g_w = GameState.width
		g_h = GameState.height
		grid_ref = GameState.grid
		scope = "world"
		# Adjust tile map scale for preview zoom
		tile_map.scale = Vector2(preview_zoom, preview_zoom)
		# We need to recalculate vw/vh based on new scale
		vw = int(map_display.size.x / (TILE_SIZE * preview_zoom)) - 2
		vh = int(map_display.size.y / (TILE_SIZE * preview_zoom)) - 2
		start_x = center.x - vw / 2
		start_y = center.y - vh / 2
	elif state == "loading":
		center = Vector2i(GameState.width / 2, GameState.height / 2)
		g_w = GameState.width
		g_h = GameState.height
		grid_ref = GameState.grid
		scope = "world"
		
		# Auto-calculate zoom to fit the whole world on screen during loading
		var panel_size = $MainLayout/ContentLayout/MapPanel.size - Vector2(40, 40)
		if panel_size.x <= 0 or panel_size.y <= 0: panel_size = Vector2(800, 600)
		
		var fit_zoom_x = panel_size.x / (g_w * TILE_SIZE)
		var fit_zoom_y = panel_size.y / (g_h * TILE_SIZE)
		var loading_zoom = max(0.01, min(fit_zoom_x, fit_zoom_y))
		
		tile_map.scale = Vector2(loading_zoom, loading_zoom)
		vw = g_w
		vh = g_h
		# Adjust start_x/y to center the world-map exactly
		start_x = 0
		start_y = 0
		# Override position to truly center it
		tile_map.position = (panel_size - Vector2(g_w, g_h) * TILE_SIZE * loading_zoom) / 2.0 + Vector2(20, 20)
	else:
		tile_map.visible = false
		map_display.visible = true
		return
	
	# 11. PRE-FETCH Entities (Entities that override terrain)
	var entities = {}
	if state == "overworld" or state == "world_preview" or state == "loading":
		if GameState.player:
			entities[GameState.player.pos] = {"char": "@", "col": Color.YELLOW}
		for army in GameState.armies:
			var col = Color.RED
			if army.faction == "player" or (GameState.player and army.faction == GameState.player.faction):
				col = Color.CYAN
			entities[army.pos] = {"char": "A", "col": col}
		for pos in GameState.settlements:
			var s = GameState.settlements[pos]
			var sym = "v"
			match s.type:
				"hamlet": sym = "h"
				"village": sym = "v"
				"town": sym = "T"
				"city": sym = "C"
				"metropolis": sym = "M"
				"castle": sym = "S"
			entities[pos] = {"char": sym, "col": Color.WHITE}
	elif state == "battle":
		for pos in battle_ctrl.unit_lookup:
			var u = battle_ctrl.unit_lookup[pos]
			var color = Color.RED
			if u.faction == GameState.player.faction or u.faction == "player":
				color = Color.CYAN
			
			var sym = u.symbol
			if u.status.get("is_prone", false): sym = "_"
			elif u.status.get("is_dead", false): sym = "%"
			entities[pos] = {"char": sym, "col": color}
			
		for p in battle_ctrl.projectiles:
			var p_pos = Vector2i(p.pos)
			var color = Color.YELLOW
			if p.has("engine"): color = Color.ORANGE
			entities[p_pos] = {"char": p.symbol, "col": color}
	elif state == "dungeon":
		entities[dungeon_ctrl.player_pos] = {"char": "@", "col": Color.YELLOW}
		for e in dungeon_ctrl.enemies:
			if e.hp > 0:
				entities[Vector2i(e.pos)] = {"char": e.type[0].to_upper(), "col": Color.RED}
		for i in dungeon_ctrl.items:
			entities[Vector2i(i.pos)] = {"char": "$", "col": Color.GOLD}
	elif state == "city":
		entities[city_ctrl.player_pos] = {"char": "@", "col": Color.YELLOW}
		for eng in city_ctrl.engines:
			entities[Vector2i(eng.pos)] = {"char": eng.type, "col": Color.ORANGE}
	
	for y in range(vh):
		for x in range(vw):
			var wx = start_x + x
			var wy = start_y + y
			if wx < 0 or wx >= g_w or wy < 0 or wy >= g_h:
				continue
				
			var pos = Vector2i(wx, wy)
			var terrain_char = grid_ref[wy][wx]
			
			var tile_color = UIPanels._get_terrain_color(GameState, pos, terrain_char, scope)
			
			if state == "overworld" or state == "world_preview":
				if GameState.map_mode == "province" or GameState.map_mode == "political":
					if GameState.province_grid.size() > wy and GameState.province_grid[wy].size() > wx:
						var p_id = GameState.province_grid[wy][wx]
						if p_id != -1:
							var p_colors = [Color.RED, Color.BLUE, Color.GREEN, Color.YELLOW, Color.PURPLE, Color.ORANGE, Color.CYAN, Color.MAGENTA, Color.PINK, Color.TEAL]
							tile_color = p_colors[p_id % p_colors.size()]
			
			# Background (Layer 0)
			var bg_alt = get_tile_for_color(tile_color)
			tile_map.set_cell(0, Vector2i(x, y), 0, Vector2i(0, 0), bg_alt)
			
			# Character (Layer 1)
			var char_to_draw = ""
			var char_color = Color.WHITE
			
			if entities.has(pos):
				char_to_draw = entities[pos].char
				char_color = entities[pos].col
			elif GameState.render_mode != "grid":
				char_to_draw = terrain_char
			
			if char_to_draw != "" and char_to_draw != " ":
				var ascii_val = char_to_draw.unicode_at(0)
				if ascii_val < 256:
					var char_atlas_pos = Vector2i(ascii_val % 16, ascii_val / 16)
					var alt_id = get_tile_for_char_color(char_atlas_pos, char_color)
					tile_map.set_cell(1, Vector2i(x, y), 1, char_atlas_pos, alt_id)
			
			# Selection & Highlights (Layer 2)
			var highlight_color = Color(0,0,0,0)
			
			if pos == last_hover_world_pos:
				highlight_color = Color(1, 1, 1, 0.4)
			
			if state == "battle" and is_instance_valid(battle_ctrl.player_unit):
				var dist = (Vector2(pos) - Vector2(battle_ctrl.player_unit.pos)).length()
				var u_range = battle_ctrl.get_unit_range(battle_ctrl.player_unit)
				
				# If in range, show light tint
				if dist <= u_range and dist > 0.1:
					if battle_ctrl.targeting_mode:
						highlight_color = Color(1, 0.2, 0.2, 0.15) # Red for attack
					else:
						highlight_color = Color(0.2, 0.5, 1.0, 0.1) # Blue for movement/presence
				
				# Highlight target
				if battle_ctrl.targeting_mode and battle_ctrl.targeting_target:
					if pos == battle_ctrl.targeting_target.pos:
						highlight_color = Color(1, 0.8, 0, 0.4) # Bright Gold for target
				
				if pos == battle_ctrl.player_unit.pos:
					highlight_color = Color(0, 1, 1, 0.2) # Teal for player
			
			elif state == "dungeon":
				var d_pos = dungeon_ctrl.player_pos
				var d_dist = (Vector2(pos) - Vector2(d_pos)).length()
				if d_dist < 4.5: # Simple torch view range
					var alpha = clamp(0.25 * (1.0 - d_dist/4.5), 0.0, 0.25)
					if alpha > 0.02:
						highlight_color = Color(1, 0.9, 0.7, alpha) # Warm torch glow

			if highlight_color.a > 0.01:
				var alt = get_tile_for_color(highlight_color)
				tile_map.set_cell(2, Vector2i(x, y), 0, Vector2i(0, 0), alt)

func _on_map_updated():
	if state == "menu":
		# Render as simple menu
		map_display.text = UIPanels.render_menu(menu_options, menu_idx, generated_world != null, saved_character != null)
		$MainLayout/ContentLayout/SidePanel.visible = false
		$MainLayout/LogPanel.visible = false
		if tile_map: tile_map.visible = false
		map_display.visible = true
		$MainLayout/ScreenHeader.text = "[center]FALLING LEAVES[/center]"
		return

	if state == "city_studio":
		map_display.text = UIPanels.render_city_studio(GameState.city_studio_config, GameState.city_studio_idx)
		$MainLayout/ContentLayout/SidePanel.visible = false
		$MainLayout/LogPanel.visible = false
		if tile_map: tile_map.visible = false
		map_display.visible = true
		$MainLayout/ScreenHeader.text = "[center]CITY DESIGN STUDIO[/center]"
		return

	if state == "loading":
		var frame = UIPanels.render_loading_screen(loading_stage, GameState if (GameState.grid.size() > 0) else null)
		$MainLayout/ScreenHeader.text = frame.header
		info_label.text = frame.side
		
		# Clear map display to ensure no ghosting from previous states
		map_display.text = "" 
		
		if graphical_mode and GameState.grid.size() > 0:
			map_display.visible = false
			tile_map.visible = true
			# Only update tilemap occasionally during generation to avoid stalls
			if Engine.get_frames_drawn() % 2 == 0:
				render_to_tilemap()
		else:
			map_display.bbcode_enabled = true # Ensure tags are parsed
			map_display.text = frame.map
			if tile_map: tile_map.visible = false
			map_display.visible = true
			
		$MainLayout/ContentLayout/SidePanel.visible = true
		$MainLayout/LogPanel.visible = false
		return

	# Handle Map Views (Overworld, Battle, Dungeon, City, World Preview, Loading)
	if graphical_mode and state in ["overworld", "battle", "dungeon", "city", "world_preview", "loading"]:
		render_to_tilemap()
	else:
		if tile_map: tile_map.visible = false
		map_display.visible = true

	match state:
		"world_creation":
			map_display.bbcode_enabled = true
			map_display.text = UIPanels.render_world_creation(world_config, world_config_idx)
			$MainLayout/ContentLayout/SidePanel.visible = false
			$MainLayout/LogPanel.visible = false
			$MainLayout/ScreenHeader.text = "[center]WORLD GENERATOR[/center]"
			return
		"battle_config":
			map_display.bbcode_enabled = true
			map_display.text = UIPanels.render_battle_config(sim_config, sim_config_idx)
			$MainLayout/ContentLayout/SidePanel.visible = false
			$MainLayout/LogPanel.visible = false
			$MainLayout/ScreenHeader.text = "[center]BATTLE SIMULATOR[/center]"
			return
		"world_preview":
			var frame = UIPanels.render_world_preview(GameState, preview_pos)
			$MainLayout/ScreenHeader.text = frame.header
			map_display.bbcode_enabled = true
			map_display.text = frame.map
			info_label.text = frame.side
			$MainLayout/ContentLayout/SidePanel.visible = true
			$MainLayout/LogPanel.visible = true
			log_label.text = "WASD: Pan | +/-: Zoom | ENTER: Accept | R: Reroll"
			return
		"character_creation":
			map_display.bbcode_enabled = true
			map_display.text = UIPanels.render_character_creation_tabbed(
				player_config, player_config_idx, trait_selection_idx, 
				calculate_creation_points(), cc_tab, cc_shop_items, 
				cc_purchases, calculate_available_crowns(),
				CC_MATERIALS[cc_mat_idx], CC_QUALITIES[cc_qual_idx]
			)
			$MainLayout/ContentLayout/SidePanel.visible = false
			$MainLayout/LogPanel.visible = false
			return
		"location_select":
			var current_loc = location_list[location_idx]
			map_display.bbcode_enabled = true
			map_display.text = UIPanels.render_location_select(GameState, current_loc)
			$MainLayout/ContentLayout/SidePanel.visible = false
			$MainLayout/LogPanel.visible = false
			return

	# Update font size FIRST based on state, so get_char_dims() is accurate
	var target_font_size = current_font_size
	if state == "world_map":
		target_font_size = 4
	elif state == "overworld":
		GameState.player.camera_zoom = current_font_size / 16.0
	elif state == "battle" and is_instance_valid(battle_ctrl):
		battle_ctrl.camera_zoom = current_font_size / 16.0
	
	map_display.add_theme_font_size_override("normal_font_size", target_font_size)
	map_display.add_theme_font_size_override("bold_font_size", target_font_size)
	_update_node_theme(map_display, target_font_size)
	
	# Explicitly reset info and log label font sizes to standard 16
	info_label.add_theme_font_size_override("normal_font_size", 16)
	log_label.add_theme_font_size_override("normal_font_size", 16)

	var dims = get_char_dims()
	var vw = max(10, dims.x)
	var vh = max(5, dims.y - 12) # Major buffer increase to ensure centering
	
	# Show panels (Godot UI handles the layout)
	$MainLayout/ContentLayout/SidePanel.visible = true
	$MainLayout/LogPanel.visible = true
	info_label.visible = true
	
	if state == "management":
		var content = UIPanels.get_management_screen(GameState, mgmt_tab, mgmt_focus, mgmt_idx_l, mgmt_idx_r, mgmt_is_designing, mgmt_design_slot, mgmt_design_prop)
		map_display.text = content
		$MainLayout/ContentLayout/SidePanel.visible = false
		$MainLayout/LogPanel.visible = false
	elif state == "world_map":
		var world_lines = UIPanels.render_world_map(GameState)
		map_display.text = "\n".join(world_lines)
		$MainLayout/ContentLayout/SidePanel.visible = false
		$MainLayout/LogPanel.visible = false
	elif state == "history":
		map_display.text = UIPanels.render_history(GameState, history_offset)
		$MainLayout/ContentLayout/SidePanel.visible = false
		$MainLayout/LogPanel.visible = false
	elif state == "city":
		var map_lines = UIPanels.render_city(GameState, city_ctrl, vw, vh)
		var side_lines = ["City: " + city_ctrl.city_name, "Pos: " + str(city_ctrl.player_pos)]
		var header = "[ CITY EXPLORER - %s ]" % city_ctrl.city_name.to_upper()
		var frame = UIPanels.get_master_frame(GameState, map_lines, side_lines, header, vw, GameState.event_log, 10)
		map_display.text = frame.map
		info_label.text = frame.side
		log_label.text = frame.log
		$MainLayout/ScreenHeader.text = "[center]%s[/center]" % frame.header
	elif state == "dungeon":
		var map_lines = UIPanels.render_dungeon(GameState, dungeon_ctrl, vw, vh)
		var side_lines = UIPanels.get_side_panel(GameState, dungeon_ctrl)
		var header = "[ %s - Floor %d ]" % [dungeon_ctrl.dungeon_name.to_upper(), dungeon_ctrl.current_floor]
		var frame = UIPanels.get_master_frame(GameState, map_lines, side_lines, header, vw, dungeon_ctrl.messages, 10, dungeon_ctrl.log_offset)
		map_display.text = frame.map
		info_label.text = frame.side
		log_label.text = frame.log
		$MainLayout/ScreenHeader.text = "[center]%s[/center]" % frame.header
	elif state == "battle":
		var map_lines = UIPanels.render_battle(GameState, battle_ctrl, vw, vh)
		var side_lines = UIPanels.get_battle_side_panel(GameState, battle_ctrl)
		var e_type = "battle"
		if battle_ctrl.enemy_ref is Dictionary:
			e_type = battle_ctrl.enemy_ref.get("type", "battle")
		elif battle_ctrl.enemy_ref:
			e_type = battle_ctrl.enemy_ref.type
			
		var header = "[ BATTLE - %s ]" % e_type.to_upper()
		var frame = UIPanels.get_master_frame(GameState, map_lines, side_lines, header, vw, battle_ctrl.battle_log, 10, battle_ctrl.log_offset)
		map_display.text = frame.map
		info_label.text = frame.side
		log_label.text = frame.log
		$MainLayout/ScreenHeader.text = "[center]%s[/center]" % frame.header
	elif state == "codex":
		map_display.text = UIPanels.render_codex(GameState, CodexData, codex_cat_idx, codex_entry_idx, codex_focus)
		$MainLayout/ContentLayout/SidePanel.visible = false
		$MainLayout/LogPanel.visible = false
	elif state == "party_info":
		map_display.text = UIPanels.get_party_info_screen(GameState)
		$MainLayout/ContentLayout/SidePanel.visible = false
		$MainLayout/LogPanel.visible = false
	elif state == "dialogue":
		map_display.text = UIPanels.get_dialogue_screen(GameState, dialogue_target, dialogue_options, dialogue_idx)
		$MainLayout/ContentLayout/SidePanel.visible = false
		$MainLayout/LogPanel.visible = false
	elif state == "region":
		var map_lines = UIPanels.render_region(GameState, region_ctrl, vw, vh)
		var side_lines = UIPanels.get_side_panel(GameState)
		var p_pos_i = Vector2i(region_ctrl.player_pos)
		var header = "[ REGION VIEW - %d, %d ]" % [p_pos_i.x, p_pos_i.y]
		var frame = UIPanels.get_master_frame(GameState, map_lines, side_lines, header, vw, GameState.event_log, 10)
		map_display.text = frame.map
		info_label.text = frame.side
		log_label.text = frame.log
		$MainLayout/ScreenHeader.text = "[center]%s[/center]" % frame.header
	else:
		var map_lines = []
		var header_override = ""
		if state == "overworld" and GameState.travel_mode == GameState.TravelMode.LOCAL:
			if battle_ctrl.last_map_pos != GameState.player.pos:
				battle_ctrl.generate_map()
			map_lines = UIPanels.render_local_viewport(GameState, battle_ctrl, vw, vh)
			header_override = "[ TRAVEL - LOCAL MODE ]"
		else:
			map_lines = UIPanels.render_viewport(GameState, vw, vh, last_calculated_path)
			
		var side_lines = UIPanels.get_side_panel(GameState)
		var frame = UIPanels.get_master_frame(GameState, map_lines, side_lines, header_override, vw, GameState.event_log, 10)
		map_display.text = frame.map
		info_label.text = frame.side
		log_label.text = frame.log
		$MainLayout/ScreenHeader.text = "[center]%s[/center]" % frame.header
		# Ensure tile info is synced immediately if we are in the overworld
		_sync_tile_info()
	
	if graphical_mode and (state == "overworld" or state == "dungeon" or state == "battle"):
		GameState.graphical_mode_active = true
		render_to_tilemap()
	else:
		GameState.graphical_mode_active = false
		if tile_map: tile_map.visible = false
		map_display.visible = true

func _sync_tile_info():
	var cursor = GameState.player.pos + GameState.player.camera_offset
	var side_lines = UIPanels.get_side_panel(GameState, cursor)
	info_label.text = "\n".join(side_lines)

func get_char_dims() -> Vector2i:
	if $MainLayout/ContentLayout/MapPanel.size.x < 10:
		return Vector2i(80, 40)
	
	var panel_size = $MainLayout/ContentLayout/MapPanel.size - Vector2(20, 20)
	
	var font = map_display.get_theme_font("normal_font")
	var font_size = map_display.get_theme_font_size("normal_font_size")
	var char_w = int(font.get_string_size("█", HORIZONTAL_ALIGNMENT_LEFT, -1, font_size).x)
	var char_h = int(font.get_height(font_size))
	
	if GameState.render_mode == "grid":
		char_h = char_w

	if graphical_mode:
		return Vector2i(int(panel_size.x / char_w), int(panel_size.y / char_w))

	return Vector2i(int(panel_size.x / char_w), int(panel_size.y / char_h))

func update_side_panels():
	# This function is now mostly redundant but we'll keep it for specific toggles if needed
	pass

func _on_log_updated():
	log_label.text = "\n".join(GameState.event_log)

func _on_battle_started(enemy):
	state = "battle"
	$MainLayout.visible = true # Keep layout visible for unified UI
	$MainLayout/LogPanel.visible = false
	battle_ui.visible = false # Hide old battle UI
	battle_ctrl.start(enemy)

func _on_battle_ended(win):
	if not generated_world:
		state = "battle_config"
		GameState.add_log("Simulation finished. Returned to config.")
		battle_ui.visible = false
		$MainLayout.visible = true
		_on_map_updated()
		return

	if battle_ctrl.is_tournament:
		if win:
			GameState.player.tournament_round += 1
			state = "dialogue"
			var s = GameState.settlements.get(GameState.player.pos)
			if GameState.player.tournament_round > s.tournament_participants.size():
				dialogue_options = ["Claim Prize"]
			else:
				GameState.add_log("Master: 'Incredible victory! You advance to the next round.'")
				dialogue_options = ["Next Round", "Leave"]
			dialogue_idx = 0
		else:
			GameState.add_log("Master: 'Better luck next time. You are eliminated.'")
			state = "overworld"
	else:
		state = "overworld"
	
	battle_ui.visible = false
	$MainLayout.visible = true
	$MainLayout/LogPanel.visible = false
	_on_map_updated()

func _on_dungeon_started(ruin):
	state = "dungeon"
	dungeon_ctrl.start(ruin, GameState.player.pos)
	_on_map_updated()

func _on_settlement_entered(s):
	state = "city"
	if city_ctrl.has_method("activate"):
		city_ctrl.activate(s, s.pos, GameState.world_seed)
	elif city_ctrl.has_method("generate_from_settlement"):
		city_ctrl.generate_from_settlement(s)
	_on_map_updated()

func _on_dungeon_ended():
	state = "overworld"
	_on_map_updated()

func _on_dialogue_started(target, options):
	state = "dialogue"
	dialogue_target = target
	dialogue_options = options
	dialogue_idx = 0
	_on_map_updated()
