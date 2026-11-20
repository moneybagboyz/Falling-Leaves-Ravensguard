# Phase 6: open items for the close-out (P6-G)

Collected while the steps are built. The close-out fixes each item or moves it into `docs/reviews/phase-6.md` with a reason, then deletes this file.

## From step 1
- **A3 standing, hooks that later steps must wire:**
  - `Standing.PriceMites` is not called by the market book yet (RP-4 tests only the function). It needs a merchant-side rebate transfer to keep the ledger exact.
  - The readers for hiring, guards, courts, church and petitions exist but are not hooked in. They belong to B2/C1 (crime and justice), B4/E2 (faith) and C3 (rule).
  - Religion groups are not counted. B4 must supply membership through `IGroupMap`.
  - Nothing calls `StandingSystem.Moved` when a household changes settlement (RP-9).
  - A2 should fold `StandingBook.Hash()` into the daily hash trail.
  - Renown and `Heard` use the best single deed, not the union of everyone who heard any deed.
  - The hero is person 0, which equals `EntityId.None`'s value, so `DeedTable.DoerOf` returns a nullable. This same id clash has caused bugs before (Phase 4 S13); consider giving the hero a non-zero id in a later save step.
- **A4 difficulty, not wired yet:**
  - Bandit band spawns, slaver raids (D1) and the great plague (C4) must read `BanditPct` and the plague accessors when they are built.
  - Fight-only wound books, abstract battle books and resident regions other than the home economy keep the default 100.
  - The Advanced panel's difficulty rows were not rendered at 960x540.
  - For the close-out: run the 100-year population soak at aging pace 1, 2, 3 and 4 (only a one-year 4x check was run).
- **A1 medicine, integration left for B1 (done in P6-B1):**
  - [x] Replace the `MedicineEnums` name lookups with A2's real enums: stream 0x600 `RngStreams.Physician`, `Occupation.Physician`, `TransferReason.Fee`.
  - [x] The `physician` business kind's owner occupation is `Physician`.
  - [x] `WorldSave` reads and writes the `medicine` section (the physicians and the hero's treatment queue).
  - [x] `PhysicianSeeding.Run` is registered as a Phase 6 seeding step (`PhysicianSeedStep`; a loaded save is seeded when the home region is built).
  - [x] `PhysicianBook.Plans` has the combat content's plan lookup (`MedicineWiring.PlanOf`).
  - [x] Guards' kit bandages (`bandages` and `rope` on the guard and soldier fight kits); the live tier fills `ChoiceContext.Treat*` and gives first aid.
  - [ ] Still not done: poppy milk sold only where a physician is; surgery room recognition; physicians for resident regions other than the home region; the HUD injury line's tourniquet and tendon warnings; the cause chain in wound tooltips; trust and kin as consent; the bed-day; a live-medic end-to-end test.
- **B1 treatment screen, auto-dress removed:** [x] `FieldDressing`'s auto-dress call sites, `LiveWorld.DressAfterFight`, `Settings.AutoDress` and its row are gone; an old trace's Dress command is skipped. The crawl below ground keeps its own Treat input and its bandage command (`AftermathDesk.DressCommand`).
- **Merge note (B1):** the Phase 5 trail hash was re-pinned again after the physicians were seated at a new game (seed 1 B724181ADCA59FF0, seed 1234 9BE661FFB1CB5663). B2 and B5 will also touch `GoldenHashes.cs`, `en.csv` (B1's keys replace the `wd_*` rows near line 2788), `WorldSave.cs` and `HeroCommandKind` (`Treat` is appended after `Will`).
- **Merge note:** the Phase 5 trail hash was re-pinned after A1 (seed 1 E94BE59F8BF56007, seed 1234 1ADA8B7E4D87D8C0). Medicine's physicians change healing in the trail's 10 days; the generated layers are unchanged.

## From step 2, part B2 (crime core)
- **Live tier gaps for crime:** no sneak mode (`MoveMode` is Walk, Jog, Sprint), so the HUD eye and the act read the full sight range; no hero verb lifts goods from a stall (the eye shows near a stall prop; `CrimeDesk.Act` is recorded and tested but nothing in the game issues it); the barrow crawl's `TabooRobbed` does not ask who saw (`CrimeSystem.Commit` with `grave_robbing` is tested with an observer at the mouth); the clothes' outer-layer dyes are not read into `ActorLook` yet.
- **C1 hooks:** guards' `Pursue` from `CryResult.Pursuers`, arrest and `Held`, `Bounties.Collect` at a capture, the `Wanted` list in the Journal (`Bounties.OnPerson`), the readers in `Standing` (`AttackedOnSight`, `Watched`, `MercySteps`) for guards and courts, `CrimeBook.Convictions` for sentence steps, mark writes (brand, outlaw) from sentences.
- **C2:** bounties are `OddJobKind.Bounty` jobs; contracts v2 should take them over (their `P0` crime id, `P1` perpetrator, `P2` flags) and the "report at the next contact with a guard" rule (reports run at hour 0 of the next day for now).
- **Calibration:** `NpcTheftPer10k` is 1.5% a day per desperate household (the spec's 0.5% gives a tenth of its volume); decide at close-out with a real soak (the share of households with a hunger grievance in a real run is not measured here). NPC crime moves no goods or coin. Site-pile theft (0.2% a night) not written.
- **Hero id:** the hero is person 0 (`EntityId.None`); crime code carries "unknown perpetrator" as -1 and never tests an id against `None`.

## From step 2, part B4 (faith core)
- **Wired for the later steps:** `ChurchServices.Allows` for the wedding (B3's FL-2) and the Phase 4 funeral's burial in holy ground (only the will's witness is wired); `ChurchServices.SlaveHeldSeason` each season for a slave owner (D1, SL-6); `ChurchServices.MayLendAtInterest`, `CanOfferLoan` and `InterestLenders` for loans (C3, RI-14). The clerical tax, a ruler's gift to push a bishop, heresy, excommunication (the `Excommunicated` mark exists), penance, relic theft, superstition and witch hunts, and the believed miracles of 10a (their stream: 0x655 is free) are E2's.
- **Not built:** abbeys and monks (they append people), the fasting doctrine's demand shift, a religion's Codex page, the Faction screen's bishops, the church standing that follows piety (x10, spec 4.3: standing moves only through deed rows), renown +5 on a pilgrimage, fast-breaking and holy-day-work sins, the live tier's saint days (it knows only the culture's feasts and the weekly holy day), the pilgrim road encounter and escort contract (C2), pardoners and fake relics.
- **Calibration to check at close-out:** the feast larder (+50% relish and ale from the larder on a holy day; run the economy soak and look at relish prices and the diet-poor count); `AttendBasePm` and `AttendPerPietyPm` give 87% attendance; the see's treasury only grows between vacancies (the bishop's stipend is its one outflow); a religion with 5 feasts has room for 7 saints' days (a world with many history saints drops the later ones).
- **Merge note (B4):** the Phase 5 trail hash was re-pinned (seed 1 FAB9AE64CB6C64D4, seed 1234 E4C04A834F586E9B) because the trail's ten days hold a feast. B3 and B5 also append to `HeroCommandKind` (`Faith` is after `Crime`), `WorldSave.cs` (the `Church` and `Religions` properties, the "faith" section), `en.csv` (the `fa_*` and `ch_faith_*` keys sit before the `fl_` block), `types.jsonc`, `deeds.jsonc` and `GoldenHashes.cs`. Shared files touched: `Society/Groups.cs`, `StandingBook.cs`, `StandingSave.cs`, `StandingSystem.cs`, `StandingView.cs`, `Rumours/TalkLink.cs`, `Economy/EconomyState.cs`, `HouseholdNeeds.cs`, `Behaviour/ScheduleBook.cs`, `Live/Schedule.cs`, `Live/RuntimePeople.cs`, `Law/LawWorld.cs`, `Law/CrimeSystem.cs`, `Hero/WillDesk.cs`, `Trade/HeroDesk.cs`, `Worlds/WorldRuntime.cs`, `Worlds/Phase6Seeding.cs`, `content/base/behaviour/schedules.jsonc`.
- **Panels:** `StandingScreen` (A3) puts its scroll in a panel that does not expand vertically, so it shows one empty line at 960 x 540; the Faith panel sets `SizeFlagsVertical = ExpandFill` on its panel (the same one-line fix is due there).
