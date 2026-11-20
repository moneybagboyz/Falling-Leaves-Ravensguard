# Phase 5 specs: overview, build order, shared streams, keys, saves, open questions and risks

Status: design, written before any Phase 5 code (plan §4: spec, then golden tests, then implementation).
Source of truth for scheduling is `docs/REDESIGN_PLAN.md` §5 Phase 5 ("Property, building, crafting, businesses, vehicle designer", 7 to 10 months). This file ties the eight Phase 5 files together in the style of `docs/specs/phase4/00-phase4-overview.md`: units on every rate, ids in a reserved block, tick placement, golden tests, open questions with recommended defaults. Conventions that still apply: integer or `Fixed` arithmetic in the sim, counter-based `DetRng` streams keyed on game time, distance or ids (never the live tick, except Phase 4's `FightTick`), stable-id iteration, content as JSON through the `ModLoader`, UI as thin views over snapshots, strings through `tr()`, money in **mites** (1 crown = 100 mites).
Plan sections covered: §2.2a (levels, structural support), §2.9 (content, blueprints, RoomDetector), §2.10 (materials, items, tool qualities, wear, repair), §3.8 and §3.9 (property, businesses, stewards, the Ledger), §3g (crafting at full depth), §3h (vehicles: the Phase 5 part), §4A.1 (workers, workplaces, recipes, wages, items, commissions), §4A.2 (housing, construction contracts, background growth made visible), §3.7 (inheritance completing Ironman succession). **Decisions bound here (locked with the user):** building is hands-on, Kenshi style (ghosts only; every piece built by a person; materials physically on site; work stops without materials or wages; half-built work stays; commissioning is NPC work for pay, never instant); build mode offers both free placement on the 1 m grid and ready-made plans from a library by culture, wealth and purpose, fitted to the lot and built the same way. Later decisions kept compatible: full hero family life and inheritance (Phase 6), systemic slavery (Phase 6), plagues reshaping labour prices (Phases 6-8).

## 1. Document map

| File | Owns | Tests |
|---|---|---|
| `items-quality-and-wear.md` | the one 5-tier quality scale (combat Qw/Qa, value, wear, repair caps), masterwork rarity and named artefacts, tool qualities and tool wear, condition bands, rust and rot, the migration from `Content.Quality` | IQ-1 to IQ-14 |
| `land-and-deeds.md` | `LandRegistry`, deeds and tenure, the lot market and prices, leases of fields, buildings and stalls, claims and homesteads, barns and granaries as storage, `LandRules.May` | LD-1 to LD-14 |
| `build-mode-and-construction.md` | piece costs, ghosts and sites, free placement and the plan library, lot fitting, materials on site and hauling, hands-on work, wages and escrow, structural support and collapse, multi-storey, contracts, save as blueprint, background growth made visible | BC-1 to BC-21 |
| `roomdetector-v2.md` | roofs and `Indoor`, incremental detection, dwellings and beds, workshops and stations, storage volume, room value, purpose gaps | RD-1 to RD-12 |
| `crafting-full.md` | one recipe format (Phase 2 recipes unified), one station vocabulary, 40 proficiencies, stations from the world, smithing quality, the live and abstract bridge, repair, refit, salvage, reforge, apprenticeship, commissions, guilds, about 80 recipes | CF-1 to CF-18 |
| `businesses-stewards-ledger.md` | founding, buying, shares and valuation, hands-on running, staff and day labour, stewards, stalls, fairs and shops, profit while away, the Ledger | BS-1 to BS-18 |
| `vehicle-designer.md` | the designer and its stats, part rows, building and repairing part by part, wear and breakage, driving and getting stuck, river boats (barge, ferry, rowing, fishing) | VD-1 to VD-20 |
| `inheritance.md` | the hero as a person row with kin, estates, debts, wills, primogeniture, switching play to the heir, minors and guardians | IN-1 to IN-13 |

About **130** golden tests plus six overview integration tests (section 11). Content volume (plan §5): about 80 build pieces and 60 workstations and furniture (74 piece rows today: about +66), about 80 recipes (34 today), about 40 vehicle parts (19) and 12 templates (7), about 45 room types (31), about 10 new blueprints (a two-storey `smithy_house`, homestead cottages, barns, a wheelwright's yard, a slipway).

## 2. Module map

```
 P1 blueprints, BuildingGrid, RoomDetector v1,       P2 businesses, recipes, markets,      P3 jobs, crafting engine, growth,       P4 resolver v2 (Qw, Qa), falls,
    StructuralSupport, levels and portals               labour market, Ledger, taxes           template carts, horses, onboarding         succession, estates
        |                                                    |                                        |                                       |
        v                                                    v                                        v                                       v
  items-quality-and-wear (L1) ----------------------------------------------------------------------> crafting-full (L3)
  land-and-deeds (L1)          roomdetector-v2 (L1)                                                        |
        |                           |                                                                      |
        +-------------+-------------+                                                                      |
                      v                                                                                    |
          build-mode-and-construction (L2)  ------------------ sites, ghosts, crews ------------------> vehicle-designer (L4)
                      |                                                                                    |
                      +------------------------------> businesses-stewards-ledger (L4) <-------------------+
                                                                     |
                                                                     v
                                                              inheritance (L5)
```
| Layer | Specs | Notes |
|---|---|---|
| L0 | Phases 0-4 (exists) | blueprint machinery, production, jobs, crafting engine, growth, carts, resolver v2, succession |
| L1 | items-quality-and-wear, land-and-deeds, roomdetector-v2 | data and pure rules; independent of each other |
| L2 | build-mode-and-construction | needs land (May, deeds), RoomDetector v2 (after each piece), items (tool wear) |
| L3 | crafting-full | needs items, RoomDetector v2 (stations), and the hero's real pack (step 1, P5-A4) |
| L4 | businesses-stewards-ledger, vehicle-designer | businesses need land, rooms, crafting; vehicles need construction sites and crafting recipes; they are independent of each other |
| L5 | inheritance | reads every holding kind |

### 2.1 What is new and what is extended
| Existing (code) | Phase 5 |
|---|---|
| `Building/RoomDetector`, `StructuralSupport`, `BlueprintPlacer`, `BlueprintWriter`, `BuildingLibrary`, `BuildPieceDef`, `RoomTypeDef`, `PieceKind` | extended (v2 wrapper; support with beams, load and collapse; incremental placement; `build` cost block; room fields; `Roof`, `Beam` appended) |
| `Crafting/*` (Phase 3 engine), `CraftRecipeDef`, `craft_station`, `craft_tool`, `proficiency` | extended (fields, stations from the world, bridge, commissions, guilds, smithing) |
| `Production/Business`, `RecipeBook`, `ProductionSystem`, `LabourMarket` | extended (shares, steward, directives; `RecipeBook` becomes a view of craft recipes; day labour) |
| `Carts/*` (`VehicleBlueprint`, `VehicleMath`, `VehicleTable`, `Stuck`) | extended (part rows replace `WheelPm`; designer; boats) |
| `Hero/Succession` (`SuccessionOrder`, `Estate`, `InheritanceRecord`) | extended (estate lists, settle, switch) |
| `Growth/GrowthSystem` (`BuildProject`) | extended (visible sites in visited chunks) |
| `Goods/ItemQuality` | becomes the only scale; `Content.Quality` and `QualityTable` removed |
| **New** | `Sim/Land/`, `Sim/Construction/`, `Production/Ownership`, `StewardBrain`, `DayLabour`, `StallTrade`, `Crafting/Commissions`, `Guilds`, `Smithing`, `Salvage`, `Carts/PartGrid`, `DesignBook`, `BoatMath`, `VehicleSite`, `Succession/EstateSettler`, `Wills`, `Hero/HeroPerson`; Godot `World/Build/`, `UI/LedgerScreen`, `UI/VehicleDesigner` |

### 2.2 Limits carried into Phase 5 and where each is answered
| Carried limit (source) | Answered by |
|---|---|
| Fair days: content-driven `FairBook` demand and stall tolls (phase-3 review) | businesses 4.5 |
| Items and barter abroad: `AbroadDealing` deals in counted goods only (phase-3) | businesses 4.5 (item rows at `ValueMites`) |
| Crafting effects with no world state (lit fire, cart repair, garment refit) (phase-3) | crafting 4.1 (wired), vehicles 4.3 |
| Landless idle adults rise to about 2,100 over a century (phase-3, growth item 5) | businesses 4.3 day labour; construction `Construct` jobs; soak gate < 800 (BS-18) |
| Autopilot `Tend` is a stub (phase-3) | businesses 4.4 |
| The live tier is "theatre" for scheduled work (`WorkDone` for farm hours only) (phase-3, phase-4 2.2) | crafting 4.4 (workshops live), construction 4.4 (building live) |
| Crafting screen on its own clock, a separate economy for the pack, not in the save (phase-3 crafting Notes) | **step 1 P5-A4** (the hero's pack as real stores and item rows in the running world) and crafting 4.1 |
| Loot and finds do not reach the pack (phase-4 open, exploration) | P5-A4 (the same pack work) |
| The hero is not a person row; death does not switch to the heir or settle the estate in game (phase-4 open, morale) | inheritance 3, 4.3 |
| Weather-driven rest, storm suspension, hero fatigue (phase-4 2.2 "the rest stays Phase 5") | construction 4.4 (rain and frost stop outdoor work; effort fatigue); feast half-days in staff schedules (businesses 4.3) |
| NPCs sheltering change exposure but not position (phase-3) | not a Phase 5 system: construction workers use `Shelter`; the general fix stays with LODBridge persistence, reviewed at P5-I |
| Soil improvement (marling, manuring) as construction contracts (growth G15) | **deferred to Phase 6** (fief zoning and the lord's planner); recorded at P5-I |
| A new street when a settlement's plots run out (growth G3) | deferred to Phase 6 zoning (land Q3) |
| Horse breeding and foals (phase-3 horses Q2) | deferred to Phase 6 (estates and studs); recorded at P5-I |
| Save, load and continue of a whole `WorldRuntime` drifted from the run that never stopped: the continent section was never written, the farming seed-debt table was not grown after a load, and a wound book put into the ledger got daily steps for days before it existed (`docs/reviews/resume-drift-diagnosis.md`) | **fixed after step 1** (branch `p5-resume-fix`): the continent is saved, an older save without it gets a continent built and ticked as a new game's, the seed-debt table grows on its own, each wound book has its own day cursor (an optional key in the wounds section, no version bump); `WorldRuntimeResumeTests` compares every section, land, the hero's person row and crafting names among them |
| Blunt weapons damage plate, rings burst (phase-4 open item 4) | Phase 4 close-out; Phase 5 repairs it (crafting 4.5 `rerivet_mail`, `beat_out_plate`) |
| The treatment screen in the street (the paper doll), physicians and their prices, `medicine/*.jsonc`, `make_crutch`, Treat utility scoring, tourniquet grade drops (phase-4 review, "Still open") | crafting 4.1 (medicine and the crutch as recipes), businesses 4.5 (the physician as a business). **It replaces the auto-dress stop-gap** (phase-4 review: `FieldDressing`, `LiveWorld.DressAfterFight`, `StreetView.AutoDress`, the `wd_*` keys and the "Bind my wounds after a fight" setting): when the screen lands, the after-fight dressing becomes its "dress all bleeding" button and the automatic call and setting go |

## 3. Build order

Each step ends with its golden tests green. Steps with the same number can run in parallel.

1. **P5-A: foundations** (four parallel parts).
   - **A1** the one quality scale, the v8 save bump and its migrator step (so later steps only add optional sections). Gate: IQ-1 to IQ-14 (IQ-2 proves the Phase 4 calibration did not move).
   - **A2** `LandRegistry`, deeds, lot market, leases, claims. Gate: LD-1 to LD-14.
   - **A3** RoomDetector v2. Gate: RD-1 to RD-12.
   - **A4** the hero as a person row (`HeroPerson`) and the hero's pack as real stores and item rows in the running world (crafting from it, loot into it). Gate: IN-1, CF-4, an exploration loot test reopened from Phase 4.
2. **P5-B: construction core** (needs A2, A3): sites, ghosts, piece costs, materials on site, hauling, hands-on work, wages, support, collapse, multi-storey portals. Gate: BC-1 to BC-8, BC-11 to BC-15, BC-18, BC-20, BC-21.
   **P5-E: crafting full** starts here in parallel (needs A1, A3, A4): unifier, stations from the world, smithing, repair and salvage, apprenticeship, commissions, guilds. Gate: CF-1 to CF-18.
3. In parallel after P5-B: **P5-C build mode UI** (free placement, the plan library, `PlanFitter`, save as blueprint; Godot `World/Build/`; gate BC-9, BC-10, BC-19 and the 960 x 540 smoke) and **P5-D contracts and visible growth** (gate BC-16, BC-17, plus the Phase 3 growth soak unchanged).
4. In parallel after P5-E and P5-C: **P5-F businesses, stewards, day labour, stalls, the Ledger** (gate BS-1 to BS-18) and **P5-G vehicles** (designer, part rows, building and repair, boats; gate VD-1 to VD-20; art: about 21 new parts in 4 drawn views plus baked diagonals).
5. **P5-H inheritance** (needs A2, A4, P5-F; can overlap the end of P5-G). Gate: IN-2 to IN-13.
6. **P5-I close-out:** the overview tests OV-1 to OV-6, the first-hour bot end to end, the 100-year soak with day labour and growth sites, the code and bug check (plan §6), `docs/reviews/phase-5.md`, a 20-30 minute fun-check session (build a cottage by hand, open a smithy, drive a wagon into mud), 2 to 3 testers asked "did you understand why?".

Milestones: **M-A "the deed"** (step 1), **M-B "the wall"** (steps 2-3: a cottage built by hand and by a crew), **M-C "the shop"** (step 4 F: the smithy earns while away), **M-D "the wagon"** (step 4 G), **M-E "the heir"** (steps 5-6).
**Estimate against plan §5 (7 to 10 months):** A 0.8 (parts in parallel; 2.6 serial), B 1.5, E 1.5 (parallel to B), C 1.0, D 0.8 (parallel to C), F 1.5, G 1.8 (parallel to F), H 0.5, I 0.7. Serial sum about **12 months**; with the parallel steps run by separate helpers the critical path is A, B, C, G, H, I = **about 6.5 months of code**, plus the art (about 66 piece and furniture sprites, 21 vehicle parts in 4 views, ghost and scaffold overlays, designer and Ledger UI) and integration slack: **plan on 9 to 10 months**, the top of the range, as Phases 3 and 4 landed. The overrun rule (plan §7) triggers at 15 months; the checkpoint is M-B by month 4.
**Cut order if late (overrun rule, plan §7):** (1) ferries and fishing boats (keep barge and rowing boat); (2) named artefacts' names and history lines (keep the Masterwork tier and the flag); (3) partible inheritance (keep primogeniture and wills); (4) vehicle diagonal facings (cardinals only); (5) the 8-transform plan fitting search (the player rotates and mirrors, with the fit check); (6) room value tiers and their mood effects (keep dwellings, beds, workshops, storage); (7) guild membership and dues (keep the "guild secret" flag, not teachable); (8) formal apprenticeship terms (keep Phase 3's per-recipe teaching). **Never cut:** hands-on construction (ghosts, materials on site, wages, resume, half-built work), both placement modes, structural support and collapse, multi-storey, deeds, lot purchase and claims, businesses with profit while away, stewards, the Ledger, the one quality scale with its combat effect, the designer and part-by-part building, getting stuck in mud, barges and rowing boats, inheritance of property, determinism, conservation.

## 4. Done-when mapped to specs and tests

| Done-when (plan §5) | Specs | Proving tests | Integration |
|---|---|---|---|
| **Design a wagon, have it built, drive it loaded to market and get it stuck in mud** | vehicles 4.1-4.4, construction 3.2, crafting 4.7 | VD-2, VD-4, VD-5, VD-17, VD-7; Phase 3 H-7 to H-9 | **OV-1:** an autoplay script designs a four-wheeled wagon with a pivot axle, commissions a cartwright (never instant: at least the sum of part hours at his crew's speed), hitches two oxen (1,300 N), loads 900 kg of grain, drives 8 km toward the market after 15 mm of rain, sticks on the mud road (total 1,200 kg: ratio 1,539, over 1,300), is freed by unloading 500 kg and two pushers (stuck need 1,751 N against 1,900), reloads on firm ground, sells at the market; coin and mass exact |
| **Buy a lot, build a two-storey smithy, hire a smith, supply iron and see profit while away** | land 4.2, construction 4.1-4.6, rooms 4.4, businesses 4.1-4.6 | LD-3, LD-4, BC-15, BC-16, RD-7, BS-1, BS-6, BS-7, BS-9, BS-10, CF-8 | **OV-2:** buy a town plot (462 crowns), place `smithy_house` from the library (fitted to the street), build it with a contractor, open a smithy, hire a smith at 400 mites a day, unload 200 kg of iron bars, leave the region for two seasons, return: the Ledger's season rows show revenue, costs and profit, the profit sits in the home chest, coin conserved to the mite |
| **Claim a clearing and build a homestead** | land 4.4, construction 4.4, rooms 4.3 | LD-9, LD-10, BC-2 to BC-6, RD-5 | **OV-3:** stake a claim outside any fief, clear an acre with two hired hands (90 worker-hours), build `test_cottage` by hand and with them (250.5 worker-hours), sow the acre; by day 60 the claim is a Freehold deed of 24 tiles |
| **The concept book's first hour plays end to end** | land 4.2-4.3, businesses 4.5, plus Phases 3-4 | LD-3, LD-8, BS-13 | **OV-4:** the first-hour bot (Phase 3 `FirstHourBot`, extended): the reeve's notice board lists a lot near 450 crowns; hauling hides pays about 2 crowns a day; the caravan guard contract pays 40; hire a deserter at 20 a week; the ford ambush (Phase 4); the splint; buy nails to sell; with about 190 crowns rent a market stall and earn the first takings; every step reachable without dev commands |
| **Inheritance of property completes Ironman succession** (Phase 5 line) | inheritance 4.1-4.3 | IN-2 to IN-8, IN-11 | **OV-5:** an Ironman hero with a smithy, a leased field and a half-built barn dies; his son of 17 continues with all three, the funeral and wages are paid, the barn site resumes when he orders it |
| Determinism and saves | all | every spec's determinism test | **OV-6:** a 30-day scripted run with construction, a collapse, crafting, a business, a voyage and a succession: save, load and save are byte-identical at five points; the same hash trail on Linux and Windows |

## 5. RNG streams

Phase 2 owns 0x200..0x2FF, Phase 3 0x300..0x3FF, Phase 4 0x400..0x4FF. **Phase 5 uses 0x500..0x5FF**, registered in `Core/RngStreams.cs` with the existing uniqueness test (extended: no id outside its phase's block). Every key is game time, distance or an id; nothing keys on the live tick except where Phase 4's `FightTick` rule applies (0x532, a blow).

| Stream | Spec | Use | Key (entity, time) |
|---|---|---|---|
| 0x500 | land | reserved for Phase 6 disputes; asserted never drawn | - |
| 0x510 | construction | collapse rubble scatter | collapse event id, cell |
| 0x511 | construction | contractor pace jitter +-10% a day | contract id, game day |
| 0x520 | rooms | reserved; the detector is pure; asserted never drawn | - |
| 0x530 | items | masterwork inspiration | crafter, job id x 64 + unit |
| 0x531 | items | named artefact | item id |
| 0x532 | crafting | brittle blade breaks on a rigid stop | `FightTick`, item id |
| 0x540 | businesses | reserved for Phase 6 steward honesty; asserted never drawn | - |
| 0x541 | businesses | reserved for fair visitor jitter; asserted never drawn | - |
| 0x550 | vehicles | capsize of a tender boat in beam seas | `GameSec/20`, boat |
| 0x551 | vehicles | swamping water entry jitter | `GameSec/20`, boat |
| 0x552 | vehicles | abstract barge leg delay | barge id, leg |
| 0x560 | inheritance | the hero's kin at a new game | hero seed |
| 0x561 | inheritance | reserved for Phase 6 disputed inheritances; asserted never drawn | - |
Reused unchanged: 0x221 (abstract tool wear), 0x223 (craft score), 0x271 (growth wobble, also on visible growth sites), 0x351 (stuck), 0x360-0x362 (craft failure, by-products, watching), 0x416 (fall placement, now also for collapses), 0x457 (succession, reserved). Free for growth: 0x501..0x50F, 0x512..0x51F, 0x521..0x52F, 0x533..0x53F, 0x542..0x54F, 0x553..0x55F, 0x562..0x5FF.

## 6. Control keys (one consolidated table)

Source of truth: `src/Ravensguard.Game/Input/InputDefs.cs` as it stands after Phase 4 (re-read for this spec). `InputDefs.All` must equal this table plus the Phase 4 table; CI checks no key bound twice inside a context, and that contexts active together have no key bound to two actions. **Phase 5 adds no World key** (after Phase 4 the only free World keys are 7, 8, 9, 0 and F11; all stay free).

| Context | Keys | Origin |
|---|---|---|
| **Build** (with World; existing: LMB place, RMB remove ghost, Z rotate ccw, X rotate cw, Esc cancel) | **new:** W A S D pan, `,` / `.` piece prev / next, `[` / `]` category prev / next, **P** plan library, **M** mirror, **R** roof over, **Delete** deconstruct brush, **Enter** commit plan, **Ctrl+Z** undo, **Ctrl+S** save as blueprint, **J** site panel, **B** close; Shift with LMB-drag = outline (read as a modifier, as the blueprint editor does) | plan §2.8 + Phase 5 |
| World actions **kept** while Build is open | Space pause, 1-4 speeds, arrows camera, `=` / `-` and the wheel zoom, Home, PgUp / PgDn level, Alt nameplates, `?` / F10 overlay, F5 / F9, F8 | World |
| World actions **suspended** while Build is open | everything else (move, E, Q, Tab, the panels C I P J L F K H, B, M, cart and camp keys, Z sneak, X stance, G, V, T, R, F1-F4, Shift sprint, Enter hint, Esc back, F6, F7) | the same mechanism by which Choice suspends the speed digits |
| **Mounted** (with World; boats use it as carts do) | O drift (walk toggle), Shift hard stroke (gallop), E land (dismount); WASD row and steer | Phase 3, unchanged |
| **Ledger** (modal, alone with Global) | Up / Down rows, Left / Right column, Tab next tab, S sort, `/` search, A alerts only, Enter open, Esc back | new |
| **VehicleDesigner** (standalone, alone with Global, like BlueprintEditor) | LMB place, RMB remove, `=` / `-` and wheel zoom, arrows pan, `,` / `.` part, `[` / `]` category, PgUp / PgDn deck, T rotate, M mirror, Ctrl+Z / Ctrl+Y undo / redo, Ctrl+N / Ctrl+O / Ctrl+S, Tab stats pane, Enter build this, Esc close | new |
Check against the kept World set: no Build key (WASD, `,`, `.`, `[`, `]`, P, M, R, Delete, Enter, Ctrl+Z, Ctrl+S, J, B, Z, X, Esc, LMB, RMB) is in it; CombatStance is never active with Build (build mode is refused in combat and closes when a fight starts), so Ctrl+S (stance dodge) cannot clash. New verbs without keys (Q menu and E): Build this piece, Haul materials here, Hire a builder, Stake a claim, Pull up banner, Buy / Lease / Sell, Open a business, Hire for, Hire as steward, Commission, Take me as apprentice, Make a will, Repair (vehicle), Launch. The `?` overlay gains the Build, Ledger and VehicleDesigner pages (built from `InputDefs`, Phase 3 fix).

## 7. Saves

**One version bump: v7 -> v8**, with one `SaveMigrator` step, because three stored meanings change:
1. the fight record's quality bytes (keys 6, 25, `WornQualities`) are remapped from `Content.Quality` to `ItemQuality` (items 6; byte 0 Standard -> 2 Common);
2. the "mounts" section's vehicle rows lose `WheelPm` and gain part rows expanded from their template (vehicles 3.3);
3. the hero becomes a person row: the people diff gains the hero's row and household, the hero section gains `PersonIndex` (inheritance 3); `InheritanceRecord.DeedsPending` becomes `Deeds` (empty in every v7 save).
New **optional** sections (absent when empty, so a world that never used them keeps its bytes): `land` (deed change log, claims, leases), `construction` (sites, ghosts, structures, contracts, groan timers), `ownership` (shares, stewards, directives, alert state), `designs` (the text and hash of every player building plan and vehicle design the save uses), `wills`. Extended by additive keys: `crafting` (session, commissions, apprenticeships, guild memberships), `growth` (a project's site id), `succession` (estate lists). Round trip test: save, load, save byte-identical with every section populated (OV-6). Save growth budget: section 9.3.

**Notes (load report, P5 close-out):** the header's whole-content hash is no longer a gate (plan §2.11). `WorldSave.Inspect` reports before loading: content changed, mods added or gone, what `ContentRemap` will do (its own notes: goods added, moved, written off; recipes and proficiencies moved or dropped), and, when the content changed, whether the regenerated layers (continent, history, region, landmarks, the baseline people) still match their saved hashes. The load screen and the world library show it for Continue or Cancel. A save is refused only for a layer that no longer regenerates identically, for content in use that is gone with no migration (an unfinished craft of a removed recipe, a business or crew of a removed kind), or a newer format, each naming its cause. Visited chunks are snapshots and never regenerate.

## 8. Cross-spec notes (resolved here)
1. **Enums grow append-only:** `PieceKind` +`Roof, Beam`; `OddJobKind` +`Construct, Deconstruct, DayLabour`; `TransferReason` +`ShareSale` (saves pad the count, as `Alms` did); `ItemFlags` +`Named` (32), `Oiled` (64), `Brittle` (128): the byte is now full, the next flag widens it with a migration; `HoldingKind` is new. No `Skill` is added: building trains Carpentry and Masonry (unskilled pieces Athletics); boats use Athletics for rowing and the team's driver skill for towing; a `Seamanship` skill is Phase 7's.
2. **One recipe format and one station vocabulary** (crafting 3.1, 3.2): Phase 2 `recipe` rows are converted and migrated by id; `RecipeBook` is a derived view; the Phase 2 goldens must pass unchanged (CF-1).
3. **One worker-hour rule everywhere:** `time_mult = 1300 - 6 x skill` (production 3.1) for building, vehicle installs and business work; crafting keeps its own `skill_factor` (Phase 3 4.2) for hands-on recipes. The construction and vehicle specs never invent a third.
4. **Wages** are the production classes (150 / 350 / 500 mites a day; 10-hour days) for crews, staff, stewards (350) and day labour; contract bids use the solver's 1,450 pm margin.
5. **Materials on site** (construction 4.3) and **materials in a commission** (crafting 4.7) are economy stores with an owner; nothing moves between stores except by a person, a cart or a haul job.
6. **Lot prices and building values** come only from `land.jsonc` (land 4.2); businesses' valuation and inheritance's creditor claims read the same function.
7. **Structures** (construction 3.3) are the subject of `Building` deeds; RoomDetector v2 and the land rule both key on structure ids; a split keeps the lower id.
8. **Quality** has one table (items 3); combat, value, wear and repair read it; any new effect of quality must be added there.
9. **Hash trail additions:** `LandHash`, `SiteHash`, `RoomHash` (building facts), `OwnershipHash`, `VehicleHash`, `EstateHash` (extended), `CraftHash` (extended with commissions).
10. **Money** in mites everywhere (the lot of 462 crowns is 46,200 mites).

## 9. Budgets (the single ledger; reference PC as in plan §2.11)

### 9.1 Live tick (20 Hz), average per tick in a normal bubble
Phase 3 used about 3.6 ms and Phase 4 about 1.8 ms of the 8 ms tick.
| Item | Spec | Budget |
|---|---|---|
| Workers on ghosts (<= 40), groan timers | construction | 0.05 ms |
| Live crafters and staff at stations (<= 20) | crafting | 0.05 ms |
| Vehicles with part rows and boats (30 + 10), wear per 20 m | vehicles | +0.1 ms over Phase 3's 0.4 |
| Stall minding, shoppers | businesses | 0.02 ms |
| **Phase 5 total** | | **about 0.25 ms** (tick total about 5.7 of 8 ms) |
Barrier (event-driven): a piece finished <= 0.5 ms; a 200-cell collapse <= 3 ms; an estate settled <= 0.02 ms per holding. Presentation: build-mode preview <= 4 ms per cursor move, ghost layer <= 1 ms a frame, Ledger open <= 15 ms, designer update <= 2 ms.
### 9.2 Abstract tick
Hourly H2: abstract construction and vehicle sites <= 2 ms; steward rules <= 0.05 ms per owned business; daily rust and rot <= 0.3 ms; day labour <= 6 ms a region at hour 6; abstract barge legs <= 0.5 ms. **The worst hour must stay under the 150 ms cap** (Phase 4 left it at 126-153 ms under load: Phase 5 adds nothing to hour 0 and P5-I profiles it).
### 9.3 Memory, saves, content
| Item | Budget |
|---|---|
| Deed change log, claims, leases | <= 100 KB a save |
| Sites, ghosts (28 B), structures (6 B a cell) | <= 600 KB for a busy town |
| Vehicle part rows (12 B) | <= 240 KB (500 vehicles x 40 parts) |
| Ownership, commissions, wills, designs | <= 200 KB |
| **Phase 5 growth** | **<= 2 MB** per detailed region and hero save |
| Content | pieces about +60 KB, recipes about 140 KB, parts and templates about 40 KB, rooms 10 KB; `CombatTablesV2` shrinks about 0.8 MB |
| Allocation gate | 0 allocations per tick for ghost work, part wear, boat speed |

## 10. Combined open questions (deduplicated; "default" = recommended)

### 10.1 For the user (the six that change how the game feels)
| # | Question | Default |
|---|---|---|
| Q1 | How fast is building by hand? | Real worker-hours: a cottage is about 250 hours, so alone about 25 game days; with a crew of 6 about 4. Speeds up to 8x are allowed while building if nothing hostile is near |
| Q2 | When the next character is a companion, not family, what does he inherit? | With a will (made at a church or the reeve for 2 crowns) he gets everything; without one he keeps what he carries and what was on the body, and land and businesses go to blood kin, else to the lord |
| Q3 | Claiming wild land inside a lord's fief | Needs a licence bought from the lord (10 crowns an acre); claiming without one is allowed but marked as squatting, and the lord evicts in Phase 6. Outside any fief, claims are free but nobody protects them |
| Q4 | Quality tiers | One 5-tier scale (crude, poor, common, fine, masterwork) with DF-style combat effects 0.8 / 0.9 / 1.0 / 1.4 / 2.0 on edge and armour. No x3 "artifact" tier: a legendary piece is a named masterwork with a history, not stronger |
| Q5 | Can stewards cheat you? | Not in Phase 5: stewards are honest but differ in skill (stale prices, poor cash planning). Skimming arrives with Phase 6's corrupt officials |
| Q6 | How dangerous is a collapse? | A floor groans for 10 game seconds before it falls; anyone still on it falls a level with real fall wounds. Alternative: instant collapse, Dwarf Fortress style |

### 10.2 Technical (decided here unless someone objects)
| # | Question | Specs | Default |
|---|---|---|---|
| T1 | Lot price level | land 2 | 150 mites a m2 in towns (462 crowns for a burgage plot) |
| T2 | New streets, soil improvement, horse breeding | land 3, overview 2.2 | Phase 6 |
| T3 | Mortgages | land 4 | Phase 6 |
| T4 | Theft from site piles, piece upkeep | construction 3, 4 | Phase 6 |
| T5 | Room value price basis | rooms 1 | reference prices |
| T6 | Partial roofs | rooms 2 | every cell roofed |
| T7 | Apprenticeship term | crafting 1 | one season, up to 7 recipes |
| T8 | Guild powers | crafting 2 | secrets and dues only |
| T9 | Commission collection window | crafting 3 | 15 days |
| T10 | Goodwill | businesses 2 | two seasons of mean profit |
| T11 | Share cap in NPC businesses | businesses 3 | 490 pm |
| T12 | Vehicle facings | vehicles 1 | 8 (diagonals baked); cut to 4 if late |
| T13 | River depth model | vehicles 2 | by width class |
| T14 | Sinking boats and drowning | vehicles 3 | people reach the bank with a swim check; no deaths until drowning lands |
| T15 | Daughters in primogeniture | inheritance 2 | after sons |
| T16 | Hero's kin at the start | inheritance 3 | small, by origin |
| T17 | Masterwork rate and rust rate | items 2, 3 | about 5% for a master with the best tools; 2 pm a day damp |

## 10a. Decided with the user (Nov 2026)

These answer the questions in §10. Where this section and a per-spec "default" disagree, this section wins.
- **Building pace:** real worker-hours. A cottage takes one person about 25 game days and a crew of six about 4. 8x is allowed while building somewhere safe.
- **Heirs when continuing as a companion:** a will decides. A will costs 2 crowns at a church or the reeve; a companion named in it inherits everything. Without a will, the companion keeps what they carry and the body's goods, and land and businesses go to blood kin, else to the lord.
- **Claims inside a lord's land:** a licence costs 10 crowns an acre and gives a protected claim. Claiming without one is allowed but marked as squatting, and the lord can evict you from Phase 6. Outside any lord's land, claims are free but nobody protects them.
- **Collapse:** the floor groans and dust falls for about 10 game seconds, then it falls, and anyone on it takes real fall wounds.
- **Artifact tier and stewards:** the user did not choose, so the recommended defaults stand. No tier above masterwork (about 1 in 10 masterworks is named, with a history, but is no stronger). Stewards are honest in Phase 5; skimming arrives with Phase 6's corrupt officials.
- **Charters to found a settlement (user request).** The ruler of a realm, or of a faction that holds land, can grant a **founding charter** for a new settlement on wild or royal land.
  - The charter is a deed with conditions: a site, a deadline to settle a minimum number of households, dues owed to the grantor, and sometimes military service.
  - You petition for one in person or through a vassal lord. The grant depends on your reputation, the ruler's opinion, a fee or gift, and whether the land is contested.
  - Once settled, the place grows by the existing rules: a village at 80+ people with a chapel or mill; a town when the ruler grants a **market charter** (a second, separate grant).
  - **Phase 5** builds the deed, the conditions, the founding flow, recruiting real households, and growth into a village.
  - **Phase 6** builds the petition and the ruler's decision as politics, charters revoked for broken conditions, and market charters.
  - Granting or revoking a charter is a chronicle event.

- **Prospecting and extraction businesses (user request):** see `prospecting-and-extraction.md`.
  - The player can prospect: walk, read the land, pan, dig test pits, sink trial shafts, or hire a prospector.
  - What a person knows is a separate record from the hidden truth, and survey reports are items.
  - Anyone with the right to the land can found extraction businesses on the resources they know about: woodcutters' camps, sawpits and water sawmills, charcoal clamps, quarries, clay pits and kilns, peat cuttings, salt pans, mines, and bloomeries.
  - Mineral rights are separate from surface rights: a lord's licence for base metals, the crown's for silver and gold, with royalties.
  - Build order: it goes in step 4, beside businesses, using streams 0x5C0 to 0x5C2. Estimate: about +0.5 to 1 month, inside the 9 to 10 months if run in parallel.

- **Crafting (decided with the user):**
  - **Reach:** tools, stations and materials count when they are in the crafter's room or within 6 m, as `crafting-full.md` §4 already says.
  - **Fetching:** materials stored elsewhere in the same building are fetched as part of the job, with real walking time and carrying limits; no manual inventory juggling.
  - **Field kits:** limited portable kits (sewing kit, whetstone and file, fletching kit, field smithing kit) give low tool qualities anywhere, at a penalty. They allow mending, sharpening, fletching and field repairs. Real smithing still needs a forge and anvil.
  - **Craft time:** historical labour times (a sword takes days of a smith's work, mail takes weeks), with 8x allowed while crafting somewhere safe, or commission the work and come back.

## 11. Risks

| Risk | Why it matters | Mitigation |
|---|---|---|
| **Hands-on building feels slow** | 250 worker-hours a cottage is honest but long for one hero | crews and contractors from day one, speeds to 8x while safe, the plan library to skip design time; tester question "did building feel worth it?" at the fun check; Q1 |
| **Hauling tedium** | materials must be on site (locked) | Buy and deliver (market orders to the pile), haul jobs, carts unloading into the pile with `\`, the site panel's "what is missing" |
| **Two recipe formats merge** | Phase 2's economy balance depends on its recipes | the unifier is a pure conversion with CF-1 (`RecipeBook` view equal field by field) and the Phase 2 soak gates unchanged |
| **Quality enum swap moves combat numbers** | Phase 4 calibration was hard won | the table keeps Common = 1,000; IQ-2 reruns the fixed-seed hashes and the duel harness; the save step remaps bytes |
| **Collapse cascades and portals** | correctness and performance in towns | `SupportGraph` per structure only, bottom-up order, BC-13/14, 3 ms budget, pathfinder invalidates only affected portals |
| **Live and abstract double counting** (workshops, sites, barges) | conservation is a plan invariant | one bridge rule each (crafting 4.4, construction 4.9, vehicles 4.6), floor both ways, CF-9, BC-17, VD-19, soak audits |
| **The hour barrier** | already near 150 ms | nothing new at hour 0; day labour at hour 6; profiling in P5-I |
| **Content volume** | about 66 pieces, 46 recipes, 21 parts, 14 rooms, 10 blueprints, their sprites | content scaled separately (plan §7); validators BC-1, CF-2, VD-1; the plan library reuses world generation's blueprints |
| **The hero-as-person change** | touches saves, people, succession, autopilot | step 1 A4 first, behind IN-1 and the migration test |
| **Scope: eight specs in 7 to 10 months** | the overrun rule | cut order (section 3), milestones M-A to M-E, the M-B checkpoint at month 4 |

## 12. Test strategy
Every spec has golden tests (**about 130**: IQ 14, LD 14, BC 21, RD 12, CF 18, BS 18, VD 20, IN 13) plus OV-1 to OV-6 (section 4). Shared harness: the Phase 2 `HeadlessWorld` and the `--autoplay` bot (Phase 3), extended with build, land, business, designer and succession commands. Conservation audits (coin through the Ledger, mass through `MassLedger`, deeds by count, people across inheritance) must stay exact in BC-21, CF-9, BS-18, IN-12, VD-14 and OV-6. Nightly: the 100-year soak with growth sites, day labour and NPC inheritance (every Phase 2 and 3 gate, plus idle adults < 800). Phase end: the code and bug check (plan §6), `docs/reviews/phase-5.md`, the fun check and the testers' question.
