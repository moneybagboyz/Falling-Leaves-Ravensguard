class_name TerrainColors
extends RefCounted

# Terrain color calculation with safe caching (no hash collisions)
static var _cache := {}

static func get_color(pos: Vector2i, terrain_char: String, geology: Dictionary, scope: String = "global") -> Color:
	# Use structured string key instead of hash to prevent collisions
	var scope_code := 0
	match scope:
		"local": scope_code = 1
		"battle": scope_code = 2
		"region": scope_code = 3
	
	var key := "%d_%d_%d_%d" % [pos.x, pos.y, scope_code, terrain_char.unicode_at(0)]
	if _cache.has(key):
		return _cache[key]
	
	var color := _calculate_color(terrain_char, geology)
	_cache[key] = color
	return color

static func _calculate_color(t: String, geo: Dictionary) -> Color:
	# Quantize values to reduce cache bloat and improve consistency
	var temp: float = snappedf(geo.get("temp", 0.5), 0.1)
	var rain: float = snappedf(geo.get("rain", 0.5), 0.1)
	var elev: float = snappedf(geo.get("elevation", 0.3), 0.1)
	
	var final_col := Color.WHITE
	
	match t:
		"~", "≈", "/", "\\", "water": # Water
			var v: float = clamp(0.3 + (elev * 0.4), 0.2, 0.8)
			final_col = Color.from_hsv(0.6, 0.7, v)
		".", ",", "plains", "desert": # Plains / Grassland / Desert
			var hue: float = clamp(0.25 + (temp * 0.08), 0.2, 0.35)
			if t == "desert" or temp > 0.8: hue = 0.12 # Shifting to yellow/orange
			var sat: float = clamp(0.3 + (rain * 0.5), 0.2, 0.9)
			var val: float = clamp(0.6 + (rain * 0.2), 0.4, 0.9)
			final_col = Color.from_hsv(hue, sat, val)
		"#", "T", "&", "forest", "jungle", "hills": # Forest / Jungle / Hills
			var hue: float = clamp(0.28 - (rain * 0.05), 0.2, 0.35)
			if t == "hills": hue = 0.22 # Slightly more olive
			var sat: float = clamp(0.4 + (rain * 0.5), 0.3, 0.9)
			var val: float = clamp(0.3 + (rain * 0.3), 0.2, 0.7)
			final_col = Color.from_hsv(hue, sat, val)
		"o", "O", "^", "peaks": # Mountains / Peaks
			if elev > 0.85 or t == "peaks": 
				final_col = Color.WHITE # Snowcaps
			else:
				var v: float = clamp(0.7 - (elev * 0.5), 0.1, 0.8)
				final_col = Color(v, v, v)
		"S", "\"", "savanna": # Savanna / Dry
			var hue: float = 0.12 - (temp * 0.04)
			var sat: float = 0.4 + (temp * 0.2)
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
		"+": # Door (dungeon)
			final_col = Color(0.5, 0.3, 0.1) # Brown door
		"%": # Debris (battle)
			final_col = Color(0.4, 0.4, 0.4) # Gray debris
		"X": # Obstacle (battle)
			final_col = Color(0.3, 0.3, 0.3) # Dark obstacle
		_:
			final_col = Color.WHITE
	
	return final_col

static func get_tile_colors(pos: Vector2i, t: String, geo: Dictionary, scope: String = "global") -> Dictionary:
	var bg := get_color(pos, t, geo, scope)
	var fg := Color.WHITE
	
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
			var noise_val: float = abs(sin(pos.x * 0.77 + pos.y * 1.33))
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

static func get_dungeon_color(t: String) -> Color:
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

static func clear_cache() -> void:
	_cache.clear()
