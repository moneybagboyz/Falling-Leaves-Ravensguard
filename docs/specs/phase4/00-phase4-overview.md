# Phase 4 specs: overview, build order, shared streams, keys, open questions and risks

Status: design, written before any Phase 4 code (plan §4: spec, then golden tests, then implementation).
Source of truth for scheduling is `docs/REDESIGN_PLAN.md` §5 Phase 4 ("Combat anywhere and exploration", 9 to 12 months). This file ties the eleven Phase 4 files together and follows the style of `docs/specs/phase3/00-phase3-overview.md` (Phase 3) and `docs/specs/00-overview.md` (Phase 2): units on every rate, ids in a reserved block, tick placement, golden tests, open questions with recommended defaults. Conventions that still apply: integer or `Fixed` arithmetic in the sim (no `double`, `Math.Pow`, `Math.Sin` in sim code), counter-based `DetRng` streams, stable-id iteration, content as JSON through the `ModLoader`, UI as thin views over snapshots, strings through `tr()`.
Plan sections covered: §2.6 (the single resolver), §2.7 (3-view animation, wound overlays, flavour, feedback), §2.8 (stance and called-shot keys), §2.10 (materials, layers, wear), §3.6 and §3.7 (combat anywhere, failure and captivity), §3b (exploration), §3e (mounted combat), §3i (watch mode), §4A.3 (the decided human detail), §6 (combat and material-science tests).

## 1. Document map

| File | Owns | Tests |
|---|---|---|
| `anatomy-and-body-plans.md` | human plan v2 (35 instances, ligaments at six joints, motor and sensory nerves, spinal cord, graded muscle), `BodyState` capacities, horse and quadruped plans, draugr and cave beast plans | AN-1 to AN-14 |
| `resolver-v2.md` | integer momentum, layers (cut, blunt, fail), conservation, penetration, severing, structures and hosted parts, organs, called-shot hit chance, gaps, blocks, falls, calibration targets | RS-1 to RS-24 |
| `wounds-bleeding-pain-healing.md` | bleeding by vessel, clotting, shock bands, pain pool, knock-outs, concussion, fractures, tendons, nerves, spine, infection, lifecycle, first aid, splints and crutches, amputation | WB-1 to WB-16 |
| `melee-and-ranged.md` | 18 weapon shapes, timings in ticks, stamina, shields and walls, bows and crossbows, thrown and improvised, the combat AI | ML-1 to ML-16 |
| `morale-capture-succession.md` | morale at 2 Hz, rout and surrender, defeat outcomes by winner, captivity, ransom, escapes, Ironman succession | MC-1 to MC-13, SU-1 to SU-5 |
| `mounted-combat.md` | charge momentum, horse as a target and its panic, unhorsing, braced spears, terrain | MT-1 to MT-13 |
| `feedback-flavour-log.md` | flavour templates from the layer trace, the log, the after-fight report, hit effects, slow motion, placeholder audio, accessibility | FL-1 to FL-13 |
| `watch-mode.md` | hand-off, stances, take-back, interrupts, odds | WM-1 to WM-13 |
| `abstract-battles-v1.md` | wound tables built by the tactical resolver, the Lanchester exchange, equivalence, cause chains | AB-1 to AB-14 |
| `exploration-and-sites.md` | site generation and contents from history, torches and light, hazards, traps, the auto-map, two monsters, weather in combat | EX-1 to EX-18 |

About **159** golden tests. Content volume (plan §5): about 18 weapon shapes, about 40 armour pieces, about 30 underground room templates, about 420 flavour text rows, 2 monster plans.

## 2. Module map

```
 P0 resolver v1 + melee spike (Sim/Combat)      P1 sites, blueprints, levels    P2 abstract tick, treasuries, Ledger    P3 jobs, behaviour, autopilot, horses,
        |                                              |                                |                                 travel/encounters, clothing, crafting, dialogue
        |                                              |                                |                                          |
        v                                              v                                v                                          v
  anatomy-and-body-plans  -->  resolver-v2  -->  wounds-bleeding-pain-healing        (all read P3 persons, inventory, Chronicle)
                                   |                         |
                                   +---------+---------------+
                                             v
                                   melee-and-ranged  (FightWorld, CombatBrain, projectiles)  <------ mounted-combat (rider + horse)
                                     |        |                |
                  feedback-flavour-log        |                +--> watch-mode (stances on CombatBrain, interrupts, odds)
                  (events -> text, audio)     v
                                   morale-capture-succession (rout, outcomes, captivity, ransom, succession)
                                             |
                                             v
                               abstract-battles-v1 (tables baked from all of the above; reports; cause chains)

   exploration-and-sites (generation, light, hazards, traps, map, 2 monsters, weather in combat) uses anatomy (monster plans), resolver (falls, traps),
   wounds (far from a surgeon), melee (monsters), morale (hideouts, captives)
```
Layer view (a spec may use only the layers below it plus named exceptions in section 7):
| Layer | Specs | Notes |
|---|---|---|
| L0 | Phases 0-3 (exists) | resolver v1, body plan v1, melee spike, abstract tick, LODBridge, jobs, horses, encounters stub, autopilot |
| L1 | anatomy-and-body-plans | content and `BodyState`; no behaviour |
| L2 | resolver-v2 | pure function; needs L1 |
| L3 | wounds-bleeding-pain-healing | needs L1, L2 |
| L4 | melee-and-ranged | `FightWorld`; needs L2, L3 |
| L5 | morale-capture-succession, mounted-combat, watch-mode | needs L4 (watch mode also autopilot) |
| L6 | feedback-flavour-log | reads events from L2 to L5; presentation only |
| L7 | abstract-battles-v1 | needs L2 to L5 to build tables, then runs alone |
| X | exploration-and-sites | uses L1 to L5, adds site content; independent of L6, L7 |

### 2.1 Phase 3 systems read or written

| Phase 3 system | Used by |
|---|---|
| `PersonTable` (`Father`, `Mother`, `Spouse`, `BirthDay`, attributes, skills), `HeroRecord`, `HeroParty`, `Followers`, `Hiring` | morale-capture-succession (succession), watch-mode (squad), melee (skills) |
| `ActionKind`, `UtilityBrain`, `ActionPolicy`, `Job`, reservations, `ScheduleBook` | melee (`Fight`, `Treat` appended), watch-mode (biases), morale (guard shifts), exploration (inhabitants) |
| `InterruptBus`, `InterruptKind` (23), `InterruptRouter`, `LimitSet.NoLethalForce`, `MaxRiskPm` | watch-mode (4 kinds appended, 27 total), morale (`Attacked`) |
| `EncounterOutcomes` (`EncounterChoice.Fight` stub), `Encounters`, `Cast` (road groups) | melee and morale: the stub is replaced by `FightSetup` (section 3) |
| `Animal`, species rows, `RideBook`, `RideMath`, surfaces | mounted-combat, anatomy (horse plan) |
| `Garment`, `ClothingBook`, wetness and exposure | resolver (worn layers, wet cloth weight), wounds (cold) |
| Crafting content: `bandage`, `poultice`, `splint`, `torch`, `rope` goods; proficiencies `first_aid`, `herbalism` | wounds (treatment), exploration (torches) |
| `LightModel`, `LightGrid`, `LevelGrid`, `Portal`, `Weather` | exploration |
| `Chronicle`, `Cause`, `RegionOutbox`, `Demography` | feedback (chains), morale (death), abstract (reports) |
| `ExploredTiles` (surface fog of war, 64 m tiles) | unchanged; the underground `AutoMap` is a separate per-site structure |

### 2.2 Phase 3 limits carried into Phase 4 (`docs/reviews/phase-3.md`, "Known limits") and where each is answered

| Carried limit | Answered by |
|---|---|
| **Real road combat: the encounter fight is a stub that robs or runs** (`EncounterOutcomes`, `ENC_FIGHT_STUB_*`) | Section 3: `Fight` creates a live `FightSetup` from the encounter's groups (`Cast`, `kit`s); the stub's "party strength weighs toward running" becomes `RiskEstimate` odds shown before choosing (watch-mode 3.3); flee, pay and detour remain; the outcome is the real defeat table (morale-capture-succession 4.4) |
| **The live people are promoted afresh from the schedule after a load (the bridge is not saved)** | Phase 4 cannot restart a fight from a schedule. A fight in progress is saved as a `FightRecord` (section 8: fighters, positions in mm, states, `FightTick`, wound books, stances), and a load **resumes the fight** at the same tick (test OV-3: save and load mid-fight, then the `FightHash` of the next 600 ticks equals the uninterrupted run). Non-fight NPCs are still rebuilt from the schedule; the LODBridge save for them is tracked separately (below) |
| NPCs sheltering from cold or rain change their exposure but not their position | Not combat. Carried to Phase 5 with the LODBridge persistence (weather shelter positions use the same `Flee` and `Shelter` actions; no Phase 4 spec depends on it) |
| The live tier is "theatre" for scheduled work (`WorkDone` reaches `IWorkSink` for farm hours only) | Not combat. Phase 4 adds `Treat`, `Fight` and site inhabitants as the first non-farm live jobs whose results are **real** (wounds, deaths, loot written back through commands); other jobs stay as in Phase 3 |
| Weather-driven rest, storm suspension, feast half-days, escort follow check, shop takings, hero fatigue | Weather in combat is in exploration 4.8. Escort follow check feeds watch-mode `protect`. The rest stays Phase 5 |
| Fair-weather start shift, items abroad, crafting effects with no world state | Unrelated; Phase 5 |
| Autopilot `Tend` and `PursueAmbition` stubs | Unrelated (Phase 5, 8) |

## 3. Replacing the encounter stub: the fight setup (one entry point for every fight)

`FightSetup { Kind (Encounter, Raid, Ambush, SiteInhabitants, Tournament, Practice), Sides[] (persons with kits, positions), Terrain, Weather, Cause (EntityId), StanceDefaults }` is built by travel encounters (`EncounterChoice.Fight` or an ambush script), by sites (inhabitants wake), by the practice yard (the Phase 0 spike weapons `practice_*`, blunted at x0.5 momentum), and by tournaments (yield at pain knock-out). It spawns a `FightWorld` in the live bubble (or, outside any bubble, an `AbstractBattle`). The hero's `EncounterChoice.Fight` is **offered with odds** ("probably lose") and never forced. Practice yard fights use the same code with `blunted` weapons so onboarding is unchanged. Time keys: `FightTick` (section 6.1).

## 4. Build order

Each step ends with its golden tests green (plan §4). Steps with the same number can run in parallel.

1. **P4-A: the blow.** Content first (`human.jsonc` v2, tissue fields, 18 weapons, ~40 armour pieces, horse, quadruped, draugr, cave beast), then `ResolverV2` (integer), `BodyState`, `CombatTables`, and `tools/calibrate` (the duel harness). Gate: RS-1 to RS-24, AN-1 to AN-14; the wiki-transcription task (resolver R1). *Built (see the Notes at the end of resolver-v2.md and anatomy-and-body-plans.md): all gates green; streams 0x400-0x417 and 0x430 registered; the wiki transcription is still blocked (df-calibration.md).*
2. **P4-B: the body after the blow.** `WoundBook`, bleeding, pain, consciousness, treatment, healing, infection, surgery. Gate: WB-1 to WB-16 (the spike's bleed-out checks now in `WB-1`).
3. **P4-C: the duel.** `FightWorld`, timeline, defence, stamina, shields and walls, projectiles, thrown and improvised, `CombatBrain`, stances (without watch UI), `ActionKind.Fight/Treat`, `FightRecord` save. Gate: ML-1 to ML-16 and the calibration targets (resolver-v2 8: 10-20 s duels, 40-60% defended, spear >= 55%, bleed-out <= 35% with treatment). The Godot stance UI and the three-view animations (rig bakes) land here (art task). *Built (see the Notes at the end of melee-and-ranged.md): ML-1 to ML-16 green; the calibration table there (duels 10.6-18 s, 21 s plate against plate; defended 40-59%; spear 60% against an unarmoured sword; the mace beats a sword in plate 57%; bleed-out with first aid 5.6%); streams 0x431-0x433 and 0x440-0x442 registered; save v6 (weapon families, the "fight" section); keys F6, Shift+F6, F7, Shift+F7 in World and CombatStance, 5 and 6 in CombatStance (the combat stance re-declares the World keys it keeps, as Phase 3 did).*
4. In parallel after P4-C: **feedback-flavour-log** (events are in P4-C already), **morale-capture-succession** (needs the fight end), **mounted-combat** (needs the horse plan and `FightWorld`).
5. **P4-F: watch mode** (needs `CombatBrain` and the interrupt kinds) and the **encounter replacement** (section 3). Gate: WM-1 to WM-13, AP-7 updated to 27 kinds.
6. **P4-G: abstract battles v1** (needs tables from steps 1 to 4). Gate: AB-1 to AB-14 (equivalence on fresh seeds).
7. **P4-H: exploration and sites** (generation, light, hazards, traps, map, monsters, weather). Can start the generation, light and map pieces at step 3; monsters and traps need step 3 to 4. Gate: EX-1 to EX-18 including the crypt crawl EX-12.
8. **P4-I: close-out.** The code and bug check (plan §6): review of all Phase 4 code, health pass (full suite green, save round trip byte-identical), findings fixed or written into `docs/reviews/phase-4.md`; a 20-30 minute fun-check session in `docs/playtests/` (a duel, a skirmish, a defeat with escape, a crypt crawl), 2 to 3 private testers asked "did you understand why?".

Milestones: **M-A "the blow"** (step 1), **M-B "the duel"** (steps 2 to 3), **M-C "the aftermath"** (steps 4 to 5: defeat, capture, succession, watch), **M-D "the crawl"** (steps 6 to 7).
Estimate against plan §5 (9 to 12 months): A 1.5 mo, B 1, C 2.5, D (parallel) 2, F 1, G 1.5, H 2.5, I 0.5 = about 12 months serial with D and H partly parallel: **plan on 10 to 12**, on the upper half of the range if the art (animations, wound overlays) is not parallel to code.
**Cut order if late (overrun rule, plan §7):** (1) placeholder audio hooks and slow motion (keep flashes and callouts), (2) the second monster (cave beast; keep draugr), (3) trample, refusal and terrain slip in mounted combat (keep charge, unhorse, brace), (4) thrown improvised weapons beyond javelin and dagger, (5) captivity transfer escapes (keep lock-pick, ransom, rescue), (6) battlefield and hoard scavenging. **Never cut:** resolver v2, wounds with first aid, real-time melee, the defeat table, captivity with a way out, succession, abstract tables, determinism and replay.

## 5. Done-when mapped to specs and tests

Plan §5 lists five done-when items (and §6 lists the combat tests):
| Done-when | Specs | Proving tests | Extra check |
|---|---|---|---|
| **A seeded fight is deterministic** | all combat files, section 6 below | RS-22 to RS-24 (draw order, near/far, cross-OS), ML-15, MT-13, WM-1, WM-2, WM-9, MC-6, AB-7, FL-13, SU-5, **OV-1** (a 20 v 20 seeded fight with every feature gives the same `FightHash` on Linux and Windows CI) | `FightHash`, `WoundHash`, `MoraleHash` in the daily hash trail |
| **Near and far soldiers produce statistically equivalent outcomes** | melee 5, resolver 6, abstract 4.4 | **ML-16** (AI at 20 Hz near vs 5 Hz far), RS-23 (no camera input), AB-3 (abstract vs tactical +-10%) | Plan §6: "near and far soldiers equivalent; the abstract resolver within +-10% of tactical" |
| **A called shot through an armour gap works** | resolver 4.1, 4.2, 4.5, melee 7, anatomy 7 | **RS-15** (gap: mail bypassed, hit chance x0.35, structure x3), RS-14, RS-21 (eye behind an open face), FL-5 | **OV-2:** an autoplay script presses T, picks `armpit` on a mail wearer, presses F; the trace shows `Bypassed` and the brachial or shoulder wound rate matches the area boost |
| **Defeat leads to a capture you can escape** | morale 4.4 to 4.7 | **MC-7 to MC-13** (robbery, capture, ransom from the payer's treasury, escape rolls, rescue control switch, conservation, time limits), SU-1 to SU-4 for the other branch | **OV-4:** a seeded bot is KO'd by an army detachment, is captured, picks the lock at skill 50, reaches the road; coin and items conserved |
| **A crypt crawl by torchlight ends with an injury far from a surgeon that matters** | exploration 4.4 to 4.10, wounds 4.6, 4.7 | **EX-12**, EX-5 to EX-9, WB-8 (tendon window) | The playtest checklist item in `docs/playtests/` |
Plan §6 combat tests: seeded fights (OV-1, ML-15); near/far (ML-16); abstract within +-10% (AB-3); hero on watch mode uses exactly the NPC combat AI (**WM-1**); every resolver outcome has a flavour template (**FL-1**); material science golden tests (RS-2 to RS-12: the battle-axe momentum numbers, mail turning cuts into blunt blows without dampening RS-10, rigid metal soaking 98 to 99% of a stopped blow RS-9, light weapons deflecting off dense armour RS-11, severing when penetration exceeds part size RS-12). The plan §6 line "Fire: spread, rain puts out" is not Phase 4; the draugr's burning (AN-12) uses a targeted burn wound only.

## 6. RNG streams

Phase 2 owns 0x200..0x2FF and Phase 3 owns 0x300..0x3FF. **Phase 4 uses 0x400..0x4FF**, registered together in `Core/RngStreams.cs` with the existing uniqueness test (a test also fails if any id lies outside 0x400..0x4FF).

### 6.1 The time-key rule and its one exception
Phase 3 keys every stream on `GameSec` windows, a distance or a serial, never on a live tick, because at 8x a live tick covers 8 game seconds. Combat needs fixed-length steps for its timings (a 0.2 s parry window is 4 ticks), so Phase 4 states one exception, in one place:
- **`FightTick`** is an integer counter that belongs to the fight (it starts at 0 at the first combat tick, increments by exactly 1 per 20 Hz fight step) and does not depend on the world clock or the speed setting. Combat streams key on it (a counter "that belongs to the thing rolled", allowed by Phase 3 rule 5.1).
- **A fight never lengthens a tick.** At 2x, 4x and 8x the bubble that contains a fight steps `speed` times per frame (and drops one speed level if the 25 ms budget is exceeded). Test WM-9 asserts identical `FightHash` at 1x, 2x, 4x, 8x. Outside fights, Phase 3 behaviour stands.
- Streams for things that happen in game time (captivity events, infection, site hazards, abstract battles, scavenging) **key on game time or an id**: game day, game hour, `GameSec/20` windows, battle id and step.
- Cosmetic streams (0x480, 0x481) are excluded from the state hash; slow motion changes steps per real second only (test FL-9).

### 6.2 Table (Phase 4 block)

| Stream | Spec | Use | Key (entity, time) |
|---|---|---|---|
| 0x400 | anatomy | body variation (size, blood volume, handedness) at creation | person |
| 0x401 | anatomy | monster size variation at spawn | creature id |
| 0x410 | resolver | hit roll | `FightTick`, attacker, swing serial x 64 + 0 |
| 0x411 | resolver | redirect on a missed called shot (yes/no, which neighbour) | same, draws 1-2 |
| 0x412 | resolver | coverage rolls outside in | same, draw = layer index |
| 0x413 | resolver | structure, ligament, nerve, cord, hosted-part and rib-slip rolls | same, draw = structure index (63 = ribs) |
| 0x414 | resolver | organ rolls | same |
| 0x415 | resolver | artery rate 80-120 (stored in the wound) | same |
| 0x416 | resolver, anatomy, mounted | fall and joint-wrench placement, partial-cord residual | `FightTick` or event serial, person |
| 0x417 | anatomy | stumble from an unstable joint | `GameSec/20`, person |
| 0x420 | wounds | daily infection, level rise, sepsis death day | game day, person, wound id |
| 0x421 | wounds | malunion at healing completion | wound id |
| 0x422 | wounds | surgery outcomes | `GameSec/20`, surgeon, wound |
| 0x423 | wounds | concussion duration jitter | `FightTick`, person |
| 0x424 | wounds | wake jitter | `FightTick`, person |
| 0x430 | melee | AI reactions (parry, dodge, block), parry and dodge success | `FightTick`, defender, blow serial |
| 0x431 | melee | AI target choice | `FightTick`, fighter |
| 0x432 | melee, watch | stance noise, tie-breaks | `FightTick`, fighter |
| 0x433 | melee | stagger and knock-down duration | `FightTick`, fighter |
| 0x440 | ranged | aim error (2 draws per shot) | `FightTick`, shooter, shot serial |
| 0x441 | ranged | ammunition breakage and recovery | fight id, ammo serial |
| 0x442 | ranged | thrown aim error | `FightTick`, thrower |
| 0x450 | morale | rally, wavering step-back | `FightTick`, fighter |
| 0x451 | morale | surrender acceptance by the winner | `FightTick`, winner |
| 0x452 | morale | loot choices, mercy bandage, cruel kill | fight id, loser |
| 0x453 | morale | captivity: guard shifts, notice rolls | `GameSec/20`, captive |
| 0x454 | morale | transfer escape windows | game day, captive |
| 0x455 | morale | lock picking and bonds | `GameSec/20`, captive, attempt |
| 0x456 | morale | ransom negotiation | game day, captive |
| 0x457 | morale | reserved: succession draws nothing (asserted) | - |
| 0x470 | mounted | unhorse, pin, lance shatter | `FightTick`, rider |
| 0x471 | mounted | horse panic effects, refusal | `FightTick`, horse |
| 0x472 | mounted | fall landing placement | `FightTick`, rider |
| 0x473 | mounted | terrain slip | `FightTick`, horse |
| 0x480 | feedback | flavour line and variant pick (cosmetic, deterministic) | `FightTick`, event serial |
| 0x481 | feedback | cosmetic jitter (shake, decals, pitch), never hashed | event serial |
| 0x490 | watch | reserved, asserted never drawn | person |
| 0x4A0 | abstract | engagement jitter | battle id, step |
| 0x4A1 | abstract | per-hit outcome and wound-template pick | battle id, soldier, step x 16 + k |
| 0x4A2 | abstract | rout sampling | battle id, step, soldier |
| 0x4A3 | abstract | leader fate | battle id |
| 0x4B0 | sites | layout | site id, level |
| 0x4B1 | sites | loot rolls | site id, room |
| 0x4B2 | sites | inhabitants | site id, serial |
| 0x4B3 | sites | hazard events (flood, cave-in, bad-air pockets) | game hour, site, hazard id |
| 0x4B4 | sites | trap trigger | `GameSec/20`, trap, entity |
| 0x4B5 | sites | trap detection | `GameSec/20`, person, trap |
| 0x4B6 | sites | monster behaviour (wake, wander, flee) | `GameSec/20`, monster |
| 0x4B7 | sites | cave-in debris pattern | event id |
| 0x4B8 | sites | reoccupation | game day, site |
| 0x4B9 | sites | scavenging yield | action serial, person |
Collision check: no id outside 0x400..0x4FF; none in the Phase 2 (0x200..0x2FF) or Phase 3 (0x300..0x3FF) ranges; the stream **0x430** (melee AI) is used by watch mode **unchanged**, and 0x490 is a reserved sentinel like 0x380. Free ids for growth: 0x402..0x40F, 0x418..0x41F, 0x425..0x42F, 0x434..0x43F, 0x443..0x44F, 0x458..0x46F, 0x474..0x47F, 0x482..0x48F, 0x491..0x49F, 0x4A4..0x4AF, 0x4BA..0x4FF.

## 6A. Control keys (one consolidated table)

Source of truth for existing keys is plan §2.8 (`InputDefs.cs`) plus the Phase 3 table. `InputDefs.All` must equal this table; CI checks (1) no key bound twice inside a context, and (2) the merged map of contexts that are active together has no key bound to two actions. Contexts active together: **World + CombatStance**, **World + Mounted**, **World + CombatStance + Mounted**.
| Context | Keys | Origin |
|---|---|---|
| **World** (as bound in `InputDefs.cs` today) | WASD move, E default action (and LMB), Q everything (and RMB), Tab cycle own people, Space pause, 1-4 speeds, **T called shot**, R (hold) order wheel, F1-F4 formations, C I P J L F K H panels, B build, M map, **U cart hitch**, `\` cart unload, `]` cart reload, `[` cart dig, `,` camp, `.` forage, `;` hunt, `'` snare, **Y** autopilot, **N** make, arrows `=` `-` Home camera, Esc, Shift sprint, **Z sneak**, X combat stance, G throw, V torch, PgUp/PgDn level, Alt nameplates, `?` or F10 overlay, F5/F9, F8, Enter (hint more) | plan, Phase 3 code |
| World, **new in Phase 4** | **F6** watch mode toggle (`world.watch_toggle`), **Shift+F6** next stance (`world.stance_next`), **F7** combat log (`world.combat_log`), **Shift+F7** last after-fight report (`world.last_report`) | Phase 4 |
| **CombatStance** (with World) | LMB attack (hold = wind up), RMB block / parry / feint / brace (mounted: shield block), Ctrl + direction dodge (mounted: horse side-step), T called shot, F1-F4, R; **new: `5` reload or nock (`combat.reload`), `6` cycle the attack type (`combat.attack_next`; the radial lists them too)**. The Choice context (numbered lists) suspends `5` and `6` exactly as it suspends the speed digits | plan §2.8; `5`, `6` new |
| **Mounted** (with World) | **O** walk toggle, Shift gallop, E dismount | Phase 3 |
| **CalledShot** (paused, alone) | up/down body-part group and part, left/right attack type, F strike, Esc cancel | plan §2.8 (the part list grows to 35 + horse parts, grouped; no new keys) |
| **CombatLog** (with World, when focused) | up/down scroll, Enter expand, `/` search, F toggles the "my side" filter, Esc | Phase 4 (modal inside the log panel) |
| **AfterFight** (alone) | arrows, Tab section, Enter on a button, Esc | Phase 4 |
| **Captive** (with World) | the Q menu verbs; Tab (own people) takes control of a free companion | Phase 3 + 4 |
Decisions behind the table:
- **The Phase 3 overview's "free in World" list was wrong in the code:** `InputDefs.cs` binds **U** (cart hitch), **`,`** (camp), **`.`** (forage), **`;`** (hunt), **`'`** (snare), `\`, `[` and `]` (cart unload, dig, reload), the backtick (console) and F12 (inspector). Every letter key is now taken. **Actually free** in World and CombatStance after Phase 3: the digits **5-9 and 0**, **F6, F7, F11**, and modifier combinations. Phase 4 therefore takes **F6, Shift+F6, F7, Shift+F7, 5 and 6**; free after Phase 4: 7, 8, 9, 0, F11. Phase 4's first CI task is to re-derive this table from `InputDefs.All` (it must equal this table) and fix the Phase 3 overview note.
- **No key for "yield", "treat", "disarm", "search", "mark", "retrace", "climb", "stance previous"**: they are Q-menu verbs, order-wheel entries or HUD chips (plan §2.8: Q opens everything). Yield is deliberately keyless to avoid accidental surrender.
- The ranged reload cannot be `]` (cart reload). `5` reloads or nocks; with the setting *Auto-reload when idle* (default on) a bow nocks and a crossbow spans by itself while the shooter is stationary, so `5` is only needed to force or cancel.
- The mouse wheel stays zoom (World) and Shift+wheel stays level look; attack types are chosen by `6`, the radial or the called-shot menu, default **Auto** (the AI rule: point or blunt against armour, edge against bare flesh).
- `T` (called shot), `G`, `V`, `X`, `Z`, `PgUp/PgDn` keep their meanings.

## 7. Combined open questions (deduplicated, each with a recommended default)

Duplicates across specs are merged; the owning specs are named. "R" = recommended default.

| # | Question | Specs | Default |
|---|---|---|---|
| Q1 | The golden numbers are what our formulas give; the DF wiki table is the real reference | resolver R1 | Transcribe the wiki's battle-axe table in the first calibration task; where it disagrees the wiki wins and content constants change, not formulas |
| Q2 | v2 rules not in the plan: conservation (cut costs `0.6 x need`), bone resist x2.5 (edge) and x5 (crush), padding absorption, severing needs 30% of M0 left | resolver R2, anatomy | Keep all four as content-tunable numbers in `calibration.jsonc`; the harness picks values to hit the targets (10-20 s duels, 40-60% defended, <= 0.12 severed limbs per duel, spear >= 55%) |
| Q3 | Part count: plan says about 25 new parts; this lands at 19 instances (12 ids) | anatomy A1 | Keep 19; add more only on tester demand |
| Q4 | Skill names: only `Melee` and `Archery` exist; the plan lists "weapon families" | melee M1 | Append `Blades, Axes, Hafted, Polearms, Shields, Throwing` (migration copies `Melee` x0.8 into each); `Melee` stays for unarmed |
| Q5 | Blood regeneration (v1 had none) and bleed-out survivability | wounds W1, W5 | 25 ml per game hour; internal organ bleeds lethal without surgery; revisit if testers find chest wounds all-fatal |
| Q6 | How harsh is a robbery at a bad moment? | morale C1 | Purse, weapons, mount and valuables taken; shirt, boots, one meal left; 50% mercy bandage; death by bleed-out converts to Ironman succession |
| Q7 | Escape and ransom pacing | morale C3 | 10 game days for bandits, up to 40 for armies; ransom `2 x purse` floor 1,500 mites for a wanderer; payer's treasury only |
| Q8 | Succession order | morale C5 | Child >= 16, spouse, sibling >= 16, child 12-15 with guardian, parent, companion, follower, none; property in Phase 5, reputation in Phase 6 |
| Q9 | Watch mode: take control on any combat input? | watch WA1 | Yes (default), off for zero-player; speeds allowed; auto-pause on danger |
| Q10 | Watch mode keys | watch WA7 | F6 toggle, Shift+F6 next stance, stance chips and the order wheel; no letter key is free |
| Q11 | Slow motion: presentation steps per second only | feedback F2 | Never changes outcomes; hero-involved only; off for NPC fights |
| Q12 | Gore level | feedback F1 | Normal; a `gore: low` template set as an accessibility option |
| Q13 | Lance power against plate | mounted H1 | Keep; quality-3.0 plate stops a skill-50 lance; tune `charge_k` |
| Q14 | Abstract battle sizes before Phase 7 | abstract B3 | Up to 200 per side; larger uses the same code, untested vs tactical until Phase 7 |
| Q15 | Equivalence tolerance on rare events | abstract B1 | +-10% on casualties and win rate; +-3 points absolute for classes with < 5% share |
| Q16 | Torch length, reoccupation, monsters per lair | exploration E1, E5, E6 | 1,200 sim-s; 2% per day after day 20; 1-3 draugr, 1 cave beast (25% a pair) |
| Q17 | Grave robbing before the crime system | exploration E7 | A `TabooRobbed` event and rumours; the law arrives in Phase 6 |
| Q18 | Mounts underground | exploration E8 | Forbidden |
| Q19 | Guards, jail and slavery | morale C6 | Phase 6; Phase 4 stubs: guard fights end with a fine and release, "sold" captives are released stripped with a rumour |
| Q20 | Firedamp and lightning | exploration E3, E4 | Not in Phase 4 (blackdamp only; lightning is flavour) |
| Q21 | Fight time scale exception | overview 6.1 | A fight never lengthens a tick; speed = steps per frame |
| Q22 | Maul contact area 60 -> 25 and other content changes | melee 3.1 | Accepted as calibration; the old 60 made the maul worse than a mace |
| Q23 | Do animals get the human soft-tissue detail? | anatomy A6 | No; horses get tendon lameness only |
| Q24 | Hit formula situational points (light, footing, mounted, wet) | exploration 4.8, mounted 4.1 | One integer field `SituationPoints` on the resolver input, added to the skill delta |

## 8. Cross-spec notes and fixes (resolved here, and what the implementer must keep in step)

1. **Enums grow append-only:** `StructureKind` (+`MotorNerve, SensoryNerve, Ligament, SpinalCord`), `WoundFlags` (+7 bits), `WoundKind` (+`Burn`), `ActionKind` (+`Fight, Treat`: `ActionPolicy.Kinds` 15 -> 17), `InterruptKind` (+`WatchDanger, WatchLosing, WatchAmmoOut, WatchOver`: 23 -> 27; the Phase 3 autopilot test AP-7 must count 27), `PortalKind` unchanged, `Skill` (+6 weapon families), `Region` unchanged. Each appends keep the existing numbering.
2. **One save-version bump** with all new columns and sections, in this order: `WoundBook` rows (wounds), `CharacterPermanent` (8 bytes per person), `PartState`, `Captive` and `Estate` (morale), `WatchStance` per hero and follower, `SiteState` deltas and `MapKnown`, `FightRecord` (a fight in progress, section 3), `BattleReport` list, `InheritanceRecord`, animal wound books. A migration maps v1 nerve ids and `HealthPm`. The Phase 3 save-budget test is extended; Phase 4 growth <= 6 MB per detailed region and hero save (section 10).
3. **`FightRecord` (saved only mid-fight):** `FightId`, `FightTick`, `Fighters[]` (id, position mm, facing Q10, `Act`, `ActTicks`, `Req`, stamina, `MoraleState`, `Stance`, wound book reference), `Projectiles[]`, formations, RNG needs nothing (counter-based). About 100 bytes per fighter; 2,000 fighters = 200 KB.
4. **Hit formula inputs:** `SituationPoints` (Q24), `Facing`, `Guarded`, `TargetGap`, `TargetInset` are the complete list; any new modifier must be added there and in resolver-v2 4.1, not elsewhere.
5. **Content id migrations** (`"migrate"` table): `neck_nerve`, `arm_nerve`, `forearm_nerve`, `hand_nerve`, `sciatic_nerve`, `leg_nerve`, `foot_nerve` -> motor and sensory pairs.
6. **Naming:** `ExploredTiles` (surface fog of war) is unchanged; `AutoMap` is per site and level. Phase 3's `HealthPm` on `Animal` becomes a derived summary of the horse's wound book.
7. **Money units:** ransoms and loot are stated in **mites** (1 crown = 100 mites, Phase 2). The 3,000 crown courser is 300,000 mites.
8. **Tick budgets** are in section 9 (the single ledger); specs must not claim their own share of the 8 ms or 25 ms.
9. **Weapon content changes** that affect existing tests: maul area, `great_axe`, `flat` attack on blades, `reach_mm` and `strike_ticks` fields; the spike's `MeleeRules` constants move to `combat/melee.jsonc` (they were C# constants).
10. **Phase 3 test AP-7 and the autopilot panel** list interrupt kinds: both read the enum, so adding kinds only needs the new default policies and `tr()` strings.

## 9. Budgets (the single ledger; reference PC as in plan §2.11)

### 9.1 Live tick (20 Hz), average per tick
Phase 3 spent about 3.6 ms of the 8 ms normal-bubble tick and left about 4 ms for Phase 4 (Phase 3 overview 9.1).
| Item | Spec | Normal bubble (about 300 people, <= 30 fighting) | Battle bubble (2,000 soldiers; plan 25 ms) |
|---|---|---|---|
| Fight step (timeline, defence, projectiles) | melee | 0.4 ms | 9 ms |
| Resolver (530/s typical, 1,300/s peak) | resolver | 0.1 ms | 4 ms per second spread = 0.2 ms per tick avg, 0.7 peak |
| Combat AI (20 Hz near, 5 Hz far) | melee, watch | 0.5 ms | 5 ms |
| Wounds (bleeding, pain; <= 300 bleeders) | wounds | 0.05 ms | 0.15 ms |
| Morale at 2 Hz | morale | 0.02 ms | 0.3 ms |
| Mounted (<= 60 entities) | mounted | 0.15 ms | 0.15 ms |
| Event ring write | feedback | 0.02 ms | 0.3 ms |
| Sites: lights, map, hazards, traps, monsters | exploration | 0.5 ms (underground) | n/a |
| **Phase 4 total** | | **about 1.8 ms** (target <= 4 ms) | **about 19 ms** (cap 25 ms) |
Presentation (render thread, per frame, not the tick): hit effects 0.5 ms, log 2 ms max, audio 1 ms, after-fight report 15 ms once.
### 9.2 Abstract tick and tools
Hourly: abstract battles <= 5 ms in an hour with 3 skirmishes (hard cap with everything else 150 ms); wound and infection tick <= 0.8 ms; reoccupation and floods daily <= 0.3 ms. `tools/calibrate`: table build <= 60 min offline; the duel harness 2,000 duels <= 40 s; equivalence suite (AB-3) <= 6 min in CI (nightly), a 1-minute smoke in PR CI.
### 9.3 Memory, saves, content
| Item | Budget |
|---|---|
| `Fighter` 64 bytes, `MoraleState` 12, `Projectile` 48 | 2,000 + 600 in flight <= 300 KB |
| `CombatTables` (weapons, armour, plans) | <= 8 MB, built at load in <= 80 ms |
| Save growth | wounds <= 24 B per wound; captives and estates < 50 KB; site deltas <= 4 KB per visited site; fight record <= 200 KB (mid-fight only); log <= 128 KB per fight; reports ~200 B each. **Phase 4 growth <= 6 MB** per detailed region and hero save |
| Allocation gate | 0 allocations per tick in steady state for the resolver, timeline, brain, projectile, wound step, morale |
| Content | weapons ~12 KB, armour ~30 KB, bodies ~210 KB, flavour ~60 KB + 80 KB `en.csv`, sites ~300 KB, generated wound tables <= 120 KB, placeholder audio <= 6 MB (CC0) |

## 10. Risks

| Risk | Why it matters | Mitigation |
|---|---|---|
| **Lethality and feel are not decided by anyone on paper** | The spike showed one landed blow wounded or amputated; the resolver numbers (conservation, bone resist, padding) are our own | `tools/calibrate` with explicit targets before any UI work (P4-A gate); the DF wiki transcription task; a human feel pass on the spike scene before P4-C closes |
| **Scope: eleven specs in 9 to 12 months** | Plan overrun rule applies | Cut order in section 4; the never-cut list; milestones M-A to M-D |
| **Determinism leaks** (floats from the spike, hash-order iteration, UI-time effects) | A seeded fight must match across OS | Integer sim; `FightTick` rule; cosmetic streams excluded from hashes; cross-OS hash tests RS-24, OV-1; reviewer checklist of plan §6 |
| **Abstract vs tactical divergence** | Two paths for the same fight | Tables built from the real resolver; fresh-seed equivalence tests nightly; promotion and demotion conservation AB-8 |
| **Mid-fight saves and LODBridge persistence** | The Phase 3 bridge was not saved; fights cannot be rebuilt from a schedule | `FightRecord` saved and resumed (OV-3); keep the non-fight NPC rebuild as is |
| **Balance of defeat** (brutal but fair) | Robbery, captivity and death can turn players away | Odds shown before fights (watch-mode 3.3), the mercy bandage, guaranteed ways out of captivity, Ironman succession with kin; testers asked "did you understand why?" |
| **Art load** | 3 views x actions x layers for wind-up, strike, block, parry, hit-react, stagger, fall, crawl; wounds overlays; horse and 2 monsters; weapons and armour layers | Rig baking (plan §2.7); palette swaps; draugr and cave beast reuse a palette-shifted human and bear rig; art produced per phase; if late, drop the second monster first |
| **Key map crowding** | Every letter key is used; Phase 4 adds six keys (F6, Shift+F6, F7, Shift+F7, 5, 6) on top of about 70 | Section 6A (re-derived from `InputDefs.cs`); CI duplicate and active-together checks; order-wheel and Q-menu fallback |
| **Performance in the battle bubble** | 2,000 fighters, projectiles, AI | Budgets in section 9; LOD for AI and rendering only; speed drops automatically; precomputed tables; 0 allocations gate |
| **Content volume** | 420 flavour rows, 40 armour pieces, 30 room templates, 18 weapons | Content scaled separately from systems (plan §7); validators; coverage tests FL-1, EX-3 |
| **Sites feel samey** | Procedural caves and crypts can bore | Contents come from history (named dead, real loot budgets, band stashes); hazards with readable warnings; monsters tied to legends; expansion pass each phase |
| **Accessibility and gore** | Flashes, shake, slow motion, gore | The `Accessibility` facade for every effect; lint FL-12; gore-low option |

## 11. Test strategy

- Every spec has golden tests (**about 159**: AN 14, RS 24, WB 16, ML 16, MC 13, SU 5, MT 13, FL 13, WM 13, AB 14, EX 18) plus four overview integration tests: **OV-1** (a seeded 20 v 20 fight with archers, shields and one mounted unit gives the same hashes on both OS), **OV-2** (called shot through an armour gap by an autoplay script), **OV-3** (save and load mid-fight resumes identically), **OV-4** (defeat, capture, escape by lock-pick with conservation).
- Shared harness: the Phase 2 `HeadlessWorld` plus the `--autoplay` bot (Phase 3) extended with combat commands (stance, called shot, treat, escape actions); `tools/calibrate` shares the same `FightWorld` headlessly.
- Conservation audits (people, items by mass, coin through the Ledger, blood and wounds across promote and demote): MC-12, AB-8, AB-9, WB-14 and OV-4 must stay exact.
- Hash trail additions: `FightHash`, `ResolverHash`, `WoundHash`, `MoraleHash`, `CaptiveHash`, `EstateHash`, `MountedHash`, `WatchHash`, `BattleHash`, `SiteHash`, `FlavourHash` (cosmetic family, excluded from state hash but checked for replay).
- Nightly: the 1-year soak with a scripted bot now includes two bandit encounters per game month (fights through the real code or abstract) and checks that coin, population and item mass stay conserved and the hero's wound state matches the log.
- Phase end: the code and bug check (plan §6), `docs/reviews/phase-4.md`, a 20-30 minute fun-check session in `docs/playtests/` and 2 to 3 testers asked "did you understand why?".
