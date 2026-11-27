# The balance pass: the economy's open findings, Phase 6's late-game leftovers, and tuning for 1.0

Status: design, before any Phase 8 code, on top of f2530c3.
Plan refs: §5 Phase 8 (the balance pass), §3 (brutal by design, fair through legibility; honest sliders), §7 (emergent chaos: closed money, conservation tests, soaks with a player bot).
Carried in: EA_F25, EA_F18, WL-9, EA_F11 (phase-7 review and `phase-7-audit.md`); OV-6 and OV-7's gaps; Phase 6 G2's late-game soil decline, household growth and delivery contracts nobody takes. Decision D13 (who does soil work).

## 1. Goals
1. Close the economy findings Phase 7 left open, with numbers.
2. Fix Phase 6's century-scale drifts.
3. Tune the game for 1.0 from measured bots and the user's fun-check notes, with every change logged.

## 2. What exists and what is new
| Today (f2530c3) | Phase 8 |
|---|---|
| `Diagnostics/WarLoad*.cs` (no armouries in the load), `IndustryNpcTests` (IN-20), `Phase7Overview` OV-6 and OV-7 | **extended:** armouries in the load; OV-6 with a claim; a copper-cap run |
| `Farming/*` soil jobs, `Growth/GrowthSystem.cs`, `Contracts/*` deliver posts | **changed:** soil work by D13; household growth capped; carters take deliveries |
| `Sim/Diagnostics` bots (`--autoplay`, the rich-player and exploit bots) | **new:** `Diagnostics/BalanceBots.cs` and `econ-audit --balance` |

## 3. Rules

### 3.1 The economy findings (BL-1)
- **EA_F25 (L1 clay marginal):** the clay chain's year is a small difference of large flows. The fix is in content: the drying shed's losses and the brick price, retuned until IN-20's band holds on 10 of 12 settling runs.
- **EA_F18 (L3 working capital 6 to 28 times its bill):**
  - suppliers of charcoal, ore and barrels to a works sell on **30-day terms** by contract (Phase 6 contracts v2), so stock is paid as it is used;
  - the Industry tab shows the **capital need** (the peak cash the works has needed) beside the bill.
- **WL-9 and EA_F11 (kits and iron):** the war-load harness carries armouries (after P8-WS-2). WL-9 is measured. For EA_F11 a full levy's kits (960 kits; 2.1 t of bar) must be met within **2 seasons** of an order by local works or imports, with the price rise logged.
- **OV-6's gap (BL-3):** the test hero holds a claim, upgrades the smeltery to L2, sends his own caravan with its quote, and checks payback.
- **OV-7's gap (BL-4):** a run where copper is struck up to the 4% cap.

### 3.2 Phase 6's late-game leftovers (BL-1)
- **Soil work** (D13, recommended: holders' own hands first, then the labour pool): a holder's household does its own soil jobs in idle hours before posting them; unfilled posts go to the day-labour pool at the posted wage.
- **Household growth:** a new cottage is founded only when a household holds more than **6 adults** or a couple has no hearth. The century gate is households up no more than population + 10 points.
- **Deliveries:** a household with a cart or pack animal scores deliver contracts like any job, by pay a km against its day wage. A post not taken in 10 days is withdrawn and its goods go by the seller's own carter.

### 3.3 The balance bots (BL-2)
- **The runs:** 8 origins x 20 seeds x 2 game years, headless, on autopilot with `PursueAmbition` and a fixed set of interrupts. Nightly.
- **What is measured, per origin:** days to the first step; hero deaths in the first game year; coin and wealth at year 1 and year 2; the share of seeds where the start ambition is done.
- **Provisional bands** (set again from the fun-check notes before BL-2 starts):

| Measure | Band |
|---|---|
| Median days to the first step | 3 to 15 |
| First-year hero deaths at default difficulty | 10% to 40% |
| Median wealth at year 2 | no origin under 0; no origin above 5 times the others' median |
| Start ambition done in 2 years | 20% to 80% of seeds |

- **Exploits:** the exploit bot finds no money loop over the Phase 7 audit's exploit rule.
- **Sliders are honest:** each world slider moves its measure the way its name says, at every step: wound lethality (deaths), bandit frequency (encounters), economy harshness (prices against wages), plague severity (plague deaths), industry output (payback).

### 3.4 How tuning is done
- Content only, where it can be. Each change is logged in `docs/reviews/phase-8.md` with the measure it moved.
- A golden that moves is re-pinned with the reason beside it. The generated layer and history never move.
- The fun-check notes of Phases 4 to 7 and tester rounds 1 and 2 are read before BL-2 starts; each note is answered (changed, or kept with a reason).

### 3.5 Integer math notes
- Payback in whole game days; working capital as the peak of `-(cumulative cash)` in `long` mites.
- Bands are compared on integer medians (the middle value of the sorted seeds).

## 4. Saves
No new keys. Contract terms for 30-day supply use the existing `contracts` section.

## 5. Golden tests
| Id | Name | Asserts |
|---|---|---|
| BL-1 | Clay | the L1 clay works pays back inside IN-20's band on 10 of 12 settling runs |
| BL-2 | L3 capital | with 30-day terms the L3's capital need is 4 times its bill or less on 3 of 5 seeds; the tab shows it |
| BL-3 | OV-6 closed | the hero's L2 upgrade, caravan quote and payback run end to end; coin and mass exact |
| BL-4 | OV-7 closed | copper reaches its 4% cap; the coin book exact every day |
| BL-5 | Kits and iron | WL-9 measured with armouries; 960 kits met within 2 seasons by works or imports |
| BL-6 | Century | the 100-year soak: soil jobs filled after year 60; households within population + 10 points; deliveries taken; famines not rising decade on decade |
| BL-7 | First step | every origin's median days to the first step in its band |
| BL-8 | Deaths | first-year hero deaths in band at default difficulty |
| BL-9 | Wealth | no origin outside the wealth band |
| BL-10 | Exploits | the exploit bot finds no loop over the rule |
| BL-11 | Honest sliders | each slider's measure moves the right way at each step |
| BL-12 | Notes answered | every fun-check and tester note has a logged answer (a script checks the log against the notes) |

## 6. Parts and gates
| Part | Scope | Gate | Months |
|---|---|---|---|
| P8-BL-1 economy carry-overs | EA_F25, EA_F18, WL-9, EA_F11, OV-6, OV-7, Phase 6's leftovers | BL-1 to BL-6 | 1.0 |
| P8-BL-2 the balance pass | the bots, the bands, sliders, the notes | BL-7 to BL-12 | 1.0 |
