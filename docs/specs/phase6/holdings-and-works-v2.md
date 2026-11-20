# Holdings and works v2: the Phase 5 carry-overs for land, building, crafting, businesses and animals

Plan refs: §3a (fields are parcels anyone can hold; soil improvement: marling or manuring is an improvement project), §3e (horses are bred: foals once a game year), §4A.2 (households put up cottages, clear fields by assarting; build limits come from free parcels, which the lord sells; a new street when plots run out), §3g and §2.10 (knapping, oiling, tools), Phase 5 overview 2.2 and T2-T4 (soil improvement, new streets, horse breeding, mortgages, theft from piles, upkeep: Phase 6), `docs/reviews/phase-5.md` (S6, S8, S12, G2 and the "Moved, with the phase each goes to" table), `docs/reviews/phase-4.md` (obsidian and flint knapping).

## 1. Purpose
Finish what Phase 5 left open in the same files, as small bounded pieces a helper can take one at a time: the hero's household as a field holder so a claim can be cleared and sown (OV-3's missing half), streets and soil improvement as the lord's works, the NPC land market, furnishing, room ownership, horse breeding, and a list of crafting and construction loose ends. Items that do not serve a Phase 6 done-when and need systems of later phases are moved on with a reason (section 4.9). Mortgages are in `rule-and-income.md` 4.6; theft from piles in `crime-and-justice.md` 4.8; piece upkeep in `fire-and-burning.md` 4.7.

## 2. Dependencies
| Needs | From |
|---|---|
| `LandWorld` (`Improve`, `ImprovementKind.SownAcre`, claims, deeds, the land market, stream 0x500 reserved), farm tables (`Farming`), `GrowthSystem` | Phase 2-5 |
| `ConstructionWorld` (sites, piles, haul jobs, contracts), `PlanFitter`, RoomDetector v2 room types | Phase 5 |
| Crafting stations, tool wear, apprenticeship, the refit rules, clothing book | Phase 3, 5 |
| `RideBook` (animals), `species.jsonc` | Phase 3 |
| The lord's planner and zoning | `rule-and-income.md`, `diplomacy-and-ai-lords.md` |
New: `Land/Zoning`, `Land/LandMarketNpc`, `Building/HouseholdFurnisher`, `Animals/Breeding`. Extended where named below.

## 3. The items (each with its rule, numbers and test)

### 3.1 The hero's household as a field holder; clearing and sowing a claim (S6)
The farm tables credit a parcel's yield to its holder's household (today leased fields still credit the lord's household's farming). Phase 6 adds the holder household per parcel in the farm rows (an additive key in `farming`), so a leased or claimed field's harvest goes to the hero's barn. **Assarting** on a claim: Q > Clear an acre makes a felling site: the acre's trees (from the chunk) are felled tree by tree (Phase 5 felling: each tree's logs kept as items at the stump, `Woodland` stock drawn down), stumps grubbed, 90 worker-hours an acre in total (Phase 5 OV-3's figure); then Q > Sow (seed 9.5 measures an acre, in the sowing window) calls `LandWorld.Improve(SownAcre)` and the claim grows 16 -> 24 tiles (Phase 5 rule). Background growth's assarts become the same felling sites in visited chunks (Phase 5 move).
**Test HW-1:** OV-3 completed: claim, cottage, an acre cleared by the hero and two hands (90 worker-hours), sown; by day 60 a Freehold deed of 24 tiles; the harvest goes to the hero's barn.

### 3.2 Claim banner, "Whose is this?", the Land overlay
`claim_banner` (good: a pole and a cloth, recipe 0.5 h) is the item planted to stake (Phase 5 content was empty). Q > **Whose is this?** on a door, field or lot names the holder, tenure and lord from `LandRegistry` (a known fact; a stranger's house may say "a burgher's"). The Land overlay is `rule-and-income.md` 6. **HW-2.**

### 3.3 The NPC land market (stream 0x500, reserved in Phase 5, now drawn)
Households with coin >= 2 x a plot's price and a member wanting a dwelling (the marriage brake's waiting couples) buy vacant plots and houses at S6 from the lord or from sellers (a household with an empty house and arrears sells at 90% of price); the hero can buy a deed from an NPC by offer (Q > Offer for this house: the holder accepts at >= price x (1,100 - trust/10) / 1,000). Ties among buyers by (coin desc, id), draws only for exact ties (0x500, key plot and season). **HW-3.**

### 3.4 Zoning and new streets (S12)
A lord's zoning marks parcels as lots, fields, waste or common. When a settlement's free lots fall to 0 and its waiting couples exceed 3, the lord's planner lays a **new street**: a street segment from the nearest street end along waste or field parcels (the Phase 1 plan's street rule), 6-10 burgage lots on it, priced by `land.jsonc`; the chunks it crosses get `ChunkDelta`s (visited) or generate with it (not visited). **HW-4:** seed 1234's home town gets a new street within 2 game years and lots near 450 crowns are listed there (closes S12).

### 3.5 Soil improvement as contracts
Marling (+15 fertility, lasts 20 game years then fades 1 a year; 250 worker-hours and 40 t of marl an acre from a marl pit) and manuring (+8 for 2 seasons; 1 t of dung an acre from byres or folding sheep) are posted by holders (the lord's planner on demesne, NPC holders when fertility < 50) as contracts (`contracts-and-rumours-v2.md`) and can be done by the hero on his own fields. **HW-5.**

### 3.6 Furnishing and room ownership
`HouseholdFurnisher`: a household in a dwelling missing a hearth or beds for its members buys or makes them (a pallet 0.5 h, a bed by commission) within a season; library plans (`test_cottage` and others) carry a hearth and beds per their capacity. Rooms gain `Owner` (a household or business) and beds `Assigned` (a person): family members sleep in their own beds (`family-and-lifespan.md`); a guest sleeps in an unassigned bed. New room types: `slipway`, `wheelwright_yard`, `loft`; the vehicle designer requires the yard or slipway for "Build this" (Phase 5 move). **HW-6.**

### 3.7 Horse breeding and studs
A mare and a stallion of a species kept together (a paddock or stable with fodder) breed once a game year with P 60% (stream **0x6E1**, key mare and year; append to `species.jsonc`: `foal_pct`, `gestation_days` 56 game days (11 months scaled x60/365, as human pregnancy is 45), `maturity_years` 3); a foal is an animal row with its parents' mean traits +-5 (0x6E1 draw 1); it can be ridden at 3, sold at any age (price by age). A stud is a business kind (founding as Phase 5). **HW-7.**

### 3.8 Crafting and construction loose ends
| Item | Rule | Test |
|---|---|---|
| Buy and deliver to a site's pile; "Post haul jobs" on the site panel | a market order with a carter's cart (a vehicle row) to the pile at the book's price + 2 mites per kg-km | HW-8 |
| Installing workstations from items | an anvil from 120 kg of iron bars by a smith's recipe, then placed as a piece | HW-9 |
| Fatigue and food for heavy work | building and felling burn food x1.5 and stamina; a worker eats at noon from his pack or the site's store | HW-9 |
| Tool wear for NPC businesses at stations; stores in rooms near the crafter | NPC crafters' tools wear by Phase 5's rule; containers within 6 m count as the crafter's store | HW-10 |
| NPC apprentices from the labour market; refits synced with the clothing book's fit | a master takes an idle youth of 12-16 for a season; a refit writes the garment's fit size | HW-10 |
| Founding a business in a generated building | a generated building's rooms are recognised as workshops with stations from its blueprint's fixtures | HW-11 |
| Clear-felling and coppice; carting distance as a quarry's cost | woodland stock by felling site; coppice regrows in 7 game years; a quarry's output price adds 2 mites per t-km to the nearest market | HW-11 |
| 30-day oiling (`oil_item`) | a recipe: 10 g of oil, 0.2 h; an oiled item rusts at 1/4 the rate for 30 days (`ItemFlags.Oiled`) | HW-12 |
| Obsidian and flint knapping | materials `flint`, `obsidian` (Phase 4's sharpness numbers from the wiki re-check: max edge 10x steel's for obsidian, brittle); recipes `knap_flint_blade`, `knap_obsidian_blade` (stone hammer, 0.3 h, skill floor 10); brittle (0x532 breaks) | HW-12 |
| Construction's haul distances on `IntMath.Isqrt` (S8) | replace `Math.Sqrt` on doubles; results unchanged for region metres | HW-13 |
| The business panel's "Supply all" from the view (G2) | the panel's view carries the inputs; no table read on the main thread | HW-13 |
| World-start market stocks (if the fun check finds the first weeks starved) | merchants start with 5 days of their book's mean demand | HW-14 (conditional) |

### 3.9 Moved on, with reasons
| Item | To | Reason |
|---|---|---|
| A water check in `PlanFitter` | Phase 8 | construction has no water hook; no Phase 6 done-when needs it |
| Cellars in library plans; a new mine as a walkable underground site | Phase 8 | digging and new underground sites belong with the Phase 8 site templates and bestiary work; heists use generated sites |
| Taking down generated buildings; reusing a lot's standing walls and Deconstruct ghosts | Phase 8 | new streets (3.4) use free parcels; demolition is a build-mode depth item |
| A share market on the Ledger | Phase 8 | shares beside the owner work; a market is depth, not a done-when |
| Boats: ferries, fishing in the daily run, drowning | Phase 7 | already moved (waterways at scale) |

## 4. Tick placement
Barrier (the hero's verbs), H2 (felling and soil work credit), H6 S6 (land market, zoning, furnishing), S1 (breeding, coppice regrowth).

## 5. Determinism
Streams: **0x500** NPC land market ties (Phase 5 reserved, now drawn), **0x6E1** breeding; the rest is ordered by id. Hashes: `LandHash` (holders in farm rows, streets), `AnimalHash` (foals).

## 6. UI
Q verbs Clear an acre, Sow, Whose is this?, Offer for this house; the site panel's Buy and deliver and Post haul jobs; the Ledger's stud and coppice rows. Readable at 960 x 540.

## 7. Tests
HW-1 to HW-14 as listed above, each a golden with the numbers in its row; plus **HW-15** determinism and save (a felling site, a foal, a new street: save -> load -> save byte-identical).

## 8. Open questions (recommended defaults)
1. **Market stocks at a new game:** only if the Phase 5 fun check says the first weeks are starved (Phase 5 decision). Default: conditional.
2. **Marling's life:** 20 game years. Default as written.

## 9. Budgets
Felling sites as Phase 5 sites; the land market <= 2 ms at S6; furnishing <= 1 ms a season; breeding <= 0.1 ms a year; save: farm holder keys 4 B a parcel held by a household other than its Phase 2 owner.

## Notes (P6-B6, built)
- **Where it lives.** `Sim/Holdings/` (`HoldingsBook` + `HoldingsSave` (the optional "holdings" section), `HoldingsWorld` (clearing, sowing, coppice, breeding, rooms' owners and beds), `HoldingsDesk` (`hw:` verbs: clear, work, hire, dismiss, sow, collect, whose, buydeliver, posthaul, install, foundlot, coppice; `HoldingsView`), `HeavyWork`, `Crafters`, `HoldingsWiring` with `HoldingsSystem` at the daily hour 18 and the season's first day), `Building/HouseholdFurnisher`, `Farming/FarmingSystem.Hand` (`SowByHand`, `SowingSeasonAt`, `HolderOf`), `Production/OwnershipWorld.Generated` (a business in a generated building, a business's near stores). Stream 0x6E1 (draws 0, 1-4, 9) is the breeding; 0x6E0 and 0x500 stay undrawn.
- **Hero's household as a holder (3.1, HW-1, OV-3).** Q > Clear an acre (`hw:clear`) makes a `Clearing` of a tile of his claim: 90 worker-hours, the first 60% felling the acre's trees one by one (wood from the tile's wood row into a store at the stumps), the rest grubbing; the hero's hours (`hw:work`, eight heavy hours a day) and up to six hired hands (a day's wage each, six hours) are credited; when it is done the tile leaves the woodland and becomes a one-acre parcel of the hero's household through `RegionGrowth` (`AddParcels`, the growth log, the chunk delta), so the farm tables credit its harvest to his barn. Q > Sow (`hw:sow`) ploughs and sows it by hand from the pack's seed in the crop's window (`SowByHand`) and calls `LandWorld.Improve(SownAcre)`. A field he leases (`LandWorld.ParcelLet`) is his household's too until the lease ends. OV-3 (Phase5OverviewTests) is now whole: banner crafted and planted, cottage, an acre cleared by the hero and two hands, sown in the autumn window, 24 tiles, the rye harvest (32 kg) in his barn.
- **Banner and Whose is this? (3.2, HW-2).** `claim_banner` (a good) and `make_claim_banner` (0.5 h from a pole and a cloth) are content; `claim_banner_item` is set, so the desk's Stake needs one and uses it up. `HoldingsDesk.WhoseAt` names the holder (a stranger's lot elsewhere says only "a stranger's"), the tenure and the lord; the Q verb sends `hw:whose`.
- **Furnishing and rooms (3.6, HW-6)**, **breeding (3.7, HW-7)**, **loose ends (3.8, HW-8 to HW-13)** as in `docs/reviews/phase-6-open.md` "B6"; tests `Holdings/HoldingsTests` (HW-1 twice, HW-2, HW-7, HW-15) and `Holdings/HoldingsWorksTests` (HW-6 twice, HW-8 to HW-13). Oiling now cuts rust to a quarter (IQ-10 updated); `knap_flint_blade` and `knap_obsidian_blade` make brittle daggers (`brittle: true` on a recipe).
- **Godot.** Q menu "Holdings" (Whose is this?, Clear an acre, the acre's status, Fell and grub by hand, Hire two hands, Sow, Take the logs), the site panel's second row (Buy and deliver what is missing, Post haul jobs); `--ng-shot homestead` writes `p6-b6-homestead.png` (an acre cleared beside the hero) and `-menu.png`.
