class_name EconomyPhase
extends WorldGenPhase

## Handles economy calculations, borders, and provincial capitals

const Globals = preload("res://src/core/Globals.gd")
const GDSettlement = preload("res://src/data/GDSettlement.gd")
const EconomyManager = preload("res://src/managers/EconomyManager.gd")

func get_phase_name() -> String:
	return "Economy"

func execute(context: WorldGenContext) -> bool:
	var w = context.width
	var h = context.height
	var world_grid = context.world_grid
	var province_grid_arr = context.province_grid.to_legacy_grid()
	
	step_completed.emit("CALCULATING ECONOMY...")
	
	for s_pos in context.world_settlements:
		var s = context.world_settlements[s_pos]
		s.inventory["grain"] = s.population * Globals.DAILY_BUSHELS_PER_PERSON * 7.0
		s.crown_stock = 1000
		
		# Check if provincial capital
		var is_seat = false
		for p_id in context.provinces:
			if context.provinces[p_id].capital == s_pos:
				is_seat = true
				break
		
		if is_seat:
			s.crown_stock = 3000
			s.inventory["wood"] = 150
			s.inventory["iron"] = 40
			s.inventory["coal"] = 20
		
		# Royal centers (tier 4)
		if s.tier == 4:
			s.crown_stock = 8000
			s.inventory["wood"] = 500
			s.inventory["iron"] = 100
			s.inventory["coal"] = 50
			s.inventory["steel"] = 25
			s.population = max(s.population, 800)
			
			var p_id = province_grid_arr[s.pos.y][s.pos.x]
			var p_name = context.provinces[p_id].name if p_id != -1 else "Royal"
			s.name = "%s Royal Center" % p_name
			s.sync_social_classes()
			
			var cap_provided = s.buildings.get("housing_district", 0) * 100
			s.houses = max(20, int((s.population - cap_provided) / 5.0) + 5)
			world_grid[s_pos.y][s_pos.x] = 'C'
		elif s.type == "city":
			world_grid[s_pos.y][s_pos.x] = 'C'
		elif s.type == "town" or s.tier == 2:
			world_grid[s_pos.y][s_pos.x] = 'v'
		elif s.type == "satellite" or s.type == "hamlet":
			world_grid[s_pos.y][s_pos.x] = 'h'
		
		# Final economy pass
		s.sync_social_classes()
		var final_cap_provided = s.buildings.get("housing_district", 0) * 100
		s.houses = max(20, int((s.population - final_cap_provided) / 5.0) + 5)
		EconomyManager.recalculate_production(s, world_grid, context.world_resources, context.geology)
	
	step_completed.emit("DELINEATING BORDERS...")
	
	# Ensure every province has a capital
	for p_id in context.provinces:
		var p = context.provinces[p_id]
		var local_settlements = []
		
		for s_pos in context.world_settlements:
			if province_grid_arr[s_pos.y][s_pos.x] == p_id:
				local_settlements.append(s_pos)
		
		if local_settlements.is_empty():
			# Create frontier outpost
			var p_seed = p.center
			var outpost = GDSettlement.new(p_seed)
			outpost.name = p.name + " Outpost"
			outpost.population = 150
			outpost.tier = 1
			outpost.type = "hamlet"
			outpost.faction = p.get("owner", "neutral")
			context.world_settlements[p_seed] = outpost
			p.capital = p_seed
			world_grid[p_seed.y][p_seed.x] = 'h'
		else:
			# Pick largest as capital
			var best_s = local_settlements[0]
			var max_pop = context.world_settlements[best_s].population
			for s_pos in local_settlements:
				if context.world_settlements[s_pos].population > max_pop:
					max_pop = context.world_settlements[s_pos].population
					best_s = s_pos
			p.capital = best_s
		
		# Mark non-capital settlements
		for s_pos in local_settlements:
			if s_pos != p.capital:
				var s = context.world_settlements[s_pos]
				if s.tier >= 3: s.type = "city"
				else: s.type = "satellite"
	
	return true

func cleanup(context: WorldGenContext) -> void:
	pass
