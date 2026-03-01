## CombatView — full-screen tactical scene for WEGO combat.
##
## Entered via SceneManager.push_scene("res://src/ui/combat_view/combat_view.tscn",
##     {"battle_id": "<id>"})
## Exits back to WorldView (or CombatTestScene) via SceneManager.pop_scene() after
## battle result is acknowledged.
##
## Layout
## ──────
## ┌─────────────────────────────────────────────────────────┐
## │  TOP BAR: phase label · turn counter · ⏸ Pause          │
## ├─────────────────────┬───────────────────────────────────┤
## │  SIDEBAR (left)     │  MAP PANEL (right, 2/3 width)     │
## │  · Formation list   │  · 25×25 grid of Labels           │
## │  · Order dropdown   │  · Player = cyan, Enemy = red     │
## │  · Target label     │  · Selected formation = bold/★    │
## │  · [Confirm Turn]   │                                   │
## │  · Troop detail     │                                   │
## └─────────────────────┴───────────────────────────────────┘
##
## WEGO flow
## ─────────
## 1. PLANNING  — player picks orders for each formation; Confirm Turn pressed.
## 2. RESOLVING — CombatResolver.resolve_turn() runs; positions update in place.
## 3. RESULTS   — BattleState.result != "" → show summary, pop or stay.
class_name CombatView
extends Control

# ── Constants ─────────────────────────────────────────────────────────────────
const SIDEBAR_W: int = 260

# ── State ─────────────────────────────────────────────────────────────────────
var _world_state:       WorldState  = null
var _battle:            BattleState = null
var _selected_fid:      String      = ""   # formation currently selected in sidebar
var _order_for_fid:     Dictionary  = {}   # formation_id → order string (planned this turn)

# ── UI ────────────────────────────────────────────────────────────────────────
var _phase_label:    Label          = null
var _turn_label:     Label          = null
var _confirm_btn:    Button         = null
var _result_panel:   PanelContainer = null

# Sidebar formation buttons (formation_id → Button)
var _formation_btns: Dictionary     = {}

# Order dropdown and target label
var _order_option:   OptionButton   = null
var _detail_label:   Label          = null

# Canvas map renderer (replaces Label grid).
var _map_canvas:     CombatMapCanvas = null

# ── Lifecycle ─────────────────────────────────────────────────────────────────
func _ready() -> void:
	set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
	mouse_filter = Control.MOUSE_FILTER_STOP

	var boot: Node = get_node_or_null("/root/Bootstrap")
	if boot != null:
		_world_state = boot.world_state

	var params: Dictionary = SceneManager.take_params()
	var bid: String = params.get("battle_id", "")

	if _world_state != null and _world_state.active_battle != null:
		_battle = _world_state.active_battle
	elif bid != "" and _world_state != null:
		# Fallback if push_scene happened before active_battle was set (debug).
		push_warning("CombatView: active_battle not set on WorldState.")

	_build_ui()

	if _battle != null:
		_refresh_all()


# ── Input ─────────────────────────────────────────────────────────────────────
func _unhandled_key_input(event: InputEvent) -> void:
	if not (event is InputEventKey and (event as InputEventKey).pressed):
		return
	match (event as InputEventKey).keycode:
		KEY_ESCAPE:
			_on_back_pressed()
		KEY_ENTER, KEY_KP_ENTER:
			if _confirm_btn != null and not _confirm_btn.disabled:
				_on_confirm_turn_pressed()


# ── UI Construction ───────────────────────────────────────────────────────────
func _build_ui() -> void:
	# Root horizontal split: sidebar | map.
	var root_hbox := HBoxContainer.new()
	root_hbox.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
	add_child(root_hbox)

	# ── SIDEBAR ───────────────────────────────────────────────────────────────
	var sidebar := _make_sidebar()
	root_hbox.add_child(sidebar)

	# ── MAP PANEL ─────────────────────────────────────────────────────────────
	var map_panel := _make_map_panel()
	map_panel.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	root_hbox.add_child(map_panel)

	# ── RESULT OVERLAY ────────────────────────────────────────────────────────
	_result_panel = _make_result_panel()
	_result_panel.visible = false
	add_child(_result_panel)


func _make_sidebar() -> Control:
	var panel := PanelContainer.new()
	panel.custom_minimum_size = Vector2(SIDEBAR_W, 0)

	var vbox := VBoxContainer.new()
	panel.add_child(vbox)

	# Phase / turn bar.
	_phase_label = Label.new()
	_phase_label.text = "Phase: Planning"
	_phase_label.add_theme_font_size_override("font_size", 13)
	vbox.add_child(_phase_label)

	_turn_label = Label.new()
	_turn_label.text = "Turn: 0"
	_turn_label.add_theme_font_size_override("font_size", 11)
	_turn_label.modulate = Color(0.7, 0.7, 0.7)
	vbox.add_child(_turn_label)

	vbox.add_child(HSeparator.new())

	# Formation list header.
	var f_hdr := Label.new()
	f_hdr.text = "YOUR FORMATIONS"
	f_hdr.add_theme_font_size_override("font_size", 11)
	f_hdr.modulate = Color(0.6, 0.8, 1.0)
	vbox.add_child(f_hdr)

	# Scrollable formation button list.
	var scroll := ScrollContainer.new()
	scroll.custom_minimum_size = Vector2(0, 120)
	scroll.size_flags_vertical = Control.SIZE_EXPAND_FILL
	var f_list := VBoxContainer.new()
	f_list.name = "FormationList"
	scroll.add_child(f_list)
	vbox.add_child(scroll)

	vbox.add_child(HSeparator.new())

	# Order selector.
	var ord_lbl := Label.new()
	ord_lbl.text = "Order:"
	ord_lbl.add_theme_font_size_override("font_size", 11)
	vbox.add_child(ord_lbl)

	_order_option = OptionButton.new()
	for o: String in [
			FormationState.ORDER_ADVANCE,
			FormationState.ORDER_HOLD,
			FormationState.ORDER_CHARGE,
			FormationState.ORDER_FLANK,
			FormationState.ORDER_RETREAT,
	]:
		_order_option.add_item(o.capitalize())
	_order_option.item_selected.connect(_on_order_selected)
	vbox.add_child(_order_option)

	vbox.add_child(HSeparator.new())

	# Confirm turn button.
	_confirm_btn = Button.new()
	_confirm_btn.text = "⚔ Confirm Turn"
	_confirm_btn.custom_minimum_size = Vector2(0, 38)
	_confirm_btn.add_theme_color_override("font_color", Color(1.0, 0.9, 0.3))
	_confirm_btn.pressed.connect(_on_confirm_turn_pressed)
	vbox.add_child(_confirm_btn)

	vbox.add_child(HSeparator.new())

	# Troop detail label.
	_detail_label = Label.new()
	_detail_label.text = "Select a formation\nto see details."
	_detail_label.add_theme_font_size_override("font_size", 11)
	_detail_label.modulate = Color(0.75, 0.75, 0.75)
	_detail_label.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	_detail_label.custom_minimum_size = Vector2(0, 80)
	vbox.add_child(_detail_label)

	vbox.add_child(HSeparator.new())

	# Back / Retreat.
	var back_btn := Button.new()
	back_btn.text = "◀ Flee Battle"
	back_btn.modulate = Color(0.8, 0.5, 0.5)
	back_btn.pressed.connect(_on_back_pressed)
	vbox.add_child(back_btn)

	return panel


func _make_map_panel() -> Control:
	var outer := VBoxContainer.new()

	# Top label strip.
	var top_bar := HBoxContainer.new()
	outer.add_child(top_bar)

	var map_hdr := Label.new()
	map_hdr.text = "Battle Map  (25×25)"
	map_hdr.add_theme_font_size_override("font_size", 11)
	map_hdr.modulate = Color(0.6, 0.6, 0.6)
	top_bar.add_child(map_hdr)

	# Canvas-based map renderer.
	var scroll := ScrollContainer.new()
	scroll.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	scroll.size_flags_vertical   = Control.SIZE_EXPAND_FILL
	outer.add_child(scroll)

	_map_canvas = CombatMapCanvas.new()
	scroll.add_child(_map_canvas)

	return outer


func _make_result_panel() -> PanelContainer:
	var panel := PanelContainer.new()
	panel.set_anchors_and_offsets_preset(Control.PRESET_CENTER)
	panel.custom_minimum_size = Vector2(400, 240)

	var vbox := VBoxContainer.new()
	panel.add_child(vbox)

	var title := Label.new()
	title.name = "ResultTitle"
	title.text = "Battle Over"
	title.add_theme_font_size_override("font_size", 20)
	title.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	vbox.add_child(title)

	var body := Label.new()
	body.name = "ResultBody"
	body.text = ""
	body.add_theme_font_size_override("font_size", 13)
	body.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	vbox.add_child(body)

	vbox.add_child(HSeparator.new())

	var ok_btn := Button.new()
	ok_btn.text = "▶ Continue"
	ok_btn.custom_minimum_size = Vector2(0, 36)
	ok_btn.pressed.connect(_on_result_continue_pressed)
	vbox.add_child(ok_btn)

	return panel


# ── Refresh helpers ───────────────────────────────────────────────────────────
func _refresh_all() -> void:
	_refresh_phase_bar()
	_refresh_formation_list()
	_refresh_map()
	if _battle.result != "":
		_show_result()


func _refresh_phase_bar() -> void:
	if _phase_label == null or _battle == null:
		return
	var phase_str: String = _battle.phase.capitalize()
	_phase_label.text = "Phase: %s" % phase_str
	_turn_label.text  = "Turn: %d" % _battle.turn

	var is_planning: bool = _battle.phase == BattleState.PHASE_PLANNING
	if _confirm_btn != null:
		_confirm_btn.disabled = not is_planning
	if _order_option != null:
		_order_option.disabled = not is_planning


func _refresh_formation_list() -> void:
	if _battle == null:
		return
	# Find the FormationList VBoxContainer inside the sidebar.
	var f_list: VBoxContainer = _find_formation_list()
	if f_list == null:
		return

	# Clear old buttons.
	for child: Node in f_list.get_children():
		child.queue_free()
	_formation_btns.clear()

	# Add player formations.
	for fid: String in _battle.formations:
		var f: FormationState = _battle.formations[fid]
		if f.team_id != "player":
			continue
		var btn := Button.new()
		var alive: int = _count_alive(f)
		var total: int = f.member_ids.size()
		btn.text = "%s (%d/%d)" % [f.label, alive, total]
		if fid == _selected_fid:
			btn.add_theme_color_override("font_color", Color(1.0, 1.0, 0.3))
		btn.pressed.connect(func() -> void: _select_formation(fid))
		f_list.add_child(btn)
		_formation_btns[fid] = btn


func _find_formation_list() -> VBoxContainer:
	# Walk the scene tree looking for the "FormationList" node.
	return _find_node_by_name(self, "FormationList") as VBoxContainer


func _find_node_by_name(root: Node, target: String) -> Node:
	if root.name == target:
		return root
	for child: Node in root.get_children():
		var found: Node = _find_node_by_name(child, target)
		if found != null:
			return found
	return null


func _refresh_map() -> void:
	if _map_canvas != null:
		_map_canvas.refresh(_battle, _selected_fid)


# ── Interaction ───────────────────────────────────────────────────────────────
func _select_formation(fid: String) -> void:
	_selected_fid = fid
	# Sync order dropdown to the planned order for this formation.
	var f: FormationState = _battle.formations.get(fid)
	if f == null:
		return
	var order_str: String = _order_for_fid.get(fid, f.order)
	_sync_order_dropdown(order_str)
	_refresh_detail_label(f)
	_refresh_formation_list()
	_refresh_map()


func _sync_order_dropdown(order_str: String) -> void:
	if _order_option == null:
		return
	var orders: Array = [
		FormationState.ORDER_ADVANCE,
		FormationState.ORDER_HOLD,
		FormationState.ORDER_CHARGE,
		FormationState.ORDER_FLANK,
		FormationState.ORDER_RETREAT,
	]
	var idx: int = orders.find(order_str)
	if idx >= 0:
		_order_option.selected = idx


func _on_order_selected(idx: int) -> void:
	if _selected_fid == "" or _battle == null:
		return
	var orders: Array = [
		FormationState.ORDER_ADVANCE,
		FormationState.ORDER_HOLD,
		FormationState.ORDER_CHARGE,
		FormationState.ORDER_FLANK,
		FormationState.ORDER_RETREAT,
	]
	if idx < orders.size():
		_order_for_fid[_selected_fid] = orders[idx]


func _refresh_detail_label(f: FormationState) -> void:
	if _detail_label == null or _battle == null:
		return
	var alive_ids: Array = f.member_ids.filter(func(mid: String) -> bool:
		var c: CombatantState = _battle.combatants.get(mid)
		return c != null and not c.is_dead
	)
	var lines: PackedStringArray = []
	lines.append("%s  [%d/%d alive]" % [f.label, alive_ids.size(), f.member_ids.size()])
	lines.append("Morale: %.0f%%" % (f.morale * 100))
	lines.append("Order: %s" % _order_for_fid.get(f.formation_id, f.order))
	for mid: String in alive_ids.slice(0, 4):
		var c: CombatantState = _battle.combatants[mid]
		var hp_bar: String = "▓▓▓▓▓" if c.pain < 0.2 else ("▓▓▒▒▒" if c.pain < 0.6 else "▒▒▒░░")
		lines.append("  %s  %s  stm:%.0f%%" % [c.display_name, hp_bar, c.stamina * 100])
	if alive_ids.size() > 4:
		lines.append("  … +%d more" % (alive_ids.size() - 4))
	_detail_label.text = "\n".join(lines)


# ── Turn resolution ───────────────────────────────────────────────────────────
func _on_confirm_turn_pressed() -> void:
	if _battle == null or _battle.phase != BattleState.PHASE_PLANNING:
		return

	# Apply planned orders to formations.
	for fid: String in _order_for_fid:
		var f: FormationState = _battle.formations.get(fid)
		if f != null:
			f.order = _order_for_fid[fid]
	_order_for_fid.clear()

	# Enemy AI picks orders.
	CombatAI.assign_enemy_orders(_battle)

	# Switch to resolving phase and run resolution.
	_battle.phase = BattleState.PHASE_RESOLVING
	_refresh_phase_bar()

	# resolve_turn returns true when battle is over.
	var world_seed_val: int = _world_state.world_seed if _world_state != null else 0
	var done: bool = CombatResolver.resolve_turn(_battle, {}, world_seed_val)

	if done or _battle.result != "":
		_battle.phase = BattleState.PHASE_RESULTS
		_refresh_all()
		_show_result()
	else:
		_battle.phase = BattleState.PHASE_PLANNING
		_refresh_all()


# ── Result overlay ────────────────────────────────────────────────────────────
func _show_result() -> void:
	if _result_panel == null or _battle == null:
		return
	var title_lbl: Label = _result_panel.get_node_or_null("VBoxContainer/ResultTitle")
	var body_lbl:  Label = _result_panel.get_node_or_null("VBoxContainer/ResultBody")

	var result_text: String
	match _battle.result:
		"player_victory":
			result_text = "Victory!"
			if title_lbl: title_lbl.modulate = Color(0.3, 1.0, 0.5)
		"player_defeat":
			result_text = "Defeat"
			if title_lbl: title_lbl.modulate = Color(1.0, 0.3, 0.3)
		_:
			result_text = "Draw"
			if title_lbl: title_lbl.modulate = Color(1.0, 1.0, 0.5)

	if title_lbl: title_lbl.text = result_text

	if body_lbl:
		var loot_str: String = ""
		if not _battle.loot_pool.is_empty():
			loot_str = "\n\nLoot: %s" % ", ".join(Array(_battle.loot_pool))
		body_lbl.text = "Turn %d ended.%s\n\nPress Continue to return to the world." % [
			_battle.turn, loot_str]

	_result_panel.visible = true


func _on_result_continue_pressed() -> void:
	if _world_state != null and _battle != null:
		var summary: Dictionary = PostBattleResolver.resolve(_battle, _world_state)
		push_warning("CombatView: PostBattleResolver summary: %s" % str(summary))
	SceneManager.pop_scene()


func _on_back_pressed() -> void:
	# Flee — treat as defeat.
	if _battle != null and _battle.result == "":
		_battle.result = "player_defeat"
	if _world_state != null and _battle != null:
		PostBattleResolver.resolve(_battle, _world_state)
	SceneManager.pop_scene()


# ── Utilities ─────────────────────────────────────────────────────────────────
func _count_alive(f: FormationState) -> int:
	var count: int = 0
	for mid: String in f.member_ids:
		var c: CombatantState = _battle.combatants.get(mid)
		if c != null and not c.is_dead:
			count += 1
	return count
