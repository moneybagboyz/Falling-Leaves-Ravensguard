# Money and markets

Plan refs: §3 item 3 (closed money supply), §4A.1 (money, prices, merchants), §2.4 (determinism). Replaces the legacy
`PricingSystem.gd`, `TradeSystem.gd` price/tariff code and every money faucet listed in plan §4A.1 ("about 8 places").

## 1. Purpose
One ledger that is the only way coin changes hands, mints that are the only faucet, a short list of explicit sinks, and
per-settlement market books whose price depends on local stock, demand and local money, with large trades moving the price
against the trader.

## 2. Data

### 2.1 Money and accounts (`Sim/Economy/Ledger.cs`)
`Money` = `long` mites (1 crown = 100 mites). An **account** is a balance owned by one record.

| Account kind | Owner id kind | Holds | Replaces |
|---|---|---|---|
| `Household` | Household | household purse (coin of all members; head decides) | `HouseholdTable.Purse` (int placeholder) |
| `Business` | Business | cash box (working capital, takings) | none |
| `Treasury` | Faction / Fief / Parish / Town / Mint | chest of a public body | none |
| `Caravan` | Caravan | trip float and takings in transit | none |
| `Hoard` | Site / Item | coin buried or lost (out of circulation, still audited) | none |
| `Market` | Settlement | change box for rounding remainders (<= 1 mite per trade) | none |

`AccountTable` (struct of arrays): `EntityId[] Owner; AccountKind[] Kind; long[] Balance; byte[] Region;` plus per-day
counters `long[] InToday, OutToday` for the dashboard. Households use the household index as account index (dense); other
kinds are allocated from `IdKind.Account` serials.

`TransferReason` (enum, for audit and chronicle): `Trade, Wage, Rent, Hearth, Toll, Tithe, MarketToll, Dividend, Mint, Seigniorage, Inheritance, Escheat, Hoard, Melt, Stipend, Fine, Seed`.

### 2.2 Ledger API (the only mutators)
```csharp
bool Transfer(AccountRef from, AccountRef to, long mites, TransferReason why); // false if from.Balance < mites (never negative)
long Mint(MintId mint, long silverMg, out long seigniorage);                      // creates coin; only a Mint may call
void Sink(AccountRef from, long mites, SinkReason why);                           // Melt only (and test-only Burn)
```
`Transfer` of 0 or negative is a no-op/ArgumentException. Balances never go below 0; "debt" is a separate arrears record
(taxes spec), not a negative balance.

### 2.3 Mints
`MintStation` (one per faction that has a mint town; a record in `TreasuryTable`): `Faction`, `Town`, `SeigniorageBp`
(default 750 = 7.5%, content range 500..1000), `SilverDustMg` (carried remainder), `Treasury` account. Content:
`crown_silver_mg = 1300`.

### 2.4 Market books (`Sim/Economy/MarketBook.cs`)
One `MarketBook` per settlement that has a market (towns, villages; farmsteads use the road-nearest book,
`Household.MarketSettlement`).

`PriceState` per (book, good): `Fixed Ref` (mites/kg or per piece), `long DemandEmaGPerDay`, `long StructuralDemandGPerDay`,
`long StockG` (sum of lots), `int TargetCoverDays`, `Fixed Mid` (derived, cached per session), `int LastTradedDay`.
`StockLot` (table): `Id`, `Owner` (Household/Business, the *seller*), `Good`, `Tier` (0..4), `long Grams` (or pieces), `Fixed FloorRatio` (lowest `Mid` multiple the owner accepts, default 0.8), `int ListedDay`.

## 3. Rules

### 3.1 The price curve
`T` = target cover in days (class value from `goods.jsonc`: staple grain 30, flour 14, bread 2, fresh fish/meat 2, ale 5,
salt 60, iron/bar 60, cloth 45, tools 60, firewood 20, charcoal 30). `d` = demand in g/day = `max(StructuralDemand, DemandEma, DemandFloor)`; the legacy bug (a good with no demand entry got `demand = 1.0`, `PricingSystem.gd:21`) is closed by
the `StructuralDemand` term: households' basket demand plus planned recipe inputs of local businesses (kg/day), and
`DemandFloor = 0.25 x population x basket_g_per_person_day` for basket goods and `0.02 x population` g/day for every other
listed good.

```
cover  c   = stock_g / d                      (days)
mid(s)     = Ref x clamp( sqrt(T x d / s), 0.3, 4.0 )        for s > 0;  mid(0) = 4.0 x Ref
```
Clamp breakpoints in stock: `s_lo = T d / 16` (price flat 4x below it) and `s_hi = T d / 0.09` (flat 0.3x above it).

**Spread:** half-spread `h = spread_half_permille / 1000` (default 80). A customer pays `ask = mid x (1 + h)`; a seller to a
merchant receives `bid = mid x (1 - h)`. Full spread 16%, inside the plan's 10-20%.

### 3.2 Per-unit repricing = exact integral
Buying `q` g from stock `s0` leaves `s1 = s0 - q`; the mid cost is the area under `mid(s)`:
```
cost_mid = integral over s in [s1, s0] of mid(s) ds
         = Ref/1000 x 2 x sqrt(T d) x ( sqrt(s0) - sqrt(s1) )       inside [s_lo, s_hi]  (grams; Ref in mites/kg)
         + 4 x Ref/1000 x (min(s0, s_lo) - s1)  if s1 < s_lo           (flat part below)
         + 0.3 x Ref/1000 x (s0 - max(s1, s_hi)) if s0 > s_hi          (flat part above)
```
Implementation: `Int128` intermediates, `Isqrt128` (integer Newton, floor) on `T d x 2^32` and `s x 2^32`, result rounded
half up to whole mites; accuracy within +-1 mite of the real-valued integral (test tolerance). Selling `q` onto the book is
the same integral from `s0` up to `s0 + q`. Customer total = `cost_mid x (1 + h)`; seller proceeds = `cost_mid x (1 - h)`.
Because the price integral is monotone, splitting a trade into pieces never beats one trade (property test
`NoArbitrageBySplitting`).

`MaxAffordable(book, good, purse)` solves `cost(q) = purse` by 40 steps of integer bisection on grams (deterministic), then
the buyer pays `cost(q)`.

### 3.3 Settlement of a purchase (one `Trade` = one Ledger transaction set)
1. Walk lots of the good in ascending `(FloorRatio, LotId)` whose floor <= current `mid`; each lot's slice of the integral is
   paid to its owner's account (`Trade`); the sum of slices equals the customer total exactly (rounding remainder goes to the
   last lot).
2. The seller pays the **market toll** `market_toll_permille` (50 = 5%, taxes spec) of their proceeds to the market owner's
   treasury (`MarketToll`). Buyers pay no toll.
3. Stock is moved lot -> buyer (`Stock.Move`); `StockG`, `DemandEma` event counters and `LastTradedDay` update.
Nothing is created: money moves buyer -> sellers (+ toll to treasury); goods move sellers -> buyer.

### 3.4 Who sells, who buys
- **Producers with surplus** (farm households above their keep target, craft businesses with finished output) first try
  **merchants**: merchants (Persons with occupation `Merchant`, a `MerchantProfile` of `CashShareBp` of household purse and
  `StockCapCoverDays = 0.8 T`) buy at `bid` in id order while their cash and cap allow; each purchase becomes a
  merchant-owned lot (floor 1.0). Unsold remainder is listed as the producer's own lot (floor 0.8) and expires after 3 days
  (returned to stores).
- **Households and businesses buying**: `buy order` = (good, qty, max price ratio). Orders are placed by the
  needs/work-allocation AI (households-and-needs, production) and filled in the session in ascending household/business
  index; partial fills allowed; unfilled orders lapse (no queue).
- **Merchants restock** each session: for goods with `cover < 0.5 T` they buy locally first; if still short they post a
  **import request** to the caravan planner (caravans spec). That is the pull model of plan §4A.1.

### 3.5 Reference price drift
`Ref` starts at the content base price (`base_price_mites_per_kg`). At each seasonal step (S5):
```
local_cpp  = sum(balances of Household and Business accounts homed in the settlement) / population         (mites per person)
target_mult = clamp(local_cpp / seed_cpp, 0.5, 2.0)          seed_cpp = 5,000 mites (50 crowns)
Ref_new    = Ref + (base x target_mult - Ref) / 4           (Fixed, floor)
```
So a richer town drifts to higher prices and a poor one lower; the hero's gold raises prices only after it is spread into
local accounts (soak `SoakRichPlayer`).

### 3.6 Market hours and sessions
Open `06:00-18:00` local (`market_open_hour/close`). Each open hour is a session for the settlement; households shop in the
session of their slice hour if open, else the next open hour. Per session a book touches only goods with an order or a lot
(typical 10-20 of ~60). Fairs (calendar feasts) double the session count and add `fair_extra_demand_pct` (content; stub in
Phase 2).

### 3.7 Mint operation (seasonal S5 and on delivery)
Miners/smelters sell `silver` (grams) to the mint at the mint rate:
```
struck_mites   = floor(silver_mg / 1300) x 100          (whole crowns only; remainder stays in SilverDustMg)
seigniorage    = floor(struck_mites x SeigniorageBp / 10000)
to_seller      = struck_mites - seigniorage
```
`Mint` creates `struck_mites` once; `to_seller` goes to the seller's account, `seigniorage` to the faction treasury. Silver
goods are destroyed (`Stock.Destroy`, reason `Minted`). Content `silver_grade_g_per_t` gives the ore-to-silver conversion in
the smelting recipe (production spec).

### 3.8 Seed money (world generation, once)
Total = `seed_money_crowns_per_person x population` crowns. Shares (content `seed_money_shares_pct`): gentry households 20,
burgher households 35, peasant households 30, fief/town treasuries 12, parish chests 3. Each share is split by weight within
its group (households: `1 + 0.25 x workers`; treasuries: fief acres), using largest remainder so the total is exact. Source
`Seed` is the only non-mint creation and runs before day 0.

### 3.9 Sinks and hoards (documented, exhaustive)
- `Hoard`: each season a household with balance > `hoard_trigger_x` (6) times its 15-day expense average buries
  `hoard_bury_pct` (10) of the excess with probability `hoard_chance_pct` (20, stream 0x260). Coin moves to a `Hoard` account
  at a site; still counted in the audit, excluded from `local_cpp` and `circulating`. Hoards are findable later (plan §3b,
  Phase 4).
- `Melt`: a silversmith recipe `melt_coin` (coin -> silver bar) calls `Ledger.Sink(Melt)` and `Stock.Create(silver)`; silver
  mass is conserved with the mint.
- Death: coin never vanishes. Estate passes by inheritance/escheat (population spec). Coin on a corpse in the live tier is an
  item drop (Phase 4); until then it stays on the account.
No other sink or faucet may exist; the CI grep test `NoDirectBalanceWrites` fails any assignment to `AccountTable.Balance`
outside `Ledger.cs`.

## 4. Tick placement
- **H3 (hourly, market hours)** sessions as above, per settlement in ascending id; inside a session: sellers/merchants (3.4)
  -> order matching in household index order -> lot expiry.
- **H5 (hour 0)**: `DemandEma` update `ema = ema + (observed_g_today - ema) / 7` (floor), structural demand recompute,
  stale-lot cleanup.
- **H6 seasonal**: S5 price drift (3.5), S5 mint run, hoard check, dashboard counters rolled.
- **Live tier**: the hero's trades are `EconCommand.Buy/Sell` executed at the next command point (abstract-tick spec 3.2);
  quotes (`Quote(book, good, side, q)`) read the last published snapshot and are exact unless a session ran in between (the
  command revalidates and returns the real total).

## 5. Determinism
- Order: settlements ascending; within a session sellers by (owner kind, owner id), orders by household index; lots by
  `(FloorRatio, LotId)`.
- Ties for the last gram: the household with the smaller `DetRng.Next(seed, hour, householdId, 0x220, 0)` wins, then id.
  Result independent of input permutation.
- All price math is integer/`Fixed` with `Int128`; `Isqrt128` is floor; rounding is half up at the final mite; remainders go
  to the last lot or the `Market` account, never lost.
- Hero commands carry the `(tickKey, sequence)` they were applied at and are logged for replay.

## 6. Content (JSON)
`content/base/economy/goods.jsonc` (shared with items spec; money-relevant keys):
```jsonc
{ "type": "good", "id": "wheat", "name": "Wheat", "class": "staple_grain",
  "base_price_mites_per_kg": 45, "target_cover_days": 30, "basket_g_per_person_day": 1100,
  "unit": "kg", "tradable": true }
```
`content/base/economy/money.jsonc`: `crown_silver_mg`, `mint_seigniorage_bp` (default 750, min 500, max 1000),
`spread_half_permille` (80), `market_toll_permille` (50), `market_open_hour`, `market_close_hour`, `price_clamp_min_permille`
(300), `price_clamp_max_permille` (4000), `ref_drift_divisor` (4), `demand_ema_days` (7), `seed_money_crowns_per_person`
(50), `seed_money_shares_pct`, hoard keys. Schema-validated; all rate keys carry unit suffixes.

## 7. Interactions
- **Production/farming/extraction**: sell output through 3.4; buy inputs with orders. **Households**: shop (needs spec).
  **Caravans**: fill `import request`s, sell at destination books, pay tolls. **Taxes**: toll and tithe flows land in
  treasuries; treasuries pay stipends/wages back to people. **Continent tick**: exports `price index` per region from the
  books. **LODBridge**: hero trades; promoted merchants keep acting abstractly (abstract-tick spec).

## 8. Edge cases
- Stock 0: `mid = 4 x Ref`; nothing to buy; orders lapse; a `Famine`/`Shortage` signal is raised for staples (population spec).
- Buyer cannot afford the minimum lot (1 g granularity): zero-fill, no error. Merchant with no cash: skips (sale fails,
  producer lists lot).
- `Ref` below 1 mite/kg for bulky goods: stored as `Fixed` so 0.5 mite/kg is fine; totals round at settlement only.
- Hero buys non-whole crowns: UI rounds the quote up to whole crowns for buys and down for sells; the difference sits in the
  `Market` account (conservation exact).
- Goods without a base price are rejected by the loader (no silent default).
- Currency overflow: `Int128` guards; `long` balance overflow throws.


## Decisions made in this spec (collected in 00-overview.md section 10.4)
- **M1** Decision: Half-spread is 8% per side (16% total). Reason: plan §4A.1 asks 10-20%.
- **M2** Decision: The seller pays the 5% market toll, buyers pay only the quoted ask. Reason: one clean ask price; merchants price the toll in.
- **M3** Decision: `Ref` drifts toward `base x clamp(local coin per person / 5,000 mites, 0.5, 2.0)` by a quarter per season. Reason: plan: `ref` drifts with the local coin per person; slow EMA avoids oscillation.
- **M4** Decision: Merchants are the market makers, limited by their own cash and stock caps (0.8 T cover). Reason: plan: merchants are real people holding stock and cash.
- **M5** Decision: Seed money shares: gentry 20, burghers 35, peasants 30, treasuries 12, parishes 3 (%). Reason: total of 50 crowns per person is the plan's; the shares are tunable content.
- **M6** Decision: Sinks are only hoard burial (still audited) and melting; death never destroys coin. Reason: plan lists the leaks; estates always pass to someone.
- **M7** Decision: Trade totals are rounded half up to a mite; remainders go to the last lot or the market's change account. Reason: exact conservation with integer money.

## 9. Golden tests
Test good `test_grain`: `Ref = 45` mites/kg, `T = 16` days, `d = 100` kg/day (so `T d = 1,600 kg`), `h = 8%`, toll 5%.

| id | Setup | Expected |
|---|---|---|
| G-M1 | Mint 10,000,000 mg silver, seigniorage 750 bp | struck 769,200 mites (7,692 crowns); seigniorage 57,690; seller 711,510; `SilverDust` 400 mg; supply +769,200 |
| G-M2 | stock 400 kg: `mid = 45 x sqrt(1600/400) = 90.0` mites/kg; cover 4 d | `mid(400 kg) = 90`; `mid(1,600 kg) = 45`; `mid(100 kg) = 180` (clamp 4x) |
| G-M3 | Buy 76 kg from 400 kg (-> 324 kg): `0.045 x 2 x sqrt(1.6e6) x (sqrt(400,000) - sqrt(324,000))` = 7,200 | `cost_mid = 7,200`; customer pays 7,776; toll 389 (388.8 up); seller nets 7,387; end `mid = 45 x sqrt(1600/324) = 100.0` |
| G-M4 | Sell 76 kg onto the book 324 -> 400 kg to a merchant | merchant pays `7,200 x 0.92 = 6,624`; round trip margin 7,776 - 6,624 = 1,152 mites |
| G-M5 | Buy 350 kg from 400 kg to 50 kg: curved part 400 -> 100 kg = `3600 x (20 - 10)` = 36,000; flat part 100 -> 50 kg at 180 = 9,000 | `cost_mid = 45,000`; customer pays 48,600 |
| G-M6 | Stock 20,000 kg (above `s_hi = 17,778 kg`) | `mid = 0.3 x 45 = 13.5` mites/kg |
| G-M7 | Split: buying 76 kg as 38 + 38 kg | total >= single-trade total (equal within +-1 mite) |
| G-M8 | Seed money, population 25,000, 50 crowns each | accounts sum to exactly 1,250,000 crowns: gentry 250,000, burghers 437,500, peasants 375,000, treasuries 150,000, parishes 37,500; any permutation of household order gives identical balances |
| G-M9 | Ref drift: `Ref = 45`, `base = 45`, local cpp = 10,000 mites (2x seed) | `target_mult = 2.0`; after one season `Ref = 45 + (90 - 45)/4 = 56.25`; after four seasons `90 - 45 x 0.75^4` = 75.76 (+-0.1, floor effects) |
| G-M10 | Stock 0 | `mid = 180`; a buyer with 5,000 mites buys 0 g and keeps all 5,000 |
| G-M11 | Property: 10,000 random trades among 200 accounts and 3 books | `sum(balances)` unchanged; per-good grams unchanged except documented creates/destroys; no negative balance |
| G-M12 | Permutation: shuffle the order in which 50 households place the same orders | identical final balances and stocks |

## 10. Soak invariants
1. Coin audit exact every day (`seed + minted - sunk`); 100 years, 0 violations.
2. `Ref` of each staple stays within [0.5, 2.0] x base; `mid/Ref` within [0.3, 4.0] always, staples in [0.6, 1.8] for >= 90%
   of days (excluding forced-drought years).
3. Money supply growth in a region with an active silver mine is 0-3%/game year; without one it is 0 (+/- hoards).
4. Per-settlement `local_cpp` never < 0.2 x seed or > 5 x seed for 100 years in the baseline soak.
5. `SoakRichPlayer`: a hero injecting 50,000 crowns over 1 year changes local staple prices by < 25% after 5 years
   (dispersal), and the audit holds.

## Notes

Phase 2 economy foundation implementation notes (no spec Decision changed):
- Seed money is apportioned by percent shares (gentry 20, burgher 35, peasant 30, treasury 12, parish 3) from `content/base/economy/money.jsonc`, largest remainder, ties by hash of owner id (stream 0x220). Creation is a Ledger `Seed` source before day 0.
- The `economy` save section is optional; worlds saved without it load with `Economy == null`. Golden generator hashes are unchanged because seeding is a separate step after population.
