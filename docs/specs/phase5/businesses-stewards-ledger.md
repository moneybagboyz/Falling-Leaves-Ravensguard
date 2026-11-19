# Businesses, shares, stewards, day labour, stalls and the Ledger

Plan refs: §3.8 (businesses run hands-on or through passive shares; stewards can be hired to run properties, so managing many stays feasible), §3.9 (the Ledger, L: every property, business and fief in sortable tables with alerts; stewards and captains handle delegation), §4A.1 (workers and workplaces: a person has an employer and a workplace and changes jobs only by being hired or quitting, driven by wages; businesses are buildings with workstations, an owner and a cash box; building levels become extra workstations and better tools, never free output; wages: labourer 1-2 crowns a day, skilled 3-4; merchants are real people holding stock and cash; shops sell what workshops made; dividends paid from profit out of the cash box), §4A.2 (shares are equity in a Business with its own ledger; a market is a square with rentable stalls), §5 Phase 5 (done-when: buy a lot, build a two-storey smithy, hire a smith, supply iron and see profit while away), concept book (rent a market stall now and start earning).

## 1. Purpose
Phase 2 businesses (`Production/Business`, `BusinessKind`, `ProductionSystem`, `LabourMarket`) already run the whole region's economy abstractly. Phase 5 lets the hero **own** them: found one in a building he holds, buy one, buy a share of one, run it with his own hands, or put a steward in charge and leave. It gives the region's idle adults day labour, wires the stall and fair takings Phase 3 left open, and gives the player the Ledger to see it all. Nothing here creates coin or goods: every crown is a `Ledger` transfer and every good a store move.

## 2. Dependencies
| Needs | From |
|---|---|
| `Business` (owner, cash box purse, store, stations, staff, owed wages, season books, `Capital`, dividends at S5: 50% above a 15-day reserve; closure after 3 loss seasons), `BusinessKind` (stations, staff slots, recipes), `ProductionSystem` (plan by margin, batches, buy orders, sale lots), `LabourMarket` (hiring by wage offer, apprentices, quits after 3 short days) | exists (Phase 2) |
| Wage classes 150 / 350 / 500 mites a day; steward wage 350 | exists (`taxes.jsonc`, production) |
| Market books, shop lots, `TendShop` odd job, `FairBook` | exists (Phase 2-3) |
| Deeds, stall leases | `land-and-deeds.md` |
| Workshop stations from recognised rooms | `roomdetector-v2.md` 4.4 |
| One recipe format, live staff at stations, commissions | `crafting-full.md` |
| Construction sites (for the Ledger and day labour) | build spec |
| Autopilot directives (`Tend` stub) | exists (Phase 3) |
New: `Production/Ownership` (`ShareBook`, valuation), `Production/StewardBrain`, `Production/DayLabour`, `Production/StallTrade`; Godot `UI/LedgerScreen`. `Occupation.Steward` exists; no enum changes beyond appends: `OddJobKind` (+`DayLabour`), `TransferReason` (+`ShareSale`, appended, saves pad as the `Alms` append did).

## 3. Data

### 3.1 Additions to `Business`
| Field | Unit | Meaning |
|---|---|---|
| `Shares` | list of `(holder EntityId, pm)` summing 1,000 | equity; the owner is the largest holder; control at > 500 |
| `Steward` | person index or -1 | who runs it when the owner does not |
| `Directives` | struct | the owner's or steward's standing orders (4.4) |
| `Premises` | structure ids | its buildings (deeds held by the business or the owner) |
| `HandsOn` | bool | the owner works and decides himself; the planner only suggests |
| `Season ledger` | Phase 2 books + `RevenueSeason`, `ProfitSeason` (mites) | the Ledger's columns |
| `Alerts` | flags | wages owed, no input, loss season, no staff, steward report |

### 3.2 Valuation (one formula, shown everywhere a price is asked)
`value = premises (land spec building value + plot price) + stock at bid + station items and tools at bid + cash box + goodwill`, `goodwill = max(0, mean profit of the last 4 seasons) x 2` (two seasons of profit; a loss-making business has none).

## 4. Rules

### 4.1 Founding, buying and selling
- **Found:** Q on a building the hero may use (land spec 4.1) > Open a business: the kinds whose stations stand in the building's recognised workshop rooms (RoomDetector v2), e.g. a smithy room with a forge and an anvil offers `smithy`. Capital is what the hero moves into the new cash box (`Gift` to the business purse; `Capital` records it, as Phase 2). Station count = stations in recognised rooms (more forges, more batches; never more output per input).
- **Buy:** any open business whose owner household would sell (the owner is dead, old over 60 with no heir in the trade, or the business lost money 2 seasons in a row), at its value; or a closed business (Phase 2's "offered to the highest-skill local worker" now also offered for sale at value x 700 pm). Price `Trade` buyer -> seller household; the deeds of its premises transfer with it (land spec).
- **Shares:** an owner sells up to 490 pm of his business (he keeps control) at `value x share`; the hero buys from the business's notice (Q on the owner > Buy a share). Dividends at S5 go to every holder pro rata (Phase 2's 50% above reserve). A shareholder sees the business in the Ledger read-only. Selling shares back: the owner buys at value if his household holds the coin, else the share is listed.

### 4.2 Running it hands-on
The hero (or a follower he assigns) works its stations through crafting (output to the business store, `crafting-full.md` 4.4); hires and fires from the region's people (Q on a person > Hire for <business>: an offer at a wage he sets, accepted by the labour market's rule: a better wage than now, or idle); sets recipes (a list with "make N a day" or "by margin", the Phase 2 planner as the default suggestion), buy limits for inputs (posted as buy orders), floor prices for outputs (shop lots never below), and the wage offer. **Supplying inputs** is moving goods into the business store (E on its chest, or unloading a cart there: plan done-when "supply iron"). The business's station tools are real item rows that wear (items spec 4.3).

### 4.3 Workers, workplaces and day labour
Staff have the business as `Employer` and its premises as `WorkLot` (Phase 1 fields). In the live bubble they go to the stations at their schedule hours (06-16, daylight) and do the queued steps live (crafting 4.4); otherwise the abstract tier credits hours as Phase 2. **Day labour (new):** every settlement holds a hiring hour at 06:00 in the square: posts from construction sites, farms short of hands (Phase 2 labour planner), businesses with a one-day need and haulage, all as `DayLabour` odd jobs at the unskilled wage (150 mites, 10 hours). Idle adults aged 16-59 without a staff post take them first come by `(distance, person id)`. This is the plan's "workers change jobs by being hired" applied by the day; a worker on 10 day-labour days in a season becomes a candidate for a staff post with priority. Soak target: landless idle adults at year 100 under 800 (Phase 3 measured 2,101; overview 5).

### 4.4 Stewards
- **Hire** a person with `Trading >= 30` and `Leadership >= 20` (Q > Hire as steward; wage 350 mites a day, skilled, from your purse or the business cash box). A steward runs up to `3 + Leadership / 20` (max 6) properties: businesses, construction sites (he hires crews and keeps materials coming), leased fields (farm labour), stalls.
- **What he does** (`StewardBrain`, daily in the business's H4 slice): Phase 2's planner (margin per hour) on the business; buy orders for inputs within the season budget; the wage offer rule; pays wages first, then inputs, then **sends profit**: everything above the reserve (15 days of costs) and above the owner's `keep_mites` goes to the owner's purse each 5 days (a `Dividend` transfer; to the home chest store if the owner is away, so coin never travels by magic: it sits in the chest, the Ledger says where).
- **Skill matters, not luck:** the steward plans with prices that are `lag_days = max(0, 10 - Trading/10)` days old (a novice uses last week's prices; Trading 100 uses today's), and keeps `reserve x (1500 - 5 x Leadership)/1000` (careless stewards hoard cash or run dry). No random draw; honesty and skimming are Phase 6 (stream 0x540 reserved).
- **Directives** (the owner sets; the Ledger edits them): `maximise profit` (default), `keep stock of <good> >= n`, `never sell <good> below p`, `season budget`, `send profit every n days` (5 default), `keep_mites`, `hire up to n`, `close if loss 2 seasons`. **Reports:** a weekly line in the Journal and Ledger alerts.
- The **autopilot `Tend` directive** (Phase 3 stub) is the hero doing the same work himself at one property: go there, work its stations or site, run the steward rules for decisions, rest by schedule.

### 4.5 Stalls, shops and fairs
A leased stall (land spec 4.3) or a shop counter in a building the business holds lets it **sell**: goods in the stall's store become shop lots on the settlement's book at the owner's ask (default the book's ask), sold to NPC shoppers by the existing market rules (households buy at the session; `ask` per unit as Phase 2). Someone must mind it: the owner, a staff member, or a `TendShop` job (Phase 3), else nothing sells. **Fair days** (Phase 3 carried limit): `FairBook` raises footfall on its days (`stall_demand_pm` 1,500 for the town's shoppers, plus visiting buyers' demand from the continent tick's fair trade), the town charges `stall_fair_day_mites` and a 5% market toll on takings (existing `MarketToll` reason). Shops sell what workshops actually made: an item row sells at `ItemTable.ValueMites` scaled by the book's price level; **items abroad** (Phase 3 carried limit): `AbroadDealing` accepts item rows at the same value, so a caravan can carry swords.

### 4.6 Profit while away (the done-when)
With the owner outside the region's bubble, the business runs abstractly (Phase 2) with the steward's or owner's directives; dividends accrue as 4.4. The Ledger on return shows each season's revenue, costs and profit and where the coin is (cash box, chest, purse). **Example (golden BS-9, prices pinned at the solver's):** a smithy with one smith (skill 50) on `forge_nails` (10 kg iron -> 9 kg nails, 20 worker-hours) makes half a batch a day; at the solver's prices each batch earns 45% of its wage cost (Phase 2 3.6: 20 h x 35 = 700, profit 315), so a season of 15 days earns **2,362 mites** before rent and the steward's wage. With a steward (350 a day = 5,250 a season) one smith loses money: the Ledger says so, and the hero learns that stewards pay off on larger businesses (3 smiths: 7,087 profit, 1,837 after the steward).

### 4.7 Tick placement
| Where | What |
|---|---|
| Live 20 Hz | staff at stations (crafting), stall minding |
| Barrier | found, buy, sell, shares, hire, fire, directives, steward hire |
| H2 | abstract work (Phase 2) |
| H4 slice (business index mod 24) | plan, orders, wages, steward rules, profit sending, alerts |
| H5 06:00 per settlement | day-labour hiring hour |
| S5 | dividends to shareholders, season books close, valuation update |

## 5. Determinism
No new random draws (0x540 reserved for Phase 6 steward honesty; 0x541 reserved for fair visitor jitter, asserted unused unless the fair demand table needs it). Ties: persons by `(distance, id)`, businesses by index. All mites integer; shares permille with the remainder to the largest holder.

## 6. UI and controls: the Ledger (L)
A modal screen (context `Ledger`, overview 6A) with tabs **Property, Businesses, Sites, Fields, Stalls, Vehicles and animals, Shares, Stewards**; each a sortable table (Up/Down rows, Left/Right column, S sort, `/` search, A alerts only, Enter opens the row's detail, Tab next tab, Esc back). Columns at 960 x 540 (6 visible, 14 px rows, horizontal scroll for the rest): Businesses: name, place, cash, profit last season, staff / slots, alerts; Sites: place, % done, missing materials, crew, escrow days, state. Detail panels: the business's recipe list with margin per hour, directives editor, staff list (hire, fire), season history (4 seasons), "Where is the money?". Alerts also show as HUD toasts (one per alert kind per day). The Ledger appears when the hero first holds a deed, a share, a lease or a site (progression: "householder unlocks B and L").

## 7. Tests (golden)
| id | Case | Expected |
|---|---|---|
| BS-1 | Found a smithy in a room with 2 forges and 1 anvil | kind `smithy`, stations from the room; capital transfer exact |
| BS-2 | Valuation: premises 50,000, stock 8,000, tools 3,000, cash 4,000, profits 2,000/1,500/2,500/2,000 | goodwill 4,000; value 69,000 |
| BS-3 | Buy a share of 400 pm at value 69,000 | 27,600 mites to the owner household; S5 dividend of 3,000 -> 1,200 to the hero, 1,800 to the owner |
| BS-4 | Control: a sale that would leave the owner at 500 or less | refused |
| BS-5 | Hands-on: the hero forges nails at his smithy's anvil | nails in the business store; inputs from it; the batch counted in the season books |
| BS-6 | Hire: offer 400 a day to a smith employed at 350 | he quits his post at the barrier and joins; his old employer posts the vacancy |
| BS-7 | Supply iron: unload 200 kg of iron bars from a cart into the business chest | store +200 kg; no coin moves; the planner starts nail batches |
| BS-8 | Steward lag: Trading 40 | plans with 6-day-old prices; Trading 100: 0 days |
| BS-9 | Profit while away (prices pinned): one smith, `forge_nails`, 15 days | profit 2,362 mites; 3 smiths 7,087; steward wage 5,250 shown against it |
| BS-10 | Profit sending: reserve 15 days of costs, `keep_mites` 1,000, every 5 days | the excess to the owner's purse, or to his home chest while away; coin exact |
| BS-11 | Wages short: cash for 1 of 2 staff | paid in ascending person index; the other owed; quits after 3 short days (Phase 2 rule unchanged) |
| BS-12 | Day labour: 40 idle adults, 25 day posts | 25 hired by (distance, id); the rest idle; each paid 150 at day end |
| BS-13 | Stall: lease, stock 20 kg of nails, minded 8 hours on a market day | sold units at the book's per-unit ask; 5% toll; unminded: no sales |
| BS-14 | Fair day: same stall | footfall x1.5; the 150 fair-day fee charged |
| BS-15 | Item rows abroad: a caravan sells 3 swords | value by `ItemTable.ValueMites`; coin and items conserved |
| BS-16 | Tend directive: autopilot hero tends his stall 8 h | equal to BS-13 with the hero minding |
| BS-17 | Ledger: 200 rows, sort by profit, alerts filter | sorted by `(profit desc, id)`; opens in <= 15 ms |
| BS-18 | Soak: 100 years with day labour | landless idle adults at year 100 < 800; every Phase 2 and 3 soak gate still met; coin exact |

## 8. Open questions (recommended defaults)
1. **Steward honesty** (overview Q5): honest in Phase 5; skill decides quality; skimming arrives with Phase 6's corrupt officials.
2. **Goodwill** = two seasons of mean profit. Default as written.
3. **Shares in NPC businesses capped at 490 pm** (owners keep control). Default; buying control means buying the business.

## 9. Budgets
Steward rules per business <= 0.05 ms in its slice; day-labour hour <= 0.2 ms per settlement (<= 6 ms a region, at hour 6, away from the heavy hour 0); Ledger build for 200 rows <= 15 ms (once on open, then incremental); save: shares 12 B each, directives 48 B, steward 8 B per business, day-labour jobs transient (not saved past the day).
