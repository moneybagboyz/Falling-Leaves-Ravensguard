# Background growth (Phase 3)

Implements plan 4A.2 "Background growth (from Phase 3)": the abstract world builds by itself, so a region's population can creep toward the land's capacity at a fraction of a percent a year while marriage stays gated on housing. Code: `src/Ravensguard.Sim/Growth/`, tunables: `content/base/economy/growth.jsonc` (`GrowthTuningDef`), tests: `tests/Ravensguard.Sim.Tests/Growth/`.

## 1. What it does

One system, `growth-seasonal` (H6, order `S6Growth` 6,650: after the marriage matching of S6 and before the migration of S7), runs in hours 0 to 4 of the first day of each season (one part an hour, decision G17: the whole step in hour 0 took up to 190 ms against the 150 ms budget) and calls `GrowthSystem.Seasonal`:

1. **Advance** every project in progress (a cottage, a clearing): buy or gather what is missing, pay the labour, finish what is done.
2. **Cottages** (3.1): start new ones.
3. **Clearings / assarting** (3.2): start new ones.
4. **New businesses** (3.3).
5. **Rural to town moves** (3.6).

Everything is integer, in ascending index order; ties are broken by index; the only randomness is the +-10 percent wobble of a project's hours (`RngStreams.GrowthWork`, 0x271, key = project serial, tick = the hour). `GrowthPick` (0x270) is reserved for future tie breaks. Nothing reads the wall clock.

## 2. State, save and replay

The generated region is a pure function of the seed and is never stored. Growth changes it (lots appended to plans, a vacant house taken up again, parcels and fields appended, `ParcelMap`, `Built` and `Forest` marked), so growth keeps a **log** (`GrowthState.Lots`, `GrowthState.Parcels`) and the saves store the log (section `growth`, and `RegionSectionData.Growth` for other regions) beside the projects in progress, the unmet-demand counters, the cleared tiles and the yearly tallies. On load the log is **replayed over the freshly generated region** (`RegionGrowth.Replay`, idempotent) before the people, farming and production sections are read; the resource layer's cleared rows are marked again from `ClearedTiles`.

The region keeps what it was at generation: `Region.PinGrowthBaseline` (called before the first change) records the lots, parcel counts, the three tile layers and the hash of the generated region. `Region.ComputeHash` keeps answering with the generated hash (so a save's region hash verifies), and `Region.GeneratedView` is the region as generated: the populator always works from it (`Populator.Populate`), so the baseline people a save is diffed against never depend on what was built. `ResourceLayer.Build` also builds from the generated view; a cleared row keeps its cap but reads zero and never regrows (`ResourceLayer.Clear`).

Save, load, save is byte for byte, with builds in progress (test `ASaveWithABuildInProgress...`), and a resumed region runs on to the same records as an uninterrupted one.

## 3. Rules

### 3.1 Cottages
- **Need** per cluster (the settlements of one fief, as marriage groups them): `waiting / waiting_per_cottage + crowded / crowded_per_cottage - free dwellings - builds in progress`. `waiting` = unmarried women with `MarriageWait > 0`; `crowded` = households over `crowded_over_pct` of their beds; `free` = dwellings marriage could use.
- **Land**: a settlement's *vacant* lots (houses that stand empty, generated `Lot.Vacant`) are refitted first (a share `vacant_repair_pct` of the work and materials); otherwise a **free plot on the plan's streets** (`RegionGrowth.FindPlot`): 16 x 38 m in a village, 11 x 28 m (burgage) in a town, on either side of a street, clear of lots, the square, other streets, other settlements' plots and the fields, on level dry ground, within the plan's radius plus `plan_margin_m`. Nearest the square first. This is the cap by land.
- **Pace caps**: at most `max_starts_per_cluster_season` starts per cluster and season; at most `max_starts_per_year_per_ten_thousand` x people / 10,000 starts a year; new dwellings at most `max_new_dwellings_pct_of_base` percent of the generated ones.
- **Payer**: the household of a waiting woman in the settlement that can afford it, else the fief's lord (his purse topped up from the fief's treasury, reason Stipend). A build is paid for in coin as it goes.
- **Cost**: 250 worker-hours (burgage x1.6) and materials per `cottage_materials`: 400 kg of logs (the poles), 1.5 t of clay (daub), 3 t of thatch. **Poles** are cut in the settlement's woodland (`Woodland.Draw`, at most `poles_from_woodland_pct` percent), **daub** is dug from the clay pits within 3 km, **thatch** is the commons' straw bought from the fief's treasury at the book's reference price (the thatch good has no producer unless a carpenter binds it, and nothing orders it); what these cannot give is bought at the market (`MarketBook.Buy`, a Trade transfer; the demand registers in the book). Bought and gathered goods are consumed as they come (`DestroyReason.Consumed`), so the mass audit stays exact. Gathering costs hours (`gather_kg_per_hour`).
- **Labour**: the owner's household members aged 14-59 (`own_hours_per_member_day`; none for gentry) and up to `crew_max` hired idle hands (`hired_hours_per_hand_day`), each paid the unskilled wage as a Wage transfer. Labour is limited by the share of materials in hand. A build takes at least `min_seasons` and normally one or two.
- **Done**: a `Lot` with a dwelling and beds is in the plan (or the vacant one is occupied); the population's per-lot arrays grow; the chronicle has `cottage_built`. Marriage uses it from then on (`Marriage.FreeDwelling` and the weekly catch-up). A build whose payer is gone, or that makes no progress for `give_up_seasons`, is dropped.

### 3.2 Assarting
- **When**: a settlement's arable acres a head under `target_acres_per_person_x10` (2.5: the plan's 2.3 arable acres plus the margin of E10; the generated region has 2.28); the biggest deficits first, at most `assart_max_starts_per_season`, one project a settlement, the fief's lord pays (treasury help as above).
- **Land** (`RegionGrowth.FindClearing`): `assart_tiles_per_project` tiles of woodland (tree cover 30+) or waste (too poor for pasture), level, dry, unbuilt, off roads and mines, within `assart_max_radius_m`, touching the settlement's own fields (or a tile already chosen), nearest first. When none touches, nothing is cleared: the land cap.
- **Work**: `assart_hours_per_acre` worker-hours an acre from hired idle hands (`assart_crew_max`), wages paid as they go.
- **Done**: the wood on the tiles (`ResourceLayer.Clear`) is taken to market as firewood (`assart_firewood_pct`) and logs, created in the lord's store (reason Cut); a new parcel is appended (`ParcelRecord`: tenant parcel of the settlement, fallow slot 2, each field's fertility the tile's times the generated fields' scale, `Forest` 0); farming's tables grow (`FarmingSystem.AddParcels`), meadow acres with them; the lord holds it until the year's re-let gives it to a landless household.

### 3.3 New businesses
A good a workshop kind makes is *wanted* when its book's price is over `unmet_price_permille` of the reference and the stock covers under a day of demand; `unmet_seasons` seasons in a row make a new business. One open business of a kind per `people_per_business` people of the settlement at most, none where a closed one waits for `LabourMarket.Reopen`, at most `business_max_per_season` a season. The owner is an idle adult of the settlement whose household holds `business_min_owner_mites`, the trade first, then a merchant, then the most skilled. Half his household's coin goes in as capital (Gift); when that falls short the fief's treasury advances up to `business_treasury_advance_mites` (Stipend). Smithy and tavern kinds need a plot (a new lot, paid to idle hands as wages); other kinds work from the owner's home lot. The labour market hires the staff.

### 3.4 Chunks (plan 2.2)
- A chunk **never visited** has no entry in the chunk store: it is generated from the changed plan (`ChunkGenerator` rebuilds its lot, settlement and crop indices and drops its stamps when `Region.GrowthVersion` changes, under a reader-writer lock), so new lots, cottages and fields render when the player first comes.
- A chunk **already visited** (a snapshot exists) gets an **ID-keyed `ChunkDelta`**, never a regeneration (`ChunkDeltaWriter`): before the change the chunk is generated as it was, after it as it is, and the difference is written as SetTile ops (the building's cells, the fields' ground, the vacant house's hearth and beds) and RemoveObject / AddObject ops (trees felled by a clearing, the new lot's yard props), keyed by the props' stable ids. The snapshot is never rewritten and the player's own edits elsewhere stay. `GrowthSystem.ChunkSink` is that writer, set by the game's runtime; a headless world has none.
- Tests: `ANewBuilding_InAVisitedChunkIsADelta_InAnUnvisitedOneIsGenerated`.

### 3.5 Rural to town (decision G12)
A town with free houses and posts its businesses cannot fill takes up to `town_moves_per_town_season` landless peasant households with an idle adult from the villages a season (the wage on offer is the draw); the household keeps its coin and goods, only its home changes.

## 4. Soak gates and report (decision G13)
`EconomySoak` rows have `Dwellings`, `FieldAcres`, `NewCottages`, `NewVacantRefits`, `NewAcres`, `NewBusinesses`; the table prints `dwell fieldac +cot +refit +acres +biz` and the yearly businesses open/closed were already there. The population gate of every soak is **-10 percent to +25 percent of the start every year, and growth at most 0.5 percent a year averaged over each decade** (the closing part of a decade by its own length), in place of "within 10 percent". Every other Phase 2 target stays: missed meals under 2 percent, honest band, cash-solvent merchants and workshops (50 percent a year, 60 percent mean), under-15 share by decade, the region's coin growing at most 2 percent a year, the world's coin exact.

## 5. Decisions

| # | Decision | Why |
|---|---|---|
| G1 | Growth is one seasonal system after marriage matching; no hourly work. | the abstract spec's seasonal slot; a season is the unit of a build |
| G2 | The generated region is never stored; the log is replayed (idempotent) over the regenerated one, and the generated hash, generated view and populator baseline stay as generated. | saves stay small; baseline people and layer hashes must not depend on play |
| G3 | Cottages come from vacant lots first, then plots on the plan's streets; no new streets. | cap growth by available land; a street is a Phase 5 decision |
| G4 | Materials: poles from the woodland stock, daub from clay pits, thatch from the commons' straw, the rest at the market; consumed as they come. | thatch has no producer; the books stay honest and the mass audit exact |
| G5 | Labour is paid as Wage transfers to real idle hands; the lord's purse is topped up from his fief's treasury. | coin conserved, idle hands get work |
| G6 | New fields are a new parcel in the fallow slot, held by the lord until the next re-let; fertility = the tile's times the generated scale. | farming's rotation and re-let stay unchanged |
| G7 | Clearing marks the wood row as cleared (cap reads 0, no regrowth) instead of deleting rows. | row indices are shared by the woodland and the save diff |
| G8 | New businesses open only after `unmet_seasons` seasons of price over 1.15 and stock under a day; capital from the owner, treasury advance when short. | the same bar for closing and for opening |
| G9 | Immigration no longer stops at the start head count: a sixth of any shortfall (as before) plus a quarter of the free dwellings the region's own waiting couples do not need, while idle adults are under 3 percent, at most 0.3 percent of the people a season. | immigration should follow vacancies and jobs (lead's item 1; was integration decision I27's cap) |
| G10 | Unvisited regions grow in closed form: each summary has a `DwellingCap` from its cells' soil and biome (10 percent plus 0.35 percent a fertility point, at most 45 percent, none on mountain, desert, tundra); while vacancies are under 2 percent, dwellings and the grain capacity grow an eighth of a percent a season. No coin moves, so coin stays conserved. | regions nobody visits grow too |
| G11 | Cow cap was divided by 10,000 instead of 100: no heifer was ever kept and the herd fell about 1 percent a year (5,800 to 2,100 over a century). Fixed; meadow acres grow with new fields. | lead's item 4 |
| G12 | Rural to town moves (3.5). | towns grew by drawing the countryside |
| G13 | Soak gates: -10 percent to +25 percent, at most 0.5 percent a year per decade (4). | background growth |
| G14 | Marriage ages in `VitalRates.EconomyDefault`: brides 22, grooms 25 (were 21 and 23). | housing is the brake now; the historical range is 22-26 for women |
| G16 | The assart target is 2.5 acres a head with up to 5 clearings of 8 acres a season: at 2.3 the extra mouths pushed the grain price up and the honest band fell under 40 percent in two dry years of the century. | measured: 100 years of seed 1234, band minimum 35 percent at 2.3, 60 percent at 2.5 |
| G17 | The seasonal step is split over hours 0-4 of the season's first day (projects, cottages, clearings, businesses, town moves). | worst hour budget 150 ms (abstract spec E4) |
| G15 | Soil improvement (marling, manuring as improvement projects) is **deferred to Phase 5**: it needs a per-field fertility cap in `FieldTable`, a lime and marl supply chain and the construction-job contracts of Phase 5, and nothing else in Phase 3 reads it. The fallow credit and grazing already raise fertility a little. | lead's item 7 |

## 6. Known limits
- A new house takes the next free *plot of the plan*: no new street, so a village stops growing when its streets are full (the cap by land).
- The visited-chunk delta is written to the store; the streamer's loaded chunks pick it up when they are next rebuilt from the store (the game's runtime owns when).
- The farm labour planner scales with the new parcels by itself; a manor's staff slots (stewards, reeves, guards) are fixed by content and do not grow with demesne acres.

## 7. Results (release, `WorldTool soak`)

Seed 1234, 100 years (17 minutes): population 25,670 to 27,082 (+5.5 percent, 0.05 percent a year; every decade at most 0.5 percent a year), missed meals at most 0.03 percent, honest band mean 64.7 percent (minimum year 60.4), cash-solvent merchants mean 77 percent (minimum 66), workshops mean 69 percent (minimum 64), under-15 share 32.1 percent after the first decade (decades 29-34), region coin growth 0.77 percent a year. Seed 1, 25 years: population 25,971 to 26,127, band mean 74.0 (minimum 67.1), under-15 30.0 percent.

| year | people | dwellings | field acres | businesses open | hungry % | band % | solvent merchants % | workshops % | idle (landless) |
|---|---|---|---|---|---|---|---|---|---|
| 1 | 25,670 | 5,002 | 58,693 | 964 | 0.00 | 83.0 | 91 | 93 | 13 |
| 10 | 25,672 | 5,598 | 60,173 | 996 | 0.00 | 69.0 | 76 | 70 | 206 |
| 25 | 25,715 | 5,898 | 62,461 | 1,248 | 0.00 | 64.8 | 73 | 68 | 388 |
| 50 | 26,059 | 5,898 | 64,570 | 1,468 | 0.00 | 62.9 | 82 | 69 | 1,054 |
| 100 | 27,082 | 5,898 | 65,856 | 1,499 | 0.01 | 63.9 | 74 | 66 | 2,101 |

Dwellings stop at +18 percent of the generated ones (the cap by land, year 17); cows 5,794 to 6,841 (decision G11); idle adults are still rising (item 5 below).

## 8. Lead's list: status
1. Immigration cap removed: done (G9). 2. Unvisited continent grows: done (G10). 3. Marriage ages: done (G14). 4. Herds: done, the cap bug (G11). 5. Idle adults: partly done: farm labour demand follows the parcels by itself and the new fields are worked by hired idle hands until the re-let, the 174 businesses opened in 15 years average 3.1 staff and hire the idle, but landless idle adults still rise (a manor's slots are fixed content; the re-let of parcels leaves landless households without fields): **open**, a labour-market design question for Phase 5 (day labour contracts). 6. Rural to town moves: done (G12). 7. Soil improvement: deferred to Phase 5 (G15).
