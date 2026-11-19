# Real-time melee and ranged combat: timings, stamina, shields and shield walls, bows and crossbows, thrown and improvised weapons

Plan refs: §4A.3 (real-time melee table: wind-up and recover per weapon, strike window 0.10-0.15 s, timings x(1 - skill/400) and +1% per kg over-encumbrance; shield block 100 degree arc, heater 0.45, tower 0.75, shield wall +0.10 per braced neighbour up to 2; parry 0.20 s window, success skill/120, 0.4 s riposte; dodge 0.35 s with 1.0 s cooldown; feint in the first half of a wind-up; stamina 100 with costs and regeneration; ranged: arrows 55 m/s, bolts 65 m/s, aim cone sigma = 3 degrees x (1 - skill/120), a projectile hits whoever is in its path, reload longbow 5 s and crossbow 10 s, finite quivers), §2.8 (combat stance context: LMB attack, RMB block, tap RMB in the 0.2 s before impact = parry, RMB during own wind-up = feint, Ctrl + direction = dodge, G throw, T called shot), §2.6 (all blows through one resolver; missiles take momentum from the launcher; thrown objects), §2.10 (improvised weapons, throw anything), §2.2a (height gives archers range and a better hit chance and lets them shoot over crowds), §5 Phase 4 ("about 18 weapons across materials").
Code today: the Phase 0 spike `Sim/Combat/Melee/` (`MeleeRules`, `MeleeWorld`, `MeleeAi`, `Fighter`, `Act`, `MeleeEvent`, `MeleeCommand`, `AttackRequest`) and the benchmark in `docs/spikes/02-melee.md` (A-wins, defended blows about 12%).

## 1. Purpose
Turn the spike's tested state machine into the shipped fighting layer: the same timeline and numbers, rewritten on integers and positions in millimetres, extended with shield walls, polearm ranks, reach, projectiles with real paths, thrown and improvised weapons, and an AI that actually defends (spike finding: only about 1.2 of 10 blows were blocked or parried). Everything that lands goes to resolver-v2.md; everything that bleeds goes to wounds-bleeding-pain-healing.md.

## 2. Dependencies
| Needs | From |
|---|---|
| `StrikeResolver` v2, blocks (resolver-v2 4.8), hit chance | `resolver-v2.md` |
| `BodyState` capacities (arm, grip, overhead, sight, balance) | `anatomy-and-body-plans.md` |
| `LiveWorld`, `PeopleBridge`, positions, `UtilityBrain`, `ActionKind`, `InterruptBus` | exists (`Sim/Live`, `Sim/Behaviour`, `Sim/Travel`) |
| Weapon and armour shapes, `Fit`, encumbrance | exists (`content/base/items`, `Sim/Combat/CombatModels.cs`) |
| Formation cards F1-F4 | plan §2.8 (squad orders, Phase 3 followers) |
| Height, levels, line of sight between levels | exists (`Sim/World/LevelGrid.cs`) |
| Weather (wind, rain, fog) as it touches aim and footing | `exploration-and-sites.md` 4.8 (the one table for weather in combat) |
New code: `Sim/Combat/Melee2/` (`FightWorld`, `Fighter`, `ActionTimeline`, `Defence`, `ShieldWall`, `CombatBrain`), `Sim/Combat/Missiles/` (`Projectile`, `ProjectileStep`, `Launcher`, `Quiver`, `Thrown`, `Improvised`), content `weapons/*.jsonc` extended (`launcher`, `ammo` types), tool `tools/calibrate`.

## 3. Data

### 3.1 Weapon shapes (18, `items/weapons.jsonc`, existing 9 kept and extended)
`weapon_shape` rows keep `head_volume_cm3`, `haft_volume_cm3`, `wind_up_s`, `recover_s`, `two_handed`, `attacks[]`; **new fields** `reach_mm`, `strike_ticks` (2 or 3), `guard_arc` (`weapon_block_pm`), `family` (`blade, axe, hafted, polearm, dagger, ranged, shield`), `hands` (1 or 2), `thrown` (bool or object), `unwieldy_pm`, `bash` (second attack for the haft or pommel).
| Id | Family | Wind-up / Recover (s) | Ticks at skill 0 (20 Hz) | Reach (m) | Strike ticks | Main attacks (type, area cm2, pen mm, velocity) | Mass (kg, steel) |
|---|---|---|---|---|---|---|---|
| `dagger` | dagger | 0.30 / 0.30 | 6 / 6 | 0.8 | 2 | stab (point, 10, 120, 1.3); slash (edge, 12, 40, 1.2) | 0.33 |
| `arming_sword` | blade | 0.45 / 0.40 | 9 / 8 | 1.3 | 2 | slash (edge, 30, 70, 1.25); stab (point, 15, 450, 1.25); pommel | 1.45 |
| `longsword` | blade | 0.50 / 0.45 | 10 / 9 | 1.5 | 2 | slash (30, 85); stab (550); pommel | 1.9 |
| `falchion` | blade | 0.50 / 0.45 | 10 / 9 | 1.2 | 2 | chop (edge, 35, 80, 1.3) | 1.6 |
| `hand_axe` | axe | 0.45 / 0.45 | 9 / 9 | 1.0 | 2 | hack (edge, 30, 70, 1.25); thrown | 1.0 |
| `axe` (battle axe) | axe | 0.60 / 0.60 | 12 / 12 | 1.2 | 3 | hack (40, 110, 1.25); haft strike | 1.35 |
| `great_axe` | axe | 0.85 / 0.80 | 17 / 16 | 1.7 | 3 | hack (edge, 50, **160**, 1.2) | 3.3 |
| `mace` | hafted | 0.70 / 0.70 | 14 / 14 | 1.1 | 3 | bash (blunt, 20, 0, 1.0) | 2.2 |
| `flail` | hafted | 0.65 / 0.70 | 13 / 14 | 1.4 | 3 | swing (blunt, 18, 0, 1.2); **ignores shield cover x0.5** | 2.0 |
| `war_hammer` | hafted | 0.60 / 0.60 | 12 / 12 | 1.2 | 3 | strike (blunt, 10, 0, 1.1); spike (point, 10, 150) | 1.8 |
| `maul` | hafted | 0.90 / 0.90 | 18 / 18 | 1.5 | 3 | smash (blunt, **25** (was 60), 0, 0.9) | 5.3 |
| `club` | hafted | 0.55 / 0.50 | 11 / 10 | 1.0 | 2 | bash (blunt, 40, 0, 1.0) | 0.8 (oak) |
| `quarterstaff` | hafted | 0.40 / 0.40 | 8 / 8 | 1.8 | 2 | strike (blunt, 30, 0, 1.0); thrust (blunt, 12) | 1.4 (ash) |
| `spear` | polearm | 0.40 / 0.50 | 8 / 10 | 2.4 | 2 | thrust (point, 12, 400, 1.3); butt | 1.1 |
| `pike` | polearm | 0.70 / 0.80 | 14 / 16 | 4.5 | 3 | thrust (point, 12, 500, 1.3); **braced** (mounted-combat.md) | 3.0 |
| `javelin` | polearm, thrown | 0.35 / 0.50 | 7 / 10 | 1.8 melee | 2 | thrust (point, 10, 350, 1.2); thrown 4.6 | 0.8 |
| `longbow` | ranged | draw 0.9, hold, release 0.2 | n/a | n/a | n/a | launcher 4.5 | 0.9 |
| `crossbow` | ranged | span 10 s reload, release 0.15 | n/a | n/a | n/a | launcher 4.5 | 3.2 |
(Shields, separate slot, in 3.2. Maul contact area drops from 60 to 25 so that its blunt need (proportional to area) is no longer worse than a mace's: a calibration fix, resolver-v2 R2.)
Timing in ticks `t = round(seconds x 20)`; skill scaling in integers: `t_eff = t x (400 - skill) / 400` rounded up to at least 3 ticks for wind-up, 2 for recover; over-encumbrance `+1% per kg` over capacity (`15 + 2.5 x strength/..` as plan; stat on the 0-100 scale, plan formula unchanged); stamina below 25: wind-up x1.3; weakened by blood loss (wounds file): x1.2; riposte wind-up x0.5.
Qualities and materials multiply as `CombatTables` already does (about 20 materials x 7 qualities per shape; "across materials": bronze, iron, steel, wood, bone).

### 3.2 Shields (separate slot; `items/shields.jsonc`, new type `shield_shape`)
| Id | Cover (permille) | Capacity C (resolver 4.8) | Durability | Mass kg | Notes |
|---|---|---|---|---|---|
| `buckler` | 250 | 160 | 900 | 0.9 | iron; hand-held; usable with a one-handed weapon in the same hand; parry bonus +10 points |
| `round_shield` | 400 | 190 | 600 | 3.0 | wood + iron boss |
| `heater` | 450 | 200 | 700 | 3.5 | plan value |
| `tower` | 750 | 300 | 1100 | 7.5 | plan value; no sprint while held |
Cover is the chance a frontal blow is shield-blocked when the defender is guarding; `+100 per braced neighbour` (max 2), clamp 950. A collarbone break on the shield side: cover -150 and no head cover (anatomy 3.2). A blocked blow that exceeds C passes the excess to the forearm and shakes the shield (resolver 4.8).

### 3.3 Combat record (`Fighter`, live only; saved in `FightRecord`, see overview 8, item 3)
`Id` (person), `Team`, `PosMm` (int x, y, z level), `FaceQ10` (int vector, 1024 = 1.0), `Act` (Idle, Windup, Strike, Recover, Block, Dodge, Stagger, Down, Dead, Fled, Surrendered, Bracing, Reloading, Drawing), `ActTicks`, `Req` (attack id, target part, gap, style), `Stamina` (x100), `DodgeReadyTick`, `BlockSince`, `RiposteUntil`, `Stance` (watch mode, watch-mode.md), `AiState`. 64 bytes. Everything the spike held as `double` is `int` (mm, milli-stamina): the spike's test `SeededDuelIsDeterministic` becomes a cross-OS hash test.

## 4. Rules

### 4.1 The action timeline (every attack)
Idle -> **Windup** (`wind` ticks; hold LMB to wait inside the wind-up; hold 6 ticks past it for a **Heavy** swing) -> **Strike** (`strike_ticks`: the damage tick is the first tick of Strike; the window the defender can parry/dodge is the 4 ticks before it, 0.20 s) -> **Recover** (`recover` ticks; cannot act, blocks at half cover). Quick = release inside the wind-up (x0.5 momentum, wind-up x0.6, stamina x0.6); Heavy = x1.5 momentum, +6 ticks, stamina x1.5. Facing blends toward the target at 540 degrees/s (`FaceQ10` lerp, no trig: precomputed 64-entry rotation table).
Geometry (integer, mm and Q10 vectors): a blow lands only if the target's centre is within `reach_mm + 400` and the dot product of `FaceQ10` with the direction to the target is **>= 512** (cos 60 degrees = 0.5, the `AttackConeCos` of the spike) at the Strike tick. Front arc of the defender (100 degrees): dot of defender facing with the direction to the attacker **>= 658** (cos 50); rear (90 degrees behind): **<= -724** (cos 135); otherwise flank. A side-step therefore makes a swing whiff (`Whiff` event).

### 4.2 Defence
- **Block** (hold RMB; guard flag on, cover = weapon `300 + 2 x skill` permille, plus shield cover; cost `3 + M/50` stamina per absorbed blow). The resolver applies it (4.8 there). Guarding (blocking or not attacking) regenerates stamina at 5/s; resting 12/s.
- **Parry** (tap RMB in the 4 ticks before impact): success `P = skill/120` capped 95%, +10 points with a buckler or main-gauche (stream 0x430 key `FightTick`, defender, draw = incoming blow serial). Success: blow negated, attacker `Stagger` (recover + 4 ticks), defender gets a **riposte** (8 ticks, next wind-up x0.5). Failure falls through to the block roll. No cost on success, 4 stamina on failure.
- **Dodge** (Ctrl + direction): 7 ticks (0.35 s) of movement at 4.3 m/s (about 1.5 m), then 20 ticks (1.0 s) cooldown after it ends. While active a blow is evaded with 80% (0x430). Cost 12. Cannot dodge while in Windup or Strike. Armour over 20 kg: -25 points evade.
- **Feint** (RMB during the first half of one's own wind-up): cancels to Recover of 3 ticks, costs 2 stamina plus the swing cost already paid; a defender who started a parry in response mis-times: its parry window is consumed (the defender's parry attempt for the next 8 ticks has `P = 0`). One feint per 30 ticks.
- **Overpowered:** a blocked blow above capacity staggers the blocker 10 ticks.
Defence rate target (calibration): 40-60% of blows are blocked, parried or dodged (spike: 12%). Levers, in this order: the AI's reaction chance (4.7), parry window readability, shield cover, weapon cover.

### 4.3 Stamina (integer x100, so 100.00 = 10000)
| Event | Cost |
|---|---|
| Swing | `4 + 2 x kg(weapon)` x style (quick 0.6, heavy 1.5) |
| Block | `3 + M/50` per absorbed blow |
| Dodge | 12; feint 2; shield bash 8; stand up 30; sprint 8 per second; throw `5 + 2 x kg`; bow hold 1 per second after 1.2 s of draw |
| Regeneration | rest 12/s, guard or armour > 20 kg 5/s, nose broken x0.8, broken ribs x0.7, infection level 2 x0.7 |
Below 25: wind-ups +30%, blocks -0.2 cover. At 0: cannot swing; may block with -0.4 cover. Skill 50, steel sword: a swing costs `4 + 2.9 = 6.9`; a 20-second duel of 12 swings and 8 blocks costs about 140 against regeneration of 80: stamina bites in fights longer than 25 s, matching the spike's note (7).

### 4.4 Shields and shield walls
- A **shield wall** is a formation (F2) of >= 3 shield bearers spaced <= 0.9 m, facing within 30 degrees of each other, each **braced** (moved less than 1 m in the last 2 s, not in Windup, Recover or Stagger). A bearer's `braced neighbours` = such bearers adjacent left and right (max 2): cover `+100 permille` each (plan +0.10), so a heater in a full wall covers 650.
- Second rank: a spear or pike bearer directly behind a braced bearer (within 0.9 m) may attack **through** the wall with its `reach_mm` (the attack passes over the shield: enemy cannot shield-block it, enemy may weapon-block); pikes 2 ranks deep.
- **Push:** a charge (running attacker, closing speed >= 4 m/s) into a shield bearer deals no resolver blow, only a shove: bearer displaced `0.3 m x (attacker mass/ bearer mass)` and loses braced status for 10 ticks; walls with >= 2 braced neighbours resist (displacement /3). This gives cavalry and heavy infantry something to do to walls (mounted-combat.md uses the same rule).
- A **flail** ignores half of a shield's cover (its head arcs around). A **flank or rear** attack bypasses the wall (cover applies only front arc).
- Shield break: durability 0 -> `Broken` item (item condition; repairable at a smith, Phase 5).

### 4.5 Ranged: bows and crossbows
Launcher content (`launcher` type): `power_pm` (longbow 1000, crossbow 600), `min_strength` (longbow 1000: a weaker archer's power scales `strength/1000`), `draw_ticks` (longbow 18; at skill 50, x(400 - 50)/400), `reload_s` (longbow 5, **crossbow 10**, plan), `ammo` (arrow or bolt), `range_effective_m` (150, 100), `range_max_m` (280, 220), `v0_mps` (arrow 55, bolt 65).
Ammo content (`ammo` type): `bodkin_arrow` (point, area 2 cm2, pen 250 mm, mass 45 g), `broadhead_arrow` (edge, area 12, pen 120, 40 g), `blunt_arrow` (blunt, area 20, practice and hunting small game), `quarrel` (point, area 3, pen 200, 70 g). Heads iron or steel (material of the head, resolver tables); quivers hold **24 arrows** (a second quiver: 48; weight 25 g shaft included) or **20 bolts**; arrows are lost or **recovered 50%** after a fight (stream 0x441: breakage on a hit 60%, a miss 20% breakage).
- **Momentum** (launcher-based, not strength): `M_milli = mass_g x v_mps x power_pm x 485 / 10,000`, with velocity drag `v(d) = v0 x (1000 - 1.2 d_m)/1000`. Longbow bodkin at the muzzle: `45 x 55 x 1000 x 485/10000 = 120,037` (120.0); at 100 m (v x 0.88) 105.6. Crossbow quarrel `70 x 65 x 600 x 485/10000 = 132,405`. Bodkin M 120 at point-blank cuts iron mail (need 48) and steel plate (need 116.8 against an iron head vs steel plate, area 2) only inside about 10 m; beyond that plate stops it. Armour matters against arrows.
- **Aim.** Error cone `sigma = 3 degrees x (1 - skill/120)` at full draw and a full second of aim; released early (draw only) `x2`; a quick shot (< 0.5 s after full draw) `x3`; moving > 1.5 m/s `x1.5`; one eye lost `x1.4`; rain `x1.15`, wind (exploration 4.8). Lateral offset `d x tan(sigma)`: small-angle `theta_rad = deg x 17,453 / 10^6`; the draw `N(0, sigma)` uses a 256-entry integer inverse-normal table (stream 0x440, key `FightTick`, shooter, draw = shot serial; two draws: lateral and vertical). Hit chance on a man-sized target at 30 m, skill 50, aimed: lateral sigma `30 x 0.0305 = 0.92 m`, width +-0.25 m: **about 21%**; skill 100: sigma 0.26 m: **about 66%**. Volleys matter, as in life.
- **Path.** A projectile is a segment per tick (`v/20` m per tick: arrow 2.75 m, bolt 3.25 m) tested against entity capsules (radius 250 mm, height 1.8 m, from `PosMm`) and against walls and doors (material hit points via the resolver for doors). **It hits the first entity along the path, friend or foe** ("a projectile hits whoever is in its path"). Per-level: blocked by floors; line of sight between levels only through openings.
- **Part hit:** the vertical coordinate at impact picks the part from a height table (feet 0-0.1 m, lower leg 0.1-0.5, upper leg 0.5-0.9, lower body 0.9-1.1, upper body 1.1-1.5, neck 1.5-1.6, head 1.6-1.8; arms by lateral offset beyond 0.18 m); the called-shot target (T) sets the aim point to that part's centre height. The impact calls `ResolveImpact` with `ExtraMomentumMilli` and no hit roll.
- **Height:** each level of elevation over the target: +10% range and `sigma x 0.9`; shooting from a wall-walk (+1) over a crowd of 40 m: the arc (`apex = 0.04 x D` metres) clears targets in front.
- **Auto-lob:** when the straight path is blocked by a friendly entity and `D >= 25 m`, the shot arcs: `sigma x 1.5`, range +15%, and the projectile skips entities in the first 70% of the path whose height < apex arc height at that point (parabola `h(f) = 4 x apex x f x (1 - f)`).
- **Reload / draw:** `5` (CombatStance) reloads or nocks now or cancels (`]` is the Phase 3 cart reload and stays so); the setting *Auto-reload when idle* (default on) does it by itself while the shooter stands still; progress is shown as a bar; crossbow reload 10 s is cancelled by movement above 1.5 m/s (spanning needs standing); a longbow nocks in 1.0 s (no separate reload, only quiver draw).

### 4.6 Thrown and improvised weapons
- **Throw** (G, then aim with the mouse; a cone shows as for bows): velocity `v = 18 x 3/(3 + kg) x clamp(strength/1250, 0.5, 1.6)` m/s; momentum `M_milli = mass_g x v x 9.7` (javelin 0.8 kg: v 14.2, M 110,200; dagger 0.33 kg: v 16.2, M 51,900; hand axe 1.0 kg: 13.5, M 131,000; rock 0.5 kg: 15.4, M 74,700). Max range `min(30, 1.8 x v)` m; the weapon becomes a `ground item` at the impact point (recoverable, 25% damaged chance for thrown knives and spears on stone). Aim sigma `3.5 degrees x (1 - skill/120)`, skill = weapon-family skill for javelin, dagger and hand axe, `Athletics` otherwise. The throw takes 7 ticks wind-up, 10 recover. A thrown item hits **point first** only if designed to (javelin, dagger, hand axe: `edge/point`); anything else lands blunt.
- **Improvised weapons** (anything with a shape): `AttackDef` synthesised: blunt unless the shape has an edge tag; contact area `min(60, volume_cm3^(2/3) x 2)` cm2; velocity 0.7; wind-up `0.45 + 0.1 x kg` s, recover the same; hit chance -10 points ("unwieldy"); **fragile**: a wooden stool or plank loses `M/3` thousandths of condition per absorbed or delivered blow and breaks at 0 (a chair breaks after about 3 hits). Examples: torch (blunt 0.8 + `ignites`: lights thatch and draugr, exploration 4.5), pitchfork (point, area 5, pen 200, as a poor spear), frying pan, fence post, bottle (breaks after one hit: 8 stamina bash, leaves `shards` item).
- Unarmed (`Melee` skill): fist (blunt, area 15, velocity 0.8, wind-up 0.3/0.3), kick (blunt, 30, velocity 0.9, 0.5/0.5); a boxing bout (tournament: yield at pain, plan §4A.3) uses blunted rules.

### 4.7 The combat AI (`CombatBrain`) and how it picks an action
Extends the spike's `MeleeAi`; same brain for NPCs, enemies, party members and the hero in watch mode (plan §6: "the hero on watch mode uses exactly the NPC combat AI"). Per fighter, every 4 ticks (5 Hz) except on a trigger:
1. **Reactions:** once per incoming wind-up, inside the last 0.2 s, pick parry (`0.15 + skill/130`), dodge (12% if free and stamina >= 12), hold block (35%), take it (rest). Probabilities shift with `Stance` (defensive +20 parry), stamina (< 22: guard) and **shield** (always block with shield if the blow is front arc; spike gap).
2. **Offence:** attack when the foe is in Recover or Stagger, or after a successful parry (riposte), else wait with guard; feint against a guard-holding foe `skill/300`.
3. **Target and part:** nearest active enemy in front arc; skilled AI aims at the weakest zone by vulnerability (armour gaps) with probability `skill/150`; weapon choice by situation (spike follow-up): point or blunt against armour, edge against bare flesh.
4. **Range management:** archers keep 15-40 m, retreat to maintain it if a melee enemy closes; spears hold at `reach - 0.3` m.
5. **Morale** (morale-capture-succession.md) can override: rout, surrender.
Utility integration: `ActionKind` appends `Fight` and `Treat` (`ActionPolicy.Kinds` 15 -> 17, content `utility.jsonc` weights); an `Attacked` interrupt (Phase 3 `InterruptKind`) puts the person in `Fight` unless policy `Flee` scores higher (existing `Flee` action).
Randomness (streams 0x430 `MeleeAiChoice`, 0x431 `AiTarget`, 0x432 `StanceNoise`, 0x433 `Stagger`) all keyed `(FightTick, fighter)`.

## 5. Tick placement (20 Hz live versus abstract)
- Combat steps run only in the **live bubble** at 20 Hz: `FightWorld.Step` after the LODBridge and before the behaviour layer. **A fight never lengthens a tick**: at speed 2x, 4x, 8x the world steps `speed` times per rendered frame (watch mode, §3i: normal speed controls); if the 25 ms battle budget is exceeded, the speed drops automatically rather than skipping steps.
- Projectile step: after fighters, before wound application.
- Fighters out of the bubble: no melee. Their fights are abstract (abstract-battles-v1.md).
- AI at 20 Hz within 60 m of the player and each active squad, 5 Hz beyond (plan §2.6, "level of detail applies only to AI and rendering"): test ML-16 asserts the outcome distribution is equal.

## 6. Determinism
Streams (0x400 block; key = `FightTick` (the fight's own counter from its first tick, advanced 1 per 20 Hz step, never by the world clock), entity = acting fighter, draw = event serial): 0x430 `MeleeAiChoice`, 0x431 `AiTarget`, 0x432 `StanceNoise`, 0x433 `Stagger` (knock-down duration), 0x440 `AimError` (two draws per shot), 0x441 `AmmoBreak`, 0x442 `ThrownAim`. Positions in mm, vectors in Q10, no `double` in the fight step (spike's doubles are replaced); `FightHash` (fighters, tick) joins the daily trail. Blows that land in the same tick resolve against the state at the start of the tick (spike review fix, kept), in ascending fighter id for effects only.

## 7. UI and controls
Combat stance (X toggles; also automatic when attacked; plan §2.8) and World keys as in the plan:
- **LMB** hold = wind-up, release = strike (attack type from the weapon: default **Auto**, `6` cycles `attacks[]`, the radial R lists them; the mouse wheel stays camera zoom); **RMB** hold = block, tap before impact = parry; RMB in the first half of own wind-up = feint; **Ctrl + WASD** dodge; **T** called shot (pauses); **G** throw; **5** reload or nock; **6** cycle the attack type. The two digits are new (overview 6A); `]` and `[` are the cart keys and are not used here.
- **Readability:** a bar over the foe (wind-up fill orange; **the last 0.2 s is a red tail** = the parry window, spike), an arc shown on the ground for the 100 degree block and the 60 degree attack cone while in stance, stamina bar with the 25 mark, an aim fan for bows, throws and crossbows, a reload ring. The three-view animation set (idle, walk, jog, wind-up, strike, recover, block, parry, hit-react, stagger, fall, crawl) is baked from the rig (feedback file).
- **Accessibility:** hold-or-toggle for block and sprint; parry window enlargement option (+2 ticks) in settings (changes only the player's own success, flagged in the log); aim assist off by default.

## 8. Tests (golden)
| Id | Check |
|---|---|
| ML-1 | Timing table in ticks at skill 0 for the 18 weapons (3.1) and skill 50: sword `9 x 350/400 = 7.9 -> 8` wind-up, 7 recover |
| ML-2 | Attack cone and arcs: dot thresholds 512 / 658 / -724; a target at 61 degrees is whiffed, 59 hit |
| ML-3 | Parry window exactly 4 ticks; `P = skill/120`: 10,000 seeds at skill 60 -> 50% +-1.5 |
| ML-4 | Dodge: 7 ticks, 20 tick cooldown after; evade 80% +-1.5; blocked while in Windup |
| ML-5 | Feint in first half only; second-half RMB is a block; a feint consumes the defender's parry for 8 ticks |
| ML-6 | Stamina: swing cost 6.9 (steel sword), block cost `3 + M/50`; regen 12 rest, 5 guard or armour > 20 kg; below 25 wind-up x1.3 |
| ML-7 | Shield wall: 3 braced heaters, middle cover 650; moving breaks bracing; flank attacks ignore cover |
| ML-8 | Second-rank spear attacks through a wall; enemy cannot shield-block it |
| ML-9 | Longbow M at 0 m 120,037, at 100 m 105,600 (+-1%); crossbow 132,405 |
| ML-10 | Aim cone: skill 50 sigma 1.75 degrees -> 30 m hit rate on a 0.5 m target 21% +-2 (50,000 shots); skill 100 66% +-2 |
| ML-11 | Projectile hits the first entity on the path, including an ally; passes over allies on auto-lob at D >= 25 m |
| ML-12 | Reload times: longbow nock 1.0 s, crossbow 10.0 s, cancelled by movement; a quiver of 24 empties after 24 shots |
| ML-13 | Throw: javelin v 14.2 m/s, M 110,200, range 25.6 m; improvised stool breaks after 3 +-1 blows |
| ML-14 | Defence rate on the calibration set (2,000 duels per matchup): 40-60%; mean duel length 10-20 s; spear vs unarmoured sword >= 55% |
| ML-15 | Determinism: a 10 v 10 skirmish over 600 ticks gives the same `FightHash` on both OS and with watch mode on and off (same seed) |
| ML-16 | LOD: near and far soldiers (AI at 20 Hz vs 5 Hz) give kill-rate and wound-class distributions within 5% over 200 skirmishes |

## 9. Open questions (recommended defaults)
| # | Question | Default |
|---|---|---|
| M1 | Skill names: plan lists "weapon families"; Phase 3 `Skill` has only `Melee` and `Archery` | Append `Blades, Axes, Hafted, Polearms, Shields, Throwing` (save migration copies old `Melee` level to all families at 80%); `Melee` stays for unarmed |
| M2 | Parry window comfort at 20 Hz | 4 ticks (spike); accessibility option +2 ticks; revisit after the human feel test of the spike scene |
| M3 | Can the player block and attack in the same hand with a buckler? | Yes (one-handed weapon + buckler in the shield hand); two-handed weapons exclude all shields |
| M4 | Friendly fire from arrows | On (plan: hits whoever is in its path); auto-lob reduces it; a "hold fire" order in the order wheel |
| M5 | Do bows need to be unstrung when idle? | No (not simulated) |
| M6 | Ammunition limits for NPC archers | 24 arrows, no resupply in the field; fewer for bandits (12) |
| M7 | Stamina drain realism vs readability | As plan numbers; show stamina bar prominently |

## 10. Budgets
| Item | Budget |
|---|---|
| `FightWorld.Step` per fighter | <= 1.5 us (spike: 200 duels < 1 s); 300 fighters <= 0.5 ms; 2,000 <= 3 ms |
| `CombatBrain` per fighter at 5 Hz | <= 4 us; at 20 Hz near the player <= 4 us (x 4) |
| Projectile step | <= 2 us each, <= 600 in flight = 1.2 ms |
| Battle bubble total (2,000 soldiers) | <= 25 ms per tick of which melee+ranged <= 9 ms (overview budget table) |
| Memory | `Fighter` 64 bytes; `Projectile` 48 bytes; 2,000 + 600 = 160 KB |
| Content | 18 weapon shapes, 4 shield shapes, 2 launchers, 4 ammo, ~12 improvised templates: <= 40 KB |

## Notes (P4-C implementation, step 3)
Code: `src/Ravensguard.Sim/Combat/Melee2/` (`FightWorld` with `.Impact` and `.Ranged`, `Fighter`, `FightTypes` (Q10 maths, the 64-direction table, the 256-entry inverse-normal table, inputs and events), `CombatBrain`, `FightContent`, `FightSetup`, `CombatSkills`, `FightCalibration`), `Save/FightSave.cs` (`FightRecord`, the "fight" section), content `combat/calibration.jsonc` (`melee_tuning` row: every tuned number; the rest are the defaults in `MeleeTuningDef`), `combat/ranged.jsonc` (2 launchers, 4 ammunition), `combat/improvised.jsonc` (12 objects), `combat/stances.jsonc` (the six presets), `combat/fight_kits.jsonc` (road and site groups), weapons `fist` (bare hands) and a `flat` attack on the blades. Godot: `World/Combat/FightView.cs` (the combat stance and the called-shot doll), `World/Combat/FightDemo.cs` (`-- --fight-demo duel|skirmish`, screenshots), `World/StreetView.Fight.cs` (the road fight). Tests `tests/.../Combat/Melee2/` (ML-1 to ML-16 and the done-when checks; `ML14_CalibrationTargets_2000DuelsPerPairing` is `Category=Soak`).
Decisions taken while building:
- **Time:** one fight step is one game second; `LiveWorld` advances its clock by exactly one game second per step while a fight is attached, whatever the speed (overview 6.1: a fight never lengthens a tick), then advances the wound ledger and steps the fight. The hero's feet belong to the fight while it lasts.
- **Timeline:** the impact is the first Strike tick; a parry is a guard pressed in the 4 ticks up to and including the impact tick (`Tick - BlockSince < 4`); a quick swing is a release inside the wind-up (it lands once 0.6 of the wind-up has run); a heavy swing is a hold 6 ticks past it. A dodge lasts 7 ticks of evasion and of movement (1.5 m) and is ready 20 ticks after its last tick. A guard held through one's own recover or stagger comes up the tick it ends (that tick counts as the press). The skill scaling rounds **up** (`t x (400 - skill) / 400`), so ML-1's sword is 8 / 7 at skill 50.
- **Defence beyond the spec:** a fighter recovering or staggered blocks at half cover (`recover_cover_pm`, `stagger_cover_pm` 500); a parry's success is scaled by the attack's `parry_pm` (a mace 600: its weight drives through a blade); the dodge roll lives in the melee layer (heavy armour -25 points), the parry roll too, the block capacity and the excess in the resolver (`Blocks`). A polearm behind a braced shield bearer strikes over the wall (`OverWall`): no shield block against it.
- **AI (4.7):** a reaction is a trigger, decided once per incoming blow inside its last 4 ticks and carried out on time (a fighter still recovering parries or dodges only if the recover ends in time; the dodge share falls to the block when the dodge is cooling). Offence attacks on an opening (the foe recovering, staggered or down, or a riposte) with 85%, else when a patience of 12-48 ticks (stance-biased) runs out; feints skill/300 against a guard; aims at the weakest zone with skill/150, and against rigid plate a point attack goes for a gap the harness leaves (armpit, groin, face, back of the knee). Attack choice: rigid plate blunt > point > edge; **mail** point > edge (mail turns a cut into a blunt blow at 95%, RS-10) > blunt; soft edge > point > blunt. LOD: 20 Hz within 60 m of the hero (or everywhere when there is none), 5 Hz beyond, staggered by fighter id; reactions are triggers and run every tick (ML-16: kill and wound-class shares within 5 points over 200 skirmishes).
- **Morale** keeps the spike's rules (2 Hz, rout and surrender, a stance's flee thresholds) until morale-capture-succession replaces them. **Ranged:** aim sigma interpolates from x3 (a shot inside 0.5 s of full draw) to x1 at a full second; a missile hits the first capsule on its path (radius 250 mm, 1.8 m, 0.4 m lying), friend or foe; the auto-lob arcs when a friend is in the corridor and the target is 25 m or more away; a raised shield catches a frontal missile by its cover. `ResolverV2` gained `StrikeV2.Missile` (the momentum is the launcher's or the throw's alone) and `CombatTablesV2.Synthetic` (arrow heads, improvised objects).
- **Improvised:** fragility is `momentum units x fragile_loss_per_unit_pm / 1000` of condition per blow delivered (4,800: a stool breaks after 3 +- 1 blows, ML-13); a bottle breaks on its first and leaves shards.
- **Skills (M1):** `Blades, Axes, Hafted, Polearms, Shields, Throwing` appended to `Skill`; anyone who never trained a family reads it as Melee x0.8 (`CombatSkills.Level`); save v6 writes the same into the hero record and the skill book's Melee rows. `ActionKind` appended `Fight` (Treat was 15, Fight 16; `ActionPolicy.Kinds` 17).
- **Save and replay:** a fight in progress is the "fight" section (`FightRecord`: kits, holdings, every integer of each fighter's state including the brain's memory and the held inputs, projectiles, ground items, spent ammunition; the books are the ledger's or the record's own). Save, load and save give the same bytes and the next 600 ticks the same `FightHash`. Every hero combat input is a `ReplayKind.Fight` command; a fight's start (`FightStart`) keeps its starting record in the session recorder so a replay rebuilds it; the live state hash includes the `FightHash`. The fight hash of a seeded 10 v 10 skirmish is a line of `WorldTool hashes` (the Windows/Linux compare, OV-1).
- **Encounter:** `EncounterChoice.Fight` now returns "fight"; the game builds `FightSetup.Encounter` (the cast kitted by `fight_kits.jsonc`, 7 m ahead or around the hero in an ambush) and settles it with `EncounterOutcomes.AfterFight` (held the field; ran; or down and robbed of a quarter of the purse until the defeat table).
- **Art:** the rig gained `parry`, `stagger`, `crawl` (todo left: carry, work), 20 wound-overlay layers (blood, bandage, splint per region, the stump of a lost forearm or shin) and the limb-split planes 3..17 (`3 + depth + 3 x group`) the fight view draws instead of 0..2 to leave a lost limb out. The atlas doubled to 1024 x 2838.
- **Content calibration (resolver-v2 R2, Q22-like):** the mace's bash velocity is 1.5 (was 1.0; DF's maces are 2.0) with `parry_pm` 600 and 0.65 / 0.65 s, the pommel strike 0.8 (was 1.0), the spear 0.50 / 0.60 s (was 0.40 / 0.50), the weapon block capacity base 150 (was 110), weapon cover base 380 permille (spec 300), the AI's parry base 170 (spec 150), crowded polearms -10 points inside 1.3 m. RS-6 and RS-7 keep table 4.4's mace (velocity 1.0) as their input; RS-24's table hash was re-pinned. The calibration "plate" kit is now a full harness (gauntlets, cuisses, backplate, mail coif): with open hands the AI's gap-seeking made plate a handicap.

### Calibration (tools/Ravensguard.Calibrate, 2,000 duels per pairing, skills 30-59, steel, the real AI; with first aid = the winner dresses the loser with TreatAi)
| Pairing | mean s | defended | A wins | severs/duel | KO'd who bled out: untreated / with first aid |
|---|---|---|---|---|---|
| sword vs sword (padded) | 15.0 | 52% | 50% | 0.00 | 47% / 8% |
| sword vs sword (unarmoured) | 15.2 | 52% | 51% | 0.00 | 47% / 10% |
| spear vs sword (unarmoured) | 11.0 | 44% | 60% | 0.00 | 33% / 7% |
| spear vs sword (padded) | 11.5 | 44% | 55% | 0.00 | 34% / 6% |
| axe vs sword (mail) | 14.0 | 42% | 20% | 0.00 | 42% / 6% |
| mace vs sword (padded) | 10.6 | 42% | 69% | 0.00 | 15% / 2% |
| sword vs sword (plate) | 21.2 | 48% | 49% | 0.00 | 13% / 4% |
| mace vs sword (plate) | 16.2 | 40% | 57% | 0.00 | 5% / 2% |
| sword (padded) vs sword (plate) | 18.2 | 50% | 43% | 0.00 | 34% / 6% |
| mace (padded) vs sword (plate) | 14.2 | 41% | 45% | 0.00 | 26% / 4% |
| sword+heater vs sword (padded) | 16.6 | 59% | 66% | 0.00 | 47% / 8% |
All knocked out: 31.2% bleed out untreated, 5.6% with first aid (target <= 35%). Off target and left: full plate against full plate lasts 21 s (the test allows 23); the mace is strong against padding (69%; maces crush through cloth); a padded axeman loses to a mailed swordsman (armour matters). Severs stay near zero (the resolver's C4 rule).

## Notes (Phase 4 close-out, step 8: items 1 to 5 of the close-out list, now in docs/reviews/phase-4.md)
Code: `Combat/Melee2/WeaponMatrix.cs` (the matrix), `Combat/Resolver2/ArmourWear.cs` (armour damage), `FightCalibration.RangedPairings`, the counter to the spear in `FightWorld.Impact.cs`
(`PassedThePoint`, `CrowdedMomentumPm`) and `CombatBrain.Decide`, tools `-- --matrix` and `-- --ranged`. Tests `Combat/Melee2/WeaponArmourTests.cs`.
- **The spear (item 1).** Back at plan 4A.3's 0.40 / 0.50 s (it alone won 67 to 69% at those timings before the counter). The counter: a fighter who parries a polearm's blow, dodges
  it, or beats its point aside with a block that stops it, when that polearm reaches at least 0.5 m further than his own weapon, is past the point and presses inside its reach for
  2 s (`inside_ticks` 40), closing to 0.8 m at a run; a polearm inside 1.3 m is choked up (its blow keeps `crowded_momentum_pm` 500 of its momentum, on top of the -10 points) and
  its AI steps back to use it again rather than strike, unless the foe gives it an opening (`crowded_step_back`). The floor holds and there is no upper bound: **spear against an
  unarmoured swordsman 62%, against a padded one 59%** (2,000 duels; the swordsman is pressing inside about 5 s of a 10 s duel and strikes 61% of his blows from within 1.3 m; 42% of the spear's blows are crowded, `-- --why`). Walking backwards at
  0.65 of the pace was tried and dropped: it moved the spear a point and broke the protect stance's footwork. Only polearms are countered (practice weapons and lances are not).
- **Weapon x armour (item 2).** The full matrix, the expectations, the cells that broke them and the fixes (rigid-only deflection, a failed edge skids off plate, a backplate is
  not met from the front, the maul's velocity 1.7, iron bodkin and quarrel heads) are in `docs/calibration/weapon-armour-matrix.md`. On the trunk (cut % / bruise % / momentum
  reaching the body % / serious wounds per blow / median time to down, s):

| weapon | cycle s | none | padded | leather | mail | mail+padding | plate | heater stops |
|---|---|---|---|---|---|---|---|---|
| dagger stab | 0.7 | 100 / 0 / 95 / 1.98 / 0.7 | 100 / 0 / 90 / 1.98 / 0.7 | 100 / 0 / 90 / 1.98 / 0.7 | 100 / 0 / 70 / 1.98 / 0.7 | 100 / 0 / 66 / 1.98 / 0.7 | 5 / 94 / 4 / 0.11 / 7.7 | 100% |
| dagger slash | 0.7 | 100 / 0 / 94 / 0.75 / 0.7 | 100 / 0 / 90 / 1.12 / 1.4 | 100 / 0 / 90 / 1.09 / 1.4 | 100 / 0 / 63 / 1.02 / 1.4 | 100 / 0 / 60 / 1.02 / 1.4 | 5 / 94 / 3 / 0.06 / 7.7 | 100% |
| arming_sword slash | 0.9 | 100 / 0 / 93 / 0.03 / 3.4 | 100 / 0 / 87 / 0.03 / 3.4 | 100 / 0 / 88 / 0.03 / 3.4 | 0 / 100 / 84 / 0.00 / 2.6 | 0 / 100 / 44 / 0.00 / 3.4 | 0 / 100 / 2 / 0.00 / 12.8 | 100% |
| arming_sword stab | 0.9 | 100 / 0 / 94 / 1.99 / 0.9 | 100 / 0 / 90 / 1.99 / 0.9 | 100 / 0 / 90 / 1.99 / 0.9 | 100 / 0 / 63 / 0.32 / 1.7 | 100 / 0 / 60 / 0.32 / 1.7 | 5 / 94 / 3 / 0.02 / 9.4 | 100% |
| falchion chop | 1.0 | 100 / 0 / 93 / 0.03 / 3.8 | 100 / 0 / 86 / 0.03 / 3.8 | 100 / 0 / 86 / 0.03 / 3.8 | 0 / 100 / 84 / 0.00 / 2.9 | 0 / 100 / 44 / 0.00 / 3.8 | 0 / 100 / 2 / 0.00 / 14.3 | 100% |
| longsword slash | 1.0 | 100 / 0 / 93 / 0.03 / 3.8 | 100 / 0 / 87 / 0.03 / 3.8 | 100 / 0 / 88 / 0.03 / 3.8 | 0 / 100 / 84 / 0.00 / 2.9 | 0 / 100 / 44 / 0.00 / 3.8 | 0 / 100 / 2 / 0.00 / 14.3 | 100% |
| longsword stab | 1.0 | 100 / 0 / 94 / 1.99 / 1.0 | 100 / 0 / 90 / 1.99 / 1.0 | 100 / 0 / 90 / 1.99 / 1.0 | 100 / 0 / 63 / 0.32 / 1.9 | 100 / 0 / 60 / 0.32 / 1.9 | 5 / 94 / 3 / 0.02 / 10.5 | 100% |
| hand_axe hack | 0.9 | 100 / 0 / 93 / 0.03 / 3.6 | 100 / 0 / 87 / 0.03 / 3.6 | 100 / 0 / 87 / 0.03 / 3.6 | 0 / 100 / 84 / 0.00 / 2.7 | 0 / 100 / 44 / 0.00 / 3.6 | 0 / 100 / 2 / 0.00 / 13.5 | 100% |
| axe hack | 1.3 | 100 / 0 / 91 / 0.03 / 5.0 | 100 / 0 / 83 / 0.03 / 5.0 | 100 / 0 / 84 / 0.03 / 5.0 | 0 / 100 / 84 / 0.00 / 5.0 | 0 / 100 / 44 / 0.00 / 5.0 | 0 / 100 / 2 / 0.00 / 20.0 | 100% |
| great_axe hack | 1.6 | 100 / 0 / 91 / 0.03 / 6.4 | 100 / 0 / 83 / 0.03 / 6.4 | 100 / 0 / 84 / 0.03 / 6.4 | 0 / 100 / 89 / 0.00 / 6.4 | 0 / 100 / 46 / 0.00 / 6.4 | 0 / 100 / 3 / 0.00 / 25.6 | 100% |
| mace bash | 1.4 | 0 / 100 / 89 / 2.12 / 1.4 | 0 / 100 / 46 / 0.00 / 4.1 | 0 / 100 / 75 / 1.11 / 2.7 | 0 / 100 / 84 / 2.12 / 1.4 | 0 / 100 / 44 / 0.00 / 4.1 | 0 / 100 / 21 / 0.00 / 6.8 | 100% |
| war_hammer strike | 1.3 | 0 / 100 / 89 / 1.09 / 2.5 | 0 / 100 / 46 / 0.00 / 3.8 | 0 / 100 / 75 / 1.09 / 2.5 | 0 / 100 / 84 / 1.09 / 2.5 | 0 / 100 / 44 / 0.00 / 3.8 | 0 / 100 / 28 / 0.00 / 3.8 | 100% |
| war_hammer spike | 1.3 | 100 / 0 / 94 / 1.98 / 1.3 | 100 / 0 / 90 / 1.98 / 1.3 | 100 / 0 / 90 / 1.98 / 1.3 | 100 / 0 / 69 / 1.98 / 1.3 | 100 / 0 / 66 / 1.98 / 1.3 | 5 / 94 / 4 / 0.11 / 13.8 | 100% |
| flail swing | 1.4 | 0 / 100 / 89 / 1.11 / 2.8 | 0 / 100 / 46 / 0.00 / 4.2 | 0 / 100 / 75 / 0.11 / 4.2 | 0 / 100 / 84 / 1.00 / 2.8 | 0 / 100 / 44 / 0.00 / 4.2 | 0 / 100 / 3 / 0.00 / 8.4 | 100% |
| club bash | 1.1 | 0 / 100 / 89 / 0.00 / 4.2 | 0 / 100 / 46 / 0.00 / 7.4 | 0 / 100 / 75 / 0.00 / 4.2 | 0 / 100 / 89 / 0.00 / 4.2 | 0 / 100 / 46 / 0.00 / 7.4 | 0 / 5 / 2 / 0.00 / - | 100% |
| maul smash | 1.8 | 0 / 100 / 89 / 2.13 / 1.8 | 0 / 100 / 46 / 0.00 / 5.3 | 0 / 100 / 75 / 1.11 / 3.5 | 0 / 100 / 84 / 2.13 / 1.8 | 0 / 100 / 44 / 0.00 / 5.3 | 0 / 100 / 21 / 0.00 / 8.8 | 0% |
| quarterstaff strike | 0.8 | 0 / 100 / 89 / 0.00 / 2.4 | 0 / 100 / 46 / 0.00 / 5.6 | 0 / 100 / 75 / 0.00 / 3.2 | 0 / 100 / 84 / 0.00 / 2.4 | 0 / 100 / 44 / 0.00 / 5.6 | 0 / 5 / 2 / 0.00 / - | 100% |
| quarterstaff jab | 0.8 | 0 / 100 / 89 / 1.10 / 1.6 | 0 / 100 / 46 / 0.00 / 3.2 | 0 / 100 / 75 / 1.10 / 1.6 | 0 / 100 / 84 / 1.10 / 1.6 | 0 / 100 / 44 / 0.00 / 3.2 | 0 / 5 / 2 / 0.00 / - | 100% |
| javelin thrust | 0.9 | 100 / 0 / 95 / 1.98 / 0.9 | 100 / 0 / 90 / 1.98 / 0.9 | 100 / 0 / 90 / 1.98 / 0.9 | 100 / 0 / 70 / 1.98 / 0.9 | 100 / 0 / 67 / 1.98 / 0.9 | 5 / 94 / 20 / 0.11 / 6.3 | 100% |
| spear thrust | 0.9 | 100 / 0 / 94 / 1.99 / 0.9 | 100 / 0 / 90 / 1.99 / 0.9 | 100 / 0 / 90 / 1.99 / 0.9 | 100 / 0 / 69 / 1.99 / 0.9 | 100 / 0 / 66 / 1.99 / 0.9 | 5 / 94 / 4 / 0.11 / 9.9 | 100% |
| billhook hook | 1.3 | 100 / 0 / 94 / 0.03 / 3.9 | 100 / 0 / 89 / 0.03 / 5.2 | 100 / 0 / 89 / 0.03 / 5.2 | 100 / 0 / 39 / 0.03 / 5.2 | 100 / 0 / 33 / 0.03 / 5.2 | 5 / 94 / 2 / 0.00 / 20.8 | 100% |
| billhook thrust | 1.3 | 100 / 0 / 94 / 1.99 / 1.3 | 100 / 0 / 90 / 1.99 / 1.3 | 100 / 0 / 90 / 1.99 / 1.3 | 100 / 0 / 67 / 1.99 / 1.3 | 100 / 0 / 64 / 1.99 / 1.3 | 5 / 94 / 4 / 0.11 / 14.3 | 100% |
| pike thrust | 1.5 | 100 / 0 / 94 / 1.99 / 1.5 | 100 / 0 / 90 / 1.99 / 1.5 | 100 / 0 / 90 / 1.99 / 1.5 | 100 / 0 / 70 / 1.99 / 1.5 | 100 / 0 / 67 / 1.99 / 1.5 | 5 / 94 / 4 / 0.11 / 12.0 | 100% |
| longbow bodkin | 3.0 | 100 / 0 / 94 / 1.96 / 3.0 | 100 / 0 / 90 / 1.96 / 3.0 | 100 / 0 / 90 / 1.96 / 3.0 | 100 / 0 / 66 / 1.40 / 3.0 | 100 / 0 / 62 / 1.40 / 3.0 | 5 / 0 / 3 / 0.08 / - | 100% |
| longbow broadhead | 3.0 | 100 / 0 / 90 / 0.02 / 9.0 | 100 / 0 / 81 / 0.02 / 9.0 | 100 / 0 / 81 / 0.02 / 9.0 | 0 / 100 / 84 / 1.10 / 6.0 | 0 / 100 / 44 / 0.00 / 9.0 | 0 / 5 / 2 / 0.00 / - | 100% |
| longbow blunt | 3.0 | 0 / 100 / 89 / 0.00 / 9.0 | 0 / 100 / 46 / 0.00 / 24.0 | 0 / 100 / 75 / 0.00 / 9.0 | 0 / 100 / 84 / 0.00 / 9.0 | 0 / 100 / 44 / 0.00 / 24.0 | 0 / 5 / 2 / 0.00 / - | 100% |
| crossbow quarrel | 11.2 | 100 / 0 / 94 / 1.96 / 11.2 | 100 / 0 / 90 / 1.96 / 11.2 | 100 / 0 / 90 / 1.96 / 11.2 | 100 / 0 / 68 / 1.96 / 11.2 | 100 / 0 / 65 / 1.96 / 11.2 | 5 / 0 / 3 / 0.11 / - | 100% |
| thrown javelin | 0.9 | 100 / 0 / 94 / 1.98 / 0.9 | 100 / 0 / 90 / 1.98 / 0.9 | 100 / 0 / 90 / 1.98 / 0.9 | 100 / 0 / 63 / 0.32 / 1.7 | 100 / 0 / 59 / 0.32 / 1.7 | 5 / 94 / 3 / 0.02 / 9.4 | 100% |
| thrown dagger | 0.9 | 100 / 0 / 93 / 0.32 / 2.6 | 100 / 0 / 86 / 0.02 / 4.3 | 100 / 0 / 86 / 0.06 / 4.3 | 0 / 100 / 84 / 0.00 / 3.4 | 0 / 100 / 44 / 0.00 / 6.0 | 0 / 100 / 2 / 0.00 / 13.6 | 100% |

  Through the armpit every edge and point gets 100% cut against every harness (a dagger against plate 1.98 serious wounds a blow, down in 0.7 s); a blunt blow cannot slip a gap.
- **Ranged runs (item 3).** In `docs/calibration/weapon-armour-matrix.md` (2,000 duels per pairing): a lone swordsman loses to an archer at 30 m unless he wears plate (the archer
  wins 100% against padding or mail, 17% against plate); a crossbowman wins 55% with his one spanned shot; javelins win 82 to 88%; like against like is even. Two AI faults fixed
  on the way: the archer now nocks while he backs off (he used to outrun the swordsman for ever, a quarter of the bow duels walked away), and a crossbowman stands to span (backing
  off cancelled the windlass) and comes into a fight spanned.

| Pairing | n | mean s | defended | A wins | B wins | severs/duel | fatal | KO'd then bled out (untreated) | (with first aid) |
|---|---|---|---|---|---|---|---|---|---|
| bow (bodkin) vs sword (padded) | 2000 | 8.9 | 0% | 100% | 0% | 0.00 | 42% | 37% | 33% |
| bow (broadhead) vs sword (unarmoured) | 2000 | 14.6 | 1% | 95% | 5% | 0.00 | 7% | 7% | 1% |
| bow (bodkin) vs sword (mail) | 2000 | 8.4 | 0% | 100% | 0% | 0.00 | 15% | 14% | 11% |
| bow (bodkin) vs sword (plate) | 2000 | 24.2 | 3% | 17% | 82% | 0.00 | 35% | 38% | 7% |
| bow (bodkin) vs sword+heater (padded) | 2000 | 8.9 | 0% | 100% | 0% | 0.00 | 42% | 37% | 33% |
| bow (bodkin) vs sword+heater, shield raised | 2000 | 25.6 | 18% | 99% | 0% | 0.00 | 45% | 33% | 25% |
| crossbow vs sword (padded) | 2000 | 18.0 | 0% | 55% | 45% | 0.00 | 49% | 49% | 29% |
| crossbow vs sword (mail) | 2000 | 18.0 | 0% | 55% | 45% | 0.00 | 46% | 47% | 28% |
| javelins vs sword (padded) | 2000 | 6.9 | 37% | 88% | 12% | 0.00 | 44% | 35% | 16% |
| javelins vs sword+heater (padded) | 2000 | 7.9 | 43% | 82% | 17% | 0.00 | 45% | 36% | 16% |
| bow vs bow (padded) | 2000 | 45.1 | 0% | 40% | 40% | 0.00 | 42% | 32% | 15% |
| bow vs crossbow (padded) | 2000 | 58.1 | 0% | 53% | 46% | 0.00 | 50% | 37% | 26% |
| crossbow vs crossbow (padded) | 2000 | 130.8 | 0% | 37% | 35% | 0.00 | 40% | 41% | 22% |
| bow vs javelins (padded) | 2000 | 9.4 | 0% | 80% | 20% | 0.00 | 48% | 38% | 32% |
| crossbow vs javelins (padded) | 2000 | 15.3 | 0% | 43% | 56% | 0.00 | 49% | 44% | 27% |

- **Armour damage (item 4).** Blunt blows dent and buckle rigid plate (condition falls, and with it the need: `Cm`), heavy blows burst mail's rings at the struck zone and battered
  plate breaks a strap and hangs loose; told in the flavour log (`after.broke.armour.*`: dented, buckled, burst, strap) and the fight log, drawn on the called-shot doll. Details in the
  calibration file and the resolver-v2 Notes.
- **Calibration (P4-C table) before -> after, 2,000 duels per pairing:**

| Pairing | mean s | defended | A wins | KO'd then bled out, untreated / with first aid |
|---|---|---|---|---|
| sword vs sword (padded) | 14.8 -> 14.8 | 52% -> 52% | 50% -> 50% | 45% / 7% -> 45% / 7% |
| sword vs sword (unarmoured) | 15.2 -> 15.2 | 52% -> 52% | 51% -> 51% | 45% / 10% -> 45% / 10% |
| spear vs sword (unarmoured) | 10.6 -> 10.3 | 44% -> 46% | 60% -> 62% | 31% / 7% -> 30% / 7% |
| spear vs sword (padded) | 11.1 -> 10.7 | 44% -> 45% | 55% -> 59% | 32% / 5% -> 30% / 5% |
| axe vs sword (mail) | 14.0 -> 14.0 | 42% -> 42% | 20% -> 20% | 40% / 5% -> 40% / 5% |
| mace vs sword (padded) | 10.6 -> 10.6 | 43% -> 43% | 69% -> 70% | 14% / 2% -> 14% / 2% |
| sword vs sword (plate) | 21.2 -> 23.1 | 48% -> 47% | 49% -> 49% | 12% / 4% -> 14% / 4% |
| mace vs sword (plate) | 16.2 -> 16.0 | 40% -> 40% | 57% -> 62% | 5% / 2% -> 5% / 2% |
| sword (padded) vs sword (plate) | 18.1 -> 18.9 | 50% -> 50% | 43% -> 40% | 32% / 6% -> 35% / 6% |
| mace (padded) vs sword (plate) | 14.2 -> 13.8 | 41% -> 41% | 46% -> 49% | 25% / 4% -> 24% / 4% |
| sword+heater vs sword (padded) | 16.4 -> 16.4 | 59% -> 59% | 65% -> 65% | 45% / 7% -> 45% / 7% |

All knocked out: 29.4% / 5.2% before, 29.6% / 5.4% after (target <= 35% with first aid).

  The plate duel lengthens to 23 s (target 10 to 20): a failed point now skids off plate instead of crushing it, and swords against full plate have little left but the gaps and a
  scuffing pommel. The test's allowance for that one pairing moves from 23 to 25 s. The spear ends its duels at about 10 s (10.3 over 2,000; the 160-duel sample reads 9.9), so
  ML-14 lets a spear row read down to 9.5 s. Defended 40 to 59% and bleed-out with first aid 5.4% hold. The mace against padding stays at 70% (the open item): it overwhelms a
  sword's block (190 units against a weapon's capacity of about 170) and its weight drives through a parry; the matrix shows padding halves what reaches the body, so it is left.
- Tests: ML-1 has the spear at 8 / 10 ticks; ML-14 keeps only the spear's floor; WM-6's threshold is 850 pm (over 240 seeds the protector was within 3 m 887 pm of the ticks
  before these changes and 889 after; the 40-seed sample read 905 before and 862 after). The fight hash of the 10 v 10 skirmish (`WorldTool hashes`: seed 1 `1001F8BDF105658D`,
  seed 1234 `8BAC3738BE098914`) and MT-13 are re-pinned: armour now wears, the spear is faster and countered, the archers nock as they back off. World generation is unchanged.

