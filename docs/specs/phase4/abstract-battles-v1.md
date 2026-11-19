# Abstract battles v1: the Lanchester exchange with per-soldier wound sampling, and equivalence to the tactical results

Plan refs: §4A.3 (abstract battles: "a Lanchester exchange whose quality factor comes from armour and skill. The output is per-soldier wounds, knock-outs, routs and captures, sampled from tables built by the tactical resolver. A golden test keeps abstract and tactical results within +-10%"; v1 bugs it replaces: "abstract battles reduce a single HP number and leave bodies untouched", "the commander is immortal", "prisoner ransom is paid from nowhere"), §2.3 (the abstract tier runs every game hour, "plus an abstract battle resolver for fights outside any bubble. Those battles are labelled as reports in the chronicle ('word reached Oakford...'), with casualties drawn from a statistical wound table"; the battle bubble), §2.6 ("outcomes never depend on where the camera is"; near and far soldiers statistically equivalent), §5 Phase 4 ("the abstract battle resolver v1; combat cause chains"), §6 (combat tests: the abstract resolver within +-10% of tactical), §3.12 (chronicle entries have a concrete cause or "market drift"; cause chains for battles and wounds).
Code today: nothing for battles beyond the hooks (`CaravanLoss` in the Phase 2 `EconCommand` set, `docs/specs/abstract-tick-and-lodbridge.md` 3.8 "abstract battles elsewhere are chronicle reports", `Sim/Abstract/RegionScheduler`, `WorldTicker`, `Chronicle`, history battles in `WorldGen/History`, `ActiveWarMarker`). Phase 7 extends this to armies at scale; v1 handles **skirmish scale up to about 200 per side** (raids, patrols, bandit clashes, war-marker skirmishes, ambushed caravans outside the bubble).

## 1. Purpose
Resolve fights nobody is watching, with the same physics the player sees: every outcome is drawn from tables **built by running the real resolver** (`tools/calibrate`), so an abstract soldier is wounded the way a live one is (a cut artery, a broken forearm), not by a hit-point number. A golden test keeps the two within 10%.

## 2. Dependencies
| Needs | From |
|---|---|
| The tactical resolver and melee layer (to build tables) | `resolver-v2.md`, `melee-and-ranged.md`, `tools/calibrate` |
| Wound records and bleeding, healing, infection | `wounds-bleeding-pain-healing.md` (abstract form) |
| Morale thresholds, outcomes by winner, ransom | `morale-capture-succession.md` |
| Mounted charge bonus | `mounted-combat.md` 5 |
| Persons, skills, equipment, `Followers`, factions, treasuries | exists (`PersonTable`, `HeroParty`, Phase 2 `taxes-and-treasuries.md`) |
| `EconCommand` queue and barrier, `Ledger` | exists (Phase 2) |
| `Chronicle`, `Cause`, rumours outbox | exists (`Sim/Core/Chronicle.cs`, `RegionOutbox`) |
| `RegionScheduler` hourly tick, battle bubble rules | exists (`Sim/Abstract`) |
New code: `Sim/Abstract/Battles/` (`BattleSide`, `AbstractBattle`, `LanchesterStep`, `WoundTables`, `WoundSampler`, `BattleReport`, `BattleCommands`), `tools/calibrate` (table builder + equivalence runner), content `content/base/abstract/wound_tables.json` (generated, hash-checked), `abstract/battle.jsonc` (constants).

## 3. Data

### 3.1 Classes (small integers so tables stay small)
- **Weapon class** (9): `blade` (sword, falchion), `dagger`, `axe`, `hafted` (mace, hammer, club, maul, flail), `polearm` (spear, pike, javelin melee), `longbow`, `crossbow`, `thrown`, `unarmed`. Bows and crossbows have range bands.
- **Armour class** (7): `none`, `cloth` (tunic), `padded` (gambeson, jack), `leather_mail_light` (brigandine, leather + mail coif), `mail` (hauberk over padding), `mail_plate`, `plate`. Derived per soldier from worn layers (the resolver's tables: the class is the nearest by the **protection index** `ProtectionPm`, the average over body zones weighted by area of the momentum needed to cut or crush through, normalised so `none` = 0 and `plate` = 1000). The protection index is computed once per `(armour set, material)` in `CombatTables`.
- **Skill delta band** (7): the attacker's skill minus the defender's, in steps of 20 from -60 to +60 (linear interpolation between bands; the absolute skill level enters through the hit chance only). Shield bearer flag (x2 layers of table rows: `shield` adds a block factor row), mounted flag (charge bonus), height flag.

### 3.2 Wound tables (`wound_tables.json`, generated; 9 weapon classes x 7 armour classes x 7 skill-delta bands x 2 shield flags = 882 rows, about 10 permille numbers each)
Per `(weapon class, armour class, skill delta band (7: -60..+60 in steps of 20), shield flag)`, produced from **4,000 resolved blows** against the human plan using the real `ResolverV2` and the melee defence rolls (block, parry, dodge by skill): the distribution of the **outcome of one attack action** (a swing or a shot):
| Field | Meaning |
|---|---|
| `p_hit_pm` | attack lands (after dodge, parry, block) |
| `p_nothing_pm` | landed but only bruised or no wound (armour held) |
| `p_light_pm` | light wounds (skin, fat, bruised muscle): pain <= 10, bleed < 2 ml/s |
| `p_serious_pm` | serious: torn or mangled muscle, cracked or broken bone, bleed 2-30 ml/s: pain 20-60 |
| `p_grave_pm` | grave: artery or vein, organ, displaced or shattered bone, nerve or spine, bleed > 30 ml/s or pain > 60 |
| `p_severed_pm` | a limb or head taken |
| `p_instant_pm` | immediately incapacitating (heart, brain, head crush): dead or down within 10 sim-s |
| `mean_pain`, `mean_bleed_x10` | for light and serious (for the abstract mini-simulation) |
| `limb_mix` | 6 numbers: shares of arm, leg, torso, head, neck, hand among wounds (for the permanent-damage sample) |
| `fx_mix` | shares of fracture, nerve/tendon/ligament, organ, artery in grave wounds |
Tables are keyed by a **content hash** (weapon shapes, armour shapes, materials, body plan, calibration constants). CI regenerates and compares (AB-1); a stale table is a test failure and a build error.

### 3.3 Battle records
```
BattleSide { Faction, Soldiers[] (person id, weapon class, armour class, skill, blood, pain, status), Leader (person), Banner?, Archers[], MountedCount, Morale (x100), Position (front, flank, rear), Supply (arrows) }
AbstractBattle { Id, Place (settlement, site or coordinate), Terrain (surface, slope, wood, water), Weather (state), StartHour, Sides[2], Cause (EntityId: war, raid, encounter, ambush), Steps, State }
BattleReport { Id, Place, Hour, Winner, Strengths, Casualties (dead, down, captured, fled), Notables[] (person, fate, cause chain), Duration (s), Cause, Text id }
BattleCommands { ApplyWounds(person, WoundBook delta), ApplyStatus(person, Dead|Captured|Fled|Down), Loot(items), Ransom(...), Chronicle(report) }  // applied at the tick barrier in stable id order
```
Constants (`abstract/battle.jsonc`): `step_s` 15, `max_steps` 80 (20 sim-min), `blows_per_min` 16 (plan: 40% in contact x one swing per 1.5 s = 0.265 per second), `engagement_pm` (fraction of the smaller side in contact: 600 line, 800 narrow, 400 loose), `outnumber_cap` 2.5 (attackers per defender), `flank_bonus_pm` 250, `rear_bonus_pm` 500, `morale_k`, `rout_below` 20, `collapse_active_pm` 300 (a side with < 30% of its start strength active breaks), `charge_pm` 600 (first contact), `pursuit_s` 120.

## 4. Rules

### 4.1 One battle
1. **Setup:** build `BattleSide`s from the persons involved (their `PersonTable` rows: skills, equipment via Phase 3 inventory, health from `WoundBook`s); class each soldier (3.1); fill `Archers`; terrain and weather multipliers (4.5).
2. **Loop** per step of 15 sim-s (`dt` = 15), until an end condition:
   a. **Engagement:** `E_A = min(Active_A, engagement_pm/1000 x Active_B x 2.5)`, same for B (fewer attackers when the other side is thin; frontage caps in narrow terrain).
   b. **Blows:** attackers `E_A` swing `blows_per_min x dt/60` times each (16 per minute = 4 per step), plus flank/rear bonus: `blows x (1000 + flank_bonus_pm)/1000` for the flanked side's defenders. A first-contact **charge** step adds `charge_pm` of casualties for a mounted attacker when the defender is neither braced nor on mud (mounted-combat.md 4.4, 4.5).
   c. **Shots:** each archer fires `60/ reload` shots per minute (longbow 12, crossbow 6) at a **range band** (the distance between lines; 8 bands) with the table row (`longbow`, target armour, skill, range) in place of melee; arrows are limited by `Supply` (24 per archer).
   d. **Distribution:** the `B_hits` to side B (the table's `p_hit` already included) are spread over active defenders: **each defender draws `Binomial(n_blows, 1/Active_B)` exactly** by sequential integer draws (stream 0x4A1, key `(battle, step, soldier)`) so soldiers are hit unevenly (front ranks first: the weights `front 1.5, middle 1.0, rear 0.4` by row).
   e. **Sample outcomes per hit:** one draw selects the outcome class from the soldier's `(weapon class of the hitter, own armour class)` row using integer cumulative permille; the class is then expanded by `limb_mix`/`fx_mix` draws into a **concrete wound record** (part, tissue, kind, grade) copied from a **template library** of recorded real wounds of that class (the calibration run stores up to 64 sample wound sets per class: so a "grave" wound can be a femoral artery or a heart wound, with its true bleed rate and pain). The soldier's `WoundBook` is updated (wounds file) and pain and blood advance by the **closed-form** bleeding and pain step for the remaining battle time (clot integral, wounds file 4.2).
   f. **Status update:** pain over the knock-out threshold, blood loss >= 40%, instant outcomes, or heart: **Down**; blood loss >= 50% or instant fatal: **Dead** (bleeding-out soldiers die later at the aftermath step if untreated); severed limb: **Down** if pain over threshold else continues one-armed (skill x0.5).
   g. **Morale** (unit level, per soldier simplified): `morale = courage - penalty - 0.35 pain - blood loss%` with `penalty` accumulating `6` per casualty observed per 10 soldiers of the side per step (the live rule integrated), leader dead `-20`, flanked `-15`, captain/banner bonuses; soldiers below 20 **rout** (leave the line, `Fled`) with probability by `Binomial` draws (stream 0x4A2); a side below `collapse_active_pm` of its start strength or without a leader and under 40% breaks at once.
3. **End:** one side broken or eliminated, or 80 steps; ties are declared a **stalemate** (both withdraw).
4. **Pursuit:** the winner's cavalry/fast units pursue routers for `pursuit_s` (120 s): routers take blows at rear factor `+500 pm` and a `charge` bonus if mounted pursuers; the rest escape.
5. **Aftermath:** the loser's **Down and Surrendered** soldiers get the outcome table (morale-capture-succession.md 4.4): army winners take prisoners (ransom rows), bandit winners rob; dead soldiers drop items for looting (a later job); wounded survivors are placed back as persons with `WoundBook`s (so the hourly abstract tick continues bleeding, first aid, infection): an `AbstractFirstAid` roll per wounded soldier by the **winner's and the loser's medics** (morale/wound spec 5).
6. **Report and commands:** `BattleReport`, chronicle entries (4.6), commands applied at the barrier. The report is dated at the hour of the tick and **labelled as a report**.

### 4.2 The Lanchester form and the quality factor
Within a step, the exchange is the square law with measured coefficients: `dA/dt = -beta_BA x E_B`, `dB/dt = -beta_AB x E_A`, with `beta_XY = blows_per_s x p_out(weapon class of X, armour class of Y, skill delta, shield)` where `p_out = p_hit x (p_serious + p_grave + p_severed + p_instant) x (probability that such a wound takes the soldier out of the battle within the step)`: the last factor comes from the sampled wounds (a serious wound with pain 50 does not take a man out alone; two do). We **sample** instead of integrating the coefficients, so deaths and downs are exact counts; the Lanchester coefficient is used for (a) **odds estimates** (`RiskEstimate` in watch-mode.md 3.3) and (b) **sanity checking** the samples (test AB-5 compares the mean sampled rate to `beta` within 5%). The "quality factor" is the ratio `beta_AB / beta_BA` and is exposed in the report ("the raiders' quality factor was 1.7").

### 4.3 Calibration against the tactical resolver
`tools/calibrate` builds the tables (4,000 blows per cell; an hour on the build machine; incremental by cell) and **fits three constants** by minimising the mismatch with tactical fights: `engagement_pm`, `morale_k`, `outnumber_cap`. Fit set: **2,000 tactical fights** (live `FightWorld` headless) over 40 scenarios (militia vs bandits, men-at-arms vs levy, archers screened by spears, mounted charge, ambush at night). Then the **equivalence tests** (section 8) run on **fresh seeds**. Report: `docs/calibration/abstract-vs-tactical.md` (tables of both).

### 4.4 Equivalence definition
Across `N` = 200 seeds per scenario, abstract vs tactical agree when: mean casualties (dead + down) per side within **+-10%**; win rate within **+-10 points**; mean duration within **+-25%**; per-casualty wound class mix (light/serious/grave/severed shares) within **+-10% relative**; deaths after bleed-out (with the same medic rules) within +-10%. Scenarios: A (20 militia gambeson, spear and club, skill 30 vs 20 bandits leather, sword and axe, skill 40), B (30 men-at-arms mail+padding, sword and heater, skill 60 vs 50 levy gambeson, spear, skill 30), C (20 longbowmen + 10 spears vs 25 raiders charging, 60 m open ground). A scenario D adds a mounted charge on mud (charge bonus must be 0).

### 4.5 Terrain, weather and time
Multipliers from the same tables as live: narrow ground (`engagement_pm` 800 with `outnumber_cap` 1.5), woods (cavalry bonus 0, archery range -40%), mud (charge 0, `blows_per_min` x0.9 from footing), rain (bow accuracy x0.85, `blows` x1), night or underground (blows x0.8, rout +10), slopes (uphill defender x1.1 on `p_hit`). Weather values read from `WeatherSample` at the battle hour.

### 4.6 Combat cause chains and the chronicle
Every report carries a `Cause`: the war event, raid, ambush encounter or bandit job that brought the sides together (`CauseKind` from Phase 2). Each **named** casualty (lords, captains, followers, notable persons) gets a `Cause(CauseKind.Wound ...)` entry: "killed by an arrow at the fight at X, which began when ..." with links: wound class, the opponent weapon class, the battle, the cause event. Unnamed soldiers are aggregated ("14 of the Wolf Company fell"). Chronicle entries are generated with templates (`CHRON_BATTLE_REPORT`: "word reached {settlement} that {A} and {B} fought at {place}; {A} lost {n}, {B} lost {m}"); rumours seed from reports through the existing `RegionOutbox`. The chain is the same type used by the live after-fight report (feedback file 4.3).

### 4.7 Live to abstract and back (conservation)
- **Demotion** of a live fight (the bubble moves away): the battle converts to an `AbstractBattle` from the live state: current blood, pain, wounds, status and ammunition per soldier, with step 0 at that moment; the abstract result continues the fight; **no soldier's state is lost or duplicated** (test AB-8).
- **Promotion** (the player arrives): an active abstract battle is exported to a live `FightRecord`: soldiers placed in formation lines at the bubble edge (`rear` for ranged), existing wounds kept; one battle bubble at a time (plan §2.3).
- Conservation: people, items, coin (loot only moves, ransoms through the Ledger) are exact across promote and demote and through the battle's end (AB-9).

### 4.8 Worked step (illustrative arithmetic; the real numbers come from `tools/calibrate`)
Scenario: 20 militia (padded, polearm, skill 30) attack 20 bandits (cloth, blade, skill 40) in a line; terrain normal. Assume the table rows give (militia polearm vs bandit cloth, skill delta -10, no shield) `p_hit` 430 pm and out-class (serious+grave+severed+instant) 520 pm; (bandit blade vs militia padded, delta +10) `p_hit` 470 pm and out-class 300 pm.
| Quantity | Militia -> bandits | Bandits -> militia |
|---|---|---|
| Engaged `E = min(20, 0.6 x 20 x 2.5 = 30)` | 20 | 20 |
| Swings in a 15 s step (16 per minute) | 80 | 80 |
| Hits `swings x p_hit` | 34.4 | 37.6 |
| Wounds of out-class (hits x out-class) | 17.9 | 11.3 |
| Distribution | spread over active bandits with front weight 1.5, middle 1.0, rear 0.4: each draws Binomial(swings on him, 1/20) | same |
Typical result after step 1 with these placeholders: militia lose 3 to 5 soldiers to serious wounds (pain over threshold or bleeding), bandits lose 5 to 8; morale penalties accumulate 6 per casualty per 10 soldiers observed; the battle ends in 6 to 10 steps (90 to 150 s) with a bandit rout. The actual wound mix per out-wound (arm, leg, torso, head, neck, hand, artery or fracture) is drawn from the recorded wound templates, so the surviving casualties carry real wound books (a broken forearm, a cut thigh vein) into the hourly tick.
Strength monotonicity (AB-6) and conservation (AB-8, AB-9) are the invariants this arithmetic must never break.

## 5. Tick placement (20 Hz live versus abstract)
- **Abstract tier only.** A battle is created by an event in the hourly tick (a raid, an encounter outside the bubble, a war marker clash, a caravan ambush) and **resolved in the same tick** (<= 80 steps of 15 s, about 20 sim-minutes): a few milliseconds of compute, reported at the hour. The effects (wounds, deaths, captures, loot, ransoms, chronicle entries) apply at the tick barrier through commands in stable id order.
- After the battle, wounded soldiers continue under the **hourly wound tick** (bleeding closed form, first aid, infection at the day boundary). Dead soldiers become corpses with loot (`Estate`-like rows) for scavenger jobs.
- Live tier: not used. If the player is within 150 m of an abstract battle at its start it is promoted to a live fight instead.

## 6. Determinism
Streams (0x400 block): **0x4A0** `AbstractNoise` (engagement jitter +-5%, key battle id, step), **0x4A1** `WoundSample` (per hit outcome and template pick, key battle id, soldier index, draw = step x 16 + k), **0x4A2** `RoutSample` (key battle id, step, soldier), **0x4A3** `LeaderFate` (leader casualty and capture, key battle id). Time key: the **game hour** index of the battle plus the step number (Phase 3 rule: no live tick); all arithmetic integer (permille, milli); sorting by person id. `BattleHash` (reports and commands) joins the daily trail. Tables are content (hash-checked), so the same seed gives the same battle on every machine.

## 7. UI and controls
- **Chronicle (H)** lists reports with cause chains and a "Report" label; **Journal (J)** shows reports about your side. The **report viewer** shows: sides and strengths, terrain and weather, a timeline strip of 15-second steps with casualties, notable fates with cause chains, the quality factor line, and "what this cost you" (your followers, mounts, items). There is **no replay and no skip**: a report is a report, not a game screen.
- On the map (M): battle markers with the label "report". Map pins can be set to a battle's cause (a soft marker the player chooses; the world never points at anything by itself).
- Rumours (Phase 3) carry distorted counts as usual; the report keeps the true numbers for the Chronicle.
- No new keys.

## 8. Tests (golden)
| Id | Check |
|---|---|
| AB-1 | Table build is reproducible: same content hash -> byte-identical `wound_tables.json`; CI fails if the file is stale |
| AB-2 | Table sanity: monotone in skill delta and in protection (a plate-armoured defender's `p_serious+` < padded < none for every weapon class); rows sum to 1000 +- 1 |
| AB-3 | **Equivalence A, B, C, D** (200 fresh seeds each): casualties +-10%, win rate +-10 points, duration +-25%, wound-class mix +-10% relative (section 4.4) |
| AB-4 | Equivalence with and without a leader, flanked (flank bonus 250) and a mounted charge on road vs mud |
| AB-5 | The sampled out-rate equals `beta` within 5% over 10,000 steps |
| AB-6 | Strength monotonicity: adding soldiers to a side never lowers its win rate (50 scenarios x 100 seeds) |
| AB-7 | Determinism: a 100 v 100 battle gives the same `BattleHash` on both OS; reruns identical |
| AB-8 | **Conservation across demote:** a live fight demoted mid-battle ends with the same number of alive + dead + captured + fled as it started, no item duplicated, wounds equal at the instant of conversion |
| AB-9 | Coin: ransoms and loot move through the Ledger and `EconCommand`s; coin total before = after; **ransom comes only from the payer's treasury** (the v1 bug "prisoner ransom is paid from nowhere") |
| AB-10 | The commander can die (v1 bug "immortal commander"): over 500 battles, leaders of the losing side die or are captured at a rate within the table (about 15-35%) and are not immune |
| AB-11 | Bodies persist: the survivors' `WoundBook`s exist, bleeding continues, bleeding casualties die at the aftermath unless a medic treats them (rates +-10% of the tactical bleed-out) |
| AB-12 | Chronicle: every report has a cause and every named casualty a chain; coverage 100% |
| AB-13 | Performance: 100 v 100 <= 3 ms, 200 v 200 <= 8 ms, 2,000 v 2,000 (Phase 7 sizing) <= 45 ms |
| AB-14 | Weather and terrain modifiers reduce or raise casualties in the stated directions (mud kills the charge bonus; rain lowers archery by 15%) |

## 9. Open questions (recommended defaults)
| # | Question | Default |
|---|---|---|
| B1 | Tolerance +-10% on every aggregate may be hard for rare events (severed limbs) | Apply +-10% to casualties, win rate and mean wound mix; for rare classes (< 5% share) use +-3 points absolute |
| B2 | Should an abstract battle include the player's hero if the hero is far away but nominally present (autopilot)? | No: the hero is always in the live bubble; companions elsewhere are normal soldiers |
| B3 | How big can v1 go before Phase 7? | Up to 200 per side; larger sets use the same code but are untested against tactical until Phase 7 |
| B4 | Table granularity (7 armour classes) too coarse? | Start at 7; add `mail_light` and `padded_heavy` only if equivalence fails by > 8% |
| B5 | Do abstract battles produce rumours automatically? | Yes through the existing outbox, with the Phase 3 distortion rules |
| B6 | Medical care in abstract battles | 90% dressed within 1 game hour if the side holds the field and has a medic (or household in a settlement), else 40% (from wounds file 5) |

## 10. Budgets
| Item | Budget |
|---|---|
| Table build | <= 60 min on the build machine (offline, incremental per cell), 882 rows x 4,000 blows |
| Table size and load | <= 120 KB; load <= 2 ms |
| One battle step | <= 12 us per 100 soldiers; 100 v 100 whole battle <= 3 ms; 200 v 200 <= 8 ms |
| Hourly abstract tick | battles add <= 5 ms in an hour with 3 skirmishes (within the 150 ms cap) |
| Save | reports <= 200 bytes + 40 bytes per notable; wounded rows as in the wounds file |
| Memory | wound-template library <= 300 KB |
