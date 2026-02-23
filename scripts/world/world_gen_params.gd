class_name WorldGenParams

## All tunable parameters for world generation.
## Passed into WorldGenerator.generate() and Hydrology.process().
## Duplicate before passing to a thread to avoid race conditions.

# ── Shape ────────────────────────────────────────────────────────────────
## Fraction of tiles that will be ocean (SEA_RATIO-th altitude percentile).
## 0.20 = mostly land   0.40 = balanced   0.65 = archipelago
var sea_ratio: float = 0.40

## Radial falloff strength; higher pushes altitude to zero near map edges.
## 0.15 = huge continent   0.35 = standard island   0.65 = tiny island
var island_falloff: float = 0.35

# ── Terrain ───────────────────────────────────────────────────────────────
## Altitude noise frequency; lower = broader landmasses, higher = jagged coast.
var noise_frequency: float = 0.003

## FBM octave count for altitude noise; more octaves = more fine terrain detail.
var noise_octaves: int = 6

# ── Tectonics ─────────────────────────────────────────────────────────────
## Blend weight for the base elevation noise (0.0–1.0 internal normalised).
var noise_factor: float = 0.40

## Blend weight for the crust layer.  Higher = plate boundaries push up visibly
## at coastlines and interior seams, producing continent-edge mountain ranges.
var crust_factor: float = 0.30

## Blend weight for tectonic activity spikes at plate boundaries.
## Higher = more dramatic mountain ridges and island chains along seams.
var tectonic_factor: float = 0.30

## Frequency of the crust noise.  Lower = bigger plates / fewer boundaries.
var crust_frequency: float = 0.002

# ── Climate ────────────────────────────────────────────────────────────────
## Added to the computed temperature after all other factors (−0.3 … +0.3).
## Negative = colder world (ice age).  Positive = warmer world (tropics).
var temp_bias: float = 0.0

## Added to the computed precipitation (−0.3 … +0.3).
## Negative = drier world (desert).  Positive = wetter world (lush).
var precip_bias: float = 0.0

# ── Hydrology ──────────────────────────────────────────────────────────────
## Minimum accumulated flow for a tile to become a river.
## Lower = more / wider rivers.   Higher = only major valleys get rivers.
var river_threshold: float = 60.0

# ── Settlements ───────────────────────────────────────────────────────────────
## Number of province capitals (hub towns) to place via global Poisson.
## More hubs = smaller provinces with fewer spokes each.
var num_provinces: int = 20

## Controls how densely spokes fill each province.
## One spoke is placed per this many province tiles (lower = denser settlement).
var tiles_per_settlement: int = 12

## Depression tiles are flooded this far above the basin minimum to form lakes.
var lake_fill_depth: float = 0.026


## Returns a shallow copy safe to pass to a worker thread.
func duplicate() -> WorldGenParams:
	var c := WorldGenParams.new()
	c.sea_ratio        = sea_ratio
	c.island_falloff   = island_falloff
	c.noise_frequency  = noise_frequency
	c.noise_octaves    = noise_octaves
	c.noise_factor     = noise_factor
	c.crust_factor     = crust_factor
	c.tectonic_factor  = tectonic_factor
	c.crust_frequency  = crust_frequency
	c.temp_bias        = temp_bias
	c.precip_bias      = precip_bias
	c.river_threshold  = river_threshold
	c.lake_fill_depth  = lake_fill_depth
	c.num_provinces         = num_provinces
	c.tiles_per_settlement  = tiles_per_settlement
	return c


## Named preset factory methods ──────────────────────────────────────────

static func make_default() -> WorldGenParams:
	return WorldGenParams.new()


static func make_pangaea() -> WorldGenParams:
	var p := WorldGenParams.new()
	p.sea_ratio       = 0.25
	p.island_falloff  = 0.18
	p.noise_frequency = 0.002
	p.noise_octaves   = 7
	p.crust_factor    = 0.45  # strong plate identity → big landmass edges
	p.tectonic_factor = 0.15  # few boundaries → fewer interior ridges
	p.crust_frequency = 0.0015
	return p


static func make_archipelago() -> WorldGenParams:
	var p := WorldGenParams.new()
	p.sea_ratio       = 0.65
	p.island_falloff  = 0.60
	p.noise_frequency = 0.006
	p.noise_octaves   = 5
	p.crust_factor    = 0.20
	p.tectonic_factor = 0.45  # many small plate collisions → island chains
	p.crust_frequency = 0.005
	p.precip_bias     = 0.10
	p.river_threshold = 40.0
	return p


static func make_ice_age() -> WorldGenParams:
	var p := WorldGenParams.new()
	p.temp_bias       = -0.28
	p.precip_bias     = -0.10
	p.river_threshold = 80.0
	return p


static func make_desert() -> WorldGenParams:
	var p := WorldGenParams.new()
	p.temp_bias       =  0.22
	p.precip_bias     = -0.28
	p.river_threshold = 120.0
	p.lake_fill_depth = 0.010
	return p


static func make_lush() -> WorldGenParams:
	var p := WorldGenParams.new()
	p.temp_bias       =  0.12
	p.precip_bias     =  0.28
	p.river_threshold = 35.0
	p.lake_fill_depth = 0.040
	return p


## ── Geological presets ───────────────────────────────────────────────────

static func make_ring_of_fire() -> WorldGenParams:
	var p := WorldGenParams.new()
	p.sea_ratio       = 0.55   # mostly ocean
	p.island_falloff  = 0.30
	p.crust_factor    = 0.25
	p.tectonic_factor = 0.55   # strong ridges at plate edges → volcanic arcs
	p.crust_frequency = 0.004
	p.noise_factor    = 0.20
	p.river_threshold = 45.0
	return p


static func make_ancient_craton() -> WorldGenParams:
	var p := WorldGenParams.new()
	p.sea_ratio       = 0.35
	p.island_falloff  = 0.20   # sweeping continent
	p.crust_factor    = 0.55   # dominant plate identity
	p.tectonic_factor = 0.05   # near-zero mountains
	p.noise_factor    = 0.40
	p.crust_frequency = 0.0012 # very large, stable plates
	p.noise_octaves   = 4      # smooth, flat terrain
	return p


static func make_rift_valley() -> WorldGenParams:
	var p := WorldGenParams.new()
	p.sea_ratio       = 0.40
	p.crust_factor    = 0.45
	p.tectonic_factor = 0.45   # high ridges at seams → deep rift depressions
	p.crust_frequency = 0.003
	p.noise_factor    = 0.10
	p.river_threshold = 30.0   # lots of rivers draining into rifts
	p.lake_fill_depth = 0.050  # big rift lakes
	return p


## ── Climate presets ─────────────────────────────────────────────────────

static func make_hothouse() -> WorldGenParams:
	var p := WorldGenParams.new()
	p.temp_bias       =  0.30  # maximum warmth → no snow, minimal tundra
	p.precip_bias     =  0.20  # very wet
	p.river_threshold = 25.0   # dense river network
	p.lake_fill_depth = 0.045
	return p


static func make_snowball() -> WorldGenParams:
	var p := WorldGenParams.new()
	p.temp_bias       = -0.30  # maximum cold → everything freezes
	p.precip_bias     = -0.15  # cold air holds little moisture
	p.river_threshold = 150.0  # rivers almost nonexistent
	p.lake_fill_depth = 0.008
	return p


static func make_monsoon() -> WorldGenParams:
	var p := WorldGenParams.new()
	p.temp_bias       =  0.05
	p.precip_bias     =  0.30  # extreme rainfall everywhere
	p.river_threshold = 20.0   # rivers on nearly every valley
	p.lake_fill_depth = 0.055  # large floodplain lakes
	return p


## ── Terrain shape presets ───────────────────────────────────────────────

static func make_inland_sea() -> WorldGenParams:
	var p := WorldGenParams.new()
	# Very weak falloff so land reaches the map edges.
	# Low crust + high tectonic at medium frequency creates a ring of ridges
	# around a central depression that tends to sink below sea level.
	p.sea_ratio       = 0.50
	p.island_falloff  = 0.05   # nearly uniform — no edge taper
	p.noise_factor    = 0.15
	p.crust_factor    = 0.20
	p.tectonic_factor = 0.65   # strong ridges at boundary ring
	p.crust_frequency = 0.003
	p.noise_frequency = 0.004
	p.river_threshold = 30.0
	p.lake_fill_depth = 0.060  # large central lake if depression forms
	return p


static func make_fractal_coast() -> WorldGenParams:
	var p := WorldGenParams.new()
	p.noise_frequency = 0.007  # high frequency → jagged coastlines
	p.noise_octaves   = 9      # maximum detail
	p.noise_factor    = 0.55
	p.crust_factor    = 0.25
	p.tectonic_factor = 0.20
	p.island_falloff  = 0.40
	return p


static func make_highlands() -> WorldGenParams:
	var p := WorldGenParams.new()
	p.sea_ratio       = 0.25   # most of the map is elevated land
	p.island_falloff  = 0.15   # very flat falloff → plateau reaches the edges
	p.noise_factor    = 0.65   # terrain noise dominant
	p.crust_factor    = 0.25
	p.tectonic_factor = 0.10   # few dramatic ridges
	p.noise_octaves   = 5
	return p
