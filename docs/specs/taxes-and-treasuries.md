# Taxes, treasuries and unrest basics

Plan refs: §3 item 3 (money moves as wages, taxes, rents, trade, tribute; old money creation removed: poll tax on abstract
classes, 2% interest, +500 crowns on promotion), §4A.1 (money: mints take 5-10%, treasuries are chests, taxes are transfers
between purses: hearth tax ~2 crowns per household per season, market toll 5%, gate tolls, tithe in kind; dividends from the
cash box), §4A.2 (government: fief = parcels plus rights to tax, levy and judge; taxes are real transfers collected by the
reeve; arrears -> debt -> eviction; higher tax levels create more grievances; unrest ladder; the four named tax levels; levy
1 per 10 households in peace, 1 per 5 in war), §4A.4 (tax/tribute context), §5 Phase 2 ("Faction treasuries; basic tax
transfers"). Replaces legacy `ConsumptionSystem.gd:184-202` (two overlapping daily taxes, a poll tax on abstract class
counts) and the stipend bug (`s.faction_id` vs `faction`).

## 1. Purpose
Public bodies hold real coin and goods in chests, take from households by named, scheduled, partial-payment transfers, and
spend on real people (stewards, reeves, priests, guards, soldiers). A burden that is too heavy produces hunger, grievances,
evasion and riots through the same mechanisms as every other pressure (no abstract "tax happiness" meter).

## 2. Data

### 2.1 `TreasuryTable` (~140 per region: 1 realm account per realm present, ~35 fiefs, ~65 parishes/town councils)
`Id` (`IdKind.Account`), `Kind` (`Realm, Fief, Parish, Town, Mint`), `Holder` (person index of lord/priest/mayor, -1 none),
`Settlement` (seat), `Fief` index (Phase 1 `Fief`), `Realm` index, `Account` (money spec), `StockOwner` (goods: tithe barn,
lord's granary), `Policy` (`TaxPolicy`), `IncomeSeason` (rolling), `ExpenseSeason`, `ArrearsOwed` (to staff), `Region`.
`TaxPolicy`: `Level` (enum `Low, Normal, High, Crushing`: the legacy "four named tax levels"), `TitheEnabled`,
`MarketTollPermille` (default 50), `GateTollMites`, `RentShareBp` default for new leases (2500), `CrownSharePct` (30, share
of hearth tax forwarded to the realm).
`Arrears` rows (struct of arrays) keyed by household: `DueMites`, `Seasons`, `Creditor` (treasury or landlord household),
`Kind` (`Hearth, Rent, Dwelling, Debt`).
Per settlement `Unrest` (0..1000), `UnrestLadder` (0..4 current rung), `LastRiotDay`. Per household `Grievance` bit set
(households spec 2.1), `Levied`.

### 2.2 Content: `content/base/economy/taxes.jsonc`
```jsonc
{ "type": "tax_policy_defaults", "id": "default",
  "hearth_tax_mites_per_household_season": { "Low": 100, "Normal": 200, "High": 350, "Crushing": 600 },
  "crown_share_pct": 30, "tithe_permille": 100, "market_toll_permille": 50,
  "dwelling_rent_mites_per_season": { "Cottage": 100, "Longhouse": 150, "Burgage": 400 },
  "parcel_coin_rent_mites_per_field_year": 320, "rent_share_bp_default": 2500,
  "arrears_debt_seasons": 3, "arrears_evict_seasons": 6, "evasion_chance_pct_with_grievance": 30,
  "tax_burden_grievance_permille": 150,
  "unrest_rungs": [200, 350, 550, 750], "unrest_hunger_days": 5, "grievance_decay_days": 15,
  "staff_wage_mites_per_day": { "steward": 350, "reeve": 250, "priest": 250, "guard": 250, "soldier": 250 },
  "lord_share_pct_of_surplus": 40, "levy_peace_per_households": 10, "levy_war_per_households": 5 }
```

## 3. Rules

### 3.1 Hearth tax (seasonal S4, first season day, households ascending)
`due = hearth_tax[Level] + arrears_hearth`; `paid = min(account, due)`; `Transfer(household -> fief treasury, paid, Hearth)`;
unpaid part carries as arrears. Of the fief's receipts `CrownSharePct` (30%) is forwarded at the same step to the realm
treasury (`Hearth`). Households marked `Levied` pay as normal (their harvest loss is the cost). Households with
`Grievance.Tax` and fief `Unrest >= rung 2` evade with probability 30% (stream 0x260, key household id): `paid = 0`, arrears
+due, chronicle only at settlement level ("tax evasion in X", cause = the grievance sources).
The old taxes (poll tax by class counts, daily tax, interest) do **not** exist.

### 3.2 Rents
- **Parcel rent**: Share leases pay in kind at harvest (farming 3.4); Coin leases pay at autumn S3: `due = fields x 320 mites/field-year` (autumn instalment). Landlord = fief treasury for Demesne-adjacent tenancies, a burgher/lord household
  for private ones (their household account), the parish for glebe tenants.
- **Dwelling rent** (deed `Rented`): per season by dwelling type to the landlord's account (`Dwelling`); deed `Tenant` of the
  lord: dwelling rent included in parcel rent, plus the labour service of the lord's demesne (boon work: 2 worker-days per
  household per season, offered first to demesne parcels in farming 3.2); deed `Owned`: none.
- Rent arrears use the same ladder.

### 3.3 Tithe (in kind)
10% of a field's net harvest (farming) and of animal products (`wool`, `milk`, `eggs`, lambs/calves: `tithe_permille` of
production counted at the creation step) go to the **parish store** of the settlement's parish (`Tithe`). Glebe parcels are
exempt. The priest's household takes a `priest_share_pct = 40` of grain tithe in kind for upkeep (stipend wages are separate:
250 mites/day from the parish chest when it can pay); the remainder is the **charity reserve** and a seasonal surplus sold
through the market by the parish (S5, coin to the parish chest). Parish granary spoilage uses the `granary` class
(1%/season).

### 3.4 Market and gate tolls
Market toll: 5% of the seller's proceeds, deducted in the sale transaction (money spec 3.3); recipient = the market owner
(chartered town -> `Town` treasury; else the fief). Gate/bridge tolls (caravans spec 3.5) go to the holder of the node. No
tolls at region borders.

### 3.5 Arrears -> debt -> eviction
For each household with arrears (any kind), per season: `Seasons++` while unpaid. At `arrears_debt_seasons` (3): arrears
convert to a `Debt` (creditor keeps, no further penalty; interest does **not** accrue, plan removes 2% compounding), tenants
may work it off with labour (2 worker-days per 100 mites at demesne). At `arrears_evict_seasons` (6): **eviction** for deeds
`Rented`/`Tenant`: the household loses its dwelling and parcels; members move in with the nearest kin household with spare
beds (population 3.8 order), else become `Homeless(NoRoom)` and migrate; the creditor claims `min(debt, household stock + account)` first; dwelling goes back to the vacancy list; parcels are re-let at S4 (to the highest-labour household without
land, ascending id). `Owned` households facing the same ladder are subject to a lien (Phase 6); Phase 2 only keeps the debt
record. Chronicle entry `Tax` cause ("evicted for arrears of N seasons") with the household and creditor.

### 3.6 Spending (stipends, wages, retinues)
From the fief treasury at the **end of each season day-1 pass** (S4 after collection) and paid daily thereafter by the
treasury's slice: steward 350, reeve 250, guards 250 (`Occupation.Guard`), priest 250 (parish), soldiers/levies on service
250/day (Phase 7), construction contracts (Phase 5), famine relief purchases (buy grain for charity when `FoodCoverDays < 3`
share > 15%: the fief steward posts a buy order at the market, capped at 20% of the treasury per season). Residual surplus
above a `reserve = 2 seasons of expenses`: `lord_share_pct_of_surplus` (40) goes to the lord's household as `Dividend`-like
`Stipend`, the rest stays. Wages are paid from the treasury in ascending person index until it is empty; unpaid staff accrue
`WageOwedMites` (guards desert after 3 unpaid days in Phase 6).
The legacy bug (`s.faction_id`) is closed by one typed `TreasuryTable.Faction`/`Realm` link used everywhere.

### 3.7 Realm treasury (basic)
Receives `CrownShare`, seigniorage (money 3.7), tolls at realm gates and tribute (Phase 6); pays the lords' stipends?
(**none** in Phase 2), mint staff, realm officers (steward per fief: wages above come from the fief; the crown pays nothing
locally), and holds the rest as chest coin. Under a `Closed` continent flow its balance is just a number in
`RegionSummary.TreasuryCoin`. Realm taxes between L3/L4 regions are closed-form (continent spec 3.5).

### 3.8 Unrest and grievances
Households carry grievance bits, set daily at H5 by rules and cleared after `grievance_decay_days` (15) without the trigger:
| Bit | Trigger |
|---|---|
| `Hunger` | `FoodCoverDays < 3` for 5 consecutive days |
| `Tax` | (hearth + coin rent + dwelling rent per season) / (`Income15d` x 4) > 150 permille, or `Level == Crushing`, or an eviction notice pending |
| `Crowding` | crowding > 100% for 15 days |
| `Cold` | `FuelCoverDays == 0` and `T_mean < 0` |
| `Crime`, `Lord` | stubs for Phase 6 (always clear) |
`Unrest` (settlement, 0..1000) = `1000 x weighted households with >= 1 bit / households`, weights: `Hunger` counts 1.5, `Tax`
1.0, others 0.5 per household with that bit only (capped per household at 1.5), then rounded and clamped to 1000. The
**ladder** (rungs 200 / 350 / 550 / 750):
1. **>= 200 rumours**: a `Rumour(kind=discontent, subject=lord/steward)` seed per season (Phase 3 spreads it).
2. **>= 350 evasion**: tax and levy evasion enabled (3.1; levy evaders hide, `Levied` quota unfilled 30%).
3. **>= 550 riot**: once per 30 days at most, a riot of `max(3, 5% of households)` real people (chosen by ascending hash of
   `(day, household)` among households with a grievance) storms the tithe barn or granary: stock transferred to the rioters'
   households pro rata (`Riot`), steward guards fight (Phase 4 live or abstract roll), the lord's reputation falls (Phase 6),
   chronicle `Famine`/`Tax` cause chain listing the grievances.
4. **>= 750 for 2 consecutive seasons: revolt brewing**: Phase 2 emits the event and stops; Phase 6 gives it the effect
   "changes who holds the fief".
Unrest returns down one rung when below `rung - 50` for 15 days.

### 3.9 Levy (Phase 2 data only)
`levy = households / 10` in peace, `/ 5` at war (sec. 1: ~520 / ~1,040 for 5,200 households), ascending person index among
males 16-40 who are not the sole adult of their household; `Household.Levied` reduces workers; the farming labour model feels
it (a levied man is out of the harvest); pay 250 mites/day from the treasury; call/discharge is a command (Phase 7).

## 4. Tick placement
- **S4 (seasonal, first season day)**: hearth tax in household index order (slice by settlement), coin parcel rent at autumn
  start, dwelling rent, crown share forward, arrears ladder, re-let parcels, treasury payroll start.
- **H4 slice of the treasury** (index mod 24): daily wages.
- **H5 hour 0**: grievance bits, `Unrest`, ladder, riot checks.
- **H2/H3**: market tolls and gate tolls as part of trades.
- **S5**: parish surplus sale, lord stipend, reserve.

## 5. Determinism
Collection order: settlements ascending, households ascending; due amounts are integer (`floor`); evasion uses
`DetRng.Chance` with `(hourKey, householdId, 0x260, season)`; riot participants by a stable sort of `DetRng.Next(…, 0x260, 100 + day)` over eligible households; treasuries pay wages in ascending person index.

## 6. Interactions
Households (accounts, grievances, `Levied`), farming (tithe/rent in kind, labour service), markets (tolls, parish sales,
relief purchases), caravans (tolls), population (eviction, famine -> unrest), money (seigniorage, every transfer reason),
continent tick (summary taxes), chronicle (`Tax` cause, rumours), LODBridge (reeve/steward appear collecting), Phase 6 (laws,
revolt, reputation).

## 7. Edge cases
- Treasury with no coin: payroll pauses; owed wages accrue; no new coin is created (no "printing"); the steward may buy less
  grain.
- Fief with no lord person (dead): treasury continues, payroll from the `Holder == -1` rule (steward as caretaker); the
  lord's share accumulates in the chest until a holder exists (Phase 6).
- Household with zero income and zero stock facing hearth tax: pays 0, arrears; priority to food if `Survive` tier (a
  household in `Survive` skips voluntary payments: the steward waives the hearth tax 50% when `Hunger` is set and the fief is
  not `Crushing`).
- Hero as taxpayer/landlord: the hero's household is an ordinary household; hero-owned parcels use leases with the same
  ladder and chronicle causes.
- Currency rounding in splits: fief 70% = `floor`, realm gets the remainder; never a mite lost.


## Decisions made in this spec (collected in 00-overview.md section 10.4)
- **T1** Decision: Hearth tax per household per season: Low 100 / Normal 200 / High 350 / Crushing 600 mites; 30% forwarded to the realm. Reason: plan: ~2 crowns per household per season and the four named levels.
- **T2** Decision: Arrears become debt at 3 seasons and eviction (rented/tenant) at 6; no interest accrues. Reason: plan: arrears -> debt -> eviction; plan removes 2% interest.
- **T3** Decision: Tithe is 10% in kind of net harvest and of animal products; the priest keeps 40%, the rest is charity and parish sales. Reason: plan §3a and §4A.1.
- **T4** Decision: Unrest rungs 200 / 350 / 550 / 750; riots by real households; revolt is only flagged in Phase 2. Reason: revolt changes fief holder, which needs Phase 6 rule.
- **T5** Decision: The lord receives 40% of the fief surplus above two seasons of reserve. Reason: closes the money loop without printing.
- **T6** Decision: Households with a tax grievance evade with 30% probability once unrest passes 350. Reason: plan: unrest ladder includes evasion.

## 8. Golden tests
| id | Case | Expected |
|---|---|---|
| G-T1 | Hearth tax at `Normal`, region of 5,200 households, none in arrears | `5,200 x 200` = 1,040,000 mites (10,400 crowns) per season; fief 70% = 728,000, realm 30% = 312,000; `Low` 520,000; `High` 1,820,000; `Crushing` 3,120,000 |
| G-T2 | Household owes 200, account 150 | pays 150 (arrears 50); next season due `200 + 50` = 250; if it pays 250 arrears clear |
| G-T3 | Arrears ladder, never pays, due 200 | after 3 seasons arrears 600 -> debt; after 6 seasons 1,200 -> eviction (Rented/Tenant); `Owned` keeps a debt record |
| G-T4 | Tithe: wheat field net 42,240 g; 1,000 sheep produce 164 kg wool, 57 lambs | tithe grain 4,224 g; tithe wool 16.4 kg; lambs 5 (+1 with probability 0.7, seeded) |
| G-T5 | Market toll on a sale of 7,776 mites | toll 389 (388.8 half up) to the market owner; seller nets 7,387 (money G-M3) |
| G-T6 | Coin parcel rent: tenant with 12 fields | `12 x 320` = 3,840 mites per year, paid at autumn start; share-lease equivalent 25% of the same fields' net at ref price ~= 3,800 (farming G-F16) |
| G-T7 | Dwelling rent per season | cottage 100, longhouse 150, burgage 400 mites; a tenant of the lord pays none but owes 2 worker-days per season |
| G-T8 | Unrest: 100 households; 30 with `Hunger` only, 10 with `Tax` only, 5 with both | weights: hunger-only 30 x 1.5 = 45, tax-only 10 x 1.0 = 10, both 5 x 1.5 (cap) = 7.5 -> total 62.5 -> `unrest = 1000 x 62.5 / 100` = 625: rung 3 (riot) reached, not 750 |
| G-T9 | Evasion at rung 2: 200 households with `Tax` grievance, p = 30% | evaders 60 +- 8 (seeded); their dues carry as arrears; revenue falls by 30% of their share |
| G-T10 | Charity: parish has 10,000 kg grain; 8 lowest-cover households of 5 members | 0.5 measure x 5 x 8 = 20 measures = 22,000 g handed out that day; reserve 10,000 kg -> 9,978 kg (+ spoilage) |
| G-T11 | Levy: 5,200 households | peace 520, war 1,040; wages 250 mites/day x 520 = 130,000 mites/day = 1,950,000 per 15-day season (treasury must hold it or levy is reduced) |
| G-T12 | Stipend cycle: fief with 728,000 intake, payroll steward 350 + reeve 250 + 6 guards 1,500 + priest 250 = 2,350/day = 35,250 per 15 days | surplus 692,750 > reserve (2 seasons of expenses 70,500) -> lord share 40% of (728,000 - 35,250 - 70,500) = 248,900 to the lord household |
| G-T13 | Conservation: 100 seasons of tax, rent, stipend flows | coin audit exact; `sum(treasuries)` never negative; no coin created |
| G-T14 | Permutation: process households of a settlement in random order | identical balances, arrears, evictions |
| G-T15 | Eviction cascade: Rented household of 5 with arrears 1,200 | dwelling vacant, 3 members to kin (beds), 2 homeless, parcels re-let to the lowest-id landless household, chronicle `Tax` entry with creditor |

## 9. Soak invariants
1. Coin audit exact; every treasury and account non-negative for 100 years.
2. At `Normal` tax: tax burden (hearth + rents) 3-12% of household income for median households; `Unrest` mean < 150 in
   normal years; riots <= 1 per 20 settlement-years; rung 4 (revolt brewing) never in normal years.
3. At `Crushing`: unrest > 350 within 2 years and evasion visible (test shows the lever works).
4. Fief treasuries: 90th percentile balance < 10 seasons of income (spending keeps pace); >= 95% of payroll paid in full in
   normal years.
5. Tithe flows: grain tithe per year within 8-11% of net harvest (excluding glebe), spoilage in parish < 2% of grain.
6. Evictions <= 0.3% of households per year in normal years; every eviction has the arrears chain recorded.


## 10. Notes: Phase 2 implementation (taxes, treasuries, unrest basics)

Code: `src/Ravensguard.Sim/Taxes/` (`TaxSystem`, `Treasuries`, `Tolls`, `TaxState` and `TaxSave`, `TaxTypes`), content `content/base/economy/taxes.jsonc`
(`TaxPolicyDef`), tests `tests/Ravensguard.Sim.Tests/Taxes/TaxTests.cs`, soak `--tax [--tax-year N]` (also printed by `--diag`). The economy world owns one
`TaxSystem` (`EconomyWorld.Taxes`); two systems drive it: `tax-daily` (H5, hour 0) and `tax-collection` (H6 S4, first day of a season). Everything below is Phase 2 scope
(plan 3j: hearth tax, rents, tithe, the crown share and basic treasuries); subsidies, tallage, customs, estates, vassal dues, loans and purveyance are Phase 6 and 7.

### 10.1 What exists
- **Treasury records** (`Treasuries`): one per realm present (purse `Treasury`, owner a faction id built from the realm's history index), per fief (the existing
  fief-treasury purse, so the market tolls, relief, workshop advances and manor payroll that already used it are unchanged), per chartered town (a settlement without a fief: its
  `Settlement` purse, which is where the market book already sent the toll) and per parish (the `Chest` purse of the priest's settlement). All are ledger purses, hence physical
  chests that can be raided. `Treasuries.All`, `Balance`, `PayeeSlot[settlement]` (where a settlement's hearth tax and rents go), `RealmSlotOfSettlement`.
- **Ledger view**: `Ledger.Tap` (a second hook next to `Observer`, which the soak's coin trace owns) feeds `TaxState.Income/Expense[slot x transfer reason]` for the season; the
  season rolls at hour 0 of its first day (`RollSeason`), and `Treasuries.History` keeps every finished season by kind (diagnostic, not saved). `IncomeOf(slot, reason)`,
  `ExpenseOf`, `LastSeasonReport()`, `EconomySoak.TaxReport(year)`. There is no `EconomySnapshot` in the code yet; the view is this one.
- **Hearth tax** (3.1): at S4 the reeve's round of settlement `s` runs at hour `s mod 24` of the first day, households ascending (G-T14: the order does not matter, tested): `due =
  level amount + arrears`, `paid = min(coin, due)`, to the settlement's fief treasury (town treasury when it has no fief), reason `Hearth`; 30 percent of the settlement's hearth
  receipts (the fief keeps `floor(70 percent)`, the realm the remainder) go on to the realm's treasury in the same step. The fief's level is data (`TaxState.FiefLevel`, `TaxSystem.SetLevel`).
- **Dwelling rent** (3.2): a household with deed `Rented` pays 100 / 150 / 400 by dwelling type to the same treasury (reason `Rent`); deed `Tenant` pays none (its labour
  service is Phase 5/6); `Owned` none. **Coin parcel rent** (autumn, 320 mites a field and year), **share rents** and the **tithe in kind** stay where they were, in farming
  (one source: `content/base/economy/taxes.jsonc` does not repeat them), and were verified: G-F16 and G-T4 (`YieldMath.Split`: tithe 4,224 g of a net 42,240 g).
- **Market tolls**: the books already take 5 percent (rounded half up, G-T5) to the fief treasury or town purse; unchanged. **Gate and bridge tolls**: see 10.3.
- **Arrears ladder** (3.5): see T11 to T14. Arrears carry; at the third unpaid season they convert to a debt (no interest); a debt is paid down with up to half of what a household
  holds above its dues; at the sixth season `Rented` and `Tenant` households are evicted (O11: everyone, the hero's tenants included; the hero's own household is an ordinary one);
  `Owned` households keep the debt record. The chronicle entry `eviction` has the cause `Tax` ("evicted for arrears of N seasons") with the household and the creditor.
- **Evasion** (3.1): a household with the `Tax` grievance in a settlement at rung 2 or above withholds its dues with probability 30 percent, `DetRng.Range(seed, day, household id, 0x260, 0, 0, 100)`;
  the dues carry as arrears and the settlement gets one `tax_evasion` chronicle entry a season (cause `Tax`). Deterministic and order-free (test G-T9/G-T14).
- **Unrest** (3.8): `Tax` bit set daily (H5, hour 0) by `TaxGrievance`: a `Crushing` level, a lease coin rent behind, arrears of two seasons (or an eviction notice pending: five), or dues
  over 150 permille of an income of 1,000 mites a member and season. `Hunger` and `Cold` come from the household rules (existing). Unrest per settlement and per fief, rung ladder 200 / 350 / 550 /
  750 with the step-down rule (below `rung - 50` for 15 days) and the 30-day test of rung 4, `TaxState.Unrest/Rung/FiefUnrest` are saved data for Phase 6. Rung 1 counts a rumour seed
  a season (`RumourSeeds`, Phase 3 spreads them); rung 3 allows one **riot** per 30 days: `max(3, 5 percent)` households with a grievance (ascending hash of day and household, stream 0x260) take 20 percent
  of the parish granary's grain, shared evenly (goods move, none are made), chronicle `riot` with the grievances as causes; rung 4 for a month emits `revolt_brewing` and stops (T4).
- **Payroll**: see T15. **Lord stipend**: at the last hour of the first day, `LordShare` (G-T12). **Levy** (3.9): `TaxState.LevyDue[fief]` = households / 10 in peace (`/5` at war,
  `TaxSystem.AtWar`), refreshed each season; no `Levied` flag or hearth-tax effect yet (Phase 7).

### 10.2 State, save and determinism
`TaxState` (tax levels, arrears rows, the season's and last season's receipts and payments by reason, wages owed, lords' shares, unrest and rungs, riot days, tallies) lives in
`AbstractState.Taxes` and is saved as key 31 of the abstract section (`TaxSave`; rows in ascending household order; a save from before it loads with a fresh state; the by-reason
tables are re-padded if a transfer reason is ever appended). Coin is never kept in it. The per-hour lists the collection builds (households by settlement, parcel arrears, workshop
boxes) are rebuilt for every hour they are used, so a resumed world sees what the uninterrupted one saw (`ResumeDeterminismTests` is green with the taxes on).

### 10.3 Public toll API for the caravan system
```csharp
Tolls.Compute(TaxPolicyDef p, TollKind kind, int vehicles, long cargoValueMites, int marketPermille = 50) -> long   // pure
Tolls.HolderPurse(SimWorld w, int settlement) -> PurseId     // fief treasury of the settlement, else its town's, else its realm's; None if it has none
Tolls.Charge(SimWorld w, PurseId payer, PurseId holder, TollKind kind, int vehicles, long cargoValueMites) -> long paid
Tolls.ChargeAt(SimWorld w, PurseId payer, int settlement, TollKind kind, int vehicles, long cargoValueMites) -> long paid
Tolls.MarketToll(long proceedsMites, int permille = 50) -> long   // G-T5: 7,776 -> 389
```
`TollKind.Gate` = carts x 25 + 5 permille of the cargo's value (G-L3: 4 carts, 64,800 mites: 424); `Bridge` = carts x 10; `Market` = the 5 percent. A payer that holds less than the toll pays what
it holds (tolls carry no arrears); the transfer is `TransferReason.Toll` (`MarketToll` for the market kind), so it shows as income of the holder's treasury in the ledger view. Constants are
`gate_toll_mites_per_cart`, `bridge_toll_mites_per_cart`, `gate_toll_permille_goods` in `taxes.jsonc`. If the caravan agent does not call it, it records the toll itself, as the caravans spec says.

### 10.4 Decisions made during integration (T7 and on; fold into overview 10.4)
- **T7** Treasuries are ledger purses named by `Treasuries`; their income and payments by transfer reason are tallied through `Ledger.Tap`, which sees every transfer (tolls, relief, wages, chevage). Reason: one view of every public chest without touching the callers.
- **T8** Parcel coin rent, share rent and the tithe percentage live only in the farming content; `taxes.jsonc` holds the hearth tax, dwelling rent, tolls, the ladder, unrest and wages. Reason: no second copy to drift.
- **T9** An unrest figure uses at least ten households as its denominator. Reason: a lone farmstead with one `Tax` bit read 1,000 and rioted (36 riots in 3 years in the first run).
- **T10** The crown's local outlay (supersedes 3.7's "pays nothing locally"): each season the realm gives `crown_outlay_pct` (80) of its chest to the fief treasuries of its realm as a grant (reason `Stipend`) in proportion to their manor payrolls: the king's sergeants and castle guards. Reason: the region is closed; a realm chest that only fills takes 1 percent of the coin supply out of circulation a year (hearth 30 percent of 40,000 crowns a year against 1.39 million). Phase 6 gives the crown real expenses and ends this.
- **T11** A coin parcel rent that is behind stays on the lease (`CoinArrears`, farming); it breeds the `Tax` grievance but is not a season of the ladder. Reason: rent day (autumn day 1) comes before the harvest is sold; counting it evicted about 70 households a year from year 6 and the region lost 5 percent of its people.
- **T12** A second round on the eighth day of a season (`CatchUp`): households in arrears pay what they hold then, hearth first, the realm's share follows, and a household that clears its arrears starts its count again; rows of vanished households are written off. A household in want (`Hunger`) owes half its hearth tax and is left 200 mites a member; a master whose coin is in his workshop's box pays the rest of his dues from it, up to half the box. Reason: coin is out in wages on the first day (many master craftsmen showed zero in the household purse and thousands in the box).
- **T13** Eviction is the region's `DepartHousehold` (the creditor claims the household's coin up to the debt first; parcels fall to the lord, the dwelling is vacated); there is no kin move-in or homeless state yet (population 3.8 order, Phase 5). A `Rented`/`Tenant` household with no one aged 16 to 59 is not evicted at the sixth season: the steward writes the debt off for charity (`WrittenOff`). Reason: the elderly widowers of the towns were most of the evictions and the region does not refill fast enough.
- **T14** Labour works arrears off at 2 worker-days per 100 mites (50 mites a day): 12 worker-days a season for each of up to two adults of a `Rented`/`Tenant` household (credit, no coin; no farm hours are drawn yet). Reason: with 8 days a lone adult still lost 200 mites a season and evictions ran at 0.4 percent a year; the ladder still evicts at `Crushing` (tested) and for those who cannot work.
- **T15** Payroll: the reeve, steward, guards and servants are the staff of the manor business and are paid from its cash box, which the fief's treasury fills first (the existing 20-days top-up, reason `Stipend`): that is the treasury's payroll, with wages owed (`Business.Owed`, three short days quit) when it is short. New: the parish chest pays its priest 250 mites a day, owing up to 90 days when empty (`WageOwed`), and each season the priest's household puts half of what it holds above 30 days of food into the chest (reason `Tithe`: the tithe sales are the parish's). The lord's share is `LordShare(balance, season payroll, 2 seasons reserve, 40 percent)`: with no treasury surplus (the manors drain it) it is rarely above zero; 913 crowns in 25 years.
- **T16** Tithe of animal products: a tenth of the wool and of the cheese goes to the parish granary (G-T4: 164 kg of wool, 16.4 kg tithe); the parish sells it with the grain. Lambs and calves (pieces) are not tithed in Phase 2.
- **T17** Riots take grain from the parish granary only (no steward fight, no reputation: Phase 4/6); the chronicle names the grievances. They do not occur in the 25-year run (unrest peak 272); the `Crushing` test shows the lever.

### 10.5 Tests
`tests/Ravensguard.Sim.Tests/Taxes/TaxTests.cs`: G-T1 (levels, fief 70 / realm 30, on the real ledger), G-T2, G-T3 (debt at 3, eviction at 6, `Owned` keeps a record), labour work-off, parcel rent not
a season, the helpless written off, G-T5 and the toll API, G-T8, G-T9 with G-T14 (seeded evasion, same result in reverse order), G-T11, G-T12, parish stipend and wages owed, the crown share on the
real tick (equal to the forwarded tally, 30 percent), a year of conservation (coin and mass exact, no treasury negative), `Crushing` raising unrest and riots, and the save round trip.
`HerdTests` was adjusted for the wool tithe. `ResumeDeterminismTests` runs unchanged.

### 10.6 Soak, seed 1234, release, `soak --years 25 --diag --tax-year 10`
Year: population / under-15 % / hungry household-days % / staples in band % / workshops finishing a batch % / merchants solvent % (pre-taxes in brackets at year 25: 25,226 / 32.2 / 0.06 / 70.5 / 82 / 95):
**1** 25,666 / 35.1 / 0.04 / 78.1 / 98 / 96; **5** 25,563 / 33.1 / 0.14 / 78.8 / 92 / 96; **10** 25,461 / 30.2 / 0.18 / 69.9 / 88 / 93; **15** 25,307 / 29.2 / 0.06 / 72.9 / 87 / 95;
**20** 25,270 / 30.6 / 0.08 / 71.9 / 87 / 99; **25** 25,192 / 32.2 / 0.09 / 72.0 / 83 / 92 (population -1.8 percent, hungry 0.11 percent over the run, band 74 percent, workshops mean 88, merchants mean 95;
coin audit exact every year). Over 25 years: hearth tax 957,000 crowns (due 999,000, evaded 674, waived 587), dwelling rent 96,000, crown share 288,000 (outlay 288,000), 42 evasion events, 0 riots,
evictions 1, 1,023 arrears converted to debts, 49,500 crowns of arrears worked off, 547 written off for the helpless; unrest mean 25, peak 272. Year 10, income and payments by season and
kind of treasury, crowns (fief: market toll about 33,000, hearth about 9,600, rent about 850; realm: the 30 percent share about 2,880, returned the same season; parish: the tithe sales banked about 17,000):
spring realm in 2,873 / fief in 50,044 [Rent 829, Hearth 9,577, MarketToll 35,805, Escheat 972, Stipend 2,861] out 49,134 [Trade 3,638, Hearth 2,873, Stipend 41,240, Alms 1,382] / parish in 16,928 out 17,729;
summer realm 2,888 / fief 44,725 [Rent 898, Hearth 9,624, MarketToll 31,032, Escheat 286, Stipend 2,885] out 46,446 / parish 17,629 out 17,632;
autumn realm 2,887 / fief 47,708 [Rent 886, Hearth 9,622, MarketToll 34,310, Stipend 2,890] out 49,706 (lord share 15) / parish 16,476 out 17,825;
winter realm 2,811 / fief 34,864 [Rent 824, Hearth 9,369, MarketToll 21,854, Stipend 2,817] out 35,266 / parish 17,100 out 15,438.
