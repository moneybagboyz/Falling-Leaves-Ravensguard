# Basic crafting: recipes, steps, unattended work, practice, learning, and the crafting screen

Plan refs: §3g (recipes are data in a Cataclysm-like shape: components with alternatives, tool qualities with levels, tools with charges or fuel, workstation, skill and
difficulty, optional proficiencies, time in game hours, minimum light, byproducts, quality formula; proficiencies as a dependency graph learned 0 to 100%; how you learn
recipes: basics, skill unlocks, apprenticeship, watching, manuscripts, taking apart; steps with unattended phases that have a deadline and a grace period; reusable requirement
sets (`using`); practice recipes; unfinished items that anyone can resume; failure can waste components; "about 30 basic recipes in Phase 3"; the crafting screen), §2.10 (tool qualities:
"cutting, sawing, hammering, chiselling, digging, prying, sewing, lockpicking, cooking"; "a heavy rock can hammer a nail, badly"; disassembly returns components; repair needs
tools), §5 Phase 3 ("fire, torches, bandages, simple meals, simple repairs; recipe steps, including unattended ones such as rising dough; practice recipes; the crafting screen;
unfinished items; learning from a master"), §3f (practice yard). Phase 2: `production-and-businesses.md` (abstract business recipes and `RecipeBook` mass check), `items-mass-value-spoilage.md` 3.2 (quality score).

## 1. Purpose
Let a person make things by hand, in game time, with real materials, tools and know-how. A recipe is data; what happens at the bench is rules in C#. Phase 3 covers the basics
(fire and light, simple food, first aid, mending and sewing, rope and snares, a few smith jobs) and establishes everything the later 80 and 250 recipes need: steps, unattended work, unfinished items, proficiencies,
teaching and the screen. Businesses in Phase 2 keep their abstract batch recipes (`content/base/production/recipes.jsonc`); Phase 5 unifies the two through the job system.

## 2. Dependencies
| Needs | From |
|---|---|
| Goods and item rows (`ItemFactory`, `ItemRow`, components, `ItemQuality`, `ItemQualityRules.Score` stream 0x223), `Store`, `MassLedger` | exists |
| `RecipeBook` mass check pattern (outputs <= inputs + `adds_kg`) | exists (`Production/RecipeBook.cs`) |
| Furniture pieces (`pieces.jsonc`: hearth, oven, forge, anvil, loom, work bench...) and `RoomDetector` | exists (Phase 1); stations gain `provides` qualities below |
| Skills (`Skill` enum), skill gain by use | `trade-inventory-character.md` 4.5 |
| Light level at a cell (`LightGrid`, 0-255), `LightModel` torch (`TorchRadius 6`, `TorchIntensity 235`) | exists |
| Jobs (`Craft` kind, resuming unfinished work), schedules | `live-behaviour-and-utility-ai.md` |
| Clothing numbers (`ConditionPermille`, `size_multiplier`), butchering outputs | `clothing-warmth-exposure.md` |
| Condition and effect vocabulary (`has_proficiency`, `has_item`, `near_fire`) | exists (`BaseVocabulary`) |
New: `Ravensguard.Sim.Crafting` (`RecipeDb`, `CraftJobTable`, `CraftRunner`, `Proficiency`, `Requirements`, `Teaching`); content `crafting/recipes_basic.jsonc`, `requirements.jsonc`, `proficiencies.jsonc`.
**Skill enum additions** (append only; used by this and other Phase 3 files): `Tailoring, Surgery, Speech, Athletics, Tactics, Stealth, Lockpicking` (plan list of skills; `Skill` today has 27 entries).

## 3. Data

### 3.1 Recipe (`crafting/recipes_basic.jsonc`)
```jsonc
{ "type": "craft_recipe", "id": "bake_loaf", "name": "Bake a loaf", "category": "food", "skill": "baking", "difficulty": 25, "effort": "moderate",
  "proficiencies": [ { "id": "bread_making", "time_mult_pm": 1600, "skill_penalty": 10 } ],
  "components": [ [ { "good": "flour", "kg": 1.0 }, { "good": "wheat", "kg": 1.1, "needs_step": "grind" } ],        // each inner array = alternatives
                  [ { "good": "water", "litres": 0.35 } ], [ { "good": "salt", "kg": 0.01 } ] ],
  "using": [ "oven_or_hearth" ], "tools": [ { "quality": "cooking", "level": 1 } ],
  "workstation": "oven", "min_light": 40, "min_attr": {},
  "steps": [ { "id": "knead", "minutes": 15, "attended": true },
             { "id": "rise", "hours": 3, "attended": false, "deadline_h": 2, "grace_h": 3, "container": "bowl", "ruined_into": "dough_sour" },
             { "id": "shape", "minutes": 10, "attended": true },
             { "id": "bake", "minutes": 40, "attended": true, "fuel": { "good": "firewood", "kg": 0.6 }, "critical": true } ],
  "results": [ { "good": "bread", "kg": 1.3 } ], "byproducts": [ { "good": "ash", "kg": 0.02, "chance_pm": 1000 } ], "adds_kg": 0.35,
  "batch": { "max": 6, "time_scale_pm": 800 },
  "learn": { "known_by_all": false, "auto_at": { "skill": "baking", "level": 10 }, "master": true, "watch_hours": 6, "manuscript": "bread_book", "decomp": false },
  "culture": "any", "secret": "none", "morale": 0 }
```
- **Units:** time `minutes` or `hours` (game time; 20 game minutes = 1 real minute at 1x); mass `kg`, volume `litres`; permille/`_pm` for chances and multipliers; `min_light` is the 0-255 ambient/lamp level at the crafting cell; difficulty 0-100 on the skill scale.
- **Component alternatives:** the first satisfiable alternative is chosen by the player's pick or the screen's default (cheapest by book price); consumed unless `"keep": true` (a mould). `needs_step` adds a step before the main work for that alternative (grinding wheat).
- **Tool qualities** have levels (cutting 1, hammering 2, smithing 3). A quality can be supplied by a carried item, a nearby tool on a station, or **level 0** (an improvised thing, such as a rock for hammering): `tool_bonus -10` and `time x1.5`. Tools with charges or fuel (`"charges"` or `"fuel"`) reduce it per use.
- **Steps:** attended steps need the crafter present and working; unattended steps are state in the world (3.3). Each step may add its own `tools`, `qualities` and `proficiencies` (as Cataclysm's recipe steps). The step marked `critical` is where failure can occur (4.4).
- **Results** are goods (stock) or item shapes with `material_from` a component class (for garments); result quality uses 4.3; goods only lose yield at low quality (4.3).
- **Requirement sets** (`requirements.jsonc`, the `using` list): `oven_or_hearth` = a heat source (station with `heat >= 2` or a lit fire within 2 m) + fuel; `fire_and_pot` = a lit fire within 2 m + tool `cooking 1`; `needle_and_thread` = `sewing 1` + `yarn 0.01 kg` per hour of sewing; `smithing_set` = forge (`heat 3`), anvil (`hammering anvil`) and tongs; `water_source` = water within 8 m or carried 2 L.
- **Station qualities:** furniture `build_piece`s gain `provides`: hearth `{cooking 2, heat 2}`, oven `{cooking 3, heat 3, baking 1}`, forge `{heat 3, smithing 1}`, anvil `{hammering 3 as anvil}`, work bench `{workbench 1}`, brew vat `{brewing 1}`, loom `{weaving 1}`; a tool item shape lists `qualities` (knife `cutting 1`, axe `cutting 1, chopping 1`, saw `sawing 1`, needle `sewing 1`, hammer `hammering 2`, steel and flint `fire_starting 1`, pot `cooking 1, boiling 1`, quern `milling 1`, whetstone `whetting 1`).

### 3.2 Proficiencies (`crafting/proficiencies.jsonc`)
```jsonc
{ "type": "proficiency", "id": "bread_making", "name": "Bread making", "requires": [ { "id": "cookery", "min_pm": 300 } ], "learn_pm_per_hour": 8, "teach_mult_pm": 2000, "manuscript_mult_pm": 1500 }
```
Eleven in Phase 3 (a graph; no cycles): `fire_craft`, `cookery`, `brewing_basics`, `bread_making` (needs cookery 30%), `preserving` (cookery 30%), `needlework`, `tailoring` (needlework 40%), `woodcraft`, `metalworking_principles`, `blacksmithing_basics` (metalworking_principles 50%), `first_aid`, `herbalism` (first_aid 30%).
Stored per tracked person as a sorted list `(proficiencyId, Pm 0..1000)` (about 40 bytes for a hero). **Gain:** `+8 pm` per hour of attended work in a step that lists it, `x2` with a master teaching, `x1.5` from a manuscript session; reaching 1000 takes 125 hours of use. **Effect below 100%:** `time x (1 + (time_mult_pm/1000 - 1) x (1000 - Pm)/1000)` and `effective_skill = skill - skill_penalty x (1000 - Pm)/1000`.

### 3.3 `CraftJob` (the unfinished item) and unattended state
`CraftJob { Id (EntityId), Recipe, Crafter (person), Owner, Place (cell or station id), Batch, StepIndex, StepSeconds (progress inside the current step), Store (id of the job's component Store), ScoreSum, State, ReadyHour, DeadlineHour, RuinHour }` with `State` in `Active, Paused, Waiting (unattended), Done, Ruined`.
- Consumed components are **moved into the job's `Store`** (not destroyed) so progress is physical, mass is conserved, and a ruined batch returns its materials as `ruined_into` goods (dough becomes `dough_sour`, then waste; cured meat becomes rotten meat; their mass stays in the world until it spoils or is dumped).
- An unfinished job is visible in the world as an object on the station/ground (a bowl under a cloth, a pit, a barrel). **Anyone with the recipe can resume it** (hero, a squad member, an NPC worker with the `Craft` job kind); interruption keeps `StepSeconds` and `ScoreSum`.
- Unattended steps run on **game hours**: `ReadyHour = start + step.hours`, `DeadlineHour = ReadyHour + deadline_h`, `RuinHour = DeadlineHour + grace_h`. They are kept in a due-queue (min-heap by hour) and need **no per-tick work**; the abstract hourly tick pops due jobs and posts a `CraftDue` reminder to the owner (a HUD alert: "Your dough is ready").

### 3.4 Content layout
`content/base/crafting/` : `recipes_basic.jsonc` (34 recipes below), `requirements.jsonc` (5 sets), `proficiencies.jsonc` (12), `manuscripts.jsonc` (3: `bread_book`, `herbal_notes`, `farrier_notes`). New goods needed (added to `goods.jsonc` with prices from the price solver): `pottage, roast_meat, flatbread, dough_sour, bandage, poultice, splint, torch, twine, rope, lye_soap, horseshoe_set, snare, bedroll, rags, ash` (ash exists in production as a byproduct if not, added), `scrap_iron, tool_handle`.

## 4. Rules

### 4.1 The recipe list (33; times are attended minutes/hours of the crafter, unattended in the notes)
| # | Id | Cat | Skill (diff) | Key inputs (alternatives with `/`) | Tools / station | Output | Notes |
|---|---|---|---|---|---|---|---|
| 1 | `kindle_fire` | Fire | Forestry (5) | tinder 0.05 kg (straw/hay/oak_bark) + firewood 0.5 kg | fire_starting 1 (or level 0 bow drill, x2 time, fail +20%) | lit fire (fuel 0.5 kg) | 3 min; success per weather (travel spec 4.3); known by all; ash 0.02 kg |
| 2 | `make_torch` | Fire | Carpentry (5) | wood stick 0.4 kg + rag 0.1 kg (cloth_wool/cloth_linen/rags) + tallow 0.05 kg | cutting 1 | torch (burns 4 game hours) | 5 min; known by all |
| 3 | `make_candle` | Fire | Housekeeping (15) | tallow 0.3 kg + twine 0.01 kg | none | 4 candles (candles good) | 20 min + set 1 h unattended (deadline 6 h) |
| 4 | `cook_pottage` | Food | Cooking (10) | grain/peas/beans 0.3 kg + vegetables 0.2 kg + water 1.0 L (+ salt 0.005) | fire_and_pot | pottage 1.3 kg (0.62 measure/kg = 0.8 measure, a day's staple) | 25 min; known by all |
| 5 | `roast_meat` | Food | Cooking (15) | meat_fresh 1.0 kg + salt 0.01 | fire + spit (workstation) | roast_meat 0.75 kg | 30 min |
| 6 | `bake_flatbread` | Food | Baking (10) | flour 0.5 kg + water 0.25 L | hot stone/griddle (hearth) | flatbread 0.7 kg | 20 min; known by all |
| 7 | `bake_loaf` | Food | Baking (25) | flour 1.0 kg (or wheat 1.1 + grind) + water 0.35 L + salt | oven | bread 1.3 kg | steps 15, **rise 3 h unattended**, 10, 40 |
| 8 | `brew_home_ale` | Food | Brewing (25) | malt 2.1 kg + firewood 1.5 kg + water 6 L | brew vat or cauldron, barrel/crock | ale 5 L | mash 60 min, **ferment 72 h** (deadline 48 h, grace 96 h), strain 15 |
| 9 | `salt_meat` | Food | Butchery (20) | meat_fresh 5 kg + salt 0.7 kg | barrel or crock | meat_salted 5.2 kg | rub 20 min, **cure 120 h** (deadline 72, grace 120) |
| 10 | `make_soft_cheese` | Food | Husbandry (20) | milk 5 L + salt 0.02 kg + offal 0.01 (rennet) | pot, cloth | cheese 0.5 kg + whey 4.4 kg | warm 20 min, **curdle 4 h** (deadline 4, grace 8), press 15 |
| 11 | `grind_meal` | Food | Milling (10) | wheat/rye/barley 1.1 kg | quern (`milling 1`) | flour 1.0 kg | 30 min per kg of grain |
| 12 | `bandage_linen` | Medical | Surgery (5) | cloth_linen/cloth_wool/rags 0.1 kg | cutting 1 | 4 bandages | 4 min; optional boil 10 min -> `clean` (halves infection risk of an undressed wound, §4A.3) |
| 13 | `herbal_poultice` | Medical | Surgery (25) | herbs 0.1 kg + cloth 0.05 + tallow 0.02 | pot | poultice | crush 10, steep 20 min; -50% infection risk for 1 game day |
| 14 | `wooden_splint` | Medical | Carpentry (10) | sticks 0.5 kg + 2 bandages | cutting 1 | splint | 10 min; fracture treatment aid |
| 15 | `mend_garment` | Cloth | Tailoring (10) | yarn 0.01 kg | `needle_and_thread` | +300 condition (max 700), `Repaired` flag, value -10% | 20 min per +300; known by all |
| 16 | `patch_garment` | Cloth | Tailoring (20) | cloth 0.1 kg + yarn | `needle_and_thread` | +300 condition (max 800), removes tear flag | 30 min |
| 17 | `sew_shirt` | Cloth | Tailoring (20) | cloth_linen 1.2 kg + yarn 0.02 kg | sewing 1, cutting 1, bench | shirt (size chosen: "to fit") + rags 0.5 kg | cut 30 min, sew 3.5 h |
| 18 | `sew_hood` | Cloth | Tailoring (25) | cloth_wool 0.9 kg + yarn | as above | hood + rags 0.35 | 3 h |
| 19 | `sew_bedroll` | Cloth | Tailoring (25) | cloth_wool 3.0 kg + straw 2 kg + yarn | as above | bedroll 3.2 kg | 6 h |
| 20 | `refit_garment` | Cloth | Tailoring (30) | cloth 15% of the garment's mass + yarn | as above | garment at a new size (fits the wearer, +/-1/7 rule) | 2 h per 1/7 of size change |
| 21 | `lye_soap` | Cloth | Housekeeping (20) | ash 1.0 kg + water 3 L + tallow 0.5 kg | fire_and_pot | lye_soap 1.2 kg | boil 30 min, **cure 48 h** (deadline 120, grace 240); one piece washes 4 items |
| 22 | `spin_twine` | Craft | Spinning (10) | wool_raw 0.12 kg | spindle (`spinning 1`) | twine 0.10 kg | 20 min |
| 23 | `braid_rope` | Craft | Spinning (15) | twine 0.5 kg | none | rope 0.45 kg | 40 min |
| 24 | `brain_tan_hide` | Craft | Tanning (30) | hide_raw 3 kg + oak_bark 1 kg + water 10 L | scraper (`scraping` via knife `cutting 1`), tub | leather 2.4 kg | scrape 1 h, **soak 24 h** (deadline 48, grace 48), work 1 h |
| 25 | `make_snare` | Craft | Forestry (15) | rope/twine 0.3 kg + stick 0.2 kg | cutting 1 | snare | 15 min |
| 26 | `carve_handle` | Craft | Carpentry (15) | logs 1 kg (ash/oak wood) | cutting 1 | tool_handle 0.4 kg | 1 h; used to remake axe or hammer heads |
| 27 | `repair_cart_wheel` | Craft | Carpentry (30) | timber_planks 3 kg + nails 0.1 kg | hammering 1, sawing 1 | wheel hit points restored | 2 h; the cart within 3 m |
| 28 | `forge_horseshoe_set` | Smith | Smithing (30) | iron_bar 1.6 kg + charcoal 2 kg | `smithing_set`, hammering 2 | horseshoe_set 1.4 kg (4 shoes) + scrap_iron 0.15 | 1.5 h attended; proficiency blacksmithing_basics 50% |
| 29 | `sharpen_blade` | Smith | Smithing (10) | none (whetstone wears 1%) | whetting 1 | +150 condition on a blade | 15 min |
| 30 | `forge_nails_small` | Smith | Smithing (15) | iron_bar 0.5 kg + charcoal 0.5 kg | `smithing_set` | nails 0.45 kg | 1 h |
| 31 | `mend_tool` | Smith | Smithing (20) | iron_bar 0.1 kg + charcoal 0.2 kg | `smithing_set` | +300 condition | 30 min |
| 32 | `practice_sampler` | **Practice** | Tailoring (10) | cloth_linen 0.2 kg | needle_and_thread | rags 0.18 kg (no item) | 30 min; gain `d = 40`, stops at level 30 |
| 33 | `practice_billet` | **Practice** | Smithing (12) | iron_bar 0.3 kg + charcoal 0.3 kg | `smithing_set` | scrap_iron 0.25 kg (no item) | 40 min; gain `d = 45`, stops at 30 |
| 34 | `practice_spoon` | **Practice** | Carpentry (8) | wood 0.3 kg | cutting 1 | shavings 0.28 kg (no item) | 30 min; gain `d = 40`, stops at 30 |
Hands-on recipes are conserved: **every recipe passes the load-time mass check** (`outputs + byproducts <= inputs + adds_kg`), `adds_kg` only for water and air (dough, ale, soap).

### 4.2 Time, effort, light
- **Time** of a step = `step time x proficiency factor x tool factor (level 0: x1.5) x skill_factor/1000`, with `skill_factor = 1000 - clamp(skill - difficulty, -30, 50) x 4` permille (50 above the difficulty is 20% faster, 30 below it 12% slower). A **batch** of `n` (<= `batch.max`) takes `time x (1 + (n - 1) x time_scale_pm/1000)` for the attended steps (0.8 per extra unit by default); unattended steps take the same time for the whole batch.
- **Effort:** `light` / `moderate` / `heavy` add `+0 / +20 / +50 permille FatiguePm per attended hour` and burn food faster (`+10% / +25% / +50%` nutrition use; Phase 2 `NutritionCd` unchanged, a nutrition factor flag); `min_attr` (strength 35 for heavy smithing) blocks recipes below it. A recipe liked or hated by the crafter (`morale` +3/-3 `Mood` per hour; traits Phase 6 sets the sign).
- **Light:** the light level at the crafting cell (0-255) must be >= `min_light`; below it the screen says "too dark" and refuses; between `min_light` and `min_light + 40` quality gets `-5`. Torches and candles count (`LightGrid`). Candles provide level 120 within 2 m, a torch 235 at its position falling with distance (`LightModel`).

### 4.3 Quality of results
`effective_skill = skill - sum(skill_penalty_i x (1000 - Pm_i)/1000)`; `score = effective_skill + Range(-20, +20) (stream 0x223, key crafter, draw output serial) + tool_bonus (-10..+10: level-0 tool -10, tool above requirement +2 per level, station fine tools +5) + material_bonus ((avg input tier - 2) x 5, a Crude input -10, Fine +5, Masterwork +10) + light_penalty (0 or -5)`; `ItemQualityRules.FromScore` (thresholds 15, 40, 70, 90: Crude, Poor, Common, Fine, Masterwork). Item results carry the tier; **goods** (food, bundles) have no tier and instead scale yield by `Crude 700, Poor 850, Common 1000, Fine 1000, Masterwork 1000` permille. `LabourHours` and `Skill` are recorded on the item row (value formula, items spec 3.2).
- **Practice** recipes train skill and proficiency but give no sellable item; `cap_level` stops gain.
- **Skill gain:** each attended step finished counts as one meaningful use at `d = max(30, difficulty) x 2` (so a 25-difficulty recipe is `d = 50`, hard recipes up to 200); rate cap as in the character spec.

### 4.4 Failure and waste
At the `critical` step: `fail_pm = clamp(100 + 20 x (difficulty - skill), 0, 800)` (0 if `skill >= difficulty + 5`), roll stream 0x360 keyed `(crafter, jobId, step)`. On failure the batch loses `waste_pct = clamp(30 + (difficulty - skill), 30, 100)`% of its components (moved from the job `Store` into a `waste` stock: mass still conserved, as rags, ash, bones, slag) and the result quality drops one tier (`Crude` if already Poor). The screen shows the chance before you start ("Risk of failure: 30%; you would lose about 40% of the flour").

### 4.5 Unattended steps: deadline, grace, ruin
For an unattended step starting at hour `S` with `time h`, `deadline_h` and `grace_h`: `Ready = S + h`; **full quality** if collected in `[Ready, Ready + deadline_h]`; if collected in the first half of the grace window, result quality `-1 tier`; second half, `-2 tiers` (floor Crude); after `Ready + deadline_h + grace_h` the job is `Ruined` (becomes `ruined_into`). The HUD alerts at `Ready` and again at `Ready + deadline_h` and at ruin minus 1 h. Example: dough set to rise at 08:00 (rise 3 h, deadline 2, grace 3): ready 11:00, no penalty until 13:00, -1 tier 13:00-14:30, -2 tiers 14:30-16:00, ruined after 16:00.
- Waiting uses the speed controls (1 to 8x when safe); the hero may leave and do other things; NPCs may be asked to collect (`Craft` job, Phase 5 stewards). Weather can interfere (rain on a drying pit): `Weather` flag halves `deadline_h` for outdoor steps (`tan pit` uncovered).

### 4.6 Learning recipes
| Way | Rule |
|---|---|
| Known by all | `kindle_fire, make_torch, cook_pottage, bake_flatbread, bandage_linen, mend_garment` (basics) |
| Skill unlock | `auto_at {skill, level}`; at the level the recipe appears with a toast |
| Master (apprenticeship) | an NPC who knows the recipe with skill >= difficulty + 10: Q > Ask to teach. Session of `learn_hours = 4 + difficulty/10` game hours working side by side (paid: `learn_hours x skilled wage (35 mites/h)`, or the same hours of labour for the master, one choice). Result: recipe known, skill gain `x2` and proficiency gain `x2` during the session |
| Watching | stay within 6 m of a crafter performing the recipe; `Perception` check each 10 game minutes; at `watch_hours = 6 + difficulty/10` accumulated hours you may "try it": the recipe is learnt with probability `p = 400 + 5 x Wits + 3 x skill` permille (cap 950; stream 0x362, key `(person, recipe)`); on failure the watch counter resets to 50% |
| Manuscript | item `manuscript`; read at `Literacy >= 20` for `2 + difficulty/10` hours; teaches the listed recipes and gives `manuscript_mult` proficiency gain; manuscripts are found in ruins, bought from monasteries, or stolen (crime in Phase 6) |
| Taking apart | disassemble an item made by the recipe (`decomp: true`): learn it if `skill >= difficulty - 10`; the parts return as components (the item's components, 70% by mass, plus byproducts) |
Recipes have `culture` and `secret: guild` flags; Phase 3 stores them and shows "taught only in the guild" but guild enforcement is Phase 6.

### 4.7 The crafting screen
- **Open:** **N** (Make, new binding in the world context, to be conflict-checked), Q on a workstation (filters to its qualities), Q on an inventory item ("What can I make with this?", "How is this made?").
- **Layout** (Cataclysm-style): left, search box and category tabs (Fire, Food, Medical, Cloth, Craft, Smith, Practice); middle, the list with colour markers (green = can make now, amber = missing something you can get, grey = known but not possible, "?" = unknown, hidden by default); right, the detail: components with alternatives (the chosen one bold, others listed, have/need counts and which container they are in), tool qualities (have/need, where from), workstation, light need vs available, skill (yours vs difficulty), proficiencies (your %, time and skill effect), time (attended and elapsed), **expected quality** (computed as the distribution of `score` over the 41 rolls: for example "Poor 24%, Common 73%, Fine 2%"), failure chance and waste, byproducts, effort.
- **Actions:** Craft (1), Craft batch (n), Queue (adds a `Craft` job for you or a worker), Pin, Resume (list of unfinished jobs nearby). **Nearby containers and furniture** within 6 m are used automatically for components and tools; a "what's missing" list highlights alternatives you could use.
- Pressing Craft starts the job: progress bar per step, Esc pauses (progress kept), accelerate with 2-4 (when no hostiles).

### 4.8 Tick placement
| Where | What |
|---|---|
| Live 20 Hz | `CraftRunner` advances `StepSeconds` by game seconds per tick for the crafter's active job (a few jobs only); consumption of fuel/charges at step ends |
| Barrier | consume components (`Store.MoveUpTo`), create results (`ItemMaterialise` / stock moves), proficiency and skill updates |
| H2 | abstract `Craft` jobs for NPC workers credit hours when LiveLeased is off (Phase 5 for businesses) |
| H5 hour 0 / hourly | due-queue pop: `CraftDue` alerts, `Ruined` transitions |
LODBridge: a `CraftJob` is a record with a `Store`; promote/demote leaves it as is; an NPC mid-craft is a `Work` abstract job with `JobId`.

## 5. Determinism
| Stream | Use | Key |
|---|---|---|
| 0x223 | craft quality roll (exists) | crafter, output serial |
| 0x360 | failure roll at the critical step | crafter, jobId, step |
| 0x361 | byproduct chance rolls (ash, offcuts) | job |
| 0x362 | learning by watching | person, recipe |
No `float`: durations in game seconds, permille factors, integer division with floor. Content ordering by ordinal id. Recipe database dense ids by ordinal string order (overview 2.2).

## 6. UI and controls
N makes the screen (Esc closes), Q on stations and items. Hotbar button "Make". A first-use hint: "N opens crafting; green recipes can be made now". The controls overlay lists N and (in the crafting screen) arrow keys, Enter to craft, Tab to cycle the detail panes, `/` to search, F to toggle "can make now".

## 7. Tests (golden)
| id | Case | Expected |
|---|---|---|
| K-1 | Data validation: 34 recipes load; all goods, qualities, requirement sets, proficiencies resolve; proficiency graph has no cycle; unattended steps have deadline and grace | pass; a recipe with a missing alternative good fails with file and id |
| K-2 | Mass check: every recipe `outputs + byproducts <= inputs + adds_kg` | pass; `bake_loaf` 1.36 in, 1.3 + 0.02 out |
| K-3 | Quality distribution: skill 50, difficulty 25, tool bonus 0, no proficiencies | over the 41 rolls: Poor (scores 30-39) 24.4%, Common (40-69) 73.2%, Fine (70) 2.4%, Crude/Masterwork 0 |
| K-4 | With tool bonus +5 | Poor 12.2%, Common 73.2%, Fine 14.6% |
| K-5 | Proficiency: `bread_making` 40%, skill 50 | effective skill `50 - 10 x 0.6 = 44`; time factor `1 + 0.6 x 0.6 = 1.36`; quality Poor 39%, Common 61% |
| K-6 | Failure: skill 20, difficulty 30 | `fail_pm = 300`; waste 40%; at skill 35 (>= difficulty + 5) 0 |
| K-7 | Unattended window: rise starts 08:00 | ready 11:00; full quality until 13:00; -1 tier at 13:30 and 14:00; -2 tiers at 15:00; `Ruined` at 16:01; no change when the game is saved and loaded in between |
| K-8 | Interrupt and resume: stop `sew_shirt` at 2 h 10 min, another person resumes with the same recipe | progress kept (`StepSeconds` 7,800), result credited to the finisher; components from the same job `Store` |
| K-9 | Mending: garment condition 350 | `mend_garment` -> 650, flag `Repaired`; a second -> 700 (cap); `patch_garment` -> 800 |
| K-10 | Level-0 tool: hammer a nail with a rock | allowed; time x1.5, tool bonus -10 |
| K-11 | Learning: master `learn_hours` for difficulty 25 | `4 + 2.5 = 6.5` hours, cost `6.5 x 35 = 227.5` -> 228 mites (rounded half up), recipe known, skill gain x2 |
| K-12 | Watching: Wits 50, skill 20, difficulty 25 | needs `6 + 2.5 = 8.5` h of watching; `p = 400 + 250 + 60 = 710` pm; deterministic with stream 0x362 |
| K-13 | Practice: `practice_billet` at skill 30 | no gain (cap), no item; at skill 12 gain per use `0.4 x 0.45 x 0.774 = 0.139` levels (`d_x100 = 45`, `t = 8,800`: `45 x 8,800^2 / 25,000 = 139,392` micro-levels) |
| K-14 | Batch: 4 loaves | time `65 min x (1 + 3 x 0.8) = 221` attended minutes; unattended rise stays 3 h (one pot of dough) |
| K-15 | Light: sewing needs 100 | at 90 refused ("too dark"); at 130 allowed with the `-5` quality penalty (below `100 + 40 = 140`); at 140 no penalty |
| K-16 | Screen: expected quality list equals the simulated one over 10,000 seeds (within 1%) | pass |
| K-17 | Conservation: craft 1,000 random recipes with failures and ruin | `MassLedger` and `Store` totals exact (waste and ruined goods counted) |
| K-18 | Determinism: replay a recorded day of crafting | identical item ids, qualities and stock |

## 8. Open questions for the user
1. **Recipe count and content depth:** 34 recipes now (plan: about 30). Default as listed; more arrive with Phase 5.
2. **Crafting speed-up:** crafting runs at the chosen game speed; attended steps allow up to 4x (8x blocked because the crafter must watch the work). Default as written.
3. **Skill additions:** `Tailoring, Surgery, Speech, Athletics, Tactics, Stealth, Lockpicking` appended to `Skill` (plan skill list). Default accept.
4. **Key for crafting:** N is free in the world map; the plan's key list has no crafting key. Default N.
5. **Food quality:** food goods have no tier (yield only); alternative: food quality affects mood. Default yield only.

## 9. Budgets
| Item | Budget |
|---|---|
| `RecipeDb` can-make evaluation for 300 recipes (cached inventory scan) | <= 1 ms; incremental on inventory change |
| Crafting screen open | <= 3 ms |
| `CraftRunner` per tick (<= 20 active jobs in the bubble) | <= 0.05 ms |
| Due-queue | O(due) per hour; <= 0.1 ms |
| Memory/save | `CraftJob` ~80 bytes + store rows; <= 500 jobs per region = under 100 KB |
