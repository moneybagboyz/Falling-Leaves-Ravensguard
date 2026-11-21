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
   - **P7-IN1, stage runtime** (lane A, 1.2). Gate: IN-1 to IN-3, IN-8, IN-16, IN-19, IN-23.
     - **Built (P7-IN1):** `Production/Industry/` (`IndustryContent`: `ChainKind`, `StageKind`, `StageTier`; `IndustryBook` with `Works`, `WorkStage`, `WorkLink` and the 60-day balance ring; `StageMath`, the pure day formula, tier reached by crew, hauling trips and link capacity; `IndustryWorld`, the runtime; `IndustryWiring` with `IndustrySystem` at H4 `ProductionDaily + 20` and the `in:` desk; `StageHost`, the two hooks Phase 2 asks), `Save/IndustrySave.cs` (the optional `industry` section, written only while a works exists), content `production/industry/chains.jsonc` (3 chains, the 17 stage kinds with all three tiers' crew bands, rates, capacities and stores, and `industry_settings`), `goods/goods_industry.jsonc` (`iron_ore_rough`, `tempered_clay`, `bricks_green`), the generated mine's composite rate (109 kg of dressed ore a worker-day, `ProductionSystem.MineDay`). A stage is a `Business` of a stage kind (the 17 are business kinds appended after every ordinary kind, so no index moves); a works' stages share the first stage's purse (`Business.Purse` and `Party` re-pointed at load by `IndustryWorld.Rebind`); Phase 2's planner, top-ups, dividends and closing skip stage businesses and the labour market hires them to the works' crew target (hewers and carriers by the tier's `support_pm`). Gate: IN-1 to IN-3, IN-8, IN-16, IN-19, IN-23 and IN-26 green (`IndustryTests`), plus the crew-band content check (IN-25's bands) early. Hero verbs `in:found|crew|wage|route|link|cash` through `HeroDesk` (`HeroCommandKind.Industry`). Chronicle `works_founded` and `works_stopped`. **Re-pinned:** `phase5_trail` and `phase6_trail` (both seeds): the three new goods sort into the goods table and move the indices after them (checked: the composite mine rate alone does not move either trail); the generated-layer and history lines did not move; `ContentRemapTests.D` lists the new goods and the 17 stage kinds as new. Left for IN2 to IN5: recognition of tiers from pieces (`WorkStage.Tier` is set by `SetTier` until then), plans, upgrades, seasons, accidents (0x740), kiln losses (0x742), packing and containers, drying days, the finery's bar, the stage panel, balance sheet and payback views, gate absorption and carting, contracts, own caravans, NPC founders; details in `industries.md`'s Notes.
   - **P7-EA2, the audit run** (lane B, 0.7):
     - the four passes on `p7-audit-base`;
     - the probes and bots;
     - `docs/reviews/phase-7-audit.md` with the grep lists and every checklist row.
     - Gate: EA-1 to EA-10, EA-13 and EA-14 measured.
   - **P7-LB2, buildings** (lane B, 1.0). Gate: LB-8 to LB-12, LB-16.
   - **P7-ES2, costs, contracts and patterns** (lane C, 0.9). Gate: ES-4, ES-5, ES-8 to ES-11, ES-14 to ES-16.
2. **Step 2, chains, the armoury, the audit's fixes, gate M-E0** (months 2.5 to 5.1)
   - **P7-CN1, coinage, sim side** (lane A, 0.5, merges after EA2's run). Gate: CN-2 to CN-10, CN-13 to CN-16.
   - **P7-IN2, chains and tiers** (lane A, 0.9). Gate: IN-4 to IN-7, IN-12 to IN-15, IN-21.
   - **P7-SC-A, roles, templates and the armoury** (lane B, 1.3, uses LB2's armoury). Gate: SC-1 to SC-6.
   - **P7-IN4, selling** (lane C, 0.7). Gate: IN-17, IN-18.
   - **P7-CN2, coinage display** (lane C, 0.3): `MoneyText`, `UiKit.Money`, the sweep of about 35 Game files and 22 localisation rows, the mint and changer panels. Gate: CN-1, CN-11, CN-12.
   - **P7-EA3a, the first fixes** (lane A, 0.5): F-1 (the R1 rule, if Q2 says yes), F-2 (loot values from material), F-4 (deposit grades), and every other High or Medium finding of EA2. Each fix has an `EA_F<n>` test that fails before it.
   - **P7-IN3, the Industry tab** (lane C, 0.7). Gate: IN-9 to IN-11 as views, a screenshot at 960 x 540.
   - **P7-IN5, NPCs and calibration** (lane B, 0.3). Gate: IN-20, IN-22, IN-24.
   - **P7-EA3b, the second run** (lane C, 0.3):
     - the war-load soak on five seeds with coinage, scale and industries in;
     - an L3 iron works near the route.
     - Gate: EA-15 (WL-1 to WL-15), `econ-audit` with no failed row.
   - **Milestone M-E0, "the audit gate"** (about month 5.1). The **month-6 checkpoint**.
3. **Step 3, armies and supply** (months 4.4 to 8.6)
   - **P7-AE1, `HostSupply` complete** (lane A, 1.2):
     - the ladder with food rents, depots and convoys;
     - muster and release (the AM levy rule, section 8.2);
     - harvest risk, camps, abstract parity.
     - Gate: AE-1 to AE-4, AE-11 to AE-13, AE-21.
   - **P7-BB-B, formations and orders** (lane B, 1.3). Gate: BB-6 to BB-13.
   - **P7-AM-A, army book and warbands** (lane C, 1.2, after M-E0 and SC-A). Gate: AM-1, AM-2, AM-14, AM-19.
   - **P7-SG-A, walls and breaches** (lane A, 1.0). Gate: SG-1 to SG-4.
   - **P7-AE2, purveyance, scutage, finance, forage and sutlers** (lane B, 1.0). Gate: AE-5 to AE-10, AE-14, AE-20.
   - **P7-AM-C, marching and camps** (lane C, 1.3). Gate: AM-7 to AM-10.
   - **P7-BB-D, command UI** (lane A, 0.9). Gate: BB-11, BB-17.
   - **P7-SG-B, engines as vehicles** (lane B, 1.4). Gate: SG-5 to SG-7.
   - **P7-AM-B, levies and the muster** (lane A, 1.0). Gate: AM-3 to AM-6, AM-18.
   - **P7-AM-D, rations and desertion** (lane C, 1.0): the military side of the seam (section 8.2), forage parties, hunger, desertion, after-battle fold-back. Gate: AM-11 to AM-13 (as rewritten in section 8.2).
4. **Step 4, the field, the march and the sea** (months 8.4 to 11.9)
   - **P7-SH-A, ships and decks** (lane B, 1.2). Gate: SH-1, SH-12 (sim side), SH-13.
   - **P7-CV-A, cavalry formations and the charge** (lane A, 1.0). Gate: CV-1 to CV-5, CV-13.
   - **P7-BB-C, the bubble** (lane C, 0.8). Gate: BB-14 to BB-16. **Milestone M-A "the field"** (about month 9.4): a battle of formations commanded with F1 to F4, R and the tactical pause.
   - **P7-AW-B, abstract scale and the library** (lane C, 0.7). Gate: AW-7, AW-8.
   - **P7-AM-E, campaigns and the war score** (lane A, 1.3). Gate: AM-15 to AM-17, AM-20. **Milestone M-B "the march"** (about month 10.8; **the month-12 checkpoint**; done-when 2).
   - **P7-SH-B, sailing and harbours** (lane B, 1.1). Gate: SH-2 to SH-8.
   - **P7-AW-A, v2 geometry and passes** (lane C, 1.3). Gate: AW-1 to AW-3, AW-13.
   - **P7-SG-C, the assault** (lane B, 1.0). Gate: SG-8 to SG-12.
   - **P7-SC-B, growth and the wage roll** (lane A, 0.9). Gate: SC-7 to SC-12.
   - **P7-CV-C, ground and weather** (lane C, 0.5). Gate: CV-6, CV-10, CV-11.
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
