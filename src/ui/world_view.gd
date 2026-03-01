## WorldView — post-generation world observation screen.
##
## Entered via the "▶▶ ENTER WORLD" button on WorldGenScreen.
## Reads WorldState from Bootstrap autoload (already set by generation).
##
## Controls:
##   Scroll = zoom  ·  Drag = pan
##   Space = pause  ·  +/- = zoom  ·  0/F = reset zoom  ·  Arrow keys = pan
##   F10 = toggle EconomyView overlay  ·  Esc = back to WorldGenScreen
##
## Click a settlement dot on the map to inspect it in the left panel.
class_name WorldView
extends Control

# ── Terrain / tier colours (match WorldGenScreen) ────────────────────────────
const TERRAIN_COLORS: Dictionary = {
	"plains":        Color(0.76, 0.88, 0.52),
	"coast":         Color(0.68, 0.84, 0.72),
	"river":         Color(0.40, 0.65, 0.95),
	"hills":         Color(0.70, 0.64, 0.44),
	"forest":        Color(0.22, 0.55, 0.25),
	"desert":        Color(0.96, 0.88, 0.54),
	"tundra":        Color(0.82, 0.86, 0.88),
	"mountain":      Color(0.62, 0.60, 0.65),
	"shallow_water": Color(0.45, 0.70, 0.95),
	"ocean":         Color(0.18, 0.38, 0.75),
	"lake":          Color(0.32, 0.58, 0.88),
}
const TIER_COLORS: Array = [
	Color(0.85, 0.85, 0.85),  # 0 hamlet
	Color(1.00, 1.00, 0.50),  # 1 village
	Color(1.00, 0.80, 0.20),  # 2 town
	Color(1.00, 0.50, 0.10),  # 3 city
	Color(1.00, 0.20, 0.20),  # 4 metropolis
]
const TIER_NAMES: Array = ["Hamlet", "Village", "Town", "City", "Metropolis"]

const ZOOM_MIN:  float = 0.5
const ZOOM_MAX:  float = 8.0
const ZOOM_STEP: float = 0.25

# ── State ─────────────────────────────────────────────────────────────────────
var _world_state:   WorldState    = null
var _economy_view:  EconomyView   = null
var _selected_sid:  String        = ""

# Map dimensions derived from world_tiles
var _map_w: int = 0
var _map_h: int = 0

# Click detection: tile Vector2i → settlement_id
var _tile_to_sid:  Dictionary = {}
# Click detection: tile Vector2i → world_tile cid string (bandit camps)
var _tile_to_camp: Dictionary = {}

# ── UI refs ───────────────────────────────────────────────────────────────────
var _map_texture: TextureRect      = null
var _map_scroll:  ScrollContainer  = null
var _zoom_level:  float            = 1.0
var _dragging:    bool             = false
var _drag_origin: Vector2          = Vector2.ZERO
var _drag_scroll: Vector2          = Vector2.ZERO

var _sim_bar:     Label            = null
var _pause_btn:   Button           = null
var _info_title:  Label            = null
var _info_body:   RichTextLabel    = null
var _enter_btn:   Button           = null  # "► ENTER SETTLEMENT" button
var _attack_btn:  Button           = null  # "⚔ ATTACK CAMP" button
var _selected_camp_cid: String     = ""    # world-tile cid of selected bandit camp


# ── Lifecycle ─────────────────────────────────────────────────────────────────
func _ready() -> void:
	set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)

	# Grab WorldState from Bootstrap.
	var boot: Node = get_node_or_null("/root/Bootstrap")
	if boot != null:
		_world_state = boot.world_state

	_build_ui()

	if _world_state != null:
		_render_map()
		_build_tile_index()
		_show_world_summary()

	# EconomyView overlay.
	_economy_view = EconomyView.new()
	get_tree().root.call_deferred("add_child", _economy_view)
	if _world_state != null:
		call_deferred("_setup_economy_view")

	SimulationClock.tick_completed.connect(_on_tick)
	_update_sim_bar()


func _setup_economy_view() -> void:
	if _economy_view != null and _world_state != null:
		_economy_view.setup(_world_state)


func _exit_tree() -> void:
	if SimulationClock.tick_completed.is_connected(_on_tick):
		SimulationClock.tick_completed.disconnect(_on_tick)
	if _economy_view != null and is_instance_valid(_economy_view):
		_economy_view.queue_free()


# ── UI builder ────────────────────────────────────────────────────────────────
func _build_ui() -> void:
	var split := HSplitContainer.new()
	split.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
	split.split_offset = 300
	add_child(split)

	# ── LEFT: Info & controls panel ───────────────────────────────────────────
	var left_scroll := ScrollContainer.new()
	left_scroll.custom_minimum_size = Vector2(280, 0)
	left_scroll.horizontal_scroll_mode = ScrollContainer.SCROLL_MODE_DISABLED
	split.add_child(left_scroll)

	var left := VBoxContainer.new()
	left.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	left_scroll.add_child(left)

	var title := Label.new()
	title.text = "WORLD VIEW"
	title.add_theme_font_size_override("font_size", 20)
	left.add_child(title)

	# Back button
	var back_btn := Button.new()
	back_btn.text = "◀ Back to World Gen"
	back_btn.pressed.connect(_on_back_pressed)
	left.add_child(back_btn)

	left.add_child(_hsep())

	# Simulation controls
	var sim_lbl := Label.new()
	sim_lbl.text = "── Simulation ──"
	sim_lbl.modulate = Color(0.6, 0.85, 1.0)
	left.add_child(sim_lbl)

	var sim_row := HBoxContainer.new()
	left.add_child(sim_row)

	_pause_btn = Button.new()
	_pause_btn.text = "⏸ Pause"
	_pause_btn.pressed.connect(_on_pause_pressed)
	sim_row.add_child(_pause_btn)

	for spd: float in [1.0, 2.0, 4.0, 8.0]:
		var sb := Button.new()
		sb.text = "%d×" % int(spd)
		sb.pressed.connect(func(): SimulationClock.set_speed(spd); _update_sim_bar())
		sim_row.add_child(sb)

	_sim_bar = Label.new()
	_sim_bar.text = "Tick: 0  |  1×  |  Running"
	_sim_bar.add_theme_font_size_override("font_size", 11)
	_sim_bar.modulate = Color(0.7, 1.0, 0.7)
	left.add_child(_sim_bar)

	left.add_child(_hsep())

	# Economy overlay toggle
	var eco_btn := Button.new()
	eco_btn.text = "F10 — Economy Overlay"
	eco_btn.pressed.connect(func(): if _economy_view != null: _economy_view._toggle())
	left.add_child(eco_btn)

	left.add_child(_hsep())

	# Settlement info pane
	var info_lbl := Label.new()
	info_lbl.text = "── Selected ──"
	info_lbl.modulate = Color(0.6, 0.85, 1.0)
	left.add_child(info_lbl)

	_info_title = Label.new()
	_info_title.text = "Click a settlement on the map"
	_info_title.modulate = Color(1.0, 0.85, 0.4)
	_info_title.add_theme_font_size_override("font_size", 13)
	_info_title.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	left.add_child(_info_title)

	_info_body = RichTextLabel.new()
	_info_body.bbcode_enabled = true
	_info_body.fit_content = true
	_info_body.scroll_active = false
	_info_body.add_theme_color_override("default_color", Color(0.85, 0.85, 0.85))
	_info_body.add_theme_font_size_override("normal_font_size", 11)
	left.add_child(_info_body)

	# Enter settlement button — only enabled when a settlement is selected.
	_enter_btn = Button.new()
	_enter_btn.text = "► ENTER SETTLEMENT"
	_enter_btn.disabled = true
	_enter_btn.custom_minimum_size = Vector2(0, 36)
	_enter_btn.pressed.connect(_on_enter_settlement_pressed)
	left.add_child(_enter_btn)

	# Attack camp button — only enabled when a bandit camp tile is selected.
	_attack_btn = Button.new()
	_attack_btn.text = "⚔ ATTACK CAMP"
	_attack_btn.disabled = true
	_attack_btn.visible = false
	_attack_btn.custom_minimum_size = Vector2(0, 36)
	_attack_btn.add_theme_color_override("font_color", Color(1.0, 0.3, 0.3))
	_attack_btn.pressed.connect(_on_attack_camp_pressed)
	left.add_child(_attack_btn)

	# ── RIGHT: Map ────────────────────────────────────────────────────────────
	var right := VBoxContainer.new()
	right.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	split.add_child(right)

	var hint := Label.new()
	hint.text = "Scroll=zoom  ·  Drag=pan  ·  Click=select settlement  ·  Space=pause  ·  F10=economy  ·  Esc=back"
	hint.add_theme_font_size_override("font_size", 11)
	hint.modulate = Color(0.6, 0.6, 0.6)
	right.add_child(hint)

	_map_scroll = ScrollContainer.new()
	_map_scroll.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	_map_scroll.size_flags_vertical   = Control.SIZE_EXPAND_FILL
	_map_scroll.gui_input.connect(_on_map_input)
	right.add_child(_map_scroll)

	_map_texture = TextureRect.new()
	_map_texture.stretch_mode   = TextureRect.STRETCH_SCALE
	_map_texture.texture_filter = CanvasItem.TEXTURE_FILTER_NEAREST
	_map_scroll.add_child(_map_texture)

	# Legend
	right.add_child(_build_legend())


# ── Map rendering ─────────────────────────────────────────────────────────────
func _render_map() -> void:
	if _world_state == null:
		return

	# Determine map extents from world_tiles.
	var max_x: int = 0
	var max_y: int = 0
	for cell: Dictionary in _world_state.world_tiles.values():
		max_x = maxi(max_x, int(cell.get("grid_x", 0)))
		max_y = maxi(max_y, int(cell.get("grid_y", 0)))
	_map_w = max_x + 1
	_map_h = max_y + 1
	if _map_w <= 0 or _map_h <= 0:
		return

	var img := Image.create(_map_w, _map_h, false, Image.FORMAT_RGB8)

	# Paint terrain.
	for cell: Dictionary in _world_state.world_tiles.values():
		var gx: int = int(cell.get("grid_x", 0))
		var gy: int = int(cell.get("grid_y", 0))
		var terrain: String = cell.get("terrain_type", "plains")
		var col: Color = TERRAIN_COLORS.get(terrain, Color(0.5, 0.5, 0.5))
		img.set_pixel(gx, gy, col)

	# Paint roads from route paths.
	var ROAD_COLOR := Color(0.50, 0.38, 0.20)   # earthy brown
	var drawn_pairs: Dictionary = {}             # avoid double-drawing
	for sid: String in _world_state.routes.keys():
		var edges: Array = _world_state.routes[sid]
		for edge: Dictionary in edges:
			# Deduplicate: only draw each undirected pair once.
			var to_id: String = edge.get("to_id", "")
			var pair_key: String = "%s<>%s" % [sid, to_id] if sid < to_id \
					else "%s<>%s" % [to_id, sid]
			if drawn_pairs.has(pair_key):
				continue
			drawn_pairs[pair_key] = true
			var path: Array = edge.get("path", [])
			for step in path:
				var rx: int
				var ry: int
				if step is Array and step.size() >= 2:
					rx = int(step[0])
					ry = int(step[1])
				elif step is Vector2i:
					rx = step.x
					ry = step.y
				else:
					continue
				if rx >= 0 and rx < _map_w and ry >= 0 and ry < _map_h:
					img.set_pixel(rx, ry, ROAD_COLOR)

	# Paint settlement dots (radius by tier).
	for sid: String in _world_state.settlements.keys():
		var sdata: Dictionary = _world_state.get_settlement_dict(sid)
		var tx: int  = int(sdata.get("tile_x", -1))
		var ty: int  = int(sdata.get("tile_y", -1))
		if tx < 0 or ty < 0:
			continue
		var tier:   int   = int(sdata.get("tier", 0))
		var radius: int   = 1 + tier
		var col:    Color = TIER_COLORS[clampi(tier, 0, TIER_COLORS.size() - 1)]
		for dy: int in range(-radius, radius + 1):
			for dx: int in range(-radius, radius + 1):
				if dx * dx + dy * dy <= radius * radius:
					img.set_pixel(
						clampi(tx + dx, 0, _map_w - 1),
						clampi(ty + dy, 0, _map_h - 1),
						col
					)

	_map_texture.texture = ImageTexture.create_from_image(img)
	_zoom_level = 1.0
	_apply_zoom()


func _build_tile_index() -> void:
	_tile_to_sid.clear()
	for sid: String in _world_state.settlements.keys():
		var sdata: Dictionary = _world_state.get_settlement_dict(sid)
		var tx: int = int(sdata.get("tile_x", -1))
		var ty: int = int(sdata.get("tile_y", -1))
		if tx >= 0 and ty >= 0:
			_tile_to_sid[Vector2i(tx, ty)] = sid

	# Index bandit camp world tiles.
	_tile_to_camp.clear()
	for cid: String in _world_state.world_tiles:
		var cell: Dictionary = _world_state.world_tiles[cid]
		if cell.get("hostile", false) and cell.get("building_id", "") == "bandit_camp":
			var parts := cid.split(",")
			if parts.size() == 2:
				_tile_to_camp[Vector2i(int(parts[0]), int(parts[1]))] = cid


# ── Settlement info ───────────────────────────────────────────────────────────
func _show_world_summary() -> void:
	if _world_state == null:
		return
	_info_title.text = "World: %s" % _world_state.region_id
	_info_body.text = (
		"Seed: %d\nSettlements: %d\nProvinces: %d\n\nClick a dot to inspect a settlement."
	) % [_world_state.world_seed, _world_state.settlements.size(), _world_state.province_names.size()]


func _show_settlement(sid: String) -> void:
	_selected_sid      = sid
	_selected_camp_cid = ""   # deselect any camp
	var sv = _world_state.settlements.get(sid)
	if sv == null:
		return
	var ss: SettlementState
	if sv is SettlementState:
		ss = sv
	else:
		ss = SettlementState.from_dict(sv)

	var tier_name: String = TIER_NAMES[clampi(ss.tier, 0, TIER_NAMES.size() - 1)]
	_info_title.text = "%s  (%s)" % [ss.name, tier_name]
	# Enable the enter button; hide the attack button.
	if _enter_btn != null:
		_enter_btn.disabled = false
	if _attack_btn != null:
		_attack_btn.disabled = true
		_attack_btn.visible  = false

	var pop: int = ss.total_population()
	var lines: PackedStringArray = []
	lines.append("[color=#88aacc]Pop:[/color] %d   [color=#88aacc]Tier:[/color] %d" % [pop, ss.tier])
	lines.append("[color=#88dd88]Prosperity:[/color] %.3f   [color=#dd8888]Unrest:[/color] %.3f" % [ss.prosperity, ss.unrest])
	lines.append("[color=#888888]Province:[/color] %s" % ss.province_id)
	lines.append("")
	lines.append("[color=#ccaa44]── Inventory ──[/color]")

	var goods: Array = ss.inventory.keys()
	goods.sort()
	for good: String in goods:
		var qty: float   = float(ss.inventory[good])
		var price: float = float(ss.prices.get(good, 0.0))
		var shortage_marker: String = "  [color=#ff5555]⚠ shortage[/color]" if ss.shortages.get(good, 0.0) > 0.1 else ""
		lines.append("  [color=#dddddd]%s[/color]  [color=#ffffff]%.0f[/color]  [color=#6688bb]@%.2f[/color]%s" % [good, qty, price, shortage_marker])

	_info_body.text = "\n".join(lines)


# ── Zoom / pan ────────────────────────────────────────────────────────────────
func _apply_zoom() -> void:
	if _map_texture == null or _map_texture.texture == null:
		return
	var new_w: float = _map_texture.texture.get_width()  * _zoom_level
	var new_h: float = _map_texture.texture.get_height() * _zoom_level
	_map_texture.custom_minimum_size = Vector2(new_w, new_h)
	_map_texture.size                = Vector2(new_w, new_h)


func _zoom_reset() -> void:
	_zoom_level = 1.0
	_apply_zoom()
	if _map_scroll:
		_map_scroll.scroll_horizontal = 0
		_map_scroll.scroll_vertical   = 0


# ── Map input ─────────────────────────────────────────────────────────────────
func _on_map_input(event: InputEvent) -> void:
	if event is InputEventMouseButton:
		var mbe := event as InputEventMouseButton
		match mbe.button_index:
			MOUSE_BUTTON_WHEEL_UP:
				if mbe.pressed:
					_zoom_level = clampf(_zoom_level + ZOOM_STEP, ZOOM_MIN, ZOOM_MAX)
					_apply_zoom()
					get_viewport().set_input_as_handled()
			MOUSE_BUTTON_WHEEL_DOWN:
				if mbe.pressed:
					_zoom_level = clampf(_zoom_level - ZOOM_STEP, ZOOM_MIN, ZOOM_MAX)
					_apply_zoom()
					get_viewport().set_input_as_handled()
			MOUSE_BUTTON_LEFT:
				if mbe.pressed:
					_drag_origin = mbe.global_position
					_drag_scroll = Vector2(_map_scroll.scroll_horizontal, _map_scroll.scroll_vertical)
					_dragging    = true
					get_viewport().set_input_as_handled()
				else:
					# Only treat as a click if we didn't move much.
					var moved: float = mbe.global_position.distance_to(_drag_origin)
					if moved < 5.0:
						_handle_map_click(mbe.position)
					_dragging = false

	elif event is InputEventMouseMotion:
		if _dragging:
			var delta: Vector2 = (event as InputEventMouseMotion).global_position - _drag_origin
			_map_scroll.scroll_horizontal = int(_drag_scroll.x - delta.x)
			_map_scroll.scroll_vertical   = int(_drag_scroll.y - delta.y)
			get_viewport().set_input_as_handled()


func _handle_map_click(local_pos: Vector2) -> void:
	if _world_state == null:
		return
	# Convert scroll-container-local position to tile coordinates.
	var world_x: float = (local_pos.x + _map_scroll.scroll_horizontal) / _zoom_level
	var world_y: float = (local_pos.y + _map_scroll.scroll_vertical)   / _zoom_level
	var tile: Vector2i = Vector2i(int(world_x), int(world_y))

	# ── Check bandit camps first (exact or near-exact match) ──────────────
	var best_camp_cid:  String = ""
	var best_camp_dist: float  = 999.0
	for search_tile: Vector2i in _tile_to_camp.keys():
		var d: float = float(tile.distance_to(search_tile))
		if d < best_camp_dist:
			best_camp_dist = d
			best_camp_cid  = _tile_to_camp[search_tile]
	if best_camp_cid != "" and best_camp_dist <= 2.0:
		_show_camp_tile(best_camp_cid, _world_state.world_tiles[best_camp_cid])
		return

	# ── Find the nearest settlement within a search radius ────────────────
	var best_sid:  String = ""
	var best_dist: float  = 999.0
	for search_tile: Vector2i in _tile_to_sid.keys():
		var d: float = float(tile.distance_to(search_tile))
		if d < best_dist:
			best_dist = d
			best_sid  = _tile_to_sid[search_tile]

	if best_sid != "" and best_dist <= 8.0:
		_show_settlement(best_sid)


# ── Keyboard ──────────────────────────────────────────────────────────────────
func _unhandled_key_input(event: InputEvent) -> void:
	if not (event is InputEventKey and (event as InputEventKey).pressed):
		return
	match (event as InputEventKey).keycode:
		KEY_ESCAPE:
			_on_back_pressed()
		KEY_SPACE:
			_on_pause_pressed()
		KEY_EQUAL, KEY_KP_ADD:
			_zoom_level = clampf(_zoom_level + ZOOM_STEP, ZOOM_MIN, ZOOM_MAX)
			_apply_zoom()
		KEY_MINUS, KEY_KP_SUBTRACT:
			_zoom_level = clampf(_zoom_level - ZOOM_STEP, ZOOM_MIN, ZOOM_MAX)
			_apply_zoom()
		KEY_0, KEY_KP_0, KEY_F:
			_zoom_reset()
		KEY_LEFT:
			if _map_scroll: _map_scroll.scroll_horizontal -= 64
		KEY_RIGHT:
			if _map_scroll: _map_scroll.scroll_horizontal += 64
		KEY_UP:
			if _map_scroll: _map_scroll.scroll_vertical -= 64
		KEY_DOWN:
			if _map_scroll: _map_scroll.scroll_vertical += 64
		KEY_F10:
			if _economy_view != null:
				_economy_view._toggle()


# ── Sim controls ──────────────────────────────────────────────────────────────
func _on_pause_pressed() -> void:
	SimulationClock.toggle_pause()
	_update_sim_bar()


func _on_tick(_tick: int) -> void:
	_update_sim_bar()
	# Refresh selected settlement info every tick so inventory stays live.
	if _selected_sid != "" and _world_state != null:
		_show_settlement(_selected_sid)


func _update_sim_bar() -> void:
	if _sim_bar == null:
		return
	var state_str: String = "Paused" if SimulationClock.is_paused() else "Running"
	_sim_bar.text = "Tick: %d  |  %d×  |  %s" % [
		SimulationClock.get_tick(), int(SimulationClock.get_speed()), state_str
	]
	if _pause_btn != null:
		_pause_btn.text = "▶ Resume" if SimulationClock.is_paused() else "⏸ Pause"


# ── Navigation ────────────────────────────────────────────────────────────────
func _on_back_pressed() -> void:
	if _economy_view != null and is_instance_valid(_economy_view):
		_economy_view.queue_free()
		_economy_view = null
	SimulationClock.pause()
	SceneManager.pop_scene()


func _on_enter_settlement_pressed() -> void:
	if _selected_sid == "":
		return
	SceneManager.push_scene(
		"res://src/ui/settlement_view.tscn",
		{"settlement_id": _selected_sid}
	)


func _show_camp_tile(cid: String, tile_data: Dictionary) -> void:
	_selected_sid      = ""   # deselect any settlement
	_selected_camp_cid = cid

	if _enter_btn != null:
		_enter_btn.disabled = true
	if _attack_btn != null:
		_attack_btn.disabled = false
		_attack_btn.visible  = true

	var group_size: int    = int(tile_data.get("bandit_group_size", 0))
	var gear_tier:  String = tile_data.get("bandit_gear_tier", "unknown")
	var parts              = cid.split(",")
	var tx: int = int(parts[0]) if parts.size() == 2 else 0
	var ty: int = int(parts[1]) if parts.size() == 2 else 0

	_info_title.text = "⚔ Bandit Camp  [%d, %d]" % [tx, ty]
	_info_body.text  = (
		"Hostile encampment outside any settled territory.\n\n"
		+ "Estimated fighters: %d\nGear quality: %s\n\n"
		+ "Attacking will trigger a tactical battle.\n"
		+ "Victory clears the camp permanently."
	) % [group_size, gear_tier]


func _on_attack_camp_pressed() -> void:
	if _selected_camp_cid == "" or _world_state == null:
		return
	_trigger_camp_combat(_selected_camp_cid)


func _trigger_camp_combat(cid: String) -> void:
	var tile_data: Dictionary = _world_state.world_tiles.get(cid, {})
	var group_size: int    = int(tile_data.get("bandit_group_size", 4))
	var gear_tier:  String = tile_data.get("bandit_gear_tier", "ragged")

	# ── Create combatants ──────────────────────────────────────────────────
	var battle := BattleState.new()
	battle.battle_id  = "camp_%s" % cid.replace(",", "_")
	battle.map_type   = "subregion"
	battle.map_tile   = cid
	battle.phase      = BattleState.PHASE_PLANNING
	battle.turn       = 0

	# Player side — player character + any followers.
	var player_member_ids: Array[String] = []
	var player_person: PersonState = _world_state.characters.get(
		_world_state.player_character_id)
	if player_person != null:
		var pc := CombatantState.from_person(player_person, "player", "p_main")
		pc.tile_pos = Vector2i(12, 5)   # south edge of a 25×25 grid
		battle.combatants[pc.combatant_id] = pc
		player_member_ids.append(pc.combatant_id)

		# Add followers to player's formation.
		var fol_x: int = 0
		for fid: String in player_person.follower_ids:
			var follower: PersonState = _world_state.characters.get(fid)
			if follower == null:
				continue
			var fc := CombatantState.from_person(follower, "player", "p_main")
			fc.tile_pos = Vector2i(10 + (fol_x % 5), 4 - (fol_x / 5))
			battle.combatants[fc.combatant_id] = fc
			player_member_ids.append(fc.combatant_id)
			fol_x += 1

	var player_formation := FormationState.make(
		"p_main", "player", "Your Party", player_member_ids, Vector2i(12, 5))
	player_formation.order = FormationState.ORDER_ADVANCE
	battle.formations[player_formation.formation_id] = player_formation

	# Enemy side — spawn group_size bandits with gear seeded from gear_tier.
	var weapon_pool_ragged:  Array = ["club", "dagger"]
	var weapon_pool_modest:  Array = ["short_sword", "axe", "club"]
	var armor_pool_ragged:   Array = ["gambeson", "leather_vest"]
	var armor_pool_modest:   Array = ["gambeson", "leather_vest", "mail_hauberk"]

	var wpn_pool: Array = weapon_pool_ragged if gear_tier == "ragged" else weapon_pool_modest
	var arm_pool: Array = armor_pool_ragged  if gear_tier == "ragged" else armor_pool_modest

	var rng := RandomNumberGenerator.new()
	rng.seed = hash(cid) ^ _world_state.world_seed

	var enemy_member_ids: Array[String] = []
	for i: int in range(group_size):
		var ep := PersonState.new()
		var eid := "bandit_%s_%d" % [cid.replace(",", "_"), i]
		ep.person_id      = eid
		ep.name           = "Bandit %d" % (i + 1)
		ep.stamina        = 1.0
		ep.equipment_refs = {
			"main_hand": wpn_pool[rng.randi() % wpn_pool.size()],
			"torso":     arm_pool[rng.randi() % arm_pool.size()],
		}
		_world_state.characters[eid] = ep

		var ec := CombatantState.from_person(ep, "enemy", "e_main")
		var spread_x: int = (i % 5)
		@warning_ignore("INTEGER_DIVISION")
		var spread_y: int = (i / 5)
		ec.tile_pos = Vector2i(10 + spread_x, 19 - spread_y)
		battle.combatants[ec.combatant_id] = ec
		enemy_member_ids.append(ec.combatant_id)

	var enemy_formation := FormationState.make(
		"e_main", "enemy", "Bandit Camp", enemy_member_ids, Vector2i(12, 19))
	enemy_formation.order = FormationState.ORDER_ADVANCE
	battle.formations[enemy_formation.formation_id] = enemy_formation

	# ── Set active battle and open CombatView ─────────────────────────────
	_world_state.active_battle = battle
	SceneManager.push_scene(
		"res://src/ui/combat_view/combat_view.tscn",
		{"battle_id": battle.battle_id}
	)


# ── Helpers ───────────────────────────────────────────────────────────────────
func _hsep() -> HSeparator:
	return HSeparator.new()


func _build_legend() -> Control:
	var flow := HFlowContainer.new()
	flow.custom_minimum_size = Vector2(0, 22)
	for terrain: String in TERRAIN_COLORS:
		var chip := ColorRect.new()
		chip.color = TERRAIN_COLORS[terrain]
		chip.custom_minimum_size = Vector2(10, 10)
		flow.add_child(chip)
		var lbl := Label.new()
		lbl.text = terrain + "  "
		lbl.add_theme_font_size_override("font_size", 10)
		flow.add_child(lbl)
	return flow
