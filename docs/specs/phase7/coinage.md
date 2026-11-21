# Coinage: gold, silver and copper; mints striking all three; how amounts and prices are stored and shown

Status: design, written before any Phase 7 code, on top of b6b39e2. Plan refs: locked decision "Money becomes gold, silver and copper: 1 gold = 10 silver = 100 copper. The silver piece is today's crown; wages stay in silver. Mints strike all three from mined gold, silver and copper"; §3.3 (coin is minted only by faction mints from mined metal; from Phase 7 step 0 there are gold, silver and copper pieces); §4A.1 Money (1 crown = 1.3 g of silver; "from Phase 7 the crown becomes the silver piece ... sums are then stored in copper, and older saves are converted at load (x10)"; mints take 5-10% seigniorage; silver output tuned to grow the money supply 1-2% a year; treasuries are chests); §5 Phase 7 ("the coin change adds about 2 weeks"); §3j (seigniorage on coin minted).

## 1. Purpose
Give the world three coins with real metal behind them, wire the mints that Phase 2 specified but never ran (`economy-audit.md` S-3), and show every sum in the game as gold, silver and copper, without breaking a single saved number or golden test.

## 2. The storage decision (the crux)

### 2.1 What the code holds today
- `Economy/Coin.cs`: `readonly record struct Coin(long Mites)`, `MitesPerCrown = 100`. Every purse, cash box, debt, wage, fine, loan, escrow and chronicle parameter in every save section is a count of **mites**, 1/100 of a crown.
- Prices are finer still: market books keep `RefMicro` (micro-mites a gram or a piece, `Economy/Markets/PriceCurve.Micro`); content prices are mites a kg with decimals (`clay` 0.65, `iron_ore` 2.6, `logs` 1.25).
- The plan's line "sums stored in copper, older saves converted at load (x10)" assumed whole crowns are stored. They are not: one copper (a tenth of a crown) is **10 mites**.

### 2.2 Options
| Option | What changes | Verdict |
|---|---|---|
| **A. Keep the integer; 1 cp = 10 mites** | No stored number changes. An old 1 crown (100 mites) reads as 1 sp = 10 cp, which is the plan's "x10" exactly, already done. The mite stays as money of account, a tenth of a copper, used for prices below a copper and for exact shares | **Chosen** |
| B. Store whole copper (divide every stored amount by 10 at load) | loses up to 9 mites per amount in about 40 sections; the ledger audit breaks unless every remainder is swept to a dust purse; prices under a copper a kg (clay, ore, logs, bricks) need a finer unit anyway; re-save changes every byte; every golden test with a mite amount moves | rejected |
| C. Store hundredths of a copper (multiply by 10 at load) | lossless, but rewrites every section on re-save, cuts the `long` headroom tenfold, and gains nothing | rejected |

### 2.3 Consequences of A
- **Format v8 stays; no migrator step; no `ContentRemap` of amounts.** The stored meaning of every amount is unchanged (a mite is a mite). An older save loaded and saved again without running is byte-identical (CN-2).
- `Coin` gains named views and constructors; `Mites` stays the field: `FromCopper(long cp) = cp x 10`, `FromSilver(long sp) = sp x 100`, `FromGold(long gp) = gp x 1,000`; `Parts(out gp, out sp, out cp, out mites)` (greedy, value units); `FromCrowns` and `WholeCrowns` are kept as obsolete aliases of the silver forms (one warning-free rename pass in Phase 7).
- Content keys named in crowns (`seed_money_crowns_per_person`, the charter fee "50 crowns") keep their names and values (a crown is a silver piece); new keys are written in mites.
- **The load report** gains a `Notices` list (new, informational: it never sets `NeedsConfirmation`). A save written by a game before Phase 7 (header `GameVersion` below the first Phase 7 version) shows one notice: "Money is now shown as gold, silver and copper. One old crown is one silver piece (ten copper). Nothing in this save changed." Content added by Phase 7 (new goods, business kinds, room types) is listed by the existing `ContentRemap` notes, as for every content update.
- If the user still wants whole-copper storage (option B), it costs about 1 more month (every section, the dust purse, every golden) and buys no gameplay: open question CNQ-1.

## 3. The coins
| Coin | Value | Metal | Mass | Metal value at book price | Kind |
|---|---|---|---|---|---|
| gold piece (gp) | 1,000 mites (10 sp) | gold, 1,100 mg | 1.1 g | 1,000 mites at `gold` 909,091 mites a kg (gold to silver 11.8 : 1) | full-bodied |
| silver piece (sp) | 100 mites (the old crown) | silver, 1,300 mg (`crown_silver_mg`, unchanged) | 1.3 g | 100 mites at `silver` 76,920 a kg | full-bodied |
| copper piece (cp) | 10 mites | copper, 5,000 mg | 5 g | about 0.85 mite at `copper` 168.9 a kg | **token** (worth its face because the mint takes it at face) |
The mite is not a coin. Wages stay in silver (unskilled 150 = 1 sp 5 cp a day; skilled 350 = 3 sp 5 cp; master 500 = 5 sp; soldier 250 = 2 sp 5 cp).

## 4. What exists and what is new (by file)
| Existing | Phase 7 |
|---|---|
| `Economy/Coin.cs` | extended (2.3) |
| `Economy/MintMath.cs` (`Strike`, `StrikeInto`: silver only, no caller) | extended to a metal parameter (`MintMetal { Gold, Silver, Copper }`, `StrikeResult` unchanged for silver so G-M1 holds); the token path for copper (5.1) |
| `Economy/Ledger.cs` (`Transfer`, `Mint`, `Sink`, `OnTransfer`) | a gold hook (5.2) on `Transfer` for opted-in purses only; `SourceReason.Mint` covers all three metals (the metal is recorded in the coin stock, not the enum) |
| `Economy/LedgerTypes.cs` | `TransferReason` + `Exchange` (appended; saves pad the count as for `Alms`) |
| `Inventory/Encumbrance.CoinMassG(mites)` | `CoinMassG(ledger, purse)` (5.3) |
| `Trade/TradeQuote.cs` `Quotes.Money` and `PerUnit` (English "cr", "m" made in the sim); `Game/UI/NewGame/UiKit.Money`; about 35 Game files calling them; `localization/en.csv` rows that say "mites" (14) or "crowns" (8) | replaced by `Core/MoneyText` (keys and parameters, no English in the sim, Phase 6 rule) and `UiKit.Money(amount, MoneyStyle)`; the rows rewritten with a `{n:money}` parameter kind (5.7) |
| `Core/ChronicleText` parameters | a `money` parameter kind, formatted at display |
| `Production/Resources/Extraction.cs` ("stone and gold are not worked in Phase 2"), `Prospecting` (`MineralType.Gold`, the crown's right to silver and gold, `LicenceFeePreciousMites`, royalty 10%) | `mining_gold` and `pan_gold` extraction kinds; the royalty unchanged |
| `content/base/economy/money.jsonc` | additive keys (5.8) |
| `content/base/goods/*` | new goods `gold_ore`, `gold` (bullion); `copper` exists |
| `content/base/sites/loot_tables.jsonc`, `materials/metals.jsonc` | gold and silver item values from material (`economy-audit.md` F-2) |
| **New** | `Economy/Mints.cs` (derived mint rows, the strike desk), `Economy/CoinStock.cs` (world counts by metal, the gold book and loose pool), `Economy/MoneyChanger.cs`, `Save/CoinageSave.cs`; business kind `money_changer`; room type `mint_house`; office `MintMaster` (appended to `Rule` office kinds) |

## 5. Rules

### 5.1 Mints
- **Where:** one mint per realm with a town in the region: the realm's seat if it is a town of the region, else its largest town (ties by settlement id). Derived from the region, never saved. Realms outside the region strike in the continent tick (their summaries' money supply grows by the same rule as history v1). A mint needs a `mint_house` room (roofed, lockable, a strongbox and a minting bench) in a building of the realm's treasury; generated towns that hold a mint get the room in their civic plan (`blueprints/civic_buildings.jsonc`).
- **Staff:** a `MintMaster` office (Trading >= 30; wage 500 a day from the realm's treasury; honesty and skimming by the Phase 6 rule, on the seigniorage he handles) and 2 moneyers (skilled, 350). A moneyer strikes 3,000 pieces a working day; deliveries beyond the day's capacity wait in the mint's store and are paid when struck (the queue is saved).
- **Delivery:** anyone may sell bullion (`gold`, `silver`, `copper` goods) at the mint's counter in market hours (06-18). The mint takes it into its store (a `Store.Move`, then `Destroy` reason `Materialised` when struck: the metal becomes coin).
- **Silver** (unchanged arithmetic, G-M1): `pieces = floor(mg / 1,300)`; `struck = pieces x 100`; `seigniorage = floor(struck x bp / 10,000)` with `bp = 750` (content `mint_seigniorage_bp`, 500-1,000); the seller gets the rest; the dust (mg) stays in the mint's store as `silver`.
- **Gold:** `pieces = floor(mg / 1,100)`; `struck = pieces x 1,000`; seigniorage at `gold_seigniorage_bp` 500; the rest to the seller; dust kept.
- **Copper (token):** the mint pays the seller the book bid for the copper from the realm treasury (a `Trade` transfer), then strikes `pieces = floor(mg / 5,000)`, `face = pieces x 10`, into the treasury (`Ledger.Mint`, `SourceReason.Mint`). The treasury's gain (face less the metal paid, about 91%) is seigniorage. **Copper cap:** the mint strikes copper only while the region's copper face (`CoinStock.CopperPieces x 10`) is at most `copper_cap_permille` 40 of the region's money supply (`ConservationReport.Circulating`); a delivery beyond the cap is struck up to it and the rest handed back; chronicle `mint_copper_capped` once a season per mint.
- **Money supply growth** is set by the metal mined, not by the mint: the audit's F-4 sets deposit grades so the region's worked deposits at their generated crews mint 1-2% of the money supply a year (EA-10). The mint never refuses silver or gold.
- **Continent:** caravans and the export desk pay in coin of the payer's realm; value is value (no exchange rate between realms in Phase 7; all realms strike to the same standard). A realm's different seigniorage is the only difference (X13).

### 5.2 Gold pieces in purses (the gold book)
Purses hold **value** (the ledger, unchanged). Gold is tracked as pieces only where it matters, for **opted-in purses**: `Treasury`, `Chest`, `Hoard`, `Caravan`, the hero's and his party's `Person` purses, the households of lords, and businesses of the kinds `money_changer` and lenders with a counting house. Every other purse holds silver and copper.
- **Invariant:** for an opted-in purse, `gold_pieces x 1,000 <= balance`. World: `sum(opted-in gold) + loose = struck_gold - melted_gold` exactly (`CoinStock`). **Loose** gold is gold in circulation in purses that do not track it (a peasant paid with a gold piece holds it as value).
- **Large payments move gold** (the hook on `Transfer`, deterministic, no draw): from an opted-in payer with `g` pieces, a transfer of `A >= gold_transfer_min_mites` (10,000 = 10 gp) moves `min(g, A / 1,000)` pieces to the payee if the payee is opted in, else to loose.
- **Clamp (making change):** after any transfer from an opted-in purse, if `g x 1,000 > balance`, the excess pieces go to the payee (opted in and within its own invariant) or to loose. No value moves in the clamp; only the count.
- **Changing:** an opted-in purse gets gold by exchange at a money changer (5.4) from loose (or the changer's own pieces); if loose is empty and the changer has none, there is no gold to be had in the region that day.
- **Cost:** the hook runs only for opted-in payers (a bit per purse); every other transfer is unchanged.

### 5.3 Coin mass
`CoinMassG(ledger, purse) = (gold x 1,100 + (balance - gold x 1,000) x 13) / 1,000` grams (silver and copper counted as silver, as today; copper change in a purse is at most a few coppers). A chest of 50,000 mites (50 gp) weighs 650 g in silver and 55 g in gold: war chests, ransoms and tax wagons are lighter in gold (`army-economy.md` 4.5).

### 5.4 Money changers
- A business kind `money_changer` (founded in towns; owner a merchant with Trading >= 40 and >= 50 gp; station a counting table in a `shop` room); generated towns of >= 1,500 people get one at the Phase 7 seeding step (keyed on the region seed and settlement id; the overview binds the step).
- **Exchange:** silver to gold or gold to silver at `changer_fee_permille` 20 of the amount (a `TransferReason.Exchange` to the changer's purse); pieces move between the changer's stock, loose and the customer. Q on the changer > Change money.
- **NPC policy (S5):** a treasury sending more than 100 gp away (a pay chest, a tribute, a ransom) changes to gold first if a changer is in its town; a lord's household holding more than 200 gp keeps half in gold. Others never change.

### 5.5 Melting and hoards
- **Melt** (`SinkReason.Melt`, unchanged): an opted-in purse's gold pieces melt to `gold` at 1,100 mg each; silver at 1,300 mg per 100 mites; copper pieces from the world count at 5,000 mg each (a loss of about 9 mites a piece to whoever melts it: nobody does unless copper rises above 2 mites a gram). The value melted leaves the money supply; the metal is a good again.
- **Hoards** (`Hoard` purses) are opted in; a hoard placed by history or by a dead household holds gold in proportion to its owner's (lords 50%, others 0). A found hoard lists its pieces.

### 5.6 Gold mining
- `gold_ore` (a vein in a `MineralType.Gold` body, worked by `mining_gold` at the miner's 500 kg a day) is dressed and washed like iron ore (`industries.md` iron stages 1-3) and refined: `refine_gold` 1,000 kg of dressed gold ore + 40 kg of charcoal -> gold at the deposit's grade (default 6 g a tonne; F-4 sets it), worked at a `cupel_hearth` (master, 20 h).
- `pan_gold` on streams within 40 tiles downstream of a gold body (`prospect_rules.pan_upstream_tiles`): 0.3 g a worker-day in summer and autumn, 0 in winter; the stream's placer stock depletes (resources rule: 500 g a tile, no regrowth).
- The crown holds the right to silver and gold (Phase 5); its royalty of 10% applies.

### 5.7 How sums and prices are shown
One function, `Core/MoneyText`, returns a text key with integer parameters; `UiKit.Money` formats it. Styles:
| Style | Rule | Example |
|---|---|---|
| **Amount** (prices, wages, values, ledgers, chronicle) | money of account, greedy: gp, sp, cp; zero parts dropped; at 100 gp and above the copper is dropped and shown as "about" in tooltips | 1,234 mites = "1 gp 2 sp 3 cp"; 150 = "1 sp 5 cp"; 250,000 = "250 gp" |
| **Exact** (Ledger detail, trade quotes, the balance sheet's cells on demand) | as Amount, the mites as a tenth of a copper | 1,234 = "1 gp 2 sp 3.4 cp" |
| **Pieces** (an opted-in purse: the hero's coin, a chest, a hoard) | the pieces held: gold counted, silver and copper from the rest | "3 gold, 47 silver, 6 copper (26 g)" |
| **Per unit** (asks and bids of bulk goods) | the smallest pack of 1, 10, 100 or 1,000 kg (or litres) whose price is at least 1 cp | `iron_ore` 2.6 mites a kg = "2 sp 6 cp per 100 kg"; `wheat` 45 a kg = "4.5 cp a kg"; a piece good per piece |
| **One unit** (the Industry tab's balance sheet, `industries.md` 6) | silver with one decimal, sortable | 1,481 mites = "14.8 sp" (a setting switches it to copper) |
Sorting always uses mites. The localisation rows that print money take `{n:money}` (Amount) or `{n:money_exact}`; the 22 rows that say "mites" or "crowns" are rewritten (the sweep is a CN-12 grep test). The `?` overlay and the Codex get a page "Coin": the three coins, their metal, the mite as a tenth of a copper.

### 5.8 Content (`economy/money.jsonc`, additive)
`gold_piece_mg` 1,100, `gold_seigniorage_bp` 500, `copper_piece_mg` 5,000, `copper_cap_permille` 40, `changer_fee_permille` 20, `gold_transfer_min_mites` 10,000, `moneyer_pieces_per_day` 3,000, `mint_master_wage_mites` 500, `gold_grade_mg_per_t` 6,000, `pan_gold_mg_per_day` 300. Goods: `gold` (bullion, 909,090.9 mites a kg, class metal, inert), `gold_ore` (priced by the R1 rule). Extraction: `mining_gold`, `pan_gold`. Recipe: `refine_gold`. Room: `mint_house`. Business: `money_changer`. Office: `MintMaster`.

## 6. Saves
- **No version bump** (section 2.3). New optional section **`coinage`**, absent while empty (no gold struck, no copper struck, no exchange, no melt of gold or copper since the save began): `{ GoldStruck, GoldMelted, CopperPieces (struck minus melted), Loose, OptedGold: sorted (purse index, pieces) for non-zero rows, MintQueues: per mint (realm id, metal, mg waiting, seller purse), MintSeason: per mint (pieces struck this season by metal) }`. Its absence means: no gold anywhere, no copper coin, every purse silver; that is exactly a pre-Phase 7 world, so an old save derives it with nothing to write.
- Mints are derived (5.1); their offices live in the Phase 6 `fiefs` section (office rows, as any office; appointed by the rule's vacancy pass once the runtime attaches, so a load and re-save without running keeps the bytes, as Phase 6's seeding did).
- `TransferReason.Exchange` is appended (the ledger's per-reason counts pad, as for `Alms`).
- `CoinHash` (the stock, the gold book, the queues) joins the hash trail only once non-empty.

## 7. RNG streams
Coinage draws nothing: every rule above is deterministic. **0x710** is reserved for coining and clipping crimes (Phase 8), asserted never drawn. Free: 0x711-0x71F.

## 8. Chronicle
| Type | Cause kind | Root | Key | Parameters |
|---|---|---|---|---|
| `mint_struck` | Trade | yes (seasonal summary, one per mint a season with any strike; compacted with commoners' entries) | `ch_coin_mint_struck` | mint town, gold, silver, copper pieces, seigniorage (`money`) |
| `mint_copper_capped` | Trade | yes | `ch_coin_copper_capped` | mint town, kg handed back |
| `gold_found` | Trade | no (caused by the `ore_strike` rumour's entry or the prospect) | `ch_coin_gold_found` | place, finder |
| `hoard_found` | Other -> the hoard's history entry | no | `ch_coin_hoard_found` | finder, place, pieces by metal |
Rows go into `chronicle/types.jsonc` with `specific_requires` and `note_keys` (Phase 6 format).

## 9. UI (960 x 540)
- Every money string through `UiKit.Money` (section 5.7). The **Character** coin line shows Pieces and mass ("3 gold, 47 silver, 6 copper; 26 g"). **Trade** shows Per unit prices and the running total as Amount, with Exact in the tooltip.
- **Mint counter** (Q on the mint master or the minting bench > Sell bullion): the metal in your pack, the rate ("1 kg of silver: 769 sp less 7.5%: 711 sp 5 cp"), the queue if the moneyers are behind, the copper cap when near it.
- **Money changer** (Q > Change money): a two-column panel (silver, gold), an amount field, the fee, the pieces available today.
- **Ledger:** amounts as Amount; a toggle "exact" per table.

## 10. Golden tests
| id | Case | Expected |
|---|---|---|
| CN-1 | A Phase 6 save (seed 1234 OV-8 trail point) with the hero holding 12,345 mites | shows "123 silver, 4 copper" (Pieces) and "12 gp 3 sp 4.5 cp" (Exact); the load report has the coin notice and asks no confirmation for it |
| CN-2 | Load the CN-1 save and save without running | byte-identical; no `coinage` section |
| CN-3 | Silver strike, G-M1 numbers | 10,000,000 mg at 750 bp: struck 769,200; seigniorage 57,690; seller 711,510; dust 400 mg |
| CN-4 | Gold strike: 1,000,000 mg at 500 bp | 909 pieces; struck 909,000; seigniorage 45,450; seller 863,550; dust 100 mg |
| CN-5 | Copper token: 50 kg at a bid of 160 mites a kg, supply 125,000,000, no copper yet | 8,000 to the seller (`Trade`); 10,000 pieces; 100,000 face minted into the treasury |
| CN-6 | Copper cap: copper face 4,990,000 of a 125,000,000 supply, 10 kg delivered | 1,000 pieces struck (to the cap of 5,000,000), 5 kg handed back; `mint_copper_capped` |
| CN-7 | Large payment: a treasury with 50 gp in pieces and 80,000 mites pays 25,000 to an opted-in captain, then 300 to a household | 25 gp move to the captain; no gold moves with the 300 |
| CN-8 | Clamp: a chest of 5 gp and 5,000 mites pays 300 to a household | 1 piece to loose; chest 4 gp, 4,700 mites; world gold unchanged |
| CN-9 | Exchange: 10,000 mites of silver to gold, loose 20 | fee 200 (`Exchange`); 9 gp to the hero; loose 11 |
| CN-10 | Mass: 9 gp and 1,000 mites in the hero's purse | 9.9 g + 13 g = 22.9 g (130 g all in silver) |
| CN-11 | Display | 1,234 -> "1 gp 2 sp 3 cp"; 150 -> "1 sp 5 cp a day"; `iron_ore` at 2.6 a kg -> "2 sp 6 cp per 100 kg"; `wheat` 45 -> "4.5 cp a kg"; 1,481 -> "14.8 sp" |
| CN-12 | No English money in the sim; every money-bearing row uses `money` | grep: no "cr", "mites" or "crowns" in sim strings; the 22 rows rewritten |
| CN-13 | Mints wired, a 1-year soak of seed 1234 | `Minted > 0`; `Sourced = Seed + Mint + Immigrate`; the audit exact |
| CN-14 | World gold conservation over a year with strikes, exchanges, payments, clamps and a melt; save mid-year | `sum(opted) + loose = struck - melted` every day; save, load, save identical |
| CN-15 | Melt 3 gp from a chest | 3,300 mg of `gold`; 3,000 mites sunk (`Melt`) |
| CN-16 | A Phase 6 save attached and run for one season | the mint master appointed; delivered silver struck; no gold anywhere; `coinage` written only after the first strike |

## 11. Budgets
The gold hook: one flag test on every `Transfer` (no measurable cost), <= 0.2 us for an opted-in payer; mints <= 0.1 ms a game day a region; `coinage` <= 20 KB a region at 10 years (opted-in purses are a few hundred); `MoneyText` 0 allocations for Amount (a cached key and three ints).

## 12. Open questions (recommended defaults)
| # | Question | Default |
|---|---|---|
| CNQ-1 | **For the user:** store sums as whole copper (as the plan's text says) or keep the existing finer unit, the mite, as a tenth of a copper? | **Keep the mite** (option A): nothing in any save changes, prices of cheap bulk goods stay exact, and every sum still shows as gold, silver and copper. Whole-copper storage costs about a month and changes nothing a player sees |
| CNQ-2 | **For the user:** copper pieces as token coin (worth their face, not their metal), capped at 4% of the money supply? | **Yes.** A full-weight copper piece would weigh about 60 g; history's small change was token or debased |
| CNQ-3 | Track gold pieces in purses? | Only in opted-in purses (5.2): treasuries, chests, hoards, caravans, the hero's party, lords, changers |
| CNQ-4 | **For the user:** the Industry tab's one unit | silver with one decimal ("14.8 sp"), switchable to copper |
| CNQ-5 | Gold to silver ratio | 11.8 : 1 (1.1 g of gold for 10 sp) |
| CNQ-6 | Exchange rates between realms | none in Phase 7 (one standard); seigniorage differs |

## 13. Estimate and parts
| Part | Content | Gate | Months |
|---|---|---|---|
| **P7-CN1** sim | `Coin` helpers, `MintMath` per metal, mints wired with offices and queues, `CoinStock` and the gold book, the changer, melt, mass, gold mining and refining, content, the `coinage` section, the load report's notices | CN-2 to CN-10, CN-13 to CN-16 | 0.5 |
| **P7-CN2** display | `MoneyText` and `UiKit.Money` styles, the sweep of about 35 Game files and 22 localisation rows, the `money` chronicle parameter, the mint and changer panels, the Codex page | CN-1, CN-11, CN-12 | 0.3 |
**About 0.8 months of code.** The plan's "about 2 weeks" did not include wiring the mints, which Phase 2 left unbuilt (S-3).

## Notes (P7-CN1)
CN1 built the sim side. The gold book lives on the ledger (`Ledger.Coinage`, a `CoinStock`); every opted-in purse is a treasury, chest, hoard or caravan by kind, or one a part of the game opts in (the hero's purse is opted in when he sells metal or changes money). `AddStruck` puts new gold in the receiving purse's count when it tracks gold, else in the loose pool, so `sum(opted) + loose = struck - melted` holds after every transfer. A mint strikes in queue order within its moneyers' day (2 x 3,000 pieces); the hero's walk-in delivery goes to the front of the queue, the businesses' standing deliveries (gold and silver in their stores, sold each morning at hour 0) behind it. The seller is paid by minting into the seller's purse and moving the seigniorage to the realm treasury as `Seigniorage`; gold pieces ride the hook. Stores count whole grams, so the mint keeps the metal waiting (mg) in its queue and holds the store at the grams that cover it, rounded up; a melt carries the milligrams short of a gram in the section. The mint needs a mint master in office (the rule's vacancy pass fills it, Trading best in the town; a town without a 30 takes the best there is); a world with no rule book strikes without one. Copper is struck at once at the counter, capped, with the metal over the cap handed back; nothing sends copper by itself. Strings use the `co_` prefix (the `cn_` keys are construction's). Deviations and what is left: no money changers are seeded into towns (they are founded businesses of kind `money_changer`, and the hero can use any open one), the NPC change policy and the gold book beyond the opted kinds are cut-order item 10; no `mint_house` room or civic plan; moneyers are a capacity, not people; realms outside the region do not strike; `pan_gold`, `gold_found` and `hoard_found` are not built. With the mints wired, seed 1234's hearths already send enough silver to saturate the moneyers' day; F-4's deposit grades are the tuning that brings growth to 1 to 2% a year.

P7-CN2 built the display. `Core/MoneyText` is the one function: `Format(mites, style, words)`, where `words` is the `mt_*` key table (`Words` falls back to the symbols gp, sp and cp for a key the table lacks, so a sim-side caller such as `Quotes.Money` or the chronicle's `money` parameter needs no translator). Amount rounds half up to a whole copper (12,345 mites = "12 gp 3 sp 5 cp"; Exact keeps the tenth, "12 gp 3 sp 4.5 cp"), shows a sum under a copper as tenths ("0.4 cp"), and from 100 gp rounds to the silver. **Deviation, the per-unit pack:** 5.7 says the smallest pack of 1, 10, 100 or 1,000 kg "whose price is at least 1 cp", but its own examples (`iron_ore` at 2.6 mites a kg = "2 sp 6 cp per 100 kg", `wheat` at 45 = "4.5 cp a kg") need a floor between 27 and 45 mites, so the floor is 3 cp (`MoneyText.PackFloorMites`, 30). The localisation rows take `{n:money}`, `{n:money_exact}` and `{n:money_unit}` through `MoneyFormat` (a custom formatter in `string.Format`, so a row keeps its positional parameters and the callers pass raw mites); `HeroText` now hands whole numbers to the formatter instead of strings. The per-kg cells of the Business panel and the Industry gate line use `PerUnit`; the Industry tab uses OneUnit. Origin coin and every old "crown" figure in the UI were mites already; the fixed gift of the Faction screen (10,000 mites) reads "10 gp", alms "1 sp". `CoinView` and `CoinPanel` (Q in a town > Sell metal at the mint, Change money): the mint counter lists gold, silver and copper in the pack with the rate after the mint's cut and the sell 100 g, 1 kg and all buttons, the moneyers' queue, and the refusals the sim gives (no mint, no master in office, hours); the changer has a silver column (change 1, 5 or 10 gp worth with the fee shown) and a gold column (1, 5, 10 or all pieces to silver), the fee and the gold to be had today. Left out: the Character screen's Pieces line with the gold book's count (it needs gold pieces in `HeroView`, a sim-side field), the Codex and `?` page "Coin", the Ledger's per-table "exact" toggle and the settings switch for the Industry tab's copper unit (the style exists), a free amount field in the changer (fixed steps), the changer's NPC change policy and seeding (CN1's list), and a screenshot.
