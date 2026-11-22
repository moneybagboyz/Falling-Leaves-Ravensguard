# Cavalry and mounted war: cavalry formations, the charge at scale, braced spears, terrain, unhorsing, dismounting

Status: design, before any Phase 7 military code, on top of b6b39e2.
Plan refs: §3e (mounted combat through one resolver: a charge adds the horse's momentum; the horse is a large target; a rider whose horse falls takes fall damage and a heavy hit unhorses; cavalry is countered by braced spears and pikes, shield walls, and terrain: mud, slopes and woods), §2.6 (a mounted charge adds the horse's momentum; no mounted-only damage rule), §3d (formations: cavalry; order "set against cavalry"), §3h (chariots charge the same way: `sieges-and-engines.md` 4.9), §5 Phase 7 ("cavalry formations and charges"). Phase 4 carry-overs (review `phase-4.md` "Moved"): mounting and dismounting in a fight, horse kicks, mounted bandit leaders, fire panic, fog, branches in woods, drowning; the weather fields slip and cavalry speed; the abstract tables' lance rows (trample and fall) are `abstract-war-and-calibration.md` 4.2's.

## 1. Goals
1. Cavalry fights as formations: a wedge or a line of riders knee to knee that walks, trots, gallops and charges together, so that a cohesive charge is a different thing from twenty riders arriving one by one.
2. Every rider's blow in a charge is still one `ResolverV2` resolution with `ChargeMath.ExtraMilli`: the formation adds **cohesion and shock** (morale), never damage.
3. The counters are physical and legible: braced spears and pikes in a schiltron (refusal and the brace return), shield walls (the push rule), stakes and ditches, mud, slopes, woods, fog and night, and men-at-arms who dismount to hold a line.
4. Close the Phase 4 mounted carry-overs that matter at scale.

## 2. What exists and what is new (by file)
| File (at b6b39e2) | Today | Phase 7 |
|---|---|---|
| `Sim/Combat/Mounted/ChargeMath.cs` | `ExtraMilli` (`charge_k x kg x v_c x weapon_pm`), `BraceReturnMilli`, `TrampleMilli`, `FallMilli`, `UnhorseBp`, `RefusalPm`, `AimPoints`, `RearThrowPm`, `PanicOf` | **unchanged** (pure functions; this spec only feeds them) |
| `Sim/Combat/Mounted/FightWorld.Mounted.cs` (849 lines), `MountedBrain.cs`, `MountedRide.cs`, `MountedLayer.cs`, `MountedTypes.cs` | horse and rider as paired `Fighter`s; motion (accel 7.5, brake 6 m/s², turn 60/150/270 deg/s); couch 3 ticks; unhorse, falls, pins; brace and refusal rolled once per approach at 8 m; panic; trample | **extended:** formation slots for riders (3.1); `MountedBrain` follows a cavalry formation's order; dismount and mount in a fight (4.5); kicks (4.6); horse archers (4.7); neighbour scans through `BattleGrid` |
| `Sim/Combat/Mounted/TerrainFooting.cs` (`Footing`, `FightTerrain`) | base footing, zones, a plane slope, heavy rain, thunderstorm; built from the Phase 3 surface under the hero | **extended:** `Footing` +`Stakes, Ditch, Rubble` (append only); built per 4 m cell for the battle box from the chunks (not a plane); weather slip and speed from `RegionWeather` (4.8) |
| `Sim/Animals/RideBook.cs`, `RideMath.cs`, `Animal.cs` | horses as animal rows, riding, fodder | read: remounts are animal rows in the baggage (armies spec) |
| `content/base/combat/mounted.jsonc` | the Phase 4 tuning | **extended:** a `cavalry` block (section 3.2) |
| **New** | | `Sim/Battle/Cavalry.cs` (charge sequencing, cohesion, shock, rally), `Sim/Battle/HorseHolders.cs`, content `battle/cavalry.jsonc`, build pieces `stake_row`, `ditch_shallow` (Phase 5 build system), art: Field 16x16 rider minis (light horse, knight), the wedge block icon |

## 3. Data

### 3.1 Cavalry formation shapes (`battle/formations.jsonc` rows, read by the formation layer of `battle-bubble-and-formations.md`)
| Shape | Spacing (lateral x rank) | Ranks | Turn (gallop / trot / walk) | Use |
|---|---|---|---|---|
| Line (cavalry) | 1,500 x 4,000 mm (knee to knee: a horse is about 1 m wide) | 2 | 30 / 60 / 120 deg/s about the centre | the charge in line |
| Wedge | rank r holds `1 + 2r` riders, 1,500 x 4,000 mm | 3-5 | 20 / 45 / 90 | a point that breaks a line; the best knights in front |
| Loose (cavalry) | 4,000 x 6,000 mm | 1-2 | free | light horse, horse archers, scouts |
| Column (cavalry) | 2 abreast, 4,000 mm | n/2 | 45 / 90 / 180 | the road |
Slot priority: the best-armoured (barding first), then Riding, then weapon skill, then id, in the front rank; a wedge's point is the captain's.

### 3.2 Constants (`mounted.jsonc` `cavalry` block)
| Name | Value | Meaning |
|---|---|---|
| `charge_steps` | walk to 200 m, trot to 120 m, gallop from 60 m, couch at 20 m | the charge sequence (4.1) |
| `cohesion_shock_pm` | 700 | cohesion needed for shock |
| `shock_min_riders` | 10 | riders in contact within 2 s for shock |
| `shock_x100` | 1500 | morale lost by foot within 5 m of a cohesive charge's contact |
| `shock_braced_x100` | 500 | the same for braced foot in a shield wall or schiltron |
| `breakthrough_mmps` | 4000 | a rider still faster than this after contact rides through |
| `ride_on_m`, `reform_m`, `reform_s` | 30, 80, 15 | after a breakthrough: ride on 30 m, re-form 80 m behind in 15 s |
| `stuck_s` | 4 | a charge below 4 m/s for 4 s is a melee |
| `holders_per_horses` | 4 | one horse-holder per 4 dismounted riders |
| `mount_ticks`, `dismount_ticks` | 80, 40 | 4 s to mount (2 s with Riding 60+), 2 s to dismount |
| `kick` | blunt, area 60 cm², `M = 60 x horse kg` milli (28,800 for a courser), rear arc 90 degrees, reach 1,200 mm, cooldown 3 s | 4.6 |
| `horse_archer_aim` | -10 points at a trot, -20 at a gallop (`ChargeMath.AimPoints` reduced by Riding) | 4.7 |
| `stake_refusal_pm` | 250 per stake row cell in the 6 m arc | 4.4 |

## 4. Rules

### 4.1 The charge as a formation order
- Ordered `Charge` (player or captain), a cavalry formation **walks** while farther than 200 m, **trots** (5.2 m/s) to 120 m, **gallops** from 60 m and couches (or raises its swords) at 20 m. Each rider's speed is his horse's own (`MountedRide`), so a mixed formation strings out unless the anchor brakes it: in a charge the anchor runs at the 20th-percentile gallop of the front rank (anchor braking, the battle spec 4.3), and cohesion falls when riders cannot keep their slot within 2 m.
- **Every blow is a Phase 4 blow:** the closing speed is each rider's own (the 4-tick position delta on the line to his target), `ChargeMath.ExtraMilli` adds the momentum (couched lance 1,000 pm, a polearm 600, a sword 350), unhorse and trample as Phase 4. The formation changes only which target is in front of whom and the morale terms below.
- **Shock (morale only):** when at least 10 riders of a formation with cohesion >= 700 make contact within 2 s, every enemy foot soldier within 5 m of the contact points takes -15 morale once (-5 if braced in a shield wall or schiltron with cohesion >= 600). A ragged charge (cohesion < 700 or fewer than 10 riders) gives no shock. This is the term that lets heavy horse break levies without anything new in the resolver.
- **After contact:** a rider still above 4 m/s rides through (`ride_on_m` 30) and the formation re-forms 80 m behind its contact line within 15 s (it turns at its gallop turn rate: a committed line); riders below 4 m/s for 4 s are in a melee (the cavalry brain's Phase 4 fight states). The order `Charge and rally` (default) re-forms; `Charge and press` stays in.

### 4.2 Choosing what to charge (cavalry captains and the AI)
The Phase 4 cavalry brain's rules lifted to formations (captains' Tactics rows of the battle spec 4.7): charge a formation whose estimated refusal is under 400 pm (Phase 4 `ai_avoid_pm`) and whose lane is clear for 30 m; avoid braced lines and shield walls; prefer their flank or rear (Tactics 50) and archers and routers (Tactics 0: always); never charge over footing that forbids a charge (4.8). Tactics 70: feint at a line to draw a schiltron out of shape, then charge its neighbour.

### 4.3 The horse as a target, unhorsing and falls (Phase 4, unchanged)
Unhorsing on any blow (`UnhorseBp`, hooks x2), falls (`FallMilli`: 91,200 milli from a gallop), the pinned leg (60%, 4 s, 40,000 milli crush), panic from wounds (rears at 600, bolts at 900), refusal (`RefusalPm`, rolled once per approach at 8 m). **At scale:** a fallen horse is a 2 m x 1 m obstacle cell in `BattleGrid` (riders behind it must turn or jump: a rider at a gallop who hits it falls with 300 pm (0x790); a trained horse (`trained_pm` >= 900) jumps it with Riding 40+). Riderless horses bolt off the field (Phase 4: `Fled` at once) and become loot after the battle (`armies-on-the-map.md` 4.9).

### 4.4 Braced spears, schiltrons, shield walls, stakes and ditches
- **Set against cavalry** (order or captain at Tactics 30): spear, pike, bill and javelin bearers brace (Phase 4 `FAct.Bracing`); the formation takes `Schiltron` shape if ordered so, or braces in its current shape. A braced polearm strikes the horse entering its reach with `M0 x skill + brace_return x charge` (Phase 4: pikes at 4.5 m beat lances at 3.6 m).
- **Refusal** counts the braced polearms within 6 m of the nearest in the horse's path (Phase 4 rule) from the grid, plus **stakes**: a `stake_row` cell in the 6 m arc adds 250 pm. A horse that runs onto a stake row at >= 4 m/s is struck as by a braced spear with `M0` 120 (an oak stake) and no wielder's skill term.
- **Stakes** (`stake_row`, a Phase 5 build piece: 1 m, 4 sharpened oak stakes, 12 kg of logs, 0.2 worker-hours with an axe) are planted by archers in front of their line: 20 s a cell in the battle bubble, faster with `Drill`. **Ditches** (`ditch_shallow`, 1 m wide, 0.6 m deep, 1.5 worker-hours with a spade): footing `Ditch` (no charge; a gallop into it falls with 400 pm).
- **Shield walls** stop a charge by the Phase 4 push rule (braced bearers with 2 neighbours take a third of the shove) and cost the shield condition; a horse does not trample a braced wall (Phase 4).

### 4.5 Dismounting and mounting in a fight (Phase 4 carry-over)
- Order `Dismount` (formation) or E (the hero): the rider takes 2 s to get down (`dismount_ticks` 40), then fights on foot in the formation's foot shape (a dismounted cavalry formation becomes `Infantry` with its own slots). One in four dismounted riders (the lowest-ranked, `holders_per_horses`) becomes a **horse-holder** who leads four horses 60 m to the rear and holds them (he does not fight; a holder charged by enemies lets go and the horses bolt).
- `Mount`: each man walks to his horse (the holder brings them forward at the order) and mounts in 4 s (2 s at Riding 60+); a mounting man defends at x0.5 block coverage and cannot attack.
- AI and captains dismount men-at-arms to hold a line against cavalry or to fight in woods, on a wall or a ship (Tactics 50 rows), and when 300 pm of their horses are wounded.

### 4.6 Horse kicks and war horses (Phase 4 carry-over)
A war-trained horse (`trained_pm` >= 900) or a panicked one (panic >= 600) kicks a foe within 1.2 m in its rear 90 degree arc every 3 s at most: a blunt blow through the resolver (area 60 cm², `M = 60 x kg` milli, 28,800 for a 480 kg courser), the target's lower body or upper body (60/40, 0x791). A trained destrier also bites forward at a man pressing its rider (area 20, `M = 8 x kg`, edge-less: blunt), once each 5 s.

### 4.7 Horse archers and mounted crossbows
Light horse with bows (a loadout of `soldiers-and-companies.md`) shoot at a trot or walk, never at a gallop: aim from `ChargeMath.AimPoints` (-10 points at a trot reduced by Riding / 150); reload as the Phase 4 bow; the formation keeps 60 to 120 m from foot and wheels away when foes close to 40 m (Tactics 30 row: skirmish). Mounted crossbowmen dismount to reload (crossbow reload is a two-handed act).

### 4.8 Terrain, weather and the battle box (Phase 4 carry-overs: slip, cavalry speed, fog, branches, fire panic, drowning)
- **Footing per 4 m cell** for the whole battle box from the chunks' surfaces (road, field, rough, mud, snow, ice, woods, ford, deep water) and the new pieces (stakes, ditches, rubble from breaches); slope from the cell heights (the Phase 1 heights) instead of a plane: uphill `v x (1 - 3 x slope)` (min 0.3), downhill `v x (1 + 1.5 x slope)` (max +20%), downhill charge +10% `v_c` (Phase 4).
- **Weather** from `RegionWeather` at the battle's cell: rain over 2 mm/h turns field to mud for horses after 3 hours (Phase 3 mud), heavy rain x0.8 field speed and x1.5 slip, snow over 5 cm `Snow`, frost below -2 C on wet ground `Ice` (the cavalry-speed and slip fields Phase 4 left unread). Thunder +100 panic every 30 s and doubled refusal (Phase 4).
- **Fog and night:** a cavalry formation charges only formations its members can see (fog density from the weather; night sight 60 m or the light grid); a charge ordered at an unseen target walks until it is seen.
- **Woods:** branches knock a galloping rider with an overhead blow (`M` 40,000 milli, the head or upper body) once per 20 s per rider with 50 pm a second (0x792); no charge (Phase 4).
- **Fire:** a horse within 8 m of a burning cell (`Fire` cells) gains +200 panic a second (Phase 4 constant, now fed).
- **Deep water:** a horse swims at 1 m/s; a rider with more than 10 kg of armour sinks and drowns by the breath rule of `ships-and-naval-war.md` 4.7 unless he gets free of the horse (Athletics 40 or 4 s).

### 4.9 Mounted bandit leaders (Phase 4 carry-over)
Bandit bands of 12 or more have a mounted leader (a palfrey or a stolen courser) from the band's loot; travel encounters and the armies spec's bandit-chasing warbands meet them. No new rule: a rider in a Phase 4 fight.

## 5. Tick placement
Live 20 Hz in the battle bubble: the formation layer (charge sequence, cohesion, shock) before the brains; the mounted layer as Phase 4. Footing cells are built when the box opens (<= 30 ms on a worker, off the frame) and updated when a piece is built or a breach made.

## 6. RNG streams (0x790..0x79F)
| Stream | Use | Key |
|---|---|---|
| 0x790 | a galloping rider meeting a fallen horse: fall or jump | battle, rider, obstacle |
| 0x791 | a kick's or bite's target part | battle, horse, tick |
| 0x792 | branches in woods | battle, rider, second |
| 0x793 | horse-holder choice ties | battle, formation |
| 0x794..0x79F | free |
Unchanged: Phase 4 0x470 (lance shatter, bolts), 0x471 (refusal), 0x472 (fall parts), 0x473 (slips).

## 7. Saves
No new section: cavalry state is in the battle's formation rows (`battle`) and the Phase 4 mounted fight record (`MountsSave`), which gains additive keys: holder id per horse, the charge stage per formation, the footing cells' build hash (the cells are rebuilt from chunks on load). `Footing` grows append only. v8 stays.

## 8. Chronicle
| Type | Key | Params | Cause |
|---|---|---|---|
| `cavalry_charge_broke` | `ch_cv_charge_broke` | formation's lord, broken formation's lord, place | the battle entry (written only when a charge routs a formation of 50 or more within 20 s) |
| `charge_refused` | `ch_cv_charge_refused` | lord, place, braced count | the battle entry (a charge of 20+ riders stopped by refusals) |

## 9. UI and keys
No new keys. The order wheel's Shape ring gains Wedge (cavalry formations only); the inner ring's Charge shows "Charge and rally" with Tab-to-toggle "and press". The formation card of a cavalry formation shows its gait, cohesion and the expected refusal against the formation under the cursor ("braced pikes ahead: about 45% of the horses will balk") from `RefusalPm` with the counts it would see: the plan's tactics tooltip. Dismount / Mount are wheel orders and, for the hero, E as in Phase 4.

## 10. Golden tests
| id | Case | Expected |
|---|---|---|
| CV-1 | Charge sequence | a 20-rider line ordered to charge from 300 m: walks to 200, trots to 120, gallops from 60, couches at 20 m (tick of each change +-2) |
| CV-2 | Same blow as Phase 4 | a rider of a charging formation and the Phase 4 single rider (`mounted-combat.md` worked example 4.7) at the same speed and target: identical `BlowResult` |
| CV-3 | Shock | 12 riders, cohesion 800, into 40 unbraced levies: -15 to those within 5 m; at cohesion 600 none; braced shield wall -5 |
| CV-4 | Breakthrough and re-form | riders above 4 m/s ride on 30 m and the formation re-forms 80 m behind within 15 s |
| CV-5 | Schiltron | 60 pikes braced in a ring against 30 coursers: refusal per `RefusalPm` with the grid's counts; the braced strikes land first (4.5 m vs 3.6 m) |
| CV-6 | Stakes | a stake row adds 250 pm refusal per cell in the arc; a horse running on a stake at 8 m/s is struck with `M0` 120 |
| CV-7 | Dismount | 20 men-at-arms dismount: 5 holders take 20 horses 60 m back; 15 fight on foot in a line; mounting takes 4 s each (2 s at Riding 60) |
| CV-8 | Kicks | a trained courser kicks a man 1 m behind once each 3 s; blunt blow through the resolver, 28,800 milli |
| CV-9 | Horse archers | 15 horse archers keep 60 to 120 m from foot and wheel away at 40 m; aim at a trot -10 x (1 - Riding/150) |
| CV-10 | Footing cells | a box with a muddy stream bottom: no charge across it; downhill charge +10% `v_c`; a ditch at a gallop falls 400 pm |
| CV-11 | Weather | 3 h of rain over 2 mm/h: field reads mud for horses; frost on wet ground reads ice; fog stops a charge at an unseen target |
| CV-12 | Fallen horse obstacle | a horse down in the path: a galloping rider behind falls or jumps by 0x790; trained 900 with Riding 40 jumps |
| CV-13 | Scale budget | 300 cavalry (600 fighters) inside the 2,000-soldier BB-5 run: the mounted layer <= 0.8 ms average |
| CV-14 | Determinism | a 400 v 400 battle with 120 horse: fight and mounted hashes equal across runs and across a mid-battle save and load |

## 11. Budgets
Mounted layer with 300 riders and 300 horses <= 0.8 ms a tick average (inside the battle ledger); footing cells for a 1 km² box (62,500 cells x 1 byte) <= 30 ms to build on a worker, 64 KB; refusal and kick checks via the grid, 0 allocations.

## 12. Open questions (recommended defaults)
1. **Does a formation make a charge hit harder?** No: cohesion buys shock (morale) and keeps riders together; each blow is the single rider's resolver blow. Default as written (the plan's one resolver).
2. **Horses in the battle-size cap:** the cap counts soldiers (people); horses are extra fighters, and the budget test carries 300. Default as written.
3. **Stakes as a build piece in battle** (archers plant them in 20 s a cell). Default yes; only archers with a `stake` item in their loadout (`soldiers-and-companies.md`).

## 13. Build parts and estimate
| Part | Content | Gate | Months |
|---|---|---|---|
| CV-A formations and the charge | shapes, charge sequence, cohesion, shock, breakthrough and re-form, cavalry captains, refusal through the grid | CV-1 to CV-5, CV-13 | 1.0 |
| CV-B dismount, kicks, horse archers, obstacles | holders, mounting in a fight, kicks and bites, horse archers, fallen horse cells, mounted bandit leaders | CV-7 to CV-9, CV-12, CV-14 | 0.7 |
| CV-C ground and weather | footing per cell, slope from heights, stakes and ditches as pieces, weather slip and speed, fog, branches, fire panic | CV-6, CV-10, CV-11 | 0.5 |
| **Total** | | | **2.2 months of code** (+ art: rider minis, wedge icon, stake and ditch pieces) |

## Notes (P7-CV-A, built)
- **Where it lives:** `Sim/Battle/Cavalry.cs` (a partial of `FormationBook`: slots, the anchor's pace, the charge, contact, shock, the rider, the cavalry captain, the cavalry tail of the save), `FormationRow.cs` (the cavalry fields), `Slots.cs` (the wedge), `FormationTypes.cs` (`Shape(f, kind)`, `Cav`, `OrderKind.Wedge`), content `battle/cavalry.jsonc` (`cavalry_rules`, read through `CavalryRulesDef`), the `wedge` and `cavalry_line`, `cavalry_loose`, `cavalry_column` rows of `formations.jsonc` (with `turn_trot_deg_per_s` and `turn_walk_deg_per_s`), three captain rows and the `wedge` order row. The mounted layer's hooks are small and guarded: `MountedBrain.Ride` hands a rider with a cavalry slot to `FormationBook.CavalryRide`; `MoveMount` takes the formation's speed cap; the want-chain of `MoveMount` is now `HorseCapMmps`, shared with `GaitMmps`; the cavalry brain's strike is `MountedBrain.StrikeAt`, the same lines moved out so the formation's riders use them (CV-2).
- **Shapes (3.1):** the line is two ranks of `n / 2` files at 1.5 m x 4 m; the wedge puts rank r (r = isqrt(slot)) at `((2r) - 2k) x 750` mm lateral and `4,000 r` back, `Files` is its widest rank, a hole is filled from the nearest man (by lateral place) in the first rank behind; the captain takes the point on forming and on a re-sort. Slot order for a cavalry formation is barding kg, rider armour, Riding, weapon skill, id (a total order).
- **The anchor and the riders (4.1):** the anchor's target pace is 95% (`anchor_pm`) of the 20th percentile of what the front rank's horses can do at the gait now (footing, wounds, load: the arithmetic the horse moves by), ramped at 225 mm/s a tick (4.5 m/s²) up and three times that down, and it marches only while cohesion is 600 or more (always once the charge is through). A rider's wished velocity is the anchor's velocity plus `min(sqrt(2 x 5 m/s² x gap), 2 x gap, 8 m/s)` toward his slot; the horse's gait is the one that speed needs and the speed is capped at it, so the riders arrive at their slots with the anchor's pace and cohesion holds in a gallop. The gait changes the turn rate (line 30 / 60 / 120 degrees a second at gallop / trot / walk, wedge 20 / 45 / 90, column 45 / 90 / 180).
- **The charge:** stage by the distance from the anchor to the target formation's anchor: walk beyond 200 m, trot to 60 m, gallop to 20 m, couch inside it (the spec's "trots to 120 m, gallops from 60 m" leaves 120 to 60 m open: the trot runs on to the gallop). Tested at 300 m with the stage ticks at the distance crossings (922, 1492, 1577) and the riders' own speeds in each gait. The longest approach is 150 s for a cavalry charge (`charge_max_ticks` 3,000), not BB-B's 30 s: a 300 m charge walks for a minute.
- **Shock:** contact is a rider within 3 m of an active foe who is not a horse; the cohesion judged is the row's at the first contact; with 10 or more contacts within 40 ticks it is struck once (-15, -5 for a man bracing or in a shield wall or schiltron at cohesion 600 or more, once each, every man within 5 m of a contact rider's place, riders excluded), or judged ragged (`ShockTick` -2) and never struck. `FormationBook.Shocks` lists what was struck for the battle entry. Tested exactly: the set struck equals the set within 5 m of the contact riders, 1,500 each, 500 for a wall, none at cohesion 600, none with eight riders.
- **After contact:** a rider above 4 m/s at his contact rides on (`CfThrough`), below it is in a melee (`CfMelee`: the Phase 4 brain rides him and he is left out of cohesion); a rider slow for 4 s once the charge is at the gallop is a melee too. The anchor then marches to 30 m past the contact anchor, then to 80 m past it while wheeling back to face the enemy, and the charge is over when it is there, facing back, with cohesion 600 or more: 14.3 s after the contact in the test (the 15 s are counted from the contact). A rider who fights on alone rejoins his slot once no foe is within 10 m, checked each second after the charge. With "press" every rider is a melee at the first contact and the formation holds where it hit.
- **The cavalry captain (4.2):** every decision (every `200 - 2 Tactics` ticks) the captain scores each enemy formation within 400 m: archers and routers 90, others 70, less a point a 10 m, plus 10 for a flank or rear at Tactics 50; from Tactics 30 a formation whose estimated refusal is 400 permille or more is skipped (`RefusalPm` of the braced or ready polearms within 6 m of its nearest man, plus 300 for a shield wall or schiltron); when every way in is skipped he rides to the flank of the nearest (a flank order, score 62). **Not built:** the feint (Tactics 70) and the 30 m clear lane.
- **Refusal and the other scans through the grid:** `Refusal`, `BracedFacing`, the braced strike, the trample, the brace wish (`BraceScan`, from the riders' side: each rider offers himself to the bearers he closes on within 30 m, 12 m when he is too slow to close at the brace speed; the bearer keeps the nearest and the first rider in pair order on a tie, as the per-bearer scan chose) and `PickTarget` (doubling boxes from 30 m, the same rule as `NearestEnemyOf`) use `Near`; the nested count in `BracedFacing` uses depth 2 and the outer scans depth 3. The unsorted query (`NearUnsorted`) is for the scans that do not depend on the order (the brace wish, contact, the rider's blow target with an explicit lowest-id tie): the grid's sort was most of a cavalry scan's cost. The oracle holds: `UseGrid` false gives the same hash over a 30-horse charge at a ring of 60 pikes.
- **Saved:** the formation book's pack is format 2 only when a cavalry row exists, with a tail per cavalry row (gait, stage and its tick, phase, contact anchor, first contact, cohesion then, shock, re-form, the order's start tick, the anchor's heading, and per member the contact tick, flags and slow ticks); a book with none is byte for byte BB-B's and an old save loads. No new section, no version bump, no stream (nothing here draws). The mounted fight record is untouched.
- **Measured** (this machine, shared, load average 13 to 22; `CavalryBudgetTests`): 2,000 soldiers in 20 formations of 100 and 300 cavalry (600 fighters, 20 formations of 15, captains on, charging from 100 m behind the lines), ticks 21 to 620: the mounted layer 0.62 ms a tick on the best of three passes (its first cost was 1.7 ms before the brace wish went to the grid and then to the unsorted query: 1.4 ms of it was the per-bearer scan), the whole tick about 7 ms average and 16 to 36 ms worst (a noisy machine); the gate is 0.8 ms for the layer. The cavalry formation layer itself (`CavalryStep`, contact scans) is inside the formation step and not in the 0.62 ms.
- **Worked example** (`WorkedExample_...`, 20 mailed lancers on coursers, 40 mailed foot at skill 45, 90 m apart, ten seconds after the contact): into loose infantry the line trots, gallops from 60 m, couches at 20 m, makes contact at tick 251 at about 10 m/s with the 20 riders within about 2 s and cohesion 1,000, shocks 14 men (-15 each), puts 15 foot down, loses no horse and no rider, and is whole again 80 m beyond at about tick 540. Into 40 spearmen braced against cavalry (a schiltron, set by the captain's rule), the same line meets the Phase 4 refusal at 8 m (34 balks: the ring's braced pikes facing a horse give 950 permille against these coursers), two horses run on and are struck by the pike at 4.5 m before any lance reaches (2 braced strikes), five riders are thrown by the balks, no ten riders come in together so there is no shock, and the charge is a melee; the ring then breaks by morale after five men are down, three of them captains in a row (BB-B's leader-down and rout cascade), so the number of foot lost is large in both runs: the counter shows in the refusals, the unhorsed and the missing shock, not in a count of dead.
- **Tests:** `Combat/Battle/CavalryTests.cs`: CV-1 (stage ticks against the distance crossings, the riders' gaits, the couched blows after the couch stage), CV-2 (the Phase 4 single rider and the formation rider: the same `StrikeV2`, the same `BlowResult`; and every `Charge` event of a real charge carries `ChargeMath` of the rider's own speed), CV-3 (the exact set, 800 / 600 cohesion, the wall, eight riders), CV-4 (breakthrough, the lame horses, the ride-on and the re-form point, press), CV-5 (the ring, the grid's count against the full scan's, the pike first, the grid oracle's hash), the worked example, the wedge and the slot order, the captain at Tactics 0 and 40, save and load mid-charge (on the approach and 30 ticks after contact) and determinism; `CavalryBudgetTests` is CV-13 in the timing collection.

## Notes (P7-CV-C, built)
- **Where it lives:** `Sim/Combat/Mounted/FootingCells.cs` (the 4 m cell grid, heights, fire, `Ground`, `Weathered`), `TerrainFooting.cs` (`FightTerrain` +`Cells`, `Wet`, `CavSpeedPm`, `VisionMm`, `SlopeAlongPm`, `ApplyRow`; the footing table grows `stakes`, `ditch`, `rubble`), `FightWorld.Mounted.cs` (`GroundHazards`, `StakeStrike`, the refusal's stake term, the cavalry speed in `HorseCapMmps`, the mixed hash and the saved weather bits), `Battle/Cavalry.cs` (the sight in `CavalryGait`), `ChargeMath.RefusalPm` (+`extraPm`), `Content/Defs/MountedDefs.cs` (the new tunables, defaults only: `combat/mounted.jsonc` is untouched because the abstract wound tables' content hash covers it). Tests: `Combat/Battle/CavalryGroundTests.cs`.
- **Cells and slope:** a cell holds 0 (no data: the zones and the base answer) or footing + 1; `FightTerrain.At` reads the cells first and then, when `Wet` is set, the weather over whatever it found. Heights are the cell centres' (mm); the height at a point is the bilinear mix of the four centres around it, and the slope along a heading is the ground one cell ahead less one behind over 8 m, so a charge's downhill bonus and the horse's uphill and downhill factor read the real ground. A fight with no cells reads the plane as before.
- **Stakes and ditches (4.4):** both are cell footings (`PlantStakes`, `DigDitch`). The refusal rolls for the nearest of a braced bearer and the first stake cell in the path within 8 m; it counts the braced polearms facing the horse and 250 pm for each stake cell whose centre is within the 6 m arc of the thing it rolls for, all before the thunderstorm doubling and the clamp (the roll is keyed to the stake cell, so it is made once per row). A horse that enters a stake cell at 4 m/s or more takes one thrust at its chest (or least barded fore part) with `M` 120 and no skill term (`Missile` blow of a synthetic oak point); a gallop entering a ditch cell falls with 400 pm (the slip path: the rider is thrown, the horse goes down). Stakes and ditches cap the horse at the trot (`charge: false`, 5 m/s).
- **Weather (4.8):** `Ground.From(RegionWeather, cell, hour)` counts the hours just past with rain at or over 2 mm/h and reads the temperature and snow now; `Weathered(footing, ground)` gives snow over 5 cm, then ice for -2 C or colder on wet ground (or mud), then mud for field or rough after 3 wet hours; stakes, ditches, rubble, woods, fords and water keep their class. `ApplyRow(row)` takes the weather row's rain and storm flags (heavy rain and thunderstorm only: snow and fog read their own numbers), `cavalry_speed_pm` (applied to any speed above a walk, so snow x0.6 keeps a gallop under the charge speed) and `vision_cap_m`, which a night's sight can lower. Ice now slips a horse at a trot as well as a gallop (the cap holds a horse to the trot there, so "above the trot" could never fire); the slip rate is the table's 80 pm a second, x1.5 in heavy rain.
- **Fog and night:** `VisionMm` above zero makes `CavalryGait` keep a charge in the walk stage while the target's anchor is farther than the sight (no trot, no gallop); once it is seen the stage follows the distance (so a 15 m fog goes walk to couch). The captain still picks targets beyond the sight (he is told where they are); he does not skip them.
- **Woods and fire:** a rider moving at 4 m/s or more in a woods cell rolls once a second against 50 pm (0x792); a hit is a head or upper body blow of `M` 40,000 through `FallBlow`, and the rider is not struck again for 20 s (`MountedExtra.BranchAt`, saved as an optional trailing value). A horse within 8 m of a burning cell (`FootingCells.SetFire`) gains the Phase 4 fire panic each second.
- **Saved:** no new section. The mounted tail's two weather slots keep bit 0 as the old 0 or 1 and carry the wet ground, the sight and the cavalry speed in the higher bits (zero in an older save, which loads as before); the branch tick trails the tail only when set. The cells are not saved, as zones are not: the setup re-supplies them from the chunks and `MountedHash` mixes their build hash only when present. A save, load and continue 30 ticks after contact on mixed ground (mud, woods, a ditch, stakes, a slope, 4 h of rain, heavy rain's sight) equals the uninterrupted run.
- **Budget:** the mounted layer with 300 cavalry (600 fighters) in the 2,000-soldier run over a box of mud, woods, rubble, ditches, stakes and a slope averages 0.42 ms a tick (0.35 ms with no cells), best of three passes (`CavalryGroundBudgetTests`, limit 0.8); the cell reads are an array index each.
- **Finding for CV-A (not changed here):** saving and loading a cavalry formation during the walk, trot or gallop approach (before the couch stage) makes the restored world drift: the anchor's y moves a few mm on the first step although `FormationBook.Pack`, the pairs and the fight hash are equal at the instant of the load. CV-A's own save test saves at tick 250 of a 90 m charge (at contact) and 30 ticks after it, which are both clear of it; it reproduces at the base commit with no CV-C code and no terrain (8 or 20 riders, a 150 m gap, saved at tick 250). Something the approach keeps outside the pack (the target point, a wheel state or a cached heading) is not restored.
- **Left out:** the chunk adapter that fills the cells, the live setting of `Ground` from the weather at a battle's cell, the chronicle lines, the captain's own sight, stake planting as a timed build, Godot.

**Fixed (after CV-C):** the save/load drift during a charge's approach was formation census counts (`ActiveNow`, `RoutingNow`, `TotalNow`) that are derived, not saved, and were filled only inside each row's own step, so after a load an enemy row read 0 until its turn. `FormationBook.Step` now takes every row's census before any row steps. Test: `CavalryTests.Save_SaveLoadContinue_EveryTwentyTicksOfTheApproach_MatchesTheUninterruptedRunEveryTick`.
