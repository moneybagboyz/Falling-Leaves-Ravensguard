extends Node

const FaunaData = preload("res://src/data/FaunaData.gd")

func handle_input(event: InputEvent):
	var gs = GameState
	var _moved = false
	
	if event.is_action_pressed("ui_up"):
		gs.player.camera_offset.y -= 1
	elif event.is_action_pressed("ui_down"):
		gs.player.camera_offset.y += 1
	elif event.is_action_pressed("ui_left"):
		gs.player.camera_offset.x -= 1
	elif event.is_action_pressed("ui_right"):
		gs.player.camera_offset.x += 1
		
	var move_dir = Vector2i.ZERO
	if event is InputEventKey and event.pressed:
		match event.keycode:
			KEY_W, KEY_UP, KEY_KP_8:
				if gs.player.free_cam_mode: gs.player.camera_offset.y -= 1
				else: move_dir.y = -1
			KEY_S, KEY_DOWN, KEY_KP_2:
				if gs.player.free_cam_mode: gs.player.camera_offset.y += 1
				else: move_dir.y = 1
			KEY_A, KEY_LEFT, KEY_KP_4:
				if gs.player.free_cam_mode: gs.player.camera_offset.x -= 1
				else: move_dir.x = -1
			KEY_D, KEY_RIGHT, KEY_KP_6:
				if gs.player.free_cam_mode: gs.player.camera_offset.x += 1
				else: move_dir.x = 1
			KEY_V:
				gs.player.free_cam_mode = !gs.player.free_cam_mode
				if !gs.player.free_cam_mode:
					gs.player.camera_offset = Vector2i.ZERO
				gs.add_log("Free Camera: " + ("ENABLED" if gs.player.free_cam_mode else "DISABLED"))
				gs.emit_signal("map_updated")
			KEY_KP_ADD, KEY_EQUAL: # Zoom In
				gs.player.camera_zoom = clamp(gs.player.camera_zoom + 0.1, 0.5, 3.0)
				gs.emit_signal("map_updated")
			KEY_KP_SUBTRACT, KEY_MINUS: # Zoom Out
				gs.player.camera_zoom = clamp(gs.player.camera_zoom - 0.1, 0.5, 3.0)
				gs.emit_signal("map_updated")
			KEY_ENTER: try_interact()
			KEY_M: try_manage()
			KEY_I: try_party_info()
			KEY_F: try_fief_info()
			KEY_H: try_history()
			KEY_B: try_found_settlement()
			KEY_K: try_tournament()
			KEY_Z: try_rest()
			KEY_P: try_toggle_map_mode("political")
			KEY_L: try_toggle_map_mode("province")
			KEY_R: try_toggle_map_mode("resource")
			KEY_TAB: try_toggle_world_map()
			KEY_PERIOD: gs.advance_time()
			KEY_T: toggle_travel_mode()
			KEY_F1: 
				while GameState.travel_mode != GameState.TravelMode.FAST:
					toggle_travel_mode()
			KEY_F2:
				while GameState.travel_mode != GameState.TravelMode.REGION:
					toggle_travel_mode()
			KEY_F3:
				while GameState.travel_mode != GameState.TravelMode.LOCAL:
					toggle_travel_mode()
	
	if move_dir != Vector2i.ZERO:
		handle_movement_delta(move_dir)

func toggle_travel_mode():
	var gs = GameState
	var main = get_parent()
	var mode_name = ""
	
	if gs.travel_mode == GameState.TravelMode.FAST:
		gs.travel_mode = GameState.TravelMode.REGION
		main.state = "region"
		main.region_ctrl.activate(gs.player.pos)
		mode_name = "REGION"
		gs.add_log("Entered [color=cyan]Region View[/color]. Move with WASD. Press T to exit to Fast travel.")
	elif gs.travel_mode == GameState.TravelMode.REGION:
		gs.travel_mode = GameState.TravelMode.LOCAL
		mode_name = "LOCAL"
		
		if gs.settlements.has(gs.player.pos):
			main.state = "city"
			main.city_ctrl.activate(gs.settlements[gs.player.pos], gs.player.pos, gs.world_seed)
			gs.add_log("Entered [color=yellow]Local View[/color]. (" + gs.settlements[gs.player.pos].name + ")")
		else:
			main.state = "overworld" # Switch back to main render loop for LOCAL logic (Wilderness)
			gs.add_log("Entered [color=green]Local View[/color]. (Wilderness)")
	else:
		gs.travel_mode = GameState.TravelMode.FAST
		mode_name = "WORLD"
		main.state = "overworld" # Switch back to main render loop for FAST
		gs.add_log("Entered [color=white]Fast Travel Mode[/color].")
	gs.emit_signal("map_updated")

func handle_local_movement(move_dir: Vector2i):
	var gs = GameState
	var dist = gs.METERS_PER_LOCAL_TILE
	
	gs.local_offset += Vector2(move_dir) * dist
	gs.local_step_count += 1
	
	# Handle World Tile Transitions (Wrapping)
	if gs.local_offset.x < 0:
		var target = gs.player.pos + Vector2i(-1, 0)
		if gs.is_walkable(target):
			gs.player.pos = target
			gs.local_offset.x += gs.WORLD_TILE_SIZE
			gs.advance_time()
		else:
			gs.local_offset.x = 0
	elif gs.local_offset.x >= gs.WORLD_TILE_SIZE:
		var target = gs.player.pos + Vector2i(1, 0)
		if gs.is_walkable(target):
			gs.player.pos = target
			gs.local_offset.x -= gs.WORLD_TILE_SIZE
			gs.advance_time()
		else:
			gs.local_offset.x = gs.WORLD_TILE_SIZE - 0.1
			
	if gs.local_offset.y < 0:
		var target = gs.player.pos + Vector2i(0, -1)
		if gs.is_walkable(target):
			gs.player.pos = target
			gs.local_offset.y += gs.WORLD_TILE_SIZE
			gs.advance_time()
		else:
			gs.local_offset.y = 0
	elif gs.local_offset.y >= gs.WORLD_TILE_SIZE:
		var target = gs.player.pos + Vector2i(0, 1)
		if gs.is_walkable(target):
			gs.player.pos = target
			gs.local_offset.y -= gs.WORLD_TILE_SIZE
			gs.advance_time()
		else:
			gs.local_offset.y = gs.WORLD_TILE_SIZE - 0.1

	# Ambient Sensory Messages (Layer 2)
	if gs.local_step_count % 30 == 0:
		trigger_ambient_message()

	# PAGING: Check if we need to regenerate the local sector
	if gs.local_offset.distance_to(gs.last_gen_offset) > gs.PAGING_THRESHOLD:
		get_node("/root/Main/BattleController").generate_map()
	else:
		gs.emit_signal("map_updated") 
		
		# Every 50 local steps advances world time by 1 hour (Increased since map is larger)
		if gs.local_step_count >= 50:
			gs.local_step_count = 0
			gs.advance_time()
		else:
			gs.emit_signal("map_updated")

func trigger_ambient_message():
	var gs = GameState
	var pos = gs.player.pos
	var tile_type = gs.get_tile_type(pos)
	var geo = gs.geology.get(pos, {"temp": 0.5, "rain": 0.5, "elevation": 0.5})
	
	var possible_msgs = []
	match tile_type:
		"forest":
			possible_msgs = [
				"Leaves crunch beneath your boots.",
				"A distant bird cries out from the canopy.",
				"The air here is cool and smells of pine.",
				"Shadows dance between the thick trunks."
			]
		"desert":
			possible_msgs = [
				"The heat shimmers over the endless dunes.",
				"Wind whistles through a gap in the sandstone.",
				"Sweat stings your eyes as you trudge through the sand.",
				"A tumbleweed rolls lazily across your path."
			]
		"mountain", "peaks":
			possible_msgs = [
				"The thin air makes every breath a struggle.",
				"Loose gravel skitters down a nearby slope.",
				"Clouds drift lazily below the jagged peaks.",
				"An eagle soars high above the granite cliffs."
			]
		"tundra":
			possible_msgs = [
				"The freezing wind bites through your cloak.",
				"Ice-crusted moss crunches under your weight.",
				"A pale sun struggles to pierce the grey mist.",
				"Your breath comes in short, white puffs."
			]
		"jungle":
			possible_msgs = [
				"The humidity is suffocating, like a damp wool cloak.",
				"Large, colorful insects buzz around your head.",
				"The sound of unseen animals fills the heavy air.",
				"Vines hang like emerald snakes from the trees."
			]
		"hills", "hill":
			possible_msgs = [
				"The trail winds steeply up a grassy ridge.",
				"The view from the summit is breathtaking.",
				"The wind howls as it crests the rolling hills.",
				"Sheep-tracks crisscross the steep slopes.",
				"The rolling horizon stretches out before you.",
				"A gentle breeze ripples through the tall grass.",
				"Sheep graze peacefully on a distant slope.",
				"The path winds lazily through the folding terrain."
			]
		"water":
			possible_msgs = [
				"Spray from the waves cools your face.",
				"Gulls cry out as they wheel above the surf.",
				"The rhythm of the tide is a steady heartbeat.",
				"The salt air fills your lungs with every breath.",
				"The water laps gently against the shore.",
				"A cool breeze brings the scent of salt and spray.",
				"Waves crash rhythmically against the rocks.",
				"Mist rises from the surface of the dark depths."
			]
		"road":
			possible_msgs = [
				"The paved stones are worn smooth by years of travel.",
				"You spot the charred remains of an old campfire.",
				"A discarded wagon wheel lies rotting in the ditch.",
				"The road ahead stretches straight toward the horizon."
			]
		"plains":
			possible_msgs = [
				"Tall grass waves like an ocean in the breeze.",
				"The horizon seems to stretch on forever.",
				"A herd of animals grazes in the distance.",
				"The sun beats down on the open expanse.",
				"The open sky feels vast and unending.",
				"Wildflowers sway gently in the breeze.",
				"A cricket chirps somewhere in the dust.",
				"The sun beats down on the golden fields."
			]
		_:
			possible_msgs = [
				"The journey continues through quiet lands.",
				"You keep a steady pace along the trail.",
				"The landscape changes slowly with every mile."
			]
			
	if not possible_msgs.is_empty():
		var msg = possible_msgs[gs.rng.randi() % possible_msgs.size()]
		gs.add_log("[color=gray][i]" + msg + "[/i][/color]")

func get_animal_at(world_pos: Vector2i, alx: int, aly: int):
	var gs = GameState
	var tile_type = gs.get_tile_type(world_pos)
	var world_fauna = FaunaData.get_fauna_for_biome(tile_type)
	if world_fauna.is_empty(): return null
	
	var a_rng = RandomNumberGenerator.new()
	a_rng.seed = (world_pos.x * 12345) ^ (world_pos.y * 6789)
	
	var anchor_x = a_rng.randi_range(30, 170)
	var anchor_y = a_rng.randi_range(30, 170)
	
	# Early exit if we are nowhere near the possible cluster
	if abs(alx - anchor_x) > 20 or abs(aly - anchor_y) > 20: return null
	
	var total_chance = 0
	for a in world_fauna: total_chance += a.chance
	var roll = a_rng.randi_range(0, total_chance - 1)
	var cumulative = 0
	var herd_template = null
	for a in world_fauna:
		cumulative += a.chance
		if roll < cumulative:
			herd_template = a
			break
			
	if not herd_template: return null
	
	var herd_count = a_rng.randi_range(herd_template.herd_range[0], herd_template.herd_range[1])
	var killed_list = gs.killed_fauna.get(world_pos, [])
	
	for i in range(herd_count):
		var off_x = a_rng.randi_range(-8, 8)
		var off_y = a_rng.randi_range(-8, 8)
		var final_x = anchor_x + off_x
		var final_y = anchor_y + off_y
		
		if alx == final_x and aly == final_y:
			if Vector2i(final_x, final_y) in killed_list: return null
			
			var res = herd_template.duplicate()
			res["world_pos"] = world_pos
			res["local_pos"] = Vector2i(final_x, final_y)
			res["type"] = "animal"
			return res
			
	return null

func _handle_animal_interaction(data, _l_pos):
	var gs = GameState
	var opts = ["Hunt (Kill)", "Attempt Capture", "Leave"]
	gs.dialogue_started.emit(data, opts)


func handle_movement_delta(move_dir: Vector2i):
	var gs = GameState
	if gs.is_resting:
		gs.add_log("You must stop resting (Z) before you can move.")
		return
		
	if gs.travel_mode == GameState.TravelMode.LOCAL:
		handle_local_movement(move_dir)
		return

	var total_w = gs.get_total_weight()
	var max_w = gs.get_max_weight()
	if total_w > max_w * 2.0:
		gs.add_log("[color=red]CRITICAL: Overburdened! (%d/%d kg). You are too heavy to move. Discard items in Management (M).[/color]" % [int(total_w), int(max_w)])
		return
		
	var target = gs.player.pos + move_dir
	if gs.is_walkable(target):
		gs.player.pos = target
		gs.player.camera_offset = Vector2i.ZERO # Reset cam on move
		gs.advance_time()
		check_encounters()

func try_manage():
	var main = get_parent()
	if main.state == "overworld":
		main.toggle_management_ui()

func try_party_info():
	var main = get_parent()
	if main.state == "overworld":
		main.toggle_party_info()

func try_fief_info():
	var main = get_parent()
	if main.state == "overworld":
		main.toggle_fief_info()

func try_history():
	var main = get_parent()
	if main.state == "overworld":
		main.toggle_history()

func try_tournament():
	var main = get_parent()
	var gs = GameState
	if main.state == "overworld":
		var s = gs.settlements.get(gs.player.pos)
		if s and s.tournament_active:
			gs.player.tournament_round = 1
			main._on_dialogue_started(s, ["Enter Tournament", "Leave"])
		else:
			gs.add_log("There is no tournament currently active here.")

func try_rest():
	var gs = GameState
	gs.is_resting = !gs.is_resting
	if gs.is_resting:
		gs.add_log("The party makes camp. Healing is increased, but you cannot move.")
	else:
		gs.add_log("The party breaks camp and prepares to move.")
	gs.emit_signal("map_updated")

func try_toggle_map_mode(mode: String):
	var gs = GameState
	if gs.map_mode == mode:
		gs.map_mode = "terrain"
		gs.add_log("Strategic Overlay: [color=white]Terrain View enabled.[/color]")
	else:
		gs.map_mode = mode
		match mode:
			"political": gs.add_log("Strategic Overlay: [color=cyan]Kingdom Borders enabled (P).[/color]")
			"province": gs.add_log("Strategic Overlay: [color=cyan]Province Borders enabled (L).[/color]")
			"resource": gs.add_log("Strategic Overlay: [color=cyan]Resource Overlay enabled (R).[/color]")
	gs.emit_signal("map_updated")

func try_toggle_world_map():
	var main = get_parent()
	var gs = GameState
	if main.state == "overworld":
		main.state = "world_map"
		gs.add_log("Viewing World Map. Press TAB or ESC to return.")
	elif main.state == "world_map":
		main.state = "overworld"
	main._on_map_updated()

func try_found_settlement():
	var gs = GameState
	var pos = gs.player.pos
	
	if gs.player.founding_timer > 0:
		gs.add_log("You are already founding a settlement at %v (%d days left)." % [gs.player.founding_pos, gs.player.founding_timer])
		return
		
	if gs.player.charters <= 0:
		gs.add_log("You do not have a Royal Charter to found a new settlement! Speak to your King to request one.")
		return
		
	if gs.settlements.has(pos):
		gs.add_log("You cannot found a settlement on top of an existing one!")
		return
		
	# Check distance to other settlements
	for s_pos in gs.settlements:
		if pos.distance_to(s_pos) < Globals.PLAYER_FOUND_MIN_DIST:
			gs.add_log("The site is too close to %s! You must move further into the wilderness." % gs.settlements[s_pos].name)
			return

	if gs.player.crowns < Globals.PLAYER_FOUND_COST_CROWNS:
		gs.add_log("You need %d Crowns to fund the pioneers." % Globals.PLAYER_FOUND_COST_CROWNS)
		return
		
	if gs.player.inventory.get("grain", 0) < Globals.PLAYER_FOUND_COST_GRAIN:
		gs.add_log("You need %d Grain to feed the settlers during construction." % Globals.PLAYER_FOUND_COST_GRAIN)
		return

	# Site evaluation
	var score_data = SettlementManager.score_tile_for_hamlet(pos, pos, gs)
	if score_data["score"] < 10:
		gs.add_log("This site is too desolate for a settlement (Score: %d)." % score_data["score"])
		return

	# Begin founding
	gs.player.charters -= 1
	gs.player.crowns -= Globals.PLAYER_FOUND_COST_CROWNS
	gs.player.inventory["grain"] -= Globals.PLAYER_FOUND_COST_GRAIN
	gs.player.founding_timer = Globals.PLAYER_FOUND_BUILD_DAYS
	gs.player.founding_pos = pos
	gs.player.founding_type = score_data["type"]
	
	gs.add_log("[color=yellow]FOUNDATION: You have established a Pioneer Camp! In %d days, it will become a Hamlet.[/color]" % Globals.PLAYER_FOUND_BUILD_DAYS)
	gs.emit_signal("map_updated")

func try_interact():
	var gs = GameState
	var pos = gs.player.pos
	
	# Local Mode Interaction (Fauna)
	if gs.travel_mode == GameState.TravelMode.LOCAL:
		var battle_ctrl = get_node("/root/Main/BattleController")
		# In paging, player is always at center of current grid (250, 250 for 500x500)
		var plx = 250
		var ply = 250
		
		for dy in range(-1, 2):
			for dx in range(-1, 2):
				if dx == 0 and dy == 0: continue
				var alx = plx + dx
				var aly = ply + dy
				
				if alx >= 0 and alx < gs.LOCAL_GRID_W and aly >= 0 and aly < gs.LOCAL_GRID_H:
					var tile = battle_ctrl.grid[aly][alx]
					# Check if tile matches any fauna symbol
					var fauna_table = FaunaData.get_fauna_table()
					for habitat in fauna_table:
						for f in fauna_table[habitat]:
							if tile == f["symbol"]:
								_handle_animal_interaction(f, Vector2i(alx, aly))
								return
					
					# Check for Flora
					for climate in gs.FLORA_TABLE:
						for f in gs.FLORA_TABLE[climate]:
							if tile == f["symbol"]:
								_handle_flora_harvest(f, Vector2i(alx, aly))
								return
					
					# Check for Resource Nodes (Single letter)
					if tile.length() == 1 and tile == tile.to_upper() and tile not in [".", "o", "^", "~", "#", "B", "+"]:
						_handle_resource_node(tile, Vector2i(alx, aly))
						return
		return # Return check local first

	# Fast Mode (Overworld) Interactions
	# Settlement
	if pos in gs.settlements:
		var s = gs.settlements[pos]
		if s.tournament_active:
			gs.add_log("Welcome to %s. A [color=yellow]TOURNAMENT[/color] is being held! Press 'T' to enter or 'M' for management." % s.name)
		else:
			gs.add_log("Welcome to %s. Press 'M' to manage, trade, or recruit." % s.name)
		return
		
	if pos in gs.ruins:
		AIManager.explore_ruin(gs, pos)
		return

	# Adjacent interactions
	var neighbors = [Vector2i(0,0), Vector2i(0,1), Vector2i(0,-1), Vector2i(1,0), Vector2i(-1,0), Vector2i(1,1), Vector2i(-1,-1), Vector2i(1,-1), Vector2i(-1,1)]
	for n in neighbors:
		var check = pos + n
		# Check Ongoing Battles first
		for b in gs.ongoing_battles:
			if b.pos == check:
				var opts = ["Join Attacker", "Join Defender", "Leave"]
				gs.dialogue_started.emit(b, opts)
				return
				
		# Check Armies
		for army_obj in gs.armies:
			if army_obj.pos == check:
				var options = ["Talk", "Attack", "Leave"]
				if army_obj.faction != "bandits" and army_obj.faction != "player" and army_obj.faction != "":
					options.insert(1, "Ask for Work")
					
					var npc = gs.find_npc(army_obj.lord_id)
					if npc and npc.title == "King":
						var days = gs.player.service_history.get(army_obj.faction, 0)
						var f_data = gs.get_faction(army_obj.faction)
						var rel = f_data.relations.get("player", 0) if f_data else 0
						
						if days >= 14 and rel >= 20 and gs.player.faction == "player":
							var available_fiefs = 0
							for s_p in gs.settlements:
								if gs.settlements[s_p].faction == army_obj.faction and not gs.settlements[s_p].is_capital:
									available_fiefs += 1
							
							if available_fiefs > 0:
								options.insert(2, "Swear Fealty (Accept Fief)")
							options.insert(3, "Swear Fealty (Royal Charter)")
						
						if gs.player.faction == army_obj.faction:
							options.insert(1, "Request Charter (2500 crowns)")

					# VASSAL COMMANDS
					if gs.player.faction == army_obj.faction and gs.player.id != army_obj.lord_id:
						var my_rel = 50 # Default for same faction for now, or check gs.player.relations
						if my_rel >= 50:
							options.insert(1, "COMMAND: Follow Me")
							options.insert(2, "COMMAND: Garrison Nearest")
							options.insert(3, "COMMAND: Resume duties")
				gs.dialogue_started.emit(army_obj, options)
				return
		# Check Caravans
		for c_obj in gs.caravans:
			if c_obj.pos == check:
				var options = ["Attack", "Trade", "Demand Toll", "Leave"]
				gs.dialogue_started.emit(c_obj, options)
				return

func _handle_flora_harvest(f, _l_pos):
	var gs = GameState
	gs.add_log("You gathered some %s." % f["name"])
	for res in f["loot"]:
		gs.player.inventory[res] = gs.player.inventory.get(res, 0) + f["loot"][res]
	# Remove from local grid
	get_node("/root/Main/BattleController").grid[_l_pos.y][_l_pos.x] = "."
	gs.emit_signal("map_updated")

func _handle_resource_node(sym, _l_pos):
	var gs = GameState
	var res_name = "Minerals"
	match sym:
		"I": res_name = "Iron"
		"G": res_name = "Gold"
		"C": res_name = "Copper"
		"S": res_name = "Silver"
		"T": res_name = "Tin"
	
	gs.add_log("You mined some %s ores." % res_name)
	var res_key = res_name.to_lower()
	gs.player.inventory[res_key] = gs.player.inventory.get(res_key, 0) + 5
	get_node("/root/Main/BattleController").grid[_l_pos.y][_l_pos.x] = "."
	gs.emit_signal("map_updated")

func check_encounters():
	# Auto-trigger dialogue if on top of battle
	for b in GameState.ongoing_battles:
		if b.pos == GameState.player.pos:
			var opts = ["Join Attacker", "Join Defender", "Leave"]
			GameState.dialogue_started.emit(b, opts)
			return

	# Auto-trigger dialogue if on top of entity
	for army_obj in GameState.armies:
		if army_obj.pos == GameState.player.pos:
			var options = ["Talk", "Attack", "Leave"]
			if army_obj.faction != "bandits" and army_obj.faction != "player" and army_obj.faction != "":
				options.insert(1, "Ask for Work")
				
				# Vassalage Option
				var npc = GameState.find_npc(army_obj.lord_id)
				if npc and npc.title == "King":
					var days = GameState.player.service_history.get(army_obj.faction, 0)
					var f_data = GameState.get_faction(army_obj.faction)
					var rel = f_data.relations.get("player", 0) if f_data else 0
					
					if days >= 14 and rel >= 20 and GameState.player.faction == "player":
						# Count available non-capital fiefs
						var available_fiefs = 0
						for s_p in GameState.settlements:
							if GameState.settlements[s_p].faction == army_obj.faction and not GameState.settlements[s_p].is_capital:
								available_fiefs += 1
						
						if available_fiefs > 0:
							options.insert(2, "Swear Fealty (Accept Fief)")
						options.insert(3, "Swear Fealty (Royal Charter)")
						
					if GameState.player.faction == army_obj.faction:
						options.insert(1, "Request Charter (2500 crowns)")
				
				# VASSAL COMMANDS
				if GameState.player.faction == army_obj.faction and GameState.player.id != army_obj.lord_id:
					var my_rel = 50 
					if my_rel >= 50:
						options.insert(1, "COMMAND: Follow Me")
						options.insert(2, "COMMAND: Garrison Nearest")
						options.insert(3, "COMMAND: Resume duties")
			GameState.dialogue_started.emit(army_obj, options)
			return
	
	for c_obj in GameState.caravans:
		if c_obj.pos == GameState.player.pos:
			var options = ["Attack", "Trade", "Demand Toll", "Leave"]
			GameState.dialogue_started.emit(c_obj, options)
			return
