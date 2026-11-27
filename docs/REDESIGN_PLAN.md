# Falling Leaves: Ravensguard — Full Redesign Plan (revision 3)

## Context
The game today is a deep simulation hidden behind a hard-to-use interface: BBCode text in monospace labels, a 3,024-line `Main.gd`, 21 screen modes, and conflicting single-letter keys. Much of it is broken:
- The New Game flow can't start.
- M, I, F, H and Tab do nothing, because the overworld controller compares an enum to strings.
- Esc is swallowed everywhere.
- `GameState.is_walkable`, `erase_army` and `track_war_event` were deleted in commit `d1bde77` but are still called (e.g. `CombatManager.gd:62,75`, `AIManager.gd:37`).
- The main map renderer draws mostly nothing.
- Saves can't serialize the game objects.

**Goal:** keep the core idea (anatomy combat, a physical economy, factions, generated worlds) and rebuild it as a fun, readable, fully interactive open-world sandbox in the spirit of Kenshi and Daggerfall:
- One continuous walkable world where everyone is a real person.
- Buy plots, build freely, run businesses, claim land, rise from wanderer to lord or your own faction, and lead armies in battle.
- Rewritten in C# on Godot .NET, PC-first, moddable from day one.

**Revision 2** follows an adversarial review of revision 1. It fixes:
- a self-contradictory clock and world scale;
- phases that depended on later phases;
- combat detail tiers that broke their own promise;
- saves that would break when content changed;
- determinism that conflicted with threading;
- colliding controls;
- missing designs for animation, failure, progression, the money supply, dialogue, travel, audio and management at scale.

**Revision 3** follows a coherence review after many incremental additions. It also folds in what the Dwarf Fortress wiki (material science, materials, clothing, anatomy) and Cataclysm's crafting and proficiency docs say. It makes §5 the single source of truth for scheduling, unifies the combat and armour model in §2.6, restores the armies section (§3d) lost in revision 2, and adds horses and mounted combat, movement between levels, save handling, performance budgets, onboarding, settings and accessibility, tooling and tester crash reports. The timeline is now about 6–8 years.

**Visual references:** the "Ravensguard Tileset" artifact is the current look (style sheet v1). The earlier "Ravensguard Concept Book" and "Ravensguard Look Options" artifacts are historical; the concept book's first-hour walkthrough still describes the intended play, but its visuals are superseded.

## Locked decisions (from the user)
| Area | Decision |
|---|---|
| Engine / language | Godot 4.7.x .NET (C#). PC first: Windows and Linux, Steam Deck later. No web build (Godot can't export C# to web as of 4.7.2) |
| Look | **Top-down pixel art in the style of CDDA's UltiCa tileset:** 32×32 terrain tiles (1 tile = 1 m), 32×64 layered characters drawn from the front, back and side (the fourth direction is the side mirrored), 16×16 items, all original medieval art **drawn by Claude** (no UltiCa assets reused). **Dark nights and underground lit only by torches and fire.** UI in a matching pixel style. Title: **Falling Leaves: Ravensguard** |
| World | **Randomly generated, Dwarf-Fortress style:** a whole continent (~512 km) with plates, climate, realms and cultures, then **100–250 years of simulated history**, then you **pick where to start** (embark). **The whole continent is one seamless walkable world:** your starting region (32 km, about 25,000 people) is built in full detail, and neighbouring regions are generated in the background as you approach, so you can walk, ride or sail from one end of the continent to the other without a loading screen (§1B). Regions you haven't reached keep running abstractly. Worlds are kept in a library and can be replayed with new characters. Battles happen in place |
| Time | Real time with pause. **Movement and combat run in real seconds; the world clock runs at 1:20** (a game day lasts 72 real minutes at 1x) |
| Progression | Staged: wanderer → sellsword → householder → vassal → lord → founder. UI layers unlock when relevant |
| Control | Direct control of your hero; squad and formation orders; mouse and keyboard equal, rebindable, always shown |
| Combat | Anatomy, called shots on a paper-doll (pick the body part and the attack type, as in Dwarf Fortress), juicy feedback, a filterable log of flavour text, slow motion on key moments |
| AI play | **Watch mode** hands a fight to the AI in real time; **autopilot** runs your hero's life on directives with interrupts. Both are allowed in Ironman (§3i) |
| Army scale | Up to 1,000–2,000 soldiers live (a setting), with reinforcement waves; formations + hero; physical marching, supply, levies from your land, sieges with engines |
| Sandbox | Buy lots and houses, claim wild land, zone fiefs, free-placement building, businesses (hands-on and passive shares) |
| NPCs | Everyone is a person; anyone can be recruited; crime with witnesses; factions react; you can found a faction |
| Direction | Ambitions, chronicle with "why" links, authored landmarks and characters |
| Difficulty | Brutal by design, fair through legibility |
| Fantasy | **Mostly grounded, with rare monsters.** A few real horrors live deep down (old barrows, deep caves, a few haunted ruins). They are rare, deadly and legendary. Everywhere else, the dangers are real-world ones, and superstition still shapes how people behave |
| Exploration | Dungeons and interiors are **underground layers in the same continuous world**, with no loading screen. Priorities: ruins and crypts, caves and mines, heists and jailbreaks, barrows and battlefields (all four) |
| Failure | The same defeat outcomes in both modes. **Ironman death: you continue as a companion or heir**; the run ends only when nobody is left |
| Modding | Data-driven with a mod folder from day one |
| Architecture | JSON for all content and tuning, plus a small typed condition and effect vocabulary; rules in tested C#. Hybrid runtime: ECS for the live tier, ID-keyed tables for records, items as a table with optional parts (§2.1a) |
| Horses | Riding and carts for travel, plus mounted combat and cavalry with full horse anatomy (§3e) |
| Vehicles | A Cataclysm-style **vehicle designer** for carts, wagons, chariots (open to every culture), sledges, siege engines and war wagons, river boats and **sea ships with naval combat**; one part-grid system (§3h) |
| Weather | Seasons, day length and live weather from continental fronts, with effects on travel, combat, farming, the economy and war (§3c) |
| Timeline | Everything above stays in 1.0; the plan is estimated at **about 6.5–9 years** (§5) |
| Sequencing | **Breadth first**, every phase playable |
| Release | **No public builds until 1.0** |
| Difficulty | One intended brutal game, plus a few **honest named sliders** shown openly in the world settings: wound lethality, bandit frequency, economy harshness, plague severity, and Ironman on or off. No hidden rubber-banding (Nov 2026) |
| Slavery | **Fully systemic, like Kenshi** (Nov 2026). Slavers and slave markets exist where the culture's law allows them, and you can be enslaved and escape. The player can buy, sell, own or free slaves, with heavy consequences for reputation, faith and law (Phase 6) |
| Plague | **Rare but devastating** (Nov 2026). Everyday coughs, fevers and flux, plus rare great plagues. A great plague can kill a third of a town, spreads along trade roads, empties villages and reshapes the economy: labour gets dear and land gets cheap (Phase 6 to 8 events; the death rates are in §4A.2) |
| Hero lifespan | **A real lifespan on the slow clock** (Nov 2026). The hero ages through a long campaign, strength fades in later years, and old age can kill, which hands play to the heir you raised (family life, Phase 6) |
| Family, justice, faith, endings | Full family life for the hero, historically harsh justice, a real faith system (all Phase 6), and optional endings (Phase 8), all decided Nov 2026 |
| Building | Hands-on, Kenshi style, with both free placement and ready-made house plans (Phase 5, Nov 2026) |
| Armies on the map | **Hybrid** (Nov 2026). Every lord keeps a small standing warband that roams all year, Mount & Blade style: patrols, escorts, visits to other lords, chasing bandits, and border raids in war. For campaigns, lords muster their levies into large seasonal hosts that march with supply, forage, and go home for the harvest and winter quarters (Phase 7) |
| Soldiers' equipment and growth | Decided Nov 2026, built in Phase 7:
- **Equipment.** Loadout templates per troop type, filled from your armoury with real items (best available, gaps shown). Any single soldier can be opened and re-equipped piece by piece, Dwarf Fortress style.
- **Growth.** No XP levels: soldiers gain weapon skills by fighting and drilling, and are promoted into troop roles (levy → spearman → man-at-arms → knight), each with a loadout, a wage and a skill requirement.
- **Who arms them.** A historical mix: levies bring gear by their wealth; retinues and mercenaries are armed from their lord's armoury.
- **Arms industry and army supply (Phase 7).** Armouries stockpiling in peace; munition-grade mass production in large workshops with many hands; the arms trade through the continent; loot recycled through repair and refit; army food from granaries, food rents, purveyance, merchants following the army and foraging. |
| Industries | Decided Nov 2026, built in Phase 7 (from the user's iron, clay and wood design sheets):
- **Staged chains of real buildings.** Each stage is its own building with its own crew: for example mine → crusher and washer → ore yard → smeltery → packing, or clay pit → yard → moulding shop → drying sheds → kiln → packing.
- **An Industry tab in the Ledger** shows each chain as a flow: throughput per stage, the bottleneck, crews, wages and the next upgrade.
- **Tier upgrades (Level 1–3) are physical, one-click plans.** A builder is hired to install them hands-on from real materials, and the level is recognised from what is actually installed. Each tier sets a minimum and maximum crew.
- **Medieval technology.** Wheelbarrows and plank runways, then mine carts on wooden rails, then horse gins and water wheels driving pumps, stamps, bellows and trip-hammers. Treadwheel cranes take the place of conveyors and cranes, and a master machine-wright takes the place of a mechanic.
- **Historical output, plus an honest slider.** Workers produce at historical rates; works grow through big crews and machines. An "industry output" slider (1x–5x) joins the honest world-settings sliders for players who want a faster tycoon game.
- **Real inputs.** Smelting burns charcoal from charcoal burners (its own chain, which eats woodland), kilns burn firewood, and packing uses barrels and crates from coopers. A shortage stops the stage and the Industry tab says why. No flat "supplies" cost.
- **Payback.** A well-run Level 1 works repays its buildings in about 2–3 game years at default settings.
- **Money becomes gold, silver and copper:** 1 gold = 10 silver = 100 copper. The silver piece is today's crown; wages stay in silver. Mints strike all three from mined gold, silver and copper.
- **Phase 7 spec decisions (21 Nov 2026):** tiers grow by historical crews plus machines (crew bands rise with the tier; yard and packing crews from L2); raw goods are priced from their labour (3-8x cheaper); levies serve 10 unpaid days a year; purveyance is historical (grain above 30 days' need at 3/4 price by tally); war's darkness as specified (sacks kill 1-5% and burn, prisoner killing possible and an atrocity for the hero); raider cultures raid in peace; F1-F4 select formations; the overview's cut order; children from 7 help with the harvest (a third of a day at reaping, binding and hay), so the levy's cost to the harvest is measured with them counted. Details in `docs/specs/phase7/00-phase7-overview.md` §10a. |

---

## 1. The scale model (everything else derives from this)
All values are constants in `content/base/world.json`, so they are tunable and moddable.

| Quantity | Value | What it implies |
|---|---|---|
| Continent | ~512 × 512 km on a coarse grid of 2 km cells (256 × 256) | Generated once per world: plates, climate, rivers, biomes, minerals, cultures, realms, then history |
| Region (unit of detail) | 32 × 32 km, 16 × 16 continental cells; the start region is picked on the continent map | Built at full detail from the continent data plus its history. **Neighbouring regions are generated seamlessly as you approach** (§1B); the playable world is the whole continent |
| Cell | 1 m; 32 px at street zoom | |
| Chunk | 32 × 32 cells | 1,000 × 1,000 chunks in the region; generated on first visit |
| Region tile | 64 m (≈ 1 acre) | 500 × 500 tiles in the region |
| Region area | 32 × 32 km = 1,024 km² | Crossing it takes ~3 real hours jogging at 1x, ~22 min at 8x, ~2.5 game days |
| Clock | 1 real s = 20 game s | Game hour = 3 real min; game day = 72 real min at 1x, 18 at 4x |
| Year | 4 seasons × 15 days = 60 days | Year ≈ 72 real hours at 1x |
| Speeds (real m/s) | walk 1.5 · jog 3 (travel default) · sprint 5 (stamina) · cart 2.5 · army column 1.3 · horse trot 5 · gallop 12 (stamina) | |
| Town spacing | 6–10 km | 8 km ≈ 44 real min jogging at 1x (5.5 at 8x), ≈ 15 game hours: food and wages matter on every trip |
| Food | 1 measure = a day's grain for one person (≈ 1.1 kg); a sown acre yields ~48 measures per harvest | ~2.3 arable acres per person; ~58,000 acres of farmland in the region (§3a) |
| Population | ~25,000 | 3–4 towns of 1,000–3,000; 40–80 villages of 80–300; farmsteads; camps |
| Realms | The region holds parts of 1–3 continental realms, with borders from history | The region alone levies ~520 men in peace (1 per 10 of ~5,200 households) and ~1,040 in war (1 per 5). Field armies of 1,000–2,000 combine levies, retinues and mercenaries from across a realm and arrive by road from outside the region |
| Combat time | Real seconds | A 5-minute fight ≈ 1.7 game hours. Bleeding and healing are specified in sim-seconds; needs and wages in game-hours. Every rate in the content schema carries an explicit unit |

**Speeds:** pause, 1x, 2x, 4x, and 8x.
- 8x only works when no hostiles are in view.
- At 8x the live bubble shrinks to the party and the road ahead, and the abstract tick coarsens. Live ticks are **not** multiplied.

---

## 1A. World generation: continent → history → embark region
Every world is random from a seed, like Dwarf Fortress. Hand-made content (landmarks, unique characters) is only ever *placed into* generated worlds. Generation runs in three stages.

**Stage 1: the continent** (coarse, 2 km cells). This ports the ideas of the old pipeline in `src/utils/worldgen/*Phase.gd` and fixes its bugs (see §4A.4).
- **Plates and terrain.**
  - Plates (default 12–15; the old `num_plates`) with convergent uplift.
  - Layouts: Pangea, Continents, Archipelago (the old `layout`).
  - One sea level, used by every phase.
- **Climate and water.**
  - Latitude temperature bands and west-to-east wind with rain shadow (the old ClimatePhase idea).
  - Priority-flood depression filling, then integer D8 flow for rivers and lakes.
  - Biomes from temperature and rain.
  - Soils, plus mineral veins as their own layer, from strata.
- **People.**
  - 3–8 **cultures**, each with its own name pools, building style and law customs.
  - Initial **realms**, seeded on good land.
  - Population is kept as numbers per cell, plus settlement points.
- All maths is fixed-point (Q16.16), so the same seed gives the same continent on every machine.

**Stage 2: history**, 100–250 years (a setting), simulated per season at continental scale.
- **Settlements and population.**
  - Population grows toward each cell's carrying capacity.
  - Settlements are founded, grow, shrink and are abandoned.
  - Trade routes form between them.
- **Realms.**
  - Realms make war and peace with the same utility rules as the live game: claims, casus belli, strength, harvest.
  - Wars produce battles and sieges.
  - Realms also split, merge and fall.
- **Disasters.** Famines, plagues, fires, floods and mine collapses.
- **Notable people** are generated as records with lifespans: rulers and heirs, generals, rebels, master craftsmen, saints and outlaws. They marry, inherit, betray and die.
- **Named artefacts** (a famous sword, a reliquary) are created, change hands, get lost or buried.
- **Output is the world's legends.** Every event is a typed chronicle record with causes. It becomes:
  - the opening chronicle and the Codex legends;
  - **ruins that exist because something happened there** (sacked in 287, abandoned after the plague of 301);
  - grudges and claims that seed present-day diplomacy;
  - family lines that the region's people descend from.
- Budget: **≤ 60 s** on the reference PC. The loading screen shows the history unfolding as a scrolling chronicle.

**Stage 3: embark**, choosing and building your region.
- **Choosing.** The continent map has overlays for realm, culture, biome, wealth, danger (savagery), rivers, towns and legends ("the field of the Battle of Red Ford, 266"). Pick a 32 km square.
- **Building.** The region is generated at full detail from that square's continental data. Heights are refined from the coarse cells with detail noise and stay consistent at the edges.
  - The settlements history placed there become the towns. Villages and farmsteads fill in, with fields and parcels.
  - All ~25,000 Persons are created. Their families descend from the history's notables and common folk.
  - Ruins, hoards and artefacts are placed where history left them.
  - Realms, fiefs and lords take their present-day state from history.
- **Quick Start** picks a suitable region automatically.

**The rest of the continent keeps going** as a seasonal abstract tick: realms, wars, trade, migration, plagues. It reaches your region through its border roads:
- caravans and merchants;
- envoys and contracts;
- invading armies;
- refugees from a famine or war elsewhere;
- news and rumours.

**Settings** ("Advanced" on the start screen): seed, continent layout, plates, number of cultures and realms, history length, mineral density, savagery, moisture, temperature.

**World library.** A world (its continent, history and generated regions) is saved once and can be played many times:
- a new character can start in a world you've played before;
- they can find your old hero's grave, smithy and enemies;
- the chronicle carries on.

**Beyond the start region:** see §1B. Travel across the continent is seamless.

---

## 1B. A seamless continent
You can walk, ride or sail from one end of the continent to the other without stopping. There is no journey screen and no loading screen at region borders.
- **Regions are the unit of detail, not of play.** The continent is a grid of 16 × 16 regions (each 32 km, 16 × 16 continental cells). Any region can be built at full detail on demand from the continent data plus history, with the same generator as the start region.
- **Generated ahead of you.** When the hero (or any active squad, army or ship) comes within about 4 km of a region border, the neighbouring region is generated on a worker thread. That covers its terrain, settlements, people, sites and landmarks, which takes about 2 s. By the time you reach the border, chunks on both sides stream like any others. Diagonal corners queue both neighbours.
- **Borders join up.** Heights already match across edges (a Phase 1 test). Rivers come from the continent's flow network, so they continue across borders. **Roads must also cross borders:** border gate positions are derived from the continental road network between settlements, never chosen per region, so a road leaving one region arrives where the next region expects it. Realm and fief borders come from history, so they're independent of region edges.
- **Records are keyed by region.** Every record (person, household, field, settlement, business, chunk) carries its region id. People move between regions as records, so migrants, caravans, armies and the hero can walk across borders.
- **Three levels of detail.**
  - **Live bubble:** around you and active squads (§2.3).
  - **Detailed regions:** every region that has been generated keeps full records and runs the hourly abstract tick while it is near the player (within one region). Regions further away fall back to a cheaper daily tick over the same records.
  - **Abstract continent:** regions that have never been visited stay as continental numbers in the seasonal tick (§1A), until you come near.
- **Persistence.** Each visited region is saved in its own save section and is loaded and unloaded independently. Only the region you're in and its neighbours stay in memory; others are paged to disk.
- **Budgets.** About 1 MB per detailed region in the save (people, lots, fields) plus visited chunks (§2.11). At most 9 detailed regions in memory (the current one and its 8 neighbours), about 300 MB. Generating a neighbour region takes ≤ 3 s on a worker thread, with no frame hitch.
- **What it changes elsewhere.** The world map shows the whole continent, with detail where generated. Armies and caravans in the continent tick can cross into detailed regions and appear on the road through the LODBridge. Weather fronts already cover the continent. Sea travel (§3h) uses the same rule along coasts.
- **Determinism.** A region's content depends only on the seed, content and history, never on the order in which regions are visited. A test generates regions in different orders and compares hashes, and CI compares them across Windows and Linux.

## 2. Technical architecture

### 2.1 Solution layout
```
Ravensguard.sln
  src/Ravensguard.Sim/        pure .NET 8+ library, NO Godot dependency: rules, world, people, economy,
                              combat, property, society, chronicle, save. Headless and deterministic.
  src/Ravensguard.Content/    content schemas, loader/validator, mod resolution and conflict report
  src/Ravensguard.Game/       Godot .NET: rendering, animation, input, UI, audio. Reads sim snapshots, listens to events
  tests/Ravensguard.Sim.Tests/   xUnit, `dotnet test`
  tools/Ravensguard.Bench/       BenchmarkDotNet + allocation/op-count gates
  tools/perf/                 frame-time and tick-time capture on the reference PC; results logged to docs/perf/
  tools/debug/                dev console, sim inspector, legends/history viewer, economy dashboard (also built into dev builds)
  art/src/                    sprite definitions: indexed pixel grids + procedural helpers, one palette
  tools/art/                  sprite baker, palette validator, autotile generator, rig frame baker
  content/base/               the base game as a mod (JSON + schemas)
  mods/                       user mods
  legacy/                     the old GDScript project, read-only design notes
```

### 2.1a Architecture rules: JSON for content, hybrid ECS for runtime
- **JSON holds content and tuning; C# holds rules.**
  - **Content and tuning in JSON (§2.9):** every definition and every tunable number. That covers materials, items, body plans, creatures, recipes, proficiencies, blueprints, cultures, weather tables, dialogue templates, site templates, AI scoring weights and the scale constants.
  - **A small, typed condition and effect vocabulary** for content to refer to, for example `has_proficiency`, `near_fire`, `season_is`, `add_rumour`, `start_contract`, `give_item`. It is validated against schemas at load and kept deliberately small. It is **not** a scripting language; Cataclysm's effect_on_condition is the cautionary example.
  - **Rules in C#, with tests:** the combat resolver, economy, pathfinding, AI evaluation, weather, history. Complex modded behaviour waits for C# script mods after 1.0.
- **Runtime data is data-oriented, with ECS only where it pays off:**
  - **The live tier uses ECS (Arch, or plain struct arrays if the Phase 0 crowd spike says so).**
    - Components include position, velocity, pose, intent, path, formation slot, combat state, projectile, light source, burning, and the rendering frame and palette row.
    - Systems run in the 20 Hz fixed step and **always iterate in stable-ID order**, never archetype order (§2.4).
  - **Canonical records live in typed tables keyed by stable IDs:** people, households, kin links, fields, parcels, deeds, settlements, factions, contracts, businesses, the chronicle.
    - They are relational (kin, owner, employer, lord) and update hourly.
    - They save and query simply.
    - They are what the abstract tier and the history simulation work on.
  - **Item instances** are a table where each item has optional parts: container contents, condition and wear, temperature or burning, fit, stains, components. That gives ECS-style composition without pushing millions of items through the ECS.
  - **The LODBridge** (§2.3) is the only path between records and the live ECS. A person is always a record; live components are a cache attached while they're in a bubble, never a second copy.
  - **Godot nodes** only render and run UI, reading immutable snapshots (§2.4). No game state lives in nodes.

### 2.2 World and persistence
- **Generation:**
  - Deterministic from the seed using **our own deterministic maths**: integer or fixed-point noise and hashing, with no `Math.Sin`/`Pow` in generation, because those differ between Windows and Linux.
  - The determinism claim is scoped to "same build, same content"; CI hashes worlds on both OSes.
- **People exist from world generation.** All ~25,000 `Person` records (~1–2 KB each) are created when the world is made. The abstract tier acts on these real records from day 0, so there is no lazy household generation and nothing gets regenerated in its day-0 state.
- **Chunks are snapshotted on first visit.** Generated content stays reproducible only until the player sees it:
  - On first visit, a chunk's generated content is stored in the save, compressed (~2–6 KB each; a long game touches a few thousand chunks, so about 10–30 MB).
  - Later changes are `ChunkDelta`s **keyed by stable object IDs**, not by "what the generator would have put here".
  - Each chunk also stores the generator version and content hash.
  - **Result:** generator fixes, content patches and mods never silently change places the player has already been.
- **Corpses and debris decay on a schedule.** Bodies become skeletons and then disappear over about a game week; spent arrows are collected or decay. This keeps `ChunkDelta` growth bounded, at well under 1 MB per large battle after decay.
- **Saves:** MessagePack with a versioned schema, `SaveMigrator` steps, and a content and mod manifest. Save handling, autosaves and budgets are in §2.11.

### 2.2a Vertical levels: multi-storey buildings and underground
- **Each chunk has stacked levels:** z = −5…−1 underground (cellars, crypts, caves, mines; §3b), z = 0 the ground, and **z = +1…+3 above**. Upper levels hold upper storeys, lofts, galleries, tower floors and wall-walks.
- **Typical heights:**
  - cottages: 1 storey plus a loft;
  - townhouses and inns: 2–3 storeys;
  - keeps and church towers: up to +3;
  - town and castle walls: a walk at +1, with towers to +2/+3.
- Blueprints define each storey as an extra `levels` entry (§2.9). The build mode lets you place floors, stairs, ladders and trapdoors on any level.
- **Showing levels top-down, as in Cataclysm:**
  - The camera shows the level your selected person is on. Levels above are cut away.
  - The level below shows through stairwells, holes and over the edge of walls and roofs. It is drawn one shade darker, so you can see out of a tower window or down from a wall into the street.
  - A level switcher (PgUp/PgDn, or the wheel with Shift held) lets you look up or down without moving anyone.
- **What height changes in play:**
  - Height gives archers more range and a better chance to hit, and lets them shoot over crowds.
  - Line of sight between levels exists only through windows, edges and openings.
  - Falls cause damage through the anatomy resolver. Jumping from a window is a real escape, with a real cost.
  - Fire and smoke rise.
  - Ladders can be pulled up.
  - Sieges use wall-walks, towers and siege towers that reach the wall-walk level.
- **Structural support** uses a simple rule rather than a physics simulation. An upper floor tile needs a wall, beam or post under it within 3 tiles. Destroy the supports in a fire, siege or demolition and the floor above collapses, dropping whatever is on it.
- **Moving between levels (pathfinding and AI):**
  - Stairs, ladders, trapdoors, shafts and cave mouths are **portals** in the HPA* graph, so a path can run from a cellar to a tower top.
  - Each level has its own flow fields for crowds. Formations on a wall-walk use the wall-walk level only.
  - Pulling up a ladder, barring a trapdoor or a floor collapsing invalidates just the affected portals and re-plans.
  - NPCs follow you up and down, and pursuers search the levels where they last saw you.
  - The live bubble measures horizontal distance only, so everyone in a building above or below you stays live.
- **Performance:** only levels that contain something are stored or simulated. Most chunks are just z = 0.

### 2.3 Simulation tiers
- **One canonical `Person` store.** Live-bubble data (position, pose, path, current action) is a component cache attached to the same record, never a second copy.
- **The live bubble:**
  - A radius of about 150 m around the player and each active squad.
  - **Hysteresis:** promote at 150 m, demote at 200 m.
  - At most 3 bubbles.
  - Promotion is **spread over frames**: at most ~50 people per frame, entering at the bubble edge, so an arriving army appears as a column marching into view.
  - Carried items, jobs and paths survive demotion as abstract job state.
- **The LODBridge** is the component that promotes abstract records into live entities and demotes them back. Entering the bubble gives a person or group a position, path and current action derived from its abstract job. Leaving it folds the live results back: casualties, items moved, work done, coin spent.
- **The battle bubble.** When a battle starts, the live area grows to the engagement's bounding box plus a 150 m margin, up to about 1 km². It has its own performance budget (§2.11). Ambient simulation in neighbouring chunks pauses until the battle ends, and normal bubbles resume afterwards.
- **The abstract tier** runs every game hour, plus an abstract battle resolver for fights outside any bubble. Those battles are **labelled as reports** in the chronicle ("word reached Oakford…"), with casualties drawn from a statistical wound table.

### 2.4 Determinism and threading
- **Random numbers** come from a **counter-based RNG** keyed by `(seed, tick, entityId, stream)`, so results don't depend on thread order.
- Worker jobs (pathfinding, chunk generation, the abstract tick) **post results to a queue that is applied at tick barriers** in stable-ID order.
- All iteration happens in stable-ID order, never in dictionary or ECS archetype order.
- The UI reads **immutable snapshots** published once per tick.

### 2.5 Live simulation
- **Fixed step:** 20 Hz sim; rendering interpolates between steps.
- **Entity storage:** see §2.1a (live tier in Arch ECS or struct arrays, decided by the Phase 0 crowd spike; records in ID-keyed tables).
- **AI:** utility AI for needs and schedules.
- **One job and reservation system** for NPCs, your people and you: haul, build, craft, farm, guard, tend shop, escort.
- **Pathfinding:**
  - HPA* over chunks with **incremental invalidation**: a wall, door or breach re-plans only that chunk's portal graph. Ungenerated chunks use the tile-level road and terrain graph.
  - Flow fields for crowds and formations.

### 2.6 Combat model: one resolver for every blow (the single spec)
Every strike goes through one resolver: melee, missiles, thrown objects, falls, animal attacks and monsters. §2.10 (materials) and §4A.3 (timings, bleeding, morale) refer here and don't restate it.
1. **Inputs.**
   - The attacker: strength, skill, stamina.
   - The weapon's shape and materials (§2.10): mass from **real volume × density** (a steel sword blade is about 0.18 L, so about 1.4 kg), contact area, edge, penetration.
   - The attack type: edge, point or blunt.
   - The target zone, the target's body plan, and the target's worn layers.
2. **Hit.** Hit chance is computed **per targeted zone, before** any redirect:
   - P(hit) = zone factor × (0.5 + Δskill/200), where zone factor = 0.2 + 0.8 × (zone size ÷ largest zone). The 0.2 floor keeps aimed blows at small parts (head 5.4%, hand 1.4%) possible, but still much harder than the body;
   - ×0.6 against a zone the defender is guarding;
   - ×0.35 against armour gaps;
   - flank attacks get +25%, rear attacks +50%, and neither can be blocked by a shield;
   - a missed called shot lands on a neighbouring zone 50% of the time.
3. **Coverage.** The blow passes through worn layers from the outside in: cover, then armour, then over, then under (the four Dwarf Fortress layers, §2.10). Each piece rolls its coverage of that zone. The **named gaps** (armpit, inside of the elbow, back of the knee, face, neck, groin) bypass the armour layer.
4. **Each layer is tested with Dwarf Fortress's momentum rules** (from the DF wiki's *Material science* page, reverse-engineered by the community; we use SI units and calibrate against DF in the Phase 0 melee spike).
   - **Momentum, not energy.** DF combat is momentum-based. A swing's momentum grows with strength, the weapon's velocity multiplier and its mass, but with diminishing returns: lighter weapons swing faster, so weight matters little past a threshold.
     - Skill multiplies momentum up to 2× for a master.
     - Quick attacks halve it; heavy or wild attacks add 50%.
     - Attacking a prone target doubles it.
     - A mounted charge adds the horse's momentum (§3e).
     - Missiles take their momentum from the launcher (bow, crossbow) rather than the archer's strength.
   - **Contact area** is the smaller of the weapon's contact area and the layer's area. Body parts use DF's relative sizes: human upper body 18%, lower body 18%, head 5.4%, neck 1.8%, upper arm 3.6%, lower arm 3.6%, hand 1.4%, upper leg 9%, lower leg 7.2%, foot 2.2%.
   - **Edged blows** cut through a layer when momentum ≥ (rSY + (A + 1) × rSF) × (10 + 2 × Qa) / (S × Qw):
     - rSY and rSF are the layer-to-weapon ratios of shear yield and shear fracture;
     - A is the contact area;
     - S is the weapon material's sharpness;
     - Qw and Qa are the weapon's and armour's quality multipliers.
     
     On success the layer is cut, momentum drops about 5%, and the next layer is tested. On failure, the blow acts as blunt against that layer.
   - **Blunt blows** can be deflected outright when 2 × weapon size × weapon impact yield < A × armour density. Otherwise they crush a layer when momentum ≥ (2 × IF − IY) × (2 + 0.4 × Qa) × A, using the layer's impact fracture (IF) and impact yield (IY).
   - **When a blow fails both tests**, it becomes blunt for good, and its momentum is multiplied by strain-at-yield ÷ 50,000. Rigid metal armour therefore soaks 98–99% of a stopped blow.
     - **Mail is elastic** (its strain-at-yield is treated as 50,000). It turns a cut into a blunt blow without softening it, which is why padding goes under mail.
     - **Woven cloth** has almost no shear resistance.
   - **Penetration depth** limits how deep a point or edge goes before the blow turns blunt. If it exceeds the size of the struck part, the part can be severed.
5. **Tissues** are tested the same way. They are the species' materials (skin, fat, muscle, bone and so on).
   - Tendons, nerves and blood vessels are structures inside muscle, hit with a probability set by their area.
   - Organs sit behind their parent part.
6. **Output.** The result is a list of **wound records** (part, tissue, size, state). These drive bleeding in ml per second, pain and loss of function (§4A.3).
- **Layer order (one definition, used everywhere):**
  - **under:** linen;
  - **over:** tunic, gambeson or padded jack;
  - **armour:** mail, then plate over it where the layer permits;
  - **cover:** surcoat, cloak.

  Helmet, shield and weapon are separate slots. The paper-doll draws in this same order (§2.7).
- **Cost.**
  - Typical load is about 530 resolutions per second at 2,000 soldiers (about 40% in contact, one swing every 1.5 s).
  - The budget allows a peak of about 1,300 per second.
  - Derived stats are precomputed tables (§2.10), so a resolution is a lookup plus a few multiplications.
- **Level of detail applies only to AI and rendering.**
  - AI and steering run at 20 Hz near the player and 5 Hz elsewhere.
  - Distant figures use the Field and Command sprites.
  - **Outcomes never depend on where the camera is.** A test checks that near and far soldiers produce statistically equivalent outcomes.
- **Bugs in the old `GameData.resolve_attack` that this replaces:**
  - inverted hardness (`GameData.gd:451-465`);
  - hit chance computed after the sub-part redirect;
  - the lung key mismatch;
  - an off-by-one roll;
  - weapon volumes in the data that are far too large (giving a 30 kg sword);
  - severed limbs rolled at 30% after the battle (`BattleController.gd:1372`). Severing now only happens in the fight, from an actual blow.

### 2.7 Presentation
- **Camera zoom bands:**

| Band | Scale | View across a 1920 px screen | What's drawn |
|---|---|---|---|
| Close | 64 px/m (2× pixels) | ~30 m | Full sprites at double size: faces, equipment layers, wound overlays |
| Street | 32 px/m (1× pixels) | ~60 m | Default; one tile = one metre |
| Field | 8 px/m | ~240 m | Baked 8×16 mini-sprites with team colours |
| Command | 2 px/m | ~1 km | Formations as banner blocks; chunk images |
| World map (M) | | | Pixel overview map with drawn icons |

  Pixel art only zooms at whole-number steps (1×, 2×, 3×). The Field and Command bands use their own smaller sprites rather than scaling the full ones down.
- **Art style: UltiCa-style pixel art**, after Cataclysm: DDA's "Ultimate Cataclysm" tileset, but with original medieval art drawn by Claude. It is top-down with a slight front tilt.
  - **Terrain:** 32×32 tiles. Walls show their top plus a short front face.
  - **Characters:** 32×64, drawn from the front, back and side; the fourth direction is the side view mirrored, so facing (and flanking) reads clearly. **Horses and large animals:** 64×64, same three views.
  - **Items:** 16×16 (up to 32×32).
  - **Buildings are drawn roofless** at Close and Street zoom (as in CDDA and RimWorld), so interiors, workers and fights indoors are always visible. Roofs appear only at Field and Command zoom and on the world map.
- **Characters are layered paper-dolls.** A body base (several body types, skin tones, hair) has equipment layers drawn on top in the §2.6 layer order: under (linen) → over (tunic, gambeson) → armour (mail, plate) → cover (surcoat, cloak), then helmet, shield and weapon. Horses get tack layers (saddle, bridle, barding).
  - **Wounds are overlay layers on each body part:** blood, bandage, splint, missing limb. What you see matches the anatomy simulation.
- **Animation is baked from a rig.** Each layer is drawn as a few body-part pieces. `tools/art/` poses a 2D rig and **bakes pixel frames**: 4 directions (3 drawn views) × actions, each with an automatic clean-up and outline pass.
  - Actions: idle, walk, jog, wind-up, strike, recover, block, parry, hit-react, stagger, fall, crawl, carry, and work loops.
  - **Attack wind-ups are readable**, because real-time melee depends on that.
  - New armour or a new weapon means drawing parts, not hand-animating hundreds of frames.
- **Crowd rendering:** one MultiMesh per layer group. Each instance carries an atlas frame index, a palette row and wound flags. Instances are y-sorted every frame.
- **Readability:**
  - Role silhouettes: apron, hood, polearm, helm crest.
  - Nameplates on hover or while holding Alt.
  - The hero, the squad, and selected or hovered people get outline rings.
- **Lighting is a mechanic.**
  - Each tile has a light level, smoothed with radial lights.
  - Day and night tint.
  - At night and underground, only torches, fires and moonlight show the world.
  - Vision radius depends on light.
  - Darkness hides you, and hides them.
- **Art pipeline (Claude-drawn pixel art):**
  - Sprites are **written as code and data**, not painted by hand: indexed pixel grids plus small procedural helpers (noise fills, brick and plank patterns, an outline pass, a top-left light ramp), using **one fixed palette of about 48 colours**. They live in `art/src/`.
  - `tools/art/` bakes them into PNG atlases. It validates palette use, sizes and outlines, generates terrain autotile variants (corners, T-junctions, damaged states) from a few base pieces, and exports palette ramps.
  - **Recolouring is a palette swap**, a shader lookup by palette row. One sprite covers every faction colour, material (iron, bronze, steel) and quality.
  - The quality bar is set by the Phase 0 style sheet (the "Ravensguard Tileset" artifact).
- **UI is pixel-styled to match the tiles.**
  - 9-slice pixel panels and pixel icons.
  - A bundled OFL pixel font.
  - UI scale only in whole steps (1×, 2×, 3×), which keeps text crisp from 720p to 4K.
  - Widgets: Tooltip, KeyHint, ContextMenu, RadialMenu, BodyDiagram, PersonCard, ItemSlot, LedgerTable, Toast, TimeControls, PartyBar, Minimap, ObjectiveTracker, LogFeed, ChronicleView, LevelIndicator.
- **Settings and accessibility:**
  - **Rebinding** for every action, per context, with conflict warnings.
  - **Resolutions that aren't an exact multiple:** the world renders at the largest whole-number scale that fits, and the remaining margin is filled by the HUD. For example, 1440p uses 2× with a wider view. An optional smooth-scaling mode is also available.
  - **Colour-blind safety:** faction palettes are chosen to stay distinct for colour-blind players, and every faction colour also has a **shape marker** on banners and shields.
  - Text size steps; toggles for screen shake, flashes and slow motion; hold-or-toggle options for sprint and block; captions for important sounds.
- **Strings:** localization is wired through `tr()` from Phase 0, and generated chronicle text uses localizable templates.

### 2.8 Input (one key map per context, conflict-checked in CI)
**World context:**

| Action | Keyboard | Mouse |
|---|---|---|
| Move | WASD | Left-click ground |
| Default action on target | E | Left-click: a person or object is used, an enemy is attacked |
| Everything you can do to a target | Q | Right-click |
| Select own people | Tab to cycle | Shift-click, box-drag |
| Pause / speeds | Space / 1 2 3 4 (= 1x, 2x, 4x, 8x) | Time widget |
| Called shot | T (pauses) | Right-click enemy → Aim |
| Order wheel | R (hold) | Radial menu |
| Formations | F1–F4 | Formation cards |
| Character / Inventory / Squad / Journal / Ledger / Faction / Codex / Chronicle | C / I / P / J / L / F / K / H | Hotbar |
| Build / Map | B / M | Hotbar, minimap |
| Camera | Arrows, `=` / `-`, Home | Middle-drag, wheel, edge pan |
| Back | Esc | ✕ on every panel |

World context, continued:

| Action | Keyboard | Mouse |
|---|---|---|
| Sprint (hold) / Sneak (toggle) | Shift / Z | |
| Combat stance (toggle; also automatic when attacked) | X | |
| Throw | G, then aim | Mouse |
| Torch on/off | V | |
| Look up / down a level | PgUp / PgDn | Shift + wheel |
| Nameplates | Alt (hold) | |
| Controls overlay | ? | Help button |
| Quicksave / quickload (not in Ironman) | F5 / F9 | |
| Report a bug (testers) | F8 | |

**Combat stance context** (the mouse becomes your weapon arm; Q still opens the context menu):
- Left mouse: attack. Hold to wind up, release to strike; the attack type comes from the weapon or the radial.
- Right mouse (hold): block. Tapping it in the 0.2 s before impact is a **parry**, which opens a riposte.
- Right mouse during your own wind-up (first half): **feint**.
- Ctrl plus a direction: **dodge**.
- T: called shot (pauses). F1–F4 and R work as in the world context.

**Called-shot context** (the game is paused):
- ↑ / ↓ selects a body part; ← / → selects an attack type.
- F strikes; Esc cancels.
- The mouse can click a part on the doll directly.

**Build context:**
- Left-click places; Z / X rotate; Esc cancels.
- Right-click removes a ghost.

There is no double-click to interact.

### 2.9 Content, modding and blueprints
- **All content is data.** Every definition is a typed C# record loaded from data files: materials, items, body plans, recipes, building pieces, room types, blueprints, business types, cultures, factions, names, dialogue, ambitions, landmarks, creatures, and ruin and dungeon site templates.
- **The base game is a mod.** `content/base` loads through the same `ModLoader` as user mods.
  - Mods have a load order.
  - They can override entries by ID or add patches.
  - Conflicts get a readable report.
  - The save records the content and mod manifest.
- **Converting the old data.** The existing data — `data/*.json`, the static tables in `BuildingData.gd`, `ItemData.gd`, `CharacterCreationData.gd` and `CodexData.gd`, and the tables in `GameData.gd` — is converted into `content/base` by a one-off script, then corrected per §4A.
- **File format: JSON with comments and trailing commas.** It's read with System.Text.Json set to skip comments, and checked against **JSON Schemas**. Editors get autocomplete, and errors are caught at load with the file, line and field.
  - Content is compiled into a binary cache, keyed by a hash of the content, on first load. That keeps start-up fast even with big mods; slow loading is a known Cataclysm complaint.
- **Renames don't break anything.** Migration tables map old IDs to new ones (`"migrate": {"old_id": "new_id"}`), so renaming content never breaks saves or mods.
- **Buildings are blueprints** (Cataclysm-style layouts, extended):
  - **One format for everything built.** World generation, the player's build tool ("save as blueprint"), NPC construction contracts and mods all use the same blueprint files.
  - **Layout is text.** `rows` of letters plus a `legend` that maps each letter to a wall, floor, door or workstation piece ID, as in Cataclysm's mapgen, so a blueprint can be read and diffed. Upper floors and cellars are extra `levels`.
  - **The in-game build mode saves any structure as a blueprint** in this same text format. Most content is drawn in the game, and editing by hand stays possible.
  - **Composition.** A blueprint is either a whole building or a **room template** (bedroom, workshop, hall, cellar). Houses are assembled from room templates fitted to a lot. Walls and roofs follow **culture style and wealth**: wattle-and-daub for poor villagers, timber for the middling, stone for the rich.
  - **Contents come from who lives there, not from the blueprint.** The blueprint defines the structure and **room purposes** (sleeping, work:tanning, storage, hearth). The game fills those rooms from the actual household's occupation, wealth and history:
    - Hedda's house gets tanning racks and hides because she's a tanner.
    - A merchant's house gets a strongbox.
    - A house burned in a raid keeps only what survived.
  - **Checked in CI:** every ID resolves; every room can be reached from a door; every room purpose can be satisfied; `RoomDetector` recognises each room as the type it's meant to be.
- **Later:** an in-game content browser, a dev console, and C# script mods (after 1.0, because of security and stability).

### 2.10 Materials, items and interactions (Dwarf Fortress material science, Cataclysm item depth)
**Materials are data, in the style of Dwarf Fortress.** This extends the old `data/materials.json`, which has 17 materials with hardness, density, impact yield, shear yield and elasticity.
- **Mechanical properties:**
  - solid density (kg/m³);
  - impact yield, fracture and strain-at-yield;
  - shear yield, fracture and strain-at-yield;
  - maximum edge (how sharp it can be made);
  - hardness.
- **Thermal properties:** specific heat, melting point, boiling point, ignition point, and heat-damage and cold-damage points (°C).
- **Everything else:** base value per kg, a palette ramp, and state (solid, liquid, powder, paste).
- **Templates with inheritance** (metal, stone, wood, leather, cloth, bone, flesh, plant fibre) keep definitions short. A mod adds "bog iron" by inheriting from iron and changing three numbers.

**Creatures and plants are made of materials.** Body-plan tissues (skin, fat, muscle, bone, cartilage, nerve, hair) are materials of that species. Consequences:
- Butchering a cow yields cow hide (which tans into cow leather), beef, tallow, bone and horn.
- A draugr's dried flesh has its own yields.
- **Combat uses one physics model for armour and flesh:** the §2.6 resolver tests each layer's material, whether that layer is mail or muscle.

**Items are shape × materials × quality × condition.**
- **Shapes** are data templates: sword, gambeson, barrel, door. Each defines volume, contact area, penetration and the slots it fills.
- **Derived values:** weight = volume × density, and edge = material maximum edge × quality. Armour protection comes from the material and the shape's thickness. Insulation, value and container volume follow the same way.
- **Composite items are made of components**, each with its own material: a blade of steel, a grip of wood, a pommel of bronze. Damage, repair and disassembly work per component, as in Cataclysm.
- **Derived stats are precomputed into tables at load,** so combat with 2,000 soldiers only looks values up.

**Clothing and armour layers, Dwarf-Fortress style.**
- Four layers: under, over, armour, cover (the §2.6 layer order). Each item has a layer size and permit, which limits what fits on top of it.
- Coverage is a percentage per body part, with the named gaps from §2.6.
- Items are fitted to a body size; a piece fits anyone within ±1/7 of that size (as in DF), so looted armour often needs refitting (§3g).
- Each layer adds encumbrance.
- Clothing gives insulation against cold and heat.
- Clothes get wet in rain, becoming heavier and colder until dried.
- Blood and dirt stains lower value until washed.

**Wear and condition.**
- Every item has a durability clock with visible wear levels (DF-style: worn, threadbare, tattered), and eventually falls apart. Clothing also tears when the wearer is hit, especially by cuts that get through.
- Iron and steel rust in damp storage. Organic items rot.
- Items above their heat-damage point scorch, burn or melt.
- Repair consumes materials and needs the right tool qualities.

**Tools and crafting, Cataclysm-style.**
- Items carry **tool qualities**: cutting, sawing, hammering, chiselling, digging, prying, sewing, lockpicking, cooking.
- Recipes ask for qualities, not specific items. A heavy rock can hammer a nail, badly.
- Containers have volume and hold items, liquids or powders. Ale is a liquid in a barrel and spoils, and water can be carried.
- Disassembly returns components.

**Interacting with the world.** Everything physical goes through the same resolver:
- Throw anything; damage comes from its mass, shape and material.
- Fight with improvised weapons.
- Smash or burn a door; it has material hit points.
- Dig through a wall, with tool quality against the wall material's hardness.
- Barricade, carry, drag, fill containers, butcher corpses, and quench and temper while smithing.

**Temperature and fire, kept targeted rather than simulated everywhere.**
- Tiles carry an ambient temperature from season, weather, heat sources and fire. Only items near heat sources or in fire update their temperature.
- **Fire spreads** using ignition points, so wooden buildings and thatch burn, and stone does not.
- Forges reach working temperature (a bloomery does not melt iron; it makes a bloom).
- People suffer cold or heat according to their clothing's insulation.
- No general fluid flow: rivers are fixed, flooding comes from events, and fire is the only spreading hazard.


### 2.11 Saves, performance budgets, tooling, testers and audio
- **Saves:**
  - Manual save slots without limit, plus quicksave (F5) and quickload (F9) outside Ironman.
  - **Autosave** every 15 real minutes and before major moments (a battle starting, going underground, a long journey), rotating three autosave slots.
  - **Ironman** uses one save slot plus two rotating backups. The backups are only for crash recovery, not for reloading. It also saves on quit.
  - **Saving never stalls the game.** A save is written on a worker thread from a sim snapshot, with a hitch of at most 100 ms.
  - **Budgets:** under 150 MB after 10 game years for a player who stays near home, and under 400 MB for one who has crossed the continent (about 1 MB per detailed region plus visited chunks); save in under 3 s and load in under 10 s on the reference PC.
  - **World-library compatibility:** worlds and saves from older versions load through `SaveMigrator`. If content or mods are missing, a readable report lists what's affected before loading.
- **Performance budgets** (reference PC: 6-core CPU, RTX 3060-class GPU, 16 GB RAM, 1080p, 60 fps):

| Work | Budget |
|---|---|
| Frame (render + UI) | 16.6 ms |
| Live sim tick at 20 Hz, normal bubble of ~300 people | ≤ 8 ms |
| Live sim tick in the battle bubble with 2,000 soldiers | ≤ 25 ms (half the tick) |
| Hourly abstract tick (25,000 people, 58,000 fields, the region economy) | ≤ 150 ms on a worker thread, applied at a tick barrier |
| Seasonal continent tick | ≤ 500 ms on a worker thread |
| Lighting | ≤ 2 ms |
| Temperature and fire | ≤ 1 ms typical; ≤ 4 ms during a large fire |
| Chunk generation | ≤ 4 ms per frame, time-sliced |
| Memory with a 10-year save | ≤ 6 GB |

- **Tooling** (`tools/debug/`, in dev builds from Phase 0–1):
  - a dev console;
  - a **sim inspector**: click anyone to see needs, job, path, relationships, purse and wounds;
  - a **legends and history viewer**;
  - an economy dashboard (prices, stocks, coin conservation);
  - a **dev blueprint editor** (Phase 1, before the player build mode exists);
  - hot-reload of content;
  - **deterministic replay** of a bug save.
- **Private testers and crash reports** (from Phase 3):
  - Local logs.
  - A crash handler that zips the log, the last autosave, the content and mod manifest, and system info.
  - An F8 bug-report button that adds a screenshot.
  - Nothing is sent anywhere without the tester's consent.
- **Version control:** art source is code and data in git. Baked atlases are build outputs and are not committed. Large binaries (audio, any commissioned art) use Git LFS.
- **Licences:** bundled fonts are OFL, shipped with their licence files. A third-party licence list ships with the game. Audio must be CC0 or royalty-free, or commissioned.
- **Audio direction** (Claude can't make sound):
  - Sound effects from CC0 or royalty-free libraries, positional in the world.
  - Ambience by weather and place.
  - Adaptive music layers: calm, tension, battle.
  - Placeholder audio from Phase 4; final audio and music in Phase 8.

---

## 3. Game design

1. **Start flow.** One Quick Start screen: name, origin cards (each with a starting ambition), Ironman, and an "Advanced" world panel.
   - **With a world from the library:** walking in under 60 seconds.
   - **With a brand-new world:** about 2–3 minutes. That is up to 60 s of history (shown as a live chronicle), the automatic region choice, and region building.
   - **"Choose region"** opens the embark map instead of picking automatically.
2. **Progression: skills improve with use** (0–100), with no XP levels.
   - Skills: weapon families, archery, athletics, riding, surgery, smithing, carpentry, farming, trade, speech, leadership, tactics, stealth, lockpicking.
   - Attributes (strength, toughness, dexterity, perception, wits) train slowly from what you do.
   - Injuries can permanently lower attributes.
3. **The economy is a closed money supply** (a redesign, not a port).
   - **Coin is minted** only by faction mints, from mined metal. Until Phase 7 the only coin is the crown (silver). From Phase 7 step 0 there are gold, silver and copper pieces (1 gp = 10 sp = 100 cp; the silver piece is the old crown), decided with the user in Nov 2026.
   - Money moves between people as wages, taxes, rents, trade and tribute.
   - **Leaving the economy:** coin is lost with the dead, buried in hoards, or melted down.
   - **Goods leave the economy:** they are eaten, worn out, burned, or lost.
   - Prices come from local supply, demand and the money in circulation.
   - The old code's money creation is removed: the poll tax on abstract class counts (`ConsumptionSystem.gd:184-202`), 2% compounding interest, and +500 crowns on promotion (`SettlementManager.gd:274-278`).
   - Soak tests watch for inflation and deflation, including with a scripted "rich player" bot.
4. **People.** All ~25,000 are records. Talk, trade, hire, recruit, rob or attack anyone.
   - **Dialogue:**
     - Systemic templates (~300 base lines plus barks), parameterised by job, mood, relationship and rumours.
     - Authored dialogue for unique NPCs (~20–50 lines each).
     - **Rumours** are drawn from the chronicle and spread person to person, so "you overheard it" is a real mechanism.
5. **Travel is a game, not a wait.**
   - **Road encounters** (data-driven): travellers, merchants, refugees, patrols, ambushes, an army column.
   - **Night camp:** cook, set sentries, and talk with companions, which builds relationships and reveals their ambitions.
   - Forage and hunt along the way.
   - Auto-travel at 8x, with interrupts.
6. **Combat anywhere** (see §2.6).
   - Morale, rout, surrender and retreat; knock-outs, capture and loot.
   - Feedback: hit flashes, part callouts, shake, persistent blood, and slow motion on crits, kills and hits on your hero.
   - **Flavour text, Dwarf Fortress style.** Each blow becomes a line of prose built from the resolver's layer trace, for example: *"Aldric's axe bites through the bronze mail and tears into the muscle of Bram's upper arm. An artery is opened; blood sprays."*
     - Lines come from localisable templates, varied by weapon, material, attack type, outcome and severity, so the same blow doesn't read the same way twice.
     - The plain wound list is shown in a tooltip and on the paper-doll.
     - The log is filterable (your hero only, your side, kills and serious wounds) and is kept for the after-fight report and the chronicle.
   - **Watch mode and autopilot:** see §3i.
7. **Failure** is the same in both modes.
   - If you're knocked out, what happens depends on who won:
     - bandits rob you and leave you;
     - slavers capture you;
     - guards jail you;
     - an army takes you prisoner for ransom.
   - **Ways out of captivity:** pick the lock (skill), work off the sentence, be ransomed by your faction or friends, be rescued by companions (you take control of one), or escape during transfer.
   - **Death:**
     - Without Ironman, you can reload or continue as someone else.
     - With Ironman, you continue as a companion or heir, who inherits property and reputation. The run ends only when nobody is left, and the world persists for a new character.
8. **Property, building and businesses.**
   - Parcels and deeds; claiming land; ghost blueprints; hauling; `RoomDetector`; businesses run hands-on or through passive shares.
   - **Stewards** can be hired to run properties, so managing many of them stays feasible.
9. **Management at scale.**
   - **Ledger (L):** every property, business and fief in sortable tables with alerts.
   - **Fief tab:** zoning, taxes, levies.
   - **Army tab:** garrisons, retinue, wages.
   - **Stewards and captains** handle delegation.
10. **Society.**
    - Owner flags; crimes only when witnessed (the HUD shows "seen" or "unseen" before you act); bounties, guards, jail, slavery; reputation.
    - **Contracts** are this game's quests: escort, guard, hunt, deliver, collect a debt, take a fief.
    - Factions react to you; you can become a vassal, then a lord, then found your own faction.
11. **Ambitions** are your goals, with milestones.
    - Hints name where people find such work.
    - Contracts and ambitions **can be pinned to the map** (a soft marker the player chooses). The world never points at anything by itself.
12. **The chronicle and "why" links, scoped honestly.**
    - Cause chains exist for **concrete event types**: fires, raids, battles, witnessed crimes, wounds, deaths, laws, famines, mine floods, wars.
    - Price changes without a concrete cause are labelled **"market drift"** and show the supply and demand trend.
    - Every chronicle entry has either a specific cause or that explicit label. Coverage is measured.
13. **Warnings before failure:** encounter strength and odds, a starvation timeline, the injury consequences shown on the paper-doll, and "seen" or "unseen".
14. **Authored content:** landmarks, unique NPCs and faction leaders, placed by a landmark generation phase.
15. **Armies and war at scale:** see §3d. **Horses:** see §3e. **Learning the game:** see §3f. **Crafting:** see §3g. **Vehicles and ships:** see §3h. **Watch mode and autopilot:** see §3i. **Realm and lordly income:** see §3j.
16. **Audio:** see §2.11.

**Cut or folded in:**
- The REGION, CITY and LOCAL modes.
- The separate battle screen.
- The old management mega-screen, replaced by the Ledger.
- The old separate dungeon mode, replaced by underground levels in the same world (§3b).
- Bump-to-start dialogue.
- Debug sandboxes, which move to the dev console.

### 3a. Farming: seasonal and field-based
This replaces the daily-trickle acreage pool. The historical numbers in `Globals.gd:28-33` are kept, but rescaled to the game year.
- **Time-compression rule.** Anything historically measured per year is scaled to the 60-day year, so that *land per person and workers per acre stay historical*:
  - Yields per harvest are multiplied by 60/365.
  - Seasonal labour rates are multiplied by 365/60.
- **Food unit: the measure.** One measure is a day's grain for one person (≈ 1.1 kg, from 15 bushels a year per person).
  - A person eats 1 measure per game day, so a year is 60 measures.
  - A sown acre of average soil yields about **48 measures** gross. About 9–10 are kept as seed, leaving roughly 38 net.
  - So one person needs about **1.6 sown acres**, or about **2.3 arable acres** with a third left fallow.
  - The region's 25,000 people therefore need about 58,000 acres (≈ 240 km², about a quarter of the region).
  - The HUD's "food in days" is literally measures ÷ mouths.
- **Fields are parcels.** A 64 m world tile is ≈ 1 acre, so every field is a real plot that can be seen, walked, owned, leased, bought, zoned, trampled and burned.
  - Each field has soil fertility (0–100, from world-gen drainage, slope and biome) and a crop.
  - Fields are created as records at world generation (~58,000), and the abstract tier uses the same per-field maths.
- **The three-field rotation is per field, as it was historically:**
  - Winter grain (wheat or rye) is sown in autumn.
  - Spring grain or legumes (barley, oats, peas, beans) are sown in spring.
  - Fallow fields are grazed.
  - **Fertility:** wheat −10 per crop, barley −6, legumes +5. Fallow with grazing manure gives +15; marling or manuring is an improvement project.
  - **Yield multiplier** = fertility / 70, capped at 1.3.
- **The seasonal calendar** (15-day seasons):
  - **Spring:** plough and sow the spring field.
  - **Summer:** weeding and hay-making (winter fodder). The **hungry gap** comes as stores run low before the harvest.
  - **Harvest window:** the last 5 days of summer plus the first 5 of autumn. This is the labour peak; seasonal hands are hired, which is a player job.
  - **Autumn:** threshing, sowing winter grain, slaughtering surplus animals.
  - **Winter:** living off stores, repairs.
- **Labour rates** (scaled ×6): a plough team (ploughman + 2 oxen) ploughs ~6 acres per game day; a reaper cuts ~2 acres per game day; threshing runs through autumn and winter.
  - In the live bubble, field work shows as visible progress on the field (ploughed furrows, stubble, sheaves), with workers animating on it. It isn't cell-by-cell movement.
- **Variance with causes.** Weather (§3c) brings late frosts, droughts, hail and wet harvests; blight strikes some years. Armies trample standing crops along their path, foragers empty barns, and raiders burn fields and granaries.
  - Every one of these is a concrete cause, so "why is bread dear?" always has an answer.
- **Storage is physical.** Sheaves go into barns; threshed grain goes into granaries with a capacity.
  - Spoilage is 5% per season in a barn or house, and 1% in a proper granary.
  - Millers grind grain to flour for a toll (about 1/16).
- **The harvest split:** seed reserve → tithe (10%) → rent (from the parcel's lease: a share of 20–33%, or coin) → household stores → surplus sold at market. This is where "grain to gold" starts.
- **Livestock is tied to farming:**
  - Oxen pull ploughs.
  - Sheep graze the fallow and manure it, and give wool.
  - Hay from meadows feeds animals over winter.
  - Herds are culled in late autumn when fodder is short.
- **What the player can do:**
  - Buy or lease fields and set their rotation.
  - Hire harvest hands; build barns, granaries and a mill (which takes tolls).
  - Hoard grain before the hungry gap and sell high.
  - Guard a harvest.
  - Time levies to avoid the harvest, or force the enemy to fight during theirs.
  - Burn an enemy's fields.

### 3b. Exploration: underground and dangerous places
For the hero alone or with a party.
- **Technology: underground layers.** Each chunk can have levels below it:
  - z = −1: cellars and crypts;
  - z = −2 to −3: catacombs and caves;
  - down to −5: deep mines.
  
  Levels connect by stairs, ladders, shafts and cave mouths. They use the same simulation, pathfinding and live bubble as the surface. The surface keeps running while you're below, and people can follow you down (or come after you). Underground chunks are generated on first visit from the site's template and its history, then snapshotted like the surface.
- **Light is a mechanic.** Underground and at night, only torchlight shows the world (see §2.7, Lighting):
  - A torch lasts about 20 real minutes of play (≈ 7 game hours at 1x), and a lantern's oil about an hour of play. These durations are set in sim-seconds, so the fast world clock doesn't cut a crawl short.
  - Darkness hides you and hides them.
  - Followers with the *superstitious* trait lose morale in the dark.
- **Every site exists because of history** (§1A). A typical region (32 km) holds:

| Site | Per region | What's there |
|---|---|---|
| Ruins and crypts | 8–15 | Sacked abbeys, burned keeps and manor ruins, each with 1–3 levels. Crypts and ossuaries. Loot comes from the event that ruined it (the reliquary of the abbey sacked in 287) |
| Caves | 10–20 | Animal dens, bandit and smuggler hideouts, hermits, underground rivers |
| Mines | 2–5 | Active, abandoned or flooded. Hazards: bad air (a candle gutters as warning), flooding, cave-ins, getting lost |
| Heist sites | Every castle and town | Castle dungeons and prisons, a lord's treasury, guild vaults, town undercrofts and drains |
| Barrows | 3–8 | Rich grave goods. Robbing them is a crime and a taboo: the church and villagers react, rumours of a curse spread, and a few really are guarded |
| Old battlefields | From history | Scavenge rusted arms and bones, plus the occasional named weapon from a legend |

- **Heists and jailbreaks:**
  - Guards follow patrol schedules and sleep in shifts.
  - Locks, keys, bribes, disguise (wearing a lord's livery), diversions and stealth in the dark.
  - Break a captured companion out, or rob a vault.
  - Consequences come through the crime and witness system (bounties, a manhunt).
- **Party play underground:**
  - Ropes, ladders, picks, shovels and lockpicks.
  - What you can carry out versus how fast you can run.
  - Traps: deadfalls, pits, collapsing floors.
  - Resting and camping below ground.
  - Injuries far from any surgeon.
  - Retreating the way you came.
  - An **auto-drawn map** of the passages you've explored.
- **Persistence:**
  - Loot is finite and historical.
  - Inhabitants are named people or animals.
  - A cleared cave can be reoccupied by a new bandit band.
  - Collapses and opened doors stay as they are.
- **Rare monsters.** A small bestiary of 3–6 kinds, drawn from northern folklore: for example draugr (barrow dead), a cave beast, a bog-thing.
  - Only 1–3 lairs per region, always deep, always tied to a legend in the history.
  - They use the same anatomy resolver with their own body plans. A draugr doesn't bleed or feel pain, so you must destroy its limbs or head. That makes them a test of the combat system, not a separate rule set.
  - Until met, they exist only as rumours. Killing one is a chronicle event and earns renown.

### 3c. Seasons and weather
- **The calendar** has four 15-day seasons (60-day year); each culture has its own names for months and feast days.
  - **Day length** follows the season and the continent's latitude. For example, at a mid-northern latitude a summer day has about 16 hours of light and a winter day about 8.
  - Day length sets work hours, market hours, how long torches are needed, and how much night there is for raids and stealth.
- **Temperature** is built in layers. This is the ambient value that §2.10 uses for warmth, freezing and fire:
  - the region's climate from world generation (latitude, elevation, distance from sea);
  - plus the seasonal curve;
  - plus the daily cycle (about ±6 °C);
  - plus weather fronts;
  - minus about 6.5 °C per 1,000 m of elevation.
- **Weather is simulated at continental scale and is deterministic from seed and date.** Fronts move across the continent with the prevailing wind from Stage 1 world generation, and each region turns the passing fronts into hourly weather from seasonal tables.
  - Weather states: clear, overcast, light or heavy rain, thunderstorm, morning fog (near water), frost, snow, blizzard, heatwave.
  - Multi-week conditions: drought, a wet year, a cold summer.
  - The same weather drives the history simulation, so famines in the legends come from real bad years.
- **Effects of weather:**

| Area | Effects |
|---|---|
| Travel | Rain turns roads to mud (×0.7 speed) and fields worse (×0.5); carts can get stuck. Snow slows travel (×0.6 to ×0.3). Heavy rain raises rivers and **closes fords** for days. In deep winter rivers freeze and can be crossed, until the ice cracks |
| Seeing | Fog and rain shrink vision and bow range. Overcast nights are darker; snow brightens the night a little |
| Combat | Rain slackens bowstrings (archery penalty) and makes footing poor (stamina cost). Wind makes arrows drift. Cold slows hands; heat tires anyone in heavy armour |
| People | Wet plus cold, set against clothing insulation, causes hypothermia. Heatwaves cause heatstroke. NPCs shelter in storms, fewer stalls open in the rain, and taverns fill up |
| Farming (§3a) | A late frost kills spring sowing. Drought cuts yields. Hail flattens crops. A wet harvest rots in the field. Snow cover protects winter wheat |
| Economy | Firewood demand doubles in winter. Closed roads stop caravans, so prices spike. The hungry gap falls before harvest. Fishing drops to ×0.4 in winter, and herds need hay |
| War (§3d) | Campaigns run spring to autumn; armies go into winter quarters. A winter siege starves and freezes the besiegers, and cold armies desert. Frozen rivers open new crossings |
| Fire | Drought and wind make fires spread faster; rain puts them out |
| Disease | Winter coughs and fevers; summer flux in crowded camps. These feed the death rates in §4A.2 |
| Buildings | Damaged roofs leak and spoil stored goods; heavy snow can collapse a weak roof |
| Superstition | Fog nights, the longest night and storms bring omens, rumours and the occasional real thing (§3b) |

- **Feasts and fairs:** a spring fair, the harvest festival and the midwinter feast. Markets, tournaments, weddings and hiring days cluster around them.
- **Visuals use palette swaps,** which fits the pixel art pipeline:
  - Grass and trees run green → autumn colours → bare in winter. Autumn leaves fall across the screen, after the game's title.
  - Snow cover is drawn over tiles; puddles and mud tiles appear after rain; water freezes.
  - Rain, snow and fog are overlays, with lightning flashes.
- **Information for the player:**
  - The time widget shows weather and temperature.
  - A **forecast** improves with skill (reading the sky) and an almanac.
  - Weather is a concrete **cause** in the chronicle, for example "Ford closed: river high after three days of rain".

### 3d. Armies and war at scale
- **Battle size** is a setting from 200 to 2,000 live soldiers; the default is 1,000. Larger armies feed **reinforcement waves** from a reserve as their formations take losses. The fight runs inside the **battle bubble** (§2.3).
- **Formations:** infantry, archers, cavalry, your retinue, and allied lords' contingents. They generalise the old battalion model (pivot, braced, target), now measured in metres.
- **Orders:** advance, charge, hold, follow me, flank left or right, shield wall, loose order, fire at will or hold fire, set against cavalry (spears braced), retreat.
- **How you command:**
  - You fight in the line as your hero.
  - F1–F4 select a formation; R opens the order wheel.
  - **A tactical pause overlay** lets you drag formations into place and draw their facing.
  - Captains with a Tactics skill run any formation you don't micromanage.
  - Your Leadership skill sets how long orders take to arrive and the reach of your morale aura.
- **Morale and rout** work per soldier and per formation (§4A.3). Routs cascade, and fleeing soldiers can be cut down or captured.
- **Armies are physical:**
  - **Marching columns.** Inside the bubble, an army is real people walking a road with a baggage train. You can scout it, ambush it, join it, or trade with its camp followers. Outside the bubble it is an abstract record on the same road graph, moved in and out by the LODBridge.
  - **Supply.** Each soldier needs 1 kg of grain a day: 1,000 men for 15 days need 15 t, about 30 carts.
    - Foraging strips the acres the army passes and hurts those settlements.
    - Wages are paid from the treasury chest.
    - Unpaid or starving armies desert, and deserters become bandits.
    - Raiding a supply train is a valid strategy.
  - **Camps** are real build pieces (tents, fires, palisades) and can be attacked at night.
- **Recruitment:**
  - **Levies** come from households (1 per 10 households in peace, 1 per 5 in war). Each man levied is missing from the harvest.
  - Retinues are paid professionals. Mercenary companies are hired groups of real people.
  - Large armies combine all of these across a realm and march into the region from outside (§1).
- **Sieges:**
  - Engines are built through the job system from hauled timber: ladders, rams, mantlets, siege towers that reach the wall-walk (z+1), and trebuchets.
  - Walls are structural pieces with hit points stored in `ChunkDelta`. A breach is a destroyed segment and stays until repaired.
  - Defenders fight from wall-walks and towers (§2.2a), using murder holes, sallies, and fire against engines.
  - Starvation sieges run on the town's real stockpile.
  - Winter sieges also starve and freeze the besiegers (§3c).
- **AI campaigns** (§4A.4):
  - Utility scoring of value ÷ (march days + siege days).
  - Armies muster after sowing and hold for the harvest unless the war is worth the shortfall.
  - The AI works from last-known positions reported by scouts and rumours.
- **Budget:** 2,000 soldiers at a 20 Hz sim tick, holding 60 fps on the reference PC (§2.11). The Phase 0 crowd spike decides how this is built. If the budget can't be met, the default battle size drops and reinforcement waves carry the rest; army sizes on the map stay the same.

### 3e. Horses and animals
- **Horses are creatures with their own body plan** (quadruped anatomy, horse materials such as hide, bone and meat) and 64×64 sprites in three views. They can be wounded, killed, stolen, bought, sold, bred (foals once a game year) and butchered.
- **Riding:**
  - Mount and dismount with E.
  - Trot at 5 m/s; gallop at 12 m/s, which costs the horse stamina.
  - Horses need fodder (hay or oats, measured in kg per day), water and shoes (iron).
  - Injured horses go lame.
- **Carts and wagons** are pulled by horses or oxen, with the capacities in §4A.1.
- **Mounted combat** uses the same resolver:
  - A charge adds the horse's momentum to the strike (§2.6), which is what makes a couched lance dangerous.
  - The horse is a large target.
  - A rider whose horse falls takes fall damage, and a heavy hit can unhorse him.
  - Cavalry is countered by braced spears and pikes, shield walls, and terrain: mud, slopes and woods slow horses.
- **Other animals** use the same creature system: dogs (guard and hunting), cattle and oxen, sheep, pigs, poultry, deer, wolves, bears, and the rare monsters (§3b).

### 3g. Crafting (Cataclysm depth, medieval knowledge)
Crafting is a full system in the style of Cataclysm: Dark Days Ahead, built on the materials and items model (§2.10) and the job system (§2.5).
- **Recipes are data**, in a JSON shape close to Cataclysm's, so its modders feel at home. A recipe lists:
  - **components with alternatives** ("2 iron bars OR 3 kg scrap iron"), consumed or kept;
  - **tool qualities with levels** (cutting 1, hammering 2, smithing 3);
  - **tools with charges or fuel** (a forge burns charcoal; a lamp burns oil);
  - **the workstation** (forge, anvil, loom, tanning pit, kiln, oven, workbench), with its qualities supplied by placed furniture;
  - the **skill** used and its **difficulty**;
  - optional **proficiencies**;
  - time in game hours;
  - minimum **light**;
  - **byproducts** (slag, offcuts, ash);
  - the result's quality formula, from skill, tools, materials and proficiencies.
- **Proficiencies** are specific know-how separate from skills, following Cataclysm's three tiers: **skill** (broad) → **proficiency** (focused) → **recipe** (single item).
  - Proficiencies form a **dependency graph**, as in Cataclysm's metalworking tree. For example: *principles of metalworking* → *blacksmithing* → *armoursmithing*, *bladesmithing*, *riveted mail*; *glazing* needs *pottery*.
  - Each proficiency is learned gradually, from 0 to 100%. Below 100%, recipes that use it take longer (a time multiplier) and are harder (a skill penalty), with both values set per recipe.
  - They are learned by doing, from a master, or from a rare manuscript.
- **How you learn recipes** (medieval rather than Cataclysm's paperback library):
  - basics everyone knows (a fire, a torch, a bandage, a simple meal);
  - recipes unlocked automatically at skill levels;
  - **apprenticeship** with a master (an NPC who has the recipe), paid in coin or labour;
  - **watching** a craftsman at work for long enough;
  - rare **manuscripts** (bought, found in ruins, stolen from a guild or monastery);
  - **taking apart** an item to learn how it was made (Cataclysm's `decomp_learn`).
  
  Recipes differ by **culture**, and guilds guard **trade secrets** (stealing one is a crime).
- **Recipes have steps.** A recipe can be split into named phases, each with its own time, tools, qualities and proficiencies, as in Cataclysm's recipe steps.
  - **Unattended steps** run without you: dough rising, hides in the tanning pit (a whole season), a charcoal clamp smouldering, pots in the kiln, meat curing.
  - Each unattended step has a deadline and a grace period. Leave it too long and the batch is ruined.
  - That's what makes running a workshop a matter of planning, not clicking.
- **Reusable requirement sets** (Cataclysm's `using`): common needs like "a fire and a pot" or "smithing tools" are defined once and referenced by many recipes.
- **Practice recipes** train a skill or proficiency without making a sellable item: forging practice billets, sewing samplers, carving spoons. They feed the practice yard idea (§3f).
- **Other requirements per recipe:** minimum attributes (strength for heavy smithing), effort level (which burns food and stamina), and morale effects for hated or satisfying work.
- **Crafting is physical work over time.**
  - **Unfinished items** exist in the world and can be resumed by you or by a worker.
  - Interruption doesn't lose progress.
  - You can craft in batches.
  - Failure can waste components, depending on difficulty and skill.
  - Nearby containers and furniture are used automatically.
  - Recipes can be queued as jobs, so workers and businesses craft through the same system (§4A.1).
- **Disassembly and salvage.** Take apart anything into components; salvage cloth into rags, armour into rings and plates, and buildings into materials (§2.10).
- **Repair and refit.** Mending, re-edging, re-riveting and resizing armour to fit another body (§2.10, fitting) all use tools, materials and skill.
- **Commissions.** Ask an NPC craftsman to make something with your materials. It uses his time, skill and workshop, and you collect it in person.
- **The crafting screen**, Cataclysm-style:
  - search and categories;
  - a "can make now" filter;
  - what's missing, with alternatives highlighted;
  - expected quality and time;
  - a link from any item to "what can I make with this" and "how is this made".
- **Volume:** about 30 basic recipes in Phase 3, about 80 in Phase 5, about 250 by 1.0. Recipes for cultures, guilds and secrets are mostly content, not code.

### 3h. Vehicles: a designer for carts, wagons, chariots, siege engines and ships
Like Cataclysm's vehicle construction: a vehicle is a grid of parts that you design, build, drive, damage, repair and take apart. The one system covers everything that moves and carries:
- handcarts and wheelbarrows;
- carts, hay wains, ox wagons and covered wagons;
- sledges and sleighs;
- chariots and war wagons;
- siege engines;
- river boats and sea ships.

- **The part grid.**
  - A vehicle is a grid of 1 m cells, the same scale as the world. Each cell stacks parts on a **frame**, which is required and structural.
  - Ships and siege towers have **several decks or floors**, using the same levels as buildings (§2.2a).
  - **Movement:** axle and wheels (solid, spoked, iron-tyred), runners (sledges), hull planks and keel (boats), mast and sail, oars and rowlocks, rudder or steering oar, brake or drag shoe.
  - **Hitching:** shafts, pole and yoke points; a draught harness (ox yoke, horse collar, breast strap).
  - **Crew and cargo:** driver's bench, seats and rowing benches, cargo bed or box (with a volume), tilt cover, bed, animal stall.
  - **War and siege:** armour boards and pavise sides, arrow slits; mounted weapons (springald, ballista); ram head on chains; the floors, ladder and drawbridge of a siege tower; trebuchet arm and counterweight.
  - **Workplaces:** a mobile forge, cook wagon or peddler's stall. These are workstations, so businesses (§4A.1) and crafting (§3g) work on board.
- **Data, not code.** Parts are content: material, volume, mass, hit points, what they need underneath them and what they provide (cargo volume, seats, pull points, buoyancy, sail area).
  - Designs are **vehicle blueprints** in the same text format as buildings: `rows` plus a `legend` of part stacks, with extra decks as `levels` (§2.9). The designer saves to it, and mods add to it.
  - Cultures have their own templates: a steppe war chariot, a northern longship, a southern cog, a hay wain.
  - **Chariots are open to everyone.** They are balanced by cost, crew and terrain: fast on open, dry, flat ground; useless in mud, woods and ploughed fields.
- **The designer.** A top-down part grid, with a deck switcher for ships and towers.
  - It works like build mode and is opened from a cartwright's or shipwright's workshop, or anywhere for small designs.
  - It shows live derived numbers: mass, cargo, crew, pull needed, speed per surface, turning circle, draft and stability for boats, and protection for war wagons.
  - It warns about invalid designs: unsupported parts, no steering, a draft too deep for local rivers, a boat that will capsize.
- **Building and upkeep** run through the job and crafting systems (§3g):
  - recipes per part;
  - **wheelwright, cartwright and shipwright proficiencies**;
  - unfinished vehicles in the world that anyone can resume;
  - repair and disassembly per part.
  
  A wagon is built in a yard; a ship is built on a slipway at the water's edge and launched.
- **Power.**
  - **Draught animals** (§3e): ox, horse, mule, donkey, or a dog for small carts. Each has a pull force, a harness requirement, stamina, and food and water needs.
  - **People** push handcarts and siege towers, and row boats; their stamina and wages are real.
  - **Wind** drives sails, using the wind from the weather system (§3c).
  - **River current** moves boats downstream.
- **Movement, simplified physics** (deterministic, fixed-point):
  - Speed comes from pull force against total mass. Rolling resistance depends on the surface: road, field, mud and snow (from weather), and ice.
  - Slopes matter; turning circles come from the axles (a pivoting front axle versus fixed); load limits apply per wheel and axle.
  - **Getting stuck:** carts get stuck in mud. You push, unload, dig out, or add another team.
  - **Fords and draft:** fords close to wagons before they close to riders.
  - **Boats** use displacement and draft from their hull volume, checked against river depth from the river polylines. Overloading, beam seas and storms can capsize a boat.
  - **Driving:** the hero drives with WASD, NPCs drive with their own skill, and the live and abstract tiers use the same speed numbers.
- **Things on board move with the vehicle.** A vehicle is a moving sub-grid with its own local frame, and people, items and lights on it ride along.
  - You can walk the deck of a ship, fight on a war wagon, or climb a siege tower while it rolls.
  - Pathfinding links vehicle decks to the world through portals (gangplanks, ladders, the tower's drawbridge).
- **Damage** goes through the §2.6 resolver and materials, per part:
  - wheels and axles break;
  - fire uses each part's ignition point (§2.10);
  - siege engines are targets;
  - a holed hull takes on water;
  - a dead or panicked team leaves a wagon stranded.
- **Uses in play:**
  - **Trade and logistics:** caravans use real wagons, whose capacity comes from the design. The §4A.1 figures (cart ~500 kg, ox wagon ~1 t) become the base templates.
  - **Farming:** hay wains and harvest carts. **Winter:** sleighs. **Peddlers:** mobile shops. **Armies:** baggage trains (§3d).
  - **War wagons and wagon forts** (Hussite-style): wagons chained into a ring become a field fortification and a formation type (§3d).
  - **Chariots:** a crew of driver plus archer or spearman. A charge adds momentum the way horses do (§3e and §2.6); chariots are countered by braced spears and bad ground.
  - **Siege engines** (§3d) are vehicles: rams, mantlets, siege towers that reach the wall-walk at z+1, and wheeled trebuchets. They are designed, built, hauled and pushed with the same rules.
- **Water and ships.**
  - **River:** barges (poled, rowed or towed from the bank), ferries at crossings, rowing and fishing boats. Boat crews set the fishing rates in §4A.1.
  - **Sea:** coastal regions get harbours. Ships range from small boats to cogs and longships, sailed by wind and weather.
  - **Sea trade and sea raids** come in from the continent tick: a raid lands on the beach as real people from a real ship, and history's sea raids (§1A) leave grudges.
  - **Naval combat:** missiles from the fore and aft castles, ramming, fire and boarding (people fight on the decks).
  - **Voyages** between coastal places outside the region are abstract journeys, like border roads. This ties into the later multi-region travel (§1A, "Later").
- **Presentation.**
  - Vehicles face **8 directions**. Each part has sprites per facing, and the art tool (§2.7) bakes the diagonals from the drawn views.
  - Ships and towers are drawn deck by deck, roofless like buildings.
  - Draught animals use their own rigged sprites, harnessed at the hitch points.
- **Scheduling (§5).**
  - Phase 3: fixed template carts and wagons (no designer).
  - Phase 5: the designer, building, repair, river boats, and the wheelwright, cartwright and shipwright proficiencies.
  - Phase 7: war wagons and wagon forts, chariot combat, siege engines built from the designer, sea ships, harbours, sea raids and naval combat.

### 3i. Watch mode and autopilot: letting the AI play your hero
The player's hero can be handed to the same AI that runs every NPC, for one fight or for whole stretches of life. It's a convenience and a way to watch the world, not a cheat.
- **Watch mode for fights** (toggle with a key and a button on the combat HUD).
  - When a fight starts, or at any point during it, your hero and your side are fought by AI in real time. Your hero picks targets, attacks, blocks, parries, uses called shots and retreats on the same utility AI and timings as everyone else (§2.6, §4A.3).
  - Squads and formations follow your captains' Tactics skill (§3d).
  - You watch with the normal speed controls, and **can take back control or give orders at any moment**.
  - There is no instant skip: watch mode is real-time and uses the real resolver. Battles outside any bubble still use the abstract resolver as reports (§2.3).
  - **Stance settings** for the hero's combat AI: aggressive, defensive, protect a target, no lethal blows (knock out and capture), flee when badly hurt.
- **Autopilot for life.** Your hero runs on the NPC utility AI (§2.5), steered by **directives** you set:
  - work a job;
  - run a trade or caravan route;
  - tend your business or fields;
  - train a skill;
  - pursue an ambition step (§3.11);
  - guard a place, escort someone, or travel to a destination;
  - rest and recover.
  
  You can also set limits: a spending cap, no crimes, avoid danger above a chosen risk, and come home by nightfall.
- **Interrupts.** Autopilot stops, pauses the game and alerts you on events you choose:
  - attacked or threatened, injured, low on food or money;
  - a contract offer, a big choice (an offer of marriage, fealty, a bribe);
  - a companion in trouble;
  - reaching the destination.
  
  Each interrupt can be set to pause, notify, or let the AI handle it.
- **One AI for everyone.** Autopilot uses the same decision code and data as NPCs, so it makes the same kinds of mistakes a person would, and its outcomes stick.
  - It is **allowed in Ironman**, under the same rules.
  - The chronicle records what the hero did on autopilot like anything else.
- **Zero-player watching.** With autopilot on and interrupts set to "notify", the world plays itself around your hero like a living story, Dwarf Fortress style. You can step back in at any time.
- **Party members** you control (§3.4) use the same directives and stances, so a squad can work, train or travel on its own while you lead another group.

### 3j. How realms and lords get supplies and money
Revenue follows the feudal chain: peasants owe their lord, lords owe their overlord, and the crown also taxes everyone through its own officials. Every payment is a real transfer of coin or goods (§3.3, §4A.1), so supplies can always be traced back to where they came from.
- **Royal and lordly estates (demesne).**
  - Every lord, and the crown, holds estates worked by hired servants and by tenants' labour service.
  - Estates pay **food rents in kind** (grain, livestock, cheese, ale) into the holder's barns. Over time these are commuted to coin.
  - An itinerant court or lord can eat its way around its own estates.
- **What vassals owe for a fief.**
  - **Military service:** a set number of armed men for a set number of days a year, or **scutage**, a cash payment instead.
  - **Aids:** fixed payments for set occasions, such as ransoming the lord, knighting his eldest son or marrying his eldest daughter.
  - **Relief:** a fee when an heir inherits.
  - **Wardship:** the overlord runs an underage heir's fief and keeps its income.
  - **Marriage:** the overlord arranges, or sells, the marriage of heirs and heiresses.
  - **Escheat:** the fief returns to the overlord when the line dies out, and is **forfeit** for treason.
  - Unpaid dues become grievances and casus belli (§4A.4).
- **General taxes.**
  - **Hearth tax** (Phase 2): collected by the fief, with a crown share forwarded to the realm.
  - **Subsidies:** a fraction of every household's movable goods, for example a tenth in towns and a fifteenth in the countryside. They are granted by a council of the great lords, and the council's agreement depends on their opinion of the ruler and the war's cause.
  - **Tallage** on towns and on the ruler's own estates.
- **Trade:** customs on exports and imports at ports and border gates, tolls at bridges and gates (O5), and fees for market and town charters.
- **Justice and the mint.** Fines and forfeitures; seigniorage on coin minted (§4A.1).
- **The church:** tithes go to the parish (Phase 2). Clerical taxes and the income of vacant bishoprics are a ruler's occasional windfall.
- **Purveyance (requisition).**
  - The ruler's officers may take grain, carts, animals and fodder for the court or an army, at a set price paid late or never.
  - It supplies campaigns quickly, but raises grievances and unrest, can spark revolt (§4A.2 unrest ladder) and is a chronicle cause.
  - Armies are otherwise supplied by buying from merchants, by their own baggage trains, and by foraging on enemy land (§3d).
- **Loans.**
  - Merchants and bankers lend to rulers against future taxes or customs, with interest paid from the treasury.
  - Defaulting ruins the lender and closes the credit line.
  - **Loans move existing coin. They never create money (§3.3).**
- **Officials collect; the player sets policy.**
  - As lord or ruler you set tax levels, dues and customs rates, decide when to call a council for a subsidy or to requisition, and appoint officials. These are reeves, stewards, sheriffs or bailiffs, and customs collectors, and each one is a Person.
  - Officials collect, and they can be **honest or corrupt** (skimming is a crime with witnesses, §3.10).
  - The Ledger (§3.9) shows receipts by source, arrears, grievances and each official's record.
  - You can still act yourself, as any person can: ride out, escort a tax wagon, arrest a defaulter.
- **Treasuries are physical.** Chests, barns and granaries can be raided, burned or stolen from. A tax wagon on the road is a caravan (§4A.4) and can be ambushed.
- **Scheduling (§5).**
  - Phase 2: hearth tax, rents, tithe, the crown share and basic treasuries.
  - Phase 6: estates and food rents, vassal dues, subsidies and councils, tallage, customs, officials and corruption, loans.
  - Phase 7: purveyance and army supply, scutage in place of service, and campaign finance.

### 3f. Learning the game (brutal, but learnable)
- **No tutorial level.** You learn in the real world during the first hour:
  - Your origin's first ambition step comes with a **mentor NPC**, for example a caravan master, who gives a first small contract.
  - The game won't start you within 10 km of an active war or within 3 km of a monster lair. That is the only softening. Brutality stays as designed.
- **Contextual hints.** The first time something new appears, a one-line toast shows its key, with a "learn more" link into the Codex. Hints can be set to off, minimal or full.
- **Controls are always visible.** Every button shows its key, and **?** opens a controls overlay for the current context (world, combat stance, build, called shot).
- **A practice yard.** Training dummies and sparring partners in towns let you learn melee timing (wind-up, block, parry) safely, with skill gain capped.
- **Failure teaches.** After a defeat, a short screen shows the cause chain from the chronicle: what happened and why.
- **The Codex is the in-game manual.** Entries unlock as you meet things. History legends appear in it from Phase 1; the full manual arrives in Phase 8.
- **Onboarding is playtested** with private testers from Phase 3. One question is always asked: "Did you understand why that happened?"

---

## 4. The old code is design notes, not a spec
(See §4A for the full mechanics audit; the list below is the short version.)
The legacy GDScript doesn't run end to end and has no tests. Before each system is ported:
1. Write the intended behaviour as a short spec in `docs/specs/`.
2. Write golden tests of that intended behaviour.
3. Then implement it.

Known defects to design out:
- **Combat maths:** see §2.6.
- **Money creation:** see §3.3.
- **Construction:** `process_construction` uses crowns only, with no materials (`SettlementManager.gd:251-263`).
- **Population:** stored as a single integer.
- **Battle grid:** an ASCII grid with one unit per cell.
- **Turn-based assumptions:** bleeding uses a 0.2 time step in battle but 5.0 in dungeons (`BattleController.gd:413` vs `DungeonController.gd:698`).
- **Unseeded randomness and wall-clock reads:** `GDBattle.gd:15-43`, `GDQuest.gd:30`, `BattlePhysics.gd:281`, `BattleSiege.gd:90,194`, `GameState.gd:1079-1082`, `CityController.gd:144`, `AIManager.gd:387`.
  - World generation itself already uses its seeded `rng`.

---

## 4A. Mechanics deep dive: keep, fix, redesign
This section comes from a full read of the legacy code by four auditors (economy, settlements, combat, world/AI), with headline claims spot-checked. It is the design spec each port starts from.

**Two rules for units and time apply everywhere:**
- **Units.** Goods are measured in kg (grain is also shown in measures: 1 measure ≈ 1.1 kg, a day's food). Money is crowns. Bleeding and combat use sim-seconds; needs and wages use game-days.
- **Time compression.** Two categories behave differently:
  - **Daily flows** run at historical daily rates: eating, burning firewood, a day's work, a day's catch.
  - **Yearly biology** is scaled so that stock per person stays historical: harvests, woodland regrowth, herd growth, fish regrowth and soil recovery are ×60/365 per game year.
  - **Life events** (ageing, births, deaths, marriage) happen at historical *per-year* rates per game year. A 60-year life is 60 game years (about 180 days of play at 1x). In practice heirs take over through violent death, disease and Ironman succession rather than old age, though children born early in a long campaign do grow up into it.

### 4A.1 Economy (beyond farming)
- **Keep:**
  - The three-tier work logic: survive, then buffer, then profit. It becomes the decision AI for households and stewards.
  - Class-graded needs, and climate scaling of firewood.
  - Prices driven by days of cover.
  - Posted base prices, where grain at 10 per bushel makes the crown roughly a silver penny.
  - The buy-order "pull" model for trade.
  - The physics data in `materials.json`.
  - The recipe ratios, used as a starting point.
- **What's broken:**
  - **Money appears from nothing** in about 8 places: two overlapping daily taxes, market tariffs, 2% interest, payouts for dumping storage, caravans being paid on both ends, +500 crowns on promotion, ransom, and sieges that mint `population×2`.
  - **Money vanishes into nothing:** shop purchases and commissions pay nobody.
  - **Workshop output** is multiplied by building level but inputs aren't, so a level-5 weaver turns 2 wool into 6 cloth (`ProductionSystem.gd:480-489`).
  - **Pricing bug:** any good without a demand entry gets a default `demand = 1.0` (`PricingSystem.gd:21`), so iron, cloth and tools sit at the price floor.
  - **Item creation crashes:** `create_item` reads `WEAPON_TEMPLATES` and `ARMOR_TEMPLATES`, which don't exist.
  - **Units and timing:** yields are computed per year divided by 360, but mining uses /30; resource tiles produce goods with no workers; nothing depletes; nothing spoils.
  - **Formula sprawl:** four item-price formulas and three quality scales.
  - **Caravans:** capacity is measured in crowns, and two caravan dispatchers fight each other.
- **Redesign:**
  - **Workers and workplaces.** A person has an employer and a workplace. They change jobs only by being hired or quitting, driven by wages.
  - **Resources are physical stocks** on 1-acre tiles that deplete and regrow:

| Resource | Stock and regrowth | Work rate |
|---|---|---|
| Fish | Lake ~150 kg/acre, +25% per season | Shore 4 kg/day; boat crew 12 kg/day; winter ×0.4 |
| Wood | Woodland 100 m³/acre; coppice regrows 5 m³/acre/yr (scaled) | Cutter ~1.5 m³/day |
| Herds | Owned animals; 2 sheep/acre or 1 cow per 2 acres | Spring shearing ~1 kg wool per sheep; winter needs hay |
| Game | Deer 0.05–0.1/acre | A carcass gives ~40 kg meat plus a hide; poaching is a crime |
| Ore | Surveyed bodies of 2,000–20,000 t | A miner hews ~0.5 t/day; mines can flood |

  - **Seasonal extraction:** foraging only in summer and autumn; trap lines in winter; peat is cut in spring and burned in winter; sea salt only in summer.
  - **Businesses** are buildings with workstations, an owner and a cash box. A recipe is inputs + worker-hours + tool wear → output, and quality comes from skill. Building levels become extra workstations and better tools, never free output.
  - **Grounded rates:**
    - Bloomery: 10 kg ore + 15 kg charcoal → ~1.5 kg bloom.
    - Charcoal: ~5 kg wood → 1 kg.
    - Spinning: ~0.2 kg yarn per spinner-day, about 8 spinners per weaver.
    - Tanning: one season in pits, using oak bark.
    - Brewing: 1 bushel of malt → ~50 L of ale, which spoils within days.
  - **Production chains** (written as data; ✓ = some form exists today):
    - ore → charcoal → bloomery → bar → smith → tools and arms (✓ partial)
    - galena → smelt → cupel → **mint → crowns** (missing)
    - fleece → spinner → weaver → fuller/dyer → tailor (✓ weaver and tailor)
    - hides + bark → tanner → cobbler/saddler (✓ tanner)
    - grain → miller → baker; grain → maltster → brewer (✓ brewer)
    - clay + fuel → bricks; stone → mason (✓ bricks)
    - wood → sawyer → planks → wheelwright/cooper (missing)
    - salt + fish/meat → preserved food (missing)
    - sand + fuel → glass (missing)
    - copper + tin → bronze (✓ bronze, but never consumed)
  - **Needs:** each household has a daily needs basket in kg (firewood ~1.5 kg per person per day, ×2 in winter). Clothing and tools wear out on durability clocks. Fresh fish and meat rot in about 3 days unless salted or smoked.
  - **Prices:** merchants are real people holding stock and cash.
    - `ask = ref × (target_cover / cover)^0.5`, clamped to 0.3–4×.
    - Recomputed **per unit** sold, so large trades move the price against you.
    - A 10–20% spread.
    - `ref` drifts with the local coin per person.
  - **Logistics:**
    - Cart ~500 kg, ox wagon ~1 t, pack horse ~100 kg.
    - Merchants and guards are hired people.
    - A trip runs only if the expected margin beats its cost; an 8 km leg by cart takes about 18 game hours.
    - Tolls are charged at gates and bridges.
  - **Money:**
    - 1 crown = 1.3 g of silver. From Phase 7 the crown becomes the silver piece, with a copper piece worth 1/10 and a gold piece worth 10. Sums are then stored in copper, and older saves are converted at load (×10).
    - Mints take 5–10% seigniorage.
    - Seed ~50 crowns per person at world generation (~1.25 M crowns, ~1.6 t of silver).
    - Silver output is tuned to grow the money supply 1–2% per year.
    - Treasuries are chests.
    - Taxes are transfers between purses: hearth tax ~2 crowns per household per season, market toll 5%, gate tolls, and tithe in kind.
    - Dividends are paid from profit, out of the business's cash box.
  - **Items:**
    - Value = kg of material × its price + labour hours × wage × skill premium.
    - One 5-tier quality scale.
    - Shops sell what workshops actually made.
    - Commissions reserve a smith's time and materials, and you pick the item up in person.
  - **Wages** (crown ≈ penny):

| Job | Pay |
|---|---|
| Labourer | 1–2 crowns/day |
| Skilled craftsman | 3–4/day |
| Soldier | 2–3/day |
| Caravan guard (danger premium) | ~15–20/day |

    A town lot costs ~300–600, so adventuring and trade, not wage labour, is how a wanderer gets ahead. That is intended.

### 4A.2 Settlements and population
- **Keep:**
  - Hierarchical site scoring and placement.
  - The flavour ladders in `BuildingData.gd`, used as physical descriptions of what each building looks like.
  - Fertility gated on food security.
  - Migrants who walk real paths.
  - The governor's utility scorer, reused as the lord's planning brain.
  - The four named tax levels.
- **What's broken:**
  - **Multi-tile settlements run their daily update once per tile.** They are stored under every footprint tile (`ProvincePhase.gd:217-221`), and the daily loop visits each entry (`GameState.gd:977-982`). A metropolis runs about 25 times a day.
  - `get_storage_capacity()` doesn't exist.
  - Stipends read `s.faction_id`, but the field is called `faction`.
  - Type renaming to "capital" or "satellite" breaks NPC creation (no king is ever created) and promotions.
  - **Growth** is `int(pop×0.0001) + 2` per day (`ConsumptionSystem.gd:113-119`). There are no natural deaths, and starvation has a +2 floor.
  - Houses never get built, and construction uses crowns only.
  - Recruits and garrisons appear from nothing.
  - Founding a settlement spawns 50 people out of nowhere.
  - Rebellion is documented but doesn't exist.
  - Swearing fealty hands you a settlement taken from its lord, silently.
- **Redesign:**
  - **Households are the unit.** About 5,200 households (towns ~4.2 people, villages ~5.3, plus servants and lodgers) are created at world generation.
    - Ages follow a life table: 0–14 = 36%, 15–49 = 48%, 50+ = 16%.
    - Kinship links (parents, spouses, siblings) support inheritance and Ironman heirs.
    - Each household has a dwelling, a deed (owned, rented or tenant) and a job.
    - Class is derived from occupation and property: gentry ~35 households, burghers ~2,500 people, the rest peasants. Social mobility emerges from that.
  - **Vital rates per game year**, divided by 60 for the daily chance:
    - Death chance by age: age 0: 20%; ages 1–4: 3%; 5–14: 0.8%; 15–49: 1.2%; 50–64: 3%; 65+: 10%.
    - Hunger or crowding multiplies these by 1.5–3; town disease by 1.2.
    - Married women aged 18–44 have a 30% yearly fertility chance, scaled by food security. Pregnancy lasts 45 game days.
    - **The brake:** a couple marries only when they can get a dwelling (the European marriage pattern), so housing and land regulate population.
    - Inheritance goes eldest son → children → spouse → back to the lord (escheat).
    - **Test:** 100 simulated years stay within ±10% of the starting population with no player input.
  - **Famine:** each person has a nutrition reserve of about 30 game days. Malnutrition raises disease deaths first; the poor die first. A granary is storage volume, not a magic modifier. The HUD shows a starvation timeline.
  - **Housing:** capacity is beds in dwellings recognised by `RoomDetector`:
    - cottage 4–7, longhouse 6–10, burgage townhouse 6–12;
    - overcrowding gives health and mood penalties;
    - the homeless sleep in barns or at the inn.
    
    Migrants are visible people on the road, choosing destinations by vacancies, jobs and rumoured land.
  - **Buildings:** no more "levels" or slots.

| Old building | Becomes |
|---|---|
| farm | acres of fields + plough teams + a barn |
| lumber mill | sawpit, later a water-powered sawmill |
| walls | actual wall segments |
| granary | storage volume |
| market | a square with rentable stalls |
| cathedral | chapel → parish church → minster |

    - Build limits come from free parcels, which the lord sells.
    - **Background growth (from Phase 3):** the abstract world builds by itself.
      - When a settlement is crowded and land is free, households put up cottages on vacant parcels. Cottages cost materials (poles, daub, thatch) bought or gathered, and worker-hours.
      - Households clear new fields (assarting) from woodland or waste, drawing down the woodland stock.
      - When demand is high, new businesses open on empty lots.
      - Population can therefore creep up toward the land's carrying capacity at a fraction of a percent a year, while marriage stays gated on housing.
      - New buildings appear in chunks the player has never seen as generated content, and in chunks already visited as ID-keyed `ChunkDelta`s (§2.2), so visited places never change silently.
      - Phase 5 turns this into visible construction jobs.
    - The lord's planner posts paid construction contracts with a blueprint, a materials list and wages. Example: an 8×5 m cottage takes ~250 worker-hours, ~40 poles, 1.5 t of daub and 3 t of thatch.
  - **Unrest** is the share of households with grievances (hunger, taxes, crowding, crime, the lord's reputation). The ladder: rumours → tax and levy evasion → a riot by real people → a revolt that changes who holds the fief.
  - **Government:**
    - A fief is a deeded set of parcels plus the rights to tax, levy and judge.
    - The lord is a Person who lives in a manor.
    - Villages have a reeve; chartered towns have a mayor and council.
    - A town is a village that has been granted a market charter.
  - **Taxes:** real transfers (rent per acre, hearth tax, tithe, market tolls) collected by the reeve. Arrears lead to debt, then eviction. Higher tax levels create more grievances.
  - **Sponsorship** means funding a contract, which earns patron reputation and naming. Donations go into a real store. Shares are equity in a Business that has its own ledger.
  - **Founding:** a charter is a deed to wild land. You recruit real households, who carry their own goods. It becomes a village once it has 80+ people and a chapel or mill.
    - **Charters come from rulers (decided with the user):** the king or a landholding faction's ruler grants a founding charter, with a site, a deadline and dues; you petition for it. A market charter, granted separately, makes the settlement a town. The deed and the founding flow come in Phase 5; the political decision and revocation in Phase 6.
  - **Recruits** are idle young adults who actually live in the settlement. The levy is one man per 10 households in peace, one per 5 in war, and each levied man is taken out of the harvest.

### 4A.3 Combat and characters
- **Keep:**
  - The tissue stacks and internal organs behind their parent part.
  - DF-style material yields; contact area against penetration.
  - Several attacks per weapon; the flail ignoring shields.
  - The battalion pivot with "anchor braking"; spatial buckets.
  - The descriptive log verbs.
- **What's broken**, beyond the five bugs already listed in §2.6:
  - **AI attacks always target the torso** (`BattleCombat.gd:103,109`), and the only location roll is never called.
  - **Weapon volumes in the data are far too large**, so weight (volume × density) gives a 30 kg steel longsword (`GameData.gd:1321`).
  - Velocity comes from a turn delay, so fast units are nearly unhittable.
  - Energy isn't conserved, so every cut that gets through armour cripples the limb.
  - Blunt damage never passes through armour.
  - **Any neck blow reaching the bone through 3+ tissues counts as a decapitation** (`GameData.gd:580-582`).
  - Any brain damage kills.
  - Facing is never used.
  - Looting adds one item per slot it covers: one gambeson becomes 9.
  - **Archers fire once per battle**, because nothing counts down their reload timer.
  - **Projectiles are never removed**, because the removal list is ignored (`BattleController.gd:371`).
  - Arrows home in on their target.
  - The siege AOE callbacks are swapped, which crashes.
  - Walls only ever take a default 40 damage.
  - Pain is never added up.
  - **Blood resets to full every battle, but bleed rates carry over.**
  - Blood never regenerates.
  - Broken ribs after a battle count as "died of wounds".
  - The commander is immortal.
  - Healing is a flat 0.25 HP per hour; a severed nerve heals in 8 hours.
  - **Morale is never updated.**
  - Abstract battles reduce a single HP number and leave bodies untouched.
  - XP points are never spent, and **survivors are renamed to their rank** (`BattleController.gd:1424`).
  - Character creation lists scenarios, professions and traits that don't exist in the data.
  - Prisoner ransom is paid from nowhere.
  - The tournament prize is never paid.
- **Redesign:**
  - **Body plans as data.** Each part has a size and an area. Tendons, nerves and vessels are *structures inside* muscle, hit with probability by area. Blood is ~5,000 ml, scaled by body mass.
    - **Human detail (decided with the user):** about 25 parts are added to the Phase 1 plan, each one chosen because it changes play:
      - **eyes:** a lost eye hurts aim and spotting, both eyes blind you;
      - **skull and ribs:** bone shells that guard the brain, heart and lungs, and can be broken or caved in;
      - **jaw and teeth:** a broken jaw stops eating solid food and speaking clearly;
      - **thumb and fingers**, grouped: grip, weapon handling and two-handed use;
      - **ears:** hearing, which matters for spotting and stealth.
      - **nose:** a broken nose bleeds into the airway and slows stamina recovery;
      - **collarbone:** broken, the arm can't be raised, so no overhead blows and the shield is held low;
      - **pelvis:** a bone shell for the bladder and intestines; broken, you can't walk;
      - **kneecaps:** broken, that leg can't bear weight.
      The chin is part of the jaw. Cheeks and forehead are covered by the skull and face. Every finger, toe and tooth individually is not modelled.
    - **Soft tissue (decided with the user):**
      - **Ligaments** at the six main joints (shoulder, elbow, wrist, hip, knee, ankle): a torn ligament makes the joint unstable, slowing movement, weakening grip and giving a chance to fall under load.
      - **Separate motor and sensory nerves** in each limb section.
      - **A spinal cord** in the neck, upper body and lower body.
      - **Graded muscle damage:** bruised, torn or mangled, weakening the part in proportion, rather than whole or ruined.
  - **Resolver, armour, layers and hit chance:** see §2.6 (the single spec).
  - **Real-time melee:**

| Weapon | Wind-up | Recover |
|---|---|---|
| Dagger | 0.30 s | 0.30 s |
| Sword | 0.45 s | 0.40 s |
| Axe | 0.60 s | 0.60 s |
| Maul | 0.90 s | 0.90 s |
| Spear | 0.40 s | 0.50 s |

    - The strike window is 0.10–0.15 s. Timings scale by ×(1 − skill/400) and get +1% per kg of over-encumbrance.
    - **Shield block:** a 100° frontal arc. A heater covers 0.45, a tower shield 0.75. A shield wall adds +0.10 per braced neighbour, up to 2.
    - **Parry:** a 0.20 s window; success chance = skill/120; a successful parry opens a 0.4 s riposte.
    - **Dodge:** 0.35 s with a 1.0 s cooldown.
    - **Feint:** cancel during the first half of a wind-up.
  - **Stamina** (100):
    - Costs: a swing costs 4 + 2 per kg of weapon; a block costs 3 + 1 per 50 J absorbed; sprinting costs 8 per second.
    - Regeneration: 12 per second resting, 5 per second guarding or in more than 20 kg of armour.
    - Below 25, wind-ups take +30% longer and blocks lose 0.2 coverage.
  - **Blood** (ml per sim-second): capillary 0.5, muscle 3, vein 15, artery 80–120, liver or spleen 20 (internal).
    - Minor bleeding clots with a 90 s half-life; arteries never clot.
    - Pressure or a bandage cuts the rate by 80% (a 4 s action); a tourniquet by 98%.
    - Shock by blood lost: <15% no effect; 15–30% weakened; 30–40% collapse; 40–50% unconscious; >50% dead.
    - A femoral artery wound downs a man in ~18 s and kills in ~30 s. An arm vein downs in ~110 s, so bandaging during battle matters.
    - **Pain** is a pool that decays 2% per second. Knock-out happens when pain exceeds 60 + 4 × toughness.
    - Heart wounds collapse a man in 5–10 s. The brain kills only when destroyed; otherwise a concussion knocks you out.
    - Bleeding keeps running 1:1 in sim-seconds after combat while any wound is open.
  - **Wounds are records:** open → dressed → stitched (surgery check) → healing → scar.
    - Infection risk is 8% per game day while a wound is undressed.
    - A set fracture heals in 20 game days. An unset one takes 35, with a 50% chance of malunion (−2 dexterity or strength).
    - **Severed tendons stay severed unless surgery at skill 50+ happens within a day. Nerve and spinal damage is always permanent, Dwarf Fortress-harsh (decided with the user):**
      - a cut motor nerve paralyses the limb and everything below it;
      - a cut sensory nerve removes feeling and pain there, and weakens blows struck with it;
      - spinal cord damage in the neck or back paralyses everything below the injury.
      Crutches and splints are items that restore some movement.
    - Amputation happens only from a real blow or a surgeon's choice.
  - **Morale** (0–100 per soldier, updated at 2 Hz):
    - −6 for each friendly death within 10 m (decaying over 15 s).
    - −15 when flanked.
    - −2 per second for each routing neighbour.
    - Minus the soldier's own pain and blood loss.
    - Plus for a nearby captain and banner.
    - Thresholds: rout below 20; surrender below 10 when cornered; a captain can rally above 40.
  - **Ranged:** arrows fly at 55 m/s, bolts at 65 m/s. Aim error cone σ = 3° × (1 − skill/120), and a projectile hits whoever is in its path. Reload: longbow 5 s, crossbow 10 s. Quivers are finite.
  - **Sieges:** wall segments have HP stored in `ChunkDelta`; engines have dispersion cones; crews take cover.
  - **Abstract battles:** a Lanchester exchange whose quality factor comes from armour and skill. The output is per-soldier wounds, knock-outs, routs and captures, sampled from tables built by the tactical resolver. A golden test keeps abstract and tactical results within ±10%.
  - **Progression:**
    - Skill gain per meaningful use = 0.4 × difficulty × (1 − s/100)². A recruit goes from 20 to 40 in about 6 fights.
    - Attributes gain +0.02 per game day of matching activity.
    - Injuries can lower attributes permanently.
  - **Encumbrance:** capacity = 15 + 2.5 × strength in kg. Over that, stamina costs rise and speed drops.
  - **Traits** are data modifiers on resolver hooks (for example, hemophiliac: clotting half-life ×4).
  - **Prisoners:** ransom is paid from the captive's faction treasury. Prisoners eat and can escape.
  - **Tournaments:** blunted weapons (×0.5 momentum, edges count as blunt), yield at pain knock-out, and the purse comes from the town treasury.

### 4A.4 World, factions and AI
- **Keep:**
  - The phase pipeline with a shared context.
  - Flow accumulation for rivers.
  - Rain shadow.
  - Strata layers that drive minerals.
  - The ore-by-geology table.
  - Avoidance of threats.
  - Settlement buy orders.
- **What's broken:**
  - **Generated factions never reach the game.** A Dictionary is iterated, which yields the id strings (`GameState.gd:694-695`).
  - **Provinces read river data that has already been erased** (`ProvincePhase.gd:15` vs `BiomePhase.gd:95`), and their flood fill stalls: in a replica it claimed only 17 of 400 sectors. Most settlements end up "Wild".
  - Sea level is 0.32 in one phase and 0.35 in another.
  - The silt bonus can never fire.
  - Each tile holds only one resource.
  - Roads are a tree with no loops and overwrite river tiles.
  - **Wars are dice:** 12% per week to declare, 5% to end (`WarManager.gd:22-29`), so ~71% of faction pairs end up at war.
  - Military campaigns are cosmetic.
  - Relations mix strings and numbers.
  - AI movement is a greedy step that calls the missing `is_walkable`.
  - The AI sees the player through terrain.
  - Defeated lords respawn with 20 recruits from nothing, and bandits respawn from nothing.
  - `ai_config.json` doctrines are ignored.
  - **Quests are never completed, failed or expired.**
  - Mercenary contracts compare their expiry against the day of the month.
  - Ruins are a "pure currency" faucet.
  - Hunting always succeeds, foraging is infinite, and every world has the same herds.
  - Names: 26 × 18, all male.
  - The calendar is 360 days.
- **Redesign:**
  - **Region generation at full detail** (Stage 3 of §1A; the continent and history stages come first):
    - Coarse continental heights refined with fixed-point (Q16.16) detail noise on the 500×500 tile grid. Generated ridge lines come from the continent's plate boundaries; nothing is hand-placed.
    - Priority-flood depression filling, then integer D8 flow.
    - A stream starts at ~2 km² of catchment; a river (15–40 m wide) at ~50 km².
    - Rivers and roads are stored as polylines with width and depth.
    - Soil fertility comes from slope, drainage and alluvium.
    - Mineral veins are a separate layer with tonnage.
  - **Settlements:** towns come from history (§1A). Villages and farmsteads are filled in around them by Poisson-disk placement: 40–80 villages 3–4 km apart, then 150–300 farmsteads. Each village has about 15 acres per household of fields within ~1 km, laid out as parcels. A fief is a manor plus its parcels; the region holds parts of 1–3 realms whose borders come from history.
  - **Roads:** a relative-neighbourhood graph, so roads form loops. Highways link towns; tracks link villages. Fords and bridges are marked at river crossings.
  - **Split between world generation and chunks:**
    - *World generation (tile resolution):* heights, rivers, soil, forest, all polylines, settlement plans (lots, house footprints, fields), all Persons, realms, fiefs, lords, landmarks and ruins.
    - *Per chunk (generated on first visit, then snapshotted):* rasterised roads and rivers, hedges and ditches, building geometry, trees, rocks, flora, dens and cellars. Each chunk's RNG is `hash(seed, chunkId, stream)`.
  - **Diplomacy:**
    - Integer opinion between −100 and 100.
    - Treaties (truce, alliance, vassalage) have expiry days.
    - War utility = claim + casus belli (a chronicle event) + strength ratio + treasury + season − harvest cost.
    - Peace comes through war score and war-weariness.
    - Every declaration is a chronicle entry with its cause.
  - **AI lords and armies:**
    - Utility AI with its considerations in JSON.
    - Recruits are real Persons: levies, or existing mercenary companies.
    - The dead stay dead. Defeated lords are captured and ransomed.
    - The AI acts only on last-known positions from scouts and rumours, with their age.
  - **Campaigns:**
    - Marching 8 km takes ≈ 34 game hours, so a season allows about one campaign plus a siege.
    - Targets are scored as value ÷ (march days + siege days).
    - Armies muster after sowing.
    - Supply is 1 kg of grain per man per day: 1,000 men for 15 days need 15 t, about 30 carts, or the army forages the enemy's land.
    - Men levied during the harvest window cost their households' yield.
    - AI lords hold or disband for the harvest unless the war is worth the shortfall.
  - **Caravans** are carts owned by merchant Persons. They act on prices they have seen or heard, with an age attached. A lost caravan is lost capital.
  - **Contracts:**
    - Generated from real deficits and chronicle events.
    - Pay is held in escrow from the issuer's purse.
    - Deadlines are in game days.
    - Completion is verified from events, and failure has consequences.
    - Granting a fief is a political act, recorded in the chronicle.
  - **Ruins:** see §3b. Treasure is real buried hoards of coin that once left the economy (no minting), and loot is finite. Monsters are rare and only deep down.
  - **Hunting and foraging:**
    - Each tile has an animal carrying capacity that regrows by season.
    - Hunting uses tracks, stealth and real combat, and dangerous animals attack.
    - Foraging takes time and skill and depletes the tile.
    - Terrain speed multipliers: road 1.0, field 0.8, forest 0.5, marsh 0.4.
  - **Chronicle:** typed events with cause IDs and witness sets, which feed rumours.
  - **Names:** per-culture pools of 150+ names per sex, plus patronymics and place-names. That is enough for 25,000 unique people.

### 4A.5 Scheduling
Everything in this audit is scheduled in §5.

## 5. Phases (breadth first; the single source of truth for scheduling)
Estimates assume one developer working with an AI assistant. Every phase ends in a playable build, a fun check and a **code and bug check** (§6). Every other section's "when" refers here.

| # | Phase | Estimate | Cumulative (midpoint) |
|---|---|---|---|
| 0 | Foundations, art toolchain, risk spikes | 5–7 mo | ~6 mo |
| 1 | The world: continent, history, region, levels, blueprints | 12–16 mo | ~20 mo |
| 2 | Economy, farming, the abstract world, seamless continent | 7–10 mo | ~29 mo |
| 3 | People, autopilot v1 | 9–11 mo | ~39 mo |
| 4 | Combat anywhere and exploration | 9–12 mo | ~49 mo |
| 5 | Property, building, crafting, businesses, vehicle designer | 7–10 mo | ~58 mo |
| 6 | Society, factions, rule, faith and family | 8–11 mo | ~67 mo |
| 7 | Armies, war at scale, ships and naval war, industries | 16–22 mo | ~84 mo |
| 8 | Depth, onboarding, polish, 1.0 | 8–12 mo | ~94 mo |

**Total: about 6.5–9 years (80–109 months).** Industries (Phase 7) added about 1–2 months. The vehicle designer and ships (§3h) added about 4–7 months, autopilot (§3i) about 1 month, and the seamless continent (§1B) about 2–3 months.
- The concept book's first hour becomes playable end to end at the end of Phase 5, about 4.5 years in.
- **After 1.0, as free updates:** C# script mods, controller and Steam Deck support, and anything cut by the overrun rule (§7).

### Phase 0: Foundations, art toolchain, risk spikes (5–7 months)
- **Infrastructure:**
  - The C# solution; Godot 4.7 .NET.
  - CI: build, `dotnet test`, Godot headless import, key-map conflict check, art and palette validation, schema validation.
- **Sim core:**
  - Counter-based RNG.
  - A clock with explicit units.
  - The event bus.
  - Chronicle and cause primitives.
- **Content (§2.9):**
  - JSON with comments plus schemas, the binary cache, migration tables.
  - The mod loader with its conflict report.
  - Converting the old data into `content/base`.
- **Materials and shapes data, with the derived-stats tables (§2.10).**
- **Saves:** chunk snapshots, ID-keyed deltas, z-levels, versioning, migration, background saving (§2.11).
- **App shell:**
  - Input contexts and the rebinding UI.
  - The pixel UI theme.
  - `tr()` strings.
  - Dev console and sim inspector shell.
- **Art toolchain (§2.7):**
  - Sprite baker, palette validator, autotile generator, rig frame baker (3 views).
  - Style sheet v2, building on the "Ravensguard Tileset" v1, approved by you.
- **Risk spikes,** each ending in a written go or reshape decision:
  1. **Scale-model notebook:** travel times, food and wages per trip, march times, levies.
  2. **Melee feel:** a real-time duel and a 10-vs-10 using resolver v1 (the DF momentum rules, calibrated against the DF wiki's worked examples), the rig animation, 3-view sprites, block, parry and stamina.
  3. **Crowds:** 2,000 layered, animated paper-dolls in a battle bubble, plus 500 chunk images, on the reference PC.
  4. **Persistence:** visit chunks, change content, reload, and confirm visited places are unchanged.
  5. **Levels:** rendering and pathfinding across z −2…+2 with stairs, ladders and a collapse.

**Done when:** CI is green; every spike has a decision record; save → load → save is byte-identical on a populated test world.

### Phase 1: The world (12–16 months)
- **Continent stage (§1A):** plates, climate, rivers, biomes, minerals, cultures, realms. Weather fronts and seasons at continental scale (§3c).
- **History v0:** a deliberately simplified rule set for settlement growth, war and peace, notables, dynasties, artefacts, disasters and ruins with causes. It is recalibrated to the live rules in Phases 6–7.
  - Typed legend events.
  - A **legends viewer** and the first Codex legends.
- **Embark map** and region choice; the world library.
- **Region stage:**
  - Terrain refinement, river and road polylines, fields and parcels.
  - Towns from history; villages and farmsteads filled in around them.
  - **All persons in households, with kin and assigned dwellings and beds.**
  - Site placement (ruins, caves, mines, barrows, battlefields; contents come in Phase 4).
  - Landmarks.
- **Levels (§2.2a):** z −5…+3 storage, the level view, portals in pathfinding.
- **Blueprints (§2.9):**
  - The format, room templates and culture styles.
  - The **dev blueprint editor**.
  - **RoomDetector v1**.
  - CI blueprint checks.
  - Enough blueprints to build every settlement type.
- **Presentation:**
  - Tiles, autotiles and props; roofless buildings.
  - **Lighting**, day and night, day length, seasonal palettes, weather visuals.
- **Hero and interface:**
  - Hero movement on HPA* with level portals.
  - Camera bands, time controls, the HUD, the world map.
  - Quick Start with origins and ambitions.
- **Content:**
  - 3–8 culture packs (names, styles, laws).
  - About 60 blueprints and room templates.
  - About 150 terrain and wall tiles.
  - About 20 site templates (layout only).

**Done when:**
- With a library world you're walking in under 60 s; a new world is ready in under 3 minutes, with history taking ≤ 60 s.
- Town-to-town travel times match §1 within 10%.
- Ruins, grudges and family lines trace back to history events.
- The same seed gives the same continent, history and region hashes on Windows and Linux.
- The reference PC holds 60 fps at street zoom.

### Phase 2: Economy, farming, the abstract world, seamless continent (7–10 months)
- **Money and markets:** the closed money supply, mints, purses and chests; markets with per-unit pricing.
- **Production:** workers with employers and workplaces; **abstract business recipes**; resource stocks per tile (§4A.1).
- **Farming (§3a):** fields, rotation, seasons, harvest, granaries; weather's effects on farming and the economy.
- **Households:** needs baskets; item mass, value, spoilage and containers (§2.10).
- **Population:** vital rates; **marriage gated on dwellings**, using the Phase 1 dwelling model; famine.
- **The wider world:**
  - The abstract tick.
  - Continent tick v1 (trade, migration and news into the region).
  - Caravans: abstract on the road, physical in the bubble via the LODBridge.
  - Faction treasuries; basic tax transfers.
- **Seamless continent (§1B):** records keyed by region; border gates from the continental road network; neighbour regions generated ahead of the hero on a worker thread; chunks streaming across borders; detailed versus daily versus abstract region ticks; per-region save sections with paging; the world map for the whole continent.
- **Content:** about 120 materials and items; about 40 business recipes.

**Done when:**
- 1-year and 100-year headless soaks pass: stable prices, no collapse, population within ±10%, coin conserved except documented sources and sinks.
- Caravans pass visibly through the region.
- **You can walk across three regions in a straight line with no stop and no frame over budget.** Roads and rivers continue across each border. Regions generated in any order give the same hashes, on Windows and Linux.

### Phase 3: People, autopilot v1 (9–11 months)
- **Behaviour:** live behaviour, utility AI, schedules across levels; the job and reservation system, including **paid odd jobs** (haul, guard, escort).
- **Talking and trading:**
  - Dialogue templates and barks (about 300 lines).
  - **Rumours v1**, spreading from the chronicle.
  - Trade, inventory and character screens.
- **Living in the world:**
  - **Clothing layers, wetness, warmth and exposure.**
  - Butchering.
  - Recruiting and the squad.
  - Skills that improve with use.
  - Travel activities (road encounters, night camp, foraging).
  - Feasts and fairs.
- **Riding horses, and carts (§3e):** fixed template carts and wagons built from vehicle blueprints (§3h), no designer yet.
- **Basic crafting (§3g):** fire, torches, bandages, simple meals, simple repairs; recipe steps, including unattended ones such as rising dough; practice recipes; the crafting screen; unfinished items; learning from a master.
- The first unique NPCs.
- **Onboarding v1 (§3f):** contextual hints, the controls overlay, the practice yard, mentor NPCs.
- **Autopilot v1 (§3i):** the hero and party members on the NPC utility AI with directives (work, trade route, travel, train, guard, escort, rest), limits and interrupts; zero-player watching.
- **Background growth (§4A.2):** cottages on vacant parcels, assarted fields and new businesses built by the abstract world, so population can grow slowly toward the land's capacity.
- **Private testers start**, with crash reports (§2.11).

**Done when:** residents keep schedules across floors; you can talk to, trade with, hire and recruit anyone; a rumour traces back to its source; testers can get through the first ride and the first odd job without help.

### Phase 4: Combat anywhere and exploration (9–12 months)
- **The resolver:** the unified resolver (§2.6), with armour layers and gaps; the finer human body (eyes, skull, ribs, jaw and teeth, thumb and fingers, ears; §4A.3).
- **Fighting:**
  - Real-time melee (timings, block, parry, dodge, feint, stamina).
  - Ranged weapons; thrown and improvised weapons.
  - 3-view animation and wound overlays.
- **After the blow:**
  - Bleeding, pain and healing (§4A.3).
  - Morale and rout.
  - Knock-outs and capture, with captivity by bandits and armies only (jail and slavery come in Phase 6).
  - Escapes.
  - **Ironman succession** by kinship. Property passes over fully in Phase 5, reputation in Phase 6.
- **Mounted combat** at skirmish scale.
- **Feel:** feedback effects, placeholder audio, slow motion.
- **Flavour text** from layer traces with localisable templates; the filterable log and after-fight report.
- **Watch mode (§3i):** hand any fight to the AI in real time, with hero combat stances, and take back control at any moment.
- **Bigger picture:** the abstract battle resolver v1; combat cause chains.
- **Exploration (§3b):** site contents for caves, mines, ruins, crypts, barrows and battlefields; hazards; torches and light; traps; the auto-map; **2 monster types**.
- **Weather in combat.**
- **Content:** about 18 weapons across materials, about 40 armour pieces, about 30 underground room templates.

**Done when:**
- A seeded fight is deterministic.
- Near and far soldiers produce statistically equivalent outcomes.
- A called shot through an armour gap works.
- Defeat leads to a capture you can escape.
- A crypt crawl by torchlight ends with an injury far from a surgeon that matters.

### Phase 5: Property, building, crafting, businesses, vehicle designer (7–10 months)
- **Land:** `LandRegistry`, deeds and claims; owning and leasing fields; barns, granaries and mills.
- **Building is hands-on, Kenshi style (decided with the user).** Placing a plan only makes ghost pieces. Every piece is built by a person doing the work: the hero by hand, which trains the building skills, or the hero's own people and hired workers through the job system.
  - Materials must physically be on site, bought, gathered or carted in. Work stops when they run out or wages go unpaid, and anyone can resume it.
  - Half-built work stays in the world. Commissioning a builder is the same work done by NPCs for pay, never an instant result.
- **The player build mode:**
  - **Both free placement and ready-made plans (decided with the user):** piece by piece on the 1 m grid, or a house plan picked from a library by culture, wealth and purpose (the same blueprints world generation uses, plus any the player saved), fitted to the lot and then built the same hands-on way.
  - Including multi-storey floors, stairs, ladders and trapdoors.
  - **Structural support and collapse**.
  - Save as blueprint.
- **Construction:** construction and hauling jobs; RoomDetector, full version.
- **Businesses:** hands-on or shares; stewards; the Ledger.
- **Crafting (§3g), full system:** tool qualities and workstations, proficiencies, learning by apprenticeship, watching and manuscripts, batches, failure, repair, refitting, disassembly and salvage, commissions, guild secrets; smithing quality.
- **Vehicles (§3h):** the vehicle designer, vehicle blueprints, building and repairing vehicles part by part, driving, getting stuck, river boats (barges, ferries, rowing and fishing boats); wheelwright, cartwright and shipwright proficiencies.
- **Inheritance of property** completes Ironman succession.
- **Content:** about 40 vehicle parts and 12 vehicle templates; about 80 build pieces, about 60 workstations and pieces of furniture, about 80 crafting recipes.

**Done when:** you can design a wagon, have it built, drive it loaded to market and get it stuck in mud; you can buy a lot, build a two-storey smithy, hire a smith, supply iron and see profit while away; you can claim a clearing and build a homestead; the concept book's first hour plays end to end.

### Phase 6: Society, factions, rule (8–11 months)
**Status: closed 21 Nov 2026** (review and close-out: `docs/reviews/phase-6.md`; fun check: `docs/playtests/phase-6-checklist.md`).
- **Crime and justice:** crime and witnesses, bounties, guards, jail, slavery (captivity complete); reputation.
  - **Historically harsh punishments (decided with the user):** fines, the stocks, flogging, branding, outlawry, a hand cut off for repeat theft, and hanging for murder or treason.
  - Maiming goes through the real anatomy and wound system, so a lost hand stays lost.
  - The sentence follows the local lord's and culture's law, and the chronicle records the cause.
- **Family life for the hero (decided with the user):**
  - courting, or an arranged marriage with dowries and alliances;
  - children born and growing up over game years, raised and trained as heirs;
  - spouses and children are real people who can be kidnapped, married off for alliance, or turn against you.

  It feeds Ironman succession (Phase 4) and inheritance (Phase 5).
- **A real faith system (decided with the user; about +1–2 months):**
  - generated religions per culture, with saints, feast days, relics and pilgrimages;
  - clergy are people, and the church holds land and collects tithes;
  - piety and reputation with the church;
  - heresy and excommunication as political tools;
  - superstition tied to the rare monsters (§3b).
  
  History v0 is recalibrated to include it.
- **Contracts v2:** generated from need, paid from escrow, with game-day deadlines.
- **Rumours v2.**
- **Politics:**
  - Diplomacy as utility AI; faction reactions.
  - Sellsword to vassal to lord.
  - Fief zoning, taxes, rent and tithe; the unrest ladder.
  - **Realm and lordly income (§3j):** estates and food rents, vassal dues (service, aids, relief, wardship, marriage, escheat), subsidies granted by councils, tallage, customs, officials (honest or corrupt), loans to rulers.
  - Founding a faction.
- **The chronicle at full depth,** with measured "why" coverage.
- **Heists and jailbreaks.**
- **Fire spread and burning buildings.**
- **History rules recalibrated** to the live rules.
- **Content:** about 40 contract types.

**Done when:**
- Theft in front of a witness raises a bounty with an explained cause.
- You can rise to lord.
- A jailbreak works.
- A thatched street burns realistically.
- Chronicle cause coverage is ≥ 90% specific.

### Phase 7: Armies, war at scale, ships and naval war (15–20 months)
**Status: closed 27 Nov 2026** (built 21-27 Nov, every part merged; the economy audit gate M-E0 closed 22 Nov; specs and build order: `docs/specs/phase7/00-phase7-overview.md`, the user's decisions in its §10; review and close-out: `docs/reviews/phase-7.md`, with what moved to Phase 8; fun check: `docs/playtests/phase-7-checklist.md`).
- **Step 0, an adversarial economy audit (decided with the user, Nov 2026).** Before armies are built, a review red-teams the whole economy and its findings are fixed first:
  - **exploits:** money loops, arbitrage, buy-low-sell-high with no cost, duplication;
  - **collapse under war load:** 2,000 soldiers eating, paid, armed and fed for seasons; levies pulled from the harvest; purveyance; sieges starving towns;
  - **bottlenecks:** iron, charcoal, timber, grain, carts, labour;
  - **coin and mass leaks**, and growth headroom after war.

  It ends in a war-load soak, run before the Phase 7 war soak, and a findings list in the review.
- **Economies of scale for every business (decided with the user, Nov 2026).**
  - **Advantages of size:** division of labour (a master with journeymen and apprentices on the simple steps); shared fixed costs (one forge, kiln, building, cart, heavy tools); bulk buying and bulk carting; water- and wind-powered machines (fulling mills, trip-hammers, bellows-driven furnaces); munition-grade standard patterns made faster than one-off pieces.
  - **Limits:** crowding at stations, a steward's span of control, guild size rules, and market demand.
- **New large buildings for it, in the same blueprint, room and business system as Phase 5.**
  - **The buildings:** manufactory halls with many stations; an armoury, which stores and maintains kit; water-powered mills (fulling mill, hammer mill, sawmill, bellows furnace); a ropewalk; warehouses; barracks; a granary depot for army supply.
  - **What they add:** new build pieces (water wheel, trip-hammer, great bellows), room types the room detector recognises, library plans, and business kinds.
  - **Where they go:** on rivers with enough fall for a wheel, recognised from the region's water.
- **Industries: staged production chains (decided with the user, Nov 2026, from the user's iron, clay and wood sheets).**
  - **Chains of real buildings.** Each stage is a separate building and business with its own crew and wages:
    - iron: mine → crusher and washer → ore yard → smeltery → packing;
    - clay: clay pit → yard → moulding shop (pots, bricks, shingles) → drying sheds → kiln → packing;
    - wood: felling → hauling → timber yard → saw mill → packing → shipping.
    
    Storage, loading and packing are real stages, with hauling throughput that can bottleneck. Packing uses barrels and crates from coopers; packed goods cart better and spoil less.
  - **Tiers 1–3 per stage,** each a ready-made plan built hands-on:
    - mines: hand-hauled → carts on wooden rails → horse gin or water-wheel hoist and pumps;
    - dressing: hand crushing → crusher and washing floor → water-powered stamps;
    - yards: hand loading → barrows and runways → treadwheel crane;
    - smelteries and kilns: one, two, then three furnaces with bellows;
    - saws: pit saw → frame saw → water-powered gang saw.
    
    The level is recognised from what is installed. Each tier sets a minimum crew to run and a maximum crew. Output comes from the labour model (§4A.1 rates, skills and proficiencies), with diminishing returns from crowding.
  - **The Industry tab in the Ledger** shows each chain as a flow: throughput and stock per stage, the bottleneck highlighted, crew sliders, wages, and the upgrade available for each stage.
  - **Selling.** Sell at the gate to passing merchants, or send your own caravan: hire a caravan master and guards, pick wagons and a destination, and see the expected margin from the prices you know, with the risk of the road.
    - Prices come from markets, not fixed tables. Distant towns pay more because they lack the goods, and flooding one market drops its price.
  - **The sheets' numbers are the targets for structure:** the tier jumps, crew ranges and stage ratios. Raw output alone should barely cover wages, and finishing up the chain should be where the profit is.
  - **Balancing rules (decided with the user after reviewing the sheets' math):**
    - **Output per worker is historical:** a miner hews about 0.5 t of ore a day, and a pair of sawyers at a pit saw makes about 100–200 board feet. Water-powered tiers multiply that. An honest "industry output" slider (1x–5x) in the world settings scales it for players who want a faster game.
    - **Inputs are real goods:** charcoal for smelting (about 15 kg per 10 kg of ore, from charcoal burners who cut woodland), firewood for kilns, and barrels and crates for packing. Upkeep is real repair work and materials from wear, not a percentage.
    - **Payback target:** a well-run Level 1 works repays its buildings in about 2–3 game years at default settings. The step 0 audit checks this, along with whether markets can absorb the output and whether merchants have the coin to buy it.
    - **The Industry tab's balance sheet** shows income, wages, inputs and upkeep per day and per working week (6 days), all in one coin unit.
  - **Money: gold, silver and copper (decided with the user).** 1 gp = 10 sp = 100 cp, minted from mined gold, silver and copper. The silver piece is today's crown. Sums are stored in copper, and older saves are converted at load.
  - About 1–2 months, folded into step 0 alongside economies of scale; the coin change adds about 2 weeks.
- **Battle:** everything in §3d.
  - The battle bubble and the 2,000-soldier budget.
  - Formations, command and the tactical pause.
  - **Cavalry formations and charges.**
  - Reinforcement waves.
- **Armies on the map:** columns, supply, camps, levies and mercenaries; **purveyance**, scutage and campaign finance (§3j).
- **Sieges** with engines, wall-walks and towers; breaches; winter sieges. Siege engines are vehicles from the designer (§3h).
- **War wagons and wagon forts; chariot combat** (§3h).
- **Ships at sea (§3h):** harbours, cogs and longships, sailing by wind, sea trade and sea raids from the continent tick, naval combat with missiles, ramming, fire and boarding.
- **AI campaigns.**
- **The abstract resolver**, calibrated against the tactical one within ±10%.
- **Content:** about 10 siege engines and defences, plus Field and Command mini-sprites for every soldier type.

**Done when:**
- 2,000 soldiers hold 60 fps on the reference PC.
- You can lead levies against an army arriving by road and see the consequences.
- An AI faction besieges and breaches your walls.
- **War soak:** several realms at war across the continent for 25 years, with abstract armies on the road graph, levies, supply, foraging and sieges. Their consequences are visible:
  - dearer bread along the march routes;
  - unrest where purveyance was used;
  - refugees, and depopulated border regions.

  But there is no collapse: coin and mass are conserved, and the population recovers within about 15 years of peace. Every war's end traces to war-weariness, money or harvest in the chronicle.
- Raiders land from a ship and you fight them on the beach and on the deck.

### Phase 8: Depth, onboarding, polish, 1.0 (8–12 months)
- **Depth:** the full ambition tree, with autopilot able to pursue ambition steps (§3i); more authored landmarks (~40) and unique NPCs (~30).
  - **Optional endings (decided with the user).** Reaching a great ambition offers to end the run with a chronicle epilogue (for example: found a dynasty, take a crown, clear the deep barrow). The player may decline and carry on; the world stays in the library for new characters.
- **The full bestiary** (3–6 types) and more site templates.
- **The full Codex**; onboarding v2.
- **Polish:**
  - Final audio and music.
  - The accessibility pass.
  - Localisation, including generated chronicle text.
  - Controller and Steam Deck support: **moved to after 1.0** (keyboard and mouse only at 1.0, decided with the user 27 Nov 2026).
  - The balance pass.
- **Modding for players:** documentation, an example mod, and the player-facing blueprint editor.
- **Release:** Steam page; 1.0.

## 6. Verification
- **Reference PC:** as defined in §2.11.
  - Frame and tick budgets are measured locally with `tools/perf` and logged to `docs/perf/`.
  - CI gates on allocations and operation counts.
- **`dotnet test`** (each test arrives with the phase that builds the system):
  - **Determinism:** continent, history, region and chunk hashes on both operating systems.
  - **Saves:** round-trip, migrations, and the persistence-spike scenario as a regression test.
  - **Content:** schema and mod validation, including every condition and effect used in content resolving to the known vocabulary.
  - **Architecture:** no game state in Godot nodes; live ECS iteration is in stable-ID order (determinism tests catch violations); every live entity maps to exactly one record.
  - **Blueprints:** IDs resolve, rooms are reachable, purposes can be satisfied, RoomDetector agrees.
  - **Conservation:** LOD promote and demote conserves people, items, coin and material mass.
  - **Derived stats:** golden tests on the derived-stats tables.
  - **Combat:** seeded fights; near and far soldiers equivalent; the abstract resolver within ±10% of tactical; the hero on watch mode uses exactly the NPC combat AI (same seed gives the same fight); every resolver outcome has a flavour-text template.
  - **Autopilot:** directives and limits are obeyed (spending cap, no crimes); every interrupt kind fires; a year-long autopilot soak keeps the hero alive and solvent in a peaceful region.
  - **Material science:** golden tests reproducing the DF wiki's worked results. Examples: the battle-axe momentum table; mail converting cuts to blunt without dampening; rigid metal soaking 98–99% of a stopped blow; light weapons deflecting off dense armour; severing when penetration exceeds part size.
  - **Vehicles:** design validation (supported parts, steering, pull, draft against river depth, stability); speed against mass, surface and slope; people and items aboard move with the vehicle; part damage and repair; abstract and live travel times agree.
  - **Crafting:** recipe data validation (components, alternatives, qualities, requirement sets, proficiency graph without cycles); unattended steps ruin the batch past their deadline plus grace; proficiency penalties apply below 100%; disassembly returns the recorded components; practice recipes train without producing items.
  - **Levels:** line of sight between levels; pathfinding through portals; invalidation when a ladder is pulled up; **structural collapse** rules.
  - **Fire:** spread through thatch and timber but not stone; rain puts it out.
  - **Weather:** deterministic from seed and date; fords close after heavy rain.
  - **Economy and society:** farming yields per acre against the §3a numbers; vital-rate soak (±10% over 100 years); coin conservation; crime and witness rules; ambition triggers.
  - **Chronicle:** cause coverage measured; history sanity (no accidental extinctions, every ruin has a cause).
  - **Saves:** budgets for size and time on a 10-year soak save.
- **Nightly:** a 1-year soak with and without a scripted player bot; save identity re-checked every phase.
- **Art:** the palette validator; size and outline checks; autotile completeness.
- **In engine:**
  - A headless smoke-boot in CI.
  - Screenshots from a scripted playthrough (`--autoplay`), captured locally or under Xvfb with the Compatibility renderer.
- **Keys:** a duplicate binding in any context (world, combat stance, called shot, build, menus) fails CI.
- **Code and bug check at the end of every phase** (a phase is not done until this passes):
  - **Review:** a review of all code added or changed in the phase. It looks for correctness bugs, determinism leaks (floats, `Math.Sin`/`Pow`, wall-clock time, unseeded random, dictionary or hash-order iteration in the sim), game state held in Godot nodes, missing units on rates, content that bypasses the loader, swallowed exceptions, and tests that can't fail.
  - **Health:** the full test suite and CI are green, with no skipped or quarantined tests. The build has no warnings that hide bugs. A save → load → save round trip is byte-identical.
  - **Close-out:** each finding is fixed, or written into `docs/reviews/phase-N.md` with a reason and the phase it moves to. Nothing is silently dropped.
- **Fun checks:**
  - At the end of each phase, a 20–30 minute checklist session recorded in `docs/playtests/`.
  - From Phase 3, 2–3 private testers, always asked: "did you understand why?".

## 7. Risks
- **Scope (about 6.5–9 years, accepted).**
  - Each phase has an estimate.
  - **Overrun rule:** if a phase runs more than 50% over, stop and move features to the after-1.0 list rather than letting the whole timeline slip.
  - Content volume is scaled separately from systems.
- **No public feedback until 1.0.** This is the biggest remaining risk. Private testers from Phase 3 are the mitigation.
- **Performance** of crowds, levels, lighting and fire. The Phase 0 spikes come first and the §2.11 budgets are measured every phase. If 2,000 soldiers can't hold, the default battle size drops and reinforcement waves carry the rest.
- **Art drawn by Claude.**
  - About 150 terrain tiles, ~200 furniture sprites, ~600 items and ~250 character layer parts in 3 views, plus horses, animals and monsters.
  - Mitigations: one palette and a validator; palette swaps; autotile and rig baking; art produced per phase.
  - If the style sheet isn't good enough, commission the character sprites and keep the tiles.
- **History that is dull or slow.** Mitigations: typed events with authored templates, tuned weights, a curated sample read every phase, and a 60 s benchmark.
- **Emergent chaos.** Mitigations: closed money, conservation tests, soaks with a player bot, measured cause coverage.
- **Brutality turning players away.** Mitigations: legibility rules, warnings, captivity with escape routes, onboarding (§3f), and the question asked of testers.
- **Legacy code.** It is design notes only (§4); every port starts from a written spec plus golden tests.
