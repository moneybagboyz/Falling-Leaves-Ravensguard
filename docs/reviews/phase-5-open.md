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
  - [x] 200 sites with 400 workers: about 27 ms per game hour against 2 ms. **Met by P5-D's cheap path**: 2.0 to 2.3 ms on a heavily loaded 4-core machine (load 11 to 16), the same bytes as the full walk. Re-measure on the reference PC in P5-I;
  - a 200-cell collapse takes about 65 ms against 3 ms (support re-checks after each post).

  The timing test's thresholds are loose for now; profile and fix in P5-I.
- ~~**Placement ignores trees and props** under a footprint.~~ Done in P5-C: building clears them; a well or a grave refuses the piece. The felled tree's logs are not kept yet.
- **Construction, still to do:**
  - "buy and deliver" from the market to the pile;
  - taking down generated buildings;
  - per-cell light at night;
  - fatigue and food for heavy work;
  - installing workstations from items (an anvil is built from 120 kg of iron bars);
  - workers walking as LOD-bridge people (they are drawn at their pieces);
  - a `Construct` job on the job board.
- **Decisions to confirm in the review:**
  - P5-D: the escrow advances a commissioned builder the materials and the haul at signing (the spec's "a third into escrow"); his ox cart brings his materials to the pile (600 kg a trip) instead of haul jobs through the board;
  - P5-D: posted work pays the hero half at half the piece-hours and the rest at handover, with no advance; a builder's pace is about 90% of the day, +-10% (0x511 as a late start);
  - P5-D: visible growth crews work Phase 3's hours a day (own 2 h, hired 4 h); vacant-house refits and assarts stay abstract (assarts are recorded with their hours);
  - a one-room hovel with a hearth and pallets counts as a dwelling;
  - a beam holds floors within 4 tiles (BC-11 cannot be met with 3);
  - rust and rot are wired in construction (piles, rubble and outdoor chests).

## From step 2 (P5-E full crafting)
- ~~**Important: adding a good breaks every existing save.**~~ **Done (P5 goods-table fix).** Saves now name their content tables in a "tables" section (no format bump), and every load maps saved goods, craft recipes, proficiencies, business kinds, book recipes and extraction kinds onto the loaded content by name (`Save/ContentRemap`). New goods get empty rows; removed ones follow the content's `migration` entries or are written off with one warning. The 7 goods and the recipes for wire, arrowheads, arrows, shingles, daub and harness are back, so the fletching kit has its recipe. Details in crafting-full.md Notes.
  - **Done (load report):** the header's whole-content hash is no longer a gate (plan §2.11). `WorldSave.Inspect` and `LoadReport` give a pre-load report (content changed, mods added or gone, `ContentRemap`'s notes, whether every regenerated layer still verifies); the load screen and the world library show it with Continue or Cancel. A save is refused only for a layer that no longer regenerates identically, content in use that is gone with no migration (a removed recipe with unfinished work and the like), or a newer format, each naming its cause. The game loads only the base content (and the blueprint mod) today, so it passes no installed-mod list and the mod lines appear only to callers that give one (the tests do).
  - Two more tables are saved by index and not covered yet: the growth projects' business kind (`GrowthSave.PBusiness`, P5-D's area) and the animal breeds (`AnimalRowSave.Breed`, `MountsSave`). Each needs a table in `IdTables` and a rewrite in `ContentRemap.Sections` (with its frozen list) before its content changes.
- **Brittle blades:** `Smithing.BrittleBreaks` exists, but the resolver does not call it.
- **Crafting, still to do:**
  - tool wear for businesses and NPCs at stations;
  - world container stores near the crafter (waits on stores in rooms);
  - Q on a station or item opening the craft screen filtered;
  - boat parts (P5-G) and vehicle-part repair;
  - live staff walking to stations and NPC apprentices from the labour market (P5-F);
  - syncing refits with the clothing book's fit.
- **Fixed on the way:** removing an emptied store mid-hour broke the wheat audit, so stores are now removed at the barrier. The world id allocator clashed with region-generated fief and site ids (latent since Phase 3), so crafting ids now start past them.
- **Decisions to confirm in the review:**
  - commission materials are laid on the maker's bench at the order;
  - guilds are derived each time, not saved;
  - CF-14 tested at skill 45, because 35 is Poor on the one scale.

## From step 3 (P5-D contracts and visible growth)
- **Contracts, still to do:**
  - NPC households and businesses commissioning builders (only the lord's planner posts work; growth households build themselves);
  - the hero bidding on posted work with his own crew at his own price, and builders bidding against each other;
  - the builder's cart as a P5-G vehicle.
- **Growth, still to do:**
  - assarts as tree-by-tree felling sites (a region change replayed from the growth log);
  - vacant-house refits as sites.
## From step 3 (P5-C build mode)
- **BC-10 names a plan that fits.** `merchant_house` is 7 m wide and fits an 11 m plot inside its 1 m side setbacks; the test uses the inn. Confirm, or change the spec's example.
- **A bought plot got a generated house** (world generation's fallback for a lot with no blueprint), invisible until the chunk streamed again, refusing every piece. Fixed in P5-C (`LandWorld.BarePlot`); a save made before keeps the house in its growth log: decide whether the migration should clear it.
- **Cellars are left out** of library plans (nothing is dug in Phase 5); the stair down becomes floor. Digging belongs with prospecting and extraction.
- **Build mode, still to do:**
  - a water check in `PlanFitter` (no water hook in construction);
  - reusing the lot's standing walls, and Deconstruct ghosts for what is in the way (spec 4.2's last sentence);
  - the Q-menu verbs on a ghost and E on a ghost;
  - "Post haul jobs" and "Buy and deliver" on the site panel;
  - a roof material by culture or wealth for library plans (always thatch);
  - the controls overlay's Build page shows only the Build actions, not the World keys kept beside them.
