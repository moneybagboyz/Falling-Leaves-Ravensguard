# Phase 5: open items for the close-out (P5-I)

Collected while the steps are built. The close-out fixes each item or moves it into `docs/reviews/phase-5.md` with a reason and the phase it goes to. This file is then deleted, as `phase-4-open.md` was.

## From step 1
- **AB13 100 v 100 abstract battle budget (3 ms).** It now fails locally even with the machine fairly quiet: 3.1 to 4.3 ms. It passes on CI with the 5x allowance. Its speed-up moved to Phase 7, but re-measure on the reference PC; if it is over budget there too, move the budget with the work, rather than letting a red local run become normal.
- **Tanner's widow origin.** She exists only in a test mod (IN-1). The default, unless the user decides otherwise, is to add her as a real 8th origin with step 5, inheritance.
- **Items, still to do:**
  - weapon condition set by fights;
  - the "Edge 82%" and "Protection x1.4" tooltip lines;
  - per-culture artefact names;
  - 30-day oiling (`oil_item`).
- **Land, still to do:**
  - the "Whose is this?" verb;
  - the map's Land overlay;
  - Ledger alerts;
  - buying a deed from an NPC;
  - the claim banner item and recipe;
  - leased fields crediting the hero's household's farming.
- **Rooms, still to do:**
  - `HouseholdFurnisher` (RD-10 reports the gap, nothing fills it);
  - room `Owner` and `Assigned`;
  - slipway and wheelwright yard room types;
  - a loft type.

## From step 2 (P5-B construction core)
- **Two speed budgets missed:**
  - 200 sites with 400 workers take about 27 ms per game hour against 2 ms (needs the cheap path for sites nobody is watching, with P5-D);
  - a 200-cell collapse takes about 65 ms against 3 ms (support re-checks after each post).

  The timing test's thresholds are loose for now; profile and fix in P5-I.
- **Placement ignores trees and props** under a footprint. Clearing them should be part of building.
- **Construction, still to do:**
  - "buy and deliver" from the market to the pile;
  - taking down generated buildings;
  - per-cell light at night;
  - fatigue and food for heavy work;
  - installing workstations from items (an anvil is built from 120 kg of iron bars);
  - workers walking as LOD-bridge people (they are drawn at their pieces);
  - a `Construct` job on the job board.
- **Decisions to confirm in the review:**
  - a one-room hovel with a hearth and pallets counts as a dwelling;
  - a beam holds floors within 4 tiles (BC-11 cannot be met with 3);
  - rust and rot are wired in construction (piles, rubble and outdoor chests).
