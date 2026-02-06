class_name WorldAudit
extends Node
@warning_ignore("shadowed_global_identifier")

static func run_audit(gs):
	var total_pop = 0
	var total_wealth = 0
	var total_eff = 0.0
	var total_houses = 0
	var total_capacity = 0
	var total_industry_lvl = 0.0
	var total_defense_lvl = 0.0
	var total_civil_lvl = 0.0
	var overcrowding_count = 0
	var food_deficit_count = 0
	var fuel_deficit_count = 0
	var total_daily_demand_food = 0.0
	var total_daily_demand_fuel = 0.0
	var settlement_count = gs.settlements.size()
	var global_inventory = {}
	var global_prices = {}
	var resource_list = [
		"grain", "fish", "meat", "wood", "stone", "iron", "copper", "tin", "lead", "silver", "gold", "gems",
		"marble", "coal", "steel", "bronze", "jewelry", "glass_sand", "fine_garments",
		"wool", "hides", "cloth", "leather", "ale", "salt", "clay", "peat", "furs", "sand"
	]
	
	for pos in gs.settlements:
		var s = gs.settlements[pos]
		var pop = s.population
		var cap = s.get_housing_capacity()
		
		total_pop += pop
		total_wealth += s.crown_stock
		total_eff += s.get_workforce_efficiency()
		total_houses += s.buildings.get("housing_district", 0)
		total_capacity += cap
		
		# Pillar Calculations
		for b in s.buildings:
			var lvl = s.buildings[b]
			if b in ["farm", "lumber_mill", "fishery", "mine", "pasture", "blacksmith", "tannery", "weaver", "brewery", "tailor", "bronzesmith", "goldsmith", "warehouse_district"]:
				total_industry_lvl += lvl
			elif b in ["stone_walls", "barracks", "granary", "watchtower"]:
				total_defense_lvl += lvl
			elif b in ["housing_district", "market", "road_network", "cathedral", "tavern", "merchant_guild"]:
				total_civil_lvl += lvl
		
		if pop > cap: overcrowding_count += 1
		
		# Sustainability Math
		var d_food = float(pop) * Globals.DAILY_BUSHELS_PER_PERSON
		var d_fuel = float(pop) / Globals.WOOD_FUEL_POP_DIVISOR
		total_daily_demand_food += d_food
		total_daily_demand_fuel += d_fuel
		
		var l_force = int(pop * s.get_workforce_efficiency())
		var max_g = (min(l_force, s.arable_acres / Globals.ACRES_WORKED_PER_LABORER) * (Globals.BUSHELS_PER_ACRE_BASE / 360.0)) * (1.0 - Globals.SEED_RATIO_INV)
		var max_f = min(l_force, s.fishing_slots) * (Globals.FISHING_YIELD_BASE / 360.0)
		var wild = s.forest_acres + s.wilderness_acres + s.pasture_acres
		var max_h = (min(l_force, wild / Globals.ACRES_WORKED_PER_LABORER) * (Globals.HUNTING_YIELD_MEAT / 360.0))
		var max_o = (min(l_force, wild / Globals.ACRES_WORKED_PER_LABORER) * (Globals.FORAGING_YIELD_GRAIN / 360.0))
		
		if (max_g + max_f + max_h + max_o) < d_food: food_deficit_count += 1
		if (min(l_force, s.total_acres / Globals.ACRES_WORKED_PER_LABORER) * (Globals.FORESTRY_YIELD_WOOD / 360.0)) < d_fuel: fuel_deficit_count += 1
		
		for res in resource_list:
			var amt = s.inventory.get(res, 0)
			global_inventory[res] = global_inventory.get(res, 0) + amt
			
			var price = EconomyManager.get_price(res, s)
			if not global_prices.has(res): global_prices[res] = []
			global_prices[res].append(price)
			
	# --- MILITARY & POLITICAL STATS ---
	var army_count = gs.armies.size()
	var total_soldiers = 0
	var armies_by_type = {} # lord, bandit, patrol, etc.
	for a in gs.armies:
		total_soldiers += a.roster.size()
		armies_by_type[a.type] = armies_by_type.get(a.type, 0) + 1
		
	var faction_holdings = {} # faction_id -> {settlements, armies, wealth}
	var lords_with_fiefs = 0
	
	for pos in gs.settlements:
		var s = gs.settlements[pos]
		if not faction_holdings.has(s.faction): faction_holdings[s.faction] = {"settlements": 0, "armies": 0, "wealth": 0}
		faction_holdings[s.faction].settlements += 1
		if s.lord_id != "":
			lords_with_fiefs += 1
			
	# Include army wealth in faction holdings calculation roughly
	for a in gs.armies:
		if a.faction != "neutral" and a.faction != "bandits":
			if not faction_holdings.has(a.faction): faction_holdings[a.faction] = {"settlements": 0, "armies": 0, "wealth": 0}
			faction_holdings[a.faction].armies += 1
			faction_holdings[a.faction].wealth += a.crowns

	print("\n--- WORLD AUDIT REPORT ---")
	print("Date: %s (Turn %d)" % [gs.get_date_string(), gs.turn])
	
	print("\n[ DEMOGRAPHICS ]")
	print("  Total Population: %d" % total_pop)
	print("  Total Houses:     %d (Capacity: %d)" % [total_houses, total_capacity])
	print("  Housing Slack:    %d" % (total_capacity - total_pop))
	print("  Overcrowded:      %d settlements" % overcrowding_count)
	print("  Avg Infrastructure Pillars:")
	print("    Industry: %.1f | Defense: %.1f | Civil: %.1f" % [
		total_industry_lvl / max(1, settlement_count),
		total_defense_lvl / max(1, settlement_count),
		total_civil_lvl / max(1, settlement_count)
	])
	
	print("\n[ SUSTAINABILITY ]")
	print("  Food Security:    %d settlements in deficit (Global Demand: %.1f)" % [food_deficit_count, total_daily_demand_food])
	print("  Fuel Security:    %d settlements in deficit (Global Demand: %.1f)" % [fuel_deficit_count, total_daily_demand_fuel])
	var avg_food_buffer = global_inventory.get("grain", 0) / max(1.0, total_daily_demand_food)
	var avg_fuel_buffer = global_inventory.get("wood", 0) / max(1.0, total_daily_demand_fuel)
	print("  Avg Food Buffer:  %.1f days" % avg_food_buffer)
	print("  Avg Fuel Buffer:  %.1f days" % avg_fuel_buffer)
	
	print("\n[ ECONOMY ]")
	print("  Settlement Wealth: %d Crowns" % total_wealth)
	print("  Avg Efficiency:    %.2f%%" % ((total_eff / max(1, settlement_count)) * 100.0))
	print("  Trade Networks:")
	print("    Active Contracts: %d (Logistical Coverage: %.0f%%)" % [
		gs.trade_contracts.size(), 
		(gs.trade_contracts.size() / max(1.0, float(gs.world_market_orders.size()))) * 100.0
	])
	var active_caravans = 0
	for a in gs.caravans:
		if a.state != "idle": active_caravans += 1
	print("    Active Logistics: %d Caravans in transit" % active_caravans)
	print("  World Market:      %d active buy orders" % gs.world_market_orders.size())
	
	print("\n[ STRATEGIC OPERATIONS ]")
	var gathering = 0
	var marching = 0
	var camp_details = []
	for camp in gs.military_campaigns:
		if camp.status == "gathering": gathering += 1
		elif camp.status == "marching": marching += 1
		var t_name = gs.settlements[camp.target_pos].name if gs.settlements.has(camp.target_pos) else "Unknown"
		camp_details.append("%s: %s -> %s (%s)" % [camp.faction.capitalize(), camp.type, t_name, camp.status])
	
	print("  Active Campaigns:  %d" % gs.military_campaigns.size())
	for detail in camp_details:
		print("    - %s" % detail)
	
	var active_sieges = []
	var total_siege_days = 0
	for s_pos in gs.settlements:
		var s = gs.settlements[s_pos]
		if s.is_under_siege:
			var days = s.siege_timer / 24.0
			var att_f = s.siege_attacker_faction if s.siege_attacker_faction != "" else "Unknown"
			active_sieges.append("%s: %d days (Attacker: %s)" % [s.name, int(days), att_f])
			total_siege_days += days
	
	print("  Active Sieges:     %d" % active_sieges.size())
	for detail in active_sieges:
		print("    - %s" % detail)

	print("\n[ MILITARY ]")
	var total_renown = 0
	var total_lords = 0
	var escorts = 0
	var wandering = 0
	var lord_wealths = []
	var total_lord_wealth = 0
	
	for a in gs.armies:
		if a.type == "lord":
			total_renown += a.renown
			total_lords += 1
			var w = a.crowns
			lord_wealths.append(w)
			total_lord_wealth += w
		
		var t_type = a.cached_target.get("type", "") if a.cached_target else ""
		if t_type == "escort":
			escorts += 1
		elif t_type == "idle" or a.cached_target == null:
			wandering += 1
	
	lord_wealths.sort()
	var median_wealth = 0
	if not lord_wealths.is_empty():
		var mid = lord_wealths.size() / 2
		if lord_wealths.size() % 2 == 0:
			median_wealth = (lord_wealths[mid-1] + lord_wealths[mid]) / 2
		else:
			median_wealth = lord_wealths[mid]

	print("  Total Armies:   %d (Lords: %d)" % [army_count, total_lords])
	print("  Coordination:   %d Escorting | %d Independent" % [escorts, wandering])
	print("  Lord Growth:")
	print("    Avg Renown:    %.1f" % (float(total_renown)/max(1, total_lords)))
	print("    Total Soldiers: %d" % total_soldiers)
	print("  Lord Capital:")
	print("    Total Gold:    %d Crowns" % total_lord_wealth)
	print("    Avg Wealth:    %d Crowns" % (total_lord_wealth / max(1, total_lords)))
	print("    Median Wealth: %d Crowns" % median_wealth)
	print("  Unit Breakdown:")
	for t in armies_by_type:
		print("    %s: %d armies" % [t.capitalize(), armies_by_type[t]])
		
	print("\n[ POLITICS ]")
	print("  Landed Lords: %d / %d settlements" % [lords_with_fiefs, settlement_count])
	print("  Faction Holdings:")
	for f in faction_holdings:
		var d = faction_holdings[f]
		print("    %s: %d settlements, %d armies" % [f, d.settlements, d.armies])
	
	print("\nFACTION BREAKDOWN:")
	var faction_stats = {}
	for f in gs.factions:
		faction_stats[f.id] = {
			"population": 0,
			"laborers": 0,
			"burghers": 0,
			"nobility": 0,
			"settlements": 0,
			"wealth": f.treasury,
			"strength": 0,
			"happiness": 0.0,
			"industry": 0.0,
			"defense": 0.0,
			"civil": 0.0,
			"production": {}
		}
	
	for pos in gs.settlements:
		var s = gs.settlements[pos]
		var f = s.faction
		if faction_stats.has(f):
			faction_stats[f]["population"] += s.population
			faction_stats[f]["laborers"] += s.laborers
			faction_stats[f]["burghers"] += s.burghers
			faction_stats[f]["nobility"] += s.nobility
			faction_stats[f]["settlements"] += 1
			faction_stats[f]["wealth"] += s.crown_stock
			faction_stats[f]["happiness"] += s.happiness
			
			# Add Pillar Totals
			for b in s.buildings:
				var lvl = s.buildings[b]
				if b in ["farm", "lumber_mill", "fishery", "mine", "pasture", "blacksmith", "tannery", "weaver", "brewery", "tailor", "goldsmith", "warehouse_district"]:
					faction_stats[f]["industry"] += lvl
				elif b in ["stone_walls", "barracks", "granary", "watchtower"]:
					faction_stats[f]["defense"] += lvl
				elif b in ["housing_district", "market", "road_network", "cathedral", "tavern", "merchant_guild"]:
					faction_stats[f]["civil"] += lvl
			
			var eff = s.get_workforce_efficiency()
			for res in s.production_capacity:
				var amt = int(s.production_capacity[res] * eff)
				if amt > 0:
					faction_stats[f]["production"][res] = faction_stats[f]["production"].get(res, 0) + amt
			
			# Add Labor-Based Production
			var alloc = s.last_labor_allocation
			if not alloc.is_empty():
				if alloc.get("farms", 0) > 0:
					var p_grain = int((alloc["farms"] * Globals.ACRES_WORKED_PER_LABORER * Globals.BUSHELS_PER_ACRE_BASE / 360.0) * (1.0 - Globals.SEED_RATIO_INV))
					faction_stats[f]["production"]["grain"] = faction_stats[f]["production"].get("grain", 0) + p_grain
				if alloc.get("fishing", 0) > 0:
					var p_fish = int(alloc["fishing"] * Globals.FISHING_YIELD_BASE / 360.0)
					faction_stats[f]["production"]["fish"] = faction_stats[f]["production"].get("fish", 0) + p_fish
				if alloc.get("wood", 0) > 0:
					var p_wood = int(alloc["wood"] * Globals.ACRES_WORKED_PER_LABORER * Globals.FORESTRY_YIELD_WOOD / 360.0)
					faction_stats[f]["production"]["wood"] = faction_stats[f]["production"].get("wood", 0) + p_wood
				if alloc.get("hunting", 0) > 0:
					var p_meat = int(alloc["hunting"] * Globals.ACRES_WORKED_PER_LABORER * Globals.HUNTING_YIELD_MEAT / 360.0)
					faction_stats[f]["production"]["meat"] = faction_stats[f]["production"].get("meat", 0) + p_meat
				if alloc.get("mining", 0) > 0:
					var p_stone = int(alloc["mining"] * 4.0 / 30.0)
					faction_stats[f]["production"]["stone"] = faction_stats[f]["production"].get("stone", 0) + p_stone
				if alloc.get("trapping", 0) > 0:
					var p_furs = int(alloc["trapping"] * Globals.ACRES_WORKED_PER_LABORER * Globals.FUR_YIELD / 360.0)
					faction_stats[f]["production"]["furs"] = faction_stats[f]["production"].get("furs", 0) + p_furs
				if alloc.get("pasture", 0) > 0:
					var p_wool = int(alloc["pasture"] * Globals.ACRES_WORKED_PER_LABORER * Globals.PASTURE_YIELD_WOOL / 360.0)
					var p_horses = int(alloc["pasture"] * Globals.ACRES_WORKED_PER_LABORER * Globals.PASTURE_YIELD_HORSES / 360.0)
					faction_stats[f]["production"]["wool"] = faction_stats[f]["production"].get("wool", 0) + p_wool
					if p_horses > 0:
						faction_stats[f]["production"]["horses"] = faction_stats[f]["production"].get("horses", 0) + p_horses
			
	for a in gs.armies:
		var f = a.faction
		if faction_stats.has(f):
			var str_val = a.roster.size() if a.roster.size() > 0 else a.strength
			faction_stats[f]["strength"] += str_val
			
	for f_id in faction_stats:
		var stats = faction_stats[f_id]
		var avg_hap = stats["happiness"] / max(1, stats["settlements"])
		var l_count = stats.get("laborers", 0)
		var b_count = stats.get("burghers", 0)
		var n_count = stats.get("nobility", 0)
		var pop = stats.get("population", 0)
		
		print("  %-10s | Pop: %-6d (L:%d%% B:%d%% N:%d%%) | Fiefs: %-2d | Wealth: %-7d | Army: %-4d | Hap: %d%%" % [
			f_id.capitalize(), pop, 
			int(float(l_count)/max(1, pop)*100), 
			int(float(b_count)/max(1, pop)*100),
			int(float(n_count)/max(1, pop)*100),
			stats["settlements"], stats["wealth"], stats["strength"], int(avg_hap)
		])
		
		var avg_ind = stats["industry"] / max(1, stats["settlements"])
		var avg_def = stats["defense"] / max(1, stats["settlements"])
		var avg_civ = stats["civil"] / max(1, stats["settlements"])
		print("             | Pillars: Ind:%.1f Def:%.1f Civ:%.1f" % [avg_ind, avg_def, avg_civ])
		
		# Print Top Production
		if stats["production"].size() > 0:
			var prod_line = "             | Prod: "
			var sorted_res = stats["production"].keys()
			sorted_res.sort_custom(func(a, b): return stats["production"][a] > stats["production"][b])
			
			var count = 0
			for res in sorted_res:
				if count >= 5: break
				prod_line += "%s:+%d " % [res.capitalize(), stats["production"][res]]
				count += 1
			print(prod_line)
	
	print("\nRESOURCE STOCKS & AVG PRICES:")
	for res in resource_list:
		var stock = global_inventory.get(res, 0)
		var prices = global_prices.get(res, [0])
		var avg_price = 0
		if prices.size() > 0:
			var sum = 0
			for p in prices: sum += p
			avg_price = sum / prices.size()
		print("  %-10s: %-6d (Avg Price: %d)" % [res.capitalize(), stock, avg_price])
	
	print("\nLOGISTICS & WAR:")
	print("  Active Caravans: %d (Losses: %d)" % [gs.caravans.size(), gs.total_caravan_raids])
	print("  Active Armies:   %d (Battles: %d)" % [gs.armies.size(), gs.total_battles])
	print("  Sieges:          %d (Captures: %d)" % [gs.total_sieges, gs.total_captures])
	
	var war_count = 0
	for i in range(gs.factions.size()):
		for j in range(i + 1, gs.factions.size()):
			if gs.get_relation(gs.factions[i].id, gs.factions[j].id) == "war":
				war_count += 1
	print("  Active Wars:     %d" % war_count)
	
	var intents = {}
	for a in gs.armies:
		var type = "idle"
		if a.cached_target: type = a.cached_target.get("type", "unknown")
		intents[type] = intents.get(type, 0) + 1
	
	var intent_str = "  Army Intent: "
	for i_type in intents:
		intent_str += "%s:%d " % [i_type, intents[i_type]]
	print(intent_str)
	print("---------------------------------------\n")

static func init_ledger(gs):
	var faction_pops = {}
	for f in gs.factions:
		var p = 0
		for s_pos in gs.settlements:
			if gs.settlements[s_pos].faction == f.id:
				p += gs.settlements[s_pos].population
		faction_pops[f.id] = p

	gs.monthly_ledger = {
		"start_day": gs.day,
		"pop_start": gs.get_total_population(),
		"crowns_start": gs.player.crowns,
		"faction_pops": faction_pops,
		"production": {},
		"consumption": {},
		"idle_buildings": {},
		"events": [],
		"starvation_deaths": 0,
		"migration_net": 0,
		"tax_collected": 0,
		"upkeep_paid": 0,
		"battles_start": gs.total_battles,
		"sieges_start": gs.total_sieges,
		"captures_start": gs.total_captures,
		"caravan_raids_start": gs.total_caravan_raids,
		"deaths_war": 0,
		"births": 0,
		"pulses_generated": 0,
		"pulses_delivered": 0,
		"pulses_dropped": 0,
		"buy_orders_placed": 0,
		"buy_orders_fulfilled": 0
	}

static func run_turbo_simulation(gs):
	if gs.is_turbo: return
	gs.is_turbo = true
	gs.add_log("[color=yellow]STARTING 30-DAY TURBO SIMULATION...[/color]")
	
	init_ledger(gs)
	
	# Run 30 days (720 hours)
	for i in range(30 * 24):
		gs.advance_time()
		if i % 24 == 0:
			gs.emit_signal("map_updated")
			gs.emit_signal("log_updated")
			await gs.get_tree().process_frame
		
	gs.is_turbo = false
	print_monthly_report(gs)
	run_audit(gs) # Call detailed audit report after simulation
	
static func run_annual_simulation(gs):
	if gs.is_turbo: return
	gs.is_turbo = true
	gs.add_log("[color=orange]STARTING 1-YEAR (360-DAY) TURBO SIMULATION...[/color]")
	
	for month in range(Globals.MONTHS_PER_YEAR):
		init_ledger(gs)
		# Clear heavy caches at the start of each month to keep memory usage stable-ish
		gs.distance_cache.clear() 
		
		# Run one month
		for i in range(Globals.DAYS_PER_MONTH):
			# Daily Optimization: In Turbo, hourly loops are skipped where possible
			# We only need to run advance_time for hours that have logic 
			# In turbo, we just run the 24 hours as one block of daily pulses
			# This is a huge speedup.
			for h in range(24):
				gs.advance_time()
			
			gs.emit_signal("map_updated") 
			await gs.get_tree().process_frame
		
		# Monthly report output
		run_audit(gs) # Added: Full detailed audit for month-to-month tracking
		print_monthly_report(gs)
		gs.add_log("[color=gray]--- End of Month %d/%d Simulation ---[/color]" % [month + 1, Globals.MONTHS_PER_YEAR])
		
	gs.is_turbo = false
	gs.add_log("[color=orange]ANNUAL SIMULATION COMPLETE.[/color]")
	gs.emit_signal("map_updated")

static func print_monthly_report(gs):
	render_diagnostic_report(gs)
	init_ledger(gs)

static func render_diagnostic_report(gs):
	var total_pop = 0
	var total_crowns = 0
	var inventory = {}
	var prices = {}
	var settlement_count = gs.settlements.size()
	
	var res_list = [
		"grain", "fish", "meat", "wood", "stone", "iron", "copper", "tin", "lead", "silver", "gold", "gems",
		"marble", "coal", "steel", "bronze", "jewelry", "glass_sand", "fine_garments",
		"wool", "hides", "cloth", "leather", "ale", "salt", "clay", "peat", "furs", "sand", "tools", "bricks"
	]
	
	var ind_lvls = 0.0
	var def_lvls = 0.0
	var civ_lvls = 0.0
	var food_demand = 0.0
	var weight_eff = 0.0
	
	var wealth_map = {} # Key: Pos, Value: {name, crowns}
	
	for pos in gs.settlements:
		var s = gs.settlements[pos]
		total_pop += s.population
		total_crowns += s.crown_stock
		wealth_map[pos] = {"name": s.name, "crowns": s.crown_stock}
		weight_eff += s.get_workforce_efficiency()
		food_demand += s.population * Globals.DAILY_BUSHELS_PER_PERSON
		
		# Commodity Stockpile & Prices
		for res in res_list:
			inventory[res] = inventory.get(res, 0) + s.inventory.get(res, 0)
			var p = EconomyManager.get_price(res, s)
			if not prices.has(res): prices[res] = []
			prices[res].append(p)
			
		# Pillars
		for b in s.buildings:
			var lvl = s.buildings[b]
			if b in ["farm", "lumber_mill", "fishery", "mine", "pasture", "blacksmith", "tannery", "weaver", "brewery", "tailor", "bronzesmith", "goldsmith", "warehouse_district"]:
				ind_lvls += lvl
			elif b in ["stone_walls", "barracks", "granary", "watchtower"]:
				def_lvls += lvl
			elif b in ["housing_district", "market", "road_network", "cathedral", "tavern", "merchant_guild"]:
				civ_lvls += lvl

	# Add Faction Treasuries and Army wealth to Global Crowns
	for f in gs.factions:
		total_crowns += f.treasury
	for a in gs.armies:
		total_crowns += a.crowns
	for c in gs.caravans:
		total_crowns += c.crowns

	print("\n" + "=".repeat(80))
	print("   MONTHLY WORLD DIAGNOSTIC REPORT - TURN %d (Day %d)" % [gs.turn, gs.day])
	print("=".repeat(80))
	
	# 1. WORLD OVERVIEW
	print("\n[ 1. WORLD OVERVIEW ]")
	print("Population: %-10d | Settlements: %-10d | Global Crowns: %d" % [total_pop, settlement_count, total_crowns])
	
	# 2. GLOBAL COMMODITY STOCKPILE
	print("\n[ 2. GLOBAL COMMODITY STOCKPILE ]")
	var stock_str = ""
	for i in range(res_list.size()):
		var r = res_list[i]
		stock_str += "%-12s: %-6d " % [r.capitalize(), inventory.get(r, 0)]
		if (i+1) % 4 == 0: stock_str += "\n"
	print(stock_str)
	
	# 3. PRICING DYNAMICS
	print("\n[ 3. PRICING DYNAMICS ]")
	print("%-12s | %-8s | %-8s | %-8s" % ["Resource", "Min", "Avg", "Max"])
	print("-".repeat(45))
	for r in ["grain", "wood", "iron", "tools", "ale", "meat"]:
		var p_list = prices.get(r, [0])
		var p_min = p_list.min()
		var p_max = p_list.max()
		var p_avg = 0.0
		for val in p_list: p_avg += val
		p_avg /= max(1, p_list.size())
		print("%-12s | %-8.1f | %-8.1f | %-8.1f" % [r.capitalize(), p_min, p_avg, p_max])
		
	# 4. DEMOGRAPHIC TRENDS
	var ledger = gs.monthly_ledger
	print("\n[ 4. DEMOGRAPHIC TRENDS ]")
	print("Births:      %-8d | Starvations: %-8d | War Deaths: %d" % [
		ledger.get("births", 0), ledger.get("starvation_deaths", 0), ledger.get("deaths_war", 0)
	])
	print("Migration:   %-8d | Net Change:  %d" % [
		ledger.get("migration_net", 0), 
		ledger.get("births", 0) - ledger.get("starvation_deaths", 0) - ledger.get("deaths_war", 0)
	])
	
	# 5. LOGISTICAL PULSE PERFORMANCE
	var p_gen = float(ledger.get("pulses_generated", 1))
	var p_del = float(ledger.get("pulses_delivered", 0))
	var p_drp = float(ledger.get("pulses_dropped", 0))
	var p_eff = (p_del / p_gen) * 100.0 if p_gen > 0 else 0.0
	print("\n[ 5. LOGISTICAL PULSE PERFORMANCE ]")
	print("Generated:   %-8d | Delivered:   %-8d | Dropped:    %d" % [p_gen, p_del, p_drp])
	print("Efficiency:  %.2f%%" % p_eff)
	
	# 6. MARKET EFFICIENCY
	var b_plc = float(ledger.get("buy_orders_placed", 1))
	var b_ful = float(ledger.get("buy_orders_fulfilled", 0))
	var b_rate = (b_ful / b_plc) * 100.0 if b_plc > 0 else 0.0
	print("\n[ 6. MARKET EFFICIENCY ]")
	print("Buy Orders:  %-8d | Fulfilled:   %-8d | Rate:       %.2f%%" % [b_plc, b_ful, b_rate])
	print("Trade Volume: %d Crowns" % gs.total_trade_volume)
	
	# 7. MILITARY ACTIVITY
	print("\n[ 7. MILITARY ACTIVITY ]")
	print("Battles:     %-8d | Sieges:      %-8d | Raids:      %d" % [
		gs.total_battles, gs.total_sieges, gs.total_caravan_raids
	])
	
	# 8. RESOURCE PILLAR DEPTH
	print("\n[ 8. RESOURCE PILLAR DEPTH ]")
	print("Industry:    %.2f | Defense:     %.2f | Civil:      %.2f" % [
		ind_lvls / settlement_count, def_lvls / settlement_count, civ_lvls / settlement_count
	])
	
	# 9. FOOD SECURITY INDEX
	var buffer = inventory.get("grain", 0) / max(1.0, food_demand)
	print("\n[ 9. FOOD SECURITY INDEX ]")
	print("Daily Demand: %.1f | World Grain: %d | Days Buffer: %.1f" % [food_demand, inventory.get("grain", 0), buffer])
	
	# 10. INDUSTRIAL OUTPUT
	print("\n[ 10. INDUSTRIAL OUTPUT (NET) ]")
	var prod = ledger.get("production", {})
	var cons = ledger.get("consumption", {})
	var top_prod = []
	for res in prod: top_prod.append([res, prod[res]])
	top_prod.sort_custom(func(a,b): return b[1] > a[1])
	var prod_str = ""
	for i in range(min(5, top_prod.size())):
		prod_str += "%s (+%d) " % [top_prod[i][0].capitalize(), top_prod[i][1]]
	print("Top Production  : %s" % prod_str)
	var top_cons = []
	for res in cons: top_cons.append([res, cons[res]])
	top_cons.sort_custom(func(a,b): return b[1] > a[1])
	var cons_str = ""
	for i in range(min(5, top_cons.size())):
		cons_str += "%s (-%d) " % [top_cons[i][0].capitalize(), top_cons[i][1]]
	print("Top Consumption : %s" % cons_str)
	
	# 11. WEALTH CONCENTRATION
	print("\n[ 11. WEALTH CONCENTRATION ]")
	var sorted_wealth = wealth_map.keys()
	sorted_wealth.sort_custom(func(a, b): return wealth_map[a].crowns > wealth_map[b].crowns)
	for i in range(min(3, sorted_wealth.size())):
		var s_data = wealth_map[sorted_wealth[i]]
		print("%d. %-15s: %d Crowns" % [i+1, s_data.name, s_data.crowns])
		
	# 12. GEOPOLITICAL STANDINGS
	print("\n[ 12. GEOPOLITICAL STANDINGS ]")
	var f_strengths = {}
	for f in gs.factions: f_strengths[f.id] = 0
	for a in gs.armies:
		if f_strengths.has(a.faction):
			f_strengths[a.faction] += a.roster.size() if a.roster.size() > 0 else a.strength
			
	for f in gs.factions:
		if f.id == "neutral" or f.id == "bandits": continue
		var rel_str = gs.get_relation(f.id, "player")
		print("%-20s | Strength: %-6d | Rel: %s" % [f.name, f_strengths.get(f.id, 0), rel_str])
		
	# 13. ANOMALIES & ALERTS
	print("\n[ 13. ANOMALIES & ALERTS ]")
	var alerts = []
	if buffer < 5.0: alerts.append("CRITICAL FOOD SHORTAGE: World grain buffer below 5 days!")
	if p_eff < 50.0: alerts.append("LOGISTICAL COLLAPSE: Under 50% pulses reaching destination.")
	if b_rate < 20.0: alerts.append("MARKET STAGNATION: Fulfillment rate under 20%.")
	if ledger.get("deaths_war", 0) > 100: alerts.append("TOTAL WAR: High combat casualties this month.")
	
	if alerts.size() == 0:
		print("Simulation steady. No major anomalies detected.")
	else:
		for a in alerts: print("!! %s" % a)
		
	# 14. HEALTH SCORE
	var score = 100.0
	if buffer < 10: score -= (10 - buffer) * 5
	if p_eff < 90: score -= (90 - p_eff)
	if weight_eff < 0.8: score -= (0.8 - weight_eff) * 50
	
	var grade = "F"
	if score >= 90: grade = "A"
	elif score >= 80: grade = "B"
	elif score >= 70: grade = "C"
	elif score >= 60: grade = "D"
	
	print("\n" + "=".repeat(40))
	print("   SIMULATION HEALTH SCORE: %.1f [%s]" % [score, grade])
	print("=".repeat(40) + "\n")
