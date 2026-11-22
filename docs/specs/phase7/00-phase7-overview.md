# Phase 7 specs: overview, build order, seam, shared enums, streams, keys, saves, open questions and risks

Status: design, written before any Phase 7 code (plan §4: spec, then golden tests, then implementation), on top of ac87253 (the thirteen Phase 7 specs and the user's industry sheets; Phase 6 closed at b6b39e2).
Source of truth for scheduling is `docs/REDESIGN_PLAN.md` §5. Its phase table gives Phase 7 ("Armies, war at scale, ships and naval war, industries") **16 to 22 months**. The Phase 7 heading still says 15 to 20; that figure was written before industries were added, so the table governs.
This file ties the thirteen Phase 7 specs together in the style of the Phase 6 overview. The Phase 6 conventions still apply:
- integer or `Fixed` arithmetic in the sim;
- counter-based `DetRng` streams keyed on game time, distance or ids;
- stable-id iteration;
- content as JSON through the `ModLoader`;
- UI as thin views over snapshots, with strings through `tr()` and no English made in the sim.

Money is stored in **mites**: 1 cp = 10 mites, 1 sp = 100 (the old crown), 1 gp = 1,000 (section 8.3).
Time: a game hour is 180 sim-seconds, a day is 86,400 game seconds, a season is 15 days, a year is 60 days and a working week is 6 days.

**Plan sections covered:**
- §5 Phase 7: step 0's audit, economies of scale, large buildings, industries, coins, battle, armies on the map, sieges, war wagons and chariots, ships, AI campaigns, the abstract resolver, content;
- §3d (armies and war at scale), §3e (horses), §3h (vehicles, engines, ships), §3j (purveyance, scutage, campaign finance);
- §4A.1 (money, logistics), §4A.3 (abstract battles, morale), §4A.4 (campaigns).

**Decisions bound here (locked with the user, Nov 2026):**
- **The economy audit comes first.** Step 0 is an adversarial economy audit whose High and Medium findings are fixed before armies are built.
- **Economies of scale apply to every business,** through new large buildings.
- **Industries are staged chains** built from the user's iron, clay and wood sheets:
  - real buildings, with tiers 1 to 3 built hands-on;
  - an Industry tab;
  - historical output with an honest 1x to 5x slider;
  - real inputs;
  - a 2 to 3 game-year payback.
- **Money is gold, silver and copper,** struck by mints.
- **Armies are hybrid:** standing warbands all year, plus seasonal hosts.
- **Soldiers grow without XP:**
  - loadout templates and per-soldier refit;
  - promotions;
  - levies arm themselves by wealth, while retinues and mercenaries draw from the armoury.
- **The arms industry and army supply are part of the phase.**
- **Battles are fought live with up to 2,000 soldiers.**

## 1. Document map

| File | Owns | Tests | Side |
|---|---|---|---|
| `economy-audit.md` | step 0: four red-team roles, the checklist (exploits, war load, bottlenecks, leaks, growth, industry targets), the suspects S-1 to S-8, the R1 rule, `WarLoad` and the 25-year war-load soak (WL-1 to WL-15), the findings file, gate **M-E0** | EA-1 to EA-16 | economy |
| `coinage.md` | the mite kept (no stored number changes), three coins, mints wired per metal, copper as capped token coin, gold tracked in opted-in purses, money changers, melt and hoards, gold mining, `MoneyText` display styles | CN-1 to CN-16 | economy |
| `economies-of-scale.md` | crew roles and step grades, the master's reach, shared heat and firing fuel, fixed costs, supply contracts, carting cost, machines' `labour_pm`, munition patterns; crowding, span of control, guild caps and licences, market absorption | ES-1 to ES-16 | economy |
| `large-buildings.md` | water power from flow and head, wind and gins, power nets, about 26 pieces, 11 room types, 13 plans, the water check for powered plans, wear and floods, armoury care, depots, warehouses, barracks, ropewalk | LB-1 to LB-16 | economy |
| `industries.md` | the stage runtime, the iron, clay and wood chains with tier tables, tier recognition, upgrades as plans, real inputs, the slider, seasons, accidents, packing, selling (gate, contract, own caravan), payback, generated industries' composite rates, NPC founders, the Industry tab | IN-1 to IN-24 | economy |
| `army-economy.md` | **the interface the military calls** (`HostSupply`), the supply ladder, foraging, purveyance and tallies, service and scutage, campaign finance, depots, sutlers, siege larders, levies out of the harvest, the arms industry, loot recycling, pay and arrears | AE-1 to AE-22 | economy |
| `battle-bubble-and-formations.md` | the battle bubble, `BattleGrid` (struct arrays, 4 m cells), formations, slots, orders and their delay, captains, morale at formation scale, reinforcement waves, F1 to F4, the order wheel, the tactical pause, `CrowdLayer`, Field and Command minis | BB-1 to BB-18 | military |
| `cavalry-and-mounted-war.md` | cavalry shapes, the charge as an order, shock (morale only), breakthrough and re-form, schiltrons, stakes and ditches, dismounting, kicks, horse archers, footing per cell, weather, fallen-horse obstacles | CV-1 to CV-14 | military |
| `armies-on-the-map.md` | warbands and their tasks, hosts, levy draw and evasion, the muster, marching and `ArmyBridge` columns, hosts from outside the region, camps and sentries, consumption and shortfalls, desertion and bandit bands, intel, `CampaignBrain`, the war score from events (replacing Phase 6's resolver), winter quarters, the hero's armies | AM-1 to AM-20 | military |
| `soldiers-and-companies.md` | troop roles and wages, loadout templates, `ArmouryFill`, who arms whom, drill and promotion, dubbing, retinues, mercenary companies, prisoners and ransom at scale, the Armoury screen | SC-1 to SC-18 | military |
| `sieges-and-engines.md` | wall damage by momentum, breaches in `ChunkDelta`, engines from the designer, dispersion, the assault on several levels, sallies, blockade and starvation, surrender, storm and sack, winter sieges, war wagons and the wagon fort, chariots | SG-1 to SG-20 | military |
| `ships-and-naval-war.md` | sea ships and decks as moving frames, sailing by wind, sea state, storms, harbours, ferries and fishing, sea edges, sea raids and the landing, naval combat (missiles, ramming, fire, grapples, boarding), overboard and drowning | SH-1 to SH-16 | military |
| `abstract-war-and-calibration.md` | abstract battle v2 with geometry, cavalry passes, 2,000 v 2,000 in 45 ms, count hosts and count battles on the continent, the ±10% gate replacing the AB-3 ratchet, **the 25-year war soak** and its gates | AW-1 to AW-16 | military |

There are **232 golden tests** (economy 110, military 122) plus the overview's eight integration tests OV-1 to OV-8 (section 4).

**Content volume:**
- **Siege:** about 10 siege engines and defences (the plan's target) and about 20 vehicle parts.
- **Ships:** 4 sea ships (longship, knarr, cog, war cog) and about 11 sea parts.
- **Buildings:** about 26 machine and building pieces, about 19 industry pieces, 13 large-building plans plus 18 tier plans, 11 room types, and about 27 business and stage kinds.
- **Soldiers:** 10 troop roles with their loadouts and Field and Command minis, and camp pieces.
- **Goods:** about 12 new goods.
- **Chronicle:** about 55 chronicle types.

## 2. Module map

```
 P2 ledger, markets, caravans   P4 FightWorld, resolver, wounds, mounted, abstract v1   P5 vehicles, construction, businesses   P6 rule, unrest, contracts, politics
        |                                   |                                                  |                                        |
        v                                   v                                                  v                                        v
 L0  step 0: Phase 6 debts (S10, S14, G1, S13, S11) and shared plumbing (enums, streams, slots, seeding step)
        |
        v
 L1  WarLoad + HostSupply first cut -> the audit run (EA)      BattleGrid (BB-A)      water power (LB1)      crews and limits (ES1)
        |                                                          |                                              |
        v                                                          v                                              v
 L2  coinage (CN)   industries runtime and chains (IN)   buildings (LB2)   costs and contracts (ES2)   roles and the armoury (SC-A)
        |
        v
 L3  the audit's fixes and second run  ==> gate M-E0 (military parts that load the economy may start)
        |
        v
 L4  HostSupply complete (AE1)  ->  purveyance, scutage, forage, sutlers (AE2)        formations and command (BB-B, BB-D)
        |                                   |                                                  |
        v                                   v                                                  v
 L5  armies on the map (AM-A..E)   walls and engines (SG-A..C)   cavalry (CV)   the bubble (BB-C)   ships (SH-A, SH-B)
        |                                   |                                                  |
        v                                   v                                                  v
 L6  abstract v2 and the re-fit (AW-A..C)   count armies (AW-D)   sieges' ends (SG-D)   raids and naval combat (SH-C, SH-D)   arms industry (AE3)
        |
        v
 L7  the war soak (AW-E)  ->  close-out (OV-1..8, budgets, review, fun check)
```

### 2.1 What is new and what is extended

| Existing (code at ac87253) | Phase 7 |
|---|---|
| `Economy/Coin`, `MintMath` (no caller), `Ledger`, `LedgerTypes` | extended: coin helpers, a metal parameter for `MintMath`, mints wired, a gold hook on `Transfer` for opted-in purses, `TransferReason` appends |
| `Goods/MassLedger` | extended: `DestroyReason` +`Waste, Trampled` |
| `Production/*` (`Business`, `ProductionSystem`, `StewardBrain`, `RecipeBook`, `Labour`), `Crafting/*` (guilds, apprentices, tool wear) | extended: crews and grades, shared heat, contracts, patterns, limits, stage kinds, absorption caps; `water_sawmill` migrates to `saw_mill` |
| `Building/RoomDetectorV2`, `Construction/PlanFitter`, `PlanLibrary`, `BuiltCell.Hp`, collapse | extended: 11 room types, a water check for powered plans, tier recognition, siege damage, rubble |
| `Vehicles/*` (`PartGrid`, `VehicleWorks`, `BoatMath`, `Voyages`) | extended: siege and sea parts and templates, per-part damage, people pushing, sea state, moving deck frames |
| `Combat/Melee2/FightWorld`, `CombatBrain`, `Morale`, `Watch/SquadTactics`, `Mounted/*`, `TerrainFooting` | extended: grid scans, struct arrays, formation layer, `Formation` and `Footing` appends, carriers, levels, kicks, dismounting |
| `Combat/Abstract/*` (v1, tables, `BattleDemotion`), `tools/Ravensguard.Calibrate` | extended to v2 (blocks, flanks, passes, lazy books, hash-consed library), count tables; v1 kept behind its version until the gate passes |
| `Live/Bubble`, `PeopleBridge`, `Caravans/CaravanRoads`, `CaravanBridge` | extended: the battle bubble kind, `ArmyBridge` for columns and camps |
| `Politics/Diplomacy.StepWar`, `PoliticsCtx.Strength`, `LordBrain`, `WarUtility` | **replaced in play**: the war score from `WarEvents`; strength from known armies; history unchanged (Q12) |
| `Rule/*` (`DueRow`, `CallService`, loans, councils, offices), `Unrest/*` (purveyance hook, `Evades`, `Trigger`), `Disease` (`ArmyCampSize`), `Bondage`, `Contracts` | called: scutage, purveyance, levy evasion, riots, camp sickness, war captives, `mercenary_service` contracts; `OfficeKind` +`MintMaster`, `DueKind` +`Scutage` |
| `Continent/ContinentTick`, `ContinentState` | extended: count hosts on edges, sea edges, the arms class, sea trade and raids |
| `Core/Difficulty` | a seventh value, `IndustryOutputPct` (100 to 500) |
| **New** | `Sim/WarSupply/` (economy: `HostSupply`, `SupplyLadder`, `Purveyance`, `Forage`, `Sutlers`, `Depots`, `Scutage`, `CampaignFinance`, `ArmsOrders`, `LootRefit`, `SiegeLarder`, `AbstractHostSupply`, `WarLoad`), `Economy/Mints`, `CoinStock`, `MoneyChanger`, `Core/MoneyText`, `Production/Crews`, `FixedCosts`, `SupplyContracts`, `Patterns`, `Limits`, `Production/Industry/*`, `Building/WaterPower`, `PowerNet`, `WindPower`, `Production/Armoury`, `Depots`, `Warehouses`; `Sim/Battle/` (bubble, grid, formations, orders, captains, waves, cavalry, horse holders), `Sim/War/` (army book, warbands, muster, march, `ArmyBridge`, camps, `Rations`, `ForageParty`, desertion, winter quarters, intel, `CampaignBrain`, `WarEvents`, `ContinentArmies`, `WarSoak`), `Sim/Soldiers/`, `Sim/Siege/`, `Sim/Naval/`, `Combat/Abstract/BattleField`, `CountBattle`; Godot `CrowdLayer`, `TacticalOverlay`, `OrderWheel`, `FormationCards`, `ArmouryScreen`, `IndustryTab`, the Army tab of the Squad panel, the siege panel, the helm HUD, the mint and changer panels |

### 2.2 Carried into Phase 7: where each is answered

| Carried item (source) | Home |
|---|---|
| **S10** gaol rations paid by `SinkReason.Burn` (phase-6 review, Low) | P7-0B (it was the audit's F-3; it moves here because the war-load soak's exact-coin gate WL-1 needs it fixed first) |
| **S14** tribute to a realm without a purse moves no coin (phase-6 review, Low) | P7-0B (also F-3): a documented holding purse for abstract realms; real coin on the side that has a purse |
| **G1** law, family and contracts hours attached only by the Godot street view (phase-6 review) | P7-0B: attached in `WorldRuntime` in a fixed order behind `HasHero`. Every headless soak (WL, the war soak) needs it; it is the audit's L5 |
| **S13** tax evasion is the union of Phase 2's roll and E1's (phase-6 review) | P7-0B: one owner (when unrest is attached, Phase 2's roll is skipped). The levy draw's evasion and the purveyance unrest gates (WL-7, the soak's unrest gate) measure it |
| **S11** `LawWorld.OpinionPm` never wired (phase-6 review) | P7-0B (small): wired to the talk book's trust, as `ContractWiring.TrustPm` is |
| Realm rulers dying and inheriting in play (phase-6-open, part F "left out") | P7-0B: a seasonal old-age check on realm rulers with `HistoryRules.OldAgePctYear` and the `FindHeir` order. A 25-year war soak with immortal rulers would not be credible. Found while writing this overview |
| S1 generated buildings adopted by fire (phase-6 review, **fixed in be76a9b**) | precondition of P7-SG-A (generated walls and gates burn and are adopted the same way) |
| S9 relic listing, S12 compaction keeping book references (phase-6 review, Low) | not blocking; Phase 7 close-out (P7-G2) |
| The purveyance hook, a levy's evaded draft, the riot trigger; stream 0x660 reserved (phase-6-open E1, C3) | P7-AE2 (0x660 now drawn); P7-AM-B (evasion) |
| The vassal's service owed; live armies; war-service contracts are `Road.RecordService` stand-ins (phase-6-open D2, G1) | P7-AE2 (service and scutage), P7-AM-A..E (armies), AM 4.13 (war service is serving in a host) |
| `DiseaseBook.ArmyCampSize` and `UnboiledWaterDays` unfed (phase-6-open C4) | P7-AM-C (camps) through `HostSupply.Camp` |
| The war rules' second step for history (phase-6-open F) | Q12 (history unchanged by default) |
| Phase 4 (review "Moved"): AB13's 2,000 v 2,000 budget, the template library's size, AB-3's 17 of 45 misses, hand and foot shares, plate durations, `ApplyHits`, prisoners from abstract battles, `BattleDemotion` in play | P7-AW-A to AW-C, AW-E |
| Phase 4: mounting and dismounting in a fight, kicks, mounted bandit leaders, fire panic, fog, branches, drowning, slip, cavalry speed | P7-CV-B, CV-C; drowning in P7-SH-A |
| Phase 5: ferries, fishing boats, people in the water, walking a deck, NPC drivers; the abstract ±10% gate; purveyance and scutage (moved on by the Phase 6 overview) | P7-SH-A, SH-B; P7-AW-C; P7-AE2 |
| Phase 5 → Phase 8: a water check in `PlanFitter` | built here **for powered plans only** (LB 4.6); the general check stays in Phase 8 |
| Phase 2: no mint ever runs (`MintMath.StrikeInto` has no caller; the soak asserts `Minted == 0`) | P7-CN1 |
| Phase 2: `TaxSystem.LevyQuota` is data only | P7-AM-B |

## 3. Build order

The 3-helper rule shapes the build order:
- **Three helpers at a time.** Three helpers work at once (the user's rule).
- **Lanes.** Each runs one lane. A lane takes its next part as soon as that part's dependencies are merged.
- **Steps.** A step below is the window of time in which its parts start. Steps are not barriers, so a part may run into the next step.
- **Part size.** Each part is sized for one helper on the cheaper model with its targeted tests. A part names its files, its gate tests and nothing else.
- **Close-out.** The close-out runs the full suite.
- **Timings.** Start and end months come from a lane simulation with the dependencies listed. The working files are not committed; the numbers are repeated here.

**The M-E0 rule, read here.** The plan says the audit's findings are fixed "before armies are built". Until gate M-E0, only two kinds of work may merge:
- economy parts;
- military parts that **create no coin or goods, take no household's labour and move no coin**. These are BB-A, SC-A (it only moves a lord's items between his armoury and his men), AW-B, and, if a lane is idle, BB-B, BB-E, CV-C, SG-A and SH-A.

Everything that loads the economy waits for M-E0. This covers armies on the map, supply, engines built from timber, ships with cargo, sieges, mercenaries and the arms trade.

**The audit's base.** The first audit run (EA2) measures the **Phase 6 economy**, so it runs on a tag `p7-audit-base`. The tag is cut after P7-0A, P7-0B and P7-EA1 merge. ES1 and LB1 wait on their branches until the tag is cut.

0. **Step 0, foundations and the audit harness** (months 0 to 1.4)
   - **P7-0A, shared plumbing** (lane A, 0.4):
     - every enum append of section 8.1, in the order given there;
     - the 0x700 to 0x7EF streams registered (section 5), with the uniqueness test and a test that the bot and load streams (0x700, 0x701, 0x7E4) are never drawn by a game started from the menu;
     - the new `SystemOrder` slots (section 8.4);
     - `Difficulty`'s seventh value, with a six-value array still read as 100;
     - the **Phase 7 seeding step** skeleton, appended to the Phase 6 seeding pass;
     - the save section registry for the ten new sections, the `phase7_trail` hash line (empty until filled) and `CauseKind` rows in `chronicle/types.jsonc`.
     - Files: `Core/RngStreams.cs`, `Economy/LedgerTypes.cs`, `Goods/MassLedger.cs`, `Core/Chronicle.cs`, `People/Types.cs`, `Combat/Melee2/FightTypes.cs`, `Combat/Mounted/TerrainFooting.cs`, `Combat/Watch/SquadTactics.cs`, `Production/OwnershipViews.cs`, `Rule/RuleBook.cs`, `Core/Difficulty.cs`, `Abstract/TickTypes.cs`, `Worlds/Phase6Seeding.cs`, `Save/WorldSave.cs`.
     - Gate: the streams test, the enum append tests (each enum's existing values keep their numbers), `DifficultyTests` with a six-value array, the save round trips unchanged, every golden hash unchanged.
     - **Built (P7-0A):** every 8.1 append in order (`Phase7PlumbingTests` pins each enum's last Phase 6 value and the new names after it); the 0x700 to 0x7E4 streams registered under descriptive names in `Core/RngStreams.cs` (table test, free ranges and 0x7F0..0x7FF stay empty, `CoinageReserved` asserted never drawn, and the three harness streams 0x700, 0x701, 0x7E4 may be referenced only under `Sim/Diagnostics`); the `SystemOrder` constants of 8.4; `Difficulty.IndustryOutputPct` (the seventh value; `ToArray` writes it only when not 100, so a default world keeps its bytes, and a six-value array reads as 100); `Worlds/Phase7Seeding` (`IPhase7SeedStep`, ten `SectionNames`, `PlannedOrder`, no steps yet) run after the Phase 6 pass in `NewGameWorld.Create` and `WorldSave.Restore`; `Diagnostics/Phase7Trail` (empty, so `hashes` prints no `phase7_trail` line until a section has something to hash). No `types.jsonc` rows are needed for the new `CauseKind`s: rows are per entry type and list cause kinds by name, and the validator reads the enum.
     - **Saves stay byte for byte.** Growing an enum widens by-reason tables, which moved the `phase6_trail` golden at first. Fixed by saving those tables *compact* (as wide as the highest reason used, never narrower than the Phase 6 width) and padding on load: the ledger's flow counters (`Ledger.Export`), `MassLedger.DestroyedState` (restores any width from 10 up), the treasuries' by-reason tables (`TaxSave.Capture`, padded in `Restore`), and the hero's skill list (an untrained `Seamanship` or `Engineering` is not written). Each Phase 7 part that uses a new reason or skill simply writes the wider row. Hero command prefix note: `Coin` is `cn:` as in 8.1, the same text prefix as Construction; dispatch is by kind, so the part that writes it must route by `HeroCommandKind`, not by prefix.
   - **P7-0B, Phase 6 debts** (lane B, 0.4):
     - S10 (a prisoner's ration is bought from the nearest baker's purse, else paid to the seat's treasury by transfer);
     - S14 (a documented `AbstractRealms` holding purse);
     - G1 (law, family and contracts attached in `WorldRuntime`);
     - S13, S11, and rulers' old age in play;
     - `AftermathCoin.Plunder` repointed from `Fine` to the new `Plunder` (re-pin any golden hash this moves, with the reason).
     - Files: `Law/Gaol.cs`, `Politics/Diplomacy.cs`, `Worlds/WorldRuntime.cs`, the Game's `StreetView.Talk/Work/Family.cs` (they stop attaching), `Taxes/TaxSystem.cs`, `Law/LawWorld.cs`, `Politics/*` (succession), `Combat/Aftermath/AftermathTypes.cs`.
     - Gate: `EA_F3_*` regression tests (fail before: S10's `Burn` and S14's delta), EA-8, a headless run that sees court days and contracts, the S13 test, the Phase 6 suites.
     - **Built (P7-0B):** S10 (`Gaol.Pay` buys from the baker's or nearest baker's purse, else transfers to the seat's treasury; no `Burn`), S14 (`Diplomacy.AbstractHolding`, one holding purse for every abstract realm; real coin both ways), G1 (`WorldRuntime.Start` calls `Phase6Links.AttachAll` behind a hero (a harness that steps with `AdvanceTo` and never starts the worker calls the same `AttachAll`, as WarLoad and the soaks do; the bare-runtime tests keep their behaviour); each link's `Attach` is once per runtime via `LinkOnce`; the street view only reads `rt.Phase6`), S13 (`EvasionHook` set skips Phase 2's roll), S11 (`OpinionPm` from the talk book's trust), rulers' old age (`Politics/RulerSuccession.cs`, called from `Diplomacy.Season`; stream `DiplomacyTies` index 5; `realm_ruler_died` and `realm_succession` chronicle types), `AftermathCoin.Plunder` on `TransferReason.Plunder`. Tests: `P70BDebtsTests`, `EA_F3_S10_*` in `JusticeTests`, `EA_S13_*` in `TaxTests`. Not built (not assigned here): person-table growth, proposed in `docs/reviews/phase-6.md`.
   - **P7-LB1, power** (lane C, 1.2): `WaterPower`, `WindPower`, `PowerNet`, the pieces, coupling, the water check, the overlay, wear, floods, ice, the `water_sawmill` migration. Gate: LB-1 to LB-7, LB-13 to LB-15.  **Built (P7-LB1):** the sim side of all of it: `Building/WaterPower.cs` (flow, ice, head, wheel output, `WaterSites` with the bank test, the head profile and the mill-site list), `WindPower.cs`, `PowerNet.cs` (`PowerCatalog`, the derived nets and the sharing rule), `PowerWorld.cs` (the daily rules: watts, wear, breakdowns on 0x730, floods on 0x731, `wheel_wrecked_flood`, the sluice, `PlanWater` for the fitter) and `PowerWiring.cs` (`PowerSystem` at H5 hour 0, `+20` after the businesses' daily globals); the 27 pieces (`pieces_machines.jsonc`), `water_power.jsonc`, `power_pieces.jsonc`, the `cn:sluice` desk verb, `PlanFitter`'s `PlanPower` argument, the `saw_mill` kind with its `water_sawmill` migration. Gate: LB-1 to LB-7, LB-13 to LB-15 green (`PowerTests`). Left for later: the Godot overlay, machine panel and ghost text, the millwright proficiency, repair, and the link from a business to its premises' net (`ProductionSystem.StationLabourPm` is the hook); details at the end of `large-buildings.md`.
   - **P7-ES1, crews and limits** (lane A, 0.9, after 0A). Gate: ES-1 to ES-3, ES-6, ES-7, ES-12, ES-13.
     - **Built (P7-ES1):** `Production/Crews.cs` (roles, the T formula, cost, reach, crowding, span, and `Crews.Rate`, the hourly form of T); `ProductionSystem.WorkStations` now credits progress by grade (master / journeyman / hand steps), wages by role (never above the recipe's own wage class, so the price solver's labour cost stays the ceiling), crowding past a station's `crew_max`, a master's reach over the business's other stations, and the span of control, for every business including generated ones (ESQ-1 / Q5); a recipe with no grade row, a crew within its limit and its span is exactly Phase 2's rule. `Production/Limits.cs` + `ScaleWorld` (guild towns, caps, the 0x721 inspection, fines to the town's treasury as `Fine`, the guild master's opinion -20, a closing order after three fines, the manufactory licence with its rent, appointed foremen) and `ScaleSystem` (a season's first day, hour 9, `Outbox + 121`); `ScaleBook` + `Save/ScaleSave.cs` (the optional `scale` section, written only while non-empty; ES2 appends contracts and pattern runs to it); content `production/scale.jsonc` (`scale_settings`) and `max_journeymen` / `max_apprentices` / `max_stations` on `guild` rows; hero commands `bz:foreman|unforeman|manufactory`; the Crew section of the business panel (`CrewView` on `BusinessView`); the steward's span (a works counts as `ceil(crew / 20)` properties). Chronicle: `manufactory_licensed`, `guild_fined_oversize`. **Re-pinned:** `phase5_trail` and `phase6_trail` (both seeds): generated businesses now use division of labour; the generated-layer and history lines did not move. **Not built (left to ES2 or a seam):** the 0x720 spoilage draw, room floor-space crowding (needs LB1's room data), NPC foremen, the `ScaleHash` in `phase7_trail` (the trail has no world handle yet; `ScaleBook.Hash()` exists), the "Move worker" button.
   - **P7-EA1, the audit harness** (lane B, 1.0, after 0B):
     - `WarLoad` on `HostSupply`'s first cut (needs, the ladder's market and baggage sources, pay, muster, blockade);
     - the `econ-audit` and `war-load` commands.
     - Gate: EA-11, EA-12, EA-16.
     - **Built (P7-EA1):** `Sim/WarSupply/` (`HostSupply` first cut: `Register`, `Update`, `Disband`, pure `Needs`, `Supply` with the baggage and market steps, pay in the 8.2 order with arrears and the hunger and unpaid days, `Muster`, `Release`, `Desert`, `SiegeBlockade`, `DaysOfFood`, `HarvestRisk`, `Camp`; `HostSupplySystem` at slot 1,150 acting at hour 18; `WarSupplyBook` and the optional `warsupply` section, absent until a host has existed), `Diagnostics/WarLoad.cs` (the load generator; stream 0x700 named only there), `WarLoadRun.cs` (a new game on the real runtime with the law, family and contract links attached, finding G1; the scenario; the yearly war table; WL-1 and WL-2 gated), `EconAudit.cs` (the 50 checklist rows, each with a result), the `econ-audit` and `war-load` WorldTool commands. Gate: EA-11, EA-12 (two runs equal at day 15; save, load, save byte-identical at day 8) and EA-16 green (`tests/.../EconomyAudit/`). Left for AE1's second half and AE2: food rents, depots and convoys, sutlers, purveyance, forage, the pay chest's moves and capture, kit and loot, `Budget`, chronicle entries and price causes. Details at the end of `economy-audit.md` and `army-economy.md`.
1. **Step 1, the audit run, the industry runtime and the battle grid** (months 1.2 to 3.1)
   - **P7-BB-A, grid and scale** (lane C, 1.0): `BattleGrid`, `FightWorld.Grid.cs`, struct arrays, every scan moved, vitals skip, the allocation gate. Gate: BB-1, BB-2, BB-4, BB-5 (sim side).
     - **Built (P7-BB-A):** `Sim/Battle/BattleGrid.cs` (struct arrays `X, Y, Team, ActBits, FormationId` by fighter id, 4 m cells, a counting sort rebuilt after movement and after separation with no allocation; `Near(depth, x, y, r)` returns the ids that could be in range **in ascending id**) and `Combat/Melee2/FightWorld.Grid.cs` (`Near`, `NearestEnemyOf`, `AnyEnemyWithin`, and `UseGrid`, whose false value makes every query return every id: the old scan, the oracle of BB-1). Every neighbour scan of the fight now asks the grid and keeps its own exact test: `Separate` (resumable box walk, see the spec's Notes), `AdjacentBraced`, `BracedNeighbours`, `OverWall`, `FindTarget`, the brain's incoming-blow scan, target pick and flee, the nearest-enemy searches, the lob test, the throw's aim and the projectiles' hit test; in morale `MoraleOnFallen`, `TermsOf` (the 30 m look only for a fighter with no friend and a foe near), the routing neighbours, the captain and banner (listed once a step), cornered, the quadrants, escape and surrender. `Fighter.X`, `Y` and `ReachMm` became properties reporting to the grid, so a query is exact at any moment, not only after a rebuild. No golden, trail or save byte moved. Gate: BB-1 (200 seeds), BB-2, BB-4 (sample; 100 x 400 v 400 is a Soak test) and BB-5's sim side green (`Combat/Battle/BattleGridTests.cs`). Left: horses in the budget run, the vitals skip (already O(1) for an unhurt book), slots and formations (BB-B); details in the spec's Notes.
   - **P7-IN1, stage runtime** (lane A, 1.2). Gate: IN-1 to IN-3, IN-8, IN-16, IN-19, IN-23.
     - **Built (P7-IN1):** `Production/Industry/` (`IndustryContent`: `ChainKind`, `StageKind`, `StageTier`; `IndustryBook` with `Works`, `WorkStage`, `WorkLink` and the 60-day balance ring; `StageMath`, the pure day formula, tier reached by crew, hauling trips and link capacity; `IndustryWorld`, the runtime; `IndustryWiring` with `IndustrySystem` at H4 `ProductionDaily + 20` and the `in:` desk; `StageHost`, the two hooks Phase 2 asks), `Save/IndustrySave.cs` (the optional `industry` section, written only while a works exists), content `production/industry/chains.jsonc` (3 chains, the 17 stage kinds with all three tiers' crew bands, rates, capacities and stores, and `industry_settings`), `goods/goods_industry.jsonc` (`iron_ore_rough`, `tempered_clay`, `bricks_green`), the generated mine's composite rate (109 kg of dressed ore a worker-day, `ProductionSystem.MineDay`). A stage is a `Business` of a stage kind (the 17 are business kinds appended after every ordinary kind, so no index moves); a works' stages share the first stage's purse (`Business.Purse` and `Party` re-pointed at load by `IndustryWorld.Rebind`); Phase 2's planner, top-ups, dividends and closing skip stage businesses and the labour market hires them to the works' crew target (hewers and carriers by the tier's `support_pm`). Gate: IN-1 to IN-3, IN-8, IN-16, IN-19, IN-23 and IN-26 green (`IndustryTests`), plus the crew-band content check (IN-25's bands) early. Hero verbs `in:found|crew|wage|route|link|cash` through `HeroDesk` (`HeroCommandKind.Industry`). Chronicle `works_founded` and `works_stopped`. **Re-pinned:** `phase5_trail` and `phase6_trail` (both seeds): the three new goods sort into the goods table and move the indices after them (checked: the composite mine rate alone does not move either trail); the generated-layer and history lines did not move; `ContentRemapTests.D` lists the new goods and the 17 stage kinds as new. Left for IN2 to IN5: recognition of tiers from pieces (`WorkStage.Tier` is set by `SetTier` until then), plans, upgrades, seasons, accidents (0x740), kiln losses (0x742), packing and containers, drying days, the finery's bar, the stage panel, balance sheet and payback views, gate absorption and carting, contracts, own caravans, NPC founders; details in `industries.md`'s Notes.
   - **P7-EA2, the audit run** (lane B, 0.7):
     - the four passes on `p7-audit-base`;
     - the probes and bots;
     - `docs/reviews/phase-7-audit.md` with the grep lists and every checklist row.
     - Gate: EA-1 to EA-10, EA-13 and EA-14 measured.
     - **Built (P7-EA2):** the audit ran on `p7-audit-base` and its findings are in `docs/reviews/phase-7-audit.md` (EA_F1 to EA_F16: High EA_F1 raw extraction at 8.7-9.3x its wage, EA_F2 loot worth up to 206x its listed value; Medium EA_F4 silver, EA_F8 strike and melt outside the seigniorage band, EA_F9 levies do not cut the harvest, EA_F11 iron for the kits, EA_F16 the money supply swings on seed 42, EA_F7 the realm's empty purse; the rest Low). All 50 rows of `econ-audit` print a result; X2 to X6, X14, L3, S-2, S-4, I4 and L6 are now measured from content (`Diagnostics/AuditMeasures.cs`). The harness gained `WarFunding` (the realm raises a campaign's coin: aid from the fief treasuries, then a tallage on household purses: the decision that makes WL-4 and WL-5 mean something), `WarGates` (WL-1 to WL-15 from the yearly rows), the farm-labour, price, wage, idle and merchant columns, and `war-load --with-control`. Tests (`tests/.../EconomyAudit/`): `AuditProbeTests` (EA-2 to EA-5, EA-7 to EA-10, X14), `AuditBotTests` (EA-1 and EA-14 the trader, EA-6 the 1,000 moves, EA-13 the rich player), `AuditWorldProbeTests`, `AuditFindingTests` (`EA_F5`, `EA_F6`, `EA_F7`, `EA_F9`), `EA_F1` and `EA_F2` beside their measures; a finding the fix of which belongs to EA3a, AE2 or CN1 has its test **skipped with the owner named** (switch it on with the fix). **No content, price, rule or golden moved**; the sim is unchanged. Result: WL-1 to 5, 8 and 11 pass on seed 1234 (WL-3 fails in peace on seed 42); WL-6 and WL-10 fail (EA_F9, EA_F10: the control drifts as much); M-E0 stays closed on EA_F1, EA_F2 and the Medium findings (the user signs any accepted, Q15). Details at the end of `economy-audit.md`.
   - **P7-LB2, buildings** (lane B, 1.0). Gate: LB-8 to LB-12, LB-16.
     - **Built (P7-LB2):** the eleven large room types (`build/rooms_large.jsonc`, with `RoomTypeDef`'s new geometry rules `min_length`, `min_width`, `max_width`, `light_per_m2`, `door_tag`, `max_doors`, `min_station_count`, `storage_litres_per_m2` and `large`, read by both detectors through `Building/RoomShape.cs`, so RD-1 parity holds), the 13 library plans (`blueprints/industry.jsonc`, powered ones with `power_w`; `BlueprintWriter` now keeps `power_w`), the large content (`production/large_buildings.jsonc`, `LargeBuildingRulesDef`), `Production/Works.cs` (`LargeRooms`, `Armouries` with capacity, care, issue book, view and gaps; `Warehouses` with leases and rents; `Depots` with lots, rotation and target; `Barracks` upkeep), `Production/WorksWorld.cs` (`WorksWorld`, `WorksSystem` at H5 hour 0 `+21`, `WorksWiring`: armoury care a day, rents and depots a season, barracks straw every 15 days, the hall's span and the machines' labour through a business-to-premises link), `Production/SeatBuildings.cs` (the pure list of what each realm seat is to get), the `works` section grown additively (keys 1 to 7, `WorksBook.Hash`), `StoreKind.Warehouse`, eight business kinds (`late`: right after the ordinary kinds, before the stage kinds), the ropewalk's `lay_rope` recipe and `hemp_fibre`, the `depot_filled` chronicle type. Gate: LB-8 to LB-12 and LB-16 green (`LargeBuildingTests`). **Shared seams touched:** `ProductionSystem.SpanBonus` (new hook, beside `StationLabourPm`, which `WorksWiring` chains and does not replace), `Crews` callers in `ProductionSystem.SpanSpeed` and `CrewViews`, `ProductionContent.Load` (the `late` kinds), `RecipeBook.StationKinds` (two kinds appended), `WorldRuntime` (one `WorksWiring.Attach` line after `ScaleWiring`), the shared content (`rooms`, `pieces*`, `businesses.jsonc` appended). **Re-pinned:** `phase5_trail` and `phase6_trail` (both seeds): a new good, one recipe and eight business kinds change the economy's tables; no generated-layer or history line moved. Left for later: the Godot panels and overlay, placing the seats' keep and depot as finished construction and stocking them (`SeatBuildings.Plan` is the list the step will walk; `Phase7Seeding.DefaultSteps` is still empty), floor-space crowding in `ProductionSystem` (the hall's 3 m2 is `HallEffects.CrowdingM2`, nothing reads it yet), the `WorksHash` in `phase7_trail`, arrows in the chests, the harvest market's purchase for depot rotation (`WorksWorld.SellGrain` is the hook), repair of worn machines; details at the end of `large-buildings.md`.
   - **P7-ES2, costs, contracts and patterns** (lane C, 0.9). Gate: ES-4, ES-5, ES-8 to ES-11, ES-14 to ES-16.
     - **Built (P7-ES2):** `Production/FixedCosts.cs` (shared heat: a forge or oven beside a lit mate saves the fixed lit-hours fuel; firings burn a fixed share however small the load), `Patterns.cs` (munition runs: setup once plus pieces, practice, quality cap), `SupplyContracts.cs` (`SupplyTerms` price and payment, `Carting` cost table), `Absorption.cs` + `ProductionSystem.AbsorbView` (the planner stops a batch past the cap when the bid is under cost), `ScaleWorld.Supply.cs` (offer, daily delivery, miss, break, restitution, machine couplings), `CostViews.cs` (the Costs section: `BusinessView.Costs`), the `scale` section's new keys 5 to 10 (contracts, runs, couplings), content `scale.jsonc` rows (`heat`, `firing`, `patterns`, contract and carting numbers), the `munition_works` kind, goods `spearheads` and `kettle_hats` with recipes `forge_spearheads` and `forge_kettle_hats`, desk verbs `bz:contract|endcontract|couple|uncouple`, chronicle `supply_contract_broken`. Gate: ES-4, ES-5, ES-8 to ES-11, ES-14 to ES-16 green (`ScaleCostTests`). Shared files touched: `ProductionSystem` (TryStart fuel, pattern hours, Finish, planner, `StationMachineWhy`), `RecipeBook` (`Recipe.Pattern`), `Business.cs` (`ProductionContent.Load`), `ScaleBook`/`ScaleSave`/`ScaleWorld`, `BusinessDesk`, `OwnershipViews` (`Costs`), `BusinessPanel`/`StreetView.Business`, `en.csv`, `CraftingFullTests` counts (44 business recipes). Left out: NPC-offered contracts, the Q-menu offer UI, the 0x720 spoilage draw, room floor space, the `ScaleHash` on `phase7_trail`; details in `economies-of-scale.md` Notes.
2. **Step 2, chains, the armoury, the audit's fixes, gate M-E0** (months 2.5 to 5.1)
   - **P7-CN1, coinage, sim side** (lane A, 0.5, merges after EA2's run). Gate: CN-2 to CN-10, CN-13 to CN-16.
     - **Built (P7-CN1):** `Coin` (`FromCopper|Silver|Gold`, `Parts`), `MintMath` per metal (`MintMetal`, `StrikeGold`, `StrikeCopper` with the 4% cap), `Economy/CoinStock` (the gold book on `Ledger.Coinage`: opted-in purses by kind plus explicit opt-ins, the large-payment and clamp hook on `Transfer` and `Sink`, loose gold, melt of gold, silver and copper), `CoinageBook` (queues, the moneyers' day, season rows, `Hash()`), `Mints.cs` (`MintWorld`: one mint per realm in its largest town, walk-in counter, queue, strike with a logged `Seigniorage`, dust kept, metal destroyed as `Materialised`, copper token path, `mint_struck` and `mint_copper_capped`, `MintSystem` at H5 hour 0), `MoneyChanger` (2% `Exchange`), `CoinageWiring` + `CoinDesk` (`cn:sell|change|melt`, routed by `HeroCommandKind.Coin`), `Encumbrance.CoinMassG(ledger, purse)` and `CoinMassMg`, `Save/CoinageSave` (the optional `coinage` section), `LoadReport.Notices` (the coinage notice for a header below 0.4.0; the game now writes its `BuildVersion`, 0.4.0-dev), the `MintMaster` office filled by the rule's vacancy pass (Trading, best in the mint town), content (`gold`, `gold_ore`, `mining_gold`, `refine_gold`, `money_changer`, the `money.jsonc` keys, the `mintmaster` office). Gate: CN-2 to CN-10, CN-13 to CN-16 green (`CoinageTests`). **Re-pinned:** `phase5_trail` and `phase6_trail` (new goods, recipe, kind and extraction; the mints strike what the hearths send); generated-layer and history lines did not move. **Measured for EA3 (F-4):** with the mints wired, seed 1234's own hearths send about 7.8 kg of silver a day, so the moneyers' capacity (6,000 pieces a day) is reached and the supply grows about 24% over a 60-day year; the deposit grades are the audit's to tune. Left out: changers seeded into towns and the NPC change policy (cut-order item 10), the `mint_house` room and civic plan, moneyers as people, continent realms striking, `pan_gold`, `gold_found` and `hoard_found`, the Phase7Trail line for `CoinHash`; details in `coinage.md`'s Notes.
   - **P7-IN2, chains and tiers** (lane A, 0.9). Gate: IN-4 to IN-7, IN-12 to IN-15, IN-21.
     - **Built (P7-IN2):** `IndustryWorld.Build.cs` (recognition of a stage's tier from the fixtures built in its **site**, `SetSite`; up and down the same day; `works_upgraded`; the machines' share `MachinePm` from the premises' power net; the drying days, the finery's bar, kiln losses (0x742), accidents (0x740) through the wound book, packing in dry casks, winter and flood stops), `IndustryWiring.Hook` (construction book, power nets, river-high days, wound book), desk verbs `in:site` and `in:upgrade` (the next tier's plan placed as ghosts; commission a builder with the existing `cn:commission`), content `build/pieces_industry.jsonc` (24 pieces), `blueprints/industry_tiers.jsonc` (33 open-air tier plans tuned to the 5.3a table, IN-27), the stage kinds' `recognise`, `capacity_unpowered`, `accident_ppm`, `floods`, `winter_pm`, `dry_days`, `loss_*`, `bar_pm`, `container` fields, `dry_cask` and `make_dry_cask`. Shared files touched: `BlueprintDef` (`open_air`), `BuildingGrid`/`BlueprintParser`/`PlanLibrary.Cells`/`BlueprintValidator` (an open-air plan has no roof and no door check), `StageMath.Plan` (a powered tier's machines lift only the capacity), the save's `industry` section (key 6, `Extras`). Gate: IN-4 to IN-7, IN-12 to IN-15, IN-21, IN-27, IN-28 green (`IndustryTierTests`). **Re-pinned:** `phase5_trail` and `phase6_trail` (the new good and recipe shift the tables); generated-layer and history lines did not move. Left for IN3 to IN5: the stage panel and upgrade view, clay weathering and mud, the crate and withies containers, selling and caravans, NPC founders; details in `industries.md`'s Notes.
   - **P7-SC-A, roles, templates and the armoury** (lane B, 1.3, uses LB2's armoury). Gate: SC-1 to SC-6.
     - **Built (P7-SC-A):** `Sim/Soldiers/`: `TroopRoles` (roles, wages with the mercenary x1.5, requirements, `Meets`, wealth classes), `ArmouryFill` (the pure `Plan`: role priority, then skill, then person id; per slot the best score, own kit first when it scores as well; misfit -300 unless an armourer is free, in which case a refit job is queued and the piece stays in the armoury until `CompleteRefit`; `Apply`, `Discharge`, `Equip`, `Strip`), `LevyGear` (levies bring the household's kinds by wealth, items moved), `SoldierBook`/`SoldierSave` (the optional `soldiers` section, keys 0 and 1; absent while empty, no bump), `SoldierWorld`/`SoldierWiring.For`, `SoldierDesk` (tag `so:` on the already appended `HeroCommandKind.Soldier`: enlist, fill, equip, strip, role, discharge), `Armouries.IssueItem` (one chosen item through the issue book), content `war/troops.jsonc` (10 roles), `war/loadouts.jsonc` (`soldier_rules` and 13 templates), 14 `so_*` strings. Gate: SC-1 to SC-6 green (`SoldierTests`, 12 cases with the hero-as-soldier, save, desk and determinism cases). Creates no coin or goods and takes no labour; no RNG stream drawn (the plan is pure), so 0x780..0x7EF is untouched. **Shared files touched:** `Works.cs` (`IssueItem`), `HeroDesk.cs` (one dispatch line), `WorldSave.cs` (the `Soldiers` property, capture, restore, write), `en.csv` (a `so_` block before `fl_`). No golden moved. Left out: the Armoury screen, the Refit mode of the Inventory screen and the Army tab (Godot), the `Armoury` input context, the `SoldierHash` in `phase7_trail`, `HostSupply.Kit`/`ReturnKit` (AM-A's seam over `ArmouryFill`), the refit job's armourer time and wire (crafting), upkeep of kit on campaign; details in `soldiers-and-companies.md` Notes.
   - **P7-IN4, selling** (lane C, 0.7). Gate: IN-17, IN-18.
     - **Built (P7-IN4):** the gate sale with carting and absorption (`IndustryWorld.Selling.cs`: `QuoteGate`, `ViewGate`, `SellAtGate`; the best book in the absorption reach by book price less the carting of ES2's `Carting.Plan`, the cart's crew and team to a carter's household and its tolls to the town's treasury or toll purse, a place with no carter or no gain sells at the works' own town), the contract routing (`Routing.Contract` keeps three deliveries of each open contract the stage is the seller of and sends the rest to the gate; the route reverts to the gate when the contract ends), and the works' own caravans (`OwnCaravans.cs`: `Quote` by part, `Send`, the standing order; wagons bought once from the wainwrights and kept, drivers and guards hired from the idle, a master of Trading 40 or more at 500 a day and 2 percent of the sales, ties on stream 0x743; the caravan is an ordinary Phase 2 caravan whose owner is the stage's business, reached through the new `IOwnCaravans` hook in `ContinentTrade`: the day's pay, the sale at the far end, the settlement at home, a loss; destinations are a town of the region (real roads, `CaravanRoads.BuildBetween`, sold to the town's merchants by `MarketBook.PlanSaleToMerchants` in the quote and `SellToMerchants` on arrival) or a neighbouring region over its border gate (the existing gate trade: the summary's price, the season's pay budget, customs, the realm toll)). Desk verbs `in:order|send|master` and `in:route|...|contract|caravan`; the `industry` section's `Extras` tail (orders, trips in flight, tallies; absent when empty, so old bytes are kept); chronicle `caravan_returned` and `caravan_returned_gate`. Gate: IN-17 and IN-18 green, plus contract, absorption, save and trip tests (`IndustrySellingTests`). **No golden moved** (a world with no works never reaches the new code; the new content rows and strings are not in any trail). Left for IN3, IN5 and EA3: the Sell panel and the quote's view, NPC owners' caravans, a back-haul, caravans to a neighbour that is itself run in detail, selling goods a caravan could list at a far book, the export payback measure (EA3b). Details in `industries.md`'s Notes.
   - **P7-CN2, coinage display** (lane C, 0.3): `MoneyText`, `UiKit.Money`, the sweep of about 35 Game files and 22 localisation rows, the mint and changer panels. Gate: CN-1, CN-11, CN-12.
     - **Built (P7-CN2):** `Core/MoneyText` (`Format` in the styles Amount, Exact, OneUnit and OneUnitCopper; `PerUnit` with the pack rule; `Pieces`/`PiecesOf`; `Words` for the key table with a symbol fallback) and `MoneyFormat` (the `{n:money}`, `{n:money_exact}` and `{n:money_unit}` parameter kinds for `string.Format`, used by `L.F`, the hero-text resolver and its en.csv fallback), `UiKit.Money|PerUnit|Pieces`, the chronicle parameter type name `money` (`mites` still read), `Quotes.Money` and `Quotes.PerUnit` now delegate to `MoneyText`, `IndustryViews.Silver` is `OneUnit`. The sweep: 32 Game files call `UiKit.Money` (no `Quotes.Money` left), the Autopilot and Job panels' own formatters and the Business panel's per-kg cells replaced, 24 rows rewritten or dropped (`UI_PENCE`, `CHAR_COIN`, `TRADE_PER_*`), seven `mt_` rows, the `co_q_` and `co_ui_` rows. `CoinPanel` (the mint counter and the money changer; `CoinView` is the read-only window, built at a barrier; every button is a `cn:` verb through the desk; Q in a town offers both), `StreetView.Coinage.cs`, both added to the smoke's fit check. Gate: CN-1 (Exact), CN-11 and CN-12 green (`MoneyTextTests`: every style, the pack rule, the provider, no mites, crowns or cr in the rows or in a Game string literal). No sim rule, stream, save or golden moved. Details and what is left in `coinage.md`'s Notes.
   - **P7-EA3a, the first fixes** (lane A, 0.5): F-1 (the R1 rule, if Q2 says yes), F-2 (loot values from material), F-4 (deposit grades), and every other High or Medium finding of EA2. Each fix has an `EA_F<n>` test that fails before it.
     - **Built (P7-EA3a):** EA_F1 (R1: raw goods priced from their labour, every raw kind earns 1.25-1.4x its wage; logs 0.2, firewood 0.3, clay 0.1, dressed iron ore 1.8, salt 4.7 mites a kg; `mining_salt` 40 kg a day), EA_F2 (loot is worth its material: `SiteLootEntryDef.Material` no longer defaults to iron, which had made every loot-table item iron; precious rows are smaller and real; site budgets unchanged), EA_F6 (rope 298 a kg from twine at 200). **Decision:** the five finishing goods of the worked examples are pinned at the R1 table (charcoal 12, bloom 240, bar 480, planks 3.0, bricks 1.2; `RecipeTests.Pinned`) and their recipes leave the 45% band, because re-solving them from the cheap raws gave an iron works a 15-year payback and a losing sawyard; at the pins an L1 works repays in 2.5-2.9 game years (iron ~813 mites a day, ~175 days; clay ~165; wood ~150: sheet arithmetic, IN3 simulates it). `scotch_kiln` 150 and `hot_floor` 60 worker hours keep the IN-27 bills within 20% of table 5.3a. EA_F4 (deposit grades) stays with CN1; EA_F5 accepted; EA_F16 with EA3b. Trails re-pinned; no generated layer moved.
     - **Built (P7-EA3c):** EA_F4 (deposit grades, content only): `cupel_silver` makes 0.15 kg of silver a tonne of galena (was 3 kg) and `refine_gold` 1 g of gold a tonne (was 6 g; `gold_grade_mg_per_t` 1,000), lead's price follows the solver (27.298); with the mints wired the money supply grows 0.7-2.5% a game year (seed 1234 mean 1.58%, seed 42 mean 1.82% over four years; was 25-28%); `econ-audit` S-2 and L6 pass (`AuditMeasures.SilverPerWorkerDay` reads the grade from the recipes); `MoneyGrowthTests` (Soak) pin it. 10c children: `child_harvest_min_age` 7 and `child_harvest_pct` 33 in the farming settings; `FarmingSystem` keeps a children's-hours table (`_hhChild`, derived each day, never saved) used by Reap, BindCart and hay only, the household's own first, then hired by neighbours at the pro rata wage; `FarmingChildrenTests` (the 10c household test, never a plough or sow task, 12-15 at half a day). EA_F9: `HostSupply.Muster` takes field hands first. **WL-6 still fails** (Y8 harvest -0.4% against the control): the harvest is not labour-bound in this model; the sweep and the options are in the audit review, and `EA_F9_*` stays skipped. Trails re-pinned (`phase5_trail`, `phase6_trail`, seeds 1 and 1234: new grades, lead price, children's hours); generated-layer and history lines did not move.
     - **Built (P7-EA3d):** `Muster` draws the levy by village: villages nearest the fief's seat first, farmsteads after the villages, the seat last; one man a household, field hands first, up to `levy_village_cap_pct` (50) of a village's households; `harvest_lost_levy` chronicle entry at the year's close. The drawn villages fall behind in the first days of reaping (-2.5% of acres by day 30; undrawn villages equal the control) but catch up inside the grace: Y8 harvest -0.2% against the control, so WL-6 still fails (see the audit review, "EA3d"). The decision is still open: a harvest-labour retune, a bigger quota from fewer villages, or accepting a war that costs no harvest.
   - **P7-IN3, the Industry tab** (lane C, 0.7). Gate: IN-9 to IN-11 as views, a screenshot at 960 x 540.
     - **Built (P7-IN3):** `IndustryViews.Build` (`Sim/Production/Industry/IndustryView.cs`: the hero's works as a snapshot built at the barrier: the chain's stages with tier, crew band, throughput, stock, stop and reason, the links' hauling, the bottleneck named with its reason, the balance sheet by day and by 6-day week for the works and each stage, the upgrade row from IN2's plan, the gate lines and the own-caravan quote with its parts), `IndustryTab` in the Ledger (flow, stage detail with crew and wage and route and the one-click plan, the Sell lines, the sheet in silver with one decimal), desk verbs only (`in:crew|wage|route|send|upgrade`). No sim rule, stream, save or golden moved. Shared files touched: `LedgerScreen`, `StreetView.Business.cs`, `StreetView.Debug.cs`, `NewGameDebug.cs`, `Phase3Smoke.cs`, `en.csv` (a `ui_ind_` block and `lg_tab_industry`). Gate: the bottleneck, balance-sheet and upgrade-row tests (`IndustryViewTests`), the smoke's fit check at 960 x 540, the screenshot `docs/playtests/p7-in3-industry-960x540.png`. IN-9 to IN-11 as sheets with prices as parameters are left to IN5's calibration; details in `industries.md`'s Notes.
   - **P7-IN5, NPCs and calibration** (lane B, 0.3). Gate: IN-20, IN-22, IN-24.
     - **Built (P7-IN5):** NPC founders (`IndustryWorld.Npc.cs`; `IndustrySystem` looks at hour 7, the pass acts once a working week): a household with >= 500 gp, an adult head and no works looks at its place and its fief's, where the trade is already worked (an iron mine, a clay pit or brickworks, a woodlot, sawpit or saw mill); the founders' reference sheet of each chain (the 5.11 worked examples as content, at the place's book prices, the payback multiplied by one more than the works of the chain already there) must say 3 game years or less; he needs the idle hands (`LabourMarket.IdleCount`) and the book's stock of the materials. He **pays the first-tier buildings with real coin and goods** (the materials bought from the book and built in as `Materialised`, the labour as wages to the place's building trades: no plan exists for tier 1, so not the construction contract), sets the sheet's crew, sells at the gate, draws a dividend above his reserve and **closes** a works that loses money for half a year (chronicle `works_closed`). Content: `build`, `sheet`, `crew`, `site_*` on the chain rows and the `npc_*` settings. Four fixes the first full-world run of a first-tier works showed (a starved stage lends its crew's day to the handling stage in front of it; a gate sale books its income in the ring, which read zero before; a byproduct passes the packing stage). Gate: IN-22 green (a 600 gp lord with a 2.5-year expected payback founds an iron works, with 4 years does not; coin and mass exact); **IN-20 green after calibration**: measured in a run on seed 1234 at Ranunfield (three works, one year, coin and mass checked every 90 days) the L1 paybacks are **iron 145, clay 148, wood 147 days** (2.4 to 2.5 game years) once the bills are 376,000, 109,000 and 72,000 mites (the sheets' 140,000, 30,000, 35,000 at the base prices; by day 60 every book's reference price is about twice its base while the wage classes are not, so the nets are 1.6 to 3 times the sheets'), the slider at 5x makes 4.96 times the ore but loses the whole 1,000,000 of working capital in four months in one town's book; IN-24 (two years, three works and the founders, an own caravan on the road at the save, save, load and continue equal to the uninterrupted run, a second save identical). **No golden moved.** Shared files touched: `LabourMarket` (`IdleCount`), `IndustryDefs.cs`, `chains.jsonc`, `IndustryWiring`, `IndustryWorld.cs` (starved stages, byproducts), `IndustryWorld.Selling.cs` (the ring), `types.jsonc` and `en.csv` (`works_closed`; the translation file is not regenerated). Left out: NPC upgrades and own caravans (nothing to place a plan on, an L1 never fills a wagon), the hero's L1 buildings (free, as IN1 left them), selling surplus raw goods from a middle stage. Details and the findings for EA3b (the price level against the wages, the empty labour market, the region's charcoal) in `industries.md`'s Notes.
   - **P7-EA3b, the second run** (lane C, 0.3):
     - the war-load soak on five seeds with coinage, scale and industries in;
     - an L3 iron works near the route.
     - Gate: EA-15 (WL-1 to WL-15), `econ-audit` with no failed row.
     - **Built (P7-EA3b):** the second run, measured and reported, not fixed (`docs/reviews/phase-7-audit.md`, "Second run (EA3b)"). Harness: `L3IronWorks` (`Diagnostics/WarLoadIndustry.cs`: an L3 iron works founded in Y2 at a town near the march route by a 26-40 year old owner, every stage at tier 3, selling on its own caravans over the border gate, working capital as counted seed money, the building bill 674,003 mites as its `BuildingsCostMites`), `WarLoadChecks` (WL-13: two runs equal at Y10 day 60 and three save round trips, the mid-siege one included), new yearly columns (coin by source and the border by trade and people, works founded and closed, the price level, charcoal and iron, the clock), `war-load --l3 none|gate|caravan --l3-fuel --checks`, `econ-audit --days N` (the world is run first), WL-6 printed as waived (10e), WL-10 read against the control at the same year (EA_F10), WL-4 printing the control's figures, X2 printed as accepted. **Result on 1234, 42, 1, 7, 99 (25 years, war and control, both with the L3):** WL-1, 2, 5, 8, 10, 11 pass on all five; WL-13 (1234) and WL-14 (1234, quiet machine) and WL-15 (1234) pass; **WL-3 fails on 1234 (0.50% average), 1 and 7 and WL-4 on 7**, all of them the economy's own and equal in the control (border trade after a poor harvest; a harvest-failure year); WL-7, 9, 12 are not measurable until AE2 and AE3; `econ-audit --strict` has no failed row on the five seeds (X2 accepted Low, S-3 passes once the mints have run). **EA_F16:** the swing is border trade (people's net 0.06-0.27% sd), the mints are smooth (+1.7 to +2.4% a year) and decide three +8% years; recommended: read WL-3's band net of the mint; the user decides. **The L3 works:** repays in 3 to 6 game years on its own caravans (target 2-3), needs 4 to 19 million mites of working capital (6-28x the bill), never repays at the gate alone, and is stopped by charcoal and cash. New findings EA_F17 to EA_F24 (Medium: F18 L3 capital and payback, F19 charcoal, F20 a works closes with its owner, F21 no idle labour); **M-E0 still needs** the user's signature on EA_F16 and EA_F17, the four Medium findings fixed or accepted, and the five-seed run repeated with AE2 and AE3 in for WL-7, 9, 12. No sim rule, content value or golden moved.
   - **P7-EA3e, the audit's two fixes** (lane C, 0.2): EA_F20 and EA_F19 as signed off in 10f, then the L3 re-measured (EA_F18). Gate: a works passes to an heir, a buyer or the lord with coin and mass exact; new burners open where charcoal is dear; `econ-audit`, the war-load harness and the listed suites green.
     - **Built (P7-EA3e):** EA_F20: a works whose owner died or left is handed on the same day (`IndustryWorld.Succession.cs`): the will's heir, else the law's order of kin (`EstateSettler.Heirs`: alive, 16 to 64, owning no works), else sold to the richest household of the fief that can pay twice the price (half the buildings' bill; the price goes to the law's first heir's household or the fief's treasury), else escheated to the fief's lord, and only a fief with no living lord closes it. Stages, crews, stores, cash box and counters stay; only the owner of each stage's business and the staff's employer change. Chronicle `works_inherited`, `works_sold`, `works_escheated` (roots, cause `Death` or `Migration`, note keys; `types.jsonc`, `en.csv`). Seed 7's L3, which ended in Y9 in the second run, now passes to an heir and runs to Y25. EA_F19: `GrowthSystem.OpenBusinesses` opens a `charcoal_crew` (4 clamps, 12 hands, no lot) in a settlement of a fief where some book's charcoal has been at 1.15 of its reference for 3 seasons while the settlement's logs are not dear, at least 6 idle adults are at hand and 400 t of woodland above the cutting floor lies in reach, the one nearest the fief's dearest charcoal book first; one a season, 24 open at most (`growth.jsonc`: `charcoal_crews_per_season`, `charcoal_crews_max`, `charcoal_min_hands`, `charcoal_min_wood_g`); the crew hires from the labour market and buys its logs from the books, so the woodcutters cut the woodland, which regrows. The Industry tab already names "short of fuel (charcoal)" as the bottleneck (a test pins it). **L3 re-measured** (25 years, caravans, no faucet; before on the main tip): seed 42 pays back in 2.1 years (2.2 before), working capital 4 M (4 M); seed 7 in 2.4 years, 10 M; seed 1234 in 3.6 years (3.6 before), working capital 9 M (19 M before), staying paid back from Y6 (Y19 before). The burners (4 to 24) did not raise the charcoal made (0.2 to 0.6 million kg a game year either way, demand-limited) and fuel still stops the L3 for stretches when its town's book runs thin; seed 1234's capital moved between 9 M and 24 M with the siting rule, so the halving is not firm; see the audit review, "EA3e". `phase6_trail` re-pinned (seeds 1 and 1234: the burners, the works' succession); `phase5_trail`, the generated layers and the history lines did not move. Tests: `IndustrySuccessionTests` (8), `CharcoalGrowthTests` (2), the IN-3 fuel bottleneck test. The IN-20 rig switches the growth's burners off (its faucet stands for them; with them on the clay L1 paid back in 230 days or worse). EA_F21's Industry-tab wage view (signed off for this part as view only) is not built here.
   - **Milestone M-E0, "the audit gate"** (about month 5.1). The **month-6 checkpoint**.
     - **Closed 22 Nov 2026.** EA3a-EA3e merged; the user signed off the second run (§10f): EA_F16/F17/F21 accepted, EA_F19 (charcoal burners follow demand) and EA_F20 (works pass to heirs) fixed, WL-6 waived (§10e). Still open and carried: WL-7, 9, 12 (measured when AE2 and AE3 exist); EA_F11 iron for the kits (AE3); EA_F18 the L3's working capital, still 6-13x its bill (the IN3 view and the Phase 7 close-out); the burners' effect on the IN-20 clay works (the IN-20 rig turns them off). The military parts that move coin, goods or labour may now start.
3. **Step 3, armies and supply** (months 4.4 to 8.6)
   - **P7-AE1, `HostSupply` complete** (lane A, 1.2):
     - the ladder with food rents, depots and convoys;
     - muster and release (the AM levy rule, section 8.2);
     - harvest risk, camps, abstract parity.
     - Gate: AE-1 to AE-4, AE-11 to AE-13, AE-21.
     - **Built (P7-AE1):** the ladder is whole as far as this part goes: baggage, **food rents** (the lord's own household barn, `FarmIds.Barn`, within the buy radius, keeping 30 days of his household's grain), **depots** (a settlement's war depot, `HostIds.Depot(s)`, a granary store opened by `HostSupply.OpenDepot`; named in `SupplyPolicy.Depots` as settlement indices; within 8 km it delivers the same day), **convoys** (below 3 days of staple in the baggage the quartermaster sends carts to the nearest depot beyond a day's reach that holds a day of staple: `Sim/WarSupply/HostConvoys.cs`, a real Phase 2 caravan over `CaravanRoads.BuildBetween`, hired drivers paid from a float of the pay chest, grain moved store to store at the depot, home into the baggage, the carts back to the host; a convoy raided loses its load to the hideout's chest and its carts; a second hook `ContinentTrade.OwnWar` beside the works' `Own`) and the **market** at its asks. `army_bought` is the price entry (5% or more on the staple ask; caused by the host's campaign entry, `HostSpec.Campaign`, else its note key); `siege_starving` (under 5 days of food, once a blockade) and `depot_emptied` are written the same way; the types and strings are in `types.jsonc` and `en.csv`. `Camp` feeds the disease rule's `ArmyCampSize` (men and followers of a camped or besieging host, for its levies and its lord). **Abstract tier:** `ToAbstract`/`ToDetailed`: the same needs, counters and pay; the food is bought abroad at the books' reference price times the continent's grain index from the chest (coin leaves as `EmigrantCoin`, goods enter as `Import`), so a host moving between tiers keeps its account. Saves: the `warsupply` section gains convoys, the noted larders and depots, and per host the campaign entry and the abstract flag (keys 5 to 7 and 12, optional; older sections load). Gate green: AE-2, AE-3, AE-4, AE-11, AE-12, AE-13, AE-21 (and AE-1) in `HostSupplyLadderTests` and `HostSupplyTests`, plus the convoy's save and load on the road equal to the straight run. **WL-8 before and after the part (seed 1234, years 1-10, with the control): raised 76,728,569 mites over the campaigns; 78,748 man-days unpaid, 29,367,736 mites of arrears, 787 stand-in deserters; after: the same, 76,728,569 raised, 78,748 man-days unpaid, 29,367,736 mites of arrears, 787 stand-in deserters (the numbers did not move: no host of the scenario has a lord's barn or a depot).** The realm's purse is still the harness's `WarFunding` (aid and tallage): the real rule for it (`DaysFunded` over the treasury, scutage, subsidy and loans, `Budget`) is AE2's finance, so WL-8 does not move here (EA_F7 stays carried to AE2). Shared files touched: `ContinentTrade.cs` and `CaravanStep.cs` (the second owner hook), `IndustryWorld.Succession.cs` (a works sold to the first heir's own household paid that household's purse to itself and threw; no coin moves then), `types.jsonc`, `en.csv`. No golden moved. Left out: purveyance, scutage, forage, sutlers, `Budget` and finance (AE2); the arms industry and loot (AE3); the steward's filling of depots from food rents and the harvest (4.6; depots are filled by whoever opens them), LB2's room-keyed depots (the seeding step "armoury_depots" is not built, so none exist), `war/supply.jsonc` (the constants are in `SupplyConfig`), the Army tab's Supply view, a convoy's escort, and the realm-owned depot's `depot_filled` write for war depots. Details in `army-economy.md` Notes.
   - **P7-BB-B, formations and orders** (lane B, 1.3). Gate: BB-6 to BB-13.
     - **Built (P7-BB-B):** `Sim/Battle/FormationBook*.cs`, `FormationRow.cs`, `Slots.cs`, `FormationTypes.cs`; content `battle/formations.jsonc`, `orders.jsonc`, `captains.jsonc`; the fight's formation book (`FightWorld.Formations`, null until a formation is made, so no existing fight, hash or save byte moved) with small guarded hooks in the step, the brain, morale and `FightRecord` (optional key 25, no version bump). Slots by role, skill and id, exact to the mm; closing ranks (40 ticks less Drill / 25); the anchor braked by the 20th-percentile pace of rank 0 and stopped under cohesion 600; turn, wheel and deployment from column; orders with the voice, horn or runner delay (queue of four, runner death loses the order), captains by Tactics in four channels (stream 0x780), formation flank term, rout cascade, wall bonus, fallen captain, hero aura, entry-point rout. Gate: BB-6 to BB-13 green (`Combat/Battle/FormationTests.cs`, 34 tests); the save round trip equals the uninterrupted battle. **Two figures in the spec disagree and the gate's reading was taken:** the delay formula is `Leadership x 4 / 5` (the written `x 2 / 5` gives 60 ticks at Leadership 50, BB-10 says 40), and deployment is `ranks x 1.5 m / 3 m/s` (a 300-man column is 50 s; the 150 s of BB-9 is 300 ranks, 900 men), tested at both. Budget (`FormationBudgetTests`, 2,000 soldiers in 20 formations of 100, lines 20 m apart, 600 ticks with contact from about tick 200, captains on): about 5 to 7 ms a tick on average, worst tick 17 to 19 ms (one noisy 43 ms pass on the shared machine), against 25 and 33. Not touched: Godot (the F-keys, order wheel, overlay and bindings are BB-D's; Phase 4's F1 to F4 still send squad shapes until then). Details in the spec's Notes.
   - **P7-AM-A, army book and warbands** (lane C, 1.2, after M-E0 and SC-A). Gate: AM-1, AM-2, AM-14, AM-19.
   - **P7-SG-A, walls and breaches** (lane A, 1.0). Gate: SG-1 to SG-4.
     - **Built (P7-SG-A):** `Sim/Siege/` (`WallDamage`, `WallTable`, `WallWorld`: the one damage rule, per-chunk-level `siege_damage` objects in `ChunkDelta`, rubble by `SetTile`, breaches as rubble rows torn through a wall at least 2 m wide, the walk and parapet over a breach falling by the strict wall-under-it rule with fall wounds, repair as a restoration behind a `pay` callback, built walls through `BuiltCell.Hp` and the Phase 5 collapse path); content `siege/walls.jsonc` (`wall_stat`, `siege_rules`), two ground pieces `rubble_stone`/`rubble_timber`, three keyed chronicle types (`wall_breached`, `wall_fell`, `wall_repaired`) and their `ch_sg_*`/`ch_note_sg_*` strings; one fire hook `FireWorld.PieceBurnt`. Gate: SG-1 to SG-4 green (`Siege/WallTests`, 11 cases with the fire, built-wall, determinism and save-load-continue cases). No section, no version bump, no RNG stream, no coin or goods, no labour. **Shared files touched:** `FireWorld.cs` (the hook), `en.csv`, `chronicle/types.jsonc`, `build/pieces.jsonc` (+2 pieces: the `BC1` piece count pin 155 to 157). No golden moved. Left out: repair labour and materials (economy gate), the strict rule for built walls, the Godot art and the `Rubble` walk speed, runtime wiring; details in `sieges-and-engines.md` Notes.
   - **P7-AE2, purveyance, scutage, finance, forage and sutlers** (lane B, 1.0). Gate: AE-5 to AE-10, AE-14, AE-20.
   - **P7-AM-C, marching and camps** (lane C, 1.3). Gate: AM-7 to AM-10.
   - **P7-BB-D, command UI** (lane A, 0.9). Gate: BB-11, BB-17.
     - **Built (P7-BB-D):** the command layer on the combat view (`FightView`): F1 to F4 select formations 1 to 4 and Shift+F1 to F4 add (the action names `formation_1..4` kept, `formation_add_1..4` new; pressed twice within 0.4 s the camera goes to it), **Phase 4's squad shapes moved to the order wheel's Shape ring** (with no army the squad is formation 1: F1 selects it and the ring carries Phase 4's Line, Shield wall, Loose and Fall back), R holds the order wheel (`UI/OrderWheel.cs`: three rings, mouse or arrows, Tab next ring, release R, Enter or a click issues, Esc cancels, a tooltip with the order's delay and signal read from the sim by `FormationBook.DelayOf`), `0` or Space opens the tactical pause overlay (`UI/TacticalOverlay.cs`: the game paused, a map of the blocks with facing, cohesion, morale, the order in force, queued, on the way (with countdowns) and placed; only what the side sees, ghosts of the rest with their age; LMB selects and drags a move, RMB drags a frontage, clicking an enemy block orders the attack, Shift queues, Ctrl+Z undoes, Delete clears, C toggles the captain, Tab next formation, Space or `0` resumes, Esc closes and stays paused), and the battle HUD (`UI/BattleHud.cs`: both sides' standing and fallen with a morale bar, F1 to F4 cards with shape, men, cohesion, morale and order, the key hint). **Orders reach the sim as recorded fight inputs** (`FightInputKind.FormOrder`, `FormFace`, `FormCaptain` = 26 to 28, `Sim/Battle/FormationCommands.cs`, applied through `FormationBook.Issue` with the hero's Leadership; they survive the replay log), and an order placed in the overlay is drafted (`OrderDraft`) and sent on resume at the paused tick, so its delay runs from resume and a pause gives no free time. Keys: new contexts `Tactical` (26 actions) and `OrderWheel` (7), World and CombatStance gain `formation_add_1..4` and `tactical_overlay` (`0`), CombatStance gains `battle_pause` (Space), overlay pages Tactical and Order wheel; `--check-keymap` 312 actions, 0 conflicts. Gate: **BB-11 met** (`Combat/Battle/CommandTests.cs`: through the hero's input and through the overlay's draft with a scaled 60 s of nothing, arrival after 40 ticks at Leadership 50 either way and the same hash; and live in the smoke: the fight does not step under the overlay and the resumed order is stamped with the paused tick), **BB-17 met** (`--check-keymap`, the self-test's `CommandKeyChecks`, the smoke). The smoke drives the real keys at 960x540 and 1280x720 and fits the HUD, the wheel and the overlay. Screenshot `docs/playtests/p7-bb-d-tactical-overlay-960x540.png` (`-- --fight-demo battle --shot-what overlay`). **Not built here:** wiring `CrowdLayer` into the battle view and the battle bubble itself (BB-C: the overlay draws its own blocks from `BattleSnapshot` over the Phase 4 view), the "Space opens the overlay while a bubble is open" setting and the World-context handling of `0` (no bubble yet: the actions exist, rebindable and listed, the combat view acts on them), "Shoot at..." and the Wedge on the wheel (shown off: the fire-at-target order and the cavalry wedge have no order kind yet), PgUp and PgDn levels (nothing to change in a fight on one level), line of sight, night and weather in what the overlay shows (a flat 300 m rule and ghosts kept by the view). Details in the battle spec's Notes.
   - **P7-SG-B, engines as vehicles** (lane B, 1.4). Gate: SG-5 to SG-7.
     - **Built (P7-SG-B):** `Sim/Siege/` gains `EngineTable` (content `siege/engines.jsonc`: an `engine_stat` row per template and the `engine_rules` row), `Engines` (`Engine` and `EngineBook`: crew, pushers, aim, the shot clock, the wet-hides clock, what burns; Raise, Aim, Advance, Hour), `EngineShots` (the dispersion cone and what a landing hits), `EngineDamage` (damage per part, cover, fire, settling), `EngineMove` (pushing by people), `EngineYard` (building at camp, the bill) and `EngineFight` (the hook into a live fight), with `FightWorld.EngineImpact` (a new partial, the one resolver) and the optional `sieges` section (`Save/SiegeSave.cs`, `WorldState.Engines`; absent while no engine exists, no version bump). Content: sixteen siege parts (`siege_beam`, `siege_axle`, `siege_wheel`, `ram_beam`, `ram_head_iron`, `penthouse_roof`, `hide_cover`, `mantlet_board`, `tower_floor`, `tower_ladder`, `tower_drawbridge`, `trebuchet_frame`, `trebuchet_arm`, `counterweight_box`, `trebuchet_sling`, `springald`, in the designer's existing categories so its palette keeps ten tabs), their sixteen recipes, five templates (`mantlet`, `ram_covered` 3 x 8 and 2.5 t, `siege_tower` 4 x 4 on three decks and 8.8 t with a drawbridge on the top deck, `trebuchet` 6 x 10 and 12.2 t with its counterweight box, `springald`) and the chronicle types `engine_built` and `engine_wrecked` (roots; `wall_breached` gains the notes `ch_note_sg_ram`, `_stone`, `_bolt`). **Building:** `EngineYard` lays a vehicle site at the camp with a crew of hands; each working hour a hand continues his part recipe through the real `CraftRunner` (components drawn from the camp store into the job and used up, the part item owned by the vehicle so it lands on the site's pile), or starts the next part nobody is making, or fits what is on the pile at the worker-hour rule; the owner pays every hand's hour through the ledger. A covered ram took 6 carpenters 3 days (202 worker-hours = 20 worker-days of ten hours, 2.9 t of goods), a tower 56 worker-days (92 parts), a trebuchet 78 (24 parts, 13.0 t of goods of which 5 t is the counterweight's stone); the camp store ends empty and the goods and coin audits balance. **Shooting:** a shot is drawn on stream 0x7C0 (key engine, serial) from the archers' inverse-normal table, sigma 1.5 degrees and range error 5% at Engineering 0, both x (1 - Engineering / 150), so 0.9 degrees and 3% at 60; a 100 kg stone at 45 m/s is 4,500 kg m/s into `WallWorld.Strike` (SG-A's one rule) and into every person within 1.5 m through the one resolver, and breaks the parts of an engine under it from the top of the cell down (a bolt along its level). One step function for both tiers: a trebuchet looses at game seconds 2,400, 4,800 ... (1.5 an hour; a ram swings each 80), the live tier steps it a tick (one game second) at a time and the abstract hour in 3,600, and three days either way give the same shots, wall bytes and book hash (the abstract side rests the engines from 18:00 to 06:00: 18 shots a day). A crew under strength winds slower in proportion, under half not at all. **Engines are targets:** a part takes `(M - 20) x 5` hit points of a blow, passes on what it did not stop; fire lights the top part that burns (iron and the ballast never; wet hides ignition x0.1, dry in 6 game hours) and burns it at 40 hp a minute, the fire going down the cell as each part burns out; boards stop 80% of a ram's crew's missiles while they stand and break under heavy ones; a tower that loses over half its base beams topples; an engine whose working parts are gone is disabled once in the chronicle. **Pushing:** each pusher 300 N through the cart formula (`Stuck.PushN`, `VehicleMath.OnSurface`): a tower 0.25 m/s with thirty on dry level ground, stopped by mud or a slope over 5%; a ram 0.5 m/s with six; a springald carried by six; a broken wheel or axle stops what rolls. Gate: **SG-5, SG-6 and SG-7 green** (`Siege/EngineTests`, `EngineDamageTests`, `EngineFightTests`, 17 cases with the worked example, the tiers' equality and the save, load and continue of an engine mid-shot). No new stream (0x7C0 carries the landing, the fire rolls and the cover rolls under distinct draw ranges), no coin or goods are made. **Shared files touched:** `WorldSave.cs` (the section), `en.csv`, `chronicle/types.jsonc`, `vehicles/parts.jsonc`, `templates.jsonc`, `crafting/recipes_vehicles.jsonc`, and the pins of `VehicleDesignerTests` (66 parts, 20 templates), `VehicleScaleTests` (five rows, engines may be wider than a 2 m track), `HorseTests`, `ContentRemapTests`, `GoldenHashes`. **Golden:** generated-layer and history lines did not move; `phase5_trail` and `phase6_trail` re-pinned for both seeds because the content grew (checked: the same code on the old content gives the old lines). Left out: engines in the abstract siege (the siege book is SG-D's), ladders as items, the tower's drawbridge reaching a wall-walk, the ram at a gate and sallies (SG-C), hoardings and the gate beam, mining, the engines' art (the part and template art tests skip them as the sea parts' do), the missile path of a live fight consulting an engine's boards (`EngineBook.Covered` is the hook), wiring the book and yard into the runtime hour (SG-D), a bolt good for the springald; details and deviations in `sieges-and-engines.md` Notes.
   - **P7-AM-B, levies and the muster** (lane A, 1.0). Gate: AM-3 to AM-6, AM-18.
   - **P7-AM-D, rations and desertion** (lane C, 1.0): the military side of the seam (section 8.2), forage parties, hunger, desertion, after-battle fold-back. Gate: AM-11 to AM-13 (as rewritten in section 8.2).
4. **Step 4, the field, the march and the sea** (months 8.4 to 11.9)
   - **P7-SH-A, ships and decks** (lane B, 1.2). Gate: SH-1, SH-12 (sim side), SH-13.
     - **Built (P7-SH-A):** nine sea parts with recipes (`hull_plank_sea`, `hull_plank_long`, `castle_fore`, `castle_aft`, `fighting_top`, `stem_post`, `boarding_plank`, `ship_ladder`, `sail_square_sea`) and the templates `longship`, `cog` and `war_cog` (SH-1: loaded drafts 0.79 m and 2.19 m, GM far above the tender line, the designer warns `overloaded`); `Sim/Naval/`: `DeckMap` (decks as a `LevelGrid`, ladders as portals, castle parapets), `ShipFrame` (moving sub-grid in Q24 headings and whole-number motion, riders in local mm, `Export`/`Restore`), `Gangways` (3 m plank portals to a ship or the quay), `Swimmers` (overboard, swimming at 0.6/0.9 m/s, sinking over 15 kg, shedding kit by stream 0x7D2, the crawl's breath rule, climbing out, current), `CarrierLayer` + `FightWorld.Carriers.cs` + the `IFighterWalls` interface (riders carried through the transform before the fight's step, the ship's decks as walls, a man past the rail overboard), `NavalHash`; `RideBook.OnSunk` replaces "people to the bank". Gate: SH-1, SH-12 (sim side), SH-13 green (`Naval/ShipDeckTests`, `CarrierBudgetTests`: 100 riders on two ships 60 us a tick). No section, no version bump, no new stream, no coin or goods, no labour. **Shared files touched:** `FightWorld.cs` and `FightWorld.Walls.cs` (three null-guarded hooks and `IFighterWalls`), `PartGrid.cs` (hull gap by tag, the `overloaded` warning), `RideBook.cs`, `RideSprites.cs` (a template with no sprite is drawn from its parts), `en.csv` (+`vd_issue_overloaded`), the vehicle content files, `recipes_vehicles.jsonc`, and the pins of `VehicleDesignerTests` (50 parts, 15 templates), `VehicleScaleTests`, `HorseTests`, `ContentRemapTests`, `GoldenHashes`. **Golden:** generated-layer and history lines did not move; `phase5_trail` and `phase6_trail` re-pinned for both seeds because the content grew (the same code on the old content gives the old lines). Left out: sailing, harbours, ferries and fishing (SH-B), sea edges and raids (SH-C), naval combat and the `naval` section (SH-D), the knarr and the parts `ballast`, `grapple`, `oar_port`, `anchor`, `bilge_pump`, the art and the runtime wiring; details and deviations (the war cog's figures) in `ships-and-naval-war.md` Notes.
   - **P7-CV-A, cavalry formations and the charge** (lane A, 1.0). Gate: CV-1 to CV-5, CV-13.
     - **Built (P7-CV-A):** `Sim/Battle/Cavalry.cs` (a partial of `FormationBook`), content `battle/cavalry.jsonc` (`cavalry_rules`) and the `cavalry_*` and `wedge` rows of `formations.jsonc`; `FormationKind.Cavalry` rows lay out as a line two ranks deep at 1.5 m x 4 m, a wedge (rank r holds 1 + 2r riders, the captain at the point), a loose order or a column of two, with the front rank the best armoured (barding, rider's armour, Riding, skill, id) and the turn rate by gait (30 / 60 / 120 for the line). The anchor runs at 95% of the 20th-percentile gait of the front rank's horses (the mounted layer's own footing, wound and load arithmetic, `FightWorld.GaitMmps`); the riders keep their slots by a feed-forward on the anchor's velocity plus a catch-up that eases to nothing, with a speed cap the mounted movement obeys. A `Charge` order walks beyond 200 m, trots, gallops from 60 m and couches at 20 m; contact is a rider within 3 m of a foe; **shock** (morale only: -15, or -5 braced) when 10 riders in contact within 2 s came in at cohesion 700 or more; after contact a rider above 4 m/s rides on (a rider below it, or slow for 4 s, fights as a Phase 4 rider), the anchor rides 30 m past the contact line then 80 m, wheeling back at its own gallop turn rate, and the formation is whole again about 14 s after contact; "Charge and press" (`Arg` 1) stays in the melee. The cavalry captain (Tactics rows `cavalry_charge` 0, `cavalry_avoid_braced` 30, `cavalry_flank_or_rear` 50) charges archers and routers always, a formation whose refusal he estimates under 400 permille otherwise, and rides to a flank when every way in is braced. No blow, no resolver term and no `ChargeMath` function changed: the rider's blow is the Phase 4 `StrikeAt` the cavalry brain already used, now a shared function. **Refusal, the braced strike, the trample, the brace wish and the pick-target scans of the mounted layer now ask the grid** (the BB-A oracle `UseGrid` gives the same fight hash: `CV5_Schiltron_TheGridIsTheFullScan`), and a new unsorted grid query (`NearUnsorted`) serves scans whose result does not depend on the order. Saved in the formation book's tail (the book's format number is 2 only when a cavalry row exists, so a book with none is byte for byte BB-B's; no version bump, no new stream, no coin or goods, no labour); a save, load and continue mid-charge equals the uninterrupted run. Gate: CV-1 to CV-5 and CV-13 green (`Combat/Battle/CavalryTests.cs`); the mounted layer averages 0.6 ms a tick (best of three passes) with 300 cavalry (600 fighters) in the 2,000-soldier run (see the spec's Notes). **Readings taken:** the spec's "trots to 120 m, gallops from 60 m" leaves 120 to 60 m unsaid: the trot runs on to 60 m; "re-forms 80 m behind its contact line" is read as 80 m beyond it (the riders have ridden through), the 15 s counted from the contact; shock's cohesion is the formation's at the first contact. **Left out:** CV-B (dismount and mount, holders, kicks, horse archers, fallen-horse cells, the `Dismount` and `Mount` orders still only recorded), CV-C (footing per cell, stakes and ditches, weather, fog, branches, fire; the charge reads the Phase 4 footing under it), the chronicle entries `cavalry_charge_broke` and `charge_refused` (the battle entry is BB-C's: the book keeps `Shocks` and the row's charge record for it to read), the feint (Tactics 70) and the lane-clear test of 4.2, and Godot (the Shape ring's Wedge, the formation card's gait line and the cavalry minis are BB-D and BB-E's). Details and deviations in the spec's Notes.
   - **P7-BB-C, the bubble** (lane C, 0.8). Gate: BB-14 to BB-16. **Milestone M-A "the field"** (about month 9.4): a battle of formations commanded with F1 to F4, R and the tactical pause.
     - **Built (P7-BB-C):** `Sim/Battle/BattleBubble.cs` (+ `.Step`, `.Close`, `.Save`), `Sim/Save/BattleSave.cs`, content `battle/bubble.jsonc` (`bubble_rules`). **The bubble owns the battle's `FightWorld`**: `BattleBubble.Open(content, BattleOpening)` takes each side's units (formation kind, shape, drill, soldiers' kits in deploy order, officers, banner), caps the live set (`CapTotal x men / all men`, at least a quarter, never more than the side has; whole formations in deploy order), lays the live units out formed on facing lines (they are "in the bubble already": no pop), makes the box (the live men's extent plus 150 m, clipped to 1,400 m a side and 1 km2 around the hero) and the entry points, and keeps the rest in each side's reserve. `Step` = people entering at the edge (at most 50 a tick over all sides, a unit formed the tick its last man is in), the wave check, the fight's own step, the close check. **Waves:** a side under 70% of its live cap, or with a formation under 70% of its start, sends at most one wave in 5 s of at most a quarter of the battle size (whole formations first, the first cut to the room), jittered along the entry road (stream 0x781); the cap counts everyone not dead, fled or yielded (a man who is down may rise), so no side is ever over it. **Close:** a side with nobody on his feet in the box, none in reserve and none walking in; or no contact for 60 s (20 min if the sides never meet); or a truce; the result lists every soldier and side, the reserve rejoins its host, wounds stay in the world ledger (never copied out), no coin, goods or labour are created or moved (T3). **Ambient chunks:** the 32 m chunks within 200 m of the box that the box does not touch are the paused set; `PeopleBridge.PausedAt` makes live people there hold (jobs kept, nobody folded back). **Chronicle:** `battle_fought` (cause: the campaign, raid or ambush entry, else a root), `battle_hero_led`, `captain_fell`, `banner_taken` (types.jsonc rows, `ch_bt_*` strings). **Saved:** the optional `battle` section (`BattleRecord`: box, caps, sides, reserves and the people entering, the wave clocks, the close bookkeeping, named captains, the AI level-of-detail centre), written only while a bubble is open, next to the `fight` section; **no version bump**. Four things the fight did not keep were found by the save-load-continue comparison and are now kept or derived: the formation steering flag (bit 16384 of a fighter's saved flags), every row's active/routing/total counts (the formation layer counts all rows before any reads another's), each fighter's side kind (set as he joins) and the level-of-detail centre (re-set every 100 ticks, saved). The live tier steps the bubble (`LiveWorld.AttachBattle`), the recorder keeps its start (`SessionRecorder.StartBattle`). **Godot (the live battle view):** `FightView.Bubble` draws the soldiers with the BB-E `CrowdLayer` (the layer is placed by the view's own mapping, so the overlay's blocks, the hero's ring and the gold outlines of the selected formations share one scale; `= / -` and the wheel step the zoom bands Command, Field, Street, Close: new `combatstance.zoom_in/out`, `--check-keymap` 314 actions, 0 conflicts), `BattleHud`'s strip shows up, down, held back and waves, `StreetView.Battle.cs` opens, saves (the `battle` section beside the `fight` one), resumes and closes a battle and writes its chronicle at a barrier (the dev console's `battle [men a side] [battle size]`), and `-- --fight-demo battle --per-side N --cap N --band field|command|street|close` runs one. **Gate: BB-14, BB-15, BB-16 green** (`Combat/Battle/BubbleTests.cs`, `BubbleWorldSaveTests.cs`, `Live/BattlePauseTests.cs`). **Milestone M-A is met** in the sim and in the engine: `MilestoneATests` runs a battle of 1,000 a side in ten formations of 100 a side, the hero's orders going through the recorded inputs and the overlay's draft sent on resume, to its close with the chronicle entries written (average 7.6 to 14 ms a tick on this loaded machine after the JIT's first ticks, the gate is 25 ms), and the smoke drives F1 to F4, R, `0` and the zoom keys on a bubble battle at 960x540 and 1280x720. Screenshot `docs/playtests/p7-bb-c-battle-bubble-1280x720.png`. **Readings and deviations:** the wave check is every second, not every 5 s (so that a wave enters within 5 s of the fall; a side still sends at most one in 5 s); "50 a frame" is 50 a live tick; nothing opens a battle by itself until the armies (AM) and the assault (SG) call `Open` (the dev console and the demo do), and the reference-PC frame budget is still unmeasured (no GPU here). **Left out:** the Phase 4 aftermath for the field's holder (the result carries what it needs), the people of the records becoming fighters through the LODBridge, the game's replay wiring of a battle (`LiveWorld.BattleFactory` is the hook), the Space-opens-overlay setting, horses in a unit's opening, new promotions into paused chunks. Details in the battle spec's Notes.
   - **P7-AW-B, abstract scale and the library** (lane C, 0.7). Gate: AW-7, AW-8.
   - **P7-AM-E, campaigns and the war score** (lane A, 1.3). Gate: AM-15 to AM-17, AM-20. **Milestone M-B "the march"** (about month 10.8; **the month-12 checkpoint**; done-when 2).
   - **P7-SH-B, sailing and harbours** (lane B, 1.1). Gate: SH-2 to SH-8.
   - **P7-AW-A, v2 geometry and passes** (lane C, 1.3). Gate: AW-1 to AW-3, AW-13.
   - **P7-SG-C, the assault** (lane B, 1.0). Gate: SG-8 to SG-12.
   - **P7-SC-B, growth and the wage roll** (lane A, 0.9). Gate: SC-7 to SC-12.
   - **P7-CV-C, ground and weather** (lane C, 0.5). Gate: CV-6, CV-10, CV-11.
     - **Built (P7-CV-C):** `Sim/Combat/Mounted/FootingCells.cs` (a byte per 4 m cell of the battle box, an optional height per cell with an integer bilinear slope along any heading, a fire layer, a build hash, the stake and ditch pieces, `Ground` and `Weathered`), `FightTerrain` +`Cells`, `Wet`, `CavSpeedPm`, `VisionMm`, `SlopeAlongPm`, `ApplyRow` (the zones and base answer where a cell holds no data, so a fight without cells reads exactly as before), `Footing` rows `stakes`, `ditch`, `rubble` (in the code's table of defaults, which `combat/mounted.jsonc` may override) and the `stake_*`, `ditch_fall_pm`, `branch_*`, `fire_radius_mm`, `mud_*`, `ice_*`, `snow_over_cm` tunables (defaults on `MountedTuningDef`; no content file changed, because the abstract wound tables' content hash covers `mounted.jsonc` and a change would have meant rebuilding them: AB1). **Ground:** slope from the cell heights replaces the plane (uphill `1 - 3 x slope`, downhill +10% `v_c`); a stake cell in the 6 m arc adds 250 pm refusal (the roll is made for the nearest of a braced bearer and the first stake cell in the path, `ChargeMath.RefusalPm` +`extraPm`, also read by the cavalry captain's and the mounted brain's estimates); a horse running onto stakes at 4 m/s or more is struck once with `M0` 120 through the resolver (a synthetic oak stake, `Missile`, no skill); a gallop entering a ditch falls with 400 pm (stream 0x473, sub-key 1); woods sweep a rider moving at 4 m/s or more with a branch (50 pm a second, once per 20 s, `M` 40,000, head or upper body, 0x792, `FallBlow`); a horse within 8 m of a burning cell gains fire panic each second. **Weather:** `Ground.From(RegionWeather, cell, hour)` reads the run of hours with rain over 2 mm/h and the sample now; `Weathered` turns field and rough to mud after 3 wet hours, wet ground in frost (-2 C or colder) to ice, snow over 5 cm to snow, and `FightTerrain.Wet` applies it to every cell, zone and base at read time; the weather row's `cavalry_speed_pm` (snow, blizzard x0.6) and `vision_cap_m` (fog 15 m, heavy rain 60 m, a night's 60 m) are read at last: a charge ordered at a target beyond the sight walks until it is seen (no trot, no gallop). Ice now slips a horse at a trot too (it holds a horse to the trot, so the Phase 4 test "above the trot" could never fire). Saved in the mounted tail's two flag slots (bit 0 is the old 0 or 1; the wet ground, the sight and the cavalry speed ride in the higher bits, zero in an older save) and an optional trailing branch tick; the cells are not saved, like zones: the setup re-supplies them and `MountedHash` carries their build hash only when present. No coin or goods, no labour, no new stream, no version bump; the goldens did not move. Gate: CV-6, CV-10, CV-11 green (`Combat/Battle/CavalryGroundTests.cs`); the mounted layer with 300 cavalry (600 fighters) in the 2,000-soldier run over mixed ground averages 0.42 ms a tick (0.35 ms with the cells off; `CavalryGroundBudgetTests`, limit 0.8). **Left out:** the game's chunk adapter that fills the cells from the Phase 1 surfaces and heights (a sampler `FootingCells.Build` takes), the live call that sets `Ground`/`ApplyRow` from the weather at a battle's cell (BB-C's battle box opens it), the horse-fall and drowning rules (CV-B, SH-A), the captain skipping a target it cannot see, stakes planted by archers as a timed build (the piece is a cell set by `PlantStakes`; the 20 s a cell and the goods are the Phase 5 build's), Godot (the footing overlay is BB-D and BB-E's).
5. **Step 5, sieges, the re-fit and the sea at war** (months 11.7 to 15.3)
   - **P7-CV-B, dismounting, kicks, horse archers, obstacles** (lane B, 0.7). Gate: CV-7 to CV-9, CV-12, CV-14.
   - **P7-AW-D, count armies** (lane A, 1.1). Gate: AW-10 to AW-12.
   - **P7-SG-D, blockade and its ends** (lane B, 1.1). Gate: SG-13 to SG-16, SG-19, SG-20. **Milestone M-C "the breach"** (about month 13.5; done-when 3).
   - **P7-AW-C, the re-fit and the ±10% gate** (lane C, 1.0). Gate: AW-4 to AW-6, AW-9.
   - **P7-SH-C, the sea in the continent tick and raids** (lane A, 1.0). Gate: SH-9 to SH-11.
   - **P7-SC-C, mercenary companies** (lane C, 1.0). Gate: SC-13 to SC-15.
   - **P7-AE3, arms industry and loot** (lane B, 0.9). Gate: AE-15 to AE-19, AE-22.
   - **P7-SH-D, naval combat** (lane A, 1.3). Gate: SH-12, SH-14 to SH-16. **Milestone M-D "the beach"** (about month 15.1; done-when 5).
   - **P7-BB-E, rendering** (lane B, 0.9): `CrowdLayer`, the y-sort, Field minis and Command blocks, the frame run. Gate: BB-3, BB-18, the frame budget.
     - **Built (P7-BB-E):** `Sim/Battle/BattleSnapshot.cs` (a read-only struct-of-arrays picture of a fight, captured once per tick into a spare buffer; the sim is otherwise untouched, no golden or save byte moved); Godot `World/CrowdLayer.cs` with `CrowdSprite.gdshader` and `CrowdBlock.gdshader` (one MultiMesh per layer group: lying bodies, standing soldiers, blocks, icons, shape markers; each instance an atlas frame index into a frame table, a palette row and wound flags; a counting y-sort by band pixel row with ties by fighter id; positions snapped to a whole texel; culling to the view; interpolation between the last two snapshots); art `tools/Ravensguard.Art/Sprites/TroopMinis.cs` (30 troop minis 8x16, ten roles x front, back, side, one 8x8 Command icon per role, six team shape markers, a lying body, the team ramp strip `mini.ramps`; baked and committed under `assets/art`, the CI validate and fresh-bake comparison pass); `World/Combat/CrowdBench.cs` (`-- --crowd-bench`, the frame run, and the BB-3 and BB-18 self-test checks). Gate: **BB-18 met** (`BattleSnapshotTests`, and again on the loaded atlas in `--selftest`); **BB-3 met** (`--selftest`: one 300-soldier fight run three times for 120 ticks with the layer drawing it, camera at opposite corners and in three bands, equal hash; the 1,000-soldier size of the gate was not used, to keep the self-test short); **the frame budget is not closed**: this machine has no GPU (llvmpipe under Xvfb, load average about 25), so the 60 fps on the reference PC is unmeasured. What was measured is the layer's own CPU cost for 2,000 soldiers with everything drawn: cull 0.04, sort 0.04 to 0.06, fill 0.11 to 0.14 and upload 0.06 ms, 0.3 ms in all, and the snapshot capture 0.5 to 0.8 ms a tick; the frame times printed by the bench (24 to 31 ms, even with 20 Command blocks) are the software rasteriser and the machine, and mean nothing about the PC. Left: the reference-PC run, the horse and rider minis (CV), the wedge and wagon-fort shapes, selection rings and the tactical overlay (BB-D), wiring the layer into the live battle view (BB-C and BB-D own that screen). Details in the spec's Notes.
   - **P7-SG-E, war wagons and chariots** (lane C, 0.9). Gate: SG-17, SG-18.
6. **Step 6, the war soak** (months 15.3 to 16.1)
   - **P7-AW-E, the war soak** (lane B, 0.8):
     - the harness and `WarSoakGates`;
     - the CI part and the nightly;
     - conversion at the bubble edge in play.
     - Gate: AW-14 to AW-16. **Milestone M-E "the soak"** (about month 16.1; done-when 4).
   - **P7-SC-D, prisoners at scale** (lane C, 0.7). Gate: SC-16 to SC-18.
7. **Step 7, the close-out** (months 16.1 to 17.2)
   - **P7-G1** (0.6):
     - OV-1 to OV-8 (section 4) in `tests/Ravensguard.Sim.Tests/Phase7Overview/` with a shared kit `P7Game.cs`;
     - the budgets of section 9 measured;
     - the reference-PC frame run for 2,000 soldiers (needs the user's machine).
   - **P7-G2** (0.5):
     - the nightly soaks green (war-load on five seeds, the war soak on ten worlds);
     - S9 and S12 of Phase 6;
     - the code and bug check (plan §6) in `docs/reviews/phase-7.md` (folding in `phase-7-audit.md`);
     - a 30-minute fun check: found a works and upgrade a stage; call the levy and meet an army on the road; hold a wall against an assault; catch raiders on the beach; besiege and starve a town;
     - 2 to 3 testers asked "did you understand why?".

**Estimate against plan §5 (16 to 22 months)**, in months of code per part:
- **Step 0:** 0A 0.4, 0B 0.4.
- **Economy (14.2, as the writers gave it):**
  - EA1 1.0, EA2 0.7, EA3 0.8 (EA3a 0.5 and EA3b 0.3);
  - CN1 0.5, CN2 0.3;
  - ES1 0.9, ES2 0.9;
  - LB1 1.2, LB2 1.0;
  - IN1 1.2, IN2 0.9, IN3 0.7, IN4 0.7, IN5 0.3;
  - AE1 1.2, AE2 1.0, AE3 0.9.
- **Military (31.7):**
  - BB-A 1.0, BB-B 1.3, BB-C 0.8, BB-D 0.9, BB-E 0.9;
  - CV-A 1.0, CV-B 0.7, CV-C 0.5;
  - AM-A 1.2, AM-B 1.0, AM-C 1.3, AM-D 1.0, AM-E 1.3;
  - SC-A 1.3, SC-B 0.9, SC-C 1.0, SC-D 0.7;
  - SG-A 1.0, SG-B 1.4, SG-C 1.0, SG-D 1.1, SG-E 0.9;
  - SH-A 1.2, SH-B 1.1, SH-C 1.0, SH-D 1.3;
  - AW-A 1.3, AW-B 0.7, AW-C 1.0, AW-D 1.1, AW-E 0.8.
- **Close-out:** G1 0.6, G2 0.5.

**Serial sum: 47.8 months.**

The critical path:
- **With unlimited helpers:** about 11.8 months. The chain is 0A, ES1, IN1, IN2, IN5 and EA3b to M-E0 (about 4.0), then AM-A, AM-C, AM-D, AM-E, AW-D, AW-E, G1 and G2.
- **With three helpers:** the phase is bound by capacity, not by that chain. The floor is 47.8 / 3 = 15.9 months, and the lane schedule above finishes code in **17.2 months**.

Then add the remaining work:
- **Art** (about 2 helper-months, about 0.7 months of calendar on three lanes):
  - about 10 troop minis in 3 views, rider minis, banner icons;
  - 10 engines in 8 facings, damaged-wall autotiles and rubble;
  - 4 ships deck by deck in 8 facings, waves;
  - about 45 pieces, with the wheels in 4 frames.
- **Integration slack** of about 15% (2.5 months): merges across two halves written by different writers, review fixes, the reference-PC runs.

**Plan on 20 to 21 months.** This is inside the plan's 16 to 22, at the top of the range: about 1.5 months above its midpoint (19), and about 3.5 above the 17 the cumulative column assumes. Phases 3 to 5 also landed near the top.

The estimate holds only with three helpers busy for the whole phase:
- with two helpers the same lanes need about 25 months of code (over the plan);
- with four, about 13.

The overrun rule (plan §7: more than 50% over) triggers at **33 months**. The checkpoints:
- M-E0 by month 6;
- M-B "the march" by month 12.

**Cut order if late (overrun rule).** Nothing below was decided with the user; each is a sub-feature or a carry-over, and its spec section stays as the after-1.0 design.
1. Horse kicks and bites, and branches in woods (Phase 4 carry-overs; CV 4.6, part of 4.8).
2. Horse archers and mounted crossbowmen (CV 4.7).
3. Windmills (LB 4.2; water mills stay).
4. Panning for gold (coinage 5.6; gold mining stays).
5. Mercenary companies changing sides and plundering on the way out (SC 4.6; hiring, pay, loyalty and leaving stay).
6. Ferries and fishing boats (SH 4.3; harbours, drowning and sea trade stay).
7. Lee shores and wrecks on rocks (SH 4.2; capsizing in beam seas and storms stays).
8. The arms export ban and NPC upgrades of works beyond L1 (AE 4.10, IN 5.12).
9. Runner orders and queues beyond one order (BB 4.5; voice and horn stay).
10. The gold book beyond treasuries, chests, hoards and the hero's purse, and the changers' NPC policy (coinage 5.2, 5.4; the three coins, the mints and the copper cap stay).
11. Budget items, only if their budgets still pass: the template library's hash-consing (accept 0.9 MB) and the counting y-sort (keep the spike's sort).

**Never cut:**
- **The audit and its fixes:** step 0's audit and the war-load soak.
- **The economy decided with the user:**
  - economies of scale for every business, and the plan's list of large buildings;
  - the three staged chains with tiers 1 to 3, the Industry tab, the industry slider, real inputs and the payback target;
  - gold, silver and copper with working mints.
- **Soldiers and supply decided with the user:**
  - hybrid armies (warbands all year and seasonal hosts);
  - loadout templates and per-soldier refit, no XP, promotions;
  - levies by wealth, and the armoury for retinues and mercenaries;
  - armouries stockpiling and munition production, the arms trade and loot recycling;
  - army food from granaries, food rents, purveyance, sutlers and foraging.
- **War in play, from plan §5 Phase 7:**
  - the 2,000-soldier battle bubble with formations, orders, the tactical pause and reinforcement waves;
  - cavalry formations and charges;
  - columns, camps, levies, mercenaries, purveyance, scutage and campaign finance;
  - sieges with engines from the designer, wall-walks, towers, breaches, starvation and winter sieges;
  - war wagons and wagon forts, chariots;
  - harbours, cogs and longships sailing by wind, sea trade and sea raids, naval combat with missiles, ramming, fire and boarding;
  - AI campaigns.
- **The proving gates:** the abstract resolver within ±10%, and the war soak and its consequences.
- **The rules everything keeps:** determinism, and conservation of coin, mass and people.

## 4. Done-when mapped to specs and tests

| Done-when (plan §5) | Specs | Proving tests | Integration |
|---|---|---|---|
| **2,000 soldiers hold 60 fps on the reference PC** | battle 4.2, 4.10, 10; cavalry 11 | BB-5, CV-13, BB-3, BB-18 | **OV-1:** 2,000 soldiers and 300 horses, 40% in contact, every zoom band, 6,000 ticks on the reference PC: tick average <= 25 ms, worst tick a minute <= 33 ms, frame p99 <= 16.6 ms, 0 allocations a tick. If it misses, the default battle size drops in steps of 250 (BB 10) and the result is recorded. **Needs the user's machine** |
| **You can lead levies against an army arriving by road and see the consequences** | armies 4.2 to 4.13, army-economy 3, 4, soldiers 4.2 to 4.5, battle 4 | AM-20, AM-3, AM-12, AE-3, AE-5, AE-10, SC-2, BB-10 | **OV-2:** seed 1234; the hero, a baron, calls his levy (60 men; the armoury fills spears and shields, gaps shown, one soldier refitted by hand) and his warband against an enemy host of 400 coming by road. Scouts see the column, the host deploys from column, and the battle bubble is fought with F1 to F4, R and the pause. Afterwards: foraged villages with `army_foraged`, purveyance tallies, levy dead in their households, the war score, desertion after the defeat. Every entry has a specific cause; coin, mass and people are exact |
| **An AI faction besieges and breaches your walls** | sieges 4.1 to 4.6, army-economy 4.8, armies 4.10 | SG-20, SG-14, AE-11 | **OV-3:** an AI host of 1,200 invests the hero's walled town and builds two trebuchets from timber it buys or fells (price entries with the host as cause). It breaches in about 3 days while the town's days of food fall, offers terms, and assaults through the breach in a battle bubble over the walk's levels. Save and load mid-assault is byte-identical. One chronicle chain from `siege_begun` |
| **War soak: 25 years, several realms, abstract armies, levies, supply, foraging, sieges; dearer bread on march routes, unrest where purveyance was used, refugees, depopulated borders; no collapse; coin and mass conserved; recovery within about 15 years; every war's end traced** | abstract war 4.4, 4.6; economy audit 5 | AW-15, AW-16, EA-15 | **OV-4:** the war-load soak (EA-15, five seeds) gates M-E0; the war soak (AW-16, ten worlds, nightly) gates the close-out with every gate of AW 4.6 by name; CI runs AW-15 (seed 1234, 2 years, coin, mass and people exact, the Linux and Windows trail equal) |
| **Raiders land from a ship and you fight them on the beach and on the deck** | ships 4.4 to 4.7, armies 4.1 | SH-11, SH-12, SH-13 | **OV-5:** in the running game two longships beach at dawn; 70 raiders raid a village 1.5 km inland; the bell brings the warband and the hero; there is a battle bubble on the beach; the hero boards a longship pushing off; a man in mail goes over the side and drowns by the breath rule |
| Step 0, the economy audit (decided with the user) | economy audit | EA-1 to EA-16 | the findings file; M-E0 signed off (Q15) |
| Industries (decided with the user) | industries, scale, large buildings | IN-9 to IN-11, IN-20, IN-24, ES-16, LB-16 | **OV-6:** headless with the bot: the hero founds an L1 iron works near Ranunfield, and the Industry tab shows the bottleneck. A commissioned builder upgrades the smeltery to L2, and the hero sends his own caravan with its margin quote. Payback is 100 to 200 days at 1x and shortens at 3x; coin and mass are exact |
| Gold, silver and copper (decided with the user) | coinage | CN-1, CN-2, CN-13, CN-14, CN-16 | **OV-7:** a Phase 6 save loads with the coin notice and re-saves byte-identical. After a season, the mint has struck silver the hero delivered (seigniorage paid), copper has been struck to the cap, and a pay chest has been changed to gold. Every price shows as gp, sp and cp, and `sum(opted) + loose = struck - melted` holds every day |
| Determinism and saves | all | every spec's determinism and save test | **OV-8:** a 60-day scripted war trail with a works, a muster with purveyance, a march, a night attack on a camp, a live battle saved mid-battle, a siege with a breach, a sea raid and a mercenary contract: save, load and save byte-identical at six points; `phase7_trail` equal on Linux and Windows; `phase5_trail` and `phase6_trail` unchanged |

## 5. RNG streams (the single table)

Phase 6 owns 0x600..0x6FF. **Phase 7 uses 0x700..0x7FF:**
- the economy specs 0x700..0x77F;
- the military specs 0x780..0x7EF, 16 each;
- **0x7F0..0x7FF unassigned** (reserve).

All are registered in `Core/RngStreams.cs` with the uniqueness test (no id outside its phase's block, except the reuses listed). Every key is game time, distance or an id.

The Phase 6 reservation **0x660** (purveyance) is now drawn. Its "asserted unused" test is rewritten to "unused before a purveyance".

| Stream | Spec | Use | Key |
|---|---|---|---|
| 0x700 / 0x701 | economy audit | `WarLoad` muster jitter and forage ties; the exploit bot and X11's moves (**never drawn by a menu game**) | war, season; bot, day |
| 0x710 | coinage | reserved for coining and clipping crimes (Phase 8); asserted never drawn | - |
| 0x720 / 0x721 | economies of scale | an apprentice or hand spoiling a batch; guild inspection | business, station, batch; guild, business, season |
| 0x730 / 0x731 | large buildings | machine and wheel breakdowns; flood damage | piece, day; wheel piece, day |
| 0x740 / 0x741 / 0x742 / 0x743 | industries | stage accidents; ore yield per batch; kiln losses; own-caravan ties | stage, day; ore body, batch; kiln, firing; caravan, day |
| 0x750 / 0x751 / 0x752 / 0x753 / 0x754 | army economy | forage yield (the one forage rule, section 8.2); sutlers joining; loot condition; a vassal's serve or pay; arms caravans | tile, day; host, day; item, battle; vassal, season; realm pair, season |
| 0x780 / 0x781 / 0x782 / 0x783 | battle bubble | captain ties; a wave's entry jitter; rank-closing hesitation; a runner's route | battle, formation, tick/40; battle, side, wave; battle, fighter, slot change; battle, order |
| 0x790 / 0x791 / 0x792 / 0x793 | cavalry | fall or jump over a fallen horse; kick and bite target; branches; horse-holder ties | battle, rider, obstacle; battle, horse, tick; battle, rider, second; battle, formation |
| 0x7A0 / 0x7A1 / 0x7A2 / 0x7A3 / 0x7A4 / 0x7A5 / 0x7A6 | armies | desertion; deserter to bandit and drift home; warband task and raid ties; persons made for a host from outside; a scout caught; campaign target ties; camp pitch jitter | person, day; person, day / band, season; warband, day; host, index; scout, day; realm, season; host, camp |
| 0x7B0 / 0x7B1 / 0x7B2 / 0x7B3 / 0x7B4 | soldiers | company loyalty; company seeding; changing sides; prisoners killed by a cruel captor; parole broken | company, day; region, index; company, war; battle, prisoner; person, season |
| 0x7C0 / 0x7C1 / 0x7C2 / 0x7C3 / 0x7C4 / 0x7C5 | sieges | an engine shot's landing; a ladder pushed off; a missile from below meeting a merlon; sack fires, deaths, captives; a runner slipping the blockade; siege AI ties | engine, shot; ladder, try; battle, projectile; siege, house; siege, person, night; siege, day |
| 0x7D0 / 0x7D1 / 0x7D2 / 0x7D3 / 0x7D4 / 0x7D5 | ships | capsize in a beam sea; wreck damage; shedding kit under water; sea raid ties and size; persons for an arriving crew; a grapple's catch | ship, 20 s step; ship, cell; person, second; realm, season; ship, index; ship pair, throw |
| 0x7E0 / 0x7E1 / 0x7E2 / 0x7E3 / 0x7E4 | abstract war | v2 block placement and reserve ties; trample and fall in a pass; count-battle rounding; count-host forage and siege rounding; the war soak's world variation (**never drawn by a menu game**) | battle, side; battle, rider, step; battle, step, class; host, day; world, year |

**Reused, unchanged:**
- 0x660 (purveyance hiding, now drawn), 0x663 (lender ties for campaign loans), 0x682 (a levy's evaded draft) from Phase 6;
- 0x690 (company ties in contracts);
- 0x417 (stumble, doubled on a heaving deck), 0x424 (wake), 0x454 (escape window), 0x456 (ransom);
- 0x470 to 0x473 (lance shatter, refusal, fall parts, slips), 0x4A0 to 0x4A3 (abstract engagement, wound sample, rout, leader);
- Phase 3's encounter streams.

**Free for growth:**
- 0x702..0x70F, 0x711..0x71F, 0x722..0x72F, 0x732..0x73F, 0x744..0x74F, 0x755..0x77F;
- 0x784..0x78F, 0x794..0x79F, 0x7A7..0x7AF, 0x7B5..0x7BF, 0x7C6..0x7CF, 0x7D6..0x7DF, 0x7E5..0x7EF;
- 0x7F0..0x7FF.

## 6. Control keys (one consolidated table)

Source of truth: `src/Ravensguard.Game/Input/InputDefs.cs` at ac87253. It has 14 contexts:
- Global, World, CombatStance, CalledShot, Build, Menu, BlueprintEditor, Trade, Mounted, Choice;
- VehicleDesigner, Ledger, Treatment, Faction.

What World has bound today:
- every letter but O;
- the digits 1 to 4;
- F1 to F10, with Shift+F6 and Shift+F7;
- Space as pause.

In World, **`0`** and **Shift+F1 to F4** are free (Choice takes 1 to 9 only, never 0). Phase 7 adds those two World keys and **three contexts**: `Tactical`, `OrderWheel` and `Armoury`. Everything else reuses existing contexts and Q-menu verbs.

| Context | Keys | Origin |
|---|---|---|
| World, CombatStance | **F1 to F4** select formations 1 to 4 (the action names `formation_1..4` are kept, but their meaning changes from Phase 4's squad shapes: Q9); **Shift+F1 to F4** add to the selection (new); pressing an F-key twice within 0.4 s centres on it; R holds the order wheel (unchanged key); **`0`** opens the tactical pause overlay (new); Space (pause) opens the overlay while a battle bubble is open if the setting "battle pause opens the overlay" is on (default on); E dismounts or mounts the hero (Phase 4) | Phase 0 keys, Phase 7 meaning |
| **Tactical** (new, modal, alone with Global) | LMB select or drag to move; RMB drag a frontage; Shift queue; F1 to F4 and Shift+F1 to F4 select; Tab next formation; R order wheel; C captain commands; Ctrl+Z undo the last placement; Delete clear the queue; PgUp and PgDn change level (wall-walks, decks); Space or `0` resume; Esc closes the overlay (stays paused) | new |
| **OrderWheel** (new, modal while R is held) | mouse direction or the arrows pick a sector; Tab next ring (Orders, Shape, Missiles); release R or Enter to issue; Esc to cancel. Ships' crew orders and Charge "and rally" or "and press" (Tab) are sectors, not keys | new |
| **Armoury** (new, modal, alone with Global) | Up and Down soldiers; Left and Right slot; Tab pane; Enter assign or open Refit; **F** fill all; **G** gaps only; **T** edit the template; `/` search; Esc back | new |
| Ledger (the Industry tab, the Fief tab's additions) | the Ledger's own (arrows, Tab next tab, Enter open or act, `/` search, S sort, A alerts, Esc back); sliders on Left and Right | Phase 5 |
| Mounted (at the helm) | W more sail or oars, S less, A and D steer (the cart driver's keys) | Phase 5 |
| Build | the water overlay is a **button on the overlay bar** with no key (decided here; `--check-keymap` keeps 0 conflicts) | - |
| Squad (P) panel's Army tab, the siege panel, the campaign panel, the mint and changer panels | the Ledger's keys; no new key | - |

**New verbs without keys (Q menu and E):**
- **Coin and industry:** Sell bullion, Change money, Offer a supply contract, Upgrade a stage, Send a caravan.
- **Armies:** Join the host, Hail the column, Trade with the sutlers, Report to the commander, Desert.
- **Soldiers:** Enlist, Offer a role, Dub, Hire this company, Pay the company, Take prisoner, Parole, Ransom.
- **Sieges:** Aim, Crew with...
- **Ships:** Book passage, Hire the ship, Load, Unload, Buy the ship.

The `?` overlay gains Tactical, Order wheel and Armoury pages built from `InputDefs`. `--check-keymap` must stay at 0 conflicts (BB-17).

## 7. Saves

**No version bump: v8 stays.** No stored meaning changes: amounts stay in mites (section 8.3) and every enum grows append-only.

Phase 7 state lives in **ten new optional sections**, absent while empty, so a world that never used them keeps its bytes:
- economy: `coinage`, `scale`, `works`, `industry`, `warsupply`;
- military: `battle` (only while a bubble is open), `armies` (count hosts included), `soldiers`, `sieges`, `naval`.

Cavalry adds no section: additive keys go in the Phase 4 `mounts` record (horse holder, charge stage, the footing cells' build hash). Abstract battles are transient.

**Additive keys:**
- politics `War` rows (+`Events`, +`HarvestsKept`);
- the fields' `Foraged` flag (an append to `FieldFlags`);
- vehicles and animals with a host as owner;
- construction pieces (the sluice's state);
- contracts (the `mercenary_service` terms) and followers (`Role`);
- the fiefs section's office rows (`MintMaster`);
- chronicle parameters (the `money` kind, `blocks_a` and `blocks_b`);
- the header's difficulty array (a seventh value; a six-value array reads as `IndustryOutputPct` 100).

**Content added by Phase 7** is mapped by name by the load-time `ContentRemap`, and the load report lists it:
- goods, pieces, parts and templates;
- room types, business and stage kinds, recipes;
- the rubble tile codes;
- the migration `water_sawmill -> saw_mill`.

The coinage notice is informational and never asks for confirmation.

**The Phase 7 seeding step** is appended to the Phase 6 seeding pass's list. It runs at a new game, and at the first season of a save that has no Phase 7 sections. It is keyed on the world seed and ids, never the day it runs. It covers:
- the mint per realm and its `MintMaster` vacancy, filled by the rule's vacancy pass once the runtime attaches;
- a money changer in towns of 1,500 or more;
- an `armoury_keep` and a `granary_depot` at each realm seat, stocked by AE 4.6 and 4.10;
- each lord's warband from his household and hired men;
- 2 to 4 mercenary companies a region (0x7B1);
- harbours at coastal towns.

**New civic buildings in old worlds.** Library worlds' chunks were generated without these buildings: harbours, mint houses, armouries and depots. Two routes, as history v0 and v1 did:
- **Old worlds:** the seeding step places these buildings as finished construction, on free plots fitted by `PlanFitter` and keyed by settlement id.
- **New worlds:** they come from the civic plans under a new placer version. Library worlds stay on the old version.

**Round trips:** OV-8, plus each spec's own (BB-14, AM-19, SC-18, SG-19, SH-16, CN-2, CN-14, ES-16, LB-16, IN-24, AE-22, EA-12, WL-13). `WorldRuntimeResumeTests` gains a ninth case holding every Phase 7 section.

**Hash trail:**
- **Economy:** `CoinHash`, `ScaleHash`, `WorksHash`, `IndustryHash`, `SupplyHash`.
- **Military:** `ArmyHash`, `SoldierHash`, `SiegeHash`, `NavalHash`.
- **Battles:** the battle's fight hash.

Each joins `phase7_trail` only once non-empty. The live trail holds only what the live tier writes (Phase 5 S1's lesson): fighters, formations, columns and ships in the bubble.

## 8. Cross-spec notes (resolved here)

### 8.1 Shared enums (append only, in this order; checked against the code at ac87253)

| Enum (file) | Today ends with | Phase 7 appends | Notes |
|---|---|---|---|
| `TransferReason` (`Economy/LedgerTypes.cs`) | ... `Penance, RelicSale` | `Exchange, Purveyance, Scutage, Plunder` | **`Plunder` is not in the enum**, but a Phase 4 alias `AftermathCoin.Plunder` maps robbed coin to `Fine`. P7-0B repoints the alias to the new value. `Wage` (army pay), `Tribute`, `LordRansom`, `ContractEscrow` and `ContractPay` (mercenaries), `Loan` and `Interest` (campaign loans), `Restitution` (supply contract breaches), `Seigniorage` and `Fine` already exist and are reused. Saves pad the per-reason count as for `Alms` |
| `SourceReason`, `SinkReason` | `Seed, Mint, Immigrate`; `Melt, Burn, Emigrate` | none | `Mint` covers all three metals (the metal is in `CoinStock`); `Burn` stays test-only (EA-8) |
| `DestroyReason` (`Goods/MassLedger.cs`) | ... `Materialised, Other` | `Waste, Trampled` | tailings and slag; forage trampling. `Materialised` is reused for metal struck into coin |
| `CauseKind` (`Core/Chronicle.cs`) | ... `Office, Illness` | `Industry, Supply, Campaign, Desertion, Siege, Naval` | `War`, `Raid`, `Battle`, `Famine`, `Weather`, `Trade` already exist and are used where the specs say so |
| `Occupation` (`People/Types.cs`) | ... `Physician, Monk` | `Soldier, Bandit, Sailor` | `Guard` exists and stays the town watch |
| `Skill` (`People/Types.cs`) | ... `Prospecting` | `Seamanship, Engineering` | **`Engineering` is found here.** SG 4.2's dispersion reads an "Engineer skill" that does not exist. Decided: append `Engineering` (trains by engine shots and engine-building hours). The `engineer` role keeps SC's Carpentry 30 to build, and aiming reads Engineering. Absent skills read 0, so no migration |
| `FAct` (`Combat/Melee2/FightTypes.cs`) | ... `Drawing, Aiming` | `Asleep` | |
| `Formation` (`Combat/Watch/SquadTactics.cs`) | `Auto, Line, ShieldWall, Loose, FallBack` | `Column, Wedge, Schiltron, WagonFort` | `FormationKind` and `OrderKind` are new enums in `Sim/Battle/` |
| `Footing` (`Combat/Mounted/TerrainFooting.cs`) | ... `DeepWater, Portal` | `Stakes, Ditch, Rubble` | |
| `LedgerTab` (`Production/OwnershipViews.cs`) | ... `Fief` | `Industry` | the Army tab lives in the Squad (P) panel, not the Ledger |
| `DueKind` (`Rule/RuleBook.cs`) | ... `Rent, Labour` | `Scutage` | `Service` exists |
| `OfficeKind` (`Rule/RuleBook.cs`) | ... `Gaoler, Treasurer` | `MintMaster` | |
| `HeroCommandKind` (`Trade/HeroDesk.cs`) | ... `Bondage, Unrest` | `Industry` (`in:`), `Coin` (`cn:`), `WarSupply` (`ws:`: policy, warrant, serve or pay), `Army` (`ar:`, armies and sieges), `Soldier` (`so:`), `Naval` (`nv:`) | every new hero command is recorded (the Phase 5 S1 and S4 replay lessons) |
| `Difficulty` (`Core/Difficulty.cs`) | six values: wound, bandit, economy, plague, ironman, pace | the seventh, `IndustryOutputPct` 100 to 500 in steps of 100 | `FromArray` today returns the default when given fewer than `ValueCount` values: keep 6 as the minimum and read a missing seventh as 100 (DS test with a six-value array) |
| Unrest content rows | `purveyance`, `tallage` ... | `foraged` (+4 for 60 days), `levy_death` (+4 for 60 days), `sacked` (+10 for a year); `UnrestConcede` gains a `siege` row | content, not an enum |

**Checked and not duplicated:** no `Plunder`, `Exchange`, `Purveyance` or `Scutage` transfer reason exists; no `Soldier`, `Bandit` or `Sailor` occupation; no `Asleep` act; no `Industry` ledger tab; no `Scutage` due kind. No `PersonStatus` is added, and `DeathCause` is unchanged: drowning is `Suffocation`, with the sinking as the cause chain.

### 8.2 The economy ↔ military seam

`army-economy.md` §3 is the interface; **its names win**. `armies-on-the-map.md` §4.7's working names map as follows:

| Military working name (AM 4.7) | Final call (AE 3) | Notes |
|---|---|---|
| `Provision.Plan(host, days)` | `HostSupply.Needs(h, day)` (pure), `HostSupply.Budget(realm, plan, days)`, `DaysFunded(realm)` | the sources are not planned per call: they are the ladder (AE 4.1) under the host's `SupplyPolicy`, set at `Register` and changed by `Update` |
| `Provision.Draw(host, good, kg, node)` | `HostSupply.Supply(h, day)` | one daily call runs the whole ladder (baggage, food rents, depots and convoys, market, sutlers, purveyance, forage) from the host's `HostPlace`; it returns `DayResult` with each source's grams and the chronicle ids |
| `Provision.Sutlers(host)` | ladder step 5 inside `Supply`, plus the camp market book (AE 4.7) | no separate call |
| `Provision.PayRoll(host, rows)` | pay inside `Supply`, **daily**, from the pay chest (`DayResult.Paid`, `Arrears`, `UnpaidDays` by role); `Disband` settles | pay order: mercenary companies (one line each, to the company purse), knights and men-at-arms, the rest; **within a role, ascending person id**, so the military derives each `ServiceRow`'s paid and arrears exactly from the role totals. A company pays its own men each 6-day week, less the captain's 10% (SC-13 kept) |
| `Provision.Kit(armoury, request)` | `HostSupply.Kit(h, template, count)` and `ReturnKit(h, items)`, over the armoury's `Issue` and `Return` (LB 4.4) | the filling rule is SC 4.2's `ArmouryFill` (the military owns templates and the rule; the economy owns the store) |
| (the muster in AM 4.2, 4.3) | `HostSupply.Muster(h, fief, men)`, `Release`, `Desert` | **the draw rule is AM 4.2's**: groups of ten (five in war), the most-manned household, the man aged 16 to 45 with the best weapon skill, never a household's only worker if another can give, evasion by `Evades`. AE 3.3's "16 to 50, head last" is replaced. The kit a levy brings is SC 4.3's |
| (camps in AM 4.5) | `HostSupply.Camp(h, tile)` | the one call that feeds `ArmyCampSize`; the military passes the water source in `HostPlace` so the same call feeds `UnboiledWaterDays` |
| (the harvest cost in AM 4.2, 4.10) | `HostSupply.HarvestRisk(fief, men, days)` | one function; `CampaignBrain` calls it (AM's own formula is replaced) |
| (blockade in SG 4.6) | `HostSupply.SiegeBlockade(settlement, on, besieger)`, `DaysOfFood(settlement)` | SG's surrender test reads `DaysOfFood` |
| (loot in AM 4.9, SG 4.6) | `HostSupply.Loot(battle, holder, items)`; a sack's coin to soldiers' purses by `TransferReason.Plunder` | |

**One rule each, where both writers wrote one:**
- **File names.** `Sim/WarSupply/HostSupply` and `Sim/WarSupply/Forage` are the economy's. The armies spec's `Sim/War/HostSupply` becomes **`Sim/War/Rations`** (hunger, relish morale, the effects of `DayResult`). Its `Sim/War/Forage` becomes **`Sim/War/ForageParty`** (who goes, and the live foragers in a bubble).
- **Rations.** `HostSupply.Needs` is the one rule:
  - staple 1,000 g a man, followers 800 g;
  - relish 150 g (AM's morale +5 when both are fed is kept, read from `Delivered`);
  - firewood 1,500 g a person, doubled in winter (SG 4.7's 3 kg agrees);
  - horses and oxen at their `animals/species.jsonc` rows. A destrier row is added if AM's 10 kg of hay and 2 kg of oats differ from the courser's.
  - AM-11's expected numbers are recomputed from these rows.
- **Forage.** AE 4.2 is the one rule:
  - foragers are 10% of the men;
  - each brings 30 kg of staple-equivalent a day (x0.8 to 1.2, stream 0x750), within **4 km**;
  - stores come first, then standing crops: half taken, half `Trampled`.
  - **Added from AM 4.6:** the strip limit (the same disc for 3 days yields a third) and livestock on enemy land (1 head of cattle or 4 sheep a forager-day, into the host's animal rows).
  - AM's 300 kg and 150 kg rates and its 3 km radius are dropped. AM-12 is rewritten to 100 foragers x 30 kg.
  - **Own land:** with the ruler's warrant (`SupplyPolicy.Purveyance`), foraging is purveyance paid by tallies; without one it is theft by the soldiers (AM Q3).
- **Timing.** `HostSupply` runs at the **host slot**: hour 18, in host id order, after the day's H5 globals. The military's daily step (`ArmiesDaily`, section 8.4) runs right after it and reads that day's `DayResult`. It covers hunger, morale, the desertion roll (AM 4.8 with `UnpaidDays` as arrears days) and service days. AM's "eat and roll desertion at H5" is moved there. `WarLoad`'s stand-in desertion is retired when P7-AM-D lands.
- **Wages: one table,** `war/troops.jsonc` (SC 3.1), **in mites**. AE's `war/pay.jsonc` is dropped:

  | Role | Wage a day (mites) |
  |---|---|
  | levy | 0 within the owed days, then 150 |
  | spearman | 250 (the Phase 2 soldier wage) |
  | archer | 250 |
  | crossbowman | 300 |
  | sergeant | 500 |
  | man-at-arms | 750 |
  | knight | 1,500 |
  | engineer | 400 |
  | sailor | 250 |
  | marine | 300 |
  | mercenary | x1.5 the role |
  | captain and quartermaster | allowances of +100 on their role's wage |

  - **Why SC's table:** it keeps the historical ratios and Phase 4's ransom ranks, and AE's spearman at 200 sat below the Phase 2 soldier wage.
  - **What this changes:** AE 4.5's example roster costs 370,000 mites a day in pay instead of 261,000. With staple and fodder the total is about 500 gp a day, so a 5,000 gp treasury funds about 10 days. AE-2, AE-13 and AE-14 are re-derived by P7-AE1; WL-8 measures the effect.
- **Who arms whom.** SC 4.3 decides it:
  - poor, middling and well-off households bring the listed kinds;
  - the lord's default fill is spears and shields;
  - mercenaries bring their own kit and have their gaps filled (Q10).
  
  AE 4.10's `armed_levy_wealth_mites` (20,000) becomes the middling floor. The armoury's `kit_target` counts the levy's gap in spears and shields only.
- **Owed service.** One number, `war_service_days`, serves both purposes:
  - the levy's unpaid days (AM's 10, AE's `levy_free_days` 40);
  - the vassal's war service that scutage prices (AE's 40).
  
  It is Q1 (default 10). Scutage stays `men x days x 300`.
- **Chronicle types deduplicated:**
  - **Levy:** `levy_drawn` (AM, `ch_am_levy`) stays and AE's `levy_mustered` is dropped.
  - **Forage:** `army_foraged` (AE, one per settlement and day) stays, with a `stripped` parameter for "more than half its stores"; AM's `village_foraged` is dropped.
  - **Siege:** `siege_begun` (SG) is the siege's root and carries the days of food. AE's `siege_blockade` is dropped and `siege_starving` (Famine) stays.
  - **Depot:** `depot_filled` is defined once (AE 8), and LB 7 refers to it.
  - **Battles:** live battles write `battle_fought`; v2 and count battles write the existing `battle_report`.
- **Bandits.** Bandit bands are the military's records (AM 3.4) of `Occupation.Bandit` people. AE's deserters "become bandits" through them.
- **Who serves or pays.** Scutage's serve-or-pay choice (AE 4.4, 0x753) is made when the muster calls `RuleDues.CallService`. A vassal who serves sends his men through `Muster`.

### 8.3 Money

- **Sums are already stored in mites.** One mite is a tenth of a copper.
  - The plan's §4A.1 line ("sums are then stored in copper, and older saves are converted at load (x10)") assumed whole crowns were stored. They never were: the old crown is 100 mites, which reads as 1 sp = 10 cp with **no conversion**.
  - Coinage's option A keeps every stored number, so a Phase 6 save re-saves byte-identical (CN-2).
  - The plan's line should be amended at the close-out to say so. Q13 asks the user to confirm.
- **No mint is wired today** (`MintMath.StrikeInto` has no caller). The money supply grows only by border trade. P7-CN1 wires the mints; the audit's F-4 sets deposit grades so mining grows the supply 1 to 2% a year.
- **The military specs wrote money in copper.** Every stored field becomes mites (`WageMites`, `ArrearsMites`, `ValueMites`, ...) and every cp figure is multiplied by 10. The affected items:
  - warbands' `ArrearsCp`;
  - `ServiceRow.WageCp` and `ArrearsCp`;
  - `SoldierRow.WageCp`;
  - `PrisonerRow.Value`;
  - the levy wage of 15 cp (150 mites);
  - the campaign score's 2,000 cp a captured lord (20,000);
  - the ransom classes (levy 1,300, man-at-arms 4,000, knight 20,000, baron 100,000, as Phase 4's `captivity.jsonc` already holds).
  
  Display is through `MoneyText` only.

### 8.4 Tick slots (`SystemOrder`, new constants)
| Slot | Value | Phase | What |
|---|---|---|---|
| `Armies` | 1,100 | H1 hourly, after caravans | hosts, warbands and bands move on the graph; scouts; encounters; warband task choice (one lord an hour by fief id, as `S10Politics` slices) |
| `Ships` | 1,120 | H1 | ships at sea move; harbour arrivals and departures; raids sail |
| `Sieges` | 1,140 | H1 | engines' abstract shots and wall damage; engine building as jobs |
| `HostSupply` | 1,150 | H1, acts only at hour 18 | the host slot (AE 3.5): `Supply` for every host in id order; `AbstractHostSupply` for abstract ones |
| `ArmiesDaily` | 1,160 | H1, acts only at hour 18 | hunger and morale from `DayResult`, desertion (0x7A0), owed days, mercenary loyalty (one company an hour of the day) |
| `MintDaily` | 5,210 | H5, after taxes | the mint's queue struck; the copper cap |
| `SoldiersDaily` | 5,230 | H5 | drill hours credited, prisoners fed, ransom terms halving |
| `SiegesDaily` | 5,240 | H5 | the besieger's and the defender's choices, the surrender test, repairs, the blockade's market cut |
| `S11War` | 6,970 | H6, after `S10Politics` | rulers' campaigns, musters and summons; the war score's season (`StepWar`); winter quarters at doy 46; promotions offered |

The industry stage day and scale's absorption run in Phase 2's daily business slice (`ProductionDaily`, H4); the wheels' watts once a day there. Purveyance tallies and arms orders run at S5 (`S5Businesses`). Nothing new runs at hour 0 of a season beyond counters (the Phase 6 rule). The continent tick (seasonal, worker) gains count hosts, sea trade and raids.

### 8.5 Other notes
1. **The hero's id is 0.** Every new person-keyed record must never read 0 or `EntityId.None` as "nobody". This covers `ServiceRow`, `SoldierRow`, `PrisonerRow`, `Host.Commander`, `ShipRow.Master`, a works' owner and intel's observer. Each spec's suite gets one "the hero as X" test (Phase 6 S6 to S8's lesson).
2. **One resolver.** Charges, kicks, rams, engine stones, murder holes, falls and drowning all go through `ResolverV2`, the wound book or the breath rule; formations add morale and position, never damage (CV Q1).
3. **People are conserved across gates.** Hosts and companies from outside are made at the gate (0x7A3, 0x7D4, 0x7B1) exactly to their counts and unmade when they leave; the war soak's people gate counts them (AW 4.6).
4. **The war score replaces the realm-level resolver in play.** History v1 keeps its own war step (Q12).
5. **The seasonal day stays sliced.** `S11War` plans one realm an hour; the hour-0 barrier gains no war work.
6. **`industries.md` still says the sheets are not in the repository.** They are now, in `docs/design/industry-sheets/`, and section 11 compares them. That answers INQ-1's first half; its second half is Q4.
7. **The plan's two Phase 7 estimates** (15 to 20 in the heading, 16 to 22 in the table): the table is used (section 3).

## 9. Budgets (the single ledger; the reference PC as in plan §2.11)

### 9.1 Live tick
**The battle bubble** has its own budget (plan §2.3; BB 10, CV 11). With 2,000 soldiers and 300 horses, 40% in contact:

| Item | Budget (average) |
|---|---|
| Grid rebuilt twice | 0.3 ms |
| Movement, slots and separation | 1.6 ms |
| Brains | 2.4 ms |
| Impacts | 0.7 ms (1.6 ms at peak) |
| Vitals and wound books | 2.0 ms |
| Morale | 0.7 ms |
| Projectiles | 0.6 ms |
| Formation layer | 0.6 ms |
| Mounted layer | 0.8 ms |
| **Total** | **about 9.7 ms typical** |

The gates: an average of 25 ms or less, the worst tick in a minute at 33 ms or less, and 0 allocations a tick in the grid, slots and morale.
- **Frame:** `CrowdLayer` at 1.5 ms of CPU or less, FarLayer minis at 1 ms or less, GPU p99 at 16.6 ms or less.
- **Memory:** 120 MB or less for 2,300 fighters.

**In a normal bubble:**
- a 1,000-man column is walking people inside Phase 3's 8 ms people tick, at 3 ms or less for them;
- a naval fight's carriers cost 0.2 ms a tick or less;
- footing cells take 30 ms or less to build, on a worker, when a box opens.

Phase 6 left the normal tick at about 5.9 ms typical, 6.5 ms worst.

### 9.2 Abstract tick
**Hourly (H1):**

| Item | Budget |
|---|---|
| Hosts, warbands and bands moving (40 or fewer groups) | 1 ms |
| Ships (30 or fewer) | 0.3 ms |
| Siege engines and wall damage | 0.5 ms a siege |
| Intel | 0.2 ms |

**At hour 18 (the host slot):**

| Item | Budget |
|---|---|
| `Supply` | 0.5 ms a host (10 hosts: 5 ms) |
| Purveyance | 0.2 ms a settlement |
| Forage | 0.3 ms a host |
| An abstract host | 0.05 ms |
| `ArmiesDaily` for 2,000 men | 2 ms |

**Daily:**

| Item | Budget |
|---|---|
| Industry stages (40 works of 5 stages a region) | 1 ms |
| Absorption caps | 0.3 ms |
| Supply contracts | 0.1 ms |
| Mints | 0.1 ms |
| Armoury care | 0.1 ms an armoury |
| `SoldiersDaily` | 1.5 ms |
| A siege's choices | 1 ms |

**Seasonal and worker jobs:**
- `S11War`: 5 ms a realm, one realm an hour.
- The continent tick with war: 500 ms or less, including count hosts at 20 ms or less.
- v2 battles on the worker: 2,000 v 2,000 in 45 ms or less, 200 v 200 in 8 ms or less, 100 v 100 in 3 ms or less.
- A count battle: 0.2 ms or less.

**The worst hour must stay under the 150 ms cap.** Phase 6 measured 78.6 to 92.9 ms with every system on, and `TalkLink` alone costs 8 to 9 ms an hour, so about 55 ms is left. Hour 18 adds up to about 8 ms in a region at war, and P7-G1 profiles it with everything on.

### 9.3 Memory, saves, content, soaks
| Item | Budget |
|---|---|
| `armies` (2,000 service rows at war), `soldiers` (2,000 rows, 10 companies), `warsupply` (a war year), `naval`, `sieges` (a siege), `coinage`, `scale`, `works`, `industry` | 300, 120, 80, 40, 20, 20, 30, 30, 60 KB a region |
| `battle` while a 2,000-soldier bubble is open | 1.5 MB (0 after) |
| **Phase 7 growth** | **<= 1 MB** per detailed region and hero save in a war year, beyond the 1.5 MB of an open battle |
| Content | count tables 120 KB, template library 300 KB (from 0.9 MB), siege 30 KB, naval 30 KB, soldiers 40 KB, scale and industry tables about 60 KB |
| Soaks | war-load 25 years <= 45 min a seed (nightly, five seeds in parallel); the war soak 25 years <= 2 h a world (nightly); the exploit and rich bots <= 10 min each; CI runs 2 years of seed 1234 |
| Allocation gates | 0 a tick in the grid, slots, morale and carriers; `HostSupply` 0 a call after warm-up; `MoneyText` 0 for Amount |

## 10. Combined open questions (deduplicated; "default" = recommended)

### 10.1 For the user (ordered by how much each changes the game)
| # | Question | Default (recommended first) | Why |
|---|---|---|---|
| Q1 | **Levy service:** how many days do levies (and vassals' men) serve unpaid in a campaign year? (merges AEQ-2 and armies Q1, which disagreed: 40 against 10) | **10 days**, the 40 historical days scaled to the 60-day year. A summer campaign is 15 days, so levies are paid for its last 5. Scutage prices the same 10 days | With 40, levies are never paid in a campaign, and arrears desertion and war finance lose their bite |
| Q2 | **Raw goods priced from their labour cost (the R1 rule)?** Ore, logs, firewood and clay become 3 to 8 times cheaper; finished goods stay near today's prices (EAQ-1) | **Yes** | Today a miner's day is worth 8.7 times his wage. Without the rule, "raw output barely covers wages, the profit is in finishing" and the 2 to 3 year payback cannot both hold |
| Q3 | **How harsh is purveyance?** (AEQ-1) | **Historical:** all grain above 30 days of a household's need is taken, along with one cart and one animal from households with two. It is paid at three quarters of the price by a tally due in 30 days, and defaulted after 4 seasons | It supplies campaigns fast, at the cost of grievance and riots the player can see |
| Q4 | **Crew sizes and tier jumps against your sheets** (section 11; INQ-1) | **Keep the spec's historical crews and machine-driven jumps,** and adopt two things from the sheets: crew bands that only rise with the tier, and real crews for the yard and packing stages from L2 | With historical output per worker, the sheets' 70-person works would flood every market. Your own later rule ("water-powered tiers multiply that") is what the machine jumps follow |
| Q5 | **Division of labour for every generated business,** not only the player's? (ESQ-1) | **Yes**; the audit's second run checks prices | The plan says "for every business". Finished goods get about a third cheaper in well-staffed shops |
| Q6 | **NPCs in industry:** may rich NPCs found works and upgrade them? Does the 1x to 5x slider apply to their works too? (INQ-2, INQ-3) | **Yes to both:** NPCs found L1 works at a payback of 3 years or less and upgrade one tier every 2 years at most; the slider applies to every staged works, but not to Phase 2's generated businesses | The same world rule for everyone (an honest slider) |
| Q7 | **Sea raids in peace** by raider cultures (Northmark)? (ships Q2) | **Yes:** a 300 permille choice a season when a weak target is known | Raiders who strike only in declared wars feel tame; the history already has them |
| Q8 | **How dark is war?** Sacks after a refused surrender, and killing prisoners (sieges Q2, soldiers Q5) | **As written:** a sack kills 1 to 5% of the townsfolk, burns houses, and takes captives where the law allows. Cruel AI commanders may kill prisoners; if the hero does, it is an atrocity (standing -300 with every group that hears). The hero may forbid sacks | It is brutal, fair and legible; no further atrocities are modelled |
| Q9 | **F1 to F4 change meaning:** from Phase 4's squad shapes to selecting formations, with the shapes moving to the order wheel (battle Q1) | **Yes, always;** with no army, a squad is formation 1 | One meaning for one key at every scale; it changes keys you have been using |
| Q10 | **Mercenaries' kit:** they keep their own and the employer fills their gaps from his armoury (soldiers Q1, a reading of the locked "armed from the lord's armoury") | **Yes** | A company of real people already owns what it carries |
| Q11 | **Copper pieces as token coin,** worth their face and capped at 4% of the money supply? (CNQ-2) | **Yes** | A full-weight copper piece would weigh about 60 g; historical small change was token or debased |
| Q12 | **New worlds' history fought by count armies** (a history v2)? (abstract war Q2) | **No:** history keeps v1's war step and play uses armies | It would add 1 to 2 minutes of world generation and a new history version for little visible gain |
| Q13 | **Storage unit:** keep the mite (a tenth of a copper) instead of the plan's whole copper? (CNQ-1) | **Keep the mite** | No save changes and cheap bulk prices stay exact. Whole copper costs about a month and shows nothing different |
| Q14 | **The Industry tab's one unit** (CNQ-4) | **Silver with one decimal** ("14.8 sp"), switchable to copper | It reads like your sheets' silver sums |
| Q15 | **Who signs off a Medium finding that is accepted unfixed?** (EAQ-2) | **You,** in the findings file, before M-E0 | Gate M-E0 opens the military half |
| Q16 | **If Phase 7 runs late, is the cut order right?** (section 3) | Kicks and branches go first, then horse archers, windmills, gold panning, mercenaries turning, ferries and fishing, lee shores, the export ban and NPC upgrades, runners, the gold book's extent, and budget items. The decided systems are never cut | The estimate is at the top of the range |

### 10.2 Technical (decided here unless someone objects)
| # | Question | Specs | Decision |
|---|---|---|---|
| T1 | Save version | overview 7 | v8 stays; ten optional sections, additive keys, the Phase 7 seeding step |
| T2 | The seam's names, forage, rations, timing, wages, levy draw, file names, chronicle duplicates | AE 3, AM 4.2 to 4.8, SC 3.1 | section 8.2 |
| T3 | Military parts before M-E0 | audit 7, overview 3 | only those that create no coin or goods, take no household's labour and move no coin (BB-A, SC-A, AW-B, and BB-B, BB-E, CV-C, SG-A, SH-A when a lane is idle) |
| T4 | Phase 6 debts in step 0 | review phase-6 | S10, S14, G1, S13, S11, rulers' old age; S9 and S12 at the close-out |
| T5 | `Engineering` skill | SG 4.2 | appended (8.1) |
| T6 | `AftermathCoin.Plunder` | 8.1 | repointed to the new `Plunder` in P7-0B |
| T7 | Civic buildings in old worlds | 7 | seeded as finished construction; new worlds through a placer version |
| T8 | Exploit threshold; seeds; siege starvation cap in WL-5 | EAQ-3 to EAQ-5 | 1.5x the skilled wage for the hero alone after costs; seeds 1234, 1, 7, 42, 9001; at most 40% inside a besieged town |
| T9 | Gold in purses; gold to silver; exchange between realms | CNQ-3, 5, 6 | opted-in purses only; 11.8 : 1; one standard, seigniorage differs |
| T10 | Munition cap; guild caps; span | ESQ-2 to 4 | common; 3 journeymen, 2 apprentices, 2 stations, licence 5 gp and 5 sp a season; `6 + Leadership / 10` up to 16, -25 permille a worker of excess, floor 500 |
| T11 | Flow a km², windmills, hemp, barracks | LBQ-1 to 4 | 15 L/s; yes, for milling and drainage only; a fibre crop and a gate import; the room is the economy's, the sleepers the military's |
| T12 | Purse per works; yard and packing at L1; accident rates | INQ-4 to 6 | one purse per works; L1 yard and packing on spare hands (L2 up: Q4); as IN 5.7 |
| T13 | Trampling; sutlers' markup; scutage rate; arms budget | AEQ-3 to 6 | as much trampled as taken; 30%; 300 mites a man-day; 10% of last year's receipts |
| T14 | Default battle size; orders while fighting; fog in the overlay | BB Q2 to Q4 | 1,000 (the default drops in steps of 250 if BB-5 fails, the top stays 2,000 with a warning); yes, with the delay; only what your side sees, with ghosts |
| T15 | A charge hitting harder; horses in the cap; stakes in battle | CV Q1 to 3 | no (shock is morale); horses are extra; archers with a stake in their loadout plant them in 20 s a cell |
| T16 | Who is drawn; forage without a warrant; hosts from outside; night marches | AM Q2 to 5 | AM 4.2's rule; theft; made at the gate and unmade; none |
| T17 | Levy wealth classes; drill cap | SC Q2, Q4 | poor, middling, well-off; drill to 40, sparring to the partner's skill minus 5 up to 60 |
| T18 | Sapping; trebuchet rate; gunpowder | SG Q1, 3, 4 | sapping is left out (Phase 8 if wanted); a shot each 120 real s, 1.5 an hour in the abstract; no gunpowder |
| T19 | Square sails upwind; swimming in armour; people on ships in the cap | SH Q1, 3, 4 | no higher than a beam reach; a man sinks above 15 kg worn unless he sheds it (the HUD warns); yes, they count |
| T20 | The gate's small-count floor; soak thresholds; scenario I | AW Q1, 3, 4 | ±0.3 men under a mean of 2.0; as written, moved only with a reason in the review; nightly, not CI |

The answers to section 10.1 will be recorded in a section 10a, "decided with the user", as in Phase 6. Where 10a and a spec's default disagree, 10a wins.

## 10a. Decided with the user (Nov 2026)

Asked on 21 Nov 2026; every answer took the recommended default.

| # | Question | Decision |
|---|---|---|
| Q4 | Industry tiers: crews or machines | **Historical crews with machine-driven tier jumps**, adjusted to the user's sheets: crew bands always rise with the tier (no overlap, no shrink at L3), and the yard and packing stages get real crews from L2. `industries.md`'s tables are to be corrected to this before IN1 starts. |
| Q2 | R1: raw goods priced from labour | **Yes.** Ore, logs, firewood and clay become 3-8x cheaper; raw output barely covers wages and the profit is in finishing (the sheets' rule), giving the 2-3 game-year payback. |
| Q1 | Levies' unpaid days a year | **10 days**, then pay or release. |
| Q3 | Purveyance | **Historical:** grain above 30 days of a household's need, at 3/4 of the price, by a tally due in 30 days. |
| Q8 | How dark war gets | **As written:** a sack kills 1-5% and burns; captives where the law allows; cruel commanders may kill prisoners; the hero doing so is an atrocity. |
| Q7 | Raiders in peace | **Yes:** raider cultures raid by sea and land in peace (about 300 per mille a season). |
| Q9 | F1-F4 | **Formations 1-4**; Phase 4's squad shapes move to the order wheel; Shift+F1-F4 adds to the selection. |
| Q16 | The cut order (section 3) | **As proposed.** |

The other user questions (Q5 division of labour in every generated business, Q6 NPC founders and the slider on their works, Q10 mercenaries keep their own kit, Q11 copper as capped token coin, Q12 history keeps v1's war step, Q13 the mite stays the stored unit, Q14 the Industry tab in silver with one decimal, Q15 the user signs off accepted Medium audit findings) take their recommended defaults; the user may revisit any of them before its part starts.

### 10b. The user's sign-off on the first audit run (21 Nov 2026)
Recorded in full in `docs/reviews/phase-7-audit.md` ("The user's sign-off"): big L2/L3 works keep their output and must ship far (IN4's own caravans matter more; EA3 measures export payback); EA_F9 is fixed (war costs harvest); EA_F16 is investigated on five seeds first; the six Low findings are accepted.

### 10d. The levy is drawn by village (the user, 21 Nov 2026)
EA3c built the children's harvest hours (§10c) and the field-hands-first muster, but a summer levy of one man in five still cost the harvest -0.4% against WL-6's 10-35%: the region has more farm hours than the reaping needs, and an even draw only delays each field by about a day inside its grace. **Decision: the levy is drawn by village.** Each fief's quota is filled village by village (the villages that owe the service, in the lord's order), so some villages send most of their able men and their grain rots in the field after the deadline, while untouched villages bring theirs in. The cost of war then falls on named places, and the chronicle says why (a `harvest_lost_levy` entry with the village and the men taken). Owner: P7-EA3d (before EA3b); WL-6 is measured with the children counted. Rejected: a tight historical harvest window with the economy retuned (the control harvest fell to 56%), and accepting a war that costs no harvest.

### 10e. War costs no harvest: accepted (the user, 21 Nov 2026)
EA3d built the village levy (§10d): the drawn villages fall 2.5% behind in their reaping by day 30 and catch up inside the 10-day grace, so a summer levy costs -0.2% of the harvest (WL-6 asks 10-35%). The harvest is not labour-bound here: the region reaps its whole harvest in 2-3 days. Offered a tighter window (peacetime harvest kept), a bigger quota from fewer villages, or acceptance, **the user accepted that war costs no harvest.** The village levy, the children's hours and the `harvest_lost_levy` entry stay (the chronicle tells whose men were taken); **WL-6 is waived** and EA_F9 is closed as accepted. War's cost to a realm stays in coin, food, men and the purveyance's grievances.

### 10f. The second audit run signed off (the user, 22 Nov 2026)
EA_F16 and EA_F17 accepted (WL-3 net of the mint, WL-4 against the control); EA_F21 accepted (hire by outbidding); EA_F20 (works pass to heirs) and EA_F19 (charcoal burners follow demand) are fixed in P7-EA3e, after which the L3 payback (EA_F18) is re-measured. Details in `docs/reviews/phase-7-audit.md`. **M-E0 closes when EA3e is merged**; WL-7, 9, 12 are measured when AE2 and AE3 exist.

### 10c. Children at the harvest (the user, 21 Nov 2026)
**Children help with the harvest.** Today the farm planner (`Farming/FarmingSystem.cs`, `RecomputeHours`) counts no one under 12 and a 12-15-year-old as half a day. Historically children from about 7 bound and stooked sheaves, gleaned, carried food and drink to the reapers and minded the animals, so a household whose men are levied still gets part of its harvest in.
- **Rule:** in the harvest (the Reap and BindCart tasks, and haymaking), a living child of 7 to 11 of a farming household gives a third of a working day (`child_harvest_min_age` 7, `child_harvest_pct` 33 in the farming defs, both content). Those hours serve **only** reaping, binding and carting and hay, never ploughing or sowing; they work their own household's land first and are hireable to a neighbour's harvest like any hand, at the day wage pro rata. 12-15 stay at half a day for every task, as now.
- **What it changes:** more harvest hours in every year, so the levy fix (EA_F9) is calibrated **with the children counted**: WL-6 (10-35% of the harvest lost to a summer levy) is measured on the world as it will be, not as it was. Owner: built with the EA_F9 fix (AE2/EA3b; the farming change is the EA3b part if AE2 has not started). Golden trails move (more harvest hours); re-pin with that reason.
- **Test:** a household of one man and three children (8, 10, 13) with the man levied reaps more of its field than the same household without the children, and the children's hours never appear on a ploughing or sowing task.

## 11. The user's industry sheets against `industries.md`

The sheets are in `docs/design/industry-sheets/`: the iron, clay and wood PDFs and their balance-sheet PNGs. Per the plan, their outputs and prices are rescaled on purpose to historical rates and the game's coin. **Their structure is the target:** the stages, tier jumps, crew ranges and stage ratios.

**What matches:**
- **Stage order.**
  - Iron is mine, cleaning, storage, smelting, packaging (the spec: mine, crusher and washer, ore yard, smeltery, packing).
  - Clay is pit, storage, processing, bake, kiln, package (the sheet's "bake, 1 week" is the spec's drying sheds).
  - Wood is chopping, storage, processing, packaging, shipping, with transportation in the sheet's calculation row.
- **Furnace counts by tier:** 1, 2 and 3 for both the smeltery and the kiln.
- **Wagon classes:** 500, 1,000 and 2,000 kg (ES 4.4's cart, ox wagon and four-horse wagon).
- **Moulding output per worker:** flat across tiers on both.
- **The wood L1 crew:** 13 on the sheet and 11 in the spec, with processing (sawing) the largest crew on both.
- **Raw output barely covers wages:** the sheet's mine earns about 1.5 times its wages; the spec's earns 1.0 times.

**Structural mismatches** (ranges are L1 / L2 / L3; "max ratio" is the L3 maximum's capacity over L1's):

| Stage | Sheet crew bands | Spec crew bands | Sheet output a worker; max ratio | Spec rate; max ratio |
|---|---|---|---|---|
| Iron mine | 1-25 / 26-50 / 51-75 | 2-8 / 6-20 / 12-40 | 15 / 17 / 19 barrels; x3.8 | 333 / 400 / 444 kg rough (hewers with fewer haulers); x6.7 |
| Iron cleaning | 1-10 / 11-17 / 18-25 | 1-6 / 3-10 / **2-8** | 40 / 41-53 / 54-60; x3.75 | 400 / 1,000 / 4,000 kg; x13 |
| Iron storage and packing | L1 open (25 a worker) / 10-15 / 16-20 (both stages) | yard 0-3 / 1-4 / 2-6; packing 0-2 / 1-3 / 1-4 | 25 / 60 / 62-75 | yard 4,000 / 8,000 / 20,000 kg |
| Smeltery | 5-10 / 11-15 / 16-20 | 2-5 / 4-14 / 6-18 | 50 / 55-67 / 69-75 ingots; x3 | 5 / 12 / 36 units a day; x7.2 |
| Clay pit | no tiers: worker grades (untrained, skilled, mechanised) at 50 / 100 / 200 on the PNG, 100 / 200 / 400 in the PDF | 2-8 / 4-12 / 6-24 at 2,000 / 3,000 / 4,000 kg | - | - |
| Clay storage (yard) | L1 open (75) / 4-6 / 8-12 | 1-6 / 2-6 / **1-4** | 75 / 150-167 / 117-150 | 1,500 / 6,000 / 15,000 kg tempered |
| Moulding | 5-15 / 16-25 / 26-40 | 2-4 / 6-12 / 12-24 | 50 a worker at every tier; x2.7 | about 333 bricks a worker at every tier; x6 |
| Drying | 1 week at every tier; L1 open / 4-13 / 14-20 | 15 / 12 / 6 days; 1-3 / 2-5 / 2-6 | - | x10 capacity a day |
| Kiln | 5-15 / 16-25 / 26-40 | 2-6 / 4-10 / 6-14 | 100 a worker; x2.7 | about 2,000 / 6,000 / 10,300 bricks a day; x5.1 |
| Felling | no tiers: worker grades at 200 / 400 / 800 ft | 2-12 / 4-16 / 6-24 at 1,050 / 1,400 / 1,750 kg | - | - |
| Hauling | wagons by capacity, no tiers | three tiers (oxen dragging, timber carriage, floating) | - | - |
| Timber yard | L1 open (300 ft) / 4-6 / 8-12 | 0-2 / 1-4 / 2-6 | 300 / 1,700-2,000 / 1,200-1,500 ft | 4,000 / 8,000 / 20,000 kg |
| Saw mill | 4-6 / 8-10 / 12-14 | 2-8 / 3-12 / **2-8** | 1,000 ft a worker at every tier; x2.3 | 75 / 150 / 600 bf a worker; x8 |
| Shipping | no table | three tiers (cart bay, jetty, jetty with crane) | - | - |

1. **Crew bands.** The sheets' bands are contiguous and always rise: L2's minimum is L1's maximum plus one, or close to it. The spec's bands overlap, and three of them **shrink at L3** (iron dressing, the clay yard, the saw mill: the machine replaces hands).
2. **Tier jumps.** On the sheets, output per worker is flat or rises modestly (at most 1.5x from L1 to L3), and capacity grows 2.3 to 3.8 times, mostly through bigger crews. The spec's jumps come from machines and are 5 to 13 times. Your later rule ("water-powered tiers multiply that") supports machine jumps, but the spec's L3 ratios are 2 to 3 times the sheets'.
3. **Logistics crews.** Storage and packing are 32 of the 75 people in the iron balance sheet. The spec runs the L1 yard and packing on spare hands (`crew_min` 0) and keeps them at 6 or fewer at L3.
4. **Works size.** The sheets' L1 works are 75 people (iron), 69 (clay) and 13 (wood); the spec's are 9, 10 and 11.
   - With historical output per worker, a 75-person L1 iron works would make several tonnes of bloom a day, against a region that wears out about 70 kg of iron a day. The rescale forces small crews.
   - It also flattens the sheets' difference between chains: iron and clay large, wood small.
5. **Untiered stages.** Felling and hauling have no tiers on the sheets (worker grades and wagon types instead), and shipping has no table. The spec tiers all three.
6. **Worker grades.** The sheets' extraction workers come in untrained, skilled and mechanised grades, with output 1 : 2 : 4 at wages of 4 and 6. The spec uses the skill speed curve and the master, journeyman and apprentice grades. P7-IN1 should check that skill 20 against skill 60 spans about 2 times.
7. **Drying time.** The sheet fixes it at a week; the spec's 15 days at L1 is longer, and its time shortens by tier.
8. **Upgrade cost.** On the sheets, L2 costs 1.5 times L1 and L3 costs 2 times, at every stage (stage buildings 5,000 / 7,500 / 10,000; the mine 10,000 / 15,000 / 20,000). The spec prices upgrades from the plans' materials and hours and states no ratio. P7-IN3's "payback of the upgrade" should land near the sheets' ratios.
9. **Relative works cost.** On the sheets, iron (mine excluded), clay and wood cost 8,000, 10,750 and 6,750. In the spec they cost 140,000, 30,000 and 35,000 mites: iron is 4 to 5 times the others instead of about the same.
10. **Smaller differences:**
    - The clay pit's grades differ between the PDF (100 / 200 / 400) and the PNG (50 / 100 / 200).
    - Iron packs into barrels of 5 ingots on the sheet and into 100 kg crates in the spec.
    - The sheets' caravan table (nearby, distant and far towns at 1, 2 and 3 weeks, paying 2 to 4 times the gate price for iron) is replaced by market prices, as decided ("prices from markets, not fixed tables"). The far-town premium will be whatever scarcity gives, likely much less than 4 times. The audit's I2 and X1 rows measure it.

The recommended answer (Q4) keeps the historical crews and the machine jumps. It changes the spec in two places, before P7-IN2:
- **Crew bands only rise:** at each tier the minimum is at least the lower tier's minimum, and the maximum is never below the lower tier's maximum. In practice the L3 maximums of iron dressing, the clay yard and the saw mill rise to their L2 maximums.
- **Yard and packing get real crews from L2:** at least 1 at L2 and 2 at L3.

## 12. Risks

| Risk | Why it matters | Mitigation |
|---|---|---|
| **Scope:** 47.8 serial months, thirteen specs, two writers | the overrun rule; the estimate fits only with three helpers busy all phase | helper-sized parts with their own gates; the lane plan of section 3; the cut order; checkpoints M-E0 by month 6 and M-B by month 12 |
| **The audit opens more than it budgets** (EA3 holds 0.8 months) | M-E0 gates the military half | the fix budget is explicit; a finding over it is a schedule finding written into the review; battle-side parts fill idle lanes before M-E0 (T3) |
| **The reference PC** (2,000 soldiers at 60 fps; Phase 0's spike ran 0.46 ms a tick for movement alone) | done-when 1; needs the user's machine | BB-5 on the shared box first; the battle-size fallback in steps of 250 with waves; the frame run in P7-G1 |
| **The ±10% re-fit** (Phase 4 missed 17 of 45 comparisons) | the abstract resolver decides most battles | geometry, flanks and passes added before the re-fit; the gate names its comparisons; a live-side outlier goes to the battle spec, not the fit |
| **The seam between halves** | two writers named the same things differently | section 8.2; AE1 implements the military's needs with stubs, and AM-D rewrites AM-11 to AM-13 against the final calls |
| **Re-pricing breaks goldens** (the R1 rule, wages, scale) | many tests pin mite amounts | IN goldens take prices as parameters; each retune re-runs `PriceSolverTests` and re-pins with the reason |
| **The hero's id 0** (Phase 6 S6 to S8) | new person-keyed books | the rule of 8.5.1 and a "hero as X" test per book |
| **Determinism on moving frames and levels** (carriers, decks, wall-walks, grid order) | a fight hash that drifts by platform | ascending-id candidates (BB-2), integer transforms, `IntMath.Isqrt` only; the Linux and Windows trail in CI |
| **The abstract hour** at war (hour 18 adds about 8 ms; `TalkLink` already 8 to 9 ms) | the 150 ms cap | hosts in their own slot, sliced planning, P7-G1 profiles everything on |
| **Headless soaks that test less than play** (Phase 6 G1) | a green soak that never ran courts or contracts | G1 fixed in step 0; the soak asserts each attached system's counters move |
| **Old worlds and new civic buildings** | library worlds must load, and their seeded state must equal a new game's | the seeding step keyed on seed and ids; a placer version for new worlds |
| **Art volume** (minis in 3 views, engines and ships in 8 facings, about 45 pieces) | done-when and the fun check need them | one palette and the validator; palette swaps for liveries; art inside the estimate's slack |
| **War becomes dull or unreadable** | brutal must stay fair through legibility | known, not true, strengths on the map; the order delays shown; the "why" on every desertion, purveyance and peace; the testers' question |

## 13. Test strategy
Every spec has golden tests, **232** in all:
- **Economy:** EA 16, CN 16, ES 16, LB 16, IN 24, AE 22.
- **Military:** BB 18, CV 14, AM 20, SC 18, SG 20, SH 16, AW 16.

OV-1 to OV-8 (section 4) come on top, in `tests/Ravensguard.Sim.Tests/Phase7Overview/` with the kit `P7Game.cs`.

**How the suites run:**
- **Helpers** run their part's tests and the save, resume and golden-hash tests.
- **CI** runs the full suite, the determinism job (with `phase7_trail`), AW-15 and the war-load harness's short runs.
- **Nightly** runs the war-load soak on five seeds (EA-15), the war soak on ten worlds (AW-16), scenario I and the 2,000 v 2,000 gate.

**The shared harness:**
- the Phase 2 `HeadlessWorld` and the Phase 6 soak harness, with G1's attachments;
- the `--autoplay` bot, extended to call levies, lead hosts, besiege, command formations, sail and found works;
- the exploit and rich-player bots;
- `WarLoad` as a client of the real `HostSupply`;
- `WorldRuntimeResumeTests` with a ninth case holding every Phase 7 section.

**Conservation audits** stay exact every soak day:
- coin through the Ledger, with no `Burn` outside tests;
- mass through `MassLedger`, with forage, baggage, sacks, ships and summaries;
- people across gates, levies, bandits and prisoners;
- world gold by `CoinStock`.

**Phase end:**
- the code and bug check (plan §6) in `docs/reviews/phase-7.md`;
- the fun check;
- the testers' question.

## 15. After the Q4 revision of `industries.md` (772b6c3)
- `industries.md` §4.1/§5.1 now hold the crew bands (contiguous, rising with the tier; yard, packing and shipping crews from L2). Section 11's "spec crew bands" column, §8.5 item 6, §10.2 row T12 and "an L2 iron works, crew about 25" (now 19) are superseded by `industries.md` §14.
- **For the economy audit (EA2/EA3):** at the gate price, L2 and L3 upgrades repay in 10 to 53 days (L1 keeps its 2-3 game-year payback). The spec leaves the market as the brake (an upgrade still repays in 2.5 years down to 62-89% of today's price); the audit's second run must confirm that real markets do bring prices down that far, or tune the upgrade bills.
- **Also for the audit:** rope is priced at 2,000 mites a kg in today's content (four times bar iron); reprice under R1.
- `large-buildings.md` 3.2's treadwheel crane "yard handling 20 t a worker-day" is read as the crane's daily lift limit.
- Measured: skill 20 against skill 60 gives 1.18x output in the code, not the 2x the sheets' untrained:skilled ratio suggests; the skill curve is left as is (R1 prices are set at skill 50).
