# The battle bubble and formations: 2,000 soldiers live, formations, orders, command, captains, reinforcement waves

Status: design, written before any Phase 7 military code (plan §4: spec, then golden tests, then implementation), on top of b6b39e2 (Phase 6 closed).
Plan refs: §2.3 (the battle bubble: the engagement's bounding box plus 150 m, up to about 1 km², its own budget, ambient simulation in neighbouring chunks paused), §2.6 (one resolver; typical 530 resolutions a second at 2,000 soldiers, peak 1,300; level of detail only for AI and rendering; outcomes never depend on the camera), §2.7 (Field 8x16 minis, Command banner blocks), §2.8 (F1-F4 formations, R order wheel), §2.11 (2,000 soldiers <= 25 ms a 20 Hz tick, 60 fps), §3d (battle size 200-2,000, default 1,000; reinforcement waves; formations, orders, how you command, morale and rout), §4A.3 (morale numbers; "keep the battalion pivot with anchor braking; spatial buckets"), §5 Phase 7 (done-when: 2,000 soldiers hold 60 fps on the reference PC; content: Field and Command mini-sprites for every soldier type).
Conventions as Phase 6 (overview header): integer or `Fixed` sim arithmetic, counter-based `DetRng` streams, stable-id iteration, content as JSON, UI as views over snapshots, `tr()` strings. Fight positions in millimetres of the fight's plane, facings Q10, ticks of 50 ms (20 Hz). Money in copper pieces (cp; 1 gp = 10 sp = 100 cp, 1 sp = the old crown, per the economy writer's coinage spec).

## 1. Goals
1. One fight engine at every scale: a battle of 2,000 is the Phase 4 `FightWorld` with every fighter going through `ResolverV2`, the same wound books, morale and AI, made to scale by a spatial grid and struct arrays for the hot loop, with nothing about a blow changed.
2. The 2,000-soldier budget met (<= 25 ms a tick average, <= 33 ms the worst tick of a minute, 60 fps rendering), or, if the reference PC says otherwise, the default battle size drops and reinforcement waves carry the rest (plan §3d), with army sizes on the map unchanged.
3. Soldiers fight in formations that hold shape, turn, close ranks and break; the player commands them by F1-F4, the order wheel and a tactical pause overlay, with orders that take time to arrive (Leadership) and captains (Tactics) who run what he leaves alone.
4. Outcomes never depend on the camera: the sim never reads the camera; AI level of detail is keyed on the hero's position, never the view.
5. Readable at every zoom: full paper-dolls at Close and Street through a MultiMesh crowd layer, 8x16 minis at Field, banner blocks at Command.

## 2. What exists and what is new (by file)

| File (at b6b39e2) | What it does today | Phase 7 |
|---|---|---|
| `Sim/Combat/Melee2/FightWorld.cs`, `Fighter.cs` | the fight: `List<Fighter>` (a class per fighter), `Step` (vitals, commands, move, `Separate`, brace, impacts), all neighbour work by full scans (`Separate` O(n²), `AdjacentBraced`, `BracedNeighbours`, `OverWall`) | **extended:** every neighbour scan goes through `BattleGrid` (new `FightWorld.Grid.cs`); `Fighter` keeps its fields; the hot positional state is mirrored into struct arrays each tick |
| `Sim/Combat/Morale/FightWorld.Morale.cs` | per-fighter morale at 2 Hz staggered by id; friend-down, routing-neighbour, outnumbered, alone and cornered terms by full scans (review S12: O(n²) a morale step) | **extended:** the scans use the grid; new formation terms (4.6) |
| `Sim/Combat/Melee2/CombatBrain.cs` | AI at 20 Hz within `AiNearRadiusMm` (60 m) of `LodCentre` (the hero's fighter), 5 Hz beyond; target choice by full scans | **extended:** target candidates from the grid; a formation member's footwork reads its slot (4.4) |
| `Sim/Combat/Watch/SquadTactics.cs` | `Formation { Auto, Line, ShieldWall, Loose, FallBack }`, `TeamOrders` per side, `SquadTactics.Choose` (Tactics 30 picks a wall or line), F-key formation cards | **extended:** `Formation` gains `Column, Wedge, Schiltron, WagonFort` (append only); per-side `TeamOrders` stays for squads, formations get their own orders (`Sim/Battle/`) |
| `Sim/Combat/Mounted/*` | riders and horses as paired fighters | read by `cavalry-and-mounted-war.md` |
| `Sim/Live/Bubble.cs`, `PeopleBridge.cs` | live bubbles (promote 150 m, demote 200 m, 3 bubbles, 50 promotions a frame), the LODBridge | **extended:** a battle bubble kind (4.1) |
| `Sim/Save/FightSave.cs` | `FightRecord` per fighter, saved with a live fight | **extended:** formation book and orders in the optional `battle` section |
| `Game/World/FarLayer.cs` | Field minis and Command blocks for people drawn from chunk images | **extended:** soldiers' minis by troop type and team; Command blocks per formation |
| `Game/World/RigAtlas.cs` (`RigActor`, one Node2D per person) | street rendering | **unchanged** for streets; battles draw through the new `CrowdLayer` |
| `Game/Spikes/CrowdSpike.cs` | the Phase 0 spike: struct arrays, a 2 m hash, MultiMesh per layer group, GO decision (`docs/spikes/03-crowds.md`) | **source** of `CrowdLayer` and the sort |
| **New** | | `Sim/Battle/` (`BattleBubble`, `BattleGrid`, `FormationBook`, `FormationRow`, `Slots`, `OrderQueue`, `Captains`, `Reinforcements`, `BattleSave`, `BattleView`), `Sim/Combat/Melee2/FightWorld.Grid.cs`, content `battle/formations.jsonc`, `battle/captains.jsonc`, `battle/orders.jsonc`; Godot `World/CrowdLayer.cs`, `UI/TacticalOverlay.cs`, `UI/OrderWheel.cs` (rebuilt), `UI/FormationCards.cs`, art `art/src/minis/` |

### 2.1 The architecture decision: struct arrays and a grid, not Arch
The plan left the live tier to "Arch ECS, or plain struct arrays if the Phase 0 crowd spike says so". The spike ran 2,000 soldiers on struct-of-arrays with a 2 m spatial hash at 0.46 ms a tick, and no ECS package was ever added (`Ravensguard.Sim.csproj` references MessagePack only). **Phase 7 keeps struct arrays.** Concretely:
- `Fighter` stays the one record of a fighter's combat state (the resolver, wound book, morale and brain read it as today). Nothing a blow does changes.
- `BattleGrid` holds, per tick, `int[] X, Y, Team, ActBits, FormationId` indexed by fighter id, and a uniform grid of **4 m cells** over the fight plane (1 km² is 62,500 cells; a cell list is a counting sort of fighter ids by cell, rebuilt once a tick after movement, 0 allocations).
- **Determinism rule:** every query returns candidates **in ascending fighter id** (the order the old full scan gave). Queries visit cells in a fixed order and merge by id, so a fight with 10 fighters gives the same hash with or without the grid (BB-2).
- The radius each old scan used is kept: separation 2 x body radius, wall spacing, 10 m friend-down, 8 m routing neighbours, 12 m outnumbered, 15 m captain, 25 m banner, 30 m alone and leader-down, 60 m AI near.

### 2.2 Level of detail (AI and rendering only)
- Movement, impacts, the resolver, wound books, bleeding and morale run for **every** fighter every tick at full rate.
- The brain's offence, target and footwork think at 20 Hz within 60 m of the hero's fighter (`LodCentre`, the hero, never the camera), at 5 Hz beyond (Phase 4 ML-16 proved this changes how often, not what). Reactions (block, parry, dodge) stay per tick for everyone.
- Rendering picks a band from the camera; the sim never reads it. BB-3 runs one battle twice with the camera parked at opposite ends and compares hashes.

## 3. Data

### 3.1 The battle bubble (`BattleBubble`, saved in the optional `battle` section while open)
| Field | Type | Meaning |
|---|---|---|
| `Id` | int | battle serial (also the fight id) |
| `Box` | mm rectangle | the engagement's bounding box plus 150 m, clipped to 1,000,000 m² (at most 1,400 m on a side) |
| `Sides` | 2..4 rows | `{ Team, Realm, Commander person, Host id (armies spec), LiveCap, Reserve person ids (stable order), WavesSent, EntryPoint mm }` |
| `CapTotal` | int | the battle-size setting (200..2,000, default 1,000; soldiers, horses not counted) |
| `StartTick`, `Cause` | | the chronicle entry that started it (a campaign, a raid, an ambush) |
| `PausedChunks` | chunk keys | neighbouring chunks whose ambient sim is paused |

### 3.2 Formations (`FormationRow`, in `FormationBook` per battle)
| Field | Type | Meaning |
|---|---|---|
| `Id` | byte | 1..32 per side; 1 is the commander's own (the hero's retinue or squad) |
| `Side`, `Kind` | | `Infantry, Archers, Crossbows, Cavalry, Retinue, Allied, Skirmishers, EngineCrew, WagonFort` (append only) |
| `Members` | fighter ids | in slot order (4.3) |
| `Captain`, `Banner` | fighter id or -1 | Phase 4 morale flags `Captain`, `Banner` |
| `Shape` | `Formation` | Line, ShieldWall, Loose, Column, Wedge, Schiltron, WagonFort, FallBack |
| `Ranks` | byte | 1..8 |
| `AnchorX, AnchorY, FaceQ10` | mm, Q10 | the formation's centre front and facing |
| `Order`, `Queue[4]` | `FormationOrder` | current and queued orders (4.5) |
| `Fire` | enum | `AtWill, Hold, AtTarget` |
| `Drill` | 0..1000 | from `soldiers-and-companies.md` 4.4 (cohesion, reform speed) |
| `Micro` | tick | last player order; a captain takes over 30 s after it (4.7) |

`battle/formations.jsonc` (one row per shape):

| Shape | Spacing (lateral x rank) | Default ranks | Turn rate | Pace | Notes |
|---|---|---|---|---|---|
| Line | 1,000 x 1,200 mm | 4 | 30 deg/s | walk 1.5 m/s, jog 3 | default for foot |
| ShieldWall | 800 x 1,000 mm | 3 | 15 deg/s | walk 1.0 | braced; +100 pm cover per braced neighbour (Phase 4, max 2) |
| Loose | 2,500 x 2,500 mm | 2 | 90 deg/s | jog 3 | -50% casualties from missiles in the expected hits per area; no wall, no brace |
| Column | 1,500 x 1,500 mm, 3 abreast | n/3 | 45 deg/s | march 1.3 | the marching order; deploys to a line (4.3) |
| Schiltron | 900 mm on a ring, facing out | 2 | 0 | 0 | spears and pikes braced all round (set against cavalry) |
| Wedge, WagonFort | | | | | `cavalry-and-mounted-war.md` 3.1, `sieges-and-engines.md` 4.8 |
| FallBack | | | | | Phase 4's "fall back to me": members' slots ring the commander at 3 m |

### 3.3 Orders (`FormationOrder`, `battle/orders.jsonc`)
`{ Kind, TargetX, TargetY, TargetFormation, FaceQ10, Frontage mm, IssuedTick, ArriveTick, By (person), Signal (Voice, Horn, Runner) }`. Kinds (append only): `MoveTo, Advance, Charge, Hold, FollowMe, FlankLeft, FlankRight, ShieldWall, Loose, Line, Column, SetAgainstCavalry, FireAtWill, HoldFire, AttackFormation, Retreat, Rally, Face, Wheel, Dismount, Mount` (the last two from the cavalry spec). Each row in content names whether a horn can carry it (Advance, Charge, Hold, Retreat, Rally: yes).

## 4. Rules

### 4.1 Opening, running and closing the battle bubble
- **Opens** when (a) two hostile groups (armies, warbands, hosts, raiders; `armies-on-the-map.md`) come within 400 m of each other and either touches a live bubble, or (b) the hero's side attacks or is attacked with more than 60 fighters in all, or (c) an assault starts at a besieged place (`sieges-and-engines.md`). A smaller fight stays a Phase 4 live fight.
- **The box** is the two groups' extent plus 150 m, at most 1 km² (if wider, centred on the hero's side's commander, else the larger group). Chunks inside it are generated (time-sliced, 4 ms a frame) before contact; people in the box who are not fighting stay live and flee by their utility AI (they are people, not fighters, unless they take up arms). **Ambient sim in neighbouring chunks** (within 200 m of the box) pauses: their people hold their abstract jobs; work credit resumes at the battle's end (no hours are lost from the abstract clock: the hour barrier still runs; only the live jobs there pause).
- **Who is live.** Each side's live cap is `CapTotal x its strength / both strengths`, at least `CapTotal / 4` while it has the men. Soldiers enter by formation in the side's deploy order (4.3); the rest wait as records in the side's **reserve** at its entry point (the road it came by), not fighting.
- **Promotion** uses the LODBridge (`PeopleBridge`) at <= 50 people a frame entering at the box edge; a 1,000-man host takes 20 frames (one third of a second at 60 fps) once its column is inside the box, so armies already in the bubble stay as they were (no pop).
- **Reinforcement waves.** Every 5 s per side: if a formation's active members fall under 70% of its start, or the side's active count under 70% of its live cap, a wave of up to `CapTotal / 4` soldiers forms from the reserve (whole formations first, by the deploy order) and walks in from the entry point. A wave never exceeds the side's cap. A side with an empty reserve sends none. Waves are a chronicle detail of the battle entry (count), not their own entries.
- **Closes** when one side has no formation with active members inside the box (fled, down, dead, yielded or demoted beyond the edge), or both sides have been out of contact for 60 s (`walk_away_s` 15 for skirmishes, 60 for battles), or a truce is agreed. Then: the Phase 4 aftermath (captives, first aid, loot) for the field's holder, survivors folded back by the LODBridge (wounds, kit, coin), reserves rejoin their host, the battle entry (section 8), paused chunks resume.
- **Saving inside a battle** is allowed (outside Ironman manual and quicksaves; Ironman saves on quit); the autosave before a battle (plan §2.11) is written when the bubble opens.

### 4.2 The grid and the hot loop (budget, section 10)
The tick order is Phase 4's (`Step`: vitals, commands, movement, separation, brace, impacts), with three additions: `BattleGrid.Rebuild` after movement and again after separation (both counting sorts, ascending id within a cell); the formation layer (slots, anchors, orders) before the brains; reinforcement checks every 100 ticks. Vitals skip a fighter with no open wound and no pain (no change of results: such a book has nothing to advance). Projectiles test only the cells their segment crosses.

### 4.3 Slots, deployment and closing ranks
- **Slots** are laid out from the anchor: rank 0 is the front, files left to right, spacing per shape (3.2). Each slot's world position = anchor + rotate(offset, facing).
- **Slot assignment** (on forming and on a shape change): members sorted by role priority for the shape (ShieldWall and Line: shield bearers to rank 0, polearms to rank 1, missiles behind; Schiltron: polearms outside), then skill descending, then person id. Pure; no draw.
- **Closing ranks:** when a slot of rank r empties (down, dead, fled), the member directly behind steps up after 40 ticks (2 s) less `Drill / 25` ticks (a drilled unit closes in about 1.2 s). Files close inward from the flanks when a rank-0 file has nobody behind.
- **Deploying from column:** a column ordered to Line forms on its head: each rank of three walks to its file at jog; a 1,000-man column (333 ranks, about 500 m) deploys in `ranks x 1.5 m / 3 m/s` = about 170 s (Drill 1,000 halves it). This is why a column caught on the road is in danger (armies spec 4.4).
- **Anchor braking** (the old battalion pivot, kept): the anchor moves at the order's pace but no faster than the 20th-percentile pace of its rank-0 members (wounded and armoured men slow the line), and stops while fewer than 600 pm of members are within 2 m of their slots (`Cohesion`).
- **Pivot:** a turn of the facing rotates slots about the anchor at the shape's turn rate (3.2); a wheel (order `Wheel`) rotates about a flank file.
- **Cohesion** = members within 2 m of their slot, permille. The shape's bonuses (shield wall cover, schiltron brace, wedge shock) need cohesion >= 600.

### 4.4 Members in contact
A member steers to his slot until an enemy is within 3 m; then the `CombatBrain` fights as in Phase 4, on a **leash**: it may not move more than 2 m from its slot (Line, ShieldWall, Schiltron), 6 m (Loose, a Charge), or none (a braced member). A member beyond his leash walks back unless routing. A formation ordered to `Hold` keeps leash 1 m. Archers in a formation shoot at the formation's target (or the nearest enemy formation in range when `AtWill`), never at a melee their own men are in within 5 m of the aim point.

### 4.5 Orders and their delay (Leadership)
- An order is issued at the tick the player (or a captain) gives it and **arrives** at the formation's captain after:
  `delay = max(10, 80 - Leadership x 2 / 5) ticks` (Leadership 0: 4.0 s; 50: 2.0 s; 100: 0.5 s)
  `+ 0` if the issuer is within 60 m of the captain (voice),
  `+ 20 ticks` (1 s) beyond 60 m for an order a horn can carry, when the side has a horn or banner bearer,
  `+ distance / 5 m/s` otherwise (a runner: a real member of the commander's formation walks it; if he is downed the order is lost and the overlay says so).
- A formation's members begin to act on an arrived order within 0..10 ticks of each other (rank by rank front first; pure, by slot index).
- Queued orders (at most 4) start when the one before completes (`MoveTo` reached within 3 m, `Charge` contact or 30 s, `Hold` until another order).
- Captains' own decisions (4.7) have no delay; they are on the spot.

### 4.6 Morale and rout at formation scale
The Phase 4 per-soldier model (`morale.jsonc`) is unchanged. Added terms, all in `battle/formations.jsonc`:
- **Formation flanked:** when 100 pm or more of a formation's members took a side or rear blow in the last 5 s, every member gets the plan's -15 flank term for 5 s.
- **Formation rout:** when 500 pm of members are routing, the rest get -20 once (the cascade); a formation within 30 m whose members see it gets -10 once.
- **Shield wall and schiltron** give +5 (the Phase 4 `shield_wall_x100` 500) to members with cohesion >= 600.
- **The hero's aura:** radius `10 m + Leadership / 5 m` (Leadership 50: 20 m, 100: 30 m); inside it the hero counts as a captain (+10, Phase 4 `captain_x100`) and can rally members above 40 (Phase 4 rally rule). Phase 4's floor stays: the hero leads at Leadership 20 or more.
- **Captain fallen:** Phase 4's `leader_down_x100` (-20 within 30 m) now applies to the formation's members wherever they stand.
- Routing soldiers flee to their side's entry point; leaving the box they are demoted as `Fled` records (the armies spec decides whether they rejoin or desert).

### 4.7 Captains (Tactics)
- A formation's captain is its member with the highest `Tactics`, ties by `Leadership`, then id, among men-at-arms and knights first (`soldiers-and-companies.md` roles), else anyone.
- A captain runs his formation when the player has not ordered it for 30 s, or always when the player sets it to "captain" in the overlay. He re-decides every `max(40, 200 - Tactics x 2) ticks` (Tactics 0: 10 s; 50: 5 s; 80+: 2 s).
- `battle/captains.jsonc`: rows by Tactics threshold, each a list of rules the captain may use (pure; ties by 0x780):

| Tactics | Adds |
|---|---|
| 0 | advance on the nearest enemy formation; archers shoot at will; hold if outnumbered 2:1 |
| 30 | shield wall against missiles; set against cavalry when horse is within 150 m (Phase 4 `SquadTactics.WallTactics`); hold fire beyond 150 m |
| 50 | face or refuse a flank attack (wheel); keep 1 formation in reserve until contact; pull back archers when foes reach 40 m |
| 70 | attack a flank of an engaged enemy formation; rally and reform a broken formation behind the line; time a charge to the enemy's reload |

### 4.8 Commanding: F1-F4, the order wheel and the tactical pause (keys in section 9)
- **F1-F4** select the hero's side's formations 1-4 (1 = his own); Shift + F1-F4 adds to the selection; pressing the same F-key twice within 0.4 s centres the camera on it. Tab (in the overlay) cycles through every formation, 5 and up included. With no army (a squad only), F1 selects the squad. **Phase 4's shape cards** (F1 line ... F4 fall back) move to the order wheel's Shape ring, so F-keys always mean the same thing (open question 1).
- **The order wheel** (hold R): an inner ring of eight (Advance, Charge, Hold, Retreat, Follow me, Flank left, Flank right, Rally), a Shape ring (Line, Shield wall, Loose, Column, Set against cavalry, Wedge for horse), a Missiles ring (Fire at will, Hold fire, Shoot at...). Mouse direction or the arrow keys choose; release R (or Enter) issues to the selection; Esc cancels. With nothing selected, the order goes to formation 1.
- **The tactical pause overlay** (key `0`, or Space while a battle bubble is open with the setting "battle pause opens the overlay", default on): the game is paused; formations are drawn as blocks over the live view with their facing, cohesion, morale bar, current and queued orders as arrows; only enemies your side can see are drawn (line of sight from any member: 300 m by day, 60 m at night or the light grid's radius, fog per weather), the rest as last-seen ghosts with their age. LMB-drag a block: a `MoveTo`; RMB-drag: draw the frontage (start to end sets width and facing, perpendicular to the line); Shift queues; Ctrl+Z undoes the last placement before resuming. Orders placed while paused are issued at the paused tick and their delay runs from resume, so pausing gives no free time.

### 4.9 Field and Command mini-sprites
- **Field (8 px/m):** an 8x16 mini per troop type (levy, spearman, archer, crossbowman, man-at-arms, knight on foot, sergeant, engine crew, sailor) and 16x16 for riders and horses (light horse, knight mounted), three views, team palette row, a shape marker per faction on the shield pixel; wound bits darken; routing minis are drawn facing away with a flicker every 0.5 s.
- **Command (2 px/m):** one banner block per formation sized to frontage x depth, the faction's colour and shape marker, a facing tick, a morale bar under it, hatched while disordered (cohesion < 600), flashing while routing; engines and wagons as their own icons.

### 4.10 The crowd layer (Godot)
`CrowdLayer` draws every battle fighter at Close and Street from baked rig frames: one MultiMesh per layer group (body; under and over; armour; cover; helmet and shield; weapon: six groups), per-instance custom data `{ atlas frame, palette row, wound bits | moving, walk phase }` as in the spike, rebuilt from the published fight snapshot each frame, y-sorted by a counting sort into 1-px y-buckets (the spike's `Array.Sort` replaced, open item 3 of its decision). Selection rings, the hero's gold ring and nameplates are a seventh, small group. `RigActor` nodes for battle participants are hidden while the bubble is open.

## 5. Tick placement
| Where | What |
|---|---|
| Live 20 Hz (battle bubble) | the Phase 4 step with the grid; the formation layer; reinforcement checks every 100 ticks; order arrivals |
| Frame (Godot) | `CrowdLayer` fill and upload; FarLayer minis and blocks; overlay |
| Barrier | opening and closing the bubble; promotions (<= 50 a frame); aftermath; chronicle |
| Abstract hour | paused chunks skip live job credit; nothing else changes |

## 6. RNG streams (0x780..0x78F, this spec)
| Stream | Use | Key |
|---|---|---|
| 0x780 | captain rule ties (equal scores) | battle, formation, tick / 40 |
| 0x781 | a wave's entry jitter along the entry road (0..20 m) | battle, side, wave |
| 0x782 | rank-closing hesitation (0..10 ticks) | battle, fighter, slot change |
| 0x783 | a runner's route choice between equal paths | battle, order serial |
| 0x784..0x78F | free | |
Everything else (hits, wounds, morale sight, AI) uses the Phase 4 streams with the fight's keys, unchanged.

## 7. Saves
Optional section **`battle`** (written only while a bubble is open; absent otherwise, so a world that never fought keeps its bytes): the `BattleBubble` row, the `FormationBook` (rows packed as integers), order queues, reserves, wave counters, paused chunk keys. Fighters stay in the Phase 4 fight record (`FightSave`, per fighter). **No version bump (v8).** Enums append only (`Formation` +`Column, Wedge, Schiltron, WagonFort`; `FormationKind`, `OrderKind` new). Size: about 300 B a fighter plus wound books: <= 1.5 MB while a 2,000-soldier battle is open, 0 after. Round trip: BB-14.

## 8. Chronicle
| Type | Text key | Params | Cause |
|---|---|---|---|
| `battle_fought` | `ch_bt_battle` | place, commander a, commander b, men a, men b, lost a, lost b, waves a, waves b, outcome key | the campaign, raid or ambush entry (`War`, `Raid`, `Battle`); root when neither |
| `battle_hero_led` | `ch_bt_hero_led` | place, formations, lost, outcome key | the `battle_fought` entry |
| `captain_fell` | `ch_bt_captain_fell` | person, formation key, place | the battle entry (only for knights and named captains) |
| `banner_taken` | `ch_bt_banner_taken` | banner's lord, taker, place | the battle entry |
Outcome keys: `ch_bt_out_won`, `ch_bt_out_lost`, `ch_bt_out_drawn`, `ch_bt_out_truce`. Rows go into `chronicle/types.jsonc` with `specific_requires: either`.

## 9. UI and keys (960 x 540)
- **Battle HUD** (World, while a bubble is open): top strip, the two sides' live / reserve / lost counts and a morale bar each; left, formation cards F1-F4 (+ "5 more" when there are) with shape icon, cohesion, morale and current order; bottom hint bar `F1-F4 select · R orders · 0 tactical pause`.
- **Tactical overlay** (context `Tactical`, modal, alone with Global): left the formation list (all of them), right the selected formation's card (captain and his Tactics, Drill, members active / start, morale, the order queue with arrival countdowns, "captain commands" toggle), bottom the key bar.
- **Order wheel** (context `OrderWheel`, modal while R is held, alone with Global): three rings as above, a tooltip per order with its expected delay ("arrives in 2.0 s, by horn").

| Context | Keys | Origin |
|---|---|---|
| World, CombatStance | F1-F4 select formation 1-4 (action names `formation_1..4` kept, meaning changed); **Shift+F1..F4** add to selection (new; free in both); R order wheel (unchanged key); **`0`** tactical pause overlay (new; free in World and CombatStance, not in Choice, which takes 1-9) | Phase 0 key, Phase 7 meaning |
| `Tactical` (new, modal) | LMB select / drag move, RMB drag frontage, Shift queue, F1-F4 / Shift+F1-F4 select, Tab next formation, R order wheel, C captain commands toggle, Ctrl+Z undo, Delete clear queue, PgUp / PgDn level (wall-walks), Space or `0` resume, Esc close overlay (stay paused) | new |
| `OrderWheel` (new, modal while held) | mouse direction or arrows sector, Tab next ring, release R or Enter issue, Esc cancel | new |
`--check-keymap` must stay at 0 conflicts; the `?` overlay gains Tactical and Order wheel pages from `InputDefs`.

## 10. Budgets (reference PC; the battle bubble ledger)
Live tick, 2,000 soldiers plus 300 horses (2,300 fighters), 40% in contact:
| Item | Budget (average) |
|---|---|
| Grid rebuild x2 (counting sort) | 0.3 ms |
| Movement, slots, separation (grid) | 1.6 ms |
| Brains (about 300 near at 20 Hz, 2,000 far at 5 Hz: about 800 thinks a tick, about 3 us each) | 2.4 ms |
| Impacts and the resolver (530 a second typical = 27 a tick at about 25 us; peak 1,300 = 65 a tick) | 0.7 ms typical, 1.6 ms peak |
| Vitals and wound books (only fighters with open wounds) | 2.0 ms (a late battle with 600 wounded) |
| Morale (2 Hz staggered: 115 a tick, about 6 us with the grid) | 0.7 ms |
| Projectiles (about 200 in flight) | 0.6 ms |
| Formation layer, orders, captains, waves | 0.6 ms |
| Mounted layer (cavalry spec) | 0.8 ms |
| **Total** | **about 9.7 ms typical, <= 25 ms average gate, <= 33 ms worst tick in a minute** |
Frame: `CrowdLayer` fill, sort and upload of 2,300 instances x 6 groups <= 1.5 ms CPU; GPU p99 <= 16.6 ms at every band with all groups visible (the spike's open item on the reference PC); FarLayer minis <= 1 ms. Memory: 2,300 fighters with wound books <= 120 MB. Allocation gate: 0 allocations a tick in the grid, slots and morale. **If the reference PC misses the tick gate**, the default `CapTotal` drops to the largest value that passes (in steps of 250), waves carry the rest, and the setting's top is labelled "may slow on your PC".

## 11. Golden tests
| id | Case | Expected |
|---|---|---|
| BB-1 | Grid equivalence | 200 Phase 4 skirmish seeds (ML-16 set) with and without the grid: fight hashes identical |
| BB-2 | Order of candidates | a constructed cell boundary case: neighbours returned in ascending id; the 10-fighter golden hash of Phase 4 unchanged |
| BB-3 | Camera independence | one 1,000-soldier battle run twice, camera parked at opposite corners (Game harness): equal fight hash |
| BB-4 | Near and far at scale | 100 seeds of 400 v 400: soldiers within 60 m of the hero and beyond differ by <= 5 points in downs, deaths and each wound class (ML-16's gate at scale) |
| BB-5 | **Budget** | 2,000 soldiers + 300 horses, 40% in contact, 6,000 ticks: average <= 25 ms, worst tick of each minute <= 33 ms, 0 allocations a tick (Timing collection, `TestBudget.BestPass`) |
| BB-6 | Slots | 100 men into a 4-rank line: shield bearers in rank 0, polearms in rank 1, sorted by skill then id; slot positions exact to the mm at facing 30 degrees |
| BB-7 | Closing ranks | a rank-0 man downed: the man behind steps up 40 ticks later (Drill 0), 28 ticks at Drill 300 |
| BB-8 | Anchor braking | a line with 10 men at walk 50% moves at their pace; with cohesion under 600 the anchor stops |
| BB-9 | Deployment | a column of 300 ranks (900 men, three abreast) ordered to Line forms in 150 s (+-2 s) at Drill 0, 75 s at Drill 1,000 (section 4.3: `ranks x 1.5 m / 3 m/s`; a 300-man column of 100 ranks forms in 50 s; reading settled at the BB-B merge) |
| BB-10 | Order delay | Leadership 50 within 60 m: 40 ticks; 200 m with a horn: 60 ticks; 200 m without: a runner arrives in 800 + 40 ticks; the runner killed: order lost and shown |
| BB-11 | Pause gives no free time | an order placed while paused for 60 real seconds arrives the same number of ticks after resume as one given unpaused |
| BB-12 | Formation flank and cascade | a line hit in the flank by a 20-man unit: flank term on all members; at 500 pm routing the rest take -20 and the neighbour -10 once |
| BB-13 | Captains | Tactics 30: shield wall when archers loose at 120 m; Tactics 50: wheels to face a flank attack within 5 s; Tactics 0: neither |
| BB-14 | Save inside a battle | save at tick 1,200 of a 1,000-soldier battle, load, continue to the end: fight hash equal to the unbroken run; save, load, save byte-identical |
| BB-15 | Waves | cap 1,000 against armies of 1,500 v 1,500: each side starts with 500 live; a wave of <= 250 enters within 5 s of a side falling under 70%; no side ever exceeds its cap |
| BB-16 | Bubble lifecycle | box clipped to 1 km²; neighbouring chunks paused and resumed; survivors folded back with wounds and kit; the `battle_fought` entry with a specific cause |
| BB-17 | Keys | `--check-keymap` 0 conflicts with `Tactical`, `OrderWheel`, `0`, Shift+F1-F4 |
| BB-18 | Mini-sprites | every troop type in `war/troops.jsonc` has a Field mini (3 views) and a Command block icon; the art validator passes palette and size |

## 12. Open questions (recommended defaults)
1. **F1-F4 change meaning** from Phase 4's squad shapes to selecting formations (the plan's meaning); shapes move to the wheel. Default: yes, always; a squad is formation 1. (For the user: it changes a key you have been using.)
2. **Default battle size:** 1,000 (plan). If the reference PC misses BB-5 at 2,000, the setting's top stays 2,000 with a warning, the default drops by 250 steps. Default as written.
3. **Can the hero give orders while fighting in the line?** Yes: R and F-keys work in CombatStance; the delay applies as for anyone.
4. **Fog in the overlay:** only what your side sees, with ghosts of last-seen enemies. Default as written (fair, and the AI works on the same rule).

## 13. Build parts and estimate
| Part | Content | Gate | Months |
|---|---|---|---|
| BB-A grid and scale | `BattleGrid`, `FightWorld.Grid.cs`, struct arrays, every scan moved, vitals skip, allocation gate | BB-1, BB-2, BB-4, BB-5 (sim side) | 1.0 |
| BB-B formations and orders | `FormationBook`, shapes, slots, closing ranks, anchor braking, deployment, leash, orders, delay, formation morale, captains | BB-6 to BB-13 | 1.3 |
| BB-C the bubble | open, box, reserves, waves, pause of neighbouring chunks, close, aftermath fold-back, `battle` section, chronicle | BB-14 to BB-16 | 0.8 |
| BB-D command UI | battle HUD, `Tactical` overlay, `OrderWheel`, F-keys, key map | BB-11, BB-17 | 0.9 |
| BB-E rendering | `CrowdLayer`, counting y-sort, Field minis and Command blocks, the reference-PC frame run | BB-3, BB-18, frame budget | 0.9 (+ art: about 10 troop minis x 3 views, banner icons) |
| **Total** | | | **4.9 months of code** |

## Notes (P7-BB-A, built)
- **Where it lives:** `Sim/Battle/BattleGrid.cs` (the arrays, the grid, the queries), `Sim/Combat/Melee2/FightWorld.Grid.cs` (`Near`, `NearestEnemyOf`, `AnyEnemyWithin`, `UseGrid`), and the scans themselves in `FightWorld.cs`, `.Impact.cs`, `.Ranged.cs`, `FightRules.cs`, `CombatBrain.cs` and `Morale/FightWorld.Morale.cs`. `Fighter` keeps its fields and gains no state of its own beyond a back-reference to the grid; `ResolverV2` and every blow are untouched.
- **How a query stays exact.** `Near(depth, x, y, r)` returns the ids that *could* be within `r` (a superset, ascending); each loop keeps its own distance test, so the order, the tests and the draws are the old scan's. The grid holds positions as of its last rebuild, so `Fighter.X`, `Y` and `ReachMm` are now properties that report each write to the grid (`NoteX`, `NoteY`, `NoteReach`); the grid keeps the largest drift of any fighter since the rebuild and widens every query box by it. That makes a query right at any moment (inside `Separate`, after a shove, after a knock-back), not only after a rebuild; the fight rebuilds after movement and after separation, and lazily when the drift passes half a cell or a fighter is added. A fighter far from the rest is clamped into a border cell of a grid at most 512 cells a side, which can add candidates, never lose one. A box covering a third of the grid returns every id (no sort).
- **`UseGrid` is the oracle.** With it false every query returns every id in order, which is the pre-grid code path itself; BB-1 and BB-2 run both and compare the fight hash and the morale hash. A mutation check (shrinking the query box) fails four of the nine tests, so they do see a wrong box.
- **`Separate`** was O(n²) on positions that earlier pushes had moved. It now takes `a`'s neighbours from a box of `2 x body radius + 600 mm` around `a`; `a` is pushed as it goes, so when it has strayed 600 mm from where the box was cut the box is cut again and the walk resumes after the last `b` tested. Every pair is still tested once, in the same order.
- **Searches with no radius** (nearest enemy for a shot, a surrender, a downed crawler, a target pick): doubling boxes from 16 m; a candidate found at distance `d <= r` ends the search, because everything closer is inside the box, and ties still go to the lowest id. The brain's target pick under the "threat" priority (a foe aiming at you counts 2 m nearer) stops at `d <= r - 2 m`. The escape check only ever compared the nearest enemy with the escape distance, so it asks for "any enemy within it".
- **Morale at scale.** Three scans were not radius scans: `RallyPoint` (the start centre of a side; the kits never change, so it is cached per team, refreshed when fighters join), `Winning` (counts the downs of the last 30 s; cached per team for one morale step, inside which no fighter goes down) and the captain and banner terms (listed once a step, so "a captain within 15 m" walks the handful of carriers instead of a 25 m box). `TermsOf` makes its 30 m "alone" look only for a fighter with no friend within 12 m and a foe there, the only case that reads it. A fall before the first morale step still initialises the whole side's morale, as the old scan did.
- **Not moved:** scans that are not neighbour scans (the hash, `IsOver`, the feint's "any defender who began a parry", setup-time captain appointment), the Watch-mode scans (`RiskEstimate`, `StanceBias`, `SquadTactics`: they read the whole side by design and run at 5 Hz) and the Mounted layer's scans (`MountedBrain`, trample and brace, a handful of riders): all still the old full scans, to move with the formation work in BB-B if the budget needs it. The public `FightWorld.NearestEnemy` stays the full scan (the Godot view calls it from the main thread; the grid's scratch is for the thread that steps the fight).
- **Vitals skip:** not added. `WoundBook.AdvanceTo` already returns at once for a book with no wound, no bleed and no pain, and the whole vitals pass measures 0.9 ms for 2,300 fighters; a second skip would only duplicate that test.
- **Cell size** is 4,000 mm as written (a division by 4,000, not a shift). **Level of detail** was already keyed on the hero (`LodFighter`, then `LodCentre`), never the camera; nothing to change.
- **Measured** (this machine, shared, load average 12 to 15, Release, `Combat/Battle/BattleGridTests.cs`): the Phase 4 skirmish at scale (two lines, the hero at one end, 20 Hz within 60 m, 5 Hz beyond), mean ms a tick while the fight lasts, grid against the full scan of the same build: 200 fighters 1.0 against 2.8; 600: 2.5 against 4.1; 1,200: 1.7 against 14.0; 2,300: 3.3 against 44.8. 2,300 fighters, 6,000 ticks (the fight is over after about 1,000): average 3.4 ms, worst tick 12.3 ms; the gates are 25 and 33. A fight that cannot be over (a dense block that never breaks) was not found: the scenario's fighters rout and yield within a few hundred ticks, so BB-5's "40% in contact" is met only at the first contact, and the 300 horses are not in the run (the Mounted layer is not part of BB-A). Zero allocations: a rebuild and a query allocate nothing (asserted); a whole step still allocates for events, the resolver and the wound books.
- **Tests:** `BB1` (200 seeds of the ML-16 set, and 3 x 120-fighter crowds and a shield-wall block compared every 40 to 100 ticks), `BB2` (ascending order across cell edges and negative coordinates, never a missed candidate after random moves and an outlier, nearest enemy against the full scan with ties), `BB4` (a 4-seed 150 v 150 sample, and the 100-seed 400 v 400 as a Soak test: run once, 11 m 42 s, near down 16.4% dead 10.8% against far 15.7% and 11.3%, wound classes within 5 points), `BB5` (the allocation check, a 300-tick budget, the 6,000-tick Soak and a grid-against-scan report; the budget tests are in the Timing collection).

## Notes (P7-BB-B, built)
- **Where it lives:** `Sim/Battle/` gains `FormationTypes.cs` (`FormationKind`, `OrderKind`, `OrderSignal`, `FireMode`, `FormationOrder`, `FormationRules`), `Slots.cs` (pure layout and slot order), `FormationRow.cs` and the partial `FormationBook` (`FormationBook.cs` forming, closing ranks, cohesion, brake, save; `.Orders.cs` issue, delay, arrival, the anchor; `.Captains.cs` 4.7; `.Morale.cs` 4.6 and the brain's hooks). Content: `content/base/battle/formations.jsonc` (`formation_shape` rows and one `battle_rules` row), `orders.jsonc`, `captains.jsonc`, and `Content/Defs/FormationDefs.cs`. `FightWorld.Formations.cs` holds the book (null until `EnsureFormations`, so a fight with no formation runs, hashes and saves as before). Hooks, all guarded by `Formations != null`: `StepWithAi` (the formation layer runs before the brains), `Move` (a member keeps the formation's pace), `Hash`, the brain (`Think`: the leash; `Decide`: slot steering, hold fire, the safe shot; `Flee`: the entry point), morale (`TermsOf` wall bonus, `MoraleOnHit` flank blow, `MoraleOnFallen` the fallen captain, `CarrierWithin` the hero's aura), `BattleGrid.Rebuild` (`FormationId` = row + 1) and `FightRecord` (optional key 25, `Formations`; null when there is none, no version bump).
- **Slots and members.** A formation is made with `FormationBook.Form(...)`; members are sorted by role class (shield bearers, polearms, other foot, missile men; a schiltron puts polearms outside), skill, id. A slot is `(lat, back)` in the formation's frame and `x = ax + ((-fy lat - fx back) >> 10)`, `y = ay + ((fx lat - fy back) >> 10)` (Q10 facing, an arithmetic shift). Each rank is centred on the anchor. Wedge and wagon fort lay out as a line until their parts land. A member who is down, dead, fled, yielded or routing gives up his slot (a vacancy); a rallied man fights on alone.
- **Closing ranks** waits `max(1, 40 - Drill / 25)` ticks from the tick the man fell (BB-7: 40 and 28), then the nearest man behind in the file steps up; with nobody behind, the flank man of that rank nearer the hole steps in; a hole at the very end of a rank is dropped. The man who stepped up leaves a vacancy that closes after the same wait plus the 0..10 ticks of stream 0x782, so the first man behind steps up exactly on time and the ripple behind him hesitates.
- **Anchor and brake.** The anchor marches only while cohesion is 600 or more and the facing is within 45 degrees of the way; it never exceeds the 20th-percentile pace (`walk x WalkPm`, less a fifth when weakened) of the rank-0 men, recomputed every 10 ticks, on a shape change and when a movement order arrives. A member steers to his slot at the anchor's pace plus twice his gap a second (`FormationBook.PaceOf` feeds `Move`; it is saved per member in the formation row); a member beyond 6 m sprints to catch up. Turning is one 5.625 degree step per `5625 / (rate x 50)` ticks (30 degrees a second: 3.75 ticks); a wheel keeps the pivot flank where it was.
- **Deployment from column.** Leaving a column for a rank shape keeps the slot order and releases the ranks head first, one rank of three at a time; the line is formed `column ranks x 1.5 m / 3 m/s` later, less `Drill / 2` permille. **The spec's own figures disagree** (4.3: a 1,000-man column, 333 ranks, about 170 s; BB-9: a 300-man column in 150 s). The rank arithmetic is built, so a 300-man column (100 ranks) deploys in 50 s and 25 s at Drill 1,000, and 900 men (300 ranks) in the 150 s and 75 s the gate names; BB-9 is tested on those two readings.
- **Orders.** `Issue(w, row, order, issuer, leadership, queue)` returns the order as sent (its `ArriveTick`); up to eight are on their way, sorted by arrival, and four queue. **Delay is `max(10, 80 - Leadership x 4 / 5)`**, not the `x 2 / 5` of 4.5's formula (that gives 60 ticks at Leadership 50 where the same line and BB-10 say 40). Within 60 m of the captain nothing is added; beyond it a horn order (content: advance, charge, hold, retreat, rally) adds 20 ticks when the side has a banner bearer on his feet; else a runner, the issuer's formation's first man on his feet, adds `distance / 250` ticks (5 m/s: 800 for 200 m); if the runner falls first the order is lost (`FormationBook.Lost`). The runner is not simulated as walking, only as a life the order depends on. Time is fight ticks, so a paused game gives no free time (BB-11 is the proof that arrival is `issue tick + delay` and nothing else). Momentary orders (shapes, fire modes, face, wheel) take effect on arrival and leave the movement order alone; `MoveTo`, `Advance`, `Charge`, `Retreat`, `FollowMe`, `FlankLeft/Right` and `AttackFormation` replace the current order or join the queue; a finished march holds. `Retreat` with no target goes to the side's entry point, else 80 m behind the facing. Members start within 0..10 ticks of each other, rank by rank (stagger), the front first.
- **Members in contact.** With no enemy within 3 m a member steers to his slot (a missile man with a foe inside 40 m stays and shoots); in contact the brain fights and the leash applies after it: 2 m, 6 m in a charge or loose order, 1 m on a hold, none when braced; beyond it he walks back; a step that would leave it is not taken. An archer's `SafeShot` refuses a target with any friend within 5 m of it and, under a shoot-at-target order (`ShootAt`), any other formation. Hold fire is the formation's `FireMode.Hold` (the brain's own hold-fire path).
- **Formation morale.** A tenth of the members with a side or rear blow in the last 5 s gives every member the flank term (the blow is recorded per member by `NoteFlankBlow`); at 500 permille routing the rest get -20 once and every friendly formation whose anchor is within 30 m plus both half-frontages gets -10 once (the flag clears below 250 permille); a shield wall or schiltron at cohesion 600 or more gives the +5 term (not on top of the Phase 4 brace); the whole formation feels its captain fall; the hero's aura reaches `10 m + Leadership / 5` as a captain's reach (and so the rally rule's); routed men run toward their side's entry point (`SetEntry`) as well as away from foes.
- **Captains.** The captain is the member with the best (officer flag, Tactics, Leadership, id); `Form` takes the officer set and per-fighter Leadership from the caller (the kit carries Tactics only, so the book keeps Leadership). He acts when the player has not ordered the formation for 30 s or `CaptainAlways`, and decides every `max(40, 200 - 2 Tactics)` ticks, no delay. The rules are four channels (shape, movement, fire, facing); the best score in each is carried out, ties by stream 0x780. A Tactics 50 captain also checks for a flank every second (`flank_alert_ticks` 20, a reaction rather than a plan, so that "wheels within 5 s" holds: 1 s to see, 3 s to turn 90 degrees at 30 degrees a second) and needs three enemies on the flank. "Archers loose at 120 m" is read as: an enemy missile man with ammunition within 150 m. A player's hold on a formation stays until the captain's takeover rule (30 s, or `CaptainAlways`) applies; a hold the captain made (outnumbered 2:1, reserve) lifts when its reason does.
- **Saved:** `FormationBook.Pack` (every integer: hero, entry points, lost orders, each row with its orders, queues, pending orders, slots, vacancies and per-member state); `Restore` rebuilds the rows and the fighters' `FmRow` and `FmMember`; the derived layout is recomputed. A saved mid-battle fight continues to the same hash and the same bytes (`Save_SaveLoadContinue_EqualsTheUninterruptedBattle`).
- **Not built here (and who has it):** the battle bubble, reinforcement waves, the box and demotion (BB-C); the HUD, order wheel, tactical overlay, F1 to F4 and Shift+F1 to F4 bindings and the `0` key (BB-D: `InputDefs` and `FightView` are untouched, so Phase 4's F-keys still send squad shapes until then); rendering (BB-E); the wedge (CV), wagon fort (SG); the `Dismount` and `Mount` orders act in the cavalry layer; a runner does not physically walk.
- **Measured** (this machine, shared; `FormationBudgetTests`): 2,000 soldiers in 20 formations of 100 (shield, spear, sword and bow, 4 ranks), two lines 20 m apart, ticks 21 to 620 (contact from about tick 200), captains on: see the overview's Built note for the figures; the gates are 25 ms average and 33 ms the worst tick of a minute.
- **Tests:** `Combat/Battle/FormationTests.cs`: BB6 (class and skill order, exact positions to the mm at 30 degrees, order independent of the order given, rings), BB7 (40 and 28 ticks, flank closing), BB8 (the 20th percentile, the 600 stop), BB9 (150, 75, 50 and 25 s; head-first release), BB10 (voice, horn, runner, runner killed, queue), BB11 (pause), BB12 (flank term, rout cascade once, wall bonus, fallen captain), BB13 (shield wall from Tactics 30, wheel from 50, takeover), steering and leash, safe shot and hold fire, the entry point, orders in the field, determinism and the save round trip.
