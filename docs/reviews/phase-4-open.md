# Phase 4: open items for the close-out (step 8)

Collected while steps 1 to 7 were built. Step 8 fixes each one or moves it into `docs/reviews/phase-4.md` with a reason and the phase it goes to. This file is deleted at close-out (its content folds into `phase-4.md`), as `phase-3-open.md` was.

## Decided with the user (Nov 2026)

1. **The spear was over-balanced. Undo it.** The plan target is a floor ("a spear beats a sword of equal skill at least 55%", resolver-v2 §8), not a band. The P4-C calibration slowed the spear to 0.50 / 0.60 s (plan §4A.3 says 0.40 / 0.50) to bring its win rate down from 87%.
   - Restore 0.40 / 0.50.
   - Add the real counter: a swordsman who parries the point or slips past it closes **inside the spear's reach**, and the spearman must step back to use it. This builds on the existing crowded-polearm penalty inside 1.3 m.
   - Keep only the ≥ 55% floor, and record the win rate the physics then gives.
2. **Weapon × armour matrix (a realism check, not equalising).** Run every melee weapon and every missile against: no armour, padded, leather, mail, mail over padding, and full plate (with and without a called shot to a gap). Expected:
   - swords and axes against mail mostly bruise rather than cut;
   - maces, hammers and mauls still hurt through plate;
   - daggers work against plate only through the gaps;
   - arrows mostly fail against plate;
   - crossbow quarrels get through mail at close range;
   - javelins mostly stick in shields;
   - against an unarmoured man, everything is deadly.

   Where a result breaks one of these, fix the material or shape data, not a per-weapon fudge factor. Weapons are allowed to be better or worse than each other.
3. **Ranged realism runs.** Add bow, crossbow and javelin pairings to `tools/Ravensguard.Calibrate`, recorded in the melee-and-ranged Notes. They are judged against the expectations above, not tuned for parity.

4. **Blunt weapons should damage rigid armour more than edges do.** At the moment `ClothingArmour.ApplyWear` wears plate 5 pm on a cut and less on a crush, the wrong way round for maces and hammers.
   - A heavy blunt blow should dent or buckle plate, and protection falls with condition.
   - A heavy blow can burst mail rings or break a plate's straps, which leaves a gap.
   - Show the damage in the flavour log ("the breastplate buckles") and on the paper doll.

   Check the new values against the item 2 matrix.

## Found during steps 1 to 5 (see each spec's Notes for detail)

- **Mounted reach:** mounted non-lance weapons get +0.4 m against riders too; the spec says +0 there.
- **Mace strength:** the mace beats a padded swordsman 69%. Re-check after item 2; this may be fine.
- **Plate duels:** plate against plate lasts 21 s (target 10 to 20 s, test allows 23 s).
- **Wounds content:** `medicine/*.jsonc` content, the `make_crutch` recipe, Treat utility scoring, tourniquet grade drops at 2 and 6 h, physician prices, the paper-doll treatment UI, an `AbstractFirstAid` test.
- **Flavour:**
  - no slow motion on a hero parry-riposte kill;
  - the larger-parry-window and hold-or-toggle accessibility options;
  - blood decals do not persist or dry;
  - the after-fight report's next-step buttons;
  - a mid-fight load does not rebuild the fight view; **Closed (step 8):** the "fight" section's record is restored through the recorder and the stance view reopens on it, paused (`ResumeFight`); a site fight comes back with its crawl.
  - the English draft needs a human read.
- **Morale, capture and succession:**
  - no morale bar under nameplates;
  - the captors' camp is not generated in the live area;
  - the rescue control switch is not wired in the street view; **Closed (step 8):** "Take control of {companion}" on the captivity screen (`mc:control`), the street view is the companion's, E near the camp runs `RescueRun` at the barrier (`mc:rescue`), control comes back either way.
  - hero death shows the succession order but does not switch to the heir or settle the estate in game; **Closed (step 8):** the hero is linked to a household at a new game (`HeroKin`, kin in the "succession" section); death opens the succession screen, `SuccessionDesk` settles the estate (items to an estate owner where the hero fell, coin less the 5% funeral) and makes the heir the hero; Ironman takes the first heir, the run ends only with nobody left.
  - `CaptiveHash` and `EstateHash` are not in `WorldTool hashes`; **Closed (step 8):** a "captive ... estate ..." line per seed, pinned in `GoldenHashes`.
  - morale neighbour checks are O(n);
  - prisoners are not fed.
- **Mounted:** not built yet:
  - mounting and dismounting in a fight;
  - the jousting approach angle;
  - horse kicks;
  - mounted bandit leaders;
  - fire panic;
  - fog;
  - branches in woods;
  - drowning;
  - the horse wound diagram.
- **Watch mode:**
  - followers do not join road fights in play; *moved to Phase 7 (see below)*;
  - the R order wheel is not wired; *moved to Phase 7 (see below)*;
  - autopilot road encounters do not use the odds rule in play;
  - settings rows (take-back, speed cap, which kinds pause);
  - about 95 `wm_` strings against a budget of 40.
- **Content:** no obsidian or flint blades (the 10x-sharpness case) yet.

## Found during steps 6 and 7

- **Abstract battles: AB-3 is only partly met.** 17 of 45 comparisons on fresh seeds are outside ±10%. The worst is the flanked variant: the abstract side wins 96%, live 41%.
  - Causes: the abstract model has no battlefield geometry (live raiders swamp a ten-spear line; flankers are only a morale penalty), and lance rows lack the trample and the fall.
  - The fast test is a ratchet: it fails if more than the 17 known misses appear. Plan §6 says a test that cannot fail is a review finding, so either close the gap or record the ratchet and its reason in phase-4.md.
  - Mounted: on mud the abstract gives 0.14 against 0.41 live. The spec's "+60% ±10" is not met; the live charge bonus is +220%.
- **Abstract battles: performance.** The AB-13 Soak test fails: 100 v 100 takes about 5 ms against a 3 ms budget, and 2,000 v 2,000 takes about 125 ms against 45 ms. The template library is 0.9 MB against a 300 KB budget. **Closed for 100 and 200 (step 8):** 100 v 100 5.6 -> 2.5 ms, 200 v 200 7.0 -> 3.5 ms on the dev box, results unchanged (`PinnedHash` and a 2000 v 2000 hash identical): a sim-second's wound losses read once, the rest of a stable sim-second summed, the body reading cached on the battle's book copies. The soak uses `TestBudget`; 2000 v 2000 and the library size moved to Phase 7.
- **The hour barrier is near its budget.** After steps 6 and 7, `Phase2ReviewTests.Daily_regions_are_staggered` has its slowest barrier at 126 to 153 ms against 150 ms; it passes alone and fails under load. Profile the hourly tick (sites daily step, caravan ambushes fought as abstract battles) and win back headroom rather than raising the budget. **Closed (step 8):** the slow barriers were 95 to 135 ms gen-0 collections (the sites and ambushes were idle in that rig); the hourly tick allocated 310 MB a day and a half, now about 80 MB (household shopping, market sessions, needs, member lists, the fronts cache), so a pass usually has no collection: slowest barrier 51 to 63 ms, also in the full suite.
- **Abstract battles, also left:**
  - live fights are not converted at the bubble edge in play (the code and AB-8 test exist);
  - prisoners from abstract battles do not enter captivity;
  - no corpse loot;
  - no in-game Chronicle (H) screen; reports reach the player only as rumours in the Journal.
- **Exploration: loot does not reach the pack.** Crawl loot and the finds from Search and Dig are not moved into the hero's items, and burnt torches are not taken from the pack. This needs a hero command.
  - Search and Dig run on the UI thread under the site book's lock, are not on the replay log, and cost no clock time.
  - **Closed (step 8):** `HeroCommandKind.Site` (`SiteDesk`): the crawl's haul becomes item rows of exactly the mass that left the site, the burnt torches leave the pack; Search and Dig are camp jobs (20 and 40 game minutes at 8x) whose find is rolled at the barrier under the book's lock; all recorded and saved as hero commands. A stash's coin stays a silver thing (no hoard purse is wired yet).
- **Exploration, also left:**
  - weather in combat is only partly wired (aim spread and fog surprise); road fights do not set the weather row from live weather; **Closed (step 8):** the road fight takes the live weather's row (saved in the fight record, in the `FightHash`); the fight reads sigma, vision cap, bow range, dodge cooldown, evade and grip points, speed, stamina regeneration, the heat's heavy-armour costs, no charges and the storm. Site fights are underground (no row). Left: torch, fire, hearing, stealth, superstition, daylight, slip and cavalry-speed fields, read by nobody yet.
  - the game passes only the hero into a crawl, not the party; **Closed (step 8):** followers on Follow (up to three) go down with the hero.
  - the site factory is not set for replays;
  - resting below is only a flag; **Closed (step 8):** resting fast-forwards at 8x until the rest stops.
  - the 8 °C underground temperature is not read by the clothing model; **Closed (step 8):** below ground the body model reads the site's 8 °C still, dry air under rock.
  - site fights use an open arena that ignores walls;
  - the cave beast is drawn as a blob and the draugr as a tinted human rig;
  - heist sites and firedamp are out of scope.

## Moved to Phase 7 (decided with the user)

Army-scale items go to Phase 7 with the battle bubble:
- **Followers in road fights, formations and the R order wheel** (priority 5 of the close-out) were not built in step 8; the party fights only below ground (it goes into a crawl with the hero).
- **The 2,000 v 2,000 abstract battle budget (45 ms)** and the template library's size come back as a Phase 7 test with the battle bubble; AB-13 keeps the 100 v 100 (3 ms) and 200 v 200 (8 ms) budgets the caravan ambushes use.
