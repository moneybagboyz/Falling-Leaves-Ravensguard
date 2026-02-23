class_name LocalMapView extends Control

## Displays a LocalMapData (48×48 tiles) for one world tile.
## Built programmatically; added to the CanvasLayer by world_map.gd.
##
## Signal:
##   drill_up() — user pressed the back-to-region button

signal drill_up()

const SIDEBAR_W: int = 200
const STATUS_H:  int = 26

# ----- Data -----
var _local: LocalMapData = null
var _wx: int             = -1
var _wy: int             = -1

# ----- UI -----
var _map_anchor:   Control     = null
var _map_display:  TextureRect = null
var _hover_label:  Label       = null
var _status_label: Label       = null
var _crumb_label:  Label       = null

# ----- Zoom / pan -----
var _zoom:       float   = 1.0
var _pan:        Vector2 = Vector2.ZERO
var _pan_origin: Vector2 = Vector2.ZERO
var _panning:    bool    = false

const ZOOM_MIN:  float = 1.0
const ZOOM_MAX:  float = 16.0
const ZOOM_STEP: float = 0.20


func _ready() -> void:
	set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
	_build_ui()


func load_tile(wx: int, wy: int) -> void:
	_wx = wx
	_wy = wy
	_local = WorldState.get_local_map(wx, wy)
	_zoom  = 1.0
	_pan   = Vector2.ZERO
	_render()
	var prov_id: int = WorldState.world_data.province_id[wy][wx] if WorldState.world_data else -1
	var prov_name: String = WorldState.get_province_name(prov_id)
	_crumb_label.text = "Local  ›  %s  ›  [%d, %d]" % [prov_name, wx, wy]
	_status_label.text = "Local map — 48×48 tiles  |  world tile [%d, %d]" % [wx, wy]


# ── Rendering ──────────────────────────────────────────────────────────────

func _render() -> void:
	if _local == null:
		return
	var img := Image.create(_local.width, _local.height, false, Image.FORMAT_RGB8)
	for y in range(_local.height):
		for x in range(_local.width):
			var terr:  int   = _local.terrain[y][x]
			var feat:  int   = _local.feature[y][x]
			var elev:  int   = _local.elevation[y][x]
			var base:  Color = TileRegistry.get_terrain_color(terr)
			# Elevation shading
			var shade: float = 1.0 - clampf((elev - 5) * 0.04, 0.0, 0.25)
			var c: Color = Color(base.r * shade, base.g * shade, base.b * shade)
			# Feature overlay
			c = _apply_feature(c, feat)
			img.set_pixel(x, y, c)
	# Road overlay: draw corridors toward connected world-tile neighbours.
	_paint_roads(img)
	_map_display.texture = ImageTexture.create_from_image(img)
	_fit_map()


## Overlay road corridors for the local map (48×48).
## Roads are 3 px wide so they remain visible at this scale.
func _paint_roads(img: Image) -> void:
	var wd: WorldData = WorldState.world_data
	if wd == null:
		return
	var wkey := Vector2i(_wx, _wy)
	if not wd.road_network.has(wkey):
		return
	const ROAD_COL: Color = Color(0.76, 0.60, 0.35)
	var iw: int = _local.width
	var ih: int = _local.height
	var cx: int = iw / 2
	var cy: int = ih / 2
	var dir_to_edge: Dictionary = {
		Vector2i( 0, -1): Vector2i(iw / 2,        0),
		Vector2i( 1, -1): Vector2i(iw - 1,        0),
		Vector2i( 1,  0): Vector2i(iw - 1,  ih / 2),
		Vector2i( 1,  1): Vector2i(iw - 1,  ih - 1),
		Vector2i( 0,  1): Vector2i(iw / 2,  ih - 1),
		Vector2i(-1,  1): Vector2i(0,        ih - 1),
		Vector2i(-1,  0): Vector2i(0,        ih / 2),
		Vector2i(-1, -1): Vector2i(0,             0),
	}
	for nb: Vector2i in wd.road_network[wkey]:
		var dv := Vector2i(signi(nb.x - _wx), signi(nb.y - _wy))
		var ep: Vector2i = dir_to_edge.get(dv, Vector2i(cx, cy))
		# Draw a 3-pixel-wide corridor using three parallel Bresenham lines
		for offset in [0, -1, 1]:
			var perp_x: int = 0
			var perp_y: int = 0
			if dv.x == 0:   perp_x = offset  # vertical road → widen horizontally
			else:           perp_y = offset  # horizontal/diagonal → widen vertically
			var x0: int = cx + perp_x; var y0: int = cy + perp_y
			var x1: int = ep.x + perp_x; var y1: int = ep.y + perp_y
			var bx: int = absi(x1 - x0)
			var by_: int = absi(y1 - y0)
			var sx: int = 1 if x0 < x1 else -1
			var sy: int = 1 if y0 < y1 else -1
			var err: int = bx - by_
			while true:
				if x0 >= 0 and x0 < iw and y0 >= 0 and y0 < ih:
					img.set_pixel(x0, y0, ROAD_COL)
				if x0 == x1 and y0 == y1:
					break
				var e2: int = 2 * err
				if e2 > -by_:
					err -= by_
					x0 += sx
				if e2 < bx:
					err += bx
					y0 += sy


static func _apply_feature(base: Color, feat: int) -> Color:
	match feat:
		LocalMapData.LocalFeature.TREE:
			return base.darkened(0.15)
		LocalMapData.LocalFeature.DENSE_TREE:
			return base.darkened(0.30)
		LocalMapData.LocalFeature.BOULDER:
			return Color(0.40, 0.38, 0.34)
		LocalMapData.LocalFeature.WATER_SHALLOW:
			return Color(0.22, 0.54, 0.92)
		LocalMapData.LocalFeature.WATER_DEEP:
			return Color(0.06, 0.24, 0.60)
		LocalMapData.LocalFeature.WALL:
			return Color(0.30, 0.28, 0.26)
		LocalMapData.LocalFeature.DOOR:
			return Color(0.65, 0.40, 0.10)
		LocalMapData.LocalFeature.FLOOR:
			return Color(0.70, 0.65, 0.55)
	return base


# ── UI construction ────────────────────────────────────────────────────────

func _build_ui() -> void:
	var root := VBoxContainer.new()
	root.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
	add_child(root)

	# Header
	var header := PanelContainer.new()
	header.custom_minimum_size = Vector2(0, 32)
	root.add_child(header)
	var hdr_box := HBoxContainer.new()
	header.add_child(hdr_box)
	var back_btn := Button.new()
	back_btn.text = "← Region Map"
	back_btn.pressed.connect(func(): drill_up.emit())
	hdr_box.add_child(back_btn)
	_crumb_label = Label.new()
	_crumb_label.text = "Local"
	_crumb_label.add_theme_font_size_override("font_size", 13)
	_crumb_label.vertical_alignment = VERTICAL_ALIGNMENT_CENTER
	_crumb_label.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	hdr_box.add_child(_crumb_label)

	# Main row
	var hbox := HBoxContainer.new()
	hbox.size_flags_vertical = Control.SIZE_EXPAND_FILL
	root.add_child(hbox)

	_build_sidebar(hbox)

	_map_anchor = Control.new()
	_map_anchor.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	_map_anchor.size_flags_vertical   = Control.SIZE_EXPAND_FILL
	_map_anchor.mouse_filter = Control.MOUSE_FILTER_STOP
	_map_anchor.mouse_exited.connect(func(): _hover_label.text = "—")
	_map_anchor.gui_input.connect(_on_map_input)
	hbox.add_child(_map_anchor)

	_map_display = TextureRect.new()
	_map_display.mouse_filter   = Control.MOUSE_FILTER_IGNORE
	_map_display.expand_mode    = TextureRect.EXPAND_IGNORE_SIZE
	_map_display.stretch_mode   = TextureRect.STRETCH_SCALE
	_map_display.texture_filter = CanvasItem.TEXTURE_FILTER_NEAREST
	_map_anchor.add_child(_map_display)

	# Status bar
	var status := PanelContainer.new()
	status.custom_minimum_size = Vector2(0, STATUS_H)
	root.add_child(status)
	_status_label = Label.new()
	_status_label.text = ""
	_status_label.vertical_alignment = VERTICAL_ALIGNMENT_CENTER
	_status_label.add_theme_font_size_override("font_size", 13)
	status.add_child(_status_label)


func _build_sidebar(parent: HBoxContainer) -> void:
	var panel := PanelContainer.new()
	panel.custom_minimum_size = Vector2(SIDEBAR_W, 0)
	panel.size_flags_vertical = Control.SIZE_EXPAND_FILL
	parent.add_child(panel)

	var vbox := VBoxContainer.new()
	vbox.add_theme_constant_override("separation", 4)
	panel.add_child(vbox)

	var title := Label.new()
	title.text = "LOCAL VIEW"
	title.add_theme_font_size_override("font_size", 13)
	title.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	vbox.add_child(title)
	vbox.add_child(HSeparator.new())

	var hint := Label.new()
	hint.text = "Hover tile for details.\n48×48 tile local map."
	hint.add_theme_font_size_override("font_size", 11)
	hint.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	vbox.add_child(hint)
	vbox.add_child(HSeparator.new())

	_hover_label = Label.new()
	_hover_label.text = "—"
	_hover_label.add_theme_font_size_override("font_size", 11)
	_hover_label.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	vbox.add_child(_hover_label)

	# Terrain legend
	vbox.add_child(HSeparator.new())
	var leg_title := Label.new()
	leg_title.text = "Terrain"
	leg_title.add_theme_font_size_override("font_size", 11)
	leg_title.modulate = Color(0.75, 0.85, 1.0)
	vbox.add_child(leg_title)
	var TERRAIN_NAMES: Array = [
		"Ocean", "Shallow Water", "Coast", "Plains", "Hills",
		"Forest", "Mountain", "Desert", "River", "Lake"
	]
	for i in range(TERRAIN_NAMES.size()):
		var row := HBoxContainer.new()
		var swatch := ColorRect.new()
		swatch.color = TileRegistry.get_terrain_color(i)
		swatch.custom_minimum_size = Vector2(12, 12)
		row.add_child(swatch)
		var lbl := Label.new()
		lbl.text = "  " + TERRAIN_NAMES[i]
		lbl.add_theme_font_size_override("font_size", 10)
		row.add_child(lbl)
		vbox.add_child(row)


# ── Input ──────────────────────────────────────────────────────────────────

func _on_map_input(event: InputEvent) -> void:
	if event is InputEventMouseButton:
		if event.button_index == MOUSE_BUTTON_WHEEL_UP and event.pressed:
			_zoom = clampf(_zoom * (1.0 + ZOOM_STEP), ZOOM_MIN, ZOOM_MAX)
			_fit_map()
		elif event.button_index == MOUSE_BUTTON_WHEEL_DOWN and event.pressed:
			_zoom = clampf(_zoom / (1.0 + ZOOM_STEP), ZOOM_MIN, ZOOM_MAX)
			_fit_map()
		elif event.button_index == MOUSE_BUTTON_MIDDLE:
			_panning = event.pressed
			_pan_origin = event.position - _pan
		elif event.button_index == MOUSE_BUTTON_RIGHT and event.pressed:
			_zoom = 1.0; _pan = Vector2.ZERO; _fit_map()

	elif event is InputEventMouseMotion:
		if _panning:
			_pan = event.position - _pan_origin
			_fit_map()
		_update_hover(event.position)


func _update_hover(mouse_pos: Vector2) -> void:
	if _local == null or _map_display.texture == null:
		return
	var local_pos: Vector2 = mouse_pos - _map_display.position
	var tile_scale: float  = _map_display.size.x / float(_local.width)
	if tile_scale <= 0.0:
		return
	var lx: int = int(local_pos.x / tile_scale)
	var ly: int = int(local_pos.y / tile_scale)
	if lx < 0 or lx >= _local.width or ly < 0 or ly >= _local.height:
		_hover_label.text = "—"
		return

	var terr:  int   = _local.terrain[ly][lx]
	var feat:  int   = _local.feature[ly][lx]
	var elev:  int   = _local.elevation[ly][lx]
	var pass_str: String = "yes" if _local.passable[ly][lx] else "no"

	_hover_label.text = (
		"[%d, %d]\nTerrain: %s\nFeature: %s\nElev: %d\nPassable: %s"
		% [lx, ly, _terrain_name(terr), _feature_name(feat), elev, pass_str]
	)


static func _terrain_name(t: int) -> String:
	match t:
		WorldData.TerrainType.OCEAN:         return "Ocean"
		WorldData.TerrainType.SHALLOW_WATER: return "Shallow Water"
		WorldData.TerrainType.COAST:         return "Coast"
		WorldData.TerrainType.PLAINS:        return "Plains"
		WorldData.TerrainType.HILLS:         return "Hills"
		WorldData.TerrainType.FOREST:        return "Forest"
		WorldData.TerrainType.MOUNTAIN:      return "Mountain"
		WorldData.TerrainType.DESERT:        return "Desert"
		WorldData.TerrainType.RIVER:         return "River"
		WorldData.TerrainType.LAKE:          return "Lake"
	return "Unknown"


static func _feature_name(f: int) -> String:
	match f:
		LocalMapData.LocalFeature.TREE:          return "Tree"
		LocalMapData.LocalFeature.DENSE_TREE:    return "Dense Tree"
		LocalMapData.LocalFeature.BOULDER:       return "Boulder"
		LocalMapData.LocalFeature.WATER_SHALLOW: return "Shallow Water"
		LocalMapData.LocalFeature.WATER_DEEP:    return "Deep Water"
		LocalMapData.LocalFeature.WALL:          return "Wall"
		LocalMapData.LocalFeature.DOOR:          return "Door"
		LocalMapData.LocalFeature.FLOOR:         return "Floor"
	return "None"


# ── Layout ─────────────────────────────────────────────────────────────────

func _fit_map() -> void:
	if _map_display == null or _map_display.texture == null:
		return
	var area: Vector2 = _map_anchor.size
	if area == Vector2.ZERO:
		return
	var tex: Vector2 = _map_display.texture.get_size()
	var fit: float   = minf(area.x / tex.x, area.y / tex.y) * _zoom
	var sz: Vector2  = tex * fit
	_map_display.size     = sz
	_map_display.position = (area - sz) * 0.5 + _pan


func _notification(what: int) -> void:
	if what == NOTIFICATION_RESIZED:
		_fit_map()
