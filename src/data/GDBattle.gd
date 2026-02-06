extends Object
class_name GDBattle

var id: String
var pos: Vector2i
var attacker: GDEntity
var defender: GDEntity
var duration_days: int = 3
var days_left: int = 3
var is_finished: bool = false
var winner = null
var description: String = "A battle is raging."

func _init(_pos: Vector2i, _att: GDEntity, _def: GDEntity, _dur: int = 48): # 48 hours = 2 days
	id = "BTL_" + str(randi() % 10000)
	pos = _pos
	attacker = _att
	defender = _def
	duration_days = _dur
	days_left = _dur
	description = "%s is attacking %s!" % [attacker.name if "name" in attacker else attacker.type, defender.name if "name" in defender else defender.type]

func process_turn(gs):
	days_left -= 1
	
	# Damage calculation
	var att_power = attacker.strength
	var def_power = defender.strength
	
	var def_dmg = max(1, int(att_power * 0.02 * (randf() + 0.5)))
	var att_dmg = max(1, int(def_power * 0.01 * (randf() + 0.5))) # Defenders usually have advantage
	
	_apply_losses(defender, def_dmg)
	_apply_losses(attacker, att_dmg)
	
	if attacker.roster.is_empty() or defender.roster.is_empty() or days_left <= 0:
		is_finished = true
		_resolve_winner(gs)

func _apply_losses(entity, amount):
	for i in range(amount):
		if entity.roster.is_empty(): break
		entity.roster.remove_at(randi() % entity.roster.size())

func _resolve_winner(gs):
	if attacker.roster.is_empty() and defender.roster.is_empty():
		winner = null # Total mutual destruction?
	elif attacker.roster.is_empty():
		winner = defender
	elif defender.roster.is_empty():
		winner = attacker
	else:
		# Time ran out, winner is whoever has more strength
		winner = attacker if attacker.strength > defender.strength else defender

	attacker.is_in_battle = false
	defender.is_in_battle = false
	
	var win_name = winner.name if winner and "name" in winner else (winner.type if winner else "None")
	gs.add_log("[color=yellow]BATTLE ENDED:[/color] %s emerged victorious!" % win_name)
	
	# Cleanup losers
	var participants = [attacker, defender]
	for p in participants:
		if p != winner and p.roster.is_empty():
			if p is GDArmy:
				gs.erase_army(p)
			elif p is GDCaravan:
				gs.caravans.erase(p)
