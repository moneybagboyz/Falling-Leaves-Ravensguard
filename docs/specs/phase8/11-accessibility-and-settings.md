# Accessibility and settings: the full settings screen, assists, colour-blind checks and captions

Status: design, before any Phase 8 code, on top of f2530c3.
Plan refs: §2.7 (rebinding per context with conflict warnings; whole-number scaling with an optional smooth mode; colour-blind faction palettes with shape markers; text size steps; toggles for shake, flashes and slow motion; hold-or-toggle for sprint and block; captions), §2.8 (one key map per context, conflict-checked in CI), §3 (honest sliders shown openly), §3f (hints off, minimal or full).
Carried in: a larger parry window and hold-or-toggle (phase-4, decided with the user); the watch settings rows (phase-4).

## 1. Goals
1. Every option the plan lists is in one settings screen, saved, and applied without a restart where possible.
2. Assists that change the sim are honest: saved with the world and shown like the difficulty sliders.
3. Colour, text and motion settings are checked by tests, not by eye.

## 2. What exists and what is new
| Today (f2530c3) | Phase 8 |
|---|---|
| `Game/UI/SettingsScreen.cs` (rebinding), `PixelTheme.cs`, the shape markers, `ControlsOverlay.cs` | **extended:** tabs Display, Text, Motion, Audio, Controls, Assists, Watch, Hints |
| `Core/Difficulty.cs` (seven values) | **appended:** the parry-window assist |
| `tools/Ravensguard.Art` (the palette validator) | **extended:** a colour-blind check |

## 3. Rules

### 3.1 The settings
| Tab | Settings |
|---|---|
| Display | resolution, window mode, UI scale (1x, 2x, 3x), smooth scaling, frame cap (30, 60, 120, none), vsync |
| Text | text size (normal, large, largest: 8, 12 and 16 px pixel font), nameplates always on, log line spacing |
| Motion | screen shake (on, low, off), hit flashes (on, off), slow motion (on, off), camera follow speed |
| Audio | the five bus volumes, mono, captions (off, important, all), caption size |
| Controls | rebinding per context (today), hold-or-toggle for sprint, block and the order wheel, mouse sensitivity, pad settings (`13-controller-and-steam-deck.md`) |
| Assists | the parry window (3.2), pause on any interrupt, slower real-time speed caps |
| Watch | take-back on a key, speed cap while watching, which interrupt kinds pause (phase-4's rows) |
| Hints | off, minimal, full (`04-codex-and-onboarding.md`) |

- Settings are in `user://settings.cfg`, outside saves, except assists (3.2).
- A change applies at once, except the window mode and resolution (applied on Apply, with a 15 s revert).

### 3.2 Assists that change the sim
- **The parry window:** 1,000 pm (normal), 1,500 or 2,000 pm. It scales the hero's parry and block timing windows in `Combat/Melee2` for the hero only.
- It is **saved in the header's difficulty array** as the eighth value. A seven-value array reads as 1,000. It is shown in the world settings beside the honest sliders, and on the Ironman badge.
- It may be changed at any time outside Ironman; in Ironman it is fixed at the start.
- Other assists (pause on interrupts, speed caps) change only the Game, not the sim.

### 3.3 Colour-blind safety
- The check simulates protanopia, deuteranopia and tritanopia on the faction palette rows and the UI's state colours (good, warning, bad, seen, unseen).
- **Pass:** every pair of faction colours differs by at least **20** in the simulated CIELAB lightness-and-colour distance (ΔE 2000), and every pair also differs by its shape marker. Every state colour has an icon too.
- The check is a tool (floats allowed); its numbers are logged in the art report.

### 3.4 Captions
- Every audio row marked important has a caption (today's `caption` field). Captions show the source's direction as an arrow at the screen edge when it is off-screen.
- Caption text goes through `tr()`; size follows the caption size setting.

### 3.5 Integer math notes
- The parry window is `window_ticks x assist_pm / 1000`, rounded up, in sim ticks.

## 4. Saves
The header's difficulty array gains an eighth value (`ParryAssistPm`). Nothing else.

## 5. Golden tests
| Id | Name | Asserts |
|---|---|---|
| AX-1 | Settings round trip | every setting written and read back equal; a missing file gives defaults |
| AX-2 | Applied at once | text size, shake, flashes and slow motion change without restart (the smoke checks each) |
| AX-3 | Text sizes | every screen at 960x540 with the largest text has no clipped label (screenshot check) |
| AX-4 | Motion off | with shake and flashes off, a fight produces no camera offset and no flash frames |
| AX-5 | Hold or toggle | block as toggle stays up with no key held; as hold drops on release |
| AX-6 | Parry assist | at 2,000 pm the hero's parry window is twice the ticks; NPCs unchanged |
| AX-7 | Assist saved | the eighth value round-trips; a seven-value save reads 1,000; Ironman refuses a change |
| AX-8 | Colour-blind | every faction pair passes ΔE 20 under each simulation, and every state colour has an icon |
| AX-9 | Captions | every important audio row has a caption key that resolves; off-screen sounds show the arrow |
| AX-10 | Rebinding | rebinding every action in every context keeps 0 conflicts (`--check-keymap`) and the overlay shows the new key |

## 6. Parts and gates
| Part | Scope | Gate | Months |
|---|---|---|---|
| P8-AX-1 accessibility and settings | the tabs, assists, colour-blind check, captions, watch rows | AX-1 to AX-10 | 0.9 |
