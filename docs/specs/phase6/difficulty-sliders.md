# Difficulty sliders: honest, named, shown openly

Plan refs: locked decision (Nov 2026: one intended brutal game, plus a few **honest named sliders** shown openly in the world settings: wound lethality, bandit frequency, economy harshness, plague severity, and Ironman on or off; **no hidden rubber-banding**); overview 10a (Nov 20: the world setting `aging_pace`), §3.7 (Ironman), §3f (brutal but learnable).

## 1. Purpose
Six settings a player chooses at a new game (five sliders and the world's ageing pace), each named for what it changes and each changing only that, with the default being the game as calibrated. Phase 6 brings the plague and the slavers that two of them govern, so all five land together, early (step 1), with every default leaving every golden unchanged.

## 2. Dependencies
`WoundBook` and `MedicineTables` (bleeding, infection), bandit bands and road encounters (Phase 3-4), slaver bands (`slavery.md`), farming yields and spoilage (Phase 2), the hero's origin coin, the great plague (`disease-and-plague.md`), the save header's `Ironman` (exists). New: `Sim/Core/Difficulty` (a record), the Quick Start's Advanced panel rows, the settings screen's read-only view.

## 3. Data
`Difficulty` record (the optional `difficulty` save section, absent while all values are default and none ever changed; the run's record in the library next to its save): 
| Slider | Values (default first) | What it multiplies (and nothing else) |
|---|---|---|
| Wound lethality | Brutal 100% / Hard 85% / Mild 70% / Merciless 130% | every external bleed rate at the wound's creation and the infection chance per day (permille x value / 100) |
| Bandit frequency | 100% / 50% / 150% / 200% | bandit band spawn chances, road ambush weights, slaver raid chances |
| Economy harshness | Normal / Lenient / Harsh | the hero's origin coin (x1 / x1.5 / x0.75), harvest variance amplitude around its mean (x1 / x0.5 / x1.5), spoilage rates (x1 / x0.75 / x1.25) |
| Plague severity | Normal / Off / Rare / Frequent; mortality x1 / n.a. / x0.5 / x1.5 | the great plague's start chance (1% / 0 / 0.5% / 2.5% a continent-year) and case fatality (capped at 90%) |
| Aging pace (world setting) | 1x historical (default) / 2x / 3x / 4x | the ageing clock of everyone, hero and NPCs alike: childhood, adulthood and old age pass `pace` times as fast, so a person is `(days lived x pace) / 60` years old. Per-year vital chances (births, deaths, illness) stay per game year; only the clock scales. History is simulated with the world's pace (a notable is `(calendar years since birth) x pace` years old), so ages stay consistent. Stored with the world (`HistorySettings.AgingPace`, in `world.json` and the save's world meta), fixed when the world is made |
| Ironman | Off / On | saves (one slot, saved on exit), succession only; Ironman locks the other four |

## 4. Rules
- Chosen on the Quick Start (Advanced) per run; shown on the world library card and in the settings (read-only in Ironman). In a non-Ironman run they can be changed in the settings; each change is a chronicle entry (`difficulty_changed`, cause: the player) and the save header records the history of values.
- **No hidden adjustment:** nothing reads the hero's success, wealth or deaths to change any rate. A source test asserts the `Difficulty` record is read only by the five named sites.
- Bandit frequency also scales the continent tick's band counts at their next seasonal spawn (nothing despawns).

## 5. Determinism
The record is part of the run's inputs: two runs with the same seed and record are identical; changing a slider changes only the named multipliers (the draws are the same streams with the same keys; only the thresholds move).

## 6. UI (960 x 540)
Six rows at the top of the Advanced panel (they stay available for a library world, whose generation settings are fixed; its ageing pace shows read-only), each with a one-line honest description ("Bleeding and infection: 70% of the calibrated rates"); the world library card lists non-default values.

## 7. Tests (golden)
| id | Case | Expected |
|---|---|---|
| DS-1 | Defaults | every golden hash of Phases 1-5 unchanged with the default record |
| DS-2 | Wound lethality 70% | a femoral wound's rate 70 ml/s instead of 100; infection per day x0.7; WB tests otherwise unchanged |
| DS-3 | Bandits 200% | spawn and ambush weights doubled; SL-13's raids at 30% |
| DS-4 | Economy harsh | origin coin x0.75; the harvest's deviation from the mean x1.5 with the same draws |
| DS-5 | Plague off | no start in 1,000 continent-years (DP-11) |
| DS-6 | Only five readers | the source test finds `Difficulty` read at the five named sites only |
| DS-7 | Change mid-run | a non-Ironman change writes `difficulty_changed`; Ironman refuses it; the aging pace is never a run change |
| DS-8 | Aging pace | a child at 4x comes of age in a quarter of the 1x game days; seeded ages equal the requested ages at every pace; a one-year targeted vital run at 4x keeps births, deaths and the population in band (the full 100-year soak at each pace is a close-out run) |

## 8. Open questions (recommended defaults)
1. **Change during a run:** allowed outside Ironman, recorded in the chronicle. Default as written.

## 9. Budgets
The record 8 B in the header; no per-tick cost (multipliers read at creation of each wound, spawn or roll).

## 10. Notes (as built, Phase 6 step 1 part A4)
- `Sim/Core/Difficulty` holds six integers (wound lethality %, bandit %, economy, plague, Ironman, aging pace) with their readers: `ScaleOriginCoin`, `HarvestVariancePct`, `SpoilagePct`, and for plague the one accessor pair `PlagueStartPermilleX10` (100 = 1%) and `PlagueFatalityPct(base)`. The plague builder (C4) reads only those.
- The record sits on `WorldState.Difficulty` (set after the economy, wounds and travel parts exist). Setting it hands one plain number to each reading site: `WoundLedger.LethalityPct` (then each `WoundBook`: bleed rate at wound creation, infection chance per day), `TravelState.BanditPct` (the Ambush, Demand and Block encounter weights; the encounter rate itself is unchanged), `EconomyState.SpoilagePct` and `HarvestVariancePct` (the harvest keeps its draw and scales the deviation from the mean). The origin coin is scaled once in `NewGameWorld.Create`. DS-6 is a source test: only `WorldSave`, `DifficultySave` and `NewGameWorld` name the record.
- Not yet wired because the system does not exist: bandit band spawns and slaver raids (they take `BanditPct` when built), the great plague (C4). Books made by a fight with no ledger, and the abstract battle's own books, are not scaled (they take the default 100). Resident regions of the continent other than the home economy keep the calibrated spoilage and harvest.
- `aging_pace` is on `HistorySettings` and `WorldHistory` (the hash only changes when it is not 1) and on `PersonTable.AgingPace`; `PersonTable.AgeYears`, `AgeYearsOf` and `BirthDayFor` are the one age arithmetic (the vital systems, `SimWorld.AgeOn`, the populator, the hero's kin, newborn adults). Pregnancy, postpartum and other day counts are not scaled. The history generator's notable ages, adult thresholds, birth years and the populator's lords read the pace.
- UI: `DifficultyRows` on the Advanced panel, one cycling button and one honest sentence per slider; the world's generation rows hide for a library world; the run list and the world card show non-default values (`RunInfo.Difficulty`).
- Close-out: run the full 100-year population soak at aging pace 1, 2, 3 and 4 (only a one-year 4x check was run in this part).
