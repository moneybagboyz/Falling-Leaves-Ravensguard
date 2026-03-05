# Full Supply Chains — Resources, Products & Buildings

All four population classes are part of the economy.
Peasants extract. Artisans process. Merchants distribute. Nobles consume and govern.
Every good that exists should pass through at least one class before reaching its end use.

---

## 1. Population Classes at a Glance

| Class | Share | Labor Days | Economic Role | Consumes |
|-------|-------|-----------|--------------|----------|
| Peasant | 70% | 0.8 | Raw resource extraction | grain, fish, basic ale |
| Artisan | 15% | 0.6 | Processing & manufacturing | flour, cloth, ale, tools |
| Merchant | 10% | 0.2 | Trade, hospitality, distribution | grain, ale, fine goods |
| Noble | 5% | 0.0 | Governance & stability | jewelry, fine_garments, ale, spices, marble |

Labor days are a productivity multiplier — a peasant working 0.8 days produces more raw
output per head than an artisan working 0.6, but artisan output is higher value per unit.
Nobles produce nothing but their presence raises governance efficiency. High-complexity
buildings (cathedral, stone_walls, barracks) require sufficient noble headcount as a
prerequisite — not a tier number, but an organic population condition.

---

## 2. Resource Tiers

```
Tier 0 — Raw Resources        (peasant extraction)
Tier 1 — Intermediate Goods   (artisan first-stage processing)
Tier 2 — Finished Goods       (artisan second-stage or luxury processing)
Tier 3 — Luxury / Prestige    (noble demand, high-value trade)
```

---

## 3. Full Supply Chains

---

### 3a. Food & Sustenance Chain

**Classes involved:** Peasant (extraction) → Artisan (processing) → all classes (consumption)

```
PEASANT EXTRACTION
──────────────────
farm_plot           → grain (T0 resource)
fishery             → fish  (T0 resource, coastal/river terrain only)
pasture / hunting   → meat  (T0 resource, grassland/wilderness terrain)

ARTISAN PROCESSING (T1)
────────────────────────
grain_mill          grain × 2       → flour × 1         (food_value 2.0 — feeds twice as many
                                                          people as the same weight of raw grain)
brewery             grain × 2       → ale × 1           (happiness good, merchant trade)
                   (labor × 2)

ARTISAN PROCESSING (T2 — luxury food)
───────────────────────────────────────
(future: bakery / kitchen)
flour + spices      → fine_food                         (noble demand)

CONSUMPTION
────────────
grain / fish / meat → peasant + artisan daily sustenance  (food_value 1.0 per unit)
flour               → all classes (food_value 2.0 — 1 flour feeds as many as 2 raw grain)
                      A settlement that mills its entire grain surplus can sustain
                      double the population on the same farmland.
ale                 → all classes happiness (+morale), noble leisure
fine_food           → noble demand only
```

**Building chain:**

| Building | Class | Workers | Input | Output | Prerequisites |
|----------|-------|---------|-------|--------|---------------|
| `farm_plot` | Peasant | 4 | — | grain | fertile terrain |
| `fishery` | Peasant | 3 | — | fish | coastal or river terrain |
| `pasture` | Peasant | 3 | — | wool, hides, meat | grassland terrain |
| `granary` | Peasant | 1 | — | storage buffer | none |
| `grain_mill` | Artisan | 2 | grain | flour (food_value ×2) | grain in supply |
| `brewery` | Artisan | 2 | grain | ale | grain surplus above food threshold |
| `alehouse` | Merchant | 1 | ale | happiness, migration pull | ale in supply |
| `inn` | Merchant | 3 | ale + stored food | hospitality, migration pull | ale + food stockpile > 50 units |

**Bottlenecks:**
- Grain is split three ways: raw consumption, brewery, grain_mill — shortage hits all simultaneously
- The 2× flour satiety makes grain_mill the single highest-leverage building for population capacity;
  a T2 city without grain mills needs roughly double the farmland of one that mills everything
- Fish and meat provide dietary variety but are terrain-locked; inland settlements are grain-dependent
- Ale requires grain first — a famine immediately cuts ale supply and happiness
- Brewery and grain_mill compete for the same grain; the governor must balance food capacity vs. morale

---

### 3b. Metal & Weapons Chain

**Classes involved:** Peasant (mining) → Artisan (smelting + smithing) → all classes (tools, weapons)

```
PEASANT EXTRACTION
──────────────────
ore_mine            → iron ore, copper ore, tin ore,
                      gold ore, silver ore, coal
                      (output mix depends on ore deposit tags on tile)

ARTISAN PROCESSING — STAGE 1 (smelter)
────────────────────────────────────────
smelter             iron ore        → iron bars
                    copper ore      → copper bars
                    tin ore         → tin bars
                    gold ore        → gold bars
                    silver ore      → silver bars
                    (coal consumed as fuel for all)

ARTISAN PROCESSING — STAGE 2 (smithy/blacksmith)
───────────────────────────────────────────────────
— MATERIALS —
smithy              iron bars × 2
                    + coal × 1          → steel × 1
                    + labor × 4

smithy              iron bar × 1
                    + wood × 2          → tools × 2
                    + labor × 4

— WEAPONS —
smithy              iron bar × 1
                    + coal × 1          → iron_sword OR iron_axe OR iron_spear
                    + labor × 3

smithy              steel × 1           → steel_sword OR steel_axe           [requires: steel in supply]
                    + coal × 1
                    + labor × 5

— ARMOR — typed items per body region (see §8 Military Gear Reference) —
smithy              iron bars × 2
                    + coal × 1          → iron_mail          (torso + arms, layer 2)
                    + labor × 6

smithy              iron bar × 1
                    + coal × 1          → iron_helm          (skull, layer 1)
                    + labor × 3

smithy              iron bars × 2
                    + coal × 1          → iron_greaves       (lower_leg pair, layer 1)
                    + labor × 4

smithy              iron bar × 1
                    + coal × 1          → iron_gauntlets     (hand pair, layer 1)
                    + labor × 3

smithy              steel × 2           → steel_plate        (torso, layer 3 — heaviest) [requires: steel in supply]
                    + coal × 1
                    + labor × 8

smithy              steel × 1           → steel_helm         (skull, layer 2)            [requires: steel in supply]
                    + coal × 1
                    + labor × 4

    Quality tiers: average → fine → masterwork
    Masterwork items carry permanent stat bonuses and trade as luxury goods.
    Quality is determined by artisan skill at craft time — not upgradeable after.

ARTISAN PROCESSING — STAGE 2 (bronzesmith)
────────────────────────────────────────────
    NOTE: Bronze is the primary military metal before steel is accessible.
    Settlements without a steel supply outfit their garrison in bronze.
    Prerequisite: copper bars + tin bars in inventory (both required simultaneously).

bronzesmith         copper bars × 2
                    + tin bars × 1      → bronze × 2
                    + labor × 2

— WEAPONS —
bronzesmith         bronze × 1          → bronze_sword OR bronze_axe OR bronze_spear
                    + labor × 3

— ARMOR —
bronzesmith         bronze × 2          → bronze_mail        (torso + arms, layer 2)
                    + labor × 5

bronzesmith         bronze × 1          → bronze_helm        (skull, layer 1)
                    + labor × 3

ARTISAN PROCESSING — STAGE 3 (goldsmith)
──────────────────────────────────────────
    Prerequisite: gold ore in inventory + coal supply. No population gate.
goldsmith           gold ore × 1
                    + coal × 1          → jewelry × 1
                    + labor × 8

CONSUMPTION
────────────
tools           → all classes (farming efficiency, construction speed)
steel           → construction material, trade export, premium weapons/armor
bronze          → mid-tier armor/weapons (see §8), trade export
jewelry         → noble demand, high-value merchant trade export
gold ore        → treasury, currency backing (goldsmith input)

— GARRISON CONSUMPTION (see §8 for full slot model) —
iron_sword/axe/spear    → garrison weapon slot (raw iron + coal available)
steel_sword/axe         → garrison weapon slot (requires: steel in supply)
bronze_mail/helm        → garrison armor slots (requires: copper + tin in supply)
iron_mail/helm/greaves  → garrison armor slots (requires: raw iron + coal in supply)
steel_plate/helm        → garrison armor slots (requires: steel in supply)

Armor items are consumed when soldiers are wounded or equipment degrades.
Garrison posts a stockpile target; smithy/bronzesmith fulfill it from inventory.
```

**Building chain:**

| Building | Class | Workers | Input | Output | Prerequisites |
|----------|-------|---------|-------|--------|---------------|
| `ore_mine` | Peasant | 8 | — | raw ore (all types) | ore deposit tag on tile |
| `charcoal_camp` | Peasant | 2 | wood × 10 | coal × 4 | forest terrain + wood surplus |
| `smelter` | Artisan | 2 | iron + coal | steel | iron in inventory + coal supply |
| `smithy` | Artisan | 2–8 | iron + coal (or steel + coal) | tools, typed weapons & armor | raw iron + coal in supply |
| `bronzesmith` | Artisan | 2–5 | copper + tin | bronze, typed weapons & armor | copper + tin both in supply |
| `goldsmith` | Artisan | 4 | gold + coal | jewelry | gold ore + coal in supply |

Priority note: `bronzesmith` should be staffed equivalent to `smithy` at settlements
where steel is unavailable — bronze is the only local armor material in that case.

**Bottlenecks:**
- Coal is required at smelter AND smithy AND goldsmith simultaneously — the single most shared input in the chain
- A settlement with ore but no coal cannot run a smelter — it must trade ore upstream or import coal
- `charcoal_camp` provides a coal fallback on forest terrain; without it, no coal = no smelting and no armor production
- Smithy labor cost scales with gear type — steel_plate takes 8 workers vs. 3 for iron_helm; a garrison restock order can saturate all smithy slots
- Goldsmith has the highest labor cost per civilian unit (8 workers per jewelry) — only viable once gold ore is consistently in supply
- Bronze is the critical military material before steel is accessible; a settlement with no copper+tin access must import bronze or equip garrison with iron only
- Armor degradation in combat creates recurring demand — a smithy that only runs once at town founding will leave the garrison unequipped after sieges

---

### 3c. Lumber & Energy Chain

**Classes involved:** Peasant (extraction) → Peasant (charcoal) → Artisan (fuel consumer) → construction

```
PEASANT EXTRACTION
──────────────────
lumber_camp         → wood (T0 resource, forest terrain only)

PEASANT PROCESSING
──────────────────
charcoal_camp       wood × 10       → coal × 4
                    (simple conversion, no artisan needed)

DISTRIBUTION — WOOD USES
─────────────────────────
wood        → construction material (all building queues)
wood        → smithy input (tools recipe: iron bar + wood × 2)
wood        → heating fuel (cold climate consumption)
coal        → smelter fuel
coal        → smithy fuel
coal        → goldsmith fuel
coal        → brickmaker fuel (clay → bricks)

MERCHANT DISTRIBUTION
──────────────────────
wood / coal → market_stall, trade caravan export
```

**Building chain:**

| Building | Class | Workers | Input | Output | Prerequisites |
|----------|-------|---------|-------|--------|---------------|
| `lumber_camp` | Peasant | 4 | — | wood | forest terrain |
| `charcoal_camp` | Peasant | 2 | wood × 10 | coal × 4 | forest terrain + wood surplus |

**Bottlenecks:**
- Wood serves construction AND fuel AND smithy tools — a lumber-poor settlement is bottlenecked in all three
- Charcoal conversion is lossy (10 wood → 4 coal); heavy smelting/smithing settlements burn forests fast
- Cold-climate settlements consume wood as heating fuel on top of industrial use

---

### 3d. Textile & Leather Chain

**Classes involved:** Peasant (livestock/hunting) → Artisan (weaver/tannery/tailor) → Merchant (trade) → Noble (fine garments)

```
PEASANT EXTRACTION
──────────────────
pasture             → wool, hides, meat
hunting             → meat, hides, furs

ARTISAN PROCESSING — STAGE 1
──────────────────────────────
weaver              wool × 2        → cloth × 1
                    + labor × 2

weaver              cloth × 2       → gambeson × 1      (full body, layer 1 armor)
                    + labor × 3
    Gambeson is the base armor layer — worn under all heavier armor.
    Every soldier needs one before iron_mail or steel_plate can be equipped.

tannery             hides × 2       → leather × 1
                    + labor × 2

tannery             leather × 2     → leather_armor × 1  (torso, layer 2 — light)
                    + labor × 3

tannery             leather × 1     → leather_cap × 1    (skull, layer 1 — light)
                    + labor × 2

tannery             leather × 1     → leather_boots × 1  (foot pair, layer 1)
                    + labor × 2
    Leather armor is the primary body protection for T0 scouts and militia.
    Cheaper than smithy outputs, available without coal or smelting.

ARTISAN PROCESSING — STAGE 2
──────────────────────────────
tailor              cloth × 2       → fine_garments × 1
                    + labor × 4

(smithy)            leather + iron bars + coal  → armor (replaces leather layer with reinforced)
                                                   not currently implemented — see §8 for hybrid armor
                    + labor

MERCHANT / NOBLE CONSUMPTION
──────────────────────────────
cloth           → peasant / artisan basic clothing (upkeep demand)
gambeson        → garrison base armor layer (every soldier needs one)
leather         → leather_armor / leather_cap / leather_boots (militia, scouts)
leather_armor   → light armor for T0 garrison; supplements gambeson
fine_garments   → noble demand (luxury consumption)
                → high-value merchant export
furs            → noble demand (cold climate luxury)
                → merchant trade export
```

**Building chain:**

| Building | Class | Workers | Input | Output | Prerequisites |
|----------|-------|---------|-------|--------|---------------|
| `pasture` | Peasant | 3 | — | wool, hides, meat | grassland terrain |
| `weaver` | Artisan | 2 | wool → cloth; cloth → gambeson | cloth + base armor layer | wool in supply |
| `tannery` | Artisan | 2 | hides → leather; leather → armor | leather + light armor | hides in supply |
| `tailor` | Artisan | 3 | cloth | fine_garments | cloth surplus above clothing upkeep |

**Bottlenecks:**
- Hides and wool both come from pasture — a settlement with no grassland terrain is locked out of the entire chain
- fine_garments require two artisan stages (pasture → weaver → tailor), making them expensive to produce and valuable to trade
- Noble demand for fine_garments scales with noble headcount; settlements with large noble populations need enough tailors to prevent noble unrest

---

### 3e. Construction Materials Chain

**Classes involved:** Peasant (stone/clay extraction) → Artisan (brickmaker) → all (construction queue)

```
PEASANT EXTRACTION
──────────────────
ore_mine / quarry   → stone (mountain / hill terrain)
clay extraction     → clay (wetland / sedimentary terrain)
ore_mine            → coal (coal geology)

ARTISAN PROCESSING
──────────────────
brickmaker          clay × 3
                    + coal × 1     → bricks × 2
                    + labor × 3

CONSUMPTION
────────────
stone       → building construction (walls, towers, important structures)
bricks      → building construction (urban buildings, roads)
wood        → building construction (basic structures, T0–T1)
marble      → prestige construction (cathedral, palace — noble demand)
```

**Building chain:**

| Building | Class | Workers | Input | Output | Prerequisites |
|----------|-------|---------|-------|--------|---------------|
| `ore_mine` | Peasant | 8 | — | stone, coal | stone or ore deposit on tile |
| `clay_pit` | Peasant | 2 | — | clay | wetland or sedimentary terrain |
| `brickmaker` | Artisan | 3 | clay + coal | bricks | clay + coal both in supply |

---

### 3f. Luxury & Prestige Chain

**Classes involved:** Artisan (goldsmith, tailor) → Merchant (distribution) → Noble (consumption)

```
NOBLE DEMAND GOODS
───────────────────
jewelry         ← goldsmith (gold ore + coal)   [req: gold ore + coal]
fine_garments   ← tailor (cloth)                 [req: cloth surplus]
fine_food       ← future kitchen / bakery (flour + spices)
ale             ← brewery (grain)                [req: grain surplus] — all classes but nobles drink more
spices          ← trade import (no local production — merchant chain only)
furs            ← hunting (wilderness terrain) or trade import
marble          ← ore_mine (marble geology) — construction prestige material

NOBLE SUPPLY CHAIN
───────────────────
                          [Peasant]          [Artisan]         [Merchant]      [Noble]
Jewelry:    gold ore  →  ore_mine  →  goldsmith (gold + coal)  →  market  →  noble household
Garments:   pasture   →  wool      →  weaver → cloth       →  tailor     →  market  →  noble household
Ale:        farm_plot →  grain     →  brewery → ale        →  inn        →  noble household
Spices:     (import)  ─────────────────────────────────────── caravan     →  market  →  noble household

NOBLE ECONOMIC EFFECTS
───────────────────────
+  Governance bonus — reduces unrest, improves tax collection
+  Unlocks high-tier buildings (cathedral, palace, grand market)
+  Luxury demand drives inter-settlement trade (settlements specialize)
-  Zero labor contribution — pure overhead on food and goods supply
-  Luxury deficit → noble unrest → governance penalty → tax revenue drop
```

---

### 3g. Merchant Distribution Chain

Merchants do not extract or manufacture. They connect supply to demand.

```
MERCHANT BUILDINGS & FUNCTIONS
────────────────────────────────
alehouse (1w)           ale → peasant/artisan happiness
    prereq: ale in supply
                        migration pull (new workers arrive)

inn (3w)                ale + stored food → traveler lodging
    prereq: ale + food stockpile > 50 units
                        migration pull (skilled workers, merchants)
                        caravan rest stop (trade route efficiency)

market_stall (2w)       local goods exchange
    prereq: any trade surplus > 20 units above local consumption
                        price signals for governor decisions
                        clears surplus grain/wool/hides from peasant overproduction

market (formula)        price discovery for the whole settlement
    prereq: market_stall present + trade volume > 100 units/day
                        +25% commerce tax multiplier
                        enables import/export of goods not locally produceable:
                          spices (noble demand, no local source)
                          furs (cold luxury, terrain-limited)
                          fine goods from neighboring specialist settlements

MERCHANT CLASS ROLE IN SUPPLY CHAIN
──────────────────────────────────────
Without merchants:
  surplus grain rots in granaries
  nobels can't access spices or foreign furs
  artisan goods pile up with no price signal to guide governor retooling

With merchants:
  surplus goods reach demand
  import fills gaps (coal for a forest-free settlement, spices for nobles)
  inn draws skilled artisan migrants → accelerates workshop expansion
  market price signals tell governor to retool workshops toward scarcity
```

---

### 3h. Livestock & Horses

**Classes involved:** Peasant (pasture) → all classes (meat/wool/hides) + Garrison (cavalry) + Governor (draft assignment)

```
PEASANT EXTRACTION (pasture — placed building, tile-driven like farm_plot)
──────────────────────────────────────────────────────────────────────────
pasture             → wool      (fast output, same tick formula as other extraction)
pasture             → hides     (fast)
pasture             → meat      (fast)
pasture             → horse     (slow — ~1 per building per 30 days at full staff)

Pasture is a placed building: 1 per 3 grassland tiles, 4 workers per building.
Horses accumulate in settlement inventory and are assigned by the governor.

HORSE USES — THREE SEPARATE OUTCOMES
──────────────────────────────────────

1. CAVALRY (garrison)
   horse × 1 (stockpile) + weapon + armor → mounted soldier
   • 1 horse consumed per cavalryman lost in combat
   • Mounted units: +50% movement speed, bonus charge damage on first melee contact
   • Garrison posts a cavalry_target count alongside foot-soldier targets

2. DRAFT ANIMAL (farm boost)
   Governor assigns horses from inventory to farm_plots.
   Each assigned draft horse: +20% yield on that farm_plot (stacks per horse).
   Draft horses consume grain × 1 per horse per day from inventory (feeding cost).
   Horses can be reassigned back to stockpile at any time; yield bonus is lost immediately.

3. TRANSPORT (caravan speed)
   At caravan departure: if horses > 0 in inventory → caravan speed × 1.5.
   Horses are not consumed — only required to be present at departure.
   A settlement that allocates all horses to cavalry or draft loses the caravan bonus.

Priority conflicts are governor decisions. A small grassland settlement that fields
cavalry, farms with draft horses, AND runs trade caravans needs a large pasture base.
```

**Building:**

| Building | Class | Workers | Input | Output | Prerequisites |
|----------|-------|---------|-------|--------|--------------|
| `pasture` | Peasant | 4 | — | wool, hides, meat, horse (slow) | grassland terrain |

**Bottlenecks:**
- Horses are slow to produce (~1 per 30 days per building). Cavalry from a single pasture takes months.
- Draft horses eat grain. At scale (10 horses), feeding cost is meaningful and competes with food supply.
- Cavalry, draft, and caravan all pull from the same horse inventory — governor must prioritize.
- Settlements without grassland terrain cannot produce horses and must import for cavalry.

---

## 4. Cross-Class Dependency Map

```
                   ┌──────────────────────────────────────────────┐
                   │                  PEASANT                     │
                   │  farm_plot  ore_mine  lumber_camp  pasture   │
                   │  fishery   charcoal_camp  clay_pit  granary  │
                   └────┬──────────┬──────────────┬──────────────┘
                        │ grain    │ raw ore       │ wool/hides/wood/coal/clay/fish
                        ▼          ▼               ▼
                   ┌──────────────────────────────────────────────┐
                   │                  ARTISAN                     │
                   │  grain_mill  smelter  smithy  brewery        │
                   │  weaver  tannery  tailor  brickmaker         │
                   │  bronzesmith  goldsmith                      │
                   └────┬──────────┬──────────────┬──────────────┘
                        │ flour    │ steel/tools   │ ale/cloth/leather
                        │ weapons  │ jewelry       │ fine_garments/bricks
                        ▼          ▼               ▼
                   ┌──────────────────────────────────────────────┐
                   │                 MERCHANT                     │
                   │  alehouse  inn  market_stall  market         │
                   └────┬──────────────────────────┬─────────────┘
                        │ sold goods + price signal │ imports
                        ▼                           ▼
                   ┌──────────────────────────────────────────────┐
                   │                   NOBLE                      │
                   │  consumes: jewelry, fine_garments, ale,      │
                   │            fine_food, spices, furs           │
                   │  provides: governance, stability, building prereqs │
                   └──────────────────────────────────────────────┘
```

---

## 5. Building Master Reference

Buildings use two fundamentally different production models:

**Extraction buildings** (farm_plot, ore_mine, lumber_camp, charcoal_camp, clay_pit, fishery)
- Count set by territory tile composition at worldgen (1 building per N matching tiles).
- Each placed building provides `WORKERS_PER_BUILDING` laborer slots.
- Output formula: `workers_assigned × ACRES_WORKED_PER_LABORER × yield_per_acre / DAYS_PER_YEAR`
- *The building doesn't produce — the land does. The building is a worker cap.*
- A farm with 3 of 5 slots filled yields 60% of potential. More farms = more ceiling. More people = higher fill rate.
- **No buildings placed** (fresh hamlet, siege damage): survival fallback runs at 15% efficiency so the settlement doesn't instantly starve while waiting for worldgen or construction.

**Processing buildings** (grain_mill, smithy, smelter, weaver, tannery, brewery, etc.)
- Count set by artisan population headcount at worldgen.
- Each building has `max_workers` (artisan slots) and a recipe: `workers_per_cycle → inputs → outputs`.
- Output = `floor(min(workers / workers_per_cycle, input_stock / input_qty))` cycles per tick.
- *A smithy with no iron or steel idles completely. Supply chain starvation propagates upstream.*
- The smithy uses a priority recipe: tries steel first → raw iron (swords) → raw iron (tools fallback). No smelter required for iron weapons.

### Peasant Buildings (Extraction — tile-driven)

| Building | Workers/Building | Tiles Per | Daily Formula | Raw Output | Feeds Into |
|----------|-----------------|-----------|--------------|-----------|------------|
| `farm_plot` | **5** | 2 | `workers × 10 acres × 12 bu/acre / 360 × 0.8` | grain | grain_mill, brewery, consumption |
| `fishery` | **4** | 2 | `workers × 30 fish / 360` | fish | consumption |
| `ore_mine` | **4** | 3 | `workers × deposit_strength / 30` (per ore type) | all raw ores + stone | smelter, smithy, brickmaker |
| `lumber_camp` | **3** | 4 | `workers × 10 acres × 100 wood/acre / 360` | wood | charcoal_camp, smithy (tools), construction |
| `charcoal_camp` | **3** | 6 | cuts wood then converts 40% → coal | wood + coal | smelter, smithy, goldsmith, brickmaker |
| `clay_pit` | **3** | 2 | `workers × CLAY_YIELD / 360` (wetland) or `SALT_YIELD` (arid) | clay / peat / salt | brickmaker, preservation |
| `pasture` | **4** | 3 | `workers × PASTURE_YIELD / 360` (wool/hides/meat); horse: 1 per building per ~30 days | wool, hides, meat, horse | weaver, tannery, cavalry, draft |

> Worker counts match `EXTRACTION_WORKERS_PER_BUILDING` in `production_system_rewrite.gd`.

### Peasant Buildings (Storage — population-driven)

| Building | Workers | Prerequisites | Output | Function |
|----------|---------|---------------|--------|----------|
| `granary` | 1 | none | storage buffer | Famine prevention; scales with peasant population |

### Artisan Buildings (Workshop Pool — Governor-Assignable)

Each artisan building has `max_workers` slots and runs recipe cycles each tick.
Cycle output = `floor(min(workers / workers_per_cycle, input_stock / input_qty))`.

| Building | Max Workers | Cycle Cost | Input → Output | Notes |
|----------|-------------|-----------|----------------|-------|
| `smithy` | 6 | 3 artisans | ①`steel×2 + coal→steel_sword`, ②`iron×2 + coal→iron_sword`, ③`iron×2→tools` | Priority recipe: tries ① first, falls back; no smelter required for ② |
| `grain_mill` | 4 | 2 artisans | `grain×2 → flour×1` | flour food_value 2.0 — 1 flour feeds as many as 2 grain |
| `smelter` | 6 | 4 artisans | `iron×3 + coal×2 → steel×2` | Upgrades iron→steel; T0/T1 smithy runs without it on raw iron |
| `bronzesmith` | 6 | 2 artisans | `copper×2 + tin×1 → bronze×2` | Primary T1 military supplier (no smelter needed) |
| `weaver` | 4 | 2 artisans | `wool×2 → cloth×1` | Cloth is upstream of gambeson (mandatory combat layer) |
| `tannery` | 4 | 3 artisans | `hides×2 → leather×1` | Primary militia armor source |
| `brewery` | 4 | 2 artisans | `grain×2 → ale×1` | Competes with grain_mill for grain; governor priority matters |
| `brickmaker` | 4 | 3 artisans | `clay×3 + coal×1 → bricks×2` | Construction material chain |
| `toolmaker` | 4 | 4 artisans | `iron×1 + wood×2 → tools×2` | Frees smithy for weapons when toolmaker is present |
| `tailor` | 4 | 4 artisans | `cloth×2 → fine_garments×1` | Noble demand; requires cloth surplus |
| `goldsmith` | 4 | 8 artisans | `gold×1 + coal×1 → jewelry×1` | Highest labor cost per cycle; luxury export |

> Recipe constants defined in `PROCESSING_RECIPES` in `production_system_rewrite.gd`.

### Merchant Buildings (Trade & Hospitality)

| Building | Workers | Prerequisites | Function | Effect |
|----------|---------|---------------|----------|--------|
| `alehouse` | 1 | ale in supply | Ale → happiness | Migration pull, peasant morale |
| `inn` | 3 | ale + food stockpile > 50 units | Lodging + food | Migration pull, caravan rest, skilled worker attraction |
| `market_stall` | 2 | any trade surplus > 20 units | Goods exchange | Local price signals, surplus clearance |
| `market` | formula | market_stall present + trade volume > 100 units/day | Price discovery | +25% commerce tax, enables imports |

### Noble-Unlocked Buildings (Institutional)

| Building | Prerequisites | Function |
|----------|---------------|-----------|
| `cathedral` | nobility > 5 + stone > 100 + marble > 20 in supply | Max stability, noble loyalty |
| `stone_walls` | stone > 200 in supply | Defense multiplier |
| `barracks` | garrison > 10 soldiers | Garrison quality/capacity |
| `palace` | nobility > 20 + stone > 200 + marble > 50 + fine_garments in supply | Prestige, governance, noble consumption hub |

### Civic Infrastructure (Population Formula)

| Building | Formula | Function |
|----------|---------|----------|
| `well` | ceil(pop / 200) | Water access, disease prevention |
| `house` | ceil(pop / capacity) | Housing slots for population growth |

---

## 6. Good-to-Tier Reference

### Raw Goods (T0 — Peasant)

| Good | Source | Terrain |
|------|--------|---------|
| grain | farm_plot | fertile |
| fish | fishery | coastal/river |
| meat | pasture/hunting | grassland/wilderness |
| wool | pasture | grassland |
| hides | pasture/hunting | grassland/wilderness |
| horse | pasture | grassland |
| furs | hunting | wilderness |
| wood | lumber_camp | forest |
| iron ore | ore_mine | iron geology |
| copper ore | ore_mine | copper geology |
| tin ore | ore_mine | tin geology |
| gold ore | ore_mine | gold geology |
| silver ore | ore_mine | silver geology |
| coal | ore_mine / charcoal_camp | coal geology / forest |
| stone | ore_mine | mountain/hill |
| clay | clay_pit | wetland/sedimentary |
| peat | extraction | wetland |
| salt | extraction | arid/desert |
| spices | trade import only | — |
| marble | ore_mine | igneous geology |

### Intermediate Goods (T1 — Artisan Stage 1)

| Good | Recipe (per cycle) | Building | Workers/Cycle |
|------|-------------------|----------|---------------|
| flour | grain×2 → flour×1 **(food_value 2.0)** | grain_mill | 2 |
| ale | grain×2 → ale×1 | brewery | 2 |
| steel | iron×3 + coal×2 → steel×2 | smelter | 4 |
| bronze | copper×2 + tin×1 → bronze×2 | bronzesmith | 2 |
| coal (charcoal) | wood×10 → coal×4 | charcoal_camp (passive) | — |
| cloth | wool×2 → cloth×1 | weaver | 2 |
| leather | hides×2 → leather×1 | tannery | 3 |
| bricks | clay×3 + coal×1 → bricks×2 | brickmaker | 3 |

### Finished Goods (Artisan Stage 2)

The smithy uses a **priority recipe** — it produces the best output its current stock supports, one tier at a time.

| Priority | Smithy Input (per cycle, 3 artisans) | Output | Tier |
|----------|--------------------------------------|--------|------|
| 1st | steel×2 + coal×1 | steel_sword (exceptional) | T2 |
| 2nd | iron×2 + coal×1 | iron_sword (standard) | T1 |
| 3rd (fallback) | iron×2 | tools×1 | T0 |

The toolmaker offloads tools production so the smithy can focus on weapons:

| Good | Recipe (per cycle) | Building | Workers/Cycle |
|------|-------------------|----------|---------------|
| tools | iron×1 + wood×2 → tools×2 | toolmaker | 4 |
| gambeson | cloth×2 → gambeson×1 | weaver | 2 |
| leather_armor | leather×2 → leather_armor×1 | tannery | 3 |
| bronze_gear | *(see bronzesmith — raw copper+tin)* | bronzesmith | 2 |
| fine_garments | cloth×2 → fine_garments×1 | tailor | 4 |
| jewelry | gold×1 + coal→ jewelry×1 | goldsmith | 8 |

**Typed weapon and armor itemization** (smithy output routing by garrison demand — see Section 8d for full lists):

- `iron` smithy cycle → `iron_sword` **or** `iron_helm` **or** `iron_mail` (governor assigns target; no smelter required)
- `steel` smithy cycle → `steel_sword` **or** `steel_plate` **or** `steel_helm` (governor assigns target)
- `bronze` bronzesmith cycle → `bronze_sword` **or** `bronze_mail` **or** `bronze_helm`

### Luxury Goods (Noble Demand)

| Good | Recipe | Building | Prerequisites | Consumer |
|------|--------|----------|---------------|----------|
| jewelry | gold ore×1 + coal×1 → 1 jewelry | goldsmith | gold ore + coal in supply | noble |
| fine_garments | cloth × 2 | tailor | cloth surplus above upkeep | noble + merchant export |
| spices | import only | market | market_stall present + trade route | noble |
| furs | hunting or import | — | wilderness terrain or trade route | noble (cold climate) |
| marble | ore_mine | — | igneous geology on tile | noble construction |

---

## 7. Inter-Settlement Trade Dependencies

Not every settlement can produce every good. This is intentional — it creates
trade value between settlements and makes merchants economically necessary.

| Good | Likely Seller | Likely Buyer |
|------|--------------|-------------|
| coal | mining settlement (coal geology) or forested settlement | any smithy/smelter settlement |
| steel | mining + smelter settlement | any smithy settlement (unlocks steel tier weapons) |
| iron ore | mining settlement | landlocked settlements (raw iron for T0/T1 smithy) |
| wood | forested settlement | ore-rich but treeless settlement |
| grain | agrarian settlement | mining boomtown |
| ale | brewing settlement | military town, noble household |
| spices | trade hub (import) | noble households everywhere |
| fine_garments | crafting settlement (tailor) | noble households, trade hubs |
| jewelry | goldsmith settlement (gold + coal access) | noble households, prestige trade |
| tools | smithy settlement | farming settlements, construction boom |
| leather | pastoral settlement | smithy, tailors, armor production |

The **merchant class** at each settlement decides which of these to stock via
`market_stall` and `market` buildings. The **governor** signals scarcity by
watching price spikes in the market and retools workshops accordingly.

---

## 8. Military Gear & Armor Reference

This section defines the body coverage model, material stack, and typed gear items
that the combat system resolves. Supply chain produces items; combat consumes them.

---

### 8a. Body Coverage Slots

Every soldier tracks equipment independently across these slots:

```
Slot          Covers
────────────────────────────────────
skull         head — critical hit zone, stun/knockdown on unarmored
face          eyes/jaw — exposure causes bleed, morale breaks
neck          artery zone — unarmored = lethal on pierce
upper_body    chest/shoulders — largest zone, most armor coverage
lower_body    abdomen/hips
upper_arm     L + R independently
lower_arm     L + R independently  (losing arm = weapon drop)
hand          L + R independently
upper_leg     L + R independently
lower_leg     L + R independently  (losing leg = falls, crawls)
foot          L + R independently  (losing foot = speed ×0.1)
```

---

### 8b. Armor Layer Stack (per slot)

Slots support up to 3 layers. Strike resolution works innermost-out:
a blow that penetrates layer 3 continues into layer 2, then layer 1, then flesh.

```
Layer   Type              Material            Block Type
──────────────────────────────────────────────────────────
  1     Base (padding)    gambeson (cloth)    Absorbs blunt only. Required under mail.
  2     Medium armor      leather_armor       Slash reduction. Low pierce resist.
                          bronze_mail         Slash + pierce. Heavy vs. blunt.
                          iron_mail           Slash + pierce. Better edge than bronze.
  3     Heavy armor       steel_plate         Best slash/pierce. Weaker vs. blunt mauls.
                          iron (no layer 3)   Iron tops out at layer 2.
```

A soldier wearing gambeson + iron_mail has 2 layers on upper_body.
A soldier wearing gambeson + iron_mail + steel_plate has 3 (full protection).
A soldier with no gambeson and iron_mail on top suffers full blunt damage through the mail
— gambeson is NOT optional.

---

### 8c. Weapon vs. Armor Resolution

```
Damage Type     Blocked By                  Passes Through
──────────────────────────────────────────────────────────────────
Slash           leather > bronze_mail > iron_mail > steel_plate    gambeson (none)
Pierce          iron_mail > bronze_mail > steel_plate              leather (partial)
Blunt           gambeson (partial) > steel_plate (partial)         mail (passes through — why
                                                                     plate + gambeson needed)
Cut/Bite        leather ≥ bronze_mail                              iron_mail (minimal)
```

Material hardness determines penetration threshold:
`cloth < leather < bronze < iron < steel`

---

### 8d. Typed Gear Reference

#### Weapons

| Item | Material | Damage Type | Recipe | Source |
|------|----------|-------------|--------|--------|
| `iron_sword` | iron | slash | iron×2 + coal | smithy |
| `iron_axe` | iron | slash/chop | iron×2 + coal | smithy |
| `iron_spear` | iron | pierce | iron×2 + coal | smithy |
| `bronze_sword` | bronze | slash | bronze × 1 | bronzesmith |
| `bronze_axe` | bronze | slash/chop | bronze × 1 | bronzesmith |
| `bronze_spear` | bronze | pierce | bronze × 1 | bronzesmith |
| `steel_sword` | steel | slash | steel + coal | smithy (req: steel in supply) |
| `steel_axe` | steel | slash/chop | steel + coal | smithy (req: steel in supply) |

#### Armor — Torso

| Item | Layer | Slots | Material | Recipe | Source |
|------|-------|-------|----------|--------|--------|
| `gambeson` | 1 | upper_body + lower_body + upper_arm | cloth × 2 | weaver |
| `leather_armor` | 2 | upper_body | leather × 2 | tannery |
| `bronze_mail` | 2 | upper_body + upper_arm + lower_arm | bronze × 2 | bronzesmith |
| `iron_mail` | 2 | upper_body + upper_arm + lower_arm | iron×2 + coal | smithy |
| `steel_plate` | 3 | upper_body + lower_body | steel × 2 + coal | smithy (req: steel in supply) |

#### Armor — Head

| Item | Layer | Material | Recipe | Source |
|------|-------|----------|--------|--------|
| `leather_cap` | 1 | leather × 1 | tannery |
| `bronze_helm` | 1 | bronze × 1 | bronzesmith |
| `iron_helm` | 1 | iron×2 + coal | smithy |
| `steel_helm` | 2 | steel + coal | smithy (req: steel in supply) |

#### Armor — Limbs

| Item | Layer | Slots | Recipe | Source |
|------|-------|-------|--------|--------|
| `leather_boots` | 1 | foot L+R | leather × 1 | tannery |
| `iron_greaves` | 1 | lower_leg L+R | iron×2 + coal | smithy |
| `iron_gauntlets` | 1 | hand L+R | iron×2 + coal | smithy |

---

### 8e. Garrison Stockpile Model

Garrison posts a target equipment load per soldier tier:

```
Light (iron only):    gambeson + leather_armor + leather_cap + iron_sword
Standard:             gambeson + iron_mail + iron_helm + iron_greaves + iron_sword
Heavy (steel req):    gambeson + iron_mail + steel_plate + steel_helm + steel_sword
Elite (steel req):    gambeson + iron_mail + steel_plate + steel_helm
                      + iron_gauntlets + iron_greaves + steel_sword
```

When stockpile < target × garrison_size, smithy/bronzesmith/weaver/tannery receive
a garrison order. Orders have higher priority than civilian commercial production.
Items degrade on wound — a pierced iron_mail gains `damaged` flag, counts as
leather_armor until repaired (repair = 2 iron + 1 coal at smithy).

---

### 8f. Artisan Skill & Quality Tiers

Every crafted gear item has a quality:

```
Quality       Combat Effect               Trade Value
────────────────────────────────────────────────────────
average       Base stats                  Base price
fine          +10% block threshold        ×1.5 price
exceptional   +20% block, +5% durability  ×2.5 price
masterwork    +35% block, +15% durability ×5 price — luxury trade good for nobles
```

Quality is determined at craft time by the artisan's skill level (0–100).
A settlement with veteran smiths produces better armor than one with new recruits.
Masterwork items do not degrade — they are never consumed, only lost in battle.

---

## 9. Governor Decision Reference

What the governor should watch and how to respond:

| Signal | Meaning | Response |
|--------|---------|----------|
| Grain price rising | Food shortage OR mill underweight | Increase `grain_mill` share first — each mill doubles effective food output; only reduce `brewery` if ale is also surplus |
| Ale price rising | Merchant income opportunity | Increase `brewery` from grain surplus |
| Steel / tools price rising | Military or construction demand | Increase `smithy` share |
| Steel / iron shortage | Smithy starved; mine underweight or no charcoal | Increase `ore_mine`; check `charcoal_camp` for coal supply |
| Fine_garments price rising | Noble demand unmet | Increase `weaver` + `tailor` share |
| Jewelry price rising | Noble unrest risk | Increase `goldsmith` share (requires gold ore + coal in supply) |
| Migration stalling | No hospitality pull | Build `alehouse` (req: ale) or `inn` (req: ale + food stockpile) |
| Construction queue stalled | Stone/brick shortage | Expand `ore_mine` or `brickmaker` |
| Happiness falling | Ale shortage or no lodging | Check `brewery` output and `alehouse`/`inn` count |
| Noble unrest | Luxury deficit | Prioritize jewelry, fine_garments, imported spices |

---

## 10. Civic Center Progression

Every settlement has exactly one civic center building. It upgrades in place and is never demolished. It provides passive happiness and tax rate bonuses — no building unlocks are gated behind it.

### 10a. Tier Table

| Tier | Building ID | Happiness Bonus | Tax Rate Bonus | Upgrade Cost |
|:----:|-------------|:---------------:|:--------------:|-------------|
| 0 | `hut` | — | — | *(starting state)* |
| 1 | `longhouse` | +1 | +1% | wood×30 |
| 2 | `village_hall` | +2 | +2% | wood×80 + stone×20 |
| 3 | `town_hall` | +3 | +4% | wood×100 + stone×80 |
| 4 | `guildhall` | +4 | +6% | stone×150 + bricks×80 |
| 5 | `manor_house` | +5 | +8% | stone×200 + bricks×120 + iron×30 |
| 6 | `keep` | +6 | +10% | stone×300 + bricks×200 + iron×60 |
| 7 | `fortress` | +7 | +13% | stone×500 + bricks×300 + iron×100 + steel×20 |
| 8 | `castle` | +8 | +16% | stone×600 + bricks×400 + iron×150 + steel×50 |
| 9 | `palace` | +10 | +20% | stone×800 + marble×50 + steel×100 |

**Happiness bonus** is added flat to the settlement’s happiness score each tick. Stacks with ale supply, housing quality, and noble presence.

**Tax rate bonus** is added on top of the base tax rate. A `town_hall` settlement with a 10% governor tax collects 10% + 4% = 14% effective.

### 10b. Upgrade Triggers

Upgrade is initiated by the governor when:
1. All required materials are in the settlement inventory.
2. No active siege or famine.

Upgrade consumes the materials immediately and swaps the building ID. Construction takes a configurable number of days; during construction the happiness bonus drops to 0 (no disruption penalty, just no benefit yet).

### 10c. Starting State

- **Hamlet / outpost:** spawns at `hut` (tier 0).
- **Player-founded settlement:** starts at `hut`; upgrades are the player’s first infrastructure decision.
- **Conquered settlement:** retains its current tier. Siege damage can downgrade the civic center (e.g. `castle` → `keep`) according to siege severity.

### 10d. Governor Decision Notes

| Signal | Meaning | Response |
|--------|---------|----------|
| Happiness stagnant despite food/ale surplus | Civic tier too low for population expectations | Gather upgrade materials; queue civic center upgrade |
| Tax income low relative to population | Civic tier below potential | Upgrading civic center has the highest tax ROI per resource spent |
| Siege ended, civic tier downgraded | Conquest damage | Prioritize civic rebuild before garrison restock — happiness drop compounds morale loss |
