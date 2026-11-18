# The abstract tick, tick barrier and the LODBridge

Plan refs: §2.3 (tiers, LODBridge, abstract tier runs every game hour), §2.4 (determinism: counter RNG, stable-ID order,
worker results applied at tick barriers, immutable snapshots), §2.1a (records canonical, live components are a cache), §2.11
(hourly abstract tick <= 150 ms for 25,000 people / 58,000 fields), §1 (speed: at 8x the abstract tick coarsens, live ticks
are not multiplied), §5 Phase 2 (the abstract tick; caravans abstract on the road, physical in the bubble via the LODBridge).

## 1. Purpose
One simulation of the economy and population that runs the same way whether or not anyone is looking, and one bridge that
makes the people, caravans and goods of that simulation appear (and fold back) when the hero or a squad comes near, without
ever holding two copies of the truth.

## 2. Data

### 2.1 Tiers and ownership
| Tier | Owns | Runs |
|---|---|---|
| Records (always) | `PersonTable`, `HouseholdTable`, `FieldTable`, `BusinessTable`, `StockTable`, `AccountTable`, `MarketBook`, `CaravanTable`, `HerdTable`, `ResourceLayer` | mutated only by the abstract tick and the command pump (single writer, below) |
| Abstract | the hourly/daily/seasonal kernels (this spec, 3.1-3.4) | worker thread(s), per active region |
| Live | ECS entities for <= 3 bubbles; each is a **cache** of one record (`LiveBinding`: person index <-> entity id, region, bubble id) | main thread, 20 Hz |

### 2.2 `EconCommand` (the only way the live tier or UI changes economy records)
`readonly record struct EconCommand(long Frame, int Seq, CommandKind Kind, EntityId Actor, EntityId Target, long A, long B, int Good, byte Tier)`; kinds in Phase 2: `Buy`, `Sell` (market trade; A = qty g, B = max/min total mites), `PayCoin` (A
mites), `GiveStock`/`TakeStock` (container/ground), `HireWorker`, `PostOrder`, `CaravanLoss` (live battle result),
`PersonDied` (live kill), `ItemMaterialise/Dematerialise`. Each returns a `CommandResult` (ok/err, actual totals, the tick it
executed at). The queue is a `ConcurrentQueue`; the pump drains it in ascending `(Frame, Seq)`.

### 2.3 `AbstractJob` (per person, replaces ad-hoc "current action")
`Kind` (`Sleep, Home, Commute, Work, Shop, Eat, Church, Forage, Travel, Haul, GuardCaravan, Idle`), `Place` (settlement+lot,
tile, site, road edge id), `StartHour long`, `ProgressPpm`, `PathPlanId` (time-parametrised path, 2.4), `Pack` (stock owner
id of the carried goods; the person's own `StockTable` row set).

### 2.4 `PathPlan` (movement is a function of time, not integrated state)
`Polyline` (region metres or road-edge ids), `StartTime GameTime`, `SpeedMmPerGs` (millimetres per game second), `Pauses[]`;
`PositionAt(GameTime t)` is a pure function. Used for caravans, migrants, commuters, travelling merchants. Because both tiers
evaluate the same function, **promotion needs no state transfer and the live entity can never drift from the record.**

### 2.5 Snapshots, outbox, environment
`EconSnapshot` (immutable, published once per hour at the barrier, `Version` = game hour): per settlement and good `mid, ask, bid, StockG, cover days`, wages, food cover index, population, unrest; per caravan `PathPlan` + goods summary; per region
`RegionSummary` (continent spec). ~100 KB. `RegionOutbox` (built by the worker, applied at the barrier): chronicle entries,
rumour seeds, `Promote/Demote` hints, migration mailbox contents, dashboard counters. `HourEnv`: weather sample per
settlement cell, daylight hours, river-high flags.

### 2.6 Content (`content/base/economy/schedules.jsonc`, `tick.jsonc`)
```jsonc
{ "type": "schedule", "id": "default_day", "blocks": [
    { "from": "21:00", "to": "04:30", "kind": "Sleep", "place": "home" },
    { "from": "04:30", "to": "05:00", "kind": "Eat", "place": "home" },
    { "from": "05:00", "to": "06:00", "kind": "Commute", "place": "work" },
    { "from": "06:00", "to": "16:00", "kind": "Work", "place": "work" },
    { "from": "16:00", "to": "17:00", "kind": "Commute", "place": "home" },
    { "from": "17:00", "to": "18:00", "kind": "Shop", "place": "market" },
    { "from": "18:00", "to": "21:00", "kind": "Home", "place": "home_or_tavern" } ],
  "holy_day_overrides": [ { "day_of_month": 5, "from": "09:00", "to": "11:00", "kind": "Church" } ],
  "occupation_overrides": { "Merchant": "market_06_16", "Guard": "shifts_3", "Innkeeper": "08_24", "Fisher": "dawn_dusk" } }
{ "type": "tick_settings", "id": "default", "slice_modulus": 24, "bubble_promote_m": 150, "bubble_demote_m": 200,
  "demote_after_real_s": 5, "promote_per_frame": 50, "max_bubbles": 3, "npc_commute_m_per_game_hour": 900,
  "max_commute_m": 1350, "long_trip_m": 2000, "hour_budget_ms": 150, "season_budget_ms": 500 }
```
Commute times follow the schedule file, not the block times: departure is `block start - commute duration` (3.4).

## 3. Rules

### 3.1 Tick barrier protocol (per active region, per game hour boundary `B(h)`)
1. Main thread, **at the first frame after the clock crosses `h`**: stop accepting commands into the *current* batch; freeze
   `batch_h = queue.DrainAll()` (stable sort by `(Frame, Seq)`); pause the live tier's *writes to records* (it keeps
   simulating entities; it only reads snapshot `h-1`).
2. Execute `batch_h` on the main thread against the records (the worker is idle): trades, payments, deaths. (The command pump
   also runs between hours whenever the worker is idle, applying commands immediately with `tickKey = h`; a command arriving
   while the worker runs waits <= 150 ms, never for the next hour.)
3. Start `AbstractHourStep(h)` on a worker for each active region (L0/L1 in parallel, regions independent; inter-region
   effects go through mailboxes). The worker holds the region gate and mutates records in place; **nothing else touches those
   tables**.
4. When all workers finish (target <= 150 ms; the main thread keeps rendering and running the live tier), the main thread
   applies the outboxes in ascending region id, ascending event order: chronicle appends, migration mailboxes (3.9 of
   population spec), snapshot swap (`Interlocked.Exchange` of the snapshot pointer), LODBridge hints, autosave triggers. Cost
   <= 5 ms.
5. Dev builds assert at the barrier: no record changed outside worker/pump (`SimGate` owner check) and `EconomyHash` (rolling
   hash of accounts, stocks, populations) is recorded.
If a worker overruns 400 ms the barrier still waits (the simulation never skips an hour); a **degraded mode** lowers the
update rate of the live-tier snapshot readers only and logs an overrun to `docs/perf` counters.

### 3.2 Hourly step (the order is binding; see overview §4)
H0 environment -> H1 caravans & migrants -> H2 work credit (businesses, extraction, farm tasks) -> H3 market session -> H4
daily slice -> H5 hour-0 daily globals -> H6 seasonal -> H7 outbox. Every phase iterates its table in ascending index. Phases
read results of earlier phases within the same hour (e.g., H3 sees stock completed in H2) and never of later ones.
**Slice rule:** a record with dense index `i` runs its daily work in the hourly step where `hour_of_day == i % 24`.
Households and businesses use their table index; settlements run daily globals at hour 0. (`index % 24` is stable across
saves because indices never change; compaction rewrites ids' indices only at save and re-derives slices deterministically.)
**Cost shaping:** per-hour work is bounded: <= 217 households (5,200/24), ~30 businesses (700/24), all active caravans (<=
60), the settlements with an open market session, plus H2 credit for working persons. Seasonal steps are sliced the same way:
on the first day of a season settlement `k` runs its S-steps at hour `k % 24` (farming rotation, which is per field, runs at
hour 0 in 24 equal index ranges `field % 24`), so every seasonal step still finishes the same game day and the worst hour
stays under the 150 ms cap.

### 3.3 Coarse mode (speed 8x, waiting, headless soak)
The abstract tick **never skips** an hour. At 8x one real second is 160 game seconds (1 hour = 22.5 real s), so single hour
steps still fit. Waiting/resting/fast-travel and headless soaks may call `AdvanceHours(n)` which loops `AbstractHourStep` n
times in one worker job; results are identical to n separate steps (test G-A9). Daily kernels in L2 regions run their 24
slices at once, with markets cleared once per day instead of twelve sessions (a documented, deterministic approximation used
only for L2).

### 3.4 Schedules (abstract position of a person: pure function of (person, GameTime))
Default day (local, culture-free, from `content/base/economy/schedules.jsonc`): sleep 21:00-04:30 (home bed), 04:30-05:00
dress/eat, **commute 05:00-06:00 (shortened by distance, below)**, work 06:00-16:00, commute 16:00-17:00, market/chores
17:00-18:00 (shoppers at the book), home/tavern 18:00-21:00. Holy days (day of month 5 of the calendar's 5-day months,
`Calendar.DayOfMonth`): chapel/church 09:00-11:00 for everyone, no work in the morning (fields idle; production/farming skip
those hours). Occupation overrides: farmers go to their parcel tile for field tasks; shepherds to pasture; merchants to the
market square 06-16; guards in 3 shifts; innkeepers 08-24; fishers dawn to dusk (daylight cap).
`PositionOf(person, t)`: from the job/schedule block containing `t`: Sleep/Home -> the household lot door; Work -> workplace
lot door / parcel centre tile (for field tasks a stable tile `hash(personId, day) % parcelTiles`) / extraction tile; Commute
-> point on the path between them at `(t - depart)/(commute duration)`; the exact spot inside a lot uses `DetRng` keyed
`(hour, personId, 0x2A0)` so the same hour always gives the same spot.
**Commute time (Decision C15):** physics at the plan's walking speed (1.5 real m/s = 0.075 m per game second = 270 m per game
hour) would make 1 km take 3.7 game hours, more than a third of the day. So scheduled NPC commutes move at the plan's
**sprint speed, 5 real m/s = 900 m per game hour**: `commute_hours = distance_m / 900`, shown in the live tier as a brisk run
along the daily path. Departure = work start minus commute (a 400 m commute leaves 05:33:20 for 06:00), commute time does not
subtract from the 10 worker-hours, and the maximum scheduled commute is `max_commute_m = 1,350` (1.5 h); farther work (fields
beyond 1.35 km, mines, clamps) uses a **field camp**: the worker stays at the site for the task window (sleeps in the field
hut/farmstead, food carried in the `Pack`). Any trip over `long_trip_m = 2,000` (merchants, migrants, caravans, hero) uses
real speeds (cart 2.5, walk 1.5, jog 3 m/s real) through `PathPlan`. Open decision O10 lets the user pick another pace.

### 3.5 Promotion (abstract -> live)
- Triggered by the bubble manager at 2 Hz (not per frame) for each bubble centre `c` (hero, active squads, army, ship; <= 3
  bubbles). Candidate sets are found spatially: settlements whose plan rect or radius is within `150 + 150 m` of `c`, road
  edges and `PathPlan`s crossing the circle, field/parcel tiles inside it. For each candidate record compute
  `PositionOf(person, now)`; inside `150 m` -> promote.
- **Spread over frames:** at most 50 entities per frame are instantiated, nearest-to-centre first (ties ascending person
  index), then moving outward; people outside screen range (> 80 m) spawn first at the edge only when `PositionOf` puts them
  there, otherwise at their position (they are inside buildings or off-screen at street zoom). An arriving column (`PathPlan`
  with many members) spawns member by member as the path enters the circle, so it **marches into view**.
- A promoted person gets components (position, pose, intent from `AbstractJob`, path from `PathPlan`, rendering row from
  class/occupation). The abstract job state stays as is (carried goods, job, schedule), and the **abstract tick keeps running
  for that person** (needs, work credit, vital rolls); the live entity is theatre in Phase 2, the person walks/works/chats on
  schedule. Interactions (talk, trade) happen as commands (`Buy/Sell` against the person's `Pack` or the settlement book).
- **Later phases** may mark a business station `LiveLeased` (hero works the forge, a NPC smith in the bubble swings a
  hammer): the abstract tick skips leased stations and the live tier emits `WorkDone(businessId, station, workerHours)`
  outcomes applied at the next barrier.

### 3.6 Hysteresis, demotion and fold-back
Demote when `distance > 200 m` for >= 5 real seconds (promote at 150, demote at 200; at most 3 bubbles). Fold-back (all as
commands/outbox applied at the next barrier unless the worker is idle):
| What happened in the bubble | Fold-back |
|---|---|
| Position/path | derive `AbstractJob`: if mid-commute -> `Commute` with progress from `PathPlan`; if working -> `Work`; if the NPC was displaced (fled, was moved) -> `Travel` to the nearest schedule point |
| Items moved | already exact (stock rows are the truth); live item drops on the ground become `ItemTable` rows owned by the chunk (items spec) |
| Coin spent | already exact (ledger commands) |
| Work done | none in Phase 2 (no leased stations); later `WorkDone` events |
| Casualties/wounds | `PersonDied` command, wounds as person columns (Phase 4) |
| Caravan attacked | `CaravanLoss(goods, guards, coin)` -> record edited, chronicle `Raid` entry with cause |
| Damaged buildings/fields | `ChunkDelta` (persistence) + `DamagePermille` on field tiles |
A person never has a live position and an independent abstract position: while live, `PositionOf` is overridden by the
entity's `LiveBinding` and the abstract job is *held* (no schedule advancement except the clock); on demotion the job
re-derives from now.

### 3.7 Caravans on the road
A `Caravan` record has `PathPlan` and `State` (loading, travelling, tolling, resting, arrived, lost). `PositionOf(caravan, t)` is evaluated by both tiers. When the path crosses a bubble circle, the LODBridge spawns carts, draught animals and guards
as entities along the polyline spaced 12 m, each following the *time-parametrised* position (the live entities may be steered
by collisions/AI within `+- 6 m`, and are reset to the function position when they drift more). Road encounters (ambush,
toll) are decided abstractly in H1 (stream 0x230) and, if the caravan is promoted, played out live: the abstract roll sets
the encounter kind, the live result flows back via `CaravanLoss`.

### 3.8 Battle bubble and pausing
While a battle bubble is active, ambient promotion in neighbouring chunks stops (plan §2.3); persons in the engagement box
are `LiveHeld`. The abstract economy continues (hourly, same rules). Outside battle results come back via commands; abstract
battles elsewhere are chronicle reports.

### 3.9 Region levels interaction
Active regions are L0 (contains a bubble) and L1 (neighbour ring): full hourly step. L2: daily kernels (3.3). L3/L4:
continent tick only (continent spec; seamless spec).

## 4. Tick placement summary
| Where | What |
|---|---|
| Main thread frame | live tier at 20 Hz fixed step; bubble manager 2 Hz; LODBridge spawn queue (<= 50/frame); snapshot read |
| Barrier `B(h)` | drain commands, execute, start workers, apply outbox |
| Worker | `AbstractHourStep(h)` per L0/L1 region; `Daily(h)` per L2 region at hour 0; `ContinentTick` after S-steps of a season day |
| Save | at a barrier: `SaveSnapshot = Clone(records)` (memcpy ~10-25 MB per region), written on a worker; never during H-steps |

## 5. Determinism
- Records mutate in ascending-index order; RNG keyed `(seed, hourKey, entityId, stream, draw)`; no `Parallel.For` over
  records inside a region step (parallelism is across regions only, with mailboxes applied in region-id order).
- Commands: logged `(tickKey, Frame, Seq, command)`; replay re-injects them at the same `tickKey`; the live tier's own
  randomness (combat) uses sim-seconds keys (combat spec).
- `EconomyHash` per day = SHA-256 over accounts, stock rows (sorted), populations; identical on Windows and Linux (integers only).
- Promotion order (distance then id) affects only entity creation order, not records.

## 6. Interactions
Every system: the abstract tick is their scheduler (overview §4). Live tier: reads `EconSnapshot`, issues `EconCommand`s.
Persistence: chunk deltas for live results; save at barriers. Continent/seamless: region levels and mailboxes. Chronicle:
outbox. Caravans: `PathPlan`.

## 7. Edge cases
- Hero trades while the worker is running: command waits <= 150 ms, then executes; the UI shows a short "settling accounts"
  state (no frame hitch because the wait is off the render thread).
- Hero teleports (fast travel/load): bubbles recomputed; all live bindings rebuilt from records; no state lost because live
  is cache.
- A promoted person dies in the bubble and the abstract tick rolls their death the same hour: death commands win (apply first
  at the barrier); the vital roll skips `Status != Alive`.
- Region L0 -> L1 transition mid-hour: takes effect at the next barrier.
- Worker exception: the region is marked `Faulted`, the barrier reports the stack, the hour is retried once from the pre-hour
  snapshot of dirty tables (dev) or the game autosaves and shows an error (release).
- Spawning cap: if > 300 people would be live, the farthest are promoted later; never more than 300 in a normal bubble.


## Decisions made in this spec (collected in 00-overview.md section 10.4)
- **A1** Decision: The worker mutates economy tables in place under a region gate; the live tier reads snapshots and writes via commands. Reason: avoids double buffering 25k records (E3).
- **A2** Decision: Scheduled NPC commutes move at 5 m/s real (900 m per game hour), max 1.35 km, longer jobs use field camps. Reason: a literal walk eats the workday (C15, O10).
- **A3** Decision: Daily work is sliced by `index % 24`. Reason: flat hourly cost (E4).
- **A4** Decision: Promote at 150 m, demote at 200 m after 5 s, at most 50 spawns per frame nearest first. Reason: plan §2.3, with a timer against flicker.
- **A5** Decision: In Phase 2 the live tier is theatre; production stays abstract; later phases lease stations. Reason: one source of truth, no double counting.
- **A6** Decision: Coarse mode loops hourly steps; it never skips. Reason: determinism independent of speed.

## 8. Golden tests
| id | Case | Expected |
|---|---|---|
| G-A1 | Slice assignment: household index 4,825; business index 713; settlement | hour `4,825 % 24 = 1` (daily at 01:00); `713 % 24 = 17` (17:00); settlements at hour 0 |
| G-A2 | Commute: labourer, workplace 400 m east of home (5 real m/s = 0.25 m per game s) | duration 1,600 game s = 0.444 h = 26 min 40 s; depart 05:33:20; at 05:46:40 position 200 m east; at 06:00 at the door; a 1.35 km commute = 1.5 h, a 2 km job needs a field camp |
| G-A3 | Caravan cart, 8 km road, 2.5 real m/s = 0.125 m per game second | duration 64,000 game s = 17.78 h; at departure + 6 h position 2,700 m; abstract and live evaluations agree to the millimetre |
| G-A4 | Promotion spread: 130 people enter the circle together | frame 1: 50 nearest; frame 2: next 50; frame 3: last 30; ties by id; none instantiated twice |
| G-A5 | Hysteresis: person at 160 m, then 170 m after promotion at 148 m; then 205 m for 6 s | stays live at 160/170; demoted at the 205 m mark after 5 s; `AbstractJob` re-derived with `StartHour = now` |
| G-A6 | Command order: commands (frame 10, seq 2), (frame 10, seq 1), (frame 11, seq 1) arrive in any order | executed 10/1, 10/2, 11/1 every run |
| G-A7 | Replay: 30 game days with 40 scripted hero trades, run twice and once with the worker artificially delayed | identical `EconomyHash` every day-end |
| G-A8 | Snapshot: reader thread loops while the barrier swaps | never sees a mix of two versions; `Version` monotonic hour counter |
| G-A9 | Coarse mode: `AdvanceHours(48)` vs 48 single steps | identical hash at the end and at every day boundary |
| G-A10 | Budget: reference region (25,000 people, 65 settlements, 62,500 fields) | hourly p99 <= 150 ms, p50 <= 10 ms, hour-0 of season day <= 150 ms; apply-barrier <= 5 ms; `SaveSnapshot` clone <= 25 ms |
| G-A11 | Fold-back: an NPC promoted 10 game minutes before leaving for work, demoted mid-commute | after demotion `AbstractJob.Kind = Commute`, position matches `PathPlan` at the demote time |
| G-A12 | Single writer: dev assert | any write to an economy table from a non-worker, non-pump thread fails the test `NoForeignWrites` |

## 9. Soak invariants
1. Replaying a recorded 1-year soak (with commands) reproduces `EconomyHash` per day on a second machine.
2. Zero `SimGate` violations in 100 years; hourly max <= 400 ms in CI (soft), p99 <= 150 ms (hard on reference runner).
3. Promote/demote churn: a hero standing still for 1 game day causes <= 5 promote/demote events per 100 people (hysteresis
   works), and walking across a town never instantiates the same person twice.
4. Live-held persons' records after demotion show no divergence from a no-bubble control run in coin, stock, and vital state
   (the live tier is a pure cache in Phase 2).

## Notes

Implementation notes of the abstract tick (`src/Ravensguard.Sim/Abstract/`, written with the population kernels; the LODBridge, caravans and the continent tick are not built yet).

- **Shape.** `WorldTicker` owns the hour loop: barrier start (drain `CommandQueue`, sort by `(Frame, Seq)`, run each command through the `CommandPump`), hour step (phases H0..H7 in the spec order, systems of a phase by ascending `IAbstractSystem.Order`, ties by name), barrier end (`RegionOutbox` applied in draft order: chronicle appends, `SimEvents` flush, `WorldSnapshot` swap by `Interlocked.Exchange`, `HourDone` advances). Synchronous drivers (`AdvanceHours`, `AdvanceDays`, `AdvanceTo`) are what tests and soaks use; `StartWorker` + `Pump(now)` is the game driver (one dedicated worker thread per ticker, barrier start and end on the calling thread, `WaitIdle` for tests). Both run the same three methods, so batching and threading cannot change a result (tests G-A7, G-A9).
- **Registration.** A system implements `IAbstractSystem` (`Name`, `Phases` flags, `Order`, `Step(TickContext)`), picks its `Order` from `SystemOrder` (the constants spell the H2/H4/S1..S9 tables of overview section 4) and is added with one line in `AbstractSystemRegistry.CreateDefault` or by marking the class `[AbstractSystem]` (public parameterless constructor; found by `Discover`). Registration order never matters; a registry is frozen when a ticker takes it. A system with different slots in different phases is written as two classes (population: `PopulationDailySystem` H5, `PopulationSeasonalSystem` H6 / S6, `PopulationMigrationSystem` H6 / S7). Shared state of other systems (field tables, market books) goes in `SimWorld.Set<T>` / `Get<T>`.
- **A1 refinement: H6 runs every hour of a season's first day.** Section 3.2 slices the seasonal steps by settlement (`k % 24`); the ticker therefore calls H6 systems on all 24 hours of day of year 1, 16, 31, 46 and the system tests `TickContext.IsSeasonSlice(k)`; a system that wants everything at once checks `HourOfDay == 0` (the population ones do: their marriage matching is one O(people) pass per season). A run that starts mid-day (a new game starts 08:00 of day 1) misses the earlier slices of that first day, once.
- **Command tick keys.** A command executes at the next barrier (tick key = the hour about to be stepped) or, when the worker is idle between hours, immediately with the same key. `ExecutedCommand` logs `(TickKey, command, result)` in order; `ScheduleReplay(log)` re-injects them at the same keys (G-A7). Built-in handlers: `PayCoin` (person or household purses, reason Gift) and `PersonDied` (death commands run at the barrier, before the hour's vital rolls, so they win). Market kinds have no handler until the markets system registers one with `CommandPump.Register`; an unhandled command is refused (`NoHandler`) and logged, never dropped.
- **Chronicle ids are allocated when an entry is drafted** (the worker is the only allocator while it runs), so a later entry can name an earlier one as its cause in the same hour; the entries reach the `Chronicle` at the barrier in draft order.
- **Timing.** `TickerStats` keeps per-phase and per-system totals, call counts and maxima, the hour wall times (median, p99, max, hour 0, season days), the barrier apply time (p99) and a text `Report()`. Seed 1234, 25,800 people, 5,170 households, harvest stand-in plus the Phase 2 population systems, release build, 60 game days: hour median 1.1 ms, p99 9.7 ms, worst hour 93 ms (the first hour a seasonal or weekly pass runs, JIT included), hour 0 median 1.5 ms, barrier apply p99 0.03 ms. The 100-game-year soak (144,000 hours) runs in about 45 s: hour median 0.22 ms, p99 5.8 ms, worst hour 203 ms (a GC pause on a marriage hour; the spec's hard cap is on p99).
- **Not yet built:** the `SimGate` single-writer assertion (G-A12), degraded mode, L2 coarse daily mode, parallel regions with mailboxes, `EconSnapshot` beyond the small `WorldSnapshot` (version, population, households, births, deaths, marriages, famines), the LODBridge. The save section "abstract" stores `HourDone`, the person vital columns and the vital counters (`Save/AbstractSave.cs`); a save without it loads and the first `SimWorld.From` seeds the pregnancies of a fresh world.
