# Industries: staged production chains, tiers 1-3, crews, historical output, real inputs, the Industry tab, selling, payback

Status: design, written before any Phase 7 code, on top of b6b39e2. Plan refs: the locked Industries row (staged chains of real buildings, each stage its own building with its own crew; an Industry tab in the Ledger with throughput per stage, the bottleneck, crews, wages and the next upgrade; tier upgrades as physical one-click plans built hands-on, the level recognised from what is installed, each tier with a minimum and maximum crew; medieval technology; historical output plus an honest 1x-5x "industry output" slider; real inputs: charcoal from charcoal burners who cut woodland, firewood for kilns, barrels and crates from coopers; a shortage stops the stage and the tab says why; a well-run Level 1 works repays its buildings in about 2-3 game years at default settings); §5 Phase 7 Industries (the three chains; storage, loading and packing as real stages with hauling that can bottleneck; packed goods cart better and spoil less; the tiers per stage; output from the labour model with diminishing returns from crowding; selling at the gate or by your own caravan with a caravan master, guards, wagons, a destination and the expected margin from known prices with the road's risk; distant towns pay more; flooding a market drops its price; the sheets' numbers as targets for structure; raw output barely covers wages, finishing is where the profit is; output per worker historical: a miner about 0.5 t of ore a day, a pair of sawyers 100-200 board feet; inputs real; upkeep real repair work; the balance sheet per day and per 6-day week in one coin unit).

Money in mites (1 cp = 10, 1 sp = 100, 1 gp = 1,000; `coinage.md`); the balance sheet's one unit is silver with one decimal. Wages: unskilled 150, skilled 350, master 500 a day. A game year is 60 days; a working week 6 days. Rates are at skill 50, full stock, clear weather, the slider at 1x.

**About the user's sheets.** The iron, clay and wood design sheets are not in the repository. The plan records what they set (stage order, tier jumps, crew ranges, stage ratios, "raw output barely covers wages"); this spec follows that structure, rescaled to historical rates and the game's money. The tables below should be checked against the sheets (INQ-1).

## 1. Purpose
Let the player build an industry the way it was built: one building per stage, each with its own crew, linked by hauling, fed by real inputs from other chains, grown by bigger crews and machines installed by hand, and judged on a balance sheet that shows where the coin goes. The same stage rules give the region's generated mines, pits and woodlots their honest rates.

## 2. What exists and what is new
| Exists (by file) | Phase 7 |
|---|---|
| `Production/Business.cs`, `ProductionSystem` (batches, extraction, staff, wages, stores), `Production/OwnershipWorld*.cs` (founding, stewards, the Ledger), `Production/Resources/*` (`ExtractionKind`, ore bodies 2,000-20,000 t, depletion, mine floods `new_mine_flood_pm`), `Prospecting/*` (surveys, licences, royalty 10%) | extended: a stage is a business of a new stage kind; a works is a group of stages (4.1) |
| `content/base/production/businesses.jsonc` (`mine`, `woodlot`, `charcoal_crew`, `clay_pit`, `sawpit`, `water_sawmill`, `brickworks`, `bloomery`), `extraction.jsonc` (`mining_iron` 500 kg a worker-day, `clay_digging` 2,000, `woodcutting` 1,050) | generated kinds keep running, at the composite stage rates (5.12) |
| `crafting/recipes_business.jsonc` (`smelt_bloom` 40 kg ore + 60 kg charcoal -> 6 kg bloom in 10 h; `forge_bar`; `burn_charcoal` 100 kg logs -> 20 kg in 12 h + 72 h cure; `saw_planks` 100 kg logs -> 70 kg planks + 25 kg sawdust in 10 h; `fire_bricks` 100 kg clay + 40 kg firewood -> 80 kg bricks; `make_barrel`) | kept as the stages' recipes; new recipes for dressing, tempering, moulding, drying, packing, crates |
| `Construction/*` (plans, ghosts, hands-on building, construction contracts, `PlanFitter`), `Building/RoomDetectorV2` | the tier plans (5.3) and tier recognition (5.2) |
| `economies-of-scale.md` (crews, shared costs, contracts, carting, patterns, limits), `large-buildings.md` (power, machines, rooms, plans) | used throughout |
| `Caravans/*` (`CaravanPlanner`, `CaravanStep`, `CaravanAmbush`), `Trade/PriceNotebook`, `ContinentDesk` (the gate's export desk) | the works' own caravan and gate sales (5.10) |
| `Production/OwnershipViews.LedgerTab` (Property ... Stewards, Fief) | `Industry` appended |
| **New** | `Production/Industry/` (`ChainKind`, `StageKind`, `Works`, `StageRuntime`, `TierRecognition`, `Hauling`, `PackedStock`, `BalanceSheet`, `OwnCaravan`, `IndustryRates`), `Save/IndustrySave.cs`; content `production/industry/{iron,clay,wood,charcoal,cooperage}.jsonc`, `build/pieces_industry.jsonc`, `blueprints/industry_tiers.jsonc`; Godot `UI/IndustryTab.cs` |

## 3. Concepts and data

### 3.1 Content
- **`ChainKind`**: id, name, ordered stage kinds, the goods that flow between them.
- **`StageKind`**: id, chain, the goods in and out, and per tier (1-3): `plan` (a blueprint id), `recognise` (pieces and rooms that must be installed, 5.2), `crew_min`, `crew_max`, `rate` (per worker-day: kg handled, or recipe units), `capacity` (per day, from the installed stations and machines), `power_w`, `store_kg` (the stage's own store), `grades_pm`, and the stops it can suffer.

### 3.2 Saved state
- **`Works`**: `{ Id, Owner (household or business), Chain, Stages: [business id], Links: [(from stage, to stage, distance m, mode: crews, barrows, carts, crane)], Routing: per stage output good (NextStage, SellGate, Contract id, Caravan, Store), CrewTarget and WageOffer per stage, BuildingsCostMites (the construction spend recorded as each plan completes), CumulativeNetMites, StartedDay, PaidBackDay }`.
- **Each stage is a `Business`** (one building, its own staff and store) of a stage kind (`stage_iron_mine`, ...; 17 kinds) whose cash box is the **works' one purse** (a business's `Purse` already names a purse; the stages of a works name the same one). Internal moves are store moves at no price; the balance sheet values them at book for display only. One purse keeps the coin audit simple and the steward's reserve one number.
- **`BalanceRing`** per stage: the last 60 days of income, wages, inputs, upkeep, royalties and rents (mites), for the sheet and the payback.
- **`PackedStock`** (5.9) and **`OwnCaravan`** rows (5.10).

## 4. The stage runtime

### 4.1 A stage's day
At its H4 slice (Phase 2's daily business slice) each stage computes its output for the next working day:
`out = min(capacity_tier, sum over crew of rate_tier x speed(skill) x eff x span x power) x slider / 100`,
in integer permille steps: `speed(skill)` is the Phase 2 rate by skill (1,000 at 50), `eff` the crowding factor and `span` the span-of-control factor (`economies-of-scale.md` 5.1, 5.2), `power` the machine share (`large-buildings.md` 4.1; 1,000 when the tier has no machine). The stage then works through the day's hours like any business (batches at stations, or kg moved for handling stages). **Inputs** come from the stage's store, filled by the link from the stage before it, by contracts or by the market; **outputs** go to the stage's store and on by its routing.
**Crew:** a stage with fewer than `crew_min` hands does nothing ("under-crewed"); hiring stops at `crew_max`; the crew slider sets the target between them and the labour market fills it at the wage offer (Phase 2 rules).
**Spare hands:** a stage that is blocked or done before the day ends lends its idle hours to the works' yard, packing and shipping stages at their tier's rate. At tier 1 the yard and packing stages may have `crew_min` 0 and run on spare hours only.

### 4.2 Stop reasons (each shown on the tab in plain words, with the good or cause)
`NoInput(good)`, `NoFuel(good)`, `Full` (the next stage's store or the stage's own store is full), `UnderCrewed`, `NoPower` (frozen, dry, sluice shut), `Broken` (a machine at 0 hp), `Flooded` (a mine's flood day without pumps), `Winter` (open drying hacks), `Unpaid` (wages owed beyond Phase 2's 3 short days), `NoLicence`, `Depleted` (the ore body or the woodland). A stage stopped more than 3 days writes `works_stopped` once.

### 4.3 Links and hauling between stages
Within 60 m, stages pass goods by the crews' own hands (counted in the stage's handling). Beyond 60 m a link needs carts or a runway: `trips = floor(10 h / (2 x d / 450 m an hour + load_h))` a cart a day (a cart covers about 450 m a game hour; loading 1 h a trip, 0.5 h for packed goods or with a crane), `kg = trips x 500` a cart (1,000 an ox wagon). A link short of capacity is the bottleneck like any stage ("hauling: 1,500 of 2,100 kg a day").

## 5. Rules

### 5.1 The three chains (tier tables)
Rates are per worker-day unless said. Prices for the worked examples are the audit's candidates (`economy-audit.md` 4.8).

**Iron: mine -> crusher and washer -> ore yard -> smeltery -> packing.**
| Stage | Tier and plan | Installed (recognised by) | Crew | Rate and capacity | Inputs and notes |
|---|---|---|---|---|---|
| Mine | L1 hand-hauled | adit or shaft to 2 levels, ladders, a windlass, baskets | 2-8 | a hewer 500 kg of `iron_ore_rough`; 1 haul hand per 2 hewers | flood days stop it |
| | L2 carts on wooden rails | + `mine_rail` on each worked level, 2 `hund` carts | 6-20 | hewer 500; 1 haul hand per 4 hewers; 4 levels | |
| | L3 horse gin or water hoist and pumps | + `horse_gin` or a powered `hoist_drum` and `rag_pump` | 12-40 | hewer 500; 1 haul hand per 8 hewers; 8 levels; no flood days | gin: 2 horses' fodder; water: 2,000 W |
| Crusher and washer | L1 hand crushing, a buddle | `crushing_floor`, `buddle` | 1-6 | 400 kg rough | 1,000 kg rough -> 600 kg `iron_ore` + 400 kg tailings (`DestroyReason.Waste`) |
| | L2 crusher and washing floor | + `crusher_rolls` (horse) , `washing_floor` | 3-10 | 1,000 kg rough | 1 horse |
| | L3 water stamps | + powered `stamp_battery` | 2-8 | 4,000 kg rough; 6 t a day a battery | 2,500 W a battery |
| Ore yard | L1 hand loading | a fenced yard, shovels and baskets | 0-3 | 4,000 kg handled; store 40 t | spare hands at L1 |
| | L2 barrows and plank runways | + 20 m `plank_runway`, 2 barrows | 1-4 | 8,000 kg; store 120 t | |
| | L3 treadwheel crane | + `treadwheel_crane` | 2-6 | 20,000 kg; store 300 t | 2 walkers in the crew |
| Smeltery | L1 one furnace | 1 bloomery, hand bellows | 2-5 | a unit (40 kg ore + 60 kg charcoal -> 6 kg bloom) per 10 worker-hours; 5 units a day | charcoal |
| | L2 two furnaces and a finery | 2 bloomeries, foot bellows, `finery_hearth` | 4-14 | 8 h a unit; 6 units a furnace (12); finery `forge_bar` 6 kg bloom -> 4.8 kg bar | charcoal (shared heat at the finery) |
| | L3 three bellows furnaces and a hammer | 3 bloomeries with powered `great_bellows`, a powered `trip_hammer` | 6-18 | 5 h a unit; 12 units a furnace (36); bar at 3 h a batch | 1,000 W a furnace, 2,000 W the hammer |
| Packing | L1 by hand | a bench | 0-2 | 400 kg; crates of 100 kg | crates |
| | L2 packing press and banding | + `packing_press` | 1-3 | 1,000 kg | crates, iron bands |
| | L3 loading bay with a crane | + crane at a loading bay | 1-4 | 2,500 kg; loading 0.5 h a cart | |

**Clay: clay pit -> yard -> moulding shop (pots, bricks, shingles) -> drying sheds -> kiln -> packing.**
| Stage | Tier | Installed | Crew | Rate and capacity | Notes |
|---|---|---|---|---|---|
| Clay pit | L1 spades and barrows | the pit, a ramp | 2-8 | 2,000 kg of clay | |
| | L2 runways out of the pit | + `plank_runway`, barrows | 4-12 | 3,000 kg | |
| | L3 bucket hoist and drainage | + `horse_gin` hoist, `rag_pump` | 6-24 | 4,000 kg; no wet-day stops | |
| Yard (weathering, tempering) | L1 treading by foot | a treading floor | 1-6 | 1,500 kg tempered | clay dug in autumn weathers over winter (5.6) |
| | L2 horse pug mill | + `pug_mill` with a gin | 2-6 | 6,000 kg | 1 horse |
| | L3 water pug mill | powered `pug_mill` | 1-4 | 15,000 kg | 1,500 W |
| Moulding shop | L1 one table | 1 `moulding_table` | 2-4 | 1,000 bricks (2.8 kg green each) a table-day with a moulder and 2 hands; or 60 pots a potter's wheel; or 800 shingles | grades 50/150/800 |
| | L2 three tables | 3 tables (+ a potter's wheel) | 6-12 | 3,000 bricks | |
| | L3 six tables and a tile press | 6 tables, `tile_press`, 2 wheels | 12-24 | 6,000 bricks | |
| Drying sheds | L1 open hacks | `drying_hack` rows, straw covers | 1-3 | 15,000 bricks drying; 15 days spring to autumn; none in winter (frost) | straw 20 kg a day |
| | L2 roofed sheds | a `drying_shed` room | 2-5 | 30,000; 12 days; winter at half rate | |
| | L3 hot floors | + `hot_floor` fed by the kiln's waste heat | 2-6 | 60,000; 6 days all year | |
| Kiln | L1 clamp | `brick_clamp` | 2-6 (setting and firing) | 20,000 bricks (50 t) a firing, 10-day cycle; firewood 5,000 kg + 0.4 kg a kg | losses 3-10% a firing (0x742) |
| | L2 two Scotch kilns | 2 `scotch_kiln` | 4-10 | 24,000 each, 8 days; 4,000 kg + 0.3 kg a kg | losses 2-5% |
| | L3 three kilns with draught | 3 kilns, `kiln_bellows` | 6-14 | 24,000 each, 7 days; 3,500 kg + 0.25 kg a kg | losses 1-3% |
| Packing | L1 stacking on straw | - | 0-2 | 3,000 kg | straw; pots in crates |
| | L2 pallets and crates | `packing_press` | 1-3 | 6,000 kg | |
| | L3 crane at a loading bay | crane | 1-4 | 12,000 kg | |

**Wood: felling -> hauling -> timber yard -> saw mill -> packing -> shipping.** (1 board foot = 2.36 L; planks at 2.2 L a kg: 1 bf = 1.07 kg.)
| Stage | Tier | Installed | Crew | Rate and capacity | Notes |
|---|---|---|---|---|---|
| Felling | L1 axes | - | 2-12 | 1,050 kg of logs (1.5 m3) | woodland stock 100 m3 an acre; coppice 5 m3 an acre a year |
| | L2 crosscut saws and felling teams | `crosscut_saw` tools | 4-16 | 1,400 kg | |
| | L3 timber chutes | + `timber_chute` on slopes | 6-24 | 1,750 kg | |
| Hauling | L1 dragging with oxen | an ox team and chains | 1-4 drivers | 3,000 kg a team-day over 2 km | 2 oxen a team |
| | L2 timber carriage | `timber_carriage` (vehicle template) | 2-6 | 6,000 kg a carriage-day | 4 oxen |
| | L3 floating | a `log_boom` on a river | 2-8 | 20,000 kg a raftsman-day downstream | a `river` class water |
| Timber yard | L1 hand rolling | a fenced yard, a seasoning shed | 0-2 | 4,000 kg; seasoning store 60 t | |
| | L2 skids and runways | + skids, `plank_runway` | 1-4 | 8,000 kg; 150 t | |
| | L3 treadwheel crane | + crane | 2-6 | 20,000 kg; 400 t | |
| Saw mill | L1 pit saws | `saw_pit` stations | 2-8 (pairs) | a pair 150 bf (161 kg of planks) a day from 230 kg of logs: 75 bf a worker | 70% planks, 25% sawdust and slabs |
| | L2 frame saws | `frame_saw` in a saw shed | 3-12 | 150 bf a worker (x2) | |
| | L3 water gang saw | powered `gang_saw_frame` | 2-8 | 600 bf a worker (x8) | 3,000 W |
| Packing | L1 bundling | withies | 0-2 | 2,000 kg | |
| | L2 banding bench | `banding_bench` | 1-3 | 4,000 kg | iron bands |
| Shipping | L1 cart bay | a loading bay | 0-2 | 4,000 kg loaded | spare hands |
| | L2 a jetty for barges | `jetty` on navigable water | 1-4 | 10,000 kg | |
| | L3 a jetty with a crane | + crane | 2-6 | 25,000 kg | |

**Input chains.** *Charcoal* (felling -> clamps, `charcoal`): L1 earth clamps (`burn_charcoal`, 5 kg of logs a kg); L2 a collier's hut and 8 clamps (crew per clamp halves); no L3. It eats woodland: an L3 smeltery's 36 units a day burn 2,160 kg of charcoal, 10.8 t of logs, about 900 m3 a game year: 180 acres of coppice, or a clear-felled acre every 6 game days. *Cooperage* (a carpentry business): `make_crate` (12 kg planks + 0.1 kg nails, 3 h; a crate holds 100 kg or 120 L) and `make_barrel` (exists).

### 5.2 Tiers are recognised from what is installed
A stage's tier is the highest whose `recognise` rule its building meets (pieces in the stage's rooms and yard, counted by `TierRecognition` from the construction book whenever a piece in them changes); never stored as a number the player sets. Removing a machine drops the tier the same day.

### 5.3 Upgrades are one-click plans built hands-on
The stage panel's Upgrade button fits the next tier's plan (`blueprints/industry_tiers.jsonc`) onto the stage's building (extending it where the plan says) as ghost pieces, lists the materials and worker-hours, and offers: build it yourself, set your people to it, or **commission a builder** (the Phase 6 construction contract: escrowed pay, a builder who hauls and builds; a machine-wright for wheels and machines). The tier changes when the last required piece is built. Work stops when materials run out or pay is owed, as every construction (Phase 5).

### 5.4 Real inputs (no flat "supplies")
| Stage | Input | Amount |
|---|---|---|
| smeltery | charcoal | 60 kg a unit (1.5 kg a kg of ore); the finery about 6 kg a bar batch (shared heat) |
| kiln | firewood | the firing formula of the tier |
| drying hacks | straw | 20 kg a day at L1 |
| packing | crates, barrels, iron bands, withies, straw | 1 crate a 100 kg; bands 0.2 kg a bundle |
| mines L3, pug mills L2, crushers L2, hauling | horses and oxen | their working fodder (hay and oats, `animals/species.jsonc`) |
| every stage | tools | the tools' wear (`ToolWear`) |
Inputs come by the stage's sources in order: the works' own chain, open supply contracts, the market's ask. A shortage stops the stage with `NoInput` or `NoFuel`.

### 5.5 The industry output slider (1x-5x)
A world setting (`difficulty-sliders.md`'s list gains a seventh value, `IndustryOutputPct`, 100-500 in steps of 100; `DifficultySave` reads a six-value array from an older save as 100). It multiplies every **staged works'** per-worker rates and machine capacities (the player's and any NPC's); inputs per unit are unchanged, so a 5x smeltery burns 5x the charcoal. It does not touch Phase 2's generated businesses, farming, households or hand crafting. At 5x an L1 works' payback falls to about a tenth and markets saturate; the slider says so in its tooltip ("faster industry; markets will fill sooner").

### 5.6 Seasons
Clay is dug in autumn and weathered over winter (the yard turns dug clay to tempered clay only after 15 days in store, any season); open hacks dry nothing in winter (L1 drying stops; moulding stops when the hacks are full); frozen days stop wheels; hauling on snow by sledge at L1 (hauling rate x1.5 on frozen ground, x0.5 in mud); felling in winter at the extraction's 800 permille.

### 5.7 Health and safety
A worker-day at a stage risks an accident (stream **0x740**, key stage, day): mine L1 1 permille, L2 0.8, L3 0.5; kiln 0.5; saw mill 0.6 (L3 0.8: the gang saw); others 0.2. An accident is a real wound through the Phase 4 wound book (rockfall: crush to a limb; burn; saw cut), with the stage as the chronicle cause; a death writes `works_accident`.

### 5.8 Upkeep is repair work
Each stage's pieces wear (construction's piece hp: 1 hp a working day for stations and machines, as `large-buildings.md` 4.7; a furnace lining 1/30 a firing; kiln walls 1/60); repair is a job for the stage's crew or a hired builder using the piece's own materials in proportion. The balance sheet's upkeep row is the cost of those materials and hours, not a percentage.

### 5.9 Packing
A packed good sits in a store with its containers: `PackedStock` per store `(good, container good, grams packed, containers)`. Packed goods load 10% denser on vehicles, load in half the time, spoil at the store's sealed rate, and sell at +3% at a book (the handling saved). Unpacking at the buyer returns the containers as goods (10% of crates and 5% of barrels worn out a trip, `DestroyReason.Worn`), so mass stays exact.

### 5.10 Selling
Each output good is routed (the stage panel): to the next stage; to **the gate**; to a **supply contract** (`economies-of-scale.md` 4.3); to **your own caravan**; or to the works' store to wait.
- **The gate:** passing merchants and the region's buyers take goods at the nearest book's bid less the carting from the works to it (`economies-of-scale.md` 4.4); goods with a continental market go to the export desk at its price within its season budget. The tab shows today's gate price and how much the books in reach take (absorption, `economies-of-scale.md` 5.4).
- **Your own caravan:** hire a caravan master (Trading >= 40; 500 a day + 2% of sales) and guards (the caravan-guard danger premium, 1,500-2,000 a day each), pick wagons you own, pick a destination from the price notebook (any town or a border gate), and see before it leaves:
  `expected margin = sum over goods of (destination bid x qty, with the per-unit repricing of that book) - (master + guards + drivers) x days - fodder - tolls - customs - wear - risk`,
  `risk = P(ambush on that road, from CaravanAmbush's rates and the bandit slider) x cargo value x 600 / 1,000` (the share a successful ambush takes, Phase 2), with the notebook's ages ("prices 6 days old"). The caravan is a real Phase 2 caravan with the works as owner; its result comes back as `caravan_returned`.
- Distant towns pay more because they lack the goods; selling much at one book moves its price (per-unit repricing). Nothing is priced from a table.

### 5.11 Payback (the target) and the worked examples
`payback_days = BuildingsCostMites / mean net a day over the last 60 days` (net = income - wages - inputs - upkeep - royalties and rents); the tab shows it live and the share repaid; `works_paid_back` when the cumulative net passes the buildings' cost. **Target:** a well-run L1 works repays its buildings in about 120-180 days (2-3 game years) at 1x.

**Iron, L1 one-furnace works** (crew 9: mine 2 hands, dressing 2 hands, smeltery 1 master, 2 journeymen, 2 hands; yard and packing on spare hours; sold at the gate's bid, 920 permille of the book):
| Row | Per day | Per 6-day week | In sp a day |
|---|---|---|---|
| Income: 30 kg bloom x 240 x 0.92 = 6,624; 100 kg spare dressed ore x 1.8 x 0.92 = 166 | 6,790 | 40,740 | 67.9 |
| Wages: 2 x 150 + 2 x 150 + 500 + 2 x 350 + 2 x 150 | -2,100 | -12,600 | -21.0 |
| Inputs: charcoal 300 kg x 12 = 3,600; 0.3 crates x 260 = 78; tool wear 60 | -3,738 | -22,428 | -37.4 |
| Royalty: 10% of 500 kg rough ore at 0.6 | -30 | -180 | -0.3 |
| Upkeep (furnace relining, windlass rope, sheds) | -70 | -420 | -0.7 |
| **Net** | **852** | **5,112** | **8.5** |
Flows: 500 kg rough -> 300 kg dressed (200 kg tailings) -> 5 units (200 kg ore, 300 kg charcoal) -> 30 kg bloom; the mine alone earns 300 a day against its 300 of wages (raw output barely covers wages). Buildings: 3,000 worker-hours and materials, about 140,000 mites, + the base-metal licence 2,000: **payback 167 days, 2.8 game years**. With a steward who runs 3 properties, a third of his 350: 735 a day, 193 days.

**Clay, L1 brickworks** (crew 10: pit 2, yard 2, moulding 1 journeyman and 2 hands, drying 1, kiln 2 on average; moulding the bottleneck at 1,000 bricks a day; bricks 2.5 kg at 1.2 a kg):
| Row | Per working day (spring to autumn, 45 days) | Week | sp a day |
|---|---|---|---|
| Income: 2,500 kg x 1.2 x 0.92 | 2,760 | 16,560 | 27.6 |
| Wages | -1,700 | -10,200 | -17.0 |
| Inputs: firewood 1,250 kg x 0.3 = 375; straw 20 kg x 8 = 160; tools 30 | -565 | -3,390 | -5.7 |
| Upkeep | -40 | -240 | -0.4 |
| **Net** | **455** | **2,730** | **4.6** |
In winter (15 days) the pit and yard dig and weather clay for spring (wages 600, no sales). A year: `45 x 455 - 15 x 640 = 10,875`. Buildings about 30,000: **payback 2.8 game years**. L2's roofed sheds dry at half rate in winter and shorten it.

**Wood, L1 sawyard** (crew 11: 2 fellers, 1 ox driver, 8 pit sawyers in 4 pairs; yard, packing and shipping on spare hours; planks by supply contract at the book mid, logs and offcuts at the gate):
| Row | Per day | Week | sp a day |
|---|---|---|---|
| Income: 644 kg planks x 3.0 = 1,932 (contract); 1,180 kg spare logs x 0.2 and 230 kg offcuts x 0.3 at 0.92 = 281 | 2,213 | 13,278 | 22.1 |
| Wages: 2 x 150 + 150 + 8 x 150 | -1,650 | -9,900 | -16.5 |
| Fodder: 2 oxen x 10 kg hay x 12 | -240 | -1,440 | -2.4 |
| Inputs: withies 20, tools 50 | -70 | -420 | -0.7 |
| Upkeep | -20 | -120 | -0.2 |
| **Net** | **233** | **1,398** | **2.3** |
Buildings (4 saw pits with sheds, a seasoning shed, a yard) about 35,000: **payback 150 days, 2.5 game years** (the oxen, 26,000, are capital the sheet also shows: 262 days with them).
**Upgrades** are judged on the same sheet. An L2 iron works (two furnaces and a finery, crew about 25) makes about 29 kg of bar and 36 kg of bloom a day: more iron than the home region's tools, nails and shoes wear out (on the order of 70 kg a day for 25,000 people; the audit measures it, B1). It pays only with exports, its own caravans or war demand (`army-economy.md` 4.10). That is intended: big works need distant markets or a war.

### 5.12 The region's generated industries
Generated businesses keep their Phase 2 kinds but use the **composite rate** of the L1 chain for the good they make (`IndustryRates.Composite`): a generated mine's miner yields dressed ore at `600 kg per (2 hewer-days + 1 haul-day + 2.5 dressing-days) = 109 kg` a worker-day (instead of 500 kg of ore that skipped dressing); woodlots and clay pits keep their rates (felling and digging are their first stage). With the R1 prices (`economy-audit.md` 4.8) that is about 1.3x the wage. NPC founders: a household or lord with >= 500 gp may found an L1 works at a mill site or a surveyed deposit when its expected payback at today's prices is <= 3 years, and upgrade one tier at most every 2 years by the same plans and builders (stream-free: the utility is deterministic; ties by id).

## 6. The Industry tab (Ledger, 960 x 540)
- **Left (200 px):** the works you own or hold shares in, with net a day and a warning dot.
- **Flow (top, 760 x 260):** the chain's stages left to right as boxes: name, tier badge (L1-L3), crew `n / min-max`, throughput a day (kg or pieces), stock, and the link's hauling rate on each arrow. The **bottleneck** (the stage or link with the lowest throughput against what the next stage could use) is outlined amber with its reason; a stopped stage is red with its stop reason (4.2).
- **Stage detail (right, on select):** crew slider (min to max), wage offer slider, the inputs' sources (own chain, contract, market) with days of cover, the output's routing, the stop reasons, **Upgrade**: the next tier's plan, materials, worker-hours, expected throughput, and the payback of the upgrade at today's prices.
- **Balance sheet (bottom, 760 x 200):** rows Income, Wages, Inputs, Upkeep, Royalties and rents, Net; columns Day and Week (6 days), by stage or for the works; one unit (silver with one decimal; a setting switches to copper); buildings' cost, repaid share, payback days.
- **Sell panel (from the balance sheet):** the gate price and absorption per good; open contracts; "Send a caravan" (master, guards, wagons, destination, the expected margin and risk as in 5.10).
- Keys: the Ledger context's (Up/Down rows, Left/Right columns and sliders, Tab next tab, Enter open or act, `/` search, Esc back). No World key is added. `LedgerTab.Industry` is appended.

## 7. Saves
Optional section **`industry`**, absent while the player and the NPCs own no works: works rows, routing, crew targets and wage offers, links, balance rings, payback counters, `PackedStock` rows (keyed by store id, so no key is added to the economy section's store rows), own caravans in flight (route, manifest, hired staff, expected margin at departure). Stage businesses are ordinary business rows (production and ownership sections). New goods (`iron_ore_rough`, green bricks as `bricks_green`, `crate`, `tempered_clay`, `hemp`, `rope`), pieces, plans, stage kinds and recipes are content (`ContentRemap` by name). `IndustryHash` joins the hash trail once non-empty. No version bump.

## 8. RNG streams
| Stream | Use | Key |
|---|---|---|
| 0x740 | stage accidents (5.7) | stage, day |
| 0x741 | ore yield per batch: dressed share 550-650 permille of rough; gold and silver within +-20% of the deposit's grade | ore body, batch serial |
| 0x742 | kiln losses per firing (5.1) | kiln, firing serial |
| 0x743 | own caravan: ties among equal candidates for master and guards | caravan, day |
Caravan ambushes use the existing caravan streams. Free: 0x744-0x74F.

## 9. Chronicle
`CauseKind.Industry` is appended.
| Type | Cause | Root | Key | Parameters |
|---|---|---|---|---|
| `works_founded` | Industry | yes | `ch_ind_founded` | owner, chain, place |
| `works_tier_up` | Industry | no (the plan's construction) | `ch_ind_tier_up` | works, stage, tier |
| `works_stopped` | Industry | no (its stop's cause: a shortage names the input's chain, a flood the weather) | `ch_ind_stopped` | works, stage, reason, days |
| `works_paid_back` | Industry | no (`works_founded`) | `ch_ind_paid_back` | works, days, buildings' cost (`money`) |
| `works_accident` | Industry | no (the stage) | `ch_ind_accident` | person, works, stage, wound |
| `caravan_returned` | Trade | no (the caravan's departure) | `ch_ind_caravan` | works, destination, sold (`money`), margin (`money`), losses |

## 10. Golden tests
| id | Case | Expected |
|---|---|---|
| IN-1 | Stage day formula: a mine L1 with 2 hewers and 1 hand at skill 50, no crowding | 1,000 kg rough |
| IN-2 | Under-crewed: smeltery L1 with 1 | no output; `UnderCrewed` |
| IN-3 | Crew max: hiring stops at 5 for smeltery L1 | as written |
| IN-4 | Dressing: 1,000 kg rough at L1 with 0x741 forced to 600 | 600 kg `iron_ore`, 400 kg `Waste`; mass exact |
| IN-5 | Tier recognition: add `mine_rail` and 2 hunds to every worked level | L2 the same day; remove the rails: L1 |
| IN-6 | Upgrade flow: commission the L2 plan; the builder runs out of planks | work stops; tier stays L1 until the last piece |
| IN-7 | Smeltery L3 with the wheel frozen | `NoPower`; bellows and hammer at 1,000; capacity falls to 18 units |
| IN-8 | Hauling link 600 m with one cart | `floor(10 / (2 x 600 / 450 + 1)) = 2` trips, 1,000 kg a day; the bottleneck if the next stage needs more |
| IN-9 | Iron L1 worked example (prices as parameters) | net 852 a day, 5,112 a week, 8.5 sp; payback 167 days |
| IN-10 | Clay L1 worked example | 455 a working day; 10,875 a year |
| IN-11 | Wood L1 worked example | 233 a day; payback 150 days |
| IN-12 | Winter at an L1 brickworks | drying stops; moulding stops when 15,000 bricks wait; the pit and yard work |
| IN-13 | Kiln L1 firing of 20,000 bricks with 0x742 forced to 5% | 19,000 fired; 25,000 kg of firewood |
| IN-14 | Packing: 1,000 kg of bar into 10 crates, carted, unpacked | 10% denser load, half the loading time, sealed spoilage; 9 crates returned, 1 worn; mass exact |
| IN-15 | Shortage: no charcoal for 4 days | `NoFuel(charcoal)`; `works_stopped` names the charcoal chain |
| IN-16 | Slider 300: mine L1 with 2 hewers | 3,000 kg rough; the smeltery's charcoal per unit unchanged |
| IN-17 | Gate sale: 100 kg of bar, nearest book 2 km at bid 440 | 44,000 less carting 2 km |
| IN-18 | Own caravan quote: 1 t of bar to a town 24 km away, bid 520, master and 2 guards, 4 days, ambush P 4% | margin = 520,000 (with repricing) - wages - fodder - tolls - customs - wear - 4% x value x 0.6, printed by part |
| IN-19 | Composite rate for a generated mine | 109 kg dressed ore a worker-day |
| IN-20 | Payback gate (`economy-audit.md` I1): the scripted L1 iron, clay and wood works on seed 1234 near Ranunfield, 3 years, content prices after the audit | each 100-200 days |
| IN-21 | Accident: 0x740 forced at a mine L1 | a crush wound to a limb through the wound book; the chronicle cause is the stage |
| IN-22 | NPC founder: a lord with 600 gp and a mill site with a 2.5-year expected payback | founds an iron L1 works; with 4 years, does not |
| IN-23 | Shared purse: wages of 3 stages paid from one purse; the sheet splits them by stage | coin exact; per-stage rows sum to the works' |
| IN-24 | Determinism and saves: a 2-year run with three works, an own caravan in flight at the save | `IndustryHash` equal; save, load, save identical; coin and mass exact |

## 11. Budgets
A stage's day <= 5 us (40 works of 5 stages <= 1 ms a region a day, in their H4 slices); tier recognition only on piece changes (<= 50 us a stage); the Industry tab opens in <= 10 ms and refreshes once a game hour; `industry` <= 60 KB a region at 10 years.

## 12. Open questions (recommended defaults)
| # | Question | Default |
|---|---|---|
| INQ-1 | **For the user:** the iron, clay and wood sheets are not in the repository. Compare the tier tables (crews, rates, tier jumps, stage ratios) with them, or add the sheets under `docs/design/` | the tables here, rescaled to historical rates; adjust after the comparison |
| INQ-2 | **For the user:** does the industry slider apply to NPC-owned staged works too, or only the player's? | to every staged works, the player's and NPCs' alike (honest: the same world rule); not to Phase 2's generated businesses |
| INQ-3 | **For the user:** may rich NPCs found and upgrade works? | yes: L1 at a payback <= 3 years, one tier every 2 years at most |
| INQ-4 | One purse per works, or one per stage? | one per works (simpler audit, one reserve); the sheet still splits by stage |
| INQ-5 | Yard and packing at L1 on spare hours | yes (`crew_min` 0) |
| INQ-6 | Accident rates | as 5.7 |

## 13. Estimate and parts
| Part | Content | Gate | Months |
|---|---|---|---|
| **P7-IN1** stage runtime | `ChainKind`, `StageKind`, `Works`, stages as businesses on a shared purse, the day formula, crews, stops, spare hands, links and hauling, the slider, composite rates for generated kinds | IN-1 to IN-3, IN-8, IN-16, IN-19, IN-23 | 1.2 |
| **P7-IN2** chains and tiers | the three chains, charcoal and cooperage content, about 18 new pieces (`mine_rail`, `hund`, `crushing_floor`, `buddle`, `crusher_rolls`, `washing_floor`, `plank_runway`, `finery_hearth`, `packing_press`, `moulding_table`, `tile_press`, `drying_hack`, `hot_floor`, `scotch_kiln`, `kiln_bellows`, `timber_chute`, `log_boom`, `jetty`, `banding_bench`), the tier plans, recognition, upgrades, seasons, accidents, packing | IN-4 to IN-7, IN-12 to IN-15, IN-21 | 0.9 |
| **P7-IN3** the Industry tab | flow, bottleneck, stage detail, upgrade view, balance sheet by day and week in one unit | IN-9 to IN-11 (views), screenshot at 960 x 540 | 0.7 |
| **P7-IN4** selling | gate sales and absorption, contract routing, the own caravan with margin and risk | IN-17, IN-18 | 0.7 |
| **P7-IN5** NPCs and calibration | NPC founders and upgrades; the payback gate with the audit's second run | IN-20, IN-22, IN-24 | 0.3 |
**About 3.8 months of code** (IN2, IN3 and IN4 can run in parallel once IN1 lands). Art: about 18 pieces and 6 tier plans per chain (data).
