# Rule and income: the fief as a holding, estates and food rents, vassal dues, councils and subsidies, tallage, customs, officials honest or corrupt, loans and mortgages, charters as politics

Plan refs: §3j (revenue follows the feudal chain; every payment a real transfer; demesne and food rents; military service, aids, relief, wardship, marriage, escheat and forfeit; unpaid dues become grievances and casus belli; hearth tax; subsidies granted by a council of great lords whose agreement depends on their opinion of the ruler and the war's cause; tallage; customs and tolls, market and town charter fees; fines and forfeitures, seigniorage; clerical taxes and vacant bishoprics; loans move existing coin; officials collect, the player sets policy; officials honest or corrupt, skimming a crime with witnesses; the Ledger shows receipts by source, arrears, grievances and each official's record; treasuries are physical; Phase 6 schedule: estates and food rents, vassal dues, subsidies and councils, tallage, customs, officials and corruption, loans), §4A.2 (government: a fief is a deeded set of parcels plus the rights to tax, levy and judge; reeve; mayor and council; a town is a village granted a market charter; taxes and arrears to eviction; founding charters from rulers, petition, revocation, market charters in Phase 6), §3.9 (Ledger, Fief tab: zoning, taxes, levies), Phase 5 decisions (stewards honest in Phase 5, skimming with Phase 6 officials; mortgages Phase 6; charters: the petition as politics and revocation in Phase 6).

## 1. Purpose
Turn the Phase 2 tax plumbing into government a player can hold. A fief becomes a deed with rights; its lord sets policy, appoints officials who are people (and may steal), collects what his tenants and vassals owe, and answers to his own overlord with dues of the historical kinds. Councils grant the ruler money only if they like him; merchants lend only coin that exists. Charters to found and to hold markets are political decisions with conditions that can be broken. **Done-when (part):** you can rise to lord (with `diplomacy-and-ai-lords.md`).

## 2. Dependencies
| Needs | From |
|---|---|
| `TaxSystem` (hearth tax, four levels, rents, arrears ladder to eviction, crown share), `Treasuries` (realm, fief, town, parish purses), `Tolls`, the Ledger reasons | Phase 2 |
| `LandRegistry` (`HoldingKind.Fief` reserved, `FoundingCharter`, `CharterState`, `GrantorKind`, `Deed.Lord`), leases with shares or coin, `EstateWorld.OnDeedInherited` / `OnFiefPassed` hooks | Phase 5 |
| `StewardBrain` (honest), stream 0x540 reserved for honesty | Phase 5 |
| Caravans at border gates, ports | Phase 2 |
| Standing, crime (embezzlement), opinion and treaties | `reputation-and-standing.md`, `crime-and-justice.md`, `diplomacy-and-ai-lords.md` |
New: `Sim/Rule/` (`FiefBook`, `Dues`, `Council`, `Officials`, `Customs`, `Loans`, `Charters`, `FiefPolicy`), content `rule/dues.jsonc`, `rule/offices.jsonc`, `rule/customs.jsonc`. Extended: `TaxSystem` (collection through officials), `Treasuries` (receipts by source, arrears and grievances per fief), `LandWorld` (fief deeds, mortgages, charter revocation), the Ledger (Fief tab, alerts), the map (Land overlay).

## 3. Data
- **Fief deed** (`HoldingKind.Fief`, now used): `{ Fief, Holder (the lord's person and household), Overlord (realm ruler or a greater lord), Rights (Tax, Levy, Judge, Market), Service (men x days a year), Policy }`; the region's fiefs get deeds at the seeding pass from the Phase 1 lords. A **manor** (one village and its demesne) is a sub-fief deed under a fief (the hero's first grant).
- **`FiefPolicy`** (saved): tax level (the four Phase 2 levels), rent form per lease (coin or a share in kind: the Phase 2 lease pick, now the lord's choice), tolls and market days, the lord's law per crime (`crime-and-justice.md` 4.6), zoning (parcels: lots, fields, waste, common; new streets, `holdings-and-works-v2.md`), customs rates (a realm ruler).
- **`Office`** (saved): `{ Kind (Reeve, Bailiff, Steward, Sheriff, CustomsCollector, Gaoler, Treasurer), Holder (person), Fief or realm, Wage (mites a day, from `offices.jsonc`: reeve 150, bailiff 350, sheriff 500, collector 350, gaoler 150), Honesty (0..100), Skill (Law or Trading), AppointedDay, Record (collected, expected, skimmed found) }`.
- **`DueRow`** (saved): `{ Kind (Service, Aid, Relief, Wardship, MarriageFee, Subsidy, Tallage, Rent), Payer, Payee, AmountMites or kind (goods), DueDay, PaidMites, State (Due, Paid, Arrears, Waived, Grievance) }`.
- **`Loan`** (saved): `{ Lender (purse), Borrower (purse), PrincipalMites, RatePmPerSeason, StartDay, TermSeasons, Security (none, a deed: a mortgage; or future customs), Repaid, State (Open, Repaid, Default) }`.
- **New `TransferReason`s (appended):** `Relief`, `Aid`, `Subsidy`, `Tallage`, `Customs`, `Loan`, `Interest`, `Skim`, `Fee` (shared with medicine and the church).

## 4. Rules

### 4.1 Estates and food rents (demesne)
Each fief's demesne parcels (`OwnerKind.Demesne`) are worked by the lord's hired servants (Phase 2 labour) and by **labour service**: each tenant household owes 1 day in 5 during the plough and harvest windows (credited as worker-hours on demesne fields, recorded as a due paid in labour; a tenant who misses it owes 150 mites a day). **Food rents:** where the lord sets rent in kind, the share (20-33% of the harvest, Phase 2) goes into the lord's barn as goods; the lord may **commute** a lease to coin at the season's book price (one policy switch a year). An itinerant lord's household eats from the barn of the manor where it is that day (stores move only by cart, Phase 5 rule).

### 4.2 What vassals owe (the feudal chain)
| Due | Rule | Unpaid |
|---|---|---|
| **Service** | `men x days` a year (content: a manor 1 man 7 days, a fief 1 man per 50 households 7 days), called by the overlord: the vassal or his men report to the overlord's seat (a contract: guard duty) | grievance (opinion -20), then scutage in Phase 7 |
| **Aid** | on three occasions of the overlord: his ransom, his eldest son's knighting (at 16), his eldest daughter's marriage: one season's hearth tax of the vassal's fief | grievance -20, arrears |
| **Relief** | when an heir takes a fief or manor deed (`OnDeedInherited`): 25% of the fief's last-year receipts, due in 2 seasons | the deed held but `Arrears`; after 4 seasons forfeit by the overlord's court |
| **Wardship** | an heir under 16 (`OnFiefPassed`): the overlord takes the fief's receipts until 16, paying the guardian 20% for keep | - |
| **Marriage** | heirs and widows of fiefs need the overlord's consent; he may sell it: fee one season's receipts (`family-and-lifespan.md` 4.1) | marrying without: a fine of two seasons' receipts, opinion -30 |
| **Escheat, forfeit** | the line dies out (Phase 5) or a treason conviction (`crime-and-justice.md`): the fief deed returns to the overlord | - |
Unpaid dues are `DueRow`s in `Grievance`: each counts in the overlord's opinion of the vassal and is a casus belli (`diplomacy-and-ai-lords.md`).

### 4.3 General taxes
- **Hearth tax and the crown share:** Phase 2, unchanged in amount; now **collected by the reeve or bailiff** (4.8): what reaches the treasury is what the official hands over.
- **Subsidy:** the ruler calls a **council** (the realm's great lords: fief holders of the region and the continent tick's abstract lords) and asks a tenth of movables in towns and a fifteenth in the countryside (household coin plus stock at book value, Phase 2) for a stated cause. Each member votes yes when `opinion of the ruler + cause weight + Range(-10, 10) >= 0` (stream **0x662**, key council and member): cause weights: defending the realm +40, a war with a casus belli +10, no war -30, a second subsidy within 2 years -20. Passed by a majority of members (weighted by their households); collected over the next season by the officials; each paying household +1 grievance that season. NPC rulers call one when the treasury holds less than 2 seasons of payroll and a war is on.
- **Tallage:** the ruler (or a lord on his own demesne towns) takes up to a tenth of a town's movables at will, no council: +2 grievances a household, opinion of the town's mayor -30.

### 4.4 Customs, tolls, charters
- **Customs** at the realm's border gates and ports: 3% of the declared value of goods entering or leaving by caravan (content per good class: wool export 10%, wine import 5%), paid by the caravan's owner to the collector (4.8). A ruler sets rates within 0-15%; above 8% the continent tick sends 10% fewer caravans by that gate per point. **Smuggling** (goods past a gate or along tracks avoiding it) is a crime when seen by a collector or a patrol.
- **Tolls** (Phase 2 bridges and gates) and **market tolls** stay; the lord sets them.
- **Market charter:** a settlement of >= 200 people with an open square lot may petition its realm's ruler: fee 50 crowns plus 5 crowns a year; granted by 4.9's rule; the settlement becomes a town (market days, stalls, market tolls to the charter's holder, a mayor and council chosen from its burghers).

### 4.5 Justice and church income
Fines and forfeitures go to the fief treasury (`crime-and-justice.md`); seigniorage stays Phase 2's; clerical taxes and vacant sees go to the ruler (`faith-and-church.md` 4.2).

### 4.6 Loans and mortgages (existing coin only)
A borrower (a ruler, a lord, any household) asks a lender (a merchant household or business with coin >= 2 x principal, a guild chest, a see where usury is allowed; none where the lender's religion forbids usury) for principal P at a rate of **4% a season** (16% a game year; consistent with leases priced per season, Phase 5), plus 2% for a borrower with arrears or standing < 0 with the lender's town. Coin moves lender -> borrower (`Loan`), interest each season (`Interest`), the principal at term. Security: none (rulers: future customs or taxes assigned), or a **deed (a mortgage)**: on default the deed passes to the lender (`DeedCause.Forfeited`, Phase 5 cause reused). **Default** (a payment 2 seasons late): the lender's household loses the coin it lent (he may close his business if it was > 50% of his wealth: Phase 2 closure), the borrower's credit closes (no lender of that town for 2 years), opinion -40 between them, a chronicle `loan_default` with the cause. **No coin is created**: MintMath and the ledger audit stay exact (RI-13).

### 4.7 Squatters and eviction
A lord's bailiff serves notice on an unlicensed claim in his fief (`Claim.Squatting`, Phase 5) at the next S6 after he hears of it: pay the licence (10 crowns an acre) within 15 days or leave; then guards evict (the claim `Pulled`, `DeedCause.Evicted`, improvements to the lord; a squatter who resists is arrested). NPC lords evict by policy (`greedy` always, `just` after a second notice). Phase 2 tenants' arrears to eviction are unchanged.

### 4.8 Officials, honest or corrupt
- **Appointment:** the lord (the player in his fief) appoints from people he knows with the skill (Law for bailiffs, sheriffs, gaolers; Trading for collectors and treasurers; any adult as reeve, chosen by the village if the lord does not). **Honesty** is rolled once per person the first time it matters: `Range(10, 100)` biased by traits (stream **0x540**, the Phase 5 reserved stream, now drawn; key person id). Phase 5 stewards get their honesty the same way at the seeding pass.
- **Skimming:** an official keeps `skim = max(0, (60 - honesty) / 4)` % of what he collects (honesty 20 -> 10%), moved to his own purse with `TransferReason.Skim` (the ledger is honest; the Ledger view shows only what the lord could know: receipts against an estimate of expected collections). Skimming is the crime `embezzlement`; a clerk or another official who handles the same chest **sees** it each season with P = 20% (0x661, key official and season) and reports by the crime rule.
- **Audit:** the lord (or his treasurer) audits an official once a season: found with P = `min(90, 30 + auditor's Law - official's Law / 2)` % (stream **0x661**, draw 1); found -> a crime record, restitution, dismissal, the court. The Ledger's **official's record** shows collections vs expectation (an honest bailiff's shortfall is from arrears; a thief's is steady).
- **Bribes:** officials of honesty < 60 may take bribes by the one rule (`heists-and-jailbreaks.md` 4.3: an offer of at least `3 x daily wage x (60 - honesty) / 20`, accepted with P = `(60 - honesty)` %); a collector who takes one lets a caravan pass for a third of its duty.

### 4.9 Charters as politics (Phase 5's simple rule replaced)
A **founding charter** or **market charter** petition (in person at the ruler's seat, or through a vassal lord) is granted when `ruler's opinion of the petitioner + standing with the realm / 10 + 5 x fee in tens of crowns - 50 if the land is contested - 20 if the petitioner is of another realm + traits (ambitious +10) >= 40`; a refusal names the largest negative term. **Revocation:** a lapsed founding charter (Phase 5: deadline missed) is revoked at the next S6 unless the holder pays 50% of the fee for one extension of the deadline (once); a charter whose dues are 2 seasons in arrears is revoked the same way. Revoked: the charter deed and the claim's tiles return to the grantor (`DeedCause.Forfeited`); settlers stay as the grantor's tenants. Granting and revoking are chronicle entries.

### 4.10 Tick placement
| Where | What |
|---|---|
| H6 S3, S4 | rents in kind and coin, hearth tax through officials, skimming, dues falling due, labour service accounted |
| H6 S5 | interest and loan terms, customs settled for the season, relief and wardship |
| H6 S6 | councils and their votes, charters (petitions decided, revocations), evictions, audits, appointments by NPC lords |
| H1 | customs at gates as caravans pass |
| Barrier | the hero's policy changes, appointments, petitions, loans |

## 5. Determinism
Streams: **0x540** official and steward honesty (drawn from Phase 6 on), **0x660** reserved for a later purveyance (Phase 7, asserted unused), **0x661** skim seen and audits, **0x662** council votes, **0x663** NPC borrowers' choice of lender ties (key borrower and season), **0x664** smuggling routes (key caravan and day). Everything else is ordered by fief, then household id. `FiefHash` covers fief deeds, policy, offices, dues and loans.

## 6. UI (960 x 540)
The Ledger's **Fief** tab: receipts by source (hearth, rents in coin and kind, tolls, market, fines, customs, subsidy, tallage), arrears, grievances per settlement, dues owed up and down the chain, each official's record, loans; policy controls (tax level, rent form, tolls, market days, the lord's law, customs for a ruler); **alerts** (an official's shortfall, a due in arrears, unrest over a rung). The map's **Land overlay** (Phase 5 carry-over): fiefs, demesne, glebe, commons, claims, squatters. A council panel when the ruler asks (each member's vote and his reason).

## 7. Tests (golden)
| id | Case | Expected |
|---|---|---|
| RI-1 | Fief deeds at seeding, seed 1234 | every region fief has a deed with holder, overlord and rights; counts equal the Phase 1 lords |
| RI-2 | Food rent in kind | a 30% share lease of 20 acres at 38 net measures an acre: 228 measures into the lord's barn at harvest; commuted next year: coin at the book price |
| RI-3 | Labour service | 40 tenant households over the plough window: 1 day in 5 credited to demesne; a missing household owes 150 mites a day |
| RI-4 | Relief | an heir takes a fief with last-year receipts 40,000 mites: relief 10,000 due in 2 seasons; unpaid 4 seasons: forfeit by the overlord's court |
| RI-5 | Wardship | an heir of 12: receipts to the overlord for 4 game years, guardian paid 20% |
| RI-6 | Aid | the overlord ransomed: each vassal owes one season of his fief's hearth tax |
| RI-7 | Council | 9 members, opinions [60, 40, 20, 10, 0, -10, -20, -40, -60], defensive war (+40), jitter 0: 8 of 9 yes (every member but the one at -60), passed; a tenth of town movables collected next season; grievances +1 |
| RI-8 | Tallage | a town with 400,000 mites of movables: 40,000 to the ruler; +2 grievances a household; the mayor's opinion -30 |
| RI-9 | Customs | a caravan of 20,000 mites of wool leaving: 2,000 at 10%; at a rate of 12% the gate's caravans -40% |
| RI-10 | Skimming | a bailiff of honesty 20 collecting 30,000: 3,000 skimmed (`Skim`), 27,000 to the treasury; the ledger's audit exact |
| RI-11 | Audit | auditor Law 60, official Law 40: found with 70% (0x661); a crime record, restitution, dismissal |
| RI-12 | Honesty stream | 0x540 drawn only from Phase 6; Phase 5 goldens that asserted it unused are rewritten to assert it unused before the seeding pass |
| RI-13 | Loans conserve | a ruler borrows 100,000 at 4% a season for 4 seasons: 16,000 interest, principal repaid; total coin and the mint audit unchanged across the year |
| RI-14 | Mortgage default | a deed mortgaged for 20,000, two seasons late: the deed to the lender (`Forfeited`), credit closed, opinion -40 |
| RI-15 | Eviction | a squatter's claim: notice at S6, 15 days, then guards; the claim `Pulled`, improvements to the lord |
| RI-16 | Charter petition | terms opinion 20, standing 200 (+20), fee 50 crowns (+25), not contested, same realm: 65 >= 40 granted; contested: 15, refused naming "the land is contested" |
| RI-17 | Revocation | a founding charter's deadline missed: revoked at S6 unless 50% of the fee is paid (one extension) |
| RI-18 | Market charter | a village of 230 with a square: granted; it becomes a town with market days, stalls and tolls to the holder |
| RI-19 | Determinism, save, conservation | a 2-year run with councils, loans and skimming: coin, mass and deeds exact; `FiefHash` equal; save -> load -> save byte-identical |

## 8. Open questions (recommended defaults)
1. **Interest:** 4% a game season (16% a game year). Default as written.
2. **Skimming visibility:** the lord sees only receipts against expectation (the honest shortfall from arrears looks the same at first); audits find the rest. Default as written.
3. **Subsidy rates:** a tenth in towns, a fifteenth in the country (the plan's example). Default as written.

## 9. Budgets
Seasonal rule steps (S3-S6) <= 10 ms a region at the season's slices (spread over the day's hours, never all at hour 0); customs <= 0.02 ms a caravan; a council <= 1 ms; save: fief deeds and policy <= 10 KB, offices 32 B, dues 24 B (about 2,000 a region-year, pruned when paid after a year), loans 40 B.

## Notes (P6-C3, built)
Built in `Sim/Rule/` (the book `RuleBook`/`RuleSave`, the pure arithmetic `RuleMath`, the passes `RuleDues`, `RuleOfficials`, `RuleCouncil`, `RuleCustoms`, `RuleLoans`, `RuleLand`, the tick `RuleSystem`/`RuleEngine`, the hero's `RuleDesk` (`HeroCommandKind.Rule`, tag `ru:`), the Ledger's `RuleLedger`) and content `content/base/rule/{dues,offices,customs}.jsonc` (`rule_settings`, `office`, `customs_rate`). The optional save section is **"fiefs"** (no format bump; written only when the book is non-empty; a new game seeds it, an older save is marked `RuleSeedPending` and derived by `RuleWiring.Attach` so a load-and-resave keeps its bytes, the load report says "Phase 6 state derived at load"). Gate: RI-1 to RI-19 all have a test (`tests/Ravensguard.Sim.Tests/Rule/RuleTests.cs`, `RuleRuntimeTests.cs`).
- **Fief deeds are rows of the rule book, not `Deed` rows of the land book.** `FiefRow` (holder, overlord realm, rights, service, policy, a four-season ring of receipts, a wardship, `Forfeited`) is derived from `Population.FiefLord` at the seeding pass; `HoldingKind.Fief` stays reserved in the land registry because the estate hooks (`OnDeedInherited`, `OnFiefPassed`) are never reached by an NPC fief today. Instead the daily pass (`RuleDues.Succession`) compares each row's holder with the lord the population names: a new holder owes relief and, under 16, puts the fief in wardship; none left is escheat. D2 gives the hero a fief by setting `FiefRow.Holder` (and `RuleBook.Rulers[realm]` for a ruler's powers); a forfeit only marks the row (`Forfeited`, no rights), it does not move the real lord in the population.
- **Streams:** 0x540 is now `RngStreams.OfficialHonesty` (renamed from `StewardHonestyReserved`; the Phase 5 test now asserts it is named only inside `Sim/Rule`), 0x661 (a colleague sees, the audit: draw 0 the audit, draw 1 + the colleague's serial the sighting), 0x662 (votes, draw = the member), 0x663 (lender ties). 0x660 and 0x664 are untouched (smuggling is not built).
- **Tax collection through officials:** `TaxSystem.Collected` (a new hook, null without a book) reports each settlement's round; the bailiff (else the reeve) of the fief handles it, keeps `(60 - honesty) / 4` percent from the fief's treasury to his household (`Skim`), and the book records collected against expected (households x the level's hearth). The Fief tab shows only the shortfall; audits (once a season, hour 23 of the first day) and a colleague's sighting find the rest, and the crime goes to B2's core as `embezzlement` (a witnessed record, reported at once by an audit, left to the daily report draw when only seen). A found official repays the season's skim (`Restitution`) and is dismissed; `official_found_skimming` is a standing deed (A3), as are `fief_forfeited` and `loan_default`.
- **Wages:** each office is paid 15 days from its fief's (or realm's) treasury at hour 12 of a season's first day (reeve 150, bailiff 350 a day...).
- **Councils** use the living holders of a realm's fiefs in the region (weighted by households) with opinion from `RuleBook.Opinions` plus a tenth of the A3 standing; the continent tick's abstract lords do not sit yet. A passed subsidy is a levy taken at hour 23 of the next season's first day from each household's coin and stock at base price (a tenth in a town, 67 permille in the country), one grievance each for a season; grievances are `Grievance.Lord` on the household (the unrest ladder already counts it). NPC rulers call one through `RuleCtx.WarCause` (idle until D2 supplies wars).
- **Customs:** `RuleCustoms.Charge` is called in the caravans' gate trade for what leaves and what enters (real transfer from the caravan's account to the realm's treasury, `Customs`); the trip planner skips days by `RuleMath.CaravanPct` of the realm's rate (no draw: a deterministic stagger). Smuggling and the collector's bribe are not built (the cut order drops them first).
- **Loans** only move coin (`Loan`, `Interest`); the lender needs twice the principal and a faith that allows interest (`ChurchServices.CanOfferLoan`); a mortgage is a deed of the land registry (`LandWorld.Transfer`, `DeedCause.Forfeited`). A lender's business is not closed on a default (left).
- **Charters:** `LandWorld.CharterJudge` (wired by the rule) replaces Phase 5's standing rule with the 4.9 sum; a refusal key is `ld_charter_refused_<term>`. Revocation, the extension and market charters are `RuleLand`; a market charter flips the generated settlement's `Class` to Town again whenever the book is attached (the saved `MarketCharters` table is the record), and the mayor and council it should elect are not built.
- **Eviction (RI-15)** is built end to end up to the guards: a notice at S6 (hour 6), 15 days, then an order. `RuleBook.EvictionOrders` and `RuleCtx.GuardsEnforce` are the hook for C1: when the justice step's guards are installed they return true from `GuardsEnforce(claimSerial, fief, day)`, walk to the claim and call `RuleLand.Complete`; without them the order is carried out at once (`LandWorld.EvictClaim`: the claim `Pulled`, a `DeedCause.Evicted` change, the improvements the lord's). The squatter's arrest for resisting needs C1's arrest.
- **Labour service** is accounted, not worked: at each spring and autumn window's end `SettleLabour` credits `3 days x 8 hours` per tenant household to `RuleBook.DemesneHours` and collects 150 mites a day missed (`Rent` to the fief's treasury). Farming does not yet read the credit to save the lord's hired labour. Commuting rents (`RuleDues.Commute`) switches the lord's leases between a share and the farming default coin rent (320 mites a field-year), once a year.
- **Not built:** the map's Land overlay, a council panel, policy controls on screen (the commands exist; a lord-hero arrives with D2), stewards' honesty drawn at the seeding pass and their skimming (`RuleOfficials.HonestyOf` is the one function to call), bribes, smuggling, the mayor and council of a new town, NPC lords' appointments by policy beyond filling vacancies.
