class_name WorldGenerator

## Procedurally generates all world data layers using FastNoiseLite.
## Uses a Whittaker-style biome classification based on temperature + precipitation.

## Main entry point. Returns a fully populated WorldData object.
## Pass a WorldGenParams to control all generation parameters;
## omit (or pass null) to use defaults.
static func generate(width: int, height: int, seed_val: int = 0, params: WorldGenParams = null) -> WorldData:
	if params == null:
		params = WorldGenParams.new()
	var s: int = seed_val if seed_val != 0 else randi()
	var data := WorldData.new(width, height, s)

	_generate_altitude(data, s, params)
	_generate_temperature(data, s + 1, params)
	_generate_precipitation(data, s + 2, params)
	_generate_drainage(data, s + 3)
	_derive_biomes(data)
	_derive_prosperity(data)
	Hydrology.process(data, params)

	# Phase 1.5 — terrain + province layers
	# GeologyGenerator runs immediately after terrain so that settlements can
	# read geology during _calculate_land().
	# ProvinceGenerator.generate() calls score_settlement_sites() internally.
	TerrainClassifier.classify(data)
	GeologyGenerator.assign(data, s + 10)
	ProvinceGenerator.generate(data, s + 9, params)

	# Phase 2 — place settlements (hub towns + spoke hamlets)
	data.settlements = ProvinceGenerator.place_settlements(data, params)

	# Phase 2.5 — road network, connectivity bonuses, percentile-based tiers
	RoadGenerator.generate(data, data.settlements)
	RoadGenerator.assign_tiers(data.settlements)

	return data


# ---------------------------------------------------------------------------
# Noise helpers
# ---------------------------------------------------------------------------

static func _make_noise(
		seed_val: int,
		frequency: float = 0.003,
		octaves: int = 6,
		gain: float = 0.5,
		lacunarity: float = 2.0) -> FastNoiseLite:

	var noise := FastNoiseLite.new()
	noise.seed = seed_val
	noise.noise_type = FastNoiseLite.TYPE_SIMPLEX_SMOOTH
	noise.fractal_type = FastNoiseLite.FRACTAL_FBM
	noise.frequency = frequency
	noise.fractal_octaves = octaves
	noise.fractal_gain = gain
	noise.fractal_lacunarity = lacunarity
	return noise


## Samples the noise over the full grid and normalises values to 0.0–1.0.
static func _sample_and_normalize(noise: FastNoiseLite, width: int, height: int) -> Array:
	var raw: Array = []
	var min_v: float = INF
	var max_v: float = -INF

	for y in range(height):
		var row: Array = []
		for x in range(width):
			var v: float = noise.get_noise_2d(float(x), float(y))
			row.append(v)
			if v < min_v:
				min_v = v
			if v > max_v:
				max_v = v
		raw.append(row)

	var range_v: float = max_v - min_v
	if range_v == 0.0:
		return raw

	for y in range(height):
		for x in range(width):
			raw[y][x] = (raw[y][x] - min_v) / range_v

	return raw


# ---------------------------------------------------------------------------
# Layer generation
# ---------------------------------------------------------------------------

static func _generate_altitude(data: WorldData, s: int, params: WorldGenParams) -> void:
	# ── Three noise layers ──────────────────────────────────────────────────
	# 1. Base elevation noise  — broad terrain shape
	var n_noise := _make_noise(s,      params.noise_frequency, params.noise_octaves, 0.5, 2.0)
	# 2. Crust noise           — simulates tectonic plate identity
	#    Low frequency so plates are large.  Fewer octaves → smoother boundaries.
	var n_crust := _make_noise(s + 10, params.crust_frequency, 4, 0.5, 2.0)
	# 3. Tectonic activity     — spikes at plate boundary zones (crust ≈ median)
	#    Same frequency as crust but offset seed so it's independent.
	var n_tect  := _make_noise(s + 20, params.crust_frequency * 1.5, 3, 0.5, 2.0)

	var noise_raw := _sample_and_normalize(n_noise, data.width, data.height)
	var crust_raw := _sample_and_normalize(n_crust, data.width, data.height)
	var tect_raw  := _sample_and_normalize(n_tect,  data.width, data.height)

	# Median of the crust layer — plate boundaries are where crust ≈ median.
	var crust_flat: Array = []
	crust_flat.resize(data.width * data.height)
	var ci: int = 0
	for y in range(data.height):
		for x in range(data.width):
			crust_flat[ci] = crust_raw[y][x]
			ci += 1
	crust_flat.sort()
	var crust_median: float = crust_flat[crust_flat.size() / 2]

	# ── Blend ───────────────────────────────────────────────────────────────
	# tectonic spikes sharply near the median of crust (= plate boundary zones).
	# Formula mirrors the reference project: 0.2/(|median - crust| + 0.1) - 0.95
	var raw: Array = WorldData._make_grid(data.width, data.height, 0.0)
	var min_v: float =  INF
	var max_v: float = -INF

	for y in range(data.height):
		for x in range(data.width):
			var n: float = noise_raw[y][x]
			var c: float = crust_raw[y][x]
			var t: float = tect_raw[y][x]
			# Tectonic activity: spikes at plate edges, suppressed elsewhere.
			var tectonic: float = maxf(0.0, 0.2 / (absf(crust_median - c) + 0.1) - 0.95)
			# Weighted blend of the three components.
			var v: float = (n * params.noise_factor
					+ c * params.crust_factor
					+ tectonic * params.tectonic_factor)
			raw[y][x] = v
			if v < min_v: min_v = v
			if v > max_v: max_v = v

	# Re-normalise the blend to 0..1.
	var range_v: float = max_v - min_v
	if range_v == 0.0:
		range_v = 1.0
	for y in range(data.height):
		for x in range(data.width):
			raw[y][x] = (raw[y][x] - min_v) / range_v

	# ── Radial falloff ───────────────────────────────────────────────────────
	var cx: float = data.width * 0.5
	var cy: float = data.height * 0.5
	var max_dist: float = minf(cx, cy)

	for y in range(data.height):
		for x in range(data.width):
			var v: float = raw[y][x]
			var dx: float = (x - cx) / max_dist
			var dy: float = (y - cy) / max_dist
			var dist: float = sqrt(dx * dx + dy * dy)
			var falloff: float = clampf(1.0 - dist * params.island_falloff, 0.0, 1.0)
			data.altitude[y][x] = clampf(v * falloff, 0.0, 1.0)

	# ── Sea level quantile ───────────────────────────────────────────────────
	var flat: Array = []
	flat.resize(data.width * data.height)
	var idx: int = 0
	for y in range(data.height):
		for x in range(data.width):
			flat[idx] = data.altitude[y][x]
			idx += 1
	flat.sort()
	data.sea_level = flat[clampi(int(flat.size() * params.sea_ratio), 0, flat.size() - 1)]


static func _generate_temperature(data: WorldData, s: int, params: WorldGenParams) -> void:
	var noise := _make_noise(s, 0.004, 4, 0.5, 2.0)
	var raw := _sample_and_normalize(noise, data.width, data.height)

	for y in range(data.height):
		# Latitude gradient: hottest at equator (map centre), coldest at poles (top/bottom).
		var lat: float = float(y) / float(data.height)          # 0..1, top to bottom
		var equator: float = 1.0 - absf(lat - 0.5) * 2.0       # 1 at centre, 0 at edges

		for x in range(data.width):
			var alt: float = data.altitude[y][x]
			# Higher altitude = colder.
			var alt_cool: float = clampf(1.0 - alt * 0.65, 0.0, 1.0)
			var temp: float = equator * alt_cool
			# Small noise variation + global temperature bias from params.
			temp = clampf(temp + raw[y][x] * 0.18 - 0.09 + params.temp_bias, 0.0, 1.0)
			data.temperature[y][x] = temp


static func _generate_precipitation(data: WorldData, s: int, params: WorldGenParams) -> void:
	var noise := _make_noise(s, 0.004, 5, 0.55, 2.0)
	var raw := _sample_and_normalize(noise, data.width, data.height)

	for y in range(data.height):
		for x in range(data.width):
			var alt: float = data.altitude[y][x]
			# Mountains create rain-shadow: very high altitude blocks most moisture.
			var rain_shadow: float = clampf(1.0 - alt * 0.72, 0.08, 1.0)
			# Global precipitation bias from params.
			data.precipitation[y][x] = clampf(raw[y][x] * rain_shadow + params.precip_bias, 0.0, 1.0)


static func _generate_drainage(data: WorldData, s: int) -> void:
	var noise := _make_noise(s, 0.005, 4, 0.5, 2.0)
	var raw := _sample_and_normalize(noise, data.width, data.height)
	for y in range(data.height):
		for x in range(data.width):
			data.drainage[y][x] = raw[y][x]


# ---------------------------------------------------------------------------
# Derived layers
# ---------------------------------------------------------------------------

static func _classify_biome(alt: float, temp: float, precip: float, sea_level: float) -> TileRegistry.BiomeType:
	# --- Water ---
	# Deep ocean: lowest 6 % of the absolute scale (always submerged).
	# The remaining ocean bands are relative to the computed sea_level.
	var deep_cut: float = sea_level * 0.15
	if alt < deep_cut:
		return TileRegistry.BiomeType.DEEP_OCEAN
	if alt < sea_level - sea_level * 0.25:
		return TileRegistry.BiomeType.OCEAN
	if alt < sea_level:
		return TileRegistry.BiomeType.SHALLOW_WATER

	# --- Coastal strip ---
	if alt < sea_level + 0.04:
		return TileRegistry.BiomeType.BEACH

	# --- High peaks ---
	if alt > 0.85:
		return TileRegistry.BiomeType.MOUNTAIN_SNOW if temp < 0.35 else TileRegistry.BiomeType.MOUNTAIN_ROCK
	if alt > 0.72:
		return TileRegistry.BiomeType.MOUNTAIN_ROCK

	# --- Cold zones ---
	if temp < 0.14:
		return TileRegistry.BiomeType.SNOW
	if temp < 0.26:
		return TileRegistry.BiomeType.TUNDRA if precip < 0.45 else TileRegistry.BiomeType.BOREAL_FOREST

	# --- Temperate zones ---
	if temp < 0.52:
		if precip < 0.22:
			return TileRegistry.BiomeType.GRASSLAND
		if precip < 0.50:
			return TileRegistry.BiomeType.WOODLAND
		return TileRegistry.BiomeType.TEMPERATE_FOREST

	# --- Warm / tropical zones ---
	if precip < 0.18:
		return TileRegistry.BiomeType.DESERT
	if precip < 0.40:
		return TileRegistry.BiomeType.SAVANNA
	if precip < 0.65:
		return TileRegistry.BiomeType.GRASSLAND
	return TileRegistry.BiomeType.TROPICAL_RAINFOREST


static func _derive_biomes(data: WorldData) -> void:
	for y in range(data.height):
		for x in range(data.width):
			data.biome[y][x] = _classify_biome(
				data.altitude[y][x],
				data.temperature[y][x],
				data.precipitation[y][x],
				data.sea_level
			)


static func _derive_prosperity(data: WorldData) -> void:
	## Base fertility per biome; modulated by drainage for a farming-viability score.
	var FERTILITY: Dictionary = {
		TileRegistry.BiomeType.DEEP_OCEAN:          0.00,
		TileRegistry.BiomeType.OCEAN:               0.00,
		TileRegistry.BiomeType.SHALLOW_WATER:       0.05,
		TileRegistry.BiomeType.BEACH:               0.08,
		TileRegistry.BiomeType.DESERT:              0.04,
		TileRegistry.BiomeType.SAVANNA:             0.40,
		TileRegistry.BiomeType.TROPICAL_RAINFOREST: 0.55,
		TileRegistry.BiomeType.GRASSLAND:           0.78,
		TileRegistry.BiomeType.WOODLAND:            0.65,
		TileRegistry.BiomeType.TEMPERATE_FOREST:    0.70,
		TileRegistry.BiomeType.BOREAL_FOREST:       0.42,
		TileRegistry.BiomeType.TUNDRA:              0.10,
		TileRegistry.BiomeType.SNOW:                0.00,
		TileRegistry.BiomeType.MOUNTAIN_ROCK:       0.05,
		TileRegistry.BiomeType.MOUNTAIN_SNOW:       0.00,
		TileRegistry.BiomeType.VOLCANIC:            0.18,
	}

	for y in range(data.height):
		for x in range(data.width):
			var base: float = FERTILITY.get(data.biome[y][x], 0.0)
			var drain_bonus: float = data.drainage[y][x] * 0.28
			data.prosperity[y][x] = clampf(base + drain_bonus - 0.12, 0.0, 1.0)
