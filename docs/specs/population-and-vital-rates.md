# Population and vital rates

Plan refs: §4A.2 (households, vital rates, the housing brake, famine, inheritance, test: 100 years within +-10%), §4A (time
compression: life events at historical per-year rates per game year), §3c (disease from weather), §5 Phase 2 (vital rates,
marriage gated on dwellings, famine). Replaces legacy growth `int(pop x 0.0001) + 2` and the starvation floor
(`ConsumptionSystem.gd:113-119`).

## 1. Purpose
Every one of the ~25,000 people is a record that ages, may marry, conceive, bear children, fall ill, starve and die at
historical per-game-year rates. Population is regulated by **dwellings** (marriage needs a home), **food** (fertility and
death multipliers) and **crowding**, not by a counter.

## 2. Data
New/extended columns (Phase 1 `PersonTable`: `Given, Family, Byname, Sex, BirthDay, Culture, Household, Father, Mother, Spouse, Occupation, Employer, WorkSettlement, WorkLot, Class, Attr, Skills, Traits, HomeSettlement, HomeLot, Bed, Homeless, Notable, Dynasty`):

| Column | Type | Meaning |
|---|---|---|
| `Status` | enum `Alive, Dead, Departed` | `Departed` = moved to another region's table (tombstone) |
| `Region` | byte | current region |
| `DeathDay` | int | game day of death, -1 alive |
| `DeathCause` | enum | `Age, Hunger, Disease, Cold, Violence, Other` (chronicle `CauseKind`) |
| `NutritionCd` | short | households spec |
| `PregnantUntilDay` | int | day the pregnancy ends, -1 none |
| `FatherOfUnborn` | int | person index of the father (the spouse) |
| `LastBirthDay` | int | for the postpartum gap |
| `MarriageWait` | byte | seasons spent eligible without a home |

`PersonTable.IndexOf(id)` can no longer be `base + serial`: it becomes `SerialIndex` (open-addressing map `serial -> index`);
dead/departed rows are kept (kin graph, legends) and compacted only by the yearly `PersonTable.Compact()` at save time when
tombstones exceed 40% (rewrites indices, never ids). Memory after 100 years: 25,000 + ~64,500 births = ~90k rows x ~300 B =
~27 MB (inside the 25 MB records-only ring-2 budget only for the *living* subset; tombstones older than 20 years move to
`Graveyard` (id, names, birth/death day, parents, cause) at 40 B each).
`Settlement` side: `VacantDwellings[settlement]` (list of lot indices whose `Lot.IsDwelling` and `LotHousehold == -1`,
ascending), `Pop`, `Households`, `Vacancy%`.

Content `content/base/economy/vital.jsonc`:
```jsonc
{ "type": "vital_rates", "id": "default",
  "death_pct_per_year": [ {"age_max":0,"pct":20}, {"age_max":4,"pct":3}, {"age_max":14,"pct":0.8},
                          {"age_max":49,"pct":1.2}, {"age_max":64,"pct":3}, {"age_max":200,"pct":10} ],
  "fertility_pct_per_year": 30, "fertility_age_min": 18, "fertility_age_max": 44, "pregnancy_days": 45, "postpartum_days": 15,
  "marry_age_min_female": 16, "marry_age_min_male": 18, "marry_chance_pct_per_season": 35,
  "nutrition_death_mult_permille": [ {"min_cd":2000,"m":1000}, {"min_cd":1000,"m":1500}, {"min_cd":500,"m":2000}, {"min_cd":0,"m":3000} ],
  "starvation_ppm_per_day": 5000, "town_disease_mult_permille": 1200, "cold_death_mult_permille": 1300 }
```
Percent per year are per **game year** (60 days); the loader rejects a rate whose key does not end `_pct_per_year`/`_per_day`/etc.

## 3. Rules

### 3.1 Death (daily, H4 slice of the person's household)
```
base_ppm      = pct x 10,000 / 60                       (floor)   age 0: 3,333; 1-4: 500; 5-14: 133; 15-49: 200; 50-64: 500; 65+: 1,666
mult_permille = nutrition_mult x crowding_mult x disease_mult x cold_mult x outbreak_mult / 1000^4   (cap 6,000)
p_ppm         = base_ppm x mult / 1000 + (N == 0 ? starvation_ppm x (age < 5 or age >= 60 ? 2 : 1) : 0)
dies          = DetRng.Chance(seed, hourKey, personId, 0x200, 0, p_ppm / 1e6)
```
- Literal plan rule (divide the yearly rate by 60): the effective yearly probability is slightly lower than the table (age 0:
  18.15% not 20%; 15-49: 1.19%).
- `nutrition_mult` from `NutritionCd` (households spec); `crowding_mult` from the household's `crowding` (households 3.7);
  `disease_mult` 1200 for towns (`SettlementClass.Town`); `cold_mult` 1300 when `FuelCoverDays == 0` and `T_mean < 0`;
  `outbreak_mult` from 3.6.
- On death: `Status = Dead`, remove from `MemberIdx` slice by swapping with the last member (`MemberCount--`), clear spouse
  link on the partner, queue inheritance (3.8), chronicle `Death` entry only for notable or violent/hunger deaths (cap 50
  entries/day region-wide; others counted in `RegionSummary`).

### 3.2 Conception and birth (daily, women)
Eligible: married (`Spouse >= 0`, spouse alive and in the same household), age 18..44, not pregnant, `day - LastBirthDay >= 15`.
```
p_ppm = fertility_pct x 10,000 / 60  = 5,000
        x fertility_food_permille / 1000       (N >= 2400: 1000; 1500..2399: 800; 500..1499: 500; < 500: 200)
        x (crowding > 150 ? 800 : 1000) / 1000
conceives = DetRng.Chance(seed, hourKey, womanId, 0x201, 0, p_ppm / 1e6)  ->  PregnantUntilDay = day + 45
```
Birth (the woman's slice on day `PregnantUntilDay`): create a Person: id from the region allocator (`IdKind.Person`, origin
region = the household's region), `Sex` = `Range(0,2)` (stream 0x201 draw 1), parents set, `Household` = mother's, `Class` =
household class, `Culture` = mother's, name from `NamePools.For(culture)` keyed `(seed, personId)`, `BirthDay = day`,
`NutritionCd = 3000`, attributes/skills from the Phase 1 child template (0..). Beds: child takes none (infant) until age 2.
Steady state: a well-fed married woman has a birth every 200 + 45 + 15 = 260 days = **0.231 births per game year** (plan's
"30% yearly fertility chance" realised as a daily conception chance).

### 3.3 Marriage gated on dwellings (seasonal S6, then weekly catch-up at H5 on doy%5 == 0)
Per settlement cluster (settlement plus those within 5 km road):
1. Eligible women: `Status Alive`, unmarried, age >= 16, age <= 40, not `Homeless Vagrant`; eligible men: unmarried, age >=
   18, age <= 50. Widows/widowers are eligible but remarriage does not need a new dwelling (3.3.4).
2. Each eligible woman is "proposed to" this season with probability `marry_chance_pct_per_season` (35%, stream 0x202, key
   woman id); proposed women are matched in ascending id to the unmarried man (same cluster) minimizing `|age diff - 3|` then
   `|class diff|` then id, excluding kin within the Phase 1 `KinGraph` degree 2 (siblings, parents, first cousins).
3. **Housing brake**: a couple needs a home: (a) a vacant dwelling from `VacantDwellings` of the cluster (first lot ascending
   whose `Beds >= 2`), or (b) the groom's father's/widowed parent's household has a **dwelling inheritance pending** (groom
   is the heir of a head who just died; dwelling passes to him), or (c) the bride is the household head. Otherwise the pair
   waits: `MarriageWait++` (cap 8 seasons, then individuals try the next cluster or emigrate at S7).
4. Remarriage: a widow(er) moving into the spouse's dwelling merges households (beds permitting) with no vacancy needed.
5. Effects: `Spouse` links set, new `Household` row created for (a)/(b) with head = groom, deed `Rented` (landlord = fief)
   unless the dwelling is Owned, `Occupation` unchanged; bride changes `Household`; dowry transfers 5% of the bride's
   parents' account (min 0) to the new household; chronicle entry for notables only.
Stability follows from the brake: a fixed number of dwellings caps households; births exceeding the cap queue as unmarried
adults (crowding), raise deaths and push emigrants.

### 3.4 Household formation and dissolution
New household rows only via 3.3 or migration arrival. A household with `MemberCount == 0` dissolves at S6 (stock/account
escheat 3.8). `Beds` come from the dwelling; the lot's `LotHousehold` is updated.

### 3.5 Famine
- Per person: nutrition reserve of 30 days (`NutritionCd = 3000`), drained by 100 centidays per fully missed day (households
  3.2). `Hunger` death multiplier by `NutritionCd`: >= 2000: x1.0, 1000-1999: x1.5, 500-999: x2.0, < 500: x3.0; at 0 add
  `starvation_ppm_per_day` (0.5%/day). The poor die first because they have no stores/coin (the model does not special-case
  them).
- **Famine event** (settlement level, daily H5): `famine = share of households with FoodCoverDays < 3 >= 15%` for 5 days ->
  chronicle `Famine` (cause: the highest-impact `CauseKind` found: weather loss share from farming entries within 120 days,
  else war/raid, else MarketDrift with supply/demand data). Famine flags the settlement for charity, migration push (3.7) and
  unrest (taxes spec).
- Fertility falls and miscarriages are modelled only by the 3.2 multiplier.

### 3.6 Disease
Outbreaks per settlement per day (stream 0x204, key settlement id, draw = day): `base_ppm_per_day` by season x class table
(JSON): winter coughs and fevers (town 4,000, village 1,500), summer flux in crowded camps/towns (town 2,500), none
otherwise. An outbreak lasts `Range(8, 20)` days and gives `outbreak_mult` 1500 (mild) / 2500 (severe, 10% of outbreaks),
only to persons with `NutritionCd < 2500` or age < 5 or >= 60 (others x1000). Averaged, disease adds ~12% to baseline deaths
(calibration test). Weather linkage: wet year +30% outbreak chance, cold winter (`T_mean < -5` for 5 days) +50%.

### 3.7 Migration (S7, seasonal)
- **Push**: a household in `Tier = Survive` for the last two seasons, or `crowding > 150` for a season, or a settlement in
  `famine`, or a member with `MarriageWait >= 4`; each rolls `emigrate_chance_pct = 40` (stream 0x203).
- **Pull** score per destination settlement `d` within 20 km (same or neighbouring detailed region): `score = 10 x vacancies(d) + 3 x wage_index(d) + 2 x food_cover_index(d) - distance_km - 5 x unrest(d)`; best score wins, ties lower id;
  destination needs >= 1 vacant dwelling (else the household becomes `Homeless Vagrant`, walks to the road, and tries again
  next season; vagrants over 2 seasons die at 1.5x or leave the region as `Migrants` with the continent tick).
- Migrants are **visible**: a `MigrantGroup` record (household id, route polyline, speed 1.0 m/s with cart 2.5, stock carried
  = what fits on a handcart 150 kg) walks the road edges like a caravan (caravans spec) and arrives after `length / speed`.
  On arrival: dwelling assigned, deeds updated, `Region` column updated (if crossing, record transfer at the barrier, 3.9).
- Immigration from outside the region comes from the continent tick (`inflow` per season) as MigrantGroups entering at a
  border gate.

### 3.8 Inheritance and escheat (on a head's death or dissolution)
1. If the household still has a living adult (age >= 16): the household persists; new head = eldest adult son, else eldest
   adult daughter, else the spouse, else eldest adult member. Dwelling, parcels, businesses, stock and the account stay with
   the household (no splitting).
2. If only minors remain: they go to the nearest adult kin household within the same cluster in `KinGraph` order
   (grandparent, aunt/uncle, sibling) together with the dwelling's stock and account (the guardian household absorbs them,
   beds permitting; the dwelling returns to the vacancy list). Parcels are re-let.
3. No kin: **escheat** to the lord: stock and account to the fief treasury (`Escheat`), dwelling vacant, parcels re-let,
   children wards of the parish (join the priest's household, else vagrants who migrate).
4. Gentry/notables: Phase 1 `Notable/Dynasty` heirs follow the same order; the title goes with the household head.

### 3.9 Region crossing
A person/household moving to another region is serialized (`PersonRecord`, household members, stocks, account) into the
destination region's `MigrationMailbox` at the barrier; origin rows become `Departed`; ids unchanged (origin region bits
stay); destination assigns a new row index. Order: mailboxes are applied in ascending (source region id, household id).

## 4. Tick placement
H4 slice: death rolls (everyone), conception/birth (women), nutrition effects. H5 (hour 0): famine/outbreak checks, weekly
marriage catch-up. H6 S6: marriage matching, class, dissolution; S7: migration decisions (before caravans so migrants appear
on roads the same day). L2 regions run the same per-day kernels once a day; L3/L4 use summary rates (`births = pop x 2.6%`,
`deaths` by the stationary table scaled by the region's food index).

## 5. Determinism
All rolls are `DetRng` with keys above; iteration ascending person/household; births allocate serials in (hour, household
index, woman index) order; matching uses ascending ids; migration scoring ties lowest id. Dead people stay in tables so
ids/serials never reuse.

## 6. Interactions
Households (nutrition, crowding, deaths remove members), farming/production (workers), taxes (hearth tax per household;
unrest), markets (demand scales with pop), continent tick (inflow/outflow, summary pop), caravans (migrant groups share
movement code), LODBridge (promoted people keep status).

## 7. Edge cases
- Last member of a region dies: region summary pop 0; settlement flagged `Abandoned` (becomes a ruin source in later phases).
- Mother dies in pregnancy: pregnancy ends silently (no stillbirth record; counted in summary).
- Spouse in another region: marriage across the border allowed only after both records are in the same region (migration
  first); not modelled in Phase 2.
- Age caps: persons reaching age 110 die (never occurs in practice).
- Child ages: `AgeYears` = `(nowDay - BirthDay) / 60` (Phase 1).
- Same-day birth and death of mother: birth happens first (slice order: births before deaths).


## Decisions made in this spec (collected in 00-overview.md section 10.4)
- **P1** Decision: Daily hazard is the yearly rate divided by 60 (literal plan rule). Reason: effective yearly mortality is slightly below the table (18.15% for infants).
- **P2** Decision: No separate childbed mortality. Reason: covered by the 15-49 rate; avoids double counting.
- **P3** Decision: Marriage matching runs each season with a 35% proposal chance and needs a home. Reason: the plan's dwelling brake with a staggered rate.
- **P4** Decision: Orphans go to kin, else the parish; no kin means escheat. Reason: plan: eldest son -> children -> spouse -> lord.
- **P5** Decision: Dead persons are kept (tombstones) and moved to a graveyard table after 20 years. Reason: kin graph and legends need them; memory stays flat.
- **P6** Decision: Outbreaks are calibrated to add ~12% to baseline deaths. Reason: plan: town disease x1.2, winter coughs, summer flux.
- **P7** Decision: Pushed households emigrate with 40% chance per season. Reason: migration must be visible and not instantaneous.

## 8. Golden tests
| id | Case | Expected |
|---|---|---|
| G-P1 | Daily death ppm from the table | age 0: 3,333; 1-4: 500; 5-14: 133; 15-49: 200; 50-64: 500; 65+: 1,666 |
| G-P2 | Adult age 30, `N = 800` (x2.0), crowding 140% (x1.10), in a town (x1.2) | `200 x 2.0 x 1.1 x 1.2` = 528 ppm/day |
| G-P3 | Effective yearly mortality with the literal rule | age 0: `1 - (1 - 0.2/60)^60` = 18.15%; 15-49: 1.19%; 65+: 9.52% |
| G-P4 | Stationary life table from G-P3 rates (max age 110) | e0 = 38.7 years; shares 0-14 / 15-49 / 50+ = 28.8% / 49.8% / 21.5%; births to hold 25,000 steady = 645.8 per year (2.58%) |
| G-P5 | Births needed per woman 18-44 | women 18-44 are 19.4% of the stationary population -> 0.133 births per woman per year; with 0.231 births per year per married well-fed woman, **57.7% of women 18-44 must be married** at equilibrium |
| G-P6 | Conception 5,000 ppm/day; food `N` < 500 | 1,000 ppm (x0.2); expected wait 200 days vs 1,000 days |
| G-P7 | Pregnancy conceived on day 100 | birth on day 145 during the woman's slice hour; `LastBirthDay = 145`; eligible again on day 160 |
| G-P8 | Marriage gating: 10 eligible matched couples in a cluster with 3 vacant dwellings | exactly 3 weddings (lowest man ids / smallest age gap first); 7 couples `MarriageWait = 1`; vacancies 0 afterwards; Households +3 |
| G-P9 | Inheritance: head (45) dies; widow 40, sons 19 and 12, daughter 16 | household persists, new head = son 19, deed unchanged; only minors (sons 12, 9, daughter 7) with a living grandfather in cluster -> children and account go to the grandfather's household |
| G-P10 | Starvation: 1,000 adults aged 30, `N = 0`, `r = 0` for 30 days | per-day p = `200 x 3.0 + 5,000` = 5,600 ppm; expected deaths `1,000 x (1 - (1 - 0.0056)^30)` = 155 +- 25 (seeded) |
| G-P11 | Migration scoring: A: 3 vacancies, wage 1.0, food 1.0, 4 km, unrest 0 -> 30 + 3 + 2 - 4 = 31; B: 0 vacancies | A chosen; B not eligible; tie between two equal scores picks the lower id |
| G-P12 | Reference region year 1 (Phase 1 start pyramid 36/48/16) | births 600-780, deaths 560-760, net change within +-2% |
| G-P13 | Id determinism | births processed in different slice-internal orders produce the same ids because order is fixed by (hour, household index, woman index) |

## 9. Soak invariants
1. **100 years, no player: population at every year end within 25,000 +-10%** (22,500-27,500); 5-year moving average within +-7%.
2. Households count within +-10% of 5,200; vacancies never > 6% of dwellings and never < 0.
3. Age shares at year 100: 0-14 in 26-33%, 50+ in 17-26%.
4. Births - deaths + net migration = population change exactly (audit), per region, per year.
5. Married share of women 18-44 stays in 45-70%; mean marriage age (women) 17-24.
6. Famine only in years with a recorded weather/war cause (every `Famine` entry has a non-`MarketDrift` cause in >= 90% of cases).
7. Drought-year test: two drought years in five reduce population by 3-12% (not extinction) and it recovers within 25 years.

## Notes

Implementation notes (`src/Ravensguard.Sim/People/Vital/`, `Save/AbstractSave.cs`; tests in `tests/Ravensguard.Sim.Tests/Population` and `/Abstract`). Decisions numbered V1.. continue the P-series above.

- **V1 Rolls are ppm compares, causes are drawn from the hazard.** Q16.16 resolves 15 ppm, too coarse for a 133 ppm child, so a roll is `NextUInt < ppm x 2^32 / 1e6` (`VitalMath.Roll`). `VitalMath.Hazard` returns the daily chance split by cause (the base is Age from 50 and Other below; each factor is credited what it added on top of the ones before it: nutrition Hunger, crowding Other, town disease and outbreak Disease, cold Cold; the starvation ppm is Hunger); a second draw (stream 0x200, draw 1) picks the named cause in proportion. G-P1, G-P2 (528 ppm), G-P3, G-P6 are exact; G-P4 gives e0 38.2 / shares 28.7-50.1-21.2 / 654 births (spec 38.7 / 28.8-49.8-21.5 / 645.8: same rates, the test uses the mid-year convention), G-P5 gives 58.1 percent (spec 57.7).
- **V2 A marriage cluster is the fief** (the settlements of one `Fief`; settlements with no fief form one more), standing in for "within 5 km by road" until the road graph is used here.
- **V3 `ChildIndex` next to `KinGraph`.** `KinGraph` is immutable and costs a rebuild per birth batch. Marriage (4 rounds a year plus the weekly catch-up) rebuilds it lazily and uses it for the degree-2 exclusion (parents, siblings, children, first cousins); guardianship and inheritance, which happen on any day, use `ChildIndex`, a children-by-person map kept current by each birth.
- **V4 A new household gets 10 percent of each good from both families** (`SettleInGoodsPct`) and the 5 percent coin dowry, because without markets a household with an empty store starves. Stock moves with `Store.Move`, coin with `Ledger.Transfer` (reason Gift), so both audits stay exact.
- **V5 Housing for a couple, in this order:** (a) the groom heads a household with a free bed and the bride moves in; (b) the bride heads one and the groom moves in; (c) both are members of other households: a vacant dwelling of the cluster (a new household, deed Rented, head the groom); (d) no vacancy: they live with the groom's, else the bride's family if that household has a free bed and no fertile married couple yet (the stem family, `StemJoinMaxOccupancyPct` 100). A lone head's household is absorbed into the one the person joins (coin, stock, parcels, dwelling returned). Spec 3.3 (b), "dwelling inheritance pending", is covered by (a): the heir who has just become head can marry into his own household. The brake alone (no (d)) settles the region at 17,900 people (-31 percent, 51 percent of the beds); with (d) and a stem rule of 80-90 percent it drifts to 28-31k; at 100 percent with the crowding emigration below it holds 24.2-25.8k (start 25,963).
- **V6 Emigration is the safety valve (S7, reduced).** Each season a household over 150 percent of its beds, and each unmarried adult who has waited 4 seasons, leaves the region with 40 percent chance (stream 0x203). A household takes its coin and stock to the continent purse and chest (owner `Demography.ContinentOwner`), a single leaves with nothing, rows become `Departed`. There is no pull score, no vagrant walk and no immigration yet (G-P11 not implemented): the continent tick owns the inflow. The audit is exact: `start + births - deaths - emigrants + immigrants = alive`.
- **V7 Pregnancies of a new world.** Generation makes people, not pregnancies, so the first takeover (`SimWorld.From`, `VitalSeeding`) makes each married woman 18-44 pregnant with probability 45/260, due in the next 45 days. Without it year 1 has 190 births instead of 581 (G-P12 band 600-780, ours 581 plus the later ones of the year).
- **V8 Succession details.** Head: eldest adult son, eldest adult daughter, spouse, eldest adult member (children of the dead head only for the first two). Only minors left: kin households of the same fief in the order grandparent, aunt or uncle, adult sibling, first with beds, else the parish (priest's household of the fief, else the lord's, never the household itself); assets inherit or escheat to the fief's treasury purse and chest; no taker at all: the eldest child heads and the children are marked Vagrant. An emptied household passes to the eldest living child (sons first) in another household, else the surviving spouse, else the lord. Parcels follow the assets; the dwelling becomes a vacancy.
- **V9 Chronicle policy.** By default: notables' deaths, births and weddings; Hunger, Cold, Violence and Disease deaths up to 50 a day; outbreaks, famines, escheats and parish wards; `VitalRates.ChronicleEveryEvent` writes everything (tests). Every death entry names its cause (`Data["cause"]`, `Cause` kind Death, Famine, Disease, Weather, Wound); a hunger death points at the settlement's open Famine entry, a disease death at the open outbreak. A Famine entry's cause is the most frequent Weather, War or Raid cause among the entries of the last 120 days, else MarketDrift with the hungry household count; the 1-year soak has cause coverage 1.0.
- **V10 Outbreaks** follow 3.6 (rates, 8-20 days, 10 percent severe, only the under-fed, the under 5 and the 60+ are hit) but are not calibrated to "+12 percent deaths" and have no weather linkage yet.
- **V11 Tables.** `PersonTable` gained `Status, DeathDay, DeathCause, PregnantUntilDay, FatherOfUnborn, LastBirthDay, MarriageWait` (`Region` and `JobKind...` not yet); dead and departed rows are kept, ids are `BaseCounter + index` and the allocator is kept in step (`IdAllocator.EnsureNext`). `HouseholdTable` can add and remove members of an existing household (`AddMemberLater`, `RemoveMember` swap-with-last, `CompactMembers` when holes exceed half). No graveyard compaction yet. `Population.ComputeHash` is unchanged (generation hashes stay pinned); `ComputeSimHash` covers the new columns.
- **Soak (seed 1234, harvest stand-in, no player):** 100 game years: start 25,963, minimum 24,194 (-6.8 percent), maximum 25,789, end 25,150; births 89,600, deaths 69,800, marriages 24,500, emigrants 20,600; households 5,140 of 5,153; age shares at the end 37.8 percent under 15 and 15.9 percent over 50 (spec band 26-33 and 17-26: the young structure comes from emigration of the surplus; to be revisited with real migration). One year: births 581, deaths 616 (G-P12). A dry year (no food for 60 days) kills 2,374 (1,956 of hunger; a normal year about 650), 374 settlement famines are recorded, and six years later the population is 22,900 of 25,100 (recovering, not extinct).

### Notes: Phase 2 integration

- **V12 Chevage** (decision I11): a household that emigrates pays `ChevagePct` (50) of its coin to its fief's treasury before it leaves with the rest, so a region people leave does not lose its money for good (the coin audit was always exact; the money simply no longer circulated).
- **V13 Under-15 share and the age structure.** The young share of the integrated soak is the generation's 35.4 percent at day 1 and then follows the births and the hunger deaths of the run (see the soak table: `u15%`); the spec band 26-33 is only reached when the population stops growing younger, which needs the economy to feed it (below). The vital rates were not touched: calibrating fertility to hit the band with a population that starves would only hide the economy's failure in the age pyramid.
- **V14 Emigration is not the answer to hunger** (not done): destitute households do not leave; they are relieved (households H10) or die; the continent tick's pull score is still open (G-P11).
