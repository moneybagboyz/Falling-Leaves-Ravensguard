# Spike 2: melee feel

Phase 0 spike (plan §4A.3 real-time melee, §2.6 resolver, §2.8 combat stance controls). Two deliverables: a pure-C# melee model with headless numbers (this document), and a Godot scene a human must play to judge **feel** (which no benchmark can).

Source:
- `src/Ravensguard.Sim/Combat/Melee/` : `MeleeRules` (every tunable), `MeleeTypes`, `MeleeWorld` (state machine, vitals, morale, impact), `MeleeAi`, `MeleeScenarios` (loadouts, duel/skirmish, benchmark).
- `tests/Ravensguard.Sim.Tests/Combat/Melee/` : `MeleeTests` (26 checks incl. timings, parry window, feint, exhaustion, bleed-out times, determinism, 10v10), `MeleeBenchTests` (the benchmark below).
- `src/Ravensguard.Game/Spikes/MeleeSpike.{cs,tscn}` : the scene.

## How a human judges feel (do this before trusting any number here)

```
G=/opt/godot/Godot_v4.7.2-stable_mono_linux_x86_64/Godot_v4.7.2-stable_mono_linux.x86_64
cd src/Ravensguard.Game
dotnet build ../../Ravensguard.slnx
$G --path . res://Spikes/MeleeSpike.tscn                 # 1v1 duel, you vs AI (needs a display and a mouse)
$G --path . -- --spike melee                              # same, via the app entry point
$G --path . res://Spikes/MeleeSpike.tscn -- --skirmish   # start in the 10v10 skirmish (you are one of the blue Wolves)
$G --path . res://Spikes/MeleeSpike.tscn -- --auto --seed=3   # AI vs AI demo, no input
```

Controls: **WASD** move (Shift sprint) · **LMB** hold = wind up, release = strike (hold 0.3 s past the wind-up for a HEAVY swing) · **RMB** hold = block; *tap it in the 0.2 s before a blow lands = parry* (opens a 0.4 s riposte, half wind-up); RMB during your own wind-up's first half = feint · **Ctrl+WASD** dodge · **T** called shot (game pauses; up/down = body part, left/right = attack type, **F** strikes, Esc cancels; shows P(hit)) · **K** duel/skirmish · **N** next opponent (sword+padded, axe+mail, spear+tunic) · **R** restart.

What to look for: can you read an incoming blow from the bar over the foe (orange fill; the **red tail is the 0.2 s parry window**)? Does a 0.45 s sword wind-up feel fair to react to, or should the window be bigger? Is a parry satisfying or a lottery (skill 55 means 46%)? Does stamina make you think before swinging? Is the 20 Hz step visibly steppy? Do the floating wound callouts and the log make wounds legible fast enough?

Headless smoke: `$G --headless --path src/Ravensguard.Game res://Spikes/MeleeSpike.tscn --quit-after 200`. Screenshot: see the command in the task brief (`--write-movie ... --fixed-fps 20`); under movie capture the scene runs the AI-vs-AI demo, so a still shows wind-up bars, callouts and the log.

## What was built

- **Timeline at 20 Hz**: Idle / Windup / Strike / Recover / Block / Dodge / Stagger / Down / Dead / Fled / Surrendered. Ticks at skill 0, no load: dagger 6/6, sword 9/8, axe 12/12, maul 18/18, spear 8/10 (wind-up/recover); strike window 2 ticks (0.10 s) or 3 (0.15 s, axe and maul). Scaled by ×(1 − skill/400), +1% per kg over capacity (15 + 2.5 × strength stat), ×1.3 below 25 stamina, ×1.2 when weakened by blood loss, ×0.5 on a riposte.
- **Block** (hold): 100° front arc; weapon cover 0.30 + 0.002 × skill (halved on the flank); shield cover 0.45 heater / 0.75 tower +0.10 per braced neighbour (max 2), front only; −0.20 each when stamina < 25; a rear blow cannot be blocked at all. Cost 3 + 1 per 50 J absorbed.
- **Parry**: Block pressed ≤ 4 ticks (0.20 s) before impact rolls skill/120 (cap 95%); success negates the blow, staggers the attacker (recovery + 0.2 s) and opens a 0.4 s riposte. Failed parry falls through to a plain block roll.
- **Dodge**: 0.35 s, 1.0 s cooldown after it ends, 12 stamina, 80% evade while active (dodge distance about 1.5 m). **Feint**: cancel in the first half of a wind-up (swing stamina is lost, +2).
- **Facing**: front / flank / rear from the defender's facing vector (no trig); passed to `StrikeResolver` (flank +25%, rear +50% hit chance). Blows land only if the target is within reach and ±60° of the attacker's facing, so side-stepping makes a swing whiff.
- **Stamina** per plan: swing 4 + 2/kg, block 3 + 1/50 J, sprint 8/s, regen 12/s resting, 5/s guarding or in more than 20 kg of armour.
- **Vitals**: wounds from the resolver become bleeders (capillary 0.5, muscle 3, vein 15, artery 80–120, severed 90, organs per body plan). Capillary/muscle bleeds clot with a 90 s half-life; arteries, veins, organs and severances never do. Shock by blood lost: 15% weakened, 30% collapse, 40% unconscious, 50% dead. Pain pool decays 2%/s, knockout at 60 + 4 × toughness (default toughness 10 = 100). Heart hit: collapse after 6 s; brain cut kills, brain crush only knocks out. Severed/tendon/nerve/broken right arm = cannot wield; left arm = drops shield; leg = 30% speed.
- **Morale** (2 Hz): courage 70 − decaying penalties (−6 per friendly fall within 10 m, −15 flanked, −2 per hit, −2/s per routing neighbour, −15 limb loss) − 0.35 × pain − 1 × blood-loss %. Rout < 20 (runs, rallies > 40), surrender < 10 when caught, escapes the field at 20 m.
- **Determinism**: every roll is `DetRng` keyed (seed, tick, fighter, stream); states are plain doubles with `+ − × ÷ sqrt` only. `SeededDuelIsDeterministic` compares full event text and final vitals.
- **AI**: approach, opportunistic attack when the foe recovers, patient when a blow is incoming; once per incoming wind-up, inside the last 0.2 s, picks parry (0.15 + skill/130) / dodge (12%) / hold block (35%); skilled AI aims by vulnerability (armour gaps) with probability skill/150; feints against a guard; backs off and guards when stamina < 22; flees when routing.

## Benchmark: 200 seeded AI-vs-AI duels per matchup

Reproduce: `dotnet test tests/Ravensguard.Sim.Tests --filter "FullyQualifiedName~MeleeBench" --logger "console;verbosity=detailed"`. Steel weapons, skills 30–59 per seed, start 6 m apart. A = first-named fighter. Fight ends when one side is out (down, dead, fled, yielded); "KO'd who then bled out" continues the sim for 60 s with no treatment.

| Matchup | n | mean s | median s | p10-p90 s | A wins | death | knockout | rout | surrender | stalemate | KO'd who then bled out (60 s) |
|---|---|---|---|---|---|---|---|---|---|---|---|
| sword vs sword (padded) | 200 | 5.1 | 4.1 | 2.3-8.5 | 52% | 7% | 86% | 2% | 5% | 0% | 172 |
| axe vs mail+gambeson+helm (sword) | 200 | 6.6 | 4.8 | 2.9-16.6 | 46% | 6% | 88% | 3% | 5% | 0% | 167 |
| spear vs unarmoured (tunic, sword) | 200 | 6.0 | 4.7 | 2.9-9.4 | 11% | 12% | 83% | 2% | 4% | 0% | 166 |

| Matchup | blows thrown / duel | wounding hits | blocked | parried | dodged |
|---|---|---|---|---|---|
| sword vs sword | 5.7 | 2.9 | 0.3 | 0.6 | 0.1 |
| axe vs mail | 6.1 | 2.9 | 0.3 | 0.6 | 0.1 |
| spear vs unarmoured | 7.5 | 4.1 | 0.4 | 0.5 | 0.1 |

| Wound records per duel | sword vs sword | axe vs mail | spear vs unarmoured |
|---|---|---|---|
| skin/fat cut | 8.87 | 7.39 | 12.33 |
| muscle cut | 2.87 | 2.39 | 3.96 |
| severed limb | **1.89** | **1.67** | **1.38** |
| organ | 0.75 | 1.03 | 1.08 |
| nerve/tendon | 0.40 | 0.46 | 0.51 |
| vein cut | 0.36 | 0.23 | 0.29 |
| artery cut | 0.32 | 0.23 | 0.35 |
| crush/bruise | 0.00 | 1.40 | 0.00 |
| fracture | 0.00 | 0.46 | 0.00 |
| vital organ (heart/brain) | 0.11 | 0.12 | 0.20 |

10v10 skirmish (20 seeds, Wolves: spear + heater-shield swords vs Crows: axe/maul/sword): all 20 finish, mean 23.8 s, Wolves win 15/20; per battle 5.5 dead, 4.7 down, 3.1 fled, 2.8 yielded, 12.3 rout events. Routs, knockouts and surrenders all occur; morale visibly decides the battle.

Bleed-out checks (tests): a 100 ml/s artery downs in 15 s and kills in 25 s; an 80 ml/s one in 18.8 s and 31 s. Both inside ±30% of the plan's ~18 s / ~30 s.

## Findings and tuning notes

1. **The real-time layer works as specified; the resolver is the lethality problem.** Duels last 5–7 s with about 6 swings, against a feel target of roughly 10–20 s. Almost every wounding hit is serious: **1.4–1.9 limbs severed per duel**, and a sword slash through a linen gambeson cuts bone and severs a leg, arm or neck in about 1 of 2 landed blows (neck severed in about 18% of the sword duels). The dump of a duel log shows "cut skin, cut fat, cut muscle, cut bone, SEVERED" on padded upper legs. Cause is in `StrikeResolver` / content (momentum about 100–150 vs shear/fracture thresholds of gambeson and bone; a 300 mm sword penetration against 40–200 mm part depths), not in the melee layer. **Fix before judging combat length**: calibrate momentum against DF (the plan says Phase 0 does this), make bone a real stop for edge cuts, and require the blow to *remain* edged with momentum left to sever. I did not touch the resolver (out of scope).
2. **Spear loses (11%)** because thrusts (point, no severing) are out-raced by sword slashes that sever. Same root cause; reach advantage is real (spear lands first) but cannot show while one cut ends the fight.
3. **Most knocked-out losers die untreated** (166–172 of 200) because severances bleed at 90 ml/s. Survival then rests entirely on bandage/tourniquet actions (plan: pressure −80% in 4 s, tourniquet −98%) which are not in this spike. Add a "bandage" action to the next iteration and re-measure.
4. **Defences are rarely used (0.3 blocks + 0.6 parries per duel)**: both fighters usually trade within the same second. The AI now waits while a blow is incoming and attacks when the foe recovers, which helped only a little; once blows stop being instant fight-enders, defensive play should rise on its own. Human check: is the 0.2 s parry window readable at 20 Hz with the red-tail bar?
5. **Pain**: default toughness 10 (KO at 100) because toughness 5 (KO at 80) knocked people out on a single amputation. A severed limb is 40 pain plus tissue (about 60 total); two serious wounds knock a man out. Plan formula unchanged; only the toughness stat default is my choice.
6. **Morale** (courage 70, −0.35 per pain, −1 per % blood lost) gives 1–5% routs and 3–7% yields in duels and 12 rout events per 10v10: fine for v1; rallies are untested against captains/banners (not modelled).
7. Stamina matters only mildly in a 6 s duel (swing 6.8 of 100); it will bite once fights are longer. Exhaustion slowing the wind-up and weakening blocks is covered by tests.
8. Simplifications to revisit: instant turning with blended facing, no weapon-to-weapon reach/range interplay beyond a single reach number, no shield damage, no captain/banner morale, no grappling, the player's attack target is "nearest in the 120° cone" with the called-shot zone, and the parry is a roll rather than a skill-vs-timing contest.

## Decision

**GO on the model, RESHAPE on lethality before the Phase 4 combat build.**
- Keep: 20 Hz action timeline, wind-up/strike/recover table, block/parry/dodge/feint, stamina, vitals (bleed/pain/shock), morale. They are deterministic, cheap (200 duels run in under a second; 20 skirmishes of up to 300 s run inside the 26 s test suite), tested and produce a sensible outcome mix once something ends a fight.
- Reshape: (a) resolver calibration so one landed blow wounds rather than amputates (target: 10–20 s duels, severing a rare event, armour mattering); (b) add bandaging so knockouts are survivable; (c) re-run this benchmark after (a), expecting more blocks/parries and a spear win rate above 50%.
- **Not decided here, a human must**: whether the feel is good. Play `MeleeSpike.tscn` against all three opponents and the skirmish; pay attention to reaction readability (wind-up bar, 0.2 s parry window), whether holding for a heavy swing is worth the exposure, and called-shot pacing (T, pick, F).

## Follow-up: first lethality fix
- Slash reach was far too deep. The old values were arming sword 300 mm, longsword 350, axe 250 and dagger 90. They are now 70, 85, 110 and 40.
- A limb is now severed only when the reach **left after armour** is at least the part's depth. Before, the attack's raw reach was compared with the part's depth.

Results over 200 duels per matchup, compared with before the fix:

| Matchup | Severed limbs per duel | Mean duel length |
|---|---|---|
| Sword vs sword | 0.50 (was 1.4–1.9) | 8.0 s (was 5–7 s) |
| Axe vs mail | 1.00 | 8.3 s |
| Spear vs unarmoured | 0.34 | 8.4 s |

The 10v10 skirmish now averages 29.5 s.

**Still open (Phase 4 combat work):**
- Too few blows are defended. About 1.2 of 10 blows are blocked or parried; a 40–60% defence rate would give the 10–20 s duels we want.
- The AI's guard and stance choices need work.
- Bandaging isn't modelled yet. Most fighters who are knocked out still bleed to death within 60 s, because nobody can bandage them.
- Feel still needs to be judged by playing `MeleeSpike.tscn`.

## Follow-up: Phase 0 review fixes
- Blows that land in the same tick now resolve against the state at the start of the tick. Before, the fighter with the lower id always won a mutual exchange.
- The AI now picks its attack by situation: blunt or point against armour, edge against bare flesh.

New benchmark over 200 duels per matchup:

| Matchup | Mean duel length | Side A wins |
|---|---|---|
| Sword vs sword | 8.0 s | 49% |
| Axe vs mail | 9.7 s | 64% |
| Spear vs unarmoured | 8.4 s | 34% |

The remaining reshape items listed above still stand for Phase 4.
