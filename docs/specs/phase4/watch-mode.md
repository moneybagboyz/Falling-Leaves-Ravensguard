# Watch mode: hand any fight to the AI with hero stances, and take control back

Plan refs: §3i (watch mode for fights: toggle with a key and a button on the combat HUD; when a fight starts, or at any point, your hero and your side are fought by AI in real time; the hero picks targets, attacks, blocks, parries, uses called shots and retreats on the same utility AI and timings as everyone else; squads and formations follow your captains' Tactics skill; normal speed controls; you can take back control or give orders at any moment; no instant skip: it is real time and uses the real resolver; battles outside any bubble use the abstract resolver as reports; stance settings for the hero's combat AI: aggressive, defensive, protect a target, no lethal blows (knock out and capture), flee when badly hurt; allowed in Ironman; the chronicle records it), §5 Phase 4 ("watch mode: hand any fight to the AI in real time, with hero combat stances, and take back control at any moment"), §6 ("the hero on watch mode uses exactly the NPC combat AI (same seed gives the same fight)"), §2.5 (one utility AI for NPCs, your people and you).
Code to reuse: the Phase 3 autopilot (`Sim/Autopilot`): `LimitSet` (`NoLethalForce`, `MaxRiskPm`, `HomeByNightfall`), `InterruptRouter` and `AiResponse` (`FleeShelterOrFight`, `AvoidOrFlee`, `Bandage`), `InterruptKind` and `InterruptPolicy` (`Pause`, `Notify`, `Ai`), `AutopilotView` presets ("Watch the world": everything `Notify` or `Ai` except `Attacked` and `Injured`), `ActionPolicy` (`BiasPm`, `FilterMask`: how a layer steers the shared brain without a second brain), `Directive.Escort`; the spike AI (`Combat/Melee/MeleeAi.cs`) that becomes `CombatBrain` (melee-and-ranged.md 4.7).

## 1. Purpose
Let the player step back from a fight (to rest, to watch, to share a stress-heavy battle with the AI, or to play the game as a story) with the guarantee that **the AI controlling the hero is the AI controlling everyone else**: same code, same timings, same data, same resolver, so it makes the same mistakes and its outcomes stick. Stances are the knobs; take-back is instant. Autopilot's directive layer (travel, work) already exists; watch mode is its combat sibling and reuses its limit and interrupt ideas.

## 2. Dependencies
| Needs | From |
|---|---|
| `CombatBrain`, fight timeline, formations F1-F4, block, parry, dodge, feint, called shots | `melee-and-ranged.md`, `resolver-v2.md` |
| Treatment actions (`Treat`) and wound thresholds | `wounds-bleeding-pain-healing.md` |
| Morale, rout, surrender, yield | `morale-capture-succession.md` |
| Autopilot `LimitSet`, `InterruptKind`, `InterruptPolicy`, `AutopilotView` | exists (`Sim/Autopilot`, `Sim/Travel/InterruptBus.cs`) |
| `Followers`, `HeroParty`, squad tactics skill (`Skill.Tactics`, appended in Phase 3) | exists (`Sim/Live/Followers.cs`, `Sim/Trade`) |
| Speed controls (1x, 2x, 4x, 8x), pause | plan §2.8 (World context) |
New code: `Sim/Combat/Watch/` (`WatchStance`, `WatchState`, `StanceBias`, `RiskEstimate`, `HandOff`), content `combat/stances.jsonc`, HUD `UI/WatchHud.cs`.

## 3. Data

### 3.1 `WatchStance` (saved per hero and party member; 16 bytes)
```jsonc
{ "id": "balanced",
  "posture": "balanced",              // aggressive | balanced | defensive
  "protect": null,                    // entity id of a person to guard (lord, child, companion) or null
  "no_lethal": false,                 // knock out and capture instead of killing
  "flee_blood_loss_pm": 250,          // flee when blood loss reaches this (permille)
  "flee_pain_pm": 700,                // flee when pain reaches this share of the knock-out threshold
  "flee_morale_below": 25,            // flee when morale falls below this (morale-capture-succession.md 4.1)
  "flee_odds_below_pm": 0,            // 0 = never by odds; e.g. 250 = flee when estimated odds of winning < 25%
  "called_shots": true,               // skilled AI aims at vulnerable zones and armour gaps
  "use_ranged": true,                 // shoot when a bow or crossbow is in hand and the range is 15-60 m
  "treat_allies": true,               // stop pressing and bandage a bleeding ally when safe
  "target_priority": "threat" }       // nearest | weakest | threat | leader
```
Five **presets** (the plan's list) in `combat/stances.jsonc`:
| Preset | Posture | Protect | No lethal | Flee blood | Flee morale | Notes |
|---|---|---|---|---|---|---|
| `aggressive` | aggressive | none | no | 400 | 15 | attacks first, heavy swings when the foe recovers, rarely guards; reaction probabilities -15 |
| `balanced` (default) | balanced | none | no | 250 | 25 | the NPC default (spike AI numbers) |
| `defensive` | defensive | none | no | 200 | 30 | holds guard; parry +20, shield block always; attacks only on the foe's recover or after a parry; fights from a wall or behind allies |
| `protect` | defensive | the chosen target | no | 300 | 25 | stays within 2.5 m of the target, takes the enemy nearest the target; breaks off only if the target is safe or dead |
| `subdue` | balanced | none | **yes** | 250 | 25 | non-lethal: 4.3 |
| `flee_hurt` | balanced | none | no | **150** | 40 | leaves early, then treats itself |
`WatchState` (live only): `On` (bool), `Since` (tick), `Stance`, `LastDecision` (reason code for the "why" line), `TakeBackArmed`, `DangerPause` flags.

### 3.2 New interrupt kinds (append-only `InterruptKind`, 23 -> 27; autopilot test AP-7 counts the new total)
`WatchDanger` (blood loss >= 30% or pain >= 85% of threshold or morale < 30), `WatchLosing` (estimated odds < 25%), `WatchAmmoOut` (archer out of ammunition), `WatchOver` (fight ended). Each has a policy `Pause | Notify | Ai` like all kinds; defaults in watch mode: `WatchDanger` **Pause**, `WatchLosing` **Notify**, `WatchAmmoOut` **Notify**, `WatchOver` **Pause**. `Attacked` and `Injured` keep their autopilot defaults. The same router (`InterruptRouter.Raise`) delivers them; the chronicle notes the hero's fight "on watch".

### 3.3 Odds (`RiskEstimate`, shared with the HUD, autopilot and abstract battles)
`PowerRating(person) = 1000 x blood_fraction x (skill_weight x weapon_weight x armour_weight)` from precomputed tables (a few integers: `ThreatPm`, `ProtectionPm`; abstract-battles-v1.md 4.2). `OddsPm = 1000 x P_ally^2 / (P_ally^2 + P_enemy^2)` (Lanchester square law, ally and enemy sums within 25 m weighted by distance). Autopilot's `MaxRiskPm` becomes: when `Attacked` is set to `Ai`, the hero fights if `OddsPm >= 1000 - MaxRiskPm`, else flees (`FleeShelterOrFight`). Accuracy of the estimate is tested against sampled outcomes (WM-11).

## 4. Rules

### 4.1 Hand-off and take-back
- **Toggle:** **F6** or the **Watch** button on the combat HUD. At the next tick boundary the hero's `MeleeCommand` is cleared and ownership passes to `CombatBrain`; an **in-progress wind-up or strike continues** (the brain adopts the active `Req` so no swing is lost); a held block or brace continues; a pending called-shot menu closes (the fight resumes if it was paused). While on, the player's hero is `IsPlayer = false` for the combat step (but morale forced-rout rules apply per stance, morale-capture-succession.md 4.1).
- **Take back:** **F6** again, **or** any combat input (LMB, RMB, Ctrl + direction, G, T, WASD movement) when the setting *Take control on any combat input* is on (**default on**; off for zero-player watching). Control returns at the next tick: input is applied that same tick (a swing started by LMB becomes the hero's new wind-up). The AI's last decision is kept in the "why" line for 3 s so the player knows what was happening.
- **Orders still work:** formation cards (F1-F4), the order wheel (R, hold), **focus target** (click an enemy: sets the priority target for your side), "hold fire", "fall back to me", "protect X". These write `StanceBias` to the followers' `CombatBrain` (not to the resolver).
- **Pause and speed:** all speed controls are legal (1x, 2x, 4x, 8x): a fight steps `speed` 20 Hz steps per render slice (melee-and-ranged.md 5), never longer steps; if the 25 ms budget is exceeded the speed drops one level and a toast says so. There is **no instant skip** and no "auto-resolve": the abstract resolver only handles fights outside any bubble.
- **Auto-pause on danger** (4.5) lets the player step in; with all policies on `Ai` it is a zero-player combat (a hero can die; Ironman succession applies).

### 4.2 The same AI (the guarantee)
`CombatBrain` has **one entry point** `Decide(fighter, world, stance)` used for every non-player fighter and the watched hero. The hero's `stance` is read from `WatchStance`; NPCs use `stances.jsonc` presets chosen by role (soldier: aggressive or balanced by morale, guard: defensive, bandit: aggressive, monster: aggressive no-flee, beast: per species). There is **no special-case code for the hero** (test WM-1: a hero fought by watch mode and the same person flagged as a plain NPC with the same stance and seed produce identical `FightHash`). Hero-specific inputs are only the stance and the player's explicit orders.
Mistakes are the same too: the AI can be out-flanked, can swing into a parry, can run out of stamina, and can fail to bandage in time. This is by design (plan: "so it makes the same kinds of mistakes a person would").

### 4.3 Stance behaviour (how `StanceBias` changes the brain)
Each stance is a table of **biases and filters**, the autopilot way (`ActionPolicy.BiasPm`, `FilterMask`), not new logic:
- **aggressive:** attack-start bias +300 pm, defence reaction probabilities -15 points, prefers heavy swings when the foe's state is `Recover` or `Stagger` and stamina >= 50, charges in at distance < 6 m, pursues routers up to 20 m.
- **defensive:** guard hold bias +300 pm, parry +20 points, always block with a shield on front-arc blows, attacks only when the foe is recovering or after a successful parry (riposte), keeps allies between it and ranged enemies, never leaves a shield wall.
- **protect X:** movement goal = a point within 2.5 m of X on the side nearest the threat; target = the enemy nearest X (ties by threat); `Treat` X before self when X bleeds; abandons only below the flee thresholds.
- **subdue (no lethal):** `no_lethal` filters the attack list: **edge and point attacks are replaced by their `flat` or butt variants** (every blade gets a `flat` attack: blunt, area 35 cm2, velocity 0.7; polearms use the butt; axes the haft; daggers the pommel) and **never aims at head, neck or upper body**; targets limbs and `lower_body` (to knock out by pain); stops attacking a `Down`, `Surrendered` or `Fled` target; accepts surrender at once; **does not finish** KO'd enemies; if no blunt option exists (there is no grappling in Phase 4) it fights with fists (blunt, area 15). Mapping: `no_lethal` equals autopilot `LimitSet.NoLethalForce` (the same flag, the same filter; set from either panel). Expected result: >= 60% KO, <= 5% deaths in the calibration set (WM-4), the rest flee or yield.
- **flee when hurt:** the **flee triggers** are evaluated at 5 Hz: blood loss >= `flee_blood_loss_pm`, pain >= `flee_pain_pm` x threshold, morale < `flee_morale_below`, or `OddsPm < flee_odds_below_pm`. Flee: `Flee` action (existing `ActionKind.Flee`) along the flow field to the nearest safe point (camp, allies >= 3, house door), sprint while stamina > 15, drop to a walk then; once 20 m from all enemies for 5 s the brain switches to `Treat` (pressure -> bandage -> tourniquet by wound state) and calls `WatchDanger`.
- **Treat allies:** with `treat_allies`, when no enemy is within 8 m and an ally within 6 m has `R >= 5 ml/s`, the brain presses or bandages (`Treat` job, wounds file 4.3), `Fight` score x0.3 meanwhile.
- **Called shots and gaps:** `called_shots` lets skilled AI (`skill/150` probability per swing) aim at the weakest zone by vulnerability (armour gaps, unarmoured parts) from the resolver preview; off = torso always (the v1 bug "AI always aims at the torso" never returns).
- **Ranged:** archers hold 15-40 m; `use_ranged` off = melee only.
- **Cavalry brain** (mounted-combat.md 4.6) and **monster brains** use the same stance filter (monsters: only `aggressive`).

### 4.4 Squads and formations
Followers in the party use `CombatBrain` with the stance of their `Directive`-like `FollowerOrders` (default `balanced`); the **squad leader's `Tactics` skill** picks the formation when the player has not set one: `Tactics >= 30` and enemy ranged or cavalry present -> **shield wall** (F2) for shield bearers; foot melee -> **line** (F1); `< 30` -> skirmish (loose). The player can always override with F1-F4 (plan §2.8). Phase 7 extends this to captains and armies; Phase 4 stays at skirmish scale (<= 60 per side in the live bubble). The player's own orders are `StanceBias` writes, so the same data path serves watching and commanding.

### 4.5 Danger pauses and the "why" line
- `WatchDanger` fires on the hero at: blood loss >= 30% (collapse imminent), pain >= 85% of the knock-out threshold, morale < 30, an unbandaged arterial bleed with `R >= 60 ml/s`. Default policy **Pause**: the game pauses and shows "Take control?". Declining continues watching (a 5 s cooldown prevents spam). `WatchLosing` (odds < 25% sustained 3 s) notifies once per fight.
- **Why line:** the HUD shows the hero's current decision reason (from `LastDecision`): "Guarding: stamina 22", "Attacking: foe recovering", "Fleeing: blood loss 26%", "Treating Edda: bleeding 40 ml/s". Reason codes are an enum with `tr()` text. They are not a trace of the RNG.
- The **log** switches to "show all" filter on first watch use (feedback file 4.2) so the player can follow the fight.

### 4.6 Ironman, autopilot, chronicle, scoring
Watch mode is **allowed in Ironman** under the same rules (plan §3i): deaths count, succession applies. It works with autopilot: with directives on and `Attacked` set to `Ai`, a fight starts under stance `balanced` (or the stance in the directive's `Params`) and ends with `WatchOver`. The chronicle records "{hero} fought {foes} (watched)" and notes the stance for cause chains. Skill gain, loot and injuries are as in manual play; **no cheats**: no damage reduction, no hidden dodge bonus.

### 4.7 Worked example: a road ambush on watch
The hero (skill 50 sword and heater, gambeson, stance `balanced`) with two followers meets four bandits (skill 35, two with bows) on a road. Watch mode is on from tick 0.
| Time | What happens | Why line |
|---|---|---|
| 0 s | `Attacked` (policy `Ai`): odds estimated 640 pm (the party outguns them in armour); `OddsPm >= 1000 - MaxRiskPm (500)`: fight | "Fighting: odds 64%" |
| 2 s | Hero advances to 8 m behind the spearman follower, raises the shield (front arc to the archers) | "Guarding: archers at 25 m" |
| 9 s | An archer hits the hero's off-side thigh, M_final 71: cut and bruise, bleed 3 ml/s | "Attacking: foe recovering" |
| 14 s | The AI picks a parry against a swordsman (reaction probability 0.15 + 50/130 = 53%, drawn from 0x430), succeeds with skill/120 = 42%, and ripostes at half wind-up | "Riposte" |
| 21 s | Blood loss reaches 8% (400 ml) from a second cut; no triggers yet (flee at 25%) | "Attacking" |
| 30 s | A bandit falls; morale on their side drops; two flee; the fight ends; `WatchOver` pauses | "Fight over; 2 wounds: press Pressure" |
| 31-45 s | `treat_allies` and self: pressure then bandage on the thigh, a follower is bandaged first (bleeding 18 ml/s) | "Treating Edda: bleeding 18 ml/s" |
At any moment the player could press F6 or click: the swing in progress completes under the AI, then the player's input takes over.

### 4.8 Stance against directive matrix
| Autopilot directive (Phase 3) | `Attacked` default | Stance used | Notes |
|---|---|---|---|
| Work, Train, Rest | Pause | `balanced` | pauses so the player decides; policy `Ai` makes it fight when odds >= 1000 - `MaxRiskPm` |
| Travel, TradeRoute | Pause (notify in zero-player) | `balanced`, `flee_hurt` when carrying > 100 crowns | caravans prefer to run to the nearest village on `Flee` |
| Guard (place) | Ai | `defensive` | holds the post, `protect` the place's lord if present |
| Escort (person) | Ai | `protect` the escorted person | keeps within 2.5 m; the escort follow check (Phase 3 limit) is wired here |
| Rest (healing) | Pause | `flee_hurt` | a recovering hero should not fight |
| Guard with `NoLethalForce` | Ai | `subdue` | arrests rather than kills (Phase 6 uses it for law) |

## 5. Tick placement (20 Hz live versus abstract)
- Live: `CombatBrain.Decide` at 20 Hz for fighters within 60 m of the player and each active squad, 5 Hz elsewhere (plan §2.6, LOD applies only to AI and rendering). The watched hero is always in the 20 Hz group. Hand-off happens at a tick barrier before the `FightWorld.Step`.
- Abstract tier: no watch mode. The hero in an abstract battle is a casualty sample like everyone else (abstract-battles-v1.md); if the player is within the bubble the fight is live, so the question never arises.

## 6. Determinism
- No new RNG streams: it uses `MeleeAiChoice` 0x430, `AiTarget` 0x431 and `StanceNoise` 0x432, keyed `(FightTick, fighter)`; **0x490** is reserved and asserted never drawn (like the Phase 3 autopilot 0x380) so a stray draw fails a test.
- Handing off or taking back must not change any random draw order: the brain draws exactly what it would draw for an NPC (test WM-2 compares hashes of a fight with a hand-off at tick 100 and a fight where the hero was AI from the start with the same stance; they match from tick 101).
- `WatchHash` (state, stance, last decision code) joins the daily trail.

## 7. UI and controls
- **F6**: toggle watch mode (`world.watch_toggle`, new key, free in World and CombatStance). **Shift+F6** cycles to the next stance preset (`world.stance_next`; the previous one is on the stance chip menu and the order wheel). Every letter key is taken in `InputDefs` (U is the cart hitch, `,` camp, `.` forage), so Phase 4 uses a function key. Hold **R** (order wheel): formations, focus fire, "protect X". Existing keys: **Space** pause, **1-4** speeds, **X** stance, **F1-F4** formations.
- **Combat HUD:** a **Watch** button with the current stance chip and the flee thresholds; a banner "WATCH - press F6 or any combat input to take control" (the second part only if take-back-on-input is on); the "why" line; **stance panel** (click the chip: presets plus the toggles of 3.1; a protect target picker from the party and nearby allies); the odds estimate ("odds 62%") and a mini-panel of ally and enemy counts. The hero's **blood and pain bars** stay visible; a countdown "collapse in 21 s" if bleeding.
- Settings: take-back policy, which watch interrupts pause, speed cap while watching (default 4x), "dim the world slightly while watching" (off by default).
- Accessibility: all banners are text and icons, no flashes; `Accessibility` rules from the feedback file apply.

## 8. Tests (golden)
| Id | Check |
|---|---|
| WM-1 | **Same AI:** the hero under watch mode with stance S and the same person flagged as a plain NPC with S produce identical `FightHash` over a 10 v 10 skirmish (20 seeds) |
| WM-2 | **Hand-off is seamless:** switching on at tick 100 mid-wind-up: the swing completes, and from tick 101 the `FightHash` equals the all-AI run; no dropped or doubled blow |
| WM-3 | **Take-back:** input at tick T gives the hero a new command applied at tick T; the AI draws no random number for the hero from T (stream draw log) |
| WM-4 | `subdue`: 200 duels, sword vs sword: edge and point blows landed = 0; knockouts >= 60%; deaths <= 5%; no blow at head or neck |
| WM-5 | `flee_hurt` (150): flees at blood loss 15% (+-2) in >= 95% of 200 duels, then treats itself (pressure within 8 sim-s of reaching 20 m) |
| WM-6 | `protect`: the hero stays within 3.0 m of the target in >= 90% of ticks over 40 seeded ambushes |
| WM-7 | Interrupts: `WatchDanger` fires at blood loss 30% and pauses; `WatchLosing` at odds < 25%; `WatchOver` when the fight ends; AP-7 counts 27 kinds |
| WM-8 | Ironman: watch mode available; a hero death in watch mode triggers succession (SU-1) |
| WM-9 | Speed: the same fight at 1x, 2x, 4x and 8x gives the same `FightHash` |
| WM-10 | `no_lethal` in the watch stance and `LimitSet.NoLethalForce` are one flag (setting either shows in both) |
| WM-11 | `OddsPm` against sampled results over 500 skirmishes of varying sides: predicted vs actual win rate within +-15 points |
| WM-12 | Squad formation choice: leader `Tactics 40` vs ranged enemy -> shield wall; `Tactics 10` -> loose; override by F-key wins |
| WM-13 | A zero-player run: autopilot + watch + all interrupts `Ai`: 20 road ambushes (seeded) end with a recorded outcome, no stuck states |

## 9. Open questions (recommended defaults)
| # | Question | Default |
|---|---|---|
| WA1 | Should any combat input take back control automatically? | **Yes** (default on), because a stray click should never fight against the AI; off for zero-player |
| WA2 | Should watch mode be disabled in "small" fights (< 3 fighters) to avoid exploits? | No, it works everywhere; the stakes are the same as manual play |
| WA3 | Hero skill-gain while watched | Same as manual (it is the hero fighting) |
| WA4 | Does the AI use consumables (bandages, tourniquets, healing herbs) on the hero? | Yes (`Treat`), same as NPCs, only after the fight breaks off or at flee thresholds |
| WA5 | Can watch mode be left on across fights? | Per fight by default; a "keep watching" toggle in settings carries it over (autopilot users) |
| WA6 | Slow motion in watch mode | Off (feedback 4.5) |
| WA7 | Key choices | F6 and Shift+F6 (no letter or punctuation key is free; overview 6A); the stance chip and the order wheel are the other routes |

## 10. Budgets
| Item | Budget |
|---|---|
| Watched hero decision | one `CombatBrain.Decide` per tick: <= 4 us (included in melee budget) |
| `RiskEstimate` | <= 20 us per call, called at 5 Hz for the player's fight (<= 0.1 ms/s) |
| HUD | <= 0.3 ms per frame |
| Save | `WatchStance` 16 bytes per hero and follower; `WatchState` not saved except in a saved fight |
| Content | `stances.jsonc` <= 4 KB; `FL_WATCH_*` strings <= 40 |

## Notes (P4-F implementation, step 5)
Code: `src/Ravensguard.Sim/Combat/Watch/` (`WatchTypes` with `WatchStance` (16 bytes: preset, flags, flee blood and pain, morale, odds, the guarded person), `WatchReason`, `WatchSignal` and `WatchState`; `FightWorld.Watch.cs` (a partial of the fight: hand-off, take-back, stances, the 5 Hz odds and danger checks, the why line); `StanceBias` (protect, fall back, formations, shield toward archers); `RiskEstimate`; `SquadTactics` (formations and the side's orders); `WatchWhy`; `WatchBook`, `WatchDesk` (the hero's watch commands at the barrier) and `WatchInterrupts` (the router); `WatchRules`, `WatchSpeed`), `Combat/Melee2/FightRules.cs` (`FightKind`, the kinds' rules: three clean hits, the tournament yield, treating in the fight), `Combat/Melee2/AbstractFightStub.cs` (`IAbstractFights`, the stub, `FightRun`), the rewritten `FightSetup` (overview 3), `Save/WatchSave.cs` (the optional "watch" section) and `WatchRow` (key 21 of a `FightRecord`). Content: `combat/stances.jsonc` gained `pursue_m`, `charge_within_m`, `shield_to_archers`, `flee_checks`; the Spring Fair lists a `tournament` contest; the audio's silent row lists the new `treated` event. Godot: `UI/WatchHud.cs` (the button, the stance chip and panel, odds and counts, blood, pain and the collapse countdown, the banner, the why line, the "Take control?" prompt; a modal guard while the panel or the prompt is up), `FightView` (F6, Shift+F6, take-back on any combat input, the signals routed at the frame), `StreetView.Fight.cs` (the hero's saved stance and "keep watching" in the setup, stance changes kept by a hero command, the watched fight at 2x-8x by stepping more often, the yard and tournament bouts), the encounter panel's odds, `--fight-demo practice` and the shots `--shot-what watch|stance|danger|protect` (`p4f-watch|stance|danger|protect|practice|encounter.png`). Tests `tests/.../Combat/Watch/` (`WatchTests` WM-1 to WM-13 and the save and stream checks, `FightSetupTests`). Interrupt kinds 23 -> 27 (`WatchDanger, WatchLosing, WatchAmmoOut, WatchOver`, defaults Pause, Notify, Notify, Pause; AP-7 in `AutopilotTests` and `TravelTests` counts 27); `AiResponse` appended `KeepWatching, TreatAfterFight`; `FightInputKind` appended `Watch 21, Stance 22, Formation 23, Order 24, DeclineDanger 25`; `FightEventKind` appended `Treated`; `HeroCommandKind` appended `Watch`. Stream 0x490 `WatchReserved` is registered and never drawn (a source scan; the watch folder has no `DetRng` at all).
Decisions taken while building:
- **The same AI (WM-1)** holds because watching sets `Fighter.Player = false` and nothing else: the brain, morale, mounted and flavour code read only that flag and the stance, so the watched hero follows NPC morale (a watched hero can rout by morale or yield when cornered, as the stance's thresholds say; the danger pause at morale 30 comes before the rout at 20). The `WatchHash` (state, stance, last decision, odds) joins the live hash trail, not the `FightHash`, so a watched hero and a plain NPC hash alike. `FightWorld.LodFighter` (set by the setup to the player's index, watched or not) keeps the hero in the 20 Hz group.
- **Hand-off (WM-2)**: a player's held wind-up becomes the brain's (`Req.Auto`, the impact at the end of the wind-up), a held guard stays, one-shots and the held walk are cleared. **Take-back (WM-3)**: the brain's held walk, facing, sprint and guard are dropped and the input of the same barrier applies at the next step; a `FightWorld.AiDrawLog` hook records every brain draw (fighter, stream) for the test. Both go through `FightInput` (recorded by the session recorder, replayed); a stance change is also a hero command (`wm:stance|...`) so the hero keeps it for the next fight.
- **Stances as data**: aggressive pursues routers to 20 m and sprints in from 6 m; defensive and protect do not chase routers at all (`pursue_m` 0); defensive raises the shield toward archers within 40 m while closing on a foe more than 6 m away. The NPC presets keep fleeing to morale; `flee_checks` (the flee_hurt row, and a custom stance with a lower pain, higher morale or any odds mark) adds the pain, morale and odds triggers of 4.3. **Subdue**: blunt attacks only, a blade without a flat (the dagger, a javelin) is put away for the fists (`Fighter.Sheathed`), and the head, neck, upper body and what they host are passed to the resolver as unreachable parts, so a missed blow's redirect never lands there either; a no-lethal fighter accepts a surrender at once. **Protect**: the guard goes straight to its charge when more than 2.5 m away (dropping the guard to run), stands 1.5 m from it toward the nearest threat, attacks only from within 2.2 m, takes the enemy nearest the charge, does not rout by morale while the charge is there (only its flee thresholds break it), and does not kneel to treat away from it.
- **Treating in the fight** is a rule of setup-built fights (`FightRules.TreatInFight`; the harness and calibration fights keep it off, so their numbers are unchanged): a fighter with `treat_allies` and no enemy within 8 m (a runner: 20 m) presses or bandages its charge, itself, then an ally within 6 m bleeding 5 ml/s or more (`TreatAi`, no items: pressure), busy for the treatment's seconds, emitting `Treated`.
- **Odds (WM-11)**: `Power = blood x pain x (300 + 14 x skill) x weapon x shield (+15%) x (1000 + 1.6 x ProtectionPm) x size`, summed per side within 25 m (full weight within 5 m). The square law under-predicted how decided the fights are (a 70% prediction won 87%), so the side powers go through the fourth power (fit on 500 fights): predicted against actual by bin 55/82, 299/404, 505/428, 699/673, 944/951 permille, mean 496/504. `ProtectionPm` is computed from the worn layers per fighter (the abstract battles' precomputed table is step 6's).
- **The walk-away winner**: when both sides stop swinging for 15 s, a side that is running (`Routing`) now counts as beaten (`DecidedWinner`); before, a road fight whose last bandit ran ended as a draw and the game sent the hero to the defeat table.
- **Measured**: WM-4 subdue (skill 60 against a club, 200 duels): 774 blows landed, 0 edged, 0 at the head or neck, 199 knock-outs, 0 deaths. WM-5 flee_hurt: of 87 duels where the blood passed 15% while the hero still fought (a hardened hero, toughness 40, with a bleeding cut in the shield arm against a careful shield bearer), 83 broke off between 13% and 17%; all 29 who got 20 m clear pressed their own wound within 8 s (the rest collapsed at 30% from what the fight had opened before they got clear, an honest outcome). A pain or morale flee before the blood mark is the stance's other trigger and is not in that count. WM-6 protect (40 ambushes, the charge a defensive companion): within 3 m in 90.5% of ticks. WM-13: 20 zero-player ambushes: 7 won, 7 not fought (autopilot's odds rule), 3 yielded, 1 killed, 2 lost, none stuck, nothing paused.
- **Speed**: in the street view a watched fight steps the bubble `speed` times a slice, capped by the setting (4x), one level lower after a slice over 25 ms (with a toast); a fight never lengthens a step (WM-9). Manual fights stay at 1x.
- **The encounter replacement (overview 3)**: `FightSetup(FightKind, kits, ...)` with `Terrain` (the mounted layer's `FightTerrain`, rain and storm from `Weather`), `SideKinds` (bandits, beasts, guards: who accepts a surrender), `StanceDefaults`, `HeroStance`, `StartWatched`, `HeroTactics`; builders `Encounter` (an ambush script is `FightKind.Ambush`), `Raid`, `Ambush`, `SiteInhabitants` (for step 7), `Practice` (the rack's `practice_*` weapons by family, blunted, to three clean hits) and `Tournament` (own weapons blunted: half momentum, and `StrikeV2.Blunted` makes edges and points blunt in the resolver; a fighter whose pain reaches the knock-out yields). `Run(..., inBubble)` gives a live fight or, outside the bubble, `IAbstractFights.Resolve`: `AbstractFightStub` names the side the odds favour and touches nobody (no draw); step 6 implements the interface. The record keeps the kind's name, so a saved bout gets its rules back. The tournament purse is paid by `TournamentPurse.Pay` (one ledger transfer from the town's fief treasury, never more than it holds; reason `Gift` as the prize) through a hero command (`wm:prize|...`).
- **Game**: the encounter panel shows "Fight [odds 34%: probably lose]" (the setup the fight would start, rated before choosing; Flee or Pay always beside it). The practice yard's Spar is fought in the combat view against the trainer (the timing bar stays for the dummy; the Phase 3 rules and tests are unchanged); on a Spring Fair day the yard offers a tournament bout for the contest purse. The road fight's hero is named as the hero (the kit was called "You", so the short log said "You charges"; the flavour log already had the name). The chronicle notes a watched fight ("fought 3 (watched; stance balanced)").
Left for later: the party's followers do not join road fights yet (the setup takes their kits; the game passes none), so squad formations and orders are sim-side and tested but have no follower to steer in play (F1-F4 send the side's formation; the order wheel R is not wired); the follower stances are saved in the watch book but not shown in a panel; autopilot's road encounters do not yet choose by `RiskEstimate.AutopilotFights` in the game (the rule and WM-13 use it); `RiskEstimate.ProtectionPm` is per fighter, not the abstract battles' precomputed table; the "dim the world while watching" setting and the settings page rows (take-back, speed cap, which kinds pause) are in the book and the HUD only; the string count (about 95 `wm_` rows) is over the spec's 40; mid-fight loads restore the watch state but the game does not rebuild a fight view after a load (as before).
