class_name BattleShaderRenderer
extends Control

## GPU-accelerated battle renderer using shaders for 5-10x speedup

var terrain_texture: ImageTexture
var unit_texture: ImageTexture
var projectile_texture: ImageTexture
var battle_material: ShaderMaterial
var grid_size := Vector2i(200, 200)
var camera_pos := Vector2(100, 100)
var zoom_level := 1.0
var show_grid := false
var graphics_mode := 1  # 0=solid colors, 1=procedural graphics

var color_rect: ColorRect

func _init():
	color_rect = ColorRect.new()
	color_rect.mouse_filter = Control.MOUSE_FILTER_PASS
	
	var shader = load("res://shaders/battle_grid.gdshader")
	if not shader:
		push_error("Failed to load battle_grid.gdshader!")
		return
	
	battle_material = ShaderMaterial.new()
	battle_material.shader = shader
	color_rect.material = battle_material

func _ready():
	add_child(color_rect)
	
	# Make ColorRect fill the entire parent Control
	color_rect.anchor_left = 0.0
	color_rect.anchor_top = 0.0
	color_rect.anchor_right = 1.0
	color_rect.anchor_bottom = 1.0
	color_rect.offset_left = 0.0
	color_rect.offset_top = 0.0
	color_rect.offset_right = 0.0
	color_rect.offset_bottom = 0.0
	
	color_rect.color = Color(0, 0, 0, 0)
	if battle_material:
		battle_material.set_shader_parameter("graphics_mode", graphics_mode)
	_update_transform()

func update_battle(battle_ctrl):
	## Convert battle state to GPU textures
	if not battle_ctrl or not battle_ctrl.active:
		return
	
	var width = battle_ctrl.MAP_W
	var height = battle_ctrl.MAP_H
	grid_size = Vector2i(width, height)
	
	# Create terrain texture
	var terrain_img = Image.create(width, height, false, Image.FORMAT_R8)
	for y in range(height):
		for x in range(width):
			var tile = battle_ctrl.grid[y][x] if y < battle_ctrl.grid.size() and x < battle_ctrl.grid[y].size() else " "
			var ascii_val = tile.unicode_at(0) if tile.length() > 0 else 32
			terrain_img.set_pixel(x, y, Color(float(ascii_val) / 255.0, 0, 0, 1))
	
	# Create unit texture (R = ASCII, G = team)
	var unit_img = Image.create(width, height, false, Image.FORMAT_RG8)
	unit_img.fill(Color(0, 0, 0, 1))
	for u in battle_ctrl.units:
		if u.status.get("is_dead", false):
			continue
		var pos = Vector2i(u.pos)
		if pos.x >= 0 and pos.x < width and pos.y >= 0 and pos.y < height:
			var ascii_val = u.symbol.unicode_at(0) if u.symbol.length() > 0 else 83
			var team_val = 0.0 if (u.faction == "player" or u.team == "player") else 1.0
			unit_img.set_pixel(pos.x, pos.y, Color(float(ascii_val) / 255.0, team_val, 0, 1))
	
	# Create projectile texture
	var proj_img = Image.create(width, height, false, Image.FORMAT_R8)
	proj_img.fill(Color(0, 0, 0, 1))
	for p in battle_ctrl.projectiles:
		var pos = Vector2i(p.pos)
		if pos.x >= 0 and pos.x < width and pos.y >= 0 and pos.y < height:
			proj_img.set_pixel(pos.x, pos.y, Color(1.0, 0, 0, 1))
	
	# Update or create textures
	if terrain_texture:
		terrain_texture.update(terrain_img)
	else:
		terrain_texture = ImageTexture.create_from_image(terrain_img)
		battle_material.set_shader_parameter("terrain_texture", terrain_texture)
	
	if unit_texture:
		unit_texture.update(unit_img)
	else:
		unit_texture = ImageTexture.create_from_image(unit_img)
		battle_material.set_shader_parameter("unit_texture", unit_texture)
	
	if projectile_texture:
		projectile_texture.update(proj_img)
	else:
		projectile_texture = ImageTexture.create_from_image(proj_img)
		battle_material.set_shader_parameter("projectile_texture", projectile_texture)
	
	# Update camera to follow player unit
	if battle_ctrl.player_unit:
		set_camera(Vector2(battle_ctrl.player_unit.pos))
	
	_update_transform()

func set_camera(pos: Vector2):
	## Set camera position (world coordinates)
	camera_pos = pos
	_update_transform()

func set_zoom(zoom: float):
	## Set zoom level (1.0 = normal, 2.0 = 2x zoomed in)
	zoom_level = clamp(zoom, 0.5, 4.0)
	_update_transform()

func toggle_grid_lines():
	## Toggle grid line visibility
	show_grid = not show_grid
	battle_material.set_shader_parameter("show_grid_lines", show_grid)

func toggle_graphics_mode():
	## Toggle between solid colors and procedural graphics
	graphics_mode = 1 - graphics_mode
	battle_material.set_shader_parameter("graphics_mode", graphics_mode)
	print("Battle graphics mode: ", "PROCEDURAL" if graphics_mode == 1 else "SOLID")

func set_graphics_mode(mode: int):
	## Set graphics mode: 0=solid colors, 1=procedural graphics
	graphics_mode = clamp(mode, 0, 1)
	battle_material.set_shader_parameter("graphics_mode", graphics_mode)

func _update_transform():
	## Update shader UV transform based on camera and zoom
	if not color_rect or not battle_material:
		return
	
	color_rect.size = size
	color_rect.position = Vector2.ZERO
	
	battle_material.set_shader_parameter("camera_pos", camera_pos)
	battle_material.set_shader_parameter("zoom", zoom_level)
	battle_material.set_shader_parameter("grid_size", Vector2(grid_size))
	battle_material.set_shader_parameter("show_grid_lines", show_grid)
	battle_material.set_shader_parameter("graphics_mode", graphics_mode)

func world_to_screen(world_pos: Vector2i) -> Vector2:
	## Convert world coordinates to screen coordinates
	var visible_width = grid_size.x / zoom_level
	var visible_height = grid_size.y / zoom_level
	var uv_offset = Vector2(
		camera_pos.x - visible_width * 0.5,
		camera_pos.y - visible_height * 0.5
	)
	var pixel_scale = Vector2(size) / Vector2(visible_width, visible_height)
	return (Vector2(world_pos) - uv_offset) * pixel_scale

func screen_to_world(screen_pos: Vector2) -> Vector2i:
	## Convert screen coordinates to world coordinates
	var visible_width = grid_size.x / zoom_level
	var visible_height = grid_size.y / zoom_level
	var uv_offset = Vector2(
		camera_pos.x - visible_width * 0.5,
		camera_pos.y - visible_height * 0.5
	)
	var pixel_scale = Vector2(size) / Vector2(visible_width, visible_height)
	var world_pos = screen_pos / pixel_scale + uv_offset
	return Vector2i(int(world_pos.x), int(world_pos.y))
