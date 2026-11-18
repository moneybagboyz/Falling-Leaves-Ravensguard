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

No `float`/`double` in simulation code. Square roots use a new `Fixed.Sqrt` (integer Newton on the Q16.16 raw value, floor);
powers with fractional exponents are avoided or tabulated. Percent/permille/ppm in JSON are integers.

### 2.2 Ids, regions and order
- `EntityId` keeps its 8-bit kind and 56-bit counter. Phase 2 splits the counter: **bits 48..55 = origin region id (0..255),
  bits 0..47 = serial within that region and kind** (`EntityId.Make(kind, region, serial)`, `EntityId.Region`). New `IdKind`
  values: `Region, Business, Caravan, Account, Contract, Lot` (append only; never renumber).
- Every record also stores its *current* region in a `Region` column (`byte`). Origin region in the id never changes; current
  region changes on migration.
- Content ids are strings in JSON; at load they become dense `ushort` runtime ids, assigned by **ordinal string order of the
  id** (mods included), so runtime ids are identical on every machine.
- All iteration is ascending id/index. Ties (two buyers wanting the last kilogram) break by `hash(DetRng.Mix(tickKey ^ id))`,
  then id. Never `Dictionary` order, never thread completion order.

### 2.3 RNG streams (all keyed `(seed, tickKey, entityId, stream, draw)`)
`tickKey` = game hour index (`GameTime.TotalHours`) of the step that draws, including for daily and seasonal steps (the hour
they run). Streams are registered in `Core/RngStreams.cs`; Phase 2 owns 0x200..0x2FF (0xC0 weather and 70..78 minerals
already exist).

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
| 0x223 | craft quality roll | crafter person |
| 0x230 | caravan ambush / loss | caravan |
| 0x231 | caravan trip noise | caravan |
| 0x240 | extraction variance | tile / site |
| 0x241 | mine flood / cave-in | site |
| 0x250 | continent tick region flows | region |
| 0x260 | event rolls for tax evasion/riot | household / settlement |
| 0x2A0 | schedule jitter (exact spot inside a lot) | person |
| 0x2B0 | region catch-up survival and births | person / mother |

### 2.4 Chronicle and causes
Every state change a player might ask "why?" about writes a `ChronicleEntry` with a `Cause` (`Core/Chronicle.cs`): famine
(`Famine`), weather-driven yield loss (`Weather`), price spikes with a concrete cause (`Weather`, `Raid`, `War`), otherwise
`MarketDrift` with the supply/demand trend in `Data`. Phase 2 adds `CauseKind.Tax`, `CauseKind.Trade`, `CauseKind.Disease`,
`CauseKind.Migration` (append only). Coverage target for Phase 2 soaks: >= 90% of entries have a cause other than
`MarketDrift/Unknown` (plan §3.12).

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

Game hour `h` boundary is a **barrier** `B(h)`. The abstract tick for each active region runs on a worker thread between
barriers; results are applied at the barrier (§4.1 of `abstract-tick-and-lodbridge.md`).

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
1. **H0 env**: weather sample per *settlement cell* (<= 256 `RegionWeather.At` calls) into `HourEnv` (temp, precip,
   river-high days, daylight).
2. **H1 caravans**: advance along edges, resolve arrivals, tolls, loss rolls.
3. **H2 work**: worker-hours for businesses (recipe progress), extraction jobs, farm tasks.
4. **H3 market session** (06:00 to 18:00 local; skipped at night): sellers post, merchants rebalance, then households shop in
   index order for the hour's slice.
5. **H4 daily slice** (households and businesses whose `index % 24 == hour`).
6. **H5** (hour 0 only): settlement daily globals (demand EMA, vacancy, housing review, news).
7. **H6** (hour 0 of season start only): seasonal steps in the order of each spec's "Seasonal" section: farming -> extraction
   -> taxes -> mint -> prices -> caravans -> summary.
8. **H7**: build the `RegionOutbox` (chronicle entries, rumour seeds, LODBridge requests, snapshot deltas).

### Seasonal step order (H6; every spec refers to these labels)
| Step | Runs | Owner spec |
|---|---|---|
| S1 | year rollover (doy 1): field rotation, fertility ledger, blight roll, window opening; spring herd growth/products, forage refill | farming, resources |
| S2 | grazing flag (sheep per fallow acre), pasture condition, closing of task windows | farming, resources |
| S3 | extraction regrowth, hay/cull at autumn end, mine hazards, coin parcel rent due (autumn) | resources, farming, taxes |
| S4 | hearth tax, dwelling rent, crown share forward, arrears ladder, re-letting of parcels, treasury payroll start | taxes |
| S5 | mint runs, price `Ref` drift, business dividends and closures, hoard checks, parish sales, lord stipend | money, production, taxes |
| S6 | household class, dissolution of empty households, marriage matching, vacancy statistics | households, population |
| S7 | migration decisions (migrants appear on roads the same day) | population |
| S8 | caravan planner, new/retired caravans | caravans |
| S9 | export `RegionSummary`, then the continent tick (C-steps) | continent |
Slicing: on the first day of a season each settlement runs its steps in the hour `k % 24` (abstract spec 3.2); field-level steps run in 24 index ranges.

## 5. Data flow
- **Goods**: extraction/farming create (faucets); recipes transform (mass-checked); households/businesses consume; spoilage
  and wear destroy (sinks). Every creation and destruction goes through `Stock.Create(reason)` / `Stock.Destroy(reason)` so
  the soak can audit mass by reason.
- **Money**: only `Ledger.Mint` creates (faction mints, from silver); only `Ledger.Sink` destroys (hoard burial, melting).
  Everything else is `Ledger.Transfer(from, to, amount, reason)`. Audit: `sum(accounts) == seed + minted - sunk`, checked
  every season in debug and every day in soaks.
- **People**: births/deaths in population; movement between regions as records at the barrier (`MigrationMailbox`).

## 6. Budgets and cost model
Reference PC (plan §2.11). Costs are per region (25,000 people, ~5,200 households, ~62,500 fields, ~65 settlements, ~700
businesses).

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

- Headless soaks run the same kernels; 100 game years = 6,000 days x ~80 ms = ~8 min (CI soak target <= 15 min). Test
  `HourlyTickBudget` fails above 150 ms p99 on the reference runner; `SeasonalContinentTickBudget` above 500 ms.
- Memory per region: records-only <= 25 MB; resident detailed (with chunks) <= 33 MB (plan 300 MB for 9).
- Saves: <= 1 MB per detailed region for people, lots, fields, plus stocks/business tables <= 0.4 MB; checked by `SaveBudget`
  test.

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
- **Golden tests** live in `tests/Ravensguard.Sim.Tests/Economy/*GoldenTests.cs`; each spec's "Golden tests" section is the
  case list (`G-<file>-<n>`); expected values were computed by hand (and cross-checked with a throwaway script) and are
  asserted exactly (integers) or within the stated tolerance.
- **Determinism**: same seed -> same `EconomyHash` after N days on Windows and Linux; run hourly vs "coarse daily" kernel
  order must match (the kernels are identical; only batching differs).
- **Soak** (headless, `tools/Ravensguard.Bench` + xUnit category `Soak`): `Soak1Year`, `Soak100Years`, `SoakRichPlayer`
  (scripted hero buys/sells, 50 crowns -> 50,000 crowns), `SoakBadYears` (forced drought x2). Common invariants (each spec
  adds its own):
  1. coin: `sum(accounts) == seed + minted - sunk` every day; 2. goods: `created - destroyed == stock delta` per good; 3.
     population 25,000 +-10% at every year end over 100 years; 4. every `price/ref` within [0.3, 4.0] always and inside [0.6,
     1.8] for staples at the 5th-95th percentile of days; 5. no NaN/overflow (`Fixed` throws); 6. `CauseCoverage >= 0.9`.

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
Each has a default already used by the specs; change the default in the named file if you disagree. **Confirmed by the user on 18 Nov 2026:** O1 (fixed region grid), O6 (famine as a real risk), O10 (villagers hurry to work, field camps beyond about 1.35 km). The others stand as defaults until the user says otherwise.

| # | Decision | Default taken | Why | Where |
|---|---|---|---|---|
| O1 | **Embark regions snap to the 16 x 16-cell region grid** (256 possible regions), not any cell window | snap | Neighbour regions then tile exactly; the embark map shows the grid | seamless 2.1 |
| O2 | **Currency shown to the player**: crowns only, whole crowns for the hero, sub-crown prices shown to 2 decimals per kg; no penny/farthing coins | crowns only | Keeps one denomination; fits "crown ~ silver penny" | money 2.1 |
| O3 | **Forestalling/cornering grain** (buying out the market before the hungry gap): allowed; it causes unrest and a "dear bread" grievance attributed to the hero; no law until Phase 6 | allowed | Plan §3a lists hoarding as a player play | taxes 3.8 |
| O4 | **Prices you see**: exact in the market you stand in; elsewhere "last heard" with its age; caravan masters and merchants use the same information | exact local, aged remote | Plan §4A.4 (act on prices seen or heard) | caravans 3.3 |
| O5 | **Toll gates**: tolls apply at bridges and realm/fief gates, never at region edges | as stated | Region edges are technical | caravans 3.5 |
| O6 | **Famine risk**: normal years leave ~4% food surplus; a drought (rain 22%) causes famine in the hungry gap unless stores or imports exist; the first famine can arrive within ~10 years | as stated | Plan wants famines from real bad years | farming 3.3 |
| O7 | **Rent default for hero-leased land**: share of harvest 25% (not coin) | 25% share | Plan §3a ("a share of 20-33%, or coin") | farming 2.2 |
| O8 | **No lending/interest in Phase 2** (plan removes the 2% interest; loans return with contracts in Phase 6) | none | Avoids a money faucet | money 3.9 |
| O9 | **The hero's wealth affects prices** only through local coin per person (the "rich player" soak); no hidden price protection | systemic | Plan §3 item 3 | money 3.5 |
| O10 | **NPC commute pace**: walking 1 km at the plan's 1.5 real m/s takes 3.7 game hours, so scheduled villager commutes move at the sprint pace (5 m/s: villagers visibly hurry to work, 1 km = 1.1 game hours) and workplaces farther than 1.35 km use field camps. Alternatives: slower visible walking with late arrivals, or smaller settlements with fields beside farmsteads | sprint-pace commutes | Keeps a 10-hour workday feasible | abstract 3.4 |
| O11 | **Eviction for unpaid rent/tax** applies to everyone, including tenants of the hero; the hero as landlord can later waive arrears (UI in Phase 5/6) | applies | Plan §4A.2 ladder | taxes 3.5 |
| O12 | **Hunting, foraging and peat cutting by hungry households are allowed everywhere** in Phase 2; poaching/forest-law crimes arrive in Phase 6 | allowed | Needed for the survive tier | resources 3.2 |

### 10.2 Engineering decisions
| # | Decision | Reason |
|---|---|---|
| E1 | Money in mites (1/100 crown), `long`; goods in grams, `long`; prices `Fixed`; market integrals in `Int128` with an integer `Isqrt` (plus `Fixed.Sqrt` for small values) | Exact conservation, no floats; Q16.16 overflows at ~32 t of stock |
| E2 | `EntityId` counter split: 8-bit origin region + 48-bit serial; new `IdKind`s `Region, Business, Caravan, Account, Contract, Lot` appended | Records keyed by region (plan §1B) without a second id type |
| E3 | The abstract worker mutates economy tables in place while it holds the region gate; the live tier reads immutable snapshots and writes only through `EconCommand`s at barriers/command points | One writer; avoids double buffering 25k records (deviation noted from plan §2.4's "results queue") |
| E4 | Daily work sliced by `index % 24` over the 24 hourly ticks; seasonal steps sliced by settlement over the first day | Keeps every hour <= 150 ms while rates stay per day |
| E5 | Trade is priced by the exact integral of the price curve (closed form), split at the 0.3x/4x clamps | "Per unit" repricing without per-unit loops |
| E6 | Rates in JSON carry unit suffixes; the loader rejects bare numbers | Plan §1: every rate has an explicit unit |
| E7 | The mill toll is 1/16 of grain drawn from stores when a mill serves the household; a transfer, not a loss | Plan §3a; mass conserved |
| E8 | Resident set: ring 0-1 detailed (hourly), ring 2 resident records-only (daily), beyond ring 2 paged with only a `RegionSummary` | Resolves "paged to disk" vs "cheaper daily tick" (C4) |
| E9 | New-record ids come from the allocator of the region where the event happens, never from visit order | Determinism across visit order |
| E10 | Phase 1 changes needed: canonical border gates (C5), embark snap (C6), a pasture/meadow layer, >= 3% vacant usable dwellings, `Parcel.Slot` -> per-field slot, `PersonTable` tombstones + serial index (C17), `HouseholdTable` member lists that allow removals (C18), `AcresPerPersonX10` 23 -> 25 (C1) | Required by the Phase 2 rules |
| E11 | The hero is a person with a one-person household and a household account | One account model, no special purse |
| E12 | Base prices come from `tools/econ/PriceSolver` (inputs + 1.45 x labour) and are checked in; material `value_per_kg` in the Phase 0 files are placeholders regenerated from it (C14) | One price formula instead of four |
| E13 | Wear/spoilage use historical days (daily flows); yearly biology (herd growth, regrowth, yields) uses x60/365 | Plan §4A time compression |
| E14 | Relish (non-grain food) is substitutable by grain 1:1 when scarce; the diet is 0.80 staple + 0.20 relish measures/day for peasants, class-graded | Keeps famine from collapsing on one missing food |
| E15 | Bandits are real persons recruited only from vagrants; loot goes to hideout hoards, never destroyed | Fixes "bandits respawn from nothing" and money sinks |
| E16 | L2 regions run the same daily kernels with markets cleared once a day; L3/L4 use closed-form summaries calibrated against detailed regions (tests G-C10) | Cost scaling with distance |
| E17 | Region activation = deterministic `Generate` (day 0) + `CatchUp` to the summary state; page-in uses the same `CatchUp` | Order-independent content, plausible elapsed time |

### 10.3 Conflicts and gaps found in the plan / Phase 1 code
| # | Finding | Resolution |
|---|---|---|
| C1 | **Food does not balance.** 2.3 acres/person x 2/3 sown x 38.4 net = 58.9 measures per person-year vs 60 eaten, before spoilage (5%/season), ale and fodder. The Phase 1 spike noted the 1.84x overhead but left it unnamed | Raise `acres_per_person_x10` to 25 (62,500 acres, 25% of the region); define the diet as 0.80 measure staple grain + 0.20 relish per day (peasant), plus ale (0.2 L/person-day ~ 0.12 measure of malt) and ~3% fodder. Region test farming G-F15: normal-year grain surplus +3.9% (accept +2..+10%); with 2.3 acres it is -4.3% |
| C2 | Plan §3a gives granary 1% / barn 5% spoilage per season; §4A.1 says "nothing spoils" about the old code | §3a rates, converted to per-day ppm (3,414 and 670) |
| C3 | Three population brakes (food-secure fertility, dwelling-gated marriage, hunger/crowding deaths) are not ordered | Dwellings are the hard brake; food scales fertility (x0.2..1.0); hunger and crowding scale deaths |
| C4 | §1B: "at most 9 regions in memory, others paged" vs "farther regions run a cheaper daily tick over the same records" | E8 |
| C5 | Phase 1 `RegionBuilder.Roads` places gates with a dry-land nudge from the *local* terrain and de-duplicates in per-region iteration order, so two neighbours can disagree on a gate | Gate = pure function of the continental route and cell pair, no terrain nudge, de-dup by global route id (seamless 2.3) |
| C6 | Phase 1 `RegionBuilder` accepts any `(cx, cy)` window; §1B assumes a 16 x 16 grid | O1 |
| C7 | §3a "seed ~9-10 measures" is a range; the "38.4 net" example needs 9.6 | Seed = 20% of the crop's base yield |
| C8 | Levy numbers: §1 says ~520 peace / ~1,040 war, the spike text said 250-500 | §1 values (1 per 10 / 1 per 5 of ~5,200 households); only `Household.Levied` and its pay in Phase 2 |
| C9 | `HouseholdTable.Purse` is an `int` placeholder | Replaced by an `Account` (long mites) |
| C10 | Phase 1 `Parcel` has one `Slot`; the plan says rotation is per *field* | Per-field slot; parcel slot only seeds initial fields (farming 2.1) |
| C11 | Fish regrowth: table says +25% per season, the §4A rule says yearly biology x60/365 | Table literal (25% of the deficit per season): the scaled rule leaves no sustainable catch |
| C12 | §4A.1 says silver output is tuned to grow the money supply 1-2% a year, but Phase 1 silver veins are 20-400 t; even at 3,000 g/t one vein is ~18% of one region's seed money once | Silver is finite and strategic; `silver_grade_g_per_t` is a calibration key; the 1-2% target is checked by a continent-wide calibration test and, if short, vein tonnage is scaled in `continent.jsonc` |
| C13 | §4A.1 "spring shearing ~1 kg wool per sheep" ignores the x60/365 rule for yearly biology | 1.0 kg is the real yearly fleece; the game year yields 0.164 kg per sheep, flock sized to clothing demand (60 sheep per 100 persons) |
| C14 | Material `value_per_kg` (Phase 0): silver 120 crowns/kg contradicts 1 crown = 1.3 g silver (769 crowns/kg) | Silver 769.2, gold 9,615; other values placeholders until the price solver (E12) |
| C15 | Real-speed walking makes 1 km take 3.7 game hours, so a literal commute eats the workday | O10, abstract 3.4 |
| C16 | The budget sentence "58,000 fields" vs 62,500 fields after C1 | Budgets are measured at 62,500 fields (+8%) |
| C17 | `PersonTable.IndexOf(id)` is `base + serial`, impossible with deaths, tombstones and region moves | Serial index map; compaction at save only |
| C18 | `HouseholdTable.MemberIdx` requires members appended in one run, impossible with births, marriages, deaths | Per-household small member arrays or a free-list slab; slices by index stay `index % 24` stable |
| C19 | `Lot.Vacant` houses are not homes; the marriage brake needs usable empty dwellings | Region generation leaves >= 3% of usable dwellings empty; vacancy statistics are tested every season |
| C20 | `IdKind` has no Region/Business/Caravan/Account/Contract/Lot kinds | Appended (E2) |

### 10.4 Index of per-spec decisions (each is also listed in its spec under "Decisions made in this spec")
| ID | Spec | Decision | Reason |
|---|---|---|---|
| M1 | `money-and-markets` | Half-spread is 8% per side (16% total). | plan §4A.1 asks 10-20%. |
| M2 | `money-and-markets` | The seller pays the 5% market toll, buyers pay only the quoted ask. | one clean ask price; merchants price the toll in. |
| M3 | `money-and-markets` | `Ref` drifts toward `base x clamp(local coin per person / 5,000 mites, 0.5, 2.0)` by a quarter per season. | plan: `ref` drifts with the local coin per person; slow EMA avoids oscillation. |
| M4 | `money-and-markets` | Merchants are the market makers, limited by their own cash and stock caps (0.8 T cover). | plan: merchants are real people holding stock and cash. |
| M5 | `money-and-markets` | Seed money shares: gentry 20, burghers 35, peasants 30, treasuries 12, parishes 3 (%). | total of 50 crowns per person is the plan's; the shares are tunable content. |
| M6 | `money-and-markets` | Sinks are only hoard burial (still audited) and melting; death never destroys coin. | plan lists the leaks; estates always pass to someone. |
| M7 | `money-and-markets` | Trade totals are rounded half up to a mite; remainders go to the last lot or the market's change account. | exact conservation with integer money. |
| B1 | `production-and-businesses` | Station time multiplier is `1300 - 6 x skill` permille. | skill speeds work and sets quality without changing quantities. |
| B2 | `production-and-businesses` | Prices are `inputs + 1.45 x labour` (45% markup on value added). | owner profit independent of input price, no middleman gain on others' work. |
| B3 | `production-and-businesses` | Tanning takes 15 days (one season). | plan says tanning is one season in pits. |
| B4 | `production-and-businesses` | Dividend = 50% of cash above 15 days of costs, paid at S5. | plan: dividends are paid from profit out of the cash box. |
| B5 | `production-and-businesses` | Wage offers move +5% / -3% per 15 days within x0.8..x1.5. | wages respond to vacancies without a global labour model. |
| B6 | `production-and-businesses` | Farms are household holdings, not businesses. | fields are already records with their own rules (farming). |
| B7 | `production-and-businesses` | The old building 'level' becomes the number of stations. | plan: levels become workstations and better tools, never free output. |
| R1 | `resources-and-extraction` | Fish regrowth is +25% of the deficit per season (the plan's table value). | the x60/365 rule would leave no sustainable catch (conflict C11). |
| R2 | `resources-and-extraction` | Wool is 0.164 kg per sheep per game year (1 kg real fleece x 60/365). | apply the yearly-biology rule uniformly (conflict C13). |
| R3 | `resources-and-extraction` | A pasture/meadow layer is derived by a pure function of the region. | Phase 1 has no meadows and herds need hay. |
| R4 | `resources-and-extraction` | Hunting is closed in spring; a hunter-day succeeds with probability `min(0.5, 3 x density)`. | fixes the legacy 'hunting always succeeds'. |
| R5 | `resources-and-extraction` | Foraging capacity is 1,600 g/acre/year, refilled each spring. | no infinite foraging; it is emergency food. |
| R6 | `resources-and-extraction` | Herds are head counts per owner, not individual animals. | breadth first; individual animals arrive with horses (§3e). |
| R7 | `resources-and-extraction` | Silver ore grade is 3,000 g/t. | rich enough that one vein matters; a calibration key (conflict C12). |
| F1 | `farming` | Seed is fixed at 20% of the base yield per sown acre; tithe and rent are taken on the yield net of seed. | reproduces plan example 48 -> 38.4 net -> 24.96 for the tenant (conflict C7). |
| F2 | `farming` | Ungrazed fallow gives +5 fertility (grazed +15). | plan gives only the grazed value; some recovery without sheep is needed. |
| F3 | `farming` | Per-field yield noise is +-4%. | fields are not clones; mean stays 1.0. |
| F4 | `farming` | Rain factor is `400 + 600 x min(ratio, 1)` permille, with a waterlogging penalty above ratio 1.4. | a 22% rain drought gives ~0.58 and a wet year costs little until the harvest rots. |
| F5 | `farming` | Crop mixes: winter 60% wheat / 40% rye, spring 40% barley / 27% oats / 20% peas / 13% beans. | gives the 1-in-3 legume rotation of plan §3a. |
| F6 | `farming` | Winter grain ripens doy 26, spring grain doy 31; the harvest window is doy 26-35. | plan: last 5 days of summer plus first 5 of autumn. |
| F7 | `farming` | A late frost kills spring fields sown in the last 12 days; resow (x0.75) only before doy 12. | plan: late frost kills spring sowing. |
| F8 | `farming` | Uncut ripe fields lose 4% per day after a 10-day grace. | labour shortage must cost something visible. |
| H1 | `households-and-needs` | Everyone eats 1.0 measure-equivalent per day (no age scaling). | plan §3a literal; keeps the region food test simple. |
| H2 | `households-and-needs` | Peasant basket is 0.80 staple + 0.20 relish; relish can be replaced by grain 1:1. | avoids a famine from one missing food (E14). |
| H3 | `households-and-needs` | Tiers: Survive below 10 days of food cover, Buffer below 20. | plan §4A.1 survive, buffer, profit; thresholds tunable. |
| H4 | `households-and-needs` | Class is recomputed each season from occupation and assets. | plan §4A.2: class derived, mobility emerges. |
| H5 | `households-and-needs` | Clothes/tools/shoes lifetimes are historical days (1,095 / 900 / 365 for peasants). | wear is a daily flow (E13). |
| H6 | `households-and-needs` | Nutrition reserve drains 100 centidays per missed day and refills 50 per full day. | plan: 30-day reserve; 60-day refill is a reasonable pace. |
| H7 | `households-and-needs` | Seed corn is eaten only in an emergency. | historically true and keeps next year's sowing meaningful. |
| P1 | `population-and-vital-rates` | Daily hazard is the yearly rate divided by 60 (literal plan rule). | effective yearly mortality is slightly below the table (18.15% for infants). |
| P2 | `population-and-vital-rates` | No separate childbed mortality. | covered by the 15-49 rate; avoids double counting. |
| P3 | `population-and-vital-rates` | Marriage matching runs each season with a 35% proposal chance and needs a home. | the plan's dwelling brake with a staggered rate. |
| P4 | `population-and-vital-rates` | Orphans go to kin, else the parish; no kin means escheat. | plan: eldest son -> children -> spouse -> lord. |
| P5 | `population-and-vital-rates` | Dead persons are kept (tombstones) and moved to a graveyard table after 20 years. | kin graph and legends need them; memory stays flat. |
| P6 | `population-and-vital-rates` | Outbreaks are calibrated to add ~12% to baseline deaths. | plan: town disease x1.2, winter coughs, summer flux. |
| P7 | `population-and-vital-rates` | Pushed households emigrate with 40% chance per season. | migration must be visible and not instantaneous. |
| I1 | `items-mass-value-spoilage` | Quality scale: Crude/Poor/Common/Fine/Masterwork, thresholds 15/40/70/90, value x0.5/0.8/1.0/1.6/3.0. | plan: one five-tier scale. |
| I2 | `items-mass-value-spoilage` | Goods are counted stock; item instances exist only when individuality matters. | keeps millions of units cheap (plan §2.1a). |
| I3 | `items-mass-value-spoilage` | Spoilage is ppm per day with stochastic rounding. | exact expectation, deterministic, no per-unit clocks. |
| I4 | `items-mass-value-spoilage` | Condition multiplier on value is `0.3 + 0.7 x condition`. | worn goods keep some value. |
| I5 | `items-mass-value-spoilage` | Stowage is modelled as litres per kg; carts are limited by mass and volume. | plan: capacity in kg (not crowns); bulky goods need volume. |
| A1 | `abstract-tick-and-lodbridge` | The worker mutates economy tables in place under a region gate; the live tier reads snapshots and writes via commands. | avoids double buffering 25k records (E3). |
| A2 | `abstract-tick-and-lodbridge` | Scheduled NPC commutes move at 5 m/s real (900 m per game hour), max 1.35 km, longer jobs use field camps. | a literal walk eats the workday (C15, O10). |
| A3 | `abstract-tick-and-lodbridge` | Daily work is sliced by `index % 24`. | flat hourly cost (E4). |
| A4 | `abstract-tick-and-lodbridge` | Promote at 150 m, demote at 200 m after 5 s, at most 50 spawns per frame nearest first. | plan §2.3, with a timer against flicker. |
| A5 | `abstract-tick-and-lodbridge` | In Phase 2 the live tier is theatre; production stays abstract; later phases lease stations. | one source of truth, no double counting. |
| A6 | `abstract-tick-and-lodbridge` | Coarse mode loops hourly steps; it never skips. | determinism independent of speed. |
| CT1 | `continent-tick` | Unvisited regions use closed-form versions of the region rules, calibrated to detailed regions within 5%. | cheap, and consistent on activation. |
| CT2 | `continent-tick` | Trade flows are matched greedily by price gap with road-class capacities. | deterministic and simple; no solver. |
| CT3 | `continent-tick` | News travels at a courier pace of 0.25 m per game second and relays up to 3 hops. | prices known with an age (plan §4A.4). |
| CT4 | `continent-tick` | Flows with a detailed region are realised as import/export orders served by caravans. | visible caravans, no teleporting goods. |
| CT5 | `continent-tick` | The continent tick tracks 8 goods and a price index only. | budget and Phase 2 scope. |
| L1 | `caravans-and-logistics` | Guard wage default is 1,750 mites/day (17.5 crowns). | midpoint of the plan's 15-20 crowns/day. |
| L2 | `caravans-and-logistics` | Caravans travel only in daylight (sunrise+0.5 h to sunset-0.5 h). | plan: day length sets work hours; safety. |
| L3 | `caravans-and-logistics` | Remote prices are discounted 1% per day of age. | plan: act on prices seen or heard, with an age. |
| L4 | `caravans-and-logistics` | A trip runs only if profit >= 15% of its cost. | plan: expected margin must beat cost. |
| L5 | `caravans-and-logistics` | Bandits are real persons recruited from vagrants; loot goes to hideout hoards. | fixes 'bandits respawn from nothing' without destroying money (E15). |
| L6 | `caravans-and-logistics` | Planners run in ascending caravan id and reserve cargo. | deterministic competition for the same stock. |
| T1 | `taxes-and-treasuries` | Hearth tax per household per season: Low 100 / Normal 200 / High 350 / Crushing 600 mites; 30% forwarded to the realm. | plan: ~2 crowns per household per season and the four named levels. |
| T2 | `taxes-and-treasuries` | Arrears become debt at 3 seasons and eviction (rented/tenant) at 6; no interest accrues. | plan: arrears -> debt -> eviction; plan removes 2% interest. |
| T3 | `taxes-and-treasuries` | Tithe is 10% in kind of net harvest and of animal products; the priest keeps 40%, the rest is charity and parish sales. | plan §3a and §4A.1. |
| T4 | `taxes-and-treasuries` | Unrest rungs 200 / 350 / 550 / 750; riots by real households; revolt is only flagged in Phase 2. | revolt changes fief holder, which needs Phase 6 rule. |
| T5 | `taxes-and-treasuries` | The lord receives 40% of the fief surplus above two seasons of reserve. | closes the money loop without printing. |
| T6 | `taxes-and-treasuries` | Households with a tax grievance evade with 30% probability once unrest passes 350. | plan: unrest ladder includes evasion. |
| S1 | `seamless-regions` | Embark windows snap to the 16-cell grid. | neighbours tile exactly (O1). |
| S2 | `seamless-regions` | Border gates are pure functions of the continental route and cell pair; no terrain nudge; de-dup by route id. | both neighbours must agree (C5). |
| S3 | `seamless-regions` | Ring 0-1 resident detailed, ring 2 resident records-only, beyond paged with a summary. | resolves the plan's memory vs daily-tick wording (E8). |
| S4 | `seamless-regions` | Activation is `Generate` (day 0) + `CatchUp` to the summary; page-in uses the same CatchUp. | order-independent content with elapsed time. |
| S5 | `seamless-regions` | Neighbours are queued at 4 km from a border or corner, at most 2 generations at a time, cancelled beyond 8 km. | plan: about 4 km; budget and hysteresis. |
| S6 | `seamless-regions` | Ring changes need 1,000 m inside the new region. | no level thrash at a border. |

## Notes: Phase 2 integration (economy running together on the abstract tick)

Done by `AbstractSystemRegistry.CreateDefault` (14 economy systems, the order is in the abstract spec notes), `EconomyWorld` (the bundle in `SimWorld`'s service bag), `HeadlessWorld` (a world from a seed), `EconomySoak` and `WorldTool soak`. The stand-ins of `SoakWorld` (harvest, wages) are gone: harvest comes from `FarmingSystem`, wages from the businesses (workshops by the worker-hour, manors, stalls and traders by the day).

| # | Spec | Decision | Why |
|---|---|---|---|
| I1 | abstract | The economy is 14 `IAbstractSystem`s, no-ops without an `EconomyWorld`; farming's daily and seasonal steps run together at H5 first (`FarmingSystem.StepDay`). | spec order; population-only worlds still tick |
| I2 | production | Dividends only from cash above the starting capital. | the first season paid half the working capital out |
| I3 | farming/resources | Herds are settlement herds, products shared by acres held; cows added; keys default to off. | G-R5, G-R6, G-R14 without per-animal rows |
| I4 | money | Demand EMA counts at most 2 x the structural demand. | unfilled daily orders inflated demand 10-20 x |
| I5 | money | First merchant of a book starts with 60 percent of the target cover. | markets are empty at day 0 |
| I6, I7 | production | Input orders for the batches the market lacks; structural demand derived down the recipe chains. | one batch every two days, one batch a day per station |
| I8, I9 | production | Lord's market stall in every book without a merchant; manor business pays the lord's retinue and calls the poorest first. | no trader in 80 villages; wages for the Phase 1 retinues and the destitute |
| I10 | households | Shoes, clothes, tools wear out by daily rolls and are bought. | durables demand (households soak 4) |
| I11 | population | Chevage: half the coin of an emigrating household goes to the fief treasury. | the coin audit was exact but the money stopped circulating |
| I12 | resources | Gardens and hens in every household. | G-R14 relish balance |
| I13 | money | The continent trades with every book at the extremes of its cover (imports only what no recipe of the region makes). | stand-in for caravans; bounds staple prices to the spec band |
| I14 | households | Seed corn is not food; seed emergency. | holders starved on their seed |
| I15 | households | Farm sales, parish charity, fief relief purchases, treasury dole. | tithe and idle treasuries feed the hungry |
| I16 | production/farming | Extraction raws repriced to labour-consistent levels; `target_mean_fertility` 86; coin leases 100 percent at 640 mites. | see production P12, farming F26 |
| I17 | households | Lead's tuning list: livery, own grain first for shopping, parish and fief relief (see households spec notes). | done in the first integration pass |
| I18 | money/households | A household with land has an all-grain staple demand (it mills for the toll and bakes at home); flour and bread count as on sale in a household's shopping only when the book holds a day of their demand. | the structural demand put 55 percent of every household's staple on flour and bread no one baked: bread and flour sat at the 4 x cap and the shopper split its orders over goods that did not exist |
| I19 | production | A workshop's input orders are budgeted by its cash box, all inputs in proportion and shared by its stations; the owner puts coin back (weekly, at most half of his household's, up to the capital); a closed workshop opens again at S5 under the most skilled idle adult of the settlement whose household holds 30 crowns (one a settlement a season, half the household's coin as capital, reason Gift). | brewers held barley and malt and no coin for firewood; closed bakeries and mills never came back |
| I20 | production/taxes | A workshop still under a quarter of its capital draws an advance from the fief's treasury: a fiftieth of the treasury, at most half the capital (reason Stipend). | the treasury's idle coin goes back to work |
| I21 | money | An unsold producer lot's floor falls by a third a day it stays on the book (to 0.3), a merchant lot's by a tenth a day after five days (to 0.75), and the merchants of a book together hold at most 0.8 of the cover. | lots with a floor of 0.9 against a curve at 0.73 never sold: 320 t of firewood on one book and no one able to buy a log |
| I22 | households/resources | Peasant and gentry households gather 125 percent of their basket's firewood (reason Cut, no tile drawn down) and sell what is over 20 days' use to the village market; only burghers' fuel is structural demand. | only 12 woodcutter crews existed for 83 villages, firewood was at the 4 x cap and bakers and brewers could not fire an oven |
| I23 | households | Home brewing: a household with land brews six days of ale from its barley and fuel when it holds under three (reason Brewed, barley and wood consumed); the ale basket is 300 / 400 / 600 ml (peasant / burgher / gentry; was 200 / 300 / 500). | ale was at the cap; a landed household no longer buys ale while it has barley |
| I24 | farming/households | Gleaning (3 percent of every holder's harvest into the village's poor box, the parish granary); the Church moves grain from the eight richest granaries of the region to a household its fief cannot feed; the lord's barn above 40 days of his household's food feeds the destitute of his fief (alms in kind); at the year's start the lord's reeve lets holdings that fell back to him to the village households with the fewest fields a head (3 a member), landless first; a holder short of seed buys it at the market after the neighbours and the lord cannot lend it. | the hungry of a fief with an empty granary went unfed while others held stock; half the burghers' fields were never sown |
| I25 | taxes | Alms in coin: each season a lord gives half of what he holds above 1,000 crowns to the destitute households of his fief (under ten days of food and under 10 crowns), at most 25 crowns a member (reason Gift). | seven lords held 40 percent of the coin and the poor none |
| I26 | logistics (stand-in) | Regional carters: each day the richest merchant of a book short of a good (under half its cover) buys it from the book of the same fief with the most beyond its cover at that book's price and lists it at home. | a town grows no grain and sold none to anyone |
| I27 | population | Immigration (stand-in for the continent tick): while the region holds fewer people than it started with, departed households come back to a vacant dwelling (a sixth of the shortfall a season, at most 1.2 percent of the start) with 20 crowns and a fortnight of grain a head from the continent's purse and chest, and singles who left alone come home to a bed; emigration waits 12 seasons (was 4) and takes 20 percent (was 40). | the region could only lose people; the unmarried left after one year |
| I28 | goods | Grain spoils 1,200 ppm a day in a house or barn (was 3,414; a season 1.8 percent, a game year 6.9 percent), flour 3,000 (was 7,000); market books keep their stock in a granary-class store. | a house lost 18.5 percent of its grain a game year and the market halls 19 percent: 10 percent of the harvest rotted |
| I29 | households | Pease pottage: a household with no grain left eats its pulses, cheese, fish and meat for the staple too. | holders with a larder of peas and no wheat starved with 130 days of cover on the books |
| I30 | farming | Cereal yields 52 / 50 / 56 / 50 measures (wheat, rye, barley, oats; were 48 / 46 / 48 / 46), mean field fertility target 88 (was 86), half the leases are coin (was 100 percent), the farm day wage is 150 mites (was 40), farm sales keep 45 days past the next threshing (was 12), clothes, shoes and tools wear out in 730 / 270 / 600 days for a peasant (548 for a burgher's clothes; were 1,095 / 365 / 900). | the spec balance of +4 percent surplus left no room for a bad harvest (surplus is now about +13 percent); a 40-mite day wage bought 0.9 kg of grain |
| I31 | farming/production | The retainers of a manor (ploughmen, labourers, shepherds, servants on its staff) work the lord's demesne as part of their service: their farm hours are added to the lord's household's own hours instead of being hired from the pool at a coin wage. | the demesne was worked only when its lord held coin to hire his own staff, and the lords held 16 crowns each: a third of the sown acres went untilled over the decades |
| I32 | save | Review finding 2 (save, load, continue differs from the uninterrupted run): the markets section now saves the buy orders posted and not yet filled (buyer, good, quantity, price ratio, priority and a hook kind and argument from which the fill callback is rebuilt: a business's input spend or a household's durable), the fief stewards' parties live in `MarketSystem` so a saved order can name them, the same-kind business count is counted on the spot instead of cached, and the staff snapshot the farm hours read is taken at the farm day's start. `ResumeDeterminismTests` runs N + M hours straight against N, save, load, M for N in 0, 24, 700 and M in 12, 40 (both in whole days of at most 24 hours: a longer `AdvanceTo` runs its older hours coarse by design). | an order posted at 14:00 is filled at the 06:00 session, so any save between lost it |
| I33 | money/seamless | Coin that crosses a region border is no longer melted and minted: the ledger has `SinkReason.Emigrate` and `SourceReason.Immigrate`, counted in the conservation report's sunk and sourced coin (the imbalance stays exactly zero) and matched in the two regions' ledgers; `RegionTransfer` uses them. Saves older than the reasons load (the arrays are padded). The immigration of I27 moves coin inside one ledger (continent purse to household, reason Gift), so it needs neither. | the review: a cross-region move should be a transfer, not a sink plus a source |
| I34 | population | A famine entry ends after 60 quiet days (`famine_quiet_days`; was the same 5 as the days that start it), emigration waits 8 seasons and takes 30 percent a season (was 4 and 40). | a settlement flickering around the share made a dozen famine entries a year (cause coverage under 90 percent in the population soak) and the unmarried left after a year |
| I35 | money/soak | Coin-flow trace (`soak --coin`, also on with `--flow`; `CoinFlowTrace`): every purse gets a holder kind (lord, other gentry, parish = priests' households and parish chests, merchant's and master's households, retainer households = a member on a manor's staff, burghers, landed peasants, landless, fief treasuries, the continent, and the cash boxes of manors, stalls, workshops and crews); each year it prints the coin each kind holds at the start and end (with the households' count, median and p90), what each kind received and paid by transfer reason, and the 24 largest kind-to-kind flows. It reads a new `Ledger.Observer` (diagnostic, not state; a test checks the run's hash is unchanged with it on). The soak diagnostics also print, per staple, the structural / EMA / floor demand, stock and books at each clamp, and the ticker prints the slowest hour's systems and the collections during it. | verify the lead's circulation hypothesis before acting on it. What it showed (seed 1234, year 10, before I36-I40): coin moved 0.8 x the money supply a year (2.2 x in year 1); the lords were *not* hoarding (6 percent of the coin, median 1 crown: they pay it all to their manors as retainers' wages); the treasuries were empty (0.03 percent); the coin sat with the priests (21 percent, 75 households, median 2,538 crowns: tithe sold, nothing spent), the top tenth of the landed peasants (21 percent, median 0), the masters (17 percent, median 0) and the retainers (14 percent); the median household of every kind held 0 to 1 crown. Peasants lost 69,000 crowns a year net, mostly to the merchants' boxes |
| I36 | money/households/markets | Demand measurement fixed where it was wrong: (a) firewood gathered follows the day's burn (`GatherFuelPct` 125 of the mild-weather basket -> 105 of the temperature-scaled burn) and a household keeps 25 days of today's burn before it sells (was 20 days at the mild rate: in frost it sold in the morning and bought back in the afternoon); firewood EMA was 1,459 t a day; (b) the demand floor of ale, salt and firewood is a quarter of the peasant's own daily quantity (was a quarter of a 1,100 g measure: 34 x the salt, the whole ale of the basket); (c) a worn set of clothes or pair of shoes is ordered as grams of the kg goods (`clothes_set_g` 1,500, `shoes_pair_g` 700 in the needs baskets; a pair of shoes was ordered as 1 g), the order is posted even when the book has none, and the wear is structural demand; (d) a landed household's grain demand on the books fades from its whole staple at no food in store to nothing at 40 days of cover (was its whole staple always: the books expected the region's entire grain consumption, merchants filled their 0.8-cover cap with grain no one bought); (e) ale keeps 3 percent a day in a market hall (granary class; was 15 percent, the house rate); (f) a merchant's lot comes down to 0.40 of Ref (was 0.75) so a glut sells at a loss instead of locking the merchant's cash; (g) the continent sells the materials the region's recipes consume (iron bar, leather, cloth, malt, charcoal...) when a book is short (it sold only goods no recipe makes: no smithy, tannery or cobbler had iron or leather), and imports when the book has orders for the good (EMA) even if it has never traded it; (h) the soak's price band counts a staple's book-days only where the book held some of it within 15 days (a village without a baker has no bread price; the old measure over every merchant book-day is printed beside it) | the overstocked grain *did* get cheaper (wheat 0.71 to 0.76 of base by year 10, 787 t in the halls with a target cover of 640 t): the curve and the 0.3 floor work. What kept grain unsold was buyers with no coin (I37) and a demand figure five times the real market; bread and ale sat at the 4 x cap for lack of supply, not of sessions (bread 1.2 t made in year 10, ale 0.4 kl: bakers had no firewood, the town's firewood was at 4 x while villages held 10,000 t at 0.3 x) |
| I37 | households/money | Spending above a reserve (`spending_rule` content, `content/base/economy/spending.jsonc`, `HouseholdSpending`, system `household-spending` in the H4 slice): once a season, on the day of the season given by its index, a household spends `spend_pct_per_season` of the coin it holds above `reserve_days` days of its members' food at the base grain price (a lord also keeps a month of his manors' wage bill; a priest's household spends the parish chest with its purse): wages to idle adults of households under 30 crowns (a labourer's 150 mites a day, at most 15 days each, the poorest first: its village, then its fief, then the region), works (`works_labour_pct` builders' wages to the same idle hands, the rest building materials ordered at the market), craft goods ordered at the market (cloth, clothes, shoes, candles, pottery, tools, ale, cookware...) and alms to destitute households (under 10 days of food or a crown a member; at most 10 crowns a member). Orders are filled at the next session at most 2 x Ref, delivered goods are destroyed (Worn, or Consumed for materials; hook kinds 3 and 4, saved with the order); a peasant buys at his village, the others in the market town. Roles and numbers: peasant 90 days, 25 percent a season (wages 30, works 20, goods 45, alms 5); burgher 90, 35 (25/25/45/5); gentry and lord 120, 45 (35/25/30/10); parish 60, 70 (15/35/15/35). `TransferReason.Alms` appended (saves with the shorter reason arrays load padded); the lords' almonry and the fiefs' dole now use it | historically the spending of lords and the Church on retainers, building, cloth, ale and alms carried rent and tithe back into the towns, and a household spent a fifth or more of what it had on cloth, tools and fuel; here the coin pooled with the priests, the top peasants, the masters and, later, a lord with a mine (31 percent of the coin in year 40 before the pools widened to the region), and the median household of every kind held nothing |
| I38 | households/markets/production | Supply where there was none: (a) the alewife: a landed peasant household with a fortnight of food brews for its village book when the book holds under half its ale cover (at most ten days of its own drink at a time, barley beyond its seed, the recipe's 520 g of barley and 350 g of fuel to a kg of ale, reason Brewed) and sells it at once; (b) the woodward: when the village book, or the market town's when it pays 15 percent more, holds under half its firewood cover, a landed peasant household cuts up to two days of its own fuel more and sells it there (reason Cut); (c) a farm household sells its grain and fuel in the nearest town when the town's mid price is 15 percent above its own book's (the cart and the day); (d) home brewing keeps a third of the seed reserve in barley (it brewed the seed: barley fell from 452 t to 82 t a year by year 10); (e) a stall with under 10 crowns and nothing on the book two seasons running closes and opens again under a new merchant with half his household's coin when its book has no solvent merchant left; (f) the soak counts a merchant solvent when its cash and its stock on the book at today's bid are worth 10 crowns (a stall has no debts; cash alone is printed beside it) | village ale was brewed and sold by alewives, firewood was cut on the commons and carted to town, and farmers took grain to the market town; a broke stall that never trades is a closed one |
| I39 | population | Economy worlds (the game and the soaks) marry brides from 20 and grooms from 22 (`VitalRates.EconomyDefault`; were 16 and 18; `VitalRates.Default` and the population tests keep the old ages) | the north-west European marriage pattern (brides in their twenties) is what held the English under-15 share near 30 percent; with brides from 16 it was 34-38 percent |
| I40 | abstract/markets | Hour 0 lightened (abstract spec E4, worst-hour budget): the store spoilage and the labour market run at hour 1, the parish at hour 2 and the continent trade at hour 3 of the day (each once a day as before, in the H4 phase after the slice); the market's daily globals, the farming day and the seasonal steps stay at hour 0 and the market sessions where they were. A producer's new goods join the lot it already has on a book (one lot per owner and good, at that lot's floor and listing day; there were tens of thousands of daily lots and every sale walked them), no merchant buys a good that loses 10 percent a day in a hall (bread, fresh meat and fish) for his stock, and the soak times the first day (JIT) apart | the slowest hours after the warm-up were hour 0 of a harvest day (farming 42, parish 36, market daily 24, continent 14, spoilage 11, labour 10 ms: 153 ms); the 329 ms the lead quoted was the first hour of the run, which compiles every system |

**Balance pass (decisions I18-I34), seed 1234, release build, `WorldTool soak`; the done-when targets are still NOT met.** Year by year (population / under-15 % / hungry household-days % / staples in band % / at the caps % / workshops finishing a batch % / merchants solvent %): **1**: 25,672 / 35.1 / 0.0 / 48.6 / 44.4 / 48 / 43; **10**: 25,336 / 33.1 / 6.2 / 36.9 / 45.8 / 21 / 24; **50**: 23,116 / 33.3 / 37.8 / 32.2 / 46.0 / 27 / 31; **100**: 18,852 / 34.7 / 36.5 / 26.3 / 46.9 / 20 / 51. The region holds within 10 percent for about 40 years, then loses ground in the famine years of this seed's weather (surplus -47 percent in year 60) and does not recover (births 670-870 against deaths 700-840); seed 1, 25 years: 25,971 -> 22,109. Every coin and mass audit is exact every year, coin 1,386,072 crowns throughout, sources equal the seed, sinks 0. What the flow trace (`soak --flow [--flow-years 1,10]`) showed: grain piles up in the market halls (790 t of wheat at the end of year 10, 650 t of it unsold all season) and at the lords while smallholders and town burghers have neither grain nor coin; flour sits unsold in the market (105 t) while bakeries bake 1 t a year; bread and ale stay at the 4 x cap because the households that would buy them brew and bake at home (I23) and the towns have no coin. Still off: workshops of the towns have no customers (non-food demand is a twentieth of a basket and the continent buys only raws), 430-570 of the 1,131 businesses end closed, bread and ale are 0.3 and 0 percent in band, staples at 26-32 percent in band, merchants solvent 20-51 percent. Not done: kin support, tally credit, a coarse daily mode for the economy systems (the region scheduler's Daily level still batches 24 hourly steps), quarry and clay pits. Timing: hourly median 1.9 ms, p99 26 ms, worst hour 329 ms (a season's first hour; the 150 ms budget is met by p99 only, the worst hour is the hour-0 daily globals plus a collection pause).

**Soak results** (seed 1234, release build, `WorldTool soak`): see the table the tool prints; the first game year is healthy (0.03 percent hungry household-days, population 25,963 -> 25,528, bread 28 t and ale 40 kl made, 52 percent of workshops finishing a batch, every audit exact); from year 2 the households that have no income from land, wages or dividends run out of coin and starve, and the region declines (seed 1234, `WorldTool soak --years 100`, 236 s on the build machine; rows are year: population / under-15 % / births / deaths / hunger deaths / hungry household-days % / staple price index wheat, rye, flour, bread, ale / workshops finishing a batch % / merchants solvent %: **1**: 25,528 / 35.4 / 629 / 608 / 0 / 0.03 / 1.00 1.04 1.99 3.25 2.77 / 52 / 28; **10**: 20,566 / 33.4 / 661 / 1,215 / 635 / 25.5 / 0.98 0.88 2.43 2.82 2.82 / 4 / 26; **50**: 10,917 / 32.4 / 313 / 525 / 254 / 26.0 / 0.58 1.51 3.10 3.13 3.13 / 1 / 24; **100**: 3,260 / 30.2 / 87 / 177 / 95 / 24.9 / 0.63 1.64 3.26 3.26 3.26 / 0 / 7; coin 1,386,450 crowns throughout with sources equal to the seed (1.08 x the households' seed money, the continent float included), sinks 0, every coin and mass audit exact every year; over the run 19.7 percent of household-days hungry, 18 percent of staple book-days in band, 64 percent at the clamps). Timing (release, 1 core): hourly abstract tick over the 100 years median 0.79 ms, p99 16.0 ms, worst hour 222 ms (hour 0 of a season, GC); 30 days of year 1 median 3.2 ms, p99 52.8 ms, max 96 ms; budget 150 ms. The plan's targets **are not met**: stable staple prices are met for flour, bread and ale only where the continent can pay; no collapse of the *economy* (bread and ale made, 100 years of exact coin and mass audits) is met, no collapse of the *population* is not. The reasons found, in order of weight: (1) the Phase 1 population has far more artisan households than domestic demand pays for (durables are 5 percent of a basket, there are no exports until caravans exist); (2) tenants on under 10 fields cannot feed a household after tithe, rent and seed and must buy grain with wages the economy does not pay; (3) the raw goods that masonries, smithies and tanneries need have no extraction business, so about 60 percent of the workshops idle; (4) the lords end up holding most of the coin (rents, crew dividends) and the retinue payroll they fund is the only wage the landless earn. Fixing these needs the taxes system (S4: hearth tax and treasury payroll close the money loop), quarry/clay/sand pits, caravans and a Phase 1 pass on how many artisans a town gets.
