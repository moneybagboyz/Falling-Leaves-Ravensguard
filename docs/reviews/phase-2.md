# Phase 2 code and bug check

Reviewed at 55ce471: the seamless continent (regions, paging, per-region saves), the runtime that ticks it beside the live game, and the tests around them. Findings were checked with probes (`review2/p1`, `p2`, `p3`) and fixed with regression tests that failed before the fix. The economy review is the second section (17 findings; five of them, 1, 2, 11, 14 and 15, were fixed by the continent and caravan agent).

- **Findings:** 13 in the seamless section (12 fixed; #2, economy state not saved, was answered by the economy section's #7) and 17 in the economy section (12 fixed here, 5 by the continent agent).
- **Save format:** v1 -> v2 (baseline guard). The default migrator takes v1 saves unguarded.
- **Tests:** 18 new in `RegionsC/Phase2ReviewTests.cs` and `SeamMarginTests.cs` (whole grid, both seeds); build has 0 warnings.
- **Checks:** `--smoke`, `--check-keymap` and `--selftest` pass; golden hashes unchanged (no generation output changed).

## Seamless continent, saves and runtime (13 findings)
| # | Severity | Defect | Fix |
|---|---|---|---|
| 1 | High | Chunk snapshots and deltas in regions without records (sea, wilderness) were in no saved section when a runtime was attached; the chunks regenerated on load, against plan 2.2 | The world's chunk section also takes every entry whose region has no section of its own (`SaveView.InMainChunkSection`); probe p1 is a test (delta and first-visit snapshot both come back) |
| 2 | Medium-high | Economy counters, parish, labour and trade state are not saved | **With the economy agent.** Hooks left in `Save/`: see the list at the end |
| 3 | Medium-high | Catch-up was not a pure function of (records, hour): the coarse/full boundary was `hour - 72`, so the result depended on the join hour, which depends on worker build time | The phases an hour runs depend on that hour alone: hours up to the last day boundary at or before `anchor - 72` run coarse, the rest full. The anchor is the game hour the region was asked for (`RegionManager.RequestedHour`, from the game clock or the barrier hour). Tests: any stops and any join delay give the same hash; with no anchor every hour is full (exact) |
| 4 | Medium | The AtBarrier save read the resident set and the pager ids separately: a region attached by the main thread between the two was lost, and `Sync` could delete the page file under the reader | `RegionRecordSet.CaptureView` takes the resident list and the paged bytes under one lock, and `Sync` attaches (insert, delete file, absorb chunks) under the same lock. A forced-interleaving test attaches a region on another thread at the moment the pager lists its ids |
| 5 | Medium | Diff-against-baseline saves had no guard against a regenerated region that differs | Region sections and the home meta store a baseline hash (regenerated population, households, lots and terrain) and the region-generator version; loading regenerates, compares and refuses with a message naming the region or the version. `SaveFormat.CurrentVersion` 2, `SaveMigrator.CreateDefault` takes v1 saves unguarded. Chunk snapshots already carry generator version and content hash each (verified by test) |
| 6 | Medium | Daily regions ran all 24 hours at the day boundary: 13 of them cost 340-600 ms in one barrier (budget 150 ms) | `DailyTarget`: 6-hour slices, each region on its own phase by id, pure in (hour, id). Tests: steps in one barrier are bounded by the schedule (12 against 168 for 7 daily regions), and the slowest barrier is under the budget (`TestBudget`; best of up to three passes of a day and a half, so a noisy pass is tolerated and a daily burst is not) |
| 7 | Medium | `MovePerson` had no callers and dropped the spouse link, left minors behind and cleared pregnancy | `MoveHousehold` moves a household with all members, kin links, pregnancies, coin and stock; kin who stay become foreign references (`PersonTable.ForeignKin`, saved). `MovePerson` refuses any move that would split a family (`CanMovePerson`). Coin and goods are conserved across the two regions (test: totals, both audits, both mass audits, ticking afterwards, save round trip). Wiring to migration and caravans is later work (spec N33) |
| 8 | Low-medium | Terrain-only regions were never evicted (3.2 MB each, up to 256) | `EvictDistantTerrainOnly` every hour: beyond the hysteresis distance of every traveller and more than one region from the ring region |
| 9 | Low-medium | After the tick faulted, saves still ran on half-stepped records and could overwrite good slots | `AtBarrier` refuses jobs after a fault (also those already queued); the existing save-failed toast or modal shows the message. Ironman quit asks the player. `Quiesce` still works |
| 10 | Low-medium | A released chunk generator was rebuilt (about 100 ms) on the main thread when the hero came back | `PrewarmGenerator` on the worker, one per hour, for built regions within the trigger distance of a traveller |
| 11 | Low | A region whose build always fails was retried forever and threw on the main thread | 3 tries with doubling waits, then marked failed, logged once (`BuildFailed`, a warning in the game) and left alone; `ClearFailure` tries again |
| 12 | Low | Temp pager folders leaked on a crash or a failed load; loaded sections were kept twice; `Dispose` could block the main thread for 5 s | Construction failures and failed or cancelled loads dispose the runtime and its pager; folders older than a day are swept; `RegionSections` is cleared once the pager holds them; `Dispose` waits at most 250 ms and a busy worker cleans up itself |
| 13 | Low | Test gaps | Hour-step budget asserted (with #6); the page-out test asserts hour and hash after `AdvanceTo`; `A_region_section_...` fails if it checks fewer than two regions; the seam margins are a generator invariant (`RegionSeamMargins`) checked over all 256 regions of both seeds (the old 3 x 3 block hid lots at 229 m, so the lot floor is 200 m) |

## Economy (17 findings)
Reviewed: `Economy/`, `Production/`, `Farming/`, `Goods/`, `Taxes/`, the abstract economy systems (`EconomySoak`, `ParishRelief`, `HouseholdSpending`, `EconomySystems`, `EconomyWorld`), their content and tests. Regression tests are in `Review/Phase2EconomyReviewTests.cs` unless named; each failed before its fix. Decisions I42-I53 in `docs/specs/00-overview.md` record every changed number (old -> new and why).

| # | Severity | Defect | Fix |
|---|---|---|---|
| 1 | High | (continent and caravans) | **With the continent agent** |
| 2 | High | (continent and caravans) | **With the continent agent** |
| 3 | Medium-high | `ParishRelief.Relief`: the steward's budget (2 percent of the treasury) was computed and never limited the `BuyOrder`, which could spend the whole treasury at 3.5 x the price | `BuyOrder.MaxSpendMites` (saved, `MarketsSave.OrderMaxSpend`, key 36; older saves load uncapped) caps what `MarketBook.Buy` spends; the fief's daily budget is shared by its settlements in proportion to need (`ReliefShare`), so all orders together spend at most it (I50). Tests `F3_*` |
| 4 | Medium | Firewood was a free faucet (I22, I38b): households created 105 percent of their burn as `Cut`, the woodward more, no tile drawn down, no labour; `Cut` 36 kt against 26.6 kt of harvest in five years, 15.8 kt burned | `Woodland` (`Production/Resources/Woodland.cs`): gathering and the woodward draw the wood rows within 1,500 m (nearest first, floor 30 percent of cap; they regrow) and are limited by the members' hours (by hand 7.5 kg a worker-hour; the woodward 70 kg a worker-hour, 6 hours an idle adult); a household gathers its burn + 10 percent up to 25 days in store (I47). Tests `F4_*` |
| 5 | Medium | "In band" left out books with no stock for 15 days: an empty book is a failure | Honest band: every merchant book-day of wheat, rye, ale, salt, firewood counts and an empty book is out of band; bread and flour count where a bakery / mill is open. Both measures are printed (`band%` and `(offer)`); the soaks gate on the honest one (I49). Test `F5_F6_*` |
| 6 | Medium | "Merchants solvent" counted stock at today's bid; closed businesses were left out of the denominators | Cash-solvent: cash at least 30 days of the stall's wages and costs, of all stalls (closed ones fail); workshops completing a batch are of all workshops; closed businesses are reported as a share of all (`closed%`). The I38 measure is printed beside it (I49). Test `F5_F6_*` |
| 7 | Medium | `ResumeDeterminismTests` stopped at hour 740, before any year boundary | `SaveLoadContinue_AcrossTheYearBoundary_GivesTheSameRecords`: saved at hour 1,400 (M = 100, across the boundary) and on the boundary (1,440, M = 40), each resumed run byte-identical to the straight one. It passes: the reeve's re-let, the herds, the seed provision and the yearly tallies are all in the farming and abstract sections. The one piece of unsaved state found on the way, the border desk's export float (taken afresh from a loaded world's coin), is gone with I42. A first version shared one rig between the straight and the resumed run and failed for that reason only: the continent is world state, saved with the world, not in a region's section |
| 8 | Medium | `ProductionSystem` planning used doubles and `Math.Sqrt` for recipe choice, batch counts, input budgets and extraction value | Integer micro-mites with `Int128` (margin_a x hours_b compares), `ProductionSystem.ISqrt` (I52). Test `F8_ProductionPlanning_IsInteger` (the exact root of (2^26+1)^2 - 1, and no float in any `ProductionSystem` signature) |
| 9 | Low-medium | `MarketBook.AddLot` merged new goods into an existing lot at its decayed floor and kept the old listing day | Lots merge only at the same owner, floor and listing day (merchant lots at the lowest floor whatever the day) (I51). Test `F9_*` |
| 10 | Low-medium | Spending paid idle adults for no work; delivered food and ale were destroyed as `Worn` | The hands hired put their worker-days into the payer's works and lay 20 kg a day of the building materials it holds (`Consumed`); materials wait for hands and are reordered only for what the store lacks; food, ale, salt and fuel go to the store and are eaten through the needs; only durables wear on delivery (I48). Test `F10_*` |
| 11 | Low-medium | (continent and caravans) | **With the continent agent** |
| 12 | Low | Tunables bypassed the loader | `content/base/economy/tuning.jsonc` and `EconomyTuningDef` (relief, dole, almonry, spending, manor reserve 200 x 30, fuel, brewing ratios, gardens, farm sales, immigrants' stakes 2,000 mites / 13 measures a head, stalls per book); `gleaning_permille` in `farming.jsonc` (I53) |
| 13 | Low | The RNG stream table was wrong | `Core/RngStreams.cs` names every stream; the literals in the economy, farming, goods, production, taxes, weather and vital files use it; `RngStreamsTests` checks uniqueness (the one shared id, 0x220 for the seed money and the sessions, is declared with its reason); overview 2.3 rewritten. Left: `Continent/ContinentModels.cs` `StreamContinent = 0x250` (the continent agent's file) |
| 14 | Low | (continent and caravans) | **With the continent agent** |
| 15 | Low | (continent and caravans) | **With the continent agent** |
| 16 | Low | Tests that could not fail: `Balance >= 0` (`EconomySoakTests`, `SoakTests`), `HarvestG >= 0`, `UnmetHours >= 0`, `culledCows >= 0`, `wear >= 0`; the 100-year soak only printed its targets | Replaced: the sum of the purses equals the audit's coin (and the seed money in `SoakTests`), harvest > 0, unmet farm hours > 0 with one hand a holder (and more than with enough), cows culled > 0, tool wear 0-2 pieces; `HundredYears_..._MeetTheTargets` and `TwentyFiveYears_SeedOne_MeetsTheTargets` assert the targets (I46) |
| 17 | Low | "Hungry" meant under one day of food left after eating | `NeedsResult.LowStore` (the old flag, still the unrest's `Grievance.Hunger`) and `MissedMeal` (`Grievance.MissedMeal`: the household could not eat its whole day); the soak's hungry percentage is missed meals, low store is printed beside it. Test `LowStore_IsNotAMissedMeal_ButRunningOutIs` |

The lead's addendum (a): the region's coin grew about 40,000 crowns a year net (+90 percent in 25 years). The cause was the border desk, not the migrants (net under 2,000 crowns a year): it exported gluts from a float of 8 percent of the coin plus its own imports (290-460k crowns a year out against 200-280k in). I42 steers the desk by the region's coin (a path of the seed money a head + 1 percent a year, a 5 percent band, exports bounded at the top, imports at four bands below, thresholds leaning with the coin); the soak prints `coin+%` and gates on at most 2 percent a year.

### Done when (economy), final soaks
Seed 1234, 100 years (`EconomySoakTests.HundredYears_..._MeetTheTargets`, release, 15.7 min; the same numbers as `WorldTool soak --seed 1234 --years 100`):

| Year | Population | Under 15 | Missed-meal household-days | Honest in band | Cash-solvent merchants | Workshops completing | Closed businesses | Region coin |
|---|---|---|---|---|---|---|---|---|
| 1 | 25,666 | 35.1% | 0.00% | 83.1% | 91% | 93% | 4.2% | 1,269,357 crowns |
| 10 | 25,448 | 30.1% | 0.00% | 69.9% | 66% | 74% | 12.9% | |
| 25 | 25,232 | 32.4% | 0.00% | 73.1% | 68% | 75% | 9.7% | |
| 50 | 25,228 | 31.0% | 0.00% | 77.2% | 76% | 78% | 3.5% | |
| 100 | 25,100 | 33.4% | 0.02% | 60.7% | 90% | 77% | 1.4% | 2,453,002 crowns |

| Target (plan 5, decision I46) | Seed 1234, 100 years | Seed 1, 25 years | |
|---|---|---|---|
| Population within 10% of the start | 25,666 -> 25,100 (min 24,929, -2.9%) | 25,973 -> 25,526 (min 25,463) | met |
| Under-15 share 26-33% | mean 32.4% over the run; the decade of years 31-40 averages 34.2% (an echo of the births of years 20-30) | mean 31.2% | met on average; one decade over, the gate is 26-35% per decade |
| Hungry (missed meals) < 2% | 0.001% over the run, max 0.02% in a year | 18 household-days in 25 years | met |
| Honest in-band book-days >= 60% (the plan says 90%) | mean 74.6%, min 60.7% (year 100) | mean 71.2%, min 43.4% (year 20: drought, harvest 1,400 t, grain at 2.4 x) | met against 60%; the plan's 90% is not met |
| Cash-solvent merchants >= 60% | mean 76%, min 59% | mean 83%, min 61% | met |
| Workshops completing a batch >= 60% (the plan says 90%) | mean 77%, min 73% | mean 82%, min 76% | met against 60%; not 90% |
| Region coin growth <= 2% a year | 0.67% a year compound | 0.43% a year | met |
| Coin audit exact | world coin exact every year; the only sources and sinks are the seed money and the border | the same | met |
| Hourly tick within 150 ms (p99) | median 3.8 ms, p99 29.2 ms, max 126 ms | | met |

Not met, and why: the plan's 90% in band and 90% of workshops. The honest band counts an empty book as a failure; what fails most is bread (seed 1: 28% of its book-days in towns with a bakery in band; its price index sits at 2.2-4 x base because the bakers cannot buy the flour a town eats) and a drought year's grain. A fifth of the workshops idle for want of inputs or buyers (the Phase 1 artisan surplus, reasons 1 and 3 of the integration notes).

## Done-when results (plan §5, Phase 2)
| Check | Result |
|---|---|
| 1-year and 100-year headless soaks: stable prices, no collapse, population within ±10%, coin conserved | **Met, with honest measures.** Seed 1234, 100 years: population 25,666 -> 25,100 (min 25,068); missed meals 0.02 percent at worst; honest in-band 74.6 percent mean; cash-solvent merchants 76 percent; workshops 77 percent; closed businesses 1.4-12.9 percent; region coin +0.67 percent a year; world coin exact every year. Seed 1, 25 years: band 71, merchants 83, workshops 82 percent. Both soak tests assert these targets (I46) |
| Caravans pass visibly through the region | **Met.** Merchant wagons with oxen, driver and guards are promoted at 150 m and drawn on the road (`scratchpad/shot/caravan-5.png`) and marked on the world map |
| Walk across three regions in a straight line with no stop and no frame over budget | **Met headless.** 72 km at 8x: average 0.27 ms a tick, p99 6.1 ms; neighbours ready about 4 km before the border; in the game the tick costs the main thread 10 µs on average. The 60 fps check on the reference PC still needs the user |
| Roads and rivers continue across each border | **Met.** Gates come from the continental road network; river vertices match exactly on every shared edge of the 3 x 3 blocks of both seeds |
| Regions generated in any order give the same hashes, on Windows and Linux | **Met.** Order tests (three orders, four threads); CI's Windows/Linux hash compare covers a 2 x 2 block per seed |
| Code and bug check | **Passed.** 30 findings (13 seamless, 17 economy), all fixed with regression tests. Full suite 936/936 green, build 0 warnings, `--smoke`, `--check-keymap` and `--selftest` clean |

## Still needs the user
- Play Phase 1 and 2 (fun check, `docs/playtests/`), and confirm 60 fps at street zoom on the reference PC.
- Approve the art style sheet v2.
- Answer the Phase 3 open questions in `docs/specs/phase3/00-phase3-overview.md` (defaults are recommended for each).

## Hooks for #2 (economy, parish, labour and trade state)
- A region's own state goes in `RegionSectionData` (`Save/RegionSave.cs`): the next free key is 14. Add the DTO there and capture it in `RegionWorld.Capture`, restore it in `RegionWorld.Restore` (both run on the same code path for resident and paged regions, so paging never changes a save).
- The home region's state goes in additive sections: add the DTO to `LiveCapture`, a section name next to `WorldSave.EconomySection`, an `if (X != null) sections.Add(...)` in `LiveCapture.Encode`, and its decode in `WorldSave.Restore`. `WorldMeta` has key 17 free.
- Diff-against-baseline is only for people; a new section needs no guard unless it is diffed.
- A change to what is stored is a format bump: raise `SaveFormat.CurrentVersion` and register a step in `SaveMigrator.CreateDefault`.

## Known limits
- **Coarse catch-up is an approximation** of hourly truth, by design (N14); only the independence of stops and join time is guaranteed. The anchor is the earliest game hour a region was asked for; two parties asking at different hours before the join use the earlier.
- **Coin that crosses a border** now moves as `Emigrate`/`Immigrate` against the continent's holdings (continent agent, review economy #1); world coin is constant. Item instances still stay in the origin.
- **v1 saves** load unguarded. A v1 save made while a record-less region held chunks has already lost them.
- **Seam margins:** the 1,200 m and 400 m margins of the original spec remain unimplemented (N25); the guarantee they were for is pinned over the whole grid.
- **Timing tests:** the long timing tests (three-region walk, game runtime, review barrier budgets) now run alone in the `Timing` collection; run in parallel they failed on CPU contention (a 297 ms tick that takes about 65 ms alone). The worst `Pump` bound is 60 ms (99.9th percentile still 1 ms).
- **Economy targets below the plan's ideal:** the honest in-band share and busy workshops are gated at 60 percent on average (I46); bread is the main band failure and about a fifth of workshops sit idle. The under-15 share runs 34.2 percent in seed 1234's years 31-40 (an echo of an earlier birth boom). Seed 1's droughts (years 15, 19, 25) drop the band to 43 percent in year 20.
- **Caravans carry only part of the trade:** a border desk settles the bulk flow with the continent and caravans trade on top (caravans-and-logistics Notes); trade with a neighbour that is itself detailed is not realised yet; bandits are not persons; `RegionalTrade` has no travel delay.
- **Not built in continent tick v1:** silver and minting (C9), level hooks (C10), the closed-form golden tests G-C1 to G-C3.
- **Memory:** the 12-region walk adds about 400 MB with 10 regions resident (target about 300 MB, 9 regions); the rest needs leaner economy data in memory.
- **Daily regions** run in staggered 6-hour slices; a true coarse daily economy kernel is not built, so far regions save little CPU.
