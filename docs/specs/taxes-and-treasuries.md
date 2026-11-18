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
