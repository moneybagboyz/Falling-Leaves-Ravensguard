# Large buildings: manufactory halls, the armoury, water- and wind-powered mills, the ropewalk, warehouses, barracks and the granary depot

Status: design, written before any Phase 7 code, on top of b6b39e2. Plan refs: §5 Phase 7 "New large buildings for it, in the same blueprint, room and business system as Phase 5" (manufactory halls with many stations; an armoury, which stores and maintains kit; water-powered mills: fulling mill, hammer mill, sawmill, bellows furnace; a ropewalk; warehouses; barracks; a granary depot for army supply; new build pieces: water wheel, trip-hammer, great bellows; room types the room detector recognises, library plans and business kinds; "on rivers with enough fall for a wheel, recognised from the region's water"); the Industries row (treadwheel cranes in place of cranes, horse gins and water wheels driving pumps, stamps, bellows and trip-hammers; a master machine-wright in place of a mechanic); §3c (weather: frozen rivers, river-high days); §2.2a (multi-storey buildings).

Money in mites (1 cp = 10, 1 sp = 100, 1 gp = 1,000). Power in watts, water flow in litres a second, heights and heads in millimetres.

## 1. Purpose
Economies of scale (`economies-of-scale.md`) need buildings big enough to hold them and machines that multiply a worker's hours. This spec adds the large buildings and the machines as ordinary Phase 5 content: build pieces placed and built by hand, rooms the detector recognises, plans in the library, business kinds that open in them. Water power is real: a wheel's watts come from the river's flow and the fall at that spot, so mills stand where the land allows, stop when the stream freezes, and are wrecked by floods.

## 2. What exists and what is new
| Exists (by file) | Phase 7 |
|---|---|
| `content/base/build/pieces.jsonc`: `waterwheel` (60 worker-hours, 600 kg logs, 300 kg planks, 20 kg iron, tag `waterwheel`), `brake_wheel` (gearing), `millstone`, `potters_wheel`; `pieces_workstations.jsonc` (bloomery, cupel hearth, brick clamp, lime kiln, ...) | extended: about 26 new pieces (3.2); the existing `waterwheel` is the breastshot wheel |
| `Building/RoomDetectorV2.cs`, `content/base/build/rooms.jsonc` (40 room types incl. `workshop_milling`, `granary`, `barn`, `storage`, `guardroom`, `yard`) | extended: 11 room types (4.4), appended with priorities above their small cousins |
| `Construction/PlanLibrary.cs`, `PlanFitter.cs` (no water check: phase-6 overview moved "a water check in PlanFitter" to Phase 8) | the water check is built here **for powered plans only** (4.6); other plans stay as Phase 8 has them |
| `Production/BusinessKind` (`water_sawmill` with `labour_pm` 125 and no wheel needed) | kinds added (4.4); `water_sawmill` migrates to `saw_mill` (`ContentRemap` migration entry) and its 125 applies only while powered |
| `WorldGen/Region/RegionModels.Waterway` (points upstream to downstream, `WidthCm`, `DepthCm`, `CatchmentTenthsKm2` at the downstream end, `LengthM`), the region's tile heights (`RegionBuilder.HeightAtGlobal`), `content/base/vehicles/water.jsonc` (width classes, high days) | read by `WaterPower` (4.1) |
| `Weather/RegionWeather` (temperature, wind, precipitation, river-high days) | flow by season, frozen days, wind power |
| `Goods/Corrosion.cs`, items' condition and the 30-day `oil_item` (Phase 6 holdings 3.8), `Goods/Store` kinds and spoilage rates (`goods/spoilage.jsonc`: grain 670 ppm a day in a granary) | the armoury's care, the warehouse's and depot's rates |
| **New** | `Building/WaterPower.cs` (sites, heads, flow, wheel output), `Building/PowerNet.cs` (wheel -> shafts -> machines, derived from pieces), `Building/WindPower.cs`, `Production/Armoury.cs`, `Production/Depots.cs`, `Production/Warehouses.cs`, `Save/WorksSave.cs`; content `build/pieces_machines.jsonc`, `build/rooms_large.jsonc`, `blueprints/industry.jsonc`, `production/large_buildings.jsonc` |

## 3. Data

### 3.1 Derived (never saved)
- **`WaterSite`** per tile next to a waterway: the reach's flow and the best head reachable from it (4.1), cached per waterway.
- **`PowerNet`** per wheel or windmill piece: the shaft cells linked to it (orthogonal runs of `line_shaft` and `cam_shaft` pieces, at most 12 m from the wheel) and the machine pieces they reach; the watts available today; each machine's share.

### 3.2 New pieces (`build/pieces_machines.jsonc`; built by hand like every piece; worker-hours at the named skill; `min_skill` 30 unless said; the machine-wright is a carpenter with the `millwright` proficiency, new)
| Piece | Role | Materials | Worker-hours | Notes |
|---|---|---|---|---|
| `undershot_wheel` | wheel, eta 200, rated 3,000 W | 500 kg logs, 250 kg planks, 15 kg iron | 50 carpentry | head >= 300 mm on a `river` or `great_river` |
| `waterwheel` (exists) | breastshot, eta 400, rated 4,000 W | as now | 60 | head 1,000-2,500 mm |
| `overshot_wheel` | eta 600, rated 5,000 W | 700 kg logs, 400 kg planks, 30 kg iron | 80, millwright | head >= 2,500 mm, needs a `leat` |
| `weir` | +1,000 mm of head on a stream <= 10 m wide | 4,000 kg stone, 300 kg logs | 60 masonry | in the stream's cells |
| `leat` (per 1 m) | channel keeping its level from a weir | none (dug) | 2 labouring a metre | head = the stream's fall over the leat's length, at most 4,000 mm |
| `sluice` | stops or starts the wheel | 80 kg planks, 5 kg iron | 8 | a switch on the machine panel |
| `line_shaft` (1 m) | carries power | 60 kg logs, 2 kg iron | 4, millwright | |
| `cam_shaft` (1 m) | drives hammers, stamps, stocks | 80 kg logs, 6 kg iron | 8, millwright | |
| `trip_hammer` | machine 2,000 W; forge `labour_pm` 375 | 400 kg logs (helve), 60 kg iron (head and bands), 300 kg stone (anvil block) | 80 carpentry + 20 smithing, min 40 | |
| `great_bellows` | machine 1,000 W; bloomery capacity x2, labour 500 | 120 kg planks, 40 kg leather, 10 kg iron | 30 carpentry + 10 leatherwork | |
| `fulling_stocks` | machine 1,500 W; fulling 125 | 300 kg logs, 40 kg planks, 8 kg iron | 40 | |
| `gang_saw_frame` | machine 3,000 W; sawing 125 | 300 kg logs, 150 kg planks, 40 kg steel blades | 60, millwright | |
| `frame_saw` | human sash saw (L2 sawing, `industries.md`) | 150 kg planks, 15 kg steel | 24 | crew 3-4 |
| `stamp_battery` | machine 2,500 W; crushing 100, 6 t a day | 600 kg logs, 120 kg iron (shoes) | 70, millwright | |
| `pug_mill` | machine 1,500 W (or a gin); clay tempering 100 | 200 kg planks, 20 kg iron | 30 | |
| `hoist_drum` + `rag_pump` | mine hoist and drainage, 2,000 W | 300 kg logs, 150 kg rope, 40 kg iron | 60, millwright | flood days 0 |
| `horse_gin` | 1,000 W from 2 horses (fodder at their working rate) | 500 kg logs, 20 kg iron | 40 | drives a hoist, pug mill or crusher |
| `treadwheel_crane` | lifts 1,000 kg with 2 walkers; yard handling 20 t a worker-day | 800 kg logs, 200 kg planks, 100 kg rope, 30 kg iron | 100, millwright | no water |
| `windmill_post` | wind machine, rated 4,000 W; millstones and pumps only | 3,000 kg logs, 800 kg planks, 200 kg cloth (sails), 60 kg iron | 300, millwright | needs open ground, no building within 30 m upwind (west) |
| `rope_wheel`, `rope_traveller` | ropewalk stations | 80 kg planks, 5 kg iron each | 12 each | |
| `armour_stand`, `weapon_rack`, `arms_shelf`, `arms_chest` | armoury storage | 20-40 kg planks | 4-6 each | capacities in 4.4 |
| `granary_bin`, `staddle_floor` (per 4 m2) | depot storage, raised floor | 200 kg planks; 600 kg stone + 100 kg planks | 10; 12 masonry | |
| `bunk` | barracks bed for 2 | 60 kg planks | 4 | |
| `warehouse_bay` | 6 m3 of stacked storage | 150 kg planks | 6 | |

## 4. Rules

### 4.1 Water power
- **Flow** at a tile on waterway `w`: `Q_lps = catchment_tenths_km2(point) x 15 / 10 x season_pm / 1,000`, where the catchment at a point is the waterway's downstream catchment scaled by the point's distance from the source over `LengthM` (deterministic, integer), 15 L/s per km2 is the temperate mean, and `season_pm` is spring 1,600, summer 600, autumn 1,000, winter 800. **Frozen:** after 3 days below -5 C at the settlement nearest the wheel, `Q = 0` until 2 days above 0 C. **River-high days** (`water.jsonc`): flow x 2 and the flood risk (4.7).
- **Head** at the wheel: with no leat, the fall of the waterway within 30 m upstream (tile heights); with a `leat` of length L (<= 300 m) from a weir, the waterway's fall over L, capped at 4,000 mm; a `weir` adds 1,000 mm on a stream <= 10 m wide (`WidthCm` <= 1,000).
- **Wheel output:** `P_W = min(rated, 981 x Q_lps x head_mm / 100,000 x eta / 1,000)`, with the wheel's eta and the head band of 3.2 (a wheel outside its band gives 0 and the build ghost says why).
- **Worked example (LB-1):** a stream of 20 km2 catchment at the point (200 tenths), autumn: Q = 300 L/s; a weir and a 120 m leat give 2,000 mm; breastshot: `981 x 300 x 2,000 / 100,000 = 5,886 W` of water, `x 400 / 1,000 = 2,354 W` at the shaft: one trip-hammer (2,000 W) with 354 W spare; in summer (Q 180) 1,412 W: the hammer runs at 70% of its effect.
- **Sharing:** machines on one `PowerNet` that are working draw their rated watts; when the sum exceeds `P`, each runs at `P / sum`, and its station's labour is `1,000 - (1,000 - labour_pm) x min(1, P / sum)`. The sluice stops the wheel (machines idle, no wear).

### 4.2 Wind and animal power
- **Windmill:** `P_W = 19 x v^3` for wind v in m/s (a 20 m rotor), 0 below 3 m/s, capped at 4,000 W, stopped above 15 m/s (sails furled; storm days). Only millstones and drainage pumps take wind power (historical).
- **Horse gin:** 1,000 W while its 2 horses work (8 working hours a day at their working fodder); drives a hoist, a pug mill or a crusher.
- **Treadwheel crane:** human power: 2 walkers of the yard crew; no watts, a handling rate (`industries.md` yards L3).

### 4.3 Machines and their stations
A machine piece couples to the nearest station piece of its kind within 3 m (a trip-hammer to a forge's anvil, stamps to a dressing floor, stocks to a fulling tub, a gang saw to a saw bench, great bellows to a bloomery). While powered, that station uses the machine's `labour_pm` and other effects (`economies-of-scale.md` 4.5). Machines wear (4.7). The **machine-wright** (`millwright` proficiency, carpentry >= 40) builds and repairs wheels, shafts and machines; a works with powered machines keeps one (or hires one by the day) or its breakdowns wait.

### 4.4 The large buildings: room types, capacities, business kinds
Room types are appended to `rooms.jsonc` (in `rooms_large.jsonc`), each with a priority above the small room it would otherwise read as.
| Room type | Recognised when | Capacity and effect | Business kind |
|---|---|---|---|
| `manufactory_hall` | roofed, >= 120 m2, >= 6 workstations of one station family, light (windows or lamps) on every 20 m2 | crowding floor 3 m2 a worker (instead of 4); overseers' span +4 (one view across the floor) | none new: an existing kind (smithy, weavery, carpentry, cobbler...) whose premises include a hall gets the hall's effects; the guild rule applies (`economies-of-scale.md` 5.3) |
| `hammer_mill` | a `trip_hammer` coupled to a forge in the room | the forge's `labour_pm` 375 while powered | `hammer_mill` (smith trades) |
| `fulling_mill` | `fulling_stocks` coupled to a fulling tub | fulling 125 | `fulling_mill` |
| `saw_mill` | `gang_saw_frame` (powered) or `frame_saw` | sawing 125 (gang) or 500 (frame) | `saw_mill` (supersedes `water_sawmill` by a content migration) |
| `furnace_house` | a bloomery with `great_bellows` | capacity x2, labour 500 | `furnace_house` |
| `stamp_mill` | a `stamp_battery` and a dressing floor | crushing 6 t a day a battery | `stamp_mill` |
| `ropewalk` | roofed or open-sided, length >= 60 m, width 3-6 m, a `rope_wheel` at one end and a `rope_traveller` | `lay_rope`: 100 kg of hemp -> 90 kg of rope, crew 4 (1 journeyman, 3 hands), 12 h a batch; rope length up to the walk's length | `ropewalk` |
| `warehouse` | roofed, >= 60 m2, >= 6 `warehouse_bay` or storage pieces | `floor_m2 x 1,500` litres stacked; spoilage: grain at the granary rate, packed goods at the sealed rate, other goods at half the house rate; the owner rents space at a set price a m3 a season (default 20 mites) | `warehouse` |
| `granary_depot` | roofed, >= 80 m2, a `staddle_floor` or stone floor, >= 4 `granary_bin`, one door | 750 kg of grain a m2 of floor; grain spoilage 670 ppm a day (1% a season); rotation policy (sell what is older than 2 seasons at the harvest market) | `granary_depot` (salaried, the lord's or a town's; `army-economy.md` 4.6) |
| `armoury` | roofed, >= 20 m2, a lockable door, >= 2 `armour_stand` or `weapon_rack` | stand: 1 body armour and its helmet; rack: 10 weapons (<= 3 kg) or 4 polearms; shelf: 10 helmets or 6 shields; chest: 40 sheaves of arrows | `armoury` (salaried, a lord's, a town's or a company's) |
| `barracks` | roofed, >= 4 `bunk` (8 men), >= 3 m2 a man, an arms rack in the room and a hearth in the building | beds = 2 a bunk; upkeep 1 kg of straw a bed a 15 days and firewood as a dwelling room; the military assigns soldiers (`army-economy.md` 3) | none (a building of the lord's household) |
| `mint_house` (`coinage.md`) | roofed, lockable, a strongbox and a minting bench | a realm's mint | (office) |

**The armoury's care:** an armourer (smithing >= 30) keeps 300 items; each item needs 0.1 h every 30 days (oil, whet, new straps: `oil_item` and repair steps). A cared item corrodes and rots at the sealed rate; an uncared one at the house rate, doubled in a room with no hearth in the building in winter. The **armoury view** lists items by kind, quality tier and condition, and the gaps against a loadout template; its `Issue(request)` and `Return(items)` are what `HostSupply.Kit` and `ReturnKit` call for the military's loadout filler (`army-economy.md` 3.3).

### 4.5 Library plans (`blueprints/industry.jsonc`; fitted and built hands-on as every Phase 5 plan)
| Plan | Footprint | Holds | Materials (approx.) | Worker-hours | Power need |
|---|---|---|---|---|---|
| `hammer_mill_small` | 8 x 10 m, timber on stone | breastshot wheel, brake wheel, cam shaft, 1 trip-hammer, forge, anvil | 3,000 kg logs, 1,500 planks, 4,000 stone, 150 iron, 20 nails | 900 | 2,000 W |
| `fulling_mill` | 8 x 8 m | wheel, cam shaft, 2 fulling stocks, 2 tubs | 2,500 logs, 1,200 planks, 3,000 stone, 80 iron | 700 | 3,000 W |
| `saw_mill_gang` | 10 x 14 m | wheel, gang saw frame, log carriage, yard | 4,000 logs, 1,800 planks, 3,000 stone, 120 iron, 40 steel | 1,100 | 3,000 W |
| `saw_shed_frame` | 6 x 12 m | 2 frame saws (no water) | 1,500 logs, 900 planks | 300 | - |
| `furnace_house` | 10 x 10 m, stone | 1-3 bloomeries, great bellows each, wheel | 2,000 logs, 600 planks, 12,000 stone, 2,000 clay, 200 iron, 80 leather | 1,400 | 1,000 W a furnace |
| `stamp_mill` | 8 x 12 m | wheel, 2 stamp batteries, buddles | 3,500 logs, 1,000 planks, 4,000 stone, 300 iron | 1,000 | 5,000 W |
| `ropewalk_120` | 120 x 5 m open-sided shed | rope wheel, traveller, hemp store | 6,000 logs, 3,000 planks, 1,200 thatch | 1,200 | - |
| `warehouse_stone` | 12 x 20 m, two floors | 40 bays | 4,000 logs, 6,000 planks, 40,000 stone, 2,000 lime | 2,600 | - |
| `granary_depot` | 10 x 20 m on staddles | 16 bins (150 t) | 3,000 logs, 5,000 planks, 12,000 stone, 2,000 thatch | 1,500 | - |
| `armoury_keep` | 8 x 10 m, stone, lockable | 10 stands, 12 racks, 8 shelves, 4 chests, a bench | 1,500 logs, 1,200 planks, 25,000 stone, 60 iron | 1,600 | - |
| `barracks_40` | 10 x 20 m | 20 bunks, 2 hearths, racks | 3,000 logs, 3,500 planks, 15,000 stone, 1,500 thatch | 1,400 | - |
| `manufactory_hall_smiths` | 12 x 24 m | 3 forges, 6 anvils, benches, a grindstone | 5,000 logs, 3,000 planks, 30,000 stone, 1,500 lime, 200 iron | 2,400 | - |
| `windmill_post` | 6 x 6 m on open ground | post mill, millstones | as the piece + 2,000 stone | 420 | wind |
Generated towns get a granary and a guardroom as today; from Phase 7 a realm's seat also gets an `armoury_keep` and a `granary_depot` at the Phase 7 seeding step (keyed on the region seed and settlement id), stocked as `army-economy.md` 4.6 and 4.10 say.

### 4.6 Siting on rivers with enough fall
- The **water check** for powered plans: `PlanFitter` places a plan that needs power only where the wheel's cell lies on a waterway's bank and `P_W` at **autumn** flow, with the plan's wheel and any weir and leat the plan includes, meets the plan's power need; otherwise the plan's ghost shows "not enough fall here: 1,180 W of 2,000 W" and is refused.
- The build mode's **water overlay** (a button on the overlay bar; `--check-keymap` decides whether the Build context gets a key) shades each bank tile by the best watts reachable in autumn and summer, with the head and wheel type in the tooltip.
- **The region's mill sites** (the Industry tab's "find a site" list and the NPC founders' list) are derived: bank tiles with >= 2,000 W in autumn, at least 200 m apart, sorted by watts.

### 4.7 Wear, breakdowns, floods
- Machines and wheels lose condition as pieces: 1 hp a running day plus a breakdown with P = 2 permille a running day (stream **0x730**, key piece, day): -20% hp (a cracked helve, a broken cam). Below 50% hp a machine runs at half effect; at 0 it stops. Repair is real work (the machine-wright, materials from the piece's recipe in proportion).
- **Floods:** on a river-high day a wheel without its sluice shut takes damage with P = 5% (stream **0x731**, key wheel piece, day): -30% hp; chronicle `wheel_wrecked_flood` when it reaches 0.
- **Ice:** frozen days stop the wheel (no damage).

## 5. Saves
- Pieces, their hp and the sluice's state are construction's (the `construction` section's piece rows; the sluice is a piece state flag, additive). Water sites, power nets and the region's mill sites are derived.
- Optional section **`works`**, absent while empty: warehouse leases (warehouse, tenant, m3, rent, paid seasons), depot policies (depot, target t, rotation on or off), the armoury's issue book (armoury, item id, holder, issued day). `WorksHash` joins the trail once non-empty.
- New room types, pieces, plans and business kinds are content (`ContentRemap` by name; `water_sawmill -> saw_mill` by a migration entry; a Phase 5 water sawmill with no wheel keeps running at `labour_pm` 1,000 and the load report says so). No version bump.

## 6. RNG streams
| Stream | Use | Key |
|---|---|---|
| 0x730 | machine and wheel breakdowns | piece, day |
| 0x731 | flood damage on river-high days | wheel piece, day |
Free: 0x732-0x73F.

## 7. Chronicle
| Type | Cause | Root | Key | Parameters |
|---|---|---|---|---|
| `works_powered` | Trade | yes | `ch_works_powered` | owner, works, river, watts |
| `wheel_wrecked_flood` | Weather | no (the river-high day's weather entry) | `ch_works_wheel_flood` | works, river |
| `depot_filled` | Supply | yes (seasonal, a lord's depot passing its target) | `ch_works_depot_filled` | lord, depot, tonnes |
`CauseKind.Supply` is appended in `army-economy.md` 6.

## 8. UI (960 x 540)
- **Build mode:** the water overlay (4.6); the ghost of a powered plan shows watts available against needed by season.
- **Machine panel** (Q on a wheel or windmill): watts now and by season, flow and head, the machines on its net with their share, condition, the sluice switch, "next breakdown risk" text, the machine-wright assigned.
- **Armoury panel** (Q on a rack or stand, or from the Ledger): items by kind, tier and condition; care due; gaps against a chosen loadout template; issue and return lists.
- **Warehouse and depot panel:** capacity used, rents or the reserve target, spoilage this season, rotation.

## 9. Golden tests
| id | Case | Expected |
|---|---|---|
| LB-1 | The worked example of 4.1, autumn and summer | 2,354 W and 1,412 W |
| LB-2 | Frozen: 3 days at -8 C | Q 0, wheel stopped, no damage; back after 2 days above 0 |
| LB-3 | Head bands: an overshot wheel at 1,800 mm | 0 W, the ghost names the band |
| LB-4 | Sharing: 2,354 W for a trip-hammer (2,000) and great bellows (1,000) | each at 785 permille; forge labour 1,000 - 625 x 0.785 = 509 |
| LB-5 | Windmill at 6 m/s, 2 m/s, 16 m/s | 4,000 W (19 x 216 = 4,104 capped), 0, 0 |
| LB-6 | Plan fitting: `hammer_mill_small` on a bank with 1,180 W in autumn | refused, "not enough fall here: 1,180 W of 2,000 W"; placed on a 2,400 W bank |
| LB-7 | Mill sites on seed 1234 | the list is non-empty, sorted by watts, sites >= 200 m apart; equal on two runs |
| LB-8 | Manufactory hall recognised: 12 x 24 m with 6 anvils and light | `manufactory_hall`; crowding floor 3 m2; spans +4 |
| LB-9 | Ropewalk: 70 x 4 m with wheel and traveller; 50 x 4 m | recognised; not recognised |
| LB-10 | Armoury capacity and care: 10 stands, 12 racks; 300 items, one armourer | 10 armours, 120 weapons; care 30 h a 30 days; an uncared mail shirt corrodes at the house rate |
| LB-11 | Granary depot 10 x 20 m | 150 t; 1% a season; rotation sells grain older than 2 seasons at harvest |
| LB-12 | Warehouse 12 x 20 m two floors | 720,000 litres; packed goods at the sealed rate |
| LB-13 | Breakdown draws on 0x730, flood on 0x731 | a forced draw: hp -20%; a flood day with the sluice open: -30%, shut: none |
| LB-14 | `water_sawmill` from a Phase 5 save with no wheel | migrated to `saw_mill`, `labour_pm` 1,000, a load report line |
| LB-15 | Machine wiring: a trip-hammer 4 m from the forge, 13 m of shaft from the wheel | not coupled (shaft beyond 12 m); at 10 m coupled |
| LB-16 | Determinism and saves: a year with a hammer mill, a depot and an armoury issuing kit | `WorksHash` equal; save, load, save identical; mass exact |

## 10. Budgets
Water sites per waterway once (cached, <= 2 ms a region on first overlay); a wheel's watts once a game day (<= 1 us); power nets rebuilt only when a piece in them changes; armoury care once a day an armoury (<= 0.1 ms); `works` <= 30 KB a region.

## 11. Open questions (recommended defaults)
| # | Question | Default |
|---|---|---|
| LBQ-1 | Mean flow a km2 of catchment | 15 L/s (temperate); content `water_power.jsonc` |
| LBQ-2 | Windmills in Phase 7 | yes, for milling and drainage only |
| LBQ-3 | Hemp for the ropewalk | a new crop row in `farming` content (fibre, like flax), and an import good at the gate until grown |
| LBQ-4 | Barracks: economy or military? | the room and its upkeep here; who sleeps there is the military's |

## 12. Estimate and parts
| Part | Content | Gate | Months |
|---|---|---|---|
| **P7-LB1** power | `WaterPower`, `WindPower`, `PowerNet`, the 26 pieces, machines coupled to stations, the water check in `PlanFitter`, the overlay and the machine panel, wear, breakdowns, floods, ice, the `water_sawmill` migration | LB-1 to LB-7, LB-13 to LB-15 | 1.2 |
| **P7-LB2** buildings | the 11 room types, the hall's effects, armoury care and the issue book, warehouse leases and spoilage, the depot's capacity and rotation, ropewalk and `lay_rope`, barracks upkeep, the 13 plans, the business kinds, the seeding of seats' armouries and depots, the panels | LB-8 to LB-12, LB-16 | 1.0 |
**About 2.2 months of code.** Art: about 26 pieces (the wheels animate in 4 frames), 13 plans (data only).
