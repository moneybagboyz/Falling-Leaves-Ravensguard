# Performance and tooling: the reference-PC pass, the day's first hour, dropping dead rows, replay gaps and the trails

Status: design, before any Phase 8 code, on top of f2530c3.
Plan refs: §2.11 (the reference PC: 6-core CPU, RTX 3060-class GPU, 16 GB, 1080p, 60 fps; the budget table; deterministic replay), §6 (budgets measured locally and logged to `docs/perf/`; CI gates on allocations and operation counts), §7 (if 2,000 soldiers cannot hold, the default battle size drops).
Carried in: P7R-13 and the plan's budgets on the reference PC (phase-7); the reference-PC checks of Phases 1 to 6; Phase 5 S9 (`Stopwatch` reads); person-table growth and compaction's dangling links (phase-6 G2); Phase 4 S10, S11, G7; Phase 5's replay gaps and G4; `phase7_trail` and `SoldierHash`.

## 1. Goals
1. Every budget of §2.11 measured on the reference PC, with each miss fixed or logged with its fallback.
2. The day's first hour inside 150 ms.
3. A 100-year world that does not slow down as its dead pile up.
4. Replays that rebuild every live thing, and both trails equal on Linux and Windows.

## 2. What exists and what is new
| Today (f2530c3) | Phase 8 |
|---|---|
| `tools/Ravensguard.Bench`, the budget tests (CI multiplies by 5), `docs/perf/` (empty) | **extended:** a reference-PC run script; `docs/perf/reference-pc.md` |
| the season-day systems all in the day's first hour | **sliced** across the day's first 6 hours |
| `People/PersonTable` keeps every dead row | **compacted** yearly |
| `Diagnostics/SessionRecorder.cs`, `Game/Diagnostics/ReplayTool.cs` | **extended:** sites, vehicle sites, commissions, voyages |

## 3. Rules

### 3.1 The reference-PC pass (PF-1)
- **The run** (needs the user's machine): one script runs every §2.11 row and the carried checks, and writes `docs/perf/reference-pc.md` with the date, the build and the numbers.
- **Carried checks:** 60 fps at street zoom (Phase 1), the three-region walk (Phase 2), a road fight with ten fighters and torchlight underground (Phase 4), build mode with a crew and two wagons (Phase 5), the Phase 5 list (the 200-cell collapse, 200 sites, a finished piece, AB-13), and 2,000 soldiers with 300 horses at every zoom band (Phase 7 OV-1).
- **A miss** is fixed in PF-1 if it is under 1.5 times its budget. Otherwise it is written into the review with its fallback: for battles, the default battle size drops in steps of 250 with reinforcement waves.
- **`Stopwatch` reads** (Phase 5 S9) are compiled only under a `DIAG` flag; release builds have none in the sim.

### 3.2 The day's first hour (P7R-13)
- The season-day systems (seasonal growth, continent trade, weather pre-warm, caravans) run in **fixed slices over hours 0 to 5** of the season's first day, each in its own `SystemOrder` slot.
- A slice's order and inputs are fixed, so the result equals the unsliced run's on every seed. Only the hour each part lands in changes; the trails move once, re-pinned with the reason.
- **Gate:** the worst hour of a 2-year run is 150 ms or less on the reference PC.

### 3.3 Dropping dead rows (PF-2)
- At the yearly compaction a dead person's row is dropped when nothing names it: no kept chronicle entry, no book row, no estate or deed, no legend, no Codex page, no library run.
- **Ids are stable:** a person's id is never reused. Rows are found by an id-to-row map; a free list reuses rows.
- **Compaction's dangling links** (phase-6 G2): an entry named by any live record is kept, whatever its age.
- **Gate:** in the 100-year soak, a game year at year 100 costs no more than 2 times year 1 (it was 304 s against 26 s).

### 3.4 Replays and trails (PF-3)
- **Fight ids** come from a saved serial, so two practice bouts never share one (Phase 4 S10).
- **The site factory** is set for replays, so a crawl rebuilds (S11).
- **The live sections** are snapshotted at the barrier with the clock (G7).
- **The tick worker's side** of a vehicle site, a commission and a voyage is in the live trail (Phase 5).
- **`phase7_trail`** holds `ArmyHash`, `SoldierHash`, `SiegeHash`, `NavalHash`, `CoinHash`, `ScaleHash`, `WorksHash`, `IndustryHash`, `SupplyHash`. **`phase8_trail`** holds `AmbitionHash`, `LairHash`, `TollHash`. Both are compared on Linux and Windows in CI.
- The dev console's `catch { }` (Phase 5 G4) logs and shows the error.

### 3.5 Integer math notes
- Budgets are measured in µs as `long`; reports print ms with one decimal (tools may use floats).
- The id-to-row map is a sorted array of `(id, row)`, binary-searched, rebuilt at compaction: no hash-order iteration.

## 4. Saves
Dropping rows changes which rows a save holds, not its format (v8). The save writes rows in id order. Trails are re-pinned once, with the reason.

## 5. Golden tests
| Id | Name | Asserts |
|---|---|---|
| PF-1 | The run | the script writes every §2.11 row and every carried check (on the reference PC; CI runs it at 5x as a smoke) |
| PF-2 | First hour | the worst hour of a 2-year run at 150 ms or less on the reference PC; 192 ms or less on CI's box at 1x |
| PF-3 | Slices equal | the sliced season day gives the same economy, weather and caravan state at day's end as the unsliced on 5 seeds |
| PF-4 | 2,000 soldiers | OV-1's numbers on the reference PC, or the smaller default battle size logged |
| PF-5 | No `Stopwatch` | a release build's sim assembly has no `Stopwatch` call outside `Diagnostics` |
| PF-6 | Rows dropped | after 30 years, dead rows named by nothing are gone; every named one stays |
| PF-7 | Ids stable | no id is reused across 100 years; every saved reference resolves |
| PF-8 | Century cost | year 100 costs at most 2 times year 1 |
| PF-9 | Fight ids | two bouts in one game minute get different ids; a replay of each rebuilds it |
| PF-10 | Crawl replay | a bug save inside a crawl replays to the same hash |
| PF-11 | Live snapshot | a save's live sections and its clock agree to the tick |
| PF-12 | Trails | `phase7_trail` and `phase8_trail` filled and equal on Linux and Windows |

## 6. Parts and gates
| Part | Scope | Gate | Months |
|---|---|---|---|
| P8-PF-1 the reference-PC pass | the run, the carried checks, the first hour, `DIAG` | PF-1 to PF-5 | 0.9 |
| P8-PF-2 dead rows | compaction, stable ids, kept links | PF-6 to PF-8 | 0.6 |
| P8-PF-3 replay and trails | fight ids, sites, snapshots, the trails, G4 | PF-9 to PF-12 | 0.6 |

PF-1 starts in month 0 because its numbers shape the battle size, the Deck profile (CT-9) and the Steam page's system requirements (RL-8).
