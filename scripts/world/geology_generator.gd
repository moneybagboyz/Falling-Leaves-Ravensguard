class_name GeologyGenerator

## Assigns a geology type ("sedimentary", "metamorphic", "igneous") to every
## MOUNTAIN or HILLS tile in the world using a low-frequency noise field.
##
## Geological zones are intentionally large (hundreds of tiles wide) so that
## mountain ranges share a consistent mineral character.  This creates regional
## economic specialisation — an empire that controls the sedimentary belt
## controls iron and coal; one that holds igneous peaks holds gold and gems.
##
## Deposit tables are loaded from data/geology_resources.json.
## Each entry maps geology_type → { resource_id: probability } where probability
## is the fraction of raw mining_slots that contain that mineral vein.

const GEOLOGY_PATH := "res://data/geology_resources.json"

# Cached deposit table so the JSON is only read once per session.
static var _deposit_table: Dictionary = {}
static var _table_loaded:  bool       = false


## Returns the deposit probability table (geology_type -> {rid: probability}).
## Loads from JSON on first call, returns cached copy thereafter.
static func deposit_table() -> Dictionary:
	if not _table_loaded:
		var file := FileAccess.open(GEOLOGY_PATH, FileAccess.READ)
		if file == null:
			push_warning("GeologyGenerator: could not open " + GEOLOGY_PATH)
			_deposit_table = {}
		else:
			_deposit_table = JSON.parse_string(file.get_as_text())
		_table_loaded = true
	return _deposit_table


## Assigns data.geology[y][x] for all MOUNTAIN and HILLS tiles.
## All other tile types remain "" (no mining potential).
static func assign(data: WorldData, seed_val: int) -> void:
	var noise := FastNoiseLite.new()
	noise.seed          = seed_val
	noise.noise_type    = FastNoiseLite.TYPE_SIMPLEX_SMOOTH
	noise.fractal_type  = FastNoiseLite.FRACTAL_FBM
	noise.frequency     = 0.006   # large zones — roughly 150-tile radius per province
	noise.fractal_octaves = 3

	const MINING_TERRAIN: Array = [
		WorldData.TerrainType.MOUNTAIN,
		WorldData.TerrainType.HILLS,
	]

	for y in range(data.height):
		for x in range(data.width):
			if data.terrain[y][x] not in MINING_TERRAIN:
				continue
			# Remap noise −1..1 → 0..1 and split into 3 zones.
			var n: float = (noise.get_noise_2d(float(x), float(y)) + 1.0) * 0.5
			if n < 0.40:
				data.geology[y][x] = "sedimentary"
			elif n < 0.72:
				data.geology[y][x] = "metamorphic"
			else:
				data.geology[y][x] = "igneous"


## Given a geology type and the raw mining_slots for a tile, returns a
## dictionary of { resource_id: effective_deposit_slots } for that tile.
## deposit_slots = raw_slots * probability from the JSON table.
static func mineral_deposits_for(geology_type: String, raw_slots: int) -> Dictionary:
	var table: Dictionary = deposit_table()
	var probs: Dictionary = table.get(geology_type, {})
	var result: Dictionary = {}
	for rid: String in probs:
		var slots: int = roundi(raw_slots * float(probs[rid]))
		if slots > 0:
			result[rid] = slots
	return result
