# Phase 1 code and bug check

Two reviewers covered all Phase 1 code: one the simulation (world generation, people, weather, lighting, live world, Codex, hero, saves), one the game screens, tools and CI. Every finding was checked against the code, most with a probe, and fixed by two separate passes with regression tests.

- **Findings:** 29. 27 fixed; 2 partly fixed with known limits (listed at the end).
- **Tests:** 492 → 544, green three runs in a row; build has 0 warnings.
- **Checks:** `--smoke`, `--check-keymap`, `--selftest` and the art drift check pass.
- **CI:** compares world hashes on Windows and Linux.

## Done-when results (plan §5, Phase 1)
| Check | Result |
|---|---|
| New world in under 3 min, history ≤ 60 s | **Met.** Seed 1234 from scratch to the first save in 4.7 s; history 1–2 s for 250 years. A library world loads to the first frame in 2.3 s |
| Town-to-town travel times match §1 within 10% | **Met after fixes.** Jog speed is 3.0 m/s, so 8 km takes 44 min. Town spacing was retuned: 20 of 20 nearest-town links are now 6–10 km (was 10 of 17), and road routes fit §1 within 10% |
| Ruins, grudges and family lines trace to history | **Met.** Every ruin, battlefield and landmark carries its history event; lords descend from history dynasties |
| Same seed, same world on Windows and Linux | **Met.** CI hashes continent, history, region, people, landmarks and sample chunks on both and compares them. Golden hashes for seeds 1 and 1234 are pinned in tests |
| 60 fps at street zoom on the reference PC | **Not yet measured:** needs the user's PC. Headless steady-state frames are under 16 ms after the fixes |

## Simulation (14 findings)
| # | Severity | Defect | Fix |
|---|---|---|---|
| 1 | Medium | Play state was never saved: hero position, live clock, Codex unlocks and the explored map were lost on save | `LiveWorld` owns them. `WorldSave.CaptureLive` saves them and `Restore` resumes them. The game's pause-menu Save, quicksave, autosave and Continue use it |
| 2 | Medium–high | Weather for a customised world was rebuilt from default continent settings | Weather is built from the world's own continent (`RegionWeather.ForContinent`) |
| 3 | Medium | Snow depth jumped up to about 100 cm at 16-day boundaries | Depth is continuous, chained from the last melt |
| 4 | Medium | Saving took 186 ms on the caller thread. Chunks outside the region were stored | Layer hashes are cached and encoding moved to the worker: hitch is now 3.4–3.8 ms. Off-region chunks are not recorded |
| 5 | Medium–low | Hashes missed several saved fields (beds, vacancy, cultures, waterway tiles, notable details); landmarks were unhashed | Fields added, landmark hash stored and verified on load, golden hashes pinned |
| 6 | Low–medium | Landmark choice used `Math.Log` (floating point in generation) | Integer weighted selection |
| 7 | Low–medium | Road lengths left out the stretch inside towns, about 0.3–0.4 km per settlement | Lengths include it, and a test checks road ≥ straight-line distance |
| 8 | Low | A signed cast skewed prop offsets: 32% fewer trees along every chunk's south edge | Cast fixed; a test checks density is even |
| 9 | Medium–low | Crossing a chunk rebuilt the whole live window (worst tick 28 ms at 8×) | Recentring with hysteresis and window preparation off-thread: average 0.25 ms, p99 about 2 ms, worst about 8 ms |
| 10 | Low | Torch flicker allocated about 180 MB per 400 steps | Buffers reused: 78 KB |
| 11 | Low | About 400 people aged 12–13 held adult jobs (reeve, smith, merchant); one mother was 54 | Adult jobs from 16; history fertility capped at 45 |
| 12 | Low | The weather cache could tear if read from two threads | Immutable reference |
| 13 | Low | Tools silently used code defaults when content wasn't found | Clear error |
| 14 | Low | A save header with missing keys failed as "corrupt"; MessagePack warned about init properties | Header DTOs fixed; missing mods read as empty; warnings gone. The feared data loss was disproved by probe |

## Game, tools and CI (15 findings)
| # | Severity | Defect | Fix |
|---|---|---|---|
| 1 | High | Click-to-move did nothing in generated regions (world versus window coordinates) | Converted to window cells; smoke clicks and checks the hero walks |
| 2 | High | Esc silently threw away the run | Pause menu with Resume, Save, Settings and Quit (Quit asks to save; Ironman saves on quit) |
| 3 | High | Frame spikes: chunk drawing all at once (69 ms), level switch 114–128 ms | Drawing time-sliced at 3 ms per frame, nearest first; incremental level switches; streaming on a budget. Headless steady-state frames are under 16 ms |
| 4 | Medium | The blueprint editor could write invalid files into content and break the next start | Validate first, atomic write, test-load, roll back on failure |
| 5 | Medium | Blueprint "hot reload" reached nothing | The next new game or street view loads the change; a running one shows a toast |
| 6 | Medium | Cancelling region build still created the run; a failed first save was only a log line | Cancellation token, visible error, cleanup |
| 7 | Medium | The Codex and the explored map were owned by the view, against §2.1a | Moved into the sim (see Simulation #1) |
| 8 | Medium–low | Going back and pressing Start again duplicated the world in the library | The flow reuses the created world |
| 9 | Low–medium | A failed far-zoom bake became a permanent blank chunk | Retries with backoff |
| 10 | Low–medium | Worker threads called Godot APIs | Inputs read on the main thread first |
| 11 | Low | Codex rule pages went stale after rebinds and showed dev keys | Keys resolved at display time; dev contexts hidden in release |
| 12 | Low | Per-frame allocations in the minimap, rig, far layer and world map | Cached |
| 13 | Low | World delete and library errors were unguarded | Errors shown, state reset |
| 14 | Low | 960×540 layout overlaps in Quick Start, Embark and the map | Fixed; the advanced panel still scrolls at that size |
| 15 | Low | `res://` read through file paths (breaks exported builds); CI compare and `same` passed on empty input; selftest exit code only | Godot file APIs with one shared atlas cache; CI fails on missing or empty hashes and zero files; selftest output is checked |

## Known limits, carried forward
- **Flaky determinism tests: fixed.** The chunk-from-worker test and the live save re-save test failed rarely in full runs. The cause was MessagePack 3.1.12's LZ4 encoder: compressing on two threads at once produced valid but different bytes. Chunk generation itself was deterministic. `SaveCodec.SerializeCompressed` now compresses under a lock, and generation stays parallel. A stress test (`ChunkSnapshotDeterminismTests`, about 7.5 s) failed 5 of 6 runs before the fix and 0 of 6 after.
- **Saves: fixed in seamless part C.** F9 quickload loads the run's manual slot (not in Ironman). Autosave and Ironman slots are per run (`<run>.autosave_N.sav`, `<run>.ironman.sav`), so two runs in one world no longer share them; the older shared names still load.
- **Chunk hand-over (Phase 2):** decoding a big town chunk into the live window still costs a few milliseconds on the tick. The worst seen at 8× was about 8 ms, with an occasional 20 ms outlier. It will be revisited with seamless region streaming.
- **Frame spike (Phase 2):** one 45–59 ms frame spike at a single chunk draw appeared in several runs and then stopped reproducing; the cause isn't pinned. The frame-time capture on the reference PC will show whether it is real.
- **Snow on permanent snowfields:** depth steps once a year where snow never melts.
- **Settlements:** parallel town streets are still straight, and villages have a single street.
- **Legends viewer:** cause links haven't been exercised by a test or screenshot.
- **Golden hashes:** any intended change to world generation must re-pin them. Run `dotnet run --project tools/Ravensguard.WorldTool -c Release -- hashes --seeds 1,1234` and paste the lines into `tests/Ravensguard.Sim.Tests/Determinism/GoldenHashes.cs`.

## Still needs the user
- Play the game and give the fun check (`docs/playtests/`).
- Confirm 60 fps at street zoom on the reference PC.
- Approve the art style sheet v2.
