## CombatAI — selects WEGO orders for all enemy formations each planning phase.
##
## Called once per turn before CombatResolver.resolve_turn().
## Rules (evaluated in priority order for each enemy formation):
##   1. If morale < ROUT_THRESHOLD → retreat (handled by FormationState itself).
##   2. If formation is destroyed → skip.
##   3. If nearest player formation is within charge range (≤ 2 tiles) → charge.
##   4. If within reach but not adjacent → advance.
##   5. Otherwise → advance toward nearest player formation.
##
## AI never stalls, never loops, always produces a valid order.
class_name CombatAI
extends RefCounted

const CHARGE_RANGE: int = 2   # Chebyshev tiles to trigger a charge order.


## Assign orders to all enemy formations in the battle.
## Call this at the start of each WEGO planning phase before resolve_turn().
static func assign_enemy_orders(battle: BattleState) -> void:
	for fid: String in battle.formations:
		var f: FormationState = battle.formations[fid]
		if f.team_id != "enemy":
			continue
		if f.is_destroyed(battle.combatants):
			continue
		# Morale rout is set automatically by FormationState.on_member_killed/shocked.
		if f.order == FormationState.ORDER_RETREAT:
			continue  # already routing; don't override

		var nearest_player: FormationState = _nearest_player_formation(f, battle)
		if nearest_player == null:
			f.order = FormationState.ORDER_HOLD
			continue

		var dist: int = _formation_distance(f, nearest_player)

		if dist <= CHARGE_RANGE:
			f.order               = FormationState.ORDER_CHARGE
			f.target_formation_id = nearest_player.formation_id
			f.target_pos          = nearest_player.anchor_pos
		else:
			f.order               = FormationState.ORDER_ADVANCE
			f.target_formation_id = nearest_player.formation_id
			f.target_pos          = nearest_player.anchor_pos


# ── Helpers ───────────────────────────────────────────────────────────────────

static func _nearest_player_formation(f: FormationState, battle: BattleState) -> FormationState:
	var best_dist: int = 999999
	var best: FormationState = null
	for fid: String in battle.formations:
		var pf: FormationState = battle.formations[fid]
		if pf.team_id != "player":
			continue
		if pf.is_destroyed(battle.combatants):
			continue
		var d: int = _formation_distance(f, pf)
		if d < best_dist:
			best_dist = d
			best      = pf
	return best


static func _formation_distance(a: FormationState, b: FormationState) -> int:
	return maxi(absi(a.anchor_pos.x - b.anchor_pos.x),
				absi(a.anchor_pos.y - b.anchor_pos.y))
