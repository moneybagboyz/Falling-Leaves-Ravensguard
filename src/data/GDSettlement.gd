class_name GDSettlement
extends RefCounted

var pos: Vector2i
var name: String
var type: String # "hamlet", "village", "town", "city", "metropolis", "castle"
var tier: int
var faction: String
var population: int
var laborers: int # Land-based workers
var burghers: int # Industrial workers
var nobility: int # Rulers and upper class
var houses: int
var house_progress: float
var radius: int
var footprint: Rect2i # The world-map area this settlement occupies
var max_slots: int
var inventory: Dictionary = {}

# --- AGRICULTURE & LAND ---
var total_acres: int = 0
var arable_acres: int = 0
var pasture_acres: int = 0
var forest_acres: int = 0
var fallow_acres: int = 0
var wetland_acres: int = 0
var arid_acres: int = 0
var river_acres: int = 0
var wilderness_acres: int = 0
var has_three_field_system: bool = true
var charcoal_burners: int = 0 # Labor allocated to charcoal

# --- INDUSTRIAL SITES ---
var mining_slots: int = 0 # Potential for mining labor
var fishing_slots: int = 0 # Potential for fishing labor
var extraction_slots: int = 0 # For Salt, Peat, Clay, etc.
var ore_deposits: Dictionary = {} # res_name -> count (number of tiles)

var production_capacity: Dictionary = {}
var crown_stock: int = 0
var tax_level: String = "normal"
var loyalty: int = 50
var happiness: int = 70
var unrest: int = 0
var stability: int = 50
var governor: Dictionary = {}

# Defense and Siege Data
var tower_count: int = 0
var tower_level: int = 1 # 1: Wood, 2: Stone, 3: Grand
var defensive_engines: Dictionary = {} # "ballista" -> count, "catapult" -> count

# Feudal Ownership
var lord_id: String = "" # ID of the NPC who holds this fief
var is_capital: bool = false # Is this the seat of the Kingdom?

# Tournament Data
var tournament_active: bool = false
var tournament_days_left: int = 0
var tournament_prize_pool: int = 0
var tournament_participants: Array = [] # IDs of NPCs participating

var burgher_unhappy: bool = false
var nobility_unhappy: bool = false
var garrison: int = 0
var garrison_max: int = 0
var buildings: Dictionary = {}         # Permanent/Static buildings (id -> level)
var player_shares: Dictionary = {}      # Shared ownership of Industry (id -> percent 0.0-1.0)
var organic_industries: Dictionary = {} # Automated/Organic industry slots
var construction_queue: Array = []
var influence: Dictionary = {"player": 0}
var parent_city: Vector2i = Vector2i(-1, -1)
var migration_buffer: int = 0
var shop_inventory: Array = []
var recruit_pool: Array = []
var npcs: Array = [] # Array of GDNPC

# --- SIEGE DATA ---
var is_under_siege: bool = false
var siege_timer: int = 0
var siege_attacker_faction: String = ""

# --- CACHE (Optimization) ---
var cache_efficiency: float = 1.0
var cache_housing_cap: int = 50
var cache_prices: Dictionary = {} # Resource Name -> Cached Price

var last_labor_allocation: Dictionary = {}

func _init(_pos: Vector2i = Vector2i.ZERO):
	pos = _pos

func get_inventory_value(res: String) -> int:
	return inventory.get(res, 0)

func add_inventory(res: String, amount: int):
	inventory[res] = inventory.get(res, 0) + amount

func remove_inventory(res: String, amount: int) -> int:
	var current = inventory.get(res, 0)
	var to_remove = min(current, amount)
	inventory[res] = current - to_remove
	return to_remove

func initialize_acres(grid, _resources, geology):
	total_acres = 0
	arable_acres = 0
	pasture_acres = 0
	forest_acres = 0
	fallow_acres = 0
	wetland_acres = 0
	arid_acres = 0
	river_acres = 0
	wilderness_acres = 0
	
	var h = grid.size()
	var w = grid[0].size()
	for dy in range(-radius, radius+1):
		for dx in range(-radius, radius+1):
			var p = pos + Vector2i(dx, dy)
			if p.x < 0 or p.x >= w or p.y < 0 or p.y >= h: continue
			if p.distance_to(pos) > radius: continue
			
			var t = GameState.get_true_terrain(p)
				
			var tile_acres = Globals.ACRES_PER_TILE
			
			match t:
				".": # Plains
					arable_acres += tile_acres
					total_acres += tile_acres
				"o": # Hills / Foothills
					# Hills provide roughly 50% arable land compared to plains
					var cleared = int(tile_acres * 0.5)
					arable_acres += cleared
					pasture_acres += (tile_acres - cleared)
					total_acres += tile_acres
				"/", "\\", "≈": # Rivers (Floodplains + Sifting)
					arable_acres += tile_acres
					river_acres += tile_acres
					total_acres += tile_acres
				"#": # Forests
					# Even in forests, people clear small plots (20% of tile)
					var cleared = int(tile_acres * 0.2)
					arable_acres += cleared
					forest_acres += (tile_acres - cleared)
					total_acres += tile_acres
				"&": # Swamps (Wetlands)
					# Jungles/Swamps are wetlands but also provide wood (treating as partial forest)
					var wooded = int(tile_acres * 0.45)
					forest_acres += wooded
					wetland_acres += (tile_acres - wooded)
					total_acres += tile_acres
				"\"": # Desert (Arid)
					arid_acres += tile_acres
					total_acres += tile_acres
				"*": # Tundra (Wilderness)
					# Tundra is too cold for grain, but provides pasture/wilderness
					pasture_acres += int(tile_acres * 0.4)
					wilderness_acres += int(tile_acres * 0.6)
					total_acres += tile_acres
				"^", "O": # Mountain
					total_acres += tile_acres # It's land!
				"~": # Water
					pass
	
	# Urban Sprawl Logic: Settlement reduces its own arable land
	# 5 acres per 100 people total (not per tile)
	var total_sprawl = int(population / 100.0 * 5)
	if total_sprawl > 0:
		var reduce = min(arable_acres, total_sprawl)
		arable_acres -= reduce
		total_acres -= reduce
	
	# Initial rough split: All plains are part of the Arable Rotation.
	# The Three-Field system handles the fallow/pasture split dynamically.
	pasture_acres = 0
	fallow_acres = 0

func sync_social_classes():
	nobility = max(1, int(population * Globals.NOBILITY_TARGET_PERCENT))
	burghers = int(population * Globals.BURGHER_TARGET_PERCENT)
	laborers = population - nobility - burghers

func get_food_supply() -> int:
	var total = 0
	for f in ["grain", "fish", "meat"]:
		total += inventory.get(f, 0)
	return total

func get_food_stock() -> int:
	return inventory.get("grain", 0) + inventory.get("fish", 0) + inventory.get("meat", 0)

func get_housing_capacity() -> int:
	var base = houses * 5
	var district_lvl = buildings.get("housing_district", 0)
	var civil_lvl = buildings.get("town_hall", 0)
	
	var cap = base + (district_lvl * 200)
	cap = int(cap * (1.0 + (civil_lvl * 0.1)))
	return cap

func get_workforce_efficiency() -> float:
	# In the infrastructure multiplier model, workforce efficiency is primarily
	# a measure of Burgher happiness and social stability.
	var base = 1.0
	if get("burgher_unhappy"): base *= 0.8
	if unrest > 50: base *= (1.0 - ((unrest - 50) / 100.0))
	return clamp(base, 0.1, 1.0)
