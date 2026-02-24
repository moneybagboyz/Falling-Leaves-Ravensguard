class_name Settlement extends Resource

## One inhabited tile on the world map.
## Created by ProvinceGenerator.place_settlements() during world generation.
## Ticked daily by WorldState._on_daily_pulse().

# ── Identity ──────────────────────────────────────────────────────────────────
var id:                   int
var name:                 String
var tile_x:               int
var tile_y:               int
var province_id:          int
var faction_id:           int    = -1
var tier:                 int    = 1    # 0=Hamlet 1=Village 2=Town 3=City 4=Metropolis
var settlement_type:      String = "village"  # village, town, city, castle, port, hamlet
var governor_personality: String = "balanced" # balanced, greedy, militant, builder

# ── Population ────────────────────────────────────────────────────────────────
var population:  int = 100
var laborers:    int = 84
var burghers:    int = 15
var nobility:    int = 1

# ── Land (calculated once on init from surrounding world tiles) ───────────────
var arable_acres:     float = 0.0
var forest_acres:     float = 0.0
var mining_slots:     int   = 0    ## total rocky terrain capacity (used for stone)
var fishing_slots:    int   = 0
## Geology-specific mineral deposit capacities (rid → effective slot count).
## Populated by _calculate_land() via GeologyGenerator.
## Only MOUNTAIN and HILLS tiles have geology; the specific mix depends on the
## geology type of each tile (sedimentary/metamorphic/igneous).
var mineral_deposits: Dictionary = {}

# ── Economy ───────────────────────────────────────────────────────────────────
var market:           Market          = null
var buildings:        Array[Building] = []
var happiness:        float           = 75.0
var unrest:           float           = 0.0
var treasury:         float           = 100.0
var housing_capacity: int             = 200

# ── Roads (set by RoadGenerator after world-gen, before simulation starts) ────
var connectivity_rate: float = 1.0  ## 1.0 = isolated; 2.0+ = road junction

# ── Flags ─────────────────────────────────────────────────────────────────────
var has_three_field:    bool = false   # unlocked at Farm level 4+
## Set each tick by Market.consume(); used by GovernorAI.collect_taxes().
var burgher_unhappy:    bool = false
var nobility_unhappy:   bool = false


# ── Initialisation ────────────────────────────────────────────────────────────

func initialize(tx: int, ty: int, pid: int, data: WorldData) -> void:
	tile_x      = tx
	tile_y      = ty
	province_id = pid
	market      = Market.new()
	_calculate_land(data)
	_init_population()
	_seed_starting_inventory()
	_add_starting_buildings()


func _calculate_land(data: WorldData) -> void:
	## Scan a radius around the settlement tile.
	## Radius grows with tier so larger settlements exploit more land.
	var radius: int = [1, 1, 2, 3, 4][clampi(tier, 0, 4)]
	for dy in range(-radius, radius + 1):
		for dx in range(-radius, radius + 1):
			var nx: int = tile_x + dx
			var ny: int = tile_y + dy
			if nx < 0 or nx >= data.width or ny < 0 or ny >= data.height:
				continue
			var raw_slots: int = 0
			match data.terrain[ny][nx]:
				WorldData.TerrainType.PLAINS:
					arable_acres += 250.0
				WorldData.TerrainType.HILLS:
					arable_acres += 125.0
					raw_slots     = 150
					mining_slots += 150
				WorldData.TerrainType.FOREST:
					arable_acres  += 50.0
					forest_acres  += 200.0
				WorldData.TerrainType.MOUNTAIN:
					raw_slots     = 400
					mining_slots += 400
				WorldData.TerrainType.RIVER:
					arable_acres  += 250.0
					fishing_slots += 150
					raw_slots     = 40
					mining_slots += 40
				WorldData.TerrainType.COAST:
					fishing_slots += 80
			# Accumulate geology-specific mineral deposits for rocky tiles.
			if raw_slots > 0:
				var geo: String = data.geology[ny][nx]
				var tile_deposits: Dictionary = GeologyGenerator.mineral_deposits_for(geo, raw_slots)
				for rid: String in tile_deposits:
					mineral_deposits[rid] = mineral_deposits.get(rid, 0) + tile_deposits[rid]


## Called once by RoadGenerator after connectivity_rate is set.
## Scales starting population by road access — crossroads towns grow larger.
func apply_connectivity_bonus() -> void:
	population = maxi(int(population * connectivity_rate), 10)
	_init_population()


func _init_population() -> void:
	laborers = int(population * 0.84)
	burghers = int(population * 0.15)
	nobility = population - laborers - burghers


func _seed_starting_inventory() -> void:
	## Give new settlements a small survival buffer.
	market.add_stock("grain", population * 1.2 * 30.0)   # 30 days of food
	market.add_stock("wood",  population * 0.02 * 14.0)  # 14 days of fuel


func _add_starting_buildings() -> void:
	## Every settlement starts with a level-1 farm.
	buildings.append(Building.new("farm", 1))
	if forest_acres > 0.0:
		buildings.append(Building.new("lumber_mill", 1))
	if mining_slots > 0:
		buildings.append(Building.new("mine", 1))
	if fishing_slots > 0:
		buildings.append(Building.new("fishery", 1))


# ── Daily tick (called by WorldState) ────────────────────────────────────────

func daily_tick() -> void:
	Production.run(self)
	market.consume(self)
	GovernorAI.decide(self)
	GovernorAI.collect_taxes(self)
	market.update_prices(self)
	_update_population()


func _update_population() -> void:
	## Grow if well-fed and within housing cap.
	if market.get_stock("grain") > population * 1.2 * 30.0 \
			and population < housing_capacity \
			and happiness > 60.0:
		var births: int = maxi(1, int(population * 0.0002))
		population += births
		_init_population()


# ── Building helpers ──────────────────────────────────────────────────────────

## Returns the level of the named building type, or 0 if not present.
func _building_level(btype: String) -> int:
	for b: Building in buildings:
		if b.building_type == btype:
			return b.level
	return 0


## Returns the Building object for the given type, or null.
func get_building(btype: String) -> Building:
	for b: Building in buildings:
		if b.building_type == btype:
			return b
	return null


## Adds a new level-1 building or upgrades an existing one.
func add_or_upgrade_building(btype: String) -> bool:
	var existing: Building = get_building(btype)
	if existing != null:
		if existing.can_upgrade():
			var cost: float = existing.upgrade_cost()
			if treasury >= cost:
				treasury -= cost
				existing.level += 1
				if btype == "farm" and existing.level >= 4:
					has_three_field = true
				return true
		return false
	else:
		var cost: float = 80.0
		if treasury >= cost:
			treasury -= cost
			buildings.append(Building.new(btype, 1))
			return true
	return false


# ── Debug ─────────────────────────────────────────────────────────────────────

func summary() -> String:
	var bldg_parts: PackedStringArray = []
	for b: Building in buildings:
		bldg_parts.append("%s lv%d" % [b.building_type, b.level])
	var bldg_str: String = ", ".join(bldg_parts) if bldg_parts.size() > 0 else "none"
	return "[%s | tier %d] pop:%d  happy:%.0f  unrest:%.0f  treasury:%.0fg\n    buildings: %s\n%s" % [
		name, tier, population, happiness, unrest, treasury, bldg_str, market.summary()
	]
