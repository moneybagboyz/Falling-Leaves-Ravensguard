# The continent tick (seasonal)

Plan refs: §1B (abstract continent: regions never visited stay as continental numbers in the seasonal tick; world map shows
the whole continent; armies/caravans in the continent tick can cross into detailed regions through the LODBridge), §2.11
(seasonal continent tick <= 500 ms on a worker thread), §3c (weather fronts cover the continent, famines from real bad
years), §5 Phase 2 ("Continent tick v1 (trade, migration and news into the region)"), §4A.4 (caravans act on seen/heard
prices). Replaces nothing in legacy; it is new.

## 1. Purpose
Keep every region of the 16 x 16 continent economically alive without records: ~256 small summaries that evolve once per
season with closed-form versions of the region rules, exchange goods, coin, people and news with each other and with the
detailed regions around the player, and can be turned into full records (and back) at region activation.

## 2. Data

### 2.1 `RegionSummary[256]` (~280 B each; saved in the world section)
| Field | Type | Meaning |
|---|---|---|
| `Id` | byte | region id = `ry x 16 + rx` (seamless spec) |
| `Level` | enum | `L0 Live, L1 Detailed, L2 Daily, L3 Sleeping (generated, paged), L4 Abstract (never generated)` |
| `Pop`, `Households`, `Dwellings` | int | people, households, dwellings (vacancies = dwellings - households) |
| `AgeShare[3]` | short permille | 0-14, 15-49, 50+ (for catch-up) |
| `FoodMeasures` | long | grain-equivalent measures in all stores |
| `GrainCapacityNet` | long | measures per normal year (cells x fertility x acres; 3.2) |
| `CoinTotal` | long mites | coin held by households, businesses (not treasuries) |
| `CoinPerPerson` | int | derived |
| `PriceIdx[K=8]` | Fixed | mites/kg for grain, firewood, iron, cloth, ale, salt, tools, bread |
| `Cover[K]` | short x10 | days of cover per tracked good |
| `Wage` | short | labourer wage (mites/day) |
| `Prosperity`, `Unrest` | short 0..1000 | |
| `FamineDays` | short | consecutive days under famine threshold |
| `Realm[2]` | byte | realm ids (up to 2 holding parts of the region) |
| `WarFlags` | byte | at war / border closed (history `WarRecord` active) |
| `SilverMg` | long | stock of silver at its mint, if any |
| `Gates[8]` | struct | per neighbour (N, NE, ... NW): gate count, best `RoadClass`, length km, capacity |
| `News[8]` | ring | `(kind, magnitude, day)` of recent items |
| `Known[8][K]` | `(price, asOfDay)` | what this region last heard about each neighbour's prices |
| `LastDetailDay` | int | last day records existed (L0-L2) |
| `TreasuryCoin` | long | realm/fief chests homed here (taxes spec) |
Phase 1 sources: `Continent` layers (Fertility, Biomes, CoastDistance, Rivers), `WorldHistory` (settlement populations,
`TradeRoute`s as cell paths, `Treaty`/`WarRecord`), `ContinentalWeather`/`RegionWeather`.

### 2.2 Edges
`RegionEdge` (8 per region, symmetric): `A, B`, `Gates` (from history trade routes crossing the shared border; seamless
spec), `Class`, `LengthKm` (centre to centre along the route), `CapacityKgPerSeason`, `Closed` (war/border). Caravans between
L0-L2 regions use real gates; L3/L4 edges are numbers.

### 2.3 Content: `content/base/economy/continent.jsonc`
`grain_per_person_year_measures` (60 x 0.92 = 55.2), `spoilage_pct_per_season` (5), `target_cover_days` per tracked good,
`birth_rate_pct_per_year` (2.58), `death_rate_pct_per_year` (2.58 at food 1.0), `house_cap_persons_per_dwelling_x10` (48),
`famine_threshold_days` (3), `emigrate_pct` normal (0.5) / famine (3.0), `edge_capacity` by `RoadClass` (carts x trips),
`news_speed_m_per_gs` (0.25), `export_surplus_cover_x` (1.3), `import_deficit_cover_x` (0.7).

## 3. Rules (each season tick, ascending region id; integer arithmetic)

### 3.0 When it runs
At hour 0 of doy 1, 16, 31, 46 **after** every active region has finished its own seasonal (S) steps and exported its summary
(C1); on a worker thread; the result is applied at the same barrier (no region record is touched except through mailboxes).
Order: C1 -> C2 -> C3 -> C4 -> C5 -> C6 -> C7 -> C8 -> C9 -> C10.

### 3.1 C1: summaries of detailed regions
For L0-L2 regions copy aggregates out of the records: `Pop`, `Households`, `Vacancies`, food measures (sum of staple stocks x
measure-eq), coin sums, prices (the books' `mid` for the 8 key goods, population-weighted), wages, unrest, famine days,
surplus/deficit. These regions are not simulated further here; they *receive* flows (3.6) as caravan orders and migrants and
*send* theirs.

### 3.2 C2: production closed forms (L3, L4 only)
`GrainCapacityNet` is fixed at world generation by `Capacity = sum over land cells of arable_acres(cell) x (2/3) x net_per_sown_acre(fertility)`, with `arable_acres` limited by `pop target x 2.5 acres` and cell fertility (the same constants
as farming: 48 x min(f,91)/70 x 0.8 net, mixed crops factor 0.965 from crop mix). Per year weather factor `w` (autumn tick,
doy 31): sample 4 central cells of the region: `ratio = rain_window / normal` (as farming 3.3) -> `rain_f`; frost/hail/blight
ignored in closed form except the region-level `LongCondition` (drought/wet/cold) of the culture-year and a hail/blight
scalar from stream 0x250 (mean 0.99). `Harvest = Capacity x w`. The **calibration test** compares this with a detailed region
over 20 years (mean within 5%, correlation of annual factors > 0.9).
Other goods: `PriceIdx` for non-grain goods follows `cover` by the same price curve (money spec 3.1) with `cover` evolving by
production capacity (population x per-capita ratio of trades from region type: mining, coastal, forested) minus consumption;
coarse by design (Phase 2: 8 tracked goods).

### 3.3 C3: food and stocks per season (L3/L4)
```
if season == autumn:  Food += Harvest                              (once a year)
Food = max(Food - Pop x 15 x grain_per_day_x100 / 100, 0)           (grain_per_day = 0.92 measure)
shortfall = max(consumption - available, 0)  ->  FamineDays += 15 if shortfall > 0 else 0
Food = Food x (100 - spoilage_pct) / 100
Cover[grain] = Food / (Pop x 0.92)
PriceIdx[grain] = base x clamp( sqrt(30 / Cover), 0.3, 4 )          (target cover 30 days)
```
### 3.4 C4: demography per season (L3/L4)
```
food_mult   = 1000 if Cover >= 20; 800 if >= 8; 500 if >= 3; 200 else          (fertility factor)
births      = Pop x birth_rate_pct x food_mult / (4 x 100 x 1000)
mort_mult   = 1000 + 20 x (shortfall share of demand in permille) ... capped 3000  (starvation)
deaths      = Pop x death_rate_pct x mort_mult / (4 x 100 x 1000)
Pop        += births - deaths;  clamp to Dwellings x house_cap_persons_per_dwelling_x10 / 10
```
Integer, floor, with the remainder carried per region (`PopRemainder`). Result is in the stationary-table band by
construction: at `food_mult 1000, mort_mult 1000`, Pop is constant (births = deaths = 2.58%/yr) — the closed-form twin of the
detailed 100-year invariant.

### 3.5 C5: coin
`CoinPerPerson` is held by trade balance only (C6); L3/L4 regions neither mint nor sink except through realm mints (C9).
Hearth tax (taxes spec) is collected into `TreasuryCoin` of the realm region: `Households x hearth_tax_mites x 0.9` per
season (evasion factor 0.9) from `CoinTotal`, and spent back as stipends/wages into the same region and the realm seat
(`Households x 0.3 x hearth`), so closed-form regions neither drain nor inflate (test).

### 3.6 C6: trade flows between regions
For each good class tracked, build candidate exports: region with `Cover > export_surplus_cover_x x T` (1.3) offers `surplus = (Cover - T) x demand`; importers with `Cover < import_deficit_cover_x x T` (0.7) want `deficit = (T - Cover) x demand`.
Process edges in ascending `(price gap desc, A, B)`; for each edge not `Closed`: `flow = min(surplus, deficit, edge_capacity)`; price `p = (PriceIdx_A + PriceIdx_B)/2`; `payment = flow_kg x p` moves from B's `CoinTotal` to A's (capped
by B's coin; a poor importer imports less: `flow = coin / p`). Updates stocks/covers and `Known` price knowledge on both
sides. `edge_capacity` per season by class: Highway 120 carts, Road 60, Track 20, Lane 6 (each 500 kg x trips per season = 15
days / round trip days; a 32 km hop = 3 days one way -> 2.5 round trips, so Road = 60 x 500 x 2.5 = 75 t = 68,181 measures
per season, matching the caravan spec); goods needing preservation (fresh) never flow.
**Between a detailed region and a summary region the flow is realized**: the detailed region's caravan planner receives an
*order* (`ImportOrder(good, qty, price_cap)` / `ExportOffer`) valid for the season; real caravans carry it to/from the gate;
on arrival the summary side is credited/debited. Unfulfilled orders expire at the next tick (no queue growth).
**Between two detailed regions** flows are real caravans on real roads (caravans spec); the continent tick only adds orders
where real surpluses exist (it uses the detailed summaries).
Gold/silver bullion moves only with mints (C9).

### 3.7 C7: migration
`emigrants = Pop x emigrate_pct / 100` (0.5% per season normally, 3% in famine, +1% with `Unrest > 700` or `WarFlags`),
capped by neighbour vacancies x 4.8 summed over edges weighted by `score = 10 x vacancies + 3 x wage + 2 x cover_index - distance_km - 5 x unrest` (population spec 3.7), distributed by descending score with ties lowest region id. Summary regions
exchange numbers; a detailed destination gets **MigrantGroups**: the continent tick posts `Immigration(n households, template, gate)` and the region instantiates synthetic households (population spec 3.7; ids from the destination allocator,
E9) whose coin and stock are scaled from the source's per-capita values (conserved: coin leaves the source `CoinTotal`).
Emigrants from a detailed region are real records (population spec 3.9) and appear in the neighbour summary as
`Pop`/`CoinTotal`/`Households` increments.

### 3.8 C8: news
`News` items (famine, plague/outbreak, harvest failure, bandit raid, war start/end, price spike, mine flood) are pushed to
neighbour rings (`age = LengthKm x 1000 / news_speed_m_per_gs / 86,400` days: 32 km at 0.25 m per game s = 1.48 days) and
relayed up to 3 hops (`age` accumulates). Caravan masters and merchants in detailed regions read `Known` for prices and
`News` for risks; rumours (Phase 3) draw from the same ring. Region-level `Known` entries update when real caravans arrive
(exact) or from news (aged).

### 3.9 C9: mints and silver
`Mint` regions (one per realm capital region) own `SilverMg`. Silver mined in L3/L4 regions by closed form (`mine tonnage x staff estimate x 0.5 t/day x 60 days x silver_grade`, only for `Active` veins; staff = 0.3% of `Pop`), minted at the realm
mint: `struck_crowns = floor(SilverMg / 1300)`; `seigniorage` to `TreasuryCoin`; remainder carried. A 100-t vein at 3,000 g/t
gives 230,769 crowns (resources G-R12): the closed form must produce the same number when the vein is exhausted (test).

### 3.10 C10: level management hooks
Computes `Desired Level` per region from the active bubbles' distances (seamless spec) and posts
`Activate(region)`/`PageOut(region)` requests applied at the barrier; sets `LastDetailDay`.

## 4. Tick placement
Worker thread at season start barrier after all regions' S9 export; budget table:
| Step | Cost model | Typical |
|---|---|---|
| C1 copy summaries (<= 25 detailed) | 25 x ~30 us | 1 ms |
| C2 weather samples: 256 regions x 4 cells x (rain, temp) x 15 days | 30,720 calls x ~4 us | 120 ms (cached per cell-season; second call free) |
| C3-C5 per region closed forms | 256 x ~5 us | 2 ms |
| C6 flows: ~1,000 edges x 8 goods | 8,000 x 1 us | 8 ms |
| C7-C10 | | 5 ms |
| **Total** | | **~140 ms typical, hard cap 500 ms** |
The weather cost is the risk: `RegionWeather.DailyRainMm/DailyMeanTemp` results are memoised per `(cell, day)` in a bounded
cache (2 years), cleared at year end.

## 5. Determinism
Regions processed in ascending id; edges in ascending `(price gap, A, B)` with integer keys; flows use integer division with
remainders carried per region; RNG streams 0x250 keyed `(seasonIndex, regionId, draw)`; **the result of a region's summary
never depends on which other regions are resident**, only on the previous summaries and shared inputs (weather is a pure
function). Therefore any visit order gives identical summaries until a region is activated; activation is a state transition
(seamless spec), logged in the save.

## 6. Interactions
Seamless regions (levels, activation, paging), caravans (orders and gates), population (immigration/emigration records),
taxes (realm treasuries), farming (closed-form calibration), markets (`Known` prices, price indices), weather (Phase 1),
history (settlement pops, routes, wars), chronicle (continent-scale events with causes), world map UI (reads summaries).

## 7. Edge cases
- Region with `Pop = 0` (abandoned/sea): skipped; `Gates` closed.
- Coastal/island regions have no edges to some neighbours; sea trade is Phase 7 (ships); coastal edges between regions with a
  shared sea cell use `Class = Lane` equivalents at 10 t per season.
- Region at the continent edge: fewer than 8 neighbours.
- Closed borders at war: `Closed`; caravans in flight are `Lost` or turned back (caravans spec); prices rise by the cut flow.
- Overflow: coin and food are `long`; `Fixed` price math uses clamps; no division by zero (`Pop` min 1 in formulas).
- Large region with several realms: tax split by the realm map (`Realm[2]`), 50/50 by Phase 1 `RealmMap` tile shares
  (precomputed).


## Decisions made in this spec (collected in 00-overview.md section 10.4)
- **CT1** Decision: Unvisited regions use closed-form versions of the region rules, calibrated to detailed regions within 5%. Reason: cheap, and consistent on activation.
- **CT2** Decision: Trade flows are matched greedily by price gap with road-class capacities. Reason: deterministic and simple; no solver.
- **CT3** Decision: News travels at a courier pace of 0.25 m per game second and relays up to 3 hops. Reason: prices known with an age (plan §4A.4).
- **CT4** Decision: Flows with a detailed region are realised as import/export orders served by caravans. Reason: visible caravans, no teleporting goods.
- **CT5** Decision: The continent tick tracks 8 goods and a price index only. Reason: budget and Phase 2 scope.

## 8. Golden tests
| id | Case | Expected |
|---|---|---|
| G-C1 | Food loop, `Pop 25,000`, harvest 1,542,195 measures (farming G-F15), consumption `25,000 x 15 x 0.92` = 345,000 per season, spoilage 5%, start 100,000 before autumn | autumn end 1,232,335; winter 843,068; spring 473,165; summer 121,757; the steady state after 30 years: seasonal stocks 1,343,011 / 948,110 / 572,955 / **216,557** |
| G-C2 | Hungry-gap price from G-C1 steady state: cover `216,557 / (25,000 x 0.92)` = 9.4 days | `sqrt(30/9.4)` = 1.79 x 45 = **80.3 mites/kg**; post-harvest (cover 58 d): 0.717 x 45 = 32.3; seasonal ratio 2.5 |
| G-C3 | Drought year (harvest x 0.475 = 732,543) from the steady state | stocks after seasons: 573,894 / 217,449 / **0 / 0**; shortfall in spring+summer 472,551 measures (34% of annual need) -> `FamineDays` 30, deaths multiplier rising, emigration 3% |
| G-C4 | Flow: A surplus 200,000 measures, B deficit 150,000, Road edge 68,181 measures per season; prices A 22.5, B 135 mites/kg | `flow = min(200,000, 150,000, 68,181)` = 68,181 measures = 75,000 kg; `p = 78.75`; payment **5,906,250 mites (59,062 crowns)** from B to A; conservation: `sum(CoinTotal)` unchanged |
| G-C5 | Migration: famine region Pop 25,000 (3% = 750), neighbour with 120 vacancies | capacity `120 x 4.8` = 576 -> 576 move, 174 stay (vagrants or next neighbour by score) |
| G-C6 | News age along a 32 km edge at 0.25 m per game s | 128,000 gs = 35.6 h = **1.48 days**; two hops 2.96 days |
| G-C7 | Demography closed form, `food_mult 1000`, `Pop 25,000` | births = deaths = 161.25 per season (25,000 x 2.58% / 4); `PopRemainder` carries 0.25 |
| G-C8 | Dwelling cap: Dwellings 5,200 | `Pop <= 24,960`; a growing region stops at the cap |
| G-C9 | Visit-order independence: tick 40 seasons with region 17 resident vs paged vs never visited | summaries of all other regions identical hashes |
| G-C10 | Calibration: closed form vs detailed region, 20 years, same weather | mean annual net grain within 5%; population year-end within 8%; correlation > 0.9 |
| G-C11 | Budget: 256 regions, 25 detailed | <= 500 ms (typical 140 ms) on the reference runner; second tick in the same season day is a cache hit <= 20 ms |
| G-C12 | Silver: vein 100 t at 3,000 g/t closed form | 230,769 crowns struck once the vein is exhausted; matches detailed (resources G-R12) |
| G-C13 | Coin conservation: 100 seasons of trade among 9 summary regions | `sum(CoinTotal + TreasuryCoin)` = initial + minted (mint tests) exactly |

## 9. Soak invariants
1. 100-year continent soak (all 256 regions L4): every region `Pop` within +-15% of start (+-25% for regions that experienced
   famine), no negative stocks or coin, `sum(coin)` conserved except mint/hoard, prices in [0.3, 4.0] x base, no region
   extinct.
2. Mixed soak (9 regions detailed, rest summary) for 20 years: exported flows equal imported flows plus losses; no stock
   created at edges.
3. Famine in a summary region has a recorded cause (weather factor, war flag) >= 90% of cases.
4. Tick time p99 <= 500 ms on the reference runner; no allocation spikes > 50 MB.
