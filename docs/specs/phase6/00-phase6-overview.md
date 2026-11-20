# Phase 6 specs: overview, build order, carry-overs, shared streams, keys, saves, open questions and risks

Status: design, written before any Phase 6 code (plan §4: spec, then golden tests, then implementation), on top of b5d9234 (Phase 5 closed).
Source of truth for scheduling is `docs/REDESIGN_PLAN.md` §5 Phase 6 ("Society, factions, rule, faith and family", 8 to 11 months). This file ties the seventeen Phase 6 specs together in the style of the Phase 4 and 5 overviews. Conventions that still apply: integer or `Fixed` arithmetic in the sim; counter-based `DetRng` streams keyed on game time, distance or ids (never the live tick, except Phase 4's `FightTick`); stable-id iteration; content as JSON through the `ModLoader`; UI as thin views over snapshots; strings through `tr()`; money in **mites** (1 crown = 100 mites); time: 1 sim-second = 1 real second at 1x = 20 game seconds, a game hour = 180 sim-s, a day = 86,400 game s, a season 15 days, a year 60 days.
Plan sections covered: §3.10 (society: crime with witnesses, bounties, guards, jail, slavery, reputation, contracts), §3.12 (the chronicle and "why" coverage), §3j (realm and lordly income: the Phase 6 list), §4A.2 (government, taxes, the unrest ladder, founding), §4A.4 (diplomacy as utility AI, AI lords), §3b (heists and jailbreaks), §2.10 (fire spread), §1A (history recalibrated). **Decisions bound here (locked with the user):** full family life for the hero; historically harsh justice through the real anatomy, by the local lord's and culture's law, with chronicle causes; a real faith system; slavery fully systemic like Kenshi; plagues rare but devastating, with everyday illness; a real hero lifespan on the slow clock; honest named difficulty sliders; stewards honest in Phase 5, corruption with Phase 6 officials; founding charters' petition as politics, revocation and market charters; eviction of squatters; and (decided Nov 20) **the Phase 4 medicine items first**, replacing the auto-dress stop-gap.

## 1. Document map

| File | Owns | Tests |
|---|---|---|
| `medicine-and-treatment.md` | the paper-doll treatment screen, physicians and barbers as paid people, `medicine/*.jsonc`, crutches and `make_crutch`, Treat utility scoring, the tourniquet's 2 h and 6 h steps, `AbstractFirstAid` test, the auto-dress removed | MT-1 to MT-14 |
| `reputation-and-standing.md` | standing with groups from heard deeds, renown, marks (brand, outlaw, excommunicated, enslaved), reputation passed to kin heirs | RP-1 to RP-10 |
| `crime-and-justice.md` | seen/unseen, witnesses and recognition, the hue and cry, reports, bounties, guards and shifts, gaol (captivity completed, prisoners fed), court days, culture and lord's law, harsh sentences through wounds, the hero as judge, NPC crime | CJ-1 to CJ-17 |
| `slavery.md` | culture law, bonds, slavers and markets, buying, owning, selling, freeing, the enslaved hero, runaways, revolts | SL-1 to SL-14 |
| `family-and-lifespan.md` | courting, arranged matches, dowries and alliances, children and upbringing, kidnapping, kin who turn, the hero's ageing and old-age death, NPC estates' carry-overs | FL-1 to FL-15 |
| `faith-and-church.md` | derived religions, saints, holy days, tithe split, church land and offices, piety, relics, pilgrimages, heresy, excommunication, superstition | FC-1 to FC-14 |
| `rule-and-income.md` | the fief deed, estates and food rents, labour service, vassal dues, councils and subsidies, tallage, customs, officials honest or corrupt, loans and mortgages, eviction, charters as politics | RI-1 to RI-19 |
| `diplomacy-and-ai-lords.md` | opinion, treaties, war utility and peace, `LordBrain`, the road from sellsword to lord and founder, the Faction (F) screen | DA-1 to DA-12 |
| `unrest-and-revolt.md` | grievances, the ladder, riots by real people, revolts that change the fief's holder, slave revolts' machinery | UR-1 to UR-11 |
| `contracts-and-rumours-v2.md` | contracts from need with escrow and deadlines (about 40 kinds), the construction market and day labour, tournaments as fair events, slander, rumour age, legends fallback, mood barks, AP-8 | CR-1 to CR-14 |
| `chronicle-full.md` | text keys and parameters (no English in the sim), specific cause coverage, chains, the effect index, the Chronicle (H) screen | CH-1 to CH-11 |
| `heists-and-jailbreaks.md` | gaols, strongrooms, vaults, undercrofts and drains as sites; guards' shifts, looking and listening; light and noise; locks, keys, bribes, disguise, diversions; indoor fights with walls; the manhunt | HJ-1 to HJ-12 |
| `fire-and-burning.md` | fuel classes, ignition and spread, sparks, smoke, firefighting, abstract building fires, arson, upkeep of thatch and daub, firedamp | FB-1 to FB-14 |
| `disease-and-plague.md` | everyday illnesses with care, the great plague on continent and region roads, quarantine, wages and land prices answering scarcity | DP-1 to DP-12 |
| `history-recalibration.md` | history v1 on the live functions, v0 frozen for library worlds, the continent tick's politics, calibration targets | HR-1 to HR-11 |
| `holdings-and-works-v2.md` | the Phase 5 carry-overs: clearing and sowing a claim, the NPC land market, zoning and new streets, soil improvement, furnishing, room ownership, horse breeding, crafting and construction loose ends | HW-1 to HW-15 |
| `difficulty-sliders.md` | the five honest sliders | DS-1 to DS-7 |

About **222** golden tests plus eight overview integration tests (section 4). Content volume (plan §5 asks about 40 contract types): about 40 contract kinds, 60 deed rows, 20 crimes and 25 punishment keys, 6 illnesses, 4 religion templates and their doctrines, 12 heist templates, about 40 new pieces and props (stocks, pillory, gallows, gaol bars, shackles, altar, reliquary, font, buckets, fire hooks, a surgery table), about 20 recipes, about 150 dialogue lines and barks.

## 2. Module map

```
 P2 people, taxes, treasuries, rumours      P4 wounds, captivity, sites, watch     P5 land, deeds, construction, businesses, estates
        |                                          |                                         |
        v                                          v                                         v
 L1  standing (RP)   chronicle keys (CH core)   medicine (MT)   difficulty (DS)   fire (FB)   holdings part 1 (HW)
        |                    |                     |
        +---------+----------+---------------------+
                  v
 L2  crime core (CJ)   family core (FL)   faith core (FC)   rule and income (RI)   disease (DP)
                  |                                              |
                  v                                              v
 L3  justice: gaol, courts, sentences (CJ)    contracts and rumours v2 (CR)    diplomacy and AI lords (DA)
                  |                                   |                              |
                  v                                   v                              v
 L4  slavery (SL)    heists and jailbreaks (HJ)    holdings part 2 (HW)    unrest (UR)   faith politics (FC)   family politics (FL)
                                                                                    |
                                                                                    v
 L5  the Chronicle screen and coverage soak (CH)   ->   history v1 and the continent tick (HR)   ->   close-out
```

### 2.1 What is new and what is extended
| Existing (code) | Phase 6 |
|---|---|
| `Combat/Wounds/*` (`Treatment`, `TreatAi`, `MedicineTables`, `FieldDressing`, `WoundSystem.AbstractFirstAid`), `Sites/Surgeon` | extended (content-driven tables, ischaemia steps, the screen's commands, `AbstractPhysic`); `StreetView.AutoDress` and `LiveWorld.DressAfterFight` removed |
| `Combat/Aftermath/*` (`CaptiveBook`, `DefeatTable`, `Ransom`, `Rescue`) | extended (`CaptorKind.Gaol`, `Slavers`; guards arrest; prisoners fed; the captors' camp) |
| `People/*` (`PersonTable`, `Marriage`, `Demography`, `VitalRates` outbreaks) | extended (the hero's ageing, courtships and matches through the housing gate, illnesses, `Occupation` +`Physician`, `Monk`; `DeathCause` +`Executed`, `Fire`, `Childbirth`) |
| `Rumours/*`, `Dialogue/*` (`RelationTable`, `BarkSystem`, `LineBook`) | extended (false flag, slander, age, legends fallback, mood barks, the History link) |
| `Taxes/*` (`TaxSystem`, `Treasuries`, `Tolls`), `Economy/LedgerTypes` | extended (collection through officials, receipts by source, new transfer reasons) |
| `Land/*` (`LandRegistry`, `LandWorld`, claims, charters, `HoldingKind.Fief` reserved) | extended (fief deeds, mortgages, eviction, revocation, market charters, the NPC land market, zoning and streets) |
| `Production/*` (`StewardBrain`, `LabourMarket`, `DayLabour`, `Ownership`) | extended (honesty and skimming, the scarcity terms, physician and stud businesses, day labour as contracts) |
| `Hero/*` (`EstateSettler`, `EstateWorld` hooks `OnDeedInherited`, `OnFiefPassed`, `SuccessionDesk`, `Wills`) | extended (relief, wardship, reputation passed, disputed inheritances, NPC wills and guardians) |
| `Core/Chronicle` (`Cause`, `CauseKind`, `CauseCoverage`) | extended (keys and parameters, `SpecificCoverage`, the effect index) |
| `WorldGen/History/*`, `Continent/ContinentTick` | extended (v1 behind a version switch; v0 frozen) |
| `Jobs/JobTable`, `Construction/ConstructionWorld.Contracts` | extended (contracts v2 on the same escrow) |
| `Crafting/Fires`, `Lighting/LightGrid`, `Sites/*` | extended (fire cells; per-cell night light; heist sites; indoor fights with walls) |
| **New** | `Sim/Medicine/`, `Sim/Society/` (standing, deeds, marks), `Sim/Law/`, `Sim/Bondage/`, `Sim/Family/`, `Sim/Faith/`, `Sim/Rule/`, `Sim/Politics/`, `Sim/Unrest/`, `Sim/Contracts/`, `Sim/Heists/`, `Sim/Fire/`, `Sim/Disease/`, `Sim/Core/Difficulty`, `Sim/Core/ChronicleText`, `Sim/Core/EffectIndex`; Godot `UI/TreatmentScreen`, `UI/CourtPanel`, `UI/FactionScreen`, `UI/ChronicleScreen` (on the legends viewer), the Character screen's Standing, Family and Faith tabs |

### 2.2 Carried into Phase 6: where each is answered
| Carried item (source) | Home |
|---|---|
| The treatment screen, physicians and prices, `medicine/*.jsonc`, `make_crutch`, Treat scoring, tourniquet 2 h / 6 h, `AbstractFirstAid` test; the auto-dress stop-gap (phase-4 "Still open", phase-5 S7) | medicine (step 1, P6-A1, P6-B1) |
| Obsidian and flint knapping (phase-4) | holdings 3.8 (P6-B6) |
| Firedamp (phase-4, phase-5 S7) | fire 4.8 (P6-B5) |
| English made in the sim (phase-4 S7) | chronicle 4.1 (P6-A2 for the Phase 4 sites, then each writer) |
| The tournament purse on the game's word (phase-4 S8); the jousting approach angle | contracts 4.4 (P6-C2) |
| The captors' camp not in the live area; prisoners not fed (phase-4) | crime 4.5, heists 4.1 (P6-C1, P6-D3) |
| An in-game Chronicle (H) screen (phase-4) | chronicle 4.4 (P6-E4) |
| Weather fields stealth and hearing; site fights ignoring walls (phase-4) | heists 4.2-4.4 (P6-D3) |
| Watch: autopilot road encounters without the odds rule (phase-4) | contracts 4.5 (P6-C2) |
| Heist sites (phase-4 "Other moves") | heists (P6-D3) |
| Landless idle adults (phase-3); AP-8 `offer_big`, AP-15; dialogue's legends fallback, mood barks, the History link, the Journal pin (phase-3) | contracts and rumours v2 (P6-C2); the History link with the Chronicle screen (P6-E4) |
| S6: the hero's household as a field holder; clearing and sowing a claim (OV-3's missing half); assarts as felling sites; the felled tree's logs (phase-5) | holdings 3.1 (P6-B6) |
| S8: `Math.Sqrt` in construction (phase-5) | holdings 3.8 (P6-B6) |
| S11: construction budgets on `TestBudget` after the reference-PC run (phase-5) | close-out (P6-G), after the reference-PC measure (still needs the user's machine) |
| S12: a new street when plots run out (phase-5) | holdings 3.4 (P6-D4) |
| S14: a scripted Phase 5 trail as a `hashes` line (phase-5) | P6-A2 (a `phase5_trail` line), extended by OV-8's Phase 6 line |
| G2: "Supply all" reads the business table on the main thread (phase-5) | holdings 3.8 (P6-B6) |
| Items: weapon condition from fights, the broken brittle blade leaving the pack | heists 4.4 (the fight's settle path, P6-D3) |
| Items: 30-day oiling (`oil_item`) | holdings 3.8 |
| Land: "Whose is this?", buying a deed from an NPC (0x500); the claim banner | holdings 3.2, 3.3 |
| Land: the Land overlay, Ledger alerts | rule and income 6 (P6-C3) |
| Land: claims inside fiefs, eviction, squatting | rule and income 4.7 (P6-C3) |
| Rooms: `HouseholdFurnisher`, room `Owner` and `Assigned`, slipway, wheelwright yard, loft; the designer's room conditions; `test_cottage` without a hearth or beds | holdings 3.6 |
| Construction: buy and deliver, haul jobs on the site panel; installing workstations from items; fatigue and food for heavy work | holdings 3.8 |
| Construction: per-cell light at night | heists 4.3 (P6-D3) |
| Construction: a `Construct` job; day-labour posts; NPCs commissioning builders; bidding; a builder's expected days; the builder's cart as a vehicle row | contracts 4.3 (P6-C2) |
| Construction: theft from site piles (T4) | crime 4.8 |
| Construction: piece upkeep (T4) | fire 4.7 |
| Crafting: tool wear for NPC stations; stores near the crafter; NPC apprentices; refit sync | holdings 3.8 |
| Businesses: founding in a generated building | holdings 3.8 |
| Prospecting: clear-felling and coppice; carting distance as a quarry's cost | holdings 3.8 |
| Prospecting: common rights and grievances; unlicensed mining as a crime | unrest 3 (enclosure), crime 3 (`unlicensed_mining`) |
| Inheritance: a creditor selling a business; an NPC heir under 16 with a guardian; NPC wills; the heir's clothes; the haul of a hero dead below ground | family 4.7 (P6-E3) |
| Soil improvement as contracts; horse breeding and foals (T2) | holdings 3.5, 3.7 |
| Mortgages (T3) | rule and income 4.6 |
| Stewards' honesty (stream 0x540 reserved) | rule and income 4.8 |
| New games' empty market books (if the fun check finds the first weeks starved) | holdings 3.8, conditional |
| **Moved on:** a water check in `PlanFitter`; cellars in library plans; a new mine as a walkable site; taking down generated buildings; the Ledger's share market | Phase 8 (holdings 3.9 gives each reason) |
| **Moved on:** boats' ferries, fishing, drowning; the abstract resolver's ±10% gate; purveyance and scutage | Phase 7 (already scheduled there) |

## 3. Build order

Each step ends with its golden tests green. Parts of one step run in parallel (separate helpers; the files they touch do not overlap). Each part is sized for one helper on the cheaper model with its targeted tests (the user's rule): a part names its files, its gate tests and nothing else; the close-out runs the full suite.

1. **P6-A: foundations** (four parallel parts; the medicine first, as decided).
   - **A1 medicine, sim side:** `medicine/*.jsonc` and `MedicineTables` from content, the ischaemia steps, `make_crutch` and the crutch, Treat scoring, `AbstractPhysic`, physicians and barbers at the seeding pass. Files: `Combat/Wounds/*`, `Sim/Medicine/*`, `Behaviour/UtilityBrain` (Treat only), content `medicine/`, `crafting/recipes_full.jsonc`. Gate: MT-1, MT-3, MT-4, MT-7 to MT-12.
   - **A2 shared plumbing:** the appended enums (section 8), the 0x6xx streams registered with the uniqueness test, `ChronicleText` and `chronicle/types.jsonc` with its validator, `SpecificCoverage`, the Phase 4 S7 sites moved to keys, the **Phase 6 seeding pass** skeleton (section 7), the Phase 5 trail `hashes` line (S14). Gate: CH-1 (for existing types), CH-2 (Phase 4 sites), CH-3, CH-6; the streams test. **Built (P6-A2):** all of it; the seeding pass is a skeleton with no steps yet (`Phase6Seeding.DefaultSteps`), the S14 line is `phase5_trail` (also folds in A3's standing hash). Details at the end of `chronicle-full.md`.
   - **A3 standing and marks.** Files: `Sim/Society/*`, `RumourTable` (heard counts), `SuccessionDesk` (reputation). Gate: RP-1 to RP-10.
   - **A4 difficulty sliders.** Gate: DS-1 to DS-7.
2. **P6-B** (six parallel parts):
   - **B1 the treatment screen** and the auto-dress removed (needs A1). Gate: MT-2, MT-5, MT-6, MT-13, MT-14. **Milestone M-A "the surgeon".**
   - **B2 crime core:** seen/unseen, witnesses, recognition, the hue and cry, reports, bounties (escrowed as Phase 3 jobs until C2 lands). Needs A2, A3. Gate: CJ-1 to CJ-4, CJ-13, CJ-15.
   - **B3 family core:** courting, weddings, dowries, children, upbringing, the hero's ageing and old age, kidnapping. Needs A3. Gate: FL-1 to FL-4, FL-7 to FL-11, FL-15.
   - **B4 faith core:** derived religions, offices and holders, the tithe split, holy days, piety, pilgrimages. Needs A2, A3. Gate: FC-1 to FC-5, FC-10, FC-12, FC-13.
   - **B5 fire and burning** (independent). Gate: FB-1 to FB-14.
   - **B6 holdings part 1:** 3.1, 3.2, 3.6, 3.7, 3.8. Gate: HW-1, HW-2, HW-6 to HW-13.
3. **P6-C** (four parallel parts):
   - **C1 justice:** guards' shifts, arrest, gaol, court days, sentences through wounds, the hero as judge (needs B2, A1). Gate: CJ-5 to CJ-12, CJ-14, CJ-16, CJ-17. **Milestone M-B "the court"** (the month-4 checkpoint).
   - **C2 contracts v2 and rumours v2** (needs A3, B2). Gate: CR-1 to CR-14.
   - **C3 rule and income** (needs A3; RI-15 eviction after C1's guards). Gate: RI-1 to RI-19.
   - **C4 disease and plague** (needs A1). Gate: DP-1 to DP-12.
4. **P6-D** (four parallel parts):
   - **D1 slavery** (needs C1, C2, B4). Gate: SL-1 to SL-14.
   - **D2 diplomacy, AI lords, the road to lord, the Faction screen** (needs C3, B3). Gate: DA-1 to DA-12. **Milestone M-C "the lord".**
   - **D3 heists and jailbreaks** (needs C1; B5 for fire diversions). Gate: HJ-1 to HJ-12. **Milestone M-D "the breakout".**
   - **D4 holdings part 2:** the NPC land market, zoning and new streets, soil improvement contracts (needs C2, C3). Gate: HW-3 to HW-5, HW-14, HW-15.
5. **P6-E** (four parallel parts):
   - **E1 unrest and revolt** (needs C3, D2; D1 for slave revolts). Gate: UR-1 to UR-11.
   - **E2 faith politics:** heresy, excommunication, relics, superstition (needs D2, B4). Gate: FC-6 to FC-9, FC-11, FC-14.
   - **E3 family politics:** lords' arranged matches as alliances, the overlord's marriage right, kin who turn and disputed inheritances, NPC estates (needs D2, C3). Gate: FL-5, FL-6, FL-12 to FL-14.
   - **E4 the Chronicle screen** and the coverage soak (needs every writer). Gate: CH-4, CH-5, CH-7 to CH-11.
6. **P6-F history v1 and the continent tick** (needs the shared functions of D2, E1, C4, B4, D1). Gate: HR-1 to HR-11.
7. **P6-G close-out:** OV-1 to OV-8, the 100-year soak with every Phase 6 system, the reference-PC budgets (Phase 5 S11 back on `TestBudget`), the code and bug check (plan §6) in `docs/reviews/phase-6.md`, a 30-minute fun-check session (get caught stealing and stand trial; court and marry; fight a street fire; break a friend out; ride out a plague; rise to a manor), 2 to 3 testers asked "did you understand why?".

**Estimate against plan §5 (8 to 11 months)**, in months of code per part: A1 0.6, A2 0.6, A3 0.4, A4 0.2; B1 0.6, B2 0.9, B3 1.0, B4 0.8, B5 1.0, B6 0.8; C1 1.2, C2 1.0, C3 1.4, C4 0.8; D1 1.0, D2 1.4, D3 1.2, D4 0.7; E1 0.7, E2 0.7, E3 0.5, E4 0.6; F 1.0; G 0.8. **Serial sum about 20 months**; with each step's parts on separate helpers the critical path is A1, B3, C3, D2, E1, F, G = **about 6.9 months of code**, plus art (about 40 props and pieces, fire and smoke animations, liveries as palette rows, clergy vestments, child body sizes, the crutch posture, shackles, brands) and integration slack: **plan on 10 months**, near the top of the range, as Phases 3 to 5 landed. The overrun rule (plan §7) triggers at 16.5 months; the checkpoint is M-B by month 4.
**Cut order if late (overrun rule):** (1) witch hunts and fake relics; (2) slander (rumours keep v1's honest degrading); (3) tallage and the clerical tax (keep councils and subsidies); (4) kin turning beyond leaving and contesting; (5) schisms in history v1 (keep heresies); (6) smuggling and customs bribes (keep customs); (7) NPC crime beyond thefts and assaults; (8) firedamp; (9) the scarcity terms' recovery curves (a fixed recovery); (10) holdings 3.8 rows outside a done-when. **Never cut:** the treatment screen and physicians, witnessed crime to a bounty with its cause, guards, gaol, courts and harsh sentences through the anatomy, systemic slavery (markets, owning, freeing, the enslaved hero, escape, revolts), family (courting, arranged matches, dowries, children, upbringing, ageing, old-age death), faith (religions, saints, holy days, relics, pilgrimages, clergy, church land, tithes, piety, heresy, excommunication, superstition), every §3j income kind, officials honest or corrupt, loans that only move coin, the rise to lord, the unrest ladder, the jailbreak, fire spread, the great plague, chronicle coverage >= 90%, determinism, conservation.

## 4. Done-when mapped to specs and tests

| Done-when (plan §5) | Specs | Proving tests | Integration |
|---|---|---|---|
| **Theft in front of a witness raises a bounty with an explained cause** | crime 4.1-4.3, standing 4.1, chronicle 4.2 | CJ-1 to CJ-4, RP-1, CH-7 | **OV-1:** the CJ-1 script in the running game (seed 1234, Ranunfield market, day 3): the HUD showed Seen (5); the hero outruns the guards; at the next H5 a 580-mite bounty is escrowed; the Journal's Wanted card and the Chronicle's Why both read "theft of a knife from Wynn's stall, seen by Wynn" |
| **You can rise to lord** | diplomacy 4.3, rule and income 4.2-4.8, contracts 4.1 | DA-6 to DA-9, RI-1, RI-4, RI-10, RI-11 | **OV-2:** the autoplay bot takes three contracts for a lord (guard, escort, bounty), is offered fealty, serves, is granted a manor, takes a war-service contract in the liege's war and is granted a vacant fief; pays relief; appoints a reeve and a bailiff; a season's receipts on the Fief tab; the skimming bailiff found by audit; about 2 game years headless; coin and deeds exact |
| **A jailbreak works** | heists 4.2-4.5, crime 4.5 | HJ-2 to HJ-9 | **OV-3:** a companion jailed for theft; at night through the drain, a sleeping gaoler's key, out unseen: rescued; a second run seen at the door: bounties and a one-day manhunt that searches the roads |
| **A thatched street burns realistically** | fire 4.2-4.5 | FB-2 to FB-5, FB-13 | **OV-4:** in the live bubble a hearth fire in a thatched village street with a 5 m/s wind: the fire runs downwind, villagers form a bucket chain from the well and pull down thatch; the stone church does not burn; the chronicle's fire entry names the hearth and lists losses and deaths with causes |
| **Chronicle cause coverage >= 90% specific** | chronicle 4.2-4.3 | CH-4 to CH-8 | **OV-5:** a 10-year headless soak with the hero bot and every Phase 6 system: specific coverage >= 0.90, every entry with a cause or the drift label, every chain resolves; every Phase 2-5 soak gate holds; coin and mass exact |
| The Phase 4 medicine items, first (decided Nov 20) | medicine | MT-1 to MT-14 | **OV-6:** after the ford ambush the hero bandages and tourniquets on the road, reaches the town's physician within the day for the tendon, pays, keeps the leg, walks on a crutch while it heals; nothing is dressed by itself |
| Family, faith and lifespan (decided Nov 2026) | family, faith, standing | FL-2, FL-4, FL-7, FL-8, FL-10, RP-7, FC-2 | **OV-7:** headless, accelerated: the hero courts and marries at the church with a dowry, two children are born, the son is tutored in arms; the hero dies of old age past 60 (the hazard table forced in the test); the son of 16+ takes over with 50% of the standing, pays relief to the overlord |
| Determinism and saves | all | every spec's determinism test | **OV-8:** a 60-day scripted run with a court day, a flogging, a fire, a riot, a slave sale, a plague's arrival, a council and a heist: save, load and save byte-identical at six points; the Phase 6 `hashes` line equal on Linux and Windows |

## 5. RNG streams

Phase 5 owns 0x500..0x5FF; **Phase 6 uses 0x600..0x6FF**, registered in `Core/RngStreams.cs` with the uniqueness test (no id outside its phase's block). Every key is game time, distance or an id. Three Phase 5 reservations are now drawn: **0x500** (NPC land market ties), **0x540** (official and steward honesty), **0x561** (disputed inheritances); the tests that asserted them unused are rewritten to assert them unused before Phase 6 code runs.

| Stream | Spec | Use | Key |
|---|---|---|---|
| 0x600 | medicine | physician seeding; `AbstractPhysic` (draw 1) | person; person, day |
| 0x610 / 0x611 / 0x612 / 0x613 / 0x614 | crime | reporting; guard recognition by clothes; NPC lord's mercy tie; bribes; NPC crime | crime, witness; guard, person, day; crime; official, day; household, day |
| 0x620 | rumours v2 | slander | author, season |
| 0x630 / 0x631 / 0x632 / 0x633 | slavery | raids; market buyer ties; runaways; revolts | region, season; market, day; person, day; holding, day |
| 0x640 / 0x641 / 0x642 / 0x643 / 0x644 | family | courtship; child attributes; kin turning; kidnapping; the hero's old age | courtship, day; child; person, year; hero, season; hero, year |
| 0x650 / 0x651 / 0x652 / 0x653 / 0x654 | faith | religions for v0 worlds; founders and holy days; heresy; pilgrim events; witch hunts | culture; religion; settlement, season / person; party, day; settlement, season |
| 0x660 / 0x661 / 0x662 / 0x663 / 0x664 | rule | reserved for purveyance (Phase 7); skim seen and audits; council votes; lender ties; smuggling | -; official, season; council, member; borrower, season; caravan, day |
| 0x670 / 0x671 / 0x672 | diplomacy | lord traits; utility ties; envoy events | person; lord, season; envoy, day |
| 0x680 / 0x681 / 0x682 | unrest | leader ties; riot joiners; revolt and evasion | settlement, day; person, riot; household, season |
| 0x690 / 0x691 / 0x692 | contracts | company ties; pay rounding; tournament brackets | company, day; contract; tournament, round |
| 0x6A0 | chronicle | reserved for template variants; asserted never drawn | - |
| 0x6B0 / 0x6B1 / 0x6B2 / 0x6B3 | heists | patrol jitter; sleepers waking; vault contents; disguise borderline | site, night; guard, search; site; guard, hour |
| 0x6C0 / 0x6C1 / 0x6C2 / 0x6C3 | fire | spread; sparks; firedamp; accidents and lightning | cell, step; cell, step; site, cell; structure, day |
| 0x6D0 / 0x6D1 / 0x6D2 / 0x6D3 / 0x6D4 | disease | onset; epidemic length and infection; plague start; spread and carriers; illness death | person, day; settlement / person, day; continent, year; edge, season / caravan; person, start |
| 0x6E0 / 0x6E1 | holdings | reserved; horse breeding | -; mare, year |
Reused unchanged: 0x420-0x422 (infection, malunion, surgery), 0x452 (bandit styles), 0x454 (escape window), 0x455 (lock picking), 0x456 (ransom), 0x532 (brittle blades, also knapped stone). History v1 appends ids to the history generator's own table (not this block). Free for growth: 0x601..0x60F, 0x615..0x61F, 0x621..0x62F, 0x634..0x63F, 0x645..0x64F, 0x655..0x65F, 0x665..0x66F, 0x673..0x67F, 0x683..0x68F, 0x693..0x69F, 0x6A1..0x6AF, 0x6B4..0x6BF, 0x6C4..0x6CF, 0x6D5..0x6DF, 0x6E2..0x6FF.

## 6. Control keys (one consolidated table)

Source of truth: `src/Ravensguard.Game/Input/InputDefs.cs` at b5d9234 (re-read for this spec: 12 contexts, World uses every letter but O (Mounted's), the digits 1-4, F1-F10 with Shift+F6 and Shift+F7, `,` `.` `;` `'` `[` `]` `\` `=` `-`, Home, PgUp, PgDn, Alt, Enter, Esc, Space, Shift, Tab and the arrows). **Phase 6 adds no World key**; 7, 8, 9, 0 and F11 stay free. Two World keys bound since Phase 0 and unused until now get their screens: **F** (`panel_faction`) opens the Faction screen, **H** (`panel_chronicle`) the Chronicle.

| Context | Keys | Origin |
|---|---|---|
| **Treatment** (modal, alone with Global) | Up / Down part, Left / Right verb, Enter do it, D dress all bleeding, Tab next patient in reach, Esc back | new |
| **Court** (modal, alone with Global) | Up / Down case, Left / Right sentence, Enter pronounce, Tab case details (witnesses, the law), Esc back (adjourn: the steward judges) | new |
| **Faction** (modal, alone with Global; mirrors the Ledger) | Up / Down rows, Left / Right column, Tab next tab, Enter open or act, `/` search, Esc back | new |
| **Menu** for the in-game Chronicle (the legends viewer's bindings, unchanged) | Up / Down, Enter, F follow cause, D follow effect, `/` search, T filter type, E filter realm, Backspace previous, O open here, Esc back | Phase 1 |
| World (unchanged) | Z sneak (seen/unseen at once), V torch, E and Q for every new verb | Phase 3-4 |
Check: the three new contexts are modal and alone with Global (backtick console, F12 inspector), so no clash is possible; the Chronicle reuses the Menu context as the legends viewer does (its main-menu actions are not handled by that screen). New verbs without keys (Q menu and E): Treat, Treat me (physician), Court, Arrange a match, Teach (set a course), Pilgrimage, Give alms, Swear fealty, Petition, Renounce, Gift, Bribe, Search (a key holder), Douse sconce, Set fire, Fetch water, Pull down thatch, Speak ill of, Buy / Sell bond, Free, Chain or unchain, Set treatment, Whose is this?, Clear an acre, Sow, Offer for this house, Join, Lead or Mediate a riot, Stand trial or Plead. The `?` overlay gains the Treatment, Court and Faction pages (built from `InputDefs`). `--check-keymap` must stay at 0 conflicts.

## 7. Saves

**No version bump: v8 stays**, because no stored meaning changes. Phase 6 state lives in new **optional sections** (absent when empty, so a world that never used them keeps its bytes): `medicine` (physicians), `standing`, `law`, `bondage`, `family`, `faith`, `fiefs` (rule: deeds' policy, offices, dues, loans), `diplomacy`, `unrest`, `contracts`, `fire`, `disease`, `heists`. **Additive keys:** wounds (`TourniquetOnS`), farming (a parcel's holder household), hero (ageing), succession (`ReputationPending` now carries the groups), talk (false flag, slander rows), chronicle entries (`TextKey`, `Params`, `NoteKey`), the header (`Difficulty`, `HistoryRulesVersion`). Enums grow append-only (section 8); new content (crimes, punishments, illnesses, contract kinds, room types, business kinds, religions' templates) is mapped by name by the load-time `ContentRemap` (Phase 5), and the load report lists it.
**The Phase 6 seeding pass** runs at a new game after the population, and at the first load of a save that has no Phase 6 sections (every v8 save from Phase 5): religions derived (pure), fief deeds from the Phase 1 lords, offices bound to people, physicians and barbers (0x600), honesty (0x540) and lord traits (0x670) rolled by person id, empty standing. It is keyed on the world seed and ids only (never the day it runs), so a Phase 5 save loaded today and a new game of the same world derive the same state; afterwards it is saved like everything else. The load report says "Phase 6 state derived at load". If an implementation step finds a stored meaning that must change after all, that step adds the v8 -> v9 migrator step and amends this section (the Phase 5 rule). Round trip: OV-8.

## 8. Cross-spec notes (resolved here)
1. **Enums, append only:** `CauseKind` +`Justice, Faith, Slavery, Family, Unrest, Diplomacy, Office, Illness`; `TransferReason` +`Fee, Restitution, Bounty, Bribe, SlaveSale, Dowry, Relief, Aid, Subsidy, Tallage, Customs, Loan, Interest, Skim` (saves pad the count, as `Alms` did); `Occupation` +`Physician, Monk`; `DeathCause` +`Executed, Fire, Childbirth`; `CaptorKind` +`Gaol, Slavers`; `ActionKind` +`FightFire, Riot, Pursue`; `OddJobKind` +`FightFire`; `HoldingKind.Fief` now used. **No `Skill` is added** (Law, Theology, Stealth, Lockpicking, Surgery, Speech, Leadership exist) and no `PersonStatus` (slaves and prisoners are `Alive`).
2. **One standing function** (`Standing.Of`) is what prices, guards, courts, petitions, councils, marriages and the church read; nobody keeps a private reputation.
3. **One war utility** (`WarUtility`) for live rulers, the continent tick and history v1; **one plague** function for history and play; **one ageing hazard** for the hero, companions and notables.
4. **Bodies:** every punishment, burn, illness and plague death goes through the Phase 4 wound book or `Demography.Kill` with a cause chain; no hit-point shortcut.
5. **Coin:** fines, bounties, dowries, dues, subsidies, customs, loans, bribes and skims are ledger transfers; loans never create coin; OV-2, OV-5 and RI-13 audit it.
6. **Witnessing has no dice:** seeing is geometry and light; dice decide only reporting, recognition by clothes and the other named draws (crime 4.1).
7. **Last-known knowledge:** AI lords, bounty hunters and the plague overlay read rumours with their age; a fact older than 2 seasons weighs half.
8. **The seasonal day is crowded:** new seasonal work goes into slices (`S10Politics` one fief an hour; rule steps spread over the first day's hours); nothing new at hour 0 of a season beyond counters.
9. **Hash trail additions:** `StandingHash`, `LawHash`, `BondHash`, `FamilyHash`, `FaithHash`, `FiefHash`, `PoliticsHash`, `UnrestHash`, `ContractHash`, `ChronicleHash`, `HeistHash`, `FireHash`, `DiseaseHash`; `WoundHash`, `LandHash`, `AnimalHash`, `TalkHash` extended. The live trail holds only what the live tier writes (Phase 5 S1's lesson): fire cells and guards in the bubble, not the abstract parts.

## 9. Budgets (the single ledger; reference PC as in plan §2.11)

### 9.1 Live tick (20 Hz), average per tick in a normal bubble
Phase 5 left the tick at about 5.7 of 8 ms.
| Item | Spec | Budget |
|---|---|---|
| Seen/unseen and witnesses (5 Hz, event-driven) | crime | 0.05 ms |
| Guards looking and listening in a site (12 guards, 5 Hz) | heists | 0.15 ms |
| Fire cells (worst: 400 burning) and smoke | fire | 0.6 ms worst, 0 when nothing burns |
| NPC Treat, riots, bucket chains | medicine, unrest, fire | inside Phase 3's people budget (they are ordinary live persons) |
| **Phase 6 total** | | **about 0.2 ms typical, 0.8 ms worst** (tick total about 5.9 typical, 6.5 worst) |
Barrier: a court day <= 2 ms a fief; a sentence's wounds <= 0.1 ms; a heist site generation <= 30 ms (on first visit, off the frame); the Chronicle screen <= 20 ms to open.
### 9.2 Abstract tick
Hour 0 (H5) additions: standing <= 1 ms, unrest <= 1 ms, illnesses and the region's plague <= 0.6 ms (<= 3 ms in an epidemic), holy days <= 2 ms (holy days only), prisoners and sentences <= 0.2 ms: **<= 5 ms on a normal day, <= 8 ms on a holy day in an epidemic**. Hourly: contracts' scanner <= 2 ms, abstract fires <= 0.3 ms. Seasonal slices: `LordBrain` <= 2 ms an hour, rule steps <= 10 ms spread over the first day, continent politics <= 20 ms at S9. **The worst hour must stay under the 150 ms cap** (P6-G profiles it with every system on).
### 9.3 Memory, saves, content
| Item | Budget |
|---|---|
| Standing, law, bondage, family, faith, fiefs, diplomacy, unrest, contracts | <= 350 KB together a detailed region |
| Chronicle growth with keys and parameters | about 120 KB a game year before compaction (<= 1.2 MB at 10 years) |
| Fire, disease, heists (transient) | <= 100 KB while active |
| **Phase 6 growth** | **<= 2 MB** per detailed region and hero save at 10 game years |
| Content | contracts <= 40 KB, law <= 20 KB, faith <= 30 KB, medicine <= 20 KB, heist templates <= 60 KB, dialogue lines about 150 |
| Allocation gate | 0 allocations per tick for fire cells, guards' checks and seen/unseen |

## 10. Combined open questions (deduplicated; "default" = recommended)

### 10.1 For the user (the six that change how the game feels)
| # | Question | Default |
|---|---|---|
| Q1 | How fast do the hero and his children age? | Historically, on the same clock as everyone: a 60-year life is about 180 days of play at 1x, a child comes of age in about 48 days of play at 1x (6 at 8x). No ageing slider. Heirs of 12 to 15 are playable with a guardian (Phase 5) |
| Q2 | Do harsh sentences apply to the hero, a lost hand and the gallows included? | Yes, as to anyone. The expected sentence is shown in the cell before the court day, with every way out: fine, restitution, a plea for mercy, benefit of clergy, a bribe, escape, rescue |
| Q3 | Can slaves travel and fight in the party? | They follow and work; they fight only if the owner arms them and flee below morale 40 unless chained. A freed slave can be recruited like any companion |
| Q4 | Real miracles? | None. Relics, pilgrimages and saints act through people: standing, morale, a sick pilgrim's comfort; superstition is belief, not magic (the rare monsters stay the only real horrors) |
| Q5 | Worlds already in your library: keep their history? | Yes: their history stays exactly as generated (it must, to load their saves); religions, offices and laws are added at load. New worlds get the recalibrated history |
| Q6 | If Phase 6 runs late, is the cut order right? | Section 3: witch hunts and fake relics go first, then slander, tallage, kin betrayal beyond leaving, schisms, smuggling, extra NPC crime, firedamp. The decided systems are never cut |

### 10.2 Technical (decided here unless someone objects)
| # | Question | Specs | Default |
|---|---|---|---|
| T1 | Save version | overview 7 | v8 stays; optional sections, additive keys, the seeding pass |
| T2 | Reputation to kin heirs | standing 4.4 | 50% of every row and of renown; marks never |
| T3 | Interest | rule 4.6 | 4% a game season (16% a game year) |
| T4 | Bounties, weregild | crime 3, 4.3 | theft 2x value + 100, murder 3,000, weregild 6,000 |
| T5 | Burning relapsed heretics | faith 4.5 | only where a culture's table says so; none of the base four |
| T6 | Fire speed | fire 4.2 | real sim-seconds: a roof in minutes, a street over a few game hours |
| T7 | Great plague frequency | disease 3 | 1% a continent-year at normal |
| T8 | Tourniquet clock on re-tying | medicine 4.3 | accumulates, no reset |
| T9 | Physician fees | medicine 3 | stitch 300, tendon 1,200, organ 2,000 mites |
| T10 | Base cultures' slavery law | slavery 3 | Northmark thralls, Valeholt none, Highmarch debt, Seaboard chattel |
| T11 | Chronicle compaction | chronicle 4.5 | 10 game years, commoners only, chains kept |
| T12 | Manhunt and locks | heists 4.5 | 1 game day of search; locks changed within 2 days |
| T13 | Wars before Phase 7 | diplomacy 8 | abstract battles and sieges; the hero joins by contract |
| T14 | The road to lord | diplomacy 4.3 | fealty at standing 300, renown 100, 3 contracts; about 2 game years for the bot |
| T15 | Marriage forms | family 8 | the base cultures' historical forms; culture fields let a mod add others |
| T16 | Ageing table | family 4.6 | strength from 40, the old-age hazard from 50 (1%, 3%, 10%, 20%) |
| T17 | Market charter threshold | rule 4.4 | 200 people and an open square |

## 10a. Decided with the user (Nov 2026)

Where this section and a spec's "default" disagree, this section wins.
- **Ageing has a world setting for faster ageing** (the user did not want the historical pace only).
  - Add `aging_pace` to the world settings and to the difficulty sliders (`difficulty-sliders.md`): 1x (historical, the default), 2x, 3x or 4x.
  - It speeds childhood, adulthood and old age together for everyone (hero and NPCs alike), so dynasties turn over faster.
  - The vital rates' per-year chances stay per game year; only the ageing clock scales.
  - Heirs aged 12 to 15 are playable with a guardian, as in `family-and-lifespan.md`.
  - The history simulation uses the world's setting, so ages stay consistent.
  - Test: a child at 4x comes of age in a quarter of the 1x game days, and population soaks hold at every pace.
- **Harsh sentences can fall on the hero, with ways out.**
  - The expected sentence is shown in the cell before the court day, with every way out: fine, mercy, benefit of clergy, bribe, escape or rescue.
  - A lost hand and hanging are included.
- **Slaves in the party** follow and work, fight only if armed, and flee below morale 40 unless chained. A freed slave can be recruited like any companion.
- **Rare, ambiguous miracles** (the user chose these over none).
  - Very rare events that people read as miracles: a cure after a pilgrimage or relic, a saved harvest, a fire that stops at a church door.
  - Each always has a possible natural explanation in the sim: the cure is a natural recovery roll made after the faith event; the fire stops at a stone wall or because the wind changed.
  - The chronicle records them as believed miracles, they raise piety and a saint's or relic's fame, and they can be disputed: sceptics, rival clergy, heresy charges.
  - No supernatural mechanics; the rare monsters stay the only real horrors.
  - Put this in `faith-and-church.md` with its own small stream and golden tests: rates, the natural explanation always present, the chronicle cause.
- **Worlds already in the library** keep their history exactly as generated; religions, officials and laws are added at load. New worlds get the recalibrated history. (This was the default; the user did not object.)
- **Cut order** as proposed in §3 (the default).

## 11. Risks

| Risk | Why it matters | Mitigation |
|---|---|---|
| **Scope: seventeen specs in 8 to 11 months** | the overrun rule | small helper-sized parts with their own gates, the cut order, milestones M-A to M-D, the M-B checkpoint at month 4 |
| **Harshness becomes frustration** (maiming, enslavement, excommunication) | brutal must stay fair through legibility | seen/unseen before acting, the expected sentence shown before trial with every way out, the chronicle's Why on every mark; the fun check asks "was it fair?" |
| **History v1 drifts from the targets, or breaks library worlds** | every world is seeded from it | v0 frozen behind a version (HR-1), the 50-seed calibration test (HR-3 to HR-8), the seeding pass for old worlds |
| **The 90% specific gate touches every writer** | many Phase 2-5 entries use `Other` or English | A2 lays the schema and the validator first; each step's suite gates its own entries at 95%; OV-5 measures the whole |
| **The seasonal day and hour 0** | Phase 4-5 left the hour barrier near its cap | nothing heavy at hour 0; politics one fief an hour; rule steps spread; P6-G profiles with every system |
| **The seeding pass on old saves** | a Phase 5 save must load and derive the same state as a new game | keyed on seed and ids only; MT-7, FC-1, RI-1, HR-2 compare both paths |
| **Live tick with fire and riots** | 400 burning cells and 60 rioters in a town | staggered fire steps, the allocation gate, people inside Phase 3's budget, FB-14 and the budget tests |
| **Replay gaps** (Phase 5 S1, S4 lessons) | new hero commands and tick-worker effects | every new hero command recorded; the live trail holds only live-tier state (section 8.9) |
| **Content volume and tone** (laws, religions, slavery, executions) | 4 religions, 40 contract kinds, 150 lines | templates and validators; neutral chronicle wording; a human read of the English (as Phase 4's flavour draft) |

## 12. Test strategy
Every spec has golden tests (**about 222**: MT 14, RP 10, CJ 17, SL 14, FL 15, FC 14, RI 19, DA 12, UR 11, CR 14, CH 11, HJ 12, FB 14, DP 12, HR 11, HW 15, DS 7) plus OV-1 to OV-8 (section 4). Helpers run their part's tests and the save, resume and golden-hash tests; CI runs the full suite. Shared harness: the Phase 2 `HeadlessWorld`, the `--autoplay` bot (extended with crime, court, courtship, fealty, petition, heist and firefighting commands), and the Phase 5 resume test (`WorldRuntimeResumeTests`) with an eighth case holding every Phase 6 section. Conservation audits (coin through the Ledger, mass through `MassLedger`, deeds by count, people across deaths and bonds) stay exact in RI-13, RI-19, FB-8, SL-14, DP-10 and OV-5. Nightly: the 100-year soak with every Phase 6 system (every Phase 2-5 gate, idle adults < 800, specific coverage >= 0.90, no realm collapse from unrest alone). Phase end: the code and bug check (plan §6), `docs/reviews/phase-6.md`, the fun check and the testers' question.
