# UIPanels Refactoring Plan

## Current Problems

### Architecture Issues
1. **Monolithic 2022-line file** - Single file with 33 static functions
2. **Poor separation of concerns** - Color calculations mixed with UI rendering
3. **No encapsulation** - Everything is static, no state management
4. **String concatenation overhead** - Lots of manual BBCode building
5. **Duplicated rendering patterns** - Similar loops in render_world_map, render_battle, render_dungeon, render_city
6. **Hash collision risk** - Using hash() for terrain color cache keys
7. **Dead code** - graphical_mode_active checks no longer needed with viewport
8. **No modularity** - Can't test or reuse components independently

### Performance Issues
1. **Inefficient caching** - Hash-based cache can have collisions
2. **Repeated string allocations** - Heavy use of PackedStringArray.append()
3. **Redundant color conversions** - Converting colors to BBCode repeatedly
4. **No viewport awareness** - Still rendering ASCII/BBCode for graphical modes

## Proposed Architecture

### 1. Split into Specialized Modules

```
src/ui/
├── core/
│   ├── TerrainColors.gd      # Terrain color calculations (static or singleton)
│   ├── UIFormatting.gd       # BBCode helpers, string utilities
│   └── EntityRenderer.gd     # Entity symbol/color mapping
│
├── renderers/
│   ├── BaseRenderer.gd       # Abstract base for all renderers
│   ├── MenuRenderer.gd       # Menu screens (main menu, configs)
│   ├── WorldRenderer.gd      # Overworld map (now mostly for text fallback)
│   ├── BattleRenderer.gd     # Battle screen
│   ├── DungeonRenderer.gd    # Dungeon screen
│   ├── CityRenderer.gd       # City screen
│   └── UIRenderer.gd         # Side panels, info screens, dialogs
│
└── UIPanels.gd               # Facade/compatibility layer (delegates to modules)
```

### 2. TerrainColors.gd - Specialized Color Management

```gdscript
class_name TerrainColors
extends RefCounted

# Better caching with struct keys instead of hash
static var _cache := {}

static func get_color(pos: Vector2i, terrain_char: String, geology: Dictionary, scope: String = "global") -> Color:
    # Use structured key (no hash collisions)
    var key := "%d_%d_%s_%s" % [pos.x, pos.y, terrain_char, scope]
    if _cache.has(key):
        return _cache[key]
    
    var color := _calculate_color(terrain_char, geology, scope)
    _cache[key] = color
    return color

static func _calculate_color(terrain: String, geo: Dictionary, scope: String) -> Color:
    # Quantize values to reduce cache bloat
    var temp := snappedf(geo.get("temp", 0.5), 0.1)
    var rain := snappedf(geo.get("rain", 0.5), 0.1)
    var elev := snappedf(geo.get("elevation", 0.3), 0.1)
    
    # Pure calculation logic (no caching here)
    match terrain:
        "~", "≈": return Color.from_hsv(0.6, 0.7, clamp(0.3 + elev * 0.4, 0.2, 0.8))
        ".": return Color.from_hsv(clamp(0.25 + temp * 0.08, 0.2, 0.35), clamp(0.3 + rain * 0.5, 0.2, 0.9), clamp(0.6 + rain * 0.2, 0.4, 0.9))
        # ... etc
    return Color.WHITE

static func clear_cache() -> void:
    _cache.clear()
```

### 3. UIFormatting.gd - BBCode Utilities

```gdscript
class_name UIFormatting
extends RefCounted

static var _color_hex_cache := {}

static func color_tag(text: String, color: Variant) -> String:
    var hex := _to_hex(color)
    return "[color=%s]%s[/color]" % [hex, text]

static func bold(text: String) -> String:
    return "[b]%s[/b]" % text

static func center(text: String) -> String:
    return "[center]%s[/center]" % text

static func bgcolor(text: String, color: Variant) -> String:
    var hex := _to_hex(color)
    return "[bgcolor=%s]%s[/bgcolor]" % [hex, text]

static func _to_hex(color: Variant) -> String:
    if color is String:
        return color
    if color is Color:
        if _color_hex_cache.has(color):
            return _color_hex_cache[color]
        var hex := "#" + color.to_html(false)
        _color_hex_cache[color] = hex
        return hex
    return "white"

static func build_table(columns: int, data: Array) -> String:
    var parts := PackedStringArray()
    parts.append("[table=%d]" % columns)
    for row in data:
        for cell in row:
            parts.append("[cell]%s[/cell]" % str(cell))
    parts.append("[/table]")
    return "".join(parts)
```

### 4. BaseRenderer.gd - Template Pattern

```gdscript
class_name BaseRenderer
extends RefCounted

# Template method pattern
func render(gs, config: Dictionary) -> Dictionary:
    var result := {
        "map": "",
        "side": "",
        "header": "",
        "log": ""
    }
    
    result.header = get_header(gs, config)
    result.map = render_map(gs, config)
    result.side = render_sidebar(gs, config)
    result.log = render_log(gs, config)
    
    return result

# Override in subclasses
func get_header(gs, config: Dictionary) -> String:
    return ""

func render_map(gs, config: Dictionary) -> String:
    return ""

func render_sidebar(gs, config: Dictionary) -> String:
    return ""

func render_log(gs, config: Dictionary) -> String:
    return ""
```

### 5. WorldRenderer.gd - Specialized for Overworld (Text Fallback Only)

```gdscript
class_name WorldRenderer
extends BaseRenderer

# NOTE: With viewport system, this is mainly for text-mode fallback
# Most rendering now handled by WorldViewport.gd

func render_map(gs, config: Dictionary) -> String:
    var vw := config.get("view_width", 80)
    var vh := config.get("view_height", 40)
    var center := config.get("center", gs.player.pos if gs.player else Vector2i(gs.width/2, gs.height/2))
    
    var lines := _render_terrain_layer(gs, center, vw, vh)
    _overlay_entities(gs, lines, center, vw, vh)
    
    return "\n".join(lines)

func _render_terrain_layer(gs, center: Vector2i, vw: int, vh: int) -> PackedStringArray:
    var lines := PackedStringArray()
    var start := center - Vector2i(vw/2, vh/2)
    
    for y in vh:
        var line_parts := PackedStringArray()
        for x in vw:
            var pos := start + Vector2i(x, y)
            var tile := _get_tile_display(gs, pos)
            line_parts.append(tile)
        lines.append("".join(line_parts))
    
    return lines

func _get_tile_display(gs, pos: Vector2i) -> String:
    if pos.x < 0 or pos.y < 0 or pos.x >= gs.width or pos.y >= gs.height:
        return " "
    
    var char := gs.grid[pos.y][pos.x]
    var color := TerrainColors.get_color(pos, char, gs.geology.get(pos, {}), "world")
    return UIFormatting.color_tag(char, color)

func _overlay_entities(gs, lines: PackedStringArray, center: Vector2i, vw: int, vh: int) -> void:
    # Use spatial hash for efficient entity lookup
    var start := center - Vector2i(vw/2, vh/2)
    var end := start + Vector2i(vw, vh)
    
    var entities := _get_visible_entities(gs, start, end)
    # Modify lines in-place to overlay entities
    # ...
```

### 6. Migration Strategy

#### Phase 1: Extract TerrainColors (Low Risk)
- Create TerrainColors.gd
- Move _get_terrain_color logic
- Update UIPanels to delegate
- Test thoroughly

#### Phase 2: Extract UIFormatting (Low Risk)
- Create UIFormatting.gd
- Move BBCode helpers
- Update UIPanels to use utilities
- Test

#### Phase 3: Create Renderers (Medium Risk)
- Create BaseRenderer.gd
- Extract MenuRenderer first (simple, isolated)
- Test menu screens
- Extract WorldRenderer (complex)
- Test overworld rendering
- Extract Battle/Dungeon/City renderers
- Test each mode

#### Phase 4: Cleanup (Low Risk)
- Remove duplicated code from UIPanels
- Keep UIPanels as facade for backward compatibility
- Update calling code gradually

### 7. Benefits

#### Maintainability
- **Smaller files**: 200-300 lines each vs 2022 lines
- **Clear responsibilities**: Each class has one job
- **Easier testing**: Can unit test TerrainColors without full game state
- **Better IDE support**: Faster autocomplete, easier navigation

#### Performance
- **Better caching**: String keys prevent hash collisions
- **Reduced allocations**: Fewer temporary string objects
- **Viewport integration**: Remove ASCII rendering for graphical modes
- **Lazy loading**: Only load renderers when needed

#### Extensibility
- **New render modes**: Just create new renderer class
- **Custom themes**: Override color calculations easily
- **Modding support**: Easy to replace renderers
- **A/B testing**: Swap implementations without touching core

### 8. Compatibility Layer

Keep UIPanels.gd as facade:

```gdscript
class_name UIPanels
extends RefCounted

static var _menu_renderer: MenuRenderer
static var _world_renderer: WorldRenderer
# ... etc

static func render_menu(options, idx, has_world, has_char) -> String:
    if not _menu_renderer:
        _menu_renderer = MenuRenderer.new()
    return _menu_renderer.render_menu(options, idx, has_world, has_char)

static func render_world_map(gs, vw=100, vh=50, center_override=null) -> Array:
    if not _world_renderer:
        _world_renderer = WorldRenderer.new()
    return _world_renderer.render(gs, {
        "view_width": vw,
        "view_height": vh,
        "center": center_override
    }).map.split("\n")

# Gradually migrate callers to use renderers directly
```

## Immediate Quick Wins (No Refactor)

### 1. Remove Dead Code
- Remove `graphical_mode_active` checks
- Remove `_wrap_grid` (only used for grid mode)
- Simplify render functions for viewport-only architecture

### 2. Fix Hash Collision Bug
```gdscript
# OLD (collision risk):
var cache_key = hash(Vector3i(pos.x, pos.y, (scope_idx << 8) | t.unicode_at(0)))

# NEW (safe):
var cache_key = "%d_%d_%d_%d" % [pos.x, pos.y, scope_idx, t.unicode_at(0)]
```

### 3. Add Viewport Awareness
```gdscript
static func should_render_text(gs) -> bool:
    # Only render ASCII/BBCode for menu/loading screens
    return gs.current_mode in ["menu", "loading", "character_creation"]
```

## Recommendation

**Approach: Gradual Refactoring**

1. **Immediate** (1-2 hours):
   - Remove dead graphical_mode code
   - Fix hash collision bug
   - Add viewport awareness checks

2. **Short-term** (1 day):
   - Extract TerrainColors.gd
   - Extract UIFormatting.gd
   - Keep UIPanels as facade

3. **Medium-term** (2-3 days):
   - Create BaseRenderer + MenuRenderer
   - Migrate menu/config screens
   - Test thoroughly

4. **Long-term** (1 week):
   - Extract all renderers
   - Update callers to use renderers directly
   - Remove UIPanels facade
   - Full test suite

## Files to Create
- src/ui/core/TerrainColors.gd
- src/ui/core/UIFormatting.gd
- src/ui/core/EntityRenderer.gd
- src/ui/renderers/BaseRenderer.gd
- src/ui/renderers/MenuRenderer.gd
- src/ui/renderers/WorldRenderer.gd (text fallback)
- src/ui/renderers/BattleRenderer.gd (text fallback)
- src/ui/renderers/DungeonRenderer.gd (text fallback)
- src/ui/renderers/CityRenderer.gd (text fallback)
- src/ui/renderers/UIRenderer.gd (panels/dialogs)

## Testing Strategy
1. Unit tests for TerrainColors (color calculations)
2. Unit tests for UIFormatting (BBCode generation)
3. Integration tests for each renderer
4. Visual regression tests (compare old vs new output)
5. Performance benchmarks (cache hit rates, allocation counts)
