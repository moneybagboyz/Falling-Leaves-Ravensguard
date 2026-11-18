# Production: businesses, workstations, recipes, workers and wages

Plan refs: §4A.1 (workers and workplaces; businesses = buildings with workstations, an owner and a cash box; recipe = inputs + worker-hours + tool wear -> output; grounded rates; production chains; wages; dividends), §2.1a (records in ID-keyed tables, content in JSON), §5 Phase 2 (workers with employers and workplaces; abstract business recipes; ~40 recipes). Replaces legacy `ProductionSystem.gd` (labour-pool heuristics, level-multiplied output, tiles producing without workers).

## 1. Purpose
Every transformation of goods happens in a **business**: a place (a settlement lot or site) with workstations, an owner, employees and a cash account. Output equals what recipes allow from inputs actually consumed and hours actually worked. No free output, no building-level multipliers, no goods from nowhere except explicitly listed faucets (farming, extraction).

## 2. Data

### 2.1 `BusinessTable` (struct of arrays; ~700 per region)
`Id` (`IdKind.Business`, region bits), `Region`, `Settlement`, `Lot` (Phase 1 lot index or -1 for sites), `Def` (ushort -> `BusinessDef`), `Owner` (person index), `OwnerHousehold`, `Account` (cash box), `StockOwner` (= business id), `StationCount` (= Phase 1 building "level" becomes this number; never an output multiplier), `Stations[]` (see below), `Staff` (up to 12 person indices, ascending), `OpenSlots` (byte), `WageOfferMites` (short per slot kind), `ArrearsMites`, `Cash15dIn/Out`, `LossSeasons` (byte), `State` (`Open, Closed`).
`Station`: `Kind` (ushort), `RecipeId` (ushort or none), `ProgressPpm` (int), `BusyUntilHour` (long, for duration recipes), `Workers` (<= `max_workers`, indices), `HasTool` (bool).
Created at world start from Phase 1 data by `BusinessBuilder`: one business per distinct `(WorkSettlement, WorkLot)` of persons whose `Occupation` is a trade (`OccupationInfo.IsTrade`) or a producer (Smith, Miller, Baker, Brewer, Butcher, Carpenter, Mason, Weaver, Tanner, Cobbler, Fisher, Woodcutter, Miner, CharcoalBurner; Spinner = put-out worker of a Weaver; Merchant = trade business (market spec)); owner = highest-skill person with no employer; others become `Staff`. Farms are **not** businesses: they are household holdings (farming spec). Owners of towns' mills, taverns and smithies follow `LotKeeper` from Phase 1.

### 2.2 Content
`content/base/economy/recipes.jsonc` (schema sketch; every rate has a unit suffix):
```jsonc
{ "type": "recipe", "id": "bake_bread", "station": "oven", "skill": "Baking", "worker_hours": 6, "max_workers": 2,
  "inputs":  [ { "good": "flour", "kg": 100 }, { "good": "firewood", "kg": 20 } ],
  "outputs": [ { "good": "bread", "kg": 130 } ], "adds_kg": 30,            // water/yeast the mass check allows
  "tool": { "quality": "baking", "min": 1, "wear_ppm_per_batch": 300 },
  "duration_days": 0, "season_window": null, "quality": true, "wage_class": "skilled" }
```
`business_defs.jsonc`: `{ id, name, lot_use, stations: [{kind, count}], staff_slots: [{occupation, count, wage_class}], recipes: [ids], outputs_sold_as: "shop|stall|export", tools: [...] }`. `station_kinds.jsonc` lists kinds (oven, mill_stones, malt_floor, brew_vat, butcher_block, cheese_press, salting_tub, spinning_wheel, loom, fulling_tub, dye_vat, tailor_bench, tan_pit, cobbler_bench, saddle_tree, charcoal_clamp, bloomery, forge, anvil, cupel_hearth, saw_pit, brick_clamp, kiln, lime_kiln, mason_yard, barrel_bench, wheelwright_bay).
**Mass check at load:** `sum(outputs kg) <= sum(inputs kg) + adds_kg` (waste outputs listed explicitly); a recipe violating it fails validation (closes the legacy bug "2 wool -> 6 cloth", `ProductionSystem.gd:480-489`).

### 2.3 The ~40 Phase 2 recipes (kg per batch; hours are worker-hours at skill 50)
| Chain | Recipes (inputs -> outputs, hours) |
|---|---|
| Grain/food | `mill_flour` 100 grain -> 93 flour, 1 h; `bake_bread` 100 flour + 20 firewood -> 130 bread, 6 h; `malt_barley` 100 barley + 10 firewood -> 80 malt, 4 h; `brew_ale` 42 malt + 30 firewood -> 100 L ale, 8 h; `make_cheese` 100 L milk + 1 salt -> 10 cheese + 80 whey, 3 h |
| Meat/fish | `slaughter_sheep` 1 sheep (40 kg) -> 20 meat, 3 hide, 2 tallow, 4 bone, 3 offal, 3 h; `slaughter_cow` 1 cow (450) -> 200 meat, 30 hide, 20 tallow, 40 bone, 10 h; `slaughter_pig` 1 pig (80) -> 55 meat, 8 fat, 4 h; `salt_fish` 100 fish + 20 salt -> 85, 4 h; `salt_meat` 100 meat + 15 salt -> 90, 4 h |
| Textile | `spin_yarn` 2.2 wool -> 2.0 yarn, 100 h (10 spinner-days: 0.2 kg per spinner-day, plan); `weave_cloth` 1.6 yarn -> 1.5 cloth, 10 h (one weaver per 8 spinners); `full_cloth` 100 cloth + 5 fuller's earth -> 95, 40 h; `dye_cloth` 100 cloth + 8 dye -> 100 (+8 absorbed), 30 h; `tailor_clothes` 1.5 cloth -> 1.5 clothes_set, 8 h |
| Leather | `tan_hides` 100 hide + 40 oak bark -> 60 leather, 40 h over **15 days** (pits, plan: one season... decision: 15 days = one season); `cobble_shoes` 1.2 leather -> 0.9 shoes, 12 h; `make_saddle` 4 leather + 2 iron -> 5 saddle, 20 h |
| Metal | `burn_charcoal` 100 logs -> 20 charcoal, 12 h over 3 days (5 kg wood : 1 kg); `smelt_bloom` 40 ore + 60 charcoal -> 6 bloom, 10 h (plan 10 : 15 : 1.5 x 4); `forge_bar` 6 bloom + 10 charcoal -> 4.8 iron, 8 h; `make_steel` 10 iron + 20 charcoal -> 8 steel, 16 h; `forge_tools` 3 iron + 5 charcoal -> 2.5 tools (pieces), 16 h; `forge_nails` 10 iron -> 9 nails, 20 h; `forge_sword` 1.4 steel + 0.5 logs + 3 charcoal -> 1.6 sword, 24 h; `smelt_copper` 100 ore + 60 charcoal -> 12 copper, 12 h; `smelt_tin` 50 ore + 40 charcoal -> 18 tin, 8 h; `make_bronze` 90 copper + 10 tin + 20 charcoal -> 100 bronze, 8 h; `cast_bronze_goods` 10 bronze + 5 charcoal -> 9.5 cookware/fittings, 6 h; `smelt_galena` 1,000 galena + 600 charcoal -> 600 argentiferous lead, 60 h; `cupel_silver` 600 lead + 100 charcoal -> 3.0 silver + 540 lead, 40 h; `melt_coin` coin -> silver bar (money spec) |
| Wood/stone | `saw_planks` 100 logs -> 70 planks (+25 sawdust), 10 h (two sawyers); `make_barrel` 12 planks + 1 iron -> 13 barrel (piece), 8 h; `make_cart` 150 planks + 15 iron -> 165 cart (piece), 80 h; `fire_bricks` 100 clay + 40 firewood -> 80 bricks, 6 h; `dress_stone` 100 raw stone -> 85 dressed, 25 h; `burn_lime` 100 limestone + 60 firewood -> 56 quicklime, 6 h; `bind_thatch` 120 straw -> 100 thatch, 6 h; `make_glass` 100 sand + 80 firewood + 10 potash -> 70 glass, 12 h; `make_pottery` 20 clay + 15 firewood -> 15 pots, 5 h; `make_candles` 10 tallow -> 9.5 candles, 4 h |
Bronze is consumed by `cast_bronze_goods` (demand from household durable `cookware`, 1 per household, lifetime 1,825 days, and church fittings later). The old legacy chains marked "missing" are all present: mint chain (`smelt_galena -> cupel_silver -> mint`), sawyer/wheelwright/cooper, salt+fish/meat, glass, bronze.

## 3. Rules

### 3.1 Time to complete: skill and tools
`hours_needed = recipe.worker_hours x time_mult / 1000`, `time_mult = 1300 - 6 x skill` (skill 0: 1300, 50: 1000, 100: 700; skill = the lowest-skill worker on the station for batch quality, mean for speed); no tool of the required quality: `x 1500/1000`. Each worker-hour on a station adds `progress_ppm = 1,000,000 x 1000 / (worker_hours x time_mult)` (floor, remainder carried per station). `max_workers` per station limits parallel workers (2 for most). A batch completes when `ProgressPpm >= 1,000,000`; with `duration_days > 0` the station is also `BusyUntilHour = start + duration x 24` (tan pits, clamps) and the batch completes at the later of both.

### 3.2 Batch lifecycle
1. **Plan (daily, H4 slice of the business)**: for each idle station choose recipe `argmax expected_margin_per_hour` among recipes of its kind whose output cover < `2 T` (not flooding the market) and whose inputs are in stock or purchasable now: `margin = out_kg x price_seen(out) x (1 - toll) - sum(in_kg x price_seen(in) x (1 + h)) - hours x wage_h` ; ties -> lowest recipe id. Staff are assigned to stations (owner first, then ascending person index).
2. **Start**: inputs `Take` from the business stock at start (`Destroy(Consumed)`); if any input is missing the batch does not start and a **buy order** is posted (markets spec 3.4, input orders in business index order, hour = slice hour).
3. **Work (H2, hours 06-16 within daylight)**: progress per 3.1; tool wear: at completion `tool_pieces_lost = stochastic(wear_ppm_per_batch / 1e6)` (stream 0x221).
4. **Finish**: outputs `Create(Crafted)` at tier from items 3.3; by-products (hides, tallow, whey, sawdust, litharge) are listed outputs; the business **lists** sale lots (money spec: shop lots floor 0.9, ask) or hands goods to the next stage (a tannery's hides come from the market, not magically from the butcher).
No-input/no-staff stations idle; idle stations cost nothing but the owner's lost income.

### 3.3 Workers, employers, hiring (links to households spec 3.5)
Phase 1 `Occupation/Employer/WorkSettlement/WorkLot` are authoritative. Staff slots come from `business_defs` (e.g., smithy: 1 smith, 0-2 labourers; bakery 1-2; mill 1-2; tannery 2-4; weaver 1 weaver + put-out spinners (their yarn is paid per kg)). A vacancy is posted when `staff < slots`; the **wage offer** per slot kind starts at the class wage (labourer/unskilled 150 mites/day, skilled 350, master 500) and each unfilled 15 days it rises 5% (cap x1.5); each 15 days when 3+ applicants queued it falls 3% (floor x0.8). Wages are paid at the end of each worker-day from the business account in the business's slice: `pay = wage x hours_worked / workday_hours`; workers are paid in ascending person index until cash runs out; the rest accrue as `ArrearsMites` on the business and `WageOwedMites` on the person; 3 consecutive short days make the worker quit (households 3.5). The **owner takes no wage**; the owner's income is profit via dividends (3.5) and his own sales margin. Put-out spinners are paid by weight: `yarn_g x 750 mites/kg` (so the 0.2 kg/day rate equals the 150-mite day wage).

### 3.4 Quality, levels and what "level" means now
`Phase 1/legacy "building level N"` becomes **N stations** of the same kind (each needs its own inputs and workers): five looms use five times the yarn and make five times the cloth, never more mass than went in. Upgrading adds stations (Phase 5); in Phase 2 the count is fixed by the lot blueprint (`stations_by_lot_use`). Better tools raise `tool_bonus`, not quantity.

### 3.5 Cash box, dividends, closure
Revenue (sales to buyers via the book, market toll deducted at source) and costs (wages, inputs, rent/taxes) flow through the business account only. At seasonal **S5**: `reserve = 15 x (average daily wage bill + average daily input spend)`; `dividend = max(0, cash - reserve) x payout_pct / 100` (`business_payout_pct` = 50) -> owner household (`Dividend`). A business with `cash < 5 x daily wage bill` and a negative 15-day net for 3 consecutive seasons closes (`State = Closed`; staff released to the labour board; stock sold at bid or kept by the owner). Heirs of a dead owner with the same trade continue it; otherwise it is offered to the highest-skill local worker at S6 (free, Phase 5 adds sale/purchase).

### 3.6 Margins and the price solver
`tools/econ/PriceSolver` fixes every good's base price from recipes (items spec 3.2): `price_out = (sum in + 1.45 x labour) / out`. At those prices every recipe earns **profit = 45% of its wage cost** for an owner-operator; market prices then move supply/demand around it. A recipe whose real margin stays negative for 3 seasons is simply not planned.

## 4. Tick placement
- **H2** (hours 06-16, per business ascending id): work credit, completions.
- **H4 slice** (business index mod 24): plan recipes, start batches, post input orders, list outputs, pay wages.
- **H5** (hour 0): vacancy/wage-offer updates, hiring queue per settlement.
- **S5** (seasonal): dividends, closure, input stock review, `Cash15d` rollover.
Businesses never run in the live tier in Phase 2 (LODBridge may show the owner at the workplace); later phases may *lease* a business to the live tier (abstract tick skips leased stations and receives `WorkDone` events).

## 5. Determinism
Ascending business id -> station -> staff index; recipe ties lowest id; remainders in station state; RNG: tool wear `(…, businessId, 0x221, recipeId)`, quality `0x223`. No `Dictionary` iteration; recipe/good lookups by dense ids.

## 6. Interactions
Extraction/farming supply raw goods via the market; households supply workers and buy outputs; markets (orders, lots, tolls); taxes (market toll, rent); money (mint = faction business with `cupel_silver` outputs sold to the mint); caravans (import inputs, export surplus); items (tier, mass check, price solver); population (owner death/inheritance).

## 7. Edge cases
- Owner absent (promoted to live tier, travelling): staff continue; owner's sales listing continues; if the owner is away > 15 days the business works at 80% efficiency (no supervision, `x 1250/1000` hours).
- Input price spike makes margin negative: not planned; staff do other recipes or idle (still paid if employed under contract for that day: wage is per worker-day present; idle staff are laid off after 5 idle days, `Occupation` unchanged).
- Weather: outdoor stations (clamps, brick clamps, saw pits, mines) lose `daylight` hours and halt in heavy rain (`WeatherState` heavy/thunder/blizzard); fishing winter x0.4 (resources spec).
- Fire/theft/damage: Phase 4-5; the business account and stock are separate from the owner's so an owner's death does not freeze them.
- Fuel: recipes using firewood/charcoal buy from the market; fuel scarcity raises both prices and idle stations (visible cause chain).

## 8. Golden tests
| id | Case | Expected |
|---|---|---|
| G-B1 | `smelt_bloom` 10 h: skill 50, 1 worker; skill 50, 2 workers; skill 0; skill 100 | 10 worker-hours (100,000 ppm per worker-hour); 5 clock hours; 13 h (time_mult 1300); 7 h (700) |
| G-B2 | Mass check on `weave_cloth` as shipped (1.6 -> 1.5) and the legacy bug (2 wool -> 6 cloth) | shipped passes; legacy fails validation with a readable error |
| G-B3 | Weaver with 1 vs 5 looms, full inputs for a day | 1.6 yarn -> 1.5 cloth vs 8.0 yarn -> 7.5 cloth; output/input mass 0.9375 in both cases |
| G-B4 | Spinning rates | 10 spinner-hours -> 0.2 kg yarn (from 0.22 kg wool); 8 spinners x 10 h = 1.6 kg = one weaver-day of yarn |
| G-B5 | Iron chain for 1,000 kg ore (batch counts exact) | 25 smelts -> 150 kg bloom -> 25 forge_bar -> **120 kg iron**; charcoal 1,500 + 250 = 1,750 kg = 87.5 burns = 8,750 kg logs; worker-hours 250 + 200 + 1,050 = **1,500** |
| G-B6 | Price solver: flour = `(100 x 45 + 1.45 x 1 x 35)/93`; bread = `(100 x flour + 20 x 1 + 1.45 x 6 x 35)/130` | flour 48.93; bread **40.14 mites/kg** |
| G-B7 | Owner-operator bakery, one 6 h batch at solver prices | revenue 130 x 40.137 = 5,217.8; inputs 4,913.3; wages 210; profit 94.5 = 45% of wages |
| G-B8 | Dividend: cash 20,000, wage bill 700/day, input spend 1,300/day; then cash 50,000 | reserve `15 x 2,000` = 30,000: first 0; second `(50,000 - 30,000) x 50%` = 10,000 to the owner household |
| G-B9 | Wage offer 350 mites unfilled for 3 x 15 days | 367 -> 385 -> 404 (floor each step; cap 525) |
| G-B10 | 3 weaving stations (max 2 workers each), 5 workers | 2, 2, 1 workers; total hours credited per clock hour = 5 |
| G-B11 | Tanning: 100 kg hides + 40 kg bark at day 0 hour 6 | 40 worker-hours spread over days; station busy 15 days; leather 60 kg appears at day 15 hour 6; second batch in the same pit cannot start earlier |
| G-B12 | `RecipeMarginBand`: every recipe at solver prices | `profit / wages` within 0.45 +- 0.01 |
| G-B13 | Hiring: 2 vacancies, candidates (id, skill) = (9, 40), (4, 70), (6, 70) | persons 4 and 6 hired, 9 not; ties by lower id |
| G-B14 | Start with 59 of 60 kg charcoal for `smelt_bloom` | batch does not start, nothing consumed, a buy order for 1+ kg is posted |
| G-B15 | Silver chain: 1,000 kg galena | 600 kg lead -> 3.0 kg silver (grade 3,000 g per t) -> mint 3,000,000 mg = 2,307 crowns struck (money G-M1 rule) |

## 9. Soak invariants
1. Mass audit per recipe: outputs <= inputs + adds, always (runtime assert in debug, counter in release).
2. Every business with staff has a matching `Employer` in `PersonTable`; wage arrears total < 5% of monthly payroll in normal years.
3. Output of each staple good over a normal year within 70-130% of consumption (production + imports); no good stuck at the 4x price cap for > 30 days in a normal year.
4. Business count stable +-10% over 100 years; closures replaced by heirs/successors within 2 seasons.
5. Bronze, copper, tin, silver, glass, planks all have non-zero production and consumption in a 1-year soak (the "missing" chains of plan §4A.1 exist).
