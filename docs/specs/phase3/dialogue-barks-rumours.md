# Dialogue templates, barks, authored dialogue and rumours v1

Plan refs: §3 item 4 (systemic templates, about 300 base lines plus barks, parameterised by job, mood, relationship and rumours; authored
dialogue for unique NPCs, about 20-50 lines each; rumours drawn from the chronicle and spread person to person, so "you overheard it" is a
real mechanism), §2.1a (condition and effect vocabulary, `add_rumour`), §2.7 (`tr()` and localisable templates), §3 item 12 (chronicle
causes), §5 Phase 3 done-when ("a rumour traces back to its source"), Phase 2 `abstract-tick-and-lodbridge.md` 2.5 (`RegionOutbox`
"rumour seeds") and `continent-tick.md` (aggregate news).

## 1. Purpose
Give every one of the ~25,000 people something true to say: what they do, how they feel, how they regard you, and what they have heard.
Keep the lines as data, so they can be translated and moded, and keep rumours as a real, traceable flow of information that starts at an
event in the chronicle and ends in a Journal entry that says who told whom.

## 2. Dependencies
| Needs | From |
|---|---|
| `Chronicle`, `ChronicleEntry`, `CauseKind` (exists, `Core/Chronicle.cs`) | Phase 1 / 2 |
| `RegionOutbox` rumour seeds, continent news | Phase 2 (outbox specified; seed type is new here) |
| `ContentVocabulary` (conditions `knows_person`, `season_is`, `has_reputation_at_least`, ... and effects `add_rumour`, `set_flag`, `start_contract`, `give_item`) | exists (`BaseVocabulary`); extended below |
| `NeedState`, `Mood`, `Action`, `Occupation` | `live-behaviour-and-utility-ai.md` |
| `KinGraph`, `PersonTable` names and bynames | exists |
| Localisation `L.cs`, `en.csv` | exists (`src/Ravensguard.Game/localization`) |
| Social action: people gather at tavern, square, well, church, market | live-behaviour 4.2 (`Socialise`, `Worship`) |
New: `Ravensguard.Sim.Talk` (`LineBook`, `LineSelector`, `RelationTable`, `BarkSystem`, `DialogueRunner`) and `Ravensguard.Sim.Rumours`
(`RumourTable`, `RumourSpreader`, `RumourTrace`); content `talk/lines.jsonc`, `talk/barks.jsonc`, `talk/unique/*.jsonc`.

## 3. Data

### 3.1 Systemic line (content `talk/lines.jsonc`)
```jsonc
{ "type": "line", "id": "greet.worker.tired", "topic": "greeting", "weight_pm": 1000,
  "when": [ { "id": "speaker_job_class", "args": { "class": "outdoor" } }, { "id": "mood_below", "args": { "value": "-20" } },
            { "id": "time_of_day_is", "args": { "part": "evening" } } ],
  "key": "D_GREET_WORKER_TIRED",          // en.csv: "Long day in the {field_or_yard}, {title}. Mind the mud."
  "params": [ "title", "field_or_yard" ],
  "effects": [] }
```
- `topic` (enum): `greeting, farewell, smalltalk, work_offer, trade, hire, recruit, directions, rumour, refusal, need, hostile`.
- `weight_pm` base weight, permille. `when` is a list of typed conditions, all must hold; each matched condition adds `specificity` 1.
- **Params** are resolved by `ParamResolver` from named providers and substituted into the localised string: `{name}`, `{title}` (by class,
  sex and relationship: "friend", "master", "sir", "goodwife"), `{job}`, `{place}`, `{season}`, `{weather}`, `{day_name}`, `{feast}`,
  `{rumour}`, `{price}`, `{person2}`. Grammar tokens: `{he|she}` by the referent's sex, `{n|one|other}` plural by count. A line never
  concatenates translated fragments: each key is one whole sentence, so every language can reorder.
- **New condition ids** (typed, registered in `BaseVocabulary`, validated at load; all other conditions already exist): `speaker_job_is`,
  `speaker_job_class`, `mood_below`, `mood_above`, `relationship_at_least` (`trust`, `familiarity`), `is_kin`, `time_of_day_is` (`dawn, morning, noon,
  afternoon, evening, night`), `weather_is`, `knows_rumour_kind`, `hero_is_armed`, `hero_is_bloody`, `hero_wealth_visible`, `speaker_has_need`
  (`hungry, tired, cold, sick, drunk`), `feast_today`.
- **No scripting:** lines carry no logic beyond `when` and the four existing effects.

### 3.2 `RelationTable` (sparse; hero-centred, NPC pairs created lazily)
| Field | Type | Unit | Meaning |
|---|---|---|---|
| `A`, `B` | person index | | ascending pair key |
| `TrustPm` | `short` | -1000..+1000 permille | grows on fair trade, help, shared meals; falls on theft, threats, attacks |
| `Familiarity` | `byte` | 0..100 | each conversation +2 (max +6 per day), decays -1 per 15 game days |
| `LastMetDay` | `int` | game day | |
| `Flags` | byte | | `Kin, Employer, Debtor, Rival, Lover` (Phase 6 extends) |
Kin links give a baseline `Familiarity 70, Trust 400` without a row. Cap 20,000 rows per world (oldest low-familiarity rows evicted);
about 14 bytes each. **Relationship tiers** for conditions: `stranger` (familiarity < 10), `acquaintance` (10-39), `friend` (>= 40 and trust >= 300), `trusted`
(>= 70 and trust >= 600), `disliked` (trust < -200), `hated` (trust < -600).

### 3.3 Barks (`talk/barks.jsonc`)
Same shape as a line with `topic: "bark"`, plus `radius_m`, `cooldown_s` (real seconds), `duration_s`, `channel` (`ambient, market, guard, alarm, combat, animal`).

### 3.4 Authored dialogue for unique NPCs (`talk/unique/<npc>.jsonc`)
```jsonc
{ "type": "dialogue", "id": "caravan_master.intro", "speaker": "caravan_master",      // a unique NPC id (landmark generation)
  "start": "n1",
  "nodes": {
    "n1": { "text": "D_CM_INTRO", "choices": [
        { "text": "D_CM_ASK_WORK", "goto": "n2", "when": [ { "id": "has_proficiency", "args": { "skill": "riding", "level": "0" } } ] },
        { "text": "D_CM_ASK_WHO",  "goto": "n3" },
        { "text": "D_CM_LEAVE",    "goto": "end" } ] },
    "n2": { "text": "D_CM_FIRST_JOB", "effects": [ { "id": "start_contract", "args": { "kind": "escort", "tier": "first" } },
                                                  { "id": "set_flag", "args": { "flag": "cm_gave_first_job" } } ], "goto": "end" },
    "n3": { "text": "D_CM_LORE", "effects": [ { "id": "add_rumour", "args": { "kind": "road_bandits", "place": "nearest_forest" } } ], "goto": "n1" } },
  "once_flags": [ "cm_gave_first_job" ] }
```
- Format: a node has `text` (an en.csv key), optional `when` (visibility), `effects`, `goto`, and up to 6 `choices`; `end` closes. Each unique NPC has
  20-50 `text` keys, a `repeat` node when the player returns, and fallback to systemic lines for anything not covered ("small talk" still works).
- Loader checks: every key exists in `en.csv`, every `goto` resolves, no unreachable node, every condition and effect is in the vocabulary, no cycle without a
  `Leave` choice. Phase 3 authors: 4 mentors (`caravan_master`, `old_knight`, `reeve_mentor`, `master_smith`; `onboarding-v1.md`) and about 8 other unique NPCs
  placed by landmark generation, 20-50 lines each (about 400 lines in total).

### 3.5 Rumour records
```
Rumour        { Id (EntityId, IdKind.Event reuse of the chronicle id), SourceEntry (chronicle EntityId), Kind (RumourKind), Place (settlement/tile),
                SubjectPersons[0..2], Severity (0..3), BornDay, ExpiresDay, FactsMask (which details are true: who, where, when, how many) }
Hearing       { RumourId, Listener (person), Teller (person index or -1 = "a traveller"), Day, Hop (0 = witness, 1.. = retellings),
                Distortion (byte flags: Vague, Exaggerated, MisattributedPlace) }
```
- `RumourKind`: `Fire, Raid, Battle, Crime, Death, Birth_Wedding (notables), Famine, Disease, Flood_Weather, Tax_Levy, War, Road_Bandits, Omen, Landmark`.
- Mapping from `CauseKind` and chronicle `TypeId`: Fire, Raid, Battle, Crime (witnessed), Death (notable or violent), Famine, Disease, Weather (flood, ford closed), Tax
  (levy changes), War. Each chronicle type has `rumour_interest_pm` and `rumour_life_days` in `talk/rumours.jsonc` (for example Fire 800 / 12 days, Famine 900 / 25 days, Crime 600 / 8 days, ordinary death 100 / 3 days).
- **Storage:** `RumourTable` (≤ 6,000 live rumours per region), `HearingTable` (≤ 120,000 per world; a person holds at most 6 hearings, lowest-interest evicted; most people hold 0 to 2); about 16 bytes per hearing = under 2 MB.

## 4. Rules

### 4.1 Choosing a line (`LineSelector`)
1. Candidate set = lines of the requested `topic` whose `when` all hold for `(speaker, listener, context)`.
2. `score = weight_pm x (1 + specificity)` (integer); lines used by this speaker in the last 8 turns, or this exact `(speaker, listener)` pair in the last 24 turns, get weight 0.
3. Pick by weighted draw, DetRng stream 0x310 keyed `(tickKey = `GameSec / 60`, i.e. the game minute, speakerId, draw = listenerIdx x 16 + turn mod 16)`, so the same moment gives the same line on replay.
4. If no candidate exists the topic falls back to `smalltalk` generic lines (never silence). A content test guarantees every `(topic, job class, mood band, relationship tier)` cell has at least 1 line.

### 4.2 Line catalogue (about 300 base lines; barks counted separately)
| Group | Lines | Parameterised by | Examples (en, with params) |
|---|---|---|---|
| Greetings | 34 | time of day (6 parts), relationship tier (5), mood (3), job class, weather | "Morning, {title}." / "Back again, {name}? Mind the dog." / "{Name}! Sit, sit; the ale is fresh." / "What do you want?" (disliked) |
| Farewells | 14 | relationship, time, mood | "Fair road to you." / "Don't be a stranger, {name}." / "Go on, then." |
| Small talk: weather | 22 | `weather_is` (10 states), season | "Rain again. The ford will be up by noon." / "Frost on the fields; the winter wheat will be glad." |
| Small talk: season and feast | 12 | season, `feast_today`, day to next feast | "Harvest feast in {n} {days|day|days}; the brewer's been at it since dawn." |
| Small talk: work (by job) | 36 | `speaker_job_is` for 18 occupations x 2 lines | Smith: "A good edge starts with good iron, and iron has been dear." / Farmer: "The barley wants rain, and so do I." |
| Small talk: local life, health, family | 20 | kin, age, `speaker_has_need` | "My eldest is courting the miller's girl." / "My back is not what it was." |
| Work offers | 20 | job, urgency, `Job.Kind`, pay | "I need {goods} carried to {place} by {day_name}. {price} for it. Interested?" / "Nothing today, but ask the reeve." |
| Trade | 32 | shop type, stock, price, hero standing | "{price} the {unit}. That is what it fetches at {place}, as I hear." / "I have none to spare." / "Come back after harvest." |
| Hire and recruit | 28 | recruit willingness, pay, danger, relationship | "{price} a day, and food? I'll carry your pack." / "I have a family. No." / "For you, {name}? Aye." |
| Directions and places | 14 | settlement, distance, road | "Take the north road past the mill; {place} is about {n} {hours|hour|hours} on foot." |
| Rumour wrappers | 32 | `RumourKind`, hop, teller, confidence | see 4.6 |
| Refusals and hostile | 18 | armed, bloody, night, law, hated | "Put that blade away." / "Not at this hour." / "I don't talk to your sort." |
| Needs and mood | 18 | hungry, tired, cold, sick, drunk | "Spare a heel of bread?" (hungry, poor) / "I'd talk, but my feet ache." |
| **Total** | **300** | | |
- **Barks** (about 110 lines, `talk/barks.jsonc`): ambient work by job (36), market cries (16: "Fresh bread, hot from the oven!"), weather (14), night guard (8: "Halt. Who goes there?"), alarm and fire (8), animals and dogs (6), reaction to the hero (12: armed, bloody, rich, ragged), combat hooks (10, used from Phase 4).
- Typical line length 40-110 characters; hard cap 160. Target total text of the base set about 20 KB in en.csv.

### 4.3 Conversation flow (hero talking)
- **E** on a person (default action): if they have a unique dialogue, start it; otherwise the **topic menu**: *Greet, Ask about work, Ask the news (rumours), Ask about a person or place (Codex link), Trade (only shopkeepers or anyone holding tradable goods), Hire, Recruit, Leave*. Entries are shown only if the relevant lines or abilities exist (a child has no work offers).
- A conversation turn costs 6-20 game seconds (about 0.3-1 real seconds at 1x) and `+2 Familiarity` once per day per partner (max 6/day). It sets `SocialPm -150`.
- A **disliked** person may refuse the whole menu except Leave; a **hated** one calls a guard (Phase 6).
- **Mood** (-100..+100) is computed daily by `MoodSystem`: `+20` if fed above 70% reserve, `+10` housed, `-30` hungry, `-15` cold, `-10` crowded, `+/-10` job satisfaction, `-20` recent bereavement (kin death within 15 days), clamped. It selects the mood band (low < -20, mid, high > +20) in `when`.

### 4.4 Barks (`BarkSystem`, live tier only)
- A person within `radius_m` of the hero (default 8 m) may bark when a bark's `when` holds and its `cooldown_s` has elapsed. Per-person cooldown 60 real seconds; global cap **2 barks per real second, 3 visible at once**; closer people first, ties by person index.
- A bark is a floating text above the head for `duration_s` (2.5-4 s) and a caption in the log (filterable "Nearby talk"); no voice in Phase 3.
- Barks use stream 0x311, key `(GameSec / 20, personId)` (a game-time window, not the live tick, so 1x and 8x give the same bark); they never change records (a pure view event) except **alarm** barks, which set `threat_pm` in listeners.

### 4.5 Rumours: creation
- `RegionOutbox` already carries chronicle appends; Phase 3 adds `RumourSeed(entry, kind, place, witnesses[])` created in H7 (outbox) for every entry with `rumour_interest_pm > 0` and a place in a populated settlement. Witnesses are people the abstract tick marks as present (fire: the household and 4 neighbours; battle: survivors; crime: recorded witnesses; famine/weather: the reeve and 3 elders).
- **Seed:** each witness gets a `Hearing { Teller = -1, Hop = 0 }` (they saw it) at `BornDay`.
- Events from other regions arrive in the continent tick's news (Phase 2): `RumourSeed` with `Teller = traveller`, `Hop = 2`, `Place` = origin, delayed by travel time (`news_lag_days = distance_km / 40` game days: a merchant day of 40 km), delivered at border settlements and market towns first.
- The hero's own deeds (a witnessed brawl, a gift) create chronicle entries and therefore rumours with the hero as `Subject`; Phase 6 reputation reads them.

### 4.6 Rumours: spreading (person to person)
`RumourSpreader` runs in the **abstract tick** (H4 slice for the socialising hour, so live and abstract agree) and the **live tier** for people in the bubble (a spoken exchange is the same function).
- **Gatherings:** people whose `Action` is `Socialise` or `Worship` or who shop at the market share a **group** per place (tavern table, well, square cluster, church, market), size 2-6, formed in ascending person index with stream 0x315. Abstract people form groups per settlement per hour with the same function.
- **Per group per hour:** for each person (ascending index) as teller, pick the held hearing with highest `interest x freshness` (freshness = 1000 - 1000 x age_days / life_days); each other member who lacks it hears it with probability `p = 600 pm x familiarity_factor x freshness / 1000`, where `familiarity_factor` = 1.0 stranger, 1.2 friend; stream 0x313 key `(hour, tellerId, draw = listenerIdx)`. One rumour per teller per hour.
- **Hop and distortion:** the listener's hearing has `Hop = teller.Hop + 1`. At each hop, with 5% (stream 0x314) one detail degrades (`Vague`: the place becomes "somewhere in the east"; or `Exaggerated`: a number x 1.25, rounded up; or `MisattributedPlace` to a neighbouring settlement in 1% of hops). Degradations are **flags**, never new facts; the original `Rumour.FactsMask` keeps the truth, so the Codex can show "partly true".
- **Reach:** a person tells only rumours that are not older than `ExpiresDay`. Settlement-to-settlement spread rides on travellers: each day, each abstract caravan or traveller arriving at a settlement counts as a teller in that settlement's market group once.
- **Calibration target (G-R3):** a Fire (interest 800) seeded with 5 witnesses in a 1,000-person town reaches 50% of residents in 6-10 game days; in a 150-person village, 50% in 3-6 days.

### 4.7 Telling the hero, and the source trace
- **Ask the news:** the NPC tells the hearing with highest `interest x freshness` that the hero does not yet hold. Wrapper lines by hop: Hop 0 "I saw it with my own eyes: {rumour}." / Hop 1 "{teller_name} told me: {rumour}." / Hop >= 2 "They say {rumour}." / Traveller "A carter from {place} said {rumour}." Distortion adds a hedge ("though I hear more than that").
- **Overhearing:** when the hero is within 6 m of a group in which a hearing is passed (live tier), the hero gets the hearing with `Teller = speaker`; the Journal entry records "overheard".
- **Journal (J) > Rumours** lists each held rumour as a card: text, how many people it passed through, confidence label (`Seen first-hand`, `Reliable` (hop <= 1, no flags), `Hearsay` (hop >= 2), `Garbled` (flags)). **"How do you know?"** expands the **trace**: the chain of `Teller` links back to hop 0, then the source chronicle entry; for example: *Overheard Edith telling Bram at the Boar (day 14) <- Edith heard it from Osric (day 12) <- Osric saw the fire (day 9) <- Chronicle: Fire at Mill Lane, cause: lamp left in a barn.* The chronicle link opens the History viewer (H).
- Trace invariants checked in tests: `Hop` strictly decreases along `Teller` links, each link's teller held the hearing on an earlier day, the chain ends at a `Hop = 0` witness whose person is on the seed's witness list, and the seed's `SourceEntry` exists. A rumour with no valid chain is a test failure (done-when).
- **Rumour effects (Phase 3):** hearings feed `{rumour}` in lines, the Codex ("heard" tag), the ambition hint system ("hints name where people find such work"), and hero pins to the map. They do not change prices or unrest yet (Phase 6 does).

### 4.8 Localisation
All text lives in `localization/<lang>.csv` keyed by `D_*`, `B_*`, `R_*`. A CI check requires: every `key` in content exists in `en.csv`; every `{param}` used in a string is declared in the line's `params`; no key unused (warning); pseudo-locale test (`+` padding of 40%) must fit UI boxes (text wraps; no truncation). Proper nouns (place, person names) are never translated; titles and day names come from the culture's `CalendarNames` and title table.

### 4.9 Tick placement
| Where | What |
|---|---|
| Live 20 Hz | `BarkSystem` (staggered, <= 0.05 ms), dialogue UI (no sim cost), overhearing detection at 2 Hz |
| Barrier | `Tell`, `Ask`, `AddRumour` commands from the hero or dialogue effects applied |
| H4 slice | rumour spreading for the settlement's gatherings in that hour; mood daily |
| H7 outbox | create `RumourSeed` and initial hearings |
| H5 hour 0 | expire rumours and prune hearings (cap) |
LODBridge: a person's hearings are table rows, so promote/demote loses nothing; a conversation in progress pauses the NPC's `AbstractJob` (held) and resumes it.

## 5. Determinism
| Stream | Use | Key |
|---|---|---|
| 0x310 | systemic line pick | `GameSec / 60`, speaker, draw = listener x 16 + turn |
| 0x311 | bark pick and cooldown jitter | `GameSec / 20`, person |
| 0x312 | seed witness selection | chronicle entry |
| 0x313 | hearing probability | game hour, teller, draw = listener |
| 0x314 | distortion roll | listener |
| 0x315 | gathering partition | place |
Hash of `(RumourTable, HearingTable, RelationTable)` joins the daily `EconomyHash` family as `TalkHash`; iteration is ascending ids; no wall-clock.

## 6. UI and controls
- **Dialogue panel:** portrait slot (name, job, mood icon, relationship tier), the NPC's line in a text box, topic list or choices numbered 1-9 (keys 1-9 and mouse), **Esc** or the x leaves. Choices show a tooltip with condition text when locked ("needs Riding 10").
- **Barks** float above heads; "Nearby talk" filter in the message log. **Journal (J)** has the Rumours tab with "How do you know?" and map pin button.
- **Text speed** setting (instant, fast, normal); line history scrolls (mouse wheel).
- Controls overlay `?` shows the dialogue context keys: 1-9 choose, E advance, Esc leave, H open history link.

## 7. Tests (golden)
| id | Case | Expected |
|---|---|---|
| D-1 | Content: all lines load, every key exists in `en.csv`, params declared, every `(topic, job class, mood band, tier)` cell has a candidate | 0 missing; line count 300 (+/-10) |
| D-2 | Selection: same `(seed, minute, speaker, listener)` | same line id on 100 runs; changing the minute changes it in at least 20% of cases |
| D-3 | Repeat: speaker with 12 candidates, 24 turns | no line repeats within 8 turns; no pair-line repeats within 24 |
| D-4 | Specificity: lines A (1 cond, 1000 pm) and B (3 conds all true, 1000 pm) | B drawn 2 times in 3 (weights 4000 vs 2000) over 6,000 seeded draws, 66.7% +/- 2% |
| D-5 | Authored dialogue validation: unreachable node, missing key, unknown condition | each rejected with a readable error naming file and node |
| D-6 | Pseudo-locale | no string overflows its box at +40% length |
| R-1 | Seed a Fire with 5 witnesses; 30 days of abstract tick in a 1,000-town | every hearing's trace resolves to the entry; `Hop` strictly decreasing; 0 broken chains over 5,000 hearings |
| R-2 | The done-when check: hero asks the news in 10 towns over 10 days | 100% of Journal rumours expand to a full chain ending at a witness |
| R-3 | Spread calibration (section 4.6) | town: 50% reached in 6-10 days; village: 3-6 days (seeded, 20 runs, median) |
| R-4 | Distortion: 10,000 hops | `Vague+Exaggerated+Misattributed` about 5% (+/- 0.5%); misattribution about 1% of hops |
| R-5 | Caps | hearings <= 120,000; per-person <= 6; table hash identical across save/load |
| R-6 | Replay | a recorded 30-day run with 10 hero asks gives identical `TalkHash` per day on two machines |

## 8. Open questions for the user
1. **Rumour truth:** v1 only degrades facts (vague, exaggerated), never invents lies. Should NPCs ever tell deliberate false rumours? Default: no in Phase 3; Phase 6 adds slander with the reputation system.
2. **Voice:** barks are text only. Default text only until Phase 8 (audio is CC0 or commissioned).
3. **Text volume:** 300 base lines are thin after 20 hours. Default: ship 300 + 110 barks, with a content-expansion pass every phase (plan scales content separately from systems).

## 9. Budgets
| Item | Budget |
|---|---|
| Line selection | <= 20 microseconds per call; 0 allocations (pooled params) |
| Barks | <= 0.05 ms per live tick |
| Rumour spreading, abstract | <= 1.0 ms per game hour per region (about 50 gatherings x 4 people) |
| Rumour spreading, live | <= 0.1 ms per tick |
| Memory | `RumourTable` <= 150 KB, `HearingTable` <= 2 MB, `RelationTable` <= 0.3 MB, en.csv Phase 3 additions <= 200 KB |
| Save size | `talk` section <= 2.5 MB |
