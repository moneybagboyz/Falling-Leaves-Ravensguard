# Ravensguard — Complete Economy Simulation Design
**Version:** 1.0  
**Date:** 2026-03-02  
**Audience:** Programmers implementing the simulation overhaul  
**Basis:** Existing `economy-design.md`, current codebase architecture  

---

## Core Design Principles

1. **Closed-loop mass balance.** Every unit of a good produced must be explicitly consumed, transported, decayed, or stockpiled. Nothing appears or disappears without a logged cause.
2. **No global market.** Prices are local. Distance, route safety, and season attenuate trade signals. A wheat surplus in the south has zero direct effect on a famine in the north unless a trade party physically bridges the gap.
3. **Settlement agency is economic, not scripted.** Settlements do not run preset behavior trees. They allocate labor and blocks by scoring available actions against current need pressure. Emergent specialization falls out of this naturally.
4. **Simulation granularity scales with tier.** Hamlets and Villages run aggregate formulas. Towns, Cities, and Metropolises run per-district allocation and can support multi-step supply chains. Do not waste CPU ticking Hamlet-level grain mills.
5. **Stability beats realism.** Every formula that can oscillate must have a smoothing factor or cooldown. The goal is believable emergent behavior, not a perfect historical model.
6. **Abstract resources are first-class.** Labor, storage capacity, transport throughput, and security are not invisible assumptions — they are tracked quantities that create real bottlenecks.

---

## 1. Resource Categories

### 1.1 Survival Goods

These are consumed every tick by population. Shortfall triggers unrest and starvation. Every settlement must produce or import these.

| ID | Name | Base Value | Weight (kg) | Spoilage (days) | Notes |
|----|------|-----------|-------------|-----------------|-------|
| `wheat_bushel` | Wheat | 4 | 27 | 180 | Primary caloric input; drives most demand |
| `rye_bushel` | Rye | 3 | 27 | 180 | Cold-climate wheat substitute; lower yield |
| `flour_sack` | Flour | 7 | 25 | 90 | Milled wheat; higher value, faster spoilage |
| `bread_loaf` | Bread | 10 | 1 | 4 | Most processed; town+ staple |
| `salt_unit` | Salt | 6 | 2 | — | Food preservation; unlocks preserved_meat |
| `preserved_meat` | Preserved Meat | 15 | 3 | 365 | High protein; requires salt to produce |
| `fresh_meat` | Fresh Meat | 12 | 5 | 3 | Spoils rapidly; local consumption only |
| `ale_barrel` | Ale | 8 | 30 | 14 | Caloric supplement + morale; grain sink |
| `firewood` | Firewood | 2 | 20 | — | Heating fuel; winter survival in cold biomes |

**Production sources:** Agriculture, livestock slaughter, milling, baking, brewing, forestry  
**Consumption:** All population classes (rates differ — see §3)  
**Locality:** Wheat and rye are regional (bulk, heavy). Bread and ale are local (spoilage). Salt is long-distance strategic.

---

### 1.2 Raw Materials

Extracted from environment. Settlement location determines availability; cannot be substituted.

| ID | Name | Base Value | Weight (kg) | Source Tag | Notes |
|----|------|-----------|-------------|-----------|-------|
| `iron_ore` | Iron Ore | 5 | 8 | `ore_iron` | Requires iron_mine building |
| `coal_chunk` | Coal | 4 | 10 | `ore_coal` | Better smelter fuel than charcoal |
| `stone_block` | Stone | 2 | 30 | `stone` | Universal construction material |
| `timber_log` | Timber | 3 | 80 | `wood` | Construction + charcoal + fuel |
| `clay_lump` | Clay | 1 | 5 | `clay` | Pottery, brick, construction |
| `flax_bundle` | Flax | 4 | 3 | `arable` | Textile fiber input |
| `wool_fleece` | Wool | 6 | 2 | `pasture` | Textile fiber input; from livestock |
| `hide_raw` | Raw Hide | 4 | 4 | — | Livestock byproduct |
| `bone_scrap` | Bone | 1 | 1 | — | Glue/tools byproduct; low value |
| `peat_brick` | Peat | 2 | 8 | `peat` | Fuel substitute in wet biomes |

**Production sources:** Mining, logging, farming, livestock  
**Consumption:** Building construction, production chain inputs  
**Locality:** All regional-to-long-distance. Heavy goods (stone, timber) discourage long haul.

---

### 1.3 Processed Goods (Mid-Chain)

Produced by applying labor + buildings to raw materials. Higher value density than inputs.

| ID | Name | Base Value | Weight (kg) | Chain Step | Notes |
|----|------|-----------|-------------|-----------|-------|
| `charcoal_sack` | Charcoal | 5 | 5 | timber → charcoal | Smelter fuel; better energy density than wood |
| `lumber_plank` | Lumber | 6 | 20 | timber → lumber | Sawn planks; better construction value |
| `iron_ingot` | Iron Ingot | 12 | 5 | ore → ingot | Universal metalwork input |
| `steel_billet` | Steel | 25 | 5 | ingot + charcoal + skill | High-end; requires smithy level 3 (town+) |
| `leather_cut` | Leather | 8 | 1 | hide + salt → leather | Tanning; armor + boots + harness |
| `cloth_bolt` | Cloth | 12 | 5 | flax/wool + loom | Clothing, sails, bags |
| `rope_coil` | Rope | 7 | 4 | hemp/flax + labor | Ships, construction, wagons |
| `pottery_jar` | Pottery | 5 | 3 | clay + kiln | Storage, trade containers |
| `brick_unit` | Brick | 3 | 4 | clay + kiln + fuel | Town+ construction |
| `cut_stone` | Cut Stone | 8 | 20 | stone + masonry | Castle/city construction |
| `glass_pane` | Glass | 15 | 2 | sand + potash + heat | Luxury; town+ |
| `candle_box` | Candles | 9 | 0.5 | tallow + wick | Interior lighting; consumed by upper classes |
| `parchment` | Parchment | 18 | 0.2 | hide + skill | Administrative; noble + clerical demand |

---

### 1.4 Tools and Productive Capital

Consumed slowly over time (degradation). They multiply production efficiency, not enable it. A smithy without tools still works — just at 0.6× output.

| ID | Name | Base Value | Durability (ticks) | Effect |
|----|------|-----------|-------------------|--------|
| `iron_tools` | Iron Tools | 15 | 180 | +30% labor factor for agriculture/extraction |
| `steel_tools` | Steel Tools | 35 | 360 | +60% labor factor; requires smithy level 3 (town+) |
| `millstone` | Millstone | 40 | 720 | Required component for grain_mill; degrades slowly |
| `bellows_set` | Bellows | 25 | 360 | +20% smelter throughput |
| `loom_frame` | Loom | 20 | 240 | Required for cloth production |
| `wagon_unit` | Wagon | 30 | 180 | +1 trade route capacity slot per wagon |
| `plow_iron` | Iron Plow | 20 | 240 | +15% acre conversion rate (fallow→worked) |

**Tool degradation model:**
```
tool_stock[t+1] = tool_stock[t] - (usage_rate_per_tick * active_workers)
```
When `tool_stock < threshold` for a building type, output factor drops linearly to 0.6× floor.

---

### 1.5 Construction Materials

Consumed by the construction system when buildings are placed or upgraded.

| ID | Name | Base Value | Notes |
|----|------|-----------|-------|
| `timber_log` | Timber | 3 | Basic structure; all buildings |
| `lumber_plank` | Lumber | 6 | Better quality construction |
| `stone_block` | Stone | 2 | Durable structures; town+ |
| `brick_unit` | Brick | 3 | Urban construction; city+ |
| `cut_stone` | Cut Stone | 8 | Castle, walls, cathedral |
| `iron_ingot` | Iron Ingot | 12 | Hinges, brackets, nails |
| `thatch_bundle` | Thatch | 1 | Hamlet roof material |
| `lime_mortar` | Lime Mortar | 4 | Stone bonding; from limestone |

---

### 1.6 Military Goods

Consumed by militia/soldiers. Demand is zero in peaceful settlements; spikes during threat events.

| ID | Name | Base Value | Notes |
|----|------|-----------|-------|
| `iron_sword` | Iron Sword | 30 | Requires smithy + ingots |
| `steel_sword` | Steel Sword | 80 | Smithy level 3 (town+) |
| `spear_unit` | Spear | 12 | Timber + iron tip |
| `shield_unit` | Shield | 18 | Timber + leather |
| `chain_mail` | Chain Mail | 60 | Requires rings from ingots; labor intensive |
| `plate_armor` | Plate Armor | 200 | Steel + armoury (city+); rare |
| `arrow_bundle` | Arrows (×20) | 8 | Fletcher building; timber + iron |
| `siege_bolt` | Siege Bolt | 25 | Ballista ammunition; strategic |

---

### 1.7 Luxury and Trade Goods

No survival value. Drive coin circulation and noble/merchant demand. Imported from distant regions.

| ID | Name | Base Value | Notes |
|----|------|-----------|-------|
| `spice_unit` | Spices | 50 | Long-distance only; no local source |
| `wine_cask` | Wine | 20 | Warm biomes only; noble demand |
| `dye_pigment` | Dye | 30 | Rare plants / minerals |
| `fur_pelt` | Fur | 35 | Cold biome only; noble warmth + prestige |
| `jewelry_piece` | Jewelry | 80 | Gold/gem + artisan |
| `book_tome` | Book | 40 | Parchment + scholar; noble + clerical |
| `ivory_piece` | Ivory | 60 | Rare animal source |
| `silk_bolt` | Silk | 90 | Foreign origin; city+ market |

---

### 1.8 Abstract Resources (First-Class Tracked Quantities)

These are not traded goods. They are per-settlement capacity limits that gate production.

| Resource | Unit | How It's Set | What It Gates |
|----------|------|-------------|---------------|
| `labor_pool` | worker-days/tick | Population × class_labor_rate | All production |
| `storage_capacity` | units | Sum of all storage buildings | Max inventory per good |
| `transport_slots` | parties/tick | Wagons + road quality | Trade party count |
| `security_level` | 0–1 float | Militia size + walls + faction | Bandit risk multiplier |
| `skill_labor[type]` | worker-days/tick | NPC assignments by skill | Specialty production |
| `draft_animals` | head | Livestock head × draft_fraction | Farming + transport |

---

## 2. Production Chains

### 2.1 Grain Chain

```
TERRAIN: arable cell
    ↓ [Farm Plot — no building required at hamlet tier]
wheat_bushel  (acreage × fertility × labour_factor)
    ↓ [Grain Mill — millstone required]
flour_sack  (1.0 wheat → 0.85 flour, 0.15 bran byproduct)
    ↓ [Bakehouse]
bread_loaf  (0.3 flour + 0.05 firewood → 1 loaf)
    ↓ [Brewery — optional branch]
ale_barrel  (0.5 wheat + 0.02 firewood + 3 days → 1 barrel)
```

**Bottlenecks:**
- Millstone durability: mills run at 0.4× without intact millstone
- Firewood dependency: bakehouse and brewery compete for wood with heating
- Bran byproduct → animal feed (reduces livestock feed cost by 20%)

**Seed retention rule:**  
Settlement must hold back `seed_reserve = worked_acres × 0.012 × wheat_bushel` before exporting. Seed is never offered to trade parties.

---

### 2.2 Livestock Chain

```
TERRAIN: pasture cell  +  fodder (grain or bran)
    ↓ [Pasture — no building]
livestock_head  (breeding rate = 0.004/head/tick when fed)
    ↓ [Butcher — building]
fresh_meat      (1 head → 3 fresh_meat + 1 hide_raw + 0.5 bone_scrap)
    ↓ [Smokehouse/Salting — requires salt]
preserved_meat  (1 fresh_meat + 0.2 salt → 1 preserved_meat)
    ↓ [Tannery — requires salt + water_access tag]
hide_raw → leather_cut  (1 hide + 0.3 salt + 4 labor-days → 2 leather)
    ↓ [Shearer — wool fleece pathway]
livestock_head → wool_fleece  (1 sheep head → 0.3 wool/tick when alive)
```

**Bottlenecks:**
- Salt dependency: without salt, hides and fresh meat cannot be processed → forced local consumption
- Water tag requirement: tanneries must be on a cell with `water_access` tag
- Fodder competition: livestock competes with human population for grain

**Livestock fodder model:**
```
fodder_needed = livestock_head × 0.008 per tick
fodder_sources = [bran_byproduct, pasture_bonus, grain_allocation]
if fodder_deficit > 0:
    livestock_head -= livestock_head × 0.02 × deficit_fraction  # herd shrinks
```

---

### 2.3 Timber Chain

```
TERRAIN: cell with wood tag
    ↓ [Lumber Camp]
timber_log  (woodlot_acres × 0.002 × yield_per_wd)
    ↓ [Sawmill — waterwheel bonus if river tag]
lumber_plank  (1 timber_log → 0.75 lumber + 0.25 sawdust/scrap)
    ↓ [Charcoal Kiln — branch]
charcoal_sack  (3 timber_log + 2 labor-days → 5 charcoal)
    ↓ [Cooperage — branch]
timber_log + iron_hoops → barrel_unit  (storage for ale, salt, fish)
```

**Bottlenecks:**
- `wood` resource tag is finite (tracked as `forest_density` 0–1 on cell)
- Deforestation model: `forest_density -= logging_rate × 0.0001 per tick`; regenerates at `0.00003 per tick`
- Below `forest_density = 0.2`, extraction yield halves

---

### 2.4 Iron Chain

```
TERRAIN: cell with ore_iron tag
    ↓ [Iron Mine — requires iron_tools]
iron_ore  (miners × skill_factor × 0.8 units/worker-day)
    ↓ [Iron Smelter — requires charcoal OR coal]
iron_ingot  (2 iron_ore + 1 charcoal_sack → 1 iron_ingot)
    ↓ [Smithy level 1–3 — hamlet+]
iron_tools   (2 ingot + 0.5 charcoal → 1 iron_tools, 2 wd)
spear_unit   (1 ingot + 1 timber → 1 spear, 1 wd)
    ↓ [Smithy level 3+ — village+]
iron_sword   (4 ingot + 1 charcoal → 1 iron_sword, 4 wd)
shield_unit  (2 timber + 1 leather → 1 shield, 2 wd)
    ↓ [Smithy level 5+ — village+]
steel_billet (2 ingot + 2 charcoal + 4 wd + journeyman_skill → 1 steel)
chain_mail   (8 ingot + 6 wd → 1 chain_mail)
    ↓ [Smithy level 7+ — town+]
plate_armor_components  (6 steel + 12 wd → plate components)
    ↓ [Armoury — hard gate: city+]
plate_armor  (plate_components ×2 + 20 wd + master_skill → 1 plate_armor)
```

**Bottlenecks:**
- Charcoal/coal competition: smelters and smithies compete for fuel; large iron centers drain surrounding timber
- Ore depletion: `ore_density` on cell depletes at logging rate; no regeneration (finite resource)
- Skill gate: steel production requires `skill_labor["smithing"] >= 5` on assigned NPC

---

### 2.5 Textile Chain

```
TERRAIN: arable (flax) or pasture (wool)
    ↓ [Farm Plot / Pasture]
flax_bundle or wool_fleece
    ↓ [Retting Pit — flax only, water_access required]
flax_bundle → retted_fiber  (3 days soak, no building input)
    ↓ [Weaving Shed — loom_frame required]
retted_fiber or wool_fleece → cloth_bolt  (3 fiber + 2 wd → 1 cloth)
    ↓ [Dye House — optional, requires dye_pigment]
cloth_bolt + dye_pigment → dyed_cloth  (+4 base value)
    ↓ [Tailor — building]
cloth_bolt → clothing_set  (consumed by population class demand)
    ↓ [Rope Walk — hemp variant]
hemp_bundle → rope_coil  (2 hemp + 1 wd → 1 rope)
```

---

### 2.6 Clay/Stone Chain

```
TERRAIN: clay tag
    ↓ [Clay Pit]
clay_lump  (workers × 1.5 units/wd)
    ↓ [Pottery Kiln — requires firewood fuel]
clay_lump → pottery_jar   (2 clay + 0.3 wood → 3 jars, 1 wd)
clay_lump → brick_unit    (3 clay + 0.5 wood → 10 bricks, 2 wd)

TERRAIN: stone tag
    ↓ [Quarry]
stone_block  (workers × 0.6 units/wd — heavy, slow)
    ↓ [Mason's Yard — requires iron_tools]
stone_block → cut_stone   (2 stone + 1 wd + iron_tools → 1 cut_stone)
    ↓ [Lime Kiln — limestone subtype tag]
stone_block → lime_mortar (3 stone + 2 wood + 3 wd → 10 mortar)
```

---

## 3. Settlement Demand Model

### 3.1 Population Consumption Formula

For each good `g` and each population class `c` at settlement `s`:

```
consumption_needed(s, g, tick) =
    SUM over classes c:
        population[s][c]
        × consumption_rate[c][g]          # from population_class JSON
        × season_modifier[g][current_season]
        × 1.0                              # (future: comfort modifier)
```

**Consumption rates by class (per head per tick):**

| Good | Peasant | Artisan | Merchant | Noble |
|------|---------|---------|----------|-------|
| `wheat_bushel` | 0.015 | 0.015 | 0.020 | 0.040 |
| `rye_bushel` | 0.010 | 0.008 | 0.005 | 0.000 |
| `bread_loaf` | 0.000 | 0.005 | 0.015 | 0.030 |
| `ale_barrel` | 0.002 | 0.003 | 0.005 | 0.008 |
| `fresh_meat` | 0.001 | 0.003 | 0.008 | 0.020 |
| `preserved_meat` | 0.002 | 0.004 | 0.010 | 0.025 |
| `firewood` | 0.003 | 0.004 | 0.006 | 0.012 |
| `cloth_bolt` | 0.000 | 0.000 | 0.001 | 0.003 |
| `salt_unit` | 0.001 | 0.001 | 0.002 | 0.003 |
| `candle_box` | 0.000 | 0.001 | 0.002 | 0.005 |
| `spice_unit` | 0.000 | 0.000 | 0.001 | 0.004 |

### 3.2 Safety Reserve Formula

Each settlement maintains a minimum reserve before it will trade any good outward:

```
safety_reserve(s, g) =
    consumption_needed(s, g, tick) × SAFETY_DAYS

SAFETY_DAYS:
    hamlet:      21
    village:     28
    town:        35
    city:        42
    metropolis:  56
```

A settlement's trade-eligible surplus for good `g`:
```
tradeable_surplus(s, g) =
    inventory[s][g]
    - safety_reserve[s][g]
    - seed_reserve[s][g]       # grain only
    - productive_input_reserve[s][g]  # e.g. charcoal held for smelter
```
Only positive values of `tradeable_surplus` are eligible for export.

### 3.3 Seasonal Reserves

Added on top of safety reserve at crop harvest time (autumn tick):

```
seasonal_reserve(s, g) =
    IF g is grain AND current_season == AUTUMN:
        consumption_needed(s, g) × WINTER_BUFFER_DAYS   # 90 days
    ELSE:
        0
```

The settlement will not dispatch trade parties that would reduce autumn grain below this level.

### 3.4 Seed Retention

Only for agricultural goods:

```
seed_reserve(s) =
    worked_acres[s] × seed_rate[crop]    # wheat: 0.012 bu/acre
```

Seed is locked and never exported.

### 3.5 Spoilage / Decay

Applied to inventory at start of each pulse, before production:

```
FOR each good g in inventory[s]:
    IF spoilage_days[g] > 0:
        decay_rate = 1.0 / spoilage_days[g]
        # Accelerate decay if storage_quality < 0.5
        IF storage_quality[s] < 0.5:
            decay_rate *= 1.5
        inventory[s][g] *= (1.0 - decay_rate)
        IF inventory[s][g] < 0.01:
            inventory[s][g] = 0.0
```

`storage_quality` = fraction of max storage capacity currently in use (inverted: 0.0 = full/bad, 1.0 = empty/well-preserved).

### 3.6 Productive Input Demand

Beyond consumption, settlements need goods to run production chains:

```
productive_input_demand(s, g) =
    SUM over all active recipes r in settlement:
        batches_planned[s][r]
        × recipe_inputs[r][g]
```

This demand is scored at high priority (equal to survival) in the production decision model. A smelter that can't get charcoal stops running.

### 3.7 Luxury Demand by Tier

```
luxury_demand_weight(tier):
    hamlet:      0.00
    village:     0.05
    town:        0.15
    city:        0.35
    metropolis:  0.60
```

Luxury demand only activates when survival goods are fully satisfied (prosperity > 0.6).

---

## 4. Production Decision Model

### 4.1 Priority Score Formula

For each possible production action `a` at settlement `s` on tick `t`:

```
priority(s, a) =
    need_pressure(s, a.output_good)
    + profit_signal(s, a)
    + strategic_value(s, a)
    + local_bonus(s, a)
    - production_cost(s, a)
    - import_availability(s, a.output_good)
```

**Variable definitions:**

```
need_pressure(s, g) =
    clamp(
        (consumption_needed(s, g) - inventory[s][g]) / consumption_needed(s, g),
        0.0, 1.0
    ) × 10.0
    # 0 = fully stocked; 10.0 = entirely depleted

profit_signal(s, a) =
    local_price[s][a.output_good]
    - SUM(local_price[s][input] × input_qty[a][input] for input in a.inputs)
    # Normalized to [0, 5] range via clamp

strategic_value(s, a) =
    IF a.output_good in STRATEGIC_GOODS:     # tools, weapons, grain
        2.0 + faction_demand_modifier[s]
    ELSE:
        0.0

local_bonus(s, a) =
    terrain_bonus(s, a)     # fertility/ore presence bonus, 0–3
    + building_bonus(s, a)  # existing related building = +1.5
    + skill_bonus(s, a)     # NPC skill match bonus, 0–2

production_cost(s, a) =
    labor_fraction_required(s, a)  # 0–5; how much of free labor it consumes
    + input_scarcity_penalty(s, a) # 0–3; inputs are locally scarce

import_availability(s, a.output_good) =
    IF route_to_supplier exists AND supplier_has_surplus:
        price_ratio_discount × 3.0  # reduce incentive to produce locally
    ELSE:
        0.0
```

### 4.2 Labor Allocation

Total labor pool per tick:

```
labor_pool(s) =
    SUM over classes c:
        population[s][c] × class_labor_rate[c]

class_labor_rate:
    peasant:  0.80   # most labor goes to field/mine
    artisan:  0.90   # skilled labor
    merchant: 0.30   # mostly trade/commerce
    noble:    0.05   # minimal physical labor
```

Labor is allocated in strict priority order:

1. **Survival production**: farm_grain, log_timber for heat, livestock maintenance
2. **Active supply chain inputs** (charcoal for smelter, ore for smelter)
3. **Productive chain outputs** (ingots, tools, cloth)
4. **Surplus export production** (additional grain, luxury goods)
5. **Construction labor** (building placement)

Unallocated labor = idle workers. Idle rate above 15% triggers a `build_pressure` flag that raises block construction priority.

### 4.3 Industrial Inertia

A settlement cannot instantly retool. When a production action's priority drops below a threshold:

```
retool_cooldown[s][a] = max_ticks_before_retool × (1 - priority(s, a) / 10.0)
```

`max_ticks_before_retool` = 90 (3 months of real game time). During cooldown, the action continues at reduced output (50%) rather than stopping entirely. This models skill retention and capital lock-in.

### 4.4 Block and Building Capacity

Each active recipe requires:
- A building instance in `settlement.buildings`
- Free workers ≤ `building.max_workers`
- Tool stock above minimum (else 0.6× output penalty)

If a recipe's building is absent, `priority(s, a)` feeds into **build_demand** for that building type, not into production.

```
build_demand(s, building_type) =
    IF priority(s, linked_recipe) > BUILD_THRESHOLD (= 6.0):
        priority(s, linked_recipe) - BUILD_THRESHOLD
    ELSE:
        0.0
```

The ConstructionSystem picks the highest `build_demand` building each tick.

---

## 5. Price and Market Formulas

### 5.1 Local Price Formation

Same exponential smoothing approach, but extended with supply-chain depth and route influence:

```
# Step 1: target from supply/demand ratio
target_price(s, g) =
    clamp(
        base_value[g] × (estimated_demand(s, g) / max(inventory[s][g], ε)),
        base_value[g] × PRICE_FLOOR,
        base_value[g] × PRICE_CAP
    )

# Step 2: route influence — nearby settlements price bleeds in
route_price_signal(s, g) =
    IF any direct route r to settlement n exists:
        weighted_avg(price[n][g], weight = 1.0 / (route_cost[r] + 1))
    ELSE:
        0.0 (no signal)

# Step 3: blend local target with route signal
blended_target(s, g) =
    target_price(s, g) × (1 - ROUTE_BLEND)
    + route_price_signal(s, g) × ROUTE_BLEND
    # ROUTE_BLEND = 0.15 — local conditions dominate

# Step 4: smooth toward blended target
price[s][g] =
    lerp(price[s][g], blended_target(s, g), SMOOTH)
    # SMOOTH = 0.20 per pulse
```

### 5.2 Seasonal Price Shifts

```
seasonal_multiplier(g, season) =
    IF g in GRAIN_GOODS:
        SPRING: 1.20    # winter stores depleted, new harvest weeks away
        SUMMER: 0.85    # harvest coming, prices ease
        AUTUMN: 0.70    # fresh harvest glut
        WINTER: 1.40    # stores shrinking, cold premium
    IF g == firewood:
        SPRING: 0.60    SUMMER: 0.50    AUTUMN: 0.90    WINTER: 2.00
    ELSE:
        1.0  (no seasonal adjustment)
```

Applied to `base_value[g]` before price formation each pulse.

### 5.3 Transport Cost Effect

Price of good `g` at destination `d` arriving from origin `o`:

```
delivered_price(o, d, g) =
    price[o][g]
    + transport_cost_per_unit(o, d, g)

transport_cost_per_unit(o, d, g) =
    route_distance(o, d)
    × COST_PER_TILE          # base: 0.02 coin/kg/tile
    × weight_kg[g]
    × (1.0 + road_quality_penalty[route])  # 0.0 = paved, 0.5 = dirt, 1.5 = no road
    × (1.0 + season_haul_penalty[route][season])  # mud season: +0.5
```

A trade party will only dispatch if:
```
delivered_price(o, d, g) < price[d][g] × 0.85   # 15% profit margin minimum
```

### 5.4 Tariffs, Tolls, and Risk

```
effective_cost(o, d, g) =
    delivered_price(o, d, g)
    + toll_on_route[o][d]                     # faction-controlled road toll
    + price[o][g] × bandit_risk_premium[route]  # bandit risk = expected loss

bandit_risk_premium(route) =
    route_danger_level × 0.1   # danger_level 0–1; 0.1 = 10% cargo loss expected
```

At `bandit_risk_premium > 0.4`, most trade party spawns abort. Routes with persistent high danger push production toward local self-sufficiency.

### 5.5 Price Volatility Events

```
apply_price_shock(s, g, cause):
    IF cause == "raid":
        inventory[s][g] *= 0.5 to 0.8     # partial loss
        price[s][g] *= 2.5                 # instant spike, will smooth down
    IF cause == "route_blocked":
        route_price_signal now absent       # no bleed from neighbor
        target_price(s, g) increases       # local supply must compensate
    IF cause == "bumper_harvest":
        inventory[s][g] += bonus_yield
        price[s][g] *= 0.6                 # immediate glut signal
```

---

## 6. Trade and Route Throughput

### 6.1 Per-Route Model

Each route edge between settlements `o` and `d` tracks:

```
RouteState:
    origin_id:          String
    destination_id:     String
    road_quality:       float   # 0.0–1.0; 0 = trail, 1 = paved road
    distance_tiles:     int
    travel_days:        int     # ceil(distance / speed × road_penalty)
    danger_level:       float   # 0.0–1.0; bandit presence
    toll_rate:          float   # coin per wagon per trip; faction-set
    seasonal_penalty:   Dict    # season → capacity multiplier
    capacity_slots:     int     # max simultaneous parties; scales with road_quality
    active_parties:     int     # count of current in-transit parties
    reliability_score:  float   # rolling average trip success; 0–1
```

### 6.2 Travel Time

```
travel_days(route) =
    ceil(
        route.distance_tiles
        / (PARTY_SPEED_TILES_PER_DAY × road_quality_speed_bonus[road_quality])
        × seasonal_penalty[route][season]
    )

PARTY_SPEED_TILES_PER_DAY = 2.0
road_quality_speed_bonus:
    0.0–0.3:  0.6   (trail/no road)
    0.3–0.6:  0.8   (dirt road)
    0.6–0.9:  1.0   (maintained road)
    0.9–1.0:  1.3   (paved highway)

seasonal_penalty:
    SUMMER: 1.0    SPRING: 1.2 (mud)    AUTUMN: 1.1    WINTER: 1.4
```

### 6.3 Trade Party Decision Logic

```
func should_dispatch_party(origin, destination, good):
    surplus = tradeable_surplus(origin, good)
    IF surplus <= 0:
        return false
    IF party_already_in_transit(origin, destination, good):
        return false
    IF active_parties[route] >= capacity_slots[route]:
        return false
    
    profit = delivered_price(origin, destination, good) - price[origin][good]
    cost   = transport_cost_per_unit(origin, destination, good)
              + toll_rate[route]
    expected_loss = price[origin][good] × bandit_risk_premium[route]
    
    IF profit - cost - expected_loss < MIN_PROFIT_MARGIN:
        return false
    
    destination_need = score_destination_need(destination, good)
    IF destination_need < SHORTAGE_THRESHOLD:
        return false
    
    return true
```

### 6.4 Settlement Import Decision

```
func should_import(settlement, good):
    days_of_supply = inventory[s][good] / max(consumption_needed(s, good), ε)
    
    IF days_of_supply > IMPORT_STOP_THRESHOLD:   # = 60 days
        return false   # amply stocked
    
    # Check if local production can cover deficit
    local_production_rate = estimate_local_output(s, good)
    production_sufficiency = local_production_rate / consumption_needed(s, good)
    
    IF production_sufficiency > 0.9:
        return false   # nearly self-sufficient; producing locally is cheaper
    
    # Check route reliability
    best_route = find_route_with_surplus(settlement, good)
    IF best_route == null:
        return false   # no available supplier
    
    IF route_reliability_score[best_route] < 0.3:
        # Route is unreliable — shift toward local production instead
        increase_production_priority(s, good)
        return false
    
    return true


func decide_response_to_shortage(settlement, good):
    # Returns one of: IMPORT | PRODUCE_LOCAL | SUBSTITUTE | STOCKPILE | ACCEPT_SHORTAGE
    
    local_viable  = local_bonus(s, good) > 2.0 AND labor_available > 0.3
    import_viable = reliable_route_exists(s, good) AND profit_positive(s, good)
    substitute    = has_substitute_good(good) AND substitute_in_stock(s)
    
    IF import_viable AND NOT local_viable:
        return IMPORT
    IF local_viable AND NOT import_viable:
        return PRODUCE_LOCAL
    IF import_viable AND local_viable:
        return IMPORT IF import_cheaper ELSE PRODUCE_LOCAL
    IF substitute:
        return SUBSTITUTE
    IF inventory[s][good] > 0 AND days_of_supply > 7:
        return STOCKPILE   # ration and wait
    RETURN ACCEPT_SHORTAGE   # triggers unrest
```

---

## 7. Settlement Class Behavior

### Hamlet (≈100 people)

**Role:** Subsistence plus one extractive export  
**Production:** Grain (primary), plus one of: timber, ore, wool, or clay depending on terrain  
**Imports:** Salt, iron_tools, occasional pottery  
**Exports:** Raw material surplus (timber, ore, or grain if excellent fertility)  
**Self-sufficiency:** 80–95% for survival goods  
**Specialization:** Low — one extractive specialty maximum  
**Building slots:** 6–10 distinct building types (`max_slots = 8`)

```
Typical buildings (level range):
    Farm            level 1–4
    Pasture         level 1–2
    Storage Shed    level 1–2
    Well            level 1
    + 1 specialty:  Lumber Camp OR Clay Pit OR Iron Mine (level 1–3)
```

---

### Village (≈500 people)

**Role:** Regional food producer + first-tier processor  
**Production:** Wheat surplus, timber, + one processing chain (mill or smelter)  
**Imports:** Iron tools, cloth, salt, specialty goods  
**Exports:** Flour, processed goods, livestock  
**Self-sufficiency:** 70–85%  
**Specialization:** Medium — 1–2 specialty chains  
**Building slots:** 14–20 distinct building types (`max_slots = 16`)

```
Typical buildings (level range):
    Farm            level 3–6
    Grain Mill      level 1–3
    Lumber Camp     level 1–4
    Smithy          level 1–4
    Market Stall    level 1–2
    Housing         level 1–4
    Storage         level 1–3
    Well            level 1–2
    Pasture         level 2–4
```

---

### Town (≈1,500 people)

**Role:** Regional market hub + full processing center  
**Production:** Limited surplus grain, full processing chains, specialty manufactured goods  
**Imports:** Raw ore, raw timber, grain from multiple villages  
**Exports:** Iron tools, cloth, flour, processed goods  
**Self-sufficiency:** 50–65% (depends heavily on hinterland food imports)  
**Specialization:** High — 2–4 full production chains  
**Building slots:** 24–35 distinct building types (`max_slots = 28`)

```
Typical buildings (level range):
    Farm            level 4–6 (edge zone only)
    Iron Smelter    level 1–5
    Smithy          level 3–6
    Grain Mill      level 3–6
    Bakehouse       level 1–4
    Weaving Shed    level 1–4
    Market          level 3–5
    Tavern          level 1–4
    Housing         level 4–7
    Warehouse       level 2–5
    Tannery         level 1–4 (if water_access)
```

---

### City (≈5,000 people)

**Role:** Regional capital, high-tier manufacturing, military production  
**Production:** Steel, weapons, armor, luxury goods, administrative output  
**Imports:** All bulk raw materials from many villages/towns  
**Exports:** Finished goods, weapons, tools, cloth — high value density only  
**Self-sufficiency:** 20–35%; heavily dependent on food imports  
**Specialization:** Very high — multiple full chains + military production  
**Building slots:** 40–60 distinct building types (`max_slots = 50`)

```
Typical buildings by zone (level range):
    [Agricultural edge]
      Farm            level 5–7
    [Industrial]
      Iron Smelter    level 4–8
      Smithy          level 5–8
      Charcoal Kiln   level 3–6
    [Textile]
      Weaving Shed    level 4–7
      Dye House       level 2–5
    [Merchant]
      Market          level 5–8
      Warehouse       level 4–8
      Inn             level 2–6
    [Residential]
      Housing (low)   level 6–9
      Manor House     level 3–6
    [Military]
      Armoury         level 1–5
      Barracks        level 3–6
    [Civic]
      Town Hall       level 3–6
      Church          level 3–7
      Granary         level 5–8
```

---

### Metropolis (≈10,000 people)

**Role:** Capital of regional economy; price setter; luxury hub; military complex  
**Production:** Highest-tier goods (plate armor, silk weaving, books); administrative output; financial services  
**Imports:** Enormous volumes of all raw and processed goods from entire region  
**Exports:** High-value manufactured goods, military equipment, financial instruments (future)  
**Self-sufficiency:** 5–15%; effectively cannot survive without continuous supply chains  
**Specialization:** Maximum — every production chain present at peak quality  
**Building slots:** 70+ distinct building types (`max_slots = 80`), all buildings pushed toward level cap

**Metropolis-specific mechanics:**
- Maintains **strategic grain reserve** (faction-level store, not settlement inventory)  
- Sets **regional reference price** for high-volume goods (other settlements' prices drift toward metropolis price over 30+ ticks)  
- Can absorb surplus from entire region without price floor triggering (large stomach effect)  
- Military production gate: plate armor, steel weapons only available here

---

## 8. Building System

### 8.1 District Categories

Districts are **conceptual classification zones**, not a spatial block grid. They describe what category of buildings a settlement tends to develop in each zone. The underlying data model is `buildings: Dictionary` (building_id → level) plus `max_slots: int` — no spatial tile assignment is required.

At hamlet and village tier, districts are implicit (no formal layout). At town+ tier they can be stored as a `district_layout` dict in `SettlementState` for use by rendering and city generation.

| District | Eligible Building Types | Practical From Tier |
|----------|------------------------|--------------------|
| `agricultural_edge` | Farm, Pasture, Orchard, Vineyard, Beehive | All |
| `extraction` | Lumber Camp, Clay Pit, Iron Mine, Stone Quarry, Peat Bog | All |
| `processing` | Grain Mill, Smithy, Smelter, Tannery, Brewery, Sawmill | All (grown from extraction) |
| `residential_low` | Cottage, Longhouse, Tenement | All |
| `residential_high` | Manor, Townhouse, Merchant Hall | Town+ |
| `storage` | Storage Shed, Granary, Warehouse, Ice House | All |
| `market` | Market Stall, Market Square, Trading Post, Bank | Village+ |
| `civic` | Well, Church, Town Hall, School, Hospital | All |
| `military` | Barracks, Armoury, Watch Tower, City Wall | Village+ |
| `workshop_specialty` | Weaving Shed, Dye House, Glassworks, Scriptorium | Town+ |
| `fuel_processing` | Charcoal Kiln, Coal Store, Peat Drying | Village+ |

### 8.2 Building Upgrade / Placement Formula

The governor AI scores each possible next action (build new building or upgrade existing) as:

```
action_score(s, building_type, target_level) =
    build_demand_score(s, building_type)       # from production decision model
    + district_fit_bonus(s, building_type)     # +2 if fits settlement's zone profile
    + adjacency_bonus(s, building_type)        # see §8.3
    - construction_cost_penalty(s, building_type, target_level)  # harder at high levels
    - level_penalty(s, building_type)          # diminishing returns on already-high level
    - hard_gate_block(s, building_type)        # -99 if hard-gated and tier too low

level_penalty(s, building_type) =
    current_level(s, building_type) × 0.5
    # Prevents over-specialising one building too early
    # Level 5 smithy gets -2.5 score reduction vs Level 1 smithy

construction_cost_penalty(s, building_type, target_level) =
    (actual_cost / s.crown_stock) × 3.0   # more expensive relative to treasury = higher penalty

hard_gate_block(s, building_type) =
    IF building_type in HARD_TIER_REQUIREMENTS:
        IF settlement.tier < HARD_TIER_REQUIREMENTS[building_type]:
            -99   # blocked entirely
    ELSE:
        0
```

**Cost formula (polynomial scaling, matching construction system):**

```
build_cost(building_type, target_level) =
    base_cost[building_type] × (target_level + 1)^2.2

Examples for Farm (base_cost = 500):
    Level 1 →    500 × 2^2.2    =    500 × 4.6   =  2,300 crowns
    Level 2 →    500 × 3^2.2    =    500 × 11.2  =  5,600 crowns
    Level 5 →    500 × 6^2.2    =    500 × 52.7  = 26,350 crowns
    Level 10 →   500 × 11^2.2   =    500 × 204   = 102,000 crowns
```

This ensures early levels are cheap to encourage settlement growth, while high-level buildings represent major investment only viable for wealthy large settlements.

### 8.3 Adjacency Bonuses

Buildings gain output or cost bonuses when built adjacent to complementary buildings:

| Building A | Adjacent To | Bonus |
|-----------|------------|-------|
| Iron Smelter | Lumber Camp / Charcoal Kiln | +15% throughput (fuel proximity) |
| Smithy | Iron Smelter | +10% throughput |
| Tannery | Butcher | +20% throughput (raw material proximity) |
| Bakehouse | Grain Mill | +10% throughput |
| Brewery | Grain Mill | +10% throughput |
| Market | Warehouse | +5% price discovery bonus (future) |
| Inn | Market | +5% merchant traffic |
| Church | Residential | +0.002 unrest reduction rate for adjacent blocks |

### 8.4 Building Levels (1–10)

All buildings scale from **level 1 to 10**. Levels represent cumulative investment and growth — a hamlet smithy is level 1–3, a city smithy is level 6–9. Settlement tier caps the maximum level accessible, and specific milestone levels unlock new recipes or capabilities.

**Tier → Max Building Level:**

| Settlement Tier | Max Building Level |
|----------------|-------------------|
| Hamlet (0) | 3 |
| Village (1) | 5 |
| Town (2) | 7 |
| City (3) | 9 |
| Metropolis (4) | 10 |

**Cost formula:**
```
cost(building_type, target_level) = base_cost[building_type] × (target_level + 1)^2.2
# Level 1: 4.6×base   Level 3: 16×base   Level 5: 52×base
# Level 7: 120×base   Level 10: 204×base
```

**Example: Smithy** (base cost: 400 crowns)

| Level | Milestone Name | Min Tier | Max Workers | Output Mult | New Recipes Unlocked | Cost (crowns) |
|-------|---------------|----------|-------------|-------------|---------------------|---------------|
| 1 | Village Forge | 0 | 1 | 0.40× | forge_tools, forge_spear | ~1,900 |
| 2 | Blacksmith | 0 | 1 | 0.55× | (no new) | ~4,500 |
| 3 | Iron Workshop | 0 | 2 | 0.70× | forge_sword, forge_shield | ~9,400 |
| 4 | Established Smithy | 1 | 2 | 0.85× | (no new) | ~17,000 |
| 5 | Master Forge | 1 | 3 | 1.00× | smelt_steel, forge_chain_mail | ~27,000 |
| 6 | Iron Works | 2 | 3 | 1.15× | (no new) | ~40,000 |
| 7 | Steel Foundry | 2 | 4 | 1.30× | forge_plate_components | ~57,000 |
| 8 | Grand Forge | 3 | 4 | 1.50× | (no new) | ~77,000 |
| 9 | Legendary Smithy | 3 | 5 | 1.75× | master_steel, masterwork_weapon | ~100,000 |
| 10 | Mythic Forge | 4 | 5 | 2.00× | (faction-unique recipes) | ~128,000 |

Milestone levels (1, 3, 5, 7, 9) are the meaningful thresholds — they unlock new recipes and should be the target for governor AI planning. Intermediate levels (2, 4, 6, 8) add workers and output without new capability.

**Example: Farm** (base cost: 500 crowns)

| Level | Milestone Name | Yield Bonus | Milestone Effect |
|-------|---------------|-------------|------------------|
| 1 | Fields | +50% base | Basic grain production |
| 2 | Expanded Fields | +100% | (no new) |
| 4 | Three-Field System | +200% | Crop rotation: fallow_acres × 0.15 returned to worked |
| 6 | Irrigation Network | +325% | Drought resistance; fertility floor raised |
| 8 | Plantation | +500% | Bulk export volumes; seed cost reduced |
| 10 | Industrial Farm | +700% | Metropolis only; feeds an entire city district |

**Buildings that remain hard-gated (population-density requirements):**
```
HARD_TIER_REQUIREMENTS = {
    "city_wall":    3,   # city+ — perimeter length + garrison mass
    "cathedral":    3,   # city+ — requires congregation population density
    "bank":         3,   # city+ — requires sufficient merchant class density
    "scriptorium":  3,   # city+ — requires literate artisan population
    "palace":       4,   # metropolis only
    "armoury":      3,   # city+ — military production scale
}
```
These buildings have no meaningful level-1 equivalent — they can't exist below their tier regardless of investment.

**Building data in the JSON schema:**

Buildings store their data as:
```json
{
  "id": "smithy",
  "base_cost": 400,
  "base_labor": 400,
  "max_level": 10,
  "category": "production",
  "desc": "Metal forging and weapons manufacture. Output scales with level.",
  "milestones": {
    "1": { "name": "Village Forge",    "flavor": "A simple hearth and anvil, enough for basic ironwork.",
            "max_workers": 1, "output_multiplier": 0.4, "recipes": ["forge_tools", "forge_spear"] },
    "3": { "name": "Iron Workshop",    "flavor": "A proper chimney, water-cooled trough, and room for an apprentice.",
            "max_workers": 2, "output_multiplier": 0.7, "recipes": ["forge_sword", "forge_shield"] },
    "5": { "name": "Master Forge",     "flavor": "The smith has learned the secrets of steel. The reek of charcoal never leaves.",
            "max_workers": 3, "output_multiplier": 1.0, "recipes": ["smelt_steel", "forge_chain_mail"] },
    "7": { "name": "Steel Foundry",    "flavor": "Bellows, trip-hammers, and a dozen journeymen. The sound carries for miles.",
            "max_workers": 4, "output_multiplier": 1.3, "recipes": ["forge_plate_components"] },
    "9": { "name": "Legendary Smithy", "flavor": "A master whose works are spoken of in distant courts.",
            "max_workers": 5, "output_multiplier": 1.75, "recipes": ["master_steel", "masterwork_weapon"] }
  }
}
```

In code, a settlement stores `buildings["smithy"] = 5` (level 5). The building system reads the milestone at or below the current level to determine active stats. The flat `tier_min` field on existing building files is superseded by this; hard-gated buildings use a separate `hard_tier_min` field instead.

### 8.5 Abandonment and Downgrade

```
IF building_type's production_priority < ABANDON_THRESHOLD (= 0.5)
   FOR sustain_ticks > ABANDONMENT_PATIENCE (= 180 ticks):
    
    building transitions to "derelict" state
    → output = 0; construction materials partially recoverable
    → block slot freed for re-use
    → NPC workers reassigned
```

Derelict buildings do not count toward `existing_count_decay` (the slot is truly freed).

---

## 9. Stability and Anti-Stupidity Rules

### 9.1 No Instant Industrial Flipping

```
# Production cannot shift more than this fraction per tick:
MAX_LABOR_REALLOCATION_PER_TICK = 0.10   # 10% of labor pool

# Building construction minimum time:
MIN_BUILD_TICKS = {
    "small" (1 block): 14
    "medium" (2 block): 30
    "large" (4+ block): 60
}
```

### 9.2 Reserve Thresholds

```
# Settlement will not export below survival floor
HARD_EXPORT_BLOCK:
    inventory[s][g] <= safety_reserve[s][g]  →  export_blocked = true

# Trade party dispatch suspended if coin reserve is < minimum
MIN_COIN_RESERVE = tier_population × 0.05
IF coin_inventory[s] < MIN_COIN_RESERVE:
    no outbound trade parties spawn
```

### 9.3 Import Trust Threshold

```
route_reliability_score[route] =
    rolling_average(
        last_20_trips: 1.0 if arrived, 0.0 if raided/blocked,
        weight_decay = 0.95  # older trips matter less
    )

import_trust_threshold = 0.4
IF route_reliability_score < import_trust_threshold:
    settlement stops treating route as reliable import
    shifts production_priority for that good upward by 3.0
    (tries to produce locally instead)
```

### 9.4 Crisis Behavior

```
FAMINE (inventory[wheat] < 3-day supply):
    - ALL labor reallocated to food production (override normal allocation)
    - All non-food exports suspended
    - Unrest accelerates at 3× normal rate
    - If famine persists > 30 ticks: population decline begins
    - Town+ can draw from strategic_grain_reserve (faction-level)

SIEGE:
    - All routes set to danger_level = 1.0
    - Production shifts to military goods
    - Consumption of all non-survival goods suspended
    - Trade parties cannot spawn; in-transit parties reroute or abort

WAR / HIGH DANGER:
    - All luxury production suspended
    - Storage capacity allocated 60% to military goods
    - Coin generation redirected to armoury construction
    
ECONOMIC COLLAPSE (prosperity < 0.1 for 30+ ticks):
    - Building abandonment threshold halved
    - Population class downgrade: merchant → artisan, artisan → peasant
    - Route reliability scores decay at 2× rate (merchants stop trading)
```

### 9.5 Skill and Labor Inertia

```
# Skilled workers cannot be instantly redeployed
skill_redeployment_cost(skill_type) =
    skill_labor[skill_type] × 14 ticks cooldown

# Example: smiths reassigned to farming → smithy idles for 14 ticks,
# output ramps back up over 7 ticks when reassigned back
```

### 9.6 Capital Friction

```
# Starting a new production chain from zero requires:
chain_startup_cost(recipe):
    1. Building must exist (ConstructionSystem)
    2. Initial tool stock: tool_stock[building] >= MIN_TOOL_STOCK
       (requires importing or producing tools first)
    3. Labor familiarity: first 14 ticks at 50% output (learning curve)
    4. Input buffer: settlement must have > 5 days of inputs before starting
       (prevents starting a chain that immediately halts for lack of inputs)
```

---

## 10. Example Seasonal Tick

**Setup:** Village "Ashford" (pop 400), Autumn, Tick 91  
**Goods state before tick:** wheat=480, flour=20, timber=85, coin=340

```
=== TICK 91 — AUTUMN, DAY 91 ===

[SPOILAGE]
  flour_sack:  480 × (1/90) = 5.3 units decayed → flour = 14.7
  bread_loaf:  0 (none in stock)
  (wheat: 180-day spoilage, negligible this early)

[PRODUCTION — ProductionLedger]
  farm_grain (worked_acres=90, fertility=0.72, labour=1.1):
    output = 90 × 0.11 × 0.72 × 1.1 × 1 = 7.84 wheat
  log_timber (woodlot=30 acres):
    output = 30 × 0.002 × 0.5 × 1 = 0.03 timber
  mill_grain (1 mill, 2 workers, 5 wheat input):
    batches = 1 / 0.5 = 2 batches
    -2 wheat, +1.7 flour (×0.85)
    flour = 14.7 + 1.7 = 16.4

  After production:
    wheat = 480 + 7.84 - 2 = 485.84
    flour = 16.4
    timber = 85.03

[COIN INCOME]
  peasant (320): 320 × 0.01 = 3.2
  artisan  (60): 60 × 0.05 = 3.0
  merchant (15): 15 × 0.15 = 2.25
  noble     (5):  5 × 0.30 = 1.5
  coin += 9.95 → coin = 349.95

[CONSUMPTION]
  wheat demand:
    peasant: 320 × 0.015 = 4.8
    artisan: 60 × 0.015  = 0.9
    merchant: 15 × 0.020 = 0.3
    noble: 5 × 0.040     = 0.2
    total wheat demand = 6.2
  wheat supplied: min(6.2, 485.84) = 6.2  → shortfall = 0
  wheat = 479.64

  firewood demand:
    total ≈ 400 × 0.0035 (autumn rate) = 1.4
    timber used as firewood: 1.4 → timber = 83.63

[PRICE UPDATE — PriceLedger]
  wheat: supply=479.64, demand=6.2
    ratio = 6.2 / 479.64 = 0.013
    target = 4 × 0.013 = 0.052 → clamped to floor: 4 × 0.25 = 1.0
    seasonal_mult(wheat, AUTUMN) = 0.70 → effective base = 2.8
    target = 2.8 × 0.013 = 0.036 → clamp to 2.8 × 0.25 = 0.70
    current_price(wheat) = 3.20
    new_price = lerp(3.20, 0.70, 0.20) = 3.20 - 0.20×(3.20-0.70) = 2.70
    ✓ Autumn glut depressing wheat price as expected

  timber: supply=83.63, demand rising (winter approaching)
    target = 2 × (1.4×90 / 83.63) = 2 × 1.505 = 3.01 → within [0.5, 8.0]
    seasonal_mult(firewood, AUTUMN) = 0.90 → effective_base = 1.8
    target = clamp(1.8 × 1.505, 0.45, 7.2) = 2.71
    current = 2.0 → new = lerp(2.0, 2.71, 0.20) = 2.14
    ✓ Timber price rising heading into winter

[PROSPERITY & UNREST]
  no shortfall → prosperity += 0.005 (was 0.62 → 0.625)
  no shortfall → unrest -= 0.003 (was 0.08 → 0.077)

[WORKED ACRES ADJUSTMENT]
  target_worked = 100 × (0.40 + 0.50 × 0.625) = 100 × 0.7125 = 71.25
  current = 90 → adjust toward target: step = (71.25 - 90) × 0.1 = -1.875
  worked_acres = 88.1
  (Prosperity is good but autumn harvest complete — scaling back for winter)

[AUTUMN SEASONAL RESERVE CHECK]
  seed_reserve = 88.1 × 0.012 = 1.057 wheat (locked)
  seasonal_reserve = 6.2/tick × 90 ticks = 558 wheat NEEDED
  current = 479.64 → DEFICIT of 78.4 wheat against winter reserve target
  → import_flag set for wheat
  → trade party dispatch from Ashford BLOCKED for wheat
    (we're in deficit; we should be importing, not exporting)

=== END TICK 91 ===
Summary: Ashford is entering winter with a modest wheat deficit.
Prices falling (glut signal) but reserves low for winter. Needs imports.
```

---

## 11. Example Worked Scenario: Village + Town + City

**Setup:**
- `Millhaven` (Village, pop 450): high-fertility plains, grain surplus producer
- `Ironford` (Town, pop 1,400): ore cell, iron production center, grain-poor
- `Durnwall` (City, pop 4,800): capital region, consumes everything, exports steel and tools

**Tick 1 state:**
```
Millhaven:  wheat=900, iron_tools=2, coin=200
Ironford:   iron_ore=400, charcoal=150, wheat=80, iron_ingot=40, coin=500
Durnwall:   wheat=600, iron_ingot=200, steel=30, coin=3000
```

**Tick 1 events:**

```
Millhaven — PRODUCTION:
  farm_grain output: 90 acres × 0.11 × 0.88 fertility × 1.2 labour = 10.5 wheat
  wheat = 910.5

Millhaven — TRADE SPAWNER:
  tradeable_surplus(wheat) = 910.5 - safety(7.2×28=202) - seed(1.08) = 707 units
  Check route to Ironford: Ironford.shortages["wheat_bushel"] = 3.1 (active shortage)
  need_score = 3.1 × 2.0 = 6.2 → DISPATCH party carrying 707 × 0.5 = 353 wheat
  wheat = 910.5 - 353 = 557.5
  → TradeParty("millhaven→ironford", cargo: {wheat_bushel: 353}, travel=4 ticks)

Ironford — PRODUCTION:
  smelt_iron: 2 ore + 1 charcoal → 1 ingot
    charcoal=150: can run 150 batches → -300 ore, -150 charcoal, +150 ingot
    iron_ingot = 190
  forge_tools: 2 ingot + 0.5 charcoal → 1 iron_tools
    no charcoal left → HALT (Leontief: input missing)
  priority("log_timber") += 5.0 (charcoal shortage flagged)

Ironford — CONSUMPTION:
  wheat demand = 1400 × 0.016 avg = 22.4
  have = 80 → supplied = 22.4 → wheat = 57.6
  → shortfall = 0 this tick
  BUT days_of_supply = 57.6 / 22.4 = 2.57 days → CRITICAL
  priority("import wheat") = 10.0 (max)

Durnwall — PRODUCTION:
  forge_tools (Master Smithy): 2 ingot + skill → 1 iron_tools
    iron_ingot = 200 → 40 batches → 40 iron_tools, ingot = 120
  steel production: ongoing, 2 steel/tick

Durnwall — TRADE SPAWNER:
  iron_tools surplus = 40 - safety(12) = 28
  Check route to Ironford: Ironford.prices[iron_tools] = 18.5 (above base 15)
  delivered_price = 15.5 + 2 tiles × 0.02 × 3kg = 15.5 + 0.12 = 15.62 < 18.5 → PROFIT
  → TradeParty("durnwall→ironford", cargo: {iron_tools: 14}, travel=2 ticks)
```

**Tick 5 (parties arrive):**

```
Tick 3: Durnwall→Ironford iron_tools party arrives
  Ironford.inventory[iron_tools] += 14
  Forge can now run again (tools unblock charcoal production → restart)

Tick 5: Millhaven→Ironford wheat party arrives
  Ironford.inventory[wheat_bushel] += 353
  days_of_supply jumps from 0.8 to (353+remaining)/22.4 ≈ 17 days
  import_flag clears; unrest starts decaying

Tick 5 price effects:
  Ironford wheat price had spiked to 8.4 (4.0×base × smoothing)
  Now with arrival: supply/demand ratio flips → price falls toward 3.2 over 6 ticks
  Millhaven observes declining wheat price at Ironford via route signal
  → next dispatch will only fire if Ironford drops back to shortage
```

**Emergent behavior visible in this scenario:**
- Millhaven specializes in grain (comparative advantage: high fertility)
- Ironford specializes in iron (ore cell) but is grain-dependent → structural import need
- Durnwall acts as value-add processor (raw ingots → tools) and re-exporter
- The charcoal bottleneck in Ironford creates an emergent demand for timber from forest hamlets
- Price signals propagate with lag (travel time), not instantaneously

---

## 12. Implementation Guidance

### Files to Create/Modify

| File | Change |
|------|--------|
| `data/goods/*.json` | Add new goods from §1 resource list |
| `data/population_classes/*.json` | Add full consumption tables from §3.1 |
| `data/recipes/*.json` | Define all production chains from §2 |
| `data/buildings/*.json` | Add all buildings from §8; replace flat `tier_min` with `levels` array (§8.4); add `district` field |
| `src/simulation/economy/production_ledger.gd` | Add extraction and standard chain pathways; add tool degradation |
| `src/simulation/economy/settlement_pulse.gd` | Add spoilage step, seasonal reserve check, coin reserve check |
| `src/simulation/economy/price_ledger.gd` | Add seasonal multiplier, route price bleed, transport cost |
| `src/simulation/economy/trade_party_spawner.gd` | Add profit margin check, route capacity check, transport cost |
| `src/simulation/world/world_state.gd` | Add `route_states: Dictionary`, `seasonal_grain_reserve: Dictionary` |
| `src/simulation/settlement/settlement_state.gd` | Add `tool_stocks`, `district_layout`, `build_demand` |
| `src/simulation/economy/production_decision.gd` | **New file** — priority scoring system from §4 |
| `src/simulation/economy/route_state.gd` | **New file** — per-route tracking (§6.1) |
| `src/worldgen/building_placer.gd` | Integrate district + adjacency + economic weight system from §8 |

### Key Constants to Expose in Data (not code)

Every tuning parameter from §13 of `economy-design.md` should be moved to a `data/config/economy_config.json` file. This enables tuning without recompilation and makes the parameters accessible to the in-editor debug panel.

### Tick Order Addition

Extend the existing `SettlementPulse._tick_one` order with new steps:

| Step | System | New? |
|------|--------|------|
| 0 | Spoilage decay | **NEW** |
| 1 | Shortage reset | existing |
| 2 | Starter stock seed | existing |
| 3a | Production (agriculture) | existing |
| 3b | Production (extraction) | existing |
| 3c | Production (standard recipes) | existing |
| 3d | Tool degradation | **NEW** |
| 4a | Coin income | existing |
| 4b | Property income/upkeep | existing |
| 5 | Consumption | existing |
| 6 | Price update (with seasonal + route bleed) | extended |
| 7 | Prosperity and unrest | existing |
| 8 | Population growth/decline | existing |
| 9 | Worked-acre adjustment | existing |
| 10 | Seasonal reserve check | **NEW** |
| 11 | Production decision scoring (build_demand update) | **NEW** |

`TradePartySpawner.try_spawn_all()` stays outside `_tick_one`, called after all settlements have been processed — that ordering is correct.


---

## 13. Building Catalog

All buildings store data as `buildings[id] = level` (int) on the settlement. Levels 110 unless noted as hard-gated. Status:  implemented in `BuildingData.gd` / `data/buildings/`,  designed but not yet in data files,  referenced in code or design but not yet specified.

### 13.1 Agriculture & Extraction

| ID | Name | Base Cost | District | Economic Role | Status |
|----|------|-----------|----------|---------------|--------|
| `farm` | Farm / Fields | 500 | `agricultural_edge` | Core grain production; yield 0.5 per level; milestone 4 = Three-Field System, 6 = Irrigation |  |
| `farmstead` | Farmstead | 300 | `agricultural_edge` | Hamlet-tier subsistence farm; lower output, lower cost |  |
| `farm_plot` | Farm Plot | 150 | `agricultural_edge` | Single-acre plot; hamlet filler slot |  |
| `pasture` | Pasture | 400 | `agricultural_edge` | Wool, hides, meat, horses; scales with pasture_acres |  |
| `lumber_camp` | Lumber Camp | 350 | `extraction` | timber_log; required for construction and fuel chains |  |
| `iron_mine` | Iron Mine | 600 | `extraction` | iron_ore; feeds smelter chain; requires mountain/mineral terrain |  |
| `fishery` | Fishery | 300 | `extraction` | Fish output; requires coastal or river terrain |  |
| `open_land` | Open Land |  | any | Unimproved slot; placeholder for governor to build on |  |
| `clay_pit` | Clay Pit | 250 | `extraction` | Clay for brickmakers; also feeds ceramics chain |  |
| `stone_quarry` | Stone Quarry | 450 | `extraction` | Stone for walls and high-tier civic construction |  |
| `peat_bog` | Peat Bog | 200 | `extraction` | Peat fuel for settlements without coal or forest |  |
| `orchard` | Orchard | 350 | `agricultural_edge` | Fruit and cider crops; slow-growing long-term investment |  |

### 13.2 Processing & Industry

| ID | Name | Base Cost | District | Economic Role | Status |
|----|------|-----------|----------|---------------|--------|
| `smithy` | Smithy | 400 | `processing` | iron_tools  weapons  steel goods; milestone chain L1/3/5/7/9 (see 8.4) |  |
| `iron_smelter` | Iron Smelter | 500 | `processing` | iron_ore  iron_ingot; required upstream of smithy |  |
| `grain_mill` | Grain Mill | 350 | `processing` | wheat_bushel  flour_sack; +10% bakehouse/brewery adjacency bonus |  |
| `tannery` | Tannery | 400 | `processing` | hides  leather; +20% throughput if adjacent to butcher or pasture |  |
| `brewery` | Brewery | 300 | `processing` | grain  ale; happiness +0.002/day per level; +10% if adjacent to grain_mill |  |
| `weaver` | Weaver / Loom | 250 | `processing` | wool  cloth_bolt; cloth is a core consumption and trade good |  |
| `blacksmith` | Blacksmith | 400 | `processing` | Lower-cost iron processing; overlaps smithy  planned to merge into smithy L1-3 |  |
| `bronzesmith` | Bronzesmith | 450 | `processing` | copper + tin  bronze; early-game weapons and tools |  |
| `brickmaker` | Brickmaker | 300 | `processing` | clay + coal  bricks; used in high-tier building construction costs |  |
| `charcoal_kiln` | Charcoal Kiln | 200 | `fuel_processing` | wood  coal; required by smelter and smithy; +15% smelter throughput if adjacent |  |
| `sawmill` | Sawmill | 320 | `processing` | timber_log  construction lumber; reduces construction labor cost |  |
| `bakehouse` | Bakehouse | 280 | `processing` | flour_sack  bread; higher nutrition per bushel; +10% if adjacent to grain_mill |  |
| `tailor` | Tailor | 280 | `processing` | cloth_bolt  cloth_garment; required consumption good for artisan/noble classes |  |
| `dye_house` | Dye House | 350 | `workshop_specialty` | cloth + dye  fine_cloth; luxury goods chain; town+ |  |
| `goldsmith` | Goldsmith | 800 | `processing` | gold  coin / jewellery; high-value trade good; greedy governor priority |  |
| `glassworks` | Glassworks | 600 | `workshop_specialty` | sand + fuel  glass; windows, bottles, lenses; town+ |  |
| `butcher` | Butcher | 200 | `processing` | livestock_head  meat + hides; pairs with tannery (+20% adjacency) |  |

### 13.3 Storage & Logistics

| ID | Name | Base Cost | District | Economic Role | Status |
|----|------|-----------|----------|---------------|--------|
| `granary` | Granary | 1,200 | `storage` | Food storage cap 50% per level; starvation resistance; cautious governor priority |  |
| `warehouse_district` | Warehouse | 3,000 | `storage` | Total inventory storage 100% per level |  |
| `market_stall` | Market Stall | 100 | `market` | Smallest market building; hamlet-tier access to price discovery |  |
| `market` | Market | 1,000 | `market` | Industrial slots + trade income; merchant traffic multiplier; +5% if adjacent to warehouse |  |
| `merchant_guild` | Merchant Guild | 5,000 | `market` | Caravan range + capacity; global trade reach |  |
| `road_network` | Road Network | 2,000 | `logistics` | Caravan speed in region; trade volume multiplier for connected settlements |  |
| `well` | Well | 100 | `civic` | Fresh water; disease risk ; required before dense residential can be built |  |
| `inn` | Inn | 800 | `civic` | Merchant traffic +; migration pull; happiness for travellers; +5% if adjacent to market |  |
| `trading_post` | Trading Post | 1,500 | `market` | Remote market node; enables trade without a full market at small settlements |  |
| `ice_house` | Ice House | 400 | `storage` | Slows spoilage on perishables; required for high-tier food export chains |  |

### 13.4 Residential & Civil

| ID | Name | Base Cost | District | Economic Role | Status |
|----|------|-----------|----------|---------------|--------|
| `house` | House | 600 | `residential_low` | Population capacity +100 per level; required before population will grow |  |
| `housing_district` | Housing District | 1,000 | `residential_low` | Higher-density housing; +100 cap per level; builder governor priority |  |
| `tavern` | Tavern / Alehouse | 800 | `civic` | Happiness +0.002/day per level; migration pull; stability buffer |  |
| `church` | Church | 1,200 | `civic` | Happiness + stability +; village+ |  |
| `cathedral` | Cathedral | 8,000 | `civic` | Stability +, nobility loyalty +; hard-gated: city+ |  |
| `town_hall` | Town Hall | 2,500 | `civic` | Tax efficiency +; enables advanced governor AI options |  |
| `school` | School | 1,800 | `civic` | Artisan supply rate +; workforce skill growth over time |  |
| `hospital` | Hospital | 2,000 | `civic` | Disease resistance +; wound recovery for garrison; population death rate  |  |
| `bank` | Bank | 6,000 | `market` | Treasury compound interest; debt-financed construction; hard-gated: city+ |  |
| `scriptorium` | Scriptorium | 5,000 | `workshop_specialty` | Literacy rate +; unlocks advanced recipes; hard-gated: city+ |  |
| `palace` | Palace | 15,000 | `civic` | Capital designation; faction prestige; governor AI bonuses; hard-gated: metropolis |  |

### 13.5 Military & Defense

| ID | Name | Base Cost | District | Economic Role | Status |
|----|------|-----------|----------|---------------|--------|
| `stone_walls` | Walls / City Wall | 15,000 | `military` | Garrison strength ; siege hardness +; city_wall hard-gated: city+ |  |
| `barracks` | Barracks | 5,000 | `military` | Garrison cap + recruit pool (odd levels) and troop quality (even levels) |  |
| `training_ground` | Training Ground | 4,000 | `military` | Recruit tier unlock per level (L1T2, L3T3, L5T4, L7T5, L9T5 elite) |  |
| `watchtower` | Watchtower | 1,000 | `military` | Detection range +; early warning; small garrison bonus |  |
| `armoury` | Armoury | 8,000 | `military` | Plate armour assembly; equips garrison with advanced kit; hard-gated: city+ |  |

### 13.6 Special / Terrain States

| ID | Name | Notes | Status |
|----|------|-------|--------|
| `bandit_camp` | Bandit Camp | Enemy-held; spawns raider activity; destroyable by player or army |  |
| `derelict` | Derelict | Abandoned building; 0 output; partial salvage; slot freed after patience timeout (8.5) |  |

---

### 13.7 Quick-Reference: Resource Flow Map

```
INPUT                     BUILDING                 OUTPUT

arable_acres               Farm (L1-10)              grain
grain                      Grain Mill                flour_sack
flour_sack                 Bakehouse                 bread
grain                      Brewery                   ale
pasture_acres              Pasture                   wool / hides / meat / horses
livestock_head             Butcher                   meat + hides
hides                      Tannery                   leather
leather                    Tailor                    leather_vest / gloves
wool                       Weaver                    cloth_bolt
cloth_bolt                 Tailor                    cloth_garment
cloth_bolt + dye           Dye House                 fine_cloth
forest_acres               Lumber Camp               timber_log
timber_log                 Sawmill                   construction_lumber
wood                       Charcoal Kiln             coal (charcoal)
mining_slots               Iron Mine                 iron_ore
iron_ore + coal            Iron Smelter              iron_ingot
iron_ingot                 Smithy L1-3               iron_tools / spear
iron_ingot                 Smithy L3+                iron_sword / shield
iron_ingot + coal          Smithy L5+                steel_billet / chain_mail
steel_billet               Smithy L7+                plate_armor_components
plate_armor_components     Armoury (city+)           plate_armor
copper + tin               Bronzesmith               bronze
clay + coal                Brickmaker                bricks
gold                       Goldsmith                 coin / jewellery
sand + fuel                Glassworks                glass
```
