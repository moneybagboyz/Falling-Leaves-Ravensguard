# Exploration and sites: caves, mines, ruins, crypts, barrows and battlefields; hazards, torches and light, traps, the auto-map, two monsters, weather in combat

Plan refs: §3b (technology: underground layers z = -1 (cellars, crypts), -2 to -3 (catacombs, caves), down to -5 (deep mines), connected by stairs, ladders, shafts and cave mouths, generated on first visit from the site's template and its history and then snapshotted; **light is a mechanic**: a torch lasts about 20 real minutes of play (about 7 game hours at 1x), a lantern's oil about an hour, durations in sim-seconds; darkness hides you and hides them; superstitious followers lose morale in the dark; every site exists because of history: ruins and crypts 8-15 per region, caves 10-20, mines 2-5, barrows 3-8, old battlefields; hazards: bad air (a candle gutters as warning), flooding, cave-ins, getting lost; party play: ropes, ladders, picks, shovels, lockpicks, carry-out versus run speed, traps (deadfalls, pits, collapsing floors), camping below, injuries far from any surgeon, retreating, an auto-drawn map; persistence: loot is finite and historical, inhabitants are named people or animals, a cleared cave can be reoccupied by a new bandit band, collapses and opened doors stay; rare monsters: 3-6 kinds, only 1-3 lairs per region, always deep, tied to a legend, same anatomy resolver, a draugr does not bleed or feel pain, until met they are rumours, killing one is a chronicle event and earns renown), §2.2a (levels, portals, structural support within 3 tiles, falls through the resolver, light and sight between levels), §2.7 (lighting is a mechanic), §3c (weather effects on travel, combat, fire), §5 Phase 4 (site contents for caves, mines, ruins, crypts, barrows and battlefields; hazards; torches and light; traps; the auto-map; **2 monster types**; weather in combat; about 30 underground room templates), §6 (levels, fire and weather tests).
Code today: `Site` (`Kind` Ruin, Cave, Mine, Barrow, Battlefield, Hoard; `SubKind`, `Levels`, `HistoryEvent`, `CauseEvent`, `HistorySettlement`, `Artefact`, `Mineral`, `Tonnage`, `Mine` (`MineState` Active, Abandoned, Flooded, Collapsed), `Dead`, `Year`, `MonsterLair`, `LairKind` draugr, cave_beast, bog_thing) from `WorldGen/Region/RegionModels.cs` and `RegionBuilder.Sites.cs`; `LevelGrid`, `Portal` (`Stairs, Ladder, Trapdoor, Shaft, CaveMouth`, `Enabled`), `LevelPathfinder`; `LightModel` (`TorchRadius` 6, `TorchIntensity` 235, `VisionRadius(light)` = 1 + light x 39/255, ambient sun and moon 22-58), `LightGrid`; `Weather` (`WeatherState`: Clear, Overcast, LightRain, HeavyRain, Thunderstorm, Fog, Frost, Snow, Blizzard, Heatwave; `WeatherSample` with `PrecipMmH`, `WindMps`); blueprint format (`rows`, `legend`, `levels`).

## 1. Purpose
Turn the site markers Phase 1 placed into places worth going into: generated on first visit from the site's kind and **its history**, lit only by what you carry, dangerous in ways you can read (a guttering candle, creaking props, a draugr's cold) and costly in ways that matter (an injury a long climb from a surgeon). Done-when: a crypt crawl by torchlight ends with an injury far from a surgeon that matters.

## 2. Dependencies
| Needs | From |
|---|---|
| Site records and history links | exists (Phase 1) |
| Levels, portals, pathfinding, structural rule, falls | exists (`Sim/World`, plan §2.2a); falls through `resolver-v2.md` 4.9 |
| Light model and grid, day phases | exists (`Sim/Lighting`) |
| Fire and ignition (torches ignite thatch, draugr), temperature | plan §2.10 (targeted fire, no fluid flow) |
| Weather sample | exists (`Sim/Weather`) |
| Monsters: plans and AI | `anatomy-and-body-plans.md` 4.3, 4.4, `melee-and-ranged.md` 4.7 |
| Wounds, treatment windows, surgery | `wounds-bleeding-pain-healing.md` |
| Bandit bands (hideouts), captivity stashes | `morale-capture-succession.md` |
| Items: torches, lanterns, rope, picks, shovels, lockpicks, chalk; `torch`, `rope`, `bandage` goods | exists (`content/base/crafting`, `Sim/Goods`) |
| Blueprints and room templates (CI checks), names | exists (Phase 1) |
New code: `Sim/Sites/` (`SiteGen`, `RoomTemplates`, `SiteLoot`, `Inhabitants`, `Reoccupation`, `Hazards`, `Air`, `Flooding`, `CaveIn`, `Traps`, `AutoMap`, `Breadcrumbs`, `Torches`, `LairBrain`), content `content/base/sites/*.jsonc`.

## 3. Data

### 3.1 Site templates (`sites/site_templates.jsonc`; one per `(SiteKind, SubKind)`)
```jsonc
{ "type": "site_template", "id": "ruin.sacked", "kind": "Ruin", "sub": "sacked", "levels": [1, 3], "size_tiles": [48, 96],
  "rooms": { "required": ["stairs_down", "undercroft"], "weighted": [["cellar_store", 30], ["crypt_nave", 25], ["ossuary", 15], ["reliquary_vault", 10], ["collapsed_stair", 10]], "min": 5, "max": 14 },
  "loot": { "base_mites_per_pop": 150, "survive_pm": 150, "decay_pm_per_year": 10, "decay_floor_pm": 100, "tables": ["church_goods", "household_goods", "weapons_rusty"] },
  "inhabitants": [ { "role": "scavengers", "chance_pm": 300, "count": [2, 5] }, { "role": "wolves", "chance_pm": 200, "count": [2, 4] } ],
  "hazards": [ { "id": "stale_air", "chance_pm": 150 }, { "id": "collapse", "chance_pm": 200, "zones": [1, 3] } ],
  "traps": [ { "id": "collapsing_floor", "per_rooms": 10 } ] }
```
Kinds and sub-kinds: `Ruin` (`sacked`, `razed`, `abandoned`; levels 1-3), `Cave` (`den`, `hideout`, `hermit`, `smugglers`, `river`; levels 1-3), `Mine` (`active`, `abandoned`, `flooded`, `collapsed`; levels 2-4), `Barrow` (levels 1, or 2-3 with `MonsterLair`), `Battlefield` (surface only), `Hoard` (a marker, buried or lost, from a history artefact; a dig site).
**About 30 underground room templates** (blueprint files in the Phase 1 text format, `content/base/blueprints/underground/`): `stairs_down`, `undercroft`, `cellar_store`, `cistern`, `drain_tunnel`, `collapsed_stair`, `crypt_nave`, `crypt_aisle`, `tomb_niche`, `ossuary`, `reliquary_vault`, `sacristy_cellar`, `tomb_chamber`, `barrow_passage`, `barrow_antechamber`, `barrow_burial_chamber`, `mass_grave`, `cave_passage`, `cave_chamber`, `den`, `hideout_hall`, `hideout_cell`, `hermit_nook`, `smuggler_dock`, `underground_river`, `mine_adit`, `mine_stope`, `mine_shaft_head`, `mine_sump` (flooded low point), `timber_prop_hall`. Each template has `purposes` (rest, storage, burial, guard, den, dock...), `portals` (stair, ladder, shaft positions) and `anchors` (loot spots, trap spots, monster beds, light sconces). CI (plan §2.9 rules reused): every ID resolves, every room reachable from a door or portal, every purpose satisfiable.

### 3.2 Records (saved as chunk deltas on the visited site only)
```
SiteState   { Site (id), Generated (bool), Seed, Cleared (game day or -1), LootRemainingPm, DoorsOpened[], Looted[] (container ids), Collapsed[] (cell sets), FloodLevelsCm[], Reoccupied (count), MapKnown (bitset per level, RLE), Breadcrumbs (ring 400 cells per hero) }
Inhabitant  { Person or Animal id, Role, Home (room anchor), Awake schedule id, State (Home, Patrol, Hunt, Dead, Fled) }          // named people from names content, animals from species
Torch       { ItemRow, BurnLeftSimS (int), Lit (bool) } — lantern: OilLeftMl; candle
AirCell     { Class (0 fresh .. 3 foul) }  (per tile, 2 bits, static per generation, changed by events)
Trap        { Id, Kind, Cell, Difficulty (detect, disarm), Armed (bool), Triggered (count) }
```
Memory per generated site level: tiles 128 x 128 x 4 bytes = 64 KB; saved as a delta of only changed cells (looted, opened, collapsed, flooded), typically < 4 KB per site; `MapKnown` RLE about 1 KB per level.

### 3.3 Constants (`sites/constants.jsonc`)
Torch `burn_sim_s` 1,200, radius 6, intensity 235 (existing constants); `candle` 1,800 sim-s, radius 3, intensity 140; `lantern` oil 3,600 sim-s per 0.3 l of oil, radius 8, intensity 245, rain-proof; torch guttering in the last 60 s (radius 6 -> 3, intensity -> 120). `air` thresholds 800 / 600 / 400 / 250. `flood_rise_cm_per_game_hour` 30, `wade_slow_cm` 30, `swim_cm` 90. `cave_in` radius 2-3 tiles, warning 6 sim-s. `loot` tables. `reoccupy_after_game_days` 20, `reoccupy_pm_per_day` 20 (2%).

## 4. Rules

### 4.1 Generation on first visit and persistence
`SiteGen.Generate(site)` runs when the first person (the hero or a party) crosses the `CaveMouth` or enters the first `Trapdoor` or stair cell (the Phase 3 `LevelGrid` portal event), **time-sliced to <= 4 ms per frame** (plan §2.11), at most 30 ms of compute per level; the level appears when ready (a "your torch flickers over rough stone..." fade covers it, <= 600 ms). Layout: **room-and-corridor** by binary partition of the template's footprint; rooms chosen by weight from the template `rooms` table (stream 0x4B0 `SiteLayout`, key site id, level), corridors 1-2 tiles wide with a few loops; portals placed so each level is reachable from the one above; deep monster lairs get a fixed `lair` room at the deepest level. Mines: adits and stopes along the vein (the `Site.Mineral` and `Tonnage` set ore stock and drift lines); caves: cellular-automaton rock with passages (cave kind sets the share of chambers).
After generation the level is a normal chunk snapshot with deltas (plan §3b). Looted chests stay empty, **opened doors stay opened, collapses stay collapsed**, a pulled-up ladder stays up (`Portal.Enabled` false), a cleared den stays cleared until reoccupied (4.9). Test EX-2 round-trips all of this through save and load.

### 4.2 Contents from history
`Site` fields drive contents:
| Site | History inputs | Contents |
|---|---|---|
| **Ruin** (sacked, razed, abandoned) | `HistorySettlement` (peak population, culture), `RuinKind`, `Year`, `CauseEvent` | Layout: undercroft, store cellars, and for abbeys and churches a crypt (nave, aisle, tomb niches with 1-12 named dead from the settlement's lords and clergy, an ossuary, a reliquary vault); loot value `LootMites = pop_peak x 150 x survive_pm/1000 x max(100, 1000 - 10 x years_since)/1000` (an abbey of 120 sacked 60 years ago: `120 x 150 x 0.15 x 0.4 = 1,080 mites`), spread over **goods**: the event's own items (the reliquary of the abbey sacked in 287 is a named item), household goods, rusted weapons (condition 30-60%, infection flag x2, resolver 4.7); inhabitants: scavenger band, wolves, a hermit (by template chances) |
| **Cave** den | `SubKind` den | 1-3 animals (wolves, a bear) with named individuals, bones, tracks, little loot (the previous victims' remnants, 0-300 mites) |
| Cave hideout | a band (3-8 bandits from the band records), `bandit_style` | a hall, 1-2 cells (captive spot, morale file 4.5), a stash (the **loot of the band's robberies**: items from `DefeatOutcome` stashes), alarm tripwires at each entrance, a sentry schedule |
| Cave hermit | 1 hermit (a named person) | herbs, a book, a shrine, a tame animal; may trade or give a rumour; hostile only if robbed |
| Cave smugglers | 4-8 persons, a dock if the cave is a `river` cave | barrels of untaxed goods (salt, wine, cloth), a boat; they attack intruders unless paid |
| Cave river | water levels, underwater passages | swim and breath hold rules (4.5); a shortcut; drowned remains with loot |
| **Mine** active | `Mineral`, `Tonnage`, 10-30 workers | props sound (instability 50 pm), cart rails, ore stock, tools; low danger (stale air near deepest drift 200 pm) |
| Mine abandoned | years since abandonment | rotten props (instability 400-700 pm), tools and ore left, bones of lost miners, vermin; stale air pockets; **hazards high** |
| Mine flooded | `Mine = Flooded` | levels below the sump are flooded (water 120-250 cm); ladders into water; pumps rusted; shipwrecked loot |
| Mine collapsed | `Mine = Collapsed` | the entrance tunnel is blocked (rubble, dig in with a pick: one tile per 20 sim-s of `digging` quality); inside intact |
| **Barrow** | `Dead` (battle dead nearby), `HistoryEvent` | passage, antechamber, a burial chamber with a main burial and grave goods (named, from the history's lord or war dead), 1-2 traps, a mass-grave chamber if `Dead` > 0; **tabooed**: robbing seeds a `TabooRobbed` event (rumours of a curse spread, villagers react; formal crime handling is Phase 6) |
| Barrow with `MonsterLair` draugr | `LairKind = draugr`, levels 2-3 | deep chamber with 1-3 draugr beds, the named dead as the draugr, best loot (the burial hoard) |
| **Battlefield** | `Dead`, `HistoryEvent`, `Year` | surface scatter (radius 150-400 m): rusted weapons and armour pieces (condition 20-50%), skeletal remains props, broken wagons; loot remaining `1000 - 8 x years` permille (floor 100), searched by the **Search** action (60 sim-s per 10 m x 10 m cell, yield by `Perception` and stream 0x4B9), a rare **named weapon** from a history `Artefact` (legend); occasional wolves, a ghostly rumour (no real ghost) |
| **Hoard** | `Artefact` (lost or buried) | a dig marker: shovel (`digging` quality) for 120 sim-s; the artefact's container; only found through rumours or a map clue |
`LootBudget` is finite: items are drawn from `loot tables` (`sites/loot_tables.jsonc`, entries with `value_mites`, `weight`, `condition`, `named`) until the budget is spent; the remaining budget is stored as `LootRemainingPm`, not regenerated.

### 4.3 Inhabitants, scavengers and reoccupation
Inhabitants are **people or animals with ids**, created at generation (names from content; persons are `Person` rows in a `SiteInhabitants` record and are promoted like anyone). Rules: bands sleep in shifts (guard schedule profile); animals sleep in dens at their cycle; named monsters in lairs have a legend. **Reoccupation:** a site with `Cleared` set and no living inhabitants rolls once per game day after 20 game days: `P = 2%` x `region_bandit_pressure/500` (stream 0x4B8 `Reoccupation`, key game day, site), on success a new band (3-8 persons from the region's wanderers or new records) moves in with its own goods; the chronicle logs "a new band took the cave". **Scavenging NPCs** (abstract): once per game year, 10% of un-looted ruins lose 5% of their remaining loot (`LootRemainingPm`).
**Persistence of people:** a hostile inhabitant stays dead; a fled one returns after 3 game days; prisoners (if the site is an enemy hideout) are people with captivity rows.

### 4.4 Torches, lanterns, candles and light
- **Burn times are in sim-seconds** (live ticks / 20) so the 1:20 world clock cannot cut a crawl short: torch 1,200 sim-s (20 real minutes at 1x, about 7 game hours), candle 1,800, lantern 3,600 per 0.3 l oil. At 8x the world runs 8 sim-s per real second and the torch still lasts 1,200 sim-s: the test EX-5 asserts equal burn at 1x and 8x. **V** lights or snuffs the carried light; relighting needs tinder or another flame (`fire_craft`, `kindle`). A snuffed torch loses nothing.
- Light on the grid: existing `LightGrid` radial falloff from intensity 235, radius 6 tiles; **vision radius** `VisionRadius(L) = 1 + L x 39/255` at the observer's tile light: a torch's own tile 235 gives 36 m; at 100 light 16 m; at moonlight 40 about 7 m; at 0 light 1 m (adjacent only). Darkness hides you: a target standing in light < 30 is seen only inside 1 + 30 x 39/255 = 5.6 m (see detection 4.6).
- **Guttering and failing:** the last 60 s of fuel the flame shrinks (radius -3, intensity 120) and flickers (cosmetic). A dead torch cannot be seen by, nor does it hide from, anyone else's light. Carry at least 4 torches per 80 minutes of exploration (a content tooltip).
- **Water and weather:** submerged: out; rain outdoors burns torches faster (`LightRain` x1.5, `HeavyRain` and storms x2.5 plus a 0.5% chance per sim-s to go out; lanterns unaffected); wind (`WindMps` > 12 on a ridge) 0.2% per sim-s.
- **Followers with the `superstitious` trait** in darkness (carried light out or `Air` poor): -5 morale (morale file 3.1); they carry spare torches if issued.
- **Lighting budget:** <= 2 ms (plan §2.11): at most 24 dynamic lights per level in view; unseen levels are not lit.

### 4.5 Hazards
Each template lists `hazards`; zones are set at generation (stream 0x4B3 `HazardEvent`, key site, level; runtime events key game hour and site).
1. **Bad air** (`Air` class per tile: fresh 1000 .. foul 0). Sources: stagnant deep mine drifts, sealed crypt chambers, tunnels past a cave-in, sump gases (flooded mines). Effects by index `A`: **>= 800** none; **600-799** candle gutters (radius x0.7: the warning, sprite and a caption "the candle gutters"); **400-599** torches burn at radius x0.5, stamina regeneration x0.6, accuracy -10 points; **250-399** `Dazed`, speed x0.7, torch out; **< 250** unconscious in 20 sim-s of exposure and death after 120 sim-s. An unconscious person in bad air is rescued by dragging out (Athletics). A flame in `firedamp` coal pockets can explode (blunt M 300 in radius 4) but only in coal mines and only with open flame: deferred (open question E3: Phase 4 has blackdamp only). Ventilated by shafts (cells within 6 tiles of an open shaft head recover +200).
2. **Flooding.** Flooded mines have water tiles at set depth; **flood events** raise water on the lowest level of an active or abandoned mine after heavy rain: when `HeavyRain` or `Thunderstorm` lasted >= 4 of the last 24 game hours, each game day the mine rolls 4% for a flood event (stream 0x4B3) that raises the water **30 cm per game hour for 6 game hours** (chronicle event "the mine flooded", cause chain to the weather). Depth effects: < 30 cm none; 30-90 cm speed x0.6, torch ok; > 90 cm **swim**: speed 1.0 m/s, hold-breath 30 s + `Athletics`/2; in more than 15 kg of total carried gear the swimmer sinks unless an `Athletics` check (50% base) per 5 s; armour > 10 kg doubles the sink odds; light out. A flooded tunnel is impassable to non-swimmers and blocks ladders at the waterline.
3. **Cave-ins.** Structure rule (plan §2.2a): a roof needs a support (prop, wall, pillar) within 3 tiles; zones with spans > 3 tiles are `unstable` with `instability_pm` (rotten props 400-700, old natural caves 100-300, fresh mines 50). While a living entity is in an unstable zone, once per 60 sim-s: `P = instability_pm / 20,000` (1000 pm = 5% per minute), boosted x3 by loud events (explosions, axe blows on props, a big fight) and x2 by heavy rain above. A collapse drops 2-3 tile radius: **6 sim-s warning** (dust and creaks, a perception check at 50%: "the roof creaks"), then each entity in the area takes a rubble blow (blunt, area 120 cm2, `ExtraMomentum` 150,000 milli per entity unless it dodges with Ctrl (80% if free) or runs out), tiles become rubble (passable by digging, 1 tile per 20 sim-s at `digging` quality 1, 12 with a pick-and-shovel party), and may **trap** the party; the delta persists. A whole-level collapse is `MineState.Collapsed` from history (entrance blocked).
4. **Getting lost.** Without light (< 20 at the player's tile and no map) heading drifts +-30 degrees per 5 s unless wall-following (movement auto-slows to 50%); with the **auto-map** (4.7) and **breadcrumbs** you are never lost; chalk marks (item `chalk`, Q menu **Mark**) help NPC followers path.
5. **Vermin and cold:** underground temperature 8 C constant; exposure (Phase 3) applies without a fire; rats gnaw rations at 2% per game day in mines and ruins.

### 4.6 Traps and detection
Content `sites/traps.jsonc`:
| Trap | Where | Effect (resolver) | Detect difficulty | Disarm |
|---|---|---|---|---|
| `deadfall` | barrows, crypts, tombs | blunt M 160,000 milli, area 100, `upper_body` 40% / head 30% / shoulder 30% | 40 | 35 |
| `pit` | barrows, ruins, caves | fall 3 m (resolver 4.9); spiked variant adds 1-3 point blows (M 90,000, area 6, pen 200) | 30 | 30 |
| `collapsing_floor` | crypts over lower levels, ruins | all in 3 x 3 drop one level (fall 3 m) with rubble; the hole stays | 35 | n/a (avoid: tap with a pole) |
| `spring_spears` | barrow doors, treasury doors | point M 110,000, area 10, pen 400 at `upper_body` or `upper_leg` | 45 | 40 |
| `tripwire_alarm` | bandit hideouts | alarm: awakens sentries, noise 30 m | 25 | 20 |
| `bear_trap` / `snare` | hermit, den, hideout paths | blunt M 140,000 area 25 on `lower_leg`, `Broken` grade likely; Athletics to open (20 sim-s) | 30 | 25 |
Placement: by room template `anchors` and per-kind densities (barrow 1 per 3 chambers, crypt 1 per 8 rooms, hideout 1 alarm per entrance, hermit 1-2 snares, mines 0). **Detection:** while a person is within 2.5 m of a trap and facing within 90 degrees, each sim-second `P_detect% = clamp(5 + (Perception - difficulty)/2 + light_bonus, 2, 80)` with `light_bonus` +10 at light >= 120 and -15 below 30, x1.5 when sneaking (Z, half speed); stream 0x4B5 `TrapDetect` (key `GameSec/20`, person, trap). A detected trap is marked on the auto-map and can be **disarmed** (`Lockpicking` skill vs the disarm difficulty, 6 sim-s, failure triggers it with 25%) or avoided. **Trigger:** stepping on an armed plate triggers (stream 0x4B4 `TrapTrigger`) unless sneaking and under 60 kg (20% skip); NPCs know their own traps.

### 4.7 The auto-map and retracing
`AutoMap` marks a tile **known** when it is in line of sight, inside the observer's vision radius at that light (>= 30 at the tile), and on the same level (or visible through an opening): walls, doors, portals, containers, traps once detected, items once seen. Storage: one bit per tile per level (`MapKnown`, RLE), updated by a pass over the cells newly visible in the 20 Hz tick <= 0.1 ms. Display: **M** opens the map screen with an *Underground* tab for the current site (PgUp/PgDn level), the minimap shows the known area of your level, unknown frontier cells shimmer; **pins** can be placed with the Q menu (Mark); party members share the map when within 20 m (merged). **Breadcrumbs:** the last 400 cells you walked are kept per hero; Q menu **Retrace steps** autowalks back to the portal you came from (same job code as `TravelAuto` at 1x; stops on any interrupt, walking at the speed of the slowest member). Mapping is deterministic from the walk and seeds.

### 4.8 Weather and light in combat
One table (`combat/weather_mods.jsonc`) read by the resolver input builder, the ranged layer, horses and the stamina model. Values permille or points; "outdoors" only (underground and indoors ignore):
| State | Ranged `sigma` | Vision cap | Footing | Fire and torches | Other |
|---|---|---|---|---|---|
| Clear, Overcast | x1.0 | none | normal | normal | Overcast day light -10% |
| LightRain | x1.15 | none | dodge cooldown +4 ticks | torch burn x1.5 | stealth +10 (noise masked), rain: fires smoulder; roads become mud after 4 game hours (Phase 3) |
| HeavyRain | x1.30 | 60 m | dodge cooldown +6 ticks, -5 evade, mud (no charges after 2 game hours) | torch burn x2.5 and 0.5%/sim-s out; fires out | hearing x0.7; bows lose 15% range; wet clothing heavier (Phase 3) |
| Thunderstorm | x1.30 | 40 m | as HeavyRain | as HeavyRain | horses +100 panic per flash (every 30-90 sim-s); superstitious followers -3 morale; lightning is flavour only |
| Fog | x1.4 beyond 15 m | 15 m | normal | normal | surprise attacks +10 hit points; cavalry charges only at seen targets |
| Frost | x1.0 | none | ice patches (8% slip per sim-s at a gallop; infantry 1% when sprinting) | normal | exposure per Phase 3; metal grips cold, `Grip` -5 at < -10 C |
| Snow | x1.1 | 30 m | speed x0.8 (cavalry x0.6), no charges | fires harder to start (+30% time) | tracks last 2 game hours (trackers follow) |
| Blizzard | x1.5 | 10 m | speed x0.6 | torches out 1%/sim-s, fires out | stamina regen x0.8, exposure severe |
| Heatwave | x1.0 | none | normal | normal | in > 10 kg armour: stamina cost of swings +10%, regen x0.85 |
- **Wind:** the projectile path adds a lateral drift `wind_cross_mps x flight_time_s x 0.05` metres (8 m/s across, 60 m shot: 0.44 m); a wind arrow on the aim fan shows the correction (melee-and-ranged 7).
- **Light affects hits:** target light `L` modifies the attacker's skill delta: `L >= 120` 0; `60-119` -5 points; `30-59` -15; `< 30` -30 (heard, not seen; ranged cannot target an unseen foe). Night outdoors at moonlight (22-58) sits in the -15 band beyond 7 m. A torchbearer is a lit target (+0) and a beacon (enemies see him from `VisionRadius` of their own light plus his 36 m).
- The resolver's hit formula takes these as extra points on `skillA - skillD` (resolver-v2 4.1; an input field `SituationPoints`, integer).

### 4.9 Two monster types (their plans and bodies are in anatomy 4.3 and 4.4)
Both use the common creature AI (`LairBrain` on the shared `CombatBrain`), rare and legendary: only `Site.MonsterLair` sites (1-3 per region, always deep: `Levels` 2-3, existing `BuildLairs`); they exist as **rumours** (Phase 3 rumour seeds from the site's legend) until met; killing one is a chronicle event with renown (+ a `Monster slayer` reputation stub and a rumour to nearby settlements within 2 game days).
- **Draugr** (barrow dead; `LairKind = draugr`; 1-3 in the deepest chamber): **dormant** on their biers; wake when (a) a grave good is removed from the chamber, (b) combat noise within 20 m, or (c) a living person stays in the chamber for 60 sim-s; once a trigger is true, wake with 10% per sim-s. Awake: hunts the nearest living by sight and hearing (light does not matter: it can see in the dark with its own senses 12 m), walks 2.0 m/s, lunges 3.5 m/s for 5 s then rests 20 s, no stamina limits, strength 1,900; follows the party out of the barrow up to **100 m from the barrow mouth** then returns to its bier after 120 sim-s without a living target in 60 m. It never flees, never yields, feels no pain or blood loss; torches and fire are the answer (anatomy 4.3: head burns through in about 30 sim-s). Drops: the named burial goods (a named weapon, a torc), a chronicle line "Ketil Ravenfoot walks again".
- **Cave beast** (`LairKind = cave_beast`; 1, 25% a pair): the **lair** is on the deepest level with a bone-strewn den; daily cycle: asleep 06:00-22:00 local game time in its den (hears sleepers' noise at 8 m), awake and hunting 22:00-06:00 and whenever disturbed; **hunts by sound**: hearing radius 25 m (walking 15, running 25, sneaking 8, fight noise 30); investigates a sound for 20 sim-s then returns; attacks anything within 8 m; feeds (drags a kill to the lair); **flees at 30% blood** to the lair and recovers slowly (1% of its blood per game hour). Light does not blind it (its eyes are useless); a sneaking, silent party can pass a sleeping beast (stealth check, `Stealth` vs hearing 8 m at 1% per sim-s per 10 skill below 50). Drops: bones, gnawed gear, the stash of its previous victims.

### 4.10 Resting, carrying out, retreat and injuries far from help
- **Camping below:** needs `Air >= 600` at the tile, no water above 30 cm and (for a fire) ventilation; a fire in a sealed chamber reduces `Air` by 100 per game hour. Rest rules (Phase 3 camp): sleep recovery as above ground; darkness risk: sentries still needed (inhabitants wander).
- **Carry-out vs speed:** capacity `15 + 2.5 x strength` kg (existing); over capacity: speed down and stamina cost up (existing); a party's loot weight sets the **time to the surface**: `T_surface = path_length / (speed x (1 - over_fraction))` shown in the map tooltip ("42 m to the stairs, 9 minutes at your load").
- **Ropes and ladders:** a 10 m rope item lets the party descend a 3 m shaft without a ladder (`Athletics` check, falls on failure per resolver 4.9); **ladders can be pulled up** by the last climber (`Portal.Enabled = false`); a ladder cannot be pulled up while someone is climbing it.
- **Injuries far from a surgeon** (done-when): the wounds spec's windows start when the wound is made: **tendon repair within 1 game day, set a fracture within 1 game day, bandage within 30 sim-seconds, infection 8% per game day undressed (x1.5 underground)**. Getting back up from three levels is minutes (hundreds of metres at 3 m/s, 100 to 400 sim-s); the real cost is the **trip to a surgeon**: a barrow or mine 20 km from the nearest town is about 0.9 game day at a trot (5 m/s: 4,000 s, 67 real minutes at 1x) and 1.5 game days on foot (3 m/s), against the tendon and fracture windows of **1 game day**. So a severed tendon deep in a crypt far from a town forces a choice: ride at once and make it, or lose the limb function for good. The HUD shows `Surgeon: nearest town X, 0.9 game day by horse, 1.5 on foot` for any hero with a wound whose window is open.

## 5. Tick placement (20 Hz live versus abstract)
- Live 20 Hz: torches burn (1 sim-s = 20 ticks), light grid refresh per moving light, auto-map pass, trap proximity checks (only armed traps within 6 m of a person), monster brains (5 Hz when no target, 20 Hz when hunting), air exposure (counted per sim-s). Hazard rolls once per 60 sim-s windows and at the game hour boundary for floods.
- Abstract tier (hourly): inhabitants of unvisited sites are not simulated; scavenging (yearly), reoccupation (daily after 20 days), flood events (daily), history decay of loot (yearly); sites outside the bubble keep their `SiteState` unchanged. A site never ticks while nobody is within 150 m (bubble rule); hazards fire only when someone is present.
- At first visit generation is time-sliced; an ungenerated site costs nothing.

## 6. Determinism
Streams (0x400 block; keys time-independent or game-time based): **0x4B0** `SiteLayout` (site id, level), **0x4B1** `SiteLoot` (site id, room), **0x4B2** `SiteInhabitants` (site id, serial), **0x4B3** `HazardEvent` (game hour, site, draw = hazard id), **0x4B4** `TrapTrigger` (trap id, entity, `GameSec/20`), **0x4B5** `TrapDetect` (`GameSec/20`, person, trap), **0x4B6** `MonsterBehaviour` (`GameSec/20`, monster), **0x4B7** `CaveInDebris` (event id), **0x4B8** `Reoccupation` (game day, site), **0x4B9** `Scavenge` (search action serial, person). Generation is a pure function of `(world seed, site id, level, template content hash)`: two machines generate identical sites (EX-1). Torch burn is a counter of sim-seconds, light and map are integer (no float in sim). `SiteHash` (site states) joins the daily trail.

## 7. UI and controls
- Keys (all existing): **V** torch on or off; **PgUp/PgDn** (or Shift + wheel) look up and down; **M** map (Underground tab); **Z** sneak (halves speed; helps detection and sleeping beasts); **E** default action (search, open, disarm when a trap is marked); **Q** everything (Mark, Search, Disarm, Dig, Climb, Pull up ladder, Retrace steps, Drag out); **X** combat stance; **T** called shot. No new keys.
- HUD: **Light meter** (torch time left as a bar and "11 min"), **Depth indicator** (`LevelIndicator`: "Level -2"), **Air warning** (a flame icon with a caption when the candle gutters or air < 600), **Lost** indicator, **Carry and exit time**, **Surgeon distance**. The map shows traps once detected, collapsed cells, water.
- Combat in the dark: a vignette shows the vision radius (accessibility: reduced setting available); nameplates show only for seen persons.
- Settings: "Show trap hints" (off), "Auto-pause on hazards" (on: air warning, cave-in creak), "Torch time warnings" (60 s and 20 s).

## 8. Tests (golden)
| Id | Check |
|---|---|
| EX-1 | **Determinism:** generating the same site twice (and on both OS) gives identical `SiteHash`; different sites differ; level generation time-sliced to <= 4 ms per frame |
| EX-2 | **Persistence:** after looting, opening a door, a collapse, a pulled ladder and a save and load, the deltas are identical; a regenerated chunk keeps them |
| EX-3 | **Blueprints:** all underground room templates pass CI (IDs resolve, rooms reachable, purposes satisfiable); every generated level is fully connected from the entrance (1,000 seeded sites) |
| EX-4 | **Loot from history:** an abbey ruin (pop 120, sacked, 60 years) has a loot budget of 1,080 +- 5% mites; a battlefield after 40 years has 680 permille remaining; barrow `Dead` > 0 has a mass grave; hideout stash equals the band's recorded robberies |
| EX-5 | Torch burns 1,200 sim-s (23.98 +- 0.02 minutes at 20 Hz), the same at 1x and 8x; lantern 3,600 per 0.3 l; heavy rain outdoors x2.5 |
| EX-6 | Vision: `VisionRadius`: light 235 -> 36 m, 100 -> 16 m, moonlight 40 -> 7 m, 0 -> 1 m; a dark target is seen only inside 5.6 m |
| EX-7 | Air: index 750 -> candle gutters (radius x0.7), 500 -> torch x0.5 and regen x0.6, 300 -> dazed and speed x0.7, 200 -> unconscious in 20 sim-s, dead at 120 |
| EX-8 | Flooding: after heavy-rain days the flood event occurs at 4% per day (10,000 seeds +-0.5), rises 30 cm per game hour for 6 hours; swimming in 18 kg of gear drowns an unskilled swimmer in 35 sim-s +-5 |
| EX-9 | Cave-in: a 1,000 pm zone collapses at 5% per minute with a person present (10,000 trials +-0.5%); warning 6 sim-s; rubble blow M 150, dodge 80% |
| EX-10 | Traps: detection at Perception 50 vs difficulty 40 = `5 + 5 + 10 = 20%` per sim-s in torchlight and the 2% floor unlit (`5 + 5 - 15 = -5`, clamped); disarm success at Lockpicking 50 vs 40 = 75%; spring spears vs a gambeson wearer wounds at the chest with the resolver result |
| EX-11 | Auto-map: the map equals the union of seen cells; two maps merge when parties are within 20 m; save size < 1 KB per level; Retrace steps returns to the entrance by the walked path |
| EX-12 | **Crypt crawl by torchlight** (seeded integration test): a 3-level abbey crypt with a trap and a draugr-free guard; the run produces an injury (a severed tendon or a broken leg) with the nearest surgeon 1.2 game days away on foot (0.7 by horse), forcing the ride-or-lose decision; the HUD "surgeon distance" is correct and a tendon repair after 1 game day fails; the scenario passes with a fixed seed |
| EX-13 | Draugr: wakes on grave-good removal (10% per sim-s afterwards), stops pursuing at 100 m from the barrow, returns to the bier; never routs; fire kills it in 30 +- 5 sim-s |
| EX-14 | Cave beast: asleep 16 of 24 hours, wakes to a running party at 25 m, flees at 30% blood, ignores a sneaking party beyond 8 m (10,000 seeds) |
| EX-15 | Reoccupation: a cleared den is reoccupied at 2% per day after day 20 under pressure 500; chronicle line exists; the new band is distinct persons |
| EX-16 | Weather in combat: heavy rain longbow sigma x1.30; fog beyond 15 m x1.4; heat 10 kg armour regen x0.85; snow cavalry x0.6; table values asserted from the content file |
| EX-17 | Light modifiers: night attack at 40 light is -15 points: hit chance 22.0% -> 7.0% for a head target at equal skill (22 x (0.5 - 0.15)/0.5) |
| EX-18 | Bad air kills nobody who leaves within 100 sim-s of the warning (walking speed 3 m/s covers 300 m); gutter warning precedes unconsciousness by >= 30 sim-s in all generated mines |

## 9. Open questions (recommended defaults)
| # | Question | Default |
|---|---|---|
| E1 | Draugr count and difficulty | 1-3 per lair; a skill-50 man-at-arms in mail loses to 2 draugr in 60% of duels, which is intended: torches, a party and retreat are the answer |
| E2 | How many hazards per site | 0-2 by template chance, always preceded by a readable warning (candle, creak, drip); never a hidden instant death |
| E3 | Firedamp explosions | Not in Phase 4 (blackdamp only); revisit with coal mining in Phase 5 |
| E4 | Lightning strikes | Flavour only (no damage) |
| E5 | Torch length | 1,200 sim-s as the plan states; tune with testers |
| E6 | Reoccupation rate | 2% per day after day 20 under pressure 500; keeps content fresh without refilling every cave |
| E7 | Grave robbing consequences before the crime system exists | A `TabooRobbed` event, rumours and a curse flag; formal law in Phase 6 |
| E8 | Mounts underground | Forbidden (portals refuse horses; a horse left at the mouth must be hitched) |
| E9 | Do we allow digging new tunnels? | Yes through rubble and loose earth with `digging` quality; not through solid rock (a wall's hardness vs tool quality, plan §2.10; 1 tile per 400 sim-s with a pick in soft rock) |

## 10. Budgets
| Item | Budget |
|---|---|
| Site generation | <= 30 ms compute per level, <= 4 ms per frame (time-sliced); 3 levels <= 100 ms total |
| Lighting underground | <= 2 ms per frame (24 lights) |
| Auto-map update | <= 0.1 ms per tick |
| Hazard and trap checks | <= 0.1 ms per tick (proximity + 1 Hz air) |
| Monsters | <= 0.2 ms per tick for a lair of 3 |
| Memory | <= 300 KB per generated site (up to 4 levels), <= 5 MB for the 5 most recent in memory |
| Save | `SiteState` delta <= 4 KB per site plus 1 KB map per level; 40 visited sites <= 300 KB |
| Content | `sites/*.jsonc` <= 150 KB; 30 room templates <= 120 KB; loot tables <= 30 KB |
