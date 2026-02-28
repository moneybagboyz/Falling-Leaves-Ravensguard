## EconomyView — live debug overlay for the regional economy.
##
## A CanvasLayer that sits above the world map. Toggle with F10.
## Call setup(world_state) after Bootstrap has run (WorldGenScreen does this
## automatically after generation).
##
## Displays per-settlement: inventory, prices, prosperity, unrest, active
## trade parties. A thin header bar shows the current tick and global totals.
##
## Layer order: 21  (above RegionMapView at 20)
class_name EconomyView
extends CanvasLayer

# ── Layout constants ──────────────────────────────────────────────────────────
const PANEL_WIDTH:  int = 560
const PANEL_HEIGHT: int = 700
const FONT_SMALL:   int = 10
const FONT_NORMAL:  int = 12

# ── Internal state ────────────────────────────────────────────────────────────
var _world_state: WorldState     = null
var _visible_now: bool           = false

# UI nodes created in _ready
var _root:       PanelContainer  = null
var _header:     Label           = null
var _scroll:     ScrollContainer = null
var _content:    VBoxContainer   = null
var _footer:     Label           = null


# ── Public API ────────────────────────────────────────────────────────────────

func setup(ws: WorldState) -> void:
	_world_state = ws
	if _visible_now:
		_refresh()


func refresh() -> void:
	_refresh()


# ── Lifecycle ─────────────────────────────────────────────────────────────────

func _ready() -> void:
	layer   = 21
	visible = false
	_build_layout()


func _unhandled_input(event: InputEvent) -> void:
	if event is InputEventKey and event.pressed and not event.echo:
		if event.keycode == KEY_F10:
			_toggle()


# ── Layout builder ────────────────────────────────────────────────────────────

func _build_layout() -> void:
	# Outer panel
	_root = PanelContainer.new()
	_root.set_anchors_preset(Control.PRESET_TOP_RIGHT)
	_root.custom_minimum_size = Vector2(PANEL_WIDTH, PANEL_HEIGHT)
	_root.position            = Vector2(-PANEL_WIDTH - 4, 4)
	add_child(_root)

	var vbox := VBoxContainer.new()
	_root.add_child(vbox)

	# ── Header bar ────────────────────────────────────────────────────────────
	var hbar := HBoxContainer.new()
	vbox.add_child(hbar)

	_header = Label.new()
	_header.text = "Economy View  [F10 to close]"
	_header.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	_header.add_theme_font_size_override("font_size", FONT_NORMAL)
	_header.modulate = Color(1.0, 0.9, 0.4)
	hbar.add_child(_header)

	var refresh_btn := Button.new()
	refresh_btn.text = "↺"
	refresh_btn.custom_minimum_size = Vector2(30, 0)
	refresh_btn.pressed.connect(_refresh)
	hbar.add_child(refresh_btn)

	var close_btn := Button.new()
	close_btn.text = "✕"
	close_btn.custom_minimum_size = Vector2(30, 0)
	close_btn.pressed.connect(_toggle)
	hbar.add_child(close_btn)

	vbox.add_child(_hsep())

	# ── Scrollable content ────────────────────────────────────────────────────
	_scroll = ScrollContainer.new()
	_scroll.size_flags_vertical = Control.SIZE_EXPAND_FILL
	_scroll.custom_minimum_size = Vector2(PANEL_WIDTH - 8, PANEL_HEIGHT - 80)
	vbox.add_child(_scroll)

	_content = VBoxContainer.new()
	_content.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	_scroll.add_child(_content)

	vbox.add_child(_hsep())

	# ── Footer ────────────────────────────────────────────────────────────────
	_footer = Label.new()
	_footer.text = ""
	_footer.add_theme_font_size_override("font_size", FONT_SMALL)
	_footer.modulate = Color(0.6, 0.6, 0.6)
	vbox.add_child(_footer)


# ── Toggle ────────────────────────────────────────────────────────────────────

func _toggle() -> void:
	_visible_now = not _visible_now
	visible      = _visible_now
	if _visible_now:
		_refresh()


# ── Content refresh ───────────────────────────────────────────────────────────

func _refresh() -> void:
	if _world_state == null or _content == null:
		return

	for ch in _content.get_children():
		ch.queue_free()

	var ws:   WorldState = _world_state
	var tick: int        = ws.current_tick

	_header.text = "Economy View  [F10]  —  Tick %d  |  %d parties active" % [
		tick, ws.trade_parties.size()
	]

	var sorted_ids: Array = ws.settlements.keys()
	sorted_ids.sort()

	for sid: String in sorted_ids:
		var sv = ws.settlements.get(sid)
		if not (sv is SettlementState):
			continue
		_content.add_child(_make_settlement_block(sv, sid, ws))
		_content.add_child(_hsep())

	if sorted_ids.is_empty():
		var lbl := Label.new()
		lbl.text = "  No settlements — generate a world first."
		lbl.modulate = Color(0.5, 0.5, 0.5)
		_content.add_child(lbl)

	# Footer: global totals
	var total_pop:   int   = 0
	var total_wheat: float = 0.0
	var total_coin:  float = 0.0
	for sid: String in ws.settlements.keys():
		var sv = ws.settlements.get(sid)
		if sv is SettlementState:
			total_pop   += sv.total_population()
			total_wheat += sv.inventory.get("wheat_bushel", 0.0)
			total_coin  += sv.inventory.get("coin",         0.0)
	_footer.text = (
		"Settlements: %d   Total pop: %d   "
		+ "World wheat: %.0f   World coin: %.0f"
	) % [sorted_ids.size(), total_pop, total_wheat, total_coin]


func _make_settlement_block(
		ss:  SettlementState,
		sid: String,
		ws:  WorldState
) -> Control:
	var vb := VBoxContainer.new()

	# Title
	var title := Label.new()
	title.text = "%s  (tier %d)  pop: %d" % [ss.name, ss.tier, ss.total_population()]
	title.add_theme_font_size_override("font_size", FONT_NORMAL)
	title.modulate = Color(1.0, 0.85, 0.4)
	vb.add_child(title)

	# Stat bar
	var meta := Label.new()
	meta.text = "  prosperity: %.3f   unrest: %.3f   sid: %s" % [
		ss.prosperity, ss.unrest, sid
	]
	meta.add_theme_font_size_override("font_size", FONT_SMALL)
	meta.modulate = Color(0.75, 0.75, 0.75)
	vb.add_child(meta)

	# Inventory / price table header
	var col_hdr := Label.new()
	col_hdr.text = "  %-22s %9s %9s %8s" % ["Good", "Inventory", "Price", "Shortage"]
	col_hdr.add_theme_font_size_override("font_size", FONT_SMALL)
	col_hdr.modulate = Color(0.5, 0.8, 1.0)
	vb.add_child(col_hdr)

	var all_goods: Array = ss.inventory.keys()
	for g: String in ss.prices.keys():
		if not all_goods.has(g):
			all_goods.append(g)
	all_goods.sort()

	for good_id: String in all_goods:
		var inv:      float = ss.inventory.get(good_id, 0.0)
		var price:    float = ss.prices.get(good_id, 0.0)
		var shortage: float = ss.shortages.get(good_id, 0.0)
		var row := Label.new()
		row.text = "  %-22s %9.2f %9.2f %8.2f" % [good_id, inv, price, shortage]
		row.add_theme_font_size_override("font_size", FONT_SMALL)
		if shortage > 0.1:
			row.modulate = Color(1.0, 0.45, 0.45)
		elif inv > 80.0:
			row.modulate = Color(0.45, 1.0, 0.6)
		else:
			row.modulate = Color(0.85, 0.85, 0.85)
		vb.add_child(row)

	# Trade parties
	var party_lines: Array[String] = []
	for pid: String in ws.trade_parties.keys():
		var p: Dictionary = ws.trade_parties[pid]
		var origin: String = p.get("origin_id", "")
		var dest:   String = p.get("dest_id",   "")
		if origin != sid and dest != sid:
			continue
		var dir:       String = "→ " + dest   if origin == sid else "← " + origin
		var cargo_str: String = ""
		for g: String in p.get("cargo", {}).keys():
			cargo_str += "%s:%.1f " % [g, p["cargo"][g]]
		party_lines.append("  %s  [%s]  en route: %d ticks" % [
			dir, cargo_str.strip_edges(), p.get("ticks_en_route", 0)
		])

	if not party_lines.is_empty():
		var ph := Label.new()
		ph.text = "  Trade parties:"
		ph.add_theme_font_size_override("font_size", FONT_SMALL)
		ph.modulate = Color(0.5, 1.0, 0.5)
		vb.add_child(ph)
		for line: String in party_lines:
			var pl := Label.new()
			pl.text = line
			pl.add_theme_font_size_override("font_size", FONT_SMALL)
			vb.add_child(pl)

	# Production log (last 5 entries)
	var log_entries: Array = ss.production_log
	if not log_entries.is_empty():
		var lh := Label.new()
		lh.text = "  Recent production:"
		lh.add_theme_font_size_override("font_size", FONT_SMALL)
		lh.modulate = Color(0.7, 0.7, 1.0)
		vb.add_child(lh)
		var start_i: int = maxi(0, log_entries.size() - 5)
		for i: int in range(start_i, log_entries.size()):
			var entry = log_entries[i]
			var entry_str: String
			if entry is Dictionary:
				entry_str = "  t%d  %s → %.2f %s" % [
					entry.get("tick", 0),
					entry.get("recipe_id", "?"),
					float(entry.get("output_amount", 0.0)),
					entry.get("output_good", "?")
				]
			else:
				entry_str = "  " + str(entry)
			var ll := Label.new()
			ll.text = entry_str
			ll.add_theme_font_size_override("font_size", FONT_SMALL)
			ll.modulate = Color(0.65, 0.65, 0.65)
			vb.add_child(ll)

	return vb


# ── Helpers ───────────────────────────────────────────────────────────────────

func _hsep() -> HSeparator:
	var sep := HSeparator.new()
	sep.modulate = Color(0.25, 0.25, 0.25)
	return sep
