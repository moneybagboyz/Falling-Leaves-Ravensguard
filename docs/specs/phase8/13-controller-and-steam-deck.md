# Controller and Steam Deck: a pad in every context, and a Deck profile

Status: design, before any Phase 8 code, on top of f2530c3.
Plan refs: locked decisions (PC first, Steam Deck later; mouse and keyboard equal, rebindable, always shown), §2.8 (one key map per context, conflict-checked in CI), §2.7 (whole-number UI scale), §5 Phase 8 (controller and Steam Deck support).
Decision D5 (how far at 1.0; recommended: a full pad in world, travel, combat, menus and the Ledger; build mode and designers by a virtual cursor; Deck "Playable").

## 1. Goals
1. Every action has a pad binding, in every context, checked in CI like the keys.
2. Menus and tables are reached by focus; a virtual cursor covers what needs pointing.
3. The Deck runs the native Linux build at 1280x800 with a profile that holds its frame.

## 2. What exists and what is new
| Today (f2530c3) | Phase 8 |
|---|---|
| `Game/Input/InputDefs.cs` (17 contexts with Tactical, OrderWheel and Armoury), `KeyMap.cs`, `InputBootstrap.cs`, `--check-keymap` | **extended:** a pad map per context; pad glyphs; focus navigation; `Input/VirtualCursor.cs` |
| `UI/OrderWheel.cs`, the radial and context menus | **read** by the stick |

## 3. Rules

### 3.1 Pad maps
- Each context has a pad map beside its key map: face buttons, shoulders, triggers, sticks and their clicks, the d-pad, and **chords** with a held shoulder (for example LB + face buttons).
- `--check-keymap` checks pad maps the same way: no duplicate binding in a context, and every action has a key **and** a pad binding (unless marked mouse-only, in which case the virtual cursor covers it).
- Rebinding works for the pad as for keys (`11-accessibility-and-settings.md`).
- **Glyphs:** every KeyHint shows the last-used device's glyph (keyboard, Xbox-style, PlayStation-style, Deck). The device switches on the first input from the other.

### 3.2 World, travel and combat
- **Left stick** moves the hero; **right stick** moves the camera or aims (in combat stance).
- **Combat:** triggers attack and block; a shoulder opens the **called-shot radial** on the body diagram (the stick picks the part, a face button the attack type); parry is the block on the wind-up's timing, as on keys.
- **Tactical:** the stick moves a selection cursor; F1 to F4 map to the d-pad; the order wheel is the right stick while its button is held.
- **Speed and pause:** the d-pad's up and down change speed; Start pauses.

### 3.3 Menus and the Ledger
- **Focus navigation:** every screen has a focus order (left to right, top to bottom, panes by shoulder). The focused control has an outline ring.
- Tables (the Ledger, the Army tab, trade) move by row on the d-pad, by page on the triggers; sort and filter on face buttons.
- Text entry (names, search) opens the platform keyboard on the Deck, and an on-screen pixel keyboard elsewhere.

### 3.4 The virtual cursor
- Build mode, the blueprint editor and the vehicle designer use a **virtual cursor** on the right stick: speed 600 px/s at 1x, snapping to the grid cell under it; A places, B cancels.
- The cursor works in any screen as a fallback (a stick click toggles it).

### 3.5 The Steam Deck (CT-2)
- **The build:** the native Linux build, not Proton.
- **The screen:** 1280x800; UI scale 1x with the large text size by default; the world at 2x where it fits (as §2.7's rule).
- **The profile** (`deck` preset): frame cap 30 or 40; lighting radius steps down one; the default battle size is set from the PF-1 numbers (CT-9); the crowd's Field minis as on desktop.
- **Steam Input:** an action manifest ships with the game, with the pad maps as its default layout.
- **Suspend:** on resume, the sim stays paused until input; the autosave runs before sleep when the platform reports it.

### 3.6 Integer math notes
None in the sim: input becomes the same commands as keys. Stick dead zones and curves are Game-side floats.

## 4. Saves
None. Pad bindings and the profile are in `user://settings.cfg`.

## 5. Golden tests
| Id | Name | Asserts |
|---|---|---|
| CT-1 | Every action bound | every action in every context has a key and a pad binding or is mouse-only with the cursor |
| CT-2 | No conflicts | `--check-keymap` reports 0 pad conflicts, chords included |
| CT-3 | Same commands | a scripted fight by pad and by keys produces the same command stream and hash |
| CT-4 | Focus | every screen's controls are reachable by focus at 960x540 and 1280x800 (smoke walks them) |
| CT-5 | Glyphs | hints switch glyphs on the first input from the other device |
| CT-6 | Called shots | the radial picks each body part and attack type of the diagram |
| CT-7 | Cursor | the virtual cursor places a wall segment in build mode on the grid cell under it |
| CT-8 | Deck layout | every screen at 1280x800 with large text has no clipped label |
| CT-9 | Deck profile | on a Deck (or its preset on the reference PC at a 30 fps cap), a 300-person bubble holds 30 fps; the battle size default is logged |
| CT-10 | Suspend | a suspend and resume keeps the world paused and loses nothing |

## 6. Parts and gates
| Part | Scope | Gate | Months |
|---|---|---|---|
| P8-CT-1 the controller | pad maps, the check, glyphs, world, combat, Tactical, focus, tables | CT-1 to CT-6 | 0.9 |
| P8-CT-2 the Steam Deck | the cursor, the Deck layout and profile, Steam Input, suspend | CT-7 to CT-10 | 0.6 |

D5 option (b), every screen native and Deck "Verified", adds about 0.8 months to CT-2.
