## CombatTestScene — debug bootstrap for the combat system.
##
## Bypasses full game flow: hard-spawns 2 player formations vs 2 enemy formations,
## equips them, places them on a flat 40×40 map, and drops straight into CombatView.
##
## Usage (run via Bootstrap debug flag or a standalone test scene):
##   Get-ChildItem project.godot        # ensure Godot is not already running
##   # In Godot editor: run THIS scene directly (Main Scene override).
##
## Gate: only active in debug builds — checks OS.has_feature("debug").
class_name CombatTestScene
extends Node


func _ready() -> void:
	if not OS.has_feature("debug"):
		push_warning("CombatTestScene: not a debug build — aborting.")
		return

	var world_state := _build_fake_world_state()
	var battle      := _build_test_battle(world_state)
	world_state.active_battle = battle

	# Wire world state into Bootstrap so CombatView can find it.
	var boot: Node = get_node_or_null("/root/Bootstrap")
	if boot != null:
		boot.world_state = world_state

	# Push CombatView.
	call_deferred("_launch")


func _launch() -> void:
	SceneManager.push_scene(
		"res://src/ui/combat_view/combat_view.tscn",
		{"battle_id": "test_battle", "debug": true}
	)


# ── Factory helpers ───────────────────────────────────────────────────────────

static func _build_fake_world_state() -> WorldState:
	var ws := WorldState.new()
	ws.world_seed       = 12345
	ws.region_id        = "debug_region"

	# Player character.
	var player := _make_person("player_1", "Ser Debug",  "melee", "short_sword", "mail_hauberk")
	var ally   := _make_person("player_2", "Ally Axe",   "melee", "axe",         "gambeson")
	var bandit1 := _make_person("enemy_1", "Bandit Grim", "melee", "club",        "gambeson")
	var bandit2 := _make_person("enemy_2", "Bandit Scar", "melee", "dagger",      "leather_vest")
	var bandit3 := _make_person("enemy_3", "Bandit Rook", "melee", "club",        "gambeson")
	var bandit4 := _make_person("enemy_4", "Bandit Last", "melee", "dagger",      "gambeson")

	ws.player_character_id = "player_1"
	ws.characters["player_1"] = player
	ws.characters["player_2"] = ally
	ws.npc_pool["enemy_1"] = bandit1
	ws.npc_pool["enemy_2"] = bandit2
	ws.npc_pool["enemy_3"] = bandit3
	ws.npc_pool["enemy_4"] = bandit4

	return ws


static func _make_person(
		pid: String, pname: String,
		_archetype: String, weapon_id: String, armor_id: String) -> PersonState:
	var p := PersonState.new()
	p.person_id       = pid
	p.name            = pname
	p.stamina         = 1.0
	p.equipment_refs  = {"main_hand": weapon_id, "torso": armor_id}
	return p


static func _build_test_battle(world_state: WorldState) -> BattleState:
	var b := BattleState.new()
	b.battle_id   = "test_battle"
	b.map_type    = "subregion"
	b.map_tile    = ""
	b.phase       = BattleState.PHASE_PLANNING
	b.turn        = 0

	# ── Player combatants ──────────────────────────────────────────────────
	var cp1 := CombatantState.from_person(
		world_state.characters["player_1"], "player", "p_alpha")
	cp1.tile_pos    = Vector2i(5, 10)
	cp1.melee_skill = 3
	cp1.agility     = 2

	var cp2 := CombatantState.from_person(
		world_state.characters["player_2"], "player", "p_alpha")
	cp2.tile_pos    = Vector2i(6, 10)
	cp2.melee_skill = 2

	# ── Enemy combatants ───────────────────────────────────────────────────
	var ce1 := CombatantState.from_person(
		world_state.npc_pool["enemy_1"], "enemy", "e_alpha")
	ce1.tile_pos = Vector2i(5, 20)

	var ce2 := CombatantState.from_person(
		world_state.npc_pool["enemy_2"], "enemy", "e_alpha")
	ce2.tile_pos = Vector2i(6, 20)

	var ce3 := CombatantState.from_person(
		world_state.npc_pool["enemy_3"], "enemy", "e_beta")
	ce3.tile_pos = Vector2i(14, 20)

	var ce4 := CombatantState.from_person(
		world_state.npc_pool["enemy_4"], "enemy", "e_beta")
	ce4.tile_pos = Vector2i(15, 20)

	for c: CombatantState in [cp1, cp2, ce1, ce2, ce3, ce4]:
		b.combatants[c.combatant_id] = c

	# ── Formations ────────────────────────────────────────────────────────
	var fp1 := FormationState.make(
		"p_alpha", "player", "Retinue",
		[cp1.combatant_id, cp2.combatant_id], Vector2i(5, 10))
	fp1.order = FormationState.ORDER_ADVANCE

	var fe1 := FormationState.make(
		"e_alpha", "enemy", "Bandit Vanguard",
		[ce1.combatant_id, ce2.combatant_id], Vector2i(5, 20))
	fe1.order = FormationState.ORDER_ADVANCE

	var fe2 := FormationState.make(
		"e_beta", "enemy", "Bandit Flank",
		[ce3.combatant_id, ce4.combatant_id], Vector2i(14, 20))
	fe2.order = FormationState.ORDER_ADVANCE

	for f: FormationState in [fp1, fe1, fe2]:
		b.formations[f.formation_id] = f

	# Flat 40×40 map_data (empty — CombatResolver will check tile_pos bounds).
	b.map_tile = "debug_flat"
	return b
