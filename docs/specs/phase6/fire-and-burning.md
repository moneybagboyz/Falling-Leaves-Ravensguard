# Fire and burning: fire that spreads by ignition points, burning buildings and streets, smoke, firefighting, arson, upkeep of thatch, and firedamp

Plan refs: §2.10 (thermal properties per material: ignition point, heat-damage point; tiles carry an ambient temperature; only items near heat sources or in fire update; **fire spreads using ignition points, so wooden buildings and thatch burn, and stone does not**; fire is the only spreading hazard), §3a (raiders burn fields and granaries; burn an enemy's fields), §3j (treasuries, barns and granaries can be burned), §3.12 (fires are a concrete cause type), §5 Phase 6 (fire spread and burning buildings; done-when: **a thatched street burns realistically**). Carried here: Phase 5 T4 (piece upkeep: thatch rots, daub needs re-daubing, "with fire"), Phase 4 and 5 (firedamp in mines).

## 1. Purpose
Fire as one rule over materials: a cell burns when its fuel reaches its ignition point, spreads to neighbours by heat and to roofs downwind by sparks, consumes pieces and goods, fills rooms with smoke, brings down what it eats (through structural support), and is fought by people with buckets and hooks. The same rule runs coarse outside the bubble per building. Every fire has a cause in the chronicle: a hearth, a torch, a riot, an arsonist, lightning.

## 2. Dependencies
| Needs | From |
|---|---|
| Material ignition points (`MaterialDef.IgnitionPointC`), pieces with materials (`BuildPieceDef`), `BuildingGrid`, structural support and collapse | Phase 0-1, 5 |
| Lit fires (`Crafting/Fires`: hearths, camp fires), torches (`V`), `LightGrid` | Phase 3, 4 |
| Live weather (wind, rain, the Phase 4 weather row), drought (long conditions) | Phase 1, 4 |
| Wounds (`WoundKind.Burn`), breath and suffocation (`DeathCause.Suffocation`) | Phase 4 |
| Stores and sinks (`SinkReason.Burn`), item heat-damage | Phase 2, 5 |
| Crime (arson), riots | `crime-and-justice.md`, `unrest-and-revolt.md` |
| Sites, bad air (the candle gutters) in mines | Phase 4, 5 |
New: `Sim/Fire/` (`FireGrid`, `FireRules`, `Smoke`, `BuildingFires` (abstract), `Firefighting`, `Firedamp`), content `fire/fuels.jsonc`, `fire/upkeep.jsonc`. Extended: `OddJobKind` (+`FightFire`), `ActionKind` (+`FightFire`), `DeathCause` (+`Fire`), pieces (`condition` for thatch and daub).

## 3. Data
- **Fuel classes** (`fire/fuels.jsonc`, by the piece's material template; mites and sim-seconds, 1 sim-s = 1 real s at 1x = 20 game s):
| Class | Ignition (°C) | Spread P per 5 s to a touching cell at intensity 2 (permille) | Burn time (sim-s) | After |
|---|---|---|---|---|
| thatch, straw, hay | 250 | 350 | 60 | ash |
| dry timber, planks, wattle | 300 | 120 | 300 | char, then rubble when load-bearing |
| floor boards, beams | 300 | 90 | 400 | collapse by support rules |
| daub over wattle | 350 | 40 | 200 | cracked daub |
| turf, sod roof | 400 | 15 | 600 | smoulder |
| stone, brick, slate, clay tile, earth | none | 0 | - | scorched |
| goods in a store (grain, cloth, ale casks) | by their material | - | burn with the room | mass to `Burn` sink |
- **`FireCell`** (live, saved in the optional `fire` section while burning): `{ Cell (x, y, z), State (Smouldering, Burning, Burnt), Intensity (1-3), StartS, FuelLeftS, Cause (entry) }`.
- **`BuildingFire`** (abstract): `{ Structure or lot, StartHour, Burning (hours), Damage (0..1000 pm), Cause }`.
- **`fire/upkeep.jsonc`**: piece condition decay (thatch 5% a game year, daub 10% a year, timber 1% a year; below 300 pm a piece leaks (roofs) and thatch ignites at x1.5 the spread P); re-thatching and re-daubing are construction jobs.

## 4. Rules

### 4.1 Ignition
A cell ignites when a flame touches its fuel: a torch held to it (Q > Set fire: 10 sim-s; thatch 4 s), a hearth or camp fire's sparks (an unattended hearth in a thatched building: the accident rule below), lightning (a storm hour: P 0.05% per thatched roof), a burning neighbour (4.2), a thrown torch, a firedamp blast. Items in a burning cell above their heat-damage point burn (Phase 2.10).

### 4.2 Spread (live, every 5 sim-s)
Each burning cell tries each touching cell (8 around it, the cell above through a floor or roof piece, the cell below through a floor) with `P = base(fuel of the target) x intensity / 2 x wind x wet` permille (stream **0x6C0**, key cell and step): **wind** x2 downwind, x0.3 upwind, x1 across (the live weather's wind at 2+ m/s; calm x1); **wet** x0.2 in rain, x0.3 for 4 game hours after rain (wet thatch), x1.5 in a drought (Phase 1 long conditions). **Sparks:** a burning roof cell at intensity 3 throws sparks downwind: each roof cell of thatch or shingle within 8 m downwind is tried at 20 permille per 5 s, within 15 m at 5 permille. Intensity rises one step per 20 s of burning up to the fuel's maximum (thatch 3, timber 3, daub 1, turf 1) and falls as fuel ends. A cell burns `burn time` then becomes `Burnt`; a load-bearing piece burnt fails, and the Phase 5 support rules decide the collapse (a groan of 10 game seconds, then the fall).

### 4.3 Smoke and people
Smoke fills an enclosed room (RoomDetector `Indoor`) with each burning cell in it: breathable for `room volume m3 / (burning cells x 2)` sim-s, then a person inside loses breath (the Phase 4 breath rule: unconscious at 30 s, dead at 240 s without air). Standing in a burning cell: a burn wound to the exposed parts every 5 s, grade by intensity (1 scorch, 2 burn, 3 deep burn), pain by the Phase 4 table; clothing burns by its material. People flee fire (utility `Flee` with fire as a threat within 10 m) unless they fight it.

### 4.4 Firefighting
"Fire!" is a shout heard to 100 m: neighbours and guards within it score `FightFire` (owner, kin and neighbours of the burning building first; a person whose own house is downwind always). Each fighter with a bucket fetches water from the nearest well, trough or river (real walking; a bucket chain forms when 4+ people stand between the water and the fire: buckets pass at 1 per 3 s), and each bucket on a cell lowers its intensity by 1 per 3 buckets (thatch soaked: x0.3 spread for 2 game hours). Fire hooks (a pole with a hook, 20 s) pull down a thatch roof cell: removed fuel, a firebreak. A building is lost when its cells burn faster than they are doused; the town's response decides whether the street goes.

### 4.5 Outside the bubble (abstract, per building)
A building fire burns 3 game hours; each hour it spreads to each building within 10 m with P = 40% (thatch roof), 15% (timber walls, other roof), 0 (stone with slate or tile) x wind (downwind x2) x wet (as 4.2), less 50% if the settlement has 10+ able adults within 200 m (a bucket chain). Damage: thatch-and-timber 1000 pm (gutted), stone with a timber roof 400 pm. **Accidents:** each dwelling with a hearth catches with P per game year 0.2% (town, thatch), 0.1% (village), x2 in winter, x3 in a drought (stream **0x6C3**, key structure and day); a riot's arson, an arsonist's crime and lightning are the other causes. Damaged buildings become growth or construction work (rebuilding is a contract); goods inside burn in proportion. A building entering the bubble while burning becomes live fire cells on its burning pieces.

### 4.6 Arson and causes
Setting fire to another's property is the crime `arson` (witnessed by the crime rule; bounty 2,000 + damage/10). Every fire has a chronicle entry `fire_started` with its cause (a person and his reason, a hearth and its thatch, a storm), and `fire_ended` with what burned (buildings, goods by value, deaths): each death's cause chain reaches the fire's cause.

### 4.7 Upkeep (Phase 5 T4)
Pieces of thatch, daub and timber lose condition yearly (data 3); an owner's household (or steward) posts a re-thatch or re-daub job when a roof is below 400 pm; rotten thatch (< 300 pm) leaks rain into the room (wet exposure, Phase 3) and catches at x1.5.

### 4.8 Firedamp
In coal seams and deep mines (z <= -3, Phase 5 mines' `bad_air` hazard), cells may hold firedamp (generated per site, stream **0x6C2**, key site and cell; 10% of coal-seam cells). An open flame (torch, candle, lamp) in a gassy cell ignites it: a blast of radius 8 m (crush and burn wounds by distance, intensity 3 at the centre, 1 at the edge), Phase 4 cave-in checks on the cells in range, the flame's carrier knocked down. **Warning:** a candle or lamp burns taller and blue in a gassy cell (light rendering, a HUD line "The flame burns blue"), the plan's honest tell; a miner with Mining >= 40 names it.

### 4.9 Tick placement
| Where | What |
|---|---|
| Live tick (20 Hz; the spread step every 5 sim-s, staggered by cell) | fire cells, smoke, burn wounds, firefighting |
| Barrier | arson (a hero command), collapse of burnt structures, goods burned, the chronicle |
| H1 (hourly) | abstract building fires, spread, accidents (at H5 hour 0 for the day's accident rolls) |
| Yearly (S1) | upkeep decay |

## 5. Determinism
Streams: **0x6C0** spread (key cell and step), **0x6C1** sparks (key source cell and step), **0x6C2** firedamp placement and ignition, **0x6C3** accidents (and lightning, draw 1). Cells are stepped in (z, y, x) order; the spread step is keyed on sim-seconds / 5, not on frames. `FireHash` covers fire cells and building fires.

## 6. UI (960 x 540)
Animated fire on cells (3 intensities x 4 frames), smoke plumes and dark rooms, char and ash tiles, fire as a light source at night; the HUD shows "Fire!" with a direction; Q verbs Set fire, Fetch water, Pull down thatch; the Ledger alerts the owner of a burning building. The chronicle's fire entry lists losses.

## 7. Tests (golden)
| id | Case | Expected |
|---|---|---|
| FB-1 | Fuel table | every piece's fuel class follows its material; stone, brick, slate and tile pieces never ignite |
| FB-2 | One cottage | a thatched timber cottage lit at one roof corner in still air: the roof gone in 120-360 sim-s, the walls by 900 sim-s; the stone hearth stands |
| FB-3 | **Done-when: a thatched street** (OV-4) | 10 thatched cottages with 6 m gaps along a street, wind 5 m/s along it, dry: burns end to end in 600-1,800 sim-s (3.3-10 game hours of the clock), downwind first; with the wind across, 30-70% of the houses burn; a stone house with a slate roof in the row does not ignite |
| FB-4 | Rain | the same street in steady rain: the fire stays in the first one or two houses |
| FB-5 | Firefighting | 12 villagers with buckets and a well 30 m away: a bucket chain forms; the fire is held to 3 houses or fewer in 8 of 10 seeds |
| FB-6 | Smoke | a person asleep in a closed 40 m3 room with 4 burning cells: unconscious after about 5 s of air plus 30 s, dead at 240 s unless pulled out |
| FB-7 | Collapse | burnt load-bearing beams fail and the floor above falls by the Phase 5 rules (the groan, then the fall) |
| FB-8 | Goods burn | a barn of 2 t of grain burns: 2 t to the `Burn` sink; the mass audit exact |
| FB-9 | Abstract | 10,000 town thatched dwelling-years: 20 +-5 accidental fires (0.2%); spread per hour by the table; a stone town loses none to spread |
| FB-10 | Arson | a fire set by the hero while seen: arson, bounty 2,000 + damage/10; the chronicle's `fire_started` cause is the hero's act; deaths in the fire chain to it |
| FB-11 | Upkeep | thatch from 1,000 pm loses 50 a year; at 300 the roof leaks and ignites at x1.5 |
| FB-12 | Firedamp | a torch carried into a gassy cell: a blast of 8 m, wounds by distance; a candle burns blue in a gassy cell before |
| FB-13 | Bubble handoff | a building burning in the abstract when the hero arrives becomes live fire cells on the same pieces; the damage is not counted twice |
| FB-14 | Determinism, save | save mid-fire -> load -> save byte-identical; the fire continues to the same cells (`FireHash`) |

## 8. Open questions (recommended defaults)
1. **Fire speed** (overview T6): real sim-seconds, so a cottage roof burns in minutes and a street over a few game hours (20-30 real minutes at 1x): realistic and fightable. Default as written.
2. **Can the hero burn fields?** Yes: standing crops in late summer are fuel class straw (0 in spring), a field cell burns like thatch; it is arson on another's field. Default as written.

## 9. Budgets
Live: 400 burning cells <= 0.6 ms a tick averaged (each cell tried every 5 sim-s, staggered); smoke per room <= 0.02 ms a step; abstract building fires <= 0.3 ms an hour; save: fire cells 16 B each while burning, upkeep conditions 1 B a piece in the structure rows (<= 60 KB a busy town); art: fire 3 x 4 frames, smoke, char, ash, rubble tiles, bucket and hook.
