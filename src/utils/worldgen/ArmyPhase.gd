class_name ArmyPhase
extends WorldGenPhase

## Handles army, caravan, and ruin spawning

const GDArmy = preload("res://src/data/GDArmy.gd")
const GDCaravan = preload("res://src/data/GDCaravan.gd")
const GDNPC = preload("res://src/data/GDNPC.gd")
const GameData = preload("res://src/core/GameData.gd")

func get_phase_name() -> String:
	return "Armies"

func execute(context: WorldGenContext) -> bool:
	var w = context.width
	var h = context.height
	var rng = context.rng
	var world_grid = context.world_grid
	var province_grid_arr = context.province_grid.to_legacy_grid()
	var mainland_set = {}  # Could retrieve from earlier phase, but let's just skip for now
	
	step_completed.emit("RAISING ARMIES...")
	
	# Quick mainland check
	for y in range(h):
		for x in range(w):
			if world_grid[y][x] != '~':
				mainland_set[Vector2i(x, y)] = true
	
	# Spawn bandits
	var b_count = 0
	for attempt in range(1000):
		if b_count >= 35: break
		var bpos = Vector2i(rng.randi_range(0, w - 1), rng.randi_range(0, h - 1))
		if mainland_set.has(bpos) and world_grid[bpos.y][bpos.x] in ['.', '#', '"', '*', '&']:
			var bandit = GDArmy.new(bpos, "bandits")
			bandit.name = "Bandit Ravagers"
			bandit.type = "bandit"
			for j in range(25): bandit.roster.append(GameData.generate_recruit(rng, 1))
			context.armies.append(bandit)
			b_count += 1
	
	# Spawn lords at provincial capitals
	for p_id in context.provinces:
		var p = context.provinces[p_id]
		if p.capital == null: continue
		
		var s_pos = p.capital
		var s = context.world_settlements[s_pos]
		
		if s.faction == "neutral" or s.faction == "bandits": continue
		
		var lord = GDArmy.new(s_pos, s.faction)
		lord.name = "Lord " + GameData.LAST_NAMES[rng.randi() % GameData.LAST_NAMES.size()]
		lord.type = "lord"
		lord.home_fief = s_pos
		lord.name += " of " + p.name
		
		var doctrines = ["defender", "conqueror", "raider"]
		lord.doctrine = doctrines[rng.randi() % doctrines.size()]
		var personalities = ["balanced", "aggressive", "cautious"]
		lord.personality = personalities[rng.randi() % personalities.size()]
		
		var lord_npc_id = "npc_lord_" + str(rng.randi())
		var npc_obj = GDNPC.new(lord_npc_id, lord.name, "Lord", s_pos, s.faction)
		npc_obj.crowns = 1000
		s.npcs.append(npc_obj)
		s.lord_id = lord_npc_id
		lord.lord_id = lord_npc_id
		
		var r_count = 30 + clamp(int(s.population / 10), 0, 150)
		for j in range(r_count):
			lord.roster.append(GameData.generate_recruit(rng, clamp(s.tier, 1, 4)))
		
		context.armies.append(lord)
		
		# Provincial caravans
		var cav = GDCaravan.new(s_pos, s.faction)
		cav.origin = s_pos
		cav.crowns = 5000
		for j in range(10): cav.roster.append(GameData.generate_recruit(rng, 2))
		context.caravans.append(cav)
	
	# Spawn ruins
	var ruins = {}
	var ruin_types = ["Vault", "Crypt", "Temple", "Keep"]
	var target_ruins = 10 + (context.savagery * 3)
	var attempts = 0
	var land_tiles = ['.', '#', '"', '*', '&', 'o', '^', 'O']
	
	while ruins.size() < target_ruins and attempts < 1000:
		attempts += 1
		var rpos = Vector2i(rng.randi_range(0, w - 1), rng.randi_range(0, h - 1))
		var t = world_grid[rpos.y][rpos.x]
		if t in land_tiles:
			if not context.world_settlements.has(rpos):
				var neighbor_water = _check_terrain_near(rpos, world_grid, 1, ['~', '≈', '/', '\\'])
				if not neighbor_water or rng.randf() < 0.2:
					ruins[rpos] = {
						"name": "Old " + ruin_types[rng.randi() % ruin_types.size()],
						"type": "ruin",
						"explored": false,
						"danger": rng.randi_range(1, 5),
						"loot_quality": rng.randi_range(1, 5)
					}
	
	context.world_resources["_ruins"] = ruins
	
	return true

func _check_terrain_near(pos: Vector2i, grid: Array, r: int, chars: Array) -> bool:
	var h = grid.size()
	var w = grid[0].size()
	for dy in range(-r, r + 1):
		for dx in range(-r, r + 1):
			var p = pos + Vector2i(dx, dy)
			if p.x >= 0 and p.x < w and p.y >= 0 and p.y < h:
				if grid[p.y][p.x] in chars: return true
	return false

func cleanup(context: WorldGenContext) -> void:
	pass
