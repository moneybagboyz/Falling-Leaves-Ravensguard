# Phase 3 specs: overview, build order, shared streams, open questions and risks

Status: design, written before any Phase 3 code (plan §4: spec, then golden tests, then implementation).
Source of truth for scheduling is `docs/REDESIGN_PLAN.md` §5 Phase 3. This file ties the ten Phase 3 specs together. It follows the
style of `docs/specs/00-overview.md` (Phase 2), whose conventions (units, ids, tick order H1..H7, `DetRng`) still apply to everything here.
It was written by reading all ten specs side by side, so section 7 lists the places where they disagree. Items 1, 2, 3, 4, 6, 11 and 12 there are now **resolved** in the specs themselves (each item is marked); items 5, 7, 8, 9, 10 and 13 are still suggestions for the implementer.

## 1. Document map

| File | Owns |
|---|---|
| `live-behaviour-and-utility-ai.md` | needs, utility scoring, schedules, beds, `Job` and reservations, odd-job boards, and the LODBridge build (milestone P3-A) |
| `trade-inventory-character.md` | exact trade quotes, "last heard" prices, inventory as item rows, skills by use, hiring, recruiting, squad |
| `dialogue-barks-rumours.md` | systemic lines (~300), barks (~110), authored unique-NPC dialogue, rumours and their source trace |
| `clothing-warmth-exposure.md` | clothing layers, insulation, cold and heat, wetness, stains, butchering |
| `basic-crafting.md` | 34 basic recipes, steps and unattended work, proficiencies, learning, crafting screen |
| `horses-and-carts.md` | animals as individuals, riding, fodder and shoes, template vehicles, speed formula, getting stuck |
| `travel-encounters-camp.md` | road encounters, auto-travel at 8x, `InterruptBus`, night camp, forage, hunting, fairs |
| `autopilot-v1.md` | directives, limits, interrupt policy, zero-player watching, party members on the same AI |
| `onboarding-v1.md` | hints, controls overlay, practice yard, mentors, safe start, the scripted first hour (first job, first ride) |
| `testers-and-crash-reports.md` | local logs, crash handler, F8 report, consent, deterministic replay of a bug save |

## 2. Module map

Arrows point from a spec to what it needs. "P2" means a Phase 2 system.

```
                         P2: abstract tick H1..H7, EconCommand queue, Ledger, MarketBook, caravans, HerdTable, weather
                                                  |
                              LODBridge (P3-A, specified in P2, never built)
                                                  |
                       +--------------------------v-------------------------+
                       |   live-behaviour-and-utility-ai   (hub: jobs, needs, schedules, boards)
                       +--+------------+-------------+------------+---------+
                          |            |             |            |
        trade-inventory-character   dialogue-barks   clothing-    basic-crafting
        (skills, hire, squad)       -rumours         warmth-       (Craft jobs,
              |        |               |             exposure      recipes)
              |        |               |               |   \         |
              |        +------+--------+               |    +--butchery--> horses-and-carts
              |               |                        |                       |
              |         travel-encounters-camp <-------+-------mounts, carts----+
              |         (InterruptBus, camp, fairs)
              |               |
              +---------> autopilot-v1 (directives on the same brain; reads every spec above)
                                  |
                          onboarding-v1 (script uses jobs, horses, talk, trade, travel, hints)
                                  |
                  testers-and-crash-reports (logs and replays everything, draws no RNG)
```

Layer view (a spec may use only the layers below it, plus the named exceptions in section 7):

| Layer | Specs | Notes |
|---|---|---|
| L0 | Phase 2 economy, abstract tick, caravans, weather, items | exists; `AbstractJob`, `PathPlan`, `LiveBinding` are specified but not built |
| L1 | live-behaviour (incl. P3-A LODBridge) | everything live depends on it |
| L2 | trade-inventory-character, clothing-warmth-exposure, basic-crafting | independent of each other except crafting needing skill gain from trade and garment numbers from clothing |
| L3 | dialogue-barks-rumours, horses-and-carts | dialogue needs chronicle and `Socialise`; horses need item rows, butchering and the Escort/Haul jobs |
| L4 | travel-encounters-camp | needs horses (gaits, `Horse` and `CartStuck` interrupts), exposure, camp cooking and snares from crafting |
| L5 | autopilot-v1 | needs jobs, travel and `InterruptBus`, trade quotes, crafting jobs, horses |
| L6 | onboarding-v1 | needs a job board, a loan horse, a mentor with dialogue, trade, auto-travel, hints for all of the above |
| X | testers-and-crash-reports | cross-cutting; hooks the log, command log and hash trail; needs onboarding and autopilot logs for its feedback form |

### 2.1 Phase 2 systems each spec reads or writes

| Phase 2 system | Used by |
|---|---|
| `EconCommand` queue and barrier, `Ledger` (wages, escrow, tolls) | live-behaviour, trade, autopilot (adds `Origin`), travel, onboarding, testers (command log) |
| `MarketBook`, `PriceNotebook`, `TripPlanner` | trade, autopilot |
| `LabourMarket`, `Business`, farming labour (live work leases a station) | live-behaviour |
| `HouseholdTable` beds, `NutritionCd`, clothes wear | live-behaviour, clothing |
| `HerdTable`, `VehicleDef`, caravans and `PathPlan` | horses, travel, autopilot |
| `Chronicle`, `RegionOutbox` rumour seeds, continent news | dialogue |
| `ResourceLayer` forage, game and fish stocks | travel |
| `EconomyHash` per day, `ScheduleReplay` | testers, autopilot (replay test), dialogue (`TalkHash`) |

## 3. Build order

Each step ends with its golden tests green (plan §4). Steps inside a number can run in parallel.

1. **P3-A: LODBridge** (`LiveBinding`, promotion queue, fold-back) plus `AbstractJob` and `PathPlan` from Phase 2 spec 3.4 to 3.6.
   Gate: Phase 2 tests G-A4 (promote and demote conserve people, items, coin) and L-13. Nothing else in Phase 3 can start without it.
2. **testers-and-crash-reports** (logging, crash handler, consent, replay trace). Early on purpose: every later step is then debuggable by replay, and
   testers can start on step 7 builds. It has no sim dependencies beyond the command log.
3. **live-behaviour-and-utility-ai** (needs, scorer, schedules, beds, `Job`, reservations, boards, odd-job pay). Gate: L-1 to L-13, cross-floor test L-7.
4. In parallel: **trade-inventory-character** (item-row inventory, quotes, skills by use, hire) and **clothing-warmth-exposure**
   (insulation, core temperature, wetness; butchery can wait for step 6) and **basic-crafting** (recipe database, `CraftJob`, requirement sets; needs skill gain from trade).
5. **horses-and-carts** (animals, mount, `VehicleMath`, templates). Gate: H-13 and H-14 (abstract and live speeds agree), since travel depends on it.
6. **travel-encounters-camp** (encounters, `TravelAuto`, `InterruptBus`, camp, forage, hunt, fairs) and **dialogue-barks-rumours** in parallel. Butchery and hunting close here.
7. **autopilot-v1** (directive runner, limit guard, router). Gate: AP-2, AP-4, AP-7, then the year soak AP-11.
8. **onboarding-v1** (start-site finder, hints, mentors, loan, first-hour script). Gate: `--autoplay` bot O-4 on all seven origins.
9. **Private testers start** on the build from step 8, with the F8 report and replay from step 2. Close-out: code and bug review (plan §6), `docs/reviews/phase-3.md`, first `docs/playtests/` session.

Milestones: P3-A (step 1), P3-B "world lives" (steps 3 to 4), P3-C "the first ride" (steps 5 to 6), P3-D "hands off" (step 7), P3-E "testers" (steps 8 to 9).

## 4. Phase 3 done-when mapped to specs and tests

Plan §5 lists four done-when items. Test ids are those in each spec's section 7.

| Done-when item | Specs | Proving tests | Extra check |
|---|---|---|---|
| Residents keep schedules across floors | live-behaviour (schedules, beds, `LiveBinding`), clothing (`OutfitI` for NPCs) | **L-7** (100 residents, bed on z=1, work on z=0, 3 days, 0 stuck), L-1 to L-3 (schedules by latitude and season), L-6 (beds), L-11 (live and abstract hashes equal), L-13 (promote and demote mid-haul), L-12 | Nightly 1-year soak with a scripted bot (plan §6) |
| You can talk to, trade with, hire and recruit anyone | dialogue (lines, relations), trade (quotes, hire, recruit), live-behaviour (jobs, wages, escrow), autopilot (`BigChoice` declined) | D-1 to D-5, T-9 to T-11 (quotes, slippage, notebook), **T-12 to T-14** (hire acceptance, recruit, wage default), T-15 (conservation over 1,000 trades), L-8 and L-9 (escrow and odd-job pay) | "Anyone" is tested for shopkeepers, labourers, unique NPCs and a stranger on the road: add one test per class (section 7, item 12) |
| A rumour traces back to its source | dialogue (rumour records, spreader, trace), chronicle causes, P2 outbox | **R-1** (every hearing resolves to its chronicle entry, 0 broken chains over 5,000 hearings), **R-2** (hero asks in 10 towns, 100% of Journal rumours expand to a witness), R-3 to R-6 (spread rate, distortion, caps, replay) | `TalkHash` joins the daily hash (AP-15, TR-10) |
| Testers can get through the first ride and the first odd job without help | onboarding (script, hints, loan, mentors), live-behaviour (board, pay), horses (loan horse, gaits), travel (auto-travel, interrupts), testers (report, replay, feedback form) | **O-4** (bot completes steps 1 to 8 on 7 origins x 3 seeds), O-5 (first job pay 71 mites), O-6 and O-7 (loan), O-1 to O-3 (safe start), O-8 to O-10 (hints, overlay), **O-14** (manual tester measure), H-5 and H-14 (cart and abstract speeds), V-2 and V-3 (8x invariance and ETA), TR-4 and TR-10 (crash zip, replay exact), TR-18 | O-14 gate: the plan's 2 to 3 private testers; each finishes steps 3 and 6 unaided, or the stuck points are written into the review (section 7, item 11, resolved) |

Plan §6 verification items that Phase 3 owns, and where they are covered:

| Plan §6 line | Covered by |
|---|---|
| Autopilot: directives and limits obeyed (spending cap, no crimes) | AP-2, AP-3, AP-4, AP-5, AP-6, AP-12 |
| Autopilot: every interrupt kind fires | AP-7 (counts all 23 kinds of the canonical list in `autopilot-v1.md` 3.2) |
| Autopilot: a year-long soak keeps the hero alive and solvent in a peaceful region | AP-11 (3 seeds), AP-10 (30-day zero-player) |
| Crafting: recipe validation, unattended steps ruin past deadline plus grace, proficiency penalties, practice recipes, disassembly returns components | K-1, K-5, K-7, K-13, **K-19 (disassembly)** |
| Vehicles: speed against mass, surface, slope; abstract and live times agree; people and items aboard move | H-5 to H-8, H-14, **H-18 (slope)**; "design validation" is out (templates only, designer is Phase 5) |
| Derived stats golden tests | T-1 to T-6, C-1 to C-16 |
| Conservation across LOD | L-13, T-15, V-16, K-17, H-12 |
| Fire: spread, rain puts out | not a Phase 3 item; camp fire (V-8) only burns fuel |
| Determinism and saves | every spec has a determinism and round-trip test; TR-10 to TR-13 cover replay across OS |
| Fun checks (20 to 30 min session, 2 to 3 testers) | O-14, TR-18 |

## 5. RNG streams

Phase 2 owns 0x200..0x2FF (`docs/specs/00-overview.md` §2.3; 0xC0 weather and 70..78 minerals exist). Phase 3 specs all use ids inside **0x300..0x3FF**, which are free in the Phase 2 table.
They must be registered together in `Core/RngStreams.cs`, with a test that fails on a duplicate id.

### 5.1 The time-key rule (resolves section 7, item 1)

Every Phase 3 stream is keyed on **game time or on something that does not depend on time scale**, never on the live tick counter or a frame number. At 8x a live tick covers 8 game seconds, so `tick / 20` is a different number at 1x and 8x and would make two runs roll differently. The rule:

- `tickKey` = `GameSec` (game seconds since day 0, `GameTime.TotalSeconds`) divided by a named window: **20 s** (one real second at 1x), **60 s** (game minute), **200 s**, **600 s** (10 game minutes), **3,600 s** (game hour, the Phase 2 key) or **86,400 s** (game day). The window is written in the key column below.
- A stream may instead key on a counter that belongs to the thing rolled and moves with the world, not the clock: a distance (`odometer_m / 100`), a job id, an output serial, an entity id.
- `RngStreams` exposes one helper, `Draw(stream, gameSec, window, entity, draw)`, that takes no tick parameter. The two invariance tests H-3 (horses) and V-2 (travel) compare the draw logs of every stream they touch between a 1x and an 8x run, draw for draw, so a stray `tick` fails them.
- Where a roll was "once per second", it now happens once per 20-game-second window, which is one real second at 1x; at 8x it happens once per 20 game seconds as well, so the roll count per game time is the same.

| Stream | Spec | Use | Entity key (time part first) |
|---|---|---|---|
| 0x223 | crafting (reused, exists) | craft quality roll | crafter, output serial |
| 0x2A0 | live-behaviour (reused, exists) | arrival spot jitter in a lot | person |
| 0x300 | live-behaviour, autopilot | utility noise and ties | `GameSec / 20`, person, draw = action index |
| 0x301 | live-behaviour, autopilot | job claim tie-break | game hour, **job**, draw = claimant person index |
| 0x302 | live-behaviour | bed fallback spot | game day, person |
| 0x303 | live-behaviour | routine job generation | game day, settlement |
| 0x310 | dialogue | systemic line pick | `GameSec / 60`, speaker, draw = listener x 16 + turn |
| 0x311 | dialogue | bark pick, cooldown jitter | `GameSec / 20`, person |
| 0x312 | dialogue | rumour seed witness selection | chronicle entry |
| 0x313 | dialogue | hearing probability | game hour, teller, draw = listener |
| 0x314 | dialogue | distortion roll | game hour, listener |
| 0x315 | dialogue | gathering partition | game hour, place |
| 0x320 | trade | attribute rounding, use-event jitter (reserved) | person |
| 0x321 | trade | hire and recruit mood noise | game day, person |
| 0x322 | trade | quality of goods in an NPC pack (stocking time only) | owner |
| 0x330 | clothing | stain rounding, tear rolls | item |
| 0x331 | clothing | per-person tolerance jitter at creation | person |
| 0x332 | clothing | butchery yield variance | carcass entity |
| 0x340 | travel | encounter roll and table choice | game hour, hero id, draw = 10-minute slot (0..5) |
| 0x341 | travel | encounter placement | encounter instance |
| 0x342 | travel | forage poisoning and item mix | `GameSec / 200`, person, draw = item index |
| 0x343 | travel | hunting spawns, snare catches | game hour, tile |
| 0x344 | travel | sentry notice rolls | `GameSec / 20`, sentry, draw = approacher index |
| 0x345 | travel | fair events, contest results | game day, settlement |
| 0x346 | travel | camp night events (split from 0x344) | game hour, camp |
| 0x350 | horses | forced-exertion injury, lameness onset | `GameSec / 20`, animal |
| 0x351 | horses | stuck check, abstract stuck duration | game hour, vehicle, draw = `odometer_m / 20` (live); vehicle, leg index (abstract) |
| 0x352 | horses | temper and breed jitter at materialisation | herd id, serial |
| 0x353 | horses | shoe loss (split from 0x350) | animal, draw = `odometer_m / 100` |
| 0x354 | horses | taming success (split from 0x350) | `GameSec / 600`, animal, draw = rider person index |
| 0x360 | crafting | failure roll at the critical step | crafter, job, step |
| 0x361 | crafting | byproduct chance | job |
| 0x362 | crafting | learning by watching | person, recipe |
| 0x370 | onboarding | start-site tie-break, mentor spot jitter | world seed, origin |
| 0x371 | onboarding | onboarding job parameters | new-game seed |
| 0x380 | autopilot | reserved, asserted never drawn | person |
| (none) | testers | draws no RNG | |

Collision check (done again after the changes above):
- **No id is claimed by two different specs for different purposes, and none lies in the Phase 2 range 0x200..0x2FF.** `docs/specs/00-overview.md` lists no 0x3xx id. The only reuses are intentional: 0x223 and 0x2A0 are existing streams, and 0x300/0x301 are named by both live-behaviour (owner) and autopilot (consumer).
- **Split (resolved):** 0x344 now serves only sentry notice rolls; camp night events moved to **0x346**. 0x350 now serves only forced-exertion injury and lameness onset (both are health rolls on the same animal); shoe loss moved to **0x353** and taming to **0x354**. A change to one roll order can no longer shift another.
- **0x301 key (resolved):** keyed by the **job**, as live-behaviour has it. Reason: a claim is a contest for one job. Each claimant gets the rank `Draw(0x301, hour, jobId, claimantIdx)` and the lowest wins, so the winner does not depend on how many claim or in what order they are processed, and the hero under autopilot, a party member and an NPC are all ranked by the same rule. Keying by person would let the result change whenever a different set of people claimed.
- **`tickKey` (resolved):** see 5.1; no live tick or frame number remains in any key.
- Free ids kept for growth: 0x304..0x30F, 0x316..0x31F, 0x323..0x32F, 0x333..0x33F, 0x347..0x34F, 0x355..0x35F, 0x363..0x36F, 0x372..0x37F, 0x381..0x3FF.

## 5A. Control keys (one consolidated table; resolves section 7, item 2)

Source of truth for existing keys is plan §2.8 (`InputDefs.cs`). This table adds the Phase 3 keys. `InputDefs.All` must equal it, and CI checks it by two rules: (1) no key is bound twice **inside** a context; (2) for contexts that are **active together**, the merged map has no key bound to two different actions. Contexts active together: **World + CombatStance**, **World + Mounted**, **World + CombatStance + Mounted**. Everything else is a modal mode or screen that replaces the world keys while it is open, so it is checked alone (a key such as `F` or `N` may mean something else there).

| Context | Keys | Origin |
|---|---|---|
| **World** | W A S D move; E default action; Q everything you can do; Tab cycle own people; Space pause; 1 2 3 4 speeds; **T called shot**; R (hold) order wheel; F1 to F4 formations; C I P J L F K H panels (character, inventory, squad, journal, ledger, faction, codex, chronicle); B build; M map; arrows, `=`, `-`, Home camera; Esc back; Shift sprint (hold); **Z sneak**; X combat stance; G throw; V torch; PgUp/PgDn level; Alt nameplates; `?` controls overlay; F5/F9 quicksave/quickload; F8 report a bug | plan §2.8 |
| World, new | **N** make (crafting screen); **Y** autopilot panel | Phase 3 |
| **CombatStance** (with World) | mouse attack and block; Ctrl + direction dodge; T called shot; F1 to F4 and R as in World | plan §2.8, no new keys |
| **Mounted** (new layer with World; only while riding or driving) | **O** walk toggle (`walk_toggle`); Shift is the World `sprint` action, shown as gallop; E is the World default action, shown as dismount. Z stays sneak and does nothing while mounted | Phase 3 (horses) |
| **CalledShot** (paused, alone) | arrows, F strike, Esc | plan §2.8 |
| **Build** (alone) | Z / X rotate, Esc | plan §2.8 |
| **Map** (new screen, alone) | arrows pan, `=` / `-` zoom, **Enter** travel here for the selected place, M or Esc close | Phase 3 (travel) |
| **Dialogue** (alone) | 1 to 9 choose, E or Enter advance, H open history link, Esc leave | Phase 3 |
| **Trade** (alone) | arrows, Left/Right quantity (Shift x10, Ctrl all), Enter confirm, Tab change pane, **N** price notebook, Esc | Phase 3 |
| **Crafting** (alone) | arrows, Enter craft, Tab detail panes, `/` search, F can-make filter, Esc | Phase 3 |
| **Loot window** (alone) | R take all, Esc | Phase 3 (trade) |
| **Menu** (alone) | mouse, Esc | Phase 3 screens |

Decisions behind the table:
- **T and Z are not reused.** "Travel here" is a Q-menu verb, an order-wheel entry and the Map screen's **Enter**; it has no World key. Horse walk is **O** in the Mounted layer, not Z.
- **N is free:** the plan's World, CombatStance, CalledShot and Build tables do not use it, and it does not collide with the Mounted layer. Inside the Trade screen N toggles the price notebook (a different, modal context).
- **Free in World after Phase 3:** U, 5 to 9, 0, F6, F7, F10 to F12 (O is used by Mounted, so it counts as taken whenever World is active).
- Autopilot's banner reads "Autopilot paused - Y opens the panel".

## 6. Combined open questions (deduplicated, each with a recommended default)

Questions below are "for the user" (player-facing or scope). Duplicates across specs are merged and the owning specs named.

| # | Question | Specs | Recommended default |
|---|---|---|---|
| Q1 | How lethal is cold and exposure? | clothing, travel, autopilot | Severe cold kills only after the person is unconscious and left outside; HUD warns hours ahead. Do not halve cooling until testers die in the first ride |
| Q2 | Must everyone sleep (strict fatigue), or is sleep cosmetic for NPCs? | live-behaviour, autopilot, travel | Strict for everyone; hero effects mild (skills -10% at 800 permille, collapse at 1000) |
| Q3 | Pay for odd jobs, and job boards versus talk only | live-behaviour, onboarding | 1.10 x unskilled wage (71 mites for the first job); boards in towns, talk in villages. Raise only if testers call the first job pointless |
| Q4 | Auto-travel at dusk: notify or auto-stop and camp? | travel, autopilot | Notify; night travel is allowed because it is dangerous, not forbidden. Autopilot `Ai` policy makes camp |
| Q5 | Encounter frequency | travel, onboarding | One scripted encounter per 7 game hours; first 2 days safe (V-5). Halve or double after tester feedback |
| Q6 | Autopilot default limits and whether big choices pause | autopilot | Cap 3 crowns a day, no crimes, risk Medium, home-by-nightfall off, one confirm screen. Marriage, fealty, bribe, oath and sale of property always pause (hard rule) |
| Q7 | Do followers pass earnings to the hero? | autopilot, trade | `remit_pct` 0 (own purse). Party cap `2 + leadership/10`, hard cap 12 (13 persons with the hero) |
| Q8 | Haggling, friend discounts | trade | No haggling and no discount in v1; Trading skill narrows spread 1% per 10 levels (max 8%) and widens "last heard" reach |
| Q9 | Carry weight strictness | trade | Strict as plan §4A.3 (140 kg at strength 50, slowing from 70 kg); a settings toggle for testers |
| Q10 | Hint level, mentor pin, loan horse | onboarding | Hints `full` for the first hour then `minimal`; pin is optional, never forced; loan a horse for 3 game days (not a gift) |
| Q11 | Fair-weather start shift | onboarding | Shift the start by up to 10 days to avoid a storm in the first 24 hours (a placement rule, like the war and lair rule) |
| Q12 | Practice yard and autopilot `Attacked` before Phase 4 combat | onboarding, autopilot | Use the Phase 0 melee spike; `Attacked` means flee or shelter. Real watch mode is Phase 4 |
| Q13 | Rumour truth: may NPCs lie? | dialogue | No; v1 only degrades facts (vague, exaggerated, rare misattribution). Slander arrives with reputation in Phase 6 |
| Q14 | Barks and voice, text volume | dialogue | Text only until Phase 8; ship 300 lines and 110 barks, expand each phase |
| Q15 | Horse price and mud severity | horses | Palfrey 480 crowns, let the price solver adjust; keep carts stuck in mud, but if testers hate it raise `pull_n` 25% or lower mud `crr_pm` to 140 |
| Q16 | Breeding, foals, wild horses | horses | Breeding deferred to Phase 5; wild horses exist on grassland as herd rows and are tameable |
| Q17 | Recipe count and crafting speed | crafting | 34 recipes (plan says about 30); crafting runs up to 4x, never 8x; food has no quality tier |
| Q18 | Crafting key | crafting, onboarding, trade | **N** opens crafting in the world context; N also toggles the notebook inside the trade screen (a modal context, so legal). Full table in section 5A |
| Q19 | New skills appended to the `Skill` enum | crafting (used by travel, clothing, trade) | Accept `Tailoring, Surgery, Speech, Athletics, Tactics, Stealth, Lockpicking`; save version bump and migration |
| Q20 | NPC wetness, frostbite, sunburn, butchering people | clothing | Abstract NPCs have no wet clothes; frostbite and sunburn wait for Phase 4 wounds; people are never butcherable (a taboo mod could change it) |
| Q21 | Hunting realism and camp decay | travel | Bow shots use the existing strike resolver with simple animal AI; camps decay after 3 game days |
| Q22 | Tester data: autosave, upload, expiry, log level, retention, screenshot | testers | Autosave on in tester builds with the file list shown and removable; no networking ever in Phase 3; 60-day build expiry with a warning at 45; `INFO` logs; keep reports until the tester deletes them; screenshot on |

## 7. Cross-spec inconsistencies and suggested fixes

1. **`tickKey` definition. RESOLVED (section 5.1).** Phase 2 §2.3 says the key is the game hour index, but Phase 3 specs used `tick/20` (live-behaviour, horses 0x350/0x351, travel sentries), `tick/200` (forage), game minute (dialogue) and `(tick, personId)` for barks. Live ticks are not multiplied at 8x but game time is, so `tick/20` would differ between 1x and 8x runs and break H-3, V-2 and cross-speed replay.
   *Fix applied:* all Phase 3 streams key on `GameSec` divided by a named window (20, 60, 200, 600, 3,600 or 86,400 s) or on a distance, job id or serial. Rewritten in live-behaviour (0x300, reservation expiry now in game seconds), horses (0x350, 0x351, 0x353, 0x354, test H-3), travel (0x340, 0x342, 0x344, 0x346, test V-2), dialogue (0x310, 0x311, 0x313), autopilot, testers, and the stream table in section 5.
2. **Control keys. RESOLVED (section 5A).** `InputDefs.cs` binds **T** to `called_shot` and **Z** to `sneak` (plan §2.8). Travel and onboarding had bound T to "travel here", horses had made Z toggle walk, and onboarding said CI rejects duplicates.
   *Fix applied:* T and Z are untouched. "Travel here" is a Q-menu, order-wheel and Map-screen verb (Map: Enter). Horse walk toggle is **O** in a new `Mounted` layer (Shift and E keep their World meaning there). N (crafting) and Y (autopilot) are confirmed free. One table in section 5A lists every context; CI checks duplicates inside a context and clashes between contexts that are active together (World, CombatStance, Mounted).
3. **Interrupt kinds disagree. RESOLVED.** Travel listed 15 kinds with values pause/notify/ignore; autopilot listed 13 of its own with Pause/Notify/Ai, said it "adds four" and counted "11 travel kinds"; four names differed (`Lost`/`BlockedPath`, `Companion`/`CompanionTrouble`, `Contract`/`ContractOffer`, `Hunger`/`LowFood`).
   *Fix applied:* one canonical list in `autopilot-v1.md` section 3.2: **23 kinds** (15 raised by travel, 8 added by autopilot: `Threatened, Injured, LowMoney, BigChoice, DirectiveDone, LimitReached, NeedMoney, Stuck`), one append-only `InterruptKind` enum and one policy type `Pause | Notify | Ai`, with each kind's trigger and default. The four duplicate names were merged. Travel's `ignore` is `Notify` with `quiet`. `travel-encounters-camp.md` now only refers to that table. AP-7 counts from the enum.
4. **Recipe count. RESOLVED.** `basic-crafting.md` said 34 in the header, K-1 and Q1, but the heading of section 4.1 said 33. The table has 34 rows (numbered 1 to 34), so the heading now says 34.
5. **Party size.** Autopilot and clothing budget for "13 persons"; trade caps the party at 12 plus hero. These agree only if the hero is counted separately. *Fix:* state once in trade: "squad cap 12, plus the hero = 13 tracked persons", and reference it in the other two. Tracked persons for `SkillMicro` are "<= 64 per world" (trade) while clothing allows "<= 400 tracked rows live": different sets (skills versus core temperature); name them `SkillTracked` and `CoreTracked`.
6. **Hunger thresholds. RESOLVED.** Travel auto-ate at 10 hours since the last meal; live-behaviour had Eat beat a Work block at 13 hours (L-4).
   *Fix applied:* one constant, `hunger_eat_hours = 13`, defined in live-behaviour 4.1 (`utility.jsonc`). Travel's `Hunger` interrupt and auto-eat, camp meals and autopilot read it; no spec keeps its own number. L-4 is unchanged.
7. **Live tick budget.** Live-behaviour claims 2.5 ms of an 8 ms tick; travel promises a 6 ms live tick at 8x; plan §2.11 gives 60 fps. Sum of Phase 3 live costs is about 3.6 ms (section 9), so the numbers fit, but only if nobody else adds. *Fix:* the overview budget table (section 9) is the single ledger; specs must not state their own share of the 8 ms.
8. **Inventory storage.** Trade moves `HeroRecord.Inventory` to item rows; clothing adds `WetPm` and `StainKind` to `ItemRow` and says the `Wet` flag "exists"; crafting and horses also add item and goods fields. Several columns are added to `ItemRow` and `PersonTable` without a combined list. *Fix:* one save-version bump listing all new columns in order: trade (`SkillMicro`, `AttrMilli`, `TrackedIdx`), live-behaviour (`FatiguePm`, `SocialPm`, `LastMealHour`, `Mood`, `Action`, `ActionSince`, `JobId`, `JobStep`), clothing (`CoreCenti`, `OutfitI`, `ExposureMin`, `WetPm`, `StainKind`), plus tables `JobTable`, `ReservationTable`, `RumourTable`, `HearingTable`, `RelationTable`, `AnimalTable`, `VehicleTable`, `CraftJobTable`, `AutopilotTable`, `SquadTable`, `LoanTable`. One migration, one round-trip test.
9. **Hash family.** Dialogue defines `TalkHash`, testers add `CraftHash`, clothing and horses define none. *Fix:* register `TalkHash, CraftHash, BehaviourHash (jobs and reservations), SurvivalHash (core, wetness), AnimalHash` together with the replay tooling (TR-10).
10. **Skill names.** Travel and autopilot use `Forestry`, `Archery`, `Butchery`, `Speech`, `Smithing`. The first three exist; `Speech` is among crafting's additions. Trade's "dictionary of 27 ids" becomes 34 after Q19; check `HeroRecord` migration (T-16) covers it.
11. **Tester numbers. RESOLVED.** Plan §6 says 2 to 3 private testers; onboarding's gate needed at least 5. *Fix applied:* the 5 minimum is dropped. The gate (onboarding O-14, testers spec) is: each of the 2 to 3 testers finishes steps 3 and 6 unaided, or every stuck point is written into `docs/reviews/phase-3.md` with its cause.
12. **Scope edges. RESOLVED for the two tests.** Added **K-19** in `basic-crafting.md` (disassembly returns 70% of the recorded components by mass, the rest goes to the waste stock, mass exact) and **H-18** in `horses-and-carts.md` (slope speed up and down, with `F_req` clamped at 0 downhill). Still open and only labelled: the horse body plan is a stub in Phase 3 yet butchering horses is specified; autopilot `Tend` and `PursueAmbition` are stubs (autopilot 3.1 says so).
13. **Phase 2 wording.** Live-behaviour, horses and travel refer to "overview E2/E9/O4/O10" and to "overview §3" of Phase 3 for the milestone P3-A. This file is that overview; P3-A is defined in section 3, step 1.

## 8. Risks

| Risk | Why it matters | Mitigation |
|---|---|---|
| **LODBridge has no code** (live-behaviour 2) | Every live system relies on promote and demote conserving state; it was specified in Phase 2 but never built | Build it first (P3-A) with the G-A4 and L-13 conservation tests; no other step merges before it passes |
| Scope: ten specs in 9 to 11 months | Plan overrun rule: more than 50% over means move features to the after-1.0 list | Cut order if late: feasts and fairs, hunting, the 40-hint list down to 12 (`minimal`), practice yard, rumour distortion, barks. Never cut LODBridge, jobs, onboarding script, replay |
| Two-tier divergence | Live is "theatre except leased work"; if the live and abstract paths differ, bugs show only at promotion | L-11 (identical hashes with and without the hero in the bubble), H-14, AP-1 equivalence, `ScheduleReplay` |
| Determinism leaks | Many new rolls keyed on live ticks, UI-thread hints, threaded logging | Section 5.1 (game-time keys only); counter-based streams only; replay tests TR-10 to TR-13 on both OSes; the reviewer's list in plan §6 |
| Autopilot exploits or bankrupts | A rule-following AI finds loopholes (buy-low sell-high, pay-wage loops) or starves the hero | Limits enforced twice (plan and barrier), AP-11 soak with 3 seeds, `LimitViolation` counter must be 0, conservation tests |
| Onboarding too hard or confusing | The plan's biggest unknown is "did you understand why?" | Safe start, loan horse, optional pin, tester metrics (O-14), hint levels; fair-weather shift; first 2 days free of ambush |
| Lethality surprise (cold, mud, fatigue) | Several specs add new ways to die or get stuck at the same time | Q1 defaults, HUD warnings, interrupts that pause, tester log of every death with cause |
| Content volume | 300 lines, 110 barks, 34 recipes, 40 hints, 24 encounters, 20 to 50 lines per mentor | Content scaled separately from systems (plan §7); validators in each spec reject missing keys; expansion pass each phase |
| Art and UI load | Many new screens: trade, inventory, character, crafting, autopilot, camp, report, hint toasts, overlay | One `PixelTheme`; reuse `ScreenRouter`; screens are thin views over snapshots (no state in nodes); autoplay screenshots in CI |
| Privacy of tester reports | Autosaves and screenshots are personal | Consent file, visible file list, redaction test TR-3, no networking test TR-8, tester build expiry |
| Save growth | Rumour hearings (up to 2 MB) and logs add to the save | Caps in each spec (section 9); the Phase 2 `SaveBudget` test extended with all new sections |
| Key map crowding | N, Y, mounted O and the Map Enter on top of ~50 existing | Section 5A table; CI checks duplicates in a context and clashes across contexts that are active together |

## 9. Budgets

Reference PC as in plan §2.11. Per-spec budgets are copied here so the totals can be checked in one place. Live tick target is 8 ms of the 16.6 ms frame (the rest is render).

### 9.1 Live tick (20 Hz), average per tick

| Item | Spec | Budget |
|---|---|---|
| Utility scorer for 300 people, movement and pose | live-behaviour | 0.15 ms and 1.5 ms (behaviour total <= 2.5 ms including `JobRunner` 0.3 ms) |
| Exposure and wetness, 300 people | clothing | 0.15 ms (+0.05 ms hero and squad, +0.05 ms stains) |
| Animals and vehicles (30 and 60) | horses | 0.4 ms |
| Barks and live rumours | dialogue | 0.05 ms + 0.1 ms |
| Crafting (<= 20 jobs) | crafting | 0.05 ms |
| Skill-use queue | trade | 0.02 ms |
| Encounter check, interrupts, sentries | travel | 0.02 ms (per minute) + 0.05 ms + 0.05 ms |
| Autopilot (13 persons) | autopilot | 0.01 ms |
| Hints, log, heartbeat, replay trace | onboarding, testers | 0.02 ms + 0.15 ms (testers' figure is per frame) |
| **Phase 3 total** | | **about 3.6 ms** (target <= 4 ms, leaving 4 ms for Phase 4 combat) |
| 8x travel, corridor bubble <= 60 entities | travel | live tick <= 6 ms |

### 9.2 Abstract tick (per game hour, region of 25,000 people)

Phase 2 budget stands: typical 3.5 ms per hour, hard cap 150 ms (`HourlyTickBudget`).

| Item | Spec | Budget |
|---|---|---|
| Rumour spreading | dialogue | <= 1.0 ms per game hour |
| Board refresh at hour 0 | live-behaviour | <= 3 ms per region |
| Abstract exposure (extreme hours only) | clothing | <= 0.5 ms |
| Danger recompute (daily) | travel | <= 0.5 ms per region |
| `surface()` cache refresh (hour 0) | horses | <= 1 ms |
| Autopilot planner | autopilot | <= 2 ms per person per day |

### 9.3 UI, tools, memory and saves

| Item | Budget |
|---|---|
| Trade screen open (<= 300 goods) | <= 8 ms; quote <= 5 microseconds |
| Crafting screen open | <= 3 ms; can-make for 300 recipes <= 1 ms |
| Inventory with 600 rows | <= 4 ms |
| Controls overlay build | <= 2 ms; autopilot panel <= 2 ms |
| `StartSiteFinder` | <= 150 ms per region, on a worker |
| F8 screenshot <= 30 ms; zip of 100 MB <= 5 s; replay >= 100x real time | testers |
| Save growth | `talk` <= 2.5 MB; `trade/inventory` <= 0.5 MB; animals and vehicles <= 0.3 MB; jobs <= 0.3 MB; camps and journeys < 100 KB; crafting < 100 KB; autopilot <= 100 KB; onboarding < 5 KB. **Total Phase 3 growth <= 4 MB** per detailed region and hero save |
| Allocation gate | 0 allocations per tick in steady state for scorer, line selection, `LimitGuard`, `VehicleMath`, quotes, logging |
| Content | `hints.jsonc` <= 40 KB; Phase 3 `en.csv` additions <= 460 KB |

## 10. Test strategy

- Every spec has golden tests (about 172 in all: AP 17, K 19, C 16, D 6, R 6, H 18, L 13, O 14, TR 19, T 16, V 16). New hash kinds join the daily hash trail so a replay finds the first divergence.
- Shared harness: the Phase 2 `HeadlessWorld`, extended with a scripted player bot (`--autoplay`) that uses only UI commands. The bot runs the onboarding script (O-4), a 30-day zero-player run (AP-10) and the nightly one-year soak.
- Conservation audits (coin through the ledger, goods through `MassLedger`, persons and animals by count) run in L-13, T-15, K-17, H-12, V-16 and must stay exact.
- At phase end: the review and health pass of plan §6, written up in `docs/reviews/phase-3.md`, and a 20 to 30 minute checklist session in `docs/playtests/`.
