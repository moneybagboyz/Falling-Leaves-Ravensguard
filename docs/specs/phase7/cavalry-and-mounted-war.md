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
