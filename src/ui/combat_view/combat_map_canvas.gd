## CombatMapCanvas — canvas control that draws the WEGO battle grid.
##
## A single Control node whose _draw() paints:
##   · Dark cell backgrounds with 1-px separator lines.
##   · Filled coloured tokens for formations and individual combatants.
##   · Centred text labels on every token (P / E / ★ / ✕ …).
##
## Usage:
##   canvas.refresh(battle, selected_fid)   ← call from CombatView._refresh_map()
class_name CombatMapCanvas
extends Control

# ── Layout ────────────────────────────────────────────────────────────────────
const CELL_PX:  int = 30   # pixel size of each grid cell
const MAP_COLS: int = 25
const MAP_ROWS: int = 25

# ── Colours ───────────────────────────────────────────────────────────────────
const C_CANVAS_BG: Color = Color(0.07, 0.08, 0.07)
const C_CELL_BG:   Color = Color(0.12, 0.13, 0.12)
const C_GRID_LINE: Color = Color(0.21, 0.22, 0.21)

const C_PLAYER:    Color = Color(0.12, 0.48, 0.72)
const C_PLAYER_HL: Color = Color(0.75, 0.68, 0.08)   # selected formation
const C_ENEMY:     Color = Color(0.68, 0.15, 0.15)
const C_DEAD:      Color = Color(0.22, 0.22, 0.22)

const C_TOKEN_TEXT: Color = Color(1.00, 1.00, 1.00)
const C_DEAD_TEXT:  Color = Color(0.50, 0.50, 0.50)

const FONT_SIZE_TOKEN: int = 14
const FONT_SIZE_COUNT: int = 9

# ── State set by CombatView ───────────────────────────────────────────────────
var battle:       BattleState = null
var selected_fid: String      = ""


func _ready() -> void:
	custom_minimum_size = Vector2(MAP_COLS * CELL_PX, MAP_ROWS * CELL_PX)


## Call this from CombatView whenever battle state changes.
func refresh(b: BattleState, sel_fid: String) -> void:
	battle       = b
	selected_fid = sel_fid
	queue_redraw()


# ── Drawing ───────────────────────────────────────────────────────────────────
func _draw() -> void:
	# ── Background ──────────────────────────────────────────────────────────
	draw_rect(Rect2(Vector2.ZERO, custom_minimum_size), C_CANVAS_BG)

	# ── Cell backgrounds + grid lines ────────────────────────────────────────
	for row: int in range(MAP_ROWS):
		for col: int in range(MAP_COLS):
			var rx: int = col * CELL_PX
			var ry: int = row * CELL_PX
			# Cell fill (1-px gap acts as grid line).
			draw_rect(Rect2(rx + 1, ry + 1, CELL_PX - 2, CELL_PX - 2), C_CELL_BG)

	if battle == null:
		return

	# ── Build token map: Vector2i → {color, text, sub_text, is_dead} ─────────
	# Priority: formation anchor > individual combatant.
	var tokens: Dictionary = {}   # Vector2i → Dictionary

	# Individual combatants (drawn as small dots when away from anchor).
	for cid: String in battle.combatants:
		var c: CombatantState = battle.combatants[cid]
		var pos: Vector2i = c.tile_pos
		if not _in_bounds(pos):
			continue
		if c.is_dead:
			tokens[pos] = {color = C_DEAD,   text = "✕",
				sub_text = "", is_dead = true}
		elif c.team_id == "player":
			tokens[pos] = {color = C_PLAYER, text = "p",
				sub_text = "", is_dead = false}
		else:
			tokens[pos] = {color = C_ENEMY,  text = "e",
				sub_text = "", is_dead = false}

	# Formation anchors override with larger labelled tokens.
	for fid: String in battle.formations:
		var f: FormationState = battle.formations[fid]
		var pos: Vector2i = f.anchor_pos
		if not _in_bounds(pos):
			continue
		var alive: int = _count_alive(f)
		var is_sel: bool = fid == selected_fid
		var total: int = f.member_ids.size()
		if alive == 0:
			tokens[pos] = {color = C_DEAD,   text = "✕",
				sub_text = "0/%d" % total, is_dead = true}
		elif f.team_id == "player":
			tokens[pos] = {
				color    = C_PLAYER_HL if is_sel else C_PLAYER,
				text     = "★" if is_sel else "P",
				sub_text = "%d/%d" % [alive, total],
				is_dead  = false,
			}
		else:
			tokens[pos] = {color = C_ENEMY,  text = "E",
				sub_text = "%d/%d" % [alive, total], is_dead = false}

	# ── Draw tokens ──────────────────────────────────────────────────────────
	var font: Font = ThemeDB.fallback_font

	for pos: Vector2i in tokens:
		var info: Dictionary = tokens[pos]
		var rx: int = pos.x * CELL_PX
		var ry: int = pos.y * CELL_PX

		# Filled token background.
		draw_rect(Rect2(rx + 1, ry + 1, CELL_PX - 2, CELL_PX - 2), info.color)

		# Main symbol, vertically centred.
		var main_text: String = info.text
		var ts: Vector2 = font.get_string_size(
			main_text, HORIZONTAL_ALIGNMENT_LEFT, -1, FONT_SIZE_TOKEN)
		var tx: float = rx + (CELL_PX - ts.x) * 0.5
		var ty: float = ry + (CELL_PX + ts.y) * 0.5 - 3.0
		var text_col: Color = C_DEAD_TEXT if info.is_dead else C_TOKEN_TEXT
		draw_string(font, Vector2(tx, ty), main_text,
			HORIZONTAL_ALIGNMENT_LEFT, -1, FONT_SIZE_TOKEN, text_col)

		# Sub-text (member count) in bottom-right corner.
		var sub: String = info.get("sub_text", "")
		if sub != "":
			var sts: Vector2 = font.get_string_size(
				sub, HORIZONTAL_ALIGNMENT_LEFT, -1, FONT_SIZE_COUNT)
			var sx: float = rx + CELL_PX - sts.x - 2.0
			var sy: float = ry + CELL_PX - 2.0
			draw_string(font, Vector2(sx, sy), sub,
				HORIZONTAL_ALIGNMENT_LEFT, -1, FONT_SIZE_COUNT,
				Color(1, 1, 1, 0.65))


# ── Helpers ───────────────────────────────────────────────────────────────────
func _in_bounds(pos: Vector2i) -> bool:
	return pos.x >= 0 and pos.x < MAP_COLS and pos.y >= 0 and pos.y < MAP_ROWS


func _count_alive(f: FormationState) -> int:
	var n: int = 0
	for mid: String in f.member_ids:
		var c: CombatantState = battle.combatants.get(mid)
		if c != null and not c.is_dead:
			n += 1
	return n
