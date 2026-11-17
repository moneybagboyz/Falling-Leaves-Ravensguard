# Watershed-Based Province System

## Overview
Provinces are now generated from **natural geographic features** (river watersheds and highland regions) instead of economic settlement clustering. This creates more realistic political boundaries that follow terrain.

## Key Improvements

### 1. **Natural Borders**
- Provinces follow drainage basins (river valleys)
- Mountain ranges and ridgelines create natural boundaries
- Highlands form distinct provinces (mountain kingdoms)
- Major rivers act as border zones

### 2. **Efficient Storage (Sector Grid System)**
- Provinces stored in **10x10 tile sectors** instead of per-tile arrays
- **100x reduction in memory** compared to full grid
- O(1) lookup performance via sector hashing
- Backward compatible with legacy `province_grid[y][x]` format

### 3. **Geographic Logic**
- **Watershed provinces**: Generated from high-flow river endpoints
- **Highland provinces**: Mountain ranges above elevation 0.58
- **Terrain-weighted expansion**: 
  - Mountains = +8 cost (strong borders)
  - Elevation differences = +10x cost (steep slopes resist)
  - Major rivers = +2 cost (natural boundaries)

## Technical Details

### Province Generation Algorithm
```gdscript
1. Detect major watersheds from flow_map (flow > 15, elevation < 0.45)
2. Add highland seeds from mountain ranges (elevation > 0.58)
3. Ensure minimum spacing between province centers
4. Expand using geographic-cost Dijkstra:
   - Mountains heavily penalize expansion
   - Rivers create moderate barriers
   - Elevation changes resist crossing
5. Convert sector grid → legacy array for compatibility
```

### Data Structures

**ProvinceSectorGrid** (new):
```gdscript
class ProvinceSectorGrid:
    var sectors = {}  # Vector2i(sector_x, sector_y) -> province_id
    func get_province(tile_pos: Vector2i) -> int
    func to_legacy_grid() -> Array  # For compatibility
```

**Province Data** (enhanced):
```gdscript
provinces[id] = {
    "id": int,
    "name": String,
    "center": Vector2i,
    "tiles": Array[Vector2i],
    "resources": Array[Vector2i],
    "type": "watershed" | "highland",  # NEW
    "capital": Vector2i | null,
    # ... existing fields
}
```

### World Generation Flow
```
1. Tectonic plates → elevation_map
2. Climate simulation → moisture, temperature
3. Flow accumulation → flow_map (hydrology)
4. Settlement site scoring → all_settlement_sites
5. ⭐ NEW: Watershed detection → provinces (8-25 provinces)
6. Settlement finalization → assign to provinces
7. Faction assignment → control provinces
```

## Performance Characteristics

### Old System (Economic Voronoi)
- Memory: `width × height × 4 bytes` (~400KB for 200×200)
- Generation: O(n×m) Dijkstra from n settlements
- Border quality: Jagged, requires post-processing
- Political logic: Economically deterministic

### New System (Watershed + Sectors)
- Memory: `~(width/10 × height/10) × 12 bytes` (~5KB for 200×200)
- Generation: O(n×m) Dijkstra from 8-25 watersheds
- Border quality: Natural ridgelines/rivers
- Political logic: Geographic realism

**Result**: 98% memory reduction, 3-5x fewer province seeds, natural borders

## Gameplay Impact

### Before (Economic Clustering)
- Rich deltas had 5+ small competing provinces
- Desert/mountains = ungoverned wilderness
- Borders ignored geography
- Provinces felt artificial

### After (Watershed-Based)
- River kingdoms control entire valleys
- Mountain realms are distinct entities
- Borders follow natural barriers
- Provinces have geographic identity

## Future Enhancements

### Hierarchical Feudalism (Phase 2)
```
Kingdoms (2-4) — Large cultural/geographic regions
└─ Provinces (8-25) — Current watershed system
   └─ Counties (settlements) — Local governance
      └─ Baronies (resource sites) — Fiefdoms
```

### Cultural Cohesion
- Watershed provinces could develop distinct cultures
- Highland vs. lowland cultural splits
- River-based trade identities

### Dynamic Borders
- Provinces could split/merge based on:
  - Civil wars (following natural divisions)
  - Conquest (absorbing neighboring watersheds)
  - Economic collapse (reverting to wilderness)

## Code References

**Generation**: [WorldGen.gd](src/utils/WorldGen.gd#L1268) → `detect_watersheds()`
**Storage**: [WorldGen.gd](src/utils/WorldGen.gd#L11) → `ProvinceSectorGrid` class
**Usage**: [WorldState.gd](src/state/WorldState.gd#L15) → `province_grid` (legacy array)
**Rendering**: [UIPanels.gd](src/utils/UIPanels.gd#L314) → province map mode

## Testing

Generate a new world and observe:
1. **Political map mode**: Provinces should follow river valleys
2. **Mountain borders**: Highland provinces distinct from lowlands
3. **Settlement distribution**: Cities naturally cluster in watershed capitals
4. **Province count**: 8-25 provinces (target ~40% of settlement count)

Check console for new generation steps:
```
DETECTING WATERSHEDS...
MAPPING RESOURCES TO PROVINCES...
```
