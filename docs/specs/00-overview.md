# Phase 2 specs: overview, tick schedule, budgets and decisions

Status: design, written before any Phase 2 code (plan §4: spec, then golden tests, then implementation).
Source of truth for scheduling is `docs/REDESIGN_PLAN.md` §5 Phase 2. This folder specifies *how*; the plan says *what*.
Legacy GDScript (`legacy/src/economy/*.gd`) is used only for ideas and for the defects listed in plan §4A.1.

## 1. Document map

| File | Owns |
|---|---|
| `money-and-markets.md` | ledger, mints, purses/chests, market books, price formula, trade API, coin conservation |
| `production-and-businesses.md` | businesses, workstations, recipes, workers/employers, wages, dividends |
| `resources-and-extraction.md` | per-tile stocks (fish, wood, herds, game, ore, peat, salt, forage), seasonal windows, mines |
| `farming.md` | fields, rotation, crops, yield, weather effects, harvest split, storage, livestock coupling |
| `households-and-needs.md` | household stores/purse, needs basket, eating, shopping, work-allocation AI, housing capacity |
| `population-and-vital-rates.md` | life table, births, marriage gated on dwellings, famine, disease, inheritance, migration |
| `items-mass-value-spoilage.md` | goods vs item instances, mass, value formula, 5-tier quality, spoilage, containers |
| `abstract-tick-and-lodbridge.md` | tiers, ownership, hourly tick, command queue, promotion/demotion, fold-back |
| `continent-tick.md` | seasonal continent tick, region summaries, aggregate trade/migration/news |
| `caravans-and-logistics.md` | caravan records, trip decision, movement, tolls, loss, physical caravans in the bubble |
| `taxes-and-treasuries.md` | faction/fief/parish treasuries, hearth tax, tolls, tithe, rent, arrears, unrest basics |
| `seamless-regions.md` | region ids, border gates, neighbour generation, region tick levels, paging, determinism |

## 2. Conventions (apply to every spec)

### 2.1 Numeric types and units
| Quantity | Type | Unit | Notes |
|---|---|---|---|
| Money | `Money` (`long`) | **mite** = 1/100 crown | 1 crown ~ 1 silver penny ~ 1.3 g silver (plan §4A.1). Accounts hold mites; the hero's purse shows whole crowns plus a mite remainder |
| Mass of goods | `long` | **gram** | All bulk stock is grams. 1 *measure* = 1,100 g (derived display unit; plan §1) |
| Counted goods | `int` | pieces | Tools, cloth bolts, barrels. Mass per piece from the shape (items spec) |
| Volume | `int` | cm3 (items) / m3 (wood stock, in litres internally) | |
| Prices | `Fixed` (Q16.16) | mites per kg (bulk) or per piece | Trade totals are rounded to whole mites at settlement time, half up |
| Rates in JSON | integer | explicit suffix: `_per_day`, `_per_season`, `_per_year`, `_ppm`, `_permille`, `_pct`, `_g`, `_kg`, `_mites` | The loader rejects a rate key without a unit suffix |
| Time | `GameTime`/`long hour` | game seconds / game hours | One game day = 24 h; season = 15 days; year = 60 days. "Per year" in this folder always means per 60-day game year |
| Randomness | `DetRng` | counter based | No `System.Random`, no `Math.Pow/Sin` in sim code |

No `float`/`double` in simulation code. Square roots use a new `Fixed.Sqrt` (integer Newton on the Q16.16 raw value, floor); powers with fractional exponents are avoided or tabulated. Percent/permille/ppm in JSON are integers.

### 2.2 Ids, regions and order
- `EntityId` keeps its 8-bit kind and 56-bit counter. Phase 2 splits the counter: **bits 48..55 = origin region id (0..255), bits 0..47 = serial within that region and kind** (`EntityId.Make(kind, region, serial)`, `EntityId.Region`). New `IdKind` values: `Region, Business, Caravan, Account, Contract, Lot` (append only; never renumber).
- Every record also stores its *current* region in a `Region` column (`byte`). Origin region in the id never changes; current region changes on migration.
- Content ids are strings in JSON; at load they become dense `ushort` runtime ids, assigned by **ordinal string order of the id** (mods included), so runtime ids are identical on every machine.
- All iteration is ascending id/index. Ties (two buyers wanting the last kilogram) break by `hash(DetRng.Mix(tickKey ^ id))`, then id. Never `Dictionary` order, never thread completion order.

### 2.3 RNG streams (all keyed `(seed, tickKey, entityId, stream, draw)`)
`tickKey` = game hour index (`GameTime.TotalHours`) of the step that draws, including for daily and seasonal steps (the hour they run). Streams are registered in `Core/RngStreams.cs`; Phase 2 owns 0x200..0x2FF (0xC0 weather and 70..78 minerals already exist).

| Stream | Use | Entity key |
|---|---|---|
| 0x200 | death roll | person |
| 0x201 | conception roll | woman |
| 0x202 | marriage matching | candidate (man) |
| 0x203 | migrant choice | household |
| 0x204 | disease/outbreak | settlement |
| 0x210 | farming: hail, blight, frost pick | parcel / region-culture |
| 0x211 | yield variance (per field, +-4%) | field |
| 0x220 | market tie-break, order shuffle | settlement |
| 0x221 | stochastic rounding of goods | owner id |
| 0x222 | spoilage rounding | owner id |
| 0x230 | caravan ambush / loss | caravan |
| 0x231 | caravan trip noise | caravan |
| 0x240 | extraction variance | tile / site |
| 0x241 | mine flood / cave-in | site |
| 0x250 | continent tick region flows | region |
| 0x260 | event rolls for tax evasion/riot | household / settlement |

### 2.4 Chronicle and causes
Every state change a player might ask "why?" about writes a `ChronicleEntry` with a `Cause` (`Core/Chronicle.cs`): famine (`Famine`), weather-driven yield loss (`Weather`), price spikes with a concrete cause (`Weather`, `Raid`, `War`), otherwise `MarketDrift` with the supply/demand trend in `Data`. Phase 2 adds `CauseKind.Tax`, `CauseKind.Trade`, `CauseKind.Disease`, `CauseKind.Migration` (append only). Coverage target for Phase 2 soaks: >= 90% of entries have a cause other than `MarketDrift/Unknown` (plan §3.12).

## 3. System map

```
            Weather (Phase 1, per cell)            Content JSON (goods, recipes, crops, rates)
                  |                                         |
   +--------------v---------------+                         v
   | Farming  --grain--> Households <--wages/food-- Production/Businesses <-- Extraction (tiles)
   |   ^   \                | ^  \                     |  ^                      
   |   |    tithe/rent      | |   buy orders           |  | inputs/outputs       
   |   |      v             v |    v                    v  |
   | Taxes/Treasuries <--> Money & Markets <------ Caravans/Logistics <--> roads (RoadEdge, gates)
   |                        (ledger, books)                 ^
   +-- Population (births/deaths/marriage/migration) --------+
                         |
              Abstract tick (hourly)  <-- LODBridge -->  Live tier (20 Hz, <=3 bubbles)
                         |
              Continent tick (seasonal, region summaries)  <-- seamless regions (levels, paging)
```

Ownership of mutable tables (single writer rule; see `abstract-tick-and-lodbridge.md`):

| Table | Writer | Readers |
|---|---|---|
| `Ledger` (accounts) | `Ledger.Transfer/Mint/Sink` only (called by all systems) | everyone via snapshots |
| `StockTable` (goods per owner) | production, farming, households, markets, caravans via `Stock.Move` | |
| `MarketBook` per settlement | markets | households, caravans, UI snapshot |
| `FieldTable`, `ParcelWork` | farming | households (food), taxes |
| `ResourceLayer` | extraction | production |
| `PersonTable`, `HouseholdTable` | population, households | all |
| `BusinessTable` | production | taxes, markets |
| `CaravanTable` | logistics | markets, LODBridge |
| `TreasuryTable` | taxes | all via ledger |
| `RegionSummary[256]` | continent tick | seamless regions, caravans |

## 4. Tick schedule

Game hour `h` boundary is a **barrier** `B(h)`. The abstract tick for each active region runs on a worker thread between barriers; results are applied at the barrier (§4.1 of `abstract-tick-and-lodbridge.md`).

| Tick | Runs when | Tier / who | Work (ordered) | Budget |
|---|---|---|---|---|
| Live fixed step | 20 Hz real time | live tier, <= 3 bubbles | movement, combat, ECS systems; reads economy snapshots; issues `EconCommand`s | <= 8 ms (300 people) |
| **Hourly (H)** | every game hour, every L0/L1 region | abstract tier | H0 env sample -> H1 caravans -> H2 work -> H3 market session -> H4 daily slice -> H5 hour-0 daily globals -> H6 seasonal (hour 0 of day 1/16/31/46) -> H7 publish | <= 150 ms per region (25k people, 58k fields) |
| Daily slice | inside H4, each household/business at `index % 24 == hour of day` | abstract tier | eat, heat, wear, wages, vital rolls, spoilage, price EMA | share of the 150 ms |
| Daily (L2 regions) | game hour 0 | resident ring-2 regions | the 24 slices run back to back, markets cleared once | <= 250 ms per region |
| **Seasonal (S)** | hour 0 of doy 1, 16, 31, 46 | abstract tier (H6) | growth/rotation, regrowth, rent/hearth tax, mint, price drift, caravan re-plan, summary export | inside the 150 ms, sliced by settlement (<= 40 ms) |
| **Continent (C)** | hour 0 of doy 1, 16, 31, 46, after all region S steps | continent worker | region summaries, aggregate trade, migration, news, weather bad-year effects | <= 500 ms (256 regions) |
| Page-in catch-up | region leaves L3 (sleeping) | worker | `CatchUp` in 15-day steps (<= 4 per game year of gap) | <= 3 s |

Order inside H (per region, fixed; each step iterates settlements, then households/businesses/caravans, in ascending id):
1. **H0 env**: weather sample per *settlement cell* (<= 256 `RegionWeather.At` calls) into `HourEnv` (temp, precip, river-high days, daylight).
2. **H1 caravans**: advance along edges, resolve arrivals, tolls, loss rolls.
3. **H2 work**: worker-hours for businesses (recipe progress), extraction jobs, farm tasks.
4. **H3 market session** (06:00 to 18:00 local; skipped at night): sellers post, merchants rebalance, then households shop in index order for the hour's slice.
5. **H4 daily slice** (households and businesses whose `index % 24 == hour`).
6. **H5** (hour 0 only): settlement daily globals (demand EMA, vacancy, housing review, news).
7. **H6** (hour 0 of season start only): seasonal steps in the order of each spec's "Seasonal" section: farming -> extraction -> taxes -> mint -> prices -> caravans -> summary.
8. **H7**: build the `RegionOutbox` (chronicle entries, rumour seeds, LODBridge requests, snapshot deltas).

## 5. Data flow
- **Goods**: extraction/farming create (faucets); recipes transform (mass-checked); households/businesses consume; spoilage and wear destroy (sinks). Every creation and destruction goes through `Stock.Create(reason)` / `Stock.Destroy(reason)` so the soak can audit mass by reason.
- **Money**: only `Ledger.Mint` creates (faction mints, from silver); only `Ledger.Sink` destroys (hoard burial, melting). Everything else is `Ledger.Transfer(from, to, amount, reason)`. Audit: `sum(accounts) == seed + minted - sunk`, checked every season in debug and every day in soaks.
- **People**: births/deaths in population; movement between regions as records at the barrier (`MigrationMailbox`).

## 6. Budgets and cost model
Reference PC (plan §2.11). Costs are per region (25,000 people, ~5,200 households, ~62,500 fields, ~65 settlements, ~700 businesses).

| Component | Work per game day | Est. ns/unit | Per game day | Per hour (sliced) |
|---|---|---|---|---|
| Household daily (eat, heat, wear, rolls) | 5,200 households | 2,500 | 13 ms | 0.5 ms avg |
| Person vital rolls | 25,000 | 300 | 7.5 ms | 0.3 ms |
| Business/work hours | 700 businesses x 10 h | 3,000 | 21 ms | 0.9 ms |
| Farm tasks (parcels) | ~9,000 parcels | 400 (active seasons) | 3.6 ms | 0.15 ms |
| Markets (65 books x ~40 goods) | 12 sessions | 40,000 per book-session | 31 ms | 2.6 ms |
| Caravans (<= 60 active) | 24 hourly steps | 2,000 | 3 ms | 0.12 ms |
| Seasonal farming (62,500 fields) | once per season | 150 | 9.4 ms per season | spread over 15 days |
| **Typical total** | | | **~80 ms/day** | **~3.5 ms/hour** |
| **Hourly worst case** (hour 0 + season start + market) | | | | **<= 150 ms hard cap** |

- Headless soaks run the same kernels; 100 game years = 6,000 days x ~80 ms = ~8 min (CI soak target <= 15 min). Test `HourlyTickBudget` fails above 150 ms p99 on the reference runner; `SeasonalContinentTickBudget` above 500 ms.
- Memory per region: records-only <= 25 MB; resident detailed (with chunks) <= 33 MB (plan 300 MB for 9).
- Saves: <= 1 MB per detailed region for people, lots, fields, plus stocks/business tables <= 0.4 MB; checked by `SaveBudget` test.

## 7. Shared constants (all in `content/base/world.json` unless stated)
| Key | Value | Used by |
|---|---|---|
| `mites_per_crown` | 100 | money |
| `grams_per_measure` | 1100 | farming, households |
| `workday_hours` | 10 | production, wages |
| `market_open_hour`, `market_close_hour` | 6, 18 | markets |
| `spoil_barn_pct_per_season`, `spoil_granary_pct_per_season` | 5, 1 | items, farming |
| `wage_labourer_mites_per_day` | 150 | wages |
| `wage_craftsman_mites_per_day` | 350 | wages |
| `crown_silver_mg` | 1300 | mint |
| `seed_money_crowns_per_person` | 50 | world gen |
| `region_food_overhead` | see farming (`acres_per_person_x10` = 25) | region gen |
| `nutrition_reserve_days` | 30 | famine |

## 8. Test strategy
- **Golden tests** live in `tests/Ravensguard.Sim.Tests/Economy/*GoldenTests.cs`; each spec's "Golden tests" section is the case list (`G-<file>-<n>`); expected values were computed by hand (and cross-checked with a throwaway script) and are asserted exactly (integers) or within the stated tolerance.
- **Determinism**: same seed -> same `EconomyHash` after N days on Windows and Linux; run hourly vs "coarse daily" kernel order must match (the kernels are identical; only batching differs).
- **Soak** (headless, `tools/Ravensguard.Bench` + xUnit category `Soak`): `Soak1Year`, `Soak100Years`, `SoakRichPlayer` (scripted hero buys/sells, 50 crowns -> 50,000 crowns), `SoakBadYears` (forced drought x2). Common invariants (each spec adds its own):
  1. coin: `sum(accounts) == seed + minted - sunk` every day; 2. goods: `created - destroyed == stock delta` per good; 3. population 25,000 +-10% at every year end over 100 years; 4. every `price/ref` within [0.3, 4.0] always and inside [0.6, 1.8] for staples at the 5th-95th percentile of days; 5. no NaN/overflow (`Fixed` throws); 6. `CauseCoverage >= 0.9`.

## 9. Phase 2 build order (each step ends with its golden tests green)
1. `Fixed.Sqrt`, `Money`, `Ledger`, `Stock` + content goods/recipes/crops loaders and validators.
2. Farming (fields, yields, harvest split) on the Phase 1 region -> region food balance test.
3. Households/needs + population (vital rates) -> 1-year soak without markets (barter via village stores).
4. Markets + production + extraction -> 1-year soak with prices.
5. Taxes/treasuries/mint -> coin audit; 100-year soak.
6. Abstract tick packaging, command queue, LODBridge, caravans physical.
7. Continent tick + seamless regions (ids, gates, paging) -> three-region walk test.

## 10. Decisions

### 10.1 Open decisions for the user (player-facing)
| # | Decision | Default taken | Why |
|---|---|---|---|
| O1 | **Embark regions snap to the 16 x 16-cell region grid** (256 possible regions), not any cell window | snap | Neighbour regions then tile exactly; the embark map shows the grid |
| O2 | **Currency shown to the player**: crowns only, whole crowns for the hero, sub-crown prices shown to 2 decimals per kg; no penny/farthing coins | crowns only | Keeps one denomination; fits "crown ~ silver penny" |
| O3 | **Forestalling/cornering grain** (buying out the market before the hungry gap): allowed, causes unrest and a "dear bread" grievance attributed to the hero; no law until Phase 6 | allowed | Plan §3a lists hoarding as a player play |
| O4 | **Prices you see**: exact in the market you are standing in; elsewhere "last heard" with its age; caravan masters and merchants use the same info | exact local, aged remote | Plan §4A.4 (act on prices seen or heard) |
| O5 | **Toll gates**: tolls apply at bridges and realm/fief gates, never at region edges | as stated | Region edges are technical |
| O6 | **Starting famine risk**: normal years leave ~6% food surplus; a drought (rain 22%) causes famine in the hungry gap unless stores or imports exist | as stated | Plan wants famines from real bad years |
| O7 | **Rent default for hero-leased land**: share of harvest 25% (not coin) | 25% share | Plan §3a ("a share of 20-33%, or coin") |
| O8 | **No lending/interest in Phase 2** (plan removes the 2% interest; loans return with contracts in Phase 6) | none | Avoids a money faucet |
| O9 | **Hero's wealth affects prices** only through local coin per person (the "rich player" soak); there is no hidden price protection | systemic | Plan §3.3 |

### 10.2 Engineering decisions
| # | Decision | Reason |
|---|---|---|
| E1 | Money in mites (1/100 crown), `long`; goods in grams, `long`; prices `Fixed` | Exact conservation, no floats |
| E2 | `EntityId` counter split: 8-bit origin region + 48-bit serial | Records keyed by region (plan §1B) without a second id type |
| E3 | The abstract worker mutates economy tables in place while it holds the region gate; the live tier reads immutable snapshots and writes only via `EconCommand`s applied at barriers/command points | One writer; avoids double buffering 25k records (deviation noted from plan §2.4's "results queue") |
| E4 | Daily work sliced by `index % 24` over the 24 hourly ticks | Keeps the hourly cost flat (<= 150 ms) while rates stay per day |
| E5 | Trade is priced by the exact integral of the price curve (closed form with `Fixed.Sqrt`), split at the 0.3x/4x clamps | "Per unit" repricing without per-unit loops |
| E6 | Rates in JSON carry unit suffixes; loader rejects bare numbers | Plan §1: every rate has an explicit unit |
| E7 | Mill toll is charged as 1/16 of grain drawn from stores when a mill serves the household; it is a transfer, not a loss | Plan §3a; keeps mass conserved |
| E8 | Resident set: ring 0-1 detailed (hourly), ring 2 resident records-only (daily), beyond ring 2 paged with only a `RegionSummary` | Resolves "paged to disk" vs "cheaper daily tick" (conflict C4) |
| E9 | Newborn/new-record ids come from the region allocator of the region where the event happens, never from visit order | Determinism across visit order |
| E10 | Phase 1 `AcresPerPersonX10` raised 23 -> 25 (see C1) and gate placement made a pure function of the cell pair (see C5) | Food balance; seamless roads |
| E11 | The hero is a person with a one-person household and a household account | One account model, no special purse |

### 10.3 Conflicts and gaps found in the plan / Phase 1 code
| # | Finding | Resolution |
|---|---|---|
| C1 | **Food does not balance.** 2.3 acres/person x 2/3 sown x 38.4 net = 58.9 measures per person-year vs 60 eaten, before spoilage (5%/season), ale and fodder. The Phase 1 spike noted the 1.84x overhead but left it unnamed | Raise `acres_per_person_x10` to 25 (62,500 acres, 25% of the region); define the diet as 0.80 measure staple grain + 0.20 relish (fish, meat, dairy, garden) per day, plus ale (0.25 L/person-day ~ 0.12 measure of malt) and ~3% fodder. Region test (farming G-F15): normal-year grain surplus +3.9% (accept +2..+10%); with 2.3 acres it is -4.3% |
| C2 | Plan §3a says granary spoilage 1% / barn 5% per season, §4A.1 says "nothing spoils" in the old code | Plan §3a wins; rates per season converted to per-day ppm |
| C3 | §4A.1 "Fertility gated on food security" vs §4A.2 "30% yearly fertility, scaled by food security" and "marriage gated on dwellings": three brakes | Dwellings are the hard brake; food security scales fertility (x0.2..1.0); hunger raises deaths |
| C4 | §1B: "at most 9 regions in memory, others paged" vs "farther regions run a cheaper daily tick over the same records" | E8 |
| C5 | Phase 1 `RegionBuilder.Roads` places gates from the *local* terrain (dry-land nudge) and de-duplicates in per-region iteration order, so two neighbours can disagree on a gate | Gate = pure function of (cellA, cellB) on the continent route; snapped to the tile grid; nudging only along the border by continental data; ordered by route id (see `seamless-regions.md`) |
| C6 | Phase 1 `RegionBuilder` accepts any `(cx, cy)` window; §1B assumes a 16 x 16 grid | O1 |
| C7 | Plan §3a "seed 9-10 measures" is a range; the example "38.4 net" needs seed 9.6 | Seed = 20% of the crop's base yield (9.6 for wheat) |
| C8 | Levy numbers differ between §1 (520/1,040) and the old spike text | §1 values stand (1 per 10 / 1 per 5 households of ~5,200); not a Phase 2 matter beyond `Household.Levied` |
| C9 | `HouseholdTable.Purse` is `int` placeholder | Replaced by an `Account` (long mites) |
| C10 | Phase 1 `Parcel` has one `Slot` per parcel but plan §3a says rotation is per *field* | Rotation is per field; the parcel slot is a default for new fields (farming.md) |
