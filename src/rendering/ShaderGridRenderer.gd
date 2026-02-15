class_name ShaderGridRenderer
extends Control

## Shader-based grid rendering for large world maps
## Uses GPU texture sampling for instant rendering of entire world

var grid_texture: ImageTexture
var grid_material: ShaderMaterial
var grid_size := Vector2i(200, 200)
var camera_pos := Vector2(100, 100)
var zoom_level := 1.0
var show_grid_lines := false
var graphics_mode := 1  # 0=solid colors, 1=procedural graphics

var panel: Panel

func _init():
	# Create Panel and shader material (but don't add to tree yet)
	panel = Panel.new()
	panel.mouse_filter = Control.MOUSE_FILTER_PASS
	
	# Create shader material
	var shader = load("res://shaders/terrain_grid.gdshader")
	if not shader:
		push_error("Failed to load terrain_grid.gdshader!")
		return
	
	grid_material = ShaderMaterial.new()
	grid_material.shader = shader
	panel.material = grid_material
	
	if not grid_material.shader:
		push_error("Shader failed to compile!")
		return

func _ready():
	# Now add to scene tree
	add_child(panel)
	
	# Make Panel fill the entire parent Control
	panel.anchor_left = 0.0
	panel.anchor_top = 0.0
	panel.anchor_right = 1.0
	panel.anchor_bottom = 1.0
	panel.offset_left = 0.0
	panel.offset_top = 0.0
	panel.offset_right = 0.0
	panel.offset_bottom = 0.0
	
	# DEBUG: Full diagnostics
	print("\n=== SHADER RENDERER DEBUG ===")
	print("ShaderGridRenderer size:", size)
	print("ShaderGridRenderer global_position:", global_position)
	print("ShaderGridRenderer anchors:", Vector4(anchor_left, anchor_top, anchor_right, anchor_bottom))
	print("ShaderGridRenderer offsets:", Vector4(offset_left, offset_top, offset_right, offset_bottom))
	if get_parent():
		print("Parent name:", get_parent().name)
		print("Parent size:", get_parent().size)
	
	print("\nPanel size:", panel.size)
	print("Panel global_position:", panel.global_position)
	print("Panel anchors:", Vector4(panel.anchor_left, panel.anchor_top, panel.anchor_right, panel.anchor_bottom))
	print("Panel material:", panel.material)
	print("Panel material shader:", panel.material.shader if panel.material else "NO MATERIAL")
	print("Panel visible:", panel.visible)
	print("Panel z_index:", panel.z_index)
	print("=== END DEBUG ===\n")
	
	# Initialize shader parameters with defaults
	if grid_material:
		grid_material.set_shader_parameter("graphics_mode", graphics_mode)
	_update_transform()

func update_grid(world_grid: Array):
	"""Convert 2D world grid to GPU texture"""
	if world_grid.is_empty():
		return
	
	var height = world_grid.size()
	var width = world_grid[0].size() if height > 0 else 0
	
	if width == 0 or height == 0:
		return
	
	grid_size = Vector2i(width, height)
	
	# Create image from grid
	var image = Image.create(width, height, false, Image.FORMAT_R8)
	
	for y in range(height):
		for x in range(width):
			var tile = world_grid[y][x]
			# Store ASCII value as pixel intensity (0-255)
			var ascii_val = tile.unicode_at(0) if tile.length() > 0 else 32
			var normalized = float(ascii_val) / 255.0
			image.set_pixel(x, y, Color(normalized, 0, 0, 1))
	
	# Create texture
	if grid_texture:
		grid_texture.update(image)
	else:
		grid_texture = ImageTexture.create_from_image(image)
		grid_material.set_shader_parameter("grid_texture", grid_texture)

func update_grid_for_mode(world_grid: Array, province_grid: Array, map_mode: String):
	"""Convert 2D world grid to GPU texture based on map mode"""
	if world_grid.is_empty():
		push_error("update_grid_for_mode: world_grid is empty!")
		return
	
	var height = world_grid.size()
	var width = world_grid[0].size() if height > 0 else 0
	
	if width == 0 or height == 0:
		push_error("update_grid_for_mode: invalid dimensions %dx%d" % [width, height])
		return
	
	print("\n=== UPDATE GRID DEBUG ===")
	print("Mode: '%s', Grid: %dx%d" % [map_mode, width, height])
	print("ShaderGridRenderer size:", size)
	print("Panel size:", panel.size if panel else "NO PANEL")
	
	grid_size = Vector2i(width, height)
	
	# Create image from grid
	var image = Image.create(width, height, false, Image.FORMAT_R8)
	print("Created image: %dx%d, format: R8" % [width, height])
	
	# Province color palette for province/political modes
	var province_colors = [126, 46, 35, 38, 34, 42, 94, 79, 111, 61, 77, 67, 84, 86, 104]  # Reuse ASCII values as color IDs
	
	for y in range(height):
		for x in range(width):
			var ascii_val = 32  # Default space
			
			if map_mode == "terrain":
				# Show terrain
				var tile = world_grid[y][x]
				ascii_val = tile.unicode_at(0) if tile.length() > 0 else 32
			
			elif map_mode == "province":
				# Show province colors
				if province_grid.size() > y and province_grid[y].size() > x:
					var p_id = province_grid[y][x]
					if p_id != -1:
						# Map province ID to a unique ASCII value for coloring
						ascii_val = 65 + (p_id % 26)  # A-Z for different provinces
			
			elif map_mode == "political":
				# Show political/faction colors (similar to province for now)
				if province_grid.size() > y and province_grid[y].size() > x:
					var p_id = province_grid[y][x]
					if p_id != -1:
						# Use different range for political
						ascii_val = 97 + (p_id % 26)  # a-z for different factions
			
			elif map_mode == "resource":
				# Show terrain as base (resources would need separate data structure)
				var tile = world_grid[y][x]
				ascii_val = tile.unicode_at(0) if tile.length() > 0 else 32
			
			var normalized = float(ascii_val) / 255.0
			image.set_pixel(x, y, Color(normalized, 0, 0, 1))
	
	# Create or update texture
	if grid_texture:
		grid_texture.update(image)
		print("Updated existing ImageTexture")
	else:
		grid_texture = ImageTexture.create_from_image(image)
		grid_material.set_shader_parameter("grid_texture", grid_texture)
		print("Created new ImageTexture")
	
	# Verify texture data
	var sample_data = image.get_pixel(width/2, height/2)
	print("Sample pixel at center (%d,%d): r=%f" % [width/2, height/2, sample_data.r])
	print("Sample ASCII value: %d" % [int(sample_data.r * 255.0)])
	print("Texture uploaded to shader")
	print("=== END UPDATE DEBUG ===\n")
	
	# Update shader parameters now that grid_size has been set
	_update_transform()

func set_camera(pos: Vector2):
	"""Set camera position (world coordinates)"""
	camera_pos = pos
	_update_transform()

func set_zoom(zoom: float):
	"""Set zoom level (1.0 = normal, 2.0 = 2x zoomed in)"""
	zoom_level = clamp(zoom, 0.5, 4.0)
	_update_transform()

func _update_transform():
	"""Update shader UV transform based on camera and zoom"""
	if not panel or not grid_material:
		print("ERROR: _update_transform: panel or grid_material not ready")
		return
	
	# Keep Panel at full panel size
	panel.size = size
	panel.position = Vector2.ZERO
	panel.scale = Vector2.ONE
	
	# Pass camera position, zoom, and grid size to shader
	print("Setting shader params: camera=(%.1f, %.1f), zoom=%.1f, grid_size=(%d, %d)" % [
		camera_pos.x, camera_pos.y, zoom_level, grid_size.x, grid_size.y
	])
	grid_material.set_shader_parameter("camera_pos", camera_pos)
	grid_material.set_shader_parameter("zoom", zoom_level)
	grid_material.set_shader_parameter("grid_size", Vector2(grid_size))
	grid_material.set_shader_parameter("show_grid_lines", show_grid_lines)
	grid_material.set_shader_parameter("graphics_mode", graphics_mode)
	
	# Verify shader parameters were set
	var cam_check = grid_material.get_shader_parameter("camera_pos")
	var zoom_check = grid_material.get_shader_parameter("zoom")
	var size_check = grid_material.get_shader_parameter("grid_size")
	var tex_check = grid_material.get_shader_parameter("grid_texture")
	print("Verified params: camera=%s, zoom=%s, grid_size=%s, texture=%s" % [
		cam_check, zoom_check, size_check, "SET" if tex_check else "NULL"
	])

func world_to_screen(world_pos: Vector2i) -> Vector2:
	"""Convert world coordinates to screen coordinates"""
	var visible_width = grid_size.x / zoom_level
	var visible_height = grid_size.y / zoom_level
	var uv_offset = Vector2(
		camera_pos.x - visible_width * 0.5,
		camera_pos.y - visible_height * 0.5
	)
	var pixel_scale = Vector2(size) / Vector2(visible_width, visible_height)
	return (Vector2(world_pos) - uv_offset) * pixel_scale

func screen_to_world(screen_pos: Vector2) -> Vector2i:
	"""Convert screen coordinates to world coordinates"""
	var visible_width = grid_size.x / zoom_level
	var visible_height = grid_size.y / zoom_level
	var uv_offset = Vector2(
		camera_pos.x - visible_width * 0.5,
		camera_pos.y - visible_height * 0.5
	)
	var pixel_scale = Vector2(size) / Vector2(visible_width, visible_height)
	var world_pos = screen_pos / pixel_scale + uv_offset
	return Vector2i(int(world_pos.x), int(world_pos.y))

func toggle_grid_lines():
	## Toggle grid line visibility
	show_grid_lines = not show_grid_lines
	if grid_material:
		grid_material.set_shader_parameter("show_grid_lines", show_grid_lines)
		print("Grid lines: ", "ON" if show_grid_lines else "OFF")

func toggle_graphics_mode():
	## Toggle between solid colors and procedural graphics
	graphics_mode = 1 - graphics_mode
	if grid_material:
		grid_material.set_shader_parameter("graphics_mode", graphics_mode)
		print("Graphics mode: ", "PROCEDURAL" if graphics_mode == 1 else "SOLID")

func set_graphics_mode(mode: int):
	## Set graphics mode: 0=solid colors, 1=procedural graphics
	graphics_mode = clamp(mode, 0, 1)
	if grid_material:
		grid_material.set_shader_parameter("graphics_mode", graphics_mode)
