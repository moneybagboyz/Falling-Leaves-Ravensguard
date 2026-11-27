# Release: builds, the platform layer, crash reports, the Steam page, and 1.0

Status: design, before any Phase 8 code, on top of f2530c3.
Plan refs: locked decisions (Godot 4.7.x .NET; PC first, Windows and Linux; no web build; no public builds until 1.0), §2.11 (local logs; the crash zip with the log, the last autosave, the content and mod manifest and system info; the F8 report; nothing sent without consent; licences: OFL fonts, a third-party list, audio CC0, royalty-free or commissioned), §5 (Steam page; 1.0).
Decisions: D7 (when the Steam page goes up), D9 (crash reports), D10 (Steam features).

## 1. Goals
1. Reproducible Windows and Linux builds from CI, smoke-booted, with the content cache built in.
2. A platform layer so the game runs the same with or without Steam.
3. Crash reports that help and never send without consent.
4. A Steam page and a release checklist that ends in 1.0.

## 2. What exists and what is new
| Today (f2530c3) | Phase 8 |
|---|---|
| `Game/Diagnostics/GameDiagnostics.cs`, `BugReportScreen.cs` (the crash zip, F8), `--smoke`, `--selftest` | **extended:** the upload by D9; the version in every report |
| Godot export presets (development) | **new:** release presets; CI export jobs; `Game/Platform/IPlatform.cs` with `SteamPlatform` and `NoPlatform` |
| licence files for the font | **new:** `THIRD_PARTY.md` (code, fonts, audio, any art) |

## 3. Rules

### 3.1 Builds (RL-1)
- **Targets:** Windows x86_64 and Linux x86_64, from the Godot 4.7.2 .NET export templates. The Deck runs the Linux build.
- **CI builds** from a tag: the content cache prebuilt, the version stamped (`1.0.0` and the commit), debug tooling (`DevConsole`, debug verbs) compiled out.
- **Smoke:** each export boots `--smoke` and `--selftest` headless in CI (Windows and Linux).
- **Size:** the depot under 2 GB; audio and art binaries from Git LFS.

### 3.2 The platform layer (RL-1)
- `IPlatform`: user name, achievements, cloud for settings, the overlay's state, and the Deck's suspend signal. `NoPlatform` does nothing.
- `SteamPlatform` wraps a maintained .NET Steamworks binding with a licence that allows shipping. If Steam is not running, the game runs on `NoPlatform`.
- **Achievements** (D10, recommended): about 30, read from the chronicle and the `ending` section only (ambition steps, endings, slain legends, a siege held). Ironman shows a mark on them, nothing more.
- **Cloud:** settings only (D10). Saves stay local; they reach 400 MB.

### 3.3 Crash reports (RL-1)
- The crash handler (today) makes the zip. By D9's recommendation, it then asks: "Send this report?" with what is in it. Only on yes is it uploaded (HTTPS, to an endpoint the user owns). No is the default.
- Every report carries the version, the commit, the OS, and the mod manifest. The save in the zip is the last autosave only.
- A report never includes the player's account name or paths outside `user://` (they are redacted, as Phase 3 does for logs).

### 3.4 The Steam page and release checklist (RL-2)
- **The page** (D7 recommends "coming soon" about six months before 1.0; a first version at about month 5 with that month's screenshots, made final here): the store's current capsule and library images, 5 or more 1920x1080 screenshots and a trailer captured from `--autoplay` scenes, the capsule art drawn through the art pipeline in the game's style, a short and a long description, the content descriptors (violence, slavery), and system requirements from PF-1 (recommended: the reference PC; minimum: what holds 30 fps in a 300-person bubble).
- **No playable build** before 1.0 (locked): no demo, no festival build, no playtest app.
- **The release checklist** (in `docs/release/checklist.md`): the code and bug check passed; OV-1 to OV-9 green; the nightlies green; the fun check and tester round 3; `THIRD_PARTY.md` complete; every string read; the save promise (a 1.0 save loads in every 1.x) tested; the store page's text and images final; the build uploaded to a private branch and played once on each OS and the Deck; then made public.

### 3.5 Integer math notes
None in the sim.

## 4. Saves
The save header's `gameVersion` becomes the release version (`1.0.0`). No other change.

## 5. Golden tests
| Id | Name | Asserts |
|---|---|---|
| RL-1 | Builds | CI exports both targets from a tag; each boots `--smoke` and `--selftest` |
| RL-2 | No dev code | a release build has no `DevConsole`, no debug verbs and no `SourceReason.DevOnly` path |
| RL-3 | No platform | with Steam absent the game runs on `NoPlatform` to the title screen and through the smoke |
| RL-4 | Achievements | each achievement unlocks from a scripted chronicle event and only from it |
| RL-5 | Crash consent | a forced crash makes the zip and asks; "no" sends nothing (a test endpoint sees no request) |
| RL-6 | Redaction | a report contains no user name and no path outside `user://` |
| RL-7 | Licences | every bundled font, library, audio and art file has a row in `THIRD_PARTY.md` |
| RL-8 | Requirements | the store's requirements match `docs/perf/reference-pc.md` |
| RL-9 | Save promise | a save from the release candidate loads in the 1.0 build byte-identical on re-save |
| RL-10 | Checklist | the checklist file has every item ticked with its date before the 1.0 tag |

## 6. Parts and gates
| Part | Scope | Gate | Months |
|---|---|---|---|
| P8-RL-1 builds, platform and crash reports | exports, CI, `IPlatform`, Steam, achievements, cloud, crash upload, redaction | RL-1 to RL-6 | 0.7 |
| P8-RL-2 the Steam page and checklist | the page, its assets, requirements, the checklist, the save promise | RL-7 to RL-10 | 0.5 |
