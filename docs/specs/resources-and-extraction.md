# Resources and extraction

Plan refs: §4A.1 (resources are physical stocks on 1-acre tiles that deplete and regrow; seasonal extraction; the rates
table), §4A (time compression: yearly biology x60/365, daily flows historical), §4A.4 (hunting/foraging: carrying capacity
per tile, regrows by season; terrain speeds), §3a (livestock tied to farming), §3b (mines: flood, bad air), §5 Phase 2
(resource stocks per tile). Replaces legacy tile "slots" and the infinite foraging/hunting-always-succeeds defects.

## 1. Purpose
All raw goods that are not grain come from finite, regrowing stocks that exist at places. Work removes stock, regrowth is
scaled correctly for the 60-day year, and over-harvesting visibly depletes the land (and moves prices).

## 2. Data

### 2.1 `ResourceLayer` (per region; sparse)
Tile = Phase 1 region tile (64 m, ~1 acre; `Region.Index(x, y)`). One row per (tile, resource) that can hold stock, sorted by
tile index, then resource kind:
`TileIdx int`, `Kind enum { Fish, Wood, Game, Peat, Forage, Pasture, Clay, Sand, Stone }`, `StockG long` (grams; wood in
grams of logs at 700 kg/m3, game in animals x 1,000), `CapG long`, `LastDay short`. Derivation at region load (deterministic,
from Phase 1 layers):

| Kind | Which tiles | Cap per acre | Initial stock |
|---|---|---|---|
| Fish | tiles with `WaterFlags.Lake`; `River` tiles (cap 40 kg/acre); `Sea` coast (cap 200 kg) | lake 150 kg | 70-100% by `hash(tile)` |
| Wood | `Forest[t] >= 30` and not field/built: cap = `Forest[t]` x 1.0 m3 x 700 kg (100 m3/acre at 100 forest) | 70,000 kg | 90% |
| Game | woodland tiles, density 0.05-0.10 animals/acre by biome (forest 0.08, grass 0.05, taiga 0.1) | fractional animal counts stored as milli-animals | 100% |
| Peat | `Biome.Marsh` and mineral `Peat` veins | 150 t per acre | 100% |
| Forage | woodland/scrub/marsh edge tiles | 1,600 g per acre per game year (12 foraged kg per 7.5 acres) | refilled yearly |
| Pasture | grass/valley tiles not field/built, within 2.5 km of a settlement, fertility >= 40 | grazing capacity heads per acre (below) | n/a |
| Clay, Sand, Stone | tiles with `Biome`/`Strata` indicators or vein `MineralType.Stone` | 5,000 t (effectively unlimited, depletion tracked only for stone quarries) | 100% |
Pasture/meadow tiles are a **Phase 2 addition to the region model** (Phase 1 has none): ~10,000 acres per region, picked by a
pure function `Pasture(region)` so it is identical in every visit order.

### 2.2 `HerdTable` (owned animals)
`Id`, `Region`, `Owner` (household/business/fief/parish id), `Species enum { Ox, Cow, Sheep, Pig, Horse, Hen }`, `Head int`,
`Settlement`, `Condition permille`, `Flags`. Initial allocation at world start by `HerdBuilder` (`herd_stock_per_100_persons`
in content: cows 24, oxen 8, sheep 60, pigs 12, horses 3, hens 100; farm households get animals in proportion to acres held,
gentry/lord demesne get sheep and horses, millers/inns get pigs). Horses beyond the basics belong to §3e (later).

### 2.3 `ExtractionJob` (abstract job state, one per working group)
`Kind`, `Settlement`, `Tile` or `Site`, `Workers` (list), `Owner` (business or household), `ActiveSinceDay`. Created by the
households' work allocation (Survive-tier forage, Buffer/Profit occupation work) and by businesses (mine, fishery, woodlot,
charcoal clamp).

### 2.4 Content: `content/base/economy/resources.jsonc`
```jsonc
{ "type": "extraction", "id": "woodcutting", "resource": "Wood", "skill": "Forestry",
  "rate_kg_per_worker_day": 1050, "season_mult_permille": [1000, 1000, 1000, 800],
  "tool": "axe", "weather_block": ["Blizzard"], "weather_half": ["HeavyRain", "Thunderstorm"],
  "output_good": "logs", "stock_floor_pct": 30 }
{ "type": "herd_species", "id": "sheep", "pasture_heads_per_acre": 2.0, "hay_kg_per_head_day_winter": 1.5,
  "growth_pct_per_year": 35, "mortality_pct_per_year": 6, "products": [ { "good": "wool", "kg_per_head_year": 1.0, "season": "spring" }, ... ],
  "cull_meat_kg": 20, "cull_pct_per_year": 25 }
```

## 3. Rules

### 3.1 Yield per worker and stock factor
```
output_g_per_worker_hour = rate_kg_per_worker_day x 1000 / workday_hours x season_mult/1000 x weather_mult/1000 x skill_mult/1000 x stock_factor/1000
skill_mult   = 800 + 4 x skill                       (skill 0: 0.8, 100: 1.2)
stock_factor = min(1000, 1000 x StockG / (CapG x stock_floor_pct / 100))      (at or above 30% of cap: full; below it, proportional)
```
The output is `Take`n from the tile's `StockG` (never more than the stock) and `Create`d in the owner's stock (reason `Cut`,
`Caught`, `Mined`, `Hunted`, `Foraged`). `workday_hours` = 10 (daylight-limited). Weather mult: 1000 normal; `weather_half`
500; `weather_block` 0.

### 3.2 Rates and seasonal windows (plan §4A.1)
| Resource job | Rate per worker-day | Seasons (spring, summer, autumn, winter) | Notes |
|---|---|---|---|
| Fish, shore | 4 kg | 1.0, 1.0, 1.0, **0.4** | lake/river tiles |
| Fish, boat crew (3 workers) | 12 kg per crew (4 kg/worker) | 1.0, 1.0, 1.0, 0.4 | boat is a business asset |
| Woodcutting | **1.5 m3 = 1,050 kg** | 1.0, 1.0, 1.0, 0.8 | tile `Wood` |
| Charcoal | wood 5 kg : charcoal 1 kg (recipe) | all | burner works clamps (production spec) |
| Hunting | `p_kill = min(0.5, 3 x density)` per hunter-day (stream 0x240); carcass 40 kg meat + 1 hide (4 kg) | 0, 0.3, 1.0, 1.0 (spring closed, breeding) | **poaching is a crime** on demesne woodland (Phase 6) |
| Foraging | 3 kg per day (berries, nuts, mushrooms, greens) | 0, **1.0**, **1.0**, 0 | summer and autumn only; depletes tile; regrows at S1 |
| Trap lines | 1.2 kg per day (hares, birds) | 0, 0, 0.5, **1.0** | winter |
| Peat cutting | 300 kg per day | **1.0** (days 1-15 only), 0, 0, 0 | cut in spring, dried, **burned in winter** (heat value 0.6 of firewood) |
| Sea salt | 40 kg per day | 0, **1.0**, 0, 0 | summer pans on sea coast; rock salt via mines |
| Mining | **0.5 t ore per miner-day**, hauling by 1 labourer per 2 miners | 1.0 all | vein `Tonnage`, state `Active` only |
| Quarry stone / clay / sand | 1.0 t / 2.0 t / 2.0 t per day | 1.0, 1.0, 1.0, 0.7 | |
| Hay (meadows) | 2 acres/worker-day (farming spec); yield 1,200 kg/acre/year real = **197 kg per game year** | summer days 16-25 | stored as `hay` |
Heavy rain halves outdoor rates; blizzard stops them; fords closed days stop crossing to the far bank (extraction group
re-picks a tile).

### 3.3 Regrowth (seasonal S3, tile by tile; integer, floor)
Yearly biology is scaled by 60/365 (plan §4A); regrowth is applied once per season (a quarter of the game-year amount) unless
stated.
- **Wood**: coppice regrowth `5 m3/acre/year x 60/365 = 0.8219 m3/acre/game-year` = `143,836 g` per acre per season, only
  while `StockG < CapG`.
- **Fish**: `+25% of (CapG - StockG)` per season (plan's table value; **Decision:** literal table, not the x60/365 rule,
  because the scaled rule would leave 4% a year and no sustainable catch; the plan's own number gives a sustainable ~75
  kg/acre/game-year at half-stock). Winter regrowth x 0.4.
- **Game**: `+ stock x growth_pct_year x 60/365 / 4` per season (deer 30%/yr real -> +1.2% per season); never above cap.
- **Peat**: `50 kg/acre/game-year` (effectively finite).
- **Forage**: tile refilled to cap at S1 (annual).
- **Herds**: at S1: `head += floor(head x growth_pct_year x 60 / 36500 + stochastic remainder)` (sheep 35%: +5.75% -> 57 per
  1,000) and mortality `mortality_pct_year x 60/36500`; both stream 0x240 keyed by herd id.
- **Pasture**: summer grazing capacity `pasture_heads_per_acre` x tile acres; over capacity -> `Condition` falls 50 permille
  per season and culls at S3.
- **Mines**: no regrowth; `Tonnage -= ore taken / 1000` (t).

### 3.4 Herds and farming coupling
- **Grazing**: summer: `pasture + fallow fields (grazed)` capacity (sheep 2/acre, cow 0.5/acre, ox 0.5/acre, pig 0.2/acre
  mast in autumn); the farming `Flags.Grazed` is set when sheep >= 0.4/fallow acre in the settlement (farming 3.6).
- **Products per head per game year** (real yearly x 60/365, applied at the season named): sheep wool 1.0 x 0.164 = 0.164 kg
  (spring shearing; plan's "~1 kg" is the real fleece, Decision: apply the yearly-biology rule), cow cheese-milk 80 kg
  cheese-eq x 0.164 = 13.1 kg, hen eggs 100 x 0.164 = 16.4 eggs, pig and sheep/cow meat by culling. Products are `Create`d in
  the owner's stock at the season step as goods (`wool`, `milk` (litres, goes to `make_cheese`), `eggs`).
- **Hay and winter** (plan: herds need hay): at autumn end (S3, doy 45): `needed = sum(head x hay_kg_per_head_day_winter x 15)`, `have = hay + 0.5 x straw` in the owner's stock; `cull_fraction = clamp(1 - have/needed, 0, 0.6)`; `cull = floor(head x cull_fraction)` (lowest `Condition`/oldest first by herd row then head index) -> slaughtered into meat/hides (recipes
  `slaughter_*`; meat is fresh so sold or salted within days). Winter: hay eaten daily 15 days; without hay animals lose
  `Condition` 40 permille/day and die at 0.
- **Oxen** pull ploughs (farming): `oxen_per_100_arable_acres = 3`; death of oxen cuts plough rate (farming 3.2). **Manure**:
  grazed fallow +15 fertility (farming 3.4).

### 3.5 Mines
`Site.Kind == Mine` with `Mine` state `Active`; Phase 1 `Tonnage` (t of ore), `Levels`, `Mineral`, `HistoryEvent` stay. A
`Mine` business (owner: the settlement lord or a guild; staff: miners, 1 hauler per 2 miners; `max_miners = 4 + 4 x Levels`)
produces `ore` of the vein mineral (Iron -> `iron_ore`, Copper -> `copper_ore`, Tin -> `tin_ore`, Silver -> `galena` (silver
grade `silver_grade_g_per_t` = 3,000 g/t of galena), Coal -> `coal`, Salt -> `rock_salt`).
Hazards (seasonal S3, stream 0x241, key site id): **flood** `3% x Levels` per season (x2 in a `WetYear`, x1.5 in a river-high
season); flooded mine yields 0, state `Flooded`, chronicle `MineFlood`; drained by pumping (Phase 5). **Bad air** (levels >=
2 and tonnage mined > 40% of vein): rate x 0.5 until ventilated (Phase 5). **Cave-in** `0.5% x Levels` per season: 1-2 miners
die (population deaths, `Violence/Other` cause), mine closed for 15 days.
Ore flows to bloomeries/smelters by caravan or local market (ore is heavy: 0.5 t ore per `ox wagon load`...), so mines near
smelters matter.

### 3.6 Search area and depletion (who works which tile)
Each extraction group picks `ActiveTile = argmax over tiles within r of (StockG/CapG) x 1000 - distance_m / 8` (tiles sorted;
ties lowest index; `r` = 1.5 km for wood/forage, 4 km for game/fish) re-evaluated at S1/S3 or when `stock_factor < 700`.
Walking time to the tile is already part of the worker's `max_commute_m = 1,350`; beyond it the group moves camp (charcoal
burners, woodcutters) as a `JobKind.Forage` outing of up to 3 days (LODBridge shows them travelling). When wood within 1.5 km
falls under 20% of cap the settlement's firewood price rises (cutters walk further, `rate x 1000/(1000 + distance/3 m)`), a
visible deforestation cause chain; below 5% cap the tile's `Forest[t]` is reduced by 20 per season (scrub), which chunk
generation reads for new chunks (existing chunks keep their snapshot).

## 4. Tick placement
- **H2**: extraction output hourly per job (hours 06-16, daylight/weather).
- **H4 slice**: group re-planning, herd feeding in winter (daily), pasture checks.
- **S3 (seasonal)**: regrowth, herd products/growth/mortality (S1), hay/cull (autumn end), mine hazards.
- **S1**: forage refill, wool/eggs at their seasons.
L2 regions apply the same per-day kernels at daily granularity; L3/L4 regions use summary stock indices
(`RegionSummary.FuelIndex`, `FishIndex`).

## 5. Determinism
Tile/herd rows ascending; job groups ascending business/household id; RNG: hunting `(…, tileIdx, 0x240, day)`, herd
births/deaths `(…, herdId, 0x240, year)`, mine hazards `(…, siteId, 0x241, season)`. Pasture/derived layers are pure
functions of the Phase 1 region hash.

## 6. Interactions
Farming (oxen, sheep, hay, fallow grazing), production (logs, ore, hides, wool, milk, salt are recipe inputs), markets (price
of fuel/ore/fish), households (forage jobs, fuel, relish), weather (blocks, floods), taxes (poaching/forest rights later),
LODBridge (workers walk to tiles; herds appear on pasture), continent tick (exports/imports of ore, wool, salt).

## 7. Edge cases
- Stock exhaustion: `stock_factor` -> 0; group moves; `Take` never negative; tile reaching 0 wood becomes scrub.
- Tiles with several resources (a marsh tile: peat + forage + game): each kind independent (fixes legacy "each tile holds
  only one resource").
- Region borders: a group may use tiles in the neighbouring region only if that region is L0/L1 and loaded; otherwise the
  nearest in-region tile (Phase 2 rule; avoids cross-region writes).
- Disease/murrain: random herd mortality events (population spec outbreak style) halve oxen in a settlement at 1% per season;
  plough capacity falls with them.
- Poaching/trespass consequences are Phase 6; in Phase 2 hunting is allowed everywhere for Survive-tier households.


## Decisions made in this spec (collected in 00-overview.md section 10.4)
- **R1** Decision: Fish regrowth is +25% of the deficit per season (the plan's table value). Reason: the x60/365 rule would leave no sustainable catch (conflict C11).
- **R2** Decision: Wool is 0.164 kg per sheep per game year (1 kg real fleece x 60/365). Reason: apply the yearly-biology rule uniformly (conflict C13).
- **R3** Decision: A pasture/meadow layer is derived by a pure function of the region. Reason: Phase 1 has no meadows and herds need hay.
- **R4** Decision: Hunting is closed in spring; a hunter-day succeeds with probability `min(0.5, 3 x density)`. Reason: fixes the legacy 'hunting always succeeds'.
- **R5** Decision: Foraging capacity is 1,600 g/acre/year, refilled each spring. Reason: no infinite foraging; it is emergency food.
- **R6** Decision: Herds are head counts per owner, not individual animals. Reason: breadth first; individual animals arrive with horses (§3e).
- **R7** Decision: Silver ore grade is 3,000 g/t. Reason: rich enough that one vein matters; a calibration key (conflict C12).

## 8. Golden tests
| id | Case | Expected |
|---|---|---|
| G-R1 | Woodcutter, skill 50 (mult 1000), summer, clear, tile at 20% of cap with floor 30% | `1,050,000 g/10 h` = 105,000 g/h at full stock; `stock_factor = 1000 x 0.20/0.30` = 666 -> **69,930 g/h** (699 kg per 10-h day) |
| G-R2 | Wood regrowth, one tile | `5 m3 x 60/365 / 4` = 0.2055 m3 -> **143,836 g per acre per season** (floor) at 700 kg/m3 |
| G-R3 | Region firewood balance: 25,000 people x 1.5 kg x mean fuel mult 1.25 | 46,875 kg/day = 2,812 t per game year = 4,018 m3 -> **4,888 acres** of regrowth; needs ~45 cutter FTE; region woodland >= 50,000 acres is >10x sustainable |
| G-R4 | Lake 100 acres (cap 15,000 kg), stock 6,000 kg | regrowth `0.25 x 9,000` = 2,250 -> 8,250 kg; 10 shore fishers 15 days = 600 kg (240 kg in winter) |
| G-R5 | 1,000 sheep, hay 15,000 kg, 15 winter days at 1.5 kg | needed 22,500; `cull_fraction = 1 - 15,000/22,500` = 0.333 -> **333 culled** |
| G-R6 | 1,000 sheep at S1, growth 35%/yr real, no mortality | `1,000 x 0.35 x 60/365` = 57.5 -> 57 +1 with probability 0.534 (seeded) |
| G-R7 | Peat cutter, spring 15 days | 4,500 kg peat; heat equivalent 2,700 kg firewood = 25 households for the 15-day winter at 7.2 kg/day |
| G-R8 | Foraging: tile cap 1,600 g/acre/year, forager 3 kg/day | one forager depletes 1.9 acres/day; 30 summer+autumn days = 56 acres; tile at 0 gives 0 until S1 |
| G-R9 | Hunting: density 0.07/acre -> `p_kill = 0.21`/day, carcass 40 kg | expected 8.4 kg/hunter-day; over 100,000 seeded hunter-days kills = 21,000 +- 400; deer stock decreases by kills |
| G-R10 | Mine: 6 miners, vein 1,000 t, no hazards | 3.0 t/day; after 30 days tonnage 910 t; lasts 333 days (5.6 game years) |
| G-R11 | Mine flood chance: 2 levels | 6% per season normal; 12% in a `WetYear` |
| G-R12 | Silver output, 100 t galena at 3,000 g/t | 300 kg silver = 230,769 crowns struck (money G-M1 rule); equals **18% of region seed money** from one vein, so silver is finite and strategic (conflict C12) |
| G-R13 | Pasture derivation determinism | two builds of the same region (any visit order) give identical `Pasture` and `ResourceLayer` hashes |
| G-R14 | Relish balance for 25,000 people (calibration, normal year) | demand 325,500 measure-eq; supply gardens 130,000 (868 t veg at 0.15), cows 6,000 x 16 = 96,000, pigs 18,600, sheep 8,500, hens 10,000, fish 32,500 -> **~296,000 = 91%**; the rest substituted by grain (+2% grain demand) |

## 9. Soak invariants
1. No tile `StockG` outside [0, CapG]; regrowth never creates stock above cap; Mass audit (Create/Destroy reasons) per resource.
2. 100-year soak: wood stock within 70-100% of cap region-wide with normal cutting; fish stock >= 25% of cap (no collapse)
   with Phase 2 fishing; deer within 60-100%.
3. Relish supply >= 85% of demand every normal year (calibration test `RelishBalance`); herd head counts stay within +-15% of
   start over 100 years (growth = culls + deaths).
4. A forced over-harvest scenario (50 cutters on one village's woods for 5 years) visibly depletes it: firewood price ratio
   rises > 2x and nearby tiles < 20% of cap, then recovers within 10 years of normal use.
5. Mines: an Active mine's `Tonnage` only decreases; flood/cave-in events carry causes; silver minted never exceeds `tonnage mined x grade`.
