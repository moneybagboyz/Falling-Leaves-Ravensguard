# Economy audit (Phase 7 step 0): the adversarial review, the war-load soak, findings and fixes

Status: design, written before any Phase 7 code, on top of b6b39e2 (Phase 6 closed). Plan refs: §5 Phase 7 step 0 ("before armies are built, a review red-teams the whole economy and its findings are fixed first": exploits, money loops, arbitrage, buy-low-sell-high with no cost, duplication; collapse under war load; bottlenecks iron, charcoal, timber, grain, carts, labour; coin and mass leaks; growth headroom after war; "it ends in a war-load soak, run before the Phase 7 war soak, and a findings list in the review"), §3.3 (closed money supply; soak tests watch inflation and deflation with a scripted rich-player bot), §4A.1 (grounded rates, wages, prices, logistics, money), §3d (supply 1 kg of grain a soldier a day; levies out of the harvest; unpaid or starving armies desert), §3j (purveyance), the Industries row of the locked decisions (the audit checks the 2-3 year payback, whether markets absorb the output and whether merchants have the coin).

Conventions (as Phase 6): integer arithmetic in the sim; `DetRng` streams keyed on game time and ids; money in **mites** internally (`Economy/Coin`); from `coinage.md` 1 cp = 10 mites, 1 sp = 100 mites (the old crown), 1 gp = 1,000 mites, and amounts below are written in mites with the coin form in brackets where it helps. A game year is 60 days (4 seasons of 15), a working week 6 days.

## 1. Purpose
Phase 7 puts the heaviest load the economy will ever carry on it: 2,000 men eating, paid, armed and carted for seasons, levies pulled from the harvest, purveyance, sieges that starve towns, and staged industries that can out-produce a whole region. Before any of that is built, the economy is attacked on purpose by four red-team roles, its leaks and loops are measured, and every High and Medium finding is fixed with a regression test. The audit ends in a **war-load soak**: the economic demands of a war imposed by a scripted load generator (no soldiers, no battles), which is also the first implementation of the interface the military specs call (`army-economy.md` section 3). The military parts start only when the gate in section 7 is met.

## 2. What exists and what is new

| Exists (by file) | Used for |
|---|---|
| `Economy/Ledger.cs`, `Economy/LedgerTypes.cs` (`ConservationReport`: `InPurses + Sunk - Sourced = 0`, `OnTransfer` trace, per-reason counters) | coin audit every day of every soak |
| `Goods/MassLedger.cs` (`created - destroyed == stock`, per good and reason), `Goods/Store.cs` | mass audit |
| `Economy/MintMath.cs` (pure strike; **no caller in the world**: grep finds only tests), `Economy/MoneySeeder.cs` | finding S-3 below |
| `Economy/Markets/*` (`PriceCurve`, per-unit repricing, `MarketBook`, `ContinentTrade.BoundDeskExports`, `PayBudgetMites`), `Caravans/CaravanStep.cs` and `ContinentDesk.cs` (exports paid by `SourceReason.Immigrate` within the continent's season budget) | money loops, the border faucet |
| `Production/RecipeBook.cs` (the price solver; `PriceSolverTests.ContentMatchesSolver`), `content/base/goods/*.jsonc`, `production/extraction.jsonc` | price targets, the R1 rule (4.6) |
| `tests/.../Abstract/EconomySoakTests.cs`, `Production/SoakWorld.cs`, `tools/Ravensguard.WorldTool/SoakCommand.cs` (`soak --seed --years --coin --flow --tax`), the Phase 6 soak harness (`Phase6Links`, P6-G2) | the soak bed |
| `Taxes/TaxSystem.LevyQuota` (data only), `Rule/*` (loans, subsidies, tallage, dues), `Unrest/*` (the purveyance hook `UnrestBook.Add(hh, "purveyance", day, entry)`), `Disease/*` (`ArmyCampSize` hook, unfed) | war-load consumers |
| `docs/reviews/phase-6.md` S10 (`Gaol` rations paid by `SinkReason.Burn`), S14 (tribute to an abstract realm moves no coin), G1 (headless runs do not attach law, family, contracts) | known leaks to close first |

**New:**
- `tests/Ravensguard.Sim.Tests/EconomyAudit/` — the probes (EA-1 to EA-12) and the bots (exploiter, rich player).
- `Sim/WarSupply/WarLoad.cs` — the scripted load generator (section 5.1). It is a client of `Sim/WarSupply/HostSupply` (`army-economy.md` 3); the military's real hosts later replace it through the same calls.
- `tools/Ravensguard.WorldTool/EconAuditCommand.cs` — `econ-audit --seed N` (static checks and probes, prints the checklist with pass or fail) and `war-load --seed N --years 25` (the soak, prints the yearly war table and exits 1 on a failed gate).
- `docs/reviews/phase-7-audit.md` — the findings list (section 6), kept until Phase 7 closes, then folded into `docs/reviews/phase-7.md`.

## 3. Method: four roles, four passes
Each pass is run by a separate helper with its own checklist rows (section 4). A row is closed by a test, a soak measure, or a written argument checked by the reviewer.

| Role | Asks | Tools |
|---|---|---|
| **The exploiter** | can a player make coin or goods from nothing, or faster than a skilled wage, by repetition? | scripted hero commands through `HeroDesk` (recorded, replayable), the exploit bot (0x701) |
| **The warlord** | what breaks when 2,000 men live off the land for seasons? | `WarLoad` (section 5) |
| **The accountant** | does every mite and gram reach a documented source or sink? | the ledger and mass audits per day, the coin-flow trace (`--flow`), a grep of every `Mint`, `Sink`, `Destroy` and `Create` call |
| **The grower** | after the war, does the region recover; does the money supply grow 1-2% a year; can new industries find buyers and labour? | the 25-year soak's peace years, the rich-player bot |

**Passes:** (a) a static read of every coin and goods path added since Phase 2 (a grep list in the findings file, each line marked checked); (b) probes: small scripted worlds that run one loop for 30 game days and measure its profit; (c) bots: the exploit bot tries every loop of 4.1 for a game year on seed 1234 and reports the best profit per game day; the rich-player bot holds 500,000 mites (500 gp) and buys, builds, lends and mints for 5 years; (d) soaks: the war-load soak (section 5) on five seeds.

**Rule for an exploit:** a loop is a finding when, after every real cost (wages at the class rate for the hero's own hours, tolls, spread, carting, wear), it yields more than **1.5 x the skilled wage (525 mites a day)** for the hero alone, or any profit with no goods or labour in it, or grows without limit with repetition. Trade, risk and skill may pay well; free money may not.

## 4. The checklist

### 4.1 Exploits, money loops, arbitrage, duplication
| Row | Loop | Measure | Pass |
|---|---|---|---|
| X1 | Buy at one book's ask, sell at a neighbour's bid (8 km), on foot (30 kg) and by cart (500 kg) | profit per game day after the 8% half-spread each side, 5% toll, carting 18 h | <= 525 a day on foot; by cart only where the price notebook showed a real gap (a "trade" profit, logged) |
| X2 | Sell to the gate's export desk, buy back from the same market | `ContinentDesk` bid vs book ask in the same hour | bid < ask always (the desk pays the book's ref times its discount) |
| X3 | Craft, then salvage or disassemble (`salvage_pm` 700-900 on pieces; `ItemTable.ValueMites`) | value out / value in over a loop | < 1 for every recipe and piece (salvage never returns more than the materials) |
| X4 | Build with bought materials, demolish, sell the salvage | as X3 | loses 10-30% |
| X5 | Loot items valued below their material: `sites/loot_tables.jsonc` `gold_torc` 300 g of gold at 1,400 mites (gold at the coinage rate is about 909 mites a gram: 272,700 mites); any item whose `value_mites` < its material's melt value | melt value / listed value | <= 1.2 for every loot row (fix F-2: loot values computed from material and labour, `ItemMath`) |
| X6 | Melt coin when bullion is dear; strike when it is cheap | silver book price vs the mint rate (face less seigniorage) | a margin exists only inside the 7.5% seigniorage band; no loop |
| X7 | Copper token coin (face 10 mites, about 0.85 mite of metal, `coinage.md` 3) | can anyone strike it but a mint? | only mints hold dies; the copper cap (`coinage.md` 5.1) bounds the mint's own faucet |
| X8 | Loans: borrow, lend on, default | `Rule/RuleLoans` | interest and principal only move coin; a default forfeits security and closes credit (RI-13, RI-14 stay green) |
| X9 | Stall footfall (`stall_footfall_mites_per_household` 1) and fair days | does any stall income come from no purse? | every mite from a household purse |
| X10 | Contracts escrow, bonds, tournament purses (Phase 6 S2 fixed) | a contract that pays without the work | none |
| X11 | Duplication: split, merge, move stacks across stores, carts, saves and the LOD bridge; items abroad (`AbroadDealing`) | `MassLedger` and `ItemTable` counts before and after 1,000 random moves (0x701), and a save and load between each | exact |
| X12 | The industry output slider at 5x (`industries.md` 5.7) | a works' profit per day and the region's price index | payback shortens by about the slider; no coin appears (the slider scales goods, never coin) |
| X13 | Arbitrage between realms' mints (seigniorage 500-1,000 bp) through a money changer (20 permille fee) | gold-silver loops | the changer's fee exceeds the seigniorage gap, or the gap is a trade profit by caravan with road risk |
| X14 | `Standing.PriceMites` (A3) not yet called by the market book | a rebate paid from nowhere when it is wired | wire it as a merchant-side transfer, or leave it unwired; never a mint |

### 4.2 Collapse under war load (run by `WarLoad`, section 5)
| Row | Load | Measure |
|---|---|---|
| W1 | 2,000 men fed 1 kg grain a day for 15-30 days a campaign, 300 horses (8 kg hay, 2.5 kg oats a working day: `animals/species.jsonc`) | grain and fodder bought, purveyed and foraged; price index on the march route |
| W2 | Paid: 1,040 levies (unpaid the first 40 days of service, then 150), 560 retinue and mercenaries (250-350), 400 men from outside the region paid by the realm | treasury days funded; arrears; desertions reported |
| W3 | Armed: 960 kits from the lords' armouries, the rest bring their own by wealth | days to fill; first input to run dry |
| W4 | Levies out of the harvest (1 in 5 households in war) | harvest shortfall vs a run without the load |
| W5 | Purveyance in two campaign years | grievances, riots, tallies unpaid |
| W6 | A 60-day siege of the region's largest town (no imports, no exits) | days of food; deaths; prices inside; recovery |
| W7 | Baggage: 60 carts (30 t) for 15 days of grain | carts and draught animals in the region; carts purveyed |

### 4.3 Bottlenecks
| Row | Input | Measure each season |
|---|---|---|
| B1 | **Iron** | bar iron stock in days of cover per book; region iron output vs demand (tools and nails wear, horseshoes, kits); a kit of 4 kg of iron for 960 men is 3.8 t of bar |
| B2 | **Charcoal** | woodland acres cut a year vs coppice regrowth (5 m3 an acre a year); a bloomery's 1.5 kg of charcoal per kg of ore |
| B3 | **Timber** | logs and planks for carts, siege engines, ships and building; standing stock by tile |
| B4 | **Grain** | the region's surplus after households, the levy's lost labour and the army's 2 t a day |
| B5 | **Carts** | vehicles and draught animals in the region (`VehicleTable`, animals) vs a baggage train's 60 |
| B6 | **Labour** | idle adults (Phase 5 gate < 800); men missing from the harvest; wages offered vs class rate |

### 4.4 Coin and mass leaks
| Row | Path | Pass |
|---|---|---|
| L1 | Every `Ledger.Mint` call (today: seed, immigration, the continent desk and caravans, starting estates; `Phase6Trail` test seed) | each is a documented source with a matching continent-side sink (decision I33) |
| L2 | Every `Ledger.Sink` (melt, burn, emigrate, gate tolls on the continent side) | `Burn` only in tests: **fix F-3** for phase-6 S10 (prison rations to the nearest baker or the seat's treasury) |
| L3 | Abstract deltas standing in for coin (phase-6 S14, tribute to a realm with no purse) | a documented holding purse; real coin on the side that has a purse |
| L4 | Every `Store.Destroy` reason (`Eaten, Burned, Spoiled, Worn, Sown, Consumed, Lost, Export, Materialised, Other`) | no `Other` in the war load; Phase 7 appends `Waste` (tailings, slag) and `Trampled` (forage) |
| L5 | Headless soak attachments (phase-6 G1) | the war-load soak attaches law, family and contracts as the game does (`WorldRuntime` does it after the G1 fix) |
| L6 | Money supply growth from mines once mints are wired | see S-2 |

### 4.5 Growth headroom after war
| Row | Measure | Pass |
|---|---|---|
| G1 | Population at Y25 vs Y5 | >= 95% |
| G2 | Wages paid a day region-wide (the GDP proxy) at Y15 vs the Y3-Y5 mean | within 10% |
| G3 | Idle adults at Y15 | < 800 |
| G4 | Money supply growth Y1-Y25 | 0.5-2.5% a year on average; no year below -5% or above +8% |
| G5 | Room for new works: unfilled vacancies and land for an L1 iron, clay and wood works within 4 km of a town | at least one site each on seed 1234 and three of the other four seeds |

### 4.6 Industry targets (with `industries.md`)
| Row | Measure | Pass |
|---|---|---|
| I1 | Payback of the scripted L1 iron, clay and wood works at 1x, near a town (IN-20) | 100-200 game days (about 2-3 game years) |
| I2 | Markets absorb: the works' daily output vs the books' demand EMA and the gate desk's room | an L1 works sells 80% of its output within 5 days at >= 90% of the opening bid |
| I3 | Merchants have the coin: the buyers' cash at the books in reach | the region's merchants hold >= 30 days of the works' sales |
| I4 | Raw output barely covers wages | a raw stage alone (mine, pit, felling) earns 1.0-1.5x its wage bill at book prices |

### 4.7 Suspects found while writing this spec (each to confirm or close in pass a)
| # | Evidence (read from content and code at b6b39e2) | Why it matters | Candidate fix |
|---|---|---|---|
| S-1 | Raw extraction is worth about **8.7x its wage** at base prices: a miner's 500 kg of ore at 2.6 mites/kg is 1,300 mites a day; a woodcutter's 1,050 kg of logs at 1.25 is 1,312; a clay digger's 2,000 kg at 0.65 is 1,300; the unskilled wage is 150 | breaks I4 and the payback target (a mine would repay in weeks); the profit flows to generated owners as dividends | **F-1, the R1 rule** (4.8): raw prices from labour cost; re-run the price solver for derived goods |
| S-2 | Silver yield: `cupel_silver` gives 3 kg of silver from 1 t of galena (0.3%); a 20,000 t ore body holds 60 t of silver, **about 46 M crowns, 37x the region's seed money** (1.25 M) | once a mint strikes it, one mine inflates the region beyond any band | **F-4:** grade by deposit (0.01-0.05% typical, about 0.02% default) so the region's worked deposits at historical crews mint 1-2% a year (`coinage.md` 5.6) |
| S-3 | **No mint runs**: `MintMath.StrikeInto` has no caller; the one-year soak asserts `Minted == 0`; silver piles up as a good | the money supply grows only by border trade | `coinage.md` 5.1 wires mints |
| S-4 | Loot below material (X5) | a melt loop worth 195x once gold has a market | F-2 |
| S-5 | Phase 6 S10 and S14 | coin leaves by a test path; tribute costs nothing | F-3 |
| S-6 | The border faucet: exports to the continent are paid by `Immigrate` up to the neighbour's season budget | an industry that exports at scale pulls coin in each season; bounded by `PayBudgetMites`, so check the bound holds against an L3 works | measure; keep |
| S-7 | Coin mass is `mites x 13 / 1000` g (`Inventory/Encumbrance.CoinMassG`), silver only | with gold, a war chest's mass changes 12x | `coinage.md` 5.3 |
| S-8 | Prices are fractional mites per kg in content (`base_price_mites_per_kg` 0.65 for clay) and micro-mites in books (`RefMicro`) | a literal "store in copper" would lose them | `coinage.md` 2 (no stored number changes) |

### 4.8 The R1 rule (labour-cost pricing of raw goods), decided in pass a
`raw_price_per_kg = class_wage_per_day x 1,250 / 1,000 / output_kg_per_worker_day` (25% for tools, rent and the owner's return), with output the composite rate of the chain's stages that make the good (`industries.md` 5.1: hewing, hauling and dressing for ore). Derived goods stay with the price solver (inputs + labour at class wage + 25%). Candidate numbers for the worked examples in `industries.md` (pass a confirms or replaces them; IN goldens pass prices as parameters so a retune does not break them):

| Good (mites per kg) | Today | Candidate | Good | Today | Candidate |
|---|---|---|---|---|---|
| logs | 1.25 | 0.20 | charcoal | 19.3 | 12 |
| firewood | 2 | 0.30 | iron bloom | 294.9 | 240 |
| iron ore, run-of-mine (new `iron_ore_rough`) | - | 0.60 | iron bar | 493.4 | 480 |
| iron ore, dressed (`iron_ore`) | 2.6 | 1.8 | planks | 3.6 | 3.0 |
| clay | 0.65 | 0.10 | bricks | 5.6 | 1.2 |
Households' fuel spend falls with firewood (about 85%); the soak checks the coin that frees goes to other goods and that no deflation spiral follows (G4).

## 5. The war-load soak

### 5.1 The load generator (`WarLoad`)
A scripted host with no soldiers: a purse (its pay chest, a `Chest` purse), a store (its baggage), a position on the road graph that walks a fixed route at the army column's pace (1.3 m/s, plan §1), and a roster of counts by role. Each game day at the host slot (`army-economy.md` 3.5) it calls `HostSupply.Needs` and `HostSupply.Supply` exactly as a military host will, and applies what it gets: men fed or hungry, paid or in arrears. It reports desertion as the military rule will (`army-economy.md` 4.11: `Desert` is the military's decision, the load uses a stand-in of 1 man per 100 unpaid or unfed man-days) and returns deserters to their households or, outside the region, to the continent. Draws: muster day jitter (+-2 days) and forage tie-breaks on stream **0x700** (key war, season). Levies are real people (`HostSupply.Muster`: the households lose their labour while they serve). The siege is `HostSupply.SiegeBlockade(settlement, on)`.

### 5.2 Scenario (25 game years; seasons spring, summer, autumn, winter = days 1-15, 16-30, 31-45, 46-60)
| Years | Load |
|---|---|
| Y1-Y5 | peace; Y3-Y5 are the baseline means |
| Y6, Y7 | a campaign from day 16 (after sowing) to day 30 (home for the harvest): 2,000 men (1,040 region levies, 560 retinue and mercenaries hired in the region, 400 from the realm outside), 300 horses, 60 carts; march out to the region's border gate with the realm at war and back; Y7 uses purveyance in the home realm |
| Y8 | the war is "worth the shortfall": days 16-45 with the levies out through the harvest; purveyance; from day 31 a **60-day siege** of the region's largest town (to Y9 day 30), besiegers foraging within 4 km |
| Y9 | the siege ends; a 15-day campaign; a loan of 100,000 mites to the realm from the richest merchant (repaid over 4 seasons) |
| Y10 | a 15-day campaign; peace at day 45 |
| Y11-Y25 | peace and recovery |
The same scenario runs on seeds **1234, 1, 7, 42, 9001**. A second run of seed 1234 with no load (the control) gives the baseline for "caused by the war" measures.

### 5.3 Gates (pass or fail; the tool exits 1 on any failure)
| Gate | Pass |
|---|---|
| WL-1 coin | `ConservationReport.IsConserved` every day, region and continent (`CoinConserved`) |
| WL-2 mass | every good's audit exact every day; no `Other` destroy reason from the load |
| WL-3 money supply | G4 |
| WL-4 bread on the march route | staple price index of the towns within 8 km of the route peaks at <= 3,000 permille of Y3-Y5 (the clamp is 4,000) and is <= 1,300 by Y12; the region's mean peaks <= 2,000 |
| WL-5 starvation | outside the besieged town, deaths from starvation in Y6-Y10 <= 3% of the Y5 population; inside it <= 40%; none in the control |
| WL-6 harvest | **waived by the user (overview 10e, 21 Nov 2026: war costs no harvest here)**; the Y8 and Y11 measures are printed as information (was: Y8 10-35% below the control, Y11 within 10%) |
| WL-7 unrest | every settlement purveyed has grievances that season; at least one reaches rung 2; no realm ruler changes from unrest alone |
| WL-8 treasury | the realm treasury pays or records arrears; the stand-in desertion fires when it cannot; no purse below zero (the ledger guarantees it) |
| WL-9 kit | 960 kits filled within 30 days of the Y6 order from armouries, markets, commissions and arms imports, or the shortfall names its first dry input (B1-B6) |
| WL-10 recovery | G1, G2, G3, with G2 read against the control at the same year (EA_F10, accepted 21 Nov 2026; EA3b): wages a day within 10% of the control's; the Y3-Y5 line is information |
| WL-11 merchants | cash-solvent merchants >= 60% each year (Phase 2 gate), >= 40% in Y8-Y9 |
| WL-12 chronicle | specific coverage >= 0.90; a staple price rise in a town the army bought from within 5 days names the army (not "market drift") |
| WL-13 determinism and saves | two runs of seed 1234 give equal hashes at Y10 day 60; save, load, save byte-identical at Y6 day 20, Y8 day 40, Y9 day 10 (mid-siege) |
| WL-14 budget | worst abstract hour <= 150 ms; mean game day <= the control's + 10% |
| WL-15 rich player | with the rich-player bot, the price index stays within 1,200 permille of the run without it, and every mite the bot gains traces to a ledger transfer |

### 5.4 Output
The yearly table of `soak` plus war columns: men fed and hungry man-days, purveyed kg and tallies owed, foraged acres, the siege town's days of food, deserters (stand-in), kits filled, the first dry input per chain, starvation deaths, harvest vs control, wages paid a day, money supply and growth, the coin and mass audits. Written to `war-load-<seed>.txt` and summarised in the findings file.

## 6. Findings: how they are recorded and fixed
- **File:** `docs/reviews/phase-7-audit.md`, in the Phase 6 review's table form: `| # | Severity | Where (file:line at the audited commit) | What is wrong | Failure scenario | Suggested fix | Confidence | Status |`, ids **F-1, F-2...** (F-1 to F-4 are taken by section 4.7's candidates). The static-read grep list and every checklist row (X, W, B, L, G, I) with its result follow the table.
- **Severity:** **High** = coin or mass not conserved, a loop above the exploit rule, a soak gate failed, a save that does not round-trip; **Medium** = a target missed (payback, absorption, recovery) or a consequence invisible (no grievance, no cause); **Low** = calibration or wording.
- **Fix rule:** each fix is its own commit with a regression test named `EA_F<n>_<what>` that fails before the fix; content retunes re-run `PriceSolverTests` and pin the new numbers; a fix that changes a stored meaning follows the save rule (section 8). After all fixes the full war-load soak runs again on the five seeds.
- **Gate M-E0 (military may start):** every High and Medium finding fixed or accepted in writing by the user (the file records who and why); WL-1 to WL-15 green on the five seeds; `econ-audit` prints no failed row. Low findings may stay open into the Phase 7 close-out.

## 7. Integration with the other Phase 7 specs
The audit runs twice: **first** on the Phase 6 economy (before coinage, scale, buildings and industries land), so its findings are fixed on known ground; **second**, the same soak with `coinage.md` (mints wired), `economies-of-scale.md` and `industries.md` in (the scripted L1 works and an L3 iron works near the route), before M-E0 closes. The military's own war soak (plan §5 done-when: 25 years, several realms) reuses the war table and gates WL-1 to WL-15 with real hosts in place of `WarLoad`.

## 8. Saves
The audit adds no save section. `WarLoad`'s state is the host's supply accounts and lives in `army-economy.md`'s optional `warsupply` section, so WL-13's mid-siege round trip tests that section. Fixes that only retune content are handled by the load-time `ContentRemap` (new goods `iron_ore_rough` and the Phase 7 goods are added by name; the load report lists them). Format v8 stays.

## 9. RNG streams (Phase 7 economy block 0x700-0x77F; the military writer has 0x780-0x7FF)
| Stream | Use | Key |
|---|---|---|
| 0x700 | `WarLoad`: muster day jitter, forage parcel ties | war, season |
| 0x701 | the exploit bot's choices; X11's random moves | bot, day |
Both are drawn only under the load generator and the bots: the stream test asserts neither is drawn by a game started from the menu. Free: 0x702-0x70F.

## 10. Chronicle
No types of its own. The load writes the types of `army-economy.md` 8 (`purveyance_taken`, `army_foraged`, `siege_blockade`, ...) with the scripted war's declaration (`war_declared`, Phase 6 diplomacy) as their root, so WL-12 measures real coverage.

## 11. UI
None in the game. The dev console gains `econ.audit` (prints the checklist for the running world) and `econ.flow <good>` (the good's sources and sinks this season). Output is text.

## 12. Golden tests
| id | Case | Expected |
|---|---|---|
| EA-1 | X1 on seed 1234, Ranunfield to the nearest village, 30 kg of the best gap in the notebook, 10 round trips | profit per game day <= 525 mites after spread, toll and the hero's hours |
| EA-2 | X2 every tracked good at every gate, hour 12 of day 3 | desk bid < book ask for all |
| EA-3 | X3 every recipe and build piece: craft or build at book prices, salvage, sell | value ratio < 1 for all; the worst five printed |
| EA-4 | X5 every loot row | melt value / listed value <= 1.2 (fails today on `gold_torc`: F-2) |
| EA-5 | X6 silver at 0.3x to 4x of its book price | melt-and-strike margin never above seigniorage |
| EA-6 | X11: 1,000 moves (0x701) with a save and load between each | `MassLedger`, `ItemTable` exact |
| EA-7 | L1 grep test: every `Ledger.Mint` call site is on the documented list | the list in the findings file equals the grep |
| EA-8 | L2: `SinkReason.Burn` outside tests after F-3 | none |
| EA-9 | S-1 measure: each extraction kind's value per worker-day at book price / its class wage | after F-1: 1.0-1.5 |
| EA-10 | S-2 measure: minted per year from the region's deposits at their generated crews | after F-4: 1-2% of the money supply |
| EA-11 | `WarLoad` alone for 15 days on a test world of 3 settlements: 200 men, 30 horses, 6 carts | 200 kg of grain a man-day exact; coin and mass exact; tallies, forage and pay match the hand count |
| EA-12 | `WarLoad` determinism | two runs equal at day 15; save, load, save byte-identical at day 8 |
| EA-13 | The rich-player bot, 5 years, seed 1234 | WL-15 |
| EA-14 | The exploit bot, 1 year, seed 1234 | its best loop under the exploit rule |
| EA-15 | The war-load soak (nightly, `Category=Soak`), five seeds | WL-1 to WL-15 |
| EA-16 | `econ-audit` prints every row of section 4 with a result | no row without a result |

## 13. Budgets
`WarLoad` and `HostSupply` for one host <= 0.5 ms a game day (the host's slot, not hour 0); the 25-year war-load soak <= 45 minutes a seed on the reference PC (nightly, the five seeds in parallel processes); the exploit and rich bots <= 10 minutes each; WL-14 for the abstract tick.

## 14. Open questions (recommended defaults)
| # | Question | Default |
|---|---|---|
| EAQ-1 | Accept the R1 rule (raw goods priced from their labour cost, so mines, woodcutters and clay pits stop earning 8.7x their wages)? It cheapens firewood, logs and ore 3-8x and leaves finished goods near today's prices | **Yes** (for the user: it is the condition for "raw output barely covers wages, the profit is in finishing" and for a 2-3 year payback) |
| EAQ-2 | Who signs off accepted (unfixed) Medium findings? | the user, in the findings file, before M-E0 |
| EAQ-3 | Exploit threshold | 1.5x the skilled wage for the hero alone after costs |
| EAQ-4 | Siege starvation cap in the soak (WL-5, 40% inside the town) | as written: harsh, visible, not total |
| EAQ-5 | Seeds | 1234, 1, 7, 42, 9001 |

## 15. Estimate and parts
| Part | Content | Gate | Months |
|---|---|---|---|
| **P7-EA1** harness | `WarLoad` on `HostSupply`'s first cut (needs, the supply ladder's market and baggage sources, pay, muster, blockade), the `econ-audit` and `war-load` commands, the G1 attachments | EA-11, EA-12, EA-16 | 1.0 |
| **P7-EA2** the audit run | the four passes, probes and bots, the findings file | EA-1 to EA-10, EA-13, EA-14 measured | 0.7 |
| **P7-EA3** fixes | F-1 to F-4 and whatever the run finds (budget held: about 0.8 months; more is a schedule finding for the overview) | every `EA_F<n>` test; EA-15 on five seeds | 0.8 |
**About 2.5 months of code** on the critical path (EA1, EA2, EA3 are serial); the second run (section 7) is part of EA3.

## Notes (P7-EA1, built)
The harness is in: `WarLoad` (`Sim/Diagnostics/WarLoad.cs`), `WarLoadRun` with the scenario of section 5.2 and the yearly war table (`WarLoadRun.cs`), `EconAudit` (the checklist, `EconAudit.cs`), `Sim/WarSupply/HostSupply` (first cut) and the `econ-audit` and `war-load` commands (`tools/Ravensguard.WorldTool/EconAuditCommand.cs`). Gate tests EA-11, EA-12 and EA-16 are green (`tests/Ravensguard.Sim.Tests/EconomyAudit/`).
- **Where `WarLoad` lives.** Under `Sim/Diagnostics`, not `Sim/WarSupply`, because the P7-0A source-scan test lets only `Diagnostics` name the harness streams; nothing a menu-started game constructs refers to 0x700. 0x700 draws only the muster day (+-2 days, key war id and season); forage ties wait for the forage step (AE2). 0x701 is not drawn yet (the exploit bot is EA2).
- **G1.** `WarLoadRun.AttachLinks` attaches the law (with heist, bond and faith-politics links), family and contract links in the street view's order; `war-load` does it by default (`--no-links` turns it off) and a test that resumes a runtime must do the same on the loaded one. The links are still not attached by `WorldRuntime` itself (that fix is the street view's to drop).
- **The scenario** is the table of 5.2 with two simplifications: the Y9 loan and purveyance are not in (they need AE2); the host marches one settlement a day along a route to the settlement furthest from the largest town (the "gate"), camps at the end and walks home, and in the Y8 siege it stays at the largest town in `Siege` mode with the blockade on. Levies are drawn by each fief's war quota (`TaxSystem.LevyQuota`, one man a household), so the roster has the levies the region's households give (about 1,020 on seed 1234, not exactly 1,040).
- **Funding.** The pay chest is funded at the muster from the realm treasury (up to 30 days of pay and food, what the treasury holds if less) and unspent coin returns at `Disband`. The realm treasury of seed 1234 held only 4,277 mites at the Y6 muster (the host owed 7.0 million of arrears when it disbanded), so the Y6 and Y7 hosts are short of coin from the first day: the table shows the hungry man-days, the arrears and the stand-in desertion that follow. That is the load doing its job (WL-8), but EA2 must decide whether the realm raises coin (subsidy, tallage, the Y9 loan) before the soak's food measures (WL-4, WL-5) mean anything.
- **What the first run printed (seed 1234, years 1 to 7, about 85 s):** coin and mass exact on every day of both campaigns; the supply hash moves only in years 6 and 7. The 25-year run on one seed is about 5 minutes.
- **Tests and the long run.** `WarLoadSoak_Short_Years1To7` (about 80 s, `Category=Soak`) is the shortened soak; `WarLoadSoak_Full25Years_Seed1234_Nightly` runs the whole scenario only with `FL_NIGHTLY=1`. The five-seed EA-15 and WL-3 to WL-15 stay EA2's and EA3's.
- **`econ-audit`** prints all 50 rows of section 4 (X, W, B, L, G, I and the suspects S-1 to S-8) with a state and a result. Measured by the harness today: L2 (no `Burn`), L5 (the links), L4 (no `Other` destroys), S-1 (the 8.7x is confirmed from the content: woodcutting 8.8x, mining 8.7x, clay 8.7x, firewood 9.3x, salt 110x), S-3 (no mint has struck), and L1 and S-6 as information. Every other row prints `PENDING` and names the pass that closes it (EA2 probe, bot or soak; EA3 fix). `--strict` exits 1 on a failed row; M-E0 needs none.
- **Saves.** The `warsupply` section is written only once a host has existed; a world that never had one keeps its bytes (golden hashes, the trails and the Phase 6 save bytes did not move). Persons on campaign are in this section by host, so no key is added to the people section.


## Notes (P7-EA2, built)
The first run is `docs/reviews/phase-7-audit.md` (findings EA_F1 to EA_F16, every checklist row, the grep lists, the war-load tables and gates, the market-depth numbers). What EA2 added to the harness:
- **`WarFunding`** (`Diagnostics/WarFunding.cs`): the realm raises a campaign's coin before the muster, EA1's open point. Order: aid, half of each fief treasury (`TransferReason.Aid`), then a tallage on household purses above 2,000 mites, an equal permille of the excess up to 15% (`TransferReason.Tallage`). `WarLoad.RealmRaises` is off by default (the EA-11 test funds the chest itself) and on in `WarLoadSoak` and `war-load`. It is the harness's decision, not a game rule: the military's `DaysFunded` (army-economy 4.9) with scutage, subsidy and loans replaces it in AE1. On seed 1234 a month's purse (11.7 million mites, 9% of the region's coin) is raised without hunger or price effect; a 90-day war is not affordable (arrears 30.5 million, 802 stand-in deserters).
- **`WarGates`** (`Diagnostics/WarGates.cs`): WL-1 to WL-15 read from the yearly rows and the control's rows; gates that need a part not built (WL-7, 9, 12, 14) print PENDING with the part; `war-load --with-control` runs the control first and prints both tables and the gates. New columns: realm raised (aid, tallage), idle adults, wages a day, money supply, staple price on the route and over the region (mean and peak), solvent merchants, farm labour hours wanted and left undone.
- **`AuditMeasures`** (`Diagnostics/AuditMeasures.cs`): the pure content measures (loot against material, raw extraction against wage, silver per worker-day, the desk over the book's ask, the salvage shares) that `econ-audit` prints and the tests assert. `EconAudit.Run` takes the content database (optional) so those rows are measured; the rest stay PENDING with the pass that closes them.
- **Bots and probes** are tests: `TradeBot` (`AuditBots.cs`) finds the best loop between books within 10 km by the books' curves, prices it with the real quotes and runs it with `MarketBook.Buy` and `SellToMerchants`; its capital is a one-off test faucet and every later move is a ledger transfer. It is the exploit bot for the books (X1, EA-14) and, with 500,000 mites and a cart, the rich-player bot (WL-15, EA-13); it does not build, lend or mint.
- **Test marking.** A finding that is a concrete defect has an `EA_F<n>` test that states the pass criterion; where the fix is a later part's it is `[Fact(Skip = "EA_F<n> owned by ...")]` (EA_F1, F2, F5, F6, F9) and the owner removes the Skip with the fix. Gates and measures that pass today are asserted (EA_F7, the soak gates, the pinned silver rate), so a change moves a green test.
- **A trap for later runs.** `WorldRuntime.AdvanceTo` takes an absolute game time and a harness that passes a day count from zero advances nothing (the world starts in year 1,000 and more); use `run.NextSlotDay` as the base, as the soak does.
