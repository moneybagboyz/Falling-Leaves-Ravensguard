# Audio: the engine, ambience, adaptive music, and the final sound content

Status: design, before any Phase 8 code, on top of f2530c3.
Plan refs: §2.11 (sound effects from CC0 or royalty-free libraries, positional in the world; ambience by weather and place; adaptive music layers: calm, tension, battle; placeholder audio from Phase 4, final audio and music in Phase 8; audio is CC0, royalty-free or commissioned; large binaries in Git LFS), §2.7 (captions for important sounds).
Decisions: D3 (how audio is sourced), D4 (which music).

## 1. Goals
1. Every sound the game names plays, placed in the world, with a caption where it matters.
2. A battle of 2,000 sounds like a battle without 2,000 voices.
3. Ambience follows place, weather and hour; music follows danger.
4. Every file has a licence on record.

## 2. What exists and what is new
| Today (f2530c3) | Phase 8 |
|---|---|
| `Game/Audio/AudioBus.cs`; `content/base/audio/events.jsonc` (22 fight rows with captions, no files) | **extended:** buses, voices, positional play, occlusion, beds, ambience and music; content `audio/ambience.jsonc`, `audio/music.jsonc`, the `file` field filled |
| no audio files | `assets/audio/` in Git LFS; `THIRD_PARTY_AUDIO.md` |

## 3. Rules
Audio lives in the Game only. It reads sim snapshots and never writes to the sim. It draws no sim randomness.

### 3.1 Buses and voices
- **Buses:** master, music, ambience, effects, interface. Each has a volume setting (0 to 100) and mute.
- **Voices:** at most **48** at once. A new sound takes a free voice, or the lowest-priority one if its priority is higher (the `priority` field of `events.jsonc`).
- **Variants:** a row may list 1 to 6 files; the variant comes from a Game-side random source that the sim never reads.

### 3.2 Positional sound
- Each world sound has a position in metres. Its volume falls as `1 / (1 + d / 4)` out to 40 m and is silent beyond; panning follows the screen x.
- **Occlusion:** -6 dB for each wall cell between the sound and the listener (up to 3), -12 dB for each level between.
- **The listener** is the camera centre at Close and Street zoom, and the hero at Field and Command zoom.

### 3.3 Battle beds
- Above **24** fighters in contact within 60 m, single blows below priority 14 stop playing. A **battle bed** plays instead: 3 loops (clash, shouts, horses) whose volumes follow the counts in contact, foot and horse.
- Priority sounds still play on top: deaths, bone breaks, horns, the hero's own blows.

### 3.4 Ambience
- `ambience.jsonc` maps place kind (town, village, road, forest, marsh, coast, deck, underground, battle camp) x weather (clear, rain, storm, snow, fog) x hour band (dawn, day, dusk, night) to 1 or 2 loops. A missing cell falls back to place x clear x day.
- Crossfade between ambiences in 3 s.

### 3.5 Adaptive music
- **States:** calm, tension, battle. Tension starts when a hostile is within 60 m or a warning shows; battle when the hero's side is in contact; back to tension after 20 s with no contact, and to calm after 60 s with no hostile near.
- Layers crossfade in **4 s**. Each state has 2 to 4 stems; a stem change waits for the bar.
- **Sets** by culture group, if D4 and the cut order allow; otherwise one set.
- Silence is allowed: calm plays a piece and then waits 60 to 180 s.

### 3.6 The content (AU-2)
- About 250 effects, 40 ambience loops, about 60 minutes of music as stems.
- **Format:** Ogg Vorbis at quality 6, 48 kHz. Music and ambience at -16 LUFS integrated; effects normalised by kind; true peak -1 dBTP or lower.
- **Licences:** each file's source, author and licence in `THIRD_PARTY_AUDIO.md`; only CC0, royalty-free or commissioned (with the commission's terms on file).
- **Memory:** effects loaded, music and ambience streamed; 300 MB or less in memory.

### 3.7 Integer math notes
None in the sim. The Game may use floats for gain; the rules above are given in dB and metres.

## 4. Saves
None. Audio settings are in `user://settings.cfg`.

## 5. Golden tests
| Id | Name | Asserts |
|---|---|---|
| AU-1 | Voices | 60 sounds at once play the 48 of highest priority |
| AU-2 | Fall-off | a sound at 0, 4, 40 and 41 m plays at 1, 1/2, 1/11 and 0 of its volume |
| AU-3 | Occlusion | two walls and one level between cost 24 dB |
| AU-4 | Battle bed | 30 men in contact stop single low-priority blows and start the bed; a death still plays |
| AU-5 | Music states | the scripted fight goes calm, tension, battle, tension, calm on the stated timings |
| AU-6 | Sim untouched | a run with audio on and off gives the same determinism hash |
| AU-7 | Every row | every `audio_event`, ambience and music row has a file or is marked silent (FL-11 extended) |
| AU-8 | Licences | every file in `assets/audio/` has a row in `THIRD_PARTY_AUDIO.md` with an allowed licence |
| AU-9 | Loudness | the check tool finds every music and ambience file within ±1 LU of -16 and every peak at -1 dBTP or lower |
| AU-10 | Memory | a 10-minute scripted run stays at 300 MB or less of audio memory |

## 6. Parts and gates
| Part | Scope | Gate | Months |
|---|---|---|---|
| P8-AU-1 the engine | buses, voices, positional play, occlusion, beds, ambience, music states (on placeholders) | AU-1 to AU-6 | 0.7 |
| P8-AU-2 the content | the files, loudness, licences, LFS, memory | AU-7 to AU-10 | 0.5 |

The audio brief and order go out by month 1 (D3, D4); stems are due by month 8.
