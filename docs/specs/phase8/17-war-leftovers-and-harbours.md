# The war leftovers and harbours: player-built and generated harbours, ships that call to trade, the helm, fire arrows, barge cargo, prisoners from storms

Status: design, on top of def9d6f. Part **P8-LW-5** (lane B, 0.9 months).
Plan refs: §3h (water and ships: harbours generated in towns and buildable by the player on his own shore land, quay, moorings, warehouse, crane; the holder collects the customs, decided with the user 28 Nov 2026; sea trade from the continent tick), §3j (customs at ports; every payment a real transfer), §2.9 (blueprints and the room detector), §5 Phase 7 (harbours, sea trade).
Carried in: the overview §2.5 rows for P8-LW-5, and the "not done" of P8-LW-4 (no harbour blueprints, voyages carry no cargo, the game does not map a fire arrow onto the fire world, an abstract storm's yielded men are not taken) and P8-WS-1 (`HelmView.Of` has nothing to read).

## 1. Goals
1. The hero builds a harbour by hand from a ready-made plan on his own shore land; the room detector knows it; he opens a harbour business; ships call, trade and pay him the customs.
2. AI lords post the same plan as a construction contract; new worlds get generated harbours in coastal towns; old worlds keep the Phase 7 route and their bytes.
3. The four leftovers close: the helm HUD on a ship the hero sails, fire arrows in the fire world, barges that carry cargo, prisoners from abstract storms.

## 2. What exists and what is new
| Today (def9d6f) | P8-LW-5 |
|---|---|
| `Naval/Harbours.cs`: `Beaches`, `Mooring`, `Harbour` (moor, warp, `PayCustoms`, `Unload` by porters and crane); nothing creates a `Harbour` | **made** from a recognised `harbour` room, from a generated harbour, or by the Phase 7 seeding step; saved in `naval` |
| `room_type` rows incl. `warehouse` (60 m2, 6 `warehouse_bay`), `slipway` (open, unroofed); `RoomWorld.WaterAt` (never set) | **new** room type `harbour`, rule `water_edge`; the game sets `WaterAt` |
| pieces `treadwheel_crane` (tag `crane`), `warehouse_bay`; plan `warehouse_stone` | **new** pieces `quay_fill`, `quay_edge_timber`, `quay_edge_stone`, `bollard`; plans `harbour_quay`, `harbour_quay_crane`, `warehouse_timber` |
| `ConstructionWorld.PlacePlan`, `Post(..., ContractKind.Civic, ...)`, `OwnershipWorld.Found` (station kinds only) | the fitter's water-side check; a room-founded business kind; `HarbourPlanner` posts Civic contracts |
| `ContinentTrade` (land gates), `RuleCustoms` (realm treasury), `SeaEdges` (derived, used by raids only) | **sea gates**: a working harbour is a gate on its sea edges; the duty goes to the holder |
| `Phase7Seeding.PlannedOrder` ends in `harbours`; no step registered | **registered**: `HarbourSeedStep` |
| `RegionSettings.LandmarkPlacer` (0/1 = v1, 2 = v2) | **new** `RegionSettings.HarbourPlacer` (0 = none, 1 = generated) |
| `HelmView`, `UI/HelmHud.cs` (dev `SetHelm` only); `Sailing.Tick` | `Naval/HeroHelm.cs`: the hero sails a sea ship he drives |
| `Fire/FireArrows.Strike`, `FightWorld.FireArrowLanded` (no setter) | `Fire/FireArrowWiring.cs` set by `BattleBubble.Open` |
| `ArmyWorld.SeaTrade.cs` barges shuttle empty; `RegionalTrade` moves lots at once | lots between barge-served markets ride in the barge's hold |
| `SiegeWorld.Resolve` (abstract storm) kills, takes nobody | the yielded go to `TakePrisoners` |

## 3. Rules and shapes

### 3.1 Pieces, the room type and the plans
Pieces (append to `build/pieces_machines.jsonc`):
```jsonc
{"type":"build_piece","id":"quay_fill","name":"Quay apron (rammed fill)","kind":"floor","glyph":",","tags":["quay"],"build":{"skill":"masonry","worker_hours":2,"materials":[{"good":"stone_raw","kg":200}],"hp":400,"salvage_pm":300,"min_skill":0}},
{"type":"build_piece","id":"quay_edge_timber","name":"Timber quay front (piles and planks)","kind":"floor","glyph":"=","material":"oak","tags":["quay","quay_edge"],"build":{"skill":"carpentry","worker_hours":6,"tools":[{"quality":"chopping"},{"quality":"sawing"}],"materials":[{"good":"logs","kg":250},{"good":"timber_planks","kg":40},{"good":"nails","kg":1}],"hp":500,"salvage_pm":600,"min_skill":30,"effort":"heavy"}},
{"type":"build_piece","id":"quay_edge_stone","name":"Stone quay front","kind":"floor","glyph":"#","material":"granite","tags":["quay","quay_edge"],"build":{"skill":"masonry","worker_hours":10,"tools":[{"quality":"chiselling"}],"materials":[{"good":"stone_dressed","kg":1500},{"good":"quicklime","kg":60}],"hp":2000,"salvage_pm":500,"min_skill":40,"effort":"heavy"}},
{"type":"build_piece","id":"bollard","name":"Mooring post","kind":"furniture","glyph":"o","material":"oak","tags":["mooring_post"],"build":{"skill":"carpentry","worker_hours":3,"tools":[{"quality":"chopping"}],"materials":[{"good":"logs","kg":80}],"hp":300,"salvage_pm":700,"min_skill":10}}
```
Every good, material and tool quality named here is in the content today (`stone_raw`, `logs`, `timber_planks`, `nails`, `stone_dressed`, `quicklime`; `oak`, `granite`; `chopping`, `sawing`, `chiselling`).

Room type (append to `build/rooms_large.jsonc`): `{ "type": "room_type", "large": true, "id": "harbour", "name": "Harbour quay", "purpose": "harbour", "priority": 59, "enclosed": false, "roofed": false, "min_area": 120, "min_length": 24, "water_edge": 24, "requires": [{ "tag": "quay_edge", "min": 24 }, { "tag": "mooring_post", "min": 2 }] }`.
- **`water_edge`** (new `RoomTypeDef` int, append; 0 = off, so no type changes): at least this many of the room's cells carrying `quay_edge` have a 4-neighbour outside the room that is water. `RoomWorld` (v2) asks `WaterAt`; `RoomDetector` (v1, plans) treats the cells beyond the plan's `water_side` as water. Both read it through `RoomShape`, so RD-1 parity holds.
- **Moorings of a room** = `min(bollards / 2, waterEdgeCells / 24)`, laid along the water edge in edge order (the edge cells sorted by the coordinate along it, then the other); each mooring's point is the water cell 4 m off the middle of its 24 m.
- **Open quays join:** two modules side by side flood into one room, so two plans make two moorings.

Plans (new file `blueprints/harbour.jsonc`; `cultures` empty = every culture; `open_air`; new `BlueprintDef.WaterSide` string, append, "n", "e", "s" or "w", null = none). `harbour_quay` (24 x 6, water to the south): rows 0 to 4 `,` (`quay_fill`), row 5 `=` (`quay_edge_timber`); fixtures `o` at (2,5) and (21,5). `harbour_quay_crane` is the same with `C` (`treadwheel_crane`) at (11,3). `warehouse_timber` (12 x 9, poor, `rooms {"f":"storage"}`): `wall_timber` ring, `floor_plank`, one `door_barn` on the south wall at (5,8), six `warehouse_bay` along the north wall at (2..7,1); its interior is 70 m2, so it reads as `warehouse`.
```
harbour_quay   rows (legend , quay_fill  = quay_edge_timber)   fixtures (o bollard)
  y 0..4       ,,,,,,,,,,,,,,,,,,,,,,,,                        ________________________
  y 5          ========================                        __o__________________o__   <- water_side "s"
```
The crane is optional: build the crane plan, or place a `treadwheel_crane` on a quay later by hand; a crane in the room sets `Harbour.Crane`.

### 3.2 Building it by hand (Phase 5 build mode)
- The three plans list in the plan library under "Harbour" for every culture. The hero places one like any plan; the preview refuses, before placing:
  - any cell the land rule refuses (`LandRegistry.May(hero, Build)`: his deed or his household's; a lease or a claim is not "own land");
  - a plan with a `water_side` whose 24 cells beyond that side are not all water (`cn_needs_water_side`; the fitter's own narrow check, not the general water check of P8-MD-2).
- He builds it hands-on, with followers and hired hands, as any Phase 5 site; materials from the pile, mass exact.

### 3.3 The harbour and its business
- **`HarbourRow`** (naval book): `Id`, `Settlement` (nearest within 2 km; -1 none), `Source` (Built, Generated, Seeded), `Structure` (RoomWorld structure of the quay; 0 when seeded), `Moorings` (point and ship), `Warehouse` (the store of a `warehouse` room whose nearest cell is within 60 m of a quay cell, the same holder; seeded harbours keep an abstract store), `Crane`, `Business`, `Calls` in and out this season, `DutySeason`. The runtime `Harbour` object is built from it on load; ids: generated and seeded `1 + settlement x 8`, built from `NextHarbour` (starts at 1,000,000).
- **Business kind `harbour`** (`production/businesses.jsonc`): `{"id":"harbour","name":"Harbour","owner_occupation":"Merchant","stations":[],"staff_slots":[{"occupation":"Labourer","count":4,"wage_class":"unskilled"}],"recipes":[],"founded_only":true,"room":"harbour"}`. New `BusinessDef.Room` (append, null = none): a kind founded on a recognised room of that type instead of stations. `OwnershipWorld.Found` takes it when the structure holds the room and a warehouse is in reach (`hb_no_warehouse` otherwise); the staff are the porters (`Harbour.Porters` = staff at work this hour).
- **The business's takings** (content on `sea_rules`, all to the business purse by `TransferReason.Fee`, from the ship's purse): quayage 150 mites a ship a day moored or part of one (a labourer's day, as keelage and anchorage were a flat due a ship); porterage 30 mites a tonne moved by porters (their wage is about 16 a tonne: 150 a day for 8 h at 1.2 t an hour); cranage 20 a tonne moved by the crane. A harbour that is not staffed still moors ships but nothing is hauled.

### 3.4 Ships call to trade
- **Sea gates.** `ContinentTrade` treats each working harbour (business open, a free mooring, the region on at least one sea edge of `SeaEdges.Of`) as a gate whose neighbours are the regions across its sea edges. The planner prices a sea trip as the cart leg from the merchant's market to the quay (`CaravanRoads`) plus the sea freight: the calling ship's crew wages for `LengthKm / sail_km_per_day` days each way at the labourer's rate. No gate toll, no road danger beyond the cart leg. It chooses a sea gate when that margin beats the land gate's (the same rule, `MinMarginPct` 8).
- **The call.** A `cog` (100 t hold) is made in the ride book at the region's sea edge facing the neighbour, its master and crew of 12 made as persons by the hosts' gate rule (stream 0x861 for their draws), and it sails in, warps the last 100 m (0.3 m/s) and moors in the nearest free mooring (`Harbour.Moor`). The neighbour's summary buys the export (inside the season's `EdgeTrade` budget, as at a land gate) and the back-haul is bought from it. The goods move between the trip's carts at the quay (or the warehouse, where carts that came early leave them as the trip's consignment, a sub-store keyed by trip id) and the hold by `Harbour.Unload` and a new `Harbour.Load` (its reverse, the same rate); the ship pays its duty (3.5) and fees and sails when the hold holds only the back-haul and the duty is paid (`MayLeave`), the crew leaving as emigrants. Goods crossing are booked imported and exported exactly as at a land gate.
- **Full harbour:** a trip is planned only for a day a mooring is free; ships never double-moor. A ship that cannot pay its duty does not unload (the Phase 7 rule) and leaves after 2 days with its hold.

### 3.5 Customs to the landholder
- The duty is `RuleCustoms.RatePct(realm, good, export)` (the realm's rate; 3% default, wool out 10%, wine in 5%) of the declared value, paid by `Harbour.PayCustoms` from the ship's account to the **holder of the quay's land**: the deed holder of the quay's first cell in (y, x) order (a person's purse, a household's, or the fief's treasury for fief land or a town's common). It does not pass the realm's collector, and the realm takes no share (the 28 Nov decision gives the holder the customs; read here as the whole duty). A holder who rules the realm sets the rate as in Phase 6.
- The Ledger's Fief tab (and, for a hero without a fief, the Businesses tab on the harbour's row) gets a line "harbour customs" by harbour; `HarbourRow.DutySeason` feeds it.

### 3.6 AI lords post the plan
`HarbourPlanner` (H6, season's first day, hour 6, after the war supply's season): in fief id order, a lord whose fief has a coastal town or village (sand or rock shore within 500 m) with no harbour row, whose treasury holds 1.5 x the plan's cost (materials at the market's ask plus `LabourMites`), and who holds shore land there, buys the materials of two `harbour_quay` modules and one `warehouse_timber` into a pile, places the plans by the fitter at the nearest fitting shore run to the settlement's centre (ties by tile, no draw), and posts each site as `ContractKind.Civic` (one at a time a fief). The hero may take it; otherwise a builder of the region does after the notice days. On handover the lord's household founds the harbour business. At most one new harbour a fief a year.

### 3.7 Generated harbours in new worlds (`HarbourPlacer`)
- **Versioned like LM-1.** `RegionSettings.HarbourPlacer` (and `RegionSettingsDef`; absent in a save means 0 and 0 is never written, so older saves keep their bytes); `content/base/worldgen/region.jsonc` gets `"harbour_placer": 1`; `Region.HarbourVersion` carries it. Library worlds and every world made before LW-5 read 0.
- **Placer v1** (a pass after `LandmarkPlacer`, pure of region seed and content): every town (not villages: plan §3h "generated in towns") with shore within 500 m, in settlement index order. The candidate is the land tile beside the sea nearest the centre (ties by tile) that is not `Built` and lies outside every landmark's radius; its water side is the first of n, e, s, w that is sea. Modules: `clamp(population / 1,000, 2, 4)` `harbour_quay` along that edge, continuing onto the next shore tile with the same water side; a `warehouse_timber` 4 m inland of the quay's middle, its door to the quay. The quay front is stone at 300 pm, else timber (stream **0x860**, key settlement id); one in three of the modules is the crane plan, the middle one first.
- Stored as `Region.Harbours` (`PlacedHarbour(Index, Settlement, X, Y, Turns, Modules, Stone, WarehouseX, WarehouseY)`), not in `Region.ComputeHash`; its own `HarbourPlacer.ComputeHash` and a new golden line `harbours`. `ChunkGenerator` stamps each module and warehouse like a lot building (`Stamp`, `Lot = -1 - index`), and the sea cells beyond the edge stay water.
- **Old worlds (placer 0)** keep the Phase 7 route: `HarbourSeedStep` (registered in `Phase7Seeding.DefaultSteps`, name `harbours`) gives each coastal town a seeded harbour from `Beaches.Moorings(all, cx, cy, 4)` with an abstract warehouse store and no crane, owned by the lord's household; nothing in the chunks changes. In placer 1 worlds the same step makes the rows of the generated harbours (moorings from the stamped bollards). The step runs at `NewGameWorld.Create` and on load when naval key 6 is absent (the step overrides a new default interface method `bool Present(save)`, which otherwise means "its section is present").

### 3.8 The helm on a live ship
- When the hero drives (E at the helm cell) a vehicle whose template is a sea ship (`SeaShip.Of` gives sail or rowers, hull planks `hull_plank_sea` or `hull_plank_long`) and he is its owner or named master, `RideBook.PlanBoat` hands the step to `Naval/HeroHelm.cs`: `Sailing.Tick` each fixed tick (20 a second) with the live weather's wind and the sea state's waves; helm -1, 0, +1 from A and D (rudder 3 deg/s, steering oar 6); W and S set or strike 10 m2 of sail (one `sail_square_sea`) up to the ship's `SailM2`; R oars out or in (needs manned rowers). The inputs are recorded with the other driving inputs, so replays agree.
- The game calls `SetHelm(HelmView.Of(...))` every frame while he holds the helm and `SetHelm(null)` when he leaves it, so the dev command is no longer the only setter. The key map gets a `Helm` context (A, D, W, S, R, E), conflict-checked in CI.

### 3.9 Fire arrows in the fire world
`BattleBubble.Open` sets `FightWorld.FireArrowLanded` when the world has a `FireWorld` (`Fire/FireArrowWiring.cs`): the landing point in bubble mm plus `OriginMm` gives the world cell; the topmost cell of that column whose fuel burns (roof first, then upper floors, then the ground's fixtures) is struck with `FireArrows.Strike` (stream 0x851, key the shot's serial; wet when it rained in the last hour). Generated buildings are adopted first (`FireWorld.EnsureAdopted`). The fire then runs as Phase 6 fire, with the arrow's `War` cause.

### 3.10 Barge cargo
- Each barge of `SeaTradeDay` takes a crew of two from the crew pool of the settlement nearest its upstream end; the freight purse is the first man's household.
- `RegionalTrade.Daily`: when the buying and selling books each lie within 2 km of an end of the same barge's waterway, the lot does not move at once. It goes into `barge.Cargo` (`Store.Move`) when the barge is at the seller's end, or waits in the seller's store as a reserved lot until it is, and it is listed at the buyer's book when the voyage reaches that end. Coin is paid at purchase as now. The freight is the crew's wages for the leg's hours (150 mites a man-day), shared by the lots' mass and paid by the buyer to the freight purse (`TransferReason.Fee`). Lots ride in book order, then good id, up to the hold's capacity.

### 3.11 Prisoners from abstract storms
In `SiegeWorld.Resolve`, a won abstract storm takes the yielded: `(garrison - lostD) x StormYieldPm / 1000` (`SiegeConfig`: 600 through a breach, 400 over the walls), chosen by stream **0x862** keyed on the siege id and day (hero excluded), handed to `TakePrisoners` (captor the host's commander, pen the place, guards the host's men) as the live storm does. The two numbers are fitted to the live storm's mean yield over LW-1's 50 seeds and held to it within ±25% (a D11 named ratchet, as LW-1's loss divisors were). A repulsed storm takes nobody.

### 3.12 Integer notes
Metres and cells for building; mm and mm/s at sea; mites for coin; permille for chances; the moorings by integer division. No floating point anywhere new.

## 4. Historical numbers (brief sources)
| Number | Value used | Source |
|---|---|---|
| Berth length | 24 m a mooring | a Bremen-type cog is 23.3 m long and 7.6 m in the beam (the Bremen cog, 1380); Phase 7's mooring spacing |
| Quay build | timber front on piles, rammed fill behind | London's waterfront advanced by timber revetments backfilled with earth and rubbish, 12th to 14th c. (Milne, *The Port of Medieval London*, 2003); stone fronts later and rarer, hence 300 pm |
| Moorings | 2 to 4 at a generated town quay | small ports worked a few ships at once; a cog's lading took days, so berths, not ships, limit trade |
| Crane | treadwheel, 500 kg a 120 s lift (kept) | harbour cranes stood at Bruges, Utrecht and Lüneburg by the 14th c. (Matheus, "Mittelalterliche Hafenkräne", 1996); the great double-wheel Gdańsk crane (1440s) lifted a few tonnes; 500 kg is a pipe of wine and keeps SH-7 |
| Porters | 30 kg a 90 s trip (kept) | a man's load at a walk, Phase 7 |
| Customs | the realm's rate, 3% default; wool out 10% | English poundage 12d in the pound (5%) from 1347; the "Great Custom" of 1275 on wool, 6s 8d a sack, several percent of its value (Phase 6 RI-9) |
| Dues to a haven's lord | quayage a flat due a ship-day | keelage and anchorage, taken by lords of havens on each ship |
| Sea freight | crew wages for the days at sea | water carriage cost a fraction of road carriage a tonne-mile (Masschaele, *Econ. Hist. Rev.* 1993) |
| A town taken by storm | a large share of the garrison yields | a place taken by assault had no right to terms; its defenders were killed or held for ransom (Keen, *The Laws of War in the Late Middle Ages*, 1965) |

## 5. Determinism
- **Streams** (Phase 8 live-war block 0x850..0x86F; 0x800, 0x810, 0x820, 0x828, 0x830 to 0x832, 0x850, 0x851, 0x858, 0x859, 0x880, 0x881, 0x8A0 and 0x8A1 are taken): **0x860** `HarbourPlacement` (stone or timber front; key settlement id), **0x861** `ShipCallCrew` (a calling ship's crew as persons; key call id, crew index), **0x862** `StormYield` (who yields; key siege id, day). 0x863 to 0x86F stay free. Register them in `Core/RngStreams.cs`, the `Phase8PlumbingTests` table and the overview §6 row. Fire arrows reuse 0x851. The AI planner, the sea gates and barge lots draw nothing.
- **Order:** harbours by id; moorings in edge order; calls by (arrival second, call id); goods by good id; settlements and fiefs by index; lots by (buyer book, seller book, good); the seeding step by settlement index. Ids are stable: generated and seeded harbours from the settlement index, built ones and calls from counters in the book.
- Same numbers live and abstract: a call's hauling uses `Harbour.Unload` seconds in both tiers; the barge's leg is `Voyages.LegSeconds`.

## 6. Saves
v8, no new section, optional keys only, null while empty:
- `naval` (`NavalSave`): key 6 `Harbours` (`HarbourSave[]`: id, settlement, source, structure, mooring points and ships, warehouse store id, crane, business, carry grams, duty and calls this season), key 7 `Calls` (`ShipCallSave[]`: id, ship, harbour, mooring, edge, state, arrive and leave seconds, goods in and out, duty paid), key 8 `NextHarbour`, key 9 `Helm` (the hero's ship: sail set, oars, helm, slip carry, stamina), key 10 `BargeLots` (barge, seller and buyer books, good, units, freight).
- `RegionSettings.HarbourPlacer` (absent = 0). The build sites, the business, the contracts, the pens and the fire are in their existing sections.
- `WorldRuntimeResumeTests` holds every new key in its Phase 8 case.

## 7. Golden hashes
- **May be re-pinned:** `phase5_trail` and `phase6_trail` only (the content grows: pieces, plans, the business kind, the room type; harbour businesses hire in the trail worlds), with the reason in the comment.
- **Must not move:** continent, history, embark, region, landmarks, landmarks_v1, weather, codex, people, gates, fight, captive, vehicles, the four chunk lines and the block lines. The harbour layer is outside `Region.ComputeHash`; harbour businesses hire through the labour market's first day, not at generation. **Added (not re-pinned):** `seed N harbours <count> <hash>`. If a pinned chunk holds a generated harbour, its line is printed with harbour placer 0 and a new `chunk_h` line is added for it.

## 8. Build steps
1. **Plumbing.** The three streams; `RoomTypeDef.WaterEdge`, `BlueprintDef.WaterSide`, `BusinessDef.Room`, `RegionSettings(Def).HarbourPlacer`, the `sea_rules` fees (`quayage_mites_per_ship_day` 150, `porterage_mites_per_t` 30, `cranage_mites_per_t` 20, `call_crew` 12, `unpaid_leave_days` 2); naval keys 6 to 10 with `NavalHash` lines while set. `BlueprintWriter` keeps `water_side`.
2. **Pieces, room type, plans** (3.1). `RoomShape` gains the water-edge rule for both detectors; the game sets `RoomWorld.WaterAt` from the chunk ground (`Water`, `DeepWater`). The plan validator accepts open-air plans with a water side.
3. **Fitter and build mode** (3.2): the water-side check, the land check, the plan library group, refusal strings.
4. **`HarbourRow`, `HarbourBook` and the room's harbour** (3.3): rows made when the business is founded; `Harbour` objects rebuilt on load; moorings in edge order; `Harbour.Load`.
5. **The harbour business** (3.3): `Room` kinds in `Found`, the fees, porters from the staff, the daily step (H5 hour 0, after `sea_trade_day`).
6. **Sea gates and calls** (3.4) in `ContinentTrade` and `Naval/HarbourCalls.cs` (an hourly system at `SystemOrder.Ships + 2`): arrival, mooring, hauling, leaving; crew persons on 0x861.
7. **Customs to the holder** (3.5) and the Ledger lines.
8. **`HarbourPlanner`** (3.6).
9. **`HarbourPlacer` v1, the chunk stamp and `HarbourSeedStep`** (3.7), the `harbours` golden line.
10. **The helm** (3.8): `HeroHelm`, `PlanBoat` branch, key 9, the game's `SetHelm` and key context.
11. **Fire arrows** (3.9).
12. **Barge cargo** (3.10), key 10.
13. **Storm prisoners** (3.11) and the fit.
14. **Godot:** the plan group and the water-side preview in red, moored ships drawn from their parts (`RideSprites`), the Businesses and Fief lines, a `--ng-shot harbour` screenshot.
15. **Close:** goldens (section 7), the overview's LW-5 "Built" note, the strings (`hb_*`, `cn_needs_water_side`, `ch_hb_*`).

## 9. Tests (`tests/.../LiveWar/HarbourTests.cs`, `LeftoverTests.cs`)
| Id | Name | Gate (fails when) |
|---|---|---|
| LH1 | Room | `harbour_quay` is not read as `harbour` by both detectors with water beyond its south side, or is read as one without the water; two modules side by side are not one room with 2 moorings |
| LH2 | Build by hand | the plan is accepted on land the hero does not hold or with land beyond its water side; on his shore parcel it is refused, or built hands-on it leaves any material gram unaccounted |
| LH3 | Business | the `harbour` kind founds without a warehouse within 60 m, or fails to found with one; capital not moved by `Gift`; no porters hired in 7 days |
| LH4 | A call | in a region with a sea edge, a profitable export does not go by the sea gate; the cog does not moor, haul 40 t in `Harbour.Unload`'s seconds (±1 s), take its back-haul and leave with the mooring free; mass or people not exact (imports, exports, crew in and out) |
| LH5 | Customs | the duty is not value x the realm's rate (wool 10%), goes to the realm's treasury rather than the quay's holder, or the ledger total moves |
| LH6 | Full | with every mooring taken a trip is planned for that day, or a ship moors twice; an unpaid duty unloads or stays past 2 days |
| LH7 | Crane | a crane in the room does not shorten the haul by exactly its rate; a quay without one uses it |
| LH8 | AI lord | a lord with the coin and coastal land posts no Civic contract in a year, posts two, or posts where a harbour stands; the business is not his on handover; escrow not conserved |
| LH9 | Generated | with placer 1 a coastal town has no harbour, or one stands on a lot, a street, a landmark or off the shore; two runs differ; a stamped quay has land beyond its water side in its chunk |
| LH10 | Old worlds | a placer 0 world stamps anything; a save without key 6 does not get its seeded harbours on load, or then fails save, load, save byte-identical |
| LH11 | Helm | the HUD's figures differ from `HelmView.Of` of the ship; the rudder turns at other than 3 deg/s; a save at sea resumes to a different position after 600 s |
| LH12 | Fire arrows | over 1,000 arrows on dry thatch roofs in a bubble the fire world lights outside 200 ± 20 pm (wet 20 ± 10), or a replay lights different cells |
| LH13 | Barge cargo | a lot between two barge-served books moves before the barge reaches the buyer's end; freight differs from the crew's wages for the leg; mass or coin not exact |
| LH14 | Storm prisoners | a won abstract storm pens other than `(garrison - lostD) x pm / 1000` men, or with rank classes missing; the abstract mean yield leaves ±25% of the live storm's over 50 seeds |
| LH15 | Save | with a harbour, a ship moored mid-haul, a call at sea, the hero at the helm and a barge lot, save, load, save is not byte-identical, or `NavalHash` differs between Linux and Windows |
| LH16 | Goldens | any pinned line but `phase5_trail` and `phase6_trail` moves; the `harbours` lines are missing |

LH1 to LH16 gate P8-LW-5. The overview's OV runs need no change.

## 10. Cut order (if it runs long)
1. The stone quay front and the stone draw (timber only; 0x860 then unused, still reserved).
2. The crew of a calling ship as persons: a count on the call row instead (0x861 unused).
3. `HarbourPlanner` (AI lords): to after 1.0; generated and seeded harbours still serve the AI's towns.
4. Barge cargo (3.10): barges keep shuttling empty, as LW-4 left them.
5. The Godot harbour screenshot and moored-ship drawing (the sim and HUD stay).
Never cut: the player-built harbour, the room type, customs to the holder, the versioned placer, the helm, fire arrows, storm prisoners.

## 11. Questions for the user
None. Every point above is settled by the plan, the 28 Nov decision or the Phase 6 and 7 specs; the readings that a helper may want to check are that the realm takes no share of a harbour's customs (the decision gives the holder the customs) and that generated harbours are placed in towns only (plan §3h).
