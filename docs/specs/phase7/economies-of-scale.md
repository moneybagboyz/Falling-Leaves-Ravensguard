# Economies of scale: division of labour, shared fixed costs, bulk buying and carting, machines, munition patterns, and their limits

Status: design, written before any Phase 7 code, on top of b6b39e2. Plan refs: §5 Phase 7 "Economies of scale for every business (decided with the user, Nov 2026)": advantages of size (division of labour, a master with journeymen and apprentices on the simple steps; shared fixed costs: one forge, kiln, building, cart, heavy tools; bulk buying and bulk carting; water- and wind-powered machines; munition-grade standard patterns made faster than one-off pieces) and limits (crowding at stations, a steward's span of control, guild size rules, market demand); §4A.1 (a recipe is inputs + worker-hours + tool wear -> output; quality from skill; building levels become extra workstations and better tools, never free output; logistics: cart ~500 kg, ox wagon ~1 t; tolls per vehicle); the Soldiers' equipment row (munition-grade mass production in large workshops with many hands).

Money in mites (1 cp = 10, 1 sp = 100, 1 gp = 1,000; `coinage.md`). Wage classes as today: unskilled 150 a day, skilled 350, master 500 (10 working hours: 15, 35, 50 an hour); under 16 half the unskilled wage.

## 1. Purpose
Today a business's cost per unit does not fall with its size: every batch costs the same worker-hours at the same wage and the same fuel, whoever works it and however many batches run. Real workshops grew because a master's hours are scarce and a hearth burns whether one smith or two work at it. This spec gives every business, generated or owned, the five historical advantages of size and the four limits that stop it growing for ever, in integer rules the planner, the steward, the Industry tab and the military's arms orders all read.

## 2. What exists and what is new
| Exists (by file) | Phase 7 |
|---|---|
| `Production/Business.cs` (`BusinessKind`: stations, staff slots, `LabourPm` one number per kind, `FoundedOnly`), `ProductionSystem` (batches at stations, margin planning, buy orders, sale lots), `Labour.cs` (hiring by skill, wage offers), `RecipeBook.cs` (the price solver) | extended: crew roles and step grades (4.1-4.2), per-station `labour_pm` from a powered machine (4.5; `LabourPm` stays as the kind's default), shared heat (4.3), absorption caps (5.4) |
| `Crafting/*` (one recipe format, `workers_max`, `wage_class`, tool wear `ToolWear`, quality from the crafter's score, `Apprenticeship`: 2 apprentices a master, unpaid, one season; `Guilds`: templates per town, members, secrets) | extended: `grades_pm` and `pattern` blocks on recipes; guild size rules (5.3) |
| `Production/StewardBrain.cs` (properties `3 + Leadership / 20`, at most 6; lagged prices; reserve) | extended: span of control inside a works (5.2); contracts and carting cost in his plan |
| `Production/OwnershipWorld*.cs`, the Ledger's Businesses tab, `UI/BusinessPanel.cs` | extended: Crew and Costs views (8) |
| `Caravans/*`, `Vehicles/VehicleTable`, `Taxes/Tolls` (per vehicle) | read for carting cost (4.4) |
| `content/base/production/businesses.jsonc` (`water_sawmill` with `labour_pm` 125 and no water check) | its 125 moves to the gang saw piece and applies only while powered (`large-buildings.md` 4.1) |
| **New** | `Production/Crews.cs` (roles, grade hours, elapsed time and cost of a batch), `Production/FixedCosts.cs` (heat and firing shares, the day's fixed costs), `Production/SupplyContracts.cs`, `Production/Patterns.cs`, `Production/Limits.cs` (crowding, span, guild caps, absorption), `Save/ScaleSave.cs`; content `production/scale.jsonc` |

## 3. Data
- **Role** (derived each day, never saved): `Master` (skill >= 60 in the recipe's skill and its proficiency known), `Journeyman` (skill 30-59), `Apprentice` (a formal apprentice, or under 16), `Hand` (unskilled occupation, or skill < 30). Wages: master class, skilled class, apprentice unpaid (formal) or half the unskilled wage (under 16), hand unskilled.
- **Recipe additions** (content, optional; defaults by category in `scale.jsonc`): `grades_pm: [M, J, A]` summing 1,000; `pattern: { setup_h, piece_h, run_max, quality_cap }`.
- **Station kind additions** (`build/pieces_workstations.jsonc`, `scale.jsonc`): `crew_max`; for heat stations `lit_fuel_g_per_h` and `share_max`; for firing stations `fuel_fixed_g`, `fuel_g_per_kg`, `capacity_kg`, `cycle_h`.
- **`SupplyContract`** (saved): `{ Id, Seller (business or household), Buyer, Good, QtyPerDelivery (g or pieces), EveryDays, PriceMicro (per g or piece), StartDay, TermSeasons, Carter (Seller or Buyer), Delivered, Missed, State (Open, Done, Broken) }`.
- **`PatternRuns`** (saved): per business and pattern recipe, runs completed and the last day.
- **`Foreman`** (saved): staff member and business; **`ManufactoryLicence`** (saved): business, granting lord, fee paid, seasons paid; **`GuildFine`** rows (saved, pruned after 2 years).

## 4. The advantages of size

### 4.1 Division of labour: the crew at a station
A recipe's worker-hours `H` (after `labour_pm` and the crafter's speed, as today) split by grade: `H_M = H x M / 1,000`, `H_J = H x J / 1,000`, `H_A = H x A / 1,000`. A master may do any step, a journeyman J and A steps, apprentices and hands A steps. With `n_M`, `n_J`, `n_A` people on one batch, the batch's elapsed working hours are
`T = max( H_M / n_M, (H_M + H_J) / (n_M + n_J), H / (n_M + n_J + n_A) )`
(integer worker-minutes, rounded up). With no master present, journeymen do the M steps at `unsupervised_pm` 1,500 (half as slow again) and the batch's quality uses the best journeyman's score. **Cost** is the hours each person actually works on the batch times his hourly wage. A master who has M steps waiting at another station of the business goes there as soon as his own M steps are done (he does not help down); otherwise he helps with J, then A steps.
**Master's reach:** a master serves the M steps of up to `2 + Leadership / 30` stations (at most 4) in the same building.
**Default grades (`scale.jsonc`):** smithing 200/400/400; armouring 250/400/350; weaving 100/300/600; tailoring 200/400/400; pottery 150/350/500; brick moulding 50/150/800; carpentry 150/450/400; sawing 0/300/700; charcoal 0/200/800; brewing 200/300/500; munition patterns 50/250/700.
**Worked example (ES-1).** `forge_sword`, 24 h at an anvil, smithing grades (4.8 / 9.6 / 9.6 h):
| Crew | T (hours) | Cost (mites) | Cost per sword |
|---|---|---|---|
| a master alone | 24 | 24 x 50 = 1,200 | 1,200 |
| master + journeyman + hand, the master helping down | max(4.8, 7.2, 8.0) = 8 | 8 x 50 + 8 x 35 + 8 x 15 = 800 | 800 (-33%) |
| 1 master, 2 journeymen, 3 hands at 3 anvils (the master on M steps only) | the master's 10 h a day serve 2.08 swords; the journeymen's 20 h serve 2.08 | per sword 4.8 x 50 + 9.6 x 35 + 9.6 x 15 = 720 | 720 (-40%), 2 swords a day |

### 4.2 Shared fixed costs
- **Heat stations** (forge, bloomery, cupel hearth, oven, brew copper, dye vat, lime kiln): fuel has a fixed part per lit hour and a variable part per batch. A lit station burns `lit_fuel_g_per_h` whatever works it, and up to `share_max` batches use one lit station at once (a forge's hearth serves 2 anvils). Content default for the forge: 1,000 g of charcoal a lit hour, `share_max` 2; `forge_bar`'s 10 kg of charcoal becomes 8 lit hours x 1 kg + 2 kg per batch. Two bars forged side by side: `(8 + 2 + 2) / 2 = 6 kg` each instead of 10 (-40%).
- **Firing stations** (kiln, brick clamp, oven, charcoal clamp): one firing burns `fuel = fuel_fixed + fuel_per_kg x load`, up to `capacity_kg`, over `cycle_h`. Scotch brick kiln: fixed 4,000 kg of firewood, 0.3 kg a kg, capacity 60,000 kg: a full kiln burns 22,000 kg for 60 t (0.37 kg a kg), a 10 t load 7,000 kg (0.70 kg a kg). The planner fills a firing before lighting it unless the owner's directive says "fire at n%".
- **The day's fixed costs** (the Costs view, 8): building and piece upkeep as real repair work and materials (`industries.md` 5.8), the draught animals' fodder whether or not they haul, the steward's and clerk's wages, guild dues, licences. Unit cost = `(fixed per day + variable per unit x units) / units`; it falls as output rises.
- **Heavy tools** (a great anvil, a grindstone, a press, a crane) are one item for every station of a room that can reach it (6 m, crafting reach); tool wear per batch is unchanged, the purchase is made once.

### 4.3 Bulk buying: supply contracts
- A **supply contract** is a standing order between two parties: at least 500 kg a delivery (or 10 gp a season for piece goods), every `EveryDays` (default 5), for 1 to 4 seasons, at a price fixed at signing: the seller's book mid `x (1,000 + (Trading_seller - Trading_buyer) / 2) / 1,000`, clamped to mid +-50 permille.
- Payment on delivery (`Trade`); **no spread and no market toll** (the goods never enter the market book). The carter (seller by default) moves the goods by cart or wagon; the cost is his.
- **Saving:** the buyer pays mid instead of ask (8% less at the default half-spread), the seller receives mid instead of bid less the 5% toll (about 13% more). The town's market loses the toll; that is the historical cost of contracts to a town.
- **Breach:** a delivery more than 2 days late is `Missed`; the buyer buys at the market and may charge the difference (`Restitution`, existing reason); 2 misses in a season and the buyer may end the contract (`Broken`): opinion -20 between the parties, chronicle `supply_contract_broken`.
- **NPC use:** a business whose input buying passes 1 t a season (or 10 gp) offers a contract to the nearest producer whose mid is below its own book's ask; a lord's armoury orders kit by contract (`army-economy.md` 4.10).

### 4.4 Bulk carting
`trip_cost = days x (crew wages + draught fodder at book price) + tolls x vehicles + wear`; `cost_per_kg = trip_cost / load`. An 8 km leg (about 18 game hours a way, 1.5 days a round trip), at hay 12 and oats 30 mites a kg:
| Vehicle | Load | Crew and team | Round trip | Cost per kg |
|---|---|---|---|---|
| cart | 500 kg | 1 driver (150), 1 horse (8 kg hay + 2.5 kg oats = 171) | 1.5 d: 482 + 2 tolls (40) = 522 | 1.04 |
| ox wagon | 1,000 kg | 1 driver, 2 oxen (10 kg hay each = 240) | 2.3 d: 897 + 40 = 937 | 0.94 |
| four-horse wagon (designer) | 2,000 kg | 1 driver, 4 horses (684) | 1.5 d: 1,251 + 40 = 1,291 | 0.65 |
A load under 60% of capacity is flagged ("half-empty cart") in the Industry tab and the steward's report. Packed goods (`industries.md` 5.9) load 10% denser.

### 4.5 Machines
A powered machine coupled to a station (`large-buildings.md` 4.3) sets that station's `labour_pm` while it runs (the kind's `LabourPm` is the default when nothing is coupled):
| Machine | Station and recipe | labour_pm | Other effect |
|---|---|---|---|
| fulling stocks | fulling tub, `full_cloth` (40 h a 100 kg) | 125 | - |
| trip-hammer | forge, `forge_bar` (8 h) and drawing out plate | 375 | - |
| great bellows | bloomery, smelting | 500 | furnace capacity x2 |
| gang saw | saw shed, `saw_planks` | 125 | the `water_sawmill`'s number, now only while powered |
| stamp battery | dressing floor, crushing | 100 | 6 t a day a battery |
| pug mill | clay yard, tempering | 100 | - |
| millstones (water or wind) | mill, `mill_flour` | as Phase 2 `mill` | - |
| water hoist and pump | mine | hauling 1 hand per 8 hewers | flood days 0 |
Without power (a frozen stream, a dry summer below the wheel's need, a broken wheel) the station falls back to `labour_pm` 1,000 and its crew needs grow; the Industry tab names the cause.

### 4.6 Munition-grade standard patterns
A recipe may carry a `pattern`: a run of `k` identical pieces (up to `run_max`) takes `setup_h + k x piece_h` hours and its quality is capped at `quality_cap` (default **common**, tier 2 of crude-poor-common-fine-masterwork). The setup (patterns, swages, gauges, cutting a form) is paid once a run; a run may span days at one station. **Practice:** a business that finished 3 runs of the same pattern in the last 2 seasons works `piece_h x 850 / 1,000`. Examples (content `crafting/recipes_munition.jsonc`, shared with `army-economy.md` 4.10):
| Item | One-off h | Pattern: setup + per piece | Per piece in a run of 20 | With practice |
|---|---|---|---|---|
| spearhead (0.4 kg of iron) | 3 | 6 + 1.2 | 1.5 | 1.32 |
| kettle hat (1.5 kg) | 12 | 16 + 5 | 5.8 | 5.05 |
| sheaf of 24 arrowheads | 6 | 4 + 2 | 2.2 | 1.9 |
| gambeson (3 kg of cloth) | 30 | 10 + 14 | 14.5 | 12.4 |
| mail shirt (10 kg) | 300 | 40 + 180 | 182 | 155 |

## 5. The limits

### 5.1 Crowding at stations
Each station kind has `crew_max` k (anvil 3, forge 4, loom 2, saw pit 2, frame saw 4, gang saw 2, moulding table 3, kiln setting 6, bloomery 4, dressing floor 4). Effective workers at a station: `eff_pm(n, k) = 1,000 x min(n, k) + 500 x min(max(0, n - k), k)` (a worker beyond k counts half, beyond 2k not at all). A room gives each worker at least 4 m2 of free floor (3 m2 in a manufactory hall, `large-buildings.md` 4.4); beyond that each extra worker counts half.

### 5.2 Span of control
Overseers are the masters, appointed foremen (a journeyman or hand with Leadership >= 20, +50 mites a day) and the steward. Each oversees `6 + Leadership / 10` workers (at most 16). A crew larger than the sum works at `speed_pm = max(500, 1,000 - 25 x excess)`. **Example (ES-6):** 30 workers, a master of Leadership 20 (8) and two foremen of Leadership 30 (9 each): span 26, excess 4, speed 900 permille. For the steward's own span (Phase 5: `3 + Leadership / 20` properties, at most 6), a works counts as `ceil(crew / 20)` properties.

### 5.3 Guild size rules
- Inside a guild town's **liberty** (the town's parcels and 1 km beyond its edge), a member's shop of the guild's trades may have at most `max_journeymen` 3, `max_apprentices` 2 (the existing `apprentice_max_per_master`) and `max_stations` 2 of the trade (per guild in `crafting/guilds.jsonc`).
- A larger works there needs a **manufactory licence** from the town's lord: 5,000 mites (5 gp) and 500 a season, granted by the charter rule (`rule-and-income.md` 4.9's sum, with the guild master's opinion of the petitioner as an extra term and -20 for every licence already granted in that trade in the town). Or it stands outside the liberty: rural industry, the historical answer.
- **Inspection:** each season the guild inspects each oversized, unlicensed member's shop with P = 50% (stream **0x721**, key guild, business, season): a fine of 2,000 mites a season per worker or station over the cap, to the town treasury (`Fine`); the guild master's opinion -20; after 3 fines the lord's court may close the shop (a closing order through `rule-and-income.md`'s court hook). Chronicle `guild_fined_oversize`.

### 5.4 Market demand
The planner and the steward cap a recipe's daily output at `absorb_per_day = sum over books within 8 km of (demand EMA a day x (1,000 - own share pm) / 1,000) + the gate desk's room a day`, and stop a batch when the book's bid at the stock it would reach is below `unit variable cost x 1,050 / 1,000`. The Business panel and the Industry tab show "the markets in reach take about N a day above cost". Selling beyond it is allowed; the per-unit repricing makes it dear.

## 6. Saves
Optional section **`scale`**, absent while empty: supply contracts, pattern runs, foremen, manufactory licences, guild fines. Roles, grade hours, effective crews, spans and absorption are derived. Recipe and station additions are content (`ContentRemap` maps new recipe ids by name). `ScaleHash` joins the hash trail once non-empty. No version bump.

## 7. RNG streams
| Stream | Use | Key |
|---|---|---|
| 0x720 | an apprentice or hand on A steps of a recipe whose difficulty exceeds his skill + 20 spoils the batch with P = `min(25, (difficulty - skill - 20) / 2)` % | business, station, batch serial |
| 0x721 | guild inspection (5.3) | guild, business, season |
Free: 0x722-0x72F.

## 8. Chronicle
| Type | Cause | Root | Key | Parameters |
|---|---|---|---|---|
| `manufactory_licensed` | Law | yes | `ch_scale_licensed` | lord, business, trade, fee (`money`) |
| `guild_fined_oversize` | Law | no (the inspection's own row) | `ch_scale_guild_fine` | guild, business, workers over, fine |
| `supply_contract_broken` | Trade | yes | `ch_scale_contract_broken` | buyer, seller, good, missed deliveries |

## 9. UI (960 x 540)
- **Business panel, Crew tab:** one row per station: crew by role (M J A icons), the grade hours of the current batch, T, crowding (green under k, amber to 2k, red beyond), the master's reach and each overseer's span with the excess; buttons Appoint foreman, Move worker.
- **Business panel, Costs tab:** per unit and per day: variable (inputs, wages of the batch), fixed (lit hours, firings, upkeep, fodder, steward, dues), the unit cost at today's output and at the absorption cap.
- **Contracts:** Q on a producer or a business you own > Offer a supply contract: good, quantity, every n days, term, carter; the price shown as the mid with the Trading band; a list of open contracts in the Ledger's Businesses tab with deliveries and misses.

## 10. Golden tests
| id | Case | Expected |
|---|---|---|
| ES-1 | `forge_sword` with the three crews of 4.1 | T 24 / 8 h; cost 1,200 / 800 / 720 a sword |
| ES-2 | No master: two journeymen on `forge_sword` | M hours 7.2 (4.8 x 1.5); T = max(7.2/2, (7.2 + 9.6)/2, 26.4/2) = 13.2 h |
| ES-3 | Master's reach: Leadership 60 serves 4 stations, 0 serves 2 | as written |
| ES-4 | Shared forge: two `forge_bar` batches side by side | 6 kg of charcoal each; one alone 10 kg |
| ES-5 | Scotch kiln at 60 t and at 10 t | 22,000 kg and 7,000 kg of firewood |
| ES-6 | Span: 30 workers, overseers 8 + 9 + 9 | speed 900 permille |
| ES-7 | Crowding: 5 at an anvil (k 3) | 4,000 permille (4 effective) |
| ES-8 | Supply contract: 1 t of charcoal at a book mid of 12 a kg, equal Trading | 12,000 a delivery, no toll, no spread; market ask would be 12,960 before repricing |
| ES-9 | Contract breach: two deliveries 3 days late in a season | `Missed` x2, the buyer's market cover charged as `Restitution`, ended `Broken`, opinion -20 |
| ES-10 | Carting cost table of 4.4 | 1.04, 0.94, 0.65 mites a kg |
| ES-11 | Pattern: 20 spearheads, then with practice | 30 h (1.5 each), then 1.32 each; quality capped at common for a master |
| ES-12 | Guild cap: a smith's shop with 5 journeymen in the liberty, unlicensed, the draw passing | fine 4,000 a season to the town; opinion -20; after 3, a closing order |
| ES-13 | Licence: petition with opinion 20, standing 200, fee 5 gp, guild master's opinion -30 | the rule sum with the extra term decides; refusal names the largest negative term |
| ES-14 | Absorption: a smithy making nails with demand EMA 40 kg a day in reach and 10 kg of desk room | the planner's cap 50 kg a day; the bid test stops batches past it |
| ES-15 | A powered trip-hammer stops (the wheel frozen) | `forge_bar` back to 8 h; the cause on the tab |
| ES-16 | Determinism, save, conservation: a year of a town with contracts, patterns and a licensed works | coin and mass exact; `ScaleHash` equal; save, load, save identical |

## 11. Budgets
Crew assignment at a batch start <= 2 us; the absorption cap once a day a business in its H4 slice (<= 0.3 ms a region a day); contracts <= 0.1 ms a region a day; `scale` <= 30 KB a region at 10 years.

## 12. Open questions (recommended defaults)
| # | Question | Default |
|---|---|---|
| ESQ-1 | **For the user:** apply division of labour to every generated business too (the plan says "for every business")? Labour costs fall about a third in well-staffed shops, so prices of finished goods fall; the audit re-runs its soak | **Yes**, for every business; the audit's second run (`economy-audit.md` 7) checks it |
| ESQ-2 | Munition quality cap | common |
| ESQ-3 | Guild caps | 3 journeymen, 2 apprentices, 2 stations; licence 5 gp + 5 sp a season |
| ESQ-4 | Span formula | `6 + Leadership / 10`, at most 16, -25 permille a worker of excess, floor 500 |

## 13. Estimate and parts
| Part | Content | Gate | Months |
|---|---|---|---|
| **P7-ES1** crews and limits | roles, grades, T and cost in `ProductionSystem` and crafting, the master's reach, crowding, span, foremen, guild caps and inspection, licences, the Crew tab | ES-1 to ES-3, ES-6, ES-7, ES-12, ES-13 | 0.9 |
| **P7-ES2** costs, contracts, patterns | heat and firing shares, the day's fixed costs, supply contracts, carting cost, patterns and practice, absorption caps, machines' per-station `labour_pm` hook, the Costs tab and contracts UI | ES-4, ES-5, ES-8 to ES-11, ES-14 to ES-16 | 0.9 |
**About 1.8 months of code**; ES1 and ES2 can run in parallel after ES1's `Crews.cs` interface is fixed (one week in).
