extends SubViewportContainer

const CHUNK_SIZE = 16
const TILE_SIZE = 16

enum RenderMode { OVERWORLD, BATTLE, DUNGEON, CITY, WORLD_PREVIEW }

var camera: Camera2D
var terrain_layer: TileMapLayer
var entity_layer: TileMapLayer
var minimap: TextureRect
var viewport: SubViewport

var dirty_chunks = {}  # Track which chunks need updating
var current_camera_chunk = Vector2i(-999, -999)
var last_entity_positions = {}  # Track entity positions for dirty updates
var current_mode = RenderMode.OVERWORLD

func _ready():
	viewport = $SubViewport
	camera = $SubViewport/Camera2D
	terrain_layer = $SubViewport/TerrainLayer
	entity_layer = $SubViewport/EntityLayer
	minimap = $MinimapContainer/Minimap
	
	# Set up viewport size to match container
	resized.connect(_on_resized)
	_on_resized()

func _on_resized():
	if viewport:
		viewport.size = size

func set_camera_position(pos: Vector2):
	if camera:
		camera.position = pos * TILE_SIZE

func set_render_mode(mode: RenderMode):
	current_mode = mode
	# Clear dirty chunks when switching modes
	dirty_chunks.clear()
	current_camera_chunk = Vector2i(-999, -999)

func get_camera_chunk() -> Vector2i:
	if not camera:
		return Vector2i.ZERO
	return Vector2i(int(camera.position.x / (CHUNK_SIZE * TILE_SIZE)), int(camera.position.y / (CHUNK_SIZE * TILE_SIZE)))

func mark_chunk_dirty(chunk_pos: Vector2i):
	dirty_chunks[chunk_pos] = true

func mark_tile_dirty(tile_pos: Vector2i):
	var chunk = Vector2i(tile_pos.x / CHUNK_SIZE, tile_pos.y / CHUNK_SIZE)
	mark_chunk_dirty(chunk)

func update_terrain_chunk(chunk_pos: Vector2i, grid: Array, width: int, height: int, tile_set):
	if not terrain_layer:
		return
		
	terrain_layer.tile_set = tile_set
	
	var start_x = chunk_pos.x * CHUNK_SIZE
	var start_y = chunk_pos.y * CHUNK_SIZE
	
	for y in range(CHUNK_SIZE):
		for x in range(CHUNK_SIZE):
			var world_x = start_x + x
			var world_y = start_y + y
			
			if world_x < 0 or world_x >= width or world_y < 0 or world_y >= height:
				continue
				
			var tile = grid[world_y][world_x]
			var atlas_coords = _get_tile_atlas_coords(tile)
			terrain_layer.set_cell(Vector2i(world_x, world_y), 0, atlas_coords)

func update_visible_terrain(camera_pos: Vector2i, grid: Array, width: int, height: int, tile_set):
	var camera_chunk = get_camera_chunk()
	
	# If camera moved to new chunk, mark surrounding chunks dirty
	if camera_chunk != current_camera_chunk:
		for dy in range(-2, 3):
			for dx in range(-2, 3):
				mark_chunk_dirty(camera_chunk + Vector2i(dx, dy))
		current_camera_chunk = camera_chunk
	
	# Update all dirty chunks
	for chunk in dirty_chunks.keys():
		update_terrain_chunk(chunk, grid, width, height, tile_set)
	
	dirty_chunks.clear()

func update_entities(entities: Dictionary, tile_set):
	if not entity_layer:
		return
		
	entity_layer.tile_set = tile_set
	
	# Clear positions where entities moved from
	for old_pos in last_entity_positions.keys():
		if not entities.has(old_pos):
			entity_layer.erase_cell(old_pos)
	
	# Draw entities at new positions
	for pos in entities:
		var entity_data = entities[pos]
		var atlas_coords = _get_entity_atlas_coords(entity_data.char, entity_data.col)
		entity_layer.set_cell(pos, 0, atlas_coords)
	
	last_entity_positions = entities.duplicate()

signal minimap_clicked(world_pos: Vector2i)

func update_minimap(grid: Array, width: int, height: int, camera_pos: Vector2i = Vector2i.ZERO, visible_size: Vector2i = Vector2i.ZERO):
	if not minimap:
		return
	
	# Create downsampled image (1 pixel per 4x4 tiles)
	var sample_rate = 4
	var mini_w = width / sample_rate
	var mini_h = height / sample_rate
	
	var image = Image.create(mini_w, mini_h, false, Image.FORMAT_RGB8)
	
	for y in range(mini_h):
		for x in range(mini_w):
			var wx = x * sample_rate
			var wy = y * sample_rate
			
			if wy < grid.size() and wx < grid[0].size():
				var tile = grid[wy][wx]
				var color = _get_terrain_color(tile)
				image.set_pixel(x, y, color)
	
	# Draw visible area rectangle if camera_pos provided
	if visible_size.x > 0 and visible_size.y > 0:
		var rect_x = camera_pos.x / sample_rate
		var rect_y = camera_pos.y / sample_rate
		var rect_w = visible_size.x / sample_rate
		var rect_h = visible_size.y / sample_rate
		
		# Draw white border for visible area
		for x in range(max(0, rect_x), min(mini_w, rect_x + rect_w)):
			if rect_y >= 0 and rect_y < mini_h:
				image.set_pixel(x, rect_y, Color.WHITE)
			if rect_y + rect_h >= 0 and rect_y + rect_h < mini_h:
				image.set_pixel(x, rect_y + rect_h, Color.WHITE)
		
		for y in range(max(0, rect_y), min(mini_h, rect_y + rect_h)):
			if rect_x >= 0 and rect_x < mini_w:
				image.set_pixel(rect_x, y, Color.WHITE)
			if rect_x + rect_w >= 0 and rect_x + rect_w < mini_w:
				image.set_pixel(rect_x + rect_w, y, Color.WHITE)
	
	var texture = ImageTexture.create_from_image(image)
	minimap.texture = texture

func _gui_input(event):
	if not minimap or not minimap.visible:
		return
	
	if event is InputEventMouseButton and event.pressed and event.button_index == MOUSE_BUTTON_LEFT:
		var minimap_rect = minimap.get_global_rect()
		if minimap_rect.has_point(event.global_position):
			# Click on minimap - calculate world position
			var local_pos = event.global_position - minimap_rect.position
			var minimap_size = minimap_rect.size
			
			if minimap_size.x > 0 and minimap_size.y > 0:
				var world_x = int((local_pos.x / minimap_size.x) * 200)  # Assuming 200x200 world
				var world_y = int((local_pos.y / minimap_size.y) * 200)
				
				# Jump camera to clicked position
				print("Minimap clicked: ", Vector2i(world_x, world_y))
				minimap_clicked.emit(Vector2i(world_x, world_y))
				get_viewport().set_input_as_handled()

func _get_tile_atlas_coords(tile: String) -> Vector2i:
	# Map terrain characters to atlas positions
	match tile:
		'.': return Vector2i(0, 0)  # Plains/Floor
		'#': return Vector2i(2, 0)  # Forest/Wall
		'&': return Vector2i(3, 0)  # Jungle
		'"': return Vector2i(4, 0)  # Desert
		'*': return Vector2i(5, 0)  # Tundra
		'o': return Vector2i(6, 0)  # Hills
		'^': return Vector2i(7, 0)  # Mountains
		'≈': return Vector2i(8, 0)  # Lake
		'/': return Vector2i(9, 0)  # River
		'\\': return Vector2i(10, 0)  # River
		'=': return Vector2i(11, 0)  # Road
		'+': return Vector2i(12, 0)  # Door (dungeon)
		'%': return Vector2i(13, 0)  # Debris (battle)
		'X': return Vector2i(14, 0)  # Obstacle (battle)
		_: return Vector2i(1, 0)  # Default to plains/floor

func _get_entity_atlas_coords(char: String, color: Color) -> Vector2i:
	# Map entity characters to atlas positions
	match char:
		'@': return Vector2i(0, 1)  # Player
		'A': return Vector2i(1, 1)  # Army
		'M': return Vector2i(2, 1)  # Metropolis
		'C': return Vector2i(3, 1)  # City
		'T': return Vector2i(4, 1)  # Town
		'v', 'V': return Vector2i(5, 1)  # Village
		'h': return Vector2i(6, 1)  # Hamlet
		'S': return Vector2i(7, 1)  # Spearman/Soldier
		'B': return Vector2i(8, 1)  # Bowman
		'K': return Vector2i(9, 1)  # Knight
		'G': return Vector2i(10, 1)  # Goblin
		'O': return Vector2i(11, 1)  # Orc
		'D': return Vector2i(12, 1)  # Dragon/Enemy
		'$': return Vector2i(13, 1)  # Item/Loot
		'*': return Vector2i(14, 1)  # Projectile
		_: return Vector2i(0, 0)  # Default

func _get_terrain_color(tile: String) -> Color:
	# Map terrain characters to colors for minimap
	match tile:
		'.': return Color(0.6, 0.8, 0.4)  # Green plains
		'#': return Color(0.2, 0.5, 0.2)  # Dark green forest
		'&': return Color(0.3, 0.6, 0.3)  # Jungle
		'"': return Color(0.9, 0.8, 0.5)  # Tan desert
		'*': return Color(0.8, 0.9, 0.9)  # White tundra
		'o': return Color(0.6, 0.6, 0.4)  # Brown hills
		'^': return Color(0.6, 0.6, 0.6)  # Gray mountains
		'≈': return Color(0.4, 0.6, 0.9)  # Light blue lake
		'/', '\\': return Color(0.3, 0.5, 0.9)  # River
		'=': return Color(0.7, 0.6, 0.4)  # Road
		'+': return Color(0.5, 0.3, 0.1)  # Brown door
		'%': return Color(0.4, 0.4, 0.4)  # Gray debris
		'X': return Color(0.3, 0.3, 0.3)  # Dark obstacle
		_: return Color(0.5, 0.5, 0.5)  # Gray default
