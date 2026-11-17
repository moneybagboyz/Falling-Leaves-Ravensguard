shader_type canvas_item;

// Textures
uniform sampler2D terrain_texture : filter_nearest;  // R channel = terrain ASCII
uniform sampler2D unit_texture : filter_nearest;     // R channel = unit ASCII, G channel = team (0=player, 255=enemy)
uniform sampler2D projectile_texture : filter_nearest; // R channel = projectile ASCII

// Camera and zoom parameters
uniform vec2 camera_pos = vec2(100.0, 100.0);
uniform float zoom = 1.0;
uniform vec2 grid_size = vec2(200.0, 200.0);

// Grid lines toggle
uniform bool show_grid_lines = false;

// Graphics mode: 0=solid colors, 1=procedural graphics
uniform int graphics_mode = 1;

// Noise/Hash functions
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

// Battle terrain graphics
vec3 draw_battle_plains(vec2 world_pos) {
	float grass = noise(world_pos * 8.0);
	vec3 dark = vec3(0.10, 0.65, 0.30);
	vec3 light = vec3(0.20, 0.85, 0.45);
	return mix(dark, light, grass);
}

vec3 draw_battle_forest(vec2 world_pos) {
	float trees = noise(world_pos * 6.0);
	vec3 dark = vec3(0.06, 0.30, 0.15);
	vec3 light = vec3(0.12, 0.45, 0.25);
	return mix(dark, light, trees);
}

vec3 draw_battle_debris(vec2 world_pos) {
	float rubble = noise(world_pos * 12.0);
	vec3 dark_brown = vec3(0.30, 0.25, 0.20);
	vec3 light_brown = vec3(0.50, 0.45, 0.35);
	return mix(dark_brown, light_brown, rubble);
}

vec3 draw_stone_wall(vec2 world_pos, vec2 tile_local) {
	// Stone blocks pattern
	float blocks_x = floor(world_pos.x * 2.0);
	float blocks_y = floor(world_pos.y * 3.0);
	float block_val = hash(vec2(blocks_x, blocks_y));
	float mortar = step(0.9, fract(world_pos.x * 2.0)) + step(0.9, fract(world_pos.y * 3.0));
	vec3 stone = vec3(0.55, 0.55, 0.55) * (0.8 + block_val * 0.4);
	vec3 dark_mortar = vec3(0.35, 0.35, 0.35);
	return mix(stone, dark_mortar, mortar * 0.7);
}

vec3 draw_wood_wall(vec2 world_pos, vec2 tile_local) {
	// Wooden planks
	float planks = floor(world_pos.y * 4.0);
	float wood_grain = hash(vec2(world_pos.x, planks));
	float knots = step(0.85, noise(world_pos * 8.0));
	vec3 wood = vec3(0.45, 0.30, 0.15) * (0.7 + wood_grain * 0.5);
	vec3 dark_knot = vec3(0.25, 0.15, 0.08);
	return mix(wood, dark_knot, knots * 0.6);
}

vec3 draw_gate(vec2 world_pos, vec2 tile_local) {
	// Metal gate with rivets
	float rivets = step(0.9, noise(world_pos * 6.0));
	float metal_grain = noise(world_pos * 16.0);
	vec3 dark_metal = vec3(0.30, 0.32, 0.35);
	vec3 light_metal = vec3(0.50, 0.52, 0.55);
	vec3 rivet_color = vec3(0.20, 0.20, 0.22);
	vec3 base = mix(dark_metal, light_metal, metal_grain);
	return mix(base, rivet_color, rivets * 0.5);
}

// Get procedural battle terrain
vec3 get_battle_graphic(float terrain_id, vec2 world_pos) {
	int id = int(terrain_id * 255.0);
	vec2 tile_local = fract(world_pos);
	
	if (id == 46) return draw_battle_plains(world_pos);  // Plains '.'
	if (id == 35) return draw_battle_forest(world_pos);  // Forest '#'
	if (id == 37) return draw_battle_debris(world_pos);  // Debris '%'
	if (id == 88) return vec3(0.25, 0.25, 0.25);         // Obstacle 'X' - dark
	if (id == 71 || id == 75 || id == 77) return draw_gate(world_pos, tile_local); // Gates
	if (id == 83) return draw_stone_wall(world_pos, tile_local); // Stone wall 'S'
	if (id == 72) return draw_wood_wall(world_pos, tile_local);  // Wood wall 'H'
	
	return vec3(0.5, 0.5, 0.5);
}

// Fallback solid colors
vec3 get_terrain_color(float terrain_id) {
	int id = int(terrain_id * 255.0);
	
	// Battle terrain
	if (id == 46) return vec3(0.13, 0.77, 0.37); // Plains '.'
	if (id == 35) return vec3(0.09, 0.40, 0.20); // Forest '#'
	if (id == 37) return vec3(0.4, 0.35, 0.25);  // Debris '%'
	if (id == 88) return vec3(0.3, 0.3, 0.3);    // Obstacle 'X'
	if (id == 71 || id == 75 || id == 77) return vec3(0.42, 0.45, 0.50); // Gates 'G', 'K', 'M'
	if (id == 83) return vec3(0.6, 0.6, 0.6);    // Stone wall 'S'
	if (id == 72) return vec3(0.5, 0.3, 0.1);    // Wood wall 'H'
	
	return vec3(0.5, 0.5, 0.5); // Default gray
}

vec3 get_unit_color(float team_id) {
	int team = int(team_id * 255.0);
	if (team == 0) return vec3(0.0, 1.0, 1.0); // Cyan for player
	return vec3(1.0, 0.0, 0.0); // Red for enemy
}

void fragment() {
	// Calculate visible portion based on zoom
	vec2 visible_size = grid_size / zoom;
	
	// Transform UV to world coordinates
	vec2 world_pos = camera_pos + (UV - 0.5) * visible_size;
	
	// Convert to texture UV (0-1 range)
	vec2 tex_uv = world_pos / grid_size;
	
	// Out of bounds check
	if (tex_uv.x < 0.0 || tex_uv.x > 1.0 || tex_uv.y < 0.0 || tex_uv.y > 1.0) {
		COLOR = vec4(0.0, 0.0, 0.0, 1.0); // Black outside bounds
		return;
	}
	
	// Sample textures
	float terrain_id = texture(terrain_texture, tex_uv).r;
	vec2 unit_data = texture(unit_texture, tex_uv).rg;
	float projectile_id = texture(projectile_texture, tex_uv).r;
	
	// Get terrain color/graphic
	vec3 color;
	if (graphics_mode == 1) {
		color = get_battle_graphic(terrain_id, world_pos);
	} else {
		color = get_terrain_color(terrain_id);
	}
	
	// Render projectiles on top
	if (projectile_id > 0.01) {
		color = vec3(1.0, 1.0, 0.0); // Yellow for projectiles
	}
	// Render units on top
	else if (unit_data.r > 0.01) {
		color = get_unit_color(unit_data.g);
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
