class_name RegionMapView extends Control

## Displays a RegionData (8×8 tiles) for one world tile.
## Built programmatically; add to a CanvasLayer via world_map.gd.
##
## Signals:
##   drill_up()              — user pressed the back button
##   drill_to_local(wx, wy)  — user double-clicked a region tile

signal drill_up()
signal drill_to_local(wx: int, wy: int)

const SIDEBAR_W: int   = 200
const STATUS_H:  int   = 26

# ----- Data -----
var _region: RegionData   = null
var _wx: int              = -1
var _wy: int              = -1

# ----- UI nodes -----
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
const ZOOM_MAX:  float = 32.0
const ZOOM_STEP: float = 0.20


func _ready() -> void:
	set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
	_build_ui()


## Load and render a new region tile.
func load_tile(wx: int, wy: int) -> void:
	_wx = wx
	_wy = wy
	_region = WorldState.get_region(wx, wy)
	_zoom = 1.0
	_pan  = Vector2.ZERO
	_render()
	var prov_id: int   = WorldState.world_data.province_id[wy][wx] if WorldState.world_data else -1
	var prov_name: String = WorldState.get_province_name(prov_id)
	_crumb_label.text = "Region  ›  %s  ›  [%d, %d]" % [prov_name, wx, wy]
	_status_label.text = "Region map — world tile [%d, %d]  |  double-click to enter local view" % [wx, wy]


# ── Rendering ──────────────────────────────────────────────────────────────

func _render() -> void:
	if _region == null:
		return
	var s: int = RegionData.SCALE
	var img  := Image.create(s, s, false, Image.FORMAT_RGB8)
	for ry in range(s):
		for rx in range(s):
			var base: Color = TileRegistry.get_biome_color(_region.biome[ry][rx])
			# Altitude shading (same as biome world view: up to −30%)
			var alt: float = _region.altitude[ry][rx]
			var shade: float = 1.0 - clampf((alt - 0.5) * 0.60, 0.0, 0.30)
			# Feature tint
			var feat: int = _region.feature[ry][rx]
			var tint: Color = _feature_tint(feat)
			var c: Color = Color(
				base.r * shade * (1.0 - tint.a) + tint.r * tint.a,
				base.g * shade * (1.0 - tint.a) + tint.g * tint.a,
				base.b * shade * (1.0 - tint.a) + tint.b * tint.a,
			)
			# River override
			if _region.is_river[ry][rx]:
				c = Color(0.22, 0.54, 0.92)
			img.set_pixel(rx, ry, c)
	# Road overlay: draw a corridor from the region centre toward each
	# connected world-tile neighbour, using the world road_network.
	_paint_roads(img, s, s)
	_map_display.texture = ImageTexture.create_from_image(img)
	_fit_map()


## Overlay road corridors onto an already-painted Image.
## Each road leading to a neighbour world tile is drawn from the image
## centre toward the appropriate edge midpoint (or corner for diagonals).
func _paint_roads(img: Image, iw: int, ih: int) -> void:
	var wd: WorldData = WorldState.world_data
	if wd == null:
		return
	var wkey := Vector2i(_wx, _wy)
	if not wd.road_network.has(wkey):
		return
	const ROAD_COL: Color = Color(0.76, 0.60, 0.35)
	var cx: int = iw / 2
	var cy: int = ih / 2
	# Maps normalised direction Vector2i → edge pixel
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
		# Bresenham line
		var x0: int = cx; var y0: int = cy
		var x1: int = ep.x; var y1: int = ep.y
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


static func _feature_tint(feat: int) -> Color:
	match feat:
		RegionData.RegionFeature.RUINS:        return Color(0.6, 0.5, 0.2, 0.35)
		RegionData.RegionFeature.CAMP:         return Color(0.9, 0.5, 0.1, 0.30)
		RegionData.RegionFeature.MINE_ENTRANCE:return Color(0.5, 0.3, 0.7, 0.35)
		RegionData.RegionFeature.FORD:         return Color(0.3, 0.8, 0.9, 0.30)
		RegionData.RegionFeature.DENSE_FOREST: return Color(0.0, 0.2, 0.0, 0.25)
	return Color(0, 0, 0, 0)


# ── UI construction ────────────────────────────────────────────────────────

func _build_ui() -> void:
	var root := VBoxContainer.new()
	root.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
	add_child(root)

	# ── Header ──────────────────────────────────────────────────
	var header := PanelContainer.new()
	header.custom_minimum_size = Vector2(0, 32)
	root.add_child(header)
	var hdr_box := HBoxContainer.new()
	header.add_child(hdr_box)
	var back_btn := Button.new()
	back_btn.text = "← World Map"
	back_btn.pressed.connect(func(): drill_up.emit())
	hdr_box.add_child(back_btn)
	_crumb_label = Label.new()
	_crumb_label.text = "Region"
	_crumb_label.add_theme_font_size_override("font_size", 13)
	_crumb_label.vertical_alignment = VERTICAL_ALIGNMENT_CENTER
	_crumb_label.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	hdr_box.add_child(_crumb_label)

	# ── Main row (sidebar + map) ─────────────────────────────────
	var hbox := HBoxContainer.new()
	hbox.size_flags_vertical = Control.SIZE_EXPAND_FILL
	root.add_child(hbox)

	_build_sidebar(hbox)

	# Map area
	_map_anchor = Control.new()
	_map_anchor.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	_map_anchor.size_flags_vertical   = Control.SIZE_EXPAND_FILL
	_map_anchor.mouse_filter = Control.MOUSE_FILTER_STOP
	_map_anchor.mouse_exited.connect(func(): _hover_label.text = "—")
	_map_anchor.gui_input.connect(_on_map_input)
	hbox.add_child(_map_anchor)

	_map_display = TextureRect.new()
	_map_display.mouse_filter     = Control.MOUSE_FILTER_IGNORE
	_map_display.expand_mode      = TextureRect.EXPAND_IGNORE_SIZE
	_map_display.stretch_mode     = TextureRect.STRETCH_SCALE
	_map_display.texture_filter   = CanvasItem.TEXTURE_FILTER_NEAREST
	_map_anchor.add_child(_map_display)

	# ── Status bar ───────────────────────────────────────────────
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
	title.text = "REGION VIEW"
	title.add_theme_font_size_override("font_size", 13)
	title.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	vbox.add_child(title)
	vbox.add_child(HSeparator.new())

	var hint := Label.new()
	hint.text = "Hover tile for info.\nDouble-click to enter."
	hint.add_theme_font_size_override("font_size", 11)
	hint.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	vbox.add_child(hint)
	vbox.add_child(HSeparator.new())

	_hover_label = Label.new()
	_hover_label.text = "—"
	_hover_label.add_theme_font_size_override("font_size", 11)
	_hover_label.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	vbox.add_child(_hover_label)

	var enter_btn := Button.new()
	enter_btn.text = "Enter  [↵]"
	enter_btn.pressed.connect(func(): _try_drill_to_local())
	vbox.add_child(enter_btn)

	# Feature legend
	vbox.add_child(HSeparator.new())
	var legend_title := Label.new()
	legend_title.text = "Features"
	legend_title.add_theme_font_size_override("font_size", 11)
	legend_title.modulate = Color(0.75, 0.85, 1.0)
	vbox.add_child(legend_title)
	for pair in [["Ruins", "★"], ["Camp", "⛺"], ["Mine", "⛏"], ["Ford", "~"], ["Dense Forest", "▓"]]:
		var lbl := Label.new()
		lbl.text = "%s  %s" % [pair[1], pair[0]]
		lbl.add_theme_font_size_override("font_size", 11)
		vbox.add_child(lbl)


# ── Input ──────────────────────────────────────────────────────────────────

func _on_map_input(event: InputEvent) -> void:
	if event is InputEventMouseButton:
		if event.button_index == MOUSE_BUTTON_WHEEL_UP and event.pressed:
			_zoom = clampf(_zoom * (1.0 + ZOOM_STEP), ZOOM_MIN, ZOOM_MAX)
			_fit_map()
		elif event.button_index == MOUSE_BUTTON_WHEEL_DOWN and event.pressed:
			_zoom = clampf(_zoom / (1.0 + ZOOM_STEP), ZOOM_MIN, ZOOM_MAX)
			_fit_map()
		elif event.button_index == MOUSE_BUTTON_LEFT and event.pressed:
			if event.double_click:
				_try_drill_to_local_at(event.position)
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


func _try_drill_to_local() -> void:
	if _wx >= 0 and _wy >= 0:
		drill_to_local.emit(_wx, _wy)


func _try_drill_to_local_at(_mouse_pos: Vector2) -> void:
	# Any double-click on the region map drills into the whole tile's local map
	drill_to_local.emit(_wx, _wy)


func _update_hover(mouse_pos: Vector2) -> void:
	if _region == null or _map_display.texture == null:
		return
	var local: Vector2 = mouse_pos - _map_display.position
	var tile_scale: float = _map_display.size.x / float(RegionData.SCALE)
	if tile_scale <= 0.0:
		return
	var rx: int = int(local.x / tile_scale)
	var ry: int = int(local.y / tile_scale)
	if rx < 0 or rx >= RegionData.SCALE or ry < 0 or ry >= RegionData.SCALE:
		_hover_label.text = "—"
		return

	var biome_name: String = TileRegistry.get_biome_name(_region.biome[ry][rx])
	var alt: float         = _region.altitude[ry][rx]
	var feat: int          = _region.feature[ry][rx]
	var feat_name: String  = _feat_name(feat)
	var river_tag: String  = "  [RIVER]" if _region.is_river[ry][rx] else ""

	_hover_label.text = (
		"[%d, %d]\n%s%s\nAlt: %.2f\n%s"
		% [rx, ry, biome_name, river_tag, alt, feat_name]
	)


static func _feat_name(feat: int) -> String:
	match feat:
		RegionData.RegionFeature.RUINS:         return "Feature: Ruins"
		RegionData.RegionFeature.CAMP:          return "Feature: Camp"
		RegionData.RegionFeature.MINE_ENTRANCE: return "Feature: Mine"
		RegionData.RegionFeature.FORD:          return "Feature: Ford"
		RegionData.RegionFeature.DENSE_FOREST:  return "Feature: Dense Forest"
	return ""


# ── Layout ─────────────────────────────────────────────────────────────────

func _fit_map() -> void:
	if _map_display == null or _map_display.texture == null:
		return
	var area: Vector2 = _map_anchor.size
	if area == Vector2.ZERO:
		return
	var tex: Vector2  = _map_display.texture.get_size()
	var fit: float    = minf(area.x / tex.x, area.y / tex.y) * _zoom
	var sz: Vector2   = tex * fit
	_map_display.size     = sz
	_map_display.position = (area - sz) * 0.5 + _pan


func _notification(what: int) -> void:
	if what == NOTIFICATION_RESIZED:
		_fit_map()
