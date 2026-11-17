# Spike 1: scale-model notebook

Source: `src/Ravensguard.Sim/World/ScaleConstants.cs` (constants defined once), `tests/Ravensguard.Sim.Tests/Spikes/ScaleModelSpike.cs` (assertions within 10% of the plan, section 1).

## Town to town, 8 km

| Mode | m/s | real min 1x | 4x | 8x | game hours |
|---|---|---|---|---|---|
| walk | 1.5 | 88.9 | 22.2 | 11.1 | 29.6 |
| jog (default) | 3 | 44.4 | 11.1 | 5.6 | 14.8 |
| cart | 2.5 | 53.3 | 13.3 | 6.7 | 17.8 |
| horse trot | 5 | 26.7 | 6.7 | 3.3 | 8.9 |
| gallop | 12 | 11.1 | 2.8 | 1.4 | 3.7 |

Food and wages per one-way trip (1 measure = 1 person-day = 1.1 kg; wage 1-2 crowns/day):

| Mode | game days | measures | grain kg | wages @1 | wages @2 |
|---|---|---|---|---|---|
| jog | 0.62 | 0.62 | 0.68 | 0.62 | 1.23 |
| cart | 0.74 | 0.74 | 0.81 | 0.74 | 1.48 |
| horse trot | 0.37 | 0.37 | 0.41 | 0.37 | 0.74 |

A round trip costs roughly 1.2-1.5 days of food and 1.2-3 crowns: it matters, as the plan intends.

## Region and clock
- Crossing 32 km jogging: 178 real min at 1x, 22 min at 8x, 2.47 game days. Matches the plan.
- Game day 72 real min at 1x (18 at 4x); year 60 days = 72 real hours. Matches.
- Region = 500 x 500 tiles, 1,000 x 1,000 chunks, 253,000 acres; a 64 m tile is 1.01 acres.

## Armies
- 20 km column march at 1.3 m/s: 256 real min at 1x (32 at 8x), 85.5 game hours = 3.6 game days.
- 1,000 men x 15 days x 1 kg = 15 t = 30 carts of 500 kg.

## Population
- 25,000 / 4.8 = 5,208 households. Levy: 521 (peace, 1 per 10), 1,042 (war, 1 per 5).
- Farmland: bare need is 60 measures/person/year / 48 = 1.25 acres/person (31,250 acres). The plan's 2.3 acres/person (57,500, 23% of the region) implies x1.84 for seed, fodder, spoilage and fallow.

## Findings
1. Travel, clock, march and supply numbers all reproduce the plan within 10%.
2. **Discrepancy (levy):** the plan says the region levies "~250-500 men (1 per 10 households; 1 per 5 in war)". With 4.8 people/household the peace levy is ~520 (top of range) and the war levy is ~1,040, double the stated range. Either state the war levy as ~1,000 or note the range assumes a region share of a realm (the region holds only parts of 1-3 realms).
3. **Discrepancy (farmland):** the 2.3 acres/person is not derivable from 48 measures/acre and a 60-day year alone (1.25). The x1.84 overhead should be a named constant (seed corn, livestock, spoilage, fallow) in `world.json`.

## Decision: GO, with two plan text fixes
The scale model is coherent and the trip times give food/wage pressure without tedium (8x makes a town trip 5.6 min). Fix the war-levy range and name the farmland overhead factor before Phase 2 economy work.
