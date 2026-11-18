# Live behaviour, utility AI, jobs and reservations

Plan refs: §2.5 (utility AI for needs and schedules; one job and reservation system for NPCs, your people and you: haul, build, craft,
farm, guard, tend shop, escort), §2.3 (LODBridge, promote 150 m, demote 200 m, "carried items, jobs and paths survive demotion as
abstract job state"), §2.2a (levels, portals), §5 Phase 3 (done-when: "residents keep schedules across floors"), §4A.1 (survive, buffer,
profit), §3c (day length sets work hours). Builds on Phase 2 `households-and-needs.md` 3.5, `abstract-tick-and-lodbridge.md` 2.3, 2.4, 3.4,
3.5, 3.6.

## 1. Purpose
Make the people in the bubble behave like the people in the records. One scoring function decides what a person does next. It runs in the
live tier at about 1 Hz per person and in the abstract tier at schedule boundaries, and it gives the same answer for the same inputs. A
single job and reservation system lets any person (an NPC, a party member, the hero on autopilot) take work, hold the things the work
needs, and finish it, whichever tier is running at the time.

## 2. Dependencies
| Needs | From |
|---|---|
| `PersonTable` (`Bed`, `HomeSettlement`, `HomeLot`, `Occupation`, `WorkSettlement`, `WorkLot`, `Skills`, `Attr`), `HouseholdTable.Beds` | Phase 1 / 2 (exists) |
| `AbstractJob`, `PathPlan`, `PositionOf(person, t)`, default schedule JSON | Phase 2 abstract spec 2.3, 2.4, 3.4 (specified; **not built**) |
| **LODBridge** (`LiveBinding`, promotion queue, fold-back) | Phase 2 abstract spec 3.5, 3.6. **No code exists** (`grep LiveBinding` finds nothing). Building it is milestone P3-A of this phase (overview §3) |
| `LevelGrid`, `LevelPathfinder` (portals), `LiveWorld.Step` 20 Hz | exists (`src/Ravensguard.Sim/World`, `Live`) |
| `LabourMarket`, `Business`, `ProductionSystem`, `FarmingSystem.Labour` | exists (Phase 2); live work leases a station or farm task (abstract 3.5 "later phases") |
| `EconCommand` queue, `Ledger` | exists; used for wages and escrow |
| Weather `Daylight`, `RegionWeather.At` | exists |

New in this file: `Ravensguard.Sim.Behaviour` (`UtilityBrain`, `NeedState`, `ScheduleResolver`), `Ravensguard.Sim.Jobs` (`JobTable`,
`ReservationTable`, `OddJobBoard`, `JobRunner`), ECS components `Intent`, `JobClaim`, `Sleeping`; content files `behaviour/utility.jsonc`,
`behaviour/schedules.jsonc`, `jobs/job_kinds.jsonc`.

## 3. Data

### 3.1 New `PersonTable` columns (all saved; defaults in brackets)
| Column | Type | Unit | Meaning |
|---|---|---|---|
| `FatiguePm` | `ushort` | permille, 0 rested .. 1000 exhausted [200] | rises while awake, falls asleep |
| `SocialPm` | `ushort` | permille, 0 satisfied .. 1000 lonely [300] | rises with time alone, falls in talk |
| `LastMealHour` | `int` | game hour index | feeds the hunger curve (abstract `NutritionCd` stays the long-term reserve) |
| `Mood` | `sbyte` | -100..+100 [0] | derived daily from needs, clothing, housing; used by dialogue and autopilot |
| `Action` | `ActionKind` (byte) | enum | what the person is doing now (both tiers) |
| `ActionSince` | `int` | game minutes since day 0 | for commitment and display |
| `JobId` | `EntityId` | | the claimed `Job` (`IdKind.Contract`), `None` for the schedule |
| `JobStep` | `byte` | | step inside the job (3.6) |

`ActionKind`: `Sleep, Eat, Drink, Work, Commute, Shop, Socialise, Worship, Shelter, Flee, Rest, Idle, Travel, JobStep` (append only).

### 3.2 `Job` record (`JobTable`, key `EntityId` kind `Contract`, region-keyed ids per overview E2)
| Field | Type | Unit | Meaning |
|---|---|---|---|
| `Kind` | `JobKind` | enum | `Haul, Guard, Escort, TendShop, FarmWork, Craft, Deliver, Fetch` (`Build` arrives in Phase 5, append only) |
| `Poster` | `EntityId` | person / household / business / faction | pays the wage |
| `Settlement` | int | | board the job is listed on |
| `Slots` | byte | people | how many may claim it |
| `Params` | up to 6 `long` | per kind (3.6) | places, goods, grams, hours |
| `WageMites` | `long` | mites total, not per hour | fixed price, escrowed at posting |
| `EscrowAccount` | int | | the job's escrow account in the ledger (money spec accounts) |
| `PostedHour`, `DeadlineHour` | `long` | game hour index | |
| `State` | enum | `Open, Claimed, Active, Done, Failed, Expired, Cancelled` | |
| `MinSkill`, `MinAge` | byte | skill 0..100; years | filter |

### 3.3 `Reservation` (`ReservationTable`)
`Key` = `(ReserveKind, ulong id)` where kinds are `Bed, Tile, Station, Item, Stock, Cart, Animal, Seat`. Value = `(holder person index,
jobId, expiresGameSec)`. Expiry is in game seconds, not sim ticks (one tick is 1 game second at 1x and 8 at 8x), so a reservation lapses at the same game time at any speed.

### 3.4 Content: `content/base/behaviour/utility.jsonc`
```jsonc
{ "type": "utility_profile", "id": "default",
  "decision_period_ticks": 20,           // 1 Hz real at 1x; staggered by personIdx % 20
  "commit_bonus_pm": 150, "noise_pm": 20, "schedule_bonus_pm": 400,
  "needs": {
    "hunger_eat_hours": 13,              // the single auto-eat threshold, shared with travel and autopilot
    "hunger":  { "from": "hours_since_meal", "curve_pm": [[0,0],[4,100],[8,400],[12,800],[14,1000],[16,1300]] },
    "fatigue": { "from": "fatigue_pm",       "curve_pm": [[0,0],[500,100],[800,500],[1000,1000]] },
    "social":  { "from": "social_pm",        "curve_pm": [[0,0],[600,150],[1000,600]] },
    "warmth":  { "from": "cold_stress_pm",   "curve_pm": [[0,0],[300,200],[700,800],[1000,1000]] },
    "safety":  { "from": "threat_pm",        "curve_pm": [[0,0],[200,300],[600,900],[1000,1000]] } },
  "duty_pm": 500,
  "actions": [
    { "kind": "Eat",    "weights": { "hunger": 1000 }, "needs_place": "food_source" },
    { "kind": "Sleep",  "weights": { "fatigue": 1000 }, "needs_place": "bed", "min_dark_or_fatigue_pm": 700 },
    { "kind": "Shelter","weights": { "warmth": 600, "safety": 400 }, "needs_place": "roof" },
    { "kind": "Flee",   "weights": { "safety": 1000 }, "needs_place": "away_from_threat" },
    { "kind": "Socialise","weights": { "social": 800 }, "needs_place": "tavern_or_square" },
    { "kind": "Work",   "weights": { "duty_pm": 1000 }, "needs_place": "workplace" } ] }
```
Every numeric key carries a unit suffix (`_pm` permille, `_ticks`, `_hours`); the content loader rejects bare rates (overview E6).

### 3.5 Content: `content/base/behaviour/schedules.jsonc` (extends Phase 2 `default_day`)
```jsonc
{ "type": "schedule", "id": "outdoor_worker",
  "anchors": { "wake": "sunrise-1:30", "work_from": "sunrise-0:30", "work_to": "min(sunset+0:30, work_from+11:00)", "sleep_from": "wake-8:30" },
  "blocks": [ { "from": "wake", "to": "work_from", "kind": "Eat", "place": "home" },
              { "from": "work_from", "to": "work_to", "kind": "Work", "place": "work", "noon_rest": "heatwave" },
              { "from": "work_to", "to": "work_to+1:00", "kind": "Shop", "place": "market" },
              { "from": "work_to+1:00", "to": "sleep_from", "kind": "Home", "place": "home_or_tavern" },
              { "from": "sleep_from", "to": "wake", "kind": "Sleep", "place": "bed" } ],
  "holy_day": { "from": "09:00", "to": "11:00", "kind": "Worship" } }
```
Anchor grammar: `sunrise`, `sunset`, `noon`, `HH:MM`, plus or minus `H:MM`, `min(...)`, `max(...)`. Evaluated once per game day per person from
`Daylight.SunriseMinute/SunsetMinute(latitudeTenths, dayOfYear)` (integer minutes).

## 4. Rules

### 4.1 Needs
| Need | Update | Rate (unit) |
|---|---|---|
| Fatigue | awake: `+62` permille per game hour (1000 over 16 h); asleep in a bed: `-125` per game hour (empty in 8 h); asleep on ground without bedroll: `-75`; resting awake: `-20` | `FatiguePm` is updated by the abstract daily slice for abstract people (hourly by `Action`), and by the live tier every 20 ticks for live people; same constants |
| Social | alone: `+25` per game hour; in conversation or at a tavern table: `-150` per game hour | |
| Hunger | `hours_since_meal` from `LastMealHour`; eating itself stays in `HouseholdNeeds` (abstract) and in the Eat action (live) | the abstract daily slice remains the only writer of `NutritionCd`. **The one auto-eat threshold is `hunger_eat_hours = 13`** (in `utility.jsonc`): from that hour a person with food at hand eats, because Eat scores 900 and ties a Work block. The travel `Hunger` interrupt, camp meals and autopilot read this same constant; no spec keeps its own number |
| Warmth | `cold_stress_pm` comes from `clothing-warmth-exposure.md` (insulation against ambient) | |
| Safety | `threat_pm` = sum over hostile or armed-unknown persons within 25 m, weighted by the Phase 4 threat rules; Phase 3: bandits, wild animals, fire within 15 m | |
Live people read nothing from the economy tables directly: they read `NeedState` filled from the person's columns and the last published snapshot.

### 4.2 Scoring and choice (one function, two cadences)
```
score(a) = (sum_i w_ai * curve_i(x_i)) / (sum_i w_ai)            // permille, integer, curves are piecewise-linear tables
         + schedule_bonus_pm          if a matches the current schedule block
         + commit_bonus_pm            if a == current Action and now - ActionSince < commit_min(a)
         + noise                      in [-noise_pm, +noise_pm], DetRng stream 0x300, key (GameSec / 20, personId, draw = actionIndex)
         * gate(a)                    0 if the place the action needs does not exist (no bed, no food, closed workplace), else 1
best = argmax score, ties by lower ActionKind then lower place id
```
- `commit_min`: Work 30 game minutes, Sleep 60, Eat 10, Socialise 20, others 5.
- **Schedule vs need:** a scheduled Work block scores `duty 500 + schedule 400 = 900`, and 1,050 once committed. Hunger (0 at a meal, 800 at 12 h, 1,000 at 14 h, 1,300 at 16 h) therefore wins an uncommitted choice at 13 h and a committed one at about 14.5 h: people work through a late meal and break off when truly hungry. Exhaustion above 800 permille overrides any non-Flee action. Flee beats everything once `threat_pm` is above 600.
- **Duty** (`duty_pm`): 500 when the schedule block says Work and the workplace is open, 0 otherwise. The job system sets it to 500 for the claimed job's current step (jobs add the same 400 schedule bonus).
- **Cadence.** Live: every `decision_period_ticks = 20` ticks (1 s of real time at 1x), staggered by `personIdx % 20`. Abstract: at the start of each schedule block and once per game hour in the daily slice (`index % 24`, overview E4). The inputs differ only in how fresh they are; the function and tables are identical, so a person promoted into the bubble does not "change character".
- **Abstract shortcut:** an abstract person on the default schedule does not run the full scorer except at block boundaries; the scorer is skipped when `Action` equals the block's kind and no need is above 700 permille.
- **Explanation string** (for UI and tests): the top two contributors, for example `"Work (schedule +400), Eat 620 (hungry 12 h)"`, built only when a UI or inspector asks for it.

### 4.3 Schedules by occupation, season and day length
`ScheduleResolver.For(person, GameTime)` returns the day's blocks as minute intervals. Rules:
| Rule | Value |
|---|---|
| Outdoor workers (farmer, ploughman, shepherd, fisher, woodcutter, miner at the surface, charcoal burner) | work from `sunrise - 0:30` to `min(sunset + 0:30, start + 11 h)`; so a 16 h summer day gives 11 h of work, a 8 h winter day gives 9 h |
| Indoor trades (smith, baker, brewer, weaver, cobbler, mason, carpenter, butcher, tanner) | default 06:00 to 16:00; in winter start moves to `max(06:00, sunrise)` and end to `min(18:00, start + 10 h)` (light for work: no work below the recipe's light minimum) |
| Merchants | 06:00 to 16:00 at the market (market hours stay 06:00 to 18:00, Phase 2 H3) |
| Guards | 3 shifts of 8 h (00:00, 08:00, 16:00 start), rotation by `personIdx % 3` fixed per season |
| Innkeepers and servants | 08:00 to 24:00 |
| Heatwave (weather state) | outdoor workers take a rest block 12:00 to 15:00 |
| Rain (heavy) / thunderstorm / blizzard | outdoor work is suspended unless the job has a deadline; they `Shelter` |
| Holy days | 09:00 to 11:00 everyone `Worship` (Phase 2: day of month 5) |
| Feast days | work block shortened to 13:00; evening `Socialise` at the square (`travel-encounters-camp.md` 4.7) |
| Sleep | 8.5 h before `wake`; winter (day length < 10 h): +1 h |
Commutes remain Phase 2 O10 (5 m/s real, max 1.35 km, field camps beyond). `jitter` of the arrival spot in a lot uses stream 0x2A0 (unchanged).

### 4.4 Sleeping in beds (Phase 1 dwellings)
- A person owns a bed by `(HomeSettlement, HomeLot, Bed)`. `Bed` is an index `0..HouseholdTable.Beds-1`.
- **Bed cell resolution:** the dwelling's `build_piece`s tagged `bed` (`bed` 1, `bunk` 2, `straw_pallet` 1; `pieces.jsonc`), listed in ascending `(z, y, x)` order, with a bunk counted twice (lower bunk first). The person's bed is entry `Bed`; if the list is shorter than `Beds` the remaining people sleep on the floor of the sleeping room (the room tagged `sleeping` by `RoomDetector`) at stable spots `hash(personId) % free cells`.
- **Claim:** at bedtime the person reserves `(Bed, bedCellId)` for the night (TTL 10 h). Visitors, lodgers and the homeless claim free beds by the same table: free inn beds, then a barn floor spot. A reserved bed is never taken by another NPC; the hero may use any unoccupied bed in a place he owns or rents, or an inn bed he pays for (`inn_bed_mites_per_night = 25`, a posted price per market). Using someone else's bed uncalled is trespass (Phase 6 crime; Phase 3 only blocks it with a prompt "This is Aldric's bed").
- **Sleeping state:** component `Sleeping { Bed cell, SinceTick }`; the person is lying (pose), invisible to nameplate by default, wakes at `wake` or when `threat_pm > 300`, noise within 6 m (a fight), or fire within 12 m. The woken person scores Flee or Shelter.
- **Multi-floor:** bed on z = 1 is reached through the portal graph; the schedule is satisfied only when the person is in the bed cell. A resident on z = 1 who finishes work walks home, climbs the stairs and lies down; the test (7) checks 100 residents over 3 game days.
- **Hero sleep:** Q on a bed ("Sleep until dawn / N hours") opens a time picker; sleeping is 8x-capable only if no hostile is within 60 m; fatigue and wounds heal at the sleeping rates. Sleeping rough (no bed) is possible and costs 40% of the fatigue recovery and exposure (clothing spec).

### 4.5 Jobs and reservations
**Lifecycle:** `Open -> Claimed -> Active -> Done | Failed | Expired | Cancelled`.
- **Posting** (a household, business, lord, innkeeper, reeve, or the hero) escrows `WageMites` from the poster's account into a job escrow account (`Ledger.Transfer`, reason `JobEscrow`). Coin is never created: payment is the escrow moved to the worker (`JobPay`); an expired or cancelled job returns the escrow (`JobRefund`).
- **Boards:** one `OddJobBoard` per settlement with a market; jobs are visible at the board (a post in the square), from the reeve and the innkeeper by talk, and on arrival at a hiring fair. Board size cap 24 open jobs.
- **Claiming:** a claim is a command `ClaimJob(jobId, person)` applied at the next tick barrier in ascending `(jobId, personIdx)` order; a person may hold one job; the first valid claim wins. NPC claim: the claim is a scored action `TakeJob` whose utility is `expected_wage_per_hour / best_alternative_per_hour` mapped through a curve (Survive tier doubles it; autopilot directives bias it, `autopilot-v1.md`).
- **Reserving:** while a job is `Claimed` the runner reserves what it needs (the stock lot, the cart, the animal, the station, the tile), each with TTL 1 game hour, refreshed while the step runs. If a reservation lapses the step restarts from the previous safe point.
- **Failure:** timeout past `DeadlineHour + grace_hours (2)`, the worker dies, the goods are lost, or the worker quits. The poster keeps the escrow minus what was earned pro rata for Haul and Guard (paid per completed hour); Escort and Deliver pay all or nothing. A worker who abandons a claimed job takes a `Reliability` reputation tick (-3, reputation system Phase 6, stored as `Mood` hint for the poster now).

### 4.6 Job kinds (Phase 3 set)
| Kind | Params | Steps (`JobStep`) | Pay formula (mites) | Abstract equivalent |
|---|---|---|---|---|
| Haul | from place, to place, good, grams | 0 go to source, 1 load (1 s per 2 kg at hero speed; NPC same), 2 carry, 3 unload, 4 report | `ceil(1.10 x W_u/10 x hours_est)` plus `5` per started 100 kg-km; `hours_est` = path m / 900 (per game hour, Phase 2 commute pace) x 2 + 0.25 | `Haul` abstract job already in `AbstractJob.Kind`; completes at `StartHour + hours_est` |
| Guard | post cell, shift hours | 0 go to post, 1 stand watch (scan every 2 s), 2 hand over | `ceil(1.25 x W_u/10 x hours)` (1.25 for risk) | `GuardCaravan`/`Work` |
| Escort | person or caravan id, destination | 0 meet, 1 follow within 30 m, 2 arrive | `ceil(1.50 x W_u/10 x trip hours) + deposit return`; deposit 10% of wage held by worker | caravan guard slot |
| TendShop | business id, stall, hours | 0 go, 1 open (set prices to the business ask), 2 serve customers (sales via `Sell` commands at the business book), 3 close and hand over takings | `ceil(1.10 x W_s/10 x hours)` where `W_s` is skilled wage if Trading >= 30 else `W_u`; takings belong to the business (conserved) | merchant staff slot |
| FarmWork | parcel/field task id (plough, sow, weed, harvest, thresh) | 0 go to tile, 1 work (hours credited per live second), 2 stand down | farm task wage class (`Labour` file); harvest premium as in Phase 2 | `FarmingSystem.Labour` worker-hours |
| Deliver | item id or stock, to person | 0 take, 1 go, 2 hand over | `ceil(1.10 x W_u/10 x hours_est)` | `Haul` small |
| Fetch | good (water, firewood), grams | 0 go, 1 gather/draw, 2 return | as Haul | none |
| Craft | recipe id, batch (commission) | handled by `basic-crafting.md` | agreed price | production recipe |
`W_u` = the settlement's `unskilled` wage in mites per game day (Phase 2 wage classes: unskilled 15 mites/h = 150/day, skilled 35 = 350/day, master 50); `W_u/10` is per worked hour (10 h day). Golden numbers use `W_u = 150`.

**Live work credit (FarmWork, TendShop, station use).** A live person on a leased task generates worker-hours at the Phase 2 rate: one real second at 1x = 20 game s = 1/180 worker-hour. Credit is accumulated in `ProgressPpm` and emitted as `WorkDone(taskId, workerHoursX1000)` once per real 10 s, applied at the next barrier; the abstract tick skips the leased task for that person (`LiveLeased`), so work is never counted twice (abstract 3.5). Efficiency uses the same `work_efficiency_pm(skill, tool, fatigue)` as the abstract tick: `1000 - max(0, 50 - skill) x 6` permille, `x 500/1000` without a tool, `x (1000 - FatiguePm/4)/1000`.

### 4.7 Promotion and demotion of job state
| State when promoted | Live behaviour | Fold-back on demotion |
|---|---|---|
| `Work` at a station | person walks to the station, animates work; station leased | `WorkDone` already emitted; job stays `Work`, `ProgressPpm` = live task progress |
| `Commute` / `Travel` | spawned at `PathPlan.PositionAt(now)` on the same path | derive progress from the nearest point on the path (error <= 1 m) |
| Haul job step 2 (carrying) | carrier appears with the goods in `Pack`; goods visible as a sack/bundle | `JobStep` and `Pack` unchanged; the abstract job continues with `PathPlan` re-planned from the live position |
| Guard step 1 | standing at post | `Work` at the post with `StartHour` kept |
| Escort | follows the escorted person's `PathPlan` | if the live distance exceeds 30 m for 60 s, `Failed` |
| Sleep | lying in the bed cell | abstract `Sleep`, same fatigue rate |
| Displaced (fled, pushed) | | `Travel` to the nearest schedule place (abstract 3.6) |
Conservation: the person's `Pack` stock rows, reserved stock lots, the job escrow and the carried coin are rows, never copies, so promote then demote is the identity on coin, goods mass and person count (test L-13).

### 4.8 Tick placement
| Where | What |
|---|---|
| Live 20 Hz | movement, `UtilityBrain` for 1/20 of live people per tick, `JobRunner` steps, reservation TTL checks, `WorkDone` accumulation |
| Barrier B(h) | apply `ClaimJob`, `WorkDone`, `Post/CancelJob`, wage and escrow commands in `(Frame, Seq)` order |
| H2 | abstract `Work` credit skips `LiveLeased` people |
| H4 slice | `NeedState` update, scorer at the person's slice hour for abstract people |
| H5 hour 0 | board refresh: expire jobs, post routine jobs (3 per 100 households per day, by `Poster` priority) |
| 2 Hz bubble manager | promotion and demotion (Phase 2) |

## 5. Determinism
New streams (proposed; overview of Phase 3 table, range 0x300..0x3FF):
| Stream | Use | Key |
|---|---|---|
| 0x300 | utility noise and ties | `GameSec / 20`, person, `draw = actionIndex` |
| 0x301 | job claim tie-break (same hour, two claimants) | game hour, **job**, `draw = claimant person index` |
| 0x302 | bed fallback spot | game day, person |
| 0x303 | routine job generation | game day, settlement |
Live decisions are keyed by `tickKey = GameSec / 20` (a 20-game-second window, never the live tick counter, so 1x and 8x draw the same values; overview section 5) and a person's choice does not depend on the stagger offset. **0x301 is keyed by the job, not the person**: a claim is a contest for one job, so each claimant's rank is `Draw(0x301, hour, jobId, claimantIdx)` and the lowest rank wins. The result does not depend on how many people claim or in what order they are processed; autopilot claims use the same key; processing order is ascending person index; claims are resolved at barriers, never in thread order. No `float`.

## 6. UI and controls
- **Q on a person** (everything you can do): "What are you doing?" shows the explanation string and the job. **E** on the innkeeper or reeve: "Any work?" opens the job list. A board (E on a post) lists up to 24 jobs with pay, distance, deadline and a risk tag.
- **Job log** in the Journal (J): active job, step, deadline, pay, "abandon" (with the penalty text).
- Time widget shows `Work/Sleep` of the selected person on hover. The inspector (F12, dev) shows needs, last scores and reservations.
- Hero sleep: Q on a bed "Sleep (until dawn / N hours)".

## 7. Tests (golden)
| id | Case | Expected |
|---|---|---|
| L-1 | Lat 480, day 23 (midsummer): sunrise 03:59, sunset 20:01 (day 16:03). Outdoor worker | wake 02:29, work 03:29 to 14:29 (11 h cap), sleep from 17:59 |
| L-2 | Lat 480, day 53 (midwinter): sunrise 08:02, sunset 15:58 (7:57) | work 07:32 to 16:28 (8:56), wake 06:32, sleep from 21:02 (8.5 h + 1 h winter) |
| L-3 | Equinox (day 8), 720 min | sunrise 06:00, sunset 18:00, wake 04:30, work 05:30 to 16:30 (11 h cap), sleep from 20:00 |
| L-4 | Scoring, noise 0, Work block active (duty 500 + 400 schedule = 900), not committed; `hunger_eat_hours` = 13 | hunger 12 h: Eat 800 < 900, keeps Work; 13 h: 900 = 900, ties go to the need-driven action, Eat; committed (1,050): Eat wins from 14.5 h |
| L-5 | Hysteresis: Work started 10 min ago (900 + 150 = 1,050), Socialise 1,000 | stays Work; at 31 min the bonus is gone, Work 900 < 1,000 and Socialise wins |
| L-6 | Beds: dwelling with `bed`, `bunk` (2), `pallet`; `Beds = 4` | cells assigned 0: bed, 1-2: bunk lower/upper, 3: pallet, stable across runs |
| L-7 | Cross-floor: 100 residents, bed at z = 1, work at z = 0, 3 game days | every resident in bed cell in the sleep block on each night, in workplace within 5 min of block start; 0 stuck |
| L-8 | Escrow: post Haul wage 63 mites, worker completes | poster -63, worker +63, escrow 0; cancelled before claim: poster net 0 |
| L-9 | Haul wage: `W_u = 150`, 1,800 m path, 60 kg (no per-100kg-km term below 100 kg-km) | `hours_est = 1800/900 x 2 + 0.25 = 4.25`; pay `ceil(1.10 x 15 x 4.25) = 71` mites |
| L-10 | Claim race: two claimants frame 10 seq 1 and 2 | seq 1 wins on any thread order, 100 runs |
| L-13 | Promote then demote 130 workers mid-haul (Phase 2 G-A4 spread) | coin, goods mass and person count identical; each person's `JobStep` unchanged; position error vs `PathPlan` <= 1 m |
| L-11 | 1 game week, bubble of 300, with and without the hero in the bubble | abstract-only run and live-assisted run have identical coin, stock and vital hashes (live is theatre except leased work) |
| L-12 | Determinism | same seed, same stagger shuffle of promote order: identical action log hash |

## 8. Open questions for the user
1. **Sleep need:** should NPCs and the hero need sleep (fatigue) strictly, or is sleep cosmetic for NPCs? Default: strict for everyone (it is what makes beds matter), with hero fatigue effects mild (skills -10% at 800 permille, collapse at 1000).
2. **Odd-job pay level:** 1.10 x unskilled wage is deliberately below skilled work. Default as written; raise only if testers say the first job feels pointless.
3. **Job boards vs talk only:** default both (board in towns, talk in villages).

## 9. Budgets
| Item | Budget |
|---|---|
| 300 live people: scorer at 1 Hz each (about 15 evals per tick, <= 80 lookups each) | <= 0.15 ms per tick |
| Movement, path following, pose for 300 | <= 1.5 ms per tick |
| `JobRunner` and reservations (<= 150 active jobs in bubble) | <= 0.3 ms |
| Total behaviour share of the 8 ms live tick | <= 2.5 ms |
| Allocations in steady state | 0 per tick (CI allocation gate) |
| Board refresh at hour 0 | <= 3 ms per region (65 settlements) |
| Save growth | `Job` rows ~96 bytes x <= 3,000 open and recent jobs per region = under 0.3 MB |
