# Industries: staged production chains, tiers 1-3, crews, historical output, real inputs, the Industry tab, selling, payback

Status: design, written before any Phase 7 code, on top of b6b39e2; tier tables revised after the user's Q4 decision (`00-phase7-overview.md` 10a; section 14 lists the changes). Plan refs: the locked Industries row (staged chains of real buildings, each stage its own building with its own crew; an Industry tab in the Ledger with throughput per stage, the bottleneck, crews, wages and the next upgrade; tier upgrades as physical one-click plans built hands-on, the level recognised from what is installed, each tier with a minimum and maximum crew; medieval technology; historical output plus an honest 1x-5x "industry output" slider; real inputs: charcoal from charcoal burners who cut woodland, firewood for kilns, barrels and crates from coopers; a shortage stops the stage and the tab says why; a well-run Level 1 works repays its buildings in about 2-3 game years at default settings); §5 Phase 7 Industries (the three chains; storage, loading and packing as real stages with hauling that can bottleneck; packed goods cart better and spoil less; the tiers per stage; output from the labour model with diminishing returns from crowding; selling at the gate or by your own caravan with a caravan master, guards, wagons, a destination and the expected margin from known prices with the road's risk; distant towns pay more; flooding a market drops its price; the sheets' numbers as targets for structure; raw output barely covers wages, finishing is where the profit is; output per worker historical: a miner about 0.5 t of ore a day, a pair of sawyers 100-200 board feet; inputs real; upkeep real repair work; the balance sheet per day and per 6-day week in one coin unit).

Money in mites (1 cp = 10, 1 sp = 100, 1 gp = 1,000; `coinage.md`); the balance sheet's one unit is silver with one decimal. Wages: unskilled 150, skilled 350, master 500 a day. A game year is 60 days; a working week 6 days. Rates are at skill 50, full stock, clear weather, the slider at 1x.

**About the user's sheets.** The iron, clay and wood design sheets and their balance sheets are in `docs/design/industry-sheets/` (`Iron_Industry.pdf`, `Clay_Industry.pdf`, `Wood_Industry.pdf`, `iron_balance_sheet.png`, `clay_balance_sheet.png`, `wood_balance_sheet.png`); `00-phase7-overview.md` 11 compares them with this spec. Their structure is followed: the stage order, crew bands that rise with the tier, logistics stages with real crews, the upgrade cost ratio, "raw output barely covers wages". Their outputs and prices are rescaled on purpose to historical rates and the game's money, and the tier jumps stay machine-driven (decided with the user, Q4).

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
**Crew:** hiring stops at `crew_max`; the crew slider sets the target between the tier's `crew_min` and `crew_max` and the labour market fills it at the wage offer (Phase 2 rules).
**Crew bands rise with the tier** (Q4, as on the sheets): at every stage `crew_min(L2) = crew_max(L1) + 1` and `crew_min(L3) = crew_max(L2) + 1`, and each `crew_max` is above the lower tier's. Bands never overlap and never shrink: where a machine does the hard part, the crew still grows, because feeding it, picking, carrying and stacking stay hand work (the tables say what the extra hands do). A tier is a step in scale as well as in machines: upgrading commits the works to the new minimum.
**Short-handed:** a stage runs at the highest tier that both its installed pieces (5.2) and its own crew reach. A stage recognised at L2 with a crew inside the L1 band works at L1's rate and capacity, and the tab shows "L2, short-handed: 4 of 6"; below the L1 minimum it does nothing (`UnderCrewed`). When the tier rises, the crew target moves to the new minimum and the Upgrade view (6) says how many more hands the tier needs before the plan is built.
**Spare hands:** a stage that is blocked or done before the day ends lends its idle hours to the works' yard, packing and shipping stages at the receiving stage's L1 rate (lent hands do not work its machines). Only at L1 may the yard, packing and shipping stages have `crew_min` 0 and run on spare hours alone; **from L2 they need their own crew** (`crew_min` at least 2 at L2 and 4 at L3). Spare hours add to a staffed stage's work but never count toward its `crew_min`.

### 4.2 Stop reasons (each shown on the tab in plain words, with the good or cause)
`NoInput(good)`, `NoFuel(good)`, `Full` (the next stage's store or the stage's own store is full), `UnderCrewed`, `NoPower` (frozen, dry, sluice shut), `Broken` (a machine at 0 hp), `Flooded` (a mine's flood day without pumps), `Winter` (open drying hacks), `Unpaid` (wages owed beyond Phase 2's 3 short days), `NoLicence`, `Depleted` (the ore body or the woodland). A stage stopped more than 3 days writes `works_stopped` once.

### 4.3 Links and hauling between stages
Within 60 m, stages pass goods by the crews' own hands (counted in the stage's handling). Beyond 60 m a link needs carts or a runway: `trips = floor(10 h / (2 x d / 450 m an hour + load_h))` a cart a day (a cart covers about 450 m a game hour; loading 1 h a trip, 0.5 h for packed goods or with a crane), `kg = trips x 500` a cart (1,000 an ox wagon). A link short of capacity is the bottleneck like any stage ("hauling: 1,500 of 2,100 kg a day").

## 5. Rules

### 5.1 The three chains (tier tables)
Rates are per worker-day unless said; "kg moved" counts each kg once into a store and once out of it. Capacity is the lower of crew x rate and the installed stations' and machines' own limit, where the table gives one. Prices for the worked examples are the audit's candidates (`economy-audit.md` 4.8).

**Iron: mine -> crusher and washer -> ore yard -> smeltery -> packing.** Crews: L1 5-14, L2 19-39, L3 44-67 (the sheet's L1 works is 75).
| Stage | Tier and plan | Installed (recognised by) | Crew | Rate and capacity | Inputs and notes |
|---|---|---|---|---|---|
| Mine | L1 hand-hauled | adit or shaft to 2 levels, ladders, a windlass, baskets | 2-4 | a hewer 500 kg of `iron_ore_rough`; 1 haul hand per 2 hewers (333 a worker on average; about 1,333 at 4) | flood days stop it |
| | L2 carts on wooden rails | + `mine_rail` on each worked level, 2 `hund` carts | 5-10 | 400 (1 haul hand per 4 hewers); 4 levels; 2,000-4,000 | the extra hands are hewers on the new levels and a timberman |
| | L3 horse gin or water hoist and pumps | + `horse_gin` or a powered `hoist_drum` and `rag_pump` | 11-20 | 444 (1 haul hand per 8 hewers); 8 levels; no flood days; 4,900-8,900 | gin: 2 horses' fodder; water: 2,000 W |
| Crusher and washer | L1 hand crushing, a buddle | `crushing_floor`, `buddle` | 1-3 | 400 kg rough | 1,000 kg rough -> 600 kg `iron_ore` + 400 kg tailings (`DestroyReason.Waste`) |
| | L2 crusher and washing floor | + `crusher_rolls` (horse), `washing_floor` | 4-8 | 600 kg rough; the rolls break up to 5,000 kg a day | 1 horse; the rolls do the breaking, the crew picks and washes |
| | L3 water stamps | the `stamp_mill` plan (`large-buildings.md` 4.5): 2 powered `stamp_battery`, buddles | 9-16 | 750 kg rough; 6 t a day a battery (12,000) | 5,000 W; the stamps crush, the crew feeds them, picks the ore and works the buddles |
| Ore yard | L1 hand loading | a fenced yard, shovels and baskets | 0-1 | 3,000 kg moved; store 40 t | spare hands at L1 |
| | L2 barrows and plank runways | + 20 m `plank_runway`, 2 barrows | 2-3 | 4,000 kg moved; store 120 t | ore and charcoal in, the furnace charge and ore for sale out |
| | L3 treadwheel crane | + `treadwheel_crane` | 4-6 | 5,000 kg moved; the crane lifts at most 20 t a day; store 300 t | 2 walkers in the crew |
| Smeltery | L1 one furnace | 1 bloomery, hand bellows | 2-5 | a unit (40 kg ore + 60 kg charcoal -> 6 kg bloom) per 10 worker-hours; 5 units a day | charcoal |
| | L2 two furnaces and a finery | 2 bloomeries, foot bellows, `finery_hearth` | 6-15 | 8 h a unit; 6 units a furnace (12); finery `forge_bar` 6 kg bloom -> 4.8 kg bar in 4 worker-hours | charcoal (shared heat at the finery) |
| | L3 three bellows furnaces and a hammer | the `furnace_house` plan: 3 bloomeries with powered `great_bellows`, a powered `trip_hammer` | 16-20 | 4 h a unit (the bellows' labour 500); 12 units a furnace (36); bar at 1.5 h a batch (the hammer's 375) | 1,000 W a furnace, 2,000 W the hammer |
| Packing | L1 by hand | a bench | 0-1 | 1,000 kg packed or loaded; dry casks of 100 kg (5.9) | casks |
| | L2 packing press and loading ramp | + `packing_press`, a ramp | 2-3 | 1,500 kg | casks |
| | L3 loading bay with a crane | + `treadwheel_crane` at a loading bay | 4-5 | 2,000 kg; loading 0.5 h a cart | |
Iron packs in casks, as on the sheet. Its finished output is small by mass (30 kg a day at L1, about 170 at L3), so packing reaches L2 only in a works that also loads dressed ore by the cartload; bulk ore leaves the yard on the buyer's cart.

**Clay: clay pit -> yard -> moulding shop (pots, bricks, shingles) -> drying sheds -> kiln -> packing.** Crews: L1 8-16, L2 22-39, L3 45-68 (the sheet's L1 works is 69).
| Stage | Tier | Installed | Crew | Rate and capacity | Notes |
|---|---|---|---|---|---|
| Clay pit | L1 spades and barrows | the pit, a ramp | 2-3 | 2,000 kg of clay | |
| | L2 runways out of the pit | + `plank_runway`, barrows | 4-6 | 2,500 kg | a deeper pit: the runways keep the rate up |
| | L3 bucket hoist and drainage | + `horse_gin` hoist, `rag_pump` | 7-10 | 3,000 kg; no wet-day stops | 2 horses |
| Yard (weathering, tempering) | L1 treading by foot | a treading floor | 1-3 | 1,500 kg tempered | clay dug in autumn weathers over winter (5.6) |
| | L2 horse pug mill | + `pug_mill` with a gin | 4-6 | 2,500 kg; the mill tempers up to 15,000 a day | 1 horse; the crew cuts, feeds, turns the heaps and carries |
| | L3 water pug mill | powered `pug_mill` | 7-9 | 3,000 kg; up to 30,000 a day | 1,500 W; the mill replaces the horse, not the carriers |
| Moulding shop | L1 one table | 1 `moulding_table` | 2-4 | 1,000 bricks (2.8 kg green each) a table-day with a moulder and 2 hands; or 60 pots a potter's wheel; or 800 shingles | grades 50/150/800; about 333 bricks a worker at every tier, as on the sheet |
| | L2 three tables | 3 tables (+ a potter's wheel) | 5-12 | 3,000 bricks | |
| | L3 six tables and a tile press | 6 tables, `tile_press`, 2 wheels | 13-24 | 6,000 bricks | |
| Drying sheds | L1 open hacks | `drying_hack` rows, straw covers | 1-2 | 1,000 bricks set, turned and taken off a worker; hacks hold 6,000; **6 days** (the sheet's week) spring to autumn; none in winter (frost) | straw 20 kg a day |
| | L2 roofed sheds | a `drying_shed` room | 3-4 | 1,000; sheds hold 18,000; 6 days, 12 in winter | |
| | L3 hot floors | + `hot_floor` fed by the kiln's waste heat | 5-6 | 1,200; floors hold 36,000; 6 days all year | |
| Kiln | L1 clamp | `brick_clamp` | 2-3 | 750 bricks set and drawn a worker; 20,000 bricks (50 t) a firing, 10-day cycle; firewood 5,000 kg + 0.4 kg a kg | losses 3-10% a firing (0x742) |
| | L2 two Scotch kilns | 2 `scotch_kiln` | 4-8 | 750; 24,000 each, 8 days; 4,000 kg + 0.3 kg a kg | losses 2-5% |
| | L3 three kilns with draught | 3 kilns, `kiln_bellows` | 9-14 | 750; 24,000 each, 7 days; 3,500 kg + 0.25 kg a kg | losses 1-3% |
| Packing | L1 stacking on straw | - | 0-1 | 2,500 kg packed or loaded | straw; pots in crates |
| | L2 pallets and crates | `packing_press` | 2-3 | 3,000 kg | |
| | L3 crane at a loading bay | crane | 4-5 | 4,000 kg; loading 0.5 h a cart | |

**Wood: felling -> hauling -> timber yard -> saw mill -> packing -> shipping.** (1 board foot = 2.36 L; planks at 2.2 L a kg: 1 bf = 1.07 kg.) Crews: L1 4-15, L2 21-30, L3 33-43 (packing at L2; the sheet's L1 works is 13).
| Stage | Tier | Installed | Crew | Rate and capacity | Notes |
|---|---|---|---|---|---|
| Felling | L1 axes | - | 1-2 | 1,050 kg of logs (1.5 m3) | woodland stock 100 m3 an acre; coppice 5 m3 an acre a year |
| | L2 crosscut saws and felling teams | `crosscut_saw` tools | 3-4 | 1,400 kg | |
| | L3 timber chutes | + `timber_chute` on slopes | 5-6 | 1,750 kg | |
| Hauling | L1 dragging with oxen | an ox team and chains a driver | 1-2 drivers | 3,000 kg a driver-day over 2 km | 2 oxen a team |
| | L2 timber carriages | `timber_carriage` (vehicle template), a driver and a loader each | 3-4 | 4,000 kg a worker-day over 2 km | 4 oxen a carriage |
| | L3 floating | a `log_boom` on a river | 5-6 | 5,000 kg a worker-day, any distance downstream | a `river` class water; the crew rolls in, steers and catches at the boom |
| Timber yard | L1 hand rolling | a fenced yard, a seasoning shed | 0-1 | 3,000 kg moved; seasoning store 60 t | |
| | L2 skids and runways | + skids, `plank_runway` | 2-3 | 4,000 kg moved; 150 t | |
| | L3 treadwheel crane | + crane | 4-6 | 5,000 kg moved; the crane lifts at most 20 t a day; 400 t | 2 walkers in the crew |
| Saw mill | L1 pit saws | `saw_pit` stations | 2-8 (pairs) | a pair 150 bf (161 kg of planks) a day from 230 kg of logs: 75 bf a worker | 70% planks, 25% sawdust and slabs |
| | L2 frame saws | 3 `frame_saw` in saw sheds (a `saw_shed_frame` holds 2) | 9-12 | 150 bf a worker (x2) | 3-4 a frame |
| | L3 water gang saw | the `saw_mill_gang` plan: a powered `gang_saw_frame`, a log carriage | 13-16 | 220 bf a worker; the frame cuts up to 3,600 bf a day | 3,000 W; the frame saws 8 times as fast as hands (`labour_pm` 125), but rolling, dogging, tailing, edging and stacking stay hand work and now set the rate |
| Packing | L1 bundling | withies | 0-1 | 1,000 kg bundled (planks and offcuts) | |
| | L2 banding bench | `banding_bench` | 2-4 | 1,500 kg | withies or iron bands |
| Shipping | L1 cart bay | a loading bay | 0-1 | 3,000 kg loaded | spare hands |
| | L2 covered bays and a ramp, or a jetty | a loading ramp, or `jetty` on navigable water | 2-3 | 4,000 kg | |
| | L3 a crane at the bay or jetty | + crane | 4-5 | 5,000 kg; loading 0.5 h a cart | |
Hauling's tiers buy distance: the rates are over 2 km, and a works d km from its wood moves `2 / d` of them (floating excepted). The L2 and L3 hauling bands are for wood far away, not for more wood.

**Per-stage tiers.** Each stage is upgraded on its own when it is the bottleneck. A works is not "L2" as a whole: its L2 example below runs an L2 smeltery on an L1 mine (5.11). Bands follow the work each tier's stations and machines need, so the stages of one tier do not all match one another; the worked examples (5.11) pick each stage's tier for the flow. The mine's, felling's and hauling's tiers buy depth, dry working and distance more than throughput: an L1 mine of 3 feeds an L2 smeltery, and an L2 mine of 6 an L3 one.

**Input chains.** *Charcoal* (felling -> clamps, `charcoal`): L1 earth clamps (`burn_charcoal`, 5 kg of logs a kg); L2 a collier's hut and 8 clamps (crew per clamp halves); no L3. It eats woodland: an L3 smeltery's 36 units and their bar burn about 2,380 kg of charcoal a day, 11.9 t of logs, about 1,000 m3 a game year: 200 acres of coppice, or a clear-felled acre every 6 game days. *Cooperage* (a carpentry business): `make_crate` (12 kg planks + 0.1 kg nails, 3 h; a crate holds 100 kg or 120 L), `make_dry_cask` (new: a slack cask with wooden hoops for dry goods, 12 kg planks + 1 kg withies, 5 h; holds 100 kg; about 260 at the candidate prices, the crate's cost) and `make_barrel` (exists; iron-hooped, for liquids).

#### 5.1a Worker grades (the sheets' untrained, skilled and mechanised)
The sheets give extraction workers three grades with output 1 : 2 : 4 (wages 4 and 6 for the first two). The game has no grade field; the same spread comes from three things it already has, multiplied together:
| Sheet grade | In the game | Output factor |
|---|---|---|
| Untrained | a **hand**: unskilled occupation or skill < 30 (`economies-of-scale.md` 3), the unskilled wage 150 | `speed(skill)`: for extraction stages `Extraction.SkillMult` = 800 + 4 x skill permille (0.8 at skill 0, 1.0 at 50, 1.2 at 100); for recipe stages `CraftMath.SkillFactorPm` (time 1,120 to 800 permille around the recipe's difficulty) |
| Skilled | a **journeyman or master** (skill >= 30, or >= 60 with the proficiency), the skilled wage 350 or master 500 | the same curve, plus the role split on recipe stages: masters and journeymen do the skilled steps, a crew of hands alone is slower (`economies-of-scale.md` 4.1) and spoils batches (stream 0x720) |
| Mechanised | **not a person: the tier.** The same hand at a tier whose tools or machine raise the rate | felling 1,050 -> 1,750 (x1.7); sawing 75 -> 150 -> 220 bf (x2.9); dressing 400 -> 750 (x1.9); a smelting unit 10 -> 4 worker-hours (x2.5) |
**Measured from the code at b6b39e2:** skill 20 against skill 60 is 880 against 1,040 permille, **1.18x, not the 2x** the overview asked P7-IN1 to check (11, item 6); skill 0 to 100 spans 1.5x. The sheets' 1 : 2 between untrained and skilled is therefore not reproduced by skill alone; a skilled crew reaches about 2x an unskilled one only together with the next tier's tools. The curve is left as it is: it is Phase 2's rule for every generated extraction, and the R1 raw prices are pinned to skill 50, so a steeper curve would move every raw price (`economy-audit.md` 4.8). The game's wage ratio is also steeper than the sheets' (150 : 350, 2.3x, against 4 : 6), so hands stay the cheaper labour per kg on simple stages, which is historical.

### 5.2 Tiers are recognised from what is installed
A stage's tier is the highest whose `recognise` rule its building meets (pieces in the stage's rooms and yard, counted by `TierRecognition` from the construction book whenever a piece in them changes); never stored as a number the player sets. Removing a machine drops the tier the same day.

### 5.3 Upgrades are one-click plans built hands-on
The stage panel's Upgrade button fits the next tier's plan (`blueprints/industry_tiers.jsonc`) onto the stage's building (extending it where the plan says) as ghost pieces, lists the materials and worker-hours, and offers: build it yourself, set your people to it, or **commission a builder** (the Phase 6 construction contract: escrowed pay, a builder who hauls and builds; a machine-wright for wheels and machines). The tier changes when the last required piece is built. Work stops when materials run out or pay is owed, as every construction (Phase 5).

#### 5.3a What an upgrade costs (the sheets' ratio)
**The rule.** The sheets price every stage's L2 upgrade at 1.5 times its L1 building and its L3 upgrade at 2 times (`Iron_Industry.pdf`: stage buildings 5,000 / 7,500 / 10,000 gp, the mine 10,000 / 15,000 / 20,000). This spec adopts that ratio for the **hand-built work** of every tier plan (sheds, floors, tables, kilns, rails, deeper levels): P7-IN2 tunes each plan's materials and worker-hours so its bill at the candidate prices lands within 20% of the table below (IN-27). **Machines are not tuned to it:** a powered machine or a `large-buildings.md` plan costs its own bill (`large-buildings.md` 3.2 and 4.5, shared with every mill in the game), and where that bill is larger than the ratio, the bill wins.

**Why L3 departs from 2x.** The spec's own numbers argue against it at L3. The water-powered plans carry 50-300 kg of iron each (wheel bands, cams, stamp shoes, hammer heads, blades), and iron is 480 a kg at the candidate prices: the iron in the stamp mill, the furnace house and the gang saw mill alone is 60,000-150,000 mites each, more than the whole L1 build of the clay or the wood works. Matching 2x would mean pricing a machine's iron below the market's (one good, one price), or raising the L1 builds, which breaks the L1 payback target. So L2 lands at 1.5-2.1x and L3 at 4-5x the L1 build. The L3 upgrades still repay in weeks at the gate price (5.11): the market, not the build cost, is what bounds them.

Bills at the candidate prices, building labour at 30 a worker-hour (a blend of the skilled and unskilled classes); rope is left out, because today's content prices it at 2,000 a kg (four times bar iron, ten times a crane's other materials) and the audit should reprice it from hemp first:
| Stage | L1 build | L2 upgrade | L3 upgrade |
|---|---|---|---|
| Iron mine | 70,000 | 105,000 | 140,000 (gin, hoist and pump about 32,000 of it; the rest is sinking 4 more levels) |
| Iron crusher and washer | 20,000 | 30,000 (horse crusher rolls about 22,000) | **183,000** (the `stamp_mill` plan) |
| Iron ore yard | 5,000 | 7,500 | **18,000** (`treadwheel_crane`) |
| Smeltery | 40,000 | 60,000 | **199,000** (`furnace_house` with 3 great bellows, + `trip_hammer`) |
| Iron packing | 5,000 | 7,500 | **18,000** (crane) |
| **Iron works** | **140,000** (+ the licence 2,000) | **210,000 (1.5x)** | **558,000 (4.0x)** |
| Clay pit | 3,000 | 4,500 | **32,000** (gin hoist and rag pump) |
| Clay yard | 3,000 | **22,000** (`pug_mill` and `horse_gin`) | **30,000** (powered `pug_mill`, breastshot wheel, sluice, shaft) |
| Moulding shop | 6,000 | 9,000 | 12,000 |
| Drying sheds | 5,000 | 7,500 | 10,000 |
| Kiln | 10,000 | 15,000 | 20,000 |
| Clay packing | 3,000 | 4,500 | **18,000** (crane) |
| **Clay works** | **30,000** | **62,500 (2.1x)** | **122,000 (4.1x)** |
| Felling | 1,000 | 1,500 | 2,000 |
| Hauling | 2,000 (+ 2 oxen, 26,000 as capital) | 3,000 (+ 2 more oxen) | 4,000 |
| Timber yard | 10,000 | 15,000 | 20,000 |
| Saw mill | 20,000 | **50,000** (a saw shed and 3 frame saws: 45 kg of steel blades) | **131,000** (the `saw_mill_gang` plan) |
| Wood packing | 1,000 | 1,500 | - |
| Shipping | 1,000 | 1,500 | **18,000** (crane) |
| **Wood works** | **35,000** | **72,500 (2.1x)** | **175,000 (5.0x)** |
Bold cells are where a machine's bill sets the price instead of the ratio. The L1 splits by stage follow the sheets' proportions (the mine about half of the iron works, the stage buildings and yards the rest).

### 5.4 Real inputs (no flat "supplies")
| Stage | Input | Amount |
|---|---|---|
| smeltery | charcoal | 60 kg a unit (1.5 kg a kg of ore); the finery about 6 kg a bar batch (shared heat) |
| kiln | firewood | the firing formula of the tier |
| drying hacks | straw | 20 kg a day at L1 |
| packing | crates, dry casks (iron), iron bands, withies, straw | 1 crate or cask a 100 kg; bands 0.2 kg a bundle |
| mines L3, clay pit hoists L3, pug mills L2, crushers L2, hauling | horses and oxen | their working fodder (hay and oats, `animals/species.jsonc`) |
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
A packed good sits in a store with its containers: `PackedStock` per store `(good, container good, grams packed, containers)`. Packed goods load 10% denser on vehicles, load in half the time, spoil at the store's sealed rate, and sell at +3% at a book (the handling saved). Unpacking at the buyer returns the containers as goods (10% of crates and dry casks and 5% of barrels worn out a trip, `DestroyReason.Worn`), so mass stays exact. **Containers by chain:** iron (bloom, bar and anything sold by the piece) packs in **dry casks** of 100 kg, as the sheet's barrels (the medieval osmund trade shipped iron in barrels); bricks stack on straw and pots go in crates; planks and offcuts are bundled with withies or iron bands. Bulk raw goods (dressed ore, logs) are not packed: they leave the yard on the buyer's cart.

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
| Inputs: charcoal 300 kg x 12 = 3,600; 0.3 dry casks x 260 = 78; tool wear 60 | -3,738 | -22,428 | -37.4 |
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
**Upgrades** are judged on the same sheet, with the same prices and the gate's 920 permille. Each works below upgrades only the stages its flow needs (5.1, per-stage tiers); crews are from the tier tables, upgrade costs from 5.3a. "Floor" is the share of today's gate price at which the upgrade still repays in 150 days (2.5 game years).
| Works | Stages: tier and crew | Crew | Output a day | Net a day | Upgrade cost | Payback at the gate price | Floor |
|---|---|---|---|---|---|---|---|
| Iron L2 | smeltery L2 12 (2 masters, 4 journeymen, 6 hands; half the bloom to bar); mine L1 3; dressing L1 3; yard L1 1; packing on spare hours | 19 | 28.8 kg bar, 36 kg bloom, 120 kg dressed ore | **6,975** (69.8 sp) | 60,000 (the smeltery) | 10 days; the works 202,000 in 29 | 73% |
| Iron L3 | smeltery L3 20 (4 M, 8 J, 8 H; all the bloom to bar); mine L2 6; dressing L2 4; yard L2 2; packing 1; a machine-wright | 34 | 172.8 kg bar | **37,883** (378.8 sp) | 341,500 (mine, dressing and yard to L2 142,500; smeltery to L3 199,000) | 11 days; the works 543,500 in 14 | 62% |
| Clay L2 | pit L2 4; yard L2 4; moulding L2 9 (3 J, 6 H); drying L2 3; kiln L2 4; packing L2 3 | 27 | 3,000 bricks spring to autumn, 1,500 in winter | **2,315** a working day, -1,413 a winter day; **83,000 a year** | 62,500 (every stage) | 52 days; the works 92,500 in 67 | 89% |
| Clay L3 | pit L3 7; yard L3 7; moulding L3 18 (6 J, 12 H); drying L3 5; kiln L3 9; packing L3 4; a machine-wright | 51 | 6,000 bricks, all year (hot floors) | **5,063**; 303,750 a year | 122,000 (every stage) | 33 days; the works 214,500 in 42 | 83% |
| Wood L2 | felling L2 3; hauling L1 2 (4 oxen); yard L2 3; saw mill L2 12 (3 J, 9 H); packing L2 2; shipping L1 1 | 23 | 1,800 bf (1,926 kg planks), 1,449 kg spare logs, 688 kg offcuts | **1,504** | 68,000 (felling, yard, saw mill, packing) + 2 oxen 26,000 | 53 days (74 with the oxen); the works 103,000 in 68 | 87% |
| Wood L3 | felling L2 4; hauling L1 2; yard L2 3; saw mill L3 16 (4 J, 12 H); packing L2 4; shipping L2 2; a machine-wright | 32 | 3,520 bf (3,766 kg planks), 219 kg spare logs, 1,345 kg offcuts | **4,851** | 132,500 (saw mill to L3, shipping to L2) | 40 days; the works 235,500 in 49 | 79% |
The rows, per day:
- **Iron L2:** income 20,866 (bar 13,824, bloom 8,640, ore 216, at 0.92); wages 4,350; inputs 9,360 (charcoal 756 kg x 12 = 9,072, 0.65 casks 168, tools 120); royalty 60; upkeep 120.
- **Iron L3:** income 76,308; wages 8,300; inputs 29,381 (charcoal 2,376 kg x 12 = 28,512, 1.73 casks 449, tools 250, the crusher's horse 170); royalty 144; upkeep 600 (the machine-wright's repairs, 5.8).
- **Clay L2:** income 8,280; wages 4,650; inputs 1,215 (firewood 2,750 kg x 0.3 = 825, packing straw 160, the pug mill's horse 170, tools 60); upkeep 100. The whole crew is kept and paid through winter (a steward who lays off the hands the half-rate sheds do not need does better).
- **Clay L3:** income 16,560; wages 9,050; inputs 2,148 (firewood 4,625 kg x 0.3 = 1,388, straw 320, the hoist's 2 horses 340, tools 100); upkeep 300.
- **Wood L2:** income 6,234 (planks 5,778 by contract, logs and offcuts 456 at the gate); wages 4,050; fodder 480; inputs 140; upkeep 60.
- **Wood L3:** income 11,711; wages 5,800; fodder 480; inputs 280; upkeep 300.

**Reading it.** At the gate price every upgrade repays in weeks, not years: the tiers raise output per worker (and clay's hot floors work all year) far faster than they raise the wage bill, and even L3's machines are small next to a year's output at book. **The brake is the market, as before.** An L2 iron works makes about as much iron as the home region's tools, nails and shoes wear out (on the order of 70 kg a day for 25,000 people; the audit measures it, B1), and an L3 one 2.5 times that, so the gate price falls with per-unit repricing until the surplus sells only by exports, its own caravans or war demand (`army-economy.md` 4.10). The floor column says how much room there is: the L3 iron upgrade still repays in 2.5 years at 62% of today's price, the L2 clay upgrade only if bricks hold 89%. The Upgrade view (6) prices the extra output with the books' per-unit repricing, not at today's gate price, so the player sees the market's answer. That is intended: big works need distant markets or a war. The payback target stays the L1 one (IN-20); the L2 and L3 rows are views (IN-29), and the audit's second run measures their absorption (I2) with an L3 iron works near the route.

### 5.12 The region's generated industries
Generated businesses keep their Phase 2 kinds but use the **composite rate** of the L1 chain for the good they make (`IndustryRates.Composite`): a generated mine's miner yields dressed ore at `600 kg per (2 hewer-days + 1 haul-day + 2.5 dressing-days) = 109 kg` a worker-day (instead of 500 kg of ore that skipped dressing); woodlots and clay pits keep their rates (felling and digging are their first stage). With the R1 prices (`economy-audit.md` 4.8) that is about 1.3x the wage. NPC founders: a household or lord with >= 500 gp may found an L1 works at a mill site or a surveyed deposit when its expected payback at today's prices is <= 3 years, and upgrade one tier at most every 2 years by the same plans and builders (stream-free: the utility is deterministic; ties by id).

## 6. The Industry tab (Ledger, 960 x 540)
- **Left (200 px):** the works you own or hold shares in, with net a day and a warning dot.
- **Flow (top, 760 x 260):** the chain's stages left to right as boxes: name, tier badge (L1-L3), crew `n / min-max`, throughput a day (kg or pieces), stock, and the link's hauling rate on each arrow. The **bottleneck** (the stage or link with the lowest throughput against what the next stage could use) is outlined amber with its reason; a stopped stage is red with its stop reason (4.2).
- **Stage detail (right, on select):** crew slider (min to max), wage offer slider, the inputs' sources (own chain, contract, market) with days of cover, the output's routing, the stop reasons, **Upgrade**: the next tier's plan, materials, worker-hours and cost (5.3a), its crew band and how many more hands it needs ("L2 needs 6-15: hire 1 more"), expected throughput, and the payback of the upgrade at today's prices with the extra output priced by the books' per-unit repricing, with the floor price (5.11).
- **Balance sheet (bottom, 760 x 200):** rows Income, Wages, Inputs, Upkeep, Royalties and rents, Net; columns Day and Week (6 days), by stage or for the works; one unit (silver with one decimal; a setting switches to copper); buildings' cost, repaid share, payback days.
- **Sell panel (from the balance sheet):** the gate price and absorption per good; open contracts; "Send a caravan" (master, guards, wagons, destination, the expected margin and risk as in 5.10).
- Keys: the Ledger context's (Up/Down rows, Left/Right columns and sliders, Tab next tab, Enter open or act, `/` search, Esc back). No World key is added. `LedgerTab.Industry` is appended.

## 7. Saves
Optional section **`industry`**, absent while the player and the NPCs own no works: works rows, routing, crew targets and wage offers, links, balance rings, payback counters, `PackedStock` rows (keyed by store id, so no key is added to the economy section's store rows), own caravans in flight (route, manifest, hired staff, expected margin at departure). Stage businesses are ordinary business rows (production and ownership sections). New goods (`iron_ore_rough`, green bricks as `bricks_green`, `crate`, `dry_cask`, `tempered_clay`, `hemp`, `rope`), pieces, plans, stage kinds and recipes are content (`ContentRemap` by name). `IndustryHash` joins the hash trail once non-empty. No version bump.

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
| IN-12 | Winter at an L1 brickworks | drying stops; moulding stops when the hacks' 6,000 bricks wait; the pit and yard work |
| IN-13 | Kiln L1 firing of 20,000 bricks with 0x742 forced to 5% | 19,000 fired; 25,000 kg of firewood |
| IN-14 | Packing: 1,000 kg of bar into 10 dry casks, carted, unpacked | 10% denser load, half the loading time, sealed spoilage; 9 casks returned, 1 worn; mass exact |
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
| IN-25 | Crew bands (content check over the 17 stage kinds) | at every stage `crew_min(L2) = crew_max(L1) + 1`, `crew_min(L3) = crew_max(L2) + 1`, each `crew_max` above the lower tier's; yard, packing and shipping `crew_min` >= 2 at L2 and >= 4 at L3; works totals as 5.1 (iron 5-14 / 19-39 / 44-67, clay 8-16 / 22-39 / 45-68, wood 4-15 / 21-30 / 33-43) |
| IN-26 | Short-handed and spare hours: a smeltery recognised L2 with 5 hands; an ore yard recognised L2 with no hands and 4 spare hours lent by the smeltery; the same yard with 1 hand | the smeltery makes 5 units at 10 h (L1's) and shows "L2, short-handed: 5 of 6"; the yard moves 1,200 kg at L1's 3,000 a day; with 1 hand it is still L1 ("short-handed: 1 of 2"), the lent hours never count toward `crew_min` |
| IN-27 | Upgrade bills (content check): each tier plan's materials and worker-hours at the candidate prices, labour 30 an hour, rope left out | within 20% of 5.3a's table; the hand-built plans at 1.5x (L2) and 2x (L3) the stage's L1 build |
| IN-28 | Drying: L1 hacks filled with 6,000 green bricks in summer; L2 sheds in winter; L3 hot floors in winter | L1 1,000 dry bricks a day from day 7; L2 12 days; L3 6 days |
| IN-29 | L2 and L3 worked rows (5.11; prices as parameters) | net a day: iron 6,975 and 37,883; clay 2,315 a working day and 5,063; wood 1,504 and 4,851; the upgrade view's floors 73%, 62%, 89%, 83%, 87%, 79% |

## 11. Budgets
A stage's day <= 5 us (40 works of 5 stages <= 1 ms a region a day, in their H4 slices); tier recognition only on piece changes (<= 50 us a stage); the Industry tab opens in <= 10 ms and refreshes once a game hour; `industry` <= 60 KB a region at 10 years.

## 12. Open questions (recommended defaults)
| # | Question | Default |
|---|---|---|
| INQ-1 | ~~The sheets are not in the repository~~ **Resolved:** they are in `docs/design/industry-sheets/`, compared in `00-phase7-overview.md` 11; the tables follow the user's Q4 decision (section 14) | the tables here: historical rates, machine-driven tier jumps, crew bands that rise with the tier |
| INQ-2 | **For the user:** does the industry slider apply to NPC-owned staged works too, or only the player's? | to every staged works, the player's and NPCs' alike (honest: the same world rule); not to Phase 2's generated businesses |
| INQ-3 | **For the user:** may rich NPCs found and upgrade works? | yes: L1 at a payback <= 3 years, one tier every 2 years at most |
| INQ-4 | One purse per works, or one per stage? | one per works (simpler audit, one reserve); the sheet still splits by stage |
| INQ-5 | Yard and packing at L1 on spare hours | **Decided (Q4):** yes at L1 only (`crew_min` 0); from L2 the yard, packing and shipping stages have their own crews (4.1) |
| INQ-6 | Accident rates | as 5.7 |

## 13. Estimate and parts
| Part | Content | Gate | Months |
|---|---|---|---|
| **P7-IN1** stage runtime | `ChainKind`, `StageKind`, `Works`, stages as businesses on a shared purse, the day formula, crews, stops, spare hands, links and hauling, the slider, composite rates for generated kinds | IN-1 to IN-3, IN-8, IN-16, IN-19, IN-23, IN-26 | 1.2 |
| **P7-IN2** chains and tiers | the three chains, charcoal and cooperage content, about 18 new pieces (`mine_rail`, `hund`, `crushing_floor`, `buddle`, `crusher_rolls`, `washing_floor`, `plank_runway`, `finery_hearth`, `packing_press`, `moulding_table`, `tile_press`, `drying_hack`, `hot_floor`, `scotch_kiln`, `kiln_bellows`, `timber_chute`, `log_boom`, `jetty`, `banding_bench`), the tier plans with the crew bands and the cost table (5.3a), recognition, upgrades, seasons, accidents, packing, `make_dry_cask` | IN-4 to IN-7, IN-12 to IN-15, IN-21, IN-25, IN-27, IN-28 | 0.9 |
| **P7-IN3** the Industry tab | flow, bottleneck, stage detail, upgrade view, balance sheet by day and week in one unit | IN-9 to IN-11 and IN-29 (views), screenshot at 960 x 540 | 0.7 |
| **P7-IN4** selling | gate sales and absorption, contract routing, the own caravan with margin and risk | IN-17, IN-18 | 0.7 |
| **P7-IN5** NPCs and calibration | NPC founders and upgrades; the payback gate with the audit's second run | IN-20, IN-22, IN-24 | 0.3 |
**About 3.8 months of code** (IN2, IN3 and IN4 can run in parallel once IN1 lands). Art: about 18 pieces and 6 tier plans per chain (data). The Q4 revision does not change the estimate: the new bands, costs and drying times are content in tables IN2 already writes, the short-handed rule is a few lines of IN1's crew check, and IN-25 to IN-29 are small table and view tests.

## 14. Changes after the user's Q4 decision (Nov 2026)
The user chose **historical crews with machine-driven tier jumps, adjusted to the sheets** (`00-phase7-overview.md` 10a, Q4; R1 labour pricing of raw goods kept, Q2). Against the version compared in the overview's section 11:
- **Crew bands rise with the tier and never overlap** (4.1, 5.1; IN-25). Each L2 band starts one above the L1 maximum and each L3 band one above the L2 maximum. The three L3 bands that shrank now grow: iron dressing 2-8 -> 9-16, the clay yard 1-4 -> 7-9, the saw mill 2-8 -> 13-16, with the extra hands doing what the machine does not (picking and washing; cutting, feeding and carrying; rolling, tailing and stacking). To keep bands contiguous without inflating L2 and L3, several L1 maxima came down: mine 8 -> 4, dressing 6 -> 3, clay pit 8 -> 3, clay yard 6 -> 3, felling 12 -> 2, hauling 4 -> 2. The L1 crews of the three worked examples are inside the new bands, so their sheets and IN-9 to IN-11 are unchanged (852, 455 and 233 a day; paybacks 167 days, 2.8 game years and 150 days).
- **Yard, packing and shipping have real crews from L2** (at least 2 at L2 and 4 at L3); spare hands carry them only at L1, and lent hours never count toward a minimum (4.1; IN-26). Iron's yard is 0-1 / 2-3 / 4-6 instead of 0-3 / 1-4 / 2-6.
- **Rates where the crews changed.** Iron dressing 400 / 600 / 750 kg a worker (was 400 / 1,000 / 4,000), with the L3 stamps being the `stamp_mill` plan's two batteries. Yards 3,000 / 4,000 / 5,000 kg moved, the crane's 20 t a day being its lift limit. Clay pit 2,000 / 2,500 / 3,000; clay yard 1,500 / 2,500 / 3,000. Saw mill L3 220 bf a worker with the frame's 3,600 bf a day as its limit (was 600). Hauling 3,000 / 4,000 / 5,000 kg a worker over 2 km: its tiers buy distance. The smeltery's L3 hours follow `large-buildings.md`'s machine labour (4 h a unit, 1.5 h a bar batch; was 5 h and 3 h).
- **Tier jumps stay machine-driven.** L3's maximum capacity over L1's is now 5-10 times (it was 5-13; the sheets give 2.3-3.8): the machines keep the jump, and L1 stays small because output per worker is historical. The L3 maxima are at or below the old ones (dressing 12 t against 32 t, saw mill 3,520 bf against 4,800), so the market-absorption reasoning holds.
- **Per-stage tiers** (5.1): a works upgrades the stage that bottlenecks; the L2 iron works runs an L2 smeltery on an L1 mine (crew 19, not about 25).
- **Upgrade costs** (5.3a; IN-27): the sheets' 1.5x (L2) and 2x (L3) govern the hand-built work of every plan; powered machines cost their `large-buildings.md` bills. That puts L2 at 1.5-2.1x and L3 at 4-5x the L1 build, because the machines' iron alone exceeds what 2x allows.
- **L2 and L3 worked rows** (5.11; IN-29): upgrades repay in 10-53 days at the gate price; the floors (62-89% of today's price) say how far the market may fall before an upgrade no longer repays in 2.5 years. The payback target stays the L1 one (2-3 game years at 1x, IN-20).
- **Worker grades** (5.1a): untrained = a hand, skilled = a journeyman or master, mechanised = the tier. Measured: skill 20 against 60 spans 1.18x, not 2x.
- **Drying is the sheets' week** (5.1; IN-12, IN-28): 6 days at every tier in the working seasons; the tiers buy rack space and the winter (L2 12 days in winter, L3 6 all year). Throughput is unchanged at 1,000 / 3,000 / 6,000 bricks a day, so the L1 clay example is unchanged.
- **Iron packs in dry casks** of 100 kg, as on the sheet (5.9; `make_dry_cask` and `dry_cask` new; IN-14). The cask costs what the crate did, so the L1 iron sheet is unchanged.
- **The sheets are cited** from `docs/design/industry-sheets/` (header, INQ-1); INQ-5 is decided.
- **Estimate unchanged** at about 3.8 months.

**Not reconciled, with the reason:**
1. **Works size.** The L1 works stay 9-11 people against the sheets' 75, 69 and 13: with historical output a 75-person L1 iron works would make tonnes of bloom a day against a region that wears out about 70 kg. The L3 maxima (67, 68, 43) come near the sheets' L1 sizes instead.
2. **Tier jumps of 5-10x**, not the sheets' 2.3-3.8x: kept on purpose (Q4, machine-driven).
3. **L3 at 4-5x the L1 build**, not 2x (5.3a).
4. **The sheets' 1 : 2 skilled spread** is not reproduced by skill alone (5.1a); the curve is left alone to keep the R1 prices.
5. **Relative works cost.** Iron (140,000) still costs 4-5 times clay or wood, against about the same on the sheets with the mine excluded. With one payback target and historical output, a works' cost follows its daily net, and iron's is 2-4 times the others'; matching the sheets would make iron repay in under a year or clay and wood in 6 or more.
6. **Untiered stages.** Felling, hauling and shipping keep tiers (the sheets use worker grades and wagon types). Hauling's tiers now buy distance rather than throughput, which is close to the sheets' "wagon x capacity".
7. **Upgrade paybacks at the gate price are weeks**, not years. That follows from historical rates and the candidate prices; the brake is market absorption, which the audit measures (I2) in its second run.
8. **For other specs** (not changed here): `00-phase7-overview.md` 11's "Spec crew bands" column, its 8.5 item 6 and T12 now describe the old tables; `large-buildings.md` 3.2 gives the treadwheel crane "yard handling 20 t a worker-day", which this spec reads as the crane's daily lift; and rope at 2,000 a kg (today's content) is an audit suspect, since it would make a crane's or a hoist's rope cost more than the rest of the machine.

## Notes (P7-IN1, built)
- **Where it lives.** `Sim/Production/Industry/`: `IndustryContent` (chains and stage kinds resolved against the goods; the composite rate), `IndustryBook` (works, stages, links, the 60-day ring), `StageMath` (pure rules), `IndustryWorld` (founding, the day, wages, forwarding, the labour market's view), `IndustryWiring` (`IndustrySystem`, the `in:` desk) and `StageHost` (`IStageHost`: the two hooks Phase 2 asks); `Save/IndustrySave.cs`; content `production/industry/chains.jsonc` (every number of 5.1's three tier tables: crew bands, rates, capacities, `support_pm`, stores; `industry_settings`) and `goods/goods_industry.jsonc`. The stage kinds are also business kinds, appended after every ordinary kind (no index of an ordinary kind moves; `ContentRemapTests.D` lists them as new).
- **The day, as built.** `StageMath.Plan` is 4.1's formula: per primary hand `rate x speed(skill) x weight`, the carriers limit the hewers (a tier's `support_pm`; with no carriers on the books hauling does not limit, Phase 2's rule, which is what IN-16's "2 hewers: 1,000 kg" needs), crowding past the tier's maximum, the span (the owner, masters in the stage's trade and foremen oversee, as in ES1), the machine share, then `min(capacity, crew) x slider / 100`. The mine's "333 a worker on average" is a 500 kg hewer with one carrier per two hewers (`support_pm` 500 / 250 / 125), so 2 hewers and a hand make 1,000 kg. Crowding reads the stage's whole crew. The skill curve is the extraction curve (`800 + 4 x skill`) for the pits, mines, felling and yards and Phase 2's recipe curve (`1,000,000 / (1,300 - 6 x skill)`) for the smeltery, moulding, kiln and saw mill; both are 1,000 at skill 50.
- **Order of a works' day** (one slice a day, at the hour of the works' index modulo 24): arrears are paid first (a stage that cannot pay what it owes does not work and shows `Unpaid`); extraction and process stages work in chain order into their own stores; the hours they could not use go into a pool; then every stage forwards its goods in chain order (handling stages are limited by their own crews plus lent hours, links by their carts, stores by their room); day wages accrue and are paid from the works' purse; the market is asked for tomorrow's inputs. So goods advance one stage a day. Process stages consume real goods from their own store (the main good, charcoal or firewood) and create the outputs; a crusher's tailings are `Waste`, a smeltery's ore and charcoal `Consumed`; the dressed share is drawn on 0x741 keyed by (stage business, batch). Mass is exact per good (`IN-23` checks the ledger against the stores).
- **Spare hands.** A stage's idle worker-hours (the share of its day's potential it did not use, over its whole crew) go into a pool that a handling stage whose first tier takes no hands may draw on at the first tier's rate, only for goods it has beyond its own capacity, and never toward a crew minimum (IN-26: 4 lent hours give an ore yard 1,200 kg).
- **Tier and crew.** `WorkStage.Tier` is the recognised tier; until IN2's recognition exists the owner or a test sets it (`SetTier`). A stage works at the highest tier up to it whose crew minimum its crew reaches (`StageStatus.ShortHanded`, "L2, short-handed: 5 of 6"). The crew target is clamped to the recognised tier's band and the labour market hires to it (`IStageHost.SlotsFor`: a mine's posts are hewers and carriers in the tier's ratio). A works' wage is the class wage by role (never above the stage kind's class), half under 16, times the wage offer.
- **One purse.** The first stage's business purse is the works' purse; the other stages' `Business.Purse` and `Party` are pointed at it (`Rebind`, run at construction and so after a load), their own empty purses staying in the ledger. Founding moves the capital in as `Gift` (a `Capital` record as Phase 2); the owner's cash verb moves coin in as `Gift` and out as `Dividend`. The works' income is what the market paid it between works days: the purse's change since the last works day plus the inputs it was charged (read from each stage business's `InputsSeason`, which stage businesses never reset), less what the owner put in. It is split over the stages that route to the gate. That is exact for the sheet but only as good as the market's own timing; IN3 and IN4 may replace it with per-sale records.
- **Phase 2 and stages.** `ProductionSystem` skips a stage business in its daily slice, work hour, vacancy counters and seasonal step (a works' cash is counted once in the region's coin); the labour market skips them in succession and reopening. A works whose owner is dead is closed by the industry world (coin and goods go to the owner's household). Hero-owned stage businesses have no holding, so the Ledger's Businesses tab does not list them until IN3's tab.
- **The composite rate.** `industry_settings` names the stages (mine and crusher at their first tier) and the dressed share (600): 600 kg for `2 + 1 + 2.5` worker-days is 109,090 g a worker-day. A generated iron mine (`ProductionSystem.MineDay`) now makes that many grams of `iron_ore` a miner a day and the ore body gives up `1 / 0.6` of it. Only iron has a chain; copper, tin, silver, coal and salt mines are unchanged.
- **Reading and deviations.** (1) Rates are in grams, capacities per day, whole units are not enforced (a smeltery may make 4.6 units); IN-26's "4 lent hours" arithmetic needs that. (2) The smeltery stage makes bloom only: the finery's bar (`forge_bar`) is IN2's second process. (3) The drying stage is a handling stage until IN2's drying days and winter rules; the kiln takes `bricks_green` and makes `bricks` at 893 permille. (4) Kiln firewood is 0.5 / 0.4 / 0.35 kg a kg of green brick by tier, the spec's firing formulas flattened; ES2's heat shares and IN2's losses refine it. (5) Packing (containers), shipping carts and the own caravan are IN2 and IN4: the gate route sells at floor 900 to merchants then lists, as Phase 2's `SellOutputs` does. (6) The `works_paid_back` chronicle needs the buildings' bill (IN2), so only `PaidBackDay` is set. (7) New goods have placeholder base prices; the audit's R1 pass reprices them.
- **Not built (left to the later parts).** Tier recognition and plans (IN2); accidents (0x740) and kiln losses (0x742); seasons (winter drying, clay weathering); the machine wiring (`PowerPm`, `ExternalStop` and `Deposit` are hooks, null in IN1: a mine's flood days, wheel frost, an ore body's depletion and the woodland's stock are IN2's to attach); the Industry tab; selling, absorption, contracts and caravans; NPC founders; `IndustryHash` in `phase7_trail` (the trail has no world handle yet, as with ES1; `IndustryBook.Hash()` exists).
- **Re-pins.** `phase5_trail` and `phase6_trail` moved on both golden seeds because the three new goods shift the goods table's indices; with the composite rate switched off they come out the same, so the rate alone moves nothing on the trails' days. The generated-layer and history lines did not move.
