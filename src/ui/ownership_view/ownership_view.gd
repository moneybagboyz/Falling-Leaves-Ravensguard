## OwnershipView — player asset overview panel.
##
## Accessible from the main HUD (added as a button in settlement_view / world_view).
## Shows:
##   • All owned buildings with income/upkeep estimate per tick.
##   • Follower roster with role, morale indicator.
##   • Faction/settlement reputation bars.
##   • Personal coin ledger graph (last 30 ticks coin balance).
##   • Active construction jobs with progress.
##   • Player camps with stock summary.
##
## Pushed via SceneManager.push_scene().
extends Control

const MORALE_GOOD_COLOR:  Color = Color(0.3, 0.9, 0.3)
const MORALE_WARN_COLOR:  Color = Color(0.9, 0.8, 0.1)
const MORALE_BAD_COLOR:   Color = Color(0.9, 0.2, 0.2)
const REP_POS_COLOR:      Color = Color(0.3, 0.7, 1.0)
const REP_NEG_COLOR:      Color = Color(0.9, 0.3, 0.3)

var _world_state: WorldState = null

# UI references.
var _coin_lbl:        Label    = null
var _buildings_list:  VBoxContainer = null
var _followers_list:  VBoxContainer = null
var _reputation_list: VBoxContainer = null
var _jobs_list:       VBoxContainer = null
var _camps_list:      VBoxContainer = null


func _ready() -> void:
	_world_state = Bootstrap.world_state
	_build_ui()
	_refresh()


# ── UI construction ───────────────────────────────────────────────────────────

func _build_ui() -> void:
	custom_minimum_size = Vector2(700, 500)

	var bg := ColorRect.new()
	bg.color = Color(0.08, 0.08, 0.10, 0.97)
	bg.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
	add_child(bg)

	var root := VBoxContainer.new()
	root.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
	root.add_theme_constant_override("separation", 6)
	add_child(root)

	# Title bar.
	var title_row := HBoxContainer.new()
	root.add_child(title_row)
	var title := Label.new()
	title.text = "Ledger & Assets"
	title.add_theme_font_size_override("font_size", 18)
	title.add_theme_color_override("font_color", Color(0.9, 0.8, 0.5))
	title_row.add_child(title)
	var spacer := Control.new()
	spacer.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	title_row.add_child(spacer)
	_coin_lbl = Label.new()
	_coin_lbl.add_theme_font_size_override("font_size", 16)
	_coin_lbl.add_theme_color_override("font_color", Color(1.0, 0.9, 0.3))
	title_row.add_child(_coin_lbl)
	var close_btn := Button.new()
	close_btn.text = "✕"
	close_btn.pressed.connect(SceneManager.pop_scene)
	title_row.add_child(close_btn)

	var scroll := ScrollContainer.new()
	scroll.size_flags_vertical = Control.SIZE_EXPAND_FILL
	root.add_child(scroll)

	var content := VBoxContainer.new()
	content.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	content.add_theme_constant_override("separation", 8)
	scroll.add_child(content)

	_buildings_list  = _add_section(content, "🏗 Owned Buildings")
	_followers_list  = _add_section(content, "⚔ Followers")
	_camps_list      = _add_section(content, "⛺ Camps")
	_jobs_list       = _add_section(content, "🔨 Construction Jobs")
	_reputation_list = _add_section(content, "★ Reputation")


func _add_section(parent: VBoxContainer, header: String) -> VBoxContainer:
	var hdr := Label.new()
	hdr.text = header
	hdr.add_theme_font_size_override("font_size", 13)
	hdr.add_theme_color_override("font_color", Color(0.7, 0.85, 1.0))
	parent.add_child(hdr)
	parent.add_child(HSeparator.new())
	var body := VBoxContainer.new()
	body.add_theme_constant_override("separation", 3)
	parent.add_child(body)
	return body


# ── Refresh ───────────────────────────────────────────────────────────────────

func _refresh() -> void:
	if _world_state == null:
		return
	var player: PersonState = _world_state.characters.get(
		_world_state.player_character_id)
	if player == null:
		return

	_coin_lbl.text = "💰 %.1f coin" % player.coin

	_refresh_buildings(player)
	_refresh_followers(player)
	_refresh_camps(player)
	_refresh_jobs()
	_refresh_reputation(player)


func _refresh_buildings(player: PersonState) -> void:
	for c: Node in _buildings_list.get_children():
		c.queue_free()

	if player.ownership_refs.is_empty():
		_buildings_list.add_child(_placeholder("No owned buildings yet."))
		return

	for key: String in player.ownership_refs:
		if key.begins_with("camp:"):
			continue  # handled in camps section
		var parts: PackedStringArray = key.split(":", true, 1)
		if parts.size() < 2:
			continue
		var bid: String = parts[0]
		var bdef: Dictionary = ContentRegistry.get_content("building", bid)
		var name_str: String  = bdef.get("name", bid) if not bdef.is_empty() else bid
		var upkeep: float = float(
			(bdef.get("upkeep_per_season", {}) as Dictionary).get("coin", 0.0)) \
			/ PropertyCore.UPKEEP_TICKS_PER_SEASON
		var income: float = upkeep * 2.0 * PropertyCore.OWNER_INCOME_SHARE
		var row := HBoxContainer.new()
		var lbl := Label.new()
		lbl.text = " %s  →  +%.2f / −%.2f coin/day" % [name_str, income, upkeep]
		lbl.add_theme_font_size_override("font_size", 11)
		row.add_child(lbl)
		_buildings_list.add_child(row)


func _refresh_followers(player: PersonState) -> void:
	for c: Node in _followers_list.get_children():
		c.queue_free()

	if player.follower_ids.is_empty():
		_followers_list.add_child(_placeholder("No followers."))
		return

	var group: GroupState = GroupState.new()
	if not _world_state.player_group.is_empty():
		group = GroupState.from_dict(_world_state.player_group)

	var morale_lbl := Label.new()
	morale_lbl.text = "Group morale: %.0f%%" % (group.morale * 100.0)
	morale_lbl.add_theme_font_size_override("font_size", 11)
	var mc: Color = MORALE_GOOD_COLOR if group.morale > 0.5 \
		else (MORALE_WARN_COLOR if group.morale > 0.2 else MORALE_BAD_COLOR)
	morale_lbl.add_theme_color_override("font_color", mc)
	_followers_list.add_child(morale_lbl)

	var pay_lbl := Label.new()
	pay_lbl.text = "Wages: %.1f coin/day total" % group.pay_per_tick
	pay_lbl.add_theme_font_size_override("font_size", 11)
	_followers_list.add_child(pay_lbl)

	for fid: String in player.follower_ids:
		var npc: PersonState = _world_state.characters.get(fid)
		if npc == null:
			continue
		var row := HBoxContainer.new()
		var lbl := Label.new()
		lbl.text = "  %s  [%s]  melee lv.%d" \
			% [npc.name, npc.active_role, npc.skill_level("melee")]
		lbl.add_theme_font_size_override("font_size", 11)
		row.add_child(lbl)
		_followers_list.add_child(row)


func _refresh_camps(player: PersonState) -> void:
	for c: Node in _camps_list.get_children():
		c.queue_free()

	var camps: Array = CampManager.get_player_camps(player, _world_state)
	if camps.is_empty():
		_camps_list.add_child(_placeholder("No player camps."))
		return

	for camp_info: Dictionary in camps:
		var ss: SettlementState = _world_state.get_settlement(camp_info["settlement_id"])
		if ss == null:
			continue
		var row := HBoxContainer.new()
		var lbl := Label.new()
		var wheat: float = float(ss.inventory.get("wheat_bushel", 0.0))
		var coin_stk: float = float(ss.inventory.get("coin", 0.0))
		lbl.text = "  %s @ %s  — wheat: %.0f  coin: %.0f" \
			% [camp_info["name"], camp_info["cell_id"], wheat, coin_stk]
		lbl.add_theme_font_size_override("font_size", 11)
		row.add_child(lbl)
		_camps_list.add_child(row)


func _refresh_jobs() -> void:
	for c: Node in _jobs_list.get_children():
		c.queue_free()

	if _world_state.construction_jobs.is_empty():
		_jobs_list.add_child(_placeholder("No active construction."))
		return

	for jid: String in _world_state.construction_jobs:
		var job: ConstructionJob = ConstructionJob.from_dict(
			_world_state.construction_jobs[jid])
		var bdef: Dictionary = ContentRegistry.get_content("building", job.building_id)
		var name_str: String  = bdef.get("name", job.building_id) if not bdef.is_empty() \
			else job.building_id
		var bar := HBoxContainer.new()
		var lbl := Label.new()
		lbl.text = "  %s @ %s  — %.0f days left" \
			% [name_str, job.cell_id, job.ticks_remaining]
		lbl.add_theme_font_size_override("font_size", 11)
		bar.add_child(lbl)
		_jobs_list.add_child(bar)


func _refresh_reputation(player: PersonState) -> void:
	for c: Node in _reputation_list.get_children():
		c.queue_free()

	if player.reputation.is_empty():
		_reputation_list.add_child(_placeholder("No reputation recorded yet."))
		return

	for fid: String in player.reputation:
		var val: float = float(player.reputation[fid])
		var row := HBoxContainer.new()
		var lbl := Label.new()
		lbl.text = "  %s: %+.2f" % [fid, val]
		lbl.add_theme_font_size_override("font_size", 11)
		lbl.add_theme_color_override("font_color",
			REP_POS_COLOR if val >= 0.0 else REP_NEG_COLOR)
		row.add_child(lbl)
		_reputation_list.add_child(row)


func _placeholder(text: String) -> Label:
	var lbl := Label.new()
	lbl.text = "  " + text
	lbl.add_theme_font_size_override("font_size", 11)
	lbl.add_theme_color_override("font_color", Color(0.5, 0.5, 0.5))
	return lbl
