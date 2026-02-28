## WorldAudit — weekly digest printed to the Godot log every 7 in-game days.
##
## Time model: 1 tick = 1 in-game day (STRATEGIC_CADENCE = 1).
## The audit fires once every 7 ticks (7 days).
##
## Each audit prints:
##   • A header with the week number and absolute tick.
##   • One summary row per settlement (name, tier, pop, wheat, coin,
##     timber, prosperity, unrest).
##   • A global footer (richest, hungriest, most unrest, party count).
##
## All output goes through print() so it appears in the Godot output log
## and in `--headless` runs captured from stdout.
class_name WorldAudit
extends RefCounted

## Ticks per in-game day. 1 tick = 1 day (STRATEGIC_CADENCE = 1).
const TICKS_PER_DAY:  int = 1
## Ticks between audit reports (7 days).
const TICKS_PER_WEEK: int = 7

## Key goods to track in the per-settlement summary.
const TRACKED_GOODS: Array[String] = ["wheat_bushel", "timber_log", "coin", "iron_ore"]

var _world_state:    WorldState = null
var _last_week_seen: int        = -1


func setup(ws: WorldState) -> void:
	_world_state    = ws
	_last_week_seen = -1


## Hook — called every PRODUCTION_PULSE tick.
func tick_audit(tick: int) -> void:
	if _world_state == null:
		return

	@warning_ignore("integer_division")
	var week_num: int = tick / TICKS_PER_WEEK
	if week_num == _last_week_seen:
		return
	_last_week_seen = week_num

	_print_weekly_audit(tick, week_num)


# ── Internal ──────────────────────────────────────────────────────────────────

func _print_weekly_audit(tick: int, week_num: int) -> void:
	var ws: WorldState = _world_state

	@warning_ignore("integer_division")
	var day_num: int = tick / TICKS_PER_DAY

	print("")
	print("╔══════════════════════════════════════════════════════════════╗")
	print("  WORLD AUDIT  •  Week %-4d  (Day %-5d  Tick %d)" % [week_num, day_num, tick])
	print("╚══════════════════════════════════════════════════════════════╝")

	if ws.settlements.is_empty():
		print("  (no settlements)")
		return

	# Column header
	print("  %-18s %4s %5s  %8s  %8s  %8s  %8s  %6s  %6s" % [
		"Settlement", "Tier", "Pop",
		"Wheat", "Timber", "Coin",
		"Iron", "Prosp", "Unrest"
	])
	print("  " + "─".repeat(90))

	# Per-settlement rows
	var richest_name:   String = ""
	var richest_coin:   float  = -1.0
	var hungriest_name: String = ""
	var hungriest_val:  float  = 1e9
	var angriest_name:  String = ""
	var angriest_val:   float  = -1.0

	var sorted_ids: Array = ws.settlements.keys()
	sorted_ids.sort()

	for sid: String in sorted_ids:
		var sv = ws.settlements[sid]
		if not (sv is SettlementState):
			continue
		var ss: SettlementState = sv

		var wheat:  float = ss.inventory.get("wheat_bushel", 0.0)
		var timber: float = ss.inventory.get("timber_log",   0.0)
		var coin:   float = ss.inventory.get("coin",         0.0)
		var iron:   float = ss.inventory.get("iron_ore",     0.0)

		# Shortage flag: mark goods in deficit
		var flags: String = ""
		for g: String in TRACKED_GOODS:
			if ss.shortages.get(g, 0.0) > 0.1:
				flags += "!" + g.substr(0, 3) + " "

		print("  %-18s %4d %5d  %8.1f  %8.1f  %8.1f  %8.1f  %6.3f  %6.3f  %s" % [
			ss.name, ss.tier, ss.total_population(),
			wheat, timber, coin, iron,
			ss.prosperity, ss.unrest,
			flags
		])

		# Track extremes
		if coin > richest_coin:
			richest_coin  = coin
			richest_name  = ss.name
		var food_ratio: float = wheat / maxf(float(ss.total_population()) * 0.015, 0.001)
		if food_ratio < hungriest_val:
			hungriest_val  = food_ratio
			hungriest_name = ss.name
		if ss.unrest > angriest_val:
			angriest_val  = ss.unrest
			angriest_name = ss.name

	print("  " + "─".repeat(90))

	# Trade party summary
	var party_count: int = ws.trade_parties.size()
	var party_summary: Dictionary = {}   # good → count
	for pid: String in ws.trade_parties.keys():
		var p: Dictionary = ws.trade_parties[pid]
		for g: String in p.get("cargo", {}).keys():
			party_summary[g] = party_summary.get(g, 0) + 1

	var party_line: String = "  Trade parties active: %d" % party_count
	if not party_summary.is_empty():
		party_line += "  (carrying:"
		for g: String in party_summary.keys():
			party_line += " %s×%d" % [g, party_summary[g]]
		party_line += ")"
	print(party_line)

	# Footer notes
	if richest_name:
		print("  Richest:    %s  (%.0f coin)" % [richest_name, richest_coin])
	if hungriest_name:
		print("  Hungriest:  %s  (food ratio %.2f)" % [hungriest_name, hungriest_val])
	if angriest_name:
		print("  Most unrest: %s  (%.3f)" % [angriest_name, angriest_val])
	print("")
