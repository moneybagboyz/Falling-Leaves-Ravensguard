# Ships and naval war: sea ships from the designer, harbours, sailing by wind, sea trade and sea raids from the continent tick, naval combat, raiders on the beach

Status: design, before any Phase 7 military code, on top of b6b39e2.
Plan refs: §3h (water and ships: coastal regions get harbours; ships from small boats to cogs and longships, sailed by wind and weather; sea trade and sea raids come in from the continent tick: a raid lands on the beach as real people from a real ship, and history's sea raids leave grudges; naval combat: missiles from the fore and aft castles, ramming, fire and boarding, people fight on the decks; voyages between coastal places outside the region are abstract journeys; things on board move with the vehicle: a moving sub-grid with its own frame, decks linked to the world by portals; ships have several decks; boats use displacement and draft; overloading, beam seas and storms can capsize; cultures' templates: a northern longship, a southern cog), §3c (wind from the weather), §5 Phase 7 (done-when: "raiders land from a ship and you fight them on the beach and on the deck"). Phase 5 carry-overs (`docs/reviews/phase-5.md`): a ferry business at a crossing, fishing boats in the fishery's daily run, wind and river-high days under the hero's boat, people in the water when a boat sinks, walking a boat's deck as its own grid; vehicles' NPC drivers on the live map.
**Division with the economy writer:** sea trade's goods, prices and merchants are the economy specs' (as caravans are); this spec gives ships, harbours, sea routes, voyage times and capacities, and raids.

## 1. Goals
1. Sea ships are vehicles from the designer with decks you walk, castles fore and aft at z+1, a hull that floats by displacement and a square sail driven by the weather's wind.
2. Coastal towns have harbours where real ships with real crews arrive, load, unload and leave; sea routes join coastal regions in the continent tick.
3. Sea raids are planned in the continent tick and arrive in the detailed region as real longships with real raiders who beach, raid a village and sail away, or are caught.
4. Naval combat uses the one resolver on moving decks: missiles from the castles, ramming hulls part by part, fire on sails and decks, grapples and boarding fights, and men overboard who swim or drown.

## 2. What exists and what is new (by file)
| File (at b6b39e2) | Today | Phase 7 |
|---|---|---|
| `Sim/Vehicles/BoatMath.cs` (`WaterTuning`: river classes, drag `DragK` 8, rowers 70 N, polers 120 N, keel clearance 150 mm, tender GM 150 mm, beam sea 10 m/s, capsize 20 pm, swamping 20 L/s, bailing 5 L/s; `SailN = 0.6 x m² x wind² x cos`; `PowerN`; `WaterInL`) | river boats: displacement, draft, power, capsize and swamp | **extended:** sea state (waves from wind and fetch), leeway, points of sail, hull speed, sea capsize |
| `Sim/Vehicles/Voyages.cs` (`WaterPath`, `Voyages.GroundMmps`: the same speed live and abstract, VD-19) | river legs | **extended:** sea legs on sea routes |
| `Sim/Vehicles/PartGrid.cs`, `VehicleBlueprint.cs` (levels as decks), parts `hull_plank, keel, gunwale, deck_plank, thwart, rowlock, steering_oar, rudder, mast, sail_square, cabin_box, ferry_ramp`; templates `rowing_boat, fishing_boat, barge, ferry` | boats as part grids | **extended:** sea parts and the cog and longship templates (3.1) |
| `Sim/Animals/RideBook.cs` (a sinking boat's cargo to `RiverBed`, people to the bank: "Phase 4 drowning is not wired: no deaths") | | **replaced:** people go into the water (4.7) |
| `Sim/Sites/SiteCrawl.Hazards.cs` (breath: drowned at `HoldBreathS + Athletics / 2`) | underground water | **reused:** the breath rule |
| `Sim/Weather/RegionWeather.cs` (`WindFromDeg`, `WindMps`, fog), `ContinentalWeather` (fronts) | wind for the region | **read** for sailing and sea state |
| `Sim/WorldGen/Region/*` (`WaterFlags.Sea` on tiles), `History/Models.cs` (`Settlement.Coastal`), `HistoryGenerator.Politics.cs` (naval wars, `SeaRaidPct` 55) | the coast; history's sea wars and raids | **read:** harbours at coastal towns; history's naval wars as grudges and casus belli (Phase 6 import) |
| `Sim/Continent/ContinentTick.cs`, `ContinentState` (land `RegionEdge`s) | | **extended:** sea edges (3.3) |
| `Sim/Combat/Melee2/FightWorld.Walls.cs` (`IFightWalls`), `Combat/Mounted/MountedLayer.cs` (the layer that carries a rider on his horse each tick) | | **pattern** for decks as walls and carriers (4.6) |
| **New** | | `Sim/Naval/`: `SeaState`, `Sailing`, `ShipFrame` (the moving sub-grid and its portals), `Harbours`, `SeaRoutes`, `SeaRaids`, `NavalFight` (carriers, ramming, grapples, boarding), `Overboard`, `NavalBook` (optional section `naval`), `NavalDesk` (tag `nv:`); `Sim/Combat/Melee2/FightWorld.Carriers.cs`; content `naval/ships.jsonc`, `naval/sea.jsonc`, new parts and templates, harbour blueprints; Godot: ships in 8 facings drawn deck by deck, the water's waves, the helm HUD |

## 3. Data

### 3.1 Sea ships (`vehicles/templates.jsonc`, culture templates)
| Template | Grid (m) | Decks | Mass empty | Draft loaded | Sail | Oars | Crew | Cargo | Notes |
|---|---|---|---|---|---|---|---|---|---|
| `longship` (Northmark) | 24 x 5 | 1 (open) | 9 t | 0.8 m | square 70 m² | 32 (16 pairs) | 34 rowers + 30 warriors | 8 t | beachable; turns 6 deg/s under oars |
| `knarr` (northern trader) | 16 x 5 | 1 | 10 t | 1.2 m | square 90 m² | 6 | 8 | 25 t | |
| `cog` (Seaboard) | 22 x 7 | 2 + castles fore and aft at z+1 | 40 t | 2.2 m | square 160 m² | none | 20 | 100 t | high sides: boarding a cog from a longship is a climb (4.6) |
| `war_cog` | as cog with `castle_fore`, `castle_aft`, `fighting_top` (z+2 on the mast) | | 45 t | 2.3 m | 160 m² | none | 20 + 40 soldiers | 60 t | |
| `fishing_boat`, `rowing_boat` | existing | | | | | | | | the fishery's daily run (4.8) |
New parts: `castle_fore`, `castle_aft` (a raised deck at z+1 with a parapet: a crenellated cell for missiles), `fighting_top` (z+2), `stem_post` (the bow; ramming), `hull_plank_sea` (thicker, 400 hp a cell), `ballast` (stone, kg), `grapple` (an item: thrown 6 m), `boarding_plank` (a 3 m portal), `oar_port`, `anchor` (an item part: holds the ship), `bilge_pump` (bailing 15 L/s with 2 men).

### 3.2 Harbours (`naval/sea.jsonc`, blueprints)
A coastal settlement (`Coastal`, tiles flagged `Sea` within 500 m) has a **harbour** placed at generation (Phase 1 placer, new blueprint `harbour_quay`, `harbour_jetty`, `slipway_sea`; a library plan the player can build): a quay or jetties with `mooring` cells (one ship each), a crane (treadwheel crane: the economy specs' industry piece) or hand loading, a customs post (Phase 6 customs), warehouses. **Beaches:** sand tiles at the shore within 2 km of a village are landing places for beachable ships (precomputed per region: `Beaches[]`, pure from the tiles).

### 3.3 Sea routes (continent)
Sea edges join coastal regions whose coasts face the same water: from the continent's `Ocean` mask, each coastal region's coastal cells; an edge between two coastal regions whose ocean cells connect within 64 km of sea path (an integer BFS over ocean cells at 2 km), `LengthKm` the path length. Derived at load (pure, cached), not saved. Ships in the continent tick move along them as carriers (`CapacityKgPerSeason` from the ships the realm's harbours hold).

### 3.4 Ships at sea and in harbour (`NavalBook`, optional section `naval`)
`ShipRow { Vehicle id, Owner (person, realm or company), Master (person), Crew (person ids), Passengers, Mission (Trade, Fish, Ferry, Raid, Patrol, Transport), Route (sea edges or a coastal path), Pos (continent mm), Heading Q10, Speed mm/s, Moored (harbour, cell), Anchored, Beached, Sea state at the last hour }`.
`SeaRaid { Id, Raiders (ship ids, persons), Target (region, settlement, beach), Plan (land, raid hours, withdraw), State (Sailing, Landed, Raiding, Withdrawing, Gone, Caught) }`.

## 4. Rules

### 4.1 Sailing by wind
- **Points of sail** for a square sail: push `SailN = 0.6 x sail m² x wind² x cos(angle off astern)` (Phase 5, existing), only for winds from 90 to 180 degrees off the bow (a square-rigger cannot point higher than a beam reach); with the wind forward of the beam the sail gives 0 and a ship drifts, anchors, or rows.
- **Speed** from force against drag: `v² = force / (DragK x wetted area)` (the Phase 5 boat drag), capped at **hull speed** `v_hull = 1.25 x sqrt(waterline m)` m/s (integer square roots, no `Math.Sqrt` in the sim): a 22 m cog about 5.9 m/s, a 24 m longship about 6.1 m/s; cruising 2.5-4 m/s.
- **Leeway:** a ship on a beam reach slips downwind at `150 pm` of its speed (longships, a shallow keel: 250 pm).
- **Oars:** 70 N a rower (x3 on a hard stroke for 60 s, Phase 5), so a longship of 32 rowers makes about 2.5 m/s in a calm and can row against a light wind; rowers tire by stamina.
- **Steering:** a rudder or steering oar turns at `rudder rate x speed / 3 m/s` (cog 3 deg/s at 3 m/s, longship 6 under oars), none at 0 speed (oars can turn a longship on the spot at 10 deg/s).
- **Same numbers live and abstract** (the VD-19 rule): the abstract tier moves a ship hourly at the same speed from the hour's wind; the live tier each tick.

### 4.2 Sea state, storms and capsizing
- **Waves** from the wind and the fetch (the open sea upwind, from the ocean mask): `wave m = wind mps x min(fetch km, 50) / 250` (a 10 m/s wind over 50 km: 2 m).
- **Beam seas:** the Phase 5 capsize check every 20 game seconds with the sea's waves for the river's; a ship with GM under the tender line in a beam sea over 10 m/s capsizes with 20 pm a check (0x7D0); waves over the freeboard swamp at 20 L/s a metre of excess (bailing 5 L/s a man, a pump 15).
- **Storms** (wind >= 17 m/s from the weather): masters with Seamanship (new skill, appended: trains by sailing hours) heave to or run before it; a ship caught off a lee shore with the wind onshore drives ashore unless it can claw off (`v` under sail and oars against leeway): wrecked on rocks (hull cells broken at random by 0x7D1) or beached on sand.
- **Fog** (weather) limits sight to 100 m: no landfall plans, collisions possible.

### 4.3 Harbours, ferries and the fishery (Phase 5 carry-overs)
- A ship enters a harbour under oars or warped (2 men on a rope, 0.3 m/s), moors in a free mooring cell, and its cargo is loaded and unloaded by hauling jobs (the hold's goods are real; the economy specs decide what is bought and sold). Customs at the post (Phase 6).
- **Ferries:** the ferry template at a river crossing becomes a business (Phase 5 rooms: `ferry_landing`), with the Phase 5 fees (15 a person, 40 a cart, in mites converted by the coinage spec), a ferryman (Seamanship or Fishing), and the hero's crossing paid.
- **Fishing boats** go out each day (`FishKgPerCrewDay` 12, winter x0.4: Phase 5 numbers) from the harbour or river bank in the fishery's daily run, and their catch lands as goods.
- **NPC masters and drivers** sail and drive on the live map with their own skill (the Phase 5 carry-over "vehicles: NPC drivers on the live map": also carts).

### 4.4 Sea trade and sea raids from the continent tick
- **Sea trade:** the continent tick's trade step (economy specs) uses sea edges like land edges with ships' capacity and voyage time (`LengthKm` / the season's mean ship speed from the region's mean wind). When a voyage's destination is a detailed region's harbour, a **real ship** arrives: the ship row with its crew (persons made by the hosts' rule at the gate: armies spec 4.4; here at the harbour mouth), sailing in from the sea's edge of the region.
- **Sea raids** (`SeaRaids`, continent tick, once a season in spring and summer): a realm with longships (a coastal raider culture: content `raids_by_sea` per culture, Northmark yes) scores coastal targets in enemy or weak realms: `value (stores and coin of the target villages, known by rumour) / sail days`, less risk (the target's known warbands within a day); raids at war, or by culture in peace (`raid_in_peace_pm` 300 for Northmark). The raid is 1-4 longships with 30-60 men each.
- **History's grudges:** history's naval wars and slave raids (`SeaRaidPct`, `SlaveRaid`) are already casus belli and grudges (Phase 6 import); a raid in play adds a `raid` casus belli (Phase 6 `CbKind.Raid`) to the victim's ruler.

### 4.5 The landing (done-when)
- A raid whose target is in the detailed region arrives **as real ships** at the region's sea edge and sails (live if the bubble is near, else abstract) to its target's beach, timed for dawn (the raiders' plan).
- **Beaching:** a longship runs onto a sand beach at 1 m/s (the hull's keel on sand: a `Beached` state; no damage on sand, hull damage on rocks); raiders jump down into the shallows (footing `Ford`: walk x0.5 for 10 m) and form on the beach. 6 to 10 men stay as ship guards.
- **The raid** (raiders' warband rules: armies spec 4.1 Raid): they run to the village (1-2 km), take coin and goods (Ledger and mass to the raiders), livestock to the shore, captives where their culture's law allows war captives (Phase 6 bondage), burn what resists (Fire), and withdraw to the ships within the plan's hours (default 4 game hours, less if the alarm brings a warband).
- **The defence:** the village's people flee or fight (Phase 3 utility), the church bell rings (a warning carried 3 km: warbands and the hero hear it as an interrupt), the lord's warband rides; a fight on the beach is a battle bubble (battle spec) when over 60, a Phase 4 fight below. The raiders' ships are **targets:** burning a beached longship (Fire on its parts) or taking it strands the raiders.
- **The deck:** raiders fighting their way back aboard, or the hero boarding a longship as it pushes off, is a fight on the deck (4.6). Afloat in 2 m of water a pushing-off ship takes 20 s to float free with its rowers.

### 4.6 Naval combat: carriers, missiles, ramming, fire, boarding
- **Decks as a moving sub-grid** (`ShipFrame`): each ship's part grid is a local frame (cells per deck level) with a transform (position, heading) updated each tick; people on board stand in local coordinates and the world sees them through the transform (the plan's "things on board move with the vehicle"). Portals: gangplanks and `boarding_plank` to the quay or another ship; `tower_ladder`-like ladders to the castles (z+1) and the fighting top (z+2).
- **In a fight** (`FightWorld.Carriers.cs`): a fighter on a carrier gets the carrier's motion applied before his own step (as the mounted layer carries a rider on his horse); the fight's `IFightWalls` include each ship's cells (gunwales block movement; the castles' parapets block missiles by the crenel rule of `sieges-and-engines.md` 4.4); the water around is footing `DeepWater` (a man who steps over the side is overboard: 4.7). A heaving deck in waves over 1 m adds a stumble (Phase 4 stumble 0x417 rate x2) and -5 aim points.
- **Missiles** from the castles and the fighting top get the height advantage (+15% range, +5 points per level) and the parapet cover; bows and crossbows only (no engines on ships in the base content beyond a springald on a war cog's castle).
- **Ramming:** a ship's `stem_post` hitting another's hull at closing speed `v_c`: the struck cell takes `max(0, M - threshold) / 60` hp (the wall rule of `sieges-and-engines.md` 3.1, `hull_plank_sea` threshold 600, hp 400) with `M = the rammer's mass t x 1,000 x v_c m/s / 10` (a 9 t longship at 4 m/s: 3,600; a 40 t cog at 3 m/s: 12,000 kg·m/s); the rammer's bow takes a third; a holed hull below the waterline takes water by the Phase 5 swamp rule (20 L/s a broken cell) and sinks when its freeboard reaches 0. Medieval ships rarely rammed: the AI rams only with a heavier ship against a lighter one that cannot flee.
- **Fire:** fire arrows (an arrow with a burning head: a `fire_arrow` ammo item, ignition on the cell it lands in) and pitch pots (thrown 15 m, ignition in a 1 m radius) set sails (canvas: low ignition point) and decks (Phase 6 fire cells on the vehicle's grid); a burning sail drops (mast cell damage); crews douse with buckets (Phase 6 firefighting with sea water at hand: x2 rate).
- **Grapples and boarding:** within 6 m a man throws a grapple (Throwing skill; hit by the Phase 4 thrown-object rule against the enemy's gunwale cells); two grapples bind the ships, and haulers pull them together in 20-40 s (2 men a grapple at 300 N against the ships' drift); bound ships move as one carrier (their frames joined). Boarding: over the gunwales where the decks are within 1 m in height (a longship against a cog's 2 m higher side: a climb of 4 s per man on a grapple's rope, one man a rope) or by a boarding plank. The fight on two decks is a battle on levels (a cog's castles are a stronghold: defenders shoot down at boarders).
- **Ending:** a ship whose crew all are down, overboard, yielded or dead is **taken** (a prize: the captor's ship row and cargo, Phase 4 loot rule); a burning or sinking ship is abandoned.

### 4.7 Overboard, swimming and drowning (Phase 5 carry-over: people in the water)
- A person in deep water swims at 0.6 m/s (Athletics 40+: 0.9) if his worn mass (armour and kit) is under **15 kg**; above it he sinks unless he sheds kit (dropping a shield 1 s; a mail shirt 10 s under water: Athletics test each second, 0x7D2) or holds something that floats (an oar, a plank, a barrel: wreckage cells of a broken ship float).
- **Breath:** the Phase 4 site crawl rule (`HoldBreathS + Athletics / 2` seconds under water, then `Drowned`, `DeathCause` with the sinking or the blow as cause); cold water (under 10 C, the weather's sea temperature: air temperature lagged by a season) takes 1% stamina a second and exposure per Phase 3.
- A sinking boat's people now go into the water (replacing `RideBook`'s "people to the bank"); a river's current carries swimmers downstream at its speed; a swimmer reaching a bank or a ship climbs out in 4 s.

## 5. Tick placement
| Where | What |
|---|---|
| Continent tick (seasonal) | sea trade on sea edges (economy specs' step); sea raids planned; ships between regions as carriers |
| H1 hourly (with `SystemOrder.Armies`) | ships at sea in the region move; harbour arrivals and departures; raids sail |
| H5 hour 0 | fishing boats' daily run; ferry fares; raid plan steps |
| Live 20 Hz | ships in the bubble (sailing, steering, waves); naval fights with carriers; overboard and swimming |
| Barrier | the hero's helm and naval commands; boarding joins frames |

## 6. RNG streams (0x7D0..0x7DF)
| Stream | Use | Key |
|---|---|---|
| 0x7D0 | capsize in a beam sea | ship, 20-second step |
| 0x7D1 | wreck damage on rocks | ship, cell |
| 0x7D2 | shedding kit under water | person, second |
| 0x7D3 | sea raid target ties and the raid's size | realm, season |
| 0x7D4 | persons made for an arriving ship's crew | ship, index |
| 0x7D5 | a grapple's catch on a moving gunwale (the throw itself uses the Phase 4 thrown rule) | ship pair, throw |
| 0x7D6..0x7DF | free |

## 7. Saves
Optional section **`naval`** (ships at sea and in harbour, raids). Harbours are generated structures (chunk content) and their damage is wall-like (`ChunkDelta`); sea edges are derived at load. `Skill` +`Seamanship` (append only; a save migration is not needed: absent skills read 0). `Occupation` +`Sailor`. Vehicle part kinds and templates mapped by name. v8 stays. Round trip: SH-16.

## 8. Chronicle
| Type | Key | Params | Cause |
|---|---|---|---|
| `sea_raid` | `ch_sh_raid` | raiders' realm or leader, settlement, ships, taken, burned, dead | the war, history's grudge or the culture's raiding (`Raid`) |
| `raiders_caught` | `ch_sh_caught` | settlement, raiders lost, ships burned or taken | the raid |
| `ship_taken` / `ship_sunk` | `ch_sh_taken`, `ch_sh_sunk` | ship, master, captor, place | the fight (`Naval`, new `CauseKind`) |
| `shipwreck` | `ch_sh_wreck` | ship, master, drowned, place | the storm (`Weather`) |
| `ship_arrived` | `ch_sh_arrived` | ship, harbour, from region, cargo key | (not written for ordinary trade; only a first arrival of a route or a notable ship) |

## 9. UI and keys (960 x 540)
- **At the helm** (E at the steering oar or rudder): the Phase 3/5 driving controls (W more sail or oars, S less, A / D steer; the Mounted context the cart driver already uses) and a helm HUD: wind arrow and speed, point of sail (a coloured arc where the sail draws), speed, heading, leeway, waves, freeboard and water in the hull, crew on oars.
- **Crew orders** through the order wheel (R): Row hard, Ship oars, Raise or Reef sail, Drop anchor, Beach her, Grapple, Board, Repel boarders, Loose at will, Douse the fire; the ship's fighting men are formation 1 (F1). No new keys.
- **Harbour:** Q verbs on a ship (Book passage, Hire the ship, Load, Unload, Buy the ship) and on the master.
- **The map:** sea routes as dotted lines, ships as icons, known raiders' sails as warnings with their age.

## 10. Golden tests
| id | Case | Expected |
|---|---|---|
| SH-1 | Templates float | cog and longship designs: displacement, draft (2.2 m, 0.8 m loaded), GM above the tender line; the designer warns on an overloaded cog |
| SH-2 | Points of sail | a cog with a 10 m/s wind from astern, on a beam reach and close-hauled: push per `SailN`, 0 forward of the beam; speed capped by hull speed |
| SH-3 | Oars | a longship of 32 rowers in a calm: about 2.5 m/s; against a 6 m/s headwind it still makes way; rowers tire |
| SH-4 | Same speed live and abstract | a 20 km sea leg: the same seconds in both tiers (VD-19 at sea) |
| SH-5 | Sea state | 10 m/s over 50 km of fetch: 2 m waves; a tender cog in a beam sea capsizes at 20 pm a check; swamping and bailing per the rule |
| SH-6 | Lee shore | a storm onshore: a cog that cannot claw off drives ashore; on sand beached, on rocks wrecked |
| SH-7 | Harbour | a cog moors, unloads 40 t by hauling jobs, pays customs, leaves; the mooring freed |
| SH-8 | Ferry and fishing (carry-overs) | a ferry business at a crossing takes fares; fishing boats land 12 kg a crew-day (x0.4 in winter) |
| SH-9 | Sea edges | two coastal regions joined across a bay: an edge with the BFS length; two backing onto different seas: none |
| SH-10 | Raid planning | a Northmark realm with longships at war: a raid on the richest weakly guarded coastal village known; a raid casus belli for the victim |
| SH-11 | **The landing** (done-when) | 2 longships beach at dawn; 70 raiders as persons; they raid the village 1.5 km inland, take goods and captives, burn a barn, run back; the bell brings the warband and the hero; the fight on the beach in a battle bubble |
| SH-12 | **The deck** (done-when) | the hero boards a longship pushing off: a fight on its deck on a moving frame; a man knocked over the side in mail sinks and drowns by the breath rule |
| SH-13 | Carriers | fighters on a moving ship keep their deck positions as it turns (positions exact to the mm through the transform); the fight hash equal across runs |
| SH-14 | Ramming and fire | a 40 t cog rams a longship at 3 m/s: the struck hull cells' hp per the rule, water in, the longship sinks; a fire arrow ignites a sail and it drops |
| SH-15 | Grapples and boarding | two grapples bind the ships in 20-40 s; a climb onto a cog 4 s a man a rope; a taken ship is a prize with its cargo |
| SH-16 | Save | ships at sea, a raid under way, a ship in harbour: save, load, save byte-identical; `NavalHash` equal on Linux and Windows |

## 11. Budgets
Ships in a region (<= 30) moving hourly <= 0.3 ms; sea routes at load <= 50 ms (cached); a live naval fight of 2 ships and 100 people inside the battle budget (carriers <= 0.2 ms a tick); the `naval` section <= 40 KB; content <= 30 KB.

## 12. Open questions (recommended defaults)
1. **Can a square-rigged ship sail upwind?** No (beam reach at best); longships row. Default as written (historical, and it makes the wind matter).
2. **Raids in peace by raider cultures** (Northmark 300 pm a season of choosing to raid when a weak target is known). Default as written. (For the user: whether raiders strike at peace or only at war.)
3. **Swimming in armour:** sinks above 15 kg worn unless shed. Default as written (brutal, legible: the HUD warns "you will sink" before you step off).
4. **Ships in the battle-size cap:** people on ships count as soldiers. Default yes.

## 13. Build parts and estimate
| Part | Content | Gate | Months |
|---|---|---|---|
| SH-A ships and decks | sea parts and templates, `ShipFrame` (moving sub-grid, portals, walking the deck), overboard, swimming, drowning (replacing the bank rule) | SH-1, SH-12 (sim side), SH-13 | 1.2 |
| SH-B sailing and harbours | points of sail, hull speed, leeway, oars, steering, sea state, storms, lee shores, harbours and beaches, ferries and fishing, NPC masters | SH-2 to SH-8 | 1.1 |
| SH-C the sea in the continent tick and raids | sea edges, ships as carriers, arriving ships as people, sea raids planned and landed, the bell, the raid's withdrawal | SH-9 to SH-11 | 1.0 |
| SH-D naval combat | carriers in `FightWorld`, missiles from castles, ramming, fire on ships, grapples, boarding, prizes | SH-12, SH-14, SH-15, SH-16 | 1.3 |
| **Total** | | | **4.6 months of code** (+ art: cog, war cog, longship, knarr deck by deck in 8 facings, waves, harbour pieces) |

## Notes (P7-SH-A, built)
- **What exists:** nine sea parts (`hull_plank_sea` for the cog's hold and 3.5 m of side, `hull_plank_long` the longship's clinker planking, `castle_fore`, `castle_aft`, `fighting_top`, `stem_post`, `boarding_plank`, `ship_ladder`, `sail_square_sea` of 10 m2 so a mast carries a stack of them: a longship 7, a cog 16) with their recipes, and the templates `longship` (24 x 5, 32 rowers, 70 m2), `cog` (22 x 7, deck 0 and castles on deck 1, 160 m2) and `war_cog` (six-row castles and a fighting top on its mast at deck 2). The designer's `Validate` takes any non-keel hull part as planking (so the sea hulls need no `hull_plank`) and warns `overloaded` when a sea ship fully laden has under a fifth of its hull's depth above the water (SH-1). `Sim/Naval/` holds `DeckMap` (the decks of a ship's part rows as a `LevelGrid`, one level a deck, ladders as portals, castle edges as parapets, the cell a ladder comes up through the gap in the parapet; the shared pathfinder walks it), `ShipFrame` (position in world mm, heading in Q24 turns, speed and a turn per tick, whole numbers with the sub-millimetre remainder kept; people, items and lights as riders in local mm; `Export`/`Restore` for the later `naval` section), `Gangways` (a boarding plank is a 3 m portal from a plank cell to another ship's or to the quay while its ends reach), `Swimmers` (4.7) and `CarrierLayer` with `FightWorld.Carriers.cs` (4.6). Gate: SH-1, SH-12 (sim side) and SH-13 green in `Naval/ShipDeckTests` (13 cases) and `CarrierBudgetTests` (two ships and 100 people about 60 us a tick against 0.2 ms).
- **Carriers:** a rider's local millimetres are the truth; each tick, before anyone moves, the ships step and every rider is put at the transform of his local place (his facing turned by the ship's turn); the fight moves him against the ship's decks as walls (`IFighterWalls`, a new interface so a man on a castle and a man under it stand on different floors; gunwale, mast, parapet and solids stop him, a hull is solid from outside); after the step his local place follows where he walked (and his world point is set to the transform of it, so the two never differ). A man who stood still keeps his local mm exactly through any turn, the dead lie where they fell, and a fight with no ships has no `Carriers`, runs as before and hashes as before. A man whose place is past the rail (an open side) or thrown over it (`TryKnockOver`, also called by a heavy landed blow, `KnockMilli`, that drives a man at the rail over it) leaves the fight (`Fled`) and the ship and enters `Swimmers`.
- **The water (4.7):** swim 0.6 m/s (0.9 from Athletics 40) in under 15 kg of worn kit; at 15 kg or more he sinks unless what he holds floats or he sheds kit (a shield in a second, other pieces a second a kilogram, an Athletics test a second at `500 + 10 x Athletics` permille, stream 0x7D2, the first piece first: shields, then the heaviest); under water the breath is the crawl's (`HoldBreathS 30 + Athletics / 2`, read from `site_constants`) and then he drowns; a swimmer that reaches a refuge (a bank point or a ship's side) climbs out in 4 s; a current carries them all. A man below Athletics 20 does not shed (he flounders), which is how a man in mail drowns. `RideBook.Sink` now tells `OnSunk` (the host puts the people in `Swimmers`; `Swimmers.Abandon` does it for a frame) instead of "people to the bank".
- **Deviations:** (1) the war cog is about 39 t empty with 60 t of cargo and a loaded draft of about 1.6 m, not 45 t and 2.3 m: those figures do not fit the cog's hull form (the cog is 40 t, 100 t of cargo and 2.2 m on the same 154 cells), so the war cog's hold is smaller and its draft shallower; SH-B may tune a form of its own. (2) The `knarr`, and the parts `ballast`, `grapple`, `oar_port`, `anchor`, `bilge_pump`, are not built: the knarr is a trader (SH-B/SH-C) and the others belong to sailing and boarding (SH-B, SH-D). (3) A drowned man dies with `DeathCause.Suffocation` (the Phase 4 breath kind), no new `DeathCause` is appended. (4) The sea parts and templates have no drawn art yet: the art-atlas and template-sprite tests skip them and `RideSprites` draws a template with no sprite from its parts. (5) The overloaded warning is a share of the hull's depth (200 permille), a design-time rule; the spec's loaded drafts are the SH-1 numbers (cog 2.19 m, longship 0.79 m).
- **Saves, streams, hashes:** no section, no version bump, no stream added (0x7D2 was registered by P7-0A and is drawn only by `Swimmers`); `ShipFrameState` is what the later `naval` section will write and a ship under sail, saved and loaded in the middle, equals the uninterrupted run (tested through `Export`/`Restore`). `NavalHash.Of` joins nothing yet (`Phase7Trail` stays empty until a section has something to hash); a fight's ships are in its `FightHash`. The generated layers did not move; `phase5_trail` and `phase6_trail` were re-pinned because the content grew (the same code on the old content gives the old lines).
- **Left out for later parts:** sailing (points of sail, hull speed, leeway, steering), sea state and capsizing at sea, harbours and beaches, ferries and fishing (SH-B); sea edges, arriving crews and raids (SH-C); missiles from the castles, ramming, fire, grapples and boarding fights, prizes, the `naval` section and SH-16 (SH-D); the Godot art (the ships in 8 facings, the helm HUD, the order wheel), runtime wiring of the hero's helm and of a sinking boat's people into `Swimmers`, and the hero's shed-kit command.
