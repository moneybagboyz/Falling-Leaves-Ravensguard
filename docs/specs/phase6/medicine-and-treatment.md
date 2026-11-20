# Medicine and treatment: the paper-doll treatment screen, physicians, medicine content, crutches, Treat scoring

Plan refs: §4A.3 (wound records open -> dressed -> stitched -> healing -> scar; tourniquet -98%; set fracture 20 game days, unset 35; tendons need surgery at skill 50+ within a day; crutches and splints restore some movement; amputation from a blow or a surgeon's choice), §3.13 (warnings before failure: the injury consequences on the paper-doll), §2.8 (keys). Moved here by the user (Nov 20): the Phase 4 medicine items that Phase 5 did not build (`docs/reviews/phase-4.md` "Still open", `docs/reviews/phase-5.md` S7). **This is step 1 of Phase 6** (overview section 3).

## 1. Purpose
Since Phase 4 a wound outlasts the fight (it bleeds, heals and infects on the ledger), but outside a fight or a crawl the hero cannot treat it by hand: the auto-dress stop-gap (`FieldDressing`, `LiveWorld.DressAfterFight`, `StreetView.AutoDress`, the `wd_*` keys and the "Bind my wounds after a fight" setting) binds bleeders once when a fight ends. This spec makes treatment a thing the player does, on any patient, anywhere, with the Phase 4 rules unchanged: a paper-doll screen, physicians and barber-surgeons as paid real people, the medicine content files, crutches, NPCs who treat each other, and the tourniquet's ischaemia steps. The auto-dress is removed when the screen lands.

## 2. Dependencies
| Needs | From |
|---|---|
| `Treatment` (13 `TreatKind`s, times, skill floors, needs), `TreatAi` (`Score`, `Next`), `MedicineTables`, `WoundBook`, `WoundLedger`, `WoundSystem`, `AbstractFirstAid`, `Surgeon.Nearest` | exists (Phase 4) |
| `FieldDressing.Dress` (the pure planner) | exists; kept as the "dress all bleeding" planner |
| `BodyAids.Crutch` capacity (hop 250 -> 550) | exists |
| Businesses, staff, the labour market, the Ledger | Phase 5 (`Production/*`) |
| Crafting recipes and stations (`bind`, woodcraft steps), `HeroPack` | Phase 5 |
| `UtilityBrain`, `ActionKind.Treat` (exists, unscored) | Phase 3/4 |
| Illness (fever, flux) treated by the same physicians | `disease-and-plague.md` |
New: content `content/base/medicine/` (five files), `Sim/Medicine/PhysicianBook`, `Sim/Medicine/TreatDesk` (hero commands), `Sim/Medicine/AbstractPhysic`; Godot `UI/TreatmentScreen`. Extended: `MedicineTables` reads content, `UtilityBrain` scores `Treat`, `WoundBook` gains the ischaemia steps. Removed: `StreetView.AutoDress`, `LiveWorld.DressAfterFight`, the setting.

## 3. Data
- **`medicine/treatments.jsonc`**: one row per `TreatKind`: `sim_s` (2, 4, 6, 12, 20, 30, 20, 10, 90, 90, 180, 60, 6), `min_skill` (Stitch 10, Tendon 50, Ligament 60, Organ 60, Amputate 30), `needs` (alternatives in order, as `Treatment.Needs`), `tool_quality` (`sewing` 1 for the four surgeries), `light` (true for surgery). `MedicineTables` and `Treatment` read it at load; **MT-1** proves the loaded values equal today's constants, so every Phase 4 golden is unchanged.
- **`medicine/healing.jsonc`**: the Phase 4 healing table (wounds 3.4) moved out of code, values unchanged.
- **`medicine/relief.jsonc`**: pain relief per dose, points per game hour for 4 game hours: ale -8, poultice -15, poppy milk -40 (good `poppy_milk`, 30 g, 600 mites, trade only in towns with a physician).
- **`medicine/aids.jsonc`**: `crutch` (good, ash or oak pole, 1.4 kg, one hand; walk capacity with one usable leg 550 pm, two crutches with no usable leg 0 and seated), `splint` (exists), `litter` (2 poles + cloth, carried by 2).
- **`medicine/prices.jsonc`** (mites, the service and the physician's materials; seeded for the price solver as a service): visit and look 50, clean 100, stitch 300, set bone 400, splint 150 + the splint, tourniquet 50, tendon 1,200, ligament 1,500, organ 2,000, amputation 800, poppy dose 600, bed-day in his house 60. A barber-surgeon charges 60% and offers only clean, stitch, set bone, splint, tourniquet, amputation.
- **Recipe `make_crutch`** (`crafting/recipes_full.jsonc`): 1 pole (2 kg wood) -> 1 crutch, 1.0 h, `cutting` 1, `carpentry` proficiency optional (no failure below skill 5), at any station or a field kit.
- **`PhysicianRow`** (saved in the optional `medicine` section): `{ Person, Business, Kind (Physician | Barber), Settlement }`. A physician is a person whose business is a `physician` business kind (append to the business kinds; a room `surgery` with a table and a bed: append to room types).
- **Wound record**: one additive key in the wounds section, `TourniquetOnS` (u32 sim-seconds the tourniquet has been on, accumulated), absent while 0.

## 4. Rules

### 4.1 The treatment screen (any patient)
Opened by **Q > Treat** on any person within 2 m who is downed, captive of the hero, kin, a party member, or who consents (a stranger consents when trust >= 0 or he is bleeding faster than 1 ml/s), and by the **Treat** button on the Character screen for the hero himself (one-handed: verbs needing two hands are refused on oneself, except Pressure and Bandage on the other arm's side). The screen shows the paper-doll; each part shows its wounds with state, grade and the live bleed rate (ml/s), and the verbs that apply with: time (sim-s), what it uses (and whether the pack has it), the success chance (Phase 4 formulas, e.g. set bone `clamp(30 + 0.6 x skill - 10 x grade, 5, 95)%`), and the skill floor. **Dress all bleeding** runs `FieldDressing.Dress` (worst rate first, up to 4) as one queued command. Doing a verb is a hero command (`HeroCommandKind.Treat`, appended), performed at the barrier at the second the work ends (`Treatment.Perform` at `start + sim_s`), so blood lost meanwhile is lost; moving more than 1 m, starting a fight or being hit cancels the rest of the queue. Treating someone else trains `Surgery` by the Phase 4 use rule.

### 4.2 Removing the auto-dress
When MT-6 is green: `StreetView.AutoDress`, `LiveWorld.DressAfterFight` and the setting go; an old settings file's key is ignored; the after-fight report's call to action ("2 wounds still bleeding") opens the screen with the worst bleeder selected. `FieldDressing` stays (the planner). `AutoDressTests` become MT-6.

### 4.3 Tourniquet ischaemia (the 2 h and 6 h steps)
`TourniquetOnS` accumulates while a tourniquet is on the limb chain (it does not reset when the tourniquet is loosened and re-tied: no exploit). At **360 sim-s (2 game hours)** every muscle of the limb distal to it drops one grade (bruised -> torn, torn -> mangled) and pain +10; at **1,080 sim-s (6 game hours)** a second drop; at **2,160 sim-s (12 game hours)** the limb is lost (Phase 4 rule: `Mangled`, removal by a surgeon). The paper-doll shows the time left to the next step; warnings at 300 and 1,020 sim-s ("Loosen it or find a surgeon: 1 hour"). Removing it (`ReleasePressure`, 6 sim-s) restores bleeding at the wound's current rate, so the fix is to stitch the vessel first (Stitch on an artery cut cuts the rate to 2 ml/s; Phase 4 4.6).

### 4.4 Physicians and barber-surgeons
- **Where:** one physician per 1,000 people in a town (at least one) and one barber-surgeon in each village of 150+ people. Chosen at the **Phase 6 seeding pass** (overview 7: deterministic from the world seed and person ids, run at a new game and when a v8 Phase 5 save is first loaded): the adult of the settlement with the highest `Surgery`, else the highest `Wits`; his `Surgery` is set to `max(current, 45 + Range(0, 26))` for a physician and `max(current, 25 + Range(0, 16))` for a barber (stream **0x600**, key person id); his occupation becomes `Physician` (appended to `Occupation`) and a `physician` business is founded in his dwelling (Phase 5 founding, his own capital 0, a surgery room recognised by RoomDetector or, if absent, his dwelling's best room counted as one).
- **The hero:** Q on a physician > **Treat me** (or a party member): the quote list (each wound: the treatment he would do by `TreatAi.Next` order, the fee from `prices.jsonc`, his success chance with his skill), pay all or pick lines; the fee goes to his business box (`TransferReason.Fee`, appended); he performs each with his skill, his tools and light (indoors he always has light). A bed-day in his house halves infection risk (Phase 4: bed rest x0.67 healing) and costs 60 mites.
- **NPCs (abstract, `AbstractPhysic`, H4 slice):** a wounded or sick person in a settlement with a physician or barber, whose household holds the fee, is treated within the day with P = 80% (physician) / 60% (barber) (stream 0x600, key person and day, draw 1); the physician does the `TreatAi.Next` sequence on the book at the slice hour and the fee moves. Without one: `AbstractFirstAid` as today.

### 4.5 NPCs who treat (Treat utility scoring)
`ActionKind.Treat` is scored in `UtilityBrain` with `TreatAi.Score(bleedingAlliesNear, firstAidPm)` (allies: kin, household, comrades of the same side, party; within 6 m), x0.5 for a stranger, x0 while the actor's own pain is over 60% of his knock-out threshold; it wins over everything but Flee and Fight. The treatment is `TreatAi.Next` with the actor's own items (`ItemBag` from his store) and skill. Guards and soldiers carry 2 bandages and a rope by kit (content `fight_kits.jsonc`).

### 4.6 Crutches and aids
A crutch in a hand gives the Phase 4 aid capacity (one usable leg: hop 250 -> 550 pm walk). It occupies that hand (no shield, no two-handed weapon); two crutches occupy both. Walking speed scales by the capacity (Phase 4 anatomy 3.6). A litter carries a person who cannot walk at the carriers' speed x0.6.

### 4.7 Tick placement
| Where | What |
|---|---|
| Barrier | `Treat` commands (performed at their finish second), the physician's treatments for the hero, payments |
| Live tick (20 Hz) | NPC `Treat` actions (as Phase 4 medics in fights), the tourniquet clock with the book's own stepping |
| H4 slice | `AbstractPhysic`, `AbstractFirstAid` (unchanged order: wounds at `SystemOrder.Wounds`) |
| Seeding pass | physicians and barbers |

## 5. Determinism
Every outcome uses Phase 4's streams (0x420 infection, 0x421 malunion, 0x422 surgery result) with their keys; **0x600** is physician seeding (key person id, draw 0) and `AbstractPhysic` (key person id, tick = game day, draw 1). Commands are recorded on the replay log like every hero command; `TourniquetOnS` is in `WoundHash`.

## 6. UI (960 x 540)
`TreatmentScreen`: left the paper-doll (2x, 240 x 400), right the selected part's wounds and verbs (one row each: verb, time, uses, chance, fee when a physician does it), bottom bar Dress all bleeding / Patient (Tab cycles patients in reach) / Back. Context `Treatment` (modal, alone with Global; overview 6). Tooltips name the Phase 4 cause chain ("Torn: cut by Bram's axe at the ford, day 12"). Warnings in the HUD's injury line for a tourniquet timer and for an untreated tendon window ("Tendon: a surgeon within 18 hours").

## 7. Tests (golden)
| id | Case | Expected |
|---|---|---|
| MT-1 | Content parity | `treatments.jsonc` and `healing.jsonc` load to the same values as `MedicineTables` and `Treatment` at b5d9234; WB-1 to WB-16 and every Phase 4 golden hash unchanged |
| MT-2 | Hero bandages his own forearm vein cut (15 ml/s) on the street | refused for two-handed verbs; Bandage takes 4 sim-s, the rate drops to 3 ml/s at second 4 (x0.20), one bandage leaves the pack |
| MT-3 | Treat a downed stranger's thigh artery (100 ml/s) with rope | tourniquet done at 6 s; blood lost meanwhile 600 ml; rate x0.02 after |
| MT-4 | Tourniquet steps | muscle grade drops at 360 and 1,080 sim-s, limb lost at 2,160; loosening at 1,000 and re-tying at 1,100 keeps the clock at 1,000 |
| MT-5 | Queue cancel | three verbs queued, the hero walks 2 m after the first: two cancelled, items of the cancelled not used |
| MT-6 | Auto-dress gone | after a fight nothing is dressed by itself; "Dress all bleeding" on the same book gives exactly the steps `AutoDressTests` expected |
| MT-7 | Physician seeding, seed 1234 | Ranunfield (the home town) has one physician per 1,000 people (at least 1) with Surgery in 45..70; every village of 150+ a barber; the same on a fresh new game and on the v8 Phase 5 save of OV-6 loaded |
| MT-8 | Physician quote and treatment: a torn tendon 10 h old | quote lists Tendon repair 1,200 mites, chance `clamp(40 + skill - 50, 40, 95)`; pay; the result uses 0x422; fee in the business box; coin conserved |
| MT-9 | `AbstractPhysic`, 2,000 wounded NPCs, household fee held | 80% +-3 treated within the day where a physician lives, 60% +-3 with a barber |
| MT-10 | `AbstractFirstAid` (the Phase 4 test owed) | 10,000 bleeding persons: 90% +-1.5 dressed with the household home, 40% +-1.5 with neighbours only, 0 alone; at most 4 wounds an hour |
| MT-11 | Treat scoring | a villager with 2 bandages 4 m from his bleeding brother scores Treat 650 and dresses him; a stranger gets 325; a man in heavy pain does not treat |
| MT-12 | `make_crutch` | 1 pole -> 1 crutch in 1.0 h at skill 5; the hero with one leg unusable walks at 550 pm with it, 250 without; a crutch hand cannot hold a shield |
| MT-13 | Save mid-queue and mid-tourniquet | load: the same queue, `TourniquetOnS` and `WoundHash`; save -> load -> save byte-identical |
| MT-14 | 960 x 540 smoke | the screen fits; every verb row readable; keys guarded |

## 8. Open questions (recommended defaults)
1. **Treating strangers without consent:** allowed when he is downed or bleeding over 1 ml/s (a lifesaver), else he refuses. Default as written.
2. **Physician fees:** `prices.jsonc` above (a stitch is two days of a labourer's wage); the price solver may move them +-20% with demand. Default as written.
3. **Tourniquet clock on re-tying:** accumulates, no reset (historically loosening helped; a reset would make the 12-hour loss avoidable for free). Default as written.

## 9. Budgets
Screen open <= 5 ms (the book and the part list); a queued command at the barrier <= 0.05 ms; Treat scoring <= 0.02 ms per candidate (only persons with a bleeding ally in 6 m are scored); `AbstractPhysic` <= 0.5 ms an hour a region; save: physicians 16 B each, `TourniquetOnS` 4 B on the few wounds that have one; content <= 20 KB (Phase 4 budget).

## Notes (P6-A1, medicine sim side)
- Built: `medicine/*.jsonc` (treatments, healing, relief, aids, prices and rules) read by `MedicineRules` (Phase 4 constants are the defaults; `Install` is called from `RegionServices`); `TourniquetOnS` and `DistalMask` on the wound record (additive save keys 32 and 33, hashed only when non-zero), steps at 360, 1,080 and 2,160 sim-s with warnings at 300, 1,020 and 1,860; `ReleasePressure` now loosens a tourniquet and takes 6 sim-s (Phase 4: 0); `PhysicianBook`, `PhysicianSeeding.Run` (the seeding skeleton calls it), `AbstractPhysic` (before `AbstractFirstAid`), `TreatDesk` (quote and paid treatment), `Crutches`, `UtilityBrain.TreatScore`, recipe `make_crutch`, founded-only business kind `physician`.
- TODO at merge with A2: `MedicineEnums` stands in for stream 0x600, `Occupation.Physician`, `TransferReason.Fee`; the `physician` kind's owner occupation is `Labourer` until the enum exists; wire the `medicine` section into `WorldSave` and call `PhysicianSeeding.Run` from the seeding pass; give `PhysicianBook.Plans` the combat content's plan lookup.
- Not done here: guards' kits with bandages (`fight_kits.jsonc`), poppy milk sold only where a physician lives, the surgery room, the live tier filling `ChoiceContext.Treat*`.

## Notes (P6-B1, the treatment screen)
- Built: `HeroCommandKind.Treat` (appended) and `Sim/Medicine/HeroTreat.cs` (`HeroTreatDesk`: `tr:do|patient|kind|part`, `tr:all|patient|n` for "Dress all bleeding" through the `FieldDressing.PlanStep` planner, `tr:cancel`, `tr:phys|man|patient|kind:part,...` for a paid physician through `TreatDesk`); `TreatQueue` (the hero's steps, saved in the optional `medicine` section next to the physicians: `MedicineSave.Queue`, key 1); `TreatView`/`TreatViews` (the paper-doll data: parts, wounds with live bleed and the tourniquet clock, verbs with time, uses, chance, skill floor, refusal, fee, the physician's quote); `MedicineWiring` (the book into the home region's services, the combat content's plan lookup, a loaded save's late seeding) and `PhysicianSeedStep` registered in `Phase6Seeding.DefaultSteps`; Godot `UI/TreatmentScreen` (context Treatment, modal, alone with Global: Up/Down part, Left/Right verb, Enter, D, Tab, Esc), `World/StreetView.Treatment.cs` (Q menu "Treat" for a person within 2 m who is downed, in the party or bleeding over 1 ml/s, "Treat me" beside a physician, "Treat my wounds", the Character screen's Treat button, the after-fight report's "N wounds still bleed" with a button).
- A verb is queued at once at a barrier (`HeroWorld.IssueTreat` applies Treat commands with `HeroDesk.ApplyNow`, not at the end of the game hour) and performed by `HeroTreatDesk.Advance` at the game second its work ends (the game asks each frame while a queue waits; the hour's end asks too); the pack gives up its items only then. The game sends `tr:cancel` when the hero moves more than a metre or a fight begins. Hero commands are recorded like all others (`ReplayKind.Hero`).
- Removed: `LiveWorld.DressAfterFight`, `SessionRecorder.Dress`, `ReplayCommand.Dress`, `StreetView.AutoDress`, `SiteCrawl.AutoDress` (the crawl keeps its own Treat input and the bandages it used still leave the pack through `AftermathDesk.DressCommand`), `Settings.AutoDress` and its row, the `wd_*` keys. An old trace's `ReplayKind.Dress` is read and skipped; an old settings file's `auto_dress` is ignored. `AutoDressTests` became `TreatmentScreenTests` (MT-2, MT-5, MT-6, MT-13 and the replay, ledger and view cases).
- A1's integration items: `RngStreams.Physician`, `Occupation.Physician` and `TransferReason.Fee` replace `MedicineEnums` (deleted); the `physician` business kind's owner occupation is `Physician`; `WorldSave` reads and writes `medicine` (`WorldState.Medicine`); at a new game the seeding step runs at once, at a loaded save without the section `MedicineWiring.Attach` seeds when the home region is built and adds the load note; `PhysicianBook.Plans` is the combat tables' plan lookup. Physicians are seeded for the home region only (other resident regions have none yet).
- The live tier fills `ChoiceContext.Treat*` (`PeopleBridge.TreatNear`: bleeding allies by household or party, strangers, first aid from the Surgery level, own pain) and gives first aid (`TreatAi.Next` with a kit, pressure, bandage or tourniquet, performed at the second its work ends; guards carry 2 bandages and a rope from the guard fight kit's new `bandages` and `rope` fields). The hero is no live actor: the game gives the bridge his id and position so his companions can help him. `PersonAnchors` gained `Household` (plus one) and `SurgeryLevel`.
- The Phase 5 trail was re-pinned again: the physicians seated at a new game change the world (seed 1 `B724181ADCA59FF0`, seed 1234 `9BE661FFB1CB5663`); the generated layers did not move.
- Not done: the HUD injury line's tourniquet and tendon-window warnings, the cause chain in wound tooltips, trust at or above 0 and kin as consent (a stranger consents only when downed or bleeding fast), the bed-day, poppy milk sold only where a physician lives, the surgery room, cauterising (needs fire), the litter, and a light check for surgery (the screen always says lit). No test drives the live medic end to end (MT-11 covers the scoring).
