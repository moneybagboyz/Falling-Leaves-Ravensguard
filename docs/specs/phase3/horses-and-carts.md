# Horses, riding, template carts and wagons

Plan refs: §3e (horses are creatures with their own body plan; mount and dismount with E; trot at 5 m/s, gallop at 12 m/s which costs stamina; fodder in kg
per day, water, iron shoes; injured horses go lame; carts and wagons pulled by horses or oxen with the §4A.1 capacities; other animals use the same creature
system), §3h ("Phase 3: fixed template carts and wagons (no designer)"; vehicles from blueprints in the building text format: `rows`, `legend`, `levels`; speed from
pull force against total mass; rolling resistance by surface: road, field, mud, snow, ice; getting stuck in mud: push, unload, dig out, add a team; fords close to wagons
before riders; the live and abstract tiers use the same speed numbers), §3c (rain turns roads to mud, x0.7 speed; fields x0.5; snow x0.6 to x0.3; heavy rain closes fords),
§4A.1 (cart ~500 kg, ox wagon ~1 t, pack horse ~100 kg), §1 (speeds), §5 Phase 3 ("riding horses, and carts: fixed template carts and wagons built from vehicle blueprints,
no designer yet"; done-when: the first ride). Phase 2: `caravans-and-logistics.md` (`VehicleDef`, `PathPlan`), `resources-and-extraction.md` (`HerdTable` head counts, species
Ox, Cow, Sheep, Pig, Horse, Hen), `items-mass-value-spoilage.md` (capacity = min of mass and volume).

## 1. Purpose
Give the player and every NPC real animals to ride and haul with, and real vehicles whose capacity, speed and failure modes come from data. Riders move at trot
and gallop and tire their horses; horses must be fed, watered and shod; carts are slow in mud and can get stuck. The same numbers drive the live tier and the abstract
caravans, so a trip takes the same time whichever tier plays it.

## 2. Dependencies
| Needs | From |
|---|---|
| `HerdTable` (`Head`, species), `Business` carters, caravans `VehicleDef` (`cart`, `ox_wagon`, `pack_horse`, `handcart`) | exists (Phase 2) |
| `PathPlan` speeds, LODBridge promotion of animals and vehicles (caravan entities spaced 12 m) | Phase 2 spec; LODBridge milestone P3-A |
| Item rows, containers, `Store`, goods `saddle` (5 kg), `cart` (165 kg), `iron_bar`, `nails`, `hay`, `oats` | exists |
| Weather: rain, snow, river-high flags, fords; `Daylight` | exists |
| Body plans and tissue materials (horse plan arrives Phase 4; Phase 3 uses a stub), butchering | `clothing-warmth-exposure.md` 4.8 |
| Utility AI, jobs (Haul, Escort), schedules (stable at night) | `live-behaviour-and-utility-ai.md` |
| `Riding` skill (exists in `Skill`), skill gain | `trade-inventory-character.md` 4.5 |
| Crafting: horseshoe set, harness, repair cart | `basic-crafting.md` |
New code: `Ravensguard.Sim.Animals` (`AnimalTable`, `MountSystem`, `AnimalCare`), `Ravensguard.Sim.Vehicles` (`VehicleBlueprint`, `VehicleStats`, `VehicleTable`, `VehicleMath`, `Hitch`, `Stuck`). Content `animals/species.jsonc`, `vehicles/parts.jsonc`, `vehicles/templates.jsonc`.

## 3. Data

### 3.1 `AnimalTable` (individuals; herds stay head counts)
Individuals exist only where needed: **a row is created ("materialised") from a `HerdTable` row** when an animal is promoted, bought, tamed or assigned to a rider or
team (`Head -= 1`, id `IdKind.Item`-like `IdKind.Other` appended `Animal`), and **folded back** (`Head += 1`) on demotion if it is still a herd animal. Animals owned by the hero or
a squad stay as rows permanently.
| Field | Type | Unit | Meaning |
|---|---|---|---|
| `Id`, `Species`, `Breed` | id / enum / string | | species `Horse, Ox, Mule, Donkey, Dog` (Phase 3); breed: `palfrey, rouncey, packhorse, draught, courser(war, Phase 4)` |
| `Sex`, `BirthDay` | | game day | age in years `/60` |
| `Owner` | `EntityId` | person/household/business/faction | theft and sale use it |
| `MassKg` | short | kg | from breed (palfrey 420, draught 650, ox 600, mule 400, donkey 200) |
| `TrainedPm` | ushort | permille | 0 = wild, 400 = pack/draught broken, 700 = ridden, 900 = steady under stress |
| `Temper` | sbyte | -100 calm .. +100 skittish | affects bucking and panic |
| `StaminaSp` | int | stamina points, 0..10,000 | short-term (3.2) |
| `WorkFatiguePm` | ushort | permille | long-term, from distance and load |
| `FeedDays` | short | days x 10 | food reserve (fodder eaten / need) |
| `WaterPm` | ushort | permille | thirst |
| `Shoes` | 4 x ushort | permille each (hoof state), 0 = bare | wear, lost shoe = 0 |
| `Lameness` | byte | 0 none, 1 mild, 2 severe, 3 unusable | |
| `HealthPm` | ushort | permille | placeholder for the Phase 4 body plan |
| `State` | enum | `Free, Tied, Ridden, Hitched, Following, Stabled, Dead` | |
| `Stable` | lot id | | where it sleeps |

### 3.2 Species content (`animals/species.jsonc`)
```jsonc
{ "type": "species", "id": "horse_palfrey", "species": "Horse", "mass_kg": 420, "carry_kg": 130, "pull_n": 530, "gait_mps": { "walk": 2.2, "trot": 5.0, "gallop": 12.0 },
  "stamina_sp": 10000, "stamina_cost_sp_per_m": { "walk": 0, "trot": 2, "gallop": 21 }, "regen_sp_per_game_s": { "walk": 5, "rest": 15 },
  "fodder": { "idle_hay_kg_per_day": 7.0, "work_hay_kg_per_day": 8.0, "work_oats_kg_per_day": 2.5 }, "water_l_per_day": 35,
  "shoe_life_km": 600, "price_mites": 48000, "body_plan": "horse", "butcher": "ungulate_large" }
```
| Breed | Mass kg | Carry kg | Pull N (sustained) | Walk / trot / gallop m/s | Hay kg/day | Price (mites, base for `PriceSolver`) |
|---|---|---|---|---|---|---|
| Palfrey (riding) | 420 | 130 | 530 | 2.2 / 5.0 / 12.0 | 7 (+2.5 oats working) | 48,000 |
| Packhorse | 450 | 100 | 560 | 2.2 / 4.5 / 9.0 | 7 | 30,000 |
| Draught horse | 650 | 160 | 780 | 2.0 / 4.0 / 8.0 | 9 (+3 oats) | 40,000 |
| Ox | 600 | 120 | 650 | 1.8 / n.a. / n.a. | 10 (grazes 8 h in summer) | 13,000 |
| Mule | 400 | 110 | 480 | 2.2 / 4.5 / 8.0 | 5 | 25,000 |
| Donkey | 200 | 70 | 240 | 2.0 / 3.5 / 6.0 | 3 | 9,000 |
Prices: palfrey 480 crowns (about 137 craftsman days at 350 mites/day), ox 130 crowns; `price_mites` are seeds for the price solver (engineering decision E12). New goods `horse`, `ox`, `mule`, `donkey` join `cow` and `sheep` as live-animal piece goods.
(Courser or war horse stats arrive in Phase 4.) Sustained pull is about 12% of body weight; it is the force over a working day, not a peak.

### 3.3 Vehicle parts and blueprints (`vehicles/parts.jsonc`, `vehicles/templates.jsonc`)
Parts (the part vocabulary from §3h trimmed to Phase 3): `frame_plank, axle_wood, wheel_solid, wheel_spoked, iron_tyre, shaft_pair, pole_yoke, cargo_bed, cargo_rack, seat_bench,
tilt_hoops, tilt_cloth, runner, brake_shoe, handle_bars`.
```jsonc
{ "type": "vehicle_part", "id": "cargo_bed", "name": "Cargo bed", "mass_kg": 40, "volume_l": 1000, "cargo_kg": 500, "hp": 120, "needs_below": ["frame_plank"], "tags": ["bed"] }
{ "type": "vehicle_blueprint", "id": "cart", "name": "Cart", "culture": "any", "facing_cells": 3, "hitch": { "slots": 1, "kind": "shafts" }, "crew": { "driver": 1, "passenger": 1 },
  "legend": { "f": ["frame_plank"], "W": ["frame_plank", "wheel_spoked", "iron_tyre"], "a": ["axle_wood"], "B": ["frame_plank", "cargo_bed"], "S": ["frame_plank", "seat_bench"], "h": ["frame_plank", "shaft_pair"] },
  "rows": [ "hBBB", "aBBBa", "WfffW", "hSf" ], "levels": [] }
```
Same text format as building blueprints (`legend`, `rows`, `levels`), validated by the existing `BlueprintValidator` rules plus vehicle rules: every part has what it needs underneath, a hitch exists if hitching, a seat exists if a crew
requires it, wheels are paired on an axle. **No designer and no player edits in Phase 3**; templates are fixed content (mods may add them). Derived stats at load (`VehicleStats.Derive`):
mass, cargo kg and L, axle load limit, wheel count, turning radius, hitch slots, crew, width in cells.

| Template | Mass kg | Cargo kg / L | Hitch | Axle limit kg | Turning radius m | Notes |
|---|---|---|---|---|---|---|
| Wheelbarrow | 25 | 80 / 100 | pushed by 1 | n/a | 1.0 | hand-held, 1 wheel |
| Handcart | 40 | 150 / 300 | pushed by 1-2 | 300 | 3 | Phase 2 `handcart` |
| Cart | 165 | 500 / 1,000 | 1 animal (shafts) | 700 | 4 (pivot front axle) | Phase 2 `cart` |
| Hay wain | 220 | 600 / 3,000 (bulk) | 1-2 animals | 800 | 6 | bulky goods; hay, straw, wool bales |
| Ox wagon | 300 | 1,000 / 2,000 | 2 oxen (pole and yoke) | 1,500 | 8 (fixed axle) | Phase 2 `ox_wagon` |
| Covered wagon | 380 | 800 / 1,600 | 2 animals | 1,200 | 8 | tilt: rain proof; sleeps 2; peddlers |
| Sledge | 60 | 400 / 800 | 1 animal | n/a | 3 | winter, low resistance on snow and ice |
| Pack saddle (not a vehicle) | 6 | 100 / 250 | on the animal | n/a | n/a | Phase 2 `pack_horse` |
**Consistency rule (test):** the derived `cargo_kg`, `cargo_l`, hitch count and base speed of `cart`, `ox_wagon`, `handcart` equal the Phase 2 `VehicleDef` values. The abstract caravan tick calls `VehicleMath.SpeedMps` (3.5) so abstract and live travel agree (a Phase 2 spec change, see overview).

## 4. Rules

### 4.1 Mounting, gaits, stamina
- **Mount:** E on a horse within 2 m; takes 2 s (40 ticks), cancelable; requires a saddle on the animal (a `saddle` item, 5 kg, equipped to the animal slot; bareback is allowed for `Riding >= 20`: speed x0.8, no saddlebags) and `TrainedPm >= 700` unless the rider tames it (`Riding` and `Husbandry`: `tame_chance_pm = 200 + 4 x Riding + 2 x Husbandry - 3 x temper`, one try per 10 game minutes, a failed try throws the rider, stun 3 s).
  Unowned or other-owned horses: a prompt "This is Bram's mare" (theft is a Phase 6 crime). **Dismount:** E when mounted (or Q > Dismount) takes 1 s; the horse then follows if `TrainedPm >= 700` and the squad order is Follow, else stands tied or free.
- **Gaits:** `Walk 2.2 m/s`, `Trot 5.0`, `Gallop 12.0` (plan numbers; riding horse). Mounted movement uses the world keys: W/A/S/D move, **default Trot**, **Shift held = Gallop**, **O toggled = Walk** (`walk_toggle`, a key of the `Mounted` input layer). `Z` stays sneak and does nothing while mounted; Shift is the world `sprint` action, shown as gallop while mounted. Gait changes take 1 s to blend.
- **Stamina (short term):** pool 10,000 sp. Cost per metre: trot 2, gallop 21 (so trot costs 10 sp per real second at 1x and drains in 1,000 s = 16.7 real minutes; gallop 250 sp/s: 40 s of gallop = 480 m). Regen per game second: walking +5 (100 sp per real second at 1x), resting (standing, tied) +15 (300 per real second). Costs are per metre and regen per game second, so 8x does not change the result per distance.
  Below 1,500 sp the horse slows to trot; at 0 gallop is refused and trot drops to walk; **forced exertion at 0** (rider keeps spurring: Shift held) risks injury: `30 pm per 20-game-second window` (one real second at 1x) roll, stream 0x350 keyed `(GameSec / 20, animalId)`, with a mild lameness result.
- **Load:** rider plus carried gear counts against `carry_kg` (palfrey 130). `load_ratio = (rider + gear) / carry_kg`; speed factor `1 - 0.5 x max(0, load_ratio - 0.8)/0.4` and stamina cost x`(1 + max(0, load_ratio - 0.8))`; above 1.2 the horse refuses (walk only at 1.2, none above).
- **Long term:** `WorkFatiguePm` rises `1 permille per 40 m` at trot or gallop on roads (+50% off road, +100% in mud) and `+ 200 pm if no water for 8 game hours`; it falls 500 pm per game night of rest with feed. Above 800 the horse is "tired": speed x0.8 and gallop refused; at 1000 it will not carry a rider that day. Typical day: 40 km at trot and walk.
- **Riding skill** gains from distance (`d = 60`, one use per game minute at trot, 80 at gallop) and trick uses (jumping a hedge, Phase 4). Skill `Riding`: below 30 an untrained rider falls when a horse spooks (`fall_pm = 400 - 8 x Riding` per spook).
- **Spooks and panic:** a horse near fire, loud combat or a predator may panic (`Temper` and `TrainedPm`); a panicking horse ignores the rider for 5 s (Phase 4 combat hooks).

### 4.2 Fodder, water, shoes, lameness
- **Feed:** per game day a working horse needs `work_hay_kg + work_oats_kg` (palfrey 8 + 2.5); idle 7 hay. Grazing replaces hay at `1.0 kg per hour` up to 8 hours on a pasture, meadow or lush grass tile (taken from the tile pasture stock, Phase 2 grazing); oats 1 kg = 1.8 kg hay equivalent. `FeedDays` falls by `1/needed_fraction`; below 1.0 the horse is weak (speed x0.85), at 0 `HealthPm` -50 per day. Hay and oats are goods (`hay` exists, `oats` exists) in stable `Store`s or the saddlebags (feed bag).
- **Water:** 35 L a day; drinking at a river, trough or well: 10 L per 30 s; `WaterPm` falls to 0 over 24 game hours without drink; dry horses add the fatigue above.
- **Shoes:** 4 hoof states, 1000 new. Wear `1000 / shoe_life_km` permille per km on road (`600 km`), x2 on rough or mud, x0.2 on grass. A **shoe is lost** at `shoe_loss_ppm_per_100m = 5` on roads and `40` in deep mud or rock (stream 0x353 keyed `(animalId, draw = odometer_m / 100)`, so it depends on distance, not on time scale). Reshoeing needs a farrier (smith with `hammering 2`), `1.6 kg iron_bar + 0.1 kg nails`, 30 game minutes, price about 12 crowns at a smithy; the hero can do it with `Smithing >= 25` and tools (recipe in basic crafting).
- **Lameness:** causes: hoof state < 200 on hard ground for more than 3 km (+1 level), forced exertion (+1), a stone bruise from a lost shoe (+1 per 2 km on road), Phase 4 wounds. Level 1 mild: speed x0.7, no gallop; level 2 severe: walk only, x0.4; level 3: unusable, the horse is led. Recovery: rest in a stable with feed, -1 level per 3 game days (a good farrier or healer -1 level at once: `Husbandry >= 30`). Horses with level 3 for 15 days are put down by NPCs (they become `Dead`, butchery meat).
- **Stabling:** at an inn: `stable_fee_mites_per_night = 20` including hay (market-posted, per settlement); at home, a lot with a `stable`/`barn` stall (`Stable`); NPC horses sleep in stalls at night per schedule.

### 4.3 NPC and abstract use
- NPC riders (lords, merchants on route, guards on patrol, messengers) use the same gaits via `PathPlan` speeds (trot 5 m/s as `SpeedMmPerGs 250`); a rider with an animal in the live bubble moves at trot unless carrying a heavy load. An abstract caravan of carts uses 3.5.
- **Conservation:** `HerdTable.Head` counts plus `AnimalTable` rows of that species, region and owner always equal the herd's original count minus deaths and sales (test H-12). The abstract tick's herd growth, murrain and culls still apply to the head counts; a materialised animal is excluded from culls until folded back.

### 4.4 Hitching, driving and loading
- **Hitch:** E on the animal near a cart's hitch point > **Hitch** (20 game seconds each; needs a **harness** item per animal: a goods item `harness`, 3 kg, leather and iron, recipe in basic crafting; oxen use a yoke and wooden `pole_yoke`). Up to `hitch.slots` (cart 1, wagon 2). Teams add `pull_n` (two oxen 1,300 N); mixed teams allowed, each animal's `TrainedPm >= 400`.
- **Drive:** the driver sits on the bench (E on the cart > Drive); WASD controls heading and speed (target gait among walk, trot, gallop limited by the slowest animal and `VehicleMath`); turning limited by the turning radius (a cart pivots: radius 4 m; a wagon 8 m); reverse at x0.3. An NPC driver steers along road edges with its own skill (`Riding` or `Husbandry`).
- **Loading:** put items from your inventory into the cart bed container (E on the cart > Load), or drag stacks; cargo is a `Store` owned by the cart row; mass limit `cargo_kg` and volume limit `cargo_l` apply together (`min`). Goods on a cart move with it; people on seats ride along (sub-grid frame: the plan's moving platform; for Phase 3 people and items on a cart are positioned in cart-local coordinates and follow its transform).
- **Axle load:** `axle_load = (vehicle mass + cargo)/axles`; above `axle_limit` -> 100-130%: speed x0.8, wear x3; above 130% the hitch is refused ("The axle would break"). Wheels (`wheel_spoked` 120 HP) wear by `1 pm per 20 m x load/limit`; at 0 a wheel breaks and the cart stops (repair recipe, `carpentry 1` plus the part).

### 4.5 Cart speed (`VehicleMath.SpeedMps`), the shared formula
```
F_req (N)   = total_mass_kg x 9.81 x (crr_pm(surface) + slope_pm + wind_pm)/1000                 ( kg x 981 x pm / 100,000 )  total = vehicle + cargo + passengers
F_pull (N)  = sum of pull_n of hitched animals x (1000 - lame_pm)/1000 x (1000 - weak_pm)/1000
ratio_pm    = F_req x 1000 / F_pull
f(ratio)    = 1000                                   if ratio <= 600
            = 1000 - (ratio - 600) x 1                if 600 < ratio <= 1000           (down to 600)
            = 600  - (ratio - 1000) x 4/3             if 1000 < ratio <= 1300          (down to 200, stamina cost x2)
            = 0 (cannot move; "stuck" if on deep mud)  if ratio > 1300
speed_mps   = base_speed(vehicle template) x f(ratio)/1000 x gait_cap(slowest animal)         base: cart 2.5, ox wagon 1.8, hay wain 2.2, covered wagon 2.5, handcart 1.5
```
| Surface | `crr_pm` | Notes |
|---|---|---|
| Paved / cobbled | 25 | town streets, bridges |
| Dry dirt road | 45 | most roads |
| Wet road (rain 2-10 mm in last 24 h) | 70 | the plan's "x0.7 speed" for rain on roads |
| Track / path | 70 | |
| Grass, dry field | 110 | |
| Mud road (rain > 10 mm in 24 h, thaw) | 170 | carts struggle |
| Deep mud (field after heavy rain, > 30 mm in 24 h; ford approaches) | 260 | stuck risk |
| Packed snow | 70 (sledge 30) | sledge uses `runner` resistance |
| Deep snow | 200 (sledge 90) | |
| Ice | 15 (wheels slip: speed cap x0.7; animal slip risk) | |
| Sand / gravel | 150 | |
`slope_pm` = rise per 1,000 along travel (5% grade = 50; negative downhill; `F_req` is clamped at 0 so a downhill never pushes speed above the base speed); `wind_pm` for covered wagons 10 in strong wind. `surface(cell, time)` is a pure function of `RegionWeather` history (rain and snow of the last 24 game hours, temperature, river-high) and tile type; it feeds both tiers.
For people and riders (no force model) the plan's multipliers apply: rain on roads x0.7, fields x0.5, snow x0.6..0.3, mud x0.6, from the same `surface()` state.
- **Examples (golden C-1 to C-6):** a cart (165 kg) with 500 kg on dry road with one palfrey (not ideal: pull 530 N) `F_req = 665 x 9.81 x 0.045 = 293.6 N`, ratio 554 -> full 2.5 m/s; wet road (70): 456.7 N, ratio 862 -> `1000-262 = 738`: 1.85 m/s (about x0.74, the plan's x0.7); mud (170): 1,109 N, ratio 2,092 -> 0 (cannot move).

### 4.6 Getting stuck
- **When:** on a surface with `crr_pm >= 170` the vehicle rolls a stuck check every 20 m: `stuck_pm = clamp(ratio_pm - 700, 0, 600)/2` (0..300) x surface (170: 1.0, 260: 1.5), stream 0x351 keyed `(game hour, vehicleId, draw = odometer_m / 20)`, so the roll belongs to a place on the road and is the same at any speed. A ratio over 1,300 is stuck immediately. Stuck means speed 0 and `ratio` is evaluated with `crr_pm x 1.5` to move again.
- **Getting out (all can combine):** (1) **push:** each person adds `push_n = 6 x strength` N (strength 50: 300 N) within 3 m behind or sides; (2) **unload:** drop cargo (drag from the bed to the ground, 5 kg per game second per person) lowering mass; (3) **dig out:** a shovel (digging quality 1) removes the mud under a wheel: -20% `crr` for 10 game minutes of work; (4) **add a team:** hitch another animal; (5) **wait** for dry weather (the mud state decays 24 game hours after rain). Free when `F_pull + pushers >= F_req(stuck)`; whoever frees it gains `Husbandry` and `Strength` activity.
- **Abstract caravans** use the same ratio: a caravan whose planned leg has `ratio_pm > 1300` is delayed by a "stuck" duration rolled by stream 0x351 (3-18 game hours) and may be lost or turn back by Phase 2 rules; chronicle cause `Weather` (mud).

### 4.7 Fords, bridges, ferries
River-high flags from `HourEnv`: **wagons** cannot cross a ford from `river_high_days >= 1`, **riders** from `>= 2`, **walkers** from `>= 3` (or immediately in blizzard ice). A bridge is always passable. A ferryman (job `Escort` variant) crosses for `15 mites per person/animal, 40 per cart`. A mounted party at a closed ford gets the `BlockedPath` interrupt. Frozen rivers: ice thickness (winter, cold weeks) lets walkers and riders cross at `river_high_days` rules replaced by `ice_cm >= 12` (walk), `>= 20` (cart); cracking in thaw closes it.

### 4.8 Tick placement
| Where | What |
|---|---|
| Live 20 Hz | gait and movement, stamina per metre and regen per game second, hitched pull (`VehicleMath` at 2 Hz per vehicle, cached by surface cell), stuck rolls per 20 m, animal AI at 1 Hz |
| Barrier | `Hitch`, `Mount`, `Buy/Sell animal`, `Materialise/Fold` commands |
| H4 slice | feed and water consumption of stabled animals (daily), `FeedDays`, shoe wear of idle animals none |
| H1 | abstract caravans call `VehicleMath` for speed |
| H5 hour 0 | `surface()` cache refresh per region cell grid (about 4,000 cells at 500 m resolution) |
LODBridge: promotion spawns drivers, animals and vehicles together (`LiveBinding` for `Vehicle` and `Animal`), positions by `PathPlan`; demotion folds `StaminaSp`, `Shoes`, cargo `Store` ownership and `Hitched` state back into rows (nothing is a cache except pose).

## 5. Determinism
| Stream | Use | Key |
|---|---|---|
| 0x350 | forced-exertion injury, lameness onset | `GameSec / 20`, animal |
| 0x351 | stuck check (live) and abstract stuck duration | game hour, vehicle, `draw = odometer_m / 20` (live); vehicle, leg index (abstract) |
| 0x352 | temper and breed jitter at materialisation | herd id, serial |
| 0x353 | shoe loss | animal, `draw = odometer_m / 100` |
| 0x354 | taming success | `GameSec / 600` (one try per 10 game minutes), animal, `draw = rider person index` |

All keys are game time or distance, never the live tick counter, so 1x and 8x rolls match (overview section 5). The old 0x350 mixed four unrelated rolls; shoe loss and taming now have their own ids so a change to one roll order cannot shift another.
Integer force and speed maths; `VehicleMath` takes no float; the `surface()` function depends only on the deterministic weather and tile data; animals' ids come from the allocator of the region (`E9`).

## 6. UI and controls
- **E on a horse:** Mount (default action). **E when mounted:** Dismount. **Q on a horse:** Mount, Lead, Tie/Untie, Feed, Water, Inspect (health, shoes, fodder, stamina), Hitch to cart, Buy/Sell, Follow/Stay. **Q on a cart:** Drive, Load, Unload, Hitch, Inspect, Sit.
- **Mount bar** (mounted): stamina bar (green/amber/red), gait icon, fodder, shoes icons, `Lame` flag; speed shown in m/s and km/h. Shift gallop, O walk, default trot. **Cart bar:** load vs capacity, axle load %, pull ratio %, speed per current surface, stuck warning.
- **Auto-travel** while mounted/driving uses the same gaits (trot, or the cart's best) and the 8x interrupts for `Horse`, `CartStuck`.
- Hints: first mount ("E to dismount, Shift to gallop; horses tire"), first mud ("Carts struggle in mud: unload or push"), first horse tired.

## 7. Tests (golden)
| id | Case | Expected |
|---|---|---|
| H-1 | Gallop: palfrey, full stamina | 252 sp/s; slows to trot at 1,500 sp after 33.7 s (404 m); the pool would be empty after 39.7 s (476 m) |
| H-2 | Trot: 1,000 s at 5 m/s | 5 km on a full pool (2 sp/m x 5,000 m = 10,000); walking regen 5 sp per game s = 100 per real s |
| H-3 | 8x invariance: ride 8 km at trot at 1x and at 8x | the pool empties at 5.0 km (trot refused at 0 sp, resumes above 1,000 sp: hysteresis); the rest is walked at 2.2 m/s; positions at each game time identical at both multipliers, and the draws of streams 0x350 and 0x353 (keys on game seconds and distance) are identical draw for draw |
| H-4 | Load: rider + gear 125 kg on a palfrey (carry 130) | ratio 0.96 -> speed factor `1 - 0.5 x 0.16/0.4 = 0.8` (4.0 m/s trot), stamina x1.16 |
| H-5 | Cart on dry road, 665 kg total, 1 palfrey | `F_req` 293.6 N, ratio 554, 2.5 m/s (matches Phase 2 `speed_real_mps`) |
| H-6 | Same on wet road (70) | 456.7 N, ratio 862, `f = 738`, 1.85 m/s |
| H-7 | Same load in mud (170), one horse | 1,109 N, ratio 2,092: cannot move; on a hay wain with 2 horses (1,060 N): ratio 1,046, `f = 539`, 1.19 m/s (base 2.2) |
| H-8 | Ox wagon 1,300 kg total, 2 oxen (1,300 N), dry road | `F_req = 574 N`, ratio 442, 1.8 m/s; wet road 893 N, ratio 687, `f = 913`: 1.64 m/s; mud (170) 2,168 N, ratio 1,668: stuck |
| H-9 | Stuck recovery: wagon stuck (`F_req(stuck)` = 1.5 x 2,168 = 3,252 N), 2 oxen 1,300 N + 4 pushers (strength 50: 300 N each = 1,200) + unload 600 kg (mass 700 kg: `F_req(stuck)` 1,750 N) | 1,300 + 1,200 = 2,500 >= 1,750: freed after the unload |
| H-10 | Feed: a working palfrey needs 8 kg hay + 2.5 kg oats = 12.5 kg hay-equivalent (oats x1.8). Day A: 5 kg grazing, 5 kg hay, 2.5 kg oats = 14.5; day B: 5 kg grazing + 2.5 kg oats = 9.5 | day A `FeedDays` +0 (full ration, capped at 1.0 day reserve increase); day B 76% of need: `FeedDays -0.24`; weak flag below 1.0 days of reserve |
| H-11 | Shoes: 600 km road | hoof state 1000 -> 0; at 540 km (state 100) on hard ground, 3 km later lameness +1 |
| H-12 | Conservation: 1,000 materialise and fold cycles on a herd of 12 horses | `HerdTable.Head + rows` constant; 0 horses created or lost |
| H-13 | Derived stats of templates equal Phase 2 `VehicleDef` (cart 500 kg / 1,000 L, ox wagon 1,000 / 2,000, handcart 150 / 300) | exact |
| H-14 | Abstract vs live: 8 km cart trip on dry road with 665 kg | `PathPlan` duration equals the live traversal at 1x within 1 s |
| H-15 | Fords: river high 1 day | wagon refused, rider crosses; 2 days: rider refused too; 3: walkers |
| H-16 | Overload: cart (1 axle, limit 700 kg, mass 165 kg) | cargo 500: gross 665 (95%) speed x1.0; cargo 560: 725 (104%) speed x0.8, wear x3; cargo 760: 925 (132%) hitch refused |
| H-17 | Save: animal rows, hitched state, cargo stores | round-trip byte-identical; replay identical |
| H-18 | Slope: cart 665 kg total, one palfrey (530 N), dry road (45), `F_req = 665 x 9.81 x (45 + slope)/1000`; slopes +30, +50 (uphill) and -50 (downhill) | +30: 489.3 N, ratio 923, `f = 677`, 1.69 m/s; +50: 619.7 N, ratio 1,169, `f = 374`, 0.93 m/s; -50: `crr + slope < 0` so `F_req` is clamped to 0, ratio 0, `f = 1000`, 2.5 m/s (downhill never exceeds base speed); the abstract `PathPlan` using the same signed grade per segment agrees with the live traversal within 1 s |

## 8. Open questions for the user
1. **Horses in the economy:** palfrey price 480 crowns (137 craftsman days) makes a horse a major purchase, matching history. Default as written; the price solver may adjust.
2. **Breeding and foals:** deferred to Phase 5 (property); Phase 3 only materialises existing herd animals. Default deferred.
3. **Wild horses and taming:** allowed on steppe cultures only? Default: wild horses exist in the continent's grasslands as herd rows (`TrainedPm 0`), tameable as above.
4. **Mud severity:** carts cannot move in mud with one horse (by the numbers). Default accepted ("carts can get stuck"); if testers hate it, raise `pull_n` by 25% or lower `crr_pm(mud)` to 140.

## 9. Budgets
| Item | Budget |
|---|---|
| `VehicleMath.SpeedMps` per vehicle (2 Hz) | <= 2 microseconds; 0 allocations |
| 30 vehicles and 60 animals in a bubble | <= 0.4 ms per live tick |
| `surface()` cache refresh (hour 0) | <= 1 ms per region |
| Animal and vehicle rows | `AnimalTable` <= 2,000 rows x 80 bytes, `VehicleTable` <= 500 rows x 120 bytes; save <= 0.3 MB |
| Carts in bubble entity count | <= 12 entities each (vehicle + driver + animals + visible cargo) |

## 10. Notes (implementation, Phase 3 step)
What was built, where it differs from the text above, and why.

**Code and content.** `Ravensguard.Sim.Animals` (`Animal`, `AnimalTable`, `AnimalCatalog`/`Breed`, `RideMath`, `AnimalCare`, `RideBook`, `HerdLedger`, `LivestockHerds`) and `Ravensguard.Sim.Carts` (`Surface`, `SurfaceModel`, `Fords`, `VehicleMath`, `VehicleTuning`, `VehicleBlueprint`/`VehicleCatalog`/`VehicleStats`, `VehicleTable`, `Stuck`). The vehicle namespace is `Carts`, not `Vehicles`: the Phase 2 static class `Ravensguard.Sim.Caravans.Vehicles` would be shadowed by a `Ravensguard.Sim.Vehicles` namespace in every file under `Ravensguard.Sim.*` (and every test). Content types: `species`, `vehicle_part`, `vehicle_blueprint`, `vehicle_tuning` (`content/base/animals`, `content/base/vehicles`). Seven templates: wheelbarrow, handcart, cart, hay wain, ox wagon, covered wagon, sledge; validated at load (parts need what they stand on, a hitch part exactly when animals pull, a seat for a driven vehicle, wheels in pairs on axles); a bad mod template is dropped and listed in `VehicleCatalog.Errors`.
**Units.** Speeds are cm per real second in content and in `VehicleMath` (500 = 5 m/s); the live and abstract tiers use millimetres per game second (`MmPerGs`: 250 cm/s = 125 mm/gs). Force is millinewtons, the ratio rounds to nearest (so H-5 gives 554, H-6 862; H-7 2,093 and H-8 441 where the text rounds the other way; tests accept +-1), speeds truncate (H-6 1.84-1.85). Stamina is kept in thousandths of a point (`StaminaU`) and fatigue in millionths of a permille, so per-metre costs land exactly at every speed.
**Time.** The live hero is stepped one game second at a time inside each tick (`LiveWorld.MoveRidden`): at 8x a tick runs eight of them. Stamina, wear, shoe loss (0x353), stuck rolls (0x351, `odometer / 20 m`) and forced-exertion rolls (0x350, `game second / 20`) therefore never see the tick; a ride at 1x and at 8x saves to identical bytes (tests H-3 and the cart determinism test). Gait changes are instant (the 1 s blend is a view matter and not drawn).
**Deviations.**
- H-11: the text says a hoof below 200 laming after 3 km, and also that a horse at 540 km (state 100) has not yet been lamed; both cannot hold. The rule as written is implemented (3 km on hard ground with any shoe below 200, 2 km after a lost shoe, one level per stretch, cap 3); the test sets a worn shoe directly.
- H-16: cargo is capped by the bed (500 kg for a cart), so 560 and 760 kg never occur; the 104 and 132 percent cases are tested on `VehicleMath.Evaluate` (gross mass), the row path at 95 percent. The hay wain's axle limit is 900 (800 would put it over its limit when full: 220 + 600).
- Sledge mass is 78 kg, not 60 (a bed needs a runner or frame under it, which weighs). Handcart and cart weights match Phase 2 exactly; the wagon 300 kg.
- The harness item is not required to hitch in Phase 3 (no `harness` good yet; crafting adds it later). Breed `draught` must be true and `TrainedPm >= 400`.
- Spooks, falls (Riding below 30), panic, the 3 s stun after a failed taming and the Phase 6 theft prompt are not built. A failed taming try returns `Untrained`; success trains the horse to 700.
- Riding skill is not gained inside the sim: `RideBook.RidingUses` collects the uses (60 a game minute at trot, 80 at gallop) for the hero record to drain.
- Water: drinking is a command (`AnimalCare.Drink`), nothing yet sends a horse to a trough; hunger and thirst are daily and hourly steps (`RideBook.Hour`) called from the street view's game hour.
- Herds: farming has no horse herds, so horses, mules and donkeys are counted in `HerdLedger` (saved with the animals); oxen, cows and sheep are read and counted down by one through `LivestockHerds` over `LivestockState`. No change to farming. `Fold` returns a head only for an unowned, free animal that came from a herd (H-12).
- Fords: the three thresholds and ice are in `Fords`; the `BlockedPath` interrupt and ferries are not wired.
- The abstract caravans keep `Caravans.Vehicles.SpeedMmPerGs` (test H-13 asserts it equals the derived template speed and `VehicleMath.CaravanMmPerGs` on dry level road: cart 125, ox wagon 90 mm/gs). `CaravanMmPerGs` takes a surface and a grade and is what a weather-aware leg should call; the continent tick does not yet have per-leg weather, so no caravan changed speed.
- Loaned horse (open question 7): `RideBook.Loan` makes an animal owned by the lender, due in 3 game days; `ExpireLoans` takes it back, dismounting the hero. Dev ways to get animals: console `horse [breed]`, `loan`, `cart [template]`, `mud`, and `--shot-ride horse|horse_near|cart|wagon|stuck`.
**Hero and UI.** `LiveWorld.AttachRide` / `RideCommand` (E mount, hitch, drive, push, unload, reload, dig; recorded in the replay trace as `ride`). E: portal first, else dismount, step down, push a stuck cart, mount the nearest animal within 2 m (40 game seconds, broken off by walking; dismount 20), or climb onto the nearest cart's bench. Shift is the gallop, O (the new `Mounted` input layer, stacked on World) toggles the walk; Z does nothing while mounted. U hitches or unhitches, Y unloads 100 kg to the heap, ] reloads it, [ digs. The heap and every bed are `Store`s of the economy (`OtherStores`), so cargo is in the mass ledger and in the economy section; the "mounts" section holds only the rows (absent in a world nobody rode in, so old saves keep their bytes). Surface under the wheels comes from the ground tile and the street's weather (`StreetView.SurfaceHere`); slope is 0 until the ground gives a grade.
**Art.** New rig action `ride` (sitting astride, side view; one frame) and sprite `object.horse_riding` (64 x 64, saddled); the existing draught horse and ox sprites draw the teams. Atlases re-baked and copied (`props_64`, `rig_32x64`, both indexes).
