# Trade screen, inventory, character sheet, skills by use, hiring, recruiting and the squad

Plan refs: §3 item 2 (skills improve with use, 0-100; attributes train slowly; injuries lower them), §4A.3 (progression formula
`0.4 x difficulty x (1 - s/100)^2`, attributes +0.02 per game day, encumbrance `15 + 2.5 x strength` kg, traits), §3 item 4 (talk, trade,
hire, recruit anyone), §2.10 (items, containers with volume, mass = volume x density), §2.8 (keys C, I, P), §5 Phase 3 ("trade, inventory
and character screens", "recruiting and the squad", "skills that improve with use"), overview O4 (prices: exact where you stand, "last
heard" with an age elsewhere), Phase 2 `money-and-markets.md` 3.1-3.3 (per-unit integral pricing), `items-mass-value-spoilage.md` 2.4, 3.1.

## 1. Purpose
Let the player buy, sell, carry, measure and improve: a trade screen that shows the true per-unit price of what the market will pay (and what
it last heard elsewhere), an inventory where weight and volume matter, a character sheet that explains itself, a skill system that rewards
doing, and a way to hire help or bring anyone into the party. The same records serve NPCs: a merchant's pack, a recruit's skills and a
shopkeeper's stock use the same tables.

## 2. Dependencies
| Needs | From |
|---|---|
| `MarketBook` (`AskMicro`, `BidMicro`, `MidMicro`, `GoodBook.Lots`), `PriceCurve`, `TradeResult`, `PriceNotebook` (`Visit`, `Hear`, `AskWithAge`) | exists (`Economy/Markets`) |
| `EconCommand` kinds `Buy`, `Sell`, `PayCoin`, `HireWorker` | Phase 2 spec 2.2 (market handlers: wired in Phase 3 for the hero) |
| `ItemTable` / `ItemRow` (`Parent`, `ContentsGood`, `Flags`, `Quality`, `ConditionPermille`), `Store`, `ItemFactory`, `ItemMath` | exists (`Goods/`) |
| `HeroRecord` (`Attributes`, `Skills`, `Inventory`, `Coin`) | exists (`Hero/HeroRecord.cs`); this file migrates `Inventory` to item rows |
| `PersonTable` `Skills` (4 slots of byte level) and `Attr` | exists; extended below |
| Jobs and `AbstractJob`, `Job.Kind` | `live-behaviour-and-utility-ai.md` |
| `CharacterScreen`, `ScreenRouter`, `PixelTheme` | exists (`Game/UI`) |
New code: `Ravensguard.Sim.Trade` (`TradeSession`, `Basket`, `PriceQuote`, `Haggle`: none in v1), `Ravensguard.Sim.Inventory` (`Carried`, `Encumbrance`,
`ContainerRules`), `Ravensguard.Sim.Characters` (`SkillGain`, `AttrGain`, `Recruit`), `Ravensguard.Sim.Squads` (`Squad`, `SquadTable`).

## 3. Data

### 3.1 Skill and attribute precision (new `PersonTable` columns)
The existing 4 slots of `(skill, byte level)` stay for NPCs. For persons who train (the hero, party members, apprentices) and for every
person at the moment a skill changes:
| Column | Type | Unit | Meaning |
|---|---|---|---|
| `SkillMicro` | `int[4]` per person, allocated only for "tracked" persons (hero, squad, apprentices; <= 64 per world) | micro-levels (level x 1,000,000) | the level to six decimals |
| `AttrMilli` | `short[5]` for tracked persons | milli-points (point x 1,000), delta from the byte `Attr` | slow attribute growth |
| `TrackedIdx` | `short` | | -1 if not tracked |
Untracked NPC skills change only through Phase 2's seasonal `LabourMarket.SkillPerSeason = 2` and apprenticeship (existing). `HeroRecord` skills (a
dictionary of 27 ids to level 0..100) move to the same micro-level store; `HeroRecord.Skill(id)` returns the floor.

### 3.2 Inventory as item rows
- Every carried object is an `ItemRow` with `Owner = person id` and `Parent = row of the container (or -1)`. Worn gear has `Owner = person` and an
  `EquipSlot` (new `byte` column in `ItemRow`, `0 = none`). Bulk goods (grain, ale) sit as `ContentsGood/ContentsQty` of a container row, so a sack
  of 20 kg grain is one row.
- **Migration:** `SaveMigrator` step "hero_inventory_to_items" converts each `HeroItem(Shape, Material, Count)` into `Count` item rows (or one counted
  row) through `ItemFactory`, assigning quality `Common` and condition 1,000; `HeroSave` keeps the arrays for old saves only (read, never written).
- **Container definitions** (content `items/containers.jsonc`; the base file already defines barrel, sack, chest in Phase 2):
```jsonc
{ "type": "container", "id": "satchel", "volume_l": 8, "empty_mass_g": 600, "soft": true, "max_item_cm": 45, "slot": "belt_or_shoulder",
  "carry_class": "worn", "spoil_class": "house" }
```
| Container | Volume (L) | Empty mass (g) | Where carried | Notes |
|---|---|---|---|---|
| Belt pouch | 2 | 120 | belt | coin is not stored here (ledger purse), small items |
| Satchel | 8 | 600 | shoulder | |
| Rucksack | 30 | 1,800 | back | |
| Sack | 50 | 400 | both hands or shoulder (counts as 2 hands) | grain, goods |
| Basket | 20 | 700 | one hand | |
| Quiver | 3 | 300 | back/belt | arrows only (Phase 4) |
| Saddlebags (pair) | 40 | 2,500 | on a horse (`horses-and-carts.md`) | |
| Cart bed, chest, barrel | by definition | | placed in the world | |
- Volume of an item: `shape volume_cm3` (shape data) or `grams x bulk_litres_per_kg / 1000` for bulk. A container accepts an item if `sum(volume) <= volume_l x 1000`
  and `item longest dimension <= max_item_cm`; nesting depth <= 3. Liquids need a container tagged `liquid` (flask 1 L, skin 2 L, barrel).

### 3.3 `Squad` and hired persons
```
Squad   { Id (EntityId), Leader (person), Members[<=12] (person idx), Order (SquadOrder), OrderArg (place/job/person), Stance (byte), Formation (byte, Phase 4) }
Hire    { Employer (person), Worker (person), WageMitesPerDay, StartDay, EndDay, Terms (flags: food, lodging, danger), State (Active, Ended, Quit, Dismissed), PaidThroughDay }
Follower{ Person, Loyalty (0..1000), Joined (day), Origin (hired|recruited), Ambition (id), AmbitionKnown (bool), WageMitesPerDay, UnpaidDays (byte) }
```
`SquadOrder`: `Follow, Hold, GuardPlace, Work(jobId), Rest, Travel(place), Scatter`. `Follower` rows exist only for recruited companions and hired people.

### 3.4 Content: `economy/trade.jsonc` and `characters/progress.jsonc`
```jsonc
{ "type": "trade_settings", "id": "default",
  "friend_discount_pm": 0, "buy_stolen_pm": 500, "price_age_fresh_days": 3, "price_age_old_days": 15,
  "stack_split_min_g": 1, "sell_unknown_condition_pm": 700 }
{ "type": "progress", "id": "default", "meaningful_uses_per_hour_cap": 12, "easy_cutoff_levels": 30, "gain_k_x100": 40,
  "attribute_gain_milli_per_activity_day": 20, "attribute_soft_cap": 70, "teaching_multiplier_pm": 2000,
  "skills": { "melee": { "attr": "strength" }, "riding": { "attr": "dexterity" }, "trading": { "attr": "wits" }, "cooking": { "attr": "wits" } },
  "uses": { "trade_deal": { "skill": "trading", "difficulty_x100": 60 }, "ride_gallop_minute": { "skill": "riding", "difficulty_x100": 80 },
            "dummy_hit": { "skill": "melee", "difficulty_x100": 40, "cap_level": 30 }, "recipe_step": { "skill": "from_recipe", "difficulty_x100": "recipe" } } }
```

## 4. Rules

### 4.1 Prices on the trade screen (per-unit, exact)
- **Where you stand (exact):** the book of the settlement you are in. For a good with `s0` grams in stock and a quantity `q`, the screen shows three numbers computed by the Phase 2 integral (`PriceCurve.CostMid`, then `x (1 + h)` buying or `x (1 - h)` selling): the **first-unit price** (mid at `s0` -> ask/bid per kg or per piece), the **average price** for `q`, and the **total** in mites rounded half up (money spec 3.2). `h` = 80 permille per side (M1), the seller pays the 5% toll (M2).
- Price per unit is shown in crowns per kg, 2 decimals (O2), pieces per piece; the total is shown in whole crowns plus mites (`3 cr 40 m`). Splitting a trade never beats a single trade (property test in money spec), so the screen only offers one-shot quantities.
- **Slippage guard:** `Buy`/`Sell` commands carry `max_total`/`min_total` mites equal to the quote +/- 2%. If the worker changed the book at the barrier the command may return `PriceMoved` (state "Settling accounts" for <= 150 ms, then the new quote is shown, nothing was spent). The result text is the actual `TradeResult.PaidMites`.
- **Trading with a shopkeeper or stall holder:** goods come from the business's `Store` listed lots on the book (`StockLot`, `FloorPermille`), so the quote is the book quote; the NPC's coin receives the proceeds. **Individual items** (a sword, a saddle) are priced from `ItemMath` value: ask = `value x (1 + h)`, bid = `value x (1 - h) x condition x quality factor`; unknown or stained items fall to `sell_unknown_condition_pm = 700` of full bid until inspected (E) once.
- **Barter:** with a person who is not a shopkeeper, an exchange of items for items is accepted if `value_received_by_npc >= value_given_by_npc` where each side uses the same value with `want_pm` (+20% for something they need per `NeedState`, -20% for goods they cannot use). No haggling in v1 (`Haggle` is a stub; Open question 3).
- **Refusals:** closed stall (market hours 06:00-18:00, Phase 2), no stock, can't afford (shows their coin), "I don't buy stolen goods" for `Stolen` flagged items when relationship < friend (price x `buy_stolen_pm`, the item is confiscated by guards in Phase 6).

### 4.2 "Last heard" prices (O4)
- The hero has a `PriceNotebook` (Phase 2 class, saved in the hero section). It is updated by `Visit(book, today)` **on opening the trade screen** in a market, on walking through a market square (once per game day, free), and by **price talk**: asking a merchant "How are prices in X?" in the Trade topic adds up to 6 `PriceNote`s the NPC knows (the NPC's own notebook age is kept, so a note can be older than today).
- **Display:** the Trade screen has a **Prices** tab: rows = goods the hero holds or has traded, columns = known markets; each cell shows the ask in cr/kg and the age: `1.84 (6 d)`. Colours: fresh (<= 3 days) normal, aged (4-15) amber, old (> 15) grey with "?" and the settlement's current local price is exact and bold.
- A **route hint** (off by default, toggle) shows the difference between the local bid and a remembered ask minus road cost: `+0.41 cr/kg, 8.0 km, about 15 h on foot, price 6 days old`. It never points at a profit as a guarantee (plan: "warnings before failure").
- **Conservation:** looking is free; buying and selling only use the ledger `Transfer` and `Store.Move` (Phase 2); the screen never mints or deletes.

### 4.3 Inventory, volume and mass
- **Mass** carried = sum of item masses (components and contents) + coin mass (`grams = mites x 13 / 1000`, 1 crown = 1.3 g silver; 1,000 crowns = 1.3 kg) + animals' loads are separate.
- **Capacity** `C = 15 + 2.5 x strength` kg (§4A.3; strength 50 -> 140 kg, female baseline 44 -> 125 kg). **Load ratio** `r = mass / C`.
| r | Move speed factor | Stamina cost factor | Other |
|---|---|---|---|
| <= 0.50 | x1.00 | x1.00 | |
| 0.50-1.00 | `1 - 0.40 x (r - 0.5)/0.5` (1.0 -> 0.6) | `1 + (r - 0.5)` | |
| 1.00-1.50 | `0.60 - 0.50 x (r - 1.0)/0.5` (0.6 -> 0.1) | `x 1.5 + 2 x (r - 1)` | melee timings +1% per kg over `C` (§4A.3) |
| > 1.50 | cannot walk | | drop something, or use a cart or horse |
- **Hands:** each hand holds one thing (<= 25 kg, <= 40 L). A sack (50 L) uses both hands unless shouldered (shoulder limit: 1 sack, speed x0.9).
- **Operations (UI and Q menu):** pick up (E on ground item, "Take all" key `R` in a loot window), drop, put in container, take out, equip/unequip, split stack (counted goods), give (to NPC, takes the item to their `Pack`), throw (G), inspect (shows shape, materials by component, mass, volume, quality tier, wear word, stains, value now). Dropping creates `ItemTable` rows owned by the chunk (`ChunkDelta`), not deletes.
- **Containers on the ground** (chest, barrel, cart bed, the world's `Store`s) use the same window; opening a chest you do not own is a theft flag (Phase 6 witnesses; Phase 3 shows "owned by X").
- **Auto-rules:** equipment slots follow `clothing-warmth-exposure.md` (layers); weapons to main/off hand; a quick bar of 8 slots (1-8) holds item references.

### 4.4 Character sheet (C)
Tabs: **Overview** (name, age, origin, ambition and current milestone, mood, conditions: hungry, tired, cold, wet; encumbrance bar with `r`), **Attributes** (5 values, the base-50 reference and trend arrows), **Skills** (27 skills grouped: Combat, Craft, Trade and Social, Wild; each shows level, a thin bar to the next whole level, an arrow if gained in the last 3 game days, and a tooltip with `uses per level` and the last use), **Body** (Phase 4 paper-doll; Phase 3 shows `Wounds` list placeholder), **Traits**, **Squad** link. Locked skills (0) are shown greyed out with "how to learn". `CharacterScreen.cs` is extended, and the same data is shown for any selected squad member.

### 4.5 Skills by use (§4A.3)
- A **meaningful use** is an event the system names (`uses` in `progress.jsonc`): a trade deal completed, a recipe step finished, one minute of galloping, a blow landing, a parry, a field task hour, a successful forage. Each use has `difficulty d` (x100 integer, 40 = easy, 100 = ordinary, 200 = hard).
- **Gain per use** (micro-levels, exact integer arithmetic, no float):
```
t       = (100,000,000 - SkillMicro) / 10,000            // 0..10000, floor  (this is (1 - s/100) x 10000)
gain    = d_x100 x t x t / 25,000                          // micro-levels;  = 0.4 x d x (1 - s/100)^2 levels x 1,000,000
```
- Limits: `<= 12 uses per skill per game hour` (one per 5 game minutes, so a 100-minute fight counts about 20 uses); no gain if `s > recipe_or_task_difficulty + easy_cutoff_levels (30)` (too easy); practice yard dummies stop at level 30 (`cap_level`); fatigue above 800 permille halves gain; **a master teaching** multiplies gain by 2.0 for as long as the master's level exceeds the student's by 10 or more.
- **Uses needed from `s1` to `s2`** for constant `d`: `n = 250 x (1/u2 - 1/u1) / (d/100)` with `u = 1 - s/100`. For `d = 1.0`: 20 -> 40 takes 104.2 uses; 40 -> 60: 208.3; 60 -> 80: 625; 80 -> 90: 1,250; 90 -> 95: 2,500. The plan's "20 to 40 in about 6 fights" matches 17 uses per 5-minute fight.
- **Attributes:** an "activity day" is a game day with >= 4 hours of matching activity (labour trains strength and toughness, riding dexterity, trade and study wits, watching and foraging perception). Gain `+20 milli-points` per activity day (0.02, §4A.3); above `attribute_soft_cap (70)` gain is halved. The byte `Attr` is the floor of `Attr + AttrMilli/1000`. **Injuries** subtract permanent milli-points (Phase 4 writes, here `AttrMilli` may go negative to `-5,000`).
- NPC skills: untracked NPCs do not run per-use gain; at promotion into the bubble a person with a trade becomes tracked only if they join the squad or an apprenticeship starts.

### 4.6 Hiring and recruiting anyone
- **Hire (Q > Hire):** a paid, fixed-term contract (1 to 30 game days) or a one-off job (`Job` kinds in the behaviour spec). Any person aged >= 14 who is alive and not in a stronger bond can be offered it.
  - **Reservation wage per day:** `R = W_u x (1000 + 12 x max_skill) / 1000 x danger_mult x (1000 - trust_discount_pm)/1000` where `danger_mult` is 1.0 (labour), 1.5 (guard or escort), 2.0 (fight-risk) and `trust_discount_pm = clamp(TrustPm / 10, 0, 150)`. Food and lodging in the terms lower `R` by 25% each (`FoodPct`, `BedPct`).
  - **Acceptance:** `score = offer x 1000 / R + trust_bonus + need_bonus - tie_penalty`, with `trust_bonus = TrustPm / 10`, `need_bonus = +150` if the person's household is in `Survive` tier (Phase 2), `tie_penalty = 300` for a head of household with dependants and a good job, `+/-100` mood noise from stream 0x321 keyed `(day, person)`. Accept when `score >= 1000`.
  - **Effects on the world:** the worker leaves their schedule job (Phase 2 `Employer` changes to the hero; the old employer's slot becomes a vacancy and re-fills at H5); their household sees the income change (`Income15d`); a hired farm hand lowers farm labour. The hero pays `WageMitesPerDay` daily at hour 0 from the household account by `PayCoin`; unpaid for 3 days -> they quit and `Mood -20`.
- **Recruit (Q > Invite to join):** a permanent companion for the squad (<= `2 + leadership/10` members, minimum 2, cap 12).
  - Requires **familiarity >= 20** (talk or work together), trust >= 100, and a **personal reason** known after a talk: wage, ambition fit (a recruit whose ambition matches the hero's current ambition step), debt relief, or gratitude (hero saved them). The recruit dialogue lists the reasons they would accept ("Pay me 1.5 W_s a day"; "I'd go if you'd clear the wolves from my flock").
  - **Signing bonus** = 5 days of the wage, paid at once.
  - **Loyalty** 0..1000: starts `400 + TrustPm/4`; +20 per shared meal at camp, +50 per talk at night camp (once per night), +10 per day paid on time, -100 per unpaid day, -150 if left in danger, -300 if abandoned; below 200 they ask to leave, at 0 they quit and may take gear (Phase 6 theft).
  - **Households:** a recruited spouse or parent leaves dependants; the UI warns "Aldric's household will lose 1 earner" (adds `Grievance.Lord`-independent mood malus).
  - **Dismiss:** severance 3 days of wage; the person returns to their previous home and schedule job if the slot is free, else the labour market.
- **Squad bar (HUD, P opens the Squad panel):** portraits with health, stamina, order icon and a small "needs" dot; click selects, Tab cycles, shift-click multi-selects, **R (hold)** opens the order wheel (Follow, Hold, Guard here, Work, Rest, Travel here, Scatter). Squad members use the NPC utility AI with their `SquadOrder` as a schedule override (`autopilot-v1.md`). Combat stances and formations arrive in Phase 4 (cards F1-F4 are present but disabled).

### 4.7 Tick placement
| Where | What |
|---|---|
| Live 20 Hz | UI reads inventory snapshots; carrying updates `Encumbrance` for speed (per tick, integer); skill-use events are queued |
| Barrier | `Buy/Sell/PayCoin/HireWorker/Recruit/Dismiss` commands; skill gains applied (batched per 5 game minutes) |
| H4 slice | wages for hired persons (hour 0 batch), loyalty update, mood; `Follower.UnpaidDays` |
| H5 | notebook age counters (`AgeDays` derived on read, no storage) |
| LODBridge | followers are persons, always `LiveHeld` while within 150 m of the hero; those farther are abstract and follow `PathPlan` to the squad (`Follow` order) |

## 5. Determinism
| Stream | Use | Key |
|---|---|---|
| 0x320 | skill-gain rounding tie (none needed with micro-levels; reserved for attribute rounding) and use-event jitter | person |
| 0x321 | hire and recruit mood noise | person, day |
| 0x322 | quality of goods for sale in an NPC pack (randomised only at stocking time) | owner |
Integer maths only; `Int128` not needed (max intermediate 400 x 10^8 = 4 x 10^10 fits `long`). Sort orders in lists use ascending ids.

## 6. UI and controls
- **E on a shopkeeper -> Trade**; on a market stall the same. **I** inventory, **C** character sheet, **P** squad. Esc or x closes.
- **Trade screen:** left pane your goods (grouped by container, search box), right pane theirs, centre the **basket** with per-line first price, average price and total, your coin and theirs, "Max" and +1 / +10 / +50% buttons; **keys:** arrows move, Left/Right adjust quantity (Shift = x10, Ctrl = all), Enter confirms, Tab changes pane, N toggles the price notebook tab, Esc cancels. Totals in whole crowns plus mites.
- **Inventory:** list and grid views, weight and volume bars (`12.4 / 140 kg`, `38 / 90 L`), colour for `r` (green <= 0.5, amber <= 1.0, red above), drag and drop, Q menu per item, quick bar 1-8.
- **Squad panel:** roster with loyalty bars, wages due, ambitions known, orders; "Hire" and "Recruit" are found in the Q menu of any person (greyed with the reason).
- Hints: first trade shows a one-line toast with N for the notebook; first overweight shows the encumbrance rule.

## 7. Tests (golden)
| id | Case | Expected |
|---|---|---|
| T-1 | Skill gain at `s = 20`, `d = 1.0` | `t = 8,000`, gain 256,000 micro-levels (0.256 levels) per use |
| T-2 | Uses needed (d = 1.0), simulated one by one from 20 | reaches 40.0 at use 104 or 105; 60.0 at 312 +/- 2 total; 80.0 at 937 +/- 3 total |
| T-3 | Rate cap: 100 uses in 1 game hour | only 12 counted; the 13th returns `CappedThisHour` |
| T-4 | Too easy: skill 70 on a difficulty-30 task | gain 0 (70 > 30 + 30 = 60); at skill 55 gain > 0 |
| T-5 | Attributes: 50 activity days at +20 milli | +1.000 point exactly; at 75 base, half rate |
| T-6 | Encumbrance: strength 50 (C = 140 kg), carrying 105 kg | r = 0.75, speed x0.80 (jog 3 -> 2.4 m/s), stamina x1.25; 175 kg: r = 1.25, speed 0.35 |
| T-7 | Coin mass | 1,000 crowns (100,000 mites) = 1,300 g |
| T-8 | Container: put 6 kg grain (1.6 L/kg x 6 = 9.6 L) in a satchel (8 L) | refused (volume); a sack accepts; a nested pouch in a satchel in a rucksack depth 3 ok, depth 4 refused |
| T-9 | Trade quote: buy 20 kg flour from a book whose stock is in the flat 4x part (mid 180 mites/kg, as Phase 2 G-H6) | first-unit ask 194.4 mites/kg, total `floor(194.4 x 20 + 0.5)` = 3,888 mites; sell to the same book: bid 165.6 x 20 = 3,312 less 5% toll = 3,146 |
| T-10 | Slippage: worker changes stock between preview and execute | command returns `PriceMoved`, purse unchanged, coin and goods conserved (`MassLedger` and ledger audits exact) |
| T-11 | Notebook: visit book on day 100, ask is 1.84; read on day 106 | shows `1.84 (6 d)` amber; after a new visit on day 107 shows exact, age 0 |
| T-12 | Hire acceptance: `W_u = 150`, labourer skill 20, offer 180, no terms, trust 100, not head | `R = 150 x 1.24 = 186`; score `180000/186 + 10 + 0 - 0` = 977 < 1000: refused; offer 195: `1,048 + 10 = 1,058` accept (+/- 100 noise tested with seeded draws at the boundary) |
| T-13 | Recruit | refused below familiarity 20; accepted with signing bonus `5 x wage`; loyalty starts `400 + trust/4` |
| T-14 | Wage default: hero cannot pay 3 days | recruit loyalty -300, quits on day 3, `Follower` removed, chronicle entry |
| T-15 | Conservation: 1,000 random trades and promote/demote cycles | total coin (ledger), total goods mass (`MassLedger`), person count unchanged except by trades' own transfers |
| T-16 | Migration: old save with 12 `HeroItem`s | item rows count and total mass equal; `HeroSave` round-trip byte-identical for new saves |

## 8. Open questions for the user
1. **Haggling:** none in v1 (price is the book). Default: no haggling; the Trading skill instead narrows your spread by 1% per 10 levels (max 8%) and unlocks "last heard" prices of more markets. Alternative: a haggle minigame.
2. **Party size cap:** `2 + leadership/10`, hard cap 12 before Phase 7 armies. Default as stated.
3. **Friend discounts:** `friend_discount_pm = 0` (the market is impersonal). Alternative: up to 10% for individual shopkeepers who like you.
4. **Carry weight strictness:** capacity 140 kg at strength 50 is generous; speed loss begins at 70 kg. Default strict as in §4A.3; testers can relax with a setting.

## 9. Budgets
| Item | Budget |
|---|---|
| Quote for any (good, quantity) | <= 5 microseconds (two `Isqrt128`), 0 allocations |
| Open Trade screen (<= 300 goods) | <= 8 ms to build rows (one frame at 120 Hz not required; <= 16 ms) |
| Inventory open with 600 item rows | <= 4 ms |
| Skill-use queue | <= 0.02 ms per tick |
| Memory | hero and squad items <= 3,000 rows x 64 bytes; tracked persons 64 x (16 + 10) bytes; `Follower` and `Hire` rows < 20 KB |
| Save | `trade/inventory` sections <= 0.5 MB for a 10-year hero |
