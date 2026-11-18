# Farming: fields, rotation, harvest and weather

Plan refs: §3a (full), §3c (weather effects on farming), §1 (measure, 48 per acre), §4A.1 (time compression). Replaces the
legacy daily "acreage pool" (`ProductionSystem.gd` farming, `GDSettlement.initialize_acres`).

## 1. Purpose
Every field is a real record with fertility, crop and state. A harvest is an annual event whose size comes from soil, weather
and timely labour, and whose grain is split between seed, tithe, rent and the household. The same per-field maths runs in
every region tier.

## 2. Data

### 2.1 `FieldTable` (replaces `Region.Fields`, a `Field[]` struct array from Phase 1)
Struct of arrays, index = Phase 1 field index (stable). Phase 1 `Field { Id, Tile, Parcel, Fertility, Slot, Crop }` is kept;
Phase 2 adds:

| Column | Type | Meaning |
|---|---|---|
| `Region` | byte | current region (id region bits stay origin) |
| `Fertility` | byte 0..100 | soil fertility now |
| `Slot` | byte 0..2 | **per field** rotation slot this year: 0 winter grain (harvested this year, sown last autumn), 1 spring grain (sown and harvested this year), 2 fallow (grazed this year; sown to winter grain in autumn) |
| `Crop` | `Crop` | `Fallow, Wheat, Rye, Barley, Oats, Peas, Beans` (Phase 1 enum) |
| `Stage` | `FieldStage` | `Stubble, Ploughed, Sown, Growing, Ripe, Cut, Threshed` (display + task gating) |
| `SownDay` | short | day of year sown (for frost) |
| `SeedG` | int | grams of seed sown on this acre (for the net-of-seed split) |
| `YieldG` | int | gross grams fixed at ripening (0 until `Ripe`) |
| `DamagePermille` | short | hail/trampling/fire accumulated (0..1000) |
| `Flags` | byte | `Resown`, `Burned`, `Grazed` |

Rotation per field is the plan; Phase 1's `Parcel.Slot` only seeds the initial field slots (conflict C10). Invariant: in each
settlement the number of fields per slot is within +-10% of a third.

### 2.2 `ParcelWork` (per Phase 1 `Parcel`, ~9,000 per region)
`Holder` (household index, -1 if none), `Landlord` (account ref: fief treasury, parish, household), `OwnerKind` (Phase 1:
`Demesne, Glebe, Tenant, Burgher, Freeholder`), `Lease { Kind: Share|Coin|None, ShareBp (2000..3300, default 2500), CoinMitesPerFieldYear (default 320) }`, task progress `int[ ] ProgressPpm` for each `FarmTask`, `Acres` (field count),
`HiredHands` (people ids list, <= 8), `WeededPpm`.

### 2.3 `CellCropClimate[256]` (per continental cell of the region; ring buffer of 2 years)
`RainWindowMm[kind]`, `NormalRainMm[kind]` (precomputed at region generation by sampling 20 reference years of
`RegionWeather.DailyRainMm`), `FrostKillDay` (doy or 0), `WinterkillPermille`, `ColdSummer`, `BlightYear`, `StormDays` list
(doy). Filled by daily calls in H0/H5 of day 1..60; consumed when yields are fixed.

### 2.4 Content: `content/base/economy/crops.jsonc`
```jsonc
{ "type": "crop", "id": "wheat", "kind": "winter_grain", "base_yield_measures": 48, "seed_pct": 20,
  "fertility_delta": -10, "sow_doy": [36, 45], "ripe_doy": 26, "reap_grace_days": 10, "min_fertility": 40,
  "food_measures_per_kg": 0.909 }
```
| Crop | Kind | Base yield (measures/acre) | Seed % of base | Fertility change per harvest | Sow doy | Ripe doy |
|---|---|---|---|---|---|---|
| wheat | winter | 48 | 20 | -10 | 36-45 | 26 |
| rye | winter | 46 | 20 | -8 | 36-45 | 26 |
| barley | spring | 48 | 20 | -6 | 3-14 | 31 |
| oats | spring | 46 | 20 | -6 | 3-14 | 31 |
| peas | spring legume | 42 | 20 | +5 | 3-14 | 31 |
| beans | spring legume | 42 | 20 | +5 | 3-14 | 31 |
Defaults: winter slot 60% wheat / 40% rye (rye if fertility < 55 or `min_fertility` fails for wheat); spring slot 40% barley
/ 27% oats / 20% peas / 13% beans (`spring_mix_pct`, chosen per field by `hash(field id, year)` so it is stable, legumes
forced when fertility < 45). The 48-measure base is the plan's value for average soil (fertility 70).

`content/base/economy/farming.jsonc` (all rates with unit suffixes): task rates, windows, weather parameters, spoilage,
tithe, rent defaults, hail/blight/frost keys, `oxen_per_100_arable_acres` (3), `grazing_sheep_per_fallow_acre_x100` (40).

## 3. Rules

### 3.1 Yield (all integer, grams; `base_g = base_yield_measures x 1100`)
```
fert_used   = field.Fertility at sowing          (clamped 0..100)
gross0_g    = base_g x min(fert_used, 91) / 70   (integer floor; 91 = 70 x 1.3 cap)
gross_g     = gross0_g x wx_permille / 1000 x var_permille / 1000 x (1000 - DamagePermille) / 1000 x late_permille / 1000
seed_g      = base_g x seed_pct / 100            (fixed per sown acre, independent of the actual yield)
net_g       = max(gross_g - seed_g, 0)
```
- `var_permille = 960 + Range(0, 81)` (stream 0x211, key field id): +-4% field-to-field noise, mean 1000.
- `wx_permille` = product (permille each, floor) of the weather factors in 3.3.
- `late_permille` = 1000 - `overdue_loss` (3.5) and the wet-harvest factor on the day cut (3.5).
- If `fert_used < min_fertility` the crop is not planted (rye substituted, else fallow carried).
- Fertility 70 wheat: 48 measures gross, 9.6 seed, **38.4 net** (plan §3a).

### 3.2 Calendar and tasks (day of year `doy` 1..60; season = `(doy-1)/15`)
Rates are acres per worker-day (10 worker-hours), already scaled x365/60 (plan §3a). Each task has a window; progress is per
parcel in ppm.

| Task | Window (doy) | Acres/worker-day | Needs | Notes |
|---|---|---|---|---|
| `plough_spring` | 1-12 | 6 | team (1 ploughman + 2 oxen) | spring-grain fields (slot 1) |
| `sow_spring` | 3-14 | 8 | seed | stage `Sown`; seed destroyed (`Sown`) |
| `weed` | 16-25 | 2 | | each unweeded ppm costs yield (3.3) |
| `reap` | ripe..ripe+grace | 2 | | winter grain from 26, spring grain from 31; window of the plan: doy 26-35 |
| `bind_cart` | with reap | 4 | cart | sheaves into barns (stage `Cut`) |
| `thresh` | 36-60 and into next year | 2 | | grain from sheaves; `Threshed` |
| `plough_fallow` | 16-30 (second ploughing) then 31-40 | 6 | team | slot 2 fields before winter sowing |
| `sow_winter` | 36-45 | 8 | seed | slot 2 fields get wheat/rye |
| `hay` | 16-25 | 2 | | meadow tiles (resources spec) |
Labour per sown acre-year = 1/6 + 1/8 + 1/2 + 1/2 + 1/4 + 1/2 = 2.04 worker-days (+ fallow ploughing 0.17/3 per field) ~=
2.1; region ~= 87,000 worker-days a year, about 1,450 full-time workers of ~4,000 farm-capable adults: the harvest window
(reap + bind need ~3,100 worker-days a day for 10 days) is the real peak. Oxen: `oxen_per_100_arable_acres = 3` gives ~1,900
oxen, 950 teams; a team ploughs 6 acres/worker-day so spring needs ~350 team-days in 10 days, available 9,500. Without teams
(e.g. after a murrain) ploughing falls to 1 acre/worker-day by spade.

**Labour assignment** (H4 daily plan, H2 hourly credit): every household with `ParcelWork.Holder == h` plans each day: its
workers (age >= 12, hours = `workday_hours` minus daylight cap) go to the open task with the earliest deadline on its own
parcels; leftover hours go to the settlement **labour pool** which feeds (a) demesne/glebe parcels, (b) hired hands for the
highest-wage bidders (wage per day `wage_labourer_mites_per_day` x harvest premium `harvest_wage_pct` = 150 during doy
26-35). Credit per worker-hour on a parcel: `ppm += rate_acres_per_day x 1,000,000 / (workday_hours x Acres)` (integer,
floor, remainder carried in `ParcelWork`). Fields complete in index order: with parcel progress `p`, the first `floor(p x n / 1e6)` fields are done (visible progress in the live bubble).

### 3.3 Weather factors (`wx_permille`; uses Phase 1 `RegionWeather`)
Windows: winter grain growth `doy 1-25`; spring grain `doy 6-30`.
- **Rain/drought**: `ratio = RainWindowMm / NormalRainMm`; `rain_f = 400 + 600 x min(ratio, 1)` (permille); if `ratio > 1.4`:
  `rain_f = 1000 - 400 x (ratio - 1.4) / 0.6`, floor 600. A `LongKind.Drought` year (rain 22%) gives ratio ~0.3 -> **580**.
- **Cold summer** (`LongKind.ColdSummer` covers the window): spring grain x 850, winter grain x 900.
- **Late frost** (spring sowing): on each day `doy 3..20`, if `At(cell, hourOf(doy,5)).TempC < -2.0` then every spring field
  with `SownDay >= doy - 12` and `Stage >= Sown` is killed (`YieldG = 0`). If the kill day `<= 12` the holder resows
  (`Flags.Resown`, new seed from stores, resown yield x 750); otherwise the field is lost for the year (chronicle `Weather`
  cause "late frost on day N").
- **Winterkill**: winter grain in a cell with >= 3 days `TempC < -10` and `SnowDepth < 5 cm` loses 400 permille.
- **Hail**: per thunderstorm day (`WeatherState.Thunderstorm`) in `doy 16-30`, each parcel in the cell is hit with
  probability `hail_chance_permille_per_storm_day = 5` (stream 0x210, key parcel id); hit fields get `DamagePermille += 500`.
- **Blight**: yearly roll per (culture, year) at 30 permille (x3 in a `WetYear`): all fields of that culture-region x 750.
- **Weeding**: `x (1000 - 250 x (1e6 - WeededPpm)/1e6)`.
- **Armies/raiders** (later phases): `DamagePermille` is the common damage channel (trampling = +300 per day on a field;
  arson sets `Flags.Burned`, `YieldG = 0`).
Every factor below 1000 that costs the region more than 2% of its yield writes one chronicle entry with `CauseKind.Weather`
(drought, frost, hail, wet harvest, blight) and the parcels/acres affected, so "why is bread dear?" has an answer.

### 3.4 Fixing the yield and fertility
At the first day of the field's `ripe_doy` (H5, doy 26 / 31) the field becomes `Ripe`, `YieldG` is fixed with 3.1 and `SeedG`
recorded. At reaping completion the **harvest split** runs:

```
net_g   = max(YieldG - SeedG, 0)
tithe_g = net_g x tithe_permille / 1000                  (tithe_permille = 100; Glebe parcels exempt)
rent_g  = net_g x ShareBp / 10000                         (Tenant parcels with a Share lease; Coin leases pay coin at S4)
holder  = YieldG - tithe_g - rent_g                       (holder's store; seed was already paid out at sowing)
```
Flows: `tithe_g` -> parish stock (`Tithe`), `rent_g` -> landlord stock (`Rent`), `holder` -> holder's sheaf pile (barn).
Demesne: everything to the lord's household stock after the tithe; the hired hands are paid wages in coin. Seed is consumed
at sowing from the holder's `seed_reserve`; the harvest "creates" `YieldG` (faucet reason `Harvest`). Freeholder: no rent.
Burgher parcels: holder is the burgher household.
Net effect for a tenant at fertility 70: -9.6 at sowing, +48 at harvest, -3.84 tithe, -9.6 rent = **+24.96 measures**.

Fertility updates at harvest: `Fertility = clamp(Fertility + crop.fertility_delta, 0, 100)`. At the autumn sowing of slot-2
fields (the fallow year ends): `+15` if grazed (settlement sheep >= 0.4 per fallow acre, 3.6) else `+5`. Year boundary (doy
60 -> 1, step S1): `Slot = (Slot + 1) mod 3`; fields entering slot 0 already carry winter grain sown last autumn.

### 3.5 Lateness, wet harvest, storage
- **Overdue**: from `ripe_doy + reap_grace_days` (10) every uncut ripe field loses `overdue_loss_permille_per_day = 40` of
  its remaining yield per day (H5, floor each day).
- **Wet harvest**: on a day a parcel's workers cut grain, the day's cut fields are multiplied by 920 if the cell has
  `DailyRainMm >= 6`, 850 if `>= 12` (grain rots in the sheaf).
- **Storage**: sheaf piles and threshed grain are plain stock in the holder's store; spoilage is rate based per day in ppm:
  house/barn `spoil_house_ppm_per_day = 3414` (5% per season), granary `670` (1% per season) (items spec 3.6). The
  lord/parish/merchants own granaries (Phase 1 `LotUse.Granary`); farmers sell surplus to them (money spec 3.4). Threshing
  converts sheaves to grain 1:1 by mass and creates `straw` 800 g per kg grain (thatch, fodder).
- **Mill toll**: where a mill serves the household (<= 4 km road), grain drawn for food pays 1/16 in kind to the miller
  (households spec 3.3); transfer, not loss.
- **Seed reserve**: a holder with fields to sow next season keeps `seed_reserve_g` (sum of `seed_g` for its next sowing)
  outside the eating algorithm until `nutrition emergency` (population spec 3.6); eating seed corn reduces next year's sown
  fraction (`SownPpm` on the parcel, fields beyond it stay fallow) and chronicles a `Famine` cause.

### 3.6 Livestock coupling (details in resources spec)
- `grazed = (sheep head in the settlement) >= 0.4 x (fallow acres in the settlement)` evaluated at S2 of summer; sets
  `Flags.Grazed` for the year; ungrazed fallow gets +5 not +15.
- Oxen limit ploughing (3.2). Fodder: hay/straw per head per winter day; shortfall culls herds late autumn (resources spec).

## 4. Tick placement
| Step | Where |
|---|---|
| Weather accumulation per cell (rain window, frost day, storms) | H0 each hour (hourly cell sample) and H5 daily (daily totals) |
| Daily plan: assign workers to tasks | H4 slice of each holder household |
| Hourly credit of worker-hours to `ParcelWork` progress | H2 (hours 06-16, inside `workday_hours`) |
| Frost kill, hail, overdue loss, ripening, wet harvest | H5 (hour 0, daily) |
| Rotation, fertility ledger, blight roll, window opening/closing, tithe/coin rent due | H6 seasonal S1-S4 (year boundary at doy 1) |
| Region food summary (stocks, days of food) | H6 S9 |
L2 (daily) regions run the same kernels at day granularity; L3/L4 use `RegionSummary.GrainNet` from a closed form
(continent-tick spec). **Order inside a day**: weather accumulation -> frost/hail -> ripening/overdue -> labour credit
already applied hourly -> harvest splits for completed parcels (in ascending parcel id).

## 5. Determinism
Ascending field/parcel index everywhere. RNG: yield variance `(seed, hourKey, fieldId, 0x211, 0)`, hail `(…, parcelId, 0x210, doy)`, blight `(…, cultureId<<16|year, 0x210, 99)`, crop mix `(…, fieldId, 0x211, year)`. `hourKey` = game hour of the step.
Weather inputs are pure functions of seed and time, so the same field in the same year always gets the same yield regardless
of tier or visit order, provided its labour history matches (labour timeliness is the only coupling).

## 6. Interactions
Weather (Phase 1) -> yields; households eat the holder's stock; taxes collect tithe/rent/coin rent; markets receive surplus;
population supplies workers; resources provide oxen, sheep, meadows; production (miller, maltster, brewer, thatch); LODBridge
shows ploughed/sown/stubble/sheaf states by `Stage` and `ParcelProgress`; chronicle receives weather-cause entries.

## 7. Edge cases
- Holder dies/leaves: parcel passes with the household (population spec); empty parcels are re-let at S4 by the lord (rent
  share default); unworked fields stay `Fallow`-like (`SownPpm = 0`) and weeds do not kill fertility.
- Seed shortage: sow what the seed allows (`SownPpm`); the lord lends seed to tenants at S1/S3 (seed loan = a tithe-barn
  stock transfer repaid in kind next harvest +20%).
- Field both hit by frost and hail: factors multiply; `YieldG` never negative; `net_g` floors at 0 (the seed is then lost).
- Fertility floors: `Fertility < min_fertility(crop)`: winter slot switches wheat -> rye; below 28 fields revert to pasture
  (Phase 1 generation floor 28).
- Parcel with no holder at harvest: crop is lost at `overdue` rate; chronicle "fields unharvested" with `Famine` risk flag.


## Decisions made in this spec (collected in 00-overview.md section 10.4)
- **F1** Decision: Seed is fixed at 20% of the base yield per sown acre; tithe and rent are taken on the yield net of seed. Reason: reproduces plan example 48 -> 38.4 net -> 24.96 for the tenant (conflict C7).
- **F2** Decision: Ungrazed fallow gives +5 fertility (grazed +15). Reason: plan gives only the grazed value; some recovery without sheep is needed.
- **F3** Decision: Per-field yield noise is +-4%. Reason: fields are not clones; mean stays 1.0.
- **F4** Decision: Rain factor is `400 + 600 x min(ratio, 1)` permille, with a waterlogging penalty above ratio 1.4. Reason: a 22% rain drought gives ~0.58 and a wet year costs little until the harvest rots.
- **F5** Decision: Crop mixes: winter 60% wheat / 40% rye, spring 40% barley / 27% oats / 20% peas / 13% beans. Reason: gives the 1-in-3 legume rotation of plan §3a.
- **F6** Decision: Winter grain ripens doy 26, spring grain doy 31; the harvest window is doy 26-35. Reason: plan: last 5 days of summer plus first 5 of autumn.
- **F7** Decision: A late frost kills spring fields sown in the last 12 days; resow (x0.75) only before doy 12. Reason: plan: late frost kills spring sowing.
- **F8** Decision: Uncut ripe fields lose 4% per day after a 10-day grace. Reason: labour shortage must cost something visible.

## 8. Golden tests (grams; 1 measure = 1,100 g)
| id | Case | Expected |
|---|---|---|
| G-F1 | Wheat, fertility 70, normal weather, no noise | gross 52,800 g (48 m); seed 10,560 (9.6); net 42,240 (38.4) |
| G-F2 | Wheat fertility 55 | gross `52,800 x 55 / 70` = 41,485 g (37.71 m); net 30,925 g (28.11 m) |
| G-F3 | Wheat fertility 95 | capped at 91: gross 68,640 g (62.4 m) |
| G-F4 | Rye fertility 40 (base 50,600 g) | gross 28,914 g; net 18,794 g |
| G-F5 | Tenant, 25% share, 10% tithe, fertility 70 wheat | tithe 4,224 g (3.84 m); rent 10,560 g (9.6 m); holder net of seed **27,456 g = 24.96 m** (stock path -10,560 +52,800 -4,224 -10,560 = +27,456) |
| G-F6 | Freeholder, same field | tithe 4,224; holder keeps 38,016 g (34.56 m) net of seed |
| G-F7 | Drought: rain ratio 0.3 -> `rain_f = 580` | gross 30,624 g; net 20,064 g (18.24 m): 47.5% of normal net |
| G-F8 | Fertility ledger, start 70: wheat -> barley -> fallow grazed | 60 -> 54 -> 69. With peas instead of barley: 60 -> 65 -> 80. Fallow ungrazed (+5) with barley: 60 -> 54 -> 59 |
| G-F9 | Overdue: gross 52,800 g, uncut 5 days past grace | after day: 50,688, 48,660, 46,713, 44,844, **43,050 g** (39.14 m) |
| G-F10 | Spoilage 15 days: 1,000,000 g in a house (3,414 ppm/day) vs granary (670 ppm/day) | house 950,003 g (+-100: 5.0% per season); granary 990,004 g (+-100) |
| G-F11 | Late frost: min temp -3.2 C on doy 10; fields sown doy 5, 9, 11; sow window 3-14 | doy 5 and 9 fields killed and resown (kill day <= 12, yield x 750: 39,600 g gross at fert 70); doy 11 field untouched; each resow costs another 10,560 g seed |
| G-F12 | Labour: parcel of 10 acres reaped by 4 workers at 2 acres/worker-day, 10-hour days | `ppm/hour/worker = 2 x 1,000,000 / (10 x 10) = 20,000`; 4 workers = 80,000/h; complete in 13 worker-hours -> day 1 (10 h) + 3 h on day 2 |
| G-F13 | Wet harvest: 4 fields cut on a day with 8 mm rain, 48 m each | each x 0.92 = 48,576 g (44.16 m); at 12 mm x 0.85 = 44,880 g |
| G-F14 | Hail: parcel hit, `DamagePermille = 500` on a normal field | gross 26,400 g (24 m); net 15,840 g |
| G-F15 | Region balance (reference region): 62,400 fields, 20,800 per slot, fertility 70, winter 60/40 wheat/rye, spring 40/27/20/13 | winter net 41,536 g/acre, spring net 40,022 g/acre; region net `20,800 x (41,536 + 40,022)` = 1,696,414,720 g = **1,542,195 measures**; x 0.96 spoilage = 1,480,507; demand 25,000 x 60 x 0.92 = 1,380,000 + fodder 45,000 = 1,425,000 -> surplus **+3.9%** (accept +2%..+10%) |
| G-F16 | Coin rent equivalent | share 25% of 38.4 m x 1.1 kg x 45 mites = 475 mites per cut acre = 317 per field-year; `rent_coin_mites_per_field_year = 320` is within 1% |
| G-F17 | Slot invariant after 3 rotations | each field returns to its start slot; per-settlement slot counts unchanged |
| G-F18 | Hail frequency | over 1,000,000 parcel storm-days with p = 5 permille, hits in 5,000 +- 150 (seeded) |

## 9. Soak invariants
1. Normal-year region grain surplus in [+2%, +10%] of demand (G-F15) for 100 years of seeded weather; in a drought year net
   production in [35%, 60%] of normal; two drought years in 10 produce a famine chronicle entry and no extinction.
2. Mean field fertility stays within +-4 points of start over 100 years with normal sheep density; with sheep removed it
   declines (test shows the ledger works).
3. No negative stock; per-year grain mass audit `created(Harvest) - destroyed(Sown, Spoiled, Eaten, Fodder, Brewed, Milled)`
   equals stock change.
4. Slot counts per settlement within +-10% of a third every year.
5. Hungry gap: staple price at doy 25 / price at doy 40 >= 1.6 in normal years (markets spec curve).
