shader_type canvas_item;

// Grid texture: each pixel represents one tile (R channel = terrain type ID)
uniform sampler2D grid_texture : filter_nearest;

// Camera and zoom parameters
uniform vec2 camera_pos = vec2(100.0, 100.0);
uniform float zoom = 1.0;
uniform vec2 grid_size = vec2(200.0, 200.0);

// Grid lines toggle
uniform bool show_grid_lines = false;

// Graphics mode: 0=solid colors, 1=procedural graphics
uniform int graphics_mode = 1;

// Noise/Hash functions for procedural generation
float hash(vec2 p) {
	return fract(sin(dot(p, vec2(12.9898, 78.233))) * 43758.5453);
}

float noise(vec2 p) {
	vec2 i = floor(p);
	vec2 f = fract(p);
	f = f * f * (3.0 - 2.0 * f);
	float a = hash(i);
	float b = hash(i + vec2(1.0, 0.0));
	float c = hash(i + vec2(0.0, 1.0));
	float d = hash(i + vec2(1.0, 1.0));
	return mix(mix(a, b, f.x), mix(c, d, f.x), f.y);
}

// Procedural terrain drawing functions
vec3 draw_ocean(vec2 world_pos, vec2 tile_local) {
	// Animated water with waves
	float wave1 = sin(world_pos.x * 0.5 + world_pos.y * 0.3) * 0.5 + 0.5;
	float wave2 = sin(world_pos.x * 0.3 - world_pos.y * 0.4) * 0.5 + 0.5;
	float foam = noise(world_pos * 4.0) * wave1 * wave2;
	vec3 deep_blue = vec3(0.08, 0.15, 0.45);
	vec3 light_blue = vec3(0.15, 0.30, 0.60);
	return mix(deep_blue, light_blue, foam * 0.3);
}

vec3 draw_plains(vec2 world_pos, vec2 tile_local) {
	// Grass with subtle variation
	float grass_pattern = noise(world_pos * 8.0);
	float grass_detail = noise(world_pos * 32.0) * 0.3;
	vec3 dark_green = vec3(0.10, 0.65, 0.30);
	vec3 light_green = vec3(0.20, 0.85, 0.45);
	return mix(dark_green, light_green, grass_pattern + grass_detail);
}

vec3 draw_forest(vec2 world_pos, vec2 tile_local) {
	// Dense trees pattern
	float tree_pattern = step(0.6, noise(world_pos * 3.0));
	float canopy = noise(world_pos * 12.0);
	vec3 dark_green = vec3(0.06, 0.30, 0.15);
	vec3 medium_green = vec3(0.12, 0.45, 0.25);
	vec3 canopy_green = vec3(0.15, 0.50, 0.28);
	vec3 base = mix(dark_green, medium_green, tree_pattern);
	return mix(base, canopy_green, canopy * 0.4);
}

vec3 draw_jungle(vec2 world_pos, vec2 tile_local) {
	// Very dense vegetation
	float vegetation = noise(world_pos * 6.0);
	float detail = noise(world_pos * 24.0);
	vec3 dark = vec3(0.15, 0.45, 0.20);
	vec3 bright = vec3(0.35, 0.70, 0.35);
	return mix(dark, bright, vegetation * 0.7 + detail * 0.3);
}

vec3 draw_desert(vec2 world_pos, vec2 tile_local) {
	// Sand dunes
	float dune = sin(world_pos.x * 0.8 + world_pos.y * 0.4) * 0.5 + 0.5;
	float sand_grain = noise(world_pos * 16.0);
	vec3 light_sand = vec3(0.95, 0.85, 0.55);
	vec3 dark_sand = vec3(0.85, 0.70, 0.35);
	return mix(dark_sand, light_sand, dune * 0.6 + sand_grain * 0.4);
}

vec3 draw_tundra(vec2 world_pos, vec2 tile_local) {
	// Icy/snowy ground
	float ice_pattern = noise(world_pos * 4.0);
	float snow = noise(world_pos * 16.0);
	vec3 white = vec3(0.85, 0.90, 0.95);
	vec3 ice_blue = vec3(0.75, 0.82, 0.90);
	return mix(ice_blue, white, ice_pattern * 0.5 + snow * 0.5);
}

vec3 draw_mountain(vec2 world_pos, vec2 tile_local) {
	// Rocky mountain with snow cap
	float height = 1.0 - tile_local.y;
	float rock_pattern = noise(world_pos * 8.0);
	float rock_detail = noise(world_pos * 32.0);
	
	// Snow cap at top
	if (tile_local.y < 0.25) {
		vec3 snow = vec3(0.92, 0.94, 0.98);
		vec3 ice = vec3(0.80, 0.85, 0.92);
		return mix(ice, snow, rock_detail);
	}
	
	// Rocky slopes
	vec3 dark_rock = vec3(0.35, 0.38, 0.42);
	vec3 light_rock = vec3(0.50, 0.53, 0.58);
	return mix(dark_rock, light_rock, rock_pattern * 0.6 + rock_detail * 0.4);
}

vec3 draw_hills(vec2 world_pos, vec2 tile_local) {
	// Rolling hills
	float hill = sin(world_pos.x * 1.2) * 0.3 + sin(world_pos.y * 0.8) * 0.3 + 0.5;
	float grass = noise(world_pos * 12.0);
	vec3 dark_brown = vec3(0.50, 0.35, 0.15);
	vec3 light_brown = vec3(0.70, 0.50, 0.20);
	vec3 green = vec3(0.40, 0.60, 0.25);
	vec3 base = mix(dark_brown, light_brown, hill);
	return mix(base, green, grass * 0.4);
}

vec3 draw_swamp(vec2 world_pos, vec2 tile_local) {
	// Murky water with vegetation
	float water = noise(world_pos * 3.0);
	float vegetation = step(0.5, noise(world_pos * 8.0));
	vec3 murky_water = vec3(0.10, 0.30, 0.28);
	vec3 dark_green = vec3(0.08, 0.40, 0.35);
	vec3 vegetation_color = vec3(0.15, 0.50, 0.40);
	vec3 base = mix(murky_water, dark_green, water);
	return mix(base, vegetation_color, vegetation * 0.5);
}

vec3 draw_river(vec2 world_pos, vec2 tile_local) {
	// Flowing water
	float flow = sin(world_pos.x * 2.0 + world_pos.y) * 0.5 + 0.5;
	float ripple = noise(world_pos * 10.0);
	vec3 water = vec3(0.05, 0.60, 0.75);
	vec3 light_water = vec3(0.10, 0.75, 0.90);
	return mix(water, light_water, flow * 0.4 + ripple * 0.3);
}

vec3 draw_road(vec2 world_pos, vec2 tile_local) {
	// Dirt/stone road
	float stones = step(0.7, noise(world_pos * 16.0));
	float dirt = noise(world_pos * 8.0);
	vec3 dark_dirt = vec3(0.50, 0.45, 0.35);
	vec3 light_dirt = vec3(0.65, 0.55, 0.40);
	vec3 stone = vec3(0.55, 0.55, 0.55);
	vec3 base = mix(dark_dirt, light_dirt, dirt);
	return mix(base, stone, stones * 0.3);
}

// Get procedural terrain graphic
vec3 get_terrain_graphic(float terrain_id, vec2 world_pos) {
	int id = int(terrain_id * 255.0);
	vec2 tile_local = fract(world_pos);
	
	// Province mode colors (A-Z = 65-90)
	if (id >= 65 && id <= 90) {
		int province_id = id - 65;
		float hue = float(province_id) * 0.382;
		hue = fract(hue);
		float r = abs(hue * 6.0 - 3.0) - 1.0;
		float g = 2.0 - abs(hue * 6.0 - 2.0);
		float b = 2.0 - abs(hue * 6.0 - 4.0);
		return clamp(vec3(r, g, b), 0.0, 1.0) * 0.8 + 0.2;
	}
	
	// Political mode colors (a-z = 97-122)
	if (id >= 97 && id <= 122) {
		int faction_id = id - 97;
		float hue = float(faction_id) * 0.618;
		hue = fract(hue);
		float r = abs(hue * 6.0 - 3.0) - 1.0;
		float g = 2.0 - abs(hue * 6.0 - 2.0);
		float b = 2.0 - abs(hue * 6.0 - 4.0);
		return clamp(vec3(r, g, b), 0.0, 1.0) * 0.7 + 0.3;
	}
	
	// Terrain graphics
	if (id == 126) return draw_ocean(world_pos, tile_local);     // Ocean '~'
	if (id == 46) return draw_plains(world_pos, tile_local);     // Plains '.'
	if (id == 35) return draw_forest(world_pos, tile_local);     // Forest '#'
	if (id == 38) return draw_jungle(world_pos, tile_local);     // Jungle '&'
	if (id == 34) return draw_desert(world_pos, tile_local);     // Desert '"'
	if (id == 42) return draw_tundra(world_pos, tile_local);     // Tundra '*'
	if (id == 94 || id == 79) return draw_mountain(world_pos, tile_local); // Mountains '^', 'O'
	if (id == 111 || id == 110) return draw_hills(world_pos, tile_local);  // Hills 'o', 'n'
	if (id == 118) return draw_swamp(world_pos, tile_local);     // Swamp 'v'
	if (id == 61 || id == 47 || id == 92) return draw_river(world_pos, tile_local); // River '=', '/', '\\'
	if (id == 61) return draw_road(world_pos, tile_local);       // Road '='
	
	return vec3(0.3, 0.3, 0.3); // Default gray
}

// Fallback solid colors
vec3 get_terrain_color(float terrain_id) {
    // Convert ASCII chars to colors
    int id = int(terrain_id * 255.0);
    
    // Province mode colors (A-Z = 65-90)
    if (id >= 65 && id <= 90) {
        int province_id = id - 65;
        // Generate distinct colors for provinces
        float hue = float(province_id) * 0.382; // Golden ratio for good distribution
        hue = fract(hue);
        // Convert HSV to RGB (simplified)
        float r = abs(hue * 6.0 - 3.0) - 1.0;
        float g = 2.0 - abs(hue * 6.0 - 2.0);
        float b = 2.0 - abs(hue * 6.0 - 4.0);
        return clamp(vec3(r, g, b), 0.0, 1.0) * 0.8 + 0.2; // Add brightness
    }
    
    // Political mode colors (a-z = 97-122)
    if (id >= 97 && id <= 122) {
        int faction_id = id - 97;
        // Different color scheme for political
        float hue = float(faction_id) * 0.618; // Different distribution
        hue = fract(hue);
        float r = abs(hue * 6.0 - 3.0) - 1.0;
        float g = 2.0 - abs(hue * 6.0 - 2.0);
        float b = 2.0 - abs(hue * 6.0 - 4.0);
        return clamp(vec3(r, g, b), 0.0, 1.0) * 0.7 + 0.3; // Slightly brighter
    }
    
    // Ocean/Water (~)
    if (id == 126) return vec3(0.12, 0.23, 0.54); // Blue #1e3a8a
    
    // Plains (.)
    if (id == 46) return vec3(0.13, 0.77, 0.37); // Green #22c55e
    
    // Forest (#)
    if (id == 35) return vec3(0.09, 0.40, 0.20); // Dark green #166534
    
    // Jungle (&)
    if (id == 38) return vec3(0.3, 0.6, 0.3); // Medium green
    
    // Desert (")
    if (id == 34) return vec3(0.98, 0.75, 0.14); // Yellow #fbbf24
    
    // Tundra (*) 
    if (id == 42) return vec3(0.82, 0.84, 0.86); // Light gray #d1d5db
    
    // Mountains (^, O, o)
    if (id == 94 || id == 79 || id == 111) return vec3(0.42, 0.45, 0.50); // Gray #6b7280
    
    // Hills (n)
    if (id == 110) return vec3(0.63, 0.38, 0.03); // Brown #a16207
    
    // Swamp (v)
    if (id == 118) return vec3(0.07, 0.37, 0.35); // Dark cyan #115e59
    
    // River (≈) - using = as approximation
    if (id == 61) return vec3(0.02, 0.71, 0.83); // Cyan #06b6d4
    
    // Default fallback
    return vec3(0.3, 0.3, 0.3); // Gray
}

void fragment() {
    // Calculate visible portion based on zoom
    vec2 visible_size = grid_size / zoom;
    
    // Transform UV to world coordinates
    vec2 world_pos = camera_pos + (UV - 0.5) * visible_size;
    
    // Convert to texture UV (0-1 range)
    vec2 tex_uv = world_pos / grid_size;
    
    // Debug: Show UV coordinates as colors if outside bounds
    if (tex_uv.x < 0.0 || tex_uv.x > 1.0 || tex_uv.y < 0.0 || tex_uv.y > 1.0) {
        // Show red if out of bounds (this helps debug)
        COLOR = vec4(1.0, 0.0, 0.0, 1.0);
        return;
    }
    
    // Sample the grid texture
    float terrain_id = texture(grid_texture, tex_uv).r;
    
    // Debug: If texture returns 0, show blue (means texture is empty/null)
    if (terrain_id < 0.001) {
        COLOR = vec4(0.0, 0.0, 1.0, 1.0); // Blue for empty texture
        return;
    }
    
    // Get color for this terrain type
    vec3 color;
    if (graphics_mode == 1) {
        color = get_terrain_graphic(terrain_id, world_pos);
    } else {
        color = get_terrain_color(terrain_id);
    }
    
    // Grid lines (optional)
    if (show_grid_lines) {
        vec2 tile_local = fract(world_pos);
        if (tile_local.x < 0.03 || tile_local.y < 0.03) {
            color = mix(color, vec3(0.2, 0.2, 0.2), 0.4);
        }
    }
    
    COLOR = vec4(color, 1.0);
}
