# Clothing layers, warmth, wetness, exposure, stains and butchering

Plan refs: §2.10 (four DF layers: under, over, armour, cover; coverage per body part with the named gaps; a piece fits anyone within +/-1/7 of its
size; each layer adds encumbrance; insulation against cold and heat; clothes get wet in rain, heavier and colder until dried; blood and dirt
stains lower value until washed; wear levels; creatures and plants are made of materials: butchering yields tissue materials), §3c (weather:
rain, snow, wind, heatwave; "wet plus cold, set against clothing insulation, causes hypothermia; heatwaves cause heatstroke"; "cold slows
hands; heat tires anyone in heavy armour"), §2.10 "People suffer cold or heat according to their clothing's insulation", §5 Phase 3 ("clothing
layers, wetness, warmth and exposure; butchering"). Phase 2: `households-and-needs.md` (clothes wear, "`Clothing` stat, Phase 3 mood"), `items-mass-value-spoilage.md`.

## 1. Purpose
Make what you wear matter. A person's clothes set how much cold or heat the body can stand; rain and rivers soak clothes and remove that
protection; fire, shelter and dry cloth restore it. The model is small and integer-only, shared by the live tier (the hero, party, and people in
the bubble) and the abstract tier (a cheap per-person outfit value). Butchering turns a corpse into goods by the tissue materials it is made of.

## 2. Dependencies
| Needs | From |
|---|---|
| `armour_shape` content (`layer`, `thickness_mm`, `layer_size`, `layer_permit`, `size_multiplier`, `coverage`, `gaps`) | exists (`content/base/items/armour.jsonc`: 8 shapes: shirt, tunic, gambeson, mail_hauberk, breastplate, helm, gauntlets, cloak) |
| Body parts: `head, neck, upper_body, lower_body, upper_arm, lower_arm, hand, upper_leg, lower_leg, foot` | exists (`bodies/human.jsonc`) |
| `ItemRow` (`Flags.Wet/Stained`, `Stains`, `ConditionPermille`, `Wear`, components) | exists; one new column below |
| `RegionWeather`/`WeatherSample` (temperature, wind, precipitation, snow), `WeatherState` | exists (`Weather/`) |
| `DeathCause.Cold` | exists (`People/Types.cs`) |
| Body plans with tissue layers, `BodyPlan.Build` | exists (`Combat/BodyPlan.cs`, `bodies/human.jsonc`, `materials/tissues.jsonc`) |
| Utility AI `warmth` need and `Shelter` action | `live-behaviour-and-utility-ai.md` 3.4, 4.1 |
| Crafting recipes for garments, soap, repair, refit | `basic-crafting.md` |
New code: `Ravensguard.Sim.Survival` (`ClothingMath`, `Exposure`, `Wetness`, `Stains`), `Ravensguard.Sim.Butchery` (`CarcassYield`).

## 3. Data

### 3.1 New clothing and material fields (content)
`armour_shape` gains (all with units):
| Field | Unit | Meaning |
|---|---|---|
| `insulation_clo_x100` | clo x 100 at full coverage | 15 = a linen shirt; 100 = a gambeson |
| `windproof_pm` | permille | share of wind chill blocked (cover layer shapes are high) |
| `waterproof_pm` | permille | share of rain kept out |
| `heat_retention_pm` | permille | extra heat load in hot weather (armour traps heat) |
| `enc_pm` | permille | encumbrance added per piece (agility, stamina) |
| `absorb_pct` | percent of dry mass | water the piece holds when wet |
| `wet_loss_pm` | permille | insulation lost at full wetness (wool 400, linen 650, leather 500, gambeson 600) |
New shapes in Phase 3 (content, about 12): `braies` (under, legs), `hose` (over, legs), `hood` (cover/over, head+neck), `coif` (under, head), `gloves` (over, hands), `mittens` (over, hands), `boots`
(over, feet), `shoes` (over, feet), `jerkin` (over, torso), `fur_coat` (cover, torso and arms and legs), `oiled_cloak` (cover), `bedroll` (not worn).
| Shape | Layer | Insulation (clo x 100) | Windproof | Waterproof | Heat retention | Enc | Absorb | Wet loss |
|---|---|---|---|---|---|---|---|---|
| shirt (linen) | under | 15 | 50 | 0 | 0 | 0 | 35 | 650 |
| tunic (wool) | over | 45 | 150 | 100 | 50 | 10 | 50 | 400 |
| hose (wool) | over | 25 | 100 | 100 | 20 | 10 | 50 | 400 |
| gambeson (linen, wool fill) | over | 100 | 300 | 50 | 400 | 30 | 70 | 600 |
| cloak (wool) | cover | 80 | 600 | 250 | 100 | 20 | 50 | 400 |
| oiled cloak | cover | 70 | 850 | 800 | 150 | 25 | 15 | 200 |
| fur coat | cover | 160 | 800 | 300 | 350 | 40 | 40 | 500 |
| hood (wool) | cover | 25 | 300 | 150 | 30 | 0 | 50 | 400 |
| boots (leather) | over | 30 | 400 | 450 | 60 | 10 | 40 | 500 |
| gloves (leather) | over | 20 | 400 | 300 | 30 | 5 | 40 | 500 |
| mail hauberk | armour | 20 | 100 | 0 | 300 | 80 | 0 | 0 |
| breastplate | armour | 10 | 300 | 0 | 600 | 60 | 0 | 0 |
| helm | armour | 10 | 200 | 100 | 250 | 30 | 0 | 0 |
All numbers are first guesses, flagged as tuning (`clothing.jsonc`), but the totals are chosen so a peasant's winter outfit is about 1.0 clo (below), a full
winter kit with cloak and hood about 2 clo, and a knight's harness in a heatwave is dangerous.

### 3.2 New columns
| Where | Column | Type | Unit | Meaning |
|---|---|---|---|---|
| `ItemRow` | `WetPm` | `ushort` | permille 0..1000 | water content of a worn or carried textile or leather item; replaces the boolean `ItemFlags.Wet` (flag kept as `WetPm > 200`) |
| `ItemRow` | `StainKind` | `byte` flags | `Blood=1, Dirt=2, Mud=4, Grease=8, Soot=16, Set=128` | with the existing `Stains` (0..255 amount) |
| `PersonTable` (tracked persons only: live bubble, hero, squad) | `CoreCenti` | `short` | centi-degrees C, 3700 = 37.00 | core temperature |
| `PersonTable` | `OutfitI` | `byte` | clo x 100, 0..255 | **abstract** cached outfit insulation for people who are not tracked (below) |
| `PersonTable` | `ExposureMin` | `ushort` | game minutes, saturating | time since last at a comfortable temperature (UI, mood) |
`CoreCenti` is stored for the persons being simulated live; abstract people hold `CoreCenti = 3700` and use closed-form hourly exposure only when conditions are extreme.

### 3.3 Content: `survival/exposure.jsonc`
```jsonc
{ "type": "exposure_settings", "id": "default",
  "comfort_c": 22, "clo_per_degree_x100_at_met100": 1400, "max_deficit_clo_x100": 300, "cool_cC_per_hour_at_max": 80, "rewarm_cC_per_hour": 150, "rewarm_fire_cC_per_hour": 250,
  "heat_free_c": 24, "heat_full_hl_x10": 600, "heat_rise_cC_per_hour_at_max": 100, "heat_cool_cC_per_hour": 100,
  "regions_pm": { "head": 100, "torso": 400, "arms": 150, "hands": 50, "legs": 220, "feet": 80 },
  "met_x100": { "sleep": 80, "rest": 100, "walk": 180, "work": 220, "sprint": 350, "combat": 350 },
  "indoor_offset_c": { "unheated": 5, "fire": 15 },
  "wet_gain_pm_per_min_per_mm_h": 5, "wet_leak_inner_pct": 20,
  "dry_pm_per_hour": { "fire_worn": 250, "fire_hung": 400, "indoor_worn": 60, "indoor_hung": 90, "outdoor_worn": 70, "wind_bonus": 40, "sun_bonus": 30 },
  "stain_value_loss_pm_max": { "Blood": 400, "Dirt": 200, "Mud": 150, "Grease": 250, "Soot": 150 } }
```
Region to body part map: head = `head, neck`; torso = `upper_body, lower_body`; arms = `upper_arm, lower_arm`; hands = `hand`; legs = `upper_leg, lower_leg`; feet = `foot`. Weights sum to 1,000 permille.

## 4. Rules

### 4.1 Layers, coverage, fit
- **Layer order** (inner to outer, plan §2.10): `under, over, armour, cover`. An item can be worn over another if its `layer_size` is within the lower item's `layer_permit` (DF rule; the build already stores both). Each body part holds at most one item per layer where the item covers it (coverage > 0); `order` breaks ties inside a layer (armour: mail `order 0` under plate `order 1`).
- **Coverage** per body part is the shape's fraction 0..1, with the named gaps from §2.6 (armpit, groin) as holes for the Phase 4 resolver; for warmth a gap is just the missing coverage.
- **Fit:** an item has `size_multiplier x body size`; it fits a wearer whose body size is within +/-1/7 of the item's design size, else it **cannot be worn** (the equip button shows "too big/small; refit at a tailor or armourer", crafting 4.9). Fit is checked at equip time only.
- **Encumbrance** (agility): `agility_pm = 1000 - sum(enc_pm of worn pieces)`, floor 300, used by stamina (`+1% stamina cost per 10 enc_pm`) and (Phase 4) combat timing; a gambeson plus mail plus helm is 140 enc_pm.
- **Condition and wear:** insulation scaled by `(500 + ConditionPermille/2)/1000` (tattered at 200 -> 0.6). Wear levels shown as words: worn (< 700), threadbare (< 400), tattered (< 200), falls apart at 0. Clothes tear when the wearer is cut (Phase 4 hook `TearFromHit(layer, depth)`), reducing condition by `depth_mm x 20` permille.
- **Households (Phase 2):** wear flows stay as they are (1,095-day clothes). Phase 3 adds only **owned pieces to worn pieces**: a person wears the best outfit their household owns (pieces >= members: everyone dressed; fewer: the youngest share).

### 4.2 Insulation of a person
```
I_item_region = insulation_clo_x100 x coverage_region x condition_factor x (1000 - wet_loss_pm x WetPm/1000)/1000     (clo x 100)
I_region      = sum over worn items covering the region of I_item_region                                        (layers add linearly)
I_eff         = sum over regions of regions_pm x I_region / 1000                                                 (clo x 100)
windproof_o   = max windproof_pm over worn items on the torso (the outermost layer dominates)
```
Integer arithmetic; coverage as permille. A **winter peasant outfit** (shirt, tunic, hose, wool cloak, hood, boots, gloves; coverage as in the shapes) has about `I_eff = 96` (0.96 clo); adding a gambeson `+0.34 clo` of torso-weighted insulation.

### 4.3 Cold: required insulation, stress, core temperature
```
T_air      = weather temperature at the person (Fixed, deg C); inside a roofed building: T_air + indoor_offset (unheated 5, lit fire within 4 m 15), wind 0, rain 0
windchill  = min(12, max(0, wind_mps - 2) x 0.6) x (1000 - windproof_o)/1000                       (deg C)
T_eff      = T_air - windchill
met        = met_x100 by Action (sleep 80, rest 100, walk/jog 180, work 220, sprint or combat 350)
I_req      = max(0, comfort_c - T_eff) x 1400 / met_x100                                           (clo x 100)
deficit    = max(0, I_req - I_eff)
cold_stress_pm = min(1000, deficit x 1000 / 300)
dCore (centi-C per game hour) = - cold_stress_pm x 80 / 1000 x 1000 / resist_pm       when cold_stress_pm > 0
                              = + 150 (250 within 4 m of a lit fire) capped at 3700       when cold_stress_pm == 0
resist_pm  = 1000 + (toughness - 50) x 6                                                            (toughness 50 -> 1.00)
```
Eating regularly is required for rewarming: with `NutritionCd < 500` rewarming is halved. Stepped once per **game minute** (live: at the minute boundary of the clock, so at 8x it runs 8 times as often per real second, not per tick; abstract: once per game hour for outdoor persons when `T_eff < 5 C`).
- **Hypothermia states** (core temperature):
| State | Core | Effects |
|---|---|---|
| Normal | >= 36.00 C | none; `cold_stress_pm > 300` adds the `warmth` need (AI seeks shelter) |
| Chilled (shivering) | < 36.00 | dexterity -10%, `Mood -15`, can't sleep outdoors well (rest recovery -50%) |
| Moderate | < 35.00 | dexterity -25%, speed x0.85, stamina regen -30%, confusion: AI-controlled people stop choosing work |
| Severe | < 32.00 | collapse (unconscious) after 30 game minutes unless rewarming starts; chronicle `Cold` cause |
| Lethal | < 28.00 | death (`DeathCause.Cold`, plan death rules) |
- **Timeline example** (HUD shows it, plan item 13): at `cold_stress 457` a person cools 0.37 C/h: chilled after 2.7 h, moderate after 5.4 h, severe after 13.7 h.

### 4.4 Heat and heatstroke
```
HL (heat load)  = max(0, T_air - 24) x met_x100 / 100 x (100 + I_eff/3) / 100 x (1000 + sum heat_retention_pm of worn)/1000          (x1 scale; 6.0 = full)
heat_stress_pm  = min(1000, HL x 1000 / 60)
dCore (cC/h)    = + heat_stress_pm x 100 / 1000          when heat_stress_pm > 0
                = - 100 (shade or resting: x1.0; swimming in water: x3)  when T_air <= 24 and core > 3700
```
Shade (a roof or tree canopy) lowers `T_air` by 4 C for this purpose; drinking water gives -15 cC per litre drunk (cap 3 L/h). Heatwave weather state sets `T_air` +6 C beyond the seasonal curve (weather).
| State | Core | Effects |
|---|---|---|
| Warm | > 38.00 C | `Mood -15`, stamina regen -25%, AI seeks shade (`Shelter` action) |
| Heat exhaustion | > 39.50 | speed x0.80, dexterity -25%, may collapse |
| Heatstroke | > 41.00 | unconscious; death after 2 game hours unless cooled below 40.00 (`DeathCause.Other`, chronicle `Weather`) |
Armour in a heatwave: a knight in gambeson (400), mail (300), helm (250) and breastplate (600) has `retention 1,550`, so the heat load is multiplied by 2.55 (golden C-4).

### 4.5 Wetness and drying
- **Wetting (outdoors, rain or snow melt):** per game minute each *exposed* item (cover layer on covered parts; if no cover, the outermost) gains `precip_mm_h x 5 x (1000 - waterproof_pm)/1000` permille of `WetPm`; snow counts at 50%. The next inner layer gains `wet_leak_inner_pct (20)` of the outer's gain while the outer is above 700. **Immersion** (rivers, fords, a fall): every worn item to the waist is set to 1,000 at once; swimming = all items.
- **Drying:** per game hour, subtract by environment: near lit fire (<= 4 m) 250 worn / 400 hung; indoors unheated 60 / 90; outdoors (not raining) 70 worn, `+40` if wind > 4 m/s, `+30` in sun (clear daytime); none in rain or fog beyond 20. Hung items need a rack or a peg (a `drying` tagged furniture piece, Phase 1 `furniture`, or a rope in the camp).
- **Mass:** `mass = dry mass x (100 + absorb_pct x WetPm/1000)/100`; a soaked wool cloak (1.8 kg) weighs about 2.7 kg; feeds the encumbrance table (trade-inventory spec 4.3).
- **Frozen clothes** (T_air < -2 C, wet): treated as `WetPm` fixed and `wet_loss` +100 pm until thawed (Phase 4 frostbite).

### 4.6 Abstract tier (no per-person wetness)
Abstract persons are not given item-level wetness. Each person's `OutfitI` (clo x 100) is derived daily at the H4 slice: pieces owned per member `< 1`: 40; `1`: 90; `>= 2`: 140; `+40` if the household owns a cloak or fur; gentry `+40`. For hours when the person is outdoors (schedule) and `T_eff < 5` or `T_air > 30`, the hourly exposure runs the same math with `OutfitI`, wet 0 (rain hours `wet_loss 300` average) and adds `ExposureMin`; abstract deaths from cold use Phase 2 vital rates (`cold mortality multiplier`), now **also** `DeathCause.Cold` when the person reaches lethal core. Because it is only for extreme hours, the cost is below 0.5 ms per game hour region-wide.

### 4.7 Stains and washing
- Added: `Blood` += bleed ml / 4 (cover or over layer at the wound's body part, per 5 game seconds of bleeding); butchering +40 per carcass on over/cover; `Mud` +1 per 4 m walked on mud (legs, feet items); `Dirt` +2 per game hour of field or smithing work (over layer); `Soot` near open fire +1 per 15 game minutes; rain removes `Mud` -2 per game hour outdoors.
- `Stains` is the amount (0..255). **Value loss** `= value x min(max_pm(kind), Stains x max_pm / 255)/1000` (blood up to 40%, dirt 20%). Staining is cosmetic (no insulation effect) except `Blood` that sets (after 6 game hours unwashed the `Set` flag makes it permanent without lye soap).
- **Washing:** Q on a water source (river, well, trough) -> "Wash (item)": 10 game minutes per item, removes 100% `Mud` and `Dirt`, and `Blood`/`Grease` 70% (not `Set`); with `soap_lye` (a Phase 3 good, one piece per 4 items) 100% including `Set`. Washing wets the item to 1,000 and needs drying. NPC servants and laundresses use the same function in the abstract tier by household `cleanliness` (not modelled; note).
- **Social effect:** `hero_is_bloody` (sum of `Blood` amounts on visible layers > 60) changes dialogue and shop refusal (`dialogue-barks-rumours.md`).

### 4.8 Butchering into tissue materials
- A corpse is an entity with its **body plan** and carcass mass `M kg = SizeCm3 x 1.05 g/cm3 / 1000` (deer 80 kg, boar 100, cow 500, sheep 45, horse 450; the Phase 2 recipe numbers `slaughter_sheep/cow/pig` are the abstract version). It stays butcherable for `butcher_fresh_hours = 24` after death at full quality, until 72 h at `quality -1 tier` and `spoilage x2`; after that it decays into a skeleton (plan §2.2: about a game week).
- **Yield by tissue**: each creature's `butcher` block maps tissue material to a good and a recovery:
```jsonc
{ "butcher": { "muscle": { "good": "meat_fresh", "share_pm": 440, "recover_pm": 1000 }, "skin": { "good": "hide_raw", "share_pm": 70, "recover_pm": 1000 },
               "fat": { "good": "tallow", "share_pm": 70, "recover_pm": 600 }, "bone": { "good": "bone", "share_pm": 120, "recover_pm": 750 },
               "organs": { "good": "offal", "share_pm": 60, "recover_pm": 1000 }, "horn": { "good": "horn", "share_pm": 5, "recover_pm": 800 } } }
```
  `share_pm` is derived from the body plan at load (sum over parts of part relative size x tissue layer thickness, normalised, scaled so the generic ungulate gives the table above; 175 permille is gut contents and 50 blood and is never recovered). Any new creature (or a draugr with `dried_flesh`) gets yields from its own tissue materials, as plan §2.10 asks, with no new code.
- **Amount:** `grams = M x share_pm x recover_pm x yield_pm / 10^9`, `yield_pm = min(1000, 700 + 6 x Butchery)` with +/-4% variance (stream 0x332). At skill 50 the abstract Phase 2 numbers are matched (sheep: 20 kg meat from 45 kg).
- **Time and tools:** `hours = (2.5 + 0.015 x M) x (1 - (skill - 50)/200)` game hours (deer at skill 50: 3.7 h; sheep 3.2 h, cow 10 h as the Phase 2 recipes), tool `cutting >= 1` (a knife) and a flat place; `sawing 1` needed for bone from carcasses over 150 kg. Hung carcass (a rack) -25% time. One person; two people halve the time (max 2).
- **Products** become item rows or stock in the butcher's `Pack`: `meat_fresh` (spoils fast, `House` class), `hide_raw`, `tallow`, `bone`, `offal`, `horn`. Meat in a pack spoils at `Open` rates (items spec 3.6); the player may salt it (recipe, basic crafting).
- **Rules:** people are never butcherable (the action does not appear); horses are butcherable unless the culture has `butcher_taboo: horse` (the action shows the reason; a nearby witness of a taboo culture raises a Phase 6 crime). Butchering a hunted animal in the wild gains Butchery skill (`d = 70` per half hour) and stains the butcher.
- **Dressing time pressure:** a hot day (T_air > 25) halves `butcher_fresh_hours` (12 h).

### 4.9 Repair, refit and wear with crafting
Mending (sewing quality 1), patching (tattered +300 condition), re-fitting to another size (tailor: cost 25% of the garment's materials, time 2 h per 1/7 of size change) and soap are crafted in `basic-crafting.md`; this file defines the numbers they act on (`ConditionPermille`, `size_multiplier`, `Stains`).

### 4.10 Tick placement
| Where | What |
|---|---|
| Live (20 Hz) | minute-boundary pass for live persons: wetness gain/dry, `CoreCenti`, state transitions, stains; weather sample from the bubble's `HourEnv` per tile region (interpolated once per game minute) |
| H4 daily slice | `OutfitI` recompute, household pieces vs members |
| Hourly (outdoor extremes) | abstract exposure per person on extreme-weather hours |
| Barrier | death commands (`PersonDied` with `Cold`), wash/butcher results as `ItemMaterialise` / stock moves |
LODBridge: `CoreCenti`, `WetPm`, `Stains` are record columns, so promote/demote needs no transfer; on demotion a tracked person's core continues in the hourly path with the current outfit.

## 5. Determinism
| Stream | Use | Key |
|---|---|---|
| 0x330 | stain accumulation rounding, tear rolls | item |
| 0x331 | per-person tolerance jitter at creation (toughness-based +/-3% on `resist_pm`) | person |
| 0x332 | butchery yield variance (+/-4%) | carcass entity |
All thresholds are integers in centi-degrees; weather is read from the deterministic `WeatherSample` (Q16.16); `max`, `min`, divisions use floor; no `Math.Pow`.

## 6. UI and controls
- **HUD condition icons** next to the clock: cold (snowflake, 3 levels), hot (sun), wet (drop); a tooltip gives the numbers: "Warmth 0.96 clo, need 1.71 clo at -4 C walking (wind chill -1 C); core 36.4 C; chilled in 1.3 h". Plan item 13: a **cold timeline** bar.
- **Character sheet > Condition** tab: a body diagram coloured by region insulation, wetness per item, the "Warmth needed" bar; item tooltips show insulation, windproof, waterproof, enc and wet state.
- Q on an item: Wear, Remove, Hang to dry (near a rack or fire), Wring out (-150 pm wet, 2 game minutes), Wash, Mend. **Q on a corpse**: Butcher (shows yields for the current skill, time and tools missing). **E** on water: Drink.
- Hints (once each): first rain ("Wet clothes lose warmth; stand by a fire or hang them to dry"), first chill, first heatwave. Controls overlay `?` lists "Hang to dry" and "Butcher".

## 7. Tests (golden)
| id | Case | Expected |
|---|---|---|
| C-1 | Winter peasant outfit; walking (met 180), `T_air = 0`, wind 0, `I_eff = 96` | `I_req = 22 x 1400/180 = 171`, deficit 75, `cold_stress = 250`, cooling 0.20 C/h; chilled (36.0) after 5.0 h |
| C-2 | Same, `T_air = -8`, wind 8 m/s, cloak windproof 600 | windchill `min(12, 6 x 0.6) x 0.4 = 1.44`; `T_eff = -9.44`; `I_req = 31.44 x 1400/180 = 244`; deficit 148; stress 493; 0.39 C/h; chilled 2.5 h, moderate 5.1 h, severe 12.7 h |
| C-3 | Heat: `T_air = 34`, working (220), `I_eff = 120`, no armour | `HL = 10 x 2.2 x 1.4 = 30.8`; stress 513; +0.51 C/h; warm (38.0) after 1.9 h |
| C-4 | Knight in gambeson + mail + helm + breastplate (retention 1,550), same weather | `HL = 30.8 x 2.55 = 78.5`; stress 1,000 (capped); +1.0 C/h; heatstroke (41.0) after 4.0 h |
| C-5 | Wetting: wool tunic (waterproof 100) with no cover layer, 4 mm/h rain | +18 pm per game min: soaked (1,000) in 56 min; oiled cloak (waterproof 800): +4 pm/min, 250 min |
| C-6 | Wet wool cloak (loss 400), `WetPm = 1000` | insulation x 0.60; mass 1.8 kg -> 2.7 kg |
| C-7 | Drying a soaked cloak | by a fire worn: 4.0 h; hung by fire: 2.5 h; outdoors dry no wind: 14.3 h; indoors unheated worn: 16.7 h |
| C-8 | Indoors unheated at `T_air = -5`: `T_eff = 0` (no wind), walking indoors (met 180) | stress as C-1: slow cooling; at a lit fire `T_eff = +10`: `I_req 93`, deficit 0, rewarm +2.5 C/h |
| C-9 | Fit: item designed for size 100 | wearer sizes 86.0..114.3 fit (`+/-1/7`); size 85 or 115 refused |
| C-10 | Stains: 60 blood on over layer, value 100 | blood max 400 pm: loss `min(400, 60 x 400/255) = 94` pm: value 90.6; washing with lye soap -> 0, 100 |
| C-11 | Butcher deer (80 kg), skill 50 | meat 35.2 kg, hide 5.6, tallow 3.36 (fat 5.6 x 0.6), bone 7.2 (9.6 x 0.75), offal 4.8; 3.7 game h; skill 20: yield 820 pm: meat 28.9 kg |
| C-12 | Sheep 45 kg skill 50 vs Phase 2 `slaughter_sheep` | meat 19.8 vs 20, hide 3.15 vs 3, tallow 1.9 vs 2, bone 4.05 vs 4, offal 2.7 vs 3 (each within 10%) |
| C-13 | Corpse age: 30 h old | meat quality -1 tier, spoilage x2; at 80 h: `Butcher` not offered |
| C-14 | Cold death: tracked person with no shelter, -20 C, no cloak | `DeathCause.Cold`, chronicle entry `Cold`, within 13 game hours |
| C-15 | Abstract vs live parity: 100 persons same outfit, 24 h at -10 C | abstract core vs live core within 0.3 C at the end |
| C-16 | Determinism and save | `WetPm`, `Stains`, `CoreCenti` round-trip byte-identical; replay identical |

## 8. Open questions for the user
1. **How lethal is cold?** Default: severe (`< 32 C`) kills only after the person is already unconscious and left outside; the HUD gives hours of warning. Alternative: gentler (halve cooling) or brutal (as written, without the 30-minute collapse grace).
2. **NPC wetness:** abstract NPCs have no wet clothes (cost); live bubble NPCs do. Default as written.
3. **Frostbite and sunburn:** deferred to Phase 4 (wounds). Default deferred.
4. **Butchering people** is blocked. Default blocked (a taboo mod could enable it).

## 9. Budgets
| Item | Budget |
|---|---|
| Live exposure and wetness for 300 persons (once per game minute, so about every 60 ticks at 1x, 8 ticks at 8x) | <= 0.15 ms per tick average |
| Hero/squad detailed calc (<= 13 persons) | <= 0.05 ms |
| Abstract exposure per hour (only outdoor extreme hours) | <= 0.5 ms |
| Stains and drying (items worn by live persons only, <= 2,000 rows) | <= 0.05 ms |
| Butcher job calc | <= 0.1 ms |
| Memory | `CoreCenti` and exposure columns for tracked persons only (<= 400 rows live); `WetPm` and `StainKind` +3 bytes per `ItemRow` (about +60 KB for a typical world) |

## 10. Notes (implementation of clothing, warmth and exposure; butchering is left for step 6)

**Built.** `Ravensguard.Sim.Clothing` (`Garments`, `ClothingMath`, `ClothingBook`, `ClothingSave`, `AbstractExposure`); content in `content/base/clothing/` (`garments.jsonc`: braies, hose, hood, coif, gloves, mittens, boots, shoes, jerkin, fur coat, oiled cloak, bedroll; `wardrobe.jsonc`: starting clothes by class and season; `exposure.jsonc`; `materials.jsonc`: fur) and the new fields on the existing shapes in `items/armour.jsonc` (`ArmourShapeDef` gained `insulation_clo_x100`, `windproof_pm`, `waterproof_pm`, `heat_retention_pm`, `enc_pm`, `absorb_pct`, `wet_loss_pm`, `mass_g`, `default_material`, `rig_layer`, `wearable`). Tests: `tests/Ravensguard.Sim.Tests/Clothing/ClothingTests.cs` (C-1 to C-10, C-14 to C-16, the winter-night scenario, the scorer, the bridge at 1x and 8x).

**Decisions that differ from the text above.**
1. **Side table, not `ItemRow` columns.** `WetPm` (kept as 1/100 permille), `StainKind`, dye and the size a piece was made for live in `ItemExtra`, a table keyed on the item and saved with the book, so the economy's `ItemRow` and `EconomySave` did not change. The book owns its own `ItemTable` and an id allocator in region 255 (no world region has that number), so the live tier dresses people on the main thread without touching the economy's table. Worn clothes are real rows (mass by shape mass scaled by material density, value with labour, condition, wear).
2. **Core temperature scale.** `CoreU` = centi-degrees x 60,000, so a per-hour rate in milli-centi-degrees is exactly one minute of change and nothing is rounded away.
3. **Heat scale.** `heat_full_hl_x10 = 600` means a heat load of 60.0 is full stress (the golden C-3 and C-4 use 60, the "6.0 = full" in 4.4 is a slip).
4. **Rewarming deadband.** A person at cold stress at or below `comfort_stress_pm` (100) rewarms rather than cools, so people in mild cold keep their heat. Every golden number has stress of 250 or more and is unchanged. Sleepers in a bed get `bedding_clo_x100` (250: straw tick and blankets).
5. **Fire.** A lit fire within four metres gives the +15 C offset outdoors as well as indoors, and the rewarm rate; the hero's "near a fire" is a strong static light on a roofed cell with no torch of their own; an NPC is by a hearth when indoors and the air is under 10 C (churches are unheated).
6. **Numbers measured.** Winter peasant outfit `I_eff` = 90 (spec "about 96"; the cloak's coverage was widened to 0.9 torso and 0.6 lower leg so a cloak hangs like one); a gambeson adds 56 over all regions (34 is its torso-weighted share). Layer stacking: different layers always stack; in one layer pieces stack when their main body part differs or their orders differ and the DF size rule holds (gambeson over tunic yes, two cloaks or boots with shoes no).
7. **Cold lethality (open question 1, default).** Severe (< 32 C) for 30 game minutes makes a person collapse; only a collapsed person at lethal (< 28 C) dies, and warming stops it. HUD warnings come first (hero: "you will be chilled in 1.3 h", chilled, cold, hypothermia). A man in a shirt, tunic and hose outdoors at -20 C chills in 0.4 h, collapses at 6.7 h and dies at 11.2 h (C-14).

**NPCs and the scorer.** `PeopleBridge.AttachClothing(book, ambient)` dresses each person at promotion (a stable draw on their id; cold pieces only in the cold seasons) and steps their body at each game-minute boundary from the weather at that second at their home, the place their schedule gives at that second (indoors, hearth, bed) and their activity, so 1x and 8x give identical bodies (tested). The scorer reads `cold_stress_pm`, new `wet_pm` and `heat_stress_pm` needs, and a new `shelter_override_pm` profile value (600): a person far too cold or hot, or wet and getting cold, takes the Shelter action. Shelter changes the body (the person counts as under cover and by a fire) but not the position, because positions are a pure function of the schedule; walking to the nearest door is a later step. People who die of exposure are queued in `TakeExposureDeaths()`; applying `DeathCause.Cold` at a barrier is not wired yet.

**Abstract tier.** `AbstractExposure` has the outfit value (`OutfitI`), the extreme-hour test, the cold stress for the shared scorer and `StepHour`, which matches sixty live minutes (C-15). It is pure and cheap but not yet called from the H4 slice or written to `PersonTable` (the abstract slice and `DeathCause.Cold` hookup belong to the economy and vital streams).

**Wear.** Clothes lose condition by `1000 / lifetime` a game day of wear, the lifetime being the Phase 2 `clothes_lifetime_days` of the person's class (730 for peasants). The remainder is kept in the item's `Wear` column.

**View.** The rig's cloak and hood layers are drawn only when such a piece is worn, tinted by the piece's dye (outer wool pieces are dyed from the class palette) or by its material through the existing tint shader; the hero shows the cloak and hood they wear. HUD condition line under the weather row (English text, not yet in the localization file). Sleepers lie on their own bed: `BedAxis` (beds side by side in a row are lain across it, others along x) and `LivePerson.LieAxis`; across a row the figure uses the rig's fall pose, upright, because the rig has no top-down lying sprite.

**Not done.** Butchering (C-11 to C-13), frozen clothes, washing and stain jobs in the world (the functions exist), the character-sheet Condition tab, the wear/remove/wring menu, a save section for the book (the book round-trips byte-identical; the hero's core and wetness are not yet in `WorldSave`, NPC clothes are re-derived at promotion), and `DeathCause.Cold` applied to the person records.
