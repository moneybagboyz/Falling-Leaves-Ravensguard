# Testers, local logs, crash reports, the F8 bug report and deterministic replay

Plan refs: §2.11 ("Private testers and crash reports (from Phase 3): local logs; a crash handler that zips the log, the last autosave, the content and mod manifest, and system info; an F8 bug-report button that adds a
screenshot; nothing is sent anywhere without the tester's consent"; tooling: "deterministic replay of a bug save"; saves and Ironman backups "only for crash recovery"; autosave every 15 real minutes and before major moments),
§2.8 (`F8` report a bug, testers), §2.4 (counter-based RNG keyed by (seed, tick, entity, stream), worker results applied at tick barriers, immutable snapshots; determinism scoped to "same build, same content"), §6 (fun checks:
"from Phase 3, 2-3 private testers, always asked: 'did you understand why?'"; CI hashes; `--autoplay`), §7 (risk: "no public feedback until 1.0; private testers from Phase 3 are the mitigation"), §3f (onboarding is playtested),
Locked decisions ("No public builds until 1.0"). Phase 2: `abstract-tick-and-lodbridge.md` 2.2, 3.1, 5 (`EconCommand` log `(tickKey, Frame, Seq)`, `ScheduleReplay`, `EconomyHash` per day, snapshots).

## 1. Purpose
Let a small number of private testers play Phase 3 builds, tell us what confused them, and let us reproduce what they saw. Everything is local and consented: logs are written to the tester's own disk, a report is a zip they can
inspect and send by hand, and a report is detailed enough to **replay the bug deterministically** on our machines. There is no network code in the game.

## 2. Dependencies
| Needs | From |
|---|---|
| Save system (`SaveWriter`, `SaveReader`, `SaveMigrator`, autosave slots, `WorldSave`), content and mod manifest (`ModManifest`, `ContentDatabase` hash), `BaselineGuard` | exists (`Ravensguard.Sim/Save`, `Ravensguard.Content`) |
| `EconCommand` queue and `CommandPump`, `ExecutedCommand` log, `ScheduleReplay`, `EconomyHash`, `TickerStats` | Phase 2 (exists in part, `Abstract/`) |
| Live sim fixed step `LiveWorld.Step`, `MoveInput`, `SimClock` | exists (`Live/`) |
| `InputDefs` action `report_bug` (F8, world context), `InputContext.Menu` | exists (`Game/Input`) |
| `SimInspector`, `DevContext`, dev console (dev builds) | exists (`Game/Debug`) |
| Onboarding log, hints, interrupts and autopilot log | `onboarding-v1.md`, `autopilot-v1.md`, `travel-encounters-camp.md` |
| Plan §6 `--autoplay`, `tools/perf` | plan |
New code: `Ravensguard.Game.Diagnostics` (`LogService`, `CrashHandler`, `Heartbeat`, `ReportBuilder`, `ConsentStore`, `ReportScreen`, `FeedbackForm`), `Ravensguard.Sim.Replay` (`InputTrace`, `CommandLog`, `StateHashTrail`, `ReplayRunner`), `tools/replay` (headless CLI), a build flavour `Tester`.

## 3. Data

### 3.1 Where things live
| Path (under the Godot user data dir, `user://`) | Content | Retention |
|---|---|---|
| `logs/game-<yyyymmdd-hhmmss>.log` | the session log (3.2) | last 10 sessions or 50 MB, oldest deleted first |
| `logs/session-<id>.json` | session header + `clean_exit` flag + last heartbeat | same as the log |
| `heartbeat.json` | written once a second: real time, frame counter, sim tick, scene, speed, last command seq | overwritten |
| `reports/report-<yyyymmdd-hhmmss>-<id>.zip` | an F8 bug report, a crash report or feedback (3.4) | until the tester deletes them (button "Delete all reports") |
| `profile/consent.json` | `{ version, accepted_at, include_save_default, include_screenshot_default, include_log_default }` | until withdrawn |
| `replay/ring/` | rolling `InputTrace` and `CommandLog` since the last autosave (memory-mapped, 2 MB cap) | until the next autosave |

### 3.2 Log format and levels
One line per record, plain text, UTF-8, `\n`: `HH:MM:SS.mmm | tick 123456 | LEVEL | category | message | k=v k=v`. `LEVEL` in `TRACE, DEBUG, INFO, WARN, ERROR, FATAL`; default `INFO` (`--verbose` or the setting gives `DEBUG`). Categories: `boot, content, save, sim, abstract, live, ui, input (action ids, never key codes), perf, onboarding, autopilot, replay, report`.
- **Always logged:** build and content hash, mod list, settings (non-personal), seed, world and run ids; every save, load and migration; every worker overrun above 400 ms; every `Faulted` region; every `LimitViolation`; every interrupt; every F8; asset or content errors.
- **Rate limiting:** a message repeated more than 20 times in one second is collapsed to one line plus `(repeated N times)`; a hard cap of 20,000 lines per minute. **Redaction:** the user's home or user name in any path becomes `~`; machine and user names are never written; environment variables are never logged; the hero's and NPC names are game data and may appear.
- Writes go through a lock-free queue to a background writer with 64 KB buffers and flush on `WARN` and above, on exit, and on crash.

### 3.3 Session header (`session-<id>.json`)
```jsonc
{ "id": "a1b2c3", "started_real": "2026-12-01T18:20:41", "build": { "version": "0.3.4-tester", "git": "5f3a9c2", "content_hash": "e9b1...", "mods": [ { "id": "base", "version": "0.3.4", "hash": "..." } ], "flavour": "Tester" },
  "system": { "os": "Linux 6.x / Windows 11", "cpu": "6 cores", "ram_mb": 16384, "gpu": "RTX 3060", "driver": "...", "dotnet": "8.0", "godot": "4.7", "renderer": "Forward+", "screen": "1920x1080", "locale": "en" },
  "game": { "seed": 1234, "world_id": "w-001", "run_id": "r-004", "ironman": false, "difficulty": "default" },
  "clean_exit": false }
```
No user name, host name, IP address, e-mail or path outside `~` appears anywhere.

### 3.4 Report zip (`report.json` and files)
| File | Content | Cap |
|---|---|---|
| `report.json` | `id`, `kind` (`bug, crash, hang, feedback`), `category` (`Bug, Confusing, I did not understand why, Balance, Idea`), tester text (what happened, what you expected, `understood`: yes / partly / no), `build`, `system`, `game` (as 3.3), `where` (screen or context, selected entity id and its inspector text, region id, game time, tick, speed), `perf` (frame ms p50 p99, tick ms p50 p99, worker overruns), `files[]` (name, bytes, sha256), `consent` (version, time) | 100 KB |
| `screenshot.png` | the framebuffer **captured before the dialog opens** (so the dialog is not in it), 1920 x 1080 max | 2 MB |
| `log_tail.txt` | last 5,000 lines of the session log | 5 MB |
| `session.json`, `system.json`, `settings.json`, `content_manifest.json` | as above; settings without paths | small |
| `autosave.bin` | the newest autosave (**T0**), compressed | 200 MB (larger: left out, `files[]` says why; the report is still replayable from `bug.save` if given) |
| `replay.trace` | `InputTrace` and `CommandLog` from T0 to the report moment T1, run-length coded | 5 MB |
| `hashes.csv` | the `StateHashTrail` entries T0 to T1 (3.6) | 100 KB |
| `bug.save` | a full save of the state at T1, **if the tester ticks it** (not the Ironman slot: a copy in the zip) | 200 MB |
| `crash.txt` (crash only) | exception chain with stack traces, thread name, tick, scene, the last 50 commands, native crash info if present | 1 MB |
| `playtest_summary.json` (feedback) | onboarding steps with times, hints seen, deaths, F8 count, answers to the questions | 50 KB |
Total zip cap `report_max_mb = 250`; if the sum is larger, `bug.save` and then `autosave.bin` are dropped, in that order, and the report says so.

### 3.5 Consent (`ConsentStore`)
First F8, first crash prompt or first "Finish session": a consent screen shows the exact list of what a report contains (the table above), where the zip is stored, **that nothing is uploaded or sent by the game**, and three checkboxes (screenshot, log tail, save files; defaults on in tester builds, user can change). Accepting writes `consent.json` with the **text version** shown; if the text version changes in a later build the screen appears again. "Withdraw consent and delete reports" removes `consent.json` and `reports/`. Logs are always written locally (they are not sent); consent governs only the **creation of report zips** with those contents.

### 3.6 Determinism instrumentation
- `InputTrace`: per live tick the sampled hero `MoveInput` and boolean actions, run-length coded `(tick, flags)` only on change; speed changes; UI-originated sim commands are `Command`s stamped `(Tick, Seq)` (the Phase 2 queue, `Frame` = sim tick). Window since the last autosave (at most 15 real minutes, plus a forced extra autosave "before a major moment" per plan §2.11).
- `CommandLog`: the Phase 2 `ExecutedCommand` list `(TickKey, Frame, Seq, command, result)` including `Origin` (`Human, Autopilot, Npc`).
- `StateHashTrail`: `EconomyHash` per game day (Phase 2) and a **live-state hash** every 400 ticks (20 real seconds): SHA-256 over sorted live entity ids, fixed-point positions and headings, action ids, hero inventory rows and the clock; plus `TalkHash` and `CraftHash` per day (Phase 3 additions). Entries `(tick, kind, hash64)`.
- **Never in the sim:** wall-clock time, frame time, unseeded random, dictionary order, thread completion order (plan §6 review rules). The trace thus fully determines the run.

## 4. Rules

### 4.1 Logging
`LogService` starts before anything else (boot category), writes a header line with build, content hash and mods, and registers listeners for Godot's `push_error`/`push_warning` and .NET `Trace` so engine messages land in the same file. Dev builds also print to the console. The log is plain text so testers can read and redact it before sending.

### 4.2 Crash handler
- **Managed crashes:** `AppDomain.UnhandledException`, `TaskScheduler.UnobservedTaskException`, and a try/catch around each worker job (the abstract hourly worker; its existing `Faulted` region state is logged) and around the main-thread frame. On a fatal error the handler: (1) logs `FATAL`, (2) captures the screenshot if the render thread is alive, (3) writes `crash.txt`, (4) builds the zip on the crashing thread without allocating on the game heap beyond the 2 MB rescue buffer, with `kind = crash`, (5) shows a plain dialog "The game stopped. A report was saved to `<path>`. Nothing was sent. Open the folder?" (consent applies: without consent the zip is not built; the dialog says the log is in `logs/`), (6) marks `clean_exit = false`.
- **Native crashes and hangs:** `Heartbeat` writes once a second from a watchdog thread; the main thread bumps a frame counter. A hang is a frame counter not advancing for 10 s (the watchdog logs `FATAL hang` with the last tick and scene and sets a `hang` marker; it does not kill the game). **On the next launch**, if the previous session's header has `clean_exit = false` or a hang marker, the game shows "The game did not close properly last time. Create a report?"; the report is built from that session's log, `heartbeat.json` and the newest autosave (`kind = crash` or `hang`). Ironman: the **two rotating backups** (plan §2.11) are offered if the main slot fails integrity (`SaveReader` checksum), never as a reload tool.
- The handler itself must not throw: it is a try/catch chain that falls back to writing only `crash.txt` and the log tail as plain files.

### 4.3 F8 bug report (`report_bug`)
1. F8 anywhere (world, menus, Ironman included). The game **pauses the sim** (not Ironman-affecting; autopilot also pauses) and captures the framebuffer.
2. The consent screen appears if needed. Then the report screen: **category** selector, **What happened?** (up to 2,000 characters), **What did you expect?**, the radio **"Did you understand why that happened?" yes / partly / no** (plan §3f/§6), checkboxes for what to include (3.5 defaults), an item "Include a full save of right now (`bug.save`)", and a **file list with sizes** that updates live ("autosave.bin 38 MB, screenshot 0.6 MB, log 0.4 MB").
3. **Save report** builds the zip on a worker, shows progress and then the path with an **Open folder** button; Esc cancels everything and unpauses.
4. The game resumes in the same state. The report is never uploaded by the game. A **Journal note** records that a report was made at this time (tester convenience).
- **During a crash-recovered session** F8 includes the previous crash id, so linked reports are visible.

### 4.4 Tester build flavour
- A `Tester` flavour build (private zips handed to 2-3 testers) enables F8 and the feedback form, writes a version stamp `0.3.x-tester+<git>` on the main menu and in every report, and **expires** 60 days after the build date (a warning at 45 days; after 60 the game offers to export the log and quits to the menu, never deleting saves). The public 1.0 build may keep F8 (consent-gated) but ships no expiry.
- **Session feedback form** (`Esc > Finish session`): five fixed questions stored in `playtest_summary.json`: (1) Did you understand why that happened? (pick the confusing moment from the last 20 entries of the onboarding/interrupt log), (2) Where were you stuck? (free text, with the step list from onboarding), (3) What did you enjoy? (4) What did you ignore? (5) Would you play again tomorrow? The summary is a small file in `reports/` the tester chooses to send.
- **Testers' packet** (outside the game, written by the lead): how to install, what to try (the first ride, the first odd job, a night on the road, a craft with a wait), how to send reports, and the question list. The lead records each session in `docs/playtests/` (plan §6).

### 4.5 Deterministic replay of a bug save
**Replay is a promise about the same build and content hash** (plan §2.2): `ReplayRunner` checks `build.git` and `content_hash` and refuses or warns on mismatch ("Build differs: results may differ").
- **Procedure (`tools/replay report.zip [--until tick] [--step] [--verify] [--dump tick]`):** (1) load `autosave.bin` (T0) through `SaveMigrator` into a headless `WorldRuntime`; (2) apply `replay.trace` as a stream of per-tick inputs and commands at their `(Tick, Seq)` stamps to a headless `LiveWorld` and `WorldTicker`; (3) run the fixed 20 Hz step to T1 as fast as possible (no render); (4) compare the `StateHashTrail`: the first mismatching entry is reported with the tick and the kind (live, economy, talk, craft) and a diff of the entities involved (positions, rows) as `divergence.txt`; (5) with `--until` it stops at a tick for inspection; with `--break-on-exception` it stops at the first exception; (6) for a crash report it runs until the exception recurs and prints the same stack.
- **In-game replay screen (dev builds):** `Load report` opens the same files, runs the replay at 1x to 64x with the HUD, lets the developer pause, inspect (sim inspector) and take control at T1 (the run then diverges from the recorded one, marked "live control" in the HUD).
- **What is replayable:** everything driven by the sim and the traced inputs. Not replayable: rendering, audio, UI animation timing, the tester's mouse movements that change nothing in the sim. **Requirement on the code:** input to the sim is sampled **once per tick at the tick boundary** and nothing else reads real time; `LiveWorld.Step` already advances by fixed ticks; UI actions that change the sim are commands with tick stamps (`Command.Frame` is the sim tick).
- **Replay of a bug save only (`bug.save` without a trace):** loads at T1 and lets the developer play forward from the exact state; it is exact for the state but cannot show how it was reached. Reports with both `autosave.bin` and `replay.trace` replay the way in.
- **Hash cadence:** `EconomyHash` per game day, the live hash every 400 ticks, `TalkHash`/`CraftHash` per game day; a divergence report tells within which interval things went wrong, then `--dump` narrows it.

### 4.6 Privacy and the "no network" rule
- The game ships **no networking code**: no `HttpClient`, `System.Net.*`, Godot `HTTPRequest`, `WebSocket` or `ENet` usage in game and sim assemblies (CI test TR-8 scans the compiled assemblies and the Godot project for these references; an allow-list is empty in Phase 3). Steam or crash-upload services, if ever added, are a separate post-1.0 decision.
- A report is just a file; the tester decides who receives it. The consent screen lists the contents; the zip has a `README.txt` explaining each file and how to delete them.
- Reports contain no path other than `~`-relative ones, no machine identifiers, no accounts, no keystrokes (only action ids). The hero name and world seed are included because they identify the game state.

### 4.7 Tick placement
| Where | What |
|---|---|
| Frame (render thread) | log queue push only, heartbeat counter bump |
| Live 20 Hz tick | input sampling and trace append (O(1)), live hash every 400 ticks |
| Barrier / hourly | `CommandLog` append, hash trail append (daily), worker overrun log |
| Worker | report zip build, hash computation for the abstract tier |
| Autosave | rotates `replay/ring` (new T0) and clears the trace window |
| Background | heartbeat thread (1 Hz), log writer thread |

## 5. Determinism
No RNG streams are added (the diagnostics code draws none). The replay tooling relies on the existing counter-based RNG keyed by `(seed, tickKey, entity, stream, draw)`, where the Phase 3 `tickKey` is always a game-time window (overview section 5), so no RNG state needs saving and a replay at a different speed draws the same values. New hash kinds `TalkHash`, `CraftHash` use the same SHA-256 of sorted rows as `EconomyHash` (integers only). Replay output is identical on Windows and Linux for the same build (CI job `replay-x-os`: record on Linux, replay on Windows and vice versa).

## 6. UI and controls
- **F8** anywhere (`report_bug`, shown in the controls overlay and on the pause menu as "Report a problem"). **Report screen** layout as 4.3; a status bar shows the zip size.
- **Pause menu** adds "Finish session (feedback)". The main menu shows the tester version stamp and the expiry date. Settings > Privacy: "Logs location: Open folder", "Reports: Open folder / Delete all", "Consent: View / Withdraw", log level.
- **Crash dialog:** two buttons, Open folder and Close; no network, no "send".

## 7. Tests (golden)
| id | Case | Expected |
|---|---|---|
| TR-1 | Log rotation | after 12 sessions only the newest 10 remain; a 60 MB set is trimmed to under 50 MB oldest first |
| TR-2 | Rate limit | a loop emitting 1,000,000 identical `ERROR` lines in 1 s produces at most 25 lines and `(repeated 999,9xx times)`; no frame over budget |
| TR-3 | Redaction | with a fake home `/home/alice` and user `alice`, no log, report or `report.json` contains `alice` or `/home/`; all paths start with `~` |
| TR-4 | Crash injection (managed): throw in a worker job, in the main thread frame and in a `Task` | each produces a zip with `report.json`, `crash.txt`, `log_tail.txt`, `session.json`, `system.json`, `content_manifest.json`, and the newest autosave when consent allows; the game exits cleanly with the dialog; `clean_exit` false |
| TR-5 | Native crash / hang simulation: kill the process, stop the frame counter for 10 s | on next launch the recovery prompt appears; the hang marker and last tick are in `crash.txt`; report built from the old session's files |
| TR-6 | Consent | without `consent.json` no zip is ever created; the screen shows the exact file list; changing the text version re-asks; withdraw deletes `reports/` |
| TR-7 | F8 screenshot | the screenshot does not contain the report dialog (compare against the frame buffer before open); F8 works in menus, in a dialogue, in Ironman; the sim is paused during the dialog and resumes after |
| TR-8 | No network | scanning the built assemblies and `project.godot` for `System.Net.Http`, `System.Net.Sockets`, `HTTPRequest`, `WebSocket`, `ENet` finds 0 references; CI fails otherwise |
| TR-9 | Size caps | a 300 MB autosave is dropped and noted; a report with everything on is at most `report_max_mb`; the progress UI shows true sizes within 5% |
| TR-10 | Replay exactness: record 10 game minutes (12,000 ticks) with 40 hero inputs changes, 30 commands, a trade, a job pickup, an autopilot hour; then replay from `autosave.bin` | all hash entries equal (30 live hashes, 1 economy hash per game day crossed); final state equal bit for bit |
| TR-11 | Cross-OS replay | recorded on Linux, replayed on Windows (CI): identical hash trail |
| TR-12 | Divergence detection | change one command in `replay.trace` | the first mismatching hash index and kind are reported, `divergence.txt` lists the differing entity rows |
| TR-13 | Worker timing independence: replay with the worker delayed by 0, 50, 400 ms | identical hashes |
| TR-14 | Trace size | 10 minutes of play with 600 input changes and 30 commands: `replay.trace` under 40 KB; memory ring under 2 MB for 15 minutes |
| TR-15 | Build mismatch | report from build A replayed with build B: warning shown, result labelled unreliable |
| TR-16 | Heartbeat and logging overhead | log + heartbeat + trace + hash add <= 0.15 ms per frame average; 0 allocations in steady state |
| TR-17 | Ironman | F8 `bug.save` is a copy inside the zip; the Ironman slot and its two backups are unchanged byte for byte |
| TR-18 | Feedback form | five answers produce `playtest_summary.json` with the onboarding step times from the log; no network |
| TR-19 | Expiry | a build dated 61 days ago refuses to start a new game and offers log export; saves untouched |

## 8. Open questions for the user
1. **Include the autosave by default?** It is the most useful file and the most personal one. Default: **on** in tester builds with the file list shown and removable before saving; in a public build, off.
2. **A future upload option:** none in Phase 3, none by default ever. Default: no networking; revisit after 1.0 with an explicit opt-in.
3. **Tester build expiry:** 60 days, with a warning at 45. Default yes (limits leaks of private builds).
4. **Default log level:** `INFO`; testers may switch to `DEBUG`. Default `INFO`.
5. **Report retention:** keep until the tester deletes them (no auto-delete). Default keep.
6. **Screenshot default:** on (it is the most useful for "I did not understand why"). Default on.

## 9. Budgets
| Item | Budget |
|---|---|
| Logging per frame | <= 0.05 ms; writer thread <= 2% of one core |
| Heartbeat | 1 write per second, <= 0.1 ms |
| Input trace and command log | <= 2 MB memory per 15 minutes; <= 40 KB per 10 minutes on disk |
| Live-state hash | <= 0.2 ms every 400 ticks (<= 300 entities) |
| F8: screenshot capture | <= 30 ms (freezes one frame) |
| F8: zip of 100 MB | <= 5 s on a worker, UI stays responsive |
| Crash dialog | appears <= 2 s after the fault |
| Replay speed | headless >= 100x real time for a 300-person bubble (12,000 ticks in <= 6 s) |
| Disk | logs <= 50 MB; reports not limited by the game, each <= 250 MB |

## 10. Notes (implementation, Phase 3 step 2)
What was built, where it differs from the text above, and what is left.

**Code.** `Ravensguard.Sim/Diagnostics/`: `LogService` (rolling log, levels, 20 per second collapse, 20,000 lines per minute cap, 10 sessions / 50 MB rotation, `session-<id>.json` with `clean_exit`), `Redactor`, `ConsentStore`/`ConsentText`, `ReportBuilder` (zip, caps, `WriteCrash`, never throws), `ReportArchive`, `CommandLog` (`replay.trace`, binary, delta-coded), `StateHashTrail`/`LiveStateHash` (`hashes.csv`), `SessionRecorder`, `ReplayRunner`. `Ravensguard.Game/Diagnostics/`: `GameDiagnostics` (start-up, crash handler, heartbeat, engine log bridge), `BugReportScreen` (F8), `ReplayTool`. The spec's namespace `Ravensguard.Sim.Replay` became `Ravensguard.Sim.Diagnostics`; there is no separate `tools/replay` project, the CLI is the game's dev command (below).
- **No change to `Live/`.** Recording wraps `LiveWorld` instead of hooking it: the street view calls `SessionRecorder.SetInput / SetSpeed / SetTorch / ToggleTorch / ClearPath / CommandMoveTo / UsePortal / Step`, which log the command with `LiveWorld.Tick` and forward. Any new player command to the live sim must go through the recorder or replay will diverge.
- **F8** stays `world.report_bug` (key table unchanged; `--check-keymap` 0 conflicts, selftest checks it is the only F8 binding). World actions are live in every screen, so it works in menus and in Ironman; `App._Input` takes the screenshot first, then pushes `BugReportScreen` on the router (the screen below stops, so the sim pauses). Esc or Cancel closes it. The smoke test opens it, presses F8 again (no second window) and closes it.
- **Consent** (overview open question 1 to 6: all recommended defaults used): off by default, `profile/consent.json` with the text version (`ConsentText.Version`); the consent list is shown inside the F8 screen (no separate first-run screen yet). Settings > Privacy has a consent toggle, DEBUG log level, open logs / reports, and "Delete all reports and withdraw consent". Without consent no zip is written anywhere: a crash then writes only `crash-<stamp>.txt` and `crash-<stamp>-log_tail.txt` into `logs/` and the dialog says so. Even with consent nothing is sent; there is no network code, and test TR-8 scans `src/` for `System.Net.Http`, sockets, `HttpClient`, `HTTPRequest`, `WebSocketPeer`, `ENet`.
- **Redaction** replaces the home folder, `/home/<x>`, `/Users/<x>`, `C:\Users\<x>` and, as a whole word, the user name with `~`, in every text file and `report.json`. A hero called like the user is redacted too (privacy over fidelity).
- **Crash handler** hooks `AppDomain.UnhandledException` (main and worker threads), unobserved task exceptions (logged, observed), C# exceptions that Godot catches in a callback (via a Godot `Logger`; written up, dialog, then the game quits), and `WorldRuntime.Fault` (reported once, the game keeps running as before). Only the first fatal error per session is reported. The dialog is `OS.Alert`; `--crash-test [--crash-test-consent]` injects an unhandled worker-thread exception for a manual check. The heartbeat thread writes `heartbeat.json` every second and logs `FATAL hang` after 10 s without frames. Next-launch recovery prompt (4.2, TR-5) is **not built yet**: the next launch only logs "the previous session did not close properly" and keeps `LastCrashId`.
- **Replay.** The trace window restarts at every autosave (`Begin` of an autosave job in `StreetView`); T0 is a copy of the saved file kept in `replay/t0.sav` (the rotating slots would move it), or, at session start, the save the run was loaded from (tick 0). A report carries `autosave.bin` only when it is exactly T0 of the trace. Hashes: a `start` hash of what a save keeps (clock, world position, facing, torch) and a `live` hash every 400 ticks plus one at the report moment; positions are world positions, so a differently cut chunk window hashes the same. `--replay bug.zip [--until tick]` (Godot dev command, exits 0 matched or crash reproduced, 1 failed, 2 diverged) restores the save, steps a headless `LiveWorld` with synchronous streaming and prints the first mismatch; a build or content hash mismatch prints "Build differs: results may differ". The smoke test records a short walk, writes a report and replays it.
- **Limits, to do later.** (1) A path in progress or the walking mode at an autosave is not in `LiveResume`, so a window that starts mid-path can diverge until the path ends (the `start` hash ignores them on purpose); saving them is a `LiveResume` change. (2) Only live-tier player commands are traced; `EconCommand` replay, `EconomyHash` per day, `TalkHash`, `CraftHash` and the abstract tier are not hashed (the abstract worker runs on its own thread and the hash would need a barrier). (3) Not built: `bug.save`, the Ironman backups prompt, `Tester` flavour and expiry (4.4), the feedback form, `divergence.txt` entity diffs, `--break-on-exception`, the in-game replay screen, `playtest_summary.json`, the pause-menu "Report a problem" entry, TR-5, TR-11, TR-13, TR-16, TR-17, TR-19. (4) The log ring is in memory (trace) and `replay/` holds only `t0.sav`; the 2 MB memory-mapped ring is not used.
- **Tests** (`tests/Ravensguard.Sim.Tests/Diagnostics`): TR-1 rotation, TR-2 repeat collapse, TR-3 redaction, TR-4 crash zip contents (and no-consent fallback), TR-6 consent, TR-8 network scan, TR-10 replay of 12,000 ticks with recorded commands (31 hashes, trace under 40 KB: TR-14), TR-12 divergence, plus the command-log round trip, rebase, until / exception stops and the end hash.
