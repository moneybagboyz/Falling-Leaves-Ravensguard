## StateInspector — live debug overlay showing WorldState and simulation stats.
##
## Attach as a CanvasLayer child or use as an autoload.
## Toggle visibility with F10 (configurable).
## Updated every REFRESH_INTERVAL ticks to avoid overhead.
##
## This is a mandatory production deliverable per the technical rules.
extends CanvasLayer

const REFRESH_INTERVAL: int = 1  # Update every N ticks.
const TOGGLE_KEY := KEY_F10

var _panel: PanelContainer
var _label: RichTextLabel
var _last_refresh_tick: int = -1


func _ready() -> void:
	_build_ui()
	SimulationClock.tick_completed.connect(_on_tick)
	# Start hidden; toggle with F10.
	visible = false


func _unhandled_key_input(event: InputEvent) -> void:
	if event is InputEventKey and event.pressed and event.keycode == TOGGLE_KEY:
		visible = not visible


func _on_tick(tick: int) -> void:
	if not visible:
		return
	if tick - _last_refresh_tick < REFRESH_INTERVAL:
		return
	_last_refresh_tick = tick
	_refresh()


func _refresh() -> void:
	var lines: Array[String] = []
	lines.append("[b]── Ravensguard State Inspector ──[/b]")
	lines.append("Tick: [color=yellow]%d[/color]   Paused: [color=yellow]%s[/color]   Speed: [color=yellow]%.1f×[/color]" % [
		SimulationClock.get_tick(),
		str(SimulationClock.is_paused()),
		SimulationClock.get_speed(),
	])
	lines.append("")

	# ContentRegistry summary
	lines.append("[b]ContentRegistry[/b]")
	var content_summary := ContentRegistry.get_summary()
	for type in content_summary:
		lines.append("  %s: %d" % [type, content_summary[type]])
	lines.append("")

	# EntityRegistry summary
	lines.append("[b]EntityRegistry[/b]")
	lines.append("  Tracked IDs: %d" % EntityRegistry.to_dict().get("entity_map", {}).size())
	lines.append("")

	# TickScheduler phase timings (ms since start)
	lines.append("[b]Phase Timings (ms accumulated)[/b]")
	var timings := TickScheduler.get_timings()
	for phase_name in timings:
		lines.append("  %s: %.2f ms" % [phase_name, timings[phase_name]])
	lines.append("")

	# WorldState summary
	var ws := Bootstrap.world_state
	if ws != null:
		lines.append("[b]WorldState[/b]")
		lines.append("  region_id: %s" % ws.region_id)
		lines.append("  seed: %d" % ws.world_seed)
		lines.append("  settlements: %d" % ws.settlements.size())
		lines.append("")

		if not ws.settlements.is_empty():
			lines.append("[b]Settlements[/b]")
			for sid in ws.settlements:
				var s: SettlementState = ws.settlements[sid]
				lines.append("  [color=cyan]%s[/color] (%s)" % [s.name, s.settlement_id])
				lines.append("    pop=%d  prosperity=%.2f  unrest=%.2f" % [
					s.total_population(), s.prosperity, s.unrest
				])
				lines.append("    faction=%s" % s.faction_id)

	_label.text = "\n".join(lines)


# ---------------------------------------------------------------------------
# UI construction
# ---------------------------------------------------------------------------

func _build_ui() -> void:
	layer = 100  # Draw on top of everything.

	_panel = PanelContainer.new()
	_panel.set_anchors_preset(Control.PRESET_TOP_LEFT)
	_panel.position = Vector2(8, 8)
	_panel.custom_minimum_size = Vector2(420, 0)
	add_child(_panel)

	var scroll := ScrollContainer.new()
	scroll.custom_minimum_size = Vector2(420, 600)
	_panel.add_child(scroll)

	_label = RichTextLabel.new()
	_label.bbcode_enabled = true
	_label.fit_content = true
	_label.custom_minimum_size = Vector2(400, 0)
	scroll.add_child(_label)
