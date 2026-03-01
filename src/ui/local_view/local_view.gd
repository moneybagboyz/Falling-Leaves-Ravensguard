## LocalView — seamless local-tile view of the world at building interior scale.
##
## Entered via SceneManager.push_scene("res://src/ui/local_view/local_view.tscn",
##   { "entry_wx": int, "entry_wy": int, "entry_rx": int, "entry_ry": int,
##     "settlement_id": String })
## Exits back to SettlementView via SceneManager.pop_scene() (Esc key or door).
##
## The viewport fills the available map area. Adjacent buildings are always
## visible — no chunk switching. + / - keys zoom; the tile count adjusts to
## always fill the screen with the player centred.
##
## P4-10 inventory / equipment sidebar:
##   'x' chest tile → [↵] opens inventory panel  (also [I] / [Tab] to toggle)
##   Panel shows chest contents (Take), carried items (Equip), equipped gear (Unequip).
##   Mutations persist in WorldState.chest_contents + PersonState.carried_items/equipment_refs.
class_name LocalView
extends Control

# ── Layout constants ─────────────────────────────────────────────────────────
## Size of each building cell's local layout, in tiles. Never changes.
const GRID_W      := 25
const GRID_H      := 25
## Zoom steps — pixels per tile.
const ZOOM_STEPS: Array[int] = [8, 12, 16, 24, 28, 32, 40, 48]
const PANEL_W     := 230
const INV_PANEL_W := 230

# ── Tile definitions ─────────────────────────────────────────────────────────
## Each entry: [Color, walkable: bool, label_char: String]
const TILE_DEFS: Dictionary = {
	"_": [Color(0.72, 0.78, 0.56), true,  ""],
	"#": [Color(0.20, 0.18, 0.16), false, "#"],
	".": [Color(0.70, 0.68, 0.62), true,  ""],
	"+": [Color(0.72, 0.50, 0.22), true,  "+"],
	"~": [Color(0.28, 0.55, 0.90), false, "~"],
	",": [Color(0.60, 0.52, 0.38), true,  ""],
	"b": [Color(0.35, 0.48, 0.82), true,  "b"],
	"t": [Color(0.58, 0.44, 0.28), true,  "t"],
	"c": [Color(0.62, 0.48, 0.30), true,  "c"],
	"s": [Color(0.52, 0.58, 0.48), false, "s"],
	"f": [Color(0.78, 0.32, 0.12), false, "f"],
	"^": [Color(0.72, 0.84, 0.98), true,  "^"],
	"v": [Color(0.52, 0.66, 0.82), true,  "v"],
	"%": [Color(0.46, 0.70, 0.24), true,  "%"],
	"*": [Color(0.50, 0.48, 0.45), true,  "*"],
	"x": [Color(0.84, 0.66, 0.20), true,  "x"],
	"A": [Color(0.42, 0.42, 0.52), false, "A"],
	"M": [Color(0.50, 0.48, 0.44), false, "M"],
	"p": [Color(0.32, 0.30, 0.28), false, "p"],
}
const TILE_FALLBACK: Array = [Color(0.30, 0.28, 0.26), false, "?"]

const COLOR_PANEL_BG  := Color(0.12, 0.12, 0.14)
const COLOR_INV_BG    := Color(0.10, 0.10, 0.13)
const COLOR_PLAYER    := Color(1.00, 1.00, 1.00)
const COLOR_CURSOR    := Color(1.00, 0.92, 0.30, 0.70)
const COLOR_TEXT      := Color(0.90, 0.90, 0.90)
const COLOR_DIM       := Color(0.55, 0.55, 0.60)
const COLOR_SEL       := Color(0.35, 0.65, 1.00)
const COLOR_EQUIPPED  := Color(0.35, 0.88, 0.35)
const COLOR_CHEST_HDR := Color(0.90, 0.75, 0.20)

# ── State ────────────────────────────────────────────────────────────────────
var _building_id:  String      = ""
var _entry_wx:     int         = 0
var _entry_wy:     int         = 0
var _building_def: Dictionary  = {}
var _world_state:  WorldState  = null

var _player_lx: int = 12   ## local X within the 25×25 layout of the current region cell
var _player_ly: int = 12   ## local Y

## Pixels per tile — the zoom level. Changed by + / - keys.
var _tile_px:  int = 28
## Tile columns / rows currently rendered (recomputed from map area size ÷ _tile_px).
var _map_cols: int = 0
var _map_rows: int = 0

## Context tag for the single action button.
## "exit_building" | "open_chest" | "stairs_up" | "stairs_down" | "talk_npc" | ""
var _pending_local_action: String = ""

## NPC person_id targeted by the current "talk_npc" action, or "".
var _pending_npc_id: String = ""

## Whether the inventory panel is currently visible.
var _inv_visible: bool = false

# ── Node refs ────────────────────────────────────────────────────────────────
var _tile_rects:   Array           = []
var _tile_labels:  Array           = []
var _player_rect:  ColorRect       = null
var _cursor_rect:  ColorRect       = null
var _bld_title:    Label           = null
var _tile_info:    RichTextLabel   = null
var _action_btn:   Button          = null
var _map_area:     Control         = null
var _inv_panel:    Control         = null
var _chest_hdr:    Label           = null
var _chest_scroll: ScrollContainer = null
var _chest_vbox:   VBoxContainer   = null
var _carried_vbox: VBoxContainer   = null
var _equip_vbox:   VBoxContainer   = null

## Settlement the player entered from (used to filter NPC pool).
var _settlement_id: String    = ""
## Reality bubble — person_id → ColorRect pawn for each local NPC.
var _npc_rects:    Dictionary = {}

## Region cell the player is currently in (0..249 within world tile).
var _reg_rx: int = 125
var _reg_ry: int = 125
## Layout cache: "rx,ry" → Array[String] (25 rows of 25 chars each).
## Pre-loaded as a 3×3 neighbourhood; refreshed on every cell boundary crossing.
var _layout_cache: Dictionary = {}


# ── Lifecycle ─────────────────────────────────────────────────────────────────
func _ready() -> void:
	var params := SceneManager.take_params()
	_entry_wx      = params.get("entry_wx",       0)
	_entry_wy      = params.get("entry_wy",       0)
	_settlement_id = params.get("settlement_id", "")
	_reg_rx        = params.get("entry_rx",       125)
	_reg_ry        = params.get("entry_ry",       125)

	# Load world state from Bootstrap autoload.
	var boot := get_node_or_null("/root/Bootstrap")
	if boot:
		_world_state = boot.get("world_state")

	# Derive building context from the entry cell.
	_building_id  = _cell_building_id(_reg_rx, _reg_ry)
	_building_def = ContentRegistry.get_content("building", _building_id) if _building_id != "" else {}

	# Pre-load neighbourhood so every layout lookup is a cache hit.
	_preload_neighbourhood(_reg_rx, _reg_ry)

	_build_ui()
	_place_player_at_entry()
	SimulationClock.tick_completed.connect(_on_local_tick)
	_refresh_tile_info()
	# Await one frame so _map_area has a valid size, then build the tile grid.
	await get_tree().process_frame
	_rebuild_grid()


# ── UI construction ────────────────────────────────────────────────────────────
func _build_ui() -> void:
	var bg := ColorRect.new()
	bg.color = Color(0.08, 0.08, 0.10)
	bg.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
	add_child(bg)

	var hbox := HBoxContainer.new()
	hbox.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
	hbox.add_theme_constant_override("separation", 0)
	add_child(hbox)

	# ── Left panel ───────────────────────────────────────────────────────────
	var panel_bg := ColorRect.new()
	panel_bg.color = COLOR_PANEL_BG
	panel_bg.custom_minimum_size = Vector2(PANEL_W, 0)
	hbox.add_child(panel_bg)

	var pvbox := VBoxContainer.new()
	pvbox.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
	pvbox.add_theme_constant_override("separation", 6)
	panel_bg.add_child(pvbox)

	var back_btn := Button.new()
	back_btn.text = "◀ Return to Map  [Esc]"
	back_btn.custom_minimum_size = Vector2(0, 32)
	back_btn.pressed.connect(_exit_to_settlement)
	pvbox.add_child(back_btn)

	pvbox.add_child(_make_sep())

	_bld_title = Label.new()
	_bld_title.text = _area_label_for_cell(_reg_rx, _reg_ry)
	_bld_title.add_theme_font_size_override("font_size", 15)
	_bld_title.add_theme_color_override("font_color", Color(1.0, 0.85, 0.4))
	_bld_title.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	pvbox.add_child(_bld_title)

	var desc_lbl := RichTextLabel.new()
	desc_lbl.bbcode_enabled = true
	desc_lbl.fit_content = true
	desc_lbl.scroll_active = false
	desc_lbl.add_theme_color_override("default_color", COLOR_DIM)
	desc_lbl.add_theme_font_size_override("normal_font_size", 11)
	desc_lbl.text = _building_def.get("description", "").left(160)
	pvbox.add_child(desc_lbl)

	pvbox.add_child(_make_sep())

	var cell_hdr := Label.new()
	cell_hdr.text = "── Current Tile ──"
	cell_hdr.add_theme_color_override("font_color", COLOR_SEL)
	cell_hdr.add_theme_font_size_override("font_size", 11)
	pvbox.add_child(cell_hdr)

	_tile_info = RichTextLabel.new()
	_tile_info.bbcode_enabled = true
	_tile_info.fit_content = true
	_tile_info.scroll_active = false
	_tile_info.add_theme_color_override("default_color", COLOR_TEXT)
	_tile_info.add_theme_font_size_override("normal_font_size", 11)
	pvbox.add_child(_tile_info)

	pvbox.add_child(_make_sep())

	var hint := Label.new()
	hint.text = "WASD / Arrows = move\nDiagonals: Q E Z C\n↵ Enter = interact\nI / Tab = inventory\n+ / - = zoom\nEsc = close / exit"
	hint.add_theme_color_override("font_color", COLOR_DIM)
	hint.add_theme_font_size_override("font_size", 10)
	pvbox.add_child(hint)

	pvbox.add_child(_make_sep())

	_action_btn = Button.new()
	_action_btn.visible = false
	_action_btn.custom_minimum_size = Vector2(0, 28)
	_action_btn.pressed.connect(_on_action_pressed)
	pvbox.add_child(_action_btn)

	# ── Centre: map area fills remaining space ────────────────────────────────
	var map_clip := Control.new()
	map_clip.clip_contents         = true
	map_clip.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	map_clip.size_flags_vertical   = Control.SIZE_EXPAND_FILL
	hbox.add_child(map_clip)

	_map_area = Control.new()
	_map_area.mouse_filter = Control.MOUSE_FILTER_IGNORE
	_map_area.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
	map_clip.add_child(_map_area)

	# Rebuild tile nodes whenever the map area is resized (e.g. window resize).
	map_clip.resized.connect(_on_map_area_resized)

	# ── Inventory panel (right, hidden by default) ────────────────────────────
	_inv_panel = ColorRect.new()
	(_inv_panel as ColorRect).color = COLOR_INV_BG
	_inv_panel.custom_minimum_size  = Vector2(INV_PANEL_W, 0)
	_inv_panel.visible              = false
	hbox.add_child(_inv_panel)

	var ivbox := VBoxContainer.new()
	ivbox.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
	ivbox.add_theme_constant_override("separation", 6)
	_inv_panel.add_child(ivbox)

	var inv_title := Label.new()
	inv_title.text = "── INVENTORY ──"
	inv_title.add_theme_color_override("font_color", COLOR_SEL)
	inv_title.add_theme_font_size_override("font_size", 12)
	ivbox.add_child(inv_title)

	ivbox.add_child(_make_sep())

	_chest_hdr = Label.new()
	_chest_hdr.text = "CHEST"
	_chest_hdr.add_theme_color_override("font_color", COLOR_CHEST_HDR)
	_chest_hdr.add_theme_font_size_override("font_size", 11)
	ivbox.add_child(_chest_hdr)

	_chest_scroll = ScrollContainer.new()
	_chest_scroll.custom_minimum_size        = Vector2(0, 100)
	_chest_scroll.size_flags_vertical        = Control.SIZE_SHRINK_CENTER
	_chest_scroll.horizontal_scroll_mode     = ScrollContainer.SCROLL_MODE_DISABLED
	ivbox.add_child(_chest_scroll)

	_chest_vbox = VBoxContainer.new()
	_chest_vbox.add_theme_constant_override("separation", 2)
	_chest_scroll.add_child(_chest_vbox)

	ivbox.add_child(_make_sep())

	var carried_hdr := Label.new()
	carried_hdr.text = "CARRIED"
	carried_hdr.add_theme_color_override("font_color", Color(0.80, 0.80, 0.90))
	carried_hdr.add_theme_font_size_override("font_size", 11)
	ivbox.add_child(carried_hdr)

	var carried_scroll := ScrollContainer.new()
	carried_scroll.custom_minimum_size     = Vector2(0, 100)
	carried_scroll.size_flags_vertical     = Control.SIZE_SHRINK_CENTER
	carried_scroll.horizontal_scroll_mode  = ScrollContainer.SCROLL_MODE_DISABLED
	ivbox.add_child(carried_scroll)

	_carried_vbox = VBoxContainer.new()
	_carried_vbox.add_theme_constant_override("separation", 2)
	carried_scroll.add_child(_carried_vbox)

	ivbox.add_child(_make_sep())

	var equip_hdr := Label.new()
	equip_hdr.text = "EQUIPPED"
	equip_hdr.add_theme_color_override("font_color", COLOR_EQUIPPED)
	equip_hdr.add_theme_font_size_override("font_size", 11)
	ivbox.add_child(equip_hdr)

	var equip_scroll := ScrollContainer.new()
	equip_scroll.size_flags_vertical    = Control.SIZE_EXPAND_FILL
	equip_scroll.horizontal_scroll_mode = ScrollContainer.SCROLL_MODE_DISABLED
	ivbox.add_child(equip_scroll)

	_equip_vbox = VBoxContainer.new()
	_equip_vbox.add_theme_constant_override("separation", 2)
	equip_scroll.add_child(_equip_vbox)


func _make_sep() -> HSeparator:
	var sep := HSeparator.new()
	sep.add_theme_color_override("color", Color(0.25, 0.25, 0.28))
	return sep


# ── Grid construction ─────────────────────────────────────────────────────────
## Called when the clip container is resized (e.g. window resize or inv panel toggle).
func _on_map_area_resized() -> void:
	if _map_area != null and _map_area.size != Vector2.ZERO:
		_rebuild_grid()


## Rebuild tile nodes for the current map area size and _tile_px zoom level.
## Also re-spawns NPC pawns. Called on entry, zoom change, and window resize.
func _rebuild_grid() -> void:
	for r in _tile_rects:
		if is_instance_valid(r):
			r.queue_free()
	_tile_rects.clear()
	_tile_labels.clear()
	if _cursor_rect != null and is_instance_valid(_cursor_rect):
		_cursor_rect.queue_free()
		_cursor_rect = null
	if _player_rect != null and is_instance_valid(_player_rect):
		_player_rect.queue_free()
		_player_rect = null
	for rect in _npc_rects.values():
		if is_instance_valid(rect):
			rect.queue_free()
	_npc_rects.clear()

	if _map_area == null or _map_area.size == Vector2.ZERO:
		return
	_map_cols = max(1, int(_map_area.size.x / _tile_px))
	_map_rows = max(1, int(_map_area.size.y / _tile_px))
	_build_tile_nodes()
	_render_viewport()
	_update_pawn_visual()
	_spawn_local_npcs()


## Instantiate _map_cols × _map_rows ColorRect tile nodes plus cursor and player pawn.
func _build_tile_nodes() -> void:
	for ty: int in range(_map_rows):
		for tx: int in range(_map_cols):
			var rect := ColorRect.new()
			rect.position = Vector2(tx * _tile_px + 1, ty * _tile_px + 1)
			rect.size     = Vector2(_tile_px - 2, _tile_px - 2)
			rect.color    = Color(0.20, 0.18, 0.16)
			_map_area.add_child(rect)
			_tile_rects.append(rect)

			var lbl := Label.new()
			lbl.text = ""
			@warning_ignore("integer_division")
			lbl.add_theme_font_size_override("font_size", clamp(_tile_px / 3, 7, 13))
			lbl.add_theme_color_override("font_color", Color(0, 0, 0, 0.55))
			lbl.position = Vector2(2, 2)
			lbl.size     = Vector2(_tile_px - 4, _tile_px - 4)
			rect.add_child(lbl)
			_tile_labels.append(lbl)

	# Cursor highlight.
	_cursor_rect = ColorRect.new()
	_cursor_rect.size         = Vector2(_tile_px - 2, _tile_px - 2)
	_cursor_rect.color        = COLOR_CURSOR
	_cursor_rect.mouse_filter = Control.MOUSE_FILTER_IGNORE
	_map_area.add_child(_cursor_rect)

	# Player pawn.
	@warning_ignore("integer_division")
	var pawn_sz: int = maxi(8, _tile_px - 8)
	_player_rect = ColorRect.new()
	_player_rect.size         = Vector2(pawn_sz, pawn_sz)
	_player_rect.color        = COLOR_PLAYER
	_player_rect.mouse_filter = Control.MOUSE_FILTER_IGNORE
	_map_area.add_child(_player_rect)

	var pawn_lbl := Label.new()
	pawn_lbl.text = "@"
	@warning_ignore("integer_division")
	pawn_lbl.add_theme_font_size_override("font_size", clamp(_tile_px / 2, 8, 16))
	pawn_lbl.add_theme_color_override("font_color", Color(0.1, 0.1, 0.1))
	pawn_lbl.position = Vector2(2, 1)
	_player_rect.add_child(pawn_lbl)


# ── Zoom ─────────────────────────────────────────────────────────────────────────
func _zoom(direction: int) -> void:
	var idx := ZOOM_STEPS.find(_tile_px)
	if idx == -1: idx = ZOOM_STEPS.find(28)
	idx = clampi(idx + direction, 0, ZOOM_STEPS.size() - 1)
	if ZOOM_STEPS[idx] == _tile_px:
		return
	_tile_px = ZOOM_STEPS[idx]
	_preload_neighbourhood(_reg_rx, _reg_ry)
	_rebuild_grid()


# ── Player placement ──────────────────────────────────────────────────────────
## Scan the layout for the `+` door tile nearest to the bottom edge.
## Fall back to the grid centre if none found.
func _place_player_at_entry() -> void:
	## Always spawn at center; the viewport slides, so the entry cell
	## surrounds the player immediately.
	_player_lx = GRID_W >> 1
	_player_ly = GRID_H >> 1
	_update_pawn_visual()


# ── Input ─────────────────────────────────────────────────────────────────────
func _input(event: InputEvent) -> void:
	if not event is InputEventKey or not event.pressed:
		return
	match event.keycode:
		KEY_ESCAPE:
			if _inv_visible:
				_set_inv_visible(false)
			else:
				_exit_to_settlement()
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
		KEY_I, KEY_TAB:
			_set_inv_visible(not _inv_visible)
		KEY_ENTER, KEY_KP_ENTER:
			if _action_btn != null and _action_btn.visible:
				_on_action_pressed()
		KEY_EQUAL, KEY_KP_ADD:
			_zoom(1)
		KEY_MINUS, KEY_KP_SUBTRACT:
			_zoom(-1)


func _try_move(dx: int, dy: int) -> void:
	var nx := _player_lx + dx
	var ny := _player_ly + dy
	var new_rx := _reg_rx
	var new_ry := _reg_ry
	# Cell boundary crossing
	if nx < 0:          new_rx -= 1; nx += GRID_W
	elif nx >= GRID_W:  new_rx += 1; nx -= GRID_W
	if ny < 0:          new_ry -= 1; ny += GRID_H
	elif ny >= GRID_H:  new_ry += 1; ny -= GRID_H
	# World tile boundary — attempt seamless crossing.
	if new_rx < 0 or new_rx >= 250 or new_ry < 0 or new_ry >= 250:
		_cross_world_tile_boundary(new_rx, new_ry, nx, ny)
		return
	# Check walkability of target tile
	var ch := _get_cell_char(new_rx, new_ry, nx, ny)
	if not (TILE_DEFS.get(ch, TILE_FALLBACK) as Array)[1]:
		return
	# Update position
	var cell_changed := (new_rx != _reg_rx or new_ry != _reg_ry)
	_reg_rx    = new_rx
	_reg_ry    = new_ry
	_player_lx = nx
	_player_ly = ny
	if cell_changed:
		_preload_neighbourhood(_reg_rx, _reg_ry)
		_building_id  = _cell_building_id(_reg_rx, _reg_ry)
		_building_def = ContentRegistry.get_content("building", _building_id) if _building_id != "" else {}
	_render_viewport()
	_update_pawn_visual()
	_refresh_all_npc_pawns()
	_refresh_tile_info()


# ── Visual update ─────────────────────────────────────────────────────────────
## Player pawn is always drawn at the centre of the map area.
func _update_pawn_visual() -> void:
	if _player_rect == null or _cursor_rect == null or _map_cols == 0 or _map_rows == 0:
		return
	@warning_ignore("integer_division")
	var cx: int = (_map_cols / 2) * _tile_px
	@warning_ignore("integer_division")
	var cy: int = (_map_rows / 2) * _tile_px
	var pawn_sz: float = _player_rect.size.x
	_cursor_rect.position = Vector2(cx + 1, cy + 1)
	_player_rect.position = Vector2(cx + (_tile_px - pawn_sz) / 2.0, cy + (_tile_px - pawn_sz) / 2.0)


# ── Info panel ────────────────────────────────────────────────────────────────
func _refresh_tile_info() -> void:
	if _tile_info == null:
		return
	var ch       := _get_cell_char(_reg_rx, _reg_ry, _player_lx, _player_ly)
	var name_str := _tile_name(ch)
	var txt := "[color=#cccccc]Cell (%d,%d)  Tile (%d,%d)[/color]\n" % [_reg_rx, _reg_ry, _player_lx, _player_ly]
	txt += "Type: %s  [color=#555555]'%s'[/color]" % [name_str, ch]
	_tile_info.text = txt
	_update_action_button(ch)
	# Update area title to reflect current cell.
	if _bld_title != null:
		_bld_title.text = _area_label_for_cell(_reg_rx, _reg_ry)


func _tile_name(ch: String) -> String:
	match ch:
		"_": return "Open ground"
		"#": return "Wall"
		".": return "Floor"
		"+": return "Door"
		"~": return "Water"
		",": return "Dirt path"
		"b": return "Bed"
		"t": return "Table"
		"c": return "Counter"
		"s": return "Shelf"
		"f": return "Forge"
		"^": return "Stairs up"
		"v": return "Stairs down"
		"%": return "Crop field"
		"*": return "Rubble"
		"x": return "Chest"
		"A": return "Anvil"
		"M": return "Millstone"
		"p": return "Pillar"
	return "Unknown"


func _update_action_button(ch: String) -> void:
	if _action_btn == null:
		return
	_pending_local_action = ""
	_pending_npc_id = ""
	match ch:
		"+":
			_pending_local_action = "exit_building"
		"x":
			_pending_local_action = "open_chest"
		"^":
			_pending_local_action = "stairs_up"
		"v":
			_pending_local_action = "stairs_down"

	# Adjacent NPC talk — only when no tile action already set.
	if _pending_local_action == "":
		var adj := _npc_adjacent_to_player()
		if adj != "":
			_pending_npc_id       = adj
			_pending_local_action = "talk_npc"

	if _pending_local_action == "":
		_action_btn.visible = false
	else:
		_action_btn.text    = _action_label(_pending_local_action)
		_action_btn.visible = true


func _action_label(tag: String) -> String:
	match tag:
		"exit_building": return "► Return to Map  [↵]"
		"open_chest":    return "📦 Open Chest  [↵]"
		"stairs_up":     return "▲ Climb Stairs  [↵]"
		"stairs_down":   return "▼ Descend Stairs  [↵]"
		"talk_npc":
			if _pending_npc_id != "" and _world_state != null:
				var npc: PersonState = _world_state.characters.get(_pending_npc_id)
				if npc != null:
					return "💬 Talk to %s  [↵]" % npc.name
			return "💬 Talk  [↵]"
	return tag


# ── Action handler ────────────────────────────────────────────────────────────
func _on_action_pressed() -> void:
	match _pending_local_action:
		"exit_building":
			_exit_to_settlement()
		"open_chest":
			_set_inv_visible(true)
		"stairs_up", "stairs_down":
			if _tile_info != null:
				_tile_info.text += "\n[color=#aaaaff](Multi-floor nav coming soon)[/color]"
		"talk_npc":
			_do_talk_npc(_pending_npc_id)


# ── Helpers ─────────────────────────────────────────────────────────────────────

## Return the person_id of the first NPC adjacent to the player (Chebyshev dist ≤ 1),
## or "" if none found.
func _npc_adjacent_to_player() -> String:
	if _world_state == null:
		return ""
	var player_abs_x: int = _reg_rx * GRID_W + _player_lx
	var player_abs_y: int = _reg_ry * GRID_H + _player_ly
	for pid: String in _npc_rects:
		var npc: PersonState = _world_state.characters.get(pid)
		if npc == null:
			continue
		var lx: int = npc.location.get("lx", -1)
		var ly: int = npc.location.get("ly", -1)
		var rx: int = npc.location.get("rx", -1)
		if lx < 0 or ly < 0 or rx < 0:
			continue
		var npc_abs_x: int = rx * GRID_W + lx
		var npc_abs_y: int = npc.location.get("ry", 0) * GRID_H + ly
		if absi(npc_abs_x - player_abs_x) <= 1 and absi(npc_abs_y - player_abs_y) <= 1:
			return pid
	return ""


## Show NPC info in the tile panel.  Trade / dialogue will expand this later.
func _do_talk_npc(pid: String) -> void:
	if _world_state == null or _tile_info == null:
		return
	var npc: PersonState = _world_state.characters.get(pid)
	if npc == null:
		return
	var greeting: String
	match npc.schedule_state:
		"working":  greeting = "I can't chat long — got work to do."
		"resting":  greeting = "*yawns* What is it?"
		"wandering":greeting = "Lovely day, isn't it?"
		_:          greeting = "Hmm?"
	var pop_label: String = npc.pop_class.capitalize() if npc.get("pop_class") != null else ""
	var role_label: String = npc.active_role.replace("_", " ").capitalize() if npc.active_role != "" else "Idle"
	_tile_info.text = (
		"[color=#f0c96e][b]%s[/b][/color]\n" % npc.name +
		"[color=#aaaaaa]%s · %s[/color]\n\n" % [pop_label, role_label] +
		"[color=#dddddd]\"%s\"[/color]" % greeting
	)


func _get_player() -> PersonState:
	if _world_state == null or _world_state.player_character_id == "":
		return null
	return _world_state.characters.get(_world_state.player_character_id)


func _chest_key() -> String:
	return "%d,%d:%s" % [_entry_wx, _entry_wy, _building_id]


## Return (and lazily initialise) the runtime chest contents for this building.
func _chest_items_list() -> Array:
	if _world_state == null:
		return []
	var key := _chest_key()
	if not _world_state.chest_contents.has(key):
		_world_state.chest_contents[key] = \
			_building_def.get("chest_items", []).duplicate()
	return _world_state.chest_contents[key]


## Return the equipment slot ID for item_id, or "" if unequippable.
func _item_equip_slot(item_id: String) -> String:
	var adef: Dictionary = ContentRegistry.get_content("armor", item_id)
	if not adef.is_empty():
		return adef.get("equip_slot", "torso")
	var wdef: Dictionary = ContentRegistry.get_content("weapon", item_id)
	if not wdef.is_empty():
		return "main_hand"
	return ""


## Human-readable name for an item_id.
func _item_name(item_id: String) -> String:
	var adef: Dictionary = ContentRegistry.get_content("armor", item_id)
	if not adef.is_empty():
		return adef.get("name", item_id)
	var wdef: Dictionary = ContentRegistry.get_content("weapon", item_id)
	if not wdef.is_empty():
		return wdef.get("name", item_id)
	return item_id


func _exit_to_settlement() -> void:
	# Deactivate reality bubble — disconnect clock only.
	# NPC local coords are intentionally kept so they persist between visits.
	if SimulationClock.tick_completed.is_connected(_on_local_tick):
		SimulationClock.tick_completed.disconnect(_on_local_tick)
	if _world_state != null:
		# Persist the player's final local-view region cell so SettlementView
		# restores them at the correct tile rather than the entry cell.
		_world_state.player_location["rx"] = _reg_rx
		_world_state.player_location["ry"] = _reg_ry
	# Pass settlement_id back so SettlementView can reload correctly.
	SceneManager.pop_scene({"settlement_id": _settlement_id})


## Attempt a seamless crossing into an adjacent world tile.
## new_rx/new_ry are the out-of-range region coords; lx/ly are the local-tile coords.
## Exits to settlement if the adjacent world tile does not exist or is blocked.
func _cross_world_tile_boundary(new_rx: int, new_ry: int, lx: int, ly: int) -> void:
	if _world_state == null:
		_exit_to_settlement()
		return

	# Work out which world tile we are crossing into.
	var new_wx := _entry_wx
	var new_wy := _entry_wy
	var wrapped_rx := new_rx
	var wrapped_ry := new_ry
	if new_rx < 0:
		new_wx   -= 1
		wrapped_rx = 249
	elif new_rx >= 250:
		new_wx   += 1
		wrapped_rx = 0
	if new_ry < 0:
		new_wy   -= 1
		wrapped_ry = 249
	elif new_ry >= 250:
		new_wy   += 1
		wrapped_ry = 0

	var new_wt_key := "%d,%d" % [new_wx, new_wy]
	if not _world_state.world_tiles.has(new_wt_key):
		# Edge of world — fall back to the settlement exit.
		_exit_to_settlement()
		return

	# Ensure the neighbouring world tile has a generated region grid.
	if not _world_state.region_grids.has(new_wt_key):
		var wt: Dictionary    = _world_state.world_tiles[new_wt_key]
		var tile_owner: String = wt.get("owner_settlement_id", "")
		var ss_tile            = _world_state.get_settlement(tile_owner) if tile_owner != "" else null
		_world_state.region_grids[new_wt_key] = SubRegionGenerator.generate(
			wt, ss_tile, _world_state.world_tiles, _world_state.world_seed, new_wx, new_wy
		)

	# Temporarily adopt the new world tile so layout helpers read the right grid.
	var old_wx := _entry_wx
	var old_wy := _entry_wy
	_entry_wx = new_wx
	_entry_wy = new_wy

	# Build the target cell layout and check if the landing tile is walkable.
	var target_layout: Array = _build_cell_layout(wrapped_rx, wrapped_ry)
	var target_char: String = "#"
	if ly >= 0 and ly < target_layout.size():
		var row: String = target_layout[ly]
		if lx >= 0 and lx < row.length():
			target_char = row[lx]
	if not (TILE_DEFS.get(target_char, TILE_FALLBACK) as Array)[1]:
		# Target tile blocked — roll back.
		_entry_wx = old_wx
		_entry_wy = old_wy
		return

	# Commit: clear tile layout cache (keys are rx,ry with no wt prefix).
	_layout_cache.clear()
	_reg_rx    = wrapped_rx
	_reg_ry    = wrapped_ry
	_player_lx = lx
	_player_ly = ly

	# Update settlement context.
	var new_owner: String = _world_state.world_tiles[new_wt_key].get("owner_settlement_id", "")
	_settlement_id = new_owner

	# Update player_location world tile fields so the simulation stays consistent.
	if _world_state.player_location != null:
		_world_state.player_location["wt_x"] = new_wx
		_world_state.player_location["wt_y"] = new_wy

	_preload_neighbourhood(_reg_rx, _reg_ry)
	_building_id  = _cell_building_id(_reg_rx, _reg_ry)
	_building_def = ContentRegistry.get_content("building", _building_id) if _building_id != "" else {}

	# Rebuild NPC pawns for the new settlement.
	_spawn_local_npcs()

	_render_viewport()
	_update_pawn_visual()
	_refresh_all_npc_pawns()
	_refresh_tile_info()


# ── Reality bubble — NPC local simulation ─────────────────────────────────────
## How many clock ticks between NPC tile moves (keeps movement readable).
const LOCAL_MOVE_EVERY: int = 4

## Filter characters to NPCs of this settlement; place them on matching anchor tiles.
func _spawn_local_npcs() -> void:
	for old_rect in _npc_rects.values():
		if is_instance_valid(old_rect):
			old_rect.queue_free()
	_npc_rects.clear()

	if _world_state == null or _map_area == null or _settlement_id == "":
		return

	## Track placement count per (role+schedule) key for spreading NPCs.
	var role_counters: Dictionary = {}

	for pid: String in _world_state.characters:
		var npc: PersonState = _world_state.characters[pid]
		if npc.home_settlement_id != _settlement_id:
			continue
		# If this NPC already has a valid position from a previous visit, keep it.
		if npc.location.get("lx", -1) >= 0 and npc.location.get("rx", -1) >= 0:
			_create_npc_pawn(pid, npc)
			continue
		# First visit — find the region cell that contains the NPC's home building.
		# Only use the home cell if it's within the visible viewport radius so the
		# NPC pawn isn't immediately hidden off-screen.
		var target_rx := _reg_rx
		var target_ry := _reg_ry
		if npc.work_cell_id != "":
			var home_rc := _find_region_cell_for_wt_key(npc.work_cell_id)
			if home_rc.x >= 0:
				@warning_ignore("integer_division")
				var vis_radius: int = maxi(1, ceili((_map_cols / 2.0) / GRID_W))
				if absi(home_rc.x - _reg_rx) <= vis_radius and absi(home_rc.y - _reg_ry) <= vis_radius:
					target_rx = home_rc.x
					target_ry = home_rc.y
		var key: String = npc.active_role + "_" + npc.schedule_state
		var offset: int = role_counters.get(key, 0)
		role_counters[key] = offset + 1
		var tile_pos := _find_anchor_tile(target_rx, target_ry, npc.active_role, npc.schedule_state, offset)
		npc.location["lx"]  = tile_pos.x
		npc.location["ly"]  = tile_pos.y
		npc.location["rx"]  = target_rx
		npc.location["ry"]  = target_ry
		npc.location["wt_x"] = _entry_wx
		npc.location["wt_y"] = _entry_wy
		_create_npc_pawn(pid, npc)


## Return the region cell (rx, ry) that was stamped from the given world-tile territory key.
## Returns Vector2i(-1, -1) if the key has no matching cell in the current world tile's grid.
func _find_region_cell_for_wt_key(wt_key: String) -> Vector2i:
	if _world_state == null:
		return Vector2i(-1, -1)
	var wk := "%d,%d" % [_entry_wx, _entry_wy]
	var grid: Dictionary = _world_state.region_grids.get(wk, {})
	for ck: String in grid:
		if grid[ck].get("source_wt_key", "") == wt_key:
			var parts := ck.split(",")
			return Vector2i(int(parts[0]), int(parts[1]))
	return Vector2i(-1, -1)


## Find a role/schedule-appropriate walkable tile in the given region cell's layout.
## offset spreads multiple NPCs with the same role across available candidates.
func _find_anchor_tile(rx: int, ry: int, role: String, schedule: String, offset: int = 0) -> Vector2i:
	var preferred: Array[String] = []
	if schedule == "resting":
		preferred = ["b"]
	elif schedule == "wandering":
		preferred = ["."]
	else:
		match role:
			"innkeeper", "server":     preferred = ["c", "t"]
			"smith":                   preferred = ["f", "A"]
			"grain_keeper":            preferred = ["s", "."]
			"market_keeper", "trader": preferred = ["t", "c"]
			"guard_captain":           preferred = ["+", "."]
			_:                         preferred = ["."]

	var candidates: Array[Vector2i] = []
	for ty: int in range(GRID_H):
		for tx: int in range(GRID_W):
			var ch := _get_cell_char(rx, ry, tx, ty)
			var def: Array = TILE_DEFS.get(ch, TILE_FALLBACK)
			if def[1] and ch in preferred:
				candidates.append(Vector2i(tx, ty))

	if candidates.is_empty():
		for ty2: int in range(GRID_H):
			for tx2: int in range(GRID_W):
				var def2: Array = TILE_DEFS.get(_get_cell_char(rx, ry, tx2, ty2), TILE_FALLBACK)
				if def2[1]:
					candidates.append(Vector2i(tx2, ty2))

	if candidates.is_empty():
		return Vector2i(GRID_W >> 1, GRID_H >> 1)

	# Sort by Manhattan distance from the cell centre so NPCs spawn near the
	# player's entry point rather than clustering at row 0 (row-major order).
	var cx := GRID_W >> 1
	var cy := GRID_H >> 1
	candidates.sort_custom(func(a: Vector2i, b: Vector2i) -> bool:
		return (absi(a.x - cx) + absi(a.y - cy)) < (absi(b.x - cx) + absi(b.y - cy)))

	# Use a prime stride so successive offsets spread across the whole list
	# rather than bunching consecutively.
	@warning_ignore("integer_division")
	var stride: int = maxi(1, candidates.size() / 7)
	return candidates[(offset * stride) % candidates.size()]


## Instantiate a coloured pawn node for one NPC.
func _create_npc_pawn(pid: String, npc: PersonState) -> void:
	if _map_area == null or npc.location.get("lx", -1) < 0:
		return
	var rect := ColorRect.new()
	@warning_ignore("integer_division")
	var pawn_sz: int = maxi(8, _tile_px - 8)
	rect.size         = Vector2(pawn_sz, pawn_sz)
	rect.color        = _npc_pawn_color(npc.population_class)
	rect.mouse_filter = Control.MOUSE_FILTER_IGNORE
	_map_area.add_child(rect)
	_npc_rects[pid] = rect
	var lbl := Label.new()
	lbl.text = npc.active_role.left(1).to_upper() if npc.active_role != "" else "?"
	@warning_ignore("integer_division")
	lbl.add_theme_font_size_override("font_size", clamp(_tile_px / 3, 6, 10))
	lbl.add_theme_color_override("font_color", Color(1.0, 1.0, 1.0, 0.9))
	lbl.position = Vector2(2, 0)
	rect.add_child(lbl)
	_refresh_npc_pawn_pos(pid, npc)


func _npc_pawn_color(pop_class: String) -> Color:
	match pop_class:
		"peasant":  return Color(0.50, 0.80, 0.38, 0.88)
		"artisan":  return Color(0.90, 0.55, 0.18, 0.88)
		"merchant": return Color(0.28, 0.62, 0.92, 0.88)
		"noble":    return Color(0.82, 0.28, 0.82, 0.88)
	return Color(0.60, 0.60, 0.60, 0.88)


func _refresh_all_npc_pawns() -> void:
	if _world_state == null:
		return
	for pid: String in _npc_rects:
		var npc: PersonState = _world_state.characters.get(pid)
		if npc != null:
			_refresh_npc_pawn_pos(pid, npc)


func _refresh_npc_pawn_pos(pid: String, npc: PersonState) -> void:
	var rect: ColorRect = _npc_rects.get(pid) as ColorRect
	if rect == null or not is_instance_valid(rect):
		return
	if npc.location.get("lx", -1) < 0 or npc.location.get("ly", -1) < 0 or npc.location.get("rx", -1) < 0:
		rect.visible = false
		return
	# Convert both player and NPC to absolute tile coords, then project to screen.
	var player_ax := _reg_rx * GRID_W + _player_lx
	var player_ay := _reg_ry * GRID_H + _player_ly
	var npc_ax: int = npc.location.get("rx", 0) * GRID_W + npc.location.get("lx", 0)
	var npc_ay: int = npc.location.get("ry", 0) * GRID_H + npc.location.get("ly", 0)
	@warning_ignore("integer_division")
	var vx: int = _map_cols / 2 + (npc_ax - player_ax)
	@warning_ignore("integer_division")
	var vy: int = _map_rows / 2 + (npc_ay - player_ay)
	if vx < 0 or vx >= _map_cols or vy < 0 or vy >= _map_rows:
		rect.visible = false
		return
	var pawn_sz := rect.size.x
	rect.visible  = true
	rect.position = Vector2(
		vx * _tile_px + (_tile_px - pawn_sz) / 2.0,
		vy * _tile_px + (_tile_px - pawn_sz) / 2.0,
	)


## Called every SimulationClock tick while LocalView is active (reality bubble).
## Only moves NPCs inside this building; all others tick schedule-state only.
func _on_local_tick(tick: int) -> void:
	if _world_state == null:
		return
	if tick % LOCAL_MOVE_EVERY != 0:
		return
	for pid: String in _npc_rects:
		var npc: PersonState = _world_state.characters.get(pid)
		if npc == null or npc.location.get("lx", -1) < 0:
			continue
		_tick_npc_movement(npc)
		_refresh_npc_pawn_pos(pid, npc)


func _tick_npc_movement(npc: PersonState) -> void:
	if npc.location.get("rx", -1) < 0:
		return
	match npc.schedule_state:
		"resting":
			var bed := _find_nearest_tile(npc.location.get("rx", 0), npc.location.get("ry", 0), npc.location.get("lx", 0), npc.location.get("ly", 0), ["b"])
			if bed != Vector2i(npc.location.get("lx", 0), npc.location.get("ly", 0)):
				_step_npc_toward(npc, bed)
		"working":
			var anchor := _find_anchor_tile(npc.location.get("rx", 0), npc.location.get("ry", 0), npc.active_role, "working")
			if Vector2i(npc.location.get("lx", 0), npc.location.get("ly", 0)) != anchor:
				_step_npc_toward(npc, anchor)
		"wandering":
			_wander_npc(npc)
		_:
			pass  # idle: no movement


## Attempt one tile step for an NPC, allowing cell-boundary crossing.
## Returns true if the step succeeded.
func _try_step_npc(npc: PersonState, dx: int, dy: int) -> bool:
	var nx: int = npc.location.get("lx", 0) + dx
	var ny: int = npc.location.get("ly", 0) + dy
	var nrx: int = npc.location.get("rx", 0)
	var nry: int = npc.location.get("ry", 0)
	if nx < 0:          nrx -= 1; nx += GRID_W
	elif nx >= GRID_W:  nrx += 1; nx -= GRID_W
	if ny < 0:          nry -= 1; ny += GRID_H
	elif ny >= GRID_H:  nry += 1; ny -= GRID_H
	if nrx < 0 or nrx >= 250 or nry < 0 or nry >= 250:
		return false
	var def: Array = TILE_DEFS.get(_get_cell_char(nrx, nry, nx, ny), TILE_FALLBACK)
	if def[1]:
		npc.location["lx"] = nx
		npc.location["ly"] = ny
		npc.location["rx"] = nrx
		npc.location["ry"] = nry
		return true
	return false


func _step_npc_toward(npc: PersonState, target: Vector2i) -> void:
	var dx: int = sign(target.x - npc.location.get("lx", 0))
	var dy: int = sign(target.y - npc.location.get("ly", 0))
	for attempt: Vector2i in [Vector2i(dx, dy), Vector2i(dx, 0), Vector2i(0, dy)]:
		if attempt == Vector2i.ZERO:
			continue
		if _try_step_npc(npc, attempt.x, attempt.y):
			return


func _wander_npc(npc: PersonState) -> void:
	# XOR with millisecond time so the same NPC doesn't cycle the same path.
	var h: int = npc.person_id.hash() ^ npc.location.get("lx", 0) ^ (npc.location.get("ly", 0) << 5) ^ Time.get_ticks_msec()
	var dirs: Array[Vector2i] = [
		Vector2i(1, 0), Vector2i(-1, 0), Vector2i(0, 1), Vector2i(0, -1),
	]
	var start: int = h & 3
	for i: int in 4:
		var d: Vector2i = dirs[(start + i) & 3]
		if _try_step_npc(npc, d.x, d.y):
			return


func _find_nearest_tile(reg_rx: int, reg_ry: int, ox: int, oy: int, chars: Array[String]) -> Vector2i:
	var best      := Vector2i(ox, oy)
	var best_dist := 9999
	for ty: int in range(GRID_H):
		for tx: int in range(GRID_W):
			if _get_cell_char(reg_rx, reg_ry, tx, ty) in chars:
				var dist := absi(tx - ox) + absi(ty - oy)
				if dist < best_dist:
					best_dist = dist
					best      = Vector2i(tx, ty)
	return best


# ── Inventory panel ───────────────────────────────────────────────────────────

func _set_inv_visible(inv_on: bool) -> void:
	_inv_visible = inv_on
	if _inv_panel != null:
		_inv_panel.visible = inv_on
	if inv_on:
		_refresh_inv_panel()


func _refresh_inv_panel() -> void:
	if _chest_vbox == null or _carried_vbox == null or _equip_vbox == null:
		return

	var player: PersonState     = _get_player()
	var on_chest: bool = (_get_cell_char(_reg_rx, _reg_ry, _player_lx, _player_ly) == "x")

	# ── Chest ──────────────────────────────────────────────────────────────
	if _chest_hdr != null:
		_chest_hdr.text    = "CHEST" if on_chest else "(move to a chest to loot)"
		_chest_scroll.visible = on_chest

	for child in _chest_vbox.get_children():
		child.queue_free()

	if on_chest:
		var items: Array = _chest_items_list()
		if items.is_empty():
			var lbl := _dim_label("[empty]")
			_chest_vbox.add_child(lbl)
		else:
			var counted: Dictionary = {}
			for iid: String in items:
				counted[iid] = counted.get(iid, 0) + 1
			for iid: String in counted:
				var row := HBoxContainer.new()
				row.add_theme_constant_override("separation", 4)
				_chest_vbox.add_child(row)
				var lbl := Label.new()
				lbl.text = "%s \u00d7%d" % [_item_name(iid), counted[iid]] \
						   if counted[iid] > 1 else _item_name(iid)
				lbl.size_flags_horizontal = Control.SIZE_EXPAND_FILL
				lbl.add_theme_font_size_override("font_size", 10)
				lbl.add_theme_color_override("font_color", COLOR_TEXT)
				row.add_child(lbl)
				if player != null:
					var btn := Button.new()
					btn.text = "Take"
					btn.custom_minimum_size = Vector2(44, 0)
					btn.add_theme_font_size_override("font_size", 9)
					var cap: String = iid
					btn.pressed.connect(func() -> void: _take_from_chest(cap))
					row.add_child(btn)

	# ── Carried ────────────────────────────────────────────────────────────
	for child in _carried_vbox.get_children():
		child.queue_free()

	if player == null:
		_carried_vbox.add_child(_dim_label("[no player]"))
	elif player.carried_items.is_empty():
		_carried_vbox.add_child(_dim_label("[nothing carried]"))
	else:
		var counted: Dictionary = {}
		for iid: String in player.carried_items:
			counted[iid] = counted.get(iid, 0) + 1
		for iid: String in counted:
			var row := HBoxContainer.new()
			row.add_theme_constant_override("separation", 4)
			_carried_vbox.add_child(row)
			var lbl := Label.new()
			lbl.text = _item_name(iid)
			lbl.size_flags_horizontal = Control.SIZE_EXPAND_FILL
			lbl.add_theme_font_size_override("font_size", 10)
			lbl.add_theme_color_override("font_color", COLOR_TEXT)
			row.add_child(lbl)
			if _item_equip_slot(iid) != "":
				var btn := Button.new()
				btn.text = "Equip"
				btn.custom_minimum_size = Vector2(44, 0)
				btn.add_theme_font_size_override("font_size", 9)
				var cap: String = iid
				btn.pressed.connect(func() -> void: _equip_item(cap))
				row.add_child(btn)

	# ── Equipped ───────────────────────────────────────────────────────────
	for child in _equip_vbox.get_children():
		child.queue_free()

	if player == null or player.equipment_refs.is_empty():
		_equip_vbox.add_child(_dim_label("[nothing equipped]"))
	else:
		for slot: String in player.equipment_refs:
			var iid: String = player.equipment_refs[slot]
			var row := HBoxContainer.new()
			row.add_theme_constant_override("separation", 4)
			_equip_vbox.add_child(row)
			var lbl := Label.new()
			lbl.text = "[%s]  %s" % [slot, _item_name(iid)]
			lbl.size_flags_horizontal = Control.SIZE_EXPAND_FILL
			lbl.add_theme_font_size_override("font_size", 10)
			lbl.add_theme_color_override("font_color", COLOR_EQUIPPED)
			row.add_child(lbl)
			var btn := Button.new()
			btn.text         = "\u25bc"
			btn.tooltip_text = "Unequip"
			btn.custom_minimum_size = Vector2(26, 0)
			btn.add_theme_font_size_override("font_size", 9)
			var cap: String = slot
			btn.pressed.connect(func() -> void: _unequip_item(cap))
			row.add_child(btn)


func _dim_label(txt: String) -> Label:
	var lbl := Label.new()
	lbl.text = txt
	lbl.add_theme_color_override("font_color", COLOR_DIM)
	lbl.add_theme_font_size_override("font_size", 10)
	return lbl


## Take one copy of item_id from the chest → carried_items.
func _take_from_chest(item_id: String) -> void:
	if _world_state == null:
		return
	var player: PersonState = _get_player()
	if player == null:
		return
	var items: Array = _chest_items_list()
	var idx: int = items.find(item_id)
	if idx == -1:
		return
	items.remove_at(idx)
	player.carried_items.append(item_id)
	_refresh_inv_panel()


## Equip item_id from carried_items; displaced item returns to carried.
func _equip_item(item_id: String) -> void:
	var player: PersonState = _get_player()
	if player == null:
		return
	var slot := _item_equip_slot(item_id)
	if slot == "":
		return
	var idx: int = player.carried_items.find(item_id)
	if idx == -1:
		return
	player.carried_items.remove_at(idx)
	if player.equipment_refs.has(slot):
		player.carried_items.append(player.equipment_refs[slot])
	player.equipment_refs[slot] = item_id
	_refresh_inv_panel()


## Unequip a slot → the item moves back to carried_items.
func _unequip_item(slot: String) -> void:
	var player: PersonState = _get_player()
	if player == null:
		return
	if not player.equipment_refs.has(slot):
		return
	player.carried_items.append(player.equipment_refs[slot])
	player.equipment_refs.erase(slot)
	_refresh_inv_panel()


# ── Layout helpers ───────────────────────────────────────────────────────────

## Return the building_id for region cell (rx,ry) in the current world tile.
func _cell_building_id(rx: int, ry: int) -> String:
	if _world_state == null:
		return "open_land"
	var wk: String = "%d,%d" % [_entry_wx, _entry_wy]
	var grid: Dictionary = _world_state.region_grids.get(wk, {})
	var ck: String = "%d,%d" % [rx, ry]
	var cell: Dictionary = grid.get(ck, {})
	var bid: String = cell.get("building_id", "")
	return bid if bid != "" else "open_land"

## Human-readable heading for the cell the player is currently standing in.
func _area_label_for_cell(rx: int, ry: int) -> String:
	if _world_state == null:
		return "Wilderness"
	var wk: String = "%d,%d" % [_entry_wx, _entry_wy]
	var grid: Dictionary = _world_state.region_grids.get(wk, {})
	var ck: String = "%d,%d" % [rx, ry]
	var cell: Dictionary = grid.get(ck, {})
	var bid: String = cell.get("building_id", "")
	if bid != "" and bid != "open_land":
		var bdef: Dictionary = ContentRegistry.get_content("building", bid)
		if not bdef.is_empty():
			var bname: String = bdef.get("name", "")
			if bname != "":
				return bname
		return bid.replace("_", " ").capitalize()
	if cell.get("has_road", false):
		return "Road"
	var terrain: String = cell.get("terrain_type", "")
	if terrain != "":
		return terrain.replace("_", " ").capitalize()
	return "Wilderness"

## Return the tile char at (lx, ly) inside region cell (rx, ry).
func _get_cell_char(rx: int, ry: int, lx: int, ly: int) -> String:
	var layout: Array = _load_cell_layout(rx, ry)
	if ly < 0 or ly >= layout.size():
		return "#"
	var row: String = layout[ly]
	if lx < 0 or lx >= row.length():
		return "#"
	return row[lx]

## Return the cached 25×25 layout for cell (rx, ry).
## The 3×3 neighbourhood is always pre-loaded, so this is always a cache hit.
## Returns an empty array for out-of-range boundary tiles (rendered as '#' walls).
func _load_cell_layout(rx: int, ry: int) -> Array:
	return _layout_cache.get("%d,%d" % [rx, ry], [])

## Pre-load the neighbourhood of region cells centred on (rx, ry) into _layout_cache.
## Radius scales with zoom so cells at the viewport edge are always ready.
func _preload_neighbourhood(rx: int, ry: int) -> void:
	@warning_ignore("integer_division")
	var radius: int = (maxi(1, ceili((_map_cols / 2.0) / GRID_W) + 1)) if _map_cols > 0 else 1
	for dy: int in range(-radius, radius + 1):
		for dx: int in range(-radius, radius + 1):
			var cx := rx + dx
			var cy := ry + dy
			if cx < 0 or cy < 0 or cx >= 250 or cy >= 250:
				continue
			var key: String = "%d,%d" % [cx, cy]
			if not _layout_cache.has(key):
				_layout_cache[key] = _build_cell_layout(cx, cy)

## Build (without caching) the 25×25 layout for region cell (rx, ry).
## Tries a static JSON file in data/local_layouts/, then falls back to procedural generation.
func _build_cell_layout(rx: int, ry: int) -> Array:
	var bid := _cell_building_id(rx, ry)
	var layout: Array = []
	if bid != "open_land":
		var path := "res://data/buildings/%s.json" % bid
		if ResourceLoader.exists(path):
			var f := FileAccess.open(path, FileAccess.READ)
			if f != null:
				var parsed = JSON.parse_string(f.get_as_text())
				f.close()
				if parsed is Dictionary:
					var ll = parsed.get("local_layout", [])
					if ll is Array:
						layout = ll
	if layout.is_empty():
		var wk: String = "%d,%d" % [_entry_wx, _entry_wy]
		var grid: Dictionary = {}
		if _world_state != null:
			grid = _world_state.region_grids.get(wk, {})
		var cell: Dictionary = grid.get("%d,%d" % [rx, ry], {})
		layout = _generate_layout_for_cell(rx, ry, cell, grid)
	return layout

## Procedurally generate a 25×25 layout for cell (rx, ry) from its cell dict
## and the surrounding region grid (for road-neighbour direction flags).
func _generate_layout_for_cell(rx: int, ry: int, cell: Dictionary, region: Dictionary) -> Array:
	const ROAD_HALF := 1
	const CX        := 12
	const CY        := 12

	var terrain: String = cell.get("terrain_type", "")
	var base_char: String
	match terrain:
		"forest", "woodland":         base_char = "*"
		"hills", "highland":          base_char = ","
		"mountain":                   base_char = "*"
		"farmland", "cropland":       base_char = "%"
		"marsh", "wetland":           base_char = ","
		"desert", "wasteland":        base_char = ","
		"river", "shallow_water",\
		"coast", "ocean", "lake":     base_char = "~"
		_:                            base_char = "."

	var rows: Array = []
	for _y in GRID_H:
		var row := PackedStringArray()
		row.resize(GRID_W)
		for _x in GRID_W:
			row[_x] = base_char
		rows.append(row)

	var road_self: bool = cell.get("has_road", false)
	if road_self:
		var nc_n: Dictionary = region.get("%d,%d" % [rx,     ry - 1], {})
		var nc_s: Dictionary = region.get("%d,%d" % [rx,     ry + 1], {})
		var nc_e: Dictionary = region.get("%d,%d" % [rx + 1, ry    ], {})
		var nc_w: Dictionary = region.get("%d,%d" % [rx - 1, ry    ], {})
		var road_n: bool = nc_n.get("has_road", false)
		var road_s: bool = nc_s.get("has_road", false)
		var road_e: bool = nc_e.get("has_road", false)
		var road_w: bool = nc_w.get("has_road", false)

		if road_n or road_s:
			var v_top    := CY if not road_n else 0
			var v_bottom := CY if not road_s else GRID_H - 1
			for gy in range(v_top, v_bottom + 1):
				for gx in range(CX - ROAD_HALF, CX + ROAD_HALF + 1):
					rows[gy][gx] = "_"
		if road_w or road_e:
			var h_left  := CX if not road_w else 0
			var h_right := CX if not road_e else GRID_W - 1
			for gy in range(CY - ROAD_HALF, CY + ROAD_HALF + 1):
				for gx in range(h_left, h_right + 1):
					rows[gy][gx] = "_"
		# Centre intersection always stamped.
		for gy in range(CY - ROAD_HALF, CY + ROAD_HALF + 1):
			for gx in range(CX - ROAD_HALF, CX + ROAD_HALF + 1):
				rows[gy][gx] = "_"

	var result: Array = []
	for row: PackedStringArray in rows:
		result.append("".join(row))
	return result

## Redraw every tile node. Player is always at the centre; the world scrolls around them.
func _render_viewport() -> void:
	if _map_cols == 0 or _map_rows == 0 or _tile_rects.size() != _map_cols * _map_rows:
		return
	@warning_ignore("integer_division")
	var half_cols: int = _map_cols / 2
	@warning_ignore("integer_division")
	var half_rows: int = _map_rows / 2
	var player_ax := _reg_rx * GRID_W + _player_lx
	var player_ay := _reg_ry * GRID_H + _player_ly
	for i: int in range(_map_cols * _map_rows):
		var vx: int = i % _map_cols
		@warning_ignore("integer_division")
		var vy: int = i / _map_cols
		var ax: int = player_ax + (vx - half_cols)
		var ay: int = player_ay + (vy - half_rows)
		var crx: int = floori(ax / float(GRID_W))
		var cry: int = floori(ay / float(GRID_H))
		var lx: int  = ax - crx * GRID_W
		var ly: int  = ay - cry * GRID_H
		var ch: String
		if crx < 0 or cry < 0 or crx >= 250 or cry >= 250:
			ch = "#"
		else:
			ch = _get_cell_char(crx, cry, lx, ly)
		var def: Array = TILE_DEFS.get(ch, TILE_FALLBACK)
		_tile_rects[i].color = def[0]
		if _tile_labels[i] != null:
			_tile_labels[i].text = ch if _tile_px >= 16 else ""
