# Items, goods, mass, value, quality and spoilage

Plan refs: §2.10 (items = shape x materials x quality x condition; weight = volume x density; containers; wear and rot),
§4A.1 (items: value = kg x price + labour; one 5-tier quality scale; fresh fish/meat rot in about 3 days), §3a (spoilage
5%/season barn or house, 1% granary), §5 Phase 2 (item mass, value, spoilage and containers; ~120 materials and items).
Replaces legacy `ItemData.gd`, `EquipmentSystem.gd` (crashing `create_item`), the four item-price formulas and three quality
scales.

## 1. Purpose
Two representations of "stuff", chosen by how much the game needs to know about each unit:
- **Goods (commodities/counted stock):** bulk kg/litres or interchangeable pieces held in `StockTable` rows `(owner, good, tier) -> quantity`. Grain, flour, ale, planks, bread, bars, bolts of cloth, nails, common tools. Millions of units cost a
  few hundred KB.
- **Item instances:** unique things with components, condition, wear, stains, contents (a particular sword, a barrel with ale
  in it, the hero's mail). Created only when something becomes individual (crafted to order, owned by the hero or a named
  NPC, picked up in the live tier) and folded back into counted stock when sold to a shop.
Plus the rules that give them mass, volume, value, quality, wear and rot.

## 2. Data

### 2.1 `GoodDef` (`content/base/economy/goods.jsonc`; ~120 goods+materials in Phase 2)
```jsonc
{ "type": "good", "id": "flour", "name": "Flour", "class": "staple", "state": "powder",
  "unit": "kg",                       // kg | litre | piece
  "piece_mass_g": 0,                  // for unit piece
  "litre_mass_g": 0,                  // for unit litre (ale 1010, milk 1030)
  "bulk_litres_per_kg": 1.6,          // stowage volume per kg (carts, containers)
  "base_price_mites_per_kg": 56,      // per piece if unit is piece (price solver output; tools/econ)
  "food_measures_per_kg": 0.95,
  "spoil": { "class": "flour" },     // row of spoilage.jsonc
  "target_cover_days": 14, "tradable": true, "material": null, "tags": ["food", "staple"] }
```
Counted pieces reference a `shape` (existing `weapon_shape`, `armour_shape`) or a bare `piece_mass_g`.
Runtime ids: dense `ushort` by ordinal string order (overview 2.2). `tier` is `byte` 0..4.

### 2.2 `StockTable` (`Sim/Economy/Stock.cs`)
Per owner (Household, Business, Caravan, Settlement book lots, Treasury, Parish, Mill...) a small sorted vector of `(GoodId ushort, Tier byte, long Qty)`. Qty unit: grams for kg/litre goods (litres convert via `litre_mass_g`), pieces for counted
goods.
```csharp
bool Take(owner, good, tier, qty)                  // false if not enough, never negative
void Add(owner, good, tier, qty)
bool Move(from, to, good, tier, qty)               // conserves
void Create(owner, good, tier, qty, CreateReason)  // Harvest, Mined, Cut, Caught, Hunted, Brewed(output), Crafted, Seed(start), Import(continent)
void Destroy(owner, good, tier, qty, DestroyReason)// Eaten, Burned, Spoiled, Worn, Sown, Consumed(input), Lost, Export(continent)
```
Every `Create/Destroy` increments per-reason counters (`MassLedger`) used by the soak audit; no code may change `Qty`
otherwise (`NoDirectStockWrites` grep test).

### 2.3 `ItemTable` (instances; struct of arrays)
`Id (IdKind.Item, region bits)`, `Shape` (ushort), `Quality` (0..4), `ConditionPermille` (0..1000), `Wear` (ushort, 0..65535
of durability clock), `Flags` (stained, burning, wet, repaired), `Owner` (EntityId, person/business/site/container), `Parent`
(container item or -1), `ComponentStart/Count` into `ComponentTable (Material ushort, MassG int, ConditionPermille short)`,
`ContentsGood/ContentsQty` for containers. Items in containers form a tree; contents are bulk stock inside the container
(e.g. 40 L ale in a barrel is `(ale, 40 L)`, not 40 instances).
Instances exist for: the hero's and named NPCs' possessions; anything on the ground in a live bubble; crafted-to-order and
ordered commissions; stolen goods (flag). Shops hold counted stock; `Materialise(good, tier)` creates an instance at
**Common/new** unless stock rows carry a `Variant` (not in Phase 2); `Dematerialise(item)` folds an undamaged instance back
into stock (damaged: sells for value only, destroyed `Consumed`).

### 2.4 Containers (`container.jsonc`)
`ContainerDef`: `volume_l`, `empty_mass_g`, `spoil_class` (`open`, `house`, `barn`, `sealed`, `granary`, `cellar`), `lid`
(bool), `hold` kinds. Examples: sack 60 L / 400 g (`house`), barrel 200 L / 18 kg (`sealed`), chest 150 L, cart bed 1,000 L,
granary bin per lot blueprint. Settlement lots carry `storage_volume_l` by `LotUse` (Barn 60,000, Granary 80,000, Cottage
3,000, Longhouse 6,000, Burgage 8,000, Manor 20,000); only Granary/Barn capacity is enforced in Phase 2 (a full granary
refuses purchases; spill rots 5%/day).

## 3. Rules

### 3.1 Mass and volume
`mass = volume x density` (shapes: `head_volume_cm3 x material density_kg_m3 / 1000` per component, haft likewise; floor to
whole grams per component). Bulk goods mass is the quantity. Stowage: `litres = mass_kg x bulk_litres_per_kg` (grain 1.33,
flour 1.6, firewood 2.5 stacked, wool 3.0 baled, iron 0.13, ale 1.0 + vessel). Carrying: person `25 kg` carried at walking
speed (strength scaling in Phase 3), handcart 150 kg, packhorse 100 kg, cart 500 kg / 1,000 L, ox wagon 1,000 kg / 2,000 L
(caravans spec). Capacity is `min(mass limit, volume limit / bulk_litres_per_kg)`.

### 3.2 Value
- **Goods:** `Ref` = `base_price_mites_per_kg` (markets spec 3.5). Base prices come from `tools/econ/PriceSolver`: for each
  recipe `price_out = (sum(input_kg x price_in) + 1.45 x worker_hours x wage_h(skill class)) / out_kg` (the 45% markup on
  labour covers tools, rent and the owner's profit; it is applied to value added, not to inputs, so middlemen do not earn on
  others' work), solved in dependency order from raw prices (extraction/farming/market). The solver output is checked in;
  test `PriceSolverMatchesJson`.
- **Instances (plan §4A.1):** `value = (sum over components mass_kg x material.value_per_kg x 100 + labour_hours x wage_h x (1 + skill/200)) x quality_mult x condition_mult` (mites; `value_per_kg` in the material file is crowns/kg).
  `quality_mult` (Crude, Poor, Common, Fine, Masterwork) = 0.5, 0.8, 1.0, 1.6, 3.0; `condition_mult = 0.3 + 0.7 x ConditionPermille/1000`; stains subtract 10% each until washed. Wage classes: unskilled 15 mites/h (labourer 150/day / 10 h),
skilled 35 (craftsman 350/day), master 50.
- **One quality scale** (replaces three): 0 Crude, 1 Poor, 2 Common, 3 Fine, 4 Masterwork. Combat/armour stats multipliers
  are specified by the combat spec; this file owns the names, thresholds and value multipliers.

### 3.3 Quality from skill (stream 0x223, key = crafter id, draw per output)
`score = skill + Range(-20, 21) + tool_bonus` where `tool_bonus` is -10..+10 from the best required tool quality vs the
recipe's `tool_min`; thresholds: `< 15` Crude, `15-39` Poor, `40-69` Common, `70-89` Fine, `>= 90` Masterwork. Batches of
counted goods get one tier per batch (the batch's lowest-skill worker's roll).

### 3.4 Wear and condition
Durability clock: each instance loses `wear += use_events x wear_per_use` (combat/work events in later phases); in Phase 2
only the **household durables** (clothes, shoes, tools, `tools_common`) use the probabilistic whole-piece failure of the
households spec (daily `p = 1/lifetime_days`, historical days: clothes 1,095 / 730 / 365 by class, shoes 365, tools 900).
Tools in business use wear `tool_wear_ppm_per_batch` of the recipe (production spec) with the same piece-failure rule.
Condition affects value (3.2) and rusting of iron below.

### 3.5 Spoilage (daily, per stock row; H4 slice of owner)
`spoilage.jsonc`: rows `class -> { open, house, barn, sealed, granary, cellar }` in **ppm of quantity per day**, plus
`temp_sensitive: bool`.
```
loss_g   = Qty x ppm x temp_mult / 1e6 / 1000       (temp_mult permille: T < 0: 500; 0..10: 800; 10..20: 1000; > 20: 1500, perishables only)
actual   = floor(loss_g) + (frac(loss_g) > u ? 1 : 0)   u = DetRng.NextFixed01(seed, hourKey, ownerId, 0x222, goodId)   (stochastic rounding keeps the expectation exact)
```
| Class | house/open (ppm/day) | granary / sealed / cellar | Note |
|---|---|---|---|
| grain, pulses | 3,414 (5%/season) | 670 (1%/season) | plan §3a |
| flour | 7,000 | 1,500 | |
| bread | 250,000 | n/a | stale in ~3 days (sold same day) |
| fish_fresh, meat_fresh | 300,000 | 150,000 (cellar) | rot ~3 days; x0.5 below 0 C |
| fish_salted/smoked, meat_salted | 8,000 | 3,000 | the point of salt |
| ale | 150,000 | 30,000 (sealed barrel) | |
| milk | 400,000 | n/a | |
| cheese | 5,000 | 1,500 | |
| vegetables | 30,000 | 8,000 (cellar) | |
| hides raw / wool | 50,000 / 1,000 | 20,000 / 300 | |
| leather, cloth | 500 | 200 | moth/damp |
| iron, steel (rust) | 400 (damp 1,500) | 100 | wiped by oiled/cellar flag in Phase 5 |
| firewood, charcoal, planks | 500, 100, 100 | | roof leaks x10 (weather) |
| straw/thatch | 2,000 | | |
| stone, brick, metals other, coin | 0 | | |
Roof damage (weather spec §3c, Phase 5 buildings): `leak_mult x10` on stock stored in that lot.
Spoilage is a sink (`Destroy(Spoiled)`); a **spoiled-goods chronicle line** is written only for a single owner losing >= 25%
of a staple in one season (Cause `Weather` if the lot's roof leaked, else MarketDrift-free `Other`).

### 3.6 Which container class a stock row is in
Owner type decides default: household -> `house`; business -> `house` (or `open` for stalls); lord/parish/merchant granary
lots -> `granary`; caravan -> `open` (x1.5 in rain); barrels/jars for ale/salted goods -> `sealed`. A household can move rows
to `cellar` when its dwelling blueprint has a cellar level (Phase 1 z = -1) for vegetables/cheese/ale. Class is per `(owner, good)` row, set by recipes/rules, not per unit.

## 4. Tick placement
Spoilage in the owner's **H4 slice**; granary/book lots in **H5 hour 0**; caravans in **H1** (per hour = daily ppm/24 rounded
stochastically); materialise/dematerialise on command points; price solver at content build (not runtime); `Compact()` of the
item table at save.

## 5. Determinism
Stock rows sorted by `(good, tier)`; iteration ascending owner id then row; stochastic rounding key `(seed, hourKey, ownerId, 0x222, goodId)`; quality rolls `(…, crafterId, 0x223, outputSerial)`; `Materialise` allocates item serials from the owner's
region allocator in command order.

## 6. Interactions
Markets (goods price/target cover), households (basket goods, wear), production (recipes consume/produce goods; mass check),
farming (grain spoilage, straw), caravans (capacity, spoilage in transit), combat/crafting later phases (shapes, components),
taxes (tithe/rent in kind), LODBridge (items carried by promoted people are instances; abstract inventories are stock rows).

## 7. Edge cases
- Quantity underflow: `Take` is all-or-nothing; partial availability handled by callers (`TakeUpTo`).
- Tiny stocks: stochastic rounding can lose 1 g from 1 g stock with probability `ppm/1e6` - fine.
- Items with no `base_price`: rejected at load; counted goods derived from shapes use the instance formula at load to produce
  their base (`value` at Common/new) until the price solver data exists.
- Mixed tiers in one row: bulk food has a single tier (2); weapons/armour/tools carry tiers 0-4 in stock; prices scale by
  `quality_mult`.
- Material file `value_per_kg` calibration: the existing silver `120.0` (crowns/kg) contradicts the coin parity of 1 crown =
  1.3 g silver (769.2 crowns/kg); set silver 769.2, gold 9,615 (ratio 12.5 kept); iron 2.0, steel 10.0 stay placeholders
  until the price solver replaces them (solver gives bar iron ~5.4, steel ~8.7 crowns/kg).


## Decisions made in this spec (collected in 00-overview.md section 10.4)
- **I1** Decision: Quality scale: Crude/Poor/Common/Fine/Masterwork, thresholds 15/40/70/90, value x0.5/0.8/1.0/1.6/3.0. Reason: plan: one five-tier scale.
- **I2** Decision: Goods are counted stock; item instances exist only when individuality matters. Reason: keeps millions of units cheap (plan §2.1a).
- **I3** Decision: Spoilage is ppm per day with stochastic rounding. Reason: exact expectation, deterministic, no per-unit clocks.
- **I4** Decision: Condition multiplier on value is `0.3 + 0.7 x condition`. Reason: worn goods keep some value.
- **I5** Decision: Stowage is modelled as litres per kg; carts are limited by mass and volume. Reason: plan: capacity in kg (not crowns); bulky goods need volume.

## 8. Golden tests
| id | Case | Expected |
|---|---|---|
| G-I1 | Arming sword mass (`head 180 cm3 steel 7850 kg/m3`, `haft 60 cm3 ash 680 kg/m3`) | head 1,413 g + haft 40 g (40.8 floored) = 1,453 g |
| G-I2 | Value at Common/new, steel 10 crowns/kg, ash 0.3, 24 h skilled labour, skill 60 | materials `1.413 x 1,000 + 0.0408 x 30` = 1,413 + 1 mites; labour `24 x 35 x 1.3` = 1,092; total **2,506**; Fine x1.6 = 4,010; Crude x0.5 = 1,253; Common at 70% condition x 0.79 = 1,980 |
| G-I3 | Quality: skill 60, roll -20, tool 0 -> 40; skill 60, roll +20, tool +10 -> 90; skill 10, roll -20 -> -10 | Common; Masterwork; Crude |
| G-I4 | Fresh fish 100,000 g, 3 days at 300,000 ppm, T 12 C (x1000) | 70,000 -> 49,000 -> **34,300 g**; at T < 0 (x500): 85,000 -> 72,250 -> 61,412 (+-1) |
| G-I5 | Grain 1,000,000 g, 15 days house 3,414 ppm vs granary 670 | house 950,003 g (+-100); granary 990,004 (+-100) (matches farming G-F10) |
| G-I6 | Cart (500 kg / 1,000 L) loads | grain 500 kg (667 L); firewood 400 kg (2.5 L/kg); baled wool 333 kg (3 L/kg); iron bars 500 kg (mass-limited); bread: volume 1.5 L/kg -> 500 kg |
| G-I7 | Stochastic rounding: 1 g stock, 3,414 ppm, 1,000,000 independent keys | expected loss 3,414 +- 100 g; with one key the result is stable across runs |
| G-I8 | `Move`/`Take` property test: random 100,000 operations over 200 owners | total grams per good constant; no negative; `Create-Destroy` counters equal net change |
| G-I9 | Price solver: charcoal `(100 kg logs x 1.5 + 1.45 x 12 h x 15) / 20 kg` | **20.55 mites/kg**; bloom `(40 x 1.5 + 60 x 20.55 + 1.45 x 10 x 35) / 6` = 300.08; bar iron 538.75; steel 869.8; `PriceSolverMatchesJson` tolerance 1% |
| G-I10 | Materialise then Dematerialise a sword | stock -1, item row created Common/new (cond 1000), stock +1 after; item row freed; id serial not reused |
| G-I11 | Coin parity | `silver value_per_kg 769.2 crowns/kg x 1.3 g` = 1.0000 crown (+-0.1%) |
| G-I12 | Container volume: 200 L barrel holds 200 L ale = 202 kg + 18 kg barrel | 220 kg total mass; barrel spoil class `sealed` (30,000 ppm/day) |

## 9. Soak invariants
1. Mass audit per good over any year: `sum(Create) - sum(Destroy) = stock delta` exact; no good below zero.
2. Spoilage share of produced grain over a normal year: 3-8% (house) and < 2% for granary-held fraction; fresh fish/meat
   spoilage < 15% of catch (they sell or are preserved).
3. No unit of any good has `base_price` outside [0.05, 5,000,000] mites per kg/piece; price solver margin per recipe 5-25%
   (test `RecipeMarginBand`).
4. Item table size after 100 years < 50,000 live instances in a region (instances only for owned/ground items).

## Notes

- Bulk goods are defined in `content/base/goods/goods.jsonc`; spoilage rates per store kind are in `spoilage.jsonc` and applied with deterministic integer arithmetic (mass-conserving via `MassLedger`).
