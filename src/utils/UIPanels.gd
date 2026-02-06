class_name UIPanels
# Force Refresh 1.3
extends Object

static var terrain_color_cache = {}
static var color_hex_cache = {}

static func _c_to_bb(c: Color) -> String:
	if color_hex_cache.has(c):
		return color_hex_cache[c]
	var h = "#" + c.to_html(false)
	color_hex_cache[c] = h
	return h

static func _wrap_grid(gs, terrain_color: Color, content: String) -> String:
	if gs.render_mode == "grid" and not gs.get("graphical_mode_active"):
		return "[bgcolor=%s]%s[/bgcolor]" % [_c_to_bb(terrain_color), content]
	return content

static func _get_terrain_color(gs, pos: Vector2i, t: String, scope: String = "global") -> Color:
	# Check cache first - use hash for efficient key generation
	var scope_idx = 0
	match scope:
		"local": scope_idx = 1
		"battle": scope_idx = 2
		"region": scope_idx = 3
	
	# Use hash instead of string concatenation for cache key
	var cache_key = hash(Vector3i(pos.x, pos.y, (scope_idx << 8) | t.unicode_at(0)))
	if terrain_color_cache.has(cache_key):
		return terrain_color_cache[cache_key]

	# Get geology data for tinting
	var geo = gs.geology.get(pos, {"temp": 0.5, "rain": 0.5, "elevation": 0.3})
	
	# QUANTIZATION: Reduce unique colors to prevent RichTextLabel "Zebra Stripe" rendering artifacts
	var temp = snappedf(geo.get("temp", 0.5), 0.1)
	var rain = snappedf(geo.get("rain", 0.5), 0.1)
	var elev = snappedf(geo.get("elevation", 0.3), 0.1)
	
	var final_col = Color.WHITE
	
	match t:
		"~", "≈", "/", "\\", "water": # Water
			var v = clamp(0.3 + (elev * 0.4), 0.2, 0.8)
			final_col = Color.from_hsv(0.6, 0.7, v)
		".", ",", "plains", "desert": # Plains / Grassland / Desert
			var hue = clamp(0.25 + (temp * 0.08), 0.2, 0.35)
			if t == "desert" or temp > 0.8: hue = 0.12 # Shifting to yellow/orange
			var sat = clamp(0.3 + (rain * 0.5), 0.2, 0.9)
			var val = clamp(0.6 + (rain * 0.2), 0.4, 0.9)
			final_col = Color.from_hsv(hue, sat, val)
		"#", "T", "&", "forest", "jungle", "hills": # Forest / Jungle / Hills
			var hue = clamp(0.28 - (rain * 0.05), 0.2, 0.35)
			if t == "hills": hue = 0.22 # Slightly more olive
			var sat = clamp(0.4 + (rain * 0.5), 0.3, 0.9)
			var val = clamp(0.3 + (rain * 0.3), 0.2, 0.7)
			final_col = Color.from_hsv(hue, sat, val)
		"o", "O", "^", "peaks": # Mountains / Peaks
			if elev > 0.85 or t == "peaks": 
				final_col = Color.WHITE # Snowcaps
			else:
				var v = clamp(0.7 - (elev * 0.5), 0.1, 0.8)
				final_col = Color(v, v, v)
		"S", "\"", "savanna": # Savanna / Dry
			var hue = 0.12 - (temp * 0.04)
			var sat = 0.4 + (temp * 0.2)
			final_col = Color.from_hsv(hue, sat, 0.8)
		"*", "tundra": # Tundra
			final_col = Color(0.88, 0.94, 1.0) # e0f0ff
		"farms", "\"": # Agriculture
			final_col = Color(0.18, 0.28, 0.15) # Dark Green Crop Rows
		"fallow": # Brown Plots (DF Style)
			final_col = Color(0.45, 0.35, 0.25) # Earthy Brown
		"orchard", "f": # Orchards
			final_col = Color.FOREST_GREEN
		"pasture", ",": # Grazing lands
			final_col = Color.DARK_SEA_GREEN
		"docks": # Waterfront
			final_col = Color(0.4, 0.25, 0.15)
		"keep", "K": # The Keep
			final_col = Color.MEDIUM_PURPLE
		"market", "M":
			final_col = Color.GOLDENROD
		"industrial", "S":
			final_col = Color.MAROON
		"slum":
			final_col = Color.SADDLE_BROWN
		"residential", "urban", "n": # City Buildings (Orange roofs)
			final_col = Color(0.85, 0.45, 0.15) # Burnt Orange Roofs
		"urban_block": # Developed Land
			final_col = Color(0.35, 0.35, 0.35) # Dark Concrete/Stone
		"walls_outer", "bridge_rail": # Stone Works
			final_col = Color.DIM_GRAY
		"=", "road", "bridge": # Road / Bridge
			final_col = Color(0.65, 0.6, 0.55) # Light Gravel/Stone Gray
		_:
			final_col = Color.WHITE
	
	terrain_color_cache[cache_key] = final_col
	return final_col

static func _get_tile_colors(gs, pos: Vector2i, t: String, scope: String = "global") -> Dictionary:
	var bg = _get_terrain_color(gs, pos, t, scope)
	var fg = Color.WHITE
	
	match t:
		"keep", "K":
			bg = Color(0.3, 0.3, 0.35) # Dark Slate Ground
			fg = Color.MEDIUM_PURPLE    # Purple Tower
		"market", "M":
			bg = Color(0.35, 0.32, 0.3)
			fg = Color.GOLDENROD
		"industrial", "S":
			bg = Color(0.32, 0.28, 0.28)
			fg = Color.MAROON
		"H": # Warehouses / Docks
			bg = Color(0.3, 0.2, 0.1)
			fg = Color(0.6, 0.45, 0.3)
		"slum":
			bg = Color(0.25, 0.2, 0.15)
			fg = Color.SADDLE_BROWN
		"residential", "urban", "n", "o", "B":
			bg = Color(0.4, 0.4, 0.4)    # Stone Ground
			fg = Color(0.85, 0.45, 0.15) # Orange Roofs / Walls
		"urban_block":
			bg = Color(0.38, 0.38, 0.38)
			fg = Color(0.3, 0.3, 0.3)    # Subtle internal grid
		"walls_outer", "wall", "#":
			bg = Color(0.2, 0.2, 0.2)
			fg = Color.DIM_GRAY
		"farms", "f":
			bg = Color(0.15, 0.22, 0.12)
			fg = Color(0.25, 0.4, 0.2)
		"fallow", "\"":
			# Noise-based dithering for fields
			var noise_val = abs(sin(pos.x * 0.77 + pos.y * 1.33))
			bg = Color(0.45, 0.35, 0.25)
			fg = bg.darkened(0.2) if noise_val > 0.5 else bg.lightened(0.1)
		"road", "=", "+":
			bg = Color(0.5, 0.45, 0.4)
			fg = Color(0.7, 0.65, 0.6)
		"docks":
			bg = Color(0.35, 0.22, 0.12) # Darker wood ground
			fg = Color(0.7, 0.55, 0.35)   # Lighter plank lines
		"water", "~":
			bg = Color(0.1, 0.2, 0.4)
			fg = Color(0.2, 0.4, 0.6)
		_:
			fg = bg.lightened(0.5) if bg.v < 0.5 else bg.darkened(0.5)

	return {"bg": bg, "fg": fg}

static func get_tile_info(gs, pos: Vector2i) -> String:
	if pos.x < 0 or pos.y < 0 or pos.y >= gs.height or pos.x >= gs.width:
		return "Out of bounds %v" % pos

	var t_char = gs.grid[pos.y][pos.x]
	var terrain_name = "Unknown"
	
	match t_char:
		"~", "≈", "/", "\\": terrain_name = "Water"
		".": terrain_name = "Plains"
		"#", "T", "&": terrain_name = "Forest / Woodland"
		"o": terrain_name = "Foothills"
		"O", "^": terrain_name = "Mountains"
		"S", "\"": terrain_name = "Savanna / Steppe"
		"*": terrain_name = "Tundra"
		"=": terrain_name = "Road"
		_: terrain_name = "Tile '%s'" % t_char

	var info = PackedStringArray()
	info.append("[b]Position:[/b] (%d, %d)\n[b]Terrain:[/b] %s" % [pos.x, pos.y, terrain_name])
	
	if gs.resources.has(pos):
		info.append("\n[color=yellow]Resource:[/color] %s" % gs.resources[pos])
	
	# Geology
	if gs.geology.has(pos):
		var geo = gs.geology[pos]
		info.append("\nTemp: %.2f | Rain: %.2f" % [geo.get("temp", 0.0), geo.get("rain", 0.0)])
	
	if gs.settlements.has(pos):
		var s = gs.settlements[pos]
		info.append("\n\n[color=cyan]Settlement: %s[/color]" % s.name)
		info.append("\nType: %s (Tier %d)" % [s.type, s.tier])
		info.append("\nPop: %d | Faction: %s" % [s.population, s.faction])
	
	# Check armies
	var armies_here = []
	for army in gs.armies:
		if Vector2i(army.pos) == pos:
			armies_here.append(army)
			
	if armies_here.size() > 0:
		info.append("\n\n[color=red]Armies (%d):[/color]" % armies_here.size())
		for a in armies_here:
			info.append("\n- %s (%d units)" % [a.name, a.roster.size()])

	# Check caravans
	var caravans_here = []
	for c in gs.caravans:
		if Vector2i(c.pos) == pos:
			caravans_here.append(c)
	if caravans_here.size() > 0:
		info.append("\n\n[color=green]Caravans: %d[/color]" % caravans_here.size())

	return "".join(info)

static func render_menu(options, idx, has_world, has_char) -> String:
	var parts = PackedStringArray()
	parts.append("[center][b]FALLING LEAVES[/b]\n\n")
	for i in range(options.size()):
		var prefix = " > " if i == idx else "   "
		var suffix = " < " if i == idx else "   "
		parts.append(prefix + options[i] + suffix + "\n")
	parts.append("\n[/center]")
	return "".join(parts)

static func render_battle_config(config, idx) -> String:
	var parts = PackedStringArray()
	parts.append("[center][b]BATTLE SIMULATOR CONFIG[/b][/center]\n\n")
	
	var max_entries = 4
	
	parts.append("[table=2]")
	parts.append("[cell][center][b]PLAYER TEAM[/b][/center][/cell]")
	parts.append("[cell][center][b]ENEMY TEAM[/b][/center][/cell]")
	
	var left_col = PackedStringArray()
	var right_col = PackedStringArray()
	
	for i in range(max_entries):
		# Player Entry (Left)
		var p_idx = i
		var p_data = config["p_lineup"][i] if i < config["p_lineup"].size() else {"type": "none", "cnt": 0, "lvl": 3}
		var p_color = "yellow" if idx == p_idx else "white"
		var p_pre = "> " if idx == p_idx else "  "
		if p_data["type"] == "none":
			left_col.append("[color=%s]%s[Empty Slot][/color]\n" % [p_color, p_pre])
		else:
			left_col.append("[color=%s]%s%s x%d (Lvl %d)[/color]\n" % [p_color, p_pre, p_data["type"].capitalize(), p_data["cnt"], p_data["lvl"]])
			
		# Enemy Entry (Right)
		var e_idx = i + max_entries
		var e_data = config["e_lineup"][i] if i < config["e_lineup"].size() else {"type": "none", "cnt": 0, "lvl": 3}
		var e_color = "yellow" if idx == e_idx else "white"
		var e_pre = "> " if idx == e_idx else "  "
		if e_data["type"] == "none":
			right_col.append("[color=%s]%s[Empty Slot][/color]\n" % [e_color, e_pre])
		else:
			right_col.append("[color=%s]%s%s x%d (Lvl %d)[/color]\n" % [e_color, e_pre, e_data["type"].capitalize(), e_data["cnt"], e_data["lvl"]])
			
	parts.append("[cell]%s[/cell]" % "".join(left_col))
	parts.append("[cell]%s[/cell]" % "".join(right_col))
	parts.append("[/table]\n\n")
	
	var start_idx = max_entries * 2
	var start_pre = " > " if idx == start_idx else "   "
	var start_col = "yellow" if idx == start_idx else "white"
	parts.append("[center][color=%s]%sSTART BATTLE[/color][/center]" % [start_col, start_pre])
	
	parts.append("\n\n[center][color=gray]Controls: WASD Navigate | 1 Type | A/D Count | Q/E Level[/color][/center]")
	
	return "".join(parts)

static func render_world_creation(config, idx) -> String:
	var parts = PackedStringArray()
	parts.append("[center][b]WORLD GENERATION[/b][/center]\n\n")
	var keys = config.keys()
	for i in range(keys.size()):
		var k = keys[i]
		var val = config[k]
		var prefix = " > " if i == idx else "   "
		parts.append("%s[b]%s:[/b] %s\n" % [prefix, k.capitalize(), str(val)])
	
	parts.append("\n")
	var start_prefix = " > " if idx == keys.size() else "   "
	parts.append("%s[b][ GENERATE WORLD ][/b]\n" % start_prefix)
	parts.append("[color=gray](Use Arrows to adjust, ENTER to Start)[/color][/center]")
	return "".join(parts)

static func render_city_studio(config, idx) -> String:
	var parts = PackedStringArray()
	parts.append("[center][b]CITY DESIGN STUDIO[/b][/center]\n\n")
	var keys = config.keys()
	for i in range(keys.size()):
		var k = keys[i]
		var val = config[k]
		var prefix = " > " if i == idx else "   "
		parts.append("%s[b]%s:[/b] %s\n" % [prefix, k.capitalize(), str(val)])
	
	var generate_idx = keys.size()
	var g_prefix = " > " if idx == generate_idx else "   "
	var g_color = "yellow" if idx == generate_idx else "white"
	parts.append("\n[center][color=%s]%s[ GENERATE SETTLEMENT ]%s[/color][/center]\n" % [g_color, g_prefix, g_prefix.reverse()])
	
	parts.append("\n[center][color=gray]W/S: Navigate | A/D: Change Values | ENTER: Build | ESC: Back[/color][/center]")
	return "".join(parts)

static func render_world_map(gs, vw=100, vh=50, center_override=null) -> Array:
	var center = center_override
	if center == null:
		if gs.player:
			center = gs.player.pos + gs.player.camera_offset
		else:
			center = Vector2i(gs.width / 2, gs.height / 2)
			
	var start_x = center.x - (vw / 2)
	var start_y = center.y - (vh / 2)
	var end_x = start_x + vw
	var end_y = start_y + vh
	
	# Optimization: Use Spatial Hash for entities in world map
	var visible_entities_dict = {}
	var margin = 2
	for cell_y in range((start_y-margin)/gs.SPATIAL_CELL_SIZE, (end_y+margin)/gs.SPATIAL_CELL_SIZE + 1):
		for cell_x in range((start_x-margin)/gs.SPATIAL_CELL_SIZE, (end_x+margin)/gs.SPATIAL_CELL_SIZE + 1):
			var cell = Vector2i(cell_x, cell_y)
			if gs.spatial_grid.has(cell):
				for ent in gs.spatial_grid[cell]:
					var e_pos = ent.pos if "pos" in ent else Vector2i(-1, -1)
					if e_pos.x >= start_x and e_pos.x < end_x and e_pos.y >= start_y and e_pos.y < end_y:
						visible_entities_dict[e_pos] = ent

	var lines = []
	for y in range(vh):
		var line_parts = PackedStringArray()
		var grid_y = start_y + y
		
		# Skip invalid Y rows (optimize loop)
		if grid_y < 0 or grid_y >= gs.height:
			line_parts.append(" ".repeat(vw))
			lines.append("".join(line_parts))
			continue
			
		for x in range(vw):
			var grid_x = start_x + x
			if grid_x < 0 or grid_x >= gs.width:
				line_parts.append(" ")
				continue
				
			# Check for overlays first (Units, Settlements)
			var pos = Vector2i(grid_x, grid_y)
			var terrain_char = gs.grid[grid_y][grid_x]
			var terrain_color_obj = _get_terrain_color(gs, pos, terrain_char, "world")
			var terrain_color = _c_to_bb(terrain_color_obj)
			var char_rendered = false
			
			if gs.map_mode == "terrain":
				# Settlements Priority
				if gs.settlements.has(pos):
					var s = gs.settlements[pos]
					var col = "cyan"
					var sym = "v"
					
					match s.type:
						"metropolis":
							col = "gold"
							sym = "M"
						"city":
							col = "orange"
							sym = "C"
						"town":
							col = "#00FFFF"
							sym = "T"
						"village":
							col = "cyan"
							sym = "v"
						"hamlet":
							col = "#ADD8E6"
							sym = "h"
						"castle":
							col = "red"
							sym = "S"
						_:
							col = "white"
							sym = "v"
							
					if s.type == "town" or gs.render_mode == "grid": 
						sym = "[b]" + sym + "[/b]"

					var set_str = "[color=%s]%s[/color]" % [col, sym]
					line_parts.append(_wrap_grid(gs, terrain_color_obj, set_str))
					char_rendered = true
					
				# Armies / Player / Caravans
				elif gs.player and pos == gs.player.pos:
					var p_str = "[color=yellow]@[/color]"
					line_parts.append(_wrap_grid(gs, terrain_color_obj, p_str))
					char_rendered = true
				elif visible_entities_dict.has(pos):
					var ent = visible_entities_dict[pos]
					var e_char = "?"
					var e_col = "white"
					if ent.type == "army":
						e_char = "A"
						e_col = "red" if ent.faction == "bandits" else "white"
					elif ent.type == "caravan":
						e_char = "C"
						e_col = "gold"
					var ent_str = "[color=%s]%s[/color]" % [e_col, e_char]
					line_parts.append(_wrap_grid(gs, terrain_color_obj, ent_str))
					char_rendered = true

			elif gs.map_mode == "resource":
				if gs.resources.has(pos):
					var res = gs.resources[pos]
					var r_sym = "$"
					var r_col = "gold"
					
					match res:
						"iron": 
							r_sym = "I"
							r_col = "gray"
						"coal":
							r_sym = "c"
							r_col = "dark_gray"
						"wood":
							r_sym = "T"
							r_col = "saddle_brown"
						"game":
							r_sym = "g"
							r_col = "pink"
						"salt":
							r_sym = ":"
							r_col = "white"
						"horses":
							r_sym = "h"
							r_col = "peru"
						"stone":
							r_sym = "o"
							r_col = "light_gray"
						"gold":
							r_sym = "$"
							r_col = "gold"
						"gems":
							r_sym = "*"
							r_col = "magenta"
						"spices":
							r_sym = "S"
							r_col = "orange_red"
						"ivory":
							r_sym = "i"
							r_col = "ivory"
						"furs":
							r_sym = "f"
							r_col = "tan"
						"clay":
							r_sym = "="
							r_col = "rosy_brown"
						"peat":
							r_sym = "p"
							r_col = "saddle_brown"
							
					var res_str = "[color=%s]%s[/color]" % [r_col, r_sym]
					line_parts.append(_wrap_grid(gs, terrain_color_obj, res_str))
					char_rendered = true

			elif gs.map_mode == "province":
				# Render Province ID Colors
				if gs.province_grid.size() > grid_y and gs.province_grid[grid_y].size() > grid_x:
					var p_id = gs.province_grid[grid_y][grid_x]
					if p_id != -1:
						var p_colors = ["red", "blue", "green", "yellow", "purple", "orange", "cyan", "magenta", "pink", "teal", "lime_green", "indigo", "salmon", "sky_blue", "khaki"]
						var p_col = p_colors[p_id % p_colors.size()]
						var t_char = gs.grid[grid_y][grid_x]
						if gs.render_mode == "grid": t_char = " "
						
						var prov_str = "[color=%s]%s[/color]" % [p_col, t_char]
						line_parts.append(_wrap_grid(gs, terrain_color_obj, prov_str))
						char_rendered = true

			elif gs.map_mode == "political":
				# Render Faction Colors
				if gs.province_grid.size() > grid_y and gs.province_grid[grid_y].size() > grid_x:
					var p_id = gs.province_grid[grid_y][grid_x]
					if p_id != -1:
						var pol_colors = ["crimson", "royal_blue", "gold", "forest_green", "indigo", "white", "black", "orange_red", "spring_green", "deep_sky_blue"]
						var owner_col = pol_colors[p_id % pol_colors.size()]
						var t_char = gs.grid[grid_y][grid_x]
						if gs.render_mode == "grid": t_char = " "
						if gs.settlements.has(pos):
							t_char = "O" if gs.render_mode == "ascii" else "▣"
							owner_col = "white" # Highlight cities
						
						var pol_str = "[color=%s]%s[/color]" % [owner_col, t_char]
						line_parts.append(_wrap_grid(gs, terrain_color_obj, pol_str))
						char_rendered = true
			
			if not char_rendered:
				# Terrain
				var t = gs.grid[grid_y][grid_x]
				
				# Grid Rendering Mode
				if gs.render_mode == "grid" and t != " ":
					var filled_sym = " "
					line_parts.append(_wrap_grid(gs, terrain_color_obj, filled_sym))
				elif terrain_color_obj != Color.WHITE:
					line_parts.append("[color=%s]%s[/color]" % [terrain_color, t])
				else:
					line_parts.append(t)
					
		lines.append("".join(line_parts))
		
	return lines

static func get_world_stats(gs) -> Array:
	return [
		"Size: %dx%d" % [gs.width, gs.height],
		"Factions: %d" % gs.factions.size(),
		"Settlements: %d" % gs.settlements.size()
	]

static func get_master_frame(gs, map_lines, side_lines, header, vw, log_msgs=[], log_height=5, log_offset=0) -> Dictionary:
	var map_text = ""
	if map_lines is Array or map_lines is PackedStringArray:
		map_text = "\n".join(map_lines)
	else:
		map_text = str(map_lines)
		
	var side_text = ""
	if side_lines is Array or side_lines is PackedStringArray:
		side_text = "\n".join(side_lines)
	else:
		side_text = str(side_lines)
		
	var log_text = ""
	if log_msgs is Array:
		# Simple log rendering
		var start = max(0, log_msgs.size() - log_height - log_offset)
		var end = min(log_msgs.size() - log_offset, log_msgs.size())
		for i in range(start, end):
			log_text += log_msgs[i] + "\n"
	else:
		log_text = str(log_msgs)

	return {
		"map": map_text,
		"side": side_text,
		"log": log_text,
		"header": header,
		"text": map_text # Fallback
	}

static func render_character_creation_tabbed(p_conf, p_idx, t_idx, pts, tab, shop_items, purchases, crowns, mat_name, qual_name) -> String:
	var tabs = ["1: Background", "2: Stats & Traits", "3: Loadout", "4: Summary"]
	var parts = PackedStringArray()
	parts.append("[center][b]CHARACTER CREATION[/b]\n")
	for i in range(tabs.size()):
		if i == tab: parts.append("[color=green]%s[/color]   " % tabs[i])
		else: parts.append("%s   " % tabs[i])
	parts.append("[/center]\n\n")
	
	var content = PackedStringArray()
	
	if tab == 0:
		content.append("Name: %s\n" % p_conf["name"])
		content.append("Points Remaining: %d\n\n" % pts)
		content.append("[b]Scenario:[/b] (determines starting resources)\n")
		var scenarios = ["trader_caravan", "lone_survivor", "noble_exile", "bandit_warlord"]
		for i in range(scenarios.size()):
			var s = scenarios[i]
			var pre = " > " if p_idx == i else "   " # Assumes p_idx tracks scenario list in this mode
			var mark = "[x]" if p_conf["scenario"] == s else "[ ]"
			content.append("%s%s %s\n" % [pre, mark, s.capitalize().replace("_", " ")])
		
		content.append("\n[b]Profession:[/b] (determines skill bonuses)\n")
		var profs = ["mercenary", "merchant", "blacksmith", "hunter", "scholar"]
		for i in range(profs.size()):
			var p = profs[i]
			var pre = " > " if p_idx == (i + scenarios.size()) else "   "
			var mark = "[x]" if p_conf["profession"] == p else "[ ]"
			content.append("%s%s %s\n" % [pre, mark, p.capitalize()])
			
	elif tab == 1:
		content.append("[b]Attributes[/b] (Cost 5 pts each)\n")
		var stats = ["strength", "agility", "endurance", "intelligence"]
		for i in range(stats.size()):
			var s = stats[i]
			var val = p_conf[s]
			var pre = " > " if i == p_idx else "   " 
			content.append("%s%s: %d\n" % [pre, s.capitalize(), val])
			
		content.append("\n[b]Traits[/b] (Cost varies)\n")
		var traits = ["strong", "quick", "tough", "brilliant", "charismatic", "brave"]
		for i in range(traits.size()):
			var t = traits[i]
			var has = p_conf["traits"].has(t)
			var pre = " > " if (i == t_idx and p_idx >= stats.size()) else "   "
			var check = "[x]" if has else "[ ]"
			content.append("%s%s %s\n" % [pre, check, t.capitalize()])
			
	elif tab == 2:
		content.append("Start Gold: %d\n" % crowns)
		content.append("Material (Q/E): %s | Quality (Z/C): %s\n\n" % [mat_name.capitalize(), qual_name.capitalize()])
		
		var half = (shop_items.size() + 1) / 2
		for i in range(half):
			var item1 = shop_items[i]
			var pre1 = " > " if i == p_idx else "   "
			var col1 = "%s%s" % [pre1, item1.capitalize()]
			
			var col2 = ""
			if i + half < shop_items.size():
				var item2 = shop_items[i + half]
				var pre2 = " > " if (i + half) == p_idx else "   "
				col2 = "%s%s" % [pre2, item2.capitalize()]
			
			content.append("%-35s %s\n" % [col1, col2])
			
		content.append("\n[b]Cart Contents:[/b]\n")
		if purchases.is_empty():
			content.append(" (Empty)")
		else:
			for p in purchases:
				content.append("- %s %s %s\n" % [p.qual.capitalize(), p.mat.capitalize(), p.id.capitalize()])
				
	elif tab == 3:
		content.append("\nName: %s\n" % p_conf["name"])
		content.append("Scenario: %s\n" % p_conf["scenario"].capitalize().replace("_", " "))
		content.append("Profession: %s\n" % p_conf["profession"].capitalize())
		content.append("\nStats: STR %d | AGI %d | END %d | INT %d\n" % [p_conf["strength"], p_conf["agility"], p_conf["endurance"], p_conf["intelligence"]])
		content.append("Traits: %s\n" % ", ".join(p_conf["traits"]))
		content.append("Items: %d\n" % purchases.size())
		content.append("\n[center]Ready to embark?\nPress ENTER to start your adventure![/center]")
	
	parts.append("".join(content))
	return "".join(parts)

static func render_location_select(gs, loc) -> String:
	return "[center]Select Starting Location:\n%s[/center]" % str(loc)

static func render_loading_screen(status: String, gs = null) -> Dictionary:
	var header = "[center][b]GENERATING WORLD[/b][/center]"
	var side = "[b]PROGRESS[/b]\n\n" + status
	
	var map = ""
	if gs and gs.grid.size() > 0:
		# Show a large preview of the world being built
		# We use a larger width to avoid the "small block" look on wide screens
		map = "\n" + render_minimap(gs, 140, 50)
	else:
		map = "\n\n\n\n[center][color=gray]... Procedural Matrix Initializing ...[/color][/center]"
	
	return {"header": header, "map": map, "side": side}

static func render_world_preview(gs, preview_pos) -> Dictionary:
	var header = "[center][b]WORLD PREVIEW[/b] - Press [b]ENTER[/b] to Start | [b]R[/b] to Re-roll[/center]"
	var side = "[b]WORLD DATA[/b]\n\n"
	side += "Size: %dx%d\n" % [gs.width, gs.height]
	side += "Seed: %s\n" % str(gs.seed_value if "seed_value" in gs else "Unknown")
	side += "\n[b]CONTROLS[/b]\n"
	side += "Arrows: Scroll\n"
	side += "Zoom: +/-"
	
	# The map itself is rendered via TileMap in Main.gd, so we just return a frame
	return {"header": header, "map": "", "side": side}

static func render_minimap(gs, w, h) -> String:
	# Downscale grid to fit fit in w x h
	var sample_rate_x = max(1, gs.width / w)
	var sample_rate_y = max(1, gs.height / h)
	
	var lines = []
	for y in range(h):
		var line = ""
		for x in range(w):
			var sx = x * sample_rate_x
			var sy = y * sample_rate_y
			
			if sy >= gs.grid.size() or sx >= gs.grid[0].size():
				line += " "
				continue
				
			var t = gs.grid[sy][sx]
			var sym = "."
			var col = _get_terrain_color(gs, Vector2i(sx, sy), t)
			
			match t:
				"~", "≈", "/", "\\": sym = "~"
				"#", "T", "&": sym = "T"
				"^", "O", "o", "A", "V": sym = "^"
				"\"", "S": sym = "\""
				_: sym = t[0] if t.length() > 0 else " "
			
			if gs.render_mode == "grid" and not gs.get("graphical_mode_active"):
				sym = "█"
				
			var col_str = _c_to_bb(col)
			line += "[color=%s]%s[/color]" % [col_str, sym]
		
		lines.append("[center]%s[/center]" % line)
		
	return "\n".join(lines)

static func get_management_screen(gs, tab, focus, idx_l, idx_r, designing, design_slot, design_prop) -> String:
	var s = "[center][b]COMMODORE MANAGEMENT[/b][/center]\n"
	s += "[center][color=gray]"
	var tabs = ["CHARACTER", "TRAINING", "ROSTER", "MARKET", "RECRUIT"]
	if not gs.player.fief_ids.is_empty(): tabs.append("FIEF")
	tabs.append_array(["TRADE", "WORLD", "OFFICE", "SQUARE"])
	
	for t in tabs:
		if t == tab: s += "[b][color=white][%s][/color][/b] " % t
		else: s += "%s " % t
	s += "[/color][/center]\n\n"
	
	if tab == "CHARACTER":
		var p = gs.player
		var u = p.commander
		if not u: return s + "No commander data."
		
		# -- Left Column: Stats --
		var focus_col_l = "yellow" if focus == 0 else "white"
		var left = "[color=%s][b]%s[/b] (Lvl %d)[/color]\nHP: %d/%d\nXP: %d\n" % [focus_col_l, u.name, u.level, u.hp, u.hp_max, u.xp]
		left += "STR: %d  INT: %d\nAGI: %d  CHA: %d\nEND: %d\n" % [u.attributes.strength, u.attributes.intelligence, u.attributes.agility, 10, u.attributes.endurance]
		left += "\n[b]Skills[/b]\n"
		var skill_subset = ["swordsmanship", "archery", "shield_use", "armor_handling", "spear_use"]
		for sk in skill_subset:
			left += "- %s: %d\n" % [sk.capitalize(), u.skills.get(sk, 0)]
		
		# -- Right Column: Incventory/Equipment --
		var focus_col_r = "yellow" if focus == 1 else "white"
		var right = "[color=%s][b]Equipment[/b][/color]\n" % focus_col_r
		var slots = ["head", "torso", "l_hand", "r_hand", "l_arm", "r_arm", "l_leg", "r_leg", "l_foot", "r_foot"]
		for i in range(slots.size()):
			var sl = slots[i]
			var item = u.equipment.get(sl, {})
			var name_str = "Empty"
			if item:
				if item.has("over"): name_str = item["over"].id if item["over"] else "Empty"
				elif item.has("under"): name_str = item["under"].id if item["under"] else "Empty"
			
			var pre = " > " if (focus == 1 and idx_r == i) else "   "
			var col = "white"
			if focus == 1 and idx_r == i: col = "yellow"
			right += "[color=%s]%s%s: %s[/color]\n" % [col, pre, sl.capitalize(), name_str]
			
		s += "[table=2]\n[cell]" + left + "[/cell]\n[cell]" + right + "[/cell]\n[/table]"
	
	elif tab == "TRAINING":
		var u = gs.player.commander
		var focus_col_l = "yellow" if focus == 0 else "white"
		var left = "[color=%s][b]ATTRIBUTES[/b][/color] (Points: %d)\n" % [focus_col_l, u.stat_points]
		var attr_keys = u.attributes.keys()
		for i in range(attr_keys.size()):
			var ak = attr_keys[i]
			var pre = " > " if (focus == 0 and idx_l == i) else "   "
			var col = "yellow" if (focus == 0 and idx_l == i) else "white"
			left += "[color=%s]%s%s: %d[/color]\n" % [col, pre, ak.capitalize(), u.attributes[ak]]
		
		var focus_col_r = "cyan" # Skills usually secondary
		var right = "[b]SKILLS[/b] (Points: %d)\n" % u.skill_points
		var sk_keys = u.skills.keys()
		# Only show first few to fit screen, or use table
		for i in range(sk_keys.size()):
			var sk = sk_keys[i]
			var level = u.skills[sk]
			var color = "white"
			if focus == 0 and idx_l == (i + attr_keys.size()): color = "yellow"
			var pre = " > " if (focus == 0 and idx_l == (i + attr_keys.size())) else "   "
			right += "[color=%s]%s%s: %d[/color]\n" % [color, pre, sk.capitalize().replace("_", " "), level]
			
		s += "[table=2]\n[cell]%s[/cell][cell]%s[/cell][/table]" % [left, right]

	elif tab == "RECRUIT":
		var s_pos = gs.player.pos
		var set = gs.settlements.get(s_pos)
		s += "[b]RECRUITMENT HUB[/b] | Tavern & Prisons\n"
		
		var left = "[b]Available Recruits[/b]\n"
		if not set or set.recruit_pool.is_empty():
			left += " No recruits at this location.\n"
		else:
			for i in range(set.recruit_pool.size()):
				var u = set.recruit_pool[i]
				var pre = " > " if (focus == 0 and idx_l == i) else "   "
				var col = "yellow" if (focus == 0 and idx_l == i) else "white"
				left += "[color=%s]%s%s (Lvl %d) - %d Crowns[/color]\n" % [col, pre, u.name, u.level, u.cost]
		
		var right = "[b]Prisoners[/b]\n"
		if gs.player.prisoners.is_empty():
			right += " No captives in the train.\n"
		else:
			for i in range(gs.player.prisoners.size()):
				var u = gs.player.prisoners[i]
				var pre = " > " if (focus == 1 and idx_r == i) else "   "
				var col = "yellow" if (focus == 1 and idx_r == i) else "white"
				right += "[color=%s]%s%s (%s)[/color]\n" % [col, pre, u.name, u.type.capitalize()]
		
		s += "[table=2]\n[cell]%s[/cell][cell]%s[/cell][/table]" % [left, right]
		s += "\n[color=gray]H: Hire | R: Ransom | U: Recruit from Prisoners[/color]"

	elif tab == "TRADE":
		var s_pos = gs.player.pos
		var set = gs.settlements.get(s_pos)
		if not set: return s + "[center]\nYou are not at a settlement.[/center]"
		
		s += "[b]COMMODITY EXCHANGE: %s[/b]\n" % set.name
		var table = "[table=4]"
		table += "[cell][b]Local Resource[/b][/cell][cell][b]Local Stock[/b][/cell][cell][b]Local Price[/b][/cell][cell][b]Your Inventory[/b][/cell]"
		
		var resources = ["grain", "fish", "meat", "wood", "stone", "iron", "horses", "wool", "ale", "tools"]
		for i in range(resources.size()):
			var res = resources[i]
			var stock = set.inventory.get(res, 0)
			var price = 10 # Hardcoded for now
			var player_amt = gs.player.inventory.get(res, 0)
			
			var row_col = "yellow" if (focus == 0 and idx_l == i) else "white"
			var pre = " > " if (focus == 0 and idx_l == i) else "   "
			
			table += "[cell][color=%s]%s%s[/color][/cell]" % [row_col, pre, res.capitalize()]
			table += "[cell][color=%s]%d[/color][/cell]" % [row_col, stock]
			table += "[cell][color=%s]%d[/color][/cell]" % [row_col, price]
			table += "[cell][color=%s]%d[/color][/cell]" % [row_col, player_amt]
		
		s += table + "[/table]"
		s += "\n[color=gray]ENTER: Buy / Sell selected resource (10 units)[/color]"

	elif tab == "SQUARE":
		var s_pos = gs.player.pos
		var set = gs.settlements.get(s_pos)
		if not set: return s + "[center]\nYou are not at a settlement.[/center]"
		
		s += "[b]TOWN SQUARE: %s[/b]\n" % set.name
		var left = "[b]Notable People[/b]\n"
		if set.npcs.is_empty():
			left += " No NPCs present.\n"
		else:
			for i in range(set.npcs.size()):
				var npc = set.npcs[i]
				var pre = " > " if (focus == 0 and idx_l == i) else "   "
				var col = "yellow" if (focus == 0 and idx_l == i) else "white"
				left += "[color=%s]%s%s (%s)[/color]\n" % [col, pre, npc.name, npc.title.capitalize()]
		
		var right = "[b]Rumors & Quests[/b]\n"
		# Logic to show quests for focused NPC
		right += " No active assignments.\n"
		
		s += "[table=2]\n[cell]%s[/cell][cell]%s[/cell][/table]" % [left, right]
		s += "\n[color=gray]ENTER: Interact with NPCs[/color]"

	elif tab == "FIEF":
		var s_pos = gs.player.pos
		var active_s = gs.settlements.get(s_pos)
		if active_s and active_s.pos in gs.player.fief_ids:
			s += get_fief_info_screen(gs, active_s)
		else:
			s += "[center]\nYour Fiefs:\n"
			for f_pos in gs.player.fief_ids:
				var fief = gs.settlements.get(f_pos)
				if fief:
					s += " - %s (%s)\n" % [fief.name, fief.type.capitalize()]
			s += "\n[color=gray]Note: Visit a fief to manage it deeply.[/color][/center]"

	elif tab == "ROSTER":
		s += "Active Party: %d / %d   |   Total Strength: %d\n\n" % [gs.get_party_size(), 100, gs.player.strength]
		var table = "[table=5]"
		table += "[cell][b]Idx[/b][/cell][cell][b]Name[/b][/cell][cell][b]Type[/b][/cell][cell][b]Lvl[/b][/cell][cell][b]Status[/b][/cell]"
		for i in range(gs.player.roster.size()):
			var u = gs.player.roster[i]
			var pre = " > " if (focus == 0 and idx_l == i) else "   "
			var status = "OK"
			var stat_col = "green"
			if u.hp < u.hp_max * 0.3: 
				status = "Wounded"
				stat_col = "orange"
			if u.hp <= 0: 
				status = "Dead"
				stat_col = "red"
			
			var row_col = "yellow" if (focus == 0 and idx_l == i) else "white"
			table += "[cell][color=%s]%s%d[/color][/cell]" % [row_col, pre, i]
			table += "[cell][color=%s]%s[/color][/cell]" % [row_col, u.name]
			table += "[cell][color=%s]%s[/color][/cell]" % [row_col, u.type.capitalize()]
			table += "[cell][color=%s]%d[/color][/cell]" % [row_col, u.level]
			table += "[cell][color=%s]%s[/color][/cell]" % [stat_col, status]
		s += table + "[/table]"
			
	elif tab == "MARKET":
		var s_pos = gs.player.pos
		var set = gs.settlements.get(s_pos)
		var gold_col = "white"
		if gs.player.gold < 100: gold_col = "red"
		s += "Crowns: [color=%s]%d[/color] | Total Weight: %d/%d kg\n\n" % [gold_col, gs.player.gold, int(gs.get_total_weight()), int(gs.get_max_weight())]
		
		var left = "[b]Your Inventory[/b]\n"
		var inv_table = "[table=2]"
		var inv_keys = gs.player.inventory.keys()
		var active_keys = []
		for k in inv_keys: 
			if gs.player.inventory[k] > 0: active_keys.append(k)
		for i in range(active_keys.size()):
			var k = active_keys[i]
			var v = gs.player.inventory[k]
			var pre = " > " if (focus == 0 and idx_l == i) else "   "
			var col = "yellow" if (focus == 0 and idx_l == i) else "white"
			inv_table += "[cell][color=%s]%s%s[/color][/cell][cell][color=%s]%d[/color][/cell]" % [col, pre, k.capitalize(), col, v]
		left += inv_table + "[/table]"
		
		var right = "[b]Local Shop[/b]\n"
		if not set:
			right += " No shop in the wilderness."
		else:
			var shop_table = "[table=2]"
			for i in range(set.shop_inventory.size()):
				var item = set.shop_inventory[i]
				var pre = " > " if (focus == 1 and idx_r == i) else "   "
				var col = "yellow" if (focus == 1 and idx_r == i) else "white"
				shop_table += "[cell][color=%s]%s%s[/color][/cell][cell][color=%s]%d[/color][/cell]" % [col, pre, item.get("id", "Item"), col, item.get("price", 10)]
			right += shop_table + "[/table]"
			
		s += "[table=2]\n[cell]%s[/cell][cell]%s[/cell][/table]" % [left, right]
		s += "\n[color=gray]LEFT/RIGHT: Switch Focus | ENTER: Buy/Sell[/color]"
			
	elif tab == "OFFICE":
		s += "[b]Founding Permits:[/b] %d\n" % gs.player.charters
		s += "[b]Fame:[/b] %d\n\n" % gs.player.fame
		s += "[b]Active Contracts[/b]\n"
		if gs.player.active_contract.is_empty():
			s += " - No current mercenary contracts.\n"
		else:
			var c = gs.player.active_contract
			s += " - Serving: %s\n" % c.get("faction_id", "Unknown")
			s += " - Wage: %d per day\n" % c.get("daily_wage", 0)
			s += " - Expires: Day %d\n" % c.get("expires_day", 0)
			
		s += "\n[b]Fiefs List[/b]\n"
		for pos in gs.player.fief_ids:
			var set = gs.settlements.get(pos)
			if set:
				s += " - %s (%s) | Pop: %d\n" % [set.name, set.type.capitalize(), set.population]

	elif tab == "WORLD":
		s += "[b]REALM LOGISTICS & GLOBAL MARKET[/b]\n\n"
		var left = "[b]Market Orders[/b]\n"
		if gs.get("world_market_orders", []).is_empty():
			left += " No active global trade orders.\n"
		else:
			for i in range(min(gs.world_market_orders.size(), 20)):
				var o = gs.world_market_orders[i]
				var pre = " > " if (focus == 0 and idx_l == i) else "   "
				var col = "yellow" if (focus == 0 and idx_l == i) else "white"
				left += "[color=%s]%s%s (%d) @ %d[/color]\n" % [col, pre, o.resource.capitalize(), o.amount, o.price]
		
		var right = "[b]Logistical Pulses[/b]\n"
		if gs.get("logistical_pulses", []).is_empty():
			right += " No pulses in transit.\n"
		else:
			for i in range(min(gs.logistical_pulses.size(), 20)):
				var p = gs.logistical_pulses[i]
				var pre = " > " if (focus == 1 and idx_r == i) else "   "
				var col = "yellow" if (focus == 1 and idx_r == i) else "white"
				right += "[color=%s]%s%s -> %v[/color]\n" % [col, pre, p.resource.capitalize(), p.target_pos]
		
		s += "[table=2]\n[cell]%s[/cell][cell]%s[/cell][/table]" % [left, right]

	return s

static func render_history(gs, offset) -> String:
	var s = "[center][b]WORLD CHRONICLE[/b][/center]\n"
	s += "[center][color=gray]Latest events across the realm[/color][/center]\n\n"
	
	var logs = gs.event_log
	var page_size = 30
	var start = max(0, logs.size() - offset - page_size)
	var end = max(0, logs.size() - offset)
	
	if logs.is_empty():
		s += "[center]No history recorded yet.[/center]"
	else:
		# Show in reverse chronological order
		for i in range(end - 1, start - 1, -1):
			s += "> %s\n" % logs[i]
			
	s += "\n[center][color=gray]Page Up/Down: Scroll | ESC: Close[/color][/center]"
	return s

static func _format_run(gs, bg: Color, fg_hex: String, text: String) -> String:
	if text == "": return ""
	var s = "[color=%s]%s[/color]" % [fg_hex, text]
	if gs.render_mode == "grid" and not gs.get("graphical_mode_active"):
		return "[bgcolor=%s]%s[/bgcolor]" % [_c_to_bb(bg), s]
	return s

static func render_city(gs, city_ctrl, vw, vh) -> Array:
	if not city_ctrl.grid:
		return ["Generating City Layout..."]
		
	var cam_x = city_ctrl.player_pos.x
	var cam_y = city_ctrl.player_pos.y
	
	var lines = []
	for y in range(vh):
		var line_parts = PackedStringArray()
		var grid_y = cam_y - vh/2 + y
		
		if grid_y < 0 or grid_y >= city_ctrl.height:
			line_parts.append(" ".repeat(vw))
			lines.append("".join(line_parts))
			continue
			
		var cur_bg = Color.TRANSPARENT
		var cur_fg_hex = ""
		var cur_text = ""
		
		for x in range(vw):
			var grid_x = cam_x - vw/2 + x
			if grid_x < 0 or grid_x >= city_ctrl.width:
				if cur_text != "":
					line_parts.append(_format_run(gs, cur_bg, cur_fg_hex, cur_text))
					cur_text = ""
					cur_bg = Color.TRANSPARENT
					cur_fg_hex = ""
				line_parts.append(" ")
				continue
				
			var pos = Vector2i(grid_x, grid_y)
			var t = city_ctrl.grid[grid_y][grid_x]
			
			# PLAYER
			if grid_x == city_ctrl.player_pos.x and grid_y == city_ctrl.player_pos.y:
				if cur_text != "":
					line_parts.append(_format_run(gs, cur_bg, cur_fg_hex, cur_text))
					cur_text = ""
				
				var colors = _get_tile_colors(gs, pos, t, "local")
				line_parts.append(_wrap_grid(gs, colors.bg, "[color=yellow]@[/color]"))
				cur_bg = Color.TRANSPARENT # Reset
				cur_fg_hex = ""
				continue
			
			var colors = _get_tile_colors(gs, pos, t, "local")
			var bg = colors.bg
			var fg_hex = _c_to_bb(colors.fg)
			var display_sym = t
			
			if gs.render_mode == "grid":
				var is_bg_only = t in [".", "+", ",", "\"", "f", "~", "urban_block", "fallow", "farms", "docks"]
				if is_bg_only: display_sym = " "

			if cur_text == "":
				cur_bg = bg
				cur_fg_hex = fg_hex
				cur_text = display_sym
			elif bg == cur_bg and fg_hex == cur_fg_hex:
				cur_text += display_sym
			else:
				line_parts.append(_format_run(gs, cur_bg, cur_fg_hex, cur_text))
				cur_bg = bg
				cur_fg_hex = fg_hex
				cur_text = display_sym
				
		if cur_text != "":
			line_parts.append(_format_run(gs, cur_bg, cur_fg_hex, cur_text))
			
		lines.append("".join(line_parts))
	return lines

static func _get_dungeon_color(t: String) -> Color:
	match t:
		"#": return Color.DIM_GRAY
		".": return Color.GRAY
		"+": return Color.SADDLE_BROWN
		"=": return Color.SLATE_GRAY
		">", "<": return Color.MAGENTA
		"~": return Color.BLUE
		"\"": return Color.DARK_GREEN
		"f": return Color.FOREST_GREEN
		",": return Color.DARK_OLIVE_GREEN
		"B": return Color.CHOCOLATE
		"S": return Color.MAROON
		"M": return Color.GOLDENROD
		"K": return Color.DARK_SLATE_GRAY
		"G": return Color.SADDLE_BROWN
		"D": return Color.PERU
		"T": return Color.DARK_GREEN
		"X", "C": return Color.BURLYWOOD
	return Color.GRAY

static func render_dungeon(gs, dungeon_ctrl, vw, vh) -> Array:
	if not dungeon_ctrl.grid:
		return ["Generating Dungeon..."]
		
	var cam_x = dungeon_ctrl.player_pos.x
	var cam_y = dungeon_ctrl.player_pos.y
	
	var dungeon_entities = {}
	for e in dungeon_ctrl.enemies:
		if e.hp > 0: dungeon_entities[e.pos] = e
			
	var dungeon_items = {}
	for itm in dungeon_ctrl.items:
		dungeon_items[itm.pos] = itm
		
	var lines = []
	for y in range(vh):
		var line_parts = PackedStringArray()
		var grid_y = cam_y - vh/2 + y
		
		if grid_y < 0 or grid_y >= dungeon_ctrl.height:
			line_parts.append(" ".repeat(vw))
			lines.append("".join(line_parts))
			continue
			
		var cur_bg = Color.TRANSPARENT
		var cur_fg_hex = ""
		var cur_text = ""
		
		for x in range(vw):
			var grid_x = cam_x - vw/2 + x
			if grid_x < 0 or grid_x >= dungeon_ctrl.width:
				if cur_text != "":
					line_parts.append(_format_run(gs, cur_bg, cur_fg_hex, cur_text))
					cur_text = ""
					cur_bg = Color.TRANSPARENT
					cur_fg_hex = ""
				line_parts.append(" ")
				continue
				
			# Fog of War Check
			if not dungeon_ctrl.fog_of_war[grid_y][grid_x]:
				if cur_text != "":
					line_parts.append(_format_run(gs, cur_bg, cur_fg_hex, cur_text))
					cur_text = ""
				line_parts.append(" ")
				continue
				
			var pos = Vector2i(grid_x, grid_y)
			
			# BREAKERS (Player, Enemy, Item)
			if pos == dungeon_ctrl.player_pos or dungeon_entities.has(pos) or dungeon_items.has(pos):
				if cur_text != "":
					line_parts.append(_format_run(gs, cur_bg, cur_fg_hex, cur_text))
					cur_text = ""
				
				var t_char = dungeon_ctrl.grid[grid_y][grid_x]
				var terrain_color_obj = _get_dungeon_color(t_char)
				
				if pos == dungeon_ctrl.player_pos:
					line_parts.append(_wrap_grid(gs, terrain_color_obj, "[color=yellow]@[/color]"))
				elif dungeon_entities.has(pos):
					var e = dungeon_entities[pos]
					var e_str = "[color=red]%s[/color]" % e.type.left(1).to_upper()
					line_parts.append(_wrap_grid(gs, terrain_color_obj, e_str))
				elif dungeon_items.has(pos):
					line_parts.append(_wrap_grid(gs, terrain_color_obj, "[color=gold]$[/color]"))
				
				cur_bg = Color.TRANSPARENT
				cur_fg_hex = ""
				continue
			
			# TERRAIN
			var t = dungeon_ctrl.grid[grid_y][grid_x]
			var bg = _get_dungeon_color(t)
			var fg_hex = _c_to_bb(bg)
			var display_sym = t
			
			if gs.render_mode == "grid" and t in ["#", ".", "~"]:
				display_sym = " "
			
			if cur_text == "":
				cur_bg = bg
				cur_fg_hex = fg_hex
				cur_text = display_sym
			elif bg == cur_bg and fg_hex == cur_fg_hex:
				cur_text += display_sym
			else:
				line_parts.append(_format_run(gs, cur_bg, cur_fg_hex, cur_text))
				cur_bg = bg
				cur_fg_hex = fg_hex
				cur_text = display_sym
				
		if cur_text != "":
			line_parts.append(_format_run(gs, cur_bg, cur_fg_hex, cur_text))
			
		lines.append("".join(line_parts))
	return lines

static func get_side_panel(gs, pos_context = null) -> Array:
	var lines = []
	
	# 1. Calendar & Global Stats
	if gs.player:
		lines.append("[b][color=yellow]" + gs.get_date_string() + "[/color][/b]")
		lines.append("Crowns: %d | Provisions: %d" % [gs.player.crowns, gs.player.provisions])
	
		lines.append("----------------------------")
		
		# 2. Tile Info (If contextual position provided)
		if pos_context is Vector2i:
			lines.append("[b]OBJECTIVE INFO[/b]")
			var tile_info = get_tile_info(gs, pos_context)
			lines.append(tile_info)
			lines.append("----------------------------")
		elif gs.travel_mode == GameState.TravelMode.REGION and gs.get("region_ctrl") != null:
			var rc = gs.region_ctrl
			var p_pos = rc.player_pos
			
			var x_coord = gs.player.pos.x + (gs.local_offset.x / 500.0)
			var y_coord = gs.player.pos.y + (gs.local_offset.y / 500.0)
			lines.append("[color=gray]Precise GPS: %.2f, %.2f[/color]" % [x_coord, y_coord])
			
			if rc.minor_pois.has(p_pos):
				var poi = rc.minor_pois[p_pos]
				lines.append("[b][color=yellow]DISCOVERY[/color][/b]")
				lines.append("Name: %s" % poi.name)
				lines.append("Type: %s" % poi.type.capitalize())
				lines.append("----------------------------")
			else:
				var l_t = rc.grid[p_pos.y][p_pos.x]
				var t_name = l_t.capitalize()
				if l_t == "road": t_name = "Cobblestone Road"
				elif l_t == "farms": t_name = "Golden Wheat Fields"
				elif l_t == "keep": t_name = "The Citadels Walls"
				lines.append("Current Location: %s" % t_name)
				lines.append("----------------------------")
		elif gs.player.pos != null:
			# Fallback to current position biome if no specific cursor provided
			var x = gs.player.pos.x
			var y = gs.player.pos.y
			if x >= 0 and x < gs.width and y >= 0 and y < gs.height:
				var t = gs.grid[y][x]
				var biome = "Wilderness"
				match t:
					".": biome = "Plains"
					"T", "#": biome = "Forest"
					"^", "O": biome = "Mountains"
					"~": biome = "Water"
				lines.append("Current Biome: %s" % biome)
			lines.append("----------------------------")
		
		# 3. Party/Player Stats
		if gs.player.commander:
			var c = gs.player.commander
			lines.append("[b]%s[/b]" % c.name)
			var lvl_str = "Lvl %d" % c.level
			if "assigned_class" in c: lvl_str += " %s" % c.assigned_class.capitalize()
			lines.append(lvl_str)
			
			if "hp" in c and "hp_max" in c:
				lines.append("HP: %d/%d" % [c.hp, c.hp_max])
			lines.append("XP: %d | Party: %d units" % [c.xp, gs.player.troop_count])
	
	return lines

static func render_battle(gs, battle_ctrl, vw, vh) -> Array:
	if not battle_ctrl.active:
		return ["Battle Ended. Press Esc."]
		
	var cam_pos = Vector2(100, 100)
	if battle_ctrl.player_unit:
		cam_pos = Vector2(battle_ctrl.player_unit.pos)
		
	var cam_x = int(cam_pos.x)
	var cam_y = int(cam_pos.y)
	
	var proj_map = {}
	for p in battle_ctrl.projectiles:
		var p_pos = Vector2i(p.pos)
		if abs(p_pos.x - cam_x) <= vw/2 + 1 and abs(p_pos.y - cam_y) <= vh/2 + 1:
			var color = "yellow"
			if p.has("engine"): color = "orange"
			proj_map[p_pos] = "[color=%s]%s[/color]" % [color, p.symbol]

	var lines = []
	for y in range(vh):
		var line_parts = PackedStringArray()
		var grid_y = cam_y - vh/2 + y
		
		if grid_y < 0 or grid_y >= battle_ctrl.MAP_H:
			line_parts.append(" ".repeat(vw))
			lines.append("".join(line_parts))
			continue
			
		var cur_bg = Color.TRANSPARENT
		var cur_fg_hex = ""
		var cur_text = ""
		
		for x in range(vw):
			var grid_x = cam_x - vw/2 + x
			if grid_x < 0 or grid_x >= battle_ctrl.MAP_W:
				if cur_text != "":
					line_parts.append(_format_run(gs, cur_bg, cur_fg_hex, cur_text))
					cur_text = ""
				line_parts.append(" ")
				continue
				
			var pos = Vector2i(grid_x, grid_y)
			
			# BREAKERS (Projectiles, Units)
			if proj_map.has(pos) or battle_ctrl.unit_lookup.has(pos):
				if cur_text != "":
					line_parts.append(_format_run(gs, cur_bg, cur_fg_hex, cur_text))
					cur_text = ""
				
				var loc_t = battle_ctrl.grid[grid_y][grid_x] if (grid_y < battle_ctrl.grid.size() and grid_x < battle_ctrl.grid[grid_y].size()) else " "
				var terrain_color_obj = _get_terrain_color(gs, gs.player.pos, loc_t, "battle")
				
				if proj_map.has(pos):
					line_parts.append(_wrap_grid(gs, terrain_color_obj, proj_map[pos]))
				elif battle_ctrl.unit_lookup.has(pos):
					var u = battle_ctrl.unit_lookup[pos]
					var color = "cyan" if (u.faction == gs.player.faction or u.faction == "player") else "red"
					var sym = u.symbol
					if u.status.get("is_prone"): sym = "_"
					elif u.status.get("is_dead"): sym = "%"
					line_parts.append(_wrap_grid(gs, terrain_color_obj, "[color=%s]%s[/color]" % [color, sym]))
				
				cur_bg = Color.TRANSPARENT
				cur_fg_hex = ""
				continue

			# TERRAIN
			var t = battle_ctrl.grid[grid_y][grid_x] if (grid_y < battle_ctrl.grid.size() and grid_x < battle_ctrl.grid[grid_y].size()) else " "
			var bg = _get_terrain_color(gs, gs.player.pos, t, "battle")
			
			if battle_ctrl.is_siege and battle_ctrl.structure_hp.has(pos):
				var hp = battle_ctrl.structure_hp[pos]
				if hp < 200: bg = Color.RED
				elif hp < 400: bg = Color.ORANGE
				else: bg = Color.DIM_GRAY

			var fg_hex = _c_to_bb(bg)
			var display_sym = t
			if gs.render_mode == "grid" and t != " " and not t in ["G", "K", "M", "S", "H"]:
				display_sym = " "
			
			if cur_text == "":
				cur_bg = bg
				cur_fg_hex = fg_hex
				cur_text = display_sym
			elif bg == cur_bg and fg_hex == cur_fg_hex:
				cur_text += display_sym
			else:
				line_parts.append(_format_run(gs, cur_bg, cur_fg_hex, cur_text))
				cur_bg = bg
				cur_fg_hex = fg_hex
				cur_text = display_sym
				
		if cur_text != "":
			line_parts.append(_format_run(gs, cur_bg, cur_fg_hex, cur_text))
			
		lines.append("".join(line_parts))
	return lines

static func get_battle_side_panel(gs, battle_ctrl) -> Array:
	var lines = []
	lines.append("[b]BATTLE CONTROLS[/b]")
	lines.append("Active Turn: %d" % battle_ctrl.turn)
	lines.append("Phase: %s" % ("Siege" if battle_ctrl.is_siege else "Skirmish"))
	if battle_ctrl.is_tournament:
		lines.append("Prize: %d gold" % battle_ctrl.tournament_prize)
		
	lines.append("")
	lines.append("[color=yellow]ORDERS (1-5)[/color]")
	var orders = ["ADVANCE", "CHARGE", "FOLLOW", "HOLD", "RETREAT"]
	for o in orders:
		if battle_ctrl.current_order == o:
			lines.append(" > [color=green]%s[/color]" % o)
		else:
			lines.append("   %s" % o)
			
	lines.append("")
	lines.append("[b]STATUS[/b]")
	if battle_ctrl.player_unit:
		var u = battle_ctrl.player_unit
		var hp_p = (u.hp / float(u.hp_max)) * 100.0
		var col = "green"
		if hp_p < 50: col = "orange"
		if hp_p < 20: col = "red"
		lines.append("HP: [color=%s]%d/%d[/color]" % [col, int(u.hp), int(u.hp_max)])
		lines.append("Fatigue: %d/%d" % [int(u.fatigue), int(u.max_fatigue)])
		lines.append("Morale: %d%%" % int(u.morale * 100))
	else:
		lines.append("Player Status: --")
		
	lines.append("")
	var enemy_count = 0
	for u in battle_ctrl.units:
		if u.faction != "player" and u.hp > 0:
			enemy_count += 1
	lines.append("Enemies Left: %d" % enemy_count)
	
	# Targeting HUD
	if battle_ctrl.targeting_mode and battle_ctrl.targeting_target and not battle_ctrl.targeting_parts.is_empty():
		var target = battle_ctrl.targeting_target
		var att_idx = battle_ctrl.targeting_attack_index
		var u = battle_ctrl.player_unit
		
		lines.append("")
		lines.append("[b][color=yellow]TARGETING REPORT[/color][/b]")
		lines.append("Target: %s (%s)" % [target.name, target.type])
		
		# Condition Report
		var health_desc = "Healthy"
		var hp_pct = float(target.hp) / float(target.hp_max)
		if hp_pct < 0.25: health_desc = "[color=red]Critical[/color]"
		elif hp_pct < 0.5: health_desc = "[color=orange]Wounded[/color]"
		elif hp_pct < 0.9: health_desc = "[color=yellow]Scratched[/color]"
		else: health_desc = "[color=green]Healthy[/color]"
		lines.append("Condition: %s" % health_desc)
		
		var status_list = []
		if target.status.get("is_prone"): status_list.append("Prone")
		if target.status.get("is_downed"): status_list.append("Downed")
		if target.bleed_rate > 5.0: status_list.append("Hemorrhaging")
		elif target.bleed_rate > 0.0: status_list.append("Bleeding")
		
		if not status_list.is_empty():
			lines.append("Status: [color=magenta]%s[/color]" % ", ".join(status_list))
		
		lines.append("")
		lines.append("[b]HIT CHANCE / DMG[/b]")
		
		# List all parts
		for i in range(battle_ctrl.targeting_parts.size()):
			var p_key = battle_ctrl.targeting_parts[i]
			var est = GameData.get_damage_estimate(u, target, p_key, att_idx)
			
			var prefix = "   "
			var color = "gray"
			if i == battle_ctrl.targeting_index:
				prefix = " > "
				color = "cyan"
			
			# e.g. " > Head: 45% (12)"
			lines.append("%s[color=%s]%s: %d%% (%d)[/color]" % [prefix, color, p_key.capitalize(), est["hit_chance"], est["est_dmg"]])
		
		# Selected Attack Info
		var sel_part = battle_ctrl.targeting_parts[battle_ctrl.targeting_index]
		var sel_est = GameData.get_damage_estimate(u, target, sel_part, att_idx)
		lines.append("")
		lines.append("Attack: [color=white]%s[/color]" % sel_est["attack_name"])
	
	if battle_ctrl.is_siege and is_instance_valid(battle_ctrl.enemy_ref):
		lines.append("")
		lines.append("[b]SIEGE INFO[/b]")
		# Count remaining walls/structures
		var walls = battle_ctrl.structure_hp.size()
		lines.append("Structures: %d" % walls)
	
	return lines

static func render_codex(gs, CodexData, cat_idx, entry_idx, focus) -> String:
	var cats = CodexData.CATEGORIES.keys()
	var cat_key = cats[cat_idx]
	var entries = CodexData.CATEGORIES[cat_key]
	
	var left_col = ""
	var center_col = ""
	
	# Left Column: Categories
	left_col += "[b]ARCHIVE SECTIONS[/b]\n"
	for i in range(cats.size()):
		var c = cats[i]
		var pre = " > " if (focus == 0 and i == cat_idx) else "   "
		var color = "white"
		if i == cat_idx: color = "yellow"
		left_col += "[color=%s]%s%s[/color]\n" % [color, pre, c.capitalize()]

	# Middle Column: Entries in selected Category
	center_col += "[b]%s ENTRIES[/b]\n" % cat_key
	for i in range(entries.size()):
		var e = entries[i]
		# Only resolve if it's a static entry key, dynamic ones we might skip or handle
		if ":" in e: 
			# Handle dynamic lists later if needed, for now placeholder
			center_col += "   (Dynamic List)\n"
			continue
			
		var pre = " > " if (focus == 1 and i == entry_idx) else "   "
		var color = "white"
		if focus == 1 and i == entry_idx: color = "yellow"
		center_col += "[color=%s]%s%s[/color]\n" % [color, pre, e]

	# Build layout
	var content = ""
	# If we are reading (Focus 2), show full page
	if focus == 2:
		var entry_key = entries[entry_idx]
		if CodexData.ENTRIES.has(entry_key):
			var data = CodexData.ENTRIES[entry_key]
			content += "[center][b]%s[/b] (%s)[/center]\n\n" % [data["title"], data["icon"]]
			content += data["content"]
			content += "\n\n[color=gray](Press ESC or Backspace to return)[/color]"
		else:
			content += "[center]Entry data not found.[/center]"
	else:
		# Split screen view
		content += "[table=2]"
		content += "[cell]" + left_col + "[/cell]"
		content += "[cell]" + center_col + "[/cell]"
		content += "[/table]"
		
		content += "\n\n[color=gray]Navigate: Arrow Keys | Select: Enter | Tab: Switch Columns[/color]"

	return content

static func get_party_info_screen(gs) -> String:
	var p = gs.player
	var s = "[center][b]PARTY OVERVIEW: THE %s COMMAND[/b][/center]\n" % p.commander.name.to_upper()
	s += "[center]------------------------------------------------[/center]\n\n"
	
	# Global Stats
	s += "[table=3]"
	s += "[cell][b]VITALITY[/b][/cell][cell][b]SUPPLY[/b][/cell][cell][b]FINANCE[/b][/cell]"
	s += "[cell]Strength: %d[/cell][cell]Provisions: %d[/cell][cell]Gold: %d[/cell]" % [p.strength, p.provisions, p.gold]
	s += "[cell]Morale: %d%%[/cell][cell]Weight: %d/%d kg[/cell][cell]Daily Wage: %d[/cell]" % [p.morale, int(gs.get_total_weight()), int(gs.get_max_weight()), 10]
	s += "[/table]\n\n"

	# Troop Breakdown
	s += "[b]TROOP BREAKDOWN[/b]\n"
	var counts = {}
	for u in p.roster:
		counts[u.type] = counts.get(u.type, 0) + 1
	
	if counts.is_empty():
		s += " No troops currently in roster.\n"
	else:
		for type in counts:
			s += " - %s: %d\n" % [type.capitalize(), counts[type]]
	
	# Fiefs
	s += "\n[b]FIEFS & HOLDINGS[/b]\n"
	if p.fief_ids.is_empty():
		s += " Landless. You currently hold no titles.\n"
	else:
		for f_pos in p.fief_ids:
			var settlement = gs.settlements.get(f_pos)
			if settlement:
				s += " - %s (%s, Pop: %d)\n" % [settlement.name, settlement.type.capitalize(), settlement.population]
	
	s += "\n[center][color=gray]Press ESC to return[/color][/center]"
	return s

static func get_dialogue_screen(gs, target, options, idx) -> String:
	var target_name = "Unknown"
	var target_desc = "You stand before someone."
	
	if target is String:
		target_name = target
	elif target.has_method("get"):
		target_name = target.get("name")
		if target.get("type") == "army":
			target_desc = "You have encountered an army commanded by %s." % target_name
		elif target.get("type") == "caravan":
			target_desc = "You hail a merchant caravan."
	
	var content = "[center][b]DIALOGUE: %s[/b][/center]\n\n" % target_name.to_upper()
	content += "%s\n\n" % target_desc
	
	content += "[b]How do you respond?[/b]\n"
	for i in range(options.size()):
		var opt = options[i]
		var text = opt.text if opt is Dictionary else str(opt)
		var pre = " > " if i == idx else "   "
		var color = "white"
		if i == idx: color = "yellow"
		
		content += "[color=%s]%s%s[/color]\n" % [color, pre, text]
		
	return content

static func get_fief_info_screen(gs, s) -> String:
	if not s: return "[center]No settlement data selected.[/center]"
	
	var content = "[center][b]FIEF MANAGEMENT: %s[/b][/center]\n" % s.name.to_upper()
	content += "[center][color=gray]%s | %s | Tier %d[/color][/center]\n\n" % [s.faction, s.type.capitalize(), s.tier]
	
	# -- Section 1: Demographics & Economy --
	content += "[table=2]"
	
	# Demographics Column
	var col_dem = "[b]DEMOGRAPHICS[/b]\n"
	col_dem += "Population: %d\n" % s.population
	col_dem += " - Laborers: %d\n" % s.laborers
	col_dem += " - Burghers: %d\n" % s.burghers
	col_dem += " - Nobility: %d\n" % s.nobility
	
	# Finance Column
	var col_fin = "[b]FINANCE[/b]\n"
	var crown_col = "white"
	if s.crown_stock < 100: crown_col = "red"
	elif s.crown_stock > 5000: crown_col = "green"
	col_fin += "Crowns: [color=%s]%d[/color]\n" % [crown_col, s.crown_stock]
	
	col_fin += "Tax Level: %s\n" % s.tax_level.capitalize()
	
	var eff_col = "white"
	if s.cache_efficiency < 0.5: eff_col = "red"
	elif s.cache_efficiency > 0.8: eff_col = "green"
	col_fin += "Efficiency: [color=%s]%d%%[/color]\n" % [eff_col, int(s.cache_efficiency * 100)]
	col_fin += "Sustainability: [color=cyan]Stable[/color]\n"
	
	content += "[cell]%s[/cell][cell]%s[/cell]" % [col_dem, col_fin]
	content += "[/table]\n"
	
	content += "\n[b]INFRASTRUCTURE[/b]\n"
	var house_col = "white"
	if s.population > s.cache_housing_cap: house_col = "red"
	content += "Housing: [color=%s]%d/%d[/color]\n" % [house_col, s.population, s.cache_housing_cap]
	
	# Assets and Land
	content += "\n[b]LAND & SITES[/b]\n"
	content += "[table=3]"
	content += "[cell]Arable: %d[/cell][cell]Pasture: %d[/cell][cell]Forest: %d[/cell]" % [s.arable_acres, s.pasture_acres, s.forest_acres]
	content += "[cell]Minerals: %d[/cell][cell]Fishing: %d[/cell][cell]Wilderness: %d[/cell]" % [s.mining_slots, s.fishing_slots, s.wilderness_acres]
	content += "[/table]\n"
	
	# Buildings
	content += "\n[b]BUILDINGS[/b]\n"
	if s.buildings.is_empty():
		content += " No advanced structures.\n"
	else:
		var b_lines = []
		for b_name in s.buildings:
			b_lines.append("%s (Lvl %d)" % [b_name.capitalize().replace("_", " "), s.buildings[b_name]])
		content += " " + ", ".join(b_lines) + "\n"
	
	# Stock (Top items)
	content += "\n[b]STOCK HIGHLIGHTS[/b]\n"
	var r_list = ["grain", "wood", "iron", "ale", "meat", "gold"]
	var stock_strs = []
	for res in r_list:
		var amt = s.inventory.get(res, 0)
		if amt > 0:
			var res_col = "white"
			if amt < 20: res_col = "orange"
			elif amt > 500: res_col = "green"
			stock_strs.append("[color=%s]%s: %d[/color]" % [res_col, res.capitalize(), amt])
	content += " " + (", ".join(stock_strs) if not stock_strs.is_empty() else "Empty")
	
	content += "\n\n[center][color=gray]Navigate: Tab/Arrows | Confirm: Enter | Back: ESC[/color][/center]"
	return content

static func render_local_viewport(gs, battle_ctrl, vw, vh) -> Array:
	if not battle_ctrl or not battle_ctrl.active:
		return ["[center]Generating Tactical Map...[/center]"]

	var center_x = 250
	var center_y = 250
	
	if battle_ctrl.player_unit:
		center_x = battle_ctrl.player_unit.pos.x
		center_y = battle_ctrl.player_unit.pos.y
	elif not battle_ctrl.camera_locked:
		center_x = int(battle_ctrl.camera_pos.x)
		center_y = int(battle_ctrl.camera_pos.y)

	var start_x = center_x - (vw / 2)
	var start_y = center_y - (vh / 2)

	var proj_lookup = {}
	for p in battle_ctrl.projectiles:
		proj_lookup[Vector2i(p.pos)] = p.symbol

	var lines = []
	for y in range(vh):
		var line_parts = PackedStringArray()
		var grid_y = start_y + y
		
		if grid_y < 0 or grid_y >= battle_ctrl.MAP_H:
			line_parts.append(" ".repeat(vw))
			lines.append("".join(line_parts))
			continue
			
		var cur_bg = Color.TRANSPARENT
		var cur_fg_hex = ""
		var cur_text = ""
		
		for x in range(vw):
			var grid_x = start_x + x
			if grid_x < 0 or grid_x >= battle_ctrl.MAP_W:
				if cur_text != "":
					line_parts.append(_format_run(gs, cur_bg, cur_fg_hex, cur_text))
					cur_text = ""
				line_parts.append(" ")
				continue
			
			var pos = Vector2i(grid_x, grid_y)
			
			# BREAKERS
			if battle_ctrl.unit_lookup.has(pos) or proj_lookup.has(pos):
				if cur_text != "":
					line_parts.append(_format_run(gs, cur_bg, cur_fg_hex, cur_text))
					cur_text = ""
				
				var loc_t = battle_ctrl.get_tile(grid_x, grid_y)
				var terrain_color_obj = _get_terrain_color(gs, gs.player.pos, loc_t, "local")
				
				if battle_ctrl.unit_lookup.has(pos):
					var u = battle_ctrl.unit_lookup[pos]
					var u_col = "white"
					if u.team == "player": u_col = "cyan"
					elif u.team == "ally": u_col = "green"
					else: u_col = "red"
					if u == battle_ctrl.player_unit: u_col = "yellow"
					
					var sym = u.symbol
					if u.status.get("is_prone"): sym = "_"
					elif u.status.get("is_dead"): sym = "%"
					line_parts.append(_wrap_grid(gs, terrain_color_obj, "[color=%s][b]%s[/b][/color]" % [u_col, sym]))
				elif proj_lookup.has(pos):
					line_parts.append(_wrap_grid(gs, terrain_color_obj, "[color=white]%s[/color]" % proj_lookup[pos]))
				
				cur_bg = Color.TRANSPARENT
				cur_fg_hex = ""
				continue

			# TERRAIN
			var t = battle_ctrl.get_tile(grid_x, grid_y)
			var bg = _get_terrain_color(gs, gs.player.pos, t, "local")
			var fg_hex = _c_to_bb(bg)
			var display_sym = t
			
			if gs.render_mode == "grid" and t != " ":
				display_sym = " "
			
			if cur_text == "":
				cur_bg = bg
				cur_fg_hex = fg_hex
				cur_text = display_sym
			elif bg == cur_bg and fg_hex == cur_fg_hex:
				cur_text += display_sym
			else:
				line_parts.append(_format_run(gs, cur_bg, cur_fg_hex, cur_text))
				cur_bg = bg
				cur_fg_hex = fg_hex
				cur_text = display_sym
				
		if cur_text != "":
			line_parts.append(_format_run(gs, cur_bg, cur_fg_hex, cur_text))
			
		lines.append("".join(line_parts))
	return lines

static func render_viewport(gs, vw, vh, path) -> Array:
	# Main overworld map render with colors
	var cam = gs.player.pos + gs.player.camera_offset
	
	# Optimization: Use Spatial Hash to find visible entities
	var visible_entities_dict = {}
	var start_x = int(cam.x - vw/2)
	var start_y = int(cam.y - vh/2)
	var end_x = start_x + vw
	var end_y = start_y + vh
	
	# Fetch entities within the viewport using the spatial grid
	var margin = 2
	for cell_y in range((start_y-margin)/gs.SPATIAL_CELL_SIZE, (end_y+margin)/gs.SPATIAL_CELL_SIZE + 1):
		for cell_x in range((start_x-margin)/gs.SPATIAL_CELL_SIZE, (end_x+margin)/gs.SPATIAL_CELL_SIZE + 1):
			var cell = Vector2i(cell_x, cell_y)
			if gs.spatial_grid.has(cell):
				for ent in gs.spatial_grid[cell]:
					var e_pos = ent.pos if "pos" in ent else Vector2i(-1, -1)
					if e_pos.x >= start_x and e_pos.x < end_x and e_pos.y >= start_y and e_pos.y < end_y:
						visible_entities_dict[e_pos] = ent

	var lines = []
	for y in range(vh):
		var line_parts = PackedStringArray()
		var wy = int(start_y + y)
		
		# Optimization: skip processing if out of Y bounds
		if wy < 0 or wy >= gs.height:
			line_parts.append(" ".repeat(vw))
			lines.append("".join(line_parts))
			continue
			
		for x in range(vw):
			var wx = int(start_x + x)
			if wx < 0 or wx >= gs.width:
				line_parts.append(" ")
				continue
				
			var pos = Vector2i(wx, wy)
			var cur_t = gs.grid[wy][wx]
			var colors = _get_tile_colors(gs, pos, cur_t, "region")
			var bg_col = colors.bg
			var char_rendered = false
			
			# 1. Player
			if gs.player and pos == gs.player.pos:
				var p_str = "[color=white][b]@[/b][/color]"
				line_parts.append(_wrap_grid(gs, bg_col, p_str))
				char_rendered = true
				
			# 2. Other Entities from Spatial Hash
			elif visible_entities_dict.has(pos):
				var ent = visible_entities_dict[pos]
				var e_char = "?"
				var e_col = "white"
				
				if "type" in ent:
					if ent.type == "army":
						e_char = "A"
						if ent.faction == "bandits": e_col = "red"
						elif ent.faction == "player": e_col = "cyan"
						else: e_col = "white"
					elif ent.type == "caravan":
						e_char = "C"
						e_col = "gold"
				
				var ent_str = "[color=%s][b]%s[/b][/color]" % [e_col, e_char]
				line_parts.append(_wrap_grid(gs, bg_col, ent_str))
				char_rendered = true
			
			# 3. Path
			elif path and pos in path:
				var path_str = "[color=gold].[/color]"
				line_parts.append(_wrap_grid(gs, bg_col, path_str))
				char_rendered = true

			# 4. Settlements
			elif gs.settlements.has(pos):
				var s = gs.settlements[pos]
				var col = "cyan"
				var symbol = "v"
				
				match s.type:
					"hamlet": 
						symbol = "h"
						col = "#ADD8E6"
					"village": 
						symbol = "v"
						col = "cyan"
					"town": 
						symbol = "T"
						col = "#00FFFF"
					"city": 
						symbol = "C"
						col = "orange"
					"metropolis": 
						symbol = "M"
						col = "gold"
					"castle": 
						symbol = "S"
						col = "red"
					_:
						symbol = "v"
						col = "cyan"

				if s.type == "town" or gs.render_mode == "grid": 
					symbol = "[b]" + symbol + "[/b]"
				
				var set_str = "[color=%s]%s[/color]" % [col, symbol]
				line_parts.append(_wrap_grid(gs, bg_col, set_str))
				char_rendered = true
				
			if not char_rendered:
				var bg_hex = _c_to_bb(colors.bg)
				var fg_hex = _c_to_bb(colors.fg)
				
				var sym = "."
				match cur_t:
					"forest": sym = "T"
					"water", "~": sym = "~"
					"mountain": sym = "^"
					"hills": sym = "n"
					"road", "+", "=": sym = "="
					"farms", "f": sym = "\""
					"fallow", "\"": sym = "\""
					"urban", "residential": sym = "o"
					"urban_block": sym = "."
					"keep", "K": sym = "K"
					"market", "M": sym = "M"
					"industrial", "S": sym = "S"
					"slum": sym = "x"
					"docks": sym = "="
					"walls_outer", "#": sym = "#"
					_: sym = cur_t[0] if cur_t.length() > 0 else " "
				
				if gs.render_mode == "grid":
					var is_special = cur_t in ["road", "+", "=", "walls_outer", "#", "keep", "market", "industrial", "urban", "residential", "slum", "urban_block", "fallow", "farms", "docks"]
					if is_special:
						line_parts.append(_wrap_grid(gs, colors.bg, "[color=%s]%s[/color]" % [fg_hex, sym]))
					else:
						line_parts.append(_wrap_grid(gs, colors.bg, " "))
				else:
					line_parts.append(_wrap_grid(gs, colors.bg, "[color=%s]%s[/color]" % [fg_hex, sym]))
		lines.append("".join(line_parts))
	return lines

static func render_region(gs, region_ctrl, vw, vh) -> Array:
	var lines = []
	var px = region_ctrl.player_pos.x
	var py = region_ctrl.player_pos.y
	
	var start_x = px - (vw / 2)
	var start_y = py - (vh / 2)
	var end_x = start_x + vw
	var end_y = start_y + vh
	
	# Optimization: Spatial Hash for world entities visible in this region
	var world_entities_dict = {}
	var w_start = region_ctrl.world_origin
	var w_end = w_start + Vector2i(10, 10)
	
	for cell_y in range(w_start.y/gs.SPATIAL_CELL_SIZE, (w_end.y)/gs.SPATIAL_CELL_SIZE + 1):
		for cell_x in range(w_start.x/gs.SPATIAL_CELL_SIZE, (w_end.x)/gs.SPATIAL_CELL_SIZE + 1):
			var cell = Vector2i(cell_x, cell_y)
			if gs.spatial_grid.has(cell):
				for ent in gs.spatial_grid[cell]:
					var e_pos = Vector2i(ent.pos) if "pos" in ent else Vector2i(-1, -1)
					if e_pos.x >= w_start.x and e_pos.x < w_end.x and e_pos.y >= w_start.y and e_pos.y < w_end.y:
						# Translate world pos to region pos (center of the 50x50 block)
						var rx = (e_pos.x - w_start.x) * 50 + 25
						var ry = (e_pos.y - w_start.y) * 50 + 25
						world_entities_dict[Vector2i(rx, ry)] = ent

	for y in range(vh):
		var line_parts = PackedStringArray()
		var gy = start_y + y
		
		if gy < 0 or gy >= region_ctrl.height:
			line_parts.append(" ".repeat(vw))
			lines.append("".join(line_parts))
			continue
			
		for x in range(vw):
			var gx = start_x + x
			if gx < 0 or gx >= region_ctrl.width:
				line_parts.append(" ")
				continue
				
			var pos = Vector2i(gx, gy)
			var t = region_ctrl.grid[gy][gx]
			var sym = "."
			
			# Sample geology based on world position
			var world_pos = region_ctrl.world_origin + Vector2i(gx / 50, gy / 50)
			var colors = _get_tile_colors(gs, pos, t, "region")
			var terrain_bg_col = colors.bg
			var terrain_fg_col = colors.fg
			var bg_hex = _c_to_bb(terrain_bg_col)
			var fg_hex = _c_to_bb(terrain_fg_col)
			
			# 1. Player Priority
			if gx == px and gy == py:
				var player_str = "[color=yellow][b]@[/b][/color]"
				line_parts.append(_wrap_grid(gs, terrain_bg_col, player_str))
				continue

			# 2. Entities (Translated from World)
			if world_entities_dict.has(pos):
				var ent = world_entities_dict[pos]
				if ent == gs.player: continue 
				
				var e_type = ent.get("type", "unknown")
				var e_sym = "A" if e_type == "army" else "C"
				var e_col = "red" if ent.get("faction", "") == "bandits" else "white"
				if e_type == "caravan": e_col = "gold"
				var ent_str = "[color=%s]%s[/color]" % [e_col, e_sym]
				line_parts.append(_wrap_grid(gs, terrain_bg_col, ent_str))
				continue
			
			# 3. Settlements (Check top-left of each 50x50 world block)
			# Only show generic cyan symbol if we aren't already rendering the detailed city blueprint here
			var is_urban = t in ["keep", "market", "industrial", "urban", "residential", "slum", "walls_outer", "urban_block"]
			if not is_urban and gx % 50 == 0 and gy % 50 == 0:
				if gs.settlements.has(world_pos):
					var s = gs.settlements[world_pos]
					var s_sym = "O"
					match s.type:
						"village", "hamlet": s_sym = "o"
						"castle": s_sym = "S"
					
					var s_str = "[color=cyan]%s[/color]" % s_sym
					line_parts.append(_wrap_grid(gs, terrain_bg_col, s_str))
					continue

			# 4. Minor POIs (Discovery)
			if region_ctrl.minor_pois.has(pos):
				var poi = region_ctrl.minor_pois[pos]
				var poi_str = "[color=%s]%s[/color]" % [poi.color, poi.symbol]
				line_parts.append(_wrap_grid(gs, terrain_bg_col, poi_str))
				continue

			# 5. Map regional string to symbol
			match t:
				"forest": sym = "T"
				"water": sym = "~"
				"mountain": sym = "^"
				"hills": sym = "n"
				"peaks": sym = "A"
				"plains": sym = ","
				"desert": sym = "."
				"tundra": sym = "*"
				"jungle": sym = "f"
				"road": sym = "="
				"bridge": sym = "H"
				"bridge_rail": sym = "#"
				"farms": sym = "\""
				"fallow": sym = "\""
				"orchard": sym = "f"
				"pasture": sym = ","
				"urban", "residential": sym = "o" # Roof symbols
				"urban_block": sym = "." 
				"keep": sym = "K"
				"market": sym = "M"
				"industrial": sym = "S"
				"slum": sym = "x"
				"docks": sym = "=" # Wooden planks
				"walls_outer": sym = "#"
				_: sym = t[0] if t.length() > 0 else "?"
			
			var display_sym = sym
			if gs.render_mode == "grid":
				var is_special = t in ["road", "bridge", "bridge_rail", "walls_outer", "keep", "market", "industrial", "urban", "residential", "slum", "urban_block", "fallow", "farms", "docks"]
				if is_special:
					line_parts.append(_wrap_grid(gs, terrain_bg_col, "[color=%s]%s[/color]" % [fg_hex, display_sym]))
				else:
					display_sym = " " # Solid grid uses bgcolor
					line_parts.append(_wrap_grid(gs, terrain_bg_col, display_sym))
			else:
				line_parts.append("[color=%s]%s[/color]" % [fg_hex, display_sym])
		lines.append("".join(line_parts))
	return lines
