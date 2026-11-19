# Mounted combat: the charge, the horse as a target, unhorsing, braced spears and terrain

Plan refs: §3e (mounted combat uses the same resolver: a charge adds the horse's momentum, which is what makes a couched lance dangerous; the horse is a large target; a rider whose horse falls takes fall damage and a heavy hit can unhorse him; cavalry is countered by braced spears and pikes, shield walls and terrain: mud, slopes and woods slow horses; horses trot at 5 m/s and gallop at 12 m/s at a stamina cost; injured horses go lame), §2.6 (a mounted charge adds the horse's momentum; the momentum rules), §2.7 (horses 64x64 in three views, tack layers: saddle, bridle, barding), §5 Phase 4 ("mounted combat at skirmish scale"; armies at scale are Phase 7), §4A.3 (horse anatomy in the resolver).
Code today: `Animal` (`MassKg`, `TrainedPm`, `Temper`, `StaminaU`, `Lameness`, `Shoes`, `State`: `Ridden`, `Hitched`...), `AnimalCatalog` and `content/base/animals/species.jsonc` (palfrey, packhorse, draught, ox, mule, donkey: `gait_cmps` walk 220 / trot 500 / gallop 1200 for the palfrey, `stamina_cost_sp_per_m` gallop 21), `RideBook`, `RideMath`, `Sim/Vehicles` (surface types) from Phase 3 (`docs/specs/phase3/horses-and-carts.md`, which reserves breed `courser (war, Phase 4)`), `LevelGrid` (portals: stairs, ladders), weather states.

## 1. Purpose
Make a mounted fight a different problem from a foot fight without a second rule set: the rider adds the horse's momentum to a blow, the horse is a body with its own wounds and fear, a charge can be stopped by a braced polearm or bad ground, and falling off hurts. Scale: a skirmish of up to about 30 mounted fighters inside the live bubble; mounted armies, wing orders and formations are Phase 7.

## 2. Dependencies
| Needs | From |
|---|---|
| Horse and animal anatomy, `WoundBook` for animals, panic instead of pain | `anatomy-and-body-plans.md` 4.1, `wounds-bleeding-pain-healing.md` |
| `ResolveImpact` with `ExtraMomentumMilli`, falls, blocks | `resolver-v2.md` 4.1, 4.8, 4.9 |
| Timings, stamina, shield wall, brace state | `melee-and-ranged.md` |
| `Animal`, `RideBook`, `RideMath`, surfaces (road, field, mud, snow, ice), lameness | exists (`Sim/Animals`, `Sim/Vehicles`) |
| Weather (mud, ice, thunder), `WeatherSample` | exists (`Sim/Weather`) |
| `Riding` skill, `Athletics` | exists (`People.Skill`) |
New code: `Sim/Combat/Mounted/` (`MountedFighter`, `ChargeMath`, `Unhorse`, `HorseNerves`, `BraceReaction`, `TerrainFooting`), content `animals/courser.jsonc`, `items/lance.jsonc`, `items/barding.jsonc`.

## 3. Data

### 3.1 Species and tack
- **Breed `courser`** (war horse; new row in `animals/species.jsonc`): `mass_kg` 480, `carry_kg` 150, `pull_n` 560, `gait_cmps` walk 230 / trot 520 / gallop 1250, `stamina_sp` 10000, `price_mites` 300000 (3,000 crowns, a seed for the price solver), `trained_pm` 900 (war-trained), `temper` 10 (steady), `body_plan` `horse`. A palfrey (`TrainedPm` 700) can be ridden into a fight but flinches (refusal and panic formulas below).
- **Barding** (`armour_shape` rows with a `body: "horse"` coverage on horse part ids): `horse_blanket` (padded, over, thickness 6 mm), `horse_mail` (armour, iron, 4 mm, coverage neck 0.8, chest 0.9, belly 0.5, rump 0.8), `horse_chanfron` (steel head plate 2 mm, head 0.9), `horse_plate` (rare). Layers and gaps work as for people; barding mass slows the horse (`carry_kg` is reduced by `kg x 1`; mail barding 18 kg).
- **Lance** (`weapon_shape` `lance`): head 38 cm3 steel, haft ash 4,400 cm3 (mass 3.3 kg), `two_handed: false` (couched in one arm), `reach_mm` 3600, attacks `couch` (point, area 10, penetration 600 mm, velocity 1.3), `bash` (blunt haft). Mounted only (on foot: unwieldy -30 points, wind-up x1.5). **Shatter:** a lance that deals a blow with `M_final >= 150` breaks with 60% (stream 0x470, draw 3), leaving a spear-length stub (a `spear` shaft). Tourney lance (`coronal`, blunt, x0.5 momentum) for tournaments.
- Mount fields for combat (`MountedFighter`, 40 bytes): `Rider` (person), `Mount` (`Animal` id), `Speed` (mm/s), `Charging` (bool), `CouchedLance` (bool), `PanicPm` (0..1000), `GripPm` (rider hold), `PinnedTicks`.

### 3.2 Constants (`combat/mounted.jsonc`)
```jsonc
{ "charge_k_milli_per_kgmps": 10,           // momentum (milli) per kg x m/s of closing speed
  "charge_weapon_pm": { "lance_couched": 1000, "polearm_overhand": 600, "one_hand": 350, "thrown": 0, "bow": 0 },
  "gallop_charge_min_mps": 9, "charge_max_mps": 12.5, "trample_k": 8, "brace_return_pm": 800,
  "unhorse": { "hold_base": 100, "hold_per_riding": 1.0, "k_pct_per_m": 0.8, "max_pct": 80, "hook_mult": 2.0 },
  "panic": { "decay_pm_per_s": 30, "cut": 80, "torn": 150, "artery": 300, "leg_broken": 600, "fire_per_s": 200, "thunder": 100, "bolts_at": 900, "rears_at": 600 },
  "refusal": { "base_pm": 200, "per_braced_polearm_pm": 150, "trained_pm_k": 2, "riding_k": 2, "max_pm": 950 } }
```

## 4. Rules

### 4.1 The charge: momentum
- **Closing speed** `v_c` (m/s) = the horse's speed projected on the direction to the target at the Strike tick (dot product with `FaceQ10`), rounded down to 0.1; **a charge is `v_c >= 9 m/s`** (gallop). A trot (5 m/s) gives a half-strength run-up; the formula is continuous.
- **Extra momentum** `M_extra_milli = charge_k x (mass_horse + mass_rider + gear) kg x v_c x weapon_pm/1000`. Courser 480 kg + rider 80 kg + gear 20 kg = 580 kg, `v_c` = 12 m/s: `10 x 580 x 12 = 69,600` milli (69.6) for a couched lance (weapon_pm 1000); 24,360 for a sword (350); 41,760 for an overhand polearm (600).
- Resolver input: `ExtraMomentumMilli` added after the skill and style multipliers (plan: "a mounted charge adds the horse's momentum"). Worked blow, skill 50 rider, couched lance (M0 = 111.4 x 1.5 = 167.1): **M = 167.1 + 69.6 = 236.7**. Against iron mail (area 10, cut need 59.9) and gambeson then ribs: cuts all; against a steel breastplate (cut need 144, 2.5 mm): **cuts** (conservation leaves 144 - 0.6 x 144... M out = min(0.95 x 236.7, 236.7 - 86.4) = 150.3), then ribs: pierces. Against a heater shield (capacity 200): **blocked, 36.7 passes to the forearm** and the shield loses 236.7/700 = 34%. A tower shield (300) stops it. A shield wall of 3 braced (capacity +20% = 240 for heater) stops it with a hairline margin (236.7 < 240): **a braced wall of good shields holds a single charge** (MT-3).
- Moving a charge consumes the horse's stamina: gallop 21 sp per metre (palfrey). A 150 m charge costs 3,150 of 10,000 sp; a winded horse (`Winded`) cannot gallop: it drops to a trot.
- **Accuracy while mounted** (the melee layer's hit chance): -5 points for a committed charge in a straight line, -10 at a trot, -20 swinging a sword at a gallop; the rider's `Riding` skill subtracts: `penalty x (1 - Riding/150)`.
- **Reach:** the rider's sword arm is 0.7 m higher; reach vs foot targets `+0.4 m` for non-lance weapons; vs a mounted opponent `+0`.
- **Trample:** the horse itself is a `Fighter` with a `trample` attack (blunt, area 80 cm2, `M = trample_k x mass_kg x v_c` milli, e.g. 480 x 10 x 8 = 38,400 at gallop) against a standing or prone person it overruns; `Prone` doubles (resolver). A target that dodges (Ctrl) takes `P_hit x 0.4`. A tight shield wall or braced pikes are **not** trampled: the horse refuses (4.5).
- **Turning:** a horse turns at `60 degrees/s` at a gallop, `150` at a trot, `270` at a walk: charges are committed lines, and a miss means the rider overshoots 15 m before turning (spike lesson on instant turning removed).

### 4.2 The horse as a target
- It is a large target: against the chest the hit factor is 1.0, head 0.40 (anatomy 4.1); +10 points of skill delta from the front. Attackers choose **rider or mount** in the called-shot (T) list: the mount's parts appear as a group after the rider's. Unaimed blows at a mounted target land on the **rider** 60% / **horse** 40% (the rider's lower body is shielded by the horse on the near side: rider lower-body zones have hit chance x0.75 on the near side, x1.0 on the off side).
- **Wounds and fear.** A horse has no pain pool: wounds add **panic** (`PanicPm`: hide cut +80, torn muscle +150, artery +300, leg bone broken +600, a blow stopped by barding +20, fire +200 per second near it, thunder +100; modified `x (1 + temper/200)` and `x 0.6` when `TrainedPm >= 900`; decays 30 pm per second). At **600**: rears and bucks (each second the rider is thrown with `P = clamp(40 - Riding/3, 5, 40)` percent, so a skill-50 rider is thrown at 23% per second of rearing); at **900**: bolts, uncontrolled, away from the nearest threat at gallop for up to 10 s.
- **Falls.** A horse falls at once if a leg bone is `Broken` or worse, a lower-leg tendon is cut (`Lameness` 3), or it collapses from blood loss (30% of its blood: 9,072 ml for a 480 kg courser). An artery wound (200-300 ml/s with `bleed_scale_pm` 2500) collapses it in **30-45 s**; a lung or heart wound in 10-40 s with panic. Horse death: heart, brain destroyed, or 50% blood loss (50-76 s). A killed or fallen horse stays on its side (a 2 m x 1 m obstacle) and can be butchered later.
- **Barding** changes the numbers: mail barding on the chest adds a cut need of `(rSY + (A+1) rSF) x 12 = 71.5` for a spear to pass (as for a person's mail) before the hide.

### 4.3 Unhorsing
When a blow lands on a rider (after armour, trace `M_in` at the part it hit) or is blocked with `M_in` in the range, roll `Unhorse`:
`U% = clamp((M_in - hold) x 0.8, 0, 80)`, `hold = 100 + Riding x 1.0` (skill 50 = 150; the stirrup and saddle). A blow with a **hook** (polearm `hook` attack: billhook, halberd; rows with `hook: true`) uses `x2.0`. Examples: sword skill 50 (156.5) vs Riding 50: 5.2%; heavy axe skill 100 (311.9): 80%; the lance charge (236.7): 69.4%. KO or paralysed riders fall automatically; a rider with a broken pelvis or legs gone is thrown (Walk 0: cannot grip, `GripPm` 0).
- **Fall damage** (resolver 4.9): `ExtraMomentumMilli = 100 x mass_kg x (3 x 1.8 + 0.5 x v)`: 80 kg at a gallop of 12 m/s = **91,200**; stationary 43,200; landing part by weights (arms 30%, upper body 20%, legs 25%, head 10%, hand 15%; stream 0x472). A helmet and padding absorb as for any blunt blow. A **horse falling with the rider on top** pins a leg 60% of the time (`PinnedTicks` 80 = 4 s to struggle free, or an ally pulls the horse's weight with Athletics) and crushes the pinned leg with `M = 40,000` milli blunt.
- Remounting: E near a loose horse (from Phase 3), takes 3 s and cannot be done in melee reach of an enemy (interrupted by any blow).

### 4.4 Braced spears and pikes against horses
- **Brace:** a spear, pike or javelin bearer who stands still (moved < 0.5 m in 1 s) with RMB held in combat stance **braces** (planting the butt): state `Bracing`, wind-up and recover are skipped for the automatic reaction (4.4), the bearer cannot attack otherwise, `dodge` disabled. Brace is cancelled by a hit taken or by moving.
- **Reaction strike:** when a mount whose `v_c >= 4 m/s` enters the polearm's `reach_mm` in the bearer's front arc, the bearer makes one automatic strike at the horse chest (or the horse's lowest unarmoured area) with `M = M0 x skill + brace_return x charge_k x (horse + rider kg) x v_c x 0.8` (the horse impales itself). Spear skill 50 (M0 160.3), courser charging 12 m/s: `160.3 + 0.8 x 69.6 = 216.0`. A pike's longer reach reaches the horse 4.5 m out so it strikes before the lance (3.6 m): **pikes beat lances**. Hit chance as for the body zone x1.0 (large target) +10 points (braced), x1.25 if the horse comes from the flank.
- **Result on the horse:** a chest spear of M 216 cuts the hide, fat, muscle and ribs (need 77.4) and rolls lung (24% each) and heart (10%); panic +300; a fall or bolt follows within 1-2 s. A dead or falling horse carries on `v_c x 0.5` for about 5 m (fall damage to the rider as 4.3, 3 m at speed).
- **Refusal:** each approaching horse rolls at 8 m from the nearest braced polearm in its path: `P_refuse = clamp(200 + 150 x n_braced_polearms_in_6m_arc - 2 x (TrainedPm - 700) - 2 x Riding, 0, 950) pm` (stream 0x471). Courser (900) ridden at skill 50 against 3 braced spears: `200 + 450 - 400 - 100 = 150 pm = 15%`; 5 pikes: 450 pm; a palfrey (700) against 5: 850 pm; an untrained rider-less horse 95%. A refusing horse stops at 4 m (rider thrown on 20% of refusals at gallop). The player sees "the horse balks" in the log.
- **Shield walls** stop a charge with the push rule (melee 4.4) and a block capacity bonus; a charge that is blocked still costs the shield 34% condition.
- **Counters list** (legibility, shown in the tactics tooltip): braced pikes, shield walls, mud and slopes, woods, caltrop-like ditches (Phase 7), dismounting the rider with a hook.

### 4.5 Terrain and footing (`TerrainFooting`)
Per tile surface (existing surface classes) and slope:
| Surface | Gallop speed x | Charge allowed | Notes |
|---|---|---|---|
| Road / hard | 1.00 | yes | |
| Field / grass | 0.85 | yes | |
| Rough ground / rubble (ruins, old battlefields) | 0.60 | no above 8 m/s | stumble 2% per sim-second at a gallop (stream 0x473), the horse falls |
| Mud (after rain, Phase 3 mud) | 0.50 | **no** (max trot 5 m/s) | `v_c < 9` so no charge; the horse stops charging at the first mud tile |
| Snow | 0.60 | no above 8 m/s | |
| Ice | 0.40 | no | slip 8% per second at a gallop: fall |
| Woods (tree cover >= 30) | 0.50 | no | branches: a rider hit by overhead branch for a gallop (M 40) 1 per 20 s |
| Shallow ford (<= 0.6 m) | 0.50 | no | |
| Deep water (> 0.8 m) | swim 1.0 m/s | no | rider's armour > 10 kg sinks; drown rule (exploration 4.4) |
| Stairs, ladders, trapdoors, cave mouths | forbidden | | horses do not take portals (level portals are infantry only); underground sites forbid mounts |
Slope (rise over run): uphill `v x (1 - 3 x slope)` (clamp 0.3), downhill `v x (1 + 1.5 x slope)` (cap +20%); a **downhill charge** adds `+10%` to `v_c`. Weather: heavy rain: field x0.8, grip-related slip rates x1.5; thunder: horses `+100` panic, thunderstorm during a charge doubles refusal odds; fog: cavalry loses sight, charges only on seen targets.

### 4.6 Cavalry AI (`CavalryBrain`, same combat brain with extra states; also for the hero in watch mode)
1. **Charge** when the lane to a target is clear for 30 m, `v_c` reachable, surface allows, and no braced polearm line is within the path (`refusal` estimated < 40%). 2. After contact, **disengage** (hit and run): turn at a wide radius to re-form 40 m away, unless pinned. 3. **Avoid** shield walls and braced lines; flank them. 4. **Retreat** when the horse's panic >= 500 or blood loss >= 20%. 5. Dismount on request (E) when the horse is dying, or to fight in woods, indoors or underground. The AI uses the same utility inputs (existing `Flee`, new `Fight`).

### 4.7 Worked example: one charge, tick by tick
A courser (480 kg) with a skill-50 rider (80 kg, 20 kg gear) and a couched lance charges two spearmen in gambeson over 60 m of dry field; the spearmen are not braced.
| Time (s) | Event |
|---|---|
| 0.0 | Rider presses Shift (gallop). Assumed acceleration 7.5 m/s2 (a content constant in `mounted.jsonc`): 9 m/s after 1.2 s (5.4 m), 12 m/s after 1.6 s (9.6 m) |
| 1.2 | `v_c` reaches 9 m/s: the charge meter fills (`M_extra` 52,200 milli at that speed, 69,600 at 12 m/s); stamina costs 21 sp per metre |
| 5.5 | At 3.6 m (lance reach) with `v_c` 12 m/s the strike starts (LMB or the AI); a couched lance needs no wind-up (the horse is the wind-up) and the blow lands at the Strike tick |
| 5.55 | Resolver: M0 167.1 + 69.6 = 236.7 on a spearman (gambeson, chest): cut, ribs cut, heart rolled at 10%, lungs at 24% each; the lance shatters with 60% (M_final >= 150) |
| 5.6 | The horse continues into the second spearman: trample 38,400 milli blunt on `upper_body`; the spearman may dodge (hit chance x0.4) |
| 6 to 9.5 | Rider overshoots about 15 m (turning 60 degrees per second at a gallop), turns wide, re-forms at 40 m. The charge cost about `(56 + 15) m x 21 = 1,490 sp`, leaving 8,510 of 10,000 |
Everything above is the resolver, the melee timeline and `ChargeMath`: no mounted-only damage rule exists.

### 4.8 Tournaments and rider against rider
A **tourney lance** (blunt coronal head, momentum x0.5) and a blunted sword are used in tournaments (plan §4A.3: yield at pain knock-out; the purse comes from the town treasury); the same charge formula applies at half weapon strength. In a real lance-on-lance charge each rider's `M_extra` uses **the closing speed of both** (sum of the two speeds along the line, capped at 20 m/s) because the opponent runs onto the point: two couched riders at 12 m/s each hit with `10 x 580 x 20 = 116,000` extra milli. Two lances meeting on shields (capacity 200) each pass 100+ milli to the arm: the first to be unhorsed (4.3) loses. The AI approaches at an angle (a jousting "tilt" 8 degrees) to avoid head-on exchanges unless it is a tournament.

## 5. Tick placement (20 Hz live versus abstract)
- Mounted fighters step in the live fight with the rider's `Fighter` and the horse's `Fighter` (two entities, one position); `ChargeMath` runs at the rider's strike tick, `Unhorse` on a landed blow, `HorseNerves` every 10th tick (2 Hz) for panic decay. No change to the 20 Hz timeline.
- Abstract: the hourly tick treats mounted soldiers as units with a `charge_pm` multiplier in the abstract battle (abstract-battles-v1.md 4.4: a one-time first-contact bonus of +60% casualty rate to the charged unit if it is not braced or on mud); live mounted combat is not run outside the bubble. Travel's `RideMath` is untouched.

## 6. Determinism
Streams (0x400 block, key `FightTick`, entity = attacker or horse): 0x470 `UnhorseRoll` (draw 1 unhorse, draw 2 pin chance, draw 3 lance shatter), 0x471 `HorsePanic` (refusal, rear, bolt direction), 0x472 `FallPlacement`, 0x473 `TerrainSlip`. All arithmetic integer (momentum milli, panic pm, speeds mm/s). `MountedHash` joins the trail. Charge `v_c` is read from the integer position delta of the last 4 ticks, not from a float velocity.

## 7. UI and controls
- Mounted layer from Phase 3 (World + Mounted): **Shift** gallop (the `sprint` action), **O** walk toggle, **E** dismount (and mount), Z does nothing while mounted. **Combat stance (X)** and **LMB/RMB** work as on foot: LMB attacks (a lance is couched automatically when galloping at a target), **RMB** holds a shield block or, with a polearm on foot, **braces**. **Ctrl + direction** is a horse side-step (0.5 s, 1.5 s cooldown, 12 stamina of the horse). **T** (called shot) lists rider parts then the mount's parts. No new keys.
- HUD: a **charge meter** (momentum build-up: empty at trot, full at 12 m/s, with the added M value), the horse's stamina (existing), a **mount silhouette** (the horse's `BodyDiagram` with wounds and a panic bar), the rider's grip. Tooltips: "unhorse chance 5%" on a hovered attacker's weapon.
- Feedback: hit flash on the horse, a hoof-thunder placeholder sound loop rising with speed, slow motion on a lance strike (accessibility toggled, feedback file).
- Warnings: before a charge into a braced line the HUD shows "this charge will probably stop: refusal 45%" (computed with the `refusal` formula).

## 8. Tests (golden)
| Id | Check |
|---|---|
| MT-1 | `M_extra` for 580 kg at 12 m/s: lance 69,600; sword 24,360; polearm 41,760; at 9 m/s 52,200; at 5 m/s 29,000 |
| MT-2 | Couched lance, skill 50: M 236.7; cuts iron mail, cuts a steel breastplate (cut need 144), passes 36.7 to the forearm through a heater (capacity 200) |
| MT-3 | The middle heater of 3 braced shields (2 braced neighbours, capacity 200 x 1.2 = 240) stops one charge (236.7); in a wall of 2 each has 1 neighbour (220) and 16.7 passes |
| MT-4 | Unhorse: sword s50 vs Riding 50 = 5.2%; heavy axe s100 = 80%; hooked polearm x2 (10,000 seeds +-1.5) |
| MT-5 | Fall: 80 kg at 12 m/s = 91,200; stationary = 43,200; falls 3 m bruise (resolver) |
| MT-6 | Brace reaction: braced spear s50 vs courser charging at 12 m/s: M 216.0; pike strikes first (4.5 m vs 3.6 m); a chest strike that cuts the ribs hits a lung or the heart in about 48% of cases (`1 - 0.76 x 0.76 x 0.90`; 10,000 seeds +-1.5) |
| MT-7 | Refusal: courser (900), Riding 50 vs 3 braced spears 15%; 5 pikes 45%; palfrey vs 5 pikes 85% |
| MT-8 | Horse falls at once with a broken leg or cut lower-leg tendon; artery wound: collapse in 30-45 s |
| MT-9 | Terrain: mud forbids charges (max 5 m/s); ice slip 8%/s at gallop (10,000 seeds); woods forbid gallop; stairs and ladders refuse mounts |
| MT-10 | Panic: wound +80 +150 on a temper +10, `TrainedPm` 900 horse = (80+150) x 1.05 x 0.6 = 145; decays 30 pm/s |
| MT-11 | Cavalry AI: does not charge into a braced line with refusal estimate >= 40%; flanks instead (skirmish sample of 20 seeds) |
| MT-12 | A 12 mounted vs 20 foot skirmish (20 seeds): charge casualty rate within 60% +- 10% of the abstract `charge_pm` bonus; abstract and tactical casualties within +-15% (abstract spec 8) |
| MT-13 | Determinism: a fixed mounted skirmish gives the same `MountedHash` on both OS |

## 9. Open questions (recommended defaults)
| # | Question | Default |
|---|---|---|
| H1 | Is the lance too strong against plate? | Keep: a couched charge defeats a single 2.5 mm plate by design; quality 3.0 (masterwork) plate stops a skill-50 lance by raising the need to 192 +- (so legibility holds); tune with `charge_k` |
| H2 | Should horse flesh take the human "bone is a stop" rules? | Yes, same tissue multipliers (bone 2.5 and 5.0) |
| H3 | Stirrup drag when a KO'd rider falls with a foot caught | Out of scope (listed, not built) |
| H4 | War-horse price and supply | 3,000 crowns seed price, 1-2 per large town; horses are rare targets for theft |
| H5 | Can the player stay mounted in forests at all? | Yes at a trot; gallop and charge forbidden |
| H6 | Rider AI skill: do bandits ride? | Rare (leaders only, 10%); armies' detachments have 10-20% cavalry |
| H7 | Can horses kick or bite (dismounted fights)? | Riderless horses panic and flee; trained war-horses kick (blunt, area 60, velocity 1.0) at adjacent enemies only when ridden |

## 10. Budgets
| Item | Budget |
|---|---|
| `ChargeMath` | <= 0.2 us per blow (integer multiply) |
| Mounted fighters per bubble | <= 60 (30 riders + 30 mounts), <= 0.15 ms per tick |
| `HorseNerves` at 2 Hz | <= 0.1 us per horse |
| Save | `MountedFighter` 40 bytes only in a saved fight; horse wound books as people |
| Content | `courser`, 4 barding rows, `lance`, `tourney_lance`, `mounted.jsonc` <= 8 KB |
