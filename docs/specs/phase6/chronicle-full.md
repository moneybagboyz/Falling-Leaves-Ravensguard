# The chronicle at full depth: text as keys and parameters, specific causes measured, the effects index, and the Chronicle (H) screen

Plan refs: §3.12 (cause chains exist for concrete event types: fires, raids, battles, witnessed crimes, wounds, deaths, laws, famines, mine floods, wars; price changes without a concrete cause are labelled "market drift" with the supply and demand trend; every entry has either a specific cause or that explicit label; coverage is measured), §5 Phase 6 ("the chronicle at full depth, with measured why coverage"; done-when: **chronicle cause coverage is >= 90% specific**), §2.9 (`tr()` strings; localisation of generated chronicle text is Phase 8). Carried here: Phase 4 S7 (English made in the sim: the ambush's notes and the death descriptions are sentences in chronicle data), Phase 4 open (an in-game Chronicle (H) screen), Phase 3 (the History (H) link, the legends fallback reads the chronicle).

## 1. Purpose
Every system in this phase writes the chronicle; this spec makes what it writes uniform, measurable and readable. Entries carry a text key and typed parameters instead of English; causes are typed and measured for specificity; each entry knows what it caused as well as what caused it; the player reads it all in a Chronicle screen that follows "why" back and "what followed" forward, from his own life to the realm's wars.

## 2. Dependencies
| Needs | From |
|---|---|
| `Chronicle`, `ChronicleEntry` (`TypeId`, `Subjects`, `Causes`, `Data`), `Cause` (`CauseKind`, `EventId`, `Note`), `CauseCoverage()` | Phase 0 |
| The legends viewer (Menu context: `follow_cause` F, `follow_effect` D, `search` /, `filter_type` T, `filter_realm` E, `previous` Backspace, `open_here` O) | Phase 1 |
| Every writer of entries (history, economy, taxes, wounds, aftermath, caravans, land, construction, crafting, inheritance, and this phase's systems) | all |
| Rumour seeding from entries | Phase 3 |
New: `Sim/Core/ChronicleText` (key and parameter schema), `Sim/Core/EffectIndex`, content `chronicle/types.jsonc` (every type id with its key, parameters, cause kind and rumour interest), strings in `en.csv`. Extended: `Cause` (`NoteKey` + parameters), `CauseKind` (appended), `ChronicleEntry` (`TextKey`, `Params`), `Chronicle.SpecificCoverage()`. Godot: `UI/ChronicleScreen` (the in-game Chronicle on H, sharing the legends viewer's code).

## 3. Data
- **`chronicle/types.jsonc`**: one row per entry type id (about 220 after Phase 6: about 120 existing, about 100 new): `{ id, text_key, params: [{ name, type: person | place | item | good | mites | count | days | entry | text_key }], cause_kinds (allowed), rumour (Phase 3 row reference), specific_requires: "event" | "note" | "either" }`. A content validator checks that every type a system can write has a row and every key is in `en.csv` (CI).
- **`ChronicleEntry`** gains `TextKey` (string id, interned) and `Params` (typed values; ids, not names); `Data` stays for machine fields. **`Cause`** gains `NoteKey` and `NoteParams`; the English `Note` stays readable for v8 saves (legacy, shown as is).
- **`CauseKind` appended:** `Justice, Faith, Slavery, Family, Unrest, Diplomacy, Office, Illness` (Phase 6). The old `Other` is no longer written by any Phase 6 code; legacy writers move to a specific kind (4.2).
- **`EffectIndex`** (derived at load, never saved): entry id -> entries whose causes name it.

## 4. Rules

### 4.1 No English in the sim
Every writer passes a key and parameters; the view formats with `tr()` and the parameters' current names (a person renamed by marriage shows the new name with "born X" in the tooltip). The Phase 4 S7 sites move first (the ambush's place and cause notes, `WoundSystem` death descriptions), then the history's v1 path (`history-recalibration.md`: v0 stays English and frozen), land, construction and the rest. A source test fails on any string literal with a space passed as a chronicle note or data value outside `Ravensguard.Game` (the existing Phase 3 rule, finding 14, now enforced over the whole sim).

### 4.2 Specific causes, measured
`SpecificCoverage()` over a set of entries = entries with at least one **specific** cause / entries, where a cause is specific when its kind is not `Unknown`, `Other` or `MarketDrift` and it has an `EventId` that resolves to an entry (or, for kinds whose row says `note`, a `NoteKey` from the type's allowed list naming a concrete thing: a person, a place, a weather row). Entries that are themselves **market drift** labels (price moves with no concrete cause, plan §3.12) are counted in a separate figure and excluded from the denominator, as the plan permits ("either a specific cause or that explicit label"). **Gates:** >= 0.90 over the 10-year soak with the hero bot (OV-5), >= 0.90 over each history (`history-recalibration.md`), >= 0.95 over each Phase 6 system's own entries (its golden suite), and every entry has a cause (specific or the label): 100%. The current `CauseCoverage()` (any cause but drift and unknown) stays for the Phase 2-5 tests.

### 4.3 Chains
A cause's `EventId` must resolve to an earlier entry (a test walks every chain in a soak to its root and checks time order). Roots are allowed only for kinds that start things: weather, a person's own decision (the actor and the utility's top consideration as `NoteKey`), the history's seeding, and a dice draw that the type marks as `chance` (an accident: a hearth fire's 0x6C3 draw names the hearth and the thatch as specifics). "Because the dice said so" without a concrete thing is not specific.

### 4.4 The Chronicle (H) screen
H opens it in the World (bound since Phase 0, unused until now). Views: **My life** (the hero and his kin as subjects), **Here** (the current settlement and fief), **Realm**, **Legends** (the history, as the legends viewer). Each entry: the formatted text, the day, subjects as links (a person opens his character card if known), place (the map pin), **Why** (the cause chain, F follows one step back) and **What followed** (the effect index, D follows forward), Backspace returns, O opens the place on the map. Filters by type family (crime, war, faith, family, economy, disaster) and search. The rumour card's History link and the Journal open it at an entry.

### 4.5 Volume and pruning
About 2,000 entries a game year in a detailed region after Phase 6 (crime about 400, contracts 300, politics 100, family 150, economy and taxes 500, faith 100, wounds and deaths 400). Kept in full for the hero's region; old commoner entries with no subject the hero knows and no effects older than 10 game years are compacted into a yearly summary entry (the effect chain of anything kept stays whole).

### 4.6 Tick placement
Entries are written by their systems where they act (no new tick step). The effect index updates on append; the coverage measure runs in tests and in the soak tool's report.

## 5. Determinism
No draws (stream **0x6A0** reserved for template variants should a type want one; asserted unused). Entry ids are allocated in the writer's deterministic order as today. `ChronicleHash` covers type, key, params and causes (not the formatted text).

## 6. UI (960 x 540)
Two panes (list 55%, the entry and its chains 45%) with a filter bar; long chains scroll; the screen shares the legends viewer's widgets and keys (Menu context, so no World key is added). Every view readable at 960 x 540 (smoke).

## 7. Tests (golden)
| id | Case | Expected |
|---|---|---|
| CH-1 | Types schema | every type id written by any system in a 10-year soak has a row; every key in `en.csv`; parameters typed |
| CH-2 | No English in the sim | the source test finds no sentence literal passed as a note or data value under `src/Ravensguard.Sim` (Phase 4 S7 closed) |
| CH-3 | Legacy notes | a v8 save's entries with English notes load and show their notes; new entries carry keys |
| CH-4 | **Done-when: >= 90% specific** | `SpecificCoverage()` over a 10-year soak with the hero bot (OV-5) >= 0.90; drift labels counted apart |
| CH-5 | Every entry has a cause | 100% of entries have a specific cause or the market drift label |
| CH-6 | Chains resolve in time order | every `EventId` in the soak resolves to an earlier entry; roots only of the allowed kinds |
| CH-7 | Effect index | CJ-1's theft entry lists `crime_witnessed`, `crime_reported`, `bounty_posted` as what followed |
| CH-8 | Per-system gate | each Phase 6 suite's entries >= 0.95 specific |
| CH-9 | Compaction | entries older than 10 years with no known subject and no kept effects compact into yearly summaries; no kept chain is broken |
| CH-10 | Screen | H opens My life; F and D follow causes and effects; 960 x 540 smoke |
| CH-11 | Determinism, save | `ChronicleHash` equal across runs; save -> load -> save byte-identical; the effect index rebuilt at load equals the one built live |

## 8. Open questions (recommended defaults)
1. **Compaction age:** 10 game years for commoners' entries with no known subject. Default as written.
2. **Library worlds' v0 history stays English** (it is frozen to regenerate identically); new worlds' history uses keys. Default as written.

## 9. Budgets
Append with key and params <= 2 us; the effect index O(causes) on append; Chronicle screen open <= 20 ms on 20,000 entries, scrolling <= 2 ms a frame; save growth about 60 B an entry (2,000 a year: 120 KB a year before compaction, <= 1.2 MB for a 10-year region).

## 10. Notes from P6-A2 (shared plumbing)
- **Built:** `ChronicleEntry.TextKey` / `Params` and `Cause.NoteKey` / `NoteParams` (typed `ChronParam`: person, place, item, good, mites, count, days, entry, text_key; ids, not names); additive save keys (entry 6, 7; cause 4, 5), no version bump, v8 sections decode with empty keys. `Chronicle.SpecificCoverage(types)` (a report: entries, specific, drift apart, no-cause), `Chronicle.Hash()` (type, key, params, causes; not the text), `Chronicle.Effects` (`EffectIndex`, kept on append so it is rebuilt at load), `ChronicleChains.Check` (CH-6), `ChronicleTypes` (`chronicle_type` rows in `content/base/chronicle/types.jsonc`, validator for CH-1). `RegionOutbox.Chronicle` has a keyed overload.
- **Rows:** 156, all `legacy: true` except `battle_report` (keyed). A writer that moves to keys drops `legacy`, lists its params and cause kinds, and adds its `ch_` keys to en.csv before the `fl_` block. Legacy rows are exempt from the root rule of CH-6; new types must be `root` or `chance` to start a chain.
- **Nested text keys:** a `text_key` parameter is a sentence filled from the same parameter bag (depth 3), so one note can say "{how} at the fight {where}".
- **Phase 4 S7 closed:** the ambush's cause and place, `WoundSystem`'s death descriptions and `BattleChronicle` (report and death chain) are keys and parameters. `Demography.Kill` takes an optional `Cause? chain`; `BattleReport`/`AbstractBattles` carry `PlaceKey` and `PlaceId`. CH-2 is pinned: the 83 sentence notes left in the sim (Phase 2-5 writers, history v0) may only fall (`LegacySentenceNotes` in `ChronicleFullTests`), and the five Phase 4 files must have none.
- **Seeding pass:** `Phase6Seeding` (`Worlds/Phase6Seeding.cs`) runs at `NewGameWorld.Create` and at the end of `WorldSave.Restore`; steps (`IPhase6SeedStep`) register in `DefaultSteps` (empty now), skip when their section is saved, and the load note appears only when a step changed something.
- **Hash trail:** `hashes` prints a `phase5_trail` line (`Diagnostics/Phase5Trail`: a new game, a floor built and knocked out, ten days through the runtime, then the Phase 5 sections, the chronicle and A3's `StandingBook.Hash()`); pinned in `GoldenHashes`. The live trail is unchanged (it holds live-tier state only, overview 8.9).
- **Streams:** `RngStreams.Physician` is 0x600 (seeding and `AbstractPhysic` share it, as the table says); every other 0x6xx of overview 5 is registered under a descriptive name.

