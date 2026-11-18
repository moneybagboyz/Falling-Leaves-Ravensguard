# Onboarding v1: hints, controls overlay, practice yard, mentors, the safe start, the first odd job and the first ride

Plan refs: §3f (no tutorial level; learn in the real world during the first hour; the origin's first ambition step comes with a mentor NPC who gives a first small contract; "the game
won't start you within 10 km of an active war or within 3 km of a monster lair", the only softening; contextual hints with a "learn more" link into the Codex, off / minimal / full; controls always
visible, `?` opens the overlay for the current context; a practice yard with training dummies and sparring partners, skill gain capped; failure teaches; the Codex is the manual; onboarding is
playtested with private testers from Phase 3 and one question is always asked: "Did you understand why that happened?"), §2.8 (one key map per context, conflict-checked in CI; `?` is the controls
overlay), §3 item 11 (contracts and ambitions can be pinned by the player; the world never points at anything by itself), §3 item 1 (Quick Start origin cards with a starting ambition), §5 Phase 3
(onboarding v1: contextual hints, the controls overlay, the practice yard, mentor NPCs; done-when: "testers can get through the first ride and the first odd job without help"), §6 (fun checks: 2-3 private testers; `--autoplay` scripted playthrough).

## 1. Purpose
A new player, on the first day, in the real world, should find something to do, do it, get paid, ride a horse to another place and understand what happened and why, without reading a manual and without being
softened except by where the game places them. This file defines the hint system, the controls overlay, the practice yard, the mentors, the start-site rules and the exact script of the first hour that a tester (or a bot) is
measured against.

## 2. Dependencies
| Needs | From |
|---|---|
| Origins with `start_location` (village, town, road, manor, monastery, wilds), `ambition`, `mentor` ids (`old_knight`, `caravan_master`, `free_farmer`, `old_sergeant`, `prior`, `steward`, `fence`) | exists (`content/base/origins/origins.jsonc`, `OriginDef`) |
| `Region.IsSafeStart(x, y, warSafeM 10000, lairSafeM 3000)`, `NearestActiveWarM`, `NearestLairM`; `RegionSettings.WarSafeKm`, `LairSafeKm` | exists (`WorldGen/Region/Region.cs`) |
| Quick Start and embark flow (`QuickStartScreen`, `RegionBuildScreen`, `NewGameFlow`) | exists (`Game/UI/NewGame`) |
| `CodexRules` (`{@action.id}` key tokens resolved from the live bindings), `CodexBook`, `InputDefs`, `KeyMap`, `L.T/L.F` localisation | exists |
| Ambitions with typed conditions (`knows_person`, `has_item`, ...) | exists (`ambitions.jsonc`) |
| Jobs and boards, TendShop/Haul/Deliver jobs, pay and escrow | `live-behaviour-and-utility-ai.md` |
| Horses, loans, saddle, mounted controls | `horses-and-carts.md` |
| Unique NPC dialogue format (20-50 lines each) | `dialogue-barks-rumours.md` 3.4 |
| Trade screen, inventory, skill gain, recruits | `trade-inventory-character.md` |
| Travel hints and interrupts, auto-travel | `travel-encounters-camp.md` |
| Local logging, F8, `--autoplay` | `testers-and-crash-reports.md`, plan §6 |
New code: `Ravensguard.Sim.Onboarding` (`OnboardingScript`, `StartSiteFinder`, `LoanTable`), `Ravensguard.Game.Onboarding` (`HintSystem`, `HintToast`, `ControlsOverlay`, `OnboardingLog`); content `onboarding/hints.jsonc`, `onboarding/first_hour.jsonc`, `blueprints/practice_yard.jsonc`.

## 3. Data

### 3.1 Hint (`onboarding/hints.jsonc`)
```jsonc
{ "type": "hint", "id": "hint.mount", "level": "minimal", "trigger": { "event": "near_object", "args": { "kind": "rideable_animal", "within_m": "3" } },
  "when": [ { "id": "not_in_combat" }, { "id": "not_in_dialogue" } ],
  "text": "HINT_MOUNT",                       // en.csv: "Press {@world.interact} to mount. {@world.sprint} gallops; horses tire."
  "learn_more": "rule.horses", "cooldown_s": 0, "max_per_game": 1, "priority": 40 }
```
- **Trigger events** (the sim and the UI emit them; a closed list): `first_seen(kind)`, `near_object(kind, within_m)`, `need_above(need, pm)`, `item_acquired(shape)`, `weather_changed(state)`, `dialogue_opened`, `trade_opened`, `inventory_over(r_pm)`, `skill_gained`, `entered_settlement`, `left_settlement`, `job_board_seen`, `job_taken`, `job_done`, `mounted`, `cart_stuck`, `night_fell`, `camp_possible`, `rumour_heard`, `recruitable_nearby`, `wounded`, `interrupt(kind)`, `time_idle(real_s)`.
- **Display rules:** one toast at a time at the bottom centre (6 real seconds, fade 0.5 s); queue by `priority`, drop entries older than 20 s; **never during** combat, dialogue or a modal; a "learn more" link (key `{@menu.learn_more}`, default Enter or mouse click) opens the Codex page; each hint shows at most `max_per_game` times per **profile** (stored in the profile settings file, not the world save, so a new world does not re-teach what you know) and can be reset in Settings.
- **Levels:** `off` (nothing), `minimal` (12 essential hints: move, speeds, interact, talk, job board, inventory weight, hunger, cold, mount, sleep, save, report-bug), `full` (all about 40). **Default `full` for the first 60 game minutes of the first character on a profile, `minimal` afterwards**; the Settings screen sets it explicitly.
- **Key tokens** `{@action.id}` resolve against the **current bindings** (a rebound key shows correctly); text strings are localisable (`HINT_*`).

### 3.2 Hint list (Phase 3; 40, shown with trigger and level)
| Group | Hints (id: trigger) |
|---|---|
| Moving (min) | `move`: first 10 s of control; `speeds`: after 2 game hours at 1x and no changes (`1 2 3 4`); `interact`: near a person or object (`E` default, `Q` everything); `camera`: first zoom need |
| People (min) | `talk`: near a person for 3 s; `topics`: dialogue opened; `trade`: trade opened (price per unit, Prices tab `N`); `rumour_how`: first rumour heard ("How do you know?" in Journal `J`) |
| Work and money (min) | `job_board`: board seen or "any work" asked; `job_taken`: shows the step list in the Journal; `job_done`: pay arrives; `escrow`: the poster pays when done |
| Body (min) | `hunger`: `hunger` need > 600 permille ("eat from your pack, or buy bread"); `fatigue`: > 700 ("sleep in a bed: `{@world.interact}` on a bed"); `cold`: `cold_stress_pm` > 300; `wet`: first rain with cover wet; `heat`: first warm state |
| Inventory | `weight`: load ratio above 0.5 ("weight slows you above half capacity"); `container`: first container; `drop`: `{@world.interact}` on item on the ground |
| Crafting | `make`: first crafting station within 3 m (`N`); `unattended`: first unattended step ("it keeps going while you are away: the alert will tell you"); `light`: "too dark" |
| Travel | `autotravel`: first long path ("`T` travel here; the game stops when something happens"); `interrupts`: first interrupt; `dusk`: sunset in 60 min while on the road; `camp`: `camp_possible` first night; `forage`: forage ground found; `ford`: closed ford |
| Horses and carts | `mount` (min), `gait`: first gallop, `tired_horse`: stamina below 20%, `feed`: fodder below 1 day, `mud`: cart ratio above 90%, `stuck`: cart stuck ("push, unload, dig, add a team") |
| Squad | `recruit`: a recruitable person with familiarity >= 20, `squad_bar`: first follower, `wages`: first wage day |
| Safety | `save` (min): after 20 real minutes without a save in a non-Ironman run; `report_bug` (min): once ("`F8` reports a problem; nothing is sent without your consent"); `pause` after Esc; `controls`: after 5 minutes, "`?` shows the keys for this screen" |
Every hint has a Codex page (`rule.*`), generated from `CodexRules` where it is controls text and authored where it is a rule (cold, weight, horses, jobs).

### 3.3 Controls overlay (`?`)
`ControlsOverlay` builds from `InputDefs.All` filtered by the **current context** (World, CombatStance, CalledShot, Build, plus Phase 3's new contexts `Dialogue`, `Trade`, `Crafting`; other Phase 3 screens use `Menu`) and shows a table: action, key, mouse. It is a view of the same table that CI conflict-checks, so it cannot disagree with the bindings. A footer links to the Codex rule page (`rule.controls.<context>`). Opening it pauses the game in single-player (not in Ironman? It also pauses: the overlay is never a cheat), Esc or `?` closes.
New bindings in Phase 3 (all added with their context, duplicates rejected by CI): world `N` (make) and `T` (travel here from the map; `T` is also called shot in the combat-stance context, which is a different context, so no conflict; the CI check decides); `Z` stays sneak (sleeping is a Q menu verb on a bed); Dialogue `1-9`, `Enter`, `Esc`; Trade `Tab`, `N`, `Enter`, `Esc`, `Shift/Ctrl` quantity modifiers; Crafting `/`, `F`, `Enter`, `Esc`.

### 3.4 Practice yard (`blueprints/practice_yard.jsonc`)
A blueprint in the Phase 1 text format: a fenced 14 x 10 m yard, three training dummies (a wooden post with a pad: `training_dummy` furniture with material hit points), a weapon rack (blunted practice weapons: `practice_sword`, `practice_spear`, `practice_staff`, each `x0.5` momentum, edges counted as blunt, like tournament weapons §4A.3), a bench, a water trough, a notice board. Placed by settlement generation in every **town** (population >= 1,000) and at lord's manors with a retinue. A **trainer** NPC (occupation `Guard` or an `old_sergeant` mentor) stands in the yard from 08:00 to 16:00.
- **Dummy practice:** hitting a dummy is a meaningful use of the weapon's skill (`melee` or `archery` for the butts when Phase 4 adds ranged), `d = 40`, gain stops at skill 30 (`cap_level`; the dummy says "you have outgrown this"). Wind-up, strike window and recover show a timing bar (§4A.3: wind-up 0.30 to 0.90 s by weapon; strike window 0.10 to 0.15 s), the hit result prints a one-line reason ("too early", "good hit").
- **Sparring partner:** the trainer spars with blunted weapons: first to a knock-out or a yield at 3 clean hits (by the existing resolver spike `Combat/Melee`, damage x0.3, no bleeding, pain only); gain `d = 60`, same cap 30; free; a second fight is available after 5 game minutes (so it cannot be grinded at 8x). In Phase 3 this uses the Phase 0 melee spike model; **the full model replaces it in Phase 4** (risk R-7).
- The yard teaches blocking and parry timing (`parry` hint) but no permanent-injury risk.

### 3.5 Mentors and the first-hour script (`onboarding/first_hour.jsonc`)
Mentors are **unique NPCs** (authored dialogue, 20-50 lines each) placed by the landmark phase within 120 m of the start point, with these roles:
| Origin | Mentor | Start place | First contract (odd job) | First ride |
|---|---|---|---|---|
| Disgraced squire | `old_knight` | town | haul a sack of oats from the granary to the stable (Haul, 300 m) | borrows the knight's palfrey to carry a sealed letter to a village 3-6 km away |
| Peddler | `caravan_master` | road (his halted caravan) | guard the wagons for 2 game hours (Guard) or unload a cart (Haul) | rides a spare horse ahead to the next town to book the stall |
| Runaway serf | `free_farmer` | wilds (a hamlet within 1.5 km) | bring in a basket of apples (Fetch) | borrows a mule to take a cart load of apples to the nearest market |
| Discharged soldier | `old_sergeant` | town | stand a guard watch at the gate (Guard 2 h) | rides a messenger's horse to the neighbouring watch post |
| Monastery orphan | `prior` | monastery | carry a basket of loaves to the infirmary (Deliver) | takes the monastery mule with a manuscript satchel to a village |
| Minor heir | `steward` | manor | collect a rent measure from a tenant (Deliver) | rides the manor's horse to the lord's town house |
| Outlaw | `fence` | wilds (a hideout within 2 km) | haul a bundle of pelts to a camp (Haul) | borrows a donkey to take the pelts to a market (no crime is committed in Phase 3) |
The script (`OnboardingScript`) is a small state machine of `OnboardingStep`s with sim-visible triggers. The mentor says the step in dialogue; the Journal gets one entry per step; nothing is forced and every step can be done in any order after step 1.
| Step | Id | Done when | What helps |
|---|---|---|---|
| 1 | `meet_mentor` | dialogue with the mentor with `start` node finished | a toast "Press {@world.interact} on the person near you"; the Journal has a **Pin to map** button for the mentor (the game never points by itself) |
| 2 | `take_job` | `job_taken` of the mentor's job (or any board job) | hint `job_board`; the mentor says where the board or the innkeeper is |
| 3 | `finish_job` | `job_done`, wage received (>= 40 mites), coin visible in the purse | `job_taken` shows steps; `escrow` hint |
| 4 | `spend_coin` | any `Buy` or `Sell` through the trade screen | hint `trade` |
| 5 | `borrow_horse` | a `Loan` row created by the mentor (3.6) | mentor dialogue; `mount` hint when near |
| 6 | `first_ride` | arrive at the errand place by a path of at least 2 km while mounted for >= 60% of it | auto-travel hint, interrupts hint, gait hint, `tired_horse` if needed |
| 7 | `deliver` | errand completed (letter given, pay received) | |
| 8 | `first_night` | slept in a bed, an inn, or a camp for >= 6 game hours | `fatigue`, `camp` hints |
Reaching step 8 marks the **onboarding complete flag** in the profile (a quiet toast "You have found your feet. The Journal lists your ambition."). The ambition's first milestone (e.g. `find_a_patron`) is shown from step 1 so the player always sees what the game thinks they are after.
**The plan's softening is only placement.** The mentors give no gifts beyond the loan, the jobs pay the posted wage, and no hint ever solves a problem for the player.

### 3.6 `LoanTable`
`Loan { Id, Lender (person), Borrower (hero), Animal (AnimalTable id), FromDay, DueDay, ReturnPlace, State (Active, Returned, Overdue, Lost) }`. The mentor lends an animal (never a warhorse) for `loan_days = 3` game days; the animal keeps its owner, the loan is a permission flag so riding it is not theft. Returning it to the `ReturnPlace` ends the loan; keeping it past `DueDay + 3` makes the mentor angry (`Trust -300`, a rumour); losing it makes the hero owe its price (a `Debt` row in the ledger: Phase 6 collects; Phase 3 shows it in the Journal and the mentor refuses further help until paid).

## 4. Rules

### 4.1 Where a new game starts (`StartSiteFinder`)
- Input: the chosen embark region and the origin's `start_location`. Candidate cells are all settlement and site cells of the matching kind, in ascending cell index order.
- **Hard rule** (plan §3f, unchanged): `Region.IsSafeStart(x, y, 10,000 m, 3,000 m)` must hold at the start cell, both for wars active at the world's start date and lairs. A location that fails is skipped. If none of the kind passes, the finder falls back in this order: another kind (`village` for a `wilds` origin, `town` for a `manor`...), the next-nearest safe cell by distance, and finally reports a clear message at embark ("No safe start of this kind in the region; choose another region").
- **Start cluster rules** (soft, scored; all must hold in the chosen result or the finder tries the next candidate): a **job board or reeve or innkeeper within 400 m**, a **mentor spawn spot within 120 m**, a **stable or herd animal holder within 800 m** with at least one animal fit to lend (`TrainedPm >= 400`), a **road to a settlement 2-8 km away** that is not blocked by a ford closed at the start date, **fresh water within 300 m**, and a **bed (inn, or the household's own, or the monastery cell)** for the first night.
- **Start date:** the world's present date is fixed by history; the finder chooses a start **hour 08:00 on a day that shifts at most 10 days** from the nominal date to the first date with no thunderstorm, blizzard, heatwave or deep frost forecast in the first 24 game hours (weather is deterministic, so this is a pure search), season spring or summer or autumn by preference. If the player chooses Ironman with fixed date, no shift.
- **Safe days:** for the first 2 game days from the start: encounter scale 300 permille (travel spec) and ambush-type encounters disabled; no raid or war event can begin within 10 km (`war_safe_days`): the history tick reads this flag.

### 4.2 Hint engine
`HintSystem.Evaluate` runs at 4 Hz on the UI thread over the **published snapshot** (no game state in nodes, plan §2.1a); each hint has an event trigger (cheap) and optional conditions; the engine evaluates only hints whose trigger event fired in the last frame, so cost is O(events). It records `HintShown(id, realTime, gameTime)` in the onboarding log. Hints that the player has satisfied before the toast comes (they already pressed the key) are cancelled.
- **"Why" support:** after each onboarding step and after the first occurrence of a consequence the game explains (cold, hunger, ruined dough, stuck cart), the toast text names the cause in one line ("You are chilled because your clothes are wet and the wind is cold"); this feeds the question "did you understand why that happened?".

### 4.3 The first ride and first job in the sim
- **Job design for onboarding:** the mentor's job and the board job are ordinary jobs from the job system (poster = mentor's household, escrow posted at world start), so all job rules, pay and failure are real. The first job always has `Slots 1`, `Deadline` 12 game hours, a distance of 100-500 m and carries ≤ 15 kg.
- **Loan and ride:** the errand is a `Deliver` job over 2-6 km on a road; the lent animal is saddled; a **safe-ride corridor** reduces danger (`danger_pm = 100`) on that road for 2 game days. Fords on the route are checked at placement.
- **If the animal dies or runs off** the mentor offers a mule or tells the player to walk (steps stay completable by foot: `first_ride` requires `mounted >= 60%` only if an animal was lent; otherwise it is skipped with a note).
- **Stuck detector:** if a step shows no progress for 8 real minutes with the Journal open or the player idle, the mentor's `reminder` line is used on the next talk and the Journal text expands ("Where to go: the stable at the east end of the square; the stable hand will lend you a horse") but never marks the map.

### 4.4 Tick placement
| Where | What |
|---|---|
| New game creation (before the first frame) | `StartSiteFinder`, mentor placement, job posting, loan stock selection |
| Live 20 Hz | none (the script reacts to events; the checks cost O(1) per tick) |
| UI thread 4 Hz | `HintSystem.Evaluate`; toast animation per frame |
| Barrier | `OnboardingStep` transitions as commands; `Loan` state; job pay |
| H5 hour 0 | overdue loan check; war-safe flag expiry |
The onboarding script runs on the sim snapshot and is part of the save (`onboarding` section: step states, loan rows); hint "seen" flags live in the profile file.

## 5. Determinism
| Stream | Use | Key |
|---|---|---|
| 0x370 | start-site tie-break among equally scored candidates; mentor spawn spot jitter | world seed, origin |
| 0x371 | onboarding job parameters (which sack, which route) | new-game seed |
The finder iterates cells in ascending index order; the start date search is a pure function of the seed; the same world and origin give the same start. The hint engine has no randomness.

## 6. UI and controls
- Toasts bottom centre; a **"?" button** top right always visible in the HUD (mouse alternative to `?`), a hint level selector in Settings (Off, Minimal, Full) and "Reset hints". The Journal (`J`) shows the first-hour list as "Getting started" with steps ticked off, hidden once complete.
- The Codex (`K`) lists rule pages for hints; "learn more" opens the right page. The mentor pin button is **optional**: the Journal says "Pin to map?" and the player chooses.
- Esc closes any panel (✕ on every panel). Hold `R` for the order wheel is introduced only when a squad exists.

## 7. Tests (golden)
| id | Case | Expected |
|---|---|---|
| O-1 | Start rule: 10,000 random embark cells in 20 worlds, each origin | **0** starts within 10,000 m of an active war or 3,000 m of a lair; fallback order exercised |
| O-2 | Start cluster: every origin in 5 worlds | board or innkeeper <= 400 m, mentor <= 120 m, loanable animal <= 800 m, a 2-8 km road, water <= 300 m, a bed; otherwise the finder rejects the site |
| O-3 | Start date: seed with a thunderstorm on nominal day 12 | shifts to the first mild day within 10 days; deterministic across OS |
| O-4 | `--autoplay` bot: for each of the 7 origins (3 seeds each), uses only UI commands and hints | steps 1-8 done, first job < 12 real minutes of 1x play (or the equivalent game time at 4x), first ride arrives, no step stuck, no exception; the bot never uses dev commands |
| O-5 | First job pay: poster escrow 71 mites | worker +71, escrow 0, mentor household -71, purse visible |
| O-6 | Loan: ride the animal 4 km, return | loan `Returned`, animal at the stable, no theft flag, mentor trust +50 |
| O-7 | Loan overdue and loss | overdue after due + 3 days: `Trust -300`, rumour; loss: debt row equals the animal's `price_mites` |
| O-8 | Hint engine: scripted run triggers each of the 40 hints once | each shows once per profile, never in combat or dialogue, key tokens resolved with a rebound key (`E` rebound to `F`) |
| O-9 | Hint levels | `off`: 0 toasts in an 1-hour scripted run; `minimal`: only the 12; `full`: all that triggered |
| O-10 | Controls overlay | for each context, the shown actions equal `InputDefs.All` of that context; duplicates in a context fail CI |
| O-11 | Practice yard: dummy at skill 28 and 30 | gains at 28 (`d = 40`), none at 30; sparring second fight refused within 5 game minutes |
| O-12 | Mentor dialogue validation | each mentor has 20-50 lines, every `goto` reachable, every key in `en.csv`, effects in the vocabulary |
| O-13 | Save and load mid-script | step states and loan rows restored; hint seen flags unaffected |
| O-14 | Tester measure (manual, `docs/playtests/`) | per tester: time to step 3, time to step 6, number of times the `?` overlay or Codex was opened, number of "stuck" markers, answer to "Did you understand why that happened?" |
**Done-when metric for testers (Phase 3):** of at least 5 testers over at least 3 origins, 4 or more complete steps 3 and 6 with no observer help, with at most 1 stuck marker each.

## 8. Open questions for the user
1. **Default hint level:** `full` for the first hour, then `minimal`. Alternative: always `minimal`. Default as written.
2. **Mentor location help:** the game offers a "Pin to map" button in the Journal for the mentor but never marks it unasked (plan §3 item 11). Alternative: a one-time forced marker for the very first step. Default optional pin.
3. **The loaned horse:** loan of 3 game days (about 3.6 real hours at 1x). Alternative: a gift of a poor mule. Default loan.
4. **Fair-weather start:** shift the start by up to 10 days to avoid storms in the first 24 hours. Alternative: no shift, brutal as designed. Default shift (it is placement, like the war and lair rule).
5. **Practice yard before Phase 4:** uses the Phase 0 melee spike. Default accept; the plan places the yard in Phase 3.
6. **New key N and T:** conflict-check in CI. Default as listed.

## 9. Budgets
| Item | Budget |
|---|---|
| `StartSiteFinder` | <= 150 ms for a region (runs once, on a worker, in the region build screen) |
| `HintSystem` | <= 0.02 ms per frame (event driven); toasts allocate nothing per frame |
| Overlay build | <= 2 ms |
| Content | hints.jsonc <= 40 KB, `en.csv` Phase 3 onboarding additions <= 60 KB |
| Save | `onboarding` section < 5 KB |
