# Sieges and engines: walls with hit points, breaches, engines from the designer, wall-walk defence, sallies, starvation and winter sieges, war wagons and wagon forts, chariots

Status: design, before any Phase 7 military code, on top of b6b39e2.
Plan refs: §3d (sieges: engines built through the job system from hauled timber: ladders, rams, mantlets, siege towers that reach the wall-walk at z+1, trebuchets; walls are structural pieces with hit points in `ChunkDelta`; a breach is a destroyed segment and stays until repaired; defenders fight from wall-walks and towers with murder holes, sallies and fire against engines; starvation sieges on the town's real stockpile; winter sieges also starve and freeze the besiegers), §2.2a (levels: walls with a walk at +1 and towers to +2/+3; height gives archers range and hit chance; structural support: an upper tile needs a wall, beam or post within 3 tiles; ladders can be pulled up; sieges use wall-walks, towers and siege towers), §3h (vehicles: siege engines are vehicles from the designer: rams, mantlets, siege towers to z+1, wheeled trebuchets, designed, built, hauled and pushed with the same rules; war wagons and wagon forts as a field fortification and a formation; chariots: driver plus archer or spearman, a charge adds momentum as horses do, countered by braced spears and bad ground; damage per part through the resolver; the Phase 5 carry-over "combat damage per part"), §4A.3 ("walls only ever take a default 40 damage" and "the siege AOE callbacks are swapped" in the old code; sieges: engines have dispersion cones, crews take cover), §5 Phase 7 (done-when: "an AI faction besieges and breaches your walls"; content: about 10 siege engines and defences).

## 1. Goals
1. Walls are real: every metre of wall, gate and tower is a piece with hit points that engines and fire wear down, saved in `ChunkDelta` (generated walls) or the construction book (built walls), so a breach is a hole that stays until masons repair it.
2. Engines are vehicles from the Phase 5 designer, built by people from hauled timber and iron through the job system, pushed or hauled into place, crewed, shot, burned and broken part by part.
3. An assault is a battle bubble (battle spec) on several levels: ladders and towers to the wall-walk at z+1, defenders on the walk and in the towers, a ram at the gate under a roof, murder holes, sallies.
4. A siege without assault is starvation on the town's real stocks, and in winter the besiegers freeze and sicken too.
5. Hussite war wagons chain into a wagon fort that is both a field fortification and a formation; chariots fight by the same momentum rules as horses.

## 2. What exists and what is new (by file)
| File (at b6b39e2) | Today | Phase 7 |
|---|---|---|
| `Sim/Vehicles/PartGrid.cs`, `VehicleBlueprint.cs`, `VehicleDesigner.cs`, `VehicleMath.cs`, `VehicleTable.cs`, `VehicleWorks.cs` (build jobs per part), `VehicleRules.cs`; content `vehicles/parts.jsonc` (frames, axles, wheels, shafts, hulls, masts...), `templates.jsonc` (12 templates: carts to ferry) | the part grid, designs, building part by part, driving, getting stuck, levels on vehicles (decks) | **extended:** siege and war parts (3.2), 10 new templates, per-part damage through the resolver, crews, pushing by people |
| `Sim/Construction/*` (`BuiltCell.Hp`, `PieceCosts.BuiltHp`, `ConstructionWorld.Collapse.cs`), content `build/pieces.jsonc` (`wall_stone` 2,000 hp, `wall_crenellated` 1,500, `wall_brick` 1,500, `wall_timber` 600, `palisade` 500, `gate_iron` 600, `gate_wood` 300, `floor_wall_walk` 150) | built pieces with hit points, structural support and collapse (Phase 5) | **extended:** siege damage to pieces (4.1), rubble |
| `blueprints/civic_buildings.jsonc` (town gates, curtain wall 20 m and 3 m thick with a walk at +1, corner towers to +2, palisade gate, the keep) | generated town and castle walls | **read:** the walls that are besieged |
| `Sim/Save/ChunkPersistence.cs` (`ChunkDelta`: `AddObject`, `RemoveObject`, `SetProperty`, `SetTile`) | ID-keyed deltas | **used:** a per-chunk `siege_damage` object and `SetTile` to rubble (7) |
| `Sim/Fire/*` (`FireWorld` cells, ignition, `EnsureAdopted` of generated buildings into the room world) | fire on built and adopted structures | **used:** fire against engines and gates (wet hides resist); the review's S1 adoption fix is a precondition (generated walls adopted the same way) |
| `Sim/Combat/Melee2/FightWorld.Walls.cs` (`IFightWalls`: move and missile blocking per 1 m cell), `FightWorld.Ranged.cs` | indoor fights with walls; missiles | **extended:** levels, crenels, murder holes, ladders and the tower's drawbridge as portals in the fight (4.4) |
| `Sim/Stores`, households' stores, granaries, markets, `Caravans` | the town's real food | **read:** starvation (4.6) |
| `Sim/Clothing/AbstractExposure.cs`, `Disease/*` | cold stress; illnesses | **read:** winter sieges (4.7) |
| **New** | | `Sim/Siege/`: `SiegeBook` (optional section `sieges`), `WallDamage`, `Breaches`, `Engines` (crew, aim, shot, reload), `EngineShot` (dispersion cones), `Assault` (opens the battle bubble over levels), `Sallies`, `Blockade` and `Starvation`, `Surrender`, `Sack`, `SiegeBrain` (AI); `Sim/Vehicles/WarWagons.cs`, `Chariots.cs`; content `siege/engines.jsonc`, `siege/walls.jsonc`, new parts and templates; Godot: engine sprites (8 facings), the siege panel |

## 3. Data

### 3.1 Wall damage (`siege/walls.jsonc`)
Per wall-like piece, an **impact threshold** (kg·m/s below which a blow does nothing) and the existing hit points (one 1 m cell of the piece):
| Piece | hp (existing) | Threshold | Fire |
|---|---|---|---|
| `wall_stone` | 2,000 | 1,500 | no |
| `wall_crenellated` (the parapet at +1) | 1,500 | 1,200 | no |
| `wall_brick` | 1,500 | 1,000 | no |
| `wall_timber` | 600 | 300 | yes |
| `palisade` | 500 | 250 | yes |
| `gate_wood` | 300 | 200 | yes |
| `gate_iron` (iron-bound) | 600 | 400 | slow (x0.3) |
| `floor_wall_walk` | 150 | 300 | yes |
**Damage per impact** = `max(0, momentum - threshold) / 60` hp (momentum in kg·m/s; integer division). One rule for rams, stones and any blow (the old "default 40 damage" bug cannot recur: there is no default).

### 3.2 New vehicle parts (`vehicles/parts.jsonc`, the designer's)
`ram_beam` (oak 6 m, 500 kg, on `ram_chains` from a frame: swing 3 m/s), `ram_head_iron` (+40 kg, x1.2 momentum), `penthouse_roof` (planks over the ram, missiles stopped), `hide_cover` (wet hides: fire ignition x0.1 while wet, dries in 6 game hours without water), `mantlet_board` (2 m x 2 m pavise board on wheels, blocks missiles), `tower_floor` (a deck level of a tower; a tower has 3, the top at the wall-walk's height), `tower_ladder` (between floors: a portal), `tower_drawbridge` (2 m; lowered onto a wall-walk within 1.5 m: a portal), `trebuchet_frame`, `trebuchet_arm` (10 m), `counterweight_box` (5 t of earth and stone loaded in place), `trebuchet_sling`, `springald` (a torsion bolt-thrower on a frame), `armour_board` and `pavise_side` (war wagons), `arrow_slit` (a board with a slit), `wagon_chain` (2 m chain and hooks between wagons), `chariot_car` (a 2-man car), `chariot_pole` (a two-horse pole).

### 3.3 Engine templates (`vehicles/templates.jsonc`, about 10, plan content target)
| Template | Grid | Mass | Crew | Moves by | Notes |
|---|---|---|---|---|---|
| `siege_ladder` | an item, not a vehicle: 6 m, 25 kg, 2 carriers | | 2 | carried | a portal to the wall-walk while raised against a wall (4.4) |
| `mantlet` | 2 x 1, wheels | 120 kg | 2 | pushed 1 m/s | cover for archers and sappers |
| `ram_covered` | 3 x 8, 4 wheels, penthouse, hides | 2.5 t | 10 swing, 6 push | pushed 0.5 m/s | a swing every 4 s: 500 kg x 3 m/s = 1,500 kg·m/s (x1.2 iron head: 1,800) |
| `siege_tower` | 4 x 4, 3 floors, drawbridge, hides | 9 t | 30 push | pushed 0.25 m/s on firm ground | the top floor at the wall-walk (z+1); 20 archers on floors 2-3 |
| `trebuchet` | 6 x 10, frame, arm, counterweight, sling | 12 t + 5 t counterweight | 8 | built in place (or wheeled on a road) | a 100 kg stone at 45 m/s, range 200-300 m, a shot each 120 real s |
| `springald` | 2 x 2 | 400 kg | 2 | carried by 6 or on a wall | a 1 kg bolt at 60 m/s, range 250 m, reload 30 s |
| `war_wagon` | 2 x 5, 4 wheels, armour boards, pavise sides, slits, chain hooks | 1.5 t | 2 drivers, 8-10 fighters | 2 horses | a wagon-fort piece (4.8) |
| `war_chariot` | 2 x 3, 2 wheels, car, pole | 300 kg | driver + archer or spearman | 2 horses | 4.9 |
| `cheval_de_frise` | 1 x 4 frame of spikes | 80 kg | 2 | carried | footing `Stakes` for horses (cavalry spec) |
| `gate_beam` / `hoarding` | defences: a beam behind a gate (+threshold 300), timber hoardings over the parapet (murder holes along the wall, flammable) | | | built on the wall | |

### 3.4 The siege (`SiegeBook`, optional section `sieges`)
`Siege { Id, Place (settlement or castle), Besieger (host), Garrison (person ids, lord), Start, State (Investing, Blockade, Bombardment, Assault, Surrendered, Stormed, Lifted, Abandoned), Engines (vehicle ids with crew), Lines (camp, palisade), Breaches (cell lists), Supply (days left as known by each side), Terms offered, SallyLast, Winter }`.

## 4. Rules

### 4.1 Walls with hit points and breaches
- Every impact on a wall-like cell (a ram's swing, an engine's stone or bolt, a pick, a falling tower) is resolved by 3.1: `max(0, M - threshold) / 60`. A trebuchet stone (100 kg x 45 m/s = 4,500) on `wall_stone` (threshold 1,500) does 50 hp; a cell of 2,000 hp falls in 40 hits; the 3 m-thick curtain needs a column of 3 cells (120 hits). A covered ram (1,800 with an iron head) on `gate_wood` (200) does 26 hp a swing: the gate (300) falls in 12 swings, about 48 s of swinging; `gate_iron` (400): 23 a swing, 600 hp in 27 swings (under 2 minutes) unless the defenders stop the ram; on `wall_stone` a ram does nothing.
- **Generated walls** (the civic blueprints placed by world generation) keep their damage in the chunk's `ChunkDelta`: one object `siege_damage:<chunk>` with a property holding the packed `(cell, hp lost)` list (`SetProperty`); a cell at 0 hp becomes rubble (`SetTile` to `rubble_stone` or `rubble_timber`, new tile codes) and the object's entry is dropped. **Built walls** keep it in `BuiltCell.Hp` (Phase 5); at 0 the piece is removed by the Phase 5 collapse path, leaving rubble.
- **A breach** is any run of wall cells through the wall's thickness at z = 0 that is rubble and at least 2 m wide: passable on foot at footing `Rubble` (walk x0.5, no horses at speed). It stays until **repaired**: a masonry job per cell (Phase 5 construction: the piece's materials and worker-hours; stone 2,000 hp needs about 1.2 t of stone and 16 worker-hours a cell) by the town's people or the besieger after a capture.
- **The walk above falls:** the wall-walk at +1 over a breach loses its support (the Phase 5 rule: a tile needs support within 3 tiles; here the parapet and walk need the wall under them); collapsing tiles drop whoever stands on them (fall damage through the resolver, Phase 4 `ResolveFall`) and add their rubble.
- **Towers** are pieces too (their walls at z 0..+2); a tower struck down to rubble at z 0 collapses its floors.

### 4.2 Engines: built, moved, crewed, shot
- **Building:** an engine is a design from the designer (templates above or the player's own), built by the job system part by part (Phase 5 `VehicleWorks`) at the besiegers' camp from **hauled timber** (a trebuchet: about 6 t of timber, 200 kg of iron, 5 t of earth for the box; a tower: 7 t of timber, 800 kg of hides; numbers in the parts' recipes), by carpenters and engineers (`soldiers-and-companies.md` role `engineer`, Carpentry), about 20 worker-days for a ram, 60 for a tower, 80 for a trebuchet. Timber is felled locally (Phase 5 felling) or carted; the economy side prices it. Defenders build engines on their walls the same way (springalds, hoardings, a stone-thrower in a tower).
- **Moving:** pushed by people (each pusher 300 N, Phase 5 `PowerN` with people as power), rolling resistance by surface; a tower needs firm, level ground (a slope over 5% or mud stops it: the attackers may fill the ditch first: `ditch_fill` job, 4 worker-hours a metre).
- **Shooting:** an engine's shot has a **dispersion cone** (`siege/engines.jsonc`): trebuchet sigma 1.5 degrees at Engineer skill 0, `x (1 - skill/150)`, and range error 5% `x (1 - skill/150)`; springald sigma 1 degree. The landing cell is drawn (stream 0x7C0) and everything there is hit: a wall cell (4.1), a roof (Fire: a burning pot ignites it), people (a 100 kg stone at 45 m/s is a blunt blow through the resolver's thrown-object path, Phase 4 resolver 4.9: far beyond any armour). Shots in the abstract tier: 1.5 an hour for a trebuchet (a shot each 120 real s is 40 game minutes), 12 shooting hours a day.
- **Crews take cover:** a crew behind a mantlet or penthouse is shot at through the board (the board's coverage blocks missiles: Phase 4 missile path).
- **Damage to engines** goes through the resolver per part (§3h, the Phase 5 carry-over): a stone or bolt breaks the part it hits (part hp from content), fire burns parts by their ignition point; a broken `trebuchet_arm` or a tower's wheel stops the engine; a tower that loses its base floor topples (everyone on it falls).

### 4.3 Investing the place, and the siege AI (`SiegeBrain`)
- A host that arrives at a walled place with a siege objective (armies spec 4.10) **invests** it: camps out of bowshot (300 m), throws a palisade line across the roads within 2 days (a `Lines` piece set), and the place is **blockaded** (4.6).
- Each day at H5 the besieger chooses (content considerations): **starve** (no engines; days = the known stock's days), **bombard** (build engines; breach days from 4.1 with his engines), **assault** (now: escalade with ladders if the wall is low (a palisade, under 4 m) or the garrison is weak (< 1 per 4 m of wall), or through a breach, or with a tower at the wall), **offer terms** (daily at the start, then after each breach: the garrison leaves with its arms; the town pays a ransom of 1/4 of its treasury), or **lift** (supply gone, winter, a relief host near and stronger by intel). Each choice is a chronicle line on the siege entry's chain.
- The defender each day: repair (a breach by night: half the masons' rate under fire), sally (4.5), ask for relief (an envoy rides out if he can get through; the relief host is the liege's campaign), surrender (4.6).

### 4.4 The assault: a battle on several levels
- An assault opens a **battle bubble** (battle spec 4.1) over the walls: the fight's `IFightWalls` is the chunks' wall cells at every level (z 0, the walk at +1, towers to +2/+3), with portals: stairs inside the towers, ladders raised against the wall, a siege tower's drawbridge, a breach (z 0), the gate when broken.
- **Ladders:** 2 carriers raise one against the wall in 4 s; it is a portal from z 0 to the walk while it stands (one man on it at a time per metre of rung: a 6 m ladder holds 3 climbers, 4 s to climb); a defender adjacent to the top can **push it off** in 3 s (Athletics test: `500 + 10 x (Athletics - 30)` pm per try, 0x7C1) and everyone on it falls (6 m: `FallMilli` through the resolver).
- **The tower** lowered at 1.5 m from the walk: a 2 m front (two men abreast) from the top floor; archers on its floors shoot at the walk from level (no height penalty).
- **Height** (plan §2.2a): a shooter one level up gets +15% range and +5 hit points; a shooter below aiming up at a crenellated walk meets the parapet: a missile from below at a defender behind a `wall_crenellated` cell is blocked by the merlon with 600 pm (the cell's `BlocksMissile` with a chance by angle, 0x7C2). Defenders lean out to shoot at the wall's foot only from hoardings or towers (flanking towers see the foot).
- **Murder holes** (the gatehouse's floor over the passage, the hoardings): defenders drop stones (a 20 kg stone falling 6 m arrives at about 10.8 m/s: a blunt blow through the resolver's falling-object path on the head or shoulders of whoever is below) or pour boiling water or hot sand (a burn wound to every person in the 1 m cell: the Phase 6 burn wounds) every 10 s per hole while the defenders have stones or a fire and cauldron.
- **The gate:** the ram's swings (4.1) under the penthouse; defenders answer with stones and fire on the penthouse (wet hides resist), and a gate beam (+300 threshold) behind it.
- **Losing the wall:** once attackers hold 10 m of walk or a breach with 20 men inside, defenders' morale takes -15 (the battle spec's formation flank term per formation); the battle continues inside the streets.

### 4.5 Sallies
A defender sallies when the besiegers' engines stand within 150 m and their guard is weaker than the sally (by the defender's sight), by night when he can: a sally formation leaves by a postern or the gate, attacks the engines (setting fire with torches and pitch: Fire ignition on the engines' parts; hides wet resist), and returns. It is a battle bubble (live) or an abstract battle (outside). A gate opened for a sally is a chance for the besieger (the gate is open until the sally is back in).

### 4.6 Starvation sieges on the town's real stock
- **Blockade:** the place's caravans stop (none routes in or out), its market trades only inside its walls (the settlement's market session sees no import), its fields outside the walls are the besieger's to forage (armies spec 4.6), and nobody walks in or out except by the siege's rules (envoys under truce, a successful runner by night: Stealth against the sentries).
- **Eating:** households, the garrison and refugees inside eat from the real stores (household stores, the town's granaries and markets, the castle's store) by the Phase 2 household rules; the garrison eats from the castle's store first, then buys or requisitions in the town (the economy spec's purveyance). Prices inside rise by the Phase 2 market rules; famine checks (Phase 2) run as anywhere; the plague and flux (Phase 6) find a crowded town.
- **Surrender** (the commander's daily test, H5): surrender when the garrison's food is under 5 days and no relief is known within 3 days' march; or when a breach is open and the garrison is outnumbered 4:1; or when the town's unrest reaches a riot (Phase 6: the townsfolk open the gate; `UnrestConcede` gains a `siege` row). Terms per 4.3.
- **Storm and sack:** a place taken by assault after refusing terms may be **sacked** (the commander's traits: cruel or greedy sack, just does not): soldiers take coin and goods from houses (Ledger transfers to their purses, mass to their packs: conserved), fires start (Fire ignition rolls per sacked house, 0x7C3), some townsfolk are killed (1-5% by the cruelty) or taken (where war captives are law), the chronicle records it, and every surviving household's grievance rises (`"sacked"` +10 for 1 year).

### 4.7 Winter sieges
From doy 46 (and on any day under 0 C) the besiegers' camp is exposed: each man's night cold stress from his clothing and the tent (Phase 3 clothing: a tent is shelter at 50% of a house) and firewood (3 kg a man a day in winter; without it every man is exposed), so frostbite and death by exposure follow the Phase 3 rules; flux and cough onsets double in camp (the `ArmyCampSize` hook); roads under snow slow supply carts (Phase 5 rolling resistance); desertion +10 pm a day (armies spec). The town suffers too but from inside its houses. A winter siege is a test of supply on both sides.

### 4.8 War wagons and the wagon fort (Hussite)
- **War wagons** are vehicles (3.3) driven in a column; in battle a wagon formation (`WagonFort`, battle spec shape) **forms a ring** (or a square of 4 sides): each wagon drives to its slot on the ring (drivers' skill: Riding as driving), the teams are unhitched and led inside, and the wagons are **chained** (`wagon_chain`, 20 s a gap with 2 men). A 20-wagon fort is a ring of about 100 m.
- **The fort as walls:** its cells are `IFightWalls` from the vehicles' part grids: `armour_board` and `pavise_side` cells block movement and missiles, `arrow_slit` boards block movement and let missiles out from inside (a shooter behind a slit is covered 0.8); gaps between wagons are closed by chains (blocks movement at the chain cell; a man can cut a chain in 30 s with an axe) or by `cheval_de_frise`. Horses refuse a fort (cavalry spec refusal with the fort's cells counted as braced polearms: 150 pm each in the arc).
- **Fighting from it:** crossbowmen and flail and polearm men inside; the parts take damage per part (the resolver against the boards' material); a broken board is a gap. **Sortie:** a gap is unchained to let a formation out and back (the order `Sortie` for the fort's formation).

### 4.9 Chariots
- A **chariot** (3.3) is a vehicle with two horses (Phase 4 horse fighters harnessed: the team), a driver (drives with Riding) and a fighter (an archer who shoots at a trot with the mounted aim penalty, or a spearman who thrusts in passing).
- **Charge:** the fighter's thrust in passing adds momentum as a rider's (`ChargeMath.ExtraMilli` with the chariot's total mass: car 300 kg + team 900 + crew 160 = 1,360 kg, and a transfer factor of 300 pm for a spear from a car: `10 x 1,360 x 10 m/s x 0.3` = 40,800 milli at 10 m/s); the team tramples (Phase 4 trample, per horse). A chariot does not turn faster than 45 degrees a second at speed.
- **Countered** by braced spears (the team refuses by the Phase 4 refusal), and by ground: footing `Woods` forbidden, `Mud` 1 m/s, `Rough` (ploughed fields) x0.4, `Ditch` and `Stakes` a crash; a broken wheel or axle (per-part damage) **crashes** the chariot at its speed (each crewman falls: `FallMilli` at the chariot's speed from 1 m). Fast on open, dry, flat ground (plan §3h).

## 5. Tick placement
| Where | What |
|---|---|
| H5 hour 0 | the besieger's and defender's daily choices; surrender test; repairs; blockade's market cut |
| Abstract hour | engines' shots (1.5 an hour each, 12 hours a day) and their damage; building engines as jobs |
| Live 20 Hz | assaults and sallies as battle bubbles; engines shooting in the bubble (real reload times); ladders, towers, rams |
| Barrier | wall damage written to `ChunkDelta` or `BuiltCell` (one delta per chunk per hour at most); breaches; collapses |

## 6. RNG streams (0x7C0..0x7CF)
| Stream | Use | Key |
|---|---|---|
| 0x7C0 | an engine shot's landing (dispersion and range) | engine, shot serial |
| 0x7C1 | pushing a ladder off | ladder, try |
| 0x7C2 | a missile from below meeting a merlon | battle, projectile |
| 0x7C3 | sack: fires, deaths, captives | siege, house |
| 0x7C4 | a runner slipping the blockade | siege, person, night |
| 0x7C5 | siege AI ties | siege, day |
| 0x7C6..0x7CF | free |

## 7. Saves
Optional section **`sieges`** (sieges, engines' crews and states, lines, terms). Wall damage lives where the plan says: generated walls in each chunk's `ChunkDelta` (the `siege_damage` object's property and `SetTile` to rubble: the existing ops, no format change), built walls in `BuiltCell.Hp`. New tile codes `rubble_stone`, `rubble_timber` are appended to the tile table (mapped by name at load, Phase 5 `ContentRemap`). Vehicles: new part kinds and templates (content, mapped by name). v8 stays. Round trip: SG-19.

## 8. Chronicle
| Type | Key | Params | Cause |
|---|---|---|---|
| `siege_begun` | `ch_sg_begun` | besieger, place, men, garrison | the campaign (`Siege`, new `CauseKind`) |
| `wall_breached` | `ch_sg_breached` | place, engine kind, days | the siege |
| `siege_assault` | `ch_sg_assault` | place, outcome key, lost a, lost b | the siege (the battle entry hangs off it) |
| `siege_sally` | `ch_sg_sally` | place, engines burned | the siege |
| `siege_surrendered` | `ch_sg_surrendered` | place, terms key, reason note (`ch_note_sg_starved`, `_breach`, `_town_rose`) | the siege |
| `siege_stormed` / `town_sacked` | `ch_sg_stormed`, `ch_sg_sacked` | place, dead, burned, taken | the assault |
| `siege_lifted` | `ch_sg_lifted` | place, reason note (`_relief`, `_winter`, `_supply`) | the siege |
| `wall_repaired` | `ch_sg_repaired` | place, cells | the breach |

## 9. UI and keys (960 x 540)
- **The siege panel** (from the Army tab or a Q verb on your engine or wall): your engines (state, crew, shots, the wall cell they aim at and its hp), the walls' damage (a strip of the wall with hp bars per segment, breaches marked), supply days on both sides (known, not true), terms offered and their buttons (offer, accept, refuse), the daily choice (starve, bombard, assault now). Ledger keys.
- **Aiming an engine:** in the world, Q on the engine > Aim, then click the target cell; the cone is drawn. Crewing: Q > Crew with... (a formation or men).
- **Walls** draw their damage states (autotile damaged variants, plan §2.7) and rubble.
- No new keys: build engines with B (the designer from a carpenter's yard or the camp), command assaults with F-keys, R and the overlay (PgUp / PgDn for the walk's level in the `Tactical` context).

## 10. Golden tests
| id | Case | Expected |
|---|---|---|
| SG-1 | Damage rule | a 4,500 kg·m/s stone on `wall_stone` 50 hp; a ram 1,800 on `gate_wood` 26 hp; a ram on stone 0 |
| SG-2 | Breach | 120 stones on one 3-cell column: rubble through the thickness, a 2 m breach passable at `Rubble`; the walk above falls by the support rule, dropping two defenders (fall wounds) |
| SG-3 | Saved where the plan says | a breached generated wall: `ChunkDelta` holds the `siege_damage` object and the rubble tiles; reload shows the breach; a built wall's `BuiltCell.Hp` |
| SG-4 | Repair | masons repair a breach cell with its materials and hours; the delta's cell restored |
| SG-5 | Engine build | a covered ram built at camp from 2.5 t of materials by 6 carpenters in 20 worker-days; a trebuchet's 80 |
| SG-6 | Dispersion | 200 shots at a cell 250 m away at skill 0 and 60: landing spread sigma 1.5 deg and 0.9 deg (+-10%) |
| SG-7 | Engine damage per part | a bolt breaks a tower's wheel: the tower stops; fire on a dry penthouse burns; on wet hides ignition x0.1 |
| SG-8 | Ladders | raised in 4 s, 3 climbers, pushed off at Athletics 50: 700 pm a try; climbers fall 6 m |
| SG-9 | Tower | pushed at 0.25 m/s on firm ground; stops in mud or on a 6% slope; the drawbridge a 2 m portal to z+1 |
| SG-10 | Height and merlons | an archer one level up: +15% range, +5 points; a missile from below at a crenellated defender blocked 600 pm |
| SG-11 | Murder holes | a 20 kg stone from 6 m: a blunt blow at 10.8 m/s on the head through the resolver's falling-object path (integer speed from a table, no `Math.Sqrt`); boiling water burns every person in the cell |
| SG-12 | Sally | engines within 150 m, guard weaker: a night sally burns an engine and returns; the gate open while out |
| SG-13 | Blockade | no caravans in or out; the market sees no import; prices rise by the Phase 2 rules |
| SG-14 | **Starvation on the real stock** | a town of 1,200 with 18 days of food, besieged: stores fall day by day as eaten; famine checks fire; surrender at under 5 days with no relief known |
| SG-15 | Sack | a stormed town: coin and goods into soldiers' purses and packs (conserved), fires by 0x7C3, grievances `sacked` +10 |
| SG-16 | Winter siege | doy 46-55 with tents and 1 kg of firewood: exposure per Phase 3 kills the badly clothed; flux doubled; desertion +10 pm |
| SG-17 | Wagon fort | 20 war wagons form a ring and chain in 3 min; cavalry refuses by the fort's count; a chain cut in 30 s opens a gap |
| SG-18 | Chariot | a spear thrust from a chariot at 10 m/s adds 40,800 milli; a broken wheel crashes it; woods forbidden, mud 1 m/s |
| SG-19 | Save | a siege with 3 engines, a breach and a camp: save, load, save byte-identical; the chunk deltas unchanged by the round trip |
| SG-20 | **Done-when** | an AI host of 1,200 besieges the hero's walled town: invests, builds two trebuchets, breaches in about 3 days, offers terms, assaults through the breach in a battle bubble; every step a chronicle entry on one chain |

## 11. Budgets
Engines' abstract shots and wall damage <= 0.5 ms an hour a siege; a siege's daily choices <= 1 ms; the assault inside the battle budget (walls via `IFightWalls` per 1 m cell at 4 levels: a lookup); wall-damage deltas <= 2 KB a chunk; the `sieges` section <= 20 KB a siege. Content: 10 templates and about 20 parts <= 30 KB.

## 12. Open questions (recommended defaults)
1. **Mining (sapping) under walls:** not in the plan's list; left out (cut to Phase 8 if wanted). Default: out.
2. **Sack:** possible after a storm when terms were refused; the commander's traits decide; the hero may forbid or allow it for his men (standing consequences). Default as written. (For the user: how dark a sack can be; default is deaths of 1-5% and burning, no further atrocities modelled.)
3. **Trebuchet rate:** a shot each 120 real seconds (40 game minutes) live, 1.5 an hour in the abstract tier. Default as written.
4. **Gunpowder:** none (the setting is pre-gunpowder; war wagons carry crossbows, flails and polearms). Default: none.

## 13. Build parts and estimate
| Part | Content | Gate | Months |
|---|---|---|---|
| SG-A walls and breaches | the damage rule, `siege_damage` deltas, rubble tiles, breaches, support collapse, repair, the generated walls' adoption (with Phase 6 S1's fix) | SG-1 to SG-4 | 1.0 |
| SG-B engines as vehicles | new parts and templates, building at camp, pushing by people, crews, aiming, dispersion, shooting live and abstract, damage per part, fire on engines | SG-5 to SG-7 | 1.4 |
| SG-C the assault | `IFightWalls` over levels, ladders, towers, drawbridges, height, merlons, murder holes, the ram at the gate, sallies | SG-8 to SG-12 | 1.0 |
| SG-D blockade and its ends | investing, the blockade's cuts, starvation on real stocks, surrender, terms, storm and sack, winter sieges, `SiegeBrain`, the siege panel | SG-13 to SG-16, SG-19, SG-20 | 1.1 |
| SG-E war wagons and chariots | the fort formation, chained gaps, sorties, chariot momentum and crashes | SG-17, SG-18 | 0.9 |
| **Total** | | | **5.4 months of code** (+ art: 10 engines in 8 facings, damaged wall autotiles, rubble) |

## Notes (P7-SG-A, built)
- **What exists:** `Sim/Siege/` holds `WallDamage` (the one rule, `max(0, M - threshold) / 60`, integer; `ImpactMilli` for the resolver's milli momentum), `WallTable` (content `siege/walls.jsonc`: a `wall_stat` row per piece with its threshold and its rubble, and the `siege_rules` row: divisor 60, breach width 2, thickness cap 8) and `WallWorld` over the chunk store. A generated wall's damage is one `siege_damage` object per chunk level in the chunk's `ChunkDelta` (id `Reserved(Chunk, space 40)` from the key; `AddObject` once, then `SetProperty` of a canonical `WallHits` list: cell index, hp lost, the piece's id as a string so a renumbered palette changes nothing, down or not); a cell at 0 hp is `SetTile` to `rubble_stone` / `rubble_timber` (two new ground pieces: walkable, no material, so they never burn) at z = 0 and simply emptied above. A built wall keeps its hp in `BuiltCell.Hp` and leaves through `ConstructionWorld.Destroy` (the Phase 5 salvage, groan and fall), then gets its rubble tile and a down entry like a generated one. `Breaches` finds ground-level rubble rows torn through a wall's whole thickness, at least 2 m side by side; `IsOpen` is the street grid's rule over the store (a breach is a path). What a breach undermines: a wall-table piece above ground (walk, parapet) falls when the cell under it is down (the plan 2.2a reach rule would let neighbouring wall carry it, so this is the spec's strict "the walk needs the wall under it"); any other piece falls when it held under the Phase 5 `SupportRules` and no longer does; the cascade runs in (z, y, x) order; people on falling cells drop 3,000 mm a level with `FallWounds` (`UseWounds`). `Repair` restores down cells (ground first, a walk cell only over a standing wall) and damaged ones once the caller's `pay` accepts a `RepairNeed` (the piece's hours and materials scaled by the share lost); the damage object leaves the chunk when its list is empty. Fire: `FireWorld.PieceBurnt` (one hook, called after the fire wrote the room world and the tiles) lets `WallWorld.Hook` turn a burnt gate or palisade into rubble and drop what it held, with `CauseKind.Fire`. Chronicle (keys only, rows in `types.jsonc`): `wall_breached` (x, y, width; note `ch_note_sg_blow` or `_burnt`), `wall_fell` (cells, people; `ch_note_sg_undermined`), `wall_repaired` (cells; `ch_note_sg_masons`); all `root` types since SG-D's siege entry is not there yet (the cause event is passed when a caller has one).
- **No section, no bump, no draw:** nothing here is saved apart from the chunk store and the construction book, so v8 is unchanged, an old save has no `siege_damage` object and keeps its bytes, and save, load, continue equals the uninterrupted run (tested on the store bytes). No RNG stream is drawn (0x7C0..0x7CF stay for the engines and the assault).
- **Deviations:** (1) the spec's SG-2 says "120 stones on one 3-cell column ... a 2 m breach"; 120 stones tear one 1 m column through, which feet already cross but the rules do not call a breach; the test takes a second column (240) for the 2 m breach. (2) A plain ram (1,500) does nothing to stone and the iron-headed one (1,800) does 5 hp, where the spec's table says "on `wall_stone` a ram does nothing". (3) `gate_iron` is in the fuel table's stone class (wrought iron), so it does not burn: the table's "slow x0.3" is the fuel table's to decide, and `walls.jsonc` carries no fire column. (4) The strict wall-top rule is applied to generated walls only; a built wall's walk goes down by the Phase 5 reach rules through the groan (`ConstructionWorld` has no strict mode yet). (5) The extra chronicle type `wall_fell`.
- **Left out for later parts (repair labour and materials belong to the economy gate, T3):** the masons' job, the stone and hours actually drawn from a store or a household (SG-4 checks the need and the restoration, not a payment: `Repair` takes a `pay` callback that a later part wires to construction contracts), the repair at night under fire and its half rate, `wall_repaired` with a place and the breach as its cause (needs `SiegeBook`), the damaged-wall autotiles and rubble art (Godot), the `Rubble` footing's x0.5 walk speed in the live pathing (the cell is open; the speed is the live tier's), `Beam`/post pieces of towers as wall-table rows, towers' floors beyond the same strict rule, wiring `WallWorld` into the runtime (SG-B/C/D create it where engines and assaults exist), and the engines, assault and siege book themselves.
