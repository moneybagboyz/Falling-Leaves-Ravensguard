# Settlement Placement Refactor — Implementation Plan

## Executive Summary

**Problem:** Current Lloyd-first system causes major settlements to cluster in high-quality regions because province boundaries are geometrically determined (equal area) while settlement tier is quality-determined (unequal). With nations coming, this architectural coupling will create more problems.

**Solution:** Decoupled settlement-first placement where major cities are placed by quality + enforced spacing constraints, then provinces and nations are built around these anchors.

**Effort:** ~2-3 days implementation + 1 day testing  
**Impact:** Resolves clustering, enables clean nation layer integration, historically accurate settlement distribution

---

## Current Architecture (Problems)

```
1. Lloyd Relaxation    → Province centroids (geometric, equal-area)
2. Settlement Scoring  → Quality values per tile
3. Tier Assignment     → Province average quality → hub tier
4. Spoke Placement     → Poisson disk within provinces

Problems:
- If 3 provinces land in a fertile valley → 3 tier-3+ cities clustered
- Province capital location isn't the best site (just the centroid)
- Can't add nations without forcing them to follow province clustering
```

**Files:**
- `src/worldgen/province_generator.gd` — Lloyd creates provinces
- `src/worldgen/settlement_placer.gd` — Places hubs at centroids, assigns tier from province quality
- `src/worldgen/region_generator.gd` — Orchestrates: provinces → settlements → roads

---

## Target Architecture (Goals)

```
1. Settlement Scoring  → Quality values per tile (unchanged)
2. Major City Placement → Tier 4 (metropolis) + Tier 3 (cities) placed globally
                         → Enforced minimum spacing (35+ tiles between cities)
                         → Picks BEST quality sites within spacing constraint
3. Province Generation → Voronoi/Lloyd around tier-3 cities (not arbitrary centroids)
                        → ~8-15 provinces, each anchored by a real city
4. Town Placement      → Tier 2 towns placed within provinces (quality + spacing)
5. Minor Settlements   → Tier 0-1 hamlets/villages via adaptive Poisson disk
6. [Future] Nations    → Cluster provinces geographically (3-5 provinces/nation)
                        → National capital = best tier-4 city in nation OR promoted tier-3

Benefits:
✓ Major cities widely distributed (spacing enforced)
✓ Provinces have real administrative centers (tier-3 cities)
✓ Nation capitals are actual important cities, not geometric points
✓ Historical realism (Rome/Paris/London were strategic sites, not centroids)
```

**Files to modify:**
- `src/worldgen/settlement_placer.gd` — Total rewrite
- `src/worldgen/province_generator.gd` — Modify to seed from settlements, not random
- `src/worldgen/region_generator.gd` — Swap step order: settlements → provinces
- [New] `src/worldgen/major_city_placer.gd` — Tier 3-4 placement logic
- [New] `src/worldgen/nation_generator.gd` — Future nation clustering (not in this refactor)

---

## Implementation Phases

### Phase 1: Create Major City Placer (New File)

**File:** `src/worldgen/major_city_placer.gd`

**Purpose:** Place tier 3-4 settlements globally with quality-scoring + enforced spacing.

**Key functions:**

```gdscript
## Returns Array[Dictionary] of tier 3-4 settlements.
## Each: {tile_x, tile_y, tier, score}
static func place_major_cities(
    data: WorldGenData,
    params: WorldGenParams
) -> Array:
    var metropolises = _place_tier(data, 4, count=1, min_spacing=50)
    var cities       = _place_tier(data, 3, count=8-15, min_spacing=35)
    return metropolises + cities

## Greedy best-first placement with spacing constraint.
static func _place_tier(
    data: WorldGenData,
    tier: int,
    count: int,
    min_spacing: int
) -> Array:
    var candidates = _collect_scored_tiles(data)
    candidates.sort()  # Best quality first
    
    var placed: Array = []
    var min_sq = min_spacing * min_spacing
    
    for candidate in candidates:
        if placed.size() >= count:
            break
        var pos = Vector2i(candidate.x, candidate.y)
        
        # Check spacing constraint
        var too_close = false
        for existing in placed:
            var dist_sq = pos.distance_squared_to(existing.pos)
            if dist_sq < min_sq:
                too_close = true
                break
        
        if not too_close:
            placed.append({
                "tile_x": pos.x,
                "tile_y": pos.y,
                "tier": tier,
                "score": candidate.score,
                "pos": pos  # Cache for distance checks
            })
    
    return placed
```

**Considerations:**
- Use `settlement_score` (already computed in SettlementScorer)
- Fallback to relaxed spacing if count target not met (50→45→40→35)
- Randomize candidate order slightly (0.8-1.2× score jitter) to prevent deterministic monopoly

**Testing:**
- Place 100 cities on a map, measure minimum spacing — should be ≥35 tiles
- Visual inspection — should see cities in valley, coast, forest, mountain regions

---

### Phase 2: Modify Province Generator (Seed from Cities)

**File:** `src/worldgen/province_generator.gd`

**Change:** Instead of `target_count` random seeds, seed Lloyd from tier-3 city positions.

**Before:**
```gdscript
# Random seed positions
for i in range(target_count):
    seeds.append(Vector2(rng.randf() * W, rng.randf() * H))

# Lloyd relaxation moves them to centroids
for iter in range(15):
    seeds = _lloyd_iteration(data, seeds)
```

**After:**
```gdscript
# Seed from tier-3 cities (passed in as parameter)
static func generate(
    data: WorldGenData,
    world_seed: int,
    params: WorldGenParams,
    tier3_cities: Array  # NEW PARAMETER
) -> void:
    var seeds: Array = []
    
    # Use tier-3 cities as seeds
    for city in tier3_cities:
        seeds.append(Vector2(city.tile_x, city.tile_y))
    
    # Optional: Add a few extra seeds in sparse regions
    # to prevent provinces from being too large
    if seeds.size() < params.min_provinces:
        seeds = _add_filler_seeds(data, seeds, params.min_provinces - seeds.size())
    
    # Lloyd relaxation (now refines city-based seeds)
    for iter in range(8):  # Fewer iterations since we start from good positions
        seeds = _lloyd_iteration(data, seeds)
    
    # Snap each seed to nearest habitable tile
    # ...rest of existing logic
```

**Considerations:**
- Province count now driven by city count (8-15 cities = 8-15 provinces)
- Lloyd still runs but refines rather than creates from scratch
- If a region has no tier-3 city, add filler seeds to prevent giant provinces
- Province quality now derivative of settlements within it, not the other way around

**Testing:**
- Each province should contain at least one tier-3 city (by construction)
- Province borders should roughly bisect distances between cities

---

### Phase 3: Rewrite Settlement Placer (Towns + Villages)

**File:** `src/worldgen/settlement_placer.gd`

**Change:** No longer places hubs. Now places tier 0-2 settlements within provinces.

**New structure:**

```gdscript
## Returns Array[Dictionary] of tier 0-2 settlements.
## major_cities (tier 3-4) are passed in and used as anchors.
static func place_minor_settlements(
    data: WorldGenData,
    params: WorldGenParams,
    major_cities: Array  # Tier 3-4 cities from MajorCityPlacer
) -> Array:
    var result: Array = []
    
    # Phase 1: Place tier-2 towns (regional centers)
    # - Within each province, 1-3 towns depending on size
    # - Min 20-tile spacing from tier-3+ cities
    # - Min 15-tile spacing from each other
    for pid in range(data.province_count):
        var towns = _place_towns_in_province(data, pid, major_cities)
        result.append_array(towns)
    
    # Phase 2: Place tier-1 villages (agricultural hubs)
    # - Adaptive Poisson disk (similar to current spoke placement)
    # - Min 12-tile spacing
    for pid in range(data.province_count):
        var villages = _place_villages_in_province(data, pid, major_cities + result)
        result.append_array(villages)
    
    # Phase 3: Place tier-0 hamlets (resource extraction)
    # - Near specific resource tags (timber, iron_ore)
    # - Min 8-tile spacing
    for pid in range(data.province_count):
        var hamlets = _place_hamlets_in_province(data, pid, major_cities + result)
        result.append_array(hamlets)
    
    return result

static func _place_towns_in_province(
    data: WorldGenData,
    province_id: int,
    existing_settlements: Array
) -> Array:
    # Collect tiles in this province, sorted by quality
    var candidates = _get_province_tiles_sorted(data, province_id)
    
    var province_size = candidates.size()
    var town_count = clampi(province_size / 150, 1, 3)  # 1-3 towns per province
    
    var placed: Array = []
    for candidate in candidates:
        if placed.size() >= town_count:
            break
        
        # Check spacing constraints (20 from cities, 15 from other towns)
        if _check_spacing(candidate, existing_settlements, 20) \
           and _check_spacing(candidate, placed, 15):
            placed.append({
                "tile_x": candidate.x,
                "tile_y": candidate.y,
                "province_id": province_id,
                "tier": 2,
                "is_hub": false
            })
    
    return placed
```

**Population assignment:**
```gdscript
# Moved from old _place_settlement to new _assign_population
static func _assign_population(settlements: Array, world_seed: int) -> void:
    var rng = RandomNumberGenerator.new()
    
    const TIER_POP_MIN = [50, 150, 700, 2000, 5000]
    const TIER_POP_MAX = [120, 500, 1200, 4000, 12000]
    
    for s in settlements:
        rng.seed = world_seed ^ (s.tile_x * 7919 + s.tile_y * 6547)
        var tier: int = s.tier
        s["population"] = rng.randi_range(TIER_POP_MIN[tier], TIER_POP_MAX[tier])
```

**Naming:**
```gdscript
static func _assign_names(settlements: Array, data: WorldGenData, world_seed: int):
    var rng = RandomNumberGenerator.new()
    
    const SUFFIXES_BY_TIER = {
        4: ["", "opolis", "grad"],           # Metropolis (capital suffixes)
        3: ["bury", "port", "city"],         # City
        2: ["ham", "ton", "ford"],           # Town
        1: ["wick", "stead", "croft"],       # Village
        0: ["hollow", "cross", "end"]        # Hamlet
    }
    
    for s in settlements:
        rng.seed = world_seed ^ (s.tile_x * 12289 + s.tile_y * 10007)
        var province_name = data.province_names[s.province_id].split(" ")[0]
        var suffix = SUFFIXES_BY_TIER[s.tier][rng.randi() % SUFFIXES_BY_TIER[s.tier].size()]
        s["name"] = province_name + suffix
```

**Testing:**
- Count tier distribution — should be ~1 tier-4, 8-15 tier-3, 20-40 tier-2, 50-100 tier 0-1
- Measure spacing — no tier-3 cities within 35 tiles of each other
- Visual review — major cities spread across map, not clustered

---

### Phase 4: Update Region Generator Orchestration

**File:** `src/worldgen/region_generator.gd`

**Change:** Swap step order — settlements before provinces.

**Before:**
```gdscript
# Step 9: Province generation (Lloyd + Dijkstra)
ProvinceGenerator.generate(data, world_seed, params)

# Step 10: Settlement placement (hub+spoke)
var settlement_records = SettlementPlacer.place(data, params)
```

**After:**
```gdscript
# Step 9: Tier 3-4 city placement (quality + spacing)
var major_cities = MajorCityPlacer.place_major_cities(data, params)

# Step 10: Province generation (around cities)
ProvinceGenerator.generate(data, world_seed, params, major_cities)

# Step 11: Tier 0-2 settlement placement (within provinces)
var minor_settlements = SettlementPlacer.place_minor_settlements(data, params, major_cities)

# Step 12: Combine and finalize
var settlement_records = major_cities + minor_settlements
SettlementPlacer.assign_population(settlement_records, world_seed)
SettlementPlacer.assign_names(settlement_records, data, world_seed)
```

**Step renumbering:**
- Update all `on_step.call_deferred()` labels
- Update any hardcoded step references in UI/debug code

**Testing:**
- Full worldgen pipeline runs without errors
- Settlement count stable (~60-120 total)
- Province count matches tier-3 city count (±2)

---

### Phase 5: Backward Compatibility (Save Files)

**Problem:** Existing WorldState saves have settlements with `is_hub: bool` but new system might need `tier_source: String` or other metadata.

**Solutions:**

**Option A: Version migration**
```gdscript
# In WorldState.from_dict()
if not data.has("worldgen_version"):
    # Old save — assume Lloyd-based hubs
    data["worldgen_version"] = 1

if data["worldgen_version"] == 1:
    # Migrate old hub flag to new system
    for settlement in data.settlements:
        if settlement.get("is_hub", false):
            settlement["tier_source"] = "legacy_lloyd"
```

**Option B: Mark as incompatible**
```gdscript
# Bump WorldState serialization version
const SAVE_VERSION = 2  # Was 1

static func from_dict(data: Dictionary):
    if data.get("save_version", 1) < 2:
        push_error("Save file from old worldgen — incompatible. Please regenerate world.")
        return null
```

**Recommendation:** Option B (clean break) — this is early development, few saves exist, and the architecture change is fundamental. Mark old saves as unsupported.

---

### Phase 6: Future — Nation Layer (Not in This Refactor)

**File:** `src/worldgen/nation_generator.gd` (create later)

**Approach:**
1. Cluster provinces geographically (K-means or hierarchical clustering)
2. Target 3-5 nations, each containing 2-4 provinces
3. For each nation, find the highest-tier city within it → national capital
4. If no tier-4, promote the best tier-3 to tier-4

**Integration point:**
```gdscript
# In region_generator.gd, after province generation:
# Step 13: Nation clustering (future)
if params.enable_nations:  # Opt-in feature flag initially
    var nations = NationGenerator.cluster_provinces(data, world_seed, params)
    NationGenerator.assign_capitals(nations, settlement_records)
```

**Not implemented in this refactor** — but the architecture is now ready for it.

---

## Testing Plan

### Unit Tests

**Create:** `tests/test_major_city_placement.gd`
```gdscript
func test_spacing_enforcement():
    var data = _create_test_worldgen_data(100, 100)
    var cities = MajorCityPlacer.place_major_cities(data, params)
    
    for i in range(cities.size()):
        for j in range(i + 1, cities.size()):
            var dist = _tile_distance(cities[i], cities[j])
            assert_true(dist >= 35, "Cities too close: %d tiles" % dist)

func test_tier_distribution():
    var cities = MajorCityPlacer.place_major_cities(data, params)
    var tier4 = cities.filter(func(c): return c.tier == 4)
    var tier3 = cities.filter(func(c): return c.tier == 3)
    
    assert_eq(tier4.size(), 1, "Should have exactly 1 metropolis")
    assert_true(tier3.size() >= 6 and tier3.size() <= 15, "Should have 6-15 cities")
```

**Create:** `tests/test_province_city_alignment.gd`
```gdscript
func test_provinces_contain_cities():
    var major_cities = MajorCityPlacer.place_major_cities(data, params)
    ProvinceGenerator.generate(data, seed, params, major_cities)
    
    # Every tier-3 city should be in a different province
    var cities_by_province = {}
    for city in major_cities:
        if city.tier == 3:
            var pid = data.province_id[city.tile_y][city.tile_x]
            assert_false(cities_by_province.has(pid), "Two tier-3 cities in same province")
            cities_by_province[pid] = city
```

### Integration Tests

**Manual visual tests:**
1. Generate 5 different seeds, inspect settlement distribution
2. Check for clustering (measure distances between all tier-3+ pairs)
3. Verify biome diversity in cities (not all in same terrain type)
4. Check province boundaries make sense (bisect city distances)

**Automated snapshot test:**
```gdscript
# tests/test_worldgen_snapshot.gd
func test_settlement_distribution_stable():
    var ws = RegionGenerator.generate(FIXED_SEED, params)
    var settlement_positions = _extract_positions(ws)
    
    # Compare against known-good reference
    var expected = load("res://tests/fixtures/expected_settlements_seed12345.json")
    assert_eq(settlement_positions, expected, "Settlement distribution changed")
```

### Performance Benchmarks

**Current system (Lloyd-first):**
- Province generation: ~150ms
- Settlement placement: ~80ms
- Total worldgen: ~2.5s (300x300 map)

**Expected (Settlement-first):**
- Major city placement: ~100ms (greedy + spacing checks)
- Province generation: ~120ms (fewer Lloyd iterations)
- Minor settlement placement: ~150ms (more settlements)
- Total worldgen: ~2.8s (acceptable +12% overhead)

---

## Migration Checklist

- [ ] Create `src/worldgen/major_city_placer.gd`
- [ ] Implement `place_major_cities()` and `_place_tier()`
- [ ] Write unit tests for spacing enforcement
- [ ] Modify `province_generator.gd` to accept `tier3_cities` parameter
- [ ] Update Lloyd seeding logic to use city positions
- [ ] Test province boundaries align with cities
- [ ] Rewrite `settlement_placer.gd` — remove hub placement logic
- [ ] Implement `place_minor_settlements()` with tier 0-2 logic
- [ ] Extract population + naming to separate functions
- [ ] Update `region_generator.gd` step orchestration
- [ ] Renumber steps in progress callbacks
- [ ] Run full worldgen pipeline, verify no errors
- [ ] Visual inspection: 5 random seeds, check city distribution
- [ ] Performance benchmark: compare before/after times
- [ ] Update `WorldState.SAVE_VERSION` to 2
- [ ] Mark old saves as incompatible in `from_dict()`
- [ ] Update any UI code that references `is_hub` field
- [ ] Commit with message: "Refactor: Settlement-first placement architecture"

**Estimated timeline:**
- Day 1: Major city placer + tests (4-5 hours)
- Day 2: Province generator changes + settlement placer rewrite (6-7 hours)
- Day 3: Integration, orchestration, testing (5-6 hours)
- Day 4: Polish, edge cases, performance validation (3-4 hours)

---

## Rollback Plan

**If refactor fails or introduces blocker bugs:**

1. Revert commits:
   ```
   git revert HEAD~N  # N = number of commits in refactor
   ```

2. Tag stable version before starting:
   ```
   git tag worldgen-lloyd-stable
   ```

3. Feature flag approach (safer):
   ```gdscript
   # In WorldGenParams
   var use_legacy_lloyd_placement: bool = false
   
   # In region_generator.gd
   if params.use_legacy_lloyd_placement:
       # Old code path
   else:
       # New settlement-first code path
   ```

4. Gradual rollout:
   - Week 1: New system opt-in (default OFF)
   - Week 2: Default ON, allow opt-out
   - Week 3: Remove legacy code path

---

## Success Criteria

**Must have:**
- ✅ No tier-3+ cities within 30 tiles of each other
- ✅ At least 1 tier-3+ city in each major biome type
- ✅ Province count matches tier-3 city count (±2)
- ✅ Worldgen completes in <3 seconds (300x300 map)
- ✅ No crashes or assertion failures in 20 test generations

**Nice to have:**
- ✅ Tier-4 metropolis visually "feels" like the best city (best terrain + resources)
- ✅ Province borders intuitive (follow terrain features when possible)
- ✅ Settlement names reflect tier (metropolis gets grand suffixes)

**Done when:**
- All checklist items complete
- 10 consecutive worldgen runs succeed
- Code review approved
- Documentation updated (this file + inline comments)
