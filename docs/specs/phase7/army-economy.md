# Army economy: the arms industry, army supply, purveyance, scutage and campaign finance, and the interface the military calls

Status: design, written before any Phase 7 code, on top of b6b39e2. Plan refs: the Soldiers' equipment row ("Arms industry and army supply (Phase 7): armouries stockpiling in peace; munition-grade mass production in large workshops with many hands; the arms trade through the continent; loot recycled through repair and refit; army food from granaries, food rents, purveyance, merchants following the army and foraging"; "who arms them: a historical mix: levies bring gear by their wealth; retinues and mercenaries are armed from their lord's armoury"); §3d (supply: 1 kg of grain a soldier a day, 1,000 men for 15 days 15 t, about 30 carts; foraging strips the acres the army passes; wages from the treasury chest; unpaid or starving armies desert, deserters become bandits; raiding a supply train; levies 1 per 10 households in peace, 1 per 5 in war, each missing from the harvest; starvation sieges on the town's real stockpile; armies muster after sowing and hold for the harvest unless the war is worth the shortfall); §3j (purveyance at a set price paid late or never, grievances and unrest, a chronicle cause; armies otherwise supplied by merchants, baggage trains and foraging on enemy land; military service or scutage; loans move existing coin; treasuries are physical, a tax wagon is a caravan); the Phase 6 hand-offs (`docs/reviews/phase-6.md`: the purveyance hook in unrest, the levy's evaded draft, the riot trigger; the rule's stream 0x660 reserved for purveyance; `DiseaseBook.ArmyCampSize`; "the vassal's service owed" left for Phase 7).

Money in mites (1 cp = 10, 1 sp = 100, 1 gp = 1,000). Food in grams of staple (1 measure = 1,100 g of grain). A game year is 60 days; seasons spring, summer, autumn, winter.

## 1. Purpose
An army is the biggest customer the economy has. This spec is the economy's side of war: what a host eats, is paid and is armed with, where each comes from and what it costs the land it passes; how rulers raise the money; how an arms industry fills armouries in peace and recycles loot in war. Section 3 is the contract with the military specs: the calls a host makes each day and what it gets back. The economy never moves soldiers; the military never moves coin or goods except through these calls.

## 2. What exists and what is new
| Exists (by file) | Used for |
|---|---|
| `Taxes/TaxSystem.LevyQuota` (1 in 10 households in peace, 1 in 5 in war; data only), `TaxState.LevyDue` | the muster count |
| `Rule/*`: `DueRow` kinds (Service: a manor 1 man 7 days, a fief 1 man per 50 households), `RuleLoans` (existing coin only, 4% a season, security, default), `RuleCouncil` (subsidies with cause weights), tallage, officials and their skimming, `RuleBook` receipts | scutage, campaign finance, purveyance's officers |
| `Unrest/*`: `UnrestBook.Add(hh, "purveyance", day, entry)` (2 points for 30 days; the content row exists), `UnrestEngine.Evades`, `UnrestConcede.Trigger(book, settlement, "tallage", day)` | purveyance's consequences |
| `Politics/WarUtility`, `Diplomacy` (wars, tribute, ransoms), `PoliticsCtx` (a realm's host from history's levy) | the AI's funding input |
| `Farming/*` (parcels, standing crops, harvest windows, the labour planner), `Rule` food rents into the lord's barn (RI-2), granaries | food rents, forage, harvest risk |
| `Economy/Markets/*` (books, per-unit repricing, the ask's rise as cover falls), `Caravans/*` (convoys are caravans; `CaravanAmbush`) | buying, convoys |
| `Disease/*` (`ArmyCampSize` hook, unfed), `People/*` (households' needs, hunger, `Demography.Kill`) | camp sickness, siege starvation |
| `Crafting/*` (repair to the tier's cap, refit, salvage), `Goods/Corrosion`, `ItemTable` (condition) | loot recycling |
| `coinage.md` (gold in pay chests), `economies-of-scale.md` (supply contracts, munition patterns), `large-buildings.md` (armoury, granary depot, barracks, warehouses), `industries.md` (iron output) | used throughout |
| **New** | `Sim/WarSupply/` (`HostSupply` and its records, `SupplyLadder`, `Purveyance`, `Forage`, `Sutlers`, `Depots`, `Scutage`, `CampaignFinance`, `ArmsOrders`, `LootRefit`, `SiegeLarder`, `AbstractHostSupply`), `Save/WarSupplySave.cs`; content `war/supply.jsonc`, `war/pay.jsonc`, `crafting/recipes_munition.jsonc`; the Army tab's Supply view (the tab is the military's) |

## 3. The interface the military calls
**Ownership boundary.** The economy owns coin, goods, stores, settlements, households and their labour. The military owns soldiers as people with troop roles, loadout templates, morale, desertion decisions, movement and battles. Each side changes the other's state only through these calls. All calls run on the sim thread at the host slot or at a tick barrier, are deterministic (hosts by id, sources in a fixed order) and allocate nothing per call after warm-up.

### 3.1 Registering a host
```
HostId Register(HostSpec spec)
record HostSpec(int Realm, EntityId Lord, PurseId PayChest, StoreId Baggage,
                Roster Men, int Followers, AnimalCounts Animals, VehicleIds Carts, SupplyPolicy Policy)
record SupplyPolicy(bool Purveyance, ForagePolicy Forage /* None, OwnLandPaid, EnemyLand */,
                    int[] Depots, int BuyRadiusM = 8000, bool Plunder = false)
void Update(HostId h, HostPlace place /* road node or tile, region or continent */, HostMode mode /* March, Camp, Siege, Garrison, Battle */, Roster men)
void Disband(HostId h)
```
The pay chest is a `Chest` purse carried in the baggage (opted in for gold, `coinage.md` 5.2); for an abstract host outside every detailed region it is the realm's treasury in the continent tick. `Roster` counts men by troop role (the military's roles: levy, spearman, archer, man-at-arms, knight, mercenary, captain, quartermaster).

### 3.2 What a host asks each day, and what it gets
```
DayNeeds  Needs(HostId h, int day)    // pure
DayResult Supply(HostId h, int day)   // runs the ladder (4.1), moves coin and goods, returns what it got
```
| `DayNeeds` field | Rule | Unit |
|---|---|---|
| `StapleG` | men x 1,000 + followers x 800 | g of staple (grain, flour, bread at their food measures) |
| `RelishG` (optional) | men x 150 (salted meat, fish, cheese, pulses); short of it: morale only | g |
| `FodderHayG`, `FodderOatsG` | each animal at its working rate (`animals/species.jsonc`: a riding horse 8 kg of hay and 2.5 kg of oats, an ox 10 kg of hay); hay halved while camped on pasture in summer and autumn | g |
| `FirewoodG` | (men + followers) x 1,500, x2 in winter | g |
| `PayMites` | sum of the roles' daily wages (`war/pay.jsonc`, 4.11) | mites |

| `DayResult` field | Meaning |
|---|---|
| `Delivered[need][source]` | grams (or mites for pay) by source: `Baggage, FoodRent, Depot, Market, Sutlers, Purveyance, Forage, Plunder` |
| `ShortG[need]` | what was not found |
| `Paid[role]`, `Arrears[role]` | mites today, cumulative arrears |
| `HungerDays[role]`, `UnpaidDays[role]` | consecutive days short of staple or pay: **the morale and desertion inputs** |
| `DaysOfFood`, `DaysFunded` | baggage staple / today's `StapleG`; pay chest / today's `PayMites` |
| `Events` | the chronicle entry ids written today (purchases that moved a price, purveyance, forage, a convoy raided), for the military's causes |

### 3.3 Men, kit and the field
```
MusterResult Muster(HostId h, int fief, int men)        // levies: picks the men, marks them on campaign, returns persons and the kit they bring
void Release(HostId h, IReadOnlyList<int> persons)       // home again: labour returns to the household
void Desert(HostId h, IReadOnlyList<int> persons)        // the military's decision; the economy settles it (4.11)
KitFill Kit(HostId h, LoadoutTemplate t, int count)      // fill from the lord's armoury view (large-buildings 4.4): best available, gaps listed
void ReturnKit(HostId h, IReadOnlyList<ItemId> items)
void Loot(int battle, int holder, IReadOnlyList<ItemId> fieldItems)   // the side holding the field: sorting and refit (4.10)
```
**Muster** picks the levy from the fief's households in a fixed order (households with a fit man aged 16-50, by household id, one man each, the head last), sets the person's campaign flag (his household loses his labour for farming, business and day labour while he serves; a staff post is held for 60 days) and returns the kit his household's wealth provides (4.10). A household that evades (`UnrestEngine.Evades`, rung 2, aggrieved, stream 0x682) gives no man that muster.

### 3.4 Settlements and the land
```
void SiegeBlockade(int settlement, bool on, HostId besieger)   // 4.8
int  DaysOfFood(int settlement)                                // staple stores / (people x 1,100 g)
long HarvestRisk(int fief, int menAbsent, int days)            // kg of harvest lost if those men miss those days of the harvest window
int  DaysFunded(int realm)                                     // 4.5, read by WarUtility
CampaignBudget Budget(int realm, Roster plan, int days)        // 4.5
void Camp(HostId h, int tile)                                  // feeds DiseaseBook.ArmyCampSize
```

### 3.5 Timing
`Supply` runs once a game day for every host at **the host slot**: hour 18, after the H5 daily globals (households' needs, taxes, unrest), in host id order. Live hosts are seen eating at their camp's meals; the accounting is the slot's. Abstract hosts (the continent tick's realms, regions not detailed) call `AbstractHostSupply` with the same numbers on the settlement summaries (a realm's grain index, treasury delta), so a host moving between tiers keeps its counters (AE-21).

## 4. Rules

### 4.1 The supply ladder
Each day each need is filled from the sources in this order until met:
1. **Baggage**: the host's store (carts' cargo).
2. **Food rents**: the host's lord's own barns within the buy radius give freely to his own host (the barn's goods to the baggage; the lord's store, so no coin).
3. **Depots**: granary depots of the host's side named in the policy, within 8 km: delivered the same day; beyond, the quartermaster sends a **convoy** (a Phase 2 caravan of the host's carts, raidable) and it arrives when it arrives.
4. **Market**: the quartermaster buys at the asks of the books within the buy radius (per-unit repricing: buying 2 t a day raises the price; the price entry names the army as its cause).
5. **Sutlers**: the camp market (4.7).
6. **Purveyance**: if the policy allows and the host is in its own realm (4.3).
7. **Forage**: if the policy allows (4.2).
8. What is left is `ShortG`.
Pay is paid from the pay chest only (4.11). Goods bought are paid from the pay chest; a chest short of coin buys less.

### 4.2 Foraging
- **Foragers** are 10% of the men; each brings in up to 30 kg of staple-equivalent a day (x 0.8 to 1.2 by stream **0x750**, key tile, day) from within 4 km of the camp.
- **What they take, in order:** barns and household stores (on enemy land this is plunder: standing -40 with the host's realm for every household robbed; on own land it is paid at the king's price as purveyance, 4.3); then standing crops: in autumn, ripe grain cut roughly (half of the acre's yield taken, the other half `DestroyReason.Trampled`: an acre of 52.8 kg gives 26.4); in summer, green crops as fodder (1 t of hay-equivalent an acre, the crop lost); in winter, stores only.
- **Records:** each parcel stripped (the farming state's crop goes to the host or to `Trampled`), acres a settlement lost, chronicle `army_foraged` per settlement and day.
- A host of 2,000 foraging ripe fields strips about 227 acres a day (6,000 kg at 26.4 an acre): the farms of about a hundred people. That is the plan's "foraging strips the acres the army passes".

### 4.3 Purveyance (own realm)
- **Who decides:** the ruler (the player as ruler on the Fief tab; an NPC ruler when markets have filled less than half of a host's staple for 2 days and the treasury holds less than 30 days of its pay).
- **Who takes:** the realm's officials (sheriff, bailiffs, the Phase 6 offices) at settlements within 16 km of the host or its depot.
- **What:** a household's grain above 30 days of its own need, hay above 15 days of its animals' need, one cart and one draught animal from households with two or more; lords' and towns' barns above the same reserves. Price: the **king's price**, 750 permille of the book mid.
- **Paid late:** by a tally, `PurveyanceTally { Id, Household, Realm, AmountMites, TakenDay, DueDay = TakenDay + 30, Paid, State (Owed, Paid, Defaulted) }`. At each S5 the realm pays tallies past due while its treasury holds more than one season of its payroll (`TransferReason.Purveyance`, appended); a tally 4 seasons past due is `Defaulted`: the household's grievance is added a second time and the chronicle writes `purveyance_unpaid`. Carts come back at `Disband` with 10% wear; 80% of the animals come back (the rest died or were sold; their tallies stand).
- **Hiding:** each household hides grain with P = `20 + unrest / 20` % (stream **0x660**, the Phase 6 reservation, now drawn; key household, day): it gives half. Officials skim by the Phase 6 rule on the value they handle.
- **Consequences:** `UnrestBook.Add(hh, "purveyance", day, entry)` for every household taken from; a settlement at unrest >= 350 when purveyed: `UnrestConcede.Trigger(book, settlement, "tallage", day)` (a riot). Chronicle `purveyance_taken` (a settlement's day: kg, carts, animals, tallies), caused by the host's campaign entry.

### 4.4 Military service and scutage
- When an overlord calls his host, each vassal's **Service** due (Phase 6 `DueRow`) becomes `men x war_service_days` (40) for that campaign year; the 7 days of peace guard duty stay as Phase 6 has them.
- The vassal **serves** (his men join the host as levies or retinue of his fief) or pays **scutage**: `men x days x 300` mites (1.2 x a soldier's 250, enough to hire a replacement), `TransferReason.Scutage` (appended), to the overlord's treasury, which the military's mercenary hiring spends.
- **NPC vassals** serve when `opinion of the overlord + Range(-10, 10) >= 0` (stream **0x753**, key vassal, season) and the household has a fit man; else they pay; unable to pay: the due goes to `Arrears`, then `Grievance` (Phase 6: opinion, casus belli). The player as vassal chooses on the Fief tab.
- Example: a manor (1 man) and a fief of 400 households (8 men): 9 men x 40 days: serve, or pay 108,000 (108 gp).
- Chronicle `scutage_paid` (one entry a season per overlord, listing payers and sums).

### 4.5 Campaign finance
- **Budget:** `daily cost = pay + staple x the region's grain mid + fodder at book + carting`; `DaysFunded(realm) = (treasury + pay chests + scutage due + subsidies granted + the loan room the lenders offer) / daily cost`. The ruler's campaign panel shows it; `WarUtility` reads it (start a campaign at >= 30 days funded; below 10, seek a loan, a subsidy or peace).
- **Sources:** the treasury (receipts by source, Phase 6), a council's subsidy (cause weights +40 defending, +10 with a casus belli), tallage, scutage, **loans** (`RuleLoans`: existing coin only, 4% a season, security future customs or taxes; lender ties on stream 0x663 as Phase 6), ransoms, loot sales, tribute.
- **The pay chest is physical:** coin moves from the treasury to the chest (a transfer) when the host musters, and by convoy when it runs low; a treasury sending more than 100 gp changes it to gold at a changer if one is in its town (`coinage.md` 5.4), so a chest of 200 gp weighs 220 g instead of 2.6 kg. A captured baggage train gives its chest to the captor (`TransferReason.Plunder`, appended), gold pieces with it.
- **Example cost:** a host of 2,000 (1,040 levies in their first 40 days unpaid, 300 spearmen at 200, 160 archers at 250, 60 men-at-arms at 350, 40 knights at 500, 400 mercenaries at 300) costs 261,000 mites a day in pay, plus 2,080 kg of staple (93,600 at 45 a kg) and fodder for 300 horses (36,900): **391,500 a day, about 392 gp**. A realm treasury of 5,000,000 funds 13 days. Campaigns need subsidies and loans; that is intended.
- **Loans only move coin** (RI-13 stays the gate).

### 4.6 Granaries and depots in peace
A lord's **granary depot** (`large-buildings.md` 4.4) has a reserve target of `levy war quota x 30 days x 1 kg`; at each harvest the steward fills the gap from food rents (RI-2) and the harvest market's low prices, and rotates grain older than 2 seasons. Depots are physical: raided, burned (Phase 6 fire), purveyed by an enemy. Chronicle `depot_filled` when a depot first passes its target in a year, `depot_emptied` when a war empties it.

### 4.7 Merchants following the army (sutlers)
A host of >= 200 men camped 2 days or more (or marching in its own realm) draws sutlers: merchant households within 16 km holding >= 50 sp and stock of staple, ale, relish or kit join with P = 10% a day each when their expected margin at the camp's prices (ask x 1,300 permille) is >= 20% (stream **0x751**, key host and day), at most one per 100 men, with at most a cart each. They sell to the quartermaster (ladder step 5) and to soldiers spending their own wages; the camp has a **camp market** (a temporary market book over the sutlers' stock) where the hero can trade. They leave when the host is beaten, pay is 3 days in arrears, or their stock is out. Chronicle `sutlers_joined` when the first joins a host.

### 4.8 Sieges starve towns
`SiegeBlockade(on)`: the settlement's market book takes no imports and sends no exports; caravans and the export desk skip it; households cannot leave except by a sally or an escape the military rules; daily needs go on from the town's real stores (households, granaries, merchants' stock). `DaysOfFood(settlement)` is shown to both sides. Below 5 days the chronicle writes `siege_starving`; hunger and deaths follow by the existing households' and population rules. The besiegers forage within 4 km (4.2). Lifting the blockade reopens the book.

### 4.9 Levies out of the harvest
A mustered man's household loses his labour: the farming labour planner's worker-hours for that household drop by 10 hours a day. `HarvestRisk(fief, men, days)` estimates `men x min(days, days left in the harvest window) x 10 h x the region's harvest kg per worker-hour` for the AI's "hold for the harvest unless the war is worth the shortfall" (`economy-audit.md` WL-6 measures it).

### 4.10 The arms industry
- **Who arms whom** (decided): levies bring kit by their household's wealth: movables >= 200 sp, a spear, a kettle hat, a shield and a gambeson from the household's items if it has them, else bought at the muster market; poorer households bring a spear at most; the rest is filled from the lord's armoury. Retinues and mercenaries are armed from the armoury (mercenaries' own kit counts first).
- **Armouries stockpile in peace:** `kit_target = retinue + garrison + armed share of the war levy` (the share whose households are below 200 sp; about 40-60% in a peasant region). At each S5 the lord's steward (or the player on the armoury panel) orders the gap by **supply contracts** for munition patterns (`economies-of-scale.md` 4.3, 4.6) within `arms_budget_pm` 100 of the lord's last year's receipts; NPC orders go to the nearest smiths, manufactories and armourers, then to imports. Chronicle `arms_ordered` (seasonal, per lord).
- **Munition kit** (`crafting/recipes_munition.jsonc`, pattern hours of `economies-of-scale.md` 4.6): spear (0.4 kg iron head, 1.8 kg ash shaft), kettle hat (1.5 kg iron), shield (4.5 kg planks, 0.3 kg iron, 0.5 kg leather; carpentry 3 h), gambeson (3 kg cloth, 1 kg wool; tailoring 12.4 h with practice), bill (1.2 kg iron), longbow (0.8 kg yew stave; bowyer 6 h), sheaf of 24 arrows (0.24 kg iron heads, shafts, goose feathers), mail shirt (10 kg iron wire; 155 h with practice). A levy kit of spear, hat and shield is 2.2 kg of iron and about 6.4 smith-hours: 960 kits are 2.1 t of bar and 6,100 smith-hours (a region's 90 smiths: about 7 days if they do nothing else), and 11,900 tailoring hours for the gambesons (the slowest part).
- **The arms trade:** the continent tick tracks `arms` as a goods class (`ContinentRules.TrackedGoods` gains it) with a price index per realm; a realm at war has its arms demand x3; each season the tick picks trading realm pairs for arms caravans (stream **0x754**, key realm pair, season); a ruler may ban arms exports to an enemy realm (`ArmsExportBan`, a policy on the Fief tab, read by customs). An L2 or L3 iron works and a manufactory hall find their buyers here (`industries.md` 5.11).
- **Loot recycled:** `Loot(battle, holder, items)` sorts each item by its condition after stripping (the stripping costs 0 to 10% more, stream **0x752**, key item, battle): >= 600 permille to the holder's armoury (or sold), 200-600 to the **refit queue** (Phase 5 repair to the tier's cap by the armoury's armourer or by commission), < 200 to **salvage**: iron items to `iron_scrap` (700 permille of their metal), forged to bar by `forge_scrap` (10 kg scrap + 6 kg charcoal -> 8 kg bar, 8 h at a forge); leather and cloth to rags (`Worn`). Prisoners and ransoms are the military's (Phase 4 and 6 rules).

### 4.11 Pay, arrears and desertion
- **Wages** (`war/pay.jsonc`): levy 0 for the first `levy_free_days` 40 days of a year's service, then 150; spearman 200; archer 250; man-at-arms 350; knight 500; mercenary 300 (or the company's contract); captain 500; quartermaster 350. Paid each day at the slot from the pay chest in this order: mercenaries, knights and men-at-arms, the rest; what the chest cannot pay is arrears by role.
- **Desertion is the military's decision** from `HungerDays` and `UnpaidDays`. When the military calls `Desert`: the men leave; levies keep their own kit; retinue men take their armoury kit (a theft, the crime rule's record with the host's lord as victim); arrears to deserters are forgiven; deserters who do not reach home within 3 days become bandits (the military's and crime's rules).
- `Disband` pays what the chest can of the arrears, returns armoury kit (`ReturnKit`, with wear), releases levies, returns purveyed carts and animals, and leaves unpaid tallies and arrears owed by the realm (arrears to the household as a due: unpaid, a grievance).

## 5. Content
`war/supply.jsonc`: `ration_g` 1,000, `follower_ration_g` 800, `relish_g` 150, `firewood_g` 1,500, `forage_share_pm` 100, `forage_kg` 30, `forage_radius_m` 4,000, `buy_radius_m` 8,000, `depot_same_day_m` 8,000, `purveyance_radius_m` 16,000, `king_price_pm` 750, `reserve_grain_days` 30, `reserve_hay_days` 15, `tally_due_days` 30, `tally_default_seasons` 4, `hide_base_pct` 20, `war_service_days` 40, `scutage_mites_per_man_day` 300, `levy_free_days` 40, `sutler_join_pct` 10, `sutler_markup_pm` 1,300, `sutler_per_men` 100, `arms_budget_pm` 100, `armed_levy_wealth_mites` 20,000, `loot_keep_pm` 600, `loot_refit_pm` 200, `scrap_metal_pm` 700, `campaign_start_days_funded` 30, `campaign_seek_funds_days` 10. `war/pay.jsonc`: the wages of 4.11. Recipes: the munition patterns, `forge_scrap`. Goods: `iron_scrap`, the kit pieces (as item shapes and goods for trade), `arms` as a continental class.

## 6. Saves
- Optional section **`warsupply`**, absent while no host has existed: hosts' supply accounts (`HostId`, realm, lord, the chest and baggage ids, policy, arrears and hunger and unpaid days by role, convoys in flight), purveyance tallies, persons on campaign (by host; no key is added to the people section), sutlers attached, the refit queue, blockades, arms orders not yet placed as contracts, depot targets. `SupplyHash` joins the trail once non-empty.
- **Additive enums** (append only, saves pad counts as for `Alms`): `TransferReason` + `Purveyance`, `Scutage`, `Plunder` (and `Exchange` from coinage); `DestroyReason` + `Waste` (industries), `Trampled`; `CauseKind` + `Industry` (industries), `Supply`; the rule's due kinds + `Scutage`.
- No version bump.

## 7. RNG streams
| Stream | Use | Key |
|---|---|---|
| 0x660 | purveyance hiding (the Phase 6 reservation, now drawn; its "asserted unused" test is rewritten to "unused before a purveyance") | household, day |
| 0x663 | campaign loans' lender ties (Phase 6's stream, reused as written) | borrower, season |
| 0x682 | a levy's evaded draft (Phase 6 unrest's stream, reused) | household, season |
| 0x750 | forage yield | tile, day |
| 0x751 | sutlers joining | host, day |
| 0x752 | loot condition after stripping | item, battle |
| 0x753 | a vassal's choice to serve or pay | vassal, season |
| 0x754 | arms caravans between realms in the continent tick | realm pair, season |
Free: 0x755-0x77F. The military writer's block is 0x780-0x7FF.

## 8. Chronicle
| Type | Cause | Root | Key | Parameters |
|---|---|---|---|---|
| `levy_mustered` | War | no (the war's call) | `ch_war_levy` | fief, men, evaded |
| `purveyance_taken` | Supply | no (the host's campaign) | `ch_war_purveyance` | settlement, kg grain, kg hay, carts, animals, tallies (`money`) |
| `purveyance_unpaid` | Supply | no (the tally's `purveyance_taken`) | `ch_war_tally_default` | realm, settlement, owed |
| `scutage_paid` | Supply | yes (seasonal) | `ch_war_scutage` | overlord, payers, total |
| `army_foraged` | War | no (the host) | `ch_war_forage` | settlement, acres, kg taken, kg trampled, plunder |
| `siege_blockade` | War | no (the siege) | `ch_war_blockade` | settlement, besieger, days of food |
| `siege_starving` | Famine | no (`siege_blockade`) | `ch_war_starving` | settlement, days of food |
| `depot_filled` / `depot_emptied` | Supply | yes / no (the host) | `ch_war_depot_filled`, `ch_war_depot_emptied` | lord, depot, tonnes |
| `sutlers_joined` | Trade | no (the host's camp) | `ch_war_sutlers` | host, merchants |
| `arms_ordered` | Supply | yes (seasonal) | `ch_war_arms_ordered` | lord, kits, budget |
| `pay_chest_captured` | War | no (the battle or raid) | `ch_war_chest_taken` | host, captor, sum |
| `campaign_funded` | Supply | no (the war) | `ch_war_funded` | realm, days funded, sources |
Price rises in towns the quartermaster bought from carry the host's entry as their cause (`economy-audit.md` WL-12).

## 9. UI (960 x 540)
- **Army tab, Supply view** (the tab and its layout are the military's; this view's rows are the economy's): per host: days of food in the baggage, today's supply by source (stacked bar), short needs in red, the pay chest and days funded, arrears and unpaid days by role, convoys on the road, the policy switches (purveyance for a ruler, forage policy, depots), the sutlers and a "go to the camp market" button.
- **Fief tab additions** (Phase 6's tab): purveyance tallies owed by and to the fief, scutage due and paid, the serve-or-pay choice for the player as vassal, the arms export ban for a ruler.
- **Campaign panel** (a ruler, from the Faction screen): daily cost by part, days funded by source, the council and loan buttons.
- **Armoury panel** (`large-buildings.md` 8) gains the kit target, the gap and the season's orders.

## 10. Golden tests
| id | Case | Expected |
|---|---|---|
| AE-1 | `Needs`: 2,000 men, 100 followers, 300 riding horses on summer pasture | staple 2,080,000 g; hay 1,200,000 g; oats 750,000 g; firewood 3,150,000 g |
| AE-2 | `Needs` pay for the 4.5 roster | 261,000 mites |
| AE-3 | Ladder order: baggage holds 1 day; the lord's barn in reach; a depot 12 km away; a town book | day 1 baggage; day 2 the barn; day 3 the book while the convoy travels; the convoy's arrival refills the baggage |
| AE-4 | Market buying moves the price | the book's ask after the purchase follows the per-unit repricing exactly; the price entry's cause is the host |
| AE-5 | Purveyance at a village of 40 households holding 3,000 kg above their 30-day reserve, mid 45, hiding forced off | 3,000 kg taken; tallies 101,250 in all, due +30 days; 40 grievances `purveyance` |
| AE-6 | Hiding at unrest 300 | P = 35%; a hiding household gives half |
| AE-7 | Purveyance at a settlement at unrest 360 | `UnrestConcede.Trigger(..., "tallage")`: a riot |
| AE-8 | Tallies paid and defaulted | paid at S5 past due with the treasury above a season of payroll (`Purveyance`); 4 seasons unpaid: `Defaulted`, the grievance added again, `purveyance_unpaid` |
| AE-9 | Scutage: 9 men x 40 days | 108,000 to the overlord (`Scutage`); an NPC vassal with opinion -20 pays, +30 serves (draw 0) |
| AE-10 | Forage in autumn on enemy land: 200 foragers, a barn of 4,000 kg, ripe fields of 52.8 kg an acre, draws 1.0 | 4,000 kg from the barn, then 2,000 kg from 76 acres (the last one partly cut), as much again `Trampled`; standing -40 for the robbed households |
| AE-11 | Siege larder: a town of 1,200 with 40,000 kg of staple | `DaysOfFood` 30; blockade on: no imports; `siege_starving` at 5 days |
| AE-12 | Muster: a fief of 520 households in war | 104 men; their households' farm hours fall 10 a day each; evaders give none |
| AE-13 | Pay order: a chest of 100,000 against the 261,000 roster | 100,000 to the mercenaries (of 120,000), the rest in arrears by role; `UnpaidDays` +1 for every role short |
| AE-14 | Days funded: treasury 5,000,000, scutage due 108,000, daily cost 391,500 | 13 days |
| AE-15 | Kit fill: 960 levy kits against an armoury of 400 spears, 300 hats, 200 shields | best available issued; gaps 560 spears, 660 hats, 760 shields, 960 gambesons; orders within the arms budget |
| AE-16 | Kit time and iron for 960 levy kits | 2,112 kg of iron; 6,115 smith-hours (pattern hours with practice) |
| AE-17 | Loot: 50 items, 20 at >= 600, 18 at 200-600, 12 below 200 with 30 kg of iron | 20 to the armoury, 18 to refit, 21 kg of `iron_scrap` |
| AE-18 | `forge_scrap` | 10 kg scrap + 6 kg charcoal -> 8 kg bar |
| AE-19 | Arms trade: a realm at war, a neutral realm with arms stock, the pair drawn | the belligerent's arms index rises; an arms caravan runs; an export ban stops it at the gate |
| AE-20 | Pay chest captured with 200 gp in pieces | 200,000 mites `Plunder` to the captor's purse with its 200 gold pieces; coin exact |
| AE-21 | Tier parity: one host abstract vs detailed for 10 days on the same land | staple, fodder and pay within 5%; counters carried across a tier move |
| AE-22 | Determinism and saves: a 30-day campaign with purveyance, forage, sutlers and a siege | `SupplyHash` equal; save, load, save identical at day 15; coin and mass exact |

## 11. Budgets
`Supply` <= 0.5 ms a host a day (10 hosts in a region <= 5 ms, at the host slot, never hour 0); purveyance <= 0.2 ms a settlement; forage <= 0.3 ms a host a day; an abstract host <= 0.05 ms; `warsupply` <= 80 KB a region in a war year (tallies pruned a year after they are paid).

## 12. Open questions (recommended defaults)
| # | Question | Default |
|---|---|---|
| AEQ-1 | **For the user:** how harsh is purveyance? | historical: all grain above 30 days of a household's need, at three quarters of the price, paid by a tally due in 2 seasons, defaulted after 4 |
| AEQ-2 | **For the user:** levies unpaid for their first 40 days of service in a year (the feudal obligation)? | yes; then the levy wage |
| AEQ-3 | Forage trampling | as much destroyed as taken in standing crops |
| AEQ-4 | Sutlers' markup | 30% over the local ask |
| AEQ-5 | Scutage rate | 1.2 x a soldier's wage a man-day (300) |
| AEQ-6 | Arms budget in peace | 10% of the lord's last year's receipts |

## 13. Estimate and parts
| Part | Content | Gate | Months |
|---|---|---|---|
| **P7-AE1** interface and supply | `HostSupply` (register, update, needs, supply), the ladder with baggage, food rents, depots and convoys, markets, pay and arrears, muster and release, the blockade and larder, harvest risk, camps, the abstract parity. Its first cut is built in P7-EA1 for the war-load soak; this part completes it for the military | AE-1 to AE-4, AE-11 to AE-13, AE-21 | 1.2 |
| **P7-AE2** purveyance, scutage, finance, forage, sutlers | tallies and hiding and riots, service and scutage, days funded and the budget, the pay chest's moves and capture, foraging, the camp market, the Fief tab and campaign panel | AE-5 to AE-10, AE-14, AE-20 | 1.0 |
| **P7-AE3** arms industry | armoury targets and seasonal orders by contract, munition kit recipes, who brings what by wealth, the arms trade class and bans, loot sorting, refit and salvage | AE-15 to AE-19, AE-22 | 0.9 |
**About 3.1 months of code**; AE1 is needed by the military's first armies-on-the-map part; AE2 and AE3 run beside the military's middle parts.

## Notes (P7-EA1: the interface's first cut)
`Sim/WarSupply/HostSupply` is built as far as the war-load soak needs it; P7-AE1 completes it. In: `Register`, `Update`, `Disband`, `Needs` (pure; AE-1's numbers check out), `Supply` with the baggage and market steps, `Muster`, `Release`, `Desert` (and `DesertCounts` for men who are counts, not persons), `SiegeBlockade` and `DaysOfFood`, `HarvestRisk`, `Camp` (records the camp; the disease rule's `ArmyCampSize` hook is not bound yet), `DaysFundedMilli(realm)` over the hosts' chests. `HostSupplySystem` runs `RunDay` at hour 18 in host id order (slot 1,150) and does nothing without a service in the world's bag.
- **Decisions in the code.** Wages are the overview 8.2 table (mercenary 375, captain and quartermaster 350) held in `SupplyConfig` until `war/troops.jsonc` lands; the levies' unpaid days are 10 (Q1, the user's decision). Day order of a host: staple from the baggage then the markets (books within the buy radius by distance, the cheapest good per measure first, at the asks, `Buy` with the books' clamp), then pay (the day's wages in the 8.2 order, then arrears with what is left), then hay, oats, firewood, relish. A host does not shop at a blockaded book. A levy person's pay goes to his household's purse; the rest of the pay goes to the host's soldiers' purse and is settled at `Disband`: the outsiders' share (`HostSpec.OutsideMen`) leaves with them (`Emigrate` plus the continent link's `EmigrantCoin`), the rest is shared among the households of the settlement the levies came from.
- **Muster** uses AE 3.3's simple draw (one man a household, aged 16 to 45, not the head if another can go, not on a business staff, not already on campaign) until the AM-B levy rule replaces it; the evasion test is a hook (`HostSupply.Evades`), unbound. The household's loss of labour is the farming planner's `IsEmployedElsewhere`, widened by `EconomyWorld.War`; businesses' staff are not drawn, so the 60-day post-holding rule is not needed yet.
- **Blockade.** `MarketBook.Blockaded` makes the regional carters, the caravan planner and the export desk skip the book; caravans already on the road still deliver. Migration out of a besieged town is not blocked yet.
- **Save.** `WarSupplyBook` (hosts, persons on campaign by host, blockades) in the optional `warsupply` section (`Save/WarSupplySave.cs`), hung on `WorldState.WarSupply`; a host's chest purse, soldiers' purse and baggage store are the economy section's own records (owners in reserved `Other` id spaces 4 to 6, `HostIds`). `WorldRuntime` re-installs the service when the save has the section.
- **Not yet:** food rents, depots and convoys, sutlers, purveyance, forage, `Plunder`, `Budget`, kit and loot, the pay chest's convoys and capture, chronicle entries (`DayResult.Events` is empty and the buying does not name the host as the cause of a price rise), the abstract tier (`AbstractHostSupply`). AE-1 and AE-11 to AE-13 are close to testable as they stand (`HostSupplyTests` covers AE-1, an AE-13-like pay order and the blockade); AE-2, AE-13 and AE-14 are re-derived from the overview's wage table by AE1.

## Notes (P7-AE1: the ladder, the larder, the abstract tier)
`HostSupply` is complete for the military's first armies. **Ladder:** baggage, then **food rents** (the host's lord's household barn, `FarmIds.Barn(household)`, when it lies within the buy radius of the host's place; the barn's goods go to the baggage and are eaten the same day, so no coin; the household keeps `ReserveGrainDays` (30) of its people's staple in it), then **depots**, then the **market**. A depot is a granary store at a settlement (`HostIds.Depot(settlement)`, opened with `OpenDepot`) listed by settlement index in `SupplyPolicy.Depots`; one within `DepotSameDayM` (8,000 m) of the host's place delivers the same day (staple and oats), and `depot_emptied` is written once when a war draws it to nothing. **Convoys** (`HostConvoys`, an `IOwnCaravans` beside the works' hook): after the day's fills, when the baggage holds under `ConvoyBelowDays` (3) of staple and the host has carts and none out, the quartermaster picks the nearest depot beyond a day's reach with at least a day of staple and sends `min(carts, 8, load/500 kg)` carts for up to `ConvoyLoadDays` (10) days of it. The convoy is a real caravan: the road is the region's, the drivers (one for two carts, no master) are hired from the idle men of the host's place and paid 150 a day from a float taken from the pay chest, the carts come off the host's count, the load is moved from the depot's store on arrival (up to the carts' mass and volume), and home it goes into the baggage, the remaining float back to the chest and the carts back to the host. A raid is the caravan rule's: the load goes to the hideout, the convoy and its carts are lost (`caravan_lost`). `ContinentTrade.OwnWar` is the second owner hook; the caravan is struck from the table when it comes home. **Simplifications:** a convoy sets out from, and returns to, the settlement the host stood at when it left (the baggage is the host's wherever it has gone since); the carts of a host are a count, not vehicles of the region (EA_F12 stays open: no cart is drawn from the farms); depots are not stocked by the steward (4.6 is not built: the harness or the test stocks them).
**Chronicle.** `army_bought` (cause `Supply`, a root type: its cause is the host's campaign entry `HostSpec.Campaign` when given, else the note key `ch_note_war_buying`) is written per book and day when the host's staple buying raised the ask by 5% or more; `siege_starving` (`Famine`) once per blockade when the town's days of food fall under 5; `depot_emptied` (`Supply`). `HostSupplySystem` sets `HostSupply.Chron` to the tick's outbox each day; a harness may set it to the chronicle. `siege_blockade` stays dropped (overview 8.2). **Disease.** `ArmyCampSizeOf(person)` is bound (once, when the world's disease book exists) to `DiseaseBook.ArmyCampSize`: the men and followers of a camped or besieging host, for a person on its muster or its lord. **Abstract tier.** `ToAbstract(host)` flips the account's flag: `Supply` then keeps the same needs, pay, arrears, hunger and unpaid days and totals, takes the baggage first, and buys the rest abroad at the books' mean reference price times the continent's wheat index (the ask side of the spread), paid from the chest (`Sink` as `Emigrate`, `EmigrantCoin`, goods created as `Import` and eaten); `ToDetailed(host, place, mode)` brings it back with the same counters. AE-21's ten days (200 spearmen, 50 archers, 30 mercenaries, 20 followers, 20 horses): staple 2,960,000 g and pay 737,500 in all three runs (detailed, abstract, detailed then abstract); hay differs by 1% (spoilage in the carts). **Saves.** Optional keys on the `warsupply` section: convoys (key 5), the noted larders and depots (6, 7), and per host the campaign entry and abstract flag (12). **Tests.** `HostSupplyLadderTests` (AE-2, AE-3, AE-4, AE-11, AE-12, AE-13, AE-21, a depot within a day, the convoy's save and load on the road, the camp, the section). AE-14 (days funded), the budget, purveyance, scutage, forage and sutlers are AE2's; AE-15 to AE-19 and AE-22 AE3's.

## Notes (P7-AE2: purveyance, scutage, finance, forage, sutlers, the pay chest)
`HostSupply` is now a partial class (`HostSupply.Purveyance.cs`, `.Forage.cs`, `.Sutlers.cs`, `.Scutage.cs`, `.Finance.cs`); the ladder is whole: baggage, food rent, depot, market, **sutlers**, **purveyance**, **forage**. **Purveyance** (4.3): own realm only (`HostAccount.Realm` is the settlement's fief realm), settlements within 16 km, nearest first; a household's grain (its barn and its store, never the host lord's own household) above 30 days of its people's need (1,100 g a day) is taken at the **king's price**, 750 per mille of the book's mid, into the baggage, as much as the day's want plus nine days of stock (ten days in the carts in all: the round is not repeated daily); hiding is P = 20 + unrest / 20 per cent (stream 0x660, `RngStreams.PurveyanceHiding`, key household and day) and a hiding household gives half. Each household taken from owes a `PurveyanceTally` (the settlement's tallies add to the whole sum with no rounding loss: AE-5's 3,000 kg at a mid of 45 is 101,250), carries a `purveyance` grievance (2 points, 30 days, `UnrestBook.Add`), a settlement at 350 unrest or more has `UnrestConcede.Trigger(.., "tallage")` struck, and `purveyance_taken` is written per settlement and day (cause: the host's campaign entry, else the note key). The tally is a real debt of the realm: `WarSupplySeasonSystem` (S5, hour 5 of a season's first day) pays tallies past due in tally order from the realm's treasury while it holds more than a season of its payroll (`RealmPayrollSeason`: its fiefs' staff and its hosts' pay for a season), coin to the household's purse, `TransferReason.Purveyance`; a tally four seasons (60 days) past due is `Defaulted`, the grievance is added again and `purveyance_unpaid` is written (one entry per taking, caused by it); settled tallies are dropped a year on. **Forage** (4.2): the foragers (a tenth of the men) go out once the baggage cannot feed the day and bring in their whole day's capacity (30 kg each times the 0x750 draw of 0.8 to 1.2, key the host's settlement and the day), the host eats its need and the rest stays in the carts; within 4 km, settlement by settlement, nearest first: stores first (on **enemy land** everything the households hold, plunder, each robbed household's opinion of the host's realm -40 in the rule book's opinions; with `ForagePolicy.OwnLandPaid` on own land the same take as purveyance at the king's price, by tallies, no hiding), then in autumn the **ripe fields** (half an acre's yield taken into the baggage as a harvest, the other half trampled: both leave the field and count as farming's `Trampling` loss), in summer the green crops for the horses' hay (a tonne an acre, the crop lost through `FarmingSystem.AddDamage`), in winter only stores. `army_foraged` per settlement and day. **Scutage** (4.4): `CallWarService(realm, day, playerPays)` gives each vassal fief of the realm `men x 40` days of service or `men x 40 x 300` mites; an NPC serves when his opinion of the overlord plus a draw of -10 to 10 (0x753, key the vassal's holder and the season) is not below zero and the fief has the households, else pays; it pays at once to the overlord's treasury (`Scutage`, the due is `DueKind.Scutage`, which the season's settling now maps to `TransferReason.Scutage`); what it cannot pay stays a due and goes to arrears and grievance as Phase 6 has it; service is a Phase 6 `Service` due. `scutage_paid` once a season per overlord (a root). **Finance** (4.5): `Budget(realm, roster, days)` is the daily cost (pay with the levies' free days, staple at the books' mean mid, fodder, carting a twentieth of the food) and the funds (the realm's treasury, its hosts' chests, scutage due, subsidies granted and not collected, the loan room of the richest eight lenders); `DaysFunded(realm)`, `MayStart` (30 days), `SeekFunds` (under 10). `Raise(realm, target, day)` is the rule's own order: the fiefs' aid (half of each fief treasury), loans of existing coin (`RuleLoans.FindLender`/`Lend`, a quarter of the want each, up to four, the Phase 6 interest and default rules), tallage on the realm's towns (`RuleCouncil.Tallage`: a tenth of movables, two grievances a household); `Fund(host, days, day)` moves the realm's coin to the host's chest and writes `campaign_funded`. The war-load harness funds its campaigns through `Raise` (the audit's `WarFunding` is the fallback for a world without a rule book). **Pay chest** (4.5): `CapturePayChest(host, captor, by, day)` moves the whole chest as `Plunder`, its gold pieces with it (the coin book carries them on a transfer of that size), `pay_chest_captured`. **Sutlers** (4.7): a host of 200 men or more camped two days (or marching in its own realm) draws trader businesses of the books within 16 km holding 50 sp and 50 kg of staple in their own store (P = 10% a day, stream 0x751, key host and day, draw by book and merchant; at most one a hundred men); they sell to the quartermaster at their book's ask times 1.3 from their stores into the baggage, paid from the chest by a trade transfer; `sutlers_joined` for the first; they leave when pay is three days in arrears or their stock is out. **Saves:** optional keys 8 to 11 on the `warsupply` section (tallies, sutlers, unnoted scutage, next tally and the running stats), absent from the hash and counters until used. **Tests:** `ArmyFinanceTests` (AE-5, AE-6, AE-7, AE-8, AE-9, AE-10, AE-14, AE-20, the rule's fund, the 0x660 stream, the save and continue, the sutlers). **Left out:** hay above 15 days, a cart and a draught animal from households with two (and their return at Disband), the officials' skim on what they handle, purveyance at lords' and towns' barns apart from the households', the camp market book and the soldiers' own spending, the Fief tab, campaign panel and Army tab views (the Game project), the NPC ruler's decision rule for purveyance (the policy is a switch the military sets), `foraged` as an unrest kind (the robbed households' opinion is what falls), the realm's decision to ask a council for a subsidy in `Raise`.
**War-load soak, seed 1234, years 1 to 10 with the control, after the part** (`WarLoadSoak_Years1To10_WithControl_Gates`, the gates read by `WarGates`): WL-1, 2, 3, 4, 5, 8, 11, 12, 14 pass; **WL-7 passes** (the two purveyance campaigns, Y7 and Y8, purveyed 8 settlements and 128,032 kg; tallies 3,718,747 mites, none paid, 2,926,959 defaulted because the realm's treasury never held a season of payroll; every purveyed settlement's households carried the grievance the next day, 3 settlements reached rung 2 within 30 days, no revolt won, no ruler changed; the Y7 riot at the 38-household village is caused by its `purveyance_taken` entry); **WL-12 passes** (111 army and finance entries, 100% with a specific cause; 61 staple price rises of 5% or more from the host's buying and 61 `army_bought` entries naming the host's campaign); **WL-9 stays pending** (kits and armouries are P7-AE3's); WL-10 reads +16.6% at year 10 against the control (it was +19.7% with the part switched off, and the same read is the harness's war pay counted as `Wage` in a ten-year slice, not the part). **WL-8 before and after** (the realm's purse is now the rule's: treasury, aid, loans, tallage on towns with the Phase 6 grievances): raised 76,728,569 mites over the campaigns, 78,748 man-days unpaid, 29,367,736 mites of arrears and 787 stand-in deserters before; raised 27,548,296, 126,384 man-days unpaid, 47,373,694 of arrears and 1,282 deserters after (a tenth of a town's movables is a smaller purse than the audit's tallage on every household's purse; no lender held twice a quarter of a month's want, so no loan was made in the soak: loans are exercised in `Raise_Loans_MoveCoinThatExists_FromARichLenderToTheRealm`). EA_F7 (the realm's empty purse) is thereby read as the rule's: the realm raises what the rule lets it and the host goes short of the rest.
