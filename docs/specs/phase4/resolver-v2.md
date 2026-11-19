# Resolver v2: momentum, layers, cut, blunt and fail, penetration, severing, structures, organs, called shots and armour gaps

Plan refs: §2.6 (the single spec: inputs, hit chance, coverage, the DF momentum rules per layer, tissues, wound records, layer order, cost, "outcomes never depend on where the camera is", the bugs v1 replaces), §2.10 (materials, four armour layers, ±1/7 fit, wear levels worn/threadbare/tattered, tears when cut through), §4A.3 (the human detail, the list of what is broken), §6 (material-science golden tests: the battle-axe momentum table, mail turning cuts into blunt blows without dampening, rigid metal soaking 98-99% of a stopped blow, light weapons deflecting off dense armour, severing when penetration exceeds part size). Spike results: `docs/spikes/02-melee.md` (lethality is a resolver problem: 0.5 to 1.9 limbs severed per duel, most bleed-outs untreated).
Code today (resolver v1): `StrikeResolver` (`Resolve`, `ResolveImpact`, `HitChance`, `InitialMomentum`), `Momentum`, `CombatTables` (`WeaponStats`, `ArmourStats`, `LayerProps`), `BodyPlan`, `Wound`, `WoundFlags`, `LayerTrace`, `LayerOutcome`, `Fit` in `src/Ravensguard.Sim/Combat/`. v1 does its arithmetic in `double`; v2 moves to integers (section 3.1).

## 1. Purpose
One function turns "this attacker, this weapon and attack, this target part, this defender and what they wear" into a list of wound records and a layer trace. Melee, missiles, thrown objects, falls, animal attacks and monsters all call it (plan §2.6). v2 keeps v1's shape and fixes what the spike found: every landed blow wounded too deeply, bone never stopped an edge, padding did nothing, and energy was not conserved.

## 2. Dependencies
| Needs | From |
|---|---|
| Body plans with hosted parts, new structure kinds, `BodyState` | `anatomy-and-body-plans.md` |
| Worn layers and `ArmourShapeDef` (`layer`, `layer_size`, `layer_permit`, `coverage`, `gaps`, `thickness_mm`, `woven`, `elastic`) | exists (`items/armour.jsonc`) |
| Weapon shapes and `AttackDef` (`type`, `contact_area`, `penetration_mm`, `velocity`) | exists (`items/weapons.jsonc`) |
| Wounds, pain, bleeding after the blow | `wounds-bleeding-pain-healing.md` |
| Real-time timing, stamina, who attacks whom, blocks | `melee-and-ranged.md` |
| `DetRng` counter RNG, `Fixed` | exists (`Core/`) |
New code: `Sim/Combat/Resolver2/` (`ResolverV2`, `LayerTest`, `StructureRolls`, `Blocks`, `Calibration`), tool `tools/calibrate` (headless duel and blow harness).

## 3. Data

### 3.1 Number formats (v2)
- **Momentum** is `long` milli-units (1,000 = 1.0; a sword blow at skill 50 is 156,525). Layer thresholds are `long` milli-units. Thickness mm, area cm2 and ratios are `int` x1000. No `double`, no `Math.Pow/Sin`. Division is integer floor division in a fixed order (test RS-24).
- Quality multiplier table (existing `QualityTable`) x1000: 1000, 1200, 1400, 1600, 1800, 2000, 3000.
- Initial momentum, integer form of the v1 formula (`sp` = species size cm3, `str` = DF-scale strength, `velPm` = attack velocity x1000, `mass` = weapon mass in mg, `size` = attacker size cm3):
  `M0_milli = ((sp x str / 1000 x velPm) x mass / (mass + size)) / 1000`
  Check: arming sword (mass 1,453,800 mg, velocity 1.25), human strength 1250, size 70,000: `M0 = 104,350`. Skill multiplier `1000 + 10 x skill` (skill 50 = x1.5 = 156,525), style x0.5 quick / x1.0 / x1.5 heavy, x2.0 prone, plus mount and missile terms (mounted-combat.md, melee-and-ranged.md).
- Weapon and armour derived stats are precomputed per `(shape, material, quality)` in `CombatTables` (existing); v2 adds `ArmourStats.BluntAbsorbPm`, `WeaponStats.PenMm`, and per-tissue `CutResistPm`, `CrushResistPm`, `BluntAbsorbPm`.

### 3.2 Records
```
StrikeInput  { Attacker (Strength, Skill, SizeCm3, SpeciesSizeCm3), Weapon (shape, material, quality), AttackId, Style, TargetPart, TargetGap?, TargetInset?, Defender (Plan, SizeCm3, Worn[], Skill, GuardedParts, Prone, ShieldBlock?), Facing, Condition[] }   // + ExtraMomentumMilli (mount, missile, fall)
Wound        { PartId, TissueId, StructureId?, Kind (Cut|Crush|Burn), Depth (permille), Grade (MuscleGrade|BoneGrade|JointGrade), BleedMlPerS (x10 fixed), Flags }
LayerTrace   { Layer, IsArmour, Outcome, MomentumInMilli, MomentumOutMilli }   // outcomes add Blocked, Parried, Absorbed
StrikeResult { Hit, PartId, Redirected, Wounds[], Trace[], InitialMilli, FinalMilli, Severed, Deflected, Blocked }
```
`WoundFlags` appends (existing values kept): `LigamentHit` 256, `MotorNerveHit` 512, `SensoryNerveHit` 1024, `SpineHit` 2048, `InsetHit` 4096 (eye, ear, nose, jaw, teeth), `Displaced` 8192, `Compound` 16384 (bone through skin), `Burn` 32768. `WoundKind` appends `Burn`.
Content additions (`content/base`): tissue fields `cut_resist_pm`, `crush_resist_pm`, `blunt_absorb_pm`; armour shape field `blunt_absorb_pm` (permille of momentum a crushed layer absorbs: linen shirt 30, tunic 60, leather jerkin 150, **gambeson 450**, padded coif 300, mail 0, plate 0, cloak 40); weapon `reach_m` (melee-and-ranged.md); condition levels (`worn` 800, `threadbare` 500, `tattered` 250 permille) per item row.

## 4. Rules

### 4.1 The blow, in order
1. **Momentum.** `M = M0 x skill x style (+ prone x2) + extras`. Extras: mounted charge, missile launch, fall (their files). Clamp to [0, 2,000,000].
2. **Block check** (4.8): done before the hit roll for attacks a defender is actively guarding; a blocked blow continues as a weakened blow or ends.
3. **Hit chance** (plan §2.6, kept; computed on the zone aimed at, before any redirect):
   `P = (200 + 800 x size / largest) x (500 + 5 x (skillA - skillD)) / 10^6`, then x0.6 if the zone is guarded, x0.35 if aimed at a named gap whose armour is worn, x1.25 flank, x1.5 rear, clamp [2%, 98%].
   Integer: permille arithmetic; `P` as permille in 0..1000; roll `Range(0, 10000)` below `P x 10` (stream 0x410).
   Golden hit chances (equal skill / attacker +20 / attacker -20 / attacker +50): upper body 50.0 / 60.0 / 40.0 / 75.0 %; upper leg 30.0 / 36.0 / 24.0 / 45.0; lower leg 26.0 / 31.2 / 20.8 / 39.0; head 22.0 / 26.4 / 17.6 / 33.0; foot 14.9 / 17.9 / 11.9 / 22.3; hand 13.1 / 15.7 / 10.5 / 19.7; neck 14.0 / 16.8 / 11.2 / 21.0.
   **Hosted parts (eyes, ears, nose, jaw, teeth, thumb, fingers, kneecaps):** a **called shot** at a hosted part has `P = P(host zone) x aim_factor_pm / 1000` (eye 12%: equal-skill called shot at an eye = 22.0 x 0.12 = **2.6%**; nose 30%; jaw 40%). An **unaimed** blow that lands on the host never aims at the inset; it may still hit it through the structure rule (4.5) if the exposure condition holds.
4. **Miss.** 50% of a missed called shot lands on a neighbouring zone (stream 0x411: draw 1 = redirect yes or no, draw 2 = which neighbour in the plan's stable neighbour order). A missed hosted-part shot lands on the host first, then siblings. Otherwise a clean miss.
5. **Coverage and layers** (4.2 to 4.4), outside in: cover, armour, over, under, then body tissues.
6. **Structures, insets, organs, severing** (4.5 to 4.7).
7. **Output** a `StrikeResult`. The melee layer applies wounds to the defender's `WoundBook` (wounds file) in the same tick.
Draw order is fixed and documented per resolution: hit (0x410), redirect (0x411), one coverage roll per covering piece outside in (0x412), then structure rolls in part order (0x413), then organ rolls (0x414), then artery rate (0x415). A test (RS-22) fails if the draw order changes.

### 4.2 Coverage and gaps
Each worn piece rolls its `coverage` for the base id of the struck part (inset parts use their host's coverage). Gaps (`armpit`, `groin`, `face`, `neck`, `inner_elbow`, `back_of_knee`; new: `throat` for beasts): a gap-aimed edged or point blow ignores the `armour`-layer pieces that list that gap (trace `Bypassed`); blunt blows cannot slip through a gap (a mace "at the armpit" tests the piece normally). Pieces that do not fit (body size off by more than 1/7, `Fit.Fits`) have coverage -25 points. A closed-visor helm lists no `face` gap; `visor_open` adds it (state on the item).

### 4.3 Testing one layer (cut, blunt and fail)
Per layer with `A = min(weapon contact area, layer area)` (layer area = part size x coverage for worn pieces, part size for tissues), quality `Qa` (1.0 for tissue), condition multiplier `Cm = (500 + cond_pm/2)/1000` (tattered armour needs half the momentum):
- **Edged test** (only while the blow is still edged): `need = (rSY + (A+1) x rSF) x (10 + 2 Qa) x Cm x CutResist / (S x Qw)`, where `rSY`, `rSF` are layer-to-weapon shear ratios, `S` the weapon material's max edge, `Qw` the weapon quality. **Cut if `M >= need`.** Then `M' = max(0, min(0.95 M, M - 0.6 x need))` (**conservation: v2 rule C1**; v1 kept 95% always, which is why one blow went through everything). Penetration `pen -= layer thickness`; `pen <= 0` ends the edge (the blow turns blunt).
- **Deflect** (worn layers only, blunt or failed-edge): if `2 x weapon volume x weapon impact yield < A x layer density`, the blow bounces: `M = 0`, trace `Deflected` (light weapons off dense armour).
- **Blunt test:** `need = (2 IF - IY) x (2 + 0.4 Qa) x A x CrushResist`. **Crushed if `M >= need`:** `M' = min(0.95 M, M - 0.6 x need)` (non-elastic) or `0.95 M` (elastic: mail, so a cut turned blunt is **not** softened), then `M' x (1000 - blunt_absorb_pm)/1000` (**v2 rule C3: padding absorbs**).
- **Fail both:** the blow is blunt for good and `M' = M x min(1, strainAtYield / 50,000)` (shear strain if it was edged, impact strain otherwise; elastic = 50,000 so mail does not damp). Steel: shear 215 -> 0.43%, impact 940 -> 1.88%; iron impact 319 -> 0.64%; bronze impact 547 -> 1.09%. **Rigid metal soaks 98 to 99.6% of a stopped blow** (test RS-9).
- **Rule C2 (bone is a real stop):** `CutResist` and `CrushResist` default to 1000; `bone_tissue` 2500 and 5000, cartilage 1500 and 2000. A blow whose edge fails bone becomes blunt and meets the crush test at **half** its ratio (an edge that cannot bite spends its force sliding).
- Wear: a worn layer that is cut loses condition `30 pm` (cloth), `10` (mail), `5` (plate); crushed `10`, `5`, `3`; leather in between. A tear shows on the paper-doll when below 800.

### 4.4 Worked calibration table (steel arming sword/axe/mace/spear, human strength 1250, skill 50 unless noted)
Standard quality, `Qa = Qw = 1.0`, coverage rolls all pass. Values in momentum units (thousandths in code). **These are the golden values (RS-1 to RS-8); they are what the formulas give, not wiki transcriptions** (see 8 and open question R1).
| Case | Blow M0 | Layers (need -> M out) | Result |
|---|---|---|---|
| Sword slash (area 30, pen 70) on bare thigh | 156.5 | skin 10.6 -> 148.7; fat 4.3 -> 141.3; muscle 6.4 -> 134.2; bone (crush need 72.0, ratio 0.93) -> 91.0 | skin, fat cut, muscle **torn** (depth 53/53 mm), bone only bruised. No fracture, no sever |
| Same through linen gambeson (cut need 16.1) | 156.5 | gambeson 146.9; then as above 125.9 at bone | as above, slightly less deep |
| Same vs iron mail over gambeson (mail cut need 164.5 > 156.5) | 156.5 | mail crush need 116.5 -> **148.7 (not softened)**; gambeson absorbs 45% -> 77.7; skin 72.3; fat 63.2; muscle 52.9; bone stops (72.0): -> 1.1 | **bruise only**: padding under mail matters |
| Same vs iron mail, no gambeson | 156.5 | mail 148.7; skin 138.4; fat 121.0; muscle 101.2; bone 58.0 (ratio 1.40) | muscle **mangled** at m>=100, bone **cracked** |
| Mace (area 20, vel 1.0) on bare thigh | 127.1 | skin 118.4; fat 103.5; muscle 86.5; bone need 48.0 (ratio 1.80) | muscle torn, bone **cracked** (heals 20 days set, wounds file) |
| Mace vs gambeson | 127.1 | gambeson absorbs -> 66.4; skin 61.8; fat 54.1; muscle 45.2; bone stops | muscle torn, bruise on bone |
| Spear thrust (area 12, pen 400, vel 1.3) vs gambeson, chest | 160.3 | gambeson cut 7.1 -> 152.3; skin 144.6; fat 137.4; muscle 130.5; **ribs cut** (need 77.4) -> 84.1 | breached; organ rolls (4.7) |
| Spear thrust vs mail over gambeson, chest | 160.3 | mail cut need 71.5 -> 117.4; gambeson 111.5; ... ribs cut -> 49.2 | breached; **mail does not stop a skill-50 spear** |
| Spear thrust vs steel breastplate (cut need 168) | 160.3 | plate crush need 101.8 -> 99.2; gambeson absorbs -> 51.8; ...; ribs crush need 28.8 (ratio 1.22) -> 18.0 | **cracked rib, no organ**: plate stops it |
| Sword vs bronze mail (cut need 129.3) | 156.5 | mail cut -> 78.9 (conservation: hard armour eats the blow); skin 72.6; fat 68.9; muscle 65.1; bone stops | skin and fat cut, shallow; nothing deeper |
| Axe hack (area 40, pen 110) **heavy** skill 50 on bare neck | 234.0 | skin 222.3; fat 211.2; muscle 200.6; bone need 229.1 (cut) fails -> crush need 96.0 ratio 1.04 | **no sever**, cracked vertebra, carotid roll |
| Same, skill 100 (x2.0, heavy) | 311.9 | ... muscle 267.5; bone cut 229.1 -> **130.0** (0.417 of M0) | **severed** (4.6): pen 110 >= depth 110 and 130.0 >= 0.30 x 311.9 = 93.6 |
Reading: a sword no longer amputates (the spike's finding); severing needs a heavy, skilled axe on an unarmoured part; mail alone does not stop a spear but padding under mail turns blunt trauma into bruises.

### 4.5 Structures inside muscle, ligaments, nerves, spinal cord, hosted parts
- **When rolled:** after the muscle layer is cut (v1 rule) the part's `Structures` are rolled in content order. Ligaments additionally on a `JointWrench` (fall, heavy blunt grade `Broken` or worse on the adjacent bone). Hosted parts (eyes and the like) are rolled after the host's soft layers (hair, skin, fat) are breached and `exposure_gap` holds (no armour piece outcome of `Cut`, `Crushed` or `Stopped` on the host: only `NotCovered`, `Bypassed` or no piece).
- **Chance each:** `min(1, area_fraction x width x gap_boost x facing_mult)` with `width = 1 + min(A, 60)/20`. Draw per structure from 0x413 with index = position in the plan's structure list (insets after structures).
- **Effects:** vessels as v1 (`artery` rate drawn 80-120 ml/s x `bleed_scale`; vein 15; set in wounds file), tendon cut -> `TendonHit`, motor nerve -> `MotorNerveHit` (paralysis, anatomy 3.6), sensory -> `SensoryNerveHit`, ligament grade by depth fraction: < 0.34 `Sprained`, < 1.0 `Torn`, full `Ruptured`; cord -> `SpineHit` (wounds file 4.9).
- **Gap boost:** an aimed blow at a named gap multiplies the area fraction of the structures that gap exposes (`inner_elbow` x3 elbow ligament, radial artery; `armpit` x3 shoulder ligament and brachial artery; `groin` x2 hip ligament and femoral artery; `back_of_knee` x3 knee ligament and tibial artery; `throat` x2 carotid). Since gap blows start at x0.35 hit chance, they are a deliberate skilled choice.

### 4.6 Penetration, severing, bone and the brain
- **Penetration** limits an edged or point blow: each cut layer (armour and tissue) consumes its thickness; at `pen <= 0` the edge ends. Point blows (`stab`, `thrust`) have large `penetration_mm` (sword stab 450, spear 400) and small area (so they cut armour easily and go deep), slashes small penetration (70).
- **Severing** (plan: "if it exceeds the size of the struck part the part can be severed"): `Severable` part, attack type `Edge`, blow still edged after every layer **including bone**, `pen_left_after_armour >= part.DepthMm` (reach left after armour, v1's follow-up fix), and `M_after >= 0.30 x M0` (v2 rule C4: enough momentum remained to finish the cut). The result is a `Severed` wound (90 ml/s, wounds file) and the part is removed with everything distal. Neck severing needs pen >= 110 mm: only the axe hack (110, unarmoured), the great axe (160) and polearm hooks qualify; any armour thickness subtracts. Blunt and point blows never sever.
- **Fracture grades** (bone layer crushed, ratio `r = M_in / need`, halved when the blow was edged on entry): `r < 1` bruise only; 1 to 2 `Cracked`; 2 to 4 `Broken`; 4 to 8 `Displaced` (organ roll through ribs, skull); >= 8 `Shattered` (`Compound`: through the skin). Shell bones (`skull`, `ribs`, `pelvis`) at `Displaced` or worse give the organ roll behind them; `skull` `Broken` or worse = concussion (wounds file).
- **Muscle grades:** cut: depth < 0.5 `Bruised` (shallow), < 1.0 `Torn`, severed `Mangled`. Crushed: `M_in < 40` `Bruised`, < 100 `Torn`, >= 100 `Mangled` (tunable `muscle_grades_m`).

### 4.7 Organs behind parts
v1 rolls organs only when every tissue layer was breached (`breachedAll`). v2:
- Edged or point: bone layer cut, or a **point blow that fails the bone while `pen_left >= 80 mm` slips between the ribs** with 35% (stream 0x413, draw index 63): on `upper_body` only. Reach multiplier as v1 (point 2.0, edge 1.0 + A/20 capped at 4.0, blunt 0.5).
- Blunt: bone `Displaced` or worse on ribs, skull or pelvis. Each organ rolls `area_fraction x reach` (stream 0x414). `heart` hit: collapse in 5-10 s; `brain` destroyed only by cut through the skull with `M_after >= 40` or `Shattered` skull with `Compound`; otherwise `concussion` (wounds file).
- Behind-part dependence: organs of a severed part are lost with it (a severed neck takes the trachea).

### 4.8 Blocks and parries (the resolver's side; timing is in melee-and-ranged.md)
A defender that is `Blocking` (or `Parrying`, `Dodging`) gets a pre-resolution step before layers:
- **Dodge:** evades with 80% (stream 0x430) if the blow starts inside the dodge window; the blow ends, trace `Dodged`.
- **Parry (success rolled by melee layer):** the blow ends; attacker staggers (melee file). Trace `Parried`.
- **Block** (weapon or shield; front arc only; a rear blow cannot be blocked, a flank blow cannot be shield-blocked): the blocker has a **capacity** `C`: weapon `110 + skill/2` (momentum units); heater shield 200; tower shield 300; buckler 160; plus `+10% per braced neighbour` (max 2, shield wall). If `M <= C`: the blow is stopped, `Blocked`, the blocker's stamina pays `3 + (M/50)`. If `M > C`: the excess `M - C` continues as a **blunt** blow on the **blocking arm** (`lower_arm` of the shield or weapon side) and the blocker staggers 0.5 s. Shield condition loss `= M x 1000 / durability` permille (durability: wooden heater 700, round 600, tower 1100, iron buckler 900); at 0 the shield is `Broken` (unusable). Sword skill 50 (156) vs heater (200): stopped. Heavy axe skill 100 (312) vs heater: **112 passes** to the forearm (bruise or crack) and the shield loses 45% of its life.
- Blocked blows against a held weapon notch it: `-20 pm` condition per 50 M absorbed.

### 4.9 Other attackers through the same function
- **Missile** (`ExtraMomentumMilli` from launcher) and **thrown**: melee-and-ranged.md 4.5 and 4.6; the hit roll is replaced by the projectile path test, the layer stack is identical (`ResolveImpact`).
- **Fall** (`AttackType.Blunt`, area 40 cm2, `ExtraMomentumMilli = 100 x mass_kg x (3 x height_m + 0.5 x speed_mps)`): an 80 kg man falling 3 m gives 72,000 milli; against a leg bone (crush need `0.2 x 2.4 x 40 x 5 = 96`) after the soft layers that is a bruise. **Falls of 3 m bruise, 6 m crack a bone, 12 m break one**; armour layers absorb as for any blunt blow. Placement of the landing part: 0x416.
- **Animal and monster attacks:** `AttackDef` rows on the species; strength and size from the species row; their tissues come from their own plan.
- **Fire/burn** (draugr, torches): not a blow; a per-second `Burn` wound step (anatomy 4.3), not this function.

## 5. Tick placement
- Live 20 Hz: called from the melee layer at the strike tick of an attack (not at wind-up start), and from the projectile step when a projectile reaches a body. Resolution is a function call: no allocation (`Span<LayerTrace>` of 16, `Span<Wound>` of 12).
- Abstract tier: **does not call this function per blow.** Abstract battles use wound tables baked by `tools/calibrate` from this resolver (abstract-battles-v1.md); the same table builder runs in a CI test to prove the tables match fresh samples within +-3%.
- The 530 resolutions per second typical (2,000 soldiers, about 40% in contact, a swing every 1.5 s) and 1,300 peak (plan §2.6) must fit in 4 ms of the 25 ms battle-bubble tick budget (section 9).

## 6. Determinism
- Streams (0x400 block, key `FightTick` = the fight's own 20 Hz counter, entity = attacker, draw = `swingSerial x 64 + k`; Phase 3's rule "key on game time" is satisfied because a fight's tick is derived from sim ticks one to one and combat never lengthens a tick, see overview 6.1): 0x410 `HitRoll`, 0x411 `Redirect`, 0x412 `Coverage`, 0x413 `StructureRoll`, 0x414 `OrganRoll`, 0x415 `BleedRate`, 0x416 `HitJitter` (falls, joint wrench, partial cord), 0x417 `Stumble`.
- Same inputs and stream states give the same `StrikeResult` on both operating systems: integer math only (RS-24 runs a 100,000-blow table and compares a hash on Linux and Windows CI).
- Near and far soldiers: the function has no camera input. Test RS-23 resolves the same 5,000 blows with a "near" and a "far" flag set and compares result hashes.
- `ResolverHash` (hash of all `StrikeResult`s of a tick) joins the daily hash trail.

## 7. UI and controls
- The resolver's `Trace` feeds the flavour log (feedback-flavour-log.md), the tooltip on any wound ("Cut through linen gambeson (146.9), skin, fat, torn muscle; bone bruised") and the **called-shot preview**: in the called-shot context (`T`, paused) the part list shows live `P(hit)` computed by 4.1 step 3 with the defender's guard, gap and facing; for hosted parts it shows the aim factor ("eye 2.6%"). A second line shows "armour here: gambeson (cut at 16), mail (blocks a sword at 164)" computed from the piece needs against the held weapon's M0 at your current skill. No new keys (T, arrows, F, Esc as plan §2.8).
- The dev inspector (existing sim inspector) can replay any recorded blow with the trace.

## 8. Tests (golden)
| Id | Check |
|---|---|
| RS-1 | M0 sword 104,350; skill 50 156,525; heavy 234,788 (integer formula) |
| RS-1b | The DF wiki's battle-axe momentum table (Material science: 28 dwarf sizes x 8 metals) within 1 milli of every cell (df-calibration.md) |
| RS-2 to RS-8 | Table 4.4: seven cases reproduce the listed per-layer M out within +-1,000 milli and the same outcome words (`Cut`, `Crushed`, `Stopped`) |
| RS-9 | Rigid metal soak: a blow of 100.0 stopped by steel plate (shear) leaves 0.43; stopped blunt by steel 1.88; iron 0.64; bronze 1.09 (98 to 99.6% soaked) |
| RS-10 | Mail turning a cut into blunt: sword 156.5 vs iron mail: crush with M out = 148.7 = 0.95 x M in |
| RS-11 | Light weapon deflection: dagger blow (volume 65 cm3, steel IY 1.505, area 10) vs steel plate (density 7.85): `2 x 65 x 1.505 = 195.7 > 78.5`, not deflected; a wooden club (volume 1,000, oak IY 0.01, area 40, plate): `2 x 1000 x 0.01 = 20 < 314`, deflected |
| RS-12 | Severing: axe hack heavy skill 100 severs a bare neck (pen 110 >= 110); the same blow with a 4 mm mail coif (pen 106) does not; great axe (160) does |
| RS-13 | Bone is a stop: sword skill 50 slash on a bare thigh never cuts bone (10,000 seeds), bone bruise only |
| RS-14 | Hit chance goldens: the 7 x 4 table in 4.1 within 0.1 point; called eye shot 2.6% |
| RS-15 | Gap: aimed armpit blow with gambeson+mail: mail bypassed (trace `Bypassed`), hit chance x0.35 and brachial/shoulder structure area x3 |
| RS-16 | Flank x1.25 and rear x1.5 hit chance; a shield cannot block either (RS-17 with 4.8) |
| RS-17 | Block: sword 156.5 vs heater (200) stopped; heavy axe skill 100 (311.9) vs heater passes 111.9 to the forearm and shield loses 445 pm |
| RS-18 | Fit: a size-off-by-more-than-1/7 piece has coverage -25 points; `Fit.Fits` boundary cases |
| RS-19 | Condition: tattered gambeson (250 pm) needs `(500 + 125)/1000 = 0.625` of the normal cut momentum (16.1 -> 10.1) |
| RS-20 | Ribs: a point blow failing the bone with pen_left >= 80 slips between ribs 35% of 10,000 seeds (+-1.5 points) |
| RS-21 | Hosted eye: closed-visor helm -> 0 eye hits in 10,000 point blows to the face; open face -> eye hit rate within the area-fraction expectation (3% x width) +-0.5 points |
| RS-22 | Draw order locked (a recorded blow's stream index sequence is a golden) |
| RS-23 | Near equals far (identical result hash for 5,000 blows with the flag flipped) |
| RS-24 | Integer determinism: hash of a 100,000-blow table identical on both OS |
| RS-25 to RS-30 | The DF wiki's other numbers (df-calibration.md): weight matters little (gold vs silver hammer < 1%; our mace in five metals within 1%); the Item quality table (Qa, Qw with an artifact's edge a masterwork's, masterwork armour +16.7%); the "min M" crush column; the DF2014 material raws; the human part sizes; slade alone deflects a war hammer, and the strain rule over every weapon metal |
Calibration targets (checked by `tools/calibrate`, 2,000 duels per matchup, steel weapons, skills 30-59; spike baseline in brackets): mean duel length **10 to 20 s** (8.0 s); limbs severed per duel **<= 0.12** (0.34 to 1.00); defended blows **40 to 60%** (about 12%); sword-vs-sword A wins 50% +- 5; spear vs unarmoured sword **>= 55%** (11% before); knocked-out fighters who then bleed out with no treatment **<= 35%** (about 83%, partly bandaging, wounds file); fatal outcomes per duel (death + bleed-out) 15 to 35%.

## 9. Open questions (recommended defaults)
| # | Question | Default |
|---|---|---|
| R1 | Our golden numbers are what our formulas give; the DF wiki's worked battle-axe table is the real reference | Transcribe the wiki table into `tests/.../MaterialScienceGolden.cs` during the first calibration task; where it disagrees, the wiki wins and the content constants change, not the formulas |
| R2 | Rules C1 to C4 (conservation 0.6, bone resist x2.5/x5, padding absorption, 30% of M0 left to sever) are v2 inventions | Keep; each is a content-tunable number in `calibration.jsonc`; the harness picks values that hit the 8.4 targets |
| R3 | Should plate be `Crushed` (momentum passes at 62%) or `Stopped` for a spear? | Keep the formula result; plate yielding and bruising is legible, and RS-9 still holds for truly stopped blows |
| R4 | Does a gap-aimed blow need a skill gate? | No; the x0.35 hit chance is the gate. The called-shot preview shows it |
| R5 | One resolver for falls and fire? | Falls yes (4.9); fire is a separate per-second wound step (the same wound records) |

## 10. Budgets
| Item | Budget |
|---|---|
| One `Resolve` (typical 4 layers + 4 tissues + 3 structure rolls) | <= 3 us, 0 allocations, <= 4 KB stack |
| 1,300 resolutions per second peak | <= 4 ms per second (0.4% of a core) |
| `CombatTables` precompute (18 weapons x ~20 materials x 7 qualities; 40 armour x ~20 materials) | <= 60 ms at load, <= 4 MB |
| Content | 18 weapon shapes (~12 KB), ~40 armour shapes (~30 KB), `calibration.jsonc` (~2 KB) |
| Armour pieces (40): under 4 (shirt, braies, hose, padded coif), over 8 (tunic, gambeson, jack, leather jerkin, doublet, gloves, boots, cap), armour 14 (mail hauberk, mail coif, mail chausses, brigandine, breastplate, backplate, helm, great helm, kettle hat, gauntlets, vambraces, greaves, cuisses, pauldrons), cover 4 (surcoat, cloak, fur cloak, tabard), shields 4 (heater, round, tower, buckler), and 6 more by culture |

## Notes (P4-A implementation, step 1)
Code: `src/Ravensguard.Sim/Combat/Resolver2/` (`ResolverV2`, `LayerTest`, `Blocks`, `CombatTablesV2`, `BodyPlanV2`, `BodyState`, `BodyExtras` (burn, body variation, v1-wound migration), `ClothingArmour`, `V1Adapter`, `Calibration`), content `combat/calibration.jsonc`, tool `tools/Ravensguard.Calibrate` (the spec's `tools/calibrate`, named like the other tools). Tests: `tests/.../Combat/Resolver2/` (RS-1 to RS-24, AN-1 to AN-14, the metal ranking, determinism, no-float, content, the harness).
Decisions taken while building (each is content-tunable unless noted):
- **Table 4.4 reproduces** within 1.0 momentum unit on every listed layer (RS-2 to RS-8 cover the first seven rows; `RS8b` the other five). Two readings were needed to make the table consistent with itself: (1) an edge that fails bone and does not crush it at half ratio is `Stopped` (bone bruised) but keeps the **crush** momentum rule `min(0.95 M, M - 0.6 need)`, not the strain rule (row 1: 134.2 -> 91.0); a plain blunt blow that fails bone uses the strain rule (row 3: 52.9 -> 1.1). (2) Crushed muscle is graded by the momentum **leaving** it (mail row: 101.2 -> mangled; mace row: 86.5 -> torn); "M_in" in 4.6 is read that way.
- Rounding: M0 floors (104,350), the skill/style/prone multipliers round half up (heavy 234,788, RS-1). Hit chance is in basis points, coverage in permille, areas in ppm.
- A failed edge turns the blow blunt for good on any layer (v1 kept it edged after a crush); RS-10 depends on it.
- **Gaps:** a named gap can open onto more than one base part (`also`: armpit on the upper body and upper arm, groin on the lower body and upper leg, inner elbow on the lower and upper arm, back of knee on the upper and lower leg), so the structure boosts of anatomy 3.3 apply where the structures live. Aiming at a gap always costs x0.35 (also on an unarmoured target, where it still buys the structure boost; otherwise aiming at a beast's throat would be free). `mail_hauberk` now lists `armpit` (RS-15) and helms list `neck`.
- Called shots at hosted parts use the part's exposure gap implicitly (an eye shot goes through the face). A missed hosted-part shot redirects to the host only. The trace word for a gap is `Bypassed`; blunt blows never bypass.
- Draw order (RS-22 golden): hit, redirect, coverage outside in, then per part: inset rolls when the soft layers are breached (k = structures + index), structures after the muscle cut with each artery's rate drawn right after its hit (0x415), rib side (k 62), joint wrench (k 32 + i), cord residual (0x416), rib slip (k 63), organs. The resolver records every draw (`BlowResult.Draws`).
- Rib slip also needs the blow to have been edged on reaching the bone (a blunt blow cannot slip). Before-bone organs (`before_bone`: the trachea) roll with the structures.
- Severing of hosted parts (ear, thumb, fingers) uses the same C4 rule on the inset's own layers; fingers lost = 1 + area / 20, at most 4.
- `WoundFlags` appended beyond the spec's list: `Concussion` (65536: skull Broken or worse, or a brain blow short of destruction) and `RibSlip` (131072). `LayerOutcome` appended `Blocked, Parried, Absorbed, Dodged`; `WoundKind` appended `Burn`.
- Blocks (4.8) are a pre-step inside `Resolve` (`StrikeV2.Block`); the dodge roll uses stream 0x430 (`MeleeDefence`); the parry roll stays with the melee layer. Stamina is reported x100.
- Wear per layer is in the trace (`LayerStep.WearPm`); `ClothingArmour.ApplyWear` writes it to the worn items. `ClothingArmour.Fill` turns a `ClothingBook` person's worn items (shape, material, quality, condition, fit) into the resolver's layers: clothing and armour are one list. Phase 3 `ItemQuality` maps to Qa as Crude 800, Poor 900, Common 1000, Fine 1400, Masterwork 2000.
- Falls: `ResolveFall`; `FallMomentumMilli(80 kg, 3 m) = 72,000` milli; 3 m bruises, 6 m cracks, 12 m breaks a shin (test).
- Great axe hack area **40** (melee spec said 50): a wider edge raises the bone's cut need, and at 50 the great axe could not take a neck at all. RS-12 is therefore: the axe hack heavy at skill 100 severs a bare neck; a 4 mm mail coif stops that; 8 mm of cloth (hood and padded coif) leaves the axe 102 mm of reach (no sever) and the great axe 152 (severs).
- Budgets: the 100,000-blow table resolves in about 0.25 s (about 2.5 us a blow including setup); `CombatTables` with v2 builds in about 40 ms (2,016 weapon and 714 armour entries). `BlowResult` is a reusable buffer; the per-blow path allocates nothing except the worn-piece sort (`stackalloc`).
- **Resolver v1 is kept**, unchanged in behaviour except that it skips the v2 hosted parts and maps the new structure kinds to its flags. Its callers (the Phase 0 melee spike, its Godot scene, the bench, the tests) still run on v1. `MeleeWorld.Strike` is now pluggable; `V1Adapter` runs v2 behind the v1 call shape, which is how the harness drives the spike. Nothing was removed; P4-C's `FightWorld` replaces the spike and v1.

### Calibration run (tools/Ravensguard.Calibrate, 200 duels per pairing, skills 30-59, steel; v1 in brackets)
- Duels last **13.6 to 17.5 s** mean (v1: 7.5 to 9.5 s); sword vs sword A wins 51% (48%).
- Limbs severed per duel **0.00** for trunk and limb parts (v1: 0.34 to 0.78); 0.03 to 0.07 finger or ear losses per duel.
- **Spear vs unarmoured sword 87%** (v1: 34%); spear vs mail 57%, vs plate 31%.
- Mail turns 93 to 100% of edged blows into blunt ones; rigid plate soaks **98.1 to 99.4%** of a stopped blow.
- Metal ranking, outer-piece cut rate: silver 299 < copper 329 < bronze 333 < iron 341 < steel 674 permille.
- Still off target, and not the resolver's: **defended 8 to 20%** (40-60% wanted: the spike's AI, P4-C 4.2/4.7); **fatal 22 to 53%** and **KO'd who bleed out 27 to 71%** untreated (15-35% and <= 35% wanted: there is no bandaging until P4-B; arteries are cut about 0.4 to 0.9 times per duel). The mace does badly against a sword (14 to 23% wins): it bruises where the sword cuts, and its 0.7 s wind-up is long; re-check with the P4-C timeline before touching the resolver.

## Notes (Phase 4 close-out, items 2 and 4)
- **Three physics fixes the weapon x armour matrix found** (`docs/calibration/weapon-armour-matrix.md`): the light-weapon deflection applies only to a rigid shell
  (`ArmourV2.Rigid`: metal, bone or wood, neither woven nor elastic), so a club no longer bounces off a shirt or a hauberk (RS-11's club off plate holds); an edge or point that fails a
  rigid shell's cut need skids off it by the strain rule instead of meeting the crush test at its own small area (a dagger no longer crushes a breastplate like a hammer); a piece
  with `side` `front` (the breastplate) is not met by a blow from behind, nor one with `back` (the backplate, coverage now 0.9 / 0.3 of the trunk) by a blow from the front.
  Table 4.4's row "spear against a steel breastplate over a gambeson" changes: the point fails the plate's cut need (168) and skids, next to nothing goes on (RS-8b updated; the
  other rows are unchanged). RS-24's 100,000-blow table hash is re-pinned (`12489677114851335651`).
- **Wear (4.3), close-out item 4.** A cut keeps its wear (cloth 30, leather 20, mail 10, plate 5 pm). A blunt blow on a rigid shell dents it `dent_full_pm x min(M / need, 2)` (150 at
  the crush need; `combat_calibration`), *buckled* at the need, *dented* from 40 pm; a failed edge only scuffs it (3). Mail cut through, or struck blunt at `burst_ratio_pm` (2,000)
  of its crush need, bursts its rings at the struck zone (`WornV2.BreachMask`: the piece covers `breach_coverage_pm` 300 less there). A rigid shell dented below `strap_below_pm` (300)
  breaks a strap (`WornV2.Slipped`: coverage x `slip_coverage_pm` 500). Protection falls with condition through the existing `Cm`. The trace carries it (`LayerStep.Harm`, `Zone`;
  the result hash mixes the harm when there is one); `ArmourWear.Apply` applies it (the fight, the matrix), `ClothingArmour.ApplyWear` writes the condition into a clothing book.
- No double, no new RNG draw: every rule is a threshold on the blow's momentum and the piece's numbers.


## Notes (the DF wiki re-check, Phase 4 close-out)
- The formulas are the wiki's (df-calibration.md has the 42-row comparison). Fixed: an artifact's edge multiplier Qw is 2, a masterwork's (`CombatTablesV2.EdgeQualityPm`; Qa keeps
  the artifact's 3); the materials are the DF2014 raws. The human hand and foot stay 1.4% and 2.2% (DF's are 1.44% and 2.16%: deferred, df-calibration.md row 25). RS-24's table hash is
  re-pinned (`2852731875240228929`). New goldens RS-1b and RS-25 to RS-30 carry the wiki's own numbers.
