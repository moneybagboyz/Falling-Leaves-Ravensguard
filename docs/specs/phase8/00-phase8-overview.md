# Phase 8 specs: overview, carried items, build order, saves, streams, integration tests, cut order and decisions

Status: design, written before any Phase 8 code (plan §4: spec, then golden tests, then implementation), on top of f2530c3 (Phase 7 closed).
Source of truth for scheduling is `docs/REDESIGN_PLAN.md` §5. It gives Phase 8 ("Depth, onboarding, polish, 1.0") **8 to 12 months**.

The Phase 7 conventions still apply:
- integer or `Fixed` arithmetic in the sim; floats only in tools, the Godot side and diagnostics;
- counter-based `DetRng` streams keyed on game time, distance or ids;
- stable-id iteration;
- content as JSON through the `ModLoader`;
- UI as thin views over snapshots, with strings through `tr()` and no English made in the sim.

Units, as in Phase 7:
- money in **mites** (1 cp = 10 mites, 1 sp = 100, 1 gp = 1,000);
- mass in grams, lengths in mm or m as named, shares in permille (pm);
- a day is 86,400 game seconds, a season 15 days, a year 60 days.

**Plan sections covered:**
- §5 Phase 8: the ambition tree with autopilot, optional endings, landmarks and unique NPCs, the bestiary and site templates, the Codex and onboarding v2, audio, accessibility, localisation, controller and Steam Deck, the balance pass, modding for players, the Steam page and 1.0;
- §3f (learning the game), §3i (autopilot), §2.7 and §2.11 (settings, accessibility, audio, budgets), §6 (verification), §7 (the overrun rule);
- every item `docs/reviews/phase-7.md` moved to Phase 8, and the open items of Phases 1 to 6.

**What is decided already (not asked again):**
- endings are optional: a great ambition offers an epilogue, and the player may carry on (plan §5);
- the world stays in the library for new characters;
- no public builds before 1.0, PC first (Windows and Linux), no web build;
- audio must be CC0, royalty-free or commissioned (plan §2.11);
- C# script mods come after 1.0.

## 1. Document map

| File | Owns | Tests | Parts |
|---|---|---|---|
| `01-ambitions-and-autopilot.md` | the ambition tree (start, middle and great ambitions), branch choices, step progress, the means table, the `PursueAmbition` planner | AT-1 to AT-12 | AT-1, AT-2 |
| `02-endings-and-epilogue.md` | the ending offer, freezing a run, the epilogue from the chronicle, the hero passing to the world, the library entry | EN-1 to EN-10 | EN-1, EN-2 |
| `03-landmarks-npcs-and-bestiary.md` | about 40 landmarks, about 30 unique NPCs, 3 new monster kinds and their lairs, 8 site templates, cellars and walkable mines, torches and daylight | LM-1 to LM-14 | LM-1 to LM-3 |
| `04-codex-and-onboarding.md` | the full Codex as the manual, live numbers in entries, hints v2, the chained first hour, failure teaches v2, new controls pages | CX-1 to CX-12 | CX-1, CX-2 |
| `05-live-war.md` | the live siege assault, ladders and towers, the beach fight, ship captains that ram, grapple and board, the bell, wagon forts and chariots in battle, prisoners from battles, the small war gaps | LW-1 to LW-16 | LW-1 to LW-4 |
| `06-war-screens-and-arms.md` | the Army tab, the Armoury screen, the Fief screen, the helm HUD, arms orders as contracts with real goods, the steward's order, refit by commission | WS-1 to WS-10 | WS-1, WS-2 |
| `07-wars-toll-and-calibration.md` | AW-16's failing gates (intensity, AI purveyance, flight and return, borders, bread, recovery), AW-10, AW-C, AB3, kicks everywhere | WT-1 to WT-12 | WT-1 to WT-3 |
| `08-balance-pass.md` | EA_F25, EA_F18, WL-9, EA_F11, Phase 6's late-game leftovers, the origin and difficulty pass | BL-1 to BL-12 | BL-1, BL-2 |
| `09-performance-and-tooling.md` | the reference-PC pass, P7R-13, dropping dead person rows, replay gaps, `phase7_trail` and `phase8_trail` | PF-1 to PF-12 | PF-1 to PF-3 |
| `10-audio.md` | buses, positional sound, battle beds, occlusion, ambience, adaptive music, the audio content and its licences | AU-1 to AU-10 | AU-1, AU-2 |
| `11-accessibility-and-settings.md` | the settings screen, text size, colour-blind checks, motion toggles, hold or toggle, assists saved in the header, captions, the watch rows | AX-1 to AX-10 | AX-1 |
| `12-localisation.md` | the string audit and freeze, pseudo-locale, plural and gender forms, chronicle grammar, fonts, translation batches | LC-1 to LC-10 | LC-1 to LC-3 |
| `13-controller-and-steam-deck.md` | gamepad maps per context, focus navigation, glyphs, the virtual cursor, combat on a pad, the Deck profile | CT-1 to CT-10 | CT-1, CT-2 |
| `14-modding.md` | the modding guide, the example mod, schema export, the player blueprint editor through the loader, load-report causes, the water check, demolition | MD-1 to MD-10 | MD-1, MD-2 |
| `15-presentation-polish.md` | the art and feel items carried since Phase 3 | PP-1 to PP-10 | PP-1, PP-2 |
| `16-release.md` | builds, the platform layer, crash reports, the Steam page, the release checklist, 1.0 | RL-1 to RL-10 | RL-1, RL-2 |

There are **180 golden tests** plus the overview's integration tests OV-1 to OV-9 (section 5).

**Content volume:**
- 22 new ambitions (30 in all) with about 110 steps;
- 28 new landmarks (40), 22 new unique NPCs (30) with 20 to 50 lines each;
- 3 new monsters (5), 8 new site templates (26);
- about 120 Codex manual entries;
- about 250 sound effects, 40 ambience loops and about 60 minutes of music (section 10, D3 and D4).

## 2. Carried into Phase 8: where each lands

### 2.1 From `docs/reviews/phase-7.md`, "Moved to Phase 8"

| Carried item | Home |
|---|---|
| War's toll (AW-16): campaign intensity, AI purveyance when short, war flight and return, emptied borders, bread dearer on march routes, recovery of hard-hit towns; then the nightly re-run | P8-WT-1; the nightly in P8-G2 (OV-5) |
| AW-10, the count battle (31, 6 and 13 of 40), the per-pair cascade | P8-WT-2 |
| AW-C: scenario E, G's duration and wound mixes, H, I, AW-5 (hand and foot), AW-6 (plate duration) | P8-WT-2 |
| AB3's known-gap bound of 18 (the user, 26 Nov) | P8-WT-2 keeps it; it may only fall |
| The live siege assault (P7R-4), the ladder and tower orders | P8-LW-1 |
| AE1 convoys for siege goods, and felling timber | P8-LW-1 |
| The AI that rams, grapples and boards; the bell as a hero interrupt; a live beach fight | P8-LW-2 |
| The helm HUD, the Army tab | P8-WS-1 |
| Prisoners taken from battles (`PrisonerPens.Take` has no caller); the feed, sale, cede and parole-broken hooks | P8-LW-4 |
| `fire_arrow`; the sailor occupation; the anchor, ballast, bilge-pump and oar-port parts; harbours as generated blueprints; voyages, ferries and fishing in the running world; a raid ship's non-grain cargo; drill growing attributes | P8-LW-4 |
| `Disband`/`Fund` leftovers to the realm treasury (P7R-9) | P8-WS-2 |
| `SoldierHash` in a Phase 7 trail | P8-PF-3 |
| Wagon forts and chariots in live play (SG-E): fort walls in live battles, the sortie order, AI for forts and chariots, chariot archers shooting, `cheval_de_frise` | P8-LW-3; their art in P8-PP-1 |
| The arms industry in play (AE3): orders as supply contracts, physical goods in the arms trade, the steward's order, refit by commission | P8-WS-2 |
| The Fief and Armoury screens | P8-WS-1 |
| EA_F25 (L1 clay marginal), EA_F18 (L3 working capital) | P8-BL-1 |
| WL-9 (needs AE3 kits), EA_F11 (iron for kits; 960 kits need 2.1 t of bar) | P8-BL-1, after P8-WS-2 |
| The OV gaps (section 2.3) and `phase7_trail` for the cross-OS compare | section 2.3; the trail in P8-PF-3 |
| P7R-13: the day's first hour against 150 ms; the plan's budgets on the reference PC | P8-PF-1 |
| Kicks everywhere (CV-B; moves MT-13's pinned hash) | P8-WT-3, by decision D2 |

### 2.2 From earlier phases

| Carried item (source) | Home |
|---|---|
| `PursueAmbition` is a stub (phase-3) | P8-AT-2 |
| Hair colour and style, the apron layer, skin tones (phase-3) | P8-PP-1 |
| Screenshots of followers on the trail and the `lie` action; the nameplate doubling watch (phase-3) | P8-PP-2 |
| The cave beast's and draugr's art (phase-4, decided with the user) | P8-PP-1, with the new monsters |
| A larger parry window and hold-or-toggle (phase-4, decided with the user) | P8-AX-1 |
| Slow motion on a hero parry-riposte kill, blood decals that persist and dry, the after-fight next-step buttons, the morale bar, the horse wound diagram (phase-4) | P8-PP-2 |
| Watch settings rows (take-back, speed cap, which kinds pause) (phase-4) | P8-AX-1 |
| About 95 `wm_` strings against a budget of 40 (phase-4) | P8-LC-1 |
| Weather fields read by nobody: torch and daylight (phase-4) | P8-LM-3 |
| Weather field: fire (phase-4) | P8-LM-3 checks that Phase 6 fire reads rain (plan §6 has the test) and closes the row |
| Weather field: superstition (phase-4) | **Cut to after 1.0.** No system reads belief in omens, and faith would need a new belief rule |
| S10 practice bouts sharing a fight id, S11 the site factory for replays, G7 the live sections a moment ahead of the clock (phase-4) | P8-PF-3 |
| A replay cannot rebuild a vehicle site, a commission or a voyage (phase-5) | P8-PF-3 |
| S9 `Stopwatch` reads in the work engine behind a diagnostics flag (phase-5) | P8-PF-1 |
| S10 swallowed plan and table exceptions; the load report names the cause (phase-5) | P8-MD-2 |
| S13 the player's blueprints read outside the loader (phase-5) | P8-MD-2 |
| S15 the first hour as one chained script (phase-5) | P8-CX-2 |
| G4 `catch { }` in the dev console (phase-5) | P8-PF-3 |
| G5 the plan library list set from a pool thread (phase-5) | P8-PP-2 |
| The "Edge 82%" and "Protection x1.4" tooltip lines; the crafting and build-mode UI list (phase-5) | P8-PP-2 |
| Per-culture artefact names (phase-5) | P8-LM-2 |
| A roof material by culture or wealth (phase-5) | P8-PP-1 |
| Workers and live staff walking as LOD-bridge people (phase-5) | P8-PP-2 |
| A general water check in `PlanFitter`; taking down generated buildings (phase-6 overview) | P8-MD-2 |
| Cellars in library plans; a new mine as a walkable underground site (phase-6 overview) | P8-LM-3 |
| The Ledger's share market (phase-6 overview) | **Cut to after 1.0.** Shares beside the owner already work, no done-when needs a market, and a new coin path this late is a conservation risk |
| Person-table growth; compaction's dangling links (phase-6 G2, left by P7-0B) | P8-PF-2 |
| Late-game soil decline, household growth, delivery contracts no NPC takes (phase-6 G2) | P8-BL-1; who does the soil work is D13 |
| 0x710 coining crimes and sapping (Phase 7 reservations, never moved) | Stay after 1.0, as Phase 7 left them |

### 2.3 The Phase 7 OV gaps

| Gap (phase-7 review) | Closed by | Re-run in |
|---|---|---|
| OV-1: the 60 fps frame not measured | P8-PF-1 on the reference PC | OV-1 |
| OV-2: abstract battle; no live bubble, no F1 to F4, no armoury refit | P8-WS-1 (refit in the screen); the test drives the live bubble that M-A built | OV-4 |
| OV-3: a 400-man host and an abstract storm | P8-LW-1 (live storm, 1,200 men in waves) | OV-4 |
| OV-4: points to the war soak | P8-WT-1 | OV-5 |
| OV-5: no beach bubble, no hero boarding, no drowning | P8-LW-2 | OV-4 |
| OV-6: no L2, no caravan quote, no payback check, no bottleneck | P8-BL-1 (the test hero gets a claim) | P8-BL-1's BL-3 |
| OV-7: the copper cap not reached | P8-BL-1 | BL-4 |
| OV-8: no night attack, no mid-battle save, no mercenary contract; `phase7_trail` empty | P8-PF-3 | OV-9 |

### 2.4 Waiting on the user (inputs, not parts)

| Item | Needed by |
|---|---|
| The fun checks of Phases 4 to 7 (`docs/playtests/phase-4..7-checklist.md`) and the Phase 1 to 3 play | P8-BL-2 starts from their notes (month 8.4) |
| The reference-PC checks of Phases 1 to 7 (60 fps at street zoom, the three-region walk, the road fight, torchlight, build mode, 2,000 soldiers, the Phase 5 list) | P8-PF-1 (month 0) |
| Private testers (2 to 3) | rounds at about months 4.5, 8 and 10.4 (section 3) |
| A human read of the English strings (Phase 4 flavour, Phase 5's 1,100 rows, every later phase) | P8-LC-1's freeze (month 8.3) |
| Art style sheet v2 (phase-3) | P8-PP-1 (month 8.1) |
| The decisions of section 10 | each part's start, as listed there |

## 3. Build order

The rules are Phase 7's:
- **Three helpers at a time** (the user's rule), one lane each.
- A lane takes its next part as soon as that part's dependencies are merged.
- Steps are windows, not barriers.
- Each part is sized for one helper with its targeted tests. A part names its files and its gate tests.
- The close-out runs the full suite.

Months are from a lane simulation with the dependencies given. The estimate per part is in months of code.

0. **Step 0, plumbing and baselines** (months 0 to 0.9)
   - **P8-0A, shared plumbing** (lane A, 0.3): the 0x800 streams (section 6), the enum appends (section 8), the `SystemOrder` slots, the save registry for the new section and keys, the `phase8_trail` line (empty until filled), the chronicle type rows. Gate: the streams test, the enum append tests, every golden unchanged.
     - **Built (P8-0A):** the eleven 0x800 to 0x881 streams named in `Core/RngStreams.cs` (0x8F0..0x8FF stays empty); the section 8 enum appends (`InterruptKind` +4, so `InterruptBus.KindCount` is 31 and the default policy arrays are 31 wide, with BellRung and AmbitionStuck Notify and AmbitionChoice and EndingOffered Pause; `CauseKind` +3; `DirectiveState.SubDirective`; `SourceReason.DevOnly`); `SystemOrder` constants (`WarTollHour`, `AmbitionHourly`, `AmbitionPlanner`, `WarTollSeason` and four `Slice*` slots for the season-day systems; numbers are placeholders their parts may move, nothing registered); `Save/Phase8Saves` (the `ending` section and the additive-key list; no seeding pass, v8 stays); `Diagnostics/Phase8Trail` (empty, so `hashes` prints no `phase8_trail` line yet); eleven chronicle type rows with en.csv keys. Not done: the eighth difficulty value (parry-window assist) is not in the entry and waits for its part. No golden moved.
   - **P8-PF-1, the reference-PC pass** (lane B, 0.9, needs the user's machine). Gate: PF-1 to PF-5.
   - **P8-WT-2, calibration** (lane C, 0.9; the calibrate tool only). Gate: WT-6 to WT-9.
     - **Built (P8-WT-2), branch p8-wt-2, partly:** WT-6 AW-10, the count battle against v2 (per-pair cascade added; both refitted): win rate 31 to 32 of 40, both sides' casualties 6 to 25, duration 13 to 25, all three 1 to 13 (a named ratchet, D11). WT-7: scenario E (casualties, win rate, duration) and G's duration are inside the AW-4 gate; the wound mixes (E 2, F 1, G 3) stay named gaps. v2's row was refitted by `--abstract wartune`; v2 on the Phase 4 scenarios fell from 18 to 16 gaps. AB3 stays at 18 (v1 untouched). **Not done:** H, I (WT-8), AW-5 (the body plan is live combat) and AW-6 (WT-9); they need a design decision or live-combat changes. No golden moved.
1. **Step 1, war in play and the ambition tree** (months 0.3 to 4.3)
   - **P8-LW-1, the live siege assault** (lane A, 1.2). Gate: LW-1 to LW-5.
     - **Built (P8-LW-1):** `SiegeAssaults.Open` takes the whole host and garrison as units of 40 in a battle bubble capped at 300 live (a wave when a side falls under 60% of its share), on the curtain the host's engines lie to (west or east), with the starting geometry (`AssaultLayer.Live`: ladders on 2 to 4 stretches, a third of the first defenders on the walk at each, two defenders and 6 s to push a ladder, held by two climbers above half height, the hook's 300 pm on 0x850; `Siege/AssaultLayer.Live.cs`, `AssaultCommands.cs`). The hero's seven wheel orders (Shape ring, assault only) are a recorded `FightInputKind.AssaultOrder` with the formation orders' delay. Timber: ladders cost 40 kg each; `SiegeTimber.Fell` fells real trees within 2 km through the chunk delta and the tile's wood row into the camp store (`Cut`, mass exact). The game (`StreetView.Assault.cs`) turns `GameDrivesLiveAssaults` on, takes an opened assault up as a battle, saves it in `sieges` (optional keys 16, 17, 20 to 27 of the assault record; v8), resumes it, and closes it at a barrier (`CloseClosed`); the dead of the bubble are killed as persons. Headless worlds keep the switch off. LW-2 to LW-5 pass. LW-1 is met: the first live storm was a bug (no advance through the breach, the garrison's reinforcements entering outside the far wall, the captains holding the host), fixed; the live storm is then a real fight (the garrison yields in droves: 1.9 and 13.3 dead on average over 50 seeds, the town taken in 45), and the abstract storm's loss divisors are re-fitted to it (`SiegeConfig.StormLost*Div`, 4,000 and 2,000 became 190,000 and 16,400), the test a named D11 ratchet. Defenders' reinforcements reach the walk by the wall stairs (a step up). **Not done:** the AE1 convoy leg of the timber (only the felling is built); no tower or ram is built by the siege brain yet, so those orders wait on engines in the book; the Godot screenshot was not taken.
   - **P8-WT-1, war's toll** (lane B, 1.2). Gate: WT-1 to WT-5.
     - **Built (P8-WT-1), branch p8-wt-1, partly:** campaign intensity (an AI realm at war calls its levy on the first day of spring, summer and autumn when 30 days of its host's cost are funded and its treasury holds the first ten days itself, and is not winning by over 300; a realm with no host sends its warbands raiding, +350 on the raid's score; the brain takes the best target there is when none pays); AI purveyance when short (a war host under 10 days of baggage, 7 for a cruel or poor lord, holds the warrant); war flight and return as whole households through `Demography.Relocate` (departure and arrival both counted, purse and store kept, a camp when no dwelling stands free; streams 0x880 and 0x881; `war_flight` and `war_return` entries caused by what touched the place; armies keys 20 and 21); the soak counts raids and places taken as touches and gains a campaign-intensity gate. Only `phase6_trail` re-pinned (the two new keys). Nightly (2 worlds x 25 years, 18 minutes): world 0 passes coin, mass, people, purveyance unrest (22 of 22) and refugees (1,148 fled, 1,117 home), and fails intensity (17 hosts in 18 realm-war-years against 1.2 a year: 8 realm-seasons lacked the 30 days), bread on routes (24%, was 34%), borders (-2.6% against +0.5%) and recovery (7 of 10, was 0 of 6); world 1 has no pair in contact, so no toll can show. **Named ratchets (D11):** intensity, bread, borders, recovery; they need a design decision on what the 30 days are, how long a refugee stays out and what the bread gate measures.
   - **P8-AT-1, the ambition tree** (lane C, 1.0). Gate: AT-1 to AT-6.
     - **Built (P8-AT-1), branch p8-at-1:** `AmbitionDef` gains `tier`, `opens`, `requires`, `excludes`, `ending`; 30 ambitions in `content/base/ambitions` (8 start, 16 middle including the heir's `find_your_place`, 6 great: `found_a_dynasty`, `take_a_crown`, `found_a_realm`, `slay_a_legend`, `merchant_prince`, `a_holy_life`, naming the six endings of D1). The 12 new conditions are in `BaseVocabulary`. `Hero/AmbitionTree.cs`: `AmbitionFacts` (plain integers a world fills), `AmbitionRules` (a progress rule per condition, a step's progress the lowest, integer pm) and `AmbitionTree` (checks, and the offer: opens first then eligible middles, at most 4). `HeroRecord` keeps `AmbitionsDone`, `Offered` and `Dropped`, saved as optional `HeroSave` keys 13 to 16 (null while empty; v8). AT-1 to AT-6 pass; no golden moved. **Not done:** nothing builds an `AmbitionFacts` from the live world or checks the current step hourly yet (no system registered, no `ambition_*` chronicle entries or `AmbitionChoice` interrupt raised), the Journal tab, and the means row of AT-2 (`autopilot/means.jsonc` is P8-AT-2's); the new conditions' world facts (fief rank, realm, faction, works tier, church office, relic, monster tier, battles won) need their sources wired with the hourly check.
   - **P8-LW-2, beach and deck** (lane A, 1.1). Gate: LW-6 to LW-9.
     - **Built (P8-LW-2):** `Naval/BeachFights.cs` (`ArmyWorld.BeachFights`, off until the game sets `GameDrivesLive`): the bell (`Loot` rings it; within 3 km of the hero it is queued and `RaiseBells` raises `BellRung`, whose default policy is now Pause as live-war 3.2 says, P8-0A had Notify) and the beach bubble (hero within 1 km of the landing, or riding with the warband: the raiders ashore against the riders and the hero, in units of 40, capped at 300 live, the raid's ships as `CarrierLayer` frames aground behind the raiders). The raid waits while the bubble is open (`SeaRaid.LiveSince`, optional naval key 32, hashed only while set); `Close` kills the fallen as persons (Raid cause, a root) and the raid then ends as the abstract one does; the abstract band fight is the fallback after 24 hours. `Naval/CaptainBrain.cs` (the pure decision: flee, board at 0.8 or shoot, grapple, ram, approach; side by 0x858) and `CaptainDrive.cs` (carries it out through `ShipCombat.Ram`/`Throw`/`Haul`, `Join` and `BeginBoard`); `Naval/PushOff.cs` (a gap of 1.5 m or less jumped on 0x859, a miss is `CarrierLayer.Fall`, then the Phase 7 breath rule). The game (`StreetView.Beach.cs`) takes the bells and the bubble up like the live assault. LW-6 to LW-9 pass; no golden moved. **Not done:** nothing in a live sea battle steps `CaptainDrive` yet (the ships in the beach bubble are aground, so no captain acts there; the drive runs in the deck fights a test builds); the guards stay off the beach bubble's deck (they are not fighters in it); ships are not drawn in the battle view; a save made inside a beach fight resumes as the plain battle and the raid is then fought abstractly (the carriers are not in the battle save); the ramming angle is read as the heading against the target's side normal.
   - **P8-AT-2, autopilot pursues ambitions** (lane C, 1.0, after AT-1). Gate: AT-7 to AT-12.
     - **Built (P8-AT-2), branch p8-at-2:** `content/base/autopilot/means.jsonc` (`MeansDef`, a row for every condition id; `none` rows are conditions the AI cannot move yet), `Autopilot/AmbitionPlanner.cs` (pure: the lowest unmet condition, limits as bars, integer score, the `(3r+d+2)/4` rate, the reason line) and `AutopilotWorld.Ambition.cs` (the `PursueAmbition` directive running a child for a day, planning at dawn and when the child ends, `AmbitionStuck` after 3 zero days or 10 still days, the hourly check of the hero's step, `ambition_done`/`great_ambition_done`/`ambition_chosen` roots, `AmbitionChoice` raised and answered by the planner's score: `AiResponse.PickByScore` replaces P8-0A's decline mapping, as spec 3.1 says). Companions use the same planner on the hero's step. The panel's why line is the parent directive's name. Saved as optional `AutopilotRow` keys 40 to 44; v8. AT-7 to AT-12 pass; no golden moved. **Gaps closed (branch p8-at-gaps):** the hourly check no longer needs autopilot: `AutopilotWorld.HourlyHero` runs once per game hour crossed from `Advance` (and from the game's barrier while the player has taken control), so a hero played by hand finishes steps, gets the `ambition_*` chronicle entries (no `autopilot` datum) and the `AmbitionChoice` interrupt (Pause by default; an autopilot row is made only when something must be raised); only the planning stays autopilot-only. It is a hero-context pass, not an abstract system (the hero record is not in the tick world), so `SystemOrder.AmbitionHourly` stays unused. New means rows: `knows_person` talk (`person_talk`), `has_item` buy and craft (`item_buy`, `item_craft`), reputation by contracts (`rep_contract`, the autopilot's own work), `has_rank` and `holds_fief_rank` petition (`rank_petition`, `fief_petition`: open only for `free_person` by `sl:selfbuy` and `landholder` by a founding charter `ld:petition`), `is_in_place_kind` travel (`place_travel`, the autopilot's own Travel to a town, village, farmstead or off the roads; a manor has no place), with the directives `talk`, `craft`, `petition` (one-shots through `MeansHook`) and `travel`. `FactsOf` now fills `PlaceKinds` from the hero's position. Still stuck, with the reason "it needs you: the AI has no way to move it": `rules_realm` (no action crowns a hero), `founded_faction` (no desk founds one), and the other world conditions of the earlier note; guild_member, knight and baron-and-above ranks have no action and read "not open to you here". **Wired (branch p8-wire):** `AutopilotWorld.InstallAmbitionHooks` (`AutopilotWorld.Hooks.cs`), called where the game builds its autopilot (`StreetView.Autopilot.cs`), sets both hooks. `FactsHook` reads trust by person kind from the people the hero knows (occupation name; gentry are `lord` and `liege`; priests `abbot`), reputation from the standing book (`district` the best settlement or fief row, `region` the realm rows, a tenth of the pm), the rank `free_person` (no bond) and `landholder`/`baron`, and the fief rank from charters and the rule book. `MeansHook` issues an Autopilot-origin command through the hero desk: `Talk` to a person of the kind where the hero stands, `BuyItem` of the wanted shape from a merchant of that market, a `cf:order` at a maker who knows a recipe for the shape, `sl:selfbuy`, or `ld:petition` at a fief's seat. The means are open only where a person, a stock, a maker or a seat is found, else the hook returns false. **Not done:** the Journal tab and the `am_*`/`ap_*` strings are not written; a petition for a charter needs the hero standing at the seat (the autopilot's travel means takes him to a town, not to a seat); `buy` takes the first matching item, not the cheapest.
   - **P8-WS-1, the war screens** (lane B, 0.9). Gate: WS-1 to WS-5.
     - **Built (P8-WS-1), branch p8-ws-1:** sim views, read-only and stateless: `Armies/ArmyTabView.cs` (one row per warband, garrison, host and the hero's retinue, from the army, supply and soldiers' books; alerts: pay owed over 10 days, baggage under 5 days, men leaving; `HostSupply.BaggageDaysMilli` reads the days of food from the stores so a load shows the same), `WarSupply/FiefWarView.cs` (the levy by company and village, purveyance tallies, scutage owed and paid, the mint and its master, depot stocks), `Soldiers/ArmouryView.cs` (soldiers by slots, gaps, what a fill could close) and `Naval/HelmView.cs`; `WarSupply/WarScreens.cs` feeds them to the Ledger as a new Army tab (`LedgerTab.Army`, three appended `OwnerAlert` flags) and extra Fief-tab rows. New desk verbs `so:fillgaps` (G: `ArmouryFill.Plan(..., gapsOnly)` keeps what a man wears and gives the pieces to others) and `so:fill1` (Enter: one man). Game: the Armoury screen (new modal `Armoury` context; keys Up, Down, Left, Right, F fill all, G gaps only, T next template, Enter fit one man, Tab next armoury, Esc; opened by End or by Enter on the Army tab's retinue row) and `UI/HelmHud.cs`. WS-1 to WS-5 pass (`tests/.../WarScreens/WarScreensTests.cs`); no golden moved, save v8 unchanged. **Checked (branch p8-wire), left as it is:** no live ship is ever in the bubble outside the beach fight, and there the longships are `ShipFrame`s aground in a `CarrierLayer` with no `SeaShip` or crew state, no helm, and the hero neither commands nor stands on one (the guards stay off the deck), so `HelmView.Of` has nothing to read; `SetHelm` stays set by the dev command only until a deck fight or a sailing hero is in the bubble. **Not done:** the books keep no day of the last desertion, so the desertion alert shows while its causes (hunger or arrears) last; the Armoury's T cycles templates rather than opening an editor; no screenshot taken (the spec asks for none).
   - **P8-LW-3, forts and chariots in battle** (lane A, 0.8, after LW-1). Gate: LW-10 to LW-12.
     - **Built (P8-LW-3), branch p8-lw-3:** `Battle/FortLayer.cs` (`FightWorld.Fort`, its boards joined to `Walls`, so bodies, horses (riders and loose) and missiles stop at them; the standing boards in the arc count as braced polearms in the mounted `Refusal`, `RefusalEstimatePm` and the cavalry captain's estimate, for the other side's horse only; the order `OrderKind.Sortie` (appended) opens the chain nearest the order's point at once for 20 s via `WagonFort.OpenNow` and then hangs it again unless an enemy is within 4 m; the AI forms the ring for six wagons or more, enemy horse 30% of the enemy's men and the nearest rider within 2 km, and sends its foot to the middle; the cheval de frise, 40 kg, two men to lay it, 2 m, a third of a man's pace, `cheval_braced` 4 as braced polearms). `Battle/ChariotLayer.cs` (`FightWorld.Chariots`: the AI drives each car at the nearest unbraced foot on firm ground, looks 12 m ahead along the heading the turn limit gives it and never stands in woods, ditch, stakes, deep water, rubble, boards or chevaux; the thrust and hooves land once a second at 3 m/s or more through `FightWorld.ImpactMilli`; a crash throws the crew; the archer's cone x2 above 3 m/s). Numbers are content (`wagon_rules`, `chariot_rules`, `battle_order sortie`). Saves: optional keys 12 and 13 of the `battle` record (v8; the vehicles are the ride book's), `BattleBubble.Restore` takes the ride book. Nothing moves in a world without a fort or chariots (hash lines only while set); no golden moved. LW-10 to LW-12 pass. **Not done:** no wheel item or art for the Sortie order in the Godot view; **Wired (branch p8-wire):** `BattleOpening.Rides` and `BattleSideSpec.VehicleOwners` name the ride book and the owners (a warband's lord, a host's baggage and commander) whose finished war wagons and chariots come with a side; `BattleBubble.Open` calls `Battle/BattleVehicles.Attach`, which lays the wagons behind the side's line under a `FortLayer` (the side with most wagons, one fort a fight; an assault sets `NoFort`, its walls are its own) and raises each chariot with two of the side's men (lowest fighter ids with a person) under a `ChariotLayer`. The beach opening names the defending warband's lord and the live storm the host's baggage and commander. A field battle opened by the armies does not exist yet in play (only the dev `battle`, the storm and the beach open one), so only those two openings carry vehicles;  a hero-driven chariot is not driven; the sortie opens at once rather than after the 20 s unchaining of SG-E (read "opens for 20 s" as the open time).
     - **Built (P8-LW-4), branch p8-lw-4, partly:** **The live road battle** (the item OV-4 needed and no part built): `Armies/RoadBattles.cs` (`ArmyWorld.RoadBattles`, off until the game sets `GameDrivesLive`): when two hosts are due to fight and the hero leads one or is among its men and within 2 km of the meeting, `FieldBattle` opens a `BattleBubble` (units of 40, 300 live, the rest in waves at 600 pm, wagons and chariots by `BattleVehicles.Attach` through the hosts' baggage and commanders, F1 to F4 and the order wheel on the hero's side) and the hosts stand; the game (`StreetView.Road.cs`) takes it up and closes it at a barrier (`CloseClosed`: the fallen die as persons under one `battle_fought` root caused by the war, then `FinishField`, the fold, pen and war events the abstract fight uses). An unclosed bubble is fought abstractly after 24 hours, and a headless world never opens one. **Live against abstract (D11 named ratchet, `LiveWarRoadTests`):** on the AM-20 scene (534 men) the same side wins, but the abstract fight takes 10 men off the rolls to the live fight's 33 (30%) and 2 prisoners to 11; the test holds it to 20 to 45% of the live losses. Neither is changed. **Prisoners (3.4):** `ArmyWorld.TakePrisoners` calls `PrisonerPens.Take` (rank class from the soldier's role, class or fief; means from the household's purse; the captor's men a guard to ten) from the field battle (abstract, the game's hook and live), the warband's chase, a live storm the host won and the beach fight; an AI captor's common prisoners are given a fate at once (every captor is just, `TemperOf` is the hook). `Soldiers/PenWiring.cs` sets the pen hooks where the soldiers' runtime is built: feeding a kilogram a day of the captor's household staple (destroyed as eaten), sale where the culture's law has a market (a buyer's household purse to the captor's by `SlaveSale`, a bond for the buyer), cession of the fief of a lord who cannot pay the quartered ransom, and 200 standing lost on a broken parole. **Also:** fire arrows (`Fire/FireArrows.cs`, stream 0x851, 200 pm dry and 20 wet; the `fire_arrow` ammo is content; `FightWorld.FireArrowLanded` tells where one fell and `fire_started` accepts a War cause); drill gives +1 Strength, then Toughness, for each 30 drill days to 100; the plunder that is not grain leaves with the raiders as an export; the running sea (`Naval/ArmyWorld.SeaTrade.cs`, a daily system: a coastal village's fishing boat and two sailors landing 12 kg a man into a household store, ferries at fords of 6 m or more with fares from the nearest village's households, barges on a shuttle voyage; kept in `NavalBook.Trade`, saved as optional naval key 5); content parts `anchor`, `ballast`, `bilge_pump`, `oar_port` with recipes (no template uses them yet). LW-13 to LW-16 pass (`LiveWar/LiveWarPrisonerTests`, `LiveWarSeaTests`, `LiveWarRoadTests`). **Not done:** harbours as generated blueprints (the town generator picks no civic blueprint by id, so a quay needs a design decision; no harbour step is registered in the Phase 7 seeding route either), voyages carry no cargo, the game does not map a fire arrow's landing onto the fire world, the hero joins a road battle only as commander or one of the host's men (not as a bystander near it), an abstract storm's yielded men are not taken, sailors keep no record of their earlier trade, the Godot screenshot was not taken.
   - **P8-LC-2, chronicle grammar** (lane C, 0.6). Gate: LC-5 to LC-7.
     - **Built (P8-LC-2):** `Core/ChronicleGrammar.cs` (`ChronGrammar`: integer CLDR plural category, name forms `{x.gen}`, gender and plural selects `{x.gender|m:..|f:..|other:..}` and `{n.plural|one:..|other:..}`, and `Check` for the template check; English and a test language). `ChronicleTypes.Fill` (now public), `Format`, `FormatNote` and `ChronicleReader` take an optional grammar and gender lookup (`ChronicleView.Genders`, view only, not saved); the default is English, so old rows read as before. Four English rows use the forms (`ch_justice_hand`, `ch_justice_displayed`, `ch_plague_settlement`, `ch_plague_quarantine`). LC-5 to LC-7 in `LocalisationGrammarTests`. D6 is open: only English and the test language exist; the per-language grammar loader is not built. Saves stay v8, no golden moved.
   - **P8-WS-2, arms in play** (lane B, 0.9, after WS-1). Gate: WS-6 to WS-10.
     - **Built (P8-WS-2), branch p8-ws-2:** `WarSupply/ArmsSupply.cs` (a lord's season order is an `arms_supply` contract of contracts v2, the price in escrow, offered to the makers of his realm then abroad: a banned maker is not offered it; the maker draws real bar iron and timber or cloth from its own store, destroyed as `Materialised` into real items, works the minutes, and the kit goes by cart or ship as items held by the load's own id, so a robbery moves them to the robbers; pay is on delivery a piece at a time from the escrow, `price x max(500, 1000 - 100 x weeks late) / 1000`, the cut and the rest return to the lord's purse; a hired steward places the season's order inside his budget and the templates' gaps once a season; a `refit_commission` contract has an armourer buy the missing parts from a supplier and fit them at the armoury; the arms trade ships real bar iron from the exporter's depot through a transit store, imported or exported through the mass ledger beside a realm with no depot; a ban stops the caravan before anything is loaded). `HostSupply.Deposit` records the purses that fund a chest and `Disband` gives the leftover back to them newest first by `Subsidy` (P7R-9). Saves: optional `ArmsSave` keys 8 to 12 and `HostAccountSave` key 13, null while empty (v8); two new contract kinds, three chronicle types (`arms_delivered`, `arms_fitted`, `arms_robbed`). WS-6 to WS-10 pass (`tests/.../WarSupplyPlay/ArmsInPlayTests.cs`). **EA_F11 and WL-9:** not measured here; they stay with P8-BL-1. What WS-2 gives them: a kit now draws real bar (a piece's iron in `arms.jsonc`, 2.1 t for 960 levy kits) from a maker's store, so the iron is a demand the economy sees once makers are wired. **Wired (branch p8-wire):** `WarSupply/ArmsPlay.cs`: the service is built from the state on use (`ArmsPlay.Build`; the book is the war book's, nothing new is saved); makers are the open smithies (their own store and purse, 480 minutes a worker a day), the supplier of a refit the smithy of the settlement, a realm's depot the war depot at its first town, a lord's depot his purse. `ArmsDaySystem` (`arms_day`, H1 after the host supply, hour 18) runs `Day` and `ArmsSeasonSystem` (`arms_season`, H6 hour 5 of a season's first day after the war supply's season) runs `StewardSeason`; both are no-ops for a world with no job, load or steward, and the runtime sets `ArmsPlayLink` in the world's bag. Desk verbs `so:steward`, `so:nosteward` and `so:commission` (`SoldierDesk`), and on the Armoury screen S (steward on or off) and C (commission 10 kits) with a status line. **Not done:** robbery of a load from a caravan encounter; the AI lords hire no steward; the steward is the hero's and orders levy kits, not the templates' gaps; the commission and steward use the levy kit list; a gambeson's cloth (3.2 kg at 1,400 mites a kg) costs more than its 1,500-mite price, so a commission cannot afford one and the armoury has no place for it.
   - **P8-LW-4, prisoners and the small war gaps** (lane A, 0.6, after LW-2). Gate: LW-13 to LW-16. **Milestone M-1 "war in play"** (about month 4.0): OV-4 green.
   - **P8-WT-3, kicks everywhere** (lane A, 0.2, by D2). Gate: WT-10 to WT-12.
2. **Step 2, endings, the world's content and input** (months 3.5 to 6.7)
   - **P8-EN-1, endings** (lane C, 0.8, after AT-1 and LC-2). Gate: EN-1 to EN-6. **Milestone M-2 "a life to an ending"** (about month 4.3): OV-2 green.
     - **Built (P8-EN-1), branch p8-en-1:** `Hero/Ending.cs` (`EndingDesk`: the offer, its 30-day lapse, and `Take`, which writes the `ending_taken` entry caused by the great ambition's, builds the epilogue, hands the hero's row to the NPC AI (status Alive, companions Ended, autopilot off; coin, mass and people untouched) and sets `WorldState.Ending`), `Core/Epilogue.cs` (beats in order: origin, each done ambition with its last cause, the 3 to 6 heaviest deeds that name the hero by a type weight table with ties by id, companions alive, dead or parted, heirs, holdings, what became of, the ending's own beat; 3 variants each, chosen by 0x810 keyed on the hero's id and the beat index; `Render` writes a paragraph from keys, ids and integers with `ChronicleTypes.Fill` and so LC-2's grammar), the `ending` section (`Save/EndingSave.cs`) and `HeroSave` keys 17 `EndedAt` and 18 the open offers (`id:lapseDay`), `WorldLibrary.RunInfo` `EndingId`/`EndDay` (omitted while null), `MarkEnded` and `Legends`. `AutopilotWorld.CheckAmbition` offers the ending when a great ambition is done (and no longer re-finishes a done ambition each hour) and raises `EndingOffered`; `InterruptRouter.PolicyOf` forces Pause for it whatever the row says, like BigChoice. The game: the offer dialog (End here, Carry on, Esc carries on), the Journal row with "End here", the last save to the run's slot, the library mark, the `EpilogueScreen` over the menu, and an ended save loads as that screen (read-only; the list tags it). EN-1 to EN-6 in `EndingTests`; about 90 `ep_*` strings. No golden moved, saves stay v8. **Not done (P8-EN-2):** the text export, the library's legend cards by name, "after the legend" and "from the first day" starts, the pseudo-locale check; "what became of" reads the unrest rung, the eldest heir's age and the best and worst standing, not the holdings' last-season profit; the great ambitions' conditions that the world cannot yet fill (see AT-2) still keep some endings out of reach in play.
   - **P8-BL-1, economy carry-overs** (lane B, 1.0, after WS-2). Gate: BL-1 to BL-6.
   - **P8-PF-2, dead rows** (lane A, 0.6). Gate: PF-6 to PF-8.
   - **P8-AX-1, accessibility and settings** (lane C, 0.9). Gate: AX-1 to AX-10.
   - **P8-LM-1, landmarks** (lane A, 0.8). Gate: LM-1 to LM-4.
   - **P8-LM-3, the bestiary and sites** (lane B, 1.2). Gate: LM-9 to LM-14.
   - **P8-CT-1, the controller** (lane C, 0.9, after AX-1). Gate: CT-1 to CT-6.
   - **P8-LM-2, unique NPCs** (lane A, 0.8, after LM-1). Gate: LM-5 to LM-8.
   - **P8-CT-2, the Steam Deck** (lane C, 0.6, after CT-1). Gate: CT-7 to CT-10. **Milestone M-3 "content complete"** (about month 6.4): LM-1 to LM-14 green.
3. **Step 3, the manual, sound and tools** (months 6.1 to 9.0)
   - **P8-CX-1, the Codex** (lane B, 0.8, after LM-3 and AT-1). Gate: CX-1 to CX-6.
   - **P8-AU-1, the audio engine** (lane A, 0.7). Gate: AU-1 to AU-6.
   - **P8-EN-2, the epilogue screen and library** (lane C, 0.4, after EN-1). Gate: EN-7 to EN-10.
   - **P8-CX-2, onboarding v2** (lane B, 0.8, after CX-1 and AT-2). Gate: CX-7 to CX-12.
   - **P8-MD-2, the blueprint editor and load causes** (lane C, 1.0). Gate: MD-5 to MD-10.
   - **P8-PF-3, replay and trails** (lane A, 0.6, after LW-4). Gate: PF-9 to PF-12.
   - **P8-PP-2, UI and feel** (lane B, 0.7). Gate: PP-6 to PP-10.
   - **P8-LC-1, the string audit and freeze** (lane A, 0.6). Gate: LC-1 to LC-4. **The string freeze** (about month 8.3).
   - **P8-PP-1, art** (lane C, 0.9, needs the style sheet). Gate: PP-1 to PP-5.
   - **P8-AU-2, audio content** (lane A, 0.5, when the files arrive: D3). Gate: AU-7 to AU-10.
4. **Step 4, balance and release prep** (months 8.4 to 10.0)
   - **P8-BL-2, the balance pass** (lane B, 1.0, after the fun-check notes). Gate: BL-7 to BL-12.
   - **P8-RL-1, builds, platform and crash reports** (lane A, 0.7). Gate: RL-1 to RL-6.
   - **P8-MD-1, the modding guide and the example mod** (lane C, 0.6, after MD-2). Gate: MD-1 to MD-4.
   - **P8-RL-2, the Steam page and the release checklist** (lane A, 0.5; the page's timing is D7). Gate: RL-7 to RL-10.
   - **P8-LC-3, translations** (lane B, 0.3, only if D6 names languages; waits for the batch back, about month 9.5). Gate: LC-8 to LC-10.
5. **Step 5, the close-out and 1.0** (months 9.8 to 10.9)
   - **P8-G1** (0.6): OV-1 to OV-9 (section 5) in `tests/Ravensguard.Sim.Tests/Phase8Overview/` with a kit `P8Game.cs`; the budgets on the reference PC; the third tester round.
   - **P8-G2** (0.5): the nightlies green (the war soak on ten worlds, the war-load soak, the 100-year soak); the code and bug check (plan §6) in `docs/reviews/phase-8.md`; the fun check; the release candidate; **1.0**.

**Outside the lanes (calendar, not code):**
- **Testers:** round 1 at about month 4.5 (first hour and war), round 2 at about month 8 (onboarding v2), round 3 on the release candidate (about month 10.4). Each asks "did you understand why?".
- **Translations** (if D6 names any): batch 1 at month 5, batch 2 at the freeze (month 8.3), back by month 9.5.
- **Audio** (D3): the brief and the order by month 1; stems by month 8.
- **The Steam page** (D7): a first "coming soon" page at about month 5, with that month's screenshots, the description and the capsule art; P8-RL-2 makes it final with the PP art, the trailer and the measured requirements.

**Estimate against plan §5 (8 to 12 months):**
- Step 0: 0A 0.3, PF-1 0.9, WT-2 0.9.
- Depth: AT 2.0, EN 1.2, LM 2.8, CX 1.6.
- War in play: LW 3.7, WS 1.8, WT-1 1.2, WT-3 0.2.
- Polish: AU 1.2, AX 0.9, LC 1.5, CT 1.5, PP 1.6, BL 2.0, PF-2 0.6, PF-3 0.6.
- Players and release: MD 1.6, RL 1.2.
- Close-out: G1 0.6, G2 0.5.

**Serial sum: 30.4 months.** With three lanes the floor is 10.1; the lane schedule above ends code at **10.9 months**. Add about 10% integration slack (merges, review fixes, the reference-PC runs): **plan on 11 to 12 months**, at the top of the plan's range, as Phases 3 to 7 were.
- With two helpers it is about 16 months; with four, about 8.5.
- If D6 is "English only", LC-3 goes (0.3) and the vendor waits leave the critical path.

**The overrun rule** (plan §7: more than 50% over) triggers at **18 months**. Checkpoints: M-1 by month 6; M-3 by month 9.

## 4. Done-when mapped to specs and tests

| Done-when (plan §5, Phase 8) | Specs | Proving tests | Integration |
|---|---|---|---|
| The full ambition tree, with autopilot pursuing ambition steps | 01 | AT-1 to AT-12 | OV-2 |
| Optional endings with a chronicle epilogue; the player may carry on; the world stays in the library | 02 | EN-1 to EN-10 | OV-2 |
| About 40 landmarks and 30 unique NPCs | 03 | LM-1 to LM-8 | OV-3 |
| The full bestiary (3 to 6) and more site templates | 03 | LM-9 to LM-14 | OV-3 |
| The full Codex; onboarding v2 | 04 | CX-1 to CX-12 | OV-3 |
| Final audio and music | 10 | AU-1 to AU-10 | OV-6 |
| The accessibility pass | 11 | AX-1 to AX-10 | OV-6 |
| Localisation, including generated chronicle text | 12 | LC-1 to LC-10 | OV-6 |
| Controller and Steam Deck | 13 | CT-1 to CT-10 | OV-7 |
| The balance pass | 08 | BL-1 to BL-12 | OV-5 |
| Modding for players: guide, example mod, blueprint editor | 14 | MD-1 to MD-10 | OV-8 |
| Steam page; 1.0 | 16 | RL-1 to RL-10 | OV-9 |
| Phase 7 carried in (war in play, war's toll, budgets) | 05, 06, 07, 09 | LW, WS, WT, PF | OV-1, OV-4, OV-5 |

## 5. The overview's integration tests

| Test | What it runs | Passes when |
|---|---|---|
| **OV-1** reference PC | every §2.11 budget, and 2,000 soldiers with 300 horses at every zoom band for 6,000 ticks, on the user's machine | tick average <= 25 ms, worst tick a minute <= 33 ms, frame p99 <= 16.6 ms, the hour <= 150 ms, the season <= 500 ms; a miss drops the default battle size in steps of 250 and is logged in `docs/perf/` |
| **OV-2** a life to an ending | headless, seed 1234: a merchant origin on autopilot with `PursueAmbition` from day 0, interrupts on "let the AI handle it" | the start ambition, a middle one and the great "merchant prince" done within 40 game years; the ending is offered and taken; the epilogue has every required beat; the world reopens in the library with the hero as an NPC; coin, mass and people exact |
| **OV-3** the first hour v2 | the chained bot script for all 8 origins, then 2 game seasons | every origin reaches its first step; the Codex unlocks at least 25 entries; a forced defeat shows a cause chain of 3 or more links; no hint fires twice; a rumour of a lair leads to its Codex page |
| **OV-4** war in play | Phase 7's OV-2, OV-3 and OV-5 rerun with their gaps closed: the live road battle with F1 to F4 and a refit; a 1,200-man host storming the hero's breach live in waves; a beach bubble, the hero boarding, a man drowning | each chain caused from its first entry; prisoners taken into pens; save and load mid-assault and mid-boarding byte-identical |
| **OV-5** war's toll | the war soak, ten worlds, 25 years (nightly) | every AW 4.6 gate by name, with the WT-1 additions; AW-15 still green in CI |
| **OV-6** presentation | `--autoplay` screenshots in English and the pseudo-locale, every settings toggle, every colour-blind filter, with the final audio on | no missing key, no clipped text at 960x540 and 1280x800, every setting round-trips, faction colours pass the check, every audio row has a file or is silent |
| **OV-7** pad only | the first hour and one fight played by injected pad input at 1280x800 | every screen reached by focus; no action without a pad binding; frame budget on the Deck profile |
| **OV-8** mods | the example mod and a player blueprint saved in the editor | both load through the `ModLoader`; the save names them; a missing mod gives a readable report; save, load and save byte-identical |
| **OV-9** release | the exported Windows and Linux builds, the 60-day war trail from Phase 7's OV-8 with a night attack, a mid-battle save and a mercenary contract | smoke-boot on both; `phase7_trail` and `phase8_trail` equal on Linux and Windows; the crash zip made; a 10-year save under 150 MB, saved in under 3 s and loaded in under 10 s |

## 6. RNG streams

Phase 8 uses **0x800..0x8FF**, 16 a spec:

| Block | Spec | Use |
|---|---|---|
| 0x800..0x80F | ambitions | means tie-breaks (0x800), keyed on the hero id and the day |
| 0x810..0x81F | endings | epilogue variant choice (0x810), keyed on the run id and beat index |
| 0x820..0x83F | landmarks, NPCs, bestiary | landmark placement (0x820), NPC placement (0x828), lair placement (0x830), monster behaviour (0x831) |
| 0x850..0x86F | live war | ladder pushes (0x850), captain approach (0x858), boarding jumps (0x859) |
| 0x880..0x88F | war's toll | flight choice (0x880), return (0x881) |
| 0x8F0..0x8FF | reserve | - |

Codex, onboarding, screens, balance, performance, audio, settings, localisation, controller, modding and release draw nothing from the sim. Presentation randomness (sound variants, decal shapes) uses its own unseeded source that the sim never reads, outside the determinism trail.

## 7. Saves

**No version bump: v8 stays, and 1.0 ships v8.** No stored meaning changes. Every addition is an optional section or an additive key, absent while empty:
- **one new section:** `ending` (only after an ending is taken);
- **additive keys:** the hero (ambition history, branch choices), `autopilot` (the planner's sub-directive and rates), `sites` (lair kind, legend, met state), `codex` (manual entries by name), `onboarding` (hints seen), `armies` (refugee rows), `sieges` (assault waves, ladders), `naval` (captain state), `battle` (fort walls, chariot cars), `soldiers` (pen takes);
- **the header's difficulty array:** an eighth value, the parry-window assist (a seven-value array reads as 1,000 pm).

Dropping dead person rows (P8-PF-2) changes which rows a save holds, not their format; it moves trails, each re-pinned with the reason.

New content (ambitions, landmarks, NPCs, monsters, templates, ship parts, `fire_arrow`, `cheval_de_frise`) is mapped by name by `ContentRemap`. **Old worlds:** library worlds keep landmark and site placer v1, so their generated hashes do not move. New worlds get placer v2. New civic pieces (harbours) come through the Phase 7 seeding route.

**Round trips:** OV-4, OV-8, OV-9 and each spec's own. `WorldRuntimeResumeTests` gains a tenth case holding every Phase 8 key.

**1.0's promise:** a 1.0 save loads in every 1.x through `SaveMigrator`; the format after 1.0 changes only by migration.

## 8. Shared enums (append only)

- `InterruptKind`: +`BellRung`, `AmbitionChoice`, `AmbitionStuck`, `EndingOffered`.
- `CauseKind`: +`WarFlight`, `WarReturn`, `MonsterLair`.
- `DirectiveState`: +`SubDirective` (a `PursueAmbition` running a child).
- `SourceReason`: +`DevOnly` (Phase 7's P7R-17 note: the dev verbs stop using `Seed`).
- Chronicle types: `ambition_chosen`, `ambition_done`, `great_ambition_done`, `ending_taken`, `lair_rumoured`, `monster_slain`, `war_flight`, `war_return`, `ladder_pushed`, `ship_boarded`, `fort_formed`.

## 9. Budgets

The plan's §2.11 table is the ledger. Phase 8 adds:

| Work | Budget |
|---|---|
| The ambition check, every hour | <= 0.1 ms |
| The planner, at dawn | <= 0.5 ms a pursuing hero or companion |
| The epilogue | <= 2 s at the end, on a worker |
| Lairs, monsters' brains in a crawl | inside Phase 4's site tick |
| War's toll: flight and return | <= 3 ms in the hour; <= 20 ms in the season |
| Audio on the main thread | <= 0.5 ms a frame; 48 voices at most |
| The Codex's live numbers | at open, <= 5 ms |
| Steam Deck profile | 30 fps floor in a 300-person bubble; the battle size from CT-9 |

## 10. Decisions for the user

Only the questions that block a part. The recommendation is first.

| # | Question | Options (recommended first) | Blocks |
|---|---|---|---|
| D1 | **How many endings at 1.0?** | **(a) Six, one per great ambition:** found a dynasty, take a crown, found a realm, slay a legend (clear the deep barrow), merchant prince, a holy life. (b) Three, the plan's examples: dynasty, crown, the deep barrow. (c) Every great ambition a mod adds, with the six as the base. | P8-AT-1 (by month 0.9) |
| D2 | **Kicks and bites everywhere** (CV-B)? | **(a) Yes:** one rule for every fight, as the resolver is one rule; MT-13's hash is re-pinned with the reason. (b) Only in formation fights, as now. | P8-WT-3 (by month 4) |
| D3 | **How is the audio sourced?** | **(a) Library effects (CC0 or royalty-free) and commissioned music.** (b) All from libraries, music included. (c) Commission both. | P8-AU-2's order (by month 1) |
| D4 | **Which music?** | **(a) Grounded early music:** frame drum, lyre, hurdy-gurdy, bone flute and voice drones, sparse, in calm, tension and battle layers, about 60 minutes. (b) Orchestral, in the usual fantasy style. (c) Ambient and minimal, music only at key moments. | P8-AU-2's brief (by month 1) |
| D5 | **How far does controller support go at 1.0?** | **(a) Full pad in world, travel, combat, menus and the Ledger; build mode and the designers by a virtual cursor; aim at Deck "Playable".** (b) Every screen native and aim at Deck "Verified" (about 0.8 months more). (c) Keyboard and mouse only; pad and Deck after 1.0. | P8-CT-1 (by month 5.2) |
| D6 | **Which languages at 1.0?** | **(a) English, with the pipeline proven by a pseudo-locale;** languages come as free updates. (b) English, German and Simplified Chinese (about 90,000 words each to a vendor). (c) English, French, German, Spanish and Simplified Chinese. | P8-LC-1's scope and the batches (by month 5) |
| D7 | **When does the Steam page go up?** | **(a) "Coming soon" about six months before 1.0** (about month 5), with screenshots and a trailer, no playable build. (b) Three months before. (c) At launch. | P8-RL-2 and the capsule art (by month 4) |
| D8 | **How many monster kinds?** | **(a) Five:** the draugr and cave beast, plus a bog-thing, a barghest (a black hound of ruins) and a lindworm (a great serpent of deep caves). (b) Four (no lindworm). (c) Six (and a mountain troll). | P8-LM-3 (by month 4.9) |
| D9 | **Crash reports at 1.0?** | **(a) Ask on each crash and upload only on yes,** to a small endpoint you own. (b) Local zip only; the player attaches it to a forum post. (c) A setting "always send", asked once. | P8-RL-1 (by month 8.8) |
| D10 | **Steam features at 1.0?** | **(a) Achievements (about 30: ambition steps, endings, legends) and cloud sync of settings only;** saves stay local (they reach 400 MB). (b) No Steam features. (c) Achievements and cloud saves under a size cap. | P8-RL-1 (by month 8.8) |
| D11 | **If a calibration gate still misses after its part** (AW-10, AW-C, an AW-16 gate), does 1.0 ship with it as a named ratchet? | **(a) Yes:** the gap is written in the review; a ratchet can only tighten. (b) No: 1.0 waits until it passes. | P8-WT-1, WT-2 and G2 |
| D12 | **If Phase 8 runs late, is the cut order right?** (section 11) | **(a) As proposed.** (b) Change the order. | the overrun rule |
| D13 | **Who does the soil work late in a long game?** (phase-6 G2: soil jobs have no crew after about year 60, and famines rise) | **(a) The holders' own households first, in idle hours, then the day-labour pool at the posted wage.** (b) The labour pool only, with a higher wage. (c) The lord's levy as labour service, a few days a season. | P8-BL-1 (by month 3.9) |

### 10a. Decided by the user (27 Nov 2026)
- **D1: six endings,** one per great ambition.
- **D2: kicks and bites everywhere.** MT-13's hash is re-pinned with the reason in P8-WT-3.
- **D11: a calibration gate that still misses ships as a named ratchet.** The gap is written in the review, and the ratchet can only tighten.
- **D13: the holders' own households do the soil work first, in idle hours, then the day-labour pool at the posted wage.**
- **D8: six monster kinds:** the draugr and cave beast, plus a bog-thing, a barghest, a lindworm and a mountain troll (27 Nov 2026). Monsters stay rare, deep and tied to a legend.
- **D5: keyboard and mouse only at 1.0** (27 Nov 2026). Controller and Steam Deck support (P8-CT-1, CT gates) move to the after-1.0 list; its 1.5 months leave the schedule.
- **WT-3b closed at merge:** AB-3's known gaps were 18 on main (the user's decision of 26 Nov); the merged result is 17, so the bound is tightened 18 to 17 as a named ratchet (D11). The 16 written above was the unmerged branch's.
- D3, D4, D6, D7, D9, D10 and D12 are asked before the part they block.

- **Built (P8-WT-3):** `FightWorld.Kicks` now defaults to on in every fight (`KicksOn => Kicks ?? true`), not only in a fight with a formation book (D2); no new stream, saves stay v8. Re-pinned, reason "D2, kicks and bites everywhere (P8-WT-3)": MT-13 `Fight` 8079067610618892121 -> 17410705065339015116 and `Mounted` 17694732195683071764 -> 9220551877504086168. The CV-B test that asserted kicks off in a Phase 4 fight now asserts on. No other golden moved. **Equivalence, re-recorded (partly recovered; stopped per D11):** the wound tables rebuilt (`--abstract tables`; AW-8 library fingerprint re-pinned 0x4C301EA952131FAA to 0x6F208EA1295D9DCD, AB-7 and AW-7 battle hashes re-pinned, all because the content they hash was rebuilt on purpose), the live references re-recorded (`--abstract reference`, `war --reference --seeds 200 --seed0 20000`, `count reference`; D, DMud, F and G moved, E and the line scenarios did not), v1's cavalry rows refitted (`--abstract fit`, then hand-tuned `charge_pm`, `rider_blows_per_min_x10`, `rider_slow_blows_per_min_x10`, `vs_rider_slow_rate_pm` for MT-12), v2 refitted (`wartune`, w4 6) and the count tables refitted (`count fit --resume`, anneal). **Recovered:** AB-3 gaps 18 to 16 (bound tightened to 16), MT-12 and AW-4 soak green. **Not recovered, bounds untouched:** AW-4 Phase 4 v2 gaps 16 to 17; AW-4 live named ratchet gains F side 1 casualties (10.8 vs 8.7) and F wound mix [1]; AW-10 count ratchet win rate 32 to 31, casualties 25 to 21, duration 25 to 23, decided winners 32 to 31, all 13 to 10; AB-4 asserts riders lose on mud (live 25%), and the live riders now win 92% on mud, so that premise needs a decision. No golden moved (phase5_trail and phase6_trail unchanged).

- **Built (P8-WT-3b, the mud finding):** with kicks on everywhere the live riders won 92% on mud (25% before; AB-4's premise). Cause: mud caps a gallop at a 5 m/s trot (`max_mmps` 5000, no charge), and the kick and bite had no gait test, so 99% of the kicks and bites in the mud fight were delivered by horses trotting through the foot (about 2 kicks per horse-minute, 40% of the riders' pain), a kick being 28,800 milli against a no-charge lance thrust of about 10,000. The fix is one line in `HorseKicks`: a ridden horse above a walk (`WalkMmps`, 2.3 m/s for a courser) neither kicks nor bites (led and held horses stand and still do); kicks stay on in every fight (D2), no new stream, saves stay v8. Live mud riders 92% -> 24% (25% before D2), D 100%. The wound tables rebuilt come back byte-identical to those before P8-WT-3 (so the AW-8, AB-7 library pins are the old ones), the count and v2 rows return to their pre-P8-WT-3 values and the stored live references (`--abstract reference`, `war --reference`, `count reference`) are re-recorded; v1's cavalry rows are refitted (`charge_pm` 414, `rider_blows_per_min_x10` 96, `rider_slow_blows_per_min_x10` 74, `vs_rider_*`; `--abstract fit --gaps`, which counts AB-3's own gaps). MT-13 re-pinned (`Fight` 7336637743236853923, `Mounted` 16230207049363040228), AB-7 and AW-7's v1 hash re-pinned. **Not recovered, bound untouched (D11):** AB-3 gaps 17 against 16 (B 4, C 6, D 3, DMud 4: mud's side-0 casualties 2.99 vs 5.17 and duration, the D and mud wound mixes); AW-4's Phase 4 v2 gaps 16 (at the bound) and AW-10's 32/25/25/32/13 (at the bounds) have no headroom to tighten.

## 11. Cut order (overrun rule)

The rule triggers at 18 months. Each item below moves to the after-1.0 list; its spec section stays as the design. First cut first:
1. Translations beyond English (if D6 named any); the pipeline and pseudo-locale stay (LC-3).
2. Deck extras beyond "Playable" (CT-2's per-screen layouts); the pad stays.
3. Voyages, ferries and fishing in the running world (LW-4); harbours and raids stay.
4. Demolition of generated buildings (MD-2); the player's own stay demolishable.
5. Chariot archers and the chariot AI (LW-3); chariots driven by the hero stay.
6. Wagon-fort AI and `cheval_de_frise` (LW-3); the hero's fort and its walls in battle stay.
7. Landmarks beyond 30 and unique NPCs beyond 20 (LM-1, LM-2; content scales apart from systems).
8. Monsters beyond three (LM-3); the plan's floor of three kinds stays.
9. Music by culture group (AU-2); one set of layers stays.
10. The blueprint editor's in-game text pane (MD-2); save as blueprint, validate and load through the loader stay.
11. The epilogue's text export (EN-2).
12. AW-C scenarios H and I beyond named ratchets (WT-2), by D11.

**Never cut:**
- the ambition tree, autopilot pursuing it, and optional endings;
- three or more monster kinds and the Codex as the manual;
- the accessibility pass, settings and captions;
- the live siege assault, the beach and deck fight, and the war screens;
- war's toll re-run and every conservation gate;
- the reference-PC pass, saves, determinism and the Linux and Windows trail;
- the release build, crash handling and the Steam page.

## 12. Risks

| Risk | Mitigation |
|---|---|
| **Scope:** 30.4 serial months on three lanes | helper-sized parts, the cut order, checkpoints M-1 by month 6 and M-3 by month 9 |
| **War's toll needs tuning, not code** | WT-1 starts in step 1 so the nightly has months; D11 names the fallback |
| **The reference PC** is only the user's | PF-1 first; the battle-size fallback in steps of 250; every miss logged |
| **External waits** (audio, translations, testers) | ordered by month 1 and 5; English-only default; AU-1 runs on placeholders |
| **Text volume** (about 9,000 rows, chronicle templates) for localisation | LC-2 early; the freeze at 8.3; pseudo-locale in CI from LC-1 |
| **Balance on testers' notes** that arrive late | BL-2 last; BL-1's measured gates do not wait on notes |
| **No public feedback until 1.0** | three tester rounds; the "why" question; the Steam page draws early interest without a build |
| **Late changes moving goldens** | each re-pin with its reason beside it; the generated layer and history never move |

## 13. Test strategy

**180 golden tests:** AT 12, EN 10, LM 14, CX 12, LW 16, WS 10, WT 12, BL 12, PF 12, AU 10, AX 10, LC 10, CT 10, MD 10, PP 10, RL 10. OV-1 to OV-9 on top.

- **Helpers** run their part's tests and the save, resume and golden-hash tests.
- **CI** runs the full suite, the determinism job (`phase7_trail`, `phase8_trail`), AW-15, the pseudo-locale screenshot pass and the key-conflict check for keys and pad maps.
- **Nightly** runs the war soak (ten worlds), the war-load soak (five seeds), the 100-year soak, the OV-2 life and the balance bots.
- **Conservation** stays exact every soak day: coin, mass, people.
- **Phase end:** the code and bug check in `docs/reviews/phase-8.md`; the fun check; the testers' question; then 1.0.
