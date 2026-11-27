# Presentation polish: the art and feel items carried since Phase 3

Status: design, before any Phase 8 code, on top of f2530c3.
Plan refs: §2.7 (paper-doll layers, wound overlays, readability, the art pipeline: one palette of about 48 colours, the validator, palette swaps, the rig), §3.6 (hit flashes, persistent blood, slow motion on crits, kills and hits on the hero), §7 (art: one palette and a validator; commission the character sprites if the style sheet is not good enough).
Carried in: hair, the apron layer and skin tones (phase-3); screenshots of followers and `lie`, the nameplate doubling (phase-3); the cave beast's and draugr's art (phase-4); slow motion on a parry-riposte kill, drying blood, the after-fight buttons, the morale bar, the horse wound diagram (phase-4); the tooltip lines, the crafting and build-mode UI list, LOD-bridge walkers, G5 (phase-5); a roof by culture or wealth (phase-5); the art of forts, chariots and `cheval_de_frise` (phase-7). Waiting on the user: art style sheet v2.

## 1. Goals
1. Every carried art and feel item is done, through the palette validator.
2. The screens and feedback the plan lists are complete before the Steam page's screenshots.

## 2. Rules

### 2.1 Art (PP-1)
- **Characters:** hair in 6 styles and 8 colours, the apron layer, skin tones as 5 palette rows; all as rig parts, baked in 3 views.
- **Monsters:** the draugr on its own rig (not a tinted human), the cave beast, and the new monsters of `03-landmarks-npcs-and-bestiary.md` (bog-thing, barghest, lindworm in segments), at 32x64 or 64x64 as their size needs.
- **War:** the war wagon and its fort boards, the chariot in 8 facings with its wheels in 4 frames, `cheval_de_frise`, siege ladders raised and falling, the siege tower's drawbridge.
- **Roofs** by culture and wealth: thatch, shingle, slate, tile, as 4 roof sets drawn at Field and Command zoom.
- Every new sprite passes the palette, size and outline checks.

### 2.2 Feel and UI (PP-2)
- **Slow motion** on a hero parry-riposte kill: 0.3x for 0.6 real s, off when the Motion setting is off.
- **Blood:** decals persist; they darken after 2 game hours, fade after 3 game days, and rain washes outdoor ones at once. Decals are Game-side and never saved in the sim (they are rebuilt from wound events in view).
- **The after-fight report:** next-step buttons (treat wounds, loot, take prisoners, leave).
- **The morale bar** under nameplates in fights (a 3-step colour plus a shape).
- **The horse wound diagram** on the mounted paper-doll.
- **Tooltips:** "Edge 82%" (an edge's condition against new) and "Protection x1.4" (against the base for its slot).
- **Crafting and build mode:** Q on a station or item opens the craft screen filtered; build mode's Q verbs and E on a ghost; the controls overlay's Build page with the World keys kept; the vehicle panel as a part grid; a separate stall panel; the resource overlay in build mode and on the map; copying a survey report.
- **LOD-bridge walkers:** construction workers and live staff walk to their stations as LOD-bridge people.
- **A threading pass** (G5): UI state set from pool threads moves to `CallDeferred`.
- **Screenshots** of followers on the trail and the `lie` action join the `--autoplay` set; the nameplate doubling is watched in the smoke (two nameplates over one person fail).

### 2.3 Integer math notes
None in the sim. Decal timings read the game clock from the snapshot.

## 3. Saves
None.

## 4. Golden tests
| Id | Name | Asserts |
|---|---|---|
| PP-1 | Palette | every new sprite passes the palette, size and outline checks |
| PP-2 | Rig parts | hair, apron and skin rows bake in 3 views with no missing frame |
| PP-3 | Monsters | each monster has idle, move, attack, hit and fall in 3 views |
| PP-4 | War art | wagons, forts, chariots (8 facings, 4 wheel frames), ladders and towers bake complete |
| PP-5 | Roofs | each culture and wealth band picks its roof set; the map shows it |
| PP-6 | Slow motion | a parry-riposte kill by the hero slows for 0.6 s; with Motion off it does not |
| PP-7 | Blood | a decal darkens at 2 hours, is gone at 3 days, and goes at once in rain outdoors |
| PP-8 | Screens | the after-fight buttons, morale bar, horse diagram and tooltip lines show in the smoke at 960x540 |
| PP-9 | No pool-thread UI | the threading check finds no UI property set off the main thread |
| PP-10 | Screenshots | the `--autoplay` set includes followers on the trail and `lie`; no doubled nameplate in any frame |

## 5. Parts and gates
| Part | Scope | Gate | Months |
|---|---|---|---|
| P8-PP-1 art | characters, monsters, war art, roofs | PP-1 to PP-5 | 0.9 |
| P8-PP-2 UI and feel | slow motion, blood, the screens, tooltips, crafting and build UI, walkers, threading, screenshots | PP-6 to PP-10 | 0.7 |

If style sheet v2 is not approved by month 8, the plan's §7 fallback applies: commission the character sprites and keep the tiles.
