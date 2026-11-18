# Caravans and logistics

Plan refs: §4A.1 (logistics: cart ~500 kg, ox wagon ~1 t, pack horse ~100 kg; merchants and guards are hired people; a trip
runs only if the expected margin beats its cost; an 8 km leg by cart takes about 18 game hours; tolls at gates and bridges;
caravan capacity in kg, not crowns), §4A.4 (caravans are carts owned by merchant Persons acting on prices seen or heard, with
an age; a lost caravan is lost capital; supply 1 kg of grain per man per day), §1 (speeds), §3c (rain, fords), §5 Phase 2
(caravans abstract on the road, physical in the bubble via the LODBridge; "caravans pass visibly through the region").

## 1. Purpose
Move goods between markets and regions as real owned capital: caravans are records with a master, crew, vehicles, cargo and a
float, planned from known (aged) prices, travelling a real road graph through real gates, paying tolls, risking ambush and
weather, and visible on the road when the hero is near. They are the pull-model counterpart of the buy orders in
`money-and-markets.md`.

## 2. Data

### 2.1 `CaravanTable` (~30-60 active per region)
`Id` (`IdKind.Caravan`, origin region), `Region` (current), `Master` (person), `OwnerHousehold`, `Account` (float + takings;
money spec `Caravan` account), `StockOwner` (cargo = a stock owner, goods as rows), `Crew[]` (person indices <= 12: drivers,
guards), `Vehicles[]` (`VehicleKind`, count), `CapKg`, `CapL`, `State` (`Planned, Loading, Travelling, Camping, Tolling, Waiting, Unloading, Returning, Lost`), `Plan` (`TripPlan`), `PathPlan`, `SegmentIdx`, `Origin`/`Dest` (settlement ids,
possibly in another region), `DepartHour`, `KnownPrices` (copy of the planning prices with ages), `ArrearsMites`.
`TripPlan`: `Legs[]` (`From`, `To`, `Route` as RoadEdge ids with directions), `Buy[]` (good, tier, grams, expected ask),
`Sell[]` (good, grams, expected bid at destination), `ExpectedProfitMites`, `DeadlineHour`, `GuardsWanted`.
`VehicleDef` (content `vehicles.jsonc`): `cart` {cap_kg 500, cap_l 1,000, speed_real_mps 2.5, draught: 1 ox or horse, crew 1
driver}, `ox_wagon` {1,000 kg, 2,000 L, 1.8 m/s, 2 oxen, crew 1}, `pack_horse` {100 kg, 250 L, 3.0 m/s, no draught, 1 handler
per 4}, `handcart` {150 kg, 300 L, 1.5 m/s}. Animals are `HerdTable` rows of the owner (borrowed into the caravan; eat fodder
8 kg/day while travelling: grain/hay from cargo or bought at camps).
Road graph: per-region `RoadNode/RoadEdge/RoadCrossing` (Phase 1) plus continental `Gate` nodes joined to the neighbour's
matching gate (seamless spec). `RoadClass` speed factors: Highway 1.0, Road 1.0, Track 0.8, Lane 0.7; off-road legs use
terrain factors (field 0.8, forest 0.5, marsh 0.4; roads required for carts, pack animals may leave roads).

### 2.2 Content
`vehicles.jsonc`, `roads.jsonc` (`class_speed_permille`, `toll_cart_mites` 25 at realm gates, `toll_cart_mites_bridge` 10,
`toll_permille_goods` 5 on cargo value at realm gates), `caravans.jsonc`: `min_margin_pct` 15,
`price_age_discount_permille_per_day` 10, `risk_premium_pct` 3 (+1 per 20 danger), `capital_cost_permille_per_day` 5,
`master_wage_mites_per_day` 350 (skilled), `driver_wage_mites_per_day` 150, `guard_wage_mites_per_day` 1,750 (plan: ~15-20
crowns/day danger premium; default 17.5), `guards_per_danger_x25`, `travel_window` rule, `ambush_*` keys.

## 3. Rules

### 3.1 Speed and the travel day
Speeds are the plan's real m/s; in game seconds `speed_gs = speed_real / 20` (cart 2.5 real m/s = 0.125 m per game second =
450 m per game hour; 8 km = 17.8 h). Caravans travel only inside the **travel window**: sunrise + 0.5 h to sunset - 0.5 h
(`Daylight` Phase 1: ~11 h at equinox, 15 h midsummer, 7 h midwinter), camping overnight at a settlement inn/yard if on the
road node, else at the roadside (camp risk x2). Weather multiplier on speed (Phase 1 `WeatherSample`): rain x0.7 (mud), heavy
rain x0.5 (carts may stick: a `Stuck` pause of `Range(1,4)` hours with probability 5%/hour), snow x0.6, deep snow/blizzard
x0.3 / stop, frozen river passable on ice in deep winter. **Ford closed** (`RiverHighDays > 0` at a ford crossing, Phase 1
`FordClosed`): wait at the node (`Waiting`) until it reopens or take a bridge detour if shorter than waiting; bridges are
never closed except by war/destruction.
Positions: `PositionOf(caravan, t)` is a pure function of its `PathPlan` (abstract spec 2.4). Each hourly step recomputes the
plan only when the effective speed changed (weather, window edge), starting from the current exact position and time, so the
function stays continuous and the promoted entities never jump.

### 3.2 Route choice (Dijkstra, integer)
Nodes: Phase 1 road nodes plus gates. Edge cost in game seconds = `length_mm / speed_mm_per_gs x 1000 / class_speed_permille`
plus toll equivalent `toll_mites x 3600 / value_of_time_mites_per_hour` (value of time = master + crew wages per hour). Ties
lowest edge id. A caravan between two detailed regions crosses the gate pair; through an ungenerated region it follows
`RegionEdge.LengthKm` as a numeric span (no road shape needed; positions inside are linear in that region's space). Paths
longer than 3 regions are replanned at each gate.

### 3.3 Trip decision (planner; runs at S8 each season for idle caravans/merchants and whenever a caravan unloads)
Candidates: for each good `g` with `ImportRequest` or price gap, each source market `s` (own settlement, neighbours within 3
regions by `Known` prices) and destination `d`:
```
known_p(d)   = Known price x (1000 - age_days x price_age_discount_permille_per_day)/1000        (aged price, never exact for remote markets)
sale         = exact integral at d (money spec 3.2) using d's *inferred* stock from known_p: s_d = T d (Ref/known_p)^2 ; revenue after bid and toll
purchase     = integral at s (exact when s is the home market; inferred otherwise)
transport    = (master + drivers + guards) wages x trip_days + animals' fodder + tolls + (cargo value x capital_cost_permille_per_day x trip_days) + risk_premium_pct x revenue
profit       = revenue_net - purchase - transport
run if profit >= min_margin_pct x (purchase + transport)   and   cash (Account + owner household share) >= purchase + 2 days' wages
```
Cargo is chosen greedily by `profit per litre-or-kg of binding capacity` (min of `CapKg` and `CapL / bulk_litres_per_kg`)
until the vehicle limits are met; mixed cargo allowed; perishables need `trip_days x spoilage <= 20%`. Capacity is **mass and
volume**, never crowns (closes the legacy defect). Two caravans cannot both plan the same last tonne: planners run in
ascending caravan id and reserve (`Reserved` on the source book's lot) quantities before the next planner sees them; an
unfilled reservation expires at the next barrier.
Back-hauls: on arrival the planner immediately plans the return leg from the destination's market (iron, cloth, salt, wool,
ale...). Empty returns happen and cost the margin.

### 3.4 Guards, danger and ambush
`danger(route) = max over edges of (Savagery of crossed continental cells + 10 x hideouts within 4 km + 25 if a recent raid in `News` + 15 at war)` capped 100. `GuardsWanted = clamp(floor(danger / 25) x ceil(vehicles / 4) , 0, 8)`; guard wages 1,750
mites/day make dangerous trade expensive (as designed). At each travelling hour (stream 0x230, key caravan id, draw = hour):
`p_encounter_ppm = 120 x (50 + danger) / 100` (night camp x2). On encounter roll `kind`: toll-taking patrol (30%),
merchants/refugees (30%, news swap), bandits (danger-weighted 0-40%), wolves/animals (10%). Bandit resolution (abstract;
replaced by the live fight if promoted):
```
p_win = guard_str / (guard_str + bandit_str)         guard_str = 4 x guards (+1 per veteran),  bandit_str = 3 x band size
win:  band loses 40-70% of members (population: deaths), caravan loses 0-10% of crew
lose: crew deaths = 30% (rounded up), cargo lost fraction = Range(50, 101)%, float coin lost; surviving caravan may retreat (State Returning)
```
Looted goods/coin move to the hideout's `Loot` stock and `Hoard` account (nothing is destroyed or conjured: "A lost caravan
is lost capital" but capital stays in the world, findable). Bandits are real persons (Phase 2 `BanditBuilder`: a band per
cave/hideout site of `SubKind` bandit, 3-8 vagrant adults, recruited only from `Homeless Vagrant` persons; killed members
stay dead; no respawn from nothing). Chronicle entry `Raid` with cause (the hideout, the danger inputs).

### 3.5 Tolls and tariffs
At each node/edge flagged toll (realm/fief gates, bridges; Phase 1 `RoadCrossing(Bridge)` and realm borders from `RealmMap`):
`toll = vehicles x toll_cart + cargo_value x toll_permille_goods / 1000`, paid `Caravan account -> treasury of the holder`
(taxes spec; reason `Toll`). Never at region borders (decision O5). Goods tolls apply once per realm crossing. Market tolls
(5% on seller proceeds) apply at the destination book.

### 3.6 Loading, unloading, settlement of accounts
- **Load** (State `Loading`, 2 game hours at the source, H1): buy per plan from the home book through market orders (merchant
  side), `Stock.Move` into the cargo stock; pay by transfer buyer account -> sellers (money spec). Wages are paid daily from
  the caravan account to crew households (`Wage` reasons), advances at departure for 3 days.
- **Unload** at destination: sell to merchants at bid or list lots; then settle: the master's household gets `net takings - wages owed - fodder` as `Dividend` of the owner; crew bonuses 5% of profit if positive. **No double payment**: coin is only
  ever moved by the two market trades, wages and tolls; there is no "caravan income" faucet (legacy: "caravans paid on both
  ends").
- Loss of a caravan: crew households lose earners; owner household bears the loss (capital); insurance does not exist in Phase 2.

### 3.7 Physical caravans (LODBridge)
When `PositionOf` enters a bubble circle: spawn per `Vehicles` entities at 12 m spacing behind the lead, drivers and guards
as promoted persons (`LiveBinding`), draught animals as animals, cargo as visible sacks/barrels per `StockOwner` composition
(representative, max 12 props). Encounters decided abstractly are played live; results via `CaravanLoss` command. The hero
may trade with the master (`Buy/Sell` against the caravan's cargo stock at the master's asking price: `ask = mid_at_next_market x 1.1`), rob, escort (Phase 3 contract), hire on as guard (wage 1,750/day). Caravans appear on the road
through the region at a visible rate: with 40 active caravans the hero sees one every ~20 real minutes on a main road at 1x
(test G-L12).

### 3.8 Migrants and army supply share the machinery
`MigrantGroup`s (population spec) are `PathPlan` carriers with `handcart/cart` capacities; army supply wagons (Phase 7) are
caravans with `Escort` roles; "1 kg grain per man per day, 1,000 men 15 days = 15 t = 30 carts" is a checkable capacity
example (G-L2).

## 4. Tick placement
- **H1** hourly: advance `PathPlan`, window/weather re-plan, tolls at nodes passed, encounter roll, camp/inn consumption,
  arrivals -> unload, load starts.
- **H5** hour 0: wage settlement day; stuck/waiting checks.
- **S8** seasonal: planner for idle caravans, new caravan founding (a merchant with `cash >= 2,500 crowns` buys a cart +
  animals when `ImportRequest`s persist 2 seasons), retirement of caravans with negative profit for 3 seasons.
- **Continent tick** C6: orders/offers from summaries (continent spec 3.6).
- **LODBridge**: promotion by position only.

## 5. Determinism
Planner order ascending caravan id; reservations by id; Dijkstra ties by lowest edge/node id; RNG `0x230` keyed `(hourKey, caravanId, draw)` and `0x231` for per-trip noise (departure jitter `Range(0, 3)` hours). `PathPlan` stored as integer
millimetres and game seconds. Gate pairs are canonical (seamless spec) so a caravan crossing a border keeps the same polyline
either side.

## 6. Interactions
Money/markets (orders, integral, tolls), households (wages, capital), population (crew deaths, bandits from vagrants), taxes
(tolls to treasuries), items (capacity, spoilage in transit `open`/`sealed`), continent (orders, news, flows), seamless
(gates, region handoff via mailboxes), weather, LODBridge, chronicle (raids, lost caravans, famine relief).

## 7. Edge cases
- Destination price collapsed on arrival: the master sells at bid anyway if `loss <= 20% of cost`, else waits `<= 3 days` for
  a better session, else carries to the next market (aged knowledge updates).
- Insufficient cash mid-trip for wages: crew arrears; 3 days -> desertion (guards leave; cargo stays with master at risk).
- Gate closed (war): caravan turns back or waits at the node `<= 5 days`; goods may be confiscated at hostile gates (Phase 6).
- Caravan enters an L3/L4 region: position becomes numeric (`Distance`), no ambush simulation beyond the abstract roll with
  the region's `danger` summary; regenerates on activation.
- Master dies: the senior crew member takes over (`Master` = lowest id), else the cargo is sold at the next node or abandoned
  (`Lost`).
- Weather freeze: an iced river on a winter route opens shorter paths (Phase 1 `RoadCrossing`/`ford`).


## Decisions made in this spec (collected in 00-overview.md section 10.4)
- **L1** Decision: Guard wage default is 1,750 mites/day (17.5 crowns). Reason: midpoint of the plan's 15-20 crowns/day.
- **L2** Decision: Caravans travel only in daylight (sunrise+0.5 h to sunset-0.5 h). Reason: plan: day length sets work hours; safety.
- **L3** Decision: Remote prices are discounted 1% per day of age. Reason: plan: act on prices seen or heard, with an age.
- **L4** Decision: A trip runs only if profit >= 15% of its cost. Reason: plan: expected margin must beat cost.
- **L5** Decision: Bandits are real persons recruited from vagrants; loot goes to hideout hoards. Reason: fixes 'bandits respawn from nothing' without destroying money (E15).
- **L6** Decision: Planners run in ascending caravan id and reserve cargo. Reason: deterministic competition for the same stock.

## 8. Golden tests
| id | Case | Expected |
|---|---|---|
| G-L1 | Cart, 8 km, equinox (travel window 06:30-17:30), depart 06:30 | speed 0.125 m per game s = 450 m/h; in 11 h covers 4,950 m, camps; next day 6.78 h more: **arrives day 2 at 13:17**; total elapsed 30.8 h, travel 17.78 h (plan: ~18) |
| G-L2 | Capacity: 30 carts | 15,000 kg = 15 t (plan's 1,000 men x 15 days); firewood cargo (2.5 L/kg) limited by volume to 400 kg per cart; ox wagon 1,000 kg / 2,000 L: grain 1,000 kg (1,333 L) |
| G-L3 | Trip decision: grain source `mid 30` (ask 32.4), destination `mid 80` (bid 73.6, 5% toll), 4 carts = 2,000 kg, master 350 + 2 drivers 300 + 3 guards 5,250 = 5,900 mites/day x 2 days, tolls `4 x 25` + 0.5% cargo = 424, capital cost 0.5%/day x 2 = 648, risk 3% of revenue | purchase 64,800; revenue 147,200 less toll 7,360 = 139,840; transport 11,800 + 424 + 648 + 4,416 = 17,288; **profit 57,752** = 70% of cost (82,088) >= 15% -> runs |
| G-L4 | The same trip with the destination price known 6 days old at 1%/day | expected mid 80 x 0.94 = 75.2 (bid 69.2); profit ~49,600, still runs; if the destination mid were only 45 (gap 15 mites/kg) profit is about -3,500 and the caravan does not run |
| G-L5 | Guards: danger 60, 4 vehicles | `floor(60/25) = 2` x `ceil(4/4) = 1` = 2 guards wanted; at danger 100 with 8 vehicles: 4 x 2 = 8 (cap) |
| G-L6 | Ambush win probability: 3 guards (str 12) vs band of 5 (str 15) | `12/27` = 0.444; over 100,000 seeded rolls frequency 0.444 +- 0.005 |
| G-L7 | Rain x0.7: 8 km cart | 315 m/h -> 25.4 h of travel; heavy rain x0.5 -> 35.6 h; ford closed 3 days adds 3 days wait |
| G-L8 | Route tie: highway 10 km (speed 1.0) vs track 8 km (0.8) | both 22.2 h; tie -> lowest edge id; a toll on one edge breaks the tie against it (value of time 350+300+5,250 = 5,900/24 per hour) |
| G-L9 | Conservation: 1,000 simulated trips | coin: `sum(accounts)` unchanged except loot moving to hoard accounts (counted) and mint events; goods: per good `Create-Destroy` unchanged except `Spoiled`/`Lost` into loot stock; no coin on both ends |
| G-L10 | Two caravans want the last 500 kg at source | lower caravan id reserves it; other plans 0 or chooses another good; identical in any input order |
| G-L11 | Position function: cart at `depart + 6 h` on a 3-edge route (3 km, 3 km, 2 km) | 2,700 m along: end of edge 1 is 3,000 m so still on edge 1 at 2,700; promoted entity matches within 1 mm |
| G-L12 | Visibility: reference region with 40 active caravans on 6 main roads | hero parked at a road segment sees >= 1 caravan per 30 real minutes at 1x on a highway-class road (statistical test over 30 days) |
| G-L13 | Gate handoff: caravan crosses a region gate | region column changes at the barrier via mailbox; its `PathPlan` position at the gate is identical in both regions' coordinates (seamless G-S3) |

## 9. Soak invariants
1. Caravans: 25-80 active in a detailed region in a normal year; > 90% of trips profitable at planning; < 3% of caravans lost
   per year at average danger.
2. Imports respond: a forced local grain shortage (drought) raises import orders and caravan count within 2 seasons and
   reduces the peak price below 4.0x within one more season (if a surplus region exists).
3. No cargo or coin appears/disappears (audit G-L9) over 100 years; lost cargo equals loot stock + hoards.
4. No caravan stuck `Waiting` > 10 days unless a gate is closed by war; no caravan with zero crew.
5. Bandit population never increases except by recruiting vagrants; bands with 0 members dissolve.
