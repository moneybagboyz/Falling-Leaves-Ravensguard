## MainMenuScreen — game entry point shown on every launch.
##
## Waits for Bootstrap.bootstrap_completed before enabling buttons.
## Routes:
##   New Game  → Bootstrap.start_new_game() → WorldGenScreen
##   Continue  → Bootstrap.continue_game()  → WorldView
##   Quit      → get_tree().quit()
class_name MainMenuScreen
extends Control

# ── Colours ────────────────────────────────────────────────────────────────────
const COLOR_BG      := Color(0.06, 0.06, 0.08)
const COLOR_TITLE   := Color(0.90, 0.76, 0.38)
const COLOR_TAGLINE := Color(0.55, 0.50, 0.42)
const COLOR_BTN_FG  := Color(0.90, 0.88, 0.82)
const COLOR_DIM     := Color(0.38, 0.38, 0.42)
const COLOR_VIGNETTE:= Color(0.0, 0.0, 0.0, 0.55)

# ── Node refs ──────────────────────────────────────────────────────────────────
var _continue_btn: Button = null
var _new_game_btn: Button = null
var _quit_btn:     Button = null
var _loading_lbl:  Label  = null
var _version_lbl:  Label  = null


func _ready() -> void:
	_build_ui()

	var boot := get_node_or_null("/root/Bootstrap")
	if boot == null:
		_on_bootstrap_ready()
		return

	if boot.get("data_loaded"):
		_on_bootstrap_ready()
	else:
		boot.bootstrap_completed.connect(_on_bootstrap_ready, CONNECT_ONE_SHOT)


# ── UI construction ────────────────────────────────────────────────────────────
func _build_ui() -> void:
	# Full-screen dark background.
	var bg := ColorRect.new()
	bg.color = COLOR_BG
	bg.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
	add_child(bg)

	# Centred column.
	var centre := VBoxContainer.new()
	centre.set_anchors_preset(Control.PRESET_CENTER)
	centre.custom_minimum_size = Vector2(340, 0)
	centre.offset_left   = -170
	centre.offset_right  =  170
	centre.offset_top    = -220
	centre.offset_bottom =  220
	centre.add_theme_constant_override("separation", 0)
	add_child(centre)

	# Title.
	var title := Label.new()
	title.text = "RAVENSGUARD"
	title.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	title.add_theme_font_size_override("font_size", 46)
	title.add_theme_color_override("font_color", COLOR_TITLE)
	centre.add_child(title)

	# Tagline.
	var tagline := Label.new()
	tagline.text = "a world simulation"
	tagline.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	tagline.add_theme_font_size_override("font_size", 13)
	tagline.add_theme_color_override("font_color", COLOR_TAGLINE)
	centre.add_child(tagline)

	# Spacer.
	var spacer := Control.new()
	spacer.custom_minimum_size = Vector2(0, 52)
	centre.add_child(spacer)

	# Continue button (hidden until save confirmed).
	_continue_btn = _make_button("▶  Continue")
	_continue_btn.visible = false
	_continue_btn.pressed.connect(_on_continue_pressed)
	centre.add_child(_continue_btn)

	_add_gap(centre, 10)

	# New Game button.
	_new_game_btn = _make_button("⊕  New Game")
	_new_game_btn.disabled = true
	_new_game_btn.pressed.connect(_on_new_game_pressed)
	centre.add_child(_new_game_btn)

	_add_gap(centre, 10)

	# Quit button.
	_quit_btn = _make_button("✕  Quit")
	_quit_btn.pressed.connect(_on_quit_pressed)
	centre.add_child(_quit_btn)

	_add_gap(centre, 40)

	# Loading label (shown while Bootstrap is loading data).
	_loading_lbl = Label.new()
	_loading_lbl.text = "Loading…"
	_loading_lbl.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	_loading_lbl.add_theme_font_size_override("font_size", 11)
	_loading_lbl.add_theme_color_override("font_color", COLOR_DIM)
	centre.add_child(_loading_lbl)

	# Version label — bottom-right corner.
	_version_lbl = Label.new()
	_version_lbl.text = "pre-alpha  ·  Phase 3"
	_version_lbl.set_anchors_preset(Control.PRESET_BOTTOM_RIGHT)
	_version_lbl.offset_left   = -180
	_version_lbl.offset_top    =  -28
	_version_lbl.offset_right  =  -12
	_version_lbl.offset_bottom =  -8
	_version_lbl.horizontal_alignment = HORIZONTAL_ALIGNMENT_RIGHT
	_version_lbl.add_theme_font_size_override("font_size", 10)
	_version_lbl.add_theme_color_override("font_color", COLOR_DIM)
	add_child(_version_lbl)


func _make_button(label: String) -> Button:
	var btn := Button.new()
	btn.text = label
	btn.custom_minimum_size = Vector2(260, 44)
	btn.add_theme_font_size_override("font_size", 15)
	btn.add_theme_color_override("font_color", COLOR_BTN_FG)
	return btn


func _add_gap(parent: Control, height: int) -> void:
	var gap := Control.new()
	gap.custom_minimum_size = Vector2(0, height)
	parent.add_child(gap)


# ── Bootstrap callback ─────────────────────────────────────────────────────────
func _on_bootstrap_ready() -> void:
	_loading_lbl.visible = false

	var boot := get_node_or_null("/root/Bootstrap")
	var save_exists: bool = boot != null and boot.has_save()

	_continue_btn.visible  = save_exists
	_new_game_btn.disabled = false


# ── Button handlers ────────────────────────────────────────────────────────────
func _on_continue_pressed() -> void:
	var boot := get_node_or_null("/root/Bootstrap")
	if boot != null:
		boot.continue_game()


func _on_new_game_pressed() -> void:
	var boot := get_node_or_null("/root/Bootstrap")
	if boot != null:
		boot.start_new_game()


func _on_quit_pressed() -> void:
	get_tree().quit()
