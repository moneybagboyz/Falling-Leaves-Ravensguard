## CharacterCreationScreen — new-game character setup.
##
## Pure-code scene: all child nodes are created in _ready(), no .tscn layout.
##
## Flow: WorldGenScreen → [ENTER WORLD] → CharacterCreationScreen → WorldView
##
## The player:
##   1. Picks a background (determines attribute bonuses, starting traits/skills)
##   2. Allocates 5 bonus attribute points across the six core attributes
##   3. Types a character name
##   4. Confirms — creates PersonState, writes into WorldState, enters WorldView
class_name CharacterCreationScreen
extends Control

# ── Constants ─────────────────────────────────────────────────────────────────
const WORLD_VIEW_SCENE := "res://src/ui/world_view.tscn"
const ATTR_NAMES := ["strength", "agility", "endurance",
                     "intelligence", "perception", "charisma"]
const ATTR_LABELS := ["Strength", "Agility", "Endurance",
                      "Intelligence", "Perception", "Charisma"]
const ATTR_BASE := 5
const BONUS_POOL := 5
const ATTR_MIN := 1
const ATTR_MAX := 10

const COL_BG    := Color(0.10, 0.10, 0.12)
const COL_PANEL := Color(0.15, 0.15, 0.18)
const COL_SEL   := Color(0.25, 0.45, 0.65)
const COL_TEXT  := Color(0.90, 0.90, 0.90)
const COL_DIM   := Color(0.55, 0.55, 0.60)
const COL_BONUS := Color(0.50, 0.90, 0.50)
const COL_WARN  := Color(0.95, 0.55, 0.20)

# ── State ─────────────────────────────────────────────────────────────────────
var _content_registry: Node  = null
var _world_state:      WorldState = null
var _entity_registry:  Node  = null

## All backgrounds loaded from ContentRegistry
var _backgrounds: Dictionary = {}
## IDs sorted alphabetically for a stable list order
var _bg_ids: Array = []
## Currently selected background ID
var _selected_bg_id: String = ""

## Bonus-point allocations: attr_name -> int (extra, on top of base+bg bonus)
var _alloc: Dictionary = {}
## Points left to spend
var _points_left: int = BONUS_POOL

# ── Node refs (built in _ready) ────────────────────────────────────────────────
var _name_input:   LineEdit = null
var _bg_buttons:   Dictionary = {}   # bg_id -> Button node
var _detail_label: RichTextLabel = null
var _attr_labels:  Dictionary = {}   # attr_name -> Label (shows current value)
var _attr_btns_plus:  Dictionary = {}
var _attr_btns_minus: Dictionary = {}
var _pool_label:   Label = null
var _confirm_btn:  Button = null
var _err_label:    Label = null


# ── Lifecycle ──────────────────────────────────────────────────────────────────
func _ready() -> void:
	_load_autoloads()
	_load_backgrounds()
	_init_alloc()
	_build_ui()
	if _bg_ids.size() > 0:
		_select_background(_bg_ids[0])


func _load_autoloads() -> void:
	var boot: Node = get_node_or_null("/root/Bootstrap")
	if boot:
		_content_registry = boot.get("content_registry")
		_world_state       = boot.get("world_state")
		_entity_registry   = boot.get("entity_registry")
	if _content_registry == null:
		_content_registry = get_node_or_null("/root/ContentRegistry")
	if _entity_registry == null:
		_entity_registry = get_node_or_null("/root/EntityRegistry")
	if _world_state == null or _content_registry == null:
		push_error("CharacterCreationScreen: missing Bootstrap autoloads")


func _load_backgrounds() -> void:
	if _content_registry == null:
		return
	_backgrounds = _content_registry.get_all("background")
	_bg_ids = _backgrounds.keys()
	_bg_ids.sort()


func _init_alloc() -> void:
	_points_left = BONUS_POOL
	for a in ATTR_NAMES:
		_alloc[a] = 0


# ── UI construction ────────────────────────────────────────────────────────────
func _build_ui() -> void:
	# Root colour background
	var bg := ColorRect.new()
	bg.color = COL_BG
	bg.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
	add_child(bg)

	# Outer VBox: title + content row + bottom bar
	var root_vbox := VBoxContainer.new()
	root_vbox.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
	root_vbox.add_theme_constant_override("separation", 0)
	add_child(root_vbox)

	# Title bar
	var title := Label.new()
	title.text = "Create Your Character"
	title.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	title.add_theme_font_size_override("font_size", 22)
	title.add_theme_color_override("font_color", COL_TEXT)
	title.custom_minimum_size = Vector2(0, 48)
	root_vbox.add_child(title)

	_add_separator(root_vbox)

	# Main content HBox: [bg list | detail | attrs]
	var content_hbox := HBoxContainer.new()
	content_hbox.size_flags_vertical = Control.SIZE_EXPAND_FILL
	content_hbox.add_theme_constant_override("separation", 8)
	root_vbox.add_child(content_hbox)

	_build_bg_list(content_hbox)
	_build_detail_panel(content_hbox)
	_build_attr_panel(content_hbox)

	_add_separator(root_vbox)

	# Bottom bar: name field + confirm
	var bottom_hbox := HBoxContainer.new()
	bottom_hbox.custom_minimum_size = Vector2(0, 52)
	bottom_hbox.add_theme_constant_override("separation", 12)
	root_vbox.add_child(bottom_hbox)
	_build_bottom_bar(bottom_hbox)


func _add_separator(parent: Control) -> void:
	var sep := HSeparator.new()
	sep.add_theme_color_override("color", COL_PANEL)
	parent.add_child(sep)


# ── Background list (left column) ─────────────────────────────────────────────
func _build_bg_list(parent: HBoxContainer) -> void:
	var panel := _make_panel(200)
	parent.add_child(panel)

	var vbox := VBoxContainer.new()
	vbox.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
	vbox.add_theme_constant_override("separation", 4)
	panel.add_child(vbox)

	var heading := Label.new()
	heading.text = "BACKGROUND"
	heading.add_theme_color_override("font_color", COL_DIM)
	heading.add_theme_font_size_override("font_size", 11)
	vbox.add_child(heading)

	var scroll := ScrollContainer.new()
	scroll.size_flags_vertical = Control.SIZE_EXPAND_FILL
	vbox.add_child(scroll)

	var inner := VBoxContainer.new()
	inner.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	inner.add_theme_constant_override("separation", 3)
	scroll.add_child(inner)

	for bg_id in _bg_ids:
		var bg_data: Dictionary = _backgrounds[bg_id]
		var btn := Button.new()
		btn.text = bg_data.get("name", bg_id)
		btn.alignment = HORIZONTAL_ALIGNMENT_LEFT
		btn.add_theme_color_override("font_color", COL_TEXT)
		btn.custom_minimum_size = Vector2(170, 32)
		btn.pressed.connect(_select_background.bind(bg_id))
		_bg_buttons[bg_id] = btn
		inner.add_child(btn)


# ── Detail panel (centre) ─────────────────────────────────────────────────────
func _build_detail_panel(parent: HBoxContainer) -> void:
	var panel := _make_panel(0, true)   # expand_h
	parent.add_child(panel)

	_detail_label = RichTextLabel.new()
	_detail_label.bbcode_enabled = true
	_detail_label.scroll_active = false
	_detail_label.fit_content = true
	_detail_label.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
	_detail_label.add_theme_color_override("default_color", COL_TEXT)
	panel.add_child(_detail_label)


# ── Attribute allocation panel (right column) ──────────────────────────────────
func _build_attr_panel(parent: HBoxContainer) -> void:
	var panel := _make_panel(240)
	parent.add_child(panel)

	var vbox := VBoxContainer.new()
	vbox.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
	vbox.add_theme_constant_override("separation", 6)
	panel.add_child(vbox)

	var heading := Label.new()
	heading.text = "ATTRIBUTES"
	heading.add_theme_color_override("font_color", COL_DIM)
	heading.add_theme_font_size_override("font_size", 11)
	vbox.add_child(heading)

	_pool_label = Label.new()
	_pool_label.add_theme_font_size_override("font_size", 13)
	vbox.add_child(_pool_label)
	_update_pool_label()

	_add_separator(vbox)

	for i in ATTR_NAMES.size():
		var attr: String = ATTR_NAMES[i]
		var row := HBoxContainer.new()
		row.add_theme_constant_override("separation", 4)
		vbox.add_child(row)

		var lbl := Label.new()
		lbl.text = ATTR_LABELS[i]
		lbl.custom_minimum_size = Vector2(110, 0)
		lbl.add_theme_color_override("font_color", COL_TEXT)
		row.add_child(lbl)

		var btn_minus := Button.new()
		btn_minus.text = "−"
		btn_minus.custom_minimum_size = Vector2(26, 26)
		btn_minus.pressed.connect(_on_attr_minus.bind(attr))
		_attr_btns_minus[attr] = btn_minus
		row.add_child(btn_minus)

		var val_label := Label.new()
		val_label.custom_minimum_size = Vector2(28, 0)
		val_label.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
		val_label.add_theme_font_size_override("font_size", 14)
		_attr_labels[attr] = val_label
		row.add_child(val_label)

		var btn_plus := Button.new()
		btn_plus.text = "+"
		btn_plus.custom_minimum_size = Vector2(26, 26)
		btn_plus.pressed.connect(_on_attr_plus.bind(attr))
		_attr_btns_plus[attr] = btn_plus
		row.add_child(btn_plus)


# ── Bottom bar ─────────────────────────────────────────────────────────────────
func _build_bottom_bar(parent: HBoxContainer) -> void:
	# Spacer
	var spacer_l := Control.new()
	spacer_l.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	parent.add_child(spacer_l)

	var name_lbl := Label.new()
	name_lbl.text = "Name:"
	name_lbl.add_theme_color_override("font_color", COL_TEXT)
	name_lbl.vertical_alignment = VERTICAL_ALIGNMENT_CENTER
	parent.add_child(name_lbl)

	_name_input = LineEdit.new()
	_name_input.placeholder_text = "Enter name…"
	_name_input.custom_minimum_size = Vector2(200, 36)
	parent.add_child(_name_input)

	_err_label = Label.new()
	_err_label.add_theme_color_override("font_color", COL_WARN)
	_err_label.text = ""
	parent.add_child(_err_label)

	var spacer_r := Control.new()
	spacer_r.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	parent.add_child(spacer_r)

	_confirm_btn = Button.new()
	_confirm_btn.text = "▶  BEGIN"
	_confirm_btn.custom_minimum_size = Vector2(120, 36)
	_confirm_btn.add_theme_font_size_override("font_size", 15)
	_confirm_btn.pressed.connect(_on_confirm)
	parent.add_child(_confirm_btn)


# ── Helper: styled panel ──────────────────────────────────────────────────────
func _make_panel(min_width: int, expand_h: bool = false) -> PanelContainer:
	var panel := PanelContainer.new()
	if expand_h:
		panel.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	else:
		panel.custom_minimum_size = Vector2(min_width, 0)
	var style := StyleBoxFlat.new()
	style.bg_color = COL_PANEL
	style.corner_radius_top_left    = 4
	style.corner_radius_top_right   = 4
	style.corner_radius_bottom_left = 4
	style.corner_radius_bottom_right= 4
	style.content_margin_left   = 10
	style.content_margin_right  = 10
	style.content_margin_top    = 10
	style.content_margin_bottom = 10
	panel.add_theme_stylebox_override("panel", style)
	return panel


# ── Background selection ───────────────────────────────────────────────────────
func _select_background(bg_id: String) -> void:
	# Deselect previous
	if _selected_bg_id != "" and _bg_buttons.has(_selected_bg_id):
		_bg_buttons[_selected_bg_id].remove_theme_color_override("font_color")

	_selected_bg_id = bg_id

	# Highlight selected
	if _bg_buttons.has(bg_id):
		_bg_buttons[bg_id].add_theme_color_override("font_color", COL_BONUS)

	_refresh_detail()
	_refresh_attr_display()


func _refresh_detail() -> void:
	if _detail_label == null or _selected_bg_id == "":
		return
	var bg: Dictionary = _backgrounds.get(_selected_bg_id, {})
	if bg.is_empty():
		_detail_label.text = ""
		return

	var sb := ""

	# Name + description
	sb += "[b][color=#e8e8e8]%s[/color][/b]\n" % bg.get("name", _selected_bg_id)
	sb += "[color=#aaaaaa]%s[/color]\n\n" % bg.get("description", "")

	# Attribute bonuses
	var bonuses: Dictionary = bg.get("attribute_bonuses", {})
	if not bonuses.is_empty():
		sb += "[color=#88aacc]Attribute Bonuses[/color]\n"
		for attr: String in bonuses:
			var val: int = bonuses[attr]
			var sign_str: String = "+" if val >= 0 else ""
			var col := "#88dd88" if val >= 0 else "#dd8888"
			var aname: String = attr.capitalize()
			sb += "  [color=%s]%s%d[/color]  %s\n" % [col, sign_str, val, aname]
		sb += "\n"

	# Starting traits
	var traits_arr: Array = bg.get("starting_traits", [])
	if not traits_arr.is_empty():
		sb += "[color=#88aacc]Starting Traits[/color]\n"
		for tid in traits_arr:
			var tdata: Dictionary = {}
			if _content_registry:
				tdata = _content_registry.get_content("trait", tid)
			var tname: String = tdata.get("name", tid)
			sb += "  • %s\n" % tname
		sb += "\n"

	# Starting skills
	var skills_dict: Dictionary = bg.get("starting_skills", {})
	if not skills_dict.is_empty():
		sb += "[color=#88aacc]Starting Skills[/color]\n"
		for sid_k in skills_dict:
			var sdata: Dictionary = {}
			if _content_registry:
				sdata = _content_registry.get_content("skill", sid_k)
			var sname: String = sdata.get("name", sid_k)
			var lvl: int = skills_dict[sid_k]
			sb += "  • %s  (Rank %d)\n" % [sname, lvl]

	_detail_label.text = sb


# ── Attribute display & allocation ────────────────────────────────────────────
func _refresh_attr_display() -> void:
	if _attr_labels.is_empty():
		return
	var bonuses: Dictionary = {}
	if _selected_bg_id != "":
		bonuses = _backgrounds.get(_selected_bg_id, {}).get("attribute_bonuses", {})

	for attr in ATTR_NAMES:
		var base_val: int = ATTR_BASE
		var bg_bonus: int = bonuses.get(attr, 0)
		var alloc_val: int = _alloc.get(attr, 0)
		var total: int = clampi(base_val + bg_bonus + alloc_val, ATTR_MIN, ATTR_MAX)

		var lbl: Label = _attr_labels[attr]
		lbl.text = str(total)
		if bg_bonus + alloc_val > 0:
			lbl.add_theme_color_override("font_color", COL_BONUS)
		elif bg_bonus + alloc_val < 0:
			lbl.add_theme_color_override("font_color", Color(0.95, 0.45, 0.45))
		else:
			lbl.add_theme_color_override("font_color", COL_TEXT)

		# Enable/disable ± buttons based on limits and pool
		var btn_p: Button = _attr_btns_plus.get(attr)
		var btn_m: Button = _attr_btns_minus.get(attr)
		if btn_p:
			btn_p.disabled = (_points_left <= 0) or (total >= ATTR_MAX)
		if btn_m:
			btn_m.disabled = (alloc_val <= 0)


func _update_pool_label() -> void:
	if _pool_label == null:
		return
	_pool_label.text = "Points to spend: %d" % _points_left
	if _points_left > 0:
		_pool_label.add_theme_color_override("font_color", COL_BONUS)
	else:
		_pool_label.add_theme_color_override("font_color", COL_DIM)


func _on_attr_plus(attr: String) -> void:
	if _points_left <= 0:
		return
	var bonuses: Dictionary = _backgrounds.get(_selected_bg_id, {}).get("attribute_bonuses", {})
	var bg_bonus: int = bonuses.get(attr, 0)
	var total: int = ATTR_BASE + bg_bonus + _alloc.get(attr, 0)
	if total >= ATTR_MAX:
		return
	_alloc[attr] = _alloc.get(attr, 0) + 1
	_points_left -= 1
	_update_pool_label()
	_refresh_attr_display()


func _on_attr_minus(attr: String) -> void:
	if _alloc.get(attr, 0) <= 0:
		return
	_alloc[attr] = _alloc.get(attr, 0) - 1
	_points_left += 1
	_update_pool_label()
	_refresh_attr_display()


# ── Confirm & create character ─────────────────────────────────────────────────
func _on_confirm() -> void:
	_err_label.text = ""

	# Validate name
	var char_name: String = _name_input.text.strip_edges()
	if char_name.is_empty():
		_err_label.text = "Enter a name first."
		return
	if _selected_bg_id == "":
		_err_label.text = "Select a background."
		return
	if _world_state == null:
		push_error("CharacterCreationScreen: WorldState is null — cannot create character")
		return

	# Build a new PersonState
	var person := PersonState.new()
	if _entity_registry:
		person.person_id = _entity_registry.generate_id("person")
	else:
		person.person_id = "person_player"

	person.name            = char_name
	person.background_id   = _selected_bg_id
	person.active_role     = "player"

	# Apply attributes: base + background bonuses + player allocations
	var bg: Dictionary = _backgrounds.get(_selected_bg_id, {})
	var bonuses: Dictionary = bg.get("attribute_bonuses", {})
	for attr in ATTR_NAMES:
		var bg_bonus: int = bonuses.get(attr, 0)
		var alloc_val: int = _alloc.get(attr, 0)
		person.attributes[attr] = clampi(ATTR_BASE + bg_bonus + alloc_val, ATTR_MIN, ATTR_MAX)

	# Apply starting traits
	for tid in bg.get("starting_traits", []):
		if not person.traits.has(tid):
			person.traits.append(tid)

	# Apply starting skills
	var starting_skills: Dictionary = bg.get("starting_skills", {})
	for skill_id in starting_skills:
		person.skills[skill_id] = {
			"level":    starting_skills[skill_id],
			"progress": 0.0,
		}

	# Apply starting coin and items
	person.coin = float(bg.get("starting_coin", 0))
	for item_id: String in bg.get("starting_items", []):
		person.carried_items.append(item_id)

	# Pick a starting cell — prefer highest-tier settlement, fall back to first
	var start_cell_id: String = _pick_start_cell()
	person.home_settlement_id = _cell_to_settlement(start_cell_id)
	person.location = {
		"cell_id":  start_cell_id,
		"lx":       0,
		"ly":       0,
		"z_level":  0,
	}

	# Store in WorldState
	_world_state.characters[person.person_id] = person
	_world_state.player_character_id = person.person_id
	_world_state.player_location = person.location.duplicate()

	# Transition to WorldView via SceneManager (replaces this screen; no back-stack entry).
	SceneManager.replace_scene(WORLD_VIEW_SCENE)


func _pick_start_cell() -> String:
	## Return the cell_id of the highest-tier settlement with at least tier 2,
	## or fall back to any settlement's cell, or "".
	if _world_state == null:
		return ""
	var best_cell := ""
	var best_tier := -1
	for sid in _world_state.settlements:
		var sdata: Dictionary = _world_state.get_settlement_dict(sid)
		var tier: int = sdata.get("tier", 0)
		var cell_id: String = sdata.get("cell_id", "")
		if tier > best_tier and tier >= 2 and cell_id != "":
			best_tier = tier
			best_cell = cell_id
	if best_cell != "":
		return best_cell
	# Fallback: any settlement with a cell_id
	for sid in _world_state.settlements:
		var sdata: Dictionary = _world_state.get_settlement_dict(sid)
		var cell_id: String = sdata.get("cell_id", "")
		if cell_id != "":
			return cell_id
	return ""


func _cell_to_settlement(cell_id: String) -> String:
	## Reverse-look up which settlement owns this cell.
	if _world_state == null or cell_id == "":
		return ""
	for sid in _world_state.settlements:
		var sdata: Dictionary = _world_state.get_settlement_dict(sid)
		if sdata.get("cell_id", "") == cell_id:
			return sid
	return ""
