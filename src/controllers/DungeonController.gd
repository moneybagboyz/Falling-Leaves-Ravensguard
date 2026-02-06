extends Node

var active = false
var grid = [] # 2D array of chars
var width = 40
var height = 40
var player_pos = Vector2i.ZERO
var enemies = [] # Array of {pos, type, hp, hp_max, equipment, body}
var items = [] # Array of {pos, item_data}
var stairs_down = Vector2i(-1, -1)
var stairs_up = Vector2i(-1, -1)
var current_floor = 1
var dungeon_name = ""
var dungeon_danger = 1
var dungeon_loot = 1
var fog_of_war = [] # 2D array of bools
var messages = []

func add_msg(msg: String):
	messages.append(msg)
	if messages.size() > 200:
		messages.pop_front()
	GameState.emit_signal("map_updated")

var floor_data = {} # floor_num -> {grid, fog, enemies, items, stairs_up, stairs_down}
var ruin_type = "Vault"
var ruin_pos = Vector2i(-1, -1)

# Vault Templates
var VAULTS = [
	{
		"name": "Throne Room",
		"tags": ["temple", "keep"],
		"width": 9, "height": 7,
		"layout": [
			"#########",
			"#.......#",
			"#.P.T.P.#",
			"#.......#",
			"#.P.P.P.#",
			"#.......#",
			"#########"
		],
		"legend": {"P": "#", "T": ">"} # P used as pillar, T for stairs/throne
	},
	{
		"name": "The Cross",
		"tags": ["keep", "vault"],
		"width": 7, "height": 7,
		"layout": [
			"###...###",
			"###...###",
			".........",
			".........",
			".........",
			"###...###",
			"###...###"
		]
	},
	{
		"name": "Circular Pit",
		"tags": ["crypt", "temple"],
		"width": 8, "height": 8,
		"layout": [
			"  ####  ",
			" ###### ",
			"########",
			"########",
			"########",
			"########",
			" ###### ",
			"  ####  "
		]
	},
	{
		"name": "Ancient Library",
		"tags": ["vault", "temple"],
		"width": 11, "height": 7,
		"layout": [
			"###########",
			"#B.B.B.B.B#",
			"#.........#",
			"#.........#",
			"#.........#",
			"#B.B.B.B.B#",
			"###########"
		],
		"legend": {"B": "#"} # B for Bookshelves
	},
	{
		"name": "Pillar Grove",
		"tags": ["temple", "keep"],
		"width": 10, "height": 6,
		"layout": [
			"##########",
			"#.P....P.#",
			"#........#",
			"#........#",
			"#.P....P.#",
			"##########"
		],
		"legend": {"P": "#"}
	},
	{
		"name": "The Arena",
		"tags": ["vault", "keep", "temple"],
		"width": 12, "height": 10,
		"layout": [
			"############",
			"#..........#",
			"#..######..#",
			"#..#....#..#",
			"#..#....#..#",
			"#..#....#..#",
			"#..#....#..#",
			"#..######..#",
			"#..........#",
			"############"
		]
	},
	{
		"name": "Small Alcoves",
		"tags": ["crypt", "vault"],
		"width": 7, "height": 7,
		"layout": [
			"#######",
			"#.#.#.#",
			"#.....#",
			"###.###",
			"#.....#",
			"#.#.#.#",
			"#######"
		]
	}
]

# Targeting Mode
var targeting_mode = false
var targeting_target = null
var targeting_parts = []
var targeting_index = 0
var targeting_attack_index = 0
var log_offset = 0

func log_message(msg):
	messages.push_back(msg)
	GameState.emit_signal("map_updated")

func start(ruin_data, pos):
	active = true
	ruin_pos = pos
	
	dungeon_name = ruin_data["name"]
	dungeon_danger = ruin_data["danger"]
	dungeon_loot = ruin_data["loot_quality"]
	
	# Determine Ruin Type
	ruin_type = "Vault"
	for t in ["Crypt", "Temple", "Keep", "Vault"]:
		if t in dungeon_name:
			ruin_type = t
			break

	# Load persistent data from GameState if it exists
	if ruin_data.has("floor_data"):
		floor_data = ruin_data["floor_data"]
		current_floor = 1
		if not load_floor(1):
			generate_floor()
		else:
			player_pos = stairs_up
			update_fog()
	else:
		floor_data = {}
		ruin_data["floor_data"] = floor_data
		current_floor = 1
		generate_floor()
		
	add_msg("Entered %s (%s)." % [dungeon_name, ruin_type])

func generate_floor():
	grid = []
	fog_of_war = []
	for y in range(height):
		var row = []
		var fog_row = []
		for x in range(width):
			row.append('#')
			fog_row.append(false)
		grid.append(row)
		fog_of_war.append(fog_row)
	
	enemies.clear()
	items.clear()
	
	# Flavor-based probabilities
	var cave_chance = 0.35
	var vault_chance = 0.15
	if ruin_type == "Crypt":
		cave_chance = 0.50
		vault_chance = 0.10
	elif ruin_type == "Temple":
		cave_chance = 0.10
		vault_chance = 0.30
	elif ruin_type == "Keep":
		cave_chance = 0.05
		vault_chance = 0.25

	# 1. BSP Selection
	var leaves = []
	split_area(Rect2i(1, 1, width - 2, height - 2), 8, leaves)
	
	var room_rects = []
	for leaf in leaves:
		var roll = GameState.rng.randf()
		if roll < vault_chance:
			# Vault - Filter by tag
			var relevant_vaults = []
			for v in VAULTS:
				if ruin_type.to_lower() in v["tags"]:
					relevant_vaults.append(v)
			
			if relevant_vaults.is_empty(): relevant_vaults = VAULTS
			
			var vault = relevant_vaults[GameState.rng.randi() % relevant_vaults.size()]
			if leaf.size.x >= vault.width and leaf.size.y >= vault.height:
				var vx = leaf.position.x + (leaf.size.x - vault.width) / 2
				var vy = leaf.position.y + (leaf.size.y - vault.height) / 2
				stamp_vault(vault, Vector2i(vx, vy))
				room_rects.append(Rect2i(vx, vy, vault.width, vault.height))
		elif roll < vault_chance + cave_chance:
			# Cave (Cellular Automata)
			generate_cave(leaf)
			room_rects.append(leaf)
		else:
			# Standard Room
			var rw = GameState.rng.randi_range(4, leaf.size.x - 2)
			var rh = GameState.rng.randi_range(4, leaf.size.y - 2)
			var rx = leaf.position.x + GameState.rng.randi_range(1, leaf.size.x - rw - 1)
			var ry = leaf.position.y + GameState.rng.randi_range(1, leaf.size.y - rh - 1)
			var r = Rect2i(rx, ry, rw, rh)
			for y in range(ry, ry + rh):
				for x in range(rx, rx + rw):
					grid[y][x] = '.'
			room_rects.append(r)

	# 2. Connect Rooms (A-B sequence ensuring full connectivity)
	for i in range(room_rects.size() - 1):
		var p1 = room_rects[i].get_center()
		var p2 = room_rects[i+1].get_center()
		create_tunnel(p1.x, p1.y, p2.x, p2.y)

	# Stairs
	stairs_up = room_rects[0].get_center()
	stairs_down = room_rects[room_rects.size()-1].get_center()
	grid[stairs_up.y][stairs_up.x] = '<'
	grid[stairs_down.y][stairs_down.x] = '>'
	
	player_pos = stairs_up
	update_fog()
	
	# Spawn Enemies in rooms
	for i in range(1, room_rects.size()):
		var r = room_rects[i]
		var enemy_count = GameState.rng.randi_range(0, 2 + int(dungeon_danger/2.5))
		for j in range(enemy_count):
			var ex = GameState.rng.randi_range(r.position.x, r.position.x + r.size.x - 1)
			var ey = GameState.rng.randi_range(r.position.y, r.position.y + r.size.y - 1)
			if grid[ey][ex] == '.':
				spawn_enemy(Vector2i(ex, ey))

	# Spawn Loot
	for i in range(1, room_rects.size()):
		var r = room_rects[i]
		if GameState.rng.randf() < 0.3:
			var ix = GameState.rng.randi_range(r.position.x, r.position.x + r.size.x - 1)
			var iy = GameState.rng.randi_range(r.position.y, r.position.y + r.size.y - 1)
			if grid[iy][ix] == '.':
				spawn_loot(Vector2i(ix, iy))

func split_area(rect: Rect2i, min_size: int, list: Array):
	if rect.size.x < min_size * 2 and rect.size.y < min_size * 2:
		list.append(rect)
		return
	
	var split_horizontal = GameState.rng.randf() > 0.5
	if rect.size.x > rect.size.y * 1.5: split_horizontal = false
	elif rect.size.y > rect.size.x * 1.5: split_horizontal = true
	
	if split_horizontal:
		var split = GameState.rng.randi_range(min_size, rect.size.y - min_size)
		split_area(Rect2i(rect.position.x, rect.position.y, rect.size.x, split), min_size, list)
		split_area(Rect2i(rect.position.x, rect.position.y + split, rect.size.x, rect.size.y - split), min_size, list)
	else:
		var split = GameState.rng.randi_range(min_size, rect.size.x - min_size)
		split_area(Rect2i(rect.position.x, rect.position.y, split, rect.size.y), min_size, list)
		split_area(Rect2i(rect.position.x + split, rect.position.y, rect.size.x - split, rect.size.y), min_size, list)

func generate_cave(rect: Rect2i):
	var local = []
	for y in range(rect.size.y):
		var row = []
		for x in range(rect.size.x):
			row.append('#' if GameState.rng.randf() < 0.45 else '.')
		local.append(row)
	
	for i in range(3):
		var next = local.duplicate(true)
		for y in range(1, rect.size.y - 1):
			for x in range(1, rect.size.x - 1):
				var n = 0
				for dy in range(-1, 2):
					for dx in range(-1, 2):
						if dx == 0 and dy == 0: continue
						if local[y+dy][x+dx] == '#': n += 1
				
				if local[y][x] == '#':
					next[y][x] = '#' if n >= 3 else '.'
				else:
					next[y][x] = '#' if n >= 5 else '.'
		local = next
	
	for y in range(rect.size.y):
		for x in range(rect.size.x):
			grid[rect.position.y + y][rect.position.x + x] = local[y][x]

func stamp_vault(vault, pos: Vector2i):
	for y in range(vault.height):
		var line = vault.layout[y]
		for x in range(vault.width):
			var glyph = line[x]
			var final_char = '.' if glyph == '.' else glyph
			if vault.has("legend") and vault.legend.has(glyph):
				final_char = vault.legend[glyph]
			
			if glyph != ' ': # Allow empty space in layout to preserve background
				grid[pos.y + y][pos.x + x] = final_char

func create_tunnel(x1, y1, x2, y2):
	var x = x1
	var y = y1
	while x != x2:
		grid[y][x] = '.'
		x += 1 if x2 > x else -1
	while y != y2:
		grid[y][x] = '.'
		y += 1 if y2 > y else -1

func spawn_enemy(pos):
	var hp_scale = 0.5 + (dungeon_danger * 0.1)
	
	var possible_types = ["rat"]
	
	match ruin_type:
		"Crypt":
			possible_types = ["skeleton", "zombie", "rat", "spider"]
			if current_floor >= 2: possible_types.append("draugr")
			if current_floor >= 3: possible_types.append("wraith")
			if current_floor >= 4: possible_types.append("lich")
		"Vault":
			possible_types = ["rat", "spider", "goblin", "imp"]
			if current_floor >= 2: possible_types.append("corrupted_guard")
			if current_floor >= 3: possible_types.append("troll")
			if current_floor >= 4: possible_types.append("centurion")
		"Temple":
			possible_types = ["imp", "spider", "skeleton", "wraith"]
			if current_floor >= 2: possible_types.append("hagraven")
			if current_floor >= 3: possible_types.append("daedra")
			if current_floor >= 4: possible_types.append("lich")
		"Keep":
			possible_types = ["goblin", "orc", "corrupted_guard"]
			if current_floor >= 3: possible_types.append("troll")
			if current_floor >= 4: possible_types.append("daedra")
			if current_floor >= 5: possible_types.append("centurion")
		_:
			possible_types = ["rat", "skeleton", "zombie", "spider", "goblin", "imp"]
		
	var type = possible_types[GameState.rng.randi() % possible_types.size()]
	
	var e = GameData.generate_monster(GameState.rng, type, hp_scale)
	e.pos = pos
	enemies.append(e)

func spawn_loot(pos):
	if GameState.rng.randf() < 0.4:
		# Spawn Crowns instead of equipment
		var amount = GameState.rng.randi_range(10, 50) * dungeon_danger
		items.append({
			"pos": pos,
			"is_crowns": true,
			"amount": amount
		})
		return

	var types = GameData.ITEMS.keys()
	var all_mats = GameData.MATERIALS.keys()
	var mats = []
	for m in all_mats:
		if m in ["flesh", "bone", "chitin", "tin", "lead"]: continue
		mats.append(m)
		
	var type = types[GameState.rng.randi() % types.size()]
	var mat = mats[GameState.rng.randi() % mats.size()]
	
	var item_base = GameData.ITEMS[type]
	
	# Filter invalid combos
	if item_base.get("type") == "armor":
		var layer = item_base.get("layer", "")
		if layer == "under" and mat not in ["linen", "wool", "silk", "leather"]:
			mat = "wool"
		elif layer == "armor" and mat in ["linen", "wool", "silk"]:
			mat = "iron"
		elif layer == "cover" and mat not in ["cloth", "linen", "wool", "silk", "leather"]:
			mat = "wool"
	elif item_base.get("type") == "weapon":
		if type in ["shortbow", "longbow", "crossbow"]:
			mat = "wood"
		elif mat in ["cloth", "linen", "wool", "silk"]:
			mat = "iron"
	elif item_base.get("type") == "shield":
		if mat in ["cloth", "linen", "wool", "silk"]:
			mat = "wood"
		
	var qual = "standard"
	if dungeon_loot >= 4: qual = "fine"
	
	items.append({
		"pos": pos,
		"data": GameState.create_item(type, mat, qual)
	})

func handle_input(event):
	if not active: return
	if not event is InputEventKey or not event.pressed: return
	
	if targeting_mode:
		handle_targeting_input(event)
		return

	if event.keycode == KEY_PAGEUP:
		log_offset += 5
		GameState.emit_signal("map_updated")
		return
	elif event.keycode == KEY_PAGEDOWN:
		log_offset = max(0, log_offset - 5)
		GameState.emit_signal("map_updated")
		return

	var move = Vector2i.ZERO
	var acted = false
	
	match event.keycode:
		KEY_W: move.y = -1
		KEY_S: move.y = 1
		KEY_A: move.x = -1
		KEY_D: move.x = 1
		KEY_SPACE:
			if perform_player_attack():
				acted = true
		KEY_PERIOD: acted = true
		KEY_COMMA:
			try_pickup()
			acted = true
		KEY_ESCAPE:
			if current_floor == 1:
				exit_dungeon()
				return

	if move != Vector2i.ZERO:
		var u_range = get_unit_range(GameState.player.commander)
		var found_enemy = null
		
		# Check for enemy in that direction within range
		for i in range(1, int(ceil(u_range)) + 1):
			var check_pos = player_pos + (move * i)
			if player_pos.distance_to(check_pos) > u_range: break
			
			var enemy = get_enemy_at(check_pos)
			if enemy:
				found_enemy = enemy
				break
			
			if not is_walkable(check_pos): break # Blocked by wall
			
		if found_enemy:
			enter_targeting_mode(found_enemy)
			return
		
		var target = player_pos + move
		if is_walkable(target):
			player_pos = target
			acted = true
			if player_pos == stairs_down:
				descend()
			elif player_pos == stairs_up and current_floor > 1:
				ascend()
			elif player_pos == stairs_up and current_floor == 1:
				exit_dungeon()
	
	if acted:
		log_offset = 0
		update_fog()
		process_enemy_turns()
		GameState.emit_signal("map_updated")

func perform_player_attack() -> bool:
	var u_range = get_unit_range(GameState.player.commander)
	var closest = null
	var min_dist = 9999
	
	for e in enemies:
		if e.hp <= 0 or e.status["is_downed"] or e.status["is_dead"]: continue
		var d = player_pos.distance_to(e.pos)
		if d <= u_range and d < min_dist:
			min_dist = d
			closest = e
			
	if closest:
		enter_targeting_mode(closest)
		return false # Don't consume turn yet, wait for part selection
	
	add_msg("No enemy in range!")
	return false

func enter_targeting_mode(enemy):
	targeting_mode = true
	targeting_target = enemy
	targeting_parts = []
	for k in enemy["body"].keys():
		var part = enemy["body"][k]
		# Allow major exterior parts: Top-level (Head, Torso) or direct attachments (Limbs, Hands, Feet)
		if not part.get("internal", false):
			var p = part.get("parent", "")
			if not p or p == "torso" or p.ends_with("_arm") or p.ends_with("_leg"):
				targeting_parts.append(k)
	targeting_index = 0
	targeting_attack_index = 0
	add_msg("Targeting %s. W/S: Part, A/D: Attack, SPACE: Strike, ESC: Cancel." % enemy["type"])
	GameState.emit_signal("map_updated")

func handle_targeting_input(event):
	if event.keycode == KEY_ESCAPE:
		targeting_mode = false
		add_msg("Cancelled attack.")
		GameState.emit_signal("map_updated")
	elif event.keycode == KEY_W:
		targeting_index = posmod(targeting_index - 1, targeting_parts.size())
		GameState.emit_signal("map_updated")
	elif event.keycode == KEY_S:
		targeting_index = posmod(targeting_index + 1, targeting_parts.size())
		GameState.emit_signal("map_updated")
	elif event.keycode == KEY_A:
		var wpn = GameState.player.commander.equipment.main_hand
		var attacks = wpn.get("attacks", []) if wpn else []
		if attacks.size() > 0:
			targeting_attack_index = posmod(targeting_attack_index - 1, attacks.size())
			GameState.emit_signal("map_updated")
	elif event.keycode == KEY_D:
		var wpn = GameState.player.commander.equipment.main_hand
		var attacks = wpn.get("attacks", []) if wpn else []
		if attacks.size() > 0:
			targeting_attack_index = posmod(targeting_attack_index + 1, attacks.size())
			GameState.emit_signal("map_updated")
	elif event.keycode == KEY_SPACE:
		var part_key = targeting_parts[targeting_index]
		perform_targeted_attack(targeting_target, part_key, targeting_attack_index)
		targeting_mode = false
		
		# After attack, enemies get their turn
		update_fog()
		process_enemy_turns()
		GameState.emit_signal("map_updated")

func perform_targeted_attack(enemy, part_key, attack_idx = 0):
	resolve_complex_damage(GameState.player.commander, enemy, part_key, attack_idx)

func is_walkable(pos):
	if pos.x < 0 or pos.x >= width or pos.y < 0 or pos.y >= height: return false
	return grid[pos.y][pos.x] != '#'

func get_enemy_at(pos):
	for e in enemies:
		if e.pos == pos and e.hp > 0 and not e.status["is_downed"] and not e.status["is_dead"]: return e
	return null

func get_unit_range(u):
	var wpn = u.equipment["main_hand"]
	if wpn:
		return float(wpn.get("range", 1.5))
	return 1.5

func update_fog():
	var radius = 6
	for dy in range(-radius, radius + 1):
		for dx in range(-radius, radius + 1):
			var p = player_pos + Vector2i(dx, dy)
			if p.x >= 0 and p.x < width and p.y >= 0 and p.y < height:
				if player_pos.distance_to(p) <= radius:
					fog_of_war[p.y][p.x] = true

func resolve_complex_damage(attacker, defender, forced_part = "", attack_idx = 0):
	var res = GameData.resolve_attack(attacker, defender, GameState.rng, forced_part, attack_idx)
	
	if not res["hit"]:
		if attacker == GameState.player.commander:
			add_msg("[color=gray]You swing at the %s but miss![/color]" % defender.type)
		else:
			add_msg("[color=gray]The %s swings at you but misses![/color]" % attacker.type)
		return

	if res["blocked"]:
		if defender == GameState.player.commander:
			add_msg("[color=yellow]You block the %s's attack with your %s![/color]" % [attacker.type, res["shield_name"]])
		else:
			add_msg("[color=yellow]The %s blocks your attack with their %s![/color]" % [defender.type, res["shield_name"]])
		return

	# 1. Main Hit Log
	var armor_str = " (%s)" % ", ".join(res["armor_layers"]) if res["armor_layers"].size() > 0 else ""
	var impact_desc = ""
	if res["tissues_hit"].has("bone"):
		impact_desc = " fracturing the bone" if res["final_dmg"] < 20 else " shattering the bone"
	elif res["tissues_hit"].has("organ"):
		impact_desc = " rupturing internal organs"
	elif res["tissues_hit"].has("muscle"):
		impact_desc = " tearing the muscle"
	elif res["tissues_hit"].has("skin"):
		impact_desc = " cutting the skin"

	var wpn = attacker.equipment.get("main_hand")
	var wpn_name = wpn.get("name", "fists") if wpn else "fists"

	if attacker == GameState.player.commander:
		var log_line = "[color=cyan]Your %s[/color] %s the %s's %s" % [wpn_name, res["verb"], defender.type, res["part_hit"]]
		if impact_desc != "": log_line += "," + impact_desc + "!"
		else: log_line += "."
		log_line += " [color=gray](%d dmg%s)[/color]" % [res["final_dmg"], armor_str]
		add_msg(log_line)
	else:
		var log_line = "[color=red]The %s's %s[/color] %s your %s" % [attacker.type, wpn_name, res["verb"], res["part_hit"]]
		if impact_desc != "": log_line += "," + impact_desc + "!"
		else: log_line += "."
		log_line += " [color=gray](%d dmg%s)[/color]" % [res["final_dmg"], armor_str]
		add_msg(log_line)

	# 2. Log Critical Events
	var target_name = "your" if defender == GameState.player.commander else defender.type + "'s"
	for event in res["critical_events"]:
		var msg = ""
		match event:
			"artery_severed":
				msg = "[color=red]  [CRITICAL] An artery in %s %s has been severed![/color]" % [target_name, res["part_hit"]]
			"vein_opened":
				msg = "[color=orange]  A major vein in %s %s has been opened![/color]" % [target_name, res["part_hit"]]
			"tendon_snapped":
				msg = "[color=red]  [CRITICAL] %s tendon in the %s has snapped![/color]" % [target_name.capitalize(), res["part_hit"]]
			"nerve_destroyed":
				msg = "[color=red]  [CRITICAL] %s nerve in the %s has been destroyed![/color]" % [target_name.capitalize(), res["part_hit"]]
			"bone_fractured":
				msg = "[color=red]  [CRITICAL] %s bone in the %s is fractured![/color]" % [target_name.capitalize(), res["part_hit"]]
			"decapitated":
				msg = "[color=red]  [FATAL] %s has been decapitated![/color]" % target_name.capitalize()
			"part_destroyed":
				msg = "[color=red]  [FATAL] %s %s has been completely destroyed![/color]" % [target_name.capitalize(), res["part_hit"]]
		
		if event.begins_with("organ_failure:"):
			var organ_name = event.split(":")[1]
			msg = "[color=red]  [FATAL] %s %s has failed![/color]" % [target_name.capitalize(), organ_name]
			
		if msg != "":
			add_msg(msg)

	if res["downed_occurred"]:
		var subject = "You" if defender == GameState.player.commander else "The " + defender.type
		add_msg("  [color=orange]%s collapses from the pain![/color]" % subject)
	
	if res["prone_occurred"]:
		var subject = "You" if defender == GameState.player.commander else "The " + defender.type
		add_msg("  [color=orange]%s is knocked to the ground![/color]" % subject)

	for msg in GameData.check_functional_integrity(defender):
		add_msg("  " + msg)
	
	if defender.status["is_dead"]:
		var death_msg = "[b][color=red]  >>> The %s has been slain! <<<[/color][/b]" % defender.type
		if defender == GameState.player.commander:
			death_msg = "[b][color=red]  >>> YOU HAVE PERISHED! <<<[/color][/b]"
		add_msg(death_msg)
	elif defender.status["is_downed"]:
		if defender.hp <= 0 and not defender.status["is_dead"]:
			var unconscious_name = "You have" if defender == GameState.player.commander else defender.type + " has"
			add_msg("  [color=orange]%s been knocked unconscious![/color]" % unconscious_name)

func process_enemy_turns():
	# Process Bleeding for Player
	var cmd = GameState.player.commander
	var p_bleed = GameData.process_bleeding(cmd, 5.0, GameState.rng)
	if p_bleed["msg"] != "":
		add_msg(p_bleed["msg"])
		if p_bleed["died"]:
			exit_dungeon(true)
			return

	for e in enemies:
		if e.hp <= 0 or e.status["is_downed"] or e.status["is_dead"]: continue
		
		# Process Bleeding for Enemy
		var e_bleed = GameData.process_bleeding(e, 5.0, GameState.rng)
		if e_bleed["msg"] != "":
			add_msg(e_bleed["msg"])
			if e_bleed["died"]:
				continue

		var dist = e.pos.distance_to(player_pos)
		var u_range = get_unit_range(e)
		
		if dist <= u_range:
			enemy_attack(e)
		elif dist < 8:
			var dir = (player_pos - e.pos)
			var move = dir.sign()
			if abs(dir.x) > abs(dir.y): move.y = 0
			else: move.x = 0
			
			var target = e.pos + move
			if is_walkable(target) and not get_enemy_at(target) and target != player_pos:
				e.pos = target

func enemy_attack(e):
	var cmd = GameState.player.commander
	
	# Enemy chooses a random attack
	var wpn = e.equipment["main_hand"]
	var attacks = wpn.get("attacks", []) if wpn else []
	var attack_idx = GameState.rng.randi() % attacks.size() if attacks.size() > 0 else 0
	
	resolve_complex_damage(e, cmd, "", attack_idx)
	
	if cmd.hp <= 0:
		add_msg("You have been defeated in the depths...")
		exit_dungeon(true)

func try_pickup():
	for i in range(items.size() - 1, -1, -1):
		var it = items[i]
		if it["pos"] == player_pos:
			if it.get("is_crowns", false):
				GameState.player.crowns += it["amount"]
				add_msg("Found [color=yellow]%d Crowns[/color] in a dusty chest!" % it["amount"])
			else:
				GameState.player.stash.append(it["data"])
				add_msg("Picked up %s." % it["data"]["name"])
			items.remove_at(i)
			return
	add_msg("Nothing here to pick up.")

func save_current_floor():
	var data = {
		"grid": grid.duplicate(true),
		"fog": fog_of_war.duplicate(true),
		"enemies": enemies.duplicate(true),
		"items": items.duplicate(true),
		"stairs_up": stairs_up,
		"stairs_down": stairs_down
	}
	floor_data[current_floor] = data

func load_floor(floor_num):
	if not floor_data.has(floor_num):
		return false
		
	var data = floor_data[floor_num]
	grid = data.grid.duplicate(true)
	fog_of_war = data.fog.duplicate(true)
	enemies = data.enemies.duplicate(true)
	items = data.items.duplicate(true)
	stairs_up = data.stairs_up
	stairs_down = data.stairs_down
	current_floor = floor_num
	return true

func descend():
	save_current_floor()
	current_floor += 1
	dungeon_danger += 1
	
	if not load_floor(current_floor):
		generate_floor()
	else:
		player_pos = stairs_up
		update_fog()
		
	add_msg("Descending to floor %d..." % current_floor)

func ascend():
	save_current_floor()
	current_floor -= 1
	dungeon_danger -= 1
	
	if not load_floor(current_floor):
		generate_floor()
	else:
		player_pos = stairs_down
		update_fog()
		
	add_msg("Ascending to floor %d..." % current_floor)

func exit_dungeon(defeated = false):
	save_current_floor()
	active = false
	if defeated:
		add_msg("[color=red]You were dragged out of the dungeon, barely alive. You lost some crowns and provisions.[/color]")
		GameState.player.crowns = int(GameState.player.crowns * 0.7)
		GameState.player.provisions = int(GameState.player.provisions * 0.5)
		
		var cmd = GameState.player.commander
		cmd.status["is_downed"] = false
		cmd.blood_current = cmd.blood_max * 0.4 # Still weak
		cmd.bleed_rate = 0.0
		
		# Ensure at least 1 HP in all tissues to prevent immediate death
		for p in cmd.body:
			for tissue in cmd.body[p]["tissues"]:
				tissue["hp"] = max(1, tissue["hp"])
		cmd.hp = GameData.get_total_hp(cmd.body)
	
	GameState.dungeon_ended.emit()

func get_tile_at(pos):
	if not fog_of_war[pos.y][pos.x]: return ' '
	return grid[pos.y][pos.x]

