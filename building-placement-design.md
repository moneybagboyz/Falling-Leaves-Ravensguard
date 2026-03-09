# Building Placement & Worker Cap Design Notes

## Implementation Status (as of March 9, 2026)

| Feature | Status | Location |
|---------|--------|----------|
| **Max workers for extraction** | ✅ IMPLEMENTED | [production_ledger.gd:169-183](../src/simulation/economy/production_ledger.gd) |
| **Dynamic building counts from acreage** | ❌ NOT IMPLEMENTED | Still uses hardcoded TIER_BASE in building_placer.gd |
| **Weighted labor split by max_workers** | ❌ NOT IMPLEMENTED | Still divides evenly (production_ledger.gd:227) |

**Summary:** 1 of 3 features complete. The lumber_camp extraction fix has been implemented and respects milestone max_workers. The other two proposals (dynamic building counts and weighted labor allocation) remain unimplemented.

---

## The current situation

`TIER_BASE` is a hardcoded lookup table:

```gdscript
# tier 0 — hamlet
{"well": 1, "farm_plot": 2, "derelict": 1, "open_land": -1}
```

Houses are dynamic — `ceil(population / housing_capacity)` — which is why
every settlement feels correctly sized for its people. Everything else is a
flat tier number that ignores the actual settlement: a hamlet with 5 people
and one with 300 both get exactly 2 farm plots.

---

## 1. Dynamic building counts

The same logic that drives house counts can drive most other buildings.
The key insight is that `_compute_territory_acreage` is currently called in
**step 6** (after stamping), but the data it produces — `arable_acres`,
`woodlot_acres`, `total_population()` — is exactly what building counts
should derive from. The fix is to move acreage computation to **step 2.5**
(right after claiming cells, before composing the placement list), then
derive counts from the live acreage data.

### Proposed derivation rules

| Building | Driver | Formula |
|---|---|---|
| `farm_plot` | arable acres | `ceil(arable_acres / 120)` capped at tier max |
| `farmstead` | arable acres | `ceil(arable_acres / 500)` capped at tier max |
| `house` | population | `ceil(pop / housing_capacity)` — unchanged |
| `well` | population | `ceil(pop / 60)`, min 1 |
| `granary` | arable acres | `ceil(arable_acres / 600)`, min 1 at tier ≥1 |
| `grain_mill` | farm_plot count | `ceil(farm_plots / 5)`, min 1 at tier ≥1 |
| `smithy` | artisan pop | `ceil(artisans / 25)` capped at tier max |
| `market_stall` | merchant pop | `ceil(merchants / 15)` if tier ≥1 |
| `market` | merchant pop | `ceil(merchants / 50)` if tier ≥2 |
| `inn` | tier | `tier` (1 per tier, min 1) |
| `derelict` | fixed | 1 per tier (unchanged) |
| `lumber_camp` | woodlot acres | `ceil(woodlot_acres / 250)` (tag-gated) |
| `iron_mine` | mining slots | `ceil(mining_slots / 2)` (tag-gated, tier ≥2) |
| `iron_smelter` | mine count | 1 if `iron_mine` ≥1 and tier ≥4 |

**Pros versus the current table:**
- A forested hamlet with real acreage might generate 3 lumber camps; a
  treeless one generates zero. Correct both ways.
- A large city with 3 000 merchants gets 4 markets; a small town with 200
  gets 1. The map becomes readable as an economic indicator.
- Tier becomes a cap/gate, not the primary determinant. Two tier-2 towns
  built in different terrain genuinely look different.

**The one tricky bit — ordering:**
Acreage must be computed before building counts, which means `_compute_territory_acreage`
moves from step 6 to step 2.5. The `building_levels` seed (step 6 remainder)
stays after stamping since it doesn't affect physical cell layout.

---

## 2. Max workers per level — what already exists

The milestone system already has `max_workers` on every building JSON:

```json
// iron_mine milestones
"1": { "max_workers": 12,  "output_multiplier": 0.5 }
"3": { "max_workers": 30,  "output_multiplier": 0.8 }
"5": { "max_workers": 75,  "output_multiplier": 1.0 }
"7": { "max_workers": 150, "output_multiplier": 1.4 }
"9": { "max_workers": 300, "output_multiplier": 2.0 }
```

And it's already respected in two out of three production pathways:

| Pathway | Respects milestone max_workers? |
|---|---|
| `_run_standard_recipes` | ✅ `mini(ms.get("max_workers", 1), wkr_per_bld)` |
| `_run_mining` | ✅ `mini(split["extraction_workers"], max_wkr)` |
| `_run_extraction` (timber) | ❌ Uses `WOODLOT_LABOUR_FACTOR` only — ignores lumber_camp milestone entirely |

So the worker cap system is already designed and partially working.
The gap is timber extraction — it bypasses the milestone entirely and
just uses `woodlot_acres * 0.002` as a labor floor. Upgrading your
lumber_camp from level 1 (3 workers) to level 7 (9 workers) currently
makes no difference to extraction output, which means there's no
meaningful reason to upgrade it.

### Fix for `_run_extraction`

```gdscript
# Replace the current flat WOODLOT_LABOUR_FACTOR approach:
var camp_level: int = ss.building_levels.get("lumber_camp", 0)
var b_def = cr.get_content("building", "lumber_camp")
var ms := _active_milestone(b_def, camp_level) if b_def != null else {}
var max_wkr: int = int(ms.get("max_workers", 8))  # fallback to level-1 cap
var split := _compute_labour_split(ss)
var ext_labour: int = mini(split["extraction_workers"], max_wkr)
# (remove the WOODLOT_LABOUR_FACTOR floor — it bypasses the cap)
var yield_per_wd: float = float(recipe.get("yield_per_worker_day", 0.5))
var out_mult: float = float(ms.get("output_multiplier", 1.0))
var output: float = float(ext_labour) * yield_per_wd * out_mult * float(delta_ticks)
```

This makes timber extraction behave identically to iron mining:
- Worker cap rises with building level (3 → 5 → 7... up to lumber_camp's max)
- `output_multiplier` from the milestone also applies
- Upgrading the lumber camp is now meaningfully worthwhile

---

## 3. Does the labor split make sense overall?

Currently `_compute_labour_split` divides peasants into `farm_workers` and
`remainder`, then splits remainder between extraction and industry based on
relative build_demand scores. Workers are a shared pool distributed across
all buildings of a type.

**What works well:**
- Food security automatically pulls labour toward farming when grain is low
- Build demand scores indirect labour allocation without explicit jobs

**What breaks down at scale:**
- A city with 10 farm_plots and 3 smithies splits `industry_workers / 13`
  per building. The smithies starve for labour. This is `_count_producing_buildings`
  not distinguishing between a 1-worker farm_plot (max_workers: 2) and a
  3-worker smithy (max_workers: 3) — they're weighted equally.
- The extraction pool isn't split between lumber_camp and iron_mine if both
  exist — whichever runs first gets the full allocation.

**Better approach (future):** weight `wkr_per_bld` by the milestone's
`max_workers` rather than dividing evenly. Each building gets
`(its_max_workers / sum_of_all_max_workers) * industry_workers`. This is
a one-line change in `_run_standard_recipes` and would fix both issues.

---

## Summary: what to implement

| Item | Effort | Impact |
|---|---|---|
| Move acreage to step 2.5, derive counts dynamically | Medium | Very high — world variety |
| Fix `_run_extraction` to use milestone max_workers | Small | Medium — upgrades matter |
| Weight labour split by max_workers | Small | Medium — large cities more realistic |

All three are independent and can be done in any order.
