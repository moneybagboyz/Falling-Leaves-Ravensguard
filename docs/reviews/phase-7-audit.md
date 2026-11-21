# Phase 7 economy audit: the first run (P7-EA2)

Audited commit: tag `p7-audit-base` (bf8557a: Phase 6 closed, with P7-0A, P7-0B, LB1, ES1, IN1, EA1 and the BB-A grid merged). Seed 1234 unless said (the five-seed run is EA3b's). Spec: `docs/specs/phase7/economy-audit.md`. The harness is EA1's (`EconAudit`, `WarLoad`, `WarLoadRun`) plus what EA2 added: `AuditMeasures` (pure content measures), `WarFunding` (the realm raises war coin), `WarGates` (WL-1 to WL-15 from the yearly rows), the trader bot (`TradeBot`, tests `AuditBotTests`), the probes (`AuditProbeTests`, `AuditWorldProbeTests`), the regression tests (`AuditFindingTests`) and `war-load --with-control`. Nothing in the sim's rules was changed: **no content, price or golden moved** (the audit's fixes are P7-EA3a's).

This file is kept until Phase 7 closes, then folded into `docs/reviews/phase-7.md`. Severity as `economy-audit.md` 6: **High** coin or mass not conserved, a loop above the exploit rule, a soak gate failed, a save that does not round-trip; **Medium** a target missed or a consequence invisible; **Low** calibration or wording. A Medium finding accepted unfixed is **signed by the user** in the "Accepted by" column (overview Q15); the file records who and why.

## 1. Summary

| | |
|---|---|
| Coin and mass | **Conserved every day** of every run: the war-load soak (seed 1234, 12 years, war and control; seed 42, 10 years), the bots, the 1,000-move duplication probe with ten save/load cycles. No `Burn` sink outside tests, no `Other` mass destroy in the war load. |
| Exploits | **No loop above the exploit rule found on the tag in the exchange**: the trader on foot made 0 mites a day (no gap above the 23.6% the spread and toll cost), the trader by cart 376 a day in one trip in a year (a 4.5x silver gap between two villages; the iron one found in an earlier run was a 3x gap that one load closed). The two exploit-shaped findings are in **content that the sale path reads**: loot (EA_F2) and raw extraction (EA_F1). |
| War load | 2,000 men for 15 to 90 days **do not hurt this region's food market**: the staple price on the march route peaks at 1.06x the Y3-Y5 mean (the gate is 3x), no one starves, the region recovers at once. What breaks is the **realm's purse**: it held nothing, so a host starved from day one until the harness made the realm raise coin (EA_F7). With 15.4 million mites a campaign raised (9 to 11% of all the coin) the host is paid for 30 days, then goes unpaid and hungry (Y8 and Y9). |
| Gates | On seed 1234 WL-1, 2, 3, 4, 5, 8, 11 **pass**; on seed 42 WL-3 fails **in peace** (EA_F16). WL-6 (harvest) and WL-10 (wages against the Y3-Y5 mean) **fail** on both (EA_F9, EA_F10); WL-7, 9, 12, 14 wait for parts not built (purveyance, kits, chronicle causes, the abstract tick under load). |
| M-E0 | **Not open.** Blocked by the High findings EA_F1 and EA_F2 (fix in P7-EA3a), the Medium findings that need a fix or the user's signature (section 3) and the pending gates WL-7, 9, 12, 14 and the five-seed run (EA3b). |

## 2. Findings

Columns as `economy-audit.md` 6. "Where" is file and line at the audited commit; "Owner" is the part that fixes it.

| # | Severity | Where | What is wrong | Failure scenario | Suggested fix | Confidence | Owner and status |
|---|---|---|---|---|---|---|---|
| **EA_F1** (S-1, I4) | **High** | `content/base/production/extraction.jsonc` rates x `goods*.jsonc` base prices | A worker-day of raw extraction is worth 8.7 to 9.3 times the 150-mite unskilled wage at book price: mining 8.7x, clay 8.7x, quarrying 8.7x, sand 8.7x, woodcutting 8.8x, peat 8.8x, firewood 9.3x, **salt mining 110x** (500 kg x 33 mites). I4 asks 1.0 to 1.5x. | Whoever owns a mine, woodlot or pit and hires hands earns 1,150 mites a hand a day (3.3x the skilled wage) for no skill; the generated owners' dividends are fed by it; a mine repays in weeks, so the 2-3 year payback target of the industries cannot hold. | R1 (overview Q2: yes): raw price = wage x 1.25 / output kg a worker-day: clay 0.09, firewood 0.27, logs 0.18, ore and coal 0.38, stone 0.19, sand 0.09, peat 0.63 mites a kg. Salt mining needs its own rate or price (110x is not a labour price). Re-run `PriceSolverTests`, pin new numbers, re-pin the trails. | High (arithmetic from content, `EA9_S1_*`, `econ-audit` S-1 and I4) | P7-EA3a. Test `EA_F1_RawExtraction_EarnsBetween1And1p5TimesItsWage` (skipped until the fix). **Open.** |
| **EA_F2** (X5, S-4) | **High** | `content/base/sites/loot_tables.jsonc`; `Sites/SiteDesk.cs:106` (`Make`); `Trade/HeroDesk.cs:657` (`SellItem`) | A loot item is made as one part of its material at its full mass, so the pack values it by `ItemMath.MaterialMites` (material `value_per_kg`), while the site's budget counts its listed `value_mites`. 18 of 25 listed rows are worth more than 1.2x their listed value: gold torc 206x (288,450 against 1,400), reliquary 205x, dead man's purse and ring 192x, silver chalice 82x, rope coil 24x, silver brooch 21x, iron pot 8x, pewter plate 8x. | A site budgeted at a few thousand mites places hundreds of thousands in silver and gold. `SellItem` pays the bid out of a merchant's purse that holds it (a chalice's bid is 92% of 34,614 less the 30% cut for an uninspected item = 22,291 mites, paid by any merchant of the book who holds that much); once bullion has a mint (CN1) the same item melts for its metal at the coinage rate (gold 909 mites a gram). | F-2: `value_mites` computed from material and labour (`ItemMath`) for every site item at load, the loot budget counted in that value; keep a listed override only below the material value. | High (`EA4_X5_*`, `econ-audit` X5) | P7-EA3a. Test `EA_F2_EveryLootRow_IsWithin1p2OfItsMaterialValue` (skipped until the fix). **Open.** |
| **EA_F3** (S-5: phase-6 S10, S14) | (was High) | `Law/Gaol.cs`, `Politics/Diplomacy.cs` | Gaol rations were burned and tribute to an abstract realm moved no coin. | Coin left by a test path; tribute cost nothing. | Fixed in P7-0B (`Gaol.Pay`, `Diplomacy.AbstractHolding`). | High | **Closed** (EA_F3 tests in `JusticeTests` and `P70BDebtsTests`; EA-8 and L2/L3 rows pass). |
| **EA_F4** (S-2, L6) | **Medium** (latent until the mint is wired) | `content/base/crafting/recipes_business.jsonc` (`smelt_galena`, `cupel_silver`), `extraction.jsonc` `mining_silver` | A silver miner digs 500 kg of galena a day = 1.5 kg of silver = **115,380 mites, 769x the wage**. The two generated silver mines are small (115 t of galena a year): measured over four years they made 136 kg of silver = 10.5 million mites = **2.04% of the money supply a year**, as a good that nobody strikes (S-3). The rate is set by the crew, not by the deposit: a crew of 20 for a year would be 108% of the supply. | Once CN1 strikes silver, one hero-owned or NPC-grown mine mints the whole region's money several times over; the desk's coin path (`BoundDeskExports`) stops exports above path+5% but cannot undo a mint. | F-4: deposit grades (0.01 to 0.05%, default 0.02%) so the crews the game can make mint 1 to 2% a year; and a cap on a licence's daily take (the royalty and licence rows already exist). Pin the rate: `EA2_Probe_*` asserts 0.5 to 3% a year today. | High for the rates; the cap is a design call | P7-EA3a with CN1 (CN1 wires the mint, so the exposure opens there). Pinned by `AuditWorldProbeTests`; no failing test until a mint exists. **Open.** |
| **EA_F5** (X2) | Low | `Caravans/ContinentDesk.cs:70-95` (`DeskExport`), `ContinentTrade.ExportDiscountPermille` 900, staples 950 | The border desk pays the book's *reference* price less 5 to 10% for a glut, whatever the book's own price is. The desk acts above 2.8x cover (1.5x when the region's coin is under its path), where the book's ask is 0.58 to 0.76 of the reference: the desk's bid after the toll is 1.2 to 1.9x the book's ask. At target stock it is 0.84x (no loop). | The producers of a glut are paid above the local price by the continent's coin (bounded by `PayBudgetMites` and the desk's room); a trader who moves goods from one glutted book to another and relists them to the desk earns the gap. Bounded and self-closing (buying lowers the stock below the threshold), so not an exploit. | Price the export at the book's mid (the curve's value at the stock after the export) with the discount, floored at 0.9 x the clamp. | High (`EA2_X2_*`, row X2) | P7-EA3a, optional. Test `EA_F5_DeskExportBid_IsNeverAboveTheBooksAsk` (skipped). Recommend **accepting** (Low). |
| **EA_F6** (overview 15) | Low | `content/base/crafting/goods_crafting.jsonc:12` | Rope is priced at 2,000 mites a kg, 4.1x bar iron and about the price of the linen it is spun from per labour hour times ten. Carts, harness, sails and every vehicle recipe that uses it inherit it. | Cart and ship costs in EA3b and the naval part are several times too dear. | Reprice under R1 with the other goods (the price solver derives it from its inputs and labour). | High | P7-EA3a with EA_F1. Test `EA_F6_Rope_IsNotDearerThanBarIron` (skipped). |
| **EA_F7** (W2, WL-8; the open point of EA1) | **Medium** | `Diagnostics/WarLoad.cs` `Begin`; the real source is `army-economy.md` 4.9 (`DaysFunded`) | The realm treasury of seed 1234 held 0 mites at the Y6 muster (4,277 later): nothing raises war coin. A host of 2,000 costs 391,500 mites a day (pay 261,000, staple 94,000, fodder 36,000): 11.7 million a month, **9% of all the coin** (128.3 million). | The Y6 and Y7 hosts starve and desert from the first day, so WL-4 and WL-5 measured nothing. | **Decided and built in the harness (`WarFunding`):** the realm raises what the host needs, in order, from (1) aid: half of each fief treasury (`TransferReason.Aid`), (2) a tallage on household purses above 2,000 mites, an equal permille of the excess up to 15% (`TransferReason.Tallage`). Every step is a ledger transfer in a fixed order. A 30-day purse raises 11.7 million (7.7 aid, 4.0 tallage at the start). The real rule, with scutage, subsidy and loans, is AE1's `DaysFunded`; the harness shows a region **can** pay for a month and **cannot** pay for the Y8 90-day war (15.4 million raised, 30.5 million of arrears, 80,227 unpaid man-days, 802 stand-in deserters). | High | Harness decision made here (`EA_F7_TheRealmRaisesTheCoinOfACampaign_*`, passes). The real rule is **P7-AE1**. **Closed in the audit; carried to AE1.** |
| **EA_F8** (X6, X13) | **Medium** (design, for CN1) | `Economy/MintMath.cs`; `coinage.md` 5 | By the arithmetic of the tag's constants a strike pays the seller 92.5% of parity, so buying bullion and striking it **profits whenever bullion is below 0.925x of coin parity** (+15.6% at 0.8x, +208% at the market's 0.3x clamp); and a melt (coin to silver, no fee, `SinkReason.Melt` exists) **profits whenever bullion is above 1.0x** (+50% at 1.5x, +300% at the 4x clamp). The spec says a margin only inside the 7.5% band. Nothing runs today (S-3), so nothing is exploited. | The day CN1 wires strike and melt, a bullion price outside the 0.925 to 1.0 band is a free-coin loop limited only by silver's stock; the market reaches the clamps at thin stock (silver trades in grams). | CN1: strike pays at the book's silver price (not parity), melt only at the mint at the same price less seigniorage, both refused outside the band, or a changer fee above the gap; test EA-5 (`CN-` row) with the table printed by `EA5_X6_*`. | High (arithmetic) | **P7-CN1** (a test there; none fails today). Signature not needed if CN1 pins it. |
| **EA_F9** (W4, WL-6) | **Medium** | `WarSupply/HostSupply.cs:377` (`Muster`), `Farming/FarmingSystem.cs:334` (`IsEmployedElsewhere`) | **1,040 levies out through the harvest cost the harvest nothing**: the Y8 harvest is 2,681 t against the control's 2,676 t (-0.2%; the gate asks 10 to 35% below). The hook works (a mustered man's hours are removed: the Y8 unmet farm hours are 778,570 against 753,396 in the control, +3.3%), but a levy takes one man per household at random and 1,040 men x 8 h is 9% of the busiest day's supply (94,370 h against a demand of 274,731 h). | "Levies out of the harvest" (plan 3d, the cost of war to a realm) is invisible: wars have no harvest price. | Either levy by the household's adult count (the fief's real quota) and take field hands first, or let the planner's deadlines turn unmet reaping into loss; decide with AE2. | High for the measure; the cause is a design choice | **P7-AE2 / P7-EA3b.** Test `EA_F9_LeviesOutThroughTheHarvest_CutTheHarvest10To35Percent` (skipped, Soak). **Open; the user may accept** (Q15). |
| **EA_F10** (G2, WL-10) | Low | `economy-audit.md` 4.5 G2 and 5.3 | The gate "wages a day within 10% of the Y3-Y5 mean at Y15" fails in the **control** too: +16.5% at Y12 (war +15.8%), with money supply +1.2% a year and the economy growing. The drift is the economy's, not the war's. | A pure peace run fails WL-10 forever. | Measure the war against the control at the same year (within 10%); keep the Y3-Y5 line as information. | High (`war-load --with-control`) | Spec wording, **EA3b**. Recommend **accepting** (Low). |
| **EA_F11** (B1, W3, WL-9) | **Medium** | the region's iron: `iron_bar` stock 3,760 kg over every book (44 days of 85 kg a day), production 41 t in four years (28 kg a day) | The 960 kits of 4 kg of iron the war asks (3.84 t of bar) are **more than every bar of iron in the region** and 137 days of its production. The first dry input of any large arming is bar iron, then charcoal (23 t held, 1 t a day used). Mines: 2; smithies 111. | A kit order or a siege-engine order empties iron in a day: price at the 4x clamp, tools and horseshoes unaffordable, the war's iron has to come from the arms trade, armouries or the gate. | None needed in the audit: the military parts must source iron from armouries (SC-A), the arms trade (AE) and the L2/L3 iron works (IN2); WL-9 will name bar iron as the first dry input. Keep the measure in the second run. | High (`EA2_Probe_*`) | **P7-SC-A, AE3, EA3b.** A finding to carry, not a fix. |
| **EA_F12** (B5, W7) | Low | `WarSupply` first cut; `Vehicles` | The region has 1,661 draught oxen but **no inventory of carts**: vehicles exist only for the hero and the caravans, so a baggage train's 60 carts come from the host's spec, not from the region. | W7's "carts purveyed" cannot happen; a war costs the region no carts. | AE1/AE2: draw carts and oxen from the farms (oxen are counted per settlement) or make carts by commission. | High | **P7-AE1/AE2.** Carried. |
| **EA_F13** (W6, WL-5) | Low | `WarSupply/HostSupply.cs` `SiegeBlockade`, `DaysOfFood` | The largest town holds **163 days of staple** in households and book, so a 60-day siege starves nobody (0 hunger deaths, WL-5 passes trivially); the prices inside do not move. | Sieges do not bite in this economy until they last five months or burn stores. | The siege part (SG) must add burning and requisition, or a longer blockade; WL-5 as written is easy. | High | **P7-SG.** Carried. |
| **EA_F14** (calibration) | Low | `Production/*`; content `businesses.jsonc` | After four years the region keeps 17 of 103 weaveries open, 35 of 71 breweries, 47 of 70 bakeries (the closing rule after two or three losing seasons; new ones are founded in other places). | Cloth, ale and bread supply is carried by a few shops; `bread` stock is 0.7 days. | Calibration for the economy part of Phase 7: not a war-load matter. | Medium | Left open; Low. |
| **EA_F15** (L1) | Low | `Game/UI/CraftingSession.Full.cs:175`, `Game/World/StreetView.Business.cs:309`, `StreetView.Contracts.cs:149`, `StreetView.BuildMode.cs:558` | Four dev-only `Ledger.Mint(.., SourceReason.Seed)` calls in the Game (a dev smith, the screenshot scene, two dev commands). They create coin from nothing for the hero. | Reachable only from dev commands and scenes; if one ships in a release build it is a free-coin button. | Compile them under a debug symbol or route them through the dev console that logs. | High | Left open; Low. Listed in the L1 table. |
| **EA_F16** (G4, WL-3) | **Medium** | `Economy/Markets/ContinentTrade.cs:92-105` (`BoundDeskExports`), `People/Vital/Demography.cs:357, 427` | On **seed 42 the money supply falls 8.4% in one peace year** (Y4 to Y5: 1,203,800 to 1,102,600 crowns) and rises 11.1% in another (Y6 to Y7); the gate allows -5% to +8% a year. Seed 1234 stays inside (worst -0.4%, best +6.2%). The control run has the same Y1 to Y5 (it is the same world), so this is the economy's border dynamics, not the war. | WL-3 and G4 fail on some seeds in peace; a mint (CN1) adds to a supply that already swings by lumps (immigrant stakes and emigrants' coin are whole households' purses). | Characterise on the five seeds (EA3b); then widen the band of the desk's coin path or smooth the household moves (emigrants and immigrants carry their coin over a season). | Medium (one seed seen) | **P7-EA3a/EA3b.** No test yet (needs the five seeds). **Open; Medium.** |

**Not findings, measured and fine:** X1 (no loop), X3, X4, X9, X10 (read), X11 (1,000 moves), X14, L2, L3, L4, L5, S-6 (bounded), WL-4, WL-5, WL-11, WL-15, G1, G3 (and G4, WL-3 on seed 1234 only: EA_F16).

## 3. M-E0 status and the user's decisions

| Item | State |
|---|---|
| High findings | EA_F1, EA_F2 open (P7-EA3a). |
| Medium findings | EA_F4 (CN1/EA3a), EA_F8 (CN1), EA_F9 (AE2/EA3b), EA_F11 (SC-A, AE3, EA3b), EA_F16 (EA3a/EA3b) open; EA_F7 closed in the audit and carried to AE1. |
| Gates | WL-1 to 5, 8, 11 pass on seed 1234 (12 years); on seed 42 (10 years) WL-3 fails in peace (EA_F16) and the others pass (section 6); WL-6 fails (EA_F9); WL-10 fails as worded (EA_F10); WL-7, 9, 12, 14 not measurable at the tag; five-seed EA-15 is EA3b's. |
| `econ-audit` | Prints a result on every row; rows that fail: X2 (EA_F5), X5 and S-4 (EA_F2), S-1 and I4 (EA_F1), S-2 and L6 (EA_F4), S-3 (no mint, CN1). `--strict` therefore exits 1 until EA3a and CN1 land. |

**Accepted by (overview Q15): the user signs each Medium finding left unfixed.** None is signed yet.

| Finding | Recommendation | Accepted by |
|---|---|---|
| EA_F5, EA_F10, EA_F12, EA_F13, EA_F14, EA_F15 (Low) | accept; no signature needed | n/a |
| EA_F9 (levies do not cut the harvest) | **fix** in AE2 (it is the cost of war the plan names); accepting it means wars cost no harvest | |
| EA_F11 (iron) | not a fix: carry to SC-A/AE3 and re-measure in EA3b | |
| EA_F4, EA_F8 | not accepted; CN1 pins them | |
| EA_F16 (money supply swings on seed 42) | characterise on five seeds first; accept only if the swing is the border's and the mint does not add to it | |

## 4. The checklist, row by row

State as `econ-audit` prints it. "Measured by" names the test or run.

| Row | State | Result | Measured by |
|---|---|---|---|
| X1 | pass | **Trader on foot, 30 kg, between books at most 10 km apart, a game year (60 days): no loop** (0 trips: no good's mid prices differ by more than the 23.6% the 8% spreads and the 5% toll cost). By cart, 500 kg: one trip in the year, silver Yorenford to Quenford, bought 0.2 kg for 6,908, sold for 31,038, profit 22,578 = **376 mites a game day** on average (1.1x the skilled wage, under the 525 line); a 3x iron-bar gap seen in an earlier run closed with one 65 kg load (+81,208). Gaps are thin-market noise, not a standing loop. | `AuditBotTests.EA1_EA14_*` (Soak) |
| X2 | **fail (Low)** | desk bid is 79% of the book's ask at target stock, 132% (staple 140%) at the glut threshold | EA_F5 |
| X3 | pass | 125 build pieces return at most 90% of their materials (60 pieces 70%, 36 pieces 60%); `salvage_mail` returns 44% and `salvage_armour` 1 to 3% of the material value (mail rings are not tradable) | `EA3_X3_X4_*` |
| X4 | pass | the same: no demolition returns more than it took; 20 of 125 return more than 70% (a loss of under the 30% the target names, not an exploit) | `EA3_X3_X4_*` |
| X5 | **fail (High)** | 18 of 25 loot rows above 1.2x; worst 206x | EA_F2 |
| X6 | info | no mint or melt runs; arithmetic table in EA_F8 | `EA5_X6_*` |
| X7 | info | no copper coin or mint exists at the tag: CN1's | n/a |
| X8 | pass (read) | `Rule/RuleLoans.cs:60, 82` and `RuleDesk.cs:110` move principal and interest by `Pay` (`TransferReason.Loan`, `Interest`) between purses: no `Mint` or `Sink` in the file; the default path takes the deed (`RuleLoans.cs:105`); RI-13 and RI-14 stay green | read |
| X9 | pass (read) | `OwnershipWorld.SellHour` (`Production/OwnershipWorld.Tick.cs:251-295`): every mite of a stall's takings is a `Transfer` from a household purse (`pay` capped by `afford = coin / price`), the toll to the book; footfall only raises the quantity offered, never the coin | read |
| X10 | pass (read) | no `Ledger.Mint` or `Sink` call in `Contracts/` (section 5.1, 5.2): escrow, bonds and tournament purses are transfers between purses (Phase 6 S2 fixed); the pay moves only through the contract's own completion | read |
| X11 | pass | 1,000 random moves between household and business stores, 200 item-owner changes, 10 save and load cycles: the mass audit exact after every move and every load, each good's net created-less-destroyed unchanged (125 goods), 40 items equal | `EA6_X11_*` |
| X12 | pending | no output slider before IN2 | EA3b |
| X13 | pending | no mint or changer before CN1 | CN1 |
| X14 | pass | `Standing.PriceMites` has no caller: it pays nothing | `EA_X14_*` |
| W1 | pass | route price peak 1.056x of the Y3-Y5 mean (gate 3x); region 1.005x (gate 2x); year-12 route mean 0.94x | war-load, WL-4 |
| W2 | pass (WL-8) | the realm raises 14.9 to 15.5 million a campaign; Y6 and Y7 fully paid and fed; Y8 and Y9 unpaid 80,227 man-days, arrears 30.5 million, 802 stand-in deserters | war-load |
| W3 | pending | kits: AE3 and SC-A; the measure of the iron they need is EA_F11 | n/a |
| W4 | **fail** | Y8 harvest -0.2% against the control | EA_F9 |
| W5 | pending | purveyance is AE2's | n/a |
| W6 | pass, easy | the town holds 163 days of food; no deaths | EA_F13 |
| W7 | info | no cart inventory | EA_F12 |
| B1 | info | iron 3.76 t held, 85 kg a day demand, 28 kg a day made | EA_F11 |
| B2 | info | charcoal 23.3 t held (22.6 days), 1.03 t a day used, 355 t made in four years by 4 crews; woodland felling is not metered in the abstract tick | `EA2_Probe_*` |
| B3 | info | logs 438 t held (56 days), planks 170 t (37 days): not a bottleneck | `EA2_Probe_*` |
| B4 | pass | the region grows 2,200 to 2,700 t of grain a year against about 1,540 t eaten (25,668 people x 1 kg x 60 days); the army's 2 t a day is 7.8% of the region's 25.7 t a day staple need, a 30-day campaign takes 60 t = 2.3% of a harvest; no shortage | war-load |
| B5 | info | 1,661 oxen, no cart inventory | EA_F12 |
| B6 | info | idle adults 4 to 25 (< 800); at the busiest farm day of four years supply is 0.34x demand (94,370 h against 274,731 h) yet nothing starves | `EA2_Probe_*` |
| L1 | info | 14 call sites, all documented (section 5.1); the border's gross flow is 155.2 million minted by `Immigrate` against 144.9 million sunk by `Emigrate` in four years (net +10.3 million = +8%, 2% a year), held to `PayBudgetMites` (4.9, 7.9 and 3.6 million per edge) and the desk's coin path | `EA7_L1_*`, `EA2_Probe_*` |
| L2 | pass | `SinkReason.Burn` has no caller outside the ledger and the diagnostics; `Melt` has none | `EA8_L2_*` |
| L3 | pass | closed in P7-0B | EA_F3 |
| L4 | pass | `DestroyReason.Other` is used only in crafting (a rollback, waste to rags, reforge) and the dev crafting scene; none in the war load or the soak | grep (section 5.3), `EconAudit` L4 |
| L5 | pass | links attached by the harness | EA1 |
| L6 | fail (latent) | EA_F4 | n/a |
| G1 | pass | population Y12 99.5% of Y5 (25,607 against 25,735) | war-load |
| G2 | **fail as worded (Low)** | +15.8% at Y12 (control +16.5%) | EA_F10 |
| G3 | pass | idle adults 25 at Y12 | war-load |
| G4 | pass on seed 1234, **fail on seed 42** | seed 1234: +1.22% a year over 11 years, worst year -0.4%, best +6.2%; seed 42: +1.28% a year but -8.4% in the peace year Y4 to Y5 and +11.1% in Y6 to Y7 | war-load, WL-3, EA_F16 |
| G5 | pending | needs the scripted L1 works (IN2) | EA3b |
| I1, I2, I3 | pending | need IN2's chains; market depth numbers in section 7 | EA3b |
| I4 | **fail (High)** | EA_F1 | n/a |
| S-1 | fail | EA_F1 | |
| S-2 | fail | EA_F4 | |
| S-3 | fail (by design) | no mint runs; silver piles up as a good (136 kg in four years) | CN1 |
| S-4 | fail | EA_F2 | |
| S-5 | pass | closed in P7-0B | |
| S-6 | pass | bounded (above) | |
| S-7, S-8 | info | CN1 | |

## 5. The static read: grep lists (pass a), each line checked

### 5.1 `Ledger.Mint` call sites (L1, EA-7: the list equals the grep, `EA7_L1_*`)
| File | Calls | Why it is a source | Counterpart |
|---|---|---|---|
| `Sim/Economy/EconomyState.cs:204` | 1 | `Seed`: a new owner's starting purse | the one-off seed (I33) |
| `Sim/Economy/MoneySeeder.cs:75` | 1 | `Seed`: the region's seed money | one-off |
| `Sim/Economy/MintMath.cs:28` | 1 | `Mint`: struck silver | **no caller in the world** (S-3); CN1 |
| `Sim/Worlds/RegionTransfer.cs:107` | 1 | `CoinIn`: a household crossing a region border | the other ledger's `Sink(Emigrate)` on the same line |
| `Sim/Caravans/CaravanStep.cs:233` | 1 | `Immigrate`: the continent pays a caravan for exports | `Emigrate` sinks at 244 and 260 (tolls, costs); `PayBudgetMites` |
| `Sim/Caravans/ContinentDesk.cs:85` | 1 | `Immigrate`: the desk pays for a glut | `Emigrate` at 62; `PayBudgetMites` and the desk's room |
| `Sim/People/Vital/Demography.cs:427` | 1 | `Immigrate`: an immigrant's stake | `Emigrate` at 357 |
| `Sim/Diagnostics/Phase6Trail.cs:206` | 1 | `Seed`: a fixture | test path |
| `Sim/Trade/AbroadDealing.cs:53, 62` | 2 | `CoinIn`: the hero's dealing abroad | `CoinOut` at 52 and 61 |
| `Sim/Hero/StartingEstate.cs:62` | 1 | `Seed`: the hero's estate | one-off |
| `Game/UI/CraftingSession.Full.cs:175`, `Game/World/StreetView.Business.cs:309`, `StreetView.Contracts.cs:149`, `StreetView.BuildMode.cs:558` | 4 | `Seed`: dev commands | none: **EA_F15** |

### 5.2 `Ledger.Sink` call sites (L2)
All `Emigrate` (`CoinOut`): `RegionTransfer.cs:107`, `CaravanStep.cs:244, 260`, `ContinentDesk.cs:62`, `Demography.cs:357`, `AbroadDealing.cs:52, 61`, `HostSupply.cs:354` (a mercenary's pay leaving with him). `Melt`: no call site. `Burn`: none (`EA8_L2_*`). Every one has a counterpart in `Immigrate` or a continent-side credit through `EdgeTrade.CoinNetIn`.

### 5.3 `Store.Destroy` and `Create` reasons (L4)
`Destroy`: Consumed 22 sites, Burned 9, Eaten 7, Export 7, Sown 3, Materialised 2, Worn 2, Lost 1, Waste 1, **Other 3**. The three `Other` sites are `CraftRunner.cs:256` (rolling back a refused job), `:372` (mass lost in a craft, returned as waste at `:374`) and `:440` (a reforge, the same mass created again at `:442`): mass-neutral by construction. `CreateReason.Other` is used by crafting for ambient water (`CraftRunner.cs:251`, `Ambient`), mail and armour salvage (`CraftRunner.Full.cs:124, 156`, mass exact by the salvage rule) and dev scenes in `Game` (oats, firewood and wheat for a test cart: `StreetView.Ride.cs:80, 178`, `StreetView.Travel.cs:556, 607`, `StreetView.Vehicles.cs:183, 226`, `UI/CraftingSession.cs:84-85`), none reachable from a world's tick.

## 6. The war-load runs

Scenario of `economy-audit.md` 5.2 with EA1's simplifications (no Y9 loan, no purveyance), the realm raising coin by `WarFunding` before each muster. Seed 1234, twelve years, war and control (`war-load --seed 1234 --years 12 --with-control`, 314 s). Columns: year; alive; harvest t; man-days fed, hungry, unpaid; deserters; arrears (mites); raised (aid+tallage); money supply (thousand crowns); staple price route mean/peak (mites a kg: the mean of wheat, rye, barley and bread over the books within 8 km of the host's route); idle adults; wages a day; solvent merchants %.

| Y | alive | harvest | fed | hungry | unpaid | desert. | arrears | raised | money | route mean/peak | idle | wages/day | solvent |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| 1 | 25667 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 1283.4 | 51/51 | 76 | 68 | 100 |
| 2 | 25671 | 2629 | 0 | 0 | 0 | 0 | 0 | 0 | 1278.1 | 57/66 | 4 | 1222936 | 94 |
| 3 | 25652 | 2574 | 0 | 0 | 0 | 0 | 0 | 0 | 1357.6 | 61/69 | 9 | 1018779 | 100 |
| 4 | 25667 | 2206 | 0 | 0 | 0 | 0 | 0 | 0 | 1368.0 | 61/69 | 5 | 1003103 | 94 |
| 5 | 25735 | 2628 | 0 | 0 | 0 | 0 | 0 | 0 | 1386.8 | 57/65 | 10 | 994176 | 92 |
| 6 | 25746 | 2606 | 33728 | 0 | 0 | 0 | 0 | 14909330 | 1398.8 | 53/62 | 7 | 1250000 | 94 |
| 7 | 25804 | 2661 | 25922 | 0 | 0 | 0 | 0 | 15437678 | 1418.3 | 52/60 | 11 | 1247732 | 95 |
| 8 | 25775 | 2681 | 44000 | 38061 | 38061 | 380 | 9849000 | 15491763 | 1432.2 | 52/63 | 8 | 1261795 | 96 |
| 9 | 25697 | 2454 | 30000 | 42166 | 42166 | 422 | 20690700 | 15359459 | 1433.5 | 52/60 | 10 | 1338636 | 97 |
| 10 | 25679 | 1752 | 28000 | 0 | 0 | 0 | 0 | 15429952 | 1444.9 | 54/63 | 23 | 1258635 | 96 |
| 11 | 25667 | 2664 | 0 | 0 | 0 | 0 | 0 | 0 | 1459.6 | 55/60 | 18 | 1163564 | 97 |
| 12 | 25607 | 1521 | 0 | 0 | 0 | 0 | 0 | 0 | 1466.7 | 56/61 | 25 | 1164159 | 97 |

The control's harvest equals the war's in Y1 to Y5 (the same world) and differs by 0.2% or less after; its wages a day are 1,038,624 (Y6), 1,085,194 (Y8) and 1,171,483 (Y12). Year 1 is the opening year (no harvest yet, labour not hired). Y10 and Y12 harvests are low in both runs (the world's own cycle, not the war).

Gates (`WarGates`, printed by `war-load`):

```
WL-1   PASS    coin conserved every day, region and continent
WL-2   PASS    every good's mass audit exact every day (no Other destroy: EconAudit L4)
WL-3   PASS    money supply 1,283,400 to 1,466,728 crowns over 11 years = 1.22 % a year; worst year -0.4 %, best 6.2 %
WL-4   PASS    route peak 1056 permille of the Y3-Y5 mean (<= 3,000), region 1005 (<= 2,000), Y12 route mean 939 (<= 1,300)
WL-5   PASS    hunger deaths Y6-Y10 0 (control 0); the besieged town held 163 days of food
WL-6   FAIL    Y8 harvest 2681 t against the control's 2676 t = -0.2 % below (pass 10-35%)       -> EA_F9
WL-7   PENDING purveyance (AE2)
WL-8   PASS    raised 76.6 million mites over five campaigns; 80,227 man-days unpaid, 30.5 million arrears, 802 deserters
WL-9   PENDING kits (SC-A, AE3)
WL-10  FAIL    wages a day +15.8% from the Y3-Y5 mean; the control run +16.5%                     -> EA_F10
WL-11  PASS    solvent merchants >= 92% every year, 96% in Y8-Y9
WL-12  PENDING army chronicle causes (AE2)
WL-13  INFO    two-run equality and the day-8 save round trip are EA-12; mid-siege round trip is EA3b's
WL-14  PENDING the abstract tick under load (EA3b)
WL-15  INFO    the rich-player bot (below)
```

### Seed 42, ten years (war and control, 243 s)

| Y | alive | harvest | fed | hungry | unpaid | desert. | arrears | raised | money | route peak | idle | solvent |
|---|---|---|---|---|---|---|---|---|---|---|---|---|
| 1 | 24826 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 1241.3 | 51 | 91 | 100 |
| 2 | 24794 | 2587 | 0 | 0 | 0 | 0 | 0 | 0 | 1217.7 | 66 | 6 | 96 |
| 3 | 24806 | 1758 | 0 | 0 | 0 | 0 | 0 | 0 | 1207.7 | 73 | 3 | 96 |
| 4 | 24792 | 2302 | 0 | 0 | 0 | 0 | 0 | 0 | 1203.8 | 73 | 15 | 98 |
| 5 | 24738 | 1462 | 0 | 0 | 0 | 0 | 0 | 0 | **1102.6** | 66 | 8 | 98 |
| 6 | 24775 | 2470 | 23510 | **3909** | 0 | 39 | 0 | 10175628 | 1124.8 | **104** | 13 | 98 |
| 7 | 24758 | 2484 | 31552 | 0 | 1572 | 15 | 363954 | 10261844 | **1249.2** | 95 | 8 | 100 |
| 8 | 24736 | 2534 | 35784 | 44178 | 44178 | 441 | 11451250 | 11972048 | 1288.1 | 65 | 12 | 100 |
| 9 | 24677 | 2517 | 30000 | 40261 | 40261 | 403 | 21868000 | 12787522 | 1375.2 | 66 | 22 | 98 |
| 10 | 24659 | 2560 | 26000 | 0 | 0 | 0 | 0 | 14209261 | 1391.7 | 68 | 32 | 98 |

Gates: WL-1, 2, 4 (route peak **1,677 permille** of the Y3-Y5 mean, region **1,701**: the Y6 campaign pushed bread 1.7x here, with 3,909 hungry man-days), 5, 8, 11 pass; **WL-3 fails** (worst year -8.4% in peace, best +11.1%); WL-6 fails (-0.4%); WL-10 +35.0% at Y10 against the control's +12.2% (Y10 is a campaign year: the army's pay is wages). The besieged town held 113 days of food. Seed 42 is a poorer-fed region than seed 1234, which is why its prices moved: the audit's five seeds will say how much.

**The rich-player bot** (EA-13, WL-15; `AuditBotTests.EA13_*`): 500,000 mites of test seed coin, trading by cart (500 kg, 4 km/h, books within 10 km), 300 days from day 30. 3 trips; purse 500,000 to 614,229 (+380 a day); 17 kg unsold; the price index (wheat, rye, barley, bread, bar iron, planks, firewood, charcoal over every book) **982 permille of the run without the bot** (gate 833 to 1,200); coin and mass exact. **Reduced:** the bot trades only; it does not build, lend or mint (builds and loans are `HeroDesk` verbs the second run scripts; minting does not exist before CN1). Its gain is the sum of its trades' ledger transfers by construction (every move is `MarketBook.Buy` or `SellToMerchants`).

## 7. Market depth for the industries (I2, the L2/L3 paybacks of overview 15)

Measured over four game years (`AuditWorldProbeTests`):
- The books' demand per day (region sum): bar iron 85 kg, iron ore 24 kg, charcoal 1,028 kg, bricks 501 kg, logs 7.9 t, planks 4.6 t, firewood 37.9 t, clay 2.5 t, wheat 5.8 t. An L2 iron works makes 28.8 kg of bar a day (a third of the region's bar demand and equal to what the region makes now, 28 kg); an L3 one 172.8 kg (**2x the region's whole bar demand**); the clay works' 3,000 bricks a day at L2 and 6,000 at L3 are many times the books' 0.5 t a day of bricks (a brick weighs kilograms).
- The brake the spec relies on is real but is the **desk's coin path, not the book's curve**: the book's price falls along `ref x sqrt(cover / stock)` to the 0.3x clamp, but the desk exports a glut only while the region's coin is under path+5% (6.4 million mites at the start, **0 at year 4**: the coin is above the band), at 0.855 to 0.9 of the reference. So the gate price of 920 permille that the spec's L2/L3 floors (62 to 89%) assume holds only while the desk has room: the room is 84 days of an L3 iron works' 76,308 a day. After that, surplus iron sells at the book's clamp (0.3x) or not at all. The upgrades repay in 10 to 53 days only inside that room; at the clamp, none of the floors (62 to 89%) holds. **The second run (EA3b) must measure an L3 works against this room, or the upgrade bills must be tuned.** This is not a finding of the tag (no works exists); it is the number EA3b needs.
- Rope at 2,000 mites a kg: EA_F6.

## 8. What was not done, and why

- **Five seeds, the full 25 years, WL-7, 9, 12, 14**: EA3b. Seed 1234 (12 years with control) and seed 42 (10 years with control) were run here.
- **X8, X9, X10, X12, X13, G5, I1 to I3**: read (X8 to X10), or need parts not at the tag (X12, X13, G5, I1 to I3).
- **The exploit bot** is one agent that finds the best trade by the books' curves and executes it; it does not drive `HeroDesk` verbs (the hero's own labour, crafting, building). X3 to X5 are measured from content, X11 by moves, so no hero command was needed for a finding; the hero-desk loops (craft, salvage, sell; build, demolish) are bounded by the shares above.
- **Person-table growth** (phase-6 review, P7-0B left it): the 25-year soak will cost more each year (26,000 rows to 111,000 at year 100); for 25 years the cost is modest; EA3b measures it.

## The user's sign-off (21 Nov 2026)
- **L2/L3 works output (overview §15; the payback question):** **keep the big output; owners must ship far.** A Level 3 works may out-produce its region (an L3 iron works makes about twice the region's bar demand); its owner finds distant markets with their own caravans (IN4: selling at the gate vs your own caravan, expected margin, road risk) or watches local prices fall. Upgrade bills are not tuned down for this. The second audit run (EA3) measures an L3 works selling through export caravans against the gate's room, and reports the payback it actually gets.
- **EA_F9 (levies cost 0.2% of the harvest against the 10-35% gate):** **fix it** — levied men are missing from the fields, so a summer war costs harvest. Owner: AE2 and EA3b.
- **EA_F16 (money supply swings outside -5%..+8% in peace on seed 42):** **investigate on five seeds first** (EA3b); accept only if border trade causes it and the mints don't add to it; otherwise fix.
- **Low findings EA_F5, F10, F12, F13, F14, F15:** **accepted** as known limits; later parts pick them up where they matter (F10 is a spec-wording fix; F12 to AE1/AE2; F13 to the siege parts).
- Not accepted (to be fixed): High EA_F1 (R1 repricing, EA3a), EA_F2 (loot values, EA3a); Medium EA_F4 and EA_F8 (CN1 pins them), EA_F11 (carried to SC-A/AE3/EA3b).
