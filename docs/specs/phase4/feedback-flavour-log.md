# Feedback and flavour: prose from the layer trace, the filterable log, the after-fight report, hit flashes, slow motion, placeholder audio and accessibility

Plan refs: §3.6 (feedback: hit flashes, part callouts, shake, persistent blood, slow motion on crits, kills and hits on your hero; flavour text Dwarf Fortress style from the resolver's layer trace, localisable templates varied by weapon, material, attack type, outcome and severity, the plain wound list in a tooltip and on the paper-doll, the log filterable (your hero only, your side, kills and serious wounds) and kept for the after-fight report and the chronicle), §2.7 (wound overlays on each body part: blood, bandage, splint, missing limb; animation actions; settings and accessibility: toggles for screen shake, flashes and slow motion; captions for important sounds; strings through `tr()`), §2.11 (audio direction: CC0 or royalty-free positional effects, ambience by weather and place, adaptive music layers calm, tension, battle; placeholder audio from Phase 4, final in Phase 8), §6 ("every resolver outcome has a flavour-text template"), §5 Phase 4 ("feel", "flavour text from layer traces with localisable templates; the filterable log and after-fight report", "combat cause chains").
Code today: `LayerTrace`, `LayerOutcome`, `WoundFlags` (`Sim/Combat/CombatModels.cs`), `MeleeEvent` and `EventKind` (spike: `Windup, Feint, Whiff, Blocked, Parried, Dodged, Hit, Wound, Down, Wake, Death, Rout, Rally, Surrender, Fled, Riposte, Info`) and `AttackDef.verb` ("slashes", "stabs", "bashes", "hacks") in `content/base/items/weapons.jsonc`; the Godot side: `Accessibility.ShakeScale(float)`, `FlashScale(float)`, `TimeScale(float)` in `src/Ravensguard.Game/scripts/Settings.cs` (the read side; effects code **must** go through it), `localization/en.csv` (`keys,en`), `L.cs` (localisation helper), the `LogFeed` widget (plan §2.7), the Phase 3 pattern of "pure code takes an `IWords` provider that Godot backs with `tr()`" (`TalkScreen`).

## 1. Purpose
Make the simulation's depth readable and exciting without changing it. Everything here is **presentation over events**: the sim emits integer `CombatEvent` records; this layer turns them into prose, bars, flashes, sounds and a report. Nothing in this file feeds back into sim state, except the log being saved with a fight and the chronicle entries it requests (through the normal chronicle API).

## 2. Dependencies
| Needs | From |
|---|---|
| `StrikeResult` (trace, wounds, flags), block and parry events | `resolver-v2.md`, `melee-and-ranged.md` |
| Wound lifecycle events, consciousness changes | `wounds-bleeding-pain-healing.md` |
| Morale events (rout, rally, surrender), outcomes, captivity | `morale-capture-succession.md` |
| `Chronicle`, `Cause` chains (`CauseKind.Wound`, `Death`) | exists (`Sim/Core/Chronicle.cs`, `Demography`) |
| `Accessibility` facade, `Settings`, `L`, `en.csv`, `LogFeed`, `BodyDiagram`, nameplates | exists (Game layer) |
| Rig animation actions and wound overlays | plan §2.7, `tools/art` (art pipeline) |
| Weather and ambient state for ambience | exists (`Sim/Weather`) |
New code: `Sim/Combat/Narration/` (`CombatEvent`, `EventRing`, `FlavourBook`, `FlavourFormatter`, `FightLog`, `AfterFightReport`), content `content/base/flavour/*.jsonc`, `content/base/audio/events.jsonc`; Game: `UI/CombatLog.cs`, `UI/AfterFightPanel.cs`, `World/HitEffects.cs`, `Audio/AudioBus.cs`, `Audio/MusicState.cs`.

## 3. Data

### 3.1 `CombatEvent` (sim output, 32 bytes, appended to the `EventRing` each tick)
`Tick` (u32 `FightTick`), `Kind` (u8: `Swing, Whiff, Hit, Blocked, Parried, Dodged, Wound, Severed, Down, Wake, Death, Rout, Rally, Surrender, Fled, Riposte, Shot, ShotMiss, Throw, Treat, Unhorse, HorsePanic, Captured, Robbed, Escape, Info`), `Actor` (u24 entity index), `Target` (u24), `Weapon` (u16 shape id + material), `AttackId` (u8), `Part` (u8), `Outcome` (u8 flags: redirected, called shot, flank, rear, heavy, quick, gap), `Severity` (u8 0..4: graze, wound, grievous, mortal, instantly fatal), `TraceRef` (u32 offset into a side ring that holds the `LayerTrace[]`), `WoundRef` (u32: first wound id), `Momentum` (u16 /16 units).
- Events carry **no text**. Text is built when needed (4.1 lazily), keeping the 530/s typical cost to a 32-byte write.
- **Which events are kept:** for hero and squad fights: all. For other fights inside the bubble: `Kill`, `Down`, `Severed`, `Rout`, `Surrender`, and any event where one party is within 30 m of the camera (a "relevant" flag set by the presentation layer, never read by the sim). The ring holds 4,096 events per fight and spills kills and serious wounds to the `FightLog` (up to 20,000 per battle).

### 3.2 Flavour templates (`content/base/flavour/*.jsonc`, type `flavour`)
```jsonc
{ "type": "flavour", "id": "hit.edge.cut_armour_into_muscle", "slot": "blow",
  "when": { "attack": ["edge"], "armour_outcome": ["cut"], "tissue_deepest": ["muscle"], "severity": [1,2] },
  "weight": 10,
  "text": "FL_BLOW_EDGE_CUT_MUSCLE_1" }       // a tr() key; the English text lives in localization/en.csv
```
`en.csv` rows (`keys,en`): `FL_BLOW_EDGE_CUT_MUSCLE_1,"{attacker}'s {weapon} bites through the {armour_material} {armour_piece} and tears into the {tissue} of {target}'s {part}."`, plus clause rows (`FL_CLAUSE_ARTERY_1,"An artery is opened; blood sprays."`).
Slots (a line is the join of up to 4 pieces; each is chosen independently): `open` (the attack verb phrase; from `AttackDef.verb`: slashes, stabs, hacks, bashes, thrusts, pierces), `armour` (one per armour layer outcome: `Cut`, `Crushed`, `Stopped`, `Deflected`, `NotCovered`, `Bypassed`), `tissue` (per wound: skin, fat, muscle graded, bone graded, organ, structure kinds), `consequence` (flags: `Severed, ArteryHit, VeinHit, NerveHit(motor/sensory), TendonHit, LigamentHit, SpineHit, BoneBroken(grade), OrganHit(which), InsetHit(eye, ear, nose, jaw, teeth), Displaced, Compound, Burn`), `after` (state change: staggers, falls, weapon dropped, loses consciousness, dies). **Conditions** in `when`: attack type (edge, point, blunt, missile, thrown, fall, bite, claw), weapon family and material (steel, iron, bronze, wood, bone), armour piece family (gambeson, mail, plate, helm, shield) and material, tissue deepest, severity, part group, facing (flank, rear), called shot, heavy or quick, target kind (human, horse, draugr, beast). Selection: all templates whose `when` matches; pick by weighted draw with a **recent-use penalty** (a template used in this fight within the last 12 lines has weight x0.25) so the same blow does not read the same way twice (plan §3.6).
Example output (from the plan): *"Aldric's axe bites through the bronze mail and tears into the muscle of Bram's upper arm. An artery is opened; blood sprays."* = `open` (axe verb "bites through" from the `cut` template) + `armour` clause (bronze mail, Cut) + `tissue` (torn muscle) + `consequence` (ArteryHit).
More samples (golden text for FL-3):
- Stopped by plate: *"Edda's spear thrust scrapes along Hugo's steel breastplate and goes no further."* (`Deflected` or `Stopped` with soak > 98%).
- Mail turns a cut: *"Aldric's sword cuts the iron links of Bram's hauberk, but the blow lands as a bruise on his ribs."* (cut-turned-blunt, `Crushed` armour with elastic flag).
- Severing: *"Gunnar's great axe takes Ketil's head from his shoulders in a single swing."* (`Severed` on neck).
- Eye: *"The point of Ivo's dagger slides through the open visor and into Karl's left eye."* (`InsetHit` eye; `Bypassed` gap `face`).
- Fracture: *"The mace crushes the bone of Osric's left forearm. It breaks and the hand goes limp."* (`BoneBroken` grade Broken; motor nerve not hit: "limp" from `Arm` capacity).
- Nerve and spine: *"Her spear takes him low in the back. His legs fold; he does not feel them."* (`SpineHit` cord_back_l).
- Misses and defences: *"Bram turns the blow aside with his shield."* (`Blocked`), *"Edda parries and answers."* (`Parried` + riposte), *"Hugo steps out of the way."* (`Dodged`), *"The arrow whistles past Karl's ear."* (`ShotMiss`).
- Wounds on beasts and monsters: *"The draugr's dry flesh splits and gives off dust, not blood."* (`target kind draugr`), *"The horse screams and rears as the spear opens its chest."*
- Morale: *"The militia breaks and runs."*, *"Ketil lays his sword on the ground."*

### 3.3 Template volume and coverage
Target content for Phase 4: **about 420 text rows** (en.csv keys): open verbs 40, armour 60, tissue 90, consequence 110, after 50, misses and defences 40, morale and after-fight 30. All localisable (`tr()` keys, parameters by name). Name formatting rules: possessive `'s`, articles for creatures ("the draugr"), pronouns by sex, plurals for groups. A `FlavourCoverage` test enumerates every combination of `{attack type} x {LayerOutcome} x {WoundFlags single bit} x {target kind}` and asserts at least one template matches (FL-1), and that every `en.csv` key referenced exists and every `{param}` in a template is supplied by the formatter (FL-2).

## 4. Rules

### 4.1 Formatting (lazy, off the sim thread)
`FlavourFormatter.Format(CombatEvent, IWords)`: looks up the trace and wounds by ref, builds a parameter map (`attacker`, `target`, `weapon` (name from material and shape: "steel arming sword" -> "sword" in prose, "bronze axe"), `armour_piece`, `armour_material`, `part`, `tissue`, `verb`), picks templates (stream 0x480 `FlavourPick`, key `(FightTick, event serial)`, so the same fight produces the same text everywhere and in replays), and returns a string. **Cost** 20 us typical, only for events that will be displayed (visible log, toast, report); a far fight with no log open formats nothing. The formatter is pure and uses no wall-clock or global RNG.
Severity classes (set by the sim from the wound list, not the text): 0 graze (skin/fat only), 1 wound (muscle bruised or torn), 2 grievous (muscle mangled, bone broken, structure cut), 3 mortal (organ, artery, vein of a major limb, head crush), 4 instantly fatal (heart, brain, severed head or neck). The log colours by severity (grey, white, orange, red, bold red); color-blind-safe icons accompany each (plan §2.7: shape markers).

### 4.2 The log (`CombatLog`, the existing `LogFeed` widget)
- Layout: lines with a timestamp (mm:ss since fight start, the `FightTick`/20), an icon by severity, the prose. A line expands (click or Enter) to its **trace table** ("gambeson: cut (need 16.1, momentum 146.9); skin: cut; ...; bone: bruised") and the plain wound list. The same list is the tooltip on the paper-doll and in the wound overlay.
- **Filters** (toggles at the log's top; saved in settings): **Hero only** (events where the hero or the hero's weapon is actor or target), **My side** (hero, squad, followers, allied soldiers), **Kills and serious wounds** (severity >= 2, `Down`, `Death`, `Rout`, `Surrender`, `Severed`), **Misses and defences** (off by default), **Treatment**, **Beasts only**, free text search. Default filter in a fight: Hero only + serious; in a battle: My side + kills.
- Behaviour: auto-scrolls; hover pauses scroll; the log can be **pinned** and resized; a "show all" mode is available in watch mode (watch-mode.md). Keep 600 visible lines per panel; the underlying `FightLog` keeps the fight (up to 20,000 events, 640 KB) and is saved with the fight record and spilled to the chronicle (4.4).
- Per-person **injury ledger**: from the character screen (C) "Injuries" tab lists every wound with its source line and treatment history. This is the "plain wound list".

### 4.3 The after-fight report (`AfterFightPanel`)
Opens when a fight involving the hero ends (and for a squad battle the hero commanded). Sections: (1) **Result:** won, lost, drawn, fled; duration; sides; casualties (dead, knocked out, captured, routed). (2) **You:** blows thrown, landed, blocked, parried, dodged; damage dealt by type; best blow (the severity-4 or highest-momentum line), skills improved (+0.4 x difficulty x (1 - s/100)^2, plan; shown), stamina and blood low point. (3) **Wounds:** paper-doll with every wound and **what to do first** with timers ("Left thigh: artery, 31 s to collapse unless pressed"); infection risks; fractures needing setting within 1 game day; tendon surgery window 1 game day; amputations needed. (4) **Fallen:** each named person who fell, with the line that finished them and a **cause chain** ("Bram died of blood loss from a cut femoral artery inflicted by Aldric's axe in the fight at the ford, which began when the bandits ambushed the cart"): chain links = wound id -> blow event -> attacker -> fight -> encounter or war event (`Cause` records, `CauseKind.Wound` and `Death`; plan §3.12: combat cause chains). (5) **Spoils and losses:** loot, ammo recovered (50%), items damaged (tears, notched weapons), mount health. (6) **Next:** buttons (Treat now, Loot, Rest, Pursue). The panel stays until dismissed; nothing is lost by closing (reopen with **Shift+F7**).
**Chronicle** entries (plan §3.12: every entry has a specific cause): created for named people's deaths and permanent injuries, notable kills (monsters), battles with >= 20 casualties, captures. Template: `CHRON_FIGHT_DEATH` with params; links to the same cause chain.

### 4.4 Hit feedback (`HitEffects`, Game layer, reads `CombatEvent`s, never the sim)
All effects check the facade first (`Accessibility.ShakeScale`, `FlashScale`, `TimeScale`).
| Effect | Trigger | Parameters (reference 60 fps) |
|---|---|---|
| **Hit flash** | any landed blow on a sprite | white flash 2 frames (33 ms) x `Accessibility.FlashScale(1)`; wound-colour tint (red) 6 frames; no strobing: at most 2 flashes per second per sprite |
| **Part callout** | a wound with severity >= 1 | floating text over the part ("Left arm", "SEVERED", "Artery"), 1.2 s rise of 24 px, severity colour; `tr()` key `FL_CALL_*` (about 40 short strings) |
| **Screen shake** | the hero is hit with `Momentum >= 60`, any severing near the hero | amplitude `clamp(M/16, 1, 8)` px x `Accessibility.ShakeScale(1)`, 0.15 s decay; ignored when the setting is off |
| **Persistent blood** | wounds with bleed > 0.5 ml/s | decals on ground tiles at the bleeder's position (1 per 0.5 s while bleeding, size by rate); up to 400 per chunk; dry to brown stains after 3 game hours, fade after 10 game days; trails follow a moving bleeder; a bloody floor hash is saved with chunk deltas as cosmetic only |
| **Wound overlays** | part state | blood, bandage, splint, missing limb overlays per part (art), tears on armour layers below 800 condition |
| **Hit-stop** | severing, heart, brain, kills of named foes near the hero | 60 ms of presentation freeze, skipped when slow motion off |
| **Slow motion** | see 4.5 | |
| **Wake and dying cues** | collapse, KO, last breath | vignette tightening as blood loss rises (15/30/40% steps), heartbeat placeholder audio, never above 40% opacity |

### 4.5 Slow motion
Triggers (only when the hero is involved or within 6 m of the camera focus): a **crit** (severity 4 blow either way), a **kill of a named foe or a monster**, a **hit on your hero with `M_final >= 100` milli-units**, a hero **parry-riposte kill**. Effect: the **presentation time scale** drops to `Accessibility.TimeScale(0.35)` for 0.4 s real, easing in 80 ms and out 150 ms; cooldown 3 s; max 6 per minute. **Implementation:** slow motion changes the number of 20 Hz sim steps per real second (7 instead of 20), never the step length, so it cannot change an outcome and does not break determinism or replays; in watch mode and abstract battles it is off. With the setting off the scale is 1 and nothing slows. The player gets extra reaction time during these moments; that is intended and documented in the tooltip for the toggle.

### 4.6 Placeholder audio hooks (Phase 4; real audio Phase 8)
- `AudioBus.Emit(name, position, intensity)`: the Game layer subscribes to `CombatEvent`s and maps them through `content/base/audio/events.jsonc` to placeholder CC0 samples (`assets/audio/placeholder/*.ogg`): `combat.hit.flesh`, `combat.hit.metal`, `combat.hit.wood`, `combat.block`, `combat.parry`, `combat.miss.swing`, `combat.arrow.loose`, `combat.arrow.hit`, `combat.arrow.miss`, `combat.shield.break`, `combat.bone.break`, `combat.scream` (variant by sex and age), `combat.death`, `combat.horse.rear`, `combat.hooves` (loop scaled by speed), `combat.horn` (formation order), `combat.rout.cry`, `combat.torch.gutter` (exploration), `combat.cavein`. Variants by material and intensity (`M/16` -> 0..1), pitch variance via cosmetic stream 0x481.
- **Positional**: 2D attenuation with distance (full volume within 8 m, falling to 0 at 60 m), occluded by walls by 40%; at most 16 voices; closest and most severe win.
- **Adaptive music state** (`MusicState`): `Calm` (default), `Tension` (an enemy aware within 30 m but no fight, or a night cave entry), `Battle` (a fight with the hero, or >= 5 fighters within 30 m of the camera); transitions with 8 s hysteresis; layers fade over 3 s. Data in `audio/music.jsonc`.
- **Captions** (accessibility): when "captions" are on, each important sound shows a bracketed line at the screen bottom ("[sword clash, left]", "[arrow overhead]", "[horn]"); text from `FL_CAP_*` keys; direction arrow by relative bearing.
- Audio never touches the sim: no sim RNG, no sim state.

### 4.7 Accessibility (all through the facade, one place)
Settings screen additions (existing toggles kept): **Screen shake**, **Flashes** (hit flash, vignette pulses), **Slow motion**, **Captions**, **Log text size** (steps 1x-3x), **Colour-blind safe severity icons** (always on), **Hold or toggle** for block and sprint, **Larger parry window** (melee-and-ranged.md 7), **Reduce blood** (decals off, blood overlay stylised; flavour text filtered of gore words by an alternate template set `gore: low`, the default off). Rule: any new effect code must call `Accessibility` or be rejected in review (the Phase 4 code-review checklist item, mirrored by a lint test FL-12 that greps `HitEffects` for direct `Settings` reads).

### 4.8 Worked example: one exchange, five views
The hero (Aldric, steel axe, `Hero only` filter) fights Bram (bronze mail over gambeson) at a ford; one second of the fight produces these events and lines:
| Tick | Event | Log line (default filter) | Other feedback |
|---|---|---|---|
| 412 | `Swing` (heavy, called shot upper arm) | (not shown: swings are off by default) | wind-up bar on Bram's nameplate, grunt placeholder |
| 421 | `Hit` + `Wound` (axe cut through mail, torn muscle, brachial artery) | *Aldric's axe bites through the bronze mail and tears into the muscle of Bram's upper arm. An artery is opened; blood sprays.* (severity 3, red) | white flash 2 frames, callout "Artery", blood decal and trail, screen shake 3 px, hit-stop 60 ms, `combat.hit.metal` |
| 424 | `Blocked` (Bram's shield, M 61 <= 200) | (filtered out unless "Misses and defences" is on) | shield flash, `combat.block` |
| 721 | `Down` (Bram collapses: blood loss 30% after 15.0 s at 100 ml/s; he fell back from the line) | *Bram staggers and drops to his knees, grey-faced.* (severity 3) | `combat.scream`, callout "Down" |
| 921 | `Death` (blood loss 50% at 25.0 s, untreated) | *Bram bleeds out in the mud.* | `combat.death`, chronicle entry with the cause chain |
Filters: **My side** adds the swings of Aldric's two followers; **Kills and serious** keeps only the artery line and the death; **Everything** shows 31 lines for these 3 s of a 6-person fight; the after-fight report lists Bram's death with the chain "brachial artery cut, Aldric's axe, the fight at the ford, the ambush on the road".

### 4.9 Event kind to feedback mapping (the table the Game layer reads)
| Event kind | Log | Flash | Shake | Callout | Audio | Slow motion |
|---|---|---|---|---|---|---|
| `Hit` / `Wound` severity 0-1 | line | 2 frames | 0 | no | `combat.hit.*` | no |
| severity 2 | line | 2 frames + tint | on hero hit only | part | + `combat.scream` | no |
| severity 3 | line (red) | + tint | 3-5 px on hero | part + wound | + `combat.bone.break` or scream | on hero hit with M >= 100 |
| severity 4 (fatal, severed) | line (bold) | + hit-stop | 6-8 px | "SEVERED" or "KILLED" | `combat.death` | yes (named foe or hero) |
| `Blocked`, `Parried` | optional | shield flash | 0 | "Parry" | `combat.block`, `combat.parry` | riposte kill only |
| `Dodged`, `Whiff`, `ShotMiss` | optional | no | 0 | no | `combat.miss.swing` | no |
| `Rout`, `Rally`, `Surrender` | line | no | 0 | banner icon | `combat.rout.cry`, `combat.horn` | no |
| `Treat`, `Wake` | line | no | 0 | no | cloth rustle (placeholder) | no |
| `Unhorse`, `HorsePanic` | line | no | 2 px | "Unhorsed" | `combat.horse.rear` | yes (hero) |

## 5. Tick placement (20 Hz live versus abstract)
- Live: the sim writes `CombatEvent`s during the combat step. The presentation layer reads the ring at render time (60 fps): formatting, flashes, audio. Nothing in this file runs in the 20 Hz sim loop except the event ring write (0.05 us each).
- Abstract tier: no events per blow. The abstract battle produces an `AbstractReport` (abstract-battles-v1.md) with a few summary lines and the chronicle entries; the **report text** uses the same `FlavourFormatter` slots (morale, casualties) and the cause-chain mechanism.
- Watch mode is the same log with the "show all" mode.

## 6. Determinism
- Text and sound selections are **deterministic but cosmetic**: stream 0x480 `FlavourPick` (key `(FightTick, event serial)`), stream 0x481 `CosmeticJitter` (shake, decal positions, pitch). Neither is read by the sim; both are excluded from `StateHash`. A separate `FlavourHash` (hash of the formatted lines of a fight) is checked in tests to prove the log is reproducible on both OS.
- Slow motion changes steps per real second only (no effect on `FightTick` sequence or results): test FL-9 runs the same fight at 1x and with a forced slow-motion schedule and compares `FightHash`.

## 7. UI and controls
- **F7** opens or focuses the combat log (filters at its top); **Shift+F7** reopens the last after-fight report; **Esc** closes. Both are new keys (overview 6A; F6 is watch mode).
- Existing: **C** (character: injury ledger), the paper-doll tooltips, **Alt** nameplates (morale bar), **X** stance, **T** called shot (shows hit chance from the resolver preview).
- Widgets: `LogFeed` (extended with filters, expand rows), `BodyDiagram`, a new `AfterFightPanel` and a `Callout` pool.
- Settings screen section "Combat feel": the toggles in 4.7.

## 8. Tests (golden)
| Id | Check |
|---|---|
| FL-1 | **Coverage:** for every `{attack type (edge, point, blunt, missile, thrown, fall, bite, claw)} x {LayerOutcome (6)} x {WoundFlags single bit (15)} x {target kind (human, horse, draugr, beast)}` at least one template matches; every `Wound` of every golden blow (resolver RS-2..8) formats to a non-empty line |
| FL-2 | Template validity: every key exists in `en.csv`, every `{param}` is supplied, no template longer than 220 characters, no duplicate ids |
| FL-3 | Golden lines: the plan's example sentence (Aldric/Bram) formats exactly from a recorded blow; the 10 sample lines in 3.2 (stopped by plate, mail turn, severing, eye, fracture, spine, block, parry, dodge, miss) match a golden file |
| FL-4 | Variety: 100 identical blows in one fight produce >= 8 distinct lines; no line repeats within 12 consecutive |
| FL-5 | Filters: "Hero only" returns exactly the events with the hero as actor or target; "Kills and serious" returns severity >= 2 and `Down`/`Death`; "My side" includes allies |
| FL-6 | After-fight report: casualty counts equal the fight record; every death line has a complete cause chain (fight -> wound -> attacker -> trigger) |
| FL-7 | Chronicle: a named death produces one entry with a cause; unnamed soldiers do not |
| FL-8 | Cost: formatting 1,000 events <= 25 ms; 100,000 events recorded cost <= 5 ms; far fights with no log open format 0 lines |
| FL-9 | Slow motion does not change `FightHash` for the same seed (forced 0.35 schedule) |
| FL-10 | Accessibility: with shake off `ShakeScale` returns 0 and no camera offset is applied; flashes off: flash intensity 0 and vignette pulse 0; slow motion off: `TimeScale` 1 |
| FL-11 | Audio events: every `CombatEvent.Kind` maps to a bus event or an explicit "silent"; all referenced placeholder files exist; at most 16 voices |
| FL-12 | Lint: no direct reads of `Settings.ScreenShake/Flashes/SlowMotion` in effect code (must use `Accessibility`) |
| FL-13 | Replay: `FlavourHash` of a recorded fight is identical on both OS and across a save and load mid-fight |

## 9. Open questions (recommended defaults)
| # | Question | Default |
|---|---|---|
| F1 | How graphic should prose be? | As the plan's example; a `gore` setting `low` swaps to an alternate template set. Default `normal` |
| F2 | Should slow motion apply to NPC-vs-NPC fights? | No, only hero-involved or near-camera hero events |
| F3 | Is an auto-opening after-fight report too intrusive after small skirmishes? | Open for fights with >= 1 wound on the hero or >= 3 casualties; otherwise a one-line toast with a link (Shift+F7) |
| F4 | Text volume: 420 rows, localisation | Ship English; keys ready for translators; a CI check that every key in a localisation CSV exists in `en.csv` |
| F5 | Blood decals and performance | 400 per chunk cap, GPU batched; setting to disable |
| F6 | Do we sample voices (screams)? | Placeholder only; recorded or commissioned in Phase 8 |
| F7 | Log retention in saves | Last 2,000 events per hero fight, spilled kills and serious wounds up to 20,000 per battle, trimmed at save to 128 KB per fight |

## 10. Budgets
| Item | Budget |
|---|---|
| Event record | 32 bytes each; ring write <= 0.05 us; ring 4,096 events = 128 KB per active fight |
| `FightLog` per fight | <= 640 KB (20,000 events); saved <= 128 KB after trimming |
| Formatting | <= 25 us per line; <= 2 ms per frame for log updates (<= 60 lines/frame) |
| Hit effects | <= 0.5 ms per frame, <= 120 live callouts, <= 400 decals per chunk |
| Audio | <= 16 voices, <= 1 ms per frame |
| After-fight report build | <= 15 ms for a 2,000-soldier battle (counts only) |
| Content | `flavour/*.jsonc` <= 60 KB, `en.csv` additions <= 80 KB, placeholder audio <= 40 files and <= 6 MB (CC0) |

## Notes (P4-D implementation, step 4)
Code: Sim `src/Ravensguard.Sim/Combat/Flavour/` (`FightLog` and the line, blow and fighter records in `FlavourTypes`, `FlavourRecorder`, `FlavourBook`, `FlavourDescribe`, `FlavourFormatter`, `LogFilter`, `AfterFightReport` with `CauseChain`, `FightChronicle`, `FeedbackPlan` (the planner, `AudioMap`, `VoicePool`, `SlowMotionGovernor`, `StepPacer`, `MusicState`)), `Save/FightLogSave.cs` (the optional "fightlog" section), content `content/base/combat/flavour/*.jsonc` (452 rows, 8 files, 59 KB) and `content/base/audio/events.jsonc` (18 sound hooks), content types `flavour` and `audio_event` in `Ravensguard.Content/Defs/FlavourDefs.cs`, streams 0x480 `FlavourPick` and 0x481 `CosmeticJitter`. Game: `World/Combat/FightFeedback.cs` (flash, callouts, shake, decals, hit-stop, slow motion, vignette, captions, sound hooks), `UI/FightLogPanel.cs` (the feed and the F7 panel), `UI/AfterFightPanel.cs` (Shift+F7), `Audio/AudioBus.cs`, `World/StreetView.FightLog.cs`, the "Combat feel" block of the Display settings tab, and `-- --fight-demo duel --shot-what feed|log|logall|report|hit` for the screenshots. Tests `tests/.../Combat/Flavour/` (FL-1 to FL-13, the golden file `golden_lines.txt`, the prose dump `FlavourSamplesTests`; set `FL_SAMPLES=file` to write the sample lines, `FL_WRITE_GOLDEN=1` to rewrite the golden file).
Decisions taken while building:
- **Listening, not feeding:** the only change to the fight is one hook, `FightWorld.OnEvent` (called from `Emit`, beside the P4-C `OnBlow`). `FlavourRecorder` chains both, writes a `FightLine` struct (about 60 bytes) per event and a `BlowSnap` (the resolver's `LayerStep`s and `WoundV2`s as interned ids, so a saved log reads without the content tables) per blow, and never touches the sim: the same seeded fight has the same `FightHash` at every tick with and without it (FL-9). `Windup`, `Reloaded` and `Shove` write no line (a swing only counts in the report); `Riposte` marks its `Parried` line ("parries and answers"); `Overpowered` marks the `Blocked` line that follows it; a `ProjectileHit` and the `Hit` after it are one line (flagged missile or thrown).
- **The picks are made by a cursor, the text lazily.** Which template each slot of a line uses (stream 0x480, key `(FightTick, line serial)`, draw `slot x 8 + attempt`, a quarter weight on pieces used in the last 12 lines, and a redraw (up to 8) when the set of picks equals any of the last 12 lines' sets, so no line repeats within 12) is chosen in line order by `FightLog.PickCursor` and stored in the line, so a line reads the same whoever looked first, after a replay and after a save (FL-4, FL-13). Strings are built only for lines that are shown: a fight nobody reads makes no picks and no strings (FL-8). A compaction that drops lines (ring 4,096; the serious older lines are kept up to 20,000) makes the picks first.
- **A line is up to five pieces:** a first sentence, from `blow` (a whole sentence for the cases that deserve one: plate scraping, mail turning a cut into a bruise, an eye through a visor, a head taken off, 21 rows) or composed from `open` (60 rows: the subject and verb by attack type, style, facing, material, family), `armour` (50: one clause for the layer that decided it) and `tissue` (69: what the blow does under it; a preposition piece has an "and ..." twin when it follows an armour clause, a verb piece always starts "and"); then up to two `consequence` sentences (111 rows, one per distinct wound flag by priority, told about the wound that carries it). Events that are not blows use one row of `defence` (39), `shot` (7), `after` (56: falling and dying by cause, waking, a broken shield, a dropped weapon, a spoiled swing), `morale` (14) or `report` (25: the result lines, the cause chains and the chronicle texts). 452 rows against the spec's 420; vocabulary (weapons, armour, materials, parts, structures, organs, causes, grades), the log and report strings, 40 callouts and the captions are further `fl_` keys: 814 in all, one contiguous block at the end of `en.csv`.
- **A blow that wounded was not stopped.** The resolver's `Stopped` means "the blow became blunt and went on", so a `Hit` whose deepest armour step is `Stopped` or `Deflected` is told as `crushed` ("the iron links hold their shape but the force goes through"); the terminal armour clauses ("goes no further") are used only for a `Stopped` event, a blow with no wound.
- **Severity** is the sim's: 0 graze, 1 wound, 2 grievous, 3 mortal, 4 fatal, computed from the wound list (organs, spine and arteries of a limb root are 3; vital organs and a severed head or neck 4); `Down` is 3 and `Death` 4. The log marks it with a colour and a shape ([.] [o] [+] [#] [X]).
- **Filters** (`LogFilter`): Hero only (actor or target is the hero), My side (either end on the hero's team), Kills and serious (severity >= 2, falling, dying, rout, yielding, fled, severing), Misses and defences, Treatment, Beasts only and a free text search that formats only the lines that passed the rest. The defaults are the spec's (a fight: hero only and serious; a battle: my side and serious); the choice is saved in `settings.cfg`.
- **Feedback numbers** are integers (`FeedbackPlanner`): white flash 33 ms, wound tint 100 ms (severity >= 2), shake `clamp(M/16, 1, 8)` px (+2 at severity 3) for 150 ms when the hero is hit with M >= 60 (M is the resolver's final momentum in units), hit-stop 60 ms and slow motion for severity 4 near the hero, or a hit on the hero with M >= 100. The planner takes `IFeedbackSettings`; the Game's backing class asks `Accessibility.ShakeScale`, `FlashScale` and `TimeScale` (FL-10), and a lint test (FL-12) greps effect code for direct `Settings.ScreenShake/Flashes/SlowMotion` reads. Callout words and captions are not effects and show with the toggles off.
- **Slow motion changes steps per real second only** (`SlowMotionGovernor` 35 percent for 0.4 s, 80 ms in, 150 ms out, 3 s cooldown, 6 a minute; `StepPacer` turns real microseconds and the scale into whole 20 Hz steps). `FightView` multiplies its time by `Feedback.TimeScale`; `StreetView._Process` does the same for the live fight (one line). FL-9 steps the same skirmish with and without a forced schedule and with and without the recorder and compares the hash.
- **Save:** a new optional section "fightlog" (`FightLogSave`: the newest 2,000 lines and up to 1,000 older serious ones, the picks, the blows they cite, the people; written only while a log exists; no format bump). `WorldState.FightLog` and `FightLogSource` follow `Fight` and `FightSource`; the game keeps the last fight's log so Shift+F7 works after a load. Save, load, save gives the same bytes and the same `FlavourHash` (FL-13).
- **Chronicle** (`FightChronicle`): `fight_death` for a named person (a person key on the kit), `fight_maiming` for a severed limb or a broken spine, `monster_slain`, `fight_capture` and `battle_fought` (20 casualties or more), each with a `CauseKind.Wound` cause that is the cause chain's sentence. Nameless soldiers get nothing (FL-7).
- **After-fight report** (`AfterFightReport`): result, duration and sides, casualties from the log's own end states (equal to the fight's, FL-6), the hero's blows and the damage by kind, the best blow, stamina and blood low points, the hero's wounds with what to do first ("about 31 s until you collapse" for an artery, from the wound's bleed rate), the fallen with the finishing line and the chain "Bram died of blood loss from a cut brachial artery, dealt by Aldric's axe in the fight, which began when strangers sprang an ambush on the road" (every death has all five links), the spoils (the number of things on the ground and arrows to recover). It opens by itself when the hero was wounded or three fell, else a toast points to Shift+F7.
- **Audio** has no files (the brief): `audio_event` rows map every fight event kind to a named sound or an explicit silence (FL-11), with a caption key and a priority; `AudioBus.Emit(name, x, y, intensity)` records the sound and calls a `Sink` for the day there is an audio layer; `VoicePool` keeps 16 voices (closest and most severe), volume full inside 8 m to nothing at 60 m, 40 percent less behind a wall; `MusicState` is the calm, tension and battle machine with 8 s hysteresis and 3 s fades (tested, not yet fed by the street view).
- **Settings:** the Display tab now scrolls and has a "Combat feel" block: shake, flashes, slow motion (with the tooltip that it never changes an outcome), captions, reduce blood (no decals, and the pieces marked `gore: high` are left out of the prose), log text size 1x to 3x; the severity shape marks are always on.
- **Shared files touched:** `FightWorld.cs` (the `OnEvent` hook, 3 lines), `RngStreams.cs` (0x480, 0x481 only), `WorldSave.cs` (the "fightlog" section, 5 additions), `FightView.cs` (recorder, feedback, panels, shake, tint, time scale; about 70 lines in hooks), `FightDemo.cs` (screenshot modes), `StreetView.cs` (the time scale and the two world keys), `StreetView.Fight.cs` (2 calls), `Settings.cs`, `SettingsScreen.cs`, `en.csv` and `en.en.translation`.

### Sample lines (seeded duels, the shipped templates)
Seeded duels, Aldric (sword, heater shield, gambeson, mail, helm) against Bram (axe, padded jack): the log with every filter off, as the test `FlavourSamplesTests` prints it (event kind and severity in brackets).

```
=== seed 7: arming_sword vs axe, 00:11 ===
00:01 [Blocked    s0] Aldric turns the blow aside with his shield.
00:01 [Hit        s2] Aldric's sword cuts deep into the flesh of Bram's left shin. An artery is opened; blood sprays. Bram's knee will not hold; the tendon is cut.
00:02 [Hit        s2] Bram puts everything into one blow of the axe, and it lands. Aldric feels nothing in his left foot now.
00:03 [Blocked    s0] Bram's guard holds against Aldric's sword.
00:03 [Blocked    s0] Aldric's guard is beaten down.
00:03 [Hit        s1] Bram's axe hacks against the linen gambeson, which turns the edge but not the force and jars the bone of Aldric's right forearm.
00:04 [Blocked    s0] Bram blocks Aldric's sword with his own.
00:05 [Blocked    s0] Bram's axe hammers Aldric's shield back into him.
00:05 [Hit        s1] Bram's axe cuts the iron links of Aldric's mail, but the blow lands as a bruise on his forearm.
00:06 [Blocked    s0] Steel meets steel as Bram blocks.
00:07 [Blocked    s0] Bram's blow drives through Aldric's guard and staggers him.
00:07 [Hit        s1] Bram's axe hacks against the mail, which drives the rings inward and bruises Aldric's left forearm to the bone.
00:08 [Parried    s0] With a ringing parry, Bram turns the blow and answers.
00:08 [Blocked    s0] Aldric catches the blow on his weapon and holds.
00:08 [Hit        s1] Bram's axe hacks into the iron mail, which gives way and leaves the bone of Aldric's right forearm aching.
00:08 [Blocked    s0] Aldric's blow is stopped by Bram's guard.
00:09 [Broke      s0] Aldric's shield splinters and falls apart.
00:09 [Blocked    s0] Aldric's guard is beaten down.
00:09 [Hit        s1] Bram's axe hacks into the linen gambeson, which gives way and bruises the bone of Aldric's left forearm.
00:10 [Blocked    s0] Bram's guard holds against Aldric's sword.
00:11 [Blocked    s0] Aldric's guard is beaten down.
00:11 [Hit        s1] Bram's axe cuts the iron links of Aldric's mail, but the blow lands as a bruise on his forearm.
00:11 [Down       s3] Aldric collapses, shaking, and does not rise.
=== seed 11: mace vs spear, 00:04 ===
00:02 [Hit        s2] Bram's aimed thrust finds the mark; the spear goes ripping through the rings of the iron mail and into the bone of Aldric's right thigh. Aldric cannot put his weight on it.
00:02 [Interrupted s0] The blow breaks Aldric's stroke.
00:03 [Hit        s2] Aldric swings the mace and it lands and crushes the bone of Bram's right foot. The crack of it carries across the field.
00:03 [Missed     s0] Bram's spear misses Aldric by a hair.
00:04 [Hit        s2] Aldric's mace slams and snaps the bone in Bram's left foot. Bram's left foot is no use to him until it is set. The joint gives way; the ligament is torn.
00:04 [Down       s3] Bram collapses, shaking, and does not rise.
=== seed 23: longsword vs falchion, 00:14 ===
00:01 [Blocked    s0] Bram blocks Aldric's longsword with his own.
00:02 [Hit        s3] Bram's blow with the falchion bites through a bare spot the mail leaves and cleaves deep into Aldric's right thigh. Aldric's right thigh runs red; the femoral artery is cut.
00:02 [Dodged     s0] Bram sways back and the longsword passes by.
00:04 [Parried    s0] With a ringing parry, Aldric turns the blow and answers.
00:04 [Hit        s1] Aldric's longsword cuts through the stitching of the jack and bruises Bram's left upper arm to the bone.
00:05 [Blocked    s0] Aldric staggers under the weight of Bram's blow but holds.
00:05 [Missed     s0] Aldric's blow goes past Bram.
00:06 [Blocked    s0] Aldric's guard is beaten down.
00:06 [Hit        s1] Bram's falchion chops into the iron mail, which gives way and numbs Aldric's right forearm with the shock of it.
00:08 [Missed     s0] Bram overreaches and misses.
00:08 [Dodged     s0] Bram ducks under Aldric's longsword.
00:10 [Blocked    s0] Bram's blow is stopped by Aldric's guard.
00:11 [Parried    s0] Bram knocks the longsword wide and counters.
00:11 [Missed     s0] The falchion goes wide of Aldric.
00:12 [Hit        s2] Aldric's keen steel longsword slices and leaves the bone of Bram's right foot aching. The feeling leaves Bram's right foot at once.
00:13 [Hit        s2] Bram's aimed cut with the falchion bites straight through the linen gambeson and jars the bone of Aldric's right thigh. A tendon snaps with a sound like a plucked string.
00:13 [Rout       s0] Aldric throws the fight away and bolts.
00:14 [Disarmed   s0] Aldric drops his weapon.
00:14 [Hit        s2] Bram's bronze falchion bites and bruises the bone of Aldric's right hand. The feeling leaves Aldric's right hand at once.
00:14 [Down       s3] Aldric collapses, shaking, and does not rise.
```

### Left for later
- Wound overlays are P4-C's art; tears on armour layers below 800 condition and the persistent decals saving as chunk deltas (decals live for the fight only), dry-to-brown over game hours.
- The "Larger parry window" and "Hold or toggle" accessibility options need a change in the fight's inputs (the sim, the recorder): not done. Treatment lines wait for the treatment events (`Treat`), which the fight does not emit yet; unhorse and horse panic wait for mounted-combat (the audio rows already name them).
- Slow motion for a hero parry-riposte kill: the riposte flag is on the parry line but the kill that follows is not linked to it; the other three triggers are in.
- The After-fight buttons (treat now, loot, rest, pursue) are a one-line hint (the panel has Close only); a mid-fight load does not rebuild the fight view yet (the section keeps the log for the report).
- Gore-low is the `high` mark on 28 pieces; a human pass over the English is wanted (the sample lines above are the first draft).
