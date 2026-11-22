# Phase 7 economy audit: the first run (P7-EA2)

Audited commit: tag `p7-audit-base` (bf8557a: Phase 6 closed, with P7-0A, P7-0B, LB1, ES1, IN1, EA1 and the BB-A grid merged). Seed 1234 unless said (the five-seed run is EA3b's). Spec: `docs/specs/phase7/economy-audit.md`. The harness is EA1's (`EconAudit`, `WarLoad`, `WarLoadRun`) plus what EA2 added: `AuditMeasures` (pure content measures), `WarFunding` (the realm raises war coin), `WarGates` (WL-1 to WL-15 from the yearly rows), the trader bot (`TradeBot`, tests `AuditBotTests`), the probes (`AuditProbeTests`, `AuditWorldProbeTests`), the regression tests (`AuditFindingTests`) and `war-load --with-control`. Nothing in the sim's rules was changed: **no content, price or golden moved** (the audit's fixes are P7-EA3a's).

**The second run (P7-EA3b, 22 Nov 2026) is the last section of this file** ("Second run (EA3b)": five seeds, EA_F16 characterised, the L3 iron works, IN5's findings measured, EA_F17 to EA_F24); sections 1 to 8 describe the first run and are not rewritten.

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
| **EA_F1** (S-1, I4) | **High** | `content/base/production/extraction.jsonc` rates x `goods*.jsonc` base prices | A worker-day of raw extraction is worth 8.7 to 9.3 times the 150-mite unskilled wage at book price: mining 8.7x, clay 8.7x, quarrying 8.7x, sand 8.7x, woodcutting 8.8x, peat 8.8x, firewood 9.3x, **salt mining 110x** (500 kg x 33 mites). I4 asks 1.0 to 1.5x. | Whoever owns a mine, woodlot or pit and hires hands earns 1,150 mites a hand a day (3.3x the skilled wage) for no skill; the generated owners' dividends are fed by it; a mine repays in weeks, so the 2-3 year payback target of the industries cannot hold. | R1 (overview Q2: yes): raw price = wage x 1.25 / output kg a worker-day: clay 0.09, firewood 0.27, logs 0.18, ore and coal 0.38, stone 0.19, sand 0.09, peat 0.63 mites a kg. Salt mining needs its own rate or price (110x is not a labour price). Re-run `PriceSolverTests`, pin new numbers, re-pin the trails. | High (arithmetic from content, `EA9_S1_*`, `econ-audit` S-1 and I4) | P7-EA3a. Test `EA_F1_RawExtraction_EarnsBetween1And1p5TimesItsWage`. **Fixed in EA3a:** R1 prices (mites a kg): logs 1.25 to 0.2, firewood 2 to 0.3, clay 0.65 to 0.1, sand 0.65 to 0.1, rough stone 1.3 to 0.2, peat 4.4 to 0.63, coal, galena, copper and tin ore 2.6 to 0.4, dressed iron ore 2.6 to 1.8 and rough iron ore 1.3 to 0.6 (the generated mine's composite is 109 kg of dressed ore a worker-day, so 1.8 is 1.3x its wage), salt 33 to 4.7 with `mining_salt` at 40 kg a worker-day (the salt pans' rate; rock salt at 500 kg x 4.7 was 15x the wage). Every raw kind now earns 1.25 to 1.4x its wage (`AuditMeasures.RawRatios` takes the composite rate for the ore). The five finishing goods of the industries' worked examples are pinned at the R1 table (charcoal 12, bloom 240, bar 480, planks 3.0, bricks 1.2; `RecipeTests.Pinned`) and every good derived from them is solved again; their five recipes leave the 45% band (`BandExempt`). The kiln and hot-floor pieces gained worker hours (60 to 150 and 20 to 60) so the IN-27 bills stay within 20% of table 5.3a at bricks 1.2. **Closed.** |
| **EA_F2** (X5, S-4) | **High** | `content/base/sites/loot_tables.jsonc`; `Sites/SiteDesk.cs:106` (`Make`); `Trade/HeroDesk.cs:657` (`SellItem`) | A loot item is made as one part of its material at its full mass, so the pack values it by `ItemMath.MaterialMites` (material `value_per_kg`), while the site's budget counts its listed `value_mites`. 18 of 25 listed rows are worth more than 1.2x their listed value: gold torc 206x (288,450 against 1,400), reliquary 205x, dead man's purse and ring 192x, silver chalice 82x, rope coil 24x, silver brooch 21x, iron pot 8x, pewter plate 8x. | A site budgeted at a few thousand mites places hundreds of thousands in silver and gold. `SellItem` pays the bid out of a merchant's purse that holds it (a chalice's bid is 92% of 34,614 less the 30% cut for an uninspected item = 22,291 mites, paid by any merchant of the book who holds that much); once bullion has a mint (CN1) the same item melts for its metal at the coinage rate (gold 909 mites a gram). | F-2: `value_mites` computed from material and labour (`ItemMath`) for every site item at load, the loot budget counted in that value; keep a listed override only below the material value. | High (`EA4_X5_*`, `econ-audit` X5) | P7-EA3a. Test `EA_F2_EveryLootRow_IsWithin1p2OfItsMaterialValue`. **Fixed in EA3a:** (a) `SiteLootEntryDef.Material` defaulted to `iron`, so a loot-table row without a material made every site item of iron (a gold torc was 300 g of iron; a salt barrel 30 kg of iron): the default is now empty and the pack uses the site item's material, as `SiteDesk.Make` always said it would; (b) the listed value of each site item and loot-table row is its material value (the budget counts what the pack pays): rope coil 50 to 1,200, iron pot 60 to 500, pewter plate 45 to 360, candlestick 90 to 480, shovel 90 to 400, pick 150 to 520, psalter 260 to 540, book 240 to 660, censer 140 to 320, herbs 30 to 240; (c) the five precious rows keep their budget scale by being the thing they are made of at a believable weight: reliquary is gilt bronze (2.4 kg, 960), silver chalice 120 g (9,230), silver brooch 10 g (769), purse and ring 3 g (231), the torc is now a gilt bronze torc (120), bale of fine cloth 2 kg (1,000), boot 450 g (270); the dug-up hoard's torc is 10 g of gold (9,615). Site budgets are unchanged, so a site can no longer place more than its budget. `AuditMeasures.LootEntryRatios` checks the loot-table rows as well as the site items. **Closed.** |
| **EA_F3** (S-5: phase-6 S10, S14) | (was High) | `Law/Gaol.cs`, `Politics/Diplomacy.cs` | Gaol rations were burned and tribute to an abstract realm moved no coin. | Coin left by a test path; tribute cost nothing. | Fixed in P7-0B (`Gaol.Pay`, `Diplomacy.AbstractHolding`). | High | **Closed** (EA_F3 tests in `JusticeTests` and `P70BDebtsTests`; EA-8 and L2/L3 rows pass). |
| **EA_F4** (S-2, L6) | **Medium** (latent until the mint is wired) | `content/base/crafting/recipes_business.jsonc` (`smelt_galena`, `cupel_silver`), `extraction.jsonc` `mining_silver` | A silver miner digs 500 kg of galena a day = 1.5 kg of silver = **115,380 mites, 769x the wage**. The two generated silver mines are small (115 t of galena a year): measured over four years they made 136 kg of silver = 10.5 million mites = **2.04% of the money supply a year**, as a good that nobody strikes (S-3). The rate is set by the crew, not by the deposit: a crew of 20 for a year would be 108% of the supply. | Once CN1 strikes silver, one hero-owned or NPC-grown mine mints the whole region's money several times over; the desk's coin path (`BoundDeskExports`) stops exports above path+5% but cannot undo a mint. | F-4: deposit grades (0.01 to 0.05%, default 0.02%) so the crews the game can make mint 1 to 2% a year; and a cap on a licence's daily take (the royalty and licence rows already exist). Pin the rate: `EA2_Probe_*` asserts 0.5 to 3% a year today. | High for the rates; the cap is a design call | P7-EA3a with CN1 (CN1 wires the mint, so the exposure opens there). Pinned by `AuditWorldProbeTests`; no failing test until a mint exists. **FIXED in P7-EA3c:** the cupel recipe makes 0.15 kg of silver from a tonne of galena (0.015%, was 0.3%) and `refine_gold` 1 g of gold from a tonne of ore (was 6 g); recipes and money keys only, no generated layer moved. Measured with the mints wired (`MoneyGrowthTests`, peace, 60-day years, minted coin over the starting supply): seed 1234 0.73, 1.48, 1.85, 2.27% in years 1-4 (mean 1.58%), seed 42 0.81, 1.84, 2.45, 2.19% (mean 1.82%); before the fix 25-28% a year. `econ-audit` S-2 and L6 pass. The licence cap on a daily take is not built (the rate is the deposit's). |
| **EA_F5** (X2) | Low | `Caravans/ContinentDesk.cs:70-95` (`DeskExport`), `ContinentTrade.ExportDiscountPermille` 900, staples 950 | The border desk pays the book's *reference* price less 5 to 10% for a glut, whatever the book's own price is. The desk acts above 2.8x cover (1.5x when the region's coin is under its path), where the book's ask is 0.58 to 0.76 of the reference: the desk's bid after the toll is 1.2 to 1.9x the book's ask. At target stock it is 0.84x (no loop). | The producers of a glut are paid above the local price by the continent's coin (bounded by `PayBudgetMites` and the desk's room); a trader who moves goods from one glutted book to another and relists them to the desk earns the gap. Bounded and self-closing (buying lowers the stock below the threshold), so not an exploit. | Price the export at the book's mid (the curve's value at the stock after the export) with the discount, floored at 0.9 x the clamp. | High (`EA2_X2_*`, row X2) | P7-EA3a, optional. Test `EA_F5_DeskExportBid_IsNeverAboveTheBooksAsk` (skipped). Recommend **accepting** (Low). |
| **EA_F6** (overview 15) | Low | `content/base/crafting/goods_crafting.jsonc:12` | Rope is priced at 2,000 mites a kg, 4.1x bar iron and about the price of the linen it is spun from per labour hour times ten. Carts, harness, sails and every vehicle recipe that uses it inherit it. | Cart and ship costs in EA3b and the naval part are several times too dear. | Reprice under R1 with the other goods (the price solver derives it from its inputs and labour). | High | P7-EA3a with EA_F1. Test `EA_F6_Rope_IsNotDearerThanBarIron`. **Fixed in EA3a:** rope has no recipe the solver reads, so it takes its twine: twine 1,800 to 200 a kg (a spinning wheel, not the hand rate of `spin_twine`) and rope 2,000 to 298 (0.5 kg of twine and forty minutes of braiding per 0.45 kg); 0.6x bar iron, a cart's rope 120 mites. **Closed.** |
| **EA_F7** (W2, WL-8; the open point of EA1) | **Medium** | `Diagnostics/WarLoad.cs` `Begin`; the real source is `army-economy.md` 4.9 (`DaysFunded`) | The realm treasury of seed 1234 held 0 mites at the Y6 muster (4,277 later): nothing raises war coin. A host of 2,000 costs 391,500 mites a day (pay 261,000, staple 94,000, fodder 36,000): 11.7 million a month, **9% of all the coin** (128.3 million). | The Y6 and Y7 hosts starve and desert from the first day, so WL-4 and WL-5 measured nothing. | **Decided and built in the harness (`WarFunding`):** the realm raises what the host needs, in order, from (1) aid: half of each fief treasury (`TransferReason.Aid`), (2) a tallage on household purses above 2,000 mites, an equal permille of the excess up to 15% (`TransferReason.Tallage`). Every step is a ledger transfer in a fixed order. A 30-day purse raises 11.7 million (7.7 aid, 4.0 tallage at the start). The real rule, with scutage, subsidy and loans, is AE1's `DaysFunded`; the harness shows a region **can** pay for a month and **cannot** pay for the Y8 90-day war (15.4 million raised, 30.5 million of arrears, 80,227 unpaid man-days, 802 stand-in deserters). | High | Harness decision made here (`EA_F7_TheRealmRaisesTheCoinOfACampaign_*`, passes). The real rule is **P7-AE1**. **Closed in the audit; carried to AE1.** |
| **EA_F8** (X6, X13) | **Medium** (design, for CN1) | `Economy/MintMath.cs`; `coinage.md` 5 | By the arithmetic of the tag's constants a strike pays the seller 92.5% of parity, so buying bullion and striking it **profits whenever bullion is below 0.925x of coin parity** (+15.6% at 0.8x, +208% at the market's 0.3x clamp); and a melt (coin to silver, no fee, `SinkReason.Melt` exists) **profits whenever bullion is above 1.0x** (+50% at 1.5x, +300% at the 4x clamp). The spec says a margin only inside the 7.5% band. Nothing runs today (S-3), so nothing is exploited. | The day CN1 wires strike and melt, a bullion price outside the 0.925 to 1.0 band is a free-coin loop limited only by silver's stock; the market reaches the clamps at thin stock (silver trades in grams). | CN1: strike pays at the book's silver price (not parity), melt only at the mint at the same price less seigniorage, both refused outside the band, or a changer fee above the gap; test EA-5 (`CN-` row) with the table printed by `EA5_X6_*`. | High (arithmetic) | **P7-CN1** (a test there; none fails today). Signature not needed if CN1 pins it. |
| **EA_F9** (W4, WL-6) | **Medium** | `WarSupply/HostSupply.cs:377` (`Muster`), `Farming/FarmingSystem.cs:334` (`IsEmployedElsewhere`) | **1,040 levies out through the harvest cost the harvest nothing**: the Y8 harvest is 2,681 t against the control's 2,676 t (-0.2%; the gate asks 10 to 35% below). The hook works (a mustered man's hours are removed: the Y8 unmet farm hours are 778,570 against 753,396 in the control, +3.3%), but a levy takes one man per household at random and 1,040 men x 8 h is 9% of the busiest day's supply (94,370 h against a demand of 274,731 h). | "Levies out of the harvest" (plan 3d, the cost of war to a realm) is invisible: wars have no harvest price. | Either levy by the household's adult count (the fief's real quota) and take field hands first, or let the planner's deadlines turn unmet reaping into loss; decide with AE2. | High for the measure; the cause is a design choice | **P7-AE2 / P7-EA3b.** Test `EA_F9_LeviesOutThroughTheHarvest_CutTheHarvest10To35Percent` (skipped, Soak). **Open; the user may accept** (Q15). **P7-EA3c: partly done, the gate still fails** (see "EA3c: what the harvest does and does not do" below). |
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
| High findings | EA_F1, EA_F2 **closed in EA3a**. |
| Medium findings | EA_F4 (CN1), EA_F8 (CN1), EA_F9 (AE2/EA3b), EA_F11 (SC-A, AE3, EA3b), EA_F16 (EA3b: five seeds first) open; EA_F7 closed in the audit and carried to AE1. |
| Gates | WL-1 to 5, 8, 11 pass on seed 1234 (12 years); on seed 42 (10 years) WL-3 fails in peace (EA_F16) and the others pass (section 6); WL-6 fails (EA_F9); WL-10 fails as worded (EA_F10); WL-7, 9, 12, 14 not measurable at the tag; five-seed EA-15 is EA3b's. |
| `econ-audit` | Prints a result on every row; rows that fail after EA3a: X2 (EA_F5, accepted Low), S-2 and L6 (EA_F4, owner CN1), S-3 (no mint, CN1); X5, S-4, S-1 and I4 pass. `--strict` therefore exits 1 until CN1 lands (and X2 is switched to accepted). |

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
| X5 | pass after EA3a (was fail, High) | every loot row and loot-table row within 1.2x of its material value (was 18 of 25, worst 206x) | EA_F2 |
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
| I4 | pass after EA3a (was fail, High) | raw stages earn 1.25x to 1.4x their wage at book prices | EA_F1 |
| S-1 | pass after EA3a | EA_F1 | |
| S-2 | fail | EA_F4 | |
| S-3 | fail (by design) | no mint runs; silver piles up as a good (136 kg in four years) | CN1 |
| S-4 | pass after EA3a | EA_F2 | |
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
  - **Children help with the harvest** (the user, later the same day): children from 7 add a third of a day to reaping, binding and hay, so the fix is calibrated with them counted (overview §10c).
- **EA_F16 (money supply swings outside -5%..+8% in peace on seed 42):** **investigate on five seeds first** (EA3b); accept only if border trade causes it and the mints don't add to it; otherwise fix.
- **Low findings EA_F5, F10, F12, F13, F14, F15:** **accepted** as known limits; later parts pick them up where they matter (F10 is a spec-wording fix; F12 to AE1/AE2; F13 to the siege parts).
- Not accepted (to be fixed): High EA_F1 (R1 repricing, EA3a), EA_F2 (loot values, EA3a); Medium EA_F4 and EA_F8 (CN1 pins them), EA_F11 (carried to SC-A/AE3/EA3b).


## EA3c: what the harvest does and does not do (EA_F4 and EA_F9, 21 Nov 2026)

**EA_F4 (fixed).** Money growth from the mints, measured on seeds 1234 and 42 over four 60-day peace years: 25-28% a year before (silver 1,200 kg a year at 3 kg a tonne, plus gold at 6 g a tonne), now 0.7-2.5% a year (silver 0.15 kg a tonne, gold 1 g a tonne; the lead's base price follows the solver, 27.298). The rate climbs for two years while the mines and hearths fill their pipelines and settles near 2% by year 4 (seed 1234 2.27%, seed 42 2.19%): at the top of the plan's 1-2% band; trim the gold grade if the user wants less.

**EA_F9 (not fixed; needs a decision).** Built: `HostSupply.Muster` takes field hands first (a farmer, ploughman, labourer or shepherd, or an unoccupied man of a peasant household), each group in household order, one man each, by the fief's war quota; and the 10c children (`child_harvest_min_age` 7, `child_harvest_pct` 33; hours for Reap, BindCart and hay only, own household first, hireable at the pro rata day wage; 12-15 at half a day for everything) with their test (`FarmingChildrenTests`). Result: the Y8 harvest is still **-0.4%** against the control (WL-6 fails; `EA_F9_*` stays skipped). Why: the sim's harvest is not labour-bound. The region has 113,000 farm hours a day against reaping and binding spikes of 100,000 hours that clear in two or three days, inside a 10-day grace; 1,040 levies are 7 to 9% of the hours (a quarter of the men's), and delay a field by about one day, which costs nothing before the deadline. I tried to make the harvest bind, on the war year in Y3 against its own control (harvest t, control / war):

| Setting tried (on top of children and field hands first) | Control | War | War loss |
|---|---|---|---|
| as shipped (reap 2, bind 4 acres a day, grace 10, overdue 40 per mille a day) | 2,208 | 2,206 | 0.1% |
| reaping a man's task only | 2,217 | 2,212 | 0.2% |
| reap 1, bind 2, grace 4 | 2,181 | 2,176 | 0.2% |
| as above, grace 5, overdue 250 | 2,019 | 1,977 | 2.1% |
| harvest labour x4 (a sickle reaps half an acre), grace 10, overdue 40 / 100 | 2,086 / 2,032 | 2,061 / 1,995 | 1.2% / 1.8% |
| harvest labour x2, grace 10, loss total after the deadline | 2,113 | 2,093 | 1.0% |
| harvest labour x3, grace 10, loss total after the deadline | 1,977 | 1,931 | 2.3% |
| harvest labour x2, grace 4, loss total | 1,788 | 1,746 | 2.3% |
| harvest labour x3, grace 4, loss total | 1,506 | 1,375 | 8.7% |
| harvest labour x4, grace 4, loss total | 1,251 | 1,093 | 12.6% |

The war's loss is always about a fifth of the control's own loss to late harvest: the labour a levy takes is spread over villages that mostly have enough hands, so no setting gives 10 to 35% against an undamaged control (the 12.6% row has the control at 56% of today's harvest). None of these was kept; the sim is as in the first row. Options for the user: (1) accept that a levy of one man in five costs the harvest about nothing in this model (the audit's recommended "accept" path, Q15); (2) a historical harvest (sickle rates, a short reaping window, a total loss when the window closes) with the control's harvest and prices re-calibrated around it, which is an economy retune and its own part; (3) draw the levy by village (the same men from fewer villages) so the villages that lose their reapers lose their harvest.
- **EA_F9 after EA3c (21 Nov 2026, the user):** the even levy still costs -0.4%; **draw the levy by village** (overview §10d), owner EA3d.

## EA3d: the levy is drawn by village (EA_F9, 21 Nov 2026)

**Built (overview 10d).** `HostSupply.Muster` fills each fief's quota village by village. The lord's order: villages (5 or more households) nearest the fief's seat first, then the farmsteads (under 5 households), the seat itself last (the lord's own demesne keeps its hands). A village sends one man a household, field hands first (then the man who is not the head), up to `levy_village_cap_pct` (content, farming settings, default 50) of its households and at least one man, before the next is asked; the men are recorded per village and year in the war book (`Levied`, saved as an additive `warsupply` key; no version bump). At the year's close a village that sent men and left grain standing gets a `harvest_lost_levy` chronicle entry (place, men, kg rotted; cause `ch_why_levy_drew_reapers`, kind War). Grain not reaped is lost by the existing rules (4% a day after the grace, all of it at the year's close): no new loss rule.

**Measured (seed 1234, Y8, 1,040 levies):** harvest 2,681 t against the control's 2,675 t (**-0.2%**; WL-6 asks 10-35%), farm hours unmet 759,692 against 728,342 (+4%). The levy does bite where it falls: by day 30 of the year the 133 drawn villages have reaped 23,403 acres against the control's 23,995 (2.5% behind), while the 250 undrawn villages have reaped 10,475 against 10,463 (the same, `LevyByVillageTests`). But the delay is made up inside the 10-day grace, so the grain lost in the named villages is 0 to 1 t each (a few tonnes in all). Cap 100% (a man from every household) and every man of the drawn villages (cap 300%) gave the same result.

**Why it cannot reach 10%.** Reaping is done by every adult from 12 up of a farming household, the idle hands, and the children, not only the men; a village that sends half its men still has more than half its hours, and a levy of one man in five is 9% of the region's hours however it is drawn. A village would have to lose about four fifths of its reaping hours to miss the grace. **WL-6 still fails and `EA_F9_*` stays skipped** (the soak test still expects Fail for WL-6). To make the war cost harvest the options are the ones of EA3c: a harvest labour retune (sickle rates and a short reaping window, with the control re-calibrated), or drawing more than one man in five from the drawn villages (a war quota change), or accepting that this war costs no harvest.
- **EA_F9 closed as accepted (21 Nov 2026, the user):** after EA3d the village levy costs -0.2%; the user accepted that war costs no harvest here (overview §10e); WL-6 is waived.


## Second run (EA3b, 22 Nov 2026)

Audited commit: `d050f20` plus the EA3b harness (below). Spec: `economy-audit.md` 5 and 7 ("the same soak with `coinage.md` (mints wired), `economies-of-scale.md` and `industries.md` in, the scripted L1 works and an L3 iron works near the route"). Everything the first run's section 1 listed as missing is in: the mints strike (CN1, deposit grades from EA3c), the scale rules, the industries with NPC founders (IN5), the repricing (EA3a) and the village levy (EA3d). **No sim rule, content value or golden was changed**; what changed is the harness and the gates' reading, listed at the end.

### 1. What was run

| Run | What | Where |
|---|---|---|
| Five-seed soak | `war-load --seed S --years 25 --l3 caravan --with-control`, S in **1234, 42, 1, 7, 99** (the user's list; the spec's 9001 was not used). The war scenario of 5.2, the control (no load), both with the **scripted L3 iron works** (below) and the NPC founders running. 25 game years of 60 days each, about 45 minutes a seed (five in parallel on four cores). | the five `WL` tables of section 2 |
| Peace, no works | `war-load --seed S --years 25 --control` with no L3: the clean series for EA_F16 | section 3 |
| WL-13 and WL-14 | `war-load --seed 1234 --years 12 --with-control --checks` (the 12-year run, then two more ten-year runs for the hashes and the round trips) | section 2 |
| L3 variants | seed 1234, 12 years, war: the L3 selling at the gate only; the L3 with a fuel faucet | section 4 |
| `econ-audit --strict` | seeds 1234, 42, 1, 7, 99, each after 120 game days | section 6 |
| Tests | `EA13_RichPlayerBot_5Years_KeepsThePriceIndexWithin1200Permille` (WL-15), `WarLoadSoak_Years1To10_WithControl_Gates`, `EconAuditTests`, the new `WarLoadSecondRunTests`: all green | |

**The scripted L3 iron works** (`L3IronWorks`, `Diagnostics/WarLoadIndustry.cs`): founded on day 1 of Y2 at the town within 8 km of the march route (a town that already works iron preferred; none did, so the nearest), by the richest household head of that town aged 26 to 40 (a works closes when its owner dies, EA_F20); every stage at tier 3 with the crew at the tier's top (mine 20, crusher 16, yard 6, smeltery 20, packing 5: 67 hands, the sheet's L3 band 44-67); the powered machines assumed built and running; **it sells through its own caravans**: four ox wagons, two guards, a standing order over the border gate to the first reachable neighbouring region (overview 10b: "owners must ship far"), a Trading-60 trader of the town as master. The building bills are not paid (nobody builds): the bill is the three tiers' plans at the base prices, **674,003 mites** (the sheet's 908,000 was drawn before EA3a), put on the works as its `BuildingsCostMites` so the sim's own books measure the payback. The crew comes from a hiring hall that takes labourers from other businesses (the market has none idle, EA_F21: 68-75 hands taken). Working capital is seed money: 3 million at the founding and 1 million whenever the works' purse falls under 600,000 (`TopUps`, counted, and subtracted from the money supply column). The founders' tending leaves the works alone (its start day is set in the future). Both the war run and the control carry it, so "caused by the war" still compares like with like.

### 2. WL-1 to WL-15 on five seeds (25 years, war with the L3 works, against its control)

| Gate | 1234 | 42 | 1 | 7 | 99 |
|---|---|---|---|---|---|
| WL-1 coin | PASS | PASS | PASS | PASS | PASS |
| WL-2 mass | PASS | PASS | PASS | PASS | PASS |
| WL-3 money supply (avg 0.5-2.5% a year; every year -5% to +8%) | **FAIL**: 0.50% (just under), worst -1.6%, best +2.6% | PASS 0.89%, -4.0%, +5.8% | **FAIL**: 0.85%, **-9.5%**, +5.8% | **FAIL**: -0.10%, **-10.1%**, +7.7% | PASS 0.76%, -2.0%, +2.4% |
| WL-4 staple price, route peak / region peak (<= 3,000 / 2,000 permille of the Y3-Y5 mean) and the route's Y25 mean (<= 1,300) | PASS 1,100 / 1,054 / 733 | PASS 1,123 / 1,177 / 770 | PASS 1,139 / 1,104 / 754 | **FAIL** 2,199 / **2,246** / **1,670**; **the control: 2,318 / 2,246 / 1,756** | PASS 1,123 / 1,098 / 898 |
| WL-5 starvation (<= 3% of Y5 outside the siege; the town's days of food at the siege's end) | PASS 0 deaths; 172 days | PASS 0; 128 | PASS 0; 132 | PASS 0; 168 | PASS 0; 96 |
| WL-6 harvest | **waived** (overview 10e): Y8 war 2,658 t against control 2,659 | waived: 2,547 / 2,551 | waived: 2,645 / 2,627 | waived: 2,193 / 2,166 | waived: 2,266 / 2,246 (every seed within +-1.2%) |
| WL-7 unrest | not measurable: part AE2 (purveyance and grievances) | same | same | same | same |
| WL-8 treasury (raised; man-days unpaid; arrears; stand-in deserters) | PASS 76.5 M; 77,794; 29.1 M; 777 | PASS 66.5 M; 82,719; 31.8 M; 827 | PASS 71.7 M; 83,554; 32.4 M; 835 | PASS 67.1 M; 80,907; 32.6 M; 809 | PASS 56.0 M; 85,815; 33.5 M; 858 |
| WL-9 kit | not measurable: parts SC-A's fill and AE3 (the load carries no armouries) | same | same | same | same |
| WL-10 recovery (Y25 population of Y5 >= 95%; wages a day within 10% of the control at the same year, EA_F10; idle < 800) | PASS 99.7%; -1.3%; 29 | PASS 99.8%; +3.7%; 39 | PASS 99.6%; +3.2%; 28 | PASS 99.5%; +3.4%; 41 | PASS 99.5%; -5.8%; 42 |
| WL-11 merchants (every year >= 60%; Y8-Y9 >= 40%) | PASS 93% / 97% | PASS 95% / 98% | PASS 95% / 98% | PASS 90% / 95% | PASS 88% / 93% |
| WL-12 chronicle | not measurable: part AE2 (the army's causes) | same | same | same | same |
| WL-13 determinism and saves | **PASS** (seed 1234 only, as the spec asks): two runs equal at Y10 day 60 (hash `1EDC6B0BE7EDB110`); save, load, save byte-identical at Y6 day 20 (35 sections, 2.8 MiB), Y8 day 40 (36 sections, 4.2 MiB, the siege on) and Y9 day 10 (4.0 MiB), the `warsupply` section present each time | not run | not run | not run | not run |
| WL-14 budget | **PASS** (seed 1234 on a quiet machine, 12 years): slowest hour-18 step 86 ms, mean game day 215 ms against the control's 217 ms | the five-process runs (four cores) measured 516 ms worst hour and 674 ms a day, 3 times the quiet figure: the gate is not read from them | same (442 ms / 667 ms) | same (357 ms / 645 ms) | same (601 ms / 659 ms) |
| WL-15 rich player | PASS (`EA13`, seed 1234, 5 years: price index within 1,200 permille, every mite traced to a ledger transfer) | not run | not run | not run | not run |

Reading it:
- **WL-3 fails on three seeds and WL-4 on one; both are the economy's own and the same in the control** (sections 3 and 5): the WL-3 misses are border trade after a poor harvest (EA_F16), the WL-4 miss is a harvest-failure year (839 t in Y24 on seed 7, in the control too) that raises the staple price with no army anywhere. **No gate fails because of the war.**
- The war rows are as in the first run: the host is paid for 30 days and then is not (77,000 to 86,000 man-days unpaid, 29 to 34 million mites of arrears, 777 to 858 stand-in deserters a seed). That is the load doing its job (WL-8) and the open point of AE1 (`DaysFunded`, scutage, subsidy, loans).
- The prices on the route peak at 1.10 to 1.14 times the Y3-Y5 mean on every seed but 7; the siege town keeps 96 to 172 days of food at the siege's end, so nobody starves (EA_F13 stands: sieges do not bite in this economy).
- WL-6 is waived: the war costs the harvest nothing on any seed (-1.2% to +0.8%). WL-10 is read against the control: the wages a day drift +5% to +40% from the Y3-Y5 mean in the control itself (EA_F10, accepted).
- **WL-14 on the five-seed runs is not a measurement**: five processes on four cores made every step three times slower. The quiet run is the figure. The hour is the host slot's hour-18 step, not the abstract hour alone; the continent tick is inside it.

### 3. EA_F16: the money-supply swing, on five seeds (the user's rule: accept only if border trade causes it and the mints do not add to it)

Method: the peace run of each seed with no L3 works (no seed money to confuse it), 25 years. The harness now splits the year's change of the money supply (coin in purses) into what crossed the border (the ledger's `Immigrate` and `Emigrate`), what the mints struck (`Mint`) and what was melted; and the border into **trade** (the continent desk's and the caravans' exports paid by `Immigrate`, imports paid by `Emigrate`) and **people** (immigrants' stakes, emigrants' purses, tolls). **The change of the supply is exactly border net plus mint minus melt every year on every seed (residual 0.00%).** Nothing else makes or unmakes coin.

| Seed | Net growth Y1-Y25 (Y10-Y25) | Worst year, best year | Years outside -5%..+8% | Border trade net a year, mean and sd | People net, sd | Mint a year, mean (Y10-25) and sd | Year-4 mint over the start supply | Growth against trade (corr) | Outside the band without the mint |
|---|---|---|---|---|---|---|---|---|---|
| 1234 | 1.06% (0.88%) | +0.4%, +3.9% | none | -0.62%, 0.79% | 0.06% | 1.68% (1.66%), 0.29% | 2.12% | 0.93 | none |
| 42 | 1.00% (0.83%) | -1.9%, **+8.2%** (Y7) | Y7 | -1.35%, 1.73% | 0.15% | 2.33% (2.34%), 0.43% | 2.50% | 0.97 | none |
| 1 | 1.00% (0.80%) | **-8.8%** (Y2), **+8.5%** (Y6) | Y2, Y6 | -1.35%, 2.78% | 0.16% | 2.39% (2.44%), 0.42% | 2.50% | 0.99 | Y2 |
| 7 | 0.11% (-0.58%) | **-9.4%** (Y2), +8.3% (Y4) | Y2, Y4, **Y24 (-11%), Y25 (-5%)** | -2.29%, 3.74% | 0.20% | 2.44% (2.35%), 0.46% | 2.46% | 1.00 | Y2, Y24, Y25 |
| 99 | 0.78% (0.50%) | -3.1%, +4.2% | none | -1.43%, 1.18% | 0.27% | 2.20% (2.10%), 0.51% | 2.99% | 0.89 | Y24 (-5.0%) |

**Verdict.**
1. **Border trade causes the swing; people do not.** The correlation of the year's growth with the net of trade is 0.89 to 1.00 on every seed; the people's net (immigrants' stakes against emigrants' purses) has a standard deviation of 0.06% to 0.27% of the supply and never explains more than 5% of the variance. The gross trade flows are very large against the region's coin: **a game year's exports are 20% to 44% of the supply and the imports 20% to 43%**, so a net balance that wanders by a few per cent of the gross is a swing of 5 to 13% of the supply.
2. **The cause behind the trade is the harvest.** The big drops follow poor harvests: seed 1 Y2 (1,542 t against about 2,500 usual) and seed 7 Y2 (1,429 t) lose 9.6% and 11.5% to grain imports; seed 7 Y24 (839 t, the worst harvest of any seed) loses 13% and Y25 (1,752 t) a further 7%; the rebounds (+6 to +8.5%) are the good years that export a surplus. Imports of food in a hunger year draining the region's coin is arguably the model working; it is the size that is wrong for a gate written as -5%..+8% a year.
3. **The mints do not add to the swing, with one qualification.** They are the smooth part: +1.7% to +2.4% of the supply a year with a standard deviation of 0.3% to 0.5%, 1% to 16% of the variance of growth. Settled (Y10-Y25) they strike **1.66%, 2.34%, 2.44%, 2.35%, 2.10%** of the supply a year on the five seeds: **over the plan's 1-2% on four of five** (the plan's figure is the growth the mines give; the border takes 0.6% to 2.2% a year back, so the net growth, 0.1% to 1.1%, is inside the 0.5-2.5% WL-3 average on four seeds). Qualification: a mint year of +2.1% to +2.8% is what lifts a +5.4% to +6.2% rebound over the +8% edge on **seeds 42 (Y7), 1 (Y6) and 7 (Y4)**; without the mint none of those three is outside the band. The mints also soften three drops (seed 99 Y24 from -5.0% to -3.1%).
4. **The measure `MoneyGrowthTests` makes** (year 4 over the starting supply): 2.12%, 2.50%, 2.50%, 2.46%, 2.99% on seeds 1234, 42, 1, 7, 99. Seed 42 matches the 2.6% that test saw; seed 99 reaches 2.99%, so the test's top of 3% (it said "temporarily") is not slack. Settled it is 1.7% to 2.4%.

**By the user's rule (22 Nov 2026 note):** border trade does cause it, and the mints are smooth, so the swing is **acceptable in kind**, but the mints do decide three over-the-top years, so "the mints don't add to it" is true of the variance and false of the +8% edge. **Not signed by me; the user decides.** Recommended: accept the swing as border trade driven by the harvest and **change the gate, not the sim**: read WL-3's band on the supply **net of coin struck at the mints** (the mints are a known +2% faucet; the gate then asks about the border alone), keep -5%..+8% for it, and keep the average 0.5-2.5% on the full supply. With the mint taken out, seeds 1234, 42 and 99 pass every year, seed 1 fails only Y2 and seed 7 fails Y2, Y24 and Y25 (the harvest failures). If the user wants the border itself calmer, the fix is a governor on the continent desk (cap the net coin a season's trade moves at about 3% of the region's supply, or smooth `PayBudgetMites` and the import room over two seasons); it was not built because it changes the trade rules.

### 4. The L3 iron works: export payback against the gate's room (overview 10b)

Per seed, the works founded in Y2 (war run; the control is within 8% of it except where noted), the bill 674,003 mites, selling on its own caravans over the border gate:

| Seed | Cumulative net first above the bill (and stays) | Net a year, Y8-Y25 | Years with a loss | Working capital put in (seed money) | Caravans, lost, sold | Notes |
|---|---|---|---|---|---|---|
| 1234 | Y6 (stays only from Y19) | **-179,000** (control +937,000) | 11 of 24 | **19 M** (control 9 M) | 59, 0, 161.8 M | the smeltery stops for want of charcoal or ore for stretches; Y10-Y18 lose up to 4.4 M a year, and 16 top-ups were needed |
| 42 | Y5 | +1.68 M (control +2.02 M) | 3 of 24 | 4 M | 90, 0, 184.1 M | steady from Y5: income 8.1 M, inputs 4.5 M, wages 1.0 M, upkeep 0.9 M |
| 1 | Y6 | +1.15 M (control +1.24 M) | 4 of 24 | 6 M | 72, 0, 173.5 M | |
| 7 | Y5 | +0.06 M | 4 of 24 | 6 M | 23, 0, 40.2 M | **the owner died or left in Y9**: a works closes with its owner (EA_F20), income stops |
| 99 | Y8 | +1.82 M (control +1.65 M) | 5 of 24 | 9 M | 77, 0, 176.2 M | |

- **Payback: the cumulative net (after every start-up loss) first covers the 674,000-mite bill in Y5 to Y8, that is 3 to 6 game years after the founding, against the industries' 2 to 3 year target;** once running it earns 1.1 to 2.0 million a year, a steady-state payback of 0.3 to 0.6 years, so what delays it is the start-up: the first year loses 1.6 to 3.1 million (the first fuel and ore bought, wagons and crews paid before the first sale), and **the owner has to put in 4 to 19 million of working capital: 6 to 28 times the building bill.** A real owner (a household with 500 gp, 500,000 mites) could not carry it: the audit's seed money did.
- **It does ship far, and far is the only way it earns.** Sold through the gate only (seed 1234, 12 years, war): income 1.0 to 2.7 million a year (Y5-Y12) against inputs of 0.7 to 3.6 million; **the works loses 14.2 million by Y12 and never pays back**; it needed 12 top-ups of 1 million on top of its 3 million. On its own caravans with a fuel faucet it nets 2.7 to 4.4 million a year and the bill is covered by the end of Y3 (cumulative +2.2 M); with real fuel 0.9 to 2.0 million. The user's 10b reading is confirmed: the L3's output (8 to 10 million mites of bar a year at the border; the region's whole bar output with it is 8 to 35 tonnes a year, about 4 to 17 million mites at 480 a kg) does not sell at home. (The gate-only and fuel-faucet variants were run with the first owner choice, the richest head of the town, who lived through the 12 years.)
- **The gate's room was not the limit.** 59 to 90 trips, none lost, every trip sold; the L3's exports are 8 to 10 million a year, about a fifth of the region's border exports (25 to 55 million a year, section 3). The desk's season budget did not bind on any seed; the limit is on the input side (next).
- **What stops it is charcoal (EA_F19) and cash (EA_F18).** At full output the smeltery's 36 units eat about 2.4 tonnes of charcoal a day (industries 5.5); the whole region makes 2.9 to 11.0 tonnes a day (the burners' output, 175,000 to 660,000 kg a game year). With real fuel the smeltery reads `NoFuel charcoal` or `NoInput` at the year's end in most years while the mine and crusher over-produce ore (120 to 360 tonnes a year pile in the yard until it is full at 300 tonnes): an L3 mine and crusher feed a smeltery that never runs full. With the faucet (2,293 tonnes of charcoal created in 12 years, 3.2 a day) the same works earns 3.5 million a year.

### 5. IN5's findings, measured (five seeds, 25 years)

| Finding (IN5's words) | Measured | Class | Owner |
|---|---|---|---|
| Reference prices about double base by day 60, wage classes fixed | **Not reproduced.** In the soak world the books' reference price is **one multiplier for every good** (the 25th to 90th percentiles are equal): 0.92 of base at the end of Y1, 0.76-0.85 at Y5, 0.69-0.73 at Y10, 0.75-0.93 at Y25 (seed 7 0.93: the poor harvests). It follows the coin per head, not the wages: the wages a day paid region-wide are 5% to 30% above the Y3-Y5 mean by Y25 in the controls. So prices fall against wages (real wages up). The doubling IN5 saw is the rig's (a 4 million mite hero, the faucets). **Consequence:** IN-20's L1 paybacks (145 to 148 days, measured at the doubled prices) are about half what the soak's price level gives; the NPC wood works earn -919 to +2,939 mites a day each (the sheet's +490): the calibration needs re-measuring in the soak world | Low (a rig artefact) with a Medium consequence for IN-20 | IN5 (re-measure at the soak's prices) |
| Almost no idle labour in towns | **Confirmed region-wide: 1 to 60 idle adults (16-59, no work, no land) of 22,000 to 26,000 people, 0.005% to 0.25%, in every year of every seed** after Y1 (Y1 76). The gate's "< 800" is met a hundred times over. An L3 works (67 hands) and a clay works (69) cannot be crewed from the market; the audit's hall took 68 to 75 labourers from other businesses | **Medium** (EA_F21) | IN3/growth: let founders and works hire from employed labour at a higher wage (poaching), or release the farms' winter hours |
| Region-wide charcoal shortage (~1,000 kg a day) | **Confirmed and worse for an L3**: stock 76,000 to 191,000 kg (8 to 26 days' cover, the price 0.73 to 1.82 of base), the burners make 2.9 to 11.0 tonnes a day, an L3 smeltery needs 2.4; a faucet of 3.2 tonnes a day was needed to keep it running | **Medium** (EA_F19) | IN5: an NPC charcoal chain (no founder has one), or burners that answer the price |
| Markets saturate by a works' second year | **Confirmed for wood**: logs trade at 47% to 64% of base from Y3 on every seed but 7; the NPC wood works earn -919 to +2,939 a day each against the sheet's +490; the clay and iron NPC chains are never founded (the labour gate, above) | Low (EA_F22) | IN5 |
| NPC founder churn (15 works in two years): the utility counts works, not the book's depth | **Smaller than IN5 saw, same cause.** 10 to 27 NPC works founded and 11 to 24 closed in 25 years per seed, never more than 6 open counting the L3, **all of them wood** (no iron or clay works but the L3 appears in any chain line of any seed) (the other chains' founders are stopped by the hands gate); a works lives about 5 years; the founders' count of rivals (the crowd rule) holds the number near 5 | Low (EA_F22) | IN5 |

### 6. `econ-audit --strict`

After the harness reads a world that has run (two game years: `econ-audit --days 120`, new) it prints **50 rows: 14 pass, 0 fail, 5 info, 31 pending, exit 0 on all five seeds.** Before this part the command read a day-0 world and printed **two failed rows**: **X2** (the desk pays above the book's ask at a glut: EA_F5, accepted Low by the user on 21 Nov; it now prints as `INFO ... ACCEPTED Low`) and **S-3** ("no mint has struck a coin": true on day 0 only; the mints strike from Y2: 1.0 to 3.0 million mites a year; it now passes after the 120 days). The 31 pending rows are the ones the war-load soak, the bots and the tests close (X1 trader EA-14, X8-X11, X12 the slider, X13, W1-W7, B1-B6, G1-G5, I1-I3, S-5, S-7, S-8); the audit command does not measure them itself and says which part does. Their evidence in this run: W1, W2, W4, W6 and G1-G4 are the WL-4, WL-8, WL-6, WL-5 and WL-10/WL-3 rows above; B1 iron is section 4 (the region's bar output with the L3 is 8 to 35 tonnes a year); B2 charcoal is EA_F19; B6 labour is EA_F21; I1, I2 are IN-20 and EA_F22; W3 (kits), W5 (purveyance) and W7 (carts) wait for SC-A's fill, AE2 and AE1/AE2.

### 7. New findings of the second run

| # | Severity | What | Evidence | Proposed fix | Owner | New? |
|---|---|---|---|---|---|---|
| **EA_F16** (open since the first run) | **Medium**, characterised | The money supply swings by up to -11% and +8.5% a year on four of five seeds. | section 3: border trade, driven by the harvest; people moves 0.0-0.3%; the mints are smooth but decide three +8% years; the supply change is border net + mint exactly | accept the cause; read WL-3's band net of the mint (no sim change); or a governor on the desk's net coin | **the user to decide** (not signed here) | no |
| **EA_F18** | **Medium** | An L3 iron works needs 4 to 19 million mites of working capital, 6 to 28 times its building bill, and repays in 3 to 6 game years, not 2 to 3; at the gate alone it never repays. | section 4 | none to the sim; the industries' payback target should be read on cumulative net with the start-up, and the capital need shown in the Industry tab (IN3 view) so a player is not surprised | IN3 view / EA close-out | **new** |
| **EA_F19** | **Medium** | The region's charcoal cannot carry an L3 smeltery (2.4 t a day against 2.9 to 11 t for the whole region); the mine and crusher over-produce ore that piles to the yard's 300 t. | section 4, 5 | an NPC charcoal chain and burners that respond to price; or an L3 mine/crusher sized to the smeltery (crew bands) | IN5 (a follow-up part before M-E0 or recorded as a known limit) | **new** (IN5 saw the shortage; the L3 shows its size) |
| **EA_F20** | **Medium** | **A works closes when its owner dies or leaves** (`IndustryWorld.cs` line 235: no heir, no sale): the scripted L3 of seed 7 ended in Y9 (inferred: the owner died or left; the scripted works is exempt from the founders' loss rule, and this is the other close path); in the first run all five L3 owners (the richest, so oldest, heads) ended their works between Y4 and Y19. `CloseWorks` closes every stage's business. | the first set of runs and seed 7 | the works passes to the household's heir (as a business does through `Succession`) or is put up for sale | IN (a follow-up) / Phase 7 succession | **new** |
| **EA_F21** | **Medium** | No idle labour: 1 to 60 idle adults in 25,000 people. Works of more than a dozen hands can only be crewed by poaching; the NPC founders only found wood works. | section 5 | let a works bid wages above the class rate to employed hands; or release farm hands in winter | IN3 / growth | **new** (IN5 saw it in towns; it is region-wide) |
| **EA_F17** | Low | WL-4 compares the war with the Y3-Y5 mean, so a harvest-failure year with no army fails it (seed 7, the control equal). | section 2 | read WL-4 against the control at the same year (as WL-10); the harness now prints the control's own peak beside it | spec wording, the user | **new** |
| **EA_F22** | Low | NPC works: 10 to 27 founded and 11 to 24 closed per seed, all wood, a few hands each; wood saturates (logs 47-64% of base) and the NPC works earn about half the sheet or lose. | section 5 | none; calibration with the L1 paybacks re-measured at the soak's price level | IN5 | **new** |
| **EA_F23** | Low | Bread trades at 2.3 to 3.3 times its base price on every seed and bricks at 1.2 to 3.1 times, all 25 years, while grain is at 0.5 to 0.9: the mid price of the thinly stocked goods sits at the high end of the curve. | the `prices` lines of the five tables | the bakeries' calibration (EA_F14 context); not a war matter | economy calibration (later) | **new** |
| **EA_F24** | Low | The mints' settled rate is 1.7% to 2.4% of the supply a year, over the plan's 1-2% on four of five seeds; the net after the border is 0.1% to 1.1%. | section 3 | none needed (the net is what the gate reads); `MoneyGrowthTests` keeps its 3% top (seed 99 reaches 2.99%) | the user (a trim of the gold grade if the mints alone should be 1-2%) | **new** |

Accepted as in the first run: EA_F5, F10 (now implemented in the gates), F12 to F15. EA_F11 (iron): the region's bar was about 10 tonnes a year in the first run and 8 to 35 with the L3; it is fuel-bound (EA_F19): carried to SC-A/AE3 as before.

### 8. What M-E0 still needs

- **The user's signature** on EA_F16 (section 3: accept the cause, and the gate read net of the mint?), and on EA_F17 (WL-4 against the control).
- **The four Medium findings of this run** (EA_F18, F19, F20, F21) fixed or accepted in writing (Q15). F20 (the works dies with its owner) is the one the military parts will meet first: the realm's armourers and the hero's own works.
- **WL-7, WL-9, WL-12** cannot be measured until AE2 (purveyance, grievances, the army's chronicle causes) and AE3 (kits filled from armouries and the arms trade) exist; the five-seed run is to be repeated with them (the harness carries the columns).
- WL-13 and WL-14 on the other four seeds (only seed 1234 was run; the spec asks 1234), and WL-15 on the other seeds (the bot test runs seed 1234).
- EA_F11 (iron) re-measured with SC-A's armouries and AE3's arms trade in.
- `econ-audit` has **no failed row** (section 6); its 31 pending rows are closed by the soak and the tests, the audit command should print their results too: not done here.

### 9. What changed in the harness (nothing in the sim's rules)

`Diagnostics/WarLoadIndustry.cs` (the scripted L3 works), `WarLoadChecks.cs` (WL-13), `WarLoadRun.cs` (the new columns: coin by source and the border by trade and people, works founded and closed, the price level, charcoal and iron stock and output, the clock, the L3 and chain lines, an `AtDayStart` hook), `WarGates.cs` (WL-6 waived and printed as information, WL-10 read against the control at the same year, WL-4 prints the control's figures, WL-13 and WL-14 evaluated), `EconAudit.cs` (X2 prints as accepted), `EconAuditCommand.cs` (`war-load --l3 none|gate|caravan --l3-fuel --checks`, `econ-audit --days N` with a run world), the tests `WarLoadSecondRunTests` (new) and the WL-6 expectation in `WarLoadSoak_Years1To10_WithControl_Gates` (Fail to Info: the user's waiver), the comment of `MoneyGrowthTests`. No test was loosened; no golden moved.
