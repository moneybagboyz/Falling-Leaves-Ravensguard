# Anatomy and body plans: the full human, ligaments, nerves, spinal cord, graded muscle, horse, animals and two monsters

Plan refs: §2.6 (body parts have a size and an area; tendons, nerves and vessels are structures inside muscle, hit by area; organs sit behind their parent part), §2.10 (tissues are materials of the species), §4A.3 (the decided human detail: eyes, skull and ribs, jaw and teeth, thumb and fingers grouped, ears, nose, collarbone, pelvis, kneecaps; ligaments at six joints; separate motor and sensory nerves per limb section; a spinal cord in the neck, upper body and lower body; graded muscle damage), §3b (rare monsters on the same resolver, a draugr does not bleed or feel pain), §3e (horses with their own plan), §5 Phase 4 ("the finer human body"). Code today: `BodyPlanDef`, `BodyPartDef`, `StructureDef`, `OrganDef`, `NamedGapDef` (`src/Ravensguard.Content/Defs/BodyPlanDef.cs`), `BodyPlan`, `BodyPart` (`Sim/Combat/BodyPlan.cs`), `content/base/bodies/human.jsonc` (10 base parts, 16 sided and unsided instances, 6 named gaps, nerves as one `Nerve` per limb section).

## 1. Purpose
Give the resolver (resolver-v2.md) something worth hitting. Every added part exists because it changes play (plan §4A.3): an eye changes aim, a collarbone changes what blows you can throw, a kneecap decides whether a leg bears weight. This file owns the **data shapes** and the **derived capacities** (what a body can still do). The rules for how a blow reaches each part are in resolver-v2.md; how wounds evolve is in wounds-bleeding-pain-healing.md.

## 2. Dependencies
| Needs | From |
|---|---|
| `ContentDatabase`, `BodyPlan.Build`, tissue materials (`content/base/materials/tissues.jsonc`) | exists (Phase 0) |
| Worn layers, `ArmourShapeDef.Coverage` keyed by base part id, named gaps | exists (`Sim/Clothing`, `content/base/items/armour.jsonc`) |
| `Animal`, species rows (`content/base/animals/species.jsonc`, `mass_kg`, gaits) | exists (Phase 3 horses; Phase 3 used a stub body, `HealthPm`) |
| `Site.LairKind` (`draugr`, `cave_beast`, `bog_thing`) | exists (`WorldGen/Region/RegionModels.cs`) |
New code: `Sim/Combat/BodyState.cs` (derived capacities), `Sim/Combat/BodyPlanV2.cs` (host parts, structure kinds), schema `body_plan.schema.json` v2, content `bodies/human.jsonc` (rewritten), `bodies/horse.jsonc`, `bodies/draugr.jsonc`, `bodies/cave_beast.jsonc`, `materials/tissues_v2.jsonc` (new tissues).

## 3. Data

### 3.1 Schema changes (content shapes)
`BodyPartDef` gains (all optional, units in the comment):
```jsonc
{ "id": "eye", "name": "Eye", "sided": true,
  "relative_size": 0.0006,          // fraction of body volume, same scale as DF (head 0.054)
  "depth_mm": 25,                   // a penetration at least this deep can sever or destroy
  "host": "head",                   // NEW: lives inside this part; reached through it or by a called shot
  "exposure_gap": "face",           // NEW: only reachable when the blow did not stop at an armour layer other than via this named gap
  "area_fraction": 0.03,            // NEW: chance fraction of the host's cross-section (for unaimed blows that breach the host's soft layers)
  "aim_factor_pm": 120,             // NEW: called-shot multiplier on the host's hit chance (permille)
  "severable": false,
  "layers": [ { "tissue": "organ", "thickness_mm": 12 } ],
  "function": { "sight_pm": 500 },  // NEW: what this part contributes; sums to 1000 per channel across the body
  "pain_pm": 1500 }                 // NEW: pain multiplier for wounds here (1000 = plain)
```
`StructureKind` gains `MotorNerve`, `SensoryNerve`, `Ligament`, `SpinalCord` (append-only enum; old `Nerve` is kept for load and migrated, below). `StructureDef` gains `"joint": "knee"` (ligaments), `"access": "front|any|rear"` (spinal cord), `"gap_boost": { "inner_elbow": 3.0 }` (area multiplier when the blow bypassed the armour at that named gap), and `"distal": ["lower_arm","hand"]` (parts that lose function when this nerve is cut; derived from `parent` chain by default).
`BodyPlanDef` gains `"blood_ml_per_kg"`, `"bleeds": true`, `"feels_pain": true`, `"animating_core": null` (monsters), `"bleed_scale_pm"` (vessel rates relative to the human numbers, 1000 = human), `"tissue_set": "human"`.
Migration table (`"migrate"`): `neck_nerve -> neck_motor + neck_sensory`, `arm_nerve -> arm_motor + arm_sensory`, same for forearm, hand, sciatic (upper leg), leg, foot. Area fraction splits 50/50. A v1 wound row naming `neck_nerve` loads as a cut of both.

### 3.2 The human plan (v2)
Existing 10 base ids stay unchanged (sizes DF: upper body 0.18, lower body 0.18, head 0.054, neck 0.018, upper arm 0.036, lower arm 0.036, hand 0.014, upper leg 0.09, lower leg 0.072, foot 0.022). **Added: 12 base ids, 19 instances** (the plan's "about 25" counts sided parts and the structures that matter; see open question A1):

| Base id | Count | Host | Relative size | Aim factor (pm) | Function and what breaking it does |
|---|---|---|---|---|---|
| `eye` | 2 | head | 0.0006 | 120 | `sight_pm` 500 each. One lost: ranged σ x1.4, spotting radius x0.8, depth misjudged (melee hit chance -8 points). Both: blind (see 3.6) |
| `ear` | 2 | head | 0.0012 | 200 | `hearing_pm` 500 each. Lost ear: hearing -50% (spot and stealth checks). Cosmetic scar |
| `nose` | 1 | head | 0.0014 | 300 | Broken: bleeds 0.5 ml/s (capillary rate, clots normally) into the airway, stamina regeneration x0.8 until set or healed (10 game days) |
| `skull` | 1 | head | 0.0200 | n/a (it is the head's bone layer) | Bone shell over the brain. Caved in (grade >= `broken`): `concussion` guaranteed, brain wound roll (resolver-v2 4.6) |
| `jaw` | 1 | head | 0.0050 | 400 | Broken: no solid food, speech clearly impaired (Speech check x0.5, dialogue slur flag). Chin is part of the jaw |
| `teeth` | 1 | head | 0.0010 | 250 | Grouped, 0..4 "lost fifths". Cosmetic plus Speech -4 per fifth. Loss comes from blunt blows to the face |
| `ribs` | 2 (l, r) | upper_body | 0.0300 each | n/a (the upper body's bone layer) | Bone shell guarding heart and lungs. Broken ribs: breathing pain (stamina regeneration x0.7), a displaced rib can puncture a lung (resolver-v2 4.6) |
| `collarbone` | 2 | upper_body | 0.0020 | 300 | Broken: that arm cannot be raised: no overhead (`hack`, `heavy`) blows, shield held low (cover -0.15, head uncovered) |
| `pelvis` | 1 | lower_body | 0.0300 | n/a (the lower body's bone layer) | Bone shell for bladder and intestines. Broken: cannot walk (crawl at 10% speed); carrying or a litter needed |
| `kneecap` | 2 | upper_leg/lower_leg joint, hosted by `lower_leg` | 0.0010 | 250 | Broken: that leg cannot bear weight (hop at 25% speed, 55% with a crutch) |
| `thumb` | 2 | hand | 0.0010 | 250 | `grip_pm` 300 of the hand. Lost: one-handed grip weak (weapon drop chance, no bow draw), two-handed grip -20% |
| `fingers` | 2 | hand | 0.0040 | 300 | `grip_pm` 700 of the hand, graded 0..4 fingers lost (each -17% of the 700). Four lost: cannot hold a weapon |

Not modelled (plan §4A.3): each finger, toe and tooth individually. `skull`, `ribs`, `pelvis` and the chin are **bone layers promoted to parts**: they keep the `bone_tissue` layer in the host's stack (so the resolver test is unchanged) and add a fracture state record and a function (above). Instance counts: eyes 2, ears 2, nose 1, skull 1, jaw 1, teeth 1, ribs 2, collarbones 2, pelvis 1, kneecaps 2, thumbs 2, fingers 2 = **19** new instances on top of the 16 existing ones (4 trunk parts: upper body, lower body, neck, head; plus 6 sided limb parts x 2). Total instances in v2: **35**.

### 3.3 Structures per part (v2)
Structures sit "inside muscle": rolled only when the blow cuts the muscle layer (v1 rule, kept), plus ligaments and the spinal cord, which have their own access rules.

| Part (each side) | Vessels (v1, kept) | Tendon (kept) | Motor nerve | Sensory nerve | Ligament (joint) | Spinal cord |
|---|---|---|---|---|---|---|
| neck | carotid 0.06, jugular 0.06 | none | `neck_motor` 0.015 (voice, head turn) | `neck_sensory` 0.015 | none | `cord_neck` 0.05, access any (front x0.6) |
| upper_body | none (aorta is the heart's wound) | none | none | none | none | `cord_back_u` 0.02, access rear (front x0.3, flank x0.8) |
| lower_body | iliac artery 0.03, iliac vein 0.03 | none | none | none | hip ligament lives in upper_leg | `cord_back_l` 0.02, access rear |
| upper_arm | brachial artery 0.05, vein 0.06 | biceps 0.04 | `arm_motor` 0.02 | `arm_sensory` 0.02 | `shoulder_lig` 0.04 (gap boost `armpit` x3) | none |
| lower_arm | radial artery 0.05, vein 0.05 | forearm 0.08 | `forearm_motor` 0.02 | `forearm_sensory` 0.02 | `elbow_lig` 0.04 (`inner_elbow` x3) | none |
| hand | palmar artery 0.03 | finger tendon 0.10 | `hand_motor` 0.025 | `hand_sensory` 0.025 | `wrist_lig` 0.04 | none |
| upper_leg | femoral artery 0.07, vein 0.08 | quadriceps 0.04 | `sciatic_motor` 0.02 | `sciatic_sensory` 0.02 | `hip_lig` 0.04 (`groin` x2) | none |
| lower_leg | tibial artery 0.05, vein 0.05 | achilles 0.08 | `leg_motor` 0.02 | `leg_sensory` 0.02 | `knee_lig` 0.04 (`back_of_knee` x3) | none |
| foot | pedal artery 0.03 | foot tendon 0.08 | `foot_motor` 0.02 | `foot_sensory` 0.02 | `ankle_lig` 0.04 | none |

Area fractions are shares of the part's cross-section. Hit chance per structure = `min(1, area_fraction x width x gap_boost x facing_mult)` where `width = 1 + min(contact_area, 60)/20` (v1). Facing multipliers apply to `access` only: front 1.0 / flank 1.0 / rear 1.0 for `any`; for `rear` access front 0.3, flank 0.8, rear 1.6 (clamped to 1).
The six ligament joints exactly match the plan: shoulder, elbow, wrist, hip, knee, ankle.

### 3.4 Tissue additions (`materials/tissues_v2.jsonc`)
| Tissue | Why | Numbers (DF raw, as `tissues.jsonc`) | New resolver fields |
|---|---|---|---|
| `ligament` | joint stability | like `tendon` (SY/SF 20000/40000) | `cut_resist_mult` 1.0 |
| `spinal_cord` | cord | like `nerve` | 1.0 |
| `dried_flesh` | draugr | SY 30000, SF 60000, IY 0.02, IF 0.04, density 900 | 1.0 |
| `hide_thick` | cave beast, horse | skin x1.6 shear, thickness set per plan | 1.0 |
| `bone_tissue` (changed) | bone is a real stop | unchanged raw numbers | **`cut_resist_mult` 2.5, `crush_resist_mult` 4.0** |
| `cartilage` (changed) | ear, nose, trachea | unchanged | `cut_resist_mult` 1.5, `crush_resist_mult` 2.0 |
New per-tissue fields: `cut_resist_mult`, `crush_resist_mult`, `blunt_absorb_pm` (skin 20, fat 80, muscle 120, bone 0, organ 0, hide_thick 40; permille of momentum a crushed layer absorbs), `pain_pm` (skin 700, fat 500, muscle 1000, bone 1500, nerve 2500, organ 1800, 1000 = plain).

### 3.5 Graded muscle and other part states
Each part has a `PartState` row per person (only for parts with wounds; sparse): `MuscleGrade` (`Sound, Bruised, Torn, Mangled`), `BoneGrade` (`Sound, Cracked, Broken, Displaced, Shattered`), `JointGrade` per ligament (`Sound, Sprained, Torn, Ruptured`), `SkinOpen` (bool), `Severed` (bool), `Dressing` (`None, Pressure, Bandage, Splint, Tourniquet, Stitched`, wounds file), `Paralysed`, `Numb` (nerve flags, permanent). 4 bytes of flags plus grades, about 12 bytes per wounded part.
Muscle grade effect on the part's strength: Sound 1000 pm, Bruised 750, Torn 400, Mangled 0 (function lost; heals only to Torn). Several wounds combine by the worst grade; each extra wound at the same grade subtracts 100 pm, floor 0.

### 3.6 Derived capacities (`BodyState`, recomputed when a wound changes, never per tick)
All values permille unless stated; integer; one pure function `BodyState.From(person, wounds)` (golden tests AN-1 to AN-14).
| Capacity | Formula |
|---|---|
| `Sight` | 1000 - 500 per lost eye (floor 0). Vision radius uses `LightModel.VisionRadius(light)` x `Sight`/1000, minimum 1 m when `Sight` = 0 |
| `Hearing` | 1000 - 500 per lost ear |
| `Grip[side]` | `Arm[side]` x (thumb 300 + fingers 700 x (4 - lost)/4)/1000; 0 if forearm tendon or `hand_motor` cut |
| `Arm[side]` | min of: shoulder, upper arm and lower arm muscle strength; 0 if arm bone `Broken` or worse and not splinted, 0 if paralysed; x600 if `shoulder_lig` Torn, 0 if Ruptured and unsurgeried |
| `Walk[side]` | 0 if leg bone `Broken` or worse and unsplinted, or kneecap broken, or paralysed, or `hip/knee/ankle_lig` Ruptured; x700 if a ligament is Torn; x1000 otherwise; scaled by muscle strength |
| `WalkSpeedPm` | both legs OK: 1000; one leg 0: 250 hop (550 with a crutch, 600 with a splint on a `Cracked` bone); both 0 or pelvis broken: 100 (crawl) |
| `Breath` | 1000 - 250 per broken rib side - 500 per collapsed lung; below 400 no sprint, below 200 unconscious in 30 s |
| `Speech` | 1000, 500 if jaw broken, -40 per lost tooth fifth |
| `Balance` | 1000 - 150 per unstable joint - 300 per missing leg; `Balance < 600` gives a stumble roll when running or carrying (stream 0x417): 3% per game second |
| `Consciousness` | from wounds file: pain, blood, concussion |
| `WeaponHand[side]` | can wield = `Grip` >= 400 and `Arm` >= 400 |
| `Overhead` | false if the collarbone on the weapon side is broken |
Spinal cord effects (permanent, always, plan §4A.3):
- `cord_neck` hit with `Cut` or crush wound of depth >= 0.5: **all four limbs paralysed**; breathing continues on partial damage; a complete cut (depth 1.0) stops breathing: unconscious in 30 s sim time, dead in 240 s.
- `cord_back_u` hit: both legs and the lower body paralysed, arms unaffected, no bladder control.
- `cord_back_l` hit: both legs paralysed.
- Partial cord crush (blunt, `Shattered` vertebral bone): same effect as a cut at that level but each limb group keeps a 20% chance of 400 pm residual strength (stream 0x416, drawn once at injury time and stored).
- Motor nerve cut: the section and everything distal is paralysed (`Arm` and `Grip` or `Walk` of that limb 0 below the cut). Sensory nerve cut: no feeling and no pain there (wounds there add no pain), blows struck with that hand have momentum x0.85 (grip feedback lost).

## 4. Rules

### 4.1 The horse plan (`bodies/horse.jsonc`, `body_plan` id `horse`)
Body size 450,000 cm3 for a 450 kg palfrey; the plan stores relative sizes and each animal scales (`SizeCm3 = mass_kg x 1000`). Blood **72 ml/kg** (30,240 ml for 420 kg), `bleed_scale_pm` 2500 (a horse's artery gives 200-300 ml/s which is about 0.8% of its blood per second, so a major wound kills in about 50-75 s, not 25 s). Parts:
| Part | Rel. size | Depth mm | Layers (mm) | Structures / organs | Severable |
|---|---|---|---|---|---|
| `head` | 0.050 | 300 | hide 4, fat 2, bone 12 | brain 0.25, eyes 2 (hosted), ears 2 | no |
| `neck` | 0.080 | 220 | hide 5, fat 8, muscle 70, bone 20 | carotid 0.05, jugular 0.06, cord_neck 0.03 | no |
| `chest` | 0.200 | 450 | hide 5, fat 8, muscle 40, bone (ribs) 12 | heart 0.05, lung_l 0.12, lung_r 0.12 | no |
| `belly` | 0.190 | 420 | hide 4, fat 6, muscle 30 | intestines 0.25, liver 0.06, stomach 0.06 | no |
| `rump` | 0.150 | 400 | hide 5, fat 10, muscle 80, bone 20 | femoral-class artery 0.05 | no |
| `fore_leg_u` x2 | 0.030 | 130 | hide 4, muscle 35, bone 25 | artery 0.06, tendon 0.05 | yes |
| `fore_leg_l` x2 | 0.020 | 70 | hide 3, muscle 8, bone 22 | tendon 0.10 (the flexor tendons: **a cut means a lame horse, permanent**) | yes |
| `hind_leg_u` x2 | 0.040 | 160 | hide 4, muscle 55, bone 28 | artery 0.06, tendon 0.05 | yes |
| `hind_leg_l` x2 | 0.022 | 75 | hide 3, muscle 8, bone 22 | achilles-class tendon 0.10 | yes |
Relative sizes sum to 1.0 with 0.134 left for hooves, tail and mane (not targetable; a blow there lands on the neighbouring leg part). Hit chance uses the same formula: largest part is the chest (0.20), so against the chest factor 1.0, head 0.2+0.8x0.25 = 0.40. **The horse is a large target**: a horse's `size_class` adds +10 points to attacker skill delta when targeted from the front (the rider's reach bonus is in mounted-combat.md). Capacities: `Walk` per leg like a human leg; a horse missing one leg or with a leg `Broken` is down (`Fall`, mounted-combat 4.4); lame (tendon) horses use `Animal.Lameness` 1..3 (existing byte): tendon cut sets 3 (unusable), a muscle `Torn` leg sets 1. `HealthPm` stays as a derived summary for UI (`1000 - blood_loss_pm/1.0 - sum of grade penalties`). Butchering (`ungulate_large`) reads the tissues of the plan: hide, meat (muscle mass), bone, tallow.

### 4.2 Other animals (reuse one quadruped template)
`dog`, `wolf`, `bear`, `deer`, `ox`, `sheep`, `pig`, `hen` use a shared quadruped base (`bodies/quadruped_base.jsonc`) with `copy_from`: head, neck, torso (chest + belly merged), 4 legs (one part each), no inset organs except heart, lungs and liver, `hide` thickness by species (wolf 2 mm, bear 6 mm, ox 5 mm, pig 4 mm, sheep 3 mm, hen 0.5 mm feathers over 0.5 mm skin), blood 70 ml/kg. They do not get ligaments, motor and sensory nerves or cords (a single `nerve` structure each), only `Cut` and `Crush` wounds with bleed rates scaled by mass. Animal attacks are `AttackDef` rows on the species (wolf bite: `point`, contact area 6, penetration 40 mm, velocity 1.2, strength 1100 equivalent; bear claw: `edge` area 10, penetration 70, bite `point` area 8; boar tusk `point`; ox horn `point` area 12) and they go through the same resolver (plan §2.6, "animal attacks and monsters").

### 4.3 Monster 1: draugr (`bodies/draugr.jsonc`)
The barrow dead (plan §3b): the human plan with `copy_from: human`, `tissue_set: "draugr"`, then:
- `bleeds: false`, `feels_pain: false`, `blood_ml_per_kg: 0`: no bleeding, no pain pool, no shock, no concussion knock-out. It cannot be "knocked out" or made to surrender (morale fixed at 100, no rout).
- Tissues: skin, fat and muscle replaced by `dried_flesh` (SY 30000, SF 60000: cuts need about 2.8x the momentum of live flesh), bone is `bone_tissue` (still cut_resist 2.5), no organs (heart, lungs, liver removed) so no organ wounds; no vessels; **sensory and motor nerves and ligaments kept** (a cut limb tendon or nerve still disables it: you disable a draugr by destroying its limbs).
- `animating_core`: `head`. It is destroyed (dies for good) when the head is `Severed` or the skull is `Shattered`, or when `upper_body` is `Severed` or `Shattered` ribs on both sides. Losing both arms and both legs leaves it crawling (Walk 100 pm, attacks by bite only). It does not die from losing limbs.
- Stats: strength 1900 (x1.5 a normal man), size 80,000 cm3, toughness 20 (irrelevant, no pain), `DeathReason` "undone".
- Weak to fire: a lit torch or oil flask ignites `dried_flesh` (ignition point 250 C against a torch flame at about 600 C, plan §2.10). A part on fire takes a `Burn` wound that deepens by 0.1 of the part's total layer depth per sim-second, so a part burned to depth 1.0 is `Destroyed` (like a severance, no blood). The head has four layers (hair 0.5, skin 2, fat 2, bone 7 mm; bone burns at one third the rate), which puts **the head burning through at about 30 sim-seconds**; test AN-12 asserts 30 +- 5. A burning draugr keeps fighting until then, and sets fire to thatch and timber it touches.
- Legend hook: a draugr's `Site.HistoryEvent` names who it was; the flavour log uses the name ("the barrow-dead Ketil Ravenfoot").

### 4.4 Monster 2: cave beast (`bodies/cave_beast.jsonc`)
A pale, blind-eyed cave predator of folklore, bear-sized (mass 380 kg, size 380,000 cm3), quadruped on the shared base with:
- Eyes small and useless (`sight_pm` 50): it hunts by hearing and smell (spot radius 25 m for sound, torch light does not blind it; sneaking Z halves the noise radius).
- Hide: `hide_thick` 8 mm over fat 20 mm over muscle 80 over bone 25; head bone 30 mm. Cut needs about 2.5x the momentum of human skin for the hide; most blows to the body cut skin and fat but stop at muscle. Weak spots: **throat** (`neck` hide 4 mm only, named gap `throat`, area x2) and eyes (called shot).
- Attacks (per `AttackDef`): `claw` edge area 14, penetration 90 mm, velocity 1.4; `bite` point area 8, penetration 150 mm, velocity 1.3, `slam` blunt area 80, velocity 1.0 (used to knock down shields). Strength 2300, speed 7 m/s sprint for 4 s, 3.5 m/s otherwise. Blood 70 ml/kg = 26,600 ml, `bleed_scale_pm` 2000.
- It flees below 30% blood (animal morale: `fear_pm` check at 2 Hz) unless cornered, then fights on.
- Placement: `Site.LairKind == "cave_beast"` (already placed by `BuildLairs`, 1-3 lairs per region, always deep, 2-3 levels). `bog_thing` stays a rumour (3 monster kinds exist in `LairKind`, 2 get plans in Phase 4, plan says 3 to 6 kinds total by 1.0).

## 5. Tick placement (20 Hz live versus abstract)
- Body plans are loaded into `CombatTables.BodyPlans` once, precomputed per `(plan, size)` (part areas, structure probabilities, named-gap map). Zero allocations per resolution.
- `BodyState` is recomputed **on a wound change event**, not per tick. Live tick reads it. Cost: 3 us per recompute, at most 530 recomputes per second in a 2,000-soldier fight.
- Abstract tier: no live body. People carry a list of `WoundRecord` rows (wounds file 3.1) and a `BodyState` is built lazily when the person is promoted or opens the character screen. The hourly tick only needs `WalkSpeedPm`, `Arm`, `Consciousness` summary (3 bytes cached on the row).
- Paralysis, nerve loss, missing parts and malunion are permanent facts stored on the person row (`CharacterPermanent`: 8 bytes), so they survive demote and promote and reach the abstract tier (a paralysed man cannot do a farm job).

## 6. Determinism
Streams in the 0x400 block (registered in `Core/RngStreams.cs`, uniqueness test):
| Stream | Use | Key |
|---|---|---|
| 0x400 `BodyVariation` | stature (size +-8%), blood volume +-5%, handedness (10% left) at person creation | person |
| 0x401 `MonsterVariation` | monster size +-10% at spawn | creature id |
| 0x416 `HitJitter` | partial cord residual strength, fall and joint-wrench placement | person, injury serial |
Everything else here is a pure function of the wound list. Plan loading is deterministic (stable `Parts` order: content order, sided parts `_l` then `_r`). Hashes: `BodyHash` (all `PartState` rows) joins the daily hash trail.

## 7. UI and controls
- **BodyDiagram** (existing widget) draws the 35 human instances as hit zones; hover shows the part, its tissues, its function and its wound grades. The paper-doll overlay layers (blood, bandage, splint, missing limb) follow part state (plan §2.7). No new keys. `C` (character) opens the doll; the called-shot context (`T`) lists parts by the same list: up and down move through 35 parts in **groups** (head group: head, eyes, ears, nose, jaw, teeth; torso: upper body, ribs, collarbones; arms; legs; spine hidden unless the target shows its back), so the list stays at 6 groups of at most 8.
- Capacities are shown as icons with a permille bar in the character screen ("Walk 250: hopping").
- A tooltip on an icon states the cause chain ("Left leg cannot bear weight: kneecap broken at the siege of Oakford").

## 8. Tests (golden)
| Id | Check |
|---|---|
| AN-1 | `human.jsonc` v2 loads: 12 new base ids, 35 instances, every `host` resolves, every `exposure_gap` is a named gap, structure area fractions per part sum <= 0.45 |
| AN-2 | Migration: a v1 wound on `arm_nerve` loads as `arm_motor` and `arm_sensory` cut |
| AN-3 | `Walk`: both legs sound 1000; right kneecap broken -> `WalkSpeedPm` 250; with crutch 550; broken pelvis -> 100 |
| AN-4 | `Grip`: thumb lost -> right grip 700 of 1000; all 4 fingers lost -> weapon hand false |
| AN-5 | Collarbone broken -> `Overhead` false and shield cover -0.15 |
| AN-6 | Sight: one eye -> 500, spotting radius x0.8; both -> 0 and vision radius 1 m regardless of torch |
| AN-7 | Motor nerve cut in upper arm paralyses upper arm, lower arm and hand; sensory cut leaves strength, removes pain contribution, momentum x0.85 |
| AN-8 | `cord_back_l` hit paralyses both legs only; `cord_neck` complete cut -> dead after 240 s (golden) |
| AN-9 | Graded muscle: one Torn upper arm = 400 pm; add a Bruised wound -> 400 - 100 = 300 pm |
| AN-10 | Horse plan: relative sizes sum 0.866 targetable + 0.134 untargetable = 1.0; chest hit factor 1.0, head 0.40 |
| AN-11 | Horse tendon cut on a lower fore leg sets `Lameness` 3; a Torn muscle sets 1 |
| AN-12 | Draugr: no `Blood`, no pain, a severed head ends it, four severed limbs does not; fire destroys the head in 30 sim s +- 5 |
| AN-13 | Cave beast: eye `sight_pm` 50; throat gap blow has x2 structure area; flees below 30% blood |
| AN-14 | Determinism: two loads of the content produce the same `BodyHash` for 1,000 seeded persons |

## 9. Open questions (recommended defaults)
| # | Question | Default |
|---|---|---|
| A1 | The plan says "about 25 parts"; this lands at 19 new instances (12 ids) plus 4 existing trunk, 16 limb parts. Add more (toes, tongue, shoulder blade)? | No. 35 instances is enough; revisit only if testers ask for more |
| A2 | Are ears and teeth worth being parts at all? | Keep, they are cheap (sparse state), and they feed the flavour log and dialogue |
| A3 | Should ligaments be reachable by an unaimed blow or only by joint wrench and called shots? | By both: structure roll when the muscle layer is cut, plus a `JointWrench` event on a fall or a very heavy blunt hit (grade by momentum ratio) |
| A4 | Spinal cord reachability from the front | x0.3 on the torso, x0.6 on the neck, to keep paralysis rare but real (about 1 serious hit in 40 to the back at 18 points of reach) |
| A5 | Can a draugr be knocked out or made to flee? | No: it is undone, not beaten. Fire and dismemberment are the answers |
| A6 | Do animals have the human soft-tissue detail? | No (one nerve, no ligaments). Only horses get tendon-driven lameness |

## 10. Budgets
| Item | Budget |
|---|---|
| `CombatTables` body-plan precompute | <= 15 ms at load for 8 plans x 7 size classes |
| `BodyState.From` | <= 3 us, 0 allocation |
| Per-person `PartState` rows | sparse; <= 12 bytes per wounded part, <= 32 wounded parts; typical 0 |
| Save growth | <= 40 bytes per wounded person; 2,000 wounded after a battle = 80 KB |
| Content | `human.jsonc` v2 <= 90 KB, horse + quadruped + 2 monsters <= 120 KB |
