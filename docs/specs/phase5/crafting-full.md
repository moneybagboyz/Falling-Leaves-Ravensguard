# Crafting, full system: one recipe format, workstations from the world, proficiencies, learning, smithing, repair, salvage, commissions, guild secrets

Plan refs: §3g (recipes as data with alternatives, tool qualities, workstation with qualities supplied by placed furniture, skill and difficulty, proficiencies as a dependency graph learned 0-100% (*principles of metalworking* -> *blacksmithing* -> *armoursmithing*, *bladesmithing*, *riveted mail*; *glazing* needs *pottery*), learned by doing, from a master or a manuscript; apprenticeship, watching, manuscripts, taking apart; recipes differ by culture; guilds guard trade secrets; steps and unattended steps; batches; failure; disassembly and salvage; repair and refit; **commissions**: an NPC craftsman makes something with your materials, using his time, skill and workshop, and you collect it in person; recipes queued as jobs so workers and businesses craft through the same system; about 80 recipes in Phase 5), §5 Phase 5 ("tool qualities and workstations, proficiencies, learning by apprenticeship, watching and manuscripts, batches, failure, repair, refitting, disassembly and salvage, commissions, guild secrets; smithing quality"), §2.10 (quench and temper while smithing; a bloomery makes a bloom), §4A.1 (a recipe is inputs + worker-hours + tool wear -> output; shops sell what workshops made; commissions reserve a smith's time and materials).

## 1. Purpose
Phase 3 built the crafting engine (`Sim/Crafting/`: `RecipeDb`, `CraftMath`, `CraftRunner`, `CraftState`, `Knowledge`, `SkillBook`, `CraftWork`; 34 recipes, 12 proficiencies, the N screen) but left it beside the world: stations were a fixed test site, the hero's pack a dev bundle, effects reported but not applied, and Phase 2 businesses kept a second recipe format. Phase 5 makes crafting **the** way things are made: one recipe format for the hero, NPCs and businesses; stations, tools and containers read from the real rooms; about 80 recipes and 40 proficiencies; smithing quality; repair, refit, salvage and reforge; commissions; guilds. What is new versus extended is marked in each section.

## 2. Dependencies
| Needs | From |
|---|---|
| The whole Phase 3 crafting engine and content | exists (`docs/specs/phase3/basic-crafting.md` and its Notes) |
| Phase 2 `recipe` rows (41), `RecipeBook` (mass check, `StationKinds`), `Station` progress (micro-ppm), `ProductionSystem` batch lifecycle | exists (`Production/`) |
| One quality scale, tool wear, repair caps | `items-quality-and-wear.md` |
| Stations in recognised rooms, nearby furniture | `roomdetector-v2.md` 4.4 |
| Businesses run recipes as queued `Craft` jobs; commissions on a business's queue | `businesses-stewards-ledger.md` |
| **The hero's pack as real stores and item rows** in the running world's economy (Phase 3 Notes: "a dev starter bundle"; Phase 4: loot does not reach the pack) | overview build step 1 (P5-A4) |
| Vehicle parts and build pieces as recipe results | `vehicle-designer.md`, build spec |
New: `Crafting/RecipeUnifier` (Phase 2 rows -> `craft_recipe`), `Crafting/Commissions`, `Crafting/Guilds`, `Crafting/Smithing`, `Crafting/Salvage`; content `crafting/recipes_*.jsonc` (about 80 total), `crafting/proficiencies.jsonc` (about 40), `crafting/manuscripts.jsonc` (8), `crafting/guilds.jsonc`.

## 3. Data

### 3.1 One recipe format (extended `CraftRecipeDef`)
New optional fields (defaults keep every Phase 3 recipe unchanged): `business: bool` (a workshop may queue it), `workers_max: int` (people per station at once, Phase 2 `max_workers`), `result` kinds `vehicle_part` and `build_piece` (an installed piece, build spec 3.1), `process: "smithing"` (4.3), `commission: bool` (default true for item results), `secret: "guild:<id>"` (was a free string). The 41 Phase 2 `recipe` rows are **converted** at load by `RecipeUnifier` (and rewritten in content once, by a one-off script, with `"migrate"` entries mapping old ids): `worker_hours` -> one attended step of that many hours, `duration_days` -> a following unattended step (deadline 24 h, grace 48 h), `tool.wear_ppm_per_batch` -> kept for abstract runs, `min_batch_permille` -> `batch.min_pm`, `station` -> the unified station id below. `RecipeBook` becomes a **derived view** of the `business: true` craft recipes (inputs per batch, worker-hours, station), so `ProductionSystem` is unchanged in its arithmetic and the Phase 2 goldens (G-B1 and on) still pass.

### 3.2 One station vocabulary (`craft_station`)
Phase 2 station kinds become `craft_station` ids, each with its build pieces and the qualities it provides (about 34 stations, 25 new workstation pieces, for the plan's "about 60 workstations and pieces of furniture"):
| Phase 2 kind | Station (piece) | Phase 2 kind | Station (piece) |
|---|---|---|---|
| `oven` | `oven` (oven) | `fulling_tub`, `dye_vat`, `tailor_bench`, `spinning_wheel` | new, same names |
| `mill_stones` | `millstone` (millstone) | `tan_pit` | `tan_pit` (tanning_pit, tanning_rack) |
| `malt_floor` | `malting_floor` (malting_floor) | `cobbler_bench`, `saddle_tree` | new |
| `brew_vat` | `brew_vat` (brewing_vat) | `charcoal_clamp` | new (charcoal_clamp) |
| `butcher_block`, `salting_tub`, `cheese_press`, `candle_table` | new | `saw_pit` | `sawpit` (sawpit) |
| `bloomery` | new (`bloomery` piece: stone 900 kg, clay 300) | `barrel_bench` | `cooper_bench` (cooper_bench) |
| `forge` | `forge` | `wheelwright_bay` | `wheelwright` (wheel pit, spoke bench) |
| `anvil` | `anvil` | `work_bench` | `workbench` |
| `cupel_hearth` | new | `mason_yard`, `lime_kiln`, `brick_clamp` | new; `kiln` = `kiln` |
Added for vehicles: `slipway` (open, at water), `tyre_ring` (a hearth ring for shrinking iron tyres).

### 3.3 Proficiencies (about 40; graph without cycles; `requires` as `{id, min_pm}`)
Phase 3's 12 stay. New: `joinery` (woodcraft 40%), `wheelwright` (joinery 50%), `cartwright` (joinery 50%), `shipwright` (joinery 60%), `coopering` (joinery 40%), `turning`, `thatching`, `daubing`, `stone_dressing` (masonry basics), `masonry_basics`, `bricklaying` (masonry_basics 40%), `lime_burning`, `bloomery_smelting` (metalworking_principles 30%), `blacksmithing` (blacksmithing_basics 60%), `bladesmithing` (blacksmithing 60%), `armoursmithing` (blacksmithing 60%), `riveted_mail` (blacksmithing 40%), `plate_armour` (armoursmithing 70%), `locksmithing` (blacksmithing 50%), `tyre_shrinking` (blacksmithing 40% and wheelwright 30%), `bronze_casting` (metalworking_principles 50%), `silver_refining` (bronze_casting 40%; the mint chain), `pottery`, `glazing` (pottery 50%), `glassmaking` (glazing 40%), `tanning_bark`, `cobbling`, `saddlery` (cobbling 50%), `weaving`, `fulling_dyeing` (weaving 40%), `malting`, `brewing_full` (brewing_basics 50%), `milling`, `butchery_full`, `cheesemaking`. Learning and effect are Phase 3's (`+8 pm` an attended hour, x2 with a master, x1.5 from a manuscript; time and skill penalties below 100%).

### 3.4 Guilds (`crafting/guilds.jsonc`) and commissions (state)
```jsonc
{ "type": "guild", "id": "smiths_oakford", "name": "Smiths' guild", "settlement_class": "town", "trades": ["Smith"], "secrets": ["forge_steel_blade_fine", "quench_oil"],
  "entry_fee_mites": 5000, "dues_mites_per_season": 300, "min_skill": 40, "residence_days": 15 }
```
Guilds exist in chartered towns per culture content (generated at world start: one per trade with at least 3 businesses in the town; the most skilled master is `GuildMaster`, an existing occupation). `Commission { Id, Client, Maker (person), Business (opt), Recipe, Alternative, Batch, MaterialsFromClient (bool), PriceMites, Escrow, QueuedAtHour, DueHour, State (Queued, Working, Ready, Collected, Cancelled), ItemIds[] }`.

## 4. Rules

### 4.1 Where crafting happens (extends Phase 3 4.7)
The screen and every NPC crafter read the real world: stations, containers and tools in the crafter's current room and within 6 m (RoomDetector v2 4.4), the hero's real pack and the stores of buildings he may use (land spec 4.1). Results go to the crafter's pack or, at a business station, to the business store. The crafting session and every `CraftJob` are in the save (the Phase 3 gap "not in the save file yet" closes). **Effects are wired:** `lit_fire` places a fire object (a `campfire` station `heat 2, cooking 1`, light 200 within 4 m, burns 0.5 kg firewood an hour, goes out when the fuel is gone or in rain over 5 mm/h); `repair_cart` adds hit points to the vehicle part named by the job (vehicle spec 4.6); `refit` changes the garment's fit size (4.5).

### 4.2 Quality (the formula is in `items-quality-and-wear.md` 4.2)
New terms: **station_bonus** from the station piece's `BuiltSkill` or the installed item's tier (Crude -5, Poor -2, Common 0, Fine +3, Masterwork +5); **mastery_bonus** +5 when every proficiency the recipe lists is at 1,000; **process_bonus** for smithing (4.3). Phase 3's tool bonus, material bonus and light penalty are unchanged; the score roll stays stream 0x223.

### 4.3 Smithing quality (new)
Recipes with `process: "smithing"` have steps `heat` (the forge must be at working heat: `heat 3`, charcoal 1 kg per heat-hour, a cold start costs 30 min), `forge` (attended, hammering 2 and anvil), `quench` (critical: Phase 3 failure roll) and, for blades, springs and tools with an edge, `temper` (30 min attended).
- **Material sets the ceiling:** edge = the material's maximum edge x Qw (items spec 4.1); steel takes and keeps an edge iron cannot. A bladesmith working `iron_bar` into a sword gets `material_bonus -5` (the recipe's alternative) and a lower edge from the material itself.
- **Process bonus:** `+5` when the smith's effective Smithing is at least the difficulty + 20 at the quench (he reads the colours), `0` otherwise; **skipping temper** (offered in the screen: 30 minutes faster) gives `-5` and the flag `Brittle` (bit 128): a brittle blade whose blow is stopped by a rigid layer breaks with 30 pm per such blow (stream 0x532, key `FightTick`, item id); **no quench** on a recipe that needs it: `-15` (soft edge).
- **Bloomery:** `smelt_bloom` (Phase 2 rate: 10 kg ore + 15 kg charcoal -> 1.5 kg bloom) stays a business recipe with an unattended `burn` step of 8 h; the bloom must be forged to bar (`forge_bar`) before smithing. A forge never melts iron (plan §2.10).

### 4.4 Batches, queues and the live bridge (extends Phase 3 4.2)
Businesses and households queue recipes as `Craft` jobs at a station (`workers_max` per station). **Live:** staff in the bubble go to the real station and do the steps with `CraftRunner` (the same calls as the hero). **Abstract:** outside the bubble `ProductionSystem` credits `Station.ProgressMicro` as Phase 2. **Bridge:** at promotion a station's batch becomes a `CraftJob` whose attended progress is `ProgressPpm x step seconds / 1,000,000` (floor); at demotion the reverse (floor), so a batch is never done twice or lost (test CF-9). This closes the Phase 3 limit "the live tier is theatre for scheduled work" for workshops.

### 4.5 Repair, refit, salvage and reforge (new recipes; caps from items spec 4.5)
| Recipe | Skill / proficiency | Inputs | Effect |
|---|---|---|---|
| `reedge_blade` | Smithing 25 / blacksmithing | whetstone wear, charcoal 0.5 | +300 condition on a blade, quench not needed |
| `rerivet_mail` | Smithing 30 / riveted_mail | iron wire 0.2 kg | +250 per 2 h on mail; removes `RingsBurst` (Phase 4 open item 4) |
| `beat_out_plate` | Smithing 35 / armoursmithing | charcoal 1 kg | +200 per 1.5 h on plate; replaces broken straps (leather 0.2) |
| `patch_leather` | Cobbling 20 | leather 0.2 kg, twine | +300 (max 800) on leather armour, shoes, saddles |
| `refit_mail` | Smithing 35 / riveted_mail | iron wire 10% of mass to grow, returns rings to shrink | +-1/7 of body size per 3 h |
| `refit_plate` | Smithing 50 / plate_armour | charcoal 2 kg | only from +1/7 too large down to fit (plate cannot grow); 4 h |
| `refit_garment` | Tailoring 30 (Phase 3) | as Phase 3 | now changes the garment's size |
| `salvage_mail` | none | a mail item | 70% of mass as `mail_rings`, the rest scrap (mass exact) |
| `salvage_armour` | none | plate or scale | plates -> `scrap_iron` 80%, straps -> leather scraps |
| `reforge_item` | as the original recipe | an item's components plus 10% more of its main material | a new item of the same shape and material at a new quality roll |
A refit by a crafter two tiers below the item drops it one tier (items spec 4.1). Disassembly (Phase 3) is unchanged; building salvage is build spec 4.1 (Deconstruct).

### 4.6 Learning (extends Phase 3 4.6)
- **Apprenticeship (new, formal):** a master with Q > Take me as apprentice (needs skill >= 40, the master's consent: he has fewer than 2 apprentices and the candidate's skill is at least his minus 50): a **term of one season** living in the master's household, working his station 10 hours a day unpaid (he feeds you). Through the term the apprentice learns, at no fee, every recipe of the master's trade with `difficulty <= min(master's skill - 10, apprentice's skill + 30)`, one recipe per 2 days in ascending difficulty; skill and proficiency gains x2. Leaving early keeps what was learnt. NPC apprentices (LabourMarket's 12-year-olds) follow the same rule, so a town's crafts improve by themselves.
- Phase 3's paid per-recipe sessions, watching, manuscripts (now 8: `bread_book`, `herbal_notes`, `farrier_notes`, `smiths_notes`, `wheelwrights_rule`, `masons_book`, `dyers_receipts`, `shipwrights_lines`) and taking apart stay as written.

### 4.7 Commissions (new)
Q on a craftsman (or his shop counter) > Commission: pick a recipe he knows (his list, filtered by his station), the alternative, the size (fit to you), and whether you supply the materials. **Price** = `labour hours x wage class (skilled 35, master 50 mites an hour) x 1,450 pm` + his materials at his ask (if he supplies) + fuel. Half goes to escrow at ordering (the rest on collection); your materials move into his store marked yours (`Owner` = you). The job joins his station's queue: `DueHour = queue end + recipe hours at his speed` (shown: "ready in 3 days"). He makes it with his own skill, tools and quality roll; the item sits in his store with you as owner, and you **collect in person** (E on him or his counter); after 15 days uncollected he may sell it and refund your materials' value. Cancelling before work starts refunds all; after, materials used are lost and half the labour is paid. A commission is never instant and never made by anyone but him or his staff.

### 4.8 Guild secrets (new)
A recipe with `secret: "guild:<id>"` is only taught (master, apprenticeship) by members, and only to members. **Joining:** live in the town 15 days, skill >= the guild's minimum in its trade, pay the entry fee to the guild chest (a purse held by the guild master); dues each season, arrears 2 seasons -> expelled. Members see the secrets in the screen ("guild recipe"); non-members see "taught only in the guild". A member teaching a secret to an outsider, and a manuscript of secrets leaving the guild, are recorded as `GuildOffence` events with witnesses (Phase 6 enforces: fines, expulsion, theft as a crime). Taking apart a secret item to learn it works (`decomp`) but is also an offence if seen.

### 4.9 Content (about 80 recipes)
Phase 3's 34 plus about 46: smithing (forge nails, hinges, iron tyre, wire, knife, axe head, spade, chisel, arming sword, spearhead, arrowheads, mail shirt, helmet, breastplate) 14; repair and refit (4.5) 9; carpentry and vehicle parts (frame plank, axle, spoked wheel, solid wheel, shafts, cargo bed, hull plank, oar, rudder) 9; building materials (saw planks, rive shingles, burn lime, dress stone, fire bricks, bind thatch, daub mix) 7; leather and cloth (shoes, harness, saddle, gambeson) 4; food and trade (malt, ale, salt fish) 3. Every recipe passes the mass check.

### 4.10 Tick placement
| Where | What |
|---|---|
| Live 20 Hz | `CraftRunner` for live crafters (hero and promoted staff), fire fuel burn |
| Barrier | start, finish, collect, commission order and collection, guild join, apprenticeship start and end |
| H2 | abstract station progress (Phase 2), commissions on abstract makers |
| H4 slice | guild dues, commission expiry, apprenticeship recipe-a-day |
| Hourly | Phase 3 due-queue (unattended steps) |

## 5. Determinism
Existing: 0x223 score, 0x360 failure, 0x361 by-products, 0x362 watching. New: 0x530 masterwork inspiration and 0x531 named (items spec), **0x532** brittle breakage. Guilds are generated without a draw (trades with 3+ businesses, the most skilled master, ties by person id). The live and abstract bridge uses floor in both directions. Recipes and stations dense-numbered by ordinal id.

## 6. UI and controls
N (unchanged). New in the screen: a **Commission** tab (makers in this settlement who know the recipe, price and days; "Order"), **Your commissions** (state, ready, where), the station panel reads the real room ("Smithy: forge (Common), anvil (Fine)"), Q on a station or an item opens the screen filtered (Phase 3's promised entry points), the skip-temper choice as a checkbox with its effect shown, the guild line on secret recipes. Fits 960 x 540 (Phase 3 layout). No new keys.

## 7. Tests (golden)
| id | Case | Expected |
|---|---|---|
| CF-1 | Unifier: the 41 Phase 2 recipes convert; `RecipeBook` view equals the old book field by field | equal; Phase 2 production goldens pass unchanged |
| CF-2 | 80 recipes load, mass check passes, 40 proficiencies acyclic, every station id has a piece | pass |
| CF-3 | World stations: the hero in the RD-7 smithy | screen lists forge x2 (from the room), anvil; tools in a chest 4 m away count; a chest 7 m away does not |
| CF-4 | Pack: craft from the real pack, save, load | items and stock in the economy section; byte-identical round trip with a job in progress |
| CF-5 | Station bonus: Fine anvil vs Common, skill 60 | +3 to every score; the expected distribution shifts by exactly 3 points |
| CF-6 | Smithing process: arming sword, smith skill 70, difficulty 45 | process +5 (70 >= 65); with skill 60: 0; skip temper: -5 and `Brittle` |
| CF-7 | Brittle break: a brittle blade blocked by plate 1,000 times | 30 +- 16 breaks (3 sigma); a tempered blade 0 |
| CF-8 | Business live: smithy staff promoted mid-batch at 400,000 ppm of a 20 h `forge_nails` | `CraftJob` at 8 h done; finished live; demoted at 15 h: `ProgressMicro` = 750,000 ppm; batch completes once |
| CF-9 | Bridge conservation: 1,000 promote and demote cycles | outputs and inputs exact; no batch done twice |
| CF-10 | Apprenticeship: master skill 60 (smith), apprentice 15 | learns every smith recipe with difficulty <= 45, one per 2 days, ascending; 15 days -> at most 7 recipes |
| CF-11 | Commission price: arming sword 24 h, master wage 50 | labour 24 x 50 x 1.45 = 1,740 mites + his steel and charcoal at ask; half to escrow |
| CF-12 | Commission queue: maker busy 30 h | due = 30 + 24 x his time factor; collected in person only; uncollected 15 days -> sold, client refunded the materials' value |
| CF-13 | Guild: a non-member asks to learn a secret | refused; after joining (15 days, 5,000 fee): taught; teaching an outsider writes a `GuildOffence` with witnesses |
| CF-14 | Repair cap: Fine mail re-riveted by skill 35 (Common) | cap 950; condition to 950 at most |
| CF-15 | Refit plate 2/7 too large | two refits of 4 h; a plate 1/7 too small: refused |
| CF-16 | Salvage mail 9 kg | 6.3 kg rings, 2.7 kg scrap; mass exact |
| CF-17 | Fire effect: kindle a fire, 1 kg fuel | heat and light present for 2 game hours, then out; rain 6 mm/h puts it out |
| CF-18 | Determinism: a recorded day of crafting, commissions and an apprenticeship | identical item ids, qualities, stores on both OS |

## 8. Open questions (recommended defaults)
1. **Apprenticeship term:** one season (15 game days) of unpaid work, learning up to 7 recipes. Default as written.
2. **Guild strength:** Phase 5 guilds gate secrets and collect dues only; price fixing and monopolies are Phase 6 politics.
3. **Commission collection window:** 15 days, then the maker may sell it. Default as written.

## 9. Budgets
`RecipeDb` can-make for 80 recipes with the real world scan (room + 6 m) <= 1.5 ms, incremental after; live crafters <= 20 per bubble <= 0.05 ms a tick; the bridge at promotion <= 0.02 ms per station; save: commissions 64 B, apprenticeships 32 B, guild memberships 16 B; content about 140 KB.


## Decided with the user (Nov 2026)
- **Reach:** room or within 6 m (unchanged).
- **Auto-fetch:** stores in the same building that the crafter may use are fetched as part of the job: real walking time and carrying limits, done as a `Haul` sub-step before the first attended step. The CF-3 "a chest 7 m away does not count" rule now means "is fetched, at a time cost", when the chest is in the same building.
- **Field kits:** new tool items, `sewing_kit`, `whetstone_file`, `fletching_kit` and `field_smith_kit`.
  - Each gives one or two low tool qualities (level 1) anywhere, with a -5 score penalty.
  - They are enough for mending, sharpening, fletching and field repair recipes.
  - Smithing steps still need the `forge` and `anvil` stations.
- **Craft time:** historical labour times, with 8x allowed in safety (as for building).

## Notes (P5-E implementation, step 2)
**Code** (`src/Ravensguard.Sim/Crafting/`): `RecipeUnifier` (Phase 2 rows to the one format and back: the recipe book view), `CraftSources` (the stores a recipe draws on, in order, and the fetch time), `CraftReach` (stations, tools and containers from RoomDetector v2: the room, 6 m, the same building), `Smithing` (process bonus, brittle breakage 0x532), `CraftRunner.Full` (refit, salvage, reforge, oil, the fire object, the bridge's station-bound jobs, the forge's heat), `Fires` (`FireBook`), `Commissions`, `Guilds`, `Apprenticeship`, `WorkshopBridge` (promote, demote, the queue API), `CraftContext` and `CraftWiring` (the world as the rules see it: purses, a maker's store and site, people of a place, guild towns, witnesses), `CraftDesk` (the hero's "cf:" commands, the hourly pass, the place view), `CraftSystem` (the abstract tick's hour, with `CraftServiceWiring` on the home region). Content: `crafting/recipes_business.jsonc` (the 41 Phase 2 rows, converted once by a script; `production/recipes.jsonc` is gone, its copy is the CF-1 test data), `recipes_full.jsonc` (30), `guilds.jsonc` (3 guild templates and `craft_settings`), no new goods (see below), 25 stations in `requirements.jsonc` (34 with Phase 3's), 4 field kits, 37 proficiencies (48 in all), 5 manuscripts (8), `build/pieces_workstations.jsonc` (19 workstation pieces, each with P5-B's `build` cost block; BC-1 now counts 99 pieces). Godot: `UI/CraftingScreen` (station panel, Commission tab and your commissions, masterwork chance and the score's terms, the temper choice, the piece a repair works on, missing-tool and fetch hints, guild line), `UI/CraftingSession.Full`, `World/StreetView.Rooms` (`CraftReachHere`: the stations come from the room the hero stands in), `UI/NewGame/CraftShotScenes` (`--ng-shot crafting --ng-craft-scene smithy|commission|kit`), the inventory tooltip's edge and protection lines.
**Tests**: `tests/.../Crafting/CraftingFullTests.cs` (CF-1 to CF-18, field kits, the masterwork chance and a blunt tool, the strings), K-1 and K-17 updated for the larger content, and the 7th case of `Abstract/WorldRuntimeResumeTests` now carries a commission (made and then sold uncollected across the save), an apprenticeship, a guild membership, a fetch job in progress and a fire.

Settled where the text was open:
- **One format.** A business recipe is a `craft_recipe` with `business: true`, `workers_max`, `wage_class`, `batch.min_pm`, the tool's `wear_ppm_per_batch` and, when renamed, `migrate` (only `salt_meat`, which is also Phase 3's hands-on recipe: the craft id is `salt_meat_tub`, the book and the business kinds keep `salt_meat`). `RecipeBook.Build(content)` builds the view; CF-1 compares it field by field with the old book (and the round trip of every old row through `ToCraft` and `ToBook`); the world hashes did not move. A mod's Phase 2 `recipe` rows are still read and converted at load. Converted recipes get difficulty 10 / 30 / 50 by wage class. `smelt_bloom` gained the 8 h unattended `burn` step (days in the view stay 0). The recipe count is 105: Phase 3's 34, the 41 business recipes (the spec's "building materials, malt, ale, salt fish, shoes, saddle, forge nails" are among them) and 30 new; boat parts (hull plank, oar, rudder) are left to P5-G, which owns the boat parts. **No new goods**: the economy section refuses a save made against another goods table (IQ-12's real v7 save showed it), so iron wire is drawn from iron bar inside the mail and refit recipes, salvaged rings come back as `scrap_iron` (CF-16: 6.3 kg of rings as iron, 2.7 kg waste) and the recipes that needed new goods (wire, arrowheads, fletched arrows, shingles, daub, harness) wait for a goods-table migration: the fletching kit has no recipe yet.
- **Reach and fetch.** `CraftReach.Scan` reads stations from the room's workstation fixtures and any within 6 m on the level (tier per station from the caller: a piece's built skill or installed item; Common when unknown); containers in the room or within 6 m are used in place and their tools count; a container elsewhere in the same structure (RoomDetector v2's structure id) is a fetched source: its materials move at the start, and the job walks `trips x (2 x distance / 1.2 m/s + 30 s)` first (`FetchLeft`, saved), trips by the carry limit (`CraftActor.CarryG`, 25 kg). Tools there do not count until carried. Land's `May(Store)` is the caller's filter (`ReachContainer.MayUse`). In the street view the stations come from the map; the world has no container stores yet (P5-B's stores in rooms), so in play the sources are the pack.
- **Score terms.** `score = skill + roll + tool (-10..10, a blunt tool -3) + station (-5..5: the workstation's tier, else the station that supplies a need) + material (the chosen alternative's bonus) + mastery (+5) + field kit (-5 when a need is met only by a kit) + light + smithing process`, all fixed in the job (saved) except the quench's +5, which is read at the quench from the smith then at work. Phase 3 recipes at a Common station score as before.
- **Smithing.** `process: "smithing"` maps steps by id (heat, forge, quench, temper); the quench must be the critical step; temper may be `optional`. The heat step is the cold start: left out when the forge at the place worked within the hour (`ForgeHot`, saved). Fuel is charcoal on the heat and forge steps (1 kg a heat-hour). Leaving the temper out: -5 and `Brittle` on the item. The -15 "no quench" applies to an edged smithing recipe (one with a temper) without a quench; no base recipe is one. `Smithing.BrittleBreaks` is the rule (0x532, key fight tick and item); **the call in the resolver is not wired** (Combat was out of this part's bounds): P4/P5 combat work adds it where a blow is stopped by a rigid layer.
- **Bridge.** A promoted batch is a station-bound job: its step seconds follow the one worker-hour rule (worker-hours x scale x `1300 - 6 x skill`), its progress floors both ways, the production system leaves a live station alone (`ProductionSystem.LiveStation`) and finishes it once when the live work completes it (`StationDone`). The queue API (`Enqueue`, `Next`, `StartNext`) is for P5-F; the live staff walking to stations is P5-F's too.
- **Commissions.** Wage class by the maker's skill (master from 60, skilled from 25); labour hours are the recipe's attended base hours; materials and fuel at his ask (the content price, integer). The work is laid out on his bench at ordering (the job takes the materials at once, so stored iron does not rust away before his hours); "work started" for a cancel means time worked. He works his oldest commission an hour at a time in his hours (08-18) in the hourly pass, with the runner a hero uses. Collection in person = the client, in the maker's settlement, through E on him (the desk checks the command's person and place). An NPC maker who knows a recipe by his trade (skill at least its difficulty) has it written down at ordering.
- **Guilds** are derived (never saved): templates x towns with three businesses of the trades; the master is the most skilled of the trades (no occupation is rewritten). Members, residence (from the hero's craft commands' places), dues and offences are saved. Offences go to the chronicle as `guild_offence` (cause Crime) with up to three trades-people of the place as witnesses. Price fixing, enforcement: Phase 6.
- **Apprenticeship** caps the term's recipes at `min(master - 10, apprentice + 30)` taken at the start (CF-10's 45); a day counts only when the hero apprentice was in the master's place. NPC apprentices: the same calls; LabourMarket does not start them yet.
- **Refit** records the size a piece was made for in the crafting section (`Fits`); the clothing book's own fit (live tier only) is not synced yet. **Oil** sets `Oiled` and the until-day (`OiledUntil`) for the rust rule; the daily rust and rot run belongs with the stores in rooms (items spec Notes: P5-B), so it is not wired here. **Fire**: a kindled fire is a `FireBook` entry with its wood in a store; heavy rain at the home cell puts it out (rain over 5 mm/h approximated by heavy rain or thunder).
- **The tick.** `CraftSystem` (H7, after land) runs commissions, fires, apprenticeship days and dues for the whole world; the hero desk only passes its notices on. Two traps found by the resume test: (1) a store taken out of the economy in the middle of an hour broke the other systems' goods audit, so stores emptied inside the tick are listed (`CraftState.Dead`, saved) and removed at the next barrier; (2) the world allocator's first "Other" serials are the region generator's fief and site ids, which key the fiefs' stores, so crafting ids now start past them (`CraftServiceWiring`; a Phase 3 latent clash too).
- **Not done**: installing a workstation from an item (phase-5-open: an anvil from an anvil item rather than 120 kg of bars) is construction's piece cost and stays open there; business and NPC tool wear at stations (abstract runs keep `wear_ppm_per_batch`), the crafting screen's container listing (no world containers yet), `Q` on a station or item opening the screen filtered, vehicle-part repair (`repair_cart` still reports only), the brittle call in the resolver, per-culture recipes.
