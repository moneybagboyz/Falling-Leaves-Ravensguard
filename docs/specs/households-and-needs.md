# Households, needs baskets and household decision AI

Plan refs: §4A.1 (keep: three-tier work logic, class-graded needs, buy-order pull, needs basket in kg), §4A.2 (households are
the unit, housing capacity, overcrowding), §3a (the measure, "food in days"), §5 Phase 2 (households: needs baskets).
Replaces legacy `ConsumptionSystem.gd` (class counts and a poll tax) and the daily per-tile loop.

## 1. Purpose
A household is the unit that lives somewhere, holds stores and coin, eats, burns fuel, wears things out, shops, sends its
workers to jobs and pays/receives money. It is the "decision AI" of plan §4A.1: **survive, then buffer, then profit.**

## 2. Data

### 2.1 Columns added to `HouseholdTable` (Phase 1: `Settlement, Head, MemberStart, Lot, MemberCount, Beds, ParcelCount, Dwelling, Deed, Class, Purse, Name`)
| Column | Type | Meaning |
|---|---|---|
| `Region` | byte | current region |
| `Account` | = household index | coin (replaces the `int Purse` placeholder, plan money spec) |
| `MarketSettlement` | int | settlement whose book the household uses (own if it has a market, else road-nearest) |
| `Tier` | enum `Survive/Buffer/Profit` | decided at the daily slice, kept for dashboards |
| `FoodCoverDays`, `FuelCoverDays`, `CashCoverDays` | short (x10) | cached daily |
| `ArrearsMites` | long | taxes/rent owed (taxes spec) |
| `Grievance` | ushort bit set | `Hunger, Tax, Crowding, Crime, Lord, Cold` for unrest (taxes spec 3.8) |
| `Levied` | byte | members taken for levy (reduces workers) |
| `StoreOwner` | EntityId | key into `StockTable` (the household id) |
| `SeedReserveG` | int | grain reserved for next sowing (farming 3.5) |
| `Income15d`, `Expense15d` | long | rolling 15-day sums for hoard/buffer decisions |

Columns added to `PersonTable`: `Region` (byte), `NutritionCd` (short, centidays 0..3000, default 3000), `JobKind` (enum
`None/Own/Employee/Hired/Forage`), `JobRef` (business/parcel/extraction id), `WageOwedMites` (int arrears owed to this
person), `QuitCounter` (byte).

### 2.2 Content: `content/base/economy/needs.jsonc`
```jsonc
{ "type": "needs_basket", "id": "peasant", "class": "Peasant",
  "staple_measures_per_person_day_x100": 80, "relish_measures_per_person_day_x100": 20,
  "ale_ml_per_person_day": 200, "salt_g_per_person_day": 8, "firewood_g_per_person_day": 1500,
  "clothes_lifetime_days": 1095, "shoes_lifetime_days": 365, "tools_lifetime_days": 900,
  "target_cover_days": { "staple": 20, "relish": 3, "ale": 3, "firewood": 20, "salt": 30 },
  "survive_below_days": 10, "buffer_below_days": 20 }
```
| Class | staple (measures) | relish | ale (ml) | firewood (g) | clothes life (days) |
|---|---|---|---|---|---|
| Peasant | 0.80 | 0.20 | 200 | 1,500 | 1,095 |
| Burgher | 0.72 | 0.28 | 300 | 1,500 | 730 |
| Gentry | 0.50 | 0.50 | 500 | 2,000 | 365 |
Children under 12 drink half the ale. **Everyone eats 1.0 measure-equivalent per day** (staple + relish) as plan §3a states;
per-age appetite is a possible later refinement (Decision: literal plan value, simple). Firewood is x2 in cold:
`fuel_mult_x100 = 100 + clamp((12 - T_mean) x 100 / 12, 0, 100)` with `T_mean` the day's mean temp
(`RegionWeather.DailyMeanTemp`): 0 C or colder -> x2.0, 6 C -> x1.5, 12 C+ -> x1.0 (plan: doubles in winter).
Staple goods: grain (any), flour, bread. Relish goods: fish, meat, dairy (cheese, milk, butter), vegetables, eggs, pulses,
preserved fish/meat. Each good has `food_measures_per_kg` (`items.jsonc`: grain 0.909, flour 0.95, bread 0.80, fish 0.45,
meat 0.69, cheese 0.96, vegetables 0.15, eggs 0.5, pulses 0.909).

## 3. Rules

### 3.1 Class
Recomputed at S6 each season: **Gentry** if head holds a fief or has occupation `Lord/Steward`, or (rare) assets >= 400 x
`wage_craftsman_mites_per_day`; **Burgher** if `OccupationInfo.IsTrade(head)` or assets >= 60 x craftsman wage; else
**Peasant**. (Plan §4A.2: class derived from occupation and property; mobility emerges.)

### 3.2 Daily needs (H4 slice, one pass per household per day)
Order inside the slice: `plan work -> eat -> drink -> heat -> wear -> nutrition -> update tier/cover caches -> post orders -> wage/transfers due`.

1. **Staple**: `need_m = round(sum over members of class staple)` (integer measure-hundredths). Draw from stock in order
   flour, bread, pulses, grain (excluding `SeedReserveG` until the emergency of 3.6). Eating `N` g of grain-equivalent from
   **grain** draws `N x 16 / 15` g when the settlement has a mill within `mill_reach_m` (4,000): the extra `N/15` is paid in
   kind to the miller's stock (reason `MillToll`, the plan's 1/16). Flour/bread bypass the toll.
2. **Relish**: draw relish goods ascending by perishability (fish, meat first, then dairy, vegetables, pulses, preserved).
   Any relish shortfall is replaced by staple grain at 1 measure per measure (substitution) so the person is fed, but it
   raises grain demand and sets `diet_poor` (feeds disease/unrest, population spec).
3. **Ale**: `ale_ml x (adults + 0.5 x children)`; from `ale` stock; no penalty in Phase 2 other than a
   `Grievance.Lord`-independent mood flag (stub for Phase 3).
4. **Fuel**: `firewood_g x fuel_mult x members`; shortage with `T_mean < 0` sets `Grievance.Cold` and the cold mortality
   multiplier (population spec 3.5).
5. **Wear**: each of `clothes` pieces owned fails with probability `1/clothes_lifetime_days` (stream 0x221, key household id,
   draw per piece); a failed piece is destroyed (`Worn`). `cookware` (1 per household, lifetime 1,825 days, from bronze goods, production spec) wears the same way. Tools: `tools_common` pieces (1 per 2 workers, min 1) fail with
   `1/tools_lifetime_days`; smiths/farmers with no tool work at 50% efficiency (production spec).
6. **Nutrition** (per member): `r = eaten_measures / need`;
   `if r >= 1000 permille: N = min(3000, N + 50)  else: N = max(0, N - 100 x (1 - r))` (centidays; full-day starvation drains
one day of reserve; refilling 60 days).
7. **Caches**: `FoodCoverDays = (staple+relish stock in measure-eq) / need_m_per_day`, `FuelCoverDays`, `CashCoverDays = account / avg daily expense`.

### 3.3 Mill toll
Millers (Phase 1 `LotUse.Mill`, occupation `Miller`) are businesses whose input is toll grain; toll grain sits in the
miller's household stock/stall and is sold or eaten. Absence of a mill within 4 km: grinding by hand quern costs
`quern_hours_per_person_day_x100 = 8` of a female adult's hours (they cannot work a job; production spec) and no toll.

### 3.4 Shopping (the buy-order pull model, H3 market session)
For each good `g` in the basket: `target_g = target_cover_days x daily_need_g`, `reorder_g = target_g / 2`. If `stock_g < reorder_g` the household posts `order(g, target_g - stock_g, max_ratio)` in its slice hour:
- `max_ratio` (max accepted `ask/Ref`): `Survive` tier 4.0 (any price); `Buffer` 2.0; `Profit` 1.5. Staples for a household
  with `FoodCoverDays < 3` ignore the ratio.
- Spending cap: `spend <= account - tax_reserve - 3 x daily wage expectation` for Buffer/Profit; Survive may spend
  everything. The order is filled by `MaxAffordable` (money spec 3.2), cheaper substitutes first when the ratio is exceeded
  (flour -> grain; fish -> pulses).
- Farmers sell surplus after harvest: `surplus_g = stock_g - keep_g`, `keep_g = max(target cover, next-sowing seed, 1.1 x remaining-year need until next harvest)`; offered to merchants (money spec 3.4). A farm household in `Survive` never sells
  grain below 30 days of cover.
Durables: missing clothes/tools (`owned < members` / `< workers/2`) post an order at the next session regardless of cover; a
household unable to afford wears fewer pieces (`Clothing` stat, Phase 3 mood).

### 3.5 Work allocation: survive, buffer, profit (H4 plan; H2 credit)
```
food = FoodCoverDays, fuel = FuelCoverDays, cash = CashCoverDays
Survive  if food < survive_below_days(10) or (fuel < 3 and T_mean < 0)
Buffer   if food < buffer_below_days(20) or fuel < 10 or cash < 15
Profit   otherwise
```
Each working member (age >= `MinWorkAge`, not pregnant in last 15 days, not levied, `hours = workday_hours` capped by
daylight, minus child-care for the youngest woman: 1 hour per child under 5 up to 3 h) is assigned in this order:
1. **Survive**: own farm task with the nearest deadline; else wage work paying >= 1 measure-eq per day-hour (market ref);
   else `Forage` (extraction spec seasonal windows); no job farther than `max_commute_m = 1,350` (1.5 game hours at the 5 m/s
   NPC commute pace, abstract spec 3.4) unless it is a field camp.
2. **Buffer**: current occupation at the current workplace (own business, employer, or parcel work) and farm tasks first;
   surplus hours to hired labour at the settlement's `LabourBoard`.
3. **Profit**: the highest `expected_daily_mites` among: own craft/farm output priced at current `Ref`, hired labour at
   posted wages (harvest premium), caravan guard/haul jobs (Phase 3), idle (mood, Phase 3). Expected value uses *last seen*
   prices with age (money spec).
Jobs: a person changes job only by being **hired** (a vacancy posted by a business with `OpenSlots`, filled at H5 in
ascending business id, candidates ascending person id, preferring matching skill then lowest current wage) or by **quitting**
(`QuitCounter` reaches 3 unpaid days, or wage < 0.8 x best offer for 15 consecutive days, or employer dissolved).
`Occupation`, `Employer`, `WorkSettlement`, `WorkLot` (Phase 1) are updated at hire/quit.

### 3.6 Hunger and the emergency
If `FoodCoverDays < 1` the household enters `Emergency` for the day: it may (in order) eat `SeedReserve`, sell tools/clothes?
(no, Phase 3), take **charity** from the parish granary (tithe stock, up to 0.5 measure per member per day, reason `Charity`;
parish gives to households with lowest cover first, stable by id), and set `Grievance.Hunger`. A household that eats seed
sets `SownPpm` lower next sowing (farming 3.5).

### 3.7 Housing
Capacity `Beds` (Phase 1 per dwelling: cottage 4-7, longhouse 6-10, burgage 6-12). `crowding = members x 100 / beds`
(percent). Effects: `crowding > 100` -> `Grievance.Crowding` after 15 days; death multiplier table
`overcrowd_death_mult_permille` { <=100:1000, <=125:1050, <=150:1100, >150:1200 } and disease x1.2 in towns (population
spec). Extra members (lodgers, grown children) occupy beds first-come by age; the surplus becomes `Homeless(NoRoom)` (Phase 1
enum) sleeping in barns/inns (no bed -> `crowding` for the host). Dwelling vacancy and marriage gating are in the population
spec.

### 3.8 Heads, splits, merges
Head = eldest adult male member, else eldest adult female (inheritance order in population spec). A household with no members
is **dissolved** at S6: stock and coin escheat/inherit (population spec 3.8), dwelling becomes vacant, parcels return to the
landlord.

## 4. Tick placement
- **H3**: shopping for households whose slice hour (`index % 24`) falls in the session (or next open hour); orders after the
  producers' sell step.
- **H4 daily slice** (hour `= index % 24`): 3.2 and 3.5 planning, wage receipts credited (employers push wages in their own
  slice; households only receive).
- **H5 hour 0**: hiring/quitting pass per settlement (3.5), class update only at S6.
- **S6**: class, tier statistics, dissolve empty households, grievance roll-up for unrest.
- Live tier: no household logic; a household member promoted to the bubble keeps its plan (`JobKind/JobRef`) as the abstract
  job state (LODBridge).

## 5. Determinism
Ascending household index; members ascending person index; ties via `0x220`. Stochastic rounding of grams (e.g., toll
remainder, spoilage) uses stream 0x221 with `(hourKey, householdId, draw = goodId)`. Wear draws `(hourKey, householdId, 0x221, 1000 + pieceIndex)`. Wear is a **daily flow** (plan §4A time compression): lifetimes are historical days (a peasant's
clothes last ~3 years = 1,095 game days), not scaled by 60/365.

## 6. Interactions
Farming (harvest stock, seed reserve, tithe), markets (orders, selling), production (wages, labour board), extraction (forage
jobs), population (nutrition -> death multipliers, housing -> marriage), taxes (hearth tax, arrears, grievance), items (goods
ids, spoilage), LODBridge (job state, inventory carried).

## 7. Edge cases
- Household with no workers (all children/old): lives on stores, charity, kin transfers (an adult kin household within 1 km
  sends 10% of surplus staple monthly: `kin_support_pct`), else starves.
- Head dies while holding the only account: account passes to the new head's household, same index (the household persists);
  coin is never split by RNG.
- A farm household whose crop failed with `FoodCoverDays < 10` and no money sells **tools last** (Phase 3), emigrates at S7
  (population spec) if `Survive` for 2 seasons.
- Zero-price goods are impossible (markets spec 8); orders exceeding the whole market stock are truncated to stock.
- Mills out of reach in winter (frozen river wheel): toll rule still applies (hand-quern penalty not applied) - Phase 2
  ignores mill seasonality (noted for Phase 5).


## Decisions made in this spec (collected in 00-overview.md section 10.4)
- **H1** Decision: Everyone eats 1.0 measure-equivalent per day (no age scaling). Reason: plan §3a literal; keeps the region food test simple.
- **H2** Decision: Peasant basket is 0.80 staple + 0.20 relish; relish can be replaced by grain 1:1. Reason: avoids a famine from one missing food (E14).
- **H3** Decision: Tiers: Survive below 10 days of food cover, Buffer below 20. Reason: plan §4A.1 survive, buffer, profit; thresholds tunable.
- **H4** Decision: Class is recomputed each season from occupation and assets. Reason: plan §4A.2: class derived, mobility emerges.
- **H5** Decision: Clothes/tools/shoes lifetimes are historical days (1,095 / 900 / 365 for peasants). Reason: wear is a daily flow (E13).
- **H6** Decision: Nutrition reserve drains 100 centidays per missed day and refills 50 per full day. Reason: plan: 30-day reserve; 60-day refill is a reasonable pace.
- **H7** Decision: Seed corn is eaten only in an emergency. Reason: historically true and keeps next year's sowing meaningful.

## 8. Golden tests
| id | Case | Expected |
|---|---|---|
| G-H1 | Peasant household: 2 adults + children aged 2, 7, 11 (5 persons); basket per day | staple 4.00 measures (4,400 g grain-eq), relish 1.00 measure-eq, ale `200 x (2 + 3 x 0.5)` = 700 ml, salt 40 g, firewood 7.5 kg at 12 C |
| G-H2 | Same household, fuel multiplier | 0 C: 15.0 kg; 6 C: 11.25 kg; 12 C: 7.5 kg; 18 C: 7.5 kg |
| G-H3 | Eating 4.0 staple measures (4,400 g grain-equivalent) | from grain with a mill in reach: draw `4,400 x 16/15` = 4,693 g (floor), toll 293 g to the miller; from flour (0.95 measure/kg): draw `ceil(4.0 / 0.95 kg)` = 4,211 g, no toll |
| G-H4 | Nutrition: start 3000, three days at r = 0.6, then full rations | 3000 -> 2960 -> 2920 -> 2880; then 2930 -> 2980 -> 3000 (cap); starving r = 0: day 30 reaches 0 |
| G-H5 | Order sizing: stock 12 measures (13,200 g), target 20 days x 4.0 m = 80 m (88,000 g) | order 74,800 g (reorder when below 40 m) |
| G-H6 | Buying power: purse 5,000 mites, lot of 50 kg below `s_lo` (flat 4x: mid 180, ask 194.4 mites/kg) | buys 25,720 g (`floor(5000/194.4 kg)`), pays 5,000 +-1 mites, 24,280 g left on the book |
| G-H7 | Wages: employer cash 400 mites, three employees owed 150 each (ids 3, 5, 9) | pays 150, 150, 100; employee 9 arrears 50; `QuitCounter` +1 for 9; zero dividends |
| G-H8 | Tier: food cover 8 d -> Survive; 15 d -> Buffer; 25 d, fuel 12 d, cash 20 d -> Profit; cover 25 d but fuel 2 d and T = -3 C -> Survive | as listed |
| G-H9 | Class: head Smith (trade) -> Burgher; head Labourer with assets 70 x 350 mites -> Burgher; Lord head -> Gentry; Labourer with 5 x 350 -> Peasant | as listed |
| G-H10 | Clothing wear: 1,000,000 piece-days at `p = 1/1,095` | pieces lost 913 +- 60 (seeded); household of 5 expects 0.00457 per day; region 22.8 pieces/day |
| G-H11 | Crowding: 7 members, 4 beds | 175% -> death multiplier 1200 permille and `Grievance.Crowding` after 15 days; 5 members, 4 beds (125%) -> 1050 |
| G-H12 | Seed emergency: farm household, cover 0.5 day, seed reserve 21,120 g | eats seed 4,400 g, `SownPpm` next sowing drops by `4,400/21,120` = 20.8% (208,333 ppm); chronicle `Famine` entry |
| G-H13 | Permutation: households processed in any input order within one hour | identical stocks, accounts, `NutritionCd` |

## 9. Soak invariants
1. In a normal year, `FoodCoverDays` of the median household >= 10 on every day after the first harvest; the 5th percentile
   never < 3 outside forced-drought years.
2. No household account is negative; no stock negative.
3. Share of households in `Survive` over a normal year <= 8% (annual mean), over a drought year <= 35%.
4. Worn-out clothes/tools destroyed per year within 10% of `pieces owned / lifetime_days x 60` summed over households (region
   ~1,370 clothes per game year).
5. Employment: >= 90% of working-age persons with an occupation have an `Employer`/`Own` job each day; vacancies < 5% of
   workstations.

## Notes

- Needs baskets live in `content/base/economy/needs.jsonc` per class; stores are seeded with a few weeks of staples, firewood, salt and ale at world creation (`EconomyState.Create`). Shortfall handling follows section 2; no deaths are produced in Phase 2.
- **Growth of the per-household and per-person arrays (population spec 2).** `EconomyState` arrays are exact-length from creation and double once births and weddings add rows (`AddPerson`, `AddHousehold`); `HouseholdCount` / `PersonCount` are the real counts (save and hash cut to them). The abstract tick's `HouseholdNeedsSystem` runs `HouseholdNeeds.RunDay` and one day of store spoilage for each household whose index mod 24 is the hour (H4, order `SystemOrder.HouseholdNeeds`, after the `Provisioning` slot where farming and markets put food in); no miller store is passed yet, so the 1/16 toll is not charged in the tick.

### Notes: Phase 2 integration

- **H7 Seed corn is not food.** The eating step has always kept `SeedReserveG`; the cover days, the shopping and the farm sales now count only the grain a household may eat (`FoodStockX100(seedReserve)`, the shopper's `Held`). Before, a holder with a barn of seed and nothing else showed 23 days of cover, bought nothing and starved with coin in the purse.
- **H8 Seed emergency (G-H12).** A household that has eaten everything else eats its seed corn rather than starve; the reserve falls by what it ate and the next sowing shrinks (`EconomyState.SeedEaten` is a running tally).
- **H9 Durables (soak invariant 4).** Each member wears out a pair of shoes (one in `shoes_lifetime_days`) and clothes, the household a tool, by a daily roll (stream 0x2C8); worn items are a count per household (`EconomyState.DueDurables`, saved) that the household's shopping buys (priority 6, the new item is consumed in service, reason Worn). The spec lifetimes are kept: they make craft goods about 5 percent of the baskets, which is why the Phase 1 artisan share (14 percent of the town households have a workshop) cannot live on domestic demand alone.
- **H10 Farm sales** (`farm-sales`, H4 slice before the needs): a household that holds land (or is gentry) and has more grain than its needs until the next threshing plus 12 days plus its seed sells a third of the surplus a day, merchants first, the rest as a lot at floor 0.8; wool, slaughtered animals and cheese above 1.5 kg a member sell at once. **Parish relief** (`parish`, H5): households under 3 days of food draw one measure a member a day from the parish granaries of their fief (their own village's first); the fief steward buys grain at the market of each settlement with hungry households with the treasury's coin (2 percent of it a day) and hands it out from the fief chest, lowest cover first; the treasury also gives 45 mites a member and day while it holds coin (famine relief, taxes spec 3.6). The parish sells tithe grain (a sixtieth of a granary a day) only in a fief where no household is hungry.
- **H11 Household production of relish** (resources spec X5): gardens and hens, created before the meal.

### Notes: lead's tuning list (decision I17)

1. **Livery: done.** A manor's staff receive a measure of grain a day from the lord's house store (`Livery`, `Store.MoveUpTo`, conserving) on top of the coin wage; manors already fill demesne jobs from the poorest idle first (production P10).
2. **Own grain first: done for shopping.** `FoodPlan` still eats flour, then bread, then grain, but a household holding grain for half its staple cover target no longer posts flour or bread orders, so growers do not buy bread while sitting on grain (the seed reserve is excluded, H7). Home brewing and the mill toll on grinding are not modelled (no miller store is passed to the needs yet).
3. **Safety nets: mostly.** Parish and fief relief (tithe grain, steward purchases, treasury dole, H10), gleaning (3 percent of every harvest into the poor box, I24), the Church's regional granaries and the lords' almonry in kind and in coin (I24, I25) and the village re-let of vacant holdings (I24) are done; kin support is not.
4. **Credit: not done.** Tally credit for food, recorded in the ledger and repaid from wages or the harvest, is not modelled; the alms and the poor box stand in for it.
5. **Migration balance: done as a stand-in.** Departed households come back to vacant dwellings while the region holds fewer people than it started with (I27); the continent tick will own the real inflow.
Effect (seed 1234, 10 years): hungry household-days over the run 21 percent -> 7 percent, population 17,700 -> 19,900 (22,100 excluding the 2,214 net emigrants); the 100-year table in the overview notes predates this change.
