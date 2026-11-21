# Abstract war and calibration: battles outside the bubble at 2,000 v 2,000, the ±10% gate against the tactical resolver, count battles on the continent, and the 25-year war soak

Status: design, before any Phase 7 military code, on top of b6b39e2.
Plan refs: §2.3 (the abstract tier's battle resolver for fights outside any bubble, labelled as reports in the chronicle), §4A.3 (abstract battles: a Lanchester exchange whose quality factor comes from armour and skill; output per-soldier wounds, knock-outs, routs and captures sampled from tables built by the tactical resolver; a golden test keeps abstract and tactical within ±10%), §6 (combat tests: the abstract resolver within ±10% of tactical), §4A.4 (campaigns), §5 Phase 7 ("the abstract resolver, calibrated against the tactical one within ±10%"; done-when: the **war soak**: several realms at war across the continent for 25 years, with abstract armies on the road graph, levies, supply, foraging and sieges; visible consequences: dearer bread along the march routes, unrest where purveyance was used, refugees and depopulated border regions; but no collapse: coin and mass conserved, population recovers within about 15 years of peace, every war's end traced to war-weariness, money or harvest in the chronicle). Items moved here from Phase 4 (`docs/reviews/phase-4.md`): **AB13's 2,000 v 2,000 budget (45 ms)** and the wound template library's size (0.9 MB against 300 KB); **AB-3's 17 of 45 misses** held by a ratchet (finding S9) to be replaced by the ±10% gate, needing battlefield geometry (lines, flanks, cavalry passes) and the lance rows' trample and fall; the human hand and foot at DF's 1.44% and 2.16% (waits for this re-fit; RS-29 allows 3% until then); plate against plate lasting about 21 s (target 10-20 s); `ApplyHits` as two thirds of an abstract battle's time; prisoners from abstract battles into captivity; live fights converted at the bubble edge in play. From Phase 6: the war rules' second step for history (`phase-6-open.md`, part F).

## 1. Goals
1. Every battle nobody watches is fought by an abstract resolver that agrees with the battle bubble within the plan's ±10%, at the sizes Phase 7 fights (up to 2,000 v 2,000), on a worker thread within 45 ms.
2. Battles in regions without people records (the continent's summary regions) are fought by a count resolver calibrated to the per-soldier one, cheap enough for a seasonal tick with dozens of battles.
3. The 25-year war soak runs nightly and gates the whole of Phase 7 (military and economy) on the plan's consequences and its "no collapse" conditions, with numbers.

## 2. What exists and what is new (by file)
| File (at b6b39e2) | Today | Phase 7 |
|---|---|---|
| `Sim/Combat/Abstract/AbstractBattle.cs` (757 lines), `AbstractBattles.cs` (`IAbstractFights`, `AbstractBattleService`), `AbstractClasses.cs`, `WoundTables.cs`, `WoundTableBuilder.cs`, `AbstractEquivalence.cs`, `AbstractScenarios.cs` (A, B, C, D, D on mud, A flanked, A without leaders), `BattleDemotion.cs` (a live fight converted at the bubble edge), `BattleRansom.cs`, `BattleChronicle.cs` | v1: two sides of soldiers with real wound books, steps of `step_s`, engaged attackers by a press with no geometry, each attack a draw from the matchup's table row, morale as the live terms integrated, the aftermath; 1,078 cells x 4,000 actions; 93 KB tables, a template library of 0.9 MB | **v2:** formations and geometry (4.1), the lance rows' trample and fall (4.2), the 2,000 v 2,000 work (4.3), the re-fit and the gate (4.5); v1 kept behind `AbstractBattleDef.Version` for the Phase 4 scenario tests until the gate passes |
| `tools/Ravensguard.Calibrate` (`--abstract fit`, `--abstract equiv`), `docs/calibration/abstract-vs-tactical.md`, `tests/.../Combat/Abstract/TacticalReference.cs` | the fit and the stored live tallies | **extended:** the live side is the battle bubble (formations), new scenarios E-I, the 2,000 v 2,000 nightly |
| `tests/.../Combat/Abstract/AbstractBattleTests.cs` (AB-3 ratchet: fails only on an 18th gap), `AbstractBattleBudgetTests` (100 v 100 3 ms, 200 v 200 8 ms) | | **replaced:** AW-4 (the gate, by name), AW-7 (2,000 v 2,000) |
| `Sim/Politics/Diplomacy.cs` (realm-level resolver), `Sim/Continent/ContinentTick.cs`, `WorldGen/History/HistoryGenerator.Politics.cs` (history v1's war step) | | **replaced** in play by `armies-on-the-map.md` 4.11 and the count armies (4.4); history unchanged (open question 2) |
| `Sim/Abstract/EconomySoak.cs`, `HeadlessWorld.cs`, the 100-year soak (Phase 6) | the soak harness | **extended:** the war soak (4.6) |
| **New** | | `Sim/Combat/Abstract/BattleField.cs` (blocks and fronts), `CountBattle.cs`, `CountTables.cs` (fitted from v2), `Sim/War/ContinentArmies.cs` (count hosts on continent edges), `Sim/War/WarSoak.cs` and `WarSoakGates.cs`; content `abstract/battle_v2.jsonc`, `abstract/count_battle.jsonc` |

## 3. Data
- **`BattleBlock`** (v2's formation): `{ Side, Kind (the battle spec's `FormationKind`), Shape, Frontage mm, Ranks, Members (soldier indices), X (mm along the field's axis), Lateral (mm: the block's centre across the field), Facing (towards the enemy or turned), Cohesion pm, Engaged with (blocks), Reserve (bool) }`.
- **The field** is two-dimensional but coarse: an axis between the sides' centres and a lateral coordinate; blocks are rectangles; a front is the overlap of two facing blocks' frontages; a **flank** is a block's side or rear edge reached by an enemy block (overlap beyond the frontage, or a block placed round it).
- **`CountHost`** (continent, saved in `armies`): `{ Id, Realm, Region (or edge and km), Men by class (levy, foot, missile, horse, elite: 5 counts), Quality pm by class (from the realm's armoury class and drill), Morale, Supply days, Objective, State }`.
- **`CountTables`** (`abstract/count_battle.jsonc`, generated by the calibrate tool from v2): per class pair, `beta` (outs per engaged man-minute), the engaged share by shape, the rout curve (share routing by losses and morale), the capture share, the wound mix of casualties (dead, crippled, wounded who recover).

## 4. Rules

### 4.1 Abstract battle v2: geometry
- **Deployment:** each side's soldiers are grouped into blocks the way the battle spec groups them (formations from the host's contingents and roles: infantry, missiles, cavalry, retinue; the same deploy order); blocks are placed on the field by the side's commander's Tactics rows (the battle spec's captains, the same content): a line of foot with missiles in front or on the wings and horse on the wings or in reserve; frontage = members / ranks x spacing.
- **Movement** per step (`step_s` 5 s): blocks advance at their pace (the battle spec's shape paces) until contact; missiles stop at their range and shoot; cavalry blocks charge (4.2); reserves wait until a block of theirs is under 60% or an enemy block is flanking.
- **Fronts:** engaged attackers are the block's members within the overlap of the two frontages (front rank) plus the second rank's polearms (as v1), so a wider line engages more men, and **the overlap beyond the narrower block's frontage wraps its flank**: those attackers engage its flank members (their blows take the flank bonus, Phase 4 +25% on the hit chance, no shield), and the flanked block's members get the -15 flank morale term and the battle spec's formation flank rule (100 pm hit from the side in 5 s). This is the term v1 lacked (the flanked scenario at 96% abstract against 41% live).
- **Blocks in contact** keep their targets as v1 (each attacker his opponent until out); the press numbers (`beset_*`, `press_rate_pct`, `apm`) stay table data.
- **Morale and rout per block:** the live terms (v1) plus the battle spec's formation terms (flanked, the cascade at 500 pm routing, the neighbour within 30 m, the shield wall's +5, the captain's aura); a block whose cohesion falls under 600 (members out from its front) loses its shape bonus.
- **Ending, aftermath, prisoners:** as v1, plus: prisoners from abstract battles go into captivity (`soldiers-and-companies.md` 4.7: the Phase 4 item "prisoners from abstract battles into captivity"), dead soldiers' kit stays on the field as loot for the holder (the economy specs' recycling), and the report names the blocks.

### 4.2 Cavalry passes, trample and fall (the lance rows)
- A cavalry block's charge is a **pass**: at contact each rider draws one blow from the charge rows (the existing lance cells, closing speed by footing and slope as the cavalry spec) **plus**, new, a **trample** draw against the man he overruns (the horse's trample rows, built by the calibrate tool from the live trample) and a **fall** draw for himself (refusal, unhorse and the horse brought down by braced polearms: rows from the live charges into braced and unbraced lines, by the defenders' braced share). Shock (cavalry spec 4.1: -15 to foot near a cohesive charge of 10+) applies to the struck block.
- Then the pass rides through (riders above 4 m/s by the rows) and re-forms behind in 3 steps (15 s), or sticks in a melee (v1's rider melee rows).
- **Mud and the bogged rider:** the D-on-mud gap (v1's bogged rider too harmless) is fitted by the rider's melee rows at the mud footing's speed (the cells exist; the press reads the footing).

### 4.3 2,000 v 2,000 within 45 ms (the AB13 budget moved here)
On the worker thread (the abstract tick's battle job, applied at the barrier), measured with `TestBudget.BestPass` in the Timing collection:
- **Only engaged soldiers draw.** A step visits each engaged attacker (about 40% of the field at most: 1,600 of 4,000) and each shooter whose reload is due; everyone else costs nothing but the block's integer morale terms.
- **Wound books are advanced lazily:** a soldier's `WoundBook.AdvanceTo` runs only when he is struck (to the hit's second, then the hit applied: v1's exactness kept) and at the end of the step only for books with an open wound (`WoundBook.HasOpenWound`, a flag kept by the book), not for the thousands unhurt; this keeps the results identical to v1 on v1's scenarios (AW-1) and removes most of `ApplyHits`'s two thirds.
- **The template library** (recorded wound sets copied into books) is **hash-consed**: identical sets stored once and cells keep indices; the target is <= 300 KB (from 0.9 MB) with the same draws (AW-8).
- **Morale per block, with per-soldier terms only where they differ** (pain, blood, flags): integrated per step for members by a shared block term plus their own.
- Budget gate: 100 v 100 <= 3 ms, 200 v 200 <= 8 ms (unchanged), **2,000 v 2,000 <= 45 ms**, reference PC; the review box's margin as Phase 4's.

### 4.4 Count battles and count hosts (the continent)
- **Count hosts** (`ContinentArmies`) move on the continent's land and sea edges at the armies spec's speeds (3.7 km a game day marching; sea by the ships spec's voyage times), eat from their realm's summary stores (grain mass moved in the summary's books, so mass is conserved), forage the summary regions they cross (a summary region's grain store and next harvest lowered by the forage rule's kg), and besiege (a summary town's stock in days from its summary: starvation, or storm with a siege-days estimate).
- **A count battle** (`CountBattle`) between count hosts (or a count host and a summary region's levy) is the Lanchester square law in 60 s steps by class: `dA_c / dt = - sum over B classes of beta(B_k, A_c) x engaged(B_k)`, engaged by shape share from the tables, with rout by the fitted curve and captures by the capture share; casualties split into dead, crippled and recovering by the tables' wound mix; the summary regions' populations lose the dead (men of the households abroad: counts), and the crippled lower the region's labour (a closed-form term). <= 0.2 ms a battle.
- **Calibration:** the count tables are fitted from v2 runs (the calibrate tool `--count fit`) over 40 scenarios of class mixes and sizes 200 to 4,000 a side, and gated within ±10% of v2 (casualties, win rate, duration) on fresh seeds (AW-10).
- **Entering a detailed region,** a count host becomes people at the gate (armies spec 4.4: persons made by 0x7A3, exactly its counts); leaving, people become counts. A battle in a detailed region between people is always v2 (or live); a count battle never touches a person.

### 4.5 The ±10% gate (replacing the AB-3 ratchet)
- **The definition** is Phase 4's (`abstract-battles-v1.md` 4.4, B1), unchanged: across 200 fresh seeds a scenario, mean casualties (dead + down) per side within **±10%**, win rate within **±10 points**, mean duration within **±25%**, the per-casualty wound class mix within **±10% relative** (classes under 5% share within **±3 points** absolute), bled-out-after within **±10%**. Plus one floor for the small numbers of the small scenarios: a casualty mean under 2.0 men is compared within **±0.3 men** absolute (A's raiders at 2.6 are not; C's line at 3.9 is not).
- **The live side is the battle bubble** (formations and captains on, cavalry spec rules on), re-fought for the stored tallies (`TacticalReference`), for the seven Phase 4 scenarios plus five new: **E** (200 v 200: two foot lines, the wider by 40%, open field: the flank wrap), **F** (60 knights charge 200 unbraced levies), **G** (60 knights charge 150 braced pikes in a schiltron), **H** (100 longbowmen behind stakes and 100 men-at-arms against 80 knights: Agincourt in small), **I** (2,000 v 2,000 mixed hosts: nightly only, 50 seeds, the live runs on the reference PC or the nightly box).
- **The test names its comparisons:** AW-4 lists each scenario x metric with its band; a fixed gap that opens again fails by name (S9's fault removed).
- **Fixes bundled with the re-fit:** the human hand and foot to DF's 1.44% and 2.16% (RS-29's 3% allowance removed); plate against plate's duration brought into 10-20 s by the re-fit of the press constants for plate classes (the live duel is the reference; if the live itself is outside, the finding goes to the battle spec, not the abstract fit).

### 4.6 The 25-year war soak (Phase 7's done-when) and its gates
**Harness:** a headless continent world (seed set of 10 worlds, nightly; one fixed world, seed 1234, in CI weekly), the home region detailed with its people, two neighbouring regions detailed (seamless: generated as the bot travels), every other region summary; history v1; every Phase 2-7 system on, both economy and military; the hero bot (autopilot) living as a lord who musters for his liege; 25 game years (1,500 game days) of war and peace from the realms' own utility. The gates (`WarSoakGates`), each with its number:
| Gate | Measure | Pass |
|---|---|---|
| **Wars happen** | realm-pairs at war, share of neighbouring pair-seasons | 5-35% (Phase 6 DA-2's ceiling 35%); at least 3 wars declared over the 25 years in each world |
| **Dearer bread on march routes** | grain price in settlements within 8 km of a host's route during its campaign season, against the same settlements' mean of the 3 years before the war | **+15% or more** in at least 70% of such settlement-seasons; back within 10% of the baseline 2 seasons after the host has gone |
| **Unrest where purveyance was used** | settlements purveyed (economy specs' events) | unrest **+100 or more** (of 1,000) within 30 days in at least 80% of them; no such rise in settlements never purveyed or foraged (control group: mean change under +30) |
| **Refugees** | households leaving settlements that were foraged, raided, sacked or besieged, within a season | **5% or more** of their households, as migrants with a `War` or `Raid` cause, arriving somewhere else (counted at both ends) |
| **Depopulated borders** | population change over the war years of regions a war front touched (hosts campaigned in) against interior regions of the same realms | border regions lose **10% or more** relative to interior ones |
| **No collapse** | across every realm and region | no realm's population under 50% of its start except by conquest (land changing hands is counted separately); no detailed settlement with zero grain in stores and market for more than 30 consecutive days outside a siege; markets open on 90%+ of market days in every detailed town; no realm with loans above 4 years of receipts in more than 5% of realm-years; the region's idle adults under the Phase 6 gate |
| **Coin conserved** | the Ledger audit every game day | exact (sources and sinks balance; no `Burn` outside tests, Phase 6 S10 fixed by the economy audit) |
| **Mass conserved** | `MassLedger` every game day, including foraging, baggage, sacks, ships and summaries | exact |
| **People conserved** | births - deaths - departures + arrivals = change, with levies, hosts made at gates and unmade, bandits and prisoners | exact |
| **Recovery** | regions that lost 10%+ of their population to war: population 15 years after the last war touching them | **95% or more** of the pre-war population (or the land's carrying cap, whichever is lower) |
| **Every war's end traced** | each `realm_peace` entry's cause chain | 100% reach one of: war-weariness (`ch_note_dp_peace_weary`), money (new `ch_note_war_money`: the treasury under one season of payroll in the loser's weariness), harvest (new `ch_note_war_harvest`: the harvests a levy host was kept) |
| **Chronicle** | specific cause coverage over the soak (Phase 6 gate) | >= 0.90 |
| **Budgets** | the worst abstract hour; the seasonal continent tick with war | <= 150 ms; <= 500 ms |
| **Determinism** | the soak's daily hash trail on Linux and Windows for 2 game years (CI) | equal |
**Run time:** 25 years headless in <= 2 hours on the reference PC (about 4.8 s a game day), nightly; CI runs 2 years of seed 1234 and the conservation and determinism gates.

## 5. Tick placement
| Where | What |
|---|---|
| Abstract tick (worker) | v2 battles in detailed regions (between hosts, warbands, raids not watched), applied at the barrier |
| Continent tick (seasonal, worker) | count hosts move, forage and besiege; count battles; the war score's events from them (armies spec 4.11) |
| Barrier | a live fight converted at the bubble edge (`BattleDemotion`, the Phase 4 code wired in play: the Phase 4 item); an abstract battle promoted to live when the player comes within 150 m at its start (v1 rule) |
| Nightly | the 2,000 v 2,000 gate; the war soak |

## 6. RNG streams (0x7E0..0x7EF)
| Stream | Use | Key |
|---|---|---|
| 0x7E0 | v2 block placement ties and reserve release ties | battle, side |
| 0x7E1 | trample and fall draws in a pass | battle, rider, step |
| 0x7E2 | count battle: rout and capture rounding draws | battle, step, class |
| 0x7E3 | count host foraging and siege rounding | host, day |
| 0x7E4 | war soak: the bot's choices (autopilot draws its own streams; this is the soak's world variation) | world, year |
| 0x7E5..0x7EF | free |
Unchanged: 0x4A0..0x4A3 (v1's and v2's engagement, wound sample, rout, leader).
**The military side's block** (all seven specs): 0x780 battle bubble, 0x790 cavalry, 0x7A0 armies, 0x7B0 soldiers and companies, 0x7C0 sieges, 0x7D0 ships, 0x7E0 abstract war, each 16 wide; **0x7F0..0x7FF unassigned** (reserve). The economy specs use the Phase 7 block below 0x780. All are registered in `Core/RngStreams.cs` with the uniqueness test.

## 7. Saves
No new section: v2 battles are transient (fought within one abstract job; a battle never spans a save); count hosts live in the `armies` section (`armies-on-the-map.md` 7). Tables are content (hash-checked; rebuilt by the calibrate tool; the stale-table test AB-1 extends to the count tables). v8 stays.

## 8. Chronicle
v2 and count battles write the existing `battle_report` type ("word reached Oakford...", `ch_report_battle`) with the blocks in its parameters (new optional params `blocks_a`, `blocks_b` as counts); a count battle on the continent writes `battle_report` only when it involves a realm that touches the detailed region or a named lord, otherwise the continent's news item (Phase 2 continent news). New note keys for peace causes: `ch_note_war_money`, `ch_note_war_harvest` (rows in `types.jsonc` for `realm_peace`'s `note_keys`).

## 9. UI
No new screen. The battle report (Phase 4 after-fight report and the Chronicle's entry) gains a block diagram: two lines of blocks at their deployment, arrows for charges and flanks, losses per block (960 x 540, the report's existing panel). The calibration lives in `docs/calibration/abstract-vs-tactical.md` (rewritten by the tool's run, as Phase 4).

## 10. Golden tests
| id | Case | Expected |
|---|---|---|
| AW-1 | v2 equals v1 where there is no geometry | v1's scenarios run with the lazy books and the hash-consed library: every battle's hash identical to v1 (the speed-ups change nothing) |
| AW-2 | Flank wrap | E: the wider line's overlap engages the flank; flanked members carry the term; abstract win share within the gate of the live E |
| AW-3 | Passes | F and G: trample and fall draws; G's riders fall and refuse at the live rates within ±10%; shock applied in F, not in G's braced block |
| AW-4 | **The ±10% gate** | A, B, C, D, D on mud, A flanked, A without leaders, E, F, G, H on 200 fresh seeds each: every metric of every scenario inside its band (listed by name in the test) |
| AW-5 | Hand and foot | the human plan at 1.44% and 2.16%; RS-29's allowance gone; AW-4 still passes |
| AW-6 | Plate against plate | the abstract duel's mean duration within ±25% of the live's; the live within 10-20 s or a finding filed |
| AW-7 | **2,000 v 2,000 budget** | <= 45 ms (BestPass, Timing collection); 100 v 100 <= 3 ms; 200 v 200 <= 8 ms |
| AW-8 | Template library | <= 300 KB after hash-consing; every draw's template the same as before (AW-1) |
| AW-9 | Scenario I (nightly) | 2,000 v 2,000 mixed: casualties, win rate and duration within the gate against 50 live battle-bubble runs |
| AW-10 | Count battle calibration | 40 scenarios: count against v2 within ±10% casualties, ±10 points win rate, ±25% duration |
| AW-11 | Count hosts conserve | a season of count hosts marching, foraging and fighting across 6 summary regions: grain mass, people and coin exact |
| AW-12 | Gate crossing | a count host of 800 enters a detailed region, fights a v2 battle, 610 leave: counts and people exact both sides |
| AW-13 | Prisoners and loot from abstract battles | the loser's yielded go to captivity with rank classes; kit left on the field as loot rows |
| AW-14 | Conversion at the bubble edge | a live battle whose last formations leave the bubble is converted (`BattleDemotion`) and finished abstractly; results continuous |
| AW-15 | **War soak (CI part)** | seed 1234, 2 game years: coin, mass and people exact; determinism trail equal on Linux and Windows; worst hour <= 150 ms |
| AW-16 | **War soak (nightly)** | 10 worlds x 25 years: every gate of 4.6 passes in every world (a world that fails one gate fails the night, with the gate named) |

## 11. Budgets
v2: 2,000 v 2,000 <= 45 ms, 200 v 200 <= 8 ms, 100 v 100 <= 3 ms (worker); count battle <= 0.2 ms; count hosts <= 20 ms a season over the continent (inside the 500 ms seasonal tick with the economy's share); tables <= 120 KB, template library <= 300 KB; soak run <= 2 h for 25 years.

## 12. Open questions (recommended defaults)
1. **The gate's small-count floor** (±0.3 men under a mean of 2.0): Phase 4's definition is kept, the floor added for tiny means only. Default as written.
2. **History's wars:** history v1 keeps its own war step (its seasons of realm-level battles); Phase 7 does not regenerate library worlds' histories with armies, and new worlds keep v1 too (a history v2 with count armies would change every world's hash and cost generation time). Default: history unchanged; play uses armies. (For the user: new worlds could get a history fought by count armies at the cost of about 1-2 minutes more generation and a new history version.)
3. **The soak's thresholds** (+15% bread, +100 unrest, 5% refugees, 10% border loss, 95% recovery in 15 years) are the plan's consequences made numbers; if the economy audit finds them wrong for our scale they move with a reason in the review. Default as written.
4. **Scenario I's live reference** needs the reference PC or a nightly box for 50 runs of a 2,000 v 2,000 battle bubble (about 75 minutes). Default: nightly, not CI.

## 13. Build parts and estimate
| Part | Content | Gate | Months |
|---|---|---|---|
| AW-A v2 geometry and passes | blocks, deployment by the captains' rows, fronts and the flank wrap, reserves, block morale, the trample and fall rows (the calibrate tool's new rows), prisoners and loot | AW-1 to AW-3, AW-13 | 1.3 |
| AW-B scale and the library | lazy books, the open-wound flag, hash-consing, block morale with per-soldier terms, the budget tests | AW-7, AW-8 | 0.7 |
| AW-C the re-fit and the gate | new scenarios E-I against the battle bubble, the fit, hand and foot, plate durations, AW-4 by name replacing the ratchet, the calibration doc | AW-4 to AW-6, AW-9 | 1.0 |
| AW-D count armies | `CountHost` on continent edges, foraging and sieges in summaries, `CountBattle` and its tables, gate crossings, the war score's events from the continent | AW-10 to AW-12 | 1.1 |
| AW-E the war soak | the harness, `WarSoakGates`, the CI part and the nightly, conversion at the bubble edge wired in play | AW-14 to AW-16 | 0.8 |
| **Total** | | | **4.9 months of code** (AW-C and AW-E need the battle bubble, cavalry, armies and the economy's purveyance first) |
