# Travel: road encounters, night camp, foraging and hunting, auto-travel at 8x, feasts and fairs

Plan refs: §3 item 5 ("travel is a game, not a wait": data-driven road encounters, night camp with cooking, sentries and companion talk, forage and
hunt along the way, auto-travel at 8x with interrupts), §1 (speeds: jog 3 m/s, cart 2.5, trot 5; 8x only with no hostiles in view; at 8x the live bubble
shrinks to the party and the road ahead, the abstract tick coarsens, live ticks are not multiplied), §3c (feasts and fairs: a spring fair, the harvest festival,
the midwinter feast; markets, tournaments, weddings and hiring days cluster around them; rain closes fords), §2.2a, §5 Phase 3 ("travel activities (road encounters,
night camp, foraging)", "feasts and fairs", first ride in the done-when), §3i (interrupts shared with autopilot). Phase 2: `caravans-and-logistics.md` 3.5-3.7
(abstract caravan encounters, stream 0x230), `resources-and-extraction.md` (forage and game stocks), `seamless-regions.md` (gates, region edges).

## 1. Purpose
Turn a trip of tens of game hours into a sequence of things that happen: someone to meet on the road, a place to stop for the night, food to find, and an
automatic travel mode that gets you there at 8x and stops when something matters. Everything here is data on top of systems that already exist: the road graph,
the bubble, the weather, the needs and the economy's tile stocks.

## 2. Dependencies
| Needs | From |
|---|---|
| Road edges, `PathPlan`, `PositionOf`, abstract caravans and encounter rolls | Phase 2 (specified; LODBridge milestone P3-A) |
| `LiveWorld` speeds (`JogSpeed 3`, `SprintSpeed 5`, 8x sub-steps), `LevelPathfinder` | exists |
| `ResourceLayer` (`Forage`, `Game`, `Fish` stocks per tile), extraction depletion and regrowth | exists (`Production/Resources`) |
| Weather (`WeatherSample`, fords, `Daylight`), `Calendar.FeastOn/NextFeast` (`Spring Fair` day 8, `Midsummer` 23, `Harvest Festival` 38, `Midwinter Feast` 53) | exists |
| Exposure (core temperature, wetness), butchering | `clothing-warmth-exposure.md` |
| Jobs (Escort), utility AI `Shelter/Rest`, schedules, sleeping rules | `live-behaviour-and-utility-ai.md` |
| Talk, rumours, companion fireside lines | `dialogue-barks-rumours.md` |
| Cooking recipes, snares, fire-making | `basic-crafting.md` |
| Mounts, carts and speeds | `horses-and-carts.md` |
| Squads, loyalty | `trade-inventory-character.md` |
New: `Ravensguard.Sim.Travel` (`EncounterDirector`, `TravelAuto`, `InterruptBus`, `Camp`, `Forage`, `Hunt`, `FairSystem`); content `travel/encounters.jsonc`, `travel/camp.jsonc`, `travel/fairs.jsonc`.

## 3. Data

### 3.1 Encounter (`travel/encounters.jsonc`)
```jsonc
{ "type": "encounter", "id": "bandit_ambush_small", "kind": "Ambush", "weight_pm": 60,
  "when": [ { "id": "road_tier_at_most", "args": { "tier": "road" } }, { "id": "danger_at_least", "args": { "pm": "300" } },
            { "id": "terrain_near", "args": { "kind": "woodland", "within_m": "30" } }, { "id": "hero_party_strength_below", "args": { "pm": "700" } } ],
  "spawn": { "groups": [ { "archetype": "bandit", "count": [3, 5], "kit": "bandit_light", "place": "cover_ahead", "min_m": 40, "max_m": 90 } ] },
  "script": "ambush_demand",              // behaviours: approach_talk, demand_toll, ambush_attack, flee, block_road, beg, trade, ask_help, pass_by
  "outcomes": { "pay": "toll_dialogue.bandit", "fight": "combat", "evade": "flee_chase" },
  "cooldown_game_hours": 24, "min_from_settlement_m": 600, "once_per_leg": true, "scale_first_two_days_pm": 300 }
```
Archetypes (`person_templates.jsonc`) give occupation, skills, gear kit, mood and a short bark set; spawned people are **real records** (vagrants: Phase 2 E15, `Homeless.Vagrant`
persons are used first; new bandits are only taken from those; otherwise the encounter is skipped, so no people appear from nothing).

### 3.2 Encounter table v1 (24 entries; weights are relative permille among eligible ones)
| Id | Kind | Weight | Needs (summary) | What happens |
|---|---|---|---|---|
| `lone_traveller` | Pass | 140 | any road | walker or rider passes; greeting bark; news (rumour wrapper) |
| `peddler` | Trade | 80 | any road | sells small goods from a pack (trade screen) |
| `merchant_cart` | Trade | 70 | road | a real abstract caravan promoted: trade, rumours, may ask for escort |
| `pilgrims` | Pass | 40 | road, holy day within 5 days | group of 4-8, blessing line, rumour |
| `shepherd_flock` | Block | 50 | pasture | sheep fill the road for 60 s (time cost, a small `FarmWork` offer) |
| `woodcutters` | Work | 40 | woodland | offer a haul job (logs) |
| `hunters` | Trade | 40 | woodland | sell game (meat), warn of wolves, may invite along |
| `refugees` | Ask | 40 | `famine` or `raid` within 15 days in chronicle | ask for food or coin; refusal costs nothing; help gives `Familiarity` and a rumour |
| `wounded_soldier` | Ask | 25 | `war` or `battle` within 30 days | asks for bandages (item), tells war news |
| `patrol` | Check | 70 | lord's land | guards ask your business; armed + bloody (stain > 60) is suspicious, `hero_is_armed` line |
| `tinker` | Trade | 35 | road | repairs for pay (basic crafting repair job done by NPC) |
| `drunk` | Pass | 25 | within 2 km of tavern, evening | harmless; `bark` and tiny rumour |
| `lost_child` | Job | 15 | daytime | escort job: return to village, reward 1 day wage plus `Trust` |
| `broken_cart` | Ask | 40 | road, rain within 2 days | help push (strength check, 3 min) for coin, or buy goods |
| `fallen_tree` | Obstacle | 25 | woodland, storm within 1 day | blocks path; sawing (quality) or detour; woodcutters appear 10 game min later |
| `flooded_ford` | Obstacle | 30 | river crossing, heavy rain | ford closed (weather rule); wait, detour to bridge, or a ferryman offers a crossing (pay) |
| `toll_keeper` | Check | 40 | gate or bridge | toll applies (O5), Phase 2 tolls |
| `highwaymen` | Demand | 45 | danger >= 400 | demand 10% of coin; pay, fight or bluff (Speech skill) |
| `bandit_ambush_small` | Ambush | 60 | danger >= 300, woodland near | attack from cover (Phase 4 combat resolves) |
| `wolves` | Animal | 35 | woodland, winter or hungry year | pack of 3-6 stalks at dusk (Phase 4 creatures; v1 uses snarl barks and a fight) |
| `bear` | Animal | 4 | deep woodland | rare, deadly, warning signs first (tracks, carcass) |
| `army_column` | Column | 6 | war active within 25 km | a column marches past on the road; stop, hide or join; never fights you unprovoked in Phase 3 |
| `shrine_omen` | Omen | 20 | fog night, solstice, storm | roadside shrine, a superstition rumour, a once-off buff `Mood +10` |
| `foraging_ground` | Find | 30 | woodland, forage stock >= 600 g/tile | prompts a safe place to forage (section 4.4) |
Totals about 1,030 permille over all rows; only the eligible rows are normalised each draw. Wars and raids come from the chronicle, so the table reflects the world's recent history.

## 4. Rules

### 4.1 When encounters happen
- A leg is a stretch of hero movement outside a settlement plan rect and not inside a building. Every **10 game minutes of leg time** (not frames; so at 8x the check runs 8x as often per real second but the same per game hour) the director rolls with stream 0x340 keyed `(hourKey = game hour, heroId, draw = 10-minute slot 0..5)`:
  `p_pm = base(road tier) x night(x2 at night) x danger/500 x region_calm(x0.5 within 600 m of a settlement) x scale_first_days` where `base` = 28 pm (highway/paved), 40 (road), 55 (track), 70 (off-road), and cooldowns apply (at most one scripted encounter per 30 game minutes, and the same id not again within its cooldown).
- Expected frequency on a road in a normal region (danger 300): `40 x 0.6 = 24 pm per 10 min` = 1 scripted encounter per 7 game hours (21 real minutes at 1x); an 8 km trip on foot (15 h) gives about 2 encounters, a night in a dangerous woodland about 1 per 3 h.
- **Danger** (`RegionDanger`, pm 0..1000, recomputed daily per 2 km cell from records, never stored): `100 + 40 x vagrants per 1,000 residents + 300 if a Raid chronicle entry in the last 15 days within 10 km + 400 if war front within 20 km + 100 in a famine year + 50 at night`, capped at 1000.
- **Placement:** the encounter spawns ahead of the hero at 120-180 m along the path (just beyond promotion range, so it arrives into view) or behind for pursuit; ambushes spawn in cover tiles 40-90 m ahead; animals approach from woodland edge. A spawn uses `PromoteQueue` with the Phase 2 rules, so no more than 50 entities per frame.
- **Abstract caravan encounters** (Phase 2 stream 0x230) remain as is; if a caravan is inside the bubble when the hero meets it, the live outcome flows back via `CaravanLoss` (existing).
- **Safe first hours:** `scale_first_two_days_pm = 300` and ambush-type encounters are disabled in the first 2 game days of a new game and when the party has an escort contract from a mentor (`onboarding-v1.md`). The start rule (no start within 10 km of war or 3 km of a lair) is in onboarding.

### 4.2 Auto-travel at 8x with interrupts
- **Start:** Q on the map or world > **Travel here** (in the Map screen: **Enter** on the selected place; `T` is called shot and is not used here; autopilot uses the same verb). The route is the HPA* road-graph path (road edges and tile paths; fords avoided if closed). The hero walks the path on the NPC movement code; the party follows in a column 2 m apart (`Follow` order), mounted members keep within 15 m; carts set the speed (horses-and-carts).
- **Speed gate:** 8x is allowed only if no hostile or unknown armed person is in view (vision range from `LightModel` and `HeroVision`), no party member has `threat_pm > 0`, the hero is not in combat stance, and no known hostile is within 200 m ahead on the path. Otherwise the clock drops to 1x and shows why ("Wolves in sight").
- **At 8x:** game time x8, live ticks stay at 20 Hz (not multiplied); movement runs in sub-steps of 0.25 m (`LiveWorld`); the bubble contracts to a **40 m radius around the party plus a 400 m corridor ahead along the path** (radius 40 m): everything else stays abstract; a stranger in the corridor still promotes (so encounters are seen). The abstract tick keeps stepping every game hour.
- **ETA:** speeds are real metres per real second at 1x, and the hero covers `speed x multiplier` per real second. `game_hours = path_m / (speed_m_s x 180)` (1 m/s at 1x covers 3,600/20 = 180 m per game hour) and `real_seconds = path_m / (speed_m_s x multiplier)`. The UI shows both: 8,000 m at 3 m/s is 14.8 game hours, 44 real minutes at 1x and 5.6 real minutes at 8x (plan section 1: "8 km is about 44 real minutes at 1x, 5.5 at 8x, about 15 game hours").
- **Interrupts:** travel raises interrupts through `InterruptBus` and keeps **no list of its own**. The canonical list of kinds (names, count, triggers, default policies) is `autopilot-v1.md` section 3.2: one append-only `InterruptKind` enum and one policy type `Pause | Notify | Ai`. Travel is the source of the rows marked "travel" there. Travel-side behaviour that the table does not repeat:
  - `Hostile` drops the clock to 1x in the same tick (V-6). `Dusk` follows the HUD setting continue / stop / make camp (default Notify).
  - `Hunger` auto-eats at the shared `hunger_eat_hours` (13, `live-behaviour-and-utility-ai.md` 4.1) with a 10-game-minute meal break; this spec has no hunger number of its own.
  - In manual play the `Ai` policy is not offered (the player is the AI). The old travel setting `ignore` is now `Notify` with the toast switched off (`quiet`); `quiet` is not allowed for `Attacked`. Settings key: `travel_interrupts`.
- **Journey log:** `Journey { Start, Dest, DistM, GameHours, CoinSpent, Encounters[], FoodUsedG }` appended to the Journal at arrival; chronicle entry only for notable events.

### 4.3 Night camp
- **Making camp:** Q on open ground > **Make camp** when: not within 200 m of a settlement plan (use an inn), flat tile (slope < 8%), not on water or deep mud, party present within 20 m. A camp is a record `Camp { Id, Cell, Fire { Lit, FuelG, Since }, Shelters[], Members[], Stock (Store id), Sentries[], MadeAt }` and chunk objects (fire pit, tents, logs). It decays after 3 game days (objects removed; fire pit stays, as a landmark of your passing).
- **Fire:** needs fuel (`firewood`, 5 kg per game hour burning, from the pack or gathered: gather 1.5 kg per game minute per person from woodland edge) and tinder + steel (recipe `make_fire`, basic crafting; skill-less success chance `700 + 3 x skill` pm in dry weather, `-300` rain/snow, `+200` with tinder dry). A lit fire gives light radius 12 m (`LightModel`), warmth `indoor_offset fire = +15 C` within 4 m (clothing spec), and a drying place. Fire attracts: bandits (`attract +200 pm danger`) and travellers; it keeps wolves away (animals need `Brave`).
- **Shelter:** bedroll (rest rate `-100` fatigue per hour instead of `-75` on bare ground), lean-to (+3 C T_eff offset, rain proof, 25 min to build from 4 poles + cover), tent (+5 C, rain proof, windproof; a good `tent` is a Phase 3 item: 8 kg, 1 min to pitch per person); sleeping in a tent by a fire is `-125` per hour as in a bed.
- **Cooking:** each cook uses basic-crafting recipes (`pottage`, `roast_meat`, `flatbread`, `boil_water`); a meal is eaten from the camp `Stock` or packs, **resetting `LastMealHour`**, eating 1.0 measure-equivalent per person-day as in Phase 2 (`food_measures_per_kg`), and creating `Familiarity +2` and `Loyalty +20` for companions who ate together (once per day). A cook with `Cooking` 40+ adds `Mood +5` ("a good meal"). Mass is conserved: eaten goods leave the stock rows.
- **Sentries:** up to 3; each watch is a shift of 2 game hours (the hero sets it from the squad panel, or members volunteer by loyalty and fatigue: `Guard` action in utility AI). A sentry's detection of an approaching person or animal: sees a hostile at `min(vision, light radius + 10 m)` where vision is `120 m` by day, `30 m` moonlit, `18 m` by fire, `8 m` dark; `notice_pm = 400 + 6 x perception - 150 if FatiguePm > 600`, rolled once per 20-game-second window (one real second at 1x) against each approacher, stream 0x344 keyed `(GameSec / 20, sentryId, draw = approacherIdx)`. A noticed threat sets an alarm bark and wakes sleepers within 15 m (fatigue and sleep depth: a sleeper wakes if `noise_m < 6` or alarm). **Unwatched camps:** approachers reach the fire; sleepers wake at 4 m with 50% chance per second.
- **Companion talk:** after the meal, `Talk around the fire` (hero action, 20 game minutes) opens a conversation with any companions present. Each companion offers one topic per night, chosen by priority: (1) **ambition reveal** (`AmbitionKnown` false: the first 3 nights with that companion reveal it, also shown on the squad panel), (2) a **backstory** node (authored for unique companions, systemic template otherwise), (3) opinion on the day's events (a rumour or chronicle entry from the last 3 days), (4) banter. Effects: `Familiarity +6`, `Loyalty +50` once per night per companion, `Mood +5` for everyone at the fire, `SocialPm -150`. The hero may choose questions; Esc skips (no penalty, but no gain).
- **Night events:** `camp_events.jsonc` rolls once per game hour in the camp's night hours (stream 0x346, keyed `(game hour, campId)`): fox steals food (-1 day of one food stock, if unwatched), wolf visit (winter), a lost traveller asking for the fire, rain starting (fire out unless sheltered), storm omen, a bad dream (companion mood). About 12 events, weights by `danger` and watch.
- **Breaking camp:** 10 game minutes; pack items return; the fire is doused or left (a lit unattended fire has `FireSpread` risk in dry grass in drought, Phase 5 fire sim uses it).

### 4.4 Foraging
- **Action:** Q on woodland/scrub/marsh tile > **Forage** (or **E** at a `foraging_ground` find), taking 10 game minutes per **sweep** of the 64 m tile around the hero (all one tile; the hero steps to the next tile for another sweep).
- **Yield per sweep:** `g = min(tile.ForageStock x 8%, 250 x (50 + Forestry) / 100)` grams, `Forestry` level 0..100 (skill `Forestry`), when the seasonal window is open (`Spring: herbs, roots; Summer: berries; Autumn: nuts, mushrooms; Winter: roots only, x0.3`); items by season table. The tile's `StockG` is **reduced by the same grams** (Phase 2 conservation; depletion per tile is real; regrowth is the annual refill at S1). An hour (6 sweeps) at skill 50 takes about 1.5 kg if the tile holds >= 3 kg.
- **Food value:** berries/nuts/roots use `food_measures_per_kg` (0.15 for vegetables; nuts 0.50); one hour of foraging is about 0.2 to 0.75 measures, a supplement, not a living (Phase 2: a forager takes 3 kg a day).
- **Identification:** `id_pm = 300 + 7 x Forestry`; unknown items show as "unknown plant"; eating an unidentified mushroom has `(1000 - id_pm)/10` pm per 100 g of poisoning (a mild disease with `NutritionCd -200`, stream 0x342, chronicle only if lethal). The skill gains `d = 50` per sweep.
- **Medicinal herbs** exist as goods for bandage recipes (basic crafting).

### 4.5 Hunting (basics)
- **Game animals** appear in the bubble from the tile `Game` stock: spawn density `animals per sq km = stock_kg / 150` capped at 8, species by biome and season (hare, roe deer, red deer, boar, fox; wolf and bear rare and in encounters); each is a creature entity with a body plan (butcherable). A shot or kill **reduces the tile `Game` stock** by its carcass mass (conservation with Phase 2).
- **Stalking:** an animal notices at `awareness_m = 25 + 5 x ... ` simplified: base 30 m, `x0.5` sneaking (Z), `x1.5` running, downwind `x0.6`, in `fog/rain x0.7`. Noticed animals flee at their speed; boar charge when wounded.
- **Weapons:** bow, sling, spear. Hit resolution in Phase 3 uses the existing `Combat.StrikeResolver` for arrows (flat shot, `Archery` skill, range falloff) so hunting is real, with Phase 4 polishing animals' AI. **Snares** (recipe): set up to 6; each is an unattended step with deadline 36 h and grace 12 h; catch chance per hour `20 x game_density/ (1+...)`: `catch_pm_per_hour = 15 + 3 x skill/10` for hare-size (0.8 kg meat per catch), the tile `Game` stock reduced; checking within the window collects it, later the snare is empty.
- **Butchering** at the kill: `clothing-warmth-exposure.md` 4.8 (hung 24 h freshness). **Legal:** O12 (Phase 2): hunting allowed everywhere in Phase 3 (poaching law in Phase 6).
- Skills trained: `Archery` (d = 100 per shot at a living target, 40 at stationary), `Forestry` (stalking, tracking), `Butchery`.

### 4.6 Feasts and fairs
- **Calendar:** `Calendar.FeastOn` supplies the day (default culture: Spring Fair day 8, Midsummer 23, Harvest Festival 38, Midwinter Feast 53); each culture renames them. `fairs.jsonc` gives type, duration and the settlement rule:
| Event | Days | Which settlements | Effects |
|---|---|---|---|
| Spring Fair | 3 (day 8-10) | towns with a market charter (3-4 per region) | temporary stalls +12, hiring day, tournament hook (Phase 4), wedding bump +20% |
| Midsummer | 1 (day 23) | every village | bonfire at dusk, dancing, social need x2 |
| Harvest Festival | 2 (days 38-39) | every settlement | feast, ale demand x1.4, harvest hire day (extra labour jobs), thanksgiving at church (tithe is Phase 2) |
| Midwinter Feast | 3 (days 53-55) | every settlement | feast, firewood and ale demand x1.3, `Social` need gets satisfied, the longest night omens |
- **Mechanics:** (1) **Schedules:** work blocks end at 13:00 (live-behaviour 4.3); evening `Socialise` at the square; church 09:00-11:00 as a holy day. (2) **Market:** a fair adds `FairStall` slots (`stall_rent_mites_per_day = 30`, rented by merchants on arrival from the caravans' destination set: Phase 2 trip decision adds `event_bonus_pm = 150` for a settlement with a fair in <= 3 days, so more caravans visit); structural demand of ale, meat, cloth, bread x1.2 for the fair days (not permanent). (3) **Hiring day:** the odd-job board posts 3x the usual jobs and labourers, guards and mercenaries gather at the square: **you can hire anyone there at the posted price** (trade-inventory 4.6). (4) **Rumours:** gathering group sizes up to 12, spread probability x1.5 (dialogue 4.6). (5) **Hero:** can rent a stall (E on the stall clerk) and tend it (TendShop job), play contests (archery butts, wrestling, foot race: skill uses at `d = 80` with small purses, 5-20 crowns, paid by the town treasury), buy and sell, and gain fame (reputation hook Phase 6).
- **Conservation:** purses are paid from the town/fief treasury (Phase 2), stalls' goods come from merchants' stocks, extra demand is a tilt of the price curve, not minted coin.
- **Announcement:** a bark and the Journal list "Spring Fair at Oakford in 3 days" (also a rumour kind `Fair`).

### 4.7 Tick placement
| Where | What |
|---|---|
| Live 20 Hz | `TravelAuto` movement (reuses NPC movement), `EncounterDirector` minute check, `InterruptBus` evaluation each tick (cheap flags), sentry notice rolls 1 Hz |
| Barrier | spawn requests (`PromoteQueue`), `PayCoin` for tolls and ferries, forage and hunt stock reductions as `StockDelta` commands, `Camp` create/destroy |
| H5 hour 0 | daily danger recompute per 2 km cell (about 65 cells x 2 per region; <= 0.5 ms), fair schedule check |
| H4 slice | none (fairs read the calendar at hour 0) |
| LODBridge | an encounter in progress is a bubble entity set; on demotion (the hero left): the participants continue as abstract `Travel` jobs, ambush survivors return to vagrant hideouts |

## 5. Determinism
| Stream | Use | Key |
|---|---|---|
| 0x340 | encounter roll and table choice | hero id, hour, minute slot |
| 0x341 | encounter placement (cover tile choice, counts) | encounter instance |
| 0x342 | forage poisoning, forage item mix | `GameSec / 200`, person, `draw = item index` |
| 0x343 | hunting: animal spawn picks, snare catches | tile, hour |
| 0x344 | sentry notice rolls | `GameSec / 20`, sentry, `draw = approacher index` |
| 0x345 | fair events, contest results | settlement, day |
| 0x346 | camp night events (`camp_events.jsonc`) | game hour, camp |
Every key is a game-time window (`GameSec` divided by 20, 200 or 3,600) or an entity or distance, never the live tick counter (overview section 5). The encounter outcome depends on game time and hero id only, never on frame rate or 8x vs 1x: the same journey gives the same encounters (test V-2).

## 6. UI and controls
- **Map (M):** right-click or Q on a place > **Travel here**; route line, ETA (game hours, real minutes at 8x), food needs and "provisions for 3.2 days" check.
- **Travel HUD:** a banner "Travelling to Oakford (6.1 km, 11 h)" with the interrupt icons, a **Stop** button (Space also pauses), and an "Auto-eat" and "Dusk" setting. **Esc** cancels. Speeds 1 2 3 4 as always; auto-travel forces 8x only when allowed.
- **Camp (Q > Make camp):** a small panel: Fire (light, add fuel), Shelter (tent, lean-to), Cook, Watch (assign sentries), Talk around the fire, Sleep until dawn (8x if safe), Break camp. **Forage** and **Hunt** are Q verbs on terrain and animals; snares in the crafting list.
- Controls overlay `?` shows the travel context (Enter in the Map screen: travel here; Space pause; Z sneak; O walk toggle when mounted). Hints: first long journey, first dusk, first camp, first forage.

## 7. Tests (golden)
| id | Case | Expected |
|---|---|---|
| V-1 | Table validation: 24 encounters; every `when` id in vocabulary, spawn archetypes exist, scripts exist; weights positive | load passes; mod with a bad condition fails with the file and id |
| V-2 | Same seed, same 8 km road trip at 1x and at 8x (time-based checks) | identical encounter list (ids, game times, positions within 1 m); the draw logs of streams 0x340, 0x341, 0x343, 0x344 and 0x346 match draw for draw, because every key uses game seconds or hours |
| V-3 | ETA: 8,000 m at 3 m/s jog | 2,667 real s (44.4 min) at 1x, 333 real s (5.6 min) at 8x; 14.8 game hours either way |
| V-4 | Frequency: danger 300, road, 10,000 simulated 7 h legs | mean scripted encounters per leg 1.0 +/- 0.1; ambush-type encounters under 10% of all encounters |
| V-5 | Safe start: first 2 game days, scale 300 pm | no ambush or highwayman encounter on 1,000 legs |
| V-6 | Interrupts: scripted hostile at 90 m, hero at 8x | clock drops to 1x within 1 tick, `Hostile` pause, interrupt record has position and kind |
| V-7 | Speed gate: wolf in view | 8x refused with reason string; allowed after the wolf leaves vision |
| V-8 | Camp fire: 5 kg firewood per hour; 8 h night | 40 kg for the night; with 25 kg the fire dies after 5 h (warm offset drops; chilled in the party after the expected exposure time) |
| V-9 | Sentry: threat approaches at 30 m, `notice_pm` 640 (perception 40: `400 + 240`), fatigue 0 | per-second detection 64%; probability noticed before reaching 8 m (about 6 s at 3.5 m/s) = 1 - 0.36^6 = 99.8% (moonlit); in the dark (8 m vision) detection starts at 8 m only |
| V-10 | Cooking and food: 4 persons eat 4.0 measure-eq from the camp stock | stock reduced by exactly `4 x 1,100 g` grain-equivalent; `LastMealHour` set; loyalty +20 each once per day |
| V-11 | Companion talk | `AmbitionKnown` true by night 3; loyalty +50 once per night; cap holds when repeating |
| V-12 | Forage: tile stock 1,600 g, skill 50, summer | sweep 1: `min(128, 250) = 128 g`; sweep 2 `min(117.8, 250) = 117 g` (floor); tile never negative; after 6 sweeps 627 g total (128, 117, 108, 99, 91, 84) |
| V-13 | Poisoning: Forestry 0, eat 100 g unknown mushroom | `(1000 - 300)/10 = 70` pm, i.e. 7% per 100 g |
| V-14 | Hunting: kill deer 80 kg | tile `Game` stock -80,000 g; butchery per clothing spec; no mass created |
| V-15 | Fair: Spring Fair day 8, a town of 1,500 | stalls +12 on day 8-10, ale structural demand x1.2 only those days, board jobs x3, work ends 13:00, back to normal on day 11 |
| V-16 | Conservation: 30-day journey soak with camps, forage, trades | coin and mass audits exact |

## 8. Open questions for the user
1. **Stop at dusk:** default `Dusk` is a notification; travelling at night is allowed (it is dangerous, not forbidden). Alternative: auto-stop and make camp.
2. **Encounter frequency:** 1 scripted encounter per 7 game hours (about 21 real minutes). If testers find the road empty, double it; if they find it exhausting, halve it. Default as written.
3. **Hunting realism in Phase 3:** a bow shot resolves through the existing strike resolver with simple animal AI; polish waits for Phase 4. Default accepted.
4. **Camp decay:** 3 game days. Default accepted.

## 9. Budgets
| Item | Budget |
|---|---|
| `EncounterDirector` per minute check | <= 20 microseconds |
| 8x travel (corridor bubble: <= 60 entities) | live tick <= 6 ms; abstract tick per hour unchanged (<= 150 ms) |
| Interrupt evaluation per tick | <= 0.05 ms |
| Sentry notice rolls | <= 0.05 ms (<= 3 sentries x <= 20 candidates at 1 Hz) |
| Danger recompute daily | <= 0.5 ms per region |
| Camp and journey records | <= 200 camps (decay), < 100 KB save |
| Spawns per encounter | <= 12 entities, within the 50 per frame limit |

## 10. Notes (implementation of travel, encounters, camp, forage, hunt, butchery and fairs)

**Built.** `Ravensguard.Sim.Travel` (`InterruptBus`, `TravelModels`, `Encounters` (director, conditions, danger), `Cast`, `TravelState` + `TravelAuto` (journey, ETA, speed gate, interrupt evaluation), `Camp`, `Gathering` (forage, hunt, snares), `Butchery`, `Fairs`, `RouteFinder`), `Save/TravelSave` (section `"travel"`, absent in a world nobody travelled in), content `content/base/travel/` (`encounters.jsonc` 24 rows, `settings.jsonc`, `camp.jsonc` 12 night events, `forage.jsonc`, `game.jsonc` game animals with their `butcher` blocks, `fairs.jsonc`) and the content types in `Content/Defs/TravelDefs.cs`. Game: `World/StreetView.Travel.cs` (keys, tick, camp, forage, dev commands), `World/TravelHud.cs`, `World/TravelFeed.cs` (the region and weather as an `ITravelWorld`), `UI/WorldMapScreen.Travel.cs` (destination picker). Tests: `tests/Ravensguard.Sim.Tests/Travel/TravelTests.cs` (V-1 to V-16 and C-11 to C-13 plus the rules around them).

**Decisions that differ from the text above.**
1. **Conditions are `conditions`, not `when`.** The vocabulary validator checks every `conditions` array already, so a bad id fails the content load with the file and id (V-1) with no new code. Twenty-two travel ids were added to `BaseVocabulary`; `season_is` is reused. `spawn.groups` is flattened to `groups`.
2. **Time is the only clock.** `TravelState.Advance(world, toSec)` works at 10-minute slot starts (speed, interrupts, the roll) and walks at that slot's integer speed in between, so any chunking (1 s a tick at 1x, 8 s at 8x, 160 s) gives the same positions and the same encounters (V-2 compares 1, 8 and 160). The roll is stream 0x340 keyed (hero, game hour, slot); the table choice is draw `6 + slot`. Draws are made even when the gap or a cooldown then blocks the encounter, so streams never shift. The first slot of a journey does not roll.
3. **Speeds** are integer mm per game second (`cm/s / 2`, `VehicleMath.MmPerGs`): jog 150, trot 250. Ground (`terrain_speed_pm`) and weather (`weather_speed_pm`) scale it; mud x0.7. The ETA of V-3 is `Eta.Of`.
4. **Frequency test.** The spec's "1 per 7 hours" is a daytime road (night doubles it); V-4 runs a typical farmland road (woodland, pasture, a wet week, a feast within 5 days) by day and gets 1.0 +/- 0.1 with ambushes under 10%. On a thin table (only woodland and a road) Ambush is 10.3% of the eligible weight, so the rule "ambushes under 10%" depends on how many rows a place makes eligible.
5. **Real people.** `IEncounterCast.Existing` is the hook for caravan crews and vagrants (not wired in the game yet); the fallback makes `Traveller` rows (ids in region 254), saved in the travel section. Nothing is skipped when no record fits, because the generated traveller is a record too. This differs from "otherwise the encounter is skipped" for bandits and is a small known gap until `Homeless.Vagrant` persons are promotable.
6. **Game stock unit.** The resource layer's `Game` stock is micro-animals (1,000,000 per animal), not grams, so V-14's "-80,000 g" is written as `mass x 25 / 2` micro-animals (1,000,000 per 80 kg deer-equivalent). A kill draws from the 13 x 13 tiles around, nearest first; a land with less game than the animal needs cannot be hunted (`Kill` returns null). Whole animals available are `floor(stock / 1e6)` capped at 8 per km2.
7. **Butchery by tissue, shares authored.** The `butcher` blocks in `game.jsonc` carry the share and recovery per tissue; deriving the shares from the body plan is not done because only the human body plan exists (a creature's body plan arrives with Phase 4). Horn has no good, so it is folded into bone (no new goods were added: a new good changes the goods hash). Mass is conserved to the gram: goods made (created as `CreateReason.Hunted`) plus the waste equal the carcass, and each tissue varies plus or minus 4% (stream 0x332, added to `RngStreams`) before a cap at the carcass mass.
8. **Forage items** use existing goods (`vegetables` for berries, herbs, roots and mushrooms, `peas` for nuts). V-12 holds exactly: 128, 117, 108, 99, 91, 84 = 627 g, and the same grams come off the tile and into the pack.
9. **Camp.** Fuel leaves the camp stock when it is loaded onto the fire (destroyed as `Burned`), so the fire's burn only counts down a number; the burn carries its remainder in gram-seconds, so any chunking gives 5 kg an hour exactly (V-8: 40 kg lasts 8 h, 25 kg dies at 5 h). Camp stock is a store of the camp's id on the economy (hosted, saved with the economy) or private rows in the travel section. A tent by a fire rests 125 an hour, a bedroll 100, bare ground 75; the hero's clothing model counts a lit camp fire within 4 m as "by a fire". A meal is one person-day for each eater (`ceil(1e9 / measures-ppm)` g: 1,100 g of wheat), loyalty +20 and familiarity +2 once per day. Companion talk: the ambition takes three nights (`AmbitionKnown` after the third), `NightOf` turns over at 18:00, loyalty +50 once per night.
10. **Interrupts.** `InterruptKind` has the 23 kinds in the autopilot order; Notify raises a toast and holds the clock at 4x for 5 real seconds; a Hostile sets the speed cap in the same call and `Evaluate` clears it when nothing hostile is in view. `Evaluate` is edge-triggered and cheap, so the game calls it each tick as well as at slot starts. Encounter kinds map to interrupts: Ambush, Animal, Demand to Hostile; Obstacle to BlockedPath; Pass, Omen, Find to Found (Notify); Trade, Ask, Check, Job, Block, Work, Column to Contract (Pause).
11. **Fairs.** `FairBook` answers every V-15 number. `FairCalendar.IsFairDay` (the default calendar's multi-day spans: 8-10, 23, 38-39, 53-55) replaces the one-day feast test in `DayInfo`, so NPC schedules switch to the fair blocks on all fair days. Stalls, demand tilts, the job-board multiplier, the trip bonus and hiring day are queries; the economy and the job board do not call them yet.
12. **Route.** `RouteFinder` is A* over the 500 x 500 region tiles with road, track, field, woodland and marsh costs and no crossing of water except on a road tile (a bridge or a ford; a closed ford, heavy rain at a crossing, blocks). It is not the HPA* road graph of the Phase 2 spec. `LiveWorld.TravelPlace` puts the hero on the route each tick (about a metre at 8x); it ignores collision and is not recorded by the session recorder, so a replay of a trip is not yet supported.

**Game.** Map: click a place or Tab through them (nearest first), the route, distance, ETA (game hours, real minutes at 1x and 8x) and days of food show, Enter starts the trip. During a trip: banner with progress, Esc stops, 1 to 4 change the clock (8x only if the gate allows, with the reason shown), a movement key takes the hero back, an interrupt pauses with an alert panel (Enter or E go on, Esc stop, `,` camp). `,` makes camp (refused near a settlement, on steep or wet ground), then 1 fire (gathers wood for 20 minutes, loads and lights), 2 cook and eat, 3 watch, 4 talk, 5 sleep until dawn at 8x, 6 shelter, 7 break camp. `.` forages the tile (10 game minutes). Dev commands: `camp`, `wood`, `encounter [id]`, `kill [species]`, `butcher`. Screenshots: `--shot-travel encounter|camp|picker` (`p3g-*.png`).

**Not done.** Hunting is a dev command (a bow and animal sprites wait for Phase 4 combat and creatures); snares work in the sim but have no key or sprite; the Q menu and order wheel do not exist, so "Travel here" is the Map's Enter and camp and forage have their own keys (`,` and `.`, World context); encounter outcomes (pay, fight, flee, trade) show the people and the alert but resolve to "go on" until dialogue, trade hand-off and combat call them; `IEncounterCast` is not wired to caravans and vagrants; the 4 stand-in danger inputs (vagrants per 1,000, raid, war, famine) are constants in `TravelFeed`; the camp fire is the hearth sprite; sentry notice rolls and night events run in the sim and as toasts but the alarm does not wake sleepers; the first-camp, first-forage and first-journey hints and the `?` overlay lines are missing; `en.csv` has the `TRAVEL_*` strings only in English.
