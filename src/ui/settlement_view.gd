## SettlementView — world-traversal view at region-cell resolution.
##
## Entered via SceneManager.push_scene("res://src/ui/settlement_view.tscn",
##   {"settlement_id": "…"}).
## Exits back to WorldView via SceneManager.pop_scene() (Esc key).
##
## Renders a fixed VIEWPORT_RADIUS-cell window around the player as 64×64
## coloured tiles.  The player can walk freely across the entire world —
## no border stops them.  The left panel auto-switches to the settlement
## whose territory the player enters, or shows "Wilderness" on open land.
## No art assets required — buildings and terrain use flat colour + label.
class_name SettlementView
extends Control

# ── Layout constants ───────────────────────────────────────────────────────────
const ZOOM_STEPS: Array[int] = [32, 48, 64, 80, 96]
const PANEL_W          := 260
## Cells visible in each direction from the player (viewport = 2R+1 square).
const VIEWPORT_RADIUS  := 8

# ── Colour palette ─────────────────────────────────────────────────────────────
## Building type → tile background colour
const BUILDING_COLORS: Dictionary = {
	"farm_plot":    Color(0.52, 0.76, 0.28),
	"inn":          Color(0.72, 0.52, 0.28),
	"well":         Color(0.30, 0.62, 0.90),
	"granary":      Color(0.90, 0.78, 0.38),
	"market_stall": Color(0.95, 0.62, 0.18),
	"derelict":     Color(0.52, 0.48, 0.42),
	"open_land":    Color(0.74, 0.80, 0.58),
}
## Terrain type → fallback colour (used when no building is placed)
const TERRAIN_COLORS: Dictionary = {
	"plains":   Color(0.76, 0.88, 0.52),
	"coast":    Color(0.68, 0.84, 0.72),
	"hills":    Color(0.70, 0.64, 0.44),
	"forest":   Color(0.22, 0.55, 0.25),
	"desert":   Color(0.96, 0.88, 0.54),
	"tundra":   Color(0.82, 0.86, 0.88),
	"mountain": Color(0.62, 0.60, 0.65),
	"river":    Color(0.40, 0.65, 0.95),
}
const COLOR_PLAYER    := Color(1.0, 1.0, 1.0)
const COLOR_CURSOR    := Color(1.0, 0.92, 0.30, 0.65)
const COLOR_ROAD      := Color(0.72, 0.66, 0.48)
const COLOR_PANEL_BG  := Color(0.12, 0.12, 0.14)
const COLOR_TEXT      := Color(0.90, 0.90, 0.90)
const COLOR_DIM       := Color(0.55, 0.55, 0.60)
const COLOR_SEL       := Color(0.35, 0.65, 1.00)

# ── State ──────────────────────────────────────────────────────────────────────
var _world_state:    WorldState    = null
var _settlement_id:  String        = ""
var _ss:             SettlementState = null
var _work_system:    WorkSystem    = null   ## Retrieved from Bootstrap on entry.

## Context tag for the single interaction button.
## Format: "apply_work:N" | "leave_work" | "rent_inn" | "claim_shelter" | ""
var _pending_interaction: String = ""
## World tile coordinates in the 512×512 grid where the player is standing.
var _wt_x: int = 0
var _wt_y: int = 0

## Region cell coordinates within the current world tile (0–249 each axis).
var _player_rx: int = SubRegionGenerator.CX
var _player_ry: int = SubRegionGenerator.CY

## Current z-level: 0 = ground, 1 = upper, -1 = cellar.
var _current_z: int = 0

## Zoom: current tile pixel size and index into ZOOM_STEPS.
var _cell_px:  int = 64
var _zoom_idx: int = 2

## Flat arrays of the (2R+1)² viewport tile nodes, row-major (slot_y * dim + slot_x).
var _viewport_rects:  Array = []
var _viewport_labels: Array = []

# ── Node refs (built in _ready) ────────────────────────────────────────────────
var _clip_box:    Control     = null   ## Clipping container for the map area
var _map_area:    Control     = null   ## Container that holds tile rects and the pawn
var _player_rect: ColorRect  = null    ## Player pawn visual
var _cursor_rect: ColorRect  = null    ## Yellow highlight on player's cell
var _panel_title: Label      = null
var _panel_body:  RichTextLabel = null
var _cell_info:   RichTextLabel = null
var _floor_label: Label         = null   ## Shows current z-level name
var _player_info: RichTextLabel = null   ## Coin + needs readout
var _interact_btn: Button       = null   ## Context-sensitive action button
var _dialogue_panel: Control    = null   ## Full-screen dialogue overlay (P3-14)
var _dlg_body:    RichTextLabel = null   ## Dialogue NPC text
var _dlg_options: VBoxContainer = null   ## Dialogue choice buttons


# ── Lifecycle ──────────────────────────────────────────────────────────────────
func _ready() -> void:
	_load_state()
	_build_ui()
	_render_cells()
	_place_player_at_start()
	_update_viewport_content()  # fill tiles now that player position is known
	_refresh_panel()
	_refresh_player_info()
	_update_floor_label()


func _load_state() -> void:
	var boot := get_node_or_null("/root/Bootstrap")
	if boot:
		_world_state = boot.get("world_state")

	var params := SceneManager.take_params()
	_settlement_id = params.get("settlement_id", "")

	# Fallback: restore from player_location if params were empty (e.g. popped back from LocalView).
	if _settlement_id == "" and _world_state != null:
		_settlement_id = _world_state.player_location.get("settlement_id", "")

	if _world_state != null and _settlement_id != "":
		_ss = _world_state.get_settlement(_settlement_id)

	if _ss == null:
		push_error("SettlementView: no settlement found for id '%s'" % _settlement_id)
		return

	# Populate NPC pool for this settlement.
	var boot2 := get_node_or_null("/root/Bootstrap")
	var ws_seed: int = _world_state.world_seed if _world_state != null else 0
	if _world_state == null and boot2:
		_world_state = boot2.get("world_state")
	if _world_state != null and _ss != null:
		NpcPoolManager.populate(_world_state, _ss, ws_seed)

	# Restore world tile from saved player location; fall back to settlement anchor.
	if _world_state != null:
		var loc: Dictionary = _world_state.player_location
		var saved_sid: String = loc.get("settlement_id", "")
		if saved_sid == _settlement_id and loc.has("wt_x"):
			_wt_x = loc.get("wt_x", _ss.tile_x)
			_wt_y = loc.get("wt_y", _ss.tile_y)
		else:
			_wt_x = _ss.tile_x
			_wt_y = _ss.tile_y
	else:
		_wt_x = _ss.tile_x
		_wt_y = _ss.tile_y

	# Grab the WorkSystem reference registered by Bootstrap.
	var boot3 := get_node_or_null("/root/Bootstrap")
	if boot3 != null:
		_work_system = boot3.get("_work_system")


func _input(event: InputEvent) -> void:
	if event is InputEventKey and event.pressed:
		# When dialogue is open, only allow closing it.
		if _dialogue_panel != null and _dialogue_panel.visible:
			if event.keycode in [KEY_ESCAPE, KEY_F, KEY_T]:
				_close_dialogue()
			return
		match event.keycode:
			KEY_ESCAPE:
				_exit_view()
			KEY_UP, KEY_W:
				_try_move(0, -1)
			KEY_DOWN, KEY_S:
				_try_move(0, 1)
			KEY_LEFT, KEY_A:
				_try_move(-1, 0)
			KEY_RIGHT, KEY_D:
				_try_move(1, 0)
			KEY_Q:
				_try_move(-1, -1)
			KEY_E:
				_try_move(1, -1)
			KEY_Z:
				_try_move(-1, 1)
			KEY_C:
				_try_move(1, 1)
			KEY_PAGEUP:
				_try_change_zlevel(1)
				get_viewport().set_input_as_handled()
			KEY_PAGEDOWN:
				_try_change_zlevel(-1)
				get_viewport().set_input_as_handled()
			KEY_F, KEY_T:
				_open_dialogue()
			KEY_ENTER, KEY_KP_ENTER:
				if _interact_btn != null and _interact_btn.visible:
					_on_interact_pressed()
			KEY_EQUAL, KEY_KP_ADD:
				_change_zoom(1)
				get_viewport().set_input_as_handled()
			KEY_MINUS, KEY_KP_SUBTRACT:
				_change_zoom(-1)
				get_viewport().set_input_as_handled()


# ── UI construction ─────────────────────────────────────────────────────────────
func _build_ui() -> void:
	var bg := ColorRect.new()
	bg.color = Color(0.08, 0.08, 0.10)
	bg.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
	add_child(bg)

	var hbox := HBoxContainer.new()
	hbox.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
	hbox.add_theme_constant_override("separation", 0)
	add_child(hbox)

	# ── Left panel ─────────────────────────────────────────────────────────
	var panel_bg := ColorRect.new()
	panel_bg.color = COLOR_PANEL_BG
	panel_bg.custom_minimum_size = Vector2(PANEL_W, 0)
	hbox.add_child(panel_bg)

	var panel_vbox := VBoxContainer.new()
	panel_vbox.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
	panel_vbox.add_theme_constant_override("separation", 6)
	panel_bg.add_child(panel_vbox)

	var back_btn := Button.new()
	back_btn.text = "◀ Back to World"
	back_btn.custom_minimum_size = Vector2(0, 32)
	back_btn.pressed.connect(_exit_view)
	panel_vbox.add_child(back_btn)

	panel_vbox.add_child(_make_sep())

	_panel_title = Label.new()
	_panel_title.text = "…"
	_panel_title.add_theme_font_size_override("font_size", 15)
	_panel_title.add_theme_color_override("font_color", Color(1.0, 0.85, 0.4))
	_panel_title.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	panel_vbox.add_child(_panel_title)

	_panel_body = RichTextLabel.new()
	_panel_body.bbcode_enabled = true
	_panel_body.fit_content = true
	_panel_body.scroll_active = false
	_panel_body.add_theme_color_override("default_color", COLOR_TEXT)
	_panel_body.add_theme_font_size_override("normal_font_size", 11)
	panel_vbox.add_child(_panel_body)

	panel_vbox.add_child(_make_sep())

	var cell_hdr := Label.new()
	cell_hdr.text = "── Current Cell ──"
	cell_hdr.add_theme_color_override("font_color", COLOR_SEL)
	cell_hdr.add_theme_font_size_override("font_size", 11)
	panel_vbox.add_child(cell_hdr)

	_cell_info = RichTextLabel.new()
	_cell_info.bbcode_enabled = true
	_cell_info.fit_content = true
	_cell_info.scroll_active = false
	_cell_info.add_theme_color_override("default_color", COLOR_TEXT)
	_cell_info.add_theme_font_size_override("normal_font_size", 11)
	panel_vbox.add_child(_cell_info)

	# Floor indicator
	_floor_label = Label.new()
	_floor_label.add_theme_color_override("font_color", Color(0.80, 0.80, 1.00))
	_floor_label.add_theme_font_size_override("font_size", 11)
	panel_vbox.add_child(_floor_label)

	panel_vbox.add_child(_make_sep())

	var hint := Label.new()
	hint.text = "WASD/Arrows = move\nDiagonals: Q E Z C\nPgUp/PgDn = floor\nF/T = talk  ↵ = interact\n+/- = zoom  Esc = back"
	hint.add_theme_color_override("font_color", COLOR_DIM)
	hint.add_theme_font_size_override("font_size", 10)
	panel_vbox.add_child(hint)

	panel_vbox.add_child(_make_sep())

	# ── Player status ──────────────────────────────────────────────────────
	var player_hdr := Label.new()
	player_hdr.text = "── Player ──"
	player_hdr.add_theme_color_override("font_color", COLOR_SEL)
	player_hdr.add_theme_font_size_override("font_size", 11)
	panel_vbox.add_child(player_hdr)

	_player_info = RichTextLabel.new()
	_player_info.bbcode_enabled = true
	_player_info.fit_content = true
	_player_info.scroll_active = false
	_player_info.add_theme_color_override("default_color", COLOR_TEXT)
	_player_info.add_theme_font_size_override("normal_font_size", 11)
	panel_vbox.add_child(_player_info)

	panel_vbox.add_child(_make_sep())

	# ── Context interaction button ─────────────────────────────────────────
	_interact_btn = Button.new()
	_interact_btn.text = ""
	_interact_btn.visible = false
	_interact_btn.custom_minimum_size = Vector2(0, 28)
	_interact_btn.pressed.connect(_on_interact_pressed)
	panel_vbox.add_child(_interact_btn)

	# ── Right: dynamic-size viewport clip box ───────────────────────────────
	# The player is always centred; tile size is driven by _cell_px.
	_clip_box = Control.new()
	_clip_box.clip_contents = true
	_clip_box.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	_clip_box.size_flags_vertical   = Control.SIZE_EXPAND_FILL
	hbox.add_child(_clip_box)

	_map_area = Control.new()
	_map_area.mouse_filter = Control.MOUSE_FILTER_IGNORE
	_clip_box.add_child(_map_area)

	_build_dialogue_panel()


func _build_dialogue_panel() -> void:
	# Semi-transparent full-screen backdrop.
	_dialogue_panel = Control.new()
	_dialogue_panel.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
	_dialogue_panel.visible = false
	add_child(_dialogue_panel)

	var backdrop := ColorRect.new()
	backdrop.color = Color(0.0, 0.0, 0.0, 0.72)
	backdrop.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
	_dialogue_panel.add_child(backdrop)

	# Centred dialogue box.
	var box := PanelContainer.new()
	box.set_anchors_preset(Control.PRESET_CENTER)
	box.custom_minimum_size = Vector2(480, 320)
	box.offset_left   = -240
	box.offset_top    = -160
	box.offset_right  =  240
	box.offset_bottom =  160
	_dialogue_panel.add_child(box)

	var vbox := VBoxContainer.new()
	vbox.add_theme_constant_override("separation", 10)
	box.add_child(vbox)

	_dlg_body = RichTextLabel.new()
	_dlg_body.bbcode_enabled = true
	_dlg_body.custom_minimum_size = Vector2(0, 180)
	_dlg_body.add_theme_font_size_override("normal_font_size", 12)
	vbox.add_child(_dlg_body)

	var sep := HSeparator.new()
	vbox.add_child(sep)

	_dlg_options = VBoxContainer.new()
	_dlg_options.add_theme_constant_override("separation", 4)
	vbox.add_child(_dlg_options)


func _make_sep() -> HSeparator:
	var sep := HSeparator.new()
	sep.add_theme_color_override("color", Color(0.25, 0.25, 0.28))
	return sep


# ── Map rendering ─────────────────────────────────────────────────────────────
## Tear down and recreate all tile nodes at the new _cell_px size.
func _rebuild_grid() -> void:
	# Free all existing tile + pawn nodes.
	for child in _map_area.get_children():
		child.queue_free()
	_viewport_rects.clear()
	_viewport_labels.clear()
	_cursor_rect = null
	_player_rect = null
	_render_cells()
	_update_viewport_content()
	_update_pawn_visual()


func _change_zoom(delta: int) -> void:
	_zoom_idx = clamp(_zoom_idx + delta, 0, ZOOM_STEPS.size() - 1)
	_cell_px   = ZOOM_STEPS[_zoom_idx]
	_rebuild_grid()


func _render_cells() -> void:
	_viewport_rects.clear()
	_viewport_labels.clear()

	# Build a (2R+1)² grid of ColorRect nodes sized by _cell_px.
	# Content is set by _update_viewport_content() once player position is known.
	var dim := 2 * VIEWPORT_RADIUS + 1
	_map_area.custom_minimum_size = Vector2(dim * _cell_px, dim * _cell_px)

	for slot_y: int in range(dim):
		for slot_x: int in range(dim):
			var px := slot_x * _cell_px
			var py := slot_y * _cell_px

			var rect := ColorRect.new()
			rect.position = Vector2(px + 1, py + 1)
			rect.size     = Vector2(_cell_px - 2, _cell_px - 2)
			rect.color    = Color(0.08, 0.08, 0.10)  # default void colour
			_map_area.add_child(rect)
			_viewport_rects.append(rect)

			var lbl := Label.new()
			lbl.text = ""
			lbl.add_theme_font_size_override("font_size", 9)
			lbl.add_theme_color_override("font_color", Color(0, 0, 0, 0.7))
			lbl.position = Vector2(2, 2)
			lbl.size     = Vector2(_cell_px - 4, _cell_px - 4)
			rect.add_child(lbl)
			_viewport_labels.append(lbl)

	# Cursor rect (yellow highlight on player's tile) — above cells, below pawn
	_cursor_rect = ColorRect.new()
	_cursor_rect.size    = Vector2(_cell_px - 2, _cell_px - 2)
	_cursor_rect.color   = COLOR_CURSOR
	_cursor_rect.mouse_filter = Control.MOUSE_FILTER_IGNORE
	_map_area.add_child(_cursor_rect)

	# Player pawn — always at the viewport centre slot
	_player_rect = ColorRect.new()
	_player_rect.size  = Vector2(24, 24)
	_player_rect.color = COLOR_PLAYER
	_player_rect.mouse_filter = Control.MOUSE_FILTER_IGNORE
	_map_area.add_child(_player_rect)

	var pawn_lbl := Label.new()
	pawn_lbl.text = "@"
	pawn_lbl.add_theme_font_size_override("font_size", 14)
	pawn_lbl.add_theme_color_override("font_color", Color(0.1, 0.1, 0.1))
	pawn_lbl.position = Vector2(4, 2)
	_player_rect.add_child(pawn_lbl)


## Update every viewport slot based on the region grid of the current world tile.
## Cells outside the 250×250 region bounds show the adjacent world tile's
## basic terrain at reduced brightness.
func _update_viewport_content() -> void:
	if _world_state == null:
		return
	var dim := 2 * VIEWPORT_RADIUS + 1
	var region := _get_or_gen_region(_wt_x, _wt_y)

	for slot_y: int in range(dim):
		for slot_x: int in range(dim):
			var dx := slot_x - VIEWPORT_RADIUS
			var dy := slot_y - VIEWPORT_RADIUS
			var look_rx := _player_rx + dx
			var look_ry := _player_ry + dy

			var i := slot_y * dim + slot_x
			var rect: ColorRect = _viewport_rects[i]
			var lbl:  Label     = _viewport_labels[i]

			# Outside the 250×250 region — show faded adjacent world tile terrain.
			if look_rx < 0 or look_rx >= SubRegionGenerator.REGION_W or \
			   look_ry < 0 or look_ry >= SubRegionGenerator.REGION_H:
				var adj_wtx := _wt_x
				var adj_wty := _wt_y
				if look_rx < 0:
					adj_wtx -= 1
				elif look_rx >= SubRegionGenerator.REGION_W:
					adj_wtx += 1
				if look_ry < 0:
					adj_wty -= 1
				elif look_ry >= SubRegionGenerator.REGION_H:
					adj_wty += 1
				var adj_key := "%d,%d" % [adj_wtx, adj_wty]
				var adj_wt: Dictionary = _world_state.world_tiles.get(adj_key, {})
				if adj_wt.is_empty():
					rect.color = Color(0.05, 0.05, 0.05)
				else:
					var adj_t: String = adj_wt.get("terrain_type", "plains")
					rect.color = TERRAIN_COLORS.get(adj_t, Color(0.5, 0.5, 0.5)).darkened(0.35)
				lbl.text = ""
				continue

			var cid := "%d,%d" % [look_rx, look_ry]
			if not region.has(cid):
				rect.color = Color(0.05, 0.05, 0.05)
				lbl.text   = ""
				continue

			var cell_data: Dictionary = region[cid]
			if cell_data.get("is_water", false):
				rect.color = TERRAIN_COLORS.get("river", Color(0.30, 0.55, 0.85))
				lbl.text   = "~"
				continue

			var bid: String     = cell_data.get("building_id",  "")
			var terrain: String = cell_data.get("terrain_type", "plains")
			var has_road: bool  = cell_data.get("has_road", false)
			var base_col: Color
			if bid != "" and bid != "open_land":
				base_col = BUILDING_COLORS.get(bid, TERRAIN_COLORS.get(terrain, Color(0.5, 0.5, 0.5)))
			elif has_road:
				base_col = COLOR_ROAD
			else:
				base_col = TERRAIN_COLORS.get(terrain, Color(0.5, 0.5, 0.5))
			var owner_sid: String = cell_data.get("owner_settlement_id", "")
			rect.color = base_col if owner_sid != "" else base_col.darkened(0.25)
			var abbrev := _building_abbrev(bid) if bid != "" else ""
			lbl.text   = abbrev if abbrev != "" else ("\u00b7" if has_road else "")


## Switch settlement context when the player crosses a world tile boundary.
func _handle_world_tile_change(new_sid: String) -> void:
	if new_sid == _settlement_id:
		return  # no change

	# Cull NPC pool for the old settlement.
	if _world_state != null and _settlement_id != "":
		NpcPoolManager.cull(_world_state, _settlement_id, _get_player())

	_settlement_id = new_sid

	if new_sid != "" and _world_state != null:
		_ss = _world_state.get_settlement(new_sid)
		NpcPoolManager.populate(_world_state, _ss, _world_state.world_seed)
		var boot := get_node_or_null("/root/Bootstrap")
		if boot != null:
			_work_system = boot.get("_work_system")
	else:
		_ss = null  # unclaimed wilderness

	_refresh_panel()


# ── Lazy region grid access ───────────────────────────────────────────────────
## Return the 250×250 region grid for world tile (wtx, wty).
## Generates and caches it in WorldState.region_grids on first visit.
func _get_or_gen_region(wtx: int, wty: int) -> Dictionary:
	if _world_state == null:
		return {}
	var wt_key := "%d,%d" % [wtx, wty]
	if _world_state.region_grids.has(wt_key):
		return _world_state.region_grids[wt_key]
	var wt_data: Dictionary = _world_state.world_tiles.get(wt_key, {})
	var ss_for_tile: SettlementState = null
	var owner_sid: String = wt_data.get("owner_settlement_id", "")
	if owner_sid != "":
		ss_for_tile = _world_state.get_settlement(owner_sid)
	var region: Dictionary = SubRegionGenerator.generate(
		wt_data, ss_for_tile, _world_state.world_tiles, _world_state.world_seed, wtx, wty
	)
	_world_state.region_grids[wt_key] = region
	return region


## Get a region cell by absolute rx/ry, transparently crossing world-tile
## boundaries. Returns {} if the neighbour world tile doesn't exist.
func _sample_region_cell(base_rx: int, base_ry: int) -> Dictionary:
	var wtx := _wt_x
	var wty := _wt_y
	var cx  := base_rx
	var cy  := base_ry
	if base_rx < 0:
		wtx -= 1
		cx   = SubRegionGenerator.REGION_W - 1
	elif base_rx >= SubRegionGenerator.REGION_W:
		wtx += 1
		cx   = 0
	if base_ry < 0:
		wty -= 1
		cy   = SubRegionGenerator.REGION_H - 1
	elif base_ry >= SubRegionGenerator.REGION_H:
		wty += 1
		cy   = 0
	var r := _get_or_gen_region(wtx, wty)
	return r.get("%d,%d" % [cx, cy], {})


# ── Player placement and movement ─────────────────────────────────────────────
func _place_player_at_start() -> void:
	if _world_state == null:
		return
	var loc: Dictionary = _world_state.player_location
	_current_z = loc.get("z_level", 0)

	# Restore region-cell position if the save points to this world tile.
	var saved_wt_x: int = loc.get("wt_x", -999)
	var saved_wt_y: int = loc.get("wt_y", -999)
	if saved_wt_x == _wt_x and saved_wt_y == _wt_y:
		_player_rx = loc.get("rx", SubRegionGenerator.CX)
		_player_ry = loc.get("ry", SubRegionGenerator.CY)
	else:
		# Fresh entry or from a different tile — start at the region centre.
		_player_rx = SubRegionGenerator.CX
		_player_ry = SubRegionGenerator.CY

	# Write canonical position back.
	_world_state.player_location["cell_id"]       = "%d,%d" % [_wt_x, _wt_y]
	_world_state.player_location["wt_x"]          = _wt_x
	_world_state.player_location["wt_y"]          = _wt_y
	_world_state.player_location["rx"]            = _player_rx
	_world_state.player_location["ry"]            = _player_ry
	_world_state.player_location["settlement_id"] = _settlement_id
	_update_pawn_visual()


func _try_move(dx: int, dy: int) -> void:
	if _world_state == null:
		return

	var new_rx  := _player_rx + dx
	var new_ry  := _player_ry + dy
	var new_wtx := _wt_x
	var new_wty := _wt_y

	# Crossing a world tile boundary?
	if new_rx < 0:
		new_wtx -= 1
		new_rx   = SubRegionGenerator.REGION_W - 1
	elif new_rx >= SubRegionGenerator.REGION_W:
		new_wtx += 1
		new_rx   = 0
	if new_ry < 0:
		new_wty -= 1
		new_ry   = SubRegionGenerator.REGION_H - 1
	elif new_ry >= SubRegionGenerator.REGION_H:
		new_wty += 1
		new_ry   = 0

	# Target world tile must exist and be land.
	var wt_key := "%d,%d" % [new_wtx, new_wty]
	var wt_data: Dictionary = _world_state.world_tiles.get(wt_key, {})
	if wt_data.is_empty() or wt_data.get("is_water", false):
		return

	# Target region cell must be land.
	var region := _get_or_gen_region(new_wtx, new_wty)
	var rcid   := "%d,%d" % [new_rx, new_ry]
	if region.get(rcid, {}).get("is_water", false):
		return

	# World tile changed — swap settlement context.
	if new_wtx != _wt_x or new_wty != _wt_y:
		_wt_x = new_wtx
		_wt_y = new_wty
		_handle_world_tile_change(wt_data.get("owner_settlement_id", ""))

	_player_rx = new_rx
	_player_ry = new_ry

	# Persist in player_location.
	_world_state.player_location["cell_id"]       = "%d,%d" % [_wt_x, _wt_y]
	_world_state.player_location["wt_x"]          = _wt_x
	_world_state.player_location["wt_y"]          = _wt_y
	_world_state.player_location["rx"]            = _player_rx
	_world_state.player_location["ry"]            = _player_ry
	_world_state.player_location["settlement_id"] = _settlement_id
	_update_viewport_content()
	_update_pawn_visual()
	_refresh_cell_info()
	_refresh_player_info()


func _try_change_zlevel(delta: int) -> void:
	# Z-levels are stored on the region cell at the player's current position.
	var region := _get_or_gen_region(_wt_x, _wt_y)
	var cid := "%d,%d" % [_player_rx, _player_ry]
	var cell_data: Dictionary = region.get(cid, {})
	var z_lev: Array = cell_data.get("z_levels", [0])
	var target_z := _current_z + delta
	if not z_lev.has(target_z):
		return  # This floor doesn't exist in this building
	_current_z = target_z
	if _world_state != null:
		_world_state.player_location["z_level"] = _current_z
	_refresh_cell_info()
	_update_floor_label()


func _update_floor_label() -> void:
	if _floor_label == null:
		return
	var floor_name: String
	match _current_z:
		0:  floor_name = "Ground floor"
		1:  floor_name = "Upper floor"
		-1: floor_name = "Cellar"
		_:  floor_name = "Floor %d" % _current_z
	_floor_label.text = "📍 %s  (PgUp/PgDn)" % floor_name


func _update_pawn_visual() -> void:
	if _player_rect == null or _cursor_rect == null:
		return
	# Player pawn always sits at the centre slot of the viewport grid.
	var cx := VIEWPORT_RADIUS * _cell_px
	var cy := VIEWPORT_RADIUS * _cell_px
	_player_rect.position = Vector2(
		cx + (_cell_px - 24) / 2.0,
		cy + (_cell_px - 24) / 2.0
	)
	_cursor_rect.position = Vector2(cx + 1, cy + 1)


# _scroll_to_player is no longer needed: the pawn is always at the
# visual centre of the fixed viewport clip box.
func _scroll_to_player() -> void:
	pass


# ── Info panels ───────────────────────────────────────────────────────────────
func _refresh_panel() -> void:
	if _ss == null:
		if _panel_title != null:
			_panel_title.text = "Wilderness"
		if _panel_body != null:
			_panel_body.text = "[color=#555555]Unclaimed land.[/color]"
		_refresh_cell_info()
		return
	_panel_title.text = _ss.name
	var npc_count := 0
	if _world_state != null:
		for pid in _world_state.npc_pool:
			var npc: PersonState = _world_state.npc_pool[pid]
			if npc.home_settlement_id == _settlement_id:
				npc_count += 1
	var lines := "[color=#88aacc]Tier %d — Pop %d[/color]\n" % [_ss.tier, _ss.total_population()]
	lines += "Prosperity: [color=#88dd88]%.2f[/color]  Unrest: [color=#dd8888]%.2f[/color]\n" % [
		_ss.prosperity, _ss.unrest]
	lines += "\n[color=#888888]Labor slots: %d   NPCs: %d[/color]" % [_ss.labor_slots.size(), npc_count]
	_panel_body.text = lines
	_refresh_cell_info()


func _refresh_cell_info() -> void:
	if _cell_info == null:
		return
	# Terrain from region cell; building/interaction from world tile.
	var region  := _get_or_gen_region(_wt_x, _wt_y)
	var rcid    := "%d,%d" % [_player_rx, _player_ry]
	var rc_data: Dictionary = region.get(rcid, {})
	var wt_key  := "%d,%d" % [_wt_x, _wt_y]

	var bid: String     = rc_data.get("building_id",  "")
	var terrain: String = rc_data.get("terrain_type", "plains")
	var z_lev: Array    = rc_data.get("z_levels",     [0])
	# Use the source world-tile key for labor-slot and NPC lookups.
	# Buildings from other territory tiles store their origin in source_wt_key.
	var slot_key: String = rc_data.get("source_wt_key", wt_key)

	var txt := "[color=#cccccc]Tile: %s  (%d,%d)[/color]\n" % [wt_key, _player_rx, _player_ry]
	txt += "Terrain: %s\n" % terrain
	if bid != "" and bid != "open_land":
		var bdef: Dictionary = ContentRegistry.get_content("building", bid)
		txt += "Building: [color=#ffdd88]%s[/color]\n" % bdef.get("name", bid)
		txt += bdef.get("description", "").left(120)
		# Z-level navigation hint
		if z_lev.has(1) and _current_z == 0:
			txt += "\n[color=#aaaaff]PgUp \u2192 Upper floor[/color]"
		elif z_lev.has(-1) and _current_z == 0:
			txt += "\n[color=#aaaaff]PgDn \u2192 Cellar[/color]"
		elif _current_z != 0:
			txt += "\n[color=#aaaaff]PgDn \u2192 Ground floor[/color]"
		# Open labor slots — matched by the building's actual world tile key.
		var open_slots: Array = []
		if _ss != null:
			for slot: Dictionary in _ss.labor_slots:
				if slot.get("cell_id", "") == slot_key and not bool(slot.get("is_filled", false)):
					open_slots.append(slot)
		if not open_slots.is_empty():
			txt += "\n\n[color=#88dd88]Open slots: %d available[/color]" % open_slots.size()
		# NPCs at this building's world tile.
		var npcs_here: Array = _npcs_at_cell(slot_key)
		if not npcs_here.is_empty():
			txt += "\n[color=#ddcc88]%d NPC(s) here \u2014 [F] to talk[/color]" % npcs_here.size()
	else:
		txt += "Open land"

	_cell_info.text = txt
	_update_interact_button(slot_key, bid)


func _update_interact_button(cid: String, bid: String) -> void:
	if _interact_btn == null:
		return
	var player: PersonState = _get_player()
	_pending_interaction = ""

	# Guard: never show interactions on water cells.
	var _cur_region := _get_or_gen_region(_wt_x, _wt_y)
	var _cur_rcell: Dictionary = _cur_region.get("%d,%d" % [_player_rx, _player_ry], {})
	if _cur_rcell.get("is_water", false):
		_interact_btn.visible = false
		return

	# 1. Building with local_layout (or open_land fallback for road/empty tiles).
	var _local_bid: String = bid if bid != "" else "open_land"
	var _lbdef: Dictionary = ContentRegistry.get_content("building", _local_bid)
	if _lbdef.has("local_layout"):
		_pending_interaction = "enter_building:%s" % _local_bid

	if player != null and _pending_interaction == "":
		# 2. Work: currently working here → offer to leave.
		if player.active_role != "" and player.work_cell_id == cid:
			_pending_interaction = "leave_work"
		# 3. Open slot at this cell → offer to apply.
		elif _ss != null:
			for i: int in _ss.labor_slots.size():
				var slot: Dictionary = _ss.labor_slots[i]
				if slot.get("cell_id", "") == cid and not bool(slot.get("is_filled", false)):
					_pending_interaction = "apply_work:%d" % i
					break
		# 4. Inn — offer to rent (if not already sheltered here).
		if _pending_interaction == "" and bid == "inn":
			if player.shelter_status != "rented":
				_pending_interaction = "rent_inn"
		# 5. Derelict — free shelter claim.
		if _pending_interaction == "" and bid == "derelict":
			if player.shelter_status == "":
				_pending_interaction = "claim_shelter"

	if _pending_interaction == "":
		_interact_btn.visible = false
	else:
		_interact_btn.text    = _interaction_label(_pending_interaction)
		_interact_btn.visible = true


func _interaction_label(tag: String) -> String:
	if tag.begins_with("enter_building:"):
		var eid: String = tag.split(":")[1]
		if eid == "open_land":
			return "► Enter Area  [↵]"
		var bdef: Dictionary = ContentRegistry.get_content("building", eid)
		return "► Enter %s  [↵]" % bdef.get("name", eid)
	if tag.begins_with("apply_work:"):
		if _ss != null:
			var idx: int = int(tag.split(":")[1])
			if idx < _ss.labor_slots.size():
				var slot: Dictionary = _ss.labor_slots[idx]
				return "▶ Apply: %s (%.0f coin/day)" % [
					slot.get("slot_id", "work"), float(slot.get("wage_per_day", 0))
				]
		return "▶ Apply for work"
	match tag:
		"leave_work":    return "◀ Leave work"
		"rent_inn":      return "🛏 Rent room (2 coin/day)"
		"claim_shelter": return "🏠 Claim as shelter (free)"
	return tag


func _on_interact_pressed() -> void:
	var player: PersonState = _get_player()
	if player == null:
		return
	var _cid: String = "%d,%d" % [_wt_x, _wt_y]

	if _pending_interaction.begins_with("apply_work:"):
		var idx: int = int(_pending_interaction.split(":")[1])
		if _work_system != null:
			_work_system.assign_player_to_slot(idx)

	elif _pending_interaction == "leave_work":
		if _work_system != null:
			_work_system.remove_player_from_slot()

	elif _pending_interaction == "rent_inn":
		player.shelter_status = "rented"

	elif _pending_interaction == "claim_shelter":
		player.shelter_status = "derelict_claimed"

	elif _pending_interaction.begins_with("enter_building:"):
		var eid: String = _pending_interaction.split(":")[1]
		SceneManager.push_scene("res://src/ui/local_view/local_view.tscn", {
			"building_id":   eid,
			"entry_wx":      _wt_x,
			"entry_wy":      _wt_y,
			"entry_rx":      _player_rx,
			"entry_ry":      _player_ry,
			"settlement_id": _settlement_id,
		})
		return  # scene is changing — skip refresh

	_refresh_cell_info()
	_refresh_player_info()


# ── Helpers ───────────────────────────────────────────────────────────────────
func _get_player() -> PersonState:
	if _world_state == null or _world_state.player_character_id == "":
		return null
	return _world_state.characters.get(_world_state.player_character_id)


func _npcs_at_cell(cid: String) -> Array:
	var result: Array = []
	if _world_state == null:
		return result
	for pid: String in _world_state.npc_pool:
		var npc: PersonState = _world_state.npc_pool[pid]
		if npc.work_cell_id == cid:
			result.append(npc)
	return result


func _refresh_player_info() -> void:
	if _player_info == null:
		return
	var player: PersonState = _get_player()
	if player == null:
		_player_info.text = "[color=#888888]No character[/color]"
		return
	var hunger:  float = player.needs.get("hunger",  0.0)
	var fatigue: float = player.needs.get("fatigue", 0.0)
	var job_str: String  = player.active_role if player.active_role != "" else "none"
	var shelter_str: String = player.shelter_status if player.shelter_status != "" else "none"
	var txt := "[color=#ffdd88]Coin: %.1f[/color]\n" % player.coin
	txt += "Hunger:  [color=%s]%.0f%%[/color]\n" % [
		_need_color(hunger), hunger * 100.0
	]
	txt += "Fatigue: [color=%s]%.0f%%[/color]\n" % [
		_need_color(fatigue), fatigue * 100.0
	]
	txt += "Job: [color=#aaddff]%s[/color]\n" % job_str
	txt += "Shelter: [color=#aaddff]%s[/color]" % shelter_str
	_player_info.text = txt


func _need_color(v: float) -> String:
	if v < 0.4:
		return "#88dd88"
	if v < 0.7:
		return "#dddd44"
	return "#dd4444"


# ── Dialogue (P3-14) ──────────────────────────────────────────────────────────
func _open_dialogue() -> void:
	if _dialogue_panel == null:
		return
	# Get the actual world tile key for this position (may be a neighbouring tile's building).
	var region := _get_or_gen_region(_wt_x, _wt_y)
	var rcid   := "%d,%d" % [_player_rx, _player_ry]
	var rc_data: Dictionary = region.get(rcid, {})
	var cid: String = rc_data.get("source_wt_key", "%d,%d" % [_wt_x, _wt_y])
	var cell_data: Dictionary = {}
	if _world_state != null:
		cell_data = _world_state.world_tiles.get(cid, {})
	var bid: String = cell_data.get("building_id", rc_data.get("building_id", ""))

	# Clear previous options.
	for child in _dlg_options.get_children():
		child.queue_free()

	var npcs: Array = _npcs_at_cell(cid)
	var header_txt: String
	if not npcs.is_empty():
		var npc: PersonState = npcs[0]
		header_txt = "[color=#ffdd88]%s[/color] ([i]%s[/i])\n" % [
			npc.name, npc.population_class
		]
		header_txt += "[color=#aaaaaa]\"Greetings, traveller.\"[/color]\n"
	else:
		# No NPC — show building/market info.
		if bid == "market_stall" and _ss != null:
			header_txt = "[color=#ffdd88]Market Stall[/color]\n"
			header_txt += "[color=#aaaaaa]Goods for sale:[/color]\n"
			for item: String in _ss.market_inventory:
				var qty: float = float(_ss.market_inventory[item])
				header_txt += "  %s — %.0f\n" % [item, qty]
		else:
			header_txt = "[color=#888888]No one to talk to here.[/color]"

	_dlg_body.text = header_txt

	# ── Dialogue options ───────────────────────────────────────────────────
	if not npcs.is_empty():
		_add_dlg_option("Inquire about work", _dlg_inquire_work)
		_add_dlg_option("Ask about the settlement", _dlg_inquire_settlement)

	if bid == "market_stall" and _ss != null:
		_add_dlg_option("Browse goods", _dlg_browse_market)

	if bid == "inn":
		_add_dlg_option("Rent a room (2 coin/day)", _dlg_rent_inn)

	_add_dlg_option("[Esc / F] Close", _close_dialogue)

	_dialogue_panel.visible = true


func _add_dlg_option(label: String, callback: Callable) -> void:
	var btn := Button.new()
	btn.text = label
	btn.alignment = HORIZONTAL_ALIGNMENT_LEFT
	btn.pressed.connect(callback)
	_dlg_options.add_child(btn)


func _close_dialogue() -> void:
	if _dialogue_panel != null:
		_dialogue_panel.visible = false


func _dlg_inquire_work() -> void:
	if _ss == null:
		return
	# Collect open slots with their global index.
	var open_slots: Array = []
	for i: int in _ss.labor_slots.size():
		var slot: Dictionary = _ss.labor_slots[i]
		if not bool(slot.get("is_filled", false)):
			open_slots.append({"index": i, "slot": slot})

	var lines := "[color=#88dd88]Open positions in %s:[/color]\n" % _ss.name
	if open_slots.is_empty():
		lines += "  [color=#888888]No open positions right now.[/color]"
	else:
		for entry: Dictionary in open_slots:
			var slot: Dictionary = entry["slot"]
			lines += "  • %s — %.0f coin/day  [%s skill]\n" % [
				slot.get("slot_id", "?"),
				float(slot.get("wage_per_day", 0)),
				slot.get("skill_required", "any")
			]
	_dlg_body.text = lines

	# Replace option buttons: one Apply per open slot + Back.
	for child in _dlg_options.get_children():
		child.queue_free()
	for entry: Dictionary in open_slots:
		var slot: Dictionary = entry["slot"]
		var idx: int = entry["index"]
		var lbl := "▶ Apply: %s (%.0f coin/day)" % [
			slot.get("slot_id", "?"), float(slot.get("wage_per_day", 0))
		]
		_add_dlg_option(lbl, func(): _dlg_apply_slot(idx))
	_add_dlg_option("◀ Back", _open_dialogue)
	_add_dlg_option("[Esc / F] Close", _close_dialogue)


func _dlg_apply_slot(idx: int) -> void:
	if _work_system == null or _ss == null:
		_dlg_body.text = "[color=#dd4444]No work system available.[/color]"
		return
	var ok: bool = _work_system.assign_player_to_slot(idx)
	if ok:
		var slot: Dictionary = _ss.labor_slots[idx] if idx < _ss.labor_slots.size() else {}
		_dlg_body.text = "[color=#88dd88]You are now working as %s (%.0f coin/day).[/color]" % [
			slot.get("slot_id", "worker"), float(slot.get("wage_per_day", 0))
		]
	else:
		_dlg_body.text = "[color=#dd4444]Could not take that position.[/color]"
	_refresh_player_info()
	_refresh_cell_info()


func _dlg_inquire_settlement() -> void:
	if _ss == null:
		return
	var txt := "[color=#ffdd88]%s[/color] — Tier %d\n" % [_ss.name, _ss.tier]
	txt += "Population: %d\n" % _ss.total_population()
	txt += "Prosperity: %.2f   Unrest: %.2f\n" % [_ss.prosperity, _ss.unrest]
	_dlg_body.text = txt


func _dlg_browse_market() -> void:
	if _ss == null:
		return
	var txt := "[color=#ffdd88]Market inventory:[/color]\n"
	for item: String in _ss.market_inventory:
		txt += "  %s — %.0f\n" % [item, float(_ss.market_inventory[item])]
	_dlg_body.text = txt


func _dlg_rent_inn() -> void:
	var player: PersonState = _get_player()
	if player == null:
		return
	player.shelter_status = "rented"
	_dlg_body.text = "[color=#88dd88]Room booked. Rent: 2 coin/day.[/color]"
	_refresh_player_info()
	_refresh_cell_info()


func _exit_view() -> void:
	## Cull NPC pool and pop the scene.
	if _world_state != null:
		var player_state: PersonState = null
		if _world_state.player_character_id != "":
			player_state = _world_state.characters.get(_world_state.player_character_id)
		NpcPoolManager.cull(_world_state, _settlement_id, player_state)
	SceneManager.pop_scene()


func _building_abbrev(bid: String) -> String:
	match bid:
		"farm_plot":    return "Farm"
		"inn":          return "Inn"
		"well":         return "Well"
		"granary":      return "Granary"
		"market_stall": return "Market"
		"derelict":     return "Ruins"
		"open_land":    return ""
	return bid.left(4)
