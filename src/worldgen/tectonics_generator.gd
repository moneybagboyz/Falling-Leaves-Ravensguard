## TectonicsGenerator — plate-tectonic elevation modifier.
##
## Runs AFTER the noise-based altitude pass fills WorldGenData.altitude and
## BEFORE sea_level is quantile-derived.  It blends a Voronoi plate map
## (continental vs oceanic) and boundary-stress effects into the existing
## noise altitude, so the two systems reinforce each other.
##
## Improvements over original:
##   • Poisson-disk plate seed placement (uniform coverage, no tight clusters).
##   • Domain-warp applied to Voronoi distances (organic plate boundaries).
##   • Domain-warp applied to continent mask (irregular, non-circular coastlines).
##   • Subduction asymmetry: oceanic trench on subducting side, arc on overriding side.
##
## Layout options (params.layout):
##   "pangea"       — single large continent, ocean at edges
##   "continents"   — two continental blobs east/west
##   "archipelago"  — scattered islands, mostly ocean
class_name TectonicsGenerator
extends RefCounted

## Boundary influence radius in tiles (jittered per vertex).
const INFLUENCE_BASE:  float = 14.0
## Boundary stress exponent (higher = sharper mountain ridges).
const BOUNDARY_POWER:  float = 1.5
## Domain-warp amplitude for plate boundary distortion (tiles).
const VORONOI_WARP:    float = 18.0
## Domain-warp amplitude for continent mask distortion (tiles).
const MASK_WARP:       float = 22.0


## Blend tectonic plate elevation into data.altitude in-place.
## Call this after _fill_altitude_noise() but before sea_level quantile.
static func blend(
		data:       WorldGenData,
		params:     WorldGenParams,
		world_seed: int
) -> void:

	var rng := RandomNumberGenerator.new()
	rng.seed = world_seed ^ 0xC0FFEE42

	var W: int    = data.width
	var H: int    = data.height
	var Wf: float = float(W)
	var Hf: float = float(H)

	var world_center := Vector2(Wf * 0.5, Hf * 0.5)
	var cont_centers := [
		Vector2(Wf * 0.25, Hf * 0.5),
		Vector2(Wf * 0.75, Hf * 0.5),
	]

	# ── Shared noise instances ───────────────────────────────────────────────
	var noise_jitter := FastNoiseLite.new()
	noise_jitter.noise_type = FastNoiseLite.TYPE_SIMPLEX
	noise_jitter.seed       = rng.randi()
	noise_jitter.frequency  = 0.030

	# Low-frequency warp noise drives large-scale organic plate shapes.
	var noise_warp := FastNoiseLite.new()
	noise_warp.noise_type = FastNoiseLite.TYPE_SIMPLEX
	noise_warp.seed       = rng.randi()
	noise_warp.frequency  = 0.018

	# ── Poisson-disk plate seed placement ────────────────────────────────────
	# Enforce a minimum separation so seeds are spread evenly across the map,
	# preventing clusters of tiny plates in one area and huge plates elsewhere.
	var num_plates: int  = params.num_plates
	var layout: String   = params.layout
	var min_sep: float   = sqrt(Wf * Hf / float(num_plates)) * 0.70
	var plates: Array    = []

	for _i in range(num_plates):
		var sp := Vector2.ZERO
		var placed := false
		for _t in range(30):
			var cand := Vector2(rng.randf_range(0.0, Wf), rng.randf_range(0.0, Hf))
			var ok := true
			for existing: Dictionary in plates:
				if cand.distance_to(existing.seed) < min_sep:
					ok = false
					break
			if ok:
				sp = cand
				placed = true
				break
		if not placed:
			# Fallback: accept any random position (avoids stall on dense grids).
			sp = Vector2(rng.randf_range(0.0, Wf), rng.randf_range(0.0, Hf))

		var oceanic: bool = true
		match layout:
			"pangea":
				var nd: float = sp.distance_to(world_center) / (minf(Wf, Hf) * 0.5)
				oceanic = nd > 0.65
				if nd < 0.25: oceanic = false
			"continents":
				for c: Vector2 in cont_centers:
					if sp.distance_to(c) < minf(Wf, Hf) * 0.35:
						oceanic = false
						break
			"archipelago":
				oceanic = rng.randf() > 0.25

		var vel: Vector2
		if oceanic:
			vel = Vector2(
				rng.randf_range(-1.5, 1.5),
				rng.randf_range(-1.5, 1.5)
			).normalized() * 2.0
		else:
			var target: Vector2 = world_center
			if layout == "continents":
				target = cont_centers[0] if sp.x < Wf * 0.5 else cont_centers[1]
			vel = (target - sp).normalized() * rng.randf_range(0.3, 1.2)

		plates.append({ "seed": sp, "vel": vel, "oceanic": oceanic })

	# ── Per-tile: domain-warped Voronoi + boundary stress ─────────────────────
	for y in range(H):
		for x in range(W):
			var xf: float = float(x)
			var yf: float = float(y)
			var pos_v := Vector2(xf, yf)

			# Domain-warp pos_v for Voronoi distance computation.
			# This distorts plate boundaries from straight Voronoi lines into
			# organic, irregular shapes.
			var warp_v := pos_v + Vector2(
				noise_warp.get_noise_2d(xf, yf),
				noise_warp.get_noise_2d(yf, xf)
			) * VORONOI_WARP

			# Find two nearest plates (Voronoi in warped space).
			var d1: float = INF
			var d2: float = INF
			var p1: Dictionary = {}
			var p2: Dictionary = {}

			for p: Dictionary in plates:
				var d: float = warp_v.distance_to(p.seed)
				if d < d1:
					d2 = d1; p2 = p1
					d1 = d;  p1 = p
				elif d < d2:
					d2 = d; p2 = p

			if p1.is_empty():
				continue

			# Base elevation by plate type.
			var plate_e: float = 0.12 if p1.oceanic else 0.42

			# Boundary zone stress.
			if not p2.is_empty():
				var boundary_dist: float = d2 - d1
				var jitter: float        = noise_jitter.get_noise_2d(xf, yf) * 8.0
				var influence: float     = INFLUENCE_BASE + jitter

				if boundary_dist < influence:
					var weight: float    = pow(1.0 - (boundary_dist / influence), BOUNDARY_POWER)
					var normal: Vector2  = (p2.seed - p1.seed).normalized()
					# Positive dot → plates colliding (convergent) → mountains.
					# Negative dot → plates separating (divergent) → rifts.
					var stress: float    = p1.vel.dot(normal) - p2.vel.dot(normal)
					var variation: float = 0.8 + (noise_jitter.get_noise_2d(yf, xf) * 0.4)

					if stress > 0.0:
						if not p1.oceanic and not p2.oceanic:
							# Continental collision — symmetric ridge on both sides.
							plate_e += stress * 0.38 * weight * variation
						elif p1.oceanic != p2.oceanic:
							# Subduction — asymmetric.
							# Oceanic plate dives under continental → trench on oceanic side,
							# volcanic arc on continental side.
							if p1.oceanic:
								# p1 is the subducting plate → oceanic trench.
								plate_e -= stress * 0.22 * weight
							else:
								# p1 is the overriding continental plate → volcanic arc.
								plate_e += stress * 0.35 * weight * variation
						else:
							# Ocean–ocean subduction — modest arc.
							plate_e += stress * 0.20 * weight * variation
					else:
						plate_e += stress * 0.25 * weight  # Divergent rift (stress negative).

			# ── Layout continent mask (domain-warped for irregular coastlines) ──
			# Use a separate warp offset so mask distortion is independent of
			# the Voronoi warp, giving coastlines their own organic shape.
			var mpos := pos_v + Vector2(
				noise_warp.get_noise_2d(xf * 0.5, yf * 0.5 + 500.0),
				noise_warp.get_noise_2d(yf * 0.5 + 500.0, xf * 0.5)
			) * MASK_WARP

			var mask: float = 1.0
			match layout:
				"pangea":
					var nd: float = mpos.distance_to(world_center) / (minf(Wf, Hf) * 0.58)
					mask = clampf(1.4 - nd, 0.0, 1.0)
				"continents":
					var m1: float = clampf(
						1.2 - mpos.distance_to(cont_centers[0]) / (minf(Wf, Hf) * 0.40), 0.0, 1.0)
					var m2: float = clampf(
						1.2 - mpos.distance_to(cont_centers[1]) / (minf(Wf, Hf) * 0.40), 0.0, 1.0)
					mask = maxf(m1, m2)
				"archipelago":
					mask = 1.0  # Island falloff in noise pass handles shape.

			plate_e = clampf(plate_e * mask, 0.0, 1.0)

			# Blend: noise drives fine detail, tectonics drives broad continents.
			var noise_e: float = data.altitude[y][x]
			data.altitude[y][x] = clampf(
				lerpf(noise_e, plate_e, params.tectonic_blend), 0.0, 1.0
			)
