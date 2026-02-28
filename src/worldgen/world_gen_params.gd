## WorldGenParams — all tunable parameters for region generation.
## Load from JSON or use defaults. Passed through every generation pass
## so callers can reproduce any world from seed + params.
class_name WorldGenParams
extends RefCounted

# ── Grid dimensions ──────────────────────────────────────────────────────────
var grid_width:  int = 512
var grid_height: int = 512

# ── Altitude synthesis (3-layer blended noise) ───────────────────────────────
## Base layer — broad continent-scale shapes.
var base_freq:    float = 0.004
var base_octaves: int   = 6
var base_weight:  float = 0.55

## Detail layer — mid-frequency terrain wrinkles.
var detail_freq:    float = 0.012
var detail_octaves: int   = 4
var detail_weight:  float = 0.25

## Ridge layer — sharp mountain ridges (folded noise).
var ridge_freq:    float = 0.008
var ridge_octaves: int   = 4
var ridge_weight:  float = 0.20

## Island falloff exponent (1.0 = linear).
var island_falloff: float = 1.40
## If true, apply elliptical island falloff.
var island_mode:    bool  = true
## Fraction of tiles that become ocean (0.0–1.0).
var sea_ratio:      float = 0.44

# ── Climate ───────────────────────────────────────────────────────────────────
var climate_freq: float = 0.006
var temp_bias:    float = 0.0
var precip_bias:  float = 0.0

# ── Tectonics ────────────────────────────────────────────────────────────────
var num_plates:     int    = 12
var layout:         String = "pangea"   # "pangea" | "continents" | "archipelago"
var tectonic_blend: float  = 0.65

# ── Province and settlement ───────────────────────────────────────────────────
## 0 = auto-scale from grid area (~1 province per 21 000 tiles, clamped 4–64).
var num_provinces:        int = 0
var tiles_per_settlement: int = 10
## Min tile gap between any two placed settlements.
var min_settlement_sep:   int = 5

# ── History sim ───────────────────────────────────────────────────────────────
var run_history_sim: bool = true


static func default_params() -> WorldGenParams:
	return WorldGenParams.new()


## Returns the effective province count.
## When num_provinces == 0 the value is derived from grid area so it scales
## automatically across all size presets (128² → 1, 512² → 12, 1024² → 50).
func resolved_num_provinces() -> int:
	if num_provinces > 0:
		return num_provinces
	@warning_ignore("integer_division")
	return clampi(grid_width * grid_height / 21000, 4, 64)


func to_dict() -> Dictionary:
	return {
		"grid_width":           grid_width,
		"grid_height":          grid_height,
		"base_freq":            base_freq,
		"base_octaves":         base_octaves,
		"base_weight":          base_weight,
		"detail_freq":          detail_freq,
		"detail_octaves":       detail_octaves,
		"detail_weight":        detail_weight,
		"ridge_freq":           ridge_freq,
		"ridge_octaves":        ridge_octaves,
		"ridge_weight":         ridge_weight,
		"island_falloff":       island_falloff,
		"island_mode":          island_mode,
		"sea_ratio":            sea_ratio,
		"climate_freq":         climate_freq,
		"temp_bias":            temp_bias,
		"precip_bias":          precip_bias,
		"num_plates":           num_plates,
		"layout":               layout,
		"tectonic_blend":       tectonic_blend,
		"num_provinces":        num_provinces,
		"tiles_per_settlement": tiles_per_settlement,
		"min_settlement_sep":   min_settlement_sep,
		"run_history_sim":      run_history_sim,
	}


static func from_dict(d: Dictionary) -> WorldGenParams:
	var p := WorldGenParams.new()
	p.grid_width           = d.get("grid_width",           512)
	p.grid_height          = d.get("grid_height",          512)
	p.base_freq            = d.get("base_freq",            0.004)
	p.base_octaves         = d.get("base_octaves",         6)
	p.base_weight          = d.get("base_weight",          0.55)
	p.detail_freq          = d.get("detail_freq",          0.012)
	p.detail_octaves       = d.get("detail_octaves",       4)
	p.detail_weight        = d.get("detail_weight",        0.25)
	p.ridge_freq           = d.get("ridge_freq",           0.008)
	p.ridge_octaves        = d.get("ridge_octaves",        4)
	p.ridge_weight         = d.get("ridge_weight",         0.20)
	p.island_falloff       = d.get("island_falloff",       1.40)
	p.island_mode          = d.get("island_mode",          true)
	p.sea_ratio            = d.get("sea_ratio",            0.44)
	p.climate_freq         = d.get("climate_freq",         0.006)
	p.temp_bias            = d.get("temp_bias",            0.0)
	p.precip_bias          = d.get("precip_bias",          0.0)
	p.num_plates           = d.get("num_plates",           12)
	p.layout               = d.get("layout",               "pangea")
	p.tectonic_blend       = d.get("tectonic_blend",       0.65)
	p.num_provinces        = d.get("num_provinces",        0)
	p.tiles_per_settlement = d.get("tiles_per_settlement", 10)
	p.min_settlement_sep   = d.get("min_settlement_sep",   5)
	p.run_history_sim      = d.get("run_history_sim",      true)
	return p
