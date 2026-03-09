# Economy Diagnostics: Trade & Resource Depletion

## Problem 2: Trade System Severely Underpowered

### Current State
- Only **3-4 active trade parties** for **160+ settlements**
- Results: Famine despite global wheat surplus
  - Week 8: Some settlements have 11,000+ wheat
  - Week 8: Others have 0 wheat, 89% unrest

### Root Causes

#### 2.1 Trade Party Spawn Constraints
**Config** (`data/config/economy_config.json`):
```json
{
  "surplus_threshold": 20.0,     // Only spawn if surplus > 20
  "shortage_threshold": 5.0,     // Only target shortages > 5
  "cargo_fraction": 0.5,         // Carry just 50% of surplus
  "days_buffer": 14.0            // Or < 14 days supply
}
```

**Spawn Logic** (`src/simulation/economy/trade_party_spawner.gd`):
- Only **one party per (origin, dest, good)** tuple at a time
- Blocks new spawns until previous party completes delivery
- For a 3-tile path, party takes ~3-5 ticks to deliver
- With 160 settlements and dozens of goods, this creates a massive queue

**Calculation**:
- 160 settlements × (wheat + timber + iron) = ~480 potential routes
- 4 active parties = **0.8% coverage**
- Even if routes doubled every tick, it would take 6 ticks to cover 50% of routes

#### 2.2 Cargo Size Too Conservative
- `cargo_fraction = 0.5` means half the surplus stays behind
- Settlement with 100 wheat surplus sends only 50
- Destination needing 200 gets 50, still starves for 5+ days
- Meanwhile, origin still has 50+ surplus that could have been sent

#### 2.3 No Emergency Triage
- All goods treated equally
- Wheat shortage (causes death) gets same priority as timber shortage (slows construction)
- No mechanism to spawn multiple parties to the same destination for critical shortages

### Recommended Fixes

#### Quick Wins
1. **Increase trade party capacity**
   - Change `cargo_fraction` from 0.5 → **0.8**
   - Change `surplus_threshold` from 20.0 → **30.0** (to avoid depleting origin)

2. **Allow multiple parties per route**
   - Remove the `_party_already_exists()` check for **critical goods** (wheat_bushel)
   - Allow up to 3 simultaneous parties for emergency shortages

3. **Emergency spawn multiplier**
   ```gdscript
   # In _try_spawn_from(), add:
   if good_id == "wheat_bushel" and shortage > 20.0:
       # Spawn 2-3 parties instead of 1
       for i in range(min(3, int(shortage / 20.0))):
           _spawn(ws, ss, sid, best_dest, good_id, cargo_amt, best_path)
   ```

#### Medium-term Solutions
1. **Trade party pool scaling**
   - Base parties: `max(4, settlement_count / 20)` (8 parties for 160 settlements)
   - Emergency parties: +4 when any settlement has >50% unrest

2. **Weighted good priority**
   ```gdscript
   var urgency_weights = {
       "wheat_bushel": 10.0,  # Food is life
       "iron_ore": 3.0,
       "timber_log": 2.0,
       "coin": 1.0
   }
   need_score *= urgency_weights.get(good_id, 1.0)
   ```

3. **Hub-and-spoke logistics**
   - Tier 4 cities spawn dedicated trade parties to tier 2-3 cities
   - Tier 2-3 cities redistribute to tier 0-1 hamlets
   - Mimics historical hub-city trade networks

---

## Problem 3: Resource Depletion Cascade

### 3.1 Timber Depletion

#### Production vs Consumption

**Production** (`log_timber` recipe):
- Workers: ~10-30% of population (labour split)
- Rate: `extraction_workers × 0.5 timber/worker/day`
- Lumber Camp level 1: max 8 workers → **4 timber/day**
- Lumber Camp level 5: max 60 workers → **30 timber/day**

**Consumption** (relentless daily burn):
1. **Iron Smelting** (`smelt_iron` recipe):
   - 2 ore + **1 timber** → 1 ingot
   - Active smelters consume 5-20 timber/day

2. **Tool Forging** (`forge_tools` recipe):
   - 3 ingots + **1 timber** → 1 tool
   - Tool degradation: `0.002 × population` tools/day
   - Pop 1000 → 2 tools/day → **2 timber/day**

3. **Construction**:
   - Buildings cost 3-20 timber each
   - Active construction: 1-3 buildings/week → **10-30 timber/week**

4. **Building Upkeep**:
   - Iron Mine: 2 timber/season
   - Iron Smelter: 5 timber/season

**Net Result**:
- Week 0: ~800 timber (starter stock)
- Week 1: ~170 timber (production started)
- Week 2: ~5 timber (smelting/tools drain)
- Week 8: **5 timber** (chronic shortage)

#### Root Causes
1. **Starter stock misleading**: 800 timber looks healthy but lasts only 5-7 days
2. **Extraction labour allocation too low**: Only 10-30% of workers assigned to extraction
3. **Lumber Camp upgrades don't happen fast enough**: Most settlements stuck at level 1 (max 8 workers)
4. **Smelting ignores timber cost**: Iron cities burn timber to make ingots without tracking timber balance

### 3.2 Iron Shortage

#### The Iron Chain
```
[Mine] iron_ore → [Smelt] iron_ingot → [Forge] iron_tools
 (↑ 0.8/wd)       (↑ 2 ore + 1 timber)  (↑ 3 ingots + 1 timber)
```

**Problem**: Each step requires **previous step's output + timber**

To produce 1 tool:
- 1 tool = 3 ingots
- 3 ingots = 6 ore + **3 timber**
- Plus 1 timber for forging = **4 timber total**

**Tool Degradation**: 0.002 × population daily
- Pop 1000 → 2 tools/day
- 2 tools/day = **8 timber/day** (just for tool replacement)
- Pop 5000 → 10 tools/day = **40 timber/day**

#### Why Iron Shortages Persist
1. **Timber bottleneck**: Can't smelt ore without timber
2. **Mine placement**: Only settlements with `iron_ore` terrain tag can build mines
   - Many settlements have no local ore → rely on trade
   - Trade parties can't keep up (see Problem 2)

3. **Compounding effect**:
   - No tools → lower extraction efficiency
   - Lower extraction → less timber
   - Less timber → can't smelt iron
   - Can't smelt iron → no tools
   - **Death spiral**

### Recommended Fixes

#### Immediate (Tuning)
1. **Boost timber production**
   ```json
   // In log_timber.json
   "yield_per_worker_day": 0.5  →  1.0  (double output)
   ```

2. **Reduce timber consumption in smelting**
   ```json
   // In smelt_iron.json
   "inputs": { "iron_ore": 2, "timber_log": 1 }
   →
   "inputs": { "iron_ore": 3, "timber_log": 1 }  // 3:1 ore:timber ratio
   ```

3. **Starter stock rebalancing**
   ```gdscript
   // In settlement_pulse.gd _seed_starter_stock()
   ss.inventory["timber_log"] = float(ss.acreage.get("woodlot_acres", 10)) * 0.3  // was 0.1
   ```

#### Medium-term (Systemic)
1. **Labour reallocation intelligence**
   - Governor AI should detect timber stockpile < 50 and reallocate 50% of workers to extraction
   - Current system doesn't react to shortages fast enough

2. **Building upgrade priority**
   - Lumber Camps should auto-upgrade when `timber_log < 20` and prosperity > 0.5
   - Currently upgrades are random/slow

3. **Alternative fuel sources**
   - Charcoal recipe: 3 timber → 5 charcoal (already exists?)
   - Smelting accepts charcoal: 2 ore + 1 charcoal → 1 ingot
   - Reduces timber dependency by 40-60%

4. **Trade party rebalancing** (see Problem 2 fixes above)
   - More aggressive timber redistribution from forest-rich settlements

---

## Data Supporting Conclusions

### From Week 8 Audit:
```
StormFordmark  (capital):  2974 wheat, 629 timber, 9 iron
Old Cbury:                11242 wheat, 2249 timber, 1536 iron
Westton:                      0 wheat,  468 timber, 0 iron, 67% unrest
Westham:                      0 wheat,  551 timber, 0 iron, 89% unrest
```

**Observations**:
- Wheat distribution: 0 to 11,242 (infinite variance)
- Timber mostly collapsed: 90% of settlements at 5-20 timber
- Iron: 80% of settlements marked `!iro` (shortage)
- Trade parties: 3 active (0.5% of potential routes)

### Config Values:
```json
"surplus_threshold": 20.0        // Too high (blocks small settlements from trading)
"cargo_fraction": 0.5            // Too conservative (leaves surplus behind)
"shortage_threshold": 5.0        // Reasonable
"days_buffer": 14.0              // Reasonable (2 weeks)
"tool_wear_per_worker_per_tick": 0.002  // Sustainable IF iron chain works
```

### Recipe Analysis:
```
Timber production: 0.5 logs/worker/day  (LOW)
Timber consumption: 1 log per 2 ore smelted + 1 log per tool + construction
Iron production: 0.8 ore/worker/day
Iron chain: 6 ore + 4 timber → 1 tool (assuming 3:1 smelt ratio)
```

**Conclusion**: Timber is the bottleneck for the entire industrial economy.

---

## Prioritized Action Plan

### Phase 1: Emergency Triage (1-2 hours)
1. ✅ Increase `cargo_fraction` to 0.8
2. ✅ Double timber yield (`yield_per_worker_day` 0.5 → 1.0)
3. ✅ Reduce iron smelting timber cost (2 ore:1 timber → 3 ore:1 timber)
4. ✅ Allow multiple wheat trade parties per route

### Phase 2: Trade System Overhaul (4-6 hours)
1. ✅ Scale trade party pool with settlement count
2. ✅ Urgency-weighted good priority (wheat > iron > timber > coin)
3. ✅ Hub-and-spoke logistics model
4. ✅ Emergency party spawn for >50% unrest settlements

### Phase 3: Production Intelligence (8-12 hours)
1. ✅ Governor AI timber shortage response
2. ✅ Auto-upgrade Lumber Camps when timber < 20
3. ✅ Charcoal alternative fuel system
4. ✅ Labour reallocation based on shortage tracking

### Phase 4: Long-term Balance (future)
1. Extraction labour model overhaul (replace WOODLOT_LABOUR_FACTOR)
2. Building milestone progression tuning
3. Population consumption curves by era/climate
4. Regional specialization mechanics (timber regions, iron regions, grain regions)
