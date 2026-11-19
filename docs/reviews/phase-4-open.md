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
  - a mid-fight load does not rebuild the fight view;
  - the English draft needs a human read.
- **Morale, capture and succession:**
  - no morale bar under nameplates;
  - the captors' camp is not generated in the live area;
  - the rescue control switch is not wired in the street view;
  - hero death shows the succession order but does not switch to the heir or settle the estate in game;
  - `CaptiveHash` and `EstateHash` are not in `WorldTool hashes`;
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
  - followers do not join road fights in play;
  - the R order wheel is not wired;
  - autopilot road encounters do not use the odds rule in play;
  - settings rows (take-back, speed cap, which kinds pause);
  - about 95 `wm_` strings against a budget of 40.
- **Content:** no obsidian or flint blades (the 10x-sharpness case) yet.
