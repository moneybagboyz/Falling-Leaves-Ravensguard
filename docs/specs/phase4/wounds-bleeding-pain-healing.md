# Wounds, bleeding, pain, knock-outs, healing, infection, permanent damage, splints and amputation

Plan refs: §4A.3 (blood about 5,000 ml scaled by body mass; ml per sim-second by vessel; clotting half-life 90 s, arteries never; pressure or bandage -80% in 4 s, tourniquet -98%; shock bands 15/30/40/50%; a femoral wound downs a man in ~18 s and kills in ~30 s, an arm vein downs in ~110 s; pain pool decays 2% per second, knock-out above 60 + 4 x toughness; heart wounds collapse in 5-10 s; brain kills only when destroyed, otherwise concussion; bleeding keeps running 1:1 in sim-seconds after combat; wound records open -> dressed -> stitched -> healing -> scar; infection 8% per game day undressed; set fracture 20 game days, unset 35 with 50% malunion (-2 dexterity or strength); severed tendons stay severed unless surgery at skill 50+ within a day; nerve and spinal damage always permanent; crutches and splints; amputation only from a real blow or a surgeon's choice), §2.6 step 6 (wound records drive bleeding, pain and loss of function), §2.3 (abstract tier), §3.7 (failure), §6 (combat tests). Fixes the v1 bugs in §4A.3: blood reset every battle, bleed rates carried over, blood never regenerates, broken ribs counted as "died of wounds", pain never added up, healing a flat 0.25 HP per hour, a severed nerve healing in 8 hours.
Spike baseline (`docs/spikes/02-melee.md`): 100 ml/s artery downs in 15 s, kills in 25 s; 80 ml/s in 18.8 s and 31 s; **most knocked-out fighters bled to death untreated** (about 83% of 200) because bandaging was not modelled.

## 1. Purpose
Everything that happens to a body after the resolver returns a wound list: how much blood is lost per sim-second, when a person collapses, wakes or dies, how wounds are treated and heal, which injuries are permanent, and how the same rules run unchanged in the abstract tier. The aim is "brutal but fair through legibility": every number is visible on the paper-doll, and there is always something to do in the first 30 seconds.

## 2. Dependencies
| Needs | From |
|---|---|
| `Wound` records, `WoundFlags`, grades | `resolver-v2.md` |
| Body parts, structure kinds, `BodyState` capacities | `anatomy-and-body-plans.md` |
| Goods `bandage`, `poultice`, `splint`, `rags`, `herbs`; skill `Surgery`; proficiency `first_aid`, `herbalism` | exists (`content/base/crafting`, `People.Skill`) |
| Attributes (strength, toughness, dexterity, perception, wits), permanent loss | exists (`PersonTable`) |
| Jobs, utility AI (`ActionKind`), reservations | exists (Phase 3 `Sim/Jobs`, `Sim/Behaviour`) |
| Clothing wetness and cold (for hypothermia interplay), stains | exists (`Sim/Clothing`) |
New code: `Sim/Combat/Wounds/` (`WoundBook`, `WoundRecord`, `BloodModel`, `PainModel`, `Consciousness`, `Treatment`, `HealingModel`, `InfectionModel`, `Surgery`), content `content/base/medicine/*.jsonc`.

## 3. Data

### 3.1 Records
`WoundRecord` (saved, 24 bytes): `Id` (u32 per person), `Part` (u8 index into the plan), `Structure` (u8, 0 = none), `Tissue` (u8), `Kind` (Cut, Crush, Burn), `Grade` (u8: muscle grade, bone grade or joint grade), `DepthPm` (u16), `BleedX10` (u16, ml/s x 10), `Flags` (u16), `State` (Open, Dressed, Stitched, Healing, Scar, Infected), `Dressing` (None, Pressure, Bandage, Tourniquet, Splint), `Born` (u32 game second), `HealDay` (u16 progress in game days x 10), `InfectionLevel` (u8 0..3), `Cause` (`EntityId` of the fight or event, for the "why" chain).
`WoundBook` (per person, <= 32 records, sparse): `Records`, `BloodMl` (int, ml x 10), `BloodMax`, `Pain` (int x 100), `Conscious` (`Awake, Dazed, Unconscious, Dying, Dead`), `UnconsciousUntilTick`, `CollapseAtTick` (heart), `ConcussedUntilDay`, `DeathCause` (enum + `EntityId`), `Permanent` (flags: paralysed limbs, numb parts, lost parts, malunion counts; 8 bytes).
Abstract tier rows: same `WoundBook` when wounded (typically 0 to 3 records); healthy people have none.

### 3.2 Bleeding rates (ml per sim-second, from §4A.3, v1 constants kept)
| Source | Rate | Clots? |
|---|---|---|
| Capillary (skin, fat, cartilage cut) | 0.5 | yes, half-life 90 s |
| Muscle cut | 3 | yes |
| Vein | 15 | no |
| Artery | 80-120 (rolled once at injury, stream 0x415; `bleed_scale_pm` applies) | no |
| Severed part | 90 | no (until dressed) |
| Liver, spleen, kidney | 20 (internal) | no |
| Lung | 25 (internal) | no |
| Heart | 120 (internal) | no |
| Stomach, intestines | 8 and 6 (internal) | no |
| Brain | 10 | no |
| Nose (broken) | 0.5 | yes |
| Compound fracture (bone through skin) | +3 | yes |
Internal bleeding cannot be dressed and does not clot: only surgery (skill 60+, section 4.6) stops it, so a heart, lung, liver or kidney wound is lethal within 25 to 200 sim-seconds without a surgeon. `Dressing` effects: Pressure or Bandage x0.20 (-80%), Tourniquet x0.02 (-98%, limb only), applied to the **external** rate of that wound only.
Total bleed per person `R = sum(rate x dressing x clot(t))`. Blood per sim-second `-= R`. Per 20 Hz tick `R/20`, integer in ml x 10.

### 3.3 Thresholds
Blood `BloodMax = plan.blood_ml x size/70000` (human default 5,000 ml). Loss fraction `L = 1 - Blood/BloodMax`.
| L | State | Effect |
|---|---|---|
| < 15% | none | |
| 15-30% | weakened | strength -20%, wind-ups x1.2 (spike), no sprint above 60% stamina |
| 30-40% | collapse | falls, cannot fight, can crawl 100 pm and talk; wakes if bleeding is stopped |
| 40-50% | unconscious | no action; death if bleeding continues |
| > 50% | dead | `DeathCause = BloodLoss` |
Golden timings at 5,000 ml (collapse 1,500 ml, unconscious 2,000, dead 2,500): **artery 100 ml/s: 15.0 / 20.0 / 25.0 s; artery 80: 18.75 / 25.0 / 31.25 s; artery 120: 12.5 / 16.7 / 20.8 s; arm vein 15 ml/s: 100 / 133 / 167 s; severed arm 90 ml/s: 16.7 / 22.2 / 27.8 s; muscle cut 3 ml/s (clotting, half-life 90 s): total loss to infinity 3 x 90/ln 2 = 390 ml = 7.8%: never collapses alone**.
Blood regeneration (v1 bug: none): **25 ml per game hour** (x1000 `regen_pm`: fed 1000, hungry 400, starving 0), applied by the hourly tick or per 180 sim-seconds in the live tier. Recovering from 40% to 30% takes 40 game hours.
Pain `P` units: decays x0.98 per sim-second (`PainDecayPerS 0.02`). KO threshold `T = 60 + 4 x toughness` (toughness default 10 = 100). Per-wound pain on creation (x `part.pain_pm`/1000 x tissue `pain_pm`/1000):
| Wound | Pain |
|---|---|
| Skin or fat cut | 3 |
| Muscle bruised / torn / mangled | 4 / 10 / 20 |
| Bone cracked / broken / displaced / shattered | 15 / 30 / 40 / 55 |
| Severed part | 45 |
| Organ hit | 35 (heart 60, brain 50) |
| Tendon cut, nerve cut | 15, 20 |
| Ligament sprained / torn / ruptured | 10 / 20 / 35 |
| Eye lost | 40; nose broken 12; jaw broken 30; teeth lost 4 each fifth |
Ongoing pain while a wound is open: +1% of its creation pain per sim-second (halved by a dressing, x0.3 by a splint on a fracture). A **numb** part (sensory nerve cut) adds 0 ongoing pain and its initial pain is cut to 30%. Typical knock-out: two serious wounds (about 60 + 45 = 105 > 100). Pain relief: ale -8 per game hour, `poultice` -15, poppy milk -40 (rare trade good, content `medicine/relief.jsonc`).
Knocked out by pain wakes when `P < 0.7 T`: from 130 -> 70 takes 31 sim-s.
Concussion: skull `Cracked` or worse, or a blunt head blow with M_in at the skull >= 40: KO for `10 + 0.4 x (M_in - 40)` sim-seconds (M_in 100 -> 34 s; cap 600), then `Dazed` for `1 + grade` game days (accuracy and spotting -25%, reading and speech check -20, no sprint). The brain is destroyed (death) only by the organ rule in resolver-v2 4.7.
Heart wound: `CollapseAtTick = now + 120 ticks` (6 s), then `Down: Heart`; bleeding 120 ml/s kills at 20.8 s. A lung wound: `Breath` -500; both lungs or a displaced rib into a lung: `Breath` 0 -> unconscious in 30 s.

### 3.4 Healing content (`medicine/healing.jsonc`, game days; speed x1.5 slower when starving, x0.67 with bed rest)
| Injury | Treated | Untreated | Notes |
|---|---|---|---|
| Skin cut, shallow (depth < 0.5) | 6 | 10 | stitched or dressed; scar |
| Skin cut, gaping (depth >= 0.5) | 8 stitched | 14, scar worse, infection x2 | stitching needs `Surgery` >= 10 |
| Muscle bruised / torn / mangled | 3 / 15 / 40 (mangled heals only to torn) | x1.5 | grade drops one step at 50% progress |
| Bone cracked | 10 set or splinted | 15 | |
| Bone broken | **20 set** | **35, 50% malunion** | plan |
| Bone displaced | 28 set (surgery 30+) | 45, 80% malunion | |
| Bone shattered / compound | 40 (surgery 60+) | 60, 100% malunion, infection x3 | |
| Ligament sprained / torn | 12 / 30 splinted | x1.5 | |
| Ligament ruptured | 30 only after surgery (skill 60, within 2 game days) | **permanent instability** | |
| Tendon severed | 40 after surgery (skill 50+, within 1 game day) | **permanent** | plan |
| Nerve (motor or sensory), spinal cord | none | **permanent, always** | plan; no surgery restores |
| Organ (lung, liver, kidney, spleen, stomach) | 20-40 with surgery (60+) | usually fatal | |
| Concussion | 1 + grade | same | |

## 4. Rules

### 4.1 After the blow
`Apply(StrikeResult)` in the same tick the blow lands: records are added to the defender's `WoundBook`; pain added; `BodyState` recomputed; if a `CollapseAtTick`, `Down` or `Dying` state changes, a `ConsciousnessChanged` event goes to the melee layer (drops weapon, falls) and the flavour log (feedback file). Bleeding and pain are updated **every tick for persons in the `BleedingSet`** (a sparse list: anyone with R > 0 or P > 0).

### 4.2 Bleeding after combat: 1:1 in sim-seconds
Blood loss is per **sim-second**, never per game second: at 1x a sim-second is 20 game seconds, so a person with an open artery after the fight is still bleeding at 100 ml/s in real time, and the 8x speed key does not slow it (it runs 8 sim-seconds per real second). In the abstract tier one game hour = 180 sim-seconds; the hourly tick applies `BloodStep(person, 180)` in closed form (with the clot integral `rate0 x 90/ln2 x (1 - 0.5^(T/90))`, a 256-entry Q16 table of `0.5^(k/90)`), so a bleeder with no help dies in the same sim time. This is the Phase 3 `AbstractExposure`-style rule: one function, called with different step sizes.

### 4.3 First aid (the 30-second game)
Actions (job kinds `Treat`, appended to `ActionKind`; the hero uses the Q menu on a body part or the radial; plan §2.8 has no key for it):
| Action | Time (sim s) | Needs | Effect |
|---|---|---|---|
| **Pressure** | 2 (4 for the effect) | hand free | -80% on one external wound; stops when the helper leaves |
| **Bandage** | 4 | `bandage` item (goods exist, 4 per crafting batch) | -80% persistent; stale after 2 game days (infection back to normal) |
| **Tourniquet** | 6 | `rope` or `bandage` x2, limb part only | -98% on all external wounds distal; after 2 game hours (360 sim-s of wearing) the limb takes ischaemia: each further 2 game hours the limb's muscle drops a grade; after 12 game hours the limb is lost (`Mangled` then removal by surgeon) |
| **Set bone** | 20 | `Surgery` check, optional `splint` | success `P = clamp(30 + skill x 0.6 - 10 x grade, 5, 95)` (grade 1 cracked..4 shattered); fail leaves it unset |
| **Splint** | 12 | `splint` item (exists) | holds the bone: cracked: walk 700; broken and set: walk 500; broken unset: 300; arm: usable at 50% |
| **Stitch** | 30 | needle, thread, `Surgery` >= 10 | closes a gaping cut, halves scar, infection risk 8 -> 1%/day |
| **Clean** | 20 | water, `herbs` or `poultice` | resets infection counter; chance to clear an infection (4.7) |
| **Cauterise** | 10 | fire, iron | stops a vein, severance or capillary bleed (not artery: 40% chance, pain 40) |
- **Self-treatment:** possible for any limb wound with a free hand; torso and head wounds need a helper. Pressure cannot be held while fighting.
- **AI use (utility):** `Treat` scores by the number of bleeding allies in 6 m, with a bonus for the `first_aid` proficiency; soldiers stop bleeding comrades after the fight ends (a medic role later). This is what moves the bleed-out rate from 83% to the 35% target (resolver-v2 8).

### 4.4 Knock-outs and waking
A person goes `Unconscious` when pain > T, blood loss >= 40%, a heart collapse happens or concussed. They wake when none of those holds for 10 sim-s: pain < 0.7 T, L < 38%, concussion timer done. Unconscious people bleed on (no one holds pressure). Knocked-out fighters stay in the fight record until the outcome (morale-capture-succession.md) is decided. A waking person who fought on was at risk: a stand-up costs stamina 30.

### 4.5 Fractures, grades and malunion
- The fracture state is `BoneGrade` on the part (resolver-v2 4.6). Treatment before the heal clock matters: **set within 1 game day** (later: the callus has begun, set success -30).
- Healing completes at `HealDay >= days`. At completion, if never set (or set failed): malunion roll `Range(0,100) < p` (stream 0x421; 50% for `Broken` per plan, 80% displaced, 100% shattered; cracked 0). A malunion lowers an attribute **permanently by 2**: arm, shoulder, ribs, collarbone -> strength; hand, fingers, leg, foot, kneecap, pelvis -> dexterity; jaw -> speech skill cap -10 (no attribute); skull cannot malunite (concussion instead).
- Splint and cast items wear: a splint outlasts 40 game days.
- Weight-bearing: a leg with a bone grade `Broken` or worse not splinted bears 0; cracked bears 400 (limp). With a splint, see 4.3 table; `Walk` then follows anatomy 3.6.

### 4.6 Surgery
Surgery skill gates (`Skill.Surgery`, 0-100): stitch 10, set bone 0 (checked), tendon repair **50** within 1 game day (plan), ligament repair 60 within 2 game days, organ repair 60, amputation 30, spinal cord: none can restore. Tendon repair success `P = clamp(40 + (skill - 50) x 1.0, 40, 95)`; one retry allowed per wound with -20; failure leaves the tendon severed (permanent, plan). Surgery takes 90 sim-s (tendon), 180 (organ), needs light (a torch or daylight), a clean cloth and a tool with the `sewing` quality (existing tool qualities, plan §2.10); the patient screams, pain +30 unless dulled. A physician NPC (town) charges 3 crowns for stitching, 12 for a tendon repair (stored in `medicine/prices.jsonc`, seeded for the price solver); only towns and larger villages have one (the plan's "far from any surgeon" is a Phase 4 exploration risk).
Amputation by choice: `Surgery >= 30`, 60 sim-s, tourniquet on, produces a `Severed` stump wound with bleed 0.5 (cauterised) instead of 90; used to remove a mangled or gangrenous limb (the infection chain cleared).

### 4.7 Infection
Per **game day** (checked at the game-day boundary, stream 0x420 key = game day, person, wound id):
- Open (undressed) wound: **8%**; dressed bandage 2%; stitched 1%; compound fracture x3; wound received underground or in mud/water x1.5; rusty weapon (item condition) x2 (flag from resolver input); a clean cut stitched within 6 game hours x0.5.
- `Infected` levels: 0 -> 1 (redness: pain +20% ongoing) -> 2 (fever: stamina regen x0.7, strength -20%) -> 3 (**sepsis**: Walk x0.5, delirium: autopilot interrupt `Injured` each day). Each game day the level rises with 35% chance (stream 0x420 draw 2) if untreated.
- **Treatment** (`Clean`, `poultice`, honey, vinegar): each game day clears one level with `P = clamp(30 + skill x 0.4, 20, 85)%` per `Clean` action. A `poultice` item gives +20 points.
- Death: at level 3 for 4 game days (jittered 3-5, stream 0x420 draw 3), `DeathCause = Infection` (a chronicle event with its cause chain to the wound, "why" link).
- Amputating the infected part clears it. The abstract tier uses the same function at the day boundary.

### 4.8 Permanent damage and mobility aids
- **Paralysis, numbness, missing parts** (permanent, anatomy 3.6) live in `Permanent` and the character screen ("Paralysed below the left elbow: motor nerve cut at the siege of Oakford"). A paralysed limb is flaccid: cannot hold a weapon or shield, drags if a leg. Paralysed legs: crawl 100 pm; a **wheeled chair** is a Phase 5 craft; Phase 4 gives **crutches** (wood, 2 per pair, goods in `medicine/aids.jsonc`, crafting recipe `make_crutch` via the existing `bind` and woodcraft steps) and **litters** (carried by 2 followers).
- Mobility aids table: both legs usable 1000; one leg usable: hop 250, crutch 550, splint (cracked bone) 700; both legs lost: crawl 100, with two crutches and a seated posture 0 (cannot walk); pelvis broken: carried; paralysed legs: litter only.
- Attribute loss: fractures malunion -2 (4.5); a destroyed eye does not reduce an attribute but `Sight` (anatomy). Brain concussion repeated 3 times in 30 game days gives -1 wits permanently.

### 4.9 Spinal injuries
`SpineHit` wounds: motor effect per anatomy 3.6. Treatment cannot restore function; a spinal injury also adds shock pain 40 and the patient is carried (litter). A neck complete cut with `Breath` 0: dead in 240 s unless... no treatment exists. Spinal injury survival is realistic: the player must live with it (permanent, plan "Dwarf Fortress-harsh").

### 4.10 The wound lifecycle and the log
`Open` (bleeding, infection 8%) -> `Dressed` (bandage) -> `Stitched` (gaping cuts) -> `Healing` (HealDay progressing) -> `Scar` (permanent mark, a visible overlay and a tooltip). Transitions are events for the flavour log and the chronicle (only for named people and serious injuries: severed, permanent damage, death).

## 5. Tick placement (20 Hz live versus abstract)
- **Live 20 Hz:** `BloodModel.Step`, `PainModel.Step`, `Consciousness.Step` for persons in the `BleedingSet`; treatment actions advance as job steps. Clotting: multiply a wound's current rate by `992,327 ppm` once per sim-second (every 20th tick, staggered by wound id mod 20).
- **Hourly abstract tick:** blood closed-form step with 180 sim-s, healing days at the day boundary, infection rolls, wake/dying resolution, first aid by present helpers (`AbstractFirstAid`: a bleeding person in a settlement is dressed with `P = 90%` if any household member is home, 40% if only neighbours, 0 on the road alone; soldiers with a medic share 80%).
- LODBridge: promotion rebuilds the live `BleedingSet` from rows; demotion writes `BloodMl`, `Pain` and wounds back unchanged (conservation test WB-14: blood, pain and wound counts equal across promote and demote).

## 6. Determinism
Streams (0x400 block; key: game day for daily rolls, `FightTick` for combat rolls, entity = person, draw = wound id):
| Stream | Use |
|---|---|
| 0x415 | artery rate 80-120 (resolver; stored in the wound) |
| 0x420 `Infection` | daily infection, level rises, death day jitter (draws 1-3) |
| 0x421 `Malunion` | malunion at completion (key: wound id) |
| 0x422 `SurgeryResult` | set, tendon, organ outcomes (key: game second / 20, surgeon, wound) |
| 0x423 `ConcussionJitter` | KO duration +-10% |
| 0x424 `WakeJitter` | wake delay 0-4 sim-s |
All arithmetic integer (`BleedX10`, ml x 10, pain x 100, permille). `WoundHash` (all `WoundBook`s) joins the daily hash trail. Healing is a pure function of the record and the game day.

## 7. UI and controls
- **BodyDiagram** shows wound overlays (blood, bandage, splint, missing limb; plan §2.7) and a ranked "worst problem first" list in the character screen (C) with a countdown for bleeding ("Left thigh: artery, 31 s to collapse; press Pressure").
- **Combat HUD:** a small body silhouette (top-left) with bleeding parts pulsing red; a **blood bar** (thresholds ticked at 15, 30, 40, 50%); a **pain bar** against T. A "time to collapse" estimate appears for a bleeding hero.
- Treatment: Q menu on a body part shows verbs available with their needs (Pressure, Bandage, Tourniquet, Splint, Stitch, Set bone, Clean, Cauterise); the order wheel `R` has "Treat nearest" for squad medics. No new World keys (keys are in the overview table).
- Warnings (plan §3.13): "A tourniquet saves this arm for 2 hours" tooltips; the after-fight report lists untreated bleeders with a call to action (feedback file).

## 8. Tests (golden)
| Id | Check |
|---|---|
| WB-1 | Artery 100 ml/s: collapse 15.0 s, unconscious 20.0 s, dead 25.0 s (+-0.1); artery 80: 18.75 / 25.0 / 31.25; arm vein 15: 100 / 133 / 167 |
| WB-2 | Clotting: a 3 ml/s muscle cut loses 390 ml +- 10 over 600 s and never collapses; its rate at 90 s is 1.5 |
| WB-3 | Pressure: -80% within 4 s; bandage persistent; tourniquet -98%; stale bandage after 2 game days |
| WB-4 | Pain: a severed arm (45) + a displaced bone (40) = 85 stays under T = 100 (toughness 10); adding a mangled muscle (20) = 105 knocks out in the same tick; wake when the pool falls below 70 (31 sim-s) |
| WB-5 | Heart wound: `Down: Heart` at 6.0 s, dead at 20.8 s |
| WB-6 | Concussion: M_in 100 -> KO 34 s then `Dazed` 1 + grade days |
| WB-7 | Fracture: set broken heals in 20 days; unset 35 days with 50% malunion (10,000 seeds, 50 +- 1.5%); malunion -2 strength for an arm |
| WB-8 | Tendon: repair at skill 50 within 1 game day succeeds 40% (10,000 seeds +-1.5); after 1 game day 0% (permanent) |
| WB-9 | Nerve and spinal cord: no healing at any game day; abstract and live agree |
| WB-10 | Infection: untreated open wound 8% per game day (10,000 seeds +-0.8); bandaged 2%; sepsis death at day 3-5 of level 3 |
| WB-11 | Bleeding continues 1:1: a live bleeder demoted for 180 sim-s (one game hour) has lost exactly the live amount; abstract and live blood equal to 1 ml |
| WB-12 | Blood regeneration 25 ml per game hour; 40% to 30% in 40 game hours |
| WB-13 | Amputation by choice: stump bleed 0.5 vs severed 90; needs skill 30 |
| WB-14 | Conservation across promote and demote: wounds, blood, pain identical hash |
| WB-15 | Treatment effectiveness: with an AI medic and bandages, bleed-out of knocked-out fighters falls from the no-treatment 83% to <= 35% in the calibration duel set (resolver-v2 8) |
| WB-16 | Bleed after 8x: a 100 ml/s bleeder at 8x dies in 3.1 real seconds (25 sim-s / 8) |

## 9. Open questions (recommended defaults)
| # | Question | Default |
|---|---|---|
| W1 | Blood regeneration speed (v1 had none; plan silent) | 25 ml per game hour, hungry x0.4, starving 0; revisit with tester deaths |
| W2 | Should a tourniquet cost the limb after hours? | Yes at 12 game hours (recoverable warnings at 2 and 6), to keep it a real trade-off |
| W3 | Can the player see "time to collapse" numerically? | Yes (legibility rule, plan §3.13), as a rounded estimate |
| W4 | Painkillers: ale, poultice, poppy | Ale and poultice in Phase 4; poppy milk later with trade goods |
| W5 | Internal bleeding without surgery | Lethal in 100-200 s; no first-aid fix, so lung, liver and heart wounds are meaningfully deadly. Revisit if testers find chest wounds all-fatal (tune by `bleed_ml_per_s`) |
| W6 | Permadeath of limbs vs surgery for paralysis | Permanent always (plan). A later "great physician" legend is out of scope |
| W7 | Mounted wounds / horse wounds use the same book? | Yes, `WoundBook` for animals with the horse plan (no pain pool: panic instead, mounted-combat.md) |

## 10. Budgets
| Item | Budget |
|---|---|
| `BleedingSet` step | <= 0.1 us per person per tick; 300 bleeders <= 0.03 ms per tick |
| `WoundBook.Apply` | <= 2 us per blow |
| Hourly abstract wound tick (25,000 people, ~500 wounded) | <= 0.5 ms |
| Day boundary (infection and healing) | <= 0.3 ms per region |
| Save | <= 24 bytes per wound + 24 bytes per wounded person; a post-battle region with 2,000 wounded <= 120 KB |
| Content | `medicine/*.jsonc` <= 20 KB |

## Notes (P4-B implementation, step 2)
Built in `src/Ravensguard.Sim/Combat/Wounds/` (`WoundTypes`, `MedicineTables`, `WoundBook` + `.Healing`, `Treatment`, `TreatAi`, `WoundLedger`, `WoundSystem`), saved by `Save/WoundSave.cs`. Gates WB-1 to WB-16 are in `tests/.../Combat/Wounds/WoundTests.cs` (24 tests, green); streams 0x420 to 0x424 registered.
- **One function for both tiers.** `WoundBook.AdvanceTo(gameSec)` is the only time step: exact integer blood loss per sim-second (rate x clot x dressing, clot from a Q32 table of 0.5^(age/90)), spread over the 20 ticks of the sim-second by a floor difference. The live tier calls it from `LiveWorld.Step` with the world clock (20 Hz, any speed); the abstract `WoundSystem` (H4, order 4250) calls it once an hour. A book stepped tick by tick, in 8-tick chunks or in one hourly call has the same hash (WB-11, WB-16). Promote and demote copy nothing: the `WoundLedger` (on `WorldState.Wounds`, also in the home `SimWorld` services) is shared; the `BleedingSet` is rebuilt from it (WB-14).
- **Units.** Time is game seconds (one live tick at 1x); blood is ml x 1000; wound rate ml/s x 10; pain x 100. The book holds no plan, so it needs none to advance. Thresholds are checked every tick for blood, once per sim-second for pain.
- **Dressing** (decisions): a pressure hold bites 4 sim-s after the action; a dressed external wound also clots (half-life 45 s), otherwise a bandaged artery (20 ml/s) would still kill in two minutes. Bandages dress every external wound on the part; tourniquets every external wound on the limb chain below; tourniquet 12 game hours loses the limb (the 2 h and 6 h grade drops are not modelled). Internal bleeding cannot be dressed; organ surgery (skill 60) stops it.
- **Healing** runs at the game-day boundary (`DailyStep`), pure in (book, day): infection 8/2/1% (open, bandaged within 2 days, stitched; compound x3, dirty x1.5, early stitch x0.5), a level rises 35%/day, sepsis kills 3 to 5 days after level 3; an infected wound does not close until cleaned. Fracture set 20 days, unset 35; malunion at completion (stream 0x421): -2 strength (arm, ribs, collarbone) or dexterity (leg, hand, pelvis, kneecap), jaw -10 speech cap. Nerve, cord, severed and unrepaired tendon records never heal (they stay in the function list, so `BodyState` keeps the paralysis and numbness); tendon surgery P = clamp(40 + skill - 50, 40, 95) within 1 game day, one retry -20.
- **Spec inconsistencies kept as stated:** WB-12 says 40% to 30% takes 40 game hours; 500 ml at 25 ml/h is 20 hours, so the test asserts 20. A splinted broken leg walks 400 (BodyState from step A), not the 500/300 of 4.3.
- **Deliberately not done:** the `medicine/*.jsonc` content files (constants are in `MedicineTables`), the `make_crutch` recipe and crutch good (the `BodyAids.Crutch` capacity exists: hop 250 becomes 550), scoring `ActionKind.Treat` in the utility profile (the enum member exists; `TreatAi.Score/Next` is what the medic and the harness use), prices of a physician, the paper-doll UI. Abstract first aid is `AbstractFirstAid` (90% household home, 40% neighbours, 0 alone, up to four wounds bandaged an hour); it has no test beyond the ledger ones.
- **Person record:** `DeathCause` gained `BloodLoss, Organ, Brain, Infection, Suffocation, Ischaemia` (appended); `DeathsByCause` grows with it, the sim hash includes the new slots only once non-zero, and an old save loads. Save format 4 -> 5 (optional `wounds` section, no-op migration).
- **Harness (WB-15):** `Calibration.Run(..., treat: true)` adds a medic to the duel aftermath (winner arrives in 3 s; tourniquet 6 s for a limb wound at >= 10 ml/s, else bandage 4 s, else pressure 2 s; kit one rope, four bandages), applying the same constants to the spike world's wounds. Knocked-out fighters who bled out within 60 s, all 16 pairings, 400 duels each: **46.3% untreated, 25.3% with a medic** (target <= 35%). Single pairings with long fights still exceed it (sword vs sword padded 37%): the losers there carry internal and many-wound injuries one medic cannot dress in time.
