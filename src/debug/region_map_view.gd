## RegionMapView — P1-07 debug overlay for generated world maps.
##
## Press F9 to toggle. Renders terrain colour, settlement markers, route edges.
## Use the seed and re-generate button in the top-left panel to run again live.
##
## Attach this script to a CanvasLayer named RegionMapView in your debug scene,
## or let state_inspector.gd instantiate it.
class_name RegionMapView
extends CanvasLayer

# ── Tune these to match your viewport ────────────────────────────────────────
const CELL_SIZE:   int = 4    # pixels per grid cell
const MARGIN:      int = 8

# ── Terrain palette ───────────────────────────────────────────────────────────
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

# ── Tier marker colours ────────────────────────────────────────────────────────
const TIER_COLORS: Array = [
	Color(0.7, 0.7, 0.7),   # 0 Hamlet
	Color(1.0, 1.0, 0.5),   # 1 Village
	Color(1.0, 0.75, 0.1),  # 2 Town
	Color(1.0, 0.45, 0.1),  # 3 City
	Color(1.0, 0.2,  0.2),  # 4 Metropolis
]

var _visible:  bool          = false
var _ws:       WorldState    = null
var _canvas:   Node2D        = null
var _ui:       Control       = null
var _seed_in:  LineEdit      = null
var _status:   Label         = null
var _cur_seed: int           = 42

# ── Lifecycle ─────────────────────────────────────────────────────────────────
func _ready() -> void:
	layer = 20
	visible = false
	_build_ui()


func _unhandled_input(event: InputEvent) -> void:
	if event is InputEventKey and event.pressed and not event.echo:
		if event.keycode == KEY_F9:
			_visible = not _visible
			visible  = _visible
			if _visible and _ws == null:
				_run_gen()


# ── UI construction ────────────────────────────────────────────────────────────
func _build_ui() -> void:
	# Canvas node for 2D drawing.
	_canvas = Node2D.new()
	_canvas.name = "MapCanvas"
	add_child(_canvas)
	_canvas.draw.connect(_on_canvas_draw)

	# Side panel for controls.
	_ui = VBoxContainer.new()
	_ui.set_anchors_preset(Control.PRESET_TOP_LEFT)
	_ui.position = Vector2(MARGIN, MARGIN)
	_ui.custom_minimum_size = Vector2(200, 0)
	add_child(_ui)

	var seed_label := Label.new()
	seed_label.text = "World seed:"
	_ui.add_child(seed_label)

	_seed_in = LineEdit.new()
	_seed_in.text = str(_cur_seed)
	_seed_in.custom_minimum_size = Vector2(180, 0)
	_ui.add_child(_seed_in)

	var btn := Button.new()
	btn.text = "Regenerate"
	btn.pressed.connect(_on_regen_pressed)
	_ui.add_child(btn)

	_status = Label.new()
	_status.text = ""
	_ui.add_child(_status)


# ── Generation ────────────────────────────────────────────────────────────────
func _run_gen() -> void:
	_status.text = "Generating…"
	# Yield one frame so the label renders.
	await get_tree().process_frame
	_cur_seed = _seed_in.text.to_int()
	_ws = RegionGenerator.generate(_cur_seed)
	_status.text = "Done — %d settlements" % _ws.settlements.size()
	_canvas.queue_redraw()


func _on_regen_pressed() -> void:
	_run_gen()


# ── Drawing ───────────────────────────────────────────────────────────────────
func _on_canvas_draw() -> void:
	if _ws == null:
		return

	# Determine grid extents from cell keys.
	var max_x: int = 0
	var max_y: int = 0
	for key: String in _ws.world_tiles.keys():
		var cd: Dictionary = _ws.world_tiles[key]
		max_x = maxi(max_x, int(cd.get("grid_x", 0)))
		max_y = maxi(max_y, int(cd.get("grid_y", 0)))

	# Terrain tiles.
	for key: String in _ws.world_tiles.keys():
		var cd: Dictionary  = _ws.world_tiles[key]
		var gx: int         = int(cd.get("grid_x", 0))
		var gy: int         = int(cd.get("grid_y", 0))
		var terrain: String = cd.get("terrain_type", "plains")
		var col: Color = TERRAIN_COLORS.get(terrain, Color(0.5, 0.5, 0.5))
		var rect := Rect2(
			Vector2(220 + gx * CELL_SIZE, gy * CELL_SIZE),
			Vector2(CELL_SIZE, CELL_SIZE)
		)
		_canvas.draw_rect(rect, col, true)

	# Route edges.
	for sid: String in _ws.routes.keys():
		var sdata: Dictionary = _ws.get_settlement_dict(sid)
		if sdata.is_empty():
			continue
		var ax: float = float(int(sdata.get("tile_x", 0))) * CELL_SIZE + 220 + CELL_SIZE * 0.5
		var ay: float = float(int(sdata.get("tile_y", 0))) * CELL_SIZE + CELL_SIZE * 0.5
		for edge: Dictionary in _ws.routes[sid]:
			var to_sid: String = edge.get("to_id", "")
			var tdata: Dictionary = _ws.get_settlement_dict(to_sid)
			if tdata.is_empty():
				continue
			var bx: float = float(int(tdata.get("tile_x", 0))) * CELL_SIZE + 220 + CELL_SIZE * 0.5
			var by: float = float(int(tdata.get("tile_y", 0))) * CELL_SIZE + CELL_SIZE * 0.5
			_canvas.draw_line(Vector2(ax, ay), Vector2(bx, by), Color(1, 1, 1, 0.5), 1.0)

	# Settlement dots.
	for sid: String in _ws.settlements.keys():
		var sdata: Dictionary = _ws.get_settlement_dict(sid)
		var sx: float = float(int(sdata.get("tile_x", 0))) * CELL_SIZE + 220 + CELL_SIZE * 0.5
		var sy: float = float(int(sdata.get("tile_y", 0))) * CELL_SIZE + CELL_SIZE * 0.5
		var tier: int = int(sdata.get("tier", 0))
		var radius: float = 2.0 + tier * 1.5
		var col: Color = TIER_COLORS[clampi(tier, 0, TIER_COLORS.size() - 1)]
		_canvas.draw_circle(Vector2(sx, sy), radius, col)
