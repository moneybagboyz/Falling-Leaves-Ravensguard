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
  - **Still a gate, for the review:** the header's whole-content hash. `WorldSave.Restore` and `WorldLibrary.Load` refuse a save or world made with different content, even when every regenerated layer still verifies. So in the game a content update still blocks old saves. The goods are no longer the reason. **Decided by plan §2.11** ("worlds and saves from older versions load through SaveMigrator; if content or mods are missing, a readable report lists what's affected before loading"). So replace the refusal with a pre-load report: what changed (goods, recipes, pieces and so on, from `ContentRemap`'s notes, plus missing mods), and load when every regenerated layer still verifies. Refuse only when a layer the save depends on no longer regenerates identically, or a removed recipe has unfinished work. To do in Phase 5, before the close-out.
  - Two more tables are saved by index and not covered yet: the growth projects' business kind (`GrowthSave.PBusiness`, P5-D's area) and the animal breeds (`AnimalRowSave.Breed`, `MountsSave`; **done in P5-G**: the mounts section names its breeds and vehicle parts by id). Each needs a table in `IdTables` and a rewrite in `ContentRemap.Sections` (with its frozen list) before its content changes.
- **Brittle blades:** `Smithing.BrittleBreaks` exists, but the resolver does not call it.
- **Crafting, still to do:**
  - tool wear for businesses and NPCs at stations;
  - world container stores near the crafter (waits on stores in rooms);
  - Q on a station or item opening the craft screen filtered;
  - ~~boat parts (P5-G) and vehicle-part repair;~~ done in P5-G (`recipes_vehicles.jsonc`, `VehicleSites.Repair`);
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
  - the builder's cart as a P5-G vehicle (still open after P5-G: the contractor's cart is the construction world's own number).
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

## From step 4 (P5-F businesses, stewards, day labour, stalls, the Ledger; prospecting and extraction)
- **The contract builder stalls** (P5-D's area, found by OV-2): a carpenter commissioned for `smithy_house` in seed 1234's town took the escrow and built 4 of its 148 pieces in 160 days. OV-2 builds the smithy with the hero's own crew and a stocked pile instead; the spec names a contractor. To look at with P5-D's builder sourcing (his materials and cart) in P5-I.
- **Businesses, still to do:**
  - live staff walking to the stations in the bubble (crafting-full 4.4's bridge has the queue API; the abstract tier credits the hours everywhere);
  - business and NPC tool wear at stations (the abstract `wear_ppm_per_batch` counter stays; no tool rows);
  - NPC apprentices from the labour market;
  - founding in a generated building (only construction's buildings have rooms with stations; a generated business can be bought);
  - day-labour posts from construction sites, farms short of hands and haulage (only the lord's works post today, through `DayLabour.Posts`);
  - a separate stall panel (the business panel and the Ledger's Stalls tab show a stall);
  - the hero's shares of NPC businesses bought only at the owner beside him (the Q menu) or by command.
- **Decisions to confirm in the review:**
  - the home chest is a purse of its own (`PurseKind.Chest`), not the hero's household purse, so the household's spending never touches the profit; the steward and the dividends pay the hero's purse only when the street last said he is at the business's place;
  - day labour: the lord's works pay 8 pm a day of the coin the fief's treasury and the lord's household hold above 200 crowns, and the well-off (lords, gentry, burghers) spend the same share on hands for whoever is left, their own place first; only the landless take the day's work (a landed household's own work their fields); they are hired the longest without work first (not 4.3's first come by distance) and are labourers for 30 days after a day's work (not 4.3's 15); the day is paid at the hiring hour;
  - a minded stall sells the larger of the book's demand share and its passers-by's spending (1 mite a household a market day);
  - eight founded-only business kinds (never generated or grown) were added, which moved the business-kind indices: the growth section's project kinds and unmet wants are now remapped by name with the others;
  - prospecting: the ore body is the region's vein; one `Skill.Prospecting` appended (the overview said no skill would be added; the prospecting spec, decided later, asks for one).
- **Prospecting, still to do:** the resource overlay in build mode and on the map (it is in the prospecting panel); a new mine as a walkable underground site; clear-felling and coppice; carting distance as a quarry's cost; common rights and grievances; copying a report.
- **Land (from its open list):** leased fields still credit the lord's household's farming; the hero has a household now (A4), but the farm tables need it as the parcel's holder: not changed here (farming's area), for P5-H or the close-out.
## From step 4 (P5-G vehicles)
- **Boats in play, still to do:**
  - a ferry business at a crossing (the ferryman, the rope, the fee: `Ferry.Cross` and the ramps exist; VD-16);
  - fishing boats in the fishery's daily run (`Fishing.Day` exists; VD-15);
  - wind and river-high days in the water under the hero's boat (0 wind in play: sails, beam seas and wave swamping are rules and tests only);
  - people in the water when a boat sinks (waits on Phase 4 drowning; no deaths now).
- **Vehicles, still to do:**
  - the designer opens anywhere and "Build this" lays the frame out beside the hero: the wheelwright's room, cartwright's yard and slipway conditions of 4.1 and 4.2 are not checked;
  - wages for a vehicle site's crew (crews are the hero's people or the commissioned maker; P5-B's wage rules not applied);
  - the vehicle panel (Q > Inspect) is a modal list, not the part grid with each part's colour;
  - NPC drivers on the live map (`NpcDriving` is the rule; the caravans keep their Phase 3 legs);
  - combat damage per part (`VehicleTable.Damage` exists; the resolver does not call it);
  - the builder's cart (P5-D) as a real vehicle row;
  - the `VehicleHash` (`VehicleHash.Of`) is compared by VD-18 and the resume test but not folded into the live `StateHashTrail`;
  - walking on a boat's deck as the vehicle's own sub-grid, ramps and gangplanks as portals (now the cargo and the hero on the bench move with the vehicle, nothing else).
- **Decisions to confirm in the review:**
  - additive keys in the "mounts" section, not a v8 -> v9 step (old rows expand from the template at load);
  - `make_cargo_bed` 2 h (was 8): the ox wagon is 215.8 worker-hours of content, not "about 182";
  - the designer counts the mud team at the bogging line (ratio 700), not at full speed (600);
  - the hero's live boat and every vehicle row share one lock (`RideBook.Gate`) with the tick worker's vehicle pass.

