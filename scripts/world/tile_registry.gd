class_name TileRegistry

## Defines all terrain/biome types and their visual representations.
## Colors are used by default; assign textures in BIOME_TEXTURES later
## to replace color blocks with actual sprites.

enum BiomeType {
	DEEP_OCEAN,
	OCEAN,
	SHALLOW_WATER,
	BEACH,
	DESERT,
	SAVANNA,
	TROPICAL_RAINFOREST,
	GRASSLAND,
	WOODLAND,
	TEMPERATE_FOREST,
	BOREAL_FOREST,
	TUNDRA,
	SNOW,
	MOUNTAIN_ROCK,
	MOUNTAIN_SNOW,
	VOLCANIC,
	RIVER,
	LAKE,
}

## Color for each biome type. Edit these to tune the visual style.
const BIOME_COLORS: Dictionary = {
	BiomeType.DEEP_OCEAN:           Color(0.02, 0.10, 0.35),
	BiomeType.OCEAN:                Color(0.06, 0.24, 0.60),
	BiomeType.SHALLOW_WATER:        Color(0.18, 0.46, 0.78),
	BiomeType.BEACH:                Color(0.87, 0.82, 0.58),
	BiomeType.DESERT:               Color(0.90, 0.78, 0.38),
	BiomeType.SAVANNA:              Color(0.74, 0.68, 0.28),
	BiomeType.TROPICAL_RAINFOREST:  Color(0.06, 0.46, 0.10),
	BiomeType.GRASSLAND:            Color(0.42, 0.70, 0.22),
	BiomeType.WOODLAND:             Color(0.28, 0.54, 0.18),
	BiomeType.TEMPERATE_FOREST:     Color(0.12, 0.42, 0.14),
	BiomeType.BOREAL_FOREST:        Color(0.08, 0.34, 0.24),
	BiomeType.TUNDRA:               Color(0.58, 0.62, 0.52),
	BiomeType.SNOW:                 Color(0.94, 0.94, 0.98),
	BiomeType.MOUNTAIN_ROCK:        Color(0.50, 0.46, 0.40),
	BiomeType.MOUNTAIN_SNOW:        Color(0.82, 0.84, 0.90),
	BiomeType.VOLCANIC:             Color(0.40, 0.08, 0.04),
	BiomeType.RIVER:                Color(0.22, 0.54, 0.92),
	BiomeType.LAKE:                 Color(0.10, 0.38, 0.75),
}

## Human-readable labels for each biome type.
const BIOME_NAMES: Dictionary = {
	BiomeType.DEEP_OCEAN:           "Deep Ocean",
	BiomeType.OCEAN:                "Ocean",
	BiomeType.SHALLOW_WATER:        "Shallow Water",
	BiomeType.BEACH:                "Beach",
	BiomeType.DESERT:               "Desert",
	BiomeType.SAVANNA:              "Savanna",
	BiomeType.TROPICAL_RAINFOREST:  "Tropical Rainforest",
	BiomeType.GRASSLAND:            "Grassland",
	BiomeType.WOODLAND:             "Woodland",
	BiomeType.TEMPERATE_FOREST:     "Temperate Forest",
	BiomeType.BOREAL_FOREST:        "Boreal Forest",
	BiomeType.TUNDRA:               "Tundra",
	BiomeType.SNOW:                 "Snow / Ice",
	BiomeType.MOUNTAIN_ROCK:        "Mountain Rock",
	BiomeType.MOUNTAIN_SNOW:        "Mountain Snow",
	BiomeType.VOLCANIC:             "Volcanic",
	BiomeType.RIVER:                "River",
	BiomeType.LAKE:                 "Lake",
}

## Optional: assign Texture2D here to replace color blocks with sprites.
## Example: BIOME_TEXTURES[BiomeType.GRASSLAND] = preload("res://art/grassland.png")
var BIOME_TEXTURES: Dictionary = {}

static func get_biome_color(biome: BiomeType) -> Color:
	return BIOME_COLORS.get(biome, Color.MAGENTA)

static func get_biome_name(biome: BiomeType) -> String:
	return BIOME_NAMES.get(biome, "Unknown")


## Colors used by Region and Local map views for TerrainType tiles.
const TERRAIN_COLORS: Dictionary = {
	WorldData.TerrainType.OCEAN:         Color(0.06, 0.24, 0.60),
	WorldData.TerrainType.SHALLOW_WATER: Color(0.18, 0.46, 0.78),
	WorldData.TerrainType.COAST:         Color(0.87, 0.82, 0.58),
	WorldData.TerrainType.PLAINS:        Color(0.42, 0.70, 0.22),
	WorldData.TerrainType.HILLS:         Color(0.62, 0.74, 0.30),
	WorldData.TerrainType.FOREST:        Color(0.12, 0.42, 0.14),
	WorldData.TerrainType.MOUNTAIN:      Color(0.50, 0.46, 0.40),
	WorldData.TerrainType.DESERT:        Color(0.90, 0.78, 0.38),
	WorldData.TerrainType.RIVER:         Color(0.22, 0.54, 0.92),
	WorldData.TerrainType.LAKE:          Color(0.10, 0.38, 0.75),
}

static func get_terrain_color(terrain: int) -> Color:
	return TERRAIN_COLORS.get(terrain, Color.MAGENTA)
