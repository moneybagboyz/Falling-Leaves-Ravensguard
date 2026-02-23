extends Node2D

## ============================================================
## WorldMap — main controller for the top-down world generator.
## All UI is built programmatically so no extra .tscn editing
## is required. Just attach this script to the root Node2D.
## ============================================================

# ------------------------------------------------------------------ #
# Keyboard shortcuts (matches reference project + extras)             #
# ------------------------------------------------------------------ #
# R           — regenerate                                            #
# B           — Biome mode                                            #
# A           — Altitude mode                                         #
# T           — Temperature mode                                      #
# P           — Precipitation mode                                    #
# D           — Drainage mode                                         #
# F           — Prosperity (Fertility) mode                           #
# Scroll      — Zoom in / out                                         #
# Middle-drag — Pan                                                   #
# ------------------------------------------------------------------ #

const SIDEBAR_WIDTH: int  = 275
const STATUS_HEIGHT: int  = 26
const ZOOM_MIN: float     = 0.25
const ZOOM_MAX: float     = 16.0
const ZOOM_STEP: float    = 0.15

# ----- State -----
var world_data: WorldData  = null
var current_mode: MapRenderer.ViewMode = MapRenderer.ViewMode.BIOME
var world_width:  int   = 256
var world_height: int   = 256
var world_seed:   int   = 0
var _params: WorldGenParams = null

# ----- UI nodes -----
var _map_anchor:    Control     = null  ## area to the right of sidebar
var _map_display:   TextureRect = null
var _hover_label:   Label       = null
var _status_label:  Label       = null
var _seed_spin:     SpinBox     = null
var _width_spin:    SpinBox     = null
var _height_spin:   SpinBox     = null
var _mode_buttons:  Array[Button] = []
var _gen_button:    Button      = null
var _start_btn:     Button         = null   ## "Start Game" — enabled after first gen

# ----- Play mode -----
var _play_mode: bool           = false
var _gen_panel: PanelContainer = null   ## generator sidebar (hidden in play mode)
var _play_panel: PanelContainer = null  ## overworld HUD (shown in play mode)
var _play_day_lbl:   Label     = null
var _play_hover_lbl: Label     = null

# ----- World-settings sliders -----
var _sl_sea_ratio:       HSlider = null
var _sl_falloff:         HSlider = null
var _sl_frequency:       HSlider = null
var _sl_octaves:         HSlider = null
var _sl_noise_factor:    HSlider = null
var _sl_crust_factor:    HSlider = null
var _sl_tectonic_factor: HSlider = null
var _sl_crust_freq:      HSlider = null
var _sl_temp_bias:       HSlider = null
var _sl_precip_bias:     HSlider = null
var _sl_river_thresh:    HSlider = null
var _sl_lake_fill:       HSlider = null
var _sl_provinces:       HSlider = null
var _sl_density:         HSlider = null

# ----- Zoom / pan -----
var _zoom: float        = 1.0
var _pan:  Vector2      = Vector2.ZERO
var _pan_origin: Vector2 = Vector2.ZERO
var _panning: bool      = false
var _tile_pinned: bool  = false  ## true while a tile is clicked-locked in the info panel

# ----- Drill-down navigation -----
var _canvas:       CanvasLayer    = null
var _world_ui:     Control        = null
var _region_view:  RegionMapView  = null
var _local_view:   LocalMapView   = null

# ----- Thread -----
var _thread:        Thread      = null
var _pending_data:  WorldData   = null
var _generating:    bool        = false


# ==================================================================
# Lifecycle
# ==================================================================

func _ready() -> void:
	_params = WorldGenParams.new()
	get_tree().root.size_changed.connect(_on_viewport_resize)
	_build_ui()
	call_deferred("_generate_world")


func _process(_delta: float) -> void:
	# Poll thread result on the main thread.
	if _pending_data != null:
		world_data = _pending_data
		_pending_data = null
		_thread.wait_to_finish()
		_thread = null
		_generating = false
		_tile_pinned = false
		_hover_label.text = "—"
		WorldState.update_world(world_data)
		_render_map()
		_status_label.text = "Ready  |  seed: %d  |  %dx%d" % [
			world_data.world_seed, world_data.width, world_data.height]
		_gen_button.disabled = false
		_gen_button.text = "Generate  [R]"
		_start_btn.disabled = false

	# Update play HUD clock display each frame.
	if _play_mode and _play_day_lbl != null:
		_play_day_lbl.text = "Day %d — %02d:00" % [GameClock.day(), GameClock.hour_of_day()]
		var paused_tag: String = "  |  PAUSED" if GameClock.paused else ""
		_status_label.text = "Day %d  |  %02d:00%s" % [GameClock.day(), GameClock.hour_of_day(), paused_tag]


# ==================================================================
# UI construction
# ==================================================================

func _build_ui() -> void:
	_canvas = CanvasLayer.new()
	_canvas.name = "UI"
	add_child(_canvas)

	_world_ui = VBoxContainer.new()
	_world_ui.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
	_canvas.add_child(_world_ui)

	# ── Top row ──────────────────────────────────────────────────
	var hbox := HBoxContainer.new()
	hbox.size_flags_vertical = Control.SIZE_EXPAND_FILL
	_world_ui.add_child(hbox)

	_gen_panel = _build_sidebar(hbox)
	_play_panel = _build_play_sidebar()
	_play_panel.hide()
	hbox.add_child(_play_panel)
	_build_map_area(hbox)

	# ── Status bar ───────────────────────────────────────────────
	var status_panel := PanelContainer.new()
	status_panel.custom_minimum_size = Vector2(0, STATUS_HEIGHT)
	_world_ui.add_child(status_panel)

	_status_label = Label.new()
	_status_label.text = "Generating…"
	_status_label.vertical_alignment = VERTICAL_ALIGNMENT_CENTER
	_status_label.add_theme_font_size_override("font_size", 13)
	status_panel.add_child(_status_label)


func _build_sidebar(parent: HBoxContainer) -> PanelContainer:
	var panel := PanelContainer.new()
	panel.custom_minimum_size = Vector2(SIDEBAR_WIDTH, 0)
	panel.size_flags_vertical = Control.SIZE_EXPAND_FILL
	parent.add_child(panel)

	var scroll := ScrollContainer.new()
	scroll.size_flags_vertical = Control.SIZE_EXPAND_FILL
	scroll.horizontal_scroll_mode = ScrollContainer.SCROLL_MODE_DISABLED
	panel.add_child(scroll)

	var vbox := VBoxContainer.new()
	vbox.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	vbox.add_theme_constant_override("separation", 5)
	scroll.add_child(vbox)

	# ── Title ────────────────────────────────────────────────────
	var title := Label.new()
	title.text = "WORLD GENERATOR"
	title.add_theme_font_size_override("font_size", 15)
	title.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	vbox.add_child(title)

	vbox.add_child(HSeparator.new())

	# ── Map size & seed ───────────────────────────────────────────
	vbox.add_child(_make_label("Map Width"))
	_width_spin = _make_spinbox(64, 1024, world_width, 64)
	vbox.add_child(_width_spin)

	vbox.add_child(_make_label("Map Height"))
	_height_spin = _make_spinbox(64, 1024, world_height, 64)
	vbox.add_child(_height_spin)

	vbox.add_child(_make_label("Seed  (0 = random)"))
	_seed_spin = _make_spinbox(0, 2147483647, 0, 1)
	vbox.add_child(_seed_spin)

	_gen_button = Button.new()
	_gen_button.text = "Generate  [R]"
	_gen_button.pressed.connect(_generate_world)
	vbox.add_child(_gen_button)

	_start_btn = Button.new()
	_start_btn.text = "▶  Start Game"
	_start_btn.disabled = true
	_start_btn.pressed.connect(_start_game)
	vbox.add_child(_start_btn)

	vbox.add_child(HSeparator.new())

	# ── World Settings ────────────────────────────────────────────
	var settings_title := Label.new()
	settings_title.text = "WORLD SETTINGS"
	settings_title.add_theme_font_size_override("font_size", 13)
	settings_title.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	vbox.add_child(settings_title)

	# Presets
	vbox.add_child(_make_label("Presets"))

	var presets: Array = [
		["Default",      WorldGenParams.make_default()],
		["Pangaea",      WorldGenParams.make_pangaea()],
		["Archipelago",  WorldGenParams.make_archipelago()],
		["Ice Age",      WorldGenParams.make_ice_age()],
		["Desert",       WorldGenParams.make_desert()],
		["Lush",         WorldGenParams.make_lush()],
		["Ring of Fire", WorldGenParams.make_ring_of_fire()],
		["Craton",       WorldGenParams.make_ancient_craton()],
		["Rift Valley",  WorldGenParams.make_rift_valley()],
		["Hothouse",     WorldGenParams.make_hothouse()],
		["Snowball",     WorldGenParams.make_snowball()],
		["Monsoon",      WorldGenParams.make_monsoon()],
		["Inland Sea",   WorldGenParams.make_inland_sea()],
		["Fractal",      WorldGenParams.make_fractal_coast()],
		["Highlands",    WorldGenParams.make_highlands()],
	]
	var grid := GridContainer.new()
	grid.columns = 3
	grid.add_theme_constant_override("h_separation", 4)
	grid.add_theme_constant_override("v_separation", 4)
	vbox.add_child(grid)
	for entry in presets:
		var preset_btn := Button.new()
		preset_btn.text = entry[0]
		preset_btn.add_theme_font_size_override("font_size", 11)
		var p: WorldGenParams = entry[1]
		preset_btn.pressed.connect(_apply_preset.bind(p))
		grid.add_child(preset_btn)

	vbox.add_child(HSeparator.new())

	# Shape sliders
	vbox.add_child(_make_section_label("── Shape"))
	var sr := _make_slider("Sea Ratio", 0.20, 0.70, _params.sea_ratio, 0.05)
	_sl_sea_ratio = sr[1];  vbox.add_child(sr[0])
	var fo := _make_slider("Island Falloff", 0.10, 0.70, _params.island_falloff, 0.05)
	_sl_falloff = fo[1];  vbox.add_child(fo[0])

	# Terrain sliders
	vbox.add_child(_make_section_label("── Terrain"))
	var fr := _make_slider("Noise Frequency", 0.001, 0.008, _params.noise_frequency, 0.0005)
	_sl_frequency = fr[1];  vbox.add_child(fr[0])
	var oc := _make_slider("Octaves", 3, 9, _params.noise_octaves, 1)
	_sl_octaves = oc[1];  vbox.add_child(oc[0])

	# Tectonic sliders
	vbox.add_child(_make_section_label("── Tectonics"))
	var nf := _make_slider("Terrain Weight",   0.10, 0.80, _params.noise_factor,    0.05)
	_sl_noise_factor = nf[1];  vbox.add_child(nf[0])
	var cf := _make_slider("Plate Weight",     0.10, 0.80, _params.crust_factor,    0.05)
	_sl_crust_factor = cf[1];  vbox.add_child(cf[0])
	var tf := _make_slider("Mountain Ridge",   0.00, 0.70, _params.tectonic_factor, 0.05)
	_sl_tectonic_factor = tf[1];  vbox.add_child(tf[0])
	var cfr := _make_slider("Plate Scale",     0.001, 0.006, _params.crust_frequency, 0.0005)
	_sl_crust_freq = cfr[1];  vbox.add_child(cfr[0])

	# Climate sliders
	vbox.add_child(_make_section_label("── Climate"))
	var tb := _make_slider("Temp Bias", -0.30, 0.30, _params.temp_bias, 0.05)
	_sl_temp_bias = tb[1];  vbox.add_child(tb[0])
	var pb := _make_slider("Precip Bias", -0.30, 0.30, _params.precip_bias, 0.05)
	_sl_precip_bias = pb[1];  vbox.add_child(pb[0])

	# Hydrology sliders
	vbox.add_child(_make_section_label("── Hydrology"))
	var rt := _make_slider("River Threshold", 20.0, 150.0, _params.river_threshold, 5.0)
	_sl_river_thresh = rt[1];  vbox.add_child(rt[0])
	var lf := _make_slider("Lake Fill Depth", 0.005, 0.080, _params.lake_fill_depth, 0.005)
	_sl_lake_fill = lf[1];  vbox.add_child(lf[0])

	# Settlements sliders
	vbox.add_child(_make_section_label("── Settlements"))
	var pc := _make_slider("Province Count", 8, 60, _params.num_provinces, 1)
	_sl_provinces = pc[1];  vbox.add_child(pc[0])
	var sd := _make_slider("Settlement Density", 6, 24, _params.tiles_per_settlement, 1)
	_sl_density = sd[1];  vbox.add_child(sd[0])

	vbox.add_child(HSeparator.new())

	# ── View mode buttons ─────────────────────────────────────────
	vbox.add_child(_make_label("View Mode"))

	var modes: Array = [
		["Biome  [B]",        MapRenderer.ViewMode.BIOME],
		["Altitude  [A]",     MapRenderer.ViewMode.ALTITUDE],
		["Temperature  [T]",  MapRenderer.ViewMode.TEMPERATURE],
		["Precipitation  [P]",MapRenderer.ViewMode.PRECIPITATION],
		["Drainage  [D]",     MapRenderer.ViewMode.DRAINAGE],
		["Prosperity  [F]",   MapRenderer.ViewMode.PROSPERITY],
		["River Flow  [V]",   MapRenderer.ViewMode.FLOW],
		["Provinces  [Q]",    MapRenderer.ViewMode.PROVINCES],
	]

	for entry in modes:
		var btn := Button.new()
		btn.text = entry[0]
		btn.toggle_mode = true
		btn.button_pressed = (entry[1] == current_mode)
		var mode_val: MapRenderer.ViewMode = entry[1]
		btn.pressed.connect(_on_mode_button.bind(mode_val, btn))
		vbox.add_child(btn)
		_mode_buttons.append(btn)

	vbox.add_child(HSeparator.new())

	# ── Hover info ────────────────────────────────────────────────
	vbox.add_child(_make_label("Tile Info"))
	_hover_label = Label.new()
	_hover_label.text = "—"
	_hover_label.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	_hover_label.add_theme_font_size_override("font_size", 12)
	vbox.add_child(_hover_label)

	vbox.add_child(HSeparator.new())

	# ── Tips ──────────────────────────────────────────────────────
	var tips := Label.new()
	tips.text = "Scroll: zoom\nMiddle-drag: pan\nRight-click: reset view"
	tips.add_theme_font_size_override("font_size", 11)
	tips.modulate = Color(0.7, 0.7, 0.7)
	tips.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	vbox.add_child(tips)

	return panel


# ==================================================================
# Play sidebar and Start Game
# ==================================================================

func _build_play_sidebar() -> PanelContainer:
	var panel := PanelContainer.new()
	panel.custom_minimum_size = Vector2(SIDEBAR_WIDTH, 0)
	panel.size_flags_vertical = Control.SIZE_EXPAND_FILL

	var vbox := VBoxContainer.new()
	vbox.size_flags_vertical = Control.SIZE_EXPAND_FILL
	vbox.add_theme_constant_override("separation", 6)
	panel.add_child(vbox)

	var title := Label.new()
	title.text = "OVERWORLD"
	title.add_theme_font_size_override("font_size", 15)
	title.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	vbox.add_child(title)

	vbox.add_child(HSeparator.new())

	_play_day_lbl = Label.new()
	_play_day_lbl.text = "Day 1 — 00:00"
	_play_day_lbl.add_theme_font_size_override("font_size", 13)
	_play_day_lbl.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	vbox.add_child(_play_day_lbl)

	vbox.add_child(HSeparator.new())

	vbox.add_child(_make_label("Speed"))
	var speed_row := HBoxContainer.new()
	speed_row.add_theme_constant_override("separation", 3)
	vbox.add_child(speed_row)

	# Pause toggle
	var pause_btn := Button.new()
	pause_btn.text = "⏸"
	pause_btn.toggle_mode = true
	pause_btn.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	pause_btn.toggled.connect(func(on: bool): GameClock.paused = on)
	speed_row.add_child(pause_btn)

	# Speed presets
	for entry: Array in [["1×", 1.0], ["5×", 0.2], ["25×", 0.04]]:
		var btn := Button.new()
		btn.text = entry[0]
		btn.size_flags_horizontal = Control.SIZE_EXPAND_FILL
		var sv: float = entry[1]
		btn.pressed.connect(func():
			GameClock.time_scale = sv
			GameClock.paused = false
			pause_btn.button_pressed = false
		)
		speed_row.add_child(btn)

	vbox.add_child(HSeparator.new())

	vbox.add_child(_make_label("Tile Info"))
	_play_hover_lbl = Label.new()
	_play_hover_lbl.text = "—"
	_play_hover_lbl.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	_play_hover_lbl.add_theme_font_size_override("font_size", 12)
	vbox.add_child(_play_hover_lbl)

	vbox.add_child(HSeparator.new())

	var play_tips := Label.new()
	play_tips.text = "Scroll: zoom\nMiddle-drag: pan\nRight-click: reset view\nDouble-click: drill in\nSpace: pause / unpause"
	play_tips.add_theme_font_size_override("font_size", 11)
	play_tips.modulate = Color(0.7, 0.7, 0.7)
	play_tips.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	vbox.add_child(play_tips)

	return panel


func _start_game() -> void:
	if world_data == null:
		return
	_play_mode = true
	_gen_panel.hide()
	_play_panel.show()
	GameClock.paused = false
	_status_label.text = "Day 1 — overworld simulation running  |  Space to pause"


func _build_map_area(parent: HBoxContainer) -> void:
	_map_anchor = Control.new()
	_map_anchor.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	_map_anchor.size_flags_vertical   = Control.SIZE_EXPAND_FILL
	_map_anchor.clip_contents = true
	_map_anchor.gui_input.connect(_on_map_input)
	_map_anchor.mouse_exited.connect(func():
		if not _tile_pinned:
			_hover_label.text = "—")
	parent.add_child(_map_anchor)

	# Background colour for the map area.
	var bg := ColorRect.new()
	bg.color = Color(0.10, 0.10, 0.12)
	bg.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
	_map_anchor.add_child(bg)

	# The actual texture that shows the rendered map.
	_map_display = TextureRect.new()
	_map_display.expand_mode = TextureRect.EXPAND_IGNORE_SIZE
	# STRETCH_SCALE fills the rect exactly; _fit_map_to_anchor handles aspect ratio manually
	# so that pixel→tile coordinate math in _update_hover is straightforward.
	_map_display.stretch_mode = TextureRect.STRETCH_SCALE
	# Nearest-neighbour filtering keeps tile pixels as hard squares when zoomed in.
	_map_display.texture_filter = CanvasItem.TEXTURE_FILTER_NEAREST
	_map_anchor.add_child(_map_display)

	# Fit to fill anchor after layout settles.
	_map_anchor.resized.connect(_fit_map_to_anchor)


# ==================================================================
# Generation
# ==================================================================

func _generate_world() -> void:
	if _generating:
		return

	world_width  = int(_width_spin.value)
	world_height = int(_height_spin.value)
	world_seed   = int(_seed_spin.value)

	# Read all slider values into _params.
	_params.sea_ratio       = _sl_sea_ratio.value
	_params.island_falloff  = _sl_falloff.value
	_params.noise_frequency = _sl_frequency.value
	_params.noise_octaves   = int(_sl_octaves.value)
	_params.noise_factor    = _sl_noise_factor.value
	_params.crust_factor    = _sl_crust_factor.value
	_params.tectonic_factor = _sl_tectonic_factor.value
	_params.crust_frequency = _sl_crust_freq.value
	_params.temp_bias       = _sl_temp_bias.value
	_params.precip_bias     = _sl_precip_bias.value
	_params.river_threshold = _sl_river_thresh.value
	_params.lake_fill_depth = _sl_lake_fill.value
	_params.num_provinces        = int(_sl_provinces.value)
	_params.tiles_per_settlement = int(_sl_density.value)

	_generating = true
	_gen_button.disabled = true
	_gen_button.text = "Generating…"
	_status_label.text = "Generating %dx%d world…" % [world_width, world_height]

	_thread = Thread.new()
	_thread.start(_thread_generate.bind(world_width, world_height, world_seed, _params.duplicate()))


func _thread_generate(w: int, h: int, s: int, params: WorldGenParams) -> void:
	var data := WorldGenerator.generate(w, h, s, params)
	# Signal back to _process via shared variable.
	_pending_data = data


func _render_map() -> void:
	if world_data == null:
		return
	var img: Image = MapRenderer.render_image(world_data, current_mode)
	# Overlay roads first so settlement dots appear on top
	_paint_roads(img)
	# Overlay settlement dots (3×3 pixels, colour-coded by tier)
	for s in WorldState.settlements:
		_paint_settlement_dot(img, s.tile_x, s.tile_y, s.tier)
	_map_display.texture = ImageTexture.create_from_image(img)
	_fit_map_to_anchor()


## Paints all roads from world_data.road_network as 1-px tan/brown lines.
## Each entry in road_network is a Vector2i tile with outgoing neighbours;
## we draw only A→B where A < B (by index) to avoid double-painting.
func _paint_roads(img: Image) -> void:
	if world_data == null or world_data.road_network.is_empty():
		return
	var w: int = img.get_width()
	var h: int = img.get_height()
	const ROAD_COLOR: Color = Color(0.76, 0.60, 0.35, 0.90)  # warm tan-brown
	for tile in world_data.road_network:
		var neighbours: Array = world_data.road_network[tile]
		for nb: Vector2i in neighbours:
			# Paint each segment only once (lower tile paints to higher)
			if tile.x > nb.x or (tile.x == nb.x and tile.y > nb.y):
				continue
			# Bresenham line between tile and nb
			var x0: int = tile.x
			var y0: int = tile.y
			var x1: int = nb.x
			var y1: int = nb.y
			var dx: int = absi(x1 - x0)
			var dy: int = absi(y1 - y0)
			var sx: int = 1 if x0 < x1 else -1
			var sy: int = 1 if y0 < y1 else -1
			var err: int = dx - dy
			while true:
				if x0 >= 0 and x0 < w and y0 >= 0 and y0 < h:
					img.set_pixel(x0, y0, ROAD_COLOR)
				if x0 == x1 and y0 == y1:
					break
				var e2: int = err * 2
				if e2 > -dy:
					err -= dy
					x0  += sx
				if e2 < dx:
					err += dx
					y0  += sy


## Paints a settlement dot sized by tier.
## Hamlet/Village = 1×1 px; Town/City = 2×2 px; Metropolis = 3×3 px.
## Colour encodes tier: hamlet=grey, village=yellow, town=orange,
## city=red, metropolis=bright magenta.
func _paint_settlement_dot(img: Image, tx: int, ty: int, tier: int) -> void:
	if img == null or tx < 0 or ty < 0 or tx >= img.get_width() or ty >= img.get_height():
		return
	const TIER_COLORS: Array = [
		Color(0.72, 0.72, 0.72),  # 0 Hamlet — light grey
		Color(1.00, 0.95, 0.20),  # 1 Village — yellow
		Color(1.00, 0.60, 0.10),  # 2 Town — orange
		Color(0.95, 0.10, 0.10),  # 3 City — red
		Color(0.90, 0.10, 0.90),  # 4 Metropolis — magenta
	]
	var col: Color = TIER_COLORS[clampi(tier, 0, TIER_COLORS.size() - 1)]
	# Dot size grows with tier: 0-1 → 1×1, 2-3 → 2×2, 4 → 3×3
	var radius: int = 0 if tier <= 1 else (1 if tier <= 3 else 2)
	var w: int = img.get_width()
	var h: int = img.get_height()
	for dy in range(-radius, radius + 1):
		for dx in range(-radius, radius + 1):
			var px: int = tx + dx
			var py: int = ty + dy
			if px >= 0 and px < w and py >= 0 and py < h:
				img.set_pixel(px, py, col)


# ==================================================================
# Display helpers
# ==================================================================

func _fit_map_to_anchor() -> void:
	if _map_display == null or _map_display.texture == null:
		return
	var area: Vector2 = _map_anchor.size
	if area == Vector2.ZERO:
		return

	var tex_size: Vector2 = _map_display.texture.get_size()
	var scale_x: float    = area.x / tex_size.x
	var scale_y: float    = area.y / tex_size.y
	var fit_scale: float  = minf(scale_x, scale_y) * _zoom
	var new_size: Vector2 = tex_size * fit_scale

	_map_display.size     = new_size
	_map_display.position = (area - new_size) * 0.5 + _pan


func _set_mode(mode: MapRenderer.ViewMode) -> void:
	current_mode = mode
	for i in _mode_buttons.size():
		_mode_buttons[i].button_pressed = (i == int(mode))
	_render_map()


func _reset_view() -> void:
	_zoom = 1.0
	_pan  = Vector2.ZERO
	_fit_map_to_anchor()


# ==================================================================
# Input
# ==================================================================

func _unhandled_key_input(event: InputEvent) -> void:
	if not event is InputEventKey or not event.pressed:
		return
	match event.keycode:
		KEY_R:
			if not _play_mode:
				_generate_world()
		KEY_SPACE:
			if _play_mode:
				GameClock.paused = not GameClock.paused
		KEY_B: _set_mode(MapRenderer.ViewMode.BIOME)
		KEY_A: _set_mode(MapRenderer.ViewMode.ALTITUDE)
		KEY_T: _set_mode(MapRenderer.ViewMode.TEMPERATURE)
		KEY_P: _set_mode(MapRenderer.ViewMode.PRECIPITATION)
		KEY_D: _set_mode(MapRenderer.ViewMode.DRAINAGE)
		KEY_F: _set_mode(MapRenderer.ViewMode.PROSPERITY)
		KEY_V: _set_mode(MapRenderer.ViewMode.FLOW)
		KEY_Q: _set_mode(MapRenderer.ViewMode.PROVINCES)


func _on_map_input(event: InputEvent) -> void:
	# ── Zoom ─────────────────────────────────────────────────────
	if event is InputEventMouseButton:
		if event.button_index == MOUSE_BUTTON_WHEEL_UP and event.pressed:
			_zoom = clampf(_zoom * (1.0 + ZOOM_STEP), ZOOM_MIN, ZOOM_MAX)
			_fit_map_to_anchor()
		elif event.button_index == MOUSE_BUTTON_WHEEL_DOWN and event.pressed:
			_zoom = clampf(_zoom / (1.0 + ZOOM_STEP), ZOOM_MIN, ZOOM_MAX)
			_fit_map_to_anchor()
		elif event.button_index == MOUSE_BUTTON_LEFT and event.pressed:
			if event.double_click:
				_drill_to_region_at(event.position)
			elif _tile_pinned:
				_tile_pinned = false
				_update_hover(event.position)
			else:
				_tile_pinned = true
				_pin_tile(event.position)
		elif event.button_index == MOUSE_BUTTON_MIDDLE:
			_panning = event.pressed
			_pan_origin = event.position - _pan
		elif event.button_index == MOUSE_BUTTON_RIGHT and event.pressed:
			_reset_view()

	# ── Pan ──────────────────────────────────────────────────────
	elif event is InputEventMouseMotion:
		if _panning:
			_pan = event.position - _pan_origin
			_fit_map_to_anchor()

		# ── Hover info ────────────────────────────────────────────
		if not _tile_pinned:
			_update_hover(event.position)


func _pin_tile(mouse_pos: Vector2) -> void:
	if world_data == null or _map_display.texture == null:
		_tile_pinned = false
		return

	var local: Vector2 = mouse_pos - _map_display.position
	var tile_scale: float = _map_display.size.x / float(world_data.width)
	if tile_scale <= 0.0:
		_tile_pinned = false
		return

	var tx: int = int(local.x / tile_scale)
	var ty: int = int(local.y / tile_scale)
	if tx < 0 or tx >= world_data.width or ty < 0 or ty >= world_data.height:
		_tile_pinned = false
		return

	var biome_name: String = TileRegistry.get_biome_name(world_data.biome[ty][tx])
	var alt: float         = world_data.altitude[ty][tx]
	var temp: float        = world_data.temperature[ty][tx]
	var precip: float      = world_data.precipitation[ty][tx]
	var drain: float       = world_data.drainage[ty][tx]
	var prosp: float       = world_data.prosperity[ty][tx]
	var flow_v: float      = world_data.flow[ty][tx]
	var is_riv: bool       = world_data.is_river[ty][tx]
	var is_lk: bool        = world_data.is_lake[ty][tx]
	var water_tag: String  = ""
	if is_lk:    water_tag = "  [LAKE]"
	elif is_riv: water_tag = "  [RIVER]"

	_hover_label.text = (
		"[%d, %d] ● PINNED\n%s%s\n\nAlt:   %.2f\nTemp:  %.2f\nRain:  %.2f\nDrain: %.2f\nProsp: %.2f\nFlow:  %.2f\n\n[click to unpin]"
		% [tx, ty, biome_name, water_tag, alt, temp, precip, drain, prosp, flow_v]
	)
	if _play_hover_lbl != null:
		_play_hover_lbl.text = _hover_label.text


func _update_hover(mouse_pos: Vector2) -> void:
	if _tile_pinned or world_data == null or _map_display.texture == null:
		return

	# Convert screen position to texture pixel.
	var local: Vector2 = mouse_pos - _map_display.position
	var tile_scale: float = _map_display.size.x / float(world_data.width)
	if tile_scale <= 0.0:
		return

	var tx: int = int(local.x / tile_scale)
	var ty: int = int(local.y / tile_scale)

	if tx < 0 or tx >= world_data.width or ty < 0 or ty >= world_data.height:
		_hover_label.text = "—"
		return

	var biome_name: String = TileRegistry.get_biome_name(world_data.biome[ty][tx])
	var alt: float         = world_data.altitude[ty][tx]
	var temp: float        = world_data.temperature[ty][tx]
	var precip: float      = world_data.precipitation[ty][tx]
	var drain: float       = world_data.drainage[ty][tx]
	var prosp: float       = world_data.prosperity[ty][tx]

	var flow_v: float = world_data.flow[ty][tx]
	var is_riv: bool  = world_data.is_river[ty][tx]
	var is_lk:  bool  = world_data.is_lake[ty][tx]
	var water_tag: String = ""
	if is_lk:  water_tag = "  [LAKE]"
	elif is_riv: water_tag = "  [RIVER]"

	_hover_label.text = (
		"[%d, %d]\n%s%s\n\nAlt:   %.2f\nTemp:  %.2f\nRain:  %.2f\nDrain: %.2f\nProsp: %.2f\nFlow:  %.2f"
		% [tx, ty, biome_name, water_tag, alt, temp, precip, drain, prosp, flow_v]
	)
	if _play_hover_lbl != null:
		_play_hover_lbl.text = _hover_label.text


# ==================================================================
# Event callbacks
# ==================================================================

func _on_mode_button(mode: MapRenderer.ViewMode, _btn: Button) -> void:
	_set_mode(mode)


func _on_viewport_resize() -> void:
	_fit_map_to_anchor()


# ==================================================================
# Helpers
# ==================================================================

static func _make_label(text: String) -> Label:
	var lbl := Label.new()
	lbl.text = text
	lbl.add_theme_font_size_override("font_size", 12)
	return lbl


static func _make_section_label(text: String) -> Label:
	var lbl := Label.new()
	lbl.text = text
	lbl.add_theme_font_size_override("font_size", 11)
	lbl.modulate = Color(0.75, 0.85, 1.0)
	return lbl


## Returns [container, HSlider].  The container can be added directly to a VBox.
static func _make_slider(label: String, min_v: float, max_v: float, val: float, step: float) -> Array:
	var container := VBoxContainer.new()
	container.size_flags_horizontal = Control.SIZE_EXPAND_FILL

	var top := HBoxContainer.new()
	var lbl := Label.new()
	lbl.text = label
	lbl.add_theme_font_size_override("font_size", 11)
	lbl.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	top.add_child(lbl)

	var val_lbl := Label.new()
	val_lbl.add_theme_font_size_override("font_size", 11)
	val_lbl.horizontal_alignment = HORIZONTAL_ALIGNMENT_RIGHT
	val_lbl.custom_minimum_size = Vector2(44, 0)
	val_lbl.text = _fmt_slider(val, step)
	top.add_child(val_lbl)
	container.add_child(top)

	var slider := HSlider.new()
	slider.min_value = min_v
	slider.max_value = max_v
	slider.value     = val
	slider.step      = step
	slider.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	slider.value_changed.connect(func(v: float): val_lbl.text = _fmt_slider(v, step))
	container.add_child(slider)

	return [container, slider]


static func _fmt_slider(v: float, step: float) -> String:
	if step >= 1.0:
		return "%d" % int(v)
	elif step >= 0.01:
		return "%.2f" % v
	else:
		return "%.4f" % v


## Push all slider handles to match the given preset and update _params.
func _apply_preset(p: WorldGenParams) -> void:
	_sl_sea_ratio.value    = p.sea_ratio
	_sl_falloff.value      = p.island_falloff
	_sl_frequency.value      = p.noise_frequency
	_sl_octaves.value        = p.noise_octaves
	_sl_noise_factor.value   = p.noise_factor
	_sl_crust_factor.value   = p.crust_factor
	_sl_tectonic_factor.value = p.tectonic_factor
	_sl_crust_freq.value     = p.crust_frequency
	_sl_temp_bias.value      = p.temp_bias
	_sl_precip_bias.value  = p.precip_bias
	_sl_river_thresh.value = p.river_threshold
	_sl_lake_fill.value    = p.lake_fill_depth
	_sl_provinces.value    = p.num_provinces
	_sl_density.value      = p.tiles_per_settlement


static func _make_spinbox(min_v: float, max_v: float, val: float, step: float) -> SpinBox:
	var sb := SpinBox.new()
	sb.min_value = min_v
	sb.max_value = max_v
	sb.value     = val
	sb.step      = step
	sb.allow_greater = false
	return sb


# ==================================================================
# Drill-down navigation
# ==================================================================

func _mouse_to_tile(mouse_pos: Vector2) -> Vector2i:
	if world_data == null or _map_display.texture == null:
		return Vector2i(-1, -1)
	var local: Vector2 = mouse_pos - _map_display.position
	var tile_scale: float = _map_display.size.x / float(world_data.width)
	if tile_scale <= 0.0:
		return Vector2i(-1, -1)
	var tx: int = int(local.x / tile_scale)
	var ty: int = int(local.y / tile_scale)
	if tx < 0 or tx >= world_data.width or ty < 0 or ty >= world_data.height:
		return Vector2i(-1, -1)
	return Vector2i(tx, ty)


func _drill_to_region_at(mouse_pos: Vector2) -> void:
	var tile := _mouse_to_tile(mouse_pos)
	if tile == Vector2i(-1, -1):
		return
	if world_data.altitude[tile.y][tile.x] <= world_data.sea_level:
		return  # don't drill into ocean
	_drill_to_region(tile.x, tile.y)


func _drill_to_region(wx: int, wy: int) -> void:
	if _region_view == null:
		_region_view = RegionMapView.new()
		_region_view.drill_up.connect(_drill_up_from_region)
		_region_view.drill_to_local.connect(_drill_to_local)
		_canvas.add_child(_region_view)
	_world_ui.hide()
	_region_view.load_tile(wx, wy)
	_region_view.show()


func _drill_to_local(wx: int, wy: int) -> void:
	if _local_view == null:
		_local_view = LocalMapView.new()
		_local_view.drill_up.connect(_drill_up_from_local)
		_canvas.add_child(_local_view)
	if _region_view != null:
		_region_view.hide()
	_local_view.load_tile(wx, wy)
	_local_view.show()


func _drill_up_from_local() -> void:
	if _local_view != null:
		_local_view.hide()
	if _region_view != null:
		_region_view.show()


func _drill_up_from_region() -> void:
	if _region_view != null:
		_region_view.hide()
	_world_ui.show()
