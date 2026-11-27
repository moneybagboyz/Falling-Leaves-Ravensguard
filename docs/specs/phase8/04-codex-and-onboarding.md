# The Codex and onboarding v2: the in-game manual, and a first hour that teaches

Status: design, before any Phase 8 code, on top of f2530c3.
Plan refs: §3f (no tutorial level; a mentor; contextual hints with "learn more"; controls always visible; the practice yard; failure teaches; the Codex is the manual, full in Phase 8; onboarding playtested), §3.13 (warnings before failure).
Carried in: S15 (phase-5: the first hour is not one chained script).

## 1. Goals
1. The Codex becomes the manual: about 120 entries over every system, unlocked as the player meets things.
2. Numbers in entries come from content, so the manual never drifts from the game.
3. Onboarding v2: the first hour runs as one chain for every origin, hints are audited, and defeat explains itself.

## 2. What exists and what is new
| Today (f2530c3) | Phase 8 |
|---|---|
| `Sim/Codex/CodexBook.cs` (legends from history), `Game/UI/CodexScreen.cs`, `CodexRules.cs` | **extended:** manual entries, categories, search, live numbers; content `codex/*.jsonc` |
| `Sim/Onboarding/HintEngine.cs`, `content/base/onboarding/hints.jsonc`, `FirstHourBot.cs`, `PracticeYard.cs` | **extended:** hint audit, levels, the chained script, the defeat screen v2 |
| `Game/UI/ControlsOverlay.cs` | **extended:** pages for Tactical, OrderWheel, Armoury and the helm |

## 3. Rules

### 3.1 Manual entries
- **An entry** is `{ id, category, title key, body key, unlock, links, params }`.
- **Categories:** People; Body and wounds; Combat; Mounted; Armies; Sieges; Ships; Economy; Industry; Building; Crafting; Law; Faith; Family; World and weather; Monsters; Legends (today's); Controls.
- **Unlock:** by a hint trigger (first sight of a thing), by an event (first siege seen), or "always" (Controls and the basics). The hero's Codex is per run; the world's legends stay as today.
- **Live numbers:** a body holds `{param}` tokens bound to content paths, for example `{levy_days}` to the levy's unpaid days (10). The binder reads the loaded content, with mods applied. An unbound token fails the content test.
- **Monsters:** pages fill in three stages (rumoured, met, slain), as `03-landmarks-npcs-and-bestiary.md` 3.3.
- **Links:** "learn more" from every hint toast and from tooltips that carry a codex id. Entries link to each other and to chronicle entries.
- **Search** is by title and body in the current language, case-folded.

### 3.2 Hints v2
- **Levels** (a setting): off, minimal (only hints marked `essential`), full. Default full.
- **Audit:** every manual entry has a trigger or is marked `manual_only`. Every hint names a Codex entry. A hint fires once a run; the seen set is saved.
- **New contexts** get a first-use hint and a controls page: Tactical, OrderWheel, Armoury, the helm, the Ambitions tab, the Epilogue.
- **Quiet in danger:** hints wait while a fight is on or an alert panel shows (as Phase 3's hint bar).

### 3.3 The first hour as one chain (S15)
- The bot plays each origin's first hour as one script: the mentor's contract, the first ambition step, the origin's set pieces (the hides, the guard contract, the deserter, the ford ambush, the splint), then the **first branch choice** of the tree.
- The mentor's second contract leads into the start ambition's last step. This is the only new authored link; brutality stays.

### 3.4 Failure teaches v2
- After a defeat, the screen shows the cause chain from the chronicle: **3 to 6 links**, newest last.
- Each link has one **"what could have helped"** line from `codex/lessons.jsonc`, keyed by cause kind (for example a wound that bled out: "a bandage stops bleeding; carry linen").
- Each line links to its Codex entry.

### 3.5 Integer math notes
- Live numbers are shown in the content's units through the existing formatters (`MoneyText` for mites, days, kg).
- No sim randomness: the Codex and hints draw nothing.

## 4. Saves
- `codex` section: additive key `Manual` (entry ids by name, with the stage for monsters).
- `onboarding` section: additive `HintsSeen` (ids by name) and `HintLevel`.

## 5. UI and strings
- The Codex screen gains a category column, search (`/`), and back and forward. 960x540 is the floor.
- About 120 entries at about 60 to 150 words each: about 12,000 words of English, read by the user before the freeze.

## 6. Golden tests
| Id | Name | Asserts |
|---|---|---|
| CX-1 | Entries load | about 120 entries; every link and codex id resolves |
| CX-2 | Live numbers | every `{param}` binds; a mod that sets levy days to 12 shows 12 |
| CX-3 | Unlocks | the first siege seen unlocks Sieges; a page is not shown before its unlock |
| CX-4 | Monster stages | a rumour shows the rumoured page; a sighting the met page; a kill the slain page |
| CX-5 | Search | "levy" finds the levy, muster and purveyance entries |
| CX-6 | Save | a Codex with 40 entries and stages round-trips; an old save opens with legends only |
| CX-7 | Hint audit | every entry has a trigger or `manual_only`; every hint names an entry |
| CX-8 | Hint levels | off fires none; minimal only `essential`; full all, each once a run |
| CX-9 | Quiet in danger | no hint shows during a fight; the queued one shows after |
| CX-10 | Chained first hour | all 8 origins complete the chain to the first branch choice (the bot) |
| CX-11 | Defeat screen | a scripted defeat shows 3 to 6 links, each with a lesson line and an entry link |
| CX-12 | Controls pages | the overlay has a page for every context in `InputDefs`, and every key it shows is the bound one |

## 7. Parts and gates
| Part | Scope | Gate | Months |
|---|---|---|---|
| P8-CX-1 the Codex | entries, categories, the binder, monster stages, search | CX-1 to CX-6 | 0.8 |
| P8-CX-2 onboarding v2 | hint audit and levels, the chain, defeat v2, the controls pages | CX-7 to CX-12 | 0.8 |

Tester round 2 (about month 8) plays the first hour of two origins and is asked "did you understand why?" after each defeat.
