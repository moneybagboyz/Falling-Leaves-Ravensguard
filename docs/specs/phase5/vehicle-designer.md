# Vehicles, Phase 5 part: the designer, part rows, building and repairing part by part, driving, river boats

Plan refs: §3h (a vehicle is a grid of 1 m cells of parts stacked on a frame; parts are data: material, volume, mass, hit points, what they need underneath and what they provide; designs are vehicle blueprints in the building text format; the designer is a top-down part grid with a deck switcher, opened from a cartwright's or shipwright's workshop or anywhere for small designs, showing mass, cargo, crew, pull needed, speed per surface, turning circle, draft and stability, and warning about unsupported parts, no steering, a draft too deep for local rivers, a boat that will capsize; building and upkeep through jobs and crafting: recipes per part, wheelwright, cartwright and shipwright proficiencies, unfinished vehicles anyone can resume, repair and disassembly per part; a wagon is built in a yard, a ship on a slipway and launched; power: draught animals, people, wind, river current; speed from pull against mass and rolling resistance; turning circles from the axles; getting stuck; fords; boats: displacement and draft from hull volume against river depth; overloading and beam seas capsize; the hero drives with WASD, NPCs with their own skill, both tiers use the same numbers; things on board move with the vehicle; damage per part), §5 Phase 5 ("the vehicle designer, vehicle blueprints, building and repairing vehicles part by part, driving, getting stuck, river boats (barges, ferries, rowing and fishing boats); wheelwright, cartwright and shipwright proficiencies"; about 40 parts and 12 templates; done-when: design a wagon, have it built, drive it loaded to market and get it stuck in mud), §4A.1 (fish: boat crew 12 kg a day). Phase 7 owns war wagons, chariots in combat, siege engines, sea ships and naval combat; this spec keeps their parts out but the formats ready.

## 1. Purpose
Phase 3 shipped seven fixed templates (`Sim/Carts/`, namespace `Ravensguard.Sim.Carts`: `VehicleBlueprint`, `VehicleCatalog`, `VehicleStats`, `VehicleMath`, `VehicleTable` with one `WheelPm` per vehicle, `Surface`, `Stuck`, `Fords`), and the formula every caravan already uses. Phase 5 opens the format to the player: design any vehicle from parts, have it built part by part by real people, keep each part's condition, repair what breaks, and take to the rivers.

## 2. Dependencies
| Needs | From |
|---|---|
| `VehicleBlueprint` parser and validator (parts need what they stand on; hitch exactly when animals pull; a seat for a driven vehicle; wheels in pairs on axles), `VehicleStats` (mass, cargo kg and L, hitch slots and `MaxAnimals`, axle limit, base speed, seat row), `VehicleMath` (`SpeedMps`, `TeamEfficiencyPm`, `AnimalsNeeded`, `CaravanMmPerGs`), `Stuck` (rolls 0x351, push, unload, dig, add a team), `Fords`, `Surface` | exists (Phase 3) |
| `VehicleRow` (`Template`, `Cargo` store, `Team`, `OdometerMm`, `WheelPm`, `Stuck`), `RideBook`, the cart HUD, the driving keys (WASD, Shift, O) | exists |
| Construction sites, ghosts and crews (the same machinery builds vehicles) | build spec 3.2, 4.4 |
| Recipes with `vehicle_part` results, proficiencies `joinery`, `wheelwright`, `cartwright`, `shipwright`, `tyre_shrinking` | `crafting-full.md` |
| River polylines (`WaterwayId`, width cm), `RoadCrossing` (fords, bridges), weather (wind, river-high days) | exists (Phase 1-3) |
| Fishery business and extraction `fishing_shore` | exists (Phase 2) |
New: `Carts/PartGrid` (per-cell part rows), `Carts/DesignBook` (content and player designs), `Carts/BoatMath`, `Carts/VehicleSite` (a construction site whose grid is a vehicle), Godot `UI/VehicleDesigner`. Content: `vehicles/parts.jsonc` (19 -> about 40), `vehicles/templates.jsonc` (7 -> 12), `vehicles/water.jsonc`.

## 3. Data

### 3.1 New part fields and parts (about 21 new)
New optional fields: `recipe` (the craft recipe that makes it), `install_h` (worker-hours to fit it on the frame), `steer` (`pivot_axle`, `rudder`, `steering_oar`, `handles`), `waterplane_pm`, `hull_depth_mm`, `block_pm`, `buoyant` (bool), `rowers` (seats that row), `sail_m2`, `height_mm` (centre of mass above the keel or ground), `rough_wear_pm` (hp loss scale on bad ground). New parts: `axle_pivot` (pivoting front axle: turning radius x0.6), `axle_iron_bound`, `wheel_dished`, `brake_shoe`, `drag_shoe`, `hull_plank`, `keel`, `gunwale`, `thwart` (rowing bench, `rowers: 1`), `rowlock`, `steering_oar`, `rudder`, `mast`, `sail_square`, `quant_pole_rack`, `tow_post`, `deck_plank`, `ferry_ramp`, `fish_well` (keeps fish fresh x2 longer), `cabin_box`, `stall_counter` (a peddler's workstation: a `shop` tag), `mobile_hearth` (cook wagon: station `hearth`). Military parts (armour boards, mounted weapons, ram head) stay Phase 7 content.

### 3.2 Templates (7 -> 12)
Added: `barge` (3 x 10 hull, poled or towed), `ferry` (3 x 6 hull with ramps, a tow-rope crossing), `rowing_boat` (2 x 5 hull, 2 thwarts), `fishing_boat` (2 x 6, a fish well, 2 thwarts, a steering oar), `peddler_wagon` (covered wagon with a stall counter).

### 3.3 Part rows on every vehicle (replaces `VehicleRow.WheelPm`)
`VehicleRow.Design` (a design id: content template or `player.*`), `Parts[]` = `{ Cell (x, y, deck), PartIndex, Hp (int), ConditionPm, State (Ghost, InProgress, Built, Broken), WorkMilli, Store }`, ordered by `(deck, y, x, stack order)`. Mass, cargo and stats are derived from the **built** parts (a wagon without its tilt is lighter and rain gets in). Migration v7 -> v8 (overview 8): each existing row expands from its template, all parts `Built`, every wheel's `Hp = template hp x WheelPm / 1000`.

### 3.4 Water (`vehicles/water.jsonc`)
River depth and current by the polyline's width class: brook (< 3 m wide) depth 400 mm, current 0.4 m/s; stream (3-10 m) 900 mm, 0.6 m/s; river (10-40 m) 1,800 mm, 0.8 m/s; great river (> 40 m) 3,000 mm, 1.0 m/s; a ford's depth is 500 mm; river-high days add `+300 mm and +0.3 m/s` per day (cap 3); `drag_k = 8` (N s2 per m4), `rower_n = 70` (sustained N per rower), `poler_n = 120`, `keel_clearance_mm = 150`, `tender_gm_mm = 150`.

## 4. Rules

### 4.1 The designer
Opened from a recognised `wheelwright` or `cartwright` workshop room, a `slipway` (boats), or anywhere for designs of up to 2 x 3 cells (handcarts, barrows, small sledges). A top-down grid at 32 px a cell with a deck switcher (PgUp / PgDn); parts in categories (Frame, Wheels and axles, Hitch, Cargo, Seats, Hull, Steering, Sail and oars, Cover, Workstations); LMB places the part on the cell's stack (only where `needs_below` is met), RMB removes the top part, T rotates the design's facing, M mirrors, Ctrl+Z / Ctrl+Y undo and redo. A **stats pane** updates on every change:
- mass (kg), cargo (kg and L), crew and seats, rowers;
- **pull needed** to move fully loaded at full speed on dry road and on mud (`VehicleMath` with `ratio <= 600`), and the team that gives it ("needs 2 oxen on dry road, 6 in mud");
- speed per surface for a chosen team (the Phase 3 formula, table of 6 surfaces);
- turning radius: the template rule (pivot axle 4 m for a cart; fixed axles 8 m for a wagon), x0.6 with `axle_pivot`;
- axle load and limit;
- boats: draft at empty and full load, freeboard, metacentric height GM, the deepest local river it fits, speed rowing / poled / towed / downstream.
**Warnings** (the design can be saved, not built, while any error stands): unsupported part; no frame under a part; no steering (`steer` part or a pivot axle on a 4-wheeled vehicle); wheels not in pairs on an axle; a driven vehicle without a seat; hitch without a pull point; boat GM <= 0 (capsizes); boat full-load draft + clearance over the local river's depth (a warning only: it may be for another river); a hull with a gap in its planking (a cell of the hull outline with no `hull_plank` leaks).
Designs save as `vehicle_blueprint` JSONC (the Phase 3 format: `rows`, `legend` of part stacks, `levels` for decks, `hitch`, `crew`) to `user://blueprints/player/` beside building plans (build spec 4.8), loaded through the `player` mod.

### 4.2 Building a vehicle part by part
Ordering a build (Enter in the designer, at a yard or slipway the hero may use) makes a `VehicleSite`: a construction site whose grid is the design and whose ghosts are parts. Each part needs its **recipe** made first (a wheel is crafted at the wheelwright's station; a hull plank at the sawpit) and then **installed** (`install_h` worker-hours at the site); recipes and installs follow the stack order (frame, then axles, then wheels, then the bed). Proficiencies: `wheelwright` for wheels and tyres (a spoked wheel: Carpentry 40, 24 h, `wheelwright` 1,600 time and 15 skill), `tyre_shrinking` for iron tyres (a smith and a wheelwright together at the `tyre_ring`), `cartwright` for frames, beds, shafts and poles, `shipwright` for hull planks, keels, masts and rudders. The hero builds it himself, orders his people, posts `Construct` jobs, or **commissions** a cartwright or shipwright business (`crafting-full.md` 4.7 at the vehicle scale: the price is the parts' recipes plus install hours). An unfinished vehicle is a world object (a frame on trestles, a hull on the slipway) and anyone allowed can resume it; it moves only when every `move` part is built. A boat is **launched** from the slipway when its hull is complete (an action: 4 people for 10 minutes, or a horse team).
**Golden ox wagon (`ox_wagon` design rebuilt in the designer):** stats identical to the Phase 3 template (300 kg, 1,000 kg / 2,000 L, axle limit 1,500, radius 8 m); parts to make: 4 spoked wheels (24 h each), 4 iron tyres (6 h smith + 2 h shrinking each), 2 axles (8 h each), frame and bed (16 cells, 2 h each), pole and yoke (6 h); installs 22 h: **about 182 worker-hours** in all (the content sums; test VD-4 asserts the exact total from the content rows).

### 4.3 Damage, wear and repair per part
- **Wear on the move:** every 20 m each wheel loses `hp_milli = 1,000 x axle_load / axle_limit x rough_pm / 1000 x wear_pm[tier] / 1000` (road 1,000, field 2,000, rock and rubble 4,000; a Fine wheel x0.8). This replaces Phase 3's one `WheelPm` with the same numbers for an evenly loaded cart (test VD-6).
- **Breakage:** a part at 0 hp is `Broken`: a broken wheel or axle stops the vehicle (speed 0, `Stuck` cannot free it), a broken shaft unhitches the team, a broken hull plank lets in water (4.5), a broken tilt lets in rain. Overload above 130% (Phase 3 refuses the hitch) and a drop of more than 1 m (a bank, a collapsed bridge) damage the axles at once (`hp -50%`).
- **Repair:** Q on the vehicle > Repair lists damaged parts; each uses its part recipe's repair line (Phase 3's `repair_cart_wheel`, now real: +hp to that part), or replacement (remove the broken part, install a new one). Field repair needs the tools and materials at hand; a wheelwright's yard repairs any.
- **Combat damage** (Phase 4 resolver) applies to the part hit, as for building pieces; Phase 7 makes vehicles fighting platforms.

### 4.4 Driving and getting stuck (Phase 3, extended)
Phase 3's driving (E on the bench > Drive, WASD heading and speed, the gait cap of the slowest animal, reverse x0.3), speed formula, stuck rolls (0x351 per 20 m on `crr >= 170`), push, unload, dig and add a team are unchanged and read the design's derived numbers. New: turning radius from the design (4.1); a vehicle with a broken part stops; NPC drivers (`Riding` or `Husbandry`) keep 5% further below the stuck ratio than the hero (they avoid deep mud when they can: route cost x3 on `crr >= 170` cells). The done-when chain: design a wagon, have it built, load it, drive it to market, get it stuck in mud, free it (test OV-1).

### 4.5 River boats (`BoatMath`, fixed-point mm, N and mm/s)
- **Displacement and draft:** `A_wp = sum over hull cells of 1 m2 x waterplane_pm`; `draft (m) = mass_kg / (1,000 kg/m3 x A_wp x block_pm/1000)`, in integers `draft_mm = mass_kg x 10^9 / (A_wp_mm2 x block_pm)` (64-bit); `freeboard = hull_depth - draft`. **Swamped** when `freeboard < 0` (sinks: cargo and people in the water, Phase 4 drowning later) and in waves (wind >= 10 m/s on a great river) when `freeboard < 100 mm` (takes water: 20 L a second per broken or low cell, bailing removes 5 L/s per person).
- **Stability:** `GM = KB + BM - KG` with `KB = draft / 2`, `BM = B_eff2 / (12 x draft x block_pm / 1000)`, `B_eff = beam cells x 1,000 mm x 700 pm`, `KG` = the mass-weighted `height_mm` of parts, cargo (stacked: 200 mm plus 100 mm per 250 kg a cell) and people (seated 600 mm, standing 1,000 mm). `GM <= 0`: capsizes at once; `GM < 150` (tender): capsizes in beam seas (wind >= 10 m/s across the river) with 20 pm per 20 game seconds (stream **0x550**, key `GameSec/20`, boat).
- **Power and speed:** water drag `F = drag_k x A_wet x v2` with `A_wet = A_wp + perimeter_m x draft_m`; `v = isqrt(F_power / (drag_k x A_wet))`. Power: rowers `70 N` each (stamina as walking), polers `120 N` each in water up to 3 m deep, a towing team on the bank its `pull_n` (Phase 3 team rules) on a towpath, a square sail `0.6 x sail_m2 x wind2 x cos(angle)` N for winds within 90 degrees of astern. **Ground speed** = water speed +- current along the river polyline (downstream +, upstream -); with no power a boat drifts at the current.
- **Depth:** a boat cannot enter water shallower than `draft + 150 mm` (aground: like stuck, freed by unloading, poling off with 2 people, or waiting for river-high days). Fords and brooks are closed to loaded barges.
- **Ferries:** a ferry with ramps at a `RoadCrossing` of kind Ford or none: a ferryman (a ferry business; Phase 3's fee 15 mites a person or animal, 40 a cart) hauls it across on a rope (`tow_post` both banks) at 0.5 m/s; carts drive on and off over the ramps (portals).
- **Fishing boats:** a fishery business with a `fishing_boat` and a crew of 2 extracts at the plan's boat rate, **12 kg a day per crew member** (winter x0.4), from the lake or river stock (Phase 2 resource rows), instead of the shore rate of 4.
- **On board:** deck cells are walkable floor of the vehicle's sub-grid (Phase 3's cart-local frame); a gangplank or ramp is a portal to the bank, removed when the boat moves.

### 4.6 Tick placement
| Where | What |
|---|---|
| Live 20 Hz | movement (Phase 3), boat speed at 2 Hz per boat (cached by water cell), wear per 20 m, capsize and swamp checks per 20 game s |
| Barrier | design save, build order, part craft and install, launch, repair, part break |
| H1 | abstract caravans and barges: `VehicleMath` / `BoatMath` per leg from the design's stats (abstract barge legs follow river polylines, current included) |
| H2 | abstract vehicle sites credit hours (as construction) |

## 5. Determinism
Integer mm, N, mm/s; `isqrt` from `IntMath`; derived stats recomputed only when a part changes. Streams: **0x550** capsize in beam seas, **0x551** swamping water entry jitter (key `GameSec/20`, boat), **0x552** abstract barge leg delay (key barge id, leg), 0x351 (stuck) unchanged. Wear is deterministic (no roll).

## 6. UI and controls
- **Designer** (context `VehicleDesigner`, overview 6A): standalone like the blueprint editor; 960 x 540 layout: grid left (scroll, zoom = / - and wheel), parts list right top, stats pane right bottom with red warnings; Enter "Build this", Ctrl+S save, Ctrl+O open, Ctrl+N new, Esc close.
- **Vehicle panel** (Q on a vehicle > Inspect): the part grid with each part's hp colour, cargo, team, "needs repair".
- **Boat HUD** (the cart bar re-used): water speed, current, ground speed, draft and freeboard, GM ("steady" / "tender" / "will capsize"), depth under the keel. Driving a boat uses the Mounted layer as carts do: W/S row harder or back water, A/D steer, Shift hard stroke (rower stamina x3), O drift (oars shipped); E at the bank boards and lands.

## 7. Tests (golden)
| id | Case | Expected |
|---|---|---|
| VD-1 | Content: 40 parts, 12 templates validate; each part with a `recipe` resolves; Phase 3 templates' stats unchanged | pass; H-13 still exact |
| VD-2 | Designer stats = template: rebuild `cart` and `ox_wagon` cell by cell | 165 kg, 500 kg / 1,000 L, radius 4 m; 300 kg, 1,000 / 2,000, axle 1,500, radius 8 m |
| VD-3 | Pivot axle: ox wagon with `axle_pivot` | radius 4.8 m |
| VD-4 | Build hours: the ox wagon site | the exact sum of its part recipes and installs from content (about 182 h); a site with 2 wheels missing does not move |
| VD-5 | Warnings: no steering on a 4-wheeler, an unpaired wheel, a hull gap, GM <= 0 | each named; build refused while an error stands |
| VD-6 | Wear parity: Phase 3 cart, 10 km of road at 95% axle load | per-wheel hp equals Phase 3 `WheelPm` x template hp within 1 |
| VD-7 | Breakage: a wheel reaches 0 on rock | vehicle stops; `Stuck` actions do not free it; replacement wheel installed -> moves |
| VD-8 | Rowing boat draft: 2 x 5 hull, `waterplane_pm` 500, `hull_depth` 450, `block_pm` 500, total 490 kg | `A_wp` 5 m2, draft 196 mm, freeboard 254 mm; max load to freeboard 100: 875 kg |
| VD-9 | Rowing boat stability, 140 kg hull at 225 mm, two seated rowers 150 kg at 600, cargo 200 kg at 200 | KB 98, BM 1,667, KG 330, GM 1,435 mm (steady); the same with 6 standing people: GM computed and "tender" when < 150 |
| VD-10 | Rowing speed: 2 rowers (140 N), `A_wet = 5 + 14 x 0.196 = 7.74 m2` | v = 1.50 m/s (150 cm/s, isqrt +-1); downstream on a river +0.8 -> 2.30; upstream 0.70 |
| VD-11 | Barge towed by one draught horse (780 N): empty 3,000 kg / loaded 15,000 kg, 3 x 10 hull, waterplane 900, block 850 | draft 131 / 654 mm; speed 1.79 / 1.49 m/s |
| VD-12 | Aground: loaded barge (draft 654) into a stream (900 mm) | 654 + 150 = 804 <= 900: enters the stream; at a ford (500 mm): aground; after 2 river-high days the ford is 1,100 mm and it passes |
| VD-13 | Capsize: a tender boat (GM 100) in an 11 m/s cross wind for 10 game minutes | capsize rolls 30 per 10 min at 20 pm; same seed same result; GM 200: none |
| VD-14 | Swamp: overload a rowing boat to freeboard -10 mm | sinks; cargo mass moves to the river bed store (mass exact) |
| VD-15 | Fishing boat: crew 2, summer | 24 kg a day from the lake stock; winter 9.6 |
| VD-16 | Ferry: a cart crosses a 30 m river | 60 s at 0.5 m/s; 40 mites to the ferryman; ramps as portals |
| VD-17 | Commission a cartwright to build a hay wain | the parts made in his shop and installed at his yard; collected in person; never instant |
| VD-18 | Save mid-build and mid-voyage | byte-identical round trip; a v7 save with a worn cart migrates to part rows (VD-6 numbers) |
| VD-19 | Abstract vs live barge leg: 8 km downstream | durations equal within 1 s |
| VD-20 | Determinism | the same `VehicleHash` on Linux and Windows for VD-11 to VD-16 |

## 8. Open questions (recommended defaults)
1. **Facings:** 8 directions (plan): designed vehicles draw from parts in 4 drawn views with the diagonals baked by the art tool; if late, diagonals fall back to the nearest cardinal (cut order 4).
2. **River depth from width class**, not measured per segment: default; Phase 7 sea depths are separate.
3. **Drowning** when a boat sinks: Phase 4's drowning item is open; Phase 5 puts people in the water at the bank side with a swim check (Athletics 20 or a cork float) and no deaths until drowning lands.

## 9. Budgets
Part rows 12 B each: 500 vehicles x 40 parts = 240 KB; stats recompute on a part change <= 0.05 ms; 30 vehicles and 10 boats in a bubble <= 0.5 ms a tick (Phase 3 had 0.4 for 30 carts); designer stats update <= 2 ms per edit; abstract barge legs <= 0.5 ms an hour per region.
