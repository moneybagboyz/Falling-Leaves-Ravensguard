# Phase 3 specs: overview, build order, shared streams, open questions and risks

Status: design, written before any Phase 3 code (plan §4: spec, then golden tests, then implementation).
Source of truth for scheduling is `docs/REDESIGN_PLAN.md` §5 Phase 3. This file ties the ten Phase 3 specs together. It follows the
style of `docs/specs/00-overview.md` (Phase 2), whose conventions (units, ids, tick order H1..H7, `DetRng`) still apply to everything here.
It was written by reading all ten specs side by side, so section 7 lists the places where they disagree.

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
| Testers can get through the first ride and the first odd job without help | onboarding (script, hints, loan, mentors), live-behaviour (board, pay), horses (loan horse, gaits), travel (auto-travel, interrupts), testers (report, replay, feedback form) | **O-4** (bot completes steps 1 to 8 on 7 origins x 3 seeds), O-5 (first job pay 71 mites), O-6 and O-7 (loan), O-1 to O-3 (safe start), O-8 to O-10 (hints, overlay), **O-14** (manual tester measure), H-5 and H-14 (cart and abstract speeds), V-2 and V-3 (8x invariance and ETA), TR-4 and TR-10 (crash zip, replay exact), TR-18 | O-14 gate: at least 5 testers over 3 origins, 4 or more finish steps 3 and 6 unaided (but see section 7, item 11) |

Plan §6 verification items that Phase 3 owns, and where they are covered:

| Plan §6 line | Covered by |
|---|---|
| Autopilot: directives and limits obeyed (spending cap, no crimes) | AP-2, AP-3, AP-4, AP-5, AP-6, AP-12 |
| Autopilot: every interrupt kind fires | AP-7 (see section 7, item 3 on the kind lists) |
| Autopilot: a year-long soak keeps the hero alive and solvent in a peaceful region | AP-11 (3 seeds), AP-10 (30-day zero-player) |
| Crafting: recipe validation, unattended steps ruin past deadline plus grace, proficiency penalties, practice recipes, disassembly returns components | K-1, K-5, K-7, K-13; **disassembly has no test in `basic-crafting.md`** (section 7, item 12) |
| Vehicles: speed against mass, surface, slope; abstract and live times agree; people and items aboard move | H-5 to H-8, H-14; "design validation" is out (templates only, designer is Phase 5); **slope has no test** (section 7, item 12) |
| Derived stats golden tests | T-1 to T-6, C-1 to C-16 |
| Conservation across LOD | L-13, T-15, V-16, K-17, H-12 |
| Fire: spread, rain puts out | not a Phase 3 item; camp fire (V-8) only burns fuel |
| Determinism and saves | every spec has a determinism and round-trip test; TR-10 to TR-13 cover replay across OS |
| Fun checks (20 to 30 min session, 2 to 3 testers) | O-14, TR-18 |

## 5. RNG streams

Phase 2 owns 0x200..0x2FF (`docs/specs/00-overview.md` §2.3; 0xC0 weather and 70..78 minerals exist). Phase 3 specs all propose ranges inside **0x300..0x3FF**.
They must be registered together in `Core/RngStreams.cs`, with a test that fails on a duplicate id.

| Stream | Spec | Use | Entity key |
|---|---|---|---|
| 0x223 | crafting (reused, exists) | craft quality roll | crafter, output serial |
| 0x2A0 | live-behaviour (reused, exists) | arrival spot jitter in a lot | person |
| 0x300 | live-behaviour, autopilot | utility noise and ties | person, draw = action index |
| 0x301 | live-behaviour, autopilot | job claim tie-break | job |
| 0x302 | live-behaviour | bed fallback spot | person |
| 0x303 | live-behaviour | routine job generation | settlement |
| 0x310 | dialogue | systemic line pick | speaker, draw = listener x 16 + turn |
| 0x311 | dialogue | bark pick, cooldown jitter | person |
| 0x312 | dialogue | rumour seed witness selection | chronicle entry |
| 0x313 | dialogue | hearing probability | teller, draw = listener |
| 0x314 | dialogue | distortion roll | listener |
| 0x315 | dialogue | gathering partition | place |
| 0x320 | trade | attribute rounding, use-event jitter (reserved) | person |
| 0x321 | trade | hire and recruit mood noise | person, day |
| 0x322 | trade | quality of goods in an NPC pack (stocking time only) | owner |
| 0x330 | clothing | stain rounding, tear rolls | item |
| 0x331 | clothing | per-person tolerance jitter at creation | person |
| 0x332 | clothing | butchery yield variance | carcass entity |
| 0x340 | travel | encounter roll and table choice | hero id, hour, minute slot |
| 0x341 | travel | encounter placement | encounter instance |
| 0x342 | travel | forage poisoning and item mix | person, tick/200 |
| 0x343 | travel | hunting spawns, snare catches | tile, hour |
| 0x344 | travel | sentry notice rolls **and** camp night events | sentry, tick/20; camp, hour |
| 0x345 | travel | fair events, contest results | settlement, day |
| 0x350 | horses | forced-exertion injury, shoe loss, lameness onset, taming | animal, tick/20 |
| 0x351 | horses | stuck check, abstract stuck duration | vehicle |
| 0x352 | horses | temper and breed jitter at materialisation | herd id, serial |
| 0x360 | crafting | failure roll at the critical step | crafter, job, step |
| 0x361 | crafting | byproduct chance | job |
| 0x362 | crafting | learning by watching | person, recipe |
| 0x370 | onboarding | start-site tie-break, mentor spot jitter | world seed, origin |
| 0x371 | onboarding | onboarding job parameters | new-game seed |
| 0x380 | autopilot | reserved, asserted never drawn | person |
| (none) | testers | draws no RNG | |

Collision check:
- **No id is claimed by two different specs for different purposes, and none lies in the Phase 2 range 0x200..0x2FF.** The only reuses are intentional: 0x223 and 0x2A0 are existing streams, and 0x300/0x301 are named by both live-behaviour (owner) and autopilot (consumer).
- **Shared ids that should be split:** 0x344 serves two unrelated things (sentry notice and camp night events) and 0x350 serves four (injury, shoe loss, lameness, taming). Same stream with different keys is legal, but a change to one roll order would shift the other. Recommended: give each its own id (0x346 camp night events, 0x353 shoe loss, 0x354 taming).
- **Key mismatch:** autopilot lists 0x301 keyed by person, live-behaviour keys it by job. Use live-behaviour's.
- **`tickKey` is inconsistent** (section 7, item 1): the Phase 2 rule is game hour index, but Phase 3 streams use `tick/20`, `tick/200`, game minute and hour.
- Free ids kept for growth: 0x303..0x30F, 0x316..0x31F, 0x323..0x32F, 0x333..0x33F, 0x346..0x34F, 0x353..0x35F, 0x363..0x36F, 0x372..0x37F, 0x381..0x3FF.

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
| Q18 | Crafting key | crafting, onboarding, trade | **N** opens crafting in the world context; N also toggles the notebook inside the trade screen (different context, so legal) |
| Q19 | New skills appended to the `Skill` enum | crafting (used by travel, clothing, trade) | Accept `Tailoring, Surgery, Speech, Athletics, Tactics, Stealth, Lockpicking`; save version bump and migration |
| Q20 | NPC wetness, frostbite, sunburn, butchering people | clothing | Abstract NPCs have no wet clothes; frostbite and sunburn wait for Phase 4 wounds; people are never butcherable (a taboo mod could change it) |
| Q21 | Hunting realism and camp decay | travel | Bow shots use the existing strike resolver with simple animal AI; camps decay after 3 game days |
| Q22 | Tester data: autosave, upload, expiry, log level, retention, screenshot | testers | Autosave on in tester builds with the file list shown and removable; no networking ever in Phase 3; 60-day build expiry with a warning at 45; `INFO` logs; keep reports until the tester deletes them; screenshot on |

## 7. Cross-spec inconsistencies and suggested fixes

1. **`tickKey` definition.** Phase 2 §2.3 says the key is the game hour index. Phase 3 uses `tick/20` (live-behaviour, horses 0x350/0x351, travel sentries), `tick/200` (forage), game minute (dialogue), hour (camp events) and "minute slot" (encounters). Because live ticks are not multiplied at 8x but game time is, **`tick/20` differs between a 1x and an 8x run** and would break the 8x invariance tests (H-3, V-2) and cross-speed replay.
   *Fix:* add to this overview and to `RngStreams.cs` one rule: Phase 3 live streams key on **game seconds** (`GameTime`) divided by a named window (20 s, 60 s, 200 s), never on the live tick counter. Rewrite the five specs' key columns accordingly.
2. **Control keys.** `InputDefs.cs` already binds **T** to `called_shot` (world and combat contexts) and **Z** to `sneak` (world). Travel and onboarding bind T to "travel here"; horses make Z toggle walk. Onboarding says it adds "N and T" and that CI rejects duplicates, so as written CI fails.
   *Fix:* "Travel here" is a map-click or Q-menu verb, not a key (drop T); the mounted context rebinds Z as `walk_toggle` only while mounted (a new `Mounted` input context). Keep N (free), Y (free; autopilot panel). Add all new keys to one table in `onboarding-v1.md` so the conflict test sees them.
3. **Interrupt kinds disagree.** Travel lists 14 kinds (`Hostile, Attacked, Weather, Cold, Heat, Hunger, Fatigue, Dusk, Horse, CartStuck, Lost/BlockedPath, Companion, Found, Arrived, Contract`) with values pause/notify/ignore. Autopilot lists 13 of its own with values Pause/Notify/Ai, says it "adds four", and counts "11 travel kinds" (omitting `Hunger`, `Contract`). Names differ: `Lost` vs `BlockedPath`, `Companion` vs `CompanionTrouble`, `Contract` vs `ContractOffer`, `Hunger` vs `LowFood`. `Attacked` and `Arrived` are in both lists.
   *Fix:* travel owns one `InterruptKind` enum (append only) and one policy type `Pause | Notify | Ai` (travel's `ignore` becomes `Notify` with no toast). Autopilot adds only `Threatened, Injured, LowMoney, BigChoice, DirectiveDone, LimitReached, NeedMoney, Stuck`; merge `Contract` and `ContractOffer`, `Companion` and `CompanionTrouble`, `Hunger` and `LowFood`. AP-7 counts from the enum.
4. **Recipe count.** `basic-crafting.md` says 34 recipes in the header, test K-1 and Q1, but the heading of section 4.1 says 33. *Fix:* count the table and correct the heading (or add the missing row).
5. **Party size.** Autopilot and clothing budget for "13 persons"; trade caps the party at 12 plus hero. These agree only if the hero is counted separately. *Fix:* state once in trade: "squad cap 12, plus the hero = 13 tracked persons", and reference it in the other two. Tracked persons for `SkillMicro` are "<= 64 per world" (trade) while clothing allows "<= 400 tracked rows live": different sets (skills versus core temperature); name them `SkillTracked` and `CoreTracked`.
6. **Hunger thresholds.** Travel: `Hunger` interrupt at 10 hours since the last meal, auto-eat. Live-behaviour: Eat beats a Work block at 13 hours (L-4). *Fix:* travel's auto-eat at 10 h is a convenience that fires first; document it as intentional in live-behaviour 4.1 and keep L-4.
7. **Live tick budget.** Live-behaviour claims 2.5 ms of an 8 ms tick; travel promises a 6 ms live tick at 8x; plan §2.11 gives 60 fps. Sum of Phase 3 live costs is about 3.6 ms (section 9), so the numbers fit, but only if nobody else adds. *Fix:* the overview budget table (section 9) is the single ledger; specs must not state their own share of the 8 ms.
8. **Inventory storage.** Trade moves `HeroRecord.Inventory` to item rows; clothing adds `WetPm` and `StainKind` to `ItemRow` and says the `Wet` flag "exists"; crafting and horses also add item and goods fields. Several columns are added to `ItemRow` and `PersonTable` without a combined list. *Fix:* one save-version bump listing all new columns in order: trade (`SkillMicro`, `AttrMilli`, `TrackedIdx`), live-behaviour (`FatiguePm`, `SocialPm`, `LastMealHour`, `Mood`, `Action`, `ActionSince`, `JobId`, `JobStep`), clothing (`CoreCenti`, `OutfitI`, `ExposureMin`, `WetPm`, `StainKind`), plus tables `JobTable`, `ReservationTable`, `RumourTable`, `HearingTable`, `RelationTable`, `AnimalTable`, `VehicleTable`, `CraftJobTable`, `AutopilotTable`, `SquadTable`, `LoanTable`. One migration, one round-trip test.
9. **Hash family.** Dialogue defines `TalkHash`, testers add `CraftHash`, clothing and horses define none. *Fix:* register `TalkHash, CraftHash, BehaviourHash (jobs and reservations), SurvivalHash (core, wetness), AnimalHash` together with the replay tooling (TR-10).
10. **Skill names.** Travel and autopilot use `Forestry`, `Archery`, `Butchery`, `Speech`, `Smithing`. The first three exist; `Speech` is among crafting's additions. Trade's "dictionary of 27 ids" becomes 34 after Q19; check `HeroRecord` migration (T-16) covers it.
11. **Tester numbers.** Plan §6 says 2 to 3 private testers. Onboarding's gate needs at least 5 testers over 3 origins, 4 of whom pass. *Fix:* keep the plan's 2 to 3 as the minimum for the phase close-out and treat 5 as a stretch goal; the gate becomes "every tester finishes steps 3 and 6, or the stuck points are written into the review".
12. **Scope edges.** Crafting K tests have no disassembly case (plan §6 requires one); horses have no slope case. Horse body plan is a stub in Phase 3 yet butchering horses is specified. Autopilot `Tend` and `PursueAmbition` are stubs. *Fix:* add K-19 (disassembly returns recorded components) and H-18 (slope speed); label the stubs in section 8.
13. **Phase 2 wording.** Live-behaviour, horses and travel refer to "overview E2/E9/O4/O10" and to "overview §3" of Phase 3 for the milestone P3-A. This file is that overview; P3-A is defined in section 3, step 1.

## 8. Risks

| Risk | Why it matters | Mitigation |
|---|---|---|
| **LODBridge has no code** (live-behaviour 2) | Every live system relies on promote and demote conserving state; it was specified in Phase 2 but never built | Build it first (P3-A) with the G-A4 and L-13 conservation tests; no other step merges before it passes |
| Scope: ten specs in 9 to 11 months | Plan overrun rule: more than 50% over means move features to the after-1.0 list | Cut order if late: feasts and fairs, hunting, the 40-hint list down to 12 (`minimal`), practice yard, rumour distortion, barks. Never cut LODBridge, jobs, onboarding script, replay |
| Two-tier divergence | Live is "theatre except leased work"; if the live and abstract paths differ, bugs show only at promotion | L-11 (identical hashes with and without the hero in the bubble), H-14, AP-1 equivalence, `ScheduleReplay` |
| Determinism leaks | Many new rolls keyed on live ticks, UI-thread hints, threaded logging | Section 7 item 1; counter-based streams only; replay tests TR-10 to TR-13 on both OSes; the reviewer's list in plan §6 |
| Autopilot exploits or bankrupts | A rule-following AI finds loopholes (buy-low sell-high, pay-wage loops) or starves the hero | Limits enforced twice (plan and barrier), AP-11 soak with 3 seeds, `LimitViolation` counter must be 0, conservation tests |
| Onboarding too hard or confusing | The plan's biggest unknown is "did you understand why?" | Safe start, loan horse, optional pin, tester metrics (O-14), hint levels; fair-weather shift; first 2 days free of ambush |
| Lethality surprise (cold, mud, fatigue) | Several specs add new ways to die or get stuck at the same time | Q1 defaults, HUD warnings, interrupts that pause, tester log of every death with cause |
| Content volume | 300 lines, 110 barks, 34 recipes, 40 hints, 24 encounters, 20 to 50 lines per mentor | Content scaled separately from systems (plan §7); validators in each spec reject missing keys; expansion pass each phase |
| Art and UI load | Many new screens: trade, inventory, character, crafting, autopilot, camp, report, hint toasts, overlay | One `PixelTheme`; reuse `ScreenRouter`; screens are thin views over snapshots (no state in nodes); autoplay screenshots in CI |
| Privacy of tester reports | Autosaves and screenshots are personal | Consent file, visible file list, redaction test TR-3, no networking test TR-8, tester build expiry |
| Save growth | Rumour hearings (up to 2 MB) and logs add to the save | Caps in each spec (section 9); the Phase 2 `SaveBudget` test extended with all new sections |
| Key map crowding | N, Y and mounted keys on top of ~50 existing | Fix per section 7 item 2; CI duplicate check |

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

- Every spec has golden tests (about 170 in all: AP 17, K 18, C 16, D 6, R 6, H 17, L 13, O 14, TR 19, T 16, V 16). New hash kinds join the daily hash trail so a replay finds the first divergence.
- Shared harness: the Phase 2 `HeadlessWorld`, extended with a scripted player bot (`--autoplay`) that uses only UI commands. The bot runs the onboarding script (O-4), a 30-day zero-player run (AP-10) and the nightly one-year soak.
- Conservation audits (coin through the ledger, goods through `MassLedger`, persons and animals by count) run in L-13, T-15, K-17, H-12, V-16 and must stay exact.
- At phase end: the review and health pass of plan §6, written up in `docs/reviews/phase-3.md`, and a 20 to 30 minute checklist session in `docs/playtests/`.
