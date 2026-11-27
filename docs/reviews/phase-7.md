# Phase 7 code and bug check

Reviewed at 24fd525: every Phase 7 build part merged except two found unbuilt at the close (AE3, arms industry and loot; SG-E, war wagons and chariots), which were then built (643e307, 39621dd) and reviewed in a second pass (P7R-18 to P7R-26 below) (the economy audit gate M-E0, industries, coinage, economies of scale, large buildings, army economy, armies on the map, the battle bubble, cavalry, sieges, ships and naval war, soldiers and companies, abstract war v2 and its calibration, and the war soak). The review covered the code added or changed in `src` and `content` since 09338fc closed Phase 6: 382 files and about 32,300 lines. Tests were outside its scope.

The review was read-only. It started with mechanical greps over the whole diff, then read the high-risk paths closely. The economy was red-teamed separately before the armies were built: see `docs/reviews/phase-7-audit.md`, the step 0 audit, signed off by the user on 22 Nov 2026.

Severity counts: **bug 7, conservation 5, determinism 1, perf 1, nit 3**.

## Findings (most severe first)

| Id | Kind | Where | Defect | Outcome |
|---|---|---|---|---|
| P7R-1 | bug, conservation | `Soldiers/SoldierTick.cs`, `Armies/ArmyWorld.Raise.cs` | Warband men and host levies were paid twice a day: once by the army's pay day, once by the wage roll. | **Fixed** (cda47e8): the soldiers' tick skips them. Test added. `phase6_trail` re-pinned. |
| P7R-2 | bug | `Battle/BattleBubble.Demote.cs` | A fight handed to the abstract tier at the bubble edge cloned the wound books but never adopted the new ones, so men killed in the abstract half stayed alive in the world. | **Fixed** (085b6a5): books adopted into the wound ledger, ammo written back. Test added. |
| P7R-3 | bug | `Siege/SiegeWorld.Storm.cs`, `.Brain.cs` | A host emptied by desertion could still storm a place with no garrison, and the siege was scored Stormed with no losses. | **Fixed** (cda47e8): a storm needs men, and an empty host is lifted first. |
| P7R-4 | bug, perf | `Siege/SiegeAssaults.cs` | Nothing in the game stepped a live assault, so a bubble of up to 300 fighters sat unfought for a day and then fell back to the abstract storm. | **Fixed** (085b6a5): `SiegeAssaults.GameDrivesLiveAssaults` is off by default and assaults run abstract. Wiring the live assault into the game is **moved to Phase 8**. |
| P7R-5 | conservation | `Armies/ContinentArmies.cs` | A storm on a summary town at population 0 counted one dead defender it did not have, so people grew by one each storm. | **Fixed** (cda47e8): the deaths are clamped and there is no storm without a levy. Test added. |
| P7R-6 | conservation | `Naval/ArmyWorld.SeaRaids.cs`, `Armies/ArmyWorld.Outside.cs` | A raid that got away left its plundered coin and grain counted in the region. Its men left without `Demography.Depart`, so the continent ledger missed them. | **Fixed** (085b6a5): an emigration sink, the grain exported, and `DepartRow` through `Demography.Depart`. Test added. The ships' non-grain cargo is not exported yet. |
| P7R-7 | conservation | `Naval/ShipCombat.cs`, `SeaRaids` | A prize ship's strongbox kept its coin with no owner, and captives had a captor purse that did not exist. | **Fixed** (085b6a5): the box goes to the captor (a caught raid's to the fief treasury), and captives' captor is the ship's chest. |
| P7R-8 | bug | `Soldiers/WageRoll.cs` | One man with no purse blocked every other man's back pay under the same lord. | **Fixed** (cda47e8). Test added. |
| P7R-9 | conservation | `WarSupply/HostSupply.Siege.cs` | The siege top-up drew more from the realm treasury than a capped buy spent, and was labelled Hoard. | **Fixed** (cda47e8): `TransferReason.Subsidy`, and unspent coin returns to its source. `Disband`/`Fund` still return leftovers to the realm treasury: **moved to Phase 8** (low). |
| P7R-10 | bug | `Soldiers/PrisonerPens.cs` | A ransom could be paid for a prisoner who had died, and a self-pay row stalled forever. | **Fixed** (cda47e8). Tests added. |
| P7R-11 | bug | `Armies/ArmyWorld.cs` | A swallowed exception around `SoldierWiring.For`. | **No change needed:** it only throws with no economy, and `Attach` returns before that. |
| P7R-12 | determinism | `Naval/ArmyWorld.SeaRaids.cs` | `Math.Sqrt` on doubles in the sim. | **Fixed** (085b6a5): `IntMath.Isqrt`. |
| P7R-13 | perf | `Soldiers/SoldierTick.cs`, `ContinentArmies`, season-day systems | The day's first hour reached 192–266 ms against the plan's 150 ms hourly budget (war soak CI part). | **Partly fixed** (cda47e8): wage rows grouped, drill names cached, BFS distances cached; 212 ms became 171–199 ms on this shared box. The remaining spikes are the season-day systems (seasonal growth, continent trade, weather pre-warm, caravans). **Moved to Phase 8** with a measurement on the reference PC. |
| P7R-14 | nit | `Naval/SeaRaids` | A coin take added 1 g to the grain taken. | **Fixed** (085b6a5). |
| P7R-15 | nit | `Soldiers/MercCompanies.cs` | A turn-coat offer was not checked for a purse. | **Fixed** (cda47e8). |
| P7R-16 | nit | `Siege/SiegeWorld.Storm.cs` | A storm with no outbox dropped its death events. | **Fixed** (cda47e8): a local outbox is flushed into the chronicle. |
| P7R-17 | conservation (dev) | `Game/World/StreetView.Industry.cs` | The industry debug verb minted coin in any build. | **Fixed** (cda47e8): gated to debug builds. It still uses `SourceReason.Seed`, because no dev-only reason exists. |

### Second pass: P7-SG-E and P7-AE3, and two Phase 6 carry-overs (27 Nov 2026)
Determinism, coin and saves were clean in the second pass. Most of the code it covers is not yet called in live play: only the arms trade runs. Every finding below is **fixed** in one commit, each with a test, and no golden moved.

| Id | Kind | Where | Defect |
|---|---|---|---|
| P7R-18 | bug | `WarSupply/ArmsWorld.cs` `RunRefit` | Condition was raised before a failed add, and unstockable items stayed queued forever. |
| P7R-19 | conservation | `ArmsWorld.cs` `Sort`, `ForgeScrap` | With a full store, items were freed and scrap was counted that was never created. |
| P7R-20 | bug | `ArmsWorld.cs` `Order` | Pending orders were ignored, rows were never cleared, and the budget was per call rather than per season. |
| P7R-21 | bug | `Vehicles/WarWagons.cs` `Seconds` | Extra men had no cap, so a chain was cut in 1 s. |
| P7R-22 | save | `WarWagons.cs`, `Chariots.cs` | A fort's origin and cause were not packed, and chariots had no save path. |
| P7R-23 | bug | `ArmsWorld.cs` `Sort` | The "sold" branch moved no coin, and duplicate ids slipped through; it is now "kept", with distinct ids. |
| P7R-24 | nit | `WarSupply/HostSupply.Arms.cs` | An `ArmsWorld` was built in peace, and a 0-mite payment was refused. |
| P7R-25 | save | `Save/WarSupplySave.cs` `ArmsSave.Restore` | Short rows were dropped silently, and shape indexes were not bounds-checked. |
| P7R-26 | tests | `WagonChariotTests`, `ArmsIndustryTests` | Three asserts could not fail. |
| S9 (Phase 6) | bug | `Faith/Relics.cs` | A true reliquary sold to another church was hidden from its altar. |
| S12 (Phase 6) | bug | `Core/ChronicleCompaction.cs` | Entries held only by saved books could be compacted away; such ids are now pinned. |

The merge also found six chronicle types written by SC-C and SC-D with no row in `types.jsonc`: `company_for_hire`, `prisoners_taken`, `prisoners_fate`, `parole_broken`, `ransom_paid` and `ransom_ceded`. `ChronicleFullTests.CH1` caught them. They are added.

## Checked and found clean
- **Floats and doubles in the sim:** only in diagnostics, calibration, and content-load rounding (`Production/Patterns.cs`, `SupplyContracts.cs`).
- **Timing calls:** Stopwatch only feeds the cost counters.
- **Collections:** every new Phase 7 book is a `SortedDictionary`, and `Parallel.For` appears only in calibration code, writing by index.
- **Coin:** no new mint or seed calls outside the dev path (P7R-17) and the diagnostics harness.
  - The siege top-up is a real ledger transfer, with no negative balance and no double-spend.
  - Wages, ransoms and shares keep their remainders, and self-transfers and empty purses are guarded.
- **Save keys:** no duplicate or missing keys, and every new company and raid field is saved.
- **Content and game state:** nothing reads content around the loader, and no game state lives in Godot nodes.

## Health (plan §6) on this branch
- **Build:** `dotnet build Ravensguard.slnx -c Release` has **0 warnings and 0 errors**.
- **Tests:** after each merge, the area suites ran in Release (the machine is shared). After the two fix merges: **131 of 131** (battle, naval, siege and saves) and **187 of 187** (soldiers, companies, prisoners, siege, supply, count hosts, naval and saves). The full suite runs on CI after each push.
- **Godot:** `--import`, `--smoke` and `--selftest` are ok after every merge that touched the game.
- **Golden hashes:** only `phase6_trail` moved, at SC-B, AW-D, SC-C, SC-D and the P7R-1 fix, each with its reason beside the pin. No generated-world, history or `phase5_trail` line moved in Phase 7.
- **Saves:** still v8, with every Phase 7 addition an optional section or key. A save, load and save round trip is byte-identical in every spec's own test.
- **Timing tests:** AB13, AW-7, the cavalry budgets and the war soak's worst hour can miss on this loaded 4-core box. CI multiplies the budgets by 5.

## The overview's integration tests (OV-1 to OV-8, `tests/Ravensguard.Sim.Tests/Phase7Overview/`)
All eight pass on CI. On this shared box OV-1's 6,000-tick soak averages 10.9 ms (gate 25) but its worst tick of a minute is 58.5 ms (gate 33); that is the reference-PC measurement moved to Phase 8. What each asserts and its gaps against the overview's section 4:
- **OV-1:** the sim side only (2,000 soldiers' tick budget); the 60 fps frame is not measured here.
- **OV-2:** the AI host by road against the hero's levy, with every entry caused and coin, mass and people exact. The battle is abstract: no live bubble, no F1-F4, no armoury refit.
- **OV-3:** the hero's stone ring, two engines, the breach, terms and surrender on one chain, and a mid-siege round trip. The host is 400 men, not 1,200, and the storm is abstract.
- **OV-4:** points to the war soak (AW-15 and AW-16).
- **OV-5:** two longships, 70 real raiders, plunder by ledger, the bell and the beach fight, and a mid-raid save. No beach bubble, no hero boarding and no drowning.
- **OV-6:** a works founded through the hero desk, with the Industry tab model. The upgrade is refused for want of a claim, so there is no L2, no caravan quote and no payback check, and the seed's works ran with no bottleneck.
- **OV-7:** a pre-0.4.0 save loads with the coin notice and re-saves byte-identical, and a season of three coins keeps the coin book exact. The copper cap is not reached.
- **OV-8:** a 60-day war trail with six byte-identical round trips, and a run resumed at day 28 matches. `phase7_trail` is still empty, so there is no Linux/Windows compare. There is no night attack, no mid-battle save and no mercenary contract in the trail.

These gaps are in the Phase 8 list below.

## Phase 7 closed (27 Nov 2026)
Every part is merged, with AE3 and SG-E built at the close. The two review passes' findings are fixed or moved with reasons, and OV-1 to OV-8 pass on CI. The fun-check checklist is `docs/playtests/phase-7-checklist.md`, waiting on the user's session. What remains open is in the Phase 8 list below.

## Phase 7 milestones
| Done-when (plan §5) | State |
|---|---|
| 2,000 soldiers hold 60 fps on the reference PC | The sim tick is measured here. The frame rate needs the reference PC (no GPU here), so it moves to the Phase 8 measurement pass. |
| Lead levies against an army arriving by road and see the consequences | Met in the sim (AM-E): the hero's levy fights a hostile host on the road, and deaths and war score follow. |
| An AI faction besieges and breaches your walls | Met in the sim (SG-D with kits): two trebuchets breach the hero's built stone wall in 16 days and the town surrenders. The storm is abstract until the live assault is wired (P7R-4). |
| War soak | The harness, the CI part and the nightly are built (AW-E). Coin, mass and people were exact every day for 25 years, and every war's end traced to weariness or money. The consequence gates (AW-16) fail 6 of 10; **the user chose to close with them as measured and move them to Phase 8** (overview §10h). |
| Raiders land from a ship and you fight them on the beach and on the deck | In the sim: sea raids land real men (SH-C), and ships ram, burn, grapple and are boarded on deck levels (SH-D). The beach fight is the abstract band fight, and the AI does not ram or board yet. |

## Moved to Phase 8 (nothing is silently dropped)
- **War's toll (AW-16):**
  - campaign intensity (7 hosts in 9 war years);
  - AI purveyance when short;
  - war flight and return (0 refugees now);
  - emptied borders;
  - bread dearer on march routes (34% of route seasons, against a 70% gate);
  - recovery of hard-hit towns.

  Then re-run the nightly.
- **Calibration:**
  - **AW-10:** the count battle is within the gate on win rate in 31 of 40 scenarios, casualties in 6, and duration in 13. It is a named ratchet. A panic cascade was tried at the user's choice and did not help; a per-pair cascade is the next idea.
  - **AW-C:**
    - scenario E (the wider line's wrap) is not inside the gate;
    - G's duration and the wound-class mixes are named gaps;
    - H (stakes), I (the nightly 2,000 v 2,000), AW-5 (hand and foot shares) and AW-6 (plate duration) are not done.
  - **AB3:** the known-gap bound is 18, the user's decision of 26 Nov.
- **Wiring the sim into the game:**
  - the live siege assault (P7R-4);
  - the ladder and tower orders in an assault;
  - the AI that rams, grapples and boards;
  - the bell as a hero interrupt;
  - a live beach fight;
  - the helm HUD;
  - the Army tab;
  - prisoners taken from battles (`PrisonerPens.Take` has no caller);
  - the feed, sale, cede and parole-broken hooks.
- **Content and smaller gaps:**
  - the `fire_arrow` ammo;
  - the sailor occupation and the anchor, ballast, bilge-pump and oar-port parts;
  - harbours placed as generated blueprints;
  - voyages, ferries and fishing in the running world;
  - AE1 convoys for siege goods, and felling timber;
  - a raid ship's non-grain cargo;
  - `Disband`/`Fund` leftovers (P7R-9);
  - drill growing attributes;
  - `SoldierHash` in a Phase 7 trail.
- **The war wagon fort and chariots in live play** (SG-E): the fort's walls are not yet in live battles, there is no sortie order and no AI for forts or chariots, chariot archers do not shoot in a fight, there is no `cheval_de_frise`, and there is no art.
- **The arms industry in play** (AE3): orders are rows rather than supply contracts, the arms trade moves no physical goods, and there are no Fief or Armoury screens, no steward's order and no refit by commission. WL-9 and EA_F11 stay open; 960 levy kits need 2.1 t of bar.
- **Economy audit carry-overs:** EA_F25 (the L1 clay works is marginal), EA_F18 (the L3 works needs 6–13x its bill as working capital), WL-9 (needs AE3 kits) and EA_F11 (iron for kits).
- **The OV gaps above**, and `phase7_trail` filled for the cross-OS compare.
- **Performance (P7R-13):** the day's first hour against 150 ms, and the plan's budgets, are to be measured on the reference PC.
- **Kicks everywhere (CV-B):** kicks and bites run only in formation fights; enabling them everywhere moves MT-13's pinned hash. This is the user's call.

## Phase 7 decisions by the user (in `docs/specs/phase7/00-phase7-overview.md` §10)
- 10a: the spec decisions of 21 Nov.
- 10c: children at the harvest.
- 10d: levy by village.
- 10e: war costs no harvest (WL-6 waived).
- 10f: the second audit's sign-off.
- 10g: walls fall in days.
- 10h: the war soak closes as it stands.
- The AB3 bound is 18 (26 Nov).
- The count battle's panic cascade was tried and shipped off (26 Nov).
