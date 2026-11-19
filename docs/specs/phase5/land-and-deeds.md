# Land: the LandRegistry, deeds, lot sales, leases, claims and property buildings

Plan refs: §5 Phase 5 ("Land: `LandRegistry`, deeds and claims; owning and leasing fields; barns, granaries and mills"; done-when "buy a lot", "claim a clearing and build a homestead"), §3.8 (parcels and deeds; claiming land), §4A.2 (households have a dwelling and a deed: owned, rented or tenant; build limits come from free parcels, which the lord sells; a fief is a deeded set of parcels plus rights; founding: a charter is a deed to wild land; a granary is storage volume; market = a square with rentable stalls; arrears lead to debt, then eviction), §4A.1 (a town lot costs about 300-600 crowns), concept book first hour (the reeve's notice board lists a lot on Tanner's Row for 450 crowns; "rent a market stall now").

## 1. Purpose
One record says who holds every lot, parcel, building, stall and claim, on what terms and owing what. Deeds can be bought, sold, leased, inherited and forfeited, always as coin moving between real purses. The lord sells free plots; anyone can lease fields; the hero can stake a claim in wild land and make it his by improving it. Everything later (build mode, businesses, inheritance, Phase 6 fiefs) asks the registry "may this person do this here?".

## 2. Dependencies
| Needs | From |
|---|---|
| `Lot` (plan lots: `Use`, `Rect`, `BlueprintId`, `Vacant`), `Parcel` (`ParcelOwner(OwnerKind, Fief, LotIndex)`), `Fief`, settlement plans and streets | exists (`WorldGen/Region/RegionModels.cs`) |
| `HouseholdTable.Deed` (`DeedKind None, Owned, Rented, Tenant`), lot binding by the populator | exists (`People/`) |
| Growth log `LotRecord`, `ParcelRecord`, `RegionGrowth.FindPlot`, `FindClearing`, `GeneratedView` | exists (`Growth/`, Phase 3) |
| `TaxSystem` dwelling rents (cottage 100, longhouse 150, burgage 400 mites a season), parcel rent 320 mites a field a year, arrears 3 seasons -> debt, 6 -> eviction | exists (`content/base/economy/taxes.jsonc`, farming) |
| `Ledger` transfers (`Trade`, `Rent`, `Inheritance`, `Escheat`, `Fine`), `Treasuries`, purses | exists (`Economy/`, `Taxes/`) |
| Region tiles (1 acre, `Forest`, slope, soil), `ResourceLayer` | exists |
| Notice boards (`notice_board` piece), the job board, dialogue `start_contract` | exists (Phase 3) |
New: `Sim/Land/` (`LandRegistry`, `Deed`, `DeedChange`, `Claim`, `Lease`, `LotMarket`, `LandRules`), save section `land`, content `economy/land.jsonc`.

## 3. Data

### 3.1 `Deed` (one per held thing; derived, not stored, until it changes)
| Field | Type | Unit / meaning |
|---|---|---|
| `Id` | `EntityId` (`IdKind.Other`, serial base `0x730_0000`) | stable: `hash(kind, region, subject)` for generated deeds, allocator for new ones |
| `Kind` | `HoldingKind` | `Lot, Parcel, Building, Stall, Claim` (`Fief` reserved for Phase 6) |
| `Subject` | int / `EntityId` | lot index, parcel id, structure id (build spec 3.3), stall piece id, claim id |
| `Region, Settlement` | int | |
| `Holder` | `EntityId` | person, household, business, fief treasury or church (`PurseKind` owner) |
| `Tenure` | enum | `Freehold, Burgage, Leasehold, Tenancy, Customary` |
| `Lord` | fief index | who receives dues, gets an escheat, and (Phase 6) judges |
| `DuesMitesPerSeason` | int | burgage rent, quit-rent or lease rent |
| `LeaseEndDay`, `Lessor` | int, `EntityId` | for `Leasehold` and stalls; `-1` and none otherwise |
| `ArrearsSeasons` | byte | from the tax step |
| `AcquiredDay`, `Cause` | int, chronicle cause | bought, inherited, granted, claimed, forfeited |
`HoldingKind` is a new enum; the household column `HouseholdTable.Deed` stays and is **derived** from the registry for the household's dwelling (`Owned` = Freehold/Burgage held by the household, `Rented` = Leasehold, `Tenant` = Tenancy/Customary).

### 3.2 Generated deeds are a function of the region; only changes are saved
`LandRegistry.Build(region, people)` derives every deed at load from the generated view plus the growth log (as growth does, decision G2): dwelling lots to their household (tenure by `DeedKind`: burghers Burgage, gentry Freehold, peasants Tenancy of the fief's lord), workplace lots to the business owner (Burgage in towns, Customary in villages), civic lots to the fief or the parish, parcels by `ParcelOwner` (Demesne and Tenant -> the lord as freeholder with the tenant household's `Tenancy` lease row, Glebe -> parish, Burgher and Freeholder -> that household), stalls to the town (lessor). The save stores the **change log** (`DeedChange { Day, DeedId, NewHolder, NewTenure, Dues, LeaseEnd, Cause }`) plus claims and leases; load replays it over the derived set (idempotent; byte-identical round trip).

### 3.3 `economy/land.jsonc`
```jsonc
{ "type": "land_rules", "id": "default",
  "plot_price_mites_per_m2": { "town": 150, "village": 40, "hamlet": 25 },      // 11 x 28 m town plot = 46,200 mites = 462 crowns
  "demand_pm": { "per_crowded_pct": 5, "min": 800, "max": 1500 },              // x(1 + 0.005 x crowded % of households), clamped
  "building_value_pm_of_cost": 800,                                             // a standing house: 80% of its materials at book price + labour at the unskilled wage
  "burgage_rent_mites_per_season": 300, "quit_rent_mites_per_acre_season": 25,
  "listings_per_settlement_season": { "town": 3, "village": 1 },
  "stall_rent_mites_per_season": 1500, "stall_fair_day_mites": 150,
  "claim": { "start_tiles": 8, "max_tiles": 40, "tiles_per_improvement": 8, "improve_within_days": 60, "min_m_from_plan": 1500,
             "licence_mites_per_acre": 1000, "banner_item": "claim_banner" },
  "lease_term_days": 60, "lease_rent_mites_per_field_year": 320, "eviction_notice_days": 15 }
```

## 4. Rules

### 4.1 Who may do what (`LandRules.May(person, action, cell)`)
| Action | Allowed when |
|---|---|
| Build anything (place ghosts) | the cell is on a deed held by the person, his household, a business he owns, or a claim he staked; or he holds a construction contract for it (build spec 4.7) |
| Build only farm structures (barn, fence, pen, haystack) | a parcel he leases (Leasehold) |
| Demolish | the holder (or a contractor for him); tenants and lessees never |
| Farm (sow, reap) | the parcel's holder or lessee |
| Store goods in a building | the holder, his household, his staff (business) |
Everything else (walking, trading at a door) is unchanged. Refusals name the holder ("This is Hedda Tanner's yard").

### 4.2 The lot market (buying from the lord, and resale)
- **Listings.** At each season's first day (H6, with growth) each settlement's lord lists up to `listings_per_settlement_season` free plots, taking `RegionGrowth.FindPlot`'s order (nearest the square first) and vacant lots he holds. Each appears as a line on the settlement's notice board ("A plot on Tanner's Row, 11 by 28 m, 462 crowns, from the reeve") and in the Ledger's Market tab. Growth's own cottage starts use the same plots, so a listed plot can be taken by a village household first (first command at the barrier wins; ties by id).
- **Price.** `price = area_m2 x plot_price[class] x demand_pm / 1000 + building_value` (rounded to whole crowns); `demand_pm = clamp(1000 + 5 x crowded_pct, 800, 1500)` with `crowded_pct` = households over 130% of their beds per 100 households (growth's measure). A town plot of 11 x 28 m with no crowding: **462 crowns**; with 10% crowded: 485; a village plot of 16 x 38 m: 243.
- **Buying** is a barrier command `LandBuy(deed, buyer)`: full price `Trade` buyer purse -> seller purse (the lord's purse for plots, his fief treasury when the lord's purse is short, as growth pays). New deed tenure Burgage in towns (dues 300 mites a season to the lord), Freehold elsewhere with a quit-rent of 25 mites per acre a season. Chronicle: `land_bought` (cause: the buyer).
- **Resale.** Any holder can offer a deed (Q on the door > Sell this house, or the Ledger). NPC buyers are households that marriage or growth would house (a waiting couple's household with coin above the price) and businesses wanting a premises; they buy at `>= price x 900 pm` within a season, else the hero can lower the ask. The hero can buy from an NPC who lists (death, debt, a move to town); NPCs never sell a home they live in unless they are leaving.

### 4.3 Leasing fields, buildings and stalls
- **Fields:** the lord's re-let in autumn (farming) offers free parcels; the hero can lease any parcel the re-let would let, at `320 mites a field a year`, paid per season (80). Term 60 days (one year), renewed at the re-let if not in arrears. The lessee farms it with Phase 2 farming rules (own hands, hired hands, a steward).
- **Buildings:** a holder can lease a building he does not use (a vacant house, a barn) at the dwelling rent table or a rent he sets; NPC households rent as housing (marriage's `FreeDwelling` sees it).
- **Market stalls:** each `market_stall` piece in a square is a `Stall` deed held by the town (or the lord), leased by the season (1,500 mites) or by the fair day (150). A stall lets its lessee sell (businesses spec 4.5). The concept book's "rent a market stall" is this lease.
- **Arrears** run through the existing tax step: after 3 seasons a debt, after 6 eviction with 15 days' notice: the deed returns to the lessor, goods left inside move to the lessor's store as distraint (Ledger `Fine`, valued at bid), half-built ghosts on it stay and pass to the lessor.

### 4.4 Claims: staking wild land and making it yours
- **Where:** tiles that no deed covers (outside every fief's parcels and lots and at least 1.5 km from any settlement plan), or the waste of a fief (tiles in a fief's area not in any parcel) with a **licence** bought from its lord (`1,000 mites an acre`, the plan's assart licence). Unlicensed claims on a fief's waste are allowed and flagged `Squatting` (the lord's reaction is Phase 6; Phase 5 shows it in the Ledger and in rumours).
- **How:** carry a `claim_banner` (recipe: pole 3 kg + cloth 0.5 kg, 20 min) and use Q on the ground > Stake a claim. The claim covers the 8 tiles nearest the banner (a connected set, level and dry first), shown as a dashed border. The banner is a world object: pull it up and the claim ends.
- **Making it yours:** within 60 game days the claim must hold one **improvement**: a dwelling recognised by RoomDetector v2 (roofed, hearth, a bed), or one sown acre. Each improvement adds 8 tiles (to 40, a yardland homestead). Without one by day 60 the claim lapses. A claim with a dwelling becomes a `Freehold` deed (outside fiefs, no dues) or a `Customary` deed of the fief (licensed, quit-rent).
- **Homestead (done-when):** claim, clear (Phase 3 assart rules by hand or hired: 90 worker-hours an acre), build a cottage hands-on (build spec), sow: the deed and the fields join the hero's Ledger. Clearing a tile gives logs and firewood to the claimant (growth's `assart_firewood_pct`).

### 4.5 Barns, granaries and mills as property
- **Storage volume.** A building's storage rooms (RoomDetector v2: storage, granary, barn, cellar) give its deed a `Store` whose volume is `room area x 2 m x 500 pm` (a 6 x 8 m barn: 48 m2 -> 48,000 L) and mass cap from the floor (2,000 kg/m2 on ground, 400 kg/m2 upper floors). Grain in a granary room keeps Phase 2 spoilage at x0.5; in an open yard x2.
- **Mills** are businesses (businesses spec): owning a mill deed with a recognised mill room and millstones lets the owner run a milling business; multure is the existing recipe margin.
- The plan's "owning a barn" is just this: a deed whose storage the owner, his household and his staff may use.

### 4.6 Tick placement
| Where | What |
|---|---|
| Barrier | `LandBuy`, `LandSell`, `LandLease`, `StakeClaim`, `PullBanner`, `Evict`; registry updates and `ChunkDelta` for the banner object |
| H6, first day of each season | listings; claim lapse checks; lease ends |
| S5 (tax step) | dues and lease rents, arrears (existing system reads `DuesMitesPerSeason`) |
| Load | `LandRegistry.Build` and change-log replay |

## 4a. Founding charters (decided with the user, Nov 2026)

A **founding charter** is a deed kind (`DeedKind.FoundingCharter`) granted by a realm's ruler, or by a landholding faction's ruler, over a named site of wild or royal land. Its data:
- grantor (the realm or faction id) and grantee (a person);
- the site, as a set of parcels or tiles;
- `settle_by_day` and `min_households` (default: 5 households within 2 game years);
- dues to the grantor (coin per year, or a share of the harvest);
- optional service owed (men for days per year);
- a fee paid at grant, through the ledger.

Phase 5 rules:
- **Phase 5's simple grant rule:** the petition is made in person at the ruler's seat. It is granted if the grantee's reputation with the realm is at least 0, the fee is paid, and the site is not claimed or contested. Phase 6 replaces this with the full political decision.
- **Settling:** the grantee recruits real households (they walk there with their own goods). When enough are settled by the deadline, the charter is fulfilled.
- **Growth:** a village forms at 80+ people with a chapel or a mill.
- **Failure:** a missed deadline marks the charter as lapsed. In Phase 5 a lapsed charter is only recorded; revocation is Phase 6.

Tests:
- **LD-C1:** a granted charter creates the deed and moves the fee to the grantor's treasury, with coin conserved.
- **LD-C2:** 5 households settled before the deadline fulfil the charter, and 4 do not.
- **LD-C3:** a site that overlaps an existing claim is refused.
- **LD-C4:** grant and fulfilment are chronicle events with causes.

## 5. Determinism
No random draws: listings follow `FindPlot` order, buyers are chosen by (coin descending, household id), ties by id. Stream 0x500 is reserved for Phase 6 disputes and asserted never drawn. Prices integer mites.

## 6. UI and controls
- **Notice board** (E on it): the lots for sale with price, size, dues and a map pin (the player pins; the world never points). Buy from the reeve or lord in person (dialogue `buy_lot`), since a deed needs a witness.
- **Q on a door or the ground:** Whose is this? (holder, tenure, dues), Buy, Lease, Sell, Stake a claim, Pull up banner.
- **Ledger (L) > Property tab** (businesses spec 6): every deed, tenure, dues, arrears, lease end, value; alerts at 3 days before a lease end and at the first arrears season. No new keys.
- Map overlay (M, the existing overlay key in the map screen): "Land" shades your deeds, leases and claims. Readable at 960 x 540: dashed claim borders 2 px, labels above 10 px.

## 7. Tests (golden)
| id | Case | Expected |
|---|---|---|
| LD-1 | Registry from generation: seed 1234 home region | every dwelling household has exactly one dwelling deed; `HouseholdTable.Deed` derived equals the generated column for all 5,000+ households |
| LD-2 | Round trip: buy, lease, claim, save, load, save | byte-identical; replayed registry hash equal |
| LD-3 | Price: town plot 11 x 28 m, no crowding | 46,200 mites (462 crowns); 10% crowded: 48,510; village 16 x 38: 24,320 |
| LD-4 | Buying a listed plot with 500 crowns | buyer -46,200, lord +46,200 (treasury tops up nothing); deed Burgage, dues 300 a season; coin conserved |
| LD-5 | Race: hero and a growth cottage start want the same plot in one barrier | the earlier command wins; the loser gets the next plot; deterministic |
| LD-6 | May: hero on his lot, on a leased parcel, on a neighbour's lot | build any / farm structures only / refused with the holder's name |
| LD-7 | Lease a field: 320 a year | 80 mites a season through the tax step; arrears 3 seasons -> debt; 6 -> eviction notice, 15 days later the lease ends |
| LD-8 | Stall: season lease and a fair day | 1,500 and 150 mites `Rent` to the town treasury |
| LD-9 | Claim outside fiefs: stake, no improvement for 60 days | 8 tiles; lapses on day 60 at H6 |
| LD-10 | Claim improved: cottage recognised on day 40, an acre sown on day 50 | 16 then 24 tiles; deed Freehold; tiles stay connected |
| LD-11 | Licence on a fief's waste, 8 acres | 8,000 mites to the lord; deed Customary, quit-rent 200 a season; without a licence flag `Squatting` |
| LD-12 | Barn storage: 6 x 8 m barn, storage room 48 m2 | store volume 48,000 L; mass cap 96 t |
| LD-13 | Eviction with goods inside | goods to the lessor as distraint at bid; `MassLedger` and coin exact |
| LD-14 | No randomness: run LD-4 to LD-11 with every `DetRng` stream counter checked | stream 0x500 never drawn |

## 8. Open questions (recommended defaults)
1. **Unlicensed claims on a lord's waste** (overview Q3): allowed, flagged Squatting; eviction by the lord comes with Phase 6 justice.
2. **Lot price level:** 150 mites/m2 in towns (462 crowns for a burgage plot, the concept book's 450). Default as written; the soak checks NPC buyers still find homes.
3. **New streets when plots run out** (Phase 3 decision G3): not in Phase 5; the lord's street planning arrives with Phase 6 fief zoning.
4. **Mortgages and loans on deeds:** Phase 6 (loans to rulers and creditors); Phase 5 deeds carry no debt except arrears.

## 9. Budgets
Registry build at load <= 40 ms per region (about 6,000 lots and 2,500 parcels); `May` <= 2 microseconds (a cell -> deed grid, 4 bytes a cell at 64 m tiles plus a lot rectangle index); change log <= 40 B an entry, <= 100 KB a save; listings <= 1 ms per region a season.

## Notes (P5-A2, as built, Nov 2026)

**Built.** `Sim/Land/`: `LandTypes` (`HoldingKind` with `FoundingCharter`, `Tenure`, `Deed`, `DeedChange`, `Claim`, `Charter`, `LandIds`, `LandBook`), `LandRules` (prices, demand, dues, licence, storage, whose land a tile is), `LandRegistry` (derived deeds plus the book, `DerivedDeedKind`, `May`, hash), `LandWorld` (lot market, resale, leases of fields, houses and stalls, dues and the arrears ladder, eviction with distraint, claims, founding charters, the daily and seasonal steps), `LandSystem` (abstract tick; discovered by attribute), `LandWiring`, `LandDesk` (hero commands and the `LandView`); `Save/LandSave` (the optional "land" section); content `economy/land.jsonc` (`land_rules`, `Content/Defs/LandDefs.cs`). Godot: `UI/LandScreen` (Lots and leases / Deeds), `World/StreetView.Land` (Q verbs, the officer check, the claim dialog, `--shot-panel land|deeds|claim`), a Deeds button on the character sheet, the smoke opens both screens.

**What it extends (all marked P5-A2).** `WorldState.Land` and the "land" section (absent while the book is empty: worlds that never touch land keep their bytes; no version bump); `HeroCommandKind.Land` (appended) routed to `LandDesk`; `WorldRuntime` attaches the land to the home region; `RngStreams.LandDisputesReserved = 0x500` (and the stream test's block widened to 0x5FF); `GrowthSystem.LandHolds` (growth never refits a vacant house the player bought or rents); `FarmingSystem.LeasedOut` (the re-let skips a leased parcel). A bought new plot is a lot of the plan through growth's own log (`LotRecord`, `Dwelling None`, no blueprint), so a load replays it, chunks get it as a delta and `FindPlot` never offers it again. Fees and dues go to the existing purses: the lord's household, else the fief's treasury, else the town's chest; a realm's treasury is the taxes' realm purse.

**Decisions taken while building (for review).**
1. *Whose land.* The region's fief map gives every tile to its nearest fief, so "outside every fief" never happens in a generated region. Land within `claim_min_m_from_plan` (1.5 km) of a settlement's plan is the waste of that fief (licence 10 crowns an acre, else squatting); farther out is wild land, free and unprotected (overview 10a). Land near a plan without a lord (a chartered town's commons) cannot be claimed.
2. *Listings are not stored*: they are a function of the records (the lord's vacant houses, then `FindPlot`'s plots, up to 3 in a town and 1 in a village), so a plot growth starts on disappears from the board, and a buyer whose plot was taken gets the next one (LD-5).
3. *Dues.* The land step (hour 9 of a season's first day) collects only the book's deeds; the derived dwelling rents stay the tax step's (it reads `HouseholdTable.Deed`), so nothing is charged twice. The ladder lives on the deed (seasons, sum owed, debt at 3, notice at 6, end 15 days later). A lease in arrears, or with goods inside, is held over past its term instead of ending.
4. *Licensed claims* pay quit-rent on the licensed acres (8 acres: 200 a season, LD-11); acres an improvement adds are assarts within the licence. A squatter's dwelling makes no deed.
5. *Improvements* come through `LandWorld.Improve(claim, Dwelling | SownAcre)`. Nothing calls it in play yet: RoomDetector v2 (A3) and construction (P5-B) call it at the barrier when a dwelling is recognised on a claim, and sowing by the hero waits for his household (A4). There is deliberately no hero command for it.
6. *Banner.* `claim_banner_item` is empty until crafting-full adds the item and its recipe; the desk checks the pack once it is set.
7. *Stalls* are derived per place (town 6, village 2, content), not from `market_stall` pieces; rent is paid when taken.
8. *Distraint* moves the goods to the lessor's store and sets their value at the market's bid against what is owed; no coin moves, so it is a book total, not a ledger `Fine`.
9. *Founding charters.* The grantor's seat is the fief's seat, or the realm's largest town in the region (its officer). Standing is kept in the book (absent is 0; Phase 6 moves it). Fee as `Rent` to the grantor's treasury; dues a quarter a season; their arrears are recorded, never a notice. The site is 40 wild tiles (a fief grants within its domain, a realm on its own or nobody's land); a site touching a claim or a charter, or within 10 km of an active war, is contested. Settling households are landless peasants: they leave their house and camp on the site, counted at the nearest settlement (lot -1), coin and stores theirs, until build mode lets them raise houses. The fifth settles it at once; a missed deadline is recorded as lapsed, nothing revoked. The village rule (80 people and a chapel or mill on the site) is checked and recorded; a separate settlement record waits for Phase 6 zoning. The spec's "DeedKind.FoundingCharter" is `HoldingKind.FoundingCharter`: `DeedKind` stays the household dwelling column.
10. *Resale.* A household that wants room (crowded, or a daughter waiting to marry) buys an offered plot when the ask is at most 90% of its value; the hero buying from an NPC who lists is not built.

**Gate.** LD-1 to LD-14 and LD-C1 to LD-C4 green (`tests/.../Land/`: `LandTests`, `LandSaveTests`, `CharterTests`, `LandMarketTests`, 24 tests). LD-2 is proven twice: save, load, save byte for byte with the real runtime and the hero's desk; and save, load and go on equal to the run that never stopped, at the region section with the land book (hour for hour, records hash and bytes).

**Left open.**
- Leasing a field gives the deed, the rent and the rights (`May`: farm, farm structures), and the re-let leaves it alone, but the farming tables still credit the lord's household with its work and harvest until the hero is a household (A4) and works his own fields.
- Leasable fields exist only once holdings fall back to a lord (a household leaves or dies out): a fresh world may list none.
- A whole `WorldRuntime` reloaded mid-game goes on with different economy, markets and production sections than one that ran on, even with no land at all (found while writing LD-2; the land section itself stays equal). It predates this step; for P5-I.
- Not built here: the "Whose is this?" door verb, the map's Land overlay, alerts in the Ledger (P5-F), mortgages (Phase 6), the lord's eviction of squatters (Phase 6), storage rooms giving a deed's store (the formula `LandRules.StoreFor` is in, LD-12; RoomDetector v2 feeds it).
