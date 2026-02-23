class_name MapRenderer

## Converts a WorldData layer into an ImageTexture for display.
## Each view mode has its own colour-mapping function.
## Pixel (x, y) directly corresponds to world cell (x, y).

enum ViewMode {
	BIOME,
	ALTITUDE,
	TEMPERATURE,
	PRECIPITATION,
	DRAINAGE,
	PROSPERITY,
	FLOW,
	PROVINCES,
}

const MODE_LABELS: Dictionary = {
	ViewMode.BIOME:         "Biome",
	ViewMode.ALTITUDE:      "Altitude",
	ViewMode.TEMPERATURE:   "Temperature",
	ViewMode.PRECIPITATION: "Precipitation",
	ViewMode.DRAINAGE:      "Drainage",
	ViewMode.PROSPERITY:    "Prosperity",
	ViewMode.FLOW:          "River Flow",
	ViewMode.PROVINCES:     "Provinces",
}


## Build a full-resolution ImageTexture from the chosen view mode.
static func render(data: WorldData, mode: ViewMode) -> ImageTexture:
	return ImageTexture.create_from_image(render_image(data, mode))


## Returns the raw Image (before texture creation) so callers can paint overlays.
static func render_image(data: WorldData, mode: ViewMode) -> Image:
	var img := Image.create(data.width, data.height, false, Image.FORMAT_RGB8)

	for y in range(data.height):
		for x in range(data.width):
			var color: Color
			match mode:
				ViewMode.BIOME:
					color = _biome_shaded(data.biome[y][x], data.altitude[y][x], data.sea_level)
				ViewMode.ALTITUDE:
					color = _altitude_color(data.altitude[y][x], data.sea_level)
				ViewMode.TEMPERATURE:
					color = _temperature_color(data.temperature[y][x])
				ViewMode.PRECIPITATION:
					color = _precipitation_color(data.precipitation[y][x])
				ViewMode.DRAINAGE:
					color = _grayscale(data.drainage[y][x])
				ViewMode.PROSPERITY:
					color = _prosperity_color(data.prosperity[y][x])
				ViewMode.FLOW:
					color = _flow_color(data.flow[y][x])
				ViewMode.PROVINCES:
					var pid: int = data.province_id[y][x]
					if pid < 0:
						color = Color(0.04, 0.06, 0.16)
					else:
						color = _province_color(pid)
				_:
					color = Color.BLACK
			img.set_pixel(x, y, color)

	return img


# ---------------------------------------------------------------------------
# Colour-mapping helpers
# ---------------------------------------------------------------------------

## Returns a deterministic colour for a province id using golden-ratio hue spacing.
static func _province_color(pid: int) -> Color:
	var h: float = fmod(float(pid) * 0.618033988749895, 1.0)
	return Color.from_hsv(h, 0.60, 0.78)


## Biome base colour with altitude shading:
## – land tiles darken up to 40 % toward mountain peaks (topographic depth)
## – ocean tiles stay flat (their biome colours already encode depth)
## – rivers/lakes are unshaded so they stay vivid
static func _biome_shaded(biome: TileRegistry.BiomeType, alt: float, sea_level: float) -> Color:
	var c: Color = TileRegistry.get_biome_color(biome)
	# Water and special tiles: no shading
	if biome == TileRegistry.BiomeType.DEEP_OCEAN \
			or biome == TileRegistry.BiomeType.OCEAN \
			or biome == TileRegistry.BiomeType.SHALLOW_WATER \
			or biome == TileRegistry.BiomeType.RIVER \
			or biome == TileRegistry.BiomeType.LAKE:
		return c
	# Land: higher altitude = darker
	var land_t: float = clampf((alt - sea_level) / (1.0 - sea_level), 0.0, 1.0)
	var shade: float = 1.0 - land_t * 0.40
	return Color(c.r * shade, c.g * shade, c.b * shade, c.a)

## Deep blue (sea) through green/yellow (lowland) to white (peaks).
static func _altitude_color(v: float, sea_level: float) -> Color:
	if v < sea_level:
		var t: float = v / sea_level
		return Color(0.0, t * 0.28, 0.38 + t * 0.40)
	else:
		var t: float = (v - sea_level) / (1.0 - sea_level)
		if t < 0.28:
			return Color.from_hsv(0.28, 0.75 - t * 0.5, 0.40 + t * 0.6)
		elif t < 0.58:
			var u: float = (t - 0.28) / 0.30
			return Color.from_hsv(lerpf(0.18, 0.08, u), 0.55, 0.70 + u * 0.15)
		elif t < 0.82:
			var u: float = (t - 0.58) / 0.24
			return Color(lerpf(0.50, 0.62, u), lerpf(0.48, 0.58, u), lerpf(0.40, 0.55, u))
		else:
			var u: float = (t - 0.82) / 0.18
			return Color(lerpf(0.62, 0.96, u), lerpf(0.58, 0.96, u), lerpf(0.55, 1.0, u))


## Blue (cold) → cyan → green → yellow → red (hot).
static func _temperature_color(v: float) -> Color:
	if v < 0.25:
		var t: float = v / 0.25
		return Color(0.0, lerpf(0.0, 0.55, t), lerpf(0.80, 0.30, t))
	elif v < 0.50:
		var t: float = (v - 0.25) / 0.25
		return Color(0.0, lerpf(0.55, 0.85, t), lerpf(0.30, 0.0, t))
	elif v < 0.75:
		var t: float = (v - 0.50) / 0.25
		return Color(lerpf(0.0, 1.0, t), lerpf(0.85, 1.0, t), 0.0)
	else:
		var t: float = (v - 0.75) / 0.25
		return Color(1.0, lerpf(1.0, 0.0, t), 0.0)


## Near-black (dry) → deep blue → bright cyan (wet).
static func _precipitation_color(v: float) -> Color:
	return Color(lerpf(0.0, 0.0, v), lerpf(0.0, 0.65, v), lerpf(0.10, 1.0, v))


## Simple greyscale.
static func _grayscale(v: float) -> Color:
	return Color(v, v, v)


## Dark brown (barren) → rich green (fertile).
static func _prosperity_color(v: float) -> Color:
	return Color(
		lerpf(0.20, 0.16, v),
		lerpf(0.12, 0.72, v),
		lerpf(0.04, 0.12, v)
	)


## Black (no flow) → dark blue → bright cyan (high flow).
## Uses a square-root remap so that minor tributaries are still visible.
static func _flow_color(v: float) -> Color:
	var t: float = sqrt(clampf(v, 0.0, 1.0))
	return Color(0.0, lerpf(0.0, 0.72, t), lerpf(0.0, 1.0, t))
