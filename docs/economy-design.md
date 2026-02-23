# Economy Design — Problems & Improvement Roadmap

This document diagnoses the structural weaknesses in the current economy simulation,
explains the economic theory behind each problem, and gives a concrete implementation
path ordered by impact-per-effort.

---

## 1. The Core Problem: A Central Planner, Not a Market

The current system is a **command economy disguised as a market economy**.

`Production.run()` issues orders: *first feed everyone, then fuel, then buffer grain,
then maximise profit.* That is not how any historical economy — medieval or otherwise —
functioned. Workers do not receive top-down assignments. They respond to prices, wages,
and survival pressures. The settlement has price tracking, but prices only influence
the last few workers in step 4. The other 90% of labour is allocated by a fixed rule
regardless of what the market is saying.

The symptom of this design is that you will tune constants forever. Behaviour is driven
by those constants, not by the structure of the model. When constants are right, every
settlement thrives equally. When one is wrong, every settlement collapses equally.
There is no emergence — no way for individual geography or circumstance to produce
interesting variation.

---

## 2. Missing: Trade and Comparative Advantage

**This is the single highest-leverage fix.**

Ricardo's principle of comparative advantage (1817) is the foundation of economic
geography: if a coastal town is better at fishing *relative to grain* than an inland
town, both gain by specialising and trading — even if the coastal town is absolutely
better at both. Specialisation is only rational when surplus can be exchanged.

Right now every settlement must be self-sufficient. That is not an economy; it is
200 isolated subsistence villages. The consequences:

- A mountain town with no arable land *cannot survive by design*, regardless of how
  much ore or stone it produces. Those 16 permanently-starving settlements in the
  audit are not a tuning problem — they are a structural impossibility.
- Coastal fisheries drown in fish while farming settlements die for lack of protein.
  Both sit next to each other and neither benefits from the other's surplus.
- There is no reason for roads, ports, or markets to exist because nothing moves.

### Implementation path

```
Phase A — Local trade (adjacent settlements only)
  1. Add `export_offers: Dictionary` to Settlement — rid → quantity available to sell
  2. After Production.run() each day, each settlement posts surpluses:
       surplus = stock - 30d_safety_buffer
       if surplus > 0: export_offers[rid] = surplus
  3. A TradeRouter autoload scans connected settlement pairs (road graph).
     For each link, if settlement A has surplus X and settlement B has deficit X,
     transfer min(surplus, deficit) * road_speed_factor units per day.
     Deduct from A's stock; add to B's stock. Record a notional price (avg of
     A's and B's current price for that resource).
  4. Road infrastructure multiplies `road_speed_factor` (dirt=0.3, cobbled=0.6,
     royal road=1.0). This makes road-building decisions meaningful.

Phase B — Regional markets
  Each province capital acts as a price-clearing hub. Settlements within the
  province contribute surpluses and draw deficits through the hub once per week
  (7 days). Hub sets a clearing price = weighted average of offers and bids.
  This is a simple double-auction without needing continuous matching.

Phase C — World market orders (already in Phase 3 of implementation guide)
  Long-distance luxury trade (salt, furs, cloth, iron) between faction capitals.
  Prices here are high and volatile; caravans take weeks to arrive, creating
  genuine supply-demand lag.
```

---

## 3. Missing: Diminishing Returns on Labour

The current model is linear: 2× workers → 2× output on the same fixed land.

Real agriculture and resource extraction exhibit **diminishing marginal returns** — the
50th farmer on the same plot adds far less than the 5th (Malthus, 1798; Ricardo's
theory of rent). Without this:

- There is no internal pressure for settlements to stop piling workers into one sector.
- Buying a second farm level is as good as the first farm level, per worker — the
  decision to diversify is never economically rational.
- A city with 1000 workers on 200 acres produces 10× what a village with 100 workers
  on the same 200 acres produces. That is physically impossible.

### Implementation path

Replace `rate * workers` with a concave production function. The simplest is:

```
output = base_rate * acres * (1 - exp(-k * workers / acres))
```

Where `k ≈ 2.0` gives roughly: first doubling of workers adds 86% of base, second
doubling adds only 10% extra. A Cobb-Douglas form is also standard:

```
output = A * (acres ^ alpha) * (workers ^ (1 - alpha))   # alpha ≈ 0.6 for land-intensive
```

This one change makes specialisation and trade self-reinforcing without any explicit
rules: a coastal settlement *wants* to fish because adding workers there still yields
good returns, while adding its 50th farmer on its meagre arable land yields almost
nothing.

---

## 4. Broken: Price Signals Do Not Drive Behaviour

The market tracks prices and updates them via supply/demand ratios. That is good
bookkeeping. But prices only affect behaviour in `_assign_profit()` — the last step,
after the central planner has already allocated the majority of workers.

A price signal with no structural behavioral response is a scoreboard, not a market.

### What should happen

- If grain prices rise sharply across the region, farmers respond by planting more
  (workers shift toward farming in the next season).
- If wood prices collapse because everyone built a lumber mill, some mills become
  unprofitable and workers shift elsewhere.
- Governors deciding to build should look at regional price trends, not just local
  stock levels. Building a forge when iron sells at 3× base price is rational.
  Building one when iron is at 0.5× base is wasteful.

### Implementation path

```
1. In _assign_profit(), weight is already price/base_price — that part is right.
   Remove the fixed steps 1–3 for everything except a minimal survival floor
   (e.g. if food < 3 days, all workers go to food regardless of price).

2. Workers above the survival floor choose their activity by comparing:
     expected_daily_income[rid] = price[rid] * rate_per_worker[rid]
   They allocate proportionally (a soft-max / logit choice), not in strict
   priority order. This means high grain prices pull workers toward farming
   automatically; no hardcoded priority list needed for normal conditions.

3. Governor AI build decisions query regional price history (moving average
   over 30 days): build the building whose output has been consistently above
   1.5× base price, not the fixed priority list.
```

---

## 5. Broken: Additive Happiness Is Not Welfare

The current system adds and subtracts constants:
- Food shortage → happiness −N
- Wood shortage → happiness −10 per unit short
- Tavern + ale → happiness +2

This means a settlement with 500-day grain surplus and no wood is almost as happy as
a balanced one until the penalties fully stack. That is not how deprivation works.

**Cold and hungry is geometrically worse than the sum of cold and hungry separately.**
This is the concept of **complementary goods** in consumer theory: basic needs must
*all* be met; meeting one exceptionally well does not compensate for failing another.

### Implementation path

Replace additive happiness with a multiplicative welfare function:

```gdscript
# Each factor is 0.0–1.0 representing how well that need is met
var food_security:  float = clampf(grain_days / 7.0, 0.0, 1.0)
var fuel_security:  float = clampf(wood_days  / 7.0, 0.0, 1.0)
var shelter_score:  float = clampf(float(housing_used) / float(population), 0.0, 1.0)
var luxury_bonus:   float = 1.0 + (0.1 if has_ale else 0.0) + (0.1 if has_salt else 0.0)

happiness = food_security * fuel_security * shelter_score * luxury_bonus * 100.0
```

A settlement is only truly happy when *all* needs are met. This creates genuine
urgency around each resource type and makes the luxury bonus meaningful without
it masking a survival crisis.

---

## 6. Missing: Production Seasons

All resources produce identically every day of the 360-day year. Medieval economies
were deeply seasonal:

- Grain: planted in spring, harvested in autumn. Fields lie fallow or are harrowed
  the rest of the year. The period between last year's harvest and next is the
  hunger gap — it was the primary cause of periodic famine.
- Fishing: storm season in winter reduces yields. Summer is the curing/salting season.
- Wood cutting: winter is the traditional felling season (sap is down; ground is hard
  for log transport on frozen rivers).
- Construction: halted in deep winter; the building season is spring–autumn.

### Implementation path — minimal version

```gdscript
# In production.gd, multiply rates by a seasonal factor
static func _season_mult(day: int, resource: String) -> float:
    var month: int = (day / 30) % 12  # 0 = Jan, 11 = Dec
    match resource:
        "grain":
            # Harvest spike in months 7-8 (Aug-Sep), near zero in Jan-Mar
            return [0.1, 0.1, 0.2, 0.5, 0.8, 1.0, 1.0, 2.0, 2.5, 1.0, 0.4, 0.2][month]
        "fish":
            return [0.6, 0.7, 1.0, 1.2, 1.2, 1.0, 1.0, 1.0, 1.0, 0.9, 0.7, 0.5][month]
        _:
            return 1.0
```

Seasons alone will create natural boom-bust cycles, price spikes, and the need for
storage infrastructure (warehouses become genuinely important to bridge the hungry gap).

---

## 7. Missing: Capital Formation and Depreciation

Buildings are permanent. Once a farm is built it produces forever at full capacity with
zero maintenance. Real capital depreciates — mills flood, mines cave in, granaries rot.

Without depreciation:
- There is no ongoing economic pressure; once optimally built a settlement is static.
- The player/governor has no reason to maintain anything.
- Wealth accumulates monotonically, which is not interesting.

### Implementation path — minimal version

```gdscript
# Each building has a chance each year of losing one level from disrepair
const DEPRECIATION_CHANCE_PER_YEAR: float = 0.05  # 5% per year per building
# Daily: chance = 0.05 / 360
if randf() < 0.05 / 360.0:
    downgrade_building(building_id)
```

Governors then *choose* between maintenance and expansion. Greedy governors neglect
maintenance, causing long-term decline — an emergent representation of historical
under-investment.

---

## Recommended Implementation Order

| Priority | Change | Why first |
|----------|--------|-----------|
| 1 | **Trade / surplus transfer** between adjacent settlements | Fixes the 16 starving settlements; creates interdependence; most content per line of code |
| 2 | **Multiplicative welfare** (replace additive happiness) | Makes resource crises matter structurally, not just numerically |
| 3 | **Price-driven worker allocation** (replace fixed priority waterfall) | Removes the central-planner feel; makes prices meaningful |
| 4 | **Diminishing returns** on land-based production | Forces specialisation; correct long-run economic behavior |
| 5 | **Seasons** | Creates natural drama and storage incentives |
| 6 | **Depreciation** | Makes the simulation dynamic over long runs |

Items 1 and 2 are largely independent and can be built in parallel. Items 3 and 4
should be done together since they reinforce each other. Items 5 and 6 are polish
that pays off in long sessions.
