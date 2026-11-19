# Weapons against armour (Phase 4 close-out, items 2 and 4)

Made with `dotnet run --project tools/Ravensguard.Calibrate -c Release -- --matrix` (400 first blows and 100 series per cell, `Sim/Combat/Melee2/WeaponMatrix.cs`) on the shipped
content. A realism check judged against the expectations the user set (docs/reviews/phase-4.md item 2), not a balance target: weapons may be better or worse than one another.
The test `WeaponArmourTests` pins the directions, not these numbers.

How a cell is measured: a skill-50 man (strength 1,250) strikes a skill-50 man in the given harness on the trunk (`upper_body`), from the front, normal swings (missiles at 10 m; a thrown
javelin or dagger by a strength-1,250 arm). The hit roll is left out: these are blows that land (a called shot at a gap lands at 0.35 of the hit chance). Each cell reads
**cut % / bruise % / momentum reaching the body % / serious wounds per blow / median time to down**:
- *cut*: the blow left a cut wound; *bruise*: only crush wounds; the rest made no wound at all;
- *momentum reaching the body*: the share of the blow's momentum that entered the first tissue layer, after every worn piece;
- *serious*: a bone broken or worse, a mangled muscle, a tendon, ligament, nerve, vessel or organ, the spine, a concussion, a severed part;
- *time to down*: blows a cycle apart (wind-up, strike and recover at skill 50; a bow's nock, draw, aim and loose; a crossbow's span) on one man whose harness wears as the blows
  come in (dents, burst rings, broken straps: `ArmourWear`), until he is unconscious, dying, collapsed or cannot stand, or a minute has passed ('-': most were still up).
- *heater stops*: the share of blows into a guarded heater shield (capacity 200) that it stops outright.

Harnesses: **none** a linen tunic; **padded** tunic and gambeson; **leather** tunic, hardened leather cuirass and bracers; **mail** tunic, iron hauberk and coif; **mail+padding**
the same over a gambeson; **plate** the calibration's full harness (gambeson, hauberk, coif, steel breastplate, backplate, helm, vambraces, gauntlets, cuisses, greaves).

## Expectations and what broke

| Expectation | Before | After |
|---|---|---|
| Swords and axes against mail mostly bruise | held (0% cut, 100% bruise) | holds |
| Maces, hammers and mauls still hurt through plate | the mace downed a plate man in 9.5 s, the hammer in 5.0 s; **the maul did nothing** (19.3 s, worse than a sword's 14.5 s: it only got through the 5% edge of the breastplate) | mace 6.8 s, hammer 3.8 s, maul 8.8 s; 21 to 28% of their momentum reaches the body against 2 to 3% for an edge |
| Daggers work against plate only through the gaps | **broken**: a dagger stab downed a plate man in 2.8 s (a failed point crushed the plate as if it were a hammer pick, at the point's small area) | 5% cut (the breastplate's uncovered edge), 4% of the momentum through; through the armpit 100% cut, 1.98 serious wounds per blow |
| Arrows mostly fail against plate | **broken**: a steel bodkin went through the full harness every time (100% cut, down in 3 s) | bodkin, broadhead and blunt arrows: 0 to 5% cut, most still up after a minute |
| Crossbow quarrels get through mail at close range | held (100% cut) | holds (100% cut at 10 m) |
| Javelins mostly stick in shields | held (a heater stops every thrown javelin) | holds |
| Against an unarmoured man, everything is deadly | **broken**: the club, the quarterstaff's strike and the blunt arrow did nothing to a man in a linen tunic (they bounced off the cloth) | every row downs him, the slowest the blunt arrow in 9 s |

The fixes (no per-weapon factor anywhere):
1. **Physics: only a rigid shell bounces a blow.** The DF light-weapon deflection (`2 x volume x IY < A x density`) was tested against every worn layer, so a wooden club bounced
   off a linen shirt and a hauberk. Cloth and mail give with a blow; it now applies only to rigid pieces (metal, bone or wood, neither woven nor elastic: plate, a helm, lamellar).
2. **Physics: a failed edge or point skids off a rigid shell.** An edge or point that fails a rigid piece's cut need was tested blunt at its own tiny contact area, so a dagger, a
   sword's thrust or a spear "crushed" a breastplate like a war hammer's face. It now meets the strain rule (the plate soaks 98 to 99.6% of it); a blunt blow is unchanged.
   Table 4.4's spear-against-breastplate row changes accordingly (RS-8b: the point now skids, nothing goes on).
3. **Physics: a breastplate is not met from behind, nor a backplate from the front** (new `side` on armour shapes; from the flank both). A frontal blow used to meet the backplate too
   (45% of the time); the backplate's coverage is now 0.9 of the trunk's back (was 0.45 of the whole trunk).
4. **Shape data: the maul's velocity 0.9 -> 1.7.** DF's momentum formula all but ignores a weapon's mass (a 5 kg sledge and a 2 kg mace differ only by the velocity multiplier), so the
   two-handed maul, at 0.9 against the mace's 1.5, hit softer than a mace and could not crush a breastplate's need at its 25 cm2 face. At 1.7 it hits a little harder than a mace,
   more slowly (0.9 / 0.9 s), and a full swing overwhelms a heater (its 220 units against the shield's 200).
5. **Material and shape data: bodkin and quarrel heads are iron, the bodkin's contact area 3 (was 2).** A steel needle of 2 cm2 met a cut need of 48 units in steel plate against its
   118; medieval arrow and bolt heads were forged iron (sometimes steel-faced) and a bodkin's head is about the quarrel's size. Iron at area 3 needs 145 units against steel plate
   (fails at any range) and 60 against iron mail (gets through at any range the drag allows).

Left as they are, and why: swords on mail down a man about as fast as on cloth (mail turns the cut into a blunt blow at 95%, rule RS-10: "mail does not soften a blow"), so mail
without padding helps little against a sword's shock, as the expectation allows; a padded gambeson does little against an edge (woven cloth, DF numbers); the hand-held javelin
thrust just cuts a breastplate at skill 50 (145 units against its 144) and goes on as a bruise; wooden weapons still bounce off plate (RS-11); the mounted trample (a 800 cm3
"bone" weapon) still bounces off a breastplate by the same rule, which is wrong for a horse and is mounted combat's to fix.

## On the trunk

| weapon | cycle s | none | padded | leather | mail | mail+padding | plate | heater stops |
|---|---|---|---|---|---|---|---|---|
| dagger stab | 0.7 | 100 / 0 / 95 / 1.98 / 0.7 | 100 / 0 / 90 / 1.98 / 0.7 | 100 / 0 / 90 / 1.98 / 0.7 | 100 / 0 / 70 / 1.98 / 0.7 | 100 / 0 / 66 / 1.98 / 0.7 | 5 / 94 / 4 / 0.11 / 7.7 | 100% |
| dagger slash | 0.7 | 100 / 0 / 94 / 0.75 / 0.7 | 100 / 0 / 90 / 1.12 / 1.4 | 100 / 0 / 90 / 1.09 / 1.4 | 100 / 0 / 63 / 1.02 / 1.4 | 100 / 0 / 60 / 1.02 / 1.4 | 5 / 94 / 3 / 0.06 / 7.7 | 100% |
| arming_sword slash | 0.9 | 100 / 0 / 93 / 0.03 / 3.4 | 100 / 0 / 87 / 0.03 / 3.4 | 100 / 0 / 88 / 0.03 / 3.4 | 0 / 100 / 84 / 0.00 / 2.6 | 0 / 100 / 44 / 0.00 / 3.4 | 0 / 100 / 2 / 0.00 / 12.8 | 100% |
| arming_sword stab | 0.9 | 100 / 0 / 94 / 1.99 / 0.9 | 100 / 0 / 90 / 1.99 / 0.9 | 100 / 0 / 90 / 1.99 / 0.9 | 100 / 0 / 63 / 0.32 / 1.7 | 100 / 0 / 60 / 0.32 / 1.7 | 5 / 94 / 3 / 0.02 / 9.4 | 100% |
| falchion chop | 1.0 | 100 / 0 / 93 / 0.03 / 3.8 | 100 / 0 / 86 / 0.03 / 3.8 | 100 / 0 / 86 / 0.03 / 3.8 | 0 / 100 / 84 / 0.00 / 2.9 | 0 / 100 / 44 / 0.00 / 3.8 | 0 / 100 / 2 / 0.00 / 14.3 | 100% |
| longsword slash | 1.0 | 100 / 0 / 93 / 0.03 / 3.8 | 100 / 0 / 87 / 0.03 / 3.8 | 100 / 0 / 88 / 0.03 / 3.8 | 0 / 100 / 84 / 0.00 / 2.9 | 0 / 100 / 44 / 0.00 / 3.8 | 0 / 100 / 2 / 0.00 / 14.3 | 100% |
| longsword stab | 1.0 | 100 / 0 / 94 / 1.99 / 1.0 | 100 / 0 / 90 / 1.99 / 1.0 | 100 / 0 / 90 / 1.99 / 1.0 | 100 / 0 / 63 / 0.32 / 1.9 | 100 / 0 / 60 / 0.32 / 1.9 | 5 / 94 / 3 / 0.02 / 10.5 | 100% |
| hand_axe hack | 0.9 | 100 / 0 / 93 / 0.03 / 3.6 | 100 / 0 / 87 / 0.03 / 3.6 | 100 / 0 / 87 / 0.03 / 3.6 | 0 / 100 / 84 / 0.00 / 2.7 | 0 / 100 / 44 / 0.00 / 3.6 | 0 / 100 / 2 / 0.00 / 13.5 | 100% |
| axe hack | 1.3 | 100 / 0 / 91 / 0.03 / 5.0 | 100 / 0 / 83 / 0.03 / 5.0 | 100 / 0 / 84 / 0.03 / 5.0 | 0 / 100 / 84 / 0.00 / 5.0 | 0 / 100 / 44 / 0.00 / 5.0 | 0 / 100 / 2 / 0.00 / 20.0 | 100% |
| great_axe hack | 1.6 | 100 / 0 / 91 / 0.03 / 6.4 | 100 / 0 / 83 / 0.03 / 6.4 | 100 / 0 / 84 / 0.03 / 6.4 | 0 / 100 / 89 / 0.00 / 6.4 | 0 / 100 / 46 / 0.00 / 6.4 | 0 / 100 / 3 / 0.00 / 25.6 | 100% |
| mace bash | 1.4 | 0 / 100 / 89 / 2.12 / 1.4 | 0 / 100 / 46 / 0.00 / 4.1 | 0 / 100 / 75 / 1.11 / 2.7 | 0 / 100 / 84 / 2.12 / 1.4 | 0 / 100 / 44 / 0.00 / 4.1 | 0 / 100 / 21 / 0.00 / 6.8 | 100% |
| war_hammer strike | 1.3 | 0 / 100 / 89 / 1.09 / 2.5 | 0 / 100 / 46 / 0.00 / 3.8 | 0 / 100 / 75 / 1.09 / 2.5 | 0 / 100 / 84 / 1.09 / 2.5 | 0 / 100 / 44 / 0.00 / 3.8 | 0 / 100 / 28 / 0.00 / 3.8 | 100% |
| war_hammer spike | 1.3 | 100 / 0 / 94 / 1.98 / 1.3 | 100 / 0 / 90 / 1.98 / 1.3 | 100 / 0 / 90 / 1.98 / 1.3 | 100 / 0 / 69 / 1.98 / 1.3 | 100 / 0 / 66 / 1.98 / 1.3 | 5 / 94 / 4 / 0.11 / 13.8 | 100% |
| flail swing | 1.4 | 0 / 100 / 89 / 1.11 / 2.8 | 0 / 100 / 46 / 0.00 / 4.2 | 0 / 100 / 75 / 0.11 / 4.2 | 0 / 100 / 84 / 1.00 / 2.8 | 0 / 100 / 44 / 0.00 / 4.2 | 0 / 100 / 3 / 0.00 / 8.4 | 100% |
| club bash | 1.1 | 0 / 100 / 89 / 0.00 / 4.2 | 0 / 100 / 46 / 0.00 / 7.4 | 0 / 100 / 75 / 0.00 / 4.2 | 0 / 100 / 89 / 0.00 / 4.2 | 0 / 100 / 46 / 0.00 / 7.4 | 0 / 5 / 2 / 0.00 / - | 100% |
| maul smash | 1.8 | 0 / 100 / 89 / 2.13 / 1.8 | 0 / 100 / 46 / 0.00 / 5.3 | 0 / 100 / 75 / 1.11 / 3.5 | 0 / 100 / 84 / 2.13 / 1.8 | 0 / 100 / 44 / 0.00 / 5.3 | 0 / 100 / 21 / 0.00 / 8.8 | 0% |
| quarterstaff strike | 0.8 | 0 / 100 / 89 / 0.00 / 2.4 | 0 / 100 / 46 / 0.00 / 5.6 | 0 / 100 / 75 / 0.00 / 3.2 | 0 / 100 / 84 / 0.00 / 2.4 | 0 / 100 / 44 / 0.00 / 5.6 | 0 / 5 / 2 / 0.00 / - | 100% |
| quarterstaff jab | 0.8 | 0 / 100 / 89 / 1.10 / 1.6 | 0 / 100 / 46 / 0.00 / 3.2 | 0 / 100 / 75 / 1.10 / 1.6 | 0 / 100 / 84 / 1.10 / 1.6 | 0 / 100 / 44 / 0.00 / 3.2 | 0 / 5 / 2 / 0.00 / - | 100% |
| javelin thrust | 0.9 | 100 / 0 / 95 / 1.98 / 0.9 | 100 / 0 / 90 / 1.98 / 0.9 | 100 / 0 / 90 / 1.98 / 0.9 | 100 / 0 / 70 / 1.98 / 0.9 | 100 / 0 / 67 / 1.98 / 0.9 | 5 / 94 / 20 / 0.11 / 6.3 | 100% |
| spear thrust | 0.9 | 100 / 0 / 94 / 1.99 / 0.9 | 100 / 0 / 90 / 1.99 / 0.9 | 100 / 0 / 90 / 1.99 / 0.9 | 100 / 0 / 69 / 1.99 / 0.9 | 100 / 0 / 66 / 1.99 / 0.9 | 5 / 94 / 4 / 0.11 / 9.9 | 100% |
| billhook hook | 1.3 | 100 / 0 / 94 / 0.03 / 3.9 | 100 / 0 / 89 / 0.03 / 5.2 | 100 / 0 / 89 / 0.03 / 5.2 | 100 / 0 / 39 / 0.03 / 5.2 | 100 / 0 / 33 / 0.03 / 5.2 | 5 / 94 / 2 / 0.00 / 20.8 | 100% |
| billhook thrust | 1.3 | 100 / 0 / 94 / 1.99 / 1.3 | 100 / 0 / 90 / 1.99 / 1.3 | 100 / 0 / 90 / 1.99 / 1.3 | 100 / 0 / 67 / 1.99 / 1.3 | 100 / 0 / 64 / 1.99 / 1.3 | 5 / 94 / 4 / 0.11 / 14.3 | 100% |
| pike thrust | 1.5 | 100 / 0 / 94 / 1.99 / 1.5 | 100 / 0 / 90 / 1.99 / 1.5 | 100 / 0 / 90 / 1.99 / 1.5 | 100 / 0 / 70 / 1.99 / 1.5 | 100 / 0 / 67 / 1.99 / 1.5 | 5 / 94 / 4 / 0.11 / 12.0 | 100% |
| longbow bodkin | 3.0 | 100 / 0 / 94 / 1.96 / 3.0 | 100 / 0 / 90 / 1.96 / 3.0 | 100 / 0 / 90 / 1.96 / 3.0 | 100 / 0 / 66 / 1.40 / 3.0 | 100 / 0 / 62 / 1.40 / 3.0 | 5 / 0 / 3 / 0.08 / - | 100% |
| longbow broadhead | 3.0 | 100 / 0 / 90 / 0.02 / 9.0 | 100 / 0 / 81 / 0.02 / 9.0 | 100 / 0 / 81 / 0.02 / 9.0 | 0 / 100 / 84 / 1.10 / 6.0 | 0 / 100 / 44 / 0.00 / 9.0 | 0 / 5 / 2 / 0.00 / - | 100% |
| longbow blunt | 3.0 | 0 / 100 / 89 / 0.00 / 9.0 | 0 / 100 / 46 / 0.00 / 24.0 | 0 / 100 / 75 / 0.00 / 9.0 | 0 / 100 / 84 / 0.00 / 9.0 | 0 / 100 / 44 / 0.00 / 24.0 | 0 / 5 / 2 / 0.00 / - | 100% |
| crossbow quarrel | 11.2 | 100 / 0 / 94 / 1.96 / 11.2 | 100 / 0 / 90 / 1.96 / 11.2 | 100 / 0 / 90 / 1.96 / 11.2 | 100 / 0 / 68 / 1.96 / 11.2 | 100 / 0 / 65 / 1.96 / 11.2 | 5 / 0 / 3 / 0.11 / - | 100% |
| thrown javelin | 0.9 | 100 / 0 / 94 / 1.98 / 0.9 | 100 / 0 / 90 / 1.98 / 0.9 | 100 / 0 / 90 / 1.98 / 0.9 | 100 / 0 / 63 / 0.32 / 1.7 | 100 / 0 / 59 / 0.32 / 1.7 | 5 / 94 / 3 / 0.02 / 9.4 | 100% |
| thrown dagger | 0.9 | 100 / 0 / 93 / 0.32 / 2.6 | 100 / 0 / 86 / 0.02 / 4.3 | 100 / 0 / 86 / 0.06 / 4.3 | 0 / 100 / 84 / 0.00 / 3.4 | 0 / 100 / 44 / 0.00 / 6.0 | 0 / 100 / 2 / 0.00 / 13.6 | 100% |

## Called shot through the armpit

A blunt blow cannot slip through a gap (resolver-v2 4.2): its row is the trunk's again.

| weapon | cycle s | none | padded | leather | mail | mail+padding | plate |
|---|---|---|---|---|---|---|---|
| dagger stab | 0.7 | 100 / 0 / 95 / 1.98 / 0.7 | 100 / 0 / 90 / 1.98 / 0.7 | 100 / 0 / 95 / 1.98 / 0.7 | 100 / 0 / 95 / 1.98 / 0.7 | 100 / 0 / 90 / 1.98 / 0.7 | 100 / 0 / 90 / 1.98 / 0.7 |
| dagger slash | 0.7 | 100 / 0 / 94 / 0.75 / 0.7 | 100 / 0 / 90 / 1.12 / 1.4 | 100 / 0 / 94 / 0.75 / 0.7 | 100 / 0 / 94 / 0.75 / 0.7 | 100 / 0 / 90 / 1.12 / 1.4 | 100 / 0 / 90 / 1.12 / 1.4 |
| arming_sword slash | 0.9 | 100 / 0 / 93 / 0.03 / 3.4 | 100 / 0 / 87 / 0.03 / 3.4 | 100 / 0 / 93 / 0.03 / 3.4 | 100 / 0 / 93 / 0.03 / 3.4 | 100 / 0 / 87 / 0.03 / 3.4 | 100 / 0 / 87 / 0.03 / 3.4 |
| arming_sword stab | 0.9 | 100 / 0 / 94 / 1.99 / 0.9 | 100 / 0 / 90 / 1.99 / 0.9 | 100 / 0 / 94 / 1.99 / 0.9 | 100 / 0 / 94 / 1.99 / 0.9 | 100 / 0 / 90 / 1.99 / 0.9 | 100 / 0 / 90 / 1.99 / 0.9 |
| falchion chop | 1.0 | 100 / 0 / 93 / 0.03 / 3.8 | 100 / 0 / 86 / 0.03 / 3.8 | 100 / 0 / 93 / 0.03 / 3.8 | 100 / 0 / 93 / 0.03 / 3.8 | 100 / 0 / 86 / 0.03 / 3.8 | 100 / 0 / 86 / 0.03 / 3.8 |
| longsword slash | 1.0 | 100 / 0 / 93 / 0.03 / 3.8 | 100 / 0 / 87 / 0.03 / 3.8 | 100 / 0 / 93 / 0.03 / 3.8 | 100 / 0 / 93 / 0.03 / 3.8 | 100 / 0 / 87 / 0.03 / 3.8 | 100 / 0 / 87 / 0.03 / 3.8 |
| longsword stab | 1.0 | 100 / 0 / 94 / 1.99 / 1.0 | 100 / 0 / 90 / 1.99 / 1.0 | 100 / 0 / 94 / 1.99 / 1.0 | 100 / 0 / 94 / 1.99 / 1.0 | 100 / 0 / 90 / 1.99 / 1.0 | 100 / 0 / 90 / 1.99 / 1.0 |
| hand_axe hack | 0.9 | 100 / 0 / 93 / 0.03 / 3.6 | 100 / 0 / 87 / 0.03 / 3.6 | 100 / 0 / 93 / 0.03 / 3.6 | 100 / 0 / 93 / 0.03 / 3.6 | 100 / 0 / 87 / 0.03 / 3.6 | 100 / 0 / 87 / 0.03 / 3.6 |
| axe hack | 1.3 | 100 / 0 / 91 / 0.03 / 5.0 | 100 / 0 / 83 / 0.03 / 5.0 | 100 / 0 / 91 / 0.03 / 5.0 | 100 / 0 / 91 / 0.03 / 5.0 | 100 / 0 / 83 / 0.03 / 5.0 | 100 / 0 / 83 / 0.03 / 5.0 |
| great_axe hack | 1.6 | 100 / 0 / 91 / 0.03 / 6.4 | 100 / 0 / 83 / 0.03 / 6.4 | 100 / 0 / 91 / 0.03 / 6.4 | 100 / 0 / 91 / 0.03 / 6.4 | 100 / 0 / 83 / 0.03 / 6.4 | 100 / 0 / 83 / 0.03 / 6.4 |
| mace bash | 1.4 | 0 / 100 / 89 / 2.12 / 1.4 | 0 / 100 / 46 / 0.00 / 4.1 | 0 / 100 / 75 / 1.11 / 2.7 | 0 / 100 / 84 / 2.12 / 1.4 | 0 / 100 / 44 / 0.00 / 4.1 | 0 / 100 / 21 / 0.00 / 6.8 |
| war_hammer strike | 1.3 | 0 / 100 / 89 / 1.09 / 2.5 | 0 / 100 / 46 / 0.00 / 3.8 | 0 / 100 / 75 / 1.09 / 2.5 | 0 / 100 / 84 / 1.09 / 2.5 | 0 / 100 / 44 / 0.00 / 3.8 | 0 / 100 / 28 / 0.00 / 3.8 |
| war_hammer spike | 1.3 | 100 / 0 / 94 / 1.98 / 1.3 | 100 / 0 / 90 / 1.98 / 1.3 | 100 / 0 / 94 / 1.98 / 1.3 | 100 / 0 / 94 / 1.98 / 1.3 | 100 / 0 / 90 / 1.98 / 1.3 | 100 / 0 / 90 / 1.98 / 1.3 |
| flail swing | 1.4 | 0 / 100 / 89 / 1.11 / 2.8 | 0 / 100 / 46 / 0.00 / 4.2 | 0 / 100 / 75 / 0.11 / 4.2 | 0 / 100 / 84 / 1.00 / 2.8 | 0 / 100 / 44 / 0.00 / 4.2 | 0 / 100 / 3 / 0.00 / 8.4 |
| club bash | 1.1 | 0 / 100 / 89 / 0.00 / 4.2 | 0 / 100 / 46 / 0.00 / 7.4 | 0 / 100 / 75 / 0.00 / 4.2 | 0 / 100 / 89 / 0.00 / 4.2 | 0 / 100 / 46 / 0.00 / 7.4 | 0 / 5 / 2 / 0.00 / - |
| maul smash | 1.8 | 0 / 100 / 89 / 2.13 / 1.8 | 0 / 100 / 46 / 0.00 / 5.3 | 0 / 100 / 75 / 1.11 / 3.5 | 0 / 100 / 84 / 2.13 / 1.8 | 0 / 100 / 44 / 0.00 / 5.3 | 0 / 100 / 21 / 0.00 / 8.8 |
| quarterstaff strike | 0.8 | 0 / 100 / 89 / 0.00 / 2.4 | 0 / 100 / 46 / 0.00 / 5.6 | 0 / 100 / 75 / 0.00 / 3.2 | 0 / 100 / 84 / 0.00 / 2.4 | 0 / 100 / 44 / 0.00 / 5.6 | 0 / 5 / 2 / 0.00 / - |
| quarterstaff jab | 0.8 | 0 / 100 / 89 / 1.10 / 1.6 | 0 / 100 / 46 / 0.00 / 3.2 | 0 / 100 / 75 / 1.10 / 1.6 | 0 / 100 / 84 / 1.10 / 1.6 | 0 / 100 / 44 / 0.00 / 3.2 | 0 / 5 / 2 / 0.00 / - |
| javelin thrust | 0.9 | 100 / 0 / 95 / 1.98 / 0.9 | 100 / 0 / 90 / 1.98 / 0.9 | 100 / 0 / 95 / 1.98 / 0.9 | 100 / 0 / 95 / 1.98 / 0.9 | 100 / 0 / 90 / 1.98 / 0.9 | 100 / 0 / 90 / 1.98 / 0.9 |
| spear thrust | 0.9 | 100 / 0 / 94 / 1.99 / 0.9 | 100 / 0 / 90 / 1.99 / 0.9 | 100 / 0 / 94 / 1.99 / 0.9 | 100 / 0 / 94 / 1.99 / 0.9 | 100 / 0 / 90 / 1.99 / 0.9 | 100 / 0 / 90 / 1.99 / 0.9 |
| billhook hook | 1.3 | 100 / 0 / 94 / 0.03 / 3.9 | 100 / 0 / 89 / 0.03 / 5.2 | 100 / 0 / 94 / 0.03 / 3.9 | 100 / 0 / 94 / 0.03 / 3.9 | 100 / 0 / 89 / 0.03 / 5.2 | 100 / 0 / 89 / 0.03 / 5.2 |
| billhook thrust | 1.3 | 100 / 0 / 94 / 1.99 / 1.3 | 100 / 0 / 90 / 1.99 / 1.3 | 100 / 0 / 94 / 1.99 / 1.3 | 100 / 0 / 94 / 1.99 / 1.3 | 100 / 0 / 90 / 1.99 / 1.3 | 100 / 0 / 90 / 1.99 / 1.3 |
| pike thrust | 1.5 | 100 / 0 / 94 / 1.99 / 1.5 | 100 / 0 / 90 / 1.99 / 1.5 | 100 / 0 / 94 / 1.99 / 1.5 | 100 / 0 / 94 / 1.99 / 1.5 | 100 / 0 / 90 / 1.99 / 1.5 | 100 / 0 / 90 / 1.99 / 1.5 |
| longbow bodkin | 3.0 | 100 / 0 / 94 / 1.96 / 3.0 | 100 / 0 / 90 / 1.96 / 3.0 | 100 / 0 / 94 / 1.96 / 3.0 | 100 / 0 / 94 / 1.96 / 3.0 | 100 / 0 / 90 / 1.96 / 3.0 | 100 / 0 / 90 / 1.96 / 3.0 |
| longbow broadhead | 3.0 | 100 / 0 / 90 / 0.02 / 9.0 | 100 / 0 / 81 / 0.02 / 9.0 | 100 / 0 / 90 / 0.02 / 9.0 | 100 / 0 / 90 / 0.02 / 9.0 | 100 / 0 / 81 / 0.02 / 9.0 | 100 / 0 / 81 / 0.02 / 9.0 |
| longbow blunt | 3.0 | 0 / 100 / 89 / 0.00 / 9.0 | 0 / 100 / 46 / 0.00 / 24.0 | 0 / 100 / 75 / 0.00 / 9.0 | 0 / 100 / 84 / 0.00 / 9.0 | 0 / 100 / 44 / 0.00 / 24.0 | 0 / 5 / 2 / 0.00 / - |
| crossbow quarrel | 11.2 | 100 / 0 / 94 / 1.96 / 11.2 | 100 / 0 / 90 / 1.96 / 11.2 | 100 / 0 / 94 / 1.96 / 11.2 | 100 / 0 / 94 / 1.96 / 11.2 | 100 / 0 / 90 / 1.96 / 11.2 | 100 / 0 / 90 / 1.96 / 11.2 |
| thrown javelin | 0.9 | 100 / 0 / 94 / 1.98 / 0.9 | 100 / 0 / 90 / 1.98 / 0.9 | 100 / 0 / 94 / 1.98 / 0.9 | 100 / 0 / 94 / 1.98 / 0.9 | 100 / 0 / 90 / 1.98 / 0.9 | 100 / 0 / 90 / 1.98 / 0.9 |
| thrown dagger | 0.9 | 100 / 0 / 93 / 0.32 / 2.6 | 100 / 0 / 86 / 0.02 / 4.3 | 100 / 0 / 93 / 0.32 / 2.6 | 100 / 0 / 93 / 0.32 / 2.6 | 100 / 0 / 86 / 0.02 / 4.3 | 100 / 0 / 86 / 0.02 / 4.3 |

## Blunt damage to rigid armour (item 4)

`ResolverV2` now writes, for each worn piece a blow met, a wear and a harm (`LayerStep.Harm`): a blunt blow (not a failed edge) on a rigid shell dents it by `dent_full_pm` (150)
x its momentum over the piece's crush need (capped at twice), *buckled* at the need or more, *dented* when the dent is 40 or more; a failed edge only scuffs it (3 pm, as before);
mail cut through, or struck blunt at twice its crush need, *bursts* its rings at that zone (`BreachMask`: the piece covers 30% less there); a shell battered below 300 condition
breaks a *strap* and hangs loose (`Slipped`: half its coverage everywhere). Condition feeds the existing multiplier `Cm = 0.5 + cond / 2`, so a dented plate needs less to crush
it. The fight applies it to the fighter's pieces (`ArmourWear.Apply`, in the fight hash and the optional `Armour` key of the fight record), tells it in the flavour log
("{target}'s breastplate buckles under the blow", `after.broke.armour.*`) and the fight log, and the called-shot doll shades a worn piece by its condition and strikes it through.
Under a mace the full harness's breastplate goes from 1,000 to under 300 in five blows and slips; under a sword it loses 3 pm a blow. `ClothingArmour.ApplyWear` writes the
same condition into a clothing book's items (burst rings and straps live in the fight only).

## Ranged realism runs (item 3)

`-- --ranged --duels 2000`: archers (a full quiver, padded) and crossbowmen (spanned, 20 quarrels) 30 m from a swordsman, 40 m from each other; a skirmisher with a sword and three
javelins 20 m out. Judged against the expectations above, not tuned for parity.

| Pairing | n | mean s | defended | A wins | B wins | severs/duel | fatal | KO'd then bled out (untreated) | (with first aid) |
|---|---|---|---|---|---|---|---|---|---|
| bow (bodkin) vs sword (padded) | 2000 | 8.9 | 0% | 100% | 0% | 0.00 | 42% | 37% | 33% |
| bow (broadhead) vs sword (unarmoured) | 2000 | 14.6 | 1% | 95% | 5% | 0.00 | 7% | 7% | 1% |
| bow (bodkin) vs sword (mail) | 2000 | 8.4 | 0% | 100% | 0% | 0.00 | 15% | 14% | 11% |
| bow (bodkin) vs sword (plate) | 2000 | 24.2 | 3% | 17% | 82% | 0.00 | 35% | 38% | 7% |
| bow (bodkin) vs sword+heater (padded) | 2000 | 8.9 | 0% | 100% | 0% | 0.00 | 42% | 37% | 33% |
| bow (bodkin) vs sword+heater, shield raised | 2000 | 25.6 | 18% | 99% | 0% | 0.00 | 45% | 33% | 25% |
| crossbow vs sword (padded) | 2000 | 18.0 | 0% | 55% | 45% | 0.00 | 49% | 49% | 29% |
| crossbow vs sword (mail) | 2000 | 18.0 | 0% | 55% | 45% | 0.00 | 46% | 47% | 28% |
| javelins vs sword (padded) | 2000 | 6.9 | 37% | 88% | 12% | 0.00 | 44% | 35% | 16% |
| javelins vs sword+heater (padded) | 2000 | 7.9 | 43% | 82% | 17% | 0.00 | 45% | 36% | 16% |
| bow vs bow (padded) | 2000 | 45.1 | 0% | 40% | 40% | 0.00 | 42% | 32% | 15% |
| bow vs crossbow (padded) | 2000 | 58.1 | 0% | 53% | 46% | 0.00 | 50% | 37% | 26% |
| crossbow vs crossbow (padded) | 2000 | 130.8 | 0% | 37% | 35% | 0.00 | 40% | 41% | 22% |
| bow vs javelins (padded) | 2000 | 9.4 | 0% | 80% | 20% | 0.00 | 48% | 38% | 32% |
| crossbow vs javelins (padded) | 2000 | 15.3 | 0% | 43% | 56% | 0.00 | 49% | 44% | 27% |

What it shows: a lone swordsman crossing 30 m of open ground to an archer who backs off and keeps shooting dies, mail or not (bodkins go through mail), unless he wears plate
(the archer then wins 17%); a raised heater (the defensive stance) catches 45% of the arrows but halves his pace, and he still loses. A crossbowman gets one spanned shot and then
fights with the stock: 55% against a swordsman, mail no help. Javelins win 82 to 88% at 20 m, their three throws before the swords meet (the balanced stance does not raise its
shield; one that does stops them, as the matrix's heater column shows). Archer against archer and crossbow against crossbow are even; the bow's rate and the crossbow's first
spanned shot and heavier bolt come out level at 40 m (53 against 46%).

Two AI faults the runs found and fixed: an archer backing off to keep his distance never nocked (he outran the swordsman at the same sprint and the fight was walked away after
15 s, a quarter of the bow duels); he now nocks as he goes. A crossbowman backed off too, which cancels the windlass, so he never spanned at all; he now stands and spans, and a
crossbow is carried into a fight spanned.
