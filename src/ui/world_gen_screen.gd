## WorldGenScreen — player-facing world generation UI.
##
## Exposes all WorldGenParams settings, generates a world on demand,
## and renders the result as a colour-coded pixel map.
##
## Inspired by the keyboard-driven config screens in:
##   moneybagboyz/Falling-Leaves-Ravensguard (UIPanels.gd / Main.gd)
##
## Controls:
##   ◀ / ▶ buttons — adjust each setting
##   Randomise Seed — new random seed
##   ▶ GENERATE WORLD — run generation and show map
##   F9  — toggle the debug CanvasLayer RegionMapView (if available)
##   F10 — toggle the EconomyView debug overlay
class_name WorldGenScreen
extends Control

# ── Size presets ──────────────────────────────────────────────────────────────
## [width, height, display_label]
const SIZE_PRESETS: Array = [
	[128,  128,  "128 × 128   (Quick test)"],
	[256,  256,  "256 × 256   (Small)"],
	[512,  512,  "512 × 512   (Standard)"],
	[1024, 1024, "1024 × 1024  (Epic — slow)"],
]
const DEFAULT_SIZE_IDX: int = 2  # 512 × 512

# ── World archetypes (applied via the preset selector) ───────────────────────
const WORLD_PRESETS: Array = [
	{"name":"Standard",       "layout":"continents",  "sea_ratio":0.44, "island_mode":true,  "island_falloff":1.40, "num_plates":12, "tectonic_blend":0.65, "temp_bias":0.0,  "precip_bias":0.0},
	{"name":"Pangea",         "layout":"pangea",      "sea_ratio":0.60, "island_mode":true,  "island_falloff":1.20, "num_plates":8,  "tectonic_blend":0.65, "temp_bias":0.0,  "precip_bias":0.0},
	{"name":"Shattered Isles","layout":"archipelago", "sea_ratio":0.72, "island_mode":true,  "island_falloff":2.00, "num_plates":20, "tectonic_blend":0.35, "temp_bias":0.1,  "precip_bias":0.15},
	{"name":"Frozen North",   "layout":"continents",  "sea_ratio":0.50, "island_mode":true,  "island_falloff":1.60, "num_plates":10, "tectonic_blend":0.55, "temp_bias":-0.4, "precip_bias":-0.1},
	{"name":"Desert World",   "layout":"pangea",      "sea_ratio":0.30, "island_mode":false, "island_falloff":1.40, "num_plates":6,  "tectonic_blend":0.45, "temp_bias":0.5,  "precip_bias":-0.4},
]

# ── Terrain colour palette (matches RegionMapView) ────────────────────────────
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

# Tier dot colours (hamlet → metropolis)
const TIER_COLORS: Array = [
	Color(0.85, 0.85, 0.85),  # 0 hamlet
	Color(1.00, 1.00, 0.50),  # 1 village
	Color(1.00, 0.80, 0.20),  # 2 town
	Color(1.00, 0.50, 0.10),  # 3 city
	Color(1.00, 0.20, 0.20),  # 4 metropolis
]

# ── Per-instance state ────────────────────────────────────────────────────────
var _params:      WorldGenParams = null
var _size_idx:    int            = DEFAULT_SIZE_IDX
var _cur_seed:    int            = 0
var _generating:  bool           = false
var _last_ws:     WorldState     = null
var _thread:      Thread         = null
var _spin_idx:    int            = 0
const _SPIN: PackedStringArray   = ["|", "/", "\u2014", "\\"]

# ── UI node references ────────────────────────────────────────────────────────
var _seed_field:   LineEdit    = null
var _status_label: Label       = null
var _gen_button:   Button      = null
var _enter_btn:    Button      = null
var _map_texture:  TextureRect = null
var _stats_label:  Label       = null
var _size_val_lbl: Label       = null
# map zoom + pan
var _map_scroll:   ScrollContainer = null
var _zoom_level:   float           = 1.0
const ZOOM_MIN:    float           = 0.5
const ZOOM_MAX:    float           = 8.0
const ZOOM_STEP:   float           = 0.25
# mouse-drag pan
var _dragging:     bool    = false
var _drag_origin:  Vector2 = Vector2.ZERO
var _drag_scroll:  Vector2 = Vector2.ZERO
# collapsible advanced noise panel
var _adv_box:        VBoxContainer = null
# world preset selector
var _preset_idx:     int           = 0
var _preset_val_lbl: Label         = null
# quick-setting live refs (updated when a preset is applied)
var _quick_lbls:   Dictionary = {}   # key -> Label (value display)
var _quick_checks: Dictionary = {}   # key -> CheckButton
var _quick_curs:   Dictionary = {}   # key -> Array (the cur[] the lambda tracks)
# debug overlays
var _economy_view: EconomyView = null

# ── Lifecycle ─────────────────────────────────────────────────────────────────
func _ready() -> void:
	set_process(false)  # enabled only while thread is running
	_params           = WorldGenParams.default_params()
	# Instantiate the EconomyView debug overlay (starts hidden; F10 to toggle).
	_economy_view = EconomyView.new()
	get_tree().root.add_child.call_deferred(_economy_view)
	_size_idx         = DEFAULT_SIZE_IDX
	_params.grid_width  = SIZE_PRESETS[_size_idx][0]
	_params.grid_height = SIZE_PRESETS[_size_idx][1]
	_cur_seed         = randi()
	_build_ui()


# ── Build the entire UI in code ───────────────────────────────────────────────
func _build_ui() -> void:
	set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
	_quick_lbls   = {}
	_quick_checks = {}
	_quick_curs   = {}

	# Root HSplitContainer
	var split := HSplitContainer.new()
	split.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
	split.split_offset = 370
	add_child(split)

	# ── LEFT: Settings panel ─────────────────────────────────────────────────
	var left := VBoxContainer.new()
	left.custom_minimum_size = Vector2(350, 0)
	split.add_child(left)

	var title := Label.new()
	title.text = "WORLD GENERATION"
	title.add_theme_font_size_override("font_size", 20)
	left.add_child(title)

	var scroll := ScrollContainer.new()
	scroll.size_flags_vertical = Control.SIZE_EXPAND_FILL
	left.add_child(scroll)

	var list := VBoxContainer.new()
	list.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	scroll.add_child(list)

	# ── World preset selector ────────────────────────────────────────────────
	_section(list, "── World Preset ──")
	_add_preset_selector(list)

	# ── Map (size + seed) ────────────────────────────────────────────────────
	_section(list, "── Map ──")
	_add_size_row(list)
	_add_seed_row(list)

	# ── World Shape — controls players care about ────────────────────────────
	_section(list, "── World Shape ──")
	_add_cycle(list, "Layout", ["pangea", "continents", "archipelago"],
			_params.layout, func(v: String): _params.layout = v, "layout")
	_add_float(list, "Sea Coverage", _params.sea_ratio, 0.10, 0.80, 0.05,
			func(v: float): _params.sea_ratio = v, "sea_ratio")
	_add_toggle(list, "Island Mode", _params.island_mode,
			func(v: bool): _params.island_mode = v, "island_mode")
	_add_int(list, "Tectonic Plates", _params.num_plates, 4, 64, 1,
			func(v: int): _params.num_plates = v, "num_plates")

	# ── Climate (meaningful to players) ──────────────────────────────────────
	_section(list, "── Climate ──")
	_add_float(list, "Temperature",   _params.temp_bias,   -1.0, 1.0, 0.1,
			func(v: float): _params.temp_bias = v, "temp_bias")
	_add_float(list, "Precipitation", _params.precip_bias, -1.0, 1.0, 0.1,
			func(v: float): _params.precip_bias = v, "precip_bias")

	# ── Settlements & history ─────────────────────────────────────────────────
	_section(list, "── Structure ──")
	_add_int(list, "Provinces",        _params.num_provinces,        0,  64, 1,
			func(v: int): _params.num_provinces = v)  # 0 = auto-scale
	_add_int(list, "Tiles/Settlement", _params.tiles_per_settlement, 5,  50, 5,
			func(v: int): _params.tiles_per_settlement = v)
	_add_int(list, "Min Settle Sep",   _params.min_settlement_sep,   2,  20, 1,
			func(v: int): _params.min_settlement_sep = v)
	_add_toggle(list, "History Sim",   _params.run_history_sim,
			func(v: bool): _params.run_history_sim = v)

	# ── Advanced (collapsed by default) ──────────────────────────────────────
	var adv_btn := Button.new()
	adv_btn.text = "⚙  Advanced  ▶"
	adv_btn.alignment = HORIZONTAL_ALIGNMENT_LEFT
	list.add_child(adv_btn)

	_adv_box = VBoxContainer.new()
	_adv_box.visible = false
	list.add_child(_adv_box)

	_section(_adv_box, "── Tectonic Detail ──")
	_add_float(_adv_box, "Tectonic Blend", _params.tectonic_blend, 0.0, 1.0, 0.05,
			func(v: float): _params.tectonic_blend = v)

	_section(_adv_box, "── Terrain Noise ──")
	_add_float(_adv_box, "Island Falloff", _params.island_falloff, 0.50, 3.00, 0.10,
			func(v: float): _params.island_falloff = v)
	_add_float(_adv_box, "Base Freq",      _params.base_freq,      0.001, 0.020, 0.001,
			func(v: float): _params.base_freq = v)
	_add_float(_adv_box, "Base Weight",    _params.base_weight,    0.10,  0.90,  0.05,
			func(v: float): _params.base_weight = v)
	_add_float(_adv_box, "Detail Freq",    _params.detail_freq,    0.005, 0.050, 0.005,
			func(v: float): _params.detail_freq = v)
	_add_float(_adv_box, "Detail Weight",  _params.detail_weight,  0.05,  0.50,  0.05,
			func(v: float): _params.detail_weight = v)
	_add_float(_adv_box, "Ridge Freq",     _params.ridge_freq,     0.002, 0.020, 0.002,
			func(v: float): _params.ridge_freq = v)
	_add_float(_adv_box, "Ridge Weight",   _params.ridge_weight,   0.05,  0.50,  0.05,
			func(v: float): _params.ridge_weight = v)
	_add_float(_adv_box, "Climate Freq",   _params.climate_freq,   0.002, 0.020, 0.002,
			func(v: float): _params.climate_freq = v)

	adv_btn.pressed.connect(func(): _toggle_advanced(adv_btn))

	# ── Generate button — sticky outside the scroll area ─────────────────────
	var sep := HSeparator.new()
	left.add_child(sep)

	_gen_button = Button.new()
	_gen_button.text = "▶  GENERATE WORLD"
	_gen_button.add_theme_font_size_override("font_size", 16)
	_gen_button.pressed.connect(_on_generate_pressed)
	left.add_child(_gen_button)

	# Fixed-height clip box prevents status text from pushing the layout around.
	var status_clip := Control.new()
	status_clip.custom_minimum_size = Vector2(0, 52)
	status_clip.size_flags_vertical = Control.SIZE_SHRINK_END
	status_clip.clip_contents       = true
	left.add_child(status_clip)

	_status_label = Label.new()
	_status_label.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
	_status_label.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	_status_label.text = "Ready. Press ▶ GENERATE WORLD to begin."
	status_clip.add_child(_status_label)

	# ► ENTER WORLD button — hidden until a world has been generated.
	var enter_sep := HSeparator.new()
	left.add_child(enter_sep)
	_enter_btn = Button.new()
	_enter_btn.text = "►►  ENTER WORLD"
	_enter_btn.add_theme_font_size_override("font_size", 16)
	_enter_btn.modulate = Color(0.4, 1.0, 0.6)
	_enter_btn.disabled = true
	_enter_btn.pressed.connect(_on_enter_world_pressed)
	left.add_child(_enter_btn)

	# ── RIGHT: Map preview panel ──────────────────────────────────────────────
	var right := VBoxContainer.new()
	right.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	split.add_child(right)

	var map_title := Label.new()
	map_title.text = "WORLD PREVIEW"
	map_title.add_theme_font_size_override("font_size", 20)
	right.add_child(map_title)

	# Zoom hint label
	var zoom_hint := Label.new()
	zoom_hint.text = "Scroll=zoom  ·  Drag=pan  ·  G=generate  R=random seed  +/-=zoom  0/F=reset  arrows=pan"
	zoom_hint.add_theme_font_size_override("font_size", 11)
	zoom_hint.modulate = Color(0.7, 0.7, 0.7)
	right.add_child(zoom_hint)

	# Scroll container wraps the texture for zoom + pan
	_map_scroll = ScrollContainer.new()
	_map_scroll.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	_map_scroll.size_flags_vertical   = Control.SIZE_EXPAND_FILL
	_map_scroll.gui_input.connect(_on_map_input)
	right.add_child(_map_scroll)

	_map_texture = TextureRect.new()
	_map_texture.stretch_mode    = TextureRect.STRETCH_SCALE
	_map_texture.texture_filter  = CanvasItem.TEXTURE_FILTER_NEAREST
	_map_scroll.add_child(_map_texture)

	var legend := _build_legend()
	right.add_child(legend)

	_stats_label = Label.new()
	_stats_label.text = "No world generated yet."
	_stats_label.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	right.add_child(_stats_label)


# ── Section header ────────────────────────────────────────────────────────────
func _section(parent: Control, text: String) -> void:
	var lbl := Label.new()
	lbl.text = text
	lbl.modulate = Color(0.6, 0.85, 1.0)
	parent.add_child(lbl)


# ── World-preset selector row ─────────────────────────────────────────────────
func _add_preset_selector(parent: Control) -> void:
	var row := HBoxContainer.new()
	parent.add_child(row)

	var minus := Button.new()
	minus.text = "◀"
	minus.custom_minimum_size = Vector2(28, 0)
	row.add_child(minus)

	_preset_val_lbl = Label.new()
	_preset_val_lbl.text = WORLD_PRESETS[_preset_idx].get("name", "")
	_preset_val_lbl.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	_preset_val_lbl.horizontal_alignment  = HORIZONTAL_ALIGNMENT_CENTER
	row.add_child(_preset_val_lbl)

	var plus := Button.new()
	plus.text = "▶"
	plus.custom_minimum_size = Vector2(28, 0)
	row.add_child(plus)

	var apply_btn := Button.new()
	apply_btn.text = "Apply"
	apply_btn.custom_minimum_size = Vector2(56, 0)
	row.add_child(apply_btn)

	minus.pressed.connect(func():
		_preset_idx = (_preset_idx - 1 + WORLD_PRESETS.size()) % WORLD_PRESETS.size()
		_preset_val_lbl.text = WORLD_PRESETS[_preset_idx].get("name", "")
	)
	plus.pressed.connect(func():
		_preset_idx = (_preset_idx + 1) % WORLD_PRESETS.size()
		_preset_val_lbl.text = WORLD_PRESETS[_preset_idx].get("name", "")
	)
	apply_btn.pressed.connect(func(): _apply_preset(_preset_idx))


# ── Apply a named world preset and refresh all tracked quick-setting labels ───
func _apply_preset(idx: int) -> void:
	var p: Dictionary = WORLD_PRESETS[idx]
	var layout_opts: Array = ["pangea", "continents", "archipelago"]

	_params.layout         = p.get("layout",         _params.layout)
	_params.sea_ratio      = p.get("sea_ratio",      _params.sea_ratio)
	_params.island_mode    = p.get("island_mode",    _params.island_mode)
	_params.num_plates     = p.get("num_plates",     _params.num_plates)
	_params.tectonic_blend = p.get("tectonic_blend", _params.tectonic_blend)
	_params.island_falloff = p.get("island_falloff", _params.island_falloff)
	_params.temp_bias      = p.get("temp_bias",      _params.temp_bias)
	_params.precip_bias    = p.get("precip_bias",    _params.precip_bias)

	# Refresh displayed labels so the UI matches the applied values.
	if _quick_lbls.has("layout"):
		(_quick_lbls["layout"] as Label).text = _params.layout
	if _quick_curs.has("layout"):
		(_quick_curs["layout"] as Array)[0] = layout_opts.find(_params.layout)

	if _quick_lbls.has("sea_ratio"):
		(_quick_lbls["sea_ratio"] as Label).text = "%.3f" % _params.sea_ratio
	if _quick_curs.has("sea_ratio"):
		(_quick_curs["sea_ratio"] as Array)[0] = _params.sea_ratio

	if _quick_checks.has("island_mode"):
		(_quick_checks["island_mode"] as CheckButton).button_pressed = _params.island_mode

	if _quick_lbls.has("num_plates"):
		(_quick_lbls["num_plates"] as Label).text = str(_params.num_plates)
	if _quick_curs.has("num_plates"):
		(_quick_curs["num_plates"] as Array)[0] = _params.num_plates

	if _quick_lbls.has("temp_bias"):
		(_quick_lbls["temp_bias"] as Label).text = "%.3f" % _params.temp_bias
	if _quick_curs.has("temp_bias"):
		(_quick_curs["temp_bias"] as Array)[0] = _params.temp_bias

	if _quick_lbls.has("precip_bias"):
		(_quick_lbls["precip_bias"] as Label).text = "%.3f" % _params.precip_bias
	if _quick_curs.has("precip_bias"):
		(_quick_curs["precip_bias"] as Array)[0] = _params.precip_bias


# ── Toggle the Advanced noise panel open/closed ───────────────────────────────
func _toggle_advanced(btn: Button) -> void:
	_adv_box.visible = not _adv_box.visible
	btn.text = "⚙  Advanced  " + ("▼" if _adv_box.visible else "▶")


# ── Map-size cycle row ────────────────────────────────────────────────────────
func _add_size_row(parent: Control) -> void:
	var row := HBoxContainer.new()
	parent.add_child(row)

	var lbl := Label.new()
	lbl.text = "Size:"
	lbl.custom_minimum_size = Vector2(130, 0)
	row.add_child(lbl)

	var minus := Button.new()
	minus.text = "◀"
	minus.custom_minimum_size = Vector2(28, 0)
	row.add_child(minus)

	_size_val_lbl = Label.new()
	_size_val_lbl.text = SIZE_PRESETS[_size_idx][2]
	_size_val_lbl.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	_size_val_lbl.horizontal_alignment  = HORIZONTAL_ALIGNMENT_CENTER
	row.add_child(_size_val_lbl)

	var plus := Button.new()
	plus.text = "▶"
	plus.custom_minimum_size = Vector2(28, 0)
	row.add_child(plus)

	minus.pressed.connect(func(): _cycle_size(-1))
	plus.pressed.connect(func():  _cycle_size(1))


# ── Seed row with randomise button ────────────────────────────────────────────
func _add_seed_row(parent: Control) -> void:
	var row := HBoxContainer.new()
	parent.add_child(row)

	var lbl := Label.new()
	lbl.text = "Seed:"
	lbl.custom_minimum_size = Vector2(64, 0)
	row.add_child(lbl)

	_seed_field = LineEdit.new()
	_seed_field.text = str(_cur_seed)
	_seed_field.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	row.add_child(_seed_field)

	var rand_btn := Button.new()
	rand_btn.text = "⟳ Random"
	rand_btn.pressed.connect(_randomise_seed)
	row.add_child(rand_btn)


# ── Float ◀/▶ row ─────────────────────────────────────────────────────────────
func _add_float(parent: Control, label: String, initial: float,
		lo: float, hi: float, step: float, setter: Callable,
		key: String = "") -> void:
	var row := HBoxContainer.new()
	parent.add_child(row)

	var lbl := Label.new()
	lbl.text = label + ":"
	lbl.custom_minimum_size = Vector2(130, 0)
	row.add_child(lbl)

	var minus := Button.new()
	minus.text = "◀"
	minus.custom_minimum_size = Vector2(28, 0)
	row.add_child(minus)

	var val_lbl := Label.new()
	val_lbl.text = "%.3f" % initial
	val_lbl.custom_minimum_size    = Vector2(62, 0)
	val_lbl.horizontal_alignment   = HORIZONTAL_ALIGNMENT_CENTER
	row.add_child(val_lbl)

	var plus := Button.new()
	plus.text = "▶"
	plus.custom_minimum_size = Vector2(28, 0)
	row.add_child(plus)

	var cur: Array[float] = [initial]
	if key != "":
		_quick_lbls[key] = val_lbl
		_quick_curs[key] = cur

	minus.pressed.connect(func():
		cur[0] = clampf(snappedf(cur[0] - step, step), lo, hi)
		val_lbl.text = "%.3f" % cur[0]
		setter.call(cur[0])
	)
	plus.pressed.connect(func():
		cur[0] = clampf(snappedf(cur[0] + step, step), lo, hi)
		val_lbl.text = "%.3f" % cur[0]
		setter.call(cur[0])
	)


# ── Int ◀/▶ row ───────────────────────────────────────────────────────────────
func _add_int(parent: Control, label: String, initial: int,
		lo: int, hi: int, step: int, setter: Callable,
		key: String = "") -> void:
	var row := HBoxContainer.new()
	parent.add_child(row)

	var lbl := Label.new()
	lbl.text = label + ":"
	lbl.custom_minimum_size = Vector2(130, 0)
	row.add_child(lbl)

	var minus := Button.new()
	minus.text = "◀"
	minus.custom_minimum_size = Vector2(28, 0)
	row.add_child(minus)

	var val_lbl := Label.new()
	val_lbl.text = str(initial)
	val_lbl.custom_minimum_size  = Vector2(62, 0)
	val_lbl.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	row.add_child(val_lbl)

	var plus := Button.new()
	plus.text = "▶"
	plus.custom_minimum_size = Vector2(28, 0)
	row.add_child(plus)

	var cur: Array[int] = [initial]
	if key != "":
		_quick_lbls[key] = val_lbl
		_quick_curs[key] = cur

	minus.pressed.connect(func():
		cur[0] = clampi(cur[0] - step, lo, hi)
		val_lbl.text = str(cur[0])
		setter.call(cur[0])
	)
	plus.pressed.connect(func():
		cur[0] = clampi(cur[0] + step, lo, hi)
		val_lbl.text = str(cur[0])
		setter.call(cur[0])
	)


# ── Bool toggle row ───────────────────────────────────────────────────────────
func _add_toggle(parent: Control, label: String, initial: bool,
		setter: Callable, key: String = "") -> void:
	var row := HBoxContainer.new()
	parent.add_child(row)

	var lbl := Label.new()
	lbl.text = label + ":"
	lbl.custom_minimum_size = Vector2(130, 0)
	row.add_child(lbl)

	var check := CheckButton.new()
	check.button_pressed = initial
	check.toggled.connect(func(v: bool): setter.call(v))
	row.add_child(check)
	if key != "":
		_quick_checks[key] = check


# ── String cycle row (◀/▶ through a fixed list of options) ───────────────────
func _add_cycle(parent: Control, label: String, options: Array,
		initial: String, setter: Callable, key: String = "") -> void:
	var row := HBoxContainer.new()
	parent.add_child(row)

	var lbl := Label.new()
	lbl.text = label + ":"
	lbl.custom_minimum_size = Vector2(130, 0)
	row.add_child(lbl)

	var minus := Button.new()
	minus.text = "◀"
	minus.custom_minimum_size = Vector2(28, 0)
	row.add_child(minus)

	var val_lbl := Label.new()
	val_lbl.text = initial
	val_lbl.custom_minimum_size    = Vector2(90, 0)
	val_lbl.horizontal_alignment   = HORIZONTAL_ALIGNMENT_CENTER
	row.add_child(val_lbl)

	var plus := Button.new()
	plus.text = "▶"
	plus.custom_minimum_size = Vector2(28, 0)
	row.add_child(plus)

	var cur: Array[int] = [options.find(initial)]
	if key != "":
		_quick_lbls[key] = val_lbl
		_quick_curs[key] = cur

	minus.pressed.connect(func():
		cur[0] = (cur[0] - 1 + options.size()) % options.size()
		val_lbl.text = options[cur[0]]
		setter.call(options[cur[0]])
	)
	plus.pressed.connect(func():
		cur[0] = (cur[0] + 1) % options.size()
		val_lbl.text = options[cur[0]]
		setter.call(options[cur[0]])
	)


# ── Terrain colour legend ─────────────────────────────────────────────────────
func _build_legend() -> Control:
	var flow := HFlowContainer.new()
	flow.custom_minimum_size = Vector2(0, 28)
	for terrain: String in TERRAIN_COLORS:
		var chip := ColorRect.new()
		chip.color = TERRAIN_COLORS[terrain]
		chip.custom_minimum_size = Vector2(12, 12)
		flow.add_child(chip)
		var lbl := Label.new()
		lbl.text = terrain + "  "
		lbl.add_theme_font_size_override("font_size", 11)
		flow.add_child(lbl)
	return flow


# ── Map input: scroll-wheel zoom + left-drag pan ──────────────────────────────
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
					_dragging    = true
					_drag_origin = mbe.global_position
					_drag_scroll = Vector2(
						_map_scroll.scroll_horizontal,
						_map_scroll.scroll_vertical)
					get_viewport().set_input_as_handled()
				else:
					_dragging = false

	elif event is InputEventMouseMotion:
		if _dragging:
			var delta: Vector2 = (event as InputEventMouseMotion).global_position - _drag_origin
			_map_scroll.scroll_horizontal = int(_drag_scroll.x - delta.x)
			_map_scroll.scroll_vertical   = int(_drag_scroll.y - delta.y)
			get_viewport().set_input_as_handled()


func _apply_zoom() -> void:
	if _map_texture == null or _map_texture.texture == null:
		return
	var tex_w: int = _map_texture.texture.get_width()
	var tex_h: int = _map_texture.texture.get_height()
	var new_w: float = tex_w * _zoom_level
	var new_h: float = tex_h * _zoom_level
	_map_texture.custom_minimum_size = Vector2(new_w, new_h)
	_map_texture.size = Vector2(new_w, new_h)


func _zoom_reset() -> void:
	_zoom_level = 1.0
	_apply_zoom()
	if _map_scroll:
		_map_scroll.scroll_horizontal = 0
		_map_scroll.scroll_vertical   = 0


# ── Keyboard shortcuts ─────────────────────────────────────────────────────────
## Keyboard shortcuts (fires when no control has focus, or use as fallback).
## G — Generate      R — Randomise seed
## + / = — Zoom in   - — Zoom out    0 / F — Fit / reset zoom
## Arrow keys — pan map
func _unhandled_key_input(event: InputEvent) -> void:
	if not (event is InputEventKey):
		return
	var key := event as InputEventKey
	if not key.pressed:
		return
	match key.keycode:
		KEY_G:
			_on_generate_pressed()
		KEY_R:
			_randomise_seed()
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
			# Economy View overlay (pass-through to the node's own handler)
			if _economy_view != null:
				_economy_view._toggle()


# ── Interactions ──────────────────────────────────────────────────────────────
func _on_enter_world_pressed() -> void:
	# Clean up our EconomyView overlay before leaving — WorldView creates its own.
	if _economy_view != null:
		_economy_view.queue_free()
		_economy_view = null
	# If the player character hasn't been created yet, go to character creation.
	# If a world was loaded from a save (player_character_id already set),
	# skip character creation and jump straight to WorldView.
	var boot: Node = get_node_or_null("/root/Bootstrap")
	var ws: WorldState = boot.get("world_state") if boot else null
	if ws != null and ws.player_character_id != "":
		SceneManager.replace_scene("res://src/ui/world_view.tscn")
	else:
		SceneManager.push_scene("res://src/ui/character_creation/character_creation_screen.tscn")


func _cycle_size(delta: int) -> void:
	_size_idx = (_size_idx + delta + SIZE_PRESETS.size()) % SIZE_PRESETS.size()
	_params.grid_width  = SIZE_PRESETS[_size_idx][0]
	_params.grid_height = SIZE_PRESETS[_size_idx][1]
	if _size_val_lbl:
		_size_val_lbl.text = SIZE_PRESETS[_size_idx][2]


func _randomise_seed() -> void:
	_cur_seed = randi()
	if _seed_field:
		_seed_field.text = str(_cur_seed)


func _on_generate_pressed() -> void:
	if _generating:
		return

	# Pause the sim clock for the duration of generation so no ticks
	# accumulate on the old (empty) world while the thread runs.
	SimulationClock.pause()

	# Sync seed from text field.
	if _seed_field and _seed_field.text.is_valid_int():
		_cur_seed = int(_seed_field.text)
	else:
		_randomise_seed()

	_generating = true
	_gen_button.disabled = true
	_stats_label.text = ""
	_spin_idx = 0
	_status_label.text = "Generating %d × %d  (seed %d)  |" % [
		_params.grid_width, _params.grid_height, _cur_seed
	]

	# Snapshot values so the thread closure captures them by value / stable ref.
	var seed_snap: int = _cur_seed
	var params_snap: WorldGenParams = _params

	_thread = Thread.new()
	_thread.start(func() -> WorldState:
		return RegionGenerator.generate(seed_snap, params_snap, _on_gen_step)
	)
	set_process(true)


# Called from worker thread via call_deferred — safe to update UI.
func _on_gen_step(step_name: String, data: WorldGenData) -> void:
	_status_label.text = step_name
	_render_live(data)


# Live altitude/terrain preview (renders whatever arrays are populated).
func _render_live(data: WorldGenData) -> void:
	var W: int = data.width
	var H: int = data.height
	if W <= 0 or H <= 0:
		return
	var img := Image.create(W, H, false, Image.FORMAT_RGB8)
	# Decide rendering mode: terrain if step 6+ has run, else altitude heatmap.
	var has_terrain: bool = not data.terrain.is_empty() and \
			not data.terrain[0].is_empty() and \
			not (data.terrain[0][0] as String).is_empty()
	var sl: float = data.sea_level if data.sea_level > 0.0 else 0.44
	for y in H:
		for x in W:
			var col: Color
			if has_terrain:
				var t: String = data.terrain[y][x]
				col = TERRAIN_COLORS.get(t, Color(0.5, 0.5, 0.5))
			else:
				var a: float = data.altitude[y][x]
				if a < sl:
					var d: float = a / maxf(sl, 0.001)
					col = Color(0.05 + d * 0.1, 0.15 + d * 0.25, 0.55 + d * 0.35)
				else:
					var t: float = (a - sl) / maxf(1.0 - sl, 0.001)
					if t < 0.35:
						col = Color(0.30 + t * 0.8, 0.60 + t * 0.5, 0.15 + t * 0.3)
					elif t < 0.70:
						col = Color(0.55 + t * 0.4, 0.50 + t * 0.2, 0.20 + t * 0.3)
					else:
						col = Color(0.70 + t * 0.3, 0.70 + t * 0.3, 0.70 + t * 0.3)
			img.set_pixel(x, y, col)
	_map_texture.texture = ImageTexture.create_from_image(img)
	_apply_zoom()


func _process(_delta: float) -> void:
	if _thread == null:
		return
	if _thread.is_alive():
		# Pulse a spinner character appended to whatever the last step label was.
		_spin_idx = (_spin_idx + 1) % _SPIN.size()
		var base: String = _status_label.text
		# Strip any prior spinner char so we don't accumulate them.
		if base.length() > 0 and _SPIN.has(base.substr(base.length() - 1)):
			base = base.substr(0, base.length() - 2)
		_status_label.text = base + "  " + _SPIN[_spin_idx]
	else:
		set_process(false)
		var ws: WorldState = _thread.wait_to_finish() as WorldState
		_thread = null
		_on_generation_done(ws)


func _on_generation_done(ws: WorldState) -> void:
	_last_ws = ws

	# Push the freshly generated world into Bootstrap and reinitialise economy.
	var boot: Node = get_node_or_null("/root/Bootstrap")
	if boot != null:
		boot.world_state = ws
		boot._setup_economy(ws)

	# Reset the tick counter so the new world always starts at tick 0.
	SimulationClock.reset()
	SimulationClock.resume()

	# Enable the Enter World button now that we have a valid world.
	if _enter_btn != null:
		_enter_btn.disabled = false

	# Feed the new world into the EconomyView overlay.
	if _economy_view != null:
		_economy_view.setup(ws)

	var s_count: int = ws.settlements.size()
	var p_count: int = ws.province_names.size()
	var route_count: int = 0
	for edges: Array in ws.routes.values():
		route_count += edges.size()
	@warning_ignore("integer_division")
	var route_edges: int = route_count / 2

	_status_label.text = "Done — %d settlements · %d provinces · %d route edges." % [
		s_count, p_count, route_edges
	]
	_stats_label.text = (
		"Seed: %d  |  Size: %d × %d\n"
		+ "Settlements: %d  |  Provinces: %d  |  Routes: %d edges"
	) % [_cur_seed, _params.grid_width, _params.grid_height,
		 s_count, p_count, route_edges]

	_render_map(ws)

	_gen_button.disabled = false
	_generating = false


# ── Map rendering ─────────────────────────────────────────────────────────────
func _render_map(ws: WorldState) -> void:
	var W: int = _params.grid_width
	var H: int = _params.grid_height

	if W <= 0 or H <= 0 or ws.world_tiles.is_empty():
		_status_label.text += "\n[Warning] No region cells to render."
		return

	var img := Image.create(W, H, false, Image.FORMAT_RGB8)

	# Fill terrain colours (1 px per tile).
	for cell_data: Dictionary in ws.world_tiles.values():
		var x: int = int(cell_data.get("grid_x", -1))
		var y: int = int(cell_data.get("grid_y", -1))
		if x < 0 or y < 0 or x >= W or y >= H:
			continue
		var terrain: String = cell_data.get("terrain_type", "plains")
		var col: Color = TERRAIN_COLORS.get(terrain, Color(0.5, 0.5, 0.5))
		img.set_pixel(x, y, col)

	# Draw route edges using actual Dijkstra path tiles.
	for sid: String in ws.routes.keys():
		for edge: Dictionary in ws.routes[sid]:
			var path = edge.get("path", [])
			for tile in path:
				var tx: int
				var ty: int
				if tile is Vector2i:
					tx = tile.x
					ty = tile.y
				else:
					tx = int(tile[0])
					ty = int(tile[1])
				if tx >= 0 and ty >= 0 and tx < W and ty < H:
					img.set_pixel(tx, ty, Color(0.95, 0.90, 0.60, 1.0))

	# Draw settlement dots sized by tier.
	for sid: String in ws.settlements.keys():
		var sdata: Dictionary = ws.get_settlement_dict(sid)
		var tx: int = int(sdata.get("tile_x", -1))
		var ty: int  = int(sdata.get("tile_y", -1))
		if tx < 0 or ty < 0:
			continue
		var tier: int  = int(sdata.get("tier", 0))
		var radius: int = 1 + tier  # hamlet=1 … metropolis=5 px radius
		var col: Color = TIER_COLORS[clampi(tier, 0, TIER_COLORS.size() - 1)]
		for dy: int in range(-radius, radius + 1):
			for dx: int in range(-radius, radius + 1):
				if dx * dx + dy * dy <= radius * radius:
					var px: int = clampi(tx + dx, 0, W - 1)
					var py: int = clampi(ty + dy, 0, H - 1)
					img.set_pixel(px, py, col)

	_map_texture.texture = ImageTexture.create_from_image(img)
	_zoom_level = 1.0
	_apply_zoom()
